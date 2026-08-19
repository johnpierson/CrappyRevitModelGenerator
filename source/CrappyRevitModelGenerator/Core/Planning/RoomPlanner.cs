using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrappyRevitModelGenerator.Core.Geometry;

namespace CrappyRevitModelGenerator.Core.Planning
{
    /// <summary>
    /// Decides which cells get rooms, which rooms stay unplaced, where room separation lines go
    /// and which rooms get (awkward) tags — plan section 7.6. Names and numbers come from
    /// <see cref="BadNames"/> when the naming scenario is on, otherwise from a clean sequence,
    /// so the rooms scenario can run with or without the naming scenario.
    /// </summary>
    public static class RoomPlanner
    {
        public const string StreamRooms = "rooms/rooms";
        public const string StreamTags = "rooms/tags";
        public const string StreamSeparation = "rooms/separation";

        public static RoomPlan Plan(BaselinePlan baseline, GenerationSettings settings, SeededRandom random, bool badNaming)
        {
            if (baseline == null) throw new ArgumentNullException(nameof(baseline));
            if (settings == null) throw new ArgumentNullException(nameof(settings));
            if (random == null) throw new ArgumentNullException(nameof(random));

            var plan = new RoomPlan();
            if (!settings.CreateRooms) return plan;

            var profile = SeverityProfile.For(settings.Severity);
            var rnd = random.Stream(StreamRooms);
            var tagRnd = random.Stream(StreamTags);

            var target = Math.Min(GenerationLimits.MaxRooms, rnd.NextIntInclusive(profile.RoomsMin, profile.RoomsMax));

            // Lowest levels first, front cells before back cells, so the model reads bottom-up.
            var candidates = baseline.Cells
                .Where(c => c.Band != CellBand.Corridor)
                .OrderBy(c => c.LevelIndex).ThenBy(c => c.Band).ThenBy(c => c.Bounds.MinX)
                .ToList();

            var chosen = candidates.Take(target).ToList();
            var usedNumbers = new List<string>();
            var cleanNumber = 101;

            foreach (var cell in chosen)
            {
                var room = new RoomSpec
                {
                    LevelIndex = cell.LevelIndex,
                    CellIndex = cell.Index,
                    Location = cell.Bounds.Center,
                };
                AssignName(room, rnd, badNaming, cell.LevelIndex, ref cleanNumber, usedNumbers);
                if (cell.EnclosureBroken)
                {
                    room.DefectTags.Add("in-broken-enclosure");
                    plan.Defects.Add(new PlannedDefect(ScenarioIds.Rooms,
                        $"Room {room.Number} '{room.Name}' is placed in a cell whose partition has a gap, so it shares its region with a neighbour (partially bounded area)."));
                }
                plan.Rooms.Add(room);
            }

            // A second room in a cell that already has one.
            for (var i = 0; i < profile.DuplicateRoomsInCell && chosen.Count > 0; i++)
            {
                var cell = rnd.Pick(chosen);
                var dup = new RoomSpec
                {
                    LevelIndex = cell.LevelIndex,
                    CellIndex = cell.Index,
                    Location = cell.Bounds.Center.Offset(Math.Round(rnd.NextJitter(Math.Max(100, cell.Bounds.Width / 5))), Math.Round(rnd.NextJitter(Math.Max(100, cell.Bounds.Depth / 5)))),
                };
                AssignName(dup, rnd, badNaming, cell.LevelIndex, ref cleanNumber, usedNumbers);
                dup.DefectTags.Add("duplicate-in-region");
                plan.Rooms.Add(dup);
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Rooms,
                    $"Room {dup.Number} '{dup.Name}' is a second room in the same enclosed region as another (Revit will warn: multiple rooms in the same region)."));
            }

            // A room in the corridor, if there is one.
            var corridor = baseline.Cells.FirstOrDefault(c => c.Band == CellBand.Corridor);
            if (profile.RoomInCorridor && corridor != null)
            {
                var room = new RoomSpec
                {
                    LevelIndex = corridor.LevelIndex,
                    CellIndex = corridor.Index,
                    Location = corridor.Bounds.Center,
                    Name = badNaming ? rnd.Pick(new[] { "Corridoor", "Corridor", "Hall", "hallway", "CORR." }) : "Corridor",
                };
                room.Number = NextNumber(rnd, badNaming, corridor.LevelIndex, ref cleanNumber, usedNumbers);
                plan.Rooms.Add(room);
            }

