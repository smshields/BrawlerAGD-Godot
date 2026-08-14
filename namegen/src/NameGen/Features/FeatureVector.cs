using System.Collections.Generic;

namespace NameGen.Features
{
    /// <summary>
    /// Flat named-feature contract between genome extraction and the trait matrix.
    /// Scalars are in [0,1] with 0.5 as population-neutral; flags are 0 or 1.
    /// </summary>
    public sealed class FeatureVector
    {
        private readonly Dictionary<string, double> _values = new();

        public IReadOnlyDictionary<string, double> Values => _values;

        public void Set(string name, double value) => _values[name] = value;

        public void SetFlag(string name, bool value) => _values[name] = value ? 1.0 : 0.0;

        /// <summary>Missing features read as neutral (0.5): absent data should not fire traits either way.</summary>
        public double Get(string name) => _values.TryGetValue(name, out var v) ? v : 0.5;

        public bool Has(string name) => _values.ContainsKey(name);
    }
}
