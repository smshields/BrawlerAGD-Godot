using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Agents;

/// <summary>
/// Utility-based playtester — the fitness instrument from 2026-07-09 on
/// (docs/features/utility-agent.md). Composes one InputFrame per decision from three
/// independently-selected channels (horizontal / jump / attack); every behavior
/// contributes non-negative scores to any channel, scores are normalized to sum 1, and
/// selection is an argmax/proportional mixture controlled by AgentConfig.Randomness.
///
/// Deliberate differences from the archived decision-tree instrument:
/// - relative-Y target reasoning (the DT compared the target's ABSOLUTE y to 0);
/// - jump/attack are single-tick presses on decision ticks (the DT held levels,
///   chaining ground→air jumps instantly);
/// - no origin-homing when no platform is sensed — an unreachable/unsensed platform
///   means the character is doomed and turns aggressive (designer requirement 1).
///
/// Determinism: seeded Pcg32; RNG is drawn ONLY on decision ticks (one mixture draw per
/// channel, plus one sample draw when the proportional branch is taken); arithmetic is
/// +,−,×,÷,sqrt — no transcendentals (cross-platform hash safety).
/// </summary>
public sealed partial class UtilityAgent : IInputSource
{
    // v1 behavior weights (docs/features/utility-agent.md "Initial utility functions").
    // Constants, not config: the comparison study is the calibration loop for these.
    private const float BaselineNeutral = 0.3f;   // idling must stay possible
    private const float BaselineNoJump = 1.0f;    // jumping must be purposeful
    private const float BaselineNoAttack = 0.5f;  // out-of-range ticks must not spam
    private const float RecoverMove = 3.0f;
    private const float RecoverJump = 2.0f;
    private const float DoomedChase = 2.0f;
    private const float DoomedAttack = 3.0f;
    private const float ApproachMax = 1.5f;       // scaled by min(distance/8, 1)
    private const float ApproachDistanceScale = 8f;
    private const float ApproachJump = 1.0f;
    private const float OpponentAboveThreshold = 1.5f;
    private const float AttackInRange = 4.0f;
    private const float AttackDamagePreference = 0.05f; // dmg ≤ ~15 → bonus ≤ 0.75 < base 4

    // Projectiles (2026-07-14, FEATURES.md §Projectiles agent spec). EQUAL WEIGHTING
    // since 2026-09-04 (designer-directed, CHANGE_LOG #35): the original 2.6/0.04
    // soft melee preference — on top of the melee-first movement stack — helped
    // drive projectile slots extinct under random composition (probe: bolts released
    // at median dx 1.3-2.2 after the target closed during warm-up, 87-91% cross-match
    // losses despite higher damage). The close-range gate stays the hard rule.
    private const float ProjectileInRange = 4.0f;       // == AttackInRange
    private const float ProjectileDamagePreference = 0.05f; // == AttackDamagePreference
    private const float MinProjectileRange = 2.5f;      // the close-range gate
    private const float ProjectileCorridorSlack = 0.6f; // vertical looseness of the aim test
    // Zoning stance (2026-09-04, designer-directed — CHANGE_LOG #35): a projectile
    // carrier plays RANGE. Retreat must beat Approach (1.5) but stay below Flank
    // (2.5) so platform routing still wins; the hold keeps the agent planted in the
    // firing pocket instead of drifting in. All O(moves) arithmetic per decision —
    // no pathfinding, no allocation (designer: must stay computationally cheap).
    private const float ZonerRetreat = 2.2f;      // too close for the gate → back out
    private const float ZonerRetreatRange = 4.0f; // retreat while inside gate + margin
    private const float ZonerMaxRange = 10f;      // the pocket's far edge; beyond, approach
    private const float ZonerHold = 1.6f;         // in the pocket: plant and fire
    private const float ZonerEdgeProbe = 1.0f;    // never back off a ledge
    private const int ProjectileLookaheadTicks = 30;    // dodge prediction horizon (0.5 s)
    private const int ProjectileLookaheadStep = 3;
    private const float EvadeMove = 2.0f;         // scaled up to 2× as damage climbs
    private const float HighDamageThreshold = 80f;
    private const float EdgeProbeDistance = 1.0f; // how far ahead evade checks for a pit
    private const float ThreatDodgeMove = 2.0f;   // opponent's hitbox reaches me → leave
    private const float ThreatDodgeJump = 1.5f;
    private const float SpacingMove = 1.0f;       // too close but can't hit → make room
    private const float SpacingDistance = 1.5f;
    // Flanking (2026-07-10 designer-reported stall): a platform between two vertically
    // separated characters blocks the direct route — route around its edge.
    private const float FlankMove = 2.5f;         // must beat Approach's max 1.5
    private const float FlankUnsafeScale = 0.5f;  // no ground beyond either edge → tepid
    private const float VerticalBlockThreshold = 1.5f;
    private const float FlankEdgeProbe = 0.75f;   // how far beyond the edge safety looks
    // Telegraph dodge (2026-07-10 designer request: agents should USE jumps to escape):
    // the opponent's WarmUp is a readable wind-up — hop out of the incoming arc.
    private const float TelegraphDodgeJump = 2.0f;
    private const float TelegraphDodgeMove = 1.0f;
    private const float TelegraphDodgeMargin = 1.0f; // how far past their reach we react
    // Traversal (2026-07-10 designer design: platform-graph next-hop table): when the
    // opponent stands on a different platform, head for the launch edge of the next
    // platform on the route and hop.
    private const float TraverseMove = 2.0f;      // above Approach (1.5), below Flank (2.5)
    private const float TraverseJump = 2.5f;
    private const float TraverseLaunchSlack = 0.6f;
    // Vulnerable caution (2026-07-10 exhausted-disengage, generalized 2026-07-13 per
    // designer playtest): CoolDown and AirJumpsExhausted cannot attack, so chasing is
    // pure exposure — offensive behaviors gate off and a retreat drift takes over.
    // The dash stays available for RECOVERY and defensive escapes, never the chase.
    private const float ExhaustedRetreat = 1.5f;
    private const float ExhaustedCautionRange = 4f;
    // Shield (2026-07-12, FEATURES.md agent spec): raise on a telegraphed swing,
    // scaled by shield health (less prone near breaking); when both the dodge-jump
    // and the shield fire, a health-weighted coin decides (the designer's
    // weighted-random trade-off); hold while threatened, release early near break;
    // punish an opponent's break stun with the most powerful move.
    private const float ShieldRaise = 3.0f;
    private const float ShieldHoldRange = 3.0f;
    private const float ShieldReleaseHealthFraction = 0.25f;
    private const float ShieldHoldUtility = 2.0f;   // vs the 1.0 release baseline
    private const float ShieldReleaseBaseline = 1.0f;
    private const float ShieldUnthreatenedHold = 0.6f; // hesitation: sampling can hold on
    // Defense channel (2026-07-13 dash feature): one weighted-random pick among the
    // defensive options on a telegraphed swing — do nothing / hop / shield / dash out.
    private const float DefenseNone = 1.0f;
    private const float DefenseJump = 2.0f;
    private const float DefenseShield = 3.0f;       // × shield health margin
    private const float DefenseDash = 2.0f;
    // 2026-07-13 fast fall / crouch / DI:
    private const float DefenseFastFall = 2.0f;
    private const float DefenseFastFallVulnerable = 2.8f; // favored in warm-up/cool-down/exhausted (spec)
    private const float DefenseCrouch = 2.5f;       // only when the crouched hurtbox clears the arc
    // Thin platforms (2026-09-01, FEATURES.md: drop-through as an ESCAPE route, only
    // with a safe landing below — CHANGE_LOG #34): a defense-channel option (hold
    // down, the crouch drop does the rest), plus a grounded drop-pursuit and a
    // vulnerable drop-disengage on the vertical channel.
    private const float DefenseDrop = 2.5f;
    private const float DropPursuit = 2.0f;         // opponent below a thin floor → drop onto them
    // 2026-07-20 reflect genes: knowing the shield/dash SENDS THE BOLT BACK makes it
    // the better answer to a ranged threat (designer: reflect should increase
    // defensive usage of these options).
    private const float ReflectDefenseBoost = 1.5f;
    // Chain defense (2026-09-10, CHANGE_LOG #37 — designer bug report: inward-knockback
    // kits chained stunned victims who neither DI'd out nor defended on stun exit):
    // the defense channel also triggers on the first decision after leaving Stun with
    // the attacker inside this range, and low-damage DI holds AWAY from the attacker
    // (survival DI toward the far blast line takes over past HighDamageThreshold).
    private const float ChainEscapeRange = 3f;
    // Nearest-platform recovery (2026-09-10, CHANGE_LOG #38): below this horizontal
    // speed the momentum split is off and recovery is purely nearest-reachable.
    private const float RecoverMomentumEpsilon = 0.1f;
    private const float BaselineVerticalNeutral = 0.5f;
    private const float FastFallPursuit = 1.2f;     // opponent below → drop onto them
    private const float CrouchBrake = 2.0f;         // negative-accel crouch at high slide speed
    private const float CrouchSlideApproach = 1.0f; // positive-accel crouch toward a far opponent
    private const float DIHold = 1.8f;              // held direction toward safety when a hit is coming
    private const float DIHoldVertical = 0.8f;
    // Dash offense/utility (2026-07-13): recovery is where the dash shines.
    private const float DashRecover = 2.5f;
    private const float DashApproach = 1.2f;
    private const float DashApproachRange = 5f;
    private const float DashPunish = 2.0f;
    // Recovery aim (2026-07-13 playtest fix): dashes target a LANDING POINT above the
    // platform top, never the box's closest point (which is the underside from below
    // and the lip from above). Clearance = how far above the top to aim; the dash is
    // saved when already above the top unless the horizontal gap is large.
    private const float RecoveryAimClearance = 1.2f;
    private const float DashRecoverHorizontalGap = 2.5f;
    private const float BreakPunishBonus = 2.0f;
    private const float BreakPunishDamagePreference = 0.2f;
    // Pursuit-from-above window (fast-fall pursuit and its thin-drop sibling):
    // opponent clearly below and roughly in our column.
    private const float PursuitVerticalGap = 1.5f;
    private const float PursuitColumnWidth = 2f;
    // Aim the raised shield only while it is small relative to the body — a shield
    // this many times the larger body half extent (or more) covers everything anyway.
    private const float SmallShieldAimFactor = 2f;
    // How far a threat arc/corridor must clear the crouched silhouette to duck it.
    private const float CrouchClearanceEpsilon = 0.05f;

