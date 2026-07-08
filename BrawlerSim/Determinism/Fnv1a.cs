namespace BrawlerSim.Determinism;

/// <summary>
/// FNV-1a 64-bit hashing. Used to fingerprint genomes and (from Phase 2) per-tick sim
/// state for replay verification. Chosen for simplicity and platform-independence —
/// this is an integrity fingerprint, not cryptography.
/// </summary>
public static class Fnv1a
{
    public const ulong OffsetBasis = 14695981039346656037UL;
    public const ulong Prime = 1099511628211UL;

    public static ulong Hash(ReadOnlySpan<byte> data, ulong hash = OffsetBasis)
    {
        foreach (byte b in data)
        {
            hash = unchecked((hash ^ b) * Prime);
        }
        return hash;
    }

    /// <summary>Folds a float in by exact bit pattern (never by decimal formatting).</summary>
    public static ulong Add(ulong hash, float value)
    {
        uint bits = BitConverter.SingleToUInt32Bits(value);
        for (int i = 0; i < 4; i++)
        {
            hash = unchecked((hash ^ ((bits >> (i * 8)) & 0xFF)) * Prime);
        }
        return hash;
    }

    public static ulong Add(ulong hash, int value)
    {
        uint bits = unchecked((uint)value);
        for (int i = 0; i < 4; i++)
        {
            hash = unchecked((hash ^ ((bits >> (i * 8)) & 0xFF)) * Prime);
        }
        return hash;
    }
}
