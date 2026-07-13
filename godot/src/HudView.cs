using Godot;
using BrawlerSim.Sim;

namespace BrawlerGodot;

/// <summary>Damage/stocks per player, end-of-match banner, and the pause overlay.</summary>
public partial class HudView : CanvasLayer
{
    private SimWorld _world = null!;
    private readonly Label[] _panels = new Label[2];
    private readonly Label[] _diArrows = new Label[2];
    private readonly int[] _lastHits = new int[2];
    private readonly int[] _flashTicks = new int[2];
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

    /// <summary>Debug DI indicator (2026-07-13, designer request): a live 8-way arrow
    /// of the held influence direction, flashing bright on the tick a hit lands.</summary>
    private void SyncDiArrow(int i)
    {
        var player = _world.Players[i];
        if (_diArrows[i] is null)
        {
            _diArrows[i] = new Label { Modulate = new Color(0.5f, 0.55f, 0.62f) };
            _diArrows[i].AddThemeFontSizeOverride("font_size", 22);
            _diArrows[i].Position = _panels[i].Position + new Vector2(i == 0 ? 210f : -34f, 2f);
            AddChild(_diArrows[i]);
        }
        int hits = player.TotalHitsReceived;
        if (hits != _lastHits[i])
        {
            _lastHits[i] = hits;
            _flashTicks[i] = 18;
        }
        _flashTicks[i] = Mathf.Max(0, _flashTicks[i] - 1);
        int dx = System.Math.Sign(player.HeldDirection.X);
        int dy = System.Math.Sign(player.HeldDirection.Y);
        string glyph = (dx, dy) switch
        {
            (0, 0) => "·",
            (1, 0) => "→", (-1, 0) => "←", (0, 1) => "↑", (0, -1) => "↓",
            (1, 1) => "↗", (-1, 1) => "↖", (1, -1) => "↘", (-1, -1) => "↙",
        };
        _diArrows[i].Text = glyph;
        _diArrows[i].Modulate = _flashTicks[i] > 0
            ? new Color(1f, 0.85f, 0.3f)
            : new Color(0.5f, 0.55f, 0.62f);
    }

    public void Sync(bool paused)
    {
        for (int i = 0; i < 2; i++)
        {
            SimPlayer player = _world.Players[i];
            string hearts = new string('#', player.Stocks).Replace("#", "● ");
            _panels[i].Text = $"{player.Name}  {player.Damage:F1}%\n{hearts}\n{player.State}";
            SyncDiArrow(i);
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
