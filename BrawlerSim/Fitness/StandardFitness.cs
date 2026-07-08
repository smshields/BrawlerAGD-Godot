using BrawlerSim.Determinism;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// The AIIDE '22 fitness function with its two defects corrected (per designer decision —
/// no bug-compatible mode, so results are not directly comparable to the paper's runs):
/// - stock fairness now actually compares the two players (Unity computed |s1 − s1| ≡ 0,
///   so the term was a constant 3 and never shaped evolution);
/// - the damage-per-stock cap counts stocks lost as (6 − s1 − s2) (Unity's sign error
///   `6 − s1 + s2` inflated the cap whenever player 2 had stocks left).
/// Everything else is term-for-term the shipped formula.
/// </summary>
public sealed class StandardFitness : IFitnessFunction
{
    public const float OvertimePenalty = -35f;
    public const float TargetDamagePerStockLost = 100f;

    private readonly float _targetLengthSeconds;
    private readonly float _maxLengthSeconds;
    private readonly float _damageScalar;

    public StandardFitness(
        float targetLengthSeconds = 45f,
        float maxLengthSeconds = 60f,
        float damageScalar = 10f)
    {
        _targetLengthSeconds = targetLengthSeconds;
        _maxLengthSeconds = maxLengthSeconds;
        _damageScalar = damageScalar;
    }

    public string Name => "standard-v2";

    public float Evaluate(MatchResult result)
    {
        PlayerStats p1 = result.Players[0];
        PlayerStats p2 = result.Players[1];

        float overtime = result.LengthSeconds >= _maxLengthSeconds ? OvertimePenalty : 0f;
        float timeFitness = -DetMath.Abs(_targetLengthSeconds - result.LengthSeconds) + overtime;

        float totalDamage = p1.TotalDamageTaken + p2.TotalDamageTaken;
        float damageFitness = totalDamage / _damageScalar;

        float stocksLost = 6f - p1.RemainingStocks - p2.RemainingStocks;
        float damageCap = stocksLost * TargetDamagePerStockLost;
        float damagePenalty = totalDamage >= damageCap ? damageCap - totalDamage : 0f;

        float collisionFitness = p1.TotalHitsReceived + p2.TotalHitsReceived;

        float damageFairness = -DetMath.Abs(p1.TotalDamageTaken - p2.TotalDamageTaken) / _damageScalar;

        float stockFairness = 3f - DetMath.Abs(p1.RemainingStocks - p2.RemainingStocks);

        return timeFitness + damageFitness + collisionFitness
             + damageFairness + stockFairness + damagePenalty;
    }
}
