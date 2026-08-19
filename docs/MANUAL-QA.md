# Manual visual QA checklist

The eyeball pass from plan section 11 ("Manual visual QA"): after the integration tests say the
mechanics work, does the *model* look the way a training or audit sample should? Run it on a
Medium-severity default run in a disposable project from the default architectural template, then
repeat the last section at Low and High.

The standard for every item is the plan's design principle: **bad, but valid, and
understandable**. A defect that cannot be spotted is not useful; a defect that makes the model
incomprehensible is not either.

## 3D view

- [ ] Open a generated 3D view (or `{3D}`). *Good:* a plain rectangular building with the
      configured number of levels, exterior walls on every buildable level, a corridor with rooms
      either side, floors on each buildable level. Nothing floats far from the building; nothing
      is stretched to absurd heights.
- [ ] Look along a corner. *Good:* one or two corners visibly do not clean up (join disallowed)
      or one exterior wall runs a little past the corner (High only); the rest are clean.
- [ ] Look at the top of the walls on a lower level. *Good:* one or two walls stop short of or
      poke past the level above (unconnected height); the rest are attached.
- [ ] Levels in an elevation or section. *Good:* the level datums are not on a clean module
      (a few tens of mm off), and — Medium/High — an unnecessary `Mezzanine`-type level sits
      between the first two with nothing on it.
- [ ] Furniture. *Good:* a few pieces per room, one rotated at an odd angle, one sitting on a
      wall line, and (Medium/High) one outside the building altogether. Not a furniture showroom.

## Plan views

- [ ] Open the plan for the lowest generated level. *Good:* exterior walls, two corridor walls,
      transverse partitions; doors from the corridor into rooms; windows in the exterior walls.
- [ ] Partition alignment. *Good:* one or two partitions are visibly a few mm off the grid line
      when you zoom in; a stub wall continues a partition into the corridor and stops. Nothing is
      a zero-length sliver.
- [ ] Corner gaps. *Good:* on Medium/High, one partition stops a hair short of the corridor wall,
      so the two rooms either side read as one region to Revit (see the room warnings).
- [ ] Doors and windows. *Good:* one door is jammed against a partition; a pair of windows is
      closer together than the rest; sill heights differ between windows in a section (see below);
      one or two doors swing the "wrong" way relative to their neighbours.
- [ ] Grids. *Good:* numbers one way, letters the other; a bubble missing at one end on some;
      overhangs that do not match; one grid nearly on top of another (Medium/High); one interior
      grid off the wall it was meant to align with.
- [ ] Room separation lines. *Good:* one across the corridor, one or two splitting a room where a
      wall would have been the honest choice.
- [ ] Room tags. *Good:* some rooms have no tag; some tags are pushed against a wall instead of
      centred; a tag is never outside its room by more than a leader can explain.
- [ ] Duplicate plans. *Good:* the Project Browser shows more than one plan per level, at
      different scales / detail levels / disciplines, with names like `Copy of Copy`,
      `Level 1 - do not use`.

## Sections and elevations

- [ ] Open a generated section. *Good:* it cuts through the building and shows the levels;
      windows at more than one sill height (Low: two, High: up to five) with no pattern; the
      unnecessary intermediate level, if any, has no floor.
- [ ] Open a generated elevation. *Good:* an elevation marker exists in a plan; the elevation
      looks at the building; crop and extents are inconsistent between elevations. Names like
      `Elev North`, `elevation-north-NEW`.

## Sheets

- [ ] Open each generated sheet. *Good:* the title block from the template (or none, reported as
      a fallback); sheet numbers that mix `A101`, `A-102`, `1`, `PLAN-03`; sheet names like
      `Floor Plan`, `Sheet 1`, `Do Not Print`.
- [ ] Viewports. *Good:* plans placed on sheets whose names/numbers do not describe them; at
      least one sheet is empty (Medium/High); viewports crowded or unevenly arranged rather than
      overlapping the title block completely.
