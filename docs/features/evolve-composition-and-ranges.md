# Feature: Evolve-menu composition control + advanced ranges (Phase 6, feature 7)

Designer request 2026-07-14, clarifications same day:

- **Composition modes**: PINNED (today's fixed attack/attack/shield/dash layout),
  RANDOM (each button draws any move type), PER-BUTTON (a per-button choice of
  attack / shield / dash / random — the "in between").
- **Fully evolvable**: in non-pinned modes the move TYPE is a structural gene that
  mutation can re-roll during the run (not just generation-time diversity).
- "Jumps" in the request meant DASHES — jump keeps its dedicated button.
- **Advanced menu**: per-parameter generation ranges are user-adjustable and
  UNRESTRICTED (may exceed the tested valid domains), with a visible warning state;
  clamping = min == max.
- Also: pre-v4 runs and demo jsons archived to `archive/` (see archive/README.md).

## Composition model

`GenerationConfig.ButtonComposition: SlotSpec[4] | null`, SlotSpec ∈
{Attack, Shield, Dash, Random}.

- **null (default) = PINNED**: the legacy path, byte-identical — MovesPerCharacter
  attacks + shield + dash slots, permutation mapping gene over the first three
  buttons, dash pinned to the last. The population fingerprint golden must not move.
- **Composed (non-null)**: exactly ONE move per button, `buttonMoves` = identity
  (the mapping gene is meaningless when every button owns a slot — "which type sits
  on which button" evolves directly). Slot i's type: fixed by spec, or drawn
  uniformly from the three types when Random.
- RANDOM mode = [Random, Random, Random, Random]; PER-BUTTON = any mix.

### Genetic ops (all new RNG draws gated to composed configs — pinned streams intact)

- **Crossover**: the mismatched-type whole-slot rule already existed
  (GameGenomeOps 2026-07-12): same-type slots single-point-cross their params;
  mismatched slots coin-flip the whole move from one parent. Composed configs skip
  the button-mapping crossover (identity is a structural invariant).
- **Mutation**: after the all-or-none game roll, each RANDOM-SPEC slot first rolls
  against `GenerationConfig.TypeRerollRate` (default 0.2): success regenerates the
  slot wholesale (uniform type draw + fresh params + sprite from the new type's
  schema — a re-roll may land the same type: that's a legitimate full re-roll);
  failure mutates params as usual. Pinned-spec slots never roll (no real choice —
  the RNG-gating principle). Buttons stay identity.
- **Zero-attack characters are expected** in RANDOM mode (P ≈ (2/3)^4 ≈ 0.20) and
  legal: agent AttackTarget already falls back to opponent position, MoveEvenness
  guards zero totals, GenomeDistance scores mismatched slots as full-distance dims.
  Multiple shields/dashes share the existing single-state machinery (one shield up
  at a time; ONE air dash per airtime regardless of how many dash slots — documented
  budget semantics). Fitness may read oddly on degenerate comps (moveMix's min-use
  term) — fitness shaping is deliberately untouched this feature; designer will
  observe first.

### Serialization

game.json needs NO format bump — v4 already expresses per-move types and any
buttonMoves. run.json (additive, stays formatVersion 1): `composition`
(["attack",...] | absent = pinned), `typeRerollRate`, `rangeOverrides`
([{schema,key,min,max}]). Resume rebuilds the GenerationConfig from these; old
checkpoints read as pinned/no-overrides.

## Advanced ranges

`RangeOverride(Schema, Key, Min, Max)` + `GenerationConfig.WithRangeOverrides(...)`
rebuilds the four ParamSchemas (character/move/shield/dash) with substituted
generation ranges. Because ParamSets carry their schema, generation AND mutation
re-rolls honor the custom ranges automatically, and `RequireSameSchema` holds
because every genome in a run binds to the run's single schema instances (resume
already threads config.Generation through population loading).

- **Unrestricted**: user ranges may exceed the spec's valid domain. To keep the
  invariant "valid domain ⊇ generation range" (so Validate() stays coherent), the
  override widens ValidMin/ValidMax to include the user range. The UI shows an
  OUTSIDE-TESTED-DOMAIN warning on such rows (designer-chosen tradeoff).
- **Clamp** = min == max (GenomeOps.NextFloat(x,x) = x; GenomeDistance skips
  zero-width dims).
- Scope: the four ParamSchemas. Stage generator knobs (platform count etc.) and
  stocks/moves-per-character stay code-side for now.

## UI (EvolveView)

- COMPOSITION option row: PINNED / RANDOM / PER-BUTTON; PER-BUTTON reveals four
  per-button type dropdowns (ATTACK/SHIELD/DASH/RANDOM).
- ADVANCED button toggles a scrollable right-side panel (swaps with the chart):
  one section per schema, one row per parameter — min/max spinboxes (unbounded),
  amber row highlight + warning label when outside the tested valid domain, edited
  rows marked, RESET ALL. Settings feed GenerationConfig at START RUN and are
  recorded in run.json.
- CLI parity: `evolve --composition pinned|random|<a,b,c,d>` and repeatable
  `--range schema.key=min:max`, `--type-reroll <f>`.
- BRAWLER_AUTOEVOLVE gains `composition=`/`range=` for headless verification.

## Tests (~18) and gates

Composed generation honors specs / identity buttons / Random draws all types;
default pinned path byte-identical (fingerprint golden UNCHANGED — the no-re-pin
proof); composed determinism; crossover mismatch + identity invariants; mutation
type re-roll rate + pinned slots never flip + re-rolled params validate under the
new schema; zero-attack & all-shield & all-dash genomes run matches
deterministically; range overrides honored in generate AND mutate; valid-domain
widening; clamp min==max; run.json round-trip + resume honoring composition and
overrides; UI screenshot (composition row + advanced panel + warning state).
Designer play-test gate: run a RANDOM-composition evolution from the menu and
inspect champions.

## Shipped (2026-07-14, pending designer play-test)

Everything above landed; findings:

- **11 feature tests** (fixed specs honored + identity buttons; Random slots draw all
  three types; composed determinism; crossover wholesale-inheritance on mismatch;
  mutation type re-rolls at rate 1/0 with fixed slots never flipping; all-shield and
  all-dash genomes play deterministic matches with finite v3 fitness; range overrides
  shape generation AND mutation; clamp exactness + distance safety; min>max rejected;
  run.json round-trip + actual resume). Suite 214/214. **No golden re-pins** — the
  pinned default path is byte-identical by construction and the population fingerprint
  proves it.
- **CLI verified live**: `--composition random` (2×300-gen smokes) and
  `--range "character.maxGroundSpeed=6:6;move.knockbackScalar=0:40"` — the clamp held
  through 20 generations of mutation (every genome exactly 6) and the widened range
  was searched (knockbacks up to 39.7 vs the stock max 25), both recorded in run.json.
- **Smoke findings** (runs/comp-rand-1001/1002, chart
  runs/media/charts/composition-trajectories.png): random composition plateaus HIGHER
  than pinned (~76 vs ~64 top) — freed from the mandatory shield slot that
  shield-blind fitness never rewards, evolution sheds it. The two seeds converge to
  DIFFERENT compositional motifs — 1001: 73% attack / 25% dash / 2% shield with an
  asymmetric champion (4 attacks vs 2 attacks + 2 dashes); 1002: 62% attack / 38%
  shield / 1% dash — composition is a real axis of the design space with multiple
  attractors. Cross-run fitness comparisons are only valid within a composition +
  range setting (recorded per run).
- UI verified headless (BRAWLER_AUTOEVOLVE gained `composition=`/`advanced=`):
  runs/media/shots-ffcdi/evolve-composition.png (per-button row) and
  evolve-advanced.png (ranges panel).
- Also this session: pre-v4 runs and demo jsons moved to `archive/` (README catalog;
  gitignored except the README).
