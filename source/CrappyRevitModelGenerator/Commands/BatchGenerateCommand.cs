using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;
using CrappyRevitModelGenerator.UI;

namespace CrappyRevitModelGenerator.Commands
{
    /// <summary>
    /// Generates several models in one pass: each is a fresh project created from a template,
    /// generated, saved and closed in turn, without ever activating a view or disturbing any
    /// document the user already has open. This is the practical way to produce a set of sample
    /// models of varying quality — the interactive "Generate Bad Model" command is for one
    /// document at a time; this one is for "make me 10".
    ///
    /// One <see cref="GenerationSettings.Seed"/> per model (base seed + index) and a severity
    /// per <see cref="BatchGenerateOptions.SeverityFor"/>, so no two models in a batch are
    /// identical even at the same severity, and the whole batch reproduces exactly given the
    /// same template, base seed and options.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class BatchGenerateCommand : IExternalCommand
    {
        private const string ScenarioId = "batch";

        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;

            try
            {
                var window = new BatchGenerateWindow(FindDefaultTemplate());
                if (uiApp.MainWindowHandle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(window).Owner = uiApp.MainWindowHandle;

                if (window.ShowDialog() != true || window.Result == null)
                    return Result.Cancelled;

                var options = window.Result;
                Directory.CreateDirectory(options.OutputFolder);

                var results = new List<ModelResult>();
                for (var i = 0; i < options.Count; i++)
                    results.Add(GenerateOne(uiApp, options, i));

                var manifestBase = Path.Combine(options.OutputFolder, "batch-manifest");
                var text = BuildSummary(options, results);
                var json = BuildManifestJson(options, results);
                File.WriteAllText(manifestBase + ".txt", text);
                File.WriteAllText(manifestBase + ".json", json);

                var failed = results.Count(r => r.Aborted || r.Exception != null);
                var headline = failed == 0
                    ? $"Batch complete — {results.Count} model(s) written to '{options.OutputFolder}'."
                    : $"Batch complete with {failed} problem(s) — {results.Count} model(s) attempted, see below.";

                var reportWindow = ReportWindow.ForText(Dialogs.Title + " — Batch Generate", headline, null, text, json, "crappy-batch-manifest");
                if (uiApp.MainWindowHandle != IntPtr.Zero)
                    new System.Windows.Interop.WindowInteropHelper(reportWindow).Owner = uiApp.MainWindowHandle;
                reportWindow.ShowDialog();

                return failed == 0 ? Result.Succeeded : Result.Failed;
            }
            catch (Exception ex)
            {
                message = $"Batch generate failed: {ex.Message}";
                TaskDialog.Show(Dialogs.Title, "Batch generate hit an unexpected error." + Environment.NewLine + Environment.NewLine + ex);
                return Result.Failed;
            }
        }

        private sealed class ModelResult
        {
            public int Index;
            public string FileName;
            public string FilePath;
            public int Seed;
            public GenerationSeverity Severity;
            public GenerationReport Report;
            public bool Aborted;
            public string Exception;
        }

        private static ModelResult GenerateOne(UIApplication uiApp, BatchGenerateOptions options, int index)
        {
            var seed = options.BaseSeed + index;
            var severity = options.SeverityFor(index);
            var fileName = $"Model_{index + 1:00}_{severity}_seed{seed}.rvt";
            var filePath = Path.Combine(options.OutputFolder, fileName);
            var result = new ModelResult { Index = index, FileName = fileName, FilePath = filePath, Seed = seed, Severity = severity };

            Document doc = null;
            try
            {
                doc = uiApp.Application.NewProjectDocument(options.TemplatePath);

                var settings = new GenerationSettings
                {
                    Seed = seed,
                    Severity = severity,
                    ConfirmedActiveDocument = true,
                    AllowWorksharedDocument = true,
                    SuppressAllWarningDialogs = true,
                };
                if (options.IncludeWarningsScenario)
                    settings.EnabledScenarioIds = ScenarioCatalog.DefaultEnabledIds(severity).Concat(new[] { ScenarioIds.Warnings }).ToList();

                var report = GenerationRunner.Run(uiApp, doc, settings);
                result.Report = report;
                result.Aborted = report.Aborted;

                doc.SaveAs(filePath, new SaveAsOptions { OverwriteExistingFile = true });

                var reportBase = Path.Combine(options.OutputFolder, Path.GetFileNameWithoutExtension(fileName));
                File.WriteAllText(reportBase + ".report.json", report.ToJson());
                File.WriteAllText(reportBase + ".report.txt", report.ToText());
            }
            catch (Exception ex)
            {
                result.Exception = ex.Message;
                try { File.WriteAllText(Path.Combine(options.OutputFolder, Path.GetFileNameWithoutExtension(fileName) + ".error.txt"), ex.ToString()); } catch { /* best effort */ }
            }
            finally
            {
                try { doc?.Close(false); } catch { /* already saved (or never got that far); nothing more to do */ }
            }

            return result;
        }

        // ---- Template discovery -----------------------------------------------------------

        /// <summary>Best guess at a default template, mirroring the fallback list in the plan's install docs. Never throws; returns null if nothing is found.</summary>
        private static string FindDefaultTemplate()
        {
            var version = App.RevitVersion;
            if (string.IsNullOrEmpty(version)) return null;

            var candidates = new[]
            {
                $@"C:\ProgramData\Autodesk\RVT {version}\Templates\English\Default-Multi-Discipline_Metric.rte",
                $@"C:\ProgramData\Autodesk\RVT {version}\Templates\English\DefaultMetric.rte",
                $@"C:\ProgramData\Autodesk\RVT {version}\Templates\English-Imperial\Default-Multi-discipline.rte",
                $@"C:\ProgramData\Autodesk\RVT {version}\Templates\Default_M_ENU.rte",
            };
            return candidates.FirstOrDefault(File.Exists);
        }

        // ---- Reporting ---------------------------------------------------------------------

        private static string BuildSummary(BatchGenerateOptions options, List<ModelResult> results)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Crappy Revit Model Generator - batch summary");
            sb.AppendLine("==============================================");
            sb.AppendLine($"Template:       {options.TemplatePath}");
            sb.AppendLine($"Output folder:  {options.OutputFolder}");
            sb.AppendLine($"Model count:    {options.Count}");
            sb.AppendLine($"Base seed:      {options.BaseSeed}");
            sb.AppendLine($"Severity:       {options.SeverityMode}");
            sb.AppendLine($"Warnings scenario included: {options.IncludeWarningsScenario}");
            sb.AppendLine();

            foreach (var r in results)
            {
                var status = r.Exception != null ? "ERROR" : r.Aborted ? "ABORTED" : "ok";
                sb.AppendLine($"[{status}] {r.FileName}  seed={r.Seed}  severity={r.Severity}  total elements={r.Report?.TotalElements ?? 0}  run={r.Report?.RunId}");
                if (r.Exception != null) sb.AppendLine($"    {r.Exception}");
                else if (r.Aborted) sb.AppendLine($"    {r.Report?.AbortReason}");
            }

            return sb.ToString();
        }

        private static string BuildManifestJson(BatchGenerateOptions options, List<ModelResult> results)
        {
            var payload = new
            {
                options.TemplatePath,
                options.OutputFolder,
                options.Count,
                options.BaseSeed,
                SeverityMode = options.SeverityMode.ToString(),
                options.IncludeWarningsScenario,
                GeneratedUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Models = results.Select(r => new
                {
                    r.Index,
                    r.FileName,
                    r.FilePath,
                    r.Seed,
                    Severity = r.Severity.ToString(),
                    r.Aborted,
                    r.Exception,
                    RunId = r.Report?.RunId,
                    TotalElements = r.Report?.TotalElements ?? 0,
                    Counts = r.Report?.Counts,
                }).ToList(),
            };
            return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
        }
    }
}
