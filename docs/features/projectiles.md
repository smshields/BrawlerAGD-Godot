# Feature: Projectiles (Phase 6, feature 8)

Spec: FEATURES.md §Projectiles + the designer's sketch (2026-07-14). Clarifications:

- **Rollout**: tested via the PER-BUTTON composition pin ([attack, projectile,
  shield, dash]) — the PINNED legacy default stays byte-identical (no fingerprint
  move). `SlotSpec.Projectile` joins the RANDOM pool immediately (uniform draw over
  FOUR types — composed-mode runs regenerate differently from v4-era seeds; noted).
- **Collision**: platforms DESTROY projectiles (despawn on contact). Blocked by
  shields under the existing coverage rule; DI applies ("knockback matches melee").
  Projectiles do not interact with each other.
- **No cap** on live projectiles — warm-up/cool-down genes bound the rate naturally;
  we accept and will watch for spam champions.
- **Look**: shooter reuses the attack tints (WarmUp yellow / Attack red / CoolDown
  blue) — the flying shape is the differentiator. Projectiles are FILLED shapes in a
  dedicated GOLD tint (1.0, 0.8, 0.25) vs the shield's white→red OUTLINE circle;
  damage decay = fade toward transparent (spec: decay only — non-decaying
  projectiles stay solid).
- Sketch mapping: "EXIT" = launch-location genes (clamped to overlap the player);
  the three drawn paths = sine / linear / gravity-arc; "despawn X after boundary" =
  blast-zone despawn. The FUTURE ideas box (charge, multiple hitboxes, split-on-hit,
  controllable direction) is out of scope.
- "Never damage the user on fire, but they might": the owner is immune while the
  projectile still overlaps them from launch; afterwards vulnerable only if the
  hitsSelf gene is active.

## The architectural first: a non-player sim entity

Projectiles are the first entities in SimWorld beyond the two players. New
deterministic machinery, all inside BrawlerSim:

- `SimWorld.Projectiles` — an ordered list (spawn order = tick order; no
  dictionary iteration). Each live projectile: owner, move index, spawn tick,
  origin, facing, current position, rotation angle, damage scale, alive flag.
- **Closed-form paths** (no per-tick integration drift; replay == live by
  construction): with t = ageTicks/60, s = v·t + ½a·t² along the spawn facing,
  lateral offset by shape — LINEAR: 0; SINE: A·DetMath.Sin(2π·f·t) with fixed
  amplitude A (one scalar gene = frequency, per spec); QUADRATIC: c·s² signed
  downward-curving like the sketch's arc. Gravity flag adds −½g·t² in world Y.
- **Tick order**: projectiles step in a new phase between player physics and
  TryHit (documented in the SimWorld tick-order comment). Despawns: platform
  contact, blast-zone exit, TTL, damage scale reaching 0, or landing a hit
  (single-hit projectiles).
- **Hit resolution** reuses the melee pipeline: ComputeKnockback semantics with the
  projectile's knockback genes, shield coverage blocking (degrades the shield),
  DI at the hit instant, stun from hitstun duration. Victim = either player,
  owner gated by launch-overlap immunity + hitsSelf.
- **Hitbox shapes for real**: circle = circle-vs-AABB; square = AABB, or OBB via
  SAT when rotating; triangle = SAT. Rotation (bool + rate genes) affects
  collision, not just the view. All shape math in DetMath with hand-computed tests.
