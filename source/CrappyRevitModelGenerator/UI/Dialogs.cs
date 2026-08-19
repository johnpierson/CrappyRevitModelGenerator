using System;
using System.Collections.Generic;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.UI
{
    /// <summary>
    /// The UI surface the commands talk to. Four static entry points wrap the WPF windows
    /// (<see cref="GenerateWindow"/>, <see cref="ReportWindow"/>, <see cref="CleanupWindow"/>),
    /// parent them to Revit's main window and show them modally, so the command layer never
    /// touches WPF and the windows never touch the Revit API. Report display falls back to a
    /// TaskDialog if the WPF window cannot be shown: a report is too valuable to lose to a UI
    /// hiccup.
    /// </summary>
    public static class Dialogs
    {
        public const string Title = "Crappy Revit Model Generator";

        /// <summary>
        /// Show the settings dialog. Returns the settings to run with, or null when the user
        /// cancelled. <paramref name="safety"/> carries the pre-flight warnings the dialog must
        /// surface (workshared, unsaved changes) so the user can acknowledge them.
        /// </summary>
        public static GenerationSettings ShowGenerateDialog(UIApplication uiApp, Document doc, SafetyCheckResult safety, GenerationSettings initial)
        {
            var settings = initial?.Clone() ?? new GenerationSettings { Seed = SeededRandom.NewSeed() };
            var window = new GenerateWindow(doc?.Title, safety ?? new SafetyCheckResult(), settings);
            OwnToRevit(window, uiApp);

            var accepted = window.ShowDialog() == true;
            return accepted ? window.Result : null;
        }

        public static void ShowReport(UIApplication uiApp, GenerationReport report)
        {
            if (report == null)
            {
                TaskDialog.Show(Title, "There is no report to show.");
                return;
            }

            string text;
            try
            {
                text = report.ToText();
            }
            catch (Exception ex)
            {
                text = "The report could not be rendered as text: " + ex;
            }

            string json;
            try
            {
                json = report.ToJson();
            }
            catch (Exception ex)
            {
                json = null;
                text += Environment.NewLine + "(JSON serialisation failed: " + ex.Message + ")";
            }

            var instruction = report.Aborted ? "Generation aborted" : report.DryRun ? "Dry run complete" : "Generation complete";
            try
            {
                var window = ReportWindow.ForGeneration(report, text, json);
                OwnToRevit(window, uiApp);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowTextFallback(instruction, text, ex);
            }
        }

        /// <summary>Let the user pick runs to remove. Returns the selection, or null/empty when cancelled.</summary>
        public static IReadOnlyList<RunRecord> ShowCleanupDialog(UIApplication uiApp, Document doc, IReadOnlyList<RunRecord> runs)
        {
            if (runs == null || runs.Count == 0)
            {
                TaskDialog.Show(Title, "No generated runs were found in this document.");
                return null;
            }

            var window = new CleanupWindow(doc?.Title, runs);
            OwnToRevit(window, uiApp);

            var accepted = window.ShowDialog() == true;
            var selected = accepted ? window.SelectedRuns : null;
            return selected == null || selected.Count == 0 ? null : selected;
        }

        public static void ShowCleanupResult(UIApplication uiApp, CleanupResult result)
        {
            if (result == null)
            {
                TaskDialog.Show(Title, "Cleanup produced no result.");
                return;
            }

            string text;
            try
            {
                text = result.ToText();
            }
            catch (Exception ex)
            {
                text = "The cleanup result could not be rendered as text: " + ex;
            }

            var headline = $"Cleanup complete — {result.Deleted} element(s) deleted, {result.Kept} kept, {result.AlreadyGone} already gone, {result.RunRecordsRemoved} run record(s) removed.";
            var summary = result.Failures.Count > 0
                ? $"{result.Failures.Count} failure(s) were recorded; see below."
                : result.Kept > 0 ? "Kept elements are listed with the reason each one stayed." : null;

            try
            {
                var window = ReportWindow.ForText(Title + " — Cleanup", headline, summary, text, null, "crappy-model-cleanup");
                OwnToRevit(window, uiApp);
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                ShowTextFallback("Cleanup complete", text, ex);
            }
        }

        // ---- Helpers ---------------------------------------------------------------------

        /// <summary>
        /// Parent a WPF window to Revit's main window so it stays on top of Revit, centres on it
        /// and does not appear as a separate taskbar entry. An unowned window still works, so
        /// failures here are swallowed.
        /// </summary>
        private static void OwnToRevit(System.Windows.Window window, UIApplication uiApp)
        {
            if (window == null) return;
            try
            {
                var handle = uiApp?.MainWindowHandle ?? IntPtr.Zero;
                if (handle == IntPtr.Zero) return;
                var helper = new System.Windows.Interop.WindowInteropHelper(window);
                helper.Owner = handle;
            }
            catch
            {
                // Unowned is acceptable; the dialog is still modal to the calling thread.
            }
        }

        /// <summary>Revit's own dialog with the (truncated) text when the WPF window could not be shown.</summary>
        private static void ShowTextFallback(string instruction, string text, Exception cause)
        {
            const int limit = 4000;
            var body = text ?? string.Empty;
            if (body.Length > limit) body = body.Substring(0, limit) + "…";

            var dialog = new TaskDialog(Title)
            {
                MainInstruction = instruction,
                MainContent = body,
                ExpandedContent = "The report window could not be shown: " + cause?.Message,
                CommonButtons = TaskDialogCommonButtons.Close,
            };
            dialog.Show();
        }
    }
}
