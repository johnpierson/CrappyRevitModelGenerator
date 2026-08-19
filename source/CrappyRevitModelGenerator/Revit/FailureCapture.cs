using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Revit
{
    /// <summary>
    /// The failures the generator expects to raise on purpose. Warnings on this list are
    /// dismissed (so no dialog interrupts the run) and recorded under "expected warnings";
    /// every other warning is recorded under "failures" and left for Revit to show. Ids were
    /// verified against the 2025, 2026 and 2027 API documentation.
    /// </summary>
    public static class ExpectedWarnings
    {
        private static readonly Lazy<HashSet<Guid>> Ids = new Lazy<HashSet<Guid>>(() => new HashSet<Guid>(new[]
        {
            BuiltInFailures.OverlapFailures.WallsOverlap,
            BuiltInFailures.OverlapFailures.DuplicateInstances,
            BuiltInFailures.OverlapFailures.WallRoomSeparationOverlap,
            BuiltInFailures.OverlapFailures.RoomSeparationLinesOverlap,
            BuiltInFailures.OverlapFailures.FloorsOverlap,
            BuiltInFailures.RoomFailures.RoomNotEnclosed,
            BuiltInFailures.RoomFailures.RoomNotEnclosedRooms,
            BuiltInFailures.RoomFailures.RoomsInSameRegion,
            BuiltInFailures.RoomFailures.RoomsInSameRegionRooms,
            BuiltInFailures.RoomFailures.RoomTagNotInRoom,
            BuiltInFailures.RoomFailures.RoomTagNotInRoomToRoom,
            BuiltInFailures.RoomFailures.RoomTooShort,
            BuiltInFailures.GeneralFailures.DuplicateValue,
        }.Select(id => id.Guid)));

        /// <summary>Message fragments (English UI) for warnings whose definition ids are not on the list.</summary>
        private static readonly string[] MessagePatterns =
        {
            "overlap",
            "not in a properly enclosed region",
            "identical instances",
            "duplicate",
            "same enclosed region",
            "slightly off axis",
            "off axis",
            "not enclosed",
            "insert conflicts",
            "can't keep elements joined",
            "cannot keep elements joined",
            "highlighted walls are joined but do not intersect",
        };

        public static bool IsExpected(FailureMessageAccessor message)
        {
            if (message == null) return false;
            try
            {
                if (Ids.Value.Contains(message.GetFailureDefinitionId().Guid)) return true;
            }
            catch
            {
                // A message without a definition id is unusual; fall through to the text match.
            }

            string text;
            try { text = message.GetDescriptionText() ?? string.Empty; }
            catch { return false; }

            var lower = text.ToLowerInvariant();
            return MessagePatterns.Any(p => lower.Contains(p));
        }
    }

    /// <summary>
    /// The <see cref="IFailuresPreprocessor"/> installed on every scenario transaction (plan
    /// section 7.8). Records every failure in the report; dismisses expected warnings (or all
    /// warnings when the user asked for that); rolls the transaction back on any error so a
    /// scenario never half-commits.
    /// </summary>
    public sealed class FailureCapture : IFailuresPreprocessor
    {
        private readonly GenerationReport _report;
        private readonly bool _suppressAllWarnings;

        public FailureCapture(GenerationReport report, bool suppressAllWarnings)
        {
            _report = report ?? throw new ArgumentNullException(nameof(report));
            _suppressAllWarnings = suppressAllWarnings;
        }

        /// <summary>Set by the coordinator before each transaction so records carry the right scenario.</summary>
        public string ScenarioId { get; set; }

        /// <summary>A short description of what the transaction was doing, for the record.</summary>
        public string Operation { get; set; }

        /// <summary>True once an error-level failure was seen in the current transaction.</summary>
        public bool SawError { get; private set; }

        public int WarningsThisTransaction { get; private set; }

        public void Reset(string scenarioId, string operation)
        {
            ScenarioId = scenarioId;
            Operation = operation;
            SawError = false;
            WarningsThisTransaction = 0;
        }

        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
        {
            IList<FailureMessageAccessor> messages;
            try
            {
                messages = failuresAccessor.GetFailureMessages();
            }
            catch (Exception ex)
            {
                _report.AddException(ScenarioId, Operation + " (reading failures)", ex, rolledBack: false);
                return FailureProcessingResult.Continue;
            }

            var anyError = false;
            foreach (var message in messages)
            {
                var record = ToRecord(message);

                if (record.Severity == FailureSeverity.Warning.ToString())
                {
                    WarningsThisTransaction++;
                    var expected = _suppressAllWarnings || ExpectedWarnings.IsExpected(message);
                    if (expected)
                    {
                        try
                        {
                            failuresAccessor.DeleteWarning(message);
                            record.Dismissed = true;
                        }
                        catch (Exception ex)
                        {
                            record.Message += $" (could not dismiss: {ex.Message})";
                        }
                        _report.AddExpectedWarning(record);
                    }
                    else
                    {
                        _report.AddFailure(record);
                    }
                }
                else
                {
                    anyError = true;
                    record.TransactionRolledBack = true;
                    _report.AddFailure(record);
                }
            }

            if (anyError)
            {
                SawError = true;
                return FailureProcessingResult.ProceedWithRollBack;
            }

            return FailureProcessingResult.Continue;
        }

        private FailureRecord ToRecord(FailureMessageAccessor message)
        {
            var record = new FailureRecord
            {
                ScenarioId = ScenarioId,
                Operation = Operation,
            };

            try { record.Severity = message.GetSeverity().ToString(); } catch { record.Severity = "Unknown"; }
            try { record.DefinitionId = message.GetFailureDefinitionId().Guid.ToString(); } catch { record.DefinitionId = null; }
            try { record.Message = message.GetDescriptionText(); } catch { record.Message = "(no description)"; }
            try { record.ElementIds.AddRange(message.GetFailingElementIds().Select(id => id.Value)); } catch { /* ids are optional */ }

            return record;
        }
    }
}
