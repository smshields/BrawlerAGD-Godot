using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// Four Player Support (2026-08-12, FEATURES.md §Game Menu / Four Player Support;
/// docs/features/four-player.md): 2-4 players per match, STOCK elimination
/// (last man standing), TIMED ranking (KOs → damage dealt → index), and
/// last-influencer KO attribution ("until next landing") with self-destruct counting.
/// 2P bit-exactness is guarded by the untouched match/utility goldens, not here.
/// </summary>
public class FourPlayerTests
{
    private static void TickNeutral(SimWorld world, int ticks)
    {
        Span<InputFrame> frame = stackalloc InputFrame[world.Players.Count];
        frame.Fill(InputFrame.Neutral);
        for (int i = 0; i < ticks; i++)
        {
            world.Tick(frame);
        }
    }

    /// <summary>Park a player far outside the blast zone so the next tick kills it.</summary>
    private static void Exile(SimPlayer player)
    {
        player.Position = new Vec2(0f, -100f);
        player.Velocity = Vec2.Zero;
    }

    [Fact]
    public void FourPlayersSpawnAtTheFourStageSpawnGenes()
    {
        GameGenome genome = TestGames.FlatArenaN(4);
        var world = new SimWorld(genome);
        Assert.Equal(4, world.Players.Count);
        for (int i = 0; i < 4; i++)
        {
            Vec2 expected = StageRules.LegacySafeSpawn(
                StageRules.SpawnOf(genome.Stage.Params, i), genome.Stage.Platforms);
            Assert.Equal(expected.X, world.Players[i].Position.X);
            Assert.Equal(expected.Y, world.Players[i].Position.Y);
        }
    }

    [Fact]
    public void StockEliminationRunsTheMatchUntilOneRemains()
    {
        var world = new SimWorld(TestGames.FlatArenaN(3));
        TickNeutral(world, 60); // settle on the floor

        // Third player dies at 0 stocks: eliminated, match CONTINUES (two live).
        world.Players[2].Stocks = 0;
        Exile(world.Players[2]);
        TickNeutral(world, 1);
        Assert.True(world.Players[2].Eliminated);
        Assert.False(world.IsOver);
        TickNeutral(world, 30); // the survivors keep simulating
        Assert.False(world.IsOver);

        // Second player dies at 0 stocks: one remains — last man standing.
        world.Players[1].Stocks = 0;
        Exile(world.Players[1]);
        TickNeutral(world, 1);
        Assert.True(world.IsOver);
        Assert.Equal(new[] { 2, 1 }, world.EliminationOrder);
        // The loser is the FIRST eliminated player (last place).
        Assert.Equal(2, world.LoserIndex);

        MatchResult result = world.BuildResult();
        Assert.Equal(new[] { 1, 2, 3 }, result.Placements); // p0 wins, p1 second, p2 last
    }

    [Fact]
    public void StockTimeoutWithAnEliminationRanksTheEliminatedLast()
    {
        var config = MatchConfig.Default with { MaxMatchSeconds = 2f };
        var world = new SimWorld(TestGames.FlatArenaN(3), config);
        TickNeutral(world, 30);

        world.Players[1].Stocks = 0;
        Exile(world.Players[1]);
        TickNeutral(world, 1);
        Assert.True(world.Players[1].Eliminated);

        // Survivors tie on stocks; damage taken breaks the survivor tie.
        world.Players[2].TotalDamageTaken = 10f;
        TickNeutral(world, config.MaxTicks); // run out the clock
        Assert.True(world.IsOver);
        Assert.Equal(1, world.LoserIndex); // first (only) eliminated player

        MatchResult result = world.BuildResult();
        Assert.Equal(new[] { 1, 3, 2 }, result.Placements);
    }

    [Fact]
    public void TimedMatchNeverEliminatesAndKeepsStocks()
    {
        var config = MatchConfig.Default with
        {
            EndRule = MatchEndRule.Timed,
            MaxMatchSeconds = 1f,
        };
        var world = new SimWorld(TestGames.FlatArenaN(3), config);
        TickNeutral(world, 10);

        int stocksBefore = world.Players[1].Stocks;
        Exile(world.Players[1]);
        TickNeutral(world, 1);
        Assert.False(world.Players[1].Eliminated);   // infinite respawns
        Assert.False(world.IsOver);
        Assert.Equal(stocksBefore, world.Players[1].Stocks); // the decrement is restored
        Assert.Equal(1, world.Players[1].SelfDestructs);     // untouched fall = SD
    }

    [Fact]
    public void TimedMatchRanksByKOsThenDamageDealtThenIndex()
    {
        var config = MatchConfig.Default with
        {
            EndRule = MatchEndRule.Timed,
            MaxMatchSeconds = 1f,
        };
        var world = new SimWorld(TestGames.FlatArenaN(4), config);

        // Outcome stats set directly — the ranking keys are what is under test.
        world.Players[0].KOs = 1;
        world.Players[1].KOs = 1;
        world.Players[1].DamageDealt = 5f; // beats p0 on the damage-dealt tiebreak
        world.Players[2].KOs = 2;          // outright first
        world.Players[3].KOs = 0;          // last

        TickNeutral(world, config.MaxTicks);
        Assert.True(world.IsOver);
        Assert.Equal(3, world.LoserIndex); // last place

        MatchResult result = world.BuildResult();
        Assert.Equal(new[] { 3, 2, 1, 4 }, result.Placements);
    }

    // ----- KO attribution (designer, 2026-08-12): hit/push influence, "until next
    // ----- landing" (continuous grounding), self-destructs otherwise. --------------

