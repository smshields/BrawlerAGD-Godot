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
    private int _flashClock; // cosmetic strobe phase (view-only, not sim state)

    // Motion trail (FEATURES.md §Movement Blur; re-rendered 2026-07-23, designer:
    // the in-quad UV smear could not draw OUTSIDE the sprite's own rect, so it read
    // as a faint dimming instead of a trail). AFTERIMAGES: ghost copies of the body
    // sprite along the recent flight path, state-tinted, fading with age, whose
    // opacity ramps with SCREEN-space speed (world speed × camera zoom) so they
    // appear exactly when the character is hard to track at any map size.
    // Second pass (2026-07-23, designer: high-speed KOs read as TELEPORTS): ghosts
    // are placed at EQUAL ARC-LENGTH intervals along the sampled polyline — at
    // knockback speeds the samples are hundreds of px apart, so fixed-stride ghosts
    // left the path empty. Interpolating along the path keeps the streak continuous
    // at ANY speed, and the teleport test is velocity-aware (a real teleport is a
    // jump far beyond what the current velocity explains) instead of a fixed px
    // threshold that a fast KO can legitimately exceed. View-only.
    // Opacity follows an EXPONENTIAL ease-in over the speed range. Re-tuned
    // 2026-07-27 (designer): the trail exists ONLY to track extremely fast
    // characters — the floor now sits ABOVE normal run/jump speeds (~12 u/s ≈
    // 850 screen px/s), so ordinary movement draws nothing, dashes read as a bare
    // hint, and knockback/KO flights ramp to the full streak.
    private const int GhostCount = 12;
    private const int TrailSamples = 10;          // polyline length (frames of history)
    private const float TrailMinSpeedPx = 850f;   // screen px/s where ghosts start
    private const float TrailFullSpeedPx = 2800f; // …and reach full opacity
    private const float TrailCurveK = 3.5f;       // exponent steepness of the ease-in
    // Ghosts never pack closer than this along the path: on a SHORT (slow) path
    // only a few separated ghosts appear — stacked ghosts compound alpha into a
    // solid blob, which read as heavy blur at low speeds.
    private const float MinGhostSpacingPx = 22f;

    private readonly Sprite2D[] _ghosts = new Sprite2D[GhostCount];
    private readonly struct TrailSample
    {
        public readonly Vector2 ArenaPos;   // body position in arena space
        public readonly Vector2 Scale;
        public readonly bool Flip;
        public readonly Color Tint;
        public TrailSample(Vector2 arenaPos, Vector2 scale, bool flip, Color tint)
        {
            ArenaPos = arenaPos; Scale = scale; Flip = flip; Tint = tint;
        }
    }
    // Newest first; index 0 is this frame's body.
    private readonly System.Collections.Generic.List<TrailSample> _trail = new();

    // A trail DETACHED by a KO/teleport lingers and fades in place instead of
    // vanishing with the body — without it, the fastest KOs (the ones the trail
    // exists for) erased their own flight path on the death frame.
    private readonly System.Collections.Generic.List<TrailSample> _dying = new();
    private float _dyingStrength;
    private float _lastStrength;
    private const float DyingDecay = 0.90f; // per frame ⇒ ~0.5 s linger

    public void Setup(SimPlayer player, int spriteIndex, int[] moveSpriteIndices, float ppu)
    {
        _player = player;
        _ppu = ppu;
        _moveTextures = new Texture2D[moveSpriteIndices.Length];
        for (int m = 0; m < moveSpriteIndices.Length; m++)
        {
            _moveTextures[m] = SpriteBank.Move(moveSpriteIndices[m]);
        }

        // Ghosts are added FIRST so the live body always draws over its own trail.
        for (int g = 0; g < GhostCount; g++)
        {
            _ghosts[g] = new Sprite2D
            {
                Texture = SpriteBank.Player(spriteIndex),
                TextureFilter = TextureFilterEnum.Nearest,
                Visible = false,
            };
            AddChild(_ghosts[g]);
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

        // Name tag in a colored pill (HUD polish, 2026-07-23): slight transparent
        // pill background in the player's assigned identity color, matching the HUD.
        _name = new Label
        {
            Text = player.Name,
            HorizontalAlignment = HorizontalAlignment.Center,
            Modulate = new Color(1f, 1f, 1f),
        };
        _name.AddThemeFontSizeOverride("font_size", 12);
        _name.AddThemeStyleboxOverride("normal", new StyleBoxFlat
        {
            BgColor = PlayerPalette.Of(player.Index) with { A = 0.38f },
            CornerRadiusTopLeft = 999, CornerRadiusTopRight = 999,
            CornerRadiusBottomLeft = 999, CornerRadiusBottomRight = 999,
            ContentMarginLeft = 8f, ContentMarginRight = 8f,
            ContentMarginTop = 1f, ContentMarginBottom = 1f,
        });
        AddChild(_name);
        _name.ResetSize();
        _name.Position = new Vector2(-_name.Size.X / 2f, -_player.BodyHalf.Y * _ppu - 36f);
    }

    public void Sync()
    {
        _flashClock++;
        // Respawn blackout (2026-07-22) or elimination (2026-08-12): an absent
        // character shows nothing — except the DETACHED motion trail (2026-07-23),
        // which lingers and fades where the KO flight ended so the fastest KOs stay
        // trackable.
        bool absent = _player.IsAbsent;
        _body.Visible = !absent;
        _name.Visible = !absent;
        if (absent)
        {
            _move.Visible = false;
            QueueRedraw(); // clears any stale shield circle
            DetachTrail();
            SyncDyingTrail();
            return;
        }
        Position = new Vector2(_player.Position.X * _ppu, -_player.Position.Y * _ppu);
        QueueRedraw(); // shield circle tracks sim state every frame
        // Crouch squish (2026-07-13): scale from sim state, feet planted.
        float crouch = _player.CrouchScale;
        _body.Scale = new Vector2(_player.WidthScalar, _player.HeightScalar * crouch) * (_ppu / 16f);
        _body.Position = new Vector2(0f, _player.BodyHalf.Y * (1f - crouch) * _ppu);
        _body.FlipH = _player.Facing < 0;
        // Alpha vocabulary (view-only): spawn invulnerability = a SLOW shimmer pulse
        // (2026-07-22, distinct from the two below); post-hit invincibility = steady
        // 0.4; dash i-frames = fast strobe.
        float alpha = _player.SpawnDamageImmune
                ? 0.7f + 0.3f * Mathf.Sin(_flashClock * 0.18f)
            : _player.InvincibleTicksLeft > 0 ? 0.4f
            : _player.DashInvulnerable ? (_flashClock % 6 < 3 ? 1f : 0.6f)
            : 1f;
        _body.Modulate = StateColor(_player.State) with { A = alpha };
        UpdateTrail();

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

    /// <summary>Afterimage trail (2026-07-23, second pass): record the body each
    /// frame into a short polyline, then place GhostCount fading, state-tinted
    /// copies at EQUAL ARC-LENGTH intervals along it — at knockback speeds
    /// (hundreds of px per frame) the ghosts interpolate BETWEEN samples, so the
    /// flight path stays a continuous, trackable streak instead of a teleport.
    /// Ghost opacity ramps with screen-space speed. A REAL teleport
    /// (respawn/materialize — a jump far beyond what the current velocity explains)
    /// clears the trail so ghosts never smear across the arena.</summary>
    private void UpdateTrail()
    {
        Vector2 arenaPos = Position + _body.Position;
        var vel = _player.Velocity;
        float speedPx = Mathf.Sqrt(vel.X * vel.X + vel.Y * vel.Y) * _ppu; // arena px/s
        if (_trail.Count > 0)
        {
            // Velocity-aware teleport test: generous ×3 (+floor) so fast-forward
            // rendering and one-tick knockback spikes never false-positive. A real
            // teleport (instant respawn) DETACHES the trail to fade in place.
            float jump = (arenaPos - _trail[0].ArenaPos).Length();
            if (jump > Mathf.Max(150f, speedPx / 60f * 3f))
            {
                DetachTrail();
            }
        }
        _trail.Insert(0, new TrailSample(arenaPos, _body.Scale, _body.FlipH, StateColor(_player.State)));
        if (_trail.Count > TrailSamples)
        {
            _trail.RemoveAt(_trail.Count - 1);
        }

        float zoom = GetViewportTransform().Scale.X; // camera zoom (px per render px)
        float raw = Mathf.Clamp(
            (speedPx * zoom - TrailMinSpeedPx) / (TrailFullSpeedPx - TrailMinSpeedPx), 0f, 1f);
        // Exponential ease-in: ~0 through walking speeds, then a dramatic climb.
        float strength = (Mathf.Exp(TrailCurveK * raw) - 1f) / (Mathf.Exp(TrailCurveK) - 1f);
        _lastStrength = strength;

        // A dying trail owns the ghost pool until it has faded (moments after a
        // KO/teleport, when the fresh trail is still empty anyway).
        if (_dying.Count > 0)
        {
            SyncDyingTrail();
            return;
        }
        PlaceGhosts(_trail, strength);
    }

    /// <summary>Move the live trail to the fading list (KO flight / teleport): it
    /// keeps rendering IN PLACE while the body is gone or elsewhere.</summary>
    private void DetachTrail()
    {
        if (_trail.Count >= 2)
        {
            _dying.Clear();
            _dying.AddRange(_trail);
            // Boost a FAST flight so it lingers readably — but a slow drift into
            // the blast zone had no trail and must not conjure one (2026-07-27:
            // the trail is for extreme speeds only).
            _dyingStrength = _lastStrength < 0.02f ? 0f : Mathf.Max(_lastStrength, 0.35f);
        }
        _trail.Clear();
    }

    private void SyncDyingTrail()
    {
        if (_dying.Count < 2)
        {
            return;
        }
        _dyingStrength *= DyingDecay;
        if (_dyingStrength < 0.04f)
        {
            _dying.Clear();
            foreach (Sprite2D g in _ghosts)
            {
                g.Visible = false;
            }
            return;
        }
        PlaceGhosts(_dying, _dyingStrength);
    }

    /// <summary>Distribute the ghost pool at equal arc-length intervals along a
    /// sampled path, oldest ghosts faintest.</summary>
    private void PlaceGhosts(System.Collections.Generic.List<TrailSample> path, float strength)
    {
        // Arc length of the recorded path (newest → oldest).
        float total = 0f;
        for (int i = 1; i < path.Count; i++)
        {
            total += (path[i].ArenaPos - path[i - 1].ArenaPos).Length();
        }
        if (strength <= 0f || total < 4f)
        {
            foreach (Sprite2D g in _ghosts)
            {
                g.Visible = false;
            }
            return;
        }

        int count = Mathf.Min(GhostCount, (int)(total / MinGhostSpacingPx));
        int seg = 1;               // current segment end index
        float segStart = 0f;       // arc distance at segment start
        float segLen = (path[1].ArenaPos - path[0].ArenaPos).Length();
        for (int g = 0; g < GhostCount; g++)
        {
            if (g >= count)
            {
                _ghosts[g].Visible = false;
                continue;
            }
            float t = (g + 1) / (float)(count + 1); // 0..1 along the path
            float s = t * total;
            while (s > segStart + segLen && seg < path.Count - 1)
            {
                segStart += segLen;
                seg++;
                segLen = (path[seg].ArenaPos - path[seg - 1].ArenaPos).Length();
            }
            TrailSample from = path[seg - 1];
            TrailSample to = path[seg];
            float f = segLen > 0.001f ? Mathf.Clamp((s - segStart) / segLen, 0f, 1f) : 0f;
            Sprite2D ghost = _ghosts[g];
            ghost.Visible = true;
            ghost.Position = from.ArenaPos.Lerp(to.ArenaPos, f) - Position; // into local space
            ghost.Scale = to.Scale;
            ghost.FlipH = to.Flip;
            ghost.Modulate = to.Tint with { A = strength * 0.45f * (1f - t) };
        }
    }

    /// <summary>Shield circle (2026-07-12): white outline that turns red as the shield
    /// degrades; radius, offset, and grow/shrink animation all come from sim state.</summary>
    public override void _Draw()
    {
        if (_player is null || _player.State != PlayerState.Shield)
        {
            return;
        }
        float radius = _player.ShieldRadius * _ppu;
        if (radius <= 0f)
        {
            return;
        }
        var center = new Vector2(_player.ShieldOffset.X * _ppu, -_player.ShieldOffset.Y * _ppu);
        SimShield? shield = _player.ActiveShield;
        float health = shield is null || shield.InitialRadius <= 0f
            ? 0f
            : _player.ShieldHealths[_player.CurrentMoveIndex] / shield.InitialRadius;
        Color color = Colors.White.Lerp(Colors.Red, Mathf.Clamp(1f - health, 0f, 1f));
        DrawArc(center, radius, 0f, Mathf.Tau, 48, color with { A = 0.9f }, 2f, antialiased: false);
        DrawCircle(center, radius, color with { A = 0.12f });
    }

    /// <summary>Unity SpriteRenderer state tints, verbatim — plus cyan for the Shield
    /// state (designer tint decision, 2026-07-12). Public: the HUD's human-readable
    /// state readout matches the body tint (HUD polish, 2026-07-23).</summary>
    public static Color StateColor(PlayerState state) => state switch
    {
        PlayerState.Shield => Colors.Cyan,
        PlayerState.Dash => Colors.Orange, // 2026-07-13 designer tint decision
        PlayerState.Crouch => Colors.Purple, // 2026-07-13 designer tint decision
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
