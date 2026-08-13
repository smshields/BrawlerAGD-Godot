using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Sim;
using BrawlerSim.Tests.Support;
using Xunit;

namespace BrawlerSim.Tests.Genome;

/// <summary>
/// Hand-computed tests for the Map Size rules (2026-07-21,
/// docs/features/map-size.md): legacy reconstruction bit-exactness, spawn repair,
/// the mirror transform, connectivity, and the sense-box scaling.
/// </summary>
public class StageRulesTests
{
    private static readonly PlatformGene[] FlatPair =
    {
        new(-8, -3, 6, 1),  // top at -2, span [-8, -2]
        new(2, -3, 6, 1),   // top at -2, span [2, 8]
    };

    // ----- legacy reconstruction ---------------------------------------------------

    [Fact]
    public void LegacyBlastZoneReconstructsBitExactly()
    {
        ParamSet legacy = StageRules.LegacyParams(FlatPair);
        Vec2 blast = StageRules.BlastHalfExtents(legacy);
        // Raw-bit equality, not tolerance: this is the replay contract for pre-v7
        // artifacts whose deaths graze the boundary.
        Assert.Equal(
            BitConverter.SingleToInt32Bits(MatchConfig.Default.BlastZoneHalfWidth),
            BitConverter.SingleToInt32Bits(blast.X));
        Assert.Equal(
            BitConverter.SingleToInt32Bits(MatchConfig.Default.BlastZoneHalfHeight),
            BitConverter.SingleToInt32Bits(blast.Y));
    }

    [Fact]
    public void LegacyParamsDeriveTheOldSpawns()
    {
        // Old ComputeSpawn: x = X + (XSize+1)/2 = -8 + 3 = -5; y = top + 2 = 0.
        ParamSet legacy = StageRules.LegacyParams(FlatPair);
        Assert.Equal(-5f, legacy.Get(StageParams.Spawn1X));
        Assert.Equal(0f, legacy.Get(StageParams.Spawn1Y));
        Assert.Equal(5f, legacy.Get(StageParams.Spawn2X));
        Assert.Equal(0f, legacy.Get(StageParams.Spawn2Y));
        Assert.True(StageRules.IsMirrored(legacy));
        Assert.False(StageRules.MirrorSideRight(legacy));
    }

    [Fact]
    public void SimWorldSpawnsMatchThePreFeatureRule()
    {
        // The end-to-end guarantee behind bit-identical legacy replays: a genome built
        // through the params-less StageGenome path spawns players exactly where the
        // old ComputeSpawn put them.
        var world = new SimWorld(BrawlerSim.Tests.Support.TestGames.FlatArena());
        Assert.Equal(0f, world.Players[0].Position.X);  // -8 + (16+1)/2 = 0 on the 16-wide arena
        Assert.Equal(0f, world.Players[1].Position.X);
    }

    [Fact]
    public void SenseBoxIsExactlyLegacyOnLegacyMapsAndScalesUp()
    {
        var legacyWorld = new SimWorld(BrawlerSim.Tests.Support.TestGames.FlatArena());
        Assert.Equal(MatchConfig.Default.PlatformSenseHalfWidth, legacyWorld.PlatformSenseHalf.X);
        Assert.Equal(MatchConfig.Default.PlatformSenseHalfHeight, legacyWorld.PlatformSenseHalf.Y);

        GameGenome game = BrawlerSim.Tests.Support.TestGames.FlatArena();
        ParamSet doubled = game.Stage.Params.With(
            (StageParams.VisibleHalfWidth, StageRules.LegacyVisibleHalfWidth * 2f),
            (StageParams.VisibleHalfHeight, StageRules.LegacyVisibleHalfHeight * 2f));
        var bigWorld = new SimWorld(new GameGenome(
            game.Characters, new StageGenome(game.Stage.Platforms, doubled)));
        Assert.Equal(MatchConfig.Default.PlatformSenseHalfWidth * 2f, bigWorld.PlatformSenseHalf.X);
        Assert.Equal(MatchConfig.Default.PlatformSenseHalfHeight * 2f, bigWorld.PlatformSenseHalf.Y);

        // Small maps do NOT shrink the instrument.
        ParamSet halved = game.Stage.Params.With(
            (StageParams.VisibleHalfWidth, StageRules.LegacyVisibleHalfWidth * 0.5f));
        var smallWorld = new SimWorld(new GameGenome(
            game.Characters, new StageGenome(game.Stage.Platforms, halved)));
        Assert.Equal(MatchConfig.Default.PlatformSenseHalfWidth, smallWorld.PlatformSenseHalf.X);
    }

