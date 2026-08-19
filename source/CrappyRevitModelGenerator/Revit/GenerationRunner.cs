using System;
using System.Linq;
using System.Reflection;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;
using CrappyRevitModelGenerator.Core.Planning;
using CrappyRevitModelGenerator.Scenarios;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// The whole generate loop, UI-free (plan section 5.2 steps 4–8): validate, build the
    /// context, run scenarios in one transaction group, store the run record, assimilate,
    /// return the report. Dry runs plan and estimate without opening a transaction.
    /// </summary>
    public static class GenerationRunner
    {
        public const string RunRecordScenarioId = "run-record";

        public static string GeneratorVersion =>
            Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.0.0";

        public static GenerationReport Run(UIApplication uiApp, Document doc, GenerationSettings settings, Action<string, int, int> progress = null)
        {
            if (doc == null) throw new ArgumentNullException(nameof(doc));
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            settings = settings.Clone();
            var revitVersion = uiApp?.Application?.VersionNumber ?? "unknown";
            var report = new GenerationReport
            {
                RunId = RunIdentity.NewRunId(settings.Seed),
                Seed = settings.Seed,
                GeneratorVersion = GeneratorVersion,
                RevitVersion = revitVersion,
                DocumentTitle = doc.Title,
                StartedUtc = DateTime.UtcNow,
                DryRun = settings.DryRun,
                Settings = settings,
            };

            var safety = DocumentSafetyGuard.CheckRun(doc, settings);
            foreach (var w in safety.Warnings) report.AddInfo(null, "Pre-flight warning: " + w);
            if (!safety.CanProceed)
            {
                report.Aborted = true;
                report.AbortReason = string.Join(" ", safety.Blockers);
                report.Finish();
                return report;
            }

            if (settings.DryRun)
            {
                DryRun(report, settings);
                report.Finish();
                return report;
            }

            var registry = new GeneratedElementRegistry(report.RunId, settings.Seed, GeneratorVersion, report);
            var failures = new FailureCapture(report, settings.SuppressAllWarningDialogs);
            var context = new GenerationContext(uiApp, doc, settings, report, registry, failures, report.RunId, GeneratorVersion, revitVersion);
            var scenarios = ScenarioFactory.CreateFor(settings.ResolveScenarioIds());

            using (var coordinator = new TransactionCoordinator(doc, "Generate Bad Model (" + report.RunId + ")", failures))
            {
                coordinator.StartGroup();

                var runner = new ScenarioRunner(context, coordinator, scenarios) { Progress = progress };
                var completed = runner.RunAll();

                if (!completed)
                {
                    // Runner already rolled the group back; nothing was kept, nothing to record.
                    report.Finish();
                    return report;
                }

                // Snapshot ids before the record transaction so the record does not list itself.
                report.GeneratedElementIds = registry.AllIds.Distinct().ToList();

                var recordResult = coordinator.RunScenario(RunRecordScenarioId, "CRMG: Save run record", registry, () =>
                {
                    var storage = RunStore.Write(doc, report, settings, report.GeneratedElementIds, report.UntaggedElementIds);
                    registry.Register(storage, GeneratedCategory.DataStorage);
                    report.RunStorageElementId = storage.Id.Value;
                });

                if (!recordResult.Succeeded)
                {
                    report.AddException(RunRecordScenarioId, "Save run record",
                        recordResult.Exception ?? new InvalidOperationException("The run record transaction did not commit."), rolledBack: true);
                    report.AddInfo(RunRecordScenarioId, "The run record could not be stored; cleanup will rely on the identity entities on the elements themselves.");
                }

                coordinator.Assimilate();
            }

            report.Finish();
            return report;
        }

        /// <summary>Plan everything, count it, create nothing.</summary>
        private static void DryRun(GenerationReport report, GenerationSettings settings)
        {
            var estimate = ElementCountEstimator.Estimate(settings);
            foreach (var pair in estimate.ByCategory)
                report.Counts[pair.Key] = pair.Value;

            var random = new SeededRandom(settings.Seed);
            var enabled = settings.ResolveScenarioIds();
            var baseline = BaselinePlanner.Plan(settings, random, enabled.Contains(ScenarioIds.Datum));
            foreach (var d in baseline.Defects) report.AddDefect(d.ScenarioId, d.Message);

            if (enabled.Contains(ScenarioIds.ContentPlacement))
                foreach (var d in ContentPlanner.Plan(baseline, settings, random, true).Defects) report.AddDefect(d.ScenarioId, d.Message);

            if (enabled.Contains(ScenarioIds.Rooms))
                foreach (var d in RoomPlanner.Plan(baseline, settings, random, enabled.Contains(ScenarioIds.Naming)).Defects) report.AddDefect(d.ScenarioId, d.Message);

            foreach (var id in enabled)
            {
                var outcome = report.BeginScenario(ScenarioCatalog.Get(id));
                outcome.Status = ScenarioStatus.NotRun;
                outcome.Message = "Dry run — would run.";
            }

            report.AddInfo(null, $"Dry run: approximately {estimate.Total} element(s) would be created. Nothing was changed.");
        }
    }
}
