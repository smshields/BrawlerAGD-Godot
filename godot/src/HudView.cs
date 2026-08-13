using Godot;
using BrawlerSim.Sim;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>
/// The match HUD (rewritten 2026-07-23, FEATURES.md §HUD + design/BrawlerAGDHUD.jpg).
/// Four static quarter-width slots along the bottom edge — left-packed, so today's
/// two players occupy quarters 1–2. Each slot is a solid-background panel outlined in
/// the player's assigned identity color (PlayerPalette) holding the colored name
/// pill, stock dots (→ "N STOCKS" text when dots would overflow), the character's
/// sprite, and a big damage % that ROLLS through interim numbers on hits (growing
/// slightly until the final roll, scaled by hit magnitude). The panel shakes subtly
/// on hits (scaled by current damage, hit player only) and violently — plus a white
/// flash — on a death. Above each slot sits the semi-transparent DEBUG STRIP
/// (default on, toggleable from the pause menu): a human-readable state readout
/// tinted like the body, intangible/invulnerable timing bars, the DI arrow, and the
/// full control layout with per-button move names whose keycaps light up on press —
/// for AI players too, so the strip doubles as a research instrument.
/// </summary>
public partial class HudView : CanvasLayer
{
    private const float PanelHeight = 100f;
    private const float DebugHeight = 62f;
    private const float BottomMargin = 8f;
    private const int MaxStockDots = 8;
    private const float RollSeconds = 0.35f;

    private static readonly string[] KeyNamesKeyboard = { "I", "J", "K", "U", "L" };
    private static readonly string[] KeyNamesPad = { "L1", "X", "A", "Y", "R1" };

    private SimWorld _world = null!;
    private Label _banner = null!;
    private Label _timer = null!;
    private Slot[] _slots = null!;

