# Naming Tab — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** A Naming tab that auto-names Web/HP/BS lines of a panel (T#, L#, HT#, HL#, BS) and lets the user rename them.

**Architecture:** A pure C# namer (`Naming/StructureNaming.cs`) turns POCO pieces into named lines. An AutoCAD adapter
feeds it the classified parts (from a 2D-only classification or from a build), a `NameStore` keeps user names in an
XRecord on the 2D block definition, and a new WPF tab shows and edits the lines.

**Tech Stack:** C# net8.0-windows, AutoCAD .NET, WPF PaletteSet, xUnit (pure files linked into the test project).

**Spec:** `docs/superpowers/specs/2026-11-05-naming-tab-design.md`

## Global Constraints
- UI text in English. ux-guardrails rules 1–3: ObjectIds of another drawing are never touched (skip + report), no
  silent catch on a click path, in-cell controls use the Cell* styles of `UI/PanelStyles.xaml`.
- Collinear tolerance **15 mm** (centre lines). FB pieces of one bar may be separated by a gap ≤ **30 mm** (split at HPs).
- Geometry is read in the 2D block's own frame (inverse block transform).
- Ordering: T/HT by X ascending; L/HL by Y ascending for Center and Port, descending for Starboard.
- XRecord key `MCG3D_NAMES`; only user-typed names are stored; renaming never rebuilds.

## Review Focus
1. Short stray HP/Web pieces (corner HPs, e.g. 7D-16C y≈42/57, x≈1152) create extra lines and shift numbering. Expected: they
   get a number, the user can rename; the reviewer checks the real 7D-16C result in AutoCAD and reports the shift.
   Pinned by `Lines_ShortPieceMakesOwnLine` (documents the behaviour).
2. A stored name whose pieces no longer exist (2D edited) must be ignored, not crash. Pinned by `Resolve_StaleKeysIgnored`.
3. A line with pieces carrying two different stored names shows the most common. Pinned by `Resolve_MostCommonName`.
4. Rotated/moved 2D block must give the same names. Pinned by the adapter using the block frame; reviewer check.
5. Panel from another drawing on the grid (rule 1): edit/rename is refused with the drawing named. Reviewer check vs
   checklist A1–A6.

---

### Task 1: Pure namer + JSON

**Files:**
- Create: `Naming/StructureNaming.cs`, `Naming/NamesJson.cs`
- Modify: `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj` (link both)
- Test: `tests/MCG_3DPanel.Tests/StructureNamingTests.cs`

