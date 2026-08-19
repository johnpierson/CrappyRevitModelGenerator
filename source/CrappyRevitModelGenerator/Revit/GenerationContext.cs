using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Planning;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// Everything a scenario needs (plan section 4): the document, settings, seeded random,
    /// report, registry, type resolver, element factory, the plans, and — filled in as
    /// scenarios run — the elements created so far, keyed by their plan indices so later
    /// scenarios can find "wall 7" or "the plan view for level 2" without searching.
    ///
    /// Lives in Revit/ rather than Core/ because it holds a <see cref="Document"/>.
    /// </summary>
    public sealed class GenerationContext
    {
        public GenerationContext(UIApplication uiApplication, Document document, GenerationSettings settings, GenerationReport report,
            GeneratedElementRegistry registry, FailureCapture failures, string runId, string generatorVersion, string revitVersion)
        {
            UIApplication = uiApplication;
            Document = document ?? throw new ArgumentNullException(nameof(document));
            Settings = settings ?? throw new ArgumentNullException(nameof(settings));
            Report = report ?? throw new ArgumentNullException(nameof(report));
            Registry = registry ?? throw new ArgumentNullException(nameof(registry));
            Failures = failures ?? throw new ArgumentNullException(nameof(failures));
            RunId = runId ?? throw new ArgumentNullException(nameof(runId));
            GeneratorVersion = generatorVersion ?? "0.0.0";
            RevitVersion = revitVersion ?? "unknown";

            Random = new SeededRandom(settings.Seed);
            Profile = SeverityProfile.For(settings.Severity);
            Types = new TypeResolver(document, report);
            Factory = new ElementFactory(this);
            EnabledScenarioIds = new HashSet<string>(settings.ResolveScenarioIds(), StringComparer.OrdinalIgnoreCase);
        }

        // ---- Services --------------------------------------------------------------------

        public UIApplication UIApplication { get; }
        public Document Document { get; }
        public GenerationSettings Settings { get; }
        public GenerationReport Report { get; }
        public GeneratedElementRegistry Registry { get; }
        public FailureCapture Failures { get; }
        public SeededRandom Random { get; }
        public SeverityProfile Profile { get; }
        public TypeResolver Types { get; }
        public ElementFactory Factory { get; }

        public string RunId { get; }
        public string GeneratorVersion { get; }
        public string RevitVersion { get; }

        public HashSet<string> EnabledScenarioIds { get; }
        public bool IsScenarioEnabled(string scenarioId) => EnabledScenarioIds.Contains(scenarioId);

        /// <summary>The scenario currently executing; the runner sets it before each scenario.</summary>
        public string CurrentScenarioId { get; set; }

        // ---- Plans -----------------------------------------------------------------------

        public BaselinePlan Baseline { get; set; }
        public ContentPlan Content { get; set; }
        public RoomPlan Rooms { get; set; }

        // ---- Created elements, by plan index ---------------------------------------------

        /// <summary>Levels by <see cref="LevelSpec.Index"/>.</summary>
        public Dictionary<int, Level> Levels { get; } = new Dictionary<int, Level>();

        /// <summary>Grids by <see cref="GridSpec.Index"/>.</summary>
        public Dictionary<int, Grid> Grids { get; } = new Dictionary<int, Grid>();

        /// <summary>Walls by <see cref="WallSpec.Index"/>.</summary>
        public Dictionary<int, Wall> Walls { get; } = new Dictionary<int, Wall>();

        /// <summary>Floors by <see cref="FloorSpec.Index"/>.</summary>
        public Dictionary<int, Floor> Floors { get; } = new Dictionary<int, Floor>();

        /// <summary>The floor plan the baseline created for each buildable level, by <see cref="LevelSpec.Index"/>.</summary>
        public Dictionary<int, ViewPlan> PlanViews { get; } = new Dictionary<int, ViewPlan>();

        /// <summary>Doors and windows by <see cref="OpeningSpec.Index"/>.</summary>
        public Dictionary<int, FamilyInstance> Openings { get; } = new Dictionary<int, FamilyInstance>();

        /// <summary>Furniture by <see cref="FurnitureSpec.Index"/>.</summary>
        public Dictionary<int, FamilyInstance> Furniture { get; } = new Dictionary<int, FamilyInstance>();

        /// <summary>Rooms by <see cref="RoomSpec.Index"/>.</summary>
        public Dictionary<int, Room> RoomElements { get; } = new Dictionary<int, Room>();

        public List<RoomTag> RoomTags { get; } = new List<RoomTag>();
        public List<ModelCurve> SeparationLines { get; } = new List<ModelCurve>();

        /// <summary>Every generated view, including the baseline plans, in creation order.</summary>
        public List<View> Views { get; } = new List<View>();

        public List<ViewSheet> Sheets { get; } = new List<ViewSheet>();
        public List<Viewport> Viewports { get; } = new List<Viewport>();
        public List<TextNote> TextNotes { get; } = new List<TextNote>();
        public List<ElementType> DuplicatedTypes { get; } = new List<ElementType>();
        public List<Material> Materials { get; } = new List<Material>();

        // ---- Queries ---------------------------------------------------------------------

        public Level LevelFor(int levelIndex) => Levels.TryGetValue(levelIndex, out var l) ? l : null;

        /// <summary>The next buildable level above the given plan level index, or null for the top level.</summary>
        public Level LevelAbove(int levelIndex)
        {
            if (Baseline == null) return null;
            var current = Baseline.Levels.FirstOrDefault(l => l.Index == levelIndex);
            if (current == null) return null;
            var next = Baseline.Levels
                .Where(l => l.IsBuildable && l.ElevationMm > current.ElevationMm)
                .OrderBy(l => l.ElevationMm)
                .FirstOrDefault();
            return next == null ? null : LevelFor(next.Index);
        }

        public ViewPlan PlanViewFor(int levelIndex) => PlanViews.TryGetValue(levelIndex, out var v) ? v : null;

        /// <summary>The Revit elements for every committed registry record that still exists.</summary>
        public IEnumerable<Element> AllGeneratedElements()
        {
            foreach (var record in Registry.Committed)
            {
                var e = Document.GetElement(new ElementId(record.ElementId));
                if (e != null && e.IsValidObject) yield return e;
            }
        }

        /// <summary>Committed generated elements of one category.</summary>
        public IEnumerable<Element> GeneratedElements(GeneratedCategory category)
        {
            foreach (var record in Registry.Committed.Where(r => r.Category == category))
            {
                var e = Document.GetElement(new ElementId(record.ElementId));
                if (e != null && e.IsValidObject) yield return e;
            }
        }
    }
}
