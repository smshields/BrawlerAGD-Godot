# BrawlerAGD → Godot 4 (C#) Conversion Plan

**Status:** APPROVED 2026-07-07 — designer decisions recorded in §4; ready for Phase 0.
**Sources reviewed:** all 26 C# scripts under `Assets/Scripts/`, `BrawlerAGD_AIIDE22.pdf` (AIIDE 2022 paper).

---

## 1. What the current system is

An automated game designer for 2-player brawlers (AIIDE '22). A genetic algorithm
(pop 100, dropout 0.5, mutation 0.4) evolves **games** = 2 characters (12-param genome each)
+ 1–2 moves per character (12-param genome each) + a stage (platform list). Each candidate
game is evaluated by a decision-tree AI playing itself in the Unity Arena scene **in real time**
(time-scaled), producing a `GameResult` whose fitness combines: target game length (45s),
total damage, total hits, damage-per-stock cap, damage fairness, and stock fairness.
Population lives **on disk** as per-game JSON folders (`player1.json`, `p1move1.json`, …,
`level.json`); crossover/mutation read and re-write those files each generation.
A run plateaus after ~12 hours (per the paper).

Play/study modes: EVO (self-play evolution), LOAD / LOADDISK (humans play a saved game),
TUTORIAL (with jump dummies). The paper's core finding: fitness optimization measurably
shaped human play, but alignment between the AI playtester's behavior and human behavior is
the crux — games where humans played like the AI scored/felt as intended; where humans
diverged, balance collapsed.

### Key architectural seams worth preserving
- **`Controller` abstraction** (`Controller.cs`): the player FSM only reads
  `GetAxis`/`GetKeyDown`/`GetKeyUp`/`GetKeyHold`; human input, `AI`, `HoldJump` dummies are
  drop-in subclasses. This is exactly the seam the new engine needs (human / AI / replay).
- **Genome-as-float-array** with declarative range tables (`SerializedPlayer.ranges`,
  `SerializedMove.ranges`) + generic single-point crossover / re-roll mutation.
- **Serialized descriptors vs runtime objects** split (`SerializedPlayer` → `Player`).

### Defects found (fix in rewrite; do not port)

> 2026-07-07: #1, #2, #3 and the seedable-RNG half of #6 have also been **fixed in this Unity
> repo** (`GameResult.cs`, `EvolutionManager.cs`, `Controller.cs`) so any interim Unity runs
> score correctly. #6's frame-timing nondeterminism is unfixable in Unity and lands in the rewrite.
| # | Location | Defect |
|---|----------|--------|
| 1 | `GameResult.evaluate()` + `evaluateHumanGame()` | `Math.Abs(remainingStocksP1 - remainingStocksP1)` — subtracts P1 from itself; the stock-fairness term is a constant `3` and never influenced evolution. Paper says it should be \|s1 − s2\|. |
| 2 | `GameResult.evaluate()` | `targetDamagePerStock = (6 - s1 + s2) * 100` — stocks lost should be `(6 - s1 - s2)`; sign error inflates the cap when P2 has stocks left. |
| 3 | `EvolutionManager.SetTimeScale()` | `fixedDeltaTime *= timeScale` compounds on every call; `Pause()` calls it with `0` → `fixedDeltaTime = 0` permanently (Resume never restores it). Physics behaves differently after any pause. |
| 4 | `Player.moveRight()/moveLeft()` | Speed-clamp check uses `Mathf.Abs(velocity.x)` but then assigns a signed max — a player moving fast one way who taps the other direction snaps instantly to full speed the opposite way. Also two near-identical copies of the logic. |
| 5 | `Player.cs` header comment | Known bug (documented, unfixed): jumps refresh when platform is above *and* below. Ground detection generally is raycast+collision-event patchwork across `OnCollisionEnter/Stay/Exit2D`. |
| 6 | Determinism | GA uses seeded-per-run `System.Random`, but the `AI` controller uses unseeded `UnityEngine.Random`; move timing uses coroutines/`WaitForSeconds` (frame-rate and timescale dependent); physics is PhysX2D. **No run is reproducible.** |
| 7 | Hit detection | Damage/knockback logic duplicated across `OnTriggerEnter/Stay/Exit2D` with slightly different bodies (Enter counts stats, Stay/Exit don't count `totalDamage`/hits but still apply damage — double-counting `damage` on multi-frame overlaps mitigated only by a 0.1s invincibility coroutine). |
| 8 | Paths | `Constants.PC_SLASH = "\\"` hardcoded — breaks on macOS/Linux; population is written *inside* `Assets/`. |
| 9 | `Player.Update()` | Entire state-machine switch duplicated in two branches (evo null-check vs paused-check); `shielding` case silently missing from the evo branch. |
| 10 | Shield/parry system | Half-implemented and effectively dead: parry/reflect windows are fields that no code reads; shield break logs a string and does nothing; `isShield` is an `int`. Move 2 was retrofitted late (`ArenaManager` has `serializedMove2*` but `GenerateGame` creates both, crossover only reads/writes move1 files… inconsistently). |
| 11 | `MapGenerator.Above()` | Child width computed as `rand.Next(2, platform.xSize - x + 1)` with **absolute** x where parent-relative was clearly intended — children of negative-x parents could exceed the parent's width and the design-space max. Fixed in the port (`StageGenerator.Above` uses `x - parent.X`); guarded by `NoPlatformExceedsTheDesignSpaceMaximumWidth`. |

> **Phase 1 finding (2026-07-07):** `knockbackModX/Y` values in evolved/legacy data
> legitimately exceed their generation ranges — the generation-time constraint lerps them
> toward the hitbox location, and Unity re-saved post-flip values into the study files. The
> schema now distinguishes generation range from valid domain (`ParamSpec.ValidMin/Max`,
> ±1.5 for both components = the convex hull of the lerp endpoints). Discovered by the
> importer tests against Games A–F.

### Performance bottlenecks (why runs take 12h)
1. **Real-time evaluation** — each candidate plays up to 60 wall-clock-scaled seconds; the
   whole GA is serialized through one Unity scene loaded/unloaded per round.
2. **Disk-as-population** — every crossover reads 10 JSON files and writes 5; every arena
   load re-reads and re-writes the game folder.
3. **No parallelism** — evaluations are embarrassingly parallel (independent matches) but run
   one at a time.
4. **Rendering/UI active during evolution** (sprites, TextMesh labels, HUD churn).

---

## 2. Target architecture (proposed)

**The central move: extract the match simulation into a pure C# library with a fixed-tick,
deterministic, engine-independent core. Godot becomes a *view* over the sim; the evolutionary
runner drives the same sim headlessly at thousands of ticks per second.**

This is also the correct answer to "run simulations quickly while keeping fitness accurate":
you cannot make Godot's (or Unity's) full physics pipeline both fast and bit-reproducible, but
this game barely uses physics — axis-aligned static platforms, velocity-driven capsules/boxes,
impulse knockback, trigger-overlap hitboxes. A bespoke ~500-line kinematic 2D sim covers it,
and then **the AI's evolution matches and the human's rendered matches execute the exact same
code**, tick for tick. That eliminates the sim-vs-play drift problem at the engine level (the
paper's residual problem — AI *policy* vs human *policy* — remains a research question, but
the substrate is now identical).

```
BrawlerAGD-Godot/
├── BrawlerSim/                  # Pure .NET class library — zero Godot references
│   ├── Params/                  #   Declarative parameter schema (name, range, entity type)
│   │   ├── ParamSpec.cs         #   → genomes are generated/crossed/mutated FROM the schema,
│   │   └── GenomeSchema.cs      #     so adding a param/move/entity = data change, not code
│   ├── Genome/                  #   GameGenome { CharacterGenome[], MoveGenome[][], StageGenome }
│   ├── Sim/                     #   Deterministic fixed-tick match simulation
│   │   ├── SimWorld.cs          #   60 Hz ticks; integer tick counters replace WaitForSeconds
│   │   ├── SimPhysics.cs        #   AABB platforms, gravity/drag/knockback, ground rules
│   │   ├── SimPlayer.cs         #   The player FSM (idle/air/exhausted/warmup/attack/cooldown/stun)
│   │   ├── SimMove.cs           #   Hitbox activation windows in ticks
│   │   └── IInputSource.cs      #   ← the old Controller seam: AI / Human / Replay / Scripted
│   ├── Agents/                  #   DecisionTreeAgent (port of AI), seeded RNG injected
│   ├── Fitness/                 #   MatchStats + FitnessFunction (pluggable, versioned)
│   ├── Evolution/               #   EvolutionEngine: selection/crossover/mutation strategies,
│   │                            #   in-memory population, parallel evaluation, checkpointing
│   └── Replay/                  #   Input trace + per-tick state hash record/verify
├── BrawlerSim.Tests/            # xUnit: determinism (seed → identical hash), genome ops,
│                                #   fitness regression vectors, FSM transition tables
├── BrawlerRunner/               # Headless CLI (dotnet run): evolve, re-evaluate, export CSV/JSON
└── godot/                       # Godot 4.x .NET project
    ├── scenes/  (Arena, MainMenu, LoadGame, Tutorial, EvolutionDashboard, Credits)
    └── scripts/ (ArenaView renders SimWorld state; HumanInputSource maps Godot Input;
                  EvolutionDashboard visualizes a live run — sampled, not every match)
```

### How each requirement lands

**Speed.** 45 sim-seconds at 60 Hz = 2,700 ticks of trivial arithmetic → a match evaluates in
single-digit milliseconds. 100-candidate generations run in parallel via `Parallel.ForEach`
(one RNG per match, seeded `hash(runSeed, generation, gameId, round)`). Expected: the 12-hour
plateau run compresses to **minutes**, on one machine, with zero rendering. Population lives
in memory; disk writes become per-generation checkpoints (resumable runs for free).

**Exact play patterns / trustworthy fitness.** Determinism by construction: fixed tick, no
wall clock, no engine physics, all RNG injected and seeded, move timings in integer ticks.
Guarantees enforced by CI tests: (a) same seed → identical per-tick state hash across runs and
across headless-vs-rendered execution; (b) every evolved game ships with its input traces, so
any fitness score can be *replayed and watched* in the Godot arena. This is a capability the
Unity version never had — you'll be able to visually audit exactly the match the fitness
function graded.

**Extensibility (parameter-driven everything).** The genome is generated from `GenomeSchema` —
a data table of parameter specs (like the paper's Table 1, but live). Adding a third move, a
new character stat, level tile types, or a new game mode = extending the schema + implementing
the sim behavior behind a parameter; crossover/mutation/serialization/UI ranges all derive from
the schema automatically. Fitness functions are pluggable strategy objects (the paper's future
work — persona-based fitness, human-data-informed evaluation — slots in here), as are
selection/mutation strategies and match formats (stocks, timed, N>2 players later).

**Code correctness.** All 10 defects above get fixed at the design level (single knockback
resolution path, one FSM implementation, schema-driven paths via `System.IO.Path`, seeded RNG
everywhere). Fitness fixes (#1, #2) change evolution dynamics vs. the paper — flagged in the
Open Questions since it affects comparability with published results.

### The determinism contract (rendered play ≡ simulated play, guaranteed)

The requirement: headless evaluation and rendered play must have **zero gameplay difference**,
including as the sim grows (projectiles, >2 players, special tiles). The design guarantees
this structurally, not by testing alone:

1. **One implementation.** There is no "headless version" and "rendered version" — there is
   one `SimWorld.Tick(inputs[])` function. Rendered play calls it once per Godot physics frame;
   headless evaluation calls it in a tight loop. Nothing else advances game state.
2. **Godot is forbidden from owning gameplay.** The `BrawlerSim` library has zero Godot
   references (enforced by the project file — it can't even compile against Godot). The Godot
   layer may only (a) sample human input into the per-tick input struct and (b) draw the sim
   state. No Godot physics bodies, no Area2D hitboxes, no TileMap collision — tiles/platforms
   collide inside the sim; Godot's TileMap is a costume.
3. **All variability is injected.** Fixed tick rate (60 Hz), integer tick counters for all
   durations (no wall clock, no coroutines), a single seeded RNG stream per match, fixed
   entity-iteration order (arrays, never dictionary order), no `Parallel` *inside* a match
   (parallelism is across matches only).
4. **Verified continuously.** CI runs every golden match headless and "rendered-path" (same
   tick loop driven through the Godot-facing API) and asserts identical per-tick state hashes;
   any new feature that breaks tick-equivalence fails the build.

Consequences for the roadmap features: **projectiles, N players, hazard tiles are just more
entities inside the sim** — `SimWorld` holds a flat, ordered entity list (players, moves,
projectiles) and a tile grid with per-type behavior, sized for N≥2 players from day one. They
inherit determinism automatically; nothing about the approach caps physics complexity (a full
match is thousands of ticks of simple arithmetic either way).

Two honest caveats, with mitigations:
- **Human input is real-time.** A human match is deterministic *given its input trace*, so
  every human game records the trace; replaying it headless reproduces the match bit-for-bit.
  (AI-vs-AI matches are bit-identical between modes with no caveat.)
- **Cross-machine float drift.** .NET floats are deterministic on a given platform/runtime but
  can differ across CPU architectures (x64 vs ARM64) for transcendental functions. Mitigation:
  a small `DetMath` wrapper (table/polynomial sin/cos, no FMA-sensitive patterns) + a
  cross-platform hash test in CI; escalate to fixed-point only if that test ever fails.

### What we deliberately do NOT carry over
- Population-in-`Assets/` and the double-constants (`Constants` + dead `Consts`).
- Coroutine-timed game logic, `GameObject.Find`, cross-scene singletons
  (`EvolutionManager.instance` / `GameSettings.instance` / static `ArenaManager.evo`) —
  replaced by explicit composition: the Godot layer owns a `SimWorld` and an app-state service.
- Checked-in build artifacts (`BrawlerAGD.exe`, `BrawlerAGD_Data/`, Doxygen HTML) — new repo
  stays clean; docs generated in CI if wanted.

---

## 3. Phased execution

Each phase ends with a **DESIGNER TEST** gate before being marked complete.

**Phase 0 — Decisions & scaffold.** Resolve open questions; create solution
(`BrawlerSim` + tests + runner + Godot 4.x .NET project); CI running tests.

**Phase 1 — Genome & schema.** Port parameter tables into `GenomeSchema`; genome types;
crossover/mutation as schema-generic operations; JSON (de)serialization; **importer for the
existing Unity game folders** (Games A–F and any archived populations remain loadable).
Tests: round-trip, crossover/mutation ranges, import fidelity against the repo's JSON files.

**Phase 2 — Deterministic sim core.** SimPhysics (platform AABBs, gravity/drag/mass,
ground detection done once and correctly), SimPlayer FSM (tick-count states), SimMove hitbox
windows, knockback + hitstun + stocks + blast zone, MatchStats collection; DecisionTree agent
ported from `AI`. Tests: determinism hashes, FSM transition table, "golden match" replays.

**Phase 3 — Fitness & evolution engine.** Port fitness with the corrected terms (decision:
no bug-compatible mode; new results are simply not directly comparable to the AIIDE '22 runs —
fitness functions stay pluggable/versioned regardless), EvolutionEngine with parallel evaluation,
seeded runs, per-generation checkpoint/resume, CSV/JSON results export. CLI runner:
`brawler evolve --seed 42 --pop 100 --generations 300 --out runs/…`.
Benchmark target: ≥100 generations/hour on a laptop (expect far better).

**Phase 4 — Godot presentation layer.** ArenaView (players, moves, tilemap platforms from
stage genome, HUD, notifications, pause) driven by SimWorld at real-time tick rate;
HumanInputSource (keyboard ×2 + **gamepads day-one** via Godot InputMap). App shell with three
functions (per designer): **Evolve** (settings screen → run → live dashboard), **Play** (pick
any saved game, human-playable), **Manage** (browse/inspect/delete saved games and runs);
replay viewer (load any evolved game + trace and watch the graded match). Tutorial/study flow
deferred until the next study is designed. Visual style: keep Kenney CC0 assets.

**Phase 5 — Validation & calibration.** Side-by-side feel check vs. the Unity build (the
`.exe` in this repo) using imported Games A–F; determinism CI suite; a full evolution run
compared qualitatively against the paper's fitness curves (Appendix Fig. 3); document the
behavioral deltas from bug fixes #1/#2/#4.

**Phase 6 — New-feature runway (backlog, post-parity).** N moves per character (schema is
already move-count agnostic), richer stage genome (tile types, hazards, moving platforms),
game modes (timed, N-player), fitness experiments (personas, human-trace-informed evaluation),
possible future: WebAssembly export for browser-based user studies.

---

## 4. Decisions (designer, 2026-07-07)

1. **Parity:** design and feature parity, not code mimicry. Calibrate feel against the shipped
   `.exe`; the binding guarantee is sim-used-for-fitness ≡ sim-humans-play.
2. **Simulation:** custom deterministic C# sim core CONFIRMED, with the hard requirement that
   rendered and simulated play be exactly identical — see "The determinism contract" (§2).
   The sim must scale to projectiles, N players, and special tiles.
3. **Fitness bugs:** fixed outright, no bug-compatible mode. (Also patched in the Unity repo.)
4. **Shield / move 2:** dropped from the port; re-implemented later as a schema-driven move type.
5. **Save format:** single `game.json` per individual + run manifest; importer for the old
   folder layout. Any saved game must be reloadable as human-playable. Design serialization so
   a later move to a DB is a storage-adapter swap, not a format rewrite.
6. **Version/platforms:** latest stable Godot 4.x .NET at kickoff; .NET 8; macOS dev; headless
   runs macOS/Linux; play builds mac/win. **Controller support day-one.**
7. **App scope:** core play/load only until the next study is designed. Shell = Evolve (with
   pre-run settings) / Play existing / Manage existing games.
8. **Repo:** new clean repository; this one is archived as the Unity reference.
