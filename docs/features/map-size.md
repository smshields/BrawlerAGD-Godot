# Map Size (FEATURES.md §Map Size) — design record

Date: 2026-07-21. Designer answers recorded verbatim-in-substance; implementation notes
follow each. This feature makes map dimensions, platform layout variety, symmetry, and
spawn positions part of the genome, adds a tracking camera + minimap to the view layer,
and raises the default match timeout.

## Designer decisions (2026-07-21)

1. **Evolvable, not run-level.** Map size evolves alongside layout. Mirroring is NOT
   preserved across the board — symmetry becomes an evolvable parameter.
2. **Bounds:** 0.5×–5× current dimensions, width and height independent. The camera
   must never show beyond the KO bounding box and must move with the players. Spawn
   fairness on asymmetric maps is left to the balance fitness terms.
3. **Mirroring is a gene**, plus a `mirrorSide` gene ("mirror left" / "mirror right")
   that decides which half is the source when a previously-asymmetric stage is
   transformed into a mirrored one during mutation.
4. **Spawn genes**, with repair restrictions: never in immediate KO bounds, always over
   a platform.
5. **Traversability = abstract grid guarantee (option a)** — platforms placed within
   jump-reach (jumpHeight/jumpLength grid steps) of a parent, connectivity by
   construction. Option b (validate against the actual characters' jump+dash physics)
   is provided as a utility + `GenerationConfig` hook to tinker with later, off by
   default and not wired into evolution.
6. **Hidden-death rule confirmed:** platforms may extend past the KO line; a player
   walking past it dies off-screen with no visual reveal.
7. **Agent platform-sense box scales with map size** (DEVIATIONS entry — on legacy-size
   maps the scale factor is 1 so the instrument is unchanged).
8. **Fitness stays blind to map size.** Match timeout default rises 60 s → 300 s and
   stays adjustable (`MatchConfig.MaxMatchSeconds`; run.json already records it, old
   checkpoints resume with their recorded value). If evolution time suffers we pivot
   the default.
9. **Minimap:** on by default, upper-right, 50% transparency; location/size/
   transparency configurable in a new persisted settings system. Character panel
   details move to the bottom of the HUD.
10. **Rollout:** simulate heavily before designer playtest — trial evolutions, charts,
    screenshots, video, bug hunt.

## Schema: the new `stage` ParamSet (game.json v7)

`StageGenome` gains a `ParamSet` (schema name `"stage"`) next to its platform list.
Legacy reference values: the true Unity-era camera view was 10 world units tall × 16:9
(half-extents 8.889 × 5), but the legacy blast-zone WIDTH carried the 1.1 factor twice
(a preserved Unity quirk: width = height·aspect·1.1, then ×1.1 again — see
MatchConfig). To reproduce the legacy blast zone bit-exactly from
`visible × (1 + koMargin)` with a single margin, the legacy `visibleHalfWidth` default
is 11·(16/9)/2 ≈ 9.778 (slightly wider than the old static view; the camera's zoom-in
floor stays the true 8.889 × 5 legacy framing). Bit-exactness of
`(a/2)·1.1f == (a·1.1f)/2` (power-of-two ops are exact) is regression-tested.

| Key | Generation range | Legacy default | Meaning |
|---|---|---|---|
| `visibleHalfWidth` | 0.5×–5× legacy ≈ [4.889, 48.889] | 9.778 | Max-camera-zoom-out half width; KO sits outside it |
| `visibleHalfHeight` | 0.5×–5× legacy = [2.5, 25] | 5.0 | Same, vertical |
| `koMarginFraction` | 0.05–0.25 | 0.1 | Blast half-extent = visible × (1 + margin) |
| `platformCount` | 2–16 (int-as-float) | 6 | Total platform budget for (re)generation |
| `maxPlatformSize` | 3–14 (int-as-float) | 6 | Max platform x-size for (re)generation |
| `mirrored` | 0–1 (bool ≥ 0.5) | 1 | Symmetric stage |
| `mirrorSide` | 0–1 (bool ≥ 0.5) | 0 | 0 = left half is source, 1 = right |
| `spawn1X/Y`, `spawn2X/Y` | drawn over platforms; valid domain = full KO box | derived | Spawn genes, repaired at sim time |

- Generation of the size genes is uniform; the platform tree and spawn genes are drawn
  by the generator itself (constrained draws), inside the schema's valid domains.
- The `stage` schema participates in the advanced-range override system
  (`GenerationConfig.WithRangeOverrides`) like every other schema, so the evolve menu /
  CLI can clamp map size per run.
- **Spawn repair (deterministic, at SimWorld construction):** clamp inside the visible
  box, then if not over any platform move horizontally to the nearest platform span,
  then raise above that platform's top and apply the legacy inside-platform nudge.
  Repair is the identity for already-legal spawns — that is what keeps legacy files
  bit-identical (their loaded spawn genes are the old derived spawns, already legal).

## Generator (rules are GENERATION-time guarantees)

Platform tree growth generalizes: Left/Right/Above/Below children, each within
jump-reach of its parent (connectivity by construction — rule "traversable"), overlap
rejected by bounded deterministic resampling (rule "no overlap"), every platform must
intersect the visible box (no invisible islands) but may extend past the KO line
(rule 6). Mirrored stages grow only the source half then reflect, so mirror copies
cannot overlap sources. Asymmetric stages grow anywhere in the visible box.

Crossover (params single-point + platform-list single-point, unchanged mechanism) and
spawn-gene mutation may violate the generation rules; the sim tolerates overlap, spawn
repair restores spawn legality, and untraversable crossover children are judged by
fitness — same philosophy as every other post-generation constraint in this system.

**Stage mutation semantics (new, replaces unconditional full regen):** the stage
ParamSet mutates via the standard 5-reroll op; then
- if the mutated `mirrored` gene is ON but the current layout is asymmetric → apply
  the `mirrorSide` transform to the existing layout (clamp source-half platforms at
  the axis, reflect);
- otherwise → regenerate the layout from the mutated params (the Unity-parity
  exploration path).

## Sim

- `SimWorld` computes the blast zone and spawns from the stage genome, not
  `MatchConfig`. `MatchConfig.BlastZoneHalf*` remain only as the legacy defaults
  feeding the loader.
- `MatchConfig.MaxMatchSeconds` default 60 → 300 (dated comment; run.json keeps
  per-run values so old checkpoints resume unchanged).
- Platform-sense half-extents scale by (visibleHalf / legacyVisibleHalf) per axis,
  min 1× — exposed on `SimWorld`, consumed by the agents (DEVIATIONS entry).

## Serialization

game.json v7: `stage` gains `params`. ≤ v6 files load with the legacy defaults above
and spawn genes derived by the old `ComputeSpawn` rule, making old games, traces, and
run checkpoints replay bit-identically (regression-tested). Population fingerprint
golden re-pins (generation consumes new draws — a real design-space change).

## View

- **Camera:** framing camera in ArenaView — centers on the players' midpoint, zooms so
  both are on screen with margin; zoom-out clamped to the visible box, zoom-in clamped
  to the legacy framing (10 world units tall) so characters stay legible; view rect
  clamped inside the KO box at all times (designer rule 2). On legacy-size maps the
  camera is static and identical to the pre-feature view.
- **Minimap:** semi-transparent overlay (platforms, player dots, camera rect) —
  default upper-right, 50% transparency, ON. Configurable: enabled, corner, size,
  opacity.
- **Settings:** first persisted settings system — `user://settings.cfg` via ConfigFile,
  SETTINGS button on the main menu. Holds the minimap options (a home for future
  settings).
- **HUD:** character panels move from the top edge to the bottom edge (minimap owns
  the upper corners).

## Testing

Generator property tests (per-seed sweep): ≥1 platform, all intersect visible box, no
overlaps, jump-graph connected, mirrored stages symmetric, spawn legality after repair.
Legacy: v6 file loads + replays bit-identically; old run checkpoint resumes. Schema
pins extended; fingerprint + any moved match goldens re-pinned with dated comments.

## What shipped (2026-07-21) — deltas found during implementation & simulation

- **Body-safe spawn columns.** Point-legal spawns proved insufficient: a spawn whose
  BODY box clips a platform edge is ejected to the platform's far side by the
  axis-clamp physics the moment a direction is held — on narrow maps that is past the
  KO line (a death per tick; both showcase probes hit it). `StageRules.TrySpawnOver`
  places spawns in columns the conservative body box (`SpawnBodyHalfWidth/Height`)
  clears, trying the legacy +2 hover then lower hovers (dense lattices roof the +2
  band). The generator REGROWS a layout up to 4 times when no body-safe column exists
  anywhere (degenerate wall-clusters), then accepts an embed fallback. Property test
  sweeps 500 seeds asserting no body-embedded spawn ships.
- **Growth-stack re-seeding.** The Unity tree died when its stack emptied (25% of
  stages were a lone mirrored pair; tall maps under-filled badly). The stack re-seeds
  from a random existing platform, bounded at 8 pops per budgeted platform → 96% mean
  budget fill, 100% within one of budget.
- **Mirrored spawns are constructed, not repaired.** Spawn 2 = spawn 1's exact mirror;
  running both through the repair pass instead let its left-biased nearest-column
  tie-breaking stack both players on one column.
- **Match timeout.** 60 s → 300 s default moved both match goldens (each was a 60 s
  draw that now resolves by KO); verified bit-identical to the old pins at 60 s before
  re-pinning. Paper-scale runs cost ~43 s vs ~25 s pre-feature (designer-accepted;
  pivot the default if it grows). Final-population timeout rates stayed 1–9%.
- **Blast-zone rect removed from StageView.** It pre-dated the camera and was never
  on-screen; the camera's zoom-out reaches the KO box on small maps and the hidden
  off-screen death rule says never reveal it. HUD text gained a dark outline (bottom
  panels can sit over white platform tiles).
- **Fingerprint re-pinned 3× same-day** (schema, re-seeding, body-safe spawns);
  final pin 302532665541572084.
- **Evolve menu:** the `stage` schema joined the advanced range list. (Noted: the
  projectile schema was ALREADY absent from that list — pre-existing gap, designer
  decision pending, not touched here.)
- **Replicate finding:** paper-scale replicates diverge into distinct map motifs
  (tall mirrored towers vs sprawling asymmetric arenas) — see
  docs/reports/img/mapsize-evolution-2026-07-21.png and runs/showcase-mapsize/
  (archived 2026-07-23 to archive/runs-2026-07-23/showcase-mapsize; three picks
  promoted to runs/demo/).
- **Camera smoothing** lags visibly only under BRAWLER_TICKS_PER_FRAME fast-forward
  (smoothing is per rendered frame); real-time play converges in ~0.5 s.
- **Option b (validate traversal against actual character physics)** did NOT ship
  beyond the hook noted above: `StageRules.IsConnected` is the abstract baseline;
  wiring character-aware validation into generation remains future tinkering.

## 2026-07-22 follow-up (designer playtest feedback)

The designer observed agents cycling back and forth faster than human input and never
attacking on large maps. Diagnosis (per-tick decision tracing): on wide/tall maps the
agents spend most of the approach airborne, and two behaviors burned the AIR jump
during that time — `ThreatDodge` hopping to flinch while airborne, and traversal
hopping between the two adjacent same-height center platforms of a mirrored stage.
Burning the air jump strands the agent in `AirJumpsExhausted`, which by FSM (Unity
parity) can neither attack nor jump; `ExhaustedCaution` then drives a retreat, so the
agent drifts back, lands, re-approaches, and repeats — the observed oscillation, and
the reason it never attacked. Fix (DEVIATIONS #28): the flinch-dodge hops only when
grounded, and traversal jumps only for a real height gain or gap (walks across
contiguous ground). Over 30 seeds/map, exhausted-time fell 65→45% (wide), 72→20%
(tall), 47→21% (a legacy game); damage output rose; a champion that timed out 22/30
now KOs 30/30; the flagship legacy GameC is materially unchanged. Regression test:
`UtilityAgentTests.TraversalWalksBetweenAdjacentSameHeightPlatforms`. Utility golden
re-pinned; DT golden and genome fingerprint untouched (agent-independent). Videos:
runs/media/mapsize-large-mirrored-fixed.mp4, mapsize-evolved-fixed.mp4.

Also added the PROJECTILE schema to the evolve-menu advanced ranges (and the CLI
`--range` help), closing the pre-existing gap where projectile genes were the only
move type not per-run tunable. Verified end-to-end: an override
(`projectile.velocity=12:15`) records in run.json and constrains generation (observed
velocities 12.07–14.97). Screenshot: docs/reports/img/evolve-ranges-2026-07-22.png.
