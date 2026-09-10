using NameGen.Traits;
using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Backgrounds;

/// <summary>The resolved background of one stage: a single full-scene entry OR a
/// recombined far + mid pair under one unified remap (Phase 2). Gene() is what the
/// genome stores.</summary>
public sealed record BackgroundSpec(
    BackgroundEntry? Single, BackgroundEntry? Far, BackgroundEntry? Mid,
    string? Remap, bool Goof)
{
    public bool IsComposite => Far is not null;

    public string Gene() => IsComposite
        ? new BackgroundComposite(Far!.Id, Mid!.Id, Remap).ToGene()
        : Single!.Id;
}

/// <summary>An optional L2 near-accent element (the tilt-shift bokeh plane): sparse,
/// heavily blurred, seeded — never over the platform envelope at readable opacity.</summary>
public sealed record BackgroundAccent(BackgroundEntry Element, int Anchor, float Factor, float Scale);

/// <summary>Everything the renderer needs for one stage's backdrop, fully derived
/// from (gene, stage, seed) — a pure function, so tests and the view agree.</summary>
public sealed record BackgroundLayout(
    BackgroundEntry? Single, BackgroundEntry? Far, BackgroundEntry? Mid,
    string? Remap, BackgroundVariant Variant,
    float FarFactor, float MidFactor, float SeamHaze, BackgroundAccent? Accent);

/// <summary>
/// Phase 2 (brief §Phase 2, corpus plan §Recombination design): cross-source
/// far x mid recombination — the corpus's primary variance engine. Pairing legality
/// is three RUNTIME predicates over entry metadata, never a precomputed pair table:
/// (a) the far/mid register intersection contains the stage's register — WAIVED in
///     the seeded goof lane, which instead prefers scene-tag-distant pairs;
/// (b) a shared legal remap target exists (both entries' pipeline-validated remap
///     lists intersected through the transition table; matching native groups admit
///     "no remap") — the UNIFIED remap is what makes arbitrary pairs coherent;
/// (c) atmospheric ordering — the far must read at least as light as the mid
///     (stored valMean); violations RAISE the seam haze rather than rejecting.
/// Plus the pairExclude scene-tag blocklist, which holds in BOTH lanes.
/// </summary>
public sealed partial class BackgroundSelector
{
    /// <summary>Sequences for the recombination rolls and the render layout — private
    /// streams, so Phase-1 candidate draws (SelectSequence) stay untouched.</summary>
    private const ulong ComposeSequence = 0x4247434f4d504f53UL; // "BGCOMPOS"
    private const ulong LayoutSequence = 0x42474c41594f5554UL;  // "BGLAYOUT"

    /// <summary>The unified-remap candidates a pair admits: "no remap" when the
    /// native groups already match, plus every target group EACH layer can serve —
    /// by already sitting in it natively (a native layer takes no remap; the render
    /// path skips it) or by a transition-legal, pipeline-validated remap into it.
    /// Empty = predicate (b) fails. Widened 2026-09-03 (designer: cityscape packs
    /// were dominating): the old both-lists-intersect rule locked the 400
    /// zero-remap entries out of almost every pair, leaving remap-rich city packs
    /// as the only legal mids.</summary>
    public List<string?> SharedRemapCandidates(BackgroundEntry far, BackgroundEntry mid)
    {
        var candidates = new List<string?>();
        if (far.PaletteGroup == mid.PaletteGroup)
        {
            candidates.Add(null);
        }
        foreach (string target in Library.RemapTargets)
        {
            bool farServes = far.PaletteGroup == target || LegalRemaps(far).Contains(target);
            bool midServes = mid.PaletteGroup == target || LegalRemaps(mid).Contains(target);
            if (farServes && midServes)
            {
                candidates.Add(target);
            }
        }
        return candidates;
    }

    /// <summary>The pairExclude scene-tag blocklist, both directions. A blocklist hit
    /// means broken, not funny — the goof lane never overrides it.</summary>
    public static bool PairExcluded(BackgroundEntry a, BackgroundEntry b) =>
        a.PairExclude.Any(b.Scene.Contains) || b.PairExclude.Any(a.Scene.Contains);

