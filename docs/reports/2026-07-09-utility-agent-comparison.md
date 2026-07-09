# Utility Agent vs Decision Tree — Instrument Comparison Report

2026-07-09 · companion to docs/features/utility-agent.md · raw per-round data in
[2026-07-09-battery.csv](2026-07-09-battery.csv) · runs in `runs/compare-{utility,dtree}-{401,402,403}/`

Everything here is exactly reproducible: battery = `evaluate --game runs/<g>.json --seed {11,20260707} --agent {utility,dtree}`;
evolution = `evolve --out runs/compare-<agent>-<seed> --seed {401,402,403} --pop 100 --generations 300 --rounds 5 --agent <agent>`.
Agent config in all cases: randomness 0.15, decision interval 8 ticks.

## 1. How the agent got its shape (iteration log)

The designer's instruction was to compare against the DT and iterate on strange
dynamics. Four versions were measured (GameC diagnostics, seed 11):

| version | change | resulting dynamic | verdict |
|---|---|---|---|
| v0 | approach = chase opponent's position | down-attack genomes → whoever holds height farms the other (52 vs 3 hits), 60 s draws | inhuman: agents never seek the position their move hits FROM |
| v1 | approach = seek attack position (opponent − move offset ×1.2, the DT's relMove generalized per move) | both seek height → stacked stun-farm: 162 hits, 962 dmg, no kills | competent but degenerate: no self-preservation |
| v2 | + threat dodge, spacing, damage-scaled evade | over-caution: 2–8 hits per 60 s match | inhuman the other way: nobody commits |
| v3 (shipped) | dodge only when we can't hit back, never mid-swing (trade commitment) | mutual trades, kills, plausible lengths | kept |

## 2. Study-game battery (games A–F, evolved UNDER the DT in 2022)

120 rounds: 6 games × 2 seeds × 5 rounds × both agents.

| metric | decision tree | utility |
|---|---|---|
| median fitness | −6.5 | −22.0 |
| timeout-draw rate | 3% | 55% |
| mean match length | 30.7 s | 49.0 s |
| mean hits/match | 23.3 | 26.6 |
| matches with zero interaction | 0% | 3% |
| one-sided matches (≥5 hits, all by one side) | 40% | 42% |

Per-game median fitness (draw rate):

| game | decision tree | utility |
|---|---|---|
| A | 55.3 (20%) | −3.9 (50%) |
| B | −5.8 (0%) | −36.0 (60%) |
| C | 28.4 (0%) | −6.0 (70%) |
| D | 1.2 (0%) | −38.0 (70%) |
| E | −16.1 (0%) | **+22.0** (30%) |
| F | −16.3 (0%) | −23.4 (50%) |

**Reading.** The study games score lower under utility mostly because *nobody dies*:
the utility agent recovers competently, while a large share of DT-era deaths were the
DT's own bad recovery (origin-homing quirk). Old-game fitness was expected to shift —
these genomes co-evolved with the DT's quirks. Two findings worth keeping:

- **GameA becomes an actual fight.** Under the DT, one character NEVER lands a hit
  (0/335–468 damage). Under utility both characters trade every round (141/112,
  102/186 …) with kills. The utility agent finds attack positions the DT never did.
- **Utility exposes a degenerate-genome exploit.** GameC's evolved knockback terms
  nearly cancel at the attacker's ideal position, so a competent attacker can
  stun-lock a victim indefinitely (observed: 191 hits, 1134 damage, zero deaths).
  The DT's erratic movement masked this; the utility agent plays it like a strong
  human would — and fitness correctly hates it (−909). Under evolution this becomes
  *pressure toward robust games*, arguably an instrument improvement.

## 3. Replicate evolution (pop 100 · 5 rounds · median · 300 gens · seeds 401–403)

| run | best-ever fitness (gen) | avg fitness, last 20 gens | plateau (avg>80 ×20) | champion re-eval, fresh seed |
|---|---|---|---|---|
| dtree-401 | 81.6 (211) | 52.2 | — | 61.3 |
| dtree-402 | 126.5 (88) | 94.7 | gen 121 | 122.7 |
| dtree-403 | 105.5 (48) | 72.6 | — | 105.5 |
| utility-401 | 107.3 (208) | 55.5 | — | 67.3 |
| utility-402 | 106.3 (55) | 49.5 | — | 62.5 |
| utility-403 | 95.8 (155) | 37.2 | — | 70.8 |

(Paper/Phase-5 DT reference: best-ever 109–146; plateau reached in 1 of 3 Phase-5
replicates.)

**Reading.**

- **Top-end quality holds.** Utility champions reach 96–107 best-ever, inside the
  DT band. Their matches look *better* than DT-era champions on the axes the paper
  cared about: re-evaluated on a fresh seed, 14 of 15 champion rounds end in a
  kill (1 draw), lengths 21–54 s around the 45 s target, and **both players land
  hits in every round** — the one-sided-farming motif largely disappears.
- **Population averages run lower and the paper's plateau criterion is not hit.**
  Two causes, both structural: (a) the utility instrument punishes fragile genomes
  harder (timeout draws → big negative fitness), keeping the population mean down;
  (b) the instrument is stochastic (randomness 0.15), so a genome's measured fitness
  is noisy — best-ever ~107 re-evaluates to ~62–71 median on fresh seeds. The DT-era
  plateau threshold (avg > 80) is calibrated to a noiseless instrument and should be
  recalibrated (or randomness lowered) if plateau detection matters to a study.
- Determinism is intact: parallel==serial, resume, and replay tests all pass with
  the utility agent as default, and the new utility golden hash joins CI.

## 4. Head-to-head (curiosity — NOT a fitness-valid configuration)

10 matches per cell, both position assignments, on GameA / GameC / both 402 champions.
Result: outcomes are dominated by which CHARACTER the agent pilots, not which agent
pilots it (e.g. GameA char-0 farms char-1 under either agent). The signal that does
come through: the utility agent *survives* farming positions the DT dies in (draws
instead of losses) and converts winning positions more reliably on the evolved-under-
utility champion (9/10 wins vs DT's 1/10 in the mirrored seat).

## 5. Clips

- `runs/media/utility402_best_seed5.mp4` — utility-evolved champion, utility agents.
- `runs/media/gamec_seed11_new-controls.mp4` — GameC under the DT (previous feature),
  for side-by-side feel.

## 6. Recommendations

1. Adopt the utility agent as instrument (already the default). Archive the DT after
   the designer play-test, keeping the DT golden test as a historical canary.
2. Treat the old plateau criterion as DT-era calibration; for utility-era studies,
   either recalibrate the threshold against these replicates or sweep
   `--agent-randomness` (0 = noiseless) when measuring convergence.
3. The stun-lock exploit is a GAME flaw the instrument now surfaces; consider a
   future fitness term or mechanic (knockback floor, stun cap) if the designer wants
   it impossible rather than just fitness-punished.
4. Behavior weights are v1 constants in `UtilityAgent` — feel iteration belongs in
   the designer play-test, ideally vs-CPU (the CPU is now the utility agent).
