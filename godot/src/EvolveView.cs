using Godot;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BrawlerSim.Evolution;
using BrawlerSim.Genome;
using BrawlerSim.Params;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// In-app evolution dashboard: configure a run, execute the EvolutionEngine on a
/// background thread (checkpointing every generation exactly like the CLI), watch the
/// fitness curves live, and preview/save any chart point.
/// UI rework (2026-09-10, designer): narrow scrollable config column (NAME / NUMBER OF
/// PLAYERS / BUTTON ASSIGNMENT up front, everything else under ADVANCED OPTIONS),
/// icon transport buttons (start/pause/reset), the match preview inline in the column
/// with an overlaid save button, and a generation progress bar under the chart.
/// Automation: BRAWLER_AUTOEVOLVE="name=x;pop=24;gens=20;seed=9" starts on load;
/// with BRAWLER_SHOT set it captures the finished dashboard and quits.
/// MAP-Elites (2026-09-10, map-elites-descriptor-spec): the ALGORITHM option runs
/// MapElitesEngine instead (POPULATION = batch size, GENERATIONS = batches), and the
/// right column is a TabContainer — RUN = the existing dashboard, HYPERSPACE = the
/// archive cube. GA runs feed the cube too via a view-only shadow archive
/// (accumulated best-per-cell over the whole run; designer 2026-09-10). Automation
/// tokens: mode=mapelites, tab=hyperspace, pilot=N (pilot sample override).
/// </summary>
public partial class EvolveView : Control
{
    private SpinBox _seed = null!;
    private SpinBox _population = null!;
    private SpinBox _generations = null!;
    private SpinBox _rounds = null!;
    private HSlider _mutation = null!;
    private HSlider _dropout = null!;
    private LineEdit _runName = null!;
    private Button _start = null!;
    private Button _pause = null!;
    private Button _reset = null!;
    private Label _status = null!;
    private ProgressBar _progress = null!;
    private FitnessChart _chart = null!;

    // Composition control + advanced ranges (2026-07-14,
    // docs/features/evolve-composition-and-ranges.md)
    private OptionButton _compositionMode = null!;
    private OptionButton _numPlayers = null!; // 2026-08-12 four-player
    private VBoxContainer _perButtonList = null!;
    private readonly OptionButton[] _buttonSlots = new OptionButton[BrawlerSim.Sim.InputFrame.ActionCount];
    private Button _advancedToggle = null!;
    private VBoxContainer _advancedBox = null!;
    private Button _rangesToggle = null!;
    private ScrollContainer _rangesPanel = null!;
    private readonly System.Collections.Generic.List<RangeRow> _rangeRows = new();

    private sealed class RangeRow
    {
        public required string Schema;
        public required ParamSpec Stock;
        public required SpinBox Min;
        public required SpinBox Max;
        public required Label Warning;
    }

    private CancellationTokenSource? _cancel;
    private string _runDir = "";
    private ulong _startTimeMs;

    // MAP-Elites (2026-09-10): algorithm choice + the Hyperspace tab.
    private OptionButton _mode = null!;
    private SpinBox _initRandom = null!;
    private Label _populationHeading = null!;
    private Label _generationsHeading = null!;
    private TabContainer _tabs = null!;
    private HyperspaceView _hyperspace = null!;
    private int _pilotSamples = BrawlerSim.Evolution.DescriptorBins.DefaultPilotSamples;
    private readonly System.Collections.Concurrent.ConcurrentQueue<HyperspaceSnapshot> _pendingSnapshots = new();

    // The RUNNING run's identity — selection labels/provenance must describe the run
    // that produced the data on screen, not the dropdown's current value.
    private bool _runIsMapElites;
    private int _runBatchSize = 1;

    // Best chart point seen this run (per-batch archive best is monotone for
    // MAP-Elites) — the end-of-run auto-select target for MAP-Elites runs, where the
    // final batch's local best is usually NOT the run's best elite.
    private float _uiBestFitness = float.MinValue;
    private int _uiBestGen = -1;
    private int _uiBestIndex;

    // Evolution Explorer (2026-07-27, designer): per-game chart points feed a live
    // match preview + the save-to-favorites button. Generations cross from the
    // engine thread through a queue (GameGenome is not a Variant, so no CallDeferred
    // args); genomes are immutable and survivors are shared refs across generations,
    // so retaining them is cheap.
    private readonly System.Collections.Concurrent.ConcurrentQueue<
        (GenerationStats Stats, float[] Scores, GameGenome[] Genomes)> _pendingGenerations = new();
    private int _lastBestIndex;
    private bool _autoFavorite;
    private Label _previewInfo = null!;
    private Button _addToGames = null!;
    private Label _savedNote = null!;
    private MatchPreview _preview = null!;
    private (string Name, string Origin, float Score, GameGenome Genome)? _selection;

    public override void _Ready()
    {
        Theme = UiTheme.Buttons; // app-wide button styling (2026-08-17)
        BuildUi();
        string auto = AutomationEnv.AutoEvolve;
        if (auto.Length > 0)
        {
            ApplyAutoConfig(auto);
            StartRun();
        }
    }

    public override void _ExitTree()
    {
        _cancel?.Cancel();
    }

    private void StartRun()
    {
        int steps = (int)_generations.Value;
        _runDir = System.IO.Path.Combine(AppPaths.RunsRoot(), RunName());
        _chart.Clear();
        _hyperspace.Clear();
        ClearSelection();
        SetRunning(true);
        _progress.MaxValue = steps;
        _progress.Value = 0;
        _status.Text = $"RUNNING → {_runDir}";
        _startTimeMs = Time.GetTicksMsec();
        _runIsMapElites = _mode.Selected == 1;
        _runBatchSize = (int)_population.Value;
        _uiBestFitness = float.MinValue;
        _uiBestGen = -1;
        _cancel = new CancellationTokenSource();

        if (_runIsMapElites)
        {
            StartMapElitesRun(steps, _cancel.Token);
        }
        else
        {
            StartGaRun(steps, _cancel.Token);
        }
    }

