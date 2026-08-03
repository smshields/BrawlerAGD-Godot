using Godot;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>
/// The evolve dashboard's fitness chart. Lines: top and average fitness per
/// generation. Since the Evolution Explorer (2026-07-27, designer) each generation
/// also plots a POINT per game (its fitness score); clicking a point selects that
/// exact genome for the live preview + ADD TO GAMES basket. Selection is a plain C#
/// event (a GameGenome is not a Variant). Points below the line range clamp to the
/// chart's bottom edge, drawn fainter. Dense populations subsample the DRAWN dots
/// (hit-testing still covers every game).
/// </summary>
public partial class FitnessChart : Control
{
    private const float HitRadiusPx = 12f;

    private readonly System.Collections.Generic.List<float> _top = new();
    private readonly System.Collections.Generic.List<float> _average = new();
    private readonly System.Collections.Generic.List<float[]> _scores = new();
    private readonly System.Collections.Generic.List<GameGenome[]> _genomes = new();
    private (int Gen, int Index)? _selected;

    /// <summary>(generation, index in population, fitness, genome) of a clicked point.</summary>
    public System.Action<int, int, float, GameGenome>? PointSelected;

    public void AddGeneration(float top, float average, float[] scores, GameGenome[] genomes)
    {
        _top.Add(top);
        _average.Add(average);
        _scores.Add(scores);
        _genomes.Add(genomes);
        QueueRedraw();
    }

    public void Clear()
    {
        _top.Clear();
        _average.Clear();
        _scores.Clear();
        _genomes.Clear();
        _selected = null;
        QueueRedraw();
    }

    /// <summary>Programmatic selection (automation + the run-finished convenience
    /// that focuses the final best game).</summary>
    public void Select(int gen, int index)
    {
        if (gen < 0 || gen >= _scores.Count || index < 0 || index >= _scores[gen].Length)
        {
            return;
        }
        _selected = (gen, index);
        QueueRedraw();
        PointSelected?.Invoke(gen, index, _scores[gen][index], _genomes[gen][index]);
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            return;
        }
        if (FindNearestPoint(click.Position) is { } hit)
        {
            AcceptEvent();
            Select(hit.Gen, hit.Index);
        }
    }

    /// <summary>Nearest game point within the hit radius — tests EVERY game, not just
    /// the subsampled drawn dots.</summary>
    private (int Gen, int Index)? FindNearestPoint(Vector2 mouse)
    {
        if (_top.Count < 2)
        {
            return null;
        }
        (float min, float max) = Range();
        Vector2 size = Size;
        (int, int)? best = null;
        float bestDistSq = HitRadiusPx * HitRadiusPx;
        for (int gen = 0; gen < _scores.Count; gen++)
        {
            float x = XOf(gen, size);
            if (Mathf.Abs(x - mouse.X) > HitRadiusPx)
            {
                continue;
            }
            float[] scores = _scores[gen];
            for (int i = 0; i < scores.Length; i++)
            {
                float y = MapY(Mathf.Clamp(scores[i], min, max), min, max, size.Y);
                float distSq = (x - mouse.X) * (x - mouse.X) + (y - mouse.Y) * (y - mouse.Y);
                if (distSq < bestDistSq)
                {
                    bestDistSq = distSq;
                    best = (gen, i);
                }
            }
        }
        return best;
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0.05f, 0.08f));
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.35f, 0.4f, 0.5f), filled: false);

        Font font = ThemeDB.FallbackFont;
        if (_top.Count < 2)
        {
            DrawString(font, new Vector2(12f, 24f), "fitness chart — waiting for generations…",
                HorizontalAlignment.Left, -1f, 14, new Color(0.5f, 0.55f, 0.65f));
            return;
        }

        (float min, float max) = Range();

        // Zero line for orientation, when in range.
        if (min < 0f && max > 0f)
        {
            float zeroY = MapY(0f, min, max, size.Y);
            DrawLine(new Vector2(0f, zeroY), new Vector2(size.X, zeroY), new Color(0.3f, 0.3f, 0.38f));
        }

        // Per-game points, under the lines. Subsample drawing on dense populations;
        // clamped-to-range points render fainter (they sit below the line window).
        for (int gen = 0; gen < _scores.Count; gen++)
        {
            float x = XOf(gen, size);
            float[] scores = _scores[gen];
            int stride = System.Math.Max(1, scores.Length / 40);
            for (int i = 0; i < scores.Length; i += stride)
            {
                bool clamped = scores[i] < min || scores[i] > max;
                float y = MapY(Mathf.Clamp(scores[i], min, max), min, max, size.Y);
                DrawRect(new Rect2(x - 1f, y - 1f, 2f, 2f),
                    new Color(0.55f, 0.62f, 0.75f, clamped ? 0.12f : 0.35f));
            }
        }

        DrawSeries(_average, min, max, size, new Color(0.55f, 0.65f, 0.9f));
        DrawSeries(_top, min, max, size, new Color(0.45f, 0.9f, 0.55f));

        // Selected point: bright ring, always drawn even if it fell in a stride gap.
        if (_selected is { } sel && sel.Gen < _scores.Count)
        {
            float score = _scores[sel.Gen][sel.Index];
            float x = XOf(sel.Gen, size);
            float y = MapY(Mathf.Clamp(score, min, max), min, max, size.Y);
            DrawRect(new Rect2(x - 2f, y - 2f, 4f, 4f), new Color(1f, 0.85f, 0.3f));
            DrawArc(new Vector2(x, y), 7f, 0f, Mathf.Tau, 24, new Color(1f, 0.85f, 0.3f), 1.5f, antialiased: true);
        }

        DrawString(font, new Vector2(12f, 22f), $"top {_top[^1]:F1}", HorizontalAlignment.Left, -1f, 14, new Color(0.45f, 0.9f, 0.55f));
        DrawString(font, new Vector2(12f, 42f), $"avg {_average[^1]:F1}", HorizontalAlignment.Left, -1f, 14, new Color(0.55f, 0.65f, 0.9f));
        DrawString(font, new Vector2(size.X - 90f, size.Y - 10f), $"gen {_top.Count - 1}",
            HorizontalAlignment.Left, -1f, 14, new Color(0.5f, 0.55f, 0.65f));
        if (_scores.Count > 0 && _scores[^1].Length > 0)
        {
            DrawString(font, new Vector2(12f, size.Y - 28f), "click a point to preview that game",
                HorizontalAlignment.Left, -1f, 12, new Color(0.45f, 0.5f, 0.6f));
        }
    }

    /// <summary>Chart range comes from the LINES (top/avg): early-generation stragglers
    /// can score hundreds below and would flatten the curves; their points clamp.</summary>
    private (float Min, float Max) Range()
    {
        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in _top) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
        foreach (float v in _average) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
        if (max - min < 1e-3f) { max = min + 1f; }
        return (min, max);
    }

    private float XOf(int gen, Vector2 size) =>
        size.X * gen / System.Math.Max(1, _top.Count - 1);

    private void DrawSeries(System.Collections.Generic.List<float> series, float min, float max, Vector2 size, Color color)
    {
        var points = new Vector2[series.Count];
        for (int i = 0; i < series.Count; i++)
        {
            points[i] = new Vector2(
                size.X * i / (series.Count - 1),
                MapY(series[i], min, max, size.Y));
        }
        DrawPolyline(points, color, 2f);
    }

    private static float MapY(float value, float min, float max, float height) =>
        height - (value - min) / (max - min) * (height - 16f) - 8f;
}