    /// <summary>Predicate (c): the far reads at least as hazy/light as the mid.</summary>
    public bool OrderingHolds(BackgroundEntry far, BackgroundEntry mid) =>
        far.Metrics.ValMean >= mid.Metrics.ValMean - Config.AtmosphericEpsilon;

    /// <summary>Scene-tag distance in [0, 1]: 1 = fully disjoint (what the goof lane
    /// pays for), 0 = one entry's scene tags all appear in the other's.</summary>
    public static double SceneDistance(BackgroundEntry a, BackgroundEntry b)
    {
        if (a.Scene.Count == 0 || b.Scene.Count == 0)
        {
            return 1;
        }
        int shared = a.Scene.Count(b.Scene.Contains);
        return 1 - shared / (double)Math.Min(a.Scene.Count, b.Scene.Count);
    }

    /// <summary>Resolve a stage's background SPEC: the seeded recombination and goof
    /// rolls (their own stream), then either the Phase-1 single pick or the far+mid
    /// pair selection; an unpairable draw falls back to single. Deterministic in
    /// (stage bytes, seed) like everything here.</summary>
    public BackgroundSpec ResolveSpec(Genome.StageGenome stage, ulong seed, string? themeId,
        IReadOnlyDictionary<string, int>? priorUse = null,
        IReadOnlyList<byte[]>? usedDescriptors = null)
    {
        var rolls = new NgPcg(seed, ComposeSequence);
        bool recombine = rolls.NextDouble() < Config.RecombinationProbability;
        bool goof = rolls.NextDouble() < Config.GoofBudget; // drawn unconditionally: stable stream

        if (recombine
            && SelectPair(stage, seed, themeId, goof, priorUse, usedDescriptors, rolls)
                is { } pair)
        {
            return pair;
        }
        BackgroundEntry single = SelectCandidates(
            stage, seed, themeId, out _, priorUse, usedDescriptors)[0].Entry;
        return new BackgroundSpec(single, null, null, PickRemap(single, themeId, seed), Goof: false);
    }

    private BackgroundSpec? SelectPair(Genome.StageGenome stage, ulong seed, string? themeId,
        bool goof, IReadOnlyDictionary<string, int>? priorUse,
        IReadOnlyList<byte[]>? usedDescriptors, NgPcg rng)
    {
        string register = PickRegister(seed);
        string? themeGroup = ThemeGroup(themeId);
        IReadOnlyList<SalientTrait> salient = Salient(stage);

        // FAR pool: register-filtered unless the goof lane waived predicate (a);
        // thin pools fall back to every far (the Phase-1 rule).
        List<BackgroundEntry> fars = Library.Entries
            .Where(e => e.LayerRole == "far" && (goof || e.Register.Contains(register)))
            .ToList();
        if (fars.Count < Config.RegisterPoolFloor)
        {
            fars = Library.Entries.Where(e => e.LayerRole == "far").ToList();
        }
        fars = FilterDistinct(fars, usedDescriptors);
        if (fars.Count == 0)
        {
            return null;
        }

        // Ordered far candidates; the first with a non-empty legal mid pool wins.
        List<BackgroundEntry> farsOrdered = SampleOrdered(
            fars, e => Score(e, salient, themeGroup, priorUse), rng);
        foreach (BackgroundEntry far in farsOrdered)
        {
            List<BackgroundEntry> mids = Library.Entries
                .Where(m => m.LayerRole == "mid"
                    && (goof || (m.Register.Contains(register) && far.Register.Contains(register)))
                    && SharedRemapCandidates(far, m).Count > 0
                    && !PairExcluded(far, m))
                .ToList();
            mids = FilterDistinct(mids, usedDescriptors);
            if (mids.Count == 0)
            {
                continue;
            }
            List<BackgroundEntry> midsOrdered = SampleOrdered(
                mids,
                m => Score(m, salient, themeGroup, priorUse)
                    + (OrderingHolds(far, m) ? Config.OrderingBonus : 0)
                    + (goof ? Config.SceneDistanceBonus * SceneDistance(far, m) : 0),
                rng);
            BackgroundEntry mid = midsOrdered[0];
            return new BackgroundSpec(null, far, mid,
                PickUnifiedRemap(far, mid, themeGroup, seed), goof);
        }
        return null;
    }