**Interfaces:**
- Produces:
  - `enum NamingKind { Web, HP, FB }`, `enum NamingDir { T, L }` (T = along Y, L = along X).
  - `sealed class NamingPiece { string Key; NamingKind Kind; double X0, Y0, X1, Y1; double Thickness; string Spec; }`
    (endpoints of the centre line in the block frame).
  - `sealed class NamingLine { NamingKind Kind; NamingDir Dir; double Center; IReadOnlyList<NamingPiece> Pieces;
    double Length; string AutoName; string Name; bool IsUserName; }`
  - `static IReadOnlyList<NamingLine> StructureNaming.Lines(IEnumerable<NamingPiece> pieces, PanelPosition pos)` —
    groups, picks kind, numbers; `Name = AutoName`.
  - `static void StructureNaming.Resolve(IReadOnlyList<NamingLine> lines, IReadOnlyDictionary<string,string> stored)` —
    sets `Name`/`IsUserName` from stored names (most common among the line's piece keys; ties → first in piece order).
  - `static string NamesJson.Serialize(IReadOnlyDictionary<string,string>)` / `Dictionary<string,string> Deserialize(string)`
    (null/empty/garbage → empty dictionary).
- Rules: direction = nearest axis of (X1−X0, Y1−Y0); Web+HP group when same Dir and |centre diff| ≤ 15; line kind =
  larger total length (tie → Web); FB groups only with FB, same Dir, centre ≤ 15 **and** axial gap ≤ 30; FB name "BS".
  Lines ordered by `Center` then numbered per counter (`T`, `L`, `HT`, `HL`). Grid order: Web T, Web L, HP T, HP L, FB.

- [ ] **Step 1: Write the failing tests** — fixture `Cas7D16C()` built from CAS.dump.json (block frame):
  - Webs along Y at x = 537 (pieces thk 6 and 20, centre 530 and 537), 2483, 4423 (4 pieces: y 90–446, 452–10057,
    10063–10908, 10917–11296), 6363, 8303, 10243, 10967 (y 11725–14177), 12233.
  - Webs along X at y = 449, 10060, 12497 (x 10246–12230 and 12236–12710), 13768.
  - HP along Y at x = 57, 12713. HP along X at y = 87, 903, 1678, 2453, 3228, 4003, 4778, 5553, 6322, 7097, 7847, 8622,
    9397, 10465, 10947, 11722, 13262, 14180, plus HP `1B2E9` at y = 12497, x 8336–10210.
  - FB along Y at x = 1043, 1983, 2983, 3923, 4923, 5863, 6863, 7803, 8803, 9743, 10743, 11733 (y 906–9394, the
    x=7803 one also split 10468–11719 and 11725–12351); FB along X at y = 9710 (pieces 570–8270, 8336–8800).

```csharp
[Fact] public void Lines_Cas7D16C_Webs()   // T1..T8 (T6a → T7, T7 → T8), L1..L4
{ var l = StructureNaming.Lines(Cas7D16C(), PanelPosition.Center);
  Assert.Equal(new[]{"T1","T2","T3","T4","T5","T6","T7","T8"}, Names(l, NamingKind.Web, NamingDir.T));
  Assert.Equal(new[]{"L1","L2","L3","L4"}, Names(l, NamingKind.Web, NamingDir.L));
  Assert.Equal(4, Line(l, "T3").Pieces.Count); Assert.Equal(2, Line(l, "T1").Pieces.Count); }
[Fact] public void Lines_Hp1B2E9_BelongsToL3() => Assert.Contains(Line(StructureNaming.Lines(Cas7D16C(), PanelPosition.Center), "L3").Pieces, p => p.Key == "1B2E9");
[Fact] public void Lines_Cas7D16C_Hp()      // HT1, HT2; HL1..HL18; HL1 at y 87, HL18 at y 14180
[Fact] public void Lines_Fb_AllBs()          // every FB line named "BS"; x=7803 bar → 2 lines (gap 1074 > 30) …
[Fact] public void Lines_FbSplitAtHp_OneLine() // pieces 906–9394 and 9400–10027 at x 8803 (gap 6) → 1 line
[Fact] public void Lines_Starboard_ReversesL() // HL1 at y 14180, L1 at y 13768; T order unchanged
[Fact] public void Lines_Port_SameAsCenter()
[Fact] public void Lines_DiagonalGoesToNearestAxis() // (0,0)-(1000,300) → Dir L
[Fact] public void Lines_ShortPieceMakesOwnLine() // extra HP 60–262 at y 57 → becomes HL1, old HL1 → HL2
[Fact] public void Resolve_UserNameOverrides() // stored {"<T7 piece>":"T6a"} → Name "T6a", IsUserName, AutoName "T7"
[Fact] public void Resolve_MostCommonName()   // 3 pieces: "A","A","B" → "A"
[Fact] public void Resolve_StaleKeysIgnored() // stored key not in any line → no change, no throw
[Fact] public void NamesJson_RoundTrip() / NamesJson_GarbageIsEmpty()
```

- [ ] **Step 2: Run** `dotnet test tests/MCG_3DPanel.Tests --filter StructureNaming` — Expected: build FAIL (types missing).
- [ ] **Step 3: Implement** `StructureNaming` and `NamesJson` (System.Text.Json, ordinal keys) per the Interfaces block.
- [ ] **Step 4: Run** `dotnet test tests/MCG_3DPanel.Tests` — Expected: all pass (140 old + new).
- [ ] **Step 5: Commit** `feat(naming): pure line grouping and auto names T/L/HT/HL/BS + names JSON`

### Task 2: Store + adapter + build hand-off

**Files:**
- Create: `Naming/NameStore.cs`, `Naming/NamingSource.cs`
- Modify: `Services/IBuild3DService.cs` (`BuildResult.ClassifiedParts`), `Commands/PanelCommands.cs` (`BuildOne` fills
  it; new `ClassifyOnly`), `Services/Corrections/CorrectionXRecord.cs` (`NamesKey = "MCG3D_NAMES"`),
  `UI/PaletteManager.cs` (`NameStore.ClearDatabase` on `DocumentToBeDestroyed`)

**Interfaces:**
- Consumes: Task 1 types; `CorrectionStore.PanelKey(ObjectId btr2D)`; `CorrectionXRecord.Read/Write`.
- Produces:
  - `BuildResult.ClassifiedParts : List<PanelPart>` (set from `cls.Parts` in `BuildOne`).
  - `sealed class NamingPanel { string PanelKey; string PanelName; Database Db; ObjectId SourceBtrId;
    PanelPosition Position; IReadOnlyList<NamingLine> Lines; Dictionary<string, ObjectId[]> Paths; Dictionary<string,
    Extents3d> Bounds; bool From3D; }` — `Paths`/`Bounds` keyed by piece Key.
  - `static NamingPanel NamingSource.FromBuild(BuildResult b)` — pieces from `ClassifiedParts`; paths = `[BlockRefId,
    SolidId]` of the solid whose `Part.SourceKey` matches, else none (row still shown, ⌖ disabled for that piece).
  - `static NamingPanel NamingSource.FromBlock(Transaction tr, ObjectId blockRef, PanelPosition pos, ClassificationResult cls)`
    — paths = `PanelPart.IdPath`.
  - Piece = `PanelPart` of type Web/GirderEnd → Web, HP → HP, FlatBar → FB; `Key = SourceKey`; endpoints from
    `ClassifyUtil.Endpoints(part)` transformed by `blockTransform.Inverse()`; `Thickness = FootprintThickness`.
  - `static List<NamingPanel> PanelCommands.ClassifyOnly(PanelPosition pos)` — picks 2D panels (same filter as Build),
    classifies with corrections, no 3D; failure per panel is reported, not thrown.
  - `NameStore`: `EnsureLoaded(tr, btr2D)`, `For(panelKey) : IReadOnlyDictionary<string,string>`,
    `SetLine(panelKey, NamingLine line, string name)` (null/blank → remove all its keys), `Save(tr, btr2D) : string`
    (error message or null; xref → message, like `CorrectionStore.Save`), `ClearDatabase(Database)`.

- [ ] **Step 1:** Implement the above; `dotnet build` — Expected: 0 errors.
- [ ] **Step 2:** `dotnet test tests/MCG_3DPanel.Tests` — Expected: all pass.
- [ ] **Step 3: Commit** `feat(naming): name store (XRecord MCG3D_NAMES), naming adapter, classify-only command`

### Task 3: Naming tab UI

**Files:**
- Create: `UI/PanelNamingView.xaml(.cs)`, `UI/NamingRow.cs`
- Modify: `UI/PaletteManager.cs` (tab index 2 "Naming"; feed `Built`, `Rebuilt`, document events),
  `docs/ux-checklist.md` (section G)

**Interfaces:**
- Consumes: Task 2 `NamingPanel`, `NamingSource`, `NameStore`, `PanelCommands.ClassifyOnly`; `BulkEdit.Plan`;
  `EntityHighlighter.ShowMany`; `StatusText.Set`.
- Produces: `PanelNamingView.Load(List<BuildResult>)`, `OnRebuilt(BuildResult old, BuildResult @new, string msg)`,
  `OnDocumentClosing(Database)`, `OnDocumentActivated(Database)`.
- Toolbar: Position combo (Center/Port/Starboard, default = Input tab's value at load) + **Select panel…** button.
- Grid columns: Panel, **Name** (CellTextBox; Enter/LostFocus commits, Esc restores), Kind, Dir, Pieces, Length (mm,
  0 decimals), Thk (distinct thicknesses "6/20"), ⌖. User names in blue (like corrected FB height).
- Commit: bulk via `BulkEdit.Plan` (rows of another drawing skipped + reported); `NameStore.SetLine` per target; one
  `NameStore.Save` per panel in a locked transaction; then `StructureNaming.Resolve` again and refresh rows. Status
  text e.g. "Renamed 3 lines to BS." / Save error shown as error.
- Context menu: Reset to auto (selection), Copy. Ctrl+C copies `Panel\tName\tKind\tDir\tPieces\tLength` with header.
- Selection highlight like Result (debounced, all selected pieces in one call; no zoom except ⌖).
- Changing Position re-numbers the loaded panels (no reclassify).

- [ ] **Step 1:** Implement; `dotnet build` — Expected: 0 errors; tests still pass.
- [ ] **Step 2:** Add checklist section G (G1 select 2D panel, G2 rename + Ctrl+S + reopen keeps name, G3 bulk rename
  "BS", G4 Reset to auto, G5 Starboard order, G6 panel from another drawing refused, G7 build → tab refreshed).
- [ ] **Step 3: Commit** `feat(naming): Naming tab — auto names, rename/bulk rename, reset, copy, highlight`

### Task 4: Skill + final

**Files:**
- Create: `.claude/skills/structure-naming/structure-naming_SKILL.md` (rules from the spec, the 7D-16C table, the
  store key, known limits: inserted lines like T6a, short pieces shift numbers).
- [ ] **Step 1:** Write the skill; commit `docs(naming): structure-naming skill`.
- [ ] **Step 2:** Final whole-branch review (fresh reviewer), fix Critical/Important with tests, report.
