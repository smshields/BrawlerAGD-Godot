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

    /// <summary>Index of the losing player once IsOver; -1 while running or on a
    /// 2P timeout draw. Under STOCK with 3-4 players this is the FIRST eliminated
    /// player (last place); under TIMED, the last-ranked player.</summary>
    public int LoserIndex { get; private set; } = -1;

    /// <summary>Player indices in elimination order, earliest first (2026-08-12,
    /// STOCK rule with 3-4 players). Empty in TIMED matches and 2P timeouts.</summary>
    public IReadOnlyList<int> EliminationOrder => _eliminationOrder;
    private readonly List<int> _eliminationOrder = new();

    private readonly SimPlayer[] _players;
    private readonly Aabb _blastZone;

    // Spawning Behaviors (2026-07-22, docs/features/spawn-and-polish.md). The feature is
    // a per-level property; off ⇒ every path below is bypassed and the sim is
    // byte-for-byte pre-feature (including the state hash — its spawn section is gated).
    private readonly bool _spawnFeatureActive;
    private readonly int _platformSpawnTicks;
    private readonly int _spawnInvulnTicks;
    private readonly Aabb[] _spawnPads;          // per-player pad geometry (static)
    private readonly Aabb[][] _platformsWithPad; // static platforms + that player's pad

    /// <summary>The spawn pad for player i (2026-07-22) — a thin platform under the
    /// spawn point, solid to its owner only. Active state lives on the player
    /// (SpawnPadActive); this is the fixed geometry, for the view.</summary>
    public Aabb SpawnPad(int playerIndex) => _spawnPads[playerIndex];

    /// <summary>Live projectiles in spawn order — the first non-player entities in
    /// the sim (2026-07-14). List order is part of the determinism contract.</summary>
    public IReadOnlyList<SimProjectile> Projectiles => _projectiles;
    private readonly List<SimProjectile> _projectiles = new();

    /// <summary>The KO boundary, genome-driven since Map Size (2026-07-21): visible
    /// half extents × (1 + koMargin). Legacy stage params reproduce the old
    /// MatchConfig constants bit-exactly (regression-tested).</summary>
    public Aabb BlastZone => _blastZone;

    /// <summary>Visible-map half extents (the camera's max zoom-out box).</summary>
    public Vec2 VisibleHalf { get; }

    /// <summary>
    /// AI platform-sensing half extents (2026-07-21, DEVIATIONS #27): the fixed Unity
    /// 20×15 sense box scaled by how much larger than legacy this map is (never
    /// scaled DOWN — small maps keep the full instrument). On legacy-size maps the
    /// factor is exactly 1, leaving the fitness instrument untouched.
    /// </summary>
    public Vec2 PlatformSenseHalf { get; }

    public SimWorld(GameGenome genome, MatchConfig? config = null)
    {
        Config = config ?? MatchConfig.Default;
        Platforms = genome.Stage.Platforms
            .Select(p => Aabb.FromRect(p.X, p.Y, p.XSize, p.YSize))
            .ToArray();

        Params.ParamSet stage = genome.Stage.Params;
        VisibleHalf = new Vec2(
            stage.Get(StageParams.VisibleHalfWidth), stage.Get(StageParams.VisibleHalfHeight));
        _blastZone = new Aabb(Vec2.Zero, StageRules.BlastHalfExtents(stage));
        PlatformSenseHalf = new Vec2(
            Config.PlatformSenseHalfWidth
                * MathF.Max(1f, VisibleHalf.X / StageRules.LegacyVisibleHalfWidth),
            Config.PlatformSenseHalfHeight
                * MathF.Max(1f, VisibleHalf.Y / StageRules.LegacyVisibleHalfHeight));

        // Spawn genes are consumed as stored (repair lives in the genetic ops, see
        // StageRules.RepairSpawns) — only the legacy inside-a-platform nudge applies,
        // exactly as the pre-feature sim did. 2-4 players since 2026-08-12
        // (four-player.md): player i takes spawn gene i (all stages carry four).
        int count = genome.Characters.Count;
        _players = new SimPlayer[count];
        for (int i = 0; i < count; i++)
        {
            Vec2 spawn = StageRules.LegacySafeSpawn(
                StageRules.SpawnOf(stage, i), genome.Stage.Platforms);
            _players[i] = new SimPlayer(i, genome.Characters[i], spawn, Config);
        }

        // Spawning Behaviors (2026-07-22): per-level durations → tick counts + feature
        // gate. The pad sits just under each spawn body's feet (SpawnPosition is the
        // body center at the +2 hover point). _platformsWithPad[i] is the owner's
        // collision set; the other players' physics never receives it, so they phase
        // through.
        _platformSpawnTicks = Config.ToTicks(StageRules.PlatformSpawnSeconds(stage));
        _spawnInvulnTicks = Config.ToTicks(StageRules.SpawnInvulnSeconds(stage));
        _spawnFeatureActive = StageRules.SpawnFeatureActive(stage);
        _spawnPads = new Aabb[count];
        _platformsWithPad = new Aabb[count][];
        for (int i = 0; i < count; i++)
        {
            SimPlayer p = _players[i];
            var padCenter = new Vec2(
                p.SpawnPosition.X, p.SpawnPosition.Y - p.BodyHalf.Y - Config.SpawnPadHalfHeight);
            _spawnPads[i] = new Aabb(padCenter, new Vec2(Config.SpawnPadHalfWidth, Config.SpawnPadHalfHeight));
            var withPad = new Aabb[Platforms.Count + 1];
            for (int k = 0; k < Platforms.Count; k++)
            {
                withPad[k] = Platforms[k];
            }
            withPad[^1] = _spawnPads[i];
            _platformsWithPad[i] = withPad;
        }
        if (_spawnFeatureActive)
        {
            // Match start (2026-07-22 designer): appear on the pad immediately — the 3 s
            // blackout is respawns only.
            foreach (SimPlayer p in _players)
            {
                p.Materialize(_platformSpawnTicks, _spawnInvulnTicks);
            }
        }
    }

    public void Tick(ReadOnlySpan<InputFrame> inputs)
    {
        if (IsOver)
        {
            return;
        }

        // 0. Spawn lifecycle (2026-07-22): blackout countdown → materialize on the pad;
        //    spawn-timer countdowns for present players. Absent (blacked-out) players
        //    take no input/FSM this tick. Whole block is a no-op when the feature is off.
        // 1. Input + state machines, fixed player order.
        for (int i = 0; i < _players.Length; i++)
        {
            SimPlayer player = _players[i];
            if (player.Eliminated)
            {
                continue; // out of the match for good (2026-08-12)
            }
            if (player.RespawnBlackoutLeft > 0)
            {
                if (--player.RespawnBlackoutLeft == 0)
                {
                    player.Materialize(_platformSpawnTicks, _spawnInvulnTicks);
                    player.StepStateMachine(inputs[i]); // fresh timers; skip this tick's countdowns
                }
                continue; // still absent, or just materialized (already stepped)
            }
            if (player.SpawnInvulnTicksLeft > 0)
            {
                player.SpawnInvulnTicksLeft--;
            }
            if (player.SpawnPadActive && --player.SpawnPadTicksLeft <= 0)
            {
                player.SpawnPadActive = false;  // platform lifetime expired
                player.SpawnIntangible = false;
            }
            if (player.InvincibleTicksLeft > 0)
            {
                player.InvincibleTicksLeft--;
            }
            player.StepStateMachine(inputs[i]);
        }

        // 2. Kinematics + collision. The spawn pad is solid to its OWNER only (the
        //    other players' steps never receive it → they phase through). Absent
        //    (blacked-out or eliminated) players skip.
        for (int i = 0; i < _players.Length; i++)
        {
            if (_players[i].IsAbsent)
            {
                continue;
            }
            IReadOnlyList<Aabb> plats = _players[i].SpawnPadActive ? _platformsWithPad[i] : Platforms;
            SimPhysics.Step(_players[i], _players, plats, Config);
        }

        // 2.5. Spawn-pad leave detection (2026-07-22): once the owner is no longer
        //      resting on the pad it despawns and intangibility ends immediately.
        for (int i = 0; i < _players.Length; i++)
        {
            SimPlayer player = _players[i];
            if (player.SpawnPadActive && LeftPad(player, _spawnPads[i]))
            {
                player.SpawnPadActive = false;
                player.SpawnIntangible = false;
            }
        }

        // 3. Body-vs-body contact, then shield spacing (2026-07-12: a raised shield
        //    expels the opponent — fixed player order, positional push capped per tick
        //    plus a low outward velocity floor; FEATURES.md "never enough to kill").
        //    All pairs in fixed index order since 2026-08-12 (identical to the old
        //    two-player block at N=2); pairs with an absent player skip.
        for (int i = 0; i < _players.Length; i++)
        {
            if (_players[i].IsAbsent)
            {
                continue;
            }
            for (int j = i + 1; j < _players.Length; j++)
            {
                if (!_players[j].IsAbsent)
                {
                    SimPhysics.ResolvePlayerContact(_players[i], _players[j], Config);
                }
            }
        }
        for (int i = 0; i < _players.Length; i++)
        {
            if (_players[i].IsAbsent)
            {
                continue;
            }
            for (int j = 0; j < _players.Length; j++)
            {
                if (j != i && !_players[j].IsAbsent)
                {
                    PushWithShield(shielder: _players[i], opponent: _players[j]);
                }
            }
        }

        // 3.5. Projectiles (2026-07-14): step lives (closed-form reposition, then the
        //      despawn checks — TTL, decayed-to-nothing, past the blast boundary,
        //      platform contact [platforms DESTROY projectiles, designer]), consume
        //      pending spawns, then projectile-vs-player hits. Fixed list order,
        //      victims in player order — all part of the tick-order contract.
        StepProjectiles();

        // 3.9. KO attribution clock (2026-08-12, four-player.md): continuous grounding
        //      clears enemy influence — "until next landing". Runs BEFORE this tick's
        //      hits so a fresh hit restarts the clock. Stats-class state only;
        //      gameplay never reads it.
        foreach (SimPlayer player in _players)
        {
            if (player.IsAbsent)
            {
                continue;
            }
            if (player.IsGrounded)
            {
                if (++player.GroundedInfluenceTicks >= Config.InfluenceGroundClearTicks)
                {
                    player.LastInfluencer = -1;
                }
            }
            else
            {
                player.GroundedInfluenceTicks = 0;
            }
        }

        // 4. Hit detection, fixed attacker order (all ordered pairs since 2026-08-12 —
        //    (0,1),(1,0) at N=2, exactly the old sweep).
        for (int i = 0; i < _players.Length; i++)
        {
            for (int j = 0; j < _players.Length; j++)
            {
                if (j != i)
                {
                    TryHit(attacker: _players[i], victim: _players[j]);
                }
            }
        }

        // 5. Blast zone → stock loss / match end. Fixed player order; the first
        //    match-ending exit stops the sweep (simultaneous KOs resolve to the lower
        //    player index). Each death first resolves KO attribution (2026-08-12):
        //    live influence credits the influencer, otherwise it is a self-destruct.
        foreach (SimPlayer player in _players)
        {
            if (IsOver)
            {
                break;
            }
            if (player.IsAbsent)
            {
                continue; // blacked-out or eliminated — cannot be KO'd
            }
            if (!player.Body.Overlaps(_blastZone))
            {
                if (player.LastInfluencer >= 0)
                {
                    _players[player.LastInfluencer].KOs++;
                }
                else
                {
                    player.SelfDestructs++;
                }
                if (Config.EndRule == MatchEndRule.Timed)
                {
                    // TIMED (2026-08-12): infinite stocks — respawn always, restoring
                    // the decrement the shared respawn path applies. The clock alone
                    // ends the match.
                    if (_spawnFeatureActive)
                    {
                        player.BeginRespawn(Config.RespawnBlackoutTicks);
                    }
                    else
                    {
                        player.Respawn();
                    }
                    player.Stocks++;
                }
                // Unity parity: dying with 0 stocks is fatal; otherwise decrement and
                // respawn (i.e. "3 stocks" = 4 lives, matching the shipped game and
                // the study's description of four-stock survival matches).
                else if (player.Stocks == 0)
                {
                    // Elimination (2026-08-12): out for good; the match continues
                    // until one player remains. At N=2 the first elimination leaves
                    // one — the match ends this same tick, exactly the legacy rule.
                    player.Eliminated = true;
                    _eliminationOrder.Add(player.Index);
                    int live = 0;
                    foreach (SimPlayer p in _players)
                    {
                        if (!p.Eliminated)
                        {
                            live++;
                        }
                    }
                    if (live <= 1)
                    {
                        IsOver = true;
                        LoserIndex = _eliminationOrder[0];
                    }
                }
                else if (_spawnFeatureActive)
                {
                    // 2026-07-22: go absent for the blackout, then Materialize on the pad.
                    player.BeginRespawn(Config.RespawnBlackoutTicks);
                }
                else
                {
                    player.Respawn(); // instant (pre-feature parity)
                }
            }
        }

        TickCount++;
        if (!IsOver && TickCount >= Config.MaxTicks)
        {
            IsOver = true;
            // TIMED: rank by KOs / damage dealt / index — the loser is last place.
            // STOCK with eliminations (3-4 players): the first eliminated player is
            // the loser; a no-elimination timeout stays a draw (legacy 2P semantics).
            if (Config.EndRule == MatchEndRule.Timed)
            {
                int[] placements = ComputePlacements();
                for (int i = 0; i < placements.Length; i++)
                {
                    if (placements[i] == _players.Length)
                    {
                        LoserIndex = i;
                    }
                }
            }
            else if (_eliminationOrder.Count > 0)
            {
                LoserIndex = _eliminationOrder[0];
            }
        }
    }

    private void StepProjectiles()
    {
        for (int i = 0; i < _projectiles.Count; i++)
        {
            SimProjectile proj = _projectiles[i];
            proj.AgeTicks++;      // lifetime clock: TTL + damage decay (survives reflection)
            proj.PathAgeTicks++;  // path clock: resets when a reflect re-fires the bolt
            proj.Position = proj.Move.PositionAt(proj.Origin, proj.Facing, proj.PathAgeTicks, Config);
            proj.Angle = proj.Move.RotationRate * proj.AgeTicks * Config.Dt;
            proj.DamageScale = proj.Move.DamageScaleAt(proj.AgeTicks, Config);
            if (proj.AgeTicks >= proj.Move.TtlTicks
                || proj.DamageScale <= 0f
                || !InsideBlastZone(proj.Position)
                || CenterInsidePlatform(proj.Position))
            {
                proj.Alive = false;
            }
        }
        _projectiles.RemoveAll(p => !p.Alive);

        foreach (SimPlayer player in _players)
        {
            if (!player.ProjectileSpawnPending)
            {
                continue;
            }
            player.ProjectileSpawnPending = false;
            SimProjectileMove move = player.ProjectileMoves[player.CurrentMoveIndex]!;
            // The sketch's EXIT point: launch fractions × body half extents, the X
            // side mirrored by facing. Age 0 at the origin this tick; motion begins
            // next tick.
            Vec2 origin = player.Position + new Vec2(
                move.LaunchFraction.X * player.BodyHalf.X * player.Facing,
                move.LaunchFraction.Y * player.BodyHalf.Y);
            _projectiles.Add(new SimProjectile(move, player.Index, player.CurrentMoveIndex, origin, player.Facing));
            player.ProjectilesFired++;
        }

        foreach (SimProjectile proj in _projectiles)
        {
            TryProjectileHit(proj);
        }
        _projectiles.RemoveAll(p => !p.Alive);
    }

    /// <summary>
    /// Projectile hit resolution mirrors TryHit's pipeline stage for stage:
    /// invincibility skip → dash i-frames negate-and-count (the projectile PASSES
    /// THROUGH — evasion beats the bullet) → shield full-coverage block (degrades
    /// the shield, consumes the projectile) → clean hit (melee knockback formula ×
    /// the decay scale, DI, capped stun). A projectile is spent by any hit or block.
    /// The owner is immune until the projectile first clears their body, then only
    /// the hitsSelf gene exposes them.
    /// </summary>
    private void TryProjectileHit(SimProjectile proj)
    {
        for (int v = 0; v < _players.Length && proj.Alive; v++)
        {
            SimPlayer victim = _players[v];
            bool overlaps = proj.OverlapsBody(victim.Body);
            if (v == proj.Owner)
            {
                if (!proj.ClearedOwner)
                {
                    if (!overlaps)
                    {
                        proj.ClearedOwner = true;
                    }
                    continue; // still leaving the barrel — never a self-hit yet
                }
                if (!proj.Move.HitsSelf)
                {
                    continue;
                }
            }
            // Spawn immunity (2026-07-22): an intangible or invulnerable victim takes no
            // damage/knockback; an absent (blacked-out or eliminated) one isn't on
            // stage. The bolt passes THROUGH rather than being consumed (nothing was
            // blocked).
            if (!overlaps || victim.InvincibleTicksLeft > 0
                || victim.SpawnDamageImmune || victim.IsAbsent)
            {
                continue;
            }
            // Dash reflection (2026-07-20): any contact during the Dash state with the
            // reflect gene re-fires the bolt — independent of (and checked before)
            // i-frames, so a reflect-dash reflects even in a non-invulnerable stage.
            if (victim.State == PlayerState.Dash && victim.ActiveDash is { Reflect: true })
            {
                proj.ReflectFrom(victim.Index, TickCount);
                victim.ProjectilesReflected++;
                return; // re-seated: resume against it next tick from the new path
            }
            if (victim.DashInvulnerable)
            {
                victim.DashInvulnDodges++;
                continue;
            }

            float scaledDamage = proj.Move.DamageGiven * proj.DamageScale;
            SimShield? shield = victim.ActiveShield;
            if (shield is not null && victim.ShieldRadius > 0f
                && OverlapFullyInsideShield(proj.Bounds, victim.Body,
                    victim.Position + victim.ShieldOffset, victim.ShieldRadius))
            {
                // Shield reflection (2026-07-20): the full-coverage geometry that
                // would BLOCK instead re-fires the bolt at its shooter. The shield
                // still degrades as if it blocked (the work isn't free — designer
                // can veto); pokes through partial cover still hit either way.
                if (shield.Reflect)
                {
                    proj.ReflectFrom(victim.Index, TickCount);
                    victim.ProjectilesReflected++;
                    victim.ShieldHealths[victim.CurrentMoveIndex] -= scaledDamage * shield.HitDegradationScalar;
                    if (victim.ShieldHealths[victim.CurrentMoveIndex] <= victim.ShieldBreakRadius)
                    {
                        victim.BreakShield();
                    }
                    return;
                }
                float blockedDamageAfter = victim.Damage + scaledDamage;
                Vec2 blockedKnockback = ComputeKnockback(
                    victim.Position, proj.Position, proj.Move.KnockbackDirection,
                    proj.Facing, proj.Move.KnockbackScalar * proj.DamageScale, blockedDamageAfter);
                victim.Velocity += blockedKnockback * (1f - shield.KnockbackReduction);
                victim.BlockedHits++;
                if (proj.Owner != victim.Index)
                {
                    victim.MarkInfluence(proj.Owner); // blocked knockback still shoves
                }
                victim.InvincibleTicksLeft = Config.InvincibilityTicks;
                victim.ShieldHealths[victim.CurrentMoveIndex] -= scaledDamage * shield.HitDegradationScalar;
                if (victim.ShieldHealths[victim.CurrentMoveIndex] <= victim.ShieldBreakRadius)
                {
                    victim.BreakShield();
                }
                proj.Alive = false;
                return;
            }

            float damageAfterHit = victim.Damage + scaledDamage;
            Vec2 knockback = ComputeKnockback(
                victim.Position, proj.Position, proj.Move.KnockbackDirection,
                proj.Facing, proj.Move.KnockbackScalar * proj.DamageScale, damageAfterHit);
            knockback = ApplyDirectionalInfluence(victim, knockback);
            int stunTicks = Config.ToTicks(
                proj.Move.HitstunDuration * damageAfterHit * victim.HitstunDamageScalar);
            if (!float.IsPositiveInfinity(Config.MaxStunSeconds))
            {
                stunTicks = Math.Min(stunTicks, Config.ToTicks(Config.MaxStunSeconds));
            }
            victim.ApplyHit(scaledDamage, knockback, stunTicks);
            // KO attribution (2026-08-12): a self-hit (hitsSelf gene) influences and
            // credits nobody — dying to your own bolt is a self-destruct.
            if (proj.Owner != victim.Index)
            {
                victim.MarkInfluence(proj.Owner);
                _players[proj.Owner].DamageDealt += scaledDamage;
            }
            victim.InvincibleTicksLeft = Config.InvincibilityTicks;
            _players[proj.Owner].ProjectileHits++;
            proj.Alive = false;
        }
    }

    /// <summary>The owner is no longer resting on its spawn pad (2026-07-22): its feet
    /// left the pad's span or lifted off the pad's top. Tolerance matches the physics
    /// skin so a settled body reads as still-on.</summary>
    private static bool LeftPad(SimPlayer player, in Aabb pad)
    {
        const float tol = 0.05f;
        float feet = player.Body.Bottom;
        return player.Position.X < pad.Left || player.Position.X > pad.Right
            || feet > pad.Top + tol || feet < pad.Top - tol;
    }

    private bool InsideBlastZone(Vec2 p) =>
        p.X >= _blastZone.Left && p.X <= _blastZone.Right
        && p.Y >= _blastZone.Bottom && p.Y <= _blastZone.Top;

    private bool CenterInsidePlatform(Vec2 p)
    {
        foreach (Aabb platform in Platforms)
        {
            if (p.X >= platform.Left && p.X <= platform.Right
                && p.Y >= platform.Bottom && p.Y <= platform.Top)
            {
                return true;
            }
        }
        return false;
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
        opponent.MarkInfluence(shielder.Index); // a shield expel is a real push (2026-08-12)
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
        // Spawn immunity / blackout / elimination (2026-07-22, 2026-08-12): no damage
        // to an intangible/invulnerable or absent victim; an absent attacker has no
        // live hitbox.
        if (!attacker.HitboxActive || victim.InvincibleTicksLeft > 0
            || victim.SpawnDamageImmune || victim.IsAbsent || attacker.IsAbsent)
        {
            return;
        }
        Aabb hitbox = attacker.Hitbox;
        if (!hitbox.Overlaps(victim.Body))
        {
            return;
        }

        // Dash i-frames (2026-07-13): a hit that WOULD have landed is negated and
        // counted — the research data sees evasion value even with fitness blind.
        if (victim.DashInvulnerable)
        {
            victim.DashInvulnDodges++;
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
            victim.MarkInfluence(attacker.Index); // blocked knockback still shoves (2026-08-12)
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

        // Directional influence (2026-07-13, FEATURES.md §DI): the victim's held
        // direction at the hit instant deflects the knockback slightly (≤10% gene)
        // and, when held near-opposite (within 45°), trims its magnitude (≤20% gene).
        // SKIPPED while shielding — including pokes through partial cover: a shielder
        // is committed to the shield, not influencing (designer clarification).
        knockback = ApplyDirectionalInfluence(victim, knockback);

        int stunTicks = Config.ToTicks(
            attacker.Move.HitstunDuration * damageAfterHit * victim.HitstunDamageScalar);
        if (!float.IsPositiveInfinity(Config.MaxStunSeconds))
        {
            stunTicks = Math.Min(stunTicks, Config.ToTicks(Config.MaxStunSeconds));
        }

        victim.ApplyHit(attacker.Move.DamageGiven, knockback, stunTicks);
        victim.MarkInfluence(attacker.Index); // KO attribution (2026-08-12)
        attacker.DamageDealt += attacker.Move.DamageGiven;
        victim.InvincibleTicksLeft = Config.InvincibilityTicks;
    }

    private static Vec2 ApplyDirectionalInfluence(SimPlayer victim, Vec2 knockback)
    {
        if (victim.DirectionalInfluence <= 0f || victim.State == PlayerState.Shield)
        {
            return knockback;
        }
        Vec2 held = victim.HeldDirection;
        float heldLength = held.Length();
        float magnitude = knockback.Length();
        if (heldLength <= 0f || magnitude <= 0f)
        {
            return knockback;
        }
        Vec2 heldUnit = held * (1f / heldLength);
        Vec2 result = knockback + heldUnit * (victim.DirectionalInfluence * magnitude);
        // Opposite-hold reduction: alignment within 45° of straight-against.
        Vec2 kbUnit = knockback * (1f / magnitude);
        float dot = heldUnit.X * kbUnit.X + heldUnit.Y * kbUnit.Y;
        if (dot < -0.70710678f)
        {
            result *= 1f - victim.DiKnockbackReduction;
        }
        victim.DIInfluencedHits++;
        return result;
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
            // 2026-07-13 dash: stage, locked direction, per-airtime budget.
            hash = Fnv1a.Add(hash, (int)p.DashPhase);
            hash = Fnv1a.Add(hash, p.DashDirection.X);
            hash = Fnv1a.Add(hash, p.DashDirection.Y);
            hash = Fnv1a.Add(hash, p.AirDashUsed ? 1 : 0);
            // 2026-07-13 fast fall / crouch / DI.
            hash = Fnv1a.Add(hash, p.HeldDirection.X);
            hash = Fnv1a.Add(hash, p.HeldDirection.Y);
            hash = Fnv1a.Add(hash, (int)p.CrouchPhase);
            hash = Fnv1a.Add(hash, p.QueuedCrouchAction);
            foreach (float health in p.ShieldHealths)
            {
                hash = Fnv1a.Add(hash, health);
            }
        }
        // 2026-07-14 projectiles: the section is appended ONLY when projectiles are
        // live — projectile-less matches (every pre-v5 game) hash exactly as before,
        // which is what keeps the golden pins valid without re-pinning. Safe because
        // gated sections are suffixes: "no section" and "count 0" cannot collide.
        if (_projectiles.Count > 0)
        {
            hash = Fnv1a.Add(hash, _projectiles.Count);
            foreach (SimProjectile proj in _projectiles)
            {
                hash = Fnv1a.Add(hash, proj.Owner);
                hash = Fnv1a.Add(hash, proj.MoveIndex);
                hash = Fnv1a.Add(hash, proj.AgeTicks);
                hash = Fnv1a.Add(hash, proj.Position.X);
                hash = Fnv1a.Add(hash, proj.Position.Y);
                hash = Fnv1a.Add(hash, proj.Angle);
                hash = Fnv1a.Add(hash, proj.DamageScale);
                hash = Fnv1a.Add(hash, proj.ClearedOwner ? 1 : 0);
                // 2026-07-20 reflection made origin/facing/path-age mutable state.
                hash = Fnv1a.Add(hash, proj.PathAgeTicks);
                hash = Fnv1a.Add(hash, proj.Origin.X);
                hash = Fnv1a.Add(hash, proj.Origin.Y);
                hash = Fnv1a.Add(hash, proj.Facing);
                hash = Fnv1a.Add(hash, proj.ReflectTick);
            }
        }
        // 2026-07-22 spawning behaviors: another gated suffix, appended ONLY when the
        // per-level feature is active — feature-off matches (every pre-v8 game, all the
        // golden pins) hash exactly as before. Safe for the same suffix reason as the
        // projectile section: "no section" cannot collide with a present one.
        if (_spawnFeatureActive)
        {
            foreach (SimPlayer p in _players)
            {
                hash = Fnv1a.Add(hash, p.RespawnBlackoutLeft);
                hash = Fnv1a.Add(hash, p.SpawnPadActive ? 1 : 0);
                hash = Fnv1a.Add(hash, p.SpawnPadTicksLeft);
                hash = Fnv1a.Add(hash, p.SpawnIntangible ? 1 : 0);
                hash = Fnv1a.Add(hash, p.SpawnInvulnTicksLeft);
            }
        }
        // 2026-08-12 four player / timed mode: gated suffix, active only for 3-4
        // player or TIMED matches — every legacy 2P STOCK golden hashes exactly as
        // before. Eliminated is gameplay state; KOs/SelfDestructs and the attribution
        // fields are outcome/stats-class (2P leaves them unhashed like the other stat
        // counters — deterministic, never read by gameplay).
        if (_players.Length > 2 || Config.EndRule == MatchEndRule.Timed)
        {
            foreach (SimPlayer p in _players)
            {
                hash = Fnv1a.Add(hash, p.Eliminated ? 1 : 0);
                hash = Fnv1a.Add(hash, p.KOs);
                hash = Fnv1a.Add(hash, p.SelfDestructs);
                hash = Fnv1a.Add(hash, p.LastInfluencer);
                hash = Fnv1a.Add(hash, p.GroundedInfluenceTicks);
            }
            foreach (int index in _eliminationOrder)
            {
                hash = Fnv1a.Add(hash, index);
            }
        }
        return hash;
    }

    /// <summary>
    /// 1-based placements per player (2026-08-12, four-player.md). TIMED: KOs desc,
    /// damage dealt desc, index asc. STOCK: eliminated players rank below survivors
    /// in reverse elimination order; survivors rank by stocks desc, then damage taken
    /// asc, then index asc. Always a total order — a 2P timeout still reports
    /// LoserIndex -1 (the legacy draw), but placements break the tie for research use.
    /// </summary>
    public int[] ComputePlacements()
    {
        int n = _players.Length;
        var order = new int[n];
        for (int i = 0; i < n; i++)
        {
            order[i] = i;
        }
        if (Config.EndRule == MatchEndRule.Timed)
        {
            Array.Sort(order, (x, y) =>
            {
                int c = _players[y].KOs.CompareTo(_players[x].KOs);
                if (c != 0)
                {
                    return c;
                }
                c = _players[y].DamageDealt.CompareTo(_players[x].DamageDealt);
                return c != 0 ? c : x.CompareTo(y);
            });
        }
        else
        {
            var elimRank = new int[n];
            Array.Fill(elimRank, int.MaxValue); // survivors outrank every elimination
            for (int k = 0; k < _eliminationOrder.Count; k++)
            {
                elimRank[_eliminationOrder[k]] = k;
            }
            Array.Sort(order, (x, y) =>
            {
                int c = elimRank[y].CompareTo(elimRank[x]); // later elimination = better
                if (c != 0)
                {
                    return c;
                }
                c = _players[y].Stocks.CompareTo(_players[x].Stocks);
                if (c != 0)
                {
                    return c;
                }
                c = _players[x].TotalDamageTaken.CompareTo(_players[y].TotalDamageTaken);
                return c != 0 ? c : x.CompareTo(y);
            });
        }
        var placements = new int[n];
        for (int rank = 0; rank < n; rank++)
        {
            placements[order[rank]] = rank + 1;
        }
        return placements;
    }

    public MatchResult BuildResult(Replay.InputTrace? trace = null) =>
        new(
            _players.Select(p => new PlayerStats(
                p.TotalDamageTaken, p.TotalHitsReceived, p.Stocks, p.RecoveryTicks,
                p.CompletedStockDamage.Append(p.Damage).ToArray(),
                p.MoveUses.ToArray(), p.StunTicks, p.Jumps,
                p.ShieldActivations, p.BlockedHits, p.ShieldBreaks, p.ShieldTicks,
                p.DashCount, p.DashInvulnDodges,
                p.FastFallTicks, p.CrouchTicks, p.DIInfluencedHits,
                p.ProjectilesFired, p.ProjectileHits, p.ProjectilesReflected,
                p.KOs, p.DamageDealt, p.SelfDestructs)).ToArray(),
            LoserIndex,
            TickCount,
            TickCount / (float)Config.TicksPerSecond,
            StateHash(),
            trace,
            ComputePlacements());

}
