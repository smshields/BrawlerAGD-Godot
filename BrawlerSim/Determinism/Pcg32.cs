namespace BrawlerSim.Determinism;

/// <summary>
/// PCG-XSH-RR 32-bit random number generator (O'Neill, pcg-random.org).
///
/// All randomness in the simulation and the evolutionary engine must flow through this
/// type. System.Random is off-limits in sim code: its algorithm is implementation-defined
/// and has already changed once between .NET versions, which would silently break replay
/// verification. Pcg32 is deterministic by construction on every platform.
///
/// Each match gets its own instance, seeded from (runSeed, generation, gameId, round),
/// so matches can be evaluated in parallel without sharing RNG state.
/// </summary>
public sealed class Pcg32
{
    private ulong _state;
    private readonly ulong _inc;

    public Pcg32(ulong seed, ulong sequence = 0)
    {
        _inc = (sequence << 1) | 1;
        _state = 0;
        NextUInt();
        _state += seed;
        NextUInt();
    }

    public uint NextUInt()
    {
        ulong oldState = _state;
        _state = unchecked(oldState * 6364136223846793005UL + _inc);
        uint xorShifted = (uint)(((oldState >> 18) ^ oldState) >> 27);
        int rot = (int)(oldState >> 59);
        return (xorShifted >> rot) | (xorShifted << (-rot & 31));
    }

    /// <summary>Uniform int in [0, maxExclusive). maxExclusive must be &gt; 0.</summary>
    public int NextInt(int maxExclusive)
    {
        // Debiased via rejection sampling (Lemire's threshold method).
        uint bound = (uint)maxExclusive;
        uint threshold = (uint)(-bound) % bound;
        while (true)
        {
            uint r = NextUInt();
            if (r >= threshold)
            {
                return (int)(r % bound);
            }
        }
    }

    /// <summary>
    /// Uniform int in [minInclusive, maxExclusive). Mirrors System.Random.Next(min, max)
    /// semantics, which the legacy generator relied on: equal bounds return minInclusive;
    /// inverted bounds throw.
    /// </summary>
    public int NextInt(int minInclusive, int maxExclusive)
    {
        if (maxExclusive < minInclusive)
        {
            throw new ArgumentOutOfRangeException(nameof(maxExclusive),
                $"maxExclusive ({maxExclusive}) must be >= minInclusive ({minInclusive}).");
        }
        if (maxExclusive == minInclusive)
        {
            return minInclusive;
        }
        return minInclusive + NextInt(maxExclusive - minInclusive);
    }

    /// <summary>Uniform float in [0, 1), with 24 bits of precision.</summary>
    public float NextFloat() => (NextUInt() >> 8) * (1f / 16777216f);

    /// <summary>Uniform float in [min, max).</summary>
    public float NextFloat(float min, float max) => min + NextFloat() * (max - min);
}
