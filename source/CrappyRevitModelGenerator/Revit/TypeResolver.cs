using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// Discovers usable types in the active document and chooses documented fallbacks (plan
    /// section 9.1). Never hard-codes element ids; every choice is deterministic (ordinal name
    /// order) so the same document gives the same picks; every fallback goes into the report.
    /// A null result means "the document has nothing usable" and the scenario must skip that
    /// content and say so.
    /// </summary>
    public sealed class TypeResolver
    {
        public const string ReportScenarioId = "type-resolver";

        private readonly Document _doc;
        private readonly GenerationReport _report;
        private readonly HashSet<long> _activated = new HashSet<long>();

        private List<WallType> _basicWallTypes;
        private List<FloorType> _floorTypes;
        private List<FamilySymbol> _doorSymbols, _windowSymbols, _furnitureSymbols, _titleBlockSymbols;
        private Dictionary<ViewFamily, List<ViewFamilyType>> _viewFamilyTypes;
        private List<TextNoteType> _textNoteTypes;
        private List<Material> _materials;
        private List<View> _viewTemplates;

        public TypeResolver(Document doc, GenerationReport report)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        // ---- Walls -----------------------------------------------------------------------

        public IReadOnlyList<WallType> BasicWallTypes => _basicWallTypes ??= new FilteredElementCollector(_doc)
            .OfClass(typeof(WallType))
            .Cast<WallType>()
            .Where(t => t.Kind == WallKind.Basic)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        private WallType _exteriorWallType, _interiorWallType, _alternateInteriorWallType;
        private bool _wallsResolved;

        public WallType ExteriorWallType { get { ResolveWalls(); return _exteriorWallType; } }
        public WallType InteriorWallType { get { ResolveWalls(); return _interiorWallType; } }
        public WallType AlternateInteriorWallType { get { ResolveWalls(); return _alternateInteriorWallType; } }

        private void ResolveWalls()
        {
            if (_wallsResolved) return;
            _wallsResolved = true;

            var types = BasicWallTypes;
            if (types.Count == 0)
            {
                _report.AddFallback(ReportScenarioId, "The document has no basic wall types; walls cannot be created.");
                return;
            }

            double Mm(WallType t) => UnitConversion.FeetToMm(t.Width);

            _exteriorWallType =
                types.FirstOrDefault(t => Contains(t.Name, "Exterior") && Mm(t) >= 150 && Mm(t) <= 450)
                ?? types.FirstOrDefault(t => Contains(t.Name, "Generic") && Mm(t) >= 180 && Mm(t) <= 320)
                ?? types.Where(t => Mm(t) >= 150 && Mm(t) <= 450).OrderByDescending(Mm).ThenBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault()
                ?? types.OrderByDescending(Mm).ThenBy(t => t.Name, StringComparer.Ordinal).First();

            _interiorWallType =
                types.FirstOrDefault(t => (Contains(t.Name, "Interior") || Contains(t.Name, "Partition")) && Mm(t) >= 70 && Mm(t) <= 180)
                ?? types.FirstOrDefault(t => Contains(t.Name, "Generic") && Mm(t) >= 70 && Mm(t) <= 160)
                ?? types.Where(t => Mm(t) >= 70 && Mm(t) <= 180).OrderBy(Mm).ThenBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault()
                ?? types.OrderBy(Mm).ThenBy(t => t.Name, StringComparer.Ordinal).First();

            var interiorWidth = Mm(_interiorWallType);
            _alternateInteriorWallType =
                types.Where(t => t.Id != _interiorWallType.Id && Math.Abs(Mm(t) - interiorWidth) >= 25 && Mm(t) >= 70 && Mm(t) <= 300)
                     .OrderBy(t => Math.Abs(Mm(t) - interiorWidth)).ThenBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault()
                ?? (_exteriorWallType.Id != _interiorWallType.Id ? _exteriorWallType : _interiorWallType);

            if (_exteriorWallType.Id == _interiorWallType.Id)
                _report.AddFallback(ReportScenarioId, $"Only one usable basic wall type ('{_interiorWallType.Name}'); exterior and interior walls share it.");
            if (_alternateInteriorWallType.Id == _interiorWallType.Id)
                _report.AddFallback(ReportScenarioId, "No second wall type with a different thickness; the 'different type for no reason' defect is skipped.");

            _report.AddInfo(ReportScenarioId, $"Wall types: exterior '{_exteriorWallType.Name}', interior '{_interiorWallType.Name}', alternate '{_alternateInteriorWallType.Name}'.");
        }

        // ---- Floors ----------------------------------------------------------------------

        public IReadOnlyList<FloorType> FloorTypes => _floorTypes ??= new FilteredElementCollector(_doc)
            .OfClass(typeof(FloorType))
            .Cast<FloorType>()
            .Where(t => !t.IsFoundationSlab)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        private FloorType _floorType, _alternateFloorType;
        private bool _floorsResolved;

        public FloorType FloorType { get { ResolveFloors(); return _floorType; } }
        public FloorType AlternateFloorType { get { ResolveFloors(); return _alternateFloorType; } }

        private void ResolveFloors()
        {
            if (_floorsResolved) return;
            _floorsResolved = true;

            var types = FloorTypes;
            if (types.Count == 0)
            {
                _report.AddFallback(ReportScenarioId, "The document has no floor types (other than foundation slabs); floors are skipped.");
                return;
            }

            _floorType = types.FirstOrDefault(t => Contains(t.Name, "Generic"))
                         ?? types.FirstOrDefault(t => Contains(t.Name, "Concrete"))
                         ?? types.First();
            _alternateFloorType = types.FirstOrDefault(t => t.Id != _floorType.Id) ?? _floorType;

            _report.AddInfo(ReportScenarioId, $"Floor type: '{_floorType.Name}'" + (_alternateFloorType.Id != _floorType.Id ? $", alternate '{_alternateFloorType.Name}'." : "."));
        }

        // ---- Family symbols --------------------------------------------------------------

        private List<FamilySymbol> Symbols(BuiltInCategory category) => new FilteredElementCollector(_doc)
            .OfClass(typeof(FamilySymbol))
            .OfCategory(category)
            .Cast<FamilySymbol>()
            .Where(s => s.Family != null && !s.Family.IsInPlace)
            .OrderBy(s => s.Family.Name, StringComparer.Ordinal)
            .ThenBy(s => s.Name, StringComparer.Ordinal)
            .ToList();

        public IReadOnlyList<FamilySymbol> DoorSymbols => _doorSymbols ??= Symbols(BuiltInCategory.OST_Doors)
            .Where(s => !Contains(s.Family.Name, "Curtain")).ToList();

        public IReadOnlyList<FamilySymbol> WindowSymbols => _windowSymbols ??= Symbols(BuiltInCategory.OST_Windows)
            .Where(s => !Contains(s.Family.Name, "Curtain")).ToList();

        public IReadOnlyList<FamilySymbol> FurnitureSymbols => _furnitureSymbols ??= Symbols(BuiltInCategory.OST_Furniture);

        public IReadOnlyList<FamilySymbol> TitleBlockSymbols => _titleBlockSymbols ??= Symbols(BuiltInCategory.OST_TitleBlocks);

        private FamilySymbol _doorSymbol, _windowSymbol, _titleBlockSymbol;
        private List<FamilySymbol> _furniturePicks;
        private bool _symbolsResolved;

        /// <summary>A single-leaf door type when the document has one; null when there are no door families at all.</summary>
        public FamilySymbol DoorSymbol { get { ResolveSymbols(); return _doorSymbol; } }
        public FamilySymbol WindowSymbol { get { ResolveSymbols(); return _windowSymbol; } }
        public FamilySymbol TitleBlockSymbol { get { ResolveSymbols(); return _titleBlockSymbol; } }

        /// <summary>Up to four furniture types from different families, for variety.</summary>
        public IReadOnlyList<FamilySymbol> FurniturePicks { get { ResolveSymbols(); return _furniturePicks; } }

        private void ResolveSymbols()
        {
            if (_symbolsResolved) return;
            _symbolsResolved = true;

            _doorSymbol = DoorSymbols.FirstOrDefault(s => Contains(s.Family.Name, "Single") && !Contains(s.Family.Name, "Double"))
                          ?? DoorSymbols.FirstOrDefault(s => !Contains(s.Family.Name, "Double") && !Contains(s.Family.Name, "Overhead") && !Contains(s.Family.Name, "Garage"))
                          ?? DoorSymbols.FirstOrDefault();
            if (_doorSymbol == null) _report.AddFallback(ReportScenarioId, "No door families are loaded; doors are skipped.");
            else _report.AddInfo(ReportScenarioId, $"Door type: '{_doorSymbol.Family.Name} : {_doorSymbol.Name}'.");

            _windowSymbol = WindowSymbols.FirstOrDefault(s => Contains(s.Family.Name, "Fixed"))
                            ?? WindowSymbols.FirstOrDefault(s => !Contains(s.Family.Name, "Skylight"))
                            ?? WindowSymbols.FirstOrDefault();
            if (_windowSymbol == null) _report.AddFallback(ReportScenarioId, "No window families are loaded; windows are skipped.");
            else _report.AddInfo(ReportScenarioId, $"Window type: '{_windowSymbol.Family.Name} : {_windowSymbol.Name}'.");

            var preferred = new[] { "Desk", "Chair", "Table", "Credenza", "Sofa", "Cabinet", "Bookcase" };
            _furniturePicks = FurnitureSymbols
                .GroupBy(s => s.Family.Id.Value)
                .Select(g => g.First())
                .OrderBy(s => preferred.Any(p => Contains(s.Family.Name, p)) ? 0 : 1)
                .ThenBy(s => s.Family.Name, StringComparer.Ordinal)
                .Take(4)
                .ToList();
            if (_furniturePicks.Count == 0) _report.AddFallback(ReportScenarioId, "No furniture families are loaded; furniture is skipped.");
            else _report.AddInfo(ReportScenarioId, "Furniture types: " + string.Join(", ", _furniturePicks.Select(s => $"'{s.Family.Name} : {s.Name}'")) + ".");

            _titleBlockSymbol = TitleBlockSymbols.FirstOrDefault();
            if (_titleBlockSymbol == null) _report.AddFallback(ReportScenarioId, "No title block families are loaded; sheets are created without a title block.");
            else _report.AddInfo(ReportScenarioId, $"Title block: '{_titleBlockSymbol.Family.Name} : {_titleBlockSymbol.Name}'.");
        }

        /// <summary>Activate a symbol before first placement (required by the API) — once per symbol per run.</summary>
        public void EnsureActive(FamilySymbol symbol)
        {
            if (symbol == null) return;
            if (_activated.Contains(symbol.Id.Value)) return;
            if (!symbol.IsActive)
            {
                symbol.Activate();
                _doc.Regenerate();
            }
            _activated.Add(symbol.Id.Value);
        }

        // ---- Views -----------------------------------------------------------------------

        private Dictionary<ViewFamily, List<ViewFamilyType>> ViewFamilyTypes => _viewFamilyTypes ??= new FilteredElementCollector(_doc)
            .OfClass(typeof(ViewFamilyType))
            .Cast<ViewFamilyType>()
            .GroupBy(t => t.ViewFamily)
            .ToDictionary(g => g.Key, g => g.OrderBy(t => t.Name, StringComparer.Ordinal).ToList());

        public ViewFamilyType ViewFamilyTypeFor(ViewFamily family)
        {
            if (ViewFamilyTypes.TryGetValue(family, out var list) && list.Count > 0) return list[0];
            _report.AddFallback(ReportScenarioId, $"The document has no {family} view family type; those views are skipped.");
            return null;
        }

        public ViewFamilyType FloorPlanType => ViewFamilyTypeFor(ViewFamily.FloorPlan);
        public ViewFamilyType CeilingPlanType => ViewFamilyTypeFor(ViewFamily.CeilingPlan);
        public ViewFamilyType SectionType => ViewFamilyTypeFor(ViewFamily.Section);
        public ViewFamilyType ElevationType => ViewFamilyTypeFor(ViewFamily.Elevation);
        public ViewFamilyType ThreeDType => ViewFamilyTypeFor(ViewFamily.ThreeDimensional);
        public ViewFamilyType DraftingType => ViewFamilyTypeFor(ViewFamily.Drafting);

        /// <summary>Existing view templates in the document, ordinal by name.</summary>
        public IReadOnlyList<View> ViewTemplates => _viewTemplates ??= new FilteredElementCollector(_doc)
            .OfClass(typeof(View))
            .Cast<View>()
            .Where(v => v.IsTemplate)
            .OrderBy(v => v.Name, StringComparer.Ordinal)
            .ToList();

        // ---- Annotation ------------------------------------------------------------------

        public IReadOnlyList<TextNoteType> TextNoteTypes => _textNoteTypes ??= new FilteredElementCollector(_doc)
            .OfClass(typeof(TextNoteType))
            .Cast<TextNoteType>()
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .ToList();

        public TextNoteType TextNoteType
        {
            get
            {
                var t = TextNoteTypes.FirstOrDefault();
                if (t == null) _report.AddFallback(ReportScenarioId, "The document has no text note types; text notes are skipped.");
                return t;
            }
        }

        // ---- Materials and phases --------------------------------------------------------

        public IReadOnlyList<Material> Materials => _materials ??= new FilteredElementCollector(_doc)
            .OfClass(typeof(Material))
            .Cast<Material>()
            .OrderBy(m => m.Name, StringComparer.Ordinal)
            .ToList();

        /// <summary>The last phase in the document — what new elements get by default — or null.</summary>
        public Phase DefaultPhase
        {
            get
            {
                var phases = _doc.Phases;
                if (phases == null || phases.Size == 0) return null;
                Phase last = null;
                foreach (Phase p in phases) last = p;
                return last;
            }
        }

        // ---- Helpers ---------------------------------------------------------------------

        private static bool Contains(string haystack, string needle) =>
            !string.IsNullOrEmpty(haystack) && haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
