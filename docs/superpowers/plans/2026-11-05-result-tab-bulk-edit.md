# Result Tab Bulk Edit — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Collar Auto/On/Off per HP, Excel-like multi-select with bulk edit, and a per-FB height (numeric only).

**Spec:** `docs/superpowers/specs/2026-11-05-result-tab-bulk-edit-design.md`

## Global Constraints
- ux-guardrails rules 1–3 (rows in another drawing are skipped and reported, no silent failure, Cell* styles).
- Legacy spec strings `h*t`, `hxt`, `hXt` and `h×t` must keep parsing. Only the height is used for FB.
- One rebuild per panel per edit.

## Review Focus
1. A bulk edit over rows from two panels must rebuild both panels exactly once each. Reviewer check: `RequestRebuild` dedupe.
2. A bulk edit that includes rows that don't support the control must skip them and report it. Pinned by `Plan_SkipsUnsupported`.
3. A FB height correction of 0, a negative number or text must be rejected with a message. Pinned by `TryParseHeight_Rejects`.
4. Old drawings with `FlatBarSpec = "120*6"` in SourceMeta must rebuild unchanged. Pinned by `TryParseHeight_Legacy`.
5. Highlight of many rows must survive a drawing switch (rule 1). Reviewer check against checklist A1–A6.

### Task 1: Pure helpers
- `PanelInput.TryParseHeight(string s, out double h)` accepts a number or a legacy spec (`*`/`x`/`X`/`×`), invariant
  culture or the current culture. `FlatBarHeight` and `HpHeight` use it. `PanelInput.FormatSpec("120*6")` returns
  `"120 × 6"`.
- `UI/BulkEdit.cs` (pure, linked into the tests):
  - `Plan<T>(T clicked, IReadOnlyList<T> selected, Func<T,bool> supports, Func<T,bool> inActiveDrawing)` returns
    `BulkPlan<T>{Targets, Unsupported, OtherDrawing}`.
  - `Describe(plan, what)` returns the status text.
- `PartCorrection.Collar` (bool?) and `PartCorrection.FbHeight` (double?) go into `IsEmpty`, with a JSON round trip.
- Tests: `TryParseHeight_Number`, `_Legacy`, `_Rejects`, `FormatSpec`, `Plan_ClickedOutsideSelection_OnlyClicked`,
  `Plan_SkipsUnsupported`, `Plan_SkipsOtherDrawing`, `Describe_Counts`, `RoundTrip_CollarFbHeight`.

### Task 2: Collar per HP
- `WebCollarPlateBuilder`:
  - After the auto picks, drop the picks whose HP has `Collar == false`.
  - For HPs with `Collar == true`, add every web×HP crossing (CO hole) that wasn't picked. Use the same `BuildOne`
    and `key` (`web~hp`) so there are no duplicates.
- Result row: a Collar dropdown (Auto/On/Off) on HP rows, with the same wiring as Tripping.

### Task 3: FB height per bar
- `BuildContext.FbHeight(PanelPart fb)` returns `Corrections[fb.SourceKey].FbHeight ?? Input.FlatBarHeight`.
- `BuildContext.FbHeightAt(Point3d p)` uses the FB whose footprint touches p (≤ 5 mm). If none is found it falls back
  to the Input height and adds a report line.
- Call sites:
  - `FlatBarBuilder` and `HpEndAtHpBuilder` (cutter + weld) use `FbHeight(fb)`.
  - `BracketBuilder` BF and `GirderEndBuilder` GE/GB-FB use `FbHeightAt(end point)`.
- Input tab: "FB height (mm)" numeric box (validated). It stores `FlatBarSpec = height`.
- HP combo shows `120 × 6`.
- Result: FB Size is `h × tT`, with an editable height box (Enter commits, empty resets). HP Size is `120 × 6`.

### Task 4: Multi-select + bulk edit
- Grid `SelectionMode=Extended`.
- `EntityHighlighter.ShowMany(paths, bounds union, zoom)` keeps a list of (path, db).
- SelectionChanged highlights all selected rows. ⌖ and the menu zoom to the union.
- Every edit handler goes through `BulkEdit.Plan` → apply → one `Rebuild` per distinct panel → status `Describe`.
- Delete, Restore and Clear Correction work on the selection. Ctrl+C / Copy uses the selected rows when more than one
  is selected.
- Checklist rows for multi-select. Final reviewer.
