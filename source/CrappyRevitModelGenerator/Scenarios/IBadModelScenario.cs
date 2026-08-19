using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// One independently toggleable source of badness (plan section 4). Each implementation:
    /// <list type="bullet">
    /// <item>reads its static description from <see cref="ScenarioCatalog"/> by <see cref="Id"/>;</item>
    /// <item>draws randomness ONLY from <c>context.Random.Stream("&lt;id&gt;/…")</c>;</item>
    /// <item>creates elements ONLY through <c>context.Factory</c> (which registers them);</item>
    /// <item>records every intentional defect with <c>context.Report.AddDefect(Id, …)</c> and every
    ///       fallback with <c>AddFallback</c>;</item>
    /// <item>runs inside the transaction the runner opens for it — it must not open its own.</item>
    /// </list>
    /// Throwing from <see cref="Generate"/> rolls the scenario back and records the exception;
    /// the run continues with the next scenario unless this one is required.
    /// </summary>
    public interface IBadModelScenario
    {
        /// <summary>Stable id from <see cref="ScenarioIds"/>.</summary>
        string Id { get; }

        /// <summary>
        /// Whether the scenario has anything to do given the settings and what earlier scenarios
        /// produced. Returning false skips it with <paramref name="reason"/> in the report.
        /// </summary>
        bool CanRun(GenerationContext context, out string reason);

        void Generate(GenerationContext context);
    }
}