    // ----- spawn repair --------------------------------------------------------------

    [Fact]
    public void RepairSpawnIsIdentityForLegalSpawns()
    {
        ParamSet legacy = StageRules.LegacyParams(FlatPair);
        float visW = legacy.Get(StageParams.VisibleHalfWidth);
        float visH = legacy.Get(StageParams.VisibleHalfHeight);
        var legal = new Vec2(-5f, 0f); // over platform 0, inside the box
        Vec2 repaired = StageRules.RepairSpawn(legal, FlatPair, visW, visH);
        Assert.Equal(legal.X, repaired.X);
        Assert.Equal(legal.Y, repaired.Y);
    }

    [Fact]
    public void RepairSpawnSnapsAnOverNothingSpawnToAPlatformColumn()
    {
        ParamSet legacy = StageRules.LegacyParams(FlatPair);
        float visW = legacy.Get(StageParams.VisibleHalfWidth);
        float visH = legacy.Get(StageParams.VisibleHalfHeight);
        // x = 0 is the pit between the platforms; nearest span edge is -2 or 2.
        Vec2 repaired = StageRules.RepairSpawn(new Vec2(0f, 0f), FlatPair, visW, visH);
        Assert.True(MathF.Abs(repaired.X) == 2f, $"expected a span edge, got {repaired.X}");
        Assert.Equal(0f, repaired.Y); // hover: top(-2) + 2
    }

    [Fact]
    public void RepairSpawnsSeparatesAllFourStackedSpawns()
    {
        // Four Player Support (2026-08-12): all four spawn genes stacked on one point
        // must repair to pairwise NON-overlapping legal spawns (designer rule) on a
        // layout with room to separate.
        ParamSet stacked = StageRules.LegacyParams(FlatPair).With(
            (StageParams.Spawn1X, -5f), (StageParams.Spawn1Y, 0f),
            (StageParams.Spawn2X, -5f), (StageParams.Spawn2Y, 0f),
            (StageParams.Spawn3X, -5f), (StageParams.Spawn3Y, 0f),
            (StageParams.Spawn4X, -5f), (StageParams.Spawn4Y, 0f));
        ParamSet repaired = StageRules.RepairSpawns(FlatPair, stacked);
        for (int i = 0; i < 4; i++)
        {
            Vec2 s = StageRules.SpawnOf(repaired, i);
            Assert.True(FlatPair.Any(p => s.X >= p.X && s.X <= p.X + p.XSize && s.Y >= p.Y + p.YSize),
                $"spawn {i + 1} ({s.X}, {s.Y}) is not over any platform");
            for (int j = i + 1; j < 4; j++)
            {
                Assert.False(StageRules.SpawnsOverlap(s, StageRules.SpawnOf(repaired, j)),
                    $"spawns {i + 1} and {j + 1} still overlap after repair");
            }
        }
        // Spawn 1 repairs first with no occupied constraint — identity for a legal gene.
        Assert.Equal(-5f, repaired.Get(StageParams.Spawn1X));
        Assert.Equal(0f, repaired.Get(StageParams.Spawn1Y));
    }

    [Fact]
    public void RepairSpawnPrefersOverlapOverEmbeddingWhenColumnsRunOut()
    {
        // Best-effort separation (2026-08-12): a single narrow platform cannot seat
        // two separated spawns — the second must land on a legal column anyway
        // (stacked beats embedded/KO-zone).
        var lone = new[] { new PlatformGene(-1, -1, 2, 1) };
        Vec2 s1 = StageRules.RepairSpawn(new Vec2(0f, 0.5f), lone, 5f, 5f);
        Vec2 s2 = StageRules.RepairSpawn(new Vec2(0f, 0.5f), lone, 5f, 5f, new[] { s1 });
        Assert.True(StageRules.SpawnsOverlap(s1, s2)); // no separated column exists
        Assert.Equal(s1.Y, s2.Y);                      // same legal hover, not an embed
    }

    [Fact]
    public void RepairSpawnClampsKoZoneGenesIntoTheBox()
    {
        ParamSet legacy = StageRules.LegacyParams(FlatPair);
        float visW = legacy.Get(StageParams.VisibleHalfWidth);
        float visH = legacy.Get(StageParams.VisibleHalfHeight);
        Vec2 repaired = StageRules.RepairSpawn(new Vec2(49f, 26f), FlatPair, visW, visH);
        Assert.True(MathF.Abs(repaired.X) <= visW - StageRules.SpawnEdgeClearance);
        Assert.True(repaired.Y <= visH - StageRules.SpawnEdgeClearance);
        // And it still ends up over a platform.
        Assert.True(repaired.X >= 2f && repaired.X <= 8f, $"got {repaired.X}");
    }

