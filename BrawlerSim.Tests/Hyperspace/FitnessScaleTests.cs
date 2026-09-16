using BrawlerSim.Hyperspace;

namespace BrawlerSim.Tests.Hyperspace;

public class FitnessScaleTests
{
    [Fact]
    public void PercentileRankSpansTheFullRangeWhateverTheScores()
    {
        // Raw scores in the hundreds and raw scores in the tenths must both fill the
        // sky the same way — the view compares WITHIN one archive, never across two.
        var big = new FitnessScale(new[] { -20f, 5f, 40f, 88f, 121f });
        var small = new FitnessScale(new[] { 0.01f, 0.02f, 0.03f, 0.04f, 0.05f });
        Assert.Equal(big.Normalize(-20f), small.Normalize(0.01f), 0.001f);
        Assert.Equal(big.Normalize(121f), small.Normalize(0.05f), 0.001f);
        Assert.True(big.Normalize(121f) > 0.85f);
        Assert.True(big.Normalize(-20f) < 0.15f);
    }

    [Fact]
    public void RankIsMonotoneAndTiesShareAValue()
    {
        var scale = new FitnessScale(new[] { 1f, 2f, 2f, 2f, 9f });
        Assert.True(scale.Normalize(1f) < scale.Normalize(2f));
        Assert.True(scale.Normalize(2f) < scale.Normalize(9f));
        Assert.Equal(scale.Normalize(2f), scale.Normalize(2f), 0.0001f);
        Assert.InRange(scale.Normalize(0f), 0f, 0.001f);   // below everything
        Assert.InRange(scale.Normalize(50f), 0.999f, 1f);  // above everything
    }

    [Fact]
    public void DegenerateArchivesStillRender()
    {
        Assert.Equal(0.5f, new FitnessScale(Array.Empty<float>()).Normalize(7f));
        Assert.Equal(0.5f, new FitnessScale(new[] { 7f }).Normalize(7f));       // lone star
        Assert.Equal(0.5f, new FitnessScale(new[] { 3f, 3f, 3f }).Normalize(3f)); // all equal
    }
}
