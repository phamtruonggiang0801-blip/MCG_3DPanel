# Naming split, Result toolbar, English prompts Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Unmerge splits automatic lines, Load 3D is removed, CSV and STEP export sit behind one dropdown, and the command line is English.

**Architecture:** A per-panel set of split `SourceKey`s, saved in `MCG3D_NAMES`, excludes pieces from automatic grouping in `StructureNaming.Lines`; manual Merge still runs afterwards. Load 3D and the code only it uses are deleted. The Result toolbar swaps two buttons for one menu button.

**Tech Stack:** C#, WPF, AutoCAD .NET API, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-06-naming-split-result-export-design.md`

## Global Constraints

- `Naming/StructureNaming.cs` and `Naming/NamesJson.cs` stay free of `Autodesk.*`.
- All UI and command-line text is English. Code comments stay Vietnamese.
- Styles live in `UI/PanelStyles.xaml`; no inline styles. Use `SetStatus` for feedback, per `.claude/skills/ui-guidelines` and `.claude/skills/ux-guardrails`.
- Existing behaviour with no split: existing tests pass unmodified, except `SourceMetaTests`, which Task 3 deletes.
- Build-report logs and warnings stay as they are, except the one line Task 3 deletes.
- Every commit message ends with a blank line, then `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. A stored split key whose piece no longer exists → ignored, no crash. (Task 1: `Split_UnknownKey_Ignored`)
2. Split, Merge two rows, then Unmerge → the rows return to split, not to the automatic line. (Task 1: `Split_MergeThenUnmerge_BackToSplit`)
3. A split piece that was Del Named → stays out, the others keep their numbers. (Task 1: `Split_DeletedPiece_NoRow`)
4. Pieces of one line split on a Starboard panel, equal Center → numbering stays stable for either input order. (Task 1: `Split_EqualCenter_StableOrder`)
5. An old `MCG3D_NAMES` JSON with no `Splits` field → loads with an empty set. (Task 1: `NamesJson_NoSplits_Empty`)

---

### Task 1: Split in grouping and JSON

**Files:**
- Modify: `Naming/StructureNaming.cs`, `Naming/NamesJson.cs`
- Test: `tests/MCG_3DPanel.Tests/StructureNamingTests.cs`

**Interfaces:**
- Produces: `StructureNaming.Lines(pieces, pos, deleted = null, merges = null, ICollection<string> splits = null)`.
- Produces: `public static bool StructureNaming.CanSplit(NamingLine line)`: not `IsMerged`, and two or more distinct non-null piece keys.
- Produces: `NamesJson.Serialize(names, deleted, merges, IEnumerable<string> splits = null)`; `Doc.Splits` (`List<string>`); overload `Deserialize(string json, out HashSet<string> deleted, out List<HashSet<string>> merges, out HashSet<string> splits)`.

- [ ] **Step 1: Write the failing tests**

```csharp
private static NamingPiece[] Three() => new[]
{
    Spec(VY(NamingKind.Web, 100, 0, 1000, key: "a")),
    Spec(VY(NamingKind.Web, 100, 1100, 2000, key: "b")),
    Spec(VY(NamingKind.Web, 100, 2100, 3000, key: "c")),
};

[Fact] public void CanSplit_Rules()
{
    var l = StructureNaming.Lines(Three(), PanelPosition.Center);
    Assert.True(StructureNaming.CanSplit(l.Single()));
    var one = StructureNaming.Lines(new[] { Spec(VY(NamingKind.Web, 100, 0, 1000, key: "a")) }, PanelPosition.Center);
    Assert.False(StructureNaming.CanSplit(one.Single()));
}

[Fact] public void Split_EachKeyOwnLine()
{
    var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, null, new[] { "a", "b", "c" });
    Assert.Equal(new[] { "T1", "T2", "T3" }, Names(l, NamingKind.Web, NamingDir.T));
    Assert.Equal("a", Line(l, "T1").Pieces.Single().Key);
    Assert.Equal("c", Line(l, "T3").Pieces.Single().Key);
}

[Fact] public void Split_OnlyOneKey_RestStayTogether()
{
    var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, null, new[] { "a" });
    Assert.Equal(2, l.Count);
    Assert.Contains(l, x => x.Pieces.Count == 2);
}

[Fact] public void Split_SameKeyPartsStayTogether()
{
    var p = new[] { Spec(VY(NamingKind.Web, 100, 0, 1000, key: "a")), Spec(VY(NamingKind.Web, 100, 1100, 2000, key: "a")), Spec(VY(NamingKind.Web, 100, 2100, 3000, key: "b")) };
    var l = StructureNaming.Lines(p, PanelPosition.Center, null, null, new[] { "a", "b" });
    Assert.Equal(2, l.Count);
    Assert.Equal(2, l.Single(x => x.Pieces.Any(q => q.Key == "a")).Pieces.Count);
}

