using BrawlerSim.Fitness.Terms;
using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// THE STITCHER (2026-09-16, fitness term refactor): a fitness function assembled at
/// runtime from an ordered list of <see cref="IFitnessTerm"/>. Evaluate() is the plain
/// sum in list order; Breakdown() reports each term's contribution, so
/// `evaluate --breakdown`, reports, and tuning sessions can see WHY a match scored what
/// it did.
///
/// Every shipped version is now a thin constructor over this
/// (<see cref="ShippedTermLists"/>), and a designer-built fitness will be the same
/// class over a list the user assembled. Replaces ComposedFitness, which held the same
/// idea as name+lambda pairs and could not report or re-read a term's constants.
///
/// ORDER IS PART OF THE FUNCTION. Float addition is not associative, so re-ordering
/// terms changes the score in the last bits. The list is the definition, not a
/// preference — which is why the shipped lists are fixed and pinned
/// (FitnessCharacterizationTests).
/// </summary>
public sealed class FitnessComposer : IFitnessFunction, IFitnessBreakdown, IFitnessTermList
{
    private readonly IFitnessTerm[] _terms;

    public FitnessComposer(string name, IEnumerable<IFitnessTerm> terms)
    {
        Name = name;
        _terms = terms.ToArray();
    }

    public string Name { get; }

    /// <summary>The assembled terms, in evaluation order. A builder UI reads this to
    /// show what a fitness currently is; each term reports its own constants through
    /// <see cref="IFitnessTerm.Values"/>.</summary>
    public IReadOnlyList<IFitnessTerm> Terms => _terms;

    public float Evaluate(MatchResult result)
    {
        var input = new FitnessInput(result);
        float total = 0f;
        foreach (IFitnessTerm term in _terms)
        {
            total += term.Evaluate(input);
        }
        return total;
    }

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result)
    {
        var input = new FitnessInput(result);
        var rows = new (string, float)[_terms.Length];
        for (int i = 0; i < _terms.Length; i++)
        {
            rows[i] = (_terms[i].Id, _terms[i].Evaluate(input));
        }
        return rows;
    }
}