    [Fact]
    public void RepairSpawnEscapesARoofedColumn()
    {
        // The left platform is double-roofed on a short (visH 2.5 → edge 2.0) box:
        // a gene inside the roof stack nudges upward past the box top, so the repair
        // must find a legal spot instead of emitting a KO-zone spawn (the seed-152
        // bug this test pins). With the hover-fallback logic the winning spot is the
        // low hover UNDER the roof, over the left platform (top −2) — body-clear of
        // both the base and the roof at y = −2 + SpawnBodyHalfHeight + 0.02.
        var walled = new[]
        {
            new PlatformGene(-8, -3, 6, 1),
            new PlatformGene(-8, 0, 6, 1),
            new PlatformGene(-8, 1, 6, 1),
            new PlatformGene(2, -3, 6, 1),
        };
        Vec2 repaired = StageRules.RepairSpawn(new Vec2(-5f, 0.5f), walled, 10f, 2.5f);
        Assert.Equal(-5f, repaired.X);
        Assert.Equal(-2f + StageRules.SpawnBodyHalfHeight + 0.02f, repaired.Y);
        // Verify the guarantee the coordinates imply: the body box clears every
        // platform and the spot sits inside the visible box.
        foreach (PlatformGene p in walled)
        {
            bool clips =
                repaired.X + StageRules.SpawnBodyHalfWidth > p.X
                && repaired.X - StageRules.SpawnBodyHalfWidth < p.X + p.XSize
                && repaired.Y + StageRules.SpawnBodyHalfHeight > p.Y
                && repaired.Y - StageRules.SpawnBodyHalfHeight < p.Y + p.YSize;
            Assert.False(clips, $"repair result body-embeds in {p}");
        }
        Assert.True(repaired.Y <= 2.5f - StageRules.SpawnEdgeClearance);
    }

    // ----- mirror transform ------------------------------------------------------------

    [Fact]
    public void MirrorTransformLeftSourceReflectsAndClampsAtTheAxis()
    {
        var asymmetric = new[]
        {
            new PlatformGene(-6, -3, 4, 1), // fully left
            new PlatformGene(-1, 0, 3, 1),  // crosses the axis: clamps to [-1, 0]
            new PlatformGene(3, 2, 2, 1),   // fully right: dropped
        };
        List<PlatformGene>? result = StageRules.MirrorTransform(asymmetric, rightSideIsSource: false);
        Assert.NotNull(result);
        Assert.True(StageRules.IsSymmetric(result!));
        Assert.Contains(new PlatformGene(-6, -3, 4, 1), result!);
        Assert.Contains(new PlatformGene(2, -3, 4, 1), result!);   // its mirror
        Assert.Contains(new PlatformGene(-1, 0, 1, 1), result!);   // clamped at x = 0
        Assert.Contains(new PlatformGene(0, 0, 1, 1), result!);    // clamped mirror
        Assert.DoesNotContain(new PlatformGene(3, 2, 2, 1), result!);
    }

    [Fact]
    public void MirrorTransformRightSourceKeepsTheRightHalf()
    {
        var asymmetric = new[]
        {
            new PlatformGene(-6, -3, 4, 1),
            new PlatformGene(3, 2, 2, 1),
        };
        List<PlatformGene>? result = StageRules.MirrorTransform(asymmetric, rightSideIsSource: true);
        Assert.NotNull(result);
        Assert.True(StageRules.IsSymmetric(result!));
        Assert.Contains(new PlatformGene(3, 2, 2, 1), result!);
        Assert.Contains(new PlatformGene(-5, 2, 2, 1), result!);
        Assert.DoesNotContain(new PlatformGene(-6, -3, 4, 1), result!);
    }

    [Fact]
    public void MirrorTransformWithNoMassOnTheSourceSideIsNull()
    {
        var allRight = new[] { new PlatformGene(3, -3, 4, 1) };
        Assert.Null(StageRules.MirrorTransform(allRight, rightSideIsSource: false));
    }

    // ----- misc helpers ------------------------------------------------------------------

