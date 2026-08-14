using System;
using System.Collections.Generic;
using System.Linq;
using NameGen;
using NameGen.Core;
using NameGen.Data;
using NameGen.Features;
using NameGen.Json;
using NameGen.Traits;

namespace NameGen.Tests
{
    public class MiniJsonTests
    {
        [Test]
        public void ParsesScalarsObjectsArraysCommentsTrailingCommas()
        {
            var v = MiniJson.Parse("{ // comment\n \"a\": [1, 2.5, -3e2,], \"b\": { \"c\": true, \"d\": null, }, \"s\": \"x\\ny\\u0041\" }");
            var o = (Dictionary<string, object?>)v!;
            var a = (List<object?>)o["a"]!;
            Assert.Equal(3, a.Count);
            Assert.Equal(2.5, (double)a[1]!);
            Assert.Equal(-300.0, (double)a[2]!);
            var b = (Dictionary<string, object?>)o["b"]!;
            Assert.Equal(true, (bool)b["c"]!);
            Assert.True(b["d"] == null);
            Assert.Equal("x\nyA", (string)o["s"]!);
        }

        [Test]
        public void RejectsMalformedInput()
        {
            Assert.Throws<FormatException>(() => MiniJson.Parse("{ \"a\": }"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("[1, 2"));
            Assert.Throws<FormatException>(() => MiniJson.Parse("{\"a\":1} trailing"));
        }
    }

    public class PcgTests
    {
        [Test]
        public void SameSeedSameSequence()
        {
            var a = new Pcg32(12345);
            var b = new Pcg32(12345);
            for (int i = 0; i < 100; i++)
                Assert.Equal(a.NextUInt(), b.NextUInt());
        }

        [Test]
        public void DifferentSeedsDiverge()
        {
            var a = new Pcg32(1);
            var b = new Pcg32(2);
            bool diverged = false;
            for (int i = 0; i < 10; i++)
                if (a.NextUInt() != b.NextUInt()) { diverged = true; break; }
            Assert.True(diverged);
        }

        [Test]
        public void NextIntStaysInBounds()
        {
            var rng = new Pcg32(7);
            for (int i = 0; i < 1000; i++)
            {
                int v = rng.NextInt(7);
                Assert.InRange(v, 0, 6);
            }
        }
    }

    public class JoinerTests
    {
        private static readonly JoinerRulesDef Rules = new()
        {
            MaxConsonantRun = 3, MaxVowelRun = 2,
            BannedClusters = new List<string> { "tsk" },
            MinLength = 3, MaxLength = 14,
        };

        [Test]
        public void FusesAndCapitalizes()
        {
            var parts = new List<Joiner.Part> { new("gor", "fuse"), new("thak", "fuse") };
            Assert.Equal("Gorthak", Joiner.Assemble(parts, Rules));
        }

        [Test]
        public void CollapsesDoubledBoundaryLetter()
        {
            var parts = new List<Joiner.Part> { new("kag", "fuse"), new("gor", "fuse") };
            Assert.Equal("Kagor", Joiner.Assemble(parts, Rules));
        }

        [Test]
        public void ResolvesVowelCollision()
        {
            var parts = new List<Joiner.Part> { new("mira", "fuse"), new("ara", "fuse") };
            // trailing a + leading a: same-letter collapse -> mira + ra
            Assert.Equal("Mirara", Joiner.Assemble(parts, Rules));
            var parts2 = new List<Joiner.Part> { new("mira", "fuse"), new("eth", "fuse") };
            // a + e are different vowels: tail's leading vowel dropped -> mira + th
            Assert.Equal("Mirath", Joiner.Assemble(parts2, Rules));
        }

        [Test]
        public void SpaceJoinMakesWords()
        {
            var parts = new List<Joiner.Part> { new("gary", "fuse"), new("jenkins", "space") };
            Assert.Equal("Gary Jenkins", Joiner.Assemble(parts, Rules));
        }

        [Test]
        public void ApostropheGluesToPreviousWord()
        {
            var parts = new List<Joiner.Part> { new("karen", "fuse"), new("s", "apostrophe"), new("diner", "space") };
            Assert.Equal("Karen's Diner", Joiner.Assemble(parts, Rules));
        }

