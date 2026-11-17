# UX checklist — MCG_3DPanel

Use this checklist on every branch that touches the UI (palette, Result tab, highlight/zoom, corrections), anything
that holds an `ObjectId`, or any builder `catch`. It exists because small UX bugs reached the engineer (2026-11-05):
a misaligned Tripping button, a squeezed Kind dropdown, and highlight/zoom that stopped working after a panel was
built in a second drawing.

**How to use it**
1. **Claude, before reporting a branch done:** go through every row against the code. Mark each row ✔ (checked in
   code), ✘ (broken — fix it first) or 🔍 (can only be seen in AutoCAD). List the 🔍 rows in the final message.
2. **Final reviewer:** checks the rows that the diff touches.
3. **Engineer:** runs the 🔍 rows in AutoCAD before merging.

## A. Several drawings

| # | Steps | Expected |
|---|---|---|
| A1 | Build 3D panel P1 from drawing A (saved), then build panel P2 from saved drawing B. Click rows and ⌖ in each 3D file. | Each file shows only its own panel. Highlight and ⌖ work in the active file. No error. |
| A2 | After A1, switch to drawing A (the 2D source). | The Result and Naming tabs show no rows (A has no 3D panel). Nothing stays highlighted in the other file. |
| A3 | After A2, switch back to a 3D file. | Its rows return by themselves (tabs follow the active drawing), and the selected row is highlighted again without clicking. |
| A4 | Close the drawing whose rows are listed. | The rows disappear and the status says "Drawing closed…". AutoCAD does not crash. |
| A5 | From another drawing, change Tripping / ⇄ / Sniped End / Delete / Clear Correction on a row. | The status says "This panel is in drawing X — switch to it to change P". Nothing is saved, and the control returns to its real state. |
| A6 | Close the active drawing while one of its rows is highlighted, then click a row of another open drawing. | No crash. The status explains what happened. |

## B. Lifecycle

| # | Steps | Expected |
|---|---|---|
| B1 | Build, then change a correction (Tripping On, ⇄, Sniped End). | Exactly one rebuild. The same row stays selected and highlighted, and the view does **not** zoom. |
| B6 | Focus the Tripping dropdown, press Esc, then press ↑ / ↓, or scroll the mouse wheel over it. | Nothing changes and there is no rebuild. The wheel scrolls the grid. |
| B2 | Ctrl+Z after a rebuild, then click a row. | The status says "Solid no longer exists…". No exception. |
| B3 | Save the 3D file, close it, reopen it (File > Open, or Open in the build dialog). | The Result and Naming tabs fill by themselves, with no Load and no rebuild. Corrections and names are intact. The file closes with no Save prompt. |
| B4 | Build several panels at once. | The Panel column is visible. Every row highlights in its own block. |
| B5 | Delete a row (right-click), then Restore it. | The row turns grey/italic, then comes back after the rebuild. |

## C. Grid and palette

| # | Steps | Expected |
|---|---|---|
| C1 | Look at a row that has ⇄, Sniped End, Tripping and ⌖. | All controls are 20 px high, vertically centred, with no control taller than its neighbours. |
| C2 | Make the palette narrow (MinimumSize), then wide. | No clipped control. Kind fills the free space. |
| C3 | Select a corrected row. | The text stays blue and readable on the light-blue selection. |
| C4 | Hover over every button and column header that has a tooltip. | The tooltip is in English and says what clicking does. |

## D. No silent failures

| # | Steps | Expected |
|---|---|---|
| D1 | Build a panel with a part that fails (e.g. 7D-16C: Web `21913`), then rebuild it from the Result tab. | Both times the status shows "⚠ 1 2D part not built (Web 21913) — see MCG_PanelBuild_….json", and that file lists the part. `Unknown` parts (non-structure lines) are not counted. |
| D2 | A builder hits an exception (cutout, collar, bracket B, tripping, HP end cut). | The status shows "⚠ N build warning(s)", and the JSON has `warningCount` / `warnings`. |
| D3 | Export CSV on a PC with no app for .csv. | "Exported X, but could not open it: …" |
| D4 | Highlight or zoom fails (e.g. empty bounds). | A status message explains it. The failure is never swallowed silently. |
| D5 | Click a valid row after a red error, such as "Solid no longer exists". | The red error is replaced by the normal summary. |