    [Fact]
    public void FitToCharactersConnectsAGapOnlyTheStrongCharacterCouldCross()
    {
        // Two platforms with a horizontal gap a STRONG jumper clears but a WEAK one
        // cannot — the asymmetric-traversal bug (2026-07-22). The fit must move a
        // platform so BOTH can hop, without overlaps, and without re-rolling.
        // y = -2 keeps the fixture above the legacy playable floor (2026-08-13
        // containment rule: bottom ≥ -blastY + clearance ≈ -2.84 on the legacy box).
        var platforms = new[]
        {
            new PlatformGene(-8, -2, 4, 1),  // span [-8, -4], top -1
            new PlatformGene(2, -2, 4, 1),   // span [2, 6], top -1 — gap of 6
        };
        var strong = new[]
        {
            (CharacterParams.GroundJumpForce, 14f), (CharacterParams.AirJumpForce, 14f),
            (CharacterParams.MaxAirSpeed, 10f), (CharacterParams.GravityScalar, 0.4f),
        };
        var weak = new[]
        {
            (CharacterParams.GroundJumpForce, 2f), (CharacterParams.AirJumpForce, 2f),
            (CharacterParams.MaxAirSpeed, 2f), (CharacterParams.GravityScalar, 1.3f),
        };
        CharacterGenome Make(string n, (string, float)[] ov) =>
            new(n, 3, 0, TestGames.Character(ov), new[] { new MoveGenome(TestGames.Move(), 0) });
        var stage = new StageGenome(platforms, StageRules.LegacyParams(platforms));
        var a = Make("Strong", strong);
        var b = Make("Weak", weak);

        var hb = new StageRules.CharHop(b, 9.81f);
        Assert.False(hb.CanHop(platforms[0], platforms[1])); // weak can't cross the raw gap

        StageGenome fitted = StageRules.FitToCharacters(stage, a, b, 9.81f, 0.74289274f);
        var p = fitted.Platforms;
        Assert.NotEqual(platforms, p);                       // a platform moved
        Assert.True(hb.CanHop(p[0], p[1]) && hb.CanHop(p[1], p[0])); // now the weak one can
        for (int i = 0; i < p.Count; i++)                    // no overlaps introduced
        {
            for (int j = i + 1; j < p.Count; j++)
            {
                Assert.False(StageRules.Overlaps(p[i], p[j]));
            }
        }
    }

    [Fact]
    public void FitToCharactersLeavesAnAlreadyFairStageUntouched()
    {
        // Identical characters + a stage they both traverse → the fit is the identity
        // (no needless platform churn, keeping most genomes stable).
        var platforms = new[] { new PlatformGene(-8, -3, 16, 1) }; // single floor
        var stage = new StageGenome(platforms, StageRules.LegacyParams(platforms));
        var c = new CharacterGenome("P", 3, 0, TestGames.Character(), new[] { new MoveGenome(TestGames.Move(), 0) });
        StageGenome fitted = StageRules.FitToCharacters(stage, c, c, 9.81f, 0.74289274f);
        Assert.Same(stage, fitted);
    }

    /// <summary>Corridors (side-by-side, vertically overlapping) whose gap the smaller
    /// body passes but the larger cannot — the audit rule for the 2026-07-27 solver.</summary>
    private static int CountAsymmetricGaps(IReadOnlyList<PlatformGene> plats, float smallW, float largeW)
    {
        int count = 0;
        for (int i = 0; i < plats.Count; i++)
        {
            for (int j = 0; j < plats.Count; j++)
            {
                if (i == j || plats[i].Y >= plats[j].Y + plats[j].YSize
                    || plats[j].Y >= plats[i].Y + plats[i].YSize)
                {
                    continue;
                }
                float gap = plats[j].X - (plats[i].X + plats[i].XSize);
                if (gap > 0f && gap >= smallW + 0.1f && gap < largeW + 0.1f)
                {
                    count++;
                }
            }
        }
        return count;
    }

