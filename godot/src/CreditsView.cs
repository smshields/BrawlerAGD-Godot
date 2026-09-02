using Godot;

namespace BrawlerGodot;

/// <summary>
/// The dev main menu's CREDITS screen (backgrounds track Phase 1, 2026-09-02 —
/// mandatory before attribution-bearing background art ships). Data-driven and
/// boring on purpose: the courtesy block, then the backgrounds section generated
/// from the live index (CreditsUi), in a scrollable list. BACK/ESC returns to the
/// main menu.
/// </summary>
public partial class CreditsView : Control
{
    public override void _Ready()
    {
        Theme = UiTheme.Buttons;
        var panel = new PanelContainer { AnchorRight = 1f, AnchorBottom = 1f };
        panel.AddThemeStyleboxOverride("panel", UiWidgets.PanelStyle(
            new Color(0.05f, 0.05f, 0.08f, 1f), marginX: 60f, marginY: 30f));
        AddChild(panel);

        ScrollContainer scroll = UiWidgets.ScrollList(out VBoxContainer text, separation: 6);
        panel.AddChild(scroll);

        text.AddChild(UiWidgets.Heading("CREDITS", 26));
        text.AddChild(CreditsUi.Line(
            "Fighters, movesets, and arenas in this app are grown by an evolutionary "
            + "algorithm; the art below dresses what evolution designs.", 14));
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });

        text.AddChild(UiWidgets.Heading("MADE WITH", 18));
        text.AddChild(CreditsUi.Line(CreditsUi.CourtesyBlock, 13));
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 10f) });

        CreditsUi.AddBackgroundsSection(text);
        text.AddChild(new Control { CustomMinimumSize = new Vector2(0f, 14f) });

        var back = new Button { Text = "BACK", CustomMinimumSize = new Vector2(200f, 40f) };
        back.Pressed += BackToMenu;
        text.AddChild(back);
        back.GrabFocus();
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e.IsActionPressed("ui_cancel"))
        {
            BackToMenu();
        }
    }

    private void BackToMenu() => GetTree().ChangeSceneToFile(Scenes.MainMenu);
}
