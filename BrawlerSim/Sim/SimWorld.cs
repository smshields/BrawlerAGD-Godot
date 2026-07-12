using BrawlerSim.Determinism;
using BrawlerSim.Genome;

namespace BrawlerSim.Sim;

/// <summary>
/// The match simulation. ALL gameplay lives behind Tick(); rendered play calls it once
/// per Godot physics frame, headless evaluation calls it in a loop — same code, same
/// results. Tick order is fixed and part of the determinism contract:
/// input/FSM (player 0 then 1) → physics (0 then 1) → player contact → hits → deaths.
/// </summary>
public sealed class SimWorld
{
    public MatchConfig Config { get; }
    public IReadOnlyList<SimPlayer> Players => _players;
    public IReadOnlyList<Aabb> Platforms { get; }
    public int TickCount { get; private set; }
    public bool IsOver { get; private set; }

    /// <summary>Index of the losing player once IsOver; -1 while running or on a draw.</summary>
    public int LoserIndex { get; private set; } = -1;

    private readonly SimPlayer[] _players;
    private readonly Aabb _blastZone;

    public SimWorld(GameGenome genome, MatchConfig? config = null)
    {
        Config = config ?? MatchConfig.Default;
        Platforms = genome.Stage.Platforms
            .Select(p => Aabb.FromRect(p.X, p.Y, p.XSize, p.YSize))
            .ToArray();
        _blastZone = new Aabb(Vec2.Zero, new Vec2(Config.BlastZoneHalfWidth, Config.BlastZoneHalfHeight));

        Vec2 spawn1 = ComputeSpawn(genome.Stage);
        Vec2 spawn2 = SafeSpawn(new Vec2(-spawn1.X, spawn1.Y), genome.Stage);
        _players = new SimPlayer[2];
        _players[0] = new SimPlayer(0, genome.Characters[0], spawn1, Config);
        _players[1] = new SimPlayer(1, genome.Characters[1], spawn2, Config);
    }

    public void Tick(ReadOnlySpan<InputFrame> inputs)
    {
        if (IsOver)
        {
            return;
        }

        // 1. Input + state machines, fixed player order.
        for (int i = 0; i < _players.Length; i++)
        {
            SimPlayer player = _players[i];
            if (player.InvincibleTicksLeft > 0)
            {
                player.InvincibleTicksLeft--;
            }
            player.StepStateMachine(inputs[i]);
        }

        // 2. Kinematics + collision (platforms and the opponent's body), fixed order.
        for (int i = 0; i < _players.Length; i++)
        {
            SimPhysics.Step(_players[i], _players[1 - i], Platforms, Config);
        }

        // 3. Body-vs-body contact, then shield spacing (2026-07-12: a raised shield
        //    expels the opponent — fixed player order, positional push capped per tick
        //    plus a low outward velocity floor; FEATURES.md "never enough to kill").
        SimPhysics.ResolvePlayerContact(_players[0], _players[1], Config);
        for (int i = 0; i < _players.Length; i++)
        {
            PushWithShield(shielder: _players[i], opponent: _players[1 - i]);
        }

        // 4. Hit detection, fixed attacker order.
        for (int i = 0; i < _players.Length; i++)
        {
            TryHit(attacker: _players[i], victim: _players[1 - i]);
        }

        // 5. Blast zone → stock loss / match end. Fixed player order; the first fatal
        //    exit ends the match (simultaneous KOs resolve to the lower player index).
        foreach (SimPlayer player in _players)
        {
            if (IsOver)
            {
                break;
            }
            if (!player.Body.Overlaps(_blastZone))
            {
                // Unity parity: dying with 0 stocks ends the match; otherwise decrement
                // and respawn (i.e. "3 stocks" = 4 lives, matching the shipped game and
                // the study's description of four-stock survival matches).
                if (player.Stocks == 0)
                {
                    IsOver = true;
                    LoserIndex = player.Index;
                }
                else
                {
                    player.Respawn();
                }
            }
        }

        TickCount++;
        if (!IsOver && TickCount >= Config.MaxTicks)
        {
            IsOver = true; // timeout draw, LoserIndex stays -1
        }
    }

