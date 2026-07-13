# Feature group: Fast Fall / Crouch / Directional Influence (Phase 6, feature 6)

Spec: FEATURES.md §Fast Fall/Crouch/Directional Influence. Clarifications 2026-07-13:

- **Crouch tint**: purple. **DI debug HUD**: live 8-way arrow beside each player's
  damage readout, flashing brighter on the tick a hit lands.
- **Fast fall**: added FLAT acceleration on top of gravity (0–15 u/s²; ≥0 makes the
  "never slower than default fall" rule structural).
- **Crouch slide**: acceleration change ±8 u/s²; positive slides cap at 1.5× the
  character's MaxGroundSpeed gene.
- **DI strength**: weaker tier — deflection gene ≤10% of knockback magnitude,
  opposite-hold reduction gene ≤20%.

## Why this group is architecturally different

These are CHARACTER-level parameters, not button-mapped moves: **seven appends to the
character schema** (append-only — crossover indexing of existing params unchanged):

| appended param | range | notes |
|---|---|---|
| fastFallAcceleration | 0–15 u/s² | 0 = the character simply has no fast fall |
| crouchAccelerationChange | −8–+8 u/s² | negative brakes, positive slides |
| crouchSpeed | 0.05–0.2 s | sink AND rise time ("very fast, not instantaneous") |
| crouchMoveSpeed | 0.3–1.5 | movement speed scalar WHILE crouched (designer veto of the no-move rule) |
| crouchHeightRatio | 0.4–0.9 | height while crouched (< 1 by construction) |
| directionalInfluence | 0.02–0.10 | ≤10% knockback-vector deflection |
| diKnockbackReduction | 0.05–0.20 | ≤20% magnitude cut when held near-opposite |

**Legacy compatibility**: old game.json files lack the six keys and the loader throws
on missing params — these seven get documented NEUTRAL loader defaults (0 / 0 / 0.1 / 1.0 /
0.9 / 0 / 0), which switch every new mechanic OFF (crouchMoveSpeed 1.0 = unchanged): legacy genomes behave
exactly as before (the Q8 "schema append + loader default" path; no format bump).

## Sim design

- **Held-direction capture**: SimPlayer stores the tick's held axes (hashed) during
  StepStateMachine — one field serving fast fall (down in air), crouch (down on
  ground), and DI (read at the hit instant). Third/fourth live consumers of Vertical.
- **Fast fall**: while airborne, holding down, and in Air / AirJumpsExhausted /
  WarmUp / CoolDown (NOT dash, attack-execute, stun): Velocity.Y −=
  fastFallAcceleration × dt in the physics step.
