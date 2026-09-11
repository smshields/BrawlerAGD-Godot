using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Evolution;

/// <summary>
/// Hand-computed expectations for the four MAP-Elites descriptors (2026-09-10,
/// docs/features/map-elites.md). Ranges referenced below are DefaultSchemas as of
/// this date: move warm-up 0.1–0.6 / execution 0.1–0.4 / cool-down 0.1–0.6 (attack
/// commitment total 0.3–1.6), shield wind-up+cool-down 0.10–0.6, dash
/// wind-up+duration 0.15–0.8, character speeds 2–10 / mass 0.5–2.5.
/// </summary>
public class DescriptorsTests
{
    private static ParamSet Shield(params (string Key, float Value)[] overrides)
    {
        var values = new Dictionary<string, float>
        {
            [ShieldParams.WindUpDuration] = 0.1f,
            [ShieldParams.CoolDownDuration] = 0.1f,
            [ShieldParams.InitialSize] = 1f,
            [ShieldParams.HoldDegradationRate] = 0.1f,
            [ShieldParams.HitDegradationScalar] = 0.5f,
            [ShieldParams.KnockbackReduction] = 0.5f,
            [ShieldParams.SpacingPush] = 1f,
            [ShieldParams.RegenRate] = 0.2f,
            [ShieldParams.BreakStunDuration] = 1f,
            [ShieldParams.Reflect] = 0f,
        };
        foreach ((string key, float value) in overrides)
        {
            values[key] = value;
        }
        return ParamSet.FromDictionary(DefaultSchemas.Shield, values);
    }

    private static CharacterGenome Char(ParamSet @params, params MoveGenome[] moves) =>
        new("c", 3, 0, @params, moves, buttonMoves: new int[BrawlerSim.Sim.InputFrame.ActionCount]);

    private static StageGenome Floor() => new(new[] { new PlatformGene(-8, -3, 16, 1) });

    // ── Axis 1: move variety ────────────────────────────────────────────────────────

    [Fact]
    public void MoveVarietyIsZeroForIdenticalKits()
    {
        Assert.Equal(0f, Descriptors.MoveVariety(TestGames.FlatArena()));
    }

    [Fact]
    public void MoveVarietyIsOneWhenEveryButtonMismatchesType()
    {
        // Every button of A holds an attack, every button of B a shield → all five
        // buttons are type mismatches → axis 1 = 1.
        var a = Char(TestGames.Character(), new MoveGenome(TestGames.Move(), 0));
        var b = Char(TestGames.Character(), new MoveGenome(Shield(), 0, MoveType.Shield));
        Assert.Equal(1f, Descriptors.MoveVariety(new GameGenome(new[] { a, b }, Floor())));
    }

    [Fact]
    public void MoveVarietyNormalizesParameterDistanceWithinSharedType()
    {
        // Same type on every button; B's warm-up differs by 0.4 over a 0.5-wide range
        // (0.1–0.6). The move schema has 12 specs, all positive width, so each
        // button's distance is (0.4/0.5)/12 and the mean over buttons is the same.
        var a = Char(TestGames.Character(), new MoveGenome(TestGames.Move((MoveParams.WarmUpDuration, 0.2f)), 0));
        var b = Char(TestGames.Character(), new MoveGenome(TestGames.Move((MoveParams.WarmUpDuration, 0.6f)), 0));
        float expected = 0.4f / 0.5f / 12f;
        Assert.Equal((double)expected, Descriptors.MoveVariety(new GameGenome(new[] { a, b }, Floor())), 5);
    }

    [Fact]
    public void MoveVarietyUsesButtonPairingNotSlotPairing()
    {
        // Both characters carry the SAME two moves [attack, shield] (slot pairing
        // would read 0), but A maps every button to the attack while B maps every
        // button to the shield → button pairing reads all-mismatch = 1.
        var attack = new MoveGenome(TestGames.Move(), 0);
        var shield = new MoveGenome(Shield(), 0, MoveType.Shield);
        var a = new CharacterGenome("a", 3, 0, TestGames.Character(), new[] { attack, shield },
            buttonMoves: new[] { 0, 0, 0, 0, 0 });
        var b = new CharacterGenome("b", 3, 0, TestGames.Character(), new[] { attack, shield },
            buttonMoves: new[] { 1, 1, 1, 1, 1 });
        Assert.Equal(1f, Descriptors.MoveVariety(new GameGenome(new[] { a, b }, Floor())));
    }

