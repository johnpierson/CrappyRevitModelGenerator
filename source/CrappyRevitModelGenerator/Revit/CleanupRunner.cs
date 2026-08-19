using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.Revit.UI;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>What cleanup did, for the report window and the report file.</summary>
    public sealed class CleanupResult
    {
        public List<CleanupPlan> Plans { get; } = new List<CleanupPlan>();
        public List<string> Messages { get; } = new List<string>();
        public List<FailureRecord> Failures { get; } = new List<FailureRecord>();
        public int Deleted { get; set; }
        public int Kept { get; set; }
        public int AlreadyGone { get; set; }
        public int RunRecordsRemoved { get; set; }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Crappy Revit Model Generator - cleanup report");
            sb.AppendLine("=============================================");
            sb.AppendLine($"Runs processed:      {Plans.Count}");
            sb.AppendLine($"Elements deleted:    {Deleted}");
            sb.AppendLine($"Elements kept:       {Kept}");
            sb.AppendLine($"Already gone:        {AlreadyGone}");
            sb.AppendLine($"Run records removed: {RunRecordsRemoved}");
            sb.AppendLine();
            foreach (var plan in Plans)
            {
                sb.AppendLine($"Run {plan.RunId}: delete {plan.ToDelete.Count}, keep {plan.Kept.Count}, already gone {plan.AlreadyGone.Count}");
                foreach (var k in plan.Kept) sb.AppendLine($"  kept {k.ElementId}: {k.Reason}");
            }
            if (Messages.Count > 0)
            {
                sb.AppendLine();
                foreach (var m in Messages) sb.AppendLine(m);
            }
            if (Failures.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine("Failures");
                sb.AppendLine("--------");
                foreach (var f in Failures) sb.AppendLine($"{f.Severity}: {f.Message}  op={f.Operation}");
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Deletes what a run created and nothing else (plan section 6). Decisions come from the
    /// Revit-free <see cref="CleanupPlanner"/>; this class only supplies the predicates and
    /// performs the deletion inside one transaction per run.
    /// </summary>
    public static class CleanupRunner
    {
        public static CleanupResult Run(UIDocument uiDoc, IEnumerable<RunRecord> runs)
        {
            if (uiDoc == null) throw new ArgumentNullException(nameof(uiDoc));
            var doc = uiDoc.Document;
            var result = new CleanupResult();

            foreach (var run in runs ?? Enumerable.Empty<RunRecord>())
            {
                var plan = CleanupPlanner.Plan(run,
                    exists: id => Exists(doc, id),
                    carriesRunIdentity: id => GeneratedElementRegistry.CarriesRunIdentity(doc.GetElement(new ElementId(id)), run.RunId),
                    dependentIds: id => Dependents(doc, id));
                result.Plans.Add(plan);
                result.AlreadyGone += plan.AlreadyGone.Count;

                // The active view cannot be deleted; move away from it first, outside any transaction.
                var toDelete = new List<long>(plan.ToDelete);
                var activeId = uiDoc.ActiveView?.Id.Value ?? -1;
                if (toDelete.Contains(activeId))
                {
                    var refuge = FindRefugeView(doc, new HashSet<long>(toDelete));
                    if (refuge != null)
                    {
                        try
                        {
                            uiDoc.ActiveView = refuge;
                            result.Messages.Add($"Switched the active view to '{refuge.Name}' so the generated view could be deleted.");
                        }
                        catch (Exception ex)
                        {
                            toDelete.Remove(activeId);
                            plan.Kept.Add(new CleanupException(activeId, "it is the active view and the view could not be switched: " + ex.Message));
                        }
                    }
                    else
                    {
                        toDelete.Remove(activeId);
                        plan.Kept.Add(new CleanupException(activeId, "it is the active view and no other view is available to switch to"));
                    }
                }

                var report = new GenerationReport { RunId = run.RunId };
                var failures = new FailureCapture(report, suppressAllWarnings: true);

                using (var tx = new Transaction(doc, "CRMG: Clean run " + run.RunId))
                {
                    tx.Start();
                    var options = tx.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(failures);
                    options.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(options);
                    failures.Reset("cleanup", "delete run " + run.RunId);

                    var deleted = DeleteAll(doc, toDelete, plan, result);
                    result.Deleted += deleted;

                    // The run record itself, last.
                    if (run.StorageElementId > 0 && Exists(doc, run.StorageElementId))
                    {
                        try
                        {
                            doc.Delete(new ElementId(run.StorageElementId));
                            result.RunRecordsRemoved++;
                        }
                        catch (Exception ex)
                        {
                            result.Failures.Add(new FailureRecord { ScenarioId = "cleanup", Operation = "delete run record", Severity = "Exception", Message = ex.Message });
                        }
                    }

                    var status = tx.Commit();
                    if (status != TransactionStatus.Committed)
                    {
                        result.Messages.Add($"Cleanup of run {run.RunId} was rolled back by Revit; see failures.");
                        result.Deleted -= deleted;
                    }
                }

                result.Failures.AddRange(report.Failures);
                result.Kept += plan.Kept.Count;
            }

            return result;
        }

        private static int DeleteAll(Document doc, List<long> ids, CleanupPlan plan, CleanupResult result)
        {
            if (ids.Count == 0) return 0;

            var elementIds = ids.Select(id => new ElementId(id)).ToList();
            try
            {
                var deleted = doc.Delete(elementIds);
                return deleted?.Count(id => ids.Contains(id.Value)) ?? 0;
            }
            catch (Exception ex)
            {
                result.Messages.Add($"Bulk delete failed ({ex.GetType().Name}: {ex.Message}); deleting one by one.");
            }

            var count = 0;
            foreach (var id in ids)
            {
                if (!Exists(doc, id)) { count++; continue; } // taken out by an earlier cascade
                try
                {
                    doc.Delete(new ElementId(id));
                    count++;
                }
                catch (Exception ex)
                {
                    plan.Kept.Add(new CleanupException(id, "Revit refused to delete it: " + ex.Message));
                }
            }
            return count;
        }

        private static bool Exists(Document doc, long id)
        {
            try
            {
                var e = doc.GetElement(new ElementId(id));
                return e != null && e.IsValidObject;
            }
            catch
            {
                return false;
            }
        }

        private static IEnumerable<long> Dependents(Document doc, long id)
        {
            try
            {
                var e = doc.GetElement(new ElementId(id));
                if (e == null) return Enumerable.Empty<long>();
                return e.GetDependentElements(null).Select(d => d.Value).ToList();
            }
            catch
            {
                return Enumerable.Empty<long>();
            }
        }

        /// <summary>A view that is not being deleted, not a template, and can be made active.</summary>
        private static View FindRefugeView(Document doc, HashSet<long> doomed)
        {
            var candidates = new FilteredElementCollector(doc)
                .OfClass(typeof(View))
                .Cast<View>()
                .Where(v => !v.IsTemplate && !doomed.Contains(v.Id.Value) && v.CanBePrinted)
                .ToList();

            return candidates.FirstOrDefault(v => v.ViewType == ViewType.ThreeD)
                   ?? candidates.FirstOrDefault(v => v.ViewType == ViewType.FloorPlan)
                   ?? candidates.FirstOrDefault(v => v.ViewType == ViewType.DrawingSheet)
                   ?? candidates.FirstOrDefault();
        }
    }
}
