# Crappy Revit Model Generator

A Revit add-in that generates a small, intentionally low-quality but **valid** Revit model on
demand: poor names, wobbly datums, awkward doors and windows, confusing rooms, duplicate views,
near-duplicate types and materials, and half-filled metadata — every defect chosen from a seed,
listed in a report, tagged for cleanup, and removable again with one command.

It exists for BIM training ("find the ten things wrong with this model"), for demonstrating audit
and model-health tools against a controlled sample, and for reproducing bad-model cases
deterministically in QA.

## What it is, and is not

| It is | It is not |
|---|---|
| A generator of *bad but valid* content: legal names, valid geometry, real Revit elements | A corruption tool. It never hands Revit illegal geometry, illegal names, or anything that damages the document |
| Deterministic: same seed + same settings + same template + same Revit version → same choices | A random-string generator. Every bad name and value comes from a fixed, reviewable list |
| Bounded: hard caps on levels, walls, rooms, views, sheets and total elements | A stress or performance tool |
| Reversible: every element carries the run's identity; cleanup deletes only those | A "delete everything" shortcut. Cleanup never touches content it did not create |
| Explainable: every intentional defect appears in the report with its scenario id | A silent modifier of your project. It refuses read-only documents, requires an explicit confirmation, and never saves or syncs |

The design is written up in [`REVIT_BAD_MODEL_GENERATOR_PLAN.md`](REVIT_BAD_MODEL_GENERATOR_PLAN.md);
the Phase 0 choices (versions, schema GUIDs, limits, units, randomness) in
[`docs/DECISIONS.md`](docs/DECISIONS.md); the code map in
[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md).

## Status

Measured against the plan's delivery phases (plan section 12):

| Phase | State | Notes |
|---|---|---|
| 0 — Decisions and fixture setup | Done | `docs/DECISIONS.md`; three build configurations; no bundled fixtures by decision |
| 1 — Add-in shell and baseline model | Done | Manifest, ribbon, four commands, settings + validation, seeded random streams, type discovery with fallbacks, units, transaction group + per-scenario transactions, baseline levels/grids/walls/floors, report |
| 2 — High-value bad choices | Code complete, **partially verified** | Extensible Storage tagging, run record, cleanup, scenario-level rollback and expected-warning capture are in place, as are all nine scenarios. **Generate Bad Model has been run successfully in a live Revit 2026 session.** Clean Generated, View Last Report and Batch Generate have not yet been exercised end to end; treat them as unverified until `docs/INTEGRATION-TESTS.md` has been run |
| 3 — Content and documentation depth | Partly done | Severity profiles, per-scenario random streams, batch generation and JSON export already exist. Fixture families, MEP spaces/zones, dimensions/tags and richer annotation defects are not started |
| 4 — Hardening and distribution | Not started | No repeated generate/clean cycles, no save/reopen or workshared testing yet, no packaging beyond the build's publish folder |

**Verified so far:** all three configurations (R25/R26/R27) build with zero warnings;
1 000 unit tests pass on .NET 8 and .NET 10; the add-in loads in Revit 2026 and generates a model
through the ribbon.

**Known gap:** the headless smoke-test path (`tools/revit-smoke.ps1`) has not yet produced a
report — the add-in's manifest does not appear among the loaded add-ins when Revit is launched
non-interactively, most likely Revit's unsigned-add-in trust prompt with nobody there to accept
it. Interactive use is unaffected.

## Quick start

