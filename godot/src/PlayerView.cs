using Godot;
using BrawlerSim.Sim;
using SimAabb = BrawlerSim.Sim.Aabb;

namespace BrawlerGodot;

/// <summary>
/// Renders one SimPlayer: Kenney sprite scaled like Unity (nominal 16 px tile = 1 unit,
/// then the character's width/height scalars), state tint colors carried over from the
/// Unity build, name floating above, move sprite shown while the hitbox is live.
/// </summary>
public partial class PlayerView : Node2D
{
    private SimPlayer _player = null!;
    private Texture2D[] _moveTextures = System.Array.Empty<Texture2D>();
    private Sprite2D _body = null!;
    private Sprite2D _move = null!;
    private Label _name = null!;
    private float _ppu;

    public void Setup(SimPlayer player, int spriteIndex, int[] moveSpriteIndices, float ppu)
    {
        _player = player;
        _ppu = ppu;
        _moveTextures = new Texture2D[moveSpriteIndices.Length];
        for (int m = 0; m < moveSpriteIndices.Length; m++)
        {
            _moveTextures[m] = SpriteBank.Move(moveSpriteIndices[m]);
        }

        _body = new Sprite2D
        {
            Texture = SpriteBank.Player(spriteIndex),
            Scale = new Vector2(player.WidthScalar, player.HeightScalar) * (_ppu / 16f),
            TextureFilter = TextureFilterEnum.Nearest,
        };
        AddChild(_body);

        _move = new Sprite2D
        {
            Texture = _moveTextures[0],
            TextureFilter = TextureFilterEnum.Nearest,
            Visible = false,
        };
        AddChild(_move);

        _name = new Label
        {
            Text = player.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            Size = new Vector2(160f, 20f),
            Position = new Vector2(-80f, -_player.BodyHalf.Y * _ppu - 34f),
            Modulate = new Color(0.8f, 0.8f, 0.8f),
        };
        _name.AddThemeFontSizeOverride("font_size", 12);
        AddChild(_name);
    }

    public void Sync()
    {
        Position = new Vector2(_player.Position.X * _ppu, -_player.Position.Y * _ppu);
        _body.FlipH = _player.Facing < 0;
        _body.Modulate = StateColor(_player.State) with
        {
            A = _player.InvincibleTicksLeft > 0 ? 0.4f : 1f,
        };

        _move.Visible = _player.HitboxActive;
        if (_player.HitboxActive)
        {
            // Each move renders with its own sprite gene — the second attack must be
            // visually distinct from the first (second-move feature, readability Q10).
            _move.Texture = _moveTextures[_player.CurrentMoveIndex];
            SimAabb hitbox = _player.Hitbox;
            _move.Position = new Vector2(
                (hitbox.Center.X - _player.Position.X) * _ppu,
                -(hitbox.Center.Y - _player.Position.Y) * _ppu);
            // Fill the hitbox: the slice is nominally 16 px = 1 unit.
            _move.Scale = new Vector2(hitbox.Half.X, hitbox.Half.Y) * 2f * (_ppu / 16f);
            _move.FlipH = _player.Facing < 0;
        }
    }

    /// <summary>Unity SpriteRenderer state tints, verbatim.</summary>
    private static Color StateColor(PlayerState state) => state switch
    {
        PlayerState.Idle => Colors.White,
        PlayerState.Air => Colors.Green,
        PlayerState.AirJumpsExhausted => Colors.Gray,
        PlayerState.WarmUp => Colors.Yellow,
        PlayerState.Attack => Colors.Red,
        PlayerState.CoolDown => Colors.Blue,
        PlayerState.Stun => Colors.Magenta,
        _ => Colors.White,
    };
}
