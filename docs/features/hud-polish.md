# Feature: HUD polish (FEATURES.md §Visual Polish → HUD)

Date: 2026-07-23/24. Spec: FEATURES.md §HUD items 1–11 + design/BrawlerAGDHUD.jpg
(the three-sticky-note mockup: debug panel / main panel / percent-roll notes).
Pure VIEW-LAYER feature — no genome, sim, trace, or fitness impact; no goldens moved.

## Designer decisions (2026-07-23)

1. **Identity colors**: the state-tint-AVOIDING set — P1 rose, P2 sky, P3 gold,
   P4 teal (`PlayerPalette`) — so an identity color never reads as a body state.
   (Known soft collision: P3 gold ≈ projectile gold; P3/P4 unused until the sim
   grows past two players.)
2. **Slots**: four static quarter-width HUDs along the bottom (each ALWAYS 1/4 of
   the screen), LEFT-PACKED — P1 quarter 1, P2 quarter 2; 3–4 reserved.
3. **Key-layout hints**: the transient beside-the-character panels (2026-07-23 AM)
   are REPLACED by the debug strip — `ControlsHintView` deleted the same day it
   shipped; `BRAWLER_FORCE_HINTS` is gone with it.
4. **Pause menu**: a real navigable menu (`PauseMenuView`): RESUME / DEBUG PANEL
   ON-OFF / SETTINGS (the minimap popup, now reachable mid-match) / QUIT TO MENU;
   mouse, keyboard, and pad navigation; ESC resumes, Q still quits. The
   debug-panel toggle persists to user://settings.cfg (`AppSettings.DebugPanelEnabled`,
   default ON).

## What shipped

**Main panel** (per player, `HudView.Slot`): solid dark background outlined 2 px in
the identity color; name in a colored pill (matching the new in-world name-tag
pill on `PlayerView`); stock DOTS that switch to "N STOCKS" text past 8 (spec #6);
the character's actual sprite; a big damage % that ROLLS through interim numbers
(~0.35 s), growing with hit magnitude until the final roll (mockup note). On a hit
the panel shakes subtly — hit player only, amplitude scaled by current damage,
capped for readability; on a death it shakes hard and flashes white (spec #8/#9).
On death the % also rolls back down to 0 — kept deliberately, reads like a reset.

**Debug strip** (above each panel, semi-transparent, default ON, spec #2/#11):
- Human-readable state, tinted to match the body's state color: READY, AIRBORNE,
  EXHAUSTED, WINDING UP, ATTACKING, RECOVERING, STUNNED / SHIELD BROKEN,
  SHIELDING, DASHING, CROUCHING, RESPAWNING n.n s (spec #3).
- INTG/INVL timing bars for the spawn intangible/invulnerable windows (spec #3.1),
  fractions computed against the stage's evolved durations; hidden when inactive.
- The DI arrow (spec #3.2), relocated from its old floating spot.
- The full control layout: JUMP + five keycaps (keyboard names for P1, pad names
  for P2) with each button's move name (ATK1/ATK2/SHLD/DASH/PROJ from the genome's
  buttonMoves), keycaps lighting up ON PRESS — including AI presses, so the strip
  doubles as a live view of the fitness instrument's inputs.

**Automation**: `BRAWLER_PAUSE_AT=<tick>` opens the pause menu at a sim tick and
saves a "paused" screenshot (replaces BRAWLER_FORCE_HINTS in the env list).

## Verification (2026-07-24)

Screenshots: both panels + strips + pills at spawn (INVL bars mid-drain, AIRBORNE
green), the death frame (white panel flash + shake offset + RESPAWNING countdown +
percent mid-roll), and the pause menu (focus on RESUME). Demo videos re-recorded:
runs/media/polish-fixes-demo.mp4 / projectile-trails-demo.mp4 /
bigmap-koflash-demo.mp4. 280 tests green; BrawlerSim untouched.