    private static SimWorld ArrangeDuel(out SimPlayer attacker, out SimPlayer victim)
    {
        var world = new SimWorld(TestGames.FlatArena());
        attacker = world.Players[0];
        victim = world.Players[1];
        attacker.Position = new Vec2(-4f, -1.4f);
        victim.Position = new Vec2(-3f, -1.4f);
        TickNeutral(world, 60); // settle
        return world;
    }

    private static void RunAttack(SimWorld world, int followTicks)
    {
        Span<InputFrame> frame = stackalloc[]
        {
            new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)),
            InputFrame.Neutral,
        };
        world.Tick(frame);
        frame[0] = InputFrame.Neutral;
        for (int i = 0; i < followTicks; i++)
        {
            world.Tick(frame);
        }
    }

    [Fact]
    public void ADeathUnderLiveHitInfluenceCreditsTheAttackerKO()
    {
        SimWorld world = ArrangeDuel(out SimPlayer attacker, out SimPlayer victim);
        RunAttack(world, followTicks: 14); // wind-up (12) + hit; victim launched airborne
        Assert.Equal(0, victim.LastInfluencer);

        Exile(victim); // stocks > 0 — a respawn death, not an elimination
        TickNeutral(world, 1);
        Assert.Equal(1, attacker.KOs);
        Assert.Equal(0, victim.SelfDestructs);
        Assert.Equal(-1, victim.LastInfluencer); // reset for the new life
    }

    [Fact]
    public void AnUntouchedFallIsASelfDestruct()
    {
        SimWorld world = ArrangeDuel(out SimPlayer attacker, out SimPlayer victim);
        Exile(victim);
        TickNeutral(world, 1);
        Assert.Equal(0, attacker.KOs);
        Assert.Equal(1, victim.SelfDestructs);
    }

    [Fact]
    public void ContinuousGroundingClearsInfluenceSoALaterFallIsASelfDestruct()
    {
        SimWorld world = ArrangeDuel(out SimPlayer attacker, out SimPlayer victim);
        RunAttack(world, followTicks: 14);
        Assert.Equal(0, victim.LastInfluencer);

        // Let the victim land and stand: influence must clear after the grounded
        // window (bounded wait — landing time depends on knockback arc).
        bool cleared = false;
        for (int i = 0; i < 600 && !cleared; i++)
        {
            TickNeutral(world, 1);
            cleared = victim.LastInfluencer == -1;
        }
        Assert.True(cleared, "influence never cleared after landing");

        Exile(victim);
        TickNeutral(world, 1);
        Assert.Equal(0, attacker.KOs);
        Assert.Equal(1, victim.SelfDestructs);
    }

    [Fact]
    public void AMomentumTransferringBodyPushCountsAsInfluence()
    {
        var world = new SimWorld(TestGames.FlatArenaN(2));
        SimPlayer pusher = world.Players[0];
        SimPlayer pushed = world.Players[1];
        pusher.Position = new Vec2(-4f, -1.4f);
        pushed.Position = new Vec2(-3f, -1.4f);
        TickNeutral(world, 60);

        // Pusher walks right into the other body: the axis clamp transfers momentum.
        Span<InputFrame> frame = stackalloc[]
        {
            new InputFrame(1f, 0f, false, 0),
            InputFrame.Neutral,
        };
        for (int i = 0; i < 30; i++)
        {
            world.Tick(frame);
        }
        Assert.Equal(0, pushed.LastInfluencer);

        Exile(pushed);
        TickNeutral(world, 1);
        Assert.Equal(1, pusher.KOs);
        Assert.Equal(0, pushed.SelfDestructs);
    }

    [Fact]
    public void DamageDealtRecordsTheAttackersOutput()
    {
        SimWorld world = ArrangeDuel(out SimPlayer attacker, out SimPlayer victim);
        RunAttack(world, followTicks: 30);
        // damageGiven = 5 + (0.2 + 0.1 + 0.2)·5 = 7.5, exactly one hit.
        Assert.Equal(7.5f, attacker.DamageDealt, 0.0001f);
        Assert.Equal(victim.TotalDamageTaken, attacker.DamageDealt, 0.0001f);
    }

    [Fact]
    public void FourPlayerMatchReplaysBitIdentically()
    {
        GameGenome genome = TestGames.FlatArenaN(4);
        var config = MatchConfig.Default with { MaxMatchSeconds = 3f };
        // Distinct deterministic scripts per player: movement, hops, and attack
        // pulses that guarantee contacts, hits, knockbacks, and respawns.
        IInputSource Script(int index) => new ScriptedSource(tick =>
        {
            float h = (index % 2 == 0 ? 1f : -1f) * ((tick / 40) % 2 == 0 ? 1f : -1f);
            bool jump = (tick + index * 7) % 90 < 2;
            byte actions = (tick + index * 13) % 50 < 1 ? InputFrame.ActionBit(0) : (byte)0;
            return new InputFrame(h, 0f, jump, actions);
        });
        var sources = new[] { Script(0), Script(1), Script(2), Script(3) };

        MatchResult live = MatchRunner.Run(genome, sources, config, recordTrace: true);
        Assert.NotNull(live.Trace);
        MatchResult replay = MatchRunner.Replay(genome, live.Trace!, config);
        Assert.Equal(live.FinalHash, replay.FinalHash);
        Assert.Equal(live.Ticks, replay.Ticks);
        Assert.Equal(live.Placements, replay.Placements);

        // The trace survives its JSON round-trip at 4-wide rows too.
        BrawlerSim.Replay.InputTrace reloaded = BrawlerSim.Replay.InputTraceJson.Deserialize(
            BrawlerSim.Replay.InputTraceJson.Serialize(live.Trace!));
        Assert.Equal(4, reloaded.PlayerCount);
        Assert.Equal(live.FinalHash, MatchRunner.Replay(genome, reloaded, config).FinalHash);
    }
}
