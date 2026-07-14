using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using Xunit;

namespace BrawlerSim.Tests.Evolution;

/// <summary>
/// Button-composition control + advanced generation ranges (2026-07-14,
/// docs/features/evolve-composition-and-ranges.md). The pinned default path is
/// guaranteed byte-identical by the population fingerprint golden
/// (Phase1PipelineTests) — these tests cover the composed and overridden paths.
/// </summary>
public class CompositionAndRangesTests
{
    private static GenerationConfig Composed(params SlotSpec[] slots) =>
        GenerationConfig.Default with { ButtonComposition = slots };

    private static readonly SlotSpec[] AllRandom =
        { SlotSpec.Random, SlotSpec.Random, SlotSpec.Random, SlotSpec.Random };

    // ── Composed generation ────────────────────────────────────────────────────

    [Fact]
    public void ComposedGenerationHonorsFixedSpecsWithIdentityButtons()
    {
        var config = Composed(SlotSpec.Attack, SlotSpec.Shield, SlotSpec.Dash, SlotSpec.Attack);
        GameGenome genome = GameGenome.Generate(config, new Pcg32(11));
        foreach (CharacterGenome c in genome.Characters)
        {
            Assert.Equal(
                new[] { MoveType.Attack, MoveType.Shield, MoveType.Dash, MoveType.Attack },
                c.Moves.Select(m => m.Type).ToArray());
            Assert.Equal(new[] { 0, 1, 2, 3 }, c.ButtonMoves);
        }
        Assert.Empty(genome.Validate());
    }

    [Fact]
    public void RandomSlotsDrawAllThreeTypesAcrossSeeds()
    {
        var config = Composed(AllRandom);
        var seen = new HashSet<MoveType>();
        for (ulong seed = 0; seed < 20; seed++)
        {
            GameGenome genome = GameGenome.Generate(config, new Pcg32(seed));
            foreach (CharacterGenome c in genome.Characters)
            {
                foreach (MoveGenome m in c.Moves)
                {
                    seen.Add(m.Type);
                }
            }
        }
        Assert.Equal(3, seen.Count);
    }

    [Fact]
    public void ComposedGenerationIsDeterministic()
    {
        var config = Composed(AllRandom);
        GameGenome a = GameGenome.Generate(config, new Pcg32(77));
        GameGenome b = GameGenome.Generate(config, new Pcg32(77));
        Assert.Equal(0f, GenomeDistance.Normalized(a, b, config));
        for (int c = 0; c < a.Characters.Count; c++)
        {
            Assert.Equal(
                a.Characters[c].Moves.Select(m => m.Type),
                b.Characters[c].Moves.Select(m => m.Type));
        }
    }

    // ── Composed genetic ops ───────────────────────────────────────────────────

    [Fact]
    public void CrossoverKeepsIdentityButtonsAndInheritsMismatchedSlotsWholesale()
    {
        var config = Composed(AllRandom);
        var rng = new Pcg32(5);
        // Draw parent pairs until slot 0 differs in type, then cross and check the
        // child's slot 0 is EXACTLY one parent's move (type + params, no mixing).
        for (int attempt = 0; attempt < 50; attempt++)
        {
            GameGenome a = GameGenome.Generate(config, rng);
            GameGenome b = GameGenome.Generate(config, rng);
            if (a.Characters[0].Moves[0].Type == b.Characters[0].Moves[0].Type)
            {
                continue;
            }
            GameGenome child = GameGenomeOps.Crossover(a, b, rng, config);
            MoveGenome slot = child.Characters[0].Moves[0];
            MoveGenome source = slot.Type == a.Characters[0].Moves[0].Type
                ? a.Characters[0].Moves[0]
                : b.Characters[0].Moves[0];
            Assert.Equal(source.Type, slot.Type);
            Assert.Equal(source.Params.ToArray(), slot.Params.ToArray());
            foreach (CharacterGenome c in child.Characters)
            {
                Assert.Equal(new[] { 0, 1, 2, 3 }, c.ButtonMoves);
            }
            return;
        }
        Assert.Fail("no type-mismatched parent pair in 50 draws — random composition is broken");
    }

    [Fact]
    public void MutationRerollsOnlyRandomSlotsAndHonorsTheRate()
    {
        // Slot 0 fixed Attack, slots 1–3 Random, reroll rate 1: every mutation must
        // regenerate slots 1–3 (types drawn fresh) and never change slot 0's type.
        var always = Composed(SlotSpec.Attack, SlotSpec.Random, SlotSpec.Random, SlotSpec.Random)
            with { TypeRerollRate = 1f };
        GameGenome genome = GameGenome.Generate(always, new Pcg32(3));
        var rng = new Pcg32(4);
        var rerolledTypes = new HashSet<MoveType>();
        for (int i = 0; i < 30; i++)
        {
            genome = GameGenomeOps.Mutate(genome, rng, always);
            Assert.Empty(genome.Validate());
            foreach (CharacterGenome c in genome.Characters)
            {
                Assert.Equal(MoveType.Attack, c.Moves[0].Type);
                Assert.Equal(new[] { 0, 1, 2, 3 }, c.ButtonMoves);
                rerolledTypes.Add(c.Moves[1].Type);
            }
        }
        Assert.Equal(3, rerolledTypes.Count); // slot 1 visited every type over 30 rerolls

        // Rate 0: types are frozen even for Random specs.
        var never = always with { TypeRerollRate = 0f };
        GameGenome frozen = GameGenome.Generate(never, new Pcg32(9));
        MoveType[] before = frozen.Characters[0].Moves.Select(m => m.Type).ToArray();
        for (int i = 0; i < 10; i++)
        {
            frozen = GameGenomeOps.Mutate(frozen, rng, never);
        }
        Assert.Equal(before, frozen.Characters[0].Moves.Select(m => m.Type).ToArray());
    }

