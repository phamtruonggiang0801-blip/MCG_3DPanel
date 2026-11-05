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
