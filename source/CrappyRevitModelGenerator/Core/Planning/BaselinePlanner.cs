using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrappyRevitModelGenerator.Core.Geometry;

namespace CrappyRevitModelGenerator.Core.Planning
{
    /// <summary>
    /// Decides the whole baseline model — levels, grids, walls, floors and the room cells they
    /// enclose — from settings and a seeded random source, without touching Revit. When the
    /// datum scenario is enabled it also plants the layout defects from plan section 7.2 and
    /// records each one as a <see cref="PlannedDefect"/> attributed to <see cref="ScenarioIds.Datum"/>.
    ///
    /// Layout: a rectangle centred on the origin, a corridor running along X at roughly 40 %
    /// of the depth, cells in front of and behind the corridor separated by transverse
    /// partitions. Same footprint on every buildable level.
    /// </summary>
    public static class BaselinePlanner
    {
        public const string StreamLevels = "baseline/levels";
        public const string StreamLayout = "baseline/layout";
        public const string StreamWalls = "baseline/walls";
        public const string StreamFloors = "baseline/floors";
        public const string StreamGrids = "baseline/grids";

        private const double GridOverhangMm = 1500;
        private const double MinCorridorLayoutDepthMm = 9000;

        public static BaselinePlan Plan(GenerationSettings settings, SeededRandom random, bool datumDefects, GeometryTolerances tolerances = null)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (random == null) throw new ArgumentNullException(nameof(random));
            tolerances ??= GeometryTolerances.Default;

            var profile = SeverityProfile.For(settings);
            var plan = new BaselinePlan
            {
                Footprint = new Rect2D(-settings.FootprintWidthMm / 2, -settings.FootprintDepthMm / 2,
                                        settings.FootprintWidthMm / 2, settings.FootprintDepthMm / 2),
                LevelHeightMm = settings.LevelHeightMm,
            };

            PlanLevels(plan, settings, random.Stream(StreamLevels), profile, datumDefects, tolerances);
            var layout = PlanLayout(plan, settings, random.Stream(StreamLayout), profile);
            PlanWallsAndCells(plan, layout, random.Stream(StreamWalls), profile, datumDefects, tolerances);
            PlanFloors(plan, settings, random.Stream(StreamFloors), profile, datumDefects, tolerances);
            PlanGrids(plan, layout, random.Stream(StreamGrids), profile, datumDefects, tolerances);

            return plan;
        }

        // ---- Levels ----------------------------------------------------------------------

