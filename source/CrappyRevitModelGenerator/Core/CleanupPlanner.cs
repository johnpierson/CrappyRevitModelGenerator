using System;
using System.Collections.Generic;
using System.Linq;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// What a run left behind, as read back from its DataStorage record. Revit-free so the
    /// cleanup decision logic can be unit tested; the Revit layer fills it from Extensible
    /// Storage.
    /// </summary>
    public sealed class RunRecord
    {
        public string RunId { get; set; }
        public int Seed { get; set; }
        public string Severity { get; set; }
        public string GeneratorVersion { get; set; }
        public string RevitVersion { get; set; }
        public DateTime CreatedUtc { get; set; }
        public string DocumentTitle { get; set; }

        /// <summary>The DataStorage element that holds this record (deleted last, after the run's elements).</summary>
        public long StorageElementId { get; set; }

        public List<long> ElementIds { get; set; } = new List<long>();
        public List<long> UntaggedElementIds { get; set; } = new List<long>();

        public string SettingsJson { get; set; }
        public string ReportJson { get; set; }

        public int TotalRecorded => ElementIds.Count + UntaggedElementIds.Count;

        public override string ToString() => $"{RunId} (seed {Seed}, {TotalRecorded} elements, {CreatedUtc:u})";
    }

    /// <summary>One element cleanup decided NOT to delete, and why.</summary>
    public sealed class CleanupException
    {
        public CleanupException(long elementId, string reason)
        {
            ElementId = elementId;
            Reason = reason ?? string.Empty;
        }

        public long ElementId { get; }
        public string Reason { get; }

        public override string ToString() => $"{ElementId}: {Reason}";
    }

    /// <summary>The decisions for one run: which ids to delete, which to keep, which are already gone.</summary>
    public sealed class CleanupPlan
    {
        public string RunId { get; set; }
        public long StorageElementId { get; set; }
        public List<long> ToDelete { get; } = new List<long>();
        public List<CleanupException> Kept { get; } = new List<CleanupException>();
        public List<long> AlreadyGone { get; } = new List<long>();

        public override string ToString() => $"{RunId}: delete {ToDelete.Count}, keep {Kept.Count}, already gone {AlreadyGone.Count}";
    }

    /// <summary>
    /// Decides what cleanup may delete (plan section 6). Rules, in order:
    /// <list type="number">
    /// <item>An id that no longer exists is skipped (already gone).</item>
    /// <item>An id must still be recognisably ours: it carries the identity entity for this run,
    ///       or it was recorded as one that refused the entity. Anything else is kept — the id
    ///       could have been reused or the entity stripped, and guessing deletes user content.</item>
    /// <item>An element that something outside the run depends on is kept and reported: deleting a
    ///       generated level would take a user's walls with it.</item>
    /// </list>
    /// </summary>
    public static class CleanupPlanner
    {
        public static CleanupPlan Plan(
            RunRecord run,
            Func<long, bool> exists,
            Func<long, bool> carriesRunIdentity,
            Func<long, IEnumerable<long>> dependentIds)
        {
            if (run == null) throw new ArgumentNullException(nameof(run));
            if (exists == null) throw new ArgumentNullException(nameof(exists));
            if (carriesRunIdentity == null) throw new ArgumentNullException(nameof(carriesRunIdentity));
            if (dependentIds == null) throw new ArgumentNullException(nameof(dependentIds));

            var plan = new CleanupPlan { RunId = run.RunId, StorageElementId = run.StorageElementId };

            var untagged = new HashSet<long>(run.UntaggedElementIds ?? Enumerable.Empty<long>());
            var candidates = (run.ElementIds ?? Enumerable.Empty<long>()).Concat(untagged).Distinct().ToList();
            var candidateSet = new HashSet<long>(candidates);

            var confirmed = new List<long>();
            foreach (var id in candidates)
            {
                if (!exists(id))
                {
                    plan.AlreadyGone.Add(id);
                    continue;
                }

                if (!carriesRunIdentity(id) && !untagged.Contains(id))
                {
                    plan.Kept.Add(new CleanupException(id, "no longer carries this run's identity entity; not deleting on an id alone"));
                    continue;
                }

                confirmed.Add(id);
            }

            var confirmedSet = new HashSet<long>(confirmed);
            foreach (var id in confirmed)
            {
                var foreign = (dependentIds(id) ?? Enumerable.Empty<long>())
                    .Where(d => d != id && !confirmedSet.Contains(d) && !candidateSet.Contains(d) && exists(d))
                    .Distinct()
                    .ToList();

                if (foreign.Count > 0)
                {
                    var shown = string.Join(",", foreign.Take(8)) + (foreign.Count > 8 ? ",…" : string.Empty);
                    plan.Kept.Add(new CleanupException(id, $"kept: {foreign.Count} element(s) not created by this run depend on it (ids {shown})"));
                    continue;
                }

                plan.ToDelete.Add(id);
            }

            return plan;
        }
    }
}
