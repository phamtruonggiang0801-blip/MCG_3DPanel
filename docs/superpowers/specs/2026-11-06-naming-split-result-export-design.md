# Naming split, Result toolbar, English prompts — design (2026-11-06)

## Why

The engineer wants to undo automatic grouping, a simpler Result toolbar, and an English command line. The tool works
only with panels built in the current session.

## Goals and non-goals

- Goal: Unmerge can split a line the tool grouped by itself.
- Goal: Load 3D is gone, button and command.
- Goal: Export CSV and Export STEP sit behind one dropdown.
- Goal: every command-line prompt and message is English.
- Non-goal: auto-loading panels when a drawing opens.
- Non-goal: translating build-report logs and warnings (hundreds of lines, technical readers).
- Non-goal: choosing single pieces inside a row. There is one row per line.

## Approaches

Unmerge for automatic lines:

- **A. Split every piece, then Merge back what belongs together (chosen by the engineer).** No new UI.
- B. Pick the piece to split off in the drawing. Rejected: a new pick flow.
- C. Child rows per piece in the grid. Rejected: grid, highlight and Pick all change.

After Load 3D goes, the engineer chose to work with panels from the current session only, over auto-load or keeping
the typed command.

## Design

### 1. Unmerge on automatic lines

- A selected row with a manual Merge: Unmerge removes the group, as today. The line returns to automatic grouping.
- A selected row with no manual Merge and two or more distinct `SourceKey`s: Unmerge splits it. Each key becomes its
  own line, and numbers are recomputed.
- A row with one key: nothing to split. Unmerge is greyed out when no selected row qualifies, with the reason.
- Parts sharing a `SourceKey` (GE cut from a Web) stay together.
- A user name stays on the longest part, as Unmerge does today. The other parts return to auto names.
- A split HP on a Web line becomes its own HT / HL line, because its kind is now its own.

Storage: `NameStore` keeps a set of split `SourceKey`s per panel, saved as `Splits` in `MCG3D_NAMES`. It is written
with names, Deleted and Merges, and is part of the "anything to save" check. Unknown keys are ignored.

`StructureNaming.Lines` gets an optional `splits` argument. A piece whose key is in the set does not join automatic
grouping; each key forms its own line. Manual Merge groups still apply afterwards, so **Merge recombines split rows**.

Reset to auto also removes the split of the selected rows, so they rejoin their automatic line. This is needed because
Merge refuses rows of different kinds, for example an HP split from a Web line (my call).

### 2. Remove Load 3D

Remove `BtnLoad3D`, `MCG_Load3DPanel`, and code only it uses: `StubFrom3D`, `WriteSourceMeta`, `SourceMeta`, the
`SourceKey` constant, `InsertTempSource` and the temporary-insert path in `RebuildOne`. Old `MCG3D_SOURCE` XRecords in
existing drawings are left alone and are harmless.

Rebuild inside the session is unchanged: the erased 2D reference still opens. If it cannot, the existing "Source 2D
block no longer exists" message shows.

After reopening, a kept panel's 2D block reference is gone. To edit it, the user inserts the 2D block and runs Build 3D.
That makes `MCG3D_<name>_2` next to the old block, which the user deletes. Corrections and names survive on the 2D block.

Every text that mentions Load 3D changes to Build 3D only: Naming and Result hints, status messages, `EntityHighlighter`,
the skills and `docs/ux-checklist.md`.

### 3. Export dropdown

```
[ Export ▾ ] [ Copy ] [ Pick ]
  ├ CSV table
  └ STEP files (one per panel)
```

`Export ▾` uses the toolbar primary style and opens a menu below it. The items call the existing `BtnExport_Click` and
`BtnStep_Click`. The STEP item is disabled with the existing "Requires AutoCAD Mechanical (STEPOUT)" reason. Right-click
"Export Table to CSV..." stays. English text; styles in `UI/PanelStyles.xaml`.

### 4. English prompts

All `ed.WriteMessage` text, prompt messages and the Keep question become English. `Yes` / `No` keywords stay.
Comments stay Vietnamese.

## Left to the builder

- Exact wording, following the English already on the UI.
- Tests in `StructureNamingTests` and a `NamesJson` round trip for `Splits`.
- Run `docs/ux-checklist.md`, update the structure-naming skill and SESSION_LOG.
