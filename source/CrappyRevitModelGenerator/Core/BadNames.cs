using System.Collections.Generic;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// Fixed lists of legal-but-terrible names and values. Scenarios pick from these instead of
    /// generating arbitrary strings, so output stays readable and every list can be reviewed
    /// against <see cref="NameSanitizer.IsLegal"/> in a unit test. Every entry is a real pattern
    /// seen in projects assembled by teams with weak standards; none contains a character Revit
    /// rejects.
    /// </summary>
    public static class BadNames
    {
        // ---- Views (plan section 7.1) ----------------------------------------------------

        public static readonly IReadOnlyList<string> ViewNames = new[]
        {
            "View 1",
            "View 2",
            "Copy of Copy",
            "Copy of Copy of Level 1",
            "NEW",
            "OLD",
            "Use This One",
            "use this one",
            "Section maybe",
            "3D - FINAL - FINAL2",
            "3D - FINAL",
            "3d view",
            "Level 1 - do not use",
            "level 1 (2)",
            "LEVEL 1 - WORKING",
            "Working View - JP",
            "temp",
            "TEMP",
            "Plan_02 ",
            "plan-02",
            "Elevation - North (old)",
            "Elev North",
            "elevation-north-NEW",
            "Untitled",
            "Detail 4 - CHECK",
            "Drafting View 1",
            "asdf",
            "xx",
        };

        // ---- Sheets ----------------------------------------------------------------------

        public static readonly IReadOnlyList<string> SheetNumbers = new[]
        {
            "A101",
            "A-102",
            "A 103",
            "1",
            "2",
            "PLAN-03",
            "A101a",
            "A101 ",
            "A1.01",
            "SK-1",
            "TBD",
            "XXX",
            "-",
            "A100 (2)",
        };

        public static readonly IReadOnlyList<string> SheetNames = new[]
        {
            "Unnamed",
            "Sheet",
            "New Sheet",
            "Floor Plan",
            "FLOOR PLANS",
            "floor plan level 1",
            "Plans - Copy",
            "Sections and Elevations (maybe)",
            "Cover (maybe)",
            "Details (WIP)",
            "Do Not Print",
            "Sheet 1",
            "sheet 1",
            "General Notes  ",
        };

        // ---- Levels ----------------------------------------------------------------------

        /// <summary>Ordered from lowest to highest so a run of N levels takes the first N.</summary>
        public static readonly IReadOnlyList<string> LevelNames = new[]
        {
            "L1",
            "Level 2",
            "Mezz",
            "Top-ish",
            "Roof (maybe)",
            "LEVEL 03",
            "lvl 4",
        };

        /// <summary>Alternates offered when the primary bad name is already taken in the document.</summary>
        public static readonly IReadOnlyList<string> LevelNameAlternates = new[]
        {
            "L1 (2)",
            "Level 2 - Copy",
            "Mezz 2",
            "Level (new)",
            "Level_",
            "T.O. Something",
            "Level 1a",
            "GROUND",
        };

        // ---- Grids -----------------------------------------------------------------------

        public static readonly IReadOnlyList<string> GridNames = new[]
        {
            "Grid 1",
            "A",
            "2A",
            "Existing (maybe)",
            "B.1",
            "C-C",
            "1'",
            "GRID",
            "grid 2",
            "New Grid",
            "AA",
            "1.5",
            "D (old)",
            "3",
        };

        // ---- Rooms -----------------------------------------------------------------------

        public static readonly IReadOnlyList<string> RoomNames = new[]
        {
            "Office",
            "Office 2",
            "office",
            "OFFICE",
            "Offce",
            "Open",
            "Misc",
            "Room",
            "room",
            "Storage (maybe)",
            "Corridor",
            "Corridoor",
            "Meeting Rm",
            "MTG",
            "Meeting Room ",
            "Break Room / Kitchen",
            "Not Sure",
            "TBD",
            "Toilet",
            "WC",
            "Restroom - M",
            "Copy",
        };

        public static readonly IReadOnlyList<string> RoomNumbers = new[]
        {
            "101",
            "101A",
            "101-old",
            "101 ",
            "102",
            "102",
            "1O3",
            "104",
            "104a",
            "1",
            "2",
            "Rm 3",
            "0",
            "999",
            "TBD",
            "201",
            "201A",
            "2O2",
            "203",
            "3",
        };

        // ---- Types, families, materials --------------------------------------------------

        public static readonly IReadOnlyList<string> TypeSuffixes = new[]
        {
            "-new",
            "_2",
            " copy",
            " final",
            " FINAL2",
            " (1)",
            " - Copy",
            " (Do Not Use)",
            "_old",
            " v2",
            ".",
            " ",
        };

        public static readonly IReadOnlyList<string> MaterialNames = new[]
        {
            "New Mat",
            "Material 1",
            "Material 1 (2)",
            "Gray-ish",
            "grayish",
            "DO NOT USE",
            "Default Material copy",
            "Concrete (maybe)",
            "concrete - new",
            "Paint - White (2)",
            "wood",
            "Wood",
            "MAT_A",
            "mat a",
            "Glass 2",
        };

        // ---- Parameter values (plan section 7.7) -----------------------------------------

        public static readonly IReadOnlyList<string> Comments = new[]
        {
            "",
            "",
            "",
            "check w/ JP",
            "TBC",
            "fix later",
            "??",
            "see email 3/12",
            "moved from other model",
            "DO NOT DELETE",
            "temp - remove",
            "per client",
            "as per client (old)",
            "wrong type?",
            "OK",
            "ok",
            "n/a",
            "N/A",
        };

        public static readonly IReadOnlyList<string> Marks = new[]
        {
            "1",
            "1",
            "2",
            "01",
            "A",
            "A1",
            "a1",
            "D-1",
            "D1",
            "TBD",
            "?",
            "",
            "",
            "100",
            "100 ",
        };

        public static readonly IReadOnlyList<string> Manufacturers = new[]
        {
            "",
            "TBD",
            "tbd",
            "Generic",
            "GENERIC",
            "generic mfr",
            "Acme",
            "ACME Corp.",
            "Acme Corp",
            "See spec",
            "See Spec.",
            "n/a",
            "-",
        };

        public static readonly IReadOnlyList<string> Descriptions = new[]
        {
            "",
            "",
            "TBD",
            "Standard",
            "STANDARD",
            "std.",
            "Type A",
            "type a (old)",
            "See schedule",
            "As per drawings",
            "?",
            "duplicate of other one",
            "New description",
        };

        public static readonly IReadOnlyList<string> Models = new[]
        {
            "",
            "",
            "Model 1",
            "MODEL-1",
            "model 1",
            "X-200",
            "x200",
            "TBD",
            "n/a",
        };

        public static readonly IReadOnlyList<string> Urls = new[]
        {
            "",
            "",
            "",
            "www.example.com",
            "http://example.com/old-link",
            "see spec",
            "TBD",
        };

        public static readonly IReadOnlyList<string> TypeMarks = new[]
        {
            "",
            "",
            "A",
            "A",
            "a",
            "1",
            "01",
            "W1",
            "w1",
            "D-1",
            "TBD",
            "?",
        };

        // ---- Annotations (plan section 7.4) ----------------------------------------------

        public static readonly IReadOnlyList<string> TextNotes = new[]
        {
            "TBD",
            "CHECK",
            "???",
            "REMOVE BEFORE ISSUE",
            "typ.",
            "TYP",
            "see detail",
            "SEE DETAIL 4/A-501 (missing)",
            "coordinate w/ struct",
            "NOTE:",
            "text",
            "Text",
            "do not scale",
            "OLD - ignore",
            "VERIFY IN FIELD",
            "V.I.F.",
            "vif",
        };

        // ---- Duplicated view / near-duplicate helpers ------------------------------------

        public static readonly IReadOnlyList<string> DuplicateViewSuffixes = new[]
        {
            " Copy 1",
            " - Copy",
            " (2)",
            " copy",
            " OLD",
            " NEW",
            " - working",
            " - do not use",
            "2",
            " ",
        };

        // ---- Grouped, for the legality tests --------------------------------------------

        /// <summary>
        /// Lists whose entries become element NAMES (view, sheet, level, grid, type, material
        /// names and the suffixes appended to them). Every entry must satisfy
        /// <see cref="NameSanitizer.IsLegal"/>; Revit rejects <c>\ : { } [ ] | ; &lt; &gt; ? ` ~</c>.
        /// </summary>
        public static IEnumerable<IReadOnlyList<string>> NameLists()
        {
            yield return ViewNames;
            yield return SheetNumbers;
            yield return SheetNames;
            yield return LevelNames;
            yield return LevelNameAlternates;
            yield return GridNames;
            yield return RoomNames;
            yield return RoomNumbers;
            yield return TypeSuffixes;
            yield return MaterialNames;
            yield return DuplicateViewSuffixes;
        }

        /// <summary>
        /// Lists whose entries become parameter VALUES or text-note contents. Any printable text
        /// is acceptable there, so these may contain characters a name may not.
        /// </summary>
        public static IEnumerable<IReadOnlyList<string>> ValueLists()
        {
            yield return Comments;
            yield return Marks;
            yield return Manufacturers;
            yield return Descriptions;
            yield return Models;
            yield return Urls;
            yield return TypeMarks;
            yield return TextNotes;
        }
    }
}
