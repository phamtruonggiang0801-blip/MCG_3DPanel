# Create Drawing › Detail Plan / Section Detail / Detail View — design (2026-11-16)

## Why

After the Plan View sheet is finished, the engineer draws the detail sheets (A1, 1:10) by hand: `DETAIL 1…n` with
sections `1A-1A…`. Three steps take most of the time (engineer, 2026-11-16):

1. **Cut the region**: draw a rectangle, explode the panel structure block until nothing is nested, trim everything
   outside, re-block.
2. **Section marks**: draw the red `AM_8` lines inside every cut plate (offset ≈ 3 mm from each face).
3. **Draw the structure sections**.

Automating these three steps **completely** is the goal. Fittings (`- tv` / `- fv` / `- sv`), dimensions and the
sheet frames stay manual.

The hand-made samples also **omit** collars, scallops where HP pass through, snips etc. The tool must show **everything
the 3D model contains**: accuracy over the hand-drawn look.

Source samples (dump v2): `C:\Users\truonph\Desktop\MCG\AutoCAD\Drawing\Detail - CAS-00622{39,41,42}`,
`CAS-0067067`, `CAS-0068762` (`.dump.json`): 7D-16P, 7D-16S, 7D-17P, 7D-16C, 7D-17C.

## What the samples show (analysis 2026-11-16)

- Sheet 1 = Plan View 1:50 (`A1` + `CAS_HEAD` at scale 50). Sheets 2…n = `A1` frames at scale 10 ("1:10") to the right
  in Model Space, 1–2 details each.
- Each detail = block `<panel>_DET.n` (real size, inserted at scale 1) + 2–5 section blocks `<panel>_DET.n_nA-nA`.
  Section marks are two arrow blocks on `AM_10` + MText `1A` (h 30). Titles: `%%UDETAIL n` and `1A-1A` /
  `1D-1D  90°ROTATED` on `AM_1` (h 50). 4–11 details per panel.
- `DET.n` is **a horizontal section just below the top plate, looking at the structure**: webs/FB/brackets are cut
  (hence `AM_8` inside them), HP bulb/flange lie lower (visible lines), the deck plate edge is a reference outline
  (`AM_7`). Measured `AM_8`: 6 mm → 1 line at 3; 10 mm → 3.3 / 6.7; 20 mm → 5 / 10 / 15.
- A Detail View (e.g. 16P `DET.5`, "DETAIL 5 (1:25)") shares the DET numbering, sits on the plan sheet, scale 2.
- Fittings exist **only in the 2D drawing** (`tv` blocks inside `<panel>_Assy`, plus `<panel>_Wire guide support`,
  `<panel>_Hole cut out`, `<panel>_Lashing Hole`); they are not in the 3D model.

## Goals

Four commands, all run **in the 2D drawing** (where the Plan View is), added to the existing **Create Drawing ▾** menu
of the palette and as command-line commands:

| Menu item | Command | Enabled when |
|---|---|---|
| Detail Plan | `MCG_DetailPlan` | active document is a 2D drawing with a `<panel>_Assy` and `<panel>_3D.dwg` exists next to it |
| Section Detail | `MCG_DetailSection` | same |
| Detail View | `MCG_DetailView` | same |
| Update Details | `MCG_DetailUpdate` | same, and the drawing holds at least one tool-made detail |

`Plan View` stays enabled only in the 3D file, as today.

### 1. Detail Plan

1. User draws a rectangle (closed polyline, layer `AM_7`) over the Plan View, then runs the command and picks it
   (pre-selection accepted, like the Inventor tool).
2. Tool resolves the panel under the rectangle, reads its 3D file, builds `<panel>_DET.n` (next free n).
3. The block follows the cursor; user clicks the insertion point. Inserted at **scale 1**.
4. The Plan View is **not modified** except: the user's `AM_7` rectangle is kept and the label `DET.n` is added next to
   it. `%%UDETAIL n` (AM_1, h 50) is placed under the inserted block.

No scale or name prompt.

### 2. Section Detail

1. User picks a tool-made `DET.n` (insert), clicks the start point, end point (snaps horizontal/vertical, as the
   Inventor `DummySectionService`), then the viewing side.
2. Tool draws the section mark on the DET (two `AM_10` arrow blocks + `nA` text), builds `<panel>_DET.n_nA-nA`
   (letters in creation order), block follows the cursor, user clicks the insertion point (scale 1).
3. Title `nA-nA` (AM_1, h 50) under it. A section from a **vertical** cut line is rotated to lie horizontal and titled
   `nA-nA  90°ROTATED`.

Section Detail is **only** available on Detail Plans, not on Detail Views.

### 3. Detail View

1. User draws a circle **or** rectangle (`AM_7`) over the Plan View, runs the command, picks it.
2. Prompts: **scale** (default **1**; it is the insert scale factor of the block), **name** (default `DETAIL n`, next
   number of the shared DET series).