    private readonly Pcg32 _rng;
    private readonly AgentConfig _config;
    private PlatformGraph? _graph; // per-match, built lazily on first GetInput

    // Committed decision, held until the window expires or a salient event fires.
    private float _heldHorizontal;
    private float _heldVertical;
    private int _ticksUntilRedecide;

    // Event-edge memory for early re-decision.
    private bool _wasGrounded = true;
    private bool _wasOverPit;
    private bool _couldHit;
    private bool _wasProjectileThreat;
    private bool _wasStunned;

    // Committed dash intent (held from the press, steered through warm-up).
    private float _dashIntentH;
    private float _dashIntentV;

    // Raised-shield decision state (ManageRaisedShield).
    private bool _heldShieldHold = true; // entering Shield implies the raise decision
    private float _heldAimH;
    private float _heldAimV;

    // Reused score buffers (never reallocated; contents rewritten before each Select).
    private readonly float[] _defenseScores = new float[DefenseOptionCount];
    private readonly float[] _shieldScores = new float[2];

    /// <summary>The defense channel's option slots — indices into _defenseScores
    /// (slot 6 = the thin drop, 2026-09-01).</summary>
    private enum DefenseOption
    {
        None = 0,
        Jump = 1,
        Shield = 2,
        Dash = 3,
        FastFall = 4,
        Crouch = 5,
        Drop = 6,
    }

    private const int DefenseOptionCount = 7;

    public UtilityAgent(Pcg32 rng, AgentConfig? config = null)
    {
        _rng = rng;
        _config = config ?? AgentConfig.Default;
        if (_config.DecisionIntervalTicks < 1)
        {
            throw new ArgumentException("DecisionIntervalTicks must be >= 1.");
        }
    }

    public InputFrame GetInput(SimWorld world, int playerIndex)
    {
        SimPlayer self = world.Players[playerIndex];
        if (self.Eliminated)
        {
            return InputFrame.Neutral; // out of the match (2026-08-12) — no RNG spent
        }
        SimPlayer opponent = SelectTarget(world, playerIndex);
        _graph ??= new PlatformGraph(world.Platforms, self, world.Config.Gravity, world.PlatformThin);

        if (self.State == PlayerState.Shield)
        {
            return ManageRaisedShield(world, self, opponent);
        }
        _heldShieldHold = true; // a future raise starts committed to holding

        if (self.State == PlayerState.Dash)
        {
            // Steer the held direction toward the dash intent during warm-up; travel
            // ignores inputs anyway. The intent WAS the decision — no re-rolls.
            return new InputFrame(_dashIntentH, _dashIntentV, false, 0);
        }

        // _wasStunned still holds LAST tick's state here (updated in the salient
        // block below) — Stun-exit detection depends on that ordering.
        UtilityContext ctx = BuildContext(world, self, opponent, _graph,
            justExitedStun: _wasStunned && self.State != PlayerState.Stun);

        if (ctx.OverPit)
        {
            self.RecoveryTicks++; // research stat, counted per tick like the DT did
        }

        bool salient =
            (self.State == PlayerState.Stun && !_wasStunned) ||
            // Stun EXIT is as salient as entry (2026-09-10, CHANGE_LOG #37): waiting
            // out the decision interval hands a chaining attacker up to 8 free ticks.
            ctx.JustExitedStun ||
            (self.IsGrounded != _wasGrounded) ||
            (ctx.OverPit != _wasOverPit) ||
            (ctx.AnyCanHit && !_couldHit) ||
            (ctx.ProjectileThreat && !_wasProjectileThreat); // a shot appears — react
        _wasStunned = self.State == PlayerState.Stun;
        _wasGrounded = self.IsGrounded;
        _wasOverPit = ctx.OverPit;
        _couldHit = ctx.AnyCanHit;
        _wasProjectileThreat = ctx.ProjectileThreat;

        if (--_ticksUntilRedecide > 0 && !salient)
        {
            // Held frame: movement AND vertical persist (crouch/fast-fall are holds);
            // jump/attack were single-tick presses.
            return new InputFrame(_heldHorizontal, _heldVertical, false, 0);
        }
        _ticksUntilRedecide = _config.DecisionIntervalTicks;

        var scores = new UtilityScores(self);
        foreach (IUtilityBehavior behavior in Behaviors)
        {
            behavior.Contribute(in ctx, scores);
        }

        int moveChoice = Select(scores.Horizontal);          // UtilityScores.Left/HNeutral/Right
        int verticalChoice = Select(scores.Vertical);        // UtilityScores.Down/VNeutral/Up
        int jumpChoice = Select(scores.Jump);                // UtilityScores.NoJump/DoJump
        int attackChoice = Select(scores.Attack);            // 0 none, else candidate

        ApplyDefenseChannel(in ctx, scores, ref moveChoice, ref verticalChoice,
            ref jumpChoice, ref attackChoice);
        bool dashChosen = TryLatchDashIntent(in ctx, scores, attackChoice);

        // Channel slots map to axis values as slot − 1 (Left/Down = −1 … Right/Up = +1).
        _heldHorizontal = dashChosen ? _dashIntentH : moveChoice - 1;
        _heldVertical = dashChosen ? _dashIntentV : verticalChoice - 1;
        byte actions = attackChoice > 0
            ? InputFrame.ActionBit(scores.AttackButtons[attackChoice])
            : (byte)0;
        return new InputFrame(_heldHorizontal, _heldVertical, jumpChoice == UtilityScores.DoJump, actions);
    }

