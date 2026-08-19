using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Planning;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Poor naming and identity (plan section 7.1). Runs after everything that creates
    /// nameable elements and renames only what the generator itself created: levels get
    /// inconsistent names in an order that does not match their elevations, grids get a mix of
    /// letters, numbers and phrases, views get "View 1" / "Copy of Copy" / "Use This One" with
    /// near-duplicate pairs that differ only in case or a trailing space, and sheets get numbers
    /// and names that mix every convention at once.
    ///
    /// Rooms are not touched here: <see cref="RoomPlanner"/> already draws room names and
    /// numbers from the bad lists when this scenario is enabled. Duplicated types and generated
    /// materials are named by the content-types scenario, which runs later.
    ///
    /// Every name goes through <see cref="ElementFactory.TrySetName"/> (or
    /// <see cref="ElementFactory.TrySetSheetNumber"/>), which walks the sanitiser's candidate
    /// list until Revit accepts one, so a taken name becomes "L1 (2)" rather than an exception
    /// and nothing illegal ever reaches the document.
    /// </summary>
    public sealed class NamingScenario : IBadModelScenario
    {
        private const string StreamLevels = "naming/levels";
        private const string StreamGrids = "naming/grids";
        private const string StreamViews = "naming/views";
        private const string StreamSheets = "naming/sheets";

        /// <summary>
        /// Reserved for a type/material rename pass. Not drawn from today: the content-types
        /// scenario names its own creations. Kept so a future pass has a stable stream name that
        /// does not disturb the four streams above.
        /// </summary>
        private const string StreamTypes = "naming/types";

        /// <summary>How many rename examples a summary line quotes before trailing off.</summary>
        private const int MaxExamples = 8;

        /// <summary>
        /// Pairs from <see cref="BadNames.ViewNames"/> that differ only in case, punctuation or a
        /// trailing space. Both members are forced into the view name assignment when there are
        /// enough views, so the "similar names for unrelated views" defect is guaranteed rather
        /// than left to chance.
        /// </summary>
        private static readonly (string A, string B)[] NearDuplicateViewPairs =
        {
            ("Use This One", "use this one"),
            ("Plan_02 ", "plan-02"),
            ("temp", "TEMP"),
            ("3D - FINAL - FINAL2", "3D - FINAL"),
        };

        public string Id => ScenarioIds.Naming;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Baseline == null)
            {
                reason = "The baseline scenario did not run; there is nothing to rename.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var report = context.Report;

            var levelsRenamed = RenameLevels(context);
            var gridsRenamed = RenameGrids(context);
            var viewsRenamed = RenameViews(context);
            var sheetsRenamed = RenameSheets(context);

            // Rooms: the planner already used the bad lists (see RoomPlanner.AssignName).
            if (context.IsScenarioEnabled(ScenarioIds.Rooms))
                report.AddInfo(Id, "Room names and numbers were drawn from the bad-name lists by the rooms scenario because naming is enabled; nothing to rename here.");
            else
                report.AddInfo(Id, "The rooms scenario is off, so there are no generated rooms to rename.");

            // Types and materials: named at creation by the content-types scenario (order 70),
            // which runs after this one; StreamTypes stays unused until a rename pass exists.
            report.AddInfo(Id, "Duplicated types and generated materials are named by the content-types scenario, which runs after naming; nothing to rename here.");

            report.AddInfo(Id, $"Naming: {levelsRenamed} level(s), {gridsRenamed} grid(s), {viewsRenamed} view(s) and {sheetsRenamed} sheet(s) renamed.");
        }

        // ---- Levels ----------------------------------------------------------------------

        private int RenameLevels(GenerationContext context)
        {
            var report = context.Report;
            var factory = context.Factory;
            var rnd = context.Random.Stream(StreamLevels);

            var levels = context.Levels.Values
                .Where(IsUsable)
                .GroupBy(l => l.Id.Value).Select(g => g.First())
                .OrderBy(l => l.ProjectElevation).ThenBy(l => l.Id.Value)
                .ToList();
            if (levels.Count == 0)
            {
                report.AddInfo(Id, "No generated levels to rename.");
                return 0;
            }

            // Lowest level takes the first bad name, and so on; the list is written in that
            // order ("L1", "Level 2", "Mezz", ...). Anything beyond the list uses alternates.
            var desired = new List<string>(levels.Count);
            for (var i = 0; i < levels.Count; i++)
                desired.Add(i < BadNames.LevelNames.Count ? BadNames.LevelNames[i] : null);
            var missing = desired.Count(d => d == null);
            if (missing > 0)
            {
                var alternates = rnd.TakeCycling(BadNames.LevelNameAlternates, missing);
                var k = 0;
                for (var i = 0; i < desired.Count; i++)
                    if (desired[i] == null) desired[i] = alternates[k++];
            }

            // Medium/High: two adjacent levels trade names so the numbering no longer follows
            // the elevations ("Level 2" sits below "L1").
            var swapIndex = -1;
            var swaps = context.Profile.Scaled(low: 0, medium: 1, high: 1);
            if (swaps > 0 && levels.Count >= 2)
            {
                swapIndex = rnd.NextInt(0, levels.Count - 1);
                (desired[swapIndex], desired[swapIndex + 1]) = (desired[swapIndex + 1], desired[swapIndex]);
            }

            var applied = new string[levels.Count];
            var renamed = 0;
            for (var i = 0; i < levels.Count; i++)
            {
                var level = levels[i];
                try
                {
                    var before = level.Name;
                    var name = factory.TrySetName(level, desired[i]);
                    if (name == null)
                    {
                        // Every candidate of the primary name was rejected; try an alternate pattern.
                        var alternate = rnd.Pick(BadNames.LevelNameAlternates);
                        name = factory.TrySetName(level, alternate);
                    }
                    if (name == null)
                    {
                        report.AddFallback(Id, $"Level '{before}' could not be renamed; Revit rejected every candidate for '{desired[i]}'.", new[] { level.Id.Value });
                        continue;
                    }
                    applied[i] = name;
                    renamed++;
                    report.AddDefect(Id, $"Level renamed '{before}' -> '{name}'.", new[] { level.Id.Value });
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"rename level {level.Id.Value}", ex, rolledBack: false);
                }
            }

            if (swapIndex >= 0 && applied[swapIndex] != null && applied[swapIndex + 1] != null)
            {
                var lower = levels[swapIndex];
                var upper = levels[swapIndex + 1];
                report.AddDefect(Id,
                    $"Level names are out of order: '{applied[swapIndex]}' at {Mm(lower)} mm sits below '{applied[swapIndex + 1]}' at {Mm(upper)} mm.",
                    new[] { lower.Id.Value, upper.Id.Value });
            }

            return renamed;
        }

        // ---- Grids -----------------------------------------------------------------------

        private int RenameGrids(GenerationContext context)
        {
            var report = context.Report;
            var factory = context.Factory;
            var rnd = context.Random.Stream(StreamGrids);

            var grids = context.Grids
                .OrderBy(kv => kv.Key)
                .Select(kv => kv.Value)
                .Where(IsUsable)
                .GroupBy(g => g.Id.Value).Select(g => g.First())
                .ToList();
            if (grids.Count == 0)
            {
                report.AddInfo(Id, "No generated grids to rename.");
                return 0;
            }

            // Grid names must be unique; TrySetName appends a bad suffix when a name is taken,
            // including by a generated grid that has not been renamed yet.
            var names = rnd.TakeCycling(BadNames.GridNames, grids.Count);
            var examples = new List<string>();
            var ids = new List<long>();
            for (var i = 0; i < grids.Count; i++)
            {
                var grid = grids[i];
                try
                {
                    var before = grid.Name;
                    var name = factory.TrySetName(grid, names[i]);
                    if (name == null)
                    {
                        report.AddFallback(Id, $"Grid '{before}' could not be renamed; Revit rejected every candidate for '{names[i]}'.", new[] { grid.Id.Value });
                        continue;
                    }
                    examples.Add($"'{before}' -> '{name}'");
                    ids.Add(grid.Id.Value);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"rename grid {grid.Id.Value}", ex, rolledBack: false);
                }
            }

            if (ids.Count > 0)
                report.AddDefect(Id, $"{ids.Count} grid(s) renamed to a mix of letters, numbers and phrases: {Examples(examples)}.", ids);

            return ids.Count;
        }

        // ---- Views -----------------------------------------------------------------------

        private int RenameViews(GenerationContext context)
        {
            var report = context.Report;
            var factory = context.Factory;
            var rnd = context.Random.Stream(StreamViews);

            // Every generated view (baseline plans, duplicates, sections, elevations, 3D,
            // drafting); templates and sheets are handled elsewhere or not at all.
            var views = context.Views
                .Where(v => IsUsable(v) && !v.IsTemplate && !(v is ViewSheet))
                .GroupBy(v => v.Id.Value).Select(g => g.First())
                .ToList();
            if (views.Count == 0)
            {
                report.AddInfo(Id, "No generated views to rename.");
                return 0;
            }

            var names = rnd.TakeCycling(BadNames.ViewNames, views.Count);
            var pairsWanted = Math.Min(Math.Min(NearDuplicateViewPairs.Length, views.Count / 2), context.Profile.Scaled(low: 1, medium: 2, high: 3));
            var forcedPairs = ForceNearDuplicatePairs(names, pairsWanted, rnd);

            var applied = new string[views.Count];
            var examples = new List<string>();
            var ids = new List<long>();
            for (var i = 0; i < views.Count; i++)
            {
                var view = views[i];
                try
                {
                    var before = view.Name;
                    var name = factory.TrySetName(view, names[i]);
                    if (name == null)
                    {
                        report.AddFallback(Id, $"View '{before}' could not be renamed; Revit rejected every candidate for '{names[i]}'.", new[] { view.Id.Value });
                        continue;
                    }
                    applied[i] = name;
                    examples.Add($"'{before}' -> '{name}'");
                    ids.Add(view.Id.Value);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"rename view {view.Id.Value}", ex, rolledBack: false);
                }
            }

            if (ids.Count > 0)
                report.AddDefect(Id, $"{ids.Count} view(s) renamed to meaningless or misleading names: {Examples(examples)}.", ids);

            // Report each near-duplicate pair that actually landed (Revit may have added a
            // suffix to one member if it treats the two as the same name; still a near-duplicate).
            foreach (var pair in forcedPairs)
            {
                var a = applied[pair.IndexA];
                var b = applied[pair.IndexB];
                if (a == null || b == null) continue;
                report.AddDefect(Id, $"Views '{a}' and '{b}' have names that differ only in case, punctuation or trailing whitespace.",
                    new[] { views[pair.IndexA].Id.Value, views[pair.IndexB].Id.Value });
            }

            return ids.Count;
        }

        /// <summary>
        /// Make sure both members of the first <paramref name="pairsWanted"/> near-duplicate pairs
        /// occur in <paramref name="names"/>, replacing random unreserved slots for any member
        /// that is missing. Returns the slot indices of each pair that was placed.
        /// </summary>
        private static List<(int IndexA, int IndexB)> ForceNearDuplicatePairs(List<string> names, int pairsWanted, RandomStream rnd)
        {
            var placed = new List<(int IndexA, int IndexB)>();
            if (names == null || names.Count < 2 || pairsWanted <= 0) return placed;

            var reserved = new HashSet<int>();
            foreach (var (a, b) in NearDuplicateViewPairs.Take(pairsWanted))
            {
                var ia = names.IndexOf(a);
                var ib = names.IndexOf(b);
                if (ia >= 0) reserved.Add(ia);
                if (ib >= 0) reserved.Add(ib);

                if (ia < 0)
                {
                    ia = PickFreeSlot(names.Count, reserved, rnd);
                    if (ia < 0) break;
                    names[ia] = a;
                    reserved.Add(ia);
                }
                if (ib < 0)
                {
                    ib = PickFreeSlot(names.Count, reserved, rnd);
                    if (ib < 0) break;
                    names[ib] = b;
                    reserved.Add(ib);
                }
                placed.Add((ia, ib));
            }
            return placed;
        }

        private static int PickFreeSlot(int count, HashSet<int> reserved, RandomStream rnd)
        {
            var free = new List<int>(count);
            for (var i = 0; i < count; i++)
                if (!reserved.Contains(i)) free.Add(i);
            return free.Count == 0 ? -1 : rnd.Pick(free);
        }

        // ---- Sheets ----------------------------------------------------------------------

        private int RenameSheets(GenerationContext context)
        {
            var report = context.Report;
            var factory = context.Factory;
            var rnd = context.Random.Stream(StreamSheets);

            var sheets = context.Sheets
                .Where(IsUsable)
                .GroupBy(s => s.Id.Value).Select(g => g.First())
                .ToList();
            if (sheets.Count == 0)
            {
                report.AddInfo(Id, "No generated sheets to rename.");
                return 0;
            }

            // Sheet numbers must be unique (TrySetSheetNumber suffixes clashes); names need not be.
            var numbers = rnd.TakeCycling(BadNames.SheetNumbers, sheets.Count);
            var names = rnd.TakeCycling(BadNames.SheetNames, sheets.Count);
            var examples = new List<string>();
            var ids = new List<long>();
            for (var i = 0; i < sheets.Count; i++)
            {
                var sheet = sheets[i];
                try
                {
                    var beforeNumber = sheet.SheetNumber;
                    var beforeName = sheet.Name;
                    var number = factory.TrySetSheetNumber(sheet, numbers[i]);
                    var name = factory.TrySetName(sheet, names[i]);
                    if (number == null && name == null)
                    {
                        report.AddFallback(Id, $"Sheet '{beforeNumber} - {beforeName}' could not be renumbered or renamed; Revit rejected every candidate.", new[] { sheet.Id.Value });
                        continue;
                    }
                    if (number == null)
                        report.AddFallback(Id, $"Sheet '{beforeNumber}' kept its number; Revit rejected every candidate for '{numbers[i]}'.", new[] { sheet.Id.Value });
                    if (name == null)
                        report.AddFallback(Id, $"Sheet '{beforeNumber}' kept its name; Revit rejected every candidate for '{names[i]}'.", new[] { sheet.Id.Value });

                    examples.Add($"'{beforeNumber} {beforeName}' -> '{number ?? beforeNumber} {name ?? beforeName}'");
                    ids.Add(sheet.Id.Value);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"rename sheet {sheet.Id.Value}", ex, rolledBack: false);
                }
            }

            if (ids.Count > 0)
                report.AddDefect(Id, $"{ids.Count} sheet(s) given numbers and names that mix every convention: {Examples(examples)}.", ids);

            return ids.Count;
        }

        // ---- Helpers ---------------------------------------------------------------------

        /// <summary>False for a null wrapper or an element a rolled-back scenario took away.</summary>
        private static bool IsUsable(Element element) => element != null && element.IsValidObject;

        private static string Examples(IReadOnlyList<string> items)
        {
            if (items == null || items.Count == 0) return string.Empty;
            var shown = string.Join(", ", items.Take(MaxExamples));
            return items.Count > MaxExamples ? shown + $", … (+{items.Count - MaxExamples} more)" : shown;
        }

        private static string Mm(Level level) =>
            UnitConversion.FeetToMm(level.ProjectElevation).ToString("0", CultureInfo.InvariantCulture);
    }
}