- [ ] Text notes. *Good:* `TBD`, `CHECK`, `???`, `REMOVE BEFORE ISSUE` and similar placed in views
      or on sheets; readable, not stacked into an unreadable blob.

## Report matches document

- [ ] Every count in the report matches what you can count in the document (levels in an
      elevation, sheets and views in the Project Browser, rooms in a Room schedule, materials in
      Manage > Materials).
- [ ] Every defect line points at something you can find. Pick five at random, select the listed
      element ids (Manage > Inquiry > Select by ID), confirm the described defect is there.
- [ ] Every fallback line corresponds to something the template really lacks.
- [ ] Every expected-warning line has a matching entry in Manage > Warnings (or the condition
      was transient and is gone — acceptable, note it).
- [ ] Unexpected failures: none, or each one understood and filed.

## Names, rooms and tags are auditable

- [ ] Project Browser: bad names are *obviously* bad to a trained eye but still tell you what the
      view is (a plan called `Copy of Copy of Level 1` is good; twenty views called `View 1`
      through `View 1 20` is not).
- [ ] Levels: names like `L1`, `Level 2`, `Mezz`, `Top-ish` — an inconsistent convention, and the
      order in the Project Browser or an elevation does not match the names' apparent order.
- [ ] Room schedule: names with typos and casing drift (`Office`, `office`, `Offce`), vague
      values (`Misc`, `TBD`); numbers like `101`, `101A`, `101-old`, `1O3`; at least one
      *Not Placed* row; possibly a *Redundant Room*.
- [ ] Type selector: near-duplicate wall/floor types (`… -new`, `… copy`, `… (Do Not Use)`);
      some of them unused.
- [ ] Materials: `New Mat`, `Material 1`, `Gray-ish`, `DO NOT USE`, near-duplicates with slightly
      different colours.
- [ ] Door/window/wall schedule with Mark, Comments, Manufacturer, Description, Type Mark:
      a mix of blank, inconsistent (`Acme` / `ACME Corp.` / `See spec`), typo'd and duplicated
      values — on generated elements only. Any template element with a changed value is a bug.

## Cleanup from a reopened document

- [ ] Save, close, reopen. **View Last Report** loads the stored report.
- [ ] **Clean Generated** removes everything the run created; the cleanup report shows kept = 0
      unless you deliberately hung user content on a generated element.
- [ ] After cleanup the document looks like the template again: no extra levels, views, sheets,
      types, materials or warnings; Manage > Warnings is as it was before the run.
- [ ] Undo after cleanup restores the run; Redo removes it again.

## Low, Medium and High feel different

Run seed `42` at each severity in three fresh documents and put the plans side by side.

- [ ] **Low** reads as "a sloppy but competent team": one or two misaligned walls per level, no
      corner gaps, no stub walls, no intermediate level, one grid bubble missing, few rooms, no
      duplicate rooms, one duplicate plan per level, one section/elevation/3D, two sheets, none
      empty, few text notes, one duplicate wall type, three materials.
- [ ] **Medium** reads as "several teams, weak standards": corner gaps and stub walls appear,
      the intermediate level appears, grid extents go inconsistent and one grid is nearly
      coincident, a duplicate room and a corridor room appear, an empty sheet appears, floors get
      an offset and a jog on some level.
- [ ] **High** reads as "nobody is in charge": more of everything per level, three misaligned
      walls, two corner gaps, two stubs, an exterior wall overrunning a corner, five sill
      heights, furniture outside the building, up to ten rooms with two duplicates, three
      sections/elevations/3D views, four sheets, eight text notes, three duplicate wall types and
      seven materials — and it is still a building you can walk through in 3D.
- [ ] Element totals rise from Low to High and all three stay under the default maximum of 400
      with the default footprint.

## Sign-off

| Section | Reviewer | Revit | Date | Pass / notes |
|---|---|---|---|---|
| 3D view | | | | |
| Plan views | | | | |
| Sections and elevations | | | | |
| Sheets | | | | |
| Report matches document | | | | |
| Names, rooms, tags auditable | | | | |
| Cleanup from reopened document | | | | |
| Low / Medium / High | | | | |
