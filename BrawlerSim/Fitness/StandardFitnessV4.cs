using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// standard-v4 (2026-08-12, designer-specified; docs/features/four-player.md):
/// standard-v3's EXACT terms plus a SELF-DESTRUCT punishment — "punish stages that
/// have characters who self-destruct" (leave a platform and die without a hit that
/// knocked them off or a person who pushed them off; see the KO-attribution rules in
/// DEVIATIONS #32). The term: −1 per self-destruct summed over both players, CAPPED
/// at −4 per match so one degenerate match cannot drown every other signal.
///
/// v3 remains frozen and selectable; v4 is the default for NEW two-player runs.
/// Scores differ from v3 only on matches containing self-destructs.
/// </summary>
public sealed class StandardFitnessV4 : IFitnessFunction
{
    public const float DefaultSelfDestructPenalty = 1f;
    public const float DefaultSelfDestructCap = 4f;

    private readonly StandardFitnessV3 _v3;
    private readonly float _sdPenalty;
    private readonly float _sdCap;

    public StandardFitnessV4(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f,
        float collisionScalar = StandardFitnessV3.DefaultCollisionScalar,
        float selfDestructPenalty = DefaultSelfDestructPenalty,
        float selfDestructCap = DefaultSelfDestructCap)
    {
        _v3 = new StandardFitnessV3(
            targetLengthSeconds, maxLengthSeconds, damageScalar, collisionScalar: collisionScalar);
        _sdPenalty = selfDestructPenalty;
        _sdCap = selfDestructCap;
    }

    public string Name => "standard-v4";

    public float Evaluate(MatchResult result) =>
        _v3.Evaluate(result) + SelfDestructTerm(result, _sdPenalty, _sdCap);

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result)
    {
        var terms = new List<(string, float)>(_v3.Breakdown(result))
        {
            ("selfDestructs", SelfDestructTerm(result, _sdPenalty, _sdCap)),
        };
        return terms;
    }

    /// <summary>−penalty × ΣSelfDestructs over all players, floored at −(cap).
    /// Shared with ffa-v1 (identical designer spec for both).</summary>
    internal static float SelfDestructTerm(MatchResult result, float penalty, float cap)
    {
        int total = 0;
        foreach (PlayerStats player in result.Players)
        {
            total += player.SelfDestructs;
        }
        return -MathF.Min(penalty * total, cap);
    }
}
