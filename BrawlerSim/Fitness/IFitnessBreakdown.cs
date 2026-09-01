using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// Optional companion to <see cref="IFitnessFunction"/>: exposes the per-term
/// decomposition of a score for diagnostics (the CLI's `evaluate --breakdown`).
/// Every fitness version that can itemize its terms implements this; callers
/// type-test for the interface instead of listing concrete classes, so new
/// versions surface in --breakdown automatically (the v5/ffa-v2 defaults were
/// silently missing from the CLI's hand-written type chain before 2026-09-01).
/// The frozen StandardFitness (standard-v2) predates term composition and
/// deliberately does not implement it.
/// </summary>
public interface IFitnessBreakdown
{
    IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result);
}
