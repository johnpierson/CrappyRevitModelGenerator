using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Near-duplicate types and materials (plan section 7.5). Duplicates the wall, floor and
    /// family types the baseline used under suffixes such as "-new", "_2" and " copy"; assigns
    /// some of the copies to a handful of generated walls, one floor and one door/window/
    /// furniture instance so otherwise-identical elements end up with inconsistent types, and
    /// leaves the rest loaded but unused. Creates materials with names like "New Mat" and
    /// "DO NOT USE", near-duplicates of them that differ only by a few colour steps, and swaps
    /// one layer of each duplicated wall/floor type to one of those materials so the copy
    /// silently differs from its source.
    ///
    /// Only types and materials this run created are ever modified — the template's own
    /// types are duplicated, never edited — and only generated walls, floors and instances are
    /// retyped. Descriptions, type marks and comments on the duplicates are left to the
    /// metadata scenario (order 80). Everything is per-element try/catch so one type Revit
    /// refuses to retype does not roll the whole scenario back.
    /// </summary>
    public sealed class ContentTypesScenario : IBadModelScenario
    {
        private const string WallStream = "content-types/walls";
        private const string FloorStream = "content-types/floors";
        private const string FamilyStream = "content-types/families";
        private const string MaterialStream = "content-types/materials";

        public string Id => ScenarioIds.ContentTypes;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Baseline == null)
            {
                reason = "The baseline model was not created.";
                return false;
            }
            if (context.Types.BasicWallTypes.Count == 0)
            {
                reason = "The document has no basic wall types to duplicate.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var report = context.Report;

            // Run-wide caps: other scenarios may already have duplicated types or created
            // materials, so the budget is what is left under the hard limits.
            var typeBudget = Math.Max(0, GenerationLimits.MaxDuplicateTypes - context.DuplicatedTypes.Count);
            var materialBudget = Math.Max(0, GenerationLimits.MaxMaterials - context.Materials.Count);

            var wallDups = new List<TypeDuplicate<WallType>>();
            var floorDups = new List<TypeDuplicate<FloorType>>();
            var familyDups = new List<TypeDuplicate<FamilySymbol>>();

            var wallCount = Math.Min(context.Profile.DuplicateWallTypes, typeBudget);
            typeBudget -= DuplicateWallTypes(context, wallCount, wallDups);

            var floorCount = Math.Min(context.Profile.DuplicateFloorTypes, typeBudget);
            typeBudget -= DuplicateFloorTypes(context, floorCount, floorDups);

            var familyCount = Math.Min(context.Profile.DuplicateFamilyTypes, typeBudget);
            typeBudget -= DuplicateFamilyTypes(context, familyCount, familyDups);

            var newMaterials = new List<Material>();
            var nearDuplicateMaterials = new List<Material>();
            var newCount = Math.Min(context.Profile.Materials, materialBudget);
            materialBudget -= CreateMaterials(context, newCount, newMaterials);

            // Near-duplicates copy this scenario's own materials; if none could be created, any
            // material an earlier scenario generated is an acceptable source (never a template one).
            var nearCount = Math.Min(context.Profile.NearDuplicateMaterials, materialBudget);
            var nearSources = newMaterials.Count > 0 ? newMaterials : context.Materials.Where(m => m != null && m.IsValidObject).ToList();
            materialBudget -= CreateNearDuplicateMaterials(context, nearCount, nearSources, nearDuplicateMaterials);

            var generatedMaterials = newMaterials.Concat(nearDuplicateMaterials).ToList();
            var assignedMaterialIds = ApplyMaterialsToDuplicates(context, wallDups, floorDups, generatedMaterials);

            var unusedMaterials = generatedMaterials.Where(m => !assignedMaterialIds.Contains(m.Id.Value)).ToList();
            if (unusedMaterials.Count > 0)
            {
                report.AddDefect(Id,
                    $"{unusedMaterials.Count} generated material(s) with unhelpful names are not assigned to anything: {Quoted(unusedMaterials.Select(m => m.Name))}.",
                    unusedMaterials.Select(m => m.Id.Value));
            }

            if (typeBudget <= 0 && (context.Profile.DuplicateWallTypes + context.Profile.DuplicateFloorTypes + context.Profile.DuplicateFamilyTypes) > wallDups.Count + floorDups.Count + familyDups.Count)
                report.AddInfo(Id, $"Duplicate type cap ({GenerationLimits.MaxDuplicateTypes}) reached; some duplicates were not created.");
            if (materialBudget <= 0 && (context.Profile.Materials + context.Profile.NearDuplicateMaterials) > generatedMaterials.Count)
                report.AddInfo(Id, $"Material cap ({GenerationLimits.MaxMaterials}) reached; some materials were not created.");

            report.AddInfo(Id,
                $"Content types: {wallDups.Count} wall type(s) ({wallDups.Count(d => d.Used)} used), " +
                $"{floorDups.Count} floor type(s) ({floorDups.Count(d => d.Used)} used), " +
                $"{familyDups.Count} family type(s) ({familyDups.Count(d => d.Used)} used), " +
                $"{newMaterials.Count} material(s), {nearDuplicateMaterials.Count} near-duplicate material(s), " +
                $"{assignedMaterialIds.Count} material(s) applied to duplicated type layers.");
            report.AddInfo(Id,
                "Descriptions, type marks and comments on the duplicated types are left for the metadata scenario"
                + (context.IsScenarioEnabled(ScenarioIds.Metadata) ? " (enabled)." : " (disabled in this run, so they stay as copied)."));
        }

        // ---- Wall types ------------------------------------------------------------------

        /// <summary>Duplicates the interior/exterior wall types; returns how many were created.</summary>
        private int DuplicateWallTypes(GenerationContext context, int count, List<TypeDuplicate<WallType>> created)
        {
            if (count <= 0) return 0;
            var report = context.Report;
            var stream = context.Random.Stream(WallStream);

            var sources = new List<WallType>();
            foreach (var t in new[] { context.Types.InteriorWallType, context.Types.ExteriorWallType })
            {
                if (t != null && sources.All(s => s.Id != t.Id)) sources.Add(t);
            }
            if (sources.Count == 0)
            {
                report.AddFallback(Id, "No interior/exterior wall type resolved; wall type duplicates skipped.");
                return 0;
            }

            foreach (var source in stream.TakeCycling(sources, count))
            {
                var desired = source.Name + stream.Pick(BadNames.TypeSuffixes);
                try
                {
                    var dup = context.Factory.DuplicateType(source, desired) as WallType;
                    if (dup == null)
                    {
                        report.AddFallback(Id, $"Wall type '{source.Name}' could not be duplicated as '{desired}'.");
                        continue;
                    }
                    created.Add(new TypeDuplicate<WallType>(source, dup));
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"duplicate wall type '{source.Name}'", ex, rolledBack: false);
                }
            }
            if (created.Count == 0) return 0;

            // About half of the copies get used on one or two walls each — walls that already
            // carry the source type, so a row of identical partitions ends up with two type
            // names. The rest stay loaded and unused.
            var usedCount = (created.Count + 1) / 2;
            var usedIndices = new HashSet<int>(stream.TakeDistinct(Enumerable.Range(0, created.Count).ToList(), usedCount));
            var reassigned = new HashSet<long>();
            var generatedWalls = context.Walls.OrderBy(kv => kv.Key).Select(kv => kv.Value).Where(w => w != null && w.IsValidObject).ToList();

            for (var i = 0; i < created.Count; i++)
            {
                var entry = created[i];
                if (!usedIndices.Contains(i) || generatedWalls.Count == 0) continue;

                var wanted = stream.NextIntInclusive(1, 2);
                var preferred = generatedWalls.Where(w => !reassigned.Contains(w.Id.Value) && SameType(w, entry.Source)).ToList();
                var targets = stream.TakeDistinct(preferred, wanted);
                if (targets.Count < wanted)
                {
                    var others = generatedWalls.Where(w => !reassigned.Contains(w.Id.Value) && targets.All(t => t.Id != w.Id)).ToList();
                    targets.AddRange(stream.TakeDistinct(others, wanted - targets.Count));
                }

                foreach (var wall in targets)
                {
                    try
                    {
                        wall.WallType = entry.Duplicate;
                        reassigned.Add(wall.Id.Value);
                        entry.AssignedIds.Add(wall.Id.Value);
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"assign wall type '{entry.Duplicate.Name}' to wall {wall.Id.Value}", ex, rolledBack: false);
                    }
                }
            }

            ReportTypeDuplicates(context, "wall type", created, "wall(s)");
            return created.Count;
        }

        private static bool SameType(Wall wall, WallType type)
        {
            try
            {
                var current = wall.WallType;
                return current != null && current.Id == type.Id;
            }
            catch
            {
                return false;
            }
        }

        // ---- Floor types -----------------------------------------------------------------

        private int DuplicateFloorTypes(GenerationContext context, int count, List<TypeDuplicate<FloorType>> created)
        {
            if (count <= 0) return 0;
            var report = context.Report;
            var stream = context.Random.Stream(FloorStream);

            var source = context.Types.FloorType;
            if (source == null)
            {
                report.AddFallback(Id, "No floor type available; floor type duplicates skipped.");
                return 0;
            }

            for (var i = 0; i < count; i++)
            {
                var desired = source.Name + stream.Pick(BadNames.TypeSuffixes);
                try
                {
                    var dup = context.Factory.DuplicateType(source, desired) as FloorType;
                    if (dup == null)
                    {
                        report.AddFallback(Id, $"Floor type '{source.Name}' could not be duplicated as '{desired}'.");
                        continue;
                    }
                    created.Add(new TypeDuplicate<FloorType>(source, dup));
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"duplicate floor type '{source.Name}'", ex, rolledBack: false);
                }
            }
            if (created.Count == 0) return 0;

            // One copy goes onto one generated floor; the others are left unused.
            var floors = context.Floors.OrderBy(kv => kv.Key).Select(kv => kv.Value).Where(f => f != null && f.IsValidObject).ToList();
            if (floors.Count > 0)
            {
                var entry = created[0];
                var floor = stream.Pick(floors);
                try
                {
                    floor.FloorType = entry.Duplicate;
                    entry.AssignedIds.Add(floor.Id.Value);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"assign floor type '{entry.Duplicate.Name}' to floor {floor.Id.Value}", ex, rolledBack: false);
                }
            }

            ReportTypeDuplicates(context, "floor type", created, "floor(s)");
            return created.Count;
        }

        // ---- Family types ----------------------------------------------------------------

        private int DuplicateFamilyTypes(GenerationContext context, int count, List<TypeDuplicate<FamilySymbol>> created)
        {
            if (count <= 0) return 0;
            var report = context.Report;
            var stream = context.Random.Stream(FamilyStream);

            var sources = new List<FamilySymbol>();
            var candidates = new List<FamilySymbol> { context.Types.DoorSymbol, context.Types.WindowSymbol };
            if (context.Types.FurniturePicks != null) candidates.AddRange(context.Types.FurniturePicks);
            foreach (var s in candidates)
            {
                if (s != null && s.IsValidObject && sources.All(x => x.Id != s.Id)) sources.Add(s);
            }
            if (sources.Count == 0)
            {
                report.AddFallback(Id, "No door, window or furniture family types are loaded; family type duplicates skipped.");
                return 0;
            }

            foreach (var source in stream.TakeCycling(sources, count))
            {
                var desired = source.Name + stream.Pick(BadNames.TypeSuffixes);
                try
                {
                    var dup = context.Factory.DuplicateType(source, desired) as FamilySymbol;
                    if (dup == null)
                    {
                        report.AddFallback(Id, $"Family type '{FamilyTypeName(source)}' could not be duplicated as '{desired}'.");
                        continue;
                    }
                    created.Add(new TypeDuplicate<FamilySymbol>(source, dup));
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"duplicate family type '{FamilyTypeName(source)}'", ex, rolledBack: false);
                }
            }
            if (created.Count == 0) return 0;

            // Exactly one copy gets used, on one generated instance of the same family; the
            // first copy whose family actually has a placed instance wins.
            var instances = context.Openings.OrderBy(kv => kv.Key).Select(kv => kv.Value)
                .Concat(context.Furniture.OrderBy(kv => kv.Key).Select(kv => kv.Value))
                .Where(f => f != null && f.IsValidObject)
                .ToList();

            foreach (var entry in created)
            {
                var sameSymbol = instances.Where(f => SymbolIs(f, entry.Source)).ToList();
                var pool = sameSymbol.Count > 0 ? sameSymbol : instances.Where(f => SameFamily(f, entry.Source)).ToList();
                if (pool.Count == 0) continue;

                var instance = stream.Pick(pool);
                try
                {
                    context.Types.EnsureActive(entry.Duplicate);
                    instance.Symbol = entry.Duplicate;
                    entry.AssignedIds.Add(instance.Id.Value);
                    break;
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"assign family type '{entry.Duplicate.Name}' to instance {instance.Id.Value}", ex, rolledBack: false);
                }
            }

            ReportTypeDuplicates(context, "family type", created, "instance(s)");
            return created.Count;
        }

        private static bool SymbolIs(FamilyInstance instance, FamilySymbol symbol)
        {
            try
            {
                var current = instance.Symbol;
                return current != null && current.Id == symbol.Id;
            }
            catch
            {
                return false;
            }
        }

        private static bool SameFamily(FamilyInstance instance, FamilySymbol symbol)
        {
            try
            {
                var current = instance.Symbol;
                return current?.Family != null && symbol.Family != null && current.Family.Id == symbol.Family.Id;
            }
            catch
            {
                return false;
            }
        }

        private static string FamilyTypeName(FamilySymbol symbol)
        {
            if (symbol == null) return "<none>";
            var family = symbol.Family;
            return family == null ? symbol.Name : $"{family.Name} : {symbol.Name}";
        }

        // ---- Materials -------------------------------------------------------------------

        private int CreateMaterials(GenerationContext context, int count, List<Material> created)
        {
            if (count <= 0) return 0;
            var report = context.Report;
            var stream = context.Random.Stream(MaterialStream);

            for (var i = 0; i < count; i++)
            {
                var desired = stream.Pick(BadNames.MaterialNames);
                // Grey-ish: one base tone with a small per-channel wobble, so the swatches look
                // like the same "default grey" someone kept re-creating.
                var tone = stream.NextIntInclusive(90, 200);
                var r = ClampByte(tone + stream.NextIntInclusive(-12, 12), 90, 200);
                var g = ClampByte(tone + stream.NextIntInclusive(-12, 12), 90, 200);
                var b = ClampByte(tone + stream.NextIntInclusive(-12, 12), 90, 200);
                try
                {
                    var material = context.Factory.CreateMaterial(desired);
                    if (material == null)
                    {
                        report.AddFallback(Id, $"Material '{desired}' could not be created (every candidate name was rejected).");
                        continue;
                    }
                    try
                    {
                        material.Color = new Color(r, g, b);
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"set colour of material '{material.Name}'", ex, rolledBack: false);
                    }
                    created.Add(material);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create material '{desired}'", ex, rolledBack: false);
                }
            }

            if (created.Count > 0)
            {
                report.AddDefect(Id,
                    $"{created.Count} material(s) with meaningless names: {Quoted(created.Select(m => m.Name))}.",
                    created.Select(m => m.Id.Value));
            }
            return created.Count;
        }

        private int CreateNearDuplicateMaterials(GenerationContext context, int count, IReadOnlyList<Material> sources, List<Material> created)
        {
            if (count <= 0) return 0;
            var report = context.Report;
            var stream = context.Random.Stream(MaterialStream);

            if (sources == null || sources.Count == 0)
            {
                report.AddFallback(Id, "No generated material to copy; near-duplicate materials skipped.");
                return 0;
            }

            for (var i = 0; i < count; i++)
            {
                var source = stream.Pick(sources);
                var desired = source.Name + stream.Pick(BadNames.TypeSuffixes);
                var dr = stream.NextIntInclusive(-5, 5);
                var dg = stream.NextIntInclusive(-5, 5);
                var db = stream.NextIntInclusive(-5, 5);
                if (dr == 0 && dg == 0 && db == 0) db = 3; // must differ, or it is a plain duplicate

                try
                {
                    var dup = context.Factory.DuplicateMaterial(source, desired);
                    if (dup == null)
                    {
                        report.AddFallback(Id, $"Material '{source.Name}' could not be duplicated as '{desired}'.");
                        continue;
                    }

                    var delta = 0;
                    try
                    {
                        var c = source.Color;
                        if (c != null && c.IsValid)
                        {
                            var r = ClampByte(c.Red + dr, 0, 255);
                            var g = ClampByte(c.Green + dg, 0, 255);
                            var b = ClampByte(c.Blue + db, 0, 255);
                            if (r == c.Red && g == c.Green && b == c.Blue) b = ClampByte(b > 250 ? b - 3 : b + 3, 0, 255);
                            dup.Color = new Color(r, g, b);
                            delta = Math.Max(Math.Abs(r - c.Red), Math.Max(Math.Abs(g - c.Green), Math.Abs(b - c.Blue)));
                        }
                        else
                        {
                            dup.Color = new Color(ClampByte(128 + dr, 0, 255), ClampByte(128 + dg, 0, 255), ClampByte(128 + db, 0, 255));
                            delta = Math.Max(Math.Abs(dr), Math.Max(Math.Abs(dg), Math.Abs(db)));
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"nudge colour of material '{dup.Name}'", ex, rolledBack: false);
                    }

                    created.Add(dup);
                    report.AddDefect(Id,
                        $"Near-duplicate material '{dup.Name}' differs from '{source.Name}' only by colour ({delta}/255 on one channel).",
                        new[] { dup.Id.Value, source.Id.Value });
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"duplicate material '{source.Name}'", ex, rolledBack: false);
                }
            }
            return created.Count;
        }

        /// <summary>
        /// Swap one layer (outermost or innermost) of every duplicated wall/floor type to a
        /// generated material so the copy differs invisibly from its source. Returns the ids of
        /// the materials that ended up assigned.
        /// </summary>
        private HashSet<long> ApplyMaterialsToDuplicates(GenerationContext context, List<TypeDuplicate<WallType>> wallDups,
            List<TypeDuplicate<FloorType>> floorDups, IReadOnlyList<Material> materials)
        {
            var assigned = new HashSet<long>();
            if (materials == null || materials.Count == 0)
            {
                if (wallDups.Count + floorDups.Count > 0)
                    context.Report.AddFallback(Id, "No generated materials; duplicated types keep their source materials.");
                return assigned;
            }

            var stream = context.Random.Stream(MaterialStream);
            var targets = wallDups.Select(d => new HostTypePair(d.Source, d.Duplicate, "wall type"))
                .Concat(floorDups.Select(d => new HostTypePair(d.Source, d.Duplicate, "floor type")))
                .ToList();

            foreach (var pair in targets)
            {
                var material = stream.Pick(materials);
                var outermost = stream.NextBool();
                try
                {
                    var cs = pair.Duplicate.GetCompoundStructure();
                    if (cs == null || cs.LayerCount <= 0) continue;

                    var layerIndex = outermost ? 0 : cs.LayerCount - 1;
                    var previousId = cs.GetMaterialId(layerIndex);
                    var previousName = MaterialName(context.Document, previousId);

                    cs.SetMaterialId(layerIndex, material.Id);
                    pair.Duplicate.SetCompoundStructure(cs);
                    assigned.Add(material.Id.Value);

                    context.Report.AddDefect(Id,
                        $"Duplicate {pair.Kind} '{pair.Duplicate.Name}' uses material '{material.Name}' on layer {layerIndex} while its source '{pair.Source.Name}' uses '{previousName}'.",
                        new[] { pair.Duplicate.Id.Value, material.Id.Value });
                }
                catch (Exception ex)
                {
                    context.Report.AddException(Id, $"set layer material on {pair.Kind} '{pair.Duplicate.Name}'", ex, rolledBack: false);
                }
            }
            return assigned;
        }

        // ---- Reporting helpers -----------------------------------------------------------

        private void ReportTypeDuplicates<T>(GenerationContext context, string kind, List<TypeDuplicate<T>> created, string targetNoun) where T : ElementType
        {
            foreach (var entry in created)
            {
                if (entry.Used)
                {
                    context.Report.AddDefect(Id,
                        $"Near-duplicate {kind} '{entry.Duplicate.Name}' (copy of '{entry.Source.Name}') assigned to {entry.AssignedIds.Count} {targetNoun} that otherwise match their neighbours.",
                        new[] { entry.Duplicate.Id.Value }.Concat(entry.AssignedIds));
                }
                else
                {
                    context.Report.AddDefect(Id,
                        $"Unused near-duplicate {kind} '{entry.Duplicate.Name}' (copy of '{entry.Source.Name}') is loaded but never used.",
                        new[] { entry.Duplicate.Id.Value });
                }
            }
        }

        private static string MaterialName(Document doc, ElementId id)
        {
            if (doc == null || id == null || id == ElementId.InvalidElementId) return "<By Category>";
            try
            {
                return doc.GetElement(id)?.Name ?? "<By Category>";
            }
            catch
            {
                return "<unknown>";
            }
        }

        private static string Quoted(IEnumerable<string> names) =>
            string.Join(", ", names.Take(12).Select(n => $"'{n}'"));

        private static byte ClampByte(int value, int min, int max) =>
            (byte)Math.Max(min, Math.Min(max, value));

        /// <summary>A duplicated type, its source, and the generated elements it was assigned to.</summary>
        private sealed class TypeDuplicate<T> where T : ElementType
        {
            public TypeDuplicate(T source, T duplicate)
            {
                Source = source;
                Duplicate = duplicate;
            }

            public T Source { get; }
            public T Duplicate { get; }
            public List<long> AssignedIds { get; } = new List<long>();
            public bool Used => AssignedIds.Count > 0;
        }

        private sealed class HostTypePair
        {
            public HostTypePair(HostObjAttributes source, HostObjAttributes duplicate, string kind)
            {
                Source = source;
                Duplicate = duplicate;
                Kind = kind;
            }

            public HostObjAttributes Source { get; }
            public HostObjAttributes Duplicate { get; }
            public string Kind { get; }
        }
    }
}
