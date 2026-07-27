# Feature: Dash (Phase 6, feature 5)

Spec: FEATURES.md §Dash Mechanic. Clarifications recorded 2026-07-13:

- **Dash-into-opponent** (the spec's cut-off sentence): SOLID contact, but the
  velocity imparted to the opponent is capped at a low, damage-INDEPENDENT push —
  a dash can shove, never KO.
- **Physics**: gravity suspended during the travel phase — crisp straight-line 8-way
  dashes; upward/diagonal ground dashes reliably lift into the air.
- **Agent uses**: all four — recovery, evasion (joins the dodge/shield arbitration),
  approach, punish.
- **Tint**: orange. **Fitness**: stats-only, blind (shield watch-first pattern).

## Strategy (deltas on the shield playbook — same skeleton throughout)

1. **Genome**: `MoveType.Dash` + `DashParams` schema:
   | param | proposed range | notes |
   |---|---|---|
   | windUpDuration | 0.05–0.4 s | "fast short dashes or slow long ones"; no cool-down |
   | acceleration | 6–18 u/s | with gravity suspended this IS the travel speed, held for the duration (documented degeneracy) |
   | duration | 0.1–0.4 s | travel distance = speed × duration ≈ 0.6–7.2 u |
   | warmUpInvulnerable | 0–1, active ≥ 0.5 | bool-as-float so it rides the normal ParamSet crossover/mutation |
   | durationInvulnerable | 0–1, active ≥ 0.5 | same |
   Guaranteed 4th slot (`DashSlotCount = 1`, 0 to disable). **Button pinned:
   buttonMoves[3] = dash** (the designer's right-shoulder / L-key clamp) at
   generation/mutation/repair; moves 0–2 cover buttons 0–2. CONSEQUENCE flagged:
   with 4 moves on 4 buttons + the pin, the mapping gene is a fixed bijection until
   dynamic composition unpins it. Serialization: `"type": "dash"` — formatVersion
   3 → 4 (a v4-only enum value; v1–v3 load unchanged).
2. **Sim**: `PlayerState.Dash` with stages {WarmUp, Travel} (shield-style, hashed).
   Entry from Idle / Air / AirJumpsExhausted-with-dash-left; blocked from
   Stun/WarmUp/CoolDown/Shield/Dash. Direction captured at TRAVEL start from held
   axes (8-way; neutral → facing) — the second live consumer of InputFrame.Vertical.
   Travel: velocity = direction × acceleration, gravity skipped (SimPhysics checks a
   dash-travel flag), inputs ignored, direction locked. End: resolve by
   grounded/air-budget; no cool-down.
   **Air budget rework**: `AirJumpsUsed` (0–2) + `AirDashUsed` (bool), reset on
   grounding, replace the single JumpsExhausted flag internally (the public state
   names stay). All spec sequences (dash-jump-jump / jump-dash-jump / jump-jump-dash)
   fall out; full exhaustion = 2 jumps AND the dash spent. For dash-less legacy
   genomes this reduces EXACTLY to today's semantics — behavior preserved, goldens
   re-pin only for hash-format growth.
   **Invulnerability**: TryHit skips victims whose current dash stage has its invuln
   flag active (separate from post-hit InvincibleTicksLeft; skipped hits counted as
   DashInvulnDodges). **Contact cap**: `MatchConfig.DashContactPushCap` (~2 u/s)
   limits velocity imparted to the opponent while the mover is dash-traveling —
   damage plays no role.
3. **Stats**: Dashes, DashInvulnDodges (+ evaluate output). Fitness untouched.
4. **Agent**: dash candidate lives on the action channel (button 3); a dash
   "management mode" (like the shield's) steers held direction during warm-up toward
   the intent target:
   - Recovery: over a pit, platform reachable BY DASH (reachability estimate gains a
     dash-distance term) — especially as the last air action when jumps are spent;
   - Evasion: the telegraph arbitration becomes a proper 3-way weighted-random pick
     (jump-dodge / shield / dash-away) via one Select() over a defense-score array —
     refactors the pairwise health-weighted coin;
   - Approach: far + same platform → dash toward (suppressed when it would enter the
     telegraphed arc);
   - Punish: stunned (especially break-stunned) opponent → dash in.
   ExhaustedCaution re-keys on FULL exhaustion (jumps + dash). All new behaviors go
   through the standard window + randomness (no frame-perfect dash timing).
5. **View**: orange tint (palette updated); no sprite — the motion telegraphs itself.
6. **Tests** (~15): FSM entry/deny rules, warm-up/travel timing, direction capture
   incl. neutral→facing and 8-way, gravity suspension, air-budget sequences (all
   three orderings + full exhaustion + landing reset), invuln flags per stage
   (hit skipped + stat), contact push cap (no KO from dash), button-3 pin under
   generation/mutation/crossover, v4 round-trip + v3 compat, agent scenario tests
   (recovery/evasion mix/approach/punish), determinism probe. Goldens re-pin LAST.
7. **Rollout**: evolution smoke (2 × 300 gens) + charts + clips; DEVIATIONS entry
   (#20: new state, physics exception, air-budget change, instrument change).

Open risks called out: the fixed button bijection (above); and upward ground-dashes
make platform camping easier to escape, which may shift stage-shape selection —
worth watching in the smoke-run stats.

## Shipped (2026-07-13, pending designer play-test)

Everything in the strategy landed; deltas and findings:

- **Two bugs caught by the test battery before shipping:**
  1. Post-travel residual momentum (up to 18 u/s) transferred to opponents UNCAPPED
     through ordinary contact — a no-KO loophole one tick after the dash ended. Fix:
     the carry velocity at travel end is clamped to the character's ordinary
     ground/air max speed (normal play reaches those speeds anyway).
  2. Grounded dash warm-ups refunded the air-dash budget every grounded tick, making
     ground-started upward dashes free (dash-jump-jump-dash…). Fix: the budget only
     resets on grounded ticks OUTSIDE the Dash state.
- Defense-channel refactor note: with the unified pick, r=0 is pure argmax (always
  the strongest defense) — ALL defensive stochasticity now flows from the one
  randomness knob, which is more principled than the old extra coin. The mix test
  asserts both outcomes at r=0.5.
- 11 dash tests (locked-line travel, neutral→facing, gravity suspension + lift-off,
  all air-budget orderings + exhaustion + reset, i-frames negate-and-count,
  vulnerable dashes still hit, contact shove ≤ cap + clamped carry, button pin under
  generation/breeding, v4 round-trip + v3 compat, agent recovery dash, determinism
  probe). 183 total green; fingerprint + both goldens re-pinned (dated).
- **Evolution smoke** (2 × 300 gens; chart runs/media/charts/dash-trajectories.png):
  unlike the shield's cold start, dashes are adopted IMMEDIATELY — champions dash
  110/116 times across 5 evaluation rounds with 13/18 i-frame dodges, under a fitness
  that is still dash-blind (mobility is its own reward: traversal, recovery, and the
  jumps term all pay indirectly). The invulnerability GENES are already being
  searched: one champion character runs (0.83, 0.97) — both stages invulnerable —
  while its opponent runs (0.04, 0.15). Champion clip: runs/media/dash801_best.mp4.

## Amendment 2026-07-13: landing-aim recovery dashes (designer playtest report)

Reported: recovery dashes pointed DOWN at the stage lip when above it, and never
pointed UP from below. Root causes, all fixed with regression tests written first:

1. The recovery aim used the platform's CLOSEST POINT — the underside from below
   (no upward component ever) and the lip from above (downward aim). Recovery
   dashes now target a LANDING POINT: x clamped to the platform span, y = top +
   1.2 u clearance ("get above the platform before floating/jumping onto it").
2. Recovery reachability ignored the dash entirely — with jumps spent, sensed
   platforms were classed unreachable, so the recovery-dash utility never fired at
   all from below. A usable dash now extends reach by its straight-line travel
   (+1 u drift slack) in any direction, including straight up.
3. Waste guard: no recovery dash when already above the top with a small gap
   (drift in instead; the dash is saved) — fires only when height is needed or the
   horizontal gap exceeds 2.5 u. And a recovery dash can never aim downward.
4. The press frame now carries the intent direction immediately (humans hold the
   direction as they press).

Evolution re-run (2 × 300 gens): trajectories unchanged (a capability fix, not an
exploit-closure) — but champion quality under the fixed agent jumps: re-evals
50.9/68.8 with ZERO timeout draws (pre-fix champions re-measure at −9.2/8.7 with
3/5 draws — they had adapted to opponents who fumbled recovery). Dash usage rose
further (95/109 per evaluation; one champion lands 38 i-frame dodges). Clip:
runs/media/dashfix801_best.mp4.

## Amendment 2026-07-23: the exhaustion rule (designer bug report, DEVIATIONS #31)

"Air Jumps are leading to exhausted states without dash usage — we should only
exhaust on jump, jump and dash." The `AirJumpsExhausted` STATE had kept its
pre-dash entry point (air jump spent ⇒ exhausted) even though the budget doc above
promised full exhaustion = 2 jumps AND the dash. Now `SimPlayer.FullyAirExhausted`
(jumps spent AND (dash spent OR no dash)) gates every entry into the state: a
character with a dash in hand stays in `Air` with FULL air abilities — including
attacks — until the dash resolves airborne. This supersedes the 2026-07-13
"dash in hand must not re-enable chasing" rule, whose premise (cannot attack in
that state) no longer exists; the agent's state-keyed reads stay correct
unchanged, and the exhausted-disengage caution now keys on full exhaustion by
construction. Dash-less characters reduce bit-for-bit to the old rule — all
goldens and the fingerprint unmoved. Trial evolutions healthy
(runs/media/charts/exhaustion-rule-trajectories.png).

## Amendment 2026-07-20: the reflect gene (designer)

Sixth dash parameter: `reflect` (bool-as-float, active ≥ 0.5). Any projectile
contact during the DASH STATE (either stage, independent of — and checked
before — the invulnerability genes) re-fires the bolt at its shooter, with the
same semantics as the shield reflect (ownership transfer, mirrored path restart,
lifetime/decay clocks continuous, no ping-pong via the owner latch). A
reflect-dash therefore answers projectiles even with both i-frame genes off.
Agent: ×1.5 defense-score boost for a reflect-dash against ranged threats.
View additions (designer): dash INVULNERABILITY now strobes the body alpha
(fast 1.0↔0.6, distinct from post-hit invincibility's steady 0.4), and a
reflected bolt strobes toward white. Loader default 0 = off; stat shared with
the shield reflect (ProjectilesReflected).
