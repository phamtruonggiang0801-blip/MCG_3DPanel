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
| I15 | Result tab: a Web row and a Closed Plate row show a Kind dropdown with Web and Closed Plate; GE, GB and bracket rows show text only. Change one, then select several and change one. | The panel rebuilds once, each row shows the new kind in blue, Reset returns the automatic kind, and the change survives Save and reopen. |
| I16 | In a 3D file, correct one part and rename one line, Save and close. Back in the 2D drawing, change another part, Save, Build 3D the same panel, press Update. | Same file name opens with the new 2D. The correction and the name stay on the unchanged part. `<panel>_3D.<yyyyMMdd_HHmmss>.bak` is next to it. The Input tab values were not used: the file's own Input and Position were. |
| I17 | In the 2D drawing add a new part, Save, then Update. | The new part builds with automatic results. No count of unmatched edits appears because of it. |
| I18 | Correct or rename a part in the 3D file, delete that part in the 2D drawing, Save, then Update. | The panel builds without the part and the status says `1 edits no longer match the 2D drawing.` (n as counted). |
| I19 | Keep `<panel>_3D.dwg` open with an unsaved change, then Update it. | That panel stops, the status says `Save or close <panel>_3D.dwg first, then Update.`, the file stays open with its change, and other panels still build. |
| I20 | Keep `<panel>_3D.dwg` open with nothing unsaved (zoom only is fine), then Update it. | The file closes by itself with no prompt, and the updated file opens under the same name. |
| I21 | Update a panel whose saved 2D now builds nothing (move its parts off the panel layers, Save). Check the folder. | The status says the update failed and the old file was restored. `<panel>_3D.dwg` is the old file (same size and date), no `.new` file and no new timestamped `.bak` remain. |
| I22 | Update a file that has no panel data, or a file built from another block of the same name. | That panel stops with `... has no panel data; use New copy.` or `... was built from another 2D block; use New copy.` and the old file is not changed. |
| I23 | In the same dialog press Open, then repeat with New copy. Also Update a selection where one panel has no 3D file. | Open and New copy behave as in I6 and I7. The panel with no 3D file builds normally with the Input tab values. |
