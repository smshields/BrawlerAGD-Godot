using Godot;
using System.Linq;
using System.Text.Json;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// Game library: evolution runs (with their best individuals) and loose game.json files
/// under the shared runs directory, plus the last played match. Selected entries can be
/// played, watched (graded-match replay when a trace exists, AI self-play otherwise),
/// or deleted after confirmation.
/// </summary>
public partial class ManageView : Control
{
    private sealed record Entry(string Label, string GamePath, string? TracePath, string DeleteTarget, bool DeleteIsDirectory);

    private readonly System.Collections.Generic.List<Entry> _entries = new();
    private ItemList _list = null!;
    private Label _detail = null!;
    private ConfirmationDialog _confirm = null!;
    private Button _twoPlayerButton = null!;

    public override void _Ready()
    {
        BuildUi();
        Refresh();
    }

    private void Refresh()
    {
        _entries.Clear();
        _list.Clear();
        string root = AppPaths.RunsRoot();

        foreach (string dir in System.IO.Directory.GetDirectories(root).OrderBy(d => d))
        {
            string manifest = System.IO.Path.Combine(dir, "run.json");
            string best = System.IO.Path.Combine(dir, "best.json");
            if (!System.IO.File.Exists(manifest) || !System.IO.File.Exists(best))
            {
                continue;
            }
            string name = System.IO.Path.GetFileName(dir);
            string summary = RunSummary(manifest);
            string? trace = System.IO.Path.Combine(dir, "best.trace.json") is string t && System.IO.File.Exists(t) ? t : null;
            Add(new Entry($"run  {name}   {summary}", best, trace, dir, DeleteIsDirectory: true));
        }

        foreach (string file in System.IO.Directory.GetFiles(root, "*.json").OrderBy(f => f))
        {
            Add(new Entry($"game {System.IO.Path.GetFileName(file)}", file, null, file, DeleteIsDirectory: false));
        }

        string lastGame = System.IO.Path.Combine(AppPaths.ReplaysRoot(), "last_match.game.json");
        string lastTrace = System.IO.Path.Combine(AppPaths.ReplaysRoot(), "last_match.trace.json");
        if (System.IO.File.Exists(lastGame) && System.IO.File.Exists(lastTrace))
        {
            Add(new Entry("last played match (replay)", lastGame, lastTrace, lastGame, DeleteIsDirectory: false));
        }

        _detail.Text = $"{_entries.Count} entries in {root}";
    }

    private static string RunSummary(string manifestPath)
    {
        try
        {
            using JsonDocument doc = JsonDocument.Parse(System.IO.File.ReadAllText(manifestPath));
            JsonElement rootEl = doc.RootElement;
            int generations = rootEl.GetProperty("generationsCompleted").GetInt32();
            JsonElement stats = rootEl.GetProperty("stats");
            float top = float.MinValue;
            foreach (JsonElement s in stats.EnumerateArray())
            {
                top = System.MathF.Max(top, s.GetProperty("topFitness").GetSingle());
            }
            return $"({generations} gens, best {top:F1})";
        }
        catch (System.Exception)
        {
            return "(unreadable manifest)";
        }
    }

    private void Add(Entry entry)
    {
        _entries.Add(entry);
        _list.AddItem(entry.Label);
    }

    private Entry? Selected()
    {
        int[] selected = _list.GetSelectedItems();
        return selected.Length > 0 ? _entries[selected[0]] : null;
    }

    private void Launch(MatchMode mode)
    {
        if (Selected() is not Entry entry)
        {
            return;
        }
        MatchSession.Game = GameGenomeJson.Load(entry.GamePath);
        if (mode == MatchMode.Replay && entry.TracePath != null)
        {
            MatchSession.Trace = BrawlerSim.Replay.InputTraceJson.Load(entry.TracePath);
            MatchSession.Mode = MatchMode.Replay;
        }
        else
        {
            MatchSession.Mode = mode == MatchMode.Replay ? MatchMode.AiVsAi : mode;
        }
        GetTree().ChangeSceneToFile("res://scenes/arena.tscn");
    }

    private void ConfirmDelete()
    {
        if (Selected() is not Entry entry)
        {
            return;
        }
        _confirm.DialogText = $"Delete {(entry.DeleteIsDirectory ? "the whole run directory" : "this file")}?\n{entry.DeleteTarget}";
        _confirm.PopupCentered();
    }

    private void DeleteSelected()
    {
        if (Selected() is not Entry entry)
        {
            return;
        }
        if (entry.DeleteIsDirectory)
        {
            System.IO.Directory.Delete(entry.DeleteTarget, recursive: true);
        }
        else
        {
            System.IO.File.Delete(entry.DeleteTarget);
        }
        Refresh();
    }

    private void BuildUi()
    {
        var root = new VBoxContainer
        {
            AnchorRight = 1f, AnchorBottom = 1f,
            OffsetLeft = 24f, OffsetTop = 24f, OffsetRight = -24f, OffsetBottom = -24f,
        };
        root.AddThemeConstantOverride("separation", 10);
        AddChild(root);

        var title = new Label { Text = "MANAGE GAMES" };
        title.AddThemeFontSizeOverride("font_size", 34);
        root.AddChild(title);

        _list = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill };
        _list.AddThemeFontSizeOverride("font_size", 17);
        root.AddChild(_list);

        _detail = new Label();
        _detail.AddThemeFontSizeOverride("font_size", 13);
        _detail.Modulate = new Color(0.6f, 0.65f, 0.72f);
        root.AddChild(_detail);

        var buttons = new HBoxContainer();
        buttons.AddThemeConstantOverride("separation", 10);
        root.AddChild(buttons);
        _twoPlayerButton = AddButton(buttons, "PLAY 2P", () => Launch(MatchMode.HumanVsHuman));
        AddButton(buttons, "PLAY vs CPU", () => Launch(MatchMode.HumanVsCpu));
        AddButton(buttons, "WATCH", () => Launch(MatchMode.Replay));
        AddButton(buttons, "DELETE…", ConfirmDelete);
        AddButton(buttons, "REFRESH", Refresh);
        AddButton(buttons, "BACK", () => GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn"));

        _confirm = new ConfirmationDialog { Title = "Delete" };
        _confirm.Confirmed += DeleteSelected;
        AddChild(_confirm);

        // 2-player needs a controller (the keyboard is entirely P1's now).
        Input.Singleton.JoyConnectionChanged += OnJoyConnectionChanged;
        UpdateTwoPlayerAvailability();
    }

    public override void _ExitTree()
    {
        Input.Singleton.JoyConnectionChanged -= OnJoyConnectionChanged;
    }

    private void OnJoyConnectionChanged(long device, bool connected) => UpdateTwoPlayerAvailability();

    private void UpdateTwoPlayerAvailability()
    {
        bool hasPad = Input.GetConnectedJoypads().Count > 0;
        _twoPlayerButton.Disabled = !hasPad;
        _twoPlayerButton.TooltipText = hasPad ? "" : "CONNECT A CONTROLLER";
    }

    private static Button AddButton(HBoxContainer box, string text, System.Action onPressed)
    {
        var button = new Button { Text = text };
        button.Pressed += () => onPressed();
        box.AddChild(button);
        return button;
    }
}
