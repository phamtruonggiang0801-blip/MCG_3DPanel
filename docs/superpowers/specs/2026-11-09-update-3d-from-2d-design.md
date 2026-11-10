# Update a 3D file from an edited 2D drawing — design (2026-11-09)

## Why

The user edits the 2D panel and Builds again, but the 3D drawing does not change. Open only opens the old file;
Rebuild 3D uses the 2D copy inside the 3D file; New copy takes the new 2D but loses corrections and names, which live
only in the 3D file. There is no way to take the new 2D and keep the edits.

## Goals

- In the existing-file dialog, **Update** takes the saved 2D, keeps the 3D file's corrections and names, and rebuilds
  under the same file name.
- The previous 3D file stays recoverable.

## Non-goals

- Updating automatically or from inside the 3D file.
- Merging changes made by hand to solids in the 3D file.

## Approaches

- **A. An Update choice (chosen by the engineer).** A deliberate action; the 3D file stays self-contained.
- B. The 3D file remembers the 2D path and Rebuild 3D reads it. Rejected: breaks when files move, and 2D changes could
  arrive unintentionally.
- C. Keep New copy only. Rejected: edits are lost.

## Design

The dialog becomes `Update`, `Open`, `New copy`, `Cancel`, still one choice for all listed panels. Panels with no 3D file
build normally under Update, Open and New copy.

For each panel under Update:

1. **Target must be free to replace.** If it is open with unsaved changes, a box asks `{file} has unsaved changes.`
   with `Save and Update` (saves the file, then goes on, so the changes are carried) and `Skip this panel` (that panel
   stops with `{file} was skipped: it has unsaved changes.`) (engineer 2026-11-10; replaces the earlier refusal
   `Save or close {file} first, then Update.`). It is then closed before the swap.
2. **Read the old file** from disk into a side database: its `MCG3D_PANEL`, and the 2D block's `MCG3D_CORRECTIONS` and
   `MCG3D_NAMES`. An old file without `MCG3D_PANEL` stops that panel with `{file} has no panel data; use New copy.`
3. **Match the panel.** The old file's stored 2D reference handle must equal the selected reference. Otherwise that panel
   stops with `{file} was built from another 2D block; use New copy.`
4. **Create the new file** from the saved source, as Build does, and write the old corrections and names on its 2D block
   definition before saving. Handles are preserved, so unchanged 2D objects keep their edits.
5. **Replace.** Move the old `.dwg` to `<name>_3D.<yyyyMMdd_HHmmss>.bak`, and the new file into its place.
6. **Build** in the file with the old file's stored `Input` and `Position`, not the Input tab: those are the values the
   3D file was last built with (my call).
7. **On failure** the new file is removed and the old `.dwg` moved back. The user is told.

Corrections or names whose 2D object no longer exists are ignored. The status says how many
(`{n} edits no longer match the 2D drawing.`), so nothing disappears silently. New 2D objects get automatic results.

The source drawing is never written, and the unsaved-source stop applies as in Build.

## Left to the builder

- How the temp file and the move fit the existing `Create`.
- English status wording beyond the texts above.
- Checklist rows and the SESSION_LOG entry.