    private void StartGaRun(int generations, CancellationToken token)
    {
        var config = new EvolutionConfig
        {
            Seed = (ulong)_seed.Value,
            PopulationSize = (int)_population.Value,
            RoundsPerIndividual = (int)_rounds.Value,
            MutationRate = (float)_mutation.Value,
            DropoutRate = (float)_dropout.Value,
            Generation = BuildGenerationConfig(),
        };
        string runDir = _runDir;
        string runName = RunName();
        int pilotSamples = _pilotSamples;

        Task.Run(() =>
        {
            try
            {
                RunGaWorker(config, generations, runDir, runName, pilotSamples, token);
            }
            catch (System.Exception e)
            {
                CallDeferred(nameof(OnRunFailed), e.Message);
            }
        }, token);
    }

    private void RunGaWorker(EvolutionConfig config, int generations, string runDir,
        string runName, int pilotSamples, CancellationToken token)
    {
        // Shadow archive (designer 2026-09-10): the GA run gets MAP-Elites binning
        // of everything it produces — ACCUMULATED best-per-cell over the whole run.
        // View-only: it consumes no engine RNG and alters nothing in the run.
        CallDeferred(nameof(SetStatus), "MEASURING DESCRIPTOR SPACE (PILOT)…");
        var shadow = new MapElitesArchive(
            LoadOrCreateBins(config.Generation, config.Seed, runDir, pilotSamples));
        CallDeferred(nameof(SetStatus), $"RUNNING → {runDir}");
        var engine = new EvolutionEngine(config);
        var history = new System.Collections.Generic.List<GenerationStats>();
        float bestSoFar = float.MinValue;
        while (engine.GenerationsCompleted < generations && !token.IsCancellationRequested)
        {
            // Snapshot the population BEFORE Step: Step evaluates exactly these
            // genomes, then replaces the bottom-dropout slots in place with fresh
            // UNEVALUATED children — pairing post-Step Population with LastFitness
            // would credit child genomes with scores they never earned (found in
            // the 2026-09-10 review; the chart had the same latent mismatch).
            GameGenome[] evaluated = engine.Population.ToArray();
            GenerationStats stats = engine.Step();
            float[] scores = engine.LastFitness.ToArray(); // the scores of `evaluated`
            history.Add(stats);
            if (stats.TopFitness > bestSoFar)
            {
                bestSoFar = stats.TopFitness;
                (_, var trace) = engine.ReplayEvaluation(stats.BestIndex, stats.Generation);
                RunStore.SaveBest(runDir, evaluated[stats.BestIndex], stats, trace);
            }
            RunStore.SaveCheckpoint(runDir, engine, config, history);
            _pendingGenerations.Enqueue((stats, scores, evaluated));
            for (int i = 0; i < evaluated.Length; i++)
            {
                shadow.Offer(new BrawlerSim.Evolution.ArchiveEntry(
                    evaluated[i], scores[i], BrawlerSim.Evolution.Descriptors.Compute(evaluated[i]),
                    stats.Generation * evaluated.Length + i), out _);
            }
            // The drain keeps only the newest snapshot, so building one is wasted
            // work unless the UI consumed the last — except at the end of the run.
            bool last = engine.GenerationsCompleted >= generations;
            if (_pendingSnapshots.IsEmpty || last)
            {
                _pendingSnapshots.Enqueue(BuildGaSnapshot(
                    shadow, config.Generation.CharacterCount, runName, evaluated.Length));
            }
            CallDeferred(nameof(DrainGenerations));
        }
        CallDeferred(nameof(OnRunFinished), engine.GenerationsCompleted, token.IsCancellationRequested);
    }

    private void SetStatus(string text) => _status.Text = text;

    /// <summary>A faulted worker (corrupt bins cache, disk error, …) must not leave
    /// the screen stuck on RUNNING with START disabled.</summary>
    private void OnRunFailed(string message)
    {
        DrainGenerations();
        SetRunning(false);
        _status.Text = $"RUN FAILED — {message}";
    }

    /// <summary>MAP-Elites run (2026-09-10): POPULATION = batch size, GENERATIONS =
    /// batches. Pilot bins are computed once (cached in the run dir), frozen, and
    /// stamped into every checkpoint by MapElitesStore.</summary>
    private void StartMapElitesRun(int batches, CancellationToken token)
    {
        // Everything the worker needs, captured on the main thread.
        GenerationConfig generation = BuildGenerationConfig();
        ulong seed = (ulong)_seed.Value;
        int batchSize = (int)_population.Value;
        int initialRandom = (int)_initRandom.Value;
        int rounds = (int)_rounds.Value;
        float mutation = (float)_mutation.Value;
        string runDir = _runDir;
        string runName = RunName();
        int pilotSamples = _pilotSamples;

        Task.Run(() =>
        {
            try
            {
                CallDeferred(nameof(SetStatus), "MEASURING DESCRIPTOR SPACE (PILOT)…");
                var config = new MapElitesConfig
                {
                    Seed = seed,
                    BatchSize = batchSize,
                    InitialRandomCandidates = initialRandom,
                    RoundsPerIndividual = rounds,
                    MutationRate = mutation,
                    Generation = generation,
                    Bins = LoadOrCreateBins(generation, seed, runDir, pilotSamples),
                };
                CallDeferred(nameof(SetStatus), $"RUNNING → {runDir}");
                RunMapElitesWorker(config, batches, runDir, runName, token);
            }
            catch (System.Exception e)
            {
                CallDeferred(nameof(OnRunFailed), e.Message);
            }
        }, token);
    }

