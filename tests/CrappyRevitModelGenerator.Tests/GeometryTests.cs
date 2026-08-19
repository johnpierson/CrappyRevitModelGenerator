using CrappyRevitModelGenerator.Core.Geometry;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class GeometryTests
    {
        private const int Precision = 9;

        // ---- Point2D -------------------------------------------------------------------------

        [Fact]
        public void PointArithmetic()
        {
            var a = new Point2D(1, 2);
            var b = new Point2D(4, 6);

            Assert.Equal(new Point2D(5, 8), a.Plus(b));
            Assert.Equal(new Point2D(-3, -4), a.Minus(b));
            Assert.Equal(new Point2D(2, 4), a.Scale(2));
            Assert.Equal(new Point2D(1.5, 1), a.Offset(0.5, -1));
            Assert.Equal(5, a.DistanceTo(b), Precision);
            Assert.Equal(5, b.DistanceTo(a), Precision);
            Assert.Equal(5, b.Minus(a).Length, Precision);
            Assert.Equal(16, a.Dot(b), Precision);
            Assert.Equal(-2, a.Cross(b), Precision);
            Assert.Equal(2, b.Cross(a), Precision);
            Assert.Equal(new Point2D(2.5, 4), Point2D.Lerp(a, b, 0.5));
            Assert.Equal(a, Point2D.Lerp(a, b, 0));
            Assert.Equal(b, Point2D.Lerp(a, b, 1));
        }

        [Fact]
        public void PointNormalizeAndPerpendicular()
        {
            var v = new Point2D(3, 4).Normalized();
            Assert.Equal(0.6, v.X, Precision);
            Assert.Equal(0.8, v.Y, Precision);
            Assert.Equal(1, v.Length, Precision);

            Assert.Equal(Point2D.Origin, Point2D.Origin.Normalized());
            Assert.Equal(Point2D.Origin, new Point2D(1e-13, 0).Normalized());

            Assert.Equal(new Point2D(0, 1), new Point2D(1, 0).PerpendicularLeft());
            Assert.Equal(new Point2D(-1, 0), new Point2D(0, 1).PerpendicularLeft());
            Assert.Equal(new Point2D(-4, 3), new Point2D(3, 4).PerpendicularLeft());
            // Left perpendicular is 90 degrees CCW: dot is 0 and cross is positive.
            var p = new Point2D(2, 5);
            Assert.Equal(0, p.Dot(p.PerpendicularLeft()), Precision);
            Assert.True(p.Cross(p.PerpendicularLeft()) > 0);
        }

        [Fact]
        public void PointEqualityAndAlmostEquals()
        {
            var a = new Point2D(1, 2);
            Assert.True(a == new Point2D(1, 2));
            Assert.True(a != new Point2D(1, 2.5));
            Assert.True(a.Equals((object)new Point2D(1, 2)));
            Assert.False(a.Equals(null));
            Assert.Equal(a.GetHashCode(), new Point2D(1, 2).GetHashCode());
            Assert.True(a.AlmostEquals(new Point2D(1.5, 2), 0.5));
            Assert.False(a.AlmostEquals(new Point2D(1.6, 2), 0.5));
            Assert.Equal("(1, 2.5)", new Point2D(1, 2.5).ToString());
            Assert.Equal("(-1234.6, 0)", new Point2D(-1234.56, 0).ToString());
        }

        // ---- Segment2D -----------------------------------------------------------------------

        [Fact]
        public void SegmentLengthDirectionAndMidpoint()
        {
            var s = new Segment2D(0, 0, 3000, 4000);
            Assert.Equal(5000, s.Length, Precision);
            Assert.Equal(new Point2D(0.6, 0.8), s.Direction);
            Assert.Equal(new Point2D(-0.8, 0.6), s.LeftNormal);
            Assert.Equal(new Point2D(1500, 2000), s.Midpoint);
            Assert.Equal(new Segment2D(3000, 4000, 0, 0), s.Reversed());
            Assert.Equal(new Point2D(3000, 4000), s.Reversed().Start);

            var degenerate = new Segment2D(1, 1, 1, 1);
            Assert.Equal(0, degenerate.Length);
            Assert.Equal(Point2D.Origin, degenerate.Direction);
            Assert.Equal(new Point2D(1, 1), degenerate.PointAtDistance(500));
        }

        [Fact]
        public void SegmentHorizontalVerticalTests()
        {
            Assert.True(new Segment2D(0, 5, 100, 5).IsHorizontal());
            Assert.True(new Segment2D(0, 5, 100, 5.005).IsHorizontal());
            Assert.False(new Segment2D(0, 5, 100, 5.5).IsHorizontal());
            Assert.True(new Segment2D(7, 0, 7, 100).IsVertical());
            Assert.False(new Segment2D(7, 0, 8, 100).IsVertical());
            Assert.False(new Segment2D(0, 0, 100, 100).IsHorizontal());
            Assert.False(new Segment2D(0, 0, 100, 100).IsVertical());
        }

        [Fact]
        public void SegmentPointAtAndPointAtDistance()
        {
            var s = new Segment2D(0, 0, 1000, 0);
            Assert.Equal(new Point2D(250, 0), s.PointAt(0.25));
            Assert.Equal(new Point2D(1500, 0), s.PointAt(1.5));
            Assert.Equal(new Point2D(700, 0), s.PointAtDistance(700));
            Assert.Equal(new Point2D(-100, 0), s.PointAtDistance(-100));

            var d = new Segment2D(0, 0, 3000, 4000);
            var p = d.PointAtDistance(2500);
            Assert.Equal(1500, p.X, Precision);
            Assert.Equal(2000, p.Y, Precision);
        }

        [Fact]
        public void SegmentOffsetLeftShiftsPerpendicular()
        {
            var alongX = new Segment2D(0, 0, 1000, 0).OffsetLeft(50);
            Assert.Equal(new Point2D(0, 50), alongX.Start);
            Assert.Equal(new Point2D(1000, 50), alongX.End);

            var alongY = new Segment2D(0, 0, 0, 1000).OffsetLeft(50);
            Assert.Equal(new Point2D(-50, 0), alongY.Start);
            Assert.Equal(new Point2D(-50, 1000), alongY.End);

            var negative = new Segment2D(0, 0, 1000, 0).OffsetLeft(-20);
            Assert.Equal(new Point2D(0, -20), negative.Start);
            Assert.Equal(1000, negative.Length, Precision);
        }

        [Fact]
        public void SegmentExtendLengthensOrShortensEachEnd()
        {
            var s = new Segment2D(0, 0, 1000, 0);
            var longer = s.Extend(100, 200);
            Assert.Equal(new Point2D(-100, 0), longer.Start);
            Assert.Equal(new Point2D(1200, 0), longer.End);
            Assert.Equal(1300, longer.Length, Precision);

            var shorter = s.Extend(0, -400);
            Assert.Equal(new Point2D(0, 0), shorter.Start);
            Assert.Equal(new Point2D(600, 0), shorter.End);

            var vertical = new Segment2D(5, 0, 5, 1000).Extend(-100, 0);
            Assert.Equal(new Point2D(5, 100), vertical.Start);
            Assert.Equal(new Point2D(5, 1000), vertical.End);
        }

        [Fact]
        public void SegmentProjectParameterAndDistanceTo()
        {
            var s = new Segment2D(0, 0, 1000, 0);
            Assert.Equal(0.5, s.ProjectParameter(new Point2D(500, 300)), Precision);
            Assert.Equal(0, s.ProjectParameter(new Point2D(0, -10)), Precision);
            Assert.Equal(1.5, s.ProjectParameter(new Point2D(1500, 0)), Precision);
            Assert.Equal(-0.25, s.ProjectParameter(new Point2D(-250, 0)), Precision);
            Assert.Equal(0, new Segment2D(1, 1, 1, 1).ProjectParameter(new Point2D(5, 5)));

            Assert.Equal(300, s.DistanceTo(new Point2D(500, 300)), Precision);
            Assert.Equal(0, s.DistanceTo(new Point2D(250, 0)), Precision);
            // Beyond the end: distance to the endpoint, not to the infinite line.
            Assert.Equal(500, s.DistanceTo(new Point2D(1300, 400)), Precision);
            Assert.Equal(100, s.DistanceTo(new Point2D(-100, 0)), Precision);
        }

        [Fact]
        public void SegmentIntersectsCrossing()
        {
            var a = new Segment2D(0, 0, 10, 10);
            var b = new Segment2D(0, 10, 10, 0);
            Assert.True(a.Intersects(b, ignoreTouchingEnds: false));
            Assert.True(a.Intersects(b, ignoreTouchingEnds: true));
            Assert.True(b.Intersects(a, ignoreTouchingEnds: true));
        }

        [Fact]
        public void SegmentIntersectsTouchingEndsDependsOnFlag()
        {
            var a = new Segment2D(0, 0, 10, 0);
            var b = new Segment2D(10, 0, 10, 10);
            Assert.True(a.Intersects(b, ignoreTouchingEnds: false));
            Assert.False(a.Intersects(b, ignoreTouchingEnds: true));
            Assert.False(b.Intersects(a, ignoreTouchingEnds: true));

            // Ends that nearly touch (within tolerance) are treated as touching.
            var c = new Segment2D(10.005, 0, 10.005, 10);
            Assert.False(a.Intersects(c, ignoreTouchingEnds: true, toleranceMm: 0.01));
        }

        [Fact]
        public void SegmentIntersectsTJunctionIsAnIntersectionEvenWhenIgnoringEnds()
        {
            // The end of b lies in the middle of a: only one segment's end is involved.
            var a = new Segment2D(0, 0, 10, 0);
            var b = new Segment2D(5, 0, 5, 10);
            Assert.True(a.Intersects(b, ignoreTouchingEnds: true));
            Assert.True(a.Intersects(b, ignoreTouchingEnds: false));
        }

        [Fact]
        public void SegmentIntersectsParallelNonIntersecting()
        {
            var a = new Segment2D(0, 0, 10, 0);
            var b = new Segment2D(0, 5, 10, 5);
            Assert.False(a.Intersects(b, ignoreTouchingEnds: false));
            Assert.False(a.Intersects(b, ignoreTouchingEnds: true));

            var farApart = new Segment2D(20, 0, 30, 3);
            Assert.False(a.Intersects(farApart, ignoreTouchingEnds: false));
        }

        [Fact]
        public void SegmentIntersectsCollinear()
        {
            var a = new Segment2D(0, 0, 10, 0);
            var overlap = new Segment2D(5, 0, 15, 0);
            var disjoint = new Segment2D(20, 0, 30, 0);
            var contained = new Segment2D(2, 0, 8, 0);
            var endToEnd = new Segment2D(10, 0, 20, 0);

            Assert.True(a.Intersects(overlap, ignoreTouchingEnds: false));
            Assert.True(a.Intersects(overlap, ignoreTouchingEnds: true));
            Assert.False(a.Intersects(disjoint, ignoreTouchingEnds: false));
            Assert.True(a.Intersects(contained, ignoreTouchingEnds: false));
            Assert.True(a.Intersects(endToEnd, ignoreTouchingEnds: false));
            Assert.False(a.Intersects(endToEnd, ignoreTouchingEnds: true));
        }

        [Fact]
        public void SegmentEquality()
        {
            var a = new Segment2D(0, 0, 1, 1);
            Assert.True(a.Equals(new Segment2D(0, 0, 1, 1)));
            Assert.False(a.Equals(new Segment2D(1, 1, 0, 0)));
            Assert.Equal(a.GetHashCode(), new Segment2D(0, 0, 1, 1).GetHashCode());
            Assert.Contains("->", a.ToString());
        }

        // ---- Rect2D --------------------------------------------------------------------------

        [Fact]
        public void RectNormalisesCornersAndExposesDimensions()
        {
            var r = new Rect2D(10, 20, -10, -20);
            Assert.Equal(-10, r.MinX);
            Assert.Equal(-20, r.MinY);
            Assert.Equal(10, r.MaxX);
            Assert.Equal(20, r.MaxY);
            Assert.Equal(20, r.Width);
            Assert.Equal(40, r.Depth);
            Assert.Equal(800, r.Area);
            Assert.Equal(Point2D.Origin, r.Center);
            Assert.Equal(new Point2D(-10, -20), r.Min);
            Assert.Equal(new Point2D(10, 20), r.Max);

            var fromCenter = Rect2D.FromCenter(new Point2D(100, 50), 20, 10);
            Assert.Equal(90, fromCenter.MinX);
            Assert.Equal(45, fromCenter.MinY);
            Assert.Equal(110, fromCenter.MaxX);
            Assert.Equal(55, fromCenter.MaxY);
        }

        [Fact]
        public void RectContainsWithTolerance()
        {
            var r = new Rect2D(0, 0, 100, 50);
            Assert.True(r.Contains(new Point2D(50, 25)));
            Assert.True(r.Contains(new Point2D(0, 0)));
            Assert.True(r.Contains(new Point2D(100, 50)));
            Assert.False(r.Contains(new Point2D(100.1, 25)));
            Assert.False(r.Contains(new Point2D(50, -0.1)));
            Assert.True(r.Contains(new Point2D(100.1, 25), toleranceMm: 0.2));
            Assert.True(r.Contains(new Point2D(-1, -1), toleranceMm: 1));
            Assert.False(r.Contains(new Point2D(-1.5, -1), toleranceMm: 1));
        }

        [Fact]
        public void RectInflateAndTranslate()
        {
            var r = new Rect2D(0, 0, 100, 50);
            var grown = r.Inflate(10);
            Assert.Equal(-10, grown.MinX);
            Assert.Equal(-10, grown.MinY);
            Assert.Equal(110, grown.MaxX);
            Assert.Equal(60, grown.MaxY);

            var shrunk = r.Inflate(-10);
            Assert.Equal(10, shrunk.MinX);
            Assert.Equal(90, shrunk.MaxX);
            Assert.Equal(80, shrunk.Width);
            Assert.Equal(30, shrunk.Depth);

            var moved = r.Translate(5, -5);
            Assert.Equal(5, moved.MinX);
            Assert.Equal(-5, moved.MinY);
            Assert.Equal(105, moved.MaxX);
            Assert.Equal(45, moved.MaxY);
            Assert.Equal(r.Width, moved.Width);
            Assert.Equal(r.Depth, moved.Depth);
        }

        [Fact]
        public void RectCornersAreCounterClockwiseFromMin()
        {
            var r = new Rect2D(0, 0, 100, 50);
            var c = r.Corners;
            Assert.Equal(4, c.Count);
            Assert.Equal(new Point2D(0, 0), c[0]);
            Assert.Equal(new Point2D(100, 0), c[1]);
            Assert.Equal(new Point2D(100, 50), c[2]);
            Assert.Equal(new Point2D(0, 50), c[3]);
            Assert.True(CurveValidation.SignedArea(c) > 0);
            Assert.Equal(r.Area, CurveValidation.SignedArea(c), Precision);
        }

        [Fact]
        public void RectEdgesAreBottomRightTopLeft()
        {
            var r = new Rect2D(0, 0, 100, 50);
            var e = r.Edges;
            Assert.Equal(4, e.Count);
            Assert.Equal(new Segment2D(0, 0, 100, 0), e[0]);
            Assert.Equal(new Segment2D(100, 0, 100, 50), e[1]);
            Assert.Equal(new Segment2D(100, 50, 0, 50), e[2]);
            Assert.Equal(new Segment2D(0, 50, 0, 0), e[3]);
            Assert.True(e[0].IsHorizontal());
            Assert.True(e[1].IsVertical());
            Assert.True(e[2].IsHorizontal());
            Assert.True(e[3].IsVertical());
            // Consecutive edges chain: end of one is start of the next.
            for (var i = 0; i < 4; i++) Assert.Equal(e[i].End, e[(i + 1) % 4].Start);
            Assert.Contains("[0,0]-[100,50]", r.ToString());
        }

        // ---- CurveValidation -----------------------------------------------------------------

        [Fact]
        public void IsValidSegmentRejectsShortNaNAndInfinite()
        {
            Assert.True(CurveValidation.IsValidSegment(new Segment2D(0, 0, 400, 0), 400));
            Assert.False(CurveValidation.IsValidSegment(new Segment2D(0, 0, 399.9, 0), 400));
            Assert.False(CurveValidation.IsValidSegment(new Segment2D(0, 0, 0, 0), 1));
            Assert.False(CurveValidation.IsValidSegment(new Segment2D(0, 0, double.NaN, 0), 1));
            Assert.False(CurveValidation.IsValidSegment(new Segment2D(double.NaN, 0, 100, 0), 1));
            Assert.False(CurveValidation.IsValidSegment(new Segment2D(0, 0, double.PositiveInfinity, 0), 1));
            Assert.False(CurveValidation.IsValidSegment(new Segment2D(0, double.NegativeInfinity, 100, 0), 1));
            Assert.True(CurveValidation.IsValidSegment(new Segment2D(0, 0, 0.5, 0), 0));
        }

        [Fact]
        public void IsFinite()
        {
            Assert.True(CurveValidation.IsFinite(new Point2D(1, -1)));
            Assert.False(CurveValidation.IsFinite(new Point2D(double.NaN, 0)));
            Assert.False(CurveValidation.IsFinite(new Point2D(0, double.PositiveInfinity)));
        }

        [Fact]
        public void SimpleClosedLoopAcceptsRectangleAndNotch()
        {
            var rect = new Rect2D(-9000, -6000, 9000, 6000).Corners;
            Assert.True(CurveValidation.IsSimpleClosedLoop(rect));

            var notch = new List<Point2D>
            {
                new Point2D(-9000, -6000),
                new Point2D(9000, -6000),
                new Point2D(9000, 5500),
                new Point2D(8500, 5500),
                new Point2D(8500, 6000),
                new Point2D(-9000, 6000),
            };
            Assert.True(CurveValidation.IsSimpleClosedLoop(notch));

            // Clockwise is still simple.
            Assert.True(CurveValidation.IsSimpleClosedLoop(rect.Reverse().ToList()));

            // A triangle is the smallest simple loop.
            Assert.True(CurveValidation.IsSimpleClosedLoop(new[] { new Point2D(0, 0), new Point2D(1000, 0), new Point2D(0, 1000) }));
        }

        [Fact]
        public void SimpleClosedLoopRejectsBowTie()
        {
            var bowTie = new[] { new Point2D(0, 0), new Point2D(1000, 1000), new Point2D(1000, 0), new Point2D(0, 1000) };
            Assert.False(CurveValidation.IsSimpleClosedLoop(bowTie));
        }

        [Fact]
        public void SimpleClosedLoopRejectsRepeatedPoint()
        {
            var repeated = new[] { new Point2D(0, 0), new Point2D(1000, 0), new Point2D(1000, 1000), new Point2D(0, 0), new Point2D(0, 1000) };
            Assert.False(CurveValidation.IsSimpleClosedLoop(repeated));

            // Nearly-repeated (within CoincidentMm) counts as repeated.
            var nearly = new[] { new Point2D(0, 0), new Point2D(1000, 0), new Point2D(1000, 1000), new Point2D(0, 1000), new Point2D(0.5, 0.5) };
            Assert.False(CurveValidation.IsSimpleClosedLoop(nearly));
        }

        [Fact]
        public void SimpleClosedLoopRejectsTooShortEdge()
        {
            var tol = new GeometryTolerances { MinCurveLengthMm = 100 };
            var shortEdge = new[] { new Point2D(0, 0), new Point2D(1000, 0), new Point2D(1000, 1000), new Point2D(1000, 1050), new Point2D(0, 1050) };
            Assert.False(CurveValidation.IsSimpleClosedLoop(shortEdge, tol));

            var relaxed = new GeometryTolerances { MinCurveLengthMm = 10 };
            Assert.True(CurveValidation.IsSimpleClosedLoop(shortEdge, relaxed));

            // The closing edge (last -> first) is checked as well.
            var shortClosing = new[] { new Point2D(0, 0), new Point2D(1000, 0), new Point2D(1000, 1000), new Point2D(0, 1000), new Point2D(0, 50) };
            Assert.False(CurveValidation.IsSimpleClosedLoop(shortClosing, tol));
        }

        [Fact]
        public void SimpleClosedLoopRejectsFewerThanThreePointsNullAndNaN()
        {
            Assert.False(CurveValidation.IsSimpleClosedLoop(null));
            Assert.False(CurveValidation.IsSimpleClosedLoop(new List<Point2D>()));
            Assert.False(CurveValidation.IsSimpleClosedLoop(new[] { new Point2D(0, 0) }));
            Assert.False(CurveValidation.IsSimpleClosedLoop(new[] { new Point2D(0, 0), new Point2D(1000, 0) }));
            Assert.False(CurveValidation.IsSimpleClosedLoop(new[] { new Point2D(0, 0), new Point2D(1000, 0), new Point2D(double.NaN, 1000) }));
        }

        [Fact]
        public void SimpleClosedLoopRejectsCollinearSpike()
        {
            // Edge 3 doubles back over edge 2: an overlapping (collinear) non-adjacent pair.
            var spike = new[]
            {
                new Point2D(0, 0), new Point2D(1000, 0), new Point2D(1000, 1000), new Point2D(1000, 2000),
                new Point2D(1000, 500), new Point2D(0, 500),
            };
            Assert.False(CurveValidation.IsSimpleClosedLoop(spike));
        }

        [Fact]
        public void SignedAreaSignFollowsWinding()
        {
            var ccw = new[] { new Point2D(0, 0), new Point2D(100, 0), new Point2D(100, 50), new Point2D(0, 50) };
            Assert.Equal(5000, CurveValidation.SignedArea(ccw), Precision);
            Assert.Equal(-5000, CurveValidation.SignedArea(Enumerable.Reverse(ccw).ToArray()), Precision);
            Assert.Equal(0, CurveValidation.SignedArea(null));
            Assert.Equal(0, CurveValidation.SignedArea(new[] { new Point2D(0, 0), new Point2D(1, 1) }));
            Assert.Equal(0.5, CurveValidation.SignedArea(new[] { new Point2D(0, 0), new Point2D(1, 0), new Point2D(0, 1) }), Precision);
        }

        [Fact]
        public void DefaultTolerancesAreOrderedSensibly()
        {
            var t = GeometryTolerances.Default;
            Assert.True(t.MinWallLengthMm > t.MinCurveLengthMm);
            Assert.True(t.MinCurveLengthMm > t.CoincidentMm);
            Assert.True(t.MinCornerGapMm <= t.MaxCornerGapMm);
            Assert.True(t.MinWallMisalignmentMm <= t.MaxWallMisalignmentMm);
            Assert.True(t.MinNearCoincidentGapMm > t.CoincidentMm);
            Assert.True(t.MaxLevelJitterMm > 0);
        }
    }
}
