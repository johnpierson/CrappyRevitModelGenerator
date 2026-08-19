using System;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Planning;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Levels, one floor plan per buildable level, grids, walls and floors — the skeleton every
    /// other scenario builds on (plan section 8, steps 2–3). Geometry comes entirely from
    /// <see cref="BaselinePlanner"/>; datum defects are already in the plan when the datum
    /// scenario is enabled, and are copied into the report here with that scenario's id.
    ///
    /// Individual element failures are recorded and skipped so a template quirk (one wall
    /// type Revit dislikes at one spot) does not abort the run; only a baseline with no walls
    /// at all is treated as fatal.
    /// </summary>
    public sealed class BaselineModelScenario : IBadModelScenario
    {
        public string Id => ScenarioIds.Baseline;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Types.BasicWallTypes.Count == 0)
            {
                reason = "The document has no basic wall types.";
                return false;
            }
            if (context.Types.FloorPlanType == null)
            {
                reason = "The document has no floor plan view family type.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var report = context.Report;
            var factory = context.Factory;
            var datumDefects = context.IsScenarioEnabled(ScenarioIds.Datum);

            var plan = BaselinePlanner.Plan(context.Settings, context.Random, datumDefects);
            context.Baseline = plan;

            // Levels, lowest first, then a plan view for each buildable one.
            foreach (var spec in plan.Levels.OrderBy(l => l.ElevationMm))
            {
                var level = factory.CreateLevel(spec);
                if (spec.IsBuildable)
                {
                    try
                    {
                        factory.CreateFloorPlan(level, spec.CleanName, spec.Index);
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"create plan view for {spec.CleanName}", ex, rolledBack: false);
                    }
                }
                else
                {
                    report.AddDefect(ScenarioIds.Datum, $"Level '{spec.CleanName}' has no plan view associated with it.", new[] { level.Id.Value });
                }
            }

            foreach (var spec in plan.Grids)
            {
                try
                {
                    factory.CreateGrid(spec);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create grid {spec.CleanName}", ex, rolledBack: false);
                }
            }

            var wallsCreated = 0;
            foreach (var spec in plan.Walls)
            {
                var level = context.LevelFor(spec.LevelIndex);
                if (level == null)
                {
                    report.AddException(Id, $"create wall {spec.Index}", new InvalidOperationException($"Level index {spec.LevelIndex} was not created."), rolledBack: false);
                    continue;
                }
                try
                {
                    factory.CreateWall(spec, level, spec.AttachTopToLevelAbove ? context.LevelAbove(spec.LevelIndex) : null);
                    wallsCreated++;
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create wall {spec.Index} ({spec.Role} {spec.Line})", ex, rolledBack: false);
                }
            }

            if (wallsCreated == 0 && plan.Walls.Count > 0)
                throw new InvalidOperationException("No walls could be created; the baseline cannot continue.");

            if (context.Settings.CreateFloors)
            {
                if (context.Types.FloorType == null)
                {
                    report.AddFallback(Id, "No floor type available; floors skipped.");
                }
                else
                {
                    foreach (var spec in plan.Floors)
                    {
                        var level = context.LevelFor(spec.LevelIndex);
                        if (level == null) continue;
                        try
                        {
                            factory.CreateFloor(spec, level);
                        }
                        catch (Exception ex)
                        {
                            report.AddException(Id, $"create floor {spec.Index}", ex, rolledBack: false);
                        }
                    }
                }
            }

            // Every planted defect, attributed to the scenario that asked for it, with the ids
            // of the walls it concerns where the planner recorded them.
            foreach (var defect in plan.Defects)
            {
                var ids = defect.RelatedIndices
                    .Select(i => context.Walls.TryGetValue(i, out var w) ? w.Id.Value : (long?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id.Value);
                report.AddDefect(defect.ScenarioId, defect.Message, ids);
            }

            report.AddInfo(Id, $"Baseline: {plan.Levels.Count} level(s), {plan.Grids.Count} grid(s), {wallsCreated}/{plan.Walls.Count} wall(s), {context.Floors.Count} floor(s), {context.PlanViews.Count} plan view(s), footprint {plan.Footprint.Width:0} x {plan.Footprint.Depth:0} mm.");
        }
    }
}
