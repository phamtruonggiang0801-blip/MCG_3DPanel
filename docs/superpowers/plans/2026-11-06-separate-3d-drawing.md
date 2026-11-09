# Separate 3D drawing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build 3D creates one self-contained `<panel>_3D.dwg` per panel, which fills the tabs by itself when reopened and rebuilds from the Input tab.

**Architecture:** The 3D file is a handle-preserving copy of the source drawing, reduced to one panel, with the 2D reference on a frozen layer. A JSON XRecord `MCG3D_PANEL` on the 3D block stores what `BuildResult` needs. On activation, `PanelSession` returns cached builds or restores them through read-only classification.

**Tech Stack:** C#, WPF, AutoCAD 2026 .NET API, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-06-separate-3d-drawing-design.md`

## Global Constraints

- The source 2D drawing is never written or dirtied.
- A 3D file is never overwritten.
- Filling tabs on open writes nothing: the file closes with no Save prompt.
- Files named `*Meta*.cs` and `Panel3DPath.cs` stay free of `Autodesk.*`.
- UI and command-line text is English; code comments are Vietnamese.
- Follow `.claude/skills/ux-guardrails/ux-guardrails_SKILL.md` and `.claude/skills/ui-guidelines/ui-guidelines_SKILL.md`.
- Layer name `MCG3D_2D_SOURCE`; XRecord key `MCG3D_PANEL`; file name `<panel block name>_3D.dwg`, then `_3D_2.dwg`.
- Every commit message ends with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A panel name with characters illegal in a file name → a valid path. (Task 2: `Target_IllegalChars_Replaced`)
2. Two references of one block in a selection → two different files. (Task 2: `NextFree_ReservedInSameBatch`)
3. `_3D.dwg` and `_3D_2.dwg` both exist → `_3D_3.dwg`. (Task 2: `NextFree_SkipsTaken`)
4. Corrupt or missing `MCG3D_PANEL` → no crash, panel not listed. (Task 2: `Meta_Invalid_Null`)
5. A stored solid erased by hand → its row is dropped and counted. (Task 2: `Meta_LiveSolids_DropsMissing`)

---

### Task 1: Spike and gate

**Files:**
- Create: `Commands/Spike3DFile.cs` (temporary, deleted in Task 7)

- [ ] **Step 1: Add command `MCG_Spike3DFile`**

The user picks a 2D panel reference. The command must:
1. `Database copy` = side database from `ReadDwgFile(db.Filename, FileOpenMode.OpenForReadAndAllShare, ...)`, stopping with a message if the drawing is unsaved or modified; compare the handles of the selected reference and all entities in its block tree against the source; print `handles kept: N / M`.
2. In `copy`, create layer `MCG3D_2D_SOURCE`, frozen and locked; move the reference onto it; `SaveAs` to `%TEMP%\mcg_spike_3D.dwg`.
3. From the palette context, not a command, open the file and make it active; print `opened: yes/no`.
4. In it, run `ClassificationService.Classify` on the reference at the same handle; print the part count, and the part count from the source.

Run step 3 from a temporary button on the Input tab, labelled `Spike`.

- [ ] **Step 2: Build**

Run: `dotnet build -c Debug MCG_3DPanel.csproj`
Expected: 0 errors.

- [ ] **Step 3: Commit, then STOP**

```bash
git add Commands/Spike3DFile.cs UI/PanelInputView.xaml UI/PanelInputView.xaml.cs
git commit -m "chore: spike for the separate 3D file"
```

The engineer runs it in AutoCAD. **Continue only if handles are all kept, the file opens, and both part counts match.** Otherwise stop and go back to the engineer; the spec makes this a hard gate.

### Task 2: Pure data: panel meta and target paths

**Files:**
- Create: `Services/Corrections/PanelMeta.cs`, `Commands/Panel3DPath.cs`
- Modify: `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj`
- Test: `tests/MCG_3DPanel.Tests/PanelMetaTests.cs`

**Interfaces:**
- Produces: `PanelMeta` with `string SourceRefHandle`, `string SourceBtrHandle`, `PanelInput Input`, `int Position`, `List<PanelMetaSolid> Solids`; `string ToJson()`; `static PanelMeta FromJson(string)` (null when invalid); `List<PanelMetaSolid> LiveSolids(Func<string, bool> exists, out int dropped)`.
- Produces: `PanelMetaSolid` with `string Handle, Type, PartKey, FlipKey, DeleteKey, Code`, `List<string> MergedCodes`, `bool Deleted`.
- Produces: `static string Panel3DPath.Target(string folder, string panelName)`; `static string Panel3DPath.NextFree(string target, Func<string, bool> taken)`.

