using System.Text.RegularExpressions;
using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class RunIdentityTests
    {
        private static readonly DateTime Stamp = new DateTime(2026, 8, 18, 14, 15, 3, DateTimeKind.Utc);
        private static readonly Guid Tail = Guid.Parse("9f3adf12-0000-0000-0000-000000000000");

        [Fact]
        public void NewRunIdFollowsTheDocumentedFormat()
        {
            var id = RunIdentity.NewRunId(42, Stamp, Tail);
            Assert.Equal("20260818-141503-42-9f3a", id);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(999_999)]
        [InlineData(int.MaxValue)]
        public void NewRunIdMatchesThePatternForPositiveSeeds(int seed)
        {
            var id = RunIdentity.NewRunId(seed, Stamp, Tail);
            Assert.Matches(new Regex(@"^\d{8}-\d{6}-\d+-[0-9a-f]{4}$"), id);
            Assert.StartsWith("20260818-141503-", id);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(-42)]
        [InlineData(int.MinValue)]
        public void NewRunIdMatchesThePatternForNegativeSeeds(int seed)
        {
            var id = RunIdentity.NewRunId(seed, Stamp, Tail);
            Assert.Matches(new Regex(@"^\d{8}-\d{6}--\d+-[0-9a-f]{4}$"), id);
        }

        [Fact]
        public void NewRunIdWithoutArgumentsIsWellFormedAndUnique()
        {
            var a = RunIdentity.NewRunId(7);
            var b = RunIdentity.NewRunId(7);
            Assert.Matches(new Regex(@"^\d{8}-\d{6}-7-[0-9a-f]{4}$"), a);
            // The random tail makes collisions vanishingly unlikely even in the same second.
            Assert.NotEqual(a, b);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(42)]
        [InlineData(999_999)]
        [InlineData(-1)]
        [InlineData(-987_654)]
        [InlineData(int.MaxValue)]
        [InlineData(int.MinValue)]
        public void TryParseSeedRoundTrips(int seed)
        {
            var id = RunIdentity.NewRunId(seed, Stamp, Guid.NewGuid());
            Assert.True(RunIdentity.TryParseSeed(id, out var parsed), id);
            Assert.Equal(seed, parsed);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("garbage")]
        [InlineData("20260818-141503")]
        [InlineData("20260818-141503-notanumber-9f3a")]
        public void TryParseSeedRejectsMalformedIds(string runId)
        {
            Assert.False(RunIdentity.TryParseSeed(runId, out var seed));
            Assert.Equal(0, seed);
        }

        [Fact]
        public void VendorIdIsStable()
        {
            // Baked into the Extensible Storage schema; changing it orphans existing runs.
            Assert.Equal("DesignTechUnraveled", RunIdentity.SchemaVendorId);
        }
    }
}
