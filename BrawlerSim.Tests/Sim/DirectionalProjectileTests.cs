using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Sim;

/// <summary>
/// Directional projectiles (2026-09-14, designer-directed; DEVIATIONS #39): bolts
/// gained melee's directionality — a launchAngle gene with moveAngle's polar
/// convention (0 = straight ahead, mirrored by facing; π/2 = straight up), a
/// PERIMETER spawn (the body-edge exit point along the launch direction, replacing
/// the interior launchX/launchY point that made self-hits geometrically trivial),
/// and the melee knockback-parity generation constraint. Path shapes bend around
/// the launch axis; gravity stays world-down.
/// </summary>
public class DirectionalProjectileTests
{
    private const float Up = MathF.PI / 2f;

    private static readonly AgentConfig Greedy = new() { Randomness = 0f, DecisionIntervalTicks = 1 };

    private static GameGenome Arena(params (string Key, float Value)[] overrides)
    {
        CharacterGenome Make(string name) => new(name, 3, 0, TestGames.Character(),
            new[] { new MoveGenome(TestGames.Projectile(overrides), 0, MoveType.Projectile) },
            new[] { 0, 0, 0, 0, 0 });
        var stage = new StageGenome(new[] { new PlatformGene(-8, -3, 16, 1) });
        return new GameGenome(new[] { Make("P1"), Make("P2") }, stage);
    }

    private static SimProjectileMove Move(GameGenome genome) =>
        new(genome.Characters[0].Moves[0], MatchConfig.Default, new Vec2(0.37f, 0.5f));

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

    // ── Rotated closed-form paths (hand-computed) ──────────────────────────────

    [Fact]
    public void UpwardLaunchRisesRegardlessOfFacing()
    {
        SimProjectileMove move = Move(Arena((ProjectileParams.LaunchAngle, Up)));
        // 30 ticks = 0.5 s at 8 u/s → 4 u straight up; facing only mirrors X.
        Vec2 right = move.PositionAt(Vec2.Zero, +1, 30, MatchConfig.Default);
        Vec2 left = move.PositionAt(Vec2.Zero, -1, 30, MatchConfig.Default);
        Assert.Equal(0f, right.X, 0.001f);
        Assert.Equal(4f, right.Y, 0.001f);
        Assert.Equal(0f, left.X, 0.001f);
        Assert.Equal(4f, left.Y, 0.001f);
    }

    [Fact]
    public void PathLateralBendsAroundTheLaunchAxis()
    {
        // Quadratic (scalar 2) fired straight up: the −1.6 lateral at s = 4 that used
        // to be a world-Y drop is now perpendicular to the ascent — a FORWARD bend
        // (facing-mirrored world X), while the travel distance itself is the rise.
        SimProjectileMove move = Move(Arena(
            (ProjectileParams.LaunchAngle, Up),
            (ProjectileParams.PathShape, 2.5f), (ProjectileParams.PathScalar, 2f)));
        Vec2 p = move.PositionAt(Vec2.Zero, +1, 30, MatchConfig.Default);
        Assert.Equal(1.6f, p.X, 0.001f);
        Assert.Equal(4f, p.Y, 0.001f);
        Vec2 mirrored = move.PositionAt(Vec2.Zero, -1, 30, MatchConfig.Default);
        Assert.Equal(-1.6f, mirrored.X, 0.001f);
    }

    [Fact]
    public void GravityStaysWorldDownWhenFiringUp()
    {
        SimProjectileMove move = Move(Arena(
            (ProjectileParams.LaunchAngle, Up), (ProjectileParams.AffectedByGravity, 1f)));
        // t = 0.5 s: rise 4 minus the ballistic drop ½·9.81·0.25 = 1.22625.
        Vec2 p = move.PositionAt(Vec2.Zero, +1, 30, MatchConfig.Default);
        Assert.Equal(0f, p.X, 0.001f);
        Assert.Equal(4f - 1.22625f, p.Y, 0.001f);
    }

    // ── Perimeter spawn ────────────────────────────────────────────────────────

    [Fact]
    public void SpawnOriginExitsThroughTheBodyEdgeAlongTheLaunchDirection()
    {
        var bodyHalf = new Vec2(0.37f, 0.5f);
        // Angle 0, facing +1: through the RIGHT edge, pushed out by the bolt's
        // half extent (0.25) + skin.
        SimProjectileMove ahead = Move(Arena());
        Vec2 o = ahead.SpawnOrigin(Vec2.Zero, bodyHalf, +1);
        Assert.Equal(0.37f + 0.25f + 0.001f, o.X, 0.0015f);
        Assert.Equal(0f, o.Y, 0.001f);
        // Facing −1 mirrors to the LEFT edge.
        o = ahead.SpawnOrigin(Vec2.Zero, bodyHalf, -1);
        Assert.Equal(-(0.37f + 0.25f + 0.001f), o.X, 0.0015f);
        // Straight up: through the TOP edge, either facing.
        SimProjectileMove up = Move(Arena((ProjectileParams.LaunchAngle, Up)));
        o = up.SpawnOrigin(Vec2.Zero, bodyHalf, -1);
        Assert.Equal(0f, o.X, 0.001f);
        Assert.Equal(0.5f + 0.25f + 0.001f, o.Y, 0.0015f);
    }

