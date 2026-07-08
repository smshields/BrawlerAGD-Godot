using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>Damage/stocks per player, end-of-match banner, and the pause overlay.</summary>
public partial class HudView : CanvasLayer
{
    private SimWorld _world = null!;
    private readonly Label[] _panels = new Label[2];
    private Label _banner = null!;
    private Label _pause = null!;

    public void Setup(SimWorld world)
    {
        _world = world;

        for (int i = 0; i < 2; i++)
        {
            var panel = new Label
            {
                HorizontalAlignment = i == 0 ? HorizontalAlignment.Left : HorizontalAlignment.Right,
                AnchorLeft = i == 0 ? 0f : 1f,
                AnchorRight = i == 0 ? 0f : 1f,
                OffsetLeft = i == 0 ? 24f : -324f,
                OffsetRight = i == 0 ? 324f : -24f,
                OffsetTop = 16f,
            };
            panel.AddThemeFontSizeOverride("font_size", 22);
            AddChild(panel);
            _panels[i] = panel;
        }

        _banner = CenteredLabel(36, new Color(1f, 0.9f, 0.6f));
        _banner.Visible = false;
        _pause = CenteredLabel(28, new Color(0.9f, 0.9f, 0.95f));
        _pause.Text = "PAUSED\nESC resume · Q quit to menu";
        _pause.Visible = false;
    }

    public void Sync(bool paused)
    {
        for (int i = 0; i < 2; i++)
        {
            SimPlayer player = _world.Players[i];
            string hearts = new string('#', player.Stocks).Replace("#", "● ");
            _panels[i].Text = $"{player.Name}  {player.Damage:F1}%\n{hearts}\n{player.State}";
        }
        _pause.Visible = paused;

        if (_world.IsOver)
        {
            _banner.Visible = true;
            _banner.Text = _world.LoserIndex < 0
                ? "TIME! IT'S A DRAW"
                : $"{_world.Players[_world.LoserIndex].Name} HAS LOST THE GAME";
            _banner.Text += "\nESC — back to menu";
        }
    }

    private Label CenteredLabel(int fontSize, Color color)
    {
        var label = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 0.32f,
            Modulate = color,
        };
        label.AddThemeFontSizeOverride("font_size", fontSize);
        AddChild(label);
        return label;
    }
}
