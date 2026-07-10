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

    /// <summary>All of this character's moves, resolved to tick-domain values.</summary>
    public IReadOnlyList<SimMove> Moves => _moves;

    /// <summary>Genome button→move mapping: ButtonMoves[b] = move triggered by button b.</summary>
    public IReadOnlyList<int> ButtonMoves => _buttonMoves;

    /// <summary>
    /// The move whose phases/hitbox are in effect — the one most recently started.
    /// Meaningful during WarmUp/Attack/CoolDown; stale (but deterministic) otherwise.
    /// </summary>
    public SimMove Move => _moves[CurrentMoveIndex];

    private readonly SimMove[] _moves;
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
    public int CurrentMoveIndex;         // index into Moves; set by StartMove (hashed state)

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

    public SimPlayer(int index, CharacterGenome genome, Vec2 spawn, MatchConfig config)
    {
        Index = index;
        Name = genome.Name;
        _moves = genome.Moves.Select(m => new SimMove(m, config)).ToArray();
        _buttonMoves = genome.ButtonMoves.ToArray();
        MoveUses = new int[_moves.Length];

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

    public bool HitboxActive => State == PlayerState.Attack;

    /// <summary>World-space hitbox: offset mirrors with facing; size inherits player scale.</summary>
    public Aabb Hitbox => new(
        Position + new Vec2(Move.Offset.X * Facing, Move.Offset.Y),
        new Vec2(Move.BaseHalf.X * WidthScalar, Move.BaseHalf.Y * HeightScalar));

    /// <summary>Advances move/stun phase timers and applies this tick's input intents.</summary>
    public void StepStateMachine(in InputFrame input)
    {
        switch (State)
        {
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
                    StartMove(_buttonMoves[input.FirstAction]);
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
                    StartMove(_buttonMoves[input.FirstAction]);
                }
                return;

            case PlayerState.AirJumpsExhausted:
                // Unity parity: movement only — no attacks once air jumps are spent.
                ApplyHorizontal(input.Horizontal);
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
