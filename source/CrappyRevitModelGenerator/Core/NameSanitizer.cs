using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// Keeps generated names legal and unique while preserving the bad pattern. Revit rejects
    /// names containing <c>\ : { } [ ] | ; &lt; &gt; ? ` ~</c> or non-printable characters, and
    /// rejects a name already used by another element of the same kind. When a name is taken we
    /// do NOT fall back to a clean name; we append one of a fixed set of equally bad suffixes
    /// (" 2", " (2)", " - Copy", "_2", …) so the result still reads like a careless project.
    /// </summary>
    public static class NameSanitizer
    {
        /// <summary>Characters Revit refuses in element names.</summary>
        public const string ForbiddenCharacters = "\\:{}[]|;<>?`~";

        /// <summary>What an empty or all-forbidden name turns into.</summary>
        public const string EmptyReplacement = "Unnamed";

        /// <summary>Practical ceiling; Revit has no documented limit but very long names are unusable.</summary>
        public const int MaxLength = 200;

        private static readonly string[] UniqueSuffixStyles =
        {
            " 2",
            " (2)",
            " - Copy",
            "_2",
            " copy",
            " (1)",
            " NEW",
            " 3",
            " (3)",
            " - Copy (2)",
        };

        public static bool IsLegal(string name)
        {
            if (name == null) return false;
            if (name.Trim().Length == 0) return false;
            if (name.Length > MaxLength) return false;
            foreach (var c in name)
            {
                if (ForbiddenCharacters.IndexOf(c) >= 0) return false;
                if (char.IsControl(c)) return false;
            }
            return true;
        }

        /// <summary>
        /// The closest legal name: forbidden characters become '-', control characters are
        /// dropped, and an empty result becomes <see cref="EmptyReplacement"/>. Leading and
        /// trailing spaces are kept — they are part of the intended badness and Revit accepts
        /// them in most name fields (it trims some; the caller handles rejection by retrying).
        /// </summary>
        public static string MakeLegal(string name)
        {
            if (name == null) return EmptyReplacement;

            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                if (char.IsControl(c)) continue;
                sb.Append(ForbiddenCharacters.IndexOf(c) >= 0 ? '-' : c);
            }

            var result = sb.ToString();
            if (result.Length > MaxLength) result = result.Substring(0, MaxLength);
            if (result.Trim().Length == 0) return EmptyReplacement;
            return result;
        }

        /// <summary>
        /// The candidate sequence for a desired name: the (legalised) name itself, then the same
        /// name with each bad suffix style, then numbered fallbacks. Deterministic, so two runs
        /// with the same seed and the same pre-existing names get the same result.
        /// </summary>
        public static IEnumerable<string> Candidates(string desired)
        {
            var baseName = MakeLegal(desired);
            yield return baseName;

            foreach (var suffix in UniqueSuffixStyles)
                yield return Truncate(baseName + suffix);

            for (var i = 4; i < 1000; i++)
                yield return Truncate($"{baseName} {i}");
        }

        /// <summary>
        /// The first candidate for <paramref name="desired"/> that <paramref name="isTaken"/>
        /// says is free. Throws if none of the first <paramref name="maxAttempts"/> are free —
        /// which would take a document already containing hundreds of copies of the same bad name.
        /// </summary>
        public static string MakeUnique(string desired, Func<string, bool> isTaken, int maxAttempts = 100)
        {
            if (isTaken == null) throw new ArgumentNullException(nameof(isTaken));

            var attempts = 0;
            foreach (var candidate in Candidates(desired))
            {
                if (attempts++ >= maxAttempts) break;
                if (!isTaken(candidate)) return candidate;
            }

            throw new InvalidOperationException($"Could not find a free name for '{desired}' in {maxAttempts} attempts.");
        }

        /// <summary>
        /// Convenience for callers that keep their own set of names handed out so far. The name
        /// returned is added to <paramref name="alreadyUsed"/>.
        /// </summary>
        public static string Reserve(string desired, ISet<string> alreadyUsed, Func<string, bool> isTakenInDocument = null)
        {
            if (alreadyUsed == null) throw new ArgumentNullException(nameof(alreadyUsed));

            var name = MakeUnique(desired, n => alreadyUsed.Contains(n) || (isTakenInDocument?.Invoke(n) ?? false));
            alreadyUsed.Add(name);
            return name;
        }

        /// <summary>Case-insensitive, whitespace-trimmed equality; Revit treats "Level 1" and "level 1" as clashes in some name fields.</summary>
        public static bool RoughlyEquals(string a, string b) =>
            string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);

        private static string Truncate(string s) => s.Length <= MaxLength ? s : s.Substring(0, MaxLength);
    }
}
