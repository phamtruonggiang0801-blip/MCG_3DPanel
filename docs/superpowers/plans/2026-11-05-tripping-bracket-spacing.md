# Tripping Bracket by 3 × Max Stiffener Spacing — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Place tripping brackets on every HP that a greedy walk picks along each beam segment, so that supports are spaced ≤ 3 × the panel's maximum stiffener spacing, and let the user force brackets on or off per HP from the Result tab.

**Architecture:** The pure rules (`TrippingBracketRules`) gain a greedy spacing picker and a max-spacing helper, both unit tested with 7D-16C numbers. `TrippingBracketBuilder` becomes two-pass: it collects all segments with their through-HPs, computes L panel-wide, and then picks per segment. Beam ends become supports, and the outermost-beam rule is removed. The per-HP override is a new `PartCorrection.Tripping` (bool?) with a button that cycles through 3 states on HP rows of the Result tab.

**Tech Stack:** C# net8.0-windows, AutoCAD .NET, WPF, xUnit (pure files linked into `tests/MCG_3DPanel.Tests`).

**Spec:** `docs/superpowers/specs/2026-11-05-tripping-bracket-spacing-design.md`

## Global Constraints

- L = 3 × s_max. Picking tolerance is 1 mm (`SPACING_TOL = 1.0`).
- Brackets go only where an HP passes through a flanged beam web (`ClassifyUtil.CrossesThrough`). The outermost-beam rule is removed.
- Segment supports are continuous flanged beams that cut across the beam, plus the run's own ends.
- Counting direction:
  - X: from small X to large X.
  - Port: Y from top to bottom.
  - Starboard: Y from bottom to top.
  - Center: from both ends toward the TopPlate centroid. Picks close together in the middle are accepted.
- Manual override: Auto (null) / On (true) / Off (false) per HP. The automatic picks are not recalculated.
- The bracket shape, side, ⇄ flip, delete, layer and colour are unchanged.
- Pure files must not `using Autodesk.*`.
- Debug build must stay green: `dotnet build MCG_3DPanel.csproj -c Debug`.

## Review Focus

1. **One irregular gap inflates L for the whole panel.** If one segment has a missing HP, its spacing (e.g. 1550) becomes s_max and every segment gets too few brackets. The expected behaviour is that the report states L, so the user can see it. Test `MaxSpacing_TakesPanelMax` pins that the max is taken across segments.
2. **An HP exactly at L.** 5332.6 − 3007.6 = 2325 = L must be picked. Pinned by `Pick_Starboard_7D16C`.
3. **Center split outside the segment.** A segment wholly on one side of the centre must walk from its chiều đổ end and must not split. Pinned by `Pick_Center_UpperBeam10046`.
4. **No HP within L of the support.** Take the next HP rather than loop forever. Pinned by `Pick_NoHpWithinL_TakesNearestNext`.
5. **An HP exactly on the Center split line.** It belongs to the lower half (s ≤ split). Pinned by `Pick_Center_HpAtSplit_GoesToLowerHalf`.

---

### Task 1: Spacing rules (pure)

**Files:**
- Modify: `Services/Builders/TrippingBracketRules.cs`. Remove `MIN_HP` and `PickHp`. Change `Segments` and update the class doc comment to the new rule.
- Test: `tests/MCG_3DPanel.Tests/TrippingBracketRulesTests.cs`. Delete the 3 `PickHp_*` tests.

**Interfaces:**
- Produces:
  - `public const double SPACING_TOL = 1.0;`
  - `public static List<(double lo, double hi)> Segments(IReadOnlyList<double> cuts, double runLen, double endTol)`. Supports are the cuts plus `0` and `runLen`. A run end is added only if no cut lies within `endTol` of it. Values within `TIE_TOL` are deduplicated. A segment lies between consecutive supports.
  - `public static double MaxSpacing(IEnumerable<IReadOnlyList<double>> segmentHpS)`. Returns the max adjacent difference of each sorted list, taken over all lists. Returns 0 if no list has 2 or more values.
  - `public static List<int> PickBySpacing(IReadOnlyList<double> s, double lo, double hi, double L, int startSign, double? split)`. `s` is sorted ascending and strictly inside (lo, hi). It returns indices in ascending order.

- [ ] **Step 1: Write the failing tests**

The 7D-16C lower region is `Lower = {1457.6, 2232.6, 3007.6, 3782.6, 4557.6, 5332.6, 6107.6, 6876.6, 7651.6, 8385.6, 9160.6, 9935.6}`, with lo = 995, hi = 10607 and L = 2325.

