# Gameplay Polish (FEATURES.md §Gameplay Polish) — design record

Date: 2026-07-22. Three features from the designer's FEATURES.md §Gameplay Polish:
one deterministic-sim feature (Spawning Behaviors) and two pure view-layer features
(Death Animations, Movement Blur). Designer decisions captured 2026-07-22.

## Spawning Behaviors (sim + agent + view)

### Model

A spawn produces, in order:
1. **(respawns only)** a fixed 3 s blackout — the character is absent (no collision,
   no hits, not rendered) — then it appears on a spawn platform at its existing spawn
   gene position. Match start skips the blackout (appears on the platform immediately).
2. **On the platform → intangible**: ignores damage AND character collision (the
   opponent passes through it and cannot push it off). Free to move / jump / attack.
3. **Intangibility ends** on the FIRST of: leaving the platform, the platform timer
   expiring, or attacking (a melee or projectile move) while still on the platform.
   When the player leaves or the timer expires, the platform despawns (fades to the
   background in the view).
4. **Invulnerable** (ignores damage, KEEPS character collision) runs on its own timer
   and ends STRICTLY when that timer expires — independent of the platform and of
   attacking. So a player can leave/attack, regain collision, and still be damage-proof
   until the invuln timer runs out.

Two distinct concepts, deliberately separated by the designer:
- **Intangible** = damage-immune + collision-pass-through (opponent phases through).
- **Invulnerable** = damage-immune only (still bumps the opponent).

### Parameters (evolvable stage genes, game.json v8)

Appended to the `stage` schema (append-only preserves crossover indexing):

| Key | Generation range | Valid floor | Meaning |
|---|---|---|---|
| `platformSpawnDuration` | 1–5 s | 0 | Spawn platform lifetime (also the intangibility cap) |
| `spawnInvulnDuration` | 1–3 s | 0 | Character damage-immunity (invulnerable) duration |

