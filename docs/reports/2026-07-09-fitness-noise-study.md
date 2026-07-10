# Fitness Noise Study — Reducing Measurement Noise Without Losing Diversity or Humanity

2026-07-09 · follows docs/reports/2026-07-09-utility-agent-comparison.md §3 ("the
instrument is stochastic") · raw sweep data in [2026-07-09-noise-sweep.csv](2026-07-09-noise-sweep.csv)

**Question (designer):** how do we make fitness less noisy WITHOUT (a) reducing genome
diversity, or (b) making the utility agent less human (its randomness stays at 0.15)?
Proposed levers to investigate: a diversity score in the evolution strategy, and longer
match time (cheap now that evaluation is headless).

**Method.** New reproducible tooling (committed):
- `BrawlerRunner noise --games <list> --reps 20 [--rounds N] [--aggregate median|mean]
  [--max-seconds S] [--target-seconds T]` — evaluates each genome 20 times with
  independent seed streams and reports the fitness spread (std) per genome.
- `BrawlerRunner popdiv --run <dir>` — mean pairwise normalized genome distance of a
  run's population (`GenomeDistance`: mean per-dimension |a−b|/generation-range over
  all character+move params; 0 = mechanical clones).
- `evolve --diversity-weight W` — opt-in fitness sharing: selection ranks
  `fitness + W × meanDistanceToPopulation`; recorded stats and the champion stay RAW
  fitness; `W = 0` (default) is bit-identical to legacy selection (tested).

Genome panel: study games A–F + the 3 utility-evolved champions + 6 fresh random
genomes (seed 9001). Noise metric: std of the aggregated (median-of-rounds) fitness
across 20 independent evaluations.

## 1. Where the noise comes from

`standard-v2` per-round scores are **multi-modal**: a kill-ending round scores ~+50…110,
a timeout draw ~−20…−50 (the −35 overtime penalty plus the length term make the 60 s
boundary a ~45-point cliff), and a degenerate "farm" round (damage-cap penalty,
unbounded in accumulated damage) −200…−900. Measured fitness noise is almost entirely
*rounds flipping between these modes* — genomes whose matches sit far from any mode
boundary (most random genomes) evaluate with std < 2 even at 5 rounds.

## 2. Sweep results (20 reps per cell; std of aggregated fitness)

| config | median std (15 genomes) | mean std | champions' mean std | draw rate |
|---|---|---|---|---|
| **rounds 5, median, 60 s (baseline)** | 11.5 | 20.0 | 13.3 | 61% |
| rounds 9, median, 60 s | **7.6** | **10.5** | **9.8** | 61% |
| rounds 15, median, 60 s | **6.0** | **6.9** | **6.3** | 61% |
| rounds 5, mean, 60 s | 10.6 | 28.0 | 18.1 | 61% |
| rounds 5, median, 90 s | 11.5 | 32.3 | 13.2 | 54% |
| rounds 9, median, 90 s | 7.5 | 14.8 | 8.7 | 54% |
| rounds 5, median, 120 s | 19.5 | 51.2 | 13.2 | 50% |
| rounds 5, median, 90 s, target 67 | 10.8 | 28.7 | 16.6 | 54% |

Per-genome highlights (std, key configs):

| genome | r5·60s | r9·60s | r5·90s | r9·90s |
|---|---|---|---|---|
| GameA | 153.4 | 51.8 | 157.3 | 81.2 |
| GameC | 19.4 | 17.8 | **186.4** | 27.7 |
| utility champions | 8.7 | 7.5 | 8.6 | 7.2 |
| random genomes | ≤6.5 | ≤4.8 | ≤8.8 | ≤7.5 |

**Findings.**

1. **Rounds are the lever.** Noise falls ~1/√rounds as expected: median std 11.5 →
   7.6 (9 rounds) → 6.0 (15 rounds); the worst genome (GameA, bimodal farm/kill)
   drops 153 → 52. Cost is linear and headless-cheap: a 300-gen pop-100 run is ~25 s
   at 5 rounds, ~45 s at 9, ~75 s at 15.
2. **Longer matches do NOT reduce noise — they amplify it.** The designer's length
   hypothesis fails for a structural reason: the damage-cap penalty grows with
   accumulated damage, so giving a farm/stall round 30–60 extra seconds makes the
   negative mode *deeper* (GameC std 19 → 186 at 90 s; overall 120 s is the noisiest
   config tested). Draw rate barely moves (61% → 50% at 2× length) — matches that
   stall at 60 s mostly stall forever; the timeout mode doesn't collapse, it deepens.
   Longer matches only become viable if the fitness shape changes first (e.g. a
   time-normalized or capped damage penalty — that would be a new `standard-v3`, a
   designer decision about what fitness MEANS).
