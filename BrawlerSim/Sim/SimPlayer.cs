using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;

namespace BrawlerSim.Sim;

public enum PlayerState
{
    Idle,
    Air,
    AirJumpsExhausted,
    WarmUp,
    Attack,
    CoolDown,
    Stun,
    Shield, // 2026-07-12, FEATURES.md §Shield — tint: cyan
    Dash,   // 2026-07-13, FEATURES.md §Dash — tint: orange
}

/// <summary>Dash sub-phase: warm-up (direction still steerable, optional i-frames),
/// then travel (locked straight line, gravity suspended, optional i-frames).</summary>
public enum DashStage
{
    None,
    WarmUp,
    Travel,
}

/// <summary>Sub-phase of the Shield state: the circle grows over the shield's wind-up,
/// holds while the button is held, and shrinks over its cool-down.</summary>
public enum ShieldStage
{
    None,
    Grow,
    Hold,
    Shrink,
}

/// <summary>
/// One character's runtime state. The FSM mirrors the Unity Player states (minus the
/// dropped shield/landing states) with two deliberate cleanups, both documented in the
/// plan: (1) being hit CANCELS an in-flight move — Unity's coroutine kept mutating state
/// after a stun, a race we do not port; (2) leaving the ground mid-move no longer yanks
/// the state to Air — move phases run to completion, then resolve by grounded/jumps.
/// </summary>
public sealed class SimPlayer
{
    public int Index { get; }
    public string Name { get; }

    /// <summary>Attack slots resolved to tick-domain values; NULL at shield slots
    /// (check MoveTypeAt / Shields).</summary>
    public IReadOnlyList<SimMove?> Moves => _moves;

    /// <summary>Shield slots resolved to tick-domain values; NULL at attack slots.</summary>
    public IReadOnlyList<SimShield?> Shields => _shields;

    /// <summary>Dash slots resolved to tick-domain values; NULL elsewhere.</summary>
    public IReadOnlyList<SimDash?> Dashes => _dashes;

    public MoveType MoveTypeAt(int index) =>
        _shields[index] is not null ? MoveType.Shield
        : _dashes[index] is not null ? MoveType.Dash
        : MoveType.Attack;

    /// <summary>Genome button→move mapping: ButtonMoves[b] = move triggered by button b.</summary>
    public IReadOnlyList<int> ButtonMoves => _buttonMoves;

    /// <summary>
    /// The move whose phases/hitbox are in effect — the one most recently started.
    /// Meaningful during WarmUp/Attack/CoolDown; stale (but deterministic) otherwise.
    /// </summary>
    public SimMove Move => _moves[CurrentMoveIndex]!;

    private readonly SimMove?[] _moves;
    private readonly SimShield?[] _shields;
    private readonly SimDash?[] _dashes;
    private readonly int[] _buttonMoves;

    // Character constants, resolved once from the genome.
    public readonly float GroundAcceleration;
    public readonly float AirAcceleration;
    public readonly float MaxGroundSpeed;
    public readonly float MaxAirSpeed;
    public readonly float GroundJumpForce;
    public readonly float AirJumpForce;
    public readonly float Mass;
    public readonly float Drag;
    public readonly float GravityScale;
    public readonly float HitstunDamageScalar;
    public readonly Vec2 BodyHalf;
    public readonly Vec2 SpawnPosition;

    // Mutable per-tick state.
    public Vec2 Position;
    public Vec2 Velocity;
    public int Facing = 1;               // +1 right, -1 left
    public PlayerState State = PlayerState.Idle;
    public int PhaseTicksLeft;           // remaining ticks of WarmUp/Attack/CoolDown/Stun
    public bool IsGrounded;
    public bool JumpsExhausted;
    public float Damage;
    public int Stocks;
    public int InvincibleTicksLeft;
    public int CurrentMoveIndex;         // index into Moves; set by StartMove/StartShield (hashed state)

    // Shield state (2026-07-12, all hashed): per-slot health persists across
    // activations (regen resumes from current, never resets to fresh).
    public ShieldStage ShieldPhase;
    public Vec2 ShieldOffset;
    public int ShieldButton = -1;        // button that raised the shield (release check)
    public readonly float[] ShieldHealths;

    /// <summary>True while the current Stun came from a shield break — the agent's
    /// punish window (FEATURES.md: "attempt to use a powerful move"). Hashed.</summary>
    public bool StunFromShieldBreak;

    // Dash state (2026-07-13, hashed except stats): one dash per airtime — the third
    // air action alongside the two jumps (dash-jump-jump in any order, then done).
    public DashStage DashPhase;
    public Vec2 DashDirection;
    public bool AirDashUsed;

