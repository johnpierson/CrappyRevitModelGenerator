using System;
using System.Diagnostics;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    public enum TransactionOutcome
    {
        Committed,
        RolledBackByFailure,
        RolledBackByException,
    }

    public sealed class TransactionResult
    {
        public TransactionOutcome Outcome { get; set; }
        public Exception Exception { get; set; }
        public double DurationMs { get; set; }
        public int WarningsRecorded { get; set; }
        public int ElementsCommitted { get; set; }
        public bool Succeeded => Outcome == TransactionOutcome.Committed;
    }

    /// <summary>
    /// One <see cref="TransactionGroup"/> for the run, one <see cref="Transaction"/> per scenario
    /// (plan section 8). Every transaction gets the <see cref="FailureCapture"/> preprocessor
    /// and ClearAfterRollback so a failed scenario leaves nothing behind. Exceptions roll the
    /// transaction back and are returned, never swallowed. Assimilate at the end so the whole
    /// run is one Undo step for the user.
    /// </summary>
    public sealed class TransactionCoordinator : IDisposable
    {
        private readonly Document _doc;
        private readonly FailureCapture _failures;
        private TransactionGroup _group;
        private bool _disposed;

        public TransactionCoordinator(Document doc, string groupName, FailureCapture failures)
        {
            _doc = doc ?? throw new ArgumentNullException(nameof(doc));
            _failures = failures ?? throw new ArgumentNullException(nameof(failures));
            GroupName = string.IsNullOrWhiteSpace(groupName) ? "Crappy Revit Model Generator" : groupName;
        }

        public string GroupName { get; }
        public bool GroupStarted => _group != null && _group.HasStarted() && !_group.HasEnded();

        public void StartGroup()
        {
            if (_group != null) throw new InvalidOperationException("The transaction group was already started.");
            _group = new TransactionGroup(_doc, GroupName);
            _group.Start();
        }

        /// <summary>
        /// Run <paramref name="body"/> inside its own transaction. The registry stage is opened
        /// before and committed/rolled back after, so records only survive if the elements do.
        /// </summary>
        public TransactionResult RunScenario(string scenarioId, string transactionName, GeneratedElementRegistry registry, Action body)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (registry == null) throw new ArgumentNullException(nameof(registry));

            var result = new TransactionResult();
            var watch = Stopwatch.StartNew();
            _failures.Reset(scenarioId, transactionName);

            registry.BeginScenario(scenarioId);

            using (var tx = new Transaction(_doc, transactionName))
            {
                try
                {
                    tx.Start();
                    var options = tx.GetFailureHandlingOptions();
                    options.SetFailuresPreprocessor(_failures);
                    options.SetClearAfterRollback(true);
                    tx.SetFailureHandlingOptions(options);

                    body();

                    var status = tx.Commit();
                    if (status == TransactionStatus.Committed && !_failures.SawError)
                    {
                        result.Outcome = TransactionOutcome.Committed;
                        result.ElementsCommitted = registry.CommitScenario();
                    }
                    else
                    {
                        // Revit rolled it back (our preprocessor asked for it, or an error it could not resolve).
                        result.Outcome = TransactionOutcome.RolledBackByFailure;
                        registry.RollbackScenario();
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    }
                }
                catch (Exception ex)
                {
                    result.Outcome = TransactionOutcome.RolledBackByException;
                    result.Exception = ex;
                    registry.RollbackScenario();
                    try
                    {
                        if (tx.GetStatus() == TransactionStatus.Started) tx.RollBack();
                    }
                    catch
                    {
                        // Nothing more can be done for this transaction; the group will be handled by the caller.
                    }
                }
            }

            watch.Stop();
            result.DurationMs = watch.Elapsed.TotalMilliseconds;
            result.WarningsRecorded = _failures.WarningsThisTransaction;
            return result;
        }

        /// <summary>Merge every committed scenario transaction into a single Undo entry.</summary>
        public void Assimilate()
        {
            if (_group == null) return;
            if (_group.HasStarted() && !_group.HasEnded())
            {
                _group.Assimilate();
            }
        }

        /// <summary>Undo everything the run did so far (fatal safety failure).</summary>
        public void RollBackAll()
        {
            if (_group == null) return;
            if (_group.HasStarted() && !_group.HasEnded())
            {
                _group.RollBack();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (_group != null)
            {
                try
                {
                    // An unassimilated open group means the caller bailed out; rolling back is the
                    // safe default because a half-run leaves the document in an unknown state.
                    if (_group.HasStarted() && !_group.HasEnded()) _group.RollBack();
                }
                catch
                {
                    // Best effort only.
                }
                _group.Dispose();
                _group = null;
            }
        }
    }
}
