# Update 3D after DC Block Panel — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Update keeps corrections and names on 2D parts that did not change after the panel block is brought in again from the Global drawing, even when every handle and the block base point changed.

**Architecture:** Pure C# matcher (`Services/Update/`, linked into the test project) maps old 2D entity handles to new ones by shape, with a per-definition base-point offset vote, and rewrites the `{hex}#{n}` tokens of the corrections and names JSON. AutoCAD side reads shape records from the old 2D block inside the old 3D file and from the selected panel in the source drawing, inside `PrepareUpdate`, before `Panel3DFile.Create` writes the carried JSON.

**Tech Stack:** C# / .NET 8, AutoCAD 2026 .NET API, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-20-update-after-dc-block-design.md`

## Global Constraints

- Files under `Services/Update/` are pure C#: no `using Autodesk.*` (header comment `// ⚠️ File THUẦN C# — KHÔNG using Autodesk.*.` as other pure files).
- Geometry tolerance **0.5 mm**; bulge tolerance **1e-3**; offsets rounded to a **0.5 mm** grid.
- Base-point offset wins only with **more than half** of the definition's old records that have a candidate; tie or no majority ⇒ (0, 0). Translation only.
- Records skip layer `Mechanical-AM_9`; each block definition is walked once; anonymous `*U…` definitions give `Block = ""` and are never matched.
- Status texts, exact: `{k} edits kept on unchanged parts; {n} dropped because their 2D part changed.` and `The base point of {block} moved by ({dx}, {dy}).` (`dx`, `dy` formatted `0.#`, invariant culture).
- Refusal text unchanged: `{file} was built from another 2D block; use New copy.`
- Never write to the source drawing; the old 3D file stays recoverable (existing Update rules).
- Build: `dotnet build -c Debug MCG_3DPanel.csproj -p:BuildStamp=$(date +%Y%m%d_%H%M%S)`; tests: `cd tests/MCG_3DPanel.Tests && dotnet test`.

## Review Focus

1. A handle token at the end of a word that happens to be hex (`1B#0/GirderFlange#0` — the `e#0`) must not be treated as a handle — pinned in Task 2.
2. A closed polyline redrawn from another start vertex or in the other direction (bulges flip sign) still matches — pinned in Task 1.
3. Many identical parts (e.g. 20 FB of the same size on one layer) pair one-to-one and the offset vote still finds the shift — pinned in Task 1.
4. A nested definition that exists only in the old or only in the new tree (renamed in Global) drops its edits without failing the Update — pinned in Task 1.
5. Empty / missing corrections or names JSON (panel never edited) — Update behaves exactly as today — pinned in Task 2.

---

### Task 1: Shape records and matcher (pure)

**Files:**
- Create: `Services/Update/ShapeRecord.cs`, `Services/Update/ShapeMatcher.cs`
- Modify: `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj` (add `<Compile Include="..\..\Services\Update\*.cs" LinkBase="Update" />`)
- Test: `tests/MCG_3DPanel.Tests/ShapeMatcherTests.cs`

**Interfaces:**
- Produces (namespace `MCG_3DPanel.Services.Update`, uses `MCG_3DPanel.Services.Detail.P2`):
  - `public sealed class ShapeRecord { public string Handle, Block, Type, Layer, Name = ""; public bool Closed; public List<P2> Points = new List<P2>(); public List<double> Bulges = new List<double>(); public List<double> Scalars = new List<double>(); }`
    - Polyline: `Points` = vertices, `Bulges` (same count), `Closed`. Line: `Points` = start, end. Circle: `Points` = centre, `Scalars` = r. Arc: `Points` = centre, `Scalars` = r, start angle, end angle. BlockReference: `Points` = position, `Scalars` = rotation, scaleX, scaleY, `Name` = effective block name. Other: `Points` = extents min, max.
  - `public sealed class MatchResult { public Dictionary<string, string> Map; public List<(string Block, P2 Offset)> Shifts; }` — `Map` old handle → new handle (case-insensitive keys, hex as stored); `Shifts` only definitions whose offset ≠ (0, 0).
  - `public static MatchResult ShapeMatcher.Match(IList<ShapeRecord> oldRecs, IList<ShapeRecord> newRecs, double tol = 0.5)`

Algorithm (per block name present in both lists; records with empty `Block` skipped):
1. **Candidates:** same `Type`, `Layer`, size key (`Points.Count` for polylines/lines; r rounded to 0.1 for circles/arcs; `Name` for block references; `"ext"` otherwise).
2. **Vote:** offset = min corner(new) − min corner(old) (min corner of `Points`; circles/arcs: centre − (r, r)); round each component to the 0.5 grid; per offset count distinct old records. Winner needs count·2 > number of old records with ≥ 1 candidate and must be strictly larger than the runner-up; else (0, 0).
3. **Match** with the offset subtracted from new geometry: identity first (same handle in both, equal geometry), then remaining old records in list order take the first unused equal new record in list order.
4. **Equal geometry:** same counts; points within `tol`; `Scalars`: radii within `tol`, angles/rotation within 1e-3, scales within 1e-6; bulges within 1e-3. Closed polylines: any cyclic start, either direction; open polylines and lines: either direction. Reversing a polyline reverses the vertex order, shifts bulges to the preceding segment and negates them.

