using System;
using System.Globalization;

namespace CrappyRevitModelGenerator.Core.Geometry
{
    /// <summary>A straight segment in plan, millimetres. Immutable; Revit-free.</summary>
    public readonly struct Segment2D : IEquatable<Segment2D>
    {
        public Segment2D(Point2D start, Point2D end)
        {
            Start = start;
            End = end;
        }

        public Segment2D(double x1, double y1, double x2, double y2)
            : this(new Point2D(x1, y1), new Point2D(x2, y2))
        {
        }

        public Point2D Start { get; }
        public Point2D End { get; }

        public double Length => Start.DistanceTo(End);

        /// <summary>Unit vector from Start to End (zero vector for a degenerate segment).</summary>
        public Point2D Direction => End.Minus(Start).Normalized();

        /// <summary>Unit normal to the left of the direction of travel.</summary>
        public Point2D LeftNormal => Direction.PerpendicularLeft();

        public Point2D Midpoint => Point2D.Lerp(Start, End, 0.5);

        public bool IsHorizontal(double toleranceMm = 0.01) => Math.Abs(Start.Y - End.Y) <= toleranceMm;
        public bool IsVertical(double toleranceMm = 0.01) => Math.Abs(Start.X - End.X) <= toleranceMm;

        /// <summary>Point at parameter t (0 = Start, 1 = End). Not clamped.</summary>
        public Point2D PointAt(double t) => Point2D.Lerp(Start, End, t);

        /// <summary>Point at a distance from Start along the segment. Not clamped.</summary>
        public Point2D PointAtDistance(double distanceMm)
        {
            var len = Length;
            return len < 1e-9 ? Start : PointAt(distanceMm / len);
        }

        /// <summary>The same segment shifted sideways; positive = to the left of travel.</summary>
        public Segment2D OffsetLeft(double distanceMm)
        {
            var shift = LeftNormal.Scale(distanceMm);
            return new Segment2D(Start.Plus(shift), End.Plus(shift));
        }

        /// <summary>Lengthened (positive) or shortened (negative) at each end.</summary>
        public Segment2D Extend(double atStartMm, double atEndMm)
        {
            var d = Direction;
            return new Segment2D(Start.Minus(d.Scale(atStartMm)), End.Plus(d.Scale(atEndMm)));
        }

        public Segment2D Reversed() => new Segment2D(End, Start);

        /// <summary>The parameter t of the closest point on the infinite line through this segment.</summary>
        public double ProjectParameter(Point2D point)
        {
            var d = End.Minus(Start);
            var lenSq = d.Dot(d);
            return lenSq < 1e-12 ? 0 : point.Minus(Start).Dot(d) / lenSq;
        }

        public double DistanceTo(Point2D point)
        {
            var t = Math.Max(0, Math.Min(1, ProjectParameter(point)));
            return PointAt(t).DistanceTo(point);
        }

        /// <summary>
        /// True when the two segments (excluding shared endpoints when <paramref name="ignoreTouchingEnds"/>)
        /// cross. Uses orientation tests; collinear overlaps count as intersecting.
        /// </summary>
        public bool Intersects(Segment2D other, bool ignoreTouchingEnds, double toleranceMm = 0.01)
        {
            if (ignoreTouchingEnds)
            {
                if (Start.AlmostEquals(other.Start, toleranceMm) || Start.AlmostEquals(other.End, toleranceMm) ||
                    End.AlmostEquals(other.Start, toleranceMm) || End.AlmostEquals(other.End, toleranceMm))
                    return false;
            }

            var d1 = Orientation(other.Start, other.End, Start);
            var d2 = Orientation(other.Start, other.End, End);
            var d3 = Orientation(Start, End, other.Start);
            var d4 = Orientation(Start, End, other.End);

            if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
                return true;

            if (Math.Abs(d1) < 1e-9 && OnSegment(other, Start)) return true;
            if (Math.Abs(d2) < 1e-9 && OnSegment(other, End)) return true;
            if (Math.Abs(d3) < 1e-9 && OnSegment(this, other.Start)) return true;
            if (Math.Abs(d4) < 1e-9 && OnSegment(this, other.End)) return true;

            return false;
        }

        private static double Orientation(Point2D a, Point2D b, Point2D c) => b.Minus(a).Cross(c.Minus(a));

        private static bool OnSegment(Segment2D s, Point2D p) =>
            p.X <= Math.Max(s.Start.X, s.End.X) + 1e-9 && p.X >= Math.Min(s.Start.X, s.End.X) - 1e-9 &&
            p.Y <= Math.Max(s.Start.Y, s.End.Y) + 1e-9 && p.Y >= Math.Min(s.Start.Y, s.End.Y) - 1e-9;

        public bool Equals(Segment2D other) => Start.Equals(other.Start) && End.Equals(other.End);
        public override bool Equals(object obj) => obj is Segment2D other && Equals(other);
        public override int GetHashCode() => unchecked((Start.GetHashCode() * 397) ^ End.GetHashCode());

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "{0} -> {1} ({2:0.#} mm)", Start, End, Length);
    }
}
