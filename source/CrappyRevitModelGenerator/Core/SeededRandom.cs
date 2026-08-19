using System;
using System.Collections.Generic;
using System.Text;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// Deterministic randomness for a run. One <see cref="SeededRandom"/> per run; each scenario
    /// asks for its own named <see cref="RandomStream"/>. A stream's sequence depends only on
    /// (seed, name), so adding a new scenario — or a scenario drawing more numbers than before —
    /// never reshuffles the choices of scenarios that already existed.
    ///
    /// The generator is a small PCG32 rather than <see cref="System.Random"/>: identical output
    /// on .NET 8 and .NET 10, and no dependence on the framework keeping its seeded algorithm
    /// stable between versions.
    /// </summary>
    public sealed class SeededRandom
    {
        private readonly Dictionary<string, RandomStream> _streams = new Dictionary<string, RandomStream>(StringComparer.Ordinal);

        public SeededRandom(int seed)
        {
            Seed = seed;
        }

        public int Seed { get; }

        /// <summary>
        /// The stream for a scenario or sub-purpose. Calling twice with the same name returns the
        /// SAME stream object (continuing its sequence), so callers that need independent
        /// sub-sequences should use distinct names such as "naming/views" and "naming/sheets".
        /// </summary>
        public RandomStream Stream(string name)
        {
            if (string.IsNullOrEmpty(name)) throw new ArgumentException("Stream name is required.", nameof(name));

            if (!_streams.TryGetValue(name, out var stream))
            {
                var nameHash = Fnv1a64(name);
                var state = SplitMix64((ulong)(uint)Seed ^ nameHash);
                var sequence = SplitMix64(nameHash ^ 0x9E3779B97F4A7C15UL);
                stream = new RandomStream(state, sequence);
                _streams[name] = stream;
            }

            return stream;
        }

        /// <summary>A fresh, non-deterministic seed for the dialog's default value.</summary>
        public static int NewSeed()
        {
            var bytes = Guid.NewGuid().ToByteArray();
            var value = BitConverter.ToInt32(bytes, 0) ^ Environment.TickCount;
            var bounded = Math.Abs(value % (GenerationLimits.MaxSeed - GenerationLimits.MinSeed + 1));
            return GenerationLimits.MinSeed + bounded;
        }

        /// <summary>Stable 64-bit FNV-1a over UTF-8; string.GetHashCode is randomised per process.</summary>
        internal static ulong Fnv1a64(string text)
        {
            const ulong offset = 14695981039346656037UL;
            const ulong prime = 1099511628211UL;
            ulong hash = offset;
            foreach (var b in Encoding.UTF8.GetBytes(text))
            {
                hash ^= b;
                hash *= prime;
            }
            return hash;
        }

        internal static ulong SplitMix64(ulong x)
        {
            x += 0x9E3779B97F4A7C15UL;
            x = (x ^ (x >> 30)) * 0xBF58476D1CE4E5B9UL;
            x = (x ^ (x >> 27)) * 0x94D049BB133111EBUL;
            return x ^ (x >> 31);
        }
    }

    /// <summary>
    /// A PCG32 (XSH-RR) stream. Not thread safe; the generator runs on Revit's UI thread only.
    /// </summary>
    public sealed class RandomStream
    {
        private ulong _state;
        private readonly ulong _increment;

        internal RandomStream(ulong initialState, ulong sequence)
        {
            _increment = (sequence << 1) | 1UL;
            _state = 0;
            NextUInt32();
            _state += initialState;
            NextUInt32();
        }

        public uint NextUInt32()
        {
            var old = _state;
            _state = unchecked(old * 6364136223846793005UL + _increment);
            var xorShifted = (uint)(((old >> 18) ^ old) >> 27);
            var rot = (int)(old >> 59);
            return (xorShifted >> rot) | (xorShifted << ((-rot) & 31));
        }

        /// <summary>Uniform integer in [minInclusive, maxExclusive).</summary>
        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
                throw new ArgumentOutOfRangeException(nameof(maxExclusive), "maxExclusive must be greater than minInclusive.");

            var range = (uint)(maxExclusive - minInclusive);
            // Rejection sampling avoids modulo bias; the loop almost never runs twice.
            var threshold = (uint)((0x1_0000_0000UL - range) % range);
            while (true)
            {
                var r = NextUInt32();
                if (r >= threshold) return (int)(minInclusive + (r % range));
            }
        }

        /// <summary>Uniform integer in [minInclusive, maxInclusive].</summary>
        public int NextIntInclusive(int minInclusive, int maxInclusive) => NextInt(minInclusive, maxInclusive + 1);

        /// <summary>Uniform double in [0, 1).</summary>
        public double NextDouble() => NextUInt32() * (1.0 / 4294967296.0);

        /// <summary>Uniform double in [min, max).</summary>
        public double NextDouble(double min, double max) => min + (max - min) * NextDouble();

        /// <summary>True with the given probability (clamped to [0, 1]).</summary>
        public bool NextBool(double probability = 0.5)
        {
            if (probability <= 0) return false;
            if (probability >= 1) return true;
            return NextDouble() < probability;
        }

        /// <summary>A uniformly chosen item. Throws on an empty list.</summary>
        public T Pick<T>(IReadOnlyList<T> items)
        {
            if (items == null || items.Count == 0) throw new ArgumentException("Cannot pick from an empty list.", nameof(items));
            return items[NextInt(0, items.Count)];
        }

        /// <summary>A uniformly chosen item, or <paramref name="fallback"/> when the list is empty.</summary>
        public T PickOr<T>(IReadOnlyList<T> items, T fallback) =>
            items == null || items.Count == 0 ? fallback : items[NextInt(0, items.Count)];

        /// <summary>A new list with the items in random order (Fisher–Yates). The input is not modified.</summary>
        public List<T> Shuffle<T>(IEnumerable<T> items)
        {
            var list = new List<T>(items);
            for (var i = list.Count - 1; i > 0; i--)
            {
                var j = NextInt(0, i + 1);
                (list[i], list[j]) = (list[j], list[i]);
            }
            return list;
        }

        /// <summary>
        /// Up to <paramref name="count"/> distinct items in random order. When the source has
        /// fewer items than requested, all of them are returned (shuffled).
        /// </summary>
        public List<T> TakeDistinct<T>(IReadOnlyList<T> items, int count)
        {
            if (items == null || items.Count == 0 || count <= 0) return new List<T>();
            var shuffled = Shuffle(items);
            if (shuffled.Count > count) shuffled.RemoveRange(count, shuffled.Count - count);
            return shuffled;
        }

        /// <summary>
        /// Items drawn WITH repetition allowed but biased towards variety: cycles through a
        /// shuffled copy, reshuffling when exhausted. Useful for "pick 14 bad view names from a
        /// list of 10" where repeats are acceptable but should be spread out.
        /// </summary>
        public List<T> TakeCycling<T>(IReadOnlyList<T> items, int count)
        {
            var result = new List<T>(Math.Max(count, 0));
            if (items == null || items.Count == 0 || count <= 0) return result;
            while (result.Count < count)
            {
                foreach (var item in Shuffle(items))
                {
                    if (result.Count >= count) break;
                    result.Add(item);
                }
            }
            return result;
        }

        /// <summary>A jitter in [-magnitude, +magnitude].</summary>
        public double NextJitter(double magnitude) => NextDouble(-magnitude, magnitude);
    }
}