            // Unplaced rooms with valid but confusing numbers.
            for (var i = 0; i < profile.UnplacedRooms; i++)
            {
                var room = new RoomSpec
                {
                    LevelIndex = -1,
                    Location = null,
                    CreateTag = false,
                };
                AssignName(room, rnd, badNaming, 0, ref cleanNumber, usedNumbers);
                room.DefectTags.Add("unplaced");
                plan.Rooms.Add(room);
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Rooms,
                    $"Room {room.Number} '{room.Name}' exists but is not placed (shows in schedules as 'Not Placed')."));
            }

            // Tags: some omitted, some awkward.
            var placed = plan.Rooms.Where(r => r.IsPlaced).ToList();
            var untagged = tagRnd.TakeDistinct(placed, (int)Math.Round(placed.Count * profile.UntaggedRoomFraction));
            foreach (var r in untagged)
            {
                r.CreateTag = false;
                r.DefectTags.Add("untagged");
            }
            if (untagged.Count > 0)
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Rooms, $"{untagged.Count} placed room(s) have no room tag."));

            var tagged = placed.Where(r => r.CreateTag).ToList();
            var awkward = tagRnd.TakeDistinct(tagged, (int)Math.Round(tagged.Count * profile.AwkwardTagFraction));
            foreach (var r in awkward)
            {
                var cell = baseline.Cells.FirstOrDefault(c => c.Index == r.CellIndex);
                var bounds = cell?.Bounds ?? Rect2D.FromCenter(r.Location.Value, 3000, 3000);
                // Push the tag almost onto a wall, still inside the room. Offsets are measured
                // from the room's ACTUAL location — a duplicate room sits off the cell centre,
                // and a half-cell offset from there would land the tag outside the room.
                var loc = r.Location.Value;
                var targetX = tagRnd.NextBool() ? bounds.MaxX - 150 : bounds.MinX + 150;
                var targetY = tagRnd.NextBool() ? bounds.MaxY - 150 : bounds.MinY + 150;
                r.TagOffsetMm = new Point2D(Math.Round(targetX - loc.X), Math.Round(targetY - loc.Y));
                r.DefectTags.Add("awkward-tag");
            }
            if (awkward.Count > 0)
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Rooms, $"{awkward.Count} room tag(s) are placed awkwardly against a wall instead of at the room centre."));

            PlanSeparationLines(baseline, plan, random.Stream(StreamSeparation), profile);

            for (var i = 0; i < plan.Rooms.Count; i++) plan.Rooms[i].Index = i;
            for (var i = 0; i < plan.SeparationLines.Count; i++) plan.SeparationLines[i].Index = i;
            return plan;
        }

        private static void PlanSeparationLines(BaselinePlan baseline, RoomPlan plan, RandomStream rnd, SeverityProfile profile)
        {
            var count = profile.SeparationLines;
            if (count <= 0) return;

            var corridors = baseline.Cells.Where(c => c.Band == CellBand.Corridor).ToList();
            var cells = baseline.Cells.Where(c => c.Band != CellBand.Corridor).ToList();

            // 1) Across the corridor: a legitimate use.
            if (corridors.Count > 0 && count > 0)
            {
                var corridor = rnd.Pick(corridors);
                var x = Math.Round(rnd.NextDouble(corridor.Bounds.MinX + 1500, corridor.Bounds.MaxX - 1500));
                var line = new Segment2D(x, corridor.Bounds.MinY, x, corridor.Bounds.MaxY);
                if (CurveValidation.IsValidSegment(line, GeometryTolerances.Default.MinCurveLengthMm))
                {
                    plan.SeparationLines.Add(new SeparationLineSpec { LevelIndex = corridor.LevelIndex, Line = line });
                    count--;
                }
            }

            // 2) Splitting a cell in half where a wall would have been the honest choice.
            foreach (var cell in rnd.TakeDistinct(cells, count))
            {
                var b = cell.Bounds;
                if (b.Width < 2400) continue;
                var x = Math.Round(b.Center.X + rnd.NextJitter(b.Width * 0.15));
                var line = new Segment2D(x, b.MinY, x, b.MaxY);
                if (!CurveValidation.IsValidSegment(line, GeometryTolerances.Default.MinCurveLengthMm)) continue;
                var spec = new SeparationLineSpec { LevelIndex = cell.LevelIndex, Line = line };
                spec.DefectTags.Add("wall-would-do");
                plan.SeparationLines.Add(spec);
                plan.Defects.Add(new PlannedDefect(ScenarioIds.Rooms,
                    $"A room separation line splits the cell at {b.Center} on level {cell.LevelIndex + 1} where a wall would have been sufficient."));
            }
        }

        private static void AssignName(RoomSpec room, RandomStream rnd, bool badNaming, int levelIndex, ref int cleanNumber, List<string> usedNumbers)
        {
            if (badNaming)
            {
                room.Name = rnd.Pick(BadNames.RoomNames);
            }
            else
            {
                room.Name = rnd.Pick(new[] { "Office", "Meeting Room", "Storage", "Open Office", "Break Room", "Copy Room" });
            }
            room.Number = NextNumber(rnd, badNaming, levelIndex, ref cleanNumber, usedNumbers);
        }

        private static string NextNumber(RandomStream rnd, bool badNaming, int levelIndex, ref int cleanNumber, List<string> usedNumbers)
        {
            string number;
            if (badNaming)
            {
                // Duplicate-looking patterns are the point; exact duplicates are allowed too
                // (Revit warns about duplicate room numbers but accepts them).
                number = rnd.Pick(BadNames.RoomNumbers);
            }
            else
            {
                number = ((Math.Max(0, levelIndex) + 1) * 100 + (cleanNumber % 100)).ToString(CultureInfo.InvariantCulture);
                cleanNumber++;
            }
            usedNumbers.Add(number);
            return number;
        }
    }
}
