# Feature: Multi-Move Control Scheme (Phase 6, feature 1)

Designer-approved answers to the CLAUDE.md design questions, recorded 2026-07-08.
This feature is the **input-side foundation** for multiple moves per character: it does
NOT add new moves yet — it adds the four assignable action buttons, the genome's
button→move mapping, and the control-scheme rework, with all four buttons mapped to the
single existing move so agent behavior (and therefore what fitness measures) is
unchanged in effect.

## Scope (designer's brief)

- 2-player mode is disabled unless a gamepad is connected — the full keyboard belongs
  to player 1 from now on.
- Movement: WASD. W (up) and S (down) do nothing *yet* but are captured as a vertical
  axis for future features.
- Jump: Space bar (dedicated, non-assignable — the agent's jump logic stays independent
  of button mapping).
- Actions: I/J/K/L are four assignable buttons. Which move each button triggers is a
  genome parameter; multiple different attacks per button become possible when
  MovesPerCharacter > 1.
- For now all four buttons resolve to move 0 — no new agent complexity.

## The 10 design questions

1. **Parameterizable?** Yes. `CharacterGenome.ButtonMoves` — an integer gene array of
   length `InputFrame.ActionCount` (4), each an index into the character's move list.
   Structural gene (like `SpriteIndex`), NOT a float `ParamSpec`: its valid range
   `[0, MovesPerCharacter-1]` is `GenerationConfig`-dependent and cannot live in a
   static schema range. Genetic ops mirror the sprite-gene precedent: generation =
   `rng.NextInt(moves.Count)` per button; crossover = per-button coin flip between
   parents; mutation = re-randomize (inert while there is one move).
2. **Bounds?** Button indices always valid move indices; loader clamps/validates.
   With MovesPerCharacter = 1 every mapping is 0, so gameplay is provably unchanged
   (equivalence baseline: scratchpad equivalence-baseline.txt — GameA/C/F × seeds
   11/20260707, all 30 rounds must reproduce exactly).
3. **Agent impact?** The agent now emits button presses instead of a single `Attack`
   bit: when it wants to attack it presses the LOWEST-index button mapped to its
   desired move (move 0 for now — with all-zero mappings that is button 0 / key I).
   Behavior-equivalent to the old scheme; DEVIATIONS.md gets an entry because the
   *trace vocabulary* changes even though decisions don't.
4. **Assets?** None. Menu hint text updates only.
5. **Entities/states?** No new entities or FSM states. `SimPlayer` holds all resolved
   `SimMove`s plus `CurrentMoveIndex` (new mutable state → added to `StateHash`).
6. **Controller differences?** Yes — `InputFrame` becomes
   `(Horizontal, Vertical, Jump, Actions[4])`. Keyboard P1: A/D move, W/S vertical
   (inert), Space jump, I/J/K/L actions. Gamepad (designer-specified): X (left face)
   and A (bottom face) = assignable buttons J and K; Y (top) and B (right) = jump;
   L1/R1 = assignable buttons I and L. P2 keyboard bindings are REMOVED.
   Device assignment: P2 = first gamepad (device 0); P1 = keyboard + optional second
   gamepad (device 1).
7. **Fitness/research data?** `standard-v2` stays blind — no new terms. Per-move usage
   stats deferred to the actual multiple-moves feature (nothing to distinguish yet).
8. **Serialization & legacy?**
   - game.json: `formatVersion` 1 → 2. `CharacterDoc` gains `buttonMoves` (int array).
     v1 files (all existing evolved games + Unity imports) load with default all-zeros.
     Files are written as v2.
   - Traces: rows grow from 3 to 7 values per player `(h, v, j, a0..a3)`. The reader
     detects the old 3-value rows by length and maps `attack → a0`, `vertical → 0` —
     old traces replay with identical behavior (a0 triggers move 0).
9. **Rollout?** Live immediately, including in evolution — with one move per character
   the mapping gene is inert, so this is not gated. Evolution genuinely searches the
   mapping only when MovesPerCharacter grows (a later feature).