    // ── Degenerate compositions must still play ────────────────────────────────

    [Theory]
    [InlineData(SlotSpec.Shield)] // zero attacks: P ≈ 0.2 per character in RANDOM mode
    [InlineData(SlotSpec.Dash)]
    public void SingleTypeCompositionsRunMatchesDeterministically(SlotSpec spec)
    {
        var config = Composed(spec, spec, spec, spec);
        GameGenome genome = GameGenome.Generate(config, new Pcg32(21));
        MatchResult a = Run(genome, 55);
        MatchResult b = Run(genome, 55);
        Assert.Equal(a.FinalHash, b.FinalHash);
        Assert.True(a.Ticks > 0);
        Assert.False(float.IsNaN(new BrawlerSim.Fitness.StandardFitnessV3().Evaluate(a)));

        static MatchResult Run(GameGenome genome, ulong seed) =>
            MatchRunner.Run(genome, new IInputSource[]
            {
                AgentConfig.Default.CreateSource(new Pcg32(seed, 0)),
                AgentConfig.Default.CreateSource(new Pcg32(seed, 1)),
            });
    }

    // ── Advanced ranges ────────────────────────────────────────────────────────

    [Fact]
    public void RangeOverridesShapeGenerationAndMutationAndWidenTheValidDomain()
    {
        // maxGroundSpeed stock range is [2,10]; force [12,14] — outside the tested
        // domain, so the override must widen ValidMax to keep Validate() coherent.
        var config = GenerationConfig.Default.WithRangeOverrides(new[]
        {
            new RangeOverride("character", CharacterParams.MaxGroundSpeed, 12f, 14f),
        });
        var rng = new Pcg32(31);
        GameGenome genome = GameGenome.Generate(config, rng);
        Assert.Empty(genome.Validate());
        foreach (CharacterGenome c in genome.Characters)
        {
            Assert.InRange(c.Params.Get(CharacterParams.MaxGroundSpeed), 12f, 14f);
        }
        for (int i = 0; i < 20; i++)
        {
            genome = GameGenomeOps.Mutate(genome, rng, config);
            foreach (CharacterGenome c in genome.Characters)
            {
                Assert.InRange(c.Params.Get(CharacterParams.MaxGroundSpeed), 12f, 14f);
            }
        }
    }

    [Fact]
    public void ClampedParamGeneratesExactlyAndDistanceSkipsIt()
    {
        var config = GenerationConfig.Default.WithRangeOverrides(new[]
        {
            new RangeOverride("character", CharacterParams.Mass, 1.5f, 1.5f),
        });
        GameGenome a = GameGenome.Generate(config, new Pcg32(41));
        GameGenome b = GameGenome.Generate(config, new Pcg32(42));
        Assert.Equal(1.5f, a.Characters[0].Params.Get(CharacterParams.Mass));
        Assert.Equal(1.5f, b.Characters[1].Params.Get(CharacterParams.Mass));
        float distance = GenomeDistance.Normalized(a, b, config); // zero-width dim skipped
        Assert.False(float.IsNaN(distance));
    }

    [Fact]
    public void RangeOverrideRejectsMinAboveMax()
    {
        Assert.Throws<ArgumentException>(() => GenerationConfig.Default.WithRangeOverrides(new[]
        {
            new RangeOverride("move", MoveParams.MoveDist, 5f, 1f),
        }));
    }

    // ── run.json round trip ────────────────────────────────────────────────────

    [Fact]
    public void RunJsonRoundTripsCompositionAndRangesAndResumes()
    {
        string dir = Path.Combine(Path.GetTempPath(), $"brawler-comp-{Guid.NewGuid():N}");
        try
        {
            var config = new EvolutionConfig
            {
                Seed = 61,
                PopulationSize = 8,
                RoundsPerIndividual = 1,
                Generation = (GenerationConfig.Default with
                {
                    ButtonComposition = new[]
                        { SlotSpec.Attack, SlotSpec.Random, SlotSpec.Shield, SlotSpec.Random },
                    TypeRerollRate = 0.35f,
                }).WithRangeOverrides(new[]
                {
                    new RangeOverride("move", MoveParams.KnockbackScalar, 0f, 40f),
                }),
            };
            var engine = new EvolutionEngine(config);
            var history = new List<GenerationStats> { engine.Step() };
            RunStore.SaveCheckpoint(dir, engine, config, history);

            (EvolutionEngine resumed, EvolutionConfig loaded, _) = RunStore.Load(dir);
            Assert.Equal(config.Generation.ButtonComposition, loaded.Generation.ButtonComposition);
            Assert.Equal(0.35f, loaded.Generation.TypeRerollRate);
            RangeOverride o = Assert.Single(loaded.Generation.RangeOverrides);
            Assert.Equal(("move", MoveParams.KnockbackScalar, 0f, 40f), (o.Schema, o.Key, o.Min, o.Max));
            // The rebuilt schema carries the override — resumed breeding honors it.
            ParamSpec spec = loaded.Generation.MoveSchema[
                loaded.Generation.MoveSchema.IndexOf(MoveParams.KnockbackScalar)];
            Assert.Equal((0f, 40f), (spec.Min, spec.Max));
            GenerationStats next = resumed.Step(); // resume actually runs
            Assert.Equal(2, resumed.GenerationsCompleted);
            Assert.False(float.IsNaN(next.TopFitness));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
