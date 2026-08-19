using CrappyRevitModelGenerator.Core;
using Xunit;

namespace CrappyRevitModelGenerator.Tests
{
    public class NameSanitizerTests
    {
        public static IEnumerable<object[]> ForbiddenCharacters() =>
            NameSanitizer.ForbiddenCharacters.Select(c => new object[] { c });

        [Theory]
        [MemberData(nameof(ForbiddenCharacters))]
        public void EachForbiddenCharacterMakesANameIllegal(char forbidden)
        {
            Assert.False(NameSanitizer.IsLegal("Level " + forbidden + " 1"));
            Assert.False(NameSanitizer.IsLegal(forbidden.ToString()));
        }

        [Fact]
        public void ForbiddenSetIsTheRevitSet()
        {
            // \ : { } [ ] | ; < > ? ` ~
            Assert.Equal(13, NameSanitizer.ForbiddenCharacters.Length);
            foreach (var c in "\\:{}[]|;<>?`~") Assert.Contains(c, NameSanitizer.ForbiddenCharacters);
        }

        [Theory]
        [InlineData("\t")]
        [InlineData("\n")]
        [InlineData("\r")]
        [InlineData("\u0001")]
        [InlineData("\u001b")]
        public void ControlCharactersMakeANameIllegal(string control)
        {
            Assert.False(NameSanitizer.IsLegal("Level" + control + "1"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData(" ")]
        [InlineData("   ")]
        public void EmptyOrWhitespaceIsIllegal(string name)
        {
            Assert.False(NameSanitizer.IsLegal(name));
        }

        [Fact]
        public void LengthLimitIsEnforced()
        {
            Assert.True(NameSanitizer.IsLegal(new string('a', NameSanitizer.MaxLength)));
            Assert.False(NameSanitizer.IsLegal(new string('a', NameSanitizer.MaxLength + 1)));
        }

        [Theory]
        [InlineData("Level 1")]
        [InlineData("Copy of Copy")]
        [InlineData("Plan_02 ")]
        [InlineData(" leading")]
        [InlineData("3D - FINAL - FINAL2")]
        [InlineData("A1.01")]
        [InlineData("1'")]
        [InlineData("Break Room / Kitchen")]
        [InlineData("(maybe)")]
        [InlineData("Ünïcödé — ok")]
        [InlineData("-")]
        public void OrdinaryBadNamesAreLegal(string name)
        {
            Assert.True(NameSanitizer.IsLegal(name));
        }

        [Fact]
        public void MakeLegalReplacesForbiddenCharactersWithHyphen()
        {
            Assert.Equal("Level - 1", NameSanitizer.MakeLegal("Level : 1"));
            Assert.Equal("a-b-c-d-e-f-g-h-i-j-k-l-m-n", NameSanitizer.MakeLegal("a\\b:c{d}e[f]g|h;i<j>k?l`m~n"));
            Assert.True(NameSanitizer.IsLegal(NameSanitizer.MakeLegal("<<<>>>")));
            Assert.Equal("------", NameSanitizer.MakeLegal("<<<>>>"));
        }

        [Fact]
        public void MakeLegalDropsControlCharacters()
        {
            Assert.Equal("Level1", NameSanitizer.MakeLegal("Level\t1"));
            Assert.Equal("ab", NameSanitizer.MakeLegal("a\r\nb"));
        }

        [Fact]
        public void MakeLegalKeepsLeadingAndTrailingSpaces()
        {
            Assert.Equal("Plan_02 ", NameSanitizer.MakeLegal("Plan_02 "));
            Assert.Equal("  A101  ", NameSanitizer.MakeLegal("  A101  "));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("\t\n")]
        public void MakeLegalTurnsEmptyIntoUnnamed(string name)
        {
            Assert.Equal(NameSanitizer.EmptyReplacement, NameSanitizer.MakeLegal(name));
            Assert.Equal("Unnamed", NameSanitizer.EmptyReplacement);
        }

        [Fact]
        public void MakeLegalTruncatesToMaxLength()
        {
            var longName = new string('x', NameSanitizer.MaxLength + 50);
            var legal = NameSanitizer.MakeLegal(longName);
            Assert.Equal(NameSanitizer.MaxLength, legal.Length);
            Assert.True(NameSanitizer.IsLegal(legal));
        }

        [Fact]
        public void MakeLegalIsIdempotent()
        {
            foreach (var name in new[] { "Level : 1", "ok", "  x  ", "<>", "" })
            {
                var once = NameSanitizer.MakeLegal(name);
                Assert.Equal(once, NameSanitizer.MakeLegal(once));
            }
        }

        [Fact]
        public void CandidatesStartWithTheLegalNameThenBadSuffixesThenNumbers()
        {
            var candidates = NameSanitizer.Candidates("View 1").Take(14).ToList();
            Assert.Equal("View 1", candidates[0]);
            Assert.Equal("View 1 2", candidates[1]);
            Assert.Equal("View 1 (2)", candidates[2]);
            Assert.Equal("View 1 - Copy", candidates[3]);
            Assert.Equal("View 1_2", candidates[4]);
            Assert.Equal("View 1 copy", candidates[5]);
            Assert.Equal("View 1 (1)", candidates[6]);
            Assert.Equal("View 1 NEW", candidates[7]);
            Assert.Equal("View 1 3", candidates[8]);
            Assert.Equal("View 1 (3)", candidates[9]);
            Assert.Equal("View 1 - Copy (2)", candidates[10]);
            Assert.Equal("View 1 4", candidates[11]);
            Assert.Equal("View 1 5", candidates[12]);
            Assert.Equal("View 1 6", candidates[13]);
        }

        [Fact]
        public void CandidatesLegaliseTheDesiredNameFirst()
        {
            var candidates = NameSanitizer.Candidates("Bad:Name").Take(3).ToList();
            Assert.Equal("Bad-Name", candidates[0]);
            Assert.All(NameSanitizer.Candidates("Bad:Name").Take(500), c => Assert.True(NameSanitizer.IsLegal(c), c));
        }

        [Fact]
        public void CandidatesAreDistinctAndPlentiful()
        {
            var many = NameSanitizer.Candidates("X").Take(1000).ToList();
            Assert.Equal(1000, many.Count);
            Assert.Equal(1000, many.Distinct().Count());
        }

        [Fact]
        public void CandidatesNeverExceedMaxLength()
        {
            var longName = new string('y', NameSanitizer.MaxLength);
            Assert.All(NameSanitizer.Candidates(longName).Take(50), c => Assert.True(c.Length <= NameSanitizer.MaxLength));
        }

        [Fact]
        public void MakeUniqueReturnsFirstFreeCandidateDeterministically()
        {
            var taken = new HashSet<string>(StringComparer.Ordinal) { "View 1", "View 1 2", "View 1 (2)" };
            Assert.Equal("View 1 - Copy", NameSanitizer.MakeUnique("View 1", taken.Contains));
            Assert.Equal("View 1 - Copy", NameSanitizer.MakeUnique("View 1", taken.Contains));
            Assert.Equal("View 1", NameSanitizer.MakeUnique("View 1", _ => false));
        }

        [Fact]
        public void MakeUniqueLegalisesBeforeChecking()
        {
            var seen = new List<string>();
            var result = NameSanitizer.MakeUnique("Bad|Name", n => { seen.Add(n); return false; });
            Assert.Equal("Bad-Name", result);
            Assert.Equal(new[] { "Bad-Name" }, seen);
        }

        [Fact]
        public void MakeUniqueThrowsAfterMaxAttempts()
        {
            var attempts = 0;
            var ex = Assert.Throws<InvalidOperationException>(() => NameSanitizer.MakeUnique("X", _ => { attempts++; return true; }, maxAttempts: 7));
            Assert.Equal(7, attempts);
            Assert.Contains("7 attempts", ex.Message);
        }

        [Fact]
        public void MakeUniqueRequiresAPredicate()
        {
            Assert.Throws<ArgumentNullException>(() => NameSanitizer.MakeUnique("X", null));
        }

        [Fact]
        public void ReserveAddsTheResultToTheSetAndAvoidsPreviousReservations()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var first = NameSanitizer.Reserve("Office", used);
            var second = NameSanitizer.Reserve("Office", used);
            var third = NameSanitizer.Reserve("Office", used);

            Assert.Equal("Office", first);
            Assert.Equal("Office 2", second);
            Assert.Equal("Office (2)", third);
            Assert.Equal(3, used.Count);
            Assert.Contains(first, used);
            Assert.Contains(second, used);
            Assert.Contains(third, used);
        }

        [Fact]
        public void ReserveConsultsTheDocumentPredicateToo()
        {
            var used = new HashSet<string>(StringComparer.Ordinal);
            var name = NameSanitizer.Reserve("Office", used, n => n == "Office" || n == "Office 2");
            Assert.Equal("Office (2)", name);
            Assert.Throws<ArgumentNullException>(() => NameSanitizer.Reserve("Office", null));
        }

        [Theory]
        [InlineData("Level 1", "level 1", true)]
        [InlineData("Level 1 ", " LEVEL 1", true)]
        [InlineData("Level 1", "Level 2", false)]
        [InlineData(null, null, true)]
        [InlineData(null, "", false)] // null stays null after ?.Trim(); only null == null
        [InlineData("a", null, false)]
        public void RoughlyEqualsIgnoresCaseAndOuterWhitespace(string a, string b, bool expected)
        {
            Assert.Equal(expected, NameSanitizer.RoughlyEquals(a, b));
        }
    }
}
