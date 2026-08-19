# Scenarios

One section per scenario id, in the order they run (`Core/ScenarioCatalog.cs`). For each: what
it is, what defects it plants and where those come from (plan section 7, the planners in
`Core/Planning/`, the catalog description), how it scales with severity (`Core/SeverityProfile.cs`
fields, listed as Low / Medium / High), which Revit warnings it is expected to raise and how they
are handled (`Revit/FailureCapture.cs`), and what its lines in the report look like.

**Verification status.** The baseline scenario and the three planners (`BaselinePlanner`,
`ContentPlanner`, `RoomPlanner`) are implemented and the planners are covered by the Revit-free
unit tests. The remaining scenario classes are implemented against the descriptions below.
**Nothing here has yet been exercised in a running Revit session**; until the procedure in
`INTEGRATION-TESTS.md` has been run, treat every "expected" statement about Revit's reaction as
unverified. Where a behaviour is decided by a planner it is exact; where it is decided by the
Revit-side class it is described from the plan and the catalog and marked *(Revit side)*.

## Common rules

- Every scenario runs in its own transaction, in catalog order, inside one transaction group.
  A scenario that throws or hits an error-level failure is rolled back and the run continues;
  only the baseline is required.
- Scenarios create elements only through `ElementFactory`, which registers each one (identity
  entity + run record) and counts it in the report.
- Every intentional defect is a report note of kind `Defect`, attributed to the scenario that
  asked for it: `[<scenario-id>] <message>  ids: <up to 12 element ids>,…`. Fallbacks (missing
  content, missing view types) are notes of kind `Fallback`. Summaries are used where a defect
  applies to many elements (one line per kind with the count and up to ~12 ids).
- Randomness comes only from named streams, `"<scenario-id>/<purpose>"` (planner stream names
  are constants on the planner class). Same seed + same settings + same template + same Revit
  version → same choices.
- Names go through `NameSanitizer`; if Revit rejects a name (already taken), the next candidate
  is the same bad name with an equally bad suffix (` 2`, ` (2)`, ` - Copy`, `_2`, …), never a
  clean fallback.

### Expected warnings and how they are handled

`FailureCapture` is installed as the failures preprocessor on every scenario transaction. For
each failure Revit reports while committing:

| Failure | Handling |
|---|---|
| Warning whose definition id is on the expected list below, or whose text matches an expected pattern | **Dismissed** (`DeleteWarning`) so no dialog interrupts the run; recorded under **Expected warnings (dismissed)** with scenario id, definition GUID, message and element ids |
| Any other warning | Recorded under **Unexpected failures** and left for Revit to show (unless *Suppress all warning dialogs* is on, in which case it is dismissed and recorded like an expected one) |
| Error or document-corruption severity | Recorded under **Unexpected failures** with `(rolled back)`; the preprocessor returns `ProceedWithRollBack`, the scenario's transaction is rolled back and its registrations discarded; the run continues with the next scenario |

Expected list (`BuiltInFailures`, verified present in the 2025/2026/2027 API):
`OverlapFailures.WallsOverlap`, `OverlapFailures.DuplicateInstances`,
`OverlapFailures.WallRoomSeparationOverlap`, `OverlapFailures.RoomSeparationLinesOverlap`,
`OverlapFailures.FloorsOverlap`, `RoomFailures.RoomNotEnclosed`, `RoomFailures.RoomNotEnclosedRooms`,
`RoomFailures.RoomsInSameRegion`, `RoomFailures.RoomsInSameRegionRooms`,
`RoomFailures.RoomTagNotInRoom`, `RoomFailures.RoomTagNotInRoomToRoom`, `RoomFailures.RoomTooShort`,
`GeneralFailures.DuplicateValue`. Text patterns (English UI): "overlap", "not in a properly
enclosed region", "identical instances", "duplicate", "same enclosed region", "slightly off axis",
"off axis", "not enclosed", "insert conflicts", "can't keep elements joined", "cannot keep
elements joined", "highlighted walls are joined but do not intersect".

Dismissing a warning at commit time does not stop Revit from re-raising a *persistent* condition
(a room that is still not enclosed) at a later regeneration; those conditions appear in
Manage > Warnings afterwards. That is intended — they are the point of the model.

---

## `baseline` — Baseline model