```csharp
[Fact] public void Pick_Starboard_7D16C()   // startSign +1, split null
    => Assert.Equal(new[] { 3007.6, 5332.6, 7651.6, 9935.6 }, Picked(Lower, 995, 10607, +1, null));
[Fact] public void Pick_Port_7D16C()        // startSign −1: walks from hi down
    => Assert.Equal(new[] { 1457.6, 3782.6, 6107.6, 8385.6 }, Picked(Lower, 995, 10607, -1, null));
[Fact] public void Pick_Center_7D16C()      // split 7678 → lower half up, upper half down
    => Assert.Equal(new[] { 3007.6, 5332.6, 7651.6, 8385.6 }, Picked(Lower, 995, 10607, +1, 7678));
[Fact] public void Pick_Center_UpperBeam10046()   // split below lo ⇒ ignored, startSign −1
    => Assert.Equal(new[] { 11004.0 }, Picked(new[] { 11004.0, 11485.6, 12260.6, 13035.6 }, 10607, 13088, -1, 7678));
[Fact] public void Pick_Center_UpperBeam11986()
    => Assert.Equal(new[] { 12260.6 }, Picked(new[] { 11004.0, 11485.6, 12260.6, 13035.6, 13800.6 }, 10607, 13940, -1, 7678));
[Fact] public void Pick_SegmentNotLongerThanL_None()
    => Assert.Empty(Picked(new[] { 11004.0, 11485.6 }, 10607, 12241, +1, null));
[Fact] public void Pick_NoHpWithinL_TakesNearestNext()
    => Assert.Equal(new[] { 3000.0 }, Picked(new[] { 3000.0 }, 0, 4000, +1, null));
[Fact] public void Pick_Center_HpAtSplit_GoesToLowerHalf()
    => Assert.Equal(new[] { 2000.0, 5000.0 }, Picked(new[] { 2000.0, 5000.0 }, 0, 7000, +1, 5000));
[Fact] public void MaxSpacing_TakesPanelMax()
    => Assert.Equal(775.0, TrippingBracketRules.MaxSpacing(new[] { new[] { 1.0, 300.0 }, new[] { 3007.6, 3782.6, 4557.6 } }), 6);
[Fact] public void Segments_RunEndsAreSupports()
    => Assert.Equal(new[] { (0.0, 847.0), (847.0, 10459.0), (10459.0, 11300.0) }, TrippingBracketRules.Segments(new List<double> { 847, 10459 }, 11300, 20));
[Fact] public void Segments_RunEndNearCut_NotDuplicated()
    => Assert.Equal(new[] { (-5.0, 847.0), (847.0, 1000.0) }, TrippingBracketRules.Segments(new List<double> { -5, 847 }, 1000, 20));
```

The helper `Picked(s, lo, hi, sign, split)` calls `PickBySpacing(s, lo, hi, 2325, sign, split)` and maps the returned indices back to values. Update the existing `Segments_*` tests to the new signature with `runLen` equal to the last cut and `endTol = 20`. Their expected results stay the same, except `Segments_RequireCutAtBothEnds`, which becomes `Segments_SingleCut_SplitsRun`: `({500}, 1000, 20)` gives `[(0,500), (500,1000)]`.

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/MCG_3DPanel.Tests`
Expected: compile errors for `PickBySpacing`, `MaxSpacing` and the `Segments` overload.

- [ ] **Step 3: Implement `PickBySpacing`, `MaxSpacing` and the new `Segments`**

The walk runs from a support `cur` toward the far end `end`, in direction `dir`, over the candidate set `C`:

```
while |end − cur| > L + SPACING_TOL:
    ahead = candidates of C strictly beyond cur in dir
    if none: break
    within = those with |s − cur| ≤ L + SPACING_TOL
    pick = within.Any ? the farthest of within : the nearest of ahead
    add pick; cur = s[pick]
```

- If `split` is null or not in (lo, hi):
  - `startSign ≥ 0` walks from lo toward hi over all candidates.
  - `startSign < 0` walks from hi toward lo.
- Otherwise:
  - The lower half walks from lo toward hi, using only candidates with s ≤ split, and end = hi.
  - The upper half walks from hi toward lo, using only candidates with s > split, and end = lo.
- Return the distinct indices, sorted.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test tests/MCG_3DPanel.Tests`
Expected: all tests pass.

- [ ] **Step 5: Commit**

`git commit -m "feat(tripping-bracket): greedy 3x max spacing picker + run ends as supports"`

---

### Task 2: Per-HP Tripping override (correction + Result tab)

