namespace BrawlerSim.Determinism;

/// <summary>
/// SplitMix64-based seed derivation. Every match evaluated during evolution gets its own
/// RNG stream derived from (runSeed, generation, individual, round), so evaluation can
/// run on any number of threads in any order and still produce identical results.
/// </summary>
public static class SeedMix
{
    public static ulong Mix(ulong x)
    {
        unchecked
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    public static ulong MatchSeed(ulong runSeed, int generation, int individual, int round)
    {
        // Sequential chaining — XOR-ing independently mixed terms cancels when two
        // terms collide (caught by SeedMixProducesDistinctDeterministicStreams).
        ulong hash = Mix(runSeed);
        hash = Mix(hash ^ (((ulong)(uint)generation << 32) | (uint)individual));
        hash = Mix(hash ^ (uint)round);
        return hash;
    }
}
