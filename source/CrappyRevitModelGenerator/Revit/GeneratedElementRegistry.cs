using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB.ExtensibleStorage;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>One generated element, as the registry remembers it.</summary>
    public sealed class GeneratedElementRecord
    {
        public long ElementId { get; set; }
        public GeneratedCategory Category { get; set; }
        public string ScenarioId { get; set; }
        public bool Tagged { get; set; }
        public string Note { get; set; }
    }

    /// <summary>
    /// Remembers every element a run creates (plan section 6). Registration is staged per
    /// scenario transaction: <see cref="BeginScenario"/> opens a stage, <see cref="Register"/>
    /// tags the element with the identity entity and stages the record, and the coordinator
    /// calls <see cref="CommitScenario"/> or <see cref="RollbackScenario"/> depending on how
    /// the transaction ended. Report counts are only touched at commit, so a rolled-back
    /// scenario leaves no trace in the counts.
    /// </summary>
    public sealed class GeneratedElementRegistry
    {
        private readonly List<GeneratedElementRecord> _committed = new List<GeneratedElementRecord>();
        private readonly List<GeneratedElementRecord> _staged = new List<GeneratedElementRecord>();
        private readonly HashSet<long> _knownIds = new HashSet<long>();
        private readonly GenerationReport _report;
        private string _stageScenarioId;

        public GeneratedElementRegistry(string runId, int seed, string generatorVersion, GenerationReport report)
        {
            RunId = runId ?? throw new ArgumentNullException(nameof(runId));
            Seed = seed;
            GeneratorVersion = generatorVersion ?? "0.0.0";
            _report = report ?? throw new ArgumentNullException(nameof(report));
        }

        public string RunId { get; }
        public int Seed { get; }
        public string GeneratorVersion { get; }

        /// <summary>Records committed so far (does not include the open stage).</summary>
        public IReadOnlyList<GeneratedElementRecord> Committed => _committed;

        public IReadOnlyList<GeneratedElementRecord> Staged => _staged;

        public bool HasOpenStage => _stageScenarioId != null;

        public bool Contains(ElementId id) => id != null && _knownIds.Contains(id.Value);

        public IEnumerable<long> AllIds => _committed.Select(r => r.ElementId);

        // ---- Staging ---------------------------------------------------------------------

        public void BeginScenario(string scenarioId)
        {
            if (_stageScenarioId != null)
                throw new InvalidOperationException($"Scenario '{_stageScenarioId}' is still open; commit or roll it back first.");
            _stageScenarioId = scenarioId ?? throw new ArgumentNullException(nameof(scenarioId));
            _staged.Clear();
        }

        /// <summary>Move the stage into the committed set and add its counts to the report. Returns the number committed.</summary>
        public int CommitScenario()
        {
            var count = _staged.Count;
            foreach (var record in _staged)
            {
                _committed.Add(record);
                _report.Increment(record.Category);
                if (!record.Tagged) _report.UntaggedElementIds.Add(record.ElementId);
            }
            _staged.Clear();
            _stageScenarioId = null;
            return count;
        }

        /// <summary>Discard the stage: the transaction rolled back, so those elements do not exist.</summary>
        public int RollbackScenario()
        {
            var count = _staged.Count;
            foreach (var record in _staged) _knownIds.Remove(record.ElementId);
            _staged.Clear();
            _stageScenarioId = null;
            return count;
        }

        // ---- Registration ----------------------------------------------------------------

        /// <summary>
        /// Tag <paramref name="element"/> with the identity entity and stage its record. Must be
        /// called inside the scenario's transaction. Never throws for a tagging failure — the
        /// element is recorded as untagged and cleanup falls back to the recorded id.
        /// </summary>
        public GeneratedElementRecord Register(Element element, GeneratedCategory category, string note = null)
        {
            if (element == null) throw new ArgumentNullException(nameof(element));
            if (_stageScenarioId == null) throw new InvalidOperationException("Register was called outside a scenario stage.");

            var id = element.Id.Value;
            var existing = _staged.FirstOrDefault(r => r.ElementId == id) ?? _committed.FirstOrDefault(r => r.ElementId == id);
            if (existing != null) return existing;

            var record = new GeneratedElementRecord
            {
                ElementId = id,
                Category = category,
                ScenarioId = _stageScenarioId,
                Note = note,
            };

            record.Tagged = TryTag(element, _stageScenarioId, out var reason);
            if (!record.Tagged) record.Note = string.IsNullOrEmpty(reason) ? record.Note : reason;

            _staged.Add(record);
            _knownIds.Add(id);
            return record;
        }

        /// <summary>Register several elements of the same category at once.</summary>
        public void RegisterAll(IEnumerable<Element> elements, GeneratedCategory category)
        {
            if (elements == null) return;
            foreach (var e in elements) if (e != null) Register(e, category);
        }

        private bool TryTag(Element element, string scenarioId, out string reason)
        {
            reason = null;
            try
            {
                var schema = GeneratorSchema.ElementSchema();
                var entity = new Entity(schema);
                entity.Set(GeneratorSchema.FieldRunId, RunId);
                entity.Set(GeneratorSchema.FieldScenarioId, scenarioId);
                entity.Set(GeneratorSchema.FieldSeed, Seed);
                entity.Set(GeneratorSchema.FieldGeneratorVersion, GeneratorVersion);
                entity.Set(GeneratorSchema.FieldCreatedUtc, DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture));
                element.SetEntity(entity);
                return true;
            }
            catch (Exception ex)
            {
                reason = $"could not attach identity entity: {ex.GetType().Name}: {ex.Message}";
                return false;
            }
        }

        // ---- Identity queries (used by cleanup) ------------------------------------------

        /// <summary>True when the element carries this generator's entity for the given run.</summary>
        public static bool CarriesRunIdentity(Element element, string runId)
        {
            if (element == null || string.IsNullOrEmpty(runId)) return false;
            try
            {
                var schema = Schema.Lookup(GeneratorSchema.ElementSchemaGuid);
                if (schema == null) return false;
                var entity = element.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return false;
                return string.Equals(entity.Get<string>(GeneratorSchema.FieldRunId), runId, StringComparison.Ordinal);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>The run id stored on the element, or null when it carries no generator identity.</summary>
        public static string ReadRunId(Element element)
        {
            if (element == null) return null;
            try
            {
                var schema = Schema.Lookup(GeneratorSchema.ElementSchemaGuid);
                if (schema == null) return null;
                var entity = element.GetEntity(schema);
                if (entity == null || !entity.IsValid()) return null;
                return entity.Get<string>(GeneratorSchema.FieldRunId);
            }
            catch
            {
                return null;
            }
        }
    }
}
