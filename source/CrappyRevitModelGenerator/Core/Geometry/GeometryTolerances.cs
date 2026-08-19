using System;
using System.Collections.Generic;
using System.Linq;

namespace CrappyRevitModelGenerator.Core.Geometry
{
    /// <summary>
    /// Every tolerance the planners use, in one object (plan section 9.2). Values are
    /// millimetres. Revit's own short-curve tolerance is about 1.2 mm; ours are far larger
    /// because a 5 mm wall is "valid" and useless.
    /// </summary>
    public sealed class GeometryTolerances
    {
        public static readonly GeometryTolerances Default = new GeometryTolerances();

        /// <summary>Shortest wall the planner will emit. Below this a wall is a sliver, not a defect.</summary>
        public double MinWallLengthMm { get; set; } = 400;

        /// <summary>Shortest curve of any kind (floor edges, separation lines, grids).</summary>
        public double MinCurveLengthMm { get; set; } = 100;

        /// <summary>Two points closer than this are treated as the same point.</summary>
        public double CoincidentMm { get; set; } = 1.0;

        /// <summary>Two parallel lines closer than this are "nearly coincident" — allowed for datum defects, but never closer.</summary>
        public double MinNearCoincidentGapMm { get; set; } = 60;

        /// <summary>Largest deliberate wall-corner gap. Big enough to see, small enough to look like a mistake.</summary>
        public double MaxCornerGapMm { get; set; } = 60;
        public double MinCornerGapMm { get; set; } = 15;

        /// <summary>Largest deliberate misalignment of a wall from its intended line.</summary>
        public double MaxWallMisalignmentMm { get; set; } = 45;
        public double MinWallMisalignmentMm { get; set; } = 12;

        /// <summary>Largest deliberate level-elevation wobble.</summary>
        public double MaxLevelJitterMm { get; set; } = 40;

        /// <summary>Angle tolerance for parallel/perpendicular tests (radians).</summary>
        public double AngleToleranceRad { get; set; } = 1e-6;
    }

    /// <summary>Validity checks the planners run before anything reaches the Revit API.</summary>
    public static class CurveValidation
    {
        public static bool IsValidSegment(Segment2D segment, double minLengthMm)
        {
            var len = segment.Length;
            return !double.IsNaN(len) && !double.IsInfinity(len) && len >= minLengthMm
                   && IsFinite(segment.Start) && IsFinite(segment.End);
        }

        public static bool IsFinite(Point2D p) =>
            !double.IsNaN(p.X) && !double.IsNaN(p.Y) && !double.IsInfinity(p.X) && !double.IsInfinity(p.Y);

        /// <summary>
        /// True when <paramref name="loop"/> (implicitly closed) is a simple polygon: at least
        /// three distinct vertices, no zero-length edge, no repeated vertex, and no two
        /// non-adjacent edges intersect. This is what <c>Floor.Create</c> and room-separation
        /// sketches require.
        /// </summary>
        public static bool IsSimpleClosedLoop(IReadOnlyList<Point2D> loop, GeometryTolerances tolerances = null)
        {
            tolerances ??= GeometryTolerances.Default;
            if (loop == null || loop.Count < 3) return false;
            if (loop.Any(p => !IsFinite(p))) return false;

            var n = loop.Count;
            var edges = new List<Segment2D>(n);
            for (var i = 0; i < n; i++)
            {
                var edge = new Segment2D(loop[i], loop[(i + 1) % n]);
                if (edge.Length < tolerances.MinCurveLengthMm) return false;
                edges.Add(edge);
            }

            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                if (loop[i].AlmostEquals(loop[j], tolerances.CoincidentMm)) return false;
            }

            for (var i = 0; i < n; i++)
            for (var j = i + 1; j < n; j++)
            {
                var adjacent = j == i + 1 || (i == 0 && j == n - 1);
                if (adjacent) continue;
                if (edges[i].Intersects(edges[j], ignoreTouchingEnds: false, tolerances.CoincidentMm)) return false;
            }

            return true;
        }

        /// <summary>Signed area; positive when counter-clockwise.</summary>
        public static double SignedArea(IReadOnlyList<Point2D> loop)
        {
            if (loop == null || loop.Count < 3) return 0;
            double sum = 0;
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }
    }
}
