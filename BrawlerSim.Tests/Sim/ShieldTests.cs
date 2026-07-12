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
/// The shield move type (FEATURES.md §Shield / docs/features/shield.md): FSM, timing,
/// degradation/regen, coverage blocking, spacing push, break stun, and serialization.
/// Test shield: windUp/coolDown 0.1 s (6 ticks), diameter 1.6 (radius 0.8), hold
/// degradation 0.3 u/s (0.005/tick), hit scalar 0.04, knockback reduction 0.8, push 2,
/// regen 0.3 u/s, break stun 1.0 s (60 ticks — far above the 0.25 s cap). Break radius
/// for an unscaled character: 1 × 0.2 = 0.2.
/// </summary>
public class ShieldTests
{
    private static ParamSet ShieldSet(params (string Key, float Value)[] overrides)
    {
        var values = new Dictionary<string, float>
        {
            [ShieldParams.WindUpDuration] = 0.1f,
            [ShieldParams.CoolDownDuration] = 0.1f,
            [ShieldParams.InitialSize] = 1.6f,
            [ShieldParams.HoldDegradationRate] = 0.3f,
            [ShieldParams.HitDegradationScalar] = 0.04f,
            [ShieldParams.KnockbackReduction] = 0.8f,
            [ShieldParams.SpacingPush] = 2f,
            [ShieldParams.RegenRate] = 0.3f,
            [ShieldParams.BreakStunDuration] = 1.0f,
        };
        foreach ((string key, float value) in overrides)
        {
            values[key] = value;
        }
        return ParamSet.FromDictionary(DefaultSchemas.Shield, values);
    }

