namespace BrawlerSim.Params;

/// <summary>
/// Declares one evolvable parameter: a stable string key (used in game.json and for
/// legacy import) and the inclusive range from which values are generated and mutated.
/// The live equivalent of Table 1 in the AIIDE '22 paper.
///
/// ValidMin/ValidMax widen the VALID domain beyond the generation range for params whose
/// values are legitimately transformed after generation (e.g. the knockback components,
/// which the generation-time constraint lerps toward the hitbox direction — a convex
/// combination bounded by the endpoints, hence knockback values in evolved/legacy data
/// can sit outside the generation range but never outside the lerp endpoints' bounds).
/// </summary>
public readonly record struct ParamSpec(string Key, float Min, float Max)
{
    public float? ValidMin { get; init; }
    public float? ValidMax { get; init; }

    public float EffectiveValidMin => ValidMin ?? Min;
    public float EffectiveValidMax => ValidMax ?? Max;

    public bool Contains(float value) => value >= EffectiveValidMin && value <= EffectiveValidMax;
}