| | |
|---|---|
| Risk | Low |
| Default | on; **required** (cannot be disabled; failure aborts the run) |
| Order | 10 |
| Relies on | nothing. Fills `context.Baseline`, `Levels`, `PlanViews`, `Grids`, `Walls`, `Floors` |
| Planner | `BaselinePlanner` — streams `baseline/levels`, `baseline/layout`, `baseline/walls`, `baseline/floors`, `baseline/grids` |
| Needs from the document | at least one basic wall type and a floor-plan view family type; a floor type if floors are on (otherwise a fallback and no floors) |

**What it creates.** A rectangular footprint centred on the origin (default 18 000 × 12 000 mm),
`LevelCount` levels at `LevelHeightMm` spacing, one floor plan per buildable level, grids at the
exterior and partition lines (numbers one way, letters the other), exterior walls, two corridor
walls when the depth allows a corridor (≥ 9 000 mm), transverse partitions front and back, and a
floor per buildable level. Cell width comes from the profile, so High packs more rooms into the
same footprint. Type choices come from `TypeResolver` (exterior / interior / alternate interior
wall types by name and thickness, "Generic" floor type first) and are written to the report as
information lines.

**Defects.** None of its own. When the `datum` scenario is enabled, `BaselinePlanner` plants the
layout defects listed under `datum` below and the baseline creates them; the report attributes
them to `datum`. When `datum` is off the baseline is clean.

**Severity.** `CellWidthMm` 4 800 / 4 200 / 3 400; `CorridorWidthMinMm–MaxMm` 1 800 / 1 650–1 950 /
1 500–2 100. Wall count is capped at `GenerationLimits.MaxWalls` (120) by trimming partitions.

**Expected warnings.** None from a clean baseline. With datum defects: possibly "walls slightly
off axis" and "can't keep elements joined" text matches — dismissed and recorded.

**Report.** `[baseline] Baseline: 3 level(s), 9 grid(s), 30/30 wall(s), 3 floor(s), 3 plan view(s), footprint 18000 x 12000 mm.`
Individual creation failures (a wall type Revit refuses at one spot) are recorded as exceptions
without rolling the scenario back; only "no walls at all" is fatal.

---

## `content-placement` — Doors, windows and furniture

| | |
|---|---|
| Risk | Medium |
| Default | on |
| Order | 20 |
| Relies on | baseline walls and levels. Fills `Openings`, `Furniture`; sets `context.Content` |
| Planner | `ContentPlanner` — streams `content/doors`, `content/windows`, `content/furniture` |
| Needs from the document | door, window and furniture families (`TypeResolver.DoorSymbol`, `WindowSymbol`, `FurniturePicks`); each missing kind is a fallback and that content is skipped |
| Settings | `CreateDoorsAndWindows`, `CreateFurniture` |

**Defects (planner, exact).**

- One door per non-corridor cell in the corridor wall (or the front exterior wall when there is
  no corridor). Up to `DoorsNearWallEnd` doors are jammed 700 mm centre-to-junction (edge ≈ 250 mm
  from the partition) — *too close to the wall end*; the rest are jittered around the cell centre.
- Doors flipped in hand and/or facing with probability `DoorFlipProbability` — *inconsistent
  handing and orientation*.
- Windows along every exterior wall at roughly `WindowSpacingMm`, jittered; up to
  `WindowPairsTooClose` extra windows placed 1 400 mm from a neighbour — *too close to one
  another*. Sill heights: the typical 900 mm plus `SillHeightVarieties − 1` odd values drawn from
  450/600/750/1 050/1 200/1 350 mm, applied to ~40 % of windows — *several unrelated sill heights*.
- Furniture: 0–`FurniturePerCellMax` pieces per cell; up to `FurnitureRotatedOddly` rotated
  15–75°; up to `FurnitureOnWall` centred on the cell's wall line; up to
  `FurnitureOutsideFootprint` pieces 300–900 mm outside the building — *misplaced components,
  elements beyond the footprint*.

Spacing rules keep every placement valid: openings never overlap each other and never straddle a
wall end, however "too close" they look. *(Revit side)* the scenario resolves families, activates
symbols, places hosted instances at the planned distance along the host wall, applies flips
(after a `Regenerate`), and sets sill height; individual placement failures are recorded and
skipped.

**Severity.** `WindowSpacingMm` 5 200 / 4 600 / 3 800; `WindowPairsTooClose` 0 / 1 / 2;
`DoorsNearWallEnd` 1 / 2 / 3; `SillHeightVarieties` 2 / 3 / 5; `DoorFlipProbability` 0.2 / 0.35 /
0.5; `FurniturePerCellMax` 1 / 2 / 3; `FurnitureOutsideFootprint` 0 / 1 / 2;
`FurnitureRotatedOddly` 1 / 1 / 2; `FurnitureOnWall` 0 / 1 / 2.

