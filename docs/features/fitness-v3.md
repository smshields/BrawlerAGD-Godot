# Feature: standard-v3 Fitness — Per-Stock Damage Shaping + Composable Terms

Designer-specified 2026-07-10, following the fitness-noise study
(docs/reports/2026-07-09-fitness-noise-study.md), which identified v2's unbounded
damage-cap penalty as the dominant fitness-noise amplifier and the blocker on longer
matches.

## Designer's brief (verbatim intent)

1. Stop rewarding "farm" rounds: above a damage level, more damage becomes punishment.
2. Punish once knockback should almost always kill — the Smash-Bros 200–300%
   intuition → **punishment starts at 300 damage within a stock**.
3. **Cap counted damage within a stock at 600** — reaching it is a severe fitness
   punishment.
4. Refactor fitness construction for easier tuning.

## What shipped

- **Per-stock damage stat**: `PlayerStats.DamagePerStock` — damage taken within each
  life (SimPlayer records on respawn; the live/fatal stock is appended at result
  build). Stats-only: no gameplay change, no hash change, goldens untouched.
- **`ComposedFitness`**: a fitness assembled from named terms with `Breakdown()`;
  versioned functions are thin constructors over it. `evaluate --breakdown` prints
  per-term contributions per round — the tuning loop the designer asked for.
- **`StandardFitnessV3`** (registry name `standard-v3`), per player per stock with
  damage d, counted = min(d, 600):
  - damage reward: counted/10 (damage past 600 counts for NOTHING),
  - farm penalty: −max(0, counted − 300) — saturates at **−300 per farmed stock**,
  - damage fairness uses counted damage (farms can't distort it),
  - time / collisions / stock-fairness terms are v2-verbatim,
  - v2's match-level `stocksLost×100` damage cap is REPLACED by the per-stock shape.
  Tunables are constructor parameters (`punishStartDamage`, `stockDamageCap`,
  `punishSlope`).
- **`FitnessRegistry`** + `EvolutionConfig.FitnessName` (default `standard-v3`) +
  `--fitness` on evolve/evaluate/noise. run.json already recorded the name; **resume
  now honors it**, so pre-v3 runs keep standard-v2. v2 itself is untouched and frozen.

## Measured behavior (see also the noise-study report)

- The GameC stun-farm round (1134 damage, one stock): v2 scored −909 and kept falling
  with time; v3 scores −148.6 with `farmPenalty` pinned at exactly −300 — severe
  (healthy rounds score +25…+40) but bounded.
- **Longer matches are unlocked**: under v2, a 90 s cap exploded GameC's fitness std
  19→186; under v3, 90 s ≈ 60 s noise (std 33 vs 30) and draw rates DROP (GameF
  47%→19%, utility champion 4%→0%) — stalling no longer digs a deeper hole, and
  matches that need extra time finish.
- ~~Known interaction, watch in future runs: the collisions term (+1/hit, v2-verbatim)
  partially offsets the farm penalty (+194 for a 191-hit farm).~~ **Closed by the
  2026-07-10 collision-scalar amendment below.**

## Amendment 2026-07-10: tunable collision scalar (designer-directed)

Same-day, pre-designer-gate amendment of v3 (no published results depended on the
1.0 default; the pre-amendment sanity runs remain on disk as `runs/v3-r9-*` for
comparison). New `collisionScalar` on `StandardFitnessV3` (+ `--collision-scalar` on
evolve/evaluate/noise, recorded in run.json — a run stays self-describing).

**Tuned default: 0.5**, picked from breakdown data, not taste:
- At 1.0, hits were double-counted — a hit averages ~6.5 damage, so it earned ~0.65
  via the damage term plus 1.0 via collisions — and a 191-hit farmed stock recouped
  65% of its −300 farm penalty.
- At 0.5, healthy champion rounds show collisions ≈ damage term (29≈34, 46≈52,
  37≈38 — the "even" point), and farm recoup drops to 32% (farmed GameC round net:
  −148.6 → −245.6).

Measured consequences (charts: runs/media/charts/v3-collision-scalar-trajectories.png):
- Noise: neutral-to-better at 9 rounds (champion std 11.0 → 7.2 — hit counts are a
  noisy quantity and now weigh half). Study-game stds within ±1.
- Evolution (2 seeds × 300 gens, r9): same trajectory shape; curves sit ~15 pts lower
  purely from the reward re-denomination. Diversity unchanged (popdiv 0.054/0.062).
  Champions evolved under 0.5 score 60.0/113.3 (median, fresh seed) under the tuned
  measure vs 48.6/43.5 for champions evolved under 1.0 — the tuned fitness selects
  what it now values. The seed-402 champion (clip:
  runs/media/v3cs05_402_best.mp4) is the strongest game any config has produced:
  425/565 mutual damage across all stocks, zero farm penalty, 50 s.
- Scores across scalar values are NOT comparable (the denomination changed);
  cross-config comparisons must re-evaluate under one scalar.

## Evolution sanity under v3 (pop 100 · 9 rounds · median · 300 gens)

| run | best-ever | avg (last 20) | champion re-eval (fresh seed, matching config) |
|---|---|---|---|
| v3-r9 · seed 401 · 60 s | 108.1 | 58.0 | 91.1 (0 draws / 6 rounds) |
| v3-r9 · seed 402 · 60 s | 128.0 | 79.2 | 76.0 (0 draws / 6 rounds) |
| v3-r9 · seed 401 · 90 s | 112.9 | 64.2 | 41.0 (1 draw / 6 rounds) |

Evolution under v3 is healthy at 60 s: champion quality holds (re-eval 76–91, kills
every round), no farm motifs in the champions. The single 90 s replicate produced a
weaker champion — longer matches are now *safe* (noise-neutral, draw-reducing on fixed
games) but not yet shown *better* for evolution; keep the 60 s default and treat match
length as an open experimental knob (`--max-seconds`, now on evaluate too).

## Testing

7 new tests: hand-computed v3 scores (threshold at 300, cap at 600, identical scores
for 700 vs 6000 damage stocks, spread-vs-farmed comparison), per-stock recording
consistency on a real match (sums match totals, lives counted), registry construction,
and resume-keeps-recorded-fitness. Suite green; golden hashes unchanged (stats-only
sim change).
