using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sprites;
using Xunit;
using NG = NameGen;

namespace BrawlerSim.Tests.Sprites;

/// <summary>
/// Sprite selection (2026-08-22, docs/features/sprite-selection.md): the brief's six
/// test families against the REAL shipped library (godot/assets/players_v2_slices.json)
/// — determinism, legacy round-trip, the repair rule, distribution shaping, register
/// coherence, and thin-register fallback — plus the evolution-stream isolation proofs
/// the goldens rely on.
/// </summary>
public class SpriteSelectionTests
{
    // ── shared fixtures ─────────────────────────────────────────────────────────

    private static readonly Lazy<SpriteLibrary> LibraryLazy = new(() =>
        SpriteLibrary.LoadFile(FindRepoFile(Path.Combine("godot", "assets", "players_v2_slices.json"))));

    private static readonly Lazy<SpriteSelectionConfig> TuningLazy = new(() =>
        SpriteSelectionConfig.LoadFile(FindRepoFile(Path.Combine("godot", "assets", "sprite_selection.json"))));

    private static SpriteLibrary Library => LibraryLazy.Value;

    private static SpriteSelector NewSelector() => new(Library, TuningLazy.Value);

    private static GenerationConfig SpriteConfig(int players = 2) =>
        GenerationConfig.Default with { CharacterCount = players, SpriteSelector = NewSelector() };