**Expected warnings.** Normally none. A window pair or a door near a junction may produce an
"insert conflicts" text match — dismissed and recorded. If a family kind is missing, a
`[type-resolver]` fallback line and no instances of that kind.

**Report.**
`[content-placement] Door on level 1 at x≈-4350 sits 250 mm from a wall junction (too close to the wall end).`
`[content-placement] 2 door(s) have inconsistent handing/orientation compared with their neighbours.`
`[content-placement] Two windows on level 1 are only 1400 mm apart centre-to-centre while the rest are spaced ~4600 mm.`
`[content-placement] 4 window(s) sit at sill heights other than the typical 900 mm (600, 1200 mm) for no reason.`
`[content-placement] Furniture on level 2 at (1234, -2345) is rotated 37° for no reason.`
`[content-placement] Furniture on level 1 at (-9000, 1200) is centred on a wall (intentionally misplaced component).`
`[content-placement] Furniture on level 1 at (2400, -6650) is 650 mm outside the building footprint.`

---

## `rooms` — Rooms and spatial data

| | |
|---|---|
| Risk | Medium |
| Default | on |
| Order | 30 |
| Relies on | baseline walls (and openings, if any, already exist). Fills `RoomElements`, `RoomTags`, `SeparationLines`; sets `context.Rooms` |
| Planner | `RoomPlanner` — streams `rooms/rooms`, `rooms/tags`, `rooms/separation` |
| Needs from the document | a phase (for unplaced rooms); plan views from the baseline (for tags and separation lines) |
| Settings | `CreateRooms` |

**Defects (planner, exact).**

- `RoomsMin`–`RoomsMax` rooms (capped at 24), lowest levels first, front cells before back cells,
  placed at cell centres. Rooms in a cell whose partition has a planted corner gap are
  *partially bounded* — they share a region with the neighbour.
- `DuplicateRoomsInCell` extra rooms placed in a cell that already has one — *multiple rooms in
  the same region*.
- One room in the corridor when `RoomInCorridor` (named `Corridoor`, `Hall`, `CORR.` … when
  naming is on).
- `UnplacedRooms` rooms created without a location — *unplaced rooms with valid but confusing
  numbers*; they show as *Not Placed* in a schedule.
- Names and numbers: when the `naming` scenario is enabled they are drawn from
  `BadNames.RoomNames` / `RoomNumbers` (`Office`, `Offce`, `Misc`, `TBD`; `101`, `101A`,
  `101-old`, `1O3`, exact duplicates allowed); otherwise clean (`Office`, `Meeting Room`; `101`,
  `102`, …).
- Tags: `UntaggedRoomFraction` of placed rooms get no tag; `AwkwardTagFraction` of the tagged
  ones get their tag pushed to within 150 mm of a wall — *tags omitted from some rooms and placed
  awkwardly in others*.
- Separation lines: one across the corridor (legitimate), then `SeparationLines − 1` splitting a
  cell in half — *room separation lines used where walls would have been sufficient*.

*(Revit side)* rooms are created after walls exist; each `NewRoom` / `NewRoomTag` /
`NewRoomBoundaryLines` call is wrapped so a placement failure becomes a report exception, not a
silent skip; a `Regenerate` precedes tagging a just-created room.

**Severity.** `RoomsMin–Max` 4–6 / 6–8 / 8–10; `UnplacedRooms` 1 / 1 / 2; `DuplicateRoomsInCell`
0 / 1 / 2; `SeparationLines` 1 / 2 / 3; `UntaggedRoomFraction` 0.2 / 0.25 / 0.35;
`AwkwardTagFraction` 0.2 / 0.3 / 0.4; `RoomInCorridor` no / yes / yes.

**Expected warnings** (all dismissed and recorded): *Room is not in a properly enclosed region*
(`RoomNotEnclosed`, from corner gaps or a stub wall); *Multiple Rooms are in the same enclosed
region* (`RoomsInSameRegion`); *Room Tag is outside of its Room* (`RoomTagNotInRoom`) if an
awkward tag lands outside; *Elements have duplicate 'Number' values* (`DuplicateValue`) when
naming is on; *A wall and a room separation line overlap* / *room separation lines overlap*
(`WallRoomSeparationOverlap`, `RoomSeparationLinesOverlap`) if a line lands on a wall.

