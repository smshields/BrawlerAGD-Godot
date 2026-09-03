using BrawlerSim.Sprites;
using NameGen;
using NameGen.Data;
using NameGen.Features;
using NameGen.Traits;
using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Backgrounds;

/// <summary>A scored background candidate, ordered by the seeded softmax sample.</summary>
public sealed record BackgroundCandidate(BackgroundEntry Entry, double Score);

/// <summary>The negotiated background presentation of one stage: entry id, the settled
/// palette remap target (null = native palette), and the shared register.</summary>
public sealed record BackgroundPresentation(string BackgroundId, string? Remap, string Register);

/// <summary>The seeded parametric variant of a background entry on one stage (brief
/// decision 4): crop window in source pixels, horizontal flip, brightness/contrast
/// jitter, blur strength scale. A pure function of (entry, stage params, seed) — the
/// view derives nothing on its own, so the same genome + seed renders the same
/// backdrop everywhere.</summary>
public sealed record BackgroundVariant(
    BgRect Crop, bool FlipX, float Brightness, float Contrast, float BlurScale);

/// <summary>
/// Semantic background selection (backgrounds track Phase 1, 2026-09-02 —
/// docs/background-implementation-brief.md): the StageThemeSelector architecture
/// applied to the bg-v1 corpus. Deterministic — same stage genome + seed = same
/// (background, remap, variant, register) everywhere; draws run on a private Pcg32
/// sequence so selection never perturbs naming, theme, or evolution streams. The
/// register pick REPLAYS the tile-theme selector's first STGTHEME draw, so name,
/// tiles, and background can never disagree on register by construction; palette
/// harmony scores the POST-remap group against the selected tile theme's group — the
/// background follows the tile pick, never the reverse.
/// </summary>
public sealed partial class BackgroundSelector
{
    /// <summary>Private Pcg32 sequences: candidate draws, the remap settle, and the
    /// parametric variant — independent of naming/theme/sprite streams.</summary>
    private const ulong SelectSequence = 0x424753454c454354UL;  // "BGSELECT"
    private const ulong RemapSequence = 0x424752454d415053UL;   // "BGREMAPS"
    private const ulong VariantSequence = 0x42475641524e5453UL; // "BGVARNTS"

    private readonly NameGenData _data;
    private readonly FeatureExtractor _extractor;
    private readonly StageThemeLibrary? _themes;

    public BackgroundLibrary Library { get; }
    public BackgroundPalette Palette { get; }
    public BackgroundSelectionConfig Config { get; }

    public BackgroundSelector(BackgroundLibrary library, BackgroundPalette palette,
        BackgroundSelectionConfig? config = null, StageThemeLibrary? themeLibrary = null,
        NameGenData? data = null)
    {
        Library = library;
        Palette = palette;
        Config = config ?? BackgroundSelectionConfig.Default;
        _themes = themeLibrary;
        _data = data ?? NameGenData.LoadEmbedded();
        _extractor = new FeatureExtractor(_data.Ranges);
    }

    /// <summary>Salient stage traits exactly as naming and theme selection derive
    /// them — same extractor, same trait table.</summary>
    public IReadOnlyList<SalientTrait> Salient(Genome.StageGenome stage)
    {
        FeatureVector features = _extractor.ExtractStage(StageThemeSelector.Map(stage));
        return TraitScorer.SelectSalient(features, _data.Traits.Stage,
            _data.Traits.SalienceTopK, _data.Traits.SalienceThreshold);
    }

    /// <summary>Raw trait-affinity score (the repair metric).</summary>
    public static double AffinityScore(BackgroundEntry entry, IReadOnlyList<SalientTrait> salient)
    {
        double score = 0;
        foreach (SalientTrait trait in salient)
        {
            if (entry.TraitAffinity.TryGetValue(trait.Name, out float affinity))
            {
                score += trait.Score * affinity;
            }
        }
        return score;
    }

    /// <summary>The content-derived seed — identical to the stage NAMING seed.</summary>
    public static ulong BackgroundSeed(Genome.StageGenome stage) =>
        Serialization.BuiltGameNaming.NamingSeed(stage);

    /// <summary>The shared register: a replay of the tile-theme selector's first
    /// STGTHEME draw from the same seed — equal by construction whether or not a
    /// theme library is attached.</summary>
    public string PickRegister(ulong seed) =>
        StageThemeSelector.PickRegister(new NgPcg(seed, StageThemeSelector.ThemeSequence), _data);

