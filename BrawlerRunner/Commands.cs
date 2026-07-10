using System.Diagnostics;
using System.Globalization;
using BrawlerSim;
using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Fitness;
using BrawlerSim.Genome;
using BrawlerSim.Replay;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;

namespace BrawlerRunner;

internal static class Commands
{
    public static int Usage()
    {
        Console.WriteLine($"BrawlerAGD runner — sim core v{SimInfo.Version} ({SimInfo.TicksPerSecond} Hz)");
        Console.WriteLine();
        Console.WriteLine("Commands:");
        Console.WriteLine("  evolve   --out <dir> [--seed 1] [--pop 100] [--generations 100] [--rounds 1]");
        Console.WriteLine("           [--dropout 0.5] [--mutation 0.4] [--resume]");
        Console.WriteLine("           [--agent utility|dtree] [--agent-randomness 0.15] [--agent-interval 8]");
        Console.WriteLine("  evaluate --game <game.json> [--seed 7] [--rounds 5]");
        Console.WriteLine("           [--agent utility|dtree] [--agent-randomness 0.15] [--agent-interval 8]");
        Console.WriteLine("  replay   --game <game.json> --trace <trace.json>");
        Console.WriteLine("  import   --unity-dir <GameX folder> --out <game.json>");
        Console.WriteLine("  bench    <unity game folder>");
        Console.WriteLine("  noise    --games <g1.json,g2.json,...> [--reps 20] [--rounds 5] [--aggregate median|mean]");
        Console.WriteLine("           [--max-seconds 60] [--target-seconds 45] [--seed 1] [--agent ...] — fitness noise per genome (CSV)");
        Console.WriteLine("  popdiv   --run <run dir> — mean pairwise normalized genome distance of the population");
        return 1;
    }

    /// <summary>
    /// Fitness-noise measurement (2026-07-09 noise study): evaluates each genome
    /// `reps` times with independent seed streams under one evaluation config and
    /// reports the spread. CSV: game, config, mean, std, min, max, drawRate.
    /// </summary>
    public static int Noise(string[] args)
    {
        var opts = ParseOptions(args);
        string[] games = Require(opts, "games").Split(',');
        int reps = GetInt(opts, "reps", 20);
        int rounds = GetInt(opts, "rounds", 5);
        bool median = opts.GetValueOrDefault("aggregate", "median") == "median";
        float maxSeconds = GetFloat(opts, "max-seconds", 60f);
        float targetSeconds = GetFloat(opts, "target-seconds", 45f);
        ulong seed = (ulong)GetInt(opts, "seed", 1);
        AgentConfig agent = ParseAgent(opts);
        var match = MatchConfig.Default with { MaxMatchSeconds = maxSeconds };
        var fitness = new StandardFitness(targetSeconds, maxSeconds);
        string config = $"r{rounds}-{(median ? "med" : "mean")}-{maxSeconds:F0}s-t{targetSeconds:F0}";

        Console.WriteLine("game,config,reps,mean,std,min,max,drawRate");
        foreach (string path in games)
        {
            GameRecord record = GameGenomeJson.Load(path);
            var scores = new double[reps];
            int draws = 0, matches = 0;
            for (int rep = 0; rep < reps; rep++)
            {
                var roundScores = new List<float>(rounds);
                for (int round = 0; round < rounds; round++)
                {
                    ulong matchSeed = SeedMix.MatchSeed(seed, rep, 0, round);
                    MatchResult result = MatchRunner.Run(record.Genome, AiSources(matchSeed, agent), match);
                    roundScores.Add(fitness.Evaluate(result));
                    matches++;
                    if (result.LoserIndex < 0)
                    {
                        draws++;
                    }
                }
                roundScores.Sort();
                scores[rep] = median ? roundScores[rounds / 2] : roundScores.Average();
            }
            double mean = scores.Average();
            double std = Math.Sqrt(scores.Select(v => (v - mean) * (v - mean)).Sum() / (reps - 1));
            Console.WriteLine(FormattableString.Invariant(
                $"{record.Name},{config},{reps},{mean:F2},{std:F2},{scores.Min():F2},{scores.Max():F2},{draws / (double)matches:F3}"));
        }
        return 0;
    }

