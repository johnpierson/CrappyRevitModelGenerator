using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>Categories counted in the report. Names are stable; they appear in JSON.</summary>
    public enum GeneratedCategory
    {
        Levels,
        Grids,
        Walls,
        Floors,
        Doors,
        Windows,
        Furniture,
        Rooms,
        RoomSeparationLines,
        RoomTags,
        Views,
        Sheets,
        Viewports,
        TextNotes,
        Types,
        Materials,
        DataStorage,
        Other,
    }

    public enum ScenarioStatus
    {
        NotRun,
        Applied,
        Skipped,
        RolledBack,
    }

    /// <summary>What happened to one scenario during a run.</summary>
    public sealed class ScenarioOutcome
    {
        public string ScenarioId { get; set; }
        public string DisplayName { get; set; }
        public ScenarioStatus Status { get; set; } = ScenarioStatus.NotRun;
        public string Message { get; set; }
        public int ElementsCreated { get; set; }
        public double DurationMs { get; set; }
    }

    /// <summary>A Revit failure message or an exception captured during a scenario.</summary>
    public sealed class FailureRecord
    {
        public string ScenarioId { get; set; }
        public string Operation { get; set; }

        /// <summary>"Warning", "Error", "DocumentCorruption" or "Exception".</summary>
        public string Severity { get; set; }

        /// <summary>The FailureDefinitionId GUID, when the failure came from Revit.</summary>
        public string DefinitionId { get; set; }
        public string Message { get; set; }
        public List<long> ElementIds { get; set; } = new List<long>();

        /// <summary>True when the warning was on the expected list and was dismissed instead of shown.</summary>
        public bool Dismissed { get; set; }
        public bool TransactionRolledBack { get; set; }
    }

    /// <summary>
    /// A line in the report that is not a failure: an intentional defect the generator planted,
    /// a fallback the type resolver took, or plain information.
    /// </summary>
    public sealed class ReportNote
    {
        public const string KindDefect = "Defect";
        public const string KindFallback = "Fallback";
        public const string KindInfo = "Info";
        public const string KindCleanup = "Cleanup";

        public string Kind { get; set; } = KindInfo;
        public string ScenarioId { get; set; }
        public string Message { get; set; }
        public List<long> ElementIds { get; set; } = new List<long>();
    }

    /// <summary>
    /// The result of one generation run. Built up while scenarios execute, shown in the report
    /// window, serialised into the run's DataStorage record, and optionally exported as JSON.
    /// Every intentional defect appears here with its scenario id (plan principle 5).
    /// </summary>
    public sealed class GenerationReport
    {
        public string RunId { get; set; }
        public int Seed { get; set; }
        public string GeneratorVersion { get; set; }
        public string RevitVersion { get; set; }
        public string DocumentTitle { get; set; }
        public DateTime StartedUtc { get; set; }
        public DateTime? FinishedUtc { get; set; }
        public bool DryRun { get; set; }
        public bool Aborted { get; set; }
        public string AbortReason { get; set; }

        public GenerationSettings Settings { get; set; }

        /// <summary>Counts by <see cref="GeneratedCategory"/> name. Sorted so JSON is stable.</summary>
        public SortedDictionary<string, int> Counts { get; set; } = new SortedDictionary<string, int>(StringComparer.Ordinal);

        public List<ScenarioOutcome> Scenarios { get; set; } = new List<ScenarioOutcome>();
        public List<FailureRecord> ExpectedWarnings { get; set; } = new List<FailureRecord>();
        public List<FailureRecord> Failures { get; set; } = new List<FailureRecord>();
        public List<ReportNote> Notes { get; set; } = new List<ReportNote>();

        /// <summary>Every element id the run created that still existed at the end of the run.</summary>
        public List<long> GeneratedElementIds { get; set; } = new List<long>();

        /// <summary>Generated elements that refused the identity entity; cleanup uses this list as a fallback.</summary>
        public List<long> UntaggedElementIds { get; set; } = new List<long>();

        /// <summary>Id of the DataStorage element holding this run's record, when one was written.</summary>
        public long? RunStorageElementId { get; set; }

        // ---- Mutation helpers -----------------------------------------------------------

        [JsonIgnore]
        public int TotalElements => Counts.Values.Sum();

        public void Increment(GeneratedCategory category, int by = 1)
        {
            if (by == 0) return;
            var key = category.ToString();
            Counts.TryGetValue(key, out var current);
            Counts[key] = current + by;
        }

        public int CountOf(GeneratedCategory category) =>
            Counts.TryGetValue(category.ToString(), out var n) ? n : 0;

        public ScenarioOutcome BeginScenario(ScenarioDefinition definition)
        {
            var outcome = new ScenarioOutcome
            {
                ScenarioId = definition.Id,
                DisplayName = definition.DisplayName,
                Status = ScenarioStatus.NotRun,
            };
            Scenarios.Add(outcome);
            return outcome;
        }

        public ScenarioOutcome FindScenario(string scenarioId) =>
            Scenarios.FirstOrDefault(s => string.Equals(s.ScenarioId, scenarioId, StringComparison.OrdinalIgnoreCase));

        public ReportNote AddDefect(string scenarioId, string message, IEnumerable<long> elementIds = null) =>
            AddNote(ReportNote.KindDefect, scenarioId, message, elementIds);

        public ReportNote AddFallback(string scenarioId, string message, IEnumerable<long> elementIds = null) =>
            AddNote(ReportNote.KindFallback, scenarioId, message, elementIds);

        public ReportNote AddInfo(string scenarioId, string message, IEnumerable<long> elementIds = null) =>
            AddNote(ReportNote.KindInfo, scenarioId, message, elementIds);

        public ReportNote AddNote(string kind, string scenarioId, string message, IEnumerable<long> elementIds = null)
        {
            var note = new ReportNote
            {
                Kind = kind ?? ReportNote.KindInfo,
                ScenarioId = scenarioId,
                Message = message ?? string.Empty,
            };
            if (elementIds != null) note.ElementIds.AddRange(elementIds);
            Notes.Add(note);
            return note;
        }

        public FailureRecord AddExpectedWarning(FailureRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            ExpectedWarnings.Add(record);
            return record;
        }

        public FailureRecord AddFailure(FailureRecord record)
        {
            if (record == null) throw new ArgumentNullException(nameof(record));
            Failures.Add(record);
            return record;
        }

        public FailureRecord AddException(string scenarioId, string operation, Exception exception, bool rolledBack)
        {
            var record = new FailureRecord
            {
                ScenarioId = scenarioId,
                Operation = operation,
                Severity = "Exception",
                Message = exception == null ? "Unknown exception" : $"{exception.GetType().Name}: {exception.Message}",
                TransactionRolledBack = rolledBack,
            };
            Failures.Add(record);
            return record;
        }

        public void Finish()
        {
            FinishedUtc = DateTime.UtcNow;
        }

        // ---- Queries -------------------------------------------------------------------

        [JsonIgnore]
        public IEnumerable<ReportNote> Defects => Notes.Where(n => n.Kind == ReportNote.KindDefect);

        [JsonIgnore]
        public IEnumerable<ReportNote> Fallbacks => Notes.Where(n => n.Kind == ReportNote.KindFallback);

        [JsonIgnore]
        public bool HasUnexpectedFailures => Failures.Count > 0;

        [JsonIgnore]
        public IEnumerable<ScenarioOutcome> RolledBackScenarios => Scenarios.Where(s => s.Status == ScenarioStatus.RolledBack);

        // ---- Serialisation ---------------------------------------------------------------

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            Converters = { new JsonStringEnumConverter() },
        };

        public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

        public static GenerationReport FromJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new ArgumentException("Report JSON is empty.", nameof(json));
            return JsonSerializer.Deserialize<GenerationReport>(json, JsonOptions)
                   ?? throw new InvalidOperationException("Report JSON deserialised to null.");
        }

        /// <summary>A plain-text rendering for the report window and for pasting into an issue.</summary>
        public string ToText()
        {
            var sb = new StringBuilder();
            var inv = CultureInfo.InvariantCulture;

            sb.AppendLine("Crappy Revit Model Generator - run report");
            sb.AppendLine("=========================================");
            sb.AppendLine($"Run id:            {RunId}");
            sb.AppendLine($"Seed:              {Seed}");
            sb.AppendLine($"Generator version: {GeneratorVersion}");
            sb.AppendLine($"Revit version:     {RevitVersion}");
            sb.AppendLine($"Document:          {DocumentTitle}");
            sb.AppendLine($"Started (UTC):     {StartedUtc.ToString("u", inv)}");
            if (FinishedUtc.HasValue)
                sb.AppendLine($"Finished (UTC):    {FinishedUtc.Value.ToString("u", inv)}  ({(FinishedUtc.Value - StartedUtc).TotalSeconds:0.0} s)");
            if (DryRun) sb.AppendLine("Mode:              DRY RUN - nothing was created");
            if (Aborted) sb.AppendLine($"ABORTED:           {AbortReason}");
            sb.AppendLine();

            if (Settings != null)
            {
                sb.AppendLine("Settings");
                sb.AppendLine("--------");
                sb.AppendLine($"Severity {Settings.Severity}, content scale {Settings.ContentScale:0.##}x, {Settings.LevelCount} level(s), footprint {Settings.FootprintWidthMm:0} x {Settings.FootprintDepthMm:0} mm, level height {Settings.LevelHeightMm:0} mm, max elements {Settings.MaxElements}");
                sb.AppendLine("Scenarios: " + string.Join(", ", Settings.ResolveScenarioIds()));
                sb.AppendLine();
            }

            sb.AppendLine("Counts");
            sb.AppendLine("------");
            foreach (var pair in Counts.OrderBy(p => p.Key, StringComparer.Ordinal))
                sb.AppendLine($"{pair.Key,-22}{pair.Value,6}");
            sb.AppendLine($"{"Total",-22}{TotalElements,6}");
            sb.AppendLine();

            sb.AppendLine("Scenarios");
            sb.AppendLine("---------");
            foreach (var s in Scenarios)
            {
                var line = $"{s.Status,-11} {s.DisplayName} [{s.ScenarioId}]  elements={s.ElementsCreated}  {s.DurationMs:0} ms";
                if (!string.IsNullOrEmpty(s.Message)) line += "  - " + s.Message;
                sb.AppendLine(line);
            }
            sb.AppendLine();

            AppendNotes(sb, "Intentional defects", Defects);
            AppendNotes(sb, "Fallbacks", Fallbacks);
            AppendNotes(sb, "Information", Notes.Where(n => n.Kind == ReportNote.KindInfo));
            AppendNotes(sb, "Cleanup", Notes.Where(n => n.Kind == ReportNote.KindCleanup));

            AppendFailures(sb, "Expected warnings (dismissed)", ExpectedWarnings);
            AppendFailures(sb, "Unexpected failures", Failures);

            sb.AppendLine("Cleanup scope");
            sb.AppendLine("-------------");
            sb.AppendLine($"{GeneratedElementIds.Count} generated element id(s) recorded; {UntaggedElementIds.Count} could not carry the identity entity.");
            if (RunStorageElementId.HasValue) sb.AppendLine($"Run record stored in DataStorage element {RunStorageElementId.Value}.");
            sb.AppendLine("Use 'Clean Generated Model' to remove this run's content.");

            return sb.ToString();
        }

        private static void AppendNotes(StringBuilder sb, string title, IEnumerable<ReportNote> notes)
        {
            var list = notes.ToList();
            if (list.Count == 0) return;
            sb.AppendLine(title);
            sb.AppendLine(new string('-', title.Length));
            foreach (var n in list)
            {
                var ids = n.ElementIds.Count > 0 ? "  ids: " + string.Join(",", n.ElementIds.Take(12)) + (n.ElementIds.Count > 12 ? ",…" : "") : string.Empty;
                sb.AppendLine($"[{n.ScenarioId ?? "-"}] {n.Message}{ids}");
            }
            sb.AppendLine();
        }

        private static void AppendFailures(StringBuilder sb, string title, IEnumerable<FailureRecord> failures)
        {
            var list = failures.ToList();
            if (list.Count == 0) return;
            sb.AppendLine(title);
            sb.AppendLine(new string('-', title.Length));
            foreach (var f in list)
            {
                var ids = f.ElementIds.Count > 0 ? "  ids: " + string.Join(",", f.ElementIds.Take(12)) + (f.ElementIds.Count > 12 ? ",…" : "") : string.Empty;
                var rb = f.TransactionRolledBack ? "  (rolled back)" : string.Empty;
                sb.AppendLine($"[{f.ScenarioId ?? "-"}] {f.Severity}: {f.Message}  op={f.Operation}{ids}{rb}");
            }
            sb.AppendLine();
        }
    }
}
