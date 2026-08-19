using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Views, sheets and annotation assembled with weak documentation standards (plan section
    /// 7.4, generation step 6). Every baseline plan gets near-duplicates at inappropriate scales
    /// and detail levels, some in the wrong discipline, with a mix of tight, hidden, asymmetric
    /// and absent crops; sections and elevations sit away from the model or have inconsistent
    /// extents; 3D and drafting views are created and left empty; sheets are empty, misleadingly
    /// titled, or carry viewports that overlap or wander off the grid; placeholder text notes
    /// land on plans (two on top of each other) and in a drafting view.
    ///
    /// Everything stays valid: duplicates are real <see cref="View.Duplicate"/> copies, crops are
    /// proper boxes inside the view's own crop coordinate system, section boxes are right-handed
    /// orthonormal transforms with positive far clips, and elevation markers face the model when
    /// the first one is asked to. Names are only made bad when the naming scenario is enabled;
    /// that scenario runs afterwards and renames generated views and sheets anyway.
    /// </summary>
    public sealed class DocumentationScenario : IBadModelScenario
    {
        public string Id => ScenarioIds.Documentation;

        private const string StreamViews = "documentation/views";
        private const string StreamSheets = "documentation/sheets";
        private const string StreamNotes = "documentation/notes";

        /// <summary>Scales nobody draws a floor plan at: 1:20 / 1:25 are detail scales, 1:200 and up are site scales.</summary>
        private static readonly int[] OddPlanScales = { 20, 25, 200, 250, 500 };

        private static readonly ViewDiscipline[] WrongDisciplines =
        {
            ViewDiscipline.Structural,
            ViewDiscipline.Mechanical,
            ViewDiscipline.Coordination,
        };

        private static readonly string[] CleanSheetNames =
        {
            "Floor Plans", "Sections and Elevations", "3D Views", "General Notes", "Details",
            "Cover Sheet", "Schedules", "Enlarged Plans", "Site Plan", "Roof Plan",
        };

        // Sheet coordinates are feet; a typical title block spans roughly (0,0)-(2.7,1.9) ft.
        private const double ViewportJitterFt = 0.15;
        private const double ViewportsTooCloseFt = 0.1;

        // Section geometry, millimetres.
        private const double SectionPadMm = 1000;
        private const double SectionBehindMm = 500;
        private const double TinyFarClipMm = 300;

        private const double NoteInsetMm = 500;
        private const double NoteOverlapJitterMm = 50;

        public bool CanRun(GenerationContext context, out string reason)
        {
            reason = null;
            if (context.Baseline == null)
            {
                reason = "The baseline plan is missing.";
                return false;
            }
            if (context.PlanViews.Count == 0)
            {
                reason = "The baseline created no plan views.";
                return false;
            }
            return true;
        }

        public void Generate(GenerationContext context)
        {
            var w = new Work
            {
                Context = context,
                Views = context.Random.Stream(StreamViews),
                Sheets = context.Random.Stream(StreamSheets),
                Notes = context.Random.Stream(StreamNotes),
                Naming = context.IsScenarioEnabled(ScenarioIds.Naming),
                Footprint = context.Baseline.Footprint,
            };

            foreach (var pair in context.PlanViews.OrderBy(p => p.Key))
            {
                var view = pair.Value;
                if (view == null || !view.IsValidObject) continue;
                var level = context.LevelFor(pair.Key) ?? SafeGenLevel(view);
                w.BaselinePlans.Add(new PlanEntry(view, level));
            }
            if (w.BaselinePlans.Count == 0)
                throw new InvalidOperationException("No valid baseline plan views to document.");

            DuplicatePlans(w);
            CreateSections(w);
            CreateElevations(w);
            CreateThreeDViews(w);
            CreateDraftingViews(w);
            ApplyMismatchedTemplates(w);
            ReportViewsWithoutTemplates(w);

            try
            {
                // Crops and scales changed above; let the outlines settle before viewports are sized.
                context.Document.Regenerate();
            }
            catch (Exception ex)
            {
                context.Report.AddException(Id, "regenerate before sheets", ex, rolledBack: false);
            }

            CreateSheetsAndViewports(w);
            CreateTextNotes(w);

            context.Report.AddInfo(Id,
                $"Documentation: {w.DuplicatePlans.Count} duplicated plan(s), {w.Sections.Count} section(s), {w.Elevations.Count} elevation(s), " +
                $"{w.ThreeD.Count} 3D view(s), {w.Drafting.Count} drafting view(s), {w.SheetList.Count} sheet(s) ({w.EmptySheetCount} empty), " +
                $"{w.ViewportCount} viewport(s), {w.TextNoteCount} text note(s).");
        }

        // ---- Duplicated plans --------------------------------------------------------------

        private void DuplicatePlans(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var factory = ctx.Factory;
            var profile = ctx.Profile;
            var rnd = w.Views;

            var dupIds = new List<long>();
            var clearedTemplateIds = new List<long>();
            var scaleNotes = new List<string>();
            var scaleIds = new List<long>();
            var detailNotes = new List<string>();
            var detailIds = new List<long>();
            var disciplineNotes = new List<string>();
            var disciplineIds = new List<long>();
            var cropCounts = new int[4];
            var cropIds = new List<long>();

            var first = true;
            var capReached = false;

            foreach (var plan in w.BaselinePlans)
            {
                if (capReached) break;
                for (var i = 0; i < profile.DuplicatePlansPerLevel; i++)
                {
                    if (!CanAddView(w))
                    {
                        capReached = true;
                        break;
                    }

                    var suffix = w.Naming
                        ? rnd.Pick(BadNames.DuplicateViewSuffixes)
                        : " Copy " + (i + 1).ToString(CultureInfo.InvariantCulture);
                    var sourceName = SafeName(plan.View);

                    View dup = null;
                    try
                    {
                        dup = factory.DuplicateView(plan.View, ViewDuplicateOption.Duplicate, sourceName + suffix);
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"duplicate plan view '{sourceName}'", ex, rolledBack: false);
                    }
                    if (dup == null)
                    {
                        report.AddFallback(Id, $"Plan view '{sourceName}' could not be duplicated; skipped.");
                        continue;
                    }

                    w.DuplicatePlans.Add(new PlanEntry(dup, plan.Level));
                    dupIds.Add(dup.Id.Value);

                    // Draw every decision up front so an API refusal does not reshuffle later picks.
                    var oddScale = rnd.NextBool(profile.OddScaleFraction) || first;
                    var scale = rnd.Pick(OddPlanScales);
                    // The profile has no separate detail-level fraction; scale and detail level share one.
                    var oddDetail = rnd.NextBool(profile.OddScaleFraction);
                    var detail = rnd.NextBool(0.5) ? ViewDetailLevel.Coarse : ViewDetailLevel.Fine;
                    var wrongDiscipline = rnd.NextBool(profile.WrongDisciplineFraction);
                    var discipline = rnd.Pick(WrongDisciplines);
                    var oddCrop = rnd.NextBool(profile.OddCropFraction) || first;
                    var cropMode = rnd.NextInt(0, 4);
                    first = false;

                    // A duplicate inherits the source's template (from the view family type's
                    // default). Drop it so the tweaks below are not blocked — "views without view
                    // templates" is itself on the list.
                    try
                    {
                        if (dup.ViewTemplateId != ElementId.InvalidElementId)
                        {
                            dup.ViewTemplateId = ElementId.InvalidElementId;
                            clearedTemplateIds.Add(dup.Id.Value);
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"clear view template on '{SafeName(dup)}'", ex, rolledBack: false);
                    }

                    if (oddScale)
                    {
                        try
                        {
                            dup.Scale = scale;
                            scaleNotes.Add($"'{SafeName(dup)}' 1:{scale}");
                            scaleIds.Add(dup.Id.Value);
                        }
                        catch (Exception ex)
                        {
                            report.AddException(Id, $"set scale 1:{scale} on '{SafeName(dup)}'", ex, rolledBack: false);
                        }
                    }

                    if (oddDetail)
                    {
                        try
                        {
                            dup.DetailLevel = detail;
                            detailNotes.Add($"'{SafeName(dup)}' {detail}");
                            detailIds.Add(dup.Id.Value);
                        }
                        catch (Exception ex)
                        {
                            report.AddException(Id, $"set detail level {detail} on '{SafeName(dup)}'", ex, rolledBack: false);
                        }
                    }

                    if (wrongDiscipline)
                    {
                        // Not every view allows its discipline to change; a refusal is not a defect.
                        try
                        {
                            dup.Discipline = discipline;
                            disciplineNotes.Add($"'{SafeName(dup)}' {discipline}");
                            disciplineIds.Add(dup.Id.Value);
                        }
                        catch (Exception ex)
                        {
                            report.AddException(Id, $"set discipline {discipline} on '{SafeName(dup)}'", ex, rolledBack: false);
                        }
                    }

                    if (oddCrop)
                    {
                        try
                        {
                            if (ApplyCrop(w, dup, plan.Level, cropMode))
                            {
                                cropCounts[cropMode]++;
                                cropIds.Add(dup.Id.Value);
                            }
                        }
                        catch (Exception ex)
                        {
                            report.AddException(Id, $"set crop (mode {cropMode}) on '{SafeName(dup)}'", ex, rolledBack: false);
                        }
                    }
                }
            }

            if (dupIds.Count > 0)
                report.AddDefect(Id, $"{dupIds.Count} near-duplicate plan view(s) created from the baseline plans with copy-style names.", dupIds);
            if (clearedTemplateIds.Count > 0)
                report.AddDefect(Id, $"{clearedTemplateIds.Count} duplicated plan(s) had their inherited view template removed (views without view templates).", clearedTemplateIds);
            if (scaleIds.Count > 0)
                report.AddDefect(Id, $"Inappropriate plan scales: {string.Join(", ", scaleNotes)}.", scaleIds);
            if (detailIds.Count > 0)
                report.AddDefect(Id, $"Inappropriate detail levels for a floor plan: {string.Join(", ", detailNotes)}.", detailIds);
            if (disciplineIds.Count > 0)
                report.AddDefect(Id, $"Plan view(s) assigned to the wrong discipline: {string.Join(", ", disciplineNotes)}.", disciplineIds);
            if (cropIds.Count > 0)
                report.AddDefect(Id,
                    $"Inconsistent crop regions on duplicated plans: {cropCounts[0]} tightly cropped to ~40% of the footprint, " +
                    $"{cropCounts[1]} cropped with the crop region hidden, {cropCounts[2]} loosely and asymmetrically cropped, {cropCounts[3]} explicitly uncropped.",
                    cropIds);
        }

        /// <summary>
        /// Crop modes: 0 = tight (about 40% of the footprint), 1 = tight with the crop region
        /// hidden, 2 = loose and asymmetric, 3 = explicitly uncropped. The new box is expressed in
        /// the view's own crop coordinate system (the setter ignores the Transform and reads
        /// Min/Max in that frame), so the footprint corners are mapped through the inverse of the
        /// existing crop box transform rather than assumed to be world XY.
        /// </summary>
        private bool ApplyCrop(Work w, View view, Level level, int mode)
        {
            var rnd = w.Views;
            var fp = w.Footprint;

            if (mode == 3)
            {
                view.CropBoxActive = false;
                view.CropBoxVisible = false;
                return true;
            }

            Rect2D region;
            if (mode == 2)
            {
                region = new Rect2D(
                    fp.MinX - rnd.NextDouble(500, 6000),
                    fp.MinY - rnd.NextDouble(500, 6000),
                    fp.MaxX + rnd.NextDouble(500, 6000),
                    fp.MaxY + rnd.NextDouble(500, 6000));
            }
            else
            {
                // 0.63 on each side is ~40% of the area.
                var width = fp.Width * 0.63;
                var depth = fp.Depth * 0.63;
                var x0 = fp.MinX + rnd.NextDouble(0, fp.Width - width);
                var y0 = fp.MinY + rnd.NextDouble(0, fp.Depth - depth);
                region = new Rect2D(x0, y0, x0 + width, y0 + depth);
            }

            var existing = view.CropBox;
            if (existing == null) return false;

            var inverse = existing.Transform.Inverse;
            var z = level != null ? level.ProjectElevation : 0.0;
            double minX = double.MaxValue, minY = double.MaxValue, maxX = double.MinValue, maxY = double.MinValue;
            foreach (var corner in region.Corners)
            {
                var local = inverse.OfPoint(UnitConversion.ToXYZAtFeet(corner, z));
                minX = Math.Min(minX, local.X);
                minY = Math.Min(minY, local.Y);
                maxX = Math.Max(maxX, local.X);
                maxY = Math.Max(maxY, local.Y);
            }
            if (maxX - minX < 1e-6 || maxY - minY < 1e-6) return false;

            var box = new BoundingBoxXYZ
            {
                Transform = existing.Transform,
                Min = new XYZ(minX, minY, existing.Min.Z),
                Max = new XYZ(maxX, maxY, existing.Max.Z),
            };

            view.CropBoxActive = true;
            view.CropBox = box;
            view.CropBoxVisible = mode != 1;
            return true;
        }

        // ---- Sections ---------------------------------------------------------------------

        private enum SectionKind
        {
            Away,
            TinyFarClip,
            Normal,
        }

        private void CreateSections(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var factory = ctx.Factory;
            var profile = ctx.Profile;
            var rnd = w.Views;
            var fp = w.Footprint;

            var count = profile.Sections;
            if (count <= 0) return;
            if (ctx.Types.SectionType == null) return; // the type resolver already reported the fallback

            var levels = ctx.Levels.Values.Where(l => l != null && l.IsValidObject).ToList();
            if (levels.Count == 0) return;
            var minZ = levels.Min(l => l.ProjectElevation);
            var maxZ = levels.Max(l => l.ProjectElevation) + UnitConversion.MmToFeet(ctx.Baseline.LevelHeightMm);

            var names = w.Naming ? rnd.TakeCycling(BadNames.ViewNames, count) : null;

            for (var i = 0; i < count; i++)
            {
                if (!CanAddView(w)) break;

                var kind = i == 0 ? SectionKind.Away : i == 1 ? SectionKind.TinyFarClip : SectionKind.Normal;
                var alongX = rnd.NextBool(0.5);
                var awayOffset = rnd.NextDouble(10000, 15000);
                var jitter = rnd.NextJitter(0.25 * (alongX ? fp.Depth : fp.Width));
                var name = names != null ? names[i] : "Section " + (i + 1).ToString(CultureInfo.InvariantCulture);

                Point2D p, q;
                double aheadMm;
                if (alongX)
                {
                    // Section line runs +X; the view looks -Y (dir x up = -Y).
                    var y = kind == SectionKind.Away ? fp.MaxY + awayOffset : fp.Center.Y + jitter;
                    if (kind == SectionKind.TinyFarClip)
                    {
                        p = new Point2D(fp.Center.X - fp.Width * 0.25, y);
                        q = new Point2D(fp.Center.X + fp.Width * 0.25, y);
                    }
                    else
                    {
                        p = new Point2D(fp.MinX - SectionPadMm, y);
                        q = new Point2D(fp.MaxX + SectionPadMm, y);
                    }
                    aheadMm = kind == SectionKind.TinyFarClip ? TinyFarClipMm : (y - fp.MinY) + SectionPadMm;
                }
                else
                {
                    // Section line runs +Y; the view looks +X.
                    var x = kind == SectionKind.Away ? fp.MinX - awayOffset : fp.Center.X + jitter;
                    if (kind == SectionKind.TinyFarClip)
                    {
                        p = new Point2D(x, fp.Center.Y - fp.Depth * 0.25);
                        q = new Point2D(x, fp.Center.Y + fp.Depth * 0.25);
                    }
                    else
                    {
                        p = new Point2D(x, fp.MinY - SectionPadMm);
                        q = new Point2D(x, fp.MaxY + SectionPadMm);
                    }
                    aheadMm = kind == SectionKind.TinyFarClip ? TinyFarClipMm : (fp.MaxX - x) + SectionPadMm;
                }

                try
                {
                    var box = SectionBox(
                        UnitConversion.ToXYZ(p, 0), UnitConversion.ToXYZ(q, 0),
                        minZ, maxZ,
                        UnitConversion.MmToFeet(SectionBehindMm), UnitConversion.MmToFeet(aheadMm), UnitConversion.MmToFeet(SectionPadMm));
                    var view = factory.CreateSection(box, name);
                    if (view == null)
                    {
                        report.AddFallback(Id, "Section view family type unavailable; sections skipped.");
                        break;
                    }
                    w.Sections.Add(view);

                    var ids = new[] { view.Id.Value };
                    switch (kind)
                    {
                        case SectionKind.Away:
                            report.AddDefect(Id, $"Section '{SafeName(view)}' is cut {awayOffset / 1000:0.0} m outside the footprint, well away from the model.", ids);
                            break;
                        case SectionKind.TinyFarClip:
                            report.AddDefect(Id, $"Section '{SafeName(view)}' has inconsistent extents: it spans only half the building and its far clip is {TinyFarClipMm:0} mm.", ids);
                            break;
                        default:
                            report.AddInfo(Id, $"Section '{SafeName(view)}' cut through the building along {(alongX ? "X" : "Y")}.", ids);
                            break;
                    }
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create section {i + 1} ({kind})", ex, rolledBack: false);
                }
            }
        }

        /// <summary>
        /// The Building Coder recipe: origin at the line's midpoint, X along the line, Y up, Z the
        /// view direction (X x Y, so the transform stays right-handed). Min/Max are in that frame:
        /// X spans the line plus padding, Y spans the levels plus padding, Z runs from a little
        /// behind the cut to the far clip.
        /// </summary>
        private static BoundingBoxXYZ SectionBox(XYZ p, XYZ q, double minZ, double maxZ, double behindFt, double aheadFt, double padFt)
        {
            var v = q - p;
            var halfLength = v.GetLength() / 2.0;
            var midpoint = p + 0.5 * v;
            var dir = v.Normalize();
            var up = XYZ.BasisZ;
            var viewDir = dir.CrossProduct(up);

            var t = Transform.Identity;
            t.Origin = midpoint;
            t.BasisX = dir;
            t.BasisY = up;
            t.BasisZ = viewDir;

            return new BoundingBoxXYZ
            {
                Transform = t,
                Min = new XYZ(-halfLength - padFt, minZ - padFt, -behindFt),
                Max = new XYZ(halfLength + padFt, maxZ + padFt, aheadFt),
            };
        }

        // ---- Elevations -------------------------------------------------------------------

        private void CreateElevations(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var factory = ctx.Factory;
            var profile = ctx.Profile;
            var rnd = w.Views;
            var fp = w.Footprint;

            var count = profile.Elevations;
            if (count <= 0) return;
            if (ctx.Types.ElevationType == null) return; // fallback already reported by the type resolver

            var lowest = w.BaselinePlans[0];
            var planView = lowest.View as ViewPlan;
            if (planView == null)
            {
                report.AddFallback(Id, "The lowest baseline plan is not a ViewPlan; elevations skipped.");
                return;
            }
            var z = lowest.Level != null ? lowest.Level.ProjectElevation : 0.0;

            var badNames = w.Naming ? rnd.TakeCycling(BadNames.ViewNames.Where(n => Contains(n, "elev")).ToList(), count) : null;

            for (var i = 0; i < count; i++)
            {
                if (!CanAddView(w)) break;

                var side = rnd.NextInt(0, 4);              // 0 south, 1 east, 2 north, 3 west
                var distanceMm = rnd.NextDouble(3000, 6000);
                var along = rnd.NextDouble(0.2, 0.8);
                var randomIndex = rnd.NextInt(0, 4);
                var index = i == 0 ? GuessIndexFacing(side) : randomIndex;
                var name = badNames != null && badNames.Count > 0 ? badNames[i % badNames.Count] : "Elevation " + (i + 1).ToString(CultureInfo.InvariantCulture);

                Point2D markerMm;
                switch (side)
                {
                    case 0: markerMm = new Point2D(fp.MinX + fp.Width * along, fp.MinY - distanceMm); break;
                    case 1: markerMm = new Point2D(fp.MaxX + distanceMm, fp.MinY + fp.Depth * along); break;
                    case 2: markerMm = new Point2D(fp.MinX + fp.Width * along, fp.MaxY + distanceMm); break;
                    default: markerMm = new Point2D(fp.MinX - distanceMm, fp.MinY + fp.Depth * along); break;
                }
                var markerPoint = UnitConversion.ToXYZAtFeet(markerMm, z);

                View view = null;
                var typeMissing = false;
                for (var attempt = 0; attempt < 4 && view == null; attempt++)
                {
                    var idx = (index + attempt) % 4;
                    try
                    {
                        view = factory.CreateElevation(markerPoint, planView, idx, name);
                        if (view == null)
                        {
                            typeMissing = true;
                            break;
                        }
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"create elevation {i + 1} at marker index {idx}", ex, rolledBack: false);
                    }
                }
                if (typeMissing)
                {
                    report.AddFallback(Id, "Elevation view family type unavailable; elevations skipped.");
                    break;
                }
                if (view == null) continue;
                w.Elevations.Add(view);

                var facing = false;
                if (i == 0)
                {
                    // The first elevation should at least look at the building; the rest may point anywhere.
                    try
                    {
                        facing = FaceTheModel(w, view, markerPoint, TowardModel(side));
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"orient elevation '{SafeName(view)}'", ex, rolledBack: false);
                    }
                }
                else
                {
                    facing = IsFacing(view, TowardModel(side));
                }

                report.AddDefect(Id,
                    $"Elevation '{SafeName(view)}' has its marker {distanceMm / 1000:0.0} m off the {SideName(side)} side of the footprint" +
                    (facing ? "; its extents were inherited from the plan view rather than fitted to the model." : " and looks away from the model (inconsistent extents)."),
                    new[] { view.Id.Value });
            }
        }

        private static XYZ TowardModel(int side)
        {
            switch (side)
            {
                case 0: return XYZ.BasisY;      // south of the model: look north
                case 1: return -XYZ.BasisX;     // east: look west
                case 2: return -XYZ.BasisY;     // north: look south
                default: return XYZ.BasisX;     // west: look east
            }
        }

        /// <summary>
        /// Best guess at the marker index that faces the model (0 = the "West" elevation looking
        /// east, 1 = "North" looking south, 2 = "East" looking west, 3 = "South" looking north).
        /// <see cref="FaceTheModel"/> checks the real ViewDirection afterwards, so a wrong guess
        /// only costs a rotation.
        /// </summary>
        private static int GuessIndexFacing(int side)
        {
            switch (side)
            {
                case 0: return 3;
                case 1: return 2;
                case 2: return 1;
                default: return 0;
            }
        }

        private static string SideName(int side)
        {
            switch (side)
            {
                case 0: return "south";
                case 1: return "east";
                case 2: return "north";
                default: return "west";
            }
        }

        private static bool IsFacing(View view, XYZ desired)
        {
            try
            {
                var d = view.ViewDirection;
                var flat = new XYZ(d.X, d.Y, 0);
                if (flat.GetLength() < 1e-6) return false;
                return flat.Normalize().DotProduct(desired) > 0.99;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Rotate the elevation's marker (ours — it hosts the view we just created) about a
        /// vertical axis until the view looks toward the model. The rotation sign convention is
        /// verified by reading ViewDirection back and reversing once if it went the wrong way.
        /// </summary>
        private bool FaceTheModel(Work w, View elevation, XYZ markerPoint, XYZ desired)
        {
            if (IsFacing(elevation, desired)) return true;

            var doc = w.Context.Document;
            var marker = FindMarkerHosting(w, elevation);
            if (marker == null) return false;

            var d = elevation.ViewDirection;
            var flat = new XYZ(d.X, d.Y, 0);
            if (flat.GetLength() < 1e-6) return false;
            flat = flat.Normalize();

            var angle = Math.Atan2(flat.CrossProduct(desired).Z, flat.DotProduct(desired));
            if (Math.Abs(angle) < 1e-6) return true;

            // The marker's rotation reaches its hosted views on regeneration, so regenerate
            // before trusting ViewDirection again.
            var axis = Line.CreateBound(markerPoint, markerPoint + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, marker.Id, axis, angle);
            doc.Regenerate();
            if (IsFacing(elevation, desired)) return true;

            ElementTransformUtils.RotateElement(doc, marker.Id, axis, -2 * angle);
            doc.Regenerate();
            return IsFacing(elevation, desired);
        }

        private static ElevationMarker FindMarkerHosting(Work w, View elevation)
        {
            var ctx = w.Context;
            foreach (var marker in new FilteredElementCollector(ctx.Document).OfClass(typeof(ElevationMarker)).Cast<ElevationMarker>())
            {
                if (!ctx.Registry.Contains(marker.Id)) continue; // never touch a marker the generator did not create
                var max = marker.MaximumViewCount;
                for (var k = 0; k < max; k++)
                {
                    if (marker.GetViewId(k) == elevation.Id) return marker;
                }
            }
            return null;
        }

        // ---- 3D and drafting views ---------------------------------------------------------

        private void CreateThreeDViews(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var factory = ctx.Factory;
            var rnd = w.Views;

            var count = ctx.Profile.ThreeDViews;
            if (count <= 0) return;
            if (ctx.Types.ThreeDType == null) return;

            var badNames = w.Naming ? rnd.TakeCycling(BadNames.ViewNames.Where(n => Contains(n, "3d")).ToList(), count) : null;

            for (var i = 0; i < count; i++)
            {
                if (!CanAddView(w)) break;
                var name = badNames != null && badNames.Count > 0 ? badNames[i % badNames.Count] : "3D View " + (i + 1).ToString(CultureInfo.InvariantCulture);
                try
                {
                    var view = factory.CreateIsometric(name);
                    if (view == null)
                    {
                        report.AddFallback(Id, "3D view family type unavailable; 3D views skipped.");
                        break;
                    }
                    w.ThreeD.Add(view);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create 3D view {i + 1}", ex, rolledBack: false);
                }
            }

            if (w.ThreeD.Count > 1)
                report.AddDefect(Id, $"{w.ThreeD.Count} default 3D views that differ only by name — nothing says which one is current.", w.ThreeD.Select(v => v.Id.Value));
            else if (w.ThreeD.Count == 1)
                report.AddInfo(Id, $"3D view '{SafeName(w.ThreeD[0])}' created with default orientation.", w.ThreeD.Select(v => v.Id.Value));
        }

        private void CreateDraftingViews(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var factory = ctx.Factory;
            var rnd = w.Views;

            var count = ctx.Profile.DraftingViews;
            if (count <= 0) return;
            if (ctx.Types.DraftingType == null) return;

            var badNames = w.Naming
                ? rnd.TakeCycling(BadNames.ViewNames.Where(n => Contains(n, "detail") || Contains(n, "drafting")).ToList(), count)
                : null;

            for (var i = 0; i < count; i++)
            {
                if (!CanAddView(w)) break;
                var name = badNames != null && badNames.Count > 0 ? badNames[i % badNames.Count] : "Drafting View " + (i + 1).ToString(CultureInfo.InvariantCulture);
                try
                {
                    var view = factory.CreateDrafting(name);
                    if (view == null)
                    {
                        report.AddFallback(Id, "Drafting view family type unavailable; drafting views skipped.");
                        break;
                    }
                    w.Drafting.Add(view);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create drafting view {i + 1}", ex, rolledBack: false);
                }
            }

            if (w.Drafting.Count > 0)
                report.AddDefect(Id, $"{w.Drafting.Count} empty drafting view(s) with nothing drawn in them.", w.Drafting.Select(v => v.Id.Value));
        }

        // ---- View templates ----------------------------------------------------------------

        private void ApplyMismatchedTemplates(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var rnd = w.Views;

            if (w.DuplicatePlans.Count == 0) return;
            var targets = rnd.TakeDistinct(w.DuplicatePlans, 2);

            var templates = ctx.Types.ViewTemplates;
            if (templates == null || templates.Count == 0)
            {
                report.AddFallback(Id, "The document has no view templates; the 'template does not match its purpose' defect is skipped.");
                return;
            }

            var reportedNone = false;
            foreach (var target in targets)
            {
                var view = target.View;
                var valid = templates.Where(t => IsValidTemplateFor(view, t)).ToList();
                if (valid.Count == 0)
                {
                    if (!reportedNone)
                    {
                        report.AddFallback(Id, "No view template in the document is applicable to the duplicated plans; the mismatched-template defect is skipped.");
                        reportedNone = true;
                    }
                    continue;
                }

                // Prefer a template that is clearly not meant for a floor plan; fall back to any.
                var offPurpose = valid.Where(t => !Contains(SafeName(t), "floor")).ToList();
                var chosen = rnd.Pick(offPurpose.Count > 0 ? offPurpose : valid);
                try
                {
                    view.ViewTemplateId = chosen.Id;
                    report.AddDefect(Id, $"View template '{SafeName(chosen)}' applied to plan '{SafeName(view)}' — the template does not match the view's purpose.", new[] { view.Id.Value });
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"apply view template '{SafeName(chosen)}' to '{SafeName(view)}'", ex, rolledBack: false);
                }
            }
        }

        private static bool IsValidTemplateFor(View view, View template)
        {
            try
            {
                return template != null && template.IsValidObject && view.IsValidViewTemplate(template.Id);
            }
            catch
            {
                return false;
            }
        }

        private void ReportViewsWithoutTemplates(Work w)
        {
            var ctx = w.Context;
            var ids = new List<long>();
            foreach (var view in ctx.Views)
            {
                try
                {
                    if (view == null || !view.IsValidObject || view.IsTemplate) continue;
                    if (view.ViewTemplateId == ElementId.InvalidElementId) ids.Add(view.Id.Value);
                }
                catch
                {
                    // A view type without the property; ignore.
                }
            }
            if (ids.Count > 0)
                ctx.Report.AddDefect(Id, $"{ids.Count} generated view(s) have no view template applied.", ids);
        }

        // ---- Sheets and viewports ------------------------------------------------------------

        private void CreateSheetsAndViewports(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var factory = ctx.Factory;
            var profile = ctx.Profile;
            var rnd = w.Sheets;

            var wanted = Math.Min(profile.Sheets, Math.Max(0, GenerationLimits.MaxSheets - ctx.Sheets.Count));
            if (wanted <= 0)
            {
                if (profile.Sheets > 0) report.AddInfo(Id, $"Sheet cap of {GenerationLimits.MaxSheets} reached; no sheets created.");
                return;
            }

            try
            {
                // ViewSheet.Create needs an active title block symbol; a fresh template may not have one active yet.
                ctx.Types.EnsureActive(ctx.Types.TitleBlockSymbol);
            }
            catch (Exception ex)
            {
                report.AddException(Id, "activate title block symbol", ex, rolledBack: false);
            }

            var numbers = w.Naming
                ? rnd.TakeCycling(BadNames.SheetNumbers, wanted)
                : Enumerable.Range(0, wanted).Select(i => "A" + (101 + i).ToString(CultureInfo.InvariantCulture)).ToList();
            var names = w.Naming
                ? rnd.TakeCycling(BadNames.SheetNames, wanted)
                : Enumerable.Range(0, wanted).Select(i => CleanSheetNames[i % CleanSheetNames.Length]).ToList();

            var emptyCount = Math.Min(profile.EmptySheets, Math.Max(0, wanted - 1));
            var emptyIndices = new HashSet<int>(rnd.TakeDistinct(Enumerable.Range(0, wanted).ToList(), emptyCount));

            var sheets = new List<ViewSheet>();
            var emptySheets = new List<ViewSheet>();
            for (var i = 0; i < wanted; i++)
            {
                try
                {
                    var sheet = factory.CreateSheet(numbers[i], names[i]);
                    if (sheet == null) continue;
                    sheets.Add(sheet);
                    if (emptyIndices.Contains(i)) emptySheets.Add(sheet);
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create sheet {numbers[i]} '{names[i]}'", ex, rolledBack: false);
                }
            }
            w.SheetList.AddRange(sheets);
            w.EmptySheetCount = emptySheets.Count;
            if (sheets.Count == 0) return;

            if (emptySheets.Count > 0)
                report.AddDefect(Id, $"{emptySheets.Count} empty sheet(s) with no views: {Describe(emptySheets)}.", emptySheets.Select(s => s.Id.Value));

            var nonEmpty = sheets.Where(s => !emptySheets.Any(e => e.Id == s.Id)).ToList();
            var pool = rnd.Shuffle(EligibleForSheets(w));
            if (nonEmpty.Count == 0 || pool.Count == 0) return;

            // Misleading title: the sheet whose title promises plans gets a section or elevation first.
            var planTitled = nonEmpty.FirstOrDefault(s => Contains(SafeName(s), "plan")) ?? nonEmpty[0];
            var sectionLike = pool.FirstOrDefault(v => v.ViewType == ViewType.Section || v.ViewType == ViewType.Elevation);
            if (sectionLike != null)
            {
                pool.Remove(sectionLike);
                pool.Insert(0, sectionLike);
            }
            var ordered = new List<ViewSheet> { planTitled };
            ordered.AddRange(nonEmpty.Where(s => s.Id != planTitled.Id));

            var tooCloseDone = false;
            var tooCloseIds = new List<long>();
            var jitteredIds = new List<long>();
            var misleadingReported = false;

            foreach (var sheet in ordered)
            {
                var perSheet = rnd.NextIntInclusive(1, 3);
                // The first sheet always carries at least two viewports so the too-close pair exists.
                if (sheet.Id == planTitled.Id) perSheet = Math.Max(perSheet, 2);
                XYZ previousCentre = null;
                Viewport previousViewport = null;

                for (var j = 0; j < perSheet && pool.Count > 0; j++)
                {
                    var view = pool[0];
                    pool.RemoveAt(0);

                    var slot = ViewportSlot(j);
                    var jx = rnd.NextJitter(ViewportJitterFt);
                    var jy = rnd.NextJitter(ViewportJitterFt);
                    var plantTooClose = !tooCloseDone && j == 1 && previousCentre != null;
                    var centre = plantTooClose
                        ? previousCentre + new XYZ(ViewportsTooCloseFt, 0, 0)
                        : new XYZ(slot.X + jx, slot.Y + jy, 0);

                    Viewport viewport = null;
                    try
                    {
                        viewport = factory.PlaceViewport(sheet, view, centre);
                    }
                    catch (Exception ex)
                    {
                        report.AddException(Id, $"place '{SafeName(view)}' on sheet {SafeNumber(sheet)}", ex, rolledBack: false);
                    }
                    if (viewport == null) continue; // the view cannot go on a sheet (or is already on one); move on

                    w.ViewportCount++;
                    if (plantTooClose)
                    {
                        tooCloseDone = true;
                        if (previousViewport != null) tooCloseIds.Add(previousViewport.Id.Value);
                        tooCloseIds.Add(viewport.Id.Value);
                    }
                    else
                    {
                        jitteredIds.Add(viewport.Id.Value);
                    }

                    if (!misleadingReported && sectionLike != null && view.Id == sectionLike.Id && sheet.Id == planTitled.Id)
                    {
                        misleadingReported = true;
                        report.AddDefect(Id,
                            $"{view.ViewType} '{SafeName(view)}' placed on sheet {SafeNumber(sheet)} '{SafeName(sheet)}' — the sheet title does not describe its content.",
                            new[] { sheet.Id.Value, viewport.Id.Value });
                    }

                    previousCentre = centre;
                    previousViewport = viewport;
                }
            }

            if (tooCloseIds.Count > 1)
                report.AddDefect(Id, $"Two viewports placed {ViewportsTooCloseFt} ft apart on sheet {SafeNumber(ordered[0])} (viewports too close together / overlapping).", tooCloseIds);
            if (jitteredIds.Count > 0)
                report.AddDefect(Id, $"{jitteredIds.Count} viewport(s) placed with random offsets of up to {ViewportJitterFt} ft from the layout grid (inconsistent alignment).", jitteredIds);
        }

        private static XYZ ViewportSlot(int index)
        {
            switch (index % 4)
            {
                case 0: return new XYZ(0.6, 0.6, 0);
                case 1: return new XYZ(1.6, 0.6, 0);
                case 2: return new XYZ(0.6, 1.4, 0);
                default: return new XYZ(1.6, 1.4, 0);
            }
        }

        private static List<View> EligibleForSheets(Work w)
        {
            var list = new List<View>();
            list.AddRange(w.BaselinePlans.Select(e => e.View));
            list.AddRange(w.DuplicatePlans.Select(e => e.View));
            list.AddRange(w.Sections);
            list.AddRange(w.Elevations);
            list.AddRange(w.ThreeD);
            list.AddRange(w.Drafting);
            return list.Where(v => v != null && v.IsValidObject).ToList();
        }

        // ---- Text notes --------------------------------------------------------------------

        private void CreateTextNotes(Work w)
        {
            var ctx = w.Context;
            var report = ctx.Report;
            var factory = ctx.Factory;
            var profile = ctx.Profile;
            var rnd = w.Notes;
            var fp = w.Footprint;

            var count = profile.TextNotes;
            if (count <= 0) return;
            if (ctx.Types.TextNoteTypes.Count == 0)
            {
                report.AddFallback(Id, "The document has no text note types; text notes skipped.");
                return;
            }

            var planEntries = w.BaselinePlans.Concat(w.DuplicatePlans).Where(e => e.View != null && e.View.IsValidObject).ToList();
            var texts = rnd.TakeCycling(BadNames.TextNotes, count);
            var draftingNote = w.Drafting.Count > 0;
            var onPlans = draftingNote ? count - 1 : count;

            var ids = new List<long>();
            var overlapIds = new List<long>();
            XYZ overlapBase = null;
            PlanEntry overlapEntry = null;

            for (var i = 0; i < onPlans && planEntries.Count > 0; i++)
            {
                var entry = planEntries[rnd.NextInt(0, planEntries.Count)];
                var x = rnd.NextDouble(fp.MinX + NoteInsetMm, fp.MaxX - NoteInsetMm);
                var y = rnd.NextDouble(fp.MinY + NoteInsetMm, fp.MaxY - NoteInsetMm);
                var dx = rnd.NextJitter(NoteOverlapJitterMm);
                var dy = rnd.NextJitter(NoteOverlapJitterMm);

                XYZ position;
                var overlapping = i == 1 && overlapBase != null && overlapEntry != null;
                if (overlapping)
                {
                    entry = overlapEntry;
                    position = overlapBase + new XYZ(UnitConversion.MmToFeet(dx), UnitConversion.MmToFeet(dy), 0);
                }
                else
                {
                    position = UnitConversion.ToXYZAtFeet(new Point2D(x, y), entry.Level != null ? entry.Level.ProjectElevation : 0.0);
                }

                try
                {
                    var note = factory.CreateTextNote(entry.View, position, texts[i]);
                    if (note == null)
                    {
                        report.AddFallback(Id, "Text note type unavailable; text notes skipped.");
                        return;
                    }
                    ids.Add(note.Id.Value);
                    w.TextNoteCount++;
                    if (i == 0)
                    {
                        overlapBase = position;
                        overlapEntry = entry;
                        overlapIds.Add(note.Id.Value);
                    }
                    else if (overlapping)
                    {
                        overlapIds.Add(note.Id.Value);
                    }
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create text note '{texts[i]}' on '{SafeName(entry.View)}'", ex, rolledBack: false);
                }
            }

            long? draftingNoteId = null;
            if (draftingNote)
            {
                var view = w.Drafting[0];
                try
                {
                    var note = factory.CreateTextNote(view, XYZ.Zero, texts[count - 1]);
                    if (note != null)
                    {
                        ids.Add(note.Id.Value);
                        draftingNoteId = note.Id.Value;
                        w.TextNoteCount++;
                    }
                }
                catch (Exception ex)
                {
                    report.AddException(Id, $"create text note on drafting view '{SafeName(view)}'", ex, rolledBack: false);
                }
            }

            if (ids.Count > 0)
                report.AddDefect(Id, $"{ids.Count} placeholder text note(s) ({string.Join(", ", texts.Take(ids.Count).Select(t => "'" + t + "'"))}).", ids);
            if (overlapIds.Count > 1)
                report.AddDefect(Id, $"Two text notes overlap (within {NoteOverlapJitterMm:0} mm of each other) on '{SafeName(overlapEntry?.View)}'.", overlapIds);
            if (draftingNoteId.HasValue)
                report.AddDefect(Id, $"Drafting view '{SafeName(w.Drafting[0])}' contains only a placeholder note.", new[] { draftingNoteId.Value });
        }

        // ---- Helpers -----------------------------------------------------------------------

        private bool CanAddView(Work w)
        {
            if (w.Context.Views.Count < GenerationLimits.MaxViews) return true;
            if (!w.ViewCapReported)
            {
                w.Context.Report.AddInfo(Id, $"View cap of {GenerationLimits.MaxViews} reached; further views skipped.");
                w.ViewCapReported = true;
            }
            return false;
        }

        private static Level SafeGenLevel(View view)
        {
            try { return view?.GenLevel; } catch { return null; }
        }

        private static string SafeName(Element element)
        {
            try { return element?.Name ?? string.Empty; } catch { return string.Empty; }
        }

        private static string SafeNumber(ViewSheet sheet)
        {
            try { return sheet?.SheetNumber ?? string.Empty; } catch { return string.Empty; }
        }

        private static string Describe(IEnumerable<ViewSheet> sheets) =>
            string.Join(", ", sheets.Select(s => $"{SafeNumber(s)} '{SafeName(s)}'"));

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>A generated plan view and the level it documents.</summary>
        private sealed class PlanEntry
        {
            public PlanEntry(View view, Level level)
            {
                View = view;
                Level = level;
            }

            public View View { get; }
            public Level Level { get; }
        }

        /// <summary>Per-run working state, so the steps can share what they created.</summary>
        private sealed class Work
        {
            public GenerationContext Context;
            public RandomStream Views;
            public RandomStream Sheets;
            public RandomStream Notes;
            public bool Naming;
            public Rect2D Footprint;
            public bool ViewCapReported;

            public readonly List<PlanEntry> BaselinePlans = new List<PlanEntry>();
            public readonly List<PlanEntry> DuplicatePlans = new List<PlanEntry>();
            public readonly List<View> Sections = new List<View>();
            public readonly List<View> Elevations = new List<View>();
            public readonly List<View> ThreeD = new List<View>();
            public readonly List<View> Drafting = new List<View>();
            public readonly List<ViewSheet> SheetList = new List<ViewSheet>();
            public int EmptySheetCount;
            public int ViewportCount;
            public int TextNoteCount;
        }
    }
}
