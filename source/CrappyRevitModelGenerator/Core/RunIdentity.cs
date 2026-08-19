using System;
using System.Globalization;

namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// Run ids and generator identity strings. A run id is unique per run (it embeds the time
    /// and a random tail) — it is NOT part of the determinism contract; the seed is.
    /// </summary>
    public static class RunIdentity
    {
        public const string SchemaVendorId = "DesignTechUnraveled";

        /// <summary>Format: <c>yyyyMMdd-HHmmss-&lt;seed&gt;-&lt;4 hex&gt;</c>, e.g. <c>20260818-141503-42-9f3a</c>.</summary>
        public static string NewRunId(int seed, DateTime utcNow, Guid tail)
        {
            var stamp = utcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
            var hex = tail.ToString("N").Substring(0, 4);
            return $"{stamp}-{seed.ToString(CultureInfo.InvariantCulture)}-{hex}";
        }

        public static string NewRunId(int seed) => NewRunId(seed, DateTime.UtcNow, Guid.NewGuid());

        public static bool TryParseSeed(string runId, out int seed)
        {
            seed = 0;
            if (string.IsNullOrEmpty(runId)) return false;
            var parts = runId.Split('-');
            // yyyyMMdd, HHmmss, seed (may be negative -> extra part), hex
            if (parts.Length < 4) return false;
            var seedText = parts.Length == 4 ? parts[2] : "-" + parts[3];
            return int.TryParse(seedText, NumberStyles.Integer, CultureInfo.InvariantCulture, out seed);
        }
    }
}
