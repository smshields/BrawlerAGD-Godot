using Godot;

namespace BrawlerGodot;

/// <summary>Minimal line chart: top and average fitness per generation.</summary>
public partial class FitnessChart : Control
{
    private readonly System.Collections.Generic.List<float> _top = new();
    private readonly System.Collections.Generic.List<float> _average = new();

    public void AddPoint(float top, float average)
    {
        _top.Add(top);
        _average.Add(average);
        QueueRedraw();
    }

    public void Clear()
    {
        _top.Clear();
        _average.Clear();
        QueueRedraw();
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

        float min = float.MaxValue, max = float.MinValue;
        foreach (float v in _top) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
        foreach (float v in _average) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); }
        if (max - min < 1e-3f) { max = min + 1f; }

        // Zero line for orientation, when in range.
        if (min < 0f && max > 0f)
        {
            float zeroY = MapY(0f, min, max, size.Y);
            DrawLine(new Vector2(0f, zeroY), new Vector2(size.X, zeroY), new Color(0.3f, 0.3f, 0.38f));
        }

        DrawSeries(_average, min, max, size, new Color(0.55f, 0.65f, 0.9f));
        DrawSeries(_top, min, max, size, new Color(0.45f, 0.9f, 0.55f));

        DrawString(font, new Vector2(12f, 22f), $"top {_top[^1]:F1}", HorizontalAlignment.Left, -1f, 14, new Color(0.45f, 0.9f, 0.55f));
        DrawString(font, new Vector2(12f, 42f), $"avg {_average[^1]:F1}", HorizontalAlignment.Left, -1f, 14, new Color(0.55f, 0.65f, 0.9f));
        DrawString(font, new Vector2(size.X - 90f, size.Y - 10f), $"gen {_top.Count - 1}",
            HorizontalAlignment.Left, -1f, 14, new Color(0.5f, 0.55f, 0.65f));
    }

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