Both generate in their live range but VALIDATE down to 0 (the DI/knockbackModX
precedent, DEVIATIONS #13): the loader defaults pre-v8 files to 0, which is the
mechanic OFF — instant vulnerable spawn, exactly today's behavior. This keeps every
existing replay and BOTH match goldens bit-identical; only the population fingerprint
re-pins (two new generation draws). The whole feature is gated on
`spawnFeatureActive = platformSpawnDuration > 0 || spawnInvulnDuration > 0`, a
per-match (level) constant — off ⇒ the sim is byte-for-byte the pre-feature sim,
including the state-hash (the spawn hash section is a gated suffix, like projectiles).

The 3 s respawn blackout is a fixed `MatchConfig.RespawnBlackoutSeconds` (not evolved),
and only engages when the feature is active.

### Sim implementation

`SimPlayer` gains hashed fields (hashed only when the feature is active): a respawn
blackout countdown, a spawn-invuln countdown, an intangible flag, and a spawn-pad
active flag. `SimWorld` gains a per-player **spawn pad** (a temporary platform at the
spawn position, geometry static, active-state mutable): it is solid to its OWNER only
(built into a per-player `platforms + pad` list handed to that player's physics step;
the opponent's step never sees it, so the opponent phases through). Intangible players
skip player-vs-player contact entirely; damage-immune players (intangible OR invuln)
take no damage/knockback in the melee and projectile hit pipelines. Attacking clears
intangibility in `StartMove` (melee + projectile only, not shield/dash). Tick order:
blackout/materialize → spawn-timer countdowns → FSM → physics (pad solid) → leave/expiry
detection → contact/projectiles/hits → blast/respawn. A KO with stocks left starts the
blackout instead of an instant respawn; the initial spawn seeds the pad+flags directly
(no blackout).

### Agent (DEVIATIONS #29)

The utility instrument treats a damage-immune opponent (spawn-intangible or
spawn-invulnerable) as unhittable — `canHit` is forced false, so the attack / projectile
/ doomed behaviors do not swing at a ghost ("agents shouldn't attempt to attack an
invulnerable enemy"). Gated on the SPAWN immunity only, NOT the 0.1 s post-hit
invincibility, so legacy matches (feature off) leave the instrument — and its golden —
untouched.

### Rollout

Implemented live as an evolvable feature; validated by trial + paper-scale evolutions
and a bug hunt (watching for spawn-camping, since invuln follows its timer regardless
of attacking) before the designer playtest.

## Death Animations (view-only)

**Re-rendered 2026-07-23 (designer):** a Smash-KO-style burst — a TALL, NARROW white
streak that shoots from the point of death PERPENDICULARLY into the screen (off the
bottom → straight up; off the right → left; the inward normal of the crossed blast
edge). No outline or border; never wider than the character (width capped at the
victim's on-screen body width). Juiced: the streak snaps out over ~0.1 s, holds, then
fades over ~0.45 s, with a bright pop at its base. The anchor is the death point
projected onto the crossed screen edge through the camera view; length/brightness scale
with KO speed + damage. Fires on every stock loss and the final KO, during the
blackout. Purely cosmetic — reads the KO-tick velocity/damage the sim already exposes;
zero sim/fitness/determinism impact.

(The first pass was an edge-anchored ellipse pointing toward arena center; the designer
asked for the perpendicular-inward tall streak above instead.)

**Always-visible + diagonals (2026-07-23, designer):** the trigger is now CAMERA-
relative, not blast-relative — the death point maps into view fractions
(unclamped), and: off one axis of the screen ⇒ the streak sits on that edge
pointing perpendicularly inward; off BOTH axes (past a screen corner — a fast
diagonal KO the lagging camera didn't follow) ⇒ it sits in that corner pointing
diagonally at the CAMERA CENTER (normalized in pixels, true on 16:9); still inside
the view (a visible blast edge) ⇒ it fires from the actual death point,
perpendicular to the crossed blast edge. The anchor is always clamped on screen,
so every KO is telegraphed no matter how far from the action it lands.

**Flame cone (2026-07-23, designer):** the needle alone read as "a single needle
flying into the screen" — a translucent cone now expands around it (base 0.6× the
needle width → 1.6–4.2× at the far end, widening over the flash's life) and fades
with it, so the burst reads as a flame. Life extended 0.45 s → 0.65 s for
readability.

## Movement Blur (view-only)

A directional smear (shader) stretching the character sprite along its velocity,
driven by SCREEN-SPACE speed (world speed × camera zoom) so trails appear exactly when
a character is hard to track on-screen at any zoom (designer). Tinted by the PlayerView
state color, characters only (projectiles keep their gold rendering), capped so it
never reads as a second body. Purely cosmetic; headless/fitness untouched.

**Re-rendered as AFTERIMAGES (2026-07-23, designer: "trails do not seem to be
working, or are not noticeable"):** the shader smeared UVs inside the sprite's own
quad, so it could never draw OUTSIDE the body — it read as a faint dimming, not a
trail. Replaced with ghost afterimages, each keeping the state tint it had when
sampled (a warm-up→attack chain shows yellow→red history). Opacity ramps with
screen-space speed — invisible at a walk, unmistakable under knockback/dash.

**Third pass (2026-07-23, designer: "blurring too much at low speed"):** opacity
now follows an EXPONENTIAL ease-in over the speed range (k = 3.5) and ghosts never
pack closer than 22 px along the path, so a short slow-speed path gets a few
separated ghosts instead of an alpha-compounding solid blob.

**Fourth pass (2026-07-27, designer: still too much at normal speeds):** the ramp
floor moved ABOVE normal run/jump speeds — ghosts start at 850 screen-px/s
(~12 u/s at legacy zoom) and reach full at 2800 — so ordinary movement draws
NOTHING, dashes read as a bare hint, and only knockback/KO flights get the streak
("the intent is to track when characters move extremely fast"). A KO linger is
boosted only when a trail was already visible; a slow drift into the blast zone no
longer conjures one. Verified: zero ghosts on moving/attacking players at normal
speeds, full continuous streak on a 55 u/s KO flight.

**Second pass (2026-07-23, designer: high-speed KOs still read as TELEPORTS):**
two defects at knockback speeds — a fixed 200 px "teleport" clear threshold that a
fast KO legitimately exceeds every frame (the trail erased itself precisely when
it mattered), and fixed-stride ghosts that left the flight path empty between
samples. Now 12 ghosts distribute at EQUAL ARC-LENGTH intervals along a 10-frame
polyline (interpolating between samples ⇒ a continuous streak at any speed), the
teleport test is VELOCITY-AWARE (clears only on a jump far beyond what the current
velocity explains — real respawn/materialize snaps), and a trail detached by a
KO/teleport LINGERS in place, fading over ~0.5 s, instead of vanishing with the
body on the death frame. Verified mid-flight on a 55 u/s KO: an unbroken
stun-magenta streak traces the whole visible trajectory.

**Projectile trails (2026-07-23, designer):** each bolt gets a slight gold comet
tail — 3 afterimage polygons at fixed low alphas (0.28/0.16/0.07, scaled by the
bolt's decay fade), 2 frames apart, behind the bolt. Deliberately less pronounced
than the player trail; purely cosmetic — hitboxes are sim-side and unaffected by
rendering. Slot reuse in the compacted sim list starts a fresh tail (no ghost
smearing between different bolts). Demo: runs/media/projectile-trails-demo.mp4.

## Key-layout hint (2026-07-23, designer, view-only) — SUPERSEDED same day

Replaced by the HUD debug strip (docs/features/hud-polish.md): `ControlsHintView`
and `BRAWLER_FORCE_HINTS` were deleted when the always-on (toggleable) per-player
control display landed. Original design for the record:

Each HUMAN player's controls float beside their character for the first ~9 s of a
match (then fade), so the button→move mapping is readable on first load: P1 shows
the keyboard layout (A/D · SPACE · I/J/K/U/L), P2 the pad layout (STICK · B ·
L1/X/A/Y/R1), with move names pulled from the genome's buttonMoves gene (slot
number appended when a type repeats, e.g. ATTACK 1 / ATTACK 2). Screen-space,
player-tracking, clamped on screen; the fade countdown freezes while paused;
AI/replay players show nothing. `BRAWLER_FORCE_HINTS=1` shows both panels in any
mode (screenshot/recording automation). `ControlsHintView.cs`.

## Per-character platform fit (2026-07-23, designer bug report)

Generated (and crossed/mutated) levels could let ONE character move between platforms
while the other couldn't, and a gap could be fall-through-passable for a small body
but a wall for a large one. Fix (DEVIATIONS #30): after the stage AND both characters
are known, `StageRules.FitToCharacters` MOVES platforms — deterministically, never by
re-rolling (which would desync the RNG stream) — so that:
- every platform is reachable from platform 0 by BOTH characters, using the SAME
  hop-feasibility model as the agent's `PlatformGraph` (jumps + air speed + scaled
  gravity, no dash — the pathfinder ignores dashes, so fitting to a dash-inclusive
  reach would still leave the agent unable to route); and
- no horizontal gap is fall-through-passable for the smaller body but a wall for the
  larger (widened to clear the larger body when that stays hop-able).

The repair pulls each unreachable platform toward its nearest reachable connector in
integer steps (trying every connector, nearest first), lowering it until the climb is
within reach then closing the gap, and — for a very weak character — docking it
contiguous at the connector's height (a walkable surface). Every move is
overlap-guarded (reverted rather than allowed to overlap). It runs in Generate,
Crossover, and Mutate; RNG-free, so the generation stream stays aligned (only platform
coordinates change → fingerprint re-pins, match goldens unmoved). Spawns are
re-repaired against the adjusted layout. Already-fair stages are returned untouched
(identity).

**Iterative solver (2026-07-27, designer: asymmetric gaps still appeared in play):**
the first body-gap pass was single-sweep with a single strategy (slide the right
platform right; skip on failure), which left residual asymmetric corridors. It is
now an iterative solver: it RE-SCANS after every repositioning (a fix can open a new
violation elsewhere) and rotates through five strategies per violating pair — widen
right, widen left, dock right, dock left, vertical separation — accepting a move
only if it introduces no overlap and does not shrink both-character connectivity.
Loop-prevention is structural: per-pair attempt counters start each revisit on a
DIFFERENT strategy, a pair that exhausts its attempts is force-resolved by docking
(a contiguous wall is symmetric for every body, and touching platforms cannot
overlap), and a pair that cannot even dock is retired as unresolvable — so open work
strictly shrinks and the solver always terminates (pass and round budgets back-stop
it). Connectivity and body-fit phases alternate until a full round is quiet.
Audited over 800 generation seeds: asymmetric corridors fell from 248 stages
(671 corridors, measured with the solver disabled) to **ZERO**, with unchanged
both-character connectivity (61 vs 62 dense-layout failures) and zero overlaps.
Property-tested in CI (200 seeds must yield zero corridors, zero overlaps) plus a
chain-of-corridors termination fixture. Trial evolutions healthy
(runs/media/charts/gap-solver-trajectories.png).

## Aesthetics ledger additions

- Spawn platform (2026-07-23 render): a SOLID white PILL (rounded rectangle,
  full-height end caps) with a bright flat top edge and a subtle darker
  underside — reads as a platform; quick fade on despawn. (Was a semi-transparent
  oval; the designer asked for solid + platform-like.)
- Invulnerable player: a slow shimmer pulse — distinct from post-hit invincibility
  (steady 0.4 alpha) and dash i-frames (fast strobe).
- Death flash (2026-07-23 render): a tall, narrow, borderless white streak from the
  death point, perpendicular-inward from the crossed screen edge (diagonal toward
  the camera center from a corner), never wider than the character; snap-out,
  hold, fade, base pop.
- Motion trail (2026-07-23 render): state-tinted afterimage ghosts,
  screen-space-speed scaled, cleared on teleports.
- Key-layout hint: outlined HUD-style text panel beside each human player,
  ALL-CAPS, fading after ~9 s.

## What shipped (2026-07-22) — verification notes

- **Legacy parity confirmed:** feature-off (durations 0) is byte-for-byte the
  pre-feature sim — both match goldens unmoved, only the population fingerprint
  re-pinned (two new generation draws). 277 tests green incl. 9 new
  SpawnBehaviorTests (the invuln/intangible distinction, both timers, attack- and
  leave-ends-intangibility, blackout→materialize, pad-owner-only collision).
- **Spawn-camping measured negligible:** over an 80-generation evolved population
  (feature on in all 60 games; platform ~2.4 s, invuln ~1.2 s mean), only 0.4% of all
  damage was dealt by an immune attacker, and 7% of matches timed out. The degenerate
  "attack from safety" strategy did not emerge; evolution favored modest, shorter-than-
  max invuln. Retunable via the stage-gene ranges if a later run turns degenerate.
- **Visual verification:** spawn pads render as flat rounded ovals under each player at
  spawn; the invuln shimmer, the hidden blackout body, the edge-anchored KO flash
  (bright core + halo), and the subtle directional motion smear were all confirmed on
  captured frames (runs/media/spawn-polish-demo.mp4). The death flash's `DrawArc`-dome
  bug was fixed (all rings are ellipses now); the flash brightness was bumped (halo +
  hot core) after a first pass read too dim.
- **Open item for the playtest:** the invuln shimmer / pad / flash tunings
  (`MatchConfig.SpawnPad*`, flash radius/alpha, blur reference speed) are all named
  constants — feel iteration is a constant change, not a refactor.