3. Same engine as Detail Plan, clipped to the circle/rectangle. Block follows the cursor; click to insert at the given
   scale. Title = the name as typed (`%%U` underlined, AM_1 h 50). The tool does not know the sheet scale, so it does
   not append "(1:x)"; the user types it into the name if wanted (my call).
4. Boundary kept on the plan with label `DET.n`.

### 4. Update Details

Re-generates the **contents** of every tool-made DET / section / view block from the current 3D file, keeping each
insert's position, rotation and scale. Everything the user placed **outside** the blocks (fittings `fv`/`sv`,
dimensions, notes) is kept. Reports which details changed (dimensions may now be off).

- **The region follows the boundary on the plan** (engineer): the tool links each DET to its `AM_7` rectangle/circle
  (by handle). If the user moved or resized it, Update cuts the new region. If the boundary was erased, Update uses
  the region stored in XData and says so.
- Section marks and `nA` letters live **inside** the DET block and are regenerated from the stored cut line. A cut
  line that no longer meets any material ⇒ the section block is left empty and named in the report; never deleted.
- Anything the user edited **inside** a tool block is overwritten; Update says so before it starts (*Continue /
  Cancel*).

### Common rules

- **Esc / right-click at any step cancels and leaves nothing behind.**
- **Stale 3D check** before every command: if `<panel>_3D.dwg` has unsaved changes (open in AutoCAD) or the 2D drawing
  was saved after the last Build/Update of the 3D file, warn and ask *Continue / Cancel*. The tool never updates the 3D
  file itself. *My call (revised while planning):* Build/Update stores a **fingerprint of the 2D `_Assy` block tree** in
  `PanelMeta`; the check compares it with the drawing's current `_Assy`. Times don't work: the 3D file's time changes on
  every correction save, and Plan View writes + saves the 2D drawing without touching `_Assy`. A 3D file without a
  fingerprint ⇒ the check is skipped.
- **Finding the 3D file** (*my call*): candidates are `<panel>_3D*.dwg` in the 2D drawing's folder whose stored
  `PanelMeta.SourcePath` / `SourceRefHandle` match this drawing and `_Assy` insert. Several match (`_3D`, `_3D_2`…) ⇒
  ask the user which one; none ⇒ the menu items stay disabled with a tooltip saying why.
- Numbering: Detail Plan and Detail View share `DET.1, DET.2…`; next number = max existing + 1 (hand-made `DET.n`
  blocks of the panel count too).

## Geometry engine (approach A — chosen)

Both Detail Plan and Section are **sections of the 3D solids**:

- Read `<panel>_3D.dwg` in the background (`ReadDwgFile`, as `Panel3DFile` / `Source2DLocator` do) — never open it.
- Map the 2D pick region into 3D coordinates through the same transform Plan View uses (2D `_Assy` ↔ 3D panel block).
- Keep only solids whose extents meet the region (performance).
- Use AutoCAD's `Section` object + `GenerateSectionGeometry`, which returns the cut boundary and the background
  (visible, hidden-line removed) geometry.
- **No hidden lines, in plan or section** (engineer, 2026-11-17, after the spike). AutoCAD removes hidden lines only
  inside one solid, so the visible background is taken from the **union** of the clipped solids, with every member's
  edges **imprinted** on it first so the joint lines between plates stay. Cut faces are still taken member by member
  (they are never hidden, and the members must stay identifiable).
  - **Detail Plan / Detail View**: horizontal plane just below the top plate, looking at the structure; depth = whole
    structure height. Region = rectangle/circle.
  - **Section**: vertical plane on the cut line, looking toward the chosen side; length = part of the cut line inside
    the DET region; depth = **to the DET boundary** in the viewing direction.
- 2D-only items (fitting `tv` blocks, wire-guide path, hole cut-outs) are copied from the 2D panel
  blocks and trimmed to the region. Lashing holes are not copied: they are not built in 3D (engineer 2026-11-18).
  Only inserts directly in `_Assy` go to `AM_2`; inserts inside a child block keep their own layer. Top plate
  cut-outs come from the 3D top plate (all section loops), on `AM_7`. 2D items are drawn — **in Detail Plan / Detail View only**; sections get no fittings. *My call:* block
  inserts (fittings) are never exploded: kept whole when their insertion point is inside the region, dropped otherwise
  (the samples keep whole blocks).
- **Orientation** (*my call*): the Detail Plan has the plan's orientation. Sections always have the top plate **on
  top**; left/right as seen by the viewer looking in the chosen direction. A section from a vertical cut line is drawn
  the same way (plate on top) and only titled `90°ROTATED`.

Rejected:
- **B** — trim the 2D plan lines and patch collars/scallops/snips from 3D: every feature type needs its own code; the
  ones not written are silently missing (against "everything the 3D has"), and sections still need a 3D engine.
- **C** — AutoCAD `VIEWBASE` / `VIEWSECTION`: Paper Space associative views, not Model Space blocks on `AM_x` layers;
  does not match the company sheet.

## Drawing conventions

