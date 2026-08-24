using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sprites;
using Xunit;
using NG = NameGen;

namespace BrawlerSim.Tests.Sprites;

/// <summary>
/// Melee attack sprite selection (2026-08-23, M4b —
/// docs/features/attack-sprite-selection.md): the brief's six test families against
/// the shipped moves_v2 library — determinism, per-character distinctness, the
/// compatiblePlans hard filter, the wields thematic link, class-spread shaping (with
/// the designer's goofy-object floor), and legacy round-trip.
/// </summary>
public class AttackSpriteSelectionTests
{
    private static readonly Lazy<SpriteLibrary> LibraryLazy = new(() =>
        SpriteLibrary.LoadFile(FindRepoFile(Path.Combine("godot", "assets", "players_v2_slices.json"))));

    private static readonly Lazy<SpriteSelectionConfig> TuningLazy = new(() =>
        SpriteSelectionConfig.LoadFile(FindRepoFile(Path.Combine("godot", "assets", "sprite_selection.json"))));

    private static MoveSpriteLibrary Moves => SpriteSelectionTests.MoveLibraryLazy.Value;

    private static SpriteSelector NewSelector() =>
        new(LibraryLazy.Value, TuningLazy.Value, moveLibrary: Moves);

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

    private static IEnumerable<(MoveGenome Move, MoveSpriteDef Sprite)> AttackPicks(CharacterGenome c) =>
        c.Moves.Where(m => m.Type == MoveType.Attack)
            .Select(m => (m, Moves.ById(m.SpriteId)
                ?? throw new Xunit.Sdk.XunitException($"attack move has no/unknown sprite id ({m.SpriteId})")));

    // ── the library itself ──────────────────────────────────────────────────────

    [Fact]
    public void ShippedMoveLibraryParsesCompletely()
    {
        Assert.Equal(111, Moves.Sprites.Count);
        var vocabulary = LibraryLazy.Value.TraitVocabulary.ToHashSet(StringComparer.Ordinal);
        Assert.All(Moves.Sprites, s =>
        {
            Assert.False(string.IsNullOrEmpty(s.Id));
            Assert.True(s.W > 0 && s.H > 0);
            Assert.False(string.IsNullOrEmpty(s.AttackClass));
            Assert.NotEmpty(s.CompatiblePlans);
            Assert.All(s.TraitAffinity.Keys, t => Assert.Contains(t, vocabulary));
        });
        // Every character's wields entries name real attack classes, so the thematic
        // link can never dangle.
        var classes = Moves.Sprites.Select(s => s.AttackClass).ToHashSet(StringComparer.Ordinal);
        Assert.All(LibraryLazy.Value.Sprites, c =>
        {
            Assert.NotEmpty(c.Wields);
            Assert.All(c.Wields, w => Assert.Contains(w, classes));
        });
    }

    // ── 1: determinism ──────────────────────────────────────────────────────────

    [Fact]
    public void SameGenomeGivesTheSameAttackSpritesEverywhere()
    {
        GameGenome first = Game(60);
        for (int run = 0; run < 20; run++)
        {
            GameGenome again = Game(60); // fresh selector, fresh generation
            Assert.Equal(
                first.Characters.SelectMany(c => c.Moves.Select(m => m.SpriteId)),
                again.Characters.SelectMany(c => c.Moves.Select(m => m.SpriteId)));
        }
        Assert.All(first.Characters, c => Assert.NotEmpty(AttackPicks(c)));
    }

    // ── 2 + 3: distinctness and the hard plan filter (property, 2,000 characters) ─