    /// <summary>Repair rule (the ThemeId pattern): re-resolve when the gene is
    /// missing/unparseable/unknown, or the stage HAS salient traits and the affinity
    /// falls below the floor. A composite repairs AS A UNIT (brief decision 1): any
    /// unknown layer, a now-illegal remap, a pairExclude hit, or a floor miss
    /// re-resolves the WHOLE gene — children can never drift into illegal pairs.</summary>
    public bool NeedsRepair(Genome.StageGenome stage)
    {
        BackgroundSpec? spec = ParseGene(stage.BackgroundId);
        if (spec is null)
        {
            return true;
        }
        if (spec.IsComposite)
        {
            List<string?> shared = SharedRemapCandidates(spec.Far!, spec.Mid!);
            if (!shared.Contains(spec.Remap) || PairExcluded(spec.Far!, spec.Mid!))
            {
                return true;
            }
            IReadOnlyList<SalientTrait> salientPair = Salient(stage);
            return salientPair.Count > 0
                && Math.Max(AffinityScore(spec.Far!, salientPair),
                    AffinityScore(spec.Mid!, salientPair)) < Config.RepairFloor;
        }
        IReadOnlyList<SalientTrait> salient = Salient(stage);
        return salient.Count > 0 && AffinityScore(spec.Single!, salient) < Config.RepairFloor;
    }

    /// <summary>The tile theme's paletteGroup mapped into the background group
    /// vocabulary (config aliases), or null when unknown/absent — harmony then scores
    /// zero everywhere and selection is trait-driven alone.</summary>
    public string? ThemeGroup(string? themeId)
    {
        string? group = _themes?.ByName(themeId)?.PaletteGroup;
        if (group is null)
        {
            return null;
        }
        return Config.ThemeGroupAliases.TryGetValue(group, out string? alias) ? alias : group;
    }

    /// <summary>Palette harmony of one background group against the theme group:
    /// same &gt; adjacent (remap transition table, either direction) &gt; none.</summary>
    public double GroupHarmony(string group, string? themeGroup)
    {
        if (themeGroup is null)
        {
            return 0;
        }
        if (group == themeGroup)
        {
            return Config.HarmonySameBonus;
        }
        return Palette.TransitionsFor(group).Contains(themeGroup)
            || Palette.TransitionsFor(themeGroup).Contains(group)
                ? Config.HarmonyAdjacentBonus
                : 0;
    }

    /// <summary>The harmony an entry can reach against the theme group across its
    /// native palette and every legal remap target — selection scores the best
    /// achievable POST-remap group (the brief's ordering: tile theme → background
    /// source → remap target that harmonizes).</summary>
    private double BestHarmony(BackgroundEntry entry, string? themeGroup)
    {
        double best = GroupHarmony(entry.PaletteGroup, themeGroup);
        foreach (string target in LegalRemaps(entry))
        {
            best = Math.Max(best, GroupHarmony(target, themeGroup));
        }
        return best;
    }

    /// <summary>The entry's pipeline-validated remap targets, filtered through the
    /// art-direction transition table.</summary>
    public IReadOnlyList<string> LegalRemaps(BackgroundEntry entry)
    {
        IReadOnlyList<string> allowed = Palette.TransitionsFor(entry.PaletteGroup);
        return entry.Remaps.Where(allowed.Contains).ToList();
    }

