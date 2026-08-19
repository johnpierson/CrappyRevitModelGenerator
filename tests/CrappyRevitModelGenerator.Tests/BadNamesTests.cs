using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class BadNamesTests
    {
        [Fact]
        public void EveryNameListEntryIsLegal()
        {
            // The two suffix lists contain entries like " " that are legal only once appended to
            // a base name; every other list's entries must be legal names on their own.
            var suffixLists = new[] { BadNames.TypeSuffixes, BadNames.DuplicateViewSuffixes };
            foreach (var list in BadNames.NameLists())
            {
                foreach (var name in list)
                {
                    var candidate = suffixLists.Contains(list) ? "Base" + name : name;
                    Assert.True(NameSanitizer.IsLegal(candidate), $"'{name}' is not a legal Revit name");
                }
            }
        }

        [Fact]
        public void EveryNameListIsNonEmptyAndFreeOfNulls()
        {
            var lists = BadNames.NameLists().ToList();
            Assert.Equal(11, lists.Count);
            foreach (var list in lists)
            {
                Assert.NotEmpty(list);
                Assert.DoesNotContain(null, list);
            }
        }

        [Fact]
        public void EveryValueListIsNonEmptyAndFreeOfNulls()
        {
            var lists = BadNames.ValueLists().ToList();
            Assert.Equal(8, lists.Count);
            foreach (var list in lists)
            {
                Assert.NotEmpty(list);
                Assert.DoesNotContain(null, list);
            }
        }

        [Fact]
        public void ValueListsHaveNoControlCharacters()
        {
            foreach (var list in BadNames.ValueLists())
            foreach (var value in list)
                Assert.DoesNotContain(value, c => char.IsControl(c));
        }

        [Fact]
        public void NameListsCoverTheDocumentedNamingCategories()
        {
            var lists = BadNames.NameLists().ToList();
            Assert.Contains(BadNames.ViewNames, lists);
            Assert.Contains(BadNames.SheetNumbers, lists);
            Assert.Contains(BadNames.SheetNames, lists);
            Assert.Contains(BadNames.LevelNames, lists);
            Assert.Contains(BadNames.LevelNameAlternates, lists);
            Assert.Contains(BadNames.GridNames, lists);
            Assert.Contains(BadNames.RoomNames, lists);
            Assert.Contains(BadNames.RoomNumbers, lists);
            Assert.Contains(BadNames.TypeSuffixes, lists);
            Assert.Contains(BadNames.MaterialNames, lists);
            Assert.Contains(BadNames.DuplicateViewSuffixes, lists);
        }

        [Fact]
        public void ValueListsCoverTheDocumentedParameterCategories()
        {
            var lists = BadNames.ValueLists().ToList();
            Assert.Contains(BadNames.Comments, lists);
            Assert.Contains(BadNames.Marks, lists);
            Assert.Contains(BadNames.Manufacturers, lists);
            Assert.Contains(BadNames.Descriptions, lists);
            Assert.Contains(BadNames.Models, lists);
            Assert.Contains(BadNames.Urls, lists);
            Assert.Contains(BadNames.TypeMarks, lists);
            Assert.Contains(BadNames.TextNotes, lists);
        }

        [Fact]
        public void SpecificEntriesFromThePlanExist()
        {
            Assert.Contains("Copy of Copy", BadNames.ViewNames);
            Assert.Contains("3D - FINAL - FINAL2", BadNames.ViewNames);
            Assert.Contains("Use This One", BadNames.ViewNames);
            Assert.Contains("L1", BadNames.LevelNames);
            Assert.Contains("Mezz", BadNames.LevelNames);
            Assert.Contains("A101", BadNames.SheetNumbers);
            Assert.Contains("PLAN-03", BadNames.SheetNumbers);
            Assert.Contains("Grid 1", BadNames.GridNames);
            Assert.Contains("2A", BadNames.GridNames);
            Assert.Contains("Office", BadNames.RoomNames);
            Assert.Contains("Misc", BadNames.RoomNames);
            Assert.Contains("101A", BadNames.RoomNumbers);
            Assert.Contains("101-old", BadNames.RoomNumbers);
            Assert.Contains("New Mat", BadNames.MaterialNames);
            Assert.Contains("DO NOT USE", BadNames.MaterialNames);
            Assert.Contains("REMOVE BEFORE ISSUE", BadNames.TextNotes);
            Assert.Contains("TBD", BadNames.TextNotes);
            Assert.Contains("-new", BadNames.TypeSuffixes);
            Assert.Contains("_2", BadNames.TypeSuffixes);
        }

        [Fact]
        public void LevelNamesCoverTheMaximumLevelCountPlusAnIntermediate()
        {
            // The planner may add one intermediate level to the user's count.
            Assert.True(BadNames.LevelNames.Count >= GenerationLimits.MaxLevels + 1);
        }

        [Fact]
        public void TypeSuffixesAppendedToACleanNameStayLegal()
        {
            foreach (var suffix in BadNames.TypeSuffixes)
                Assert.True(NameSanitizer.IsLegal("Generic - 200mm" + suffix), suffix);
            foreach (var suffix in BadNames.DuplicateViewSuffixes)
                Assert.True(NameSanitizer.IsLegal("Level 1" + suffix), suffix);
        }

        [Fact]
        public void ValueListsDeliberatelyContainBlanksForBlankMetadata()
        {
            Assert.Contains("", BadNames.Comments);
            Assert.Contains("", BadNames.Marks);
            Assert.Contains("", BadNames.Manufacturers);
        }
    }
}
