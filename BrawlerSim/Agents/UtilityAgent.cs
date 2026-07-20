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
public sealed class UtilityAgent : IInputSource
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

    // Projectiles (2026-07-14, FEATURES.md §Projectiles agent spec). Fire-from-range
    // sits BELOW AttackInRange so melee stays preferred when both connect ("avoid
    // using projectiles at close range" is the hard gate; this is the soft one), and
    // the weight is deliberately moderate — zoning findable, not agent-forced.
    private const float ProjectileInRange = 2.6f;
    private const float ProjectileDamagePreference = 0.04f;
    private const float MinProjectileRange = 2.5f;      // the close-range gate
    private const float ProjectileCorridorSlack = 0.6f; // vertical looseness of the aim test
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
        SimPlayer opponent = world.Players[1 - playerIndex];
        _graph ??= new PlatformGraph(world.Platforms, self, world.Config.Gravity);

        if (self.State == PlayerState.Shield)
        {
            return ManageRaisedShield(self, opponent);
        }
        _heldShieldHold = true; // a future raise starts committed to holding

        if (self.State == PlayerState.Dash)
        {
            // Steer the held direction toward the dash intent during warm-up; travel
            // ignores inputs anyway. The intent WAS the decision — no re-rolls.
            return new InputFrame(_dashIntentH, _dashIntentV, false, 0);
        }

        UtilityContext ctx = BuildContext(world, self, opponent, _graph);

        if (ctx.OverPit)
        {
            self.RecoveryTicks++; // research stat, counted per tick like the DT did
        }

        bool salient =
            (self.State == PlayerState.Stun && !_wasStunned) ||
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

        int moveChoice = Select(scores.Horizontal);          // 0 left, 1 neutral, 2 right
        int verticalChoice = Select(scores.Vertical);        // 0 down, 1 neutral, 2 up
        int jumpChoice = Select(scores.Jump);                // 0 no, 1 yes
        int attackChoice = Select(scores.Attack);            // 0 none, else candidate

        // Defense channel (2026-07-13, replaces the pairwise dodge/shield coin): on a
        // telegraphed swing with no counter-hit available — trade-commit: landing OUR
        // melee interrupts THEIRS — ONE weighted-random pick among the defense
        // options. An incoming projectile (2026-07-14) triggers defense REGARDLESS of
        // counter-hit options: the bolt is committed damage already in flight, and
        // counter-firing does nothing to stop it (unlike the melee trade).
        if ((ctx.TelegraphThreat && !ctx.AnyCanHit || ctx.ProjectileThreat)
            && ctx.Self.State is not (PlayerState.WarmUp or PlayerState.Attack))
        {
            bool jumpAvailable = ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted;
            float shieldMargin = MathF.Max(0f,
                (ctx.ShieldHealthFraction - ShieldReleaseHealthFraction) / (1f - ShieldReleaseHealthFraction));
            bool shieldUsable = ctx.Self.State == PlayerState.Idle && shieldMargin > 0f && !ctx.OverPit;
            bool airborne = !ctx.Self.IsGrounded;
            bool fastFallVulnState = ctx.Self.State is PlayerState.WarmUp
                or PlayerState.CoolDown or PlayerState.AirJumpsExhausted;
            _defenseScores[0] = DefenseNone;
            _defenseScores[1] = jumpAvailable ? DefenseJump : 0f;
            _defenseScores[2] = shieldUsable ? DefenseShield * shieldMargin : 0f;
            _defenseScores[3] = ctx.DashUsable ? DefenseDash : 0f;
            _defenseScores[4] = airborne && ctx.Self.FastFallAcceleration > 0f
                ? (fastFallVulnState ? DefenseFastFallVulnerable : DefenseFastFall) : 0f;
            _defenseScores[5] = ctx.CrouchClearsThreat ? DefenseCrouch : 0f;
            int defense = Select(_defenseScores);
            int away = -ctx.FacingToOpponent;
            switch (defense)
            {
                case 1:
                    jumpChoice = 1;
                    moveChoice = away > 0 ? 2 : 0;
                    attackChoice = 0;
                    break;
                case 2:
                    jumpChoice = 0;
                    attackChoice = ShieldCandidate(scores, ctx.Self);
                    break;
                case 3:
                    jumpChoice = 0;
                    attackChoice = DashCandidate(scores, ctx.DashSlot);
                    break;
                case 4: // fast fall out of the arc
                    jumpChoice = 0;
                    attackChoice = 0;
                    verticalChoice = 0;
                    break;
                case 5: // duck under it
                    jumpChoice = 0;
                    attackChoice = 0;
                    verticalChoice = 0;
                    moveChoice = 1; // stay planted; the FSM enters Crouch from Idle+down
                    break;
            }
        }

        // A selected dash press locks in its intent direction (held from the press
        // itself and steered through warm-up): recovery → the landing aim above the
        // platform; threatened → away; otherwise → the opponent.
        bool dashChosen = attackChoice > 0
            && ctx.Self.MoveTypeAt(scores.AttackMoves[attackChoice]) == Genome.MoveType.Dash;
        if (dashChosen)
        {
            Vec2 target = ctx.OverPit && ctx.RecoverTargetValid ? ctx.RecoverAim - ctx.Self.Position
                : ctx.TelegraphThreat ? new Vec2(-ctx.FacingToOpponent, 0f)
                : ctx.Opponent.Position - ctx.Self.Position;
            _dashIntentH = MathF.Sign(target.X);
            _dashIntentV = MathF.Sign(target.Y);
            if (ctx.OverPit && _dashIntentV < 0f)
            {
                _dashIntentV = 0f; // a recovery dash never points downward (designer)
            }
        }

        _heldHorizontal = dashChosen ? _dashIntentH : moveChoice - 1;
        _heldVertical = dashChosen ? _dashIntentV : verticalChoice - 1;
        byte actions = attackChoice > 0
            ? InputFrame.ActionBit(scores.AttackButtons[attackChoice])
            : (byte)0;
        return new InputFrame(_heldHorizontal, _heldVertical, jumpChoice == 1, actions);
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

    private readonly float[] _defenseScores = new float[6];
    private float _dashIntentH;
    private float _dashIntentV;

    /// <summary>
    /// Shield management (2026-07-12 humanization, designer-directed): hold/release
    /// and aim go through the SAME imperfection machinery as everything else — the
    /// commitment window delays reactions and the randomness mixture makes release
    /// timing fallible — so evolution cannot tune shield timings against
    /// frame-perfect execution no human could deliver. The one deterministic override:
    /// health at/below the release threshold always releases (the circle is visibly
    /// red — even humans don't miss that).
    /// </summary>
    private InputFrame ManageRaisedShield(SimPlayer self, SimPlayer opponent)
    {
        float healthFraction = ShieldHealthFractionOf(self);
        if (healthFraction <= ShieldReleaseHealthFraction)
        {
            _heldShieldHold = false; // forced release — never ride into the break
        }
        else if (--_ticksUntilRedecide <= 0)
        {
            _ticksUntilRedecide = _config.DecisionIntervalTicks;
            bool threatened = opponent.State is PlayerState.WarmUp or PlayerState.Attack
                || (opponent.Position - self.Position).Length() <= ShieldHoldRange;
            _shieldScores[0] = ShieldReleaseBaseline;
            _shieldScores[1] = threatened ? ShieldHoldUtility : ShieldUnthreatenedHold;
            _heldShieldHold = Select(_shieldScores) == 1;

            _heldAimH = 0f;
            _heldAimV = 0f;
            if (_heldShieldHold && self.ShieldRadius < MathF.Max(self.BodyHalf.X, self.BodyHalf.Y) * 2f)
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

    private readonly float[] _shieldScores = new float[2];
    private bool _heldShieldHold = true; // entering Shield implies the raise decision
    private float _heldAimH;
    private float _heldAimV;

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

    // ── Context ────────────────────────────────────────────────────────────────

    private static UtilityContext BuildContext(SimWorld world, SimPlayer self, SimPlayer opponent, PlatformGraph graph)
    {
        // Dash availability first — recovery reachability must credit a usable dash
        // (2026-07-13 playtest fix: with jumps spent, the dash IS the way back up).
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

        bool overPit = OverPit(world, self, 0f);
        Vec2 recoverTarget = Vec2.Zero;
        Aabb recoverPlatform = default;
        bool targetSensed = false, reachable = false;
        if (overPit)
        {
            // Directional recovery (2026-07-10 traversal fix): among REACHABLE sensed
            // platforms, prefer the one closest to the OPPONENT — mid-hop recovery
            // continues the chase instead of pulling back to the platform just left.
            targetSensed = TrySensedRecoverTarget(world, self, opponent, dashRange,
                out recoverTarget, out recoverPlatform, out reachable);
        }

        int facingToOpponent = opponent.Position.X >= self.Position.X ? 1 : -1;
        var canHit = new bool[self.Moves.Count];
        bool anyCanHit = false;
        Vec2 attackTarget = opponent.Position;
        float bestTravel = float.PositiveInfinity;
        for (int m = 0; m < self.Moves.Count; m++)
        {
            if (self.ButtonForMove(m) < 0 || self.Moves[m] is not SimMove move)
            {
                continue; // shield slots have no hitbox to reach with
            }
            // Reach test with facing toward the opponent — turning is a same-tick input.
            Vec2 offset = new(move.Offset.X * facingToOpponent, move.Offset.Y);
            var hitbox = new Aabb(
                self.Position + offset,
                new Vec2(move.BaseHalf.X * self.WidthScalar, move.BaseHalf.Y * self.HeightScalar));
            canHit[m] = hitbox.Overlaps(opponent.Body);
            anyCanHit |= canHit[m];

            // The position to fight FROM: stand where this move's hitbox lands on the
            // opponent (the DT's relMove×1.2 chase, generalized per move). A downward
            // move makes the agent seek height above the opponent — the hop-over
            // corridor dance the paper observed emerges from the genome, not the code.
            Vec2 candidate = opponent.Position - offset * 1.2f;
            float travel = (candidate - self.Position).Length();
            if (travel < bestTravel)
            {
                bestTravel = travel;
                attackTarget = candidate;
            }
        }

        // Projectile reach (2026-07-14): a LOOSE corridor prediction per the spec —
        // horizontal distance within the closed-form range, vertical offset within the
        // path's lateral envelope (+slack), and outside the close-range gate. The
        // candidate then scores on the attack channel via ProjectileBehavior.
        for (int m = 0; m < self.Moves.Count; m++)
        {
            if (self.ButtonForMove(m) < 0 || self.ProjectileMoves[m] is not SimProjectileMove ranged)
            {
                continue;
            }
            canHit[m] = ProjectileCorridorHit(ranged, self, opponent, world.Config);
            anyCanHit |= canHit[m];
        }

        // Telegraph: the opponent is WINDING UP an attack whose arc (+margin) covers
        // us — the readable moment defensive options respond to.
        bool telegraphThreat = false;
        bool crouchClearsThreat = false;
        if (opponent.State == PlayerState.WarmUp
            && opponent.Moves[opponent.CurrentMoveIndex] is SimMove windingUp)
        {
            var arc = new Aabb(
                opponent.Position + new Vec2(windingUp.Offset.X * opponent.Facing, windingUp.Offset.Y),
                new Vec2(
                    windingUp.BaseHalf.X * opponent.WidthScalar + TelegraphDodgeMargin,
                    windingUp.BaseHalf.Y * opponent.HeightScalar + TelegraphDodgeMargin));
            telegraphThreat = arc.Overlaps(self.Body);
            // Ducking helps only if the CROUCHED hurtbox top would clear the arc's
            // bottom edge (grounded, from Idle, feet planted).
            if (telegraphThreat && self.IsGrounded && self.State == PlayerState.Idle)
            {
                float feetY = self.Position.Y - self.BodyHalf.Y;
                float crouchedTop = feetY + 2f * self.BodyHalf.Y * self.CrouchHeightRatio;
                crouchClearsThreat = arc.Bottom > crouchedTop + 0.05f;
            }
        }
        // A winding-up PROJECTILE telegraphs exactly like melee (2026-07-20, designer:
        // warm-up phases signal defensive counterplay across the board). The "arc" is
        // the shot's predicted corridor at our column — the same loose test the shooter
        // aimed with, seen from the receiving end. This is what makes shields viable
        // against zoners: the defender now has warm-up + flight time, not just the
        // last half-second of flight. Trade-commit still applies (interrupting the
        // shooter during warm-up cancels the shot, exactly like a melee trade).
        else if (opponent.State == PlayerState.WarmUp
            && opponent.ProjectileMoves[opponent.CurrentMoveIndex] is SimProjectileMove windingShot)
        {
            telegraphThreat = ProjectileCorridorHit(windingShot, opponent, self, world.Config);
            if (telegraphThreat && self.IsGrounded && self.State == PlayerState.Idle)
            {
                // Duck only if the corridor's center (at our column, launch height +
                // path drop) passes above the crouched silhouette by the bolt's half
                // extent — same geometry rule as the melee arc bottom.
                float corridorBottom = ProjectileCorridorCenterY(
                    windingShot, opponent, self, world.Config) - windingShot.HalfExtent;
                float feetY = self.Position.Y - self.BodyHalf.Y;
                float crouchedTop = feetY + 2f * self.BodyHalf.Y * self.CrouchHeightRatio;
                crouchClearsThreat = corridorBottom > crouchedTop + 0.05f;
            }
        }

        // Incoming projectiles (2026-07-14): sample each dangerous projectile's
        // closed-form path over the lookahead; a predicted overlap with our body
        // (inflated by its half extent) is a threat the defense channel answers.
        // Ducking helps only if every threatening sample passes above the crouched
        // silhouette — same geometry rule as the melee arc.
        bool projectileThreat = false;
        if (world.Projectiles.Count > 0)
        {
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
                        incoming.Origin, incoming.Facing, incoming.AgeTicks + k, world.Config);
                    if (predicted.X >= threatBox.Left && predicted.X <= threatBox.Right
                        && predicted.Y >= threatBox.Bottom && predicted.Y <= threatBox.Top)
                    {
                        projectileThreat = true;
                        minPredictedBottom = MathF.Min(
                            minPredictedBottom, predicted.Y - incoming.Move.HalfExtent);
                    }
                }
            }
            if (projectileThreat && self.IsGrounded && self.State == PlayerState.Idle)
            {
                float feetY = self.Position.Y - self.BodyHalf.Y;
                float crouchedTop = feetY + 2f * self.BodyHalf.Y * self.CrouchHeightRatio;
                crouchClearsThreat |= minPredictedBottom > crouchedTop + 0.05f;
            }
        }

        // Threat: can any of the OPPONENT's moves reach me right now (their facing
        // toward me)? Humans see the incoming swing arc and leave it.
        bool underThreat = false;
        int opponentFacing = -facingToOpponent;
        for (int m = 0; m < opponent.Moves.Count && !underThreat; m++)
        {
            if (opponent.ButtonForMove(m) < 0 || opponent.Moves[m] is not SimMove move)
            {
                continue;
            }
            var theirHitbox = new Aabb(
                opponent.Position + new Vec2(move.Offset.X * opponentFacing, move.Offset.Y),
                new Vec2(move.BaseHalf.X * opponent.WidthScalar, move.BaseHalf.Y * opponent.HeightScalar));
            underThreat = theirHitbox.Overlaps(self.Body);
        }

        // Vulnerable = cannot attack (CoolDown / AirJumpsExhausted) — a dash in hand
        // does NOT re-enable chasing (2026-07-13 playtest fix); it stays available
        // for recovery and the defense channel.
        bool vulnerable = self.State is PlayerState.CoolDown or PlayerState.AirJumpsExhausted;
        // The recovery LANDING AIM: above the platform top, x clamped to its span —
        // "get above the platform before floating/jumping onto it" (designer).
        Vec2 recoverAim = targetSensed
            ? new Vec2(
                MathF.Min(MathF.Max(self.Position.X, recoverPlatform.Left), recoverPlatform.Right),
                recoverPlatform.Top + RecoveryAimClearance)
            : Vec2.Zero;

        (int flankDirection, bool flankSafe) = ComputeFlank(world, self, opponent);

        // Traversal: next hop toward the opponent's platform via the per-match graph.
        bool hasTraversal = false;
        Vec2 traversalLaunch = Vec2.Zero;
        int traversalDirection = 0;
        bool traversalNeedsJump = false;
        int myPlatform = graph.PlatformAt(self.Position);
        int theirPlatform = graph.PlatformAt(opponent.Position);
        if (myPlatform >= 0 && theirPlatform >= 0 && myPlatform != theirPlatform
            && graph.TryRoute(myPlatform, theirPlatform, out int nextPlatform))
        {
            Aabb mine = graph.Platform(myPlatform);
            Aabb next = graph.Platform(nextPlatform);
            float launchX = next.Center.X >= mine.Center.X ? mine.Right : mine.Left;
            hasTraversal = true;
            traversalLaunch = new Vec2(launchX, mine.Top);
            traversalDirection = next.Center.X >= mine.Center.X ? 1 : -1;
            traversalNeedsJump = next.Top >= mine.Top - 0.5f;
        }

        return new UtilityContext(
            world, self, opponent, overPit,
            Doomed: overPit && !reachable,
            recoverTarget, targetSensed && reachable,
            Distance: (opponent.Position - self.Position).Length(),
            canHit, anyCanHit, facingToOpponent, attackTarget, underThreat,
            flankDirection, flankSafe,
            hasTraversal, traversalLaunch, traversalDirection, traversalNeedsJump,
            vulnerable,
            ShieldHealthFractionOf(self),
            OpponentBreakStunned: opponent.State == PlayerState.Stun && opponent.StunFromShieldBreak,
            telegraphThreat,
            dashUsable,
            dashSlot,
            OpponentStunned: opponent.State == PlayerState.Stun,
            recoverAim,
            crouchClearsThreat,
            projectileThreat);
    }

    /// <summary>
    /// The spec's "loose range of hits based on projectile shape": facing-toward
    /// reach out to the closed-form range (accounting for deceleration peaking early),
    /// vertical tolerance = the path's lateral envelope + the sine amplitude + slack +
    /// the target's half height, gated closed inside MinProjectileRange. Deliberately
    /// coarse — precision comes from the sim, misses are the humanizing noise.
    /// </summary>
    private static bool ProjectileCorridorHit(
        SimProjectileMove ranged, SimPlayer shooter, SimPlayer target, MatchConfig config)
    {
        float dx = MathF.Abs(target.Position.X - shooter.Position.X);
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
        float centerY = ProjectileCorridorCenterY(ranged, shooter, target, config);
        float tolerance = ProjectileCorridorSlack + target.BodyHalf.Y
            + (ranged.Path == ProjectilePath.Sine ? ranged.SineAmplitude : 0f);
        return MathF.Abs(target.Position.Y - centerY) <= tolerance;
    }

    /// <summary>Where the shot's path sits vertically when it reaches the target's
    /// column (loose: time from launch speed alone). Shared by the shooter's aim test
    /// and the defender's wind-up telegraph (2026-07-20).</summary>
    private static float ProjectileCorridorCenterY(
        SimProjectileMove ranged, SimPlayer shooter, SimPlayer target, MatchConfig config)
    {
        float dx = MathF.Abs(target.Position.X - shooter.Position.X);
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
            bool leftSafe = !OverPit(world, self, platform.Left - FlankEdgeProbe - self.Position.X);
            bool rightSafe = !OverPit(world, self, platform.Right + FlankEdgeProbe - self.Position.X);
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

    /// <summary>No platform anywhere below the sample point (same test as the DT used).</summary>
    private static bool OverPit(SimWorld world, SimPlayer self, float xOffset)
    {
        float x = self.Position.X + xOffset;
        foreach (Aabb platform in world.Platforms)
        {
            if (x >= platform.Left && x <= platform.Right && platform.Top <= self.Position.Y)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Recovery target among sensed platforms: the REACHABLE one whose
    /// closest point is nearest to the opponent (chase-preserving); when none is
    /// reachable, the nearest-to-self sensed point (the Doomed check's subject).</summary>
    private static bool TrySensedRecoverTarget(
        SimWorld world, SimPlayer self, SimPlayer opponent, float dashRange,
        out Vec2 target, out Aabb chosenPlatform, out bool reachable)
    {
        var sense = new Aabb(
            self.Position,
            new Vec2(world.Config.PlatformSenseHalfWidth, world.Config.PlatformSenseHalfHeight));
        target = Vec2.Zero;
        chosenPlatform = default;
        reachable = false;
        bool found = false;
        float bestReachable = float.PositiveInfinity, bestFallback = float.PositiveInfinity;
        Vec2 fallback = Vec2.Zero;
        Aabb fallbackPlatform = default;
        foreach (Aabb platform in world.Platforms)
        {
            if (!sense.Overlaps(platform))
            {
                continue;
            }
            Vec2 point = platform.ClosestPoint(self.Position);
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
                float toOpponent = (point - opponent.Position).Length();
                if (toOpponent < bestReachable)
                {
                    bestReachable = toOpponent;
                    target = point;
                    chosenPlatform = platform;
                    reachable = true;
                }
            }
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

    // ── Behaviors (fixed order — extensibility point for shield/dash/projectile) ──

    private static readonly IUtilityBehavior[] Behaviors =
    {
        new BaselineBehavior(),
        new RecoverBehavior(),
        new DoomedBehavior(),
        new ApproachBehavior(),
        new TraverseBehavior(),
        new FlankBehavior(),
        new AttackBehavior(),
        new ProjectileBehavior(),
        new DashUtilityBehavior(),
        new VerticalUtilityBehavior(),
        new EvadeBehavior(),
        new ThreatDodgeBehavior(),
        new ExhaustedCautionBehavior(),
        new SpacingBehavior(),
    };

    /// <summary>
    /// One appraisal: reads the context, adds non-negative utility to any channel.
    /// Behaviors are stateless — all state lives in the sim or the agent shell.
    /// </summary>
    public interface IUtilityBehavior
    {
        void Contribute(in UtilityContext ctx, UtilityScores scores);
    }

    /// <summary>Keeps every channel's "do nothing" option live so normalization never
    /// divides by zero and inaction stays selectable under randomness.</summary>
    private sealed class BaselineBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            scores.Horizontal[1] += BaselineNeutral;
            scores.Vertical[1] += BaselineVerticalNeutral;
            scores.Jump[0] += BaselineNoJump;
            scores.Attack[0] += BaselineNoAttack;
        }
    }

    /// <summary>Req 1a: over a pit with a reachable platform → move toward it; jump to
    /// gain or keep height unless the platform is comfortably below.</summary>
    private sealed class RecoverBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (!ctx.OverPit || !ctx.RecoverTargetValid)
            {
                return;
            }
            scores.Horizontal[ctx.RecoverTarget.X >= ctx.Self.Position.X ? 2 : 0] += RecoverMove;
            bool jumpAvailable = ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted;
            if (jumpAvailable && ctx.RecoverTarget.Y - ctx.Self.Position.Y > -0.5f)
            {
                scores.Jump[1] += RecoverJump;
            }
        }
    }

    /// <summary>Req 1b: over a pit with nothing reachable → spend the remaining ticks
    /// chasing and swinging at the opponent.</summary>
    private sealed class DoomedBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (!ctx.Doomed)
            {
                return;
            }
            scores.Horizontal[ctx.FacingToOpponent > 0 ? 2 : 0] += DoomedChase;
            for (int c = 1; c < scores.Attack.Length; c++)
            {
                if (ctx.CanHit[scores.AttackMoves[c]])
                {
                    scores.Attack[c] += DoomedAttack;
                }
            }
        }
    }

    /// <summary>Req 2: close in on the ATTACK position (where the best move's hitbox
    /// lands on the opponent), not the opponent's body. Jump when that position is
    /// meaningfully above (RELATIVE — the DT's absolute-y quirk is deliberately not
    /// carried over) or when running off a grounded edge mid-chase.</summary>
    private sealed class ApproachBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (ctx.OverPit || ctx.Vulnerable)
            {
                return; // recovery/doomed own the off-stage story; vulnerable disengages
            }
            float dx = ctx.AttackTarget.X - ctx.Self.Position.X;
            float urgency = MathF.Min(ctx.Distance / ApproachDistanceScale, 1f);
            if (MathF.Abs(dx) > 0.1f)
            {
                scores.Horizontal[dx > 0f ? 2 : 0] += ApproachMax * MathF.Max(urgency, 0.4f);
            }

            bool jumpAvailable = ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted;
            // While a platform blocks the vertical route, jumping at the target just
            // bonks the underside — the flank behavior owns the route instead.
            bool targetAbove = ctx.FlankDirection == 0
                && ctx.AttackTarget.Y - ctx.Self.Position.Y > OpponentAboveThreshold;
            bool runningOffEdge = ctx.Self.IsGrounded
                && OverPit(ctx.World, ctx.Self, EdgeProbeDistance * ctx.FacingToOpponent);
            if (jumpAvailable && (targetAbove || runningOffEdge))
            {
                scores.Jump[1] += ApproachJump;
            }
        }
    }

    /// <summary>Different platforms → follow the per-match next-hop route: walk to the
    /// launch edge, then hop toward the next platform (no jump for drop-downs).
    /// Designer's platform-graph design, 2026-07-10.</summary>
    private sealed class TraverseBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (!ctx.HasTraversal || ctx.OverPit || ctx.Vulnerable)
            {
                return;
            }
            float dx = ctx.TraversalLaunch.X - ctx.Self.Position.X;
            if (MathF.Abs(dx) > TraverseLaunchSlack)
            {
                scores.Horizontal[dx > 0f ? 2 : 0] += TraverseMove;
                return;
            }
            // At the launch edge: commit to the hop.
            scores.Horizontal[ctx.TraversalDirection > 0 ? 2 : 0] += TraverseMove;
            if (ctx.TraversalNeedsJump && (ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted))
            {
                scores.Jump[1] += TraverseJump;
            }
        }
    }

    /// <summary>Vertical separation blocked by a platform → head for its edge (the
    /// safe one when only one has ground beyond it) instead of pacing under/over the
    /// opponent. Designer-reported stall, 2026-07-10.</summary>
    private sealed class FlankBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (ctx.FlankDirection == 0 || ctx.OverPit || ctx.Vulnerable)
            {
                return;
            }
            float weight = FlankMove * (ctx.FlankSafe ? 1f : FlankUnsafeScale);
            scores.Horizontal[ctx.FlankDirection > 0 ? 2 : 0] += weight;
        }
    }

    /// <summary>Req 3 + second-move update (2026-07-10): every move whose hitbox
    /// reaches the opponent scores its button, ranked by DAMAGE — the strongest move
    /// that can currently hit wins the channel (argmax; ties → lower index). The
    /// damage bonus stays below the in-range base so "some hit" always beats "none".</summary>
    private sealed class AttackBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (ctx.Vulnerable)
            {
                return; // the FSM ignores attacks here — don't press dead buttons
            }
            for (int c = 1; c < scores.Attack.Length; c++)
            {
                int move = scores.AttackMoves[c];
                if (ctx.CanHit[move] && ctx.Self.Moves[move] is SimMove attack)
                {
                    // A break-stunned opponent is the punish window: strongly prefer
                    // the most POWERFUL move (FEATURES.md agent spec).
                    float damagePreference = ctx.OpponentBreakStunned
                        ? BreakPunishDamagePreference : AttackDamagePreference;
                    float bonus = ctx.OpponentBreakStunned ? BreakPunishBonus : 0f;
                    scores.Attack[c] += AttackInRange + bonus + damagePreference * attack.DamageGiven;
                }
            }
        }
    }

    /// <summary>Projectile firing (2026-07-14, FEATURES.md §Projectiles agent spec):
    /// scores any projectile slot whose corridor test says the opponent is plausibly
    /// hittable — canHit already encodes both the close-range gate and the loose aim,
    /// so this behavior only prices the candidate. A break-stunned opponent gets the
    /// half punish bonus (the full one belongs to melee, which actually confirms).</summary>
    private sealed class ProjectileBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (ctx.Vulnerable)
            {
                return;
            }
            for (int c = 1; c < scores.Attack.Length; c++)
            {
                int move = scores.AttackMoves[c];
                if (ctx.CanHit[move] && ctx.Self.ProjectileMoves[move] is SimProjectileMove ranged)
                {
                    float bonus = ctx.OpponentBreakStunned ? BreakPunishBonus * 0.5f : 0f;
                    scores.Attack[c] += ProjectileInRange + bonus
                        + ProjectileDamagePreference * ranged.DamageGiven;
                }
            }
        }
    }

    /// <summary>Non-defense dash uses (2026-07-13): recovery over a pit (the dash is
    /// the premier third air action), approach from range, and stun punish — each a
    /// candidate on the action channel, arbitrated by normal channel selection.</summary>
    private sealed class DashUtilityBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (!ctx.DashUsable)
            {
                return;
            }
            float utility = 0f;
            if (ctx.Vulnerable && !ctx.OverPit)
            {
                return; // no chase-dashes while unable to attack; recovery still runs
            }
            if (ctx.OverPit && ctx.RecoverTargetValid)
            {
                // Playtest fix (2026-07-13): the recovery dash exists to gain HEIGHT
                // (or cross a large gap) — falling onto the platform from above with a
                // small gap doesn't spend it.
                bool needsHeight = ctx.RecoverAim.Y > ctx.Self.Position.Y;
                bool bigGap = MathF.Abs(ctx.RecoverAim.X - ctx.Self.Position.X) > DashRecoverHorizontalGap;
                if (needsHeight || bigGap)
                {
                    utility = DashRecover;
                }
            }
            else if (ctx.OpponentStunned && ctx.Distance > SpacingDistance)
            {
                utility = DashPunish;
            }
            else if (!ctx.OverPit && !ctx.TelegraphThreat && ctx.Distance > DashApproachRange
                && !ctx.HasTraversal)
            {
                utility = DashApproach;
            }
            if (utility <= 0f)
            {
                return;
            }
            for (int c = 1; c < scores.Attack.Length; c++)
            {
                if (scores.AttackMoves[c] == ctx.DashSlot)
                {
                    scores.Attack[c] += utility;
                    return;
                }
            }
        }
    }

    /// <summary>2026-07-13 fast fall / crouch / DI, all on the vertical (and DI also
    /// the horizontal) channel: drop onto an opponent below; crouch-brake a deadly
    /// ground slide (negative crouch accel); crouch-slide toward a far opponent
    /// (positive accel); and pre-position the held direction toward safety when a
    /// hit is coming or landing — DI reads whatever is held at the hit instant, so
    /// the commitment window supplies exactly the imperfection the spec demands.</summary>
    private sealed class VerticalUtilityBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            SimPlayer self = ctx.Self;
            // Fast-fall pursuit: airborne, opponent clearly below and roughly under us.
            if (!self.IsGrounded && self.FastFallAcceleration > 0f
                && ctx.Opponent.Position.Y < self.Position.Y - 1.5f
                && MathF.Abs(ctx.Opponent.Position.X - self.Position.X) < 2f)
            {
                scores.Vertical[0] += FastFallPursuit;
            }
            // Crouch braking: sliding dangerously fast at high damage with a braking gene.
            if (self.IsGrounded && self.State == PlayerState.Idle
                && self.CrouchAcceleration < 0f && self.Damage >= HighDamageThreshold
                && MathF.Abs(self.Velocity.X) > self.MaxGroundSpeed)
            {
                scores.Vertical[0] += CrouchBrake;
            }
            // Crouch-slide approach: a speed-boosting gene and a distant opponent.
            if (self.IsGrounded && self.State == PlayerState.Idle
                && self.CrouchAcceleration > 0f && !ctx.TelegraphThreat
                && ctx.Distance > DashApproachRange)
            {
                scores.Vertical[0] += CrouchSlideApproach;
            }
            // DI pre-positioning: about to be hit (or being juggled) → hold toward the
            // farthest blast line (stage center) and up.
            if (self.DirectionalInfluence > 0f
                && (ctx.TelegraphThreat || ctx.UnderThreat || self.State == PlayerState.Stun))
            {
                scores.Horizontal[self.Position.X >= 0f ? 0 : 2] += DIHold;
                scores.Vertical[2] += DIHoldVertical;
            }
        }
    }

    /// <summary>Req 4: at high damage, back away — harder the higher the damage (up to
    /// 2×) — but toward stage center when the retreat direction walks off the platform.
    /// Attacks stay live via AttackBehavior.</summary>
    private sealed class EvadeBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (ctx.Self.Damage < HighDamageThreshold || ctx.OverPit)
            {
                return;
            }
            int away = -ctx.FacingToOpponent;
            bool retreatFallsOff = ctx.Self.IsGrounded
                && OverPit(ctx.World, ctx.Self, EdgeProbeDistance * away);
            if (retreatFallsOff)
            {
                away = ctx.Self.Position.X >= 0f ? -1 : 1; // toward stage center
            }
            float scale = MathF.Min(ctx.Self.Damage / HighDamageThreshold, 2f);
            scores.Horizontal[away > 0 ? 2 : 0] += EvadeMove * scale;
        }
    }

    /// <summary>Humans don't stand inside the opponent's swing arc — unless they can
    /// swing back (then they commit to the trade, the hit-trading the paper observed).
    /// Dodge only when threatened WITHOUT a hit of our own available, and never
    /// mid-swing (WarmUp/Attack movement stays on target).</summary>
    private sealed class ThreatDodgeBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (!ctx.UnderThreat || ctx.OverPit || ctx.AnyCanHit
                || ctx.Self.State is PlayerState.WarmUp or PlayerState.Attack)
            {
                return;
            }
            int away = -ctx.FacingToOpponent;
            bool retreatFallsOff = ctx.Self.IsGrounded
                && OverPit(ctx.World, ctx.Self, EdgeProbeDistance * away);
            if (retreatFallsOff)
            {
                away = ctx.Self.Position.X >= 0f ? -1 : 1;
            }
            scores.Horizontal[away > 0 ? 2 : 0] += ThreatDodgeMove;
            if (ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted)
            {
                scores.Jump[1] += ThreatDodgeJump;
            }
        }
    }

    /// <summary>CoolDown and AirJumpsExhausted cannot attack, so proximity is pure
    /// exposure: drift away from a nearby opponent until capability returns
    /// (2026-07-10, generalized to both vulnerable states 2026-07-13 per designer
    /// playtest — a dash in hand does not re-enable the chase). Recovery still
    /// overrides over pits; Doomed is deliberately exempt (off-stage death, req 1b).</summary>
    private sealed class ExhaustedCautionBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (!ctx.Vulnerable || ctx.OverPit || ctx.Distance > ExhaustedCautionRange)
            {
                return;
            }
            int away = -ctx.FacingToOpponent;
            bool retreatFallsOff = OverPit(ctx.World, ctx.Self, EdgeProbeDistance * away);
            if (retreatFallsOff)
            {
                away = ctx.Self.Position.X >= 0f ? -1 : 1;
            }
            scores.Horizontal[away > 0 ? 2 : 0] += ExhaustedRetreat;
        }
    }

    /// <summary>Crowding without a hit available is dead time: back off to re-approach
    /// from an angle the attack target actually favors (breaks stacked stalemates).</summary>
    private sealed class SpacingBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (ctx.OverPit || ctx.AnyCanHit || ctx.Distance > SpacingDistance)
            {
                return;
            }
            scores.Horizontal[ctx.FacingToOpponent > 0 ? 0 : 2] += SpacingMove;
        }
    }
}

