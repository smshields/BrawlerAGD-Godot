using System.Diagnostics;
using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;

namespace BrawlerRunner;

internal static class Bench
{
    public static void Run(string gameDir)
    {
        var genome = LegacyImporter.ImportGameFolder(gameDir).Genome;
        // Warmup
        for (ulong s = 0; s < 10; s++) RunOne(genome, s);
        const int n = 500;
        var sw = Stopwatch.StartNew();
        long ticks = 0;
        for (ulong s = 0; s < n; s++) ticks += RunOne(genome, s).Ticks;
        sw.Stop();
        Console.WriteLine($"{n} matches, {ticks} sim-ticks in {sw.ElapsedMilliseconds} ms " +
            $"= {n * 1000.0 / sw.ElapsedMilliseconds:F0} matches/s single-threaded " +
            $"({ticks / (double)n / 60.0:F1} avg sim-seconds/match)");
    }

    private static MatchResult RunOne(BrawlerSim.Genome.GameGenome g, ulong seed) =>
        MatchRunner.Run(g, new IInputSource[]
        {
            AgentConfig.Default.CreateSource(new Pcg32(seed, 0)),
            AgentConfig.Default.CreateSource(new Pcg32(seed, 1)),
        });
}
