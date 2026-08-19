using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class CleanupPlannerTests
    {
        private static RunRecord Record(long[] tagged = null, long[] untagged = null) => new RunRecord
        {
            RunId = "20260818-120000-42-abcd",
            Seed = 42,
            StorageElementId = 9000,
            ElementIds = (tagged ?? Array.Empty<long>()).ToList(),
            UntaggedElementIds = (untagged ?? Array.Empty<long>()).ToList(),
        };

        private static IEnumerable<long> NoDependents(long id) => Enumerable.Empty<long>();

        [Fact]
        public void HappyPathDeletesEverythingStillTagged()
        {
            var record = Record(new long[] { 1, 2, 3 });
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, NoDependents);

            Assert.Equal(new long[] { 1, 2, 3 }, plan.ToDelete);
            Assert.Empty(plan.Kept);
            Assert.Empty(plan.AlreadyGone);
            Assert.Equal(record.RunId, plan.RunId);
            Assert.Equal(9000, plan.StorageElementId);
        }

        [Fact]
        public void MissingIdsGoToAlreadyGone()
        {
            var record = Record(new long[] { 1, 2, 3 });
            var plan = CleanupPlanner.Plan(record, id => id != 2, _ => true, NoDependents);

            Assert.Equal(new long[] { 1, 3 }, plan.ToDelete);
            Assert.Equal(new long[] { 2 }, plan.AlreadyGone);
            Assert.Empty(plan.Kept);
        }

        [Fact]
        public void IdsWithoutIdentityAndNotUntaggedAreKeptWithTheReason()
        {
            var record = Record(new long[] { 1, 2 });
            var plan = CleanupPlanner.Plan(record, _ => true, id => id != 2, NoDependents);

            Assert.Equal(new long[] { 1 }, plan.ToDelete);
            var kept = Assert.Single(plan.Kept);
            Assert.Equal(2, kept.ElementId);
            Assert.Contains("identity", kept.Reason);
            Assert.Contains("not deleting on an id alone", kept.Reason);
        }

        [Fact]
        public void UntaggedIdsAreDeletedWithoutIdentityWhenTheyExist()
        {
            var record = Record(new long[] { 1 }, new long[] { 50, 51 });
            var plan = CleanupPlanner.Plan(record, id => id != 51, _ => false, NoDependents);

            // 1 lost its identity -> kept. 50 is on the untagged list -> deleted. 51 is gone.
            Assert.Equal(new long[] { 50 }, plan.ToDelete);
            Assert.Equal(new long[] { 51 }, plan.AlreadyGone);
            var kept = Assert.Single(plan.Kept);
            Assert.Equal(1, kept.ElementId);
        }

        [Fact]
        public void ForeignDependentsKeepTheElementAndTheReasonListsThem()
        {
            var record = Record(new long[] { 10 });
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, id => new long[] { 777, 888 });

            Assert.Empty(plan.ToDelete);
            var kept = Assert.Single(plan.Kept);
            Assert.Equal(10, kept.ElementId);
            Assert.Contains("2 element(s) not created by this run", kept.Reason);
            Assert.Contains("777", kept.Reason);
            Assert.Contains("888", kept.Reason);
        }

        [Fact]
        public void LongDependentListsAreTruncatedInTheReason()
        {
            var record = Record(new long[] { 10 });
            var dependents = Enumerable.Range(100, 12).Select(i => (long)i).ToArray();
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, _ => dependents);

            var kept = Assert.Single(plan.Kept);
            Assert.Contains("12 element(s)", kept.Reason);
            Assert.Contains("107", kept.Reason);
            Assert.DoesNotContain("108", kept.Reason);
            Assert.Contains("…", kept.Reason);
        }

        [Fact]
        public void DependentsThatAreThemselvesCandidatesDoNotBlockDeletion()
        {
            // A generated wall (20) depends on a generated level (10): both are candidates, both go.
            var record = Record(new long[] { 10, 20 });
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, id => id == 10 ? new long[] { 20 } : Array.Empty<long>());

            Assert.Equal(new long[] { 10, 20 }, plan.ToDelete);
            Assert.Empty(plan.Kept);
        }

        [Fact]
        public void DependentsOnTheUntaggedListDoNotBlockDeletion()
        {
            var record = Record(new long[] { 10 }, new long[] { 30 });
            var plan = CleanupPlanner.Plan(record, _ => true, id => id == 10, id => id == 10 ? new long[] { 30 } : Array.Empty<long>());
            Assert.Equal(new long[] { 10, 30 }, plan.ToDelete);
            Assert.Empty(plan.Kept);
        }

        [Fact]
        public void KeptCandidateDependentsDoNotBlockDeletionOfTheirHost()
        {
            // 20 is a candidate that loses its identity (kept); it still does not count as foreign for 10,
            // because it is in the candidate set.
            var record = Record(new long[] { 10, 20 });
            var plan = CleanupPlanner.Plan(record, _ => true, id => id != 20, id => id == 10 ? new long[] { 20 } : Array.Empty<long>());

            Assert.Equal(new long[] { 10 }, plan.ToDelete);
            Assert.Single(plan.Kept, k => k.ElementId == 20);
        }

        [Fact]
        public void SelfIdInDependentsIsIgnored()
        {
            var record = Record(new long[] { 10 });
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, id => new long[] { id });
            Assert.Equal(new long[] { 10 }, plan.ToDelete);
            Assert.Empty(plan.Kept);
        }

        [Fact]
        public void DependentsThatNoLongerExistAreIgnored()
        {
            var record = Record(new long[] { 10 });
            var plan = CleanupPlanner.Plan(record, id => id == 10, _ => true, _ => new long[] { 999 });
            Assert.Equal(new long[] { 10 }, plan.ToDelete);
            Assert.Empty(plan.Kept);
        }

        [Fact]
        public void NullDependentEnumerableIsTolerated()
        {
            var record = Record(new long[] { 10 });
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, _ => null);
            Assert.Equal(new long[] { 10 }, plan.ToDelete);
        }

        [Fact]
        public void NullIdListsOnTheRecordAreTolerated()
        {
            var record = Record();
            record.ElementIds = null;
            record.UntaggedElementIds = null;
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, NoDependents);
            Assert.Empty(plan.ToDelete);
            Assert.Empty(plan.Kept);
            Assert.Empty(plan.AlreadyGone);
        }

        [Fact]
        public void DuplicateAndOverlappingIdsAreProcessedOnce()
        {
            var record = Record(new long[] { 1, 1, 2 }, new long[] { 2, 3, 3 });
            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, NoDependents);
            Assert.Equal(new long[] { 1, 2, 3 }, plan.ToDelete.OrderBy(x => x));
            Assert.Equal(3, plan.ToDelete.Count);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void EveryCandidateEndsUpInExactlyOneBucket(int variant)
        {
            var record = Record(new long[] { 1, 2, 3, 4, 5 }, new long[] { 6, 7 });
            Func<long, bool> exists = variant switch
            {
                0 => _ => true,
                1 => id => id % 2 == 0,
                2 => _ => false,
                _ => id => id > 2,
            };
            Func<long, bool> identity = variant switch
            {
                0 => _ => true,
                1 => id => id != 3,
                _ => id => id % 3 != 0,
            };
            Func<long, IEnumerable<long>> dependents = variant == 3 ? id => new long[] { 100 + id } : NoDependents;

            var plan = CleanupPlanner.Plan(record, exists, identity, dependents);
            var all = plan.ToDelete.Concat(plan.Kept.Select(k => k.ElementId)).Concat(plan.AlreadyGone).OrderBy(x => x).ToList();
            Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6, 7 }, all);
        }

        [Fact]
        public void PlanRejectsNullArguments()
        {
            var record = Record(new long[] { 1 });
            Assert.Throws<ArgumentNullException>(() => CleanupPlanner.Plan(null, _ => true, _ => true, NoDependents));
            Assert.Throws<ArgumentNullException>(() => CleanupPlanner.Plan(record, null, _ => true, NoDependents));
            Assert.Throws<ArgumentNullException>(() => CleanupPlanner.Plan(record, _ => true, null, NoDependents));
            Assert.Throws<ArgumentNullException>(() => CleanupPlanner.Plan(record, _ => true, _ => true, null));
        }

        [Fact]
        public void RunRecordTotalsAndToString()
        {
            var record = Record(new long[] { 1, 2 }, new long[] { 3 });
            Assert.Equal(3, record.TotalRecorded);
            Assert.Contains("seed 42", record.ToString());
            Assert.Contains("3 elements", record.ToString());

            var plan = CleanupPlanner.Plan(record, _ => true, _ => true, NoDependents);
            Assert.Contains("delete 3", plan.ToString());
            Assert.Contains("keep 0", plan.ToString());
            Assert.Contains("already gone 0", plan.ToString());
            Assert.Contains("42: kept reason", new CleanupException(42, "kept reason").ToString());
            Assert.Equal(string.Empty, new CleanupException(1, null).Reason);
        }
    }
}
