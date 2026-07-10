# BRAWLER AGD — GODOT EDITION

Automated game design research system: a genetic algorithm evolves 2D brawler games
(two characters + their moves + a platform stage), each candidate evaluated by AI
self-play against a fitness function. All code is C#. This is the successor to the Unity
system behind *"Searching for Balanced 2D Brawler Games: Successes and Failures of
Automated Evaluation"* (Shields, Mawhorter, Melcer, Mateas — AIIDE 2022). The Unity repo
at `../BrawlerAGD` is a **read-only reference archive** — consult it for original
behavior, never develop there.

## ROLES

The user is the designer/PM (and the paper's first author). You are the developer.
Ask questions when clarification is needed; do not implement outside the scope of
current work; do not change game mechanics unilaterally. **Nothing is complete until
the designer has play-tested it** — deliver with evidence (tests + screenshots/mp4
recordings), then wait for their confirmation.

## RESEARCH GOALS (keep these in mind for every decision)

- **The core research question:** when does automated evaluation (fitness computed from
  AI playtests) align with human judgment of emergent properties like balance? The
  DecisionTreeAgent is the *measurement instrument* — its quirks are intentionally
  preserved (see docs/DEVIATIONS.md); changing its behavior changes what fitness means.
- **Exact reproducibility:** any result must be reproducible from (genome, seed). Every
  match records an input trace; every fitness score is replayable and watchable.
- **Parameter-driven everything:** new mechanics must be expressible as schema
  parameters so evolution can search them. The genome schema IS the design space.
- **Speed enables new science:** ~1,000 matches/s per core; a paper-scale run (pop 100,
  5 rounds, 300 generations) takes ~25 s. Replicate studies are cheap — use them.

## THE DETERMINISM CONTRACT (inviolable)

1. ALL gameplay lives in `BrawlerSim/` — a pure .NET library with **zero Godot
   references** (CI grep-gates this). The Godot layer only samples input and draws state.
2. One `SimWorld.Tick(inputs)`: rendered play calls it once per physics frame; headless
   evaluation loops it. Same code ⇒ same results, proven by hash equality.
3. In sim code: no wall clock, no `System.Random`, no dictionary-order iteration, no
   `Stopwatch`. Use `Pcg32` (injected, seeded) and `DetMath`. All durations are integer
   ticks at 60 Hz (`MatchConfig.ToTicks`).
4. Golden-hash tests (`Phase1PipelineTests`, `MatchTests.GoldenMatchHashMatches`) are
   cross-platform canaries. A gameplay change may re-pin them ONLY with a dated comment
   stating what changed — never silently.

## ARCHITECTURE MAP

| Path | Contents |
|------|----------|
| `BrawlerSim/Params/` | Schema-driven parameters. `ParamSpec` = key + generation range + optional wider valid domain. Order matters (crossover indexes it) — extend by appending. |
| `BrawlerSim/Genome/` | Genome types, `DefaultSchemas` (the live Table 1), Unity-parity genetic ops, `MoveRules` (derived values), `StageGenerator`. |
| `BrawlerSim/Sim/` | `MatchConfig` (all constants, documented provenance), `SimWorld` (fixed tick order), `SimPlayer` FSM, `SimPhysics`, `MatchRunner`. |
| `BrawlerSim/Agents/` | `DecisionTreeAgent` — the fitness instrument. Quirks documented + intentional. |
| `BrawlerSim/Fitness/` | Versioned fitness functions — `standard-v3` (default, composable terms + per-stock damage shaping), `standard-v2` (frozen). New research fitness = new class via `FitnessRegistry`; run.json records the name and resume honors it. |
| `BrawlerSim/Evolution/` | `EvolutionEngine` (parallel, order-independent), `RunStore` (checkpoints under `runs/<name>/`). |
| `BrawlerSim/Replay/` | `InputTrace` record/playback + JSON. |
| `BrawlerRunner/` | Headless CLI: `evolve / evaluate / replay / import / bench`. |
| `godot/` | View layer ONLY: `ArenaView` (owns SimWorld), `PlayerView/StageView/HudView`, menus (`MainMenu/EvolveView/ManageView`), `Boot` autoload (input map + automation). |
| `docs/CONVERSION_PLAN.md` | Project history, decisions, defect ledger of the Unity original. |
| `docs/DEVIATIONS.md` | Complete ledger of intentional differences vs Unity — REQUIRED READING before touching sim behavior. |
| `docs/ADDING_FEATURES.md` | The step-by-step pattern for new gameplay features. |

## TOOLCHAIN & COMMANDS

- .NET 8 (keg-only): `export PATH="/opt/homebrew/opt/dotnet@8/bin:$PATH"`
- Godot: **`/Applications/Godot_mono.app` (4.7 .NET) ONLY** — `/Applications/Godot.app`
  is 4.6 without C# and will corrupt the project if it opens it.
- `dotnet test` — full suite (~100 tests, <1 s). ALWAYS run before committing.
- `tools/open-editor.sh` — open the editor correctly (sets DOTNET_ROOT).
- `tools/record-match.sh <game.json> [seed] [secs] [out.mp4]` — gameplay recording.
- `tools/export-macos.sh` — playable .app (the deep re-sign step is mandatory).
- Evolution: `dotnet run --project BrawlerRunner -c Release -- evolve --out runs/x --seed N --pop 100 --generations 300 --rounds 5`
- Headless visual verification (window flashes on screen; needed after any view change):
  env vars `BRAWLER_AUTOPLAY=ai:<seed>`, `BRAWLER_GAME=<abs path>`, `BRAWLER_TRACE`,
  `BRAWLER_SHOT[_DIR]`, `BRAWLER_SHOT_TICKS=60,300`, `BRAWLER_TICKS_PER_FRAME=10`,
  `BRAWLER_QUIT_AFTER=<s>`, `BRAWLER_SCENE=evolve|manage`, `BRAWLER_AUTOEVOLVE=...`.
  After adding assets: `Godot --path godot --headless --import` once.
- Git: commit locally with clear messages; **do NOT push** — remote
  `smshields/BrawlerAGD-Godot` isn't created yet and pushing awaits designer go-ahead
  (first push also fires the cross-platform CI canaries — review them when it happens).

## AESTHETICS

- Kenney 1-bit monochrome pack, sliced EXACTLY as Unity did (`godot/assets/*_slices.json`
  extracted from the Unity .meta files) — a genome's spriteIndex must render the same
  glyph as the original.
- Minimalist line-art on dark background `(0.09, 0.09, 0.12)`; white platform tiles
  (LevelLoader 9-slice Q/W/E/A/S/D/Z/X/C); no skeuomorphism; abstraction over detail.
- Player state tints (Unity-verbatim): Idle white · Air green · JumpsExhausted grey ·
  WarmUp yellow · Attack red · CoolDown blue · Stun magenta. These ARE the game's
  readability system — new states need a tint decision.
- ALL-CAPS for menu/UI labels. 1280×720 design viewport, `canvas_items` stretch,
  fixed 16:9 world view (players die off-screen at the blast zone — that hidden-death
  design is intentional; do not reveal the blast zone to players).

## TESTING RULES

- Every sim behavior gets a unit test with hand-computed expected values.
- Every bug gets a named regression test that reproduced it before the fix.
- The determinism suite (parallel==serial, replay==live, resume==uninterrupted, golden
  hashes) must stay green; gameplay changes re-pin goldens with a dated comment.
- View changes are verified visually: capture screenshots via the automation envs and
  actually look at them; record an mp4 for the designer when behavior/motion changed.

## ADDING NEW GAMEPLAY (the pattern)

When the designer proposes a feature, FIRST ask whichever of these their description
doesn't already answer — before writing any code:

1. Should this feature be parameterizable for the evolutionary features?
2. What kinds of behavior do you want to avoid / what bounds should limit the feature?
3. Does the NPC agent behavior need to be adjusted? If so, how? (Remember: the agent is
   the fitness instrument — changes alter what fitness measures and go in DEVIATIONS.md.)
4. Are new assets needed?
5. What are the behaviors, entities involved, and any state restrictions for the feature?
6. Are there controller differences? (New buttons/axes also change the InputFrame and
   therefore the trace format — see Q8.)
7. What should the fitness function and research data see? New per-player/match stats to
   record? Should existing fitness remain blind to it, or does it need new terms
   (new fitness = new versioned class, never edit a shipped version in place)?
8. Serialization & legacy: does it add genome params (schema append) or structure
   (formatVersion bump)? What default do imported Unity games and existing evolved
   games get? Does the InputFrame/trace format change?
9. Rollout: should evolution search it immediately, or land it human-play-only first
   behind a `GenerationConfig`/`MatchConfig` flag and enable evolution after the
   designer test?
10. Player feedback & readability: how is it telegraphed (state tint, sprite, HUD
    element, animation) so both players can read it — per the aesthetics rules above?

Record the answers, then follow `docs/ADDING_FEATURES.md` step by step
(schema → sim → stats → agent → tests → view → docs → designer test gate).

## STATUS (as of 2026-07-09)

Conversion phases 0–5 complete: schema/genome layer, deterministic sim core, evolution
engine + CLI, full Godot app (Play/Watch/Evolve/Manage), export pipeline, replication
study validating dynamics against the paper (plateau criterion + both design motifs
reproduced). Designer has play-confirmed core combat + solid-contact physics.
Phase 6 feature 1 (multi-move control scheme: 4 assignable action buttons, button→move
genome gene, WASD + Space + IJKL, 2P requires a controller — see
docs/features/multi-move-controls.md) and feature 2 (**UtilityAgent is now the fitness
instrument**, replacing the decision tree as default everywhere; DEVIATIONS.md #18,
docs/features/utility-agent.md, comparison report in docs/reports/) are implemented
and awaiting the designer play-test gate. Trace format is 7 values/player (legacy
readable); game.json is formatVersion 2 (v1 readable); run.json records the agent
config (old checkpoints resume as DT). Outstanding: first push + CI canary review
(needs designer's GitHub auth); DT archival after designer confirms the utility pivot.
Next after confirmation: actual multiple moves per character (schema counts + utility
move selection).
