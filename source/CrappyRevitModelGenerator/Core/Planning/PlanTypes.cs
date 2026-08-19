using System.Collections.Generic;
using CrappyRevitModelGenerator.Core.Geometry;

namespace CrappyRevitModelGenerator.Core.Planning
{
    /// <summary>
    /// A defect a planner decided to plant, before any element exists. The Revit-side scenario
    /// copies these into the report once it knows the element ids.
    /// </summary>
    public sealed class PlannedDefect
    {
        public PlannedDefect(string scenarioId, string message)
        {
            ScenarioId = scenarioId;
            Message = message;
        }

        public string ScenarioId { get; }
        public string Message { get; }

        /// <summary>Optional: indices into the plan list the defect concerns (e.g. wall indices).</summary>
        public List<int> RelatedIndices { get; } = new List<int>();

        public override string ToString() => $"[{ScenarioId}] {Message}";
    }

    public sealed class LevelSpec
    {
        public int Index { get; set; }

        /// <summary>The name used when the naming scenario is off ("Level 01"). Naming renames it later.</summary>
        public string CleanName { get; set; }

        public double ElevationMm { get; set; }

        /// <summary>An unnecessary intermediate level ("Mezz") planted by the datum defects.</summary>
        public bool IsIntermediate { get; set; }

        /// <summary>Whether this level gets a floor and walls (intermediate levels do not).</summary>
        public bool IsBuildable => !IsIntermediate;

        public override string ToString() => $"{CleanName} @ {ElevationMm:0} mm";
    }

    public sealed class GridSpec
    {
        public int Index { get; set; }
        public string CleanName { get; set; }
        public Segment2D Line { get; set; }
        public bool IsVertical { get; set; }
        public bool BubbleAtStart { get; set; } = true;
        public bool BubbleAtEnd { get; set; } = true;
        public List<string> DefectTags { get; } = new List<string>();

        public override string ToString() => $"Grid {CleanName}: {Line}";
    }

    public enum WallRole
    {
        Exterior,
        Corridor,
        Partition,
        Stub,
    }

    public sealed class WallSpec
    {
        public int Index { get; set; }
        public int LevelIndex { get; set; }
        public Segment2D Line { get; set; }
        public WallRole Role { get; set; }

        /// <summary>0 = the primary wall type for this role, 1 = the alternate ("a different thickness without a visible reason").</summary>
        public int TypeChoice { get; set; }

        /// <summary>Unconnected height. Used when <see cref="AttachTopToLevelAbove"/> is false or there is no level above.</summary>
        public double HeightMm { get; set; }

        public bool AttachTopToLevelAbove { get; set; } = true;

        /// <summary>0 = leave Revit's default location line; 1..5 = a deliberately odd WALL_KEY_REF_PARAM value.</summary>
        public int LocationLineChoice { get; set; }

        public bool IsRoomBounding { get; set; } = true;

        /// <summary>Corner joins to disallow: bit 1 = at start, bit 2 = at end.</summary>
        public int DisallowJoinMask { get; set; }

        public List<string> DefectTags { get; } = new List<string>();

        public override string ToString() => $"Wall {Index} L{LevelIndex} {Role}: {Line}";
    }

    public sealed class FloorSpec
    {
        public int Index { get; set; }
        public int LevelIndex { get; set; }
        public List<Point2D> Loop { get; } = new List<Point2D>();
        public List<string> DefectTags { get; } = new List<string>();
    }

    public enum CellBand
    {
        Front,
        Corridor,
        Back,
    }

    /// <summary>A region enclosed by walls (in principle) — where rooms and furniture go.</summary>
    public sealed class RoomCell
    {
        public int Index { get; set; }
        public int LevelIndex { get; set; }
        public Rect2D Bounds { get; set; }
        public CellBand Band { get; set; }

        /// <summary>The corridor wall (by wall index) a door into this cell would sit in, or -1.</summary>
        public int DoorWallIndex { get; set; } = -1;

