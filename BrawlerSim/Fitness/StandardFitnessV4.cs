using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// standard-v4 (2026-08-12, designer-specified; docs/features/four-player.md):
/// standard-v3's EXACT terms plus a SELF-DESTRUCT punishment — "punish stages that
/// have characters who self-destruct" (leave a platform and die without a hit that
/// knocked them off or a person who pushed them off; see the KO-attribution rules in
/// CHANGE_LOG #32). The term: −1 per self-destruct summed over both players, CAPPED
/// at −4 per match so one degenerate match cannot drown every other signal.
///
/// v3 remains frozen and selectable; v4 is the default for NEW two-player runs.
/// Scores differ from v3 only on matches containing self-destructs.
/// </summary>
public sealed class StandardFitnessV4 : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    public const float DefaultSelfDestructPenalty = 1f;
    public const float DefaultSelfDestructCap = 4f;

    private readonly FitnessComposer _composed;

    public StandardFitnessV4(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar,
        float selfDestructPenalty = DefaultSelfDestructPenalty,
        float selfDestructCap = DefaultSelfDestructCap)
    {
        _composed = new FitnessComposer("standard-v4", ShippedTermLists.V4(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar,
            selfDestructPenalty, selfDestructCap,
            StandardFitnessV3.DefaultPunishStartDamage,
            StandardFitnessV3.DefaultStockDamageCap,
            StandardFitnessV3.DefaultPunishSlope,
            StandardFitnessV3.DefaultMoveMixWeight,
            StandardFitnessV3.DefaultStunLockWeight,
            StandardFitnessV3.DefaultJumpWeight,
            StandardFitnessV3.DefaultBlockReward));
    }

    public string Name => _composed.Name;

    public float Evaluate(MatchResult result) => _composed.Evaluate(result);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _composed.Breakdown(result);

    public IReadOnlyList<IFitnessTerm> Terms => _composed.Terms;
}
