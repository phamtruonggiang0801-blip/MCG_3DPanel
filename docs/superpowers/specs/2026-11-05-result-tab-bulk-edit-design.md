# Result tab: Collar per HP, Excel-like multi-select, FB height per bar — design (2026-11-05)

## Why

The engineer wants more correction power on the Result tab, with the same quality bar as `ux-guardrails`:
add or remove Collar Plates per HP (like Tripping), select many rows and edit them in one go (like Excel), and
change the height of individual Flat Bars, without the error-prone `h*t` text format.

## Decisions (engineer, 2026-11-05)

### 1. Collar Plate per HP
- Each HP row gets a **Collar: Auto / On / Off** dropdown, next to Tripping, stored as `PartCorrection.Collar` (bool?)
  on the HP's SourceKey.
- **On** puts a collar at **every CO hole of that HP** (every web it passes through).
- **Off** removes every collar on that HP.
- Auto collars of other HPs are not recalculated. Deleting a single collar still uses Delete.

### 2. Excel-like multi-select
- Selection works like Excel: click, Shift+click (range), Ctrl+click (toggle), mouse drag and Ctrl+A.
- Every selected object is highlighted at once. ⌖ and Zoom to Object zoom to the union of the selection.
- **Bulk edit (like Ctrl+Enter):** changing Tripping, Collar, FB height, ⇄ or Sniped End on a row that is part of the
  selection applies the **same resulting value** to every selected row that supports it. For example, ⇄ sets
  `FlipDir = !clicked.FlipDir` on all of them, and Sniped End L sets the clicked row's new state on all of them.
  Rows that don't support the edit are skipped, and the status line says how many were changed or skipped.
- Changing a control on a row **outside** the selection affects only that row.
- One rebuild per panel per edit (the existing `RequestRebuild` queue already dedupes per panel).
- The context menu's Delete, Restore and Clear Correction act on the whole selection. Ctrl+C copies the selected
  rows (with header) for Excel.
- `ux-guardrails` rule 1 applies to every row: a row in another drawing is skipped and reported, and nothing is
  saved for it.

### 3. FB height per bar
- Only the **height** can be edited. The thickness always comes from the 2D footprint.
- **Input tab:** "Flat Bar spec" (`h*t` text) becomes **"FB height (mm)"**, a numeric box that only accepts a positive
  number.
- **Result tab:** the Size of an FB shows `120 × t6`. The height part is an editable numeric box (type 100, then
  Enter), stored as `PartCorrection.FbHeight` (double?) on the FB's SourceKey. Clearing it returns to the Input value.
- Every detail tied to that FB uses **its own height**: the FB solid, the BF bracket at the FB end, GE-FB/GB-FB,
  and HP end at FB (weld face `h_fb − 15`).
- Legacy specs stored as `120*6`, `120x6`, `120X6` or `120×6` (saved Input settings, SourceMeta in old drawings)
  are still read. Only the height is used.
- HP size stays a dropdown from HP.csv. Its text is displayed as `120 × 6`.

## Approaches

- Per-HP Collar: the same pattern as Tripping (a correction on the HP plus a builder override). An AutoCAD pick
  command was rejected for the same reason it was rejected for Tripping.
- FB size input: **A, a numeric height only (chosen)**, versus B, a text box that accepts `*`, `x` and `×`. B was
  rejected because the thickness part would be typed but ignored.
- Bulk edit: **apply on the edited control (chosen)**, versus a separate "Apply to selection" button. The chosen
  option matches Excel and needs no extra step.

## Builder's calls

- Pure helpers, unit tested:
  - `PanelInput.TryParseHeight(string)` (number, or a legacy `h*t`/`hxt`/`h×t`).
  - The FB height resolver (correction ?? input).
  - The bulk-edit planner (which rows get which value; skipped rows and their reasons).
- Per-FB height is read through one helper, `BuildContext.FbHeight(PanelPart fb)`. Details that only know "an FB
  touches here" (BF bracket, GE/GB-FB) use the FB touching that end. If none is found they fall back to the Input
  height and add a report line.
- `EntityHighlighter` holds a set of paths, still stored with their owning Database (rule 1).
- The checklist gets rows for multi-select and bulk edit. Each task ends with the ux-checklist pass and a final
  reviewer.
