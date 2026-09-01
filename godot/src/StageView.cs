using Godot;
using System;
using System.Text.Json;
using BrawlerSim.Serialization;
using BrawlerSim.Sim;
using BrawlerSim.Sprites;
using SimAabb = BrawlerSim.Sim.Aabb;
using StageGenome = BrawlerSim.Genome.StageGenome;
using StageRules = BrawlerSim.Genome.StageRules;

namespace BrawlerGodot;

/// <summary>
/// Renders platforms. TWO generations coexist (M4d, 2026-09-01 —
/// docs/features/stage-tile-selection.md):
///
/// - v2 (themed stages — a ThemeId gene / presented theme): the tiles_v2 library's
///   16-piece EXPOSURE contract — key = vertical state {T,M,B,S} × horizontal state
///   {L,M,R,S}, S = both sides exposed — a pure function of the footprint with no
///   precedence quirks, so 1-wide columns, 1×1 blocks, and 1-tall rows finally read
///   correctly. Each piece carries 1-4 hand-drawn VARIANTS; the renderer picks one
///   per cell from a deterministic hash (no RNG state, adjacent cells stop
///   repeating). Thin platforms draw the theme's dropTiles slab row on the collision
///   slice; PROPS (cosmetic, 0-2 per solid platform, seeded from the stage's naming
///   seed, never over spawn points) sit bottom-aligned on the surface, dimmed toward
///   the background so they can never be read as fighters (designer 2026-09-01).
///
/// - v1 (null-theme stages — every pre-v13 file): the Kenney 9-piece port of Unity
///   LevelLoader, byte-for-byte, including its precedence quirks; thin platforms
///   keep the white placeholder slab. Purely cosmetic — collision lives in the sim.
/// </summary>
public partial class StageView : Node2D
{
    private SimWorld _world = null!;
    private float _ppu;
    private Texture2D _tiles = null!;
    private readonly System.Collections.Generic.Dictionary<string, Rect2> _tileRects = new();

    // v2 theming (M4d): resolved once at Setup; null = the v1 path.
    private ThemeDef? _theme;
    private StageGenome? _stage;
    private ulong _propSeed;

    /// <summary>Dim modulate for props — background elements by decree (designer
    /// 2026-09-01): well below the lit tile caps and the bright, outlined fighters.</summary>
    private static readonly Color PropTint = new(0.62f, 0.62f, 0.68f);

    public void Setup(SimWorld world, float ppu, StageGenome? stage = null)
    {
        _world = world;
        _ppu = ppu;
        _stage = stage;
        _theme = stage?.ThemeId is { } themeId ? ThemeBank.Library.ByName(themeId) : null;
        _propSeed = stage is not null && _theme is not null
            ? BuiltGameNaming.NamingSeed(stage)
            : 0UL;
        TextureFilter = TextureFilterEnum.Nearest;

        _tiles = GD.Load<Texture2D>("res://assets/tiles.png");
        using JsonDocument doc = JsonDocument.Parse(FileAccess.GetFileAsString("res://assets/tiles_slices.json"));
        foreach (JsonProperty tile in doc.RootElement.GetProperty("tiles").EnumerateObject())
        {
            JsonElement r = tile.Value;
            _tileRects[tile.Name] = new Rect2(
                r[0].GetSingle(), r[1].GetSingle(), r[2].GetSingle(), r[3].GetSingle());
        }
        QueueRedraw();
    }

    public override void _Draw()
    {
        if (_world == null)
        {
            return;
        }

        for (int i = 0; i < _world.Platforms.Count; i++)
        {
            if (_theme is not null)
            {
                DrawThemedPlatform(i);
            }
            else if (_world.PlatformThin[i])
            {
                DrawThinPlaceholder(_world.Platforms[i]);
            }
            else
            {
                DrawPlatform(_world.Platforms[i]);
            }
        }
        if (_theme is not null && _stage is not null)
        {
            DrawProps();
        }
        // The blast boundary is deliberately NOT drawn (2026-07-21): pre-camera it sat
        // off-screen by construction; the zooming camera can now reach the KO box on
        // small maps, and hidden off-screen death is an intentional design rule.
    }

