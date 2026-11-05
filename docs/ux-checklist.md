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
| A1 | Build panel P1 in drawing A. Open a new drawing B, copy in panel P2 and build it. Click rows and ⌖. | B's rows highlight. ⌖ zooms. No error. |
| A2 | After A1, switch back to drawing A without building. | The status bar says the rows belong to drawing B. Nothing stays highlighted in B. |
| A3 | After A2, switch to B again. | The normal summary returns, and the selected row is highlighted again without clicking. |
| A4 | Close the drawing whose rows are listed. | The rows disappear and the status says "Drawing closed…". AutoCAD does not crash. |
| A5 | From another drawing, change Tripping / ⇄ / Sniped End / Delete / Clear Correction on a row. | The status says "This panel is in drawing X — switch to it to change P". Nothing is saved, and the control returns to its real state. |
| A6 | Close the active drawing while one of its rows is highlighted, then click a row of another open drawing. | No crash. The status explains what happened. |

## B. Lifecycle

| # | Steps | Expected |
|---|---|---|
| B1 | Build, then change a correction (Tripping On, ⇄, Sniped End). | Exactly one rebuild. The same row stays selected and highlighted, and the view does **not** zoom. |
| B6 | Focus the Tripping dropdown, press Esc, then press ↑ / ↓, or scroll the mouse wheel over it. | Nothing changes and there is no rebuild. The wheel scrolls the grid. |
| B2 | Ctrl+Z after a rebuild, then click a row. | The status says "Solid no longer exists…". No exception. |
| B3 | Save, close, reopen the drawing, then Load 3D. | The rows return, corrections are kept, and highlight works. |
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
| F5 | Load 3D on an old drawing whose saved spec is `120*6`. | It rebuilds with height 120 and no error. |

## G. Naming tab

| # | Steps | Expected |
|---|---|---|
| G1 | Naming tab → Select panel… → pick a 2D panel (no 3D). | One row per Web/HP line and per flat bar, names T#/L#/HT#/HL#/BS; clicking a row highlights every piece of the line. |
| G2 | Rename a line (type `T6a`, Enter), Ctrl+S, close and reopen the drawing, Select panel… again. | The name `T6a` is still there (blue); the other numbers are unchanged. |
| G3 | Select 12 BS rows, type `BS1` on one of them and press Enter. | All 12 rows get `BS1`; the status shows the count. |
| G4 | Right-click → Reset to auto on renamed rows. | They return to the generated names (black). |
| G5 | Change Position to Starboard. | L/HL numbers reverse (top → bottom); T/HT stay; your own names stay. |
| G6 | Select panel in drawing A, switch to drawing B, rename a row. | Nothing is saved; the status names drawing A. |
| G7 | Build 3D (Keep), then open Naming; then change a Kind on Result (rebuild). | Naming lists the built panel and highlights its 3D solids; after the rebuild the rows and your names are still there. |
| G8 | Type a name, then click another row without pressing Enter. | Only the edited row is renamed. |
| G9 | Look at T1 and a web without flange, an HP and a BS row. | `T1 WEB 374x6.0/FLANGE 100x20.0`, `T2 WEB 374x6.0` (if no flange), `HL1 HP 120x6.0`, `BS FB 80x6.0`; only Panel/Name/Kind columns. |
| G10 | Edit `T7 WEB …` to `T6a WEB …` (or just `T6a`), Enter. | Name becomes `T6a WEB 374x6.0/FLANGE …`; the size is never doubled. |
| G11 | Select 2 stray rows → right-click Del Name. Then Restore deleted names (N). | Rows disappear and the rest renumber; Restore brings them back and renumbers again; survives Ctrl+S + reopen. |
| G12 | A BS line split far apart (e.g. 947 + 8428 on one axis). | One row. |
| G13 | Select panel… with an invalid value on the Input tab. | A message asks to fix the Input tab; nothing is selected. |
| G14 | Look at the columns. | Name (editable, e.g. `T1`) and Size (read-only, e.g. `WEB 374x6.0/FLANGE 100x20.0`) are separate; Copy pastes them in separate columns. |
| G15 | Click a Web row that has a flange (2D and after Build/Keep). | The web and its flange(s) highlight together. |
| G16 | Select T3 and T4 → right-click Merge. Then Unmerge. | One row `T3`, Kind `Web (merged)`, T5 becomes T4…; Unmerge splits and renumbers back. Merging Web with HP, or T with L, is refused with a message. Survives Ctrl+S + reopen. |

## H. Pick in drawing / Esc

| # | Steps | Expected |
|---|---|---|
| H1 | Result tab → Pick → click 3 solids inside the 3D block, Enter. | The 3 rows are selected (scrolled to) and highlighted; the selection stays after Enter. |
| H2 | Naming tab (2D panel from Select panel) → Pick → click a web polyline, then its flange. | The web line row is selected both times; web + flange highlight. |
| H3 | Pick an object with no row (bracket on Naming, top plate…). | Nothing happens, no message; picking continues. |
| H4 | During Pick press Esc. | Picking ends; CAD highlight and grid selection (both tabs) are cleared. |
| H5 | Select rows, click in the drawing area, press Esc (no command). | Highlight off in CAD, no row selected on either tab. |
| H6 | Esc on the grid (not editing a name / not in an open dropdown). | Same as H5. Esc while typing a name only cancels the typing. |
| H7 | Start LINE, then click Pick on the palette. | A message asks to finish or cancel LINE first; the selection is kept. |
| H8 | Click Pick, then click Pick (or Pick in Drawing) again before finishing. | "Already picking…" message; the first pick keeps working. |
| H9 | Press Esc in another palette (e.g. Properties) or hold Esc down. | At most one clear; no slowdown. (Esc in other palettes also clears — accepted.) |
| H10 | Pick 3 HP rows, change Tripping (or Collar / FB height) while the Pick prompt is still active, then press Enter in the drawing. | Status says the change is saved and the 3D rebuilds after picking; after Enter the panel rebuilds once with the new value. Same after ending with Esc (selection cleared, rebuild still runs). |

> 2026-11-05: Naming has no Select panel / Copy buttons and no Position box any more. G1, G5 and G13 now read: panels
> come from Build 3D / Load 3D; change Position on the Input tab, then open Naming ⇒ L/HL renumber; Copy stays in the
> right-click menu and Ctrl+C.
| H11 | Pick → click empty space and drag left→right over a few solids/lines, then right→left. | Window adds only objects fully inside; Crossing adds objects touching the box. Works in plan and in a rotated 3D view. Diagonal members may be over-selected by Crossing (bounding box). |
| H12 | During Pick: Shift + click a selected object; Shift + drag a window; type R, click, then A, click. | Shift (or Remove mode) unselects those rows and their highlight; A returns to adding. Shift on a row that is not selected does nothing. |
