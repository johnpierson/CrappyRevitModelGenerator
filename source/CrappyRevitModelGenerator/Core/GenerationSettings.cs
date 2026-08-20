using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// Everything the user chose in the dialog, and nothing that depends on Revit. A settings
    /// object is serialised into the report and into the run's DataStorage record so a run can
    /// be reproduced (same seed + same settings + same template + same Revit version).
    /// </summary>
    public sealed class GenerationSettings
    {
        // ---- Run setup -------------------------------------------------------------------

        public int Seed { get; set; }

        public GenerationSeverity Severity { get; set; } = GenerationSeverity.Medium;

        /// <summary>Preview only: plan and estimate, open no transaction, create nothing.</summary>
        public bool DryRun { get; set; }

        // ---- Model content ---------------------------------------------------------------

        public int LevelCount { get; set; } = GenerationLimits.DefaultLevels;

        public double FootprintWidthMm { get; set; } = GenerationLimits.DefaultFootprintWidthMm;

        public double FootprintDepthMm { get; set; } = GenerationLimits.DefaultFootprintDepthMm;

        public double LevelHeightMm { get; set; } = GenerationLimits.DefaultLevelHeightMm;

        /// <summary>
        /// Multiplies the per-severity content quantities — rooms, views, sheets, text notes,
        /// duplicate types and materials — so a run can produce a large model without making it
        /// a worse one. Levels and footprint decide the building; this decides how much content
        /// is hung off it. 1.0 is the historical behaviour.
        /// </summary>
        public double ContentScale { get; set; } = GenerationLimits.DefaultContentScale;

        public bool CreateFloors { get; set; } = true;

        public bool CreateDoorsAndWindows { get; set; } = true;

        public bool CreateFurniture { get; set; } = true;

        public bool CreateRooms { get; set; } = true;

        // ---- Scenarios -------------------------------------------------------------------

        /// <summary>
        /// Scenario ids to run. The baseline scenario is always run whether or not it is listed.
        /// Null means "the defaults for the chosen severity" (see <see cref="ScenarioCatalog"/>).
        /// </summary>
        public List<string> EnabledScenarioIds { get; set; }

        // ---- Safety ----------------------------------------------------------------------

        public int MaxElements { get; set; } = GenerationLimits.DefaultMaxElements;

        /// <summary>The user ticked the box acknowledging that content goes into the active document.</summary>
        public bool ConfirmedActiveDocument { get; set; }

        /// <summary>The user explicitly allowed running in a workshared (central/local) document.</summary>
        public bool AllowWorksharedDocument { get; set; }

        /// <summary>
        /// When true every Revit warning raised while committing a scenario is dismissed so no
        /// dialog interrupts the run. When false (the default) only warnings on the curated
        /// "expected" list are dismissed; anything else surfaces in Revit's own dialog. Either
        /// way every warning is recorded in the report.
        /// </summary>
        public bool SuppressAllWarningDialogs { get; set; }

        /// <summary>
        /// Optional path for a JSON copy of the report. Null (default) writes no file: the plan
        /// forbids silently writing next to a production project.
        /// </summary>
        public string ReportExportPath { get; set; }

        // ---- Behaviour -------------------------------------------------------------------

        public GenerationSettings Clone()
        {
            var clone = (GenerationSettings)MemberwiseClone();
            clone.EnabledScenarioIds = EnabledScenarioIds?.ToList();
            return clone;
        }

        /// <summary>The scenario ids that will actually run, in catalog order, baseline first.</summary>
        public IReadOnlyList<string> ResolveScenarioIds()
        {
            var requested = EnabledScenarioIds ?? ScenarioCatalog.DefaultEnabledIds(Severity).ToList();
            var set = new HashSet<string>(requested, StringComparer.OrdinalIgnoreCase) { ScenarioIds.Baseline };
            return ScenarioCatalog.All
                .Where(s => set.Contains(s.Id))
                .OrderBy(s => s.Order)
                .Select(s => s.Id)
                .ToList();
        }

        public bool IsScenarioEnabled(string scenarioId) =>
            ResolveScenarioIds().Contains(scenarioId, StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Validate against <see cref="GenerationLimits"/>. Errors block generation; warnings are
        /// shown but do not block. Never opens a transaction; safe to call from the dialog on
        /// every keystroke.
        /// </summary>
        public ValidationResult Validate()
        {
            var result = new ValidationResult();

            if (LevelCount < GenerationLimits.MinLevels || LevelCount > GenerationLimits.MaxLevels)
                result.AddError($"Level count must be between {GenerationLimits.MinLevels} and {GenerationLimits.MaxLevels} (was {LevelCount}).");

            if (double.IsNaN(FootprintWidthMm) || FootprintWidthMm < GenerationLimits.MinFootprintMm || FootprintWidthMm > GenerationLimits.MaxFootprintMm)
                result.AddError($"Footprint width must be between {GenerationLimits.MinFootprintMm:0} and {GenerationLimits.MaxFootprintMm:0} mm (was {FootprintWidthMm:0}).");

            if (double.IsNaN(FootprintDepthMm) || FootprintDepthMm < GenerationLimits.MinFootprintMm || FootprintDepthMm > GenerationLimits.MaxFootprintMm)
                result.AddError($"Footprint depth must be between {GenerationLimits.MinFootprintMm:0} and {GenerationLimits.MaxFootprintMm:0} mm (was {FootprintDepthMm:0}).");

            if (double.IsNaN(LevelHeightMm) || LevelHeightMm < GenerationLimits.MinLevelHeightMm || LevelHeightMm > GenerationLimits.MaxLevelHeightMm)
                result.AddError($"Level height must be between {GenerationLimits.MinLevelHeightMm:0} and {GenerationLimits.MaxLevelHeightMm:0} mm (was {LevelHeightMm:0}).");

            if (double.IsNaN(ContentScale) || ContentScale < GenerationLimits.MinContentScale || ContentScale > GenerationLimits.MaxContentScale)
                result.AddError($"Content scale must be between {GenerationLimits.MinContentScale:0.##} and {GenerationLimits.MaxContentScale:0.##} (was {ContentScale:0.##}).");

            if (MaxElements < GenerationLimits.MinMaxElements)
                result.AddError($"Maximum element count must be at least {GenerationLimits.MinMaxElements} (was {MaxElements}).");

            if (MaxElements > GenerationLimits.HardMaxElements)
                result.AddError($"Maximum element count cannot exceed the hard cap of {GenerationLimits.HardMaxElements} (was {MaxElements}).");

            if (!Enum.IsDefined(typeof(GenerationSeverity), Severity))
                result.AddError($"Unknown severity value {(int)Severity}.");

            if (EnabledScenarioIds != null)
            {
                foreach (var id in EnabledScenarioIds.Where(id => ScenarioCatalog.Find(id) == null))
                    result.AddError($"Unknown scenario id '{id}'.");
            }

            if (!string.IsNullOrEmpty(ReportExportPath))
            {
                try
                {
                    var full = System.IO.Path.GetFullPath(ReportExportPath);
                    if (string.IsNullOrEmpty(System.IO.Path.GetFileName(full)))
                        result.AddError("Report export path must include a file name.");
                }
                catch (Exception ex)
                {
                    result.AddError($"Report export path is not valid: {ex.Message}");
                }
            }

            if (result.IsValid)
            {
                var estimate = ElementCountEstimator.Estimate(this);
                if (estimate.Total > MaxElements)
                    result.AddError($"The estimated element count ({estimate.Total}) exceeds the maximum ({MaxElements}). Reduce levels, footprint, content scale or scenarios, or raise the maximum.");
                else if (estimate.Total > MaxElements * 0.8)
                    result.AddWarning($"The estimated element count ({estimate.Total}) is close to the maximum ({MaxElements}).");
            }

            if (IsScenarioEnabled(ScenarioIds.Warnings))
                result.AddWarning("The Warnings scenario intentionally creates overlapping and duplicate elements that Revit will flag.");

            return result;
        }

        // ---- Serialisation ---------------------------------------------------------------

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static GenerationSettings FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Settings JSON is empty.", nameof(json));
            return JsonSerializer.Deserialize<GenerationSettings>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Settings JSON deserialised to null.");
        }
    }

    /// <summary>Outcome of <see cref="GenerationSettings.Validate"/>.</summary>
    public sealed class ValidationResult
    {
        private readonly List<string> _errors = new List<string>();
        private readonly List<string> _warnings = new List<string>();

        public IReadOnlyList<string> Errors => _errors;
        public IReadOnlyList<string> Warnings => _warnings;
        public bool IsValid => _errors.Count == 0;

        public void AddError(string message) { if (!string.IsNullOrWhiteSpace(message)) _errors.Add(message); }
        public void AddWarning(string message) { if (!string.IsNullOrWhiteSpace(message)) _warnings.Add(message); }

        public override string ToString()
        {
            var lines = new List<string>();
            lines.AddRange(_errors.Select(e => "Error: " + e));
            lines.AddRange(_warnings.Select(w => "Warning: " + w));
            return string.Join(Environment.NewLine, lines);
        }
    }
}
