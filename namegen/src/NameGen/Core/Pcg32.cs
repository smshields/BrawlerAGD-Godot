using System;

namespace NameGen.Core
{
    /// <summary>
    /// PCG-XSH-RR 32-bit PRNG. Small, fast, platform-stable. Seed-injectable so
    /// tests are reproducible; production callers can seed from any entropy source.
    /// </summary>
    public sealed class Pcg32
    {
        private ulong _state;
        private readonly ulong _inc;

        public Pcg32(ulong seed, ulong sequence = 0xda3e39cb94b95bdbUL)
        {
            _inc = (sequence << 1) | 1UL;
            _state = 0UL;
            NextUInt();
            _state += seed;
            NextUInt();
        }

        public uint NextUInt()
        {
            ulong old = _state;
            _state = old * 6364136223846793005UL + _inc;
            uint xorshifted = (uint)(((old >> 18) ^ old) >> 27);
            int rot = (int)(old >> 59);
            return (xorshifted >> rot) | (xorshifted << ((-rot) & 31));
        }

        /// <summary>Uniform integer in [0, maxExclusive). maxExclusive must be positive.</summary>
        public int NextInt(int maxExclusive)
        {
            if (maxExclusive <= 0) throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            // Debiased via rejection sampling.
            uint bound = (uint)maxExclusive;
            uint threshold = (uint)(-bound) % bound;
            while (true)
            {
                uint r = NextUInt();
                if (r >= threshold) return (int)(r % bound);
            }
        }

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => NextUInt() * (1.0 / 4294967296.0);

        public bool NextBool(double probability) => NextDouble() < probability;
    }
}
