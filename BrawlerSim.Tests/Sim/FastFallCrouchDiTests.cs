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
/// Fast fall / crouch / directional influence (FEATURES.md, docs/features/
/// fastfall-crouch-di.md). Test character overrides per scenario; the TestGames
/// defaults keep all three mechanics OFF (neutral values).
/// </summary>
public class FastFallCrouchDiTests
{
    private static GameGenome Arena(params (string Key, float Value)[] overrides) =>
        TestGames.FlatArena(overrides);

    private static SimWorld Grounded(GameGenome genome, float p0X = -4f, float p1X = 6f)
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

    private static readonly InputFrame HoldDown = new(0f, -1f, false, 0);

    // ── Fast fall ──────────────────────────────────────────────────────────────

    [Fact]
    public void FastFallAcceleratesDescentOnlyWhileHoldingDown()
    {
        var genome = Arena((CharacterParams.FastFallAcceleration, 12f));
        SimWorld world = Grounded(genome);
        SimPlayer p = world.Players[0];
        // Jump, coast to just past apex, then compare one second of plain falling
        // against one second of held-down falling from the same state.
        Tick(world, 1, new InputFrame(0f, 0f, true, 0));
        Tick(world, 49, InputFrame.Neutral); // past apex (jump force 8, g 9.81)
        float vyPlain = p.Velocity.Y;
        Tick(world, 10, InputFrame.Neutral);
        float plainDrop = vyPlain - p.Velocity.Y;

        // Fresh identical world, held down over the same window.
        SimWorld world2 = Grounded(genome);
        SimPlayer q = world2.Players[0];
        Tick(world2, 1, new InputFrame(0f, 0f, true, 0));
        Tick(world2, 49, InputFrame.Neutral);
        Tick(world2, 10, HoldDown);
        float fastDrop = vyPlain - q.Velocity.Y;

        Assert.True(fastDrop > plainDrop + 1.0f,
            $"fast fall added no descent (plain {plainDrop:F2}, held {fastDrop:F2})");
        Assert.True(q.FastFallTicks >= 10);
    }

    [Fact]
    public void FastFallIsUnavailableGroundedAndDuringDashTravel()
    {
        var genome = Arena((CharacterParams.FastFallAcceleration, 12f));
        SimWorld world = Grounded(genome);
        Tick(world, 5, HoldDown); // grounded: down = crouch entry, never fast fall
        Assert.Equal(0, world.Players[0].FastFallTicks);
    }

    // ── Crouch ─────────────────────────────────────────────────────────────────

    [Fact]
    public void CrouchSinksHoldsSquishedAndRises()
    {
        // crouchSpeed 0.1 s = 6 ticks per stage; ratio 0.5.
        var genome = Arena((CharacterParams.CrouchHeightRatio, 0.5f));
        SimWorld world = Grounded(genome);
        SimPlayer p = world.Players[0];
        float fullTop = p.Body.Top;
        float feet = p.Body.Bottom;

        Tick(world, 1, HoldDown);
        Assert.Equal(PlayerState.Crouch, p.State);
        Assert.Equal(CrouchStage.Sink, p.CrouchPhase);
        Tick(world, 6, HoldDown);
        Assert.Equal(CrouchStage.Held, p.CrouchPhase);
        Assert.Equal(0.5f, p.CrouchScale, 0.001f);
        Assert.Equal(feet, p.Body.Bottom, 0.001f);          // feet planted
        Assert.True(p.Body.Top < fullTop - 0.4f);           // hurtbox visibly lower

        Tick(world, 1, InputFrame.Neutral);                 // release → rise
        Assert.Equal(CrouchStage.Rise, p.CrouchPhase);
        Tick(world, 6, InputFrame.Neutral);
        Assert.Equal(PlayerState.Idle, p.State);
        Assert.Equal(1f, p.CrouchScale, 0.001f);
    }

    [Fact]
    public void CrouchedMovementUsesTheScaledSpeed()
    {
        // crouchMoveSpeed 0.5 halves the max: walk crouched, never exceed half.
        var genome = Arena((CharacterParams.CrouchMoveSpeed, 0.5f));
        SimWorld world = Grounded(genome);
        SimPlayer p = world.Players[0];
        Tick(world, 7, HoldDown); // into Held
        Assert.Equal(CrouchStage.Held, p.CrouchPhase);
        Tick(world, 60, new InputFrame(1f, -1f, false, 0)); // crawl right, still down
        Assert.Equal(PlayerState.Crouch, p.State);
        Assert.InRange(p.Velocity.X, 0.5f, p.MaxGroundSpeed * 0.5f + 0.001f);
    }

