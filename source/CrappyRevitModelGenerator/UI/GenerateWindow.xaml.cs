using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;
using ValidationResult = CrappyRevitModelGenerator.Core.ValidationResult;
using Visibility = System.Windows.Visibility;

namespace CrappyRevitModelGenerator.UI
{
    /// <summary>
    /// The settings dialog (plan section 10): run setup, model content, scenario toggles and the
    /// safety block, with a live element estimate that re-runs <see cref="GenerationSettings.Validate"/>
    /// and <see cref="ElementCountEstimator"/> on every change. Plain code-behind: the controls
    /// are read into a fresh <see cref="GenerationSettings"/> each time, so there is exactly one
    /// place (<see cref="BuildSettings"/>) that maps UI to settings. The window never touches
    /// Revit; the caller owns it to Revit's main window and shows it modally.
    /// </summary>
    public partial class GenerateWindow : Window
    {
        private static readonly Brush ErrorBrush = Brushes.Firebrick;
        private static readonly Brush WarningBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xB3, 0x5C, 0x00));
        private static readonly Brush HintBrush = Brushes.Gray;
        private static readonly Brush InvalidBackground = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0xFF, 0xE9, 0xE9));

        private readonly string _documentTitle;
        private readonly SafetyCheckResult _safety;
        private readonly GenerationSettings _initial;
        private readonly List<CheckBox> _scenarioBoxes = new List<CheckBox>();

        /// <summary>Set once controls are populated; input events fire during population and must not refresh yet.</summary>
        private bool _ready;

        /// <summary>The settings to run with. Only meaningful when ShowDialog returned true.</summary>
        public GenerationSettings Result { get; private set; }

        public GenerateWindow(string documentTitle, SafetyCheckResult safety, GenerationSettings initial)
        {
            _documentTitle = string.IsNullOrWhiteSpace(documentTitle) ? "(untitled)" : documentTitle;
            _safety = safety ?? new SafetyCheckResult();
            _initial = (initial ?? new GenerationSettings { Seed = SeededRandom.NewSeed() }).Clone();

            InitializeComponent();
            FitToWorkArea();
            Populate();
            _ready = true;
            Refresh();
        }

        /// <summary>The default size assumes a desktop monitor; do not open taller than a small laptop screen.</summary>
        private void FitToWorkArea()
        {
            try
            {
                var work = SystemParameters.WorkArea;
                if (Height > work.Height - 24) Height = Math.Max(MinHeight, work.Height - 24);
                if (Width > work.Width - 24) Width = Math.Max(MinWidth, work.Width - 24);
            }
            catch
            {
                // Keep the XAML defaults.
            }
        }

        // ---- Population ------------------------------------------------------------------

        private void Populate()
        {
            Title = Dialogs.Title + " — Generate Bad Model";
            DocumentText.Text = "Active document: " + _documentTitle;

            if (_safety.Warnings.Count > 0)
            {
                WarningsPanel.Visibility = Visibility.Visible;
                foreach (var warning in _safety.Warnings)
                {
                    WarningsList.Children.Add(new TextBlock
                    {
                        Text = "⚠ " + warning,
                        TextWrapping = TextWrapping.Wrap,
                        Margin = new Thickness(0, 1, 0, 1),
                    });
                }
            }

            // Run setup
            SeedBox.Text = _initial.Seed.ToString(CultureInfo.InvariantCulture);
            SeverityBox.ItemsSource = new[] { GenerationSeverity.Low, GenerationSeverity.Medium, GenerationSeverity.High };
            SeverityBox.SelectedItem = Enum.IsDefined(typeof(GenerationSeverity), _initial.Severity) ? _initial.Severity : GenerationSeverity.Medium;
            DryRunBox.IsChecked = _initial.DryRun;

            // Model content — labels carry the limits so they can never go stale.
            LevelsLabel.Text = $"Levels ({GenerationLimits.MinLevels}–{GenerationLimits.MaxLevels})";
            ContentLimitsHint.Text =
                $"Footprint {GenerationLimits.MinFootprintMm:0}–{GenerationLimits.MaxFootprintMm:0} mm per side, " +
                $"level height {GenerationLimits.MinLevelHeightMm:0}–{GenerationLimits.MaxLevelHeightMm:0} mm.";
            LevelCountBox.Text = _initial.LevelCount.ToString(CultureInfo.CurrentCulture);
            FootprintWidthBox.Text = FormatMm(_initial.FootprintWidthMm);
            FootprintDepthBox.Text = FormatMm(_initial.FootprintDepthMm);
            LevelHeightBox.Text = FormatMm(_initial.LevelHeightMm);
            CreateFloorsBox.IsChecked = _initial.CreateFloors;
            DoorsWindowsBox.IsChecked = _initial.CreateDoorsAndWindows;
            FurnitureBox.IsChecked = _initial.CreateFurniture;
            RoomsBox.IsChecked = _initial.CreateRooms;

            // Scenarios — one box per catalog entry, in execution order.
            var explicitIds = _initial.EnabledScenarioIds == null
                ? null
                : new HashSet<string>(_initial.EnabledScenarioIds, StringComparer.OrdinalIgnoreCase);

            foreach (var definition in ScenarioCatalog.All.OrderBy(s => s.Order))
            {
                // The warnings entry's display name already says "(high risk)"; do not repeat it.
                var alreadyStated = definition.DisplayName.IndexOf(
                    $"({definition.Risk} risk)", StringComparison.OrdinalIgnoreCase) >= 0;
                var label = new TextBlock
                {
                    Text = alreadyStated ? definition.DisplayName : $"{definition.DisplayName} ({definition.Risk} risk)",
                    TextWrapping = TextWrapping.Wrap,
                };
                if (definition.Risk == ScenarioRisk.High)
                {
                    label.Foreground = ErrorBrush;
                    label.FontWeight = FontWeights.SemiBold;
                    label.Text += " — creates conditions Revit will flag";
                }
                else if (definition.Required)
                {
                    label.Text += " — always runs";
                }

                var box = new CheckBox
                {
                    Content = label,
                    Tag = definition.Id,
                    ToolTip = definition.Description,
                    IsEnabled = !definition.Required,
                    IsChecked = definition.Required || (explicitIds != null ? explicitIds.Contains(definition.Id) : definition.DefaultEnabled),
                    Margin = new Thickness(0, 3, 0, 3),
                };
                ToolTipService.SetShowDuration(box, 20000);
                ToolTipService.SetShowOnDisabled(box, true);
                box.Checked += OnInputChanged;
                box.Unchecked += OnInputChanged;
                ScenariosPanel.Children.Add(box);
                _scenarioBoxes.Add(box);
            }

            // Safety
            MaxElementsBox.Text = Math.Min(Math.Max(_initial.MaxElements, GenerationLimits.MinMaxElements), GenerationLimits.HardMaxElements)
                .ToString(CultureInfo.CurrentCulture);
            MaxElementsHint.Text = $"Hard cap {GenerationLimits.HardMaxElements}. The run refuses to start when the estimate exceeds this.";

            ConfirmBox.Content = new TextBlock
            {
                Text = $"I understand content will be created in the ACTIVE document '{_documentTitle}'",
                TextWrapping = TextWrapping.Wrap,
            };
            ConfirmBox.IsChecked = _initial.ConfirmedActiveDocument;

            WorksharedBox.IsEnabled = _safety.IsWorkshared;
            WorksharedBox.IsChecked = _safety.IsWorkshared && _initial.AllowWorksharedDocument;
            if (!_safety.IsWorkshared)
            {
                WorksharedBox.ToolTip = "The active document is not workshared, so this does not apply.";
                ToolTipService.SetShowOnDisabled(WorksharedBox, true);
            }

            SuppressBox.IsChecked = _initial.SuppressAllWarningDialogs;
            ExportPathBox.Text = _initial.ReportExportPath ?? string.Empty;
        }

        // ---- UI -> settings --------------------------------------------------------------

        /// <summary>
        /// Read every control into a settings object. Parse problems are appended to
        /// <paramref name="errors"/> and the offending box is highlighted; the previous/initial
        /// value is used so the rest of the estimate still works.
        /// </summary>
        private GenerationSettings BuildSettings(List<string> errors)
        {
            var settings = _initial.Clone();

            settings.Seed = ReadInt(SeedBox, "Seed", errors, settings.Seed);
            settings.Severity = SeverityBox.SelectedItem is GenerationSeverity severity ? severity : GenerationSeverity.Medium;
            settings.DryRun = DryRunBox.IsChecked == true;

            settings.LevelCount = ReadInt(LevelCountBox, "Level count", errors, settings.LevelCount);
            settings.FootprintWidthMm = ReadDouble(FootprintWidthBox, "Footprint width", errors, settings.FootprintWidthMm);
            settings.FootprintDepthMm = ReadDouble(FootprintDepthBox, "Footprint depth", errors, settings.FootprintDepthMm);
            settings.LevelHeightMm = ReadDouble(LevelHeightBox, "Level height", errors, settings.LevelHeightMm);
            settings.CreateFloors = CreateFloorsBox.IsChecked == true;
            settings.CreateDoorsAndWindows = DoorsWindowsBox.IsChecked == true;
            settings.CreateFurniture = FurnitureBox.IsChecked == true;
            settings.CreateRooms = RoomsBox.IsChecked == true;

            // Always explicit: what the user sees ticked is what runs, whatever the defaults do later.
            settings.EnabledScenarioIds = _scenarioBoxes
                .Where(b => b.IsChecked == true)
                .Select(b => b.Tag as string)
                .Where(id => !string.IsNullOrEmpty(id))
                .ToList();

            settings.MaxElements = ReadInt(MaxElementsBox, "Max elements", errors, settings.MaxElements);
            settings.ConfirmedActiveDocument = ConfirmBox.IsChecked == true;
            settings.AllowWorksharedDocument = _safety.IsWorkshared && WorksharedBox.IsChecked == true;
            settings.SuppressAllWarningDialogs = SuppressBox.IsChecked == true;
            settings.ReportExportPath = string.IsNullOrWhiteSpace(ExportPathBox.Text) ? null : ExportPathBox.Text.Trim();

            return settings;
        }

        private static int ReadInt(TextBox box, string label, List<string> errors, int fallback)
        {
            var text = (box.Text ?? string.Empty).Trim();
            const NumberStyles styles = NumberStyles.Integer | NumberStyles.AllowThousands;
            if (int.TryParse(text, styles, CultureInfo.CurrentCulture, out var value) ||
                int.TryParse(text, styles, CultureInfo.InvariantCulture, out value))
            {
                MarkValid(box);
                return value;
            }

            MarkInvalid(box, $"{label} must be a whole number.");
            errors.Add($"{label} must be a whole number.");
            return fallback;
        }

        private static double ReadDouble(TextBox box, string label, List<string> errors, double fallback)
        {
            var text = (box.Text ?? string.Empty).Trim();
            const NumberStyles styles = NumberStyles.Float | NumberStyles.AllowThousands;
            if ((double.TryParse(text, styles, CultureInfo.CurrentCulture, out var value) ||
                 double.TryParse(text, styles, CultureInfo.InvariantCulture, out value)) &&
                !double.IsNaN(value) && !double.IsInfinity(value))
            {
                MarkValid(box);
                return value;
            }

            MarkInvalid(box, $"{label} must be a number (millimetres).");
            errors.Add($"{label} must be a number (millimetres).");
            return fallback;
        }

        private static void MarkInvalid(TextBox box, string message)
        {
            box.Background = InvalidBackground;
            box.BorderBrush = ErrorBrush;
            box.ToolTip = message;
        }

        private static void MarkValid(TextBox box)
        {
            box.ClearValue(TextBox.BackgroundProperty);
            box.ClearValue(TextBox.BorderBrushProperty);
            box.ClearValue(TextBox.ToolTipProperty);
        }

        private static string FormatMm(double value) =>
            double.IsNaN(value) || double.IsInfinity(value) ? string.Empty : value.ToString("0.###", CultureInfo.CurrentCulture);

        // ---- Live estimate ---------------------------------------------------------------

        private void OnInputChanged(object sender, RoutedEventArgs e) => Refresh();

        private void Refresh()
        {
            if (!_ready) return;

            var parseErrors = new List<string>();
            var errors = new List<string>();
            var warnings = new List<string>();
            var hints = new List<string>();

            GenerationSettings settings = null;
            try
            {
                settings = BuildSettings(parseErrors);
            }
            catch (Exception ex)
            {
                parseErrors.Add("Could not read the settings: " + ex.Message);
            }
            errors.AddRange(parseErrors);

            ValidationResult validation = null;
            if (settings != null)
            {
                try
                {
                    validation = settings.Validate();
                    errors.AddRange(validation.Errors);
                    warnings.AddRange(validation.Warnings);
                }
                catch (Exception ex)
                {
                    errors.Add("Validation failed: " + ex.Message);
                }
            }

            // The planners assume in-range inputs; only estimate once every box parses and the
            // model settings are sane. An estimate that merely exceeds Max elements still shows,
            // so the user can see what to trim.
            ElementCountEstimate estimate = null;
            if (settings != null && parseErrors.Count == 0 && WithinPlannerLimits(settings))
            {
                try
                {
                    estimate = ElementCountEstimator.Estimate(settings);
                }
                catch (Exception ex)
                {
                    errors.Add("Estimate failed: " + ex.Message);
                }
            }

            EstimateText.Text = estimate == null
                ? "Estimate unavailable until every field is a number and the model settings are within their limits."
                : $"≈ {estimate.Total} elements: {DescribeEstimate(estimate)}";
            if (estimate == null) EstimateText.Foreground = HintBrush;
            else EstimateText.ClearValue(TextBlock.ForegroundProperty);

            var valid = errors.Count == 0 && validation != null && validation.IsValid;
            var dryRun = DryRunBox.IsChecked == true;
            var confirmed = ConfirmBox.IsChecked == true;
            var worksharedOk = !_safety.IsWorkshared || WorksharedBox.IsChecked == true;

            if (valid && !dryRun)
            {
                if (!confirmed) hints.Add("Tick the confirmation box under Safety to enable Generate.");
                if (!worksharedOk) hints.Add("Tick 'Allow workshared documents' to generate in this workshared document.");
            }

            GenerateButton.IsEnabled = valid && (dryRun || (confirmed && worksharedOk));
            GenerateButton.Content = dryRun ? "Generate (dry run)" : "Generate";
            DryRunButton.IsEnabled = valid;

            ShowMessages(errors, warnings, hints);
        }

        private static bool WithinPlannerLimits(GenerationSettings s) =>
            s.LevelCount >= GenerationLimits.MinLevels && s.LevelCount <= GenerationLimits.MaxLevels &&
            s.FootprintWidthMm >= GenerationLimits.MinFootprintMm && s.FootprintWidthMm <= GenerationLimits.MaxFootprintMm &&
            s.FootprintDepthMm >= GenerationLimits.MinFootprintMm && s.FootprintDepthMm <= GenerationLimits.MaxFootprintMm &&
            s.LevelHeightMm >= GenerationLimits.MinLevelHeightMm && s.LevelHeightMm <= GenerationLimits.MaxLevelHeightMm;

        /// <summary>"Walls 30, Doors 12, …" in category (not alphabetical) order, skipping zeros.</summary>
        private static string DescribeEstimate(ElementCountEstimate estimate)
        {
            var parts = new List<string>();
            foreach (GeneratedCategory category in Enum.GetValues(typeof(GeneratedCategory)))
            {
                var count = estimate.Of(category);
                if (count > 0) parts.Add($"{category} {count}");
            }
            return parts.Count == 0 ? "nothing" : string.Join(", ", parts);
        }

        private void ShowMessages(List<string> errors, List<string> warnings, List<string> hints)
        {
            MessagesText.Inlines.Clear();
            var any = false;

            foreach (var line in errors.Distinct())
            {
                if (any) MessagesText.Inlines.Add(new LineBreak());
                MessagesText.Inlines.Add(new Run("✖ " + line) { Foreground = ErrorBrush });
                any = true;
            }
            foreach (var line in warnings.Distinct())
            {
                if (any) MessagesText.Inlines.Add(new LineBreak());
                MessagesText.Inlines.Add(new Run("⚠ " + line) { Foreground = WarningBrush });
                any = true;
            }
            foreach (var line in hints.Distinct())
            {
                if (any) MessagesText.Inlines.Add(new LineBreak());
                MessagesText.Inlines.Add(new Run(line) { Foreground = HintBrush });
                any = true;
            }

            MessagesText.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        }

        // ---- Buttons ---------------------------------------------------------------------

        private void OnNewSeed(object sender, RoutedEventArgs e)
        {
            SeedBox.Text = SeededRandom.NewSeed().ToString(CultureInfo.InvariantCulture);
        }

        private void OnBrowseExport(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    Title = "Export JSON report",
                    Filter = "JSON report (*.json)|*.json|All files (*.*)|*.*",
                    DefaultExt = ".json",
                    AddExtension = true,
                    OverwritePrompt = true,
                    FileName = "crappy-revit-model-report.json",
                };

                var current = ExportPathBox.Text?.Trim();
                if (!string.IsNullOrEmpty(current))
                {
                    try
                    {
                        var full = System.IO.Path.GetFullPath(current);
                        var directory = System.IO.Path.GetDirectoryName(full);
                        var name = System.IO.Path.GetFileName(full);
                        if (!string.IsNullOrEmpty(directory) && System.IO.Directory.Exists(directory)) dialog.InitialDirectory = directory;
                        if (!string.IsNullOrEmpty(name)) dialog.FileName = name;
                    }
                    catch
                    {
                        // Not a usable path; the dialog just opens at its default location.
                    }
                }

                if (dialog.ShowDialog(this) == true)
                    ExportPathBox.Text = dialog.FileName;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "The file dialog could not be shown: " + ex.Message, Dialogs.Title, MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnGenerate(object sender, RoutedEventArgs e) => Finish(DryRunBox.IsChecked == true);

        private void OnDryRun(object sender, RoutedEventArgs e) => Finish(dryRun: true);

        private void Finish(bool dryRun)
        {
            var errors = new List<string>();
            var settings = BuildSettings(errors);
            settings.DryRun = dryRun;

            ValidationResult validation;
            try
            {
                validation = settings.Validate();
            }
            catch (Exception ex)
            {
                errors.Add("Validation failed: " + ex.Message);
                validation = null;
            }

            if (errors.Count > 0 || validation == null || !validation.IsValid)
            {
                Refresh();
                return;
            }

            if (!dryRun && (!settings.ConfirmedActiveDocument || (_safety.IsWorkshared && !settings.AllowWorksharedDocument)))
            {
                Refresh();
                return;
            }

            Result = settings;
            DialogResult = true;
        }
    }
}
