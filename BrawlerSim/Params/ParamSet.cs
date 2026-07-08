namespace BrawlerSim.Params;

/// <summary>
/// An immutable value assignment for every parameter in a schema — one genome segment.
/// </summary>
public sealed class ParamSet
{
    private readonly float[] _values;

    public ParamSchema Schema { get; }

    public ParamSet(ParamSchema schema, IReadOnlyList<float> values)
    {
        if (values.Count != schema.Count)
        {
            throw new ArgumentException(
                $"Schema '{schema.Name}' expects {schema.Count} values, got {values.Count}.");
        }
        Schema = schema;
        _values = values.ToArray();
    }

    public float this[int index] => _values[index];

    public float Get(string key) => _values[Schema.IndexOf(key)];

    public float[] ToArray() => (float[])_values.Clone();

    /// <summary>Returns a copy with the given params replaced. Used by genome rules.</summary>
    public ParamSet With(params (string Key, float Value)[] overrides)
    {
        float[] next = ToArray();
        foreach ((string key, float value) in overrides)
        {
            next[Schema.IndexOf(key)] = value;
        }
        return new ParamSet(Schema, next);
    }

    /// <summary>
    /// Out-of-range violations, empty when valid. Imported and evolved genomes are
    /// expected to validate cleanly; a violation indicates a bad file or an ops bug.
    /// </summary>
    public List<string> Validate()
    {
        var violations = new List<string>();
        for (int i = 0; i < Schema.Count; i++)
        {
            ParamSpec spec = Schema[i];
            if (!spec.Contains(_values[i]) || float.IsNaN(_values[i]))
            {
                violations.Add(
                    $"{Schema.Name}.{spec.Key}={_values[i]} outside [{spec.EffectiveValidMin}, {spec.EffectiveValidMax}]");
            }
        }
        return violations;
    }

    /// <summary>Params in schema order, for serialization.</summary>
    public Dictionary<string, float> ToDictionary()
    {
        var dict = new Dictionary<string, float>(Schema.Count, StringComparer.Ordinal);
        for (int i = 0; i < Schema.Count; i++)
        {
            dict[Schema[i].Key] = _values[i];
        }
        return dict;
    }

    /// <summary>
    /// Builds a ParamSet from named values. Every schema key must be present (a missing
    /// param is a data error that must surface, not default silently); keys the schema
    /// doesn't know are ignored, which lets newer files load under a schema that dropped
    /// a param and lets the legacy importer pass whole Unity objects through.
    /// </summary>
    public static ParamSet FromDictionary(ParamSchema schema, IReadOnlyDictionary<string, float> values)
    {
        float[] result = new float[schema.Count];
        for (int i = 0; i < schema.Count; i++)
        {
            string key = schema[i].Key;
            if (!values.TryGetValue(key, out result[i]))
            {
                throw new KeyNotFoundException($"Missing param '{key}' for schema '{schema.Name}'.");
            }
        }
        return new ParamSet(schema, result);
    }
}