    [Fact]
    public void AttacksAreDistinctPerCharacterAndAlwaysPlanCompatible()
    {
        int bladesOnNonBipeds = 0;
        for (ulong seed = 1; seed <= 1000; seed++)
        {
            foreach (CharacterGenome c in Game(seed).Characters)
            {
                SpriteDef body = LibraryLazy.Value.ById(c.SpriteId)!;
                var picks = AttackPicks(c).ToList();
                Assert.Equal(picks.Count, picks.Select(p => p.Sprite.Id).Distinct().Count());
                foreach ((_, MoveSpriteDef sprite) in picks)
                {
                    Assert.Contains(body.BodyPlan, sprite.CompatiblePlans);
                    if (sprite.AttackClass == "blade" && body.BodyPlan != "biped")
                    {
                        bladesOnNonBipeds++;
                    }
                }
                // Non-attack slots never carry a move sprite gene.
                Assert.All(c.Moves.Where(m => m.Type != MoveType.Attack),
                    m => Assert.Null(m.SpriteId));
            }
        }
        // Blades ship biped-only in the data, so the hard filter makes this exactly
        // zero — "quadrupeds never swing swords" with no goof-budget exception needed
        // (objects, not blades, are the goofy channel).
        Assert.Equal(0, bladesOnNonBipeds);
    }

    // ── 4: the wields thematic link ─────────────────────────────────────────────

    [Fact]
    public void ModalAttackClassFollowsTheCharactersFirstWieldsEntry()
    {
        int matched = 0;
        int total = 0;
        for (ulong seed = 1; seed <= 250; seed++)
        {
            foreach (CharacterGenome c in Game(seed + 5000).Characters)
            {
                SpriteDef body = LibraryLazy.Value.ById(c.SpriteId)!;
                var classes = AttackPicks(c).Select(p => p.Sprite.AttackClass).ToList();
                if (classes.Count == 0)
                {
                    continue;
                }
                total++;
                // With two attacks per fighter, ties are the common case — the first
                // wields entry "is the modal class" when it sits among the argmax set.
                int max = classes.GroupBy(x => x).Max(g => g.Count());
                if (classes.Count(x => x == body.Wields[0]) == max)
                {
                    matched++;
                }
            }
        }
        Assert.True(total >= 500, $"only {total} characters sampled");
        Assert.True(matched > total * 0.5,
            $"modal class matched first wields for {matched}/{total} ({matched / (double)total:0.###})");
    }

    // ── 5: class spread — blades capped, objects decently goofy, all reachable ──

    [Fact]
    public void ClassSpreadHoldsAcrossTwoThousandPicks()
    {
        var byClass = new Dictionary<string, int>(StringComparer.Ordinal);
        int picks = 0;
        for (ulong seed = 1; picks < 2000; seed++)
        {
            foreach (CharacterGenome c in Game(seed + 9000).Characters)
            {
                foreach ((_, MoveSpriteDef sprite) in AttackPicks(c))
                {
                    byClass[sprite.AttackClass] =
                        byClass.TryGetValue(sprite.AttackClass, out int n) ? n + 1 : 1;
                    picks++;
                }
            }
        }
        double Share(string cls) => byClass.TryGetValue(cls, out int n) ? n / (double)picks : 0;
        Assert.True(Share("blade") <= 0.35, $"blade share {Share("blade"):0.###}");
        // Designer: goofy stays decently likely — the object budget pins ~10% of
        // every pick; per character (two attacks) that's ~1 in 5 fighters carrying
        // something ridiculous.
        Assert.InRange(Share("object"), 0.07, 0.15);
        foreach (string cls in Moves.Sprites.Select(s => s.AttackClass).Distinct())
        {
            Assert.True(byClass.ContainsKey(cls), $"class '{cls}' never selected");
        }
    }

    // ── 6: legacy + serialization ───────────────────────────────────────────────

    [Fact]
    public void MoveSpriteIdsSurviveRoundTripAndLegacyFilesReadAsNull()
    {
        GameGenome game = Game(70);
        string json = GameGenomeJson.Serialize(new GameRecord("g", null, game));
        GameGenome reloaded = GameGenomeJson.Deserialize(json).Genome;
        Assert.Equal(
            game.Characters.SelectMany(c => c.Moves.Select(m => m.SpriteId)),
            reloaded.Characters.SelectMany(c => c.Moves.Select(m => m.SpriteId)));

        GameGenome legacy = Game(70, sprites: false);
        string legacyJson = GameGenomeJson.Serialize(new GameRecord("g", null, legacy));
        Assert.All(GameGenomeJson.Deserialize(legacyJson).Genome.Characters,
            c => Assert.All(c.Moves, m => Assert.Null(m.SpriteId)));
    }

