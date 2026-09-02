namespace BrawlerSim.Weather;

/// <summary>
/// The fixed easing vocabulary shared by every weather channel (backgrounds track
/// Phase 3, 2026-09-02 — brief §Phase 3): pure functions over t in [0, 1], all
/// monotone (so a schedule's static maxima are its keyframe endpoint values) and all
/// endpoint-exact (f(0) = 0, f(1) = 1 — what keeps evaluated curves continuous at
/// keyframe boundaries). Adding an easing is adding a pure function here.
/// </summary>
public static class EasingLibrary
{
    public static readonly IReadOnlyList<string> Names = new[]
    {
        "linear", "sineIn", "sineOut", "sineInOut",
        "cubicIn", "cubicOut", "cubicInOut", "expoOut", "step",
    };

    public static bool Contains(string name) => Names.Contains(name);

    public static float Evaluate(string easing, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return easing switch
        {
            "sineIn" => 1f - MathF.Cos(t * MathF.PI / 2f),
            "sineOut" => MathF.Sin(t * MathF.PI / 2f),
            "sineInOut" => -(MathF.Cos(MathF.PI * t) - 1f) / 2f,
            "cubicIn" => t * t * t,
            "cubicOut" => 1f - MathF.Pow(1f - t, 3f),
            "cubicInOut" => t < 0.5f ? 4f * t * t * t : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f,
            "expoOut" => t >= 1f ? 1f : 1f - MathF.Pow(2f, -10f * t),
            "step" => t >= 1f ? 1f : 0f,
            _ => t, // linear
        };
    }
}
