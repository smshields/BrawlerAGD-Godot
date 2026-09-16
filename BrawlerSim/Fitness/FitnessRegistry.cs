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
    /// <summary>The default for NEW two-player runs — standard-v7 since 2026-09-14
    /// (designer-directed: opponent-only interaction inputs after the projectile
    /// self-hit reward exploit, plus the v6 map-scaled time target graduated to
    /// default). Old checkpoints resume under their recorded name.</summary>
    public const string DefaultName = "standard-v7";

    /// <summary>3/4-player runs default to the N-player generalization — ffa-v3
    /// since 2026-09-14 (the standard-v7 formula under its N-player name).</summary>
    public static string DefaultNameFor(int playerCount) =>
        playerCount > 2 ? "ffa-v3" : DefaultName;

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
        // standard-v7 / ffa-v3 (2026-09-14): opponent-only interaction (self-hit
        // reward fix) + the scaled time target — the new defaults; identical
        // formula, two names (SelfBlindTerms.Build).
        ("standard-v7", false, (target, max, cs) => new StandardFitnessV7(target, max, collisionScalar: cs)),
        ("ffa-v1", true, (target, max, cs) => new FfaFitnessV1(target, max, collisionScalar: cs)),
        ("ffa-v2", true, (target, max, cs) => new FfaFitnessV2(target, max, collisionScalar: cs)),
        ("ffa-v3", true, (target, max, cs) => new FfaFitnessV3(target, max, collisionScalar: cs)),
    };

    /// <summary>Every shipped version name. A custom assembly may not take one of
    /// these (FitnessRecipe.Validate) — a run scored by a designer-built instrument
    /// must never be mistakable for a shipped version in a manifest or a chart.</summary>
    public static IReadOnlyList<string> Names { get; } = Registry.Select(entry => entry.Name).ToArray();

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
