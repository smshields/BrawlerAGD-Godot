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

        var subtitle = new Label
        {
            Text = $"{game.Characters.Count} FIGHTERS · {game.Stages.Count} STAGES — "
                + "A GAME GROWN BY EVOLUTION",
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(0.55f, 0.6f, 0.68f),
        };
        subtitle.AddThemeFontSizeOverride("font_size", 14);
        box.AddChild(subtitle);

        box.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 16f) });

        AddButton(box, "PLAY", Play);
        AddButton(box, "SETTINGS", OpenSettings);
        AddButton(box, "CREDITS", ToggleCredits);
        AddButton(box, "QUIT", () => GetTree().Quit());

        // Automation (screenshot verification): BRAWLER_TITLE="play"|"credits"|"settings"
        // presses that button on load.
        switch (OS.GetEnvironment("BRAWLER_TITLE"))
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
        GetTree().ChangeSceneToFile("res://scenes/character_select.tscn");
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
        var popup = new PopupPanel();
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
        overlay.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.05f, 0.05f, 0.08f, 0.96f),
            ContentMarginLeft = 60f, ContentMarginRight = 60f,
            ContentMarginTop = 30f, ContentMarginBottom = 30f,
        });
        _credits = overlay;
        AddChild(overlay);

        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        overlay.AddChild(scroll);
        var text = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        text.AddThemeConstantOverride("separation", 6);
        scroll.AddChild(text);

        void Heading(string s)
        {
            var label = new Label { Text = s, Modulate = new Color(0.65f, 0.7f, 0.78f) };
            label.AddThemeFontSizeOverride("font_size", 18);
            text.AddChild(label);
        }
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
        Line("Engine: Godot. Sprites: Kenney 1-bit pack (kenney.nl). "
            + "Fighter and stage names: namegen.");
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 14f) });

        var back = new Button { Text = "BACK", CustomMinimumSize = new Vector2(200f, 40f) };
        back.Pressed += ToggleCredits;
        text.AddChild(back);
    }
}