/// <summary>Everything a behavior may appraise, computed once per tick. AttackTarget is
/// the position to fight from — opponent minus the best move's mirrored hitbox offset.</summary>
public readonly record struct UtilityContext(
    SimWorld World,
    SimPlayer Self,
    SimPlayer Opponent,
    bool OverPit,
    bool Doomed,
    Vec2 RecoverTarget,
    bool RecoverTargetValid,
    float Distance,
    bool[] CanHit,
    bool AnyCanHit,
    int FacingToOpponent,
    Vec2 AttackTarget,
    bool UnderThreat,
    int FlankDirection,
    bool FlankSafe,
    bool HasTraversal,
    Vec2 TraversalLaunch,
    int TraversalDirection,
    bool TraversalNeedsJump,
    bool Vulnerable,
    float ShieldHealthFraction,
    bool OpponentBreakStunned,
    bool TelegraphThreat,
    bool DashUsable,
    int DashSlot,
    bool OpponentStunned,
    Vec2 RecoverAim,
    bool CrouchClearsThreat,
    bool ProjectileThreat);

/// <summary>
/// The per-decision score sheet. Horizontal = {left, neutral, right}; Jump = {no, yes};
/// Attack[0] = none, then one candidate per distinct usable move (lowest mapped button,
/// feature-1 convention). AttackMoves/AttackButtons map candidates back to moves/buttons.
/// </summary>
public sealed class UtilityScores
{
    public readonly float[] Horizontal = new float[3];
    public readonly float[] Vertical = new float[3]; // down, neutral, up (2026-07-13)
    public readonly float[] Jump = new float[2];
    public readonly float[] Attack;
    public readonly int[] AttackMoves;
    public readonly int[] AttackButtons;

    public UtilityScores(SimPlayer self)
    {
        var moves = new List<int>(self.Moves.Count);
        for (int m = 0; m < self.Moves.Count; m++)
        {
            if (self.ButtonForMove(m) >= 0)
            {
                moves.Add(m);
            }
        }
        Attack = new float[1 + moves.Count];
        AttackMoves = new int[1 + moves.Count];
        AttackButtons = new int[1 + moves.Count];
        for (int c = 0; c < moves.Count; c++)
        {
            AttackMoves[c + 1] = moves[c];
            AttackButtons[c + 1] = self.ButtonForMove(moves[c]);
        }
    }
}