1. Prerequisites: Windows, Revit 2025/2026/2027, .NET SDK 10.0.300 or later (see `global.json`).
2. Build one configuration with Revit closed. A Debug build installs itself:

   ```
   dotnet build -c "Debug R26"      # Revit 2026; also "Debug R25", "Debug R27"
   ```

   This copies `CrappyRevitModelGenerator.addin` and the `CrappyRevitModelGenerator\` folder into
   `%AppData%\Autodesk\Revit\Addins\2026\`. Details and the Release/uninstall paths are in
   [`docs/INSTALL.md`](docs/INSTALL.md).
3. Start Revit. Accept the add-in load prompt. The **Add-Ins** tab gains a panel
   **Crappy Model Generator** with four buttons:
   - **Generate Bad Model** — pre-flight checks, settings, generation, report.
   - **Clean Generated** — lists the runs recorded in the document and removes their elements.
   - **View Last Report** — the last report of this session, or the newest one stored in the document.
   - **Batch Generate** — creates several fresh projects from a template in one pass, each
     generated at its own seed and severity, saved to a folder you choose. This is the way to
     produce a set of sample models (e.g. "10 models of varying quality") without repeating the
     Generate dialog by hand: pick a template and output folder, a model count, a base seed, and
     whether severity cycles Low → Medium → High or stays fixed. Model *N* gets seed
     `base + (N-1)`, so the batch reproduces exactly given the same inputs. Each model is created,
     generated and saved without touching any document you already have open or switching any
     view, and a `batch-manifest.json`/`.txt` alongside the models summarises the whole run.
4. Recommended target: **a disposable project created from the default architectural template**
   (File > New > Project). The generator discovers wall, floor, door, window, furniture, title
   block and view types from whatever document is active and reports every fallback it takes; a
   project with no template still generates, with fewer things.
5. Click **Generate Bad Model**, keep the defaults (Medium severity, 3 levels, 18 000 × 12 000 mm),
   note the seed, confirm that content goes into the active document, and read the report.
6. Click **Clean Generated** to remove it again, or press Undo once.

## Safety behaviour

- **Active document only.** Content is created in the active project document after an explicit
  confirmation in the dialog (`ConfirmedActiveDocument`). Creating a new document is deferred.
- **Refused outright:** family documents, linked documents, read-only documents, and documents that
  are not modifiable (another transaction open).
- **Workshared documents** (central, local or detached) are blocked unless the user ticks
  *Allow workshared documents*. Content then goes into the active workset.
- **Unsaved changes** produce a warning in the dialog, not a block.
- **Limits are validated before any transaction opens.** The dialog shows the estimated element
  count; anything above the maximum (default 400, hard cap 1 500) is an error.
- **No auto-save, sync, publish or telemetry.** The only file written is a JSON report, and only
  to a path the user chose.
- **One Undo step.** The run is a `TransactionGroup` named `Generate Bad Model (<run id>)` with
  one `Transaction` per scenario; the group is assimilated at the end.
- **Warnings are handled, not hidden.** A curated list of expected warnings (overlaps, rooms not
  enclosed, duplicate values, …) is dismissed so no dialog interrupts the run; every one is still
  recorded in the report. Anything else is recorded and left for Revit to show. An error-level
  failure rolls that scenario back; the run continues. If the baseline scenario cannot run, the
  whole group is rolled back and the report says *Aborted*.
- **Cleanup scope** is exactly the elements the run recorded that still carry the run's identity
  (see below).

## Settings

The Generate dialog edits a `GenerationSettings` object; the same object is serialised into the
report and into the run record so a run can be reproduced.

| Group | Setting | Default | Notes |
|---|---|---|---|
| Run setup | Seed | fresh random | Any `int`; the dialog spinner shows 0–999 999. Shown in the report |
| | Severity | Medium | Low / Medium / High; every quantity comes from `SeverityProfile` |
| | Dry run | off | Plans, estimates and lists the defects that *would* be planted; opens no transaction |
| Model content | Levels | 3 | 1–6 |
| | Footprint width × depth | 18 000 × 12 000 mm | 6 000–40 000 mm each; a corridor layout needs depth ≥ 9 000 |
| | Level height | 3 500 mm | 2 400–6 000 |
| | Create floors / doors & windows / furniture / rooms | on | Toggles the content the planners emit |
| Scenarios | Enabled scenario ids | all default-on scenarios | Baseline always runs. *Generate warnings* is off by default. See `docs/SCENARIOS.md` |
| Safety | Maximum elements | 400 | 20–1 500; the estimate must fit under it |
| | Confirm active document | must be ticked | Not required for a dry run |
| | Allow workshared documents | off | Required when the document is workshared |
| | Suppress all warning dialogs | off | Off: only the curated expected list is dismissed. On: every warning is dismissed. Either way all are recorded |
| | Report export path | none | Optional JSON copy of the report; nothing is written otherwise |

## The report

Shown after every run and stored (as JSON) in the run's `DataStorage` element. It contains:

- Run id, seed, generator version, Revit version, document title, start/finish, dry-run and
  aborted flags.
- The settings that were used, including the resolved scenario list.
- **Counts** by category: Levels, Grids, Walls, Floors, Doors, Windows, Furniture, Rooms,
  RoomSeparationLines, RoomTags, Views, Sheets, Viewports, TextNotes, Types, Materials,
  DataStorage, Other — plus the total.
- **Scenarios**: one line each with status (`Applied`, `Skipped` with reason, `RolledBack` with
  reason, `NotRun` in a dry run), elements committed and duration.
- **Intentional defects**: `[scenario-id] message  ids: …` — every planted defect with up to
  twelve element ids.
- **Fallbacks**: what the type resolver could not find and what it did instead.
- **Information**: which wall/floor/door/window/furniture/title-block types were chosen, and
  baseline totals.
- **Expected warnings (dismissed)** and **Unexpected failures**, each with severity, Revit
  failure definition GUID, message, element ids and whether the transaction was rolled back.
- **Cleanup scope**: how many element ids were recorded, how many refused the identity entity,
  and the id of the run's `DataStorage` element.

## How cleanup decides

Cleanup reads the run records (`DataStorage` elements carrying the `CrappyGenerationRun` schema),
asks for confirmation, and for each run applies `CleanupPlanner` (Revit-free, unit-tested):

1. An id that no longer exists is *already gone* — skipped.
2. An id must still be recognisably ours: it carries the `CrappyGeneratedElement` entity with the
   same run id, or it was recorded as one that refused the entity. Anything else is **kept** and
   reported — an id alone is never enough to delete.
3. An element that something *outside the run* depends on (a user's wall on a generated level)
   is **kept** and reported with the dependent ids.
4. Everything else is deleted in one transaction per run (`CRMG: Clean run <id>`), the run's
   `DataStorage` last. If the active view is on the list, cleanup switches to another view first.

The cleanup report lists deleted / kept / already-gone counts and every kept element with its
reason.

## Auditing what it produced

A generated model is only useful if something reads it back. [`docs/DYNAMO-AUDIT.md`](docs/DYNAMO-AUDIT.md)
specifies Dynamo graphs that audit these models independently of the generator's own report —
starting with a warnings audit that needs only out-of-the-box nodes, through a full quality audit,
a dry-run-by-default fix graph, and a cross-model dashboard. They are written to run either
interactively in Dynamo for Revit or headless as Design Automation workitems.

Pairing them with **Batch Generate** answers two questions at once: whether the audit finds what
is genuinely in the model (compare against the generator's own report for the same seed), and
whether Low / Medium / High severities actually differ (findings should climb with severity).

## Limits

From `Core/GenerationLimits.cs`; anything outside a range is a validation error before a
transaction opens.

| Limit | Min | Default | Max / hard cap |
|---|---|---|---|
| Levels | 1 | 3 | 6 |
| Footprint width, depth (mm) | 6 000 | 18 000 × 12 000 | 40 000 |
| Level height (mm) | 2 400 | 3 500 | 6 000 |
| Walls | — | by severity and footprint | 120 (planner cap) |
| Rooms | — | 4–10 by severity | 24 |
| Views | — | by severity | 40 |
| Sheets | — | 2–4 by severity | 10 |
| Materials | — | 3–7 by severity | 12 |
| Duplicate types | — | by severity | 12 |
| Total generated elements | 20 | 400 | 1 500 |
| Seed (dialog range) | 0 | random | 999 999 |

## Supported Revit versions

| Configuration | Revit | Target framework | Runtime |
|---|---|---|---|
| `Debug R25` / `Release R25` | 2025 | `net8.0-windows` | .NET 8, shipped with Revit |
| `Debug R26` / `Release R26` | 2026 | `net8.0-windows` | .NET 8, shipped with Revit |
| `Debug R27` / `Release R27` | 2027 | `net10.0-windows` | .NET 10, shipped with Revit |

Revit 2024 and earlier (.NET Framework) are out of scope. A plain `dotnet build` maps to R26.

## Repository layout

```
REVIT_BAD_MODEL_GENERATOR_PLAN.md      the plan; section 7 is the defect catalog
docs/                                  DECISIONS, ARCHITECTURE, INSTALL, SCENARIOS,
                                       INTEGRATION-TESTS, MANUAL-QA, DYNAMO-AUDIT
