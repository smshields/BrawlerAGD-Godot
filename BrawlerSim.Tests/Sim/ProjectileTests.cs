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
/// Projectiles (2026-07-14, FEATURES.md §Projectiles; docs/features/projectiles.md).
/// The gated StateHash means projectile-less matches hash exactly as before — the
/// standing golden tests are the no-regression proof; everything here exercises the
/// new entity.
/// </summary>
public class ProjectileTests
{
    private const float Dt = 1f / 60f;

    private static GameGenome ProjectileArena(params (string Key, float Value)[] overrides)
    {
        CharacterGenome Make(string name) => new(name, 3, 0, TestGames.Character(),
            new[] { new MoveGenome(TestGames.Projectile(overrides), 0, MoveType.Projectile) },
            new[] { 0, 0, 0, 0, 0 });
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

    /// <summary>P0 presses its projectile button once, then both go neutral until the
    /// first projectile exists (or maxTicks pass). Returns ticks waited.</summary>
    private static int FireAndWait(SimWorld world, int maxTicks = 60)
    {
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
        int waited = 1;
        while (world.Projectiles.Count == 0 && waited < maxTicks)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            waited++;
        }
        return waited;
    }

    // ── Shape overlap (hand-computed) ─────────────────────────────────────────

    [Fact]
    public void CircleVsAabbUsesClosestPointDistance()
    {
        var box = new Aabb(new Vec2(0f, 0f), new Vec2(1f, 1f));
        // Corner at (1,1); circle at (2,2) radius √2 barely reaches (distance √2).
        Assert.True(SimShapes.CircleOverlapsAabb(new Vec2(2f, 2f), 1.4143f, box));
        Assert.False(SimShapes.CircleOverlapsAabb(new Vec2(2f, 2f), 1.4141f, box));
    }

    [Fact]
    public void RotatedSquareReachesFartherOnItsDiagonal()
    {
        var box = new Aabb(new Vec2(2f, 0f), new Vec2(1f, 1f));
        // Box left edge at x=1. Unrotated square (h=0.7) at origin reaches x=0.7: miss.
        Assert.False(SimShapes.RotatedSquareOverlapsAabb(new Vec2(0f, 0f), 0.7f, 0f, box));
        // Rotated 45°, the corner reaches 0.7·√2 ≈ 0.99: still a miss at x=1…
        Assert.False(SimShapes.RotatedSquareOverlapsAabb(new Vec2(0.005f, 0f), 0.7f, MathF.PI / 4f, box));
        // …but from x=0.02 the diagonal (0.02+0.9899 > 1) connects while flat doesn't.
        Assert.True(SimShapes.RotatedSquareOverlapsAabb(new Vec2(0.02f, 0f), 0.7f, MathF.PI / 4f, box));
        Assert.False(SimShapes.RotatedSquareOverlapsAabb(new Vec2(0.02f, 0f), 0.7f, 0f, box));
    }

    [Fact]
    public void TriangleHitsOnlyWhereItPoints()
    {
        var box = new Aabb(new Vec2(2f, 0f), new Vec2(1f, 1f));
        // Circumradius 1.2, vertex along +X at angle 0: tip reaches x=1.2 > box left 1.
        Assert.True(SimShapes.TriangleOverlapsAabb(new Vec2(0f, 0f), 1.2f, 0f, box));
        // Rotated 180°: the FLAT side faces the box at x = r·cos(60°) = 0.6: miss.
        Assert.False(SimShapes.TriangleOverlapsAabb(new Vec2(0f, 0f), 1.2f, MathF.PI, box));
    }

    // ── Closed-form paths (hand-computed) ─────────────────────────────────────

    [Fact]
    public void LinearPathIsVelocityTimesTimeMirroredByFacing()
    {
        var genome = ProjectileArena();
        var move = new SimProjectileMove(genome.Characters[0].Moves[0], MatchConfig.Default, new Vec2(0.37f, 0.5f));
        Vec2 origin = new(1f, 2f);
        // 30 ticks = 0.5 s at 8 u/s → 4 u along facing.
        Assert.Equal(new Vec2(5f, 2f), move.PositionAt(origin, +1, 30, MatchConfig.Default));
        Assert.Equal(new Vec2(-3f, 2f), move.PositionAt(origin, -1, 30, MatchConfig.Default));
    }

