using System;
using System.Collections.Generic;
using System.Linq;
using NameGen;
using NameGen.Core;

namespace NameGen.Tests
{
    public class GenerationIntegrationTests
    {
        private static readonly NameGenerator Gen = NameGenerator.CreateDefault();

        private static CharacterGenome RandomCharacter(Pcg32 rng)
        {
            // Rough stand-in for the game's generator: uniform in schema ranges.
            float U(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            var p = new Dictionary<string, float>
            {
                ["maxGroundSpeed"] = U(2, 10), ["maxAirSpeed"] = U(2, 10),
                ["groundAccelerationFactor"] = U(0, 1), ["airAccelerationFactor"] = U(0, 1),
                ["groundJumpForce"] = U(1, 15), ["airJumpForce"] = U(1, 15),
                ["mass"] = U(0.5f, 2.5f), ["drag"] = U(1, 6),
                ["widthScalar"] = U(0.7f, 1.5f), ["heightScalar"] = U(0.5f, 1.5f),
                ["gravityScalar"] = U(0.3f, 1.3f), ["hitstunDamageScalar"] = U(0.1f, 0.3f),
                ["fastFallAcceleration"] = U(0, 15),
                ["crouchHeightRatio"] = U(0.4f, 0.9f), ["crouchMoveSpeed"] = U(0.3f, 1.5f),
                ["directionalInfluence"] = rng.NextBool(0.5) ? U(0.02f, 0.10f) : 0f,
            };
            var moves = new List<MoveGenome>();
            for (int i = 0; i < 3; i++)
                moves.Add(new MoveGenome(MoveKind.Melee, new Dictionary<string, float>
                {
                    ["moveDist"] = U(0.8f, 1.5f), ["damageFactor"] = U(0, 10),
                    ["knockbackScalar"] = U(1, 16), ["knockbackModY"] = U(-1, 1),
                    ["warmUpDuration"] = U(0.1f, 0.6f), ["coolDownDuration"] = U(0.1f, 0.6f),
                    ["hitstunDuration"] = U(0, 1),
                }));
            if (rng.NextBool(0.5))
                moves.Add(new MoveGenome(MoveKind.Projectile, new Dictionary<string, float>
                {
                    ["pathShape"] = (float)(rng.NextDouble() * 3), ["velocity"] = U(3, 15),
                    ["damageFactor"] = U(0, 10), ["doesRotate"] = (float)rng.NextDouble(),
                }));
            if (rng.NextBool(0.7))
                moves.Add(new MoveGenome(MoveKind.Shield, new Dictionary<string, float>
                {
                    ["initialSize"] = U(0.5f, 2f), ["regenRate"] = U(0.05f, 0.5f),
                    ["holdDegradationRate"] = U(0.05f, 0.4f), ["reflect"] = (float)rng.NextDouble(),
                }));
            if (rng.NextBool(0.7))
                moves.Add(new MoveGenome(MoveKind.Dash, new Dictionary<string, float>
                {
                    ["acceleration"] = U(6, 18),
                    ["warmUpInvulnerable"] = (float)rng.NextDouble(),
                    ["durationInvulnerable"] = (float)rng.NextDouble(),
                }));
            return new CharacterGenome(p, moves);
        }

        private static StageGenome RandomStage(Pcg32 rng)
        {
            float U(float min, float max) => min + (float)rng.NextDouble() * (max - min);
            return new StageGenome(new Dictionary<string, float>
            {
                ["visibleHalfWidth"] = U(4.9f, 48.9f), ["visibleHalfHeight"] = U(2.75f, 27.5f),
                ["koMarginFraction"] = U(0.05f, 0.25f), ["platformCount"] = U(2, 16),
                ["maxPlatformSize"] = U(3, 14), ["mirrored"] = (float)rng.NextDouble(),
                ["platformSpawnDuration"] = rng.NextBool(0.5) ? U(1, 5) : 0f,
                ["spawnInvulnDuration"] = rng.NextBool(0.5) ? U(1, 3) : 0f,
            });
        }

        [Test]
        public void SameSeedSameName()
        {
            var rng = new Pcg32(42);
            var genome = RandomCharacter(rng);
            var a = Gen.GenerateCharacterName(genome, new NameOptions { Seed = 99 });
            var b = Gen.GenerateCharacterName(genome, new NameOptions { Seed = 99 });
            Assert.Equal(a.Display, b.Display);
            Assert.Equal(a.Register, b.Register);
        }

        [Test]
        public void BatchOfCharacterNamesIsWellFormed()
        {
            var rng = new Pcg32(1);
            for (ulong seed = 0; seed < 500; seed++)
            {
                var result = Gen.GenerateCharacterName(RandomCharacter(rng), new NameOptions { Seed = seed });
                Assert.True(result.Display.Length >= 2, $"too short: '{result.Display}'");
                Assert.True(result.Display.Length <= 40, $"too long: '{result.Display}'");
                Assert.False(result.Display.Contains("  "), $"double space: '{result.Display}'");
                Assert.True(char.IsLetterOrDigit(result.Display[0]) || result.Display[0] == '\'',
                    $"odd leading char: '{result.Display}'");
                Assert.True(result.Parts.Count > 0, "no provenance parts");
            }
        }

        [Test]
        public void BatchOfStageNamesIsWellFormed()
        {
            var rng = new Pcg32(2);
            for (ulong seed = 1000; seed < 1300; seed++)
            {
                var result = Gen.GenerateStageName(RandomStage(rng), new NameOptions { Seed = seed });
                Assert.True(result.Display.Length >= 3, $"too short: '{result.Display}'");
                Assert.True(result.Display.Length <= 45, $"too long: '{result.Display}'");
                Assert.True(result.Parts.Count > 0, "no provenance parts");
            }
        }

        [Test]
        public void EveryRegisterIsReachableAndForceable()
        {
            var rng = new Pcg32(3);
            var genome = RandomCharacter(rng);
            foreach (var reg in new[] { "fantasy", "scifi", "horror", "normal" })
            {
                var result = Gen.GenerateCharacterName(genome, new NameOptions { Seed = 5, Register = reg });
                Assert.Equal(reg, result.Register);
            }
        }

        [Test]
        public void ForcedShapeIsHonored()
        {
            var rng = new Pcg32(4);
            var genome = RandomCharacter(rng);
            for (ulong seed = 0; seed < 50; seed++)
            {
                var single = Gen.GenerateCharacterName(genome, new NameOptions { Seed = seed, Shape = NameShape.Single });
                Assert.Equal(NameShape.Single, single.Shape);
            }
        }

        [Test]
        public void UnknownRegisterThrows()
        {
            var rng = new Pcg32(5);
            var genome = RandomCharacter(rng);
            Assert.Throws<ArgumentException>(() =>
                Gen.GenerateCharacterName(genome, new NameOptions { Seed = 1, Register = "cyberpunk" }));
        }

        [Test]
        public void TraitBiasIsMeasurable()
        {
            // A maximally heavy, slow character should draw heavy/brutal-tagged morphemes
            // far more often than a maximally light, fast one. This is the core promise
            // of the library: the name points at the genome.
            var heavy = new CharacterGenome(new Dictionary<string, float>
            {
                ["mass"] = 2.5f, ["widthScalar"] = 1.5f, ["heightScalar"] = 1.5f,
                ["maxGroundSpeed"] = 2f, ["maxAirSpeed"] = 2f,
            });
            var light = new CharacterGenome(new Dictionary<string, float>
            {
                ["mass"] = 0.5f, ["widthScalar"] = 0.7f, ["heightScalar"] = 0.5f,
                ["maxGroundSpeed"] = 10f, ["maxAirSpeed"] = 10f,
            });

            int heavyHits = 0, lightHits = 0;
            const int n = 400;
            for (ulong seed = 0; seed < n; seed++)
            {
                var opts = new NameOptions { Seed = seed, Register = "fantasy", MundaneProbability = 0, BleedProbability = 0 };
                if (HasTag(Gen.GenerateCharacterName(heavy, opts), "heavy", "giant", "brutal", "sluggish")) heavyHits++;
                if (HasTag(Gen.GenerateCharacterName(light, opts), "heavy", "giant", "brutal", "sluggish")) lightHits++;
            }

            Assert.True(heavyHits > lightHits * 2,
                $"heavy genome drew heavy-family tags {heavyHits}/{n}, light genome {lightHits}/{n}; bias too weak");
            Assert.True(heavyHits > n / 3, $"heavy genome only drew heavy-family tags {heavyHits}/{n}");
        }

        private static bool HasTag(NameResult result, params string[] tags)
            => result.Parts.Any(p => p.Tags.Any(t => tags.Contains(t)));

        [Test]
        public void MundaneAndBleedProbabilitiesWork()
        {
            var rng = new Pcg32(6);
            var genome = RandomCharacter(rng);

            int mundane = 0, bleed = 0;
            const int n = 300;
            for (ulong seed = 0; seed < n; seed++)
            {
                var forced = Gen.GenerateCharacterName(genome,
                    new NameOptions { Seed = seed, Register = "fantasy", MundaneProbability = 1.0 });
                if (forced.Parts.Any(p => p.IsMundane)) mundane++;

                var bled = Gen.GenerateCharacterName(genome,
                    new NameOptions { Seed = seed, Register = "fantasy", BleedProbability = 1.0, MundaneProbability = 0 });
                if (bled.Parts.Any(p => p.IsBleed)) bleed++;
            }
            // Probability 1.0 applies per eligible slot; nearly every template starts
            // with an eligible slot, so require a strong majority rather than 100%.
            Assert.True(mundane > n * 2 / 3, $"mundane hijack only appeared in {mundane}/{n} names at p=1.0");
            Assert.True(bleed > n * 3 / 4, $"bleed only appeared in {bleed}/{n} names at p=1.0");
        }

        [Test]
        public void ZeroProbabilitiesMeanPureRegisters()
        {
            var rng = new Pcg32(7);
            var genome = RandomCharacter(rng);
            for (ulong seed = 0; seed < 200; seed++)
            {
                var result = Gen.GenerateCharacterName(genome,
                    new NameOptions { Seed = seed, BleedProbability = 0, MundaneProbability = 0 });
                Assert.False(result.Parts.Any(p => p.IsBleed || p.IsMundane),
                    $"impurity at p=0: '{result.Display}'");
            }
        }

        [Test]
        public void NoBlocklistedNamesInLargeBatch()
        {
            var data = NameGen.Data.NameGenData.LoadEmbedded();
            var blocklist = new Blocklist(data.Blocklist);
            var rng = new Pcg32(8);
            for (ulong seed = 0; seed < 2000; seed++)
            {
                var result = Gen.GenerateCharacterName(RandomCharacter(rng), new NameOptions { Seed = seed });
                Assert.False(blocklist.IsBlocked(result.Display), $"blocklisted name escaped: '{result.Display}'");
            }
        }

        [Test]
        public void DiversityIsHigh()
        {
            var rng = new Pcg32(9);
            var seen = new HashSet<string>();
            const int n = 1000;
            for (ulong seed = 0; seed < n; seed++)
                seen.Add(Gen.GenerateCharacterName(RandomCharacter(rng), new NameOptions { Seed = seed }).Display);
            // Distinct genomes with distinct seeds should rarely collide.
            Assert.True(seen.Count > n * 8 / 10, $"only {seen.Count}/{n} unique names");
        }
    }
}
