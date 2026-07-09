# Behavioral Deviations from the Unity BrawlerAGD (AIIDE '22)

The port preserves the Unity build's *design-space semantics* — same parameters, same
ranges, same FSM rules, same AI playtester decision tree, quirks included. This page is
the complete ledger of **intentional differences**, so results produced with this system
can be interpreted against the published AIIDE '22 findings. Anything not listed here is
either bit-parity by construction or an unintentional bug (please report it).

Bottom line for comparability: **fitness scores from this system are NOT directly
comparable to the paper's numbers** (items 1–2 alone change the fitness landscape).
Within this system, all results are exactly reproducible from (genome, seed).

## Fitness function (`standard-v2`)

| # | Change | Unity behavior | Why it matters |
|---|--------|----------------|----------------|
| 1 | Stock-fairness term computes \|s1 − s2\| | `Math.Abs(s1 - s1)` ≡ 0 — the term was a constant 3 and never influenced selection | Evolution now actually rewards stock-even matches. Published runs evolved without this pressure. |
| 2 | Damage cap counts total stocks lost `(6 − s1 − s2)` | Sign error `(6 − s1 + s2)` inflated the cap whenever P2 had stocks left | The anti-"corner grinding" penalty now engages as designed; high-damage no-kill games score lower than they did in the paper's runs. |

## Simulation & physics

| # | Change | Unity behavior | Why it matters |
|---|--------|----------------|----------------|
| 3 | Fixed 60 Hz tick; all durations in integer ticks | Gameplay logic ran per rendered frame (variable), physics at 50 Hz, move timings on wall-clock coroutines | Removes frame-rate dependence entirely; matches are a pure function of (genome, inputs, seed). |
| 4 | Blast zone fixed to 16:9-equivalent constants (±10.76 × ±5.5) | Sized from the camera at runtime — **depended on the player's window aspect ratio** | Every match plays in the same arena, headless or rendered, any monitor. |
| 5 | Movement clamp fixed | Tapping the opposite direction snapped a character to full speed the other way; self-movement input could bleed off knockback speed above the cap | Fixes Unity defect #4 (plan §1). Movement feels crisper; knockback momentum is preserved while held toward. |
| 6 | Being hit cancels an in-flight move | The move coroutine kept firing state changes after a stun (race), occasionally re-entering attack mid-stun | Deterministic FSM; standard fighting-game semantics. |
| 7 | Leaving the ground mid-move no longer aborts the move state | `OnCollisionExit2D` yanked state to Air regardless of current state, racing the move coroutine | Moves run warm-up → execute → cool-down to completion, then resolve by grounded/jumps. |
| 8 | Single clean hit path with 6-tick invincibility | Damage logic duplicated across Enter/Stay/Exit trigger events with inconsistent stat counting (double-count risk) | One hit = one damage application = one stat increment, always. |
| 9 | Solid player-vs-player contact: movement clamps at the opponent's body with mass-weighted momentum transfer; residual overlaps separate ≤0.05 u/tick | Box2D iterative contact solve (soft, impulse-based) | Same emergent behavior (push, stand on heads, no pass-through, no teleports) via a deterministic model. Contact stiffness is tunable (`MatchConfig.MaxDepenetrationPerTick`). |
| 10 | Ground detection: feet-on-platform-top test | Raycast + collision-event patchwork; documented Unity bug: jumps refreshed when a platform was above AND below | Fixes Unity defect #5. |
| 11 | "3 stocks = 4 lives" **preserved** (not a deviation — noted to prevent "fixing") | `respawn()` ends the game only when dying at 0 stocks | Matches the shipped game and the study's four-stock survival description. |

## Generation & genome

