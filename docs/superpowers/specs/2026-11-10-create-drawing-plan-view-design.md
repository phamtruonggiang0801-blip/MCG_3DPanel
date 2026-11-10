# Create Drawing › Plan View — design (2026-11-10)

## Why

The engineer works like Inventor's `.iam` → `.idw`: the 3D panel file (`<panel>_3D.dwg`) is where the panel is edited
(Kind, names, Tripping, Collar, snipe, delete/merge). Today the finished 2D sheet is still annotated by hand: names,
symbols, ~100–300 dimensions, sections X-X / Y-Y, lashing-hole plan. Create Drawing writes that annotation from the 3D
file back into the 2D drawing the panel was built from, and redoes it whenever the 3D file changes.

## Goals

- A **Create Drawing ▾** dropdown next to **Export ▾** in the 3D file. First item: **Plan View**. Later items (detail
  sheets DET.x at 1:10 / 1:25) come later.
- Output goes into **the 2D drawing the panel was built from**, Model Space, one panel = one sheet. The sheet frame
  (`A1` + `CAS_HEAD`) and the panel block (`<panel>_Assy`) already exist there.
- Annotation follows the company sheet exactly: existing layers, dim styles, text styles and symbol blocks. Nothing new
  is created in those tables.
- Running it again after editing the 3D file updates the sheet: everything the tool drew is removed and drawn again.

## Non-goals

- Detail sheets (DET.1…DET.12, sections 1A-1A…), AMDTNOTE notes, revision triangles. Later dropdown items.
- Redrawing plan geometry. The plan stays the hand-drawn `<panel>_Assy` block.
- Writing the 3D model into the 2D drawing.

## Anti-goals

- **Touching any entity inside the `<panel>_Assy` block tree.** Its handles key every correction and name of the 3D
  file (`SourceKey = {2D handle}#{i}`); a changed handle loses the user's 3D edits on the next Update.
- Saving the 2D drawing without the user seeing the result.
- Creating layers, dim styles, text styles; changing their settings.
- Silently deleting hand-drawn annotation.

## Decisions (engineer, 2026-11-10)

1. Only text, symbols, dimensions and section blocks are added; the hand-drawn plan is kept.
2. Symbols from 3D. Add/delete in 3D ⇒ the symbol appears/disappears on the next run.
   - **B** = tripping bracket, placed near the bracket.
   - **GE / GB** = end type. The text is the **Kind value shown in the Result tab** for that row.
   - **S** = Sniped End.
   - **OB / OB1** = bracket codes.
   - `CS_horl_up_right` = one symbol at **every place where a member runs continuously through the web of another**:
     HP/FB through a girder web, or a girder through a girder. It goes on the side of the web given by the
     structure's **build direction** (`chiều đổ`, the panel `Position`, as used by the Tripping rule). On 7D-17P that
     gives 95 = 19 HT × 5 inner L webs.
3. Sections X-X / Y-Y are **typical sections**: every member of one direction is collected onto one section, even if it
   does not lie on one cut line. Deleting/merging members in 3D edits that list.
4. Dimensions on `Mechanical-AM_9` inside the panel blocks are **drafts** (the layer is frozen). They are the source of
   the sheet dimensions and are **copied as they are**: 3D delete/merge does not drop a draft (it only edits the
   sections). A panel without drafts gets only the overall Top Plate dimensions and the lashing-hole diameter; the tool
   does not invent dimensions.
5. The lashing-hole plan, `M.D1` / `M.D2` and the tolerance note are part of the sheet even though they are not 3D data.
6. Existing hand-drawn annotation (old drawings): the tool detects it and asks **Replace / Keep both / Cancel**.
   - **Replace** erases only what the tool redraws (my call):
     - texts matching the label patterns (`T#`, `L#`, `HT#`, `HL#`, `BS`, `B`, `OB*`, `GE*`, `S`, `GB`);
     - AM_4 / AM_5 dimensions whose axis and interval match a draft, or the overall Top Plate dimensions;
     - the hand-drawn section and lashing-plan inserts;
     - the symbol blocks listed in Flow step 8.

     Kept: `DETAIL n`, `1A-1A`, `REF. DIM`, `(<>)` reference dimensions, chain totals, dimensions drawn on the sections.
   - **Keep both** (my call): the tool's own section and lashing-plan blocks use their own names, `<panel>_X-X_MCG`,
     `<panel>_Y-Y_MCG`. Hand-drawn block definitions are never redefined.
7. Approach A (below): open the 2D drawing in AutoCAD, draw there, switch to it, do not save.

## What the reference sheets show