- [ ] **Step 1: Write the failing tests** in `ShapeMatcherTests.cs` (helper `R(handle, block, layer, closed, params (x,y)[] pts)` builds a polyline record with zero bulges):
  - `SameShapes_NewHandles_AllMapped` — two rectangles in block `7D-16P_Structure`, new handles `A1/A2` ⇒ `Map["2B7B3"] == "A1"`, `Map["2B7B5"] == "A2"`, `Shifts` empty.
  - `MovedVertex_NotMapped` — one vertex moved 0.6 ⇒ old handle absent from `Map`.
  - `WithinTolerance_Mapped` — moved 0.4 ⇒ mapped.
  - `ClosedRedrawnFromOtherStartAndReversed_Mapped`.
  - `ReversedWithBulge_Mapped` — open polyline with bulge 0.5 on segment 0, reversed record has bulge −0.5 on its last segment ⇒ mapped.
  - `OtherLayer_NotMapped`.
  - `UnchangedHandle_MapsToItself`.
  - `IdenticalDuplicates_PairedInOrder` — 20 equal rectangles at distinct places, shuffled handles ⇒ 20 entries, each new handle used once.
  - `BasePointMoved_AllMappedAndShiftReported` — every new record shifted by (100, −50.2) ⇒ all mapped, `Shifts` = `[("7D-16P_Structure", (100, −50))]` within 0.5.
  - `BasePointMovedAndTwoEdited_OthersMapped` — 10 records shifted (100, 0), 2 of them also edited ⇒ 8 mapped, shift found.
  - `NoMajority_NoShift` — 2 records shifted (100, 0), 2 shifted (0, 100) ⇒ `Shifts` empty.
  - `BlockOnlyInOld_Dropped` — records of `7D-16P_Old` with no new block of that name ⇒ not in `Map`, no exception.
  - `AnonymousBlock_Skipped` — `Block = ""` ⇒ not mapped.
- [ ] **Step 2: Run** `dotnet test --filter ShapeMatcherTests` — Expected: FAIL (types do not exist).
- [ ] **Step 3: Implement** `ShapeRecord` and `ShapeMatcher.Match` as specified above.
- [ ] **Step 4: Run** `dotnet test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(update): match 2D parts by shape, with a base point offset vote`

### Task 2: Re-key corrections and names (pure)

**Files:**
- Create: `Services/Update/KeyRemap.cs`
- Test: `tests/MCG_3DPanel.Tests/KeyRemapTests.cs`

**Interfaces:**
- Consumes: `CorrectionJson.Serialize/Deserialize`, `NamesJson.Serialize(names, deleted, merges, splits)` / `NamesJson.Deserialize(json, out deleted, out merges, out splits)` (existing).
- Produces (namespace `MCG_3DPanel.Services.Update`):
  - `public static string KeyRemap.Remap(string key, IReadOnlyDictionary<string, string> map)` — replaces each token matched by `(?<![0-9A-Za-z])([0-9A-Fa-f]+)#(\d+)` with `{map[hex]}#{n}`; returns null if any token's hex is not in `map`; a key with no token is returned unchanged.
  - `public static string KeyRemap.Corrections(string json, IReadOnlyDictionary<string, string> map, ref int kept, ref int dropped)`
  - `public static string KeyRemap.Names(string json, IReadOnlyDictionary<string, string> map, ref int kept, ref int dropped)`
  - Both return the rewritten JSON (null/empty input ⇒ returned unchanged, counts untouched). Counts are per stored key (corrections `Parts` + `Deleted`; names `Names` + `Deleted` + each merge member + `Splits`). A merge group keeps its remapped members; a group left with < 2 members is dropped by `NamesJson.Serialize`.

- [ ] **Step 1: Write the failing tests:**
  - `Plain_Remapped` — `Remap("2B7B3#0", {2B7B3→A1}) == "A1#0"`.
  - `Generated_AllTokensRemapped` — `"CollarPlate:2B7B3#0~2B53D#1"` with both mapped ⇒ `"CollarPlate:A1#0~A2#1"`.
  - `OneTokenMissing_Null` — second handle unmapped ⇒ null.
  - `HexTailOfWord_NotAToken` — `"1B#0/GirderFlange#0"` with `{1B→C9}` ⇒ `"C9#0/GirderFlange#0"`.
  - `NoToken_Unchanged` — `"HpEndBracketB:x"` ⇒ same.
  - `Corrections_KeptAndDropped` — JSON with 2 parts + 1 deleted, one handle unmapped ⇒ kept 2, dropped 1, deserialized result has the new keys.
  - `Names_MergeGroupLosesMember` — merge of 3 keys, one unmapped ⇒ group of 2 remains; kept/dropped counted per member.
  - `EmptyJson_Unchanged` — `Corrections(null, …)` returns null, counts 0.