    private void PushWithShield(SimPlayer shielder, SimPlayer opponent)
    {
        SimShield? shield = shielder.ActiveShield;
        float radius = shielder.ShieldRadius;
        if (shield is null || radius <= 0f)
        {
            return;
        }
        Vec2 center = shielder.Position + shielder.ShieldOffset;
        Vec2 closest = opponent.Body.ClosestPoint(center);
        Vec2 toClosest = closest - center;
        if (toClosest.Length() >= radius)
        {
            return;
        }
        // Push direction: radially from the shield center through the opponent's
        // center (facing fallback for the degenerate concentric case).
        Vec2 direction = opponent.Position - center;
        float length = direction.Length();
        direction = length > 0.0001f ? direction * (1f / length) : new Vec2(shielder.Facing, 0f);

        // Positional: expel toward the circle edge, capped per tick (no teleports).
        float penetration = radius - toClosest.Length();
        float step = MathF.Min(penetration, Config.ShieldPushMaxPerTick);
        opponent.Position += direction * step;

        // Velocity floor: the opponent leaves at least at the shield's spacing push.
        float radial = opponent.Velocity.X * direction.X + opponent.Velocity.Y * direction.Y;
        if (radial < shield.SpacingPush)
        {
            opponent.Velocity += direction * (shield.SpacingPush - radial);
        }
    }

    /// <summary>
    /// Unity hit semantics (single clean path — the Enter/Stay/Exit duplication is not
    /// ported): damage first, then knockback = (victim − hitbox center + unit knockback
    /// direction) · scalar · (victim damage · 0.1), then hitstun scaled by victim damage.
    /// Shield interception (2026-07-12): a hit is BLOCKED iff the overlap region between
    /// the attack hitbox and the victim's body lies entirely inside the shield circle —
    /// partial cover is still a clean hit ("a shield only protects where it covers").
    /// Blocked: zero damage/stun, knockback scaled by (1 − reduction), shield health
    /// loses damage × hitDegradationScalar (break applies immediately).
    /// </summary>
    private void TryHit(SimPlayer attacker, SimPlayer victim)
    {
        if (!attacker.HitboxActive || victim.InvincibleTicksLeft > 0)
        {
            return;
        }
        Aabb hitbox = attacker.Hitbox;
        if (!hitbox.Overlaps(victim.Body))
        {
            return;
        }

        SimShield? shield = victim.ActiveShield;
        if (shield is not null && victim.ShieldRadius > 0f
            && OverlapFullyInsideShield(hitbox, victim.Body,
                victim.Position + victim.ShieldOffset, victim.ShieldRadius))
        {
            float blockedDamageAfter = victim.Damage + attacker.Move.DamageGiven;
            Vec2 blockedKnockback = ComputeKnockback(
                victim.Position, hitbox.Center, attacker.Move.KnockbackDirection,
                attacker.Facing, attacker.Move.KnockbackScalar, blockedDamageAfter);
            victim.Velocity += blockedKnockback * (1f - shield.KnockbackReduction);
            victim.BlockedHits++;
            victim.InvincibleTicksLeft = Config.InvincibilityTicks;
            victim.ShieldHealths[victim.CurrentMoveIndex] -=
                attacker.Move.DamageGiven * shield.HitDegradationScalar;
            if (victim.ShieldHealths[victim.CurrentMoveIndex] <= victim.ShieldBreakRadius)
            {
                victim.BreakShield();
            }
            return;
        }

        float damageAfterHit = victim.Damage + attacker.Move.DamageGiven;
        Vec2 knockback = ComputeKnockback(
            victim.Position, hitbox.Center, attacker.Move.KnockbackDirection,
            attacker.Facing, attacker.Move.KnockbackScalar, damageAfterHit);

        int stunTicks = Config.ToTicks(
            attacker.Move.HitstunDuration * damageAfterHit * victim.HitstunDamageScalar);
        if (!float.IsPositiveInfinity(Config.MaxStunSeconds))
        {
            stunTicks = Math.Min(stunTicks, Config.ToTicks(Config.MaxStunSeconds));
        }

        victim.ApplyHit(attacker.Move.DamageGiven, knockback, stunTicks);
        victim.InvincibleTicksLeft = Config.InvincibilityTicks;
    }

    /// <summary>
    /// Unity knockback formula, verbatim: (victim − hitbox center [unnormalized] + unit
    /// knockback direction [x mirrored by facing]) · scalar · (victim damage AFTER the
    /// hit · 0.1). Public and static so tests can pin it against hand-computed values.
    /// </summary>
    public static Vec2 ComputeKnockback(
        Vec2 victimPosition, Vec2 hitboxCenter, Vec2 knockbackDirection,
        int attackerFacing, float knockbackScalar, float damageAfterHit)
    {
        if (attackerFacing < 0)
        {
            knockbackDirection = knockbackDirection with { X = -knockbackDirection.X };
        }
        return (victimPosition - hitboxCenter + knockbackDirection)
            * knockbackScalar
            * (damageAfterHit * 0.1f);
    }