    // ── Axis 2: character asymmetry ─────────────────────────────────────────────────

    [Fact]
    public void CharacterAsymmetryIsZeroForIdenticalCharacters()
    {
        Assert.Equal(0f, Descriptors.CharacterAsymmetry(TestGames.FlatArena()));
    }

    [Fact]
    public void CharacterAsymmetryAveragesTheFiveNormalizedPhysicalParams()
    {
        // Ground speed differs 4 vs 8 over range 2–10 → 0.5; mass 1 vs 2 over
        // 0.5–2.5 → 0.5; the other three params are equal → (0.5+0+0.5+0+0)/5 = 0.2.
        var a = Char(TestGames.Character(), new MoveGenome(TestGames.Move(), 0));
        var b = Char(TestGames.Character(
                (CharacterParams.MaxGroundSpeed, 8f), (CharacterParams.Mass, 2f)),
            new MoveGenome(TestGames.Move(), 0));
        Assert.Equal(0.2, Descriptors.CharacterAsymmetry(new GameGenome(new[] { a, b }, Floor())), 5);
    }

    [Fact]
    public void CharacterAsymmetryAveragesOverAllPairsAtThreePlayers()
    {
        // Masses 1 / 2 / 1.5, everything else equal. Pair means over the five params:
        // AB (1/2)/5 = 0.1, AC (0.25/2)/5 = 0.05, BC 0.05 → mean over 3 pairs = 0.2/3.
        var move = new MoveGenome(TestGames.Move(), 0);
        var a = Char(TestGames.Character((CharacterParams.Mass, 1f)), move);
        var b = Char(TestGames.Character((CharacterParams.Mass, 2f)), move);
        var c = Char(TestGames.Character((CharacterParams.Mass, 1.5f)), move);
        Assert.Equal((double)(0.2f / 3f), Descriptors.CharacterAsymmetry(
            new GameGenome(new[] { a, b, c }, Floor())), 5);
    }

    // ── Axis 3: stage platform ratio ────────────────────────────────────────────────

    [Fact]
    public void StagePlatformRatioScalesLinearlyWithCoveredCells()
    {
        // Same (legacy-derived) box: doubling the platform cells doubles the ratio.
        var eight = TestGames.FlatArena();
        var chars = eight.Characters;
        var sixteen = new GameGenome(chars, new StageGenome(
            new[] { new PlatformGene(-8, -3, 16, 1), new PlatformGene(-8, 0, 16, 1) },
            eight.Stage.Params));
        float one = Descriptors.StagePlatformRatio(eight);
        Assert.True(one > 0f);
        Assert.Equal((double)(2f * one), Descriptors.StagePlatformRatio(sixteen), 5);
    }

    [Fact]
    public void StagePlatformRatioNeverDoubleCountsOverlappingCells()
    {
        // Two 4-wide platforms overlapping by 2 cells union to 6 cells — exactly the
        // ratio of a single 6-wide platform in the same box.
        var reference = TestGames.FlatArena();
        var overlapping = new GameGenome(reference.Characters, new StageGenome(
            new[] { new PlatformGene(0, -3, 4, 1), new PlatformGene(2, -3, 4, 1) },
            reference.Stage.Params));
        var single = new GameGenome(reference.Characters, new StageGenome(
            new[] { new PlatformGene(0, -3, 6, 1) },
            reference.Stage.Params));
        Assert.Equal(
            (double)Descriptors.StagePlatformRatio(single),
            Descriptors.StagePlatformRatio(overlapping), 6);
    }

    [Fact]
    public void ThinPlatformsCountAtFullCellArea()
    {
        // Designer decision 2026-09-10: thinness does not discount coverage.
        var reference = TestGames.FlatArena();
        var thin = new GameGenome(reference.Characters, new StageGenome(
            new[] { new PlatformGene(-8, -3, 16, 1, Thin: true) },
            reference.Stage.Params));
        Assert.Equal(
            (double)Descriptors.StagePlatformRatio(reference),
            Descriptors.StagePlatformRatio(thin), 6);
    }

    // ── Axis 4: timing commitment ───────────────────────────────────────────────────

