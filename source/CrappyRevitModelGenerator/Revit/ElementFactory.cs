using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Geometry;
using CrappyRevitModelGenerator.Core.Planning;
using RevitExceptions = Autodesk.Revit.Exceptions;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// The only place elements are created. Every method converts units, calls the API,
    /// registers the new element (tagging it with the run identity) and returns it. Methods
    /// must be called inside a scenario transaction. They throw on API failure — the
    /// coordinator turns that into a rolled-back scenario with the exception in the report;
    /// scenarios that want to survive an individual failure wrap the call themselves.
    /// </summary>
    public sealed class ElementFactory
    {
        private readonly GenerationContext _ctx;

        public ElementFactory(GenerationContext ctx)
        {
            _ctx = ctx ?? throw new ArgumentNullException(nameof(ctx));
        }

        private Document Doc => _ctx.Document;

        // ---- Registration ----------------------------------------------------------------

        /// <summary>Register an element created outside the factory (e.g. a title block a sheet created).</summary>
        public T Register<T>(T element, GeneratedCategory category) where T : Element
        {
            if (element != null) _ctx.Registry.Register(element, category);
            return element;
        }

        // ---- Datum -----------------------------------------------------------------------

        public Level CreateLevel(LevelSpec spec)
        {
            var level = Level.Create(Doc, UnitConversion.MmToFeet(spec.ElevationMm));
            TrySetName(level, spec.CleanName);
            Register(level, GeneratedCategory.Levels);
            _ctx.Levels[spec.Index] = level;
            return level;
        }

        public Grid CreateGrid(GridSpec spec)
        {
            var grid = Grid.Create(Doc, UnitConversion.ToLine(spec.Line));
            TrySetName(grid, spec.CleanName);
            Register(grid, GeneratedCategory.Grids);
            _ctx.Grids[spec.Index] = grid;
            return grid;
        }

        // ---- Walls and floors ------------------------------------------------------------

        public WallType WallTypeFor(WallSpec spec)
        {
            switch (spec.Role)
            {
                case WallRole.Exterior:
                    return _ctx.Types.ExteriorWallType;
                default:
                    return spec.TypeChoice == 1 ? _ctx.Types.AlternateInteriorWallType : _ctx.Types.InteriorWallType;
            }
        }

        public Wall CreateWall(WallSpec spec, Level level, Level levelAbove)
        {
            var wallType = WallTypeFor(spec) ?? throw new InvalidOperationException("No usable wall type in the document.");
            var line = UnitConversion.ToLineAtFeet(spec.Line, level.ProjectElevation);
            var height = UnitConversion.MmToFeet(spec.HeightMm);

            var wall = Wall.Create(Doc, line, wallType.Id, level.Id, height, 0.0, false, false);

            if (spec.AttachTopToLevelAbove && levelAbove != null)
                TrySet(wall, BuiltInParameter.WALL_HEIGHT_TYPE, levelAbove.Id);

            if (spec.LocationLineChoice > 0)
                TrySet(wall, BuiltInParameter.WALL_KEY_REF_PARAM, spec.LocationLineChoice);

            if (!spec.IsRoomBounding)
                TrySet(wall, BuiltInParameter.WALL_ATTR_ROOM_BOUNDING, 0);

            if ((spec.DisallowJoinMask & 1) != 0) WallUtils.DisallowWallJoinAtEnd(wall, 0);
            if ((spec.DisallowJoinMask & 2) != 0) WallUtils.DisallowWallJoinAtEnd(wall, 1);

            Register(wall, GeneratedCategory.Walls);
            _ctx.Walls[spec.Index] = wall;
            return wall;
        }

        /// <summary>A wall from an arbitrary plan segment (used by the warnings scenario for overlaps).</summary>
        public Wall CreateWall(Segment2D line, WallType type, Level level, double heightMm)
        {
            var wall = Wall.Create(Doc, UnitConversion.ToLineAtFeet(line, level.ProjectElevation), type.Id, level.Id, UnitConversion.MmToFeet(heightMm), 0.0, false, false);
            Register(wall, GeneratedCategory.Walls);
            return wall;
        }

        public Floor CreateFloor(FloorSpec spec, Level level, FloorType floorType = null)
        {
            floorType ??= _ctx.Types.FloorType ?? throw new InvalidOperationException("No usable floor type in the document.");
            var loop = UnitConversion.ToCurveLoopAtFeet(spec.Loop, level.ProjectElevation);
            var loops = new List<CurveLoop> { loop };
            if (!BoundaryValidation.IsValidHorizontalBoundary(loops))
                throw new InvalidOperationException($"Floor {spec.Index} profile is not a valid horizontal boundary.");

            var floor = Floor.Create(Doc, loops, floorType.Id, level.Id);
            Register(floor, GeneratedCategory.Floors);
            _ctx.Floors[spec.Index] = floor;
            return floor;
        }

        public Floor CreateFloor(IReadOnlyList<Point2D> loopMm, Level level, FloorType floorType)
        {
            var loops = new List<CurveLoop> { UnitConversion.ToCurveLoopAtFeet(loopMm, level.ProjectElevation) };
            if (!BoundaryValidation.IsValidHorizontalBoundary(loops))
                throw new InvalidOperationException("Floor profile is not a valid horizontal boundary.");
            var floor = Floor.Create(Doc, loops, floorType.Id, level.Id);
            Register(floor, GeneratedCategory.Floors);
            return floor;
        }

        // ---- Views -----------------------------------------------------------------------

        public ViewPlan CreateFloorPlan(Level level, string name, int levelIndex)
        {
            var vft = _ctx.Types.FloorPlanType ?? throw new InvalidOperationException("No floor plan view family type in the document.");
            var view = ViewPlan.Create(Doc, vft.Id, level.Id);
            TrySetName(view, name);
            Register(view, GeneratedCategory.Views);
            _ctx.Views.Add(view);
            _ctx.PlanViews[levelIndex] = view;
            return view;
        }

        public ViewPlan CreateCeilingPlan(Level level, string name)
        {
            var vft = _ctx.Types.CeilingPlanType;
            if (vft == null) return null;
            var view = ViewPlan.Create(Doc, vft.Id, level.Id);
            TrySetName(view, name);
            Register(view, GeneratedCategory.Views);
            _ctx.Views.Add(view);
            return view;
        }

        public View DuplicateView(View source, ViewDuplicateOption option, string name)
        {
            if (!source.CanViewBeDuplicated(option)) return null;
            var id = source.Duplicate(option);
            var dup = Doc.GetElement(id) as View;
            if (dup == null) return null;
            TrySetName(dup, name);
            Register(dup, GeneratedCategory.Views);
            _ctx.Views.Add(dup);
            return dup;
        }

        public ViewSection CreateSection(BoundingBoxXYZ box, string name)
        {
            var vft = _ctx.Types.SectionType;
            if (vft == null) return null;
            var view = ViewSection.CreateSection(Doc, vft.Id, box);
            TrySetName(view, name);
            Register(view, GeneratedCategory.Views);
            _ctx.Views.Add(view);
            return view;
        }

        /// <summary>An elevation marker plus one elevation view looking in the given index direction (0..3).</summary>
        public ViewSection CreateElevation(XYZ markerPoint, ViewPlan planView, int index, string name)
        {
            var vft = _ctx.Types.ElevationType;
            if (vft == null || planView == null) return null;
            var marker = ElevationMarker.CreateElevationMarker(Doc, vft.Id, markerPoint, planView.Scale);
            Register(marker, GeneratedCategory.Other);
            var view = marker.CreateElevation(Doc, planView.Id, index);
            TrySetName(view, name);
            Register(view, GeneratedCategory.Views);
            _ctx.Views.Add(view);
            return view;
        }

        public View3D CreateIsometric(string name)
        {
            var vft = _ctx.Types.ThreeDType;
            if (vft == null) return null;
            var view = View3D.CreateIsometric(Doc, vft.Id);
            TrySetName(view, name);
            Register(view, GeneratedCategory.Views);
            _ctx.Views.Add(view);
            return view;
        }

        public ViewDrafting CreateDrafting(string name)
        {
            var vft = _ctx.Types.DraftingType;
            if (vft == null) return null;
            var view = ViewDrafting.Create(Doc, vft.Id);
            TrySetName(view, name);
            Register(view, GeneratedCategory.Views);
            _ctx.Views.Add(view);
            return view;
        }

        public ViewSheet CreateSheet(string number, string name, bool withTitleBlock = true)
        {
            var titleBlock = withTitleBlock ? _ctx.Types.TitleBlockSymbol : null;
            var sheet = ViewSheet.Create(Doc, titleBlock?.Id ?? ElementId.InvalidElementId);
            TrySetSheetNumber(sheet, number);
            TrySetName(sheet, name);
            Register(sheet, GeneratedCategory.Sheets);
            _ctx.Sheets.Add(sheet);

            // The title block instance the sheet created belongs to the run too.
            foreach (var tb in new FilteredElementCollector(Doc, sheet.Id).OfCategory(BuiltInCategory.OST_TitleBlocks).WhereElementIsNotElementType())
                Register(tb, GeneratedCategory.Other);

            return sheet;
        }

        public Viewport PlaceViewport(ViewSheet sheet, View view, XYZ centre)
        {
            if (sheet == null || view == null) return null;
            if (!Viewport.CanAddViewToSheet(Doc, sheet.Id, view.Id)) return null;
            var vp = Viewport.Create(Doc, sheet.Id, view.Id, centre);
            if (vp == null) return null;
            Register(vp, GeneratedCategory.Viewports);
            _ctx.Viewports.Add(vp);
            return vp;
        }

        public TextNote CreateTextNote(View view, XYZ position, string text)
        {
            var type = _ctx.Types.TextNoteType;
            if (type == null || view == null) return null;
            var note = TextNote.Create(Doc, view.Id, position, text ?? string.Empty, type.Id);
            Register(note, GeneratedCategory.TextNotes);
            _ctx.TextNotes.Add(note);
            return note;
        }

        // ---- Hosted and free instances ---------------------------------------------------

        public FamilyInstance PlaceHosted(FamilySymbol symbol, Wall host, Level level, XYZ location, GeneratedCategory category)
        {
            _ctx.Types.EnsureActive(symbol);
            var instance = Doc.Create.NewFamilyInstance(location, symbol, host, level, StructuralType.NonStructural);
            Register(instance, category);
            return instance;
        }

        public FamilyInstance PlaceFree(FamilySymbol symbol, Level level, XYZ location, double rotationRadians, GeneratedCategory category)
        {
            _ctx.Types.EnsureActive(symbol);
            var instance = Doc.Create.NewFamilyInstance(location, symbol, level, StructuralType.NonStructural);
            if (Math.Abs(rotationRadians) > 1e-9)
            {
                var axis = Line.CreateBound(location, location + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(Doc, instance.Id, axis, rotationRadians);
            }
            Register(instance, category);
            return instance;
        }

        // ---- Rooms -----------------------------------------------------------------------

        public Room CreateRoom(Level level, UV location)
        {
            var room = Doc.Create.NewRoom(level, location);
            Register(room, GeneratedCategory.Rooms);
            return room;
        }

        public Room CreateUnplacedRoom(Phase phase)
        {
            phase ??= _ctx.Types.DefaultPhase ?? throw new InvalidOperationException("The document has no phases; an unplaced room needs one.");
            var room = Doc.Create.NewRoom(phase);
            Register(room, GeneratedCategory.Rooms);
            return room;
        }

        public RoomTag CreateRoomTag(Room room, UV location, View planView)
        {
            // NewRoomTag is documented to return null when the tag cannot be created (e.g. a
            // phase mismatch between the room and the view); never let a null into the context list.
            var tag = Doc.Create.NewRoomTag(new LinkElementId(room.Id), location, planView.Id);
            if (tag == null) return null;
            Register(tag, GeneratedCategory.RoomTags);
            _ctx.RoomTags.Add(tag);
            return tag;
        }

        public IList<ModelCurve> CreateRoomSeparationLines(ViewPlan planView, Level level, IEnumerable<Segment2D> segments)
        {
            var sketchPlane = SketchPlane.Create(Doc, level.Id);
            Register(sketchPlane, GeneratedCategory.Other);

            var curves = new CurveArray();
            foreach (var s in segments) curves.Append(UnitConversion.ToLineAtFeet(s, level.ProjectElevation));

            var created = Doc.Create.NewRoomBoundaryLines(sketchPlane, curves, planView);
            var result = new List<ModelCurve>();
            foreach (ModelCurve mc in created)
            {
                Register(mc, GeneratedCategory.RoomSeparationLines);
                _ctx.SeparationLines.Add(mc);
                result.Add(mc);
            }
            return result;
        }

        // ---- Types and materials ---------------------------------------------------------

        public Material CreateMaterial(string desiredName)
        {
            Material material = null;
            string applied = null;
            foreach (var candidate in NameSanitizer.Candidates(desiredName).Take(50))
            {
                try
                {
                    var id = Material.Create(Doc, candidate);
                    material = Doc.GetElement(id) as Material;
                    applied = candidate;
                    break;
                }
                catch (RevitExceptions.ArgumentException)
                {
                    // Name in use or rejected; try the next candidate.
                }
            }
            if (material == null) return null;
            Register(material, GeneratedCategory.Materials);
            _ctx.Materials.Add(material);
            return material;
        }

        public Material DuplicateMaterial(Material source, string desiredName)
        {
            if (source == null) return null;
            Material material = null;
            foreach (var candidate in NameSanitizer.Candidates(desiredName).Take(50))
            {
                try
                {
                    material = source.Duplicate(candidate);
                    break;
                }
                catch (RevitExceptions.ArgumentException)
                {
                }
            }
            if (material == null) return null;
            Register(material, GeneratedCategory.Materials);
            _ctx.Materials.Add(material);
            return material;
        }

        public ElementType DuplicateType(ElementType source, string desiredName)
        {
            if (source == null) return null;
            ElementType dup = null;
            foreach (var candidate in NameSanitizer.Candidates(desiredName).Take(50))
            {
                try
                {
                    dup = source.Duplicate(candidate);
                    break;
                }
                catch (RevitExceptions.ArgumentException)
                {
                }
            }
            if (dup == null) return null;
            Register(dup, GeneratedCategory.Types);
            _ctx.DuplicatedTypes.Add(dup);
            return dup;
        }

        // ---- Names and parameters --------------------------------------------------------

        /// <summary>
        /// Try the desired name, then the bad-suffix candidates, until Revit accepts one. Returns
        /// the applied name or null when every candidate was rejected (the element keeps its
        /// current name; nothing throws).
        /// </summary>
        public string TrySetName(Element element, string desired, int maxAttempts = 40)
        {
            if (element == null || desired == null) return null;
            var attempts = 0;
            foreach (var candidate in NameSanitizer.Candidates(desired))
            {
                if (attempts++ >= maxAttempts) break;
                try
                {
                    element.Name = candidate;
                    return candidate;
                }
                catch (RevitExceptions.ArgumentException)
                {
                    // In use or illegal for this element; try the next.
                }
                catch (RevitExceptions.InvalidOperationException)
                {
                    return null; // this element's name cannot be changed at all
                }
            }
            return null;
        }

        public string TrySetSheetNumber(ViewSheet sheet, string desired, int maxAttempts = 40)
        {
            if (sheet == null || desired == null) return null;
            var attempts = 0;
            foreach (var candidate in NameSanitizer.Candidates(desired))
            {
                if (attempts++ >= maxAttempts) break;
                try
                {
                    sheet.SheetNumber = candidate;
                    return candidate;
                }
                catch (RevitExceptions.ArgumentException)
                {
                }
                catch (RevitExceptions.InvalidOperationException)
                {
                    return null;
                }
            }
            return null;
        }

        public bool TrySet(Element element, BuiltInParameter bip, string value)
        {
            var p = element?.get_Parameter(bip);
            return TrySet(p, value);
        }

        public bool TrySet(Element element, string parameterName, string value)
        {
            var p = element?.LookupParameter(parameterName);
            return TrySet(p, value);
        }

        public bool TrySet(Element element, BuiltInParameter bip, int value)
        {
            var p = element?.get_Parameter(bip);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Integer) return false;
            try { return p.Set(value); } catch (RevitExceptions.ApplicationException) { return false; }
        }

        public bool TrySet(Element element, BuiltInParameter bip, double valueInternalUnits)
        {
            var p = element?.get_Parameter(bip);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.Double) return false;
            try { return p.Set(valueInternalUnits); } catch (RevitExceptions.ApplicationException) { return false; }
        }

        public bool TrySet(Element element, BuiltInParameter bip, ElementId value)
        {
            var p = element?.get_Parameter(bip);
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.ElementId) return false;
            try { return p.Set(value); } catch (RevitExceptions.ApplicationException) { return false; }
        }

        private static bool TrySet(Parameter p, string value)
        {
            if (p == null || p.IsReadOnly || p.StorageType != StorageType.String) return false;
            try { return p.Set(value ?? string.Empty); } catch (RevitExceptions.ApplicationException) { return false; }
        }
    }
}