    /// <summary>The rect where the hitbox meets the body, tested against the shield
    /// circle. A rect is inside a circle iff all four corners are (convexity) — exact,
    /// not approximate.</summary>
    private static bool OverlapFullyInsideShield(Aabb hitbox, Aabb body, Vec2 center, float radius)
    {
        float left = MathF.Max(hitbox.Left, body.Left);
        float right = MathF.Min(hitbox.Right, body.Right);
        float bottom = MathF.Max(hitbox.Bottom, body.Bottom);
        float top = MathF.Min(hitbox.Top, body.Top);
        float r2 = radius * radius;
        return Inside(left, bottom) && Inside(left, top) && Inside(right, bottom) && Inside(right, top);

        bool Inside(float x, float y)
        {
            float dx = x - center.X;
            float dy = y - center.Y;
            return dx * dx + dy * dy <= r2;
        }
    }

    /// <summary>FNV-1a fingerprint of complete gameplay state. Equal hashes ⇔ equal states.</summary>
    public ulong StateHash()
    {
        ulong hash = Fnv1a.OffsetBasis;
        hash = Fnv1a.Add(hash, TickCount);
        foreach (SimPlayer p in _players)
        {
            hash = Fnv1a.Add(hash, p.Position.X);
            hash = Fnv1a.Add(hash, p.Position.Y);
            hash = Fnv1a.Add(hash, p.Velocity.X);
            hash = Fnv1a.Add(hash, p.Velocity.Y);
            hash = Fnv1a.Add(hash, p.Damage);
            hash = Fnv1a.Add(hash, p.Stocks);
            hash = Fnv1a.Add(hash, (int)p.State);
            hash = Fnv1a.Add(hash, p.PhaseTicksLeft);
            hash = Fnv1a.Add(hash, p.Facing);
            hash = Fnv1a.Add(hash, p.JumpsExhausted ? 1 : 0);
            hash = Fnv1a.Add(hash, p.InvincibleTicksLeft);
            // 2026-07-08 multi-move controls: which move is in flight is now mutable
            // state and must be fingerprinted (an unhashed field is a determinism hole).
            hash = Fnv1a.Add(hash, p.CurrentMoveIndex);
            // 2026-07-12 shields: phase, aim, activating button, per-slot health.
            hash = Fnv1a.Add(hash, (int)p.ShieldPhase);
            hash = Fnv1a.Add(hash, p.ShieldOffset.X);
            hash = Fnv1a.Add(hash, p.ShieldOffset.Y);
            hash = Fnv1a.Add(hash, p.ShieldButton);
            hash = Fnv1a.Add(hash, p.StunFromShieldBreak ? 1 : 0);
            foreach (float health in p.ShieldHealths)
            {
                hash = Fnv1a.Add(hash, health);
            }
        }
        return hash;
    }

    public MatchResult BuildResult(Replay.InputTrace? trace = null) =>
        new(
            _players.Select(p => new PlayerStats(
                p.TotalDamageTaken, p.TotalHitsReceived, p.Stocks, p.RecoveryTicks,
                p.CompletedStockDamage.Append(p.Damage).ToArray(),
                p.MoveUses.ToArray(), p.StunTicks, p.Jumps,
                p.ShieldActivations, p.BlockedHits, p.ShieldBreaks, p.ShieldTicks)).ToArray(),
            LoserIndex,
            TickCount,
            TickCount / (float)Config.TicksPerSecond,
            StateHash(),
            trace);

    /// <summary>
    /// Unity spawn rules (ArenaManager): player 1 spawns centered above the initial
    /// platform, +2 above its top, nudged upward while inside any platform; player 2
    /// mirrors across x = 0 with the same nudge.
    /// </summary>
    private static Vec2 ComputeSpawn(StageGenome stage)
    {
        PlatformGene initial = stage.Platforms[0];
        int x = initial.X + (initial.XSize + 1) / 2;
        int y = initial.Y + initial.YSize + 2;
        return SafeSpawn(new Vec2(x, y), stage);
    }

    private static Vec2 SafeSpawn(Vec2 candidate, StageGenome stage)
    {
        float y = candidate.Y;
        while (SpawnInsideAnyPlatform(candidate.X, y, stage))
        {
            y += 1f;
        }
        return new Vec2(candidate.X, y);
    }

    private static bool SpawnInsideAnyPlatform(float x, float y, StageGenome stage)
    {
        foreach (PlatformGene p in stage.Platforms)
        {
            if (x >= p.X && x <= p.X + p.XSize && y >= p.Y && y <= p.Y + p.YSize)
            {
                return true;
            }
        }
        return false;
    }
}
