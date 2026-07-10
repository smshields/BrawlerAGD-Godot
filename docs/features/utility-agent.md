# Feature: Utility Agent (Phase 6, feature 2)

Designer decisions recorded 2026-07-09. This replaces the decision-tree playtester as
the fitness instrument — a deliberate research pivot, not a port decision. The
DecisionTreeAgent remains in the codebase ONLY for the comparison study and the
cross-platform golden tests; once the designer confirms the utility approach, it gets
archived (kept in git, removed from the application proper).

## Designer's brief

Utility-based agent: score candidate actions with utility functions, **normalize to 1
for selection**, and select with a **randomness factor** (never pure argmax, never
pure worst-avoidance). Initial functions replicate the current agent's *intent* but
select over **inputs** (the InputFrame vocabulary). Requirements:

1. Over a gap → prioritize reaching a platform within current movement capability;
   if unreachable (doomed) → prioritize hitting the opponent before dying.
2. Far from the opponent → close distance to deal damage.
3. An attack that can hit → use it (must account for MULTIPLE available attacks —
   the feature-1 button→move mapping).
4. High damage + at risk of dying → play evasively, avoid the opponent, but still
   attack when the opponent is in range.

Extensible for future behaviors: shielding, projectiles, dashes.

## Designer decisions (AskUserQuestion, 2026-07-09)

- **Instrument policy:** utility agent is the DEFAULT everywhere immediately (CLI,
  evolution engine, Godot app). Full pivot; DT archived after confirmation.
- **Randomness:** per-run agent config (constructor parameter, recorded in run.json).
  Architecture must allow a future per-character genome parameter, but do NOT add the
  genome param now and do NOT make it part of default testing.
- **Decision cadence:** configurable interval — lower limit every tick (1), upper limit
  ~human reaction. Multiple inputs must be selectable together (movement + jump +
  attack in one frame).
- **Comparison:** all three — replicate evolution study, study-game battery,
  head-to-head DT-vs-utility matches — delivered as a report.

## Architecture

`BrawlerSim/Agents/UtilityAgent.cs` + `AgentConfig.cs`.

- **Channels, not one action list.** Each tick's InputFrame is composed from three
  independently-selected channels (extensibility = add a channel or a behavior):
  - horizontal: {left, neutral, right}
  - jump: {no, yes}
  - attack: {none} ∪ {lowest button per DISTINCT usable move}
  Vertical stays 0 (nothing reads it yet).
- **Behaviors** (fixed, ordered array — deterministic): Recover, Doomed, Approach,
  Attack, Evade. Each contributes non-negative scores to any channel. New mechanics
  (shield/dash/projectile) = new behavior and/or new channel.
- **Normalization:** per channel, scores are divided by their sum (all-zero → uniform).
  This is the designer's "values always normalize to one for selection".
- **Selection (randomness r ∈ [0,1]):** per channel, with probability (1−r) take the
  argmax (ties → lowest index); with probability r sample proportionally to the
  normalized utilities. r=0 is pure argmax; r=1 is fully proportional (the worst option
  keeps exactly its normalized share — "don't always avoid worst"). No transcendental
  math (cross-platform determinism; mixture instead of softmax is deliberate).
- **Commitment window:** a decision holds for `DecisionIntervalTicks` (default 8 ≈
  133 ms; valid 1..15 where 15 ≈ 250 ms human reaction). Early re-decision on salient
  events: got stunned, grounded changed, over-pit changed, or an attack newly able to
  hit. Jump and attack are emitted as single-tick presses (edges) on the decision tick;
  horizontal is held for the window. This is deliberately human-shaped — it does NOT
  reproduce the DT's level-based instant jump chaining.
- **RecoveryTicks** stat increments every tick the Recover behavior is active
  (over-pit), analogous to the DT's counting, so the research stat stays populated.
  (`standard-v2` does not read it; no fitness coupling.)
- **Determinism:** seeded Pcg32 per agent instance; draws only on decision ticks
  (mixture branch + optional categorical sample per channel); arithmetic only —
  no Pow/Exp/trig.

## Config & recording

- `AgentConfig { Kind, Randomness = 0.15, DecisionIntervalTicks = 8 }` with
  `CreateSource(Pcg32)`; `EvolutionConfig.Agent` (default utility);
  run.json records kind + knobs. Old run.json files have no agent fields → resume as
  **DecisionTree** (the instrument that actually produced those generations).
- CLI: `--agent utility|dtree`, `--agent-randomness`, `--agent-interval` on
  evolve/evaluate; bench + ArenaView (Watch AI, vs-CPU opponent) default to utility.
- Genome-parameter hook for randomness: `AgentConfig` is per-instance, so a future
  feature can construct per-player configs from genome params without structural change.

## Initial utility functions (v1 — constants documented in code)

