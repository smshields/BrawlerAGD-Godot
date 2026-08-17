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
        Console.WriteLine("           [--players 2|3|4] [--dropout 0.5] [--mutation 0.4] [--resume]");
        Console.WriteLine("           [--agent utility|dtree] [--agent-randomness 0.15] [--agent-interval 8]");
        Console.WriteLine("           [--composition pinned|random|<attack,shield,dash,random x4>] [--type-reroll 0.2]");
        Console.WriteLine("           [--range \"schema.key=min:max;...\"]  (schemas: character|move|shield|dash|projectile|stage)");
        Console.WriteLine("           [--fitness standard-v4|ffa-v1|standard-v3|standard-v2]  (default: v4 at 2P, ffa-v1 at 3/4P)");
        Console.WriteLine("  evaluate --game <game.json> [--seed 7] [--rounds 5] [--fitness standard-v4|ffa-v1|standard-v3|standard-v2]");
        Console.WriteLine("           [--breakdown] [--max-seconds 60] [--target-seconds 45]");
        Console.WriteLine("           [--agent utility|dtree] [--agent-randomness 0.15] [--agent-interval 8]");
        Console.WriteLine("  replay   --game <game.json> --trace <trace.json>");
        Console.WriteLine("  import   --unity-dir <GameX folder> --out <game.json>");
        Console.WriteLine("  bench    <unity game folder>");
        Console.WriteLine("  noise    --games <g1.json,g2.json,...> [--reps 20] [--rounds 5] [--aggregate median|mean]");
        Console.WriteLine("           [--max-seconds 60] [--target-seconds 45] [--seed 1] [--agent ...] — fitness noise per genome (CSV)");
        Console.WriteLine("  popdiv   --run <run dir> — mean pairwise normalized genome distance of the population");
        Console.WriteLine("  prep-game --game <built-game.json> --out <embedded.json> — packaging gate:");
        Console.WriteLine("           requires a COMPLETE built game (8 chars + 4 stages) and applies the");
        Console.WriteLine("           namegen naming pass so packaged games never ship default names");
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
        var match = MatchConfig.Default with
        {
            MaxMatchSeconds = maxSeconds,
            MaxStunSeconds = GetFloat(opts, "max-stun", float.PositiveInfinity),
        };
        Console.WriteLine("game,config,reps,mean,std,min,max,drawRate");
        foreach (string path in games)
        {
            GameRecord record = GameGenomeJson.Load(path);
            int players = record.Genome.Characters.Count;
            IFitnessFunction fitness = FitnessRegistry.Create(
                opts.GetValueOrDefault("fitness"), targetSeconds, maxSeconds,
                opts.ContainsKey("collision-scalar") ? GetFloat(opts, "collision-scalar", 0f) : null,
                players);
            string config = $"r{rounds}-{(median ? "med" : "mean")}-{maxSeconds:F0}s-t{targetSeconds:F0}-{fitness.Name}"
                + (opts.ContainsKey("collision-scalar")
                    ? FormattableString.Invariant($"-cs{GetFloat(opts, "collision-scalar", 0f):0.##}")
                    : "");
            var scores = new double[reps];
            int draws = 0, matches = 0;
            for (int rep = 0; rep < reps; rep++)
            {
                var roundScores = new List<float>(rounds);
                for (int round = 0; round < rounds; round++)
                {
                    ulong matchSeed = SeedMix.MatchSeed(seed, rep, 0, round);
                    MatchResult result = MatchRunner.Run(
                        record.Genome, AiSources(matchSeed, agent, players), match);
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
                Match = MatchConfig.Default with
                {
                    MaxMatchSeconds = GetFloat(opts, "max-seconds", 60f),
                    MaxStunSeconds = GetFloat(opts, "max-stun", float.PositiveInfinity),
                },
                DiversityWeight = GetFloat(opts, "diversity-weight", 0f),
                // Absent --fitness = auto: standard-v4 at 2 players, ffa-v1 at 3/4.
                FitnessName = opts.GetValueOrDefault("fitness"),
                FitnessCollisionScalar = opts.ContainsKey("collision-scalar")
                    ? GetFloat(opts, "collision-scalar", 0f) : null,
                Generation = ParseGeneration(opts),
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
        float maxSeconds = GetFloat(opts, "max-seconds", 60f);
        var matchConfig = MatchConfig.Default with
        {
            MaxMatchSeconds = maxSeconds,
            MaxStunSeconds = GetFloat(opts, "max-stun", float.PositiveInfinity),
        };
        int players = record.Genome.Characters.Count;
        IFitnessFunction fitness = FitnessRegistry.Create(
            opts.GetValueOrDefault("fitness"),
            GetFloat(opts, "target-seconds", 45f), maxSeconds,
            opts.ContainsKey("collision-scalar") ? GetFloat(opts, "collision-scalar", 0f) : null,
            players);
        bool breakdown = opts.ContainsKey("breakdown");

        Console.WriteLine(
            $"Evaluating '{record.Name}' ({record.Origin}, {players} players) over {rounds} rounds, " +
            $"seed {seed}, agent {agent.Kind}, fitness {fitness.Name}:");
        var scores = new List<float>();
        for (int round = 0; round < rounds; round++)
        {
            ulong matchSeed = SeedMix.MatchSeed(seed, 0, 0, round);
            MatchResult result = MatchRunner.Run(
                record.Genome, AiSources(matchSeed, agent, players), matchConfig);
            float score = fitness.Evaluate(result);
            scores.Add(score);
            // Per-player columns joined with '/' — any player count (2026-08-12).
            string Per(Func<PlayerStats, string> field) =>
                string.Join("/", result.Players.Select(field));
            Console.WriteLine(
                $"  round {round}: fitness {score,8:F2}  length {result.LengthSeconds,5:F1}s  " +
                $"loser {(result.LoserIndex < 0 ? "draw" : result.LoserIndex.ToString())}  " +
                $"place {string.Join("/", result.Placements ?? Array.Empty<int>())}  " +
                $"dmg {Per(p => p.TotalDamageTaken.ToString("F0"))}  " +
                $"hits {Per(p => p.TotalHitsReceived.ToString())}  " +
                $"stocks {Per(p => p.RemainingStocks.ToString())}  " +
                $"ko/sd {Per(p => $"{p.KOs}-{p.SelfDestructs}")}  " +
                $"stun {Per(p => (100f * p.StunTicks / result.Ticks).ToString("F0") + "%")}  " +
                $"uses {Per(p => string.Join("+", p.MoveUses ?? Array.Empty<int>()))}  " +
                $"jumps {Per(p => p.Jumps.ToString())}  " +
                $"shield(act-blk-brk) {Per(p => $"{p.ShieldActivations}-{p.BlockedHits}-{p.ShieldBreaks}")}  " +
                $"dash(n-dodge) {Per(p => $"{p.DashCount}-{p.DashInvulnDodges}")}  " +
                $"ff-crouch-di {Per(p => $"{p.FastFallTicks}-{p.CrouchTicks}-{p.DIInfluencedHits}")}  " +
                $"proj(fired-hit-refl) {Per(p => $"{p.ProjectilesFired}-{p.ProjectileHits}-{p.ProjectilesReflected}")}");
            if (breakdown && fitness is ComposedFitness composed)
            {
                Console.WriteLine("           " + string.Join("  ",
                    composed.Breakdown(result).Select(t => $"{t.Name} {t.Value:F1}")));
            }
            else if (breakdown && fitness is StandardFitnessV3 v3)
            {
                Console.WriteLine("           " + string.Join("  ",
                    v3.Breakdown(result).Select(t => $"{t.Name} {t.Value:F1}")));
            }
            else if (breakdown && fitness is StandardFitnessV4 v4)
            {
                Console.WriteLine("           " + string.Join("  ",
                    v4.Breakdown(result).Select(t => $"{t.Name} {t.Value:F1}")));
            }
            else if (breakdown && fitness is FfaFitnessV1 ffa)
            {
                Console.WriteLine("           " + string.Join("  ",
                    ffa.Breakdown(result).Select(t => $"{t.Name} {t.Value:F1}")));
            }
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

    /// <summary>
    /// Packaging gate (Packaged Games, 2026-08-15 — docs/features/packaged-games.md):
    /// loads a built game, refuses incomplete ones, applies the SAME naming rules as
    /// the Game Player (BuiltGameNaming pattern + content-derived seeds, roster-unique
    /// via UniqueNameSession), and writes the embedded document the packaged app
    /// boots from. The genome→namegen mapping mirrors BuiltGameNamer (godot/) — the
    /// app layer and this dev tool are the two namegen consumers by design.
    /// </summary>
    public static int PrepGame(string[] args)
    {
        var opts = ParseOptions(args);
        BuiltGame game = BuiltGameJson.Load(Require(opts, "game"));
        if (!game.IsComplete)
        {
            Console.Error.WriteLine(
                $"'{game.Name}' is incomplete ({game.Characters.Count}/{BuiltGame.RequiredCharacters} characters, "
                + $"{game.Stages.Count}/{BuiltGame.RequiredStages} stages) — finish it in BUILD GAME first.");
            return 1;
        }

        var generator = NameGen.NameGenerator.CreateDefault();
        var session = new NameGen.UniqueNameSession(generator);
        foreach (BuiltCharacter c in game.Characters.Where(c => !BuiltGameNaming.NeedsGeneratedName(c.DisplayName)))
        {
            session.Reserve(c.DisplayName);
        }
        foreach (BuiltStage s in game.Stages.Where(s => !BuiltGameNaming.NeedsGeneratedName(s.DisplayName)))
        {
            session.Reserve(s.DisplayName);
        }
        int named = 0;
        for (int i = 0; i < game.Characters.Count; i++)
        {
            if (!BuiltGameNaming.NeedsGeneratedName(game.Characters[i].DisplayName))
            {
                continue;
            }
            game.Characters[i] = game.Characters[i] with
            {
                DisplayName = session.GenerateCharacterName(
                    MapCharacter(game.Characters[i].Character),
                    new NameGen.NameOptions
                    {
                        Seed = BuiltGameNaming.NamingSeed(game.Characters[i].Character),
                    }).Display,
            };
            named++;
        }
        for (int i = 0; i < game.Stages.Count; i++)
        {
            if (!BuiltGameNaming.NeedsGeneratedName(game.Stages[i].DisplayName))
            {
                continue;
            }
            game.Stages[i] = game.Stages[i] with
            {
                DisplayName = session.GenerateStageName(
                    new NameGen.StageGenome(game.Stages[i].Stage.Params.ToDictionary()),
                    new NameGen.NameOptions
                    {
                        Seed = BuiltGameNaming.NamingSeed(game.Stages[i].Stage),
                    }).Display,
            };
            named++;
        }
        BuiltGameJson.Save(game, Require(opts, "out"));
        Console.WriteLine($"prepared '{game.Name}': named {named} elements → {opts["out"]}");
        // The shell packager reads these two lines to brand the build.
        Console.WriteLine($"name={game.Name}");
        Console.WriteLine($"slug={Slug(game.Name)}");
        return 0;
    }

    private static NameGen.CharacterGenome MapCharacter(CharacterGenome c) => new(
        c.Params.ToDictionary(),
        c.Moves.Select(m => new NameGen.MoveGenome(m.Type switch
        {
            MoveType.Shield => NameGen.MoveKind.Shield,
            MoveType.Dash => NameGen.MoveKind.Dash,
            MoveType.Projectile => NameGen.MoveKind.Projectile,
            _ => NameGen.MoveKind.Melee,
        }, m.Params.ToDictionary())).ToList());

    private static string Slug(string name)
    {
        var slug = new string(name.ToLowerInvariant()
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '-').ToArray()).Trim('-');
        while (slug.Contains("--"))
        {
            slug = slug.Replace("--", "-");
        }
        return slug.Length > 0 ? slug : "game";
    }

    private static IInputSource[] AiSources(ulong seed, AgentConfig agent, int players = 2)
    {
        var sources = new IInputSource[players];
        for (int p = 0; p < players; p++)
        {
            sources[p] = agent.CreateSource(new Pcg32(seed, (ulong)p));
        }
        return sources;
    }

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

    /// <summary>Composition + advanced ranges (2026-07-14,
    /// docs/features/evolve-composition-and-ranges.md) + player count (2026-08-12,
    /// docs/features/four-player.md).</summary>
    private static GenerationConfig ParseGeneration(Dictionary<string, string> opts)
    {
        int players = GetInt(opts, "players", 2);
        if (players is < 2 or > 4)
        {
            throw new ArgumentException($"--players must be 2, 3, or 4 (got {players}).");
        }
        GenerationConfig generation = GenerationConfig.Default with { CharacterCount = players };
        string composition = opts.GetValueOrDefault("composition", "pinned");
        if (!string.Equals(composition, "pinned", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<SlotSpec> slots = string.Equals(composition, "random", StringComparison.OrdinalIgnoreCase)
                ? GenerationConfig.RandomComposition
                : composition.Split(',').Select(s => Enum.Parse<SlotSpec>(s.Trim(), ignoreCase: true)).ToArray();
            if (slots.Count != InputFrame.ActionCount)
            {
                throw new ArgumentException(
                    $"--composition needs {InputFrame.ActionCount} slots, got {slots.Count}.");
            }
            generation = generation with
            {
                ButtonComposition = slots,
                TypeRerollRate = GetFloat(opts, "type-reroll", generation.TypeRerollRate),
            };
        }
        if (opts.TryGetValue("range", out string? ranges) && ranges.Length > 0)
        {
            var overrides = new List<RangeOverride>();
            foreach (string entry in ranges.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                string[] kv = entry.Split('=');
                string[] path = kv[0].Trim().Split('.');
                string[] range = kv.Length == 2 ? kv[1].Split(':') : Array.Empty<string>();
                if (path.Length != 2 || range.Length != 2)
                {
                    throw new ArgumentException($"Bad --range entry '{entry}' (want schema.key=min:max).");
                }
                overrides.Add(new RangeOverride(path[0].Trim(), path[1].Trim(),
                    float.Parse(range[0], CultureInfo.InvariantCulture),
                    float.Parse(range[1], CultureInfo.InvariantCulture)));
            }
            generation = generation.WithRangeOverrides(overrides);
        }
        return generation;
    }

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
