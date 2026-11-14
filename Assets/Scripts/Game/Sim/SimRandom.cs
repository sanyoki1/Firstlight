namespace Firstlight.Game
{
    /// <summary>
    /// The sim's random numbers: SplitMix64, one 64-bit word of state, so a save can store it and a loaded game
    /// draws exactly the numbers the original would have (System.Random's state can't be saved). Same calls as
    /// System.Random where the sim uses them.
    /// </summary>
    public sealed class SimRandom
    {
        public ulong State;

        public SimRandom(int seed) { State = (ulong)(uint)seed * 0x9E3779B97F4A7C15ul ^ 0xD1B54A32D192ED03ul; }

        public ulong NextU64()
        {
            ulong z = State += 0x9E3779B97F4A7C15ul;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
            return z ^ (z >> 31);
        }

        /// <summary>0 &lt;= x &lt; 1.</summary>
        public double NextDouble() => (NextU64() >> 11) * (1.0 / 9007199254740992.0);

        /// <summary>0 &lt;= x &lt; max (0 if max &lt;= 0).</summary>
        public int Next(int max) => max <= 0 ? 0 : (int)(NextDouble() * max);

        /// <summary>min &lt;= x &lt; max.</summary>
        public int Next(int min, int max) => min + Next(max - min);
    }
}
