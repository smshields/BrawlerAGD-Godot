using BrawlerSim.Determinism;
using Xunit;

namespace BrawlerSim.Tests.Determinism;

public class Pcg32Tests
{
    /// <summary>
    /// Golden values from the PCG reference implementation (pcg32-demo, seed 42, seq 54).
    /// If this test ever fails on any platform or .NET version, cross-machine replay
    /// determinism is broken and must be treated as a release blocker.
    /// </summary>
    [Fact]
    public void MatchesReferenceImplementation()
    {
        var rng = new Pcg32(42, 54);
        Assert.Equal(0xa15c02b7u, rng.NextUInt());
        Assert.Equal(0x7b47f409u, rng.NextUInt());
        Assert.Equal(0xba1d3330u, rng.NextUInt());
        Assert.Equal(0x83d2f293u, rng.NextUInt());
    }

    [Fact]
    public void SameSeedProducesIdenticalSequences()
    {
        var a = new Pcg32(123456789, 7);
        var b = new Pcg32(123456789, 7);
        for (int i = 0; i < 10_000; i++)
        {
            Assert.Equal(a.NextUInt(), b.NextUInt());
        }
    }

    [Fact]
    public void DifferentSequencesDiverge()
    {
        var a = new Pcg32(1, 0);
        var b = new Pcg32(1, 1);
        bool anyDifferent = false;
        for (int i = 0; i < 100; i++)
        {
            if (a.NextUInt() != b.NextUInt())
            {
                anyDifferent = true;
                break;
            }
        }
        Assert.True(anyDifferent);
    }

    [Fact]
    public void NextFloatStaysInUnitInterval()
    {
        var rng = new Pcg32(99);
        for (int i = 0; i < 10_000; i++)
        {
            float f = rng.NextFloat();
            Assert.InRange(f, 0f, 0.99999994f);
        }
    }

    [Fact]
    public void NextIntStaysInBounds()
    {
        var rng = new Pcg32(7);
        for (int i = 0; i < 10_000; i++)
        {
            Assert.InRange(rng.NextInt(12), 0, 11);
        }
    }
}
