using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Sim;

public enum ProjectilePath
{
    Linear = 0,
    Sine = 1,
    Quadratic = 2,
}

public enum ProjectileShape
{
    Square = 0,
    Circle = 1,
    Triangle = 2,
}

/// <summary>
/// A projectile move's genes resolved against MatchConfig and its owner's body
/// (2026-07-14, FEATURES.md §Projectiles; docs/features/projectiles.md).
/// DamageGiven mirrors the melee formula (base + total commitment time × factor);
/// HalfExtent enforces "never larger than the shooting character" against the
/// owner's ACTUAL scaled body; LaunchFraction is the sketch's EXIT point, clamped
/// by the schema to overlap the player.
/// </summary>
public sealed class SimProjectileMove
{
    public readonly int WarmUpTicks;
    public readonly int ExecuteTicks;
    public readonly int CoolDownTicks;
    public readonly int TtlTicks;
    public readonly ProjectilePath Path;
    public readonly float PathScalar;
    public readonly float SineAmplitude;
    public readonly float QuadraticScale;
    public readonly float LaunchSpeed;
    public readonly float Acceleration;   // 0 when the doesAccelerate gene is off
    public readonly bool Gravity;
    public readonly float HalfExtent;     // hitboxSize/2 after the owner-size cap
    public readonly ProjectileShape Shape;
    public readonly float RotationRate;   // rad/s, 0 when the doesRotate gene is off
    public readonly float KnockbackScalar;
    public readonly Vec2 KnockbackDirection;
    public readonly float DamageGiven;
    public readonly float HitstunDuration;
    public readonly bool DamageDecay;
    public readonly float DecayRate;      // damage-scale units per second
    public readonly bool HitsSelf;
    public readonly Vec2 LaunchFraction;  // × owner body half extents (X mirrors with facing)

    public SimProjectileMove(MoveGenome genome, MatchConfig config, Vec2 ownerBodyHalf)
    {
        ParamSet p = genome.Params;
        WarmUpTicks = config.ToTicks(p.Get(ProjectileParams.WarmUpDuration));
        ExecuteTicks = config.ToTicks(p.Get(ProjectileParams.ExecutionDuration));
        CoolDownTicks = config.ToTicks(p.Get(ProjectileParams.CoolDownDuration));
        TtlTicks = config.ToTicks(p.Get(ProjectileParams.TimeToDecay));
        Path = (ProjectilePath)Math.Min(2, (int)MathF.Floor(p.Get(ProjectileParams.PathShape)));
        PathScalar = p.Get(ProjectileParams.PathScalar);
        SineAmplitude = config.ProjectileSineAmplitude;
        QuadraticScale = config.ProjectileQuadraticScale;
        LaunchSpeed = p.Get(ProjectileParams.Velocity);
        Acceleration = p.Get(ProjectileParams.DoesAccelerate) >= 0.5f
            ? p.Get(ProjectileParams.Acceleration) : 0f;
        Gravity = p.Get(ProjectileParams.AffectedByGravity) >= 0.5f;
        // "Never larger than the shooting character": cap the full extent at the
        // owner's smaller body dimension (matters for shrunken characters).
        float extent = MathF.Min(
            p.Get(ProjectileParams.HitboxSize),
            2f * MathF.Min(ownerBodyHalf.X, ownerBodyHalf.Y));
        HalfExtent = extent / 2f;
        Shape = (ProjectileShape)Math.Min(2, (int)MathF.Floor(p.Get(ProjectileParams.HitboxShape)));
        RotationRate = p.Get(ProjectileParams.DoesRotate) >= 0.5f
            ? p.Get(ProjectileParams.RotationRate) : 0f;
        KnockbackScalar = p.Get(ProjectileParams.KnockbackScalar);
        KnockbackDirection = new Vec2(
            p.Get(ProjectileParams.KnockbackModX), p.Get(ProjectileParams.KnockbackModY));
        DamageGiven = MoveRules.BaseDamage
            + (p.Get(ProjectileParams.WarmUpDuration)
               + p.Get(ProjectileParams.ExecutionDuration)
               + p.Get(ProjectileParams.CoolDownDuration))
              * p.Get(ProjectileParams.DamageFactor);
        HitstunDuration = p.Get(ProjectileParams.HitstunDuration);
        DamageDecay = p.Get(ProjectileParams.DamageDecay) >= 0.5f;
        DecayRate = p.Get(ProjectileParams.DecayRate);
        HitsSelf = p.Get(ProjectileParams.HitsSelf) >= 0.5f;
        LaunchFraction = new Vec2(p.Get(ProjectileParams.LaunchX), p.Get(ProjectileParams.LaunchY));
    }

    /// <summary>
    /// The CLOSED-FORM trajectory — position is a pure function of age, never
    /// integrated, so replay == live by construction and the agent's dodge/aim
    /// prediction is exact. s runs along the spawn facing; the lateral offset is the
    /// path shape (sine over TIME, quadratic over DISTANCE like the sketch's arc,
    /// always curving downward) plus the optional gravity term.
    /// </summary>
    public Vec2 PositionAt(Vec2 origin, int facing, int ageTicks, MatchConfig config)
    {
        float t = ageTicks * config.Dt;
        float s = LaunchSpeed * t + 0.5f * Acceleration * t * t;
        float lateral = Path switch
        {
            ProjectilePath.Sine => SineAmplitude * DetMath.Sin(2f * MathF.PI * PathScalar * t),
            ProjectilePath.Quadratic => -PathScalar * QuadraticScale * s * s,
            _ => 0f,
        };
        if (Gravity)
        {
            lateral -= 0.5f * config.Gravity * t * t;
        }
        return origin + new Vec2(facing * s, lateral);
    }

    public float DamageScaleAt(int ageTicks, MatchConfig config) =>
        DamageDecay ? MathF.Max(0f, 1f - DecayRate * ageTicks * config.Dt) : 1f;
}

/// <summary>
/// One live projectile — the first non-player entity in SimWorld. Position, angle,
/// and damage scale are recomputed from age each tick (closed form); the only
/// integrated state is age itself plus the owner-clearance latch.
/// </summary>
public sealed class SimProjectile
{
    public readonly SimProjectileMove Move;
    public readonly int Owner;
    public readonly int MoveIndex;
    public readonly Vec2 Origin;
    public readonly int Facing;

    public int AgeTicks;
    public Vec2 Position;
    public float Angle;
    public float DamageScale = 1f;

    /// <summary>False until the projectile first stops overlapping its owner; the
    /// owner cannot be hit before then ("never damage the user ON FIRE"), and only
    /// with the hitsSelf gene after.</summary>
    public bool ClearedOwner;

    public bool Alive = true;

    public SimProjectile(SimProjectileMove move, int owner, int moveIndex, Vec2 origin, int facing)
    {
        Move = move;
        Owner = owner;
        MoveIndex = moveIndex;
        Origin = origin;
        Facing = facing;
        Position = origin;
    }

    public bool OverlapsBody(Aabb body) => Move.Shape switch
    {
        ProjectileShape.Circle => SimShapes.CircleOverlapsAabb(Position, Move.HalfExtent, body),
        ProjectileShape.Triangle => SimShapes.TriangleOverlapsAabb(Position, Move.HalfExtent, Angle, body),
        _ => SimShapes.RotatedSquareOverlapsAabb(Position, Move.HalfExtent, Angle, body),
    };

    /// <summary>Conservative bounds for the shield-coverage rect (the exact shapes
    /// project into their bounding box for the "fully covered" test).</summary>
    public Aabb Bounds => new(Position, new Vec2(Move.HalfExtent, Move.HalfExtent));
}
