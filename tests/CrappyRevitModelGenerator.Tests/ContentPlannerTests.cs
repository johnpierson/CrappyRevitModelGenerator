using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Core.Planning;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class ContentPlannerTests
    {
        private static readonly double[] OddSills = { 600, 750, 1050, 1200, 1350, 450 };

        private static (BaselinePlan baseline, ContentPlan content) Plan(GenerationSettings settings, bool defects = true, bool datum = true)
        {
            var random = new SeededRandom(settings.Seed);
            var baseline = BaselinePlanner.Plan(settings, random, datum);
            var content = ContentPlanner.Plan(baseline, settings, random, defects);
            return (baseline, content);
        }

        public static IEnumerable<object[]> Cases()
        {
            foreach (var severity in TestSupport.AllSeverities)
            foreach (var levels in new[] { 1, 3, 6 })
            foreach (var footprint in new[] { (18000.0, 12000.0), (6000.0, 6000.0), (18000.0, 8500.0), (40000.0, 40000.0) })
                yield return new object[] { severity, levels, footprint.Item1, footprint.Item2 };
        }

        // ---- Determinism ---------------------------------------------------------------------

        [Theory]
        [InlineData(GenerationSeverity.Low, 1)]
        [InlineData(GenerationSeverity.Medium, 42)]
        [InlineData(GenerationSeverity.High, 1234)]
        public void SameSettingsAndSeedGiveIdenticalPlans(GenerationSeverity severity, int seed)
        {
            var settings = TestSupport.Settings(seed, severity);
            var a = TestSupport.Dump(Plan(settings).content);
            var b = TestSupport.Dump(Plan(settings.Clone()).content);
            Assert.Equal(a, b);
            Assert.NotEmpty(a);
        }

        [Fact]
        public void PlanDoesNotDependOnUnrelatedStreams()
        {
            var settings = TestSupport.Settings(5);
            var reference = TestSupport.Dump(Plan(settings).content);

            var random = new SeededRandom(5);
            var baseline = BaselinePlanner.Plan(settings, random, true);
            random.Stream("rooms/rooms").NextDouble();
            random.Stream("naming/views").Shuffle(new[] { 1, 2, 3 });
            Assert.Equal(reference, TestSupport.Dump(ContentPlanner.Plan(baseline, settings, random, true)));
        }

        [Fact]
        public void DifferentSeedsChangeThePlan()
        {
            Assert.NotEqual(TestSupport.Dump(Plan(TestSupport.Settings(1)).content), TestSupport.Dump(Plan(TestSupport.Settings(2)).content));
        }

        [Fact]
        public void PlanRejectsNullArguments()
        {
            var settings = TestSupport.Settings();
            var baseline = TestSupport.Baseline(settings);
            Assert.Throws<ArgumentNullException>(() => ContentPlanner.Plan(null, settings, new SeededRandom(1), true));
            Assert.Throws<ArgumentNullException>(() => ContentPlanner.Plan(baseline, null, new SeededRandom(1), true));
            Assert.Throws<ArgumentNullException>(() => ContentPlanner.Plan(baseline, settings, null, true));
        }

        // ---- Openings ------------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void OpeningsNeverCrowdEachOtherOnTheSameWall(GenerationSeverity severity, int levels, double width, double depth)
        {
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var (_, content) = Plan(TestSupport.Settings(seed, severity, levels, width, depth));
                foreach (var group in content.Openings.GroupBy(o => o.WallIndex))
                {
                    var positions = group.Select(o => o.DistanceAlongMm).OrderBy(d => d).ToList();
                    for (var i = 0; i + 1 < positions.Count; i++)
                        Assert.True(positions[i + 1] - positions[i] >= ContentPlanner.TooCloseWindowSpacingMm - 1,
                            $"{severity} L{levels} {width}x{depth} seed {seed}: wall {group.Key} has openings {positions[i]} and {positions[i + 1]}");
                }
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void OpeningsKeepClearOfWallEnds(GenerationSeverity severity, int levels, double width, double depth)
        {
            var (baseline, content) = Plan(TestSupport.Settings(7, severity, levels, width, depth));
            for (var i = 0; i < content.Openings.Count; i++)
            {
                var o = content.Openings[i];
                Assert.Equal(i, o.Index);
                Assert.InRange(o.WallIndex, 0, baseline.Walls.Count - 1);
                var wall = baseline.Walls[o.WallIndex];
                Assert.Equal(wall.LevelIndex, o.LevelIndex);

                var clearance = o.Kind == OpeningKind.Door ? ContentPlanner.DoorNearEndMm : ContentPlanner.WindowEndClearanceMm;
                Assert.True(o.DistanceAlongMm >= clearance - 0.5, $"{o} on {wall} (len {wall.Line.Length})");
                Assert.True(o.DistanceAlongMm <= wall.Line.Length - clearance + 0.5, $"{o} on {wall} (len {wall.Line.Length})");
                Assert.Equal(Math.Round(o.DistanceAlongMm), o.DistanceAlongMm);
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void DoorsSitInDoorWallsAndWindowsInExteriorWalls(GenerationSeverity severity, int levels, double width, double depth)
        {
            var (baseline, content) = Plan(TestSupport.Settings(3, severity, levels, width, depth));
            var doorWalls = new HashSet<int>(baseline.Cells.Where(c => c.Band != CellBand.Corridor && c.DoorWallIndex >= 0).Select(c => c.DoorWallIndex));
            var nonCorridorCells = baseline.Cells.Count(c => c.Band != CellBand.Corridor);

            var doors = content.Openings.Where(o => o.Kind == OpeningKind.Door).ToList();
            var windows = content.Openings.Where(o => o.Kind == OpeningKind.Window).ToList();

            Assert.True(doors.Count <= nonCorridorCells);
            foreach (var d in doors)
            {
                Assert.Contains(d.WallIndex, doorWalls);
                Assert.Equal(0, d.SillHeightMm);
                // Doors land inside the extent of one cell that uses this wall.
                var host = baseline.Walls[d.WallIndex];
                var at = host.Line.PointAtDistance(d.DistanceAlongMm);
                Assert.Contains(baseline.Cells, c => c.DoorWallIndex == d.WallIndex && c.Bounds.MinX - 0.5 <= at.X && at.X <= c.Bounds.MaxX + 0.5);
            }
            foreach (var w in windows)
            {
                Assert.Equal(WallRole.Exterior, baseline.Walls[w.WallIndex].Role);
                Assert.False(w.FlipHand);
                Assert.False(w.FlipFacing);
            }

            // At most one door per cell edge: doors per wall <= cells that use that wall.
            foreach (var group in doors.GroupBy(d => d.WallIndex))
                Assert.True(group.Count() <= baseline.Cells.Count(c => c.DoorWallIndex == group.Key));
        }

        [Fact]
        public void DefaultSettingsProduceDoorsAndWindowsOnEveryLevel()
        {
            foreach (var severity in TestSupport.AllSeverities)
            {
                var (baseline, content) = Plan(TestSupport.Settings(2, severity));
                foreach (var level in baseline.Levels.Where(l => l.IsBuildable))
                {
                    Assert.Contains(content.Openings, o => o.Kind == OpeningKind.Door && o.LevelIndex == level.Index);
                    Assert.Contains(content.Openings, o => o.Kind == OpeningKind.Window && o.LevelIndex == level.Index);
                }
                // Every exterior wall long enough gets at least one window.
                foreach (var wall in baseline.Walls.Where(w => w.Role == WallRole.Exterior))
                    Assert.Contains(content.Openings, o => o.Kind == OpeningKind.Window && o.WallIndex == wall.Index);
            }
        }

        [Theory]
        [MemberData(nameof(Cases))]
        public void SillHeightsAreTypicalOrOneOfTheOddValues(GenerationSeverity severity, int levels, double width, double depth)
        {
            var (_, content) = Plan(TestSupport.Settings(4, severity, levels, width, depth));
            var profile = SeverityProfile.For(severity);
            var distinctOdd = new HashSet<double>();
            foreach (var w in content.Openings.Where(o => o.Kind == OpeningKind.Window))
            {
                if (w.SillHeightMm == ContentPlanner.TypicalSillMm)
                {
                    Assert.DoesNotContain("odd-sill", w.DefectTags);
                }
                else
                {
                    Assert.Contains(w.SillHeightMm, OddSills);
                    Assert.Contains("odd-sill", w.DefectTags);
                    distinctOdd.Add(w.SillHeightMm);
                }
            }
            Assert.True(distinctOdd.Count <= Math.Max(0, profile.SillHeightVarieties - 1));
        }

        [Fact]
        public void OddSillsAndTooClosePairsAppearAtHighSeverity()
        {
            var (_, content) = Plan(TestSupport.Settings(6, GenerationSeverity.High));
            Assert.Contains(content.Openings, o => o.DefectTags.Contains("odd-sill"));
            Assert.Contains(content.Openings, o => o.DefectTags.Contains("too-close"));
            Assert.Contains(content.Openings, o => o.DefectTags.Contains("near-wall-end"));
            Assert.Contains(content.Defects, d => d.Message.Contains("sill heights"));
            Assert.Contains(content.Defects, d => d.Message.Contains("apart centre-to-centre"));
            Assert.Contains(content.Defects, d => d.Message.Contains("too close to the wall end"));

            // Too-close pairs are exactly the documented spacing apart.
            var pairs = content.Openings.Where(o => o.DefectTags.Contains("too-close")).GroupBy(o => o.WallIndex);
            foreach (var g in pairs)
            {
                var ds = g.Select(o => o.DistanceAlongMm).OrderBy(d => d).ToList();
                Assert.Contains(ds.Zip(ds.Skip(1), (a, b) => b - a), gap => Math.Abs(gap - ContentPlanner.TooCloseWindowSpacingMm) < 0.001);
            }
        }

        [Fact]
        public void NearWallEndDoorsAreExactlyAtTheNearEndDistanceFromAJunction()
        {
            var (baseline, content) = Plan(TestSupport.Settings(6, GenerationSeverity.High));
            var near = content.Openings.Where(o => o.DefectTags.Contains("near-wall-end")).ToList();
            Assert.NotEmpty(near);
            foreach (var d in near)
            {
                var host = baseline.Walls[d.WallIndex];
                var x = host.Line.PointAtDistance(d.DistanceAlongMm).X;
                var junctions = baseline.Cells.Where(c => c.DoorWallIndex == d.WallIndex).SelectMany(c => new[] { c.Bounds.MinX, c.Bounds.MaxX });
                Assert.Contains(junctions, j => Math.Abs(Math.Abs(j - x) - ContentPlanner.DoorNearEndMm) < 1);
            }
        }

        [Fact]
        public void HandingDefectsSetAtLeastOneFlip()
        {
            var flipped = 0;
            foreach (var seed in Enumerable.Range(1, 5))
            {
                var (_, content) = Plan(TestSupport.Settings(seed, GenerationSeverity.High));
                foreach (var d in content.Openings.Where(o => o.DefectTags.Contains("inconsistent-handing")))
                {
                    Assert.Equal(OpeningKind.Door, d.Kind);
                    Assert.True(d.FlipHand || d.FlipFacing);
                    flipped++;
                }
                foreach (var d in content.Openings.Where(o => o.Kind == OpeningKind.Door && !o.DefectTags.Contains("inconsistent-handing")))
                    Assert.False(d.FlipHand || d.FlipFacing);
                if (content.Openings.Any(o => o.DefectTags.Contains("inconsistent-handing")))
                    Assert.Contains(content.Defects, d => d.Message.Contains("inconsistent handing"));
            }
            Assert.True(flipped > 0);
        }

        // ---- Toggles ---------------------------------------------------------------------------

        [Fact]
        public void CreateDoorsAndWindowsFalseYieldsNoOpenings()
        {
            var settings = TestSupport.Settings(1);
            settings.CreateDoorsAndWindows = false;
            var (_, content) = Plan(settings);
            Assert.Empty(content.Openings);
            Assert.NotEmpty(content.Furniture);
            Assert.DoesNotContain(content.Defects, d => d.Message.Contains("Door") || d.Message.Contains("window"));
        }

        [Fact]
        public void CreateFurnitureFalseYieldsNoFurniture()
        {
            var settings = TestSupport.Settings(1);
            settings.CreateFurniture = false;
            var (_, content) = Plan(settings);
            Assert.Empty(content.Furniture);
            Assert.NotEmpty(content.Openings);
            Assert.DoesNotContain(content.Defects, d => d.Message.Contains("Furniture"));
            Assert.Equal(content.Openings.Count, content.ElementCount);
        }

        [Fact]
        public void BothTogglesOffYieldsAnEmptyPlan()
        {
            var settings = TestSupport.Settings(1);
            settings.CreateFurniture = false;
            settings.CreateDoorsAndWindows = false;
            var (_, content) = Plan(settings);
            Assert.Equal(0, content.ElementCount);
            Assert.Empty(content.Defects);
        }

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void GeometryDefectsOffMeansNoDefectsAndTypicalEverything(GenerationSeverity severity)
        {
            var (baseline, content) = Plan(TestSupport.Settings(9, severity), defects: false);
            Assert.Empty(content.Defects);
            Assert.All(content.Openings, o => Assert.Empty(o.DefectTags));
            Assert.All(content.Furniture, f => Assert.Empty(f.DefectTags));
            Assert.All(content.Openings.Where(o => o.Kind == OpeningKind.Window), w => Assert.Equal(ContentPlanner.TypicalSillMm, w.SillHeightMm));
            Assert.All(content.Openings, o => Assert.False(o.FlipHand || o.FlipFacing));
            Assert.All(content.Furniture, f => Assert.True(f.RotationDegrees == 0 || f.RotationDegrees == 90));
            Assert.All(content.Furniture, f => Assert.True(baseline.Footprint.Contains(f.Location)));

            // Doors are centred in their cell edge (no jitter) with the normal clearance.
            foreach (var d in content.Openings.Where(o => o.Kind == OpeningKind.Door))
            {
                var host = baseline.Walls[d.WallIndex];
                var x = host.Line.PointAtDistance(d.DistanceAlongMm).X;
                Assert.Contains(baseline.Cells, c => c.DoorWallIndex == d.WallIndex && Math.Abs(c.Bounds.Center.X - x) <= 0.5);
            }
        }

        // ---- Furniture -------------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void FurnitureIsInsideItsCellUnlessDeliberatelyOutside(GenerationSeverity severity, int levels, double width, double depth)
        {
            var (baseline, content) = Plan(TestSupport.Settings(8, severity, levels, width, depth));
            var profile = SeverityProfile.For(severity);
            var fp = baseline.Footprint;

            for (var i = 0; i < content.Furniture.Count; i++)
            {
                var f = content.Furniture[i];
                Assert.Equal(i, f.Index);
                Assert.Contains(f.LevelIndex, baseline.Levels.Where(l => l.IsBuildable).Select(l => l.Index));
                Assert.Equal(Math.Round(f.Location.X), f.Location.X);
                Assert.Equal(Math.Round(f.Location.Y), f.Location.Y);

                if (f.DefectTags.Contains("outside-footprint"))
                {
                    Assert.False(fp.Contains(f.Location), f.Location.ToString());
                    Assert.True(fp.Inflate(900).Contains(f.Location), f.Location.ToString());
                    Assert.Equal(-1, f.CellIndex);
                    Assert.Equal(0, f.RotationDegrees);
                    continue;
                }

                Assert.True(fp.Contains(f.Location), f.Location.ToString());
                Assert.InRange(f.CellIndex, 0, baseline.Cells.Count - 1);
                var cell = baseline.Cells[f.CellIndex];
                Assert.Equal(cell.LevelIndex, f.LevelIndex);
                Assert.NotEqual(CellBand.Corridor, cell.Band);
                Assert.True(cell.Bounds.Contains(f.Location), $"{f.Location} not in {cell}");

                if (f.DefectTags.Contains("on-wall"))
                {
                    Assert.Equal(cell.Bounds.MinX, f.Location.X);
                }
                else
                {
                    // Ordinary furniture keeps a 700 mm inset from the cell walls.
                    Assert.True(cell.Bounds.Inflate(-700 + 0.5).Contains(f.Location), $"{f.Location} too close to walls of {cell}");
                }

                if (f.DefectTags.Contains("odd-rotation")) Assert.InRange(f.RotationDegrees, 15, 75);
                else Assert.True(f.RotationDegrees == 0 || f.RotationDegrees == 90);
            }

            Assert.True(content.Furniture.Count(f => f.DefectTags.Contains("outside-footprint")) <= profile.FurnitureOutsideFootprint);
            Assert.True(content.Furniture.Count(f => f.DefectTags.Contains("odd-rotation")) <= profile.FurnitureRotatedOddly);
            Assert.True(content.Furniture.Count(f => f.DefectTags.Contains("on-wall")) <= profile.FurnitureOnWall);
            foreach (var group in content.Furniture.Where(f => f.CellIndex >= 0).GroupBy(f => f.CellIndex))
                Assert.True(group.Count() <= profile.FurniturePerCellMax);
        }

        [Fact]
        public void HighSeverityPlantsEveryFurnitureDefectKind()
        {
            var seenOutside = false; var seenRotated = false; var seenOnWall = false;
            foreach (var seed in Enumerable.Range(1, 6))
            {
                var (_, content) = Plan(TestSupport.Settings(seed, GenerationSeverity.High));
                seenOutside |= content.Furniture.Any(f => f.DefectTags.Contains("outside-footprint"));
                seenRotated |= content.Furniture.Any(f => f.DefectTags.Contains("odd-rotation"));
                seenOnWall |= content.Furniture.Any(f => f.DefectTags.Contains("on-wall"));
            }
            Assert.True(seenOutside && seenRotated && seenOnWall, $"outside={seenOutside} rotated={seenRotated} onWall={seenOnWall}");
        }

        // ---- Defect attribution ----------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Cases))]
        public void DefectsAreAttributedToContentPlacement(GenerationSeverity severity, int levels, double width, double depth)
        {
            var (_, content) = Plan(TestSupport.Settings(10, severity, levels, width, depth));
            Assert.All(content.Defects, d => Assert.Equal(ScenarioIds.ContentPlacement, d.ScenarioId));
            Assert.All(content.Defects, d => Assert.False(string.IsNullOrWhiteSpace(d.Message)));
            Assert.Equal(content.Openings.Count + content.Furniture.Count, content.ElementCount);

            // A tagged spec always has a defect line explaining it (near-wall-end, too-close, outside, on-wall, odd-rotation, odd-sill, handing).
            if (content.Openings.Any(o => o.DefectTags.Count > 0) || content.Furniture.Any(f => f.DefectTags.Count > 0))
                Assert.NotEmpty(content.Defects);
        }
    }
}
