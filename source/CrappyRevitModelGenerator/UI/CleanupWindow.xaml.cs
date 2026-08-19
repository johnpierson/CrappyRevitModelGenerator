using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Windows;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.UI
{
    /// <summary>
    /// Picks which generated runs to remove (plan section 6): one row per <see cref="RunRecord"/>
    /// found in the document, all ticked by default, with a Yes/No confirmation naming the
    /// counts before anything is returned. The window only selects; <c>CleanupRunner</c> does
    /// the deleting and reports what it kept.
    /// </summary>
    public partial class CleanupWindow : Window
    {
        private readonly string _documentTitle;
        private readonly List<CleanupRunRow> _rows;

        /// <summary>The runs the user confirmed. Only meaningful when ShowDialog returned true.</summary>
        public IReadOnlyList<RunRecord> SelectedRuns { get; private set; }

        public CleanupWindow(string documentTitle, IReadOnlyList<RunRecord> runs)
        {
            _documentTitle = string.IsNullOrWhiteSpace(documentTitle) ? "(untitled)" : documentTitle;
            _rows = (runs ?? Array.Empty<RunRecord>()).Where(r => r != null).Select(r => new CleanupRunRow(r)).ToList();

            InitializeComponent();

            Title = Dialogs.Title + " — Clean Generated Model";
            HeaderText.Text = $"{_rows.Count} generated run(s) found in '{_documentTitle}'";

            foreach (var row in _rows) row.PropertyChanged += OnRowChanged;
            RunsList.ItemsSource = _rows;
            UpdateState();
        }

        private void OnRowChanged(object sender, PropertyChangedEventArgs e) => UpdateState();

        private void UpdateState()
        {
            var selected = _rows.Where(r => r.IsSelected).ToList();
            var elements = selected.Sum(r => r.Elements);
            SelectionText.Text = $"{selected.Count} of {_rows.Count} run(s) selected, {elements} recorded element(s).";
            DeleteButton.IsEnabled = selected.Count > 0;
        }

        private void OnSelectAll(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.IsSelected = true;
        }

        private void OnSelectNone(object sender, RoutedEventArgs e)
        {
            foreach (var row in _rows) row.IsSelected = false;
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            var selected = _rows.Where(r => r.IsSelected).Select(r => r.Record).ToList();
            if (selected.Count == 0) return;

            var elements = selected.Sum(r => r.TotalRecorded);
            var message =
                $"Delete {selected.Count} generated run(s) with {elements} recorded element(s) from '{_documentTitle}'?" +
                Environment.NewLine + Environment.NewLine +
                "Only elements recorded by the generator are deleted. Elements that user content depends on are kept and reported. " +
                "Each run's cleanup is one Undo step.";

            var answer = MessageBox.Show(this, message, Dialogs.Title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No);
            if (answer != MessageBoxResult.Yes) return;

            SelectedRuns = selected;
            DialogResult = true;
        }
    }

    /// <summary>A <see cref="RunRecord"/> as the cleanup list shows it, plus its tick state.</summary>
    public sealed class CleanupRunRow : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public CleanupRunRow(RunRecord record)
        {
            Record = record ?? throw new ArgumentNullException(nameof(record));
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public RunRecord Record { get; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value) return;
                _isSelected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
            }
        }

        public string RunId => string.IsNullOrWhiteSpace(Record.RunId) ? "(unknown)" : Record.RunId;
        public int Seed => Record.Seed;
        public string Severity => string.IsNullOrWhiteSpace(Record.Severity) ? "-" : Record.Severity;
        public string CreatedLocal => FormatLocal(Record.CreatedUtc);
        public int Elements => Record.TotalRecorded;
        public string GeneratorVersion => string.IsNullOrWhiteSpace(Record.GeneratorVersion) ? "-" : Record.GeneratorVersion;
        public string RevitVersion => string.IsNullOrWhiteSpace(Record.RevitVersion) ? "-" : Record.RevitVersion;

        /// <summary>The stored timestamp is UTC (RunStore writes it with the "o" format); show it in the user's zone.</summary>
        private static string FormatLocal(DateTime utc)
        {
            if (utc == DateTime.MinValue) return "-";
            var asUtc = utc.Kind == DateTimeKind.Unspecified ? DateTime.SpecifyKind(utc, DateTimeKind.Utc) : utc;
            return asUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
        }
    }
}
