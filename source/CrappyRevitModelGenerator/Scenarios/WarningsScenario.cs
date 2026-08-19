using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Core.Planning;
using CrappyRevitModelGenerator.Revit;
using RevitExceptions = Autodesk.Revit.Exceptions;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// The opt-in, high-risk scenario (plan section 7.8): conditions Revit flags with a warning
    /// but still commits. Three kinds, each scaled by the severity profile:
    /// <list type="bullet">
    /// <item>a second wall of the same type laid along the middle 40–60 % of an existing
    ///       generated partition or corridor wall ("Highlighted walls overlap");</item>
    /// <item>generated furniture (or, when there is none, doors and windows) copied in place
    ///       ("There are identical instances in the same place");</item>
    /// <item>a small square floor inside a generated floor plate ("Highlighted floors overlap").</item>
    /// </list>
    /// Every one of these is a warning on the <see cref="ExpectedWarnings"/> list, so the
    /// failure preprocessor dismisses and records it. Nothing here should raise an error-level
    /// failure — that would roll the whole scenario back. Deliberately unused views, types and
    /// materials are already produced by the documentation and content-types scenarios and are
    /// not repeated here. Runs last, so it only ever touches elements the run itself created.
    /// </summary>
    public sealed class WarningsScenario : IBadModelScenario
    {
        private const string StreamOverlaps = "warnings/overlaps";
        private const string StreamDuplicates = "warnings/duplicates";

        /// <summary>The overlapping wall starts at 30 % of the host and ends at 70–90 % of it.</summary>
        private const double OverlapStartFraction = 0.30;
        private const double OverlapEndFraction = 0.70;
        private const double OverlapEndJitterFraction = 0.20;

        /// <summary>Side of the small overlapping floor square and its distance from the footprint corner.</summary>
        private const double OverlapFloorSideMm = 2000;
        private const double OverlapFloorCornerOffsetMm = 1000;

        public string Id => ScenarioIds.Warnings;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Baseline == null)
            {
                reason = "The baseline plan is missing.";
                return false;
            }
            if (context.Walls.Count == 0)
            {
                reason = "No generated walls exist to overlap.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var overlappingWalls = PlantOverlappingWalls(context);
            var duplicateInstances = PlantDuplicateInstances(context);
            var overlappingFloors = PlantOverlappingFloors(context);

            context.Report.AddInfo(Id,
                $"Warnings: {overlappingWalls} overlapping wall(s), {duplicateInstances} duplicate instance(s), {overlappingFloors} overlapping floor(s) planted. " +
                "Revit's warnings for them are dismissed during the run and listed under expected warnings.");
        }

        // ---- Overlapping walls -----------------------------------------------------------

        private int PlantOverlappingWalls(GenerationContext context)
        {
            var wanted = context.Profile.OverlappingWalls;
            if (wanted <= 0) return 0;

            var rnd = context.Random.Stream(StreamOverlaps);
            var minLength = GeometryTolerances.Default.MinWallLengthMm;

            // Interior walls only (a doubled exterior wall reads as a design choice, not a slip),
            // long enough that 40 % of them is still a real wall, and actually created.
            var candidates = context.Baseline.Walls
                .Where(w => w.Role != WallRole.Exterior
                            && w.Line.Length * (OverlapEndFraction - OverlapStartFraction) >= minLength
                            && context.Walls.ContainsKey(w.Index))
                .OrderBy(w => w.Index)
                .ToList();
            if (candidates.Count == 0)
            {
                context.Report.AddFallback(Id, "No generated interior wall is long enough to overlap; overlapping walls skipped.");
                return 0;
            }

            var created = 0;
            foreach (var spec in rnd.TakeDistinct(candidates, wanted))
            {
                // Draw before any early exit so the stream stays in step across documents.
                var jitter = rnd.NextDouble(0.0, OverlapEndJitterFraction);

                if (!context.Walls.TryGetValue(spec.Index, out var host) || !IsLive(host)) continue;
                var level = context.LevelFor(spec.LevelIndex);
                if (level == null) continue;

                var piece = new Segment2D(spec.Line.PointAt(OverlapStartFraction), spec.Line.PointAt(OverlapEndFraction + jitter));
                if (!CurveValidation.IsValidSegment(piece, minLength)) continue;

                var heightMm = spec.HeightMm > 0 ? spec.HeightMm : context.Baseline.LevelHeightMm;

                try
                {
                    var type = host.WallType;
                    if (type == null) continue;

                    var overlap = context.Factory.CreateWall(piece, type, level, heightMm);
                    created++;

                    var coverage = (OverlapEndFraction + jitter - OverlapStartFraction) * 100;
                    context.Report.AddDefect(Id,
                        $"{level.Name}: a second '{type.Name}' wall {Fmt(piece.Length)} mm long sits exactly on the {spec.Role.ToString().ToLowerInvariant()} wall {DescribeLine(spec.Line)}, covering {Fmt(coverage)} % of its length (walls overlap).",
                        new[] { host.Id.Value, overlap.Id.Value });
                }
                catch (Exception ex)
                {
                    context.Report.AddException(Id, $"create overlapping wall on wall {spec.Index} ({DescribeLine(spec.Line)})", ex, rolledBack: false);
                }
            }

            return created;
        }

        // ---- Duplicate instances ---------------------------------------------------------

        private int PlantDuplicateInstances(GenerationContext context)
        {
            var wanted = context.Profile.DuplicateInstances;
            if (wanted <= 0) return 0;

            var rnd = context.Random.Stream(StreamDuplicates);

            var pool = context.Furniture.OrderBy(p => p.Key).Select(p => p.Value).Where(IsLive).ToList();
            var kind = "furniture";
            if (pool.Count == 0)
            {
                pool = context.Openings.OrderBy(p => p.Key).Select(p => p.Value).Where(IsLive).ToList();
                kind = "door/window";
                if (pool.Count > 0)
                    context.Report.AddFallback(Id, "No generated furniture to duplicate; doors and windows are duplicated in place instead.");
            }
            if (pool.Count == 0)
            {
                context.Report.AddFallback(Id, "No generated furniture, doors or windows to duplicate; duplicate instances skipped.");
                return 0;
            }

            var created = 0;
            foreach (var source in rnd.TakeDistinct(pool, wanted))
            {
                var label = Describe(source);
                try
                {
                    var copies = CopyInPlace(context.Document, source);
                    var ids = new List<long> { source.Id.Value };
                    foreach (var id in copies)
                    {
                        var copy = context.Document.GetElement(id);
                        if (copy == null) continue;
                        // The copy carries the source's identity entity; Register re-tags it for this scenario.
                        context.Factory.Register(copy, CategoryFor(copy));
                        ids.Add(id.Value);
                    }

                    if (ids.Count == 1)
                    {
                        context.Report.AddException(Id, $"copy {kind} {label} in place",
                            new InvalidOperationException("CopyElements returned no new elements."), rolledBack: false);
                        continue;
                    }

                    created++;
                    context.Report.AddDefect(Id, $"{Capitalise(kind)} {label} has an identical copy at exactly the same place (duplicate instance).", ids);
                }
                catch (Exception ex)
                {
                    context.Report.AddException(Id, $"copy {kind} {label} in place", ex, rolledBack: false);
                }
            }

            return created;
        }

        /// <summary>
        /// Copy one instance onto itself. The translation overload is what free-standing furniture
        /// needs; it does not rehost, so for a hosted door or window that it rejects the
        /// document-to-document form (same document, identity transform) is tried instead.
        /// </summary>
        private static ICollection<ElementId> CopyInPlace(Document doc, FamilyInstance source)
        {
            var ids = new List<ElementId> { source.Id };
            var hosted = source.Host != null;
            if (!hosted) return ElementTransformUtils.CopyElements(doc, ids, XYZ.Zero);

            try
            {
                return ElementTransformUtils.CopyElements(doc, ids, XYZ.Zero);
            }
            catch (RevitExceptions.ApplicationException)
            {
                return ElementTransformUtils.CopyElements(doc, ids, doc, Transform.Identity, null);
            }
        }

        private static GeneratedCategory CategoryFor(Element element)
        {
            if (!(element is FamilyInstance)) return GeneratedCategory.Other;
            BuiltInCategory bic;
            try
            {
                bic = element.Category?.BuiltInCategory ?? BuiltInCategory.INVALID;
            }
            catch
            {
                return GeneratedCategory.Other;
            }
            switch (bic)
            {
                case BuiltInCategory.OST_Furniture: return GeneratedCategory.Furniture;
                case BuiltInCategory.OST_Doors: return GeneratedCategory.Doors;
                case BuiltInCategory.OST_Windows: return GeneratedCategory.Windows;
                default: return GeneratedCategory.Other;
            }
        }

        // ---- Overlapping floors ----------------------------------------------------------

        private int PlantOverlappingFloors(GenerationContext context)
        {
            var wanted = context.Profile.OverlappingFloors;
            if (wanted <= 0) return 0;

            var floorType = context.Types.FloorType;
            if (floorType == null)
            {
                context.Report.AddFallback(Id, "No floor type available; overlapping floors skipped.");
                return 0;
            }

            // The lowest level that actually received a baseline floor, so the square really
            // overlaps something rather than being a lonely slab.
            var target = context.Baseline.Floors
                .Where(f => context.Floors.TryGetValue(f.Index, out var existing) && IsLive(existing))
                .Select(f => new { Spec = f, LevelSpec = context.Baseline.Levels.FirstOrDefault(l => l.Index == f.LevelIndex) })
                .Where(x => x.LevelSpec != null && context.LevelFor(x.LevelSpec.Index) != null)
                .OrderBy(x => x.LevelSpec.ElevationMm)
                .ThenBy(x => x.Spec.Index)
                .FirstOrDefault();
            if (target == null)
            {
                context.Report.AddFallback(Id, "No generated floor exists to overlap; overlapping floors skipped.");
                return 0;
            }

            var level = context.LevelFor(target.LevelSpec.Index);
            var baseFloor = context.Floors[target.Spec.Index];
            var fp = context.Baseline.Footprint;

            // Shrink the square if the footprint is too small for a full-size one; give up if it degenerates.
            var side = Math.Min(OverlapFloorSideMm, Math.Min(fp.Width, fp.Depth) - 2 * OverlapFloorCornerOffsetMm);
            if (side < GeometryTolerances.Default.MinCurveLengthMm * 3)
            {
                context.Report.AddFallback(Id, "The footprint is too small for an overlapping floor square; overlapping floors skipped.");
                return 0;
            }

            var rnd = context.Random.Stream(StreamOverlaps);
            var firstCorner = rnd.NextInt(0, 4);
            var corners = fp.Corners;
            var centre = fp.Center;

            var created = 0;
            for (var i = 0; i < wanted; i++)
            {
                var cornerIndex = (firstCorner + i) % corners.Count;
                var corner = corners[cornerIndex];
                var sx = corner.X < centre.X ? 1 : -1;
                var sy = corner.Y < centre.Y ? 1 : -1;
                var square = new Rect2D(
                    corner.X + sx * OverlapFloorCornerOffsetMm, corner.Y + sy * OverlapFloorCornerOffsetMm,
                    corner.X + sx * (OverlapFloorCornerOffsetMm + side), corner.Y + sy * (OverlapFloorCornerOffsetMm + side));
                var loop = new List<Point2D>(square.Corners);
                if (!CurveValidation.IsSimpleClosedLoop(loop)) continue;

                try
                {
                    var floor = context.Factory.CreateFloor(loop, level, floorType);
                    created++;
                    context.Report.AddDefect(Id,
                        $"{level.Name}: a stray {Fmt(side)} x {Fmt(side)} mm '{floorType.Name}' floor sits {Fmt(OverlapFloorCornerOffsetMm)} mm in from the footprint corner {corner}, on top of the level's floor plate (floors overlap).",
                        new[] { baseFloor.Id.Value, floor.Id.Value });
                }
                catch (Exception ex)
                {
                    context.Report.AddException(Id, $"create overlapping floor at corner {corner}", ex, rolledBack: false);
                }
            }

            return created;
        }

        // ---- Helpers ---------------------------------------------------------------------

        private static bool IsLive(Element e) => e != null && e.IsValidObject;

        private static string Fmt(double mm) => mm.ToString("0", CultureInfo.InvariantCulture);

        private static string Capitalise(string s) =>
            string.IsNullOrEmpty(s) ? s : char.ToUpperInvariant(s[0]) + s.Substring(1);

        private static string DescribeLine(Segment2D s) =>
            s.IsHorizontal() ? $"along y={Fmt(s.Start.Y)}" : s.IsVertical() ? $"along x={Fmt(s.Start.X)}" : $"from {s.Start} to {s.End}";

        /// <summary>"'Desk : 1500 x 750' at (x, y) mm" — enough for an auditor to find it; never throws.</summary>
        private static string Describe(FamilyInstance instance)
        {
            if (instance == null) return "(null instance)";
            try
            {
                var symbol = instance.Symbol;
                var name = symbol == null
                    ? instance.Name
                    : $"{symbol.Family?.Name ?? symbol.FamilyName} : {symbol.Name}";
                var location = instance.Location is LocationPoint lp && lp.Point != null
                    ? $" at {UnitConversion.ToPoint2D(lp.Point)} mm"
                    : string.Empty;
                return $"'{name}'{location} (id {instance.Id.Value})";
            }
            catch
            {
                return $"instance {instance.Id.Value}";
            }
        }
    }
}
