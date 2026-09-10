using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Agents;

/// <summary>The behavior table and the stateless behavior classes — the half of
/// the agent that turns a built UtilityContext into channel scores. Split from
/// UtilityAgent.cs 2026-09-01 (pure text move; the shell, sensors, and channel
/// selection stay in UtilityAgent.cs).</summary>
public sealed partial class UtilityAgent
{
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
        new ZonerBehavior(),
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
            scores.Horizontal[UtilityScores.HNeutral] += BaselineNeutral;
            scores.Vertical[UtilityScores.VNeutral] += BaselineVerticalNeutral;
            scores.Jump[UtilityScores.NoJump] += BaselineNoJump;
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
            scores.Horizontal[ctx.RecoverTarget.X >= ctx.Self.Position.X
                ? UtilityScores.Right : UtilityScores.Left] += RecoverMove;
            bool jumpAvailable = ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted;
            if (jumpAvailable && ctx.RecoverTarget.Y - ctx.Self.Position.Y > -0.5f)
            {
                scores.Jump[UtilityScores.DoJump] += RecoverJump;
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
            scores.Horizontal[UtilityScores.Toward(ctx.FacingToOpponent)] += DoomedChase;
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
                scores.Horizontal[UtilityScores.Toward(dx)] += ApproachMax * MathF.Max(urgency, 0.4f);
            }