    private readonly MatchConfig _config;

    // Stats accumulated for fitness/research.
    public float TotalDamageTaken;
    public int TotalHitsReceived;
    public int RecoveryTicks;            // Unity's totalRecoveryStateTransition (see agent)

    /// <summary>Damage taken in each COMPLETED life (closed out by Respawn). The live
    /// stock's running damage is `Damage`; BuildResult appends it non-mutatingly.</summary>
    public readonly List<float> CompletedStockDamage = new();

    /// <summary>How many times each move was started (2026-07-10, second-move stats).</summary>
    public readonly int[] MoveUses;

    /// <summary>Total ticks spent in Stun (2026-07-10, stun-cap research stat).</summary>
    public int StunTicks;

    /// <summary>Ground + air jumps executed (2026-07-10, jump-value research stat).</summary>
    public int Jumps;

    // Shield stats (2026-07-12, research-only).
    public int ShieldActivations;
    public int BlockedHits;
    public int ShieldBreaks;
    public int ShieldTicks;

    // Dash stats (2026-07-13, research-only).
    public int DashCount;
    public int DashInvulnDodges;

    public SimPlayer(int index, CharacterGenome genome, Vec2 spawn, MatchConfig config)
    {
        Index = index;
        Name = genome.Name;
        _moves = genome.Moves.Select(m => m.Type == MoveType.Attack ? new SimMove(m, config) : null).ToArray();
        _shields = genome.Moves.Select(m => m.Type == MoveType.Shield ? new SimShield(m, config) : null).ToArray();
        _dashes = genome.Moves.Select(m => m.Type == MoveType.Dash ? new SimDash(m, config) : null).ToArray();
        _buttonMoves = genome.ButtonMoves.ToArray();
        MoveUses = new int[_moves.Length];
        ShieldHealths = _shields.Select(sh => sh?.InitialRadius ?? 0f).ToArray();
        _config = config;

        ParamSet p = genome.Params;
        MaxGroundSpeed = p.Get(CharacterParams.MaxGroundSpeed);
        MaxAirSpeed = p.Get(CharacterParams.MaxAirSpeed);
        // Unity derived accelerations: factor × the matching max speed.
        GroundAcceleration = p.Get(CharacterParams.GroundAccelerationFactor) * MaxGroundSpeed;
        AirAcceleration = p.Get(CharacterParams.AirAccelerationFactor) * MaxAirSpeed;
        GroundJumpForce = p.Get(CharacterParams.GroundJumpForce);
        AirJumpForce = p.Get(CharacterParams.AirJumpForce);
        Mass = p.Get(CharacterParams.Mass);
        Drag = p.Get(CharacterParams.Drag);
        GravityScale = p.Get(CharacterParams.GravityScalar);
        HitstunDamageScalar = p.Get(CharacterParams.HitstunDamageScalar);
        WidthScalar = p.Get(CharacterParams.WidthScalar);
        HeightScalar = p.Get(CharacterParams.HeightScalar);
        BodyHalf = new Vec2(
            config.PlayerBaseWidth * WidthScalar / 2f,
            config.PlayerBaseHeight * HeightScalar / 2f);

        SpawnPosition = spawn;
        Position = spawn;
        Stocks = genome.Stocks;
    }

    public readonly float WidthScalar;
    public readonly float HeightScalar;

    public Aabb Body => new(Position, BodyHalf);

    /// <summary>The shield being held right now, or null.</summary>
    public SimShield? ActiveShield => State == PlayerState.Shield ? _shields[CurrentMoveIndex] : null;

    /// <summary>The dash in progress, or null.</summary>
    public SimDash? ActiveDash => State == PlayerState.Dash ? _dashes[CurrentMoveIndex] : null;

    /// <summary>Gravity is suspended and velocity locked while travelling.</summary>
    public bool IsDashTraveling => State == PlayerState.Dash && DashPhase == DashStage.Travel;

    /// <summary>Per-stage dash i-frames (FEATURES.md): distinct from post-hit
    /// invincibility; negated hits are counted as DashInvulnDodges.</summary>
    public bool DashInvulnerable
    {
        get
        {
            SimDash? dash = ActiveDash;
            return dash is not null && (DashPhase == DashStage.WarmUp
                ? dash.WarmUpInvulnerable
                : dash.DurationInvulnerable);
        }
    }

    /// <summary>Can a dash start right now? Grounded always; airborne once per
    /// airtime (the spec's third air action, usable even with jumps spent).</summary>
    public bool CanDash => IsGrounded || !AirDashUsed;

