# Revit Bad Model Generator - Implementation Plan

## 1. Product goal

Build a Revit desktop add-in that generates a small, intentionally low-quality model on demand. The result should look like a project assembled by several teams with weak standards: poor naming, inconsistent documentation, duplicated content, questionable geometry, and incomplete metadata.

The model must remain a valid Revit document. The goal is to create a useful training, QA, and demonstration artifact, not to corrupt a project or make Revit unstable.

Primary use cases:

- Train modelers to recognize common BIM quality problems.
- Demonstrate audit and model-health tools against a controlled sample.
- Reproduce bad-model cases deterministically for testing.
- Generate a fresh practice model without manually creating every defect.

## 2. Recommended MVP

Start with an `IExternalCommand` named **Generate Bad Model** that runs in the active project document and creates one bounded example building:

- 2-3 levels.
- 2-4 simple floor plates.
- 20-60 walls.
- A small number of doors and windows.
- 4-10 rooms, where room-bounding failures are intentional in selected areas.
- 6-12 views and 2-4 sheets.
- A small library of intentionally poor names, types, materials, and parameter values.

The command should expose these settings:

- Random seed, with a generated seed shown in the results.
- Severity: Low, Medium, or High.
- Number of levels and approximate building size.
- Scenario checkboxes.
- Create in the active document or, in a later phase, create a new project document.
- Dry-run/preview mode for counts and expected warnings, if practical.

The default run should complete in a few seconds to a minute and create no more than a few hundred elements. Add hard limits so a careless setting cannot generate an unusably large document.

## 3. Design principles

1. **Bad, but valid:** Prefer poor standards and questionable coordination over invalid API state or document corruption.
2. **Deterministic:** The same seed, Revit version, template, and settings should produce the same scenario choices and approximately the same element layout.
3. **Bounded:** Enforce limits for element count, level count, room count, and transaction duration.
4. **Reversible:** Tag generated content and provide a cleanup command that removes only content created by this add-in.
5. **Explainable:** Every generated defect should have a scenario name and appear in the final report.
6. **Template-aware:** Do not assume a particular office template contains a specific wall, door, window, title block, or view family. Discover available types and use documented fallbacks.
7. **Version-aware:** Compile against one explicit Revit API version first. Add other Revit versions through separate build targets or adapter assemblies rather than guessing at API differences.

## 4. Proposed solution structure

Create a C# class library for the selected Revit version. Keep Revit API calls at the edges so most decisions can be unit tested without Revit.

```text
src/
  CrappyRevitModelGenerator/
    App.cs                         # IExternalApplication and ribbon setup
    Commands/
      GenerateBadModelCommand.cs
      CleanupGeneratedModelCommand.cs
      ShowGenerationReportCommand.cs
    Core/
      GenerationSettings.cs
      GenerationContext.cs
      GenerationReport.cs
      SeededRandom.cs
      ScenarioDefinition.cs
    Revit/
      DocumentSafetyGuard.cs
      TransactionCoordinator.cs
      ElementFactory.cs
      TypeResolver.cs
      GeneratedElementRegistry.cs
      FailureCapture.cs
      UnitConversion.cs
    Scenarios/
      IBadModelScenario.cs
      NamingScenario.cs
      DatumScenario.cs
      GeometryScenario.cs
      ContentScenario.cs
      DocumentationScenario.cs
      MetadataScenario.cs
      CoordinationScenario.cs
    UI/
      GenerateWindow.xaml
      GenerateWindow.xaml.cs
      ReportWindow.xaml
      ReportWindow.xaml.cs
tests/
  CrappyRevitModelGenerator.Tests/
installer/
  CrappyRevitModelGenerator.addin
  README-install.md
```

Suggested scenario contract:

```csharp
public interface IBadModelScenario
{
    string Id { get; }
    string DisplayName { get; }
    ScenarioRisk Risk { get; }
    void Generate(GenerationContext context);
}
```

`GenerationContext` should contain the active `Document`, settings, seeded random generator, element registry, type resolver, report, and references to the baseline levels and elements created by earlier scenarios.

## 5. Revit add-in shell