    // ── v2: the themed exposure-contract renderer (M4d) ─────────────────────────

    private void DrawThemedPlatform(int platformIndex)
    {
        SimAabb box = _world.Platforms[platformIndex];
        if (_world.PlatformThin[platformIndex])
        {
            // Drop-through slab: the theme's dropTiles row, drawn exactly on the
            // collision slice (one cell of art per gene cell of width).
            int cols = Mathf.RoundToInt(box.Right - box.Left);
            int x0 = Mathf.RoundToInt(box.Left);
            for (int cx = 0; cx < cols; cx++)
            {
                string key = cols == 1 ? "S" : cx == 0 ? "L" : cx == cols - 1 ? "R" : "M";
                TileRect rect = Variant(_theme!.DropTiles[key], cx, 0, platformIndex);
                float sliceHeight = box.Top - box.Bottom;
                var screen = new Rect2(
                    (x0 + cx) * _ppu, -box.Top * _ppu, _ppu, sliceHeight * _ppu);
                DrawTextureRectRegion(ThemeBank.Tiles, screen, ToRect2(rect));
            }
            return;
        }

        // Solid platform: exposure mask over the integer cell grid (the sim box IS
        // the gene rect for solid platforms).
        int gx0 = Mathf.RoundToInt(box.Left);
        int gy0 = Mathf.RoundToInt(box.Bottom);
        int gcols = Mathf.RoundToInt(box.Right - box.Left);
        int grows = Mathf.RoundToInt(box.Top - box.Bottom);
        for (int cy = 0; cy < grows; cy++)
        {
            // cy = 0 is the bottom row; the vertical key is T at the top edge.
            string vk = grows == 1 ? "S" : cy == grows - 1 ? "T" : cy == 0 ? "B" : "M";
            for (int cx = 0; cx < gcols; cx++)
            {
                string hk = gcols == 1 ? "S" : cx == 0 ? "L" : cx == gcols - 1 ? "R" : "M";
                TileRect rect = Variant(_theme!.Tiles[vk + hk], cx, cy, platformIndex);
                var screen = new Rect2((gx0 + cx) * _ppu, -(gy0 + cy + 1) * _ppu, _ppu, _ppu);
                DrawTextureRectRegion(ThemeBank.Tiles, screen, ToRect2(rect));
            }
        }
    }

    /// <summary>Deterministic per-cell variant pick — the brief's
    /// hash(cx, cy, platformIndex) % variants: no RNG state, stable across frames,
    /// adjacent cells stop repeating. View-only, so the mix function just has to be
    /// fixed, not part of any sim contract.</summary>
    private static TileRect Variant(
        System.Collections.Generic.IReadOnlyList<TileRect> variants, int cx, int cy, int platformIndex)
    {
        if (variants.Count == 1)
        {
            return variants[0];
        }
        uint hash = (uint)(cx * 73856093) ^ (uint)(cy * 19349663) ^ (uint)(platformIndex * 83492791);
        return variants[(int)(hash % (uint)variants.Count)];
    }