## E. Multi-select and bulk edit

| # | Steps | Expected |
|---|---|---|
| E1 | Select rows with Shift+click, Ctrl+click, a mouse drag and Ctrl+A. | Every selected object is highlighted in the drawing at once. No zoom. |
| E2 | With 5 HP rows and 2 Web rows selected, set Tripping = On on one of the HP rows. | All 5 HP rows change, the Web rows are skipped. The status says "Tripping = On on 5 rows · 2 skipped (not applicable)". Each panel rebuilds once, and the 7 rows stay selected. |
| E3 | Select rows from two panels and press ⇄ on one of them. | Both panels rebuild once each, and every selected row gets the same direction. |
| E4 | Change a control on a row that is NOT in the selection. | Only that row changes. |
| E5 | Select rows from another drawing and change Collar. | Nothing is saved, the control returns to its old value, and the status names the drawing. |
| E6 | Right-click Delete, then Restore, with several rows selected. Then Clear Correction. | They act on the whole selection, and the status shows the counts. |
| E7 | Ctrl+C with 3 rows selected, then paste into Excel. | The header plus the 3 rows are pasted. |
| E8 | ⌖ on a selected row while several rows are selected. | The view zooms to all selected objects. |

## F. FB height

| # | Steps | Expected |
|---|---|---|
| F1 | Type `x`, `0` or `-5` in the FB height box (Input tab or Result tab), then press Enter. | A clear message asks for a positive number. Nothing is rebuilt. |
| F2 | On the Result tab, set an FB to 100 and press Enter. | The FB, its BF bracket, GE/GB-FB and any HP end at that FB use 100. Size shows `100 × t…`. |
| F3 | Clear the box and press Enter. | The FB returns to the Input tab height. |
| F4 | Type a new height, then click away without pressing Enter. | The value is not saved and the old value comes back. |

## G. Naming tab