**Files:**
- Modify: `Services/Corrections/PartCorrection.cs`. Add `public bool? Tripping { get; set; }` (null = Auto, true = On, false = Off) and include `Tripping == null` in `IsEmpty`.
- Modify: `UI/ResultRow.cs`. Add `public bool CanTrip { get; set; }` and `public string TripLabel { get; set; }`. Set them only for rows where `p.Type == PartType.HP` and `!s.Deleted`. The label is `"Auto"`, `"On"` or `"Off"`, taken from `corr?.Tripping`.
- Modify: `UI/PanelResultView.xaml`. Add a new `DataGridTemplateColumn Header="Tripping"` after "Sniped End". It holds one `Button Content="{Binding TripLabel}"`, visible via `CanTrip`, with `Click="BtnTrip_Click"`, the same height and font size as the snip buttons, and the tooltip "Tripping bracket on this HP: Auto → On (every beam it passes through) → Off. Click to change and rebuild."
- Modify: `UI/PanelResultView.xaml.cs`. `BtnTrip_Click` cycles `c.Tripping` null → true → false → null and then calls `CorrectionStore.Set` and `Rebuild(row)`, following the same pattern as `BtnFlip_Click`.
- Test: `tests/MCG_3DPanel.Tests/CorrectionJsonTests.cs`.

**Interfaces:**
- Produces: `PartCorrection.Tripping` (bool?), keyed by the HP part's SourceKey. Task 3 reads it.

- [ ] **Step 1: Write the failing test.** `RoundTrip_Tripping`: serialise `{ ["218DA#0"] = new PartCorrection { Tripping = true }, ["218E4#0"] = new PartCorrection { Tripping = false } }` and deserialise it again. Assert true and false respectively. Also assert that `new PartCorrection { Tripping = false }.IsEmpty` is false.
- [ ] **Step 2: Run** `dotnet test tests/MCG_3DPanel.Tests`. Expected: a compile error, because `Tripping` is undefined.
- [ ] **Step 3: Implement** the field and the UI parts listed above.
- [ ] **Step 4: Run** `dotnet test tests/MCG_3DPanel.Tests` and expect PASS. Then run `dotnet build MCG_3DPanel.csproj -c Debug` and expect 0 errors.
- [ ] **Step 5: Commit** `feat(tripping-bracket): per-HP Auto/On/Off override on Result tab`

---

### Task 3: Builder — two-pass spacing placement

**Files:**
- Modify: `Services/Builders/TrippingBracketBuilder.cs`.

**Interfaces:**
- Consumes: `Segments(cuts, runLen, END_TOL)`, `MaxSpacing`, `PickBySpacing`, `SPACING_TOL` (Task 1), and `PartCorrection.Tripping` (Task 2).

- [ ] **Step 1: Restructure `Build`**
  - Remove `IsOutermost`, `EDGE_TOL` and the per-segment `PickHp` call.
  - **Pass 1:** for each flanged beam, compute the cuts as today, then `segs = Segments(cuts, beam.Len, END_TOL)`.
    - Drop a segment if `FlangeAt(midPt)` is false, as today.
    - Collect the through-HPs `xs` as today, sorted by s, into a list of segment records: beam, lo, hi, xs.
    - Support positions are the cut s values (girder centreline) or the run ends.
  - Compute `sMax = MaxSpacing(records.Select(r => r.xs.s))` and `L = 3 * sMax`. Add the report line `"{LOG} s_max={sMax:0.#} → L={L:0.#}."`. If `sMax <= 0`, return.
  - **Pass 2:** for each record:
    - `startSign = Math.Sign(Doz(pos, midPt, center).DotProduct(beam.Axis))`. Treat 0 as +1.
    - For Center when `|beam.Axis.Y| > 1e-6`: `split = (center.Y − beam.P1.Y) / beam.Axis.Y`. Otherwise split is null.
    - `picks = PickBySpacing(...)`.
    - Build the final set of indices:
      - Remove any HP whose correction has `Tripping == false`.
      - Add every index whose HP has `Tripping == true`.
    - Each pick goes through `BuildOne` unchanged, with doz computed at X (the intersection point), not at the segment midpoint. That way the Center side follows the half the bracket is in.
- [ ] **Step 2: Report lines.** Per bracket: `"{segTag} · HP {h} s={s:0} Δ={distance from previous support:0}/L={L:0} ({auto|tay On}) · {place}"`. Per segment with no pick: `"{LOG} SKIP {segTag}: không cần (đoạn ≤ L)."`
- [ ] **Step 3: Verify.** Run `dotnet build MCG_3DPanel.csproj -c Debug` and expect 0 errors. Run `dotnet test tests/MCG_3DPanel.Tests` and expect all tests to pass.
- [ ] **Step 4: Update the docs.**
  - In `.claude/skills/tripping-bracket/tripping-bracket_SKILL.md`, replace the selection rule with R1–R5 from the spec and add the 7D-16C expected table.
  - Update the memory file `tripping-bracket.md`.
- [ ] **Step 5: Commit** `feat(tripping-bracket): place by 3x max spacing, beam ends as supports, per-HP override`

The visual check in AutoCAD is the user's to do (7D-16C should give about 22 brackets, see the spec table).
