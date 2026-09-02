using NgPcg = NameGen.Core.Pcg32;

namespace BrawlerSim.Weather;

/// <summary>A [min, max] envelope, sampled uniformly per keyframe from the seed.</summary>
public sealed record Envelope(float Min, float Max)
{
    public float Sample(NgPcg rng) => Min + (float)rng.NextDouble() * (Max - Min);

    public bool Contains(float v, float slack = 1e-3f) => v >= Min - slack && v <= Max + slack;
}

/// <summary>One tween segment: value eases from Start to End over [T0, T1]; between
/// segments the value HOLDS the previous End (that is what keeps curves continuous —
/// every segment starts exactly where the last one finished).</summary>
public sealed record ScheduleSegment(float T0, float T1, float Start, float End, string Easing);

/// <summary>
/// A SEEDED KEYFRAME SCHEDULE (brief decision 5): a channel's preset envelope is
/// expanded ONCE at load into tween segments generated from the stage seed;
/// per-frame evaluation is a pure lookup against the match clock. No live RNG in any
/// deterministic channel; same seed = the same storm, the same gust at 0:43.
/// Static maxima are exact and cheap (every easing is monotone, so the extrema are
/// the segment endpoints) — which is what makes the readability budget enforceable
/// at load instead of hoped-for at runtime.
/// </summary>
public sealed class KeyframeSchedule
{
    private readonly ScheduleSegment[] _segments;
    private readonly float _initial;

    public IReadOnlyList<ScheduleSegment> Segments => _segments;

    public KeyframeSchedule(float initial, IEnumerable<ScheduleSegment> segments)
    {
        _initial = initial;
        _segments = segments.ToArray();
    }

    public float Evaluate(float t)
    {
        float value = _initial;
        foreach (ScheduleSegment s in _segments)
        {
            if (t < s.T0)
            {
                return value;
            }
            if (t < s.T1)
            {
                return s.Start + (s.End - s.Start)
                    * EasingLibrary.Evaluate(s.Easing, (t - s.T0) / (s.T1 - s.T0));
            }
            value = s.End;
        }
        return value;
    }

    public float StaticMax()
    {
        float max = _initial;
        foreach (ScheduleSegment s in _segments)
        {
            max = Math.Max(max, Math.Max(s.Start, s.End));
        }
        return max;
    }

    /// <summary>A generic dynamics channel: hold (changeEveryS), then tween
    /// (tweenDurS, an easing drawn from the set) to a fresh envelope sample, over the
    /// whole horizon. Beyond the horizon the last value holds.</summary>
    public static KeyframeSchedule Channel(Envelope range, Envelope changeEveryS,
        Envelope tweenDurS, IReadOnlyList<string> easings, NgPcg rng, float horizonS)
    {
        float value = range.Sample(rng);
        float initial = value;
        var segments = new List<ScheduleSegment>();
        float t = 0;
        while (t < horizonS)
        {
            float hold = Math.Max(0.1f, changeEveryS.Sample(rng));
            float tween = Math.Max(0.1f, tweenDurS.Sample(rng));
            float next = range.Sample(rng);
            string easing = easings.Count == 0 ? "linear" : easings[rng.NextInt(easings.Count)];
            segments.Add(new ScheduleSegment(t + hold, t + hold + tween, value, next, easing));
            value = next;
            t += hold + tween;
        }
        return new KeyframeSchedule(initial, segments);
    }

    /// <summary>The episode gate in [0, 1]: persistent = always 1; otherwise gaps
    /// (gapS) and episodes (durS) with eased ramps — precipitation swells and dies
    /// instead of popping. Ramps subtract from the episode's duration, so dur/gap
    /// stay inside their envelopes.</summary>
    public static KeyframeSchedule Episodes(bool persistent, Envelope durS, Envelope gapS,
        float rampInS, float rampOutS, string easing, NgPcg rng, float horizonS)
    {
        if (persistent)
        {
            return new KeyframeSchedule(1f, Array.Empty<ScheduleSegment>());
        }
        var segments = new List<ScheduleSegment>();
        float t = 0;
        while (t < horizonS)
        {
            float gap = Math.Max(0.5f, gapS.Sample(rng));
            float dur = Math.Max(rampInS + rampOutS + 0.5f, durS.Sample(rng));
            float up0 = t + gap;
            segments.Add(new ScheduleSegment(up0, up0 + rampInS, 0f, 1f, easing));
            float down0 = up0 + dur - rampOutS;
            segments.Add(new ScheduleSegment(down0, down0 + rampOutS, 1f, 0f, easing));
            t = up0 + dur;
        }
        return new KeyframeSchedule(0f, segments);
    }
}

/// <summary>The direction channel: a base heading, a slow signed drift across the
/// match, and a bounded sine oscillation (gusts sway rather than snap) — analytic,
/// so it is continuous by construction; every parameter is sampled once from the
/// seed at load.</summary>
public sealed record DirectionPlan(
    float BaseDeg, float DriftDegPerMin, float OscAmpDeg, float OscPeriodS, float PhaseRad)
{
    public float Evaluate(float t) =>
        BaseDeg + DriftDegPerMin * (t / 60f)
        + OscAmpDeg * MathF.Sin(MathF.Tau * t / Math.Max(0.5f, OscPeriodS) + PhaseRad);

    public static DirectionPlan Sample(Envelope baseDeg, Envelope driftDegPerMin,
        Envelope oscAmpDeg, Envelope oscPeriodS, NgPcg rng)
    {
        float drift = driftDegPerMin.Sample(rng) * (rng.NextInt(2) == 0 ? 1f : -1f);
        return new DirectionPlan(
            baseDeg.Sample(rng), drift, oscAmpDeg.Sample(rng), oscPeriodS.Sample(rng),
            (float)(rng.NextDouble() * Math.Tau));
    }
}