| # | Steps | Expected |
|---|---|---|
| G1 | Build 3D a panel (it opens as `<panel>_3D.dwg`), then open the Naming tab. | One row per Web/HP line and per flat bar, names T#/L#/HT#/HL#/BS; clicking a row highlights every piece of the line. |
| G2 | Rename a line (type `T6a`, Enter), Ctrl+S, close and reopen the 3D file. | The name `T6a` is still there (blue); the other numbers are unchanged. |
| G3 | Select 12 BS rows, type `BS1` on one of them and press Enter. | All 12 rows get `BS1`; the status shows the count. |
| G4 | Right-click → Reset to auto on renamed rows. | They return to the generated names (black). |
| G5 | Change Position to Starboard. | L/HL numbers reverse (top → bottom); T/HT stay; your own names stay. |
| G6 | With 3D file A and 3D file B open, switch to B and rename a row. | Only B changes; A is untouched. Switching back to A shows its own names. |
| G7 | Build 3D, then open Naming; then change a Kind on Result (rebuild). | Naming lists the built panel and highlights its 3D solids; after the rebuild the rows and your names are still there. |
| G8 | Type a name, then click another row without pressing Enter. | Only the edited row is renamed. |
| G9 | Look at T1 and a web without flange, an HP and a BS row. | `T1 WEB 374x6.0/FLANGE 100x20.0`, `T2 WEB 374x6.0` (if no flange), `HL1 HP 120x6.0`, `BS FB 80x6.0`; only Panel/Name/Kind columns. |
| G10 | Edit `T7 WEB …` to `T6a WEB …` (or just `T6a`), Enter. | Name becomes `T6a WEB 374x6.0/FLANGE …`; the size is never doubled. |
| G11 | Select 2 stray rows → right-click Del Name. Then Restore deleted names (N). | Rows disappear and the rest renumber; Restore brings them back and renumbers again; survives Ctrl+S + reopen. |
| G12 | A BS line split far apart (e.g. 947 + 8428 on one axis). | One row. |
| G13 | Click Build 3D with an invalid value on the Input tab. | A message asks to fix the Input tab; no file is created. |
| G14 | Look at the columns. | Name (editable, e.g. `T1`) and Size (read-only, e.g. `WEB 374x6.0/FLANGE 100x20.0`) are separate; Copy pastes them in separate columns. |
| G15 | Click a Web row that has a flange (right after Build, and after reopening the 3D file). | The web and its flange(s) highlight together. |
| G16 | In a 3D file, open Naming. Select T3 and T4 → right-click Merge. Then Unmerge. | One row `T3`, Kind `Web (merged)`, T5 becomes T4…; Unmerge splits and renumbers back. Merging Web with HP, or T with L, is refused with a message. Survives Ctrl+S + reopen of the 3D file. |
| G17 | Right-click a line of 2+ pieces that was never merged → Unmerge. | The line splits into one row per piece and everything renumbers; the status says Unmerged. Unmerge on a single-piece line is disabled with a tooltip. |
| G18 | After G17, right-click ONE of the split rows → Reset to auto. | All pieces of that automatic line rejoin into one row (not only the clicked piece); the status says the pieces rejoined. Merge a+b of a split line, Reset the merged row, then Unmerge: you get separate rows, never a merged one. |
| G19 | On a split row: Del Name, then Restore deleted names. | The row disappears and the rest renumber; Restore brings the piece back as its own split row, and numbers update again. |
| G20 | Result tab → Export dropdown, with the STEP item disabled (active drawing has no 3D panel). | CSV works; STEP is greyed with a tooltip saying why; clicking it does nothing and no error appears. |
| G21 | Pick: drag right-to-left around one Web in an isometric view, not touching any top plate or flange edge. | Only that Web is selected; the top plate and flanges are not. |

## H. Pick in drawing / Esc

| # | Steps | Expected |
|---|---|---|
| H1 | Result tab → Pick → click 3 solids inside the 3D block, Enter. | The 3 rows are selected (scrolled to) and highlighted; the selection stays after Enter. |
| H2 | Naming tab (3D file) → Pick → click a web solid, then its flange. | The web line row is selected both times; web + flange highlight. |
| H3 | Pick an object with no row (bracket on Naming, top plate…). | Nothing happens, no message; picking continues. |
| H4 | During Pick press Esc. | Picking ends; CAD highlight and grid selection (both tabs) are cleared. |
| H5 | Select rows, click in the drawing area, press Esc (no command). | Highlight off in CAD, no row selected on either tab. |
| H6 | Esc on the grid (not editing a name / not in an open dropdown). | Same as H5. Esc while typing a name only cancels the typing. |
| H7 | Start LINE, then click Pick on the palette. | A message asks to finish or cancel LINE first; the selection is kept. |
| H8 | Click Pick, then click Pick (or Pick in Drawing) again before finishing. | "Already picking…" message; the first pick keeps working. |
| H9 | Press Esc in another palette (e.g. Properties) or hold Esc down. | At most one clear; no slowdown. (Esc in other palettes also clears — accepted.) |
| H10 | Pick 3 HP rows, change Tripping (or Collar / FB height) while the Pick prompt is still active, then press Enter in the drawing. | Status says the change is saved and the 3D rebuilds after picking; after Enter the panel rebuilds once with the new value. Same after ending with Esc (selection cleared, rebuild still runs). |

