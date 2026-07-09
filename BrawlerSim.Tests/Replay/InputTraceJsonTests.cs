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
    public void NewFormatRoundTripsVerticalAndAllActionButtons()
    {
        var trace = new InputTrace();
        trace.Record(stackalloc[]
        {
            new InputFrame(0.5f, -1f, true, (byte)(InputFrame.ActionBit(1) | InputFrame.ActionBit(3))),
            new InputFrame(-1f, 1f, false, InputFrame.ActionBit(0)),
        });
        trace.Record(stackalloc[] { InputFrame.Neutral, new InputFrame(0f, 0f, false, InputFrame.ActionBit(2)) });

        InputTrace loaded = InputTraceJson.Deserialize(InputTraceJson.Serialize(trace));
        Assert.Equal(trace.TickCount, loaded.TickCount);
        for (int t = 0; t < trace.TickCount; t++)
        {
            for (int p = 0; p < 2; p++)
            {
                Assert.Equal(trace.Get(t, p), loaded.Get(t, p));
            }
        }
    }

    [Fact]
    public void LegacyThreeValueRowsUpgradeToActionButtonZero()
    {
        // Pre-2026-07-08 traces are research artifacts: (h, jump, attack) rows must keep
        // loading, with attack becoming action button 0 and vertical defaulting to 0.
        InputTrace trace = InputTraceJson.Deserialize(
            """{"Players":2,"Ticks":[[0.5,1,1, -1,0,0],[0,0,0, 1,1,1]]}""");

        Assert.Equal(2, trace.TickCount);
        Assert.Equal(new InputFrame(0.5f, 0f, true, InputFrame.ActionBit(0)), trace.Get(0, 0));
        Assert.Equal(new InputFrame(-1f, 0f, false, 0), trace.Get(0, 1));
        Assert.Equal(InputFrame.Neutral, trace.Get(1, 0));
        Assert.Equal(new InputFrame(1f, 0f, true, InputFrame.ActionBit(0)), trace.Get(1, 1));
    }

    [Fact]
    public void LegacyTracesReplayToTheSameFinalStateAsTheLiveMatch()
    {
        // End-to-end proof that the format upgrade preserves behavior: record a live
        // match, down-convert its trace to the OLD 3-value format (the agent only
        // presses button 0, so the old format can express it), reload, replay.
        var genome = LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", "GameC")).Genome;
        var sources = new IInputSource[]
        {
            new DecisionTreeAgent(new Pcg32(42, 0)),
            new DecisionTreeAgent(new Pcg32(42, 1)),
        };
        MatchResult live = MatchRunner.Run(genome, sources, recordTrace: true);

        var rows = new List<string>();
        for (int t = 0; t < live.Trace!.TickCount; t++)
        {
            var row = new List<string>();
            for (int p = 0; p < 2; p++)
            {
                InputFrame f = live.Trace.Get(t, p);
                Assert.True(f.Actions == 0 || f.Actions == InputFrame.ActionBit(0));
                row.Add($"{f.Horizontal.ToString(System.Globalization.CultureInfo.InvariantCulture)}," +
                        $"{(f.Jump ? 1 : 0)},{(f.ActionPressed(0) ? 1 : 0)}");
            }
            rows.Add($"[{string.Join(",", row)}]");
        }
        string legacyJson = $$"""{"Players":2,"Ticks":[{{string.Join(",", rows)}}]}""";

        MatchResult replayed = MatchRunner.Replay(genome, InputTraceJson.Deserialize(legacyJson));
        Assert.Equal(live.FinalHash, replayed.FinalHash);
        Assert.Equal(live.Ticks, replayed.Ticks);
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