[Fact] public void Split_HpOnWebLine_BecomesHp()
{
    var p = new[] { Spec(HX(NamingKind.Web, 100, 0, 5000, key: "w")), Spec(HX(NamingKind.HP, 100, 5100, 6000, key: "h")) };
    Assert.Single(StructureNaming.Lines(p, PanelPosition.Center));
    var l = StructureNaming.Lines(p, PanelPosition.Center, null, null, new[] { "h" });
    Assert.Equal("HL1", l.Single(x => x.Pieces.Any(q => q.Key == "h")).Name);
}

[Fact] public void Split_Skew_EachKeyOwnLine()
{
    var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center, null, null, new[] { "1AE23", "1AE5D" });
    Assert.Single(l.Single(x => x.Pieces.Any(q => q.Key == "1AE23")).Pieces);
    Assert.Single(l.Single(x => x.Pieces.Any(q => q.Key == "1AE5D")).Pieces);
}

[Fact] public void Split_MergeRecombines()
{
    var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, new[] { new[] { "a", "b" } }, new[] { "a", "b", "c" });
    Assert.Equal(2, l.Count);
    Assert.True(l.Single(x => x.Pieces.Count == 2).IsMerged);
}

[Fact] public void Split_MergeThenUnmerge_BackToSplit()
{
    var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, new string[0][], new[] { "a", "b", "c" });
    Assert.Equal(3, l.Count);
}

[Fact] public void Split_UnknownKey_Ignored()
{
    var l = StructureNaming.Lines(Three(), PanelPosition.Center, null, null, new[] { "gone" });
    Assert.Single(l);
}

[Fact] public void Split_DeletedPiece_NoRow()
{
    var l = StructureNaming.Lines(Three(), PanelPosition.Center, new[] { "b" }, null, new[] { "a", "b", "c" });
    Assert.Equal(new[] { "T1", "T2" }, Names(l, NamingKind.Web, NamingDir.T));
    Assert.DoesNotContain(l, x => x.Pieces.Any(q => q.Key == "b"));
}

[Fact] public void Split_EqualCenter_StableOrder()
{
    var a = StructureNaming.Lines(Three(), PanelPosition.Starboard, null, null, new[] { "a", "b", "c" });
    var b = StructureNaming.Lines(Three().Reverse(), PanelPosition.Starboard, null, null, new[] { "a", "b", "c" });
    Assert.Equal(a.Select(x => x.Pieces[0].Key + x.Name), b.Select(x => x.Pieces[0].Key + x.Name));
}

[Fact] public void NamesJson_Splits_RoundTrip()
{
    var json = NamesJson.Serialize(new Dictionary<string, string>(), null, null, new[] { "a", "b" });
    NamesJson.Deserialize(json, out _, out _, out HashSet<string> s);
    Assert.Equal(new[] { "a", "b" }, s.OrderBy(k => k).ToArray());
}