source/CrappyRevitModelGenerator/
  Core/        Revit-free: settings, limits, seeded random, bad names, name sanitiser,
               report, scenario catalog, geometry, planners, cleanup decisions
  Revit/       API edge: safety guard, transactions + failure capture, type resolver,
               element factory, units, identity (Extensible Storage), run store, runners
  Scenarios/   one class per catalog id
  Commands/    IExternalCommand entry points + the headless automation runner
  UI/          WPF dialogs (generate, report, cleanup, batch)
  Resources/   ribbon icons
tests/CrappyRevitModelGenerator.Tests/ xunit, net8.0 + net10.0, links Core/**/*.cs
tools/apicheck.sh                      verifies API members against the 2025/26/27 XML docs
tools/revit-smoke.ps1                  drives a headless Revit session end to end (see Status)
```

[`docs/ARCHITECTURE.md`](docs/ARCHITECTURE.md) explains the layers, the run pipeline, the
scenario order, the determinism contract and the identity/cleanup rules.

## Development

```
dotnet build -c "Debug R26"                       # build + install for Revit 2026 (also R25, R27)
dotnet build -c "Release R26"                     # publish folder only, under bin\
dotnet test tests/CrappyRevitModelGenerator.Tests # Core unit tests on net8.0 and net10.0
bash tools/apicheck.sh 'M:Autodesk.Revit.DB.Floor.Create\('   # Y/N for 2025, 2026, 2027
bash tools/apicheck.sh --grep 'BuiltInFailures.RoomFailures'  # list matching members
bash tools/apicheck.sh --doc 'M:Autodesk.Revit.DB.Viewport.Create('  # doc block
```

Rules that keep the code honest (see `docs/ARCHITECTURE.md`): `Core/` never references
`Autodesk.Revit.*`; scenarios create elements only through `ElementFactory` and draw randomness
only from named `RandomStream`s; obsolete-API warnings (`CS0612`, `CS0618`) are build errors so a
build that passes on all three configurations is not relying on something the next release
removes.

### Headless runs

`App.OnStartup` checks the `CRMG_AUTOMATION` environment variable; when it points at a JSON
parameters file, the add-in runs one generation (and optionally a cleanup) on the first `Idling`
tick, writes the report, and exits Revit. `tools/revit-smoke.ps1` wraps that:

```
powershell -File tools/revit-smoke.ps1 -RevitYear 2026 -Seed 42 -Severity Medium -Cleanup
```

Parameters: `report` (required output path), `settings`, `seed`, `severity`, `scenarios`,
`dryRun`, `template`, `saveAs`, `cleanup`. See the Status section for the current limitation.

## License

GPL-3.0-or-later. See [`LICENSE`](LICENSE).

## Credits

Design Tech Unraveled.
