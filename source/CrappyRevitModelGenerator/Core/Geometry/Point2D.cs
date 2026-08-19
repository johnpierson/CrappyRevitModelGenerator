using System;
using System.Globalization;

namespace CrappyRevitModelGenerator.Core.Geometry
{
    /// <summary>A point or vector in plan, in millimetres. Immutable; Revit-free.</summary>
    public readonly struct Point2D : IEquatable<Point2D>
    {
        public Point2D(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public static readonly Point2D Origin = new Point2D(0, 0);

        public double Length => Math.Sqrt(X * X + Y * Y);

        public Point2D Plus(Point2D other) => new Point2D(X + other.X, Y + other.Y);
        public Point2D Minus(Point2D other) => new Point2D(X - other.X, Y - other.Y);
        public Point2D Scale(double factor) => new Point2D(X * factor, Y * factor);
        public Point2D Offset(double dx, double dy) => new Point2D(X + dx, Y + dy);

        public double DistanceTo(Point2D other) => Minus(other).Length;

        public double Dot(Point2D other) => X * other.X + Y * other.Y;

        /// <summary>Z component of the 2D cross product; sign tells which side <paramref name="other"/> is on.</summary>
        public double Cross(Point2D other) => X * other.Y - Y * other.X;

        public Point2D Normalized()
        {
            var len = Length;
            return len < 1e-12 ? Origin : new Point2D(X / len, Y / len);
        }

        /// <summary>Rotated 90° counter-clockwise.</summary>
        public Point2D PerpendicularLeft() => new Point2D(-Y, X);

        public static Point2D Lerp(Point2D a, Point2D b, double t) => new Point2D(a.X + (b.X - a.X) * t, a.Y + (b.Y - a.Y) * t);

        public bool AlmostEquals(Point2D other, double toleranceMm) => DistanceTo(other) <= toleranceMm;

        public bool Equals(Point2D other) => X.Equals(other.X) && Y.Equals(other.Y);
        public override bool Equals(object obj) => obj is Point2D other && Equals(other);
        public override int GetHashCode() => unchecked((X.GetHashCode() * 397) ^ Y.GetHashCode());
        public static bool operator ==(Point2D a, Point2D b) => a.Equals(b);
        public static bool operator !=(Point2D a, Point2D b) => !a.Equals(b);

        public override string ToString() => string.Format(CultureInfo.InvariantCulture, "({0:0.#}, {1:0.#})", X, Y);
    }
}