    private void RunMapElitesWorker(MapElitesConfig config, int batches, string runDir,
        string runName, CancellationToken token)
    {
        var engine = new MapElitesEngine(config);
        var history = new System.Collections.Generic.List<MapElitesBatchStats>();
        float bestSoFar = float.MinValue;
        while (engine.BatchesCompleted < batches && !token.IsCancellationRequested)
        {
            MapElitesBatchStats stats = engine.Step();
            history.Add(stats);
            if (stats.BestFitness > bestSoFar && engine.Archive.Best is { } best)
            {
                bestSoFar = stats.BestFitness;
                (_, var trace) = engine.ReplayEvaluation(best);
                MapElitesStore.SaveBest(runDir, best, trace);
            }
            MapElitesStore.SaveCheckpoint(runDir, engine, config, history);
            // The chart plots the batch: top = archive best, average = batch mean,
            // best index = the batch's own best (feeds the end-of-run auto-select).
            float[] scores = engine.LastBatchFitness.ToArray();
            int batchBest = 0;
            for (int i = 1; i < scores.Length; i++)
            {
                if (scores[i] >= scores[batchBest])
                {
                    batchBest = i;
                }
            }
            _pendingGenerations.Enqueue((
                new GenerationStats(stats.Batch, stats.BestFitness,
                    scores.Length > 0 ? scores.Average() : 0f, 0f, batchBest),
                scores, engine.LastBatch.ToArray()));
            bool last = engine.BatchesCompleted >= batches;
            if (_pendingSnapshots.IsEmpty || last)
            {
                _pendingSnapshots.Enqueue(BuildMapElitesSnapshot(
                    engine, runName, config.Generation.CharacterCount));
            }
            CallDeferred(nameof(DrainGenerations));
        }
        CallDeferred(nameof(OnRunFinished), engine.BatchesCompleted, token.IsCancellationRequested);
    }

    /// <summary>Pilot bin edges for the run dir: cached in descriptor-bins.json so a
    /// second session over the same run bins identically (frozen-edges rule). The
    /// cache only counts when it matches the CURRENT configuration and pilot — a
    /// reused run name at a different player count/composition/seed must re-pilot,
    /// never silently bin with stale edges (pilots are per-configuration). A corrupt
    /// cache also just re-pilots.</summary>
    private static BrawlerSim.Evolution.DescriptorBins LoadOrCreateBins(
        GenerationConfig generation, ulong seed, string runDir, int samples)
    {
        string path = System.IO.Path.Combine(runDir, MapElitesStore.BinsFileName);
        ulong pilotSeed = BrawlerSim.Evolution.DescriptorBins.DefaultPilotSeed(seed);
        string configKey = BrawlerSim.Evolution.DescriptorBins.ConfigKeyFor(generation);
        if (System.IO.File.Exists(path))
        {
            try
            {
                var cached = BrawlerSim.Evolution.DescriptorBins.Load(path);
                if (cached.ConfigKey == configKey && cached.PilotSeed == pilotSeed
                    && cached.PilotSamples == samples)
                {
                    return cached;
                }
                GD.Print($"descriptor bins cache is for another configuration — re-piloting ({path})");
            }
            catch (System.Exception e)
            {
                GD.Print($"descriptor bins cache unreadable — re-piloting ({e.Message})");
            }
        }
        var bins = BrawlerSim.Evolution.DescriptorBins.FromPilot(generation, pilotSeed, samples);
        System.IO.Directory.CreateDirectory(runDir);
        bins.Save(path);
        return bins;
    }

    private static HyperspaceSnapshot BuildGaSnapshot(
        MapElitesArchive shadow, int players, string runName, int populationSize)
    {
        var entries = new HyperspaceEntry[shadow.Count];
        int i = 0;
        foreach (var kv in shadow.Cells)
        {
            int gen = kv.Value.Candidate / populationSize;
            int index = kv.Value.Candidate % populationSize;
            entries[i++] = new HyperspaceEntry(
                kv.Value.Descriptor, kv.Value.Fitness, kv.Value.Genome,
                $"{runName}-g{gen}-game{index}",
                $"evolve-explorer:{runName} gen {gen} game {index} fitness {kv.Value.Fitness:F1}",
                PreviewSeed: (ulong)(gen * 1000 + index + 1));
        }
        return new HyperspaceSnapshot(shadow.Bins, players, entries, ArchiveStatusLine(shadow, "SHADOW OF GA RUN"));
    }

    private static HyperspaceSnapshot BuildMapElitesSnapshot(
        MapElitesEngine engine, string runName, int players)
    {
        var entries = new HyperspaceEntry[engine.Archive.Count];
        int i = 0;
        foreach (var kv in engine.Archive.Cells)
        {
            entries[i++] = new HyperspaceEntry(
                kv.Value.Descriptor, kv.Value.Fitness, kv.Value.Genome,
                $"{runName}-cell{kv.Key}",
                $"mapelites:{runName} cell {kv.Key} cand {kv.Value.Candidate} fitness {kv.Value.Fitness:F1}",
                PreviewSeed: (ulong)(kv.Value.Candidate + 1));
        }
        return new HyperspaceSnapshot(engine.Archive.Bins, players, entries,
            ArchiveStatusLine(engine.Archive, "MAP-ELITES ARCHIVE"));
    }

    private static string ArchiveStatusLine(MapElitesArchive archive, string kind) =>
        $"{kind} — CELLS {archive.Count}/{BrawlerSim.Evolution.DescriptorBins.CellCount} " +
        $"({archive.Coverage:P1}) · QD {archive.QdScore:F0} · BEST {archive.Best?.Fitness ?? 0f:F1}" +
        (archive.OutOfPilotRangeCount > 0 ? $" · OUT-OF-PILOT {archive.OutOfPilotRangeCount}" : "");

