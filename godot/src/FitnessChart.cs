using System.Collections.Generic;
using Godot;
using BrawlerSim.Genome;

namespace BrawlerGodot;

/// <summary>
/// The evolve dashboard's fitness chart.
///
/// LIVE AND TIME-BASED since 2026-09-16 (designer): the x axis is elapsed RUN TIME,
/// not generation index, and each game's dot appears the moment that game finishes
/// evaluating rather than in a block when the generation ends. A game that took ten
/// times as long lands ten times further right, so the spacing of the dots IS the
/// pacing of the run — a slow generation reads as a sparse stretch, a fast one as a
/// tight cluster, and there is never a blank chart waiting on a generation. Vertical
/// dividers mark generation boundaries; the top/avg lines connect at those times.
///
/// Everything that moves, eases. New dots fade in, and the y range slides toward its
/// target rather than snapping when a straggler widens it — a chart that jumps is the
/// thing this replaced.
///
/// Clicking a dot selects that exact genome for the live preview + ADD TO GAMES
/// (selection is a plain C# event — a GameGenome is not a Variant). Dots outside the
/// line range clamp to the edge and draw fainter. Dense runs subsample the DRAWN dots,
/// never the recent ones and never the hit test.
/// </summary>
public partial class FitnessChart : Control
{
    private const float HitRadiusPx = 12f;
    private const float DotRadiusPx = 2.6f;
    private const float FadeSeconds = 0.45f;

    /// <summary>Above this the drawn dots thin out — UNIFORMLY. An earlier version
    /// exempted the most recent generations so the live edge stayed crisp, and the
    /// density step where the exemption ended read as an event in the run that never
    /// happened. In an instrument, apparent density has to mean what it looks like it
    /// means, so the whole chart thins together or not at all.</summary>
    private const int MaxDrawnDots = 20_000;

    private readonly struct Dot
    {
        public Dot(int generation, int index, float score, float time, float appearedAt, GameGenome genome)
        {
            Generation = generation;
            Index = index;
            Score = score;
            Time = time;
            AppearedAt = appearedAt;
            Genome = genome;
        }

        public int Generation { get; }
        public int Index { get; }
        public float Score { get; }

        /// <summary>Seconds into the run at which this game finished evaluating.</summary>
        public float Time { get; }

        /// <summary>Chart clock when it was added — drives the fade-in.</summary>
        public float AppearedAt { get; }

        public GameGenome Genome { get; }
    }

    private readonly struct GenerationMark
    {
        public GenerationMark(int generation, float top, float average, float endTime)
        {
            Generation = generation;
            Top = top;
            Average = average;
            EndTime = endTime;
        }

        public int Generation { get; }
        public float Top { get; }
        public float Average { get; }
        public float EndTime { get; }
    }

    private readonly List<Dot> _dots = new();
    private readonly List<GenerationMark> _marks = new();

    private float _clock;

    /// <summary>How far the newest line segment has been drawn, 0..1. Reset when a
    /// generation closes and eased back to 1 over ~0.9 s, so the trend lines advance
    /// continuously instead of gaining a vertex per generation.</summary>
    private float _lineGrowth = 1f;
    private const float LineGrowthSeconds = 0.9f;

    private float LineGrowth => Mathf.SmoothStep(0f, 1f, _lineGrowth);
    private float _now;              // run time the axis extends to
    private float _redrawUntil;      // keep animating while anything is still fading
    private float _shownMin, _shownMax;
    private bool _rangeInitialized;
    private int _selected = -1;      // index into _dots

    /// <summary>(generation, index in population, fitness, genome) of a clicked point.</summary>
    public System.Action<int, int, float, GameGenome>? PointSelected;

    /// <summary>One game finished evaluating, `time` seconds into the run.</summary>
    public void AddCandidate(int generation, int index, float score, float time, GameGenome genome)
    {
        _dots.Add(new Dot(generation, index, score, time, _clock, genome));
        _now = Mathf.Max(_now, time);
        _redrawUntil = _clock + FadeSeconds;
        QueueRedraw();
    }

