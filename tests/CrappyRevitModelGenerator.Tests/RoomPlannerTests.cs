using System.Text.RegularExpressions;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Core.Planning;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class RoomPlannerTests
    {
        private static readonly string[] CleanNames = { "Office", "Meeting Room", "Storage", "Open Office", "Break Room", "Copy Room", "Corridor" };
        private static readonly string[] BadCorridorNames = { "Corridoor", "Corridor", "Hall", "hallway", "CORR." };

        private static (BaselinePlan baseline, RoomPlan rooms) Plan(GenerationSettings settings, bool badNaming = true, bool datum = true)
        {
            var random = new SeededRandom(settings.Seed);
            var baseline = BaselinePlanner.Plan(settings, random, datum);
            var rooms = RoomPlanner.Plan(baseline, settings, random, badNaming);
            return (baseline, rooms);
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var severity in TestSupport.AllSeverities)
            foreach (var levels in new[] { 1, 3, 6 })
            foreach (var footprint in new[] { (18000.0, 12000.0), (6000.0, 6000.0), (18000.0, 8500.0), (40000.0, 40000.0) })
            foreach (var badNaming in new[] { true, false })
                yield return new object[] { severity, levels, footprint.Item1, footprint.Item2, badNaming };
        }

        // ---- Determinism ---------------------------------------------------------------------

        [Theory]
        [InlineData(GenerationSeverity.Low, 1, true)]
        [InlineData(GenerationSeverity.Medium, 42, true)]
        [InlineData(GenerationSeverity.High, 7, false)]
        public void SameSettingsAndSeedGiveIdenticalPlans(GenerationSeverity severity, int seed, bool badNaming)
        {
            var settings = TestSupport.Settings(seed, severity);
            var a = TestSupport.Dump(Plan(settings, badNaming).rooms);
            var b = TestSupport.Dump(Plan(settings.Clone(), badNaming).rooms);
            Assert.Equal(a, b);
            Assert.NotEmpty(a);
        }

        [Fact]
        public void PlanDoesNotDependOnUnrelatedStreams()
        {
            var settings = TestSupport.Settings(5);
            var reference = TestSupport.Dump(Plan(settings).rooms);

            var random = new SeededRandom(5);
            var baseline = BaselinePlanner.Plan(settings, random, true);
            ContentPlanner.Plan(baseline, settings, random, true);
            random.Stream("naming/rooms").NextDouble();
            Assert.Equal(reference, TestSupport.Dump(RoomPlanner.Plan(baseline, settings, random, true)));
        }

        [Fact]
        public void DifferentSeedsChangeThePlan()
        {
            Assert.NotEqual(TestSupport.Dump(Plan(TestSupport.Settings(1)).rooms), TestSupport.Dump(Plan(TestSupport.Settings(2)).rooms));
        }

        [Fact]
        public void PlanRejectsNullArguments()
        {
            var settings = TestSupport.Settings();
            var baseline = TestSupport.Baseline(settings);
            Assert.Throws<ArgumentNullException>(() => RoomPlanner.Plan(null, settings, new SeededRandom(1), true));
            Assert.Throws<ArgumentNullException>(() => RoomPlanner.Plan(baseline, null, new SeededRandom(1), true));
            Assert.Throws<ArgumentNullException>(() => RoomPlanner.Plan(baseline, settings, null, true));
        }

        // ---- Toggles and counts ----------------------------------------------------------------

        [Fact]
        public void CreateRoomsFalseYieldsAnEmptyPlan()
        {
            var settings = TestSupport.Settings(1);
            settings.CreateRooms = false;
            var (_, rooms) = Plan(settings);
            Assert.Empty(rooms.Rooms);
            Assert.Empty(rooms.SeparationLines);
            Assert.Empty(rooms.Defects);
            Assert.Equal(0, rooms.ElementCount);
            Assert.Equal(0, rooms.TagCount);
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void RoomCountIsBoundedByProfileAndCells(GenerationSeverity severity, int levels, double width, double depth, bool badNaming)
        {
            var (baseline, rooms) = Plan(TestSupport.Settings(3, severity, levels, width, depth), badNaming);
            var profile = SeverityProfile.For(severity);
            var cells = baseline.Cells.Count(c => c.Band != CellBand.Corridor);
            var hasCorridor = baseline.Cells.Any(c => c.Band == CellBand.Corridor);

            var primary = rooms.Rooms.Where(r => r.IsPlaced && !r.DefectTags.Contains("duplicate-in-region") && baseline.Cells[r.CellIndex].Band != CellBand.Corridor).ToList();
            var duplicates = rooms.Rooms.Where(r => r.DefectTags.Contains("duplicate-in-region")).ToList();
            var corridorRooms = rooms.Rooms.Where(r => r.IsPlaced && baseline.Cells[r.CellIndex].Band == CellBand.Corridor).ToList();
            var unplaced = rooms.Rooms.Where(r => !r.IsPlaced).ToList();

            Assert.InRange(primary.Count, Math.Min(profile.RoomsMin, cells), Math.Min(profile.RoomsMax, Math.Min(cells, GenerationLimits.MaxRooms)));
            Assert.Equal(cells > 0 ? profile.DuplicateRoomsInCell : 0, duplicates.Count);
            Assert.Equal(profile.RoomInCorridor && hasCorridor ? 1 : 0, corridorRooms.Count);
            Assert.Equal(profile.UnplacedRooms, unplaced.Count);
            Assert.Equal(primary.Count + duplicates.Count + corridorRooms.Count + unplaced.Count, rooms.Rooms.Count);
            Assert.True(rooms.Rooms.Count <= GenerationLimits.MaxRooms + profile.DuplicateRoomsInCell + 1 + profile.UnplacedRooms);

            // Primary rooms sit in distinct cells, lowest levels first.
            Assert.Equal(primary.Count, primary.Select(r => r.CellIndex).Distinct().Count());
            var levelsUsed = primary.Select(r => r.LevelIndex).ToList();
            Assert.Equal(levelsUsed.OrderBy(l => l), levelsUsed);
        }

        [Fact]
        public void DefaultFootprintReachesTheProfileMinimum()
        {
            foreach (var severity in TestSupport.AllSeverities)
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var (baseline, rooms) = Plan(TestSupport.Settings(seed, severity));
                var profile = SeverityProfile.For(severity);
                var primary = rooms.Rooms.Count(r => r.IsPlaced && !r.DefectTags.Contains("duplicate-in-region") && baseline.Cells[r.CellIndex].Band != CellBand.Corridor);
                Assert.InRange(primary, profile.RoomsMin, profile.RoomsMax);
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void RoomsAreIndexedPlacedInsideTheirCellsAndTaggedInside(GenerationSeverity severity, int levels, double width, double depth, bool badNaming)
        {
            var (baseline, rooms) = Plan(TestSupport.Settings(4, severity, levels, width, depth), badNaming);
            for (var i = 0; i < rooms.Rooms.Count; i++)
            {
                var r = rooms.Rooms[i];
                Assert.Equal(i, r.Index);
                Assert.False(string.IsNullOrWhiteSpace(r.Name));
                Assert.False(string.IsNullOrWhiteSpace(r.Number));

                if (!r.IsPlaced)
                {
                    Assert.Null(r.Location);
                    Assert.False(r.CreateTag);
                    Assert.Equal(-1, r.LevelIndex);
                    Assert.Equal(-1, r.CellIndex);
                    Assert.Contains("unplaced", r.DefectTags);
                    continue;
                }

                Assert.InRange(r.CellIndex, 0, baseline.Cells.Count - 1);
                var cell = baseline.Cells[r.CellIndex];
                Assert.Equal(cell.LevelIndex, r.LevelIndex);
                Assert.True(cell.Bounds.Contains(r.Location.Value), $"{r} not inside {cell}");
                Assert.True(baseline.Footprint.Contains(r.Location.Value));

                if (r.CreateTag)
                {
                    var tagAt = r.Location.Value.Plus(r.TagOffsetMm);
                    Assert.True(cell.Bounds.Contains(tagAt), $"tag of {r} at {tagAt} outside {cell}");
                    if (r.DefectTags.Contains("awkward-tag"))
                        Assert.True(Math.Abs(r.TagOffsetMm.X) > 0 || Math.Abs(r.TagOffsetMm.Y) > 0);
                    else
                        Assert.Equal(Point2D.Origin, r.TagOffsetMm);
                    Assert.Equal(r.FakeTag, r.DefectTags.Contains("fake-tag"));
                }
                else
                {
                    Assert.Contains("untagged", r.DefectTags);
                    Assert.False(r.FakeTag);
                }

                if (cell.EnclosureBroken && !r.DefectTags.Contains("duplicate-in-region") && cell.Band != CellBand.Corridor)
                    Assert.Contains("in-broken-enclosure", r.DefectTags);
            }

            Assert.Equal(rooms.Rooms.Count(r => r.IsPlaced && r.CreateTag && !r.FakeTag), rooms.TagCount);
            Assert.Equal(rooms.Rooms.Count(r => r.IsPlaced && r.CreateTag && r.FakeTag), rooms.FakeTagCount);
            Assert.Equal(rooms.Rooms.Count + rooms.SeparationLines.Count + rooms.TagCount + rooms.FakeTagCount * RoomPlan.ElementsPerFakeTag, rooms.ElementCount);
        }

        [Fact]
        public void TagFractionsFollowTheProfile()
        {
            foreach (var severity in TestSupport.AllSeverities)
            {
                var (_, rooms) = Plan(TestSupport.Settings(2, severity));
                var profile = SeverityProfile.For(severity);
                var placed = rooms.Rooms.Where(r => r.IsPlaced).ToList();
                var untagged = placed.Count(r => !r.CreateTag);
                var awkward = placed.Count(r => r.DefectTags.Contains("awkward-tag"));
                var fake = placed.Count(r => r.FakeTag);
                Assert.Equal((int)Math.Round(placed.Count * profile.UntaggedRoomFraction), untagged);
                Assert.Equal((int)Math.Round((placed.Count - untagged) * profile.AwkwardTagFraction), awkward);
                Assert.Equal((int)Math.Round((placed.Count - untagged) * profile.FakeRoomTagFraction), fake);
                if (untagged > 0) Assert.Contains(rooms.Defects, d => d.Message.Contains("no room tag"));
                if (awkward > 0) Assert.Contains(rooms.Defects, d => d.Message.Contains("awkwardly"));
                if (fake > 0) Assert.Contains(rooms.Defects, d => d.Message.Contains("text note"));
            }
        }

        [Fact]
        public void FakeTagsAreASubsetOfTaggedRoomsAtEverySeverity()
        {
            foreach (var severity in TestSupport.AllSeverities)
            foreach (var seed in Enumerable.Range(1, 6))
            {
                var (_, rooms) = Plan(TestSupport.Settings(seed, severity));
                var fake = rooms.Rooms.Where(r => r.FakeTag).ToList();
                Assert.All(fake, r => Assert.True(r.IsPlaced && r.CreateTag, r.ToString()));
                Assert.Equal(fake.Count, rooms.FakeTagCount);
                // A fake tag never counts as a real tag.
                Assert.Equal(rooms.Rooms.Count(r => r.IsPlaced && r.CreateTag) - fake.Count, rooms.TagCount);
            }
        }

        [Fact]
        public void DefaultSettingsPlantFakeTagsAtEverySeverity()
        {
            foreach (var severity in TestSupport.AllSeverities)
            {
                var seen = false;
                foreach (var seed in Enumerable.Range(1, 6))
                    seen |= Plan(TestSupport.Settings(seed, severity)).rooms.Rooms.Any(r => r.FakeTag);
                Assert.True(seen, $"no fake tags at {severity} across six seeds");
            }
        }

        // ---- Naming ----------------------------------------------------------------------------

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void BadNamingUsesTheBadNameLists(GenerationSeverity severity)
        {
            var (baseline, rooms) = Plan(TestSupport.Settings(6, severity), badNaming: true);
            foreach (var r in rooms.Rooms)
            {
                var inCorridor = r.IsPlaced && baseline.Cells[r.CellIndex].Band == CellBand.Corridor;
                if (inCorridor) Assert.Contains(r.Name, BadCorridorNames);
                else Assert.Contains(r.Name, BadNames.RoomNames);
                Assert.Contains(r.Number, BadNames.RoomNumbers);
                Assert.True(NameSanitizer.IsLegal(r.Name));
            }
        }

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void CleanNamingUsesCleanNamesAndSequentialNumbers(GenerationSeverity severity)
        {
            var (baseline, rooms) = Plan(TestSupport.Settings(6, severity), badNaming: false);
            var numbers = new List<string>();
            foreach (var r in rooms.Rooms)
            {
                var inCorridor = r.IsPlaced && baseline.Cells[r.CellIndex].Band == CellBand.Corridor;
                if (inCorridor) Assert.Equal("Corridor", r.Name);
                else Assert.Contains(r.Name, CleanNames);

                Assert.Matches(new Regex("^[1-9][0-9]{2}$"), r.Number);
                var expectedHundreds = (Math.Max(0, r.LevelIndex) + 1) * 100;
                Assert.Equal(expectedHundreds, int.Parse(r.Number) / 100 * 100);
                numbers.Add(r.Number);
            }
            // Clean numbers are unique.
            Assert.Equal(numbers.Count, numbers.Distinct().Count());
        }

        // ---- Separation lines ------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void SeparationLinesAreValidInsideTheFootprintAndBounded(GenerationSeverity severity, int levels, double width, double depth, bool badNaming)
        {
            var (baseline, rooms) = Plan(TestSupport.Settings(5, severity, levels, width, depth), badNaming);
            var profile = SeverityProfile.For(severity);
            Assert.True(rooms.SeparationLines.Count <= profile.SeparationLines);

            for (var i = 0; i < rooms.SeparationLines.Count; i++)
            {
                var s = rooms.SeparationLines[i];
                Assert.Equal(i, s.Index);
                Assert.True(CurveValidation.IsValidSegment(s.Line, GeometryTolerances.Default.MinCurveLengthMm), s.Line.ToString());
                Assert.True(baseline.Footprint.Contains(s.Line.Start) && baseline.Footprint.Contains(s.Line.End), s.Line.ToString());
                Assert.True(s.Line.IsVertical());
                Assert.Contains(s.LevelIndex, baseline.Levels.Where(l => l.IsBuildable).Select(l => l.Index));

                // Each line spans exactly one cell on its level, from bottom edge to top edge.
                var cell = baseline.Cells.FirstOrDefault(c => c.LevelIndex == s.LevelIndex &&
                                                             c.Bounds.MinX < s.Line.Start.X && s.Line.Start.X < c.Bounds.MaxX &&
                                                             Math.Abs(c.Bounds.MinY - Math.Min(s.Line.Start.Y, s.Line.End.Y)) < 0.001 &&
                                                             Math.Abs(c.Bounds.MaxY - Math.Max(s.Line.Start.Y, s.Line.End.Y)) < 0.001);
                Assert.NotNull(cell);
                if (s.DefectTags.Contains("wall-would-do"))
                {
                    Assert.NotEqual(CellBand.Corridor, cell.Band);
                    Assert.True(cell.Bounds.Width >= 2400);
                }
                else
                {
                    Assert.Equal(CellBand.Corridor, cell.Band);
                }
            }
        }

        [Fact]
        public void DefaultSettingsProduceSeparationLinesOfBothKinds()
        {
            var (_, rooms) = Plan(TestSupport.Settings(1, GenerationSeverity.High));
            Assert.Contains(rooms.SeparationLines, s => s.DefectTags.Contains("wall-would-do"));
            Assert.Contains(rooms.SeparationLines, s => s.DefectTags.Count == 0);
            Assert.Contains(rooms.Defects, d => d.Message.Contains("room separation line"));
        }

        // ---- Defects ---------------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void DefectsAreAttributedToRooms(GenerationSeverity severity, int levels, double width, double depth, bool badNaming)
        {
            var (_, rooms) = Plan(TestSupport.Settings(8, severity, levels, width, depth), badNaming);
            Assert.All(rooms.Defects, d => Assert.Equal(ScenarioIds.Rooms, d.ScenarioId));
            Assert.All(rooms.Defects, d => Assert.False(string.IsNullOrWhiteSpace(d.Message)));
            var profile = SeverityProfile.For(severity);
            if (profile.UnplacedRooms > 0) Assert.Contains(rooms.Defects, d => d.Message.Contains("not placed"));
            Assert.Equal(rooms.Rooms.Count(r => r.DefectTags.Contains("unplaced")), rooms.Defects.Count(d => d.Message.Contains("not placed")));
            Assert.Equal(rooms.Rooms.Count(r => r.DefectTags.Contains("duplicate-in-region")), rooms.Defects.Count(d => d.Message.Contains("second room")));
            Assert.Equal(rooms.Rooms.Count(r => r.DefectTags.Contains("in-broken-enclosure")), rooms.Defects.Count(d => d.Message.Contains("partially bounded")));
        }

        [Fact]
        public void HighSeverityPlantsDuplicateAndBrokenEnclosureRooms()
        {
            var seenDuplicate = false; var seenBroken = false;
            foreach (var seed in Enumerable.Range(1, 8))
            {
                var (baseline, rooms) = Plan(TestSupport.Settings(seed, GenerationSeverity.High));
                seenDuplicate |= rooms.Rooms.Any(r => r.DefectTags.Contains("duplicate-in-region"));
                seenBroken |= rooms.Rooms.Any(r => r.DefectTags.Contains("in-broken-enclosure"));

                // Duplicate rooms share a cell with a primary room.
                foreach (var dup in rooms.Rooms.Where(r => r.DefectTags.Contains("duplicate-in-region")))
                    Assert.Contains(rooms.Rooms, r => r != dup && r.CellIndex == dup.CellIndex && !r.DefectTags.Contains("duplicate-in-region"));
            }
            Assert.True(seenDuplicate && seenBroken, $"duplicate={seenDuplicate} broken={seenBroken}");
        }
    }
}