    /// <summary>FlatArena floor; slot 0 = attack, slot 1 = shield; buttons [0,0,1,1]
    /// (button 2 raises the shield).</summary>
    private static GameGenome ShieldArena(params (string Key, float Value)[] shieldOverrides)
    {
        var moves = new[]
        {
            new MoveGenome(TestGames.Move(), 0),
            new MoveGenome(ShieldSet(shieldOverrides), 0, MoveType.Shield),
        };
        CharacterGenome Make(string name) =>
            new(name, 3, 0, TestGames.Character(), moves, new[] { 0, 0, 1, 1 });
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { Make("P1"), Make("P2") }, stage);
    }

    private static readonly InputFrame HoldShield = new(0f, 0f, false, InputFrame.ActionBit(2));

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

    private static void Hold(SimWorld world, int ticks, InputFrame p0)
    {
        for (int t = 0; t < ticks; t++)
        {
            world.Tick(stackalloc[] { p0, InputFrame.Neutral });
        }
    }

    [Fact]
    public void ShieldGrowsHoldsAndShrinksOnSchedule()
    {
        SimWorld world = Grounded(ShieldArena(), -4f, 6f);
        SimPlayer p = world.Players[0];

        Hold(world, 1, HoldShield);
        Assert.Equal(PlayerState.Shield, p.State);
        Assert.Equal(ShieldStage.Grow, p.ShieldPhase);
        Assert.Equal(1, p.ShieldActivations);

        Hold(world, 6, HoldShield); // wind-up = 6 ticks
        Assert.Equal(ShieldStage.Hold, p.ShieldPhase);
        Assert.Equal(0.8f, p.ShieldRadius, 0.05f); // full size (minor hold decay)

        Hold(world, 1, InputFrame.Neutral); // release → shrink
        Assert.Equal(ShieldStage.Shrink, p.ShieldPhase);
        Hold(world, 6, InputFrame.Neutral); // cool-down = 6 ticks
        Assert.Equal(PlayerState.Idle, p.State);
    }

    [Fact]
    public void ShieldCannotBeRaisedInTheAir()
    {
        SimWorld world = Grounded(ShieldArena(), -4f, 6f);
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, true, 0), InputFrame.Neutral }); // jump
        Assert.Equal(PlayerState.Air, world.Players[0].State);
        Hold(world, 1, HoldShield);
        Assert.NotEqual(PlayerState.Shield, world.Players[0].State); // press ignored
    }

    [Fact]
    public void HoldingDegradesUntilBreakAndBreakStunIgnoresTheCap()
    {
        SimWorld world = Grounded(ShieldArena(), -4f, 6f);
        SimPlayer p = world.Players[0];
        // Health 0.8 → break radius 0.2 at 0.005/tick from Hold start: 120 Hold ticks.
        Hold(world, 7 + 119, HoldShield); // 1 entry + 6 grow, then 119 hold ticks
        Assert.Equal(PlayerState.Shield, p.State);
        Hold(world, 2, HoldShield);
        Assert.Equal(PlayerState.Stun, p.State);
        Assert.True(p.StunFromShieldBreak);
        Assert.Equal(1, p.ShieldBreaks);
        Assert.Equal(0f, p.ShieldHealths[1]);
        // Break stun = 1.0 s = 60 ticks — far beyond the 0.25 s (15-tick) global cap.
        Assert.InRange(p.PhaseTicksLeft, 55, 60);
    }

    [Fact]
    public void ShieldRegeneratesFromCurrentHealthNotFresh()
    {
        SimWorld world = Grounded(ShieldArena(), -4f, 6f);
        SimPlayer p = world.Players[0];
        Hold(world, 7 + 60, HoldShield);       // burn 60 hold ticks ≈ 0.30 health
        float afterHold = p.ShieldHealths[1];
        Assert.InRange(afterHold, 0.45f, 0.55f);
        Hold(world, 1 + 6, InputFrame.Neutral); // release + shrink out
        Assert.Equal(PlayerState.Idle, p.State);
        Hold(world, 20, InputFrame.Neutral);    // regen 0.005/tick — includes shrink ticks too
        Assert.InRange(p.ShieldHealths[1], afterHold + 0.09f, afterHold + 0.15f);
        Assert.True(p.ShieldHealths[1] < 0.8f); // resumed, not reset to fresh
    }

    [Fact]
    public void CoveredHitsAreBlockedWithReducedKnockbackAndShieldDamage()
    {
        SimWorld world = Grounded(ShieldArena(), -1f, 0.2f);
        SimPlayer attacker = world.Players[0];
        SimPlayer victim = world.Players[1];

        // Victim raises + holds; attacker swings (warm-up 12, execute 6).
        Span<InputFrame> frame = stackalloc[]
            { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), HoldShield };
        world.Tick(frame);
        frame[0] = InputFrame.Neutral;
        float healthBefore = 0f;
        for (int t = 0; t < 30; t++)
        {
            if (t == 10) healthBefore = victim.ShieldHealths[1];
            world.Tick(frame);
        }

        Assert.Equal(1, victim.BlockedHits);
        Assert.Equal(0, victim.TotalHitsReceived);          // no damage through the shield
        Assert.Equal(0f, victim.TotalDamageTaken, 0.0001f);
        // damageGiven 7.5 × hitScalar 0.04 = 0.30 from the block, plus ~20 held ticks
        // of hold degradation (0.005/tick ≈ 0.10) between the measurement and the end.
        Assert.InRange(healthBefore - victim.ShieldHealths[1], 0.38f, 0.44f);
        Assert.NotEqual(PlayerState.Stun, victim.State);    // blocked hits never stun
    }

    [Fact]
    public void ATinyShieldLeavesTheBodyExposed()
    {
        // Diameter 0.7 → radius 0.35: the hitbox∩body corners fall outside → clean hit.
        SimWorld world = Grounded(ShieldArena((ShieldParams.InitialSize, 0.7f)), -1f, 0.2f);
        SimPlayer victim = world.Players[1];
        Span<InputFrame> frame = stackalloc[]
            { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), HoldShield };
        world.Tick(frame);
        frame[0] = InputFrame.Neutral;
        for (int t = 0; t < 30; t++)
        {
            world.Tick(frame);
        }
        Assert.Equal(1, victim.TotalHitsReceived); // hit went through
        Assert.Equal(0, victim.BlockedHits);
    }

    [Fact]
    public void ShieldSpacingExpelsTheOpponent()
    {
        SimWorld world = Grounded(ShieldArena(), -0.4f, 0.2f); // overlapping-ish start
        SimPlayer shielder = world.Players[0];
        SimPlayer opponent = world.Players[1];
        Hold(world, 40, HoldShield);
        Assert.Equal(PlayerState.Shield, shielder.State);
        // Opponent stands clear of the shield circle (radius ~0.8 + body half 0.37).
        float gap = MathF.Abs(opponent.Position.X - shielder.Position.X);
        Assert.True(gap > 0.9f, $"opponent still inside the shield (gap {gap:F2})");
    }

    [Fact]
    public void ShieldOffsetFollowsDirectionalInputAndStaysClamped()
    {
        SimWorld world = Grounded(ShieldArena(), -4f, 6f);
        SimPlayer p = world.Players[0];
        var aimRight = new InputFrame(1f, 1f, false, InputFrame.ActionBit(2));
        Hold(world, 7, HoldShield);
        Hold(world, 60, aimRight);
        Assert.True(p.ShieldOffset.X > 0.2f && p.ShieldOffset.Y > 0.2f, "offset did not follow input");
        // Edge never leaves the character's center: |offset| ≤ radius.
        Assert.True(p.ShieldOffset.Length() <= p.ShieldRadius + 0.0001f);
    }

    [Fact]
    public void ShieldRoundTripsThroughJsonAndOldFilesLoadAsAttacks()
    {
        var record = new GameRecord("shielded", "test", ShieldArena());
        GameRecord loaded = GameGenomeJson.Deserialize(GameGenomeJson.Serialize(record));
        Assert.Equal(MoveType.Shield, loaded.Genome.Characters[0].Moves[1].Type);
        Assert.Equal(GameGenomeJson.Serialize(record), GameGenomeJson.Serialize(loaded));

        // A v2-era file (no move types) must load as all-attacks.
        string v2 = GameGenomeJson.Serialize(new GameRecord("legacy", null, TestGames.FlatArena()))
            .Replace($"\"formatVersion\": {GameGenomeJson.CurrentFormatVersion}", "\"formatVersion\": 2")
            .Replace("      \"type\": \"attack\",\n", "");
        GameRecord legacy = GameGenomeJson.Deserialize(v2);
        Assert.All(legacy.Genome.Characters, c =>
            Assert.All(c.Moves, m => Assert.Equal(MoveType.Attack, m.Type)));
    }

    [Fact]
    public void AgentTradesOffShieldAndDodgeUnderThreat()
    {
        // Designer spec: shield-vs-dodge is a weighted-random choice. Across seeds,
        // BOTH outcomes must occur when threatened at full shield health — and with a
        // near-broken shield the agent must never raise it.
        int shielded = 0, dodged = 0;
        for (ulong seed = 0; seed < 30; seed++)
        {
            SimWorld world = Grounded(ShieldArena(), -1.2f, 0.8f);
            world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(-1f, 0f, false, 0) });
            world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) });
            Assert.Equal(PlayerState.WarmUp, world.Players[1].State);

            var agent = new UtilityAgent(new Pcg32(seed), new AgentConfig { Randomness = 0f, DecisionIntervalTicks = 1 });
            InputFrame input = agent.GetInput(world, 0);
            if (input.ActionPressed(2) || input.ActionPressed(3)) shielded++;
            else if (input.Jump) dodged++;
        }
        Assert.True(shielded >= 3, $"agent never shields under threat ({shielded})");
        Assert.True(dodged >= 3, $"agent never dodges under threat ({dodged})");
    }

    [Fact]
    public void AgentAvoidsRaisingANearlyBrokenShield()
    {
        for (ulong seed = 0; seed < 30; seed++)
        {
            SimWorld world = Grounded(ShieldArena(), -1.2f, 0.8f);
            world.Players[0].ShieldHealths[1] = 0.21f; // a sliver above the 0.2 break radius
            world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(-1f, 0f, false, 0) });
            world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) });

            var agent = new UtilityAgent(new Pcg32(seed), new AgentConfig { Randomness = 0f, DecisionIntervalTicks = 1 });
            InputFrame input = agent.GetInput(world, 0);
            Assert.False(input.ActionPressed(2) || input.ActionPressed(3),
                $"agent raised a nearly-broken shield (seed {seed})");
        }
    }

    [Fact]
    public void AgentHoldsTheShieldWhileThreatenedAndReleasesAfter()
    {
        SimWorld world = Grounded(ShieldArena(), -1.2f, 0.8f);
        SimPlayer self = world.Players[0];
        Hold(world, 7, HoldShield); // raise manually
        Assert.Equal(ShieldStage.Hold, self.ShieldPhase);

        var agent = new UtilityAgent(new Pcg32(1), new AgentConfig { Randomness = 0f, DecisionIntervalTicks = 1 });
        // Opponent adjacent (within hold range) → keep holding.
        Assert.True(agent.GetInput(world, 0).ActionPressed(2));
        // Opponent teleports far away → release.
        world.Players[1].Position = new Vec2(7f, -1.4f);
        Assert.False(agent.GetInput(world, 0).ActionPressed(2));
    }

    [Fact]
    public void ShieldMatchesStayDeterministicAndTerminate()
    {
        var config = GenerationConfig.Default; // includes the guaranteed shield slot
        var rng = new Pcg32(99);
        for (int i = 0; i < 4; i++)
        {
            GameGenome genome = GameGenome.Generate(config, rng);
            MatchResult a = Run(genome, 300 + (ulong)i);
            MatchResult b = Run(genome, 300 + (ulong)i);
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