**Report.**
`[rooms] Room 101 'Office' is placed in a cell whose partition has a gap, so it shares its region with a neighbour (partially bounded area).`
`[rooms] Room 1O3 'office' is a second room in the same enclosed region as another (Revit will warn: multiple rooms in the same region).`
`[rooms] Room TBD 'Not Sure' exists but is not placed (shows in schedules as 'Not Placed').`
`[rooms] 2 placed room(s) have no room tag.`
`[rooms] 2 room tag(s) are placed awkwardly against a wall instead of at the room centre.`
`[rooms] A room separation line splits the cell at (-6750, -3900) on level 1 where a wall would have been sufficient.`

---

## `documentation` — Views and sheets

| | |
|---|---|
| Risk | Low |
| Default | on |
| Order | 40 |
| Relies on | baseline plan views and levels; rooms if present. Fills `Views`, `Sheets`, `Viewports`, `TextNotes` |
| Planner | none — decided on the Revit side from `SeverityProfile` and what `TypeResolver` finds |
| Needs from the document | view family types for section / elevation / 3D / drafting (each missing one is a fallback and those views are skipped); a title block (missing → sheets without a title block, reported); a text note type (missing → no notes, reported) |

**Defects** *(Revit side, plan section 7.4)*.

- `DuplicatePlansPerLevel` duplicates of each baseline plan (real `View.Duplicate`, never
  fabricated references), then given odd scales (`OddScaleFraction`), inconsistent crops
  (`OddCropFraction`: uncropped / over-cropped / mismatched), the wrong discipline
  (`WrongDisciplineFraction`), and no view template — plus one or two with a template that does
  not match their purpose when the document has templates.
- `Sections` sections and `Elevations` elevations, placed away from the model or with
  inconsistent extents; `ThreeDViews` isometrics; `DraftingViews` empty drafting views.
- `Sheets` sheets, `EmptySheets` of them left empty; the rest carry viewports whose sheet
  numbers and names do not describe them (`A101`, `A-102`, `1`, `PLAN-03`; `Floor Plan`,
  `Do Not Print`), viewports arranged unevenly / too close together.
- `TextNotes` placeholder notes from `BadNames.TextNotes` (`TBD`, `CHECK`, `???`,
  `REMOVE BEFORE ISSUE`, `V.I.F.`) in views and on sheets.
- Names come from `BadNames.ViewNames` / `DuplicateViewSuffixes` when the `naming` scenario is
  on; the naming scenario also renames what this scenario made.

Estimated element counts (used by the dialog): views = buildable levels × (1 +
`DuplicatePlansPerLevel`) + `Sections` + `Elevations` + `ThreeDViews` + `DraftingViews`, capped
at 40; sheets = min(`Sheets`, 10); viewports ≤ (sheets − `EmptySheets`) × 3; text notes =
`TextNotes`.

**Severity.** `DuplicatePlansPerLevel` 1 / 1 / 2; `Sections` 1 / 2 / 3; `Elevations` 1 / 2 / 3;
`ThreeDViews` 1 / 2 / 3; `DraftingViews` 1 / 1 / 2; `Sheets` 2 / 3 / 4; `EmptySheets` 0 / 1 / 1;
`TextNotes` 3 / 5 / 8; `WrongDisciplineFraction` 0.15 / 0.25 / 0.35; `OddScaleFraction` 0.25 /
0.35 / 0.5; `OddCropFraction` 0.25 / 0.35 / 0.5.

**Expected warnings.** None. Duplicate sheet numbers and view names are rejected by Revit as
exceptions, not warnings; the factory walks the bad-suffix candidates until one is accepted.

**Report.** One summary line per kind, e.g. `[documentation] 3 duplicate plan view(s) at scales
1:500 / 1:20 with no view template.  ids: …`, `[documentation] Sheet 'PLAN-03' is empty.  ids: …`,
`[documentation] 5 placeholder text note(s) (TBD, CHECK, ???).  ids: …`. Missing view types
appear as `[type-resolver] The document has no Drafting view family type; those views are skipped.`

---

## `datum` — Datum and layout tweaks

| | |
|---|---|
| Risk | Low |
| Default | on |
| Order | 50 |
| Relies on | grids, plan views (bubble visibility needs a view), walls |
| Planner | `BaselinePlanner` plants the layout defects when this scenario is enabled; the baseline creates them; this scenario does the element-level tweaks that need existing elements |
| Needs from the document | nothing beyond the baseline |

**Defects (planner, exact — created by the baseline, attributed here).**

