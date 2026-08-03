# BrawlerAGD-Godot

Automated game design for 2D brawler (platform-fighter) games: a genetic algorithm evolves
characters, moves, and stages, evaluated by AI self-play against a fitness function. This is
the Godot 4 / C# successor to the Unity project behind *"Searching for Balanced 2D Brawler
Games: Successes and Failures of Automated Evaluation"* (Shields, Mawhorter, Melcer, Mateas —
AIIDE 2022). Original Unity implementation: [smshields/BrawlerAGD](https://github.com/smshields/BrawlerAGD).

**Status:** the Unity→Godot conversion is complete — schema/genome layer, deterministic sim
core, evolution engine + CLI, full Godot app (Play / Watch / Evolve / Manage), export
pipeline, and a replication study validating evolution dynamics against the paper. Current
work adds new evolvable mechanics (multi-move controls, shields, dashes, projectiles,
evolvable stages) on top; [FEATURES.md](FEATURES.md) is the design spec.

## Architecture in one paragraph

All gameplay lives in **`BrawlerSim`**, a pure .NET library with a fixed-tick (60 Hz)
deterministic simulation — no Godot references, no wall clock, no `System.Random` (a seeded
PCG32 stream per match). The Godot project (**`godot/`**) is strictly a view: it samples human
input into per-tick input structs and draws sim state. Headless evolution (**`BrawlerRunner`**)
steps the *same* `Tick()` in a tight loop, thousands of times faster than real time, in
parallel across the population. Because rendered play and simulated play execute identical
code, the matches the fitness function grades are exactly the matches humans can play and
watch — enforced in CI by per-tick state-hash comparison and an engine-free grep gate on the
sim core.

## Layout

| Path | What it is |
|------|------------|
| `BrawlerSim/` | Deterministic sim core: genome schema, match simulation, agents, fitness, evolution engine |
| `BrawlerSim.Tests/` | xUnit suite — determinism golden values, genome ops, fitness regression |
| `BrawlerRunner/` | Headless CLI for evolution runs, replays, re-evaluation |
| `godot/` | Godot 4.7 (.NET) project — arena view, app shell (Evolve / Play / Manage) |
| `FEATURES.md` | Design spec for the game's mechanics and evolvable feature set |
| `runs/` | (gitignored) evolution run output |

## Prerequisites

- .NET SDK 8.x (`brew install dotnet@8`)
- Godot 4.7+ (.NET edition) — only needed for the view layer / editor work

## Commands

```sh
dotnet test    # build + run the full test suite

# headless evolution run (checkpoints + results under runs/<name>/)
dotnet run --project BrawlerRunner -c Release -- evolve --out runs/x --seed 1 --pop 100 --generations 300 --rounds 5
```

Open `godot/project.godot` in the Godot editor for the game/view layer.

## License

TBD — the Unity predecessor is licensed for research use; pick and add a license before
publishing this repository.