| Item | Layer |
|---|---|
| Outline of cut material | `AM_0` |
| Section-mark lines inside cut material | `AM_8` |
| Visible lines beyond the cut (Detail Plan **and** Section) | `AM_0` |
| Top plate edge + cut-outs in Detail Plan / View (reference) | `AM_7`; where the region cuts the plate: `AM_4` `amzigzag` |
| Wire-guide path (from 2D) | `AM_7`, width 3 |
| Fitting `tv` blocks (from 2D) | `AM_2` |
| Break where the region boundary cuts through material | `AM_4`, linetype `amzigzag` |
| Section mark arrows + letter | `AM_10` (text h 30); arrows linetype `Continuous` (AM_10 is dashed) |
| Titles | `AM_1` (text h 50) |
| Continuous symbol + thickness text | `AM_5` (block `Continious symbol`, text) |
| Weld trace mark `$` | `AM_10`, MText h 35 |

- **AM_8 count** inside a cut plate of thickness t: `max(1, round(t / 6))` lines, evenly dividing t
  (6 → 1, 10 → 2, 12 → 2, 15 → 3, 20 → 3).
- **HP in sections**: true cross-section from 3D (not the `HP_xxx_sv` block).
- **Continuous symbol** — sections only: one for every **upright** plate (web, FB, bracket; not HP) cut by the plane
  **and continuing on both sides** of it — never the deck plate or face plates (engineer 2026-11-18: as the samples,
  where all 47 symbols are horizontal, rotation 180°). Arrow line perpendicular to the plate at mid height; thickness
  text always in parentheses, `(10.0)`, right beside the plate it belongs to, just above/below the arrow, moved along
  the plate until it touches no line, symbol or text. Members ending at, or interrupted at, the plane get nothing.
- **`$` weld trace** — sections only: for **each** plate A **parallel to the section plane** that is **visible**
  (not hidden) behind the cut, **one** `$` **centred on** the middle of its visible line (the line runs through the
  `$`, as all 26 in the samples). Only the plate's own lines count: lines coinciding with another member's edge are
  skipped (engineer 2026-11-18). Sample 16P 1B-1B: a web parallel to the cut ends at x = 887; its top/bottom edges
  coincide with the deck plate and face plate, so only the end line shows, and `$` sits on it.
- **Section mark arrows** sit **outside** the DET picture: the cut line is extended past the extents of the DET
  content by 250 (samples: 240–330), arrows at those ends.
- **AM_8** also applies to cut HP webs in the Detail Plan (*my call*: everything cut gets marked).
- Existing layers / dim styles / text styles only — create none (as Plan View). Symbol blocks missing from the drawing
  follow the Plan View fallback: drawing → `CAS.dwt` → tool-drawn shape.
- Tool entities and blocks carry XData (`MCG3D_DETAIL`) with: panel, kind (plan / section / view), number/letter,
  region (rectangle/circle in 2D coordinates), cut line + viewing side, scale. Update Details is driven by it.

## Non-goals

- Fittings `fv` / `sv` in sections, dimensions, `thk_sym`, sheet frames `A1` / `CAS_HEAD`, notes.
- Choosing detail locations or cut lines automatically.
- Sections on Detail Views.
- Updating the 3D file from the 2D drawing.

## Anti-goals

- Touching the Plan View / `<panel>_Assy` block tree (handles key the 3D corrections — see Plan View spec).
- Leaving partial geometry after Esc or an error.
- Dropping 3D features (collar, scallop, snip, chamfer) to look like the hand sample.
- Erasing user work outside tool blocks on Update.
- Creating layers / styles.

## Decisions (engineer, 2026-11-16)

1. Three creation functions + Update; all in the 2D drawing.
2. Detail Plan: no scale/name prompt; Detail View: scale (default 1) + name; no sections on Detail Views.
3. Engine = approach A (3D solid sections).
4. Everything in 3D is shown (collar, scallop, snip…).
5. Visible lines beyond the cut → `AM_0` in both plan and section.
6. Section depth to the DET boundary; HP as true section.
7. `thk_sym` dropped; Continuous symbol + `(t)` + `$` rules as above.
8. Insert scale is 1 (no frame-scale detection).
9. Update Details button (regenerate block contents in place).
10. UI: items in Create Drawing ▾, command names above, stale-3D warning, shared DET numbering.
11. Update follows the `AM_7` boundary as it is now on the plan (moved/resized ⇒ new region).
12. `$`: one per visible plate parallel to the section plane, beside its visible outline at mid height.

## Left to the builder

- Exact 2D↔3D transform reuse from Plan View code; plane elevation "just below the top plate" (e.g. 1 mm under its
  underside).
- Turning `GenerateSectionGeometry` output (regions, curves, splines) into polylines/lines on the right layers; merging
  collinear/duplicate edges.
- Clipping to a circle for Detail View; zigzag break placement.
- Section mark arrow block: reuse the samples' arrow block shape (anonymous `A$C…` in samples) — drawn by the tool if
  absent.
- Performance on large panels (7D-17C ≈ 15 m).
