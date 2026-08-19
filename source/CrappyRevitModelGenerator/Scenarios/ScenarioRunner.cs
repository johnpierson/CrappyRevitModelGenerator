using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Revit;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Executes scenarios in catalog order, one transaction each (plan section 8). A required
    /// scenario that fails aborts the run and rolls the whole group back; any other failure is
    /// recorded and the run continues.
    /// </summary>
    public sealed class ScenarioRunner
    {
        private readonly GenerationContext _ctx;
        private readonly TransactionCoordinator _coordinator;
        private readonly IReadOnlyList<IBadModelScenario> _scenarios;

        public ScenarioRunner(GenerationContext context, TransactionCoordinator coordinator, IReadOnlyList<IBadModelScenario> scenarios)
        {
            _ctx = context ?? throw new ArgumentNullException(nameof(context));
            _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
            _scenarios = scenarios ?? throw new ArgumentNullException(nameof(scenarios));
        }

        /// <summary>Optional progress callback: (scenario display name, index, total).</summary>
        public Action<string, int, int> Progress { get; set; }

        /// <summary>Runs every scenario. Returns false when the run was aborted (group rolled back).</summary>
        public bool RunAll()
        {
            var report = _ctx.Report;
            var total = _scenarios.Count;

            for (var i = 0; i < total; i++)
            {
                var scenario = _scenarios[i];
                var definition = ScenarioCatalog.Get(scenario.Id);
                var outcome = report.BeginScenario(definition);
                _ctx.CurrentScenarioId = scenario.Id;
                Progress?.Invoke(definition.DisplayName, i + 1, total);

                if (!scenario.CanRun(_ctx, out var reason))
                {
                    outcome.Status = ScenarioStatus.Skipped;
                    outcome.Message = reason ?? "Skipped.";
                    if (definition.Required)
                    {
                        Abort(report, $"Required scenario '{definition.DisplayName}' cannot run: {outcome.Message}");
                        return false;
                    }
                    continue;
                }

                var result = _coordinator.RunScenario(scenario.Id, "CRMG: " + definition.DisplayName, _ctx.Registry, () => scenario.Generate(_ctx));
                outcome.DurationMs = result.DurationMs;
                outcome.ElementsCreated = result.ElementsCommitted;

                switch (result.Outcome)
                {
                    case TransactionOutcome.Committed:
                        outcome.Status = ScenarioStatus.Applied;
                        outcome.Message = result.WarningsRecorded > 0 ? $"{result.WarningsRecorded} warning(s) recorded." : null;
                        break;

                    case TransactionOutcome.RolledBackByFailure:
                        outcome.Status = ScenarioStatus.RolledBack;
                        outcome.Message = "Revit reported an error-level failure; the scenario was rolled back (see failures).";
                        break;

                    case TransactionOutcome.RolledBackByException:
                        outcome.Status = ScenarioStatus.RolledBack;
                        outcome.Message = result.Exception == null ? "Exception; rolled back." : $"{result.Exception.GetType().Name}: {result.Exception.Message}";
                        report.AddException(scenario.Id, "Generate", result.Exception, rolledBack: true);
                        break;
                }

                if (outcome.Status == ScenarioStatus.RolledBack && definition.Required)
                {
                    Abort(report, $"Required scenario '{definition.DisplayName}' failed: {outcome.Message}");
                    return false;
                }
            }

            _ctx.CurrentScenarioId = null;
            return true;
        }

        private void Abort(GenerationReport report, string reason)
        {
            report.Aborted = true;
            report.AbortReason = reason;
            _coordinator.RollBackAll();
        }
    }
}