    /// <summary>The unified remap for a pair: best theme harmony over the shared
    /// candidates ("no remap" carries its keep bonus), seeded tie-break.</summary>
    public string? PickUnifiedRemap(BackgroundEntry far, BackgroundEntry mid,
        string? themeGroup, ulong seed)
    {
        List<string?> candidates = SharedRemapCandidates(far, mid);
        if (candidates.Count == 0)
        {
            return null; // callers guarantee predicate (b); defensive only
        }
        double Best(string? target) => target is null
            ? GroupHarmony(far.PaletteGroup, themeGroup) + Config.RemapNoneBonus
            : GroupHarmony(target, themeGroup);
        double best = candidates.Max(Best);
        List<string?> tied = candidates.Where(c => Best(c) >= best - 1e-9).ToList();
        if (tied.Count == 1)
        {
            return tied[0];
        }
        var rng = new NgPcg(seed, RemapSequence);
        return tied[rng.NextInt(tied.Count)];
    }

    private double Score(BackgroundEntry e, IReadOnlyList<SalientTrait> salient,
        string? themeGroup, IReadOnlyDictionary<string, int>? priorUse)
    {
        double score = AffinityScore(e, salient) + BestHarmony(e, themeGroup);
        if (e.Style == "texture")
        {
            score -= Config.TextureStylePenalty; // scene art first (2026-09-10)
        }
        if (e.Metrics.ContrastBand > Config.ContrastBandCeiling)
        {
            score -= Config.ContrastPenaltyWeight
                * (e.Metrics.ContrastBand - Config.ContrastBandCeiling)
                / Config.ContrastBandCeiling;
        }
        if (priorUse is not null && priorUse.TryGetValue(e.Id, out int uses))
        {
            score -= Config.OverusePenalty * uses;
        }
        return score;
    }

    private List<BackgroundEntry> FilterDistinct(
        List<BackgroundEntry> pool, IReadOnlyList<byte[]>? usedDescriptors)
    {
        if (usedDescriptors is not { Count: > 0 })
        {
            return pool;
        }
        List<BackgroundEntry> distinct = pool.Where(e =>
            usedDescriptors.All(d => BackgroundEntry.DescriptorDistance(e.Descriptor, d)
                >= Config.DescriptorMinDistance)).ToList();
        return distinct.Count > 0 ? distinct : pool;
    }

    /// <summary>Seeded softmax + share cap + ordered top-k over an arbitrary pool —
    /// the Phase-1 sampling shape, reused for far and mid picks.</summary>
    private List<BackgroundEntry> SampleOrdered(
        List<BackgroundEntry> pool, Func<BackgroundEntry, double> score, NgPcg rng)
    {
        var scores = new double[pool.Count];
        for (int i = 0; i < pool.Count; i++)
        {
            scores[i] = score(pool[i]);
        }
        double[] probs = SelectionMath.Softmax(scores, Config.SoftmaxTemperature);
        SelectionMath.CapGroupShare(probs, SourceGroups(pool), Config.SourceShareCap);
        SelectionMath.CapShare(probs, Config.MaxEntryShare);
        int k = Math.Min(Config.CandidateCount, pool.Count);
        var ordered = new List<BackgroundEntry>(k);
        for (int draw = 0; draw < k; draw++)
        {
            int index = SelectionMath.SampleIndex(probs, rng);
            ordered.Add(pool[index]);
            probs[index] = 0;
        }
        return ordered;
    }

    // ── gene parsing + the render layout ───────────────────────────────────────

    /// <summary>Resolve a GENE string back to its entries; null when any part is
    /// unknown (repair follows).</summary>
    public BackgroundSpec? ParseGene(string? gene)
    {
        if (gene is null)
        {
            return null;
        }
        if (BackgroundComposite.TryParse(gene) is { } composite)
        {
            BackgroundEntry? far = Library.ById(composite.FarId);
            BackgroundEntry? mid = Library.ById(composite.MidId);
            return far is null || mid is null
                ? null
                : new BackgroundSpec(null, far, mid, composite.Remap, Goof: false);
        }
        BackgroundEntry? single = Library.ById(gene);
        return single is null ? null : new BackgroundSpec(single, null, null, null, Goof: false);
    }

