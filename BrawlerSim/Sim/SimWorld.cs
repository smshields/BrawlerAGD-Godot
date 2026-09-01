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

    /// <summary>Thin platforms (2026-09-01, FEATURES.md §Thin Platforms): per-index
    /// drop-through flags, parallel to Platforms. A thin platform's Aabb is the top
    /// slice of its gene cell (MatchConfig.ThinPlatformThickness) — same top edge,
    /// higher underside.</summary>
    public IReadOnlyList<bool> PlatformThin { get; }
    private readonly bool _thinFeatureActive; // any thin platform on this stage

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
        IReadOnlyList<PlatformGene> genes = genome.Stage.Platforms;
        var boxes = new Aabb[genes.Count];
        var thinFlags = new bool[genes.Count];
        for (int i = 0; i < genes.Count; i++)
        {
            PlatformGene p = genes[i];
            thinFlags[i] = p.Thin;
            _thinFeatureActive |= p.Thin;
            // A thin platform collides (and renders) as a slice at the TOP of its
            // gene cell — the surface the reachability models key on is unchanged.
            boxes[i] = p.Thin
                ? Aabb.FromRect(p.X, p.Y + p.YSize - Config.ThinPlatformThickness,
                    p.XSize, Config.ThinPlatformThickness)
                : Aabb.FromRect(p.X, p.Y, p.XSize, p.YSize);
        }
        Platforms = boxes;
        PlatformThin = thinFlags;

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

    /// <summary>One simulation step. The phase ORDER below is the determinism
    /// contract — each phase is a fixed-order sweep over players (see the phase
    /// methods for the per-phase rules); reordering any two calls is a gameplay
    /// change and moves the golden hashes.</summary>
    public void Tick(ReadOnlySpan<InputFrame> inputs)
    {
        if (IsOver)
        {
            return;
        }

        StepSpawnLifecycleAndInput(inputs); // 0-1. spawn lifecycle, input + FSM
        StepKinematics();                   // 2.   physics + collision
        ReleaseClearedDropThroughs();       // 2.4. thin drop-through ignores expire
        ExpireLeftSpawnPads();              // 2.5. spawn-pad leave detection
        ResolveBodyContacts();              // 3.   body-vs-body contact...
        ApplyShieldSpacing();               //      ...then shield expulsion
        StepProjectiles();                  // 3.5. projectile step/spawn/hits
        StepInfluenceClock();               // 3.9. KO attribution clock
        ResolveHits();                      // 4.   melee hit detection
        ResolveBlastZoneExits();            // 5.   deaths, stocks, match end

        TickCount++;
        FinalizeTimeout();
    }

    /// <summary>Phase 0-1. Spawn lifecycle (2026-07-22): blackout countdown →
    /// materialize on the pad; spawn-timer countdowns for present players. Absent
    /// (blacked-out) players take no input/FSM this tick; the spawn block is a
    /// no-op when the feature is off. Then input + state machines, fixed player
    /// order.</summary>
    private void StepSpawnLifecycleAndInput(ReadOnlySpan<InputFrame> inputs)
    {
        for (int i = 0; i < _players.Length; i++)
        {
            SimPlayer player = _players[i];
            if (player.Eliminated)
            {
                continue; // out of the match for good (2026-08-12)
            }
            if (_thinFeatureActive)
            {
                // Thin platforms (2026-09-01): resolve the thin support under the
                // feet (from LAST tick's settled position) before the FSM step —
                // the crouch drop reads it. Derived state, recomputed every tick.
                int support = player.IsGrounded
                    ? SimPhysics.SupportPlatformIndex(player, Platforms, player.DropThroughPlatform)
                    : -1;
                player.ThinSupport = support >= 0 && PlatformThin[support] ? support : -1;
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
    }

    /// <summary>Phase 2. Kinematics + collision. The spawn pad is solid to its
    /// OWNER only (the other players' steps never receive it → they phase
    /// through). Absent (blacked-out or eliminated) players skip.</summary>
    private void StepKinematics()
    {
        for (int i = 0; i < _players.Length; i++)
        {
            if (_players[i].IsAbsent)
            {
                continue;
            }
            IReadOnlyList<Aabb> plats = _players[i].SpawnPadActive ? _platformsWithPad[i] : Platforms;
            // Thin flags are indexed like Platforms; the pad appended past their end
            // reads as solid (SimPhysics guards the index), so no parallel pad set.
            SimPhysics.Step(_players[i], _players, plats, Config,
                _thinFeatureActive ? PlatformThin : null);
        }
    }

    /// <summary>Phase 2.4. Thin platforms (2026-09-01): a drop-through ignore ends
    /// once the body has fully cleared the dropped platform's slice.</summary>
    private void ReleaseClearedDropThroughs()
    {
        if (!_thinFeatureActive)
        {
            return;
        }
        foreach (SimPlayer player in _players)
        {
            if (player.DropThroughPlatform >= 0
                && !player.Body.Overlaps(Platforms[player.DropThroughPlatform]))
            {
                player.DropThroughPlatform = -1;
            }
        }
    }

    /// <summary>Phase 2.5. Spawn-pad leave detection (2026-07-22): once the owner is
    /// no longer resting on the pad it despawns and intangibility ends immediately.</summary>
    private void ExpireLeftSpawnPads()
    {
        for (int i = 0; i < _players.Length; i++)
        {
            SimPlayer player = _players[i];
            if (player.SpawnPadActive && LeftPad(player, _spawnPads[i]))
            {
                player.SpawnPadActive = false;
                player.SpawnIntangible = false;
            }
        }
    }

    /// <summary>Phase 3 (first half). Body-vs-body contact — all pairs in fixed
    /// index order since 2026-08-12 (identical to the old two-player block at N=2);
    /// pairs with an absent player skip.</summary>
    private void ResolveBodyContacts()
    {
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
    }

    /// <summary>Phase 3 (second half). Shield spacing (2026-07-12: a raised shield
    /// expels the opponent — fixed player order, positional push capped per tick
    /// plus a low outward velocity floor; FEATURES.md "never enough to kill").</summary>
    private void ApplyShieldSpacing()
    {
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
    }

    /// <summary>Phase 3.9. KO attribution clock (2026-08-12, four-player.md):
    /// continuous grounding clears enemy influence — "until next landing". Runs
    /// BEFORE this tick's hits so a fresh hit restarts the clock. Stats-class
    /// state only; gameplay never reads it.</summary>
    private void StepInfluenceClock()
    {
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
    }

    /// <summary>Phase 4. Melee hit detection, fixed attacker order (all ordered
    /// pairs since 2026-08-12 — (0,1),(1,0) at N=2, exactly the old sweep).</summary>
    private void ResolveHits()
    {
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
    }

    /// <summary>Phase 5. Blast zone → stock loss / match end. Fixed player order;
    /// the first match-ending exit stops the sweep (simultaneous KOs resolve to the
    /// lower player index). Each death first resolves KO attribution (2026-08-12):
    /// live influence credits the influencer, otherwise it is a self-destruct.</summary>
    private void ResolveBlastZoneExits()
    {
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
    }

    /// <summary>Match-end-by-clock check, after TickCount has advanced. TIMED: rank
    /// by KOs / damage dealt / index — the loser is last place. STOCK with
    /// eliminations (3-4 players): the first eliminated player is the loser; a
    /// no-elimination timeout stays a draw (legacy 2P semantics).</summary>
    private void FinalizeTimeout()
    {
        if (IsOver || TickCount < Config.MaxTicks)
        {
            return;
        }
        IsOver = true;
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

    /// <summary>Phase 3.5. Projectiles (2026-07-14): step lives (closed-form
    /// reposition, then the despawn checks — TTL, decayed-to-nothing, past the
    /// blast boundary, platform contact [platforms DESTROY projectiles, designer]),
    /// consume pending spawns, then projectile-vs-player hits. Fixed list order,
    /// victims in player order — all part of the tick-order contract.</summary>
    private void StepProjectiles()
    {
        for (int i = 0; i < _projectiles.Count; i++)
        {
            SimProjectile proj = _projectiles[i];
            Vec2 previous = proj.Position; // thin-platform surface crossing (2026-09-01)
            proj.AgeTicks++;      // lifetime clock: TTL + damage decay (survives reflection)
            proj.PathAgeTicks++;  // path clock: resets when a reflect re-fires the bolt
            proj.Position = proj.Move.PositionAt(proj.Origin, proj.Facing, proj.PathAgeTicks, Config);
            proj.Angle = proj.Move.RotationRate * proj.AgeTicks * Config.Dt;
            proj.DamageScale = proj.Move.DamageScaleAt(proj.AgeTicks, Config);
            if (proj.AgeTicks >= proj.Move.TtlTicks
                || proj.DamageScale <= 0f
                || !InsideBlastZone(proj.Position)
                || HitsPlatform(previous, proj.Position))
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
            // Spawn immunity (2026-07-22): shared gate with melee. The bolt passes
            // THROUGH rather than being consumed (nothing was blocked).
            if (!overlaps || ImmuneToHits(victim))
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
            if (TryDashDodge(victim))
            {
                continue; // negated — the bolt passes through and stays live
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
                    DegradeShield(victim, shield, scaledDamage);
                    return;
                }
                ApplyBlockedHit(victim, shield, proj.Position, proj.Move.KnockbackDirection,
                    proj.Facing, proj.Move.KnockbackScalar * proj.DamageScale, scaledDamage,
                    proj.Owner);
                proj.Alive = false;
                return;
            }

            // KO attribution note (2026-08-12): a self-hit (hitsSelf gene) influences
            // and credits nobody — dying to your own bolt is a self-destruct
            // (ApplyCleanHit gates on attackerIndex ≠ victim).
            ApplyCleanHit(victim, scaledDamage, proj.Position, proj.Move.KnockbackDirection,
                proj.Facing, proj.Move.KnockbackScalar * proj.DamageScale,
                proj.Move.HitstunDuration, proj.Owner);
            _players[proj.Owner].ProjectileHits++;
            proj.Alive = false;
        }
    }

    /// <summary>Shared victim gate for melee and projectile hits (2026-07-22,
    /// 2026-08-12): an intangible/invulnerable victim takes no damage/knockback;
    /// an absent (blacked-out or eliminated) one isn't on stage.</summary>
    private static bool ImmuneToHits(SimPlayer victim) =>
        victim.InvincibleTicksLeft > 0 || victim.SpawnDamageImmune || victim.IsAbsent;

    /// <summary>Dash i-frames (2026-07-13): a hit that WOULD have landed is negated
    /// and counted — the research data sees evasion value even with fitness blind.
    /// True = dodged; the caller stops resolving this victim.</summary>
    private static bool TryDashDodge(SimPlayer victim)
    {
        if (!victim.DashInvulnerable)
        {
            return false;
        }
        victim.DashInvulnDodges++;
        return true;
    }

    /// <summary>Degrade the victim's active shield by damage × its degradation
    /// scalar; break applies immediately at/below the break radius. One home for
    /// what were three verbatim copies (melee block, projectile block, projectile
    /// shield-reflect).</summary>
    private static void DegradeShield(SimPlayer victim, SimShield shield, float damage)
    {
        victim.ShieldHealths[victim.CurrentMoveIndex] -= damage * shield.HitDegradationScalar;
        if (victim.ShieldHealths[victim.CurrentMoveIndex] <= victim.ShieldBreakRadius)
        {
            victim.BreakShield();
        }
    }

    /// <summary>Blocked-hit application, identical for melee and projectiles:
    /// zero damage/stun, knockback scaled by (1 − reduction), BlockedHits, influence
    /// (skipped when the attacker IS the victim — a self-hit shoves nobody), post-hit
    /// invincibility, then shield degradation.</summary>
    private void ApplyBlockedHit(SimPlayer victim, SimShield shield, Vec2 hitboxCenter,
        Vec2 knockbackDirection, int facing, float knockbackScalar, float damage,
        int attackerIndex)
    {
        float blockedDamageAfter = victim.Damage + damage;
        Vec2 blockedKnockback = ComputeKnockback(
            victim.Position, hitboxCenter, knockbackDirection,
            facing, knockbackScalar, blockedDamageAfter);
        victim.Velocity += blockedKnockback * (1f - shield.KnockbackReduction);
        victim.BlockedHits++;
        if (attackerIndex != victim.Index)
        {
            victim.MarkInfluence(attackerIndex); // blocked knockback still shoves (2026-08-12)
        }
        victim.InvincibleTicksLeft = Config.InvincibilityTicks;
        DegradeShield(victim, shield, damage);
    }

    /// <summary>Hitstun in ticks: hitstun gene × damage AFTER the hit × the victim's
    /// hitstun scalar, capped by MaxStunSeconds when finite.</summary>
    private int ComputeStunTicks(float hitstunDuration, float damageAfterHit, float victimScalar)
    {
        int stunTicks = Config.ToTicks(hitstunDuration * damageAfterHit * victimScalar);
        if (!float.IsPositiveInfinity(Config.MaxStunSeconds))
        {
            stunTicks = Math.Min(stunTicks, Config.ToTicks(Config.MaxStunSeconds));
        }
        return stunTicks;
    }

    /// <summary>Clean-hit application, identical for melee and projectiles: damage,
    /// knockback deflected by DI, capped stun; influence + DamageDealt credit are
    /// skipped when the attacker IS the victim (KO attribution 2026-08-12 — dying to
    /// your own bolt is a self-destruct); then post-hit invincibility.</summary>
    private void ApplyCleanHit(SimPlayer victim, float damage, Vec2 hitboxCenter,
        Vec2 knockbackDirection, int facing, float knockbackScalar,
        float hitstunDuration, int attackerIndex)
    {
        float damageAfterHit = victim.Damage + damage;
        Vec2 knockback = ComputeKnockback(
            victim.Position, hitboxCenter, knockbackDirection,
            facing, knockbackScalar, damageAfterHit);
        knockback = ApplyDirectionalInfluence(victim, knockback);
        int stunTicks = ComputeStunTicks(hitstunDuration, damageAfterHit, victim.HitstunDamageScalar);
        victim.ApplyHit(damage, knockback, stunTicks);
        if (attackerIndex != victim.Index)
        {
            victim.MarkInfluence(attackerIndex);
            _players[attackerIndex].DamageDealt += damage;
        }
        victim.InvincibleTicksLeft = Config.InvincibilityTicks;
    }

    /// <summary>Feet-on-pad slack for LeftPad (2026-07-22). NOT the physics skin
    /// (SimPhysics.Skin = 0.001, 50× smaller): a deliberately looser band, the same
    /// magnitude as MatchConfig.MaxDepenetrationPerTick, so a body still settling
    /// by capped depenetration reads as on the pad.</summary>
    private const float PadRestTolerance = 0.05f;

    /// <summary>The owner is no longer resting on its spawn pad (2026-07-22): its feet
    /// left the pad's span or lifted off the pad's top.</summary>
    private static bool LeftPad(SimPlayer player, in Aabb pad)
    {
        float feet = player.Body.Bottom;
        return player.Position.X < pad.Left || player.Position.X > pad.Right
            || feet > pad.Top + PadRestTolerance || feet < pad.Top - PadRestTolerance;
    }

    private bool InsideBlastZone(Vec2 p) => _blastZone.Contains(p);

    /// <summary>Does this tick's motion put the bolt into a platform? Solid platforms
    /// keep the legacy center-inside test (platforms DESTROY projectiles, designer).
    /// Thin platforms (2026-09-01, designer) destroy from the TOP only: a downward
    /// crossing of the surface within the span consumes the bolt; entry from below
    /// or the side passes through. The crossing test (not containment) means a fast
    /// bolt cannot tunnel the thin slice.</summary>
    private bool HitsPlatform(Vec2 previous, Vec2 current)
    {
        for (int i = 0; i < Platforms.Count; i++)
        {
            Aabb platform = Platforms[i];
            if (!PlatformThin[i])
            {
                if (platform.Contains(current))
                {
                    return true;
                }
                continue;
            }
            if (previous.Y >= platform.Top && current.Y <= platform.Top && current.Y < previous.Y)
            {
                // Interpolate the X where the segment crosses the platform's top edge.
                float t = (previous.Y - platform.Top) / (previous.Y - current.Y);
                float crossX = previous.X + (current.X - previous.X) * t;
                if (crossX >= platform.Left && crossX <= platform.Right)
                {
                    return true;
                }
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
        direction = length > DegenerateDirectionEpsilon
            ? direction * (1f / length)
            : new Vec2(shielder.Facing, 0f);

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
        // Immunity gate (2026-07-22, 2026-08-12): shared with projectiles, plus an
        // absent attacker has no live hitbox.
        if (!attacker.HitboxActive || ImmuneToHits(victim) || attacker.IsAbsent)
        {
            return;
        }
        Aabb hitbox = attacker.Hitbox;
        if (!hitbox.Overlaps(victim.Body))
        {
            return;
        }
        if (TryDashDodge(victim))
        {
            return;
        }

        SimShield? shield = victim.ActiveShield;
        if (shield is not null && victim.ShieldRadius > 0f
            && OverlapFullyInsideShield(hitbox, victim.Body,
                victim.Position + victim.ShieldOffset, victim.ShieldRadius))
        {
            ApplyBlockedHit(victim, shield, hitbox.Center, attacker.Move.KnockbackDirection,
                attacker.Facing, attacker.Move.KnockbackScalar, attacker.Move.DamageGiven,
                attacker.Index);
            return;
        }

        // Directional influence (2026-07-13, FEATURES.md §DI): inside ApplyCleanHit
        // the victim's held direction deflects the knockback slightly (≤10% gene)
        // and, when held near-opposite (within 45°), trims its magnitude (≤20% gene).
        // SKIPPED while shielding — including pokes through partial cover: a shielder
        // is committed to the shield, not influencing (designer clarification).
        ApplyCleanHit(victim, attacker.Move.DamageGiven, hitbox.Center,
            attacker.Move.KnockbackDirection, attacker.Facing, attacker.Move.KnockbackScalar,
            attacker.Move.HitstunDuration, attacker.Index);
    }

    /// <summary>Below this length a push direction is treated as degenerate
    /// (concentric bodies) and falls back to the shielder's facing.</summary>
    private const float DegenerateDirectionEpsilon = 0.0001f;

    /// <summary>cos 135° — the "held near-opposite" threshold for DI's
    /// knockback-magnitude reduction (within 45° of straight-against).</summary>
    private const float OppositeHoldCosine = -0.70710678f;

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
        if (dot < OppositeHoldCosine)
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
    /// <summary>The Unity formula's damage-to-knockback conversion factor.</summary>
    public const float DamagePerKnockbackUnit = 0.1f;

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
            * (damageAfterHit * DamagePerKnockbackUnit);
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
        // 2026-09-01 thin platforms: gated suffix, appended ONLY when the stage has a
        // thin platform — all-solid matches (every pre-v12 game, all the golden pins)
        // hash exactly as before. DropThroughPlatform/DropDelayTicksLeft are the
        // feature's only new mutable gameplay state (ThinSupport is derived and
        // recomputed each tick; DropThroughs is a stat counter).
        if (_thinFeatureActive)
        {
            foreach (SimPlayer p in _players)
            {
                hash = Fnv1a.Add(hash, p.DropThroughPlatform);
                hash = Fnv1a.Add(hash, p.DropDelayTicksLeft);
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
            // Fully named so a same-typed pair can never swap silently — a wrong-order
            // stat here would only ever surface as a research-data anomaly.
            _players.Select(p => new PlayerStats(
                TotalDamageTaken: p.TotalDamageTaken,
                TotalHitsReceived: p.TotalHitsReceived,
                RemainingStocks: p.Stocks,
                RecoveryTicks: p.RecoveryTicks,
                DamagePerStock: p.CompletedStockDamage.Append(p.Damage).ToArray(),
                MoveUses: p.MoveUses.ToArray(),
                StunTicks: p.StunTicks,
                Jumps: p.Jumps,
                ShieldActivations: p.ShieldActivations,
                BlockedHits: p.BlockedHits,
                ShieldBreaks: p.ShieldBreaks,
                ShieldTicks: p.ShieldTicks,
                DashCount: p.DashCount,
                DashInvulnDodges: p.DashInvulnDodges,
                FastFallTicks: p.FastFallTicks,
                CrouchTicks: p.CrouchTicks,
                DIInfluencedHits: p.DIInfluencedHits,
                ProjectilesFired: p.ProjectilesFired,
                ProjectileHits: p.ProjectileHits,
                ProjectilesReflected: p.ProjectilesReflected,
                KOs: p.KOs,
                DamageDealt: p.DamageDealt,
                SelfDestructs: p.SelfDestructs,
                DropThroughs: p.DropThroughs)).ToArray(),
            LoserIndex,
            TickCount,
            TickCount / (float)Config.TicksPerSecond,
            StateHash(),
            trace,
            ComputePlacements());

}
