using Godot;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BrawlerSim.Agents;
using BrawlerSim.Determinism;
using BrawlerSim.Evolution;
using BrawlerSim.Fitness;
using BrawlerSim.Genome;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>
/// QUALITY EXPLORATION (2026-09-11, designer — the requirement the Hyperspace tab
/// missed): a dedicated screen under the main menu's EVOLVE flyout that renders the
/// descriptor hypercube across ALL saved games the app has ever generated — the
/// favorites library, the demo games, every run's best.json, and loose game.jsons in
/// the runs root. Each game's fitness is (re-)evaluated live on a background thread
/// with content-derived seeds (deterministic: same library ⇒ same colors), binned by
/// a per-player-count pilot cached beside the runs root, and plotted through the
/// shared HyperspaceView. The 3D interaction set is the designer's ongoing rework;
/// this screen supplies the surface and the data.
/// Automation: BRAWLER_SCENE=quality_exploration; with BRAWLER_SHOT set it captures
/// the populated cube and quits.
/// </summary>
public partial class QualityExplorationView : Control
{
    /// <summary>Stable pilot stream for this screen ("QUAL") — not tied to any run,
    /// so every session bins the library identically per player count.</summary>
    private static readonly ulong PilotSeed = DescriptorBins.DefaultPilotSeed(0x5155414CUL);

    /// <summary>Bo3 upper-median per game, seeds derived from the game's own content
    /// bytes — adding or renaming OTHER games never changes a game's score.</summary>
    private const int EvaluationRounds = 3;

    private HyperspaceView _hyperspace = null!;
    private OptionButton _players = null!;
    private Button _rescan = null!;
    private Label _scanStatus = null!;
    private CancellationTokenSource? _cancel;
    private int _scanGeneration; // stale background results are dropped

    public override void _Ready()
    {
        Theme = UiTheme.Buttons;
        BuildUi();
        StartScan();
    }

    public override void _ExitTree()
    {
        _cancel?.Cancel();
    }

    private void BuildUi()
    {
        var column = new VBoxContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 16f, OffsetTop = 16f, OffsetRight = -16f, OffsetBottom = -16f,
        };
        column.AddThemeConstantOverride("separation", 8);
        AddChild(column);

        var header = new HBoxContainer();
        header.AddThemeConstantOverride("separation", 12);
        column.AddChild(header);

        var back = new Button { Text = "BACK" };
        back.Pressed += () => GetTree().ChangeSceneToFile(Scenes.MainMenu);
        header.AddChild(back);

        var title = new Label { Text = "QUALITY EXPLORATION" };
        title.AddThemeFontSizeOverride("font_size", 26);
        header.AddChild(title);

        var spacer = new Control { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        header.AddChild(spacer);

        header.AddChild(UiWidgets.Heading("PLAYERS", 12));
        _players = new OptionButton();
        _players.AddItem("2 PLAYERS", 0);
        _players.AddItem("3 PLAYERS", 1);
        _players.AddItem("4 PLAYERS", 2);
        _players.Selected = 0;
        _players.ItemSelected += _ => StartScan();
        header.AddChild(_players);

        _rescan = new Button { Text = "RESCAN", TooltipText = "RE-SCAN THE SAVED-GAME LIBRARY" };
        _rescan.Pressed += StartScan;
        header.AddChild(_rescan);

        _scanStatus = UiWidgets.MakeLabel("", 12);
        column.AddChild(_scanStatus);

        _hyperspace = new HyperspaceView
        {
            ShowLibraryLandmarks = false, // the library IS the plotted content here
            SizeFlagsVertical = SizeFlags.ExpandFill,
        };
        column.AddChild(_hyperspace);
    }

    // ── Library scan + evaluation (background) ─────────────────────────────────────

    private void StartScan()
    {
        _cancel?.Cancel();
        _cancel = new CancellationTokenSource();
        CancellationToken token = _cancel.Token;
        int scan = ++_scanGeneration;
        int players = _players.Selected + 2;
        _rescan.Disabled = true;
        _scanStatus.Text = "SCANNING SAVED GAMES…";
        _hyperspace.Clear();

        Task.Run(() =>
        {
            try
            {
                (HyperspaceSnapshot snapshot, string status) = BuildLibrarySnapshot(players, token);
                if (!token.IsCancellationRequested)
                {
                    CallDeferred(nameof(ApplyScan), scan, status);
                    _pendingSnapshot = snapshot;
                    CallDeferred(nameof(ApplySnapshot), scan);
                }
            }
            catch (System.Exception e)
            {
                CallDeferred(nameof(ApplyScanFailure), scan, e.Message);
            }
        }, token);
    }

    // Snapshot crosses threads via a field (GameGenome is not a Variant).
    private HyperspaceSnapshot? _pendingSnapshot;

    private void ApplyScan(int scan, string status)
    {
        if (scan != _scanGeneration)
        {
            return;
        }
        _scanStatus.Text = status;
        _rescan.Disabled = false;
    }

    private void ApplySnapshot(int scan)
    {
        if (scan != _scanGeneration || _pendingSnapshot is not { } snapshot)
        {
            return;
        }
        _pendingSnapshot = null;
        _hyperspace.SetSnapshot(snapshot);

        // Automation: capture the populated cube (BRAWLER_SCENE=quality_exploration).
        string shot = AutomationEnv.Shot;
        if (shot.Length > 0 && AutomationEnv.Scene == "quality_exploration")
        {
            _ = Screenshot.CaptureAsync(this, shot, quitWhenDone: true);
        }
    }