            bool jumpAvailable = ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted;
            // While a platform blocks the vertical route, jumping at the target just
            // bonks the underside — the flank behavior owns the route instead.
            bool targetAbove = ctx.FlankDirection == 0
                && ctx.AttackTarget.Y - ctx.Self.Position.Y > OpponentAboveThreshold;
            bool runningOffEdge = ctx.Self.IsGrounded
                && AgentGeometry.OverPit(ctx.World, ctx.Self, EdgeProbeDistance * ctx.FacingToOpponent);
            if (jumpAvailable && (targetAbove || runningOffEdge))
            {
                scores.Jump[UtilityScores.DoJump] += ApproachJump;
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
                scores.Horizontal[UtilityScores.Toward(dx)] += TraverseMove;
                return;
            }
            if (ctx.TraversalDrop)
            {
                // Drop route (2026-09-01, thin platforms): over the overlap, stay
                // planted and hold down — the crouch drop carries the hop.
                scores.Horizontal[UtilityScores.HNeutral] += TraverseMove;
                scores.Vertical[UtilityScores.Down] += TraverseJump;
                return;
            }
            // At the launch edge: commit to the hop.
            scores.Horizontal[UtilityScores.Toward(ctx.TraversalDirection)] += TraverseMove;
            if (ctx.TraversalNeedsJump && (ctx.Self.IsGrounded || !ctx.Self.JumpsExhausted))
            {
                scores.Jump[UtilityScores.DoJump] += TraverseJump;
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
            scores.Horizontal[UtilityScores.Toward(ctx.FlankDirection)] += weight;
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
    /// hittable — canHit already encodes the close-range gate, the RELEASE-MOMENT
    /// lead (2026-09-04), and the loose aim, so this behavior only prices the
    /// candidate — at PARITY with melee since 2026-09-04 (designer-directed,
    /// DEVIATIONS #35). A break-stunned opponent gets the half punish bonus (the
    /// full one belongs to melee, which actually confirms).</summary>
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

    /// <summary>Zoning stance (2026-09-04, designer-directed — DEVIATIONS #35): a
    /// character holding a fireable projectile plays RANGE instead of pure rushdown.
    /// Too close for the firing gate → back out (edge-safe via the shared retreat
    /// helper); inside the firing pocket → plant and let the attack channel shoot.
    /// Gates: never over a pit, never while vulnerable, and never during the
    /// opponent's break stun (that is the melee punish window — go in). Beyond the
    /// pocket the normal approach stack takes over, so zoners still close distance
    /// on runaways. O(moves) arithmetic, no allocation.</summary>
    private sealed class ZonerBehavior : IUtilityBehavior
    {
        public void Contribute(in UtilityContext ctx, UtilityScores scores)
        {
            if (ctx.OverPit || ctx.Vulnerable || ctx.OpponentBreakStunned)
            {
                return;
            }
            bool armed = false;
            for (int m = 0; m < ctx.Self.Moves.Count; m++)
            {
                if (ctx.Self.ButtonForMove(m) >= 0 && ctx.Self.ProjectileMoves[m] is not null)
                {
                    armed = true;
                    break;
                }
            }
            if (!armed || ctx.Distance > ZonerMaxRange)
            {
                return;
            }
            if (ctx.Distance < ZonerRetreatRange)
            {
                scores.Horizontal[UtilityScores.Toward(
                    SafeRetreatDirection(ctx, requireGrounded: true))] += ZonerRetreat;
                return;
            }
            scores.Horizontal[UtilityScores.HNeutral] += ZonerHold;
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
                && ctx.Opponent.Position.Y < self.Position.Y - PursuitVerticalGap
                && MathF.Abs(ctx.Opponent.Position.X - self.Position.X) < PursuitColumnWidth)
            {
                scores.Vertical[UtilityScores.Down] += FastFallPursuit;
            }
            // A crouch on a thin platform with nothing below turns into a suicide
            // drop once the delay elapses — the crouch utilities gate on safety
            // (2026-09-01; thin-free stages see the exact pre-feature conditions).
            bool crouchSafe = !ctx.OnThinPlatform || ctx.CanDropSafely;
            // Crouch braking: sliding dangerously fast at high damage with a braking gene.
            if (self.IsGrounded && self.State == PlayerState.Idle && crouchSafe
                && self.CrouchAcceleration < 0f && self.Damage >= HighDamageThreshold
                && MathF.Abs(self.Velocity.X) > self.MaxGroundSpeed)
            {
                scores.Vertical[UtilityScores.Down] += CrouchBrake;
            }
            // Crouch-slide approach: a speed-boosting gene and a distant opponent.
            if (self.IsGrounded && self.State == PlayerState.Idle && crouchSafe
                && self.CrouchAcceleration > 0f && !ctx.TelegraphThreat
                && ctx.Distance > DashApproachRange)
            {
                scores.Vertical[UtilityScores.Down] += CrouchSlideApproach;
            }
            // Drop pursuit (2026-09-01, thin platforms): the opponent is below the
            // thin floor under our feet and roughly under us — drop onto them (the
            // grounded sibling of the fast-fall pursuit; also the descending-flank
            // answer: the "blocking" platform is the one we stand on).
            if (ctx.CanDropSafely && !ctx.Vulnerable
                && ctx.Opponent.Position.Y < self.Position.Y - PursuitVerticalGap
                && MathF.Abs(ctx.Opponent.Position.X - self.Position.X) < PursuitColumnWidth)
            {
                scores.Vertical[UtilityScores.Down] += DropPursuit;
            }
            // DI pre-positioning: about to be hit (or being juggled) → hold and up.
            // Percent-aware since 2026-09-10 (DEVIATIONS #37): at low damage there is
            // no kill risk and the always-toward-center hold FED stun chains (mid-
            // stage, the attacker usually IS center-ward — the victim's own DI pulled
            // it back into the chain), so the low-damage hold breaks adjacency by
            // pointing AWAY from the attacker. Past HighDamageThreshold the survival
            // hold toward the farthest blast line (stage center) takes over, as before.
            if (self.DirectionalInfluence > 0f
                && (ctx.TelegraphThreat || ctx.UnderThreat || self.State == PlayerState.Stun))
            {
                float holdDirection = self.Damage < HighDamageThreshold
                    ? -ctx.FacingToOpponent
                    : TowardStageCenterX(self);
                scores.Horizontal[UtilityScores.Toward(holdDirection)] += DIHold;
                scores.Vertical[UtilityScores.Up] += DIHoldVertical;
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
            int away = SafeRetreatDirection(in ctx, requireGrounded: true);
            float scale = MathF.Min(ctx.Self.Damage / HighDamageThreshold, 2f);
            scores.Horizontal[UtilityScores.Toward(away)] += EvadeMove * scale;
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
            int away = SafeRetreatDirection(in ctx, requireGrounded: true);
            scores.Horizontal[UtilityScores.Toward(away)] += ThreatDodgeMove;
            // Hop away only when GROUNDED (2026-07-22, DEVIATIONS #28): a flinch-dodge
            // is a cheap ground hop. Spending the AIR jump to flinch mid-air was the
            // large-map oscillation bug — the agent burned its second jump dodging
            // while airborne (which happens constantly on wide/tall maps), stranding
            // itself in AirJumpsExhausted where it can neither attack nor jump, so it
            // drifted, landed, re-approached, and dodged again forever. The air jump is
            // reserved for recovery and traversal; airborne dodges use lateral drift.
            if (ctx.Self.IsGrounded)
            {
                scores.Jump[UtilityScores.DoJump] += ThreatDodgeJump;
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
            int away = SafeRetreatDirection(in ctx, requireGrounded: false);
            scores.Horizontal[UtilityScores.Toward(away)] += ExhaustedRetreat;
            // Thin platforms (2026-09-01): dropping through the floor is a second
            // disengage route — scored, not forced; the vertical channel arbitrates.
            if (ctx.CanDropSafely)
            {
                scores.Vertical[UtilityScores.Down] += ExhaustedRetreat;
            }
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
            scores.Horizontal[UtilityScores.Toward(-ctx.FacingToOpponent)] += SpacingMove;
        }
    }
}