    [Fact]
    public void ContentKeyAndNamingSeedIgnoreMoveSpriteGenes()
    {
        // The per-move seed derives from NamingSeed, which derives from ContentKey:
        // assigning move genes must never shift it, or repair-time selection would
        // disagree with generation-time selection.
        CharacterGenome bare = Game(80, sprites: false).Characters[0];
        CharacterGenome sprited = Game(80).Characters[0]; // same params, genes assigned
        Assert.Equal(BuiltGame.ContentKey(bare), BuiltGame.ContentKey(sprited));
        Assert.Equal(BuiltGameNaming.NamingSeed(bare), BuiltGameNaming.NamingSeed(sprited));
    }

    // ── heredity + repair ───────────────────────────────────────────────────────

    [Fact]
    public void CrossoverInheritsAttackSpritesAndRepairFixesPlanViolations()
    {
        GenerationConfig config = SpriteConfig();
        for (ulong seed = 91; seed <= 95; seed++)
        {
            GameGenome parent = Game(seed);
            GameGenome child = GameGenomeOps.Crossover(parent, parent, new Pcg32(seed), config);
            Assert.Equal(
                parent.Characters.SelectMany(c => c.Moves.Select(m => m.SpriteId)),
                child.Characters.SelectMany(c => c.Moves.Select(m => m.SpriteId)));
        }

        // Force a plan violation: hand a floating character a biped-only blade — the
        // repair rule must re-resolve it (the hard filter holds across inheritance).
        SpriteSelector selector = NewSelector();
        MoveSpriteDef bipedOnly = Moves.Sprites.First(s =>
            s.CompatiblePlans.Count == 1 && s.CompatiblePlans[0] == "biped");
        CharacterGenome victim = Enumerable.Range(1, 200)
            .Select(s => Game((ulong)s + 300).Characters[0])
            .First(c => LibraryLazy.Value.ById(c.SpriteId)!.BodyPlan != "biped");
        var moves = victim.Moves.ToList();
        int attack = Enumerable.Range(0, moves.Count).First(m => moves[m].Type == MoveType.Attack);
        moves[attack] = moves[attack].WithSpriteId(bipedOnly.Id);
        var broken = new CharacterGenome(victim.Name, victim.Stocks, victim.SpriteIndex,
            victim.Params, moves, victim.ButtonMoves, victim.SpriteId);

        CharacterGenome repaired = selector.EnsureMoveGenes(broken);
        MoveSpriteDef fixedSprite = Moves.ById(repaired.Moves[attack].SpriteId)!;
        Assert.NotEqual(bipedOnly.Id, fixedSprite.Id);
        Assert.Contains(LibraryLazy.Value.ById(victim.SpriteId)!.BodyPlan, fixedSprite.CompatiblePlans);
    }

    [Fact]
    public void HeavyRosterUsageSteersAwayFromASingleSpriteClass()
    {
        // Cross-roster duplication is a soft penalty (the brief), applied at BOTH
        // stages — a one-sprite class (whip = the lone bullwhip) must stop winning
        // once the roster has worn it out, even for whip-first characters.
        SpriteSelector selector = NewSelector();
        var usage = new Dictionary<string, int> { ["mv_bullwhip"] = 5 };
        int bullwhips = 0;
        int sampled = 0;
        for (ulong seed = 1; seed <= 300 && sampled < 60; seed++)
        {
            CharacterGenome c = Game(seed + 7000, sprites: false).Characters[0];
            ulong s = SpriteSelector.SpriteSeed(c);
            string body = selector.ResolveSpriteId(c, s);
            SpriteDef entry = LibraryLazy.Value.ById(body)!;
            if (!entry.Wields.Take(2).Contains("whip"))
            {
                continue;
            }
            sampled++;
            string id = selector.SelectMoveSpriteId(c.Moves[0], entry,
                selector.PickRegister(s), SpriteSelector.MoveSpriteSeed(c, 0), null, usage);
            if (id == "mv_bullwhip")
            {
                bullwhips++;
            }
        }
        Assert.True(sampled >= 20, $"only {sampled} whip-leaning characters found");
        Assert.True(bullwhips <= sampled / 10,
            $"bullwhip still picked {bullwhips}/{sampled} times under 5 prior uses");
    }

