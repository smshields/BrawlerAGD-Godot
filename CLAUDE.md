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
  `BRAWLER_QUIT_AFTER=<s>`, `BRAWLER_SCENE=evolve|manage`, `BRAWLER_AUTOEVOLVE=...`,
  `BRAWLER_PAUSE_AT=<tick>` (open the pause menu at a sim tick + shoot "paused").
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
- Player state tints (Unity-verbatim + additions): Idle white · Air green ·
  JumpsExhausted grey · WarmUp yellow · Attack red · CoolDown blue · Stun magenta ·
  Shield cyan (2026-07-12; the shield itself is a white→red circle by degradation) ·
  Dash orange (2026-07-13) · Crouch purple with the body squished feet-planted
  (2026-07-13; distinct from Stun magenta).
  These ARE the game's readability system — new states need a tint decision.
  Flash vocabulary (2026-07-20): post-hit invincibility = steady 0.4 alpha ·
  dash i-frames = fast body-alpha strobe (1.0↔0.6) · a just-reflected projectile
  strobes gold↔white for ~¼ s · spawn invulnerability = SLOW shimmer pulse
  (0.7↔1.0, 2026-07-22; distinct from the two above). Projectiles are gold FILLED
  shapes; the shield is a white→red OUTLINE circle. Spawn platform (2026-07-22) =
  a SOLID white PILL (rounded rect, bright flat top edge, subtle darker underside —
  reads as a platform; 2026-07-23 render), quick-fading on despawn. Death flash
  (re-rendered 2026-07-23) = a tall, narrow, BORDERLESS white streak from the death
  point, perpendicular-inward from the crossed screen edge (diagonal toward camera
  center from a screen corner; always on screen), never wider than the character,
  wrapped in an EXPANDING translucent flame cone; snap-out + hold + fade (0.65 s) +
  base pop, length∝KO speed+damage. Motion trail (re-rendered 2026-07-23, 3 passes) =
  state-tinted afterimage ghosts at equal ARC-LENGTH intervals along the recent path
  (continuous streak at any speed), opacity on an EXPONENTIAL screen-speed ease-in
  with the floor ABOVE normal run/jump speeds (2026-07-27: nothing at ordinary
  movement, a hint on dashes, the full streak only at knockback/KO speeds — the
  trail exists to track extreme speed), min ghost spacing 22 px; a KO/teleport
  DETACHES the trail to fade in place (~0.5 s) rather than vanish with the body;
  teleport test is velocity-aware. Projectile trail (2026-07-23) = slight 3-ghost
  gold comet tail, fainter than players. Key-layout hint (2026-07-23) = outlined
  ALL-CAPS panel beside each human player, fades ~9 s.
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
Phase 6 feature 1 (multi-move control scheme: assignable action buttons, button→move
genome gene, WASD + Space + I/J/K/U/L — FIVE buttons since 2026-07-20 (pad: L1/X/A/Y/R1,
B = the single jump; game.json v6, trace rows 8-wide, old artifacts migrate + replay
bit-identically), 2P requires a controller — see
docs/features/multi-move-controls.md) and feature 2 (**UtilityAgent is now the fitness
instrument**, replacing the decision tree as default everywhere; DEVIATIONS.md #18,
docs/features/utility-agent.md, comparison report in docs/reports/) are implemented
and awaiting the designer play-test gate. Trace format is 7 values/player (legacy
readable); game.json is formatVersion 2 (v1 readable); run.json records the agent
config (old checkpoints resume as DT). Outstanding: first push + CI canary review
(needs designer's GitHub auth); DT archival after designer confirms the utility pivot.
Feature 3 (second move, button coverage, damage-ranked selection, moveMix nudge,
0.25 s stun cap + stunLock/jumps fitness terms) feature 4 (SHIELD move type, game.json v3, block-reward fitness term —
docs/features/shield.md), and feature 5 (DASH move type: gravity-suspended travel,
per-stage evolvable i-frames, jumps+dash air budget, no-KO contact cap, pinned last
button, unified defense channel, game.json v4 — docs/features/dash.md) are
implemented and awaiting the designer play-test gate.
Utility-agent behavior log (flank/traversal/exhausted-disengage/shield) lives in
docs/features/utility-agent.md; fitness standard-v3 is shield-BLIND for now.
Feature 7 (2026-07-14): evolve-menu composition control (PINNED default / RANDOM /
PER-BUTTON; move types are evolvable structural genes in composed modes) + advanced
per-parameter generation ranges incl. clamps and beyond-domain ranges (run.json
records both; pinned path byte-identical — docs/features/evolve-composition-and-
ranges.md, DEVIATIONS #22). Pre-v4 runs archived to archive/ (see its README).
Feature 8 (2026-07-14): PROJECTILES — 24-gene attack-family move type, the first
non-player sim entities (closed-form paths, SAT shape collision, melee hit
pipeline, platform/boundary/TTL/decay despawns, no live cap), gold filled-shape
rendering, agent corridor-fire/zoning/dodge (defense fires regardless of
counter-hit vs bolts), SlotSpec.Projectile in the RANDOM pool, game.json v5,
fitness-blind stats — docs/features/projectiles.md, DEVIATIONS #23. Match goldens
unmoved (hash section gated on live bolts); fingerprint re-pinned (bytes only).
2026-07-20 follow-ups (designer): projectile WIND-UPS telegraph exactly like
melee (closes the zero-shields-vs-zoners gap — DEVIATIONS #24) and the FIVE-button
control scheme (single jump button; DEVIATIONS #25 — see feature 1 above).
REFLECT genes on shield & dash (2026-07-20, DEVIATIONS #26): fully-covered /
dash-contacted PROJECTILES are re-fired at their shooter (ownership transfers,
path restarts mirrored, TTL/decay clocks continuous); reflected bolts strobe
white, dash i-frames strobe body alpha; agents prefer reflect options ×1.5 vs
ranged threats; stat ProjectilesReflected, fitness blind.
Feature 9 (2026-07-21): MAP SIZE — the stage grew an 11-gene ParamSet (visible
half extents 0.5×–5×, KO margin, platform count/size budgets, mirrored/mirrorSide,
spawn genes; game.json v7, DEVIATIONS #27, docs/features/map-size.md). Blast zone
is genome-driven (legacy bit-exact); generator grows all four directions with
stack re-seeding, asymmetry, and BODY-SAFE spawn columns (degenerate layouts
regrow); stage mutation transforms asymmetric→mirrored per mirrorSide instead of
always regenerating; agent sense box scales up with map size; MaxMatchSeconds
default 60→300. View: framing camera (zoom clamped legacy-floor↔KO box), minimap
overlay + first SETTINGS menu (user://settings.cfg: corner/size/opacity), HUD
panels moved to the bottom edge with outlined text, StageView's blast rect
removed (hidden-death rule). Stage schema joined the evolve-menu ranges.
Showcase games + evolved champions in archive/runs-2026-07-23/showcase-mapsize/
(best picks promoted to runs/demo/), mp4s in
runs/media/mapsize-*.mp4, charts in docs/reports/img/. Awaiting the designer
play-test gate.
Feature 10 (2026-07-22): agent air-jump conservation (DEVIATIONS #28) — fixes the
large-map oscillation (ThreatDodge no longer spends the air jump to flinch; traversal
walks between adjacent same-height platforms). Utility golden re-pinned; DT + fingerprint
untouched. Also: projectile schema joined the evolve-menu/CLI advanced ranges.
Feature 11 (2026-07-22): GAMEPLAY POLISH — (a) SPAWNING BEHAVIORS (DEVIATIONS #29,
game.json v8): spawn on a temporary intangible platform, invulnerable on a separate
timer; two evolvable stage genes (platformSpawnDuration 1–5 s, spawnInvulnDuration
1–3 s, both default 0 = feature-off/legacy-parity); 3 s respawn blackout; agent skips
damage-immune targets; fitness-blind. (b) DEATH ANIMATIONS + (c) MOVEMENT BLUR — pure
view-layer (edge-anchored KO flash; screen-space directional motion smear).
docs/features/spawn-and-polish.md; demo runs/media/spawn-polish-demo.mp4. Match goldens
unmoved (legacy feature-off); fingerprint re-pinned. Awaiting the designer play-test
gate. Trial evolution measured spawn-camping negligible (0.4%).
Feature 11 follow-ups (2026-07-23, designer bug reports): (a) death animation
RE-RENDERED as a tall narrow inward-perpendicular streak from the death point (no
border, Smash-KO juice); (b) HUD now lists INVULNERABLE/INTANGIBLE/RESPAWNING with a
countdown (spawn conditions are flags, not FSM states, so they were previously
invisible); (c) per-character PLATFORM FIT (DEVIATIONS #30) — Generate/Crossover/Mutate
move platforms (RNG-free) so both characters can traverse and no gap is asymmetrically
passable (asymmetric stages 11%→5%, zero overlaps). Fingerprint re-pinned twice;
match goldens unmoved.
Second round (2026-07-23, designer bug reports): (1) motion trail RE-RENDERED as
afterimage ghosts (the in-quad UV smear could not draw outside the sprite — unnoticeable
by construction); (2) KEY-LAYOUT HINT beside each human player for the first ~9 s
(superseded next day by the HUD debug strip — see feature 12); (3) death flash is
CAMERA-relative and always on screen — perpendicular from the crossed screen edge,
DIAGONAL toward camera center from a screen corner, from the actual death point when
visible; (4) EXHAUSTION RULE (DEVIATIONS #31, sim change): AirJumpsExhausted now
requires jump, jump, AND dash — a dash in hand keeps the character in Air with full
abilities (supersedes the 2026-07-13 no-chase-with-dash rule, whose premise fell).
Dash-less genomes bit-identical: ALL goldens + fingerprint unmoved, no re-pins.
Trial evolutions healthy (runs/exh-310*, charts/exhaustion-rule-trajectories.png).
Third round (2026-07-23): trail SECOND PASS (arc-length ghost placement — continuous
streak at any speed; velocity-aware teleport test — the old fixed 200 px clear erased
the trail at KO speeds; detached trails linger ~0.5 s after a KO/teleport) and
PROJECTILE comet tails (3 faint gold ghosts, view-only).
Fourth round (2026-07-23): trail opacity EXPONENTIAL in screen speed + 22 px min
ghost spacing (readable at low speed, frenzy at high); spawn pad re-rendered as a
SOLID PILL platform; death flash gained an expanding flame CONE + 0.65 s life.
(Also fixed: godot-layer builds had been failing silently on a MathF/Mathf typo —
quiet-build tails hid the error; several intermediate view captures were stale.)
RUNS CURATION (designer): `runs/` now holds only `demo/` (curated interesting
games — cannonball-arena, zoner-crossfire, spawn-sanctuary, skyscraper-duel,
corridor-hopper; see runs/demo/README.md, mean pairwise genome distance 5.9) and
`media/`; all 19 run dirs archived to archive/runs-2026-07-23/ (README updated).
Maintain runs/demo/ as new champions demonstrate new things.
Demos: runs/media/polish-fixes-demo.mp4 (trails/flash/pill pads),
bigmap-koflash-demo.mp4, projectile-trails-demo.mp4 (all re-recorded with the HUD).
Feature 12 (2026-07-24): HUD POLISH (FEATURES.md §HUD + design/BrawlerAGDHUD.jpg;
docs/features/hud-polish.md; view-only, no goldens). Four static quarter-width
bottom slots, LEFT-PACKED (P1/P2 in quarters 1–2); identity colors = the
state-tint-avoiding set rose/sky/gold/teal (PlayerPalette; in-world name tags got
matching pill backgrounds); solid outlined panels with name pill, stock dots (→
"N STOCKS" past 8), character sprite, and a damage % that ROLLS through interim
numbers growing with hit magnitude; hit shake scaled by damage (hit player only),
death = major shake + white panel flash. DEBUG STRIP above each panel (default ON,
persisted toggle): human-readable states tinted like the body (READY/AIRBORNE/
EXHAUSTED/WINDING UP/...), INTG/INVL spawn-timer bars, the DI arrow, and the full
control layout with per-genome move names, keycaps lighting on press (AI presses
included — an instrument view). PAUSE MENU is now a real navigable menu
(RESUME / DEBUG PANEL / SETTINGS / QUIT; PauseMenuView; ESC/Q shortcuts kept;
AppSettings gained hud.debugPanel). ControlsHintView deleted (superseded);
BRAWLER_PAUSE_AT=<tick> automation env added.
2026-07-27 follow-ups (designer bug reports): (1) motion trail retuned to EXTREME
speeds only (floor 850 screen-px/s — above normal run/jump; nothing at ordinary
movement, full streak only at knockback/KO; slow blast-drifts no longer linger);
(2) the platform fit's body-gap pass became an ITERATIVE five-strategy solver with
structural termination (DEVIATIONS #30 amendment) — asymmetric corridors over 800
generation seeds: 248 stages → ZERO, connectivity unchanged, zero overlaps;
fingerprint re-pinned (match goldens unmoved); property test
GeneratedStagesNeverHaveAsymmetricBodyGaps + corridor-chain termination fixture;
trial evolutions healthy (charts/gap-solver-trajectories.png; runs archived).
Awaiting the designer play-test gate.
