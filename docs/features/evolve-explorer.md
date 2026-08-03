# Feature: Evolution Explorer (FEATURES.md §Evolution Tools)

Date: 2026-07-27. Designer request: the evolve graph "doesn't tell us much about the
games, and it doesn't let us investigate the matches well" — add selection, preview,
and a "basket". Pure VIEW-LAYER tooling: zero BrawlerSim changes, no genome/trace/
fitness impact, no goldens moved, evolution results byte-identical (the dashboard
only *observes* the engine).

## What shipped

**Per-game chart points** (`FitnessChart`): each generation plots a point per game
(its fitness score) under the top/average lines. The engine already exposes
`Population` + `LastFitness` after every `Step()`, so the dashboard snapshots
(scores copy, genome refs) between steps and ships them to the UI over a
`ConcurrentQueue` (a `GameGenome` is not a Godot Variant, so no `CallDeferred`
args). Genomes are immutable and survivors are shared references across
generations, so retention is cheap. Chart range still comes from the lines —
early-generation stragglers score hundreds below and would flatten the curves —
so out-of-range points clamp to the bottom edge, fainter. Dense populations
subsample the *drawn* dots (≤ ~40 per generation column); hit-testing covers
every game. Points are in-session data: they exist for generations run in this
dashboard session (matching the workflow — run, watch, click).

**Click → selection → live preview**: clicking within 12 px of a point selects the
nearest game (gold ring + `GEN n · GAME i · FITNESS f` readout) and starts a LIVE
preview in the right-hand column: `MatchPreview`, a miniature arena in a
SubViewport that reuses the real view stack (StageView, PlayerView incl. trails
and name pills, ProjectileLayer, SpawnPadView, ArenaCamera) over a private
SimWorld with two standard utility agents. New matches loop continuously — a
finished match lingers ~1.5 s on its end state, then the next seed plays
(deterministic per point: first seed = gen·1000 + index + 1, then +1 per match).
No traces are recorded and `MatchSession` is untouched. When a run finishes, the
final generation's best game auto-selects so the preview is live immediately.

**ADD TO GAMES (the basket)**: saves the selected genome as a normal game.json in
the favorites library — `runs/favorites/` in dev (shared with the CLI),
`user://runs/favorites/` in exported builds (`AppPaths.FavoritesRoot`). Name
`<run>-g<gen>-game<index>` (collision-suffixed `-2`, `-3`, …); `origin` records
run/generation/index/fitness for provenance.

**Game picker** (`MainMenu.PickGame`): PLAY — 2 PLAYERS / PLAY — VS CPU / WATCH AI
MATCH / WATCH REPLAY now open a simple in-scene overlay list (not a native popup —
those don't embed on macOS and dodge screenshots): FAVORITES first (with a hint
pointing at the EVOLVE screen when empty), then DEMO GAMES (`runs/demo/`,
maintained per the curation rule), then `ADVANCED: BROWSE FILES…` — the old file
explorer, hidden by default — and CANCEL. Selection feeds the exact same
`OnGamePicked` path (Replay still asks for its trace file afterwards).

## Automation

- `BRAWLER_AUTOEVOLVE` gained `favorite=1`: after the auto-run finishes and the
  best point auto-selects, ADD TO GAMES fires (screenshot/e2e verification).
- `BRAWLER_PICKER=1`: opens the game picker on menu load (screenshots).

## Verification (2026-07-27)

Auto-evolve (pop 30 × 40 gens): dashboard screenshot shows the point cloud,
clamped stragglers, gold selection ring, live preview mid-match with correct
GEN/GAME/FITNESS line and looping seed counter; the favorite landed in
`runs/favorites/` (collision suffix verified with a second save) and the saved
game loads in the real arena via `BRAWLER_GAME`. Picker screenshot shows
FAVORITES + DEMO GAMES + ADVANCED. Fixed en route: the status label's unwrapped
run-dir path forced the middle column past the window edge (now `ClipText` +
ellipsis). 283 tests green; sim untouched.
