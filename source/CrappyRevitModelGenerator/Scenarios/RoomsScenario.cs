using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Architecture;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Core.Planning;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Rooms, room separation lines and room tags (plan section 7.6, order 30). Every decision —
    /// which cells get rooms, which rooms stay unplaced, which cell gets a second room, where the
    /// separation lines go, which tags are omitted, shoved against a wall or faked with a text
    /// note and detail lines — is made by
    /// <see cref="RoomPlanner"/>; this class only turns the plan into elements and gathers the
    /// evidence Revit produces (rooms with zero area, dismissed warnings) into the report.
    ///
    /// Separation lines are created before rooms because they take part in enclosure: the
    /// corridor room is only bounded once the line across the corridor exists. Names and numbers
    /// are written through the room parameters, where any string is legal, so deliberately bad
    /// numbers such as "101 " or exact duplicates survive intact (Revit warns about duplicate
    /// numbers and rooms sharing a region; those warnings are on the expected list and are
    /// dismissed and recorded automatically). Individual failures are recorded and skipped; the
    /// scenario draws no randomness of its own beyond the planner's streams.
    /// </summary>
    public sealed class RoomsScenario : IBadModelScenario
    {
        // Defect tags the planner puts on specs (RoomPlanner is Core and frozen, so mirrored here).
        private const string TagBrokenEnclosure = "in-broken-enclosure";
        private const string TagDuplicateInRegion = "duplicate-in-region";
        private const string TagUnplaced = "unplaced";
        private const string TagUntagged = "untagged";
        private const string TagAwkwardTag = "awkward-tag";
        private const string TagWallWouldDo = "wall-would-do";

        public string Id => ScenarioIds.Rooms;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (!context.Settings.CreateRooms)
            {
                reason = "Rooms are disabled in the settings.";
                return false;
            }
            if (context.Baseline == null || context.Baseline.Cells.Count == 0)
            {
                reason = "The baseline produced no room cells.";
                return false;
            }
            if (context.Levels.Count == 0)
            {
                reason = "No generated levels exist to host rooms.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var report = context.Report;
            var factory = context.Factory;

            var plan = RoomPlanner.Plan(context.Baseline, context.Settings, context.Random, badNaming: context.IsScenarioEnabled(ScenarioIds.Naming));
            context.Rooms = plan;

            if (plan.Rooms.Count == 0 && plan.SeparationLines.Count == 0)
            {
                report.AddInfo(Id, "The room planner produced nothing to create (no non-corridor cells and no corridor).");
                return;
            }

            // Levels whose separation lines / tags had to be skipped because no plan view exists.
            var levelsWithoutPlanView = new SortedSet<int>();
            var linesSkippedNoView = 0;
            var tagsSkippedNoView = 0;

            // ---- 1. Separation lines first: they take part in enclosure --------------------
            var wallWouldDoLineIds = new List<long>();
            var linesCreated = 0;
            foreach (var group in plan.SeparationLines.GroupBy(s => s.LevelIndex).OrderBy(g => g.Key))
            {
                var specs = group.OrderBy(s => s.Index).ToList();
                var level = context.LevelFor(group.Key);
                var planView = context.PlanViewFor(group.Key);
                if (level == null || planView == null)
                {
                    levelsWithoutPlanView.Add(group.Key);
                    linesSkippedNoView += specs.Count;
                    continue;
                }

                var lines = CreateSeparationLines(context, specs, group.Key, level, planView);
                for (var i = 0; i < specs.Count; i++)
                {
                    if (lines[i] == null) continue;
                    linesCreated++;
                    if (specs[i].DefectTags.Contains(TagWallWouldDo)) wallWouldDoLineIds.Add(lines[i].Id.Value);
                }
            }

            // ---- 2. Rooms -------------------------------------------------------------------
            var placedCount = 0;
            var unplacedCount = 0;
            var brokenEnclosureIds = new List<long>();
            var duplicateInRegionIds = new List<long>();
            var unplacedIds = new List<long>();
            var untaggedIds = new List<long>();

            foreach (var spec in plan.Rooms)
            {
                var room = CreateRoomElement(context, spec);
                if (room == null) continue;

                ApplyNameAndNumber(context, room, spec);
                context.RoomElements[spec.Index] = room;

                if (spec.IsPlaced) placedCount++; else unplacedCount++;
                var id = room.Id.Value;
                if (spec.DefectTags.Contains(TagBrokenEnclosure)) brokenEnclosureIds.Add(id);
                if (spec.DefectTags.Contains(TagDuplicateInRegion)) duplicateInRegionIds.Add(id);
                if (spec.DefectTags.Contains(TagUnplaced)) unplacedIds.Add(id);
                if (spec.DefectTags.Contains(TagUntagged)) untaggedIds.Add(id);
            }

            // Rooms compute their boundaries (and therefore Area) on regeneration; tags also
            // need the room to be resolved before they can reference it.
            try
            {
                context.Document.Regenerate();
            }
            catch (Exception ex)
            {
                report.AddException(Id, "regenerate after creating rooms", ex, rolledBack: false);
            }

            // ---- 3. Tags --------------------------------------------------------------------
            var taggedCount = 0;
            var fakeTaggedCount = 0;
            var fakeTagsSkippedNoTextType = 0;
            var awkwardTagIds = new List<long>();
            var fakeTagIds = new List<long>();
            foreach (var spec in plan.Rooms.Where(r => r.IsPlaced && r.CreateTag))
            {
                if (!context.RoomElements.TryGetValue(spec.Index, out var room) || room == null) continue;

                var planView = context.PlanViewFor(spec.LevelIndex);
                if (planView == null)
                {
                    levelsWithoutPlanView.Add(spec.LevelIndex);
                    tagsSkippedNoView++;
                    continue;
                }

                var tagPoint = spec.Location.Value.Plus(spec.TagOffsetMm);

                if (spec.FakeTag)
                {
                    if (context.Types.TextNoteType == null)
                    {
                        fakeTagsSkippedNoTextType++;
                        continue;
                    }
                    try
                    {
                        var ids = CreateFakeTag(context, planView, spec, tagPoint);
                        if (ids.Count > 0)
                        {
                            fakeTaggedCount++;
                            fakeTagIds.AddRange(ids);
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"fake-tag room {spec.Number} '{spec.Name}' at {tagPoint}", ex, rolledBack: false);
                    }
                    continue;
                }

                try
                {
                    var tag = factory.CreateRoomTag(room, UnitConversion.ToUV(tagPoint), planView);
                    if (tag == null)
                    {
                        report.AddException(Id, $"tag room {spec.Number} '{spec.Name}'", new InvalidOperationException("Revit returned no room tag."), rolledBack: false);
                        continue;
                    }
                    taggedCount++;
                    if (spec.DefectTags.Contains(TagAwkwardTag)) awkwardTagIds.Add(tag.Id.Value);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"tag room {spec.Number} '{spec.Name}' at {tagPoint}", ex, rolledBack: false);
                }
            }
            // The factory adds whatever NewRoomTag returned to the shared list; keep it null-free.
            context.RoomTags.RemoveAll(t => t == null);

            // ---- 4. Evidence: placed rooms Revit could not bound -----------------------------
            var zeroAreaIds = new List<long>();
            foreach (var spec in plan.Rooms.Where(r => r.IsPlaced))
            {
                if (!context.RoomElements.TryGetValue(spec.Index, out var room) || room == null) continue;
                double area;
                try { area = room.Area; }
                catch (Exception) { continue; }
                if (area <= 1e-9) zeroAreaIds.Add(room.Id.Value);
            }

            // ---- 5. Report ------------------------------------------------------------------
            foreach (var defect in plan.Defects)
            {
                // RelatedIndices, when the planner fills them, are room indices.
                var ids = defect.RelatedIndices
                    .Select(i => context.RoomElements.TryGetValue(i, out var r) && r != null ? r.Id.Value : (long?)null)
                    .Where(id => id.HasValue)
                    .Select(id => id.Value);
                report.AddDefect(defect.ScenarioId, defect.Message, ids);
            }

            // One id-bearing line per defect kind so each planted condition can be audited.
            AddSummaryDefect(report, "Rooms placed in a cell whose partition gap merges it with a neighbour (partially bounded)", brokenEnclosureIds);
            AddSummaryDefect(report, "Rooms that share an enclosed region with another room", duplicateInRegionIds);
            AddSummaryDefect(report, "Rooms that exist but are not placed", unplacedIds);
            AddSummaryDefect(report, "Placed rooms with no room tag", untaggedIds);
            AddSummaryDefect(report, "Room tags pushed against a wall instead of the room centre", awkwardTagIds);
            AddSummaryDefect(report, "Room separation lines used where a wall would have been sufficient", wallWouldDoLineIds);

            if (fakeTagIds.Count > 0)
            {
                report.AddDefect(Id,
                    $"{fakeTaggedCount} room(s) are 'tagged' with a text note and detail lines instead of a real room tag; the text will not update when the room does.",
                    fakeTagIds);
            }

            if (zeroAreaIds.Count > 0)
            {
                report.AddDefect(Id,
                    $"{zeroAreaIds.Count} placed room(s) have zero area after regeneration: not enclosed by walls or separation lines, or redundant with another room in the same region (Revit shows 'Not Enclosed' / 'Redundant Room').",
                    zeroAreaIds);
            }

            if (levelsWithoutPlanView.Count > 0)
            {
                report.AddFallback(Id,
                    $"No plan view exists for level index(es) {string.Join(", ", levelsWithoutPlanView)}; {linesSkippedNoView} room separation line(s) and {tagsSkippedNoView} room tag(s) there were skipped.");
            }

            if (fakeTagsSkippedNoTextType > 0)
            {
                report.AddFallback(Id,
                    $"The document has no text note type; {fakeTagsSkippedNoTextType} fake room tag(s) were skipped and those rooms are untagged.");
            }

            report.AddInfo(Id,
                $"Rooms: {placedCount} placed, {unplacedCount} unplaced, {taggedCount} tagged, {fakeTaggedCount} fake-tagged with a text note, {linesCreated} separation line(s), {zeroAreaIds.Count} placed room(s) with zero area. " +
                "Duplicate room numbers and rooms sharing a region raise expected Revit warnings that are dismissed automatically and listed under expected warnings.");
        }

        // ---- Helpers -----------------------------------------------------------------------

        /// <summary>
        /// The separation lines for one level, in spec order (null where creation failed). One
        /// call for the whole level keeps a single sketch plane; if that call fails and there
        /// are several segments, each is retried on its own so one bad segment does not cost
        /// the level all its lines.
        /// </summary>
        private ModelCurve[] CreateSeparationLines(GenerationContext context, IReadOnlyList<SeparationLineSpec> specs, int levelIndex, Level level, ViewPlan planView)
        {
            var report = context.Report;
            var factory = context.Factory;
            var lines = new ModelCurve[specs.Count];

            try
            {
                var created = factory.CreateRoomSeparationLines(planView, level, specs.Select(s => s.Line));
                for (var i = 0; i < specs.Count && i < created.Count; i++) lines[i] = created[i];
                return lines;
            }
            catch (Exception ex)
            {
                report.AddException(Id, $"create {specs.Count} room separation line(s) on level index {levelIndex}", ex, rolledBack: false);
            }

            if (specs.Count <= 1) return lines;

            for (var i = 0; i < specs.Count; i++)
            {
                try
                {
                    var created = factory.CreateRoomSeparationLines(planView, level, new[] { specs[i].Line });
                    if (created.Count > 0) lines[i] = created[0];
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create room separation line {specs[i].Index} ({specs[i].Line})", ex, rolledBack: false);
                }
            }
            return lines;
        }

        /// <summary>A placed or unplaced room for the spec, or null after recording why it could not be made.</summary>
        private Room CreateRoomElement(GenerationContext context, RoomSpec spec)
        {
            var report = context.Report;
            var factory = context.Factory;
            Room room = null;

            if (spec.IsPlaced)
            {
                var level = context.LevelFor(spec.LevelIndex);
                if (level == null)
                {
                    report.AddException(Id, $"create room {spec.Number} '{spec.Name}'", new InvalidOperationException($"Level index {spec.LevelIndex} was not created."), rolledBack: false);
                    return null;
                }
                var operation = $"create room {spec.Number} '{spec.Name}' at {spec.Location.Value} on level index {spec.LevelIndex}";
                try
                {
                    room = factory.CreateRoom(level, UnitConversion.ToUV(spec.Location.Value));
                }
                catch (Exception ex)
                {
                    report.AddException(Id, operation, ex, rolledBack: false);
                    return null;
                }
                if (room == null)
                {
                    report.AddException(Id, operation, new InvalidOperationException("Revit returned no room."), rolledBack: false);
                    return null;
                }
            }
            else
            {
                var operation = $"create unplaced room {spec.Number} '{spec.Name}'";
                try
                {
                    room = factory.CreateUnplacedRoom(null);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, operation, ex, rolledBack: false);
                    return null;
                }
                if (room == null)
                {
                    report.AddException(Id, operation, new InvalidOperationException("Revit returned no room."), rolledBack: false);
                    return null;
                }
            }

            return room;
        }

        /// <summary>
        /// The fake tag from issue #1: one text note with the room's name over its number, boxed
        /// by four detail lines so it reads as a room tag from across the office. The box is
        /// sized by eye from the character count and the view scale, so it never quite fits the
        /// text — faithful to how these get drawn. Returns the ids of whatever was created.
        /// </summary>
        private List<long> CreateFakeTag(GenerationContext context, ViewPlan planView, RoomSpec spec, Point2D tagPoint)
        {
            var factory = context.Factory;
            var ids = new List<long>();

            // Detail curves must lie in the view's plane; the view origin is on it by definition.
            var zFeet = planView.Origin.Z;
            var note = factory.CreateTextNote(planView, UnitConversion.ToXYZAtFeet(tagPoint, zFeet), $"{spec.Name}\n{spec.Number}");
            if (note == null) return ids;
            ids.Add(note.Id.Value);

            var scale = Math.Max(1, planView.Scale);
            var chars = Math.Max(spec.Name?.Length ?? 1, spec.Number?.Length ?? 1);
            var halfWidthMm = Math.Max(300.0, (chars + 1) * scale);
            var halfHeightMm = 4.5 * scale;
            // The note's insertion point is its top-left corner, so the box trails right and down.
            var centre = tagPoint.Offset(halfWidthMm * 0.9, -halfHeightMm * 0.9);

            var corners = new[]
            {
                new Point2D(centre.X - halfWidthMm, centre.Y - halfHeightMm),
                new Point2D(centre.X + halfWidthMm, centre.Y - halfHeightMm),
                new Point2D(centre.X + halfWidthMm, centre.Y + halfHeightMm),
                new Point2D(centre.X - halfWidthMm, centre.Y + halfHeightMm),
            };
            for (var i = 0; i < corners.Length; i++)
            {
                var line = Line.CreateBound(
                    UnitConversion.ToXYZAtFeet(corners[i], zFeet),
                    UnitConversion.ToXYZAtFeet(corners[(i + 1) % corners.Length], zFeet));
                var curve = factory.CreateDetailLine(planView, line);
                if (curve != null) ids.Add(curve.Id.Value);
            }
            return ids;
        }

        /// <summary>
        /// Name and number through the parameters (any string is legal there, so the bad values
        /// survive), falling back to the properties when a parameter refuses.
        /// </summary>
        private void ApplyNameAndNumber(GenerationContext context, Room room, RoomSpec spec)
        {
            var report = context.Report;
            var factory = context.Factory;

            if (spec.Name != null && !factory.TrySet(room, BuiltInParameter.ROOM_NAME, spec.Name))
            {
                try { room.Name = spec.Name; }
                catch (Exception ex) { report.AddException(Id, $"set room name '{spec.Name}'", ex, rolledBack: false); }
            }

            if (spec.Number != null && !factory.TrySet(room, BuiltInParameter.ROOM_NUMBER, spec.Number))
            {
                try { room.Number = spec.Number; }
                catch (Exception ex) { report.AddException(Id, $"set room number '{spec.Number}'", ex, rolledBack: false); }
            }
        }

        private void AddSummaryDefect(GenerationReport report, string message, List<long> ids)
        {
            if (ids == null || ids.Count == 0) return;
            report.AddDefect(Id, $"{message} ({ids.Count}).", ids);
        }
    }
}
