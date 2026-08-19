using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Media;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.UI
{
    /// <summary>
    /// Collects the parameters for a batch run (template, output folder, count, seed, severity
    /// spread). Plain code-behind, same pattern as <see cref="GenerateWindow"/>: read every
    /// control into a fresh <see cref="BatchGenerateOptions"/> and validate before the window is
    /// allowed to close. The window never touches Revit or the file system beyond checking that
    /// paths look plausible; the command performs the actual batch.
    /// </summary>
    public partial class BatchGenerateWindow : Window
    {
        private const int MinCount = 1;
        private const int MaxCount = 50;

        private static readonly Brush ErrorBrush = Brushes.Firebrick;

        /// <summary>Set once controls are populated; input events fire during population and must not validate yet.</summary>
        private bool _ready;

        public BatchGenerateOptions Result { get; private set; }

        public BatchGenerateWindow(string defaultTemplatePath)
        {
            InitializeComponent();
            Populate(defaultTemplatePath);
            _ready = true;
            Refresh();
        }

        private void Populate(string defaultTemplatePath)
        {
            TemplateBox.Text = defaultTemplatePath ?? string.Empty;
            OutputFolderBox.Text = DefaultOutputFolder();
            CountBox.Text = "10";
            CountHint.Text = $"{MinCount}–{MaxCount} models.";
            BaseSeedBox.Text = SeededRandom.NewSeed().ToString(CultureInfo.InvariantCulture);

            SeverityModeBox.ItemsSource = new[]
            {
                BatchSeverityMode.CycleLowMediumHigh,
                BatchSeverityMode.AllLow,
                BatchSeverityMode.AllMedium,
                BatchSeverityMode.AllHigh,
            };
            SeverityModeBox.SelectedItem = BatchSeverityMode.CycleLowMediumHigh;

            WarningsBox.IsChecked = false;
        }

        private static string DefaultOutputFolder()
        {
            try
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                return Path.Combine(documents, "CrappyRevitModelGenerator", "Batch " + DateTime.Now.ToString("yyyy-MM-dd HHmm", CultureInfo.InvariantCulture));
            }
            catch
            {
                return string.Empty;
            }
        }

        // ---- Browsing ----------------------------------------------------------------------

        private void OnBrowseTemplate(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFileDialog
                {
                    Title = "Choose a project template",
                    Filter = "Revit template (*.rte)|*.rte|All files (*.*)|*.*",
                    CheckFileExists = true,
                };
                var current = TemplateBox.Text?.Trim();
                if (!string.IsNullOrEmpty(current) && File.Exists(current)) dialog.FileName = current;

                if (dialog.ShowDialog(this) == true) TemplateBox.Text = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The file dialog could not be shown: " + ex.Message, Dialogs.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnBrowseOutputFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.OpenFolderDialog { Title = "Choose an output folder" };
                var current = OutputFolderBox.Text?.Trim();
                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        var existing = Directory.Exists(current) ? current : Path.GetDirectoryName(Path.GetFullPath(current));
                        if (!string.IsNullOrEmpty(existing) && Directory.Exists(existing)) dialog.InitialDirectory = existing;
                    }
                    catch
                    {
                        // Not a usable path; the dialog just opens at its default location.
                    }
                }

                if (dialog.ShowDialog(this) == true) OutputFolderBox.Text = dialog.FolderName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The folder dialog could not be shown: " + ex.Message, Dialogs.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnNewSeed(object sender, RoutedEventArgs e)
        {
            BaseSeedBox.Text = SeededRandom.NewSeed().ToString(CultureInfo.InvariantCulture);
        }

        // ---- Validation ----------------------------------------------------------------------

        private void OnInputChanged(object sender, RoutedEventArgs e) => Refresh();

        private BatchGenerateOptions Build(out string error)
        {
            error = null;
            var template = (TemplateBox.Text ?? string.Empty).Trim();
            var outputFolder = (OutputFolderBox.Text ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(template)) { error = "Choose a template (.rte) to create each model from."; return null; }
            if (!File.Exists(template)) { error = "The template file does not exist."; return null; }
            if (string.IsNullOrEmpty(outputFolder)) { error = "Choose an output folder."; return null; }

            if (!int.TryParse((CountBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var count) &&
                !int.TryParse((CountBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out count))
            {
                error = "Model count must be a whole number.";
                return null;
            }
            if (count < MinCount || count > MaxCount) { error = $"Model count must be between {MinCount} and {MaxCount}."; return null; }

            if (!int.TryParse((BaseSeedBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out var baseSeed) &&
                !int.TryParse((BaseSeedBox.Text ?? string.Empty).Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out baseSeed))
            {
                error = "Base seed must be a whole number.";
                return null;
            }

            return new BatchGenerateOptions
            {
                TemplatePath = template,
                OutputFolder = outputFolder,
                Count = count,
                BaseSeed = baseSeed,
                SeverityMode = SeverityModeBox.SelectedItem is BatchSeverityMode mode ? mode : BatchSeverityMode.CycleLowMediumHigh,
                IncludeWarningsScenario = WarningsBox.IsChecked == true,
            };
        }

        private void Refresh()
        {
            if (!_ready) return;

            var options = Build(out var error);
            if (error != null)
            {
                MessagesText.Text = "✖ " + error;
                MessagesText.Foreground = ErrorBrush;
                StartButton.IsEnabled = false;
                return;
            }

            MessagesText.Foreground = Brushes.Black;
            MessagesText.Text = $"Will create {options.Count} model(s) in '{options.OutputFolder}', seeds {options.BaseSeed}–{options.BaseSeed + options.Count - 1}, " +
                                 $"severity {DescribeSeverity(options)}.";
            StartButton.IsEnabled = true;
        }

        private static string DescribeSeverity(BatchGenerateOptions options)
        {
            switch (options.SeverityMode)
            {
                case BatchSeverityMode.AllLow: return "all Low";
                case BatchSeverityMode.AllMedium: return "all Medium";
                case BatchSeverityMode.AllHigh: return "all High";
                default: return "cycling Low → Medium → High";
            }
        }

        private void OnStart(object sender, RoutedEventArgs e)
        {
            var options = Build(out var error);
            if (error != null)
            {
                Refresh();
                return;
            }
            Result = options;
            DialogResult = true;
        }
    }
}
