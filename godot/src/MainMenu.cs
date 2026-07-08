using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// App shell: Play (2P / vs CPU), Watch AI, Watch Replay (file dialogs for any
/// game.json), plus the Evolve dashboard and the Manage library browser.
/// </summary>
public partial class MainMenu : Control
{
    private FileDialog _gameDialog = null!;
    private FileDialog _traceDialog = null!;
    private MatchMode _pendingMode;
    private Label _hint = null!;

    public override void _Ready()
    {
        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
        };
        box.AddThemeConstantOverride("separation", 10);
        AddChild(box);

        var title = new Label { Text = "BRAWLER AGD", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 52);
        box.AddChild(title);
        var subtitle = new Label
        {
            Text = "automated brawler game designer — godot edition",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.65f, 0.7f, 0.78f),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 15);
        box.AddChild(subtitle);
        box.AddChild(new Control { CustomMinimumSize = new Vector2(0, 18) });

        AddButton(box, "PLAY — 2 PLAYERS", () => PickGame(MatchMode.HumanVsHuman));
        AddButton(box, "PLAY — VS CPU", () => PickGame(MatchMode.HumanVsCpu));
        AddButton(box, "WATCH AI MATCH", () => PickGame(MatchMode.AiVsAi));
        AddButton(box, "WATCH REPLAY", () => PickGame(MatchMode.Replay));
        AddButton(box, "EVOLVE", () => GetTree().ChangeSceneToFile("res://scenes/evolve.tscn"));
        AddButton(box, "MANAGE GAMES", () => GetTree().ChangeSceneToFile("res://scenes/manage.tscn"));
        AddButton(box, "QUIT", () => GetTree().Quit());

        _hint = new Label
        {
            Text = "P1: A/D move · W jump · S attack        P2: J/L move · I jump · K attack\n" +
                   "gamepads: stick/dpad · A/B jump · X/Y attack",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.55f, 0.6f, 0.68f),
            AnchorTop = 1f, AnchorBottom = 1f, AnchorRight = 1f,
            OffsetTop = -64f,
        };
        _hint.AddThemeFontSizeOverride("font_size", 14);
        AddChild(_hint);

        _gameDialog = MakeDialog("Choose a game.json", OnGamePicked);
        _traceDialog = MakeDialog("Choose the matching trace.json", OnTracePicked);
    }

    private void PickGame(MatchMode mode)
    {
        _pendingMode = mode;
        _gameDialog.PopupCentered(new Vector2I(900, 600));
    }

    private void OnGamePicked(string path)
    {
        MatchSession.Game = GameGenomeJson.Load(path);
        MatchSession.Mode = _pendingMode;
        if (_pendingMode == MatchMode.Replay)
        {
            _traceDialog.CurrentDir = _gameDialog.CurrentDir;
            _traceDialog.PopupCentered(new Vector2I(900, 600));
            return;
        }
        StartMatch();
    }

    private void OnTracePicked(string path)
    {
        MatchSession.Trace = BrawlerSim.Replay.InputTraceJson.Load(path);
        StartMatch();
    }

    private void StartMatch()
    {
        GetTree().ChangeSceneToFile("res://scenes/arena.tscn");
    }

    private FileDialog MakeDialog(string title, System.Action<string> onSelected)
    {
        var dialog = new FileDialog
        {
            Title = title,
            Access = FileDialog.AccessEnum.Filesystem,
            FileMode = FileDialog.FileModeEnum.OpenFile,
            Filters = new[] { "*.json" },
            CurrentDir = ProjectSettings.GlobalizePath("res://").GetBaseDir(),
        };
        dialog.FileSelected += path => onSelected(path);
        AddChild(dialog);
        return dialog;
    }

    private static void AddButton(VBoxContainer box, string text, System.Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(340f, 44f) };
        button.Pressed += () => onPressed();
        box.AddChild(button);
    }
}