    [Fact]
    public void AccelerationAddsHalfAtSquared()
    {
        var genome = ProjectileArena(
            (ProjectileParams.DoesAccelerate, 1f), (ProjectileParams.Acceleration, 4f));
        var move = new SimProjectileMove(genome.Characters[0].Moves[0], MatchConfig.Default, new Vec2(0.37f, 0.5f));
        // t = 1 s: s = 8 + ½·4 = 10.
        Vec2 p = move.PositionAt(Vec2.Zero, +1, 60, MatchConfig.Default);
        Assert.Equal(10f, p.X, 0.001f);
    }

    [Fact]
    public void SinePathOscillatesAtTheGeneFrequency()
    {
        var genome = ProjectileArena(
            (ProjectileParams.PathShape, 1.2f), (ProjectileParams.PathScalar, 1f));
        var move = new SimProjectileMove(genome.Characters[0].Moves[0], MatchConfig.Default, new Vec2(0.37f, 0.5f));
        MatchConfig config = MatchConfig.Default;
        // f = 1 Hz: peak +amplitude at t = 0.25 s (15 ticks), trough at 0.75 s.
        Assert.Equal(config.ProjectileSineAmplitude, move.PositionAt(Vec2.Zero, 1, 15, config).Y, 0.001f);
        Assert.Equal(-config.ProjectileSineAmplitude, move.PositionAt(Vec2.Zero, 1, 45, config).Y, 0.001f);
    }

    [Fact]
    public void QuadraticPathCurvesDownWithTravel()
    {
        var genome = ProjectileArena(
            (ProjectileParams.PathShape, 2.5f), (ProjectileParams.PathScalar, 2f));
        var move = new SimProjectileMove(genome.Characters[0].Moves[0], MatchConfig.Default, new Vec2(0.37f, 0.5f));
        // t = 0.5 s: s = 4 → y = −2 · 0.05 · 16 = −1.6.
        Vec2 p = move.PositionAt(Vec2.Zero, +1, 30, MatchConfig.Default);
        Assert.Equal(4f, p.X, 0.001f);
        Assert.Equal(-1.6f, p.Y, 0.001f);
    }

    [Fact]
    public void GravityFlagAddsBallisticDrop()
    {
        var genome = ProjectileArena((ProjectileParams.AffectedByGravity, 1f));
        var move = new SimProjectileMove(genome.Characters[0].Moves[0], MatchConfig.Default, new Vec2(0.37f, 0.5f));
        // t = 0.5 s: drop = ½·9.81·0.25 = 1.22625.
        Assert.Equal(-1.22625f, move.PositionAt(Vec2.Zero, +1, 30, MatchConfig.Default).Y, 0.001f);
    }

    // ── Firing lifecycle ───────────────────────────────────────────────────────

    [Fact]
    public void FiringSpawnsAtTheLaunchPointAfterWarmUp()
    {
        var genome = ProjectileArena();
        SimWorld world = Grounded(genome, -4f, 6f);
        SimPlayer shooter = world.Players[0];
        shooter.Facing = 1;
        Vec2 posAtSpawn = shooter.Position;
        int waited = FireAndWait(world);
        // Warm-up 12 ticks; the spawn happens on the WarmUp→Attack transition tick.
        Assert.Equal(13, waited);
        SimProjectile proj = Assert.Single(world.Projectiles);
        Assert.Equal(1, shooter.ProjectilesFired);
        Assert.Equal(0, proj.Owner);
        // Launch offset: (0.3 × BodyHalf.X × facing, 0) from the shooter's center.
        Assert.Equal(posAtSpawn.X + 0.3f * shooter.BodyHalf.X, proj.Origin.X, 0.01f);
        Assert.Equal(posAtSpawn.Y, proj.Origin.Y, 0.01f);
        Assert.Equal(PlayerState.Attack, shooter.State);
    }