### 5.1 Application and ribbon

- Implement `IExternalApplication.OnStartup`.
- Create a ribbon tab/panel with buttons for:
  - Generate Bad Model.
  - Clean Generated Model.
  - View Last Report.
- Implement `OnShutdown` without leaving subscriptions or modeless windows alive.
- Add an `.addin` manifest containing the assembly path, full class names, a stable `AddInId`, and vendor information.

### 5.2 Command behavior

`GenerateBadModelCommand` should:

1. Check that a project document is active and not read-only.
2. Warn when the active document is a central/workshared project unless the user explicitly enables that mode.
3. Show settings.
4. Validate all requested limits before opening a transaction.
5. Create a run identifier and a `GenerationContext`.
6. Execute selected scenarios in a known order.
7. Commit successful scenario transactions and record failures without swallowing them.
8. Show a report with created counts, intentionally triggered warnings, skipped items, and cleanup information.

Use a modal settings window for the MVP. If a modeless progress window is added later, all Revit API work must be marshalled through `ExternalEvent`; Revit API calls must not run on a worker thread.

## 6. Generated content identity and cleanup

Every generated element should be traceable to the run that created it.

Preferred approach:

- Define a versioned Extensible Storage schema with fields such as `RunId`, `ScenarioId`, `Seed`, and `GeneratorVersion`.
- Attach the schema entity to generated elements where permitted.
- Store a run summary in a `DataStorage` element so cleanup can find generated elements even after the command ends.
- Keep an in-memory registry during generation for reporting and fast cleanup.

Optional fallback for elements that cannot accept the schema:

- Store their `ElementId` in the run registry and report them as cleanup exceptions if they no longer exist.
- Do not rely solely on a visible Comments value, because that modifies user-facing project data and is easy to overwrite.

`CleanupGeneratedModelCommand` should:

- Ask for confirmation and display the run(s) selected for removal.
- Delete only elements recorded by the registry/schema.
- Remove generated `DataStorage` records after successful deletion.
- Report elements that Revit refuses to delete because they are referenced by user-created content.
- Never delete the entire document, all views, or all unused types as a shortcut.

## 7. Scenario catalog

Implement each scenario behind an interface and allow scenarios to be enabled independently. The following catalog provides the initial backlog.

### 7.1 Poor naming and identity

Create names that are legal in Revit but obviously violate reasonable project standards.

- Views named `View 1`, `Copy of Copy`, `NEW`, `OLD`, `Use This One`, `Section maybe`, and `3D - FINAL - FINAL2`.
- Mixed capitalization, inconsistent separators, trailing spaces where Revit permits them, and unexplained abbreviations.
- Similar names for unrelated views, families, types, materials, and sheets.
- Sheet numbers that mix `A101`, `A-102`, `1`, `PLAN-03`, and blank or placeholder-like values.
- Level names such as `L1`, `Level 2`, `Mezz`, `Top-ish`, and `Roof?`, with inconsistent numbering.
- Grid names that mix letters, numbers, and phrases such as `Grid 1`, `A`, `2A`, and `Existing?`.
- Room names with typos, inconsistent casing, and vague values such as `Office`, `Office 2`, `Open`, and `Misc`.
- Type names that differ only by punctuation or an accidental suffix.

Do not attempt to create illegal duplicate names. If Revit rejects a proposed name, add a legal suffix while retaining the bad naming pattern.

### 7.2 Datum and layout problems

- Levels at slightly inconsistent elevations with an unnecessary intermediate level.
- Levels in an order that does not match the naming convention.
- Grids that do not align with structural or architectural intent.
- Grids with inconsistent extents and bubbles shown on only some ends.
- Nearly coincident grids or levels, but separated enough to remain valid.
- Walls that are almost, but not quite, aligned.
- Wall corners with tiny gaps, overlaps, or inconsistent joins.
- Short walls that terminate just outside a room boundary.
- A few walls using a different thickness or location line without a visible reason.
- Floor boundaries with small jogs, unnecessary segments, and inconsistent offsets.

Use minimum-distance checks before creating geometry so the generator does not produce zero-length curves or self-intersecting profiles.

