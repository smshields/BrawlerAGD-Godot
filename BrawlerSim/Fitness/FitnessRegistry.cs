namespace BrawlerSim.Fitness;

/// <summary>
/// Name → fitness function. run.json records the name a run was scored with; resuming
/// MUST reconstruct that exact version (a resumed pre-v3 run keeps standard-v2), so
/// every version ever shipped stays constructible here. The Registry table below is
/// the ONE list of shipped versions — the factory, the N-player guard, and the
/// unknown-name error message all derive from it, so a new version is exactly one row.
/// </summary>
public static class FitnessRegistry
{
    /// <summary>The default for NEW two-player runs — standard-v5 since 2026-09-01
    /// (v4 + the thin-platform drop-through tiebreaker, designer-directed). Old
    /// checkpoints resume under their recorded name.</summary>
    public const string DefaultName = "standard-v5";

    /// <summary>3/4-player runs default to the N-player generalization — ffa-v2
    /// since 2026-09-01 (ffa-v1 + the drop-through tiebreaker).</summary>
    public static string DefaultNameFor(int playerCount) =>
        playerCount > 2 ? "ffa-v2" : DefaultName;

    /// <summary>Every shipped version. SupportsNPlayers: the 2P-only versions' terms
    /// read exactly two players, so scoring an N-player match with them would
    /// silently ignore players 3/4. collisionScalar applies to the v3 family only
    /// (v2 is frozen at 1) — its factory simply ignores the argument.</summary>
    private static readonly (string Name, bool SupportsNPlayers,
        Func<float, float, float, IFitnessFunction> Create)[] Registry =
    {
        ("standard-v2", false, (target, max, _) => new StandardFitness(target, max)),
        ("standard-v3", false, (target, max, cs) => new StandardFitnessV3(target, max, collisionScalar: cs)),
        ("standard-v4", false, (target, max, cs) => new StandardFitnessV4(target, max, collisionScalar: cs)),
        ("standard-v5", false, (target, max, cs) => new StandardFitnessV5(target, max, collisionScalar: cs)),
        // standard-v6 (2026-09-09): the scaled-time stage-diversity EXPERIMENT — v5
        // with the time target re-anchored to map size. Opt-in, NOT the default,
        // pending the designer gate.
        ("standard-v6", false, (target, max, cs) => new StandardFitnessV6(target, max, collisionScalar: cs)),
        ("ffa-v1", true, (target, max, cs) => new FfaFitnessV1(target, max, collisionScalar: cs)),
        ("ffa-v2", true, (target, max, cs) => new FfaFitnessV2(target, max, collisionScalar: cs)),
    };

    public static IFitnessFunction Create(
        string? name, float targetLengthSeconds, float maxLengthSeconds,
        float? collisionScalar = null, int playerCount = 2)
    {
        string resolved = name ?? DefaultNameFor(playerCount);
        foreach ((string candidate, bool supportsN, var create) in Registry)
        {
            if (candidate != resolved)
            {
                continue;
            }
            if (playerCount != 2 && !supportsN)
            {
                throw new ArgumentException(
                    $"Fitness '{resolved}' scores exactly two players; use ffa-v1/ffa-v2 for {playerCount}-player runs.");
            }
            return create(targetLengthSeconds, maxLengthSeconds,
                collisionScalar ?? StandardFitnessV3.DefaultCollisionScalar);
        }
        throw new ArgumentException(
            $"Unknown fitness '{resolved}' ({string.Join("|", Registry.Select(entry => entry.Name))}).");
    }
}