    /// <summary>
    /// Defense channel (2026-07-13, replaces the pairwise dodge/shield coin): on a
    /// telegraphed swing with no counter-hit available — trade-commit: landing OUR
    /// melee interrupts THEIRS — ONE weighted-random pick among the defense options.
    /// An incoming projectile (2026-07-14) triggers defense REGARDLESS of counter-hit
    /// options: the bolt is committed damage already in flight, and counter-firing
    /// does nothing to stop it (unlike the melee trade). Draws RNG (one Select) ONLY
    /// when the trigger condition holds — the RNG stream is part of the instrument.
    /// </summary>
    private void ApplyDefenseChannel(in UtilityContext ctx, UtilityScores scores,
        ref int moveChoice, ref int verticalChoice, ref int jumpChoice, ref int attackChoice)
    {
        // Chain escape (2026-09-10, CHANGE_LOG #37): the first decision after leaving
        // Stun with the attacker in chain range is a defense moment even before any
        // telegraph — inward-knockback kits re-swing faster than the telegraph scan
        // reacts. A counter-hit in hand still takes priority (trade-commit: landing
        // OUR melee interrupts the chain just as well).
        bool chainEscape = ctx.JustExitedStun && ctx.Distance < ChainEscapeRange && !ctx.AnyCanHit;
        bool triggered = (ctx.TelegraphThreat && !ctx.AnyCanHit || ctx.ProjectileThreat || chainEscape)
            && ctx.Self.State is not (PlayerState.WarmUp or PlayerState.Attack);
        if (!triggered)
        {
            return;
        }
        bool jumpAvailable = ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted;
        float shieldMargin = MathF.Max(0f,
            (ctx.ShieldHealthFraction - ShieldReleaseHealthFraction) / (1f - ShieldReleaseHealthFraction));
        bool shieldUsable = ctx.Self.State == PlayerState.Idle && shieldMargin > 0f && !ctx.OverPit;
        bool airborne = !ctx.Self.IsGrounded;
        bool fastFallVulnState = ctx.Self.State is PlayerState.WarmUp
            or PlayerState.CoolDown or PlayerState.AirJumpsExhausted;
        _defenseScores[(int)DefenseOption.None] = DefenseNone;
        _defenseScores[(int)DefenseOption.Jump] = jumpAvailable ? DefenseJump : 0f;
        // Reflect awareness (2026-07-20): against a RANGED threat, an option that
        // re-fires the bolt outranks one that merely avoids it.
        float shieldBoost = ctx.RangedThreat && FirstShieldReflects(ctx.Self) ? ReflectDefenseBoost : 1f;
        float dashBoost = ctx.RangedThreat && ctx.DashSlot >= 0
            && ctx.Self.Dashes[ctx.DashSlot] is { Reflect: true } ? ReflectDefenseBoost : 1f;
        _defenseScores[(int)DefenseOption.Shield] = shieldUsable ? DefenseShield * shieldMargin * shieldBoost : 0f;
        _defenseScores[(int)DefenseOption.Dash] = ctx.DashUsable ? DefenseDash * dashBoost : 0f;
        _defenseScores[(int)DefenseOption.FastFall] = airborne && ctx.Self.FastFallAcceleration > 0f
            ? (fastFallVulnState ? DefenseFastFallVulnerable : DefenseFastFall) : 0f;
        // Ducking on a thin platform without a landing below would turn into a
        // suicide drop once the delay elapses — gate it (2026-09-01); thin-free
        // stages see the exact pre-feature condition.
        _defenseScores[(int)DefenseOption.Crouch] = ctx.CrouchClearsThreat
            && (!ctx.OnThinPlatform || ctx.CanDropSafely) ? DefenseCrouch : 0f;
        // Drop-through escape (2026-09-01): standing on a thin platform with a
        // safe landing below, holding down rides the crouch drop out of the arc.
        _defenseScores[(int)DefenseOption.Drop] = ctx.CanDropSafely
            && ctx.Self.State is PlayerState.Idle or PlayerState.Crouch ? DefenseDrop : 0f;
        var defense = (DefenseOption)Select(_defenseScores);
        int away = -ctx.FacingToOpponent;
        switch (defense)
        {
            case DefenseOption.Jump:
                jumpChoice = UtilityScores.DoJump;
                moveChoice = UtilityScores.Toward(away);
                attackChoice = 0;
                break;
            case DefenseOption.Shield:
                jumpChoice = UtilityScores.NoJump;
                attackChoice = ShieldCandidate(scores, ctx.Self);
                break;
            case DefenseOption.Dash:
                jumpChoice = UtilityScores.NoJump;
                attackChoice = DashCandidate(scores, ctx.DashSlot);
                break;
            case DefenseOption.FastFall: // fast fall out of the arc
                jumpChoice = UtilityScores.NoJump;
                attackChoice = 0;
                verticalChoice = UtilityScores.Down;
                break;
            case DefenseOption.Crouch: // duck under it
                jumpChoice = UtilityScores.NoJump;
                attackChoice = 0;
                verticalChoice = UtilityScores.Down;
                moveChoice = UtilityScores.HNeutral; // stay planted; the FSM enters Crouch from Idle+down
                break;
            case DefenseOption.Drop: // drop through the thin platform (2026-09-01)
                jumpChoice = UtilityScores.NoJump;
                attackChoice = 0;
                verticalChoice = UtilityScores.Down;
                moveChoice = UtilityScores.HNeutral; // held down: crouch → sink → delay → drop
                break;
        }
    }

    /// <summary>A selected dash press locks in its intent direction (held from the
    /// press itself and steered through warm-up): recovery → the landing aim above
    /// the platform; threatened → away; otherwise → the opponent.</summary>
    private bool TryLatchDashIntent(in UtilityContext ctx, UtilityScores scores, int attackChoice)
    {
        bool dashChosen = attackChoice > 0
            && ctx.Self.MoveTypeAt(scores.AttackMoves[attackChoice]) == Genome.MoveType.Dash;
        if (!dashChosen)
        {
            return false;
        }
        Vec2 target = ctx.OverPit && ctx.RecoverTargetValid ? ctx.RecoverAim - ctx.Self.Position
            : ctx.TelegraphThreat ? new Vec2(-ctx.FacingToOpponent, 0f)
            : ctx.Opponent.Position - ctx.Self.Position;
        _dashIntentH = MathF.Sign(target.X);
        _dashIntentV = MathF.Sign(target.Y);
        if (ctx.OverPit && _dashIntentV < 0f)
        {
            _dashIntentV = 0f; // a recovery dash never points downward (designer)
        }
        return true;
    }

