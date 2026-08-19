# Changelog

All notable changes to this project are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/); versions follow
[Semantic Versioning](https://semver.org/). The version number lives in `Directory.Build.props`
and is stamped into the assembly, the report and the Extensible Storage records.

## [0.1.0] — Unreleased

Initial implementation of the plan in `REVIT_BAD_MODEL_GENERATOR_PLAN.md`, Phases 0–2. Nothing
in this release has yet been exercised in a running Revit session; see `docs/INTEGRATION-TESTS.md`
for the procedure that will change that.

### Added

- **Add-in shell**: `.addin` manifest (Application, stable `ClientId`), ribbon panel *Crappy
  Model Generator* on the Add-Ins tab with **Generate Bad Model**, **Clean Generated** and
  **View Last Report**; no event subscriptions or modeless windows.
- **Build matrix**: `Debug|Release R25/R26/R27` for Revit 2025 (`net8.0-windows`), 2026
  (`net8.0-windows`) and 2027 (`net10.0-windows`) from one source tree; Revit API from the
  Nice3point NuGet packages with per-version lock files; Debug builds install to
  `%AppData%\Autodesk\Revit\Addins\<year>\`; obsolete-API warnings (`CS0612`, `CS0618`) are build
  errors. `tools/apicheck.sh` verifies API members against the 2025/2026/2027 XML docs.
- **Safety**: pre-flight guard refusing family, linked, read-only and non-modifiable documents;
  workshared documents require an explicit opt-in; unsaved changes warned; explicit
  active-document confirmation; settings validated against hard limits and an element-count
  estimate before any transaction opens; no auto-save, sync, publish or telemetry; JSON report
  written only to a user-chosen path.
- **Core (Revit-free)**: `GenerationSettings` + validation, `GenerationLimits`, `SeverityProfile`
  (Low / Medium / High in one place), `SeededRandom` (PCG32, named per-scenario streams),
  `BadNames` (fixed lists of legal but terrible names and values), `NameSanitizer`,
  `GenerationReport` (text + JSON), `ScenarioCatalog` with stable ids and run order,
  `ElementCountEstimator`, geometry primitives and tolerances, `BaselinePlanner`,
  `ContentPlanner`, `RoomPlanner`, `CleanupPlanner`, `RunIdentity`.
- **Revit edge**: `DocumentSafetyGuard`, `TransactionCoordinator` (one group per run, one
  transaction per scenario, assimilated into a single Undo step), `FailureCapture`
  (`IFailuresPreprocessor`: dismisses and records expected warnings, records the rest, rolls a
  scenario back on error-level failures), `TypeResolver` (discovers wall/floor/family/view/text/
  material types with documented fallbacks), `ElementFactory` (the only place elements are
  created; registers and tags every one; name and sheet-number retries), `UnitConversion`
  (millimetres → internal feet), `GeneratedElementRegistry` + `GeneratorSchema` (Extensible
  Storage schemas `CrappyGeneratedElement` and `CrappyGenerationRun`), `RunStore` (per-run
  `DataStorage` record with settings, report and element ids), `GenerationRunner` (including dry
  run), `CleanupRunner`, `GenerationContext`.
- **Scenarios**: `baseline` (levels, plans, grids, walls, floors — required),
  `content-placement`, `rooms`, `documentation`, `datum`, `naming`, `content-types`, `metadata`,
  and the opt-in `warnings` scenario, each toggleable and reported independently; datum layout
  defects are planted by the baseline planner and attributed to `datum`.
- **Cleanup**: deletes only elements that still carry the run's identity (or were recorded as
  untaggable), keeps and reports anything user content depends on, removes the run record last,
  switches away from a generated active view first, one transaction per run.
- **Report**: run id, seed, versions, settings, counts by category, per-scenario outcome,
  intentional defects with element ids, fallbacks, information, expected warnings, unexpected
  failures, cleanup scope; stored in the document and shown after a restart via **View Last
  Report**.
- **Tests**: xunit project on `net8.0` and `net10.0` linking `Core/**/*.cs`.
- **Docs**: `README.md`, `docs/DECISIONS.md`, `docs/ARCHITECTURE.md`, `docs/INSTALL.md`,
  `docs/SCENARIOS.md`, `docs/INTEGRATION-TESTS.md`, `docs/MANUAL-QA.md`.

### Not in this release

- Bundled `.rte` / `.rfa` fixtures (the generator discovers what the document has and reports
  fallbacks).
- Creating a new project document from the add-in (active-document only).
- MEP spaces/zones, dimensions, tag overlaps and other Phase 3 annotation depth.
- An installer or signed assembly (Phase 4).
- Revit 2024 and earlier.