    public void Setup(SimWorld world, GameGenome genome)
    {
        _world = world;
        // Left-packed quarters (designer): one per player, P1 in quarter 1 — the four
        // static slots finally fill at four players (2026-08-12, four-player.md).
        _slots = new Slot[world.Players.Count];
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i] = new Slot(this, i, genome.Characters[i], genome.Stage.Params, world.Players[i]);
        }

        _banner = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.32f,
            Modulate = new Color(1f, 0.9f, 0.6f),
            Visible = false,
        };
        _banner.AddThemeFontSizeOverride("font_size", 36);
        AddChild(_banner);

        // TIMED mode (2026-08-12): the clock IS the match — show it top center.
        _timer = new Label
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            AnchorLeft = 0f, AnchorRight = 1f, AnchorTop = 0.02f,
            Visible = _world.Config.EndRule == MatchEndRule.Timed,
        };
        _timer.AddThemeFontSizeOverride("font_size", 30);
        _timer.AddThemeColorOverride("font_color", Colors.White);
        _timer.AddThemeColorOverride("font_outline_color", new Color(0f, 0f, 0f, 0.85f));
        _timer.AddThemeConstantOverride("outline_size", 6);
        AddChild(_timer);
    }

    public void Sync(InputFrame[] inputs)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            _slots[i].Sync(_world.Players[i], inputs[i]);
        }

        if (_world.Config.EndRule == MatchEndRule.Timed)
        {
            int ticksLeft = Mathf.Max(0, _world.Config.MaxTicks - _world.TickCount);
            int seconds = ticksLeft / BrawlerSim.SimInfo.TicksPerSecond;
            _timer.Text = $"{seconds / 60}:{seconds % 60:D2}";
        }

        if (_world.IsOver)
        {
            _banner.Visible = true;
            _banner.Text = BannerText();
            _banner.Text += "\nESC — back to menu";
        }
    }

    /// <summary>Match-over banner: the 2P STOCK lines verbatim; TIMED announces the
    /// winner by ranking; 3/4-player STOCK announces the survivor.</summary>
    private string BannerText()
    {
        if (_world.Config.EndRule == MatchEndRule.Timed)
        {
            int[] placements = _world.ComputePlacements();
            for (int i = 0; i < placements.Length; i++)
            {
                if (placements[i] == 1)
                {
                    return $"TIME! {_world.Players[i].Name} WINS ({_world.Players[i].KOs} KOs)";
                }
            }
        }
        if (_world.LoserIndex < 0)
        {
            return "TIME! IT'S A DRAW";
        }
        if (_world.Players.Count > 2)
        {
            foreach (SimPlayer player in _world.Players)
            {
                if (!player.Eliminated)
                {
                    return $"{player.Name} WINS THE GAME";
                }
            }
        }
        return $"{_world.Players[_world.LoserIndex].Name} HAS LOST THE GAME";
    }

    /// <summary>Human-readable state names (FEATURES.md §HUD #3) — machine enums
    /// stay in the sim; the player reads verbs.</summary>
    private static string StateName(SimPlayer player)
    {
        if (player.Eliminated)
        {
            return "ELIMINATED"; // out for good (2026-08-12, STOCK rule)
        }
        if (player.IsRespawning)
        {
            return $"RESPAWNING {player.RespawnBlackoutLeft / BrawlerSim.SimInfo.TicksPerSecond:F1}s";
        }
        if (player.State == PlayerState.Stun && player.StunFromShieldBreak)
        {
            return "SHIELD BROKEN";
        }
        return player.State switch
        {
            PlayerState.Idle => "READY",
            PlayerState.Air => "AIRBORNE",
            PlayerState.AirJumpsExhausted => "EXHAUSTED",
            PlayerState.WarmUp => "WINDING UP",
            PlayerState.Attack => "ATTACKING",
            PlayerState.CoolDown => "RECOVERING",
            PlayerState.Stun => "STUNNED",
            PlayerState.Shield => "SHIELDING",
            PlayerState.Dash => "DASHING",
            PlayerState.Crouch => "CROUCHING",
            _ => player.State.ToString().ToUpperInvariant(),
        };
    }

    private static string MoveAbbrev(CharacterGenome character, int moveIndex)
    {
        MoveType type = character.Moves[moveIndex].Type;
        if (type == MoveType.Attack)
        {
            return $"ATK{moveIndex + 1}";
        }
        return type switch
        {
            MoveType.Shield => "SHLD",
            MoveType.Dash => "DASH",
            MoveType.Projectile => "PROJ",
            _ => type.ToString().ToUpperInvariant(),
        };
    }

    /// <summary>One player's quarter: main panel + debug strip + all animations.</summary>
    private sealed class Slot
    {
        private readonly Color _color;
        private readonly Control _root;       // shaken as a unit
        private readonly PanelContainer _panel = new();
        private readonly Label _name = new();
        private readonly Label _stocks = new();
        private readonly Label _damage = new();
        private readonly ColorRect _deathFlash = new();
        private readonly Control _debug = new();
        private readonly Label _state = new();
        private readonly Label _diArrow = new();
        private readonly Bar _intangible;
        private readonly Bar _invulnerable;
        private readonly Keycap[] _keys;
        private readonly float _spawnPadSeconds;
        private readonly float _spawnInvulnSeconds;

        // Animation state (view-only).
        private float _shownDamage;
        private float _rollFrom;
        private float _rollClock = RollSeconds; // idle
        private float _rollMagnitude;
        private float _shake;
        private float _flash;
        private int _lastHits;
        private int _lastDeaths;
        private bool _wasEliminated;
        private int _clock;
        private readonly bool _timed;

        public Slot(HudView hud, int index, CharacterGenome character,
            BrawlerSim.Params.ParamSet stageParams, SimPlayer player)
        {
            _color = PlayerPalette.Of(index);
            _lastHits = player.TotalHitsReceived;
            _lastDeaths = player.CompletedStockDamage.Count;
            _shownDamage = player.Damage;
            _rollTarget = player.Damage;
            _spawnPadSeconds = StageRules.PlatformSpawnSeconds(stageParams);
            _spawnInvulnSeconds = StageRules.SpawnInvulnSeconds(stageParams);
            _timed = hud._world.Config.EndRule == MatchEndRule.Timed;

            // The static quarter: anchors only — each HUD is ALWAYS 1/4 of the screen.
            _root = new Control
            {
                AnchorLeft = index * 0.25f,
                AnchorRight = (index + 1) * 0.25f,
                AnchorTop = 1f,
                AnchorBottom = 1f,
            };
            hud.AddChild(_root);

            // Main panel: solid background, outline in the identity color.
            _panel.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.13f, 0.13f, 0.17f),
                BorderColor = _color,
                BorderWidthTop = 2, BorderWidthBottom = 2, BorderWidthLeft = 2, BorderWidthRight = 2,
                CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            });
            _panel.AnchorLeft = 0f;
            _panel.AnchorRight = 1f;
            _panel.AnchorTop = 1f;
            _panel.AnchorBottom = 1f;
            _panel.OffsetLeft = 6f;
            _panel.OffsetRight = -6f;
            _panel.OffsetTop = -(BottomMargin + PanelHeight);
            _panel.OffsetBottom = -BottomMargin;
            _root.AddChild(_panel);

            var content = new Control();
            _panel.AddChild(content);

            // Name pill (colored, matches the in-world tag).
            _name.Text = player.Name;
            _name.AddThemeFontSizeOverride("font_size", 15);
            _name.AddThemeStyleboxOverride("normal", new StyleBoxFlat
            {
                BgColor = _color with { A = 0.30f },
                CornerRadiusTopLeft = 999, CornerRadiusTopRight = 999,
                CornerRadiusBottomLeft = 999, CornerRadiusBottomRight = 999,
                ContentMarginLeft = 10f, ContentMarginRight = 10f,
                ContentMarginTop = 2f, ContentMarginBottom = 2f,
            });
            _name.Modulate = new Color(1f, 1f, 1f);
            _name.Position = new Vector2(10f, 8f);
            content.AddChild(_name);

            // Stocks, top-right.
            _stocks.HorizontalAlignment = HorizontalAlignment.Right;
            _stocks.AnchorLeft = 1f;
            _stocks.AnchorRight = 1f;
            _stocks.OffsetLeft = -150f;
            _stocks.OffsetRight = -12f;
            _stocks.OffsetTop = 10f;
            _stocks.AddThemeFontSizeOverride("font_size", 15);
            _stocks.Modulate = new Color(0.95f, 0.95f, 1f);
            content.AddChild(_stocks);

            // Character sprite (so you know which player is which).
            var sprite = new TextureRect
            {
                Texture = SpriteBank.Player(character.SpriteIndex),
                TextureFilter = CanvasItem.TextureFilterEnum.Nearest,
                StretchMode = TextureRect.StretchModeEnum.KeepAspectCentered,
                ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
                Position = new Vector2(14f, 38f),
                Size = new Vector2(52f, 52f),
            };
            content.AddChild(sprite);

            // Big damage %, center-right.
            _damage.HorizontalAlignment = HorizontalAlignment.Center;
            _damage.AnchorLeft = 0.25f;
            _damage.AnchorRight = 1f;
            _damage.OffsetTop = 36f;
            _damage.OffsetBottom = PanelHeight - 10f;
            _damage.AddThemeFontSizeOverride("font_size", 34);
            _damage.Modulate = new Color(0.98f, 0.96f, 0.9f);
            content.AddChild(_damage);

            // Death flash overlay.
            _deathFlash.Color = new Color(1f, 1f, 1f, 0f);
            _deathFlash.AnchorRight = 1f;
            _deathFlash.AnchorBottom = 1f;
            _deathFlash.MouseFilter = Control.MouseFilterEnum.Ignore;
            _panel.AddChild(_deathFlash);

            // Debug strip (semi-transparent) above the panel.
            _debug.AnchorLeft = 0f;
            _debug.AnchorRight = 1f;
            _debug.AnchorTop = 1f;
            _debug.AnchorBottom = 1f;
            _debug.OffsetLeft = 6f;
            _debug.OffsetRight = -6f;
            _debug.OffsetTop = -(BottomMargin + PanelHeight + 4f + DebugHeight);
            _debug.OffsetBottom = -(BottomMargin + PanelHeight + 4f);
            _root.AddChild(_debug);

            var debugBg = new PanelContainer();
            debugBg.AddThemeStyleboxOverride("panel", new StyleBoxFlat
            {
                BgColor = new Color(0.09f, 0.09f, 0.12f, 0.55f),
                CornerRadiusTopLeft = 8, CornerRadiusTopRight = 8,
                CornerRadiusBottomLeft = 8, CornerRadiusBottomRight = 8,
            });
            debugBg.AnchorRight = 1f;
            debugBg.AnchorBottom = 1f;
            _debug.AddChild(debugBg);

            _state.Position = new Vector2(10f, 4f);
            _state.AddThemeFontSizeOverride("font_size", 14);
            _debug.AddChild(_state);

            _diArrow.AnchorLeft = 1f;
            _diArrow.AnchorRight = 1f;
            _diArrow.OffsetLeft = -30f;
            _diArrow.OffsetRight = -8f;
            _diArrow.OffsetTop = 2f;
            _diArrow.AddThemeFontSizeOverride("font_size", 16);
            _debug.AddChild(_diArrow);

            _intangible = new Bar(_debug, "INTG", new Color(1f, 1f, 1f), new Vector2(150f, 8f));
            _invulnerable = new Bar(_debug, "INVL", new Color(0.75f, 0.85f, 1f), new Vector2(150f, 20f));

            // Control layout: jump + the five action buttons, move names attached,
            // keycaps highlight on press (agent presses included).
            string[] keys = index == 0 ? KeyNamesKeyboard : KeyNamesPad;
            string jumpKey = index == 0 ? "SPC" : "B";
            _keys = new Keycap[keys.Length + 1];
            float x = 8f;
            _keys[0] = new Keycap(_debug, jumpKey, "JUMP", new Vector2(x, DebugHeight - 34f));
            x += 52f;
            for (int b = 0; b < keys.Length; b++)
            {
                _keys[b + 1] = new Keycap(_debug, keys[b],
                    MoveAbbrev(character, character.ButtonMoves[b]), new Vector2(x, DebugHeight - 34f));
                x += 52f;
            }
        }

        public void Sync(SimPlayer player, InputFrame input)
        {
            _clock++;
            float dt = 1f / 60f;

            // Percent roll (mockup: roll through interim numbers, grow slightly
            // until the final roll, scale with hit magnitude — hit player only).
            if (player.Damage != _rollTarget)
            {
                _rollMagnitude = Mathf.Clamp(Mathf.Abs(player.Damage - _shownDamage) / 25f, 0.15f, 1f);
                _rollFrom = _shownDamage;
                _rollTarget = player.Damage;
                _rollClock = 0f;
            }
            if (_rollClock < RollSeconds)
            {
                _rollClock = Mathf.Min(RollSeconds, _rollClock + dt);
                float t = _rollClock / RollSeconds;
                _shownDamage = Mathf.Lerp(_rollFrom, _rollTarget, t);
                // Grow during the roll, snap back on the final number.
                float grow = t < 1f ? 1f + 0.25f * _rollMagnitude * Mathf.Sin(t * Mathf.Pi) : 1f;
                _damage.Scale = new Vector2(grow, grow);
                _damage.PivotOffset = _damage.Size / 2f;
            }
            else
            {
                _shownDamage = player.Damage;
                _damage.Scale = Vector2.One;
            }
            _damage.Text = $"{_shownDamage:F1}%";

            // Hit shake (subtle, damage-scaled) and death shake + flash (major).
            // Deaths are read from the per-life ledger (2026-08-12): stock decrements
            // fill it exactly as before, TIMED-mode deaths fill it with stocks
            // untouched, and an elimination is the final death.
            if (player.TotalHitsReceived != _lastHits)
            {
                _lastHits = player.TotalHitsReceived;
                _shake = Mathf.Max(_shake, Mathf.Clamp(1.5f + player.Damage * 0.03f, 1.5f, 6f));
            }
            if (player.CompletedStockDamage.Count != _lastDeaths
                || (player.Eliminated && !_wasEliminated))
            {
                _lastDeaths = player.CompletedStockDamage.Count;
                _wasEliminated = player.Eliminated;
                _shake = 14f;
                _flash = 0.85f;
            }
            _shake *= 0.86f;
            _flash *= 0.88f;
            // Shake via anchor OFFSETS — setting Control.Position would override the
            // quarter anchors and relocate the slot to the parent origin.
            Vector2 jolt = _shake > 0.3f
                ? new Vector2(
                    Mathf.Sin(_clock * 2.1f) * _shake,
                    Mathf.Cos(_clock * 2.7f) * _shake * 0.6f)
                : Vector2.Zero;
            _root.OffsetLeft = jolt.X;
            _root.OffsetRight = jolt.X;
            _root.OffsetTop = jolt.Y;
            _root.OffsetBottom = jolt.Y;
            _deathFlash.Color = new Color(1f, 1f, 1f, _flash > 0.03f ? _flash : 0f);

            // Stocks: dots until they no longer fit, then a count. TIMED mode
            // (2026-08-12) has infinite stocks — the score is the KO count.
            _stocks.Text = _timed
                ? $"{player.KOs} KOs"
                : player.Eliminated
                    ? "OUT"
                    : player.Stocks <= MaxStockDots
                        ? string.Join(" ", System.Linq.Enumerable.Repeat("●", System.Math.Max(0, player.Stocks)))
                        : $"{player.Stocks} STOCKS";
            // An eliminated player's quarter dims — still readable, clearly done.
            _panel.Modulate = player.Eliminated
                ? new Color(0.55f, 0.55f, 0.6f) : Colors.White;

            // Debug strip.
            _debug.Visible = AppSettings.DebugPanelEnabled;
            if (!_debug.Visible)
            {
                return;
            }
            _state.Text = StateName(player);
            _state.Modulate = player.Eliminated || player.IsRespawning
                ? new Color(0.8f, 0.8f, 0.85f)
                : PlayerView.StateColor(player.State);

            float fps = BrawlerSim.SimInfo.TicksPerSecond;
            _intangible.Sync(player.SpawnIntangible && _spawnPadSeconds > 0f
                ? player.SpawnPadTicksLeft / fps / _spawnPadSeconds : 0f);
            _invulnerable.Sync(player.SpawnInvulnTicksLeft > 0 && _spawnInvulnSeconds > 0f
                ? player.SpawnInvulnTicksLeft / fps / _spawnInvulnSeconds : 0f);

            int dx = System.Math.Sign(player.HeldDirection.X);
            int dy = System.Math.Sign(player.HeldDirection.Y);
            _diArrow.Text = (dx, dy) switch
            {
                (0, 0) => "·",
                (1, 0) => "→", (-1, 0) => "←", (0, 1) => "↑", (0, -1) => "↓",
                (1, 1) => "↗", (-1, 1) => "↖", (1, -1) => "↘", (-1, -1) => "↙",
                _ => "·",
            };
            _diArrow.Modulate = new Color(0.6f, 0.65f, 0.72f);

            _keys[0].Sync(input.Jump);
            for (int b = 0; b < _keys.Length - 1; b++)
            {
                _keys[b + 1].Sync(input.ActionPressed(b));
            }
        }

        private float _rollTarget;
    }

    /// <summary>A labelled timing bar (intangible/invulnerable) — hidden at zero.</summary>
    private sealed class Bar
    {
        private readonly Label _caption = new();
        private readonly ColorRect _track = new();
        private readonly ColorRect _fill = new();
        private const float Width = 96f;

        public Bar(Control parent, string caption, Color color, Vector2 position)
        {
            _caption.Text = caption;
            _caption.AddThemeFontSizeOverride("font_size", 10);
            _caption.Position = position + new Vector2(0f, -3f);
            _caption.Modulate = new Color(0.7f, 0.75f, 0.8f);
            parent.AddChild(_caption);

            _track.Color = new Color(1f, 1f, 1f, 0.12f);
            _track.Position = position + new Vector2(34f, 0f);
            _track.Size = new Vector2(Width, 6f);
            parent.AddChild(_track);

            _fill.Color = color with { A = 0.9f };
            _fill.Position = _track.Position;
            _fill.Size = new Vector2(0f, 6f);
            parent.AddChild(_fill);
        }

        public void Sync(float fraction)
        {
            bool visible = fraction > 0f;
            _caption.Visible = visible;
            _track.Visible = visible;
            _fill.Visible = visible;
            _fill.Size = new Vector2(Width * Mathf.Clamp(fraction, 0f, 1f), 6f);
        }
    }

    /// <summary>A mini keycap + its move name; lights up while pressed.</summary>
    private sealed class Keycap
    {
        private readonly Label _key = new();
        private readonly Label _move = new();
        private readonly StyleBoxFlat _style;
        private int _flash;

        public Keycap(Control parent, string key, string move, Vector2 position)
        {
            _style = new StyleBoxFlat
            {
                BgColor = new Color(1f, 1f, 1f, 0.10f),
                CornerRadiusTopLeft = 4, CornerRadiusTopRight = 4,
                CornerRadiusBottomLeft = 4, CornerRadiusBottomRight = 4,
                ContentMarginLeft = 6f, ContentMarginRight = 6f,
                ContentMarginTop = 1f, ContentMarginBottom = 1f,
            };
            _key.Text = key;
            _key.HorizontalAlignment = HorizontalAlignment.Center;
            _key.AddThemeFontSizeOverride("font_size", 12);
            _key.AddThemeStyleboxOverride("normal", _style);
            _key.Position = position;
            _key.CustomMinimumSize = new Vector2(40f, 18f);
            parent.AddChild(_key);

            _move.Text = move;
            _move.HorizontalAlignment = HorizontalAlignment.Center;
            _move.AddThemeFontSizeOverride("font_size", 10);
            _move.Modulate = new Color(0.65f, 0.7f, 0.78f);
            _move.Position = position + new Vector2(0f, 19f);
            _move.CustomMinimumSize = new Vector2(40f, 12f);
            parent.AddChild(_move);
        }

        public void Sync(bool pressed)
        {
            if (pressed)
            {
                _flash = 8;
            }
            _flash = System.Math.Max(0, _flash - 1);
            bool lit = _flash > 0;
            _style.BgColor = lit ? new Color(1f, 1f, 1f, 0.45f) : new Color(1f, 1f, 1f, 0.10f);
            _key.Modulate = lit ? new Color(0.09f, 0.09f, 0.12f) : new Color(0.9f, 0.92f, 0.98f);
        }
    }
}
