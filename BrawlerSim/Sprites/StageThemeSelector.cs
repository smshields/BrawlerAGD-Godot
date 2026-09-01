using System.Text.Json;
using System.Text.Json.Serialization;
using BrawlerSim.Genome;
using NameGen;
using NameGen.Data;
using NameGen.Features;
using NameGen.Traits;
using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Sprites;

/// <summary>A scored theme candidate, ordered by the seeded softmax sample.</summary>
public sealed record ThemeCandidate(ThemeDef Theme, double Score);

/// <summary>The negotiated presentation of one stage: theme, display name, and the
/// shared register that keeps them coherent — "The Sunken Cathedral" gets
/// church/marble tiles by construction (M4d, stage-tile-selection.md).</summary>
public sealed record ThemePresentation(string ThemeId, string DisplayName, string Register);

/// <summary>
/// Semantic stage theme selection (M4d, 2026-09-01 —
/// docs/features/stage-tile-selection.md): the SpriteSelector architecture applied to
/// stages. Deterministic — same stage genome + seed = same (theme, name, register)
/// everywhere; the namegen Pcg32 on a theme-private sequence, so selection never
/// perturbs the naming stream. Uses namegen's STAGE side (ExtractStage + the stage
/// trait table) exactly as stage naming does, and picks the register through the same
/// weighted register data, so tile theme and stage-name register can never disagree.
/// ONE theme per stage (platform coherence beats variety within a single arena).
/// </summary>
public sealed class StageThemeSelector
{
    /// <summary>Private Pcg32 sequence for theme draws — independent of the naming
    /// stream and of every sprite sequence, even under related seeds.</summary>
    private const ulong ThemeSequence = 0x5354475448454d45UL; // "STGTHEME"

    private readonly NameGenData _data;
    private readonly FeatureExtractor _extractor;

    public StageThemeLibrary Library { get; }
    public StageThemeConfig Config { get; }

    public StageThemeSelector(StageThemeLibrary library, StageThemeConfig? config = null,
        NameGenData? data = null)
    {
        Library = library;
        Config = config ?? StageThemeConfig.Default;
        _data = data ?? NameGenData.LoadEmbedded();
        _extractor = new FeatureExtractor(_data.Ranges);
    }

    /// <summary>The genome mapping — identical to what the naming pass feeds
    /// GenerateStageName (BuiltGamePresentation's convention).</summary>
    public static NameGen.StageGenome Map(Genome.StageGenome stage) =>
        new(stage.Params.ToDictionary());

    /// <summary>Salient stage traits exactly as naming derives them.</summary>
    public IReadOnlyList<SalientTrait> Salient(Genome.StageGenome stage)
    {
        FeatureVector features = _extractor.ExtractStage(Map(stage));
        return TraitScorer.SelectSalient(features, _data.Traits.Stage,
            _data.Traits.SalienceTopK, _data.Traits.SalienceThreshold);
    }

    /// <summary>Raw trait-affinity score (the repair metric): sum over salient stage
    /// traits of salience × the theme's affinity for that trait.</summary>
    public static double AffinityScore(ThemeDef theme, IReadOnlyList<SalientTrait> salient)
    {
        double score = 0;
        foreach (SalientTrait trait in salient)
        {
            if (theme.TraitAffinity.TryGetValue(trait.Name, out float affinity))
            {
                score += trait.Score * affinity;
            }
        }
        return score;
    }

    /// <summary>Repair rule (the SpriteId pattern): a theme needs re-resolving when
    /// it is missing/unknown, or when the stage HAS salient traits and the theme's
    /// affinity falls below the floor. A neutral stage contradicts nothing.</summary>
    public bool NeedsRepair(Genome.StageGenome stage)
    {
        ThemeDef? theme = Library.ByName(stage.ThemeId);
        if (theme is null)
        {
            return true;
        }
        IReadOnlyList<SalientTrait> salient = Salient(stage);
        return salient.Count > 0 && AffinityScore(theme, salient) < Config.RepairFloor;
    }