        [Test]
        public void BreaksLongConsonantRuns()
        {
            string cleaned = Joiner.Cleanup("krgstn", Rules);
            // No 4+ consonant run survives.
            int run = 0, maxRun = 0;
            foreach (char c in cleaned)
            {
                run = "aeiouy".IndexOf(c) >= 0 ? 0 : run + 1;
                maxRun = Math.Max(maxRun, run);
            }
            Assert.True(maxRun <= 3, $"'{cleaned}' still has a consonant run > 3");
        }

        [Test]
        public void NeverProducesTripleLetters()
        {
            string cleaned = Joiner.Cleanup("grosss", Rules);
            Assert.False(cleaned.Contains("sss"), $"'{cleaned}' has a triple letter");
        }

        [Test]
        public void BreaksBannedClusters()
        {
            string cleaned = Joiner.Cleanup("otskan", Rules);
            Assert.False(cleaned.ToLowerInvariant().Contains("tsk"), $"'{cleaned}' still contains banned cluster");
        }
    }

    public class BlocklistTests
    {
        private static readonly Blocklist List = new(new BlocklistDef
        {
            Substrings = new List<string> { "fuck", "nazi" },
        });

        [Test]
        public void CatchesPlainAndSpannedMatches()
        {
            Assert.True(List.IsBlocked("Fuckwit"));
            Assert.True(List.IsBlocked("Fu Ckham")); // spans a space after normalization
            Assert.True(List.IsBlocked("Nazgul Azir") == false, "near-miss should pass");
        }

        [Test]
        public void CatchesLeetSubstitutions()
        {
            Assert.True(List.IsBlocked("N4ZI-Bot"));
            Assert.True(List.IsBlocked("fUcK"));
        }

        [Test]
        public void PassesCleanNames()
        {
            Assert.False(List.IsBlocked("Gorthak Jenkins"));
            Assert.False(List.IsBlocked("The Rotting Food Court"));
        }
    }

    public class FeatureExtractorTests
    {
        private static readonly NameGenData Data = NameGenData.LoadEmbedded();
        private static readonly FeatureExtractor Extractor = new(Data.Ranges);

        private static CharacterGenome Character(Dictionary<string, float> p, params MoveGenome[] moves)
            => new(p, moves.ToList());

        [Test]
        public void NormalizesAgainstSchemaRanges()
        {
            var f = Extractor.ExtractCharacter(Character(new()
            {
                ["mass"] = 2.5f,          // top of range
                ["maxGroundSpeed"] = 2f,  // bottom of range
                ["maxAirSpeed"] = 2f,
            }));
            Assert.Equal(1.0, f.Get("mass"));
            Assert.Equal(0.0, f.Get("speed"));
        }

        [Test]
        public void ClampsOutOfRangeValues()
        {
            var f = Extractor.ExtractCharacter(Character(new() { ["mass"] = 99f }));
            Assert.Equal(1.0, f.Get("mass"));
        }

        [Test]
        public void MissingParamsReadNeutral()
        {
            var f = Extractor.ExtractCharacter(Character(new()));
            Assert.Equal(0.5, f.Get("mass"));
            Assert.Equal(0.5, f.Get("speed"));
        }

        [Test]
        public void OffAtZeroParamsReadAbsent()
        {
            var withDI = Extractor.ExtractCharacter(Character(new() { ["directionalInfluence"] = 0.05f }));
            var withoutDI = Extractor.ExtractCharacter(Character(new() { ["directionalInfluence"] = 0f }));
            Assert.Equal(1.0, withDI.Get("hasDI"));
            Assert.Equal(0.0, withoutDI.Get("hasDI"));
        }

        [Test]
        public void BoolAsFloatThresholdsAtHalf()
        {
            var reflectOn = Character(new(), new MoveGenome(MoveKind.Shield, new Dictionary<string, float> { ["reflect"] = 0.7f }));
            var reflectOff = Character(new(), new MoveGenome(MoveKind.Shield, new Dictionary<string, float> { ["reflect"] = 0.3f }));
            Assert.Equal(1.0, Extractor.ExtractCharacter(reflectOn).Get("hasShieldReflect"));
            Assert.Equal(0.0, Extractor.ExtractCharacter(reflectOff).Get("hasShieldReflect"));
        }

        [Test]
        public void IntAsFloatFloorsPathShape()
        {
            var sine = Character(new(), new MoveGenome(MoveKind.Projectile, new Dictionary<string, float> { ["pathShape"] = 1.9f }));
            var f = Extractor.ExtractCharacter(sine);
            Assert.Equal(1.0, f.Get("projSine"));
            Assert.Equal(0.0, f.Get("projLinear"));
            Assert.Equal(0.0, f.Get("projQuad"));
        }

