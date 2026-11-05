# Tripping Bracket: placement by 3 × max stiffener spacing (revision 2026-11-05)

Replaces the HP-selection rule in `2026-11-04-tripping-bracket-design.md`. The bracket shape, the placement
side, the Result-tab flip and the layer/colour are unchanged.

## Why

The engineer tested panel 7D-16C, and the old rule (1 bracket per beam segment, under the HP nearest the middle)
was wrong. The class requirement is that **the distance between supports against tripping along a beam must be
≤ 3 × the maximum stiffener spacing**. The engineer listed the HPs that should carry brackets (26875, 26879, 2687B,
2687E, 26889). On analysis, that list only fits counting from low Y, and 7D-16C is a **Center** panel. The engineer
chose (A): accept the result of the Center rule below and adjust on the Result tab where needed.

## Rules (agreed 2026-11-05)

- **R1 Location.** A bracket goes only at an intersection where an **HP passes through the web of a flanged beam**
  (CO cutout). This replaces the "skip outermost beams" rule: on 7D-16C, edge webs 26963 and 26968 get no bracket
  because the HPs stop at them and do not pass through.
- **R2 Beam segment.** A segment is the piece of a beam (web with flange) between two consecutive supports.
  Supports are (a) a continuous flanged beam that cuts across it and (b) **the ends of the beam itself**, whatever
  it ends on (girder, faceplate, edge web). This replaces the earlier "both ends must be cut by a continuous beam"
  condition, so beam x≈4226 (ending at a faceplate) now counts.
- **R3 Spacing (option b).** L = 3 × s_max, where s_max is the largest spacing between adjacent through-HPs along
  the same segment, taken as the maximum over all segments of the panel. Start at the support at the **chiều đổ
  end** of the segment. Choose the **farthest** HP whose distance from the current support is ≤ L + 1 mm
  (tolerance: 7D-16C has 5332 − 3007 = 2325 = L exactly). That HP becomes the new support. Repeat until the
  distance from the current support to the far end of the segment is ≤ L. If no HP fits within L, take the
  nearest next HP. The "≥ 3 HPs" rule is dropped: a segment no longer than L gets no bracket.
- **R4 Counting direction (chiều đổ).**
  - X: from small X to large X.
  - Port: Y from top to bottom.
  - Starboard: Y from bottom to top.
  - **Center**: from both ends toward the panel centre (TopPlate centroid). A segment that crosses the centre is
    split there, and each half walks from its outer end, choosing only HPs inside its own half. If the gap
    between the last pick on each side is still > L, add a greedy step across the gap. **Picks close together in
    the middle are acceptable**, because the user can edit them on the Result tab.
- **R5 Manual override (option A).** A new **Tripping: Auto / On / Off** control on each **HP row** of the Result
  tab, stored in `PartCorrection` on the HP's SourceKey:
  - **On** places a bracket at every R1 intersection of that HP.
  - **Off** removes every bracket on that HP.
  - The automatic picks are **not recalculated** (engineer, 2026-11-05).
  - Removing one bracket still uses the existing per-row delete, and ⇄ still flips the side.

## Expected result on 7D-16C (Center, centroid Y≈7678, L = 3 × 775 = 2325)

| Segment | Picks |
|---|---|
| Lower region, beams x≈4226, 6166, 8106, 10046, 11986 (Y 1105–10532) | 3007, 5332, 7651 (lower half) + 8385 (upper half), i.e. 4 × 5 = 20 brackets |
| Upper region, beam x≈11986 (Y 10682–13940) | 12260 |
| Upper region, beam x≈10046 (Y 10682–13088) | 11004 |
| Upper region, beams x≈6166 and x≈8106 | no bracket (segment ≤ L) |

HP 7651 is only about 27 mm from the centroid, so a small shift of the centroid moves it into the other half.
This is accepted.

## Approaches considered

- **Count every 3rd HP (a)** vs **distance ≤ 3 × max (b)**. The engineer chose (b) because it holds when HP
  spacing is uneven.
- **Override on the HP row (A)** vs **an AutoCAD pick command (B)**. The engineer chose A because it reuses the
  correction mechanism. It works per HP, not per intersection; per-intersection removal stays with the existing
  delete.

## My calls (left to the builder)

- s_max is the maximum over the whole panel, not per segment. A per-segment value breaks down on segments with
  only 1–2 HPs.
- Distances are measured along the beam axis between HP centrelines and support centrelines (the girder web
  centre, or the beam's end point).
- The greedy algorithm goes into `TrippingBracketRules` (pure code, unit tested with the 7D-16C numbers above).
  `PickHp` and `MIN_HP` are removed.
- The report line for each bracket gives the distance from the previous support and the value of L.
