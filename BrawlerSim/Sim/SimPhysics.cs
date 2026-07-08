using BrawlerSim.Determinism;

namespace BrawlerSim.Sim;

/// <summary>
/// Deterministic kinematics: Box2D-equivalent gravity/damping integration, substepped
/// axis-separated platform collision (no tunneling at knockback speeds), correct ground
/// detection (fixes Unity defect #5), and mass-weighted player push-apart.
/// </summary>
public static class SimPhysics
{
    private const float Skin = 0.001f; // resolution slack to keep resting contacts stable

    public static void Step(SimPlayer player, IReadOnlyList<Aabb> platforms, MatchConfig config)
    {
        float dt = config.Dt;

        // Box2D order: gravity, then linear damping, then integrate position.
        player.Velocity += new Vec2(0f, -config.Gravity * player.GravityScale * dt);
        player.Velocity *= 1f / (1f + dt * player.Drag);

        Vec2 displacement = player.Velocity * dt;
        float maxAxis = MathF.Max(DetMath.Abs(displacement.X), DetMath.Abs(displacement.Y));
        int substeps = (int)DetMath.Clamp(MathF.Ceiling(maxAxis / config.MaxStepDistance), 1f, 64f);
        Vec2 step = displacement * (1f / substeps);

        for (int i = 0; i < substeps; i++)
        {
            MoveAxis(player, platforms, step.X, horizontal: true);
            MoveAxis(player, platforms, step.Y, horizontal: false);
        }

        player.OnGroundedChanged(IsGrounded(player, platforms));
    }

    /// <summary>Move along one axis, clamping against the first overlapping platform.</summary>
    private static void MoveAxis(SimPlayer player, IReadOnlyList<Aabb> platforms, float delta, bool horizontal)
    {
        if (delta == 0f)
        {
            return;
        }
        player.Position += horizontal ? new Vec2(delta, 0f) : new Vec2(0f, delta);
        Aabb body = player.Body;

        foreach (Aabb platform in platforms)
        {
            if (!body.Overlaps(platform))
            {
                continue;
            }
            if (horizontal)
            {
                float resolvedX = delta > 0f
                    ? platform.Left - player.BodyHalf.X - Skin
                    : platform.Right + player.BodyHalf.X + Skin;
                player.Position = player.Position with { X = resolvedX };
                player.Velocity = player.Velocity with { X = 0f };
            }
            else
            {
                float resolvedY = delta > 0f
                    ? platform.Bottom - player.BodyHalf.Y - Skin
                    : platform.Top + player.BodyHalf.Y + Skin;
                player.Position = player.Position with { Y = resolvedY };
                player.Velocity = player.Velocity with { Y = 0f };
            }
            body = player.Body;
        }
    }

    /// <summary>Grounded = a platform top directly under the feet, and not moving upward.</summary>
    public static bool IsGrounded(SimPlayer player, IReadOnlyList<Aabb> platforms)
    {
        if (player.Velocity.Y > 0.01f)
        {
            return false;
        }
        Aabb feet = new(
            new Vec2(player.Position.X, player.Body.Bottom - Skin),
            new Vec2(player.BodyHalf.X, 2f * Skin));
        foreach (Aabb platform in platforms)
        {
            if (feet.Overlaps(platform) && player.Body.Bottom >= platform.Top - 4f * Skin)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Player-vs-player contact (Unity's collision matrix had Player×Player on): resolve
    /// overlap along the axis of least penetration, displacement split inversely to mass,
    /// approaching velocity resolved inelastically (momentum-conserving) — a deterministic
    /// stand-in for Box2D's dynamic-vs-dynamic contact solve.
    /// </summary>
    public static void ResolvePlayerContact(SimPlayer a, SimPlayer b)
    {
        Aabb bodyA = a.Body;
        Aabb bodyB = b.Body;
        if (!bodyA.Overlaps(bodyB))
        {
            return;
        }

        Vec2 pen = bodyA.Penetration(bodyB);
        float total = a.Mass + b.Mass;
        if (pen.X < pen.Y)
        {
            float direction = a.Position.X <= b.Position.X ? -1f : 1f;
            a.Position += new Vec2(direction * pen.X * (b.Mass / total), 0f);
            b.Position -= new Vec2(direction * pen.X * (a.Mass / total), 0f);
            ResolveAxisVelocity(a, b, horizontal: true, direction);
        }
        else
        {
            float direction = a.Position.Y <= b.Position.Y ? -1f : 1f;
            a.Position += new Vec2(0f, direction * pen.Y * (b.Mass / total));
            b.Position -= new Vec2(0f, direction * pen.Y * (a.Mass / total));
            ResolveAxisVelocity(a, b, horizontal: false, direction);
        }
    }

    private static void ResolveAxisVelocity(SimPlayer a, SimPlayer b, bool horizontal, float directionOfA)
    {
        float va = horizontal ? a.Velocity.X : a.Velocity.Y;
        float vb = horizontal ? b.Velocity.X : b.Velocity.Y;
        // Only resolve if they are moving toward each other along the contact axis.
        if ((vb - va) * directionOfA >= 0f)
        {
            return;
        }
        float common = (a.Mass * va + b.Mass * vb) / (a.Mass + b.Mass);
        a.Velocity = horizontal ? a.Velocity with { X = common } : a.Velocity with { Y = common };
        b.Velocity = horizontal ? b.Velocity with { X = common } : b.Velocity with { Y = common };
    }
}
