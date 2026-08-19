using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class GenerationReportTests
    {
        private static GenerationReport SampleReport()
        {
            var report = new GenerationReport
            {
                RunId = "20260818-141503-42-9f3a",
                Seed = 42,
                GeneratorVersion = "1.2.3",
                RevitVersion = "2026",
                DocumentTitle = "Project1",
                StartedUtc = new DateTime(2026, 8, 18, 14, 15, 3, DateTimeKind.Utc),
                Settings = new GenerationSettings { Seed = 42, Severity = GenerationSeverity.High, EnabledScenarioIds = new List<string> { ScenarioIds.Naming } },
            };
            report.Increment(GeneratedCategory.Walls, 12);
            report.Increment(GeneratedCategory.Levels, 3);
            report.Increment(GeneratedCategory.Views);

            var baseline = report.BeginScenario(ScenarioCatalog.Get(ScenarioIds.Baseline));
            baseline.Status = ScenarioStatus.Applied;
            baseline.ElementsCreated = 15;
            baseline.DurationMs = 12.5;
            var naming = report.BeginScenario(ScenarioCatalog.Get(ScenarioIds.Naming));
            naming.Status = ScenarioStatus.RolledBack;
            naming.Message = "boom";

            report.AddDefect(ScenarioIds.Naming, "View named 'Copy of Copy'", new long[] { 1001, 1002 });
            report.AddFallback(ScenarioIds.Baseline, "No title block; sheets skipped");
            report.AddInfo(ScenarioIds.Baseline, "hello");
            report.AddNote(ReportNote.KindCleanup, null, "cleanup note", new long[] { 5 });
            report.AddException(ScenarioIds.Naming, "rename", new InvalidOperationException("nope"), rolledBack: true);
            report.AddExpectedWarning(new FailureRecord { ScenarioId = ScenarioIds.Warnings, Severity = "Warning", DefinitionId = "abc", Message = "overlap", Dismissed = true, ElementIds = { 7, 8 } });
            report.GeneratedElementIds.AddRange(new long[] { 1001, 1002, 1003 });
            report.UntaggedElementIds.Add(1003);
            report.RunStorageElementId = 9999;
            report.FinishedUtc = report.StartedUtc.AddSeconds(4);
            return report;
        }

        [Fact]
        public void IncrementCountOfAndTotalElements()
        {
            var report = new GenerationReport();
            Assert.Equal(0, report.TotalElements);
            Assert.Equal(0, report.CountOf(GeneratedCategory.Walls));

            report.Increment(GeneratedCategory.Walls);
            report.Increment(GeneratedCategory.Walls, 4);
            report.Increment(GeneratedCategory.Doors, 2);
            report.Increment(GeneratedCategory.Rooms, 0);

            Assert.Equal(5, report.CountOf(GeneratedCategory.Walls));
            Assert.Equal(2, report.CountOf(GeneratedCategory.Doors));
            Assert.Equal(0, report.CountOf(GeneratedCategory.Rooms));
            Assert.Equal(7, report.TotalElements);
            Assert.False(report.Counts.ContainsKey("Rooms"));
            Assert.Equal(new[] { "Doors", "Walls" }, report.Counts.Keys);
        }

        [Fact]
        public void IncrementByNegativeSubtracts()
        {
            var report = new GenerationReport();
            report.Increment(GeneratedCategory.Walls, 3);
            report.Increment(GeneratedCategory.Walls, -1);
            Assert.Equal(2, report.CountOf(GeneratedCategory.Walls));
        }

        [Fact]
        public void BeginScenarioRegistersAndFindScenarioIsCaseInsensitive()
        {
            var report = new GenerationReport();
            var outcome = report.BeginScenario(ScenarioCatalog.Get(ScenarioIds.Rooms));

            Assert.Equal(ScenarioIds.Rooms, outcome.ScenarioId);
            Assert.Equal("Rooms and spatial data", outcome.DisplayName);
            Assert.Equal(ScenarioStatus.NotRun, outcome.Status);
            Assert.Single(report.Scenarios);
            Assert.Same(outcome, report.FindScenario("ROOMS"));
            Assert.Null(report.FindScenario(ScenarioIds.Naming));
            Assert.Null(report.FindScenario(null));
        }

        [Fact]
        public void NoteHelpersSetTheKind()
        {
            var report = new GenerationReport();
            var defect = report.AddDefect("s", "d", new long[] { 1 });
            var fallback = report.AddFallback("s", "f");
            var info = report.AddInfo("s", "i", null);
            var custom = report.AddNote(null, "s", null);

            Assert.Equal(ReportNote.KindDefect, defect.Kind);
            Assert.Equal(ReportNote.KindFallback, fallback.Kind);
            Assert.Equal(ReportNote.KindInfo, info.Kind);
            Assert.Equal(ReportNote.KindInfo, custom.Kind);
            Assert.Equal(string.Empty, custom.Message);
            Assert.Equal(new long[] { 1 }, defect.ElementIds);
            Assert.Empty(fallback.ElementIds);
            Assert.Equal(4, report.Notes.Count);

            Assert.Single(report.Defects);
            Assert.Same(defect, report.Defects.Single());
            Assert.Single(report.Fallbacks);
            Assert.Same(fallback, report.Fallbacks.Single());
        }

        [Fact]
        public void AddExceptionBuildsAFailureRecord()
        {
            var report = new GenerationReport();
            Assert.False(report.HasUnexpectedFailures);

            var record = report.AddException(ScenarioIds.Naming, "rename view", new InvalidOperationException("nope"), rolledBack: true);
            Assert.Equal(ScenarioIds.Naming, record.ScenarioId);
            Assert.Equal("rename view", record.Operation);
            Assert.Equal("Exception", record.Severity);
            Assert.Equal("InvalidOperationException: nope", record.Message);
            Assert.True(record.TransactionRolledBack);
            Assert.Null(record.DefinitionId);
            Assert.Empty(record.ElementIds);
            Assert.Single(report.Failures);
            Assert.True(report.HasUnexpectedFailures);

            var unknown = report.AddException("s", "op", null, rolledBack: false);
            Assert.Equal("Unknown exception", unknown.Message);
            Assert.False(unknown.TransactionRolledBack);
        }

        [Fact]
        public void AddFailureAndExpectedWarningRequireARecord()
        {
            var report = new GenerationReport();
            Assert.Throws<ArgumentNullException>(() => report.AddFailure(null));
            Assert.Throws<ArgumentNullException>(() => report.AddExpectedWarning(null));

            var f = report.AddFailure(new FailureRecord { Severity = "Error" });
            var w = report.AddExpectedWarning(new FailureRecord { Severity = "Warning" });
            Assert.Same(f, report.Failures.Single());
            Assert.Same(w, report.ExpectedWarnings.Single());
        }

        [Fact]
        public void RolledBackScenariosQuery()
        {
            var report = SampleReport();
            Assert.Single(report.RolledBackScenarios);
            Assert.Equal(ScenarioIds.Naming, report.RolledBackScenarios.Single().ScenarioId);
        }

        [Fact]
        public void FinishSetsFinishedUtc()
        {
            var report = new GenerationReport();
            Assert.Null(report.FinishedUtc);
            report.Finish();
            Assert.NotNull(report.FinishedUtc);
            Assert.Equal(DateTimeKind.Utc, report.FinishedUtc.Value.Kind);
        }

        [Fact]
        public void JsonRoundTripPreservesEverything()
        {
            var report = SampleReport();
            var json = report.ToJson();

            Assert.Contains("\"Status\": \"Applied\"", json);
            Assert.Contains("\"Status\": \"RolledBack\"", json);
            Assert.Contains("\"Severity\": \"High\"", json);
            Assert.DoesNotContain("TotalElements", json);
            Assert.DoesNotContain("\"Defects\"", json);

            var back = GenerationReport.FromJson(json);
            Assert.Equal(report.RunId, back.RunId);
            Assert.Equal(report.Seed, back.Seed);
            Assert.Equal(report.GeneratorVersion, back.GeneratorVersion);
            Assert.Equal(report.RevitVersion, back.RevitVersion);
            Assert.Equal(report.DocumentTitle, back.DocumentTitle);
            Assert.Equal(report.StartedUtc, back.StartedUtc);
            Assert.Equal(report.FinishedUtc, back.FinishedUtc);
            Assert.Equal(report.DryRun, back.DryRun);
            Assert.Equal(report.Aborted, back.Aborted);

            Assert.Equal(report.Counts, back.Counts);
            Assert.Equal(report.TotalElements, back.TotalElements);
            Assert.Equal(12, back.CountOf(GeneratedCategory.Walls));

            Assert.Equal(report.Scenarios.Count, back.Scenarios.Count);
            for (var i = 0; i < report.Scenarios.Count; i++)
            {
                Assert.Equal(report.Scenarios[i].ScenarioId, back.Scenarios[i].ScenarioId);
                Assert.Equal(report.Scenarios[i].DisplayName, back.Scenarios[i].DisplayName);
                Assert.Equal(report.Scenarios[i].Status, back.Scenarios[i].Status);
                Assert.Equal(report.Scenarios[i].Message, back.Scenarios[i].Message);
                Assert.Equal(report.Scenarios[i].ElementsCreated, back.Scenarios[i].ElementsCreated);
                Assert.Equal(report.Scenarios[i].DurationMs, back.Scenarios[i].DurationMs);
            }

            Assert.Equal(report.Notes.Count, back.Notes.Count);
            for (var i = 0; i < report.Notes.Count; i++)
            {
                Assert.Equal(report.Notes[i].Kind, back.Notes[i].Kind);
                Assert.Equal(report.Notes[i].ScenarioId, back.Notes[i].ScenarioId);
                Assert.Equal(report.Notes[i].Message, back.Notes[i].Message);
                Assert.Equal(report.Notes[i].ElementIds, back.Notes[i].ElementIds);
            }
            Assert.Single(back.Defects);
            Assert.Single(back.Fallbacks);

            Assert.Single(back.Failures);
            Assert.Equal("Exception", back.Failures[0].Severity);
            Assert.Equal("InvalidOperationException: nope", back.Failures[0].Message);
            Assert.True(back.Failures[0].TransactionRolledBack);
            Assert.Single(back.ExpectedWarnings);
            Assert.Equal(new long[] { 7, 8 }, back.ExpectedWarnings[0].ElementIds);
            Assert.True(back.ExpectedWarnings[0].Dismissed);
            Assert.Equal("abc", back.ExpectedWarnings[0].DefinitionId);

            Assert.Equal(report.GeneratedElementIds, back.GeneratedElementIds);
            Assert.Equal(report.UntaggedElementIds, back.UntaggedElementIds);
            Assert.Equal(report.RunStorageElementId, back.RunStorageElementId);

            Assert.NotNull(back.Settings);
            Assert.Equal(GenerationSeverity.High, back.Settings.Severity);
            Assert.Equal(new[] { ScenarioIds.Naming }, back.Settings.EnabledScenarioIds);

            Assert.Equal(json, back.ToJson());
        }

        [Fact]
        public void JsonRoundTripOfAnEmptyReportWorks()
        {
            var back = GenerationReport.FromJson(new GenerationReport().ToJson());
            Assert.Empty(back.Counts);
            Assert.Empty(back.Scenarios);
            Assert.Empty(back.Notes);
            Assert.Null(back.Settings);
            Assert.Null(back.RunStorageElementId);
            Assert.Equal(0, back.TotalElements);
        }

        [Fact]
        public void FromJsonRejectsEmptyAndNullLiteral()
        {
            Assert.Throws<ArgumentException>(() => GenerationReport.FromJson(" "));
            Assert.Throws<ArgumentException>(() => GenerationReport.FromJson(null));
            Assert.Throws<InvalidOperationException>(() => GenerationReport.FromJson("null"));
        }

        [Fact]
        public void ToTextContainsHeaderCountsScenariosAndNotes()
        {
            var report = SampleReport();
            var text = report.ToText();

            Assert.Contains("Run id:            20260818-141503-42-9f3a", text);
            Assert.Contains("Seed:              42", text);
            Assert.Contains("1.2.3", text);
            Assert.Contains("2026", text);
            Assert.Contains("Project1", text);
            Assert.Contains("2026-08-18 14:15:03Z", text);
            Assert.Contains("(4.0 s)", text);

            Assert.Contains("Severity High", text);
            Assert.Contains("Scenarios: baseline, naming", text);

            Assert.Contains("Walls", text);
            Assert.Contains("Levels", text);
            Assert.Contains("Total", text);
            Assert.Contains("16", text);

            Assert.Contains("Applied", text);
            Assert.Contains("RolledBack", text);
            Assert.Contains("[baseline]", text);
            Assert.Contains("[naming]", text);
            Assert.Contains("- boom", text);

            Assert.Contains("Intentional defects", text);
            Assert.Contains("View named 'Copy of Copy'", text);
            Assert.Contains("ids: 1001,1002", text);
            Assert.Contains("Fallbacks", text);
            Assert.Contains("No title block", text);
            Assert.Contains("Information", text);
            Assert.Contains("Cleanup", text);
            Assert.Contains("[-] cleanup note", text);

            Assert.Contains("Expected warnings (dismissed)", text);
            Assert.Contains("Unexpected failures", text);
            Assert.Contains("InvalidOperationException: nope", text);
            Assert.Contains("(rolled back)", text);

            Assert.Contains("3 generated element id(s) recorded; 1 could not carry the identity entity.", text);
            Assert.Contains("DataStorage element 9999", text);
        }

        [Fact]
        public void ToTextFlagsDryRunAndAbort()
        {
            var report = new GenerationReport { DryRun = true, Aborted = true, AbortReason = "baseline failed" };
            var text = report.ToText();
            Assert.Contains("DRY RUN", text);
            Assert.Contains("ABORTED:           baseline failed", text);
            Assert.DoesNotContain("Intentional defects", text);
            Assert.DoesNotContain("Unexpected failures", text);
        }

        [Fact]
        public void ToTextTruncatesLongIdLists()
        {
            var report = new GenerationReport();
            report.AddDefect("s", "many", Enumerable.Range(1, 20).Select(i => (long)i));
            var line = report.ToText().Split('\n').Single(l => l.Contains("many"));
            Assert.Contains("ids: 1,2,3,4,5,6,7,8,9,10,11,12,…", line);
            Assert.DoesNotContain("13", line);
        }
    }
}
