using System;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// Every quantity that makes Low, Medium and High "feel meaningfully different" (plan
    /// section 11), in one place. Planners and scenarios read from here instead of sprinkling
    /// magic numbers; tuning the feel of a severity is an edit to this file only.
    /// </summary>
    public sealed class SeverityProfile
    {
        public static SeverityProfile For(GenerationSeverity severity)
        {
            switch (severity)
            {
                case GenerationSeverity.Low: return Low;
                case GenerationSeverity.High: return High;
                default: return Medium;
            }
        }

        public GenerationSeverity Severity { get; private set; }

        // ---- Layout ----------------------------------------------------------------------
        public double CellWidthMm { get; private set; }
        public double CorridorWidthMinMm { get; private set; }
        public double CorridorWidthMaxMm { get; private set; }

        // ---- Datum defects (planted by the baseline planner when the datum scenario is on) ---
        public double LevelJitterMm { get; private set; }
        public double LevelOopsMinMm { get; private set; }
        public double LevelOopsMaxMm { get; private set; }
        public bool IntermediateLevel { get; private set; }
        public int MisalignedWallsPerLevel { get; private set; }
        public int CornerGapsPerLevel { get; private set; }
        public int StubWallsPerLevel { get; private set; }
        public int AlternateTypeWallsPerLevel { get; private set; }
        public int OddLocationLineWallsPerLevel { get; private set; }
        public int UnattachedWallsPerLevel { get; private set; }
        public int DisallowedJoinsPerLevel { get; private set; }
        public bool ExteriorOverrun { get; private set; }
        public bool FloorOffset { get; private set; }
        public bool FloorJog { get; private set; }
        public bool FloorInset { get; private set; }
        public bool GridExtentChaos { get; private set; }
        public int OneEndBubbleGrids { get; private set; }
        public int MisalignedGrids { get; private set; }
        public bool NearCoincidentGrid { get; private set; }

        // ---- Content placement ------------------------------------------------------------
        public double WindowSpacingMm { get; private set; }
        public int WindowPairsTooClose { get; private set; }
        public int DoorsNearWallEnd { get; private set; }
        public int SillHeightVarieties { get; private set; }
        public double DoorFlipProbability { get; private set; }
        public int FurniturePerCellMax { get; private set; }
        public int FurnitureOutsideFootprint { get; private set; }
        public int FurnitureRotatedOddly { get; private set; }
        public int FurnitureOnWall { get; private set; }

        // ---- Rooms ------------------------------------------------------------------------
        public int RoomsMin { get; private set; }
        public int RoomsMax { get; private set; }
        public int UnplacedRooms { get; private set; }
        public int DuplicateRoomsInCell { get; private set; }
        public int SeparationLines { get; private set; }
        public double UntaggedRoomFraction { get; private set; }
        public double AwkwardTagFraction { get; private set; }
        public bool RoomInCorridor { get; private set; }

        // ---- Documentation ----------------------------------------------------------------
        public int DuplicatePlansPerLevel { get; private set; }
        public int Sections { get; private set; }
        public int Elevations { get; private set; }
        public int ThreeDViews { get; private set; }
        public int DraftingViews { get; private set; }
        public int Sheets { get; private set; }
        public int EmptySheets { get; private set; }
        public int TextNotes { get; private set; }
        public double WrongDisciplineFraction { get; private set; }
        public double OddScaleFraction { get; private set; }
        public double OddCropFraction { get; private set; }

        // ---- Types, materials, metadata ---------------------------------------------------
        public int DuplicateWallTypes { get; private set; }
        public int DuplicateFloorTypes { get; private set; }
        public int DuplicateFamilyTypes { get; private set; }
        public int Materials { get; private set; }
        public int NearDuplicateMaterials { get; private set; }
        public double BlankMetadataFraction { get; private set; }
        public double BadMetadataFraction { get; private set; }
        public double DuplicateMarkFraction { get; private set; }

        // ---- Warnings scenario ------------------------------------------------------------
        public int OverlappingWalls { get; private set; }
        public int DuplicateInstances { get; private set; }
        public int OverlappingFloors { get; private set; }

        public static readonly SeverityProfile Low = new SeverityProfile
        {
            Severity = GenerationSeverity.Low,
            CellWidthMm = 4800, CorridorWidthMinMm = 1800, CorridorWidthMaxMm = 1800,
            LevelJitterMm = 12, LevelOopsMinMm = 0, LevelOopsMaxMm = 0, IntermediateLevel = false,
            MisalignedWallsPerLevel = 1, CornerGapsPerLevel = 0, StubWallsPerLevel = 0,
            AlternateTypeWallsPerLevel = 1, OddLocationLineWallsPerLevel = 0, UnattachedWallsPerLevel = 1,
            DisallowedJoinsPerLevel = 0, ExteriorOverrun = false,
            FloorOffset = false, FloorJog = false, FloorInset = true,
            GridExtentChaos = false, OneEndBubbleGrids = 1, MisalignedGrids = 0, NearCoincidentGrid = false,
            WindowSpacingMm = 5200, WindowPairsTooClose = 0, DoorsNearWallEnd = 1, SillHeightVarieties = 2, DoorFlipProbability = 0.2,
            FurniturePerCellMax = 1, FurnitureOutsideFootprint = 0, FurnitureRotatedOddly = 1, FurnitureOnWall = 0,
            RoomsMin = 4, RoomsMax = 6, UnplacedRooms = 1, DuplicateRoomsInCell = 0, SeparationLines = 1,
            UntaggedRoomFraction = 0.2, AwkwardTagFraction = 0.2, RoomInCorridor = false,
            DuplicatePlansPerLevel = 1, Sections = 1, Elevations = 1, ThreeDViews = 1, DraftingViews = 1,
            Sheets = 2, EmptySheets = 0, TextNotes = 3, WrongDisciplineFraction = 0.15, OddScaleFraction = 0.25, OddCropFraction = 0.25,
            DuplicateWallTypes = 1, DuplicateFloorTypes = 0, DuplicateFamilyTypes = 1, Materials = 3, NearDuplicateMaterials = 1,
            BlankMetadataFraction = 0.4, BadMetadataFraction = 0.3, DuplicateMarkFraction = 0.2,
            OverlappingWalls = 1, DuplicateInstances = 1, OverlappingFloors = 0,
        };

        public static readonly SeverityProfile Medium = new SeverityProfile
        {
            Severity = GenerationSeverity.Medium,
            CellWidthMm = 4200, CorridorWidthMinMm = 1650, CorridorWidthMaxMm = 1950,
            LevelJitterMm = 25, LevelOopsMinMm = 60, LevelOopsMaxMm = 120, IntermediateLevel = true,
            MisalignedWallsPerLevel = 2, CornerGapsPerLevel = 1, StubWallsPerLevel = 1,
            AlternateTypeWallsPerLevel = 2, OddLocationLineWallsPerLevel = 1, UnattachedWallsPerLevel = 2,
            DisallowedJoinsPerLevel = 1, ExteriorOverrun = false,
            FloorOffset = true, FloorJog = true, FloorInset = true,
            GridExtentChaos = true, OneEndBubbleGrids = 2, MisalignedGrids = 1, NearCoincidentGrid = true,
            WindowSpacingMm = 4600, WindowPairsTooClose = 1, DoorsNearWallEnd = 2, SillHeightVarieties = 3, DoorFlipProbability = 0.35,
            FurniturePerCellMax = 2, FurnitureOutsideFootprint = 1, FurnitureRotatedOddly = 1, FurnitureOnWall = 1,
            RoomsMin = 6, RoomsMax = 8, UnplacedRooms = 1, DuplicateRoomsInCell = 1, SeparationLines = 2,
            UntaggedRoomFraction = 0.25, AwkwardTagFraction = 0.3, RoomInCorridor = true,
            DuplicatePlansPerLevel = 1, Sections = 2, Elevations = 2, ThreeDViews = 2, DraftingViews = 1,
            Sheets = 3, EmptySheets = 1, TextNotes = 5, WrongDisciplineFraction = 0.25, OddScaleFraction = 0.35, OddCropFraction = 0.35,
            DuplicateWallTypes = 2, DuplicateFloorTypes = 1, DuplicateFamilyTypes = 2, Materials = 5, NearDuplicateMaterials = 2,
            BlankMetadataFraction = 0.35, BadMetadataFraction = 0.45, DuplicateMarkFraction = 0.3,
            OverlappingWalls = 2, DuplicateInstances = 2, OverlappingFloors = 1,
        };

        public static readonly SeverityProfile High = new SeverityProfile
        {
            Severity = GenerationSeverity.High,
            CellWidthMm = 3400, CorridorWidthMinMm = 1500, CorridorWidthMaxMm = 2100,
            LevelJitterMm = 40, LevelOopsMinMm = 100, LevelOopsMaxMm = 180, IntermediateLevel = true,
            MisalignedWallsPerLevel = 3, CornerGapsPerLevel = 2, StubWallsPerLevel = 2,
            AlternateTypeWallsPerLevel = 3, OddLocationLineWallsPerLevel = 2, UnattachedWallsPerLevel = 3,
            DisallowedJoinsPerLevel = 2, ExteriorOverrun = true,
            FloorOffset = true, FloorJog = true, FloorInset = true,
            GridExtentChaos = true, OneEndBubbleGrids = 3, MisalignedGrids = 2, NearCoincidentGrid = true,
            WindowSpacingMm = 3800, WindowPairsTooClose = 2, DoorsNearWallEnd = 3, SillHeightVarieties = 5, DoorFlipProbability = 0.5,
            FurniturePerCellMax = 3, FurnitureOutsideFootprint = 2, FurnitureRotatedOddly = 2, FurnitureOnWall = 2,
            RoomsMin = 8, RoomsMax = 10, UnplacedRooms = 2, DuplicateRoomsInCell = 2, SeparationLines = 3,
            UntaggedRoomFraction = 0.35, AwkwardTagFraction = 0.4, RoomInCorridor = true,
            DuplicatePlansPerLevel = 2, Sections = 3, Elevations = 3, ThreeDViews = 3, DraftingViews = 2,
            Sheets = 4, EmptySheets = 1, TextNotes = 8, WrongDisciplineFraction = 0.35, OddScaleFraction = 0.5, OddCropFraction = 0.5,
            DuplicateWallTypes = 3, DuplicateFloorTypes = 2, DuplicateFamilyTypes = 3, Materials = 7, NearDuplicateMaterials = 3,
            BlankMetadataFraction = 0.3, BadMetadataFraction = 0.6, DuplicateMarkFraction = 0.4,
            OverlappingWalls = 3, DuplicateInstances = 3, OverlappingFloors = 1,
        };

        private SeverityProfile()
        {
        }

        /// <summary>Scale an integer count by severity ordinal (0, 1, 2) with a floor of <paramref name="minimum"/>.</summary>
        public int Scaled(int low, int medium, int high, int minimum = 0)
        {
            var v = Severity == GenerationSeverity.Low ? low : Severity == GenerationSeverity.High ? high : medium;
            return Math.Max(minimum, v);
        }
    }
}