    [Fact]
    public void TimingCommitmentNormalizesAttacksWithinTheirTypeRange()
    {
        // Default test move: 0.2 + 0.1 + 0.2 = 0.5 total against attack bounds
        // 0.3–1.6 → (0.5 − 0.3) / 1.3, identical for both characters.
        Assert.Equal((double)(0.2f / 1.3f), Descriptors.TimingCommitment(TestGames.FlatArena()), 5);
    }

    [Fact]
    public void TimingCommitmentNormalizesEachMoveTypeAgainstItsOwnRange()
    {
        // Kit = [attack 0.5 total → 0.2/1.3, shield 0.1+0.3 = 0.4 total against
        // 0.10–0.6 → 0.3/0.5 = 0.6]; kit mean = (0.2/1.3 + 0.6)/2 for both chars.
        var attack = new MoveGenome(TestGames.Move(), 0);
        var shield = new MoveGenome(Shield(
            (ShieldParams.WindUpDuration, 0.1f), (ShieldParams.CoolDownDuration, 0.3f)),
            0, MoveType.Shield);
        CharacterGenome Make() => new("c", 3, 0, TestGames.Character(),
            new[] { attack, shield }, buttonMoves: new[] { 0, 0, 0, 0, 1 });
        var genome = new GameGenome(new[] { Make(), Make() }, Floor());
        float expected = (0.2f / 1.3f + 0.6f) / 2f;
        Assert.Equal((double)(expected), Descriptors.TimingCommitment(genome), 5);
    }

    [Fact]
    public void TimingCommitmentIsZeroAtSnappiestAndOneAtSlowest()
    {
        var snappy = new MoveGenome(TestGames.Move(
            (MoveParams.WarmUpDuration, 0.1f), (MoveParams.ExecutionDuration, 0.1f),
            (MoveParams.CoolDownDuration, 0.1f)), 0);
        var committal = new MoveGenome(TestGames.Move(
            (MoveParams.WarmUpDuration, 0.6f), (MoveParams.ExecutionDuration, 0.4f),
            (MoveParams.CoolDownDuration, 0.6f)), 0);
        var fast = new GameGenome(new[]
        {
            Char(TestGames.Character(), snappy), Char(TestGames.Character(), snappy),
        }, Floor());
        var slow = new GameGenome(new[]
        {
            Char(TestGames.Character(), committal), Char(TestGames.Character(), committal),
        }, Floor());
        Assert.Equal((double)(0f), Descriptors.TimingCommitment(fast), 5);
        Assert.Equal((double)(1f), Descriptors.TimingCommitment(slow), 5);
    }

    // ── General contracts ───────────────────────────────────────────────────────────

    [Fact]
    public void AllDescriptorsStayInUnitRangeOverRandomGenomes()
    {
        var rng = new BrawlerSim.Determinism.Pcg32(4242);
        for (int i = 0; i < 200; i++)
        {
            float[] descriptor = Descriptors.Compute(GameGenome.Generate(GenerationConfig.Default, rng));
            for (int axis = 0; axis < Descriptors.Count; axis++)
            {
                Assert.InRange(descriptor[axis], 0f, 1f);
            }
        }
    }

    [Fact]
    public void DescriptorsArePureFunctionsOfTheGenome()
    {
        var rng = new BrawlerSim.Determinism.Pcg32(77);
        GameGenome genome = GameGenome.Generate(GenerationConfig.Default, rng);
        Assert.Equal(Descriptors.Compute(genome), Descriptors.Compute(genome));
    }

    [Fact]
    public void RandomCompositionKeepsButtonPairingNonDegenerate()
    {
        // In composed/random mode ButtonMoves is the identity, so the axis degrades
        // to slot pairing — it must still produce nonzero variety across a sample.
        var config = GenerationConfig.Default with
        {
            ButtonComposition = GenerationConfig.RandomComposition,
        };
        var rng = new BrawlerSim.Determinism.Pcg32(11);
        bool anyPositive = false;
        for (int i = 0; i < 20 && !anyPositive; i++)
        {
            anyPositive = Descriptors.MoveVariety(GameGenome.Generate(config, rng)) > 0f;
        }
        Assert.True(anyPositive, "random composition produced zero move variety over 20 genomes");
    }
}