    /// <summary>Does the shield the defense channel would raise (the first shield
    /// slot — ShieldCandidate's convention) carry the reflect gene?</summary>
    private static bool FirstShieldReflects(SimPlayer self)
    {
        foreach (SimShield? shield in self.Shields)
        {
            if (shield is not null)
            {
                return shield.Reflect;
            }
        }
        return false;
    }

    private static int ShieldCandidate(UtilityScores scores, SimPlayer self)
    {
        for (int c = 1; c < scores.Attack.Length; c++)
        {
            if (self.MoveTypeAt(scores.AttackMoves[c]) == Genome.MoveType.Shield)
            {
                return c;
            }
        }
        return 0;
    }

    private static int DashCandidate(UtilityScores scores, int dashSlot)
    {
        for (int c = 1; c < scores.Attack.Length; c++)
        {
            if (scores.AttackMoves[c] == dashSlot)
            {
                return c;
            }
        }
        return 0;
    }

    /// <summary>
    /// Shield management (2026-07-12 humanization, designer-directed): hold/release
    /// and aim go through the SAME imperfection machinery as everything else — the
    /// commitment window delays reactions and the randomness mixture makes release
    /// timing fallible — so evolution cannot tune shield timings against
    /// frame-perfect execution no human could deliver. The one deterministic override:
    /// health at/below the release threshold always releases (the circle is visibly
    /// red — even humans don't miss that).
    /// </summary>
    private InputFrame ManageRaisedShield(SimWorld world, SimPlayer self, SimPlayer opponent)
    {
        float healthFraction = ShieldHealthFractionOf(self);
        if (healthFraction <= ShieldReleaseHealthFraction)
        {
            _heldShieldHold = false; // forced release — never ride into the break
        }
        else if (--_ticksUntilRedecide <= 0)
        {
            _ticksUntilRedecide = _config.DecisionIntervalTicks;
            // ANY present enemy attacking or close keeps the shield up (2026-08-12 —
            // the single-enemy test is the old one exactly); aim stays at the target.
            bool threatened = false;
            foreach (SimPlayer enemy in world.Players)
            {
                if (enemy == self || enemy.IsAbsent)
                {
                    continue;
                }
                threatened |= enemy.State is PlayerState.WarmUp or PlayerState.Attack
                    || (enemy.Position - self.Position).Length() <= ShieldHoldRange;
            }
            _shieldScores[0] = ShieldReleaseBaseline;
            _shieldScores[1] = threatened ? ShieldHoldUtility : ShieldUnthreatenedHold;
            _heldShieldHold = Select(_shieldScores) == 1;

            _heldAimH = 0f;
            _heldAimV = 0f;
            if (_heldShieldHold && self.ShieldRadius < MathF.Max(self.BodyHalf.X, self.BodyHalf.Y) * SmallShieldAimFactor)
            {
                _heldAimH = MathF.Sign(opponent.Position.X - self.Position.X);
                _heldAimV = MathF.Sign(opponent.Position.Y - self.Position.Y);
            }
        }

        byte actions = _heldShieldHold && self.ShieldButton >= 0
            ? InputFrame.ActionBit(self.ShieldButton)
            : (byte)0;
        return new InputFrame(_heldAimH, _heldAimV, false, actions);
    }

    private static float ShieldHealthFractionOf(SimPlayer self)
    {
        for (int slot = 0; slot < self.Shields.Count; slot++)
        {
            if (self.Shields[slot] is SimShield shield && self.ButtonForMove(slot) >= 0)
            {
                return shield.InitialRadius <= 0f ? 0f : self.ShieldHealths[slot] / shield.InitialRadius;
            }
        }
        return 0f;
    }

    /// <summary>
    /// Normalize to sum 1, then the randomness mixture: probability (1−r) argmax
    /// (ties → lowest index), probability r proportional sample. All-zero → uniform.
    /// </summary>
    private int Select(float[] scores)
    {
        float sum = 0f;
        for (int i = 0; i < scores.Length; i++)
        {
            sum += scores[i];
        }

        if (_rng.NextFloat() >= _config.Randomness)
        {
            if (sum <= 0f)
            {
                return 0;
            }
            int best = 0;
            for (int i = 1; i < scores.Length; i++)
            {
                if (scores[i] > scores[best])
                {
                    best = i;
                }
            }
            return best;
        }

        float roll = _rng.NextFloat(); // in [0,1): proportional over normalized scores
        if (sum <= 0f)
        {
            return (int)(roll * scores.Length); // uniform
        }
        float cumulative = 0f;
        for (int i = 0; i < scores.Length; i++)
        {
            cumulative += scores[i] / sum;
            if (roll < cumulative)
            {
                return i;
            }
        }
        return scores.Length - 1; // float round-off guard
    }

    // ── Target selection (2026-08-12, four-player.md; CHANGE_LOG #32) ──────────

    /// <summary>
    /// The enemy this agent fights: nearest non-eliminated enemy, preferring PRESENT
    /// over blacked-out and damage-VULNERABLE over spawn-immune (the #29 rule said
    /// don't swing at ghosts — with a choice, don't even chase one); within a tier,
    /// smallest squared distance, ties to the lower index. Deterministic, ZERO RNG,
    /// re-evaluated every query. With a single enemy every tier reduces to
    /// world.Players[1 - playerIndex] — the pre-feature opponent — so 2P behavior is
    /// bit-identical (utility golden unmoved). Threat REACTIONS scan all enemies
    /// regardless of target (designer: dodge whoever is winding up on you).
    /// </summary>
    public static SimPlayer SelectTarget(SimWorld world, int playerIndex) // public: tests pin it
    {
        SimPlayer self = world.Players[playerIndex];
        SimPlayer? best = null;
        int bestTier = int.MaxValue;
        float bestDistSq = float.MaxValue;
        for (int i = 0; i < world.Players.Count; i++)
        {
            if (i == playerIndex)
            {
                continue;
            }
            SimPlayer enemy = world.Players[i];
            if (enemy.Eliminated)
            {
                continue;
            }
            int tier = enemy.IsRespawning ? 2 : enemy.SpawnDamageImmune ? 1 : 0;
            Vec2 d = enemy.Position - self.Position;
            float distSq = d.X * d.X + d.Y * d.Y;
            if (tier < bestTier || (tier == bestTier && distSq < bestDistSq))
            {
                best = enemy;
                bestTier = tier;
                bestDistSq = distSq;
            }
        }
        // Every enemy eliminated ⇒ the match is over before the next input is
        // sampled; return an inert deterministic reference just in case.
        return best ?? world.Players[playerIndex == 0 ? 1 : 0];
    }

    // ── Context ────────────────────────────────────────────────────────────────