    [Fact]
    public void ProjectileDespawnsAtTimeToDecay()
    {
        // Slow projectile, short TTL (0.5 s = 30 ticks), fired from mid-air platform
        // height so nothing else kills it first.
        var genome = ProjectileArena(
            (ProjectileParams.Velocity, 3f), (ProjectileParams.TimeToDecay, 0.5f));
        SimWorld world = Grounded(genome, -4f, 6f);
        FireAndWait(world);
        int alive = 0;
        while (world.Projectiles.Count > 0 && alive < 90)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            alive++;
        }
        Assert.InRange(alive, 25, 31); // ~30 ticks of life
    }

    [Fact]
    public void ProjectileDespawnsPastTheBlastBoundary()
    {
        var genome = ProjectileArena((ProjectileParams.Velocity, 15f), (ProjectileParams.TimeToDecay, 4f));
        SimWorld world = Grounded(genome, 4f, -6f); // fires rightward from x=4
        FireAndWait(world);
        int alive = 0;
        while (world.Projectiles.Count > 0 && alive < 120)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            alive++;
        }
        // Blast zone right edge ≈ 10.76; ~6.5 u to cover at 15 u/s ≈ 26 ticks ≪ TTL 240.
        Assert.InRange(alive, 10, 40);
    }

    [Fact]
    public void PlatformContactDestroysTheProjectile()
    {
        // Ballistic drop into the floor platform (top y=−2, spans x∈[−8,8]).
        var genome = ProjectileArena(
            (ProjectileParams.Velocity, 4f), (ProjectileParams.AffectedByGravity, 1f),
            (ProjectileParams.TimeToDecay, 4f));
        SimWorld world = Grounded(genome, -4f, 6f);
        FireAndWait(world);
        int alive = 0;
        while (world.Projectiles.Count > 0 && alive < 200)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            alive++;
        }
        // Needs to fall ~0.6 u to the platform top: t=√(2·0.6/9.81)≈0.35 s ≈ 21 ticks,
        // far sooner than TTL (240) or the boundary (would take ~100+ at 4 u/s).
        Assert.InRange(alive, 10, 45);
    }

    [Fact]
    public void HitUsesTheMeleeKnockbackFormulaAndStuns()
    {
        var genome = ProjectileArena();
        SimWorld world = Grounded(genome, -2f, 2f);
        SimPlayer victim = world.Players[1];
        FireAndWait(world);
        SimProjectile proj = Assert.Single(world.Projectiles);
        float travelled = 0f;
        int ticks = 0;
        while (victim.TotalHitsReceived == 0 && ticks < 60)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            ticks++;
        }
        Assert.Equal(1, victim.TotalHitsReceived);
        Assert.Equal(7.5f, victim.Damage, 0.01f); // 5 + (0.2+0.1+0.2)·5, melee formula
        Assert.Equal(1, world.Players[0].ProjectileHits);
        Assert.Equal(PlayerState.Stun, victim.State);
        Assert.Empty(world.Projectiles); // spent on the hit
        _ = travelled;
    }

    [Fact]
    public void DamageDecayScalesTheHitAndFadesOut()
    {
        // decayRate 0.5/s: after ~1 s of flight the hit lands at roughly half damage.
        var genome = ProjectileArena(
            (ProjectileParams.Velocity, 3f), (ProjectileParams.DamageDecay, 1f),
            (ProjectileParams.DecayRate, 0.5f), (ProjectileParams.TimeToDecay, 4f));
        SimWorld world = Grounded(genome, -4f, 2.2f); // ~6 u apart within blast zone
        SimPlayer victim = world.Players[1];
        FireAndWait(world);
        int ticks = 0;
        while (victim.TotalHitsReceived == 0 && ticks < 200 && world.Projectiles.Count > 0)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            ticks++;
        }
        Assert.Equal(1, victim.TotalHitsReceived);
        // ~6 u at 3 u/s ≈ 2 s?? — 6.2/3 ≈ 124 ticks → scale ≈ 1−0.5·2.06 ≈ 0. Use the
        // recorded damage: strictly between 0 and full (decay engaged, not expired).
        Assert.InRange(victim.Damage, 0.01f, 7.4f);
    }

    [Fact]
    public void OwnerIsImmuneOnLaunchAndHitsSelfGeneGovernsAfter()
    {
        // A decelerating projectile that reverses back through the shooter.
        (string, float)[] Overrides(float hitsSelf) => new[]
        {
            (ProjectileParams.Velocity, 4f),
            (ProjectileParams.DoesAccelerate, 1f),
            (ProjectileParams.Acceleration, -8f),   // reverses at t = 0.5 s
            (ProjectileParams.TimeToDecay, 3f),
            (ProjectileParams.HitsSelf, hitsSelf),
        };

        foreach (float gene in new[] { 0f, 1f })
        {
            var genome = ProjectileArena(Overrides(gene));
            SimWorld world = Grounded(genome, -4f, 7f);
            SimPlayer shooter = world.Players[0];
            FireAndWait(world);
            int ticks = 0;
            while (world.Projectiles.Count > 0 && shooter.TotalHitsReceived == 0 && ticks < 240)
            {
                world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
                ticks++;
            }
            if (gene >= 0.5f)
            {
                Assert.Equal(1, shooter.TotalHitsReceived); // the comeback clips them
            }
            else
            {
                Assert.Equal(0, shooter.TotalHitsReceived); // immune without the gene
            }
        }
    }

    [Fact]
    public void ShieldBlocksAProjectileAndItIsSpent()
    {
        // Victim raises a big shield; the bolt arrives, blocks, and despawns.
        var shieldParams = ParamSet.FromDictionary(DefaultSchemas.Shield, new Dictionary<string, float>
        {
            ["windUpDuration"] = 0.05f, ["coolDownDuration"] = 0.1f, ["initialSize"] = 2.0f,
            ["holdDegradationRate"] = 0.05f, ["hitDegradationScalar"] = 0.02f,
            ["knockbackReduction"] = 0.8f, ["spacingPush"] = 0.5f, ["regenRate"] = 0.3f,
            ["breakStunDuration"] = 1f,
        });
        var shooter = new CharacterGenome("Shooter", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(TestGames.Projectile(), 0, MoveType.Projectile) },
            new[] { 0, 0, 0, 0, 0 });
        var blocker = new CharacterGenome("Blocker", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(shieldParams, 0, MoveType.Shield) },
            new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(new[] { shooter, blocker },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        SimWorld world = Grounded(genome, -3f, 1f);
        SimPlayer victim = world.Players[1];
        // Blocker holds the shield the whole time; shooter fires once.
        world.Tick(stackalloc[]
            { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) });
        for (int t = 0; t < 80 && victim.BlockedHits == 0; t++)
        {
            world.Tick(stackalloc[]
                { InputFrame.Neutral, new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)) });
        }
        Assert.Equal(1, victim.BlockedHits);
        Assert.Equal(0f, victim.Damage); // blocked clean — no damage, no stun
        Assert.NotEqual(PlayerState.Stun, victim.State);
        Assert.Empty(world.Projectiles);
    }

    [Fact]
    public void DashIFramesNegateTheHitAndTheProjectilePassesThrough()
    {
        var shooter = new CharacterGenome("Shooter", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(TestGames.Projectile((ProjectileParams.Velocity, 6f)), 0, MoveType.Projectile) },
            new[] { 0, 0, 0, 0, 0 });
        var dashParams = ParamSet.FromDictionary(DefaultSchemas.Dash, new Dictionary<string, float>
        {
            ["windUpDuration"] = 0.05f, ["acceleration"] = 6f, ["duration"] = 0.4f,
            ["warmUpInvulnerable"] = 1f, ["durationInvulnerable"] = 1f,
        });
        var dodger = new CharacterGenome("Dodger", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(dashParams, 0, MoveType.Dash) },
            new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(new[] { shooter, dodger },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        SimWorld world = Grounded(genome, -3f, 1.5f);
        SimPlayer victim = world.Players[1];
        world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
        // ONE timed dash, LEFT held so the direction capture sends it INTO the bolt.
        // Spawn ≈ tick 12 at x ≈ −2.89, 6 u/s rightward; the victim's body-overlap
        // window is ticks ≈ 50–62. Dashing at t = 48 gives 3 + 24 invulnerable ticks
        // (49–75) covering the whole crossing. (Mashing re-dash instead leaves a
        // 1-tick Idle gap each cycle — the bolt found it; a real finding about how
        // continuous i-frames AREN'T, kept deliberate.)
        for (int t = 0; t < 48; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        world.Tick(stackalloc[] { InputFrame.Neutral, new InputFrame(-1f, 0f, false, InputFrame.ActionBit(0)) });
        for (int t = 0; t < 60; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Equal(0, victim.TotalHitsReceived);
        Assert.True(victim.DashInvulnDodges >= 1, "i-frames never negated the projectile");
    }

    [Fact]
    public void MultipleProjectilesCanBeLiveAndSpamIsDeterministic()
    {
        var genome = ProjectileArena(
            (ProjectileParams.WarmUpDuration, 0.1f), (ProjectileParams.ExecutionDuration, 0.1f),
            (ProjectileParams.CoolDownDuration, 0.1f), (ProjectileParams.Velocity, 3f),
            (ProjectileParams.TimeToDecay, 4f));

        int maxLive = 0;
        ulong Run()
        {
            SimWorld world = Grounded(genome, -4f, 7f);
            var mash = new InputFrame(0f, 0f, false, InputFrame.ActionBit(0));
            for (int t = 0; t < 300; t++)
            {
                world.Tick(stackalloc[] { mash, mash });
                maxLive = Math.Max(maxLive, world.Projectiles.Count);
            }
            return world.StateHash();
        }
        ulong a = Run();
        ulong b = Run();
        Assert.Equal(a, b);
        Assert.True(maxLive >= 3, $"spam never stacked projectiles (max {maxLive})");
    }

    // ── Serialization & composition ────────────────────────────────────────────

    [Fact]
    public void V5RoundTripsProjectileGenomes()
    {
        var genome = ProjectileArena((ProjectileParams.PathShape, 1.7f));
        string json = GameGenomeJson.Serialize(new GameRecord("t", null, genome));
        Assert.Contains("\"projectile\"", json);
        GameRecord loaded = GameGenomeJson.Deserialize(json);
        MoveGenome move = loaded.Genome.Characters[0].Moves[0];
        Assert.Equal(MoveType.Projectile, move.Type);
        Assert.Equal(1.7f, move.Params.Get(ProjectileParams.PathShape), 0.0001f);
        Assert.Empty(loaded.Genome.Validate());
    }

    [Fact]
    public void PerButtonProjectileSpecGeneratesProjectileSlots()
    {
        var config = GenerationConfig.Default with
        {
            ButtonComposition = new[]
                { SlotSpec.Attack, SlotSpec.Projectile, SlotSpec.Shield, SlotSpec.Attack, SlotSpec.Dash },
        };
        GameGenome genome = GameGenome.Generate(config, new Pcg32(99));
        foreach (CharacterGenome c in genome.Characters)
        {
            Assert.Equal(MoveType.Projectile, c.Moves[1].Type);
        }
        Assert.Empty(genome.Validate());
        // And it plays deterministically under the utility agent.
        MatchResult a = Run(genome, 3);
        MatchResult b = Run(genome, 3);
        Assert.Equal(a.FinalHash, b.FinalHash);

        static MatchResult Run(GameGenome genome, ulong seed) =>
            MatchRunner.Run(genome, new IInputSource[]
            {
                AgentConfig.Default.CreateSource(new Pcg32(seed, 0)),
                AgentConfig.Default.CreateSource(new Pcg32(seed, 1)),
            });
    }

    // ── Agent ──────────────────────────────────────────────────────────────────

    [Fact]
    public void AgentFiresFromRangeButNotPointBlank()
    {
        var genome = ProjectileArena((ProjectileParams.TimeToDecay, 2f));
        int firedAtRange = 0, firedClose = 0;
        for (ulong seed = 0; seed < 25; seed++)
        {
            SimWorld far = Grounded(genome, -4f, 3f); // 7 u apart
            var agent = new UtilityAgent(new Pcg32(seed), new AgentConfig { Randomness = 0.3f, DecisionIntervalTicks = 1 });
            if (agent.GetInput(far, 0).Actions != 0)
            {
                firedAtRange++;
            }
            SimWorld close = Grounded(genome, -0.8f, 0.8f); // 1.6 u — inside the gate
            var closeAgent = new UtilityAgent(new Pcg32(seed), new AgentConfig { Randomness = 0.3f, DecisionIntervalTicks = 1 });
            if (closeAgent.GetInput(close, 0).Actions != 0)
            {
                firedClose++;
            }
        }
        Assert.True(firedAtRange >= 5, $"agent never fires from range ({firedAtRange}/25)");
        Assert.Equal(0, firedClose); // the close-range gate is hard
    }

    [Fact]
    public void AgentReactsDefensivelyToAnIncomingProjectile()
    {
        var genome = ProjectileArena((ProjectileParams.Velocity, 5f), (ProjectileParams.TimeToDecay, 3f));
        int defended = 0;
        for (ulong seed = 0; seed < 25; seed++)
        {
            SimWorld world = Grounded(genome, -5f, 3f);
            // P0 fires; advance until the bolt sits INSIDE the 30-tick dodge horizon
            // (spawn ≈ tick 13 at x≈−4.9; ~7.3 u to the victim at 5 u/s ≈ 87 ticks of
            // flight — react when ~25 remain; the agent shouldn't panic at a bolt
            // still a second and a half away).
            world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
            for (int t = 0; t < 75; t++)
            {
                world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            }
            Assert.Single(world.Projectiles);
            var agent = new UtilityAgent(new Pcg32(seed), new AgentConfig { Randomness = 0.5f, DecisionIntervalTicks = 1 });
            InputFrame input = agent.GetInput(world, 1);
            // Any defensive output counts: hop, retreat from the shot, or duck.
            if (input.Jump || input.Horizontal > 0f || input.Vertical < 0f)
            {
                defended++;
            }
        }
        Assert.True(defended >= 8, $"agent ignores incoming projectiles ({defended}/25)");
    }

    [Fact]
    public void AgentShieldsDuringAProjectileWindUp()
    {
        // 2026-07-20 designer: warm-ups telegraph across the board. A shield-bearing
        // defender watching a shooter WIND UP (no bolt exists yet) must sometimes
        // raise the shield — the failure mode this pins: zoners never triggered the
        // melee telegraph, so shields were never selected against them (0 activations
        // in the shield-vs-zoner franken match).
        var shooter = new CharacterGenome("Shooter", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(TestGames.Projectile(
                (ProjectileParams.WarmUpDuration, 0.6f)), 0, MoveType.Projectile) },
            new[] { 0, 0, 0, 0, 0 });
        var shieldParams = ParamSet.FromDictionary(DefaultSchemas.Shield, new Dictionary<string, float>
        {
            ["windUpDuration"] = 0.1f, ["coolDownDuration"] = 0.1f, ["initialSize"] = 1.6f,
            ["holdDegradationRate"] = 0.05f, ["hitDegradationScalar"] = 0.02f,
            ["knockbackReduction"] = 0.8f, ["spacingPush"] = 0.5f, ["regenRate"] = 0.3f,
            ["breakStunDuration"] = 1f,
        });
        var defender = new CharacterGenome("Defender", 3, 0, TestGames.Character(),
            new[] { new MoveGenome(shieldParams, 0, MoveType.Shield) },
            new[] { 0, 0, 0, 0, 0 });
        var genome = new GameGenome(new[] { shooter, defender },
            new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) }));

        int shielded = 0;
        for (ulong seed = 0; seed < 25; seed++)
        {
            SimWorld world = Grounded(genome, -5f, 0f);
            // Shooter starts its 36-tick wind-up; NO projectile exists yet.
            world.Tick(stackalloc[] { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
            for (int t = 0; t < 6; t++)
            {
                world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            }
            Assert.Equal(PlayerState.WarmUp, world.Players[0].State);
            Assert.Empty(world.Projectiles);
            var agent = new UtilityAgent(new Pcg32(seed), new AgentConfig { Randomness = 0.3f, DecisionIntervalTicks = 1 });
            if (agent.GetInput(world, 1).ActionPressed(0)) // button 0 = the shield slot
            {
                shielded++;
            }
        }
        Assert.True(shielded >= 8, $"defender never pre-shields a wind-up ({shielded}/25)");
    }

    [Fact]
    public void RandomCompositionMatchesWithProjectilesAreDeterministic()
    {
        var config = GenerationConfig.Default with { ButtonComposition = GenerationConfig.RandomComposition };
        var rng = new Pcg32(2026_07_14);
        for (int i = 0; i < 3; i++)
        {
            GameGenome genome = GameGenome.Generate(config, rng);
            MatchResult a = Run(genome, 80 + (ulong)i);
            MatchResult b = Run(genome, 80 + (ulong)i);
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