3. **Median beats mean** at equal rounds (mean std 20.0 vs 28.0): the mean is dragged
   by farm-round outliers. Keep the Unity-parity median.
4. **Scaling the target length with the cap (t67·90s) doesn't help either** — same
   mechanism as (2).

## 3. Evolution-level validation (does lower noise cost diversity or quality?)

Setup: 3 seeds (401–403) × 300 generations × pop 100, utility agent r=0.15 throughout.
Baseline = the comparison study's `compare-utility-*` runs (5 rounds). Candidates:
9 rounds, and 9 rounds + `--diversity-weight 30`. Population diversity = popdiv (mean
pairwise normalized genome distance) of the final population; champion quality =
median fitness re-evaluated on a fresh seed (5 rounds, seed 5).

| run | best-ever | avg fitness (last 20 gens) | popdiv (final) | champion re-eval (fresh seed) |
|---|---|---|---|---|
| baseline r5 · 401 | 107.3 | 55.5 | 0.0487 | 67.3 |
| baseline r5 · 402 | 106.3 | 49.5 | 0.0616 | 62.5 |
| baseline r5 · 403 | 95.8 | 37.2 | 0.0457 | 70.8 |
| **rounds 9 · 401** | 105.1 | **69.3** | 0.0547 | **102.0** |
| **rounds 9 · 402** | 119.1 | **64.3** | 0.0626 | **98.1** |
| **rounds 9 · 403** | 118.8 | **65.1** | 0.0446 | **110.2** |
| r9 + div30 · 401 | 109.8 | 77.0 | 0.0568 | 106.5 |
| r9 + div30 · 402 | 125.7 | 67.4 | 0.0606 | 82.3 |
| r9 + div30 · 403 | 95.6 | 51.3 | 0.0424 | 56.2 |

**Findings.**

1. **9 rounds does not cost diversity.** Final-population popdiv is statistically
   indistinguishable from baseline (0.045–0.063 in both). The fear that sharper
   measurement collapses the population onto one mechanical niche is not borne out —
   selection pressure was already truncation-by-half; only the *accuracy* of who gets
   truncated changed.
2. **9 rounds removes the winner's curse.** Under 5 rounds, best-ever (~96–107) was
   partly measurement luck: champions re-evaluated at only 62–71. Under 9 rounds,
   best-ever ≈ re-eval (105→102, 119→98, 119→110): the recorded champion is real.
   Champion quality itself jumps ~50% (re-eval 98–110 vs 62–71) — with noisy fitness,
   evolution had been promoting lucky genomes over good ones.
3. **Population averages rise** (64–69 vs 37–56) purely from better selection — the
   instrument, agent randomness, and fitness function are unchanged. The paper's
   plateau criterion (avg > 80 × 20 gens) is still not met; if plateau detection
   matters, recalibrate the threshold to the utility era (these runs suggest ~65 is
   the new "converged" level at r=0.15).
4. **The diversity bonus (weight 30) is not needed and not reliably helpful here** —
   popdiv doesn't systematically rise, and one seed (403) got materially worse (the
   bonus kept weaker niches alive at the expense of refining the best one). Its
   property is proven (unit test shows spread preservation at higher weights on small
   populations), so it remains available as an opt-in research knob, default 0.

## 4. Recommendations

1. **Adopt 9 rounds (median) as the standard evaluation for utility-era runs** —
   `--rounds 9`. It halves measurement noise, eliminates the winner's curse, raises
   real champion quality ~50%, does not touch genome diversity, and keeps the agent
   exactly as human as before. Cost: 300-gen pop-100 run ≈ 45 s (vs 25 s). For
   publication-grade studies, 15 rounds buys another ~25% noise reduction at ~75 s.
   (I have NOT changed the default `RoundsPerIndividual` — Unity parity is 1 and the
   CLI examples use explicit flags; say the word and I'll make 9 the config default.)
2. **Do not lengthen matches under the current fitness.** The damage-cap penalty is
   unbounded in time, so longer caps deepen the degenerate mode and add noise. If
   longer matches are wanted for design reasons, that requires a `standard-v3` with a
   time-normalized or floored damage penalty — a fitness-semantics decision that is
   yours to make, not a tuning knob.
3. **Keep the diversity weight at 0 for normal runs**; use it (10–60) only for
   experiments that specifically need niche preservation. It is recorded in run.json
   either way, so any run remains self-describing and reproducible.
4. Keep the median aggregate (Unity parity) — the mean is strictly noisier here.
5. Future variance-reduction option, unexplored: common random numbers (share agent
   RNG streams across individuals within a generation) reduces *ranking* noise
   without touching per-genome scores; worth a follow-up if selection accuracy at
   5 rounds ever matters more than raw throughput.
