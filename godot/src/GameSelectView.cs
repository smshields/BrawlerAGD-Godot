using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>Hand-off from game selection to the character select screen.</summary>
public static class BuiltGameSession
{
    public static BuiltGame? Game;
    public static string? Path;
}

/// <summary>
/// The Game Player's game selection screen (2026-08-14, FEATURES.md §Game Menu /
/// Game Player; docs/features/game-player.md): an organized, game-menu-styled list
/// of built games. COMPLETE games launch the character select — running the namegen
/// pass first (names generated on open, persisted once); incomplete games are shown
/// greyed with their progress badge.
/// </summary>
public partial class GameSelectView : Control
{
    public override void _Ready()
    {
        Theme = UiTheme.Buttons; // app-wide button styling (2026-08-17)
        Boot.ResetPadBindings(); // leave any previous session's pad joins behind

        var root = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0f, AnchorBottom = 1f,
            OffsetLeft = -280f, OffsetRight = 280f, OffsetTop = 40f, OffsetBottom = -40f,
        };
        root.AddThemeConstantOverride("separation", 10);
        AddChild(root);

        var title = new Label { Text = "PLAY GAME", HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 34);
        root.AddChild(title);

        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer list, separation: 8);
        scroll.SizeFlagsVertical = SizeFlags.ExpandFill;
        root.AddChild(scroll);

        // Load every built game ONCE and reuse the (path, game) pairs for the list,
        // the BRAWLER_AUTOOPEN scan, and the deferred open (previously each step
        // re-parsed the files).
        string[] files = System.IO.Directory.GetFiles(AppPaths.GamesRoot(), "*.json");
        System.Array.Sort(files);
        var games = new System.Collections.Generic.List<(string Path, BuiltGame Game)>();
        foreach (string file in files)
        {
            try
            {
                games.Add((file, BuiltGameJson.Load(file)));
            }
            catch (System.Exception e)
            {
                GD.PrintErr($"built game {file}: {e.Message}");
            }
        }
        int playable = 0;
        foreach ((string path, BuiltGame game) in games)
        {
            bool complete = game.IsComplete;
            var button = new Button
            {
                Text = complete
                    ? $"{game.Name}   —   {game.Characters.Count} FIGHTERS · {game.Stages.Count} STAGES"
                    : $"{game.Name}   —   IN PROGRESS ({GameLibraryUi.CompletionBadge(game)})",
                Alignment = HorizontalAlignment.Left,
                Disabled = !complete,
                CustomMinimumSize = new Vector2(0f, 52f),
            };
            if (complete)
            {
                playable++;
                button.Pressed += () => OpenGame(game, path);
            }
            list.AddChild(button);
        }
        if (playable == 0)
        {
            Label empty = UiWidgets.Hint(
                    "no complete games yet — assemble one in BUILD GAME "
                    + "(8 characters + 4 stages)", 14);
            empty.AutowrapMode = TextServer.AutowrapMode.WordSmart;
            list.AddChild(empty);
        }

        var back = new Button { Text = "BACK", CustomMinimumSize = new Vector2(0f, 44f) };
        back.Pressed += () => GetTree().ChangeSceneToFile("res://scenes/main_menu.tscn");
        root.AddChild(back);

        // Automation: BRAWLER_AUTOOPEN=1 opens the first complete game (with the
        // naming pass) so screenshots can reach the character select headlessly.
        if (OS.GetEnvironment("BRAWLER_AUTOOPEN") == "1")
        {
            foreach ((string path, BuiltGame game) in games)
            {
                if (game.IsComplete)
                {
                    _autoOpen = (path, game);
                    CallDeferred(nameof(DeferredOpen));
                    break;
                }
            }
        }
    }

    private (string Path, BuiltGame Game)? _autoOpen;

    private void DeferredOpen()
    {
        if (_autoOpen is { } pending)
        {
            OpenGame(pending.Game, pending.Path);
        }
    }

    private void OpenGame(BuiltGame game, string path)
    {
        // Item 6: names are generated on open and persisted once (BuiltGameNamer
        // leaves manual renames and previously generated names alone).
        if (BuiltGameNamer.EnsureNamed(game, path))
        {
            GD.Print($"named built game elements: {path}");
        }
        BuiltGameSession.Game = game;
        BuiltGameSession.Path = path;
        GetTree().ChangeSceneToFile("res://scenes/character_select.tscn");
    }
}
