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

    /// <summary>
    /// Dots render through a GPU-instanced MultiMesh layer (2026-09-17 performance
    /// round). The CPU used to tessellate a DrawCircle per dot per frame — 100 new
    /// dots per generation at 60 Hz meant the chart's draw cost grew forever with
    /// the run, which surfaced as "the evolution slows down after 20-30 generations"
    /// and would only be worse on weaker machines. Now a dot is uploaded ONCE when
    /// its game finishes; the fade-in, the time axis, the eased range and the clamp
    /// dimming are all computed in the dot shader from a handful of per-frame
    /// uniforms, so per-frame CPU cost is constant no matter how long the run gets.
    /// No thinning either — instanced quads are trivial at any count this app hits,
    /// so drawn density always means what it looks like it means.
    /// </summary>
    private MultiMeshInstance2D _dotLayer = null!;
    private MultiMesh _dotMesh = null!;
    private ShaderMaterial _dotMaterial = null!;
    private int _dotCapacity;

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
    private float _shownMin, _shownMax;
    private bool _rangeInitialized;
    private int _selected = -1;      // index into _dots
    private float _lastDrawnSpan = 1f; // axis span at the last vector redraw
    private bool _vectorDirty = true;  // marks/selection changed since the last draw

    /// <summary>(generation, index in population, fitness, genome) of a clicked point.</summary>
    public System.Action<int, int, float, GameGenome>? PointSelected;

    public override void _Ready()
    {
        // The background is its OWN layer, not part of _Draw: the dot layer renders
        // behind the parent's drawing, and a background painted in _Draw sits OVER
        // the dots and hides every one of them (found the hard way). Draw order:
        // background, dots, then the parent's lines/dividers/labels.
        var background = new ColorRect
        {
            Color = new Color(0.05f, 0.05f, 0.08f),
            ShowBehindParent = true,
            MouseFilter = MouseFilterEnum.Ignore,
        };
        AddChild(background);
        background.SetAnchorsAndOffsetsPreset(LayoutPreset.FullRect);

        _dotMaterial = new ShaderMaterial { Shader = DotShader() };
        _dotMaterial.SetShaderParameter("dot_radius", DotRadiusPx);
        _dotMaterial.SetShaderParameter("fade_seconds", FadeSeconds);
        _dotMesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform2D,
            UseCustomData = true,
            Mesh = new QuadMesh { Size = Vector2.One },
        };
        _dotLayer = new MultiMeshInstance2D
        {
            Multimesh = _dotMesh,
            Material = _dotMaterial,
            // Dots sit UNDER the vector layer (lines, dividers, selection ring).
            ShowBehindParent = true,
        };
        AddChild(_dotLayer);
        ClipContents = true;
    }

    /// <summary>Each instance carries (time, score, appearedAt) in CUSTOM data; the
    /// vertex shader maps run time and score to pixels with the SAME formulas the
    /// vector layer uses (XOf / MapY — keep them in step), fades the dot in against
    /// the clock uniform, and dims it when the score sits outside the shown range,
    /// exactly like a historical straggler.</summary>
    private static Shader DotShader() => new()
    {
        Code = """
        shader_type canvas_item;

        uniform vec2 chart_size = vec2(100.0, 100.0);
        uniform float time_span = 1.0;
        uniform float y_min = 0.0;
        uniform float y_range = 1.0;
        uniform float clock = 0.0;
        uniform float dot_radius = 2.6;
        uniform float fade_seconds = 0.45;
        uniform vec4 dot_color : source_color = vec4(0.58, 0.66, 0.80, 1.0);

        varying float alpha_factor;

        void vertex() {
            float t = INSTANCE_CUSTOM.x;
            float score = INSTANCE_CUSTOM.y;
            float appeared = INSTANCE_CUSTOM.z;

            float x = t / max(time_span, 0.001) * chart_size.x;
            float shown = clamp(score, y_min, y_min + y_range);
            float y = chart_size.y
                - (shown - y_min) / max(y_range, 0.001) * (chart_size.y - 16.0) - 8.0;

            bool clamped = score < y_min || score > y_min + y_range;
            float fade = clamp((clock - appeared) / fade_seconds, 0.0, 1.0);
            alpha_factor = (clamped ? 0.14 : 0.42) * fade;

            VERTEX = vec2(x, y) + (UV - 0.5) * dot_radius * 2.2;
        }

        void fragment() {
            float d = length(UV - 0.5) * 2.0;
            float disc = 1.0 - smoothstep(0.78, 1.0, d);
            COLOR = vec4(dot_color.rgb, dot_color.a * alpha_factor * disc);
        }
        """,
    };

    /// <summary>Grow the instance buffer. Changing InstanceCount clears it, so the
    /// custom data is re-uploaded from the dot list — rare and amortized.</summary>
    private void EnsureDotCapacity(int needed)
    {
        if (needed <= _dotCapacity)
        {
            return;
        }
        _dotCapacity = Mathf.Max(2048, _dotCapacity * 2);
        while (_dotCapacity < needed)
        {
            _dotCapacity *= 2;
        }
        _dotMesh.InstanceCount = _dotCapacity;
        // Changing InstanceCount zeroes the buffer, and a ZERO instance transform
        // collapses the quad after the vertex shader has run — the dots exist but
        // occupy no pixels. Every slot gets an identity transform up front; the
        // shader positions the quad itself from the custom data.
        for (int i = 0; i < _dotCapacity; i++)
        {
            _dotMesh.SetInstanceTransform2D(i, Transform2D.Identity);
        }
        for (int i = 0; i < _dots.Count; i++)
        {
            UploadDot(i);
        }
    }

    private void UploadDot(int index)
    {
        Dot dot = _dots[index];
        _dotMesh.SetInstanceCustomData(index,
            new Color(dot.Time, dot.Score, dot.AppearedAt, 0f));
    }

    /// <summary>One game finished evaluating, `time` seconds into the run.</summary>
    public void AddCandidate(int generation, int index, float score, float time, GameGenome genome)
    {
        _dots.Add(new Dot(generation, index, score, time, _clock, genome));
        _now = Mathf.Max(_now, time);
        EnsureDotCapacity(_dots.Count);
        UploadDot(_dots.Count - 1);
        _dotMesh.VisibleInstanceCount = _dots.Count;
    }

    /// <summary>A generation closed: its top/avg join the lines and a divider drops.</summary>
    public void AddGeneration(int generation, float top, float average, float endTime)
    {
        _marks.Add(new GenerationMark(generation, top, average, endTime));
        _now = Mathf.Max(_now, endTime);
        _lineGrowth = 0f;
        _vectorDirty = true;
    }

    /// <summary>The run clock, so the axis keeps extending while a long generation is
    /// still computing instead of standing still and then jumping. Never queues a
    /// redraw itself — _Process redraws the vector layer only once the axis has
    /// actually moved by a visible amount, and the dot layer follows the uniforms.</summary>
    public void SetElapsed(float seconds)
    {
        _now = Mathf.Max(_now, seconds);
    }

    public void Clear()
    {
        _dots.Clear();
        _marks.Clear();
        _selected = -1;
        _now = 0f;
        _lineGrowth = 1f;
        _rangeInitialized = false;
        if (_dotMesh is not null)
        {
            _dotMesh.VisibleInstanceCount = 0;
        }
        _vectorDirty = true;
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
        _vectorDirty = true;
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
        EaseRange((float)delta);

        // The dot layer animates entirely from these uniforms — no per-dot work and
        // no CanvasItem redraw, whatever the dot count.
        float span = TimeSpan();
        _dotMaterial.SetShaderParameter("chart_size", Size);
        _dotMaterial.SetShaderParameter("time_span", span);
        _dotMaterial.SetShaderParameter("y_min", _shownMin);
        _dotMaterial.SetShaderParameter("y_range", Mathf.Max(1e-3f, _shownMax - _shownMin));
        _dotMaterial.SetShaderParameter("clock", _clock);

        // The vector layer (lines, dividers, labels) redraws only when something of
        // its own moved: a new mark or selection, a growing line, the range easing,
        // or the axis having compressed far enough to shift pixels.
        bool axisMoved = Mathf.Abs(span - _lastDrawnSpan) / span * Size.X > 0.5f;
        if (_vectorDirty || _lineGrowth < 1f || RangeIsSettling() || axisMoved)
        {
            QueueRedraw();
        }
    }

    /// <summary>Frame-rate independent ease of the shown y window toward the target
    /// (lines only — see TargetRange): ~95% of the way in a second, so a widening
    /// range slides instead of snapping.</summary>
    private void EaseRange(float delta)
    {
        (float min, float max) = TargetRange();
        if (!_rangeInitialized)
        {
            _shownMin = min;
            _shownMax = max;
            _rangeInitialized = true;
            return;
        }
        float gain = 1f - Mathf.Pow(0.05f, Mathf.Min(0.1f, delta));
        _shownMin = Mathf.Lerp(_shownMin, min, gain);
        _shownMax = Mathf.Lerp(_shownMax, max, gain);
    }

    private float TimeSpan() => Mathf.Max(1f, _now * 1.02f);

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
        (float min, float max) = (_shownMin, _shownMax);
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
        // Background lives on its own layer behind the dots (see _Ready); only the
        // border is drawn here.
        DrawRect(new Rect2(Vector2.Zero, size), new Color(0.35f, 0.4f, 0.5f), filled: false);

        Font font = ThemeDB.FallbackFont;
        if (_dots.Count == 0)
        {
            DrawString(font, new Vector2(12f, 24f), "fitness chart — waiting for the first games…",
                HorizontalAlignment.Left, -1f, 14, new Color(0.5f, 0.55f, 0.65f));
            return;
        }

        (float min, float max) = (_shownMin, _shownMax);
        _lastDrawnSpan = TimeSpan();
        _vectorDirty = false;

        if (min < 0f && max > 0f)
        {
            float zeroY = MapY(0f, min, max, size.Y);
            DrawLine(new Vector2(0f, zeroY), new Vector2(size.X, zeroY), new Color(0.3f, 0.3f, 0.38f));
        }

        DrawGenerationDividers(font, size);
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
    private float XOf(float time, Vector2 size) =>
        size.X * Mathf.Clamp(time / TimeSpan(), 0f, 1f);

    private static float MapY(float value, float min, float max, float height) =>
        height - (value - min) / (max - min) * (height - 16f) - 8f;
}
