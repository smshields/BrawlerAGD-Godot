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
        Console.WriteLine("           [--fitness standard-v6|standard-v5|ffa-v2|standard-v4|ffa-v1|standard-v3|standard-v2]  (default: v5 at 2P, ffa-v2 at 3/4P; v6 = scaled-time experiment)");
        Console.WriteLine("           [--max-seconds 300]");
        Console.WriteLine("  mapelites --out <dir> [--seed 1] [--batch 100] [--batches 100] [--rounds 1]");
        Console.WriteLine("           [--players 2|3|4] [--mutation 0.4] [--init-random 500] [--pilot 10000] [--resume]");
        Console.WriteLine("           [--agent ...] [--composition ...] [--range ...] [--fitness ...] [--max-seconds 300]");
        Console.WriteLine("           — MAP-Elites illumination: 8^4 descriptor-grid archive of every generated game");
        Console.WriteLine("  evaluate --game <game.json> [--seed 7] [--rounds 5] [--fitness standard-v6|standard-v5|ffa-v2|standard-v4|ffa-v1|standard-v3|standard-v2]");
        Console.WriteLine("           [--breakdown] [--max-seconds 300] [--target-seconds 45]");
        Console.WriteLine("           [--agent utility|dtree] [--agent-randomness 0.15] [--agent-interval 8]");
        Console.WriteLine("  replay   --game <game.json> --trace <trace.json>");
        Console.WriteLine("  import   --unity-dir <GameX folder> --out <game.json>");
        Console.WriteLine("  bench    <unity game folder>");
        Console.WriteLine("  noise    --games <g1.json,g2.json,...> [--reps 20] [--rounds 5] [--aggregate median|mean]");
        Console.WriteLine("           [--max-seconds 300] [--target-seconds 45] [--seed 1] [--agent ...] — fitness noise per genome (CSV)");
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
        float maxSeconds = GetFloat(opts, "max-seconds", MatchConfig.Default.MaxMatchSeconds);
        float targetSeconds = GetFloat(opts, "target-seconds", 45f);
        ulong seed = (ulong)GetInt(opts, "seed", 1);
        AgentConfig agent = ParseAgent(opts);
        MatchConfig match = BuildMatchConfig(opts);
        Console.WriteLine("game,config,reps,mean,std,min,max,drawRate");
        foreach (string path in games)
        {
            GameRecord record = GameGenomeJson.Load(path);
            int players = record.Genome.Characters.Count;
            IFitnessFunction fitness = ResolveFitness(opts, players);
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
            (engine, config, history) = RunStore.Load(outDir, LoadSpriteSelector(opts),
                LoadStageThemeSelector(opts), LoadBackgroundSelector(opts));
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
                Match = BuildMatchConfig(opts),
                DiversityWeight = GetFloat(opts, "diversity-weight", 0f),
                // Absent --fitness = auto: standard-v5 at 2 players, ffa-v2 at 3/4 (2026-09-01).
                FitnessName = opts.GetValueOrDefault("fitness"),
                FitnessCollisionScalar = CollisionScalar(opts),
                // Sprite selection (2026-08-22) + stage tile themes (M4d,
                // 2026-09-01) + backgrounds (2026-09-02): on whenever the libraries
                // are found — cosmetic, RNG-free, fitness-blind; --no-sprites turns
                // all three off.
                Generation = opts.ContainsKey("no-sprites")
                    ? ParseGeneration(opts)
                    : ParseGeneration(opts) with
                    {
                        SpriteSelector = LoadSpriteSelector(opts),
                        StageThemeSelector = LoadStageThemeSelector(opts),
                        BackgroundSelector = LoadBackgroundSelector(opts),
                    },
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

    /// <summary>
    /// MAP-Elites run (2026-09-10, docs/features/map-elites.md): pilot-binned 8^4
    /// descriptor grid, per-batch checkpoints, resumable. The pilot (equal-frequency
    /// bin edges from N random genomes at this exact configuration) runs once at run
    /// start and its edges are FROZEN into every checkpoint.
    /// </summary>
    public static int MapElites(string[] args)
    {
        var opts = ParseOptions(args);
        string outDir = Require(opts, "out");
        int batches = GetInt(opts, "batches", 100);

        MapElitesEngine engine;
        MapElitesConfig config;
        List<MapElitesBatchStats> history;
        if (opts.ContainsKey("resume"))
        {
            (engine, config, history) = MapElitesStore.Load(outDir, LoadSpriteSelector(opts),
                LoadStageThemeSelector(opts), LoadBackgroundSelector(opts));
            Console.WriteLine(
                $"Resumed {outDir} at batch {engine.BatchesCompleted} "
                + $"({engine.Archive.Count} cells, {engine.CandidatesEvaluated} candidates).");
        }
        else
        {
            ulong seed = (ulong)GetInt(opts, "seed", 1);
            GenerationConfig generation = opts.ContainsKey("no-sprites")
                ? ParseGeneration(opts)
                : ParseGeneration(opts) with
                {
                    SpriteSelector = LoadSpriteSelector(opts),
                    StageThemeSelector = LoadStageThemeSelector(opts),
                    BackgroundSelector = LoadBackgroundSelector(opts),
                };
            int pilotSamples = GetInt(opts, "pilot", DescriptorBins.DefaultPilotSamples);
            var pilotWatch = Stopwatch.StartNew();
            DescriptorBins bins = DescriptorBins.FromPilot(
                generation, DescriptorBins.DefaultPilotSeed(seed), pilotSamples);
            Console.WriteLine(
                $"Pilot: {pilotSamples} genomes in {pilotWatch.Elapsed.TotalSeconds:F1}s — bin edges frozen.");
            config = new MapElitesConfig
            {
                Seed = seed,
                BatchSize = GetInt(opts, "batch", 100),
                InitialRandomCandidates = GetInt(opts, "init-random", 500),
                MutationRate = GetFloat(opts, "mutation", 0.4f),
                RoundsPerIndividual = GetInt(opts, "rounds", 1),
                Agent = ParseAgent(opts),
                TargetGameLengthSeconds = GetFloat(opts, "target-seconds", 45f),
                Match = BuildMatchConfig(opts),
                FitnessName = opts.GetValueOrDefault("fitness"),
                FitnessCollisionScalar = CollisionScalar(opts),
                Generation = generation,
                Bins = bins,
            };
            engine = new MapElitesEngine(config);
            history = new List<MapElitesBatchStats>();
        }

        float bestSoFar = history.Count > 0 ? history.Max(s => s.BestFitness) : float.MinValue;
        var stopwatch = Stopwatch.StartNew();
        while (engine.BatchesCompleted < batches)
        {
            MapElitesBatchStats stats = engine.Step();
            history.Add(stats);

            bool improved = stats.BestFitness > bestSoFar;
            if (improved && engine.Archive.Best is { } best)
            {
                bestSoFar = stats.BestFitness;
                (_, InputTrace trace) = engine.ReplayEvaluation(best);
                MapElitesStore.SaveBest(outDir, best, trace);
            }
            MapElitesStore.SaveCheckpoint(outDir, engine, config, history);

            Console.WriteLine(
                $"batch {stats.Batch,4}  cells {stats.Filled,4} ({stats.Coverage:P1})  " +
                $"qd {stats.QdScore,10:F1}  best {stats.BestFitness,8:F2}  " +
                $"+{stats.Insertions}/^{stats.Replacements}  {stopwatch.Elapsed.TotalSeconds,7:F1}s" +
                (stats.OutOfPilotRange > 0 ? $"  [out-of-pilot {stats.OutOfPilotRange}]" : "") +
                (improved ? "  * new best" : ""));
        }
        Console.WriteLine(
            $"Done: {batches} batches ({engine.CandidatesEvaluated} candidates) in "
            + $"{stopwatch.Elapsed.TotalMinutes:F1} min — {engine.Archive.Count}/{DescriptorBins.CellCount} "
            + $"cells filled. Run saved to {outDir}.");
        return 0;
    }

    public static int Evaluate(string[] args)
    {
        var opts = ParseOptions(args);
        GameRecord record = GameGenomeJson.Load(Require(opts, "game"));
        ulong seed = (ulong)GetInt(opts, "seed", 7);
        int rounds = GetInt(opts, "rounds", 5);
        AgentConfig agent = ParseAgent(opts);
        MatchConfig matchConfig = BuildMatchConfig(opts);
        int players = record.Genome.Characters.Count;
        IFitnessFunction fitness = ResolveFitness(opts, players);
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
                $"drops {Per(p => p.DropThroughs.ToString())}  " +
                $"proj(fired-hit-refl) {Per(p => $"{p.ProjectilesFired}-{p.ProjectileHits}-{p.ProjectilesReflected}")}");
            if (breakdown && fitness is IFitnessBreakdown itemized)
            {
                Console.WriteLine("           " + string.Join("  ",
                    itemized.Breakdown(result).Select(t => $"{t.Name} {t.Value:F1}")));
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
    /// loads a built game, refuses incomplete ones, applies the SAME presentation pass
    /// as the Game Player (BuiltGamePresentation: names + sprites + shared register,
    /// content-derived seeds, roster-unique), and writes the embedded document the
    /// packaged app boots from.
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
        int changed = BuiltGamePresentation.EnsurePresented(
            game, generator, LoadSpriteSelector(opts), LoadStageThemeSelector(opts),
            LoadBackgroundSelector(opts));
        BuiltGameJson.Save(game, Require(opts, "out"));
        Console.WriteLine($"prepared '{game.Name}': presented {changed} elements → {opts["out"]}");
        // The shell packager reads these two lines to brand the build.
        Console.WriteLine($"name={game.Name}");
        Console.WriteLine($"slug={Slug(game.Name)}");
        return 0;
    }

    /// <summary>The sprite library + tuning, from --sprites <slices.json> or found by
    /// walking up from the working directory (the repo's godot/assets). Null — with a
    /// loud warning — degrades the presentation pass to names only.</summary>
    internal static BrawlerSim.Sprites.SpriteSelector? LoadSpriteSelector(Dictionary<string, string> opts)
    {
        string? slices = opts.TryGetValue("sprites", out string? given)
            ? given
            : FindUpward(Path.Combine("godot", "assets", "players_v2_slices.json"));
        if (slices is null || !File.Exists(slices))
        {
            Console.Error.WriteLine(
                "warning: sprite library not found (godot/assets/players_v2_slices.json; "
                + "override with --sprites) — running without sprite selection.");
            return null;
        }
        var library = BrawlerSim.Sprites.SpriteLibrary.LoadFile(slices);
        string dir = Path.GetDirectoryName(Path.GetFullPath(slices))!;
        string tuning = Path.Combine(dir, "sprite_selection.json");
        var config = File.Exists(tuning)
            ? BrawlerSim.Sprites.SpriteSelectionConfig.LoadFile(tuning)
            : BrawlerSim.Sprites.SpriteSelectionConfig.Default;
        // Melee attack sprites (M4b) ride along whenever their library sits next to
        // the character slices; absent = character selection only.
        string moves = Path.Combine(dir, "moves_v2_slices.json");
        var moveLibrary = File.Exists(moves)
            ? BrawlerSim.Sprites.MoveSpriteLibrary.LoadFile(moves)
            : null;
        return new BrawlerSim.Sprites.SpriteSelector(library, config, moveLibrary: moveLibrary);
    }

    /// <summary>The stage theme library (M4d, 2026-09-01) — same auto-discovery as
    /// the sprite library; tile_selection.json rides alongside. Null (with a warning)
    /// = stages keep null theme genes / legacy names.</summary>
    internal static BrawlerSim.Sprites.StageThemeSelector? LoadStageThemeSelector(Dictionary<string, string> opts)
    {
        string? slices = opts.TryGetValue("tiles", out string? given)
            ? given
            : FindUpward(Path.Combine("godot", "assets", "tiles_v2_slices.json"));
        if (slices is null || !File.Exists(slices))
        {
            Console.Error.WriteLine(
                "warning: stage tile library not found (godot/assets/tiles_v2_slices.json; "
                + "override with --tiles) — running without stage theme selection.");
            return null;
        }
        var library = BrawlerSim.Sprites.StageThemeLibrary.LoadFile(slices);
        string tuning = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(slices))!, "tile_selection.json");
        var config = File.Exists(tuning)
            ? BrawlerSim.Sprites.StageThemeConfig.LoadFile(tuning)
            : BrawlerSim.Sprites.StageThemeConfig.Default;
        return new BrawlerSim.Sprites.StageThemeSelector(library, config);
    }

    /// <summary>The background library + palette data + tuning (backgrounds track,
    /// 2026-09-02) — from --backgrounds <index.json> or found by walking up like the
    /// other libraries; the palette/ramps/transition files and
    /// background_selection.json ride alongside the index. The tile theme library
    /// feeds palette harmony when present. Null (with a warning) = stages keep null
    /// background genes.</summary>
    internal static BrawlerSim.Backgrounds.BackgroundSelector? LoadBackgroundSelector(
        Dictionary<string, string> opts)
    {
        string? index = opts.TryGetValue("backgrounds", out string? given)
            ? given
            : FindUpward(Path.Combine("godot", "assets", "backgrounds_v1", "backgrounds_v1_index.json"));
        if (index is null || !File.Exists(index))
        {
            Console.Error.WriteLine(
                "warning: background library not found (godot/assets/backgrounds_v1/"
                + "backgrounds_v1_index.json; override with --backgrounds) — running "
                + "without background selection.");
            return null;
        }
        string dir = Path.GetDirectoryName(Path.GetFullPath(index))!;
        string master = Path.Combine(dir, "master_palette.json");
        string ramps = Path.Combine(dir, "background_ramps_bidir.json");
        string transitions = Path.Combine(dir, "remap_transition_table.json");
        if (!File.Exists(master) || !File.Exists(ramps) || !File.Exists(transitions))
        {
            Console.Error.WriteLine(
                "warning: background palette data missing next to the index — running "
                + "without background selection.");
            return null;
        }
        var library = BrawlerSim.Backgrounds.BackgroundLibrary.LoadFile(index);
        if (library.Refused.Count > 0)
        {
            Console.Error.WriteLine(
                $"warning: background index refused {library.Refused.Count} entries "
                + $"(license/attribution assertions), e.g. {library.Refused[0]}");
        }
        var palette = BrawlerSim.Backgrounds.BackgroundPalette.LoadFiles(master, ramps, transitions);
        string tuning = Path.Combine(Path.GetDirectoryName(dir)!, "background_selection.json");
        var config = File.Exists(tuning)
            ? BrawlerSim.Backgrounds.BackgroundSelectionConfig.LoadFile(tuning)
            : BrawlerSim.Backgrounds.BackgroundSelectionConfig.Default;
        // Palette harmony follows the tile theme when that library is available.
        string? tiles = FindUpward(Path.Combine("godot", "assets", "tiles_v2_slices.json"));
        var themeLibrary = tiles is not null && File.Exists(tiles)
            ? BrawlerSim.Sprites.StageThemeLibrary.LoadFile(tiles)
            : null;
        return new BrawlerSim.Backgrounds.BackgroundSelector(
            library, palette, config, themeLibrary);
    }

    private static string? FindUpward(string relative)
    {
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        return null;
    }

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

    private static IInputSource[] AiSources(ulong seed, AgentConfig agent, int players = 2) =>
        agent.CreateSources(seed, players);

    /// <summary>The evaluation MatchConfig shared by evolve/evaluate/noise:
    /// --max-seconds and --max-stun (default uncapped). The max-seconds default
    /// follows MatchConfig.Default (300 s) since 2026-09-10 (designer-directed,
    /// scaled-time experiment finding: the old CLI-only 60 s cap was itself a major
    /// stage-size homogenizer — long-pacing maps ate the overtime cliff regardless
    /// of fitness version). Old runs resume under their recorded maxMatchSeconds.</summary>
    private static MatchConfig BuildMatchConfig(Dictionary<string, string> opts) =>
        MatchConfig.Default with
        {
            MaxMatchSeconds = GetFloat(opts, "max-seconds", MatchConfig.Default.MaxMatchSeconds),
            MaxStunSeconds = GetFloat(opts, "max-stun", float.PositiveInfinity),
        };

    /// <summary>--collision-scalar when present, else null = the registry default.</summary>
    private static float? CollisionScalar(Dictionary<string, string> opts) =>
        opts.ContainsKey("collision-scalar") ? GetFloat(opts, "collision-scalar", 0f) : null;

    /// <summary>The fitness resolution shared by evaluate/noise: --fitness (absent =
    /// auto by player count), --target-seconds/--max-seconds, --collision-scalar.</summary>
    private static IFitnessFunction ResolveFitness(Dictionary<string, string> opts, int players) =>
        FitnessRegistry.Create(
            opts.GetValueOrDefault("fitness"),
            GetFloat(opts, "target-seconds", 45f),
            GetFloat(opts, "max-seconds", MatchConfig.Default.MaxMatchSeconds),
            CollisionScalar(opts),
            players);

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
