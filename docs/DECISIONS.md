# Phase 0 decisions

These are the decisions the plan (`REVIT_BAD_MODEL_GENERATOR_PLAN.md`, section 12, Phase 0)
asks for before code is written. Each one records the choice, the reason, and what would
make us revisit it.

## Revit versions and .NET targets

| Configuration | Revit | Target framework | Revit API package |
|---|---|---|---|
| `Debug R25` / `Release R25` | 2025 | `net8.0-windows` | `Nice3point.Revit.Api.RevitAPI 2025.*` |
| `Debug R26` / `Release R26` | 2026 | `net8.0-windows` | `Nice3point.Revit.Api.RevitAPI 2026.*` |
| `Debug R27` / `Release R27` | 2027 | `net10.0-windows` | `Nice3point.Revit.Api.RevitAPI 2027.*` |

- **First compile target: Revit 2026** (`Debug R26`). A plain `dotnet build` with no
  configuration maps to it. It is the current shipping release and the one most likely to be
  used for training material this year.
- 2025 and 2027 are built from the same source through configuration-selected target
  frameworks, exactly as `3dSpatialTags` does. There is no .NET Framework build: Revit 2024 and
  earlier are out of scope for the first release.
- The Revit API comes from the Nice3point NuGet packages, not from a `HintPath` into
  `C:\Program Files\Autodesk`, so a machine without Revit can still build. Lock files
  (`packages.R20xx.lock.json`) pin the exact API package patch per year.
- `Nice3point.Revit.Build.Tasks` provides the `REVIT2026`, `REVIT2026_OR_GREATER` … define
  constants for the rare `#if` a version difference needs, and publishes Debug builds to
  `%AppData%\Autodesk\Revit\Addins\<year>\`. Release builds only produce a publish folder.

## Template and fixtures

- **No bundled `.rte` or `.rfa` fixtures in the first release.** The generator discovers what
  the active document already has (`TypeResolver`) and reports each fallback it takes. A
  document with no door/window/furniture families or no title block still generates; the
  report says what was skipped and why. Curated fixture families are Phase 3.
- Recommended target document for users: a fresh project from Autodesk's default
  architectural template, or any disposable copy of an office template.

## Active document vs. new document

- **Active-document only** for the first release, behind an explicit confirmation that names
  the document. Creating a new project document from the add-in needs a template path and a
  UI-thread document switch and is deferred.
- Read-only documents are refused. Workshared documents require an extra opt-in checkbox.

## Identity and cleanup

- Extensible Storage schema **`GeneratedElement`**, GUID `06A9B449-E2E6-4251-89F3-E3DC66BD5160`,
  vendor `DesignTechUnraveled`, fields `RunId`, `ScenarioId`, `Seed`, `GeneratorVersion`,
  `CreatedUtc`. Attached to every generated element that accepts it.
- Extensible Storage schema **`GenerationRun`**, GUID `5B13A5D5-1582-46CC-9B55-43107D7AA4D7`,
  stored on one `DataStorage` element per run: `RunId`, `Seed`, `Severity`, `GeneratorVersion`,
  `RevitVersion`, `CreatedUtc`, `SettingsJson`, `ReportJson`, `ElementIds` (all generated
  element ids), `UntaggedElementIds` (elements that refused the entity).
- Cleanup deletes only ids recorded in a run's `DataStorage` **and** confirmed to still be ours
  (entity present with the same `RunId`, or listed under `UntaggedElementIds`). An element
  that user-created content depends on is kept and reported.
- Schema versions are part of the schema name (`GeneratedElement`, `GenerationRun`); a
  breaking field change gets a new GUID and a new name, never a silent field change.

## Default limits (hard caps in parentheses)

| Limit | Default | Hard cap |
|---|---|---|
| Levels | 3 | 6 |
| Footprint width/depth (mm) | 18 000 × 12 000 | 40 000 |
| Walls | ~24–48 by severity | 120 |
| Rooms | 6–10 | 24 |
| Views | 6–12 | 40 |
| Sheets | 2–4 | 10 |
| Total generated elements | estimated in dialog | 1 500 |

Anything above a hard cap is a validation error before a transaction opens.

## Units

- Every dimension in the core layer is **millimetres**. Conversion to Revit internal feet
  happens once, in `Revit/UnitConversion.cs`, through `UnitUtils`.

## Randomness

- One `int` seed. Each scenario draws from its own **named stream** (`SeededRandom.Stream(id)`)
  seeded by a stable hash of (seed, stream name), so adding a scenario does not reshuffle the
  choices of the ones that already exist. The PRNG is a small PCG32 implemented in
  `Core/SeededRandom.cs`, not `System.Random`, so sequences are identical on .NET 8 and .NET 10.
