using System;
using System.Linq;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Revit;
using CrappyRevitModelGenerator.UI;

namespace CrappyRevitModelGenerator.Commands
{
    /// <summary>
    /// Removes what previous runs created and nothing else (plan section 6). Reads the run
    /// records from the document, asks for confirmation, delegates to <see cref="CleanupRunner"/>.
    /// </summary>
    [Transaction(TransactionMode.Manual)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class CleanupGeneratedModelCommand : IExternalCommand
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
                var runs = RunStore.ReadAll(doc);
                var selected = Dialogs.ShowCleanupDialog(uiApp, doc, runs);
                if (selected == null || selected.Count == 0) return Result.Cancelled;

                var result = CleanupRunner.Run(uiDoc, selected);
                Dialogs.ShowCleanupResult(uiApp, result);

                if (App.LastReport != null && selected.Any(r => r.RunId == App.LastReport.RunId))
                    App.LastReport.AddNote(Core.ReportNote.KindCleanup, "cleanup", $"Cleaned up: {result.Deleted} deleted, {result.Kept} kept.");

                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = $"{Dialogs.Title} cleanup failed: {ex.Message}";
                TaskDialog.Show(Dialogs.Title, "Cleanup hit an unexpected error." + Environment.NewLine + Environment.NewLine + ex);
                return Result.Failed;
            }
        }
    }
}