> 2026-11-05: Naming has no Select panel / Copy buttons and no Position box any more. G1, G5 and G13 now read: panels
> come from Build 3D; change Position on the Input tab, then open Naming ⇒ L/HL renumber; Copy stays in the
> right-click menu and Ctrl+C.
| H11 | Pick → click empty space and drag left→right over a few solids/lines, then right→left. | Window adds only objects fully inside; Crossing adds objects touching the box. Works in plan and in a rotated 3D view. Diagonal members may be over-selected by Crossing (bounding box). |
| H12 | During Pick: Shift + click a selected object; Shift + drag a window; type R, click, then A, click. | Shift (or Remove mode) unselects those rows and their highlight; A returns to adding. Shift on a row that is not selected does nothing. |
| H13 | Turn Ortho (F8) and OSnap (F3) on, set LASTPOINT far from the panel (e.g. draw a line at 0,0). Pick → hold Shift, click empty space, drag. | The window starts exactly where you clicked (no jump to 0,0), corners don't snap; after Enter/Esc, Ortho, OSnap and Shift-override are back as before. |

## I. Separate 3D drawing (2026-11-06)

| # | Steps | Expected |
|---|---|---|
| I1 | Save the 2D drawing, run Build 3D on one panel, then check the source drawing. | `<panel>_3D.dwg` opens as the active drawing and the source is not modified: no `*` in its tab, no Save prompt on close, no new blocks or layers. |
| I2 | Make a change in the 2D drawing without saving (or use a drawing never saved), then click Build 3D. | Build stops before anything is created. The status says `Save this drawing first, then Build 3D.` |
| I3 | Save the 2D drawing, then only zoom or pan, then click Build 3D. | The stop of I2 does NOT appear (view changes do not count as unsaved changes). The build runs. |
| I4 | Select 3 panels in one Build 3D. | One file per panel, each open; the last one is active. A panel that fails or builds nothing does not stop the others, and the status counts them. |
| I5 | Build 3D a panel whose `<panel>_3D.dwg` already exists. | One dialog lists those panels with Update (default), Open, New copy and Cancel. Nothing is lost whichever is pressed. |
| I6 | In I5 press Open. | The existing files open (or switch to them if already open); no build for those panels, and their rows fill the tabs. |
| I7 | In I5 press New copy. | The panel builds into `<panel>_3D_2.dwg` (next free number). Cancel builds nothing and creates nothing. |
| I8 | Keep a `<panel>_3D.dwg` open in AutoCAD, then Build 3D the same panel and press Open. | AutoCAD switches to the open file. It is not opened twice and no error appears. |
| I9 | Save and close a 3D file, then reopen it from the Start tab or File > Open. | Result and Naming fill by themselves. Close it again without changes: no Save prompt. |
| I10 | In a 3D file, erase one solid by hand (ERASE), then reopen the file or switch away and back. | The row is dropped and the status says how many solids are missing. A rebuild brings the solid back. |
| I11 | In a 3D file with corrections and renamed lines, change a value on the Input tab (the button reads Rebuild 3D) and press it. | The same file rebuilds with no selection prompt and no new file. Corrections and names stay. A failed rebuild puts the old Input values back and the status says why. |
| I12 | Build 3D a panel that produces no solids (wrong layers). | No file stays on disk, the status says the panel built nothing, and the other panels still build. |
| I13 | Open an old 3D block made before 2026-11-06 (no panel data). | It is not listed, and the status says to Build it again. |
| I14 | Build a panel from a drawing whose layout has a title block. Open the 3D file's layout tab. | The layout is empty apart from its main viewport; blocks used only there are gone. |
| I15 | Result tab: a Web row and a Closed Plate row show a Kind dropdown with Web and Closed Plate; GE, GB and bracket rows show text only. Change one, then select several and change one. | The panel rebuilds once, each row shows the new kind in blue, Clear Correction returns the automatic kind, and the change survives Save and reopen. |
| I16 | In a 3D file, correct one part and rename one line, Save and close. Back in the 2D drawing, change another part, Save, Build 3D the same panel, press Update. | Same file name opens with the new 2D. The correction and the name stay on the unchanged part. `<panel>_3D.<yyyyMMdd_HHmmss>.bak` is next to it. The Input tab values were not used: the file's own Input and Position were. |
| I17 | In the 2D drawing add a new part, Save, then Update. | The new part builds with automatic results. No count of unmatched edits appears because of it. |
| I18 | Correct or rename a part in the 3D file, delete that part in the 2D drawing, Save, then Update. | The panel builds without the part and the status says `1 edits no longer match the 2D drawing.` (n as counted). |
| I19 | Keep `<panel>_3D.dwg` open with an unsaved change (e.g. a Kind change), then Update it. Run once choosing `Save and Update`, once choosing `Skip this panel`. | A box asks `<panel>_3D.dwg has unsaved changes.` Save and Update: the file is saved, then updated from the 2D, and the unsaved change is still there after the Update. Skip this panel: that panel stops, the status says `<panel>_3D.dwg was skipped: it has unsaved changes.`, the file stays open with its change, the drawing that was active before comes back, and other panels still build. |
| I20 | Keep `<panel>_3D.dwg` open with nothing unsaved (zoom only is fine), then Update it. | The file closes by itself with no prompt, and the updated file opens under the same name. |
| I21 | Update a panel whose saved 2D now builds nothing (move its parts off the panel layers, Save). Check the folder. | The status says the update failed and the old file was restored. `<panel>_3D.dwg` is the old file (same size and date), no `.new` file and no new timestamped `.bak` remain. If the file was open and saved before Update, it is open again. The drawing that was active before comes back. |
| I22 | Update a file that has no panel data, or a file built from another block of the same name. | That panel stops with `... has no panel data; use New copy.` or `... was built from another 2D block; use New copy.` and the old file is not changed. If it was open, it stays open. Two references of one block selected together: one updates, the other is refused, and the updated file stays open and active. |
| I23 | In the same dialog press Open, then repeat with New copy. Also Update a selection where one panel has no 3D file. | Open and New copy behave as in I6 and I7. The panel with no 3D file builds normally with the Input tab values. |
| I24 | Result and Naming: double-click a row on plain text. Then double-click inside the Name box, a Kind / Tripping dropdown and a button. | Plain text zooms to the row (all selected rows if it is one of them). The Name box selects text as usual; dropdowns and buttons do not zoom. |