    /// <summary>The content-derived seed (the M4 shared-seed doctrine): identical to
    /// the stage NAMING seed, so selection and naming pick the same register.</summary>
    public static ulong ThemeSeed(Genome.StageGenome stage) =>
        Serialization.BuiltGameNaming.NamingSeed(stage);

    /// <summary>The register the shared seed picks — recomputable without sampling.</summary>
    public string PickRegister(ulong seed) => PickRegister(new NgPcg(seed, ThemeSequence));

    /// <summary>Steps 1–5, the SpriteSelector shape: seeded register pick → register
    /// pool (full-library fallback below the floor — the fey register has no themes) →
    /// affinity scoring − per-game overuse → shaping (goofy budget, theme share cap) →
    /// seeded softmax sample of an ordered candidate list.</summary>
    public IReadOnlyList<ThemeCandidate> SelectCandidates(
        Genome.StageGenome stage, ulong seed, out string register,
        IReadOnlyDictionary<string, int>? priorUse = null)
    {
        var rng = new NgPcg(seed, ThemeSequence);
        string picked = PickRegister(rng);
        register = picked;

        var pool = Library.Themes.Where(t => t.Registers.Contains(picked)).ToList();
        if (pool.Count < Config.RegisterPoolFloor)
        {
            pool = Library.Themes.ToList();
        }

        IReadOnlyList<SalientTrait> salient = Salient(stage);
        var scores = new double[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            scores[i] = AffinityScore(pool[i], salient);
            if (priorUse is not null && priorUse.TryGetValue(pool[i].Name, out int uses))
            {
                scores[i] -= Config.OverusePenalty * uses;
            }
        }

        double[] probs = Softmax(scores, Config.SoftmaxTemperature);
        ScaleClass(pool, probs, t => Config.GoofVibes.Contains(t.Vibe), Config.GoofBudget, exact: true);
        CapPerTheme(probs, Config.MaxThemeShare);

        int k = Math.Min(Config.CandidateCount, pool.Count);
        var ordered = new List<ThemeCandidate>(k);
        for (int draw = 0; draw < k; draw++)
        {
            int index = SampleIndex(probs, rng);
            ordered.Add(new ThemeCandidate(pool[index], scores[index]));
            probs[index] = 0;
        }
        return ordered;
    }

    /// <summary>Resolve the ThemeId GENE: the first candidate of the seeded sample.</summary>
    public string ResolveThemeId(Genome.StageGenome stage, ulong seed,
        IReadOnlyDictionary<string, int>? priorUse = null) =>
        SelectCandidates(stage, seed, out _, priorUse)[0].Theme.Name;

    /// <summary>Gene upkeep for the breeding pipeline: assigns a theme to a stage
    /// that needs one (fresh generation, unknown id, or the repair floor fired) using
    /// the content-derived seed, and returns the stage unchanged otherwise. RNG-free
    /// with respect to the evolution stream.</summary>
    public Genome.StageGenome EnsureGene(Genome.StageGenome stage)
    {
        if (!NeedsRepair(stage))
        {
            return stage;
        }
        return stage.WithThemeId(ResolveThemeId(stage, ThemeSeed(stage)));
    }

    /// <summary>The presentation pass (game open / prep-game): settle theme, name,
    /// and their shared register together. The inherited gene wins when it is known,
    /// above the repair floor, and not already worn by an earlier stage in this game
    /// (the roster-distinctness rule); otherwise the seeded top candidate. The name
    /// is generated with the SAME register the theme pool used.</summary>
    public ThemePresentation Present(Genome.StageGenome stage, ulong seed,
        NameGenerator generator, IReadOnlyDictionary<string, int>? priorUse = null,
        string? inheritedThemeId = null)
    {
        IReadOnlyList<ThemeCandidate> candidates =
            SelectCandidates(stage, seed, out string register, priorUse);
        string themeId = inheritedThemeId is not null
            && Library.Contains(inheritedThemeId) && !NeedsRepair(stage.WithThemeId(inheritedThemeId))
                ? inheritedThemeId
                : candidates[0].Theme.Name;
        string name = generator.GenerateStageName(Map(stage), new NameOptions
        {
            Seed = seed,
            Register = register,
        }).Display;
        return new ThemePresentation(themeId, name, register);
    }