| # | Change | Unity behavior | Why it matters |
|---|--------|----------------|----------------|
| 12 | Stage generator sizes Above-children parent-relative | `rand.Next(2, platform.xSize - x + 1)` used **absolute** x — children of negative-x parents could exceed the parent width and the design-space max | Generated stages stay within the intended design space. Imported legacy stages are untouched (data loads verbatim). |
| 13 | Knockback params: generation range vs valid domain split | Unity re-saved post-flip/post-constraint values silently outside the generation range | Formalized: `knockbackModX/Y` generate in [0,1]/[−1,1] but validate in ±1.5 (the constraint lerp's convex hull). Discovered from the study games' own files. |
| 14 | Move 2 / shield not ported | Half-implemented: parry/reflect fields no code read, shield break did nothing | Per designer decision; returns later as a schema-driven move type. |

## Determinism & evaluation

| # | Change | Unity behavior | Why it matters |
|---|--------|----------------|----------------|
| 15 | All RNG seeded (PCG32; per-match streams derived from run seed) | GA time-seeded; AI used a second, unseeded RNG | Any match or whole run is exactly reproducible; every fitness score is replayable from its input trace. |
| 16 | Evaluation is parallel and order-independent | Sequential, real-time, one Unity scene at a time (~12 h/run) | ~50,000× faster per core; replicate studies are practical (minutes). Parallelism provably does not change results (tested). |

## Controls & input (2026-07-09, docs/features/multi-move-controls.md)

| # | Change | Unity behavior | Why it matters |
|---|--------|----------------|----------------|
| 17 | Input model: 4 assignable action buttons + captured (inert) vertical axis; genome gains a button→move mapping gene; keyboard scheme is WASD + Space + IJKL, P2 is gamepad-only | One attack button (P1: S, P2: K); W/I jumped; no button assignment concept | Foundation for evolving multiple moves per character. The agent's DECISIONS are unchanged — it presses the lowest button mapped to move 0 instead of "attack" — and for single-move genomes (everything the paper studied) behavior is provably identical: pre/post evaluation of Games A/C/F × 2 seeds reproduced all 30 rounds' stats exactly, and genome-generation RNG streams are untouched (button genes consume RNG only when >1 move exists). Trace format grew 3→7 values/player (old traces load and replay bit-identically); game.json is formatVersion 2 (v1 loads with all-buttons→move-0). When multi-move genomes arrive, agent move SELECTION will be new instrument behavior and gets its own entry. |

## Instrument change (2026-07-09, docs/features/utility-agent.md)

| # | Change | Unity behavior | Why it matters |
|---|--------|----------------|----------------|
| 18 | **The fitness instrument is now the UtilityAgent**, replacing the ported decision tree as the default playtester everywhere (evolution, CLI, app). Channel-based utility scores normalized to 1, argmax/proportional selection mixture (randomness 0.15), 8-tick decision commitment, attack-position seeking, threat dodge with trade commitment, damage-scaled evasion, reachability-gated recovery. Per-run config recorded in run.json; pre-2026-07-09 checkpoints resume under the DT. | The AIIDE '22 decision tree (level-held inputs, absolute-y checks, origin-homing recovery) | **Fitness scores under the two instruments are not comparable** — a deliberate research pivot, designer-directed. Full comparison: docs/reports/2026-07-09-utility-agent-comparison.md. Notable consequences: DT-era deaths often came from the DT's own bad recovery; the utility agent recovers competently, so fragile genomes time out (punished by fitness) instead of self-destructing; degenerate genomes (knockback-cancel stun-locks) are now exploited and heavily punished rather than masked by agent noise; the instrument is stochastic, so measured fitness carries sampling noise (median-of-rounds mitigates). The DT remains in-tree for comparison and its golden test; archival planned after designer confirmation. |

## Known intentional quirk preservation (unchanged, for the record)

- AI playtester ported verbatim including: absolute-Y target comparisons, level-based
  jump input (instant ground→air jump chaining), 20×15 platform sense box, recovery
  homing toward world origin when no platform is sensed, recovery-tick stat counting.
- The 45°-knockback flip that produced the paper's "backwards knockback" player
  confusion is preserved (changing it is a design decision, not a port decision).
- Move hitboxes inherit the owning character's scale (big characters swing bigger).
- Single-point crossover semantics (point 0 = full copy of parent B), 5 mutation
  re-rolls **with replacement**, all-or-none whole-game mutation roll, stage mutation =
  full regeneration.