    /// <summary>Effective (rendered AND blocking) radius: health scaled by the
    /// grow/shrink animation fraction. 0 when not shielding.</summary>
    public float ShieldRadius
    {
        get
        {
            SimShield? shield = ActiveShield;
            if (shield is null)
            {
                return 0f;
            }
            float fraction = ShieldPhase switch
            {
                ShieldStage.Grow => 1f - PhaseTicksLeft / (float)shield.WindUpTicks,
                ShieldStage.Shrink => PhaseTicksLeft / (float)shield.CoolDownTicks,
                _ => 1f,
            };
            return ShieldHealths[CurrentMoveIndex] * fraction;
        }
    }

    /// <summary>Break threshold: 1/5 of the character's (scaled) height.</summary>
    public float ShieldBreakRadius => _config.PlayerBaseHeight * HeightScalar * _config.ShieldBreakRadiusFraction;

    public bool HitboxActive => State == PlayerState.Attack;

    /// <summary>World-space hitbox: offset mirrors with facing; size inherits player scale.</summary>
    public Aabb Hitbox => new(
        Position + new Vec2(Move.Offset.X * Facing, Move.Offset.Y),
        new Vec2(Move.BaseHalf.X * WidthScalar, Move.BaseHalf.Y * HeightScalar));

    /// <summary>Advances move/stun phase timers and applies this tick's input intents.</summary>
    public void StepStateMachine(in InputFrame input)
    {
        // Shield regeneration: every slot not currently held regenerates toward full —
        // resuming from CURRENT health (spec: never resets to fresh).
        for (int slot = 0; slot < _shields.Length; slot++)
        {
            if (_shields[slot] is SimShield sh
                && !(State == PlayerState.Shield && CurrentMoveIndex == slot))
            {
                ShieldHealths[slot] = MathF.Min(sh.InitialRadius, ShieldHealths[slot] + sh.RegenPerTick);
            }
        }

        switch (State)
        {
            case PlayerState.Shield:
                StepShield(input);
                return;

            case PlayerState.Dash:
                StepDash(input);
                return;

            case PlayerState.Stun:
                StunTicks++;
                if (--PhaseTicksLeft <= 0) ResolveNeutralState();
                return; // no control in stun

            case PlayerState.WarmUp:
                ApplyHorizontal(input.Horizontal); // Unity allowed movement during warm-up
                if (--PhaseTicksLeft <= 0)
                {
                    State = PlayerState.Attack;
                    PhaseTicksLeft = Move.ExecuteTicks;
                }
                return;

            case PlayerState.Attack:
                if (--PhaseTicksLeft <= 0)
                {
                    State = PlayerState.CoolDown;
                    PhaseTicksLeft = Move.CoolDownTicks;
                }
                return;

            case PlayerState.CoolDown:
                if (--PhaseTicksLeft <= 0) ResolveNeutralState();
                return;

            case PlayerState.Idle:
                ApplyHorizontal(input.Horizontal);
                if (input.Jump && IsGrounded)
                {
                    Jumps++;
                    Velocity = Velocity with { Y = GroundJumpForce };
                    State = PlayerState.Air;
                }
                else if (input.Actions != 0)
                {
                    StartAction(input.FirstAction);
                }
                return;

            case PlayerState.Air:
                ApplyHorizontal(input.Horizontal);
                if (input.Jump && !JumpsExhausted)
                {
                    Jumps++;
                    Velocity = Velocity with { Y = AirJumpForce };
                    JumpsExhausted = true;
                    State = PlayerState.AirJumpsExhausted;
                }
                else if (input.Actions != 0)
                {
                    StartAction(input.FirstAction, airborne: true);
                }
                return;

            case PlayerState.AirJumpsExhausted:
                // Unity parity: movement only — no attacks once air jumps are spent.
                // EXCEPT the dash (2026-07-13): it is the third air action and remains
                // available here until used (jump-jump-dash).
                ApplyHorizontal(input.Horizontal);
                if (input.Actions != 0)
                {
                    int slot = _buttonMoves[input.FirstAction];
                    if (_dashes[slot] is not null && CanDash)
                    {
                        StartDash(slot);
                    }
                }
                return;
        }
    }

