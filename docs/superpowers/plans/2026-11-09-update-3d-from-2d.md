# Update 3D file from 2D Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** An `Update` choice takes the saved 2D, keeps the 3D file's corrections and names, and rebuilds under the same file name.

**Architecture:** Update reads the old 3D file from disk, creates a new file from the saved source with the old corrections and names written on its 2D block, swaps it into the target name with the old file kept as a timestamped `.bak`, and builds there. Decisions that need no AutoCAD live in pure C#.

**Tech Stack:** C#, WPF, AutoCAD 2026 .NET API, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-09-update-3d-from-2d-design.md`

## Global Constraints

- The source 2D drawing is never written.
- The old 3D file is never lost: until the new build is saved, it can be put back; afterwards it stays as `<name>_3D.<yyyyMMdd_HHmmss>.bak`.
- `Commands/Panel3DPath.cs` and `Services/Corrections/PanelMeta.cs` stay free of `Autodesk.*`.
- After a document switch never use a captured `Editor`; no exception may escape a palette handler.
- UI text English; comments Vietnamese.
- Exact texts: `Save or close {file} first, then Update.`; `{file} has no panel data; use New copy.`; `{file} was built from another 2D block; use New copy.`; `{n} edits no longer match the 2D drawing.`
- Every commit message ends with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. Old file with no panel data → that panel stops with its message; the file is untouched. (Task 1: `Refusal_NoMeta`)
2. Old file built from another 2D reference → that panel stops. (Task 1: `Refusal_OtherReference`)
3. A `.bak` with the same second's name already exists → another free name, never overwritten. (Task 1: `Backup_TakenSameSecond`)
4. Stored keys for generated solids such as `CollarPlate:x` → not counted as lost. (Task 1: `Unmatched_IgnoresGeneratedKeys`)
5. The new build fails after the swap → the old `.dwg` is back under its name, nothing else left. (Task 2: checklist row, needs AutoCAD)

---

### Task 1: Pure update rules

**Files:**
- Modify: `Commands/Panel3DPath.cs`
- Test: `tests/MCG_3DPanel.Tests/PanelMetaTests.cs`

**Interfaces:**
- Produces: `static string Panel3DPath.Backup(string target, DateTime now, Func<string, bool> taken)`.
- Produces: `static string Panel3DPath.UpdateRefusal(string fileName, PanelMeta meta, string selectedRefHandle)`, null when Update may proceed.
- Produces: `static int Panel3DPath.CountUnmatched(IEnumerable<string> storedKeys, ICollection<string> partKeys)`: counts keys shaped `{hex}#{digits}` absent from `partKeys`.

- [ ] **Step 1: Write the failing tests**

```csharp
private static readonly DateTime T = new DateTime(2026, 11, 9, 14, 5, 6);

[Fact] public void Backup_Name() => Assert.Equal(@"C:\p\A_3D.20261109_140506.bak", Panel3DPath.Backup(@"C:\p\A_3D.dwg", T, _ => false));
[Fact] public void Backup_TakenSameSecond()
{
    var taken = new HashSet<string> { @"C:\p\A_3D.20261109_140506.bak" };
    Assert.Equal(@"C:\p\A_3D.20261109_140506_2.bak", Panel3DPath.Backup(@"C:\p\A_3D.dwg", T, taken.Contains));
}

[Fact] public void Refusal_NoMeta() => Assert.Equal("A_3D.dwg has no panel data; use New copy.", Panel3DPath.UpdateRefusal("A_3D.dwg", null, "1B"));
[Fact] public void Refusal_OtherReference()
    => Assert.Equal("A_3D.dwg was built from another 2D block; use New copy.", Panel3DPath.UpdateRefusal("A_3D.dwg", new PanelMeta { SourceRefHandle = "2C" }, "1B"));
[Fact] public void Refusal_SameReference_Null() => Assert.Null(Panel3DPath.UpdateRefusal("A_3D.dwg", new PanelMeta { SourceRefHandle = "1b" }, "1B"));

[Fact] public void Unmatched_CountsMissingParts() => Assert.Equal(1, Panel3DPath.CountUnmatched(new[] { "1B#0", "2C#1" }, new[] { "1B#0" }));
[Fact] public void Unmatched_IgnoresGeneratedKeys() => Assert.Equal(0, Panel3DPath.CountUnmatched(new[] { "CollarPlate:x", "HpEndBracketB:y", "1B#0/GirderFlange#0" }, new string[0]));
```

