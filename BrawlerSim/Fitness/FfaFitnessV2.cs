using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// ffa-v2 (2026-09-01, designer-specified; docs/features/thin-platforms.md): ffa-v1's
/// EXACT terms plus the standard-v5 drop-through tiebreaker (+0.25 per crouch drop
/// summed over all players, capped at +1, never negative). Because ffa-v1 at N = 2
/// scores identically to standard-v4 and the added term is player-count-agnostic,
/// ffa-v2 at N = 2 scores identically to standard-v5 (regression-tested). The default
/// fitness for NEW 3/4-player runs; ffa-v1 remains frozen and selectable.
/// </summary>
public sealed class FfaFitnessV2 : IFitnessFunction
{
    private readonly FfaFitnessV1 _v1;
    private readonly float _dropReward;
    private readonly float _dropCap;

    public FfaFitnessV2(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar,
        float dropThroughReward = StandardFitnessV5.DefaultDropThroughReward,
        float dropThroughCap = StandardFitnessV5.DefaultDropThroughCap)
    {
        _v1 = new FfaFitnessV1(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar: collisionScalar);
        _dropReward = dropThroughReward;
        _dropCap = dropThroughCap;
    }

    public string Name => "ffa-v2";

    public float Evaluate(MatchResult result) =>
        _v1.Evaluate(result) + StandardFitnessV5.DropThroughTerm(result, _dropReward, _dropCap);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result)
    {
        var terms = new List<(string, float)>(_v1.Breakdown(result))
        {
            ("dropThroughs", StandardFitnessV5.DropThroughTerm(result, _dropReward, _dropCap)),
        };
        return terms;
    }
}