    /// <summary>Called by physics after ground contact is resolved for this tick.</summary>
    public void OnGroundedChanged(bool grounded)
    {
        IsGrounded = grounded;
        if (grounded)
        {
            JumpsExhausted = false;
            if (State != PlayerState.Dash)
            {
                // A grounded dash warm-up must not refund the budget it just spent —
                // the ground-started upward dash IS the air dash (dash-jump-jump).
                AirDashUsed = false;
            }
            if (State is PlayerState.Air or PlayerState.AirJumpsExhausted)
            {
                State = PlayerState.Idle;
            }
        }
        else if (State == PlayerState.Idle)
        {
            State = JumpsExhausted ? PlayerState.AirJumpsExhausted : PlayerState.Air;
        }
    }

    public void ApplyHit(float damage, Vec2 knockback, int stunTicks)
    {
        Damage += damage;
        TotalDamageTaken += damage;
        TotalHitsReceived++;
        Velocity += knockback;
        State = PlayerState.Stun; // cancels any in-flight move (deliberate cleanup)
        StunFromShieldBreak = false;
        ShieldPhase = ShieldStage.None;
        DashPhase = DashStage.None;
        PhaseTicksLeft = stunTicks;
    }

    public void Respawn()
    {
        CompletedStockDamage.Add(Damage);
        Stocks--;
        Damage = 0f;
        Velocity = Vec2.Zero;
        Position = SpawnPosition;
        State = PlayerState.Idle;
        PhaseTicksLeft = 0;
        JumpsExhausted = false;
    }

    /// <summary>Routes a pressed button to its slot's action: attacks start from Idle
    /// or Air; shields ONLY from Idle (spec: no shielding in the air); dashes from
    /// Idle/Air (and AirJumpsExhausted, handled in its own case) once per airtime.</summary>
    private void StartAction(int button, bool airborne = false)
    {
        int slot = _buttonMoves[button];
        if (_shields[slot] is SimShield)
        {
            // Re-raising a shield already at/below its break threshold would
            // instant-break; the press is ignored instead (documented).
            if (!airborne && ShieldHealths[slot] > ShieldBreakRadius)
            {
                StartShield(slot, button);
            }
            return;
        }
        if (_dashes[slot] is not null)
        {
            if (CanDash)
            {
                StartDash(slot);
            }
            return;
        }
        StartMove(slot);
    }

    private void StartDash(int slot)
    {
        CurrentMoveIndex = slot;
        State = PlayerState.Dash;
        DashPhase = DashStage.WarmUp;
        PhaseTicksLeft = _dashes[slot]!.WindUpTicks;
        AirDashUsed = true; // grounding resets it; airborne chains cap at one dash
        DashCount++;
    }

    private void StepDash(in InputFrame input)
    {
        SimDash dash = _dashes[CurrentMoveIndex]!;
        switch (DashPhase)
        {
            case DashStage.WarmUp:
                if (--PhaseTicksLeft <= 0)
                {
                    // Direction captured at travel start from the HELD axes (8-way);
                    // neutral falls back to horizontal facing (spec).
                    var direction = new Vec2(MathF.Sign(input.Horizontal), MathF.Sign(input.Vertical));
                    float length = direction.Length();
                    DashDirection = length > 0f ? direction * (1f / length) : new Vec2(Facing, 0f);
                    if (DashDirection.X != 0f)
                    {
                        Facing = DashDirection.X > 0f ? 1 : -1;
                    }
                    DashPhase = DashStage.Travel;
                    PhaseTicksLeft = dash.DurationTicks;
                    Velocity = DashDirection * dash.Speed;
                }
                return;
            case DashStage.Travel:
                // Locked straight line: velocity re-asserted every tick (drag and
                // contact cannot bleed it; gravity is skipped in SimPhysics).
                Velocity = DashDirection * dash.Speed;
                if (--PhaseTicksLeft <= 0)
                {
                    DashPhase = DashStage.None;
                    // Carry momentum, but clamped to ordinary movement speed — the
                    // no-KO guarantee must survive the travel→normal-physics handoff
                    // (residual 18 u/s slamming into a body would launch uncapped).
                    float carry = MathF.Min(dash.Speed, IsGrounded ? MaxGroundSpeed : MaxAirSpeed);
                    Velocity = DashDirection * carry;
                    ResolveNeutralState(); // gravity resumes
                }
                return;
        }
    }

    private void StartShield(int slot, int button)
    {
        CurrentMoveIndex = slot;
        ShieldButton = button;
        ShieldPhase = ShieldStage.Grow;
        ShieldOffset = Vec2.Zero;
        State = PlayerState.Shield;
        PhaseTicksLeft = _shields[slot]!.WindUpTicks;
        ShieldActivations++;
    }