- [ ] **Step 1: Write the failing tests**

```csharp
[Fact] public void Meta_RoundTrip()
{
    var m = new PanelMeta { SourceRefHandle = "1B", SourceBtrHandle = "2C", Input = new PanelInput { WebHeight = 400 }, Position = 2 };
    m.Solids.Add(new PanelMetaSolid { Handle = "A1", PartKey = "1B5#0", DeleteKey = "1B5#0", Code = "GE-HP", MergedCodes = { "GE-HP" } });
    var b = PanelMeta.FromJson(m.ToJson());
    Assert.Equal("1B", b.SourceRefHandle);
    Assert.Equal(400, b.Input.WebHeight);
    Assert.Equal(2, b.Position);
    Assert.Equal("GE-HP", b.Solids[0].MergedCodes[0]);
}

[Theory] [InlineData(null)] [InlineData("")] [InlineData("{bad")]
public void Meta_Invalid_Null(string json) => Assert.Null(PanelMeta.FromJson(json));

[Fact] public void Meta_LiveSolids_DropsMissing()
{
    var m = new PanelMeta();
    m.Solids.Add(new PanelMetaSolid { Handle = "A" });
    m.Solids.Add(new PanelMetaSolid { Handle = "B" });
    m.Solids.Add(new PanelMetaSolid { Handle = "C", Deleted = true });
    var live = m.LiveSolids(h => h == "A", out int dropped);
    Assert.Equal(new[] { "A", "C" }, live.Select(s => s.Handle));
    Assert.Equal(1, dropped);
}

[Fact] public void Target_Plain() => Assert.Equal(@"C:\p\7D-14P_Assy_3D.dwg", Panel3DPath.Target(@"C:\p", "7D-14P_Assy"));
[Fact] public void Target_IllegalChars_Replaced() => Assert.Equal(@"C:\p\A_B_3D.dwg", Panel3DPath.Target(@"C:\p", "A/B"));
[Fact] public void NextFree_FreeStaysSame() => Assert.Equal(@"C:\p\A_3D.dwg", Panel3DPath.NextFree(@"C:\p\A_3D.dwg", _ => false));
[Fact] public void NextFree_SkipsTaken()
{
    var taken = new HashSet<string> { @"C:\p\A_3D.dwg", @"C:\p\A_3D_2.dwg" };
    Assert.Equal(@"C:\p\A_3D_3.dwg", Panel3DPath.NextFree(@"C:\p\A_3D.dwg", taken.Contains));
}
[Fact] public void NextFree_ReservedInSameBatch()
{
    var reserved = new HashSet<string>();
    var a = Panel3DPath.NextFree(@"C:\p\A_3D.dwg", reserved.Contains); reserved.Add(a);
    Assert.Equal(@"C:\p\A_3D_2.dwg", Panel3DPath.NextFree(@"C:\p\A_3D.dwg", reserved.Contains));
}
```

- [ ] **Step 2: Run and confirm they fail**

Run: `dotnet test tests/MCG_3DPanel.Tests --filter "FullyQualifiedName~PanelMetaTests"`
Expected: build error, types missing.

- [ ] **Step 3: Implement both files and link them in the test csproj**

