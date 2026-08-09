using System;

namespace Harvestline.Core.Rng
{
    /// <summary>
    /// A small, fully specified pseudo-random generator (SplitMix64). Used for the
    /// market walk and contract generation. It is deliberately NOT System.Random:
    /// the spec requires the economy to be simulated forward deterministically on app
    /// open (spec §6), so the algorithm must be identical across platforms, .NET
    /// versions, and Unity's Mono/IL2CPP — which System.Random does not guarantee.
    ///
    /// The 64-bit state is part of the save file, so a resumed session continues the
    /// exact same stream.
    /// </summary>
    public sealed class DeterministicRng
    {
        private ulong _state;

        public DeterministicRng(ulong seed) => _state = seed;

        /// <summary>Current internal state — persist this in the save and restore it.</summary>
        public ulong State
        {
            get => _state;
            set => _state = value;
        }

        public ulong NextUInt64()
        {
            unchecked
            {
                _state += 0x9E3779B97F4A7C15UL;
                ulong z = _state;
                z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
                z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
                return z ^ (z >> 31);
            }
        }

        /// <summary>Uniform double in [0,1).</summary>
        public double NextDouble()
        {
            // Use the top 53 bits for a full-precision double mantissa.
            return (NextUInt64() >> 11) * (1.0 / 9007199254740992.0);
        }

        /// <summary>Uniform double in [min,max).</summary>
        public double NextDouble(double min, double max) => min + NextDouble() * (max - min);

        /// <summary>Integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive) return minInclusive;
            ulong range = (ulong)(maxExclusive - minInclusive);
            return minInclusive + (int)(NextUInt64() % range);
        }

        /// <summary>
        /// Standard normal sample (mean 0, sd 1) via polar Box–Muller. The second
        /// generated value is intentionally discarded rather than cached, so the
        /// generator's entire observable state is the single <see cref="State"/>
        /// word — a save/restore mid-stream reproduces the exact sequence.
        /// </summary>
        public double NextGaussian()
        {
            double u, v, w;
            do
            {
                u = 2.0 * NextDouble() - 1.0;
                v = 2.0 * NextDouble() - 1.0;
                w = u * u + v * v;
            } while (w >= 1.0 || w == 0.0);

            double mul = Math.Sqrt(-2.0 * Math.Log(w) / w);
            return u * mul;
        }
    }
}