### 7.3 Inconsistent geometry and coordination

- Walls offset from the floor footprint by small random distances.
- Door and window instances too close to wall ends or too close to one another.
- Doors with inconsistent handing and orientation.
- Windows at several unrelated sill heights.
- A stair or opening that does not line up cleanly with the floor above, if the required types are available.
- A few elements extending beyond the nominal footprint.
- One or two intentionally misplaced components documented in the report.
- Inconsistent wall type selection across otherwise similar spaces.

Keep this scenario conservative. Prefer valid placements that look poorly coordinated over placements that create unrecoverable failures.

### 7.4 Views and documentation

- Duplicate or near-duplicate plan views at different scales.
- Views with inappropriate scales or detail levels for their purpose.
- Views assigned to the wrong discipline where the API permits it.
- A mix of uncropped, excessively cropped, and inconsistent crop regions.
- Views without view templates, plus one or two views using a template that does not match their purpose.
- Elevations and sections placed away from the model or with inconsistent extents.
- Empty drafting views and empty sheets.
- Plans placed on sheets with misleading titles or sheet numbers.
- Viewports placed too close together or arranged with inconsistent alignment.
- Placeholder text notes such as `TBD`, `CHECK`, `???`, and `REMOVE BEFORE ISSUE`.
- A few missing tags or dimensions, plus a few annotations that overlap.

Do not create invalid view relationships. For example, create actual duplicate views using supported duplication options, then give them bad names and settings rather than fabricating unsupported references.

### 7.5 Families, types, materials, and content

- Load a small set of generic model, furniture, door, and window content with poor family/type names.
- Create several near-duplicate types with unclear suffixes such as `-new`, `_2`, `copy`, and `final`.
- Leave some loaded types unused.
- Mix standard and custom content without a naming convention.
- Use a few materials with names like `New Mat`, `Material 1`, `Gray-ish`, and `DO NOT USE`.
- Create near-duplicate materials with slightly different colors or render appearances.
- Apply inconsistent material assignments to similar walls, floors, and furniture.
- Leave some family instance comments blank and others filled with personal shorthand.

Family names may not be safely renameable in every project context. Prefer shipping a tiny set of generated or curated `.rfa` fixtures with deliberately poor names and load them only when needed. Use discovered project content as a fallback, and record which content path was used.

### 7.6 Rooms and spatial data

- Unplaced rooms with valid but confusing numbers and names.
- Rooms with duplicate-looking numbering patterns such as `101`, `101A`, and `101-old`.
- Unbounded or partially bounded areas caused by small wall gaps.
- Room separation lines used where walls would have been sufficient.
- Room tags omitted from some rooms and placed awkwardly in others.
- Spaces or zones with inconsistent names when the project supports MEP content.
- Area boundaries that do not follow the architectural footprint.

Create rooms only after the basic walls exist, and catch room-placement failures as explicit report entries rather than silently ignoring them.

### 7.7 Metadata and parameter quality

Populate writable parameters with poor but valid values:

- Inconsistent `Mark`, `Comments`, `Description`, `Type Mark`, and manufacturer values.
- Blank values in fields expected by a project standard.
- Typos, mixed casing, stale dates, and inconsistent abbreviations.
- Repeated marks where uniqueness would normally be expected.
- Unclear phase, design option, or workset assignments when those features are available.
- Instance values that conflict with the type name.

Never overwrite an existing user value unless the user has explicitly selected a new document or an explicit overwrite mode. Apply bad metadata only to elements created by the generator.

### 7.8 Optional high-risk warnings

Add these only behind a separate **Generate Warnings** toggle:

- Slightly overlapping walls or floors likely to produce join/overlap warnings.
- Unconnected or undersized elements that remain valid Revit elements.
- Duplicate instances in locations where Revit can still commit the transaction.
- Deliberately unused views, types, and materials.

Do not automatically dismiss all Revit failures. Install an `IFailuresPreprocessor` that captures failure messages, deletes only explicitly approved expected warnings, and rolls back a scenario when an error-level failure occurs.

## 8. Generation order

