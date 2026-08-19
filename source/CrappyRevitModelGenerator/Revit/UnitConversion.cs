using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core.Geometry;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// The one place millimetres become Revit internal feet (plan section 9.2). Everything in
    /// Core is millimetres; everything handed to the Revit API goes through here.
    /// </summary>
    public static class UnitConversion
    {
        public static double MmToFeet(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);

        public static double FeetToMm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);

        public static XYZ ToXYZ(Point2D p, double elevationMm = 0) =>
            new XYZ(MmToFeet(p.X), MmToFeet(p.Y), MmToFeet(elevationMm));

        /// <summary>A point at an explicit internal-unit Z (e.g. a Level's Elevation).</summary>
        public static XYZ ToXYZAtFeet(Point2D p, double zFeet) =>
            new XYZ(MmToFeet(p.X), MmToFeet(p.Y), zFeet);

        public static UV ToUV(Point2D p) => new UV(MmToFeet(p.X), MmToFeet(p.Y));

        public static Line ToLine(Segment2D s, double elevationMm = 0) =>
            Line.CreateBound(ToXYZ(s.Start, elevationMm), ToXYZ(s.End, elevationMm));

        public static Line ToLineAtFeet(Segment2D s, double zFeet) =>
            Line.CreateBound(ToXYZAtFeet(s.Start, zFeet), ToXYZAtFeet(s.End, zFeet));

        /// <summary>A closed CurveLoop from plan points, at an explicit internal-unit Z.</summary>
        public static CurveLoop ToCurveLoopAtFeet(IReadOnlyList<Point2D> loop, double zFeet)
        {
            var curves = new List<Curve>(loop.Count);
            for (var i = 0; i < loop.Count; i++)
            {
                var a = loop[i];
                var b = loop[(i + 1) % loop.Count];
                curves.Add(Line.CreateBound(ToXYZAtFeet(a, zFeet), ToXYZAtFeet(b, zFeet)));
            }
            return CurveLoop.Create(curves);
        }

        public static Point2D ToPoint2D(XYZ p) => new Point2D(FeetToMm(p.X), FeetToMm(p.Y));

        public static double DegreesToRadians(double degrees) => degrees * System.Math.PI / 180.0;
    }
}
