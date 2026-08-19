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
    /// The element-level half of plan section 7.2. The layout defects (jittered levels, the
    /// intermediate level, misaligned / gapped / stub walls, odd wall types and location lines,
    /// disallowed joins, floor jogs and offsets, grid extents, misaligned and near-coincident
    /// grids) are planted by <see cref="BaselinePlanner"/> and created by the baseline scenario,
    /// which already reports them under this scenario's id. What is left is what needs existing
    /// elements and views:
    /// <list type="bullet">
    /// <item>grid bubbles hidden at one end (a per-view setting, so it needs the plan views);</item>
    /// <item>a few grids given a view-specific, shorter extent in one plan view only;</item>
    /// <item>level bubbles hidden at one end in a generated section or elevation, if there is one;</item>
    /// <item>one or two more partition walls with a join disallowed after the fact.</item>
    /// </list>
    /// Every operation is wrapped individually: a grid that Revit will not show in a view is a
    /// recorded exception, not a rolled-back scenario.
    /// </summary>
    public sealed class DatumScenario : IBadModelScenario
    {
        private const string StreamGrids = "datum/grids";
        private const string StreamLevels = "datum/levels";
        private const string StreamWalls = "datum/walls";

        /// <summary>How much shorter a view-specific grid extent gets, in millimetres.</summary>
        private const double MinShortenMm = 1000;
        private const double MaxShortenMm = 3000;

        /// <summary>Never leave a view-specific grid shorter than this (keeps the bubble legible and the curve valid).</summary>
        private const double MinRemainingGridMm = 1000;

        public string Id => ScenarioIds.Datum;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Baseline == null)
            {
                reason = "The baseline plan is not available.";
                return false;
            }
            if (context.Grids.Count == 0)
            {
                reason = "No generated grids to tweak.";
                return false;
            }
            if (context.PlanViews.Count == 0)
            {
                reason = "No generated plan views; grid bubble visibility is a per-view setting.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var gridStream = context.Random.Stream(StreamGrids);
            var levelStream = context.Random.Stream(StreamLevels);
            var wallStream = context.Random.Stream(StreamWalls);

            // Dictionaries are enumerated by key so the same seed visits elements in the same order.
            var planViews = context.PlanViews.OrderBy(p => p.Key).Select(p => p.Value).Where(v => v != null && v.IsValidObject).ToList();
            var grids = context.Grids.OrderBy(p => p.Key).Select(p => p.Value).Where(g => g != null && g.IsValidObject).ToList();
            var levels = context.Levels.OrderBy(p => p.Key).Select(p => p.Value).Where(l => l != null && l.IsValidObject).ToList();

            var bubbleGrids = HideOneEndGridBubbles(context, planViews);
            var extentGrids = MakeViewSpecificExtents(context, gridStream, grids, planViews, out var extentView);
            var levelBubbles = HideLevelBubbles(context, levelStream, levels, out var levelView);
            var joins = DisallowExtraJoins(context, wallStream);

            var summary = new List<string>
            {
                $"{bubbleGrids} grid(s) with a bubble hidden at one end across {planViews.Count} plan view(s)",
                extentView == null
                    ? "no view-specific grid extents"
                    : $"{extentGrids} grid(s) with a view-specific extent in '{extentView.Name}'",
                levelView == null
                    ? "no section/elevation view for level bubbles"
                    : $"{levelBubbles} level bubble(s) hidden in '{levelView.Name}'",
                $"{joins} extra wall join(s) disallowed",
            };
            context.Report.AddInfo(Id, "Datum tweaks: " + string.Join("; ", summary) + ".");
        }

        // ---- Grid bubbles ------------------------------------------------------------------

        /// <summary>
        /// The planner decided which grids show a bubble at only one end; here it becomes real in
        /// every baseline plan view. Bubble visibility is view-specific, so plans duplicated by
        /// the documentation scenario keep both bubbles — an inconsistency between views that
        /// reads as exactly the kind of mess this scenario is for.
        /// </summary>
        private int HideOneEndGridBubbles(GenerationContext context, IReadOnlyList<ViewPlan> planViews)
        {
            var report = context.Report;
            var affected = 0;

            foreach (var spec in context.Baseline.Grids)
            {
                if (spec.BubbleAtStart && spec.BubbleAtEnd) continue;
                if (!context.Grids.TryGetValue(spec.Index, out var grid) || grid == null || !grid.IsValidObject) continue;

                var end = spec.BubbleAtStart ? DatumEnds.End1 : DatumEnds.End0;
                var hiddenIn = 0;

                foreach (var view in planViews)
                {
                    try
                    {
                        if (!grid.CanBeVisibleInView(view)) continue;
                        grid.HideBubbleInView(end, view);
                        hiddenIn++;
                    }
                    catch (RevitExceptions.ArgumentException)
                    {
                        // The grid cannot be shown in this view (outside its range or orientation); leave it.
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"hide bubble of grid '{grid.Name}' in view '{view.Name}'", ex, rolledBack: false);
                    }
                }

                if (hiddenIn == 0) continue;
                affected++;
                report.AddDefect(Id,
                    $"Grid '{grid.Name}' shows its bubble at only one end: hidden at the {EndLabel(end)} in {hiddenIn} of {planViews.Count} plan view(s).",
                    new[] { grid.Id.Value });
            }

            return affected;
        }

        // ---- View-specific extents ---------------------------------------------------------

        /// <summary>
        /// One plan view gets a few grids whose extent is view-specific and shorter at one end.
        /// The datum end 1 is switched from model to view-specific, then its curve in that view is
        /// replaced with a collinear line 1–3 m shorter — every other view keeps the model extent.
        /// </summary>
        private int MakeViewSpecificExtents(GenerationContext context, RandomStream rnd, IReadOnlyList<Grid> grids, IReadOnlyList<ViewPlan> planViews, out ViewPlan view)
        {
            view = null;
            var report = context.Report;
            var wanted = context.Profile.Scaled(1, 2, 3);
            if (wanted <= 0 || grids.Count == 0 || planViews.Count == 0) return 0;

            view = rnd.Pick(planViews);
            var applied = 0;

            foreach (var grid in rnd.TakeDistinct(grids, wanted))
            {
                var shortenMm = Math.Round(rnd.NextDouble(MinShortenMm, MaxShortenMm) / 100) * 100;
                try
                {
                    if (grid.IsCurved || !grid.CanBeVisibleInView(view))
                    {
                        report.AddFallback(Id, $"Grid '{grid.Name}' cannot get a view-specific extent in '{view.Name}' (not a straight grid visible in that view).", new[] { grid.Id.Value });
                        continue;
                    }

                    grid.SetDatumExtentType(DatumEnds.End1, view, DatumExtentType.ViewSpecific);

                    var curves = grid.GetCurvesInView(DatumExtentType.ViewSpecific, view);
                    var line = curves == null || curves.Count == 0 ? null : curves[0] as Line;
                    if (line == null)
                    {
                        report.AddFallback(Id, $"Grid '{grid.Name}' has no straight view-specific curve in '{view.Name}'; extent left alone.", new[] { grid.Id.Value });
                        continue;
                    }

                    var shorter = ShortenAtEnd(line, UnitConversion.MmToFeet(shortenMm), UnitConversion.MmToFeet(MinRemainingGridMm), out var actualShortenFeet);
                    if (shorter == null || !grid.IsCurveValidInView(DatumExtentType.ViewSpecific, view, shorter))
                    {
                        report.AddFallback(Id, $"Grid '{grid.Name}' is too short in '{view.Name}' for a view-specific extent; left alone.", new[] { grid.Id.Value });
                        continue;
                    }

                    grid.SetCurveInView(DatumExtentType.ViewSpecific, view, shorter);
                    applied++;
                    report.AddDefect(Id,
                        $"Grid '{grid.Name}' has a different extent in view '{view.Name}' than everywhere else (view-specific, {Fmt(UnitConversion.FeetToMm(actualShortenFeet))} mm shorter at the {EndLabel(DatumEnds.End1)}).",
                        new[] { grid.Id.Value, view.Id.Value });
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"set view-specific extent of grid '{grid.Name}' in view '{view.Name}'", ex, rolledBack: false);
                }
            }

            return applied;
        }

        /// <summary>
        /// A collinear line with the same start and an end pulled back by <paramref name="shortenFeet"/>,
        /// clamped so at least <paramref name="minRemainingFeet"/> remains. Null when even that is impossible.
        /// </summary>
        private static Line ShortenAtEnd(Line line, double shortenFeet, double minRemainingFeet, out double actualShortenFeet)
        {
            actualShortenFeet = 0;
            var p0 = line.GetEndPoint(0);
            var p1 = line.GetEndPoint(1);
            var length = p0.DistanceTo(p1);
            if (length <= minRemainingFeet) return null;

            actualShortenFeet = Math.Min(shortenFeet, length - minRemainingFeet);
            if (actualShortenFeet <= 1e-6) return null;

            var direction = (p1 - p0).Normalize();
            var newEnd = p1 - direction * actualShortenFeet;
            return Line.CreateBound(p0, newEnd);
        }

        // ---- Level bubbles -----------------------------------------------------------------

        /// <summary>
        /// In one generated section or elevation, one or two levels lose the bubble at one end.
        /// Silently skipped when there is no such view or a level cannot be shown in it.
        /// </summary>
        private int HideLevelBubbles(GenerationContext context, RandomStream rnd, IReadOnlyList<Level> levels, out View view)
        {
            view = null;
            var report = context.Report;
            if (levels.Count == 0) return 0;

            var candidates = context.Views
                .Where(v => v != null && v.IsValidObject && !v.IsTemplate && (v.ViewType == ViewType.Section || v.ViewType == ViewType.Elevation))
                .ToList();
            if (candidates.Count == 0)
            {
                report.AddFallback(Id, "No generated section or elevation view; level bubbles left as they are.");
                return 0;
            }

            view = rnd.Pick(candidates);
            var count = Math.Min(levels.Count, rnd.NextIntInclusive(1, 2));
            var hidden = 0;

            foreach (var level in rnd.TakeDistinct(levels, count))
            {
                var end = rnd.NextBool() ? DatumEnds.End0 : DatumEnds.End1;
                try
                {
                    if (!level.CanBeVisibleInView(view)) continue;
                    level.HideBubbleInView(end, view);
                    hidden++;
                    report.AddDefect(Id,
                        $"Level '{level.Name}' has its bubble hidden at the {EndLabel(end)} in {view.ViewType.ToString().ToLowerInvariant()} view '{view.Name}' only.",
                        new[] { level.Id.Value, view.Id.Value });
                }
                catch (RevitExceptions.ArgumentException)
                {
                    // Not visible in this view; nothing to hide.
                }
                catch (RevitExceptions.InvalidOperationException)
                {
                    // This datum does not support bubble operations here.
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"hide bubble of level '{level.Name}' in view '{view.Name}'", ex, rolledBack: false);
                }
            }

            return hidden;
        }

        // ---- Wall joins --------------------------------------------------------------------

        /// <summary>
        /// One or two more partition walls (as many as the profile allows per level) get a join
        /// disallowed at one end, on top of the ones the planner already marked — a corner that
        /// used to clean up no longer does.
        /// </summary>
        private int DisallowExtraJoins(GenerationContext context, RandomStream rnd)
        {
            var report = context.Report;
            var perLevel = context.Profile.DisallowedJoinsPerLevel;
            if (perLevel <= 0) return 0;

            var candidates = context.Baseline.Walls
                .Where(w => w.Role == WallRole.Partition && w.DisallowJoinMask == 0)
                .Where(w => context.Walls.TryGetValue(w.Index, out var wall) && wall != null && wall.IsValidObject)
                .ToList();
            if (candidates.Count == 0) return 0;

            var wanted = Math.Min(2, Math.Max(1, perLevel));
            var done = 0;

            foreach (var spec in rnd.TakeDistinct(candidates, wanted))
            {
                var end = rnd.NextInt(0, 2);
                var wall = context.Walls[spec.Index];
                try
                {
                    if (!WallUtils.IsWallJoinAllowedAtEnd(wall, end)) continue;
                    WallUtils.DisallowWallJoinAtEnd(wall, end);
                    done++;
                    var levelName = context.Baseline.Levels.FirstOrDefault(l => l.Index == spec.LevelIndex)?.CleanName ?? $"level {spec.LevelIndex}";
                    report.AddDefect(Id,
                        $"{levelName}: partition wall {DescribeLine(spec.Line)} had its join disallowed at {(end == 0 ? "the start" : "the end")} after the corner was built, so it no longer cleans up.",
                        new[] { wall.Id.Value });
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"disallow join at end {end} of wall {spec.Index}", ex, rolledBack: false);
                }
            }

            return done;
        }

        // ---- Helpers -----------------------------------------------------------------------

        private static string EndLabel(DatumEnds end) => end == DatumEnds.End0 ? "start" : "end";

        private static string Fmt(double mm) => mm.ToString("0", CultureInfo.InvariantCulture);

        private static string DescribeLine(Segment2D s) =>
            s.IsHorizontal() ? $"along y={Fmt(s.Start.Y)}" : s.IsVertical() ? $"along x={Fmt(s.Start.X)}" : $"from {s.Start} to {s.End}";
    }
}
