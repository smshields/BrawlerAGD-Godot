namespace BrawlerSim.Fitness;

/// <summary>
/// Name → fitness function. run.json records the name a run was scored with; resuming
/// MUST reconstruct that exact version (a resumed pre-v3 run keeps standard-v2), so
/// every version ever shipped stays constructible here.
/// </summary>
public static class FitnessRegistry
{
    /// <summary>The default for NEW two-player runs — standard-v4 since 2026-08-12
    /// (v3 + the self-destruct punishment, designer-directed). Old checkpoints resume
    /// under their recorded name.</summary>
    public const string DefaultName = "standard-v4";

    /// <summary>3/4-player runs default to the N-player generalization (2026-08-12,
    /// docs/features/four-player.md).</summary>
    public static string DefaultNameFor(int playerCount) =>
        playerCount > 2 ? "ffa-v1" : DefaultName;

    /// <summary>collisionScalar applies to v3-family fitnesses only (v2 is frozen at 1).
    /// playerCount guards the 2P-only versions: their terms read exactly two players,
    /// so scoring an N-player match with them would silently ignore players 3/4.</summary>
    public static IFitnessFunction Create(
        string? name, float targetLengthSeconds, float maxLengthSeconds,
        float? collisionScalar = null, int playerCount = 2)
    {
        string resolved = name ?? DefaultNameFor(playerCount);
        if (playerCount != 2 && resolved != "ffa-v1")
        {
            throw new ArgumentException(
                $"Fitness '{resolved}' scores exactly two players; use ffa-v1 for {playerCount}-player runs.");
        }
        return resolved switch
        {
            "standard-v2" => new StandardFitness(targetLengthSeconds, maxLengthSeconds),
            "standard-v3" => new StandardFitnessV3(targetLengthSeconds, maxLengthSeconds,
                collisionScalar: collisionScalar ?? StandardFitnessV3.DefaultCollisionScalar),
            "standard-v4" => new StandardFitnessV4(targetLengthSeconds, maxLengthSeconds,
                collisionScalar: collisionScalar ?? StandardFitnessV3.DefaultCollisionScalar),
            "ffa-v1" => new FfaFitnessV1(targetLengthSeconds, maxLengthSeconds,
                collisionScalar: collisionScalar ?? StandardFitnessV3.DefaultCollisionScalar),
            var other => throw new ArgumentException(
                $"Unknown fitness '{other}' (standard-v2|standard-v3|standard-v4|ffa-v1)."),
        };
    }
}
