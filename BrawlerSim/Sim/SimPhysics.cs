using BrawlerSim.Determinism;

namespace BrawlerSim.Sim;

/// <summary>
/// Deterministic kinematics: Box2D-equivalent gravity/damping integration, substepped
/// axis-separated collision (no tunneling at knockback speeds), correct ground detection
/// (fixes Unity defect #5), and solid player-vs-player contact.
///
/// Player contact model (mirrors Box2D dynamic-vs-dynamic behavior):
/// 1. During movement, the opponent's body is a SOLID collider — a move that would cross
///    into them clamps at the contact face and transfers momentum (mass-weighted), so
///    players can push each other, stand on heads, and never pass through one another.
/// 2. Any RESIDUAL overlap (spawn overlap, simultaneous moves) separates gradually via
///    ResolvePlayerContact, capped per tick so deep overlaps never resolve as a teleport.
/// </summary>
public static class SimPhysics
{
    private const float Skin = 0.001f; // resolution slack to keep resting contacts stable

    public static void Step(SimPlayer player, SimPlayer opponent, IReadOnlyList<Aabb> platforms, MatchConfig config)
    {
        float dt = config.Dt;

        // Box2D order: gravity, then linear damping, then integrate position.
        // Dash travel (2026-07-13): a locked straight line — gravity and drag are
        // suspended (the dash re-asserts its velocity every tick anyway).
        if (!player.IsDashTraveling)
        {
            player.Velocity += new Vec2(0f, -config.Gravity * player.GravityScale * dt);
            player.Velocity *= 1f / (1f + dt * player.Drag);
        }

        Vec2 displacement = player.Velocity * dt;
        float maxAxis = MathF.Max(DetMath.Abs(displacement.X), DetMath.Abs(displacement.Y));
        int substeps = (int)DetMath.Clamp(MathF.Ceiling(maxAxis / config.MaxStepDistance), 1f, 64f);
        Vec2 step = displacement * (1f / substeps);

        for (int i = 0; i < substeps; i++)
        {
            MoveAxis(player, opponent, platforms, config, step.X, horizontal: true);
            MoveAxis(player, opponent, platforms, config, step.Y, horizontal: false);
        }

        player.OnGroundedChanged(IsGrounded(player, platforms));
    }

    /// <summary>
    /// Move along one axis, clamping against platforms and — when the move would CREATE
    /// a crossing — against the opponent's body. Pre-existing overlap is left for the
    /// capped resolver, so residual contact never snaps positions.
    /// </summary>
    private static void MoveAxis(SimPlayer player, SimPlayer opponent, IReadOnlyList<Aabb> platforms, MatchConfig config, float delta, bool horizontal)
    {
        if (delta == 0f)
        {
            return;
        }
        bool overlappedBefore = player.Body.Overlaps(opponent.Body);
        player.Position += horizontal ? new Vec2(delta, 0f) : new Vec2(0f, delta);

        // Opponent contact FIRST, and only when this motion created the overlap —
        // platform clamps below may squeeze players together, and that residual case
        // belongs to the capped resolver, never to a face snap. The contact face comes
        // from relative position (nearest side), not motion direction: a squeezed
        // player moving "down" is not necessarily above the opponent.
        if (!overlappedBefore && player.Body.Overlaps(opponent.Body))
        {
            Aabb other = opponent.Body;
            if (horizontal)
            {
                bool playerOnLeft = player.Position.X <= opponent.Position.X;
                float resolvedX = playerOnLeft
                    ? other.Left - player.BodyHalf.X - Skin
                    : other.Right + player.BodyHalf.X + Skin;
                player.Position = player.Position with { X = resolvedX };
                ResolveAxisVelocity(player, opponent, config, horizontal: true,
                    directionOfA: playerOnLeft ? -1f : 1f);
            }
            else
            {
                bool playerBelow = player.Position.Y <= opponent.Position.Y;
                float resolvedY = playerBelow
                    ? other.Bottom - player.BodyHalf.Y - Skin
                    : other.Top + player.BodyHalf.Y + Skin;
                player.Position = player.Position with { Y = resolvedY };
                ResolveAxisVelocity(player, opponent, config, horizontal: false,
                    directionOfA: playerBelow ? -1f : 1f);
            }
        }

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
    public static void ResolvePlayerContact(SimPlayer a, SimPlayer b, MatchConfig config)
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
            // Capped like Box2D's Baumgarte correction: deep overlaps resolve over
            // several ticks instead of one instant position jump (landing on the
            // opponent's head used to teleport a character sideways).
            float correction = MathF.Min(pen.X, config.MaxDepenetrationPerTick);
            float direction = a.Position.X <= b.Position.X ? -1f : 1f;
            a.Position += new Vec2(direction * correction * (b.Mass / total), 0f);
            b.Position -= new Vec2(direction * correction * (a.Mass / total), 0f);
            ResolveAxisVelocity(a, b, config, horizontal: true, direction);
        }
        else
        {
            float correction = MathF.Min(pen.Y, config.MaxDepenetrationPerTick);
            float direction = a.Position.Y <= b.Position.Y ? -1f : 1f;
            a.Position += new Vec2(0f, direction * correction * (b.Mass / total));
            b.Position -= new Vec2(0f, direction * correction * (a.Mass / total));
            ResolveAxisVelocity(a, b, config, horizontal: false, direction);
        }
    }

    private static void ResolveAxisVelocity(SimPlayer a, SimPlayer b, MatchConfig config, bool horizontal, float directionOfA)
    {
        float va = horizontal ? a.Velocity.X : a.Velocity.Y;
        float vb = horizontal ? b.Velocity.X : b.Velocity.Y;
        // Only resolve if they are moving toward each other along the contact axis.
        // directionOfA is the push-out direction of A (away from B), so the pair is
        // approaching iff (vb − va)·directionOfA > 0. (The original check was inverted:
        // it skipped approaching pairs and glued separating ones together.)
        if ((vb - va) * directionOfA <= 0f)
        {
            return;
        }
        float common = (a.Mass * va + b.Mass * vb) / (a.Mass + b.Mass);
        // Dash contact cap (2026-07-13, designer): a dashing player shoves but can
        // never KO — the velocity imparted to the NON-dashing side is clamped,
        // damage-independent. (The dasher re-asserts its own speed next tick.)
        float aNew = common, bNew = common;
        if (b.IsDashTraveling && !a.IsDashTraveling)
        {
            aNew = MathF.Abs(common) > config.DashContactPushCap
                ? MathF.Sign(common) * config.DashContactPushCap : common;
        }
        else if (a.IsDashTraveling && !b.IsDashTraveling)
        {
            bNew = MathF.Abs(common) > config.DashContactPushCap
                ? MathF.Sign(common) * config.DashContactPushCap : common;
        }
        a.Velocity = horizontal ? a.Velocity with { X = aNew } : a.Velocity with { Y = aNew };
        b.Velocity = horizontal ? b.Velocity with { X = bNew } : b.Velocity with { Y = bNew };
    }
}
