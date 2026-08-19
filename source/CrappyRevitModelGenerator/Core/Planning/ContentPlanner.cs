using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrappyRevitModelGenerator.Core.Geometry;

namespace CrappyRevitModelGenerator.Core.Planning
{
    /// <summary>
    /// Decides where doors, windows and furniture go (plan section 7.3) from a
    /// <see cref="BaselinePlan"/>. Positions are along-wall distances and plan points in
    /// millimetres; the Revit-side scenario resolves families and places instances. Every
    /// deliberate misplacement is recorded as a <see cref="PlannedDefect"/> attributed to
    /// <see cref="ScenarioIds.ContentPlacement"/>.
    ///
    /// Spacing rules keep every placement valid: openings never overlap each other and never
    /// straddle a wall end, however "too close" they are meant to look.
    /// </summary>
    public static class ContentPlanner
    {
        public const string StreamDoors = "content/doors";
        public const string StreamWindows = "content/windows";
        public const string StreamFurniture = "content/furniture";

        /// <summary>Nominal opening widths used only for spacing; the real families may differ a little.</summary>
        public const double NominalDoorWidthMm = 900;
        public const double NominalWindowWidthMm = 1200;

        /// <summary>Closest a door centre may be to a wall end or a partition junction (edge ~250 mm from the corner: clearly bad, still valid).</summary>
        public const double DoorNearEndMm = 700;
        public const double DoorNormalEndClearanceMm = 1200;
        public const double WindowEndClearanceMm = 900;
        public const double MinOpeningCentreSpacingMm = 1500;
        public const double TooCloseWindowSpacingMm = 1400;
        public const double TypicalSillMm = 900;

        private static readonly double[] OddSillHeightsMm = { 600, 750, 1050, 1200, 1350, 450 };

        public static ContentPlan Plan(BaselinePlan baseline, GenerationSettings settings, SeededRandom random, bool geometryDefects)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (random == null) throw new ArgumentNullException(nameof(random));

            var plan = new ContentPlan();
            var profile = SeverityProfile.For(settings.Severity);

            if (settings.CreateDoorsAndWindows)
            {
                // One occupancy map for BOTH kinds: without a corridor, doors share the front
                // exterior wall with windows, and a door overlapping a window is invalid, not "bad".
                var occupied = new Dictionary<int, List<double>>(); // wall index -> centre distances
                PlanDoors(baseline, plan, random.Stream(StreamDoors), profile, geometryDefects, occupied);
                PlanWindows(baseline, plan, random.Stream(StreamWindows), profile, geometryDefects, occupied);
            }

            if (settings.CreateFurniture)
                PlanFurniture(baseline, plan, random.Stream(StreamFurniture), profile, geometryDefects);

            for (var i = 0; i < plan.Openings.Count; i++) plan.Openings[i].Index = i;
            for (var i = 0; i < plan.Furniture.Count; i++) plan.Furniture[i].Index = i;
            return plan;
        }

        // ---- Doors -----------------------------------------------------------------------

        private static void PlanDoors(BaselinePlan baseline, ContentPlan plan, RandomStream rnd, SeverityProfile profile, bool defects, Dictionary<int, List<double>> occupied)
        {
            var cells = baseline.Cells.Where(c => c.Band != CellBand.Corridor && c.DoorWallIndex >= 0).ToList();
            var nearEndBudget = defects ? profile.DoorsNearWallEnd : 0;

            // Junction positions per host wall: partitions T-ing into it are wall "ends" for
            // door-placement purposes even though the host runs through.
            foreach (var cell in cells)
            {
                var host = baseline.Walls[cell.DoorWallIndex];
                var hostLine = host.Line;
                var along = hostLine.Length;
                if (along < DoorNormalEndClearanceMm * 2) continue;

                // Cell extent along the host wall, as distances from the host's start.
                var t0 = hostLine.ProjectParameter(new Point2D(cell.Bounds.MinX, hostLine.Start.Y)) * along;
                var t1 = hostLine.ProjectParameter(new Point2D(cell.Bounds.MaxX, hostLine.Start.Y)) * along;
                var lo = Math.Min(t0, t1);
                var hi = Math.Max(t0, t1);
                var cellSpan = hi - lo;
                if (cellSpan < DoorNearEndMm * 2) continue;

                double centre;
                var spec = new OpeningSpec
                {
                    LevelIndex = cell.LevelIndex,
                    WallIndex = host.Index,
                    Kind = OpeningKind.Door,
                };

                if (nearEndBudget > 0 && cellSpan > DoorNearEndMm * 2 + 100)
                {
                    // Jam it against the partition (or the exterior wall) at one end of the cell.
                    var atLow = rnd.NextBool();
                    centre = atLow ? lo + DoorNearEndMm : hi - DoorNearEndMm;
                    nearEndBudget--;
                    spec.DefectTags.Add("near-wall-end");
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.ContentPlacement,
                        $"Door on level {cell.LevelIndex + 1} at x≈{Fmt(hostLine.PointAtDistance(centre).X)} sits {Fmt(DoorNearEndMm - NominalDoorWidthMm / 2)} mm from a wall junction (too close to the wall end)."));
                }
                else
                {
                    var jitter = defects ? rnd.NextJitter(Math.Max(0, cellSpan / 2 - DoorNormalEndClearanceMm)) * 0.6 : 0;
                    centre = (lo + hi) / 2 + jitter;
                    centre = Math.Max(lo + DoorNormalEndClearanceMm, Math.Min(hi - DoorNormalEndClearanceMm, centre));
                }

