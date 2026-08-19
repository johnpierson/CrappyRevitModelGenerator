using System;
using System.Collections.Generic;
using System.Linq;
using CrappyRevitModelGenerator.Core;

namespace CrappyRevitModelGenerator.Scenarios
{
    /// <summary>
    /// Maps catalog ids to implementations. Every id in <see cref="ScenarioCatalog.All"/> must
    /// have an entry here; a unit-testable check in the runner refuses to start otherwise.
    /// </summary>
    public static class ScenarioFactory
    {
        public static IReadOnlyList<IBadModelScenario> CreateAll() => new IBadModelScenario[]
        {
            new BaselineModelScenario(),
            new ContentPlacementScenario(),
            new RoomsScenario(),
            new DocumentationScenario(),
            new DatumScenario(),
            new NamingScenario(),
            new ContentTypesScenario(),
            new MetadataScenario(),
            new WarningsScenario(),
        };

        /// <summary>Implementations for the requested ids, in catalog order.</summary>
        public static IReadOnlyList<IBadModelScenario> CreateFor(IEnumerable<string> scenarioIds)
        {
            var all = CreateAll().ToDictionary(s => s.Id, StringComparer.OrdinalIgnoreCase);
            var missing = ScenarioCatalog.All.Where(d => !all.ContainsKey(d.Id)).Select(d => d.Id).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException("Scenario catalog entries without an implementation: " + string.Join(", ", missing));

            return ScenarioCatalog.Ordered(scenarioIds)
                .Select(d => all[d.Id])
                .ToList();
        }
    }
}