    public static int PopDiv(string[] args)
    {
        var opts = ParseOptions(args);
        (EvolutionEngine engine, EvolutionConfig config, _) = RunStore.Load(Require(opts, "run"));
        float diversity = GenomeDistance.MeanPairwise(engine.Population, config.Generation);
        Console.WriteLine(FormattableString.Invariant(
            $"{opts["run"]},gen{engine.GenerationsCompleted},popdiv,{diversity:F4}"));
        return 0;
    }

    public static int Evolve(string[] args)
    {
        var opts = ParseOptions(args);
        string outDir = Require(opts, "out");
        int generations = GetInt(opts, "generations", 100);

        EvolutionEngine engine;
        EvolutionConfig config;
        List<GenerationStats> history;
        if (opts.ContainsKey("resume"))
        {
            (engine, config, history) = RunStore.Load(outDir);
            Console.WriteLine($"Resumed {outDir} at generation {engine.GenerationsCompleted}.");
        }
        else
        {
            config = new EvolutionConfig
            {
                Seed = (ulong)GetInt(opts, "seed", 1),
                PopulationSize = GetInt(opts, "pop", 100),
                RoundsPerIndividual = GetInt(opts, "rounds", 1),
                DropoutRate = GetFloat(opts, "dropout", 0.5f),
                MutationRate = GetFloat(opts, "mutation", 0.4f),
                Agent = ParseAgent(opts),
                TargetGameLengthSeconds = GetFloat(opts, "target-seconds", 45f),
                Match = MatchConfig.Default with { MaxMatchSeconds = GetFloat(opts, "max-seconds", 60f) },
                DiversityWeight = GetFloat(opts, "diversity-weight", 0f),
            };
            engine = new EvolutionEngine(config);
            history = new List<GenerationStats>();
        }

        float bestSoFar = history.Count > 0 ? history.Max(s => s.TopFitness) : float.MinValue;
        var stopwatch = Stopwatch.StartNew();
        while (engine.GenerationsCompleted < generations)
        {
            GenerationStats stats = engine.Step();
            history.Add(stats);

            bool improved = stats.TopFitness > bestSoFar;
            if (improved)
            {
                bestSoFar = stats.TopFitness;
                (_, InputTrace trace) = engine.ReplayEvaluation(stats.BestIndex, stats.Generation);
                RunStore.SaveBest(outDir, engine.Population[stats.BestIndex], stats, trace);
            }
            RunStore.SaveCheckpoint(outDir, engine, config, history);

            Console.WriteLine(
                $"gen {stats.Generation,4}  top {stats.TopFitness,8:F2}  avg {stats.AverageFitness,8:F2}  " +
                $"survivors {stats.AverageSurvivorFitness,8:F2}  {stopwatch.Elapsed.TotalSeconds,7:F1}s" +
                (improved ? "  ★ new best" : ""));
        }
        Console.WriteLine($"Done: {generations} generations in {stopwatch.Elapsed.TotalMinutes:F1} min. Run saved to {outDir}.");
        return 0;
    }

    public static int Evaluate(string[] args)
    {
        var opts = ParseOptions(args);
        GameRecord record = GameGenomeJson.Load(Require(opts, "game"));
        ulong seed = (ulong)GetInt(opts, "seed", 7);
        int rounds = GetInt(opts, "rounds", 5);
        AgentConfig agent = ParseAgent(opts);
        var fitness = new StandardFitness();

        Console.WriteLine(
            $"Evaluating '{record.Name}' ({record.Origin}) over {rounds} rounds, seed {seed}, agent {agent.Kind}:");
        var scores = new List<float>();
        for (int round = 0; round < rounds; round++)
        {
            ulong matchSeed = SeedMix.MatchSeed(seed, 0, 0, round);
            MatchResult result = MatchRunner.Run(record.Genome, AiSources(matchSeed, agent));
            float score = fitness.Evaluate(result);
            scores.Add(score);
            Console.WriteLine(
                $"  round {round}: fitness {score,8:F2}  length {result.LengthSeconds,5:F1}s  " +
                $"loser {(result.LoserIndex < 0 ? "draw" : result.LoserIndex.ToString())}  " +
                $"dmg {result.Players[0].TotalDamageTaken:F0}/{result.Players[1].TotalDamageTaken:F0}  " +
                $"hits {result.Players[0].TotalHitsReceived}/{result.Players[1].TotalHitsReceived}  " +
                $"stocks {result.Players[0].RemainingStocks}/{result.Players[1].RemainingStocks}");
        }
        scores.Sort();
        Console.WriteLine($"Median fitness ({fitness.Name}): {scores[scores.Count / 2]:F2}");
        return 0;
    }