    [Fact]
    public void FitToCharactersResolvesAnAsymmetricCorridor()
    {
        // Designer bug (2026-07-27): a 1-unit corridor a 0.45-wide body slips through
        // but a 1.11-wide body cannot. After the fit no corridor may discriminate.
        var platforms = new[]
        {
            new PlatformGene(-5, -3, 4, 1), // span [-5, -1]
            new PlatformGene(0, -3, 4, 1),  // span [0, 4] — gap of exactly 1
        };
        CharacterGenome Make(string n, float widthScalar) => new(
            n, 3, 0, TestGames.Character((CharacterParams.WidthScalar, widthScalar)),
            new[] { new MoveGenome(TestGames.Move(), 0) });
        var stage = new StageGenome(platforms, StageRules.LegacyParams(platforms));
        var small = Make("Small", 0.6f);
        var large = Make("Large", 1.5f);
        const float baseW = 0.74289274f;
        float smallW = baseW * 0.6f;
        float largeW = baseW * 1.5f;
        Assert.Equal(1, CountAsymmetricGaps(platforms, smallW, largeW));

        StageGenome fitted = StageRules.FitToCharacters(stage, small, large, 9.81f, baseW);
        Assert.Equal(0, CountAsymmetricGaps(fitted.Platforms, smallW, largeW));
        for (int i = 0; i < fitted.Platforms.Count; i++)
        {
            for (int j = i + 1; j < fitted.Platforms.Count; j++)
            {
                Assert.False(StageRules.Overlaps(fitted.Platforms[i], fitted.Platforms[j]));
            }
        }
    }

    [Fact]
    public void FitToCharactersTerminatesAndResolvesAChainOfCorridors()
    {
        // A row of gap-1 corridors: fixing one by sliding a platform re-opens the
        // next — the shape that could loop forever. The solver's attempt counters and
        // dock fallback must terminate with every corridor resolved.
        var platforms = new[]
        {
            new PlatformGene(-10, -3, 4, 1), // [-10, -6]
            new PlatformGene(-5, -3, 4, 1),  // [-5, -1]   gap 1
            new PlatformGene(0, -3, 4, 1),   // [0, 4]     gap 1
            new PlatformGene(5, -3, 4, 1),   // [5, 9]     gap 1
        };
        CharacterGenome Make(string n, float widthScalar) => new(
            n, 3, 0, TestGames.Character((CharacterParams.WidthScalar, widthScalar)),
            new[] { new MoveGenome(TestGames.Move(), 0) });
        var stage = new StageGenome(platforms, StageRules.LegacyParams(platforms));
        const float baseW = 0.74289274f;
        StageGenome fitted = StageRules.FitToCharacters(
            stage, Make("Small", 0.6f), Make("Large", 1.5f), 9.81f, baseW);

        Assert.Equal(0, CountAsymmetricGaps(fitted.Platforms, baseW * 0.6f, baseW * 1.5f));
        for (int i = 0; i < fitted.Platforms.Count; i++)
        {
            for (int j = i + 1; j < fitted.Platforms.Count; j++)
            {
                Assert.False(StageRules.Overlaps(fitted.Platforms[i], fitted.Platforms[j]));
            }
        }
    }

    [Fact]
    public void GeneratedStagesNeverHaveAsymmetricBodyGaps()
    {
        // Property audit (200 seeds, mirrors the 800-seed offline audit): generation —
        // which runs the full fit — must never emit a corridor one body passes and the
        // other cannot, and never an overlap.
        const float baseW = 0.74289274f;
        for (ulong seed = 1; seed <= 200; seed++)
        {
            var rng = new Pcg32(seed);
            GameGenome g = GameGenome.Generate(GenerationConfig.Default, rng);
            float wa = baseW * g.Characters[0].Params.Get(CharacterParams.WidthScalar);
            float wb = baseW * g.Characters[1].Params.Get(CharacterParams.WidthScalar);
            Assert.Equal(0, CountAsymmetricGaps(
                g.Stage.Platforms, MathF.Min(wa, wb), MathF.Max(wa, wb)));
            for (int i = 0; i < g.Stage.Platforms.Count; i++)
            {
                for (int j = i + 1; j < g.Stage.Platforms.Count; j++)
                {
                    Assert.False(StageRules.Overlaps(g.Stage.Platforms[i], g.Stage.Platforms[j]));
                }
            }
        }
    }

