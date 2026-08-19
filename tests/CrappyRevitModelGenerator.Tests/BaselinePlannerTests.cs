using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Core.Planning;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class BaselinePlannerTests
    {
        /// <summary>Mirror of the planner's private GridOverhangMm; grids overhang by exactly this when datum defects are off.</summary>
        private const double GridOverhangMm = 1500;

        /// <summary>Every severity x level count x datum on/off, plus a few footprints. Enough variety to catch layout edge cases quickly.</summary>
        public static IEnumerable<object[]> Matrix()
        {
            foreach (var severity in TestSupport.AllSeverities)
            for (var levels = GenerationLimits.MinLevels; levels <= GenerationLimits.MaxLevels; levels++)
            foreach (var datum in new[] { true, false })
                yield return new object[] { severity, levels, datum };
        }

        public static IEnumerable<object[]> Footprints()
        {
            yield return new object[] { GenerationLimits.MinFootprintMm, GenerationLimits.MinFootprintMm };
            yield return new object[] { 8000.0, 7000.0 };
            yield return new object[] { 18000.0, 8500.0 };  // wide but no corridor (depth < 9000)
            yield return new object[] { GenerationLimits.DefaultFootprintWidthMm, GenerationLimits.DefaultFootprintDepthMm };
            yield return new object[] { 6000.0, 40000.0 };
            yield return new object[] { GenerationLimits.MaxFootprintMm, GenerationLimits.MaxFootprintMm };
        }

        // ---- Determinism ---------------------------------------------------------------------

        [Theory]
        [InlineData(GenerationSeverity.Low, 1)]
        [InlineData(GenerationSeverity.Medium, 42)]
        [InlineData(GenerationSeverity.High, 99)]
        [InlineData(GenerationSeverity.High, -3)]
        public void SameSettingsAndSeedGiveIdenticalPlans(GenerationSeverity severity, int seed)
        {
            var settings = TestSupport.Settings(seed, severity, levels: 4);
            var a = TestSupport.Dump(TestSupport.Baseline(settings));
            var b = TestSupport.Dump(TestSupport.Baseline(settings.Clone()));
            Assert.Equal(a, b);

            var c = TestSupport.Dump(TestSupport.Baseline(settings, datumDefects: false));
            var d = TestSupport.Dump(TestSupport.Baseline(settings, datumDefects: false));
            Assert.Equal(c, d);
        }

        [Fact]
        public void PlanIsIndependentOfOtherStreamsHavingBeenUsed()
        {
            var settings = TestSupport.Settings(7);
            var reference = TestSupport.Dump(BaselinePlanner.Plan(settings, new SeededRandom(7), true));

            var random = new SeededRandom(7);
            random.Stream("naming/views").NextInt(0, 100);
            random.Stream("content/doors").NextDouble();
            Assert.Equal(reference, TestSupport.Dump(BaselinePlanner.Plan(settings, random, true)));
        }

        [Fact]
        public void DifferentSeedChangesSomethingWhenDatumDefectsAreOn()
        {
            var a = TestSupport.Dump(TestSupport.Baseline(TestSupport.Settings(1)));
            var b = TestSupport.Dump(TestSupport.Baseline(TestSupport.Settings(2)));
            Assert.NotEqual(a, b);
        }

        [Fact]
        public void PlanRejectsNullArguments()
        {
            Assert.Throws<ArgumentNullException>(() => BaselinePlanner.Plan(null, new SeededRandom(1), true));
            Assert.Throws<ArgumentNullException>(() => BaselinePlanner.Plan(TestSupport.Settings(), null, true));
        }

        // ---- No datum defects ----------------------------------------------------------------

        [Theory]
        [InlineData(GenerationSeverity.Low, 1)]
        [InlineData(GenerationSeverity.Medium, 3)]
        [InlineData(GenerationSeverity.High, 6)]
        public void WithoutDatumDefectsThePlanIsClean(GenerationSeverity severity, int levels)
        {
            var settings = TestSupport.Settings(5, severity, levels, levelHeight: 3300);
            var plan = TestSupport.Baseline(settings, datumDefects: false);

            Assert.Empty(plan.Defects);
            Assert.Equal(levels, plan.Levels.Count);
            for (var i = 0; i < levels; i++)
            {
                Assert.Equal(i * 3300.0, plan.Levels[i].ElevationMm);
                Assert.False(plan.Levels[i].IsIntermediate);
                Assert.Equal($"Level {i + 1:00}", plan.Levels[i].CleanName);
            }

            var fp = plan.Footprint;
            foreach (var g in plan.Grids)
            {
                Assert.Empty(g.DefectTags);
                Assert.True(g.BubbleAtStart && g.BubbleAtEnd);
                if (g.IsVertical)
                {
                    Assert.Equal(fp.MinY - GridOverhangMm, g.Line.Start.Y);
                    Assert.Equal(fp.MaxY + GridOverhangMm, g.Line.End.Y);
                    Assert.Equal(g.Line.Start.X, g.Line.End.X);
                }
                else
                {
                    Assert.Equal(fp.MinX - GridOverhangMm, g.Line.Start.X);
                    Assert.Equal(fp.MaxX + GridOverhangMm, g.Line.End.X);
                    Assert.Equal(g.Line.Start.Y, g.Line.End.Y);
                }
            }

            foreach (var w in plan.Walls)
            {
                Assert.Empty(w.DefectTags);
                Assert.Equal(0, w.TypeChoice);
                Assert.Equal(0, w.LocationLineChoice);
                Assert.Equal(0, w.DisallowJoinMask);
                Assert.NotEqual(WallRole.Stub, w.Role);
                Assert.True(w.Line.IsHorizontal() || w.Line.IsVertical());
            }
            Assert.All(plan.Floors, f => Assert.Empty(f.DefectTags));
            Assert.All(plan.Floors, f => Assert.Equal(4, f.Loop.Count));
            Assert.All(plan.Cells, c => Assert.False(c.EnclosureBroken));

            // Exterior walls trace the footprint exactly.
            var exterior = plan.Walls.Where(w => w.Role == WallRole.Exterior && w.LevelIndex == plan.Levels[0].Index).ToList();
            Assert.Equal(4, exterior.Count);
            Assert.All(exterior, w => Assert.True(fp.Contains(w.Line.Start) && fp.Contains(w.Line.End)));
        }

        [Fact]
        public void WithoutDatumDefectsTopWallsAreUnattachedAndOthersAttach()
        {
            var plan = TestSupport.Baseline(TestSupport.Settings(3, levels: 3), datumDefects: false);
            var top = plan.Levels.Where(l => l.IsBuildable).Max(l => l.Index);
            foreach (var w in plan.Walls)
            {
                Assert.Equal(w.LevelIndex != top, w.AttachTopToLevelAbove);
                Assert.Equal(GenerationLimits.DefaultLevelHeightMm, w.HeightMm);
                Assert.True(w.IsRoomBounding);
            }
        }

        // ---- Structural invariants over the whole matrix ------------------------------------

        [Theory]
        [MemberData(nameof(Matrix))]
        public void WallsAreValidBoundedAndIndexed(GenerationSeverity severity, int levels, bool datum)
        {
            foreach (var seed in new[] { 1, 2, 3 })
            {
                var plan = TestSupport.Baseline(TestSupport.Settings(seed, severity, levels), datum);
                AssertWallInvariants(plan);
            }
        }

        [Theory]
        [MemberData(nameof(Footprints))]
        public void WallsAreValidBoundedAndIndexedForAllFootprints(double width, double depth)
        {
            foreach (var severity in TestSupport.AllSeverities)
            foreach (var levels in new[] { 1, 3, 6 })
            foreach (var datum in new[] { true, false })
            {
                var plan = TestSupport.Baseline(TestSupport.Settings(11, severity, levels, width, depth), datum);
                AssertWallInvariants(plan);
                AssertCellInvariants(plan);
                AssertFloorInvariants(plan);
                AssertGridInvariants(plan);
            }
        }

        private static void AssertWallInvariants(BaselinePlan plan)
        {
            var tol = GeometryTolerances.Default;
            Assert.InRange(plan.Walls.Count, 4, GenerationLimits.MaxWalls);

            for (var i = 0; i < plan.Walls.Count; i++)
            {
                var w = plan.Walls[i];
                Assert.Equal(i, w.Index);
                Assert.True(CurveValidation.IsValidSegment(w.Line, tol.MinWallLengthMm), w.ToString());
                Assert.True(w.HeightMm >= 1200, w.ToString());
                Assert.Contains(w.LevelIndex, plan.Levels.Where(l => l.IsBuildable).Select(l => l.Index));
                Assert.InRange(w.LocationLineChoice, 0, 5);
                Assert.InRange(w.DisallowJoinMask, 0, 3);
                Assert.InRange(w.TypeChoice, 0, 1);
            }

            var buildable = plan.Levels.Count(l => l.IsBuildable);
            Assert.True(buildable >= 1);
            foreach (var group in plan.Walls.GroupBy(w => w.LevelIndex))
            {
                Assert.True(group.Count() <= GenerationLimits.MaxWalls / buildable, $"level {group.Key}: {group.Count()} walls");
                Assert.Equal(4, group.Count(w => w.Role == WallRole.Exterior));
                Assert.InRange(group.Count(w => w.Role == WallRole.Corridor), 0, 2);
            }

            // Every buildable level gets walls.
            foreach (var level in plan.Levels.Where(l => l.IsBuildable))
                Assert.Contains(plan.Walls, w => w.LevelIndex == level.Index);
        }

        private static void AssertCellInvariants(BaselinePlan plan)
        {
            var fp = plan.Footprint;
            for (var i = 0; i < plan.Cells.Count; i++)
            {
                var c = plan.Cells[i];
                Assert.Equal(i, c.Index);
                Assert.True(fp.Contains(c.Bounds.Min, 0.001) && fp.Contains(c.Bounds.Max, 0.001), c.ToString());
                Assert.True(c.Bounds.Width > 0 && c.Bounds.Depth > 0, c.ToString());

                if (c.Band == CellBand.Corridor)
                {
                    Assert.Equal(-1, c.DoorWallIndex);
                    Assert.False(c.EnclosureBroken);
                }
                else
                {
                    Assert.True(c.DoorWallIndex >= -1 && c.DoorWallIndex < plan.Walls.Count, c.ToString());
                    if (c.DoorWallIndex >= 0)
                    {
                        var host = plan.Walls[c.DoorWallIndex];
                        Assert.Equal(c.LevelIndex, host.LevelIndex);
                        Assert.True(host.Line.IsHorizontal(), host.ToString());
                        Assert.True(host.Role == WallRole.Corridor || host.Role == WallRole.Exterior, host.ToString());
                        // The host wall runs along the cell edge that faces the corridor (or the front exterior wall).
                        var edgeY = c.Band == CellBand.Back ? c.Bounds.MinY : c.Bounds.MaxY;
                        if (host.Role == WallRole.Corridor) Assert.Equal(edgeY, host.Line.Start.Y, 3);
                        else Assert.Equal(c.Bounds.MinY, host.Line.Start.Y, 3);
                    }
                }
            }

            // Non-corridor cells on a level tile the front (and back) bands without overlap.
            foreach (var group in plan.Cells.GroupBy(c => new { c.LevelIndex, c.Band }))
            {
                var ordered = group.OrderBy(c => c.Bounds.MinX).ToList();
                for (var i = 0; i + 1 < ordered.Count; i++)
                    Assert.Equal(ordered[i].Bounds.MaxX, ordered[i + 1].Bounds.MinX, 3);
                Assert.Equal(fp.MinX, ordered[0].Bounds.MinX, 3);
                Assert.Equal(fp.MaxX, ordered[ordered.Count - 1].Bounds.MaxX, 3);
            }
        }

        private static void AssertFloorInvariants(BaselinePlan plan)
        {
            var buildable = plan.Levels.Where(l => l.IsBuildable).Select(l => l.Index).ToList();
            Assert.Equal(buildable.Count, plan.Floors.Count);
            for (var i = 0; i < plan.Floors.Count; i++)
            {
                var f = plan.Floors[i];
                Assert.Equal(i, f.Index);
                Assert.Contains(f.LevelIndex, buildable);
                Assert.True(CurveValidation.IsSimpleClosedLoop(f.Loop), $"floor {i}");
                Assert.True(CurveValidation.SignedArea(f.Loop) > 0, $"floor {i} should be counter-clockwise");
                // A floor never strays far from the footprint (offset <= 50 mm, inset <= 140 mm).
                Assert.All(f.Loop, p => Assert.True(plan.Footprint.Inflate(60).Contains(p), p.ToString()));
            }
            Assert.Equal(buildable.Count, plan.Floors.Select(f => f.LevelIndex).Distinct().Count());
        }

        private static void AssertGridInvariants(BaselinePlan plan)
        {
            var tol = GeometryTolerances.Default;
            Assert.True(plan.Grids.Count >= 4);
            for (var i = 0; i < plan.Grids.Count; i++)
            {
                var g = plan.Grids[i];
                Assert.Equal(i, g.Index);
                Assert.True(CurveValidation.IsValidSegment(g.Line, tol.MinCurveLengthMm));
                Assert.Equal(g.IsVertical, g.Line.IsVertical());
                Assert.Equal(!g.IsVertical, g.Line.IsHorizontal());
                Assert.False(string.IsNullOrWhiteSpace(g.CleanName));
                // Grids span the whole footprint.
                if (g.IsVertical) Assert.True(g.Line.Start.Y < plan.Footprint.MinY && g.Line.End.Y > plan.Footprint.MaxY);
                else Assert.True(g.Line.Start.X < plan.Footprint.MinX && g.Line.End.X > plan.Footprint.MaxX);
            }
            Assert.Equal(plan.Grids.Count, plan.Grids.Select(g => g.CleanName).Distinct().Count());

            // No two parallel grids closer than the near-coincident minimum.
            foreach (var pair in plan.Grids.SelectMany(a => plan.Grids.Where(b => b.Index > a.Index && b.IsVertical == a.IsVertical), (a, b) => (a, b)))
            {
                var gap = pair.a.IsVertical ? Math.Abs(pair.a.Line.Start.X - pair.b.Line.Start.X) : Math.Abs(pair.a.Line.Start.Y - pair.b.Line.Start.Y);
                Assert.True(gap >= tol.MinNearCoincidentGapMm - 0.001, $"grids {pair.a.CleanName} and {pair.b.CleanName} are {gap} mm apart");
            }
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public void CellsFloorsGridsAndLevelsAreConsistent(GenerationSeverity severity, int levels, bool datum)
        {
            var plan = TestSupport.Baseline(TestSupport.Settings(21, severity, levels), datum);
            AssertCellInvariants(plan);
            AssertFloorInvariants(plan);
            AssertGridInvariants(plan);

            // Levels: index = position, strictly increasing elevation, first at 0.
            for (var i = 0; i < plan.Levels.Count; i++)
            {
                Assert.Equal(i, plan.Levels[i].Index);
                if (i > 0) Assert.True(plan.Levels[i].ElevationMm > plan.Levels[i - 1].ElevationMm + 500, "levels must be well separated");
            }
            Assert.Equal(0, plan.Levels[0].ElevationMm);
            Assert.Equal(levels, plan.Levels.Count(l => !l.IsIntermediate));
            Assert.Equal(GenerationLimits.DefaultLevelHeightMm, plan.LevelHeightMm);
            Assert.Equal(plan.Levels.Count + plan.Grids.Count + plan.Walls.Count + plan.Floors.Count, plan.ElementCount);

            // Every buildable level has at least one non-corridor cell.
            foreach (var level in plan.Levels.Where(l => l.IsBuildable))
                Assert.Contains(plan.Cells, c => c.LevelIndex == level.Index && c.Band != CellBand.Corridor);
        }

        [Fact]
        public void CreateFloorsFalseYieldsNoFloors()
        {
            var settings = TestSupport.Settings(1);
            settings.CreateFloors = false;
            var plan = TestSupport.Baseline(settings);
            Assert.Empty(plan.Floors);
            Assert.NotEmpty(plan.Walls);
            Assert.DoesNotContain(plan.Defects, d => d.Message.Contains("floor boundary"));
        }

        // ---- Datum defects -------------------------------------------------------------------

        [Theory]
        [MemberData(nameof(Matrix))]
        public void EveryPlannedDefectIsAttributedToDatum(GenerationSeverity severity, int levels, bool datum)
        {
            var plan = TestSupport.Baseline(TestSupport.Settings(13, severity, levels), datum);
            Assert.All(plan.Defects, d => Assert.Equal(ScenarioIds.Datum, d.ScenarioId));
            Assert.All(plan.Defects, d => Assert.False(string.IsNullOrWhiteSpace(d.Message)));
            if (!datum) Assert.Empty(plan.Defects);
            else Assert.NotEmpty(plan.Defects);
        }

        [Fact]
        public void DatumDefectsPlantTaggedWallsAndGridsWithMatchingDefectEntries()
        {
            var plan = TestSupport.Baseline(TestSupport.Settings(8, GenerationSeverity.High, 3));

            Assert.Contains(plan.Walls, w => w.DefectTags.Contains("misaligned"));
            Assert.Contains(plan.Walls, w => w.DefectTags.Contains("alternate-type") && w.TypeChoice == 1);
            Assert.Contains(plan.Walls, w => w.DefectTags.Contains("odd-location-line") && w.LocationLineChoice >= 1);
            Assert.Contains(plan.Walls, w => w.DefectTags.Contains("join-disallowed") && (w.DisallowJoinMask == 1 || w.DisallowJoinMask == 2));
            Assert.Contains(plan.Walls, w => w.DefectTags.Contains("unconnected-height") && !w.AttachTopToLevelAbove);
            Assert.Contains(plan.Walls, w => w.DefectTags.Contains("overrun") && w.Role == WallRole.Exterior);
            Assert.Contains(plan.Grids, g => g.DefectTags.Contains("inconsistent-extent"));
            Assert.Contains(plan.Grids, g => g.DefectTags.Contains("one-end-bubble") && (g.BubbleAtStart ^ g.BubbleAtEnd));
            Assert.Contains(plan.Grids, g => g.DefectTags.Contains("near-coincident"));

            // Every tagged wall/grid corresponds to at least one defect message and vice versa in count terms.
            Assert.True(plan.Defects.Count >= plan.Walls.Count(w => w.DefectTags.Count > 0));
            Assert.Contains(plan.Defects, d => d.Message.Contains("almost aligned"));
            Assert.Contains(plan.Defects, d => d.Message.Contains("bubble at only one end"));
            Assert.Contains(plan.Defects, d => d.Message.Contains("nearly coincident"));
        }

        [Fact]
        public void MisalignedWallsStayWithinTheConfiguredRange()
        {
            var tol = GeometryTolerances.Default;
            foreach (var seed in Enumerable.Range(1, 6))
            {
                var settings = TestSupport.Settings(seed, GenerationSeverity.High, 2);
                var plan = TestSupport.Baseline(settings);
                foreach (var w in plan.Walls.Where(w => w.DefectTags.Contains("misaligned")))
                {
                    Assert.Equal(WallRole.Partition, w.Role);
                    Assert.True(w.Line.IsVertical());
                    // Cells are built from the IDEAL partition positions, so the nearest cell edge on
                    // this level is the line the wall was meant to sit on.
                    var ideal = plan.Cells.Where(c => c.LevelIndex == w.LevelIndex && c.Band != CellBand.Corridor)
                        .SelectMany(c => new[] { c.Bounds.MinX, c.Bounds.MaxX }).Distinct().ToList();
                    var nearest = ideal.Min(x => Math.Abs(x - w.Line.Start.X));
                    Assert.InRange(nearest, tol.MinWallMisalignmentMm - 0.001, tol.MaxWallMisalignmentMm + 0.001);
                }
            }
        }

        [Fact]
        public void CornerGapPartitionsMarkAdjacentCellsAsBroken()
        {
            var tol = GeometryTolerances.Default;
            var gapsSeen = 0;
            foreach (var seed in Enumerable.Range(1, 8))
            {
                var plan = TestSupport.Baseline(TestSupport.Settings(seed, GenerationSeverity.High, 2));
                var gapped = plan.Walls.Where(w => w.DefectTags.Contains("corner-gap")).ToList();
                gapsSeen += gapped.Count;

                foreach (var w in gapped)
                {
                    Assert.Equal(WallRole.Partition, w.Role);
                    Assert.True(w.Line.IsVertical());
                    var x = w.Line.Start.X;
                    var adjacent = plan.Cells.Where(c => c.LevelIndex == w.LevelIndex && c.Band != CellBand.Corridor &&
                                                         (Math.Abs(c.Bounds.MinX - x) < 1 || Math.Abs(c.Bounds.MaxX - x) < 1)).ToList();
                    Assert.NotEmpty(adjacent);
                    Assert.All(adjacent, c => Assert.True(c.EnclosureBroken, c.ToString()));

                    // The gap itself is between the configured bounds: the wall stops short of a corridor wall.
                    var corridorYs = plan.Walls.Where(o => o.LevelIndex == w.LevelIndex && o.Role == WallRole.Corridor).Select(o => o.Line.Start.Y).ToList();
                    var nearestGap = corridorYs.Min(y => Math.Min(Math.Abs(y - w.Line.Start.Y), Math.Abs(y - w.Line.End.Y)));
                    Assert.InRange(nearestGap, tol.MinCornerGapMm - 0.001, tol.MaxCornerGapMm + 0.001);
                }

                // Conversely, a broken cell always sits next to a gapped partition on its level.
                foreach (var cell in plan.Cells.Where(c => c.EnclosureBroken))
                {
                    Assert.Contains(gapped, w => w.LevelIndex == cell.LevelIndex &&
                                                 (Math.Abs(cell.Bounds.MinX - w.Line.Start.X) < 1 || Math.Abs(cell.Bounds.MaxX - w.Line.Start.X) < 1));
                }
            }
            Assert.True(gapsSeen > 0, "High severity over 8 seeds should plant at least one corner gap");
        }

        [Fact]
        public void StubWallsSitInTheCorridorAndAreNotAttached()
        {
            var plan = TestSupport.Baseline(TestSupport.Settings(4, GenerationSeverity.High, 3));
            var stubs = plan.Walls.Where(w => w.Role == WallRole.Stub).ToList();
            Assert.NotEmpty(stubs);
            foreach (var s in stubs)
            {
                Assert.Contains("stub", s.DefectTags);
                Assert.False(s.AttachTopToLevelAbove);
                Assert.True(s.Line.IsVertical());
                var corridor = plan.Cells.Single(c => c.LevelIndex == s.LevelIndex && c.Band == CellBand.Corridor);
                Assert.True(corridor.Bounds.Contains(s.Line.Start, 0.001) && corridor.Bounds.Contains(s.Line.End, 0.001), s.ToString());
                Assert.True(s.Line.Length >= GeometryTolerances.Default.MinWallLengthMm);
            }
        }

        [Fact]
        public void LevelDefectsFollowTheProfile()
        {
            var h = GenerationLimits.DefaultLevelHeightMm;
            var tol = GeometryTolerances.Default;
            foreach (var severity in TestSupport.AllSeverities)
            {
                var profile = SeverityProfile.For(severity);
                var plan = TestSupport.Baseline(TestSupport.Settings(17, severity, 4));
                var main = plan.Levels.Where(l => !l.IsIntermediate).ToList();
                Assert.Equal(0, main[0].ElevationMm);
                for (var i = 1; i < main.Count; i++)
                {
                    var off = main[i].ElevationMm - i * h;
                    Assert.True(Math.Abs(off) <= Math.Min(profile.LevelJitterMm, tol.MaxLevelJitterMm) + profile.LevelOopsMaxMm + 0.5, $"{severity} level {i} off by {off}");
                    Assert.Equal(Math.Round(main[i].ElevationMm), main[i].ElevationMm);
                }
            }
        }

        [Theory]
        [MemberData(nameof(Matrix))]
        public void IntermediateLevelAppearsExactlyWhenTheProfileSaysSo(GenerationSeverity severity, int levels, bool datum)
        {
            var plan = TestSupport.Baseline(TestSupport.Settings(2, severity, levels), datum);
            var profile = SeverityProfile.For(severity);
            var expected = datum && profile.IntermediateLevel && levels >= 2 && levels < GenerationLimits.MaxLevels ? 1 : 0;

            var intermediates = plan.Levels.Where(l => l.IsIntermediate).ToList();
            Assert.Equal(expected, intermediates.Count);
            Assert.Equal(levels + expected, plan.Levels.Count);
            if (expected == 1)
            {
                var mezz = intermediates[0];
                Assert.Equal("Mezzanine", mezz.CleanName);
                Assert.False(mezz.IsBuildable);
                Assert.InRange(mezz.ElevationMm, 0.38 * GenerationLimits.DefaultLevelHeightMm - 1, 0.48 * GenerationLimits.DefaultLevelHeightMm + 1);
                Assert.Equal(1, mezz.Index);
                Assert.DoesNotContain(plan.Walls, w => w.LevelIndex == mezz.Index);
                Assert.DoesNotContain(plan.Floors, f => f.LevelIndex == mezz.Index);
                Assert.DoesNotContain(plan.Cells, c => c.LevelIndex == mezz.Index);
                Assert.Contains(plan.Defects, d => d.Message.Contains("intermediate level"));
            }
        }

        [Fact]
        public void FloorDefectsAreValidAndTagged()
        {
            var seenOffset = false; var seenJog = false; var seenInset = false;
            foreach (var seed in Enumerable.Range(1, 10))
            {
                var plan = TestSupport.Baseline(TestSupport.Settings(seed, GenerationSeverity.High, 4));
                foreach (var f in plan.Floors)
                {
                    Assert.True(CurveValidation.IsSimpleClosedLoop(f.Loop));
                    if (f.DefectTags.Contains("jog")) { seenJog = true; Assert.Equal(6, f.Loop.Count); }
                    if (f.DefectTags.Contains("offset")) { seenOffset = true; Assert.NotEqual(plan.Footprint.Corners, f.Loop.Take(4)); }
                    if (f.DefectTags.Contains("inset")) { seenInset = true; Assert.All(f.Loop, p => Assert.True(plan.Footprint.Inflate(-79).Contains(p))); }
                    Assert.False(f.DefectTags.Contains("offset") && f.DefectTags.Contains("inset"), "offset and inset are exclusive");
                }
            }
            Assert.True(seenOffset && seenJog && seenInset, $"offset={seenOffset} jog={seenJog} inset={seenInset}");
        }

        // ---- Small footprint -----------------------------------------------------------------

        [Theory]
        [InlineData(GenerationSeverity.Low)]
        [InlineData(GenerationSeverity.Medium)]
        [InlineData(GenerationSeverity.High)]
        public void MinimumFootprintHasNoCorridorAndIsStillValid(GenerationSeverity severity)
        {
            foreach (var datum in new[] { true, false })
            {
                var settings = TestSupport.Settings(9, severity, 2, GenerationLimits.MinFootprintMm, GenerationLimits.MinFootprintMm);
                var plan = TestSupport.Baseline(settings, datum);

                Assert.DoesNotContain(plan.Cells, c => c.Band == CellBand.Corridor);
                Assert.DoesNotContain(plan.Cells, c => c.Band == CellBand.Back);
                Assert.DoesNotContain(plan.Walls, w => w.Role == WallRole.Corridor);
                Assert.DoesNotContain(plan.Walls, w => w.Role == WallRole.Stub);
                Assert.All(plan.Cells, c => Assert.Equal(CellBand.Front, c.Band));
                // At most one partition can fit a 6 m footprint; every wall is exterior or partition.
                Assert.True(plan.Walls.Count(w => w.Role == WallRole.Partition) <= plan.Levels.Count(l => l.IsBuildable));
                Assert.All(plan.Walls, w => Assert.True(w.Role == WallRole.Exterior || w.Role == WallRole.Partition));

                AssertWallInvariants(plan);
                AssertCellInvariants(plan);
                AssertFloorInvariants(plan);
                AssertGridInvariants(plan);

                // Doors go into the front exterior wall when there is no corridor.
                foreach (var cell in plan.Cells)
                {
                    Assert.True(cell.DoorWallIndex >= 0);
                    Assert.Equal(WallRole.Exterior, plan.Walls[cell.DoorWallIndex].Role);
                }
                Assert.Equal(2, plan.Grids.Count(g => !g.IsVertical && !g.DefectTags.Contains("near-coincident")));
            }
        }

        [Fact]
        public void MaximumFootprintRespectsTheWallCapAtEveryLevelCount()
        {
            for (var levels = 1; levels <= GenerationLimits.MaxLevels; levels++)
            foreach (var severity in TestSupport.AllSeverities)
            {
                var settings = TestSupport.Settings(5, severity, levels, GenerationLimits.MaxFootprintMm, GenerationLimits.MaxFootprintMm);
                var plan = TestSupport.Baseline(settings, datumDefects: true);
                Assert.True(plan.Walls.Count <= GenerationLimits.MaxWalls, $"{severity} {levels}: {plan.Walls.Count}");
                Assert.True(plan.Walls.Count(w => w.Role == WallRole.Partition) > 0);
            }
        }
    }
}
