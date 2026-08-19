using System;
using System.Collections.Generic;
using System.Globalization;

namespace CrappyRevitModelGenerator.Core.Geometry
{
    /// <summary>An axis-aligned rectangle in plan, millimetres. Immutable; Revit-free.</summary>
    public readonly struct Rect2D
    {
        public Rect2D(double minX, double minY, double maxX, double maxY)
        {
            MinX = Math.Min(minX, maxX);
            MinY = Math.Min(minY, maxY);
            MaxX = Math.Max(minX, maxX);
            MaxY = Math.Max(minY, maxY);
        }

        public static Rect2D FromCenter(Point2D center, double width, double depth) =>
            new Rect2D(center.X - width / 2, center.Y - depth / 2, center.X + width / 2, center.Y + depth / 2);

        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        public double Width => MaxX - MinX;
        public double Depth => MaxY - MinY;
        public double Area => Width * Depth;
        public Point2D Center => new Point2D((MinX + MaxX) / 2, (MinY + MaxY) / 2);
        public Point2D Min => new Point2D(MinX, MinY);
        public Point2D Max => new Point2D(MaxX, MaxY);

        /// <summary>Corners counter-clockwise starting at (MinX, MinY).</summary>
        public IReadOnlyList<Point2D> Corners => new[]
        {
            new Point2D(MinX, MinY),
            new Point2D(MaxX, MinY),
            new Point2D(MaxX, MaxY),
            new Point2D(MinX, MaxY),
        };

        /// <summary>Edges counter-clockwise: bottom, right, top, left.</summary>
        public IReadOnlyList<Segment2D> Edges
        {
            get
            {
                var c = Corners;
                return new[]
                {
                    new Segment2D(c[0], c[1]),
                    new Segment2D(c[1], c[2]),
                    new Segment2D(c[2], c[3]),
                    new Segment2D(c[3], c[0]),
                };
            }
        }

        public bool Contains(Point2D p, double toleranceMm = 0) =>
            p.X >= MinX - toleranceMm && p.X <= MaxX + toleranceMm &&
            p.Y >= MinY - toleranceMm && p.Y <= MaxY + toleranceMm;

        /// <summary>Grown (positive) or shrunk (negative) on every side.</summary>
        public Rect2D Inflate(double byMm) => new Rect2D(MinX - byMm, MinY - byMm, MaxX + byMm, MaxY + byMm);

        public Rect2D Translate(double dx, double dy) => new Rect2D(MinX + dx, MinY + dy, MaxX + dx, MaxY + dy);

        public override string ToString() =>
            string.Format(CultureInfo.InvariantCulture, "[{0:0.#},{1:0.#}]-[{2:0.#},{3:0.#}]", MinX, MinY, MaxX, MaxY);
    }
}