Let d = |self − opponent| (horizontal-weighted), reach = per-move hitbox-overlap test
at current position with facing toward the opponent.

- **Recover** (over pit, platform reachable): horizontal toward nearest reachable
  platform point 3.0; jump 2.0 when platform not below-and-behind fall line or to stall
  descent (air jump available). Reachability: coarse ballistic budget — horizontal
  distance coverable at MaxAirSpeed during remaining fall time (+jump allowance).
- **Doomed** (over pit, nothing reachable): horizontal toward opponent 2.0; attack
  channel: any move that can hit 3.0.
- **Approach** (not over pit): horizontal toward opponent scaled by min(d/8, 1) × 1.5;
  jump 1.0 when the opponent is >1.5 above SELF (relative — the DT's absolute-y quirk
  is intentionally NOT carried over; that quirk existed to be preserved, and this pivot
  is the sanctioned break point) or when approaching a grounded edge (DT-style probe).
- **Attack** (always): each distinct move that can hit contributes 4.0 to its button;
  "none" keeps a 0.5 baseline so out-of-range ticks don't attack-spam.
- **Evade** (Damage ≥ 80, the "high damage" default): horizontal away from opponent
  2.0, but toward stage center when the away direction walks off the platform edge;
  attack contributions stay live (req 4).

These weights are v1 calibration targets — the comparison study is the feedback loop.

## Testing & acceptance

- Unit: normalization sums to 1; r=0 → argmax; same-seed bit-determinism; scenario
  tests with hand-placed players (over pit → moves toward platform; doomed → attacks;
  far → approaches; high damage → evades; multi-move genome → presses the button of
  the move that can actually hit).
- Integration: matches terminate; parallel==serial; replay==live; a NEW utility-agent
  golden match hash joins (not replaces) the DT golden.
- Comparison study (the designer's acceptance evidence): study-game battery (A–F ×
  seeds × both agents), 3-seed replicate evolution per agent (pop 100, rounds 5,
  median, 300 gens), head-to-head DT-vs-utility, written up as a report with stats
  tables, trajectories, and clips. Undesirable/inhuman dynamics → iterate on
  behaviors/weights before the designer gate.

## Shipped (2026-07-09, pending designer play-test)

- `UtilityAgent` + `AgentConfig` (`Kind`, `Randomness`, `DecisionIntervalTicks`);
  utility is the default instrument in EvolutionEngine, CLI (`--agent utility|dtree`,
  `--agent-randomness`, `--agent-interval`), Bench, and ArenaView (Watch AI + vs CPU).
- run.json records the agent config; pre-feature checkpoints resume as DecisionTree.
- Behaviors shipped (v3 after three measured iterations — see the report §1):
  Baseline, Recover (reachability-gated), Doomed, Approach (attack-position seeking),
  Attack (per-distinct-move), Evade (damage-scaled), ThreatDodge (with trade
  commitment), Spacing. Weights are documented constants in `UtilityAgent`.
- 12 new tests + utility golden hash (GameC, seeds 20260709/0-1, macOS ARM64);
  127 tests green at ship; engine determinism suite passes under the new default.

### Behavior log (post-ship instrument changes — each re-pins the utility golden)

- **2026-07-10 FlankBehavior** (designer-reported stall): characters separated
  vertically by a platform paced back and forth — the approach target's X flips sign
  around the opponent while the route is blocked, and the below character wasted jumps
  on the platform's underside. Now: when |Δy| > 1.5 and a platform surface lies
  between the two heights across their horizontal span, the agent heads for that
  platform's edge (weight 2.5 > approach's 1.5), preferring an edge with ground
  beyond it (probe 0.75 past the edge; an unsafe-only flank runs at half weight —
  the designer's stay-recoverable constraint), and approach's jump-at-target is
  suppressed while blocked. Regression tests: UtilityAgentFlankTests (stall
  reproduced pre-fix). Golden re-pinned 3417322836374644188.
- **2026-07-10 TelegraphDodgeBehavior** (designer: agents should use jumps to escape
  attacks): when the opponent is in WarmUp and their move's arc (+1 margin) covers us
  and we cannot hit back and we're not mid-swing, jump (2.0) + move away (1.0). Makes
  jumping a live defensive tool; with the fitness jump reward, evolved jump-force
  genes recovered. Tested: TelegraphedSwingsAreDodgedWithAJump. (Goldens unchanged by
  the behavior itself — GameC's pinned match never aligns the trigger — but re-pinned
  the same day for the 0.25 s stun cap.)
- Comparison study delivered: docs/reports/2026-07-09-utility-agent-comparison.md
  (+ battery CSV, runs/compare-*, clips in runs/media/).
- DEVIATIONS.md #18. DT archival deferred until designer confirmation.