        [Test]
        public void MovesetAggregatesPeaks()
        {
            var g = Character(new(),
                new MoveGenome(MoveKind.Melee, new Dictionary<string, float> { ["damageFactor"] = 2f }),
                new MoveGenome(MoveKind.Melee, new Dictionary<string, float> { ["damageFactor"] = 10f }));
            Assert.Equal(1.0, Extractor.ExtractCharacter(g).Get("peakDamage"));
        }

        [Test]
        public void StageFeaturesRespondToParams()
        {
            // Range minimums per BrawlerSim DefaultSchemas: visibleHalfHeight's legacy
            // reference is 5 (0.5x = 2.5), not the 5.5 blast half height — corrected
            // 2026-08-14 by the game-side schema audit (NameGenIntegrationTests).
            var tiny = Extractor.ExtractStage(new StageGenome(new Dictionary<string, float>
            {
                ["visibleHalfWidth"] = 4.888889f,
                ["visibleHalfHeight"] = 2.5f,
                ["koMarginFraction"] = 0.05f,
            }));
            Assert.InRange(tiny.Get("vastness"), 0.0, 0.001);
            Assert.InRange(tiny.Get("lethality"), 0.999, 1.0);
        }
    }

    public class TraitScorerTests
    {
        private static readonly NameGenData Data = NameGenData.LoadEmbedded();

        [Test]
        public void NeutralFeaturesFireNoTraits()
        {
            var f = new FeatureVector(); // everything reads 0.5
            var salient = TraitScorer.SelectSalient(f, Data.Traits.Character, 3, 0.15);
            Assert.Equal(0, salient.Count);
        }

        [Test]
        public void ExtremeMassFiresHeavy()
        {
            var f = new FeatureVector();
            f.Set("mass", 1.0);
            f.Set("bulk", 0.9);
            f.Set("speed", 0.2);
            var salient = TraitScorer.SelectSalient(f, Data.Traits.Character, 3, 0.15);
            Assert.True(salient.Any(t => t.Name == "heavy"), $"got: {string.Join(", ", salient.Select(t => t.Name))}");
        }

        [Test]
        public void SalienceIsOrderedAndCapped()
        {
            var f = new FeatureVector();
            f.Set("mass", 1.0);
            f.Set("speed", 1.0);
            f.Set("stature", 1.0);
            f.Set("girth", 1.0);
            f.Set("peakDamage", 1.0);
            var salient = TraitScorer.SelectSalient(f, Data.Traits.Character, 3, 0.15);
            Assert.True(salient.Count <= 3);
            for (int i = 1; i < salient.Count; i++)
                Assert.True(salient[i - 1].Score >= salient[i].Score, "not sorted by score");
        }
    }

    public class DataValidationTests
    {
        [Test]
        public void EmbeddedDataLoadsAndValidates()
        {
            var data = NameGenData.LoadEmbedded();
            Assert.Equal(4, data.Registers.Count);
            Assert.True(data.Traits.Character.Count >= 20);
            Assert.True(data.Traits.Stage.Count >= 8);
            Assert.True(data.Blocklist.Substrings.Count > 0);
            Assert.True(data.Mundane.Morphemes.Count > 0);
        }

        [Test]
        public void EveryRegisterHasBothTemplateKindsAndShapes()
        {
            var data = NameGenData.LoadEmbedded();
            foreach (var reg in data.Registers)
            {
                Assert.True(reg.Templates.Any(t => t.Kind == "character"), $"{reg.Name}: no character templates");
                Assert.True(reg.Templates.Any(t => t.Kind == "stage"), $"{reg.Name}: no stage templates");
            }
        }

        [Test]
        public void BadDataThrowsAtLoad()
        {
            var bad = new RegisterDef
            {
                Name = "bad",
                Templates = new List<TemplateDef> { new() { Kind = "character", Shape = "single",
                    Slots = new List<SlotDef> { new() { Position = "nonsense" } } } },
                Morphemes = new List<MorphemeDef> { new() { Form = "x", Positions = new List<string> { "prefix" },
                    Tags = new List<string> { "notATrait" } } },
            };
            var good = NameGenData.LoadEmbedded();
            Assert.Throws<System.IO.InvalidDataException>(() => new NameGenData(
                new List<RegisterDef> { bad }, good.Traits, good.Ranges, good.Mundane, good.Blocklist));
        }
    }
}