    private static UtilityContext BuildContext(SimWorld world, SimPlayer self, SimPlayer opponent,
        PlatformGraph graph, bool justExitedStun)
    {
        // Dash availability first — recovery reachability must credit a usable dash
        // (2026-07-13 playtest fix: with jumps spent, the dash IS the way back up).
        (int dashSlot, bool dashUsable, float dashRange) = ResolveDashCapability(world, self);

        bool overPit = AgentGeometry.OverPit(world, self, 0f);
        Vec2 recoverTarget = Vec2.Zero;
        Aabb recoverPlatform = default;
        bool targetSensed = false, reachable = false;
        if (overPit)
        {
            // Nearest-platform recovery (2026-09-10, CHANGE_LOG #38 — reverses the
            // 2026-07-10 chase-preserving pick): among REACHABLE sensed platforms,
            // prefer the one closest to SELF — the reliable ledge back, not the
            // opponent's far platform.
            targetSensed = TrySensedRecoverTarget(world, self, dashRange,
                out recoverTarget, out recoverPlatform, out reachable);
        }

        int facingToOpponent = opponent.Position.X >= self.Position.X ? 1 : -1;
        var canHit = new bool[self.Moves.Count];
        bool anyCanHit = false;
        // Spawn immunity (2026-07-22, CHANGE_LOG #29): an intangible/invulnerable
        // opponent takes no damage — "agents shouldn't attempt to attack an invulnerable
        // enemy." Force every hit-check false so attack/projectile/doomed don't swing at
        // a ghost. Gated on the SPAWN immunity only (not the 0.1 s post-hit
        // invincibility), so legacy matches leave the instrument untouched.
        bool opponentImmune = opponent.SpawnDamageImmune;
        Vec2 attackTarget = ComputeMeleeReach(self, opponent, facingToOpponent,
            opponentImmune, canHit, ref anyCanHit);
        ComputeProjectileReach(world, self, opponent, opponentImmune, canHit, ref anyCanHit);

        (bool telegraphThreat, bool rangedTelegraph, bool crouchClearsThreat) =
            ScanTelegraphThreats(world, self);

        bool projectileThreat = ScanIncomingProjectiles(world, self, ref crouchClearsThreat);

        bool underThreat = ScanEnemyThreatReach(world, self);

        // Vulnerable = cannot attack (CoolDown / AirJumpsExhausted). Since the
        // 2026-07-23 exhaustion rule (CHANGE_LOG #31) a dash in hand keeps the
        // character in Air — able to attack, so chasing is legitimate there; the
        // exhausted state now always means the WHOLE air budget is gone.
        bool vulnerable = self.State is PlayerState.CoolDown or PlayerState.AirJumpsExhausted;
        // The recovery LANDING AIM: above the platform top, x clamped to its span —
        // "get above the platform before floating/jumping onto it" (designer).
        Vec2 recoverAim = targetSensed
            ? new Vec2(
                MathF.Min(MathF.Max(self.Position.X, recoverPlatform.Left), recoverPlatform.Right),
                recoverPlatform.Top + RecoveryAimClearance)
            : Vec2.Zero;

        (int flankDirection, bool flankSafe) = ComputeFlank(world, self, opponent);

        // Thin platforms (2026-09-01, CHANGE_LOG #34): where the character stands and
        // whether a crouch drop from here lands somewhere. All false on thin-free
        // stages — the instrument is untouched there (utility golden unmoved).
        int myPlatform = graph.PlatformAt(self.Position);
        bool onThinPlatform = self.IsGrounded && myPlatform >= 0 && graph.IsThin(myPlatform);
        bool canDropSafely = onThinPlatform && graph.TryDropLanding(myPlatform, self.Position.X, out _);

        // Traversal: next hop toward the opponent's platform via the per-match graph.
        (bool hasTraversal, Vec2 traversalLaunch, int traversalDirection,
            bool traversalNeedsJump, bool traversalDrop) =
            ComputeTraversal(graph, self, opponent, myPlatform, onThinPlatform);

        // Every argument named: the record has 30+ parameters with same-typed
        // neighbors, and a silent positional swap here would surface only as a
        // golden-hash diff.
        return new UtilityContext(
            World: world,
            Self: self,
            Opponent: opponent,
            OverPit: overPit,
            Doomed: overPit && !reachable,
            RecoverTarget: recoverTarget,
            RecoverTargetValid: targetSensed && reachable,
            Distance: (opponent.Position - self.Position).Length(),
            CanHit: canHit,
            AnyCanHit: anyCanHit,
            FacingToOpponent: facingToOpponent,
            AttackTarget: attackTarget,
            UnderThreat: underThreat,
            FlankDirection: flankDirection,
            FlankSafe: flankSafe,
            HasTraversal: hasTraversal,
            TraversalLaunch: traversalLaunch,
            TraversalDirection: traversalDirection,
            TraversalNeedsJump: traversalNeedsJump,
            Vulnerable: vulnerable,
            ShieldHealthFraction: ShieldHealthFractionOf(self),
            OpponentBreakStunned: opponent.State == PlayerState.Stun && opponent.StunFromShieldBreak,
            TelegraphThreat: telegraphThreat,
            DashUsable: dashUsable,
            DashSlot: dashSlot,
            OpponentStunned: opponent.State == PlayerState.Stun,
            RecoverAim: recoverAim,
            CrouchClearsThreat: crouchClearsThreat,
            ProjectileThreat: projectileThreat,
            RangedThreat: rangedTelegraph || projectileThreat,
            OnThinPlatform: onThinPlatform,
            CanDropSafely: canDropSafely,
            TraversalDrop: traversalDrop,
            JustExitedStun: justExitedStun);
    }

    /// <summary>Where the mirrored hitbox of <paramref name="move"/> sits when
    /// <paramref name="owner"/> faces <paramref name="facing"/> — SimPlayer.Hitbox
    /// with the facing (and an optional inflation margin) parameterized. One home
    /// for the reach test, the telegraph arc, and the enemy-threat scan.</summary>
    private static Aabb MoveHitbox(SimPlayer owner, SimMove move, int facing, float inflate = 0f) =>
        new(
            owner.Position + new Vec2(move.Offset.X * facing, move.Offset.Y),
            new Vec2(
                move.BaseHalf.X * owner.WidthScalar + inflate,
                move.BaseHalf.Y * owner.HeightScalar + inflate));

    /// <summary>Top of the crouched silhouette (grounded, feet planted).</summary>
    private static float CrouchedTopY(SimPlayer self)
    {
        float feetY = self.Position.Y - self.BodyHalf.Y;
        return feetY + 2f * self.BodyHalf.Y * self.CrouchHeightRatio;
    }

    /// <summary>Sensor: the first button-mapped dash slot, whether it is usable this
    /// tick, and its straight-line travel range (0 when unusable).</summary>
    private static (int Slot, bool Usable, float Range) ResolveDashCapability(SimWorld world, SimPlayer self)
    {
        int dashSlot = -1;
        for (int m = 0; m < self.Dashes.Count; m++)
        {
            if (self.Dashes[m] is not null && self.ButtonForMove(m) >= 0)
            {
                dashSlot = m;
                break;
            }
        }
        bool dashUsable = dashSlot >= 0 && self.CanDash
            && self.State is PlayerState.Idle or PlayerState.Air or PlayerState.AirJumpsExhausted;
        float dashRange = dashUsable
            ? self.Dashes[dashSlot]!.Speed * self.Dashes[dashSlot]!.DurationTicks * world.Config.Dt
            : 0f;
        return (dashSlot, dashUsable, dashRange);
    }

    /// <summary>Sensor: which melee moves can hit right now (facing toward the
    /// opponent — turning is a same-tick input), and the position to fight FROM:
    /// stand where the best move's hitbox lands on the opponent (the DT's
    /// relMove×1.2 chase, generalized per move). A downward move makes the agent
    /// seek height above the opponent — the hop-over corridor dance the paper
    /// observed emerges from the genome, not the code.</summary>
    private static Vec2 ComputeMeleeReach(SimPlayer self, SimPlayer opponent, int facingToOpponent,
        bool opponentImmune, bool[] canHit, ref bool anyCanHit)
    {
        Vec2 attackTarget = opponent.Position;
        float bestTravel = float.PositiveInfinity;
        for (int m = 0; m < self.Moves.Count; m++)
        {
            if (self.ButtonForMove(m) < 0 || self.Moves[m] is not SimMove move)
            {
                continue; // shield slots have no hitbox to reach with
            }
            Vec2 offset = new(move.Offset.X * facingToOpponent, move.Offset.Y);
            Aabb hitbox = MoveHitbox(self, move, facingToOpponent);
            canHit[m] = !opponentImmune && hitbox.Overlaps(opponent.Body);
            anyCanHit |= canHit[m];

            Vec2 candidate = opponent.Position - offset * 1.2f;
            float travel = (candidate - self.Position).Length();
            if (travel < bestTravel)
            {
                bestTravel = travel;
                attackTarget = candidate;
            }
        }
        return attackTarget;
    }