    /// <summary>Selection over the Phase-1 pool (layerRole == "full"): register pool
    /// with full-library fallback below the floor → trait affinity + best-achievable
    /// palette harmony − action-band busyness − per-game overuse → within-game
    /// descriptor-distance filter → seeded softmax with the no-monopoly cap → ordered
    /// top-k sample. Same shape and primitives as theme selection.</summary>
    public IReadOnlyList<BackgroundCandidate> SelectCandidates(
        Genome.StageGenome stage, ulong seed, string? themeId, out string register,
        IReadOnlyDictionary<string, int>? priorUse = null,
        IReadOnlyList<byte[]>? usedDescriptors = null)
    {
        register = PickRegister(seed);
        string picked = register;
        var rng = new NgPcg(seed, SelectSequence);

        List<BackgroundEntry> pool = Library.Entries
            .Where(e => e.LayerRole == "full" && e.Register.Contains(picked))
            .ToList();
        if (pool.Count < Config.RegisterPoolFloor)
        {
            pool = Library.Entries.Where(e => e.LayerRole == "full").ToList();
        }

        // Lineup uniqueness (the perceptual-descriptor rule): entries too close to a
        // backdrop already on this game are excluded — unless that empties the pool.
        if (usedDescriptors is { Count: > 0 })
        {
            List<BackgroundEntry> distinct = pool.Where(e =>
                usedDescriptors.All(d =>
                    BackgroundEntry.DescriptorDistance(e.Descriptor, d)
                        >= Config.DescriptorMinDistance)).ToList();
            if (distinct.Count > 0)
            {
                pool = distinct;
            }
        }

        string? themeGroup = ThemeGroup(themeId);
        IReadOnlyList<SalientTrait> salient = Salient(stage);
        var scores = new double[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            BackgroundEntry e = pool[i];
            scores[i] = AffinityScore(e, salient) + BestHarmony(e, themeGroup);
            if (e.Metrics.ContrastBand > Config.ContrastBandCeiling)
            {
                scores[i] -= Config.ContrastPenaltyWeight
                    * (e.Metrics.ContrastBand - Config.ContrastBandCeiling)
                    / Config.ContrastBandCeiling;
            }
            if (priorUse is not null && priorUse.TryGetValue(e.Id, out int uses))
            {
                scores[i] -= Config.OverusePenalty * uses;
            }
        }

        double[] probs = SelectionMath.Softmax(scores, Config.SoftmaxTemperature);
        SelectionMath.CapGroupShare(probs, SourceGroups(pool), Config.SourceShareCap);
        SelectionMath.CapShare(probs, Config.MaxEntryShare);

        int k = Math.Min(Config.CandidateCount, pool.Count);
        var ordered = new List<BackgroundCandidate>(k);
        for (int draw = 0; draw < k; draw++)
        {
            int index = SelectionMath.SampleIndex(probs, rng);
            ordered.Add(new BackgroundCandidate(pool[index], scores[index]));
            probs[index] = 0;
        }
        return ordered;
    }