    private void StepShield(in InputFrame input)
    {
        ShieldTicks++;
        SimShield shield = _shields[CurrentMoveIndex]!;

        // Directional shield control (first live use of InputFrame.Vertical): slide the
        // offset; the shield's EDGE may never leave the character's center, i.e.
        // |offset| ≤ current radius.
        if (ShieldPhase != ShieldStage.Shrink)
        {
            var direction = new Vec2(MathF.Sign(input.Horizontal), MathF.Sign(input.Vertical));
            ShieldOffset += direction * (_config.ShieldOffsetSpeed * _config.Dt);
            float radius = ShieldRadius;
            float length = ShieldOffset.Length();
            if (length > radius && length > 0f)
            {
                ShieldOffset *= radius / length;
            }
        }

        switch (ShieldPhase)
        {
            case ShieldStage.Grow:
                if (--PhaseTicksLeft <= 0)
                {
                    ShieldPhase = ShieldStage.Hold;
                    PhaseTicksLeft = 0;
                }
                break;
            case ShieldStage.Hold:
                ShieldHealths[CurrentMoveIndex] -= shield.HoldDegradationPerTick;
                if (ShieldHealths[CurrentMoveIndex] <= ShieldBreakRadius)
                {
                    BreakShield();
                }
                else if (!input.ActionPressed(ShieldButton))
                {
                    ShieldPhase = ShieldStage.Shrink;
                    PhaseTicksLeft = shield.CoolDownTicks;
                }
                break;
            case ShieldStage.Shrink:
                if (--PhaseTicksLeft <= 0)
                {
                    ShieldPhase = ShieldStage.None;
                    ResolveNeutralState();
                }
                break;
        }

        // Re-clamp AFTER degradation: the edge-never-past-center invariant must hold
        // against this tick's shrinkage too, not just last tick's radius.
        if (State == PlayerState.Shield)
        {
            float clampRadius = ShieldRadius;
            float offsetLength = ShieldOffset.Length();
            if (offsetLength > clampRadius && offsetLength > 0f)
            {
                ShieldOffset *= clampRadius / offsetLength;
            }
        }
    }

    /// <summary>Break: health zeroes (regen restarts from nothing) and the shielder
    /// eats the shield's break stun — deliberately EXEMPT from MaxStunSeconds.</summary>
    public void BreakShield()
    {
        SimShield shield = _shields[CurrentMoveIndex]!;
        ShieldHealths[CurrentMoveIndex] = 0f;
        ShieldPhase = ShieldStage.None;
        ShieldBreaks++;
        State = PlayerState.Stun;
        StunFromShieldBreak = true;
        PhaseTicksLeft = shield.BreakStunTicks;
    }

    private void StartMove(int moveIndex)
    {
        CurrentMoveIndex = moveIndex;
        MoveUses[moveIndex]++;
        State = PlayerState.WarmUp;
        PhaseTicksLeft = Move.WarmUpTicks;
    }

    /// <summary>
    /// Lowest-index button mapped to <paramref name="moveIndex"/>, or -1 when none. The
    /// decision-tree agent uses this to express "do move X" in button vocabulary.
    /// </summary>
    public int ButtonForMove(int moveIndex)
    {
        for (int b = 0; b < _buttonMoves.Length; b++)
        {
            if (_buttonMoves[b] == moveIndex)
            {
                return b;
            }
        }
        return -1;
    }

    private void ResolveNeutralState()
    {
        StunFromShieldBreak = false;
        PhaseTicksLeft = 0;
        State = IsGrounded
            ? PlayerState.Idle
            : (JumpsExhausted ? PlayerState.AirJumpsExhausted : PlayerState.Air);
    }

    /// <summary>
    /// Self-applied horizontal movement. Fixes Unity defect #4: acceleration is signed and
    /// the cap only limits SELF-applied speed — it neither snap-reverses direction nor
    /// bleeds off external (knockback) velocity above the cap.
    /// </summary>
    private void ApplyHorizontal(float horizontal)
    {
        if (horizontal > 0f)
        {
            Facing = 1;
            float accel = IsGrounded ? GroundAcceleration : AirAcceleration;
            float max = IsGrounded ? MaxGroundSpeed : MaxAirSpeed;
            if (Velocity.X < max)
            {
                Velocity = Velocity with { X = MathF.Min(Velocity.X + accel, max) };
            }
        }
        else if (horizontal < 0f)
        {
            Facing = -1;
            float accel = IsGrounded ? GroundAcceleration : AirAcceleration;
            float max = IsGrounded ? MaxGroundSpeed : MaxAirSpeed;
            if (Velocity.X > -max)
            {
                Velocity = Velocity with { X = MathF.Max(Velocity.X - accel, -max) };
            }
        }
    }
}