    [Fact]
    public void CrouchSlideBrakesWithNegativeAcceleration()
    {
        var genome = Arena((CharacterParams.CrouchAccelerationChange, -8f));
        SimWorld world = Grounded(genome);
        SimPlayer p = world.Players[0];
        Tick(world, 7, HoldDown);
        p.Velocity = new Vec2(9f, 0f); // fast knockback-style slide
        Tick(world, 30, new InputFrame(0f, -1f, false, 0));
        Assert.True(p.Velocity.X < 6.5f, $"crouch did not brake the slide ({p.Velocity.X:F2})");
    }

    [Fact]
    public void CrouchSlideBoostCapsAtOneAndAHalfTimesGroundSpeed()
    {
        var genome = Arena((CharacterParams.CrouchAccelerationChange, 8f));
        SimWorld world = Grounded(genome);
        SimPlayer p = world.Players[0];
        Tick(world, 7, HoldDown);
        p.Velocity = new Vec2(3f, 0f);
        Tick(world, 180, new InputFrame(0f, -1f, false, 0)); // slide right, crouched
        Assert.True(p.Velocity.X <= p.MaxGroundSpeed * 1.5f + 0.01f,
            $"slide exceeded the cap ({p.Velocity.X:F2} vs {p.MaxGroundSpeed * 1.5f:F2})");
    }

    [Fact]
    public void ActionsFromCrouchQueueThroughTheUncancellableRise()
    {
        SimWorld world = Grounded(Arena(), -1f, 0.2f);
        SimPlayer p = world.Players[0];
        Tick(world, 7, HoldDown);
        Assert.Equal(CrouchStage.Held, p.CrouchPhase);

        // Attack press queues; the rise is input-deaf (release/jump presses ignored).
        Tick(world, 1, new InputFrame(0f, -1f, false, InputFrame.ActionBit(0)));
        Assert.Equal(CrouchStage.Rise, p.CrouchPhase);
        Tick(world, 3, new InputFrame(0f, 0f, true, 0)); // mid-rise jump press: deaf
        Assert.Equal(CrouchStage.Rise, p.CrouchPhase);
        Tick(world, 3, InputFrame.Neutral);
        Assert.Equal(PlayerState.WarmUp, p.State); // the queued attack fired at full size
        Assert.Equal(1f, p.CrouchScale, 0.001f);
    }

    [Fact]
    public void JumpFromCrouchAlsoQueuesThroughTheRise()
    {
        SimWorld world = Grounded(Arena());
        SimPlayer p = world.Players[0];
        Tick(world, 7, HoldDown);
        Tick(world, 1, new InputFrame(0f, -1f, true, 0)); // jump press while crouched
        Assert.Equal(CrouchStage.Rise, p.CrouchPhase);
        Tick(world, 6, InputFrame.Neutral);
        Assert.Equal(PlayerState.Air, p.State); // rose, THEN jumped
        Assert.True(p.Velocity.Y > 0f);
    }

    [Fact]
    public void AHitCancelsCrouchAtFullSize()
    {
        SimWorld world = Grounded(Arena(), -1f, 0.2f);
        SimPlayer victim = world.Players[0];
        Span<InputFrame> frame = stackalloc[]
            { HoldDown, new InputFrame(-1f, 0f, false, 0) };
        world.Tick(frame); // victim crouches; attacker faces them
        frame[1] = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(frame);
        frame[1] = InputFrame.Neutral;
        for (int t = 0; t < 30 && victim.State != PlayerState.Stun; t++)
        {
            world.Tick(frame);
        }
        Assert.Equal(PlayerState.Stun, victim.State);
        Assert.Equal(CrouchStage.None, victim.CrouchPhase);
        Assert.Equal(1f, victim.CrouchScale, 0.001f);
    }

