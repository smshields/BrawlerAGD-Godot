namespace BrawlerSim.Params;

/// <summary>
/// An ordered, named collection of ParamSpecs describing one genome segment
/// (e.g. "character", "move"). Order is significant: single-point crossover
/// operates on the schema's index space, so reordering specs changes evolution
/// semantics. Extend by appending.
/// </summary>
public sealed class ParamSchema
{
    private readonly ParamSpec[] _specs;
    private readonly Dictionary<string, int> _indexByKey;

    public string Name { get; }

    public ParamSchema(string name, IEnumerable<ParamSpec> specs)
    {
        Name = name;
        _specs = specs.ToArray();
        _indexByKey = new Dictionary<string, int>(_specs.Length, StringComparer.Ordinal);
        for (int i = 0; i < _specs.Length; i++)
        {
            if (!_indexByKey.TryAdd(_specs[i].Key, i))
            {
                throw new ArgumentException($"Schema '{name}' has duplicate param key '{_specs[i].Key}'.");
            }
        }
    }

    public int Count => _specs.Length;

    public ParamSpec this[int index] => _specs[index];

    public IReadOnlyList<ParamSpec> Specs => _specs;

    public int IndexOf(string key) =>
        _indexByKey.TryGetValue(key, out int i)
            ? i
            : throw new KeyNotFoundException($"Schema '{Name}' has no param '{key}'.");

    public bool ContainsKey(string key) => _indexByKey.ContainsKey(key);
}