- **StateHash**: projectile section is appended ONLY when the list is non-empty —
  projectile-less matches hash exactly as today, so NO golden re-pins anywhere
  (fingerprint untouched too, since the pinned default doesn't change).

## Genome (schema "projectile", ~21 params — order fixed, append-only thereafter)

| gene | range | notes |
|---|---|---|
| pathShape | 0–3 (floor → linear/sine/quadratic) | int-as-float, like the bool-as-float precedent |
| pathScalar | 0.5–6 | sine frequency (Hz) or quadratic curvature |
| timeToDecay | 0.5–4 s | TTL |
| velocity | 3–15 u/s | launch speed |
| doesAccelerate | 0–1, active ≥ 0.5 | |
| acceleration | −10–10 u/s² | sketch shows both signs; applied only when accelerating |
| affectedByGravity | 0–1, active ≥ 0.5 | |
| warmUpDuration / executionDuration / coolDownDuration | melee-matching ranges | the shooter's FSM timing |
| hitboxSize | 0.2–0.7 u | "never larger than the shooting character" (PlayerBaseWidth 0.74) |
| hitboxShape | 0–3 (floor → square/circle/triangle) | |
| doesRotate | 0–1, active ≥ 0.5 | |
| rotationRate | 0.5–8 rad/s | |
| knockbackScalar | 0–25 | melee-matching |
| knockbackModX / knockbackModY | melee-matching (incl. ConstrainKnockback lerp + valid-domain note) | "same behaviors as melee" |
| damageFactor | melee-matching | |
| damageDecay | 0–1, active ≥ 0.5 | |
| decayRate | 0.1–1 /s | scales damage AND knockback down over flight; view fades with it |
| hitsSelf | 0–1, active ≥ 0.5 | |
| launchX / launchY | ±0.5 × body half extents | clamped so the spawn overlaps the player ("EXIT" in the sketch) |

`MoveType.Projectile` + `SlotSpec.Projectile`; game.json → **formatVersion 5**
(v1–v4 load unchanged); composition strings gain "projectile"; the advanced-ranges
panel picks the new schema up automatically (it iterates DefaultSchemas).

## Agent (instrument change — DEVIATIONS entry; all through window + randomness)

- **Firing**: projectile moves join the attack channel with a LOOSE reach test —
  the opponent lies within a corridor around the spawn facing (closed-form range
  from velocity/TTL/gravity, padded), gated OFF at close range (spec) where melee
  candidates stay preferred.
- **Zoning, not overly**: a distance-band behavior scores projectile candidates up
  at range with a moderate weight — deliberately below chase/attack weights so
  pure keep-away is possible for evolution to find but not agent-forced.
- **Dodging**: each enemy projectile is projected forward k ticks (closed-form);
  a predicted self-intersection raises the existing telegraph-threat machinery —
  all six defense options apply (crouch under high shots, fast-fall under arcs,
  shield per coverage, dash i-frames, hop, nothing).
- Stats: ProjectilesFired, ProjectileHits, ProjectileHitsTaken, ProjectileDodges
  (+ evaluate line). **Fitness stays blind** (watch-first pattern; "zoning possible
  but not overly selected for" becomes a fitness-shaping decision after play-test).

## View

- `ProjectileView` pool under ArenaView: filled Polygon2D (square/circle/triangle)
  in gold, rotated per sim angle, alpha = damage scale while decaying; removed on
  despawn. Shield differentiation per the clarification above.
- No new player tints; no HUD change.

## Tests (~25) and gates

Schema pin + defaults; closed-form path positions hand-computed per shape (incl.
gravity + acceleration composition); spawn point clamped to player overlap; owner
immunity then hitsSelf; platform/boundary/TTL/decay/hit despawns; shape collision
matrix (rotated square SAT, triangle SAT, circle) hand-computed; shield block +
degradation + DI on projectile hits; multiple live projectiles; no-cap stress
determinism (spam genome, hash equality); v5 round-trip + v4 compat; composed
generation with SlotSpec.Projectile; agent fire-at-range / hold-at-close /
dodge-incoming scenarios; hash-absence proof (projectile-less match hashes
unchanged — the no-re-pin guarantee); full determinism probes (parallel, replay,
resume). Then: per-button smoke (2 × 300 gens, [attack, projectile, shield, dash])
+ random-pool smoke, trajectory charts, champion clip, evaluate stats line,
DEVIATIONS #23, designer play-test gate.

## Shipped (2026-07-14, pending designer play-test)

Everything in the plan landed; deltas and findings:

- **Schema grew to 24 genes** (+hitstunDuration — the spec's "knockback matches
  melee" implies stun does too; the melee stun-cap applies, break-stun exemption does
  not). The melee knockback valid-domain widening was NOT copied (ConstrainKnockback
  is hitbox-relative, a melee-only concept).
- **23 feature tests** (SAT shape matrix incl. the diagonal-reach and flat-side
  cases; every path term hand-computed; spawn point/timing; TTL/boundary/platform/
  decay despawns; melee-formula hit + stun; owner immunity with a decelerating
  boomerang bolt; shield block; dash i-frame negation with pass-through; spam
  determinism (multi-bolt); v5 round-trip; per-button + random-pool composed
  generation; agent fire/hold/dodge scenarios). Suite 237/237. Match goldens
  UNMOVED (the hash-gate design); fingerprint re-pinned for formatVersion bytes only.
- **Two agent-design findings from the battery, both deliberate:**
  1. The defense channel now triggers on projectile threats REGARDLESS of
     counter-hit options — the original `!AnyCanHit` gate made the victim
     trade-commit into a bolt already in flight, which counter-firing cannot stop
     (unlike the melee trade). DEVIATIONS #23.
  2. Mashed re-dashing has a 1-tick Idle gap between travel end and the next
     warm-up — a crossing bolt found it. Continuous i-frames are not achievable;
     the test pins a single timed dash instead. Kept as designed.
- **Smokes** (2×300 gens pinned [attack,projectile,shield,dash] seeds 1101/1102 +
  1×300 random pool seed 1103, 9 rounds; chart
  runs/media/charts/projectile-trajectories.png): pinned-projectile plateaus at
  ~61 top vs the ~88 no-projectile baseline (one melee slot became a
  harder-to-confirm ranged one; DI + dodges already cut strings), the random pool
  sits between at ~76. Champions genuinely shoot (2–9 bolts/round, hits landing).
  The random pool gives projectiles **38%** of the final population (dash 37%,
  attack 25%, shield 1%) and produced a **3-projectile zoner vs 3-dash rusher**
  champion — ranged-vs-mobility as an emergent matchup. Clip:
  runs/media/projrand1103_best.mp4.
- Visual verification: runs/media/shots-proj/tick_00085.png — a gold filled circle
  mid-flight between a CoolDown shooter and a winding-up opponent (trace-replayed);
  the follow-up frame shows the hit (stun + damage). A sine bolt launched at body
  height died on the mid platform — low sine shots grazing floors is an emergent
  hazard of platform destruction, not a bug.
- Dead-json sweep: comp-rand-1001/1002 moved to archive/ (3-type-pool era,
  non-regenerable now that Random draws over four types).

## Amendment 2026-07-20: wind-up telegraph (designer follow-up)

Play-test review surfaced ZERO shield activations against a projectile champion
(shield-vs-zoner franken match, instrumented: 26 threat ticks over 172 bolt-live
ticks, and the defender was never grounded-Idle inside the 0.5 s dodge horizon —
zoners also never triggered the melee telegraph, so there was no early signal at
all). Designer ruling: warm-up phases signal defensive counterplay ACROSS THE
BOARD. A winding-up projectile now telegraphs exactly like a melee wind-up — the
"arc" is the shot's predicted corridor at the defender's column (the shooter's own
aim test, reversed), crouch-clear reads the corridor bottom, and the melee
trade-commit gate applies (interrupting the shooter cancels the shot). The same
franken match now shows real raises/blocks/breaks. DEVIATIONS #24; pinned by
AgentShieldsDuringAProjectileWindUp. Evolution result (tele5-1201/1202 smokes,
random pool): shield adoption jumped from 1% of final-population slots to
49%/39%, with shield+projectile champions on both sides of a match — the
telegraph made shields answer ranged threats, and ranged threats made shields
worth a slot. Fitness plateau dropped ~76 → ~63 (defense works — the
stricter-instrument pattern).