    private void DrainGenerations()
    {
        while (_pendingGenerations.TryDequeue(out var gen))
        {
            _chart.AddGeneration(gen.Stats.TopFitness, gen.Stats.AverageFitness, gen.Scores, gen.Genomes);
            _lastBestIndex = gen.Stats.BestIndex;
            if (gen.Stats.TopFitness > _uiBestFitness)
            {
                _uiBestFitness = gen.Stats.TopFitness;
                _uiBestGen = gen.Stats.Generation;
                _uiBestIndex = gen.Stats.BestIndex;
            }
            _progress.Value = gen.Stats.Generation;
            float elapsed = (Time.GetTicksMsec() - _startTimeMs) / 1000f;
            _status.Text = $"TOP {gen.Stats.TopFitness:F1} · AVG {gen.Stats.AverageFitness:F1} · {elapsed:F1}S";
        }
        // The Hyperspace tab re-renders per snapshot, never per insertion — only the
        // newest queued archive state matters.
        HyperspaceSnapshot? latest = null;
        while (_pendingSnapshots.TryDequeue(out HyperspaceSnapshot? snapshot))
        {
            latest = snapshot;
        }
        if (latest is not null)
        {
            _hyperspace.SetSnapshot(latest);
        }
    }

    private void OnRunFinished(int generations, bool cancelled)
    {
        DrainGenerations(); // anything still queued when the loop ended
        float elapsed = (Time.GetTicksMsec() - _startTimeMs) / 1000f;
        _status.Text = (cancelled
            ? $"PAUSED AFTER {generations} GENERATIONS (CHECKPOINT KEPT)"
            : $"DONE — {generations} GENERATIONS IN {elapsed:F1}S") + $" — {_runDir}";
        SetRunning(false);

        // Convenience: focus the best game so the preview is live the moment a run
        // ends (also what automation screenshots capture). GA: the final generation's
        // best (the run's champion by convention). MAP-Elites: the batch that set the
        // archive best — the final batch's local best is usually NOT the run's best.
        if (!cancelled && generations > 0)
        {
            if (_runIsMapElites && _uiBestGen >= 0)
            {
                _chart.Select(_uiBestGen, _uiBestIndex);
            }
            else
            {
                _chart.Select(generations - 1, _lastBestIndex);
            }
            if (_autoFavorite)
            {
                AddSelectionToGames();
            }
        }

        string shot = AutomationEnv.Shot;
        if (shot.Length > 0 && AutomationEnv.AutoEvolve.Length > 0)
        {
            _ = CaptureAndQuit(shot);
        }
    }

    /// <summary>START runs, PAUSE cancels (checkpoint kept), RESET only between runs.</summary>
    private void SetRunning(bool running)
    {
        _start.Disabled = running;
        _pause.Disabled = !running;
        _reset.Disabled = running;
    }

    /// <summary>RESET (designer 2026-09-10): clear the dashboard and configure a fresh
    /// run — next EVOLUTION number, new random seed. Touches no files: the previous
    /// run's directory keeps everything it wrote.</summary>
    private void ResetForNewRun()
    {
        _chart.Clear();
        _hyperspace.Clear();
        ClearSelection();
        _runName.Text = NextEvolutionName(_runName.Text);
        _seed.Value = RandomSeed();
        _progress.MaxValue = 1;
        _progress.Value = 0;
        _runDir = "";
        _status.Text = "CONFIGURE A RUN AND PRESS START";
    }

    /// <summary>Default run name: EVOLUTION X, X = first number past everything in the
    /// runs directory (and past the current field on RESET, so the number always
    /// increments even if the previous run never started).</summary>
    private static string NextEvolutionName(string? current = null)
    {
        int max = 0;
        foreach (string dir in System.IO.Directory.GetDirectories(AppPaths.RunsRoot()))
        {
            if (TryParseEvolutionNumber(System.IO.Path.GetFileName(dir), out int n) && n > max)
            {
                max = n;
            }
        }
        if (current is not null && TryParseEvolutionNumber(current.Trim(), out int c) && c > max)
        {
            max = c;
        }
        return $"EVOLUTION {max + 1}";
    }