- [ ] **Step 2: Run** `dotnet test --filter KeyRemapTests` — Expected: FAIL.
- [ ] **Step 3: Implement** `KeyRemap`.
- [ ] **Step 4: Run** `dotnet test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(update): re-key corrections and names from old handles to new`

### Task 3: Same-panel check by block name

**Files:**
- Modify: `Commands/Panel3DPath.cs:42-49`
- Test: `tests/MCG_3DPanel.Tests/PanelMetaTests.cs` (next to the `Refusal_*` tests)

**Interfaces:**
- Produces: `public static string Panel3DPath.UpdateRefusal(string fileName, PanelMeta meta, string selectedRefHandle, string selectedBlockName = null, string oldBlockName = null)` — null also when both names are non-empty and equal (ordinal, case-insensitive). Texts unchanged.

- [ ] **Step 1: Write the failing tests:** `Refusal_OtherHandle_SameBlockName_Null` (`"2C"` vs `"1B"`, names `7D-16P_Assy`/`7d-16p_assy` ⇒ null); `Refusal_OtherHandle_OtherBlockName` (⇒ existing text); `Refusal_OtherHandle_NoNames` (⇒ existing text). Existing three tests stay green.
- [ ] **Step 2: Run** `dotnet test --filter PanelMetaTests` — Expected: FAIL on the new same-name test.
- [ ] **Step 3: Implement** the extra parameters.
- [ ] **Step 4: Run** `dotnet test` — Expected: all pass.
- [ ] **Step 5: Commit** `feat(update): allow Update when the panel block was inserted again`

### Task 4: Wire into Update (AutoCAD)

**Files:**
- Create: `Commands/ShapeRecordReader.cs`
- Modify: `Commands/Panel3DFile.cs` (`OldPanelData`, `ReadOld`), `Commands/PanelCommands.cs` (`PrepareUpdate` ~316-357, `UpdateFile` success path near `AddUnmatchedNote`)

**Interfaces:**
- Consumes: Task 1 `ShapeMatcher.Match`, Task 2 `KeyRemap.Corrections/Names`, Task 3 `UpdateRefusal(…, selectedBlockName, oldBlockName)`.
- Produces:
  - `public static List<ShapeRecord> ShapeRecordReader.Records(Transaction tr, ObjectId panelBtrId)` — walk like `SourceFingerprintReader` (definition once, skip `Mechanical-AM_9`, effective name for dynamic blocks via `DynamicBlockTableRecord`, `*U…` ⇒ `Block = ""`), filling records per Task 1's field rules from `Polyline`, `Line`, `Circle`, `Arc`, `BlockReference`; anything else from `GeometricExtents` (no extents ⇒ skip).
  - `OldPanelData` gains `public string BlockName; public List<ShapeRecord> Shapes;` filled by `ReadOld` from the old 2D block (`SourceBtrHandle`).
  - `OldPanelData` gains `public string RemapNote;` (null = nothing to report): the kept/dropped line plus one base-point line per shift, joined by `"\n"`.

- [ ] **Step 1:** In `PrepareUpdate`, read the selected reference's block name and records from the source database (`job.RefId.Database`, open-close transaction, read only); pass names to `UpdateRefusal`.
- [ ] **Step 2:** If the refusal is null: `Match(carry.Shapes, newRecords)`; if `Map` is not the identity (some key ≠ value, or some old record unmapped) rewrite `carry.CorrectionsJson` / `carry.NamesJson` with `KeyRemap` and set `carry.RemapNote` with the exact texts from Global Constraints (only when the map is not the identity or a shift exists). Any exception in this step ⇒ stop the panel with `Could not match the 2D parts of {file}: {message}. The old file was not changed.`
- [ ] **Step 3:** After a successful Update, add each line of `carry.RemapNote` to `run.Problems` before `AddUnmatchedNote`.
- [ ] **Step 4: Build** — Expected: `Build succeeded`; `dotnet test` all pass.
- [ ] **Step 5: Manual check in AutoCAD** (engineer): on 7D-16P official drawing with corrections + names, (a) DC the block from Global unchanged ⇒ all edits kept, status `N edits kept on unchanged parts; 0 dropped…`; (b) move one bracket in Global then DC ⇒ that bracket's edit dropped; (c) change the `_Assy` base point in Global then DC ⇒ edits kept, base point line shown; (d) delete and re-insert the block ⇒ Update not refused.
- [ ] **Step 6: Commit** `feat(update): keep 3D edits on unchanged parts after DC Block Panel`