                if (!Reserve(occupied, host.Index, centre, MinOpeningCentreSpacingMm)) continue;

                spec.DistanceAlongMm = Math.Round(centre);
                if (defects && rnd.NextBool(profile.DoorFlipProbability))
                {
                    spec.FlipHand = rnd.NextBool();
                    spec.FlipFacing = !spec.FlipHand || rnd.NextBool();
                    spec.DefectTags.Add("inconsistent-handing");
                }
                plan.Openings.Add(spec);
            }

            if (defects && plan.Openings.Any(o => o.DefectTags.Contains("inconsistent-handing")))
                plan.Defects.Add(new PlannedDefect(ScenarioIds.ContentPlacement,
                    $"{plan.Openings.Count(o => o.DefectTags.Contains("inconsistent-handing"))} door(s) have inconsistent handing/orientation compared with their neighbours."));
        }

        // ---- Windows ---------------------------------------------------------------------

        private static void PlanWindows(BaselinePlan baseline, ContentPlan plan, RandomStream rnd, SeverityProfile profile, bool defects, Dictionary<int, List<double>> occupied)
        {
            var exterior = baseline.Walls.Where(w => w.Role == WallRole.Exterior).ToList();
            var tooClosePairs = defects ? profile.WindowPairsTooClose : 0;

            var sillChoices = new List<double> { TypicalSillMm };
            if (defects)
                sillChoices.AddRange(rnd.TakeDistinct(OddSillHeightsMm, Math.Max(0, profile.SillHeightVarieties - 1)));

            var oddSillCount = 0;
            foreach (var wall in exterior)
            {
                var len = wall.Line.Length;
                var usable = len - 2 * WindowEndClearanceMm;
                if (usable < NominalWindowWidthMm) continue;

                var count = Math.Max(1, (int)Math.Floor(usable / profile.WindowSpacingMm));
                count = Math.Min(count, 5);
                var spacing = usable / count;

                for (var i = 0; i < count; i++)
                {
                    var centre = WindowEndClearanceMm + spacing * (i + 0.5);
                    if (defects) centre += rnd.NextJitter(Math.Min(300, spacing * 0.15));
                    centre = Math.Max(WindowEndClearanceMm, Math.Min(len - WindowEndClearanceMm, centre));
                    if (!Reserve(occupied, wall.Index, centre, MinOpeningCentreSpacingMm)) continue;

                    var spec = NewWindow(wall, centre, sillChoices, rnd, defects, ref oddSillCount);
                    plan.Openings.Add(spec);

                    // Optionally a second window jammed next to this one.
                    if (tooClosePairs > 0 && i < count - 1)
                    {
                        var second = centre + TooCloseWindowSpacingMm;
                        if (second <= len - WindowEndClearanceMm && Reserve(occupied, wall.Index, second, TooCloseWindowSpacingMm - 1))
                        {
                            var pair = NewWindow(wall, second, sillChoices, rnd, defects, ref oddSillCount);
                            pair.DefectTags.Add("too-close");
                            spec.DefectTags.Add("too-close");
                            plan.Openings.Add(pair);
                            tooClosePairs--;
                            plan.Defects.Add(new PlannedDefect(ScenarioIds.ContentPlacement,
                                $"Two windows on level {wall.LevelIndex + 1} are only {Fmt(TooCloseWindowSpacingMm)} mm apart centre-to-centre while the rest are spaced ~{Fmt(spacing)} mm."));
                            i++; // the pair consumed the next slot
                        }
                    }
                }
            }

            if (oddSillCount > 0)
                plan.Defects.Add(new PlannedDefect(ScenarioIds.ContentPlacement,
                    $"{oddSillCount} window(s) sit at sill heights other than the typical {Fmt(TypicalSillMm)} mm ({string.Join(", ", sillChoices.Skip(1).Select(Fmt))} mm) for no reason."));
        }

        private static OpeningSpec NewWindow(WallSpec wall, double centre, List<double> sillChoices, RandomStream rnd, bool defects, ref int oddSillCount)
        {
            var sill = TypicalSillMm;
            if (defects && sillChoices.Count > 1 && rnd.NextBool(0.4))
            {
                sill = rnd.Pick(sillChoices.Skip(1).ToList());
                oddSillCount++;
            }

            var spec = new OpeningSpec
            {
                LevelIndex = wall.LevelIndex,
                WallIndex = wall.Index,
                Kind = OpeningKind.Window,
                DistanceAlongMm = Math.Round(centre),
                SillHeightMm = sill,
            };
            if (Math.Abs(sill - TypicalSillMm) > 1) spec.DefectTags.Add("odd-sill");
            return spec;
        }

        // ---- Furniture -------------------------------------------------------------------

        private static void PlanFurniture(BaselinePlan baseline, ContentPlan plan, RandomStream rnd, SeverityProfile profile, bool defects)
        {
            var cells = baseline.Cells.Where(c => c.Band != CellBand.Corridor).ToList();
            var outsideBudget = defects ? profile.FurnitureOutsideFootprint : 0;
            var rotatedBudget = defects ? profile.FurnitureRotatedOddly : 0;
            var onWallBudget = defects ? profile.FurnitureOnWall : 0;

            foreach (var cell in cells)
            {
                var n = rnd.NextInt(0, profile.FurniturePerCellMax + 1);
                for (var i = 0; i < n; i++)
                {
                    var inset = 700.0;
                    var b = cell.Bounds;
                    if (b.Width < inset * 2 + 200 || b.Depth < inset * 2 + 200) break;

                    var spec = new FurnitureSpec
                    {
                        LevelIndex = cell.LevelIndex,
                        CellIndex = cell.Index,
                        Location = new Point2D(
                            Math.Round(rnd.NextDouble(b.MinX + inset, b.MaxX - inset)),
                            Math.Round(rnd.NextDouble(b.MinY + inset, b.MaxY - inset))),
                        RotationDegrees = rnd.NextBool() ? 0 : 90,
                    };

                    if (rotatedBudget > 0 && rnd.NextBool(0.5))
                    {
                        spec.RotationDegrees = Math.Round(rnd.NextDouble(15, 75));
                        spec.DefectTags.Add("odd-rotation");
                        rotatedBudget--;
                        plan.Defects.Add(new PlannedDefect(ScenarioIds.ContentPlacement,
                            $"Furniture on level {cell.LevelIndex + 1} at {spec.Location} is rotated {Fmt(spec.RotationDegrees)}° for no reason."));
                    }
                    else if (onWallBudget > 0 && rnd.NextBool(0.5))
                    {
                        // Centre it on the cell's left wall line.
                        spec.Location = new Point2D(b.MinX, spec.Location.Y);
                        spec.DefectTags.Add("on-wall");
                        onWallBudget--;
                        plan.Defects.Add(new PlannedDefect(ScenarioIds.ContentPlacement,
                            $"Furniture on level {cell.LevelIndex + 1} at {spec.Location} is centred on a wall (intentionally misplaced component)."));
                    }

                    plan.Furniture.Add(spec);
                }
            }

            // A couple of pieces outside the building altogether.
            var fp = baseline.Footprint;
            var levels = baseline.Cells.Select(c => c.LevelIndex).Distinct().ToList();
            for (var i = 0; i < outsideBudget && levels.Count > 0; i++)
            {
                var side = rnd.NextInt(0, 4);
                var offset = Math.Round(rnd.NextDouble(300, 900));
                var along = rnd.NextDouble(0.2, 0.8);
                Point2D p;
                switch (side)
                {
                    case 0: p = new Point2D(fp.MinX + fp.Width * along, fp.MinY - offset); break;
                    case 1: p = new Point2D(fp.MaxX + offset, fp.MinY + fp.Depth * along); break;
                    case 2: p = new Point2D(fp.MinX + fp.Width * along, fp.MaxY + offset); break;
                    default: p = new Point2D(fp.MinX - offset, fp.MinY + fp.Depth * along); break;
                }
                var spec = new FurnitureSpec
                {
                    LevelIndex = rnd.Pick(levels),
                    Location = new Point2D(Math.Round(p.X), Math.Round(p.Y)),
                    RotationDegrees = 0,
                };
                spec.DefectTags.Add("outside-footprint");
                plan.Furniture.Add(spec);
                plan.Defects.Add(new PlannedDefect(ScenarioIds.ContentPlacement,
                    $"Furniture on level {spec.LevelIndex + 1} at {spec.Location} is {Fmt(offset)} mm outside the building footprint."));
            }
        }

        // ---- Helpers ---------------------------------------------------------------------

        private static bool Reserve(Dictionary<int, List<double>> occupied, int wallIndex, double centre, double minSpacing)
        {
            if (!occupied.TryGetValue(wallIndex, out var list))
            {
                list = new List<double>();
                occupied[wallIndex] = list;
            }
            if (list.Any(c => Math.Abs(c - centre) < minSpacing)) return false;
            list.Add(centre);
            return true;
        }

        private static string Fmt(double mm) => mm.ToString("0", CultureInfo.InvariantCulture);
    }
}