    private void ApplyScanFailure(int scan, string message)
    {
        if (scan != _scanGeneration)
        {
            return;
        }
        _scanStatus.Text = $"SCAN FAILED — {message}";
        _rescan.Disabled = false;
    }

    private (HyperspaceSnapshot Snapshot, string Status) BuildLibrarySnapshot(
        int players, CancellationToken token)
    {
        System.Collections.Generic.List<(string Path, GameRecord Record)> library = ScanLibrary();
        var matching = library.Where(g => g.Record.Genome.Characters.Count == players).ToList();
        int otherCounts = library.Count - matching.Count;

        // Frozen per-player-count bins, cached beside the runs root so every session
        // over this library bins identically.
        GenerationConfig pilotConfig = GenerationConfig.Default with { CharacterCount = players };
        DescriptorBins bins = DescriptorBinsCache.LoadOrCreate(
            pilotConfig, PilotSeed,
            System.IO.Path.Combine(AppPaths.RunsRoot(), "quality-exploration",
                $"bins-{players}p-{DescriptorBins.DefaultFileName}"),
            DescriptorBins.DefaultPilotSamples);

        // Evaluate every matching game — Bo3 median under the standard evaluation
        // setup, fitness auto-selected by player count, seeds derived from the game's
        // own bytes. ~1,000 matches/s: even hundreds of games stay sub-second.
        IFitnessFunction fitness = FitnessRegistry.Create(
            null, 45f, MatchConfig.Default.MaxMatchSeconds, null, players);
        var entries = new HyperspaceEntry[matching.Count];
        var archive = new MapElitesArchive(bins); // stats only (coverage/best readout)
        for (int i = 0; i < matching.Count; i++)
        {
            token.ThrowIfCancellationRequested();
            (string path, GameRecord record) = matching[i];
            ulong contentSeed = ContentSeed(record);
            var rounds = new float[EvaluationRounds];
            for (int round = 0; round < rounds.Length; round++)
            {
                ulong matchSeed = SeedMix.MatchSeed(contentSeed, 0, 0, round);
                IInputSource[] sources = AgentConfig.Default.CreateSources(
                    matchSeed, record.Genome.Characters.Count);
                rounds[round] = fitness.Evaluate(
                    MatchRunner.Run(record.Genome, sources, MatchConfig.Default));
            }
            System.Array.Sort(rounds);
            float score = rounds[rounds.Length / 2]; // upper median, evolution parity
            float[] descriptor = Descriptors.Compute(record.Genome);
            entries[i] = new HyperspaceEntry(
                descriptor, score, record.Genome,
                record.Name,
                record.Origin is { Length: > 0 } origin
                    ? origin
                    : $"library:{System.IO.Path.GetFileName(path)}",
                PreviewSeed: contentSeed);
            archive.Offer(new ArchiveEntry(record.Genome, score, descriptor, i), out _);
        }

        string status = $"{matching.Count} SAVED GAMES AT {players} PLAYERS"
            + (otherCounts > 0 ? $" · {otherCounts} AT OTHER PLAYER COUNTS (USE THE FILTER)" : "");
        var snapshot = new HyperspaceSnapshot(bins, players, entries,
            $"SAVED-GAME ARCHIVE — CELLS {archive.Count}/{DescriptorBins.CellCount} "
            + $"({archive.Coverage:P1}) · BEST {archive.Best?.Fitness ?? 0f:F1} "
            + $"· FITNESS {fitness.Name}");
        return (snapshot, status);
    }

    /// <summary>Every game the app has saved: favorites, demo games, each run's
    /// best.json, and loose game.jsons in the runs root. Sorted for stable order;
    /// unreadable files are skipped with a log line.</summary>
    private static System.Collections.Generic.List<(string, GameRecord)> ScanLibrary()
    {
        var paths = new System.Collections.Generic.List<string>();
        foreach (string dir in new[] { AppPaths.FavoritesRoot(), AppPaths.DemoRoot() })
        {
            if (System.IO.Directory.Exists(dir))
            {
                paths.AddRange(System.IO.Directory.GetFiles(dir, "*.json"));
            }
        }
        string runsRoot = AppPaths.RunsRoot();
        paths.AddRange(System.IO.Directory.GetFiles(runsRoot, "*.json"));
        foreach (string runDir in System.IO.Directory.GetDirectories(runsRoot))
        {
            string best = System.IO.Path.Combine(runDir, RunStore.BestGameFileName);
            if (System.IO.File.Exists(best))
            {
                paths.Add(best);
            }
        }

        var library = new System.Collections.Generic.List<(string, GameRecord)>();
        foreach (string path in paths.OrderBy(p => p, System.StringComparer.Ordinal))
        {
            try
            {
                library.Add((path, GameGenomeJson.Load(path)));
            }
            catch (System.Exception e)
            {
                GD.Print($"quality exploration: skipped unreadable {path}: {e.Message}");
            }
        }
        return library;
    }

    /// <summary>Deterministic per-game seed from the serialized genome bytes: scores
    /// and preview matches are stable for the same content no matter where the file
    /// lives or what else is in the library.</summary>
    private static ulong ContentSeed(GameRecord record)
    {
        byte[] bytes = System.Text.Encoding.UTF8.GetBytes(
            GameGenomeJson.Serialize(new GameRecord("k", null, record.Genome)));
        return Fnv1a.Hash(bytes, Fnv1a.OffsetBasis);
    }
}
