using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class SeededRandomTests
    {
        private static List<int> Ints(RandomStream s, int n, int min = 0, int max = 1000)
        {
            var list = new List<int>(n);
            for (var i = 0; i < n; i++) list.Add(s.NextInt(min, max));
            return list;
        }

        private static List<double> Doubles(RandomStream s, int n)
        {
            var list = new List<double>(n);
            for (var i = 0; i < n; i++) list.Add(s.NextDouble());
            return list;
        }

        [Theory]
        [InlineData(0)]
        [InlineData(42)]
        [InlineData(-7)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void SameSeedAndStreamNameGiveIdenticalSequences(int seed)
        {
            var a = new SeededRandom(seed).Stream("naming/views");
            var b = new SeededRandom(seed).Stream("naming/views");

            Assert.Equal(Ints(a, 200), Ints(b, 200));
            Assert.Equal(Doubles(a, 200), Doubles(b, 200));

            var items = Enumerable.Range(0, 20).ToList();
            for (var i = 0; i < 20; i++) Assert.Equal(a.Pick(items), b.Pick(items));
            Assert.Equal(a.Shuffle(items), b.Shuffle(items));
            Assert.Equal(a.TakeDistinct(items, 7), b.TakeDistinct(items, 7));
            Assert.Equal(a.TakeCycling(items, 45), b.TakeCycling(items, 45));
            for (var i = 0; i < 20; i++) Assert.Equal(a.NextBool(0.3), b.NextBool(0.3));
            for (var i = 0; i < 20; i++) Assert.Equal(a.NextJitter(12.5), b.NextJitter(12.5));
        }

        [Fact]
        public void DifferentSeedsGiveDifferentSequences()
        {
            var a = Ints(new SeededRandom(1).Stream("x"), 50);
            var b = Ints(new SeededRandom(2).Stream("x"), 50);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void DifferentStreamNamesGiveDifferentSequences()
        {
            var random = new SeededRandom(42);
            var a = Ints(random.Stream("naming/views"), 50);
            var b = Ints(random.Stream("naming/sheets"), 50);
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void StreamNameIsCaseSensitive()
        {
            var random = new SeededRandom(42);
            Assert.NotEqual(Ints(random.Stream("A"), 20), Ints(random.Stream("a"), 20));
        }

        [Fact]
        public void SameNameReturnsTheSameStreamObjectContinuingItsSequence()
        {
            var random = new SeededRandom(42);
            var first = random.Stream("s");
            var again = random.Stream("s");
            Assert.Same(first, again);

            // Continuing: the second half drawn via "again" equals the second half of a fresh stream.
            var reference = Ints(new SeededRandom(42).Stream("s"), 40);
            var half1 = Ints(first, 20);
            var half2 = Ints(again, 20);
            Assert.Equal(reference, half1.Concat(half2));
        }

        [Fact]
        public void ConsumingAnotherStreamDoesNotChangeThisStream()
        {
            var reference = Ints(new SeededRandom(99).Stream("baseline/walls"), 100);

            var random = new SeededRandom(99);
            // Add and drain several unrelated streams first, in a different order.
            Ints(random.Stream("naming/views"), 500);
            Doubles(random.Stream("rooms/rooms"), 500);
            random.Stream("content/doors").Shuffle(Enumerable.Range(0, 100));
            var actual = Ints(random.Stream("baseline/walls"), 100);

            Assert.Equal(reference, actual);

            // And draining more from this stream leaves the others untouched too.
            var viewsRef = Ints(new SeededRandom(99).Stream("naming/views"), 10);
            var r2 = new SeededRandom(99);
            Ints(r2.Stream("baseline/walls"), 3);
            Assert.Equal(viewsRef, Ints(r2.Stream("naming/views"), 10));
        }

        [Fact]
        public void StreamRequiresAName()
        {
            var random = new SeededRandom(1);
            Assert.Throws<ArgumentException>(() => random.Stream(null));
            Assert.Throws<ArgumentException>(() => random.Stream(""));
        }

        [Fact]
        public void SeedIsExposed()
        {
            Assert.Equal(1234, new SeededRandom(1234).Seed);
        }

        [Theory]
        [InlineData(0, 1)]
        [InlineData(0, 2)]
        [InlineData(-5, 5)]
        [InlineData(10, 13)]
        [InlineData(int.MinValue, int.MinValue + 3)]
        [InlineData(0, int.MaxValue)]
        public void NextIntStaysWithinExclusiveUpperBound(int min, int maxExclusive)
        {
            var s = new SeededRandom(7).Stream("bounds");
            var seen = new HashSet<int>();
            for (var i = 0; i < 5000; i++)
            {
                var v = s.NextInt(min, maxExclusive);
                Assert.InRange(v, min, maxExclusive - 1);
                seen.Add(v);
            }
            // Small ranges must be fully covered in 5000 draws.
            if ((long)maxExclusive - min <= 10) Assert.Equal((int)((long)maxExclusive - min), seen.Count);
        }

        [Fact]
        public void NextIntInclusiveReachesBothEnds()
        {
            var s = new SeededRandom(3).Stream("inclusive");
            var seen = new HashSet<int>();
            for (var i = 0; i < 2000; i++)
            {
                var v = s.NextIntInclusive(4, 6);
                Assert.InRange(v, 4, 6);
                seen.Add(v);
            }
            Assert.Equal(new[] { 4, 5, 6 }, seen.OrderBy(x => x));
        }

        [Fact]
        public void NextIntWithEmptyRangeThrows()
        {
            var s = new SeededRandom(3).Stream("bad");
            Assert.Throws<ArgumentOutOfRangeException>(() => s.NextInt(5, 5));
            Assert.Throws<ArgumentOutOfRangeException>(() => s.NextInt(6, 5));
        }

        [Fact]
        public void NextDoubleIsInUnitIntervalAndRanged()
        {
            var s = new SeededRandom(11).Stream("dbl");
            for (var i = 0; i < 5000; i++)
            {
                var v = s.NextDouble();
                Assert.True(v >= 0 && v < 1, v.ToString());
                var r = s.NextDouble(-2.5, 3.5);
                Assert.True(r >= -2.5 && r < 3.5, r.ToString());
                var j = s.NextJitter(40);
                Assert.InRange(j, -40, 40);
            }
        }

        [Fact]
        public void NextBoolZeroAndOneAreConstantAndFractionalIsMixed()
        {
            var s = new SeededRandom(5).Stream("bool");
            for (var i = 0; i < 100; i++)
            {
                Assert.False(s.NextBool(0));
                Assert.False(s.NextBool(-1));
                Assert.True(s.NextBool(1));
                Assert.True(s.NextBool(2));
            }

            var trues = 0;
            for (var i = 0; i < 2000; i++) if (s.NextBool(0.5)) trues++;
            Assert.InRange(trues, 800, 1200);
        }

        [Fact]
        public void PickThrowsOnEmptyAndPickOrFallsBack()
        {
            var s = new SeededRandom(5).Stream("pick");
            Assert.Throws<ArgumentException>(() => s.Pick(new List<int>()));
            Assert.Throws<ArgumentException>(() => s.Pick<int>(null));
            Assert.Equal("fallback", s.PickOr(new List<string>(), "fallback"));
            Assert.Equal("fallback", s.PickOr<string>(null, "fallback"));
            Assert.Equal("only", s.PickOr(new[] { "only" }, "fallback"));

            var items = new[] { "a", "b", "c" };
            for (var i = 0; i < 100; i++) Assert.Contains(s.Pick(items), items);
        }

        [Fact]
        public void ShuffleIsAPermutationAndDoesNotModifyInput()
        {
            var s = new SeededRandom(8).Stream("shuffle");
            var input = Enumerable.Range(0, 50).ToList();
            var snapshot = input.ToList();

            var shuffled = s.Shuffle(input);
            Assert.Equal(snapshot, input);
            Assert.Equal(50, shuffled.Count);
            Assert.Equal(snapshot, shuffled.OrderBy(x => x));
            Assert.NotEqual(snapshot, shuffled);

            Assert.Empty(s.Shuffle(new List<int>()));
            Assert.Equal(new[] { 1 }, s.Shuffle(new[] { 1 }));
        }

        [Theory]
        [InlineData(10, 3, 3)]
        [InlineData(10, 10, 10)]
        [InlineData(10, 25, 10)]
        [InlineData(10, 0, 0)]
        [InlineData(10, -2, 0)]
        [InlineData(0, 5, 0)]
        public void TakeDistinctReturnsMinOfCountAndSize(int size, int requested, int expected)
        {
            var s = new SeededRandom(9).Stream("distinct");
            var items = Enumerable.Range(0, size).ToList();
            var taken = s.TakeDistinct(items, requested);
            Assert.Equal(expected, taken.Count);
            Assert.Equal(taken.Count, taken.Distinct().Count());
            Assert.All(taken, t => Assert.Contains(t, items));
        }

        [Fact]
        public void TakeDistinctFromNullIsEmpty()
        {
            var s = new SeededRandom(9).Stream("distinct");
            Assert.Empty(s.TakeDistinct<int>(null, 3));
        }

        [Theory]
        [InlineData(10, 3)]
        [InlineData(10, 10)]
        [InlineData(10, 25)]
        [InlineData(3, 100)]
        [InlineData(10, 0)]
        [InlineData(10, -1)]
        public void TakeCyclingReturnsExactlyCountAndSpreadsRepeats(int size, int requested)
        {
            var s = new SeededRandom(10).Stream("cycling");
            var items = Enumerable.Range(0, size).ToList();
            var taken = s.TakeCycling(items, requested);
            Assert.Equal(Math.Max(0, requested), taken.Count);
            Assert.All(taken, t => Assert.Contains(t, items));

            // Every full cycle of `size` items is itself a permutation: no item repeats before all are used.
            for (var start = 0; start + size <= taken.Count; start += size)
                Assert.Equal(size, taken.Skip(start).Take(size).Distinct().Count());
        }

        [Fact]
        public void TakeCyclingFromEmptyIsEmpty()
        {
            var s = new SeededRandom(10).Stream("cycling");
            Assert.Empty(s.TakeCycling(new List<int>(), 5));
            Assert.Empty(s.TakeCycling<int>(null, 5));
        }

        [Theory]
        [InlineData("", 14695981039346656037UL)]
        [InlineData("a", 0xaf63dc4c8601ec8cUL)]
        [InlineData("foobar", 0x85944171f73967e8UL)]
        public void Fnv1a64MatchesReferenceVectors(string text, ulong expected)
        {
            Assert.Equal(expected, SeededRandom.Fnv1a64(text));
        }

        [Fact]
        public void Fnv1a64IsStableAcrossCalls()
        {
            Assert.Equal(SeededRandom.Fnv1a64("baseline/levels"), SeededRandom.Fnv1a64("baseline/levels"));
            Assert.NotEqual(SeededRandom.Fnv1a64("baseline/levels"), SeededRandom.Fnv1a64("baseline/level"));
        }

        [Fact]
        public void NewSeedIsWithinDialogRange()
        {
            for (var i = 0; i < 200; i++)
                Assert.InRange(SeededRandom.NewSeed(), GenerationLimits.MinSeed, GenerationLimits.MaxSeed);
        }

        [Fact]
        public void UniformityIsRoughlyEven()
        {
            // 10 buckets x 20000 draws: each bucket within 15 % of its expectation.
            var s = new SeededRandom(2024).Stream("uniform");
            var buckets = new int[10];
            for (var i = 0; i < 20000; i++) buckets[s.NextInt(0, 10)]++;
            Assert.All(buckets, b => Assert.InRange(b, 1700, 2300));
        }
    }
}