10. **Readability?** No new telegraphing needed while all buttons are identical. Menu
    hint text documents the new controls. When moves differ, per-button move display
    becomes part of that feature's design.

## 2P gating (designer-selected semantics)

`PLAY — 2 PLAYERS` is enabled only while ≥1 gamepad is connected (live-updates on
connect/disconnect). P2 plays on the gamepad; P1 on the keyboard (or a second pad).
Disabled state shows a "CONNECT A CONTROLLER" hint.

## Determinism notes

- `CurrentMoveIndex` joins `StateHash` → all golden hashes re-pin (dated comments).
- Match *outcomes* must NOT change: the equivalence baseline above is the gate.
- Agent decision logic untouched; only its output encoding changes.

## Shipped (2026-07-09, pending designer play-test)

- `InputFrame` = `(Horizontal, Vertical, Jump, Actions)` with `ActionCount = 4`,
  `ActionBit/ActionPressed/FirstAction` helpers; lowest-pressed-button tie-break is part
  of the input contract.
- `CharacterGenome.ButtonMoves` gene + ctor validation; generation/crossover/mutation
  mirror the sprite-gene ops but consume RNG **only when Moves.Count > 1** — proven
  RNG-neutral by diffing 200 bred genomes' every value against a pre-feature build.
- `SimPlayer`: all moves resolved (`Moves`), `CurrentMoveIndex` (hashed), button
  dispatch in Idle/Air, `ButtonForMove` for the agent.
- Agent emits the lowest button mapped to move 0; decisions untouched.
- Traces: 7 values/player, PascalCase keys as before; 3-value legacy rows upgrade on
  read (attack→button 0) — proven replay-identical (FinalHash) on a GameC match.
- game.json formatVersion 2 (`buttonMoves` per character); v1 loads with zeros.
- Behavior equivalence gate: GameA/C/F × seeds 11/20260707 — all 30 rounds identical
  fitness/length/loser/damage/hits/stocks pre/post.
- Goldens re-pinned 2026-07-09 with dated comments: match hash 8640048477680184839
  (StateHash format grew), population fingerprint 13551893661434631362 (JSON format
  grew). 115 tests green.
- Godot: Boot bindings (WASD + Space + IJKL; pads: Y/B jump, X=J, A=K, L1=I, R1=L;
  P2 = pad 0, P1 = keyboard + optional pad 1), HumanInputSource samples vertical +
  4 action edges, 2P gated on a connected controller in MainMenu + ManageView
  (live-updates on connect/disconnect), menu hints updated.
- Visual evidence: runs/media/shots/menu-new-controls.png (gated 2P button + new
  hints), tick screenshots of an AI match (WarmUp tint via the new button path),
  runs/media/gamec_seed11_new-controls.mp4.

## Amendment 2026-07-20: five assignable buttons (designer-directed)

Jump shrank to a SINGLE button (pad B; Space unchanged), freeing pad Y:
`InputFrame.ActionCount` is now 5. Layout: I/J/K/U/L keys ↔ L1/X/A/Y/R1 pad —
U/Y is the NEW slot at index 3 so L/R1 stays the LAST button (the dash pin's
physical home, unchanged). Compatibility contract, hash-verified end to end on a
real evolved champion + trace (identical final hash under the pre- and
post-change builds):

- game.json → v6; 4-entry buttonMoves migrate as [b0, b1, b2, b0, b3] — the new
  slot duplicates button 0's move (no legacy trace ever presses it), and the old
  button 3 (R1/L) keeps its move at the new last index.
- Traces: 8 values/player; 7-value rows (4-button era) upgrade with a0..a2 in
  place and old a3 → NEW BIT 4, mirroring the buttonMoves migration; 3-value
  rows (v1) unchanged.
- Composed checkpoints recording 4-slot compositions refuse to resume with an
  explicit error (archived; their artifacts stay loadable/replayable).
- Population fingerprint re-pinned (the pinned mapping gene now draws over four
  mappable buttons); the DT and utility match goldens did not move.