- [ ] **Step 2: Run and confirm they fail**

Run: `dotnet test tests/MCG_3DPanel.Tests --filter "FullyQualifiedName~PanelMetaTests"`
Expected: build error, methods missing.

- [ ] **Step 3: Implement the three methods in `Commands/Panel3DPath.cs`**

Handle comparison ignores case. Backup number starts at `_2`.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/MCG_3DPanel.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Commands/Panel3DPath.cs tests/MCG_3DPanel.Tests/PanelMetaTests.cs
git commit -m "feat(update): rules for updating a 3D file"
```

### Task 2: Update choice and flow

**Files:**
- Modify: `Commands/Panel3DFile.cs`, `Commands/PanelCommands.cs` (`RunBuildCore`, `BuildInNewFile`, `PanelJob`), `UI/ExistingFileDialog.xaml`, `UI/ExistingFileDialog.xaml.cs`, `docs/ux-checklist.md`, `SESSION_LOG.md`

**Interfaces:**
- Consumes: `Panel3DPath.Backup`, `UpdateRefusal`, `CountUnmatched` from Task 1.
- Produces: `enum ExistingChoice { Update, Open, NewCopy, Cancel }`.
- Produces: `sealed class Panel3DFile.OldPanelData { PanelMeta Meta; string CorrectionsJson; string NamesJson; }`; `static OldPanelData Panel3DFile.ReadOld(string path)`.
- Produces: `Panel3DFile.Create(string file, ObjectId ref2D, string path, OldPanelData carry = null)`.

- [ ] **Step 1: Add `ReadOld` and the `carry` argument**

`ReadOld` opens `path` in a side database with shared read access and returns `Meta` (from `MCG3D_PANEL` on the first `MCG3D_*` definition, null if none) and the two XRecords from the 2D block definition at `Meta.SourceBtrHandle`. `Create`, when `carry` is given, writes both on the 2D reference's definition in the copy before `SaveAs`, using `CorrectionXRecord.Write`, replacing what came from the source.

- [ ] **Step 2: Add `Update` to the dialog**

`Update` first and default, using `DialogPrimaryButtonStyle`, with tooltip `Take the saved 2D drawing and keep this file's edits. The old file is kept as .bak.` `Open` becomes a normal button.

- [ ] **Step 3: Add the flow**

In `RunBuildCore`, for `Update`, mark jobs whose target is taken `job.Update = true`; those skip `NextFree`. A marked job in `BuildInNewFile` does, in order:

1. `FindOpen(target)`: if open, `Activate` it and test `IsSavedOnDisk`; unsaved stops the panel with the exact text; saved closes it with `CloseAndDiscard`.
2. `ReadOld`, then `UpdateRefusal` with the selected reference's handle; a refusal stops the panel.
3. `Create` to `<target>.new<guid>.dwg` with `carry`.
4. `File.Move(target, Panel3DPath.Backup(target, DateTime.Now, File.Exists), false)`, then `File.Move` the new file to `target`, no overwrite. If the second move fails, move the backup back.
5. Open, activate, `BuildOne` with `carry.Meta.Input` and `(PanelPosition)carry.Meta.Position`, save, as a normal build.
6. On any failure after step 4, including no solids: close without saving, delete the new `target`, move the backup back, count the panel failed, say so.

After a successful build, add `{n} edits no longer match the 2D drawing.` to the run's notes when `CountUnmatched` over the old correction, deleted and name keys, against the build's `ClassifiedParts` `SourceKey`s, is above zero. Stopped panels add their text to the run's notes; the others continue.

- [ ] **Step 4: Update docs**

`docs/ux-checklist.md`: rows for Update keeping a correction and a name on an unchanged part; a new 2D object; a deleted 2D object and its count; target open and unsaved; target open and saved; a failed build restoring the old file; Open and New copy unchanged. Add a SESSION_LOG entry saying this has not run in AutoCAD.

- [ ] **Step 5: Build and run tests**

Run: `dotnet build -c Debug MCG_3DPanel.csproj` then `dotnet test tests/MCG_3DPanel.Tests`
Expected: 0 errors, PASS.

- [ ] **Step 6: Commit**

```bash
git add Commands/Panel3DFile.cs Commands/PanelCommands.cs UI/ExistingFileDialog.xaml UI/ExistingFileDialog.xaml.cs docs/ux-checklist.md SESSION_LOG.md
git commit -m "feat(update): Update takes the saved 2D and keeps the 3D file's edits"
```
