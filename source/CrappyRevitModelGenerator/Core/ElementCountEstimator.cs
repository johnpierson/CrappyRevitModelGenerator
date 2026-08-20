using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core.Planning;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>The estimate shown in the dialog before anything is generated.</summary>
    public sealed class ElementCountEstimate
    {
        public SortedDictionary<string, int> ByCategory { get; } = new SortedDictionary<string, int>(StringComparer.Ordinal);
        public int Total => ByCategory.Values.Sum();

        public void Add(GeneratedCategory category, int count)
        {
            if (count <= 0) return;
            var key = category.ToString();
            ByCategory.TryGetValue(key, out var current);
            ByCategory[key] = current + count;
        }

        public int Of(GeneratedCategory category) => ByCategory.TryGetValue(category.ToString(), out var n) ? n : 0;

        public override string ToString() =>
            string.Join(", ", ByCategory.Select(p => $"{p.Key} {p.Value}")) + $" — total {Total}";
    }

    /// <summary>
    /// Predicts how many elements a run will create. Baseline, content and rooms are exact:
    /// the same planners run with the same seed. Documentation, types, materials and warnings
    /// are upper-bound estimates from the severity profile, because they depend on what the
    /// document already contains (a template with no title block creates fewer sheets, etc.).
    /// </summary>
    public static class ElementCountEstimator
    {
        public static ElementCountEstimate Estimate(GenerationSettings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            var estimate = new ElementCountEstimate();
            var random = new SeededRandom(settings.Seed);
            var enabled = new HashSet<string>(settings.ResolveScenarioIds(), StringComparer.OrdinalIgnoreCase);
            var profile = SeverityProfile.For(settings);

            var baseline = BaselinePlanner.Plan(settings, random, enabled.Contains(ScenarioIds.Datum));
            estimate.Add(GeneratedCategory.Levels, baseline.Levels.Count);
            estimate.Add(GeneratedCategory.Grids, baseline.Grids.Count);
            estimate.Add(GeneratedCategory.Walls, baseline.Walls.Count);
            estimate.Add(GeneratedCategory.Floors, baseline.Floors.Count);

            if (enabled.Contains(ScenarioIds.ContentPlacement))
            {
                var content = ContentPlanner.Plan(baseline, settings, random, geometryDefects: true);
                estimate.Add(GeneratedCategory.Doors, content.Openings.Count(o => o.Kind == OpeningKind.Door));
                estimate.Add(GeneratedCategory.Windows, content.Openings.Count(o => o.Kind == OpeningKind.Window));
                estimate.Add(GeneratedCategory.Furniture, content.Furniture.Count);
            }

            if (enabled.Contains(ScenarioIds.Rooms))
            {
                var rooms = RoomPlanner.Plan(baseline, settings, random, enabled.Contains(ScenarioIds.Naming));
                estimate.Add(GeneratedCategory.Rooms, rooms.Rooms.Count);
                estimate.Add(GeneratedCategory.RoomTags, rooms.TagCount);
                estimate.Add(GeneratedCategory.RoomSeparationLines, rooms.SeparationLines.Count);
                // A fake tag is one text note and four detail lines (registered as Other).
                estimate.Add(GeneratedCategory.TextNotes, rooms.FakeTagCount);
                estimate.Add(GeneratedCategory.Other, rooms.FakeTagCount * (RoomPlan.ElementsPerFakeTag - 1));
            }

            if (enabled.Contains(ScenarioIds.Documentation))
            {
                var buildable = baseline.Levels.Count(l => l.IsBuildable);
                var plans = buildable * (1 + profile.DuplicatePlansPerLevel);
                var views = plans + profile.Sections + profile.Elevations + profile.ThreeDViews + profile.DraftingViews;
                views = Math.Min(views, GenerationLimits.MaxViews);
                var sheets = Math.Min(profile.Sheets, GenerationLimits.MaxSheets);
                estimate.Add(GeneratedCategory.Views, views);
                estimate.Add(GeneratedCategory.Sheets, sheets);
                estimate.Add(GeneratedCategory.Viewports, Math.Min(views, Math.Max(0, sheets - profile.EmptySheets) * 3));
                estimate.Add(GeneratedCategory.TextNotes, profile.TextNotes);
            }

            if (enabled.Contains(ScenarioIds.ContentTypes))
            {
                estimate.Add(GeneratedCategory.Types, Math.Min(GenerationLimits.MaxDuplicateTypes,
                    profile.DuplicateWallTypes + profile.DuplicateFloorTypes + profile.DuplicateFamilyTypes));
                estimate.Add(GeneratedCategory.Materials, Math.Min(GenerationLimits.MaxMaterials, profile.Materials + profile.NearDuplicateMaterials));
            }

            if (enabled.Contains(ScenarioIds.Warnings))
            {
                estimate.Add(GeneratedCategory.Walls, profile.OverlappingWalls);
                estimate.Add(GeneratedCategory.Furniture, profile.DuplicateInstances);
                estimate.Add(GeneratedCategory.Floors, profile.OverlappingFloors);
            }

            // The run record itself.
            estimate.Add(GeneratedCategory.DataStorage, 1);

            return estimate;
        }
    }
}