    private static bool TryParseEvolutionNumber(string name, out int number)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            name, @"^EVOLUTION (\d+)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        number = 0;
        return match.Success && int.TryParse(match.Groups[1].Value, out number);
    }

    private static int RandomSeed() => System.Random.Shared.Next(1, 1_000_000);

    // ── Evolution Explorer: selection → preview → basket ──────────────────────────

    private void OnPointSelected(int gen, int index, float score, GameGenome genome)
    {
        string runName = RunName();
        if (_runIsMapElites)
        {
            // MAP-Elites provenance: the chart plots BATCHES, and the same genome
            // picked from the cube carries candidate-derived naming/seeding — match it
            // so the audit trail is one vocabulary regardless of which tab picked.
            int candidate = gen * _runBatchSize + index;
            SelectGame(
                $"{runName}-b{gen}-game{index}",
                $"mapelites:{runName} batch {gen} game {index} cand {candidate} fitness {score:F1}",
                score, genome,
                $"BATCH {gen} · GAME {index} · FITNESS {score:F1}",
                previewSeed: (ulong)(candidate + 1));
            return;
        }
        SelectGame(
            $"{runName}-g{gen}-game{index}",
            $"evolve-explorer:{runName} gen {gen} game {index} fitness {score:F1}",
            score, genome,
            $"GEN {gen} · GAME {index} · FITNESS {score:F1}",
            previewSeed: (ulong)(gen * 1000 + index + 1));
    }

    /// <summary>A Hyperspace cube pick lands in the same preview/save plumbing as a
    /// chart point (map-elites-descriptor-spec §8 selection contract).</summary>
    private void OnHyperspaceEntrySelected(HyperspaceEntry entry)
    {
        SelectGame(entry.Name, entry.Origin, entry.Fitness, entry.Genome,
            $"{entry.Name.ToUpperInvariant()} · FITNESS {entry.Fitness:F1}", entry.PreviewSeed);
    }

    private void SelectGame(string name, string origin, float score, GameGenome genome,
        string info, ulong previewSeed)
    {
        _selection = (name, origin, score, genome);
        _previewInfo.Text = info;
        _addToGames.Disabled = false;
        _savedNote.Visible = false;
        _preview.ShowGame(new GameRecord(name, origin, genome), firstSeed: previewSeed);
    }

    private string RunName() => _runName.Text.Trim().Length > 0 ? _runName.Text.Trim() : "unnamed";

    private void ClearSelection()
    {
        _selection = null;
        _preview.Stop();
        _previewInfo.Text = "";
        _addToGames.Disabled = true;
        _savedNote.Visible = false;
    }

    /// <summary>The preview's save button: saves the selected genome as a game.json
    /// in the favorites library and flashes the SAVED notification.</summary>
    private void AddSelectionToGames()
    {
        if (_selection is not { } sel)
        {
            return;
        }
        string baseName = Sanitize(sel.Name);
        var record = new GameRecord(baseName, sel.Origin, sel.Genome);
        string dir = AppPaths.FavoritesRoot();
        string path = System.IO.Path.Combine(dir, baseName + ".json");
        for (int n = 2; System.IO.File.Exists(path); n++)
        {
            path = System.IO.Path.Combine(dir, $"{baseName}-{n}.json");
        }
        GameGenomeJson.Save(record, path);
        _savedNote.Visible = true;
        GetTree().CreateTimer(2.5).Timeout += () => _savedNote.Visible = false;
    }

    private static string Sanitize(string name)
    {
        foreach (char c in System.IO.Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '-');
        }
        return name;
    }

    private Task CaptureAndQuit(string path) => Screenshot.CaptureAsync(this, path, quitWhenDone: true);

    /// <summary>Collects composition mode + advanced range rows into the run's
    /// GenerationConfig. PINNED with untouched rows = GenerationConfig.Default —
    /// the byte-identical legacy path.</summary>
    private GenerationConfig BuildGenerationConfig()
    {
        GenerationConfig generation = GenerationConfig.Default with
        {
            CharacterCount = _numPlayers.Selected + 2, // 2026-08-12 four-player
            SpriteSelector = SpriteBank.Selector, // 2026-08-22 sprite selection (cosmetic, RNG-free)
            StageThemeSelector = ThemeBank.Selector, // M4d stage tile themes (2026-09-01)
            BackgroundSelector = BackgroundBank.Selector, // backgrounds track (2026-09-02)
        };
        if (_compositionMode.Selected == 1)
        {
            generation = generation with { ButtonComposition = GenerationConfig.RandomComposition };
        }
        else if (_compositionMode.Selected == 3)
        {
            // PINNED + PROJECTILE (designer 2026-09-04).
            generation = generation with
            {
                ButtonComposition = new[]
                {
                    SlotSpec.Attack, SlotSpec.Attack, SlotSpec.Projectile,
                    SlotSpec.Shield, SlotSpec.Dash,
                },
            };
        }
        else if (_compositionMode.Selected == 2)
        {
            generation = generation with
            {
                ButtonComposition = _buttonSlots.Select(s => (SlotSpec)s.Selected).ToArray(),
            };
        }
        var overrides = new System.Collections.Generic.List<RangeOverride>();
        foreach (RangeRow row in _rangeRows)
        {
            float min = (float)row.Min.Value, max = (float)row.Max.Value;
            if (min != row.Stock.Min || max != row.Stock.Max)
            {
                overrides.Add(new RangeOverride(row.Schema, row.Stock.Key, System.MathF.Min(min, max), System.MathF.Max(min, max)));
            }
        }
        return overrides.Count > 0 ? generation.WithRangeOverrides(overrides) : generation;
    }

    private void ApplyAutoConfig(string spec)
    {
        foreach (string pair in spec.Split(';'))
        {
            string[] kv = pair.Split('=');
            if (kv.Length != 2) continue;
            switch (kv[0])
            {
                case "name": _runName.Text = kv[1]; break;
                case "pop": _population.Value = double.Parse(kv[1]); break;
                case "gens": _generations.Value = double.Parse(kv[1]); break;
                case "seed": _seed.Value = double.Parse(kv[1]); break;
                case "rounds": _rounds.Value = double.Parse(kv[1]); break;
                case "players": _numPlayers.Selected = int.Parse(kv[1]) - 2; break; // 2026-08-12
                case "composition": // pinned|random|perbutton|projectile (headless UI verification)
                    _compositionMode.Selected = kv[1] switch
                        { "random" => 1, "perbutton" => 2, "projectile" => 3, _ => 0 };
                    OnCompositionModeChanged(_compositionMode.Selected);
                    break;
                case "advanced": // any value: open advanced options + ranges for screenshots
                    SetAdvancedVisible(true);
                    ToggleRanges();
                    break;
                case "favorite": // =1: save the auto-selected best to favorites (automation)
                    _autoFavorite = kv[1] == "1";
                    break;
                case "mode": // ga|mapelites (2026-09-10)
                    _mode.Selected = kv[1] == "mapelites" ? 1 : 0;
                    OnModeChanged();
                    break;
                case "tab": // =hyperspace: open the archive cube (screenshots)
                    if (kv[1] == "hyperspace")
                    {
                        _tabs.CurrentTab = 1;
                    }
                    break;
                case "pilot": // pilot sample override so automation runs stay fast
                    _pilotSamples = int.Parse(kv[1]);
                    break;
                case "initrandom": // MAP-Elites initial random candidates
                    _initRandom.Value = double.Parse(kv[1]);
                    break;
                case "hslice": // hidden-axis slider position, 8 = ALL (screenshots)
                    _hyperspace.SetSliceForAutomation(int.Parse(kv[1]));
                    break;
            }
        }
    }

    private void BuildUi()
    {
        var root = new HBoxContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 16f, OffsetTop = 16f, OffsetRight = -16f, OffsetBottom = -16f,
        };
        root.AddThemeConstantOverride("separation", 16);
        AddChild(root);

        BuildConfigColumn(root);
        BuildChartColumn(root);
    }

    /// <summary>The config column (designer 2026-09-10): one narrow ScrollContainer —
    /// header, the three primary fields (labels above, small caps), transport icons,
    /// the match preview, then the ADVANCED OPTIONS dropdown.</summary>
    private void BuildConfigColumn(HBoxContainer root)
    {
        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer left, separation: 10);
        scroll.CustomMinimumSize = new Vector2(264f, 0f);
        root.AddChild(scroll);

        // Header: BACK top-left, EVOLVE centered (a spacer mirrors BACK's width so
        // the title centers on the column, not the leftover space).
        var header = new HBoxContainer();
        var back = new Button { Text = "BACK" };
        back.Pressed += () => GetTree().ChangeSceneToFile(Scenes.MainMenu);
        header.AddChild(back);
        var title = new Label
        {
            Text = "EVOLVE",
            HorizontalAlignment = HorizontalAlignment.Center,
            SizeFlagsHorizontal = SizeFlags.ExpandFill,
        };
        title.AddThemeFontSizeOverride("font_size", 26);
        header.AddChild(title);
        var spacer = new Control { CustomMinimumSize = new Vector2(64f, 0f) };
        header.AddChild(spacer);
        left.AddChild(header);

        _runName = new LineEdit { Text = NextEvolutionName(), PlaceholderText = "EVOLUTION 1" };
        left.AddChild(Field("NAME", _runName));

        // Four Player Support (2026-08-12): each game holds 2-4 characters; runs past
        // two players score under ffa-v1 automatically (run.json records both).
        _numPlayers = new OptionButton();
        _numPlayers.AddItem("2 PLAYERS", 0);
        _numPlayers.AddItem("3 PLAYERS", 1);
        _numPlayers.AddItem("4 PLAYERS", 2);
        _numPlayers.Selected = 0;
        left.AddChild(Field("NUMBER OF PLAYERS", _numPlayers));

        // Algorithm (2026-09-10, map-elites-descriptor-spec): the GA selects for
        // fitness alone; MAP-Elites keeps one elite per descriptor cell — the archive
        // renders in the HYPERSPACE tab. POPULATION/GENERATIONS re-read as
        // BATCH SIZE/BATCHES in MAP-Elites mode (headings update live).
        _mode = new OptionButton { FitToLongestItem = false, ClipText = true };
        _mode.AddThemeFontSizeOverride("font_size", 14);
        _mode.AddItem("GENETIC ALGORITHM", 0);
        _mode.AddItem("MAP-ELITES (HYPERSPACE)", 1);
        _mode.Selected = 0;
        _mode.ItemSelected += _ => OnModeChanged();
        left.AddChild(Field("ALGORITHM", _mode));

        // Composition (2026-07-14): PINNED = today's fixed attack/attack/shield/dash;
        // RANDOM = every button free; PER-BUTTON = pin some, free others.
        _compositionMode = new OptionButton { FitToLongestItem = false, ClipText = true };
        _compositionMode.AddThemeFontSizeOverride("font_size", 14); // longest label fits the column
        _compositionMode.AddItem("PINNED (ATK/ATK/SHLD/DASH)", 0);
        _compositionMode.AddItem("RANDOMIZED (TYPES EVOLVE)", 1);
        _compositionMode.AddItem("PER-BUTTON", 2);
        // Projectile pin as a first-class option (designer 2026-09-04): the standard
        // kit with a guaranteed bolt slot — previously only reachable via PER-BUTTON.
        // Appended so existing indices (and autoevolve tokens) stay stable.
        _compositionMode.AddItem("PINNED + PROJECTILE", 3);
        _compositionMode.Selected = 0;
        _compositionMode.ItemSelected += i => OnCompositionModeChanged((int)i);
        left.AddChild(Field("BUTTON ASSIGNMENT", _compositionMode));

        // PER-BUTTON slots stack vertically to fit the narrow column.
        _perButtonList = new VBoxContainer { Visible = false };
        _perButtonList.AddThemeConstantOverride("separation", 4);
        // Slot-order invariant (L pinned last) documented on ControlLabels.
        string[] buttonNames = ControlLabels.Keyboard;
        for (int b = 0; b < _buttonSlots.Length; b++)
        {
            var slot = new OptionButton { SizeFlagsHorizontal = SizeFlags.ExpandFill };
            // Item order mirrors SlotSpec numerically (BuildGenerationConfig casts).
            slot.AddItem("ATTACK", 0);
            slot.AddItem("SHIELD", 1);
            slot.AddItem("DASH", 2);
            slot.AddItem("PROJECTILE", 3); // 2026-07-14
            slot.AddItem("RANDOM", 4);
            // Seed from the pinned layout (attack/attack/shield/attack/dash).
            slot.Selected = b switch { 2 => 1, 4 => 2, _ => 0 };
            _buttonSlots[b] = slot;
            var row = new HBoxContainer();
            var name = new Label { Text = buttonNames[b], CustomMinimumSize = new Vector2(44f, 0f) };
            name.AddThemeFontSizeOverride("font_size", 12);
            row.AddChild(name);
            row.AddChild(slot);
            _perButtonList.AddChild(row);
        }
        left.AddChild(_perButtonList);

        // Transport: START / PAUSE (cancel, checkpoint kept) / RESET.
        var transport = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        transport.AddThemeConstantOverride("separation", 12);
        _start = IconButton(UiIcons.Play(), "START RUN");
        _start.Pressed += StartRun;
        transport.AddChild(_start);
        _pause = IconButton(UiIcons.Pause(), "PAUSE (KEEPS CHECKPOINT)");
        _pause.Disabled = true;
        _pause.Pressed += () => _cancel?.Cancel();
        transport.AddChild(_pause);
        _reset = IconButton(UiIcons.Reset(), "RESET (CONFIGURE A NEW RUN — PRIOR FILES KEPT)");
        _reset.Pressed += ResetForNewRun;
        transport.AddChild(_reset);
        left.AddChild(transport);

        BuildPreviewBlock(left);

        _advancedToggle = new Button { Text = "ADVANCED OPTIONS", ToggleMode = true };
        _advancedToggle.Toggled += on => _advancedBox.Visible = on;
        left.AddChild(_advancedToggle);

        _advancedBox = new VBoxContainer { Visible = false };
        _advancedBox.AddThemeConstantOverride("separation", 10);
        _seed = Spin(RandomSeed(), 1, 999_999);
        _advancedBox.AddChild(Field("SEED", _seed));
        _population = Spin(100, 4, 500);
        _advancedBox.AddChild(Field("POPULATION", _population, out _populationHeading));
        _generations = Spin(300, 1, 5000);
        _advancedBox.AddChild(Field("GENERATIONS", _generations, out _generationsHeading));
        _rounds = Spin(3, 1, 9);
        _advancedBox.AddChild(Field("ROUNDS / INDIVIDUAL", _rounds));
        _mutation = Slider(0.4f);
        _advancedBox.AddChild(Field("MUTATION RATE", _mutation));
        _dropout = Slider(0.5f);
        _advancedBox.AddChild(Field("DROPOUT RATE", _dropout));
        // MAP-Elites only: candidates generated fresh before elite variation starts.
        _initRandom = Spin(500, 10, 10_000);
        _advancedBox.AddChild(Field("INITIAL RANDOM (MAP-ELITES)", _initRandom));
        _rangesToggle = new Button { Text = "PARAMETER RANGES", ToggleMode = true };
        _rangesToggle.Pressed += ToggleRanges;
        _advancedBox.AddChild(_rangesToggle);
        left.AddChild(_advancedBox);
    }

    /// <summary>The match preview, inline in the config column: mini arena with the
    /// save button overlaid top-right and the SAVED notification overlaid at the
    /// bottom; the selection readout sits underneath.</summary>
    private void BuildPreviewBlock(VBoxContainer left)
    {
        left.AddChild(UiWidgets.Heading("MATCH PREVIEW", 12));

        var frame = new Control { CustomMinimumSize = new Vector2(248f, 140f) }; // 16:9
        var container = new SubViewportContainer { Stretch = true };
        frame.AddChild(container);
        container.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);
        var viewport = new SubViewport { RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        container.AddChild(viewport);
        _preview = new MatchPreview();
        viewport.AddChild(_preview);

        _addToGames = new Button { Icon = UiIcons.Save(18), TooltipText = "ADD TO GAMES", Disabled = true };
        frame.AddChild(_addToGames);
        _addToGames.SetAnchorsAndOffsetsPreset(LayoutPreset.TopRight, LayoutPresetMode.Minsize, 6);
        _addToGames.Pressed += AddSelectionToGames;

        _savedNote = new Label
        {
            Text = "SAVED MATCH TO STORAGE",
            Visible = false,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.5f, 0.9f, 0.6f),
        };
        _savedNote.AddThemeFontSizeOverride("font_size", 12);
        _savedNote.AddThemeStyleboxOverride("normal", UiWidgets.PanelStyle(
            new Color(0.05f, 0.05f, 0.08f, 0.85f), cornerRadius: 4, marginX: 8f, marginY: 4f));
        frame.AddChild(_savedNote);
        _savedNote.SetAnchorsAndOffsetsPreset(LayoutPreset.CenterBottom, LayoutPresetMode.Minsize, 8);

        left.AddChild(frame);

        _previewInfo = new Label { Text = "", AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _previewInfo.AddThemeFontSizeOverride("font_size", 12);
        left.AddChild(_previewInfo);
    }

    /// <summary>The right column is a TabContainer (map-elites-descriptor-spec §8):
    /// RUN = the existing dashboard column unchanged, HYPERSPACE = the archive cube.
    /// Both algorithms feed both tabs.</summary>
    private void BuildChartColumn(HBoxContainer root)
    {
        _tabs = new TabContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        root.AddChild(_tabs);

        var right = new VBoxContainer { Name = "RUN" };
        right.AddThemeConstantOverride("separation", 8);
        _tabs.AddChild(right);

        _hyperspace = new HyperspaceView { Name = "HYPERSPACE" };
        _hyperspace.EntrySelected += OnHyperspaceEntrySelected;
        _tabs.AddChild(_hyperspace);
        _chart = new FitnessChart { SizeFlagsVertical = SizeFlags.ExpandFill };
        _chart.PointSelected += OnPointSelected;
        right.AddChild(_chart);
        _rangesPanel = BuildRangesPanel();
        right.AddChild(_rangesPanel);
        // Generation progress as a bar (designer 2026-09-10); the label under it
        // carries fitness/run-state only. ClipText: the run-dir path is long.
        _progress = new ProgressBar
        {
            MinValue = 0, MaxValue = 1, Value = 0,
            ShowPercentage = false,
            CustomMinimumSize = new Vector2(0f, 12f),
        };
        // Green fill over a panel-dark trough so it reads as progress, not a scrollbar.
        _progress.AddThemeStyleboxOverride("background", UiWidgets.PanelStyle(UiPalette.PanelBg, cornerRadius: 3));
        _progress.AddThemeStyleboxOverride("fill", UiWidgets.PanelStyle(new Color(0.45f, 0.9f, 0.55f), cornerRadius: 3));
        right.AddChild(_progress);
        _status = new Label
        {
            Text = "CONFIGURE A RUN AND PRESS START",
            ClipText = true,
            TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis,
        };
        _status.AddThemeFontSizeOverride("font_size", 13);
        right.AddChild(_status);
    }

    private void OnCompositionModeChanged(int mode)
    {
        _perButtonList.Visible = mode == 2;
    }

    private void SetAdvancedVisible(bool show)
    {
        _advancedToggle.ButtonPressed = show;
        _advancedBox.Visible = show;
    }

    /// <summary>The parameter-ranges panel swaps with the chart (same slot on the
    /// right) so the range grid gets full height; the run keeps drawing to the chart
    /// underneath and reappears when the panel is toggled off.</summary>
    private void ToggleRanges()
    {
        bool show = !_rangesPanel.Visible;
        _rangesPanel.Visible = show;
        _chart.Visible = !show;
        _rangesToggle.ButtonPressed = show;
    }

    private ScrollContainer BuildRangesPanel()
    {
        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer list, separation: 2);
        scroll.Visible = false;
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;

        var heading = new Label { Text = "GENERATION RANGES — EDITS APPLY TO NEW RUNS AND ARE RECORDED IN RUN.JSON" };
        heading.AddThemeFontSizeOverride("font_size", 14);
        list.AddChild(heading);
        var note = new Label
        {
            Text = "CLAMP A PARAMETER BY SETTING MIN = MAX. AMBER = OUTSIDE THE TESTED DOMAIN.",
            Modulate = new Color(0.7f, 0.7f, 0.75f),
        };
        note.AddThemeFontSizeOverride("font_size", 12);
        list.AddChild(note);

        var reset = new Button { Text = "RESET ALL TO DEFAULTS" };
        reset.Pressed += () =>
        {
            foreach (RangeRow row in _rangeRows)
            {
                row.Min.SetValueNoSignal(row.Stock.Min);
                row.Max.SetValueNoSignal(row.Stock.Max);
                UpdateRowWarning(row);
            }
        };
        list.AddChild(reset);

        foreach ((string name, ParamSchema schema) in new[]
        {
            ("character", DefaultSchemas.Character),
            ("move", DefaultSchemas.Move),
            ("shield", DefaultSchemas.Shield),
            ("dash", DefaultSchemas.Dash),
            ("projectile", DefaultSchemas.Projectile), // 2026-07-22 (designer)
            ("stage", DefaultSchemas.Stage), // 2026-07-21 Map Size
        })
        {
            var section = new Label { Text = name.ToUpperInvariant() };
            section.AddThemeFontSizeOverride("font_size", 16);
            list.AddChild(section);
            foreach (ParamSpec spec in schema.Specs)
            {
                list.AddChild(BuildRangeRow(name, spec));
            }
        }
        return scroll;
    }

    private Control BuildRangeRow(string schema, ParamSpec spec)
    {
        var row = new HBoxContainer();
        var label = new Label { Text = spec.Key, CustomMinimumSize = new Vector2(230f, 0f) };
        label.AddThemeFontSizeOverride("font_size", 13);
        row.AddChild(label);
        SpinBox min = RangeSpin(spec.Min);
        SpinBox max = RangeSpin(spec.Max);
        row.AddChild(min);
        row.AddChild(max);
        var warning = new Label
        {
            Text = "",
            CustomMinimumSize = new Vector2(200f, 0f),
            Modulate = new Color(1f, 0.75f, 0.25f),
        };
        warning.AddThemeFontSizeOverride("font_size", 12);
        row.AddChild(warning);

        var rangeRow = new RangeRow { Schema = schema, Stock = spec, Min = min, Max = max, Warning = warning };
        _rangeRows.Add(rangeRow);
        min.ValueChanged += _ => UpdateRowWarning(rangeRow);
        max.ValueChanged += _ => UpdateRowWarning(rangeRow);
        return row;
    }

    private static void UpdateRowWarning(RangeRow row)
    {
        float min = (float)row.Min.Value, max = (float)row.Max.Value;
        bool edited = min != row.Stock.Min || max != row.Stock.Max;
        bool outside = min < row.Stock.EffectiveValidMin || max > row.Stock.EffectiveValidMax;
        row.Warning.Text = outside ? "OUTSIDE TESTED DOMAIN" : edited ? (min == max ? "CLAMPED" : "EDITED") : "";
        row.Warning.Modulate = outside ? new Color(1f, 0.75f, 0.25f) : new Color(0.6f, 0.75f, 0.6f);
    }

    private static SpinBox RangeSpin(float value) => new()
    {
        MinValue = -10_000, MaxValue = 10_000, Step = 0.01, Value = value,
        AllowGreater = true, AllowLesser = true,
        CustomMinimumSize = new Vector2(110f, 0f),
    };

    private static Button IconButton(Texture2D icon, string tooltip) => new()
    {
        Icon = icon,
        TooltipText = tooltip,
        IconAlignment = HorizontalAlignment.Center,
        CustomMinimumSize = new Vector2(64f, 40f),
    };

    private static SpinBox Spin(double value, double min, double max) =>
        new() { MinValue = min, MaxValue = max, Value = value };

    private static HSlider Slider(float value) =>
        new() { MinValue = 0, MaxValue = 1, Step = 0.05, Value = value, CustomMinimumSize = new Vector2(0f, 20f) };

    /// <summary>Field ritual (designer 2026-09-10): small caps label ABOVE the
    /// control so the column collapses as narrow as possible.</summary>
    private static Control Field(string text, Control control)
        => Field(text, control, out _);

    private static Control Field(string text, Control control, out Label heading)
    {
        var box = new VBoxContainer();
        box.AddThemeConstantOverride("separation", 2);
        heading = UiWidgets.Heading(text, 12);
        box.AddChild(heading);
        control.SizeFlagsHorizontal = SizeFlags.ExpandFill;
        box.AddChild(control);
        return box;
    }

    /// <summary>ALGORITHM change: the two shared spins mean different things per mode,
    /// so their headings track the selection.</summary>
    private void OnModeChanged()
    {
        bool mapElites = _mode.Selected == 1;
        _populationHeading.Text = mapElites ? "BATCH SIZE" : "POPULATION";
        _generationsHeading.Text = mapElites ? "BATCHES" : "GENERATIONS";
    }
}
