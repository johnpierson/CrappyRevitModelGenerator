# Architecture and conventions

This is the map for anyone adding a scenario, a dialog or a test. It follows the plan
(`REVIT_BAD_MODEL_GENERATOR_PLAN.md`, sections 4, 8 and 9); where the code deviates from the
plan's file list, this document is right and says why.

## Layers

```
source/CrappyRevitModelGenerator/
  Core/        Revit-free. Settings, limits, seeded random, bad-name lists, name sanitiser,
               report model, scenario catalog, geometry, planners, cleanup decisions.
               Linked into the xunit project — MUST NOT reference Autodesk.Revit.*.
  Revit/       The API edge. Safety guard, transactions + failure capture, type resolver,
               element factory, units, identity (Extensible Storage), run store, runners,
               GenerationContext (lives here, not in Core, because it holds a Document).
  Scenarios/   One class per catalog id, each implementing IBadModelScenario. Uses only
               context.Factory to create, context.Random.Stream("<id>/...") for randomness,
               context.Report to explain itself.
  Commands/    IExternalCommand entry points. Thin: guard, dialog, runner, report.
  UI/          WPF dialogs. Dialogs.cs is the contract the commands call.
tests/CrappyRevitModelGenerator.Tests/   xunit, net8.0 + net10.0, links Core/**/*.cs.
tools/apicheck.sh                        Verifies API members against the 2025/26/27 XML docs.
```

Rule of thumb: if a file in `Core/` needs a Revit type, the logic belongs in `Revit/` and the
decision belongs in `Core/` (see `CleanupPlanner` vs `CleanupRunner` for the pattern).

## Naming collisions

`Nice3point.Revit.Build.Tasks` injects `global using Autodesk.Revit.DB;` into the add-in
project. Core type names therefore avoid every Revit DB type name (`Level`, `Wall`, `Grid`,
`Floor`, `View`, `Color`, `Units`, `Options`, `Line`, `Curve`, `Material`, `Room`, `Phase`,
`Category`, `Element`, `Group`, `Instance`, `Definition`, `Transaction`, `Document`, …). Core
uses `LevelSpec`, `WallSpec`, `Point2D`, `Segment2D`, `Rect2D`, `GeneratedCategory`, and so on.

## Run pipeline

1. `GenerateBadModelCommand` → `DocumentSafetyGuard.CheckDocument` → `Dialogs.ShowGenerateDialog`.
2. `GenerationRunner.Run` → `DocumentSafetyGuard.CheckRun` → dry run, or:
3. `GenerationContext` (random, profile, type resolver, factory, registry, failure capture).
4. `TransactionCoordinator.StartGroup()`; `ScenarioRunner.RunAll()` runs each enabled scenario
   in catalog order inside its own `Transaction` with `FailureCapture` as preprocessor.
   - Baseline is required: if it cannot run or rolls back, the group is rolled back and the
     report says `Aborted`.
   - Any other scenario that throws or hits an error-level failure is rolled back, recorded,
     and the run continues.
5. Final transaction writes the `DataStorage` run record (`RunStore.Write`).
6. `TransactionCoordinator.Assimilate()` → one Undo step. Report shown, optionally exported.

## Scenario order (ScenarioCatalog)

| Order | Id | What it may rely on |
|---|---|---|
| 10 | `baseline` | nothing — creates levels, plan views, grids, walls, floors; fills `context.Baseline`, `Levels`, `PlanViews`, `Grids`, `Walls`, `Floors` |
| 20 | `content-placement` | baseline; fills `Openings`, `Furniture`; sets `context.Content` |
| 30 | `rooms` | baseline (+ openings exist); fills `RoomElements`, `RoomTags`, `SeparationLines`; sets `context.Rooms` |
| 40 | `documentation` | baseline plan views, rooms; fills `Views`, `Sheets`, `Viewports`, `TextNotes` |
| 50 | `datum` | grids + plan views (bubble visibility needs a view), walls |
| 60 | `naming` | everything above — renames generated levels, grids, views, sheets, rooms, types |
| 70 | `content-types` | walls/floors/materials exist — duplicates types and materials |
| 80 | `metadata` | everything — sets parameters on generated elements only |
| 90 | `warnings` | everything — opt-in, creates overlaps/duplicates |

Planner-time defects: the datum scenario's layout defects (levels, grid, wall and floor
irregularities) are planted by `BaselinePlanner` when `datum` is enabled and are created by the
baseline scenario; the report attributes them to `datum`. Odd location lines and per-spec
disallowed joins are applied by `ElementFactory.CreateWall` from the `WallSpec` at creation
time. `DatumScenario` itself does only the element-level tweaks that need existing elements
and views: grid bubbles hidden per view, view-specific grid extents, level bubbles, and a few
extra wall joins disallowed after the fact.

## Determinism contract

- One `SeededRandom(seed)` per run; every draw goes through a named `RandomStream`. Stream
  names are `"<scenario-id>/<purpose>"` (planner streams are constants on the planner class).
- Never call `System.Random`, `Guid.NewGuid()` or `DateTime.Now` to make a *choice*. Run ids
  and timestamps are metadata, not choices.
- Choices are always from fixed lists (`BadNames`) or bounded numeric ranges.
- Reading a stream in a different order or count changes that stream only.

## Names

- `NameSanitizer.MakeLegal` before any name reaches Revit; `ElementFactory.TrySetName`
  walks `NameSanitizer.Candidates` until Revit accepts one; it never throws for a taken name.
- Names come from `BadNames.NameLists()`; parameter values from `BadNames.ValueLists()`.

## Identity and cleanup

- `GeneratedElementRegistry.Register` (called by every factory method) attaches the
  `CrappyGeneratedElement` entity and stages the record; the coordinator commits or discards
  the stage with the transaction.
- `RunStore.Write` stores the `CrappyGenerationRun` record on a `DataStorage`.
- `CleanupPlanner` (Core) decides; `CleanupRunner` (Revit) executes. Cleanup never deletes an
  id that does not still carry the run's identity (or was recorded as untaggable), and never
  deletes an element that non-generated content depends on.

## Verifying API members

`bash tools/apicheck.sh 'M:Autodesk.Revit.DB.Floor.Create\('` prints `Y/N` for 2025, 2026 and
2027. `--grep <regex>` lists matching members; `--doc <member>` prints the documentation block.
Use it before calling anything you are not certain exists in all three versions.
Known removals: `ElementId.IntegerValue` and `new ElementId(int)` are gone in 2026+; use
`ElementId.Value` (long) and `new ElementId(long)`.

## Building

```
dotnet build -c "Debug R26"      # also R25, R27; Debug publishes to %AppData%\Autodesk\Revit\Addins\<year>
dotnet build -c "Release R26"    # publish folder only, under bin\
dotnet test tests/CrappyRevitModelGenerator.Tests
```

`CS0612`/`CS0618` (obsolete API) are errors, so a build that passes on all three
configurations is not silently relying on something the next Revit release removes.