    /// <summary>Sensor: projectile reach (2026-07-14) — a LOOSE corridor prediction
    /// per the spec: horizontal distance within the closed-form range, vertical
    /// offset within the path's lateral envelope (+slack), and outside the
    /// close-range gate. The candidate then scores on the attack channel via
    /// ProjectileBehavior.</summary>
    private static void ComputeProjectileReach(SimWorld world, SimPlayer self, SimPlayer opponent,
        bool opponentImmune, bool[] canHit, ref bool anyCanHit)
    {
        for (int m = 0; m < self.Moves.Count; m++)
        {
            if (self.ButtonForMove(m) < 0 || self.ProjectileMoves[m] is not SimProjectileMove ranged)
            {
                continue;
            }
            // Commit awareness + horizontal lead (2026-09-04, CHANGE_LOG #35): the
            // shot is a warm-up commitment, so AIM AT THE RELEASE MOMENT — the
            // target's position led by its current velocity over the warm-up. A
            // closing target's led position falls inside the close-range gate and
            // the shot is refused (the probe's point-blank releases at median dx
            // 1.3-2.2); a retreating target must still be in range at release.
            // Loose by design like the rest of the corridor: horizontal lead only.
            float warmUpSeconds = ranged.WarmUpTicks * world.Config.Dt;
            var led = new Determinism.Vec2(
                opponent.Position.X + opponent.Velocity.X * warmUpSeconds,
                opponent.Position.Y);
            canHit[m] = !opponentImmune
                && ProjectileCorridorHit(ranged, self, led, opponent.BodyHalf, world.Config);
            anyCanHit |= canHit[m];
        }
    }

    /// <summary>Sensor: an enemy is WINDING UP an attack whose arc (+margin) covers
    /// us — the readable moment defensive options respond to. ALL present enemies
    /// are scanned in index order (2026-08-12, designer: dodge whoever is winding up
    /// on you, not just the target) — with a single enemy this is exactly the old
    /// single-opponent test. A winding-up PROJECTILE telegraphs exactly like melee
    /// (2026-07-20, designer: warm-up phases signal defensive counterplay across
    /// the board): the "arc" is the shot's predicted corridor at our column — the
    /// same loose test the shooter aimed with, seen from the receiving end. That is
    /// what makes shields viable against zoners (warm-up + flight time to react);
    /// trade-commit still applies (interrupting the shooter cancels the shot).
    /// CrouchClears is true only if EVERY threatening source passes above the
    /// crouched silhouette (grounded, from Idle, feet planted).</summary>
    private static (bool Telegraph, bool Ranged, bool CrouchClears) ScanTelegraphThreats(
        SimWorld world, SimPlayer self)
    {
        bool telegraphThreat = false;
        bool rangedTelegraph = false;
        bool crouchClearsAllTelegraphs = true;
        foreach (SimPlayer enemy in world.Players)
        {
            if (enemy == self || enemy.IsAbsent || enemy.State != PlayerState.WarmUp)
            {
                continue;
            }
            float crouchedTop = CrouchedTopY(self);
            bool canDuck = self.IsGrounded && self.State == PlayerState.Idle;
            if (enemy.Moves[enemy.CurrentMoveIndex] is SimMove windingUp)
            {
                Aabb arc = MoveHitbox(enemy, windingUp, enemy.Facing, TelegraphDodgeMargin);
                if (arc.Overlaps(self.Body))
                {
                    telegraphThreat = true;
                    crouchClearsAllTelegraphs &= canDuck
                        && arc.Bottom > crouchedTop + CrouchClearanceEpsilon;
                }
            }
            else if (enemy.ProjectileMoves[enemy.CurrentMoveIndex] is SimProjectileMove windingShot
                && ProjectileCorridorHit(windingShot, enemy, self, world.Config))
            {
                telegraphThreat = true;
                rangedTelegraph = true;
                float corridorBottom = ProjectileCorridorCenterY(
                    windingShot, enemy, self, world.Config) - windingShot.HalfExtent;
                crouchClearsAllTelegraphs &= canDuck
                    && corridorBottom > crouchedTop + CrouchClearanceEpsilon;
            }
        }
        return (telegraphThreat, rangedTelegraph, telegraphThreat && crouchClearsAllTelegraphs);
    }

    /// <summary>Sensor: incoming projectiles (2026-07-14) — sample each dangerous
    /// projectile's closed-form path over the lookahead; a predicted overlap with
    /// our body (inflated by its half extent) is a threat the defense channel
    /// answers. Ducking helps only if every threatening sample passes above the
    /// crouched silhouette — same geometry rule as the melee arc — so this may
    /// also flip <paramref name="crouchClearsThreat"/> on.</summary>
    private static bool ScanIncomingProjectiles(SimWorld world, SimPlayer self, ref bool crouchClearsThreat)
    {
        bool projectileThreat = false;
        if (world.Projectiles.Count == 0)
        {
            return false;
        }
        float minPredictedBottom = float.MaxValue;
        Aabb body = self.Body;
        foreach (SimProjectile incoming in world.Projectiles)
        {
            bool dangerous = incoming.Owner != self.Index
                || (incoming.Move.HitsSelf && incoming.ClearedOwner);
            if (!dangerous)
            {
                continue;
            }
            var threatBox = new Aabb(body.Center,
                new Vec2(body.Half.X + incoming.Move.HalfExtent, body.Half.Y + incoming.Move.HalfExtent));
            for (int k = ProjectileLookaheadStep; k <= ProjectileLookaheadTicks; k += ProjectileLookaheadStep)
            {
                Vec2 predicted = incoming.Move.PositionAt(
                    incoming.Origin, incoming.Facing, incoming.PathAgeTicks + k, world.Config);
                if (threatBox.Contains(predicted))
                {
                    projectileThreat = true;
                    minPredictedBottom = MathF.Min(
                        minPredictedBottom, predicted.Y - incoming.Move.HalfExtent);
                }
            }
        }
        if (projectileThreat && self.IsGrounded && self.State == PlayerState.Idle)
        {
            crouchClearsThreat |= minPredictedBottom > CrouchedTopY(self) + CrouchClearanceEpsilon;
        }
        return projectileThreat;
    }

    /// <summary>Sensor: can any ENEMY's move reach me right now (their facing toward
    /// me)? Humans see the incoming swing arc and leave it. All present enemies
    /// since 2026-08-12 — the single-enemy scan is the old one exactly.</summary>
    private static bool ScanEnemyThreatReach(SimWorld world, SimPlayer self)
    {
        bool underThreat = false;
        foreach (SimPlayer enemy in world.Players)
        {
            if (enemy == self || enemy.IsAbsent || underThreat)
            {
                continue;
            }
            // Exactly the old -facingToOpponent, including the >= tie-break at equal X.
            int enemyFacing = enemy.Position.X >= self.Position.X ? -1 : 1;
            for (int m = 0; m < enemy.Moves.Count && !underThreat; m++)
            {
                if (enemy.ButtonForMove(m) < 0 || enemy.Moves[m] is not SimMove move)
                {
                    continue;
                }
                underThreat = MoveHitbox(enemy, move, enemyFacing).Overlaps(self.Body);
            }
        }
        return underThreat;
    }

