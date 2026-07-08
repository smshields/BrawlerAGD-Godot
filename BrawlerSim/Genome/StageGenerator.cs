using BrawlerSim.Determinism;

namespace BrawlerSim.Genome;

/// <summary>
/// Procedural stage generation, ported from the Unity MapGenerator: grow a platform tree
/// from an initial platform (Left / Above children, coin-flip each), then mirror the
/// whole set across x = 0 for a symmetrical stage.
///
/// One deliberate fix vs. Unity: the original sized Above-children with
/// `xSize = rand.Next(2, platform.xSize - x + 1)` using ABSOLUTE x, so children of
/// negative-x parents could be wider than the parent (the intent was clearly
/// parent-relative). This port uses the parent-relative form. Flagged in
/// docs/CONVERSION_PLAN.md; revert by switching back to the absolute expression.
/// </summary>
public sealed class StageGenerator
{
    public const int MinWidth = 1;
    public const int MaxWidth = 2;
    public const int InitialY = -3;

    private readonly int _jumpHeight;
    private readonly int _jumpLength;
    private readonly int _platformCount;
    private readonly int _maxPlatformSize;

    public StageGenerator(int jumpHeight, int jumpLength, int platformCount, int maxPlatformSize)
    {
        _jumpHeight = jumpHeight;
        _jumpLength = jumpLength;
        _platformCount = platformCount;
        _maxPlatformSize = maxPlatformSize;
    }

    public StageGenome Generate(Pcg32 rng)
    {
        var all = new List<PlatformGene>();
        var stack = new Stack<PlatformGene>();

        PlatformGene initial = Initial(rng);
        all.Add(initial);
        stack.Push(initial);

        while (all.Count < _platformCount && stack.Count > 0)
        {
            PlatformGene top = stack.Pop();
            if (rng.NextInt(2) == 1)
            {
                PlatformGene left = Left(top, rng);
                stack.Push(left);
                all.Add(left);
            }
            if (rng.NextInt(2) == 1)
            {
                PlatformGene above = Above(top, rng);
                stack.Push(above);
                all.Add(above);
            }
        }

        // Mirror everything across x = 0 for symmetry.
        int unmirrored = all.Count;
        for (int i = 0; i < unmirrored; i++)
        {
            all.Add(all[i].MirrorX());
        }
        return new StageGenome(all);
    }

    private PlatformGene Initial(Pcg32 rng)
    {
        int ySize = rng.NextInt(MinWidth, MaxWidth + 1);
        int x = rng.NextInt(-_maxPlatformSize - 1, -2);
        int midGap = _jumpLength / 2;
        int xSize = -x + rng.NextInt(-midGap, 0);
        return new PlatformGene(x, InitialY, xSize, ySize);
    }

    private PlatformGene Above(PlatformGene parent, Pcg32 rng)
    {
        int platformTop = parent.Y + parent.YSize;
        int y = platformTop + rng.NextInt(2, _jumpHeight + 1);
        int ySize = rng.NextInt(MinWidth, Math.Min(MaxWidth, y - platformTop));
        int x = rng.NextInt(parent.X + 1, parent.X + parent.XSize);
        int xSize = rng.NextInt(2, parent.XSize - (x - parent.X) + 1);
        return new PlatformGene(x, y, xSize, ySize);
    }

    private PlatformGene Left(PlatformGene parent, Pcg32 rng)
    {
        int y = rng.NextInt(parent.Y - _jumpHeight, parent.Y + _jumpHeight + 1);
        int ySize = rng.NextInt(MinWidth, MaxWidth + 1);
        int xSize = rng.NextInt(2, _maxPlatformSize);
        int xRight = rng.NextInt(1, _jumpLength);
        int x = parent.X - xRight - xSize;
        return new PlatformGene(x, y, xSize, ySize);
    }
}