        private static void PlanLevels(BaselinePlan plan, GenerationSettings settings, RandomStream rnd, SeverityProfile profile, bool datumDefects, GeometryTolerances tol)
        {
            var h = settings.LevelHeightMm;
            var count = Math.Max(GenerationLimits.MinLevels, Math.Min(GenerationLimits.MaxLevels, settings.LevelCount));

            var oopsIndex = datumDefects && profile.LevelOopsMaxMm > 0 && count > 1 ? rnd.NextInt(1, count) : -1;

            for (var i = 0; i < count; i++)
            {
                var elevation = i * h;
                if (datumDefects && i > 0)
                {
                    var jitter = Math.Round(rnd.NextJitter(Math.Min(profile.LevelJitterMm, tol.MaxLevelJitterMm)));
                    elevation += jitter;
                    if (Math.Abs(jitter) >= 5)
                        plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                            $"Level {i + 1} elevation is {Fmt(jitter)} mm off the {Fmt(h)} mm module (slightly inconsistent level elevations)."));

                    if (i == oopsIndex)
                    {
                        var oops = Math.Round(rnd.NextDouble(profile.LevelOopsMinMm, profile.LevelOopsMaxMm));
                        elevation += oops;
                        plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                            $"Level {i + 1} is a further {Fmt(oops)} mm high with no reason (someone nudged it)."));
                    }
                }

                plan.Levels.Add(new LevelSpec
                {
                    CleanName = "Level " + (i + 1).ToString("00", CultureInfo.InvariantCulture),
                    ElevationMm = elevation,
                });
            }

            if (datumDefects && profile.IntermediateLevel && count >= 2 && count < GenerationLimits.MaxLevels)
            {
                plan.Levels.Add(new LevelSpec
                {
                    CleanName = "Mezzanine",
                    ElevationMm = Math.Round(h * rnd.NextDouble(0.38, 0.48)),
                    IsIntermediate = true,
                });
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    "An unnecessary intermediate level with no floor or walls sits between the first two levels."));
            }

            // Sort by elevation and index. Names stay with their elevation, so a jittered
            // level order still reads correctly here; the naming scenario is what scrambles it.
            var sorted = plan.Levels.OrderBy(l => l.ElevationMm).ToList();
            plan.Levels.Clear();
            for (var i = 0; i < sorted.Count; i++)
            {
                sorted[i].Index = i;
                plan.Levels.Add(sorted[i]);
            }
        }

        // ---- Layout ----------------------------------------------------------------------

        private sealed class Layout
        {
            public bool HasCorridor;
            public double CorridorY1;
            public double CorridorY2;
            public List<double> FrontPartitionX = new List<double>();
            public List<double> BackPartitionX = new List<double>();
        }

        private static Layout PlanLayout(BaselinePlan plan, GenerationSettings settings, RandomStream rnd, SeverityProfile profile)
        {
            var fp = plan.Footprint;
            var layout = new Layout();

            layout.HasCorridor = fp.Depth >= MinCorridorLayoutDepthMm;
            if (layout.HasCorridor)
            {
                var frontFraction = rnd.NextDouble(0.40, 0.45);
                var corridorWidth = Math.Round(rnd.NextDouble(profile.CorridorWidthMinMm, profile.CorridorWidthMaxMm) / 50) * 50;
                layout.CorridorY1 = Math.Round(fp.MinY + fp.Depth * frontFraction);
                layout.CorridorY2 = layout.CorridorY1 + corridorWidth;
            }
            else
            {
                layout.CorridorY1 = fp.MaxY;
                layout.CorridorY2 = fp.MaxY;
            }

            // Front partitions: evenly spaced by the severity's target cell width.
            var cellWidth = profile.CellWidthMm + Math.Round(rnd.NextJitter(300) / 50) * 50;
            var nFront = Math.Max(0, (int)Math.Round(fp.Width / cellWidth) - 1);
            if (fp.Width >= 8000) nFront = Math.Max(1, nFront);

            // Respect the wall cap: 4 exterior + 2 corridor + partitions + stubs per level.
            var buildable = plan.Levels.Count(l => l.IsBuildable);
            var perLevelFixed = 4 + (layout.HasCorridor ? 2 : 0) + profile.StubWallsPerLevel;
            var perLevelBudget = Math.Max(0, GenerationLimits.MaxWalls / Math.Max(1, buildable) - perLevelFixed);
            var nBack = layout.HasCorridor ? Math.Max(0, nFront / 2) : 0;
            while (nFront + nBack > perLevelBudget && nFront > 0)
            {
                if (nBack >= nFront && nBack > 0) nBack--; else nFront--;
            }

            for (var k = 1; k <= nFront; k++)
                layout.FrontPartitionX.Add(Math.Round(fp.MinX + fp.Width * k / (nFront + 1)));

            if (layout.HasCorridor && nBack > 0)
            {
                // Back partitions sit on every other front partition — aligned by intent.
                for (var k = 0; k < layout.FrontPartitionX.Count && layout.BackPartitionX.Count < nBack; k += 2)
                    layout.BackPartitionX.Add(layout.FrontPartitionX[k]);
            }

            return layout;
        }

        // ---- Walls and cells -------------------------------------------------------------

        private static void PlanWallsAndCells(BaselinePlan plan, Layout layout, RandomStream rnd, SeverityProfile profile, bool datumDefects, GeometryTolerances tol)
        {
            var fp = plan.Footprint;
            var buildable = plan.Levels.Where(l => l.IsBuildable).OrderBy(l => l.ElevationMm).ToList();

            for (var b = 0; b < buildable.Count; b++)
            {
                var level = buildable[b];
                var next = b + 1 < buildable.Count ? buildable[b + 1] : null;
                var height = next != null ? next.ElevationMm - level.ElevationMm : plan.LevelHeightMm;
                var isTop = next == null;

                var levelWalls = new List<WallSpec>();

                WallSpec Add(Segment2D line, WallRole role)
                {
                    var w = new WallSpec
                    {
                        LevelIndex = level.Index,
                        Line = line,
                        Role = role,
                        HeightMm = height,
                        AttachTopToLevelAbove = !isTop,
                    };
                    levelWalls.Add(w);
                    return w;
                }

                // Exterior, counter-clockwise from the bottom-left corner.
                var c = fp.Corners;
                var bottom = Add(new Segment2D(c[0], c[1]), WallRole.Exterior);
                Add(new Segment2D(c[1], c[2]), WallRole.Exterior);
                Add(new Segment2D(c[2], c[3]), WallRole.Exterior);
                Add(new Segment2D(c[3], c[0]), WallRole.Exterior);

                WallSpec corridorFront = null, corridorBack = null;
                if (layout.HasCorridor)
                {
                    corridorFront = Add(new Segment2D(fp.MinX, layout.CorridorY1, fp.MaxX, layout.CorridorY1), WallRole.Corridor);
                    corridorBack = Add(new Segment2D(fp.MinX, layout.CorridorY2, fp.MaxX, layout.CorridorY2), WallRole.Corridor);
                }

                var frontPartitions = new List<WallSpec>();
                foreach (var x in layout.FrontPartitionX)
                    frontPartitions.Add(Add(new Segment2D(x, fp.MinY, x, layout.CorridorY1), WallRole.Partition));

                var backPartitions = new List<WallSpec>();
                foreach (var x in layout.BackPartitionX)
                    backPartitions.Add(Add(new Segment2D(x, layout.CorridorY2, x, fp.MaxY), WallRole.Partition));

                var partitions = frontPartitions.Concat(backPartitions).ToList();
                var gappedPartitionX = new HashSet<double>();

                if (datumDefects)
                {
                    PlantWallDefects(plan, level, rnd, profile, tol, levelWalls, partitions, frontPartitions, corridorFront, corridorBack, bottom, layout, gappedPartitionX, height);
                }

                // Assign indices now that the level's list is final.
                foreach (var w in levelWalls)
                {
                    w.Index = plan.Walls.Count;
                    plan.Walls.Add(w);
                }

                // Cells from the IDEAL partition positions (misalignment is a few mm).
                var xs = new List<double> { fp.MinX };
                xs.AddRange(layout.FrontPartitionX);
                xs.Add(fp.MaxX);
                for (var i = 0; i + 1 < xs.Count; i++)
                {
                    var bounds = new Rect2D(xs[i], fp.MinY, xs[i + 1], layout.CorridorY1);
                    var cell = new RoomCell
                    {
                        Index = plan.Cells.Count,
                        LevelIndex = level.Index,
                        Bounds = bounds,
                        Band = CellBand.Front,
                        DoorWallIndex = (corridorFront ?? bottom).Index,
                        EnclosureBroken = gappedPartitionX.Contains(xs[i]) || gappedPartitionX.Contains(xs[i + 1]),
                    };
                    plan.Cells.Add(cell);
                }

                if (layout.HasCorridor)
                {
                    plan.Cells.Add(new RoomCell
                    {
                        Index = plan.Cells.Count,
                        LevelIndex = level.Index,
                        Bounds = new Rect2D(fp.MinX, layout.CorridorY1, fp.MaxX, layout.CorridorY2),
                        Band = CellBand.Corridor,
                        DoorWallIndex = -1,
                    });

                    var bxs = new List<double> { fp.MinX };
                    bxs.AddRange(layout.BackPartitionX);
                    bxs.Add(fp.MaxX);
                    for (var i = 0; i + 1 < bxs.Count; i++)
                    {
                        plan.Cells.Add(new RoomCell
                        {
                            Index = plan.Cells.Count,
                            LevelIndex = level.Index,
                            Bounds = new Rect2D(bxs[i], layout.CorridorY2, bxs[i + 1], fp.MaxY),
                            Band = CellBand.Back,
                            DoorWallIndex = corridorBack.Index,
                            EnclosureBroken = gappedPartitionX.Contains(bxs[i]) || gappedPartitionX.Contains(bxs[i + 1]),
                        });
                    }
                }
            }

            // Final safety net: nothing shorter than the minimum wall length reaches Revit.
            var tooShort = plan.Walls.Where(w => !CurveValidation.IsValidSegment(w.Line, tol.MinWallLengthMm)).ToList();
            foreach (var w in tooShort) plan.Walls.Remove(w);
            for (var i = 0; i < plan.Walls.Count; i++) plan.Walls[i].Index = i;
            // Cells reference walls by index; re-resolve door walls after any removal.
            if (tooShort.Count > 0)
            {
                foreach (var cell in plan.Cells)
                {
                    var host = plan.Walls.FirstOrDefault(w => w.LevelIndex == cell.LevelIndex &&
                                                              (w.Role == WallRole.Corridor || w.Role == WallRole.Exterior) &&
                                                              w.Line.IsHorizontal() &&
                                                              Math.Abs(w.Line.Start.Y - (cell.Band == CellBand.Back ? cell.Bounds.MinY : cell.Bounds.MaxY)) < 1);
                    cell.DoorWallIndex = host?.Index ?? -1;
                }
            }
        }

        private static void PlantWallDefects(BaselinePlan plan, LevelSpec level, RandomStream rnd, SeverityProfile profile, GeometryTolerances tol,
            List<WallSpec> levelWalls, List<WallSpec> partitions, List<WallSpec> frontPartitions,
            WallSpec corridorFront, WallSpec corridorBack, WallSpec bottom, Layout layout, HashSet<double> gappedPartitionX, double height)
        {
            var levelLabel = level.CleanName;

            // Walls almost, but not quite, aligned.
            foreach (var w in rnd.TakeDistinct(partitions, profile.MisalignedWallsPerLevel))
            {
                var shift = rnd.NextDouble(tol.MinWallMisalignmentMm, tol.MaxWallMisalignmentMm) * (rnd.NextBool() ? 1 : -1);
                w.Line = w.Line.OffsetLeft(shift);
                w.DefectTags.Add("misaligned");
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    $"{levelLabel}: partition at x={Fmt(w.Line.Start.X)} is {Fmt(Math.Abs(shift))} mm off its grid line (almost aligned)."));
            }

            // Tiny gaps at partition/corridor junctions — cells on either side merge.
            var gapCandidates = partitions.Where(p => !p.DefectTags.Contains("misaligned")).ToList();
            foreach (var w in rnd.TakeDistinct(gapCandidates, profile.CornerGapsPerLevel))
            {
                var gap = Math.Round(rnd.NextDouble(tol.MinCornerGapMm, tol.MaxCornerGapMm));
                // Front partitions run minY -> corridorY1 (gap at End); back run corridorY2 -> maxY (gap at Start).
                var isFront = frontPartitions.Contains(w);
                var shortened = isFront ? w.Line.Extend(0, -gap) : w.Line.Extend(-gap, 0);
                if (!CurveValidation.IsValidSegment(shortened, tol.MinWallLengthMm)) continue;
                w.Line = shortened;
                w.DefectTags.Add("corner-gap");
                gappedPartitionX.Add(Math.Round(isFront ? w.Line.Start.X : w.Line.End.X));
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    $"{levelLabel}: partition at x={Fmt(w.Line.Start.X)} stops {Fmt(gap)} mm short of the corridor wall (tiny corner gap; the rooms either side are no longer separately enclosed)."));
            }

            // Short stub walls poking through into the corridor.
            if (corridorFront != null && layout.HasCorridor)
            {
                foreach (var host in rnd.TakeDistinct(frontPartitions, profile.StubWallsPerLevel))
                {
                    var length = Math.Round(rnd.NextDouble(600, 900));
                    var corridorDepth = layout.CorridorY2 - layout.CorridorY1;
                    length = Math.Min(length, corridorDepth - 300);
                    if (length < tol.MinWallLengthMm) continue;
                    var x = host.Line.Start.X;
                    var stub = new WallSpec
                    {
                        LevelIndex = level.Index,
                        Line = new Segment2D(x, layout.CorridorY1, x, layout.CorridorY1 + length),
                        Role = WallRole.Stub,
                        HeightMm = height,
                        AttachTopToLevelAbove = false,
                        IsRoomBounding = true,
                    };
                    stub.DefectTags.Add("stub");
                    levelWalls.Add(stub);
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                        $"{levelLabel}: a {Fmt(length)} mm stub wall continues the partition at x={Fmt(x)} into the corridor and stops just outside the room boundary."));
                }
            }

            // Different type (thickness) with no visible reason.
            foreach (var w in rnd.TakeDistinct(partitions.Where(p => p.Role == WallRole.Partition).ToList(), profile.AlternateTypeWallsPerLevel))
            {
                w.TypeChoice = 1;
                w.DefectTags.Add("alternate-type");
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    $"{levelLabel}: partition {DescribeLine(w.Line)} (y {Fmt(Math.Min(w.Line.Start.Y, w.Line.End.Y))}..{Fmt(Math.Max(w.Line.Start.Y, w.Line.End.Y))}) uses a different wall type than its neighbours for no reason."));
            }

            // Odd location line.
            foreach (var w in rnd.TakeDistinct(levelWalls.Where(x => x.Role != WallRole.Stub).ToList(), profile.OddLocationLineWallsPerLevel))
            {
                w.LocationLineChoice = rnd.NextInt(1, 6);
                w.DefectTags.Add("odd-location-line");
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    $"{levelLabel}: {w.Role} wall {DescribeLine(w.Line)} uses location line option {w.LocationLineChoice} while its neighbours use the default."));
            }

            // Unattached tops with a height that does not match the level above.
            foreach (var w in rnd.TakeDistinct(levelWalls.Where(x => x.AttachTopToLevelAbove).ToList(), profile.UnattachedWallsPerLevel))
            {
                var delta = rnd.NextBool() ? -Math.Round(rnd.NextDouble(50, 110)) : Math.Round(rnd.NextDouble(80, 160));
                w.AttachTopToLevelAbove = false;
                w.HeightMm = Math.Max(1200, height + delta);
                w.DefectTags.Add("unconnected-height");
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    $"{levelLabel}: {w.Role} wall {DescribeLine(w.Line)} has an unconnected height {Fmt(Math.Abs(delta))} mm {(delta < 0 ? "short of" : "past")} the level above instead of being attached to it."));
            }

            // Inconsistent joins.
            foreach (var w in rnd.TakeDistinct(levelWalls.Where(x => x.Role == WallRole.Exterior || x.Role == WallRole.Partition).ToList(), profile.DisallowedJoinsPerLevel))
            {
                w.DisallowJoinMask = rnd.NextBool() ? 1 : 2;
                w.DefectTags.Add("join-disallowed");
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    $"{levelLabel}: {w.Role} wall {DescribeLine(w.Line)} has its join disallowed at {(w.DisallowJoinMask == 1 ? "the start" : "the end")}, so the corner does not clean up."));
            }

            // One exterior wall overrunning the corner.
            if (profile.ExteriorOverrun && bottom != null)
            {
                var overrun = Math.Round(rnd.NextDouble(150, 300));
                bottom.Line = bottom.Line.Extend(0, overrun);
                bottom.DefectTags.Add("overrun");
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                    $"{levelLabel}: the front exterior wall runs {Fmt(overrun)} mm past the corner (element beyond the nominal footprint)."));
            }
        }

        // ---- Floors ----------------------------------------------------------------------

        private static void PlanFloors(BaselinePlan plan, GenerationSettings settings, RandomStream rnd, SeverityProfile profile, bool datumDefects, GeometryTolerances tol)
        {
            if (!settings.CreateFloors) return;

            var buildable = plan.Levels.Where(l => l.IsBuildable).OrderBy(l => l.ElevationMm).ToList();
            var offsetLevel = datumDefects && profile.FloorOffset && buildable.Count > 0 ? rnd.NextInt(0, buildable.Count) : -1;
            var jogLevel = datumDefects && profile.FloorJog && buildable.Count > 0 ? rnd.NextInt(0, buildable.Count) : -1;
            var insetLevel = datumDefects && profile.FloorInset && buildable.Count > 1 ? rnd.NextInt(0, buildable.Count) : -1;
            if (insetLevel == offsetLevel) insetLevel = -1;

            for (var b = 0; b < buildable.Count; b++)
            {
                var level = buildable[b];
                var rect = plan.Footprint;
                var floor = new FloorSpec { Index = plan.Floors.Count, LevelIndex = level.Index };

                if (b == offsetLevel)
                {
                    var dx = Math.Round(rnd.NextDouble(20, 50)) * (rnd.NextBool() ? 1 : -1);
                    var dy = Math.Round(rnd.NextDouble(20, 50)) * (rnd.NextBool() ? 1 : -1);
                    rect = rect.Translate(dx, dy);
                    floor.DefectTags.Add("offset");
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                        $"{level.CleanName}: floor boundary is shifted {Fmt(dx)}, {Fmt(dy)} mm from the wall footprint."));
                }
                else if (b == insetLevel)
                {
                    var inset = Math.Round(rnd.NextDouble(80, 140));
                    rect = rect.Inflate(-inset);
                    floor.DefectTags.Add("inset");
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                        $"{level.CleanName}: floor boundary is inset {Fmt(inset)} mm from the wall centrelines while other levels are not (inconsistent offsets)."));
                }

                var loop = new List<Point2D>(rect.Corners);
                if (b == jogLevel)
                {
                    var j = Math.Round(rnd.NextDouble(300, 600));
                    // Replace the top-right corner with a small notch: three points instead of one.
                    loop = new List<Point2D>
                    {
                        new Point2D(rect.MinX, rect.MinY),
                        new Point2D(rect.MaxX, rect.MinY),
                        new Point2D(rect.MaxX, rect.MaxY - j),
                        new Point2D(rect.MaxX - j, rect.MaxY - j),
                        new Point2D(rect.MaxX - j, rect.MaxY),
                        new Point2D(rect.MinX, rect.MaxY),
                    };
                    floor.DefectTags.Add("jog");
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum,
                        $"{level.CleanName}: floor boundary has an unexplained {Fmt(j)} mm jog at one corner (unnecessary segments)."));
                }

                if (!CurveValidation.IsSimpleClosedLoop(loop, tol))
                {
                    // Never hand Revit an invalid profile; fall back to the plain rectangle.
                    loop = new List<Point2D>(plan.Footprint.Corners);
                    floor.DefectTags.Clear();
                }

                floor.Loop.AddRange(loop);
                plan.Floors.Add(floor);
            }
        }

        // ---- Grids -----------------------------------------------------------------------

        private static void PlanGrids(BaselinePlan plan, Layout layout, RandomStream rnd, SeverityProfile profile, bool datumDefects, GeometryTolerances tol)
        {
            var fp = plan.Footprint;

            // Vertical grids: numbers, left to right, at exterior and partition lines.
            var xs = new List<double> { fp.MinX };
            xs.AddRange(layout.FrontPartitionX);
            xs.Add(fp.MaxX);
            var number = 1;
            foreach (var x in xs)
            {
                plan.Grids.Add(new GridSpec
                {
                    CleanName = (number++).ToString(CultureInfo.InvariantCulture),
                    Line = new Segment2D(x, fp.MinY - GridOverhangMm, x, fp.MaxY + GridOverhangMm),
                    IsVertical = true,
                });
            }

            // Horizontal grids: letters, bottom to top, at exterior and corridor lines.
            var ys = new List<double> { fp.MinY };
            if (layout.HasCorridor) { ys.Add(layout.CorridorY1); ys.Add(layout.CorridorY2); }
            ys.Add(fp.MaxY);
            var letter = 'A';
            foreach (var y in ys)
            {
                plan.Grids.Add(new GridSpec
                {
                    CleanName = letter.ToString(),
                    Line = new Segment2D(fp.MinX - GridOverhangMm, y, fp.MaxX + GridOverhangMm, y),
                    IsVertical = false,
                });
                letter++;
            }

            if (datumDefects)
            {
                if (profile.GridExtentChaos)
                {
                    foreach (var g in plan.Grids)
                    {
                        var a = Math.Round(rnd.NextDouble(800, 3500) / 100) * 100;
                        var b = Math.Round(rnd.NextDouble(800, 3500) / 100) * 100;
                        g.Line = g.IsVertical
                            ? new Segment2D(g.Line.Start.X, fp.MinY - a, g.Line.End.X, fp.MaxY + b)
                            : new Segment2D(fp.MinX - a, g.Line.Start.Y, fp.MaxX + b, g.Line.End.Y);
                        g.DefectTags.Add("inconsistent-extent");
                    }
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum, "Grid extents are inconsistent; every grid overhangs the footprint by a different amount."));
                }

                foreach (var g in rnd.TakeDistinct(plan.Grids, profile.OneEndBubbleGrids))
                {
                    if (rnd.NextBool()) g.BubbleAtStart = false; else g.BubbleAtEnd = false;
                    g.DefectTags.Add("one-end-bubble");
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum, $"Grid {g.CleanName} shows its bubble at only one end."));
                }

                var interior = plan.Grids.Where(g => g.IsVertical && g.Line.Start.X > fp.MinX + 1 && g.Line.Start.X < fp.MaxX - 1).ToList();
                foreach (var g in rnd.TakeDistinct(interior, profile.MisalignedGrids))
                {
                    var shift = Math.Round(rnd.NextDouble(100, 300)) * (rnd.NextBool() ? 1 : -1);
                    g.Line = g.Line.OffsetLeft(shift);
                    g.DefectTags.Add("misaligned");
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum, $"Grid {g.CleanName} is {Fmt(Math.Abs(shift))} mm off the wall it was meant to align with."));
                }

                if (profile.NearCoincidentGrid && plan.Grids.Count < GenerationLimits.MaxWalls)
                {
                    var source = rnd.Pick(plan.Grids);
                    var gap = Math.Max(tol.MinNearCoincidentGapMm, Math.Round(rnd.NextDouble(tol.MinNearCoincidentGapMm, 150)));
                    var dup = new GridSpec
                    {
                        CleanName = source.CleanName + ".1",
                        Line = source.Line.OffsetLeft(gap),
                        IsVertical = source.IsVertical,
                        BubbleAtStart = source.BubbleAtStart,
                        BubbleAtEnd = source.BubbleAtEnd,
                    };
                    dup.DefectTags.Add("near-coincident");
                    plan.Grids.Add(dup);
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Datum, $"Grid {dup.CleanName} runs {Fmt(gap)} mm from grid {source.CleanName} (nearly coincident grids)."));
                }
            }

            plan.Grids.RemoveAll(g => !CurveValidation.IsValidSegment(g.Line, tol.MinCurveLengthMm));
            for (var i = 0; i < plan.Grids.Count; i++) plan.Grids[i].Index = i;
        }

        // ---- Helpers ---------------------------------------------------------------------

        private static string Fmt(double mm) => mm.ToString("0", CultureInfo.InvariantCulture);

        private static string DescribeLine(Segment2D s) =>
            s.IsHorizontal() ? $"along y={Fmt(s.Start.Y)}" : s.IsVertical() ? $"along x={Fmt(s.Start.X)}" : $"from {s.Start} to {s.End}";
    }
}