    [Fact]
    public void SpawnNeverOverlapsTheShooterAtAnyAngle()
    {
        for (int step = 0; step < 16; step++)
        {
            float angle = step * MathF.PI / 8f;
            var genome = Arena((ProjectileParams.LaunchAngle, angle));
            SimWorld world = Grounded(genome, 0f, 6f);
            SimPlayer shooter = world.Players[0];
            world.Tick(stackalloc[]
                { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
            for (int t = 0; world.Projectiles.Count == 0 && t < 30; t++)
            {
                world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
            }
            SimProjectile proj = Assert.Single(world.Projectiles);
            Assert.False(proj.OverlapsBody(shooter.Body),
                $"angle {angle:F2} spawned inside the shooter");
        }
    }

    // ── End to end: the mortar arc ─────────────────────────────────────────────

    /// <summary>A 45-degree gravity bolt arcs over and lands on a grounded target
    /// ~7 u away (range v²·sin2θ/g ≈ 6.5 from the exit point) — directionality,
    /// perimeter exit, and world gravity working together in a real match.</summary>
    [Fact]
    public void MortarArcLandsOnADistantGroundedTarget()
    {
        var genome = Arena(
            (ProjectileParams.LaunchAngle, MathF.PI / 4f),
            (ProjectileParams.AffectedByGravity, 1f),
            (ProjectileParams.TimeToDecay, 3f));
        SimWorld world = Grounded(genome, -4f, 3.2f);
        SimPlayer victim = world.Players[1];
        world.Tick(stackalloc[]
            { new InputFrame(0f, 0f, false, InputFrame.ActionBit(0)), InputFrame.Neutral });
        for (int t = 0; t < 240 && victim.TotalHitsReceived == 0; t++)
        {
            world.Tick(stackalloc[] { InputFrame.Neutral, InputFrame.Neutral });
        }
        Assert.Equal(1, victim.TotalHitsReceived);
        Assert.Equal(1, world.Players[0].ProjectileHits);
    }

    // ── The agent sees direction (DEVIATIONS #39) ──────────────────────────────

    /// <summary>The aim test samples the bolt's actual path: a straight-up bolt can
    /// never hit a level target, so the agent must not fire it; the same-speed
    /// mortar arc can, so the agent uses it. Under the old horizontal-corridor test
    /// both kits read identically.</summary>
    [Fact]
    public void AgentFiresTheMortarButNotTheStraightUpBoltAtLevelTargets()
    {
        (string, float)[] upKit =
        {
            (ProjectileParams.LaunchAngle, Up),
        };
        (string, float)[] mortarKit =
        {
            (ProjectileParams.LaunchAngle, MathF.PI / 4f),
            (ProjectileParams.AffectedByGravity, 1f),
            (ProjectileParams.TimeToDecay, 3f),
        };
        int FiredAfter(GameGenome genome, int ticks)
        {
            SimWorld world = Grounded(genome, -4f, 3f);
            var p0 = new UtilityAgent(new Pcg32(5), Greedy);
            var p1 = new UtilityAgent(new Pcg32(6), Greedy);
            for (int t = 0; t < ticks && !world.IsOver; t++)
            {
                world.Tick(stackalloc[] { p0.GetInput(world, 0), p1.GetInput(world, 1) });
            }
            return world.Players[0].ProjectilesFired;
        }
        Assert.Equal(0, FiredAfter(Arena(upKit), 300));
        Assert.True(FiredAfter(Arena(mortarKit), 300) >= 1,
            "the agent never fired the mortar at a reachable target");
    }

    // ── Knockback parity with melee ────────────────────────────────────────────

    [Fact]
    public void GeneratedProjectileKnockbackIsConstrainedTowardTheLaunchDirection()
    {
        // The melee rule, launch direction standing in for the hitbox location:
        // no generated bolt keeps knockback pointing >= 135 degrees off its flight.
        var rng = new Pcg32(77);
        for (int i = 0; i < 50; i++)
        {
            MoveGenome bolt = MoveGenome.GenerateProjectile(GenerationConfig.Default, rng);
            Vec2 launch = MoveRules.LaunchDirection(bolt.Params);
            var knockback = new Vec2(
                bolt.Params.Get(ProjectileParams.KnockbackModX),
                bolt.Params.Get(ProjectileParams.KnockbackModY));
            Assert.True(Vec2.AngleDeg(launch, knockback) < 135f,
                $"bolt {i}: knockback {knockback.X:F2},{knockback.Y:F2} vs launch {launch.X:F2},{launch.Y:F2}");
        }
    }
}
