using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;
using RevitExceptions = Autodesk.Revit.Exceptions;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Poor-but-valid parameter values on generated elements only (plan section 7.7): Comments
    /// and Mark on walls, floors, doors, windows and furniture; Department, Occupancy and
    /// Comments on rooms; Drawn By, Checked By and Issue Date on sheets; "Title on Sheet" on a
    /// few views; Description, Type Mark, Manufacturer, Model, URL and Type Comments on the
    /// types the content-types scenario duplicated; Class, Description and Comments on generated
    /// materials; plus one planted conflict where a type named "...-new" is described as
    /// "OLD - superseded".
    ///
    /// Every field on every element is rolled independently against the severity profile:
    /// <see cref="SeverityProfile.BlankMetadataFraction"/> of fields are cleared,
    /// <see cref="SeverityProfile.BadMetadataFraction"/> get a shorthand / typo / stale value
    /// from <see cref="BadNames"/>, and the remainder are left exactly as Revit created them —
    /// so the result is inconsistent rather than uniformly wrong, which is what a real project
    /// with weak standards looks like. Marks reuse the previous element's mark with
    /// <see cref="SeverityProfile.DuplicateMarkFraction"/>; Revit's duplicate-value warning is
    /// on the expected list in <see cref="ExpectedWarnings"/> and is dismissed automatically.
    ///
    /// Only elements in the run's registry (or the context collections filled by earlier
    /// scenarios) are touched; the template's own types, materials and views are never modified.
    /// Every write goes through <see cref="ElementFactory.TrySet(Element, BuiltInParameter, string)"/>,
    /// which refuses read-only or missing parameters instead of throwing, and each element
    /// operation is isolated so a single odd element cannot roll the scenario back.
    /// </summary>
    public sealed class MetadataScenario : IBadModelScenario
    {
        private const string StreamName = "metadata/values";
        private const int MaxIdsPerNote = 12;
        private const int MaxDuplicateLines = 8;

        // Value lists specific to this scenario; the shared ones live in BadNames (Core is frozen).
        private static readonly IReadOnlyList<string> Departments = new[] { "Admin", "admin", "ADMIN", "?", "Dept 1", "tbd", "" };
        private static readonly IReadOnlyList<string> Occupancies = new[] { "", "1", "TBD" };
        private static readonly IReadOnlyList<string> DrawnBy = new[] { "JP", "jp", "??", "-", "" };
        private static readonly IReadOnlyList<string> CheckedBy = new[] { "", "Checker", "chk", "same" };
        private static readonly IReadOnlyList<string> IssueDates = new[] { "3/12/22", "2022-03-12", "TBD", "", "12 Mar" };
        private static readonly IReadOnlyList<string> TitlesOnSheet = new[] { "PLAN", "plan - CHECK", "OLD" };
        private static readonly IReadOnlyList<string> MaterialClasses = new[] { "Misc", "misc", "MISC", "Generic", "generic", "?", "" };

        /// <summary>Type-name fragment and a Description that contradicts it; the first fragment found is planted.</summary>
        private static readonly (string Fragment, string Description)[] TypeNameConflicts =
        {
            ("new", "OLD - superseded"),
            ("old", "NEW - use this one"),
            ("final", "DRAFT - not final"),
            ("do not use", "Preferred type - use this"),
            ("copy", "Original"),
        };

        private enum Choice
        {
            Leave,
            Blank,
            Bad,
        }

        public string Id => ScenarioIds.Metadata;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Baseline == null)
            {
                reason = "The baseline plan is missing; there is nothing to annotate.";
                return false;
            }
            if (context.Registry.Committed.Count == 0)
            {
                reason = "No generated elements exist yet.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var stream = context.Random.Stream(StreamName);
            var totals = new Totals();

            ApplyToInstances(context, stream, totals);
            ApplyToRooms(context, stream, totals);
            ApplyToSheets(context, stream, totals);
            ApplyToViews(context, stream, totals);
            ApplyToTypes(context, stream, totals);
            ApplyToMaterials(context, stream, totals);

            context.Report.AddInfo(Id,
                $"Metadata: {totals.Writes} parameter value(s) written on {totals.Elements.Count} generated element(s) - " +
                $"instances {totals.Count(Totals.Instances)}, rooms {totals.Count(Totals.Rooms)}, sheets {totals.Count(Totals.Sheets)}, " +
                $"views {totals.Count(Totals.Views)}, types {totals.Count(Totals.Types)}, materials {totals.Count(Totals.Materials)}.");
        }

        // ---- Instances: walls, floors, doors, windows, furniture -----------------------------

        private void ApplyToInstances(GenerationContext ctx, RandomStream stream, Totals totals)
        {
            var groups = new (GeneratedCategory Category, string Label)[]
            {
                (GeneratedCategory.Walls, "walls"),
                (GeneratedCategory.Floors, "floors"),
                (GeneratedCategory.Doors, "doors"),
                (GeneratedCategory.Windows, "windows"),
                (GeneratedCategory.Furniture, "furniture"),
            };

            var commentField = new[] { new FieldSpec("Comments", BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, BadNames.Comments) };
            var commentStats = NewStats(commentField);
            var markStats = new MarkStats("Mark");
            var seen = new HashSet<long>();
            var total = 0;

            void Process(IReadOnlyList<Element> elements, string label)
            {
                if (elements.Count == 0) return;
                total += elements.Count;
                ApplyFields(ctx, stream, elements, commentField, commentStats, Totals.Instances, totals);
                // Duplicate marks are tracked per category: that is where Revit (and a schedule) notices them.
                ApplyMarks(ctx, stream, elements, label, BuiltInParameter.ALL_MODEL_MARK, BadNames.Marks, markStats, Totals.Instances, totals);
            }

            foreach (var (category, label) in groups)
            {
                var elements = Valid(ctx.GeneratedElements(category));
                foreach (var e in elements) seen.Add(e.Id.Value);
                Process(elements, label);
            }

            // Anything indexed by an earlier scenario but registered under another category
            // (e.g. an opening tagged as Other) still belongs to the run; sweep it up last.
            // Dictionaries are read by key so the order of draws is stable.
            var indexed = new List<Element>();
            indexed.AddRange(ctx.Walls.OrderBy(p => p.Key).Select(p => (Element)p.Value));
            indexed.AddRange(ctx.Floors.OrderBy(p => p.Key).Select(p => (Element)p.Value));
            indexed.AddRange(ctx.Openings.OrderBy(p => p.Key).Select(p => (Element)p.Value));
            indexed.AddRange(ctx.Furniture.OrderBy(p => p.Key).Select(p => (Element)p.Value));
            var leftovers = Valid(indexed).Where(e => seen.Add(e.Id.Value)).ToList();
            Process(leftovers, "other generated instances");

            if (total == 0)
            {
                ctx.Report.AddInfo(Id, "No generated walls, floors, doors, windows or furniture to annotate.");
                return;
            }

            ReportFields(ctx, commentStats, "instance element(s)", total);
            ReportMarks(ctx, markStats, "instance element(s)", total);
        }

        // ---- Rooms ---------------------------------------------------------------------------

        private void ApplyToRooms(GenerationContext ctx, RandomStream stream, Totals totals)
        {
            // The registry is the authority: it includes unplaced rooms and any room the rooms
            // scenario created without indexing it in context.RoomElements.
            var rooms = Valid(ctx.GeneratedElements(GeneratedCategory.Rooms));
            if (rooms.Count == 0) return;

            var fields = new[]
            {
                new FieldSpec("Department", BuiltInParameter.ROOM_DEPARTMENT, Departments),
                new FieldSpec("Occupancy", BuiltInParameter.ROOM_OCCUPANCY, Occupancies),
                new FieldSpec("Comments", BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, BadNames.Comments),
            };
            var stats = NewStats(fields);
            ApplyFields(ctx, stream, rooms, fields, stats, Totals.Rooms, totals);
            ReportFields(ctx, stats, "room(s)", rooms.Count);
        }

        // ---- Sheets --------------------------------------------------------------------------

        private void ApplyToSheets(GenerationContext ctx, RandomStream stream, Totals totals)
        {
            var sheets = Valid(ctx.Sheets);
            if (sheets.Count == 0) return;

            var fields = new[]
            {
                new FieldSpec("Drawn By", BuiltInParameter.SHEET_DRAWN_BY, DrawnBy),
                new FieldSpec("Checked By", BuiltInParameter.SHEET_CHECKED_BY, CheckedBy),
                new FieldSpec("Issue Date", BuiltInParameter.SHEET_ISSUE_DATE, IssueDates),
            };
            var stats = NewStats(fields);
            ApplyFields(ctx, stream, sheets, fields, stats, Totals.Sheets, totals);
            ReportFields(ctx, stats, "sheet(s)", sheets.Count);
        }

        // ---- Views: a misleading "Title on Sheet" on a few -----------------------------------

        private void ApplyToViews(GenerationContext ctx, RandomStream stream, Totals totals)
        {
            var views = new List<View>();
            foreach (var view in ctx.Views)
            {
                if (view == null || !view.IsValidObject) continue;
                try
                {
                    if (view.IsTemplate) continue;
                }
                catch (RevitExceptions.ApplicationException)
                {
                    continue;
                }
                views.Add(view);
            }
            if (views.Count == 0) return;

            // "A few": the bad fraction of the generated views, at least one.
            var count = Math.Min(views.Count, Math.Max(1, (int)Math.Round(views.Count * ctx.Profile.BadMetadataFraction)));
            var chosen = stream.TakeDistinct(views, count);

            var byValue = new SortedDictionary<string, int>(StringComparer.Ordinal);
            var ids = new List<long>();
            foreach (var view in chosen)
            {
                var id = view.Id.Value;
                try
                {
                    var title = stream.Pick(TitlesOnSheet);
                    if (!ctx.Factory.TrySet(view, BuiltInParameter.VIEW_DESCRIPTION, title)) continue;
                    ids.Add(id);
                    byValue.TryGetValue(title, out var n);
                    byValue[title] = n + 1;
                    totals.Touch(Totals.Views, id);
                }
                catch (Exception ex)
                {
                    ctx.Report.AddException(Id, $"set Title on Sheet on view {id}", ex, rolledBack: false);
                }
            }

            if (ids.Count == 0) return;
            var detail = string.Join(", ", byValue.Select(kv => $"'{kv.Key}' x{kv.Value}"));
            ctx.Report.AddDefect(Id, $"{ids.Count} of {views.Count} generated view(s) got a misleading 'Title on Sheet' ({detail}).", ids.Take(MaxIdsPerNote));
        }

        // ---- Generated types -----------------------------------------------------------------

        private void ApplyToTypes(GenerationContext ctx, RandomStream stream, Totals totals)
        {
            var types = Valid(ctx.DuplicatedTypes);
            if (types.Count == 0)
            {
                ctx.Report.AddInfo(Id, "No generated types to annotate (the content-types scenario did not run or created none).");
                return;
            }

            var fields = new[]
            {
                new FieldSpec("Description", BuiltInParameter.ALL_MODEL_DESCRIPTION, BadNames.Descriptions),
                new FieldSpec("Manufacturer", BuiltInParameter.ALL_MODEL_MANUFACTURER, BadNames.Manufacturers),
                new FieldSpec("Model", BuiltInParameter.ALL_MODEL_MODEL, BadNames.Models),
                new FieldSpec("URL", BuiltInParameter.ALL_MODEL_URL, BadNames.Urls),
                new FieldSpec("Type Comments", BuiltInParameter.ALL_MODEL_TYPE_COMMENTS, BadNames.Comments),
            };
            var stats = NewStats(fields);
            ApplyFields(ctx, stream, types, fields, stats, Totals.Types, totals);
            ReportFields(ctx, stats, "generated type(s)", types.Count);

            // Type Mark, with duplicates tracked per Revit category (walls, doors, ...) — GroupBy
            // keeps first-appearance order, so the sequence of draws is stable.
            var markStats = new MarkStats("Type Mark");
            foreach (var group in types.GroupBy(CategoryLabel))
                ApplyMarks(ctx, stream, group.ToList(), group.Key, BuiltInParameter.ALL_MODEL_TYPE_MARK, BadNames.TypeMarks, markStats, Totals.Types, totals);
            ReportMarks(ctx, markStats, "generated type(s)", types.Count);

            PlantTypeNameConflict(ctx, types, totals);
        }

        /// <summary>
        /// One "value contradicts the name" defect: the first duplicated type whose name contains a
        /// fragment from <see cref="TypeNameConflicts"/> gets the matching Description, overriding
        /// whatever the random pass wrote.
        /// </summary>
        private void PlantTypeNameConflict(GenerationContext ctx, IReadOnlyList<Element> types, Totals totals)
        {
            foreach (var (fragment, description) in TypeNameConflicts)
            {
                foreach (var type in types)
                {
                    string name;
                    try
                    {
                        name = type.Name ?? string.Empty;
                    }
                    catch (RevitExceptions.ApplicationException)
                    {
                        continue;
                    }
                    if (name.IndexOf(fragment, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    var id = type.Id.Value;
                    try
                    {
                        if (!ctx.Factory.TrySet(type, BuiltInParameter.ALL_MODEL_DESCRIPTION, description)) continue;
                        totals.Touch(Totals.Types, id);
                        ctx.Report.AddDefect(Id, $"Type '{name}' has Description '{description}', which contradicts its name.", new[] { id });
                        return;
                    }
                    catch (Exception ex)
                    {
                        ctx.Report.AddException(Id, $"set Description on type {id}", ex, rolledBack: false);
                    }
                }
            }
            ctx.Report.AddInfo(Id, "No generated type name suited the planted 'description contradicts type name' defect; skipped.");
        }

        // ---- Generated materials -------------------------------------------------------------

        private void ApplyToMaterials(GenerationContext ctx, RandomStream stream, Totals totals)
        {
            var materials = new List<Material>();
            foreach (var m in ctx.Materials)
                if (m != null && m.IsValidObject) materials.Add(m);
            if (materials.Count == 0) return;

            // Class is a property, not a parameter, so it does not go through the field helper.
            var classStats = new FieldStats("Class");
            foreach (var material in materials)
            {
                var id = material.Id.Value;
                try
                {
                    var choice = Roll(stream, ctx.Profile);
                    if (choice == Choice.Leave)
                    {
                        classStats.Left++;
                        continue;
                    }
                    var value = choice == Choice.Blank ? string.Empty : stream.Pick(MaterialClasses);
                    if (!TrySetMaterialClass(ctx, material, value))
                    {
                        classStats.Unwritable++;
                        continue;
                    }
                    classStats.Record(value, id);
                    totals.Touch(Totals.Materials, id);
                }
                catch (Exception ex)
                {
                    ctx.Report.AddException(Id, $"set Class on material {id}", ex, rolledBack: false);
                }
            }

            var fields = new[]
            {
                new FieldSpec("Description", BuiltInParameter.ALL_MODEL_DESCRIPTION, BadNames.Descriptions),
                new FieldSpec("Comments", BuiltInParameter.ALL_MODEL_INSTANCE_COMMENTS, BadNames.Comments),
            };
            var stats = NewStats(fields);
            ApplyFields(ctx, stream, materials.Cast<Element>().ToList(), fields, stats, Totals.Materials, totals);

            var all = new List<FieldStats> { classStats };
            all.AddRange(stats);
            ReportFields(ctx, all, "generated material(s)", materials.Count);
        }

        private bool TrySetMaterialClass(GenerationContext ctx, Material material, string value)
        {
            try
            {
                material.MaterialClass = value;
                return true;
            }
            catch (RevitExceptions.ArgumentException)
            {
                // This version rejects the value (an empty class, most likely); treat as not writable.
                return false;
            }
            catch (RevitExceptions.InvalidOperationException)
            {
                return false;
            }
            catch (Exception ex)
            {
                ctx.Report.AddException(Id, $"set Class on material {material.Id.Value}", ex, rolledBack: false);
                return false;
            }
        }

        // ---- The generic passes --------------------------------------------------------------

        /// <summary>Blank / Bad / Leave with the profile's fractions; the remainder is Leave.</summary>
        private static Choice Roll(RandomStream stream, SeverityProfile profile)
        {
            var r = stream.NextDouble();
            if (r < profile.BlankMetadataFraction) return Choice.Blank;
            if (r < profile.BlankMetadataFraction + profile.BadMetadataFraction) return Choice.Bad;
            return Choice.Leave;
        }

        /// <summary>
        /// For every element and every field: roll, write the chosen value, count. Stats are
        /// accumulated into <paramref name="stats"/> (one per field, same order as
        /// <paramref name="fields"/>) so a caller can run several element groups into one tally.
        /// </summary>
        private void ApplyFields(GenerationContext ctx, RandomStream stream, IReadOnlyList<Element> elements, IReadOnlyList<FieldSpec> fields,
            IReadOnlyList<FieldStats> stats, string totalsGroup, Totals totals)
        {
            foreach (var element in elements)
            {
                var id = element.Id.Value;
                for (var i = 0; i < fields.Count; i++)
                {
                    var field = fields[i];
                    var s = stats[i];
                    try
                    {
                        var choice = Roll(stream, ctx.Profile);
                        if (choice == Choice.Leave)
                        {
                            s.Left++;
                            continue;
                        }
                        var value = choice == Choice.Blank ? string.Empty : stream.Pick(field.Values);
                        if (!ctx.Factory.TrySet(element, field.Parameter, value))
                        {
                            s.Unwritable++;
                            continue;
                        }
                        s.Record(value, id);
                        totals.Touch(totalsGroup, id);
                    }
                    catch (Exception ex)
                    {
                        ctx.Report.AddException(Id, $"set {field.Label} on element {id}", ex, rolledBack: false);
                    }
                }
            }
        }

        /// <summary>
        /// Mark-like fields: same roll, but a Bad value reuses the previous element's mark with
        /// the profile's duplicate fraction. <paramref name="groupLabel"/> names the group the
        /// duplicates are counted in ("doors", "Walls", ...).
        /// </summary>
        private void ApplyMarks(GenerationContext ctx, RandomStream stream, IReadOnlyList<Element> elements, string groupLabel,
            BuiltInParameter parameter, IReadOnlyList<string> values, MarkStats stats, string totalsGroup, Totals totals)
        {
            string lastMark = null;
            foreach (var element in elements)
            {
                var id = element.Id.Value;
                try
                {
                    var choice = Roll(stream, ctx.Profile);
                    if (choice == Choice.Leave)
                    {
                        stats.Left++;
                        continue;
                    }

                    string value;
                    var reused = false;
                    if (choice == Choice.Blank)
                    {
                        value = string.Empty;
                    }
                    else if (!string.IsNullOrEmpty(lastMark) && stream.NextBool(ctx.Profile.DuplicateMarkFraction))
                    {
                        value = lastMark;
                        reused = true;
                    }
                    else
                    {
                        value = stream.Pick(values);
                    }

                    if (!ctx.Factory.TrySet(element, parameter, value))
                    {
                        stats.Unwritable++;
                        continue;
                    }
                    stats.Record(value, id, reused, groupLabel);
                    totals.Touch(totalsGroup, id);
                    lastMark = value;
                }
                catch (Exception ex)
                {
                    ctx.Report.AddException(Id, $"set {stats.Label} on element {id}", ex, rolledBack: false);
                }
            }
        }

        // ---- Reporting -----------------------------------------------------------------------

        private void ReportFields(GenerationContext ctx, IReadOnlyList<FieldStats> stats, string what, int total)
        {
            var written = stats.Sum(s => s.Written);
            if (written == 0) return;

            var ids = stats.SelectMany(s => s.Ids).Distinct().ToList();
            var labels = stats.Count == 1
                ? stats[0].Label
                : string.Join(", ", stats.Take(stats.Count - 1).Select(s => s.Label)) + " and " + stats[stats.Count - 1].Label;
            var detail = string.Join("; ", stats.Select(s => s.Describe()));
            ctx.Report.AddDefect(Id, $"{ids.Count} of {total} {what} got inconsistent {labels}: {detail}.", ids.Take(MaxIdsPerNote));
        }

        private void ReportMarks(GenerationContext ctx, MarkStats stats, string what, int total)
        {
            if (stats.Written == 0) return;

            ctx.Report.AddDefect(Id,
                $"{stats.Ids.Count} of {total} {what} got inconsistent {stats.Label} values: cleared {stats.Blank}, arbitrary {stats.Bad} " +
                $"(of which {stats.Duplicated} deliberately reused the previous one), untouched {stats.Left}" +
                (stats.Unwritable > 0 ? $", not writable {stats.Unwritable}" : string.Empty) + ".",
                stats.Ids.Take(MaxIdsPerNote));

            var groups = stats.DuplicateGroups();
            foreach (var g in groups.Take(MaxDuplicateLines))
                ctx.Report.AddDefect(Id, $"Duplicate {stats.Label} '{g.Mark}' on {g.Ids.Count} {g.Group}.", g.Ids.Take(MaxIdsPerNote));

            if (groups.Count > MaxDuplicateLines)
            {
                var rest = groups.Skip(MaxDuplicateLines).ToList();
                ctx.Report.AddDefect(Id, $"{rest.Count} more duplicate {stats.Label} group(s) across {rest.Sum(g => g.Ids.Count)} element(s).",
                    rest.SelectMany(g => g.Ids).Take(MaxIdsPerNote));
            }
        }

        // ---- Helpers -------------------------------------------------------------------------

        private static List<Element> Valid<T>(IEnumerable<T> elements) where T : Element
        {
            var list = new List<Element>();
            if (elements == null) return list;
            foreach (var e in elements)
                if (e != null && e.IsValidObject) list.Add(e);
            return list;
        }

        private static string CategoryLabel(Element element)
        {
            try
            {
                var name = element.Category?.Name;
                return string.IsNullOrEmpty(name) ? "types" : name.ToLowerInvariant() + " types";
            }
            catch (RevitExceptions.ApplicationException)
            {
                return "types";
            }
        }

        private static List<FieldStats> NewStats(IReadOnlyList<FieldSpec> fields) =>
            fields.Select(f => new FieldStats(f.Label)).ToList();

        private sealed class FieldSpec
        {
            public FieldSpec(string label, BuiltInParameter parameter, IReadOnlyList<string> values)
            {
                Label = label;
                Parameter = parameter;
                Values = values;
            }

            public string Label { get; }
            public BuiltInParameter Parameter { get; }
            public IReadOnlyList<string> Values { get; }
        }

        private class FieldStats
        {
            public FieldStats(string label)
            {
                Label = label;
            }

            public string Label { get; }
            public int Blank;
            public int Bad;
            public int Left;
            public int Unwritable;
            public readonly List<long> Ids = new List<long>();

            public int Written => Blank + Bad;

            public void Record(string value, long id)
            {
                if (string.IsNullOrEmpty(value)) Blank++;
                else Bad++;
                Ids.Add(id);
            }

            public string Describe() =>
                $"{Label} blank {Blank}, bad {Bad}, untouched {Left}" + (Unwritable > 0 ? $", not writable {Unwritable}" : string.Empty);
        }

        private sealed class MarkStats : FieldStats
        {
            private readonly Dictionary<(string Group, string Mark), List<long>> _groups = new Dictionary<(string Group, string Mark), List<long>>();

            public MarkStats(string label) : base(label)
            {
            }

            /// <summary>How many Bad values deliberately copied the previous element's mark.</summary>
            public int Duplicated;

            public void Record(string value, long id, bool reused, string group)
            {
                Record(value, id);
                if (string.IsNullOrEmpty(value)) return;
                if (reused) Duplicated++;
                var key = (group, value);
                if (!_groups.TryGetValue(key, out var ids)) _groups[key] = ids = new List<long>();
                ids.Add(id);
            }

            /// <summary>Marks used more than once within a group, largest group first (stable order for the report).</summary>
            public List<(string Group, string Mark, List<long> Ids)> DuplicateGroups() =>
                _groups.Where(g => g.Value.Count > 1)
                    .OrderByDescending(g => g.Value.Count)
                    .ThenBy(g => g.Key.Group, StringComparer.Ordinal)
                    .ThenBy(g => g.Key.Mark, StringComparer.Ordinal)
                    .Select(g => (g.Key.Group, g.Key.Mark, g.Value))
                    .ToList();
        }

        private sealed class Totals
        {
            public const string Instances = "instances";
            public const string Rooms = "rooms";
            public const string Sheets = "sheets";
            public const string Views = "views";
            public const string Types = "types";
            public const string Materials = "materials";

            private readonly Dictionary<string, HashSet<long>> _byGroup = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);

            public int Writes;
            public readonly HashSet<long> Elements = new HashSet<long>();

            public void Touch(string group, long id)
            {
                Writes++;
                Elements.Add(id);
                if (!_byGroup.TryGetValue(group, out var set)) _byGroup[group] = set = new HashSet<long>();
                set.Add(id);
            }

            public int Count(string group) => _byGroup.TryGetValue(group, out var set) ? set.Count : 0;
        }
    }
}