    private static GameGenome Game(ulong seed, int players = 2, bool sprites = true) =>
        GameGenome.Generate(
            sprites ? SpriteConfig(players) : GenerationConfig.Default with { CharacterCount = players },
            new Pcg32(seed));

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"could not locate {relative} above the test directory");
    }

    // ── the library and tuning files themselves ─────────────────────────────────

    [Fact]
    public void ShippedLibraryParsesCompletely()
    {
        Assert.Equal(490, Library.Sprites.Count);
        Assert.Equal(30, Library.TraitVocabulary.Count);
        Assert.All(Library.Sprites, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.Id));
            Assert.True(s.W > 0 && s.H > 0);
            Assert.NotEmpty(s.Registers);
        });
        // Every affinity key stays inside the declared vocabulary (opaque-data contract:
        // the C# side never interprets names, but the data must be self-consistent).
        var vocabulary = Library.TraitVocabulary.ToHashSet(StringComparer.Ordinal);
        Assert.All(Library.Sprites, s =>
            Assert.All(s.TraitAffinity.Keys, t => Assert.Contains(t, vocabulary)));
        Assert.NotNull(Library.ById("dcss_deep_elf_knight"));
        Assert.Null(Library.ById("no_such_sprite"));
    }

    [Fact]
    public void ShippedTuningFileBindsEveryKnob()
    {
        SpriteSelectionConfig tuning = TuningLazy.Value;
        // The shipped file starts at the code defaults; if this fails the two drifted —
        // retune BOTH or neither (the JSON is the hot-editable source of truth).
        Assert.Equal(SpriteSelectionConfig.Default, tuning);
    }

    [Fact]
    public void FeyRegisterHasARealPool()
    {
        // 2026-08-22 data edit: fey must clear the register pool floor so fey names
        // get fey-looking sprites instead of the full-library fallback.
        int fey = Library.Sprites.Count(s => s.Registers.Contains("fey"));
        Assert.True(fey >= TuningLazy.Value.RegisterPoolFloor, $"fey pool is {fey}");
    }

    // ── 1: determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void SameGenomeAndSeedGiveTheSamePresentationEverywhere()
    {
        CharacterGenome character = Game(11, sprites: false).Characters[0];
        ulong seed = SpriteSelector.SpriteSeed(character);

        SpritePresentation first = NewSelector().Negotiate(
            character, seed, NG.NameGenerator.CreateDefault());
        for (int run = 0; run < 100; run++)
        {
            // Fresh selector + fresh generator every time: nothing may depend on
            // instance state, iteration order, wall clock, or global RNG.
            SpritePresentation again = NewSelector().Negotiate(
                character, seed, NG.NameGenerator.CreateDefault());
            Assert.Equal(first, again);
        }
        Assert.True(Library.Contains(first.SpriteId));
        Assert.False(string.IsNullOrWhiteSpace(first.DisplayName));
    }

    [Fact]
    public void GenerationResolvesTheSameSpritesFromTheSameSeed()
    {
        GameGenome a = Game(77, players: 4);
        GameGenome b = Game(77, players: 4);
        Assert.Equal(
            a.Characters.Select(c => c.SpriteId),
            b.Characters.Select(c => c.SpriteId));
        Assert.All(a.Characters, c => Assert.True(Library.Contains(c.SpriteId)));
    }

    // ── 2: legacy round-trip ────────────────────────────────────────────────────

    [Fact]
    public void SpriteIdSurvivesTheFileRoundTripAndLegacyFilesReadAsNull()
    {
        GameGenome game = Game(5);
        string json = GameGenomeJson.Serialize(new GameRecord("g", null, game));
        Assert.Contains("\"spriteId\"", json);

        GameGenome reloaded = GameGenomeJson.Deserialize(json).Genome;
        Assert.Equal(
            game.Characters.Select(c => c.SpriteId),
            reloaded.Characters.Select(c => c.SpriteId));

        // A v9-era document (no spriteId anywhere) still loads, with null genes and
        // no spriteId in its re-serialized bytes.
        GameGenome legacy = Game(5, sprites: false);
        string legacyJson = GameGenomeJson.Serialize(new GameRecord("g", null, legacy));
        Assert.DoesNotContain("spriteId", legacyJson);
        Assert.All(GameGenomeJson.Deserialize(legacyJson).Genome.Characters,
            c => Assert.Null(c.SpriteId));
    }

    [Fact]
    public void ContentKeyAndNamingSeedIgnoreTheSpriteGene()
    {
        // The naming/sprite seed derives from ContentKey; selection and repair change
        // the look, so the key (and therefore the shared seed) must not move with it —
        // and legacy (null-gene) characters must keep their pre-feature bytes exactly.
        CharacterGenome bare = Game(9, sprites: false).Characters[0];
        CharacterGenome sprited = bare.WithSpriteId("dcss_deep_elf_knight");

        Assert.Equal(BuiltGame.ContentKey(bare), BuiltGame.ContentKey(sprited));
        Assert.Equal(BuiltGameNaming.NamingSeed(bare), BuiltGameNaming.NamingSeed(sprited));
        Assert.DoesNotContain("SpriteId", BuiltGame.ContentKey(sprited));
    }

    // ── 3: the repair rule ──────────────────────────────────────────────────────

    [Fact]
    public void RepairFiresWhenParamsContradictTheInheritedSprite()
    {
        SpriteSelector selector = NewSelector();

        // A max-mass, max-bulk character is salient "heavy"; a sprite whose only
        // affinities are elsewhere (tiny/swift) scores 0 — below any positive floor.
        CharacterGenome character = Game(21, sprites: false).Characters[0];
        var schema = character.Params.Schema;
        CharacterGenome heavy = new(
            character.Name, character.Stocks, character.SpriteIndex,
            character.Params.With(
                (CharacterParams.Mass, schema[schema.IndexOf(CharacterParams.Mass)].Max),
                (CharacterParams.WidthScalar, schema[schema.IndexOf(CharacterParams.WidthScalar)].Max),
                (CharacterParams.HeightScalar, schema[schema.IndexOf(CharacterParams.HeightScalar)].Max)),
            character.Moves, character.ButtonMoves,
            Library.Sprites.First(s =>
                s.TraitAffinity.ContainsKey("tiny") && !s.TraitAffinity.ContainsKey("heavy")
                && !s.TraitAffinity.ContainsKey("giant")).Id);

        Assert.True(selector.NeedsRepair(heavy));
        CharacterGenome repaired = selector.EnsureGene(heavy);
        Assert.NotEqual(heavy.SpriteId, repaired.SpriteId);
        Assert.False(selector.NeedsRepair(repaired)); // the fix itself must stick
    }

    [Fact]
    public void MildDriftKeepsTheInheritedSprite()
    {
        // The selector's own pick is by construction above the floor: EnsureGene on an
        // already-resolved genome is the identity (heredity stands).
        SpriteSelector selector = NewSelector();
        foreach (CharacterGenome character in Game(22, players: 4).Characters)
        {
            Assert.Same(character, selector.EnsureGene(character));
        }
    }

    [Fact]
    public void NeutralGenomesContradictNothing()
    {
        // No salient traits → nothing for a sprite to disagree with → any known
        // sprite stands, even a wildly specific one.
        SpriteSelector selector = NewSelector();
        var neutral = new Dictionary<string, float>();
        foreach (var spec in DefaultSchemas.Character.Specs)
        {
            neutral[spec.Key] = (spec.Min + spec.Max) / 2f;
        }
        CharacterGenome character = Game(23, sprites: false).Characters[0];
        var mid = new CharacterGenome(character.Name, character.Stocks, character.SpriteIndex,
            BrawlerSim.Params.ParamSet.FromDictionary(DefaultSchemas.Character, neutral),
            character.Moves, character.ButtonMoves, Library.Sprites[0].Id);
        if (selector.Salient(mid).Count == 0)
        {
            Assert.False(selector.NeedsRepair(mid));
        }
        // Unknown ids always re-resolve, salient or not.
        Assert.True(selector.NeedsRepair(mid.WithSpriteId("gone_from_library")));
        Assert.True(selector.NeedsRepair(mid.WithSpriteId(null)));
    }

    // ── 4: distribution shaping ─────────────────────────────────────────────────

    [Fact]
    public void TwoThousandSelectionsHoldTheShapedDistribution()
    {
        SpriteSelector selector = NewSelector();
        var picks = new Dictionary<string, int>(StringComparer.Ordinal);
        var registers = new HashSet<string>(StringComparer.Ordinal);
        int biped = 0;
        int goofy = 0;
        const int n = 2000;
        for (ulong seed = 1; seed <= n; seed++)
        {
            CharacterGenome character = Game(seed, sprites: false).Characters[(int)(seed % 2)];
            SpriteCandidate top = selector.SelectCandidates(
                character, seed * 0x9E3779B97F4A7C15UL, out string register)[0];
            registers.Add(register);
            picks[top.Sprite.Id] = picks.TryGetValue(top.Sprite.Id, out int c) ? c + 1 : 1;
            if (top.Sprite.BodyPlan == "biped")
            {
                biped++;
            }
            if (top.Sprite.Vibe == "goofy")
            {
                goofy++;
            }
        }
        Assert.True(biped <= n * 0.58, $"biped share {biped / (double)n:0.###}");
        Assert.InRange(goofy / (double)n, 0.08, 0.18);
        int most = picks.Values.Max();
        Assert.True(most <= n * 0.02,
            $"a single sprite took {most / (double)n:0.###} of picks ({picks.MaxBy(kv => kv.Value).Key})");
        Assert.Equal(5, registers.Count); // fantasy, scifi, horror, normal, fey — all reachable
    }

    // ── 5: register coherence ───────────────────────────────────────────────────

    [Fact]
    public void SpriteRegisterAndNameRegisterCanNeverDisagree()
    {
        SpriteSelector selector = NewSelector();
        var generator = NG.NameGenerator.CreateDefault();
        int filtered = 0;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            CharacterGenome character = Game(seed % 25 + 1, sprites: false).Characters[0];
            ulong spriteSeed = seed * 0x2545F4914F6CDD1DUL;
            IReadOnlyList<SpriteCandidate> candidates =
                selector.SelectCandidates(character, spriteSeed, out string register);

            // The name half: forcing the shared register through NameOptions holds.
            SpritePresentation presentation = selector.Negotiate(character, spriteSeed, generator);
            Assert.Equal(register, presentation.Register);
            Assert.Equal(register, generator.GenerateCharacterName(
                SpriteSelector.Map(character),
                new NG.NameOptions { Seed = spriteSeed, Register = register }).Register);

            // The sprite half: with a pool at/above the floor, every candidate carries
            // the picked register (below it, the documented full-library fallback).
            int pool = Library.Sprites.Count(s => s.Registers.Contains(register));
            if (pool >= selector.Config.RegisterPoolFloor)
            {
                filtered++;
                Assert.All(candidates, c => Assert.Contains(register, c.Sprite.Registers));
            }
        }
        Assert.True(filtered >= 100, "most picks should hit registers with real pools");
    }

    // ── 6: thin registers must not starve ───────────────────────────────────────

    [Fact]
    public void ThinRegistersFallBackToTheFullLibraryWithoutThrowing()
    {
        // scifi ships with ~14 sprites — below the 20 floor — so its picks must
        // engage the fallback and still return a full candidate list.
        SpriteSelector selector = NewSelector();
        CharacterGenome character = Game(31, sprites: false).Characters[0];
        int scifiPicks = 0;
        for (ulong seed = 1; seed <= 400 && scifiPicks < 5; seed++)
        {
            IReadOnlyList<SpriteCandidate> candidates =
                selector.SelectCandidates(character, seed, out string register);
            Assert.Equal(selector.Config.CandidateCount, candidates.Count);
            if (register == "scifi")
            {
                scifiPicks++;
            }
        }
        Assert.True(scifiPicks >= 5, "400 seeds never picked scifi — register weights broke");
        int pool = Library.Sprites.Count(s => s.Registers.Contains("scifi"));
        Assert.True(pool < selector.Config.RegisterPoolFloor,
            "scifi grew past the floor — retire this fixture for a synthetic thin register");
    }

    // ── evolution-stream isolation (the golden-hash guarantee) ──────────────────

    [Fact]
    public void SpriteSelectionNeverPerturbsGenerationOrBreedingStreams()
    {
        // Same seed with and without a sprite library: every param, move, button gene
        // and platform must be byte-identical — SpriteId is the ONLY difference.
        // (ContentKey excludes SpriteId; stage docs never had one.)
        for (ulong seed = 1; seed <= 10; seed++)
        {
            GameGenome with = Game(seed, players: 2);
            GameGenome without = Game(seed, players: 2, sprites: false);
            Assert.Equal(
                without.Characters.Select(c => BuiltGame.ContentKey(c)),
                with.Characters.Select(c => BuiltGame.ContentKey(c)));
            Assert.Equal(BuiltGame.ContentKey(without.Stage), BuiltGame.ContentKey(with.Stage));

            var rngA = new Pcg32(seed * 31);
            var rngB = new Pcg32(seed * 31);
            GameGenome childWith = GameGenomeOps.Breed(with, Game(seed + 100), 1f, rngA, SpriteConfig());
            GameGenome childWithout = GameGenomeOps.Breed(
                without, Game(seed + 100, sprites: false), 1f, rngB, GenerationConfig.Default);
            Assert.Equal(
                childWithout.Characters.Select(c => BuiltGame.ContentKey(c)),
                childWith.Characters.Select(c => BuiltGame.ContentKey(c)));
            Assert.Equal(BuiltGame.ContentKey(childWithout.Stage), BuiltGame.ContentKey(childWith.Stage));
        }
    }

    [Fact]
    public void CrossoverInheritsAParentsLook()
    {
        // Identical parents: the child's params equal the parent's, so no repair can
        // fire and the coin flip lands on the same inherited sprite either way.
        GenerationConfig config = SpriteConfig();
        for (ulong seed = 41; seed <= 45; seed++)
        {
            GameGenome parent = Game(seed);
            GameGenome child = GameGenomeOps.Crossover(parent, parent, new Pcg32(seed), config);
            Assert.Equal(
                parent.Characters.Select(c => c.SpriteId),
                child.Characters.Select(c => c.SpriteId));
        }
    }

    // ── the built-game presentation pass ────────────────────────────────────────

    private static BuiltGame NewBuiltGame(ulong seed, bool sprites = false)
    {
        var game = new BuiltGame { Name = "TEST GAME" };
        for (ulong i = 0; i < 4; i++)
        {
            GameGenome g = Game(seed + i, players: 2, sprites: sprites);
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P1", null, g.Characters[0]), out _));
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P2", null, g.Characters[1]), out _));
            Assert.True(game.TryAddStage(new BuiltStage("SRC STAGE", null, g.Stage), out _));
        }
        return game;
    }

    [Fact]
    public void PresentationPassSettlesNamesSpritesAndRegistersTogether()
    {
        BuiltGame game = NewBuiltGame(500);
        int changed = BuiltGamePresentation.EnsurePresented(
            game, NG.NameGenerator.CreateDefault(), NewSelector());
        Assert.Equal(12, changed); // 8 characters + 4 stages

        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (BuiltCharacter c in game.Characters)
        {
            Assert.False(BuiltGameNaming.NeedsGeneratedName(c.DisplayName));
            Assert.True(names.Add(c.DisplayName), $"duplicate roster name {c.DisplayName}");
            Assert.True(Library.Contains(c.SpriteId));
            Assert.False(string.IsNullOrEmpty(c.Register));
            Assert.Equal(c.SpriteId, c.Presented.SpriteId); // views read Presented
        }
        Assert.All(game.Stages, s => Assert.False(BuiltGameNaming.NeedsGeneratedName(s.DisplayName)));

        // Persisted once: a second pass over the settled game changes nothing.
        Assert.Equal(0, BuiltGamePresentation.EnsurePresented(
            game, NG.NameGenerator.CreateDefault(), NewSelector()));

        // Deterministic: a fresh copy of the same content settles identically.
        BuiltGame again = NewBuiltGame(500);
        BuiltGamePresentation.EnsurePresented(again, NG.NameGenerator.CreateDefault(), NewSelector());
        Assert.Equal(
            game.Characters.Select(c => (c.DisplayName, c.SpriteId, c.Register)),
            again.Characters.Select(c => (c.DisplayName, c.SpriteId, c.Register)));
    }

    [Fact]
    public void PresentationKeepsManualNamesButStillAssignsSprites()
    {
        BuiltGame game = NewBuiltGame(600);
        game.Characters[0] = game.Characters[0] with { DisplayName = "The Designer's Favorite" };
        BuiltGamePresentation.EnsurePresented(game, NG.NameGenerator.CreateDefault(), NewSelector());
        Assert.Equal("The Designer's Favorite", game.Characters[0].DisplayName);
        Assert.True(Library.Contains(game.Characters[0].SpriteId));
        Assert.False(string.IsNullOrEmpty(game.Characters[0].Register));
    }

    [Fact]
    public void BuiltGameV2RoundTripsPresentationAndReadsV1WithNulls()
    {
        BuiltGame game = NewBuiltGame(700);
        BuiltGamePresentation.EnsurePresented(game, NG.NameGenerator.CreateDefault(), NewSelector());
        string json = BuiltGameJson.Serialize(game);
        Assert.Contains("\"spriteId\"", json);
        Assert.Contains("\"register\"", json);

        BuiltGame reloaded = BuiltGameJson.Deserialize(json);
        Assert.Equal(
            game.Characters.Select(c => (c.DisplayName, c.SpriteId, c.Register)),
            reloaded.Characters.Select(c => (c.DisplayName, c.SpriteId, c.Register)));

        // A v1-shaped document (no presentation fields) loads with nulls.
        BuiltGame v1 = BuiltGameJson.Deserialize(BuiltGameJson.Serialize(NewBuiltGame(700)));
        Assert.All(v1.Characters, c => Assert.Null(c.Register));
    }

    [Fact]
    public void OveruseDivergesDuplicateFighters()
    {
        // The per-game overuse penalty exists so identical fighters in one roster
        // don't share a face: re-selecting under a usage count usually moves.
        SpriteSelector selector = NewSelector();
        int diverged = 0;
        const int n = 50;
        for (ulong seed = 1; seed <= n; seed++)
        {
            CharacterGenome character = Game(seed, sprites: false).Characters[0];
            ulong spriteSeed = SpriteSelector.SpriteSeed(character);
            string first = selector.ResolveSpriteId(character, spriteSeed);
            string second = selector.ResolveSpriteId(character, spriteSeed,
                new Dictionary<string, int> { [first] = 1 });
            if (first != second)
            {
                diverged++;
            }
        }
        Assert.True(diverged >= n / 2, $"only {diverged}/{n} duplicate rosters diverged");
    }
}
