# Feature: Second Move (Phase 6, feature 3)

Designer brief, 2026-07-10: add a second attack per character on a different button,
using the existing move structure but genuinely different from the first; consider a
MILD fitness term for an even move split (without over-opinionating builds — surprise
is a goal); the utility agent selects the best move among those that can currently
hit; and cap move stun output, choosing the cap from a few experimental values,
because high-scoring games exploit stun-locking.

Feature 1 (multi-move-controls.md) built the rails: MovesPerCharacter, the ButtonMoves
gene, per-move agent plumbing, and trace/serialization support. This feature turns the
count to 2 and fills the gaps.

## Design decisions (the 10 questions, deltas only)

1. **Parameterization**: `GenerationConfig.MovesPerCharacter` default 1 → 2. Both
   moves are independently generated `MoveGenome`s (independent params ⇒ "different
   from the first" without hand-constraints; the design space stays unopinionated).
   **Button coverage guarantee**: every move must be reachable — after generation,
   mutation, or crossover of `ButtonMoves`, any move index with no button is assigned
   deterministically to the button of its own index (`buttonMoves[m] = m`). The
   designer's "mapped to a button that is different from the first" is thereby
   structural: with 2 moves and 4 buttons, both moves always have ≥1 distinct button.
2. **Bounds**: existing move-param ranges; nothing new.
3. **Agent**: AttackBehavior ranks moves that can currently hit by DAMAGE — utility
   4.0 + DamageGiven/10 per hitting candidate — so "best move that can hit" wins the
   attack channel; ties break to the lower move index (deterministic). Instrument
   change → utility golden re-pin (behavior log in utility-agent.md).
4. **Assets**: none — each move already carries its own sprite gene.
5. **Entities/state**: none new. `CurrentMoveIndex` already hashed (feature 1).
6. **Controls**: unchanged (feature 1's four buttons); mapping gene now meaningful.
7. **Research data**: `PlayerStats.MoveUses` (per-move start counts) and
   `PlayerStats.StunTicks` (total ticks spent stunned) — both stats-only.
   Fitness: `standard-v3` gains a `moveMix` term (same-day amendment):
   weight × evenness per player, evenness = 1 − |u₀−u₁|/(u₀+u₁) over move-use counts
   (0 when a player never attacks). Default weight 5 → max +10 total, a NUDGE next to
   ±100-scale fitness, per the don't-over-opinionate instruction. `--move-mix-weight`
   on the CLI, recorded in run.json.
8. **Serialization**: none — feature-1 formats already carry N moves + buttonMoves.
   Legacy 1-move games load and play unchanged (validated by existing tests).
9. **Rollout**: 2 moves is the new generation default (evolution searches it
   immediately). Population golden fingerprint re-pins (dated) — generation now draws
   RNG for the second move and button genes, exactly the gated path designed in
   feature 1.
10. **Readability**: PlayerView now shows the CURRENT move's sprite during attacks
    (was: always move 0's sprite) — the two attacks are visually distinct via their
    sprite genes.

## Stun cap (experimental)

`MatchConfig.MaxStunSeconds` clamps the post-hit stun duration (`TryHit`). Gameplay
change → all match goldens re-pin when a non-infinite default lands. Values compared
experimentally (evolution runs + StunTicks stats + champion inspection):
uncapped / 3.0 s / 1.5 s / 0.75 s. Decision criteria: stun-locking no longer dominates
champions (share of match spent stunned), while stun stays a meaningful mechanic.

### Results (2 seeds x 300 gens x 9 rounds each; charts: runs/media/charts/stun-cap-trajectories.png)

| cap | best-ever (501/502) | avg last-20 | champ re-eval | worst stun share | move evenness |
|---|---|---|---|---|---|
| uncapped | 131.1 / 50.0 | 79.7 / 27.0 | 115.9 / 40.8 | **46%** | **0.05** / 0.87 |
| 3.0 s | 92.0 / 86.6 | 47.6 / 46.0 | 70.2 / 60.8 | 38% | 0.79 / 0.78 |
| 1.5 s | 85.7 / 101.7 | 49.0 / 53.7 | 55.1 / 66.7 | 29% | 0.20 / 0.60 |
| **0.75 s (chosen)** | 112.1 / 89.2 | 58.5 / 50.2 | 83.9 / 68.6 | **26%** | **0.83 / 0.78** |

**Decision: 0.75 s default** (MatchConfig.MaxStunSeconds). The uncapped table-topper
(131.1) is exactly the exploit the designer flagged: one round has its victim stunned
46% of the match and the champion uses essentially ONE move (evenness 0.05). Under
0.75 s, stun chains still exist (~26% worst-case — the mechanic is alive) but single-hit
locks are bounded; both seeds produced strong, two-move champions (evenness ~0.8) and
the fastest, highest-converging trajectories. Gameplay change → both match goldens
re-pinned (dated). Uncapped remains one flag away (--max-stun) for comparison studies.

## Shipped (2026-07-10, pending designer play-test)

- Two moves per character (GenerationConfig default), independently generated;
  pigeonhole-safe button-coverage guarantee across generation/mutation/crossover.
- Stats: PlayerStats.MoveUses + StunTicks; evaluate prints stun% and per-move uses.
- Agent: damage-ranked selection among moves that can hit (utility golden unchanged
  by this — single-move study games are unaffected; the stun cap re-pinned it).
- Fitness: standard-v3 moveMix term, weight 5 (nudge, not mandate).
- Stun cap: MatchConfig.MaxStunSeconds = 0.75 s (experiment above); --max-stun on
  evolve/evaluate/noise, recorded in run.json.
- View: PlayerView renders the CURRENT move's sprite (the attacks are visually
  distinct). Demo clip: runs/media/twomove_demo.mp4; champion clip:
  runs/media/stun075_501_best.mp4.
- Population fingerprint re-pinned (2-move generation); both match goldens re-pinned
  (stun cap). 148 tests green.

## Follow-up 2026-07-10: stun-lock elimination + jump valuation (designer-directed)

The 0.75 s cap turned out insufficient: 0.1 s invincibility < 0.75 s stun means an
attacker can re-chain indefinitely — the second sweep caught a champion round with a
97%-stunned victim. Changes:

- **standard-v3 `stunLock` term**: −5 × 100 × max(0, stunShare − 15%) per player —
  prices the chains the per-hit cap can't reach (26% → −55, 46% → −155).
- **standard-v3 `jumps` term**: 10 × min(totalJumps, 40)/40 — saturating, so jumping
  matters but spam earns nothing.
- **Agent TelegraphDodgeBehavior**: the opponent's WarmUp is a readable wind-up — if
  their arc (+1.0 margin) covers us and we can't hit back, hop away (jump 2.0,
  move-away 1.0). Committed swings stay planted; trades are still taken.
- **Stats**: PlayerStats.Jumps; evaluate prints jumps per player.
- **Cap re-sweep** (0.75/0.50/0.25 × 2 seeds × 300 gens, new regime; chart:
  runs/media/charts/stunlock-jump-trajectories.png):

| config | best-ever | champ re-eval | worst stun share | jumps/match | jump gene |
|---|---|---|---|---|---|
| old regime 0.75 s | 112.1 / 89.2 | 52.4 / 55.5 | 26% / 21% | 34.6 / 31.4 | 8.66 / 7.98 |
| new 0.75 s | 54.5 / 80.4 | 30.4 / 1.4 | 5% / **97%** | 74.4 / 35.6 | 4.89 / 8.17 |
| new 0.50 s | 63.1 / 76.7 | 49.6 / 71.3 | 14% / 15% | 47.0 / 46.4 | 5.08 / 4.99 |
| **new 0.25 s (chosen)** | 99.7 / 109.7 | 55.1 / 79.6 | **11% / 16%** | 46.6 / 48.4 | **10.00 / 7.21** |

**Decision: MaxStunSeconds = 0.25 s** — stun becomes a flinch that cannot chain past
~16% of a match even in the worst champion round; jumps/match rose ~40% over the old
regime; the mean jump-force GENE in final populations recovered (7.2–10.0 vs ~5 at the
laggard caps — evolution reinvests in jumps when agents and fitness value them);
trajectories match or beat the old regime. Goldens re-pinned again (dated).