    public static int Replay(string[] args)
    {
        var opts = ParseOptions(args);
        GameRecord record = GameGenomeJson.Load(Require(opts, "game"));
        InputTrace trace = InputTraceJson.Load(Require(opts, "trace"));
        MatchResult result = MatchRunner.Replay(record.Genome, trace);
        Console.WriteLine(
            $"Replayed {result.Ticks} ticks ({result.LengthSeconds:F1}s): " +
            $"loser {(result.LoserIndex < 0 ? "draw" : result.LoserIndex.ToString())}, " +
            $"final hash {result.FinalHash}");
        return 0;
    }

    public static int Import(string[] args)
    {
        var opts = ParseOptions(args);
        GameRecord record = LegacyImporter.ImportGameFolder(Require(opts, "unity-dir"));
        var violations = record.Genome.Validate();
        foreach (string violation in violations)
        {
            Console.WriteLine($"  warning: {violation}");
        }
        GameGenomeJson.Save(record, Require(opts, "out"));
        Console.WriteLine($"Imported '{record.Name}' → {opts["out"]}" +
            (violations.Count > 0 ? $" ({violations.Count} range warnings)" : ""));
        return 0;
    }

    public static int BenchCommand(string[] args)
    {
        if (args.Length < 2)
        {
            return Usage();
        }
        Bench.Run(args[1]);
        return 0;
    }

    private static IInputSource[] AiSources(ulong seed, AgentConfig agent) => new IInputSource[]
    {
        agent.CreateSource(new Pcg32(seed, 0)),
        agent.CreateSource(new Pcg32(seed, 1)),
    };

    private static AgentConfig ParseAgent(Dictionary<string, string> opts) => new()
    {
        Kind = opts.GetValueOrDefault("agent", "utility") switch
        {
            "dtree" or "decision-tree" => AgentKind.DecisionTree,
            "utility" => AgentKind.Utility,
            var other => throw new ArgumentException($"Unknown --agent '{other}' (utility|dtree)."),
        },
        Randomness = GetFloat(opts, "agent-randomness", AgentConfig.Default.Randomness),
        DecisionIntervalTicks = GetInt(opts, "agent-interval", AgentConfig.Default.DecisionIntervalTicks),
    };

    private static Dictionary<string, string> ParseOptions(string[] args)
    {
        var opts = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 1; i < args.Length; i++)
        {
            if (!args[i].StartsWith("--", StringComparison.Ordinal))
            {
                continue;
            }
            string key = args[i][2..];
            bool hasValue = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal);
            opts[key] = hasValue ? args[++i] : "";
        }
        return opts;
    }

    private static string Require(Dictionary<string, string> opts, string key) =>
        opts.TryGetValue(key, out string? value) && value.Length > 0
            ? value
            : throw new ArgumentException($"Missing required option --{key}");

    private static int GetInt(Dictionary<string, string> opts, string key, int fallback) =>
        opts.TryGetValue(key, out string? value) ? int.Parse(value, CultureInfo.InvariantCulture) : fallback;

    private static float GetFloat(Dictionary<string, string> opts, string key, float fallback) =>
        opts.TryGetValue(key, out string? value) ? float.Parse(value, CultureInfo.InvariantCulture) : fallback;
}
