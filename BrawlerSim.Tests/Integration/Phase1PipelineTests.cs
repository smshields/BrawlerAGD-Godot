using System.Text;
using BrawlerSim.Determinism;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using Xunit;

namespace BrawlerSim.Tests.Integration;

/// <summary>
/// End-to-end exercise of everything Phase 1 built: seeded generation → repeated
/// crossover/mutation → serialization, with determinism verified by fingerprint.
/// </summary>
public class Phase1PipelineTests
{
    private const int PopulationSize = 20;
    private const int Generations = 10;
    private const float MutationRate = 0.4f;

    private static ulong RunPipeline(ulong seed)
    {
        var rng = new Pcg32(seed);
        var population = new List<GameGenome>(PopulationSize);
        for (int i = 0; i < PopulationSize; i++)
        {
            population.Add(GameGenome.Generate(GenerationConfig.Default, rng));
        }

        for (int gen = 0; gen < Generations; gen++)
        {
            var next = new List<GameGenome>(PopulationSize);
            for (int i = 0; i < PopulationSize; i++)
            {
                GameGenome a = population[rng.NextInt(PopulationSize)];
                GameGenome b = population[rng.NextInt(PopulationSize)];
                GameGenome child = GameGenomeOps.Breed(a, b, MutationRate, rng);
                Assert.Empty(child.Validate());
                next.Add(child);
            }
            population = next;
        }

        ulong hash = Fnv1a.OffsetBasis;
        foreach (GameGenome genome in population)
        {
            string json = GameGenomeJson.Serialize(new GameRecord("g", null, genome));
            hash = Fnv1a.Hash(Encoding.UTF8.GetBytes(json), hash);
        }
        return hash;
    }

    [Fact]
    public void TenGenerationsOfBreedingStayValidAndDeterministic()
    {
        Assert.Equal(RunPipeline(20260707), RunPipeline(20260707));
    }

    [Fact]
    public void DifferentSeedsProduceDifferentPopulations()
    {
        Assert.NotEqual(RunPipeline(1), RunPipeline(2));
    }

    /// <summary>
    /// Cross-platform / cross-runtime canary. This value was produced on macOS ARM64,
    /// .NET 8; CI runs Linux x64. If the two ever disagree, some operation in the genome
    /// pipeline is not bit-deterministic across platforms (prime suspect: transcendental
    /// functions behind DetMath) and must be hardened per the determinism contract —
    /// treat a failure here as a release blocker, not a flaky test.
    /// </summary>
    [Fact]
    public void PopulationFingerprintMatchesGoldenValue()
    {
        // Re-pinned 2026-08-13: Smash-style stage containment (designer;
        // docs/features/four-player.md follow-up, DEVIATIONS #33) — every platform
        // must sit completely inside the kill box and the floor must clear the bottom
        // kill line by the derived HUD-band clearance. Generator acceptance, crossover
        // platform repair, mirror-transform validity, and the platform-fit bounds all
        // changed (different rejection/redraw streams and serialized bytes). Match
        // goldens + utility golden unmoved (fixture genomes). Prior pin:
        // 2963760689975173760.
        // Re-pinned 2026-07-27: the platform fit's body-gap pass became an ITERATIVE
        // multi-strategy solver (designer: asymmetric gaps still appeared in play) —
        // re-scans after every move, five strategies per corridor, force-dock fallback.
        // RNG-free like its predecessor: only platform coordinates differ. Audited
        // seeds 1-800: asymmetric corridors 248 stages → ZERO, connectivity unchanged,
        // zero overlaps. Match goldens unmoved. Prior pin 10847440123006787147.
        // Re-pinned 2026-07-22 (2nd): per-character platform fit — Generate/Crossover/
        // Re-pinned 2026-08-12: Four Player Support (docs/features/four-player.md) —
        // the stage schema appended spawn3X/Y + spawn4X/Y (every stage carries FOUR
        // spawn points), the generator draws two extra spawns (2 draws each: platform
        // index + fraction; mirrored stages mirror spawn 3 for spawn 4), spawns now
        // avoid overlapping each other (best-effort: 4 strict separation regrows in
        // front of the original 4 bare-tolerant attempts, occupied/axis-clearance
        // column blocking), and game.json is v9 (four spawn genes in the serialized
        // bytes). Match goldens + utility golden unmoved (fixture genomes, 2P never
        // reads spawns 3/4). Prior pin: 9743610122867389384.
        // Mutate now MOVE platforms so both characters can traverse (RNG-free, so draw
        // order is unchanged; only platform coordinates differ). Match goldens unmoved.
        // Re-pinned 2026-07-22: Spawning Behaviors appended two stage genes
        // (platformSpawnDuration, spawnInvulnDuration) — two more generation draws per
        // stage and two more serialized fields. Match goldens unmoved (feature defaults
        // off on legacy games). Prior pins below.
        // Re-pinned 2026-07-21 (3rd): Map Size — the stage grew an 11-gene ParamSet
        // (new generation/crossover/mutation draws), the generator was rewritten for
        // dynamic dimensions/symmetry, and game.json became v7 (stage params in the
        // serialized bytes). Second pin same day: growth-stack RE-SEEDING so platform
        // budgets actually fill (was 25% lone-pair stages; now 96% mean fill). Third:
        // BODY-SAFE spawn columns + degenerate-layout regrow (the spawn-eject deaths
        // found by the tall-narrow and seed-152 probes).
        // Prior pins: 17408519500504785457 / 11630069815993670970 (map size, same
        // day), 10906770023368156630 (reflect genes), 11998549590211428551 (five
        // buttons), 6139255332495310431 (v5 bytes only), 5768454974650524447
        // (fast fall/crouch/DI), 16079587979934170348 (dash slot),
        // 10607725140721060960 (shield), 5432710911100783110 (two moves),
        // 13551893661434631362, 9300943650238635838.
        Assert.Equal(14350526745249818436UL, RunPipeline(20260707));
    }
}
