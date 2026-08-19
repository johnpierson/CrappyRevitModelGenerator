using System;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using CrappyRevitModelGenerator.Core;
using Visibility = System.Windows.Visibility;

namespace CrappyRevitModelGenerator.UI
{
    /// <summary>
    /// Read-only viewer for a run report or a cleanup result (plan section 9.5): the plain-text
    /// rendering in a monospace box, the JSON on a second tab when there is any, and Copy /
    /// Export / Close. Export writes only where the user points the file dialog. The window is
    /// content-agnostic (title + headline + text + optional JSON) so both the generation report
    /// and the cleanup summary reuse it.
    /// </summary>
    public partial class ReportWindow : Window
    {
        private static readonly Brush ProblemBrush = Brushes.Firebrick;
        private static readonly Brush CautionBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB3, 0x5C, 0x00));
        private static readonly Brush OkBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x2E, 0x7D, 0x32));
        private static readonly Brush NeutralBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x1F, 0x5F, 0x9F));

        private readonly string _text;
        private readonly string _json;
        private readonly string _exportBaseName;

        /// <summary>
        /// Generic constructor. <paramref name="json"/> may be null: the JSON tab is then hidden and
        /// Export writes the text as .txt instead.
        /// </summary>
        public ReportWindow(string title, string headline, string summary, string text, string json, string exportBaseName)
            : this(title, headline, summary, text, json, exportBaseName, null)
        {
        }

        private ReportWindow(string title, string headline, string summary, string text, string json, string exportBaseName, Brush headlineBrush)
        {
            _text = text ?? string.Empty;
            _json = string.IsNullOrWhiteSpace(json) ? null : json;
            _exportBaseName = SanitizeFileName(string.IsNullOrWhiteSpace(exportBaseName) ? "crappy-revit-model-report" : exportBaseName);

            InitializeComponent();

            Title = string.IsNullOrWhiteSpace(title) ? Dialogs.Title : title;
            HeadlineText.Text = headline ?? string.Empty;
            if (headlineBrush != null) HeadlineText.Foreground = headlineBrush;
            SummaryText.Text = summary ?? string.Empty;
            SummaryText.Visibility = string.IsNullOrEmpty(SummaryText.Text) ? Visibility.Collapsed : Visibility.Visible;

            TextView.Text = _text;
            if (_json != null)
            {
                JsonView.Text = _json;
            }
            else
            {
                JsonTab.Visibility = Visibility.Collapsed;
                ExportButton.Content = "Export text…";
            }
            Tabs.SelectedItem = TextTab;
        }

        /// <summary>The window for a generation report: title carries run id and Aborted / Dry run state.</summary>
        public static ReportWindow ForGeneration(GenerationReport report, string text, string json)
        {
            if (report == null) throw new ArgumentNullException(nameof(report));

            var state = report.Aborted ? " (ABORTED)" : report.DryRun ? " (dry run)" : string.Empty;
            var title = $"{Dialogs.Title} — Report {report.RunId ?? "?"}{state}";

            string headline;
            Brush brush;
            if (report.Aborted)
            {
                headline = "Generation ABORTED" + (string.IsNullOrWhiteSpace(report.AbortReason) ? string.Empty : ": " + report.AbortReason);
                brush = ProblemBrush;
            }
            else if (report.DryRun)
            {
                headline = $"Dry run complete — nothing was created. Approximately {report.TotalElements} element(s) would be generated.";
                brush = NeutralBrush;
            }
            else
            {
                var failures = report.Failures?.Count ?? 0;
                var rolledBack = report.RolledBackScenarios.Count();
                headline = $"Generation complete — {report.TotalElements} element(s), {report.Defects.Count()} intentional defect(s), " +
                           $"{report.ExpectedWarnings?.Count ?? 0} expected warning(s), {failures} unexpected failure(s)" +
                           (rolledBack > 0 ? $", {rolledBack} scenario(s) rolled back" : string.Empty) + ".";
                brush = failures > 0 || rolledBack > 0 ? CautionBrush : OkBrush;
            }

            var summary = $"Run {report.RunId ?? "?"} · seed {report.Seed} · {report.DocumentTitle ?? "(untitled)"} · Revit {report.RevitVersion ?? "?"} · generator {report.GeneratorVersion ?? "?"}";
            var baseName = "crappy-model-report-" + (report.RunId ?? "run");
            return new ReportWindow(title, headline, summary, text, json, baseName, brush);
        }

        /// <summary>The window for any other text (cleanup results).</summary>
        public static ReportWindow ForText(string title, string headline, string summary, string text, string json = null, string exportBaseName = null) =>
            new ReportWindow(title, headline, summary, text, json, exportBaseName, null);

        // ---- Buttons ---------------------------------------------------------------------

        private string CurrentText => Tabs.SelectedItem == JsonTab && _json != null ? _json : _text;

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetDataObject(CurrentText, true);
                StatusText.Text = Tabs.SelectedItem == JsonTab ? "JSON copied to the clipboard." : "Report copied to the clipboard.";
            }
            catch (Exception ex)
            {
                // Clipboard access fails when another process holds it; not worth more than a note.
                StatusText.Text = "Copy failed: " + ex.Message;
            }
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            var isJson = _json != null;
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = isJson ? "Export JSON report" : "Export report text",
                    Filter = isJson ? "JSON report (*.json)|*.json|All files (*.*)|*.*" : "Text file (*.txt)|*.txt|All files (*.*)|*.*",
                    DefaultExt = isJson ? ".json" : ".txt",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = _exportBaseName + (isJson ? ".json" : ".txt"),
                };
                if (dialog.ShowDialog(this) != true) return;

                var directory = System.IO.Path.GetDirectoryName(dialog.FileName);
                if (!string.IsNullOrEmpty(directory)) System.IO.Directory.CreateDirectory(directory);
                System.IO.File.WriteAllText(dialog.FileName, isJson ? _json : _text);
                StatusText.Text = "Exported to " + dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The report could not be exported:" + Environment.NewLine + ex.Message,
                    Dialogs.Title, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SanitizeFileName(string name)
        {
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            var chars = name.Select(c => invalid.Contains(c) ? '-' : c).ToArray();
            var clean = new string(chars).Trim();
            return string.IsNullOrEmpty(clean) ? "crappy-revit-model-report" : clean;
        }
    }
}
