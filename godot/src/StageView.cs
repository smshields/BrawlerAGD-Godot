using Godot;
using System.Text.Json;
using BrawlerSim.Sim;
using SimAabb = BrawlerSim.Sim.Aabb;

namespace BrawlerGodot;

/// <summary>
/// Renders platforms as tiles, porting the Unity LevelLoader exactly: each platform is
/// an integer cell rect filled from a 3×3 tile block (Q W E / A S D / Z X C — corners,
/// edges, fill), using the same nine 16 px Kenney tiles the Unity scene had wired up
/// (rects extracted from the tilemap sheet's .meta). Also draws a faint blast-zone
/// boundary. Purely cosmetic — collision lives in the sim.
/// </summary>
public partial class StageView : Node2D
{
    private SimWorld _world = null!;
    private float _ppu;
    private Texture2D _tiles = null!;
    private readonly System.Collections.Generic.Dictionary<string, Rect2> _tileRects = new();

    public void Setup(SimWorld world, float ppu)
    {
        _world = world;
        _ppu = ppu;
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
            if (_world.PlatformThin[i])
            {
                // Thin platform (2026-09-01, FEATURES.md §Thin Platforms): a solid
                // white PLACEHOLDER slab exactly on the collision slice — the tile
                // treatment is the next feature ("a different slice of tilesets").
                DrawThinPlatform(_world.Platforms[i]);
            }
            else
            {
                DrawPlatform(_world.Platforms[i]);
            }
        }
        // The blast boundary is deliberately NOT drawn (2026-07-21): pre-camera it sat
        // off-screen by construction; the zooming camera can now reach the KO box on
        // small maps, and hidden off-screen death is an intentional design rule.
    }

    /// <summary>Placeholder slab for a drop-through platform: the sim's thin top
    /// slice, flat white with a subtly darker underside (the spawn-pill vocabulary:
    /// bright top edge = standable surface).</summary>
    private void DrawThinPlatform(in SimAabb slice)
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
    /// (top row wins on 1-tall platforms; right edge wins on 1-wide ones).</summary>
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
