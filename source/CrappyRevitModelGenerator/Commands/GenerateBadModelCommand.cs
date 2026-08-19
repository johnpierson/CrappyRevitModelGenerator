using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;
using CrappyRevitModelGenerator.UI;

namespace CrappyRevitModelGenerator.Commands
{
    /// <summary>
    /// Ribbon entry point (plan section 5.2): guard the document, show settings, validate,
    /// generate, show the report. Nothing here touches the Revit API directly except the
    /// pre-flight checks; the work is in <see cref="GenerationRunner"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class GenerateBadModelCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var uiDoc = uiApp.ActiveUIDocument;
            var doc = uiDoc?.Document;

            var safety = DocumentSafetyGuard.CheckDocument(doc);
            if (!safety.CanProceed)
            {
                TaskDialog.Show(Dialogs.Title, string.Join(Environment.NewLine, safety.Blockers));
                return Result.Cancelled;
            }

            try
            {
                var initial = App.LastSettings?.Clone() ?? new GenerationSettings();
                initial.Seed = SeededRandom.NewSeed();
                initial.ConfirmedActiveDocument = false;
                initial.DryRun = false;

                var settings = Dialogs.ShowGenerateDialog(uiApp, doc, safety, initial);
                if (settings == null) return Result.Cancelled;

                var check = DocumentSafetyGuard.CheckRun(doc, settings);
                if (!check.CanProceed)
                {
                    TaskDialog.Show(Dialogs.Title, "Cannot generate:" + Environment.NewLine + string.Join(Environment.NewLine, check.Blockers));
                    return Result.Cancelled;
                }

                App.LastSettings = settings.Clone();

                var report = GenerationRunner.Run(uiApp, doc, settings);
                App.LastReport = report;

                TryExport(report, settings);
                Dialogs.ShowReport(uiApp, report);

                return report.Aborted ? Result.Failed : Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"{Dialogs.Title} failed: {ex.Message}";
                TaskDialog.Show(Dialogs.Title, "The generator hit an unexpected error and made no further changes." + Environment.NewLine + Environment.NewLine + ex);
                return Result.Failed;
            }
        }

        /// <summary>Write the JSON report ONLY to a path the user chose (plan section 9.5).</summary>
        private static void TryExport(GenerationReport report, GenerationSettings settings)
        {
            if (string.IsNullOrWhiteSpace(settings.ReportExportPath)) return;
            try
            {
                var dir = System.IO.Path.GetDirectoryName(settings.ReportExportPath);
                if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
                System.IO.File.WriteAllText(settings.ReportExportPath, report.ToJson());
                report.AddInfo(null, "Report exported to " + settings.ReportExportPath);
            }
            catch (Exception ex)
            {
                report.AddInfo(null, $"Report export to '{settings.ReportExportPath}' failed: {ex.Message}");
            }
        }
    }
}
