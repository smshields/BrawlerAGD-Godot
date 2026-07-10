using BrawlerSim.Sim;

namespace BrawlerSim.Fitness;

/// <summary>
/// A fitness function assembled from named, independently-computable terms
/// (2026-07-10 refactor for tunability). Evaluate() is the plain sum; Breakdown()
/// returns each term's contribution so `evaluate --breakdown`, reports, and tuning
/// sessions can see WHY a match scored what it did. Versioned functions
/// (StandardFitnessV3, future persona fitness) are thin constructors over this.
/// </summary>
public sealed class ComposedFitness : IFitnessFunction
{
    public sealed record Term(string Name, Func<MatchResult, float> Value);

    private readonly Term[] _terms;

    public ComposedFitness(string name, IEnumerable<Term> terms)
    {
        Name = name;
        _terms = terms.ToArray();
    }

    public string Name { get; }

    public IReadOnlyList<Term> Terms => _terms;

    public float Evaluate(MatchResult result)
    {
        float total = 0f;
        foreach (Term term in _terms)
        {
            total += term.Value(result);
        }
        return total;
    }

    public IReadOnlyList<(string Name, float Value)> Breakdown(MatchResult result) =>
        _terms.Select(t => (t.Name, t.Value(result))).ToArray();
}