`PanelMeta` uses `System.Text.Json`, as `NamesJson` does. `Position` is an int so the class stays independent of the enum cast.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/MCG_3DPanel.Tests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Services/Corrections/PanelMeta.cs Commands/Panel3DPath.cs tests/MCG_3DPanel.Tests
git commit -m "feat(panel): stored panel data and 3D file paths"
```

### Task 3: Write `MCG3D_PANEL` on Build and rebuild

**Files:**
- Modify: `Services/Corrections/CorrectionXRecord.cs`, `Commands/PanelCommands.cs` (`BuildOne`, `RebuildOne`)

**Interfaces:**
- Consumes: `PanelMeta`, `PanelMetaSolid` from Task 2.
- Produces: `CorrectionXRecord.PanelKey = "MCG3D_PANEL"`; `internal static void PanelCommands.WritePanelMeta(Database db, BuildResult build)`.

- [ ] **Step 1: Implement**

`WritePanelMeta` fills one `PanelMetaSolid` per `BuiltSolid`: `Handle` from `SolidId`, `PartKey` from `Part?.SourceKey`, the rest copied. `DeleteKey` rows with `Deleted = true` are kept. Write it on `build.BtrId` in the same undo step as the build. Call it at the end of `BuildOne`, and in `RebuildOne` after the rename. A failed write adds to `build.Warnings`, telling the user the panel will not fill the tabs when reopened.

- [ ] **Step 2: Build and run tests**

Run: `dotnet build -c Debug MCG_3DPanel.csproj` then `dotnet test tests/MCG_3DPanel.Tests`
Expected: 0 errors, PASS.

- [ ] **Step 3: Commit**

```bash
git add Services/Corrections/CorrectionXRecord.cs Commands/PanelCommands.cs
git commit -m "feat(panel): save panel data in the 3D block"
```

### Task 4: Create the 3D file on Build

**Files:**
- Create: `Commands/Panel3DFile.cs`, `UI/ExistingFileDialog.xaml`, `UI/ExistingFileDialog.xaml.cs`
- Modify: `Commands/PanelCommands.cs` (`RunBuild`, remove `AskKeep`), `UI/PanelInputView.xaml.cs`

**Interfaces:**
- Consumes: `Panel3DPath` from Task 2.
- Produces: `static void Panel3DFile.Create(Database source, ObjectId ref2D, string path)`: writes the file and throws with an English message on failure.
- Produces: `enum ExistingChoice { Open, NewCopy, Cancel }`; `static ExistingChoice ExistingFileDialog.Ask(IEnumerable<string> fileNames)`.
- Produces: `RunBuild` returns the builds of the last opened file; `event Action<Database, List<BuildResult>> PanelCommands.FileBuilt` for each file.

- [ ] **Step 1: Implement `Panel3DFile.Create`**

Steps as in the spec's File creation: read `source.Filename` into a side database with shared read access, erase Model Space entities except `ref2D`, erase `MCG3D_*` block definitions, move the reference to `MCG3D_2D_SOURCE` (frozen, locked), purge with `Database.Purge` until nothing is left except what the reference needs, then `SaveAs`. Dispose the copy.

- [ ] **Step 2: Add the dialog**

Text `These panels already have a 3D file:`, then the file names, then buttons `Open`, `New copy`, `Cancel`. Show with `Application.ShowModalWindow`. Closing the window means Cancel.

- [ ] **Step 3: Rewrite `RunBuild`**

1. If the drawing has never been saved or has unsaved changes (`DBMOD`), stop before selection with `Save this drawing first, then Build 3D.` Then `TryPickPanels`, and compute a target per panel with `Panel3DPath`, in the folder of `db.Filename`. A file counts as taken if it exists or is open in `DocumentManager`.
2. Any target taken: ask once. Cancel returns null with nothing created. Open opens those files, or switches to one already open, and skips their build. New copy uses `NextFree`, reserving paths within the batch.
3. For each panel to build: `Create`, open the file, make it active, lock it, find the reference by the same handle, run `BuildOne`, save the file, raise `FileBuilt`. No solids: close the document, delete the file, tell the user, continue. A panel that throws is reported and skipped.

`AskKeep` and the Keep branch go. `Kept` is always true in a 3D file. The source document is not locked for write.

- [ ] **Step 4: Update `BtnBuild_Click` status**

`Built {solids} solids in {n} 3D file(s).`, plus the existing failure notes.

- [ ] **Step 5: Build and run tests**

Run: `dotnet build -c Debug MCG_3DPanel.csproj` then `dotnet test tests/MCG_3DPanel.Tests`
Expected: 0 errors, PASS.

- [ ] **Step 6: Commit**

```bash
git add Commands UI
git commit -m "feat(build): Build 3D creates one self-contained 3D file per panel"
```

### Task 5: Fill the tabs from the active file

**Files:**
- Create: `Commands/PanelSession.cs`
- Modify: `UI/PaletteManager.cs`, `UI/PanelResultView.xaml.cs`, `UI/PanelNamingView.xaml.cs`

**Interfaces:**
- Consumes: `PanelMeta`, `CorrectionXRecord.PanelKey`, `PanelCommands.FileBuilt`.
- Produces: `static List<BuildResult> PanelSession.For(Database db, out string message)`; `static void PanelSession.Set(Database db, List<BuildResult> builds)`; `static void PanelSession.Forget(Database db)`.

- [ ] **Step 1: Implement `PanelSession`**

A cache by `Database`. On a miss, in a read-only `StartOpenCloseTransaction`, find block references named `MCG3D_*` whose definition has `MCG3D_PANEL`. For each: `FromJson`; resolve handles; `CorrectionStore.EnsureLoaded` and `NameStore.EnsureLoaded`; `ClassificationService.Classify` on the source reference with stored corrections; build `BuildResult` with `Kept = true`, `Input`, `Position`, `SourceRefId`, `SourceBtrId`, `SourceTransform`, `ClassifiedParts`; add `BuiltSolid` from `LiveSolids`, with `Part` found by `PartKey` and `Bounds` from the solid. `message` reports dropped solids, unreadable panels, and old blocks with no data: `Old 3D block {name} — run Build 3D on its 2D block again.` Nothing is written.

- [ ] **Step 2: Connect the tabs**

On `DocumentBecameCurrent`, and when the palette opens: `PanelSession.For(active db)`, then `Load` on Result and Naming, replacing the rows, and show `message`. `FileBuilt` and rebuild call `Set`. `DocumentToBeDestroyed` calls `Forget`. Remove the "rows belong to drawing X" text that this makes unreachable. Keep database guards on highlight and Pick.

- [ ] **Step 3: Build and run tests**

Run: `dotnet build -c Debug MCG_3DPanel.csproj` then `dotnet test tests/MCG_3DPanel.Tests`
Expected: 0 errors, PASS.

- [ ] **Step 4: Commit**

```bash
git add Commands/PanelSession.cs UI
git commit -m "feat(panel): tabs fill from the active 3D file, nothing written on open"
```

### Task 6: Rebuild 3D from the Input tab

**Files:**
- Modify: `UI/PanelInputView.xaml.cs`, `UI/PaletteManager.cs`, `Commands/PanelCommands.cs`

**Interfaces:**
- Consumes: `PanelSession.For`.
- Produces: `PanelInputView.ShowPanel(BuildResult panelOrNull)`: panel present fills fields with its `Input` and `Position` and sets the button to `Rebuild 3D`; null restores `Build 3D`.

- [ ] **Step 1: Implement**

Call `ShowPanel` with the first build of the active file whenever tabs refresh. `BtnBuild_Click` with a panel: `TryReadInput`, set `Input` and `Position` on that `BuildResult`, then `PanelCommands.RequestRebuild`. Result, Naming and `MCG3D_PANEL` update through the normal rebuild.

- [ ] **Step 2: Build and run tests**

Run: `dotnet build -c Debug MCG_3DPanel.csproj` then `dotnet test tests/MCG_3DPanel.Tests`
Expected: 0 errors, PASS.

- [ ] **Step 3: Commit**

```bash
git add UI Commands/PanelCommands.cs
git commit -m "feat(input): Rebuild 3D applies Input values inside the 3D file"
```

### Task 7: Remove the spike, docs

**Files:**
- Delete: `Commands/Spike3DFile.cs`
- Modify: `UI/PanelInputView.xaml`, `UI/PanelInputView.xaml.cs`, `docs/ux-checklist.md`, `.claude/skills/structure-naming/structure-naming_SKILL.md`, `SESSION_LOG.md`

- [ ] **Step 1: Remove the spike command and button**

Run: `grep -rn "Spike" --include=*.cs --include=*.xaml Commands UI`
Expected: no output.

- [ ] **Step 2: Update docs**

`docs/ux-checklist.md`: rows for a build from 2D leaving the source unmodified; several panels; existing-file dialog (Open, New copy, Cancel) and the case of a file already open; reopen with no Save prompt on close; a solid erased by hand; Rebuild 3D from Input keeping corrections and names; a panel with no solids deleting its file. Rewrite B3 and G16 for the 3D file. Skill and SESSION_LOG: the new flow.

- [ ] **Step 3: Build and run tests**

Run: `dotnet build -c Debug MCG_3DPanel.csproj` then `dotnet test tests/MCG_3DPanel.Tests`
Expected: 0 errors, PASS.

- [ ] **Step 4: Commit**

```bash
git add -A Commands UI docs/ux-checklist.md .claude/skills/structure-naming SESSION_LOG.md
git commit -m "docs: separate 3D drawing, remove the spike"
```
