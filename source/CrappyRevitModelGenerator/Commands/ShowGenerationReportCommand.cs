using System;
using Autodesk.Revit.Attributes;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;
using CrappyRevitModelGenerator.UI;

namespace CrappyRevitModelGenerator.Commands
{
    /// <summary>
    /// Shows the last report of this session, or — after a restart — the newest run record
    /// stored in the active document.
    /// </summary>
    [Transaction(TransactionMode.ReadOnly)]
    [Regeneration(RegenerationOption.Manual)]
    [Journaling(JournalingMode.NoCommandData)]
    public class ShowGenerationReportCommand : IExternalCommand
    {
        public Result Execute(ExternalCommandData commandData, ref string message, ElementSet elements)
        {
            var uiApp = commandData.Application;
            var doc = uiApp.ActiveUIDocument?.Document;

            try
            {
                var report = App.LastReport;
                if (report == null && doc != null && !doc.IsFamilyDocument)
                {
                    var latest = RunStore.ReadLatest(doc);
                    if (latest != null && !string.IsNullOrWhiteSpace(latest.ReportJson))
                    {
                        try
                        {
                            report = GenerationReport.FromJson(latest.ReportJson);
                            report.AddInfo(null, "Loaded from the run record stored in the document.");
                        }
                        catch (Exception ex)
                        {
                            TaskDialog.Show(Dialogs.Title, "A run record exists but its report could not be read: " + ex.Message);
                            return Result.Failed;
                        }
                    }
                }

                if (report == null)
                {
                    TaskDialog.Show(Dialogs.Title, "No report yet. Run 'Generate Bad Model' first.");
                    return Result.Cancelled;
                }

                Dialogs.ShowReport(uiApp, report);
                return Result.Succeeded;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return Result.Failed;
            }
        }
    }
}