    [Fact]
    public void FourCharacterGamesNeverHaveAsymmetricBodyGapsEither()
    {
        // Four Player Support (2026-08-12): the platform fit quantifies over ALL of a
        // game's characters — the smallest/largest bodies among four must never see an
        // asymmetric corridor, and layouts stay overlap-free. 100 seeds (the 2P audit
        // covers the pairwise path at 200).
        const float baseW = 0.74289274f;
        var config = GenerationConfig.Default with { CharacterCount = 4 };
        for (ulong seed = 1; seed <= 100; seed++)
        {
            GameGenome g = GameGenome.Generate(config, new Pcg32(seed));
            Assert.Equal(4, g.Characters.Count);
            float smallW = float.MaxValue;
            float largeW = float.MinValue;
            foreach (CharacterGenome c in g.Characters)
            {
                float w = baseW * c.Params.Get(CharacterParams.WidthScalar);
                smallW = MathF.Min(smallW, w);
                largeW = MathF.Max(largeW, w);
            }
            Assert.Equal(0, CountAsymmetricGaps(g.Stage.Platforms, smallW, largeW));
            for (int i = 0; i < g.Stage.Platforms.Count; i++)
            {
                for (int j = i + 1; j < g.Stage.Platforms.Count; j++)
                {
                    Assert.False(StageRules.Overlaps(g.Stage.Platforms[i], g.Stage.Platforms[j]));
                }
            }
        }
    }

    [Fact]
    public void TwoCharacterListFitMatchesThePairwiseFit()
    {
        // The N-character overload must be bit-identical to the pairwise fit for two
        // characters (pure predicates over the same set) — the 2P generation stream
        // depends on it.
        for (ulong seed = 1; seed <= 50; seed++)
        {
            var rng = new Pcg32(seed);
            GameGenome g = GameGenome.Generate(GenerationConfig.Default, rng);
            StageGenome pair = StageRules.FitToCharacters(
                g.Stage, g.Characters[0], g.Characters[1],
                MatchConfig.Default.Gravity, MatchConfig.Default.PlayerBaseWidth);
            StageGenome list = StageRules.FitToCharacters(
                g.Stage, g.Characters,
                MatchConfig.Default.Gravity, MatchConfig.Default.PlayerBaseWidth);
            Assert.Equal(pair.Platforms, list.Platforms);
            Assert.Equal(pair.Params.ToArray(), list.Params.ToArray());
        }
    }

    [Fact]
    public void IntGeneFloorsAndClampsInclusive()
    {
        Assert.Equal(2, StageRules.IntGene(2.0f, 2, 16));
        Assert.Equal(2, StageRules.IntGene(2.99f, 2, 16));
        Assert.Equal(16, StageRules.IntGene(16.0f, 2, 16)); // top of range floors to top
        Assert.Equal(2, StageRules.IntGene(-5f, 2, 16));
    }

    [Fact]
    public void ConnectivityDetectsAJumpableGapAndAnUnjumpableOne()
    {
        // Gap of exactly jumpLength (2): connected (the legacy mirror seam).
        var seamGap = new[]
        {
            new PlatformGene(-8, -3, 6, 1), // span [-8, -2]
            new PlatformGene(0, -3, 6, 1),  // span [0, 6] — gap exactly 2
        };
        Assert.True(StageRules.IsConnected(seamGap, jumpHeight: 2, jumpLength: 2));
        // FlatPair's gap is 4 — beyond jump reach.
        Assert.False(StageRules.IsConnected(FlatPair, jumpHeight: 2, jumpLength: 2));
    }

    [Fact]
    public void MutationTurningMirroredOnTransformsAnAsymmetricStage()
    {
        // Drive GameGenomeOps.Mutate over seeds until one lands mirrored ≥ 0.5 AND the
        // pre-mutation layout is asymmetric — then the layout must be the TRANSFORM of
        // the original (platform multiset ⊆ transform of source half), not a regen.
        var asymmetric = new[]
        {
            new PlatformGene(-6, -3, 4, 1),
            new PlatformGene(1, -1, 3, 1),
        };
        GameGenome game = BrawlerSim.Tests.Support.TestGames.FlatArena();
        var stage = new StageGenome(asymmetric, StageRules.LegacyParams(asymmetric));
        var genome = new GameGenome(game.Characters, stage);

        bool sawTransform = false;
        for (ulong seed = 0; seed < 200 && !sawTransform; seed++)
        {
            GameGenome mutated = GameGenomeOps.Mutate(genome, new Pcg32(seed));
            if (!StageRules.IsMirrored(mutated.Stage.Params))
            {
                continue;
            }
            Assert.True(StageRules.IsSymmetric(mutated.Stage.Platforms),
                $"seed {seed}: mirrored gene on but layout asymmetric");
            // Transform (not regen) preserves at least one original source platform.
            if (mutated.Stage.Platforms.Contains(new PlatformGene(-6, -3, 4, 1))
                || mutated.Stage.Platforms.Contains(new PlatformGene(1, -1, 3, 1)))
            {
                sawTransform = true;
            }
        }
        Assert.True(sawTransform, "no mutation in 200 seeds exercised the mirror transform");
    }
}