- Levels: every level above the first jittered by up to `LevelJitterMm` (≤ 40 mm, the tolerance
  cap); one level pushed a further `LevelOopsMinMm`–`LevelOopsMaxMm` — *slightly inconsistent
  elevations*; when `IntermediateLevel`, an unnecessary `Mezzanine` at 38–48 % of the first storey
  with no floor, walls or plan view — *unnecessary intermediate level*.
- Walls, per buildable level: `MisalignedWallsPerLevel` partitions shifted 12–45 mm off their
  line — *almost, but not quite, aligned*; `CornerGapsPerLevel` partitions stopping 15–60 mm short
  of the corridor wall — *tiny gaps* (these break room enclosure); `StubWallsPerLevel` 600–900 mm
  stubs continuing a partition into the corridor — *short walls terminating just outside a room
  boundary*; `AlternateTypeWallsPerLevel` partitions on the alternate wall type — *different
  thickness without a visible reason*; `OddLocationLineWallsPerLevel` walls on a non-default
  location line; `UnattachedWallsPerLevel` walls left unattached with a height 50–110 mm short or
  80–160 mm past the level above; `DisallowedJoinsPerLevel` walls with the join disallowed at one
  end — *inconsistent joins*; when `ExteriorOverrun`, the front exterior wall extended 150–300 mm
  past the corner — *element beyond the nominal footprint*.
- Floors: when `FloorOffset`, one floor shifted 20–50 mm in x and y from the wall footprint;
  when `FloorInset`, another inset 80–140 mm — *inconsistent offsets*; when `FloorJog`, one floor
  with a 300–600 mm notch at a corner — *unnecessary segments*. Any loop that fails
  `IsSimpleClosedLoop` falls back to the plain rectangle.
- Grids: when `GridExtentChaos`, every grid overhangs by a different 800–3 500 mm;
  `OneEndBubbleGrids` grids with the bubble at one end only; `MisalignedGrids` interior grids
  shifted 100–300 mm off the wall they align with; when `NearCoincidentGrid`, one grid duplicated
  ≥ 60 mm away as `<name>.1` — *nearly coincident grids*.

Minimum-distance checks run before anything reaches Revit: no wall shorter than 400 mm, no
curve shorter than 100 mm, no gap under 15 mm, no near-coincident pair under 60 mm.

*(Revit side)* `DatumScenario` applies what needs an existing element and a view: hiding the
bubble at one end of the planned grids in the baseline plan views is the main one. The
location-line and disallowed-join choices in the plan are applied by `ElementFactory.CreateWall`
when the wall is created.

**Severity.** `LevelJitterMm` 12 / 25 / 40; `LevelOopsMinMm–MaxMm` 0 / 60–120 / 100–180;
`IntermediateLevel` no / yes / yes; `MisalignedWallsPerLevel` 1 / 2 / 3; `CornerGapsPerLevel` 0 / 1 / 2;
`StubWallsPerLevel` 0 / 1 / 2; `AlternateTypeWallsPerLevel` 1 / 2 / 3; `OddLocationLineWallsPerLevel`
0 / 1 / 2; `UnattachedWallsPerLevel` 1 / 2 / 3; `DisallowedJoinsPerLevel` 0 / 1 / 2;
`ExteriorOverrun` no / no / yes; `FloorOffset` no / yes / yes; `FloorJog` no / yes / yes;
`FloorInset` yes / yes / yes; `GridExtentChaos` no / yes / yes; `OneEndBubbleGrids` 1 / 2 / 3;
`MisalignedGrids` 0 / 1 / 2; `NearCoincidentGrid` no / yes / yes.

**Expected warnings.** Possibly "walls slightly off axis" (misaligned partitions are still
orthogonal, so usually none) and "can't keep elements joined" text matches — dismissed and
recorded. The corner gaps make rooms *not enclosed*; that warning is raised in the `rooms`
scenario's transaction and attributed there.

