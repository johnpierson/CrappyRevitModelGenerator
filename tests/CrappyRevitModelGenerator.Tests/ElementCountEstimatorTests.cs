using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Planning;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class ElementCountEstimatorTests
    {
        private static GenerationSettings With(params string[] scenarioIds)
        {
            var s = TestSupport.Settings(42);
            s.EnabledScenarioIds = scenarioIds.ToList();
            return s;
        }

        [Fact]
        public void EstimateIsDeterministic()
        {
            var settings = TestSupport.Settings(42, GenerationSeverity.High, 4);
            var a = ElementCountEstimator.Estimate(settings);
            var b = ElementCountEstimator.Estimate(settings.Clone());
            Assert.Equal(a.ToString(), b.ToString());
            Assert.Equal(a.Total, b.Total);
            Assert.Equal(a.ByCategory, b.ByCategory);
        }

        [Fact]
        public void EstimateRejectsNull()
        {
            Assert.Throws<ArgumentNullException>(() => ElementCountEstimator.Estimate(null));
        }

        [Theory]
        [InlineData(GenerationSeverity.Low, 1)]
        [InlineData(GenerationSeverity.Medium, 3)]
        [InlineData(GenerationSeverity.High, 6)]
        public void TotalIsPositiveAndDataStorageIsAlwaysOne(GenerationSeverity severity, int levels)
        {
            var settings = TestSupport.Settings(1, severity, levels);
            var estimate = ElementCountEstimator.Estimate(settings);
            Assert.True(estimate.Total > 0);
            Assert.Equal(1, estimate.Of(GeneratedCategory.DataStorage));

            var bare = With();
            var bareEstimate = ElementCountEstimator.Estimate(bare);
            Assert.Equal(1, bareEstimate.Of(GeneratedCategory.DataStorage));
            Assert.True(bareEstimate.Total > 1);
            Assert.Equal(0, bareEstimate.Of(GeneratedCategory.Doors));
            Assert.Equal(0, bareEstimate.Of(GeneratedCategory.Rooms));
            Assert.Equal(0, bareEstimate.Of(GeneratedCategory.Views));
            Assert.Equal(0, bareEstimate.Of(GeneratedCategory.Types));
        }

        [Fact]
        public void EnablingScenariosInCatalogOrderNeverDecreasesTheTotal()
        {
            foreach (var severity in TestSupport.AllSeverities)
            {
                var enabled = new List<string>();
                var previous = 0;
                foreach (var definition in ScenarioCatalog.All)
                {
                    enabled.Add(definition.Id);
                    var settings = TestSupport.Settings(7, severity);
                    settings.EnabledScenarioIds = enabled.ToList();
                    var total = ElementCountEstimator.Estimate(settings).Total;
                    Assert.True(total >= previous, $"{severity}: enabling {definition.Id} dropped the estimate from {previous} to {total}");
                    previous = total;
                }
            }
        }

        [Fact]
        public void AddingAnyScenarioToTheDefaultsNeverDecreasesTheTotal()
        {
            foreach (var severity in TestSupport.AllSeverities)
            {
                var baseSettings = TestSupport.Settings(3, severity);
                baseSettings.EnabledScenarioIds = new List<string> { ScenarioIds.Baseline };
                var baseTotal = ElementCountEstimator.Estimate(baseSettings).Total;
                foreach (var definition in ScenarioCatalog.All)
                {
                    var settings = baseSettings.Clone();
                    settings.EnabledScenarioIds.Add(definition.Id);
                    Assert.True(ElementCountEstimator.Estimate(settings).Total >= baseTotal, $"{severity}: {definition.Id}");
                }
            }
        }

        [Fact]
        public void EachOptionalScenarioAddsItsOwnCategories()
        {
            var bare = ElementCountEstimator.Estimate(With());

            var content = ElementCountEstimator.Estimate(With(ScenarioIds.ContentPlacement));
            Assert.True(content.Of(GeneratedCategory.Doors) > 0);
            Assert.True(content.Of(GeneratedCategory.Windows) > 0);
            Assert.True(content.Of(GeneratedCategory.Furniture) > 0);

            var rooms = ElementCountEstimator.Estimate(With(ScenarioIds.Rooms));
            Assert.True(rooms.Of(GeneratedCategory.Rooms) > 0);
            Assert.True(rooms.Of(GeneratedCategory.RoomTags) > 0);
            Assert.True(rooms.Of(GeneratedCategory.RoomSeparationLines) > 0);
            // Fake room tags: one text note and four detail lines (Other) each.
            Assert.True(rooms.Of(GeneratedCategory.TextNotes) > 0);
            Assert.Equal(rooms.Of(GeneratedCategory.TextNotes) * (RoomPlan.ElementsPerFakeTag - 1), rooms.Of(GeneratedCategory.Other));

            var docs = ElementCountEstimator.Estimate(With(ScenarioIds.Documentation));
            Assert.True(docs.Of(GeneratedCategory.Views) > 0);
            Assert.True(docs.Of(GeneratedCategory.Sheets) > 0);
            Assert.True(docs.Of(GeneratedCategory.Viewports) > 0);
            Assert.True(docs.Of(GeneratedCategory.TextNotes) > 0);
            Assert.True(docs.Of(GeneratedCategory.Views) <= GenerationLimits.MaxViews);
            Assert.True(docs.Of(GeneratedCategory.Sheets) <= GenerationLimits.MaxSheets);

            var types = ElementCountEstimator.Estimate(With(ScenarioIds.ContentTypes));
            Assert.True(types.Of(GeneratedCategory.Types) > 0);
            Assert.True(types.Of(GeneratedCategory.Materials) > 0);
            Assert.True(types.Of(GeneratedCategory.Types) <= GenerationLimits.MaxDuplicateTypes);
            Assert.True(types.Of(GeneratedCategory.Materials) <= GenerationLimits.MaxMaterials);

            // Naming and metadata create nothing on their own.
            Assert.Equal(bare.Total, ElementCountEstimator.Estimate(With(ScenarioIds.Naming)).Total);
            Assert.Equal(bare.Total, ElementCountEstimator.Estimate(With(ScenarioIds.Metadata)).Total);
        }

        [Fact]
        public void WarningsAddsOverlappingWallsFloorsAndDuplicateInstances()
        {
            foreach (var severity in TestSupport.AllSeverities)
            {
                var without = TestSupport.Settings(2, severity);
                without.EnabledScenarioIds = ScenarioCatalog.DefaultEnabledIds(severity).ToList();
                var with = without.Clone();
                with.EnabledScenarioIds.Add(ScenarioIds.Warnings);

                var a = ElementCountEstimator.Estimate(without);
                var b = ElementCountEstimator.Estimate(with);
                var profile = SeverityProfile.For(severity);

                Assert.Equal(a.Of(GeneratedCategory.Walls) + profile.OverlappingWalls, b.Of(GeneratedCategory.Walls));
                Assert.Equal(a.Of(GeneratedCategory.Furniture) + profile.DuplicateInstances, b.Of(GeneratedCategory.Furniture));
                Assert.Equal(a.Of(GeneratedCategory.Floors) + profile.OverlappingFloors, b.Of(GeneratedCategory.Floors));
                Assert.Equal(a.Total + profile.OverlappingWalls + profile.DuplicateInstances + profile.OverlappingFloors, b.Total);
            }
        }

        [Theory]
        [InlineData(GenerationSeverity.Low, 2, true)]
        [InlineData(GenerationSeverity.Medium, 3, true)]
        [InlineData(GenerationSeverity.High, 5, false)]
        [InlineData(GenerationSeverity.High, 6, true)]
        public void BaselineCategoriesMatchThePlannerExactly(GenerationSeverity severity, int levels, bool datum)
        {
            var settings = TestSupport.Settings(11, severity, levels);
            settings.EnabledScenarioIds = ScenarioCatalog.DefaultEnabledIds(severity).Where(id => datum || id != ScenarioIds.Datum).ToList();
            var estimate = ElementCountEstimator.Estimate(settings);

            var random = new SeededRandom(settings.Seed);
            var baseline = BaselinePlanner.Plan(settings, random, datum);
            Assert.Equal(baseline.Levels.Count, estimate.Of(GeneratedCategory.Levels));
            Assert.Equal(baseline.Grids.Count, estimate.Of(GeneratedCategory.Grids));
            Assert.Equal(baseline.Walls.Count, estimate.Of(GeneratedCategory.Walls));
            Assert.Equal(baseline.Floors.Count, estimate.Of(GeneratedCategory.Floors));

            var content = ContentPlanner.Plan(baseline, settings, random, geometryDefects: true);
            Assert.Equal(content.Openings.Count(o => o.Kind == OpeningKind.Door), estimate.Of(GeneratedCategory.Doors));
            Assert.Equal(content.Openings.Count(o => o.Kind == OpeningKind.Window), estimate.Of(GeneratedCategory.Windows));
            Assert.Equal(content.Furniture.Count, estimate.Of(GeneratedCategory.Furniture));

            var rooms = RoomPlanner.Plan(baseline, settings, random, badNaming: true);
            Assert.Equal(rooms.Rooms.Count, estimate.Of(GeneratedCategory.Rooms));
            Assert.Equal(rooms.TagCount, estimate.Of(GeneratedCategory.RoomTags));
            Assert.Equal(rooms.SeparationLines.Count, estimate.Of(GeneratedCategory.RoomSeparationLines));
        }

        [Fact]
        public void EstimateHonoursContentToggles()
        {
            var settings = TestSupport.Settings(5);
            settings.CreateFloors = false;
            settings.CreateDoorsAndWindows = false;
            settings.CreateFurniture = false;
            settings.CreateRooms = false;
            var estimate = ElementCountEstimator.Estimate(settings);
            Assert.Equal(0, estimate.Of(GeneratedCategory.Floors));
            Assert.Equal(0, estimate.Of(GeneratedCategory.Doors));
            Assert.Equal(0, estimate.Of(GeneratedCategory.Windows));
            Assert.Equal(0, estimate.Of(GeneratedCategory.Furniture));
            Assert.Equal(0, estimate.Of(GeneratedCategory.Rooms));
            Assert.Equal(0, estimate.Of(GeneratedCategory.RoomTags));
            Assert.True(estimate.Of(GeneratedCategory.Walls) > 0);
        }

        [Fact]
        public void EstimateObjectIgnoresNonPositiveAddsAndFormats()
        {
            var e = new ElementCountEstimate();
            e.Add(GeneratedCategory.Walls, 0);
            e.Add(GeneratedCategory.Walls, -3);
            Assert.Equal(0, e.Total);
            Assert.Empty(e.ByCategory);
            e.Add(GeneratedCategory.Walls, 2);
            e.Add(GeneratedCategory.Walls, 3);
            e.Add(GeneratedCategory.Doors, 1);
            Assert.Equal(5, e.Of(GeneratedCategory.Walls));
            Assert.Equal(6, e.Total);
            Assert.Equal("Doors 1, Walls 5 — total 6", e.ToString());
        }

        [Fact]
        public void EstimatesStayUnderTheHardCapAndDefaultsUnderTheDefaultMaximum()
        {
            foreach (var severity in TestSupport.AllSeverities)
            for (var levels = GenerationLimits.MinLevels; levels <= GenerationLimits.MaxLevels; levels++)
            {
                var estimate = ElementCountEstimator.Estimate(TestSupport.Settings(1, severity, levels));
                Assert.True(estimate.Total <= GenerationLimits.HardMaxElements, $"{severity} {levels}: {estimate}");
                // The out-of-the-box dialog (default level count) fits the default cap at every severity.
                if (levels == GenerationLimits.DefaultLevels)
                    Assert.True(estimate.Total <= GenerationLimits.DefaultMaxElements, $"{severity}: {estimate}");
            }
        }
    }
}
