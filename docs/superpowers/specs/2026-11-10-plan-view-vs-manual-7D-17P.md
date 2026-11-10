# Plan View — tool vs manual sheet, 7D-17P (2026-11-10)

Compared dumps:
- **manual**: `…\400280826\CAS-0062242.dump.json`
- **tool**: `C:\Users\truonph\Desktop\MCG\AutoCAD\Drawing1.dump.json`

Same panel. `_Assy` is inserted at the same point in both (5019.76, 22476.66), so every entity can be compared one to one.
Comparison tool: `tools/DumpAnalyzer/compare.cs`. Distances are **paper mm** at 1:50.

## 1. What already matches

| Item | Result |
|---|---|
| `A1`, `CAS_HEAD`, `_Assy`, `Measurements_Tolerances vFuture` | Same position (0.0 mm) |
| `CS_horl_up_right` at HP × web crossings | 95/95 within 0.1 mm, same rotation (90°) |
| OB / OB1 | 23/23 and 4/4, same set (positions differ, see 3.7) |
| HT positions in section X-X | Same pitch 775, same members (HT1 … HT21) |
| Overall Top Plate length | 16298.8, same points and the same `±4 TOP PLATE` text |
| Layers, text heights (250 / 150 / 175), `AM_ISO$0`, DIMSCALE 50 | Same |

## 2. Counts (manual / tool)

| Layer / type | Manual | Tool |
|---|---|---|
| Text AM_1 (T/L) | 9 | 10 (`L8` extra) |
| Text AM_6 (HT/HL/BS/B/OB/GE) | 130 | 107 |
| — `B` | 66 | 30 |
| — `BS` | 13 | 20 |
| — `HT` / `HL` | 21 / 2 | 23 / 4 |
| — `GE1` / `GE` | 1 / 0 | 0 / 2 |
| Dim AM_4 (lashing) | 48 | 108 |
| Dim AM_5 (structure) | 60 | 97 |
| `CS_horl_up_right` | 95 | 110 |
| `thk_sym` | 49 | 21 |
| Lines/polylines AM_7 (diagonals) | 4 | 0 |
| Leaders `posb` | 2 | 0 |
| `BS_symbol` | 1 | 0 |
| AM_8 triple frame at the bottom | 3 polylines + 9 lines | 0 |

## 3. Differences, cause, fix

### 3.1 Extra members in names and sections (input from the Naming tab)

| Tool label | What it is in the 2D panel | Manual |
|---|---|---|
| `L1` (y 9798) | The tool sees a separate L "web" line formed by a girder-end / bracket piece (AM_0, 557 × 355) near the bottom edge. | No label. The manual's `L1` is the next line (flanged edge girder at y 9994). **Every L number is therefore shifted by one, and `L8` appears.** |
| `HL2` (y 9783) | Short HP stub, 358 long, outboard of T2. | No label |
| `HL3` (y 10492) | HP, 2181 long, collinear with an FB 12388 long on the same line. | One label `BS` for the whole line |
| `HT22` (x 20695) | HP stub, 176 long. | No label |
| 4 × `BS` (T-dir) | FB stubs 340–434 long at the plate edges. | No label (only the long one, 11745, is labelled) |

The same stubs also appear as extra profiles in the sections:
- X-X: 5 BS + 3 HP extra.
- Y-Y: 2 HP + 3 BS + 1 web extra.

**Fix (Naming rules, used by both the Naming tab and Plan View):**
1. A line shorter than a threshold is a stub. It gets no auto name and does not appear in labels or sections. Proposed threshold: 1000 mm (all named members on the reference are ≥ 11 m; stubs are 176–557 mm).
2. An HP piece collinear with a longer FB line belongs to that FB line, so the line is `BS`.
3. Girder-end and bracket pieces never start a Web line of their own.

### 3.2 Label style and position

The manual uses `MiddleLeft` for every label, with the text centred over the member (insertion = centre − ½ text width). The rows on 7D-17P:

| Label | Manual | Tool now |
|---|---|---|
| T girder | 21.0 mm above the plate top (text middle) | BottomCenter, 44 |
| HT | 25.2 above | BottomCenter, 24 |
| BS (T-dir) | 31.8 above | BottomCenter, 24 |
| L girder | 35.8 left of the plate | MiddleLeft, 62 (pushed out by chains) |
| HL / BS (L-dir) | 42.6 left | MiddleLeft, 62 |

**Fix:**
- Write labels as `MiddleLeft`, with the insertion point = centre − half the text width. The width is measured by AutoCAD on the created text, so the entity matches the hand-made ones.
- Re-measure the rows on all six sheets using the **alignment point**. The table of 2026-11-10 used the text position, so its label values are wrong.

### 3.3 Dimensions: where they go

On the manual sheet the main plan carries **only** the two overall dimensions.

| Source | Manual | Tool now |
|---|---|---|
| Structure drafts along X (775 HP pitch, …) | Chain **above section X-X** | Around the plan |
| Structure drafts along Y (2045 L pitch, 500/1045 BS pitch) | Chain **left of section Y-Y** | Around the plan |
| Lashing-hole drafts | **Only on the lashing-hole plan**: top chain (≈ 9.5 mm above its plate) + right chain | On the main plan **and** the lashing plan (54 + 54) |
| Lashing drafts that end on a plate edge (140, 480, 395.5) | Layer AM_5 | AM_4 |
| Overall length / width | Bottom 24.7 mm, left 13.2 mm | Bottom 46, left 54 (pushed by chains) |
| Overall width | 13356.2, measured to the **top-edge vertex nearest the left edge** | 13392.3 (bounding box) |

The section dimensions also carry plate-end and girder dimensions, not only the pitch: X-X has 290, 15110.5, 60, 59.9, 400, 660.5; Y-Y has 60, 59.8, 400.1. Five of them use oblique ticks (`DIMBLK1/2 = _Oblique`, `DIMSAH`). Only 27 of 60 AM_5 dims have DIMTMOVE = 1; the tool sets it on all of them.

**Fix:**
- Route structure drafts to the sections and lashing drafts to the lashing plan.
- Put a lashing draft on AM_5 when it ends on a plate edge.
- Add the plate-end and girder dimensions to the sections, with oblique ticks as the manual does.
- Measure the overall width on the plate outline, not the bounding box.

### 3.4 Sections

| | Manual | Tool now |
|---|---|---|
| X-X origin | `_Assy` origin X, 36.2 mm above the plate top | Plate min X, 61 mm above |
| Y-Y origin | 45.4 mm left of the plate, Y = plate min Y | 77 mm |
| Plate length in section | 15939.8 (X-X), 12952.3 (Y-Y) = the plate **at the cut line** | Bounding box (16298.9 / 13392.3) |
| Profile insertion | At the **back face** of the HP: centre − t/2, or centre + t/2 when mirrored | At the centre |
| HP bulb direction | Mirrored (sx = −1) where the bulb faces −X (first HP of X-X, first of Y-Y) | Never mirrored |
| `thk_sym` | One per member at the section's plate line (49), rotation = section, mirrored like the profile | One per plate rectangle, at its centre (21) |

**Fix:**
- Section origin = `_Assy` origin.
- Plate length = intersection of the plate outline with the cut line.
- Profile insertion at the back face; mirror from the HP bulb direction in 3D.
- `thk_sym` per member at the plate line.

### 3.5 Continuous symbol `CS_horl_up_right`

- **Mirroring.** On all six reference sheets every symbol has sx = 1; the rotation follows the continuous member. The tool mirrors 44 of them. **Fix:** never mirror.
- **15 extra symbols** where T1/T2 cross the L webs (and BS/L cross the T webs). The manual has none at girder × girder crossings. See question 2.

### 3.6 Symbols B / GE

- **B.** The manual's 66 B = tripping brackets **plus** HP-end brackets (Result row Kind `B`, solid type `HpEndBracketB`). The tool marks only tripping brackets (30). **Fix:** add HP-end brackets.
- **B placement.** Manual: `MiddleLeft`, just left of the HP, under the web. Tool: `MiddleCenter` at the bracket centre.
- **GE.** Manual `GE1` (one label); tool `GE` from the Kind column (two labels). See question 3.

