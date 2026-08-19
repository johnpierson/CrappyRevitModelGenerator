using System;
using Autodesk.Revit.DB.ExtensibleStorage;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// The two Extensible Storage schemas (plan section 6, docs/DECISIONS.md). Both are public
    /// read/write so audit tools — and the cleanup command in a later session — can read them
    /// without a vendor match. A schema's fields are frozen for its GUID: change the fields and
    /// you must mint a new GUID and a new name, or Revit refuses to open documents carrying
    /// the old definition.
    /// </summary>
    public static class GeneratorSchema
    {
        // ---- GeneratedElement: attached to every generated element ------------------------

        public static readonly Guid ElementSchemaGuid = new Guid("06A9B449-E2E6-4251-89F3-E3DC66BD5160");
        public const string ElementSchemaName = "CrappyGeneratedElement";
        public const string FieldRunId = "RunId";
        public const string FieldScenarioId = "ScenarioId";
        public const string FieldSeed = "Seed";
        public const string FieldGeneratorVersion = "GeneratorVersion";
        public const string FieldCreatedUtc = "CreatedUtc";

        // ---- GenerationRun: one DataStorage per run -----------------------------------------

        public static readonly Guid RunSchemaGuid = new Guid("5B13A5D5-1582-46CC-9B55-43107D7AA4D7");
        public const string RunSchemaName = "CrappyGenerationRun";
        public const string FieldSeverity = "Severity";
        public const string FieldRevitVersion = "RevitVersion";
        public const string FieldDocumentTitle = "DocumentTitle";
        public const string FieldSettingsJson = "SettingsJson";
        public const string FieldReportJson = "ReportJson";
        public const string FieldElementIds = "ElementIds";
        public const string FieldUntaggedElementIds = "UntaggedElementIds";

        /// <summary>Prefix of the DataStorage element name, so it is recognisable in a schedule or the API.</summary>
        public const string RunStorageNamePrefix = "CrappyRevitModelGenerator Run ";

        private static Schema _elementSchema;
        private static Schema _runSchema;

        public static Schema ElementSchema()
        {
            if (_elementSchema != null) return _elementSchema;
            _elementSchema = Schema.Lookup(ElementSchemaGuid);
            if (_elementSchema != null) return _elementSchema;

            var builder = new SchemaBuilder(ElementSchemaGuid);
            builder.SetSchemaName(ElementSchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId(RunIdentity.SchemaVendorId);
            builder.SetDocumentation("Identifies an element created by the Crappy Revit Model Generator so it can be reported and cleaned up.");
            builder.AddSimpleField(FieldRunId, typeof(string)).SetDocumentation("Run id of the generation run that created the element.");
            builder.AddSimpleField(FieldScenarioId, typeof(string)).SetDocumentation("Scenario id that created the element.");
            builder.AddSimpleField(FieldSeed, typeof(int)).SetDocumentation("Random seed of the run.");
            builder.AddSimpleField(FieldGeneratorVersion, typeof(string)).SetDocumentation("Generator assembly version.");
            builder.AddSimpleField(FieldCreatedUtc, typeof(string)).SetDocumentation("UTC creation time, ISO 8601.");
            _elementSchema = builder.Finish();
            return _elementSchema;
        }

        public static Schema RunSchema()
        {
            if (_runSchema != null) return _runSchema;
            _runSchema = Schema.Lookup(RunSchemaGuid);
            if (_runSchema != null) return _runSchema;

            var builder = new SchemaBuilder(RunSchemaGuid);
            builder.SetSchemaName(RunSchemaName);
            builder.SetReadAccessLevel(AccessLevel.Public);
            builder.SetWriteAccessLevel(AccessLevel.Public);
            builder.SetVendorId(RunIdentity.SchemaVendorId);
            builder.SetDocumentation("Summary of one Crappy Revit Model Generator run: settings, report and the ids of every generated element.");
            builder.AddSimpleField(FieldRunId, typeof(string));
            builder.AddSimpleField(FieldSeed, typeof(int));
            builder.AddSimpleField(FieldSeverity, typeof(string));
            builder.AddSimpleField(FieldGeneratorVersion, typeof(string));
            builder.AddSimpleField(FieldRevitVersion, typeof(string));
            builder.AddSimpleField(FieldCreatedUtc, typeof(string));
            builder.AddSimpleField(FieldDocumentTitle, typeof(string));
            builder.AddSimpleField(FieldSettingsJson, typeof(string));
            builder.AddSimpleField(FieldReportJson, typeof(string));
            builder.AddArrayField(FieldElementIds, typeof(ElementId));
            builder.AddArrayField(FieldUntaggedElementIds, typeof(ElementId));
            _runSchema = builder.Finish();
            return _runSchema;
        }
    }
}