    /// <summary>Props (cosmetic, no collision): 0-2 per SOLID platform at least 3
    /// cells wide, bottom-aligned on the top surface, seeded from the stage's naming
    /// seed, never over a spawn point, dimmed to read as background.</summary>
    private void DrawProps()
    {
        if (_theme!.Props.Count == 0)
        {
            return;
        }
        Span<float> spawnXs = stackalloc float[4];
        for (int i = 0; i < 4; i++)
        {
            spawnXs[i] = StageRules.SpawnOf(_stage!.Params, i).X;
        }

        for (int p = 0; p < _world.Platforms.Count; p++)
        {
            SimAabb box = _world.Platforms[p];
            int cols = Mathf.RoundToInt(box.Right - box.Left);
            if (_world.PlatformThin[p] || cols < 3)
            {
                continue;
            }
            // A tiny deterministic stream per platform (SplitMix-style mixing of the
            // stage seed and the platform index) — view-only.
            ulong state = _propSeed ^ (0x9E3779B97F4A7C15UL * (ulong)(p + 1));
            int count = (int)(Mix(ref state) % 3); // 0, 1, or 2 props
            for (int k = 0; k < count; k++)
            {
                ThemeProp prop = _theme.Props[(int)(Mix(ref state) % (uint)_theme.Props.Count)];
                int cell = 1 + (int)(Mix(ref state) % (uint)(cols - 2)); // never the corner caps
                float propX = box.Left + cell + 0.5f;
                bool overSpawn = false;
                foreach (float sx in spawnXs)
                {
                    overSpawn |= Mathf.Abs(propX - sx) < 1f;
                }
                if (overSpawn)
                {
                    continue;
                }
                float w = prop.Rect.W / (float)ThemeBank.Library.TileSize;
                float h = prop.Rect.H / (float)ThemeBank.Library.TileSize;
                var screen = new Rect2(
                    (propX - w / 2f) * _ppu, -(box.Top + h) * _ppu, w * _ppu, h * _ppu);
                DrawTextureRectRegion(ThemeBank.Tiles, screen, ToRect2(prop.Rect), PropTint);
            }
        }
    }

    private static uint Mix(ref ulong state)
    {
        state += 0x9E3779B97F4A7C15UL;
        ulong z = state;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        return (uint)(z >> 33);
    }

    private static Rect2 ToRect2(in TileRect r) => new(r.X, r.Y, r.W, r.H);

    // ── v1: the Kenney legacy path (pre-v13 stages), untouched ──────────────────

    /// <summary>Placeholder slab for a drop-through platform on a THEME-LESS stage:
    /// the sim's thin top slice, flat white with a subtly darker underside (the
    /// spawn-pill vocabulary: bright top edge = standable surface).</summary>
    private void DrawThinPlaceholder(in SimAabb slice)
    {
        var rect = new Rect2(
            slice.Left * _ppu, -slice.Top * _ppu,
            (slice.Right - slice.Left) * _ppu, (slice.Top - slice.Bottom) * _ppu);
        DrawRect(rect, new Color(0.9f, 0.9f, 0.94f));
        float edge = Mathf.Max(1f, rect.Size.Y * 0.25f);
        DrawRect(new Rect2(rect.Position, new Vector2(rect.Size.X, edge)), Colors.White);
        DrawRect(new Rect2(rect.Position + new Vector2(0f, rect.Size.Y - edge),
            new Vector2(rect.Size.X, edge)), new Color(0.62f, 0.62f, 0.7f));
    }

    private void DrawPlatform(in SimAabb platform)
    {
        // Platforms come from integer PlatformGenes; recover the cell grid.
        int x0 = Mathf.RoundToInt(platform.Left);
        int y0 = Mathf.RoundToInt(platform.Bottom);
        int cols = Mathf.RoundToInt(platform.Right - platform.Left);
        int rows = Mathf.RoundToInt(platform.Top - platform.Bottom);

        for (int cy = 0; cy < rows; cy++)
        {
            for (int cx = 0; cx < cols; cx++)
            {
                string tile = TileFor(cx, cy, cols, rows);
                var screen = new Rect2((x0 + cx) * _ppu, -(y0 + cy + 1) * _ppu, _ppu, _ppu);
                DrawTextureRectRegion(_tiles, screen, _tileRects[tile]);
            }
        }
    }

    /// <summary>Unity LevelLoader.GetTile parity, including its precedence quirks
    /// (top row wins on 1-tall platforms; right edge wins on 1-wide ones). Kept for
    /// legacy (null-theme) stages ONLY — themed stages use the 16-piece exposure
    /// contract above, which has no such quirks.</summary>
    private static string TileFor(int cx, int cy, int cols, int rows)
    {
        if (cy == rows - 1)
        {
            return cx == cols - 1 ? "E" : (cx == 0 ? "Q" : "W");
        }
        if (cy == 0)
        {
            return cx == cols - 1 ? "C" : (cx == 0 ? "Z" : "X");
        }
        return cx == cols - 1 ? "D" : (cx == 0 ? "A" : "S");
    }
}
