# Update 3D after the panel block is brought in again from the Global drawing — design (2026-11-20)

## Why

Engineers work in a **Global** drawing that holds many panel blocks. Each panel also has an **official drawing** where
they Build 3D, make the Plan View and the Details. When the Global drawing changes after the 3D is done, they bring the
panel block from Global into the official drawing again (DesignCenter, "DC Block Panel") and run Update.

Today that loses every edit. Corrections (tab Result) and names (tab Naming) are keyed by the handles of the 2D
polylines (`{handle}#{i}`). Bringing the block in again copies every entity of the definition, so every handle is new:
Update carries the old corrections and names, none of them match a part, and the status says `{n} edits no longer match
the 2D drawing.` If the engineer deleted and inserted the block instead of redefining it, the block reference handle is
new too and Update refuses: `{file} was built from another 2D block; use New copy.`

## Goals

- After the block is brought in again, **Update keeps every correction and name whose 2D part did not change** and
  drops the ones whose part changed in Global (those parts get automatic results, as new parts do today).
- Works with 3D files built before this change (no new data needs to be stored first).
- Update is still allowed when the block reference itself was replaced, as long as it is the same panel.

## Non-goals

- Keeping an edit on a part whose shape changed (moved vertex, new length, other layer). The engineer accepts losing it.
- Writing anything into the Global drawing or the official drawing.
- Matching across different panels, or helping with the DesignCenter step itself.

## Approaches

- **A. Match parts by shape at Update (chosen by the engineer).** The old 3D file already holds the copy of the 2D block
  it was built from, with the old handles the edits are keyed by. Update compares it with the 2D block in the official
  drawing, entity by entity, and re-keys the edits from old handles to new ones.
- B. Tag each 2D polyline with a stable id. Rejected: the block from Global carries no tag, so bringing it in drops them.
- C. Ask the engineer to copy only the changed lines by hand instead of the whole block. Rejected: slow and error-prone
  on the Global workflow.

## Design

### Where it runs

In `PrepareUpdate`, after `Panel3DFile.ReadOld` and before `Panel3DFile.Create`:

1. **Same-panel check (replaces the handle-only check).** Update is allowed when the old meta's `SourceRefHandle` equals
   the selected reference (today's rule), **or** the selected reference's block name equals the name of the old file's 2D
   block (the `_Assy` block, e.g. `7D-16P_Assy`). Otherwise the refusal stays `{file} was built from another 2D block; use
   New copy.`
2. **Shape match.** Read entity records from both block trees: the old 2D block inside the old 3D file (`ReadOld` already
   opens it; resolved by `SourceBtrHandle`) and the selected reference's block in the saved source file (the same saved
   file `Create` copies from).
3. **Re-key.** Build a map old handle → new handle and rewrite the keys of `MCG3D_CORRECTIONS` and `MCG3D_NAMES` in
   `OldPanelData` before `Create` writes them on the new file.

When nothing was re-inserted (handles unchanged), the map is the identity and the result is exactly today's Update.

### Entity records

Walk the panel block and every nested block definition it references, each definition once (as
`SourceFingerprintReader` does), skipping layer `Mechanical-AM_9`. A handle belongs to an entity of one definition, not
to each insertion of it, so records stay in their own definition. One record per entity:

- `Handle` (hex), `Block` (name of the owning definition; effective name for dynamic blocks; anonymous `*U…` ⇒ the
  record is not matched), `Type`, `Layer`.
- Geometry in the **owning definition's own coordinates**: polyline vertices with bulges and the closed flag; line end
  points; circle/arc centre, radius, angles; block references: name + insertion point, rotation, scale; anything else its
  geometric extents.

### Match rule (pure C#, testable)

- Two records match when `Block`, `Type`, `Layer` are equal and the geometry is equal within **0.5 mm** (my call):
  same vertex count, each vertex within 0.5, bulges within 1e-3. A closed polyline matches with any start vertex and
  either direction; an open one in either direction.
- A record whose handle exists in both trees with equal geometry maps to itself first (the common case: only some
  parts changed, or the block was not re-inserted).
- Identical duplicates (same signature several times) are paired in drawing order; they are interchangeable.
- Each new record is used once. Unmatched old records ⇒ their edits are dropped; unmatched new records ⇒ automatic.

### Re-keying

Keys hold handles as `{hex}#{n}` tokens, alone (`2B7B3#0`) or inside generated keys (`CollarPlate:2B7B3#0~2B53D#1`,
merge/split lists, deleted lists). Replace each token whose hex is in the map with the new hex, keeping `#{n}`. A key
with any token not in the map is dropped (its part changed). This covers corrections `Parts` + `Deleted`, and names
`Names`, `Deleted`, `Merges`, `Splits`.

### What the engineer sees

- Status adds one line when the map is not the identity: `{k} edits kept on unchanged parts; {n} dropped because their
  2D part changed.` The existing `{n} edits no longer match the 2D drawing.` keeps counting what still fails after
  re-keying.
- Nothing else changes: same dialog, same backup `.bak`, same rollback on failure.

## Left to the builder

- Exact English wording of the new status line within the meaning above.
- Where the record reader lives (next to `SourceFingerprintReader` is natural) and how the pure matcher is split for tests.
- Checklist rows and the SESSION_LOG entry.