### 3.7 OB / OB1 placement

The manual puts the text ≈ 600 mm (12 mm paper) **outside** the bracket, away from the plate, as `MiddleLeft`. The tool puts it at the bracket centre.

**Fix:** offset on the outboard side, `MiddleLeft`.

### 3.8 Lashing-hole plan, M.D1 / M.D2, titles, boxes

**Lashing plan.** On 1:50 sheets its left edge sits at ≈ 513 mm from the frame origin (4 of 5 sheets). A wide panel is pushed left so the right margin stays ≥ 45.6 mm (7D-17P: 469.4).
- Fix: x = min(513, 841 − 45.6 − width).
- The tool uses plan + 97 mm, which is 46.8 mm off on 7D-17P.

**M.D1 / M.D2.**
- The pair is centred on the lashing plan's centre (all six sheets, ±2 mm), 50 mm below its top edge.
- The spacing is freehand (44–133 mm). Proposal: 0.4 × plate width.

**Diagonals (missing).**
- The manual draws both plate diagonals of the lashing plan on AM_7, plus a short corner tick.
- It adds 2 `posb` leaders from M.D1 / M.D2 to the diagonals.
- Fix: draw them.

**Titles.** The exact manual MText contents:
- `\pxqc;{\L\C161;PLAN VIEW\P\H0.7x;\l\C2;(ALL FITTINGS SHOWN ARE FOR REFERENCE LINE)}`, width 182.2
- `\pxqc;{\L\C161;LASHING HOLES PLAN}`, width 136.1

The plan title sits 50.7 mm below the plate (under the overall dimension).

**Member list.** The manual adds extra spaces and font codes. Lines are the same once the naming fix (3.1) removes the stub `L1`, which the tool lists as `L1 : WEB 374x6.0` with no flange.

**Rec. for tolerance.** At the overall dimension text: vertical one rotated 90°. The tool follows the dims, so the position changes with 3.3.

### 3.9 Manual items the tool does not make

| Item | Proposal |
|---|---|
| `BS_symbol` (AM_8, one, at the top end of the long T-dir BS, rot 270°) | See question 4 |
| AM_8 triple rectangle + 9 lines at the bottom (x 8637–16420, y 1104–4175 model) | See question 4 |
| 9 empty MText on AM_0 (h 75), 1 DBPoint, 2 MText on frozen AM_9 | Leftovers. Ignore. |

## Decisions (engineer, 2026-11-10)

1. **Naming: no change.** Plan View follows the names the user set in the Naming tab. The name differences in 3.1 come
   from testing and are accepted.
2. **Girder × girder continuous symbols: keep.** The reference sheet is probably missing them.
3. **GE:** write the Kind value (`GE`) for now. No numbering.
4. **AM_8 triple frame** = the frame holding the structure spec notes, around the member list. The tool draws it.
   `BS_symbol`: not used for now.
5. **Section cut line:** at the middle of the panel. The user may edit the section afterwards.

## 4. Achievable target

- **Exactly the same:** every entity type and count, text strings, layers, styles, justification, dimension intervals and dimension texts, symbol positions and rotations, section contents. These all follow rules.
- **Close, not exact:** title, M.D1 / M.D2, lashing-plan offsets and row offsets. They are freehand on the reference sheets (spread up to 90 mm between sheets). They follow the rules above, and the target on 7D-17P is ±2 mm.

## 5. Order of work

1. Naming rules (3.1). Every later count depends on them.
2. Dimension routing and the overall width (3.3).
3. Sections: origin, cut-line length, back-face insertion, mirroring, `thk_sym` (3.4).
4. Labels `MiddleLeft` and re-measured rows (3.2), OB / B placement (3.6–3.7), CS never mirrored (3.5).
5. Lashing plan, M.D, diagonals and leaders, titles (3.8).
6. Re-run `compare.cs` on 7D-17P. Repeat on 7D-16P and 7D-15C (sheets without hand annotation: dump the 2D, run the tool, compare with the manual sheet).
