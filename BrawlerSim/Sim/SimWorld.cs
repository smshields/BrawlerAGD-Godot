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

        // 3. Body-vs-body contact.
        SimPhysics.ResolvePlayerContact(_players[0], _players[1], Config);

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

    /// <summary>
    /// Unity hit semantics (single clean path — the Enter/Stay/Exit duplication is not
    /// ported): damage first, then knockback = (victim − hitbox center + unit knockback
    /// direction) · scalar · (victim damage · 0.1), then hitstun scaled by victim damage.
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

        float damageAfterHit = victim.Damage + attacker.Move.DamageGiven;
        Vec2 knockback = ComputeKnockback(
            victim.Position, hitbox.Center, attacker.Move.KnockbackDirection,
            attacker.Facing, attacker.Move.KnockbackScalar, damageAfterHit);

        int stunTicks = Config.ToTicks(
            attacker.Move.HitstunDuration * damageAfterHit * victim.HitstunDamageScalar);

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
        }
        return hash;
    }

    public MatchResult BuildResult(Replay.InputTrace? trace = null) =>
        new(
            _players.Select(p => new PlayerStats(
                p.TotalDamageTaken, p.TotalHitsReceived, p.Stocks, p.RecoveryTicks)).ToArray(),
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