## J. Create Drawing › Plan View (2026-11-10)

Work on COPIES of a finished sheet (e.g. `CAS-0062242.dwg`, 7D-17P) and of its `_3D.dwg`. Spec: `docs/superpowers/specs/2026-11-10-create-drawing-plan-view-design.md`.

| # | Steps | Expected |
|---|---|---|
| J1 | Result tab in a 2D drawing (no 3D panel): open Create Drawing ▾. | Plan View is greyed; its tooltip says `Open a 3D panel file first`. In a 3D file it is enabled. |
| J2 | In the 3D file run Plan View on the hand-drawn sheet. | The 2D drawing opens and becomes active. The dialog says `{n} hand-drawn items found on the 7D-17P sheet.` with Replace / Keep both / Cancel. Cancel ⇒ nothing changes. |
| J3 | Repeat J2, press **Replace**. Before and after, run `MCG_DumpDrawing` and compare the handles inside `7D-17P_Structure`. | Hand labels, symbols, sections X-X / Y-Y and the hand lashing plan are gone; the tool's ones are drawn. `DETAIL…`, `1A-1A`, `REF. DIM`, `(<>)` dimensions and chain totals are untouched. Handles inside `_Assy` are identical. The 2D drawing has a `*` (not saved). |
| J4 | Run Plan View again (no change in 3D). | No dialog. Exactly one set of tool output (no duplicates). |
| J5 | On a fresh copy press **Keep both**, then run again. | First run: hand items stay, tool items added. Second run: no dialog, still one tool set. |
| J6 | In 3D delete a Tripping Bracket and toggle a Sniped End, save, run Plan View. | The `B` at that bracket is gone; `S` appears/disappears at that end. The section X-X / Y-Y drops a member deleted in 3D. |
| J7 | Move (only) the `_Assy` reference in the 2D file, save, run. Then rotate it 90° (or mirror it), save, run. | Moved: labels, symbols, dims and sections follow the plan. Rotated/mirrored: `The 7D-17P block is rotated or mirrored; Plan View supports unrotated blocks only. Nothing was drawn.` |
| J8 | Run on a raw 2D drawing that has none of the sheet blocks (`CS_horl_up_right`, `thk_sym`, `HP120x6`, `BS 80x6`, `M.D1`, `M.D2`, `Measurements_Tolerances vFuture`, `Rec. for tolerance`). | Blocks are copied from `CAS.dwt`; the status lists them after `Added from CAS.dwt:` and any layer/style created with them after `New with the blocks:`. Existing blocks with the same name are NOT changed. Rename `CAS.dwt` temporarily and run again on a fresh raw copy: the run still works; the status lists the blocks after `Drew blocks:` and any layer created (e.g. `layer Mechanical-AM_3`). |
| J9 | Make the 2D file read-only (file properties), run. | `… is read-only or in use.` Nothing written. |
| J10 | Move the 2D file to another folder, run. | A file dialog `Select the 2D drawing of 7D-17P_Assy` opens. Picking a wrong drawing ⇒ `… does not contain the 2D block …`. Picking the right one works and is remembered (next run does not ask). |
| J11 | Look at `CS_horl_up_right` on Center, Port and Starboard panels. | Symbols sit at every HP/girder passing through a web, on the build-direction side. If ALL are on the wrong side, report it: one flag flips them (ledger Task 6). |
| J12 | `MCG_DumpDrawing` of the result vs the reference dump. | Counts per layer (AM_1 / AM_4 / AM_5 / AM_6 texts and dims) within about ±10 % of the hand sheet. |
| J13 | After a run, press Undo once (U). | The whole Plan View run disappears in ONE step (command name `MCG_PlanView`); hand items erased by Replace come back. |
| J14 | Lock layer `Mechanical-AM_6` in the 2D file, run with Replace. | The run fails with a message; the sheet is exactly as before (hand items and previous tool output still there). Unlock and run again: it works. |
| J15 | Panel with an HP size not in `CAS.dwt` (e.g. HP 140*7 in the Input tab), run. | Status says `Drew blocks: HP140x7.`; the block outline matches the HP section of the 3D (back at x = 0, bulb to +X, down to −140). |
| J16 | After J8 without `CAS.dwt`, compare the tool-drawn `CS_horl_up_right`, `thk_sym`, `M.D1`, `M.D2`, `Rec. for tolerance`, tolerance note with the same blocks in a finished sheet (e.g. `CAS-0062242.dwg`). | Same shapes, layers, colours and texts. |
| J17 | Run Plan View on a copy of the 7D-17P raw 2D, `MCG_DumpDrawing`, then `dotnet run tools/DumpAnalyzer/compare.cs -- <CAS-0062242.dump.json> <result.dump.json>`. | Main plan has only the 2 overall dims (13356.2 width); lashing dims only on the lashing plan (left chain 8.3 mm, top 9.6 mm, points at the holes); X-X/Y-Y start at the plate on the mid cut (15939.8 / 12952.3) with 775 chain above X-X and 2045 chain left of Y-Y; CS symbols never mirrored; labels MiddleLeft centred on members; diagonals + leaders + AM_8 frame present. Name differences follow the Naming tab (accepted). |
| J18 | On a raw 2D drawing: run Plan View, press Undo (U), run Plan View again. Also: make a run fail (lock `Mechanical-AM_6`), unlock, run again. | The second run works; the status lists the blocks again under `Added from CAS.dwt:` / `Drew blocks:`. No `eKeyNotFound`. |
| J19 | 7D-17P: run Plan View, dump, `compare.cs` against `CAS-0062242.dump.json`. | `CS_horl_up_right` 95/95 (no symbol where T1/T2 meet the L webs at the girder ends); B next to the HP (left 3.1 mm, toward the bracket 4.25 mm), not on the bracket body; X-X chain 500 / 775 … / 660.5 and 290 / 15110.5; Y-Y without flange-width dims, with 350.2; lashing 480 / 395.5 / 470 on AM_5 inside the plate; AM_8 frame rules between the groups of the member list, frame height following its lines. |
| J20 | 7D-17P lashing-hole plan after Plan View. | 48 AM_4 chain dims; 290 `(<>)` from the last hole to the right plate edge on the top chain; bottom edge segment of the vertical chain `(<>)`; 480 / 395.5 / 470 on AM_5 inside the plate, halfway to the next hole column/row; 6404 (right, +10.4 mm) and 7966.1 (top, +20.4 mm) from the plate's right reference corner to the COG circle; Ø55 on a middle-lower hole, text at 28°. |

