using System;

namespace BunkerBuster
{
    /// <summary>
    /// Small, fast, seedable random number generator (the "mulberry32" algorithm).
    /// Produces exactly the same sequence as the mulberry32 function in the browser game,
    /// so a given seed gives the same level in both versions.
    /// </summary>
    public sealed class Mulberry32
    {
        private uint _state;

        /// <summary>Creates a generator from a 32-bit seed.</summary>
        public Mulberry32(uint seed)
        {
            _state = seed;
        }

        /// <summary>Returns the next number in the range 0 (inclusive) to 1 (exclusive).</summary>
        public double Next()
        {
            unchecked
            {
                _state += 0x6D2B79F5u;
                uint t = _state;
                t = (t ^ (t >> 15)) * (t | 1u);
                t ^= t + (t ^ (t >> 7)) * (t | 61u);
                return (t ^ (t >> 14)) / 4294967296.0;
            }
        }

        /// <summary>Returns a delegate that calls <see cref="Next"/>, for code that takes a Func of double.</summary>
        public Func<double> AsFunc()
        {
            return Next;
        }
    }
}