**Report** (attributed to `datum`, with wall ids where the planner recorded them):
`[datum] Level 2 elevation is 18 mm off the 3500 mm module (slightly inconsistent level elevations).`
`[datum] Level 3 is a further 87 mm high with no reason (someone nudged it).`
`[datum] An unnecessary intermediate level with no floor or walls sits between the first two levels.`
`[datum] Level 'Mezzanine' has no plan view associated with it.  ids: …`
`[datum] Level 01: partition at x=-4482 is 18 mm off its grid line (almost aligned).  ids: …`
`[datum] Level 01: partition at x=4500 stops 42 mm short of the corridor wall (tiny corner gap; the rooms either side are no longer separately enclosed).  ids: …`
`[datum] Level 01: a 720 mm stub wall continues the partition at x=0 into the corridor and stops just outside the room boundary.  ids: …`
`[datum] Level 01: partition along x=-4500 (y -6000..-1200) uses a different wall type than its neighbours for no reason.  ids: …`
`[datum] Level 01: Exterior wall along y=-6000 uses location line option 3 while its neighbours use the default.  ids: …`
`[datum] Level 01: Partition wall along x=4500 has an unconnected height 74 mm short of the level above instead of being attached to it.  ids: …`
`[datum] Level 01: Exterior wall along x=9000 has its join disallowed at the end, so the corner does not clean up.  ids: …`
`[datum] Level 01: the front exterior wall runs 220 mm past the corner (element beyond the nominal footprint).  ids: …`
`[datum] Level 02: floor boundary is shifted 30, -40 mm from the wall footprint.`
`[datum] Level 03: floor boundary is inset 110 mm from the wall centrelines while other levels are not (inconsistent offsets).`
`[datum] Level 01: floor boundary has an unexplained 450 mm jog at one corner (unnecessary segments).`
`[datum] Grid extents are inconsistent; every grid overhangs the footprint by a different amount.`
`[datum] Grid 3 shows its bubble at only one end.`
`[datum] Grid 2 is 180 mm off the wall it was meant to align with.`
`[datum] Grid B.1 runs 95 mm from grid B (nearly coincident grids).`

---

## `naming` — Poor naming

| | |
|---|---|
| Risk | Low |
| Default | on |
| Order | 60 |
| Relies on | everything above — renames generated levels, grids, views, sheets, rooms and duplicated types; also switches `RoomPlanner` to bad room names/numbers |
| Planner | none (room names/numbers are chosen by `RoomPlanner` when this scenario is enabled) |
| Names from | `BadNames.LevelNames` / `LevelNameAlternates`, `GridNames`, `ViewNames`, `DuplicateViewSuffixes`, `SheetNumbers`, `SheetNames`, `RoomNames`, `RoomNumbers`, `TypeSuffixes` |

**Defects** *(Revit side, plan section 7.1)*. Only elements the generator created are renamed;
the template's own levels, views, types and materials are never touched.

- Levels: `L1`, `Level 2`, `Mezz`, `Top-ish`, `Roof (maybe)`, `LEVEL 03`, `lvl 4` — an
  inconsistent convention whose apparent order does not match the elevations.
- Grids: `Grid 1`, `A`, `2A`, `Existing (maybe)`, `B.1`, `C-C`, `1'`, `GRID` — letters, numbers
  and phrases mixed.
- Views: `View 1`, `Copy of Copy`, `NEW`, `OLD`, `Use This One`, `Section maybe`,
  `3D - FINAL - FINAL2`, `Level 1 - do not use`, `Plan_02 ` (trailing space), `asdf` — mixed
  capitalisation, inconsistent separators, similar names for unrelated views.
- Sheets: numbers `A101`, `A-102`, `A 103`, `1`, `PLAN-03`, `A1.01`, `TBD`, `-`; names
  `Unnamed`, `Sheet`, `FLOOR PLANS`, `Plans - Copy`, `Do Not Print`.
- Rooms: names with typos, casing drift and vague values; numbers with duplicate-looking
  patterns (`101`, `101A`, `101-old`, `1O3`) — assigned by `RoomPlanner`.
- Types (those the `content-types` scenario duplicated): suffixes that differ only by
  punctuation or an accidental suffix (`-new`, `_2`, ` copy`, ` final`, ` (Do Not Use)`, `.`, ` `).

Illegal duplicate names are never attempted: `TrySetName` / `TrySetSheetNumber` walk the
sanitiser's candidates until Revit accepts one, keeping the bad pattern (`Copy of Copy (2)`).
An element whose name Revit refuses to change at all keeps its name and is skipped silently.

**Severity.** No naming-specific `SeverityProfile` fields; the scenario renames what earlier
scenarios produced, so it scales with them.

**Expected warnings.** None; name rejections are exceptions handled by the factory. Duplicate
room numbers can produce *Elements have duplicate 'Number' values* in the `rooms` transaction.

**Report.** One summary line per kind: `[naming] Renamed 3 level(s): L1, Level 2, Mezz.  ids: …`,
`[naming] Renamed 9 grid(s) …`, `[naming] Renamed 12 view(s) …`, `[naming] Renumbered/renamed 3 sheet(s) …`,
`[naming] Renamed 3 duplicated type(s) …`.

