namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// Hard caps and defaults, in one place. Settings validation rejects anything above a hard
    /// cap before a transaction opens, so a careless setting cannot produce an unusably large
    /// document. The defaults are what the dialog shows on first open.
    /// </summary>
    public static class GenerationLimits
    {
        // Levels
        public const int MinLevels = 1;
        public const int MaxLevels = 6;
        public const int DefaultLevels = 3;

        // Footprint (millimetres)
        public const double MinFootprintMm = 6000;
        public const double MaxFootprintMm = 40000;
        public const double DefaultFootprintWidthMm = 18000;
        public const double DefaultFootprintDepthMm = 12000;

        // Level-to-level height (millimetres)
        public const double MinLevelHeightMm = 2400;
        public const double MaxLevelHeightMm = 6000;
        public const double DefaultLevelHeightMm = 3500;

        // Per-category caps enforced by the planner and the scenarios
        public const int MaxWalls = 120;
        public const int MaxRooms = 24;
        public const int MaxViews = 40;
        public const int MaxSheets = 10;
        public const int MaxMaterials = 12;
        public const int MaxDuplicateTypes = 12;

        // Total generated elements
        public const int DefaultMaxElements = 400;
        public const int HardMaxElements = 1500;
        public const int MinMaxElements = 20;

        // Seed range shown to users; any int is valid, this only bounds the dialog spinner.
        public const int MinSeed = 0;
        public const int MaxSeed = 999_999;
    }
}
