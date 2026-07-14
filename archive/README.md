# Archived runs & demo games (moved 2026-07-14)

Everything here predates game.json formatVersion 4 (the current generator's design
space: 2 attacks + shield + dash on four pinned buttons). All files still LOAD with
current code — v1–v3 compatibility is guaranteed by the loaders — but the genomes are
structurally from earlier design-space eras, so they no longer represent what the
generator produces and were moved out of `runs/` to keep it current.

Reports in `docs/reports/` reference these by their old `runs/<name>` paths.

| Entry | Era / purpose |
|---|---|
| `smoke`, `ui-smoke`, `run-1` | v1–v2 scratch runs from the initial pipeline & app bring-up |
| `replicate-101/202/303` | v1 — the paper replication study (plateau criterion + design motifs) |
| `compare-dtree-*` / `compare-utility-*` | v2, standard-v2 — the DecisionTree vs UtilityAgent instrument comparison (docs/reports/2026-07-09-utility-agent-comparison.md) |
| `noise-r9-*` / `noise-r9div-*` | v2 — fitness-noise & diversity-weight study (docs/reports) |
| `v3-r9-*`, `v3-r9-90s-401`, `v3cs05-r9-*` | v2 — standard-v3 fitness shaping + collision-scalar tuning |
| `flank-r9-*` | v2 — flank/attack-position agent behavior validation |
| `traverse-601/602` | v2 — platform-graph next-hop traversal fix validation |
| `exhaust-601/602` | v2 — exhausted-jumpers-don't-chase fix validation |
| `stun-*-50x` | v2 — stun-cap experiments (0.75/1.5/3.0/inf) |
| `jump-0xx-60x` | v2 — jump-reward fitness term sweep |
| `shield-701/702`, `shieldrw-701/702` | v3 — shield feature smoke + shield-reward fitness |
| `gamea–f.json` | v1 — Unity-imported reference games |
| `shield-demo.json`, `shield-scripted.json` (+trace), `twomove-demo.json` | v2–v3 hand-built feature demo games |