    /// <summary>Pool entries keyed by source pack, for the family-level share cap
    /// (designer 2026-09-03: one pack contributing half the corpus was dominating
    /// picks — the per-entry cap cannot police that).</summary>
    internal static int[] SourceGroups(List<BackgroundEntry> pool)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var groups = new int[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            if (!ids.TryGetValue(pool[i].Source, out int id))
            {
                id = ids.Count;
                ids[pool[i].Source] = id;
            }
            groups[i] = id;
        }
        return groups;
    }

    /// <summary>Resolve the BackgroundId GENE — the seeded spec's gene string (a
    /// single entry id, or a composite since Phase 2). The stage's own ThemeId feeds
    /// harmony (backgrounds resolve AFTER themes).</summary>
    public string ResolveBackgroundId(Genome.StageGenome stage, ulong seed,
        IReadOnlyDictionary<string, int>? priorUse = null) =>
        ResolveSpec(stage, seed, stage.ThemeId, priorUse).Gene();

    /// <summary>Gene upkeep for the breeding pipeline: assigns a background to a stage
    /// that needs one using the content-derived seed, and returns the stage unchanged
    /// otherwise. RNG-free with respect to the evolution stream.</summary>
    public Genome.StageGenome EnsureGene(Genome.StageGenome stage)
    {
        if (!NeedsRepair(stage))
        {
            return stage;
        }
        return stage.WithBackgroundId(ResolveBackgroundId(stage, BackgroundSeed(stage)));
    }

    /// <summary>Settle the remap target for a chosen entry against the tile theme:
    /// best harmony wins over {native + legal remaps}, the native palette carries a
    /// small keep bonus, and exact ties break by a seeded draw on a private sequence.
    /// Null = no remap.</summary>
    public string? PickRemap(BackgroundEntry entry, string? themeId, ulong seed)
    {
        string? themeGroup = ThemeGroup(themeId);
        var options = new List<(string? Target, double Score)>
        {
            (null, GroupHarmony(entry.PaletteGroup, themeGroup) + Config.RemapNoneBonus),
        };
        foreach (string target in LegalRemaps(entry))
        {
            options.Add((target, GroupHarmony(target, themeGroup)));
        }
        double best = options.Max(o => o.Score);
        List<(string? Target, double Score)> tied =
            options.Where(o => o.Score >= best - 1e-9).ToList();
        if (tied.Count == 1)
        {
            return tied[0].Target;
        }
        var rng = new NgPcg(seed, RemapSequence);
        return tied[rng.NextInt(tied.Count)].Target;
    }

    /// <summary>The presentation pass entry point (game open / prep-game): settle the
    /// background and its remap for one stage, honoring an inherited gene when it is
    /// known, above the repair floor, and distinct enough from the lineup. The register
    /// comes from the same shared seed as the theme/name pass.</summary>
    public BackgroundPresentation Present(Genome.StageGenome stage, ulong seed,
        string? themeId, IReadOnlyDictionary<string, int>? priorUse = null,
        IReadOnlyList<byte[]>? usedDescriptors = null, string? inheritedBackgroundId = null)
    {
        string register = PickRegister(seed);
        BackgroundSpec? inherited = ParseGene(inheritedBackgroundId);
        if (inherited is not null
            && !NeedsRepair(stage.WithBackgroundId(inheritedBackgroundId))
            && DistinctEnough(inherited, usedDescriptors))
        {
            string? inheritedRemap = inherited.IsComposite
                ? inherited.Remap
                : PickRemap(inherited.Single!, themeId, seed);
            return new BackgroundPresentation(inheritedBackgroundId!, inheritedRemap, register);
        }
        BackgroundSpec spec = ResolveSpec(stage, seed, themeId, priorUse, usedDescriptors);
        return new BackgroundPresentation(spec.Gene(), spec.Remap, register);
    }

    /// <summary>Every entry of the spec clears the lineup descriptor-distance floor.</summary>
    private bool DistinctEnough(BackgroundSpec spec, IReadOnlyList<byte[]>? usedDescriptors)
    {
        if (usedDescriptors is not { Count: > 0 })
        {
            return true;
        }
        IEnumerable<BackgroundEntry> parts = spec.IsComposite
            ? new[] { spec.Far!, spec.Mid! }
            : new[] { spec.Single! };
        return parts.All(e => usedDescriptors.All(d =>
            BackgroundEntry.DescriptorDistance(e.Descriptor, d) >= Config.DescriptorMinDistance));
    }

    /// <summary>The seeded parametric variant (brief Phase 1 step 5): a crop window
    /// matching the stage's kill-box aspect (vertically anchored so horizonY lands in
    /// the configured band when the entry has one), a 50% horizontal flip, and
    /// brightness/contrast/blur jitter within guardrail bounds. Pure and stateless —
    /// derived identically by tests and the renderer.</summary>
    public BackgroundVariant Variant(BackgroundEntry entry, Genome.StageGenome stage, ulong seed)
    {
        var rng = new NgPcg(seed, VariantSequence);
        Determinism.Vec2 blast = Genome.StageRules.BlastHalfExtents(stage.Params);
        double aspect = blast.Y <= 0 ? 16.0 / 9.0 : blast.X / (double)blast.Y;

        int cw, ch;
        if (entry.Width / (double)entry.Height > aspect)
        {
            ch = entry.Height;
            cw = Math.Max(1, (int)Math.Floor(entry.Height * aspect));
        }
        else
        {
            cw = entry.Width;
            ch = Math.Max(1, (int)Math.Floor(entry.Width / aspect));
        }

        int cx = entry.Width > cw ? rng.NextInt(entry.Width - cw + 1) : 0;
        int cy;
        if (entry.HorizonY is { } horizon && ch < entry.Height)
        {
            double f = Config.HorizonBandMin
                + rng.NextDouble() * (Config.HorizonBandMax - Config.HorizonBandMin);
            cy = Math.Clamp((int)Math.Round(horizon - f * ch), 0, entry.Height - ch);
        }
        else
        {
            cy = entry.Height > ch ? rng.NextInt(entry.Height - ch + 1) : 0;
        }

        bool flip = rng.NextInt(2) == 1;
        float brightness = 1f + (float)(rng.NextDouble() * 2 - 1) * Config.BrightnessJitter;
        float contrast = 1f + (float)(rng.NextDouble() * 2 - 1) * Config.ContrastJitter;
        float blur = Config.BlurScaleMin
            + (float)rng.NextDouble() * (Config.BlurScaleMax - Config.BlurScaleMin);
        return new BackgroundVariant(new BgRect(cx, cy, cw, ch), flip, brightness, contrast, blur);
    }
}
