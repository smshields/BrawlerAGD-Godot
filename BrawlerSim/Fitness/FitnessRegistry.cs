namespace BrawlerSim.Fitness;

/// <summary>
/// Name → fitness function. run.json records the name a run was scored with; resuming
/// MUST reconstruct that exact version (a resumed pre-v3 run keeps standard-v2), so
/// every version ever shipped stays constructible here.
/// </summary>
public static class FitnessRegistry
{
    public const string DefaultName = "standard-v3";

    /// <summary>collisionScalar applies to standard-v3 only (v2 is frozen at 1).</summary>
    public static IFitnessFunction Create(
        string? name, float targetLengthSeconds, float maxLengthSeconds, float? collisionScalar = null) =>
        (name ?? DefaultName) switch
        {
            "standard-v2" => new StandardFitness(targetLengthSeconds, maxLengthSeconds),
            "standard-v3" => new StandardFitnessV3(targetLengthSeconds, maxLengthSeconds,
                collisionScalar: collisionScalar ?? StandardFitnessV3.DefaultCollisionScalar),
            var other => throw new ArgumentException($"Unknown fitness '{other}' (standard-v2|standard-v3)."),
        };
}