    /// <summary>Sensor: the next hop toward the opponent's platform via the
    /// per-match graph — walk target, hop direction, whether the hop needs the
    /// jump, or a crouch-drop route (2026-09-01: standing on a thin platform whose
    /// next hop is BELOW under its span, the route is a crouch drop — walk over
    /// the overlap and hold down, no jump, no edge detour).</summary>
    private static (bool Has, Vec2 Launch, int Direction, bool NeedsJump, bool Drop) ComputeTraversal(
        PlatformGraph graph, SimPlayer self, SimPlayer opponent, int myPlatform, bool onThinPlatform)
    {
        int theirPlatform = graph.PlatformAt(opponent.Position);
        if (myPlatform < 0 || theirPlatform < 0 || myPlatform == theirPlatform
            || !graph.TryRoute(myPlatform, theirPlatform, out int nextPlatform))
        {
            return (false, Vec2.Zero, 0, false, false);
        }
        Aabb mine = graph.Platform(myPlatform);
        Aabb next = graph.Platform(nextPlatform);
        float overlapLo = MathF.Max(mine.Left, next.Left);
        float overlapHi = MathF.Min(mine.Right, next.Right);
        if (onThinPlatform && next.Top < mine.Top - 0.5f && overlapHi - overlapLo >= 0.5f)
        {
            var dropLaunch = new Vec2(
                DetMath.Clamp(self.Position.X, overlapLo + 0.25f, overlapHi - 0.25f), mine.Top);
            return (true, dropLaunch, 0, false, true);
        }
        float launchX = next.Center.X >= mine.Center.X ? mine.Right : mine.Left;
        var launch = new Vec2(launchX, mine.Top);
        int direction = next.Center.X >= mine.Center.X ? 1 : -1;
        // Hop only for a real height gain or a real horizontal gap (2026-07-22,
        // CHANGE_LOG #28). The old test (next.Top >= mine.Top − 0.5) jumped between
        // platforms at the SAME height that were horizontally ADJACENT — common on
        // large mirrored maps, where the two center halves touch — burning the air
        // jump to "hop" across ground the agent could simply walk onto. A gap of 0
        // and no rise means walk; the horizontal move alone carries it across.
        float gap = MathF.Max(0f, MathF.Max(next.Left - mine.Right, mine.Left - next.Right));
        bool needsJump = next.Top > mine.Top + 0.5f || gap > 0.5f;
        return (true, launch, direction, needsJump, false);
    }

    /// <summary>
    /// The spec's "loose range of hits based on projectile shape": facing-toward
    /// reach out to the closed-form range (accounting for deceleration peaking early),
    /// vertical tolerance = the path's lateral envelope + the sine amplitude + slack +
    /// the target's half height, gated closed inside MinProjectileRange. Deliberately
    /// coarse — precision comes from the sim, misses are the humanizing noise.
    /// </summary>
    private static bool ProjectileCorridorHit(
        SimProjectileMove ranged, SimPlayer shooter, SimPlayer target, MatchConfig config) =>
        ProjectileCorridorHit(ranged, shooter, target.Position, target.BodyHalf, config);

    /// <summary>Position-based core (2026-09-04): the shooter aims at the LED target
    /// position (release-moment prediction); the telegraph scan keeps the plain
    /// current-position overload above — a committed shot threatens where you ARE.</summary>
    private static bool ProjectileCorridorHit(SimProjectileMove ranged, SimPlayer shooter,
        Determinism.Vec2 targetPos, Determinism.Vec2 targetHalf, MatchConfig config)
    {
        float dx = MathF.Abs(targetPos.X - shooter.Position.X);
        if (dx < MinProjectileRange)
        {
            return false;
        }
        float ttl = ranged.TtlTicks * config.Dt;
        float maxRange = ranged.LaunchSpeed * ttl + 0.5f * ranged.Acceleration * ttl * ttl;
        if (ranged.Acceleration < 0f)
        {
            float tPeak = -ranged.LaunchSpeed / ranged.Acceleration; // decelerating: s peaks here
            if (tPeak < ttl)
            {
                maxRange = ranged.LaunchSpeed * tPeak + 0.5f * ranged.Acceleration * tPeak * tPeak;
            }
        }
        if (maxRange <= 0f || dx > maxRange + 1f)
        {
            return false;
        }
        float centerY = ProjectileCorridorCenterY(ranged, shooter, targetPos, config);
        float tolerance = ProjectileCorridorSlack + targetHalf.Y
            + (ranged.Path == ProjectilePath.Sine ? ranged.SineAmplitude : 0f);
        return MathF.Abs(targetPos.Y - centerY) <= tolerance;
    }

    /// <summary>Where the shot's path sits vertically when it reaches the target's
    /// column (loose: time from launch speed alone). Shared by the shooter's aim test
    /// and the defender's wind-up telegraph (2026-07-20).</summary>
    private static float ProjectileCorridorCenterY(
        SimProjectileMove ranged, SimPlayer shooter, SimPlayer target, MatchConfig config) =>
        ProjectileCorridorCenterY(ranged, shooter, target.Position, config);

    private static float ProjectileCorridorCenterY(
        SimProjectileMove ranged, SimPlayer shooter, Determinism.Vec2 targetPos, MatchConfig config)
    {
        float dx = MathF.Abs(targetPos.X - shooter.Position.X);
        float t = dx / MathF.Max(ranged.LaunchSpeed, 0.5f);
        float centerY = shooter.Position.Y + ranged.LaunchFraction.Y * shooter.BodyHalf.Y;
        if (ranged.Path == ProjectilePath.Quadratic)
        {
            centerY -= ranged.PathScalar * ranged.QuadraticScale * dx * dx;
        }
        if (ranged.Gravity)
        {
            centerY -= 0.5f * config.Gravity * t * t;
        }
        return centerY;
    }

    /// <summary>Retreat direction: away from the opponent, flipped toward stage
    /// center when retreating would walk off the platform. Evade/ThreatDodge probe
    /// the edge only when GROUNDED; ExhaustedCaution deliberately probes airborne
    /// too (requireGrounded: false) — a preserved asymmetry from the original
    /// three copies of this logic.</summary>
    private static int SafeRetreatDirection(in UtilityContext ctx, bool requireGrounded)
    {
        int away = -ctx.FacingToOpponent;
        bool retreatFallsOff = (!requireGrounded || ctx.Self.IsGrounded)
            && AgentGeometry.OverPit(ctx.World, ctx.Self, EdgeProbeDistance * away);
        return retreatFallsOff ? TowardStageCenterX(ctx.Self) : away;
    }

    /// <summary>Horizontal direction toward the stage center. Generated stages are
    /// centered on x = 0 (StageGenerator invariant) — this bakes that in.</summary>
    private static int TowardStageCenterX(SimPlayer self) => self.Position.X >= 0f ? -1 : 1;

