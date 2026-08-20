using System;

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
        public const int MaxLevels = 20;
        public const int DefaultLevels = 3;

        // Footprint (millimetres)
        public const double MinFootprintMm = 6000;
        public const double MaxFootprintMm = 120000;
        public const double DefaultFootprintWidthMm = 18000;
        public const double DefaultFootprintDepthMm = 12000;

        // Level-to-level height (millimetres)
        public const double MinLevelHeightMm = 2400;
        public const double MaxLevelHeightMm = 6000;
        public const double DefaultLevelHeightMm = 3500;

        /// <summary>
        /// Multiplies the per-severity content quantities — rooms, views, sheets, text notes,
        /// duplicate types and materials — without changing which defects are planted. Severity
        /// says how bad the model is; scale says how big it is. 1.0 is the historical behaviour.
        /// </summary>
        public const double MinContentScale = 0.5;
        public const double MaxContentScale = 20.0;
        public const double DefaultContentScale = 1.0;

        // Per-category caps enforced by the planner and the scenarios
        public const int MaxWalls = 1200;
        public const int MaxRooms = 400;
        public const int MaxViews = 400;
        public const int MaxSheets = 100;
        public const int MaxMaterials = 60;
        public const int MaxDuplicateTypes = 60;

        // Total generated elements
        public const int DefaultMaxElements = 400;
        public const int HardMaxElements = 25_000;
        public const int MinMaxElements = 20;

        // Seed range shown to users; any int is valid, this only bounds the dialog spinner.
        public const int MinSeed = 0;
        public const int MaxSeed = 999_999;

        /// <summary>
        /// A max-element budget with headroom over an estimate, rounded to a readable number.
        /// The dialog uses this to keep the safety cap out of the way while the user is still
        /// dialling in size; it is never applied to a value the user typed themselves.
        /// </summary>
        public static int SuggestedMaxElements(int estimatedTotal)
        {
            var withHeadroom = (int)Math.Ceiling(Math.Max(0, estimatedTotal) * 1.25 / 50.0) * 50;
            return Math.Max(DefaultMaxElements, Math.Min(HardMaxElements, withHeadroom));
        }

        /// <summary>Clamp a content scale into the supported range; NaN falls back to the default.</summary>
        public static double ClampContentScale(double scale) =>
            double.IsNaN(scale) ? DefaultContentScale : Math.Max(MinContentScale, Math.Min(MaxContentScale, scale));
    }
}
