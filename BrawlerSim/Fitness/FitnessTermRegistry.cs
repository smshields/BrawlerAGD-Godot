using BrawlerSim.Fitness.Terms;

namespace BrawlerSim.Fitness;

/// <summary>
/// Term id → what it is and how to build one (2026-09-16, fitness term refactor).
/// The counterpart to <see cref="FitnessRegistry"/>: that one lists shipped VERSIONS,
/// this one lists the PARTS a version — or a designer — assembles a fitness from.
///
/// The table below is the ONE list of available terms. A builder UI enumerates it to
/// draw its rows and spin boxes, <see cref="Create"/> turns a term id plus a partial
/// parameter map into a live term (missing keys take the spec default), and
/// <see cref="IFitnessTerm.Values"/> reads a built term back out again. Adding a term
/// is exactly one row here plus its class.
///
/// Not yet wired to run.json or the CLI: a custom assembly is constructible and
/// evaluable, but a RUN scored by one is not yet reproducible, because the manifest
/// still records only a version name. That plumbing is the next step — see
/// docs/features/fitness-builder.md §4 phase 3.
/// </summary>
public static class FitnessTermRegistry
{
    /// <summary>One available term: its identity, its tunable constants, and its
    /// factory. Label and Doc are UI copy and carry no behavior.</summary>
    public sealed record Entry(
        string Id,
        string Label,
        string Doc,
        IReadOnlyList<FitnessTermParam> Parameters,
        Func<IReadOnlyDictionary<string, float>?, IFitnessTerm> Create);

    public static IReadOnlyList<Entry> All { get; } = new[]
    {
        new Entry(TimeTerm.TermId, "PACING",
            "Rewards matches close to a target length; penalizes hitting the timeout.",
            TimeTerm.Specs, v => new TimeTerm(v)),

        new Entry(DamageTerm.TermId, "DAMAGE",
            "Rewards damage taken, per stock, clipped so a farmed life stops paying out.",
            DamageTerm.Specs, v => new DamageTerm(v)),

        new Entry(FarmPenaltyTerm.TermId, "FARM PENALTY",
            "Punishes a single stock absorbing far more damage than a kill should take.",
            FarmPenaltyTerm.Specs, v => new FarmPenaltyTerm(v)),

        new Entry(CollisionsTerm.TermId, "INTERACTION DENSITY",
            "Rewards hits landed, regardless of damage.",
            CollisionsTerm.Specs, v => new CollisionsTerm(v)),

        new Entry(DamageFairnessTerm.TermId, "DAMAGE BALANCE",
            "Punishes the damage spread between the most and least damaged fighter.",
            DamageFairnessTerm.Specs, v => new DamageFairnessTerm(v)),

        new Entry(StockFairnessTerm.TermId, "STOCK BALANCE",
            "Rewards close finishes over sweeps.",
            StockFairnessTerm.Specs, v => new StockFairnessTerm(v)),

        new Entry(MoveMixTerm.TermId, "KIT BREADTH",
            "Rewards even use of every move a character has.",
            MoveMixTerm.Specs, v => new MoveMixTerm(v)),

        new Entry(StunLockTerm.TermId, "STUN LOCK",
            "Punishes the share of the match spent stunned above a tolerance.",
            StunLockTerm.Specs, v => new StunLockTerm(v)),

        new Entry(JumpsTerm.TermId, "AIRBORNE PLAY",
            "Saturating reward for jumping; jump-spam earns nothing extra.",
            JumpsTerm.Specs, v => new JumpsTerm(v)),

        new Entry(BlocksTerm.TermId, "BLOCKS",
            "Rewards blocked hits, so shields are not selected against.",
            BlocksTerm.Specs, v => new BlocksTerm(v)),

        new Entry(SelfDestructsTerm.TermId, "SELF-DESTRUCTS",
            "Punishes deaths with no attributable enemy influence. Capped per match.",
            SelfDestructsTerm.Specs, v => new SelfDestructsTerm(v)),

        new Entry(DropThroughsTerm.TermId, "DROP-THROUGHS",
            "Minor never-negative tiebreaker for thin platforms actually being used.",
            DropThroughsTerm.Specs, v => new DropThroughsTerm(v)),

        new Entry(TimeRescaleTerm.TermId, "PACING CORRECTION (LEGACY)",
            "standard-v6's map-scale patch over a flat pacing term. Prefer the pacing term's own SCALE TARGET BY MAP SIZE flag.",
            TimeRescaleTerm.Specs, v => new TimeRescaleTerm(v)),
    };

    public static Entry For(string id)
    {
        foreach (Entry entry in All)
        {
            if (entry.Id == id)
            {
                return entry;
            }
        }
        throw new ArgumentException(
            $"Unknown fitness term '{id}' ({string.Join("|", All.Select(e => e.Id))}).");
    }

    /// <summary>Build one term. Keys absent from <paramref name="values"/> take their
    /// spec default, so a UI or a saved assembly only has to carry what it changed.</summary>
    public static IFitnessTerm Create(string id, IReadOnlyDictionary<string, float>? values = null) =>
        For(id).Create(values);

    /// <summary>Assemble a fitness from term ids and their parameter maps, in the
    /// order given. ORDER MATTERS — float addition is not associative, so the list is
    /// part of the definition (see <see cref="FitnessComposer"/>).</summary>
    public static FitnessComposer Compose(
        string name, IEnumerable<(string Id, IReadOnlyDictionary<string, float>? Values)> terms) =>
        new(name, terms.Select(t => Create(t.Id, t.Values)));

    /// <summary>The CURRENT DEFAULT fitness as an editable assembly — the seed a
    /// builder starts from. Identical term-for-term to the version
    /// <see cref="FitnessRegistry.DefaultNameFor"/> returns (standard-v7 at two
    /// players, ffa-v3 above), and it scores identically; only the name differs, so a
    /// custom assembly can never be mistaken for a shipped version in a manifest or a
    /// chart.</summary>
    public static FitnessComposer SeedFromDefault(
        string name = "custom",
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar) =>
        new(name, ShippedTermLists.V7(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar));
}
