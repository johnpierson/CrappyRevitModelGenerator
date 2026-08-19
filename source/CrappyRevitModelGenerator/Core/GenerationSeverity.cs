namespace CrappyRevitModelGenerator.Core
{
    /// <summary>
    /// How bad the generated model should be. Severity scales quantities (walls, rooms, views,
    /// duplicates) and the probability that an individual element gets a defect. It never
    /// changes which scenarios run — that is the scenario toggles' job — so a Low run with all
    /// scenarios on still exercises every code path, just gently.
    /// </summary>
    public enum GenerationSeverity
    {
        Low = 0,
        Medium = 1,
        High = 2,
    }
}
