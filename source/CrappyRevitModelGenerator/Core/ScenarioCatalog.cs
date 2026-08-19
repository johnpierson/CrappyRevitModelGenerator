using System;
using System.Collections.Generic;
using System.Linq;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>How likely a scenario is to trigger Revit warnings or need a rollback.</summary>
    public enum ScenarioRisk
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }

    /// <summary>Stable scenario ids. These appear in reports, Extensible Storage and settings JSON — never rename one.</summary>
    public static class ScenarioIds
    {
        public const string Baseline = "baseline";
        public const string ContentPlacement = "content-placement";
        public const string Rooms = "rooms";
        public const string Documentation = "documentation";
        public const string Datum = "datum";
        public const string Naming = "naming";
        public const string ContentTypes = "content-types";
        public const string Metadata = "metadata";
        public const string Warnings = "warnings";
    }

    /// <summary>Static description of a scenario; the Revit-side implementation lives in Scenarios/.</summary>
    public sealed class ScenarioDefinition
    {
        public ScenarioDefinition(string id, string displayName, string description, ScenarioRisk risk, int order, bool required, bool defaultEnabled)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
            Description = description ?? string.Empty;
            Risk = risk;
            Order = order;
            Required = required;
            DefaultEnabled = defaultEnabled;
        }

        public string Id { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public ScenarioRisk Risk { get; }

        /// <summary>Execution order. Later scenarios may reference elements created by earlier ones.</summary>
        public int Order { get; }

        /// <summary>A required scenario cannot be disabled and its failure aborts the whole run.</summary>
        public bool Required { get; }

        /// <summary>Whether the dialog ticks it by default (independent of severity).</summary>
        public bool DefaultEnabled { get; }

        public override string ToString() => $"{Id} ({Risk}, order {Order})";
    }

    /// <summary>
    /// The registry of scenarios and the order they run in. The order follows plan section 8:
    /// datum + footprint first, then content, rooms, documentation, and only then the passes
    /// that rename, retype and re-parameterise what exists. Warnings run last, behind a toggle.
    /// </summary>
    public static class ScenarioCatalog
    {
        public static readonly IReadOnlyList<ScenarioDefinition> All = new[]
        {
            new ScenarioDefinition(ScenarioIds.Baseline, "Baseline model",
                "Levels, grids, footprint walls and floors. Always runs; everything else builds on it.",
                ScenarioRisk.Low, order: 10, required: true, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.ContentPlacement, "Doors, windows and furniture",
                "Hosted doors and windows at awkward positions and sill heights, furniture that strays outside the footprint, inconsistent handing.",
                ScenarioRisk.Medium, order: 20, required: false, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.Rooms, "Rooms and spatial data",
                "Rooms with confusing numbers, unplaced rooms, small enclosure gaps, room separation lines where walls exist, awkward or missing tags.",
                ScenarioRisk.Medium, order: 30, required: false, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.Documentation, "Views and sheets",
                "Duplicate plans at odd scales, wrong disciplines, inconsistent crops, empty drafting views and sheets, misleading sheet numbers, placeholder notes.",
                ScenarioRisk.Low, order: 40, required: false, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.Datum, "Datum and layout tweaks",
                "Grid bubbles shown on only some ends, wall joins disallowed at random corners, walls switched to odd location lines.",
                ScenarioRisk.Low, order: 50, required: false, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.Naming, "Poor naming",
                "Levels, grids, views, sheets, rooms and generated types renamed to legal but terrible names.",
                ScenarioRisk.Low, order: 60, required: false, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.ContentTypes, "Near-duplicate types and materials",
                "Wall/floor type copies with '-new', '_2', 'copy' suffixes, unused duplicates, materials named 'New Mat' and 'DO NOT USE'.",
                ScenarioRisk.Low, order: 70, required: false, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.Metadata, "Metadata and parameters",
                "Inconsistent Mark, Comments, Description, Manufacturer and Type Mark values on generated elements only.",
                ScenarioRisk.Low, order: 80, required: false, defaultEnabled: true),

            new ScenarioDefinition(ScenarioIds.Warnings, "Generate warnings (high risk)",
                "Slightly overlapping walls, duplicate instances in place, and other conditions Revit will flag. Off by default.",
                ScenarioRisk.High, order: 90, required: false, defaultEnabled: false),
        };

        public static ScenarioDefinition Find(string id) =>
            id == null ? null : All.FirstOrDefault(s => string.Equals(s.Id, id, StringComparison.OrdinalIgnoreCase));

        public static ScenarioDefinition Get(string id) =>
            Find(id) ?? throw new KeyNotFoundException($"Unknown scenario id '{id}'.");

        /// <summary>
        /// Ids ticked by default. Severity does not change this set (Low still runs everything
        /// that is on by default, just gently); the Warnings scenario is only ever opt-in.
        /// </summary>
        public static IReadOnlyList<string> DefaultEnabledIds(GenerationSeverity severity) =>
            All.Where(s => s.DefaultEnabled).OrderBy(s => s.Order).Select(s => s.Id).ToList();

        public static IReadOnlyList<ScenarioDefinition> Ordered(IEnumerable<string> ids)
        {
            var set = new HashSet<string>(ids ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            return All.Where(s => set.Contains(s.Id)).OrderBy(s => s.Order).ToList();
        }
    }
}
