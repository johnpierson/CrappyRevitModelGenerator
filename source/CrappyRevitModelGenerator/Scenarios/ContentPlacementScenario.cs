using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Planning;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Doors, windows and furniture (plan section 7.3, plus the "content placed without care"
    /// half of 7.5). Every position, sill height, flip and rotation comes from
    /// <see cref="ContentPlanner"/>: doors jammed against wall junctions and handed
    /// inconsistently, window pairs too close together and windows at unrelated sill heights,
    /// furniture rotated oddly, centred on a wall or sitting outside the footprint. This class
    /// only resolves symbols, converts units and places instances.
    ///
    /// Each instance is placed inside its own try/catch: a family Revit refuses at one spot
    /// (too close to a join it decided to make, a wall shorter than the family) is recorded and
    /// skipped so the rest of the content, and every later scenario, still runs. A missing
    /// door, window or furniture family is a single fallback note, not one failure per instance.
    /// </summary>
    public sealed class ContentPlacementScenario : IBadModelScenario
    {
        private const string FurnitureSymbolStream = "content-placement/furniture-symbol";

        public string Id => ScenarioIds.ContentPlacement;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Baseline == null)
            {
                reason = "The baseline plan is missing; there are no walls or levels to host content on.";
                return false;
            }

            var settings = context.Settings;
            if (!settings.CreateDoorsAndWindows && !settings.CreateFurniture)
            {
                reason = "Doors/windows and furniture are both switched off in the settings.";
                return false;
            }

            // Only the kinds the settings ask for matter; a missing door family is a fallback,
            // not a reason to skip the windows and furniture.
            var types = context.Types;
            var missing = new List<string>();
            var anyUsable = false;
            if (settings.CreateDoorsAndWindows)
            {
                if (types.DoorSymbol != null || types.WindowSymbol != null) anyUsable = true;
                if (types.DoorSymbol == null) missing.Add("door families");
                if (types.WindowSymbol == null) missing.Add("window families");
            }
            if (settings.CreateFurniture)
            {
                if (types.FurniturePicks != null && types.FurniturePicks.Count > 0) anyUsable = true;
                else missing.Add("furniture families");
            }

            if (!anyUsable)
            {
                reason = "The document has no usable " + string.Join(" or ", missing) + " loaded.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var report = context.Report;

            var plan = ContentPlanner.Plan(context.Baseline, context.Settings, context.Random, geometryDefects: true);
            context.Content = plan;

            var tally = new Tally();
            PlaceOpenings(context, plan, tally);
            PlaceFurniture(context, plan, tally);

            // The planner's decisions, attributed as it recorded them. It does not know element
            // ids, so the ids per defect kind follow as information lines below.
            foreach (var defect in plan.Defects)
                report.AddDefect(defect.ScenarioId, defect.Message);

            ReportDefectIds(context, plan, tally);

            var doorsPlanned = plan.Openings.Count(o => o.Kind == OpeningKind.Door);
            var windowsPlanned = plan.Openings.Count(o => o.Kind == OpeningKind.Window);
            report.AddInfo(Id,
                $"Content placement: doors {tally.DoorsPlaced}/{doorsPlanned} placed" + Skipped(tally.DoorsSkipped, tally.DoorsFailed) +
                (tally.DoorsFlipped > 0 ? $", {tally.DoorsFlipped} flipped" : string.Empty) +
                $"; windows {tally.WindowsPlaced}/{windowsPlanned} placed" + Skipped(tally.WindowsSkipped, tally.WindowsFailed) +
                $"; furniture {tally.FurniturePlaced}/{plan.Furniture.Count} placed" + Skipped(tally.FurnitureSkipped, tally.FurnitureFailed) + ".");
        }

        // ---- Doors and windows -----------------------------------------------------------

        private void PlaceOpenings(GenerationContext context, ContentPlan plan, Tally tally)
        {
            if (plan.Openings.Count == 0) return;

            var report = context.Report;
            var factory = context.Factory;
            var doorSymbol = context.Types.DoorSymbol;
            var windowSymbol = context.Types.WindowSymbol;

            if (doorSymbol == null && plan.Openings.Any(o => o.Kind == OpeningKind.Door))
                report.AddFallback(Id, $"No door family is loaded; {plan.Openings.Count(o => o.Kind == OpeningKind.Door)} planned door(s) skipped.");
            if (windowSymbol == null && plan.Openings.Any(o => o.Kind == OpeningKind.Window))
                report.AddFallback(Id, $"No window family is loaded; {plan.Openings.Count(o => o.Kind == OpeningKind.Window)} planned window(s) skipped.");

            // Doors whose handing the planner wants flipped; flipped after one regenerate so the
            // instances have geometry to flip.
            var doorsToFlip = new List<KeyValuePair<OpeningSpec, FamilyInstance>>();

            foreach (var spec in plan.Openings)
            {
                var isDoor = spec.Kind == OpeningKind.Door;
                var kind = isDoor ? "door" : "window";
                var symbol = isDoor ? doorSymbol : windowSymbol;
                if (symbol == null)
                {
                    tally.Skip(isDoor);
                    continue;
                }

                var operation = $"place {kind} {spec.Index} on wall {spec.WallIndex} at {spec.DistanceAlongMm:0} mm";

                if (!context.Walls.TryGetValue(spec.WallIndex, out var wall) || wall == null || !wall.IsValidObject)
                {
                    report.AddException(Id, operation, new InvalidOperationException($"Host wall {spec.WallIndex} was not created."), rolledBack: false);
                    tally.Fail(isDoor);
                    continue;
                }
                if (spec.WallIndex < 0 || spec.WallIndex >= context.Baseline.Walls.Count)
                {
                    report.AddException(Id, operation, new InvalidOperationException($"Wall index {spec.WallIndex} is not in the baseline plan."), rolledBack: false);
                    tally.Fail(isDoor);
                    continue;
                }
                var level = context.LevelFor(spec.LevelIndex);
                if (level == null)
                {
                    report.AddException(Id, operation, new InvalidOperationException($"Level index {spec.LevelIndex} was not created."), rolledBack: false);
                    tally.Fail(isDoor);
                    continue;
                }

                try
                {
                    var location = HostLocation(wall, context.Baseline.Walls[spec.WallIndex], spec, level);
                    var instance = factory.PlaceHosted(symbol, wall, level, location, isDoor ? GeneratedCategory.Doors : GeneratedCategory.Windows);
                    if (instance == null)
                    {
                        report.AddException(Id, operation, new InvalidOperationException("Revit returned no instance."), rolledBack: false);
                        tally.Fail(isDoor);
                        continue;
                    }

                    context.Openings[spec.Index] = instance;
                    tally.Place(isDoor);

                    if (isDoor)
                    {
                        if (spec.FlipHand || spec.FlipFacing)
                            doorsToFlip.Add(new KeyValuePair<OpeningSpec, FamilyInstance>(spec, instance));
                    }
                    else
                    {
                        // Every window gets its planned sill so the typical ones agree with each
                        // other and the odd ones stand out; the family default might be neither.
                        factory.TrySet(instance, BuiltInParameter.INSTANCE_SILL_HEIGHT_PARAM, UnitConversion.MmToFeet(spec.SillHeightMm));
                    }
                }
                catch (Exception ex)
                {
                    report.AddException(Id, operation, ex, rolledBack: false);
                    tally.Fail(isDoor);
                }
            }

            if (doorsToFlip.Count == 0) return;

            try
            {
                context.Document.Regenerate();
            }
            catch (Exception ex)
            {
                report.AddException(Id, "regenerate before flipping doors", ex, rolledBack: false);
            }

            foreach (var pair in doorsToFlip)
            {
                var spec = pair.Key;
                var instance = pair.Value;
                try
                {
                    if (!instance.IsValidObject) continue;
                    var flipped = false;
                    if (spec.FlipHand && instance.CanFlipHand && instance.flipHand()) flipped = true;
                    if (spec.FlipFacing && instance.CanFlipFacing && instance.flipFacing()) flipped = true;
                    if (flipped)
                    {
                        tally.DoorsFlipped++;
                        tally.FlippedDoorIndices.Add(spec.Index);
                    }
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"flip door {spec.Index}", ex, rolledBack: false);
                }
            }
        }

        /// <summary>
        /// The planned point along the wall, projected onto the wall's real location curve so an
        /// odd location line (a datum defect) or a corner join can never leave it off the host.
        /// </summary>
        private static XYZ HostLocation(Wall wall, WallSpec wallSpec, OpeningSpec spec, Level level)
        {
            var planPoint = wallSpec.Line.PointAtDistance(spec.DistanceAlongMm);
            var point = UnitConversion.ToXYZAtFeet(planPoint, level.ProjectElevation);

            if (wall.Location is LocationCurve locationCurve && locationCurve.Curve != null)
            {
                var projected = locationCurve.Curve.Project(point);
                if (projected != null && projected.XYZPoint != null) return projected.XYZPoint;
            }
            return point;
        }

        // ---- Furniture -------------------------------------------------------------------

        private void PlaceFurniture(GenerationContext context, ContentPlan plan, Tally tally)
        {
            if (plan.Furniture.Count == 0) return;

            var report = context.Report;
            var factory = context.Factory;

            var picks = context.Types.FurniturePicks;
            if (picks == null || picks.Count == 0)
            {
                report.AddFallback(Id, $"No furniture family is loaded; {plan.Furniture.Count} planned piece(s) skipped.");
                tally.FurnitureSkipped += plan.Furniture.Count;
                return;
            }

            var symbols = UsableFurniture(picks, report);
            var rnd = context.Random.Stream(FurnitureSymbolStream);

            foreach (var spec in plan.Furniture)
            {
                // Draw before any check so the symbol per piece is stable whatever else fails.
                var symbol = rnd.Pick(symbols);
                var operation = $"place furniture {spec.Index} ({symbol.Family?.Name}) at {spec.Location}";

                var level = context.LevelFor(spec.LevelIndex);
                if (level == null)
                {
                    report.AddException(Id, operation, new InvalidOperationException($"Level index {spec.LevelIndex} was not created."), rolledBack: false);
                    tally.FurnitureFailed++;
                    continue;
                }

                try
                {
                    var location = UnitConversion.ToXYZAtFeet(spec.Location, level.ProjectElevation);
                    var rotation = UnitConversion.DegreesToRadians(spec.RotationDegrees);
                    var instance = factory.PlaceFree(symbol, level, location, rotation, GeneratedCategory.Furniture);
                    if (instance == null)
                    {
                        report.AddException(Id, operation, new InvalidOperationException("Revit returned no instance."), rolledBack: false);
                        tally.FurnitureFailed++;
                        continue;
                    }
                    context.Furniture[spec.Index] = instance;
                    tally.FurniturePlaced++;
                }
                catch (Exception ex)
                {
                    report.AddException(Id, operation, ex, rolledBack: false);
                    tally.FurnitureFailed++;
                }
            }
        }

        /// <summary>
        /// Furniture that can be dropped on a level. Wall- or face-hosted furniture families need
        /// a host the free-placement call does not give them, so they are left out when anything
        /// level-based is available; otherwise every pick is tried and failures are recorded.
        /// </summary>
        private IReadOnlyList<FamilySymbol> UsableFurniture(IReadOnlyList<FamilySymbol> picks, GenerationReport report)
        {
            var levelBased = picks
                .Where(s => s != null && s.Family != null && s.Family.FamilyPlacementType == FamilyPlacementType.OneLevelBased)
                .ToList();

            if (levelBased.Count == 0)
            {
                report.AddFallback(Id, "None of the furniture types is level-based; trying them anyway (hosted families will fail and be recorded).");
                return picks;
            }
            if (levelBased.Count < picks.Count)
            {
                var dropped = picks.Where(s => !levelBased.Contains(s)).Select(s => $"'{s?.Family?.Name}'");
                report.AddFallback(Id, $"Furniture type(s) {string.Join(", ", dropped)} need a host or work plane and are not used.");
            }
            return levelBased;
        }

        // ---- Reporting -------------------------------------------------------------------

        /// <summary>One information line per defect kind with the ids of the instances that carry it.</summary>
        private void ReportDefectIds(GenerationContext context, ContentPlan plan, Tally tally)
        {
            var byTag = new SortedDictionary<string, List<long>>(StringComparer.Ordinal);

            foreach (var spec in plan.Openings)
            {
                if (spec.DefectTags.Count == 0 || !context.Openings.TryGetValue(spec.Index, out var inst) || inst == null) continue;
                foreach (var tag in spec.DefectTags)
                {
                    // A door the family refused to flip does not carry the handing defect.
                    if (tag == "inconsistent-handing" && !tally.FlippedDoorIndices.Contains(spec.Index)) continue;
                    Add(byTag, tag, inst.Id.Value);
                }
            }
            foreach (var spec in plan.Furniture)
            {
                if (spec.DefectTags.Count == 0 || !context.Furniture.TryGetValue(spec.Index, out var inst) || inst == null) continue;
                foreach (var tag in spec.DefectTags) Add(byTag, tag, inst.Id.Value);
            }

            foreach (var pair in byTag)
                context.Report.AddInfo(Id, $"{Describe(pair.Key)}: {pair.Value.Count} element(s).", pair.Value);
        }

        private static void Add(IDictionary<string, List<long>> byTag, string tag, long id)
        {
            if (!byTag.TryGetValue(tag, out var list))
            {
                list = new List<long>();
                byTag[tag] = list;
            }
            list.Add(id);
        }

        private static string Describe(string tag)
        {
            switch (tag)
            {
                case "near-wall-end": return "Doors too close to a wall end or junction";
                case "inconsistent-handing": return "Doors with flipped hand/facing";
                case "too-close": return "Windows too close to their neighbour";
                case "odd-sill": return "Windows at a non-typical sill height";
                case "odd-rotation": return "Furniture rotated for no reason";
                case "on-wall": return "Furniture centred on a wall";
                case "outside-footprint": return "Furniture outside the footprint";
                default: return "Content tagged '" + tag + "'";
            }
        }

        private static string Skipped(int skipped, int failed)
        {
            if (skipped == 0 && failed == 0) return string.Empty;
            var parts = new List<string>();
            if (skipped > 0) parts.Add($"{skipped} skipped, no family");
            if (failed > 0) parts.Add($"{failed} failed");
            return " (" + string.Join("; ", parts) + ")";
        }

        private sealed class Tally
        {
            public int DoorsPlaced, DoorsSkipped, DoorsFailed, DoorsFlipped;
            public int WindowsPlaced, WindowsSkipped, WindowsFailed;
            public int FurniturePlaced, FurnitureSkipped, FurnitureFailed;
            public readonly HashSet<int> FlippedDoorIndices = new HashSet<int>();

            public void Place(bool isDoor) { if (isDoor) DoorsPlaced++; else WindowsPlaced++; }
            public void Skip(bool isDoor) { if (isDoor) DoorsSkipped++; else WindowsSkipped++; }
            public void Fail(bool isDoor) { if (isDoor) DoorsFailed++; else WindowsFailed++; }
        }
    }
}
