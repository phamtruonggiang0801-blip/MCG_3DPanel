# Separate 3D drawing — design (2026-11-06)

## Why

The user wants to work the way Inventor separates `.iam` and `.idw`: the 3D panel lives in its own drawing, the user
edits it there (names, Kind, Tripping and Collar Plate from the Result tab), and later 2D drawings are generated from
that 3D file. Today 2D and 3D share one drawing, Keep deletes the 2D block, and nothing survives a reopen.

## Goals

- Build 3D creates one separate 3D drawing per panel and makes it the working file.
- The 3D drawing is self-contained: Result, Naming, rebuild and corrections work with only that file open.
- Opening a 3D drawing fills the Result and Naming tabs by itself, with no Load button and no rebuild.
- The source 2D drawing is left unchanged.

## Non-goals

- Generating 2D drawings from 3D. That is the next stage; this stage only keeps its data in the 3D file.
- Updating the 3D file when the source 2D drawing changes later.
- New Result features. Tripping and Collar Plate already work from the Result tab.
- Solids outside the block. The Pick discussion is separate.

## Anti-goals

- Silently overwriting a 3D file, which would lose the user's edits.
- Changing or dirtying the source 2D drawing.
- Opening a 3D file and marking it modified just because the tabs were filled.

## Constraints from the code

- `SourceKey` is `{2D entity handle}#{i}`. Corrections, names, Deleted, Merges and Splits are keyed by it, and
  `DeleteKey` and `FlipKey` embed it. **The 2D entities must keep their handles in the 3D file.**
- Corrections and names are XRecords on the 2D block definition, so they travel with it.
- `RebuildOne` needs a block reference to the 2D source in the same database. Today it reopens the erased reference,
  which only works in one session.
- `BuiltSolid` (solid id, part, type, keys, codes) exists only in memory. Without it the tabs have no rows.
- Multi-drawing guards compare the row's database with the active one. If 2D and 3D share one file, they stay valid.
- Rule from `ux-guardrails`: every ObjectId belongs to one drawing; no silent errors on user actions.

## Approaches

- **A. Self-contained 3D file (chosen by the engineer).** The file carries the 2D source, corrections and names.
  Rebuild, Result and Naming barely change, and the file can be sent on and still edited. Cost: later edits to the
  source 2D drawing do not reach the 3D file.
- B. Two linked files. The 3D file points at the 2D file. Rejected: breaks when files move, and every multi-drawing
  guard would need rewriting.
- C. Export when finished. Edits stay in the 2D drawing. Rejected: not the workflow the user wants.

This replaces the earlier decision "work only with panels built in this session": the engineer chose auto-fill on open.

## Design

### File creation

For each selected panel, in the source drawing:

1. Read the saved source file from disk into a side database (`new Database(false, true)`, `ReadDwgFile` with shared
   read access). Handles are those of the file. `Database.Wblock()` was tried first and kept 0 of 3631 handles.
2. In the side database, erase Model Space content except that panel's 2D block reference, and erase any `MCG3D_*` blocks.
3. Move the 2D reference to a frozen, locked layer `MCG3D_2D_SOURCE`.
4. Purge what is left unused, keeping only this panel's block tree. Other panels' blocks, corrections and names go
   (my call: the file carries this panel only, and auto-fill cannot pick up anything else).
5. Save as `<source folder>\<panel block name>_3D.dwg` and open it as the active document.
6. Build there with the Input and Position from the Input tab.

The source drawing is never written. The source must be saved: if it has unsaved changes or has never been saved,
Build stops before anything is created, with `Save this drawing first, then Build 3D.` (engineer, 2026-11-06).

### Keep

The Keep question goes away. The file is the result; closing without saving discards it. If a panel builds no solids,
its file is deleted, the user is told, and other panels continue.

### Stored panel data

A new XRecord `MCG3D_PANEL` on the 3D block definition, JSON, written on Build and rebuild:

- handle of the 2D reference and of the 2D block definition;
- `PanelInput` and `Position`;
- one entry per solid: solid handle, type, part `SourceKey`, `FlipKey`, `DeleteKey`, `Code`, merged codes, deleted flag.

Pure C# serializer `PanelMetaJson` with tests. Old `MCG3D_SOURCE` records stay ignored.

### Auto-fill on open

On document open or activate, and when the palette opens: find block references named `MCG3D_*` with `MCG3D_PANEL`,
and for each one run classification only (read-only, applies stored corrections), then rebuild `BuildResult` from
the stored data, reading bounds from the solids. No geometry is written and the document stays unmodified.

`ClassificationService` already reads the frozen reference. Failures show on the status line and the panel stays
out of the list. An old 3D block without `MCG3D_PANEL` is not listed; the status says to Build it again.

Rows after auto-fill must behave exactly like rows after a fresh Build, for Result, Naming, Pick and rebuild (my call).
If a stored solid no longer exists because the user erased it by hand, its row is dropped and the status says how many;
the next rebuild brings it back (my call). Auto-fill writes nothing: the file must close with no Save prompt, and this
is a checklist row (my call).

### Rebuild

`RebuildOne` keeps working. The 2D reference is now live, so `SourceStillExists` holds across reopen, and
`SourceRefId` comes from the stored handle. `MCG3D_PANEL` is rewritten in the same transaction.

### Tabs

Result and Naming follow the active document as they do now: the rows of the 3D file show when it is active. Closing
the file clears its rows. The source drawing shows no rows. In a 2D drawing the Input tab starts a Build as usual.

### Spike (first task, in AutoCAD)

Confirm before anything else: the side database keeps entity handles; a frozen locked reference can still be read by
classification; a palette button can open a document and continue building there.

This is a hard gate: if handles are not kept, stop and go back to the engineer before planning the key remap (my call).

### Several panels (engineer)

One panel, one file. A selection of several panels builds them in turn and leaves each file open; the last one is
active. A panel that fails does not stop the others.

### Target file already exists

The target may hold the user's edits, so it is never overwritten. After the user selects panels and before anything
is built, if any target file exists, one dialog lists those panels and offers one choice for all of them:

- **Open**: open the existing files, no build for those panels.
- **New copy**: build into `<name>_3D_2.dwg`, the next free number.
- **Cancel**: stop, nothing is built.

Panels with no existing file build normally under Open and New copy. A target already open in AutoCAD counts as
existing; Open switches to it. No dialog appears when no target exists. To start over, the user deletes the old file.

### Build inside a 3D file (engineer)

When the active drawing holds a panel with `MCG3D_PANEL`, the Input tab shows that panel's stored values and the button
reads **Rebuild 3D**. Pressing it applies the Input tab values to that panel through the normal rebuild, in the same
file, and rewrites `MCG3D_PANEL`. Corrections and names stay. No selection prompt, no new file.

## Left to the builder

- The exact open-document API and context, within the rules of `ux-guardrails`.
- Status texts in English.
- `docs/ux-checklist.md` rows, the structure-naming skill, SESSION_LOG.