## K. Detail Drawing (2026-11-16)

Work on a COPY of `Plan - CAS-0062239.dwg` (7D-16P) with its `_3D.dwg` next to it (Build 3D first). Spec: `docs/superpowers/specs/2026-11-16-detail-drawing-design.md`. Commands run in the 2D drawing.

| # | Steps | Expected |
|---|---|---|
| K1 | Draw the DET.1 rectangle (closed polyline, `Mechanical-AM_7`) over the plan, run `MCG_DetailPlan`, pick it, click an insertion point. | Block `7D-16P_DET.1` at scale 1: webs with AM_8 lines; HP bulbs on AM_0; collars and scallops present; fittings on AM_2; zigzag lines (AM_4 `amzigzag`) where the rectangle cuts members. `DETAIL 1` (underlined, AM_1 h 50) under the block; `DET.1` (AM_1 h 150) next to the rectangle's top-right corner; the rectangle is kept. Status `DETAIL 1 created from 7D-16P_3D.dwg.` plus any `Notes: …`. |
| K2 | Select the rectangle first, then run `MCG_DetailPlan`. | No selection prompt; the block follows the cursor at once. |
| K3 | Pick a rectangle on another layer, an open polyline, a polyline with an arc, a circle (Detail Plan). | Each is refused with the reason (`must be on layer Mechanical-AM_7`, `is not closed`, `has arcs`, `Select a closed polyline.`) and the prompt repeats. |
| K4 | Press Esc at the selection prompt, during placement, at `Scale <1>:` / `Name <DETAIL n>:`, at the 3D-file choice, and press No in the stale dialog. | `… cancelled. Nothing was drawn.` `-PURGE` lists no new `_DET.` block, no `DET.n` label, and `LIST` / XData of the rectangle shows no `MCG3D_DETAIL`. |
| K5 | Draw a rectangle outside every panel and run. Draw one over an empty area of the panel (no structure). | `The rectangle is not on a panel.` / `No structure inside the boundary. Nothing was drawn.` |
| K6 | Rename or remove layer `Mechanical-AM_8` in a copy, run. | `Detail Plan stopped; the drawing is missing: layer Mechanical-AM_8. Nothing was drawn.` No layer is created. |
| K7 | Change the `_Assy` structure in 2D (e.g. move a web) without rebuilding 3D, run. Then open the 3D file, change a correction without saving, run. | A Yes/No dialog names the reason (`block changed in this drawing after …` / `… has unsaved changes`). No ⇒ nothing drawn. Yes ⇒ the detail is drawn and the status notes the 3D file may be out of date. |
| K8 | Copy `7D-16P_3D.dwg` to `7D-16P_3D_2.dwg` (both built from this drawing), run. | The command line lists both files and asks `Which 3D file [1-2] <1>:`. Enter takes the first; Esc cancels with nothing drawn. |
| K9 | Run `MCG_DetailView` on an AM_7 circle; type scale `2.5` and name `DETAIL A (1:4)`. | Block `7D-16P_DET.2` clipped to the circle, inserted at scale 2.5; title `DETAIL A (1:4)` underlined; `DET.2` label at the circle's upper-right (45°). Enter at both prompts ⇒ scale 1 and `DETAIL n`. Typing `0` or `-1` for scale is refused. |
| K10 | After K1 + K9, run Detail Plan again. | Next number is 3 (Plan and View share the series; hand-made `7D-16P_DET.n` blocks count too). |
| K11 | Whenever a run fails with an error (watch for it during K1–K10). Also replace `7D-16P_3D.dwg` with a non-DWG file of the same name and run. | Error: full exception on the command line and status `… failed: … Nothing was drawn.`. Bad file: `Could not read the 3D file of 7D-16P (…)`. Either way no new block, label or XData. |
| K12 | After a successful run press Undo (U) once. | The whole detail (block, title, label, XData on the boundary) disappears in one step. |
| K13 | On the DET.1 from K1 run `MCG_DetailSection`, pick the DET block, click the start and end of a cut where the hand-drawn 1B-1B is (the line rubber-bands from the start and snaps horizontal/vertical), click the viewing side, click an insertion point. | Two `AM_10` arrows + `1A` (h 30) at the cut ends inside the DET block, pointing to the picked side (also in every other reference of `7D-16P_DET.1`). Block `7D-16P_DET.1_1A-1A` at scale 1: cut deck plate with 1 AM_8 line; 10 mm web with 2 AM_8 lines; HP as a true section; visible bracket edge on AM_0; `Continious symbol` + `(10.0)` (AM_5) on the continuing web; `$` (AM_10) beside the x = 887 end line. Top plate on top. Title `1A-1A` (AM_1 h 50) under it. Status `1A-1A created from 7D-16P_3D.dwg.` plus any `Notes: …`. |
| K14 | Cut a second section on the same DET with a **vertical** line. Then cut one on another DET. | Letter `B`, title `1B-1B  90°ROTATED` (two spaces), section drawn horizontal. On DET.2 the first section is `2A-2A` (letters count per DET; hand-made `7D-16P_DET.1_1C-1C` blocks also take their letter). |
| K15 | Pick a Detail View block; a section block; any other block / entity. Select a DET block first, then run the command. | `Sections are only made on Detail Plans.` / `Select a Detail Plan block (DET.n) made by the tool.`, the prompt repeats. Pre-selected DET ⇒ no selection prompt. |
| K16 | Click the end point on the start point; draw a cut line fully outside the DET boundary; click the viewing side exactly on the cut line. Cut through an area with no structure. | `The cut line has no length…` / `…does not cross the detail. Pick the end point again.` (end point asked again) / `The viewing side is on the cut line…` (only the side asked again). No structure ⇒ `The cut line crosses no structure. Nothing was drawn.` |
| K17 | Press Esc at the DET selection, the start point, the end point, the viewing side, during placement; press No in the stale dialog (K7 setup); Esc at the 3D-file choice (K8 setup). | `Section Detail cancelled. Nothing was drawn.` The DET block shows **no** new arrows / `nA` (marks are not added to the DET definition), `-PURGE` lists no `_DET.1_1x-1x` block, and the next run still uses the same letter. In a drawing without `Continious symbol`, Esc during placement leaves no `Continious symbol` block either. |
| K18 | Rename `Mechanical-AM_10` in a copy, run. Erase the `7D-16P_Assy` reference, run on a remaining DET. | `Section Detail stopped; the drawing is missing: layer Mechanical-AM_10. Nothing was drawn.` / `The 7D-16P_Assy block of DET.1 is no longer in this drawing. Nothing was drawn.` |
| K19 | After a successful section press Undo (U) once. | Section block, its title and the marks inside the DET disappear in one step. |
