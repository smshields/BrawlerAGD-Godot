using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// The dash move type (FEATURES.md §Dash / docs/features/dash.md). Test dash: wind-up
/// 0.1 s (6 ticks), speed 12 u/s, duration 0.2 s (12 ticks) → travel 2.4 u; both
/// invulnerability stages configurable per test.
/// </summary>
public class DashTests
{
    private static ParamSet DashSet(float warmInv = 0f, float durInv = 0f, params (string Key, float Value)[] overrides)
    {
        var values = new Dictionary<string, float>
        {
            [DashParams.WindUpDuration] = 0.1f,
            [DashParams.Acceleration] = 12f,
            [DashParams.Duration] = 0.2f,
            [DashParams.WarmUpInvulnerable] = warmInv,
            [DashParams.DurationInvulnerable] = durInv,
        };
        foreach ((string key, float value) in overrides)
        {
            values[key] = value;
        }
        return ParamSet.FromDictionary(DefaultSchemas.Dash, values);
    }

    /// <summary>Floor arena; slot 0 = attack (buttons 0–2), slot 1 = dash (button 3).</summary>
    private static GameGenome DashArena(float warmInv = 0f, float durInv = 0f)
    {
        var moves = new[]
        {
            new MoveGenome(TestGames.Move(), 0),
            new MoveGenome(DashSet(warmInv, durInv), 0, MoveType.Dash),
        };
        CharacterGenome Make(string name) =>
            new(name, 3, 0, TestGames.Character(), moves, new[] { 0, 0, 0, 1 });
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { Make("P1"), Make("P2") }, stage);
    }

    private static readonly InputFrame PressDash = new(0f, 0f, false, InputFrame.ActionBit(3));

    /// <summary>Travel 0.35 s (21 ticks) so the whole opposing execute window falls
    /// inside the dash for the i-frame test.</summary>
    private static GameGenome DashArenaLongTravel(float durInv)
    {
        var moves = new[]
        {
            new MoveGenome(TestGames.Move(), 0),
            new MoveGenome(DashSet(0f, durInv, (DashParams.Duration, 0.35f)), 0, MoveType.Dash),
        };
        CharacterGenome Make(string name) =>
            new(name, 3, 0, TestGames.Character(), moves, new[] { 0, 0, 0, 1 });
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { Make("P1"), Make("P2") }, stage);
    }

    private static SimWorld Grounded(GameGenome genome, float p0X, float p1X)
    {
        var world = new SimWorld(genome);
        world.Players[0].Position = new Vec2(p0X, -1.4f);
        world.Players[1].Position = new Vec2(p1X, -1.4f);
        for (int i = 0; i < 120 && !(world.Players[0].IsGrounded && world.Players[1].IsGrounded); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        return world;
    }

    private static void Tick(SimWorld world, int ticks, InputFrame p0)
    {
        for (int t = 0; t < ticks; t++)
        {
            world.Tick(stackalloc[] { p0, InputFrame.Neutral });
        }
    }

    [Fact]
    public void DashTravelsAStraightLockedLine()
    {
        SimWorld world = Grounded(DashArena(), -6f, 6f);
        SimPlayer p = world.Players[0];
        Tick(world, 1, PressDash);
        Assert.Equal(PlayerState.Dash, p.State);
        Assert.Equal(DashStage.WarmUp, p.DashPhase);
        Assert.Equal(1, p.DashCount);

        // Hold RIGHT through warm-up end: direction captured at travel start.
        Tick(world, 6, new InputFrame(1f, 0f, false, InputFrame.ActionBit(3)));
        Assert.Equal(DashStage.Travel, p.DashPhase);
        float startX = p.Position.X;
        // Mid-travel: inputs (including reverse) are ignored; speed stays locked.
        Tick(world, 6, new InputFrame(-1f, 0f, false, 0));
        Assert.Equal(12f, p.Velocity.X, 0.01f);
        Tick(world, 7, InputFrame.Neutral);
        Assert.NotEqual(PlayerState.Dash, p.State);
        // ~12 travel ticks × 0.2 u = 2.4 u.
        Assert.InRange(p.Position.X - startX, 2.0f, 2.6f);
    }

    [Fact]
    public void NeutralDashGoesTheWayTheCharacterFaces()
    {
        SimWorld world = Grounded(DashArena(), -2f, -6f); // opponent LEFT
        SimPlayer p = world.Players[0];
        Tick(world, 2, new InputFrame(-1f, 0f, false, 0)); // face left
        Assert.Equal(-1, p.Facing);
        Tick(world, 1, PressDash);
        Tick(world, 6, PressDash); // neutral through warm-up end
        Assert.Equal(DashStage.Travel, p.DashPhase);
        Assert.True(p.Velocity.X < -11f, "dash did not follow facing");
    }

    [Fact]
    public void UpwardDashLiftsOffTheGroundWithGravitySuspended()
    {
        SimWorld world = Grounded(DashArena(), -6f, 6f);
        SimPlayer p = world.Players[0];
        Tick(world, 1, PressDash);
        Tick(world, 6, new InputFrame(0f, 1f, false, InputFrame.ActionBit(3))); // hold UP
        Assert.Equal(DashStage.Travel, p.DashPhase);
        float y0 = p.Position.Y;
        Tick(world, 6, InputFrame.Neutral);
        Assert.Equal(12f, p.Velocity.Y, 0.01f); // no gravity sag mid-travel
        Assert.True(p.Position.Y > y0 + 1f, "upward dash did not lift");
        Assert.False(p.IsGrounded);
    }

    [Fact]
    public void AirBudgetAllowsAllThreeOrderingsThenExhausts()
    {
        // jump - jump - dash: after both jumps (AirJumpsExhausted) the dash still fires;
        // afterwards the character is fully exhausted until landing.
        SimWorld world = Grounded(DashArena(), -6f, 6f);
        SimPlayer p = world.Players[0];
        Tick(world, 1, new InputFrame(0f, 0f, true, 0));
        Tick(world, 2, InputFrame.Neutral);
        Tick(world, 1, new InputFrame(0f, 0f, true, 0));
        Assert.Equal(PlayerState.AirJumpsExhausted, p.State);
        Tick(world, 1, PressDash);
        Assert.Equal(PlayerState.Dash, p.State); // the third air action
        Assert.True(p.AirDashUsed);

        // dash - jump - jump: land first (reset), then dash upward, then both jumps.
        for (int i = 0; i < 600 && !(p.IsGrounded && p.State == PlayerState.Idle); i++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.False(p.AirDashUsed); // grounding reset
        Tick(world, 1, PressDash);
        Tick(world, 6, new InputFrame(0f, 1f, false, InputFrame.ActionBit(3)));
        Tick(world, 13, InputFrame.Neutral); // travel out, now airborne
        Assert.False(p.IsGrounded);
        Tick(world, 1, new InputFrame(0f, 0f, true, 0)); // air jump 1 (ground jump unused → this is the air jump)
        Assert.Equal(PlayerState.AirJumpsExhausted, p.State);
        // Dash spent + jumps spent: nothing else fires.
        Tick(world, 1, PressDash);
        Assert.Equal(PlayerState.AirJumpsExhausted, p.State);
    }

    [Fact]
    public void DurationInvulnerabilityNegatesHitsAndCountsThem()
    {
        SimWorld world = Grounded(DashArenaLongTravel(durInv: 1f), -1f, 0.2f);
        SimPlayer attacker = world.Players[1];
        SimPlayer dasher = world.Players[0];
        // Attacker (P1) swings; P0 dashes THROUGH the execute window.
        Span<InputFrame> frame = stackalloc[]
            { PressDash, new InputFrame(-1f, 0f, false, 0) };
        world.Tick(frame); // P1 faces P0
        frame[1] = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(frame); // P1 warm-up starts (12 ticks), P0 dash warm-up (6)
        frame[1] = InputFrame.Neutral;
        frame[0] = new InputFrame(1f, 0f, false, InputFrame.ActionBit(3));
        for (int t = 0; t < 24; t++)
        {
            world.Tick(frame);
            frame[0] = InputFrame.Neutral;
        }
        Assert.Equal(0, dasher.TotalHitsReceived);
        Assert.True(dasher.DashInvulnDodges >= 1, "i-frames never negated the swing");
    }

    [Fact]
    public void VulnerableDashesStillGetHit()
    {
        SimWorld world = Grounded(DashArena(), -1f, 0.2f); // both invuln flags off
        SimPlayer dasher = world.Players[0];
        Span<InputFrame> frame = stackalloc[]
            { PressDash, new InputFrame(-1f, 0f, false, 0) };
        world.Tick(frame);
        frame[1] = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(frame);
        frame[1] = InputFrame.Neutral;
        frame[0] = new InputFrame(1f, 0f, false, InputFrame.ActionBit(3));
        for (int t = 0; t < 24; t++)
        {
            world.Tick(frame);
            frame[0] = InputFrame.Neutral;
        }
        Assert.True(dasher.TotalHitsReceived >= 1);
        Assert.Equal(0, dasher.DashInvulnDodges);
    }

    [Fact]
    public void DashContactShovesButNeverLaunches()
    {
        SimWorld world = Grounded(DashArena(), -2.5f, -0.5f); // opponent in the path
        SimPlayer opponent = world.Players[1];
        Tick(world, 1, PressDash);
        Tick(world, 6, new InputFrame(1f, 0f, false, InputFrame.ActionBit(3)));
        float maxSpeedWhileDashing = 0f;
        for (int t = 0; t < 14; t++)
        {
            Tick(world, 1, InputFrame.Neutral);
            if (world.Players[0].IsDashTraveling)
            {
                maxSpeedWhileDashing = MathF.Max(maxSpeedWhileDashing, MathF.Abs(opponent.Velocity.X));
            }
        }
        Assert.True(opponent.Position.X > -0.5f, "opponent was not shoved at all");
        Assert.True(maxSpeedWhileDashing <= MatchConfig.Default.DashContactPushCap + 0.01f,
            $"dash imparted {maxSpeedWhileDashing:F2} u/s — beyond the no-KO cap");
        // Post-travel, residual momentum is clamped to ordinary movement speed, so
        // the handoff cannot launch either (normal play reaches these speeds anyway).
        Assert.True(MathF.Abs(world.Players[0].Velocity.X) <= world.Players[0].MaxGroundSpeed + 0.01f);
    }

    [Fact]
    public void DashButtonIsPinnedThroughGenerationAndBreeding()
    {
        var config = GenerationConfig.Default; // 2 attacks + shield + dash
        var rng = new Pcg32(555);
        var population = new List<GameGenome>();
        for (int i = 0; i < 10; i++)
        {
            population.Add(GameGenome.Generate(config, rng));
        }
        for (int i = 0; i < 40; i++)
        {
            population.Add(GameGenomeOps.Breed(
                population[rng.NextInt(population.Count)],
                population[rng.NextInt(population.Count)], 1f, rng, config));
        }
        foreach (GameGenome g in population)
        {
            foreach (CharacterGenome c in g.Characters)
            {
                int last = c.Moves.Count - 1;
                Assert.Equal(MoveType.Dash, c.Moves[last].Type);
                Assert.Equal(last, c.ButtonMoves[InputFrame.ActionCount - 1]); // the pin
                for (int m = 0; m < last; m++)
                {
                    Assert.Contains(m, c.ButtonMoves.Take(3)); // others covered on 0–2
                }
            }
        }
    }

    [Fact]
    public void DashRoundTripsThroughV4AndV3FilesStillLoad()
    {
        var record = new GameRecord("dashy", "test", DashArena());
        GameRecord loaded = GameGenomeJson.Deserialize(GameGenomeJson.Serialize(record));
        Assert.Equal(MoveType.Dash, loaded.Genome.Characters[0].Moves[1].Type);
        Assert.Equal(GameGenomeJson.Serialize(record), GameGenomeJson.Serialize(loaded));

        string v3 = GameGenomeJson.Serialize(new GameRecord("legacy", null, TestGames.FlatArena()))
            .Replace($"\"formatVersion\": {GameGenomeJson.CurrentFormatVersion}", "\"formatVersion\": 3");
        Assert.All(GameGenomeJson.Deserialize(v3).Genome.Characters, c =>
            Assert.All(c.Moves, m => Assert.Equal(MoveType.Attack, m.Type)));
    }

    [Fact]
    public void AgentDashesToRecoverWhenJumpsAreSpent()
    {
        // Over the void, jumps exhausted, dash in hand: the greedy agent presses it
        // (the premier third-air-action recovery) with intent toward the platform.
        var world = new SimWorld(DashArena());
        SimPlayer self = world.Players[0];
        self.Position = new Vec2(-9.4f, 0.5f);
        self.JumpsExhausted = true;
        world.Players[1].Position = new Vec2(6f, -1.4f);
        world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        Assert.Equal(PlayerState.AirJumpsExhausted, self.State);

        var agent = new UtilityAgent(new Pcg32(1), new AgentConfig { Randomness = 0f, DecisionIntervalTicks = 1 });
        InputFrame input = agent.GetInput(world, 0);
        Assert.True(input.ActionPressed(3), "agent did not dash to recover");
    }

    [Fact]
    public void DashMatchesStayDeterministicAndTerminate()
    {
        var config = GenerationConfig.Default;
        var rng = new Pcg32(777);
        for (int i = 0; i < 4; i++)
        {
            GameGenome genome = GameGenome.Generate(config, rng);
            MatchResult a = Run(genome, 900 + (ulong)i);
            MatchResult b = Run(genome, 900 + (ulong)i);
            Assert.True(a.Ticks <= MatchConfig.Default.MaxTicks);
            Assert.Equal(a.FinalHash, b.FinalHash);
        }

        static MatchResult Run(GameGenome genome, ulong seed) =>
            MatchRunner.Run(genome, new IInputSource[]
            {
                AgentConfig.Default.CreateSource(new Pcg32(seed, 0)),
                AgentConfig.Default.CreateSource(new Pcg32(seed, 1)),
            });
    }
}