Run scenarios in this order so later choices can reference existing elements:

1. Validate document, settings, template availability, and safety mode.
2. Create the baseline levels and grids.
3. Create footprint walls, floors, and roofs if enabled.
4. Resolve and place doors, windows, and a small amount of furniture.
5. Add rooms, room separation lines, tags, and spatial metadata.
6. Create views, duplicate views, sections, elevations, sheets, and viewports.
7. Add poor names, parameters, materials, and documentation defects.
8. Add optional coordination warnings.
9. Attach identity metadata and finalize the report.

Use a `TransactionGroup` for the run and a child `Transaction` per scenario. If a scenario fails, roll back that child transaction, record the exception and scenario, and continue only when the remaining scenarios can safely proceed. If a fatal safety check fails, roll back the entire group.

## 9. Core implementation details

### 9.1 Type discovery

Create a `TypeResolver` that:

- Finds usable `WallType`, `FloorType`, `RoofType`, `FamilySymbol`, `TextNoteType`, `DimensionType`, `ViewFamilyType`, and title block symbols.
- Activates symbols before placing instances when required.
- Chooses a documented fallback when a preferred type is not present.
- Records fallback choices in the report.
- Avoids hard-coded element ids because ids differ by template and document.

### 9.2 Units and geometry

- Store design dimensions in a clear project unit such as millimeters or feet in the core settings.
- Convert to Revit internal units with `UnitUtils` at the Revit boundary.
- Reject zero-length, self-intersecting, or too-short curves before calling element creation APIs.
- Use tolerances for near-coincident checks and keep those tolerances in one configuration object.
- Keep the footprint simple enough that the bad choices remain understandable.

### 9.3 Randomness

Implement a wrapper around a seeded random generator:

- Use named random streams or deterministic sequence allocation per scenario so adding a new scenario does not completely change all existing scenarios.
- Select from fixed bad-name lists instead of generating arbitrary unreadable strings.
- Persist the seed and settings in the report record.
- Never use random values to bypass Revit validation.

### 9.4 Failure handling

Capture:

- Scenario id.
- API operation being attempted.
- Failure definition id and severity where available.
- Element ids involved.
- Whether the transaction was committed or rolled back.

Surface actionable failures in the results window. Avoid broad catches that convert a failed generation into a success message.

### 9.5 Reporting

The result should show:

- Run id, generator version, Revit version, seed, and settings.
- Counts by category: levels, grids, walls, floors, doors, windows, rooms, views, sheets, families/types, materials, and annotations.
- Scenarios applied, skipped, or rolled back.
- Expected warnings and unexpected failures.
- Cleanup scope and any elements that could not be tagged or removed.

Also write a small JSON or text report beside the model only when the user explicitly chooses an export location. Do not silently write files next to a production project.

## 10. UI and safety behavior

The settings window should group controls into:

- Run setup: seed, severity, target document, limits.
- Model content: levels, footprint, doors/windows, rooms, furniture.
- Bad-choice scenarios: naming, geometry, documentation, content, metadata, warnings.
- Safety: workshared-document confirmation, maximum element count, cleanup behavior.

Safety rules:

- Default to a new blank or disposable project workflow in documentation, while the MVP may operate on the active document after an explicit confirmation.
- Disable generation for read-only documents.
- Warn before running in a central model or a document with unsaved user changes.
- Show the approximate element count before generation.
- Allow the user to cancel before the transaction starts.
- Do not auto-save, sync, or publish.
- Keep generated content identifiable and removable.

## 11. Testing strategy

### Unit tests without Revit

- Same seed produces the same name/type/choice sequence.
- Different seeds produce variation within configured bounds.
- Name sanitization produces legal, unique Revit names while retaining bad patterns.
- Geometry utilities reject invalid curves and maintain configured tolerances.
- Settings validation rejects negative counts, excessive limits, and incompatible options.
- Scenario registry reports all scenarios and their risk levels.

### Revit integration tests

Use a disposable test project or a Revit test harness for the target version:

