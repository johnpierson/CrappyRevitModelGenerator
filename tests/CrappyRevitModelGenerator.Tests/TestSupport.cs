using System.Globalization;
using System.Text;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Core.Planning;

namespace CrappyRevitModelGenerator.Tests
{
    /// <summary>
    /// Shared helpers: settings factories and canonical text dumps of the planner outputs.
    /// The dumps include every field of every spec so "same seed twice gives the same plan"
    /// is a single string comparison and a change to any field is caught.
    /// </summary>
    internal static class TestSupport
    {
        public static readonly GenerationSeverity[] AllSeverities =
        {
            GenerationSeverity.Low, GenerationSeverity.Medium, GenerationSeverity.High,
        };

        public static GenerationSettings Settings(
            int seed = 42,
            GenerationSeverity severity = GenerationSeverity.Medium,
            int levels = GenerationLimits.DefaultLevels,
            double width = GenerationLimits.DefaultFootprintWidthMm,
            double depth = GenerationLimits.DefaultFootprintDepthMm,
            double levelHeight = GenerationLimits.DefaultLevelHeightMm,
            double contentScale = GenerationLimits.DefaultContentScale)
        {
            return new GenerationSettings
            {
                Seed = seed,
                Severity = severity,
                LevelCount = levels,
                FootprintWidthMm = width,
                FootprintDepthMm = depth,
                LevelHeightMm = levelHeight,
                ContentScale = contentScale,
            };
        }

        public static BaselinePlan Baseline(GenerationSettings settings, bool datumDefects = true) =>
            BaselinePlanner.Plan(settings, new SeededRandom(settings.Seed), datumDefects);

        // ---- Dumps ---------------------------------------------------------------------------

        private static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        private static string P(Point2D p) => $"({F(p.X)},{F(p.Y)})";
        private static string S(Segment2D s) => $"{P(s.Start)}->{P(s.End)}";
        private static string R(Rect2D r) => $"[{F(r.MinX)},{F(r.MinY)}]-[{F(r.MaxX)},{F(r.MaxY)}]";
        private static string Tags(IEnumerable<string> tags) => "{" + string.Join("|", tags) + "}";

        public static string Dump(BaselinePlan plan)
        {
            var sb = new StringBuilder();
            sb.AppendLine("footprint " + R(plan.Footprint) + " h=" + F(plan.LevelHeightMm));
            foreach (var l in plan.Levels)
                sb.AppendLine($"level {l.Index} {l.CleanName} {F(l.ElevationMm)} inter={l.IsIntermediate}");
            foreach (var g in plan.Grids)
                sb.AppendLine($"grid {g.Index} {g.CleanName} {S(g.Line)} v={g.IsVertical} bs={g.BubbleAtStart} be={g.BubbleAtEnd} {Tags(g.DefectTags)}");
            foreach (var w in plan.Walls)
                sb.AppendLine($"wall {w.Index} L{w.LevelIndex} {w.Role} {S(w.Line)} type={w.TypeChoice} h={F(w.HeightMm)} attach={w.AttachTopToLevelAbove} loc={w.LocationLineChoice} rb={w.IsRoomBounding} join={w.DisallowJoinMask} {Tags(w.DefectTags)}");
            foreach (var f in plan.Floors)
                sb.AppendLine($"floor {f.Index} L{f.LevelIndex} {string.Join(";", f.Loop.Select(P))} {Tags(f.DefectTags)}");
            foreach (var c in plan.Cells)
                sb.AppendLine($"cell {c.Index} L{c.LevelIndex} {c.Band} {R(c.Bounds)} door={c.DoorWallIndex} broken={c.EnclosureBroken}");
            foreach (var d in plan.Defects)
                sb.AppendLine($"defect {d.ScenarioId} {d.Message} [{string.Join(",", d.RelatedIndices)}]");
            return sb.ToString();
        }

        public static string Dump(ContentPlan plan)
        {
            var sb = new StringBuilder();
            foreach (var o in plan.Openings)
                sb.AppendLine($"opening {o.Index} L{o.LevelIndex} W{o.WallIndex} {o.Kind} d={F(o.DistanceAlongMm)} sill={F(o.SillHeightMm)} hand={o.FlipHand} face={o.FlipFacing} {Tags(o.DefectTags)}");
            foreach (var f in plan.Furniture)
                sb.AppendLine($"furniture {f.Index} L{f.LevelIndex} C{f.CellIndex} {P(f.Location)} rot={F(f.RotationDegrees)} {Tags(f.DefectTags)}");
            foreach (var d in plan.Defects)
                sb.AppendLine($"defect {d.ScenarioId} {d.Message}");
            return sb.ToString();
        }

        public static string Dump(RoomPlan plan)
        {
            var sb = new StringBuilder();
            foreach (var r in plan.Rooms)
                sb.AppendLine($"room {r.Index} L{r.LevelIndex} C{r.CellIndex} loc={(r.Location.HasValue ? P(r.Location.Value) : "null")} '{r.Name}' '{r.Number}' tag={r.CreateTag} fake={r.FakeTag} off={P(r.TagOffsetMm)} {Tags(r.DefectTags)}");
            foreach (var s in plan.SeparationLines)
                sb.AppendLine($"sep {s.Index} L{s.LevelIndex} {S(s.Line)} {Tags(s.DefectTags)}");
            foreach (var d in plan.Defects)
                sb.AppendLine($"defect {d.ScenarioId} {d.Message}");
            return sb.ToString();
        }
    }
}
