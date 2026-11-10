# Plan View — placement rules (measured 2026-11-10)

Measured with `tools/DumpAnalyzer/placement.cs` on the six reference sheets:
7D-15P, 7D-16P, 7D-17P, 7D-16C and 7D-15C at 1:50, 7D-17C at 1:75.

All values are **paper mm**; model = paper × sheet scale. "Plate" = the Top Plate outline of the plan
(`<panel>_Top Plate` inside `<panel>_Assy`), in Model Space. Hand placement varies, so each rule takes the median and
turns it into a systematic rule. "Spread" is min–max across all sheets.

| Constant | Value | Spread | Rule |
|---|---|---|---|
| `LabelTopStiffener` | 24 | 24–52 | HT / BS names of T-direction members: text **BottomCenter** on the member centerline, baseline this far **above** the plate top edge. |
| `LabelTopGirder` | 44 | 19–52 | T# names: same, one row further out. |
| `LabelLeft` | 52 | 36–104 | L# / HL# / BS names of L-direction members: text **MiddleLeft**, this far **left** of the plate left edge, on the member centerline. |
| `ChainFirstOffset` | 22 | 12–56 | First dimension chain: dimension line this far outside the plate edge on the draft's side. |
| `ChainSpacing` | 8 | 7–44 | Every further chain on the same side. |
| `SectionGapY` | 56 | 36–66 | X-X insertion (plate top line of the section) this far above the plate top edge; X = plate min X (measured 0). |
| `SectionGapX` | 77 | 45–108 | Y-Y (rotated 90°) insertion this far left of the plate left edge; Y = plate min Y. |
| `LashingPlanGap` | 97 | 50–138 | Lashing-hole plan: its plate starts this far right of the plan's plate right edge; same Y (measured 0). |
| `MdOffsetX`, `MdOffsetY` | 65, −50 | 52–93, −118 to −36 | `M.D1` from the lashing plan's plate (min X, top edge). |
| `MdSpacing` | 99 | 55–133 | `M.D2` = `M.D1` + this along X. |
| `TitleBelow` | 41 | 15–51 | View titles: MText **TopCenter**, this far below the view's plate bottom edge, X = plate centre (measured 95–165 from min X; centre is my call). |
| `TitleWidth` | 182 | 182 | MText width of the plan title. |
| `MemberListX`, `MemberListY` | 176, 84 | 165–185, 67–85 | Member list MText, **TopLeft**, from the sheet frame origin; width 176. |
| `ToleranceNoteX`, `ToleranceNoteY` | 32.031, 109.714 | exact | `Measurements_Tolerances vFuture` from the frame origin (same on all six). |

## Collisions and how the rules avoid them (my calls)

1. **Top side: dimensions vs. labels.** On the samples, top chains (median 25.5) and HT labels (24) overlap in height;
   the engineer moved them apart by hand. Rule: dimension chains come first from the edge (22, 30, 38…). The label
   rows move outward to stay 8 mm beyond the outermost top chain. `LabelTopStiffener` and `LabelTopGirder` are
   minimums, and the girder row stays 20 mm beyond the stiffener row. X-X then sits 12 mm beyond the girder row,
   minimum `SectionGapY`.
2. **Left side**: the same stacking applies to `LabelLeft` and `SectionGapX`.
3. **Two labels at the same spot** (two pieces with one name, collinear members): one label per name, at the piece
   that reaches the edge or, failing that, the longest piece.
4. **Members that do not reach the plate edge** (spread up to 300 mm on samples: the engineer then wrote the label
   next to the member) still get their label in the edge row/column on their centerline. Simpler, and readable,
   because the row is outside the plan.

## Not measured

- Placement of `B`, `S`, `GE`/`GB`, `OB`. There are too few samples, and S/GB never appear. Rule: 3 mm text,
  **MiddleCenter**, 4 mm paper from the mark point, on the side away from the member it belongs to.
- `CS_horl_up_right`: inserted at the crossing point (the symbol is drawn around its insertion point at scale/50).
