using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.UI
{
    /// <summary>How severity is assigned across a batch run's models.</summary>
    public enum BatchSeverityMode
    {
        CycleLowMediumHigh,
        AllLow,
        AllMedium,
        AllHigh,
    }

    /// <summary>What <see cref="BatchGenerateWindow"/> collected. Immutable once returned.</summary>
    public sealed class BatchGenerateOptions
    {
        public string TemplatePath { get; set; }
        public string OutputFolder { get; set; }
        public int Count { get; set; }
        public int BaseSeed { get; set; }
        public BatchSeverityMode SeverityMode { get; set; }
        public bool IncludeWarningsScenario { get; set; }

        /// <summary>The severity for model index i (0-based), per <see cref="SeverityMode"/>.</summary>
        public GenerationSeverity SeverityFor(int index)
        {
            switch (SeverityMode)
            {
                case BatchSeverityMode.AllLow: return GenerationSeverity.Low;
                case BatchSeverityMode.AllHigh: return GenerationSeverity.High;
                case BatchSeverityMode.AllMedium: return GenerationSeverity.Medium;
                default:
                    var cycle = new[] { GenerationSeverity.Low, GenerationSeverity.Medium, GenerationSeverity.High };
                    return cycle[index % cycle.Length];
            }
        }
    }
}