Source: `MCG_DumpDrawing` (dump v2) of six finished sheets in
`c:\MacGregor_CAS_WF\Designs\40 Products\RAMP AND DECK\CAR DECK\Hoistable Car Deck\Part Complete\400280826\`
(`CAS-0062235` 7D-15P, `-0062239` 7D-16P, `-0062242` 7D-17P, `-0067067` 7D-16C, `-0068762` 7D-17C, `-0068789` 7D-15C)
and `CAS.dwt` (= 7D-17P). All figures below are at sheet scale 1:50.

**Scale.** `A1` and `CAS_HEAD` are inserted with scale = sheet scale (50; 7D-17C uses 75), `CAS_HEAD` attribute
`GEN-TITLE-SCA{5.42}` = `1:50`. Every size below is paper size × scale: at 1:75 the T/L text is 375, symbols ×1.5.
`INSUNITS` = mm, `DIMSTYLE` = `AM_ISO`, all text style `STANDARD` (ISOCP.SHX, height 0).

**Layout.** Positions are relative to the plan; the plan block origin differs per drawing.
- `<panel>_X-X` directly above the plan, same X as the `<panel>_Assy` insertion.
- `<panel>_Y-Y` rotated 90° left of the plan.
- Lashing-hole plan to the right: a second insert of `<panel>_Top Plate` + `<panel>_Lashing Hole`.
- `M.D1`, `M.D2` above the lashing-hole plan.
- `Measurements_Tolerances vFuture` at `(1601.55, 5485.71)` × scale/50 from the frame origin (layer AM_6).
- Two `Rec. for tolerance` (×50) at the overall Top Plate dimensions.

**Text.**

| Item | Layer | Height | Example |
|---|---|---|---|
| Girder names | AM_1 | 250 | T1, L7, T6a |
| Stiffener names and symbols | AM_6 | 150 | HT12, HL2, BS, GE1, B, OB, OB1 |
| View titles (MText) | AM_1 | 250 | `PLAN VIEW\P(ALL FITTINGS SHOWN ARE FOR REFERENCE LINE)`, `LASHING HOLES PLAN` |
| Member list (MText) | AM_6 | 175 | `TOP PLATE THICKNESS: 6.0`, `STIFFENERS (HL & HT): HP 120x6.0`, `TRANSVERSAL GIRDERS T1,T2 : WEB 374x6.0/FLANGE 380x20.0`, … |

**Dimensions.**
- All linear dimensions use `AM_ISO$0` with overrides **DIMSCALE = sheet scale** and **DIMTMOVE = 1**. Text is the
  measured value (`<>`).
- **AM_4** = lashing holes. 100 % of them repeat an AM_9 draft of `<panel>_Lashing Hole` (48/48, 30/30, 41/41): same
  axis, same interval. The definition points are moved to the plate edge, and the dimension line sits outside the
  view, in chains.
- **AM_5** = structure. On the plan sheet most repeat an AM_9 draft of `<panel>_Structure` or `_Lashing Hole`
  (44/60 on 7D-17P, 78/88 on 7D-15C). The rest are:
  - overall Top Plate length and width, text `<>{}{\H0.71x;\C3;%%p4}{{}}TOP PLATE`;
  - one or two chain totals;
  - the lashing-hole diameter (`AM_ISO$3`, `<>-LASHING HOLES`);
  - a few on the sections;
  - reference dimensions shown as `(<>)`.

  Some use oblique ticks (`DIMBLK1/2 = _Oblique`, `DIMSAH`).

**Sections.** The blocks `<panel>_X-X` / `_Y-Y` (all entities on layer 0) contain:
- the Top Plate as a thin rectangle polyline (full length × thickness);
- each girder as two rectangles (web t×h, flange b×tf) at its position;
- each HP / BS as a profile block named by size (`HP120x6`, `BS 80x6`).

7D-17P X-X shows T1, T2 and 22 HP at 775. Its Y-Y shows L1…L7 (flanges 250/170/170/150/170/170/250, matching the member
list) and 12 BS + 2 HP. In Model Space, `thk_sym` blocks (40–140 per sheet, scale ±1 × scale/50) mark the plate
thicknesses on the sections.

**Continuous symbol.** `CS_horl_up_right` on AM_0 (42–95 per sheet). 7D-17C uses `CS_symbol` on AM_6 instead.

## Approaches considered

- **A. Open the 2D drawing and draw in it (chosen).** Use the document if it is open, else open it; draw; make it
  active; do not save. The user sees the result, can Undo, and saves. Costs a second tab, and an unsaved 2D drawing
  must be handled first, as Update does.
- B. Write the 2D file in the background and save it. Rejected: overwrites a file the user has not seen, no Undo,
  impossible while the file is open.
- C. Overlay only the hand-drawn sheet without sections. Rejected earlier: sections are part of the sheet.

## Design

### Finding the 2D drawing

- Build and Update store the 2D drawing's full path in the 3D file's panel data, next to `SourceRefHandle` /
  `SourceBtrHandle`.
- Create Drawing uses that path when the file exists and contains a block reference with `SourceRefHandle` whose block
  definition is `SourceBtrHandle`.
- Otherwise (moved file, older 3D file without the path, wrong drawing), it asks the user to pick the 2D drawing, checks
  the handles, and stores the new path.

### Flow (one panel; Create Drawing runs for the panel of the active 3D file)

1. The 3D file must be saved. If it has changes, ask **Save and Create** / **Cancel**, like Update.
2. Find the 2D drawing (above). If it is open with unsaved changes, continue there: the change becomes part of the same
   Undo history. My call; the user will save it anyway.
3. Open or activate the 2D document. Do not use the old document's Editor after switching (this crashed AutoCAD
   before).
4. Read everything needed **from the 3D file** before switching:
   - the solids' panel data;
   - corrections and names;
   - the copy of the 2D block tree (same handles), including its AM_9 drafts.
5. Check the 2D drawing has every layer, dim style (`AM_ISO`, `AM_ISO$0`, `AM_ISO$3`), text style and symbol block
   used. If one is missing, stop and list it. Nothing is created.
6. Find the sheet: the `A1` frame containing the `<panel>_Assy` insertion. Its scale is the sheet scale.
7. Remove what the tool drew last time: every entity carrying its mark for this panel.
8. Detect hand-drawn annotation on this sheet; if found, ask **Replace / Keep both / Cancel**. Replace erases it.
   Detection covers:
   - blocks named `<panel>_X-X` / `_Y-Y` / the second `_Top Plate` + `_Lashing Hole`;
   - Text/MText on AM_1/AM_6 and dimensions on AM_4/AM_5 inside the sheet frame;
   - `CS_horl_up_right`, `thk_sym`, `M.D1`/`M.D2`, `Rec. for tolerance`, `Measurements_Tolerances vFuture` inside the
     frame.
9. Draw (next section) in one transaction. Every entity gets the mark (XData: app name + panel `SourceBtrHandle`).
   The tool's section blocks are always `<panel>_X-X_MCG` / `<panel>_Y-Y_MCG`, redefined on each run. After Replace
   the hand-drawn `<panel>_X-X` / `_Y-Y` inserts are erased; their definitions are left for PURGE.
10. Status: `Plan View: {n} labels, {m} dimensions, sections X-X / Y-Y.` The 2D drawing is left unsaved and active.

### What is drawn

1. **Names.** One per named member from the Naming tab, at the member (text placement rule learned from the
   reference sheets). T/L on AM_1, h = 5 mm paper; HT/HL/BS/GE on AM_6, h = 3 mm paper.
2. **Symbols.**
   - B at each tripping bracket; S at each sniped end; the Result-tab Kind text (GE…/GB…) at each girder-end
     / girder-bracket row; OB/OB1 at brackets (code from 3D). Text on AM_6, 3 mm paper, like B and OB in the
     reference sheets.
   - `CS_horl_up_right` on AM_0 at each continuous pass-through, on the build-direction side of the web.
3. **Dimensions.**
   - Every AM_9 draft in `<panel>_Lashing Hole` → AM_4; every one in `<panel>_Structure` → AM_5. Same axis and
     interval, moved outside the view in chains.
   - Plus: overall Top Plate length and width with the ±4 text, and the lashing-hole diameter.
   - `AM_ISO$0`, DIMSCALE = sheet scale, DIMTMOVE = 1.
4. **Sections X-X / Y-Y** (typical): every live member of the direction at its position.
   - Plates as rectangles on layer 0. Thickness and height come from 3D; deleted members are left out; merged members
     appear once. A member whose height varies (girder end, chamfer tier) is drawn with its main web height (my call).
   - HP/BS as their profile block, plus `thk_sym` per plate.
   - Spacing dimensions on AM_5.
5. **Lashing-hole plan**: second insert of `_Top Plate` + `_Lashing Hole`, with its AM_4 dimensions, `M.D1`, `M.D2`,
   title.
6. **Fixed items**: `Measurements_Tolerances vFuture`, `Rec. for tolerance` ×2, view titles, member-list MText (sizes
   from 3D, wording as in the reference).

## Constraints from the code

- `PanelMeta` holds `SourceRefHandle`, `SourceBtrHandle`, `Input`, `Position`, solids; no 2D path yet.
- The 3D file contains the 2D block tree with original handles (read with `ReadDwgFile`), so the drafts and plan
  geometry are available without the 2D file.
- Rule from `ux-guardrails`: every ObjectId belongs to one drawing; no silent errors on user actions.

## Left to the builder

- Exact placement rules (label offset from its member, chain spacing, distance of sections from the plan). Derive from
  the reference dumps and state each one in the plan.
- XData app name and how the mark is registered.
- English wording of dialogs and status beyond the texts above.
- Checklist rows (`docs/ux-checklist.md`) and the SESSION_LOG entry.