---

## `content-types` — Near-duplicate types and materials

| | |
|---|---|
| Risk | Low |
| Default | on |
| Order | 70 |
| Relies on | walls, floors and materials existing (baseline; the document's materials). Fills `DuplicatedTypes`, `Materials` |
| Planner | none |
| Needs from the document | the wall/floor types the baseline used; at least one material to duplicate (otherwise materials are created from scratch); door/window/furniture symbols for family-type duplicates |

**Defects** *(Revit side, plan section 7.5)*. Duplicates are made from the types the generator
used, never by editing template types in place.

- `DuplicateWallTypes` / `DuplicateFloorTypes` / `DuplicateFamilyTypes` copies with unclear
  suffixes from `BadNames.TypeSuffixes` (`-new`, `_2`, ` copy`, ` final`, ` FINAL2`, ` (1)`,
  ` - Copy`, ` (Do Not Use)`, `_old`, ` v2`); some assigned to a few generated walls/floors/
  instances (*inconsistent type selection across similar spaces*), some left unused.
- `Materials` new materials named from `BadNames.MaterialNames` (`New Mat`, `Material 1`,
  `Gray-ish`, `grayish`, `DO NOT USE`, `Concrete (maybe)`, `MAT_A`, `mat a`), plus
  `NearDuplicateMaterials` duplicates of existing materials with slightly different colours;
  applied inconsistently to the duplicated wall/floor types.
- Caps: `GenerationLimits.MaxDuplicateTypes` (12) and `MaxMaterials` (12).

**Severity.** `DuplicateWallTypes` 1 / 2 / 3; `DuplicateFloorTypes` 0 / 1 / 2;
`DuplicateFamilyTypes` 1 / 2 / 3; `Materials` 3 / 5 / 7; `NearDuplicateMaterials` 1 / 2 / 3.

**Expected warnings.** None. Name clashes are exceptions handled by
`ElementFactory.DuplicateType` / `CreateMaterial` / `DuplicateMaterial`, which try the next bad
candidate; a null result (every candidate rejected) is recorded as a fallback.

**Report.** `[content-types] Duplicated wall type 'Generic - 200mm' as 'Generic - 200mm -new' (unused).  ids: …`,
`[content-types] Created 5 material(s): New Mat, Material 1, DO NOT USE, …  ids: …`,
`[content-types] Material 'Concrete (maybe)' is a near-duplicate of 'Concrete' with a slightly different colour.  ids: …`.

---

## `metadata` — Metadata and parameters

| | |
|---|---|
| Risk | Low |
| Default | on |
| Order | 80 |
| Relies on | everything — sets parameters on generated elements only |
| Planner | none |
| Values from | `BadNames.Marks`, `Comments`, `Manufacturers`, `Descriptions`, `Models`, `Urls`, `TypeMarks` |

**Defects** *(Revit side, plan section 7.7)*. Only writable parameters, only on elements in the
run's registry (`context.GeneratedElements(...)`); a template element with a changed value is a
bug.

- Instance parameters (`Mark`, `Comments`) and type parameters on duplicated types (`Type Mark`,
  `Description`, `Manufacturer`, `Model`, `URL`) filled with inconsistent values: typos, mixed
  casing, stale dates (`see email 3/12`), personal shorthand (`check w/ JP`), placeholders
  (`TBD`, `?`, `n/a`), values that conflict with the type name.
- `BlankMetadataFraction` of targets left blank where a standard would expect a value;
  `BadMetadataFraction` given a bad value; `DuplicateMarkFraction` of marks repeated where
  uniqueness would be expected (`1`, `1`, `01`, `A1`, `a1`).
- Values are set through `ElementFactory.TrySet`, which refuses read-only parameters and never
  throws.

**Severity.** `BlankMetadataFraction` 0.4 / 0.35 / 0.3; `BadMetadataFraction` 0.3 / 0.45 / 0.6;
`DuplicateMarkFraction` 0.2 / 0.3 / 0.4.

**Expected warnings.** *Elements have duplicate 'Mark' values* (`GeneralFailures.DuplicateValue`)
when duplicate marks are set on doors or windows — dismissed and recorded.

**Report.** `[metadata] Set Mark on 14 element(s): 5 blank, 4 duplicated ('1' x3, 'A1' x2).  ids: …`,
`[metadata] Set Comments on 20 element(s): 8 blank, 12 shorthand.  ids: …`,
`[metadata] Set Manufacturer/Description/Type Mark on 4 type(s).  ids: …`.

---

## `warnings` — Generate warnings (high risk)

| | |
|---|---|
| Risk | High |
| Default | **off** — opt-in only; severity never enables it |
| Order | 90 |
| Relies on | everything |
| Planner | none |
| Needs from the document | the baseline walls/floors and a furniture symbol (for duplicate instances) |

**Defects** *(Revit side, plan section 7.8)*. Conditions Revit will flag but still commit:

- `OverlappingWalls` walls created slightly overlapping an existing generated wall (same line,
  offset by a few mm, shorter) — *Highlighted walls overlap*.
- `DuplicateInstances` furniture (or door/window) instances placed exactly on top of a generated
  one — *identical instances in the same place*.
- `OverlappingFloors` floors overlapping a generated floor — *Highlighted floors overlap*.
- Deliberately unused views, types and materials are already produced by `documentation` and
  `content-types`.

The `GenerationSettings.Validate` result carries a warning whenever this scenario is enabled:
"The Warnings scenario intentionally creates overlapping and duplicate elements that Revit will
flag."

**Severity.** `OverlappingWalls` 1 / 2 / 3; `DuplicateInstances` 1 / 2 / 3; `OverlappingFloors`
0 / 1 / 1.

**Expected warnings** (all dismissed and recorded, and all persistent in Manage > Warnings
afterwards): `OverlapFailures.WallsOverlap`, `OverlapFailures.DuplicateInstances`,
`OverlapFailures.FloorsOverlap`. Overlapping walls may additionally raise *not enclosed* room
warnings on the next regeneration. If any of these were ever reported at error severity in a
particular document, the scenario would be rolled back and the run would continue — that is the
designed behaviour, not a failure of the run.

**Report.** `[warnings] Wall overlaps generated wall 12345 by 1800 mm (Revit: walls overlap).  ids: …`,
`[warnings] Duplicate instance placed on furniture 12346 (Revit: identical instances in the same place).  ids: …`,
`[warnings] Floor overlaps generated floor 12347 (Revit: floors overlap).  ids: …`, plus the
matching entries under **Expected warnings (dismissed)** with the definition GUIDs.

---

## Quick reference

| Order | Id | Display name | Risk | Default | Planner | Profile fields |
|---|---|---|---|---|---|---|
| 10 | `baseline` | Baseline model | Low | on (required) | `BaselinePlanner` | `CellWidthMm`, `CorridorWidth*` |
| 20 | `content-placement` | Doors, windows and furniture | Medium | on | `ContentPlanner` | `WindowSpacingMm`, `WindowPairsTooClose`, `DoorsNearWallEnd`, `SillHeightVarieties`, `DoorFlipProbability`, `Furniture*` |
| 30 | `rooms` | Rooms and spatial data | Medium | on | `RoomPlanner` | `RoomsMin/Max`, `UnplacedRooms`, `DuplicateRoomsInCell`, `SeparationLines`, `UntaggedRoomFraction`, `AwkwardTagFraction`, `RoomInCorridor` |
| 40 | `documentation` | Views and sheets | Low | on | — | `DuplicatePlansPerLevel`, `Sections`, `Elevations`, `ThreeDViews`, `DraftingViews`, `Sheets`, `EmptySheets`, `TextNotes`, `WrongDisciplineFraction`, `OddScaleFraction`, `OddCropFraction` |
| 50 | `datum` | Datum and layout tweaks | Low | on | `BaselinePlanner` (planted) | `LevelJitterMm`, `LevelOops*`, `IntermediateLevel`, `*WallsPerLevel`, `*PerLevel`, `ExteriorOverrun`, `Floor*`, `Grid*`, `OneEndBubbleGrids`, `MisalignedGrids`, `NearCoincidentGrid` |
| 60 | `naming` | Poor naming | Low | on | — (`RoomPlanner` reads the flag) | none |
| 70 | `content-types` | Near-duplicate types and materials | Low | on | — | `Duplicate*Types`, `Materials`, `NearDuplicateMaterials` |
| 80 | `metadata` | Metadata and parameters | Low | on | — | `BlankMetadataFraction`, `BadMetadataFraction`, `DuplicateMarkFraction` |
| 90 | `warnings` | Generate warnings (high risk) | High | **off** | — | `OverlappingWalls`, `DuplicateInstances`, `OverlappingFloors` |