    /// <summary>A generation closed: its top/avg join the lines and a divider drops.</summary>
    public void AddGeneration(int generation, float top, float average, float endTime)
    {
        _marks.Add(new GenerationMark(generation, top, average, endTime));
        _now = Mathf.Max(_now, endTime);
        _lineGrowth = 0f;
        QueueRedraw();
    }

    /// <summary>The run clock, so the axis keeps extending while a long generation is
    /// still computing instead of standing still and then jumping.</summary>
    public void SetElapsed(float seconds)
    {
        if (seconds > _now)
        {
            _now = seconds;
            QueueRedraw();
        }
    }

    public void Clear()
    {
        _dots.Clear();
        _marks.Clear();
        _selected = -1;
        _now = 0f;
        _lineGrowth = 1f;
        _rangeInitialized = false;
        QueueRedraw();
    }

    /// <summary>Programmatic selection (automation + the run-finished convenience that
    /// focuses the final best game).</summary>
    public void Select(int generation, int index)
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            if (_dots[i].Generation == generation && _dots[i].Index == index)
            {
                SelectDot(i);
                return;
            }
        }
    }

    private void SelectDot(int dot)
    {
        _selected = dot;
        QueueRedraw();
        PointSelected?.Invoke(_dots[dot].Generation, _dots[dot].Index, _dots[dot].Score, _dots[dot].Genome);
    }

    public override void _Process(double delta)
    {
        _clock += (float)delta;
        if (_lineGrowth < 1f)
        {
            _lineGrowth = Mathf.Min(1f, _lineGrowth + (float)delta / LineGrowthSeconds);
        }
        // Redraw while anything is animating: dots fading in, lines growing, or the
        // range easing.
        if (_clock < _redrawUntil || _lineGrowth < 1f || RangeIsSettling())
        {
            QueueRedraw();
        }
    }

    public override void _GuiInput(InputEvent @event)
    {
        if (@event is not InputEventMouseButton { ButtonIndex: MouseButton.Left, Pressed: true } click)
        {
            return;
        }
        if (FindNearestDot(click.Position) is { } hit)
        {
            AcceptEvent();
            SelectDot(hit);
        }
    }

    /// <summary>Nearest dot within the hit radius — tests EVERY game, including any
    /// the draw pass thinned out.</summary>
    private int? FindNearestDot(Vector2 mouse)
    {
        if (_dots.Count == 0)
        {
            return null;
        }
        Vector2 size = Size;
        (float min, float max) = ShownRange();
        int best = -1;
        float bestDistSq = HitRadiusPx * HitRadiusPx;
        for (int i = 0; i < _dots.Count; i++)
        {
            float x = XOf(_dots[i].Time, size);
            if (Mathf.Abs(x - mouse.X) > HitRadiusPx)
            {
                continue;
            }
            float y = MapY(Mathf.Clamp(_dots[i].Score, min, max), min, max, size.Y);
            float distSq = (x - mouse.X) * (x - mouse.X) + (y - mouse.Y) * (y - mouse.Y);
            if (distSq < bestDistSq)
            {
                bestDistSq = distSq;
                best = i;
            }
        }
        return best >= 0 ? best : null;
    }

    public override void _Draw()
    {
        Vector2 size = Size;
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.05f, 0.05f, 0.08f));
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.35f, 0.4f, 0.5f), filled: false);

        Font font = ThemeDB.FallbackFont;
        if (_dots.Count == 0)
        {
            DrawString(font, new Vector2(12f, 24f), "fitness chart — waiting for the first games…",
                HorizontalAlignment.Left, -1f, 14, new Color(0.5f, 0.55f, 0.65f));
            return;
        }

        (float min, float max) = ShownRange();

        if (min < 0f && max > 0f)
        {
            float zeroY = MapY(0f, min, max, size.Y);
            DrawLine(new Vector2(0f, zeroY), new Vector2(size.X, zeroY), new Color(0.3f, 0.3f, 0.38f));
        }

        DrawGenerationDividers(font, size);
        DrawDots(min, max, size);
        DrawSeries(mark => mark.Average, min, max, size, new Color(0.55f, 0.65f, 0.9f));
        DrawSeries(mark => mark.Top, min, max, size, new Color(0.45f, 0.9f, 0.55f));
        DrawSelection(min, max, size);
        DrawReadout(font, size);
    }

    /// <summary>
    /// Generation boundaries. Spacing varies with how long each generation took —
    /// that IS the point — so dividers are skipped once they would be closer than a
    /// few pixels and turn into a wall.
    /// </summary>
    private void DrawGenerationDividers(Font font, Vector2 size)
    {
        if (_marks.Count == 0)
        {
            return;
        }
        // Below this the dividers stop being boundaries and become a grey wall over
        // the dots they are supposed to bracket.
        float spacing = size.X / Mathf.Max(1, _marks.Count);
        if (spacing < 9f)
        {
            return;
        }
        var color = new Color(0.30f, 0.34f, 0.44f, 0.55f);
        int labelEvery = Mathf.Max(1, Mathf.CeilToInt(52f / spacing));
        for (int i = 0; i < _marks.Count; i++)
        {
            float x = XOf(_marks[i].EndTime, size);
            DrawLine(new Vector2(x, 0f), new Vector2(x, size.Y), color);
            if (spacing >= 24f && _marks[i].Generation % labelEvery == 0)
            {
                DrawString(font, new Vector2(x + 3f, size.Y - 6f), _marks[i].Generation.ToString(),
                    HorizontalAlignment.Left, -1f, 10, new Color(0.45f, 0.5f, 0.6f, 0.7f));
            }
        }
    }

    /// <summary>
    /// Per-game dots, under the lines. Each fades in over its first fraction of a
    /// second, so a generation lands as a shimmer rather than a block. Only deep
    /// history thins, and only when the total would cost too much per frame.
    /// </summary>
    private void DrawDots(float min, float max, Vector2 size)
    {
        int stride = _dots.Count > MaxDrawnDots
            ? Mathf.CeilToInt(_dots.Count / (float)MaxDrawnDots)
            : 1;
        for (int i = 0; i < _dots.Count; i += stride)
        {
            Dot dot = _dots[i];
            float fade = Mathf.Clamp((_clock - dot.AppearedAt) / FadeSeconds, 0f, 1f);
            bool clamped = dot.Score < min || dot.Score > max;
            float x = XOf(dot.Time, size);
            float y = MapY(Mathf.Clamp(dot.Score, min, max), min, max, size.Y);
            DrawCircle(new Vector2(x, y), DotRadiusPx,
                new Color(0.58f, 0.66f, 0.80f, (clamped ? 0.14f : 0.42f) * fade));
        }
    }

    /// <summary>
    /// A trend line, drawn from the ORIGIN (designer: both lines start at zero, so
    /// there is a line from the first moment rather than an empty chart until two
    /// generations exist) and GROWING toward its newest vertex rather than snapping
    /// to it. The tip is interpolated, so the line extends continuously between
    /// generations; running a generation behind is fine and is what makes the motion
    /// read as drawing rather than stepping (designer, 2026-09-16).
    /// </summary>
    private void DrawSeries(System.Func<GenerationMark, float> value, float min, float max,
        Vector2 size, Color color)
    {
        if (_marks.Count == 0)
        {
            return;
        }
        var points = new Vector2[_marks.Count + 1];
        points[0] = new Vector2(XOf(0f, size), MapY(Mathf.Clamp(0f, min, max), min, max, size.Y));
        for (int i = 0; i < _marks.Count - 1; i++)
        {
            points[i + 1] = new Vector2(
                XOf(_marks[i].EndTime, size),
                MapY(Mathf.Clamp(value(_marks[i]), min, max), min, max, size.Y));
        }

        // The newest vertex is approached, not jumped to.
        GenerationMark newest = _marks[^1];
        Vector2 tip = new(
            XOf(newest.EndTime, size),
            MapY(Mathf.Clamp(value(newest), min, max), min, max, size.Y));
        points[^1] = points[^2].Lerp(tip, LineGrowth);
        DrawPolyline(points, color, 2f);
    }

    private void DrawSelection(float min, float max, Vector2 size)
    {
        if (_selected < 0 || _selected >= _dots.Count)
        {
            return;
        }
        Dot dot = _dots[_selected];
        float x = XOf(dot.Time, size);
        float y = MapY(Mathf.Clamp(dot.Score, min, max), min, max, size.Y);
        var gold = new Color(1f, 0.85f, 0.3f);
        DrawCircle(new Vector2(x, y), DotRadiusPx + 0.6f, gold);
        DrawArc(new Vector2(x, y), 7f, 0f, Mathf.Tau, 24, gold, 1.5f, antialiased: true);
    }

    private void DrawReadout(Font font, Vector2 size)
    {
        if (_marks.Count > 0)
        {
            GenerationMark last = _marks[^1];
            DrawString(font, new Vector2(12f, 22f), $"top {last.Top:F1}",
                HorizontalAlignment.Left, -1f, 14, new Color(0.45f, 0.9f, 0.55f));
            DrawString(font, new Vector2(12f, 42f), $"avg {last.Average:F1}",
                HorizontalAlignment.Left, -1f, 14, new Color(0.55f, 0.65f, 0.9f));
        }
        DrawString(font, new Vector2(size.X - 110f, size.Y - 10f),
            $"gen {(_marks.Count > 0 ? _marks[^1].Generation : 0)} · {_now:F1}s",
            HorizontalAlignment.Left, -1f, 14, new Color(0.5f, 0.55f, 0.65f));
    }

    /// <summary>
    /// The y window, eased toward a target that comes from the LINES ALONE — plus the
    /// origin both lines start from.
    ///
    /// It deliberately ignores the dots. An earlier version included the in-flight
    /// generation's dots so they could not plot off-screen, and the axis then chased
    /// every incoming outlier: with games landing several times a second the window
    /// never settled and the whole chart oscillated (designer, 2026-09-16 — "bouncing
    /// and nauseating"). Lines move once per generation, so the window now moves once
    /// per generation too, and a dot outside it clamps to the edge and draws fainter
    /// exactly like a historical straggler.
    /// </summary>
    private (float Min, float Max) ShownRange()
    {
        (float min, float max) = TargetRange();
        if (!_rangeInitialized)
        {
            _shownMin = min;
            _shownMax = max;
            _rangeInitialized = true;
        }
        else
        {
            // Frame-rate independent, and slow enough to read as a slide rather than
            // a correction: ~95% of the way in a second.
            float gain = 1f - Mathf.Pow(0.05f, Mathf.Min(0.1f, (float)GetProcessDeltaTime()));
            _shownMin = Mathf.Lerp(_shownMin, min, gain);
            _shownMax = Mathf.Lerp(_shownMax, max, gain);
        }
        return (_shownMin, _shownMax);
    }

    private bool RangeIsSettling()
    {
        if (!_rangeInitialized)
        {
            return false;
        }
        float span = Mathf.Max(1e-3f, _shownMax - _shownMin);
        (float min, float max) = TargetRange();
        return Mathf.Abs(min - _shownMin) / span > 0.001f || Mathf.Abs(max - _shownMax) / span > 0.001f;
    }

    /// <summary>Both lines start at zero (designer), so the origin is always in the
    /// window — the chart has an axis to read against before any generation closes.</summary>
    private (float Min, float Max) TargetRange()
    {
        float min = 0f, max = 0f;
        foreach (GenerationMark mark in _marks)
        {
            min = Mathf.Min(min, Mathf.Min(mark.Top, mark.Average));
            max = Mathf.Max(max, Mathf.Max(mark.Top, mark.Average));
        }
        if (max - min < 1e-3f)
        {
            max = min + 1f;
        }
        return (min, max);
    }

    /// <summary>Run time to pixels. The axis always spans a little past the latest
    /// event so the live edge is not glued to the frame.</summary>
    private float XOf(float time, Vector2 size)
    {
        float span = Mathf.Max(1f, _now * 1.02f);
        return size.X * Mathf.Clamp(time / span, 0f, 1f);
    }

    private static float MapY(float value, float min, float max, float height) =>
        height - (value - min) / (max - min) * (height - 16f) - 8f;
}