    /// <summary>Everything the renderer draws for this stage, derived purely from
    /// (gene, stage bytes, seed): the single/far variant, per-layer parallax factors,
    /// the seam haze strength (raised when predicate c is violated), and the optional
    /// L2 accent. remapOverride carries a built stage's persisted SINGLE-image remap;
    /// composites carry their remap inside the gene.</summary>
    public BackgroundLayout? Layout(Genome.StageGenome stage, ulong seed, string? remapOverride = null)
    {
        BackgroundSpec? spec = ParseGene(stage.BackgroundId);
        if (spec is null)
        {
            return null;
        }
        var rng = new NgPcg(seed, LayoutSequence);
        float farFactor = Lerp(Config.FarFactorMin, Config.FarFactorMax, rng.NextDouble());
        float midFactor = Lerp(Config.MidFactorMin, Config.MidFactorMax, rng.NextDouble());
        bool accentRoll = rng.NextDouble() < Config.AccentProbability;
        int accentPick = rng.NextInt(int.MaxValue);
        int anchor = rng.NextInt(3);
        float accentFactor = Lerp(Config.AccentFactorMin, Config.AccentFactorMax, rng.NextDouble());
        float accentScale = Lerp(Config.AccentScaleMin, Config.AccentScaleMax, rng.NextDouble());

        BackgroundEntry cropEntry = spec.Single ?? spec.Far!;
        BackgroundVariant variant = Variant(cropEntry, stage, seed);

        if (!spec.IsComposite)
        {
            string? remap = remapOverride ?? spec.Remap
                ?? PickRemap(spec.Single!, stage.ThemeId, seed);
            return new BackgroundLayout(spec.Single, null, null, remap, variant,
                farFactor, 0f, 0f, null);
        }

        float seamHaze = OrderingHolds(spec.Far!, spec.Mid!)
            ? Config.SeamHazeBase
            : Config.SeamHazeForced;
        BackgroundAccent? accent = accentRoll
            ? PickAccent(stage, seed, accentPick, anchor, accentFactor, accentScale)
            : null;
        return new BackgroundLayout(null, spec.Far, spec.Mid, spec.Remap, variant,
            farFactor, midFactor, seamHaze, accent);
    }

    /// <summary>The L2 accent element: register-pooled (full fallback), uniform
    /// seeded pick. Skipped when the platform envelope reaches into the accent band
    /// (the top third of the kill box) — the bokeh plane must never sit over the
    /// action at readable opacity.</summary>
    private BackgroundAccent? PickAccent(Genome.StageGenome stage, ulong seed,
        int pick, int anchor, float factor, float scale)
    {
        Determinism.Vec2 blast = Genome.StageRules.BlastHalfExtents(stage.Params);
        float envelopeTop = float.MinValue;
        foreach (Genome.PlatformGene p in stage.Platforms)
        {
            envelopeTop = Math.Max(envelopeTop, p.Y + p.YSize);
        }
        if (envelopeTop >= blast.Y * 0.35f)
        {
            return null;
        }
        string register = PickRegister(seed);
        bool Prop(BackgroundEntry e) => e.LayerRole == "element"
            && Math.Max(e.Width, e.Height)
                <= Config.AccentMaxAspect * Math.Min(e.Width, e.Height);
        List<BackgroundEntry> pool = Library.Entries
            .Where(e => Prop(e) && e.Register.Contains(register))
            .ToList();
        if (pool.Count == 0)
        {
            pool = Library.Entries.Where(Prop).ToList();
        }
        if (pool.Count == 0)
        {
            return null;
        }
        return new BackgroundAccent(pool[pick % pool.Count], anchor, factor, scale);
    }

    private static float Lerp(float min, float max, double t) => min + (float)t * (max - min);
}