- Generate a low-severity model in an empty project.
- Generate each scenario independently.
- Generate the same seed twice in clean documents and compare counts, names, and key coordinates.
- Verify the document opens after generation and after save/reopen.
- Verify cleanup removes generated elements and preserves a user-created control element.
- Verify missing content types produce a report entry and do not crash the run.
- Verify an intentional scenario rollback does not leave orphaned registry entries.

### Manual visual QA

Inspect the generated model in 3D, plan, section, elevation, and sheet views. Confirm that:

- The model visibly contains bad choices without being incomprehensible.
- The UI report matches what is in the document.
- View names, sheet names, room data, and tags are easy to audit.
- Cleanup works from a reopened document.
- Low, medium, and high severity feel meaningfully different.

## 12. Delivery phases

### Phase 0 - Decisions and fixture setup

- Choose the first Revit version and .NET target.
- Obtain a minimal project template and any permitted `.rfa` fixtures.
- Decide whether the first release is active-document-only or supports a disposable new-document workflow.
- Define the generator schema GUID, version, and default limits.

**Exit criteria:** A clean build target and a documented local install path exist.

### Phase 1 - Add-in shell and baseline model

- Create the solution and `.addin` manifest.
- Add the ribbon and a command that validates the active document.
- Implement settings, seeded randomness, type discovery, units, and transaction coordination.
- Generate levels, grids, a simple footprint, and a report.

**Exit criteria:** A low-level command creates a valid small model in a disposable project and reports counts.

### Phase 2 - High-value bad choices

- Implement naming, datum, geometry, rooms, views, sheets, and metadata scenarios.
- Add Extensible Storage tagging and cleanup.
- Add scenario-level rollback and expected-warning capture.

**Exit criteria:** At least six independent scenarios can be enabled together, disabled individually, reported, and cleaned up.

### Phase 3 - Content and documentation depth

- Add fixture families, near-duplicate types/materials, more annotation defects, and optional MEP spaces/zones.
- Add severity profiles and stable random streams per scenario.
- Add richer reports and optional export.

**Exit criteria:** The model is useful for BIM training and audit demonstrations across several disciplines.

### Phase 4 - Hardening and distribution

- Run repeated generation/cleanup cycles.
- Test save/reopen, workshared warnings, missing template content, and partial failures.
- Add telemetry only if explicitly required and approved; default to no external reporting.
- Package per-Revit-version builds with a clear install/uninstall guide.

**Exit criteria:** A user can install, generate, inspect, clean up, and uninstall without manual project repair.

## 13. Acceptance checklist

- [ ] The add-in loads through a valid Revit `.addin` manifest.
- [ ] The command refuses read-only documents and clearly warns about workshared documents.
- [ ] A seed is displayed and saved with the run metadata.
- [ ] The generated model contains at least five visibly different bad-modeling categories.
- [ ] The same seed produces repeatable choices within the same template and Revit version.
- [ ] Scenario failures are reported and do not silently look successful.
- [ ] Generated elements are identifiable after save/reopen.
- [ ] Cleanup removes only generated content and reports exceptions.
- [ ] No unbounded element generation, auto-sync, auto-save, or destructive project-wide cleanup occurs.
- [ ] The project includes unit tests, an integration test procedure, and a manual visual QA checklist.

## 14. Suggested first implementation slice

Implement these files first:

1. `App.cs` and `GenerateBadModelCommand.cs`.
2. `GenerationSettings.cs`, `GenerationContext.cs`, `GenerationReport.cs`, and `SeededRandom.cs`.
3. `DocumentSafetyGuard.cs`, `TransactionCoordinator.cs`, `TypeResolver.cs`, and `ElementFactory.cs`.
4. `BaselineModelScenario.cs`, `NamingScenario.cs`, and `DocumentationScenario.cs`.
5. `GeneratedElementRegistry.cs` and `CleanupGeneratedModelCommand.cs`.
6. A minimal WPF settings/report UI.
7. Unit tests for settings validation, deterministic random choices, name generation, and cleanup bookkeeping.

This slice proves the core loop: configure -> generate -> tag -> report -> clean up. Geometry and content variety can then grow as independent scenarios without redesigning the add-in shell.