[Fact] public void NamesJson_NoSplits_Empty()
{
    NamesJson.Deserialize("{\"Version\":1,\"Names\":{\"a\":\"T1\"}}", out _, out _, out HashSet<string> s);
    Assert.Empty(s);
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/MCG_3DPanel.Tests --filter "FullyQualifiedName~StructureNamingTests"`
Expected: build error on the new `Lines` argument, `CanSplit` and `Deserialize` overload.

- [ ] **Step 3: Implement**

In `Lines`, after the `deleted` filter, take Web, HP and FB pieces whose key is in `splits` out of `all` before grouping. Flange pieces stay. After automatic grouping and before the Merge loop, add one line per distinct key: kind from the piece, `dir` from the longest piece, built with `Make`. Everything after that is unchanged.

Implement `CanSplit`, the `Doc.Splits` field, `Serialize` and the `Deserialize` overload following the existing `Merges` code. The existing `Deserialize` overloads keep their signatures.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/MCG_3DPanel.Tests`
Expected: PASS, existing tests unmodified.

- [ ] **Step 5: Commit**

```bash
git add Naming/StructureNaming.cs Naming/NamesJson.cs tests/MCG_3DPanel.Tests/StructureNamingTests.cs
git commit -m "feat(naming): split keys leave automatic grouping, saved as Splits"
```

### Task 2: Unmerge and Reset on the Naming tab

**Files:**
- Modify: `Naming/NameStore.cs`, `Naming/NamingSource.cs:33-37`, `UI/PanelNamingView.xaml`, `UI/PanelNamingView.xaml.cs:176-188,272-310`

**Interfaces:**
- Consumes: `StructureNaming.Lines(..., splits)`, `StructureNaming.CanSplit`, `NamesJson` from Task 1.
- Produces: `NameStore.Splits(string panelKey) : ISet<string>` style accessor, `NameStore.Split(string panelKey, NamingLine line)`, `NameStore.Unsplit(string panelKey, NamingLine line) : bool`.

- [ ] **Step 1: Add storage to `NameStore`**

A `_splits` dictionary keyed by panel, handled like `_merges`: loaded in `EnsureLoaded` from the new `Deserialize` overload, included in the "anything to save" check and in `Serialize` inside `Save`, removed in the drawing-closed cleanup.

- [ ] **Step 2: Pass splits in `NamingPanel.Recompute`**

`StructureNaming.Lines(Pieces, Position, NameStore.Deleted(PanelKey), NameStore.Merges(PanelKey), NameStore.Splits(PanelKey))`.

- [ ] **Step 3: Change `MnuUnmerge_Click`**

Qualifying rows: `r.Line.IsMerged || StructureNaming.CanSplit(r.Line)`. For each target, `IsMerged` calls `NameStore.Unmerge`; otherwise `NameStore.Split`. The existing keep-name-on-longest logic applies to both. Empty selection status: `"Select a merged line, or a line of two or more pieces."`. Report verb stays `"Unmerged"`.

- [ ] **Step 4: Change `GridMenu_Opened` and the menu text**

`MnuUnmerge` is enabled when any selected row qualifies. Its tooltip when greyed out: `"This line has one piece — nothing to split."`. Enabled tooltip: `"Split the selected lines into their pieces, then renumber. Use Merge to put pieces back together."`. Show the tooltip on disabled, as `MnuMerge` does.

- [ ] **Step 5: Change `MnuReset_Click`**

The plan filter becomes `r.IsUserName || r.Line.Pieces.Any(p => split set contains p.Key)`. Unsplit every target, recompute the touched panels and save, then apply the reset as today. Rows that were only split must also rejoin, so the action runs when either condition holds. Keep the "already has its auto name" messages for rows with neither.

- [ ] **Step 6: Run tests and build**

Run: `dotnet test tests/MCG_3DPanel.Tests` then `dotnet build -c Debug MCG_3DPanel.csproj`
Expected: tests PASS, 0 errors.

- [ ] **Step 7: Commit**

```bash
git add Naming/NameStore.cs Naming/NamingSource.cs UI/PanelNamingView.xaml UI/PanelNamingView.xaml.cs
git commit -m "feat(naming): Unmerge splits automatic lines, Reset to auto rejoins them"
```

### Task 3: Remove Load 3D

**Files:**
- Modify: `UI/PanelResultView.xaml:45-46`, `UI/PanelResultView.xaml.cs:45,54,81,116,624-632`, `Commands/PanelCommands.cs`, `Commands/EntityHighlighter.cs:45`, `Services/Corrections/CorrectionXRecord.cs:16`, `UI/PanelNamingView.xaml:87`, `UI/PanelNamingView.xaml.cs:36,59,108`, `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj:18`, `docs/ux-checklist.md:32,76,115`, `.claude/skills/structure-naming/structure-naming_SKILL.md:40`
- Delete: `Services/Corrections/SourceMeta.cs`, `tests/MCG_3DPanel.Tests/SourceMetaTests.cs`

- [ ] **Step 1: Delete the feature**

Remove `BtnLoad3D` and `BtnLoad3D_Click`; `Load3DCommand`; `StubFrom3D`; `WriteSourceMeta` and its call; `InsertTempSource`; `SourceMeta`; `CorrectionXRecord.SourceKey`; `SourceMetaTests` and its csproj link. Remove `CorrectionXRecord.ReadPointer` and the pointer argument on `Write` only if nothing else uses them. In `RebuildOne`, drop the temp-insert branch and `tempRef`: when `SourceStillExists` is false, raise the existing `"Source 2D block no longer exists — cannot rebuild."` and return.

- [ ] **Step 2: Change the texts**

- `"No panel yet. Run Build 3D (Input tab)."` in both Naming places.
- `PanelResultView.xaml.cs:116` and `EntityHighlighter.cs:45`: end with `or run Build 3D here.`
- Vietnamese comments that name Load 3D: drop the mention.
- `docs/ux-checklist.md`: B3 becomes "Save, close, reopen the drawing. The Result and Naming tabs are empty; Build 3D on an inserted 2D block brings back corrections and names." Delete F5. Line 115 reads "come from Build 3D".

- [ ] **Step 3: Verify nothing remains**

Run: `grep -rn "Load 3D\|Load3D\|SourceMeta\|StubFrom3D\|InsertTempSource" --include=*.cs --include=*.xaml --include=*.csproj Commands UI Services Naming tests/MCG_3DPanel.Tests/*.cs tests/MCG_3DPanel.Tests/*.csproj`
Expected: no output.

- [ ] **Step 4: Run tests and build**

Run: `dotnet test tests/MCG_3DPanel.Tests` then `dotnet build -c Debug MCG_3DPanel.csproj`
Expected: tests PASS, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add -A Commands UI Services tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj tests/MCG_3DPanel.Tests/SourceMetaTests.cs docs/ux-checklist.md .claude/skills/structure-naming
git commit -m "feat(result): remove Load 3D — the tool works with panels built in this session"
```

### Task 4: Export dropdown

**Files:**
- Modify: `UI/PanelResultView.xaml:47-51`, `UI/PanelResultView.xaml.cs:29-30`

- [ ] **Step 1: Replace the two buttons**

One button `BtnExportMenu`, `Content="Export ▾"`, `ToolbarPrimaryButtonStyle`, `ToolTip="Export the table to CSV, or each panel to STEP"`, with `Click="BtnExportMenu_Click"`. Its `ContextMenu` holds `MnuExportCsv` (`Header="CSV table"`, `Click="BtnExport_Click"`, tooltip `"Export the table (incl. corrections) to a CSV file"`) and `MnuExportStep` (`Header="STEP files (one per panel)"`, `Click="BtnStep_Click"`, tooltip `"Export each panel to its own STEP file (<Panel>.stp)"`, `ToolTipService.ShowOnDisabled="True"`).

`BtnExportMenu_Click` sets `PlacementTarget` to the button, `Placement` to `Bottom`, and `IsOpen = true`. The right-click item "Export Table to CSV..." stays.

- [ ] **Step 2: Move the Mechanical check**

Lines 29-30 act on `MnuExportStep` with the same reason text. Update every other `BtnExport` / `BtnStep` reference found by grep, keeping the handler names.

- [ ] **Step 3: Build**

Run: `dotnet build -c Debug MCG_3DPanel.csproj`
Expected: 0 errors.

- [ ] **Step 4: Commit**

```bash
git add UI/PanelResultView.xaml UI/PanelResultView.xaml.cs
git commit -m "feat(result): Export dropdown with CSV and STEP"
```

### Task 5: English prompts and docs

**Files:**
- Modify: `Commands/PanelCommands.cs`, `.claude/skills/structure-naming/structure-naming_SKILL.md`, `SESSION_LOG.md`

- [ ] **Step 1: Translate**

Replace these exact strings (line numbers from before Task 3; find by text):

| Old | New |
|---|---|
| `Đã nạp. Gõ MCG_Build3DPanel để mở bảng điều khiển.` | `Loaded. Type MCG_Build3DPanel to open the palette.` |
| `{tag} Không dựng được solid nào — bỏ.` | `{tag} No solid could be built — skipped.` |
| `{tag} Lỗi khi dựng panel {ids[i].Handle}: {ex.Message} — bỏ qua.` | `{tag} Error building panel {ids[i].Handle}: {ex.Message} — skipped.` |
| `Đã giữ 3D ({withSolids.Count} panel), xoá block 2D.` | `Kept 3D ({withSolids.Count} panel(s)), 2D block deleted.` |
| `Đã bỏ mô hình 3D.` | `3D model discarded.` |
| `Không ghi được build report: {ex.Message}` | `Could not write the build report: {ex.Message}` |
| `Giữ mô hình 3D của {panelCount} panel và xoá block 2D?` | `Keep the 3D model of {panelCount} panels and delete the 2D blocks?` |
| `Giữ mô hình 3D và xoá block 2D?` | `Keep the 3D model and delete the 2D block?` |
| `Rebuild: không thay được block cũ ({ex.Message}).` | `Rebuild: could not replace the old block ({ex.Message}).` |
| `Chọn (các) Block Panel:` | `Select panel block(s):` |
| `Đã huỷ.` | `Cancelled.` |
| `Đã chọn {ids.Length} block panel.` | `{ids.Length} panel block(s) selected.` |

- [ ] **Step 2: Verify no Vietnamese command-line text remains**

Run a search over `Commands` and `UI` for `WriteMessage`, `MessageFor`, `Prompt*Options` and `Keywords` lines with non-ASCII letters outside `—`, `→`, `⇒`.
Expected: none. Fix any that the table missed, translating in the same style.

- [ ] **Step 3: Update docs**

In the skill: Unmerge splits automatic lines, `Splits` in `MCG3D_NAMES`, Reset to auto rejoins, Merge recombines. Add a 2026-11-06 entry to `SESSION_LOG.md` covering all four changes.

- [ ] **Step 4: Run tests and build**

Run: `dotnet test tests/MCG_3DPanel.Tests` then `dotnet build -c Debug MCG_3DPanel.csproj`
Expected: tests PASS, 0 errors.

- [ ] **Step 5: Commit**

```bash
git add Commands/PanelCommands.cs .claude/skills/structure-naming/structure-naming_SKILL.md SESSION_LOG.md
git commit -m "feat(ui): English command-line prompts, docs"
```