        /// <summary>True when a planted gap or missing wall means this cell merges with a neighbour.</summary>
        public bool EnclosureBroken { get; set; }

        public override string ToString() => $"Cell {Index} L{LevelIndex} {Band} {Bounds}";
    }

    /// <summary>Everything the baseline scenario creates, decided before Revit is touched.</summary>
    public sealed class BaselinePlan
    {
        public Rect2D Footprint { get; set; }
        public double LevelHeightMm { get; set; }
        public List<LevelSpec> Levels { get; } = new List<LevelSpec>();
        public List<GridSpec> Grids { get; } = new List<GridSpec>();
        public List<WallSpec> Walls { get; } = new List<WallSpec>();
        public List<FloorSpec> Floors { get; } = new List<FloorSpec>();
        public List<RoomCell> Cells { get; } = new List<RoomCell>();
        public List<PlannedDefect> Defects { get; } = new List<PlannedDefect>();

        public int ElementCount => Levels.Count + Grids.Count + Walls.Count + Floors.Count;
    }

    public enum OpeningKind
    {
        Door,
        Window,
    }

    public sealed class OpeningSpec
    {
        public int Index { get; set; }
        public int LevelIndex { get; set; }
        public int WallIndex { get; set; }
        public OpeningKind Kind { get; set; }

        /// <summary>Distance from the host wall's start point along its line.</summary>
        public double DistanceAlongMm { get; set; }

        /// <summary>Windows only.</summary>
        public double SillHeightMm { get; set; }

        public bool FlipHand { get; set; }
        public bool FlipFacing { get; set; }
        public List<string> DefectTags { get; } = new List<string>();

        public override string ToString() => $"{Kind} on wall {WallIndex} @ {DistanceAlongMm:0} mm";
    }

    public sealed class FurnitureSpec
    {
        public int Index { get; set; }
        public int LevelIndex { get; set; }
        public int CellIndex { get; set; } = -1;
        public Point2D Location { get; set; }
        public double RotationDegrees { get; set; }
        public List<string> DefectTags { get; } = new List<string>();
    }

    public sealed class ContentPlan
    {
        public List<OpeningSpec> Openings { get; } = new List<OpeningSpec>();
        public List<FurnitureSpec> Furniture { get; } = new List<FurnitureSpec>();
        public List<PlannedDefect> Defects { get; } = new List<PlannedDefect>();

        public int ElementCount => Openings.Count + Furniture.Count;
    }

    public sealed class RoomSpec
    {
        public int Index { get; set; }
        public int LevelIndex { get; set; }
        public int CellIndex { get; set; } = -1;

        /// <summary>Null = an unplaced room.</summary>
        public Point2D? Location { get; set; }

        public string Name { get; set; }
        public string Number { get; set; }
        public bool CreateTag { get; set; } = true;

        /// <summary>Tag position relative to the room location; awkward offsets are a planted defect.</summary>
        public Point2D TagOffsetMm { get; set; }

        public List<string> DefectTags { get; } = new List<string>();

        public bool IsPlaced => Location.HasValue;

        public override string ToString() => $"Room {Number} '{Name}' L{LevelIndex} {(IsPlaced ? Location.ToString() : "unplaced")}";
    }

    public sealed class SeparationLineSpec
    {
        public int Index { get; set; }
        public int LevelIndex { get; set; }
        public Segment2D Line { get; set; }
        public List<string> DefectTags { get; } = new List<string>();
    }

    public sealed class RoomPlan
    {
        public List<RoomSpec> Rooms { get; } = new List<RoomSpec>();
        public List<SeparationLineSpec> SeparationLines { get; } = new List<SeparationLineSpec>();
        public List<PlannedDefect> Defects { get; } = new List<PlannedDefect>();

        public int ElementCount => Rooms.Count + SeparationLines.Count + TagCount;
        public int TagCount
        {
            get
            {
                var n = 0;
                foreach (var r in Rooms) if (r.IsPlaced && r.CreateTag) n++;
                return n;
            }
        }
    }
}
