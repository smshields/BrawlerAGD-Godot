# Feature: Shield (Phase 6, feature 4)

Spec: FEATURES.md §Shield Mechanic (designer-owned). Clarifications recorded 2026-07-12:

- **Composition:** third move slot, GUARANTEED shield for now (2 attacks + 1 shield).
  Dynamic type assignment must be enable-able later — the type gene and
  mismatched-type crossover semantics are built now, generation just pins slot 2.
- **Break stun:** evolvable per-shield parameter, EXEMPT from the 0.25 s stun cap
  (the break is the mechanic's designed counterweight).
- **Visuals:** white circle outline that lerps toward red as the shield shrinks;
  player state tint while shielding = cyan (new state → new tint, per aesthetics).
- **Fitness/data:** stats only; standard-v3 stays blind to shields in v1.

## Design mapping (deltas on the 10 questions)

1. **Schema:** new `ShieldParams` + `DefaultSchemas.Shield` (a NEW schema — attack
   schema untouched). Proposed generation ranges (designer-tunable at review):
   | param | range | notes |
   |---|---|---|
   | windUp | 0.05–0.3 s | "shorter than attacks" (attacks: 0.1–0.6) |
   | coolDown | 0.05–0.3 s | same |
   | initialSize | 0.5–2.0 u diameter | "no larger than 2× character size" (char height 1 × scalar ≤1.5) |
   | holdDegradationRate | 0.05–0.4 u/s | radius shrink while held |
   | hitDegradationScalar | 0.01–0.06 u/dmg | always positively damage-correlated |
   | knockbackReduction | 0.5–0.9 | "always significant" |
   | spacingPush | 0.5–3.0 u/s impulse | low, never lethal (valid-domain capped) |
   | regenRate | 0.05–0.5 u/s | resumes from current health, not fresh |
   | breakStun | 0.5–2.5 s | cap-EXEMPT (designer decision) |
   `MoveGenome` gains a structural `MoveType` gene (Attack | Shield) + the matching
   param set. Generation: slots 0–1 attack, slot 2 shield (`GenerationConfig.ShieldSlotCount = 1`,
   settable 0 to disable). Crossover: same-type slots cross params positionally as
   today; MISMATCHED types coin-flip the whole move from one parent — RNG-gated so
   fixed compositions consume no extra draws (stream-stable, same trick as feature 1).
2. **Bounds:** break threshold radius = character size / 5 (MatchConfig); shield
   edge may never cross the character's center (|offset| ≤ current radius); spacing
   push velocity capped well below lethal.
3. **Agent (instrument change, behavior-logged + golden re-pin):**
   - `ShieldBehavior`: raise on opponent WarmUp in range — competes with
     TelegraphDodge through the normal weighted-random selection (the designer's
     "trade-off driven by the weighted random nature"); utility scales DOWN as
     health nears the break threshold; releases when threat passes.
   - Shield aim: while shielding and shield smaller than body, steer offset toward
     the opponent via the directional controls (first live use of InputFrame.Vertical).
   - Break punish: opponent stunned by a SHIELD BREAK → prefer the highest-damage
     move (extends the damage-ranked attack selection).
4. **Assets:** none — procedural circle draw (scales cleanly for grow/shrink/degrade).
5. **Entities/state:** new `PlayerState.Shield`. FSM: Idle + held shield-button →
   WarmUp(shield) [circle grows over windUp] → Shield [held; hold-degradation ticks;
   directional offset control] → release → CoolDown [circle shrinks] → Idle. Cannot
   enter from Stun/WarmUp/CoolDown/air (per spec). Break → Stun(breakStun, uncapped)
   and health regenerates from zero. New hashed state: ShieldHealth, ShieldOffset,
   (shield phase runs through existing PhaseTicksLeft/CurrentMoveIndex).
   Hit interception in TryHit: blocked iff the (hitbox ∩ body) region is fully inside
   the shield circle — partial cover = still hit ("only protects where it covers");
   blocked hit: zero damage, knockback × (1 − reduction), shield health −= damage ×
   hitDegradationScalar. Spacing push joins the body-contact phase of the tick
   (documented tick-order addition → DEVIATIONS).
6. **Controls:** no new buttons. Shield-mapped buttons are HELD (level semantics);
   attack buttons stay edge-pressed. HumanInputSource distinguishes per button via
   the genome mapping it already has access to; the agent holds the bit across its
   commitment window. Traces unchanged (bits per tick already express holds).
7. **Research data:** per-player ShieldActivations, BlockedHits, ShieldBreaks,
   ShieldTicks (stats-only; fitness blind; printed by evaluate).
8. **Serialization:** game.json formatVersion 2 → 3 (moves gain `type` + shield
   params). v1/v2 files load as all-attack genomes (type defaults to Attack).
   Population fingerprint + match goldens re-pin (dated) — real design-space change.
9. **Rollout:** shield slot ON by default in generation (needed to verify agents
   leverage it), one config switch to disable (`--shields 0`); fitness blind.
10. **Readability:** cyan player tint (new state color, CLAUDE.md palette updated);
    white→red circle by degradation; grow/shrink animates from sim state.

## Implementation order (gates per ADDING_FEATURES.md)

1. Schema + genome (`MoveType`, ShieldParams, generation/crossover/mutation incl.
   mismatched-type semantics) → schema pin tests, round-trips, fingerprint re-pin.
2. Sim: state, phases, degradation/regen, coverage-blocking, spacing push, break
   stun (cap-exempt), StateHash extension → hand-computed unit tests per formula;
   determinism suite unmodified.
3. Stats + evaluate output.
4. Input plumbing: hold semantics (human per-mapping, agent held bits).
5. Agent behaviors (shield raise/aim/release, break punish) → scenario regression
   tests; utility golden re-pin.
6. Serialization v3 + legacy-load tests.
7. View: circle draw, tints; visual verification screenshots + clip.
8. Integration probe + evolution smoke (2 seeds × 300 gens) + charts + champion clip.
9. Docs (DEVIATIONS: new state, tick-order addition, instrument change; CLAUDE.md
   tint palette) → designer play-test gate.

Estimated size: the largest feature since the utility agent (~10 files touched in
BrawlerSim, 2 in godot/, ~25 new tests).

## Shipped (2026-07-12, pending designer play-test)

Everything in the plan above landed as specified; deltas and findings:

- The offset-clamp invariant caught a real bug in review-by-test: the clamp ran
  before same-tick hold degradation, letting the shield edge slip past the
  character's center by one tick's decay. Fixed (re-clamp after degradation).
- Agent near-break aversion is a RAMP — weight scales with
  (health − releaseThreshold)/(1 − releaseThreshold) — so a sliver-above-break
  shield never outbids doing nothing.
- 13 shield tests (FSM entry rules, grow/hold/shrink timing, hold/hit degradation
  and break with cap-EXEMPT stun, regen-resumes-not-fresh, covered-vs-exposed
  blocking geometry, spacing expulsion, offset clamp, v3 round-trip + v2
  compatibility, dodge-vs-shield mix across seeds, near-break refusal,
  hold/release management, determinism probe). 169 total green. Population
  fingerprint + both match goldens re-pinned (dated).
- Scripted showcase: runs/shield-scripted.json + .trace.json (replayable);
  screenshots in runs/media/shots/ (cyan shielder, white→red circle, aim offset);
  clip runs/media/shield_scripted_demo.mp4.
- **Dynamics findings for the designer:**
  1. High spacingPush zones out melee entirely — attackers get pushed beyond
     reach during warm-up and every swing whiffs. Within spec ("push back at a
     consistent distance"), but it makes strong-push shields a hard counter to
     melee once evolution notices.
  2. Evolution smoke (2 × 300 gens): champions barely use shields (3 activations,
     0 blocks across 5 champion rounds). Under the deliberately-blind fitness,
     shields are ANTI-fitness: blocking suppresses the damage/hit interaction the
     function rewards. The mechanic works; the incentive doesn't point at it.
     Expected next iteration if desired: a block/blocked-hit reward term (v3
     amendment) or persona fitness — designer decision.

## Amendment 2026-07-12 (same day): block reward + humanized shield management

Designer-directed follow-up to the two findings above:

- **standard-v3 `blocks` term**: +2.0 per blocked hit (constructor-tunable). A block
  suppresses ~1.15 of rewarded interaction (the hit's damage + collision terms), so
  2.0 makes blocking a net-positive event comparable to landing a hit. Naturally
  bounded — every block costs shield health.
- **Shield management is no longer frame-perfect** (designer: "someone would not be
  able to execute every single time"). Hold/release and aim now run through the same
  commitment window and randomness mixture as every other decision; an unthreatened
  hold keeps a 0.6 hesitation utility so release timing varies across seeds
  (quantized to reaction-window boundaries — the human cadence). One deterministic
  override: health at/below the release threshold always releases (the circle is
  visibly red). Raise timing already carried noise (window + mixture + the
  health-weighted dodge coin).
- Measured (2 × 300 gens, same seeds as the blind runs): champion shield activations
  5/2 → 32/14, blocks 1/0 → 9/0, breaks 0; trajectories slightly above the blind
  runs. Shield use is now selected FOR. Seed 701's champion raises often but blocks
  rarely — the spacing/zoning value of a raised shield is doing work even without
  blocks, which is exactly the surprising-builds space the designer wanted open.


## Amendment 2026-07-20: the reflect gene (designer)

Tenth shield parameter: `reflect` (bool-as-float, active ≥ 0.5). When active, a
projectile that the coverage geometry would BLOCK is instead RE-FIRED at its
shooter: ownership transfers to the reflector (the return bolt can hit the
original shooter and credits the reflector's ProjectileHits), the path restarts
mirrored from the reflection point with fresh kinematics from the bolt's genes,
and the TTL + damage-decay clocks keep counting from the ORIGINAL launch
(designer: "keeping damage decay and lifetime decay constant"). Pokes through
partial cover still hit exactly as before; melee is untouched (projectiles only).
Judgment call held open for the designer: a reflect still DEGRADES the shield
like a block — the work isn't free. The owner-clearance latch prevents
ping-pong re-reflection while the bolt leaves. Agent: against a RANGED threat
(wind-up telegraph or bolt in flight), a reflect-shield's defense score is
boosted ×1.5 — knowing it sends the bolt back makes it the better answer
(designer intent). View: a just-reflected bolt strobes toward white for ~a
quarter second. Loader default 0 = off for all pre-append files (no format
bump); stat: ProjectilesReflected (+ evaluate `proj(fired/hit/refl)`).
Pre-reflect smokes remain valid as PRE-change baselines only.
