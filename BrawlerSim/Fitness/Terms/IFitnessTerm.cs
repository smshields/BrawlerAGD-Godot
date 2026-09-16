namespace BrawlerSim.Fitness.Terms;

/// <summary>
/// One named, independently-computable contribution to a fitness score
/// (2026-09-16, fitness term refactor). A term owns its own constants — every value
/// that used to be a bare literal or an unreachable <c>const</c> inside a version
/// class is now a constructor parameter — and reports them through
/// <see cref="Values"/> so a builder UI can read what a term is currently set to and
/// write it back out.
///
/// Terms are PURE and STATELESS: same input, same float, every time, on every
/// platform. They are summed in list order by <see cref="FitnessComposer"/>.
/// </summary>
public interface IFitnessTerm
{
    /// <summary>Stable identifier — the breakdown row name, the registry key, and the
    /// serialization key when recipes land. Never renamed once shipped.</summary>
    string Id { get; }

    /// <summary>This term's contribution, signed.</summary>
    float Evaluate(FitnessInput input);

    /// <summary>The constants this instance was built with, keyed by
    /// <see cref="FitnessTermParam.Key"/>. Round-trips through
    /// <see cref="FitnessTermRegistry.Create"/>.</summary>
    IReadOnlyDictionary<string, float> Values { get; }
}

/// <summary>
/// A fitness that is built from terms and can hand them back — every version from
/// standard-v3 on, plus any designer-built assembly. Capturing a fitness as a
/// FitnessRecipe, and opening one for editing, both go through this.
/// standard-v2 deliberately does NOT implement it (it predates term composition).
/// </summary>
public interface IFitnessTermList
{
    IReadOnlyList<IFitnessTerm> Terms { get; }
}

/// <summary>
/// The declaration of one tunable constant: what it is called, what it defaults to,
/// and the range a UI should offer. Min/Max bound the CONTROL, not the maths — a term
/// evaluates whatever it is handed, exactly like the genome schema's generation range
/// versus its wider valid domain.
/// </summary>
public sealed record FitnessTermParam(
    string Key,
    string Label,
    float Default,
    float Min,
    float Max,
    string Doc)
{
    /// <summary>A 0/1 switch rendered as a toggle rather than a spinner. Kept as a
    /// float so the parameter map stays one uniform type from the UI through the
    /// registry to serialization.</summary>
    public static FitnessTermParam Flag(string key, string label, bool @default, string doc) =>
        new(key, label, @default ? 1f : 0f, 0f, 1f, doc);

    public bool IsFlag => Min == 0f && Max == 1f && Doc.StartsWith("Flag:", StringComparison.Ordinal);
}

/// <summary>Helpers shared by every term implementation.</summary>
internal static class TermValues
{
    internal static IReadOnlyDictionary<string, float> Of(params (string Key, float Value)[] pairs)
    {
        var map = new Dictionary<string, float>(pairs.Length, StringComparer.Ordinal);
        foreach ((string key, float value) in pairs)
        {
            map[key] = value;
        }
        return map;
    }

    /// <summary>The value for <paramref name="key"/>, or the spec default when the
    /// caller did not supply one — how a partial parameter map from a UI or a saved
    /// recipe fills in.</summary>
    internal static float Read(
        IReadOnlyDictionary<string, float>? values, IReadOnlyList<FitnessTermParam> specs, string key)
    {
        if (values is not null)
        {
            // A key this term does not declare is a MISTAKE, not a no-op: a typo in a
            // saved assembly would otherwise read as "that knob does nothing".
            foreach (string supplied in values.Keys)
            {
                if (!Declares(specs, supplied))
                {
                    throw new ArgumentException(
                        $"Term does not declare a parameter '{supplied}' " +
                        $"({string.Join("|", specs.Select(s => s.Key))}).");
                }
            }
            if (values.TryGetValue(key, out float value))
            {
                return value;
            }
        }
        foreach (FitnessTermParam spec in specs)
        {
            if (spec.Key == key)
            {
                return spec.Default;
            }
        }
        throw new ArgumentException($"No parameter '{key}' declared on this term.");
    }

    private static bool Declares(IReadOnlyList<FitnessTermParam> specs, string key)
    {
        foreach (FitnessTermParam spec in specs)
        {
            if (spec.Key == key)
            {
                return true;
            }
        }
        return false;
    }

    internal static bool Flag(
        IReadOnlyDictionary<string, float>? values, IReadOnlyList<FitnessTermParam> specs, string key) =>
        Read(values, specs, key) != 0f;
}
