using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.Revit.DB.ExtensibleStorage;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// Reads and writes the per-run <see cref="DataStorage"/> record (plan section 6) so
    /// cleanup and "View Last Report" work after the command ends and after save/reopen.
    /// </summary>
    public static class RunStore
    {
        /// <summary>
        /// Write (or overwrite) the run record. Must be called inside a transaction. Returns the
        /// DataStorage element. When <paramref name="existing"/> is supplied its entity is
        /// replaced instead of creating a new element.
        /// </summary>
        public static DataStorage Write(Document doc, GenerationReport report, GenerationSettings settings, IEnumerable<long> elementIds, IEnumerable<long> untaggedIds, DataStorage existing = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (report == null) throw new ArgumentNullException(nameof(report));

            var schema = GeneratorSchema.RunSchema();
            var storage = existing ?? DataStorage.Create(doc);

            try
            {
                storage.Name = GeneratorSchema.RunStorageNamePrefix + report.RunId;
            }
            catch
            {
                // A duplicate or rejected name is not worth failing the run over; the entity carries the id.
            }

            var entity = new Entity(schema);
            entity.Set(GeneratorSchema.FieldRunId, report.RunId ?? string.Empty);
            entity.Set(GeneratorSchema.FieldSeed, report.Seed);
            entity.Set(GeneratorSchema.FieldSeverity, (settings?.Severity ?? GenerationSeverity.Medium).ToString());
            entity.Set(GeneratorSchema.FieldGeneratorVersion, report.GeneratorVersion ?? string.Empty);
            entity.Set(GeneratorSchema.FieldRevitVersion, report.RevitVersion ?? string.Empty);
            entity.Set(GeneratorSchema.FieldCreatedUtc, report.StartedUtc.ToString("o", CultureInfo.InvariantCulture));
            entity.Set(GeneratorSchema.FieldDocumentTitle, report.DocumentTitle ?? string.Empty);
            entity.Set(GeneratorSchema.FieldSettingsJson, settings?.ToJson() ?? string.Empty);
            entity.Set(GeneratorSchema.FieldReportJson, report.ToJson());
            entity.Set<IList<ElementId>>(GeneratorSchema.FieldElementIds, (elementIds ?? Enumerable.Empty<long>()).Distinct().Select(id => new ElementId(id)).ToList());
            entity.Set<IList<ElementId>>(GeneratorSchema.FieldUntaggedElementIds, (untaggedIds ?? Enumerable.Empty<long>()).Distinct().Select(id => new ElementId(id)).ToList());
            storage.SetEntity(entity);

            return storage;
        }

        /// <summary>Every run record in the document, newest first.</summary>
        public static List<RunRecord> ReadAll(Document doc)
        {
            var result = new List<RunRecord>();
            if (doc == null) return result;

            var schema = Schema.Lookup(GeneratorSchema.RunSchemaGuid);
            if (schema == null) return result;

            var storages = new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .ToList();

            foreach (var storage in storages)
            {
                var record = Read(storage, schema);
                if (record != null) result.Add(record);
            }

            return result.OrderByDescending(r => r.CreatedUtc).ToList();
        }

        public static RunRecord Read(DataStorage storage)
        {
            var schema = Schema.Lookup(GeneratorSchema.RunSchemaGuid);
            return schema == null ? null : Read(storage, schema);
        }

        private static RunRecord Read(DataStorage storage, Schema schema)
        {
            if (storage == null) return null;
            Entity entity;
            try
            {
                entity = storage.GetEntity(schema);
            }
            catch
            {
                return null;
            }
            if (entity == null || !entity.IsValid()) return null;

            var record = new RunRecord
            {
                StorageElementId = storage.Id.Value,
                RunId = SafeGet<string>(entity, GeneratorSchema.FieldRunId),
                Seed = SafeGet<int>(entity, GeneratorSchema.FieldSeed),
                Severity = SafeGet<string>(entity, GeneratorSchema.FieldSeverity),
                GeneratorVersion = SafeGet<string>(entity, GeneratorSchema.FieldGeneratorVersion),
                RevitVersion = SafeGet<string>(entity, GeneratorSchema.FieldRevitVersion),
                DocumentTitle = SafeGet<string>(entity, GeneratorSchema.FieldDocumentTitle),
                SettingsJson = SafeGet<string>(entity, GeneratorSchema.FieldSettingsJson),
                ReportJson = SafeGet<string>(entity, GeneratorSchema.FieldReportJson),
            };

            var created = SafeGet<string>(entity, GeneratorSchema.FieldCreatedUtc);
            if (!DateTime.TryParse(created, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var createdUtc))
                createdUtc = DateTime.MinValue;
            record.CreatedUtc = createdUtc;

            record.ElementIds = (SafeGet<IList<ElementId>>(entity, GeneratorSchema.FieldElementIds) ?? new List<ElementId>()).Select(id => id.Value).ToList();
            record.UntaggedElementIds = (SafeGet<IList<ElementId>>(entity, GeneratorSchema.FieldUntaggedElementIds) ?? new List<ElementId>()).Select(id => id.Value).ToList();

            return record;
        }

        /// <summary>The most recent run record, or null.</summary>
        public static RunRecord ReadLatest(Document doc) => ReadAll(doc).FirstOrDefault();

        public static DataStorage FindStorage(Document doc, string runId)
        {
            if (doc == null || string.IsNullOrEmpty(runId)) return null;
            var schema = Schema.Lookup(GeneratorSchema.RunSchemaGuid);
            if (schema == null) return null;

            return new FilteredElementCollector(doc)
                .OfClass(typeof(DataStorage))
                .Cast<DataStorage>()
                .FirstOrDefault(ds =>
                {
                    try
                    {
                        var e = ds.GetEntity(schema);
                        return e != null && e.IsValid() && string.Equals(e.Get<string>(GeneratorSchema.FieldRunId), runId, StringComparison.Ordinal);
                    }
                    catch
                    {
                        return false;
                    }
                });
        }

        private static T SafeGet<T>(Entity entity, string field)
        {
            try { return entity.Get<T>(field); }
            catch { return default; }
        }
    }
}