- **Crouch**: new `PlayerState.Crouch` (purple) with stages Sink → Held → Rise,
  timing from crouchSpeed. Entry from Idle only, grounded, holding down; release
  rises. Height animates (BodyHalf.Y scaled by ratio+stage progress), FEET PLANTED
  (center Y adjusts with the half-height), hurtbox = body so ducking under high arcs
  works structurally. Friction: while crouched, vx += sign(vx) × accelChange × dt,
  positive slides clamped at 1.5× MaxGroundSpeed; braking applies to knockback
  slides too (the designer's survive-at-high-percent use). **Actions from crouch
  (designer revision 2026-07-13)**: while HELD, the character can MOVE — max speed
  and acceleration scaled by crouchMoveSpeed — alongside the slide friction. Any
  action press (attack, JUMP, dash; shield included for consistency) queues the
  action (hashed), forces the uncancellable input-deaf Rise, then auto-executes at
  full size. Sink and Rise remain input-deaf per the original spec. A hit cancels
  crouch into Stun at full size.
- **DI (in TryHit; SKIPPED for any hit taken while shielding — blocked OR a poke
  through partial cover: a shielder is committed to the shield, not influencing —
  designer clarification 2026-07-13)**:
  knockback += heldUnit × DI × |knockback|; if the held direction is within 45° of
  opposite the knockback, magnitude × (1 − diReduction). Applied at the hit instant
  from the captured held direction — which makes agent DI imperfect FOR FREE: the
  held direction is whatever the commitment window last decided.
- Stats (fitness stays blind, v1 pattern): FastFallTicks, CrouchTicks, DIInfluencedHits.

## Agent design

- **Vertical becomes a real channel** ({down, neutral, up}, selected like the
  others): fast-fall/crouch/DI all express through it.
- **Defense channel grows to six options**: nothing / hop / shield / dash-out /
  FAST-FALL (airborne; favored when warm-up/cool-down/jumps-exhausted per spec) /
  CROUCH-UNDER (grounded; strong only when the crouched hurtbox would actually clear
  the incoming arc — computable from heightRatio vs arc bottom).
- **Fast-fall approach**: airborne above the opponent → hold down (occasional, via
  normal weighting).
- **Crouch braking/approach**: high damage + fast ground slide + negative
  accelChange → hold crouch to brake; positive accelChange + far opponent →
  occasional slide-approach.
- **DIBehavior**: when threatened or stunned, the horizontal/vertical channels get a
  contribution toward the FARTHEST blast line (stage-center bias) — pre-positioning
  the held direction so the next hit gets influenced. Imperfection = window +
  randomness, exactly as the designer requires ("might still be holding a previous
  direction, may also influence perfectly").

## View

- Purple crouch tint; the body sprite scales vertically with the sim's crouch scale
  (feet planted).
- **DI debug arrow**: HudView gains a small 8-way arrow beside each damage readout
  showing the live held direction, flashing bright for a few frames on the tick a
  hit lands (reads the TotalHitsReceived delta).

## Tests (~20) and gates

Fast-fall eligibility matrix + acceleration math; crouch stage timing, height and
feet-planted position math, friction both signs + the 1.5× cap, rise-before-attack
queue (uncancellable, input-deaf), idle-only entry, hit-cancels-crouch; DI deflection
and opposite-hold reduction hand-computed, 45° gate, held-direction capture timing;
agent: defense options fire, crouch-under only when it clears the arc, DI-toward-
safety, imperfection distribution across seeds; schema pin + legacy loader defaults;
determinism probes. Goldens re-pin LAST (hash format + fingerprint + agent RNG; DT
golden expected to move only for hash format thanks to neutral defaults).

Then: evolution smoke (2 × 300 gens) + charts + clips, DEVIATIONS #21, CLAUDE.md
palette, designer play-test gate.

## Shipped (2026-07-13, pending designer play-test)

Everything above landed, including the veto revisions (crouched movement + all
actions queueing through the Rise; DI skipped for shield pokes). Deltas and findings:

- **16 feature tests** (fast-fall vs plain-fall descent over an identical window;
  grounded down = crouch never fast fall; Sink/Held/Rise timing + feet-planted
  height math; crouched movement at the scaled speed; slide brake AND the 1.5× boost
  cap; attack/jump queue through the uncancellable input-deaf Rise; hit cancels
  crouch at full size; structural duck under a high arc; DI up-deflection and
  opposite-hold reduction through real hits; shield pokes get no DI; legacy loader
  defaults; agent duck across seeds; determinism probes). Suite 203/203; fingerprint
  + both goldens re-pinned (dated).
- Two test-battery corrections worth recording: the TestGames default move knocks
  back STRAIGHT UP, so "hold against the hit" means holding DOWN (a sideways hold
  just walks the victim out of range); and the agent's crouch-clear test pads the
  arc by the 1.0 telegraph margin, so a duck only registers as useful when the
  attack's offset.Y exceeds ~1.45 — both are properties of the design, not bugs.
- **Evolution smoke** (2 × 300 gens, seeds 901/902, 9 rounds, chart
  runs/media/charts/ffcdi-trajectories.png): trajectory shape preserved (plateau
  ~gen 100) but the plateau sits LOWER than the vuln-801/802 baseline (top ~72 vs
  ~88). Cause visible in champion evaluations: baseline champions farm 450–480
  damage rounds; new-gene champions top out ~270 — DI knockback reduction plus
  fast-fall recovery cut juggle strings short, so the per-stock damage term binds
  sooner. Adoption under blind fitness: fast fall immediately and heavily (up to
  933 ticks/round), DI live on most hits (8–29 influenced hits/round), crouch
  situational (15–57 ticks in some rounds).
- Visual verification: crouch = purple + squished feet-planted (trace-replay shot),
  DI arrow tracks held direction (↘ for a held (1,−1)) and flashes gold on the tick
  a hit lands. Champion clip: runs/media/ffcdi902_best.mp4.
- Verification note: `BRAWLER_TRACE` is only honored with `BRAWLER_AUTOPLAY=replay`
  — without it an AI drives and the arrow "mismatch" is the agent's real inputs.
