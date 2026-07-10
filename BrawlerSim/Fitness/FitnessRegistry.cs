namespace BrawlerSim.Fitness;

/// <summary>
/// Name → fitness function. run.json records the name a run was scored with; resuming
/// MUST reconstruct that exact version (a resumed pre-v3 run keeps standard-v2), so
/// every version ever shipped stays constructible here.
/// </summary>
public static class FitnessRegistry
{
    public const string DefaultName = "standard-v3";

    public static IFitnessFunction Create(string? name, float targetLengthSeconds, float maxLengthSeconds) =>
        (name ?? DefaultName) switch
        {
            "standard-v2" => new StandardFitness(targetLengthSeconds, maxLengthSeconds),
            "standard-v3" => new StandardFitnessV3(targetLengthSeconds, maxLengthSeconds),
            var other => throw new ArgumentException($"Unknown fitness '{other}' (standard-v2|standard-v3)."),
        };
}
