using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class ScenarioCatalogTests
    {
        [Fact]
        public void IdsAreUniqueCaseInsensitively()
        {
            var ids = ScenarioCatalog.All.Select(s => s.Id).ToList();
            Assert.Equal(ids.Count, ids.Distinct(StringComparer.OrdinalIgnoreCase).Count());
            Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
        }

        [Fact]
        public void OrdersAreUniqueAndAscendingInAll()
        {
            var orders = ScenarioCatalog.All.Select(s => s.Order).ToList();
            Assert.Equal(orders.Count, orders.Distinct().Count());
            Assert.Equal(orders.OrderBy(o => o), orders);
        }

        [Fact]
        public void CatalogContainsEveryKnownIdAndNothingElse()
        {
            var expected = new[]
            {
                ScenarioIds.Baseline, ScenarioIds.ContentPlacement, ScenarioIds.Rooms, ScenarioIds.Documentation,
                ScenarioIds.Datum, ScenarioIds.Naming, ScenarioIds.ContentTypes, ScenarioIds.Metadata, ScenarioIds.Warnings,
            };
            Assert.Equal(expected, ScenarioCatalog.All.Select(s => s.Id));
        }

        [Fact]
        public void BaselineIsRequiredFirstAndTheOnlyRequiredScenario()
        {
            var first = ScenarioCatalog.All[0];
            Assert.Equal(ScenarioIds.Baseline, first.Id);
            Assert.True(first.Required);
            Assert.True(first.DefaultEnabled);
            Assert.Equal(ScenarioRisk.Low, first.Risk);
            Assert.Single(ScenarioCatalog.All, s => s.Required);
        }

        [Fact]
        public void WarningsIsOptInHighRiskAndLast()
        {
            var warnings = ScenarioCatalog.Get(ScenarioIds.Warnings);
            Assert.False(warnings.DefaultEnabled);
            Assert.False(warnings.Required);
            Assert.Equal(ScenarioRisk.High, warnings.Risk);
            Assert.Equal(ScenarioCatalog.All.Max(s => s.Order), warnings.Order);
            Assert.Single(ScenarioCatalog.All, s => s.Risk == ScenarioRisk.High);
        }

        [Fact]
        public void EveryScenarioHasDisplayNameAndDescription()
        {
            foreach (var s in ScenarioCatalog.All)
            {
                Assert.False(string.IsNullOrWhiteSpace(s.DisplayName), s.Id);
                Assert.False(string.IsNullOrWhiteSpace(s.Description), s.Id);
                Assert.Contains(s.Id, s.ToString());
            }
        }

        [Fact]
        public void EveryScenarioExceptWarningsIsDefaultEnabled()
        {
            foreach (var s in ScenarioCatalog.All)
                Assert.Equal(s.Id != ScenarioIds.Warnings, s.DefaultEnabled);
        }

        [Theory]
        [InlineData("naming")]
        [InlineData("NAMING")]
        [InlineData("Naming")]
        public void FindIsCaseInsensitive(string id)
        {
            var found = ScenarioCatalog.Find(id);
            Assert.NotNull(found);
            Assert.Equal(ScenarioIds.Naming, found.Id);
        }

        [Fact]
        public void FindReturnsNullForUnknownOrNull()
        {
            Assert.Null(ScenarioCatalog.Find("nope"));
            Assert.Null(ScenarioCatalog.Find(null));
            Assert.Null(ScenarioCatalog.Find(""));
        }

        [Fact]
        public void GetThrowsForUnknown()
        {
            var ex = Assert.Throws<KeyNotFoundException>(() => ScenarioCatalog.Get("nope"));
            Assert.Contains("nope", ex.Message);
            Assert.Throws<KeyNotFoundException>(() => ScenarioCatalog.Get(null));
            Assert.Same(ScenarioCatalog.Find(ScenarioIds.Rooms), ScenarioCatalog.Get("ROOMS"));
        }

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void DefaultEnabledIdsExcludeWarningsAndIncludeBaselineForEverySeverity(GenerationSeverity severity)
        {
            var ids = ScenarioCatalog.DefaultEnabledIds(severity);
            Assert.DoesNotContain(ScenarioIds.Warnings, ids);
            Assert.Equal(ScenarioIds.Baseline, ids[0]);
            Assert.Equal(ScenarioCatalog.All.Count - 1, ids.Count);

            var orders = ids.Select(id => ScenarioCatalog.Get(id).Order).ToList();
            Assert.Equal(orders.OrderBy(o => o), orders);
        }

        [Fact]
        public void DefaultEnabledIdsDoNotDependOnSeverity()
        {
            Assert.Equal(ScenarioCatalog.DefaultEnabledIds(GenerationSeverity.Low), ScenarioCatalog.DefaultEnabledIds(GenerationSeverity.High));
        }

        [Fact]
        public void OrderedRespectsCatalogOrderRegardlessOfInputOrder()
        {
            var input = new[] { ScenarioIds.Warnings, "METADATA", ScenarioIds.Baseline, ScenarioIds.Rooms, ScenarioIds.Rooms, "bogus" };
            var ordered = ScenarioCatalog.Ordered(input);
            Assert.Equal(new[] { ScenarioIds.Baseline, ScenarioIds.Rooms, ScenarioIds.Metadata, ScenarioIds.Warnings }, ordered.Select(s => s.Id));
        }

        [Fact]
        public void OrderedToleratesNullAndEmpty()
        {
            Assert.Empty(ScenarioCatalog.Ordered(null));
            Assert.Empty(ScenarioCatalog.Ordered(Array.Empty<string>()));
        }

        [Fact]
        public void DefinitionConstructorDefaults()
        {
            Assert.Throws<ArgumentNullException>(() => new ScenarioDefinition(null, "x", "y", ScenarioRisk.Low, 1, false, true));
            var d = new ScenarioDefinition("id", null, null, ScenarioRisk.Medium, 5, false, false);
            Assert.Equal("id", d.DisplayName);
            Assert.Equal(string.Empty, d.Description);
        }
    }
}
