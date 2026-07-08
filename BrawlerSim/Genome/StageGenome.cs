using BrawlerSim.Determinism;

namespace BrawlerSim.Genome;

/// <summary>One platform: integer grid rect, position = bottom-left corner.</summary>
public readonly record struct PlatformGene(int X, int Y, int XSize, int YSize)
{
    /// <summary>Mirror across x = 0 (Unity Platform.xMirror parity).</summary>
    public PlatformGene MirrorX() => this with { X = -X - XSize };
}

/// <summary>The stage segment of a game genome: an ordered platform list.</summary>
public sealed class StageGenome
{
    private readonly PlatformGene[] _platforms;

    public StageGenome(IEnumerable<PlatformGene> platforms)
    {
        _platforms = platforms.ToArray();
        if (_platforms.Length == 0)
        {
            throw new ArgumentException("A stage must have at least one platform.");
        }
    }

    public IReadOnlyList<PlatformGene> Platforms => _platforms;

    /// <summary>
    /// Single-point crossover on the platform lists, Unity parity: point is drawn in
    /// [0, min(lenA, lenB)); the child is a's platforms before the point followed by
    /// b's platforms from the point onward (child length = b's length unless a is shorter).
    /// </summary>
    public static StageGenome SinglePointCrossover(StageGenome a, StageGenome b, Pcg32 rng)
    {
        int point = rng.NextInt(Math.Min(a._platforms.Length, b._platforms.Length));
        var child = new List<PlatformGene>(point + b._platforms.Length - point);
        for (int i = 0; i < point; i++)
        {
            child.Add(a._platforms[i]);
        }
        for (int i = point; i < b._platforms.Length; i++)
        {
            child.Add(b._platforms[i]);
        }
        return new StageGenome(child);
    }
}