    [Fact]
    public void DuckingUnderAHighArcAvoidsTheHitEntirely()
    {
        // Attack aimed at standing head height (moveAngle up-forward, offset high);
        // ratio 0.4 pulls the hurtbox top below the arc → whiff while crouched.
        var moves = new[] { new MoveGenome(TestGames.Move(
            (MoveParams.MoveAngle, 0.6f), (MoveParams.MoveDist, 1.2f)), 0) };
        CharacterGenome Make(string name, (string, float)[] o) =>
            new(name, 3, 0, TestGames.Character(o), moves, new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(new[]
        {
            Make("Ducker", new[] { (CharacterParams.CrouchHeightRatio, 0.4f) }),
            Make("Swinger", System.Array.Empty<(string, float)>()),
        }, new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        SimWorld world = Grounded(genome, -1f, 0.2f);
        SimPlayer ducker = world.Players[0];
        Span<InputFrame> frame = stackalloc[]
            { HoldDown, new InputFrame(-1f, 0f, false, 0) };
        world.Tick(frame);
        frame[1] = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
        world.Tick(frame);
        frame[1] = InputFrame.Neutral;
        for (int t = 0; t < 30; t++)
        {
            world.Tick(frame);
        }
        Assert.Equal(0, ducker.TotalHitsReceived); // the swing passed overhead
        Assert.Equal(PlayerState.Crouch, ducker.State);
    }

    // ── Directional influence ──────────────────────────────────────────────────

    private static Vec2 HitWithHeld(Vec2 held, float di, float reduction)
    {
        // The TestGames default move knocks back STRAIGHT UP (modX 0, modY 1), so
        // "against the hit" means holding DOWN. The held direction must not walk the
        // victim out of the attacker's 1-unit reach — vertical holds don't move them.
        var genome = Arena(
            (CharacterParams.DirectionalInfluence, di),
            (CharacterParams.DiKnockbackReduction, reduction));
        SimWorld world = Grounded(genome, 0.2f, -1f); // victim P0 right of attacker P1
        SimPlayer victim = world.Players[0];

        var victimFrame = new InputFrame(held.X, held.Y, false, 0);
        world.Tick(stackalloc[] { victimFrame, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) });
        Vec2 before = victim.Velocity;
        for (int t = 0; t < 30 && victim.TotalHitsReceived == 0; t++)
        {
            before = victim.Velocity;
            world.Tick(stackalloc[] { victimFrame, InputFrame.Neutral });
        }
        Assert.Equal(1, victim.TotalHitsReceived);
        return victim.Velocity - before; // delta over the hit tick ≈ the final knockback
    }

    [Fact]
    public void HoldingUpDeflectsKnockbackUpward()
    {
        Vec2 withDi = HitWithHeld(new Vec2(0f, 1f), 0.10f, 0f);
        Vec2 without = HitWithHeld(new Vec2(0f, 1f), 0f, 0f);
        Assert.True(withDi.Y > without.Y + 0.05f,
            $"upward hold did not deflect knockback up ({withDi.Y:F2} vs {without.Y:F2})");
    }

    [Fact]
    public void HoldingAgainstTheHitReducesItsMagnitude()
    {
        // Knockback is straight up; holding DOWN is within 45° of opposite, so both
        // the deflection (−5%) and the reduction (×0.8) apply: expect ≈ 0.76×.
        // (The grounded victim enters Crouch while holding down — DI must still
        // apply there; only shielding opts out.)
        Vec2 against = HitWithHeld(new Vec2(0f, -1f), 0.05f, 0.20f);
        Vec2 neutral = HitWithHeld(new Vec2(0f, 0f), 0.05f, 0.20f);
        Assert.True(against.Length() < neutral.Length() * 0.85f,
            $"opposite hold did not reduce knockback ({against.Length():F2} vs {neutral.Length():F2})");
    }

    [Fact]
    public void ShieldPokesGetNoDirectionalInfluence()
    {
        // A shielding victim with a tiny shield takes a poke: DI must NOT engage.
        var moves = new[]
        {
            new MoveGenome(TestGames.Move(), 0),
            new MoveGenome(ParamSet.FromDictionary(DefaultSchemas.Shield, new Dictionary<string, float>
            {
                ["windUpDuration"] = 0.1f, ["coolDownDuration"] = 0.1f, ["initialSize"] = 0.6f,
                ["holdDegradationRate"] = 0.05f, ["hitDegradationScalar"] = 0.02f,
                ["knockbackReduction"] = 0.8f, ["spacingPush"] = 0.5f, ["regenRate"] = 0.3f,
                ["breakStunDuration"] = 1f, ["reflect"] = 0f,
            }), 0, MoveType.Shield),
        };
        CharacterGenome Make(string name) => new(name, 3, 0,
            TestGames.Character(
                (CharacterParams.DirectionalInfluence, 0.10f),
                (CharacterParams.DiKnockbackReduction, 0.20f)),
            moves, new[] { 0, 0, 1, 1, 0 });
        var genome = new GameGenome(new[] { Make("P1"), Make("P2") },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        SimWorld world = Grounded(genome, 0.2f, -1f);
        SimPlayer victim = world.Players[0];
        // Victim raises the (tiny) shield and holds hard away; attacker swings.
        Span<InputFrame> frame = stackalloc[]
            { new InputFrame(1f, 0f, false, InputFrame.ActionBit(2)), new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) };
        world.Tick(frame);
        frame[1] = InputFrame.Neutral;
        for (int t = 0; t < 30 && victim.TotalHitsReceived == 0; t++)
        {
            world.Tick(stackalloc[] { new InputFrame(1f, 0f, false, InputFrame.ActionBit(2)), InputFrame.Neutral });
        }
        Assert.Equal(1, victim.TotalHitsReceived); // poked through the tiny shield
        Assert.Equal(0, victim.DIInfluencedHits);  // and DI stayed out of it
    }

    // ── Legacy compatibility & serialization ───────────────────────────────────

    [Fact]
    public void LegacyGamesLoadWithAllThreeMechanicsOff()
    {
        var record = LegacyImporter.ImportGameFolder(
            Path.Combine(AppContext.BaseDirectory, "TestData", "UnityGames", "GameC"));
        foreach (CharacterGenome c in record.Genome.Characters)
        {
            Assert.Equal(0f, c.Params.Get(CharacterParams.FastFallAcceleration));
            Assert.Equal(0f, c.Params.Get(CharacterParams.DirectionalInfluence));
            Assert.Equal(1f, c.Params.Get(CharacterParams.CrouchMoveSpeed));
        }
        Assert.Empty(record.Genome.Validate());
    }

    [Fact]
    public void AgentDefenseCanPickCrouchWhenItClearsTheArc()
    {
        // High-swinging opponent + deep crouch ratio: across seeds at r=0.5 the agent
        // must sometimes duck (defense option 5). The crouch-clear test pads the arc
        // by the 1.0 telegraph margin, so offset.Y must exceed 1.45 for the padded
        // bottom (offY − 3) to clear the ratio-0.4 crouched top (−1.6 + 0.05 slack):
        // dist 1.8 × sin(1.0) = 1.51.
        var moves = new[] { new MoveGenome(TestGames.Move(
            (MoveParams.MoveAngle, 1.0f), (MoveParams.MoveDist, 1.8f)), 0) };
        CharacterGenome Make(string name) => new(name, 3, 0,
            TestGames.Character((CharacterParams.CrouchHeightRatio, 0.4f)),
            moves, new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(new[] { Make("P1"), Make("P2") },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        int crouched = 0;
        for (ulong seed = 0; seed < 25; seed++)
        {
            SimWorld world = Grounded(genome, -1.2f, 0.8f);
            world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(-1f, 0f, false, 0) });
            world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) });
            Assert.Equal(PlayerState.WarmUp, world.Players[1].State);

            var agent = new UtilityAgent(new Pcg32(seed), new AgentConfig { Randomness = 0.5f, DecisionIntervalTicks = 1 });
            InputFrame input = agent.GetInput(world, 0);
            if (input.Vertical < 0f && input.Actions == 0 && !input.Jump) crouched++;
        }
        Assert.True(crouched >= 2, $"agent never ducks under the high arc ({crouched}/25)");
    }

    [Fact]
    public void MechanicsStayDeterministic()
    {
        var config = GenerationConfig.Default;
        var rng = new Pcg32(1234);
        for (int i = 0; i < 3; i++)
        {
            GameGenome genome = GameGenome.Generate(config, rng);
            MatchResult a = Run(genome, 40 + (ulong)i);
            MatchResult b = Run(genome, 40 + (ulong)i);
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
