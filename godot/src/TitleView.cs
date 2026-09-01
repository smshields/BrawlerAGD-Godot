using Godot;
using BrawlerSim.Serialization;

namespace BrawlerGodot;

/// <summary>
/// The packaged game's title screen (2026-08-15, docs/features/packaged-games.md;
/// designer surface: PLAY / SETTINGS / CREDITS / QUIT). CREDITS is generated from
/// the game itself — the roster and stages with their evolution provenance are the
/// credits of a machine-grown game — plus the engine/asset/tool acknowledgements.
/// </summary>
public partial class TitleView : Control
{
    private Control? _credits;

    public override void _Ready()
    {
        // Reaching the title screen — exported boot, the dev menu's TEST STANDALONE
        // GAME button, or running the scene directly — ENTERS standalone navigation
        // (2026-08-17): character select's BACK and the arena's quit return here.
        Standalone.Enter();
        Theme = UiTheme.Buttons; // app-wide button styling (2026-08-17)
        Boot.ResetPadBindings();
        BuiltGame game = Standalone.Game;

        var box = new VBoxContainer
        {
            AnchorLeft = 0.5f, AnchorRight = 0.5f, AnchorTop = 0.5f, AnchorBottom = 0.5f,
            GrowHorizontal = GrowDirection.Both, GrowVertical = GrowDirection.Both,
        };
        box.AddThemeConstantOverride("separation", 12);
        AddChild(box);

        var title = new Label
        {
            Text = game.Name.ToUpperInvariant(),
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        title.AddThemeFontSizeOverride("font_size", 52);
        box.AddChild(title);

        Label subtitle = UiWidgets.Hint(
            $"{game.Characters.Count} FIGHTERS · {game.Stages.Count} STAGES — "
            + "A GAME GROWN BY EVOLUTION", 14);
        subtitle.HorizontalAlignment = HorizontalAlignment.Center;
        box.AddChild(subtitle);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 16f) });

        AddButton(box, "PLAY", Play);
        AddButton(box, "SETTINGS", OpenSettings);
        AddButton(box, "CREDITS", ToggleCredits);
        AddButton(box, "QUIT", () => GetTree().Quit());

        // Editor-only escape hatch back to the dev menu (2026-08-17, designer:
        // standalone testing must not lock the session out of the evolution UI).
        // Exported packages never show it — "editor" is an editor-binary feature.
        if (OS.HasFeature("editor"))
        {
            box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });
            var dev = new Button
            {
                Text = "DEV MENU",
                CustomMinimumSize = new Vector2(340f, 36f),
                Modulate = new Color(1f, 1f, 1f, 0.6f),
            };
            dev.Pressed += () =>
            {
                Standalone.ExitToDevMenu();
                GetTree().ChangeSceneToFile(Scenes.MainMenu);
            };
            box.AddChild(dev);
        }

        // Automation (screenshot verification): BRAWLER_TITLE="play"|"credits"|"settings"
        // presses that button on load.
        switch (AutomationEnv.Title)
        {
            case "play": CallDeferred(nameof(Play)); break;
            case "credits": CallDeferred(nameof(ToggleCredits)); break;
            case "settings": CallDeferred(nameof(OpenSettings)); break;
        }
    }

    private void Play()
    {
        BuiltGameSession.Game = Standalone.Game;
        BuiltGameSession.Path = null; // embedded: read-only, never re-persisted
        GetTree().ChangeSceneToFile(Scenes.CharacterSelect);
    }

    private static void AddButton(VBoxContainer box, string text, System.Action onPressed)
    {
        var button = new Button { Text = text, CustomMinimumSize = new Vector2(340f, 44f) };
        button.Pressed += () => onPressed();
        box.AddChild(button);
    }

    /// <summary>Same options as the pause menu's settings (minimap + debug strip).</summary>
    private void OpenSettings()
    {
        var popup = new PopupPanel { Theme = UiTheme.Buttons }; // popups don't inherit the scene theme
        var box = new VBoxContainer { CustomMinimumSize = new Vector2(360f, 0f) };
        box.AddThemeConstantOverride("separation", 8);
        popup.AddChild(box);

        var minimap = new CheckButton { Text = "MINIMAP", ButtonPressed = AppSettings.MinimapEnabled };
        minimap.Toggled += on => AppSettings.MinimapEnabled = on;
        box.AddChild(minimap);

        var debug = new CheckButton { Text = "DEBUG PANEL", ButtonPressed = AppSettings.DebugPanelEnabled };
        debug.Toggled += on => AppSettings.DebugPanelEnabled = on;
        box.AddChild(debug);

        var close = new Button { Text = "CLOSE" };
        close.Pressed += popup.Hide;
        box.AddChild(close);

        AddChild(popup);
        popup.PopupCentered();
    }

    private void ToggleCredits()
    {
        if (_credits is not null)
        {
            _credits.QueueFree();
            _credits = null;
            return;
        }
        BuiltGame game = Standalone.Game;
        var overlay = new PanelContainer { AnchorRight = 1f, AnchorBottom = 1f };
        overlay.AddThemeStyleboxOverride("panel", UiWidgets.PanelStyle(
            new Color(0.05f, 0.05f, 0.08f, 0.96f), marginX: 60f, marginY: 30f));
        _credits = overlay;
        AddChild(overlay);

        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer text, separation: 6);
        overlay.AddChild(scroll);

        void Heading(string s) => text.AddChild(UiWidgets.Heading(s, 18));
        void Line(string s, int size = 13)
        {
            var label = new Label { Text = s, AutowrapMode = TextServer.AutowrapMode.WordSmart };
            label.AddThemeFontSizeOverride("font_size", size);
            text.AddChild(label);
        }

        Heading(game.Name.ToUpperInvariant());
        Line("This game was not designed by hand: its fighters, movesets, and arenas were "
            + "GROWN by an evolutionary algorithm playing millions of matches against itself, "
            + "then curated and compiled into the package you are holding.", 14);
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });

        Heading("THE FIGHTERS");
        foreach (BuiltCharacter c in game.Characters)
        {
            Line($"{c.DisplayName.ToUpperInvariant()}  —  {c.Origin ?? "origin unknown"}");
        }
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });

        Heading("THE STAGES");
        foreach (BuiltStage s in game.Stages)
        {
            Line($"{s.DisplayName.ToUpperInvariant()}  —  {s.Origin ?? "origin unknown"}");
        }
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });

        Heading("HOW IT WAS MADE");
        Line("BrawlerAGD — automated game design research by Sam Shields "
            + "(Shields Games and Research).");
        Line("Built on \"Searching for Balanced 2D Brawler Games: Successes and Failures "
            + "of Automated Evaluation\" (Shields, Mawhorter, Melcer, Mateas — AIIDE 2022).");
        Line("Engine: Godot. Character and attack sprites: Dungeon Crawl Stone Soup "
            + "tiles (CC0 — thank you to the DCSS artists; github.com/crawl/tiles). "
            + "Tile sprites: Kenney 1-bit pack (kenney.nl). "
            + "Fighter and stage names: namegen.");
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 14f) });

        var back = new Button { Text = "BACK", CustomMinimumSize = new Vector2(200f, 40f) };
        back.Pressed += ToggleCredits;
        text.AddChild(back);
    }
}
