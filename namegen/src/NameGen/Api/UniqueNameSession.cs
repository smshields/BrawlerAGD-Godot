using System;
using System.Collections.Generic;

namespace NameGen
{
    /// <summary>
    /// Roster-scoped uniqueness on top of NameGenerator. The core generator is pure per
    /// call and knows nothing about other characters; this wrapper retries with derived
    /// seeds until the display name is unused in this session. Use one session per
    /// generated game/roster. Not thread-safe (guard externally if you name in parallel).
    /// </summary>
    public sealed class UniqueNameSession
    {
        private readonly NameGenerator _generator;
        private readonly HashSet<string> _used = new(StringComparer.OrdinalIgnoreCase);
        private readonly int _maxAttempts;

        public UniqueNameSession(NameGenerator generator, int maxAttempts = 32)
        {
            _generator = generator;
            _maxAttempts = maxAttempts;
        }

        public IReadOnlyCollection<string> UsedNames => _used;

        /// <summary>Pre-mark a name as taken (e.g. hand-authored characters).</summary>
        public void Reserve(string name) => _used.Add(name);

        public NameResult GenerateCharacterName(CharacterGenome genome, NameOptions? options = null)
            => Unique(seed => _generator.GenerateCharacterName(genome, WithSeed(options, seed)), options);

        public NameResult GenerateStageName(StageGenome genome, NameOptions? options = null)
            => Unique(seed => _generator.GenerateStageName(genome, WithSeed(options, seed)), options);

        private NameResult Unique(Func<ulong, NameResult> generate, NameOptions? options)
        {
            ulong baseSeed = options?.Seed ?? (ulong)DateTime.UtcNow.Ticks ^ (ulong)Guid.NewGuid().GetHashCode();
            NameResult result = generate(baseSeed);
            for (int attempt = 1; attempt < _maxAttempts && _used.Contains(result.Display); attempt++)
                result = generate(baseSeed + (ulong)attempt * 0x9E3779B97F4A7C15UL);
            _used.Add(result.Display); // accept a duplicate after maxAttempts rather than loop forever
            return result;
        }

        private static NameOptions WithSeed(NameOptions? options, ulong seed)
            => (options ?? NameOptions.Default) with { Seed = seed };
    }
}
