using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Autodesk.Revit.DB.ExtensibleStorage;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Commands
{
    /// <summary>
    /// Headless generation for the integration-test procedure (plan section 11): runs a
    /// generation — and optionally a cleanup — without any dialog and writes the report to a
    /// path the caller chose. Two ways in:
    /// <list type="bullet">
    /// <item><see cref="App"/> reads the <c>CRMG_AUTOMATION</c> environment variable at startup
    ///       and, if set to a JSON parameters file, runs this once on the first
    ///       <c>Idling</c> event and then exits Revit. This is what
    ///       <c>tools/revit-smoke.ps1</c> drives — Revit's undocumented per-version journal
    ///       syntax for invoking an external command (<c>Jrn.RibbonEvent "Execute external
    ///       command:…"</c> / <c>Jrn.Data "APIStringStringMapJournalData"</c>) turned out not to
    ///       be recognised by the 2026 journal interpreter at all, so automation does not depend
    ///       on it.</item>
    /// <item><see cref="AutomationCommand"/> exposes the same logic as an ordinary external
    ///       command (Add-Ins &gt; External Tools) for a human to trigger by hand, reading the
    ///       same environment variable.</item>
    /// </list>
    ///
    /// Keys: <c>report</c> (required, output .json path; a .txt sibling is written too),
    /// <c>settings</c> (optional path to a GenerationSettings JSON), <c>seed</c>, <c>severity</c>
    /// (overrides), <c>template</c> (optional .rte: create a new project from it, save it as
    /// <c>saveAs</c>, open it, generate in it — for a zero-document start), <c>saveAs</c>
    /// (optional path to save the document after generating), <c>cleanup</c> ("true": run
    /// cleanup on the run afterwards, save as <c>&lt;saveAs&gt;.cleaned.rvt</c>, write
    /// <c>&lt;report&gt;.cleanup.json</c>).
    ///
    /// This class writes files. It only ever writes to the paths it was given.
    /// </summary>
    public static class AutomationRunner
    {
        public const string EnvironmentVariable = "CRMG_AUTOMATION";

        public static Result Run(UIApplication uiApp, IDictionary<string, string> parameters, out string message)
        {
            message = null;
            if (parameters == null || parameters.Count == 0)
            {
                message = "No parameters. Set the " + EnvironmentVariable +
                           " environment variable to a JSON file with keys report, settings, seed, severity, template, saveAs, cleanup.";
                return Result.Cancelled;
            }

            var reportPath = Get(parameters, "report");
            if (string.IsNullOrWhiteSpace(reportPath))
            {
                message = "The 'report' parameter (output path) is required.";
                return Result.Failed;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(reportPath)) ?? ".");

                var uiDoc = uiApp.ActiveUIDocument;
                var template = Get(parameters, "template");
                var saveAs = Get(parameters, "saveAs");

                if (!string.IsNullOrWhiteSpace(template))
                {
                    if (string.IsNullOrWhiteSpace(saveAs))
                        throw new InvalidOperationException("'template' requires 'saveAs' (the new project has to be saved before it can be activated).");
                    uiDoc = CreateAndActivateProject(uiApp, template, saveAs);
                }

                var doc = uiDoc?.Document ?? throw new InvalidOperationException("No active document and no 'template' parameter.");

                var settings = BuildSettings(parameters);
                var report = GenerationRunner.Run(uiApp, doc, settings);
                App.LastReport = report;
                WriteReport(reportPath, report);

                if (!report.Aborted && !settings.DryRun)
                {
                    if (!string.IsNullOrWhiteSpace(saveAs)) SaveAs(doc, saveAs);
                    else if (!string.IsNullOrWhiteSpace(doc.PathName)) doc.Save();
                }

                if (string.Equals(Get(parameters, "cleanup"), "true", StringComparison.OrdinalIgnoreCase) && !report.Aborted && !settings.DryRun)
                {
                    var cleanupPath = Path.ChangeExtension(reportPath, null) + ".cleanup.json";
                    RunCleanup(uiDoc, report, cleanupPath);
                    if (!string.IsNullOrWhiteSpace(saveAs))
                        SaveAs(doc, Path.ChangeExtension(saveAs, null) + ".cleaned.rvt");
                }

                return report.Aborted ? Result.Failed : Result.Succeeded;
            }
            catch (Exception ex)
            {
                try { File.WriteAllText(Path.ChangeExtension(reportPath, null) + ".error.txt", ex.ToString()); } catch { /* nothing else to do */ }
                message = ex.Message;
                return Result.Failed;
            }
        }

        /// <summary>Reads parameters from the <see cref="EnvironmentVariable"/> JSON file, or null when it is not set.</summary>
        public static Dictionary<string, string> ReadParametersFromEnvironment()
        {
            var envPath = Environment.GetEnvironmentVariable(EnvironmentVariable);
            if (string.IsNullOrWhiteSpace(envPath) || !File.Exists(envPath)) return null;

            var json = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(envPath));
            return json == null ? null : new Dictionary<string, string>(json, StringComparer.OrdinalIgnoreCase);
        }

        // ---- Parameters ------------------------------------------------------------------

        private static string Get(IDictionary<string, string> parameters, string key) =>
            parameters.TryGetValue(key, out var v) ? v : null;

        private static GenerationSettings BuildSettings(IDictionary<string, string> parameters)
        {
            GenerationSettings settings;
            var settingsPath = Get(parameters, "settings");
            if (!string.IsNullOrWhiteSpace(settingsPath) && File.Exists(settingsPath))
                settings = GenerationSettings.FromJson(File.ReadAllText(settingsPath));
            else
                settings = new GenerationSettings { Seed = 42 };

            if (int.TryParse(Get(parameters, "seed"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var seed))
                settings.Seed = seed;
            if (Enum.TryParse<GenerationSeverity>(Get(parameters, "severity"), true, out var severity))
                settings.Severity = severity;
            var scenarios = Get(parameters, "scenarios");
            if (!string.IsNullOrWhiteSpace(scenarios))
                settings.EnabledScenarioIds = scenarios.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            if (bool.TryParse(Get(parameters, "dryRun"), out var dryRun))
                settings.DryRun = dryRun;

            // Headless: nobody is there to click, and the caller explicitly asked for a run.
            settings.ConfirmedActiveDocument = true;
            settings.AllowWorksharedDocument = true;
            settings.SuppressAllWarningDialogs = true;
            settings.ReportExportPath = null; // we write the report ourselves
            return settings;
        }

        // ---- Document handling -----------------------------------------------------------

        private static UIDocument CreateAndActivateProject(UIApplication uiApp, string templatePath, string saveAs)
        {
            if (!File.Exists(templatePath)) throw new FileNotFoundException("Template not found.", templatePath);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(saveAs)) ?? ".");

            var newDoc = uiApp.Application.NewProjectDocument(templatePath);
            try
            {
                newDoc.SaveAs(saveAs, new SaveAsOptions { OverwriteExistingFile = true });
            }
            finally
            {
                newDoc.Close(false);
            }

            return uiApp.OpenAndActivateDocument(saveAs);
        }

        private static void SaveAs(Document doc, string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)) ?? ".");
            doc.SaveAs(path, new SaveAsOptions { OverwriteExistingFile = true });
        }

        // ---- Output ----------------------------------------------------------------------

        private static void WriteReport(string reportPath, GenerationReport report)
        {
            File.WriteAllText(reportPath, report.ToJson());
            File.WriteAllText(Path.ChangeExtension(reportPath, ".txt"), report.ToText());
        }

        private static void RunCleanup(UIDocument uiDoc, GenerationReport report, string cleanupPath)
        {
            var doc = uiDoc.Document;
            var runs = RunStore.ReadAll(doc).Where(r => r.RunId == report.RunId).ToList();
            var result = CleanupRunner.Run(uiDoc, runs);

            // Verification the caller cannot easily do itself: is anything of the run left?
            var remainingTagged = CountTagged(doc, report.RunId);
            var remainingRecords = RunStore.ReadAll(doc).Count(r => r.RunId == report.RunId);
            var remainingRecordedIds = report.GeneratedElementIds
                .Count(id => doc.GetElement(new ElementId(id)) is Element e && e.IsValidObject);

            var payload = new
            {
                report.RunId,
                RunsFound = runs.Count,
                result.Deleted,
                result.Kept,
                result.AlreadyGone,
                result.RunRecordsRemoved,
                RemainingTaggedElements = remainingTagged,
                RemainingRunRecords = remainingRecords,
                RemainingRecordedIds = remainingRecordedIds,
                KeptDetails = result.Plans.SelectMany(p => p.Kept.Select(k => new { k.ElementId, k.Reason })).ToList(),
                Messages = result.Messages,
                Failures = result.Failures,
                Text = result.ToText(),
            };
            File.WriteAllText(cleanupPath, JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true }));
        }

        private static int CountTagged(Document doc, string runId)
        {
            try
            {
                var schema = Schema.Lookup(GeneratorSchema.ElementSchemaGuid);
                if (schema == null) return 0;
                return new FilteredElementCollector(doc)
                    .WherePasses(new ExtensibleStorageFilter(GeneratorSchema.ElementSchemaGuid))
                    .Count(e => GeneratedElementRegistry.CarriesRunIdentity(e, runId));
            }
            catch
            {
                return -1;
            }
        }
    }
}
