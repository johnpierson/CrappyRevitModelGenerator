using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Media.Imaging;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Events;
using CrappyRevitModelGenerator.Commands;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator
{
    /// <summary>
    /// Ribbon setup (plan section 5.1): one panel on the Add-Ins tab with Generate, Clean and
    /// View Last Report. Also the entry point for headless automation (see
    /// <see cref="AutomationRunner"/>): when the <c>CRMG_AUTOMATION</c> environment variable
    /// names an existing parameters file at startup, one-shot <c>Idling</c> subscription runs
    /// it on the first idle tick — after Revit has finished loading and a document driven in by
    /// the launching journal is active — then posts the Exit command. The subscription is
    /// removed the moment it fires, so a normal interactive session has nothing left running.
    /// </summary>
    public class App : IExternalApplication
    {
        public const string PanelName = "Crappy Model Generator";

        /// <summary>The last report produced in this session (any document).</summary>
        public static GenerationReport LastReport { get; set; }

        /// <summary>The last settings the user ran with, so the dialog re-opens on them.</summary>
        public static GenerationSettings LastSettings { get; set; }

        public static string RevitVersion { get; private set; }

        private UIControlledApplication _application;

        public Result OnStartup(UIControlledApplication application)
        {
            RevitVersion = application.ControlledApplication.VersionNumber;
            _application = application;

            try
            {
                CreateRibbon(application);
            }
            catch (Exception ex)
            {
                TaskDialog.Show("Crappy Revit Model Generator", "The ribbon could not be created: " + ex.Message);
                return Result.Failed;
            }

            var automationParameters = AutomationRunner.ReadParametersFromEnvironment();
            if (automationParameters != null)
                application.Idling += OnAutomationIdle;

            return Result.Succeeded;
        }

        public Result OnShutdown(UIControlledApplication application)
        {
            application.Idling -= OnAutomationIdle;
            LastReport = null;
            LastSettings = null;
            return Result.Succeeded;
        }

        /// <summary>
        /// Runs once. A document opened by the launching journal (or created from a template by
        /// <see cref="AutomationRunner"/> itself) is normally already active by the first idle
        /// tick; if not, this simply waits for the next one. Exits Revit itself when done —
        /// PostCommand queues ExitRevit, it does not run inline — so the process always
        /// terminates instead of leaving an unattended Revit window behind.
        /// </summary>
        private void OnAutomationIdle(object sender, IdlingEventArgs e)
        {
            _application.Idling -= OnAutomationIdle;

            var uiApp = sender as UIApplication;
            var parameters = AutomationRunner.ReadParametersFromEnvironment();
            if (uiApp == null || parameters == null) return;

            try
            {
                AutomationRunner.Run(uiApp, parameters, out _);
            }
            finally
            {
                try
                {
                    var exitId = RevitCommandId.LookupPostableCommandId(PostableCommand.ExitRevit);
                    if (uiApp.CanPostCommand(exitId)) uiApp.PostCommand(exitId);
                }
                catch
                {
                    // If Revit cannot be asked to exit, leaving it open beats losing the report
                    // that AutomationRunner already wrote to disk.
                }
            }
        }

        private static void CreateRibbon(UIControlledApplication app)
        {
            var panel = app.GetRibbonPanels().FirstOrDefault(p => p.Name == PanelName) ?? app.CreateRibbonPanel(PanelName);
            var assemblyPath = Assembly.GetExecutingAssembly().Location;

            AddButton(panel, assemblyPath, "CRMG_Generate", "Generate\nBad Model",
                typeof(Commands.GenerateBadModelCommand).FullName,
                "Generate a small, intentionally low-quality model in the active document. Every element is tagged so it can be removed again.",
                "generate");

            AddButton(panel, assemblyPath, "CRMG_Cleanup", "Clean\nGenerated",
                typeof(Commands.CleanupGeneratedModelCommand).FullName,
                "Remove elements created by this add-in — and only those — from the active document.",
                "cleanup");

            AddButton(panel, assemblyPath, "CRMG_Report", "View Last\nReport",
                typeof(Commands.ShowGenerationReportCommand).FullName,
                "Show the report of the last run (this session, or stored in the document).",
                "report");

            AddButton(panel, assemblyPath, "CRMG_Batch", "Batch\nGenerate",
                typeof(Commands.BatchGenerateCommand).FullName,
                "Generate several models of varying quality in one pass, each a fresh project saved to a folder you choose.",
                "batch");
        }

        private static void AddButton(RibbonPanel panel, string assemblyPath, string name, string text, string className, string tooltip, string iconName)
        {
            var data = new PushButtonData(name, text, assemblyPath, className)
            {
                ToolTip = tooltip,
                LargeImage = LoadIcon(iconName + "_32.png"),
                Image = LoadIcon(iconName + "_16.png"),
            };
            panel.AddItem(data);
        }

        /// <summary>An embedded PNG as a BitmapImage, or null when it is missing (the button then shows text only).</summary>
        private static BitmapImage LoadIcon(string fileName)
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = assembly.GetManifestResourceNames().FirstOrDefault(n => n.EndsWith("." + fileName, StringComparison.OrdinalIgnoreCase));
                if (resourceName == null) return null;

                using (var stream = assembly.GetManifestResourceStream(resourceName))
                {
                    if (stream == null) return null;
                    var image = new BitmapImage();
                    image.BeginInit();
                    image.CacheOption = BitmapCacheOption.OnLoad;
                    image.StreamSource = stream;
                    image.EndInit();
                    image.Freeze();
                    return image;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}
