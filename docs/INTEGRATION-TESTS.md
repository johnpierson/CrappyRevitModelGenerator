# Revit integration test procedure

The manual counterpart of the unit tests: what a person runs inside Revit to prove the loop
*configure → generate → tag → report → clean up* works (plan section 11, "Revit integration
tests"). Written for **Revit 2026** and the `Debug R26` build; the same steps apply to 2025 and
2027 with the matching configuration.

None of these tests has been executed yet. Record the outcome of each in the table at the end
and file anything unexpected as an issue with the report JSON attached.

## Before you start

- Build and install with Revit closed: `dotnet build -c "Debug R26"` (see `INSTALL.md`).
- Start Revit 2026, accept the add-in load prompt, confirm the **Add-Ins > Crappy Model
  Generator** panel exists.
- Create the **disposable test project**: File > New > Project, choose the architectural
  template that ships with Revit, Project. Save it as `crmg-test.rvt` in a scratch folder.
  This document is throwaway; nothing here should be run in a real project.
- Have a scratch folder ready for exported report JSON files (the *Report export path* setting).

Terms used below:

- *Report window*: the dialog shown after a run; the same text is available later via
  **View Last Report**.
- *Defect line*: an entry under **Intentional defects** in the report, formatted
  `[scenario-id] message  ids: …`.
- *Manage > Warnings*: Revit's Manage tab, Inquiry panel, Warnings button — the persistent
  warnings Revit keeps for the document.

## Test 1 — Low-severity run in an empty project

1. In `crmg-test.rvt`, click **Generate Bad Model**.
2. Settings: seed `42`, severity **Low**, everything else at its default (3 levels,
   18 000 × 12 000 mm, all default-on scenarios, *Generate warnings* off). Tick the active-document
   confirmation. Set a report export path, e.g. `…\scratch\low-42.json`.
3. Generate.

Expected:

- No exception dialog. The report window opens with "Generation complete".
- **Scenarios**: `baseline` is `Applied`; every other default scenario is `Applied`, or `Skipped`
  with a reason that names missing content (e.g. no furniture families). None is `RolledBack`.
- **Counts**: Levels 3, Grids ≥ 6, Walls between 24 and 48, Floors 3, Views ≥ 3, DataStorage 1;
  Total ≤ 400 (the default maximum). Exact numbers depend on the seed and the template; the
  point is that every category the settings enabled is non-zero and the total is under the cap.
- **Intentional defects** lists at least one line per applied scenario. **Fallbacks** lists what
  the template lacked, if anything. **Unexpected failures** is empty or contains only warnings
  (severity `Warning`, no `(rolled back)` suffix).
- **Cleanup scope** reports N recorded ids, 0 (or a small number) untagged, and a DataStorage id.
- Project Browser: three new floor plans, new levels, and — with the documentation scenario —
  new sections/elevations/3D/drafting views and 2 sheets, all with recognisably bad names.
- Undo history: exactly one entry, `Generate Bad Model (<run id>)`. Undo once removes everything;
  Redo brings it back. Leave it redone.
- Manage > Warnings: may list persistent conditions the generator planted (rooms not enclosed,
  duplicate room numbers, walls slightly off axis). Every one should be traceable to a defect
  line or an expected-warning line in the report. Nothing about elements outside the run.
- `low-42.json` exists and its `Counts` match the report window.

## Test 2 — Each scenario alone

For each of the eight optional scenarios, start from a **fresh** document (new project from the
same template; or run **Clean Generated** on the previous run and confirm the cleanup report shows
`Elements kept: 0`), then run with **only that scenario** enabled (baseline always runs), seed
`42`, severity **Medium**.

| Scenario id | Expect in the document | Expect in the report |
|---|---|---|
| `content-placement` | Doors in corridor/exterior walls, windows in exterior walls at more than one sill height, a few furniture pieces, one or two outside the footprint (Medium: 1) | `Applied`; defect lines about doors near wall ends, window pairs too close, sill heights, furniture rotated/outside/on a wall; fallbacks if a family kind is missing |
| `rooms` | Rooms in most cells, one unplaced room (Room schedule shows *Not Placed*), room separation lines, some rooms untagged, some tags pushed against a wall | `Applied`; defect lines per unplaced/duplicate/untagged/awkward-tag room; expected warnings *not in a properly enclosed region* / *Multiple Rooms are in the same enclosed region* dismissed and listed |
| `documentation` | Duplicate floor plans, sections, elevations, 3D and drafting views; 3 sheets, one empty; viewports on the others; text notes such as `TBD`, `CHECK`, `???` | `Applied`; defect lines for odd scales, wrong discipline, odd crops, empty sheet, misleading numbers |
| `datum` | Levels not on a clean 3 500 mm module plus a `Mezzanine` with no plan; some grids with a bubble at one end only; grid extents inconsistent; one near-coincident grid; a few partitions a few mm off, one corner gap, one stub wall in the corridor | `Applied`; the planted layout defects appear as `[datum]` lines even though the baseline created them |
| `naming` | Levels like `L1`, `Level 2`, `Mezz`; grids like `Grid 1`, `A`, `2A`; plan views like `Copy of Copy`, `Use This One`; sheets like `A-102`, `PLAN-03`; rooms like `Offce`, `Misc` (only if `rooms` is also on) | `Applied`; a summary line per kind renamed |
| `content-types` | Duplicated wall/floor types with `-new`, `_2`, `copy` suffixes in the type selector; new materials `New Mat`, `Material 1`, `DO NOT USE`, near-duplicates | `Applied`; one line per duplicate type / material |
| `metadata` | Generated walls/floors/doors/windows with mixed Mark, Comments, Manufacturer, Description, Type Mark values; some blank, some duplicated | `Applied`; summary lines per parameter; expected warning *Elements have duplicate 'Mark' values* dismissed and listed |
| `warnings` | Two overlapping walls, duplicate furniture instances in place, one overlapping floor | `Applied`; expected warnings *Highlighted walls overlap*, *identical instances in the same place*, *Highlighted floors overlap* dismissed and listed |

For every row: the scenario must be togglable off (re-run with it unticked, confirm it is absent
from the report and the document) and, when on, must never leave a scenario `RolledBack` in a
default-template document. Any `RolledBack` is a finding.

## Test 3 — Same seed twice

1. Two **fresh** documents from the same template: `crmg-a.rvt` and `crmg-b.rvt`.
2. In each: seed `4242`, severity **Medium**, defaults, all default scenarios, export path
   `…\a-4242.json` / `…\b-4242.json`.
3. Compare the two JSON files after removing the fields that legitimately differ:
   `RunId`, `StartedUtc`, `FinishedUtc`, `DocumentTitle`, `Settings.ReportExportPath`,
   `RunStorageElementId`, `GeneratedElementIds`, `UntaggedElementIds`, every `ElementIds` array,
   and every `DurationMs`.

Expected:

- `Counts` identical.
- `Scenarios[*].Status` and `ElementsCreated` identical.
- The **messages** of every note (defects, fallbacks, information) identical and in the same
  order.
- In Revit: identical level names and elevations, grid names, view and sheet names, room names
  and numbers, and the same doors/windows at the same positions (spot-check two walls with a
  temporary dimension).

The guarantee is *same seed + same settings + same template + same Revit version*. Running the
same seed twice in the **same** document (after cleanup) should also match, except that names
Revit still considers taken get the next bad suffix (`… (2)`, `… - Copy`).

## Test 4 — Save, reopen, clean up

1. After any run (Test 1 will do), **Save**, close the document, close Revit, reopen Revit and
   the document.
2. **View Last Report**: the report opens with the extra line "Loaded from the run record stored
   in the document." — proof the run record survived save/reopen.
3. **Clean Generated**: the dialog lists the run (run id, seed, element count, timestamp).
   Confirm.

Expected:

- Cleanup report: `Elements deleted` equals the recorded count (or that count minus explained
  kept elements), `Elements kept: 0`, `Already gone: 0`, `Run records removed: 1`.
- Project Browser: generated levels, plans, sections, elevations, sheets and drafting views are
  gone; the template's own views and levels are untouched.
- Manage > Warnings: none of the generated conditions remain.
- Manage > Materials: generated materials gone. Type selector: duplicated types gone.
- **Clean Generated** again: "No generated runs were found in this document."
- Undo history: one entry `CRMG: Clean run <run id>`.

If the active view was one of the generated views, the cleanup report says it switched the
active view first; that is expected.

## Test 5 — A user-created control element survives

1. Fresh document. Before generating, draw one wall by hand on the template's Level 1, well away
   from the origin (this is *control A*), and add a text note *control B* in a template view.
2. Generate (seed `42`, Medium, defaults).
3. After generating, draw one more wall by hand on a **generated** level (e.g. the generated
   `Level 02` / `Level 2`) — *control C*.
4. **Clean Generated**.

Expected:

- Control A and control B are untouched (position, type, text).
- Control C survives, and the generated level it sits on is **kept**: the cleanup report shows
  `kept <level id>: kept: 1 element(s) not created by this run depend on it (ids <control C id>)`.
  Everything else from the run is deleted.
- At no point does cleanup delete an element it did not create.
- Tidy-up: the run record is removed with the run, so the kept level cannot be cleaned by the
  add-in afterwards; delete control C and then the level by hand.

## Test 6 — Missing content does not crash the run

1. File > New > Project, template **None**, Project. Save as `crmg-empty.rvt`.
2. Generate with seed `42`, Medium, all default scenarios.

Expected, one of two outcomes, both acceptable:

- If the empty project has at least one basic wall type and a floor-plan view type: the run
  proceeds. **Fallbacks** lists the missing kinds (no door families, no window families, no
  furniture families, no title block, only one wall type, no second thickness, possibly no floor
  type or text note type). Content that needs them is skipped and the affected scenario reports
  `Applied` with fewer elements or `Skipped` with a reason. Sheets, if created, have no title
  block.
- If it has no basic wall type at all: the report says **ABORTED** with
  `Required scenario 'Baseline model' cannot run: The document has no basic wall types.` and the
  document is unchanged (no undo entry, no run record).

In neither case: an unhandled-exception dialog, a half-created model, or a run record for
elements that do not exist.

## Test 7 — Forced scenario rollback

There is no user-facing switch that makes a scenario fail; overlaps and duplicates are warnings,
not errors, so the `warnings` scenario does not roll back on a default template. To exercise the
rollback path a developer temporarily makes an optional scenario throw:

1. In `Scenarios/ContentTypesScenario.cs`, add `throw new InvalidOperationException("forced");`
   as the last line of `Generate`. Rebuild (`Debug R26`), restart Revit.
2. Fresh document; generate with seed `42`, Medium, defaults.

Expected:

- The report lists `content-types` as `RolledBack` with `InvalidOperationException: forced`, and
  the same exception under **Unexpected failures** with the `(rolled back)` suffix.
- Every scenario after it (`metadata`) still ran and is `Applied`.
- **Counts** contain no `Types` or `Materials` created by the rolled-back scenario; the type
  selector and material browser show none of its duplicates.
- **Cleanup scope** ids equal the counts total (rolled-back registrations were discarded, so no
  ids of non-existent elements are recorded).
- **Clean Generated** afterwards reports `Already gone: 0` — the acceptance criterion "an
  intentional scenario rollback does not leave orphaned registry entries".
- One undo entry for the whole run, as usual.

3. Move the `throw` to `BaselineModelScenario.Generate` and repeat.

Expected: the report says **ABORTED** (`Required scenario 'Baseline model' failed: …`), the
document is unchanged, there is no undo entry, and **Clean Generated** finds no runs.

4. Remove the `throw`, rebuild.

## Test 8 — Refusals

Short checks of the pre-flight guard:

| Situation | How to set up | Expected |
|---|---|---|
| Read-only document | Open a `.rvt` from a folder you have made read-only, or a file opened as read-only | **Generate Bad Model** shows the blocker "The active document is read-only…" and returns without a dialog |
| Family document | Open any `.rfa` | Blocker "The active document is a family…" |
| Workshared, opt-in off | Enable worksharing on a copy of `crmg-test.rvt` (Collaborate > Worksets), save as central, reopen | The dialog shows the workshared warning; running with *Allow workshared documents* unticked is blocked with "…'Allow workshared documents' is not enabled"; ticking it lets the run proceed into the active workset |
| Unsaved changes | Draw a line, do not save, run | The dialog shows the unsaved-changes warning; the run is allowed |
| Estimate over the maximum | Set *Maximum elements* to 20 | Validation error "The estimated element count (…) exceeds the maximum (20)…"; no transaction opens |
| Dry run | Tick *Dry run* | Report says `DRY RUN - nothing was created`, lists estimated counts and the defects that would be planted; no undo entry; the document's modified flag does not change |

## Where to look — summary

| Question | Where |
|---|---|
| What was created, which scenarios ran, what went wrong | Report window; **View Last Report**; exported JSON |
| What Revit itself thinks is wrong with the model | Manage > Inquiry > Warnings |
| Names of levels, grids, views, sheets | Project Browser; a View List / Sheet List schedule for bulk review; an elevation view for level names and elevations |
| Rooms, numbers, unplaced rooms | A Room schedule (View > Schedules > Schedule/Quantities > Rooms); unplaced rooms show *Not Placed* |
| Types and materials | Type selector for a wall/floor; Manage > Materials |
| Metadata values | Element properties; a Door/Window/Wall schedule with Mark, Comments, Manufacturer, Description columns |
| Whether an element belongs to a run | It carries the `CrappyGeneratedElement` Extensible Storage entity (visible with an Extensible Storage viewer add-in such as RevitLookup) |
| The run record | A `DataStorage` element named `CrappyRevitModelGenerator Run <run id>` (RevitLookup, or a Dynamo/pyRevit query) |

## Results

| Test | Revit | Build | Date | Result | Notes / issue link |
|---|---|---|---|---|---|
| 1 Low-severity run | | | | | |
| 2 Each scenario alone | | | | | |
| 3 Same seed twice | | | | | |
| 4 Save, reopen, clean up | | | | | |
| 5 Control element survives | | | | | |
| 6 Missing content | | | | | |
| 7 Forced rollback | | | | | |
| 8 Refusals | | | | | |