    // ── The SpriteSelector primitives, over themes (kept local: the two selectors
    // share a shape, not a base class — their pools and shaping knobs differ). ──────

    private string PickRegister(NgPcg rng)
    {
        double total = 0;
        foreach (RegisterDef def in _data.Registers)
        {
            if (def.Weight > 0)
            {
                total += def.Weight;
            }
        }
        double roll = rng.NextDouble() * total;
        double acc = 0;
        foreach (RegisterDef def in _data.Registers)
        {
            if (def.Weight <= 0)
            {
                continue;
            }
            acc += def.Weight;
            if (roll < acc)
            {
                return def.Name;
            }
        }
        return _data.Registers[_data.Registers.Count - 1].Name;
    }

    private static double[] Softmax(double[] scores, float temperature)
    {
        double max = double.NegativeInfinity;
        foreach (double s in scores)
        {
            max = Math.Max(max, s);
        }
        double t = Math.Max(1e-3, temperature);
        var probs = new double[scores.Length];
        double sum = 0;
        for (int i = 0; i < scores.Length; i++)
        {
            probs[i] = Math.Exp((scores[i] - max) / t);
            sum += probs[i];
        }
        for (int i = 0; i < probs.Length; i++)
        {
            probs[i] /= sum;
        }
        return probs;
    }

    private static void ScaleClass(List<ThemeDef> pool, double[] probs,
        Func<ThemeDef, bool> inClass, float share, bool exact)
    {
        double classMass = 0;
        double otherMass = 0;
        for (int i = 0; i < pool.Count; i++)
        {
            if (inClass(pool[i]))
            {
                classMass += probs[i];
            }
            else
            {
                otherMass += probs[i];
            }
        }
        if (classMass <= 0 || otherMass <= 0)
        {
            return;
        }
        if (!exact && classMass <= share)
        {
            return;
        }
        double classFactor = share / classMass;
        double otherFactor = (1.0 - share) / otherMass;
        for (int i = 0; i < pool.Count; i++)
        {
            probs[i] *= inClass(pool[i]) ? classFactor : otherFactor;
        }
    }

    /// <summary>The no-monopoly water-fill, exactly the SpriteSelector rule.</summary>
    private static void CapPerTheme(double[] probs, float cap)
    {
        if (cap <= 0 || cap * probs.Length <= 1.0)
        {
            return;
        }
        var capped = new bool[probs.Length];
        for (int pass = 0; pass < probs.Length; pass++)
        {
            int cappedCount = 0;
            double freeMass = 0;
            for (int i = 0; i < probs.Length; i++)
            {
                if (capped[i])
                {
                    cappedCount++;
                }
                else
                {
                    freeMass += probs[i];
                }
            }
            double target = 1.0 - cappedCount * (double)cap;
            if (freeMass <= 0 || target <= 0)
            {
                return;
            }
            double scale = target / freeMass;
            bool cappedAnother = false;
            for (int i = 0; i < probs.Length; i++)
            {
                if (capped[i])
                {
                    probs[i] = cap;
                    continue;
                }
                probs[i] *= scale;
                if (probs[i] > cap)
                {
                    probs[i] = cap;
                    capped[i] = true;
                    cappedAnother = true;
                }
            }
            if (!cappedAnother)
            {
                return;
            }
        }
    }

    private static int SampleIndex(double[] probs, NgPcg rng)
    {
        double total = 0;
        foreach (double p in probs)
        {
            total += p;
        }
        double roll = rng.NextDouble() * total;
        double acc = 0;
        int last = 0;
        for (int i = 0; i < probs.Length; i++)
        {
            if (probs[i] <= 0)
            {
                continue;
            }
            acc += probs[i];
            last = i;
            if (roll < acc)
            {
                return i;
            }
        }
        return last;
    }
}