    [Fact]
    public void EvolutionDoesNotRatchetObjectsPastTheBudget()
    {
        // The banana ratchet (2026-08-23): converged populations collide move sprites
        // in crossover constantly; if every collision repair re-rolled the 10% object
        // lottery, objects — in every wields list, with no traits to contradict —
        // would absorb entire lineages (observed: 4/4 sampled evolved fighters carried
        // one). Repair picks on merit (budget as cap only); chained breeding must stay
        // near the fresh-pick rate.
        GenerationConfig config = SpriteConfig();
        var rng = new Pcg32(777);
        GameGenome a = Game(501);
        GameGenome b = Game(502);
        int objects = 0;
        int attacks = 0;
        for (int i = 0; i < 200; i++)
        {
            GameGenome child = GameGenomeOps.Breed(a, b, 0.4f, rng, config);
            foreach (CharacterGenome c in child.Characters)
            {
                foreach ((_, MoveSpriteDef sprite) in AttackPicks(c))
                {
                    attacks++;
                    if (sprite.AttackClass == "object")
                    {
                        objects++;
                    }
                }
            }
            a = b;
            b = child;
        }
        Assert.True(objects <= attacks * 0.25,
            $"objects ratcheted to {objects}/{attacks} ({objects / (double)attacks:0.###})");
    }

    // ── the presentation pass ───────────────────────────────────────────────────

    [Fact]
    public void PresentationSettlesMoveSpritesAroundTheNegotiatedLook()
    {
        var game = new BuiltGame { Name = "ARMORY" };
        for (ulong i = 0; i < 4; i++)
        {
            GameGenome g = Game(400 + i, sprites: false); // v10-era shape: NO genes at all
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P1", null, g.Characters[0]), out _));
            Assert.True(game.TryAddCharacter(new BuiltCharacter("SRC P2", null, g.Characters[1]), out _));
            Assert.True(game.TryAddStage(new BuiltStage("SRC STAGE", null, g.Stage), out _));
        }
        BuiltGamePresentation.EnsurePresented(game, NG.NameGenerator.CreateDefault(), NewSelector());

        foreach (BuiltCharacter entry in game.Characters)
        {
            Assert.NotNull(entry.MoveSpriteIds);
            Assert.Equal(entry.Character.Moves.Count, entry.MoveSpriteIds!.Count);
            SpriteDef body = LibraryLazy.Value.ById(entry.SpriteId)!;
            var attackIds = new List<string>();
            for (int m = 0; m < entry.Character.Moves.Count; m++)
            {
                if (entry.Character.Moves[m].Type != MoveType.Attack)
                {
                    Assert.Null(entry.MoveSpriteIds[m]);
                    continue;
                }
                MoveSpriteDef sprite = Moves.ById(entry.MoveSpriteIds[m])!;
                Assert.Contains(body.BodyPlan, sprite.CompatiblePlans);
                attackIds.Add(sprite.Id);
                // Presented injects the settled id for views and match launches.
                Assert.Equal(sprite.Id, entry.Presented.Moves[m].SpriteId);
            }
            Assert.Equal(attackIds.Count, attackIds.Distinct().Count());
        }

        // Persisted once + round-trip.
        Assert.Equal(0, BuiltGamePresentation.EnsurePresented(
            game, NG.NameGenerator.CreateDefault(), NewSelector()));
        BuiltGame reloaded = BuiltGameJson.Deserialize(BuiltGameJson.Serialize(game));
        Assert.Equal(
            game.Characters.Select(c => string.Join("|", c.MoveSpriteIds!)),
            reloaded.Characters.Select(c => string.Join("|", c.MoveSpriteIds!)));
    }
}
