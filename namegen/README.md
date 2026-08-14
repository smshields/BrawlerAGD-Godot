# NameGen

Offline, data-driven name generator for procedurally generated fighting game characters and stages. Zero dependencies, no engine coupling, netstandard2.1 (loads in Godot 3 Mono and Godot 4 .NET).

Names point at the genome: continuous params are normalized against the game schema's ranges, folded into features (bulk, floatiness, commitment, projectile character...), scored into salient traits, and those traits bias which roots the grammar picks and how candidates are ranked. Provenance comes back with every name so you can see exactly why it was chosen.

## Integration

Reference the project (or the built `NameGen.dll`) from your Godot C# project:

```xml
<ItemGroup>
  <ProjectReference Include="path/to/namegen/src/NameGen/NameGen.csproj" />
</ItemGroup>
```

```csharp
using NameGen;

// Once, at startup. Loads and validates the embedded database.
var generator = NameGenerator.CreateDefault();

// Or, while tuning data without recompiling:
// var generator = NameGenerator.CreateFromDirectory("res://../namegen-data");

// Per generated game, for roster-unique names:
var session = new UniqueNameSession(generator);

var characterGenome = new CharacterGenome(
    parameters: paramDict,             // IReadOnlyDictionary<string,float>, keys = game.json param keys
    moves: new List<MoveGenome> {
        new(MoveKind.Melee, meleeParams),
        new(MoveKind.Projectile, projectileParams),
        new(MoveKind.Shield, shieldParams),
        new(MoveKind.Dash, dashParams),
    });

NameResult name = session.GenerateCharacterName(characterGenome);
GD.Print(name.Display);                 // "Gorthak Jenkins"
GD.Print(name.Register);                // "fantasy"
foreach (var t in name.SalientTraits)   // heavy:0.82, brutal:0.44
    GD.Print($"{t.Name}:{t.Score}");

NameResult stage = session.GenerateStageName(new StageGenome(stageParams));
```

Names are generated once at game creation and stored by the caller; the library holds no state beyond `UniqueNameSession`'s used-name set. `NameOptions` exposes `Seed` (reproducibility), `Register`/`Shape` (force), `BleedProbability`/`MundaneProbability` (override the comedy dials), `CandidateCount`.

Unknown param keys are ignored and missing keys read as neutral, so schema appends never break naming; update `Data/schema-ranges.json` when ranges change.

## Layout

```
src/NameGen/            the library (ship this)
  Api/                  NameGenerator, UniqueNameSession, genome POCOs, results
  Features/             genome -> feature extraction (owns all schema quirks)
  Traits/               feature -> trait scoring, salience
  Engine/               template filling, bleed/mundane hijacks, trait boosts
  Core/                 PRNG, sampler, joiner, phonetic scorer, blocklist
  Json/                 dependency-free JSON parser
  Data/                 embedded database (JSON)
tests/NameGen.Tests/    dependency-free runner: dotnet run --project tests/NameGen.Tests
tools/NameGen.Cli/      batch review: dotnet run --project tools/NameGen.Cli -- demo | dump
```

Tests use a built-in harness because the library targets zero external packages end to end; asserts are xUnit-shaped, so migrating to xUnit is add-package + rename `[Test]` to `[Fact]` + delete `TestFramework.cs`.

## The database

All content lives in `src/NameGen/Data/*.json`, embedded into the DLL at build. Any file can be overridden at runtime via `CreateFromDirectory(dir)` (same relative paths), which is the tuning loop: edit JSON, re-run the CLI, read the CSV. No recompile.

### Registers (`registers/*.json`)

Four registers: `fantasy`, `scifi`, `horror`, `normal`. Each carries its own morphemes, templates, joiner rules, shape weights, and the two comedy dials:

- `bleedProbability`: chance per slot of borrowing a morpheme from another register ("Dreadis" the sci-fi fighter).
- `mundaneProbability`: chance (once per name) of the mundane pool hijacking a slot ("Spatulamir", "Gorthak Jenkins", "The Rotting Food Court"). Mundane entries live in `mundane.json` and deliberately carry no trait tags; the joke is that they point at nothing.

### Morphemes

```json
{ "form": "gor", "positions": ["prefix", "suffix"], "tags": ["heavy", "brutal"],
  "weight": 1.0, "gloss": "brute strength" }
```

- `positions`: `prefix` / `suffix` (fusable root parts), `standalone` (whole mononym), `given` / `family` (word-per-slot names), `adjective` / `place` (stage grammar). One morpheme may fill several.
- `tags`: trait names from `traits.json`. Tagged morphemes get weight-boosted when their trait is salient for the genome; the first home-register slot of every name is guaranteed to come from a trait-matching morpheme when any exists.
- Forms may contain spaces ("parking garage") and hyphens; capitalization is applied at assembly.

### Templates

```json
{ "kind": "character", "shape": "single", "weight": 3,
  "slots": [ { "position": "prefix" }, { "position": "suffix", "join": "fuse" } ] }
```

`join` is how a slot attaches to the previous one: `fuse` (boundary-repaired concatenation), `space`, `hyphen`, `apostrophe`. `literal` slots emit fixed text with `#` = random digit, `@` = random uppercase letter ("Vekta-7"). `allowMundane` overrides the default hijack eligibility (default: any non-suffix slot).

### Traits (`traits.json`)

Character and stage trait matrices: each trait is a weighted sum over features (weight × (2·feature − 1), so 0.5 is neutral) plus phoneme affinities used to rank candidates by sound symbolism (heavy → back vowels and voiced plosives, swift → front vowels and voiceless stops). `boostFactor`, `salienceTopK`, `salienceThreshold` are the global pointing dials: raise boost for more literal names, lower it for subtler ones.

Feature names are produced by `Features/FeatureExtractor.cs`; that file is the single place that knows schema semantics (bools-as-floats ≥ 0.5, ints-as-floats floored, off-at-zero params like `directionalInfluence` reading as absent rather than low).

### Generation pipeline

```
genome -> features -> salient traits -> register -> shape -> template
       -> slots filled (trait boosts, bleed/mundane rolls, no-repeat within name)
       -> joiner (boundary repair, run collapsing, capitalization)
       -> N candidates -> blocklist filter -> legality + phoneme-affinity score
       -> sample from top half -> NameResult with provenance
```

The scorer samples from the surviving top half rather than taking the argmax, on purpose: its job is to filter garbage, not converge the whole roster onto one house style.

## CLI

```
dotnet run --project tools/NameGen.Cli -- demo
dotnet run --project tools/NameGen.Cli -- dump --count 500 --out names.csv
dotnet run --project tools/NameGen.Cli -- dump --count 200 --register horror --kind stage
dotnet run --project tools/NameGen.Cli -- dump --count 100 --data src/NameGen/Data
```

CSV columns include the salient traits, chosen morphemes, glosses, and bleed/mundane flags, so a tuning pass is: dump, sort, read, edit JSON, dump again.

## Extension points already accounted for

- Register selection is currently uniform; your sound/visual orchestration can force it via `NameOptions.Register` without touching anything downstream. Deriving one register per game (shared by its characters and stage) is recommended for coherence.
- Epithets ("X the Y") are a template-data addition; the slot grammar already supports literals and word joins.
- Move naming: run per-move features through the same pipeline with a move-template set; no engine changes required.
- Population calibration: if evolution drifts your population so "fast" stops being distinctive, replace range-midpoint neutrality by shipping percentile data and recentering features before trait scoring. The seam is `FeatureExtractor`.
