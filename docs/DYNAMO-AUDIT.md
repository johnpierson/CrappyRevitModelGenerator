# Auditing the generated models with Dynamo

The generator's own report says what it *intended* to plant. An independent audit says what is
actually in the document. This file specifies graphs that do the second job — useful both as a
check on the generator and as the demo payload the generated models exist to feed.

Two audiences:

- **Interactive** — open a generated model, run the graph in Dynamo for Revit, read the CSV.
- **Headless (Design Automation for Revit / Dynamo Player)** — one workitem per model, no UI.

## Constraints for headless runs

These are what make a graph work as a service rather than only on a desk:

| Constraint | Consequence |
|---|---|
| No UI | No dialogs, no `ActiveView`, no interactive pickers. All I/O through **named input/output nodes** the service binds by node name |
| No absolute paths | Use relative filenames (`"audit-report.csv"`); the service maps them to workitem file arguments |
| No Excel | The sandbox has no Excel, so `Data.ExportExcel` fails. Write **CSV or JSON** |
| One `.rvt` per workitem | The engine opens exactly one document as `Document.Current`. A cross-model roll-up either runs once per model, or takes a placeholder document it never reads |

## Graph 1 — Warnings audit (100 % out-of-the-box nodes)

The simplest useful audit, and the one to start with: no Python, no packages. Revit's own
persistent warnings are the standard first-line model-health signal.

`Document.Current` → `Document.Warnings` → for each: `Warning.GetDescriptionText` and
`Warning.GetFailingElements` → `Element.Id`. Group with `List.GroupByKey` keyed on the
description, count per group, sort with `List.SortByKey` most-frequent-first. Export both the
grouped summary (text + count) and the flat pairing of warning text to failing element ids with
`Data.ExportCSV`, to a String input node `OutputFileName` (default `"warnings-audit.csv"`).

**Unenclosed rooms** is nearly as simple and also fully OOTB: `All Elements of Category` (Rooms)
→ `Element.GetParameterValueByName "Area"` → `== 0` → `List.FilterByBoolMask` → `Data.ExportCSV`.

## Graph 2 — Full quality audit (Python for the parts OOTB cannot reach)

Same execution model, more checks. Inputs: String `OutputFileName` (default
`"audit-report.csv"`). Against `Document.Current`:

1. **Model warnings** — `document.GetWarnings()` in Python; total plus a breakdown by
   `GetDescriptionText()`.
2. **Unenclosed rooms** — `Area == 0`.
3. **Duplicate room numbers** — group by `Number`, flag groups larger than one.
4. **Untagged rooms** — placed rooms with no matching `RoomTag.Room`.
5. **Doors/windows missing `Mark`** — blank `Mark` on `OST_Doors` / `OST_Windows` instances.
6. **Placeholder or default view/sheet names** — `(?i)^(copy of|untitled|new|old|temp|asdf)`, or
   leading/trailing whitespace (Python, `re`).
7. **Views not on a sheet** — views excluding templates and schedules not referenced by any
   `Viewport.ViewId`.
8. **Unused view templates** — templates no non-template view's `ViewTemplateId` references.
9. **Inconsistent wall types per level** — levels carrying more than one distinct
   `WallType.Name`.

Output one row per finding (`Category,ElementId,Issue,Detail`) written as plain CSV from a Python
node, plus an Integer output `FindingCount` and a Boolean output `HasWarnings` so an orchestrator
can triage without parsing the file.

## Graph 3 — Fix common issues (dry-run by default)

Same detectors; adds a Boolean input `ApplyFixes` (default **false**) and String input
`OutputFileName` (default `"fix-report.csv"`). When `ApplyFixes` is true, mutations run inside a
transaction (`TransactionManager.Instance.EnsureInTransaction` / `ForceCloseTransaction`):

- Blank `Mark` on doors/windows → sequential per category (`D-01`, `W-01`, …), skipping values
  already in use.
- Duplicate room `Number`s → append `-DUP1`, `-DUP2`, … to every duplicate after the first.
- Untagged placed rooms → a `RoomTag` at the room's location point.
- View/sheet names with stray whitespace → trimmed.
- Unused view templates → **reported, never deleted**; deletion is a project-standards decision.

Unenclosed rooms and anything Revit reports as a warning are **report-only** — they need human
judgement. Output the audit CSV plus a `Fixed` column (`true`/`false`/`dry-run`) and a Boolean
output `DocumentModified`, true only if a transaction actually committed, so the caller knows
whether to re-upload the `.rvt`.

## Graph 4 — Batch dashboard

Rolls the per-model CSVs into one table. As a Design Automation workitem it takes a placeholder
`.rvt` it never reads (the engine requires one) and a String input `ReportsFolderName` (default
`"reports"`) holding every per-model `audit-report.csv`.

Glob and parse them with Python's `csv`, then emit one row per model:
`ModelName, TotalFindings, WarningCount, UnenclosedRooms, DuplicateRoomNumbers, UntaggedRooms,
MissingMarks, BadViewNames, ViewsNotOnSheets, UnusedViewTemplates, InconsistentWallTypes`,
derived from each file's `Category` column. Sort by `TotalFindings` descending so the worst model
surfaces first; append a `TOTAL` row. Write to a String output `DashboardFileName` (default
`"batch-dashboard.csv"`), with Integer output `ModelsProcessed` and Boolean output `Success`
(true only if at least one report was found and every file had the expected columns).

The caller registers `ReportsFolderName` and the placeholder `.rvt` as workitem input arguments
and `DashboardFileName` as an output argument, matching the `OutputFileName` / `ApplyFixes`
naming convention of the other graphs.

## Why this pairs with the generator

A batch produced by **Batch Generate** (see the README) gives a set of models whose defects are
known in advance, at known severities, from known seeds. Running these graphs across that batch
answers two questions at once:

- *Does the audit tool find what is really there?* — compare the graph's findings against the
  generator's own report for the same model.
- *Do Low / Medium / High actually differ?* — the dashboard's `TotalFindings` should climb with
  severity. That is the concrete version of the "severities feel meaningfully different" item in
  [`MANUAL-QA.md`](MANUAL-QA.md).
