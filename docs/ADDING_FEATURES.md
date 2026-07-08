# Adding New Gameplay Features — the Pattern

Every new mechanic (moves, tiles/hazards, game modes, projectiles, N players…) follows
this sequence. The design questions in CLAUDE.md come first; this is the implementation
order once they're answered. Steps marked ⛔ are gates — do not proceed past them with
failures.

## 0. Design record

Write the designer's answers to the CLAUDE.md questions into
`docs/features/<feature-name>.md` (create the folder on first use): scope, bounds,
parameterization decision, agent impact, asset needs, serialization plan, rollout plan.
This is what future sessions read to understand why the feature is shaped the way it is.

## 1. Schema first (if parameterizable)

- Add keys to the relevant `*Params` constants class and `DefaultSchemas` (or a new
  schema for a new entity type). **Append only** — order is crossover semantics.
- Each `ParamSpec`: generation range from the designer's bounds (Q2); set
  `ValidMin/ValidMax` only if post-generation rules legitimately widen the domain
  (see knockbackModX precedent in DEVIATIONS.md §13).
- New entity lists (e.g. more moves) go through `GenerationConfig` counts so the genome
  stays variable-size-ready.
- ⛔ Tests: extend the schema-pinning tests (ranges are regression-locked), round-trip
  serialization, crossover/mutation stay in range.

## 2. Sim implementation (`BrawlerSim/Sim/`)

- Resolve genome params to tick-domain runtime values in a `Sim*` type (see `SimMove`):
  durations via `MatchConfig.ToTicks`, geometry into `Aabb`/`Vec2`.
- New entities (projectiles etc.): flat arrays/lists on `SimWorld`, **fixed iteration
  order**, spawned/despawned deterministically. Extend `SimWorld.StateHash()` with ALL
  new mutable state — an unhashed field is an invisible determinism hole.
- Slot the behavior into the documented tick order (input/FSM → physics → contact →
  hits → deaths). If the order must change, update the `SimWorld` doc comment and note
  it in DEVIATIONS.md.
- FSM changes: new `PlayerState` values need a tint (aesthetics), an entry in the state
  tests, and explicit decisions for stun/grounding interactions.
- Constants go in `MatchConfig` with a doc comment stating their provenance/tuning
  intent — never inline magic numbers.
- ⛔ `dotnet test` green except goldens; determinism tests must pass unmodified.

## 3. Stats & research data

- Add per-player counters to `SimPlayer` and surface them through
  `PlayerStats`/`MatchResult` if the designer wants the data (Q7).
- Fitness: if the feature needs fitness pressure, create a NEW `IFitnessFunction`
  class with a new version name; `standard-v2` is frozen for comparability.

## 4. Agent (only if Q3 says so)

- `DecisionTreeAgent` changes alter what fitness measures. Implement exactly what the
  designer specified, add a DEVIATIONS.md entry ("agent behavior vs AIIDE '22"), and
  prefer additive branches over restructuring the ported tree.
- Consider whether the old behavior should remain available (constructor flag) for
  comparison studies.

## 5. Tests for the feature itself

- Unit tests with hand-computed values for every formula/threshold.
- A bounds test proving Q2's limits hold under adversarial inputs (max knockback, max
  speeds, spam inputs).
- An integration probe: AI-vs-AI matches on generated genomes exercising the feature
  still terminate, stats stay sane.
- ⛔ Re-pin goldens LAST, once behavior is final, with a dated comment.

## 6. Serialization & compatibility (Q8)

- New params flow through `game.json` automatically via the schema (unknown keys are
  ignored on load; missing keys THROW). Decide explicitly:
  - schema append + `LegacyImporter`/loader default for old files, or
  - `GameGenomeJson.CurrentFormatVersion` bump + migration for structural changes.
- If `InputFrame` gains fields, update `InputTraceJson` with backward-compatible
  reading (old traces must still replay bit-exactly — they are research artifacts).
- ⛔ Games A–F importer tests and existing-run loading must still pass.

## 7. View layer (`godot/`)

- Sprites: reuse the Kenney sheets via `SpriteBank`; new slices → extend the
  `*_slices.json` (extraction precedent in git history). Respect state tints, ALL-CAPS,
  dark-background minimalism.
- Rendering reads sim state only — no gameplay logic, no Godot physics, ever.
- New inputs: register in `Boot.RegisterActions` (keyboard both players + both pads)
  and update the menu hint text.
- ⛔ Visual verification: automation-env screenshots at meaningful ticks, LOOK at them,
  and record an mp4 for the designer (`tools/record-match.sh`).

## 8. Docs & goldens

- DEVIATIONS.md entry if the feature or its agent handling changes anything about how
  results compare to the paper.
- Feature doc from step 0 updated with what actually shipped.
- Golden hashes re-pinned with dated justification (if gameplay changed).

## 9. Designer test gate ⛔

Deliver: what changed, test count, screenshots/mp4, and the exact way to try it
(controls, which game/menu). The feature is NOT complete until the designer confirms.
Expect feel iteration — tuning knobs should already be named `MatchConfig`/schema
values so iteration is a constant change, not a refactor.

## Worked precedents in git history

- **Schema + valid-domain nuance:** knockback params (Phase 1, commit `50bbab2`).
- **Sim entity + FSM + tests:** the whole of Phase 2 (`e86ecd0`).
- **Agent-faithful port with quirks ledger:** `DecisionTreeAgent` (same commit).
- **Contact physics iteration driven by designer play-testing:** `901b703` → `b838318`
  (including a wrong first fix — read both messages; the lesson is prevention vs
  cleanup are different jobs).
- **View feature with visual verification:** tiles (`753a454`), window sizing (`4a6981f`).