    /// <summary>
    /// Flank detection (2026-07-10, designer-reported stall): when the opponent is
    /// meaningfully above/below AND a platform's surface lies between the two heights
    /// across the horizontal span between them, the direct route is blocked — the
    /// approach target's X flips sign around the opponent and the character paces in
    /// place. Returns the horizontal direction toward the blocking platform's edge,
    /// preferring an edge with ground beyond it (self-preservation: flanking must not
    /// become a self-destruct); 0 when unblocked.
    /// </summary>
    private static (int Direction, bool Safe) ComputeFlank(SimWorld world, SimPlayer self, SimPlayer opponent)
    {
        float dy = opponent.Position.Y - self.Position.Y;
        if (MathF.Abs(dy) < VerticalBlockThreshold)
        {
            return (0, false);
        }
        float lowY = MathF.Min(self.Position.Y, opponent.Position.Y);
        float highY = MathF.Max(self.Position.Y, opponent.Position.Y);
        float leftX = MathF.Min(self.Position.X, opponent.Position.X);
        float rightX = MathF.Max(self.Position.X, opponent.Position.X);

        foreach (Aabb platform in world.Platforms) // fixed order → deterministic pick
        {
            if (platform.Top <= lowY || platform.Top >= highY
                || platform.Right < leftX || platform.Left > rightX)
            {
                continue;
            }
            // Blocked by this platform. Probe just beyond each edge for ground below.
            bool leftSafe = !AgentGeometry.OverPit(world, self, platform.Left - FlankEdgeProbe - self.Position.X);
            bool rightSafe = !AgentGeometry.OverPit(world, self, platform.Right + FlankEdgeProbe - self.Position.X);
            float leftDist = MathF.Abs(self.Position.X - platform.Left);
            float rightDist = MathF.Abs(platform.Right - self.Position.X);

            if (leftSafe != rightSafe)
            {
                return (leftSafe ? -1 : 1, true); // the safe edge wins regardless of distance
            }
            return (leftDist <= rightDist ? -1 : 1, leftSafe); // nearest edge; Safe=false halves the urge
        }
        return (0, false);
    }

    /// <summary>Recovery target among sensed platforms: the REACHABLE one whose
    /// LANDING SURFACE (nearest point on the top edge) is nearest to SELF; when none
    /// is reachable, the nearest-to-self landing point (the Doomed check's subject).
    ///
    /// HISTORY (CHANGE_LOG #38): from 2026-07-10 to 2026-09-10 the reachable pick was
    /// nearest-to-the-OPPONENT (chase-preserving directional recovery). The designer
    /// reversed it 2026-09-10: agents chasing an enemy off stage aimed their recovery
    /// at the enemy's platform — the far, risky option — and self-destructed when the
    /// margin didn't hold; the nearest reachable platform is the reliable ledge back.
    /// (This is also what makes thin side platforms recovery targets in practice —
    /// the opponent's platform is always solid by the at-least-one-solid rule, so the
    /// old rule never chose thin.) A pure nearest pick reintroduced the 2026-07-10
    /// oscillation stall verbatim (AChaseCrossesTheWholeLevel failed: every traversal
    /// hop got hijacked back to its origin platform), hence the momentum split.</summary>
    private static bool TrySensedRecoverTarget(
        SimWorld world, SimPlayer self, float dashRange,
        out Vec2 target, out Aabb chosenPlatform, out bool reachable)
    {
        var sense = AgentGeometry.SenseBox(world, self);
        target = Vec2.Zero;
        chosenPlatform = default;
        reachable = false;
        bool found = false;
        float bestFallback = float.PositiveInfinity;
        Vec2 fallback = Vec2.Zero;
        Aabb fallbackPlatform = default;
        // Momentum split: a platform is AHEAD when its closest point lies in the
        // direction of current horizontal motion. Nearest-AHEAD beats nearest-BEHIND
        // so a mid-hop traversal is never hijacked back to the platform just left
        // (the 2026-07-10 oscillation stall, guarded by UtilityAgentTraversalTests);
        // a chase past the last platform has nothing reachable ahead and turns back.
        float vx = self.Velocity.X;
        bool directional = MathF.Abs(vx) > RecoverMomentumEpsilon;
        float bestAhead = float.PositiveInfinity, bestBehind = float.PositiveInfinity;
        Vec2 aheadPoint = Vec2.Zero, behindPoint = Vec2.Zero;
        Aabb aheadPlatform = default, behindPlatform = default;
        foreach (Aabb platform in world.Platforms)
        {
            if (!sense.Overlaps(platform))
            {
                continue;
            }
            // Measure to the LANDING SURFACE (2026-09-10 designer amendment to
            // CHANGE_LOG #38): the nearest point on the platform's TOP edge, x
            // clamped to its span. The old collision-box ClosestPoint let a tall
            // solid platform's low SIDE point pass the reachability test — but
            // reaching a side is not landing, so agents committed to ledges they
            // could not climb. Thin platforms get the honest rise-through credit
            // for free: their landing surface is the same top edge, and the
            // vertical budget in EstimateReachable is exactly the rise they need.
            var point = new Vec2(
                MathF.Min(MathF.Max(self.Position.X, platform.Left), platform.Right),
                platform.Top);
            found = true;
            float toSelf = (point - self.Position).Length();
            if (toSelf < bestFallback)
            {
                bestFallback = toSelf;
                fallback = point;
                fallbackPlatform = platform;
            }
            // A usable dash extends reach by its straight-line travel (+1 u of
            // post-dash drift slack) in ANY direction — including straight up.
            bool inDashReach = dashRange > 0f && toSelf <= dashRange + 1f;
            if (inDashReach || EstimateReachable(world, self, point))
            {
                bool ahead = !directional || (point.X - self.Position.X) * vx >= 0f;
                if (ahead && toSelf < bestAhead)
                {
                    bestAhead = toSelf;
                    aheadPoint = point;
                    aheadPlatform = platform;
                }
                else if (!ahead && toSelf < bestBehind)
                {
                    bestBehind = toSelf;
                    behindPoint = point;
                    behindPlatform = platform;
                }
                reachable = true;
            }
        }
        if (reachable)
        {
            bool useAhead = bestAhead < float.PositiveInfinity;
            target = useAhead ? aheadPoint : behindPoint;
            chosenPlatform = useAhead ? aheadPlatform : behindPlatform;
        }
        if (!reachable)
        {
            target = fallback;
            chosenPlatform = fallbackPlatform;
        }
        return found;
    }

    /// <summary>
    /// Coarse ballistic feasibility — "within its current movement capability" (req 1):
    /// can MaxAirSpeed cover the horizontal gap in the time the character has before it
    /// falls past the target height, counting the hang time an unspent air jump buys?
    /// A target ABOVE the character requires an available jump that can gain the height.
    /// </summary>
    private static bool EstimateReachable(SimWorld world, SimPlayer self, Vec2 target)
    {
        float g = world.Config.Gravity * self.GravityScale;
        if (g <= 0f)
        {
            return true; // floaty degenerate genomes can drift anywhere
        }
        float dx = MathF.Abs(target.X - self.Position.X);
        float dy = target.Y - self.Position.Y;
        bool jumpAvailable = self.IsGrounded || !self.JumpsExhausted;
        float jumpForce = self.IsGrounded ? self.GroundJumpForce : self.AirJumpForce;

        if (dy > 0f)
        {
            // Must gain height: peak of an immediate jump = v²/2g (current vy if rising).
            float v = jumpAvailable ? jumpForce : MathF.Max(self.Velocity.Y, 0f);
            if (v * v / (2f * g) < dy)
            {
                return false;
            }
            return dx <= self.MaxAirSpeed * (2f * v / g); // full up-and-down arc budget
        }

        // Falling to (or past) the target height: time to descend |dy| from current vy,
        // plus the hang time of an unspent jump.
        float vy = self.Velocity.Y;
        float fall = (vy + MathF.Sqrt(vy * vy + 2f * g * -dy)) / g;
        if (jumpAvailable)
        {
            fall += jumpForce / g;
        }
        return dx <= self.MaxAirSpeed * fall;
    }
}
