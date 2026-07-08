using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Replay;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Replay;

public class InputTraceJsonTests
{
    [Fact]
    public void SerializedTraceReplaysBitExactly()
    {
        var genome = LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", "GameA")).Genome;
        var sources = new IInputSource[]
        {
            new DecisionTreeAgent(new Pcg32(5, 0)),
            new DecisionTreeAgent(new Pcg32(5, 1)),
        };
        MatchResult live = MatchRunner.Run(genome, sources, recordTrace: true);

        InputTrace roundTripped = InputTraceJson.Deserialize(InputTraceJson.Serialize(live.Trace!));
        Assert.Equal(live.Trace!.TickCount, roundTripped.TickCount);

        MatchResult replayed = MatchRunner.Replay(genome, roundTripped);
        Assert.Equal(live.FinalHash, replayed.FinalHash);
    }

    [Fact]
    public void SeedMixProducesDistinctDeterministicStreams()
    {
        Assert.Equal(SeedMix.MatchSeed(1, 2, 3, 4), SeedMix.MatchSeed(1, 2, 3, 4));
        var seen = new HashSet<ulong>();
        for (int gen = 0; gen < 10; gen++)
        {
            for (int idx = 0; idx < 10; idx++)
            {
                for (int round = 0; round < 3; round++)
                {
                    Assert.True(seen.Add(SeedMix.MatchSeed(42, gen, idx, round)),
                        $"seed collision at gen {gen}, idx {idx}, round {round}");
                }
            }
        }
    }
}
