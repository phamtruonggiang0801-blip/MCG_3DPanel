# Detail Drawing (Detail Plan / Section Detail / Detail View / Update) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Four commands, run in the 2D drawing, that cut detail plans, sections and detail views out of the panel's 3D solids and write them as Model Space blocks in the company's detail-sheet style.

**Architecture:** All geometry comes from sectioning the solids of `<panel>_3D.dwg`, which is read in the background. The region is clipped by intersecting the solids with a box (rectangle or circle prism). After that, AutoCAD's `Section.GenerateSectionGeometry` returns the cut faces and the visible background curves. The rules are pure C# in `Services/Detail/` and are unit-tested: numbering, AM_8 offsets, cut-line frame, symbol decisions, the XData record, 3D-file matching and staleness. AutoCAD-bound code lives in `Commands/Detail/`. Its writer follows the Plan View pattern: one transaction, XData on every tool entity, nothing left behind on Esc or error.

**Tech Stack:** C# / .NET 8, AutoCAD 2026 .NET API (Mechanical layers `Mechanical-AM_*`), WPF palette, xUnit (`tests/MCG_3DPanel.Tests`, net8.0-windows).

**Spec:** `docs/superpowers/specs/2026-11-16-detail-drawing-design.md` (read it first). Hand samples: `C:\Users\truonph\Desktop\MCG\AutoCAD\Drawing\Detail - CAS-*.dump.json`.

## Global Constraints

- Everything runs in the **2D drawing**. Never modify the `<panel>_Assy` block tree (its handles key the 3D corrections).
- Create **no** layers, dim styles or text styles. Missing layer ⇒ stop before drawing and list it (same as Plan View's `PlanViewWriter.Missing`). Missing linetype `amzigzag` ⇒ draw the break as Continuous and say so in the status line.
- Blocks the drawing lacks (`Continious symbol`) come from the drawing, then `CAS.dwt`, then the tool's own shape (`PlanViewBlockSource` + `SymbolBlockShapes`).
- **Esc / right-click at any step ⇒ nothing remains in the drawing.** An error at any step ⇒ nothing remains, and the full exception goes to the command line (the `PlanViewCommand.Run` pattern).
- Blocks are inserted at scale 1. A Detail View is inserted at the user's scale (default 1).
- Layers: cut outline `Mechanical-AM_0`; AM_8 marks `Mechanical-AM_8`; visible beyond the cut `Mechanical-AM_0`; deck-plate edge in plan/view `Mechanical-AM_7`; wire-guide path `Mechanical-AM_7` width 3; fitting blocks `Mechanical-AM_2`; break `Mechanical-AM_4` linetype `amzigzag`; section marks `Mechanical-AM_10` (text h 30); titles `Mechanical-AM_1` (h 50, `%%U` for DETAIL titles); continuous symbol + `(t)` text `Mechanical-AM_5`; `$` `Mechanical-AM_10` MText h 35.
- AM_8 count = `max(1, round(t/6))`, evenly dividing t.
- Names: `<panel>_DET.n`, `<panel>_DET.n_nA-nA`. Titles: `DETAIL n`, `nA-nA`, `nA-nA  90°ROTATED` (two spaces). Plan label: `DET.n`.
- XData RegApp `MCG3D_DETAIL` on every tool entity and block reference. The palette and status text are in English (existing UI language).
- Build only with `deploy.bat`. IDE background builds have corrupted `obj\Debug\UI\*.g.cs` before. Tests: `dotnet test tests/MCG_3DPanel.Tests`.
- Files under `Services/Detail/` must not reference `Autodesk.*` (they are linked into the test project).

**Deviation from spec, flagged to the engineer:** the stale-3D check compares a **fingerprint of the 2D `_Assy` block tree**, stored in `PanelMeta` at Build/Update, instead of a build time. Plan View writes into and saves the 2D drawing, so a time comparison would warn after every Plan View run. Plan View never touches `_Assy`, so the fingerprint only changes when the structure really changes.

## Review Focus

1. **3D file open in AutoCAD with unsaved edits** while the command runs in the 2D drawing. Expected: warn *Continue / Cancel*; Continue reads the open document's database, not the disk file. → Task 3 test `Stale_Unsaved3D_Warns` (pure decision) + checklist K3.
2. **Region with no solids inside** (rectangle drawn off the panel, or a cut line through empty space). Expected: Detail Plan / Section stops with "No structure inside the boundary. Nothing was drawn." On Update the block is left empty and reported. → Task 6 test `Frame_LineOutsideRegion_ReturnsNull` + Task 13 checklist K10.
3. **Boundary is not a closed rectangle / circle on AM_7** (open polyline, rotated rectangle, polyline with arcs, wrong layer). Expected: refuse with a sentence naming what is wrong; rotated rectangles are accepted (region stays rotated). → Task 5 test `Region_FromPolyline_*`.
4. **A cut line that is not horizontal/vertical, or a viewing-side click on the line itself.** Expected: snap to H/V; side click within 1 mm of the line ⇒ ask again. → Task 6 tests `Snap_*`, `Frame_SideOnLine_ReturnsNull`.
5. **Hand-made `DET.n` blocks already in the drawing** (old sheets). Expected: numbering continues after the highest number; Update ignores blocks without `MCG3D_DETAIL`. → Task 4 test `NextDetailNumber_CountsHandMade`.

---

## File Structure

| File | Responsibility |
|---|---|
| `Services/Detail/DetailNaming.cs` | DET numbers, section letters, block names, titles (pure) |
| `Services/Detail/SectionMarkRules.cs` | AM_8 offsets, plate-like test, min-area rectangle (pure) |
| `Services/Detail/DetailRegion.cs` | Rect/circle region: contains, clip segment, from polyline (pure) |
| `Services/Detail/SectionFrame.cs` | Snap H/V, viewing side, plane frame, clip to region (pure) |
| `Services/Detail/SectionSymbolRules.cs` | Continuous / `$` decisions + placement (pure) |
| `Services/Detail/DetailRecord.cs` | XData payload ⇄ JSON (pure) |
| `Services/Detail/Detail3DMatch.cs` | Pick the 3D file among candidates; staleness decision; fingerprint hash (pure) |
| `Services/Corrections/PanelMeta.cs` | + `SourceFingerprint` |
| `Commands/Detail/SourceFingerprintReader.cs` | Walk an `_Assy` tree → fingerprint records (AutoCAD) |
| `Commands/Detail/Detail3DSource.cs` | Locate/read the 3D file, solids in 2D-plan coordinates (AutoCAD) |
| `Commands/Detail/SectionEngine.cs` | Box clip + `GenerateSectionGeometry` → classified 2D curves (AutoCAD) |
| `Commands/Detail/DetailComposer.cs` | Curves → layered entities + AM_8 + breaks + symbols + 2D-only items (AutoCAD) |
| `Commands/Detail/DetailWriter.cs` | Block defs, inserts, XData, titles, labels, erase-own, jig (AutoCAD) |
| `Commands/Detail/DetailCommands.cs` | The 4 `CommandMethod`s + `Request(kind)` for the palette |
| `UI/PanelResultView.xaml(.cs)` | 4 menu items under Create Drawing ▾ |
| `tests/MCG_3DPanel.Tests/Detail*Tests.cs` | Unit tests |
| `docs/ux-checklist.md` | Section K (manual AutoCAD checks) |

---

### Task 1: Spike: sectioning works on solids from a background-read 3D file (throwaway)

This decides whether the engine design holds. The spike code is **not** kept.

**Files:**
- Create (throwaway, delete before Task 9): `Commands/Detail/DetailSpike.cs`

- [ ] **Step 1: Write command `MCG_DetailSpike`.** It prompts for a 3D file path and a rectangle (two points, in that file's WCS). Then it:
  - reads the file with `ReadDwgFile`;
  - clones every `Solid3d` of the `MCG3D_*` block as a **non-database-resident** clone transformed by the block reference;
  - intersects each clone with a box (rectangle × Z −5000…−1) using `BooleanOperation(BooleanOperationType.BoolIntersect, …)`;
  - builds a non-resident `Section` (horizontal plane Z = −1, `SectionType.Section3d` or `LiveSection`, whichever works) and calls `GenerateSectionGeometry` per clipped solid;
  - adds the returned entities to the current drawing's Model Space, offset by +20000 in X;
  - prints timing and the counts of intersection boundary / fill / background / foreground entities.
- [ ] **Step 2: Run in AutoCAD 2026** on `7D-17P_3D.dwg` (DET.1 region) and on `7D-17C_3D.dwg` with a 2000×2000 region. Record:
  - whether non-resident `Section` works, or it must be appended to a scratch `Database`;
  - entity types returned (Region / Polyline / Spline / Line);
  - whether the background has hidden lines removed;
  - the time taken.
- [ ] **Step 3: Write findings** into a `## Spike results (Task 1)` section at the end of this plan file. If non-resident fails, write the workaround used: a scratch `new Database(true, true)` holding the clones and the Section. Task 9 follows these findings.
- [ ] **Step 4: Delete `DetailSpike.cs`; commit only the plan update.**

```bash
git add docs/superpowers/plans/2026-11-16-detail-drawing.md
git commit -m "docs(detail): record section-engine spike results"
```

### Task 2: Source fingerprint in PanelMeta

**Files:**
- Modify: `Services/Corrections/PanelMeta.cs` (add property)
- Create: `Services/Detail/Detail3DMatch.cs` (fingerprint hash only in this task)
- Create: `Commands/Detail/SourceFingerprintReader.cs`
- Modify: `Commands/PanelCommands.cs` (Build ≈ line 615 and Rebuild ≈ line 770 set the fingerprint before `WritePanelMeta`)
- Modify: `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj` (add `<Compile Include="..\..\Services\Detail\*.cs" LinkBase="Detail" />`)
- Test: `tests/MCG_3DPanel.Tests/PanelMetaTests.cs`, `tests/MCG_3DPanel.Tests/Detail3DMatchTests.cs`

**Interfaces:**
- Produces: `PanelMeta.SourceFingerprint : string` (`""` for old files).
- Produces: `static string Detail3DMatch.Fingerprint(IEnumerable<string> records)`: SHA-256 hex of the records **sorted ordinally** and joined with `\n`.
- Produces: `static List<string> SourceFingerprintReader.Records(Transaction tr, ObjectId assyBtrId)`: one record per entity in the tree, recursing into nested block definitions once per definition: `"{blockName}|{type}|{layer}|{extents rounded to 0.01}"`. Handles are excluded (the 2D file and its 3D copy share handles anyway, but a correction-free re-save must not change it). Entities on `Mechanical-AM_9` are excluded because draft dimensions don't change the structure.

- [ ] **Step 1: Failing tests**
```csharp
[Fact] public void Fingerprint_OrderIndependent() => Assert.Equal(Detail3DMatch.Fingerprint(new[]{"a","b"}), Detail3DMatch.Fingerprint(new[]{"b","a"}));
[Fact] public void Fingerprint_ChangesWithContent() => Assert.NotEqual(Detail3DMatch.Fingerprint(new[]{"a"}), Detail3DMatch.Fingerprint(new[]{"a","b"}));
[Fact] public void PanelMeta_OldJsonWithoutFingerprint_ReadsEmpty() { /* existing valid JSON without the field */ Assert.Equal("", PanelMeta.FromJson(json).SourceFingerprint); }
[Fact] public void PanelMeta_Fingerprint_RoundTrips() { var m = Valid(); m.SourceFingerprint = "ab12"; Assert.Equal("ab12", PanelMeta.FromJson(m.ToJson()).SourceFingerprint); }
```
- [ ] **Step 2: Run, expect FAIL** (`dotnet test tests/MCG_3DPanel.Tests --filter "Fingerprint"`): compile error, property and type missing.
- [ ] **Step 3: Implement.**
  - Add the property and default `null` → `""` in `FromJson`, like `SourcePath`.
  - Add the hash.
  - Add the reader.
  - In Build and Rebuild, set `meta.SourceFingerprint` from the **source BTR in the 3D file** (`build.SourceBtrId`) before `WritePanelMeta`. Rebuild recomputes it from the same BTR, so the value is unchanged.
- [ ] **Step 4: Run tests, expect PASS.** Then run `deploy.bat` and expect `Build succeeded`.
- [ ] **Step 5: Commit** `feat(detail): store 2D source fingerprint in panel meta`.

### Task 3: Rules: 3D file choice and staleness

**Files:** `Services/Detail/Detail3DMatch.cs`, `tests/MCG_3DPanel.Tests/Detail3DMatchTests.cs`

**Interfaces (produces):**
```csharp
public sealed class Candidate3D { public string Path; public string SourcePath; public string SourceRefHandle; public string Fingerprint; public bool OpenUnsaved; }
public enum StaleState { Fresh, Unknown /*old file, no fingerprint*/, Changed2D, Unsaved3D }
static List<Candidate3D> Detail3DMatch.Matching(IEnumerable<Candidate3D> all, string drawingPath, string assyRefHandle);  // SourcePath equals drawingPath (OrdinalIgnoreCase, full path) AND SourceRefHandle equals (OrdinalIgnoreCase)
static StaleState Detail3DMatch.Stale(Candidate3D c, string current2DFingerprint);  // Unsaved3D wins; "" ⇒ Unknown; differ ⇒ Changed2D
static IEnumerable<string> Detail3DMatch.CandidateFileNames(string panel); // "<panel>_3D.dwg" then "<panel>_3D_2.dwg"… pattern used by Panel3DPath.NextFree — return the glob "<sanitized panel>_3D*.dwg"
```

- [ ] **Step 1: Failing tests**: `Matching_FiltersByPathAndHandle`, `Matching_PathCaseInsensitive`, `Stale_Unsaved3D_Warns` (OpenUnsaved=true ⇒ `Unsaved3D` even when fingerprints match), `Stale_EmptyFingerprint_Unknown`, `Stale_Differs_Changed2D`, `Stale_Same_Fresh`.
- [ ] **Step 2: Run, expect FAIL.**
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run, expect PASS.**
- [ ] **Step 5: Commit** `feat(detail): 3D file matching and staleness rules`.

### Task 4: Rules: naming and numbering

**Files:** `Services/Detail/DetailNaming.cs`, `tests/MCG_3DPanel.Tests/DetailNamingTests.cs`

**Interfaces (produces):**
```csharp
static int    DetailNaming.NextDetailNumber(string panel, IEnumerable<string> blockNames); // max n over "<panel>_DET.n", "<panel>_DETn_…", "NEW_<panel>_DET.n…" + 1; none ⇒ 1
static string DetailNaming.NextSectionLetter(IEnumerable<string> usedLetters);         // "A","B",…,"Z","AA"
static string DetailNaming.DetailBlock(string panel, int n)            => $"{panel}_DET.{n}";
static string DetailNaming.SectionBlock(string panel, int n, string L)  => $"{panel}_DET.{n}_{n}{L}-{n}{L}";
static string DetailNaming.SectionMark(int n, string L)                 => $"{n}{L}";
static string DetailNaming.SectionTitle(int n, string L, bool vertical) // "1A-1A" or "1A-1A  90°ROTATED"
static string DetailNaming.DetailTitle(int n)                           => $"%%UDETAIL {n}";
static string DetailNaming.PlanLabel(int n)                             => $"DET.{n}";
```

- [ ] **Step 1: Failing tests**, using names copied from the samples:
  - `NextDetailNumber_CountsHandMade`: `["7D-16C_DET.1","7D-16C_DET4_4A-4A","7D-16C_DET.5","NEW_7D-17C_DET.10_10A-10A"]` for panel `7D-16C` ⇒ 6.
  - `NextDetailNumber_OtherPanelIgnored`.
  - `NextSectionLetter_SkipsUsed` (`["A","B"]` ⇒ `"C"`).
  - `NextSectionLetter_AfterZ` ⇒ `"AA"`.
  - `SectionTitle_Vertical` ⇒ `"1D-1D  90°ROTATED"`.
- [ ] **Step 2–4:** Run (FAIL) → implement → run (PASS).
- [ ] **Step 5: Commit** `feat(detail): detail numbering and names`.

### Task 5: Rules: region and AM_8 geometry

**Files:** `Services/Detail/DetailRegion.cs`, `Services/Detail/SectionMarkRules.cs`, tests `DetailRegionTests.cs`, `SectionMarkRulesTests.cs`

**Interfaces (produces):**
```csharp
public readonly struct P2 { public readonly double X, Y; ... }
public sealed class DetailRegion {
  public bool IsCircle; public P2 Center; public double Radius;          // circle
  public P2[] Corners;                                                   // rectangle, 4 CCW corners (may be rotated)
  public static DetailRegion FromPolyline(IList<(double x,double y,double bulge)> v, bool closed, out string why); // null + why: "not closed", "not a rectangle", "has arcs"
  public static DetailRegion FromCircle(P2 c, double r);
  public bool Contains(P2 p);
  public P2[] Outline(int circleSegments = 72);                           // for box prisms / clipping
}
static double[] SectionMarkRules.Offsets(double t);          // distances from one face: t=6 ⇒ [3]; 10 ⇒ [3.333,6.667]; 20 ⇒ [5,10,15]
static bool     SectionMarkRules.IsPlateLike(double area, double width, double length); // area >= 0.95*width*length && length >= 2*width
static (P2 origin, P2 axis, double width, double length) SectionMarkRules.MinAreaRect(IList<P2> pts); // rotating calipers over convex hull; axis = unit vector along length
```

- [ ] **Step 1: Failing tests:**
  - `Offsets_6_10_12_15_20` (12 ⇒ 2 lines at 4/8; 15 ⇒ 3 at 3.75/7.5/11.25).
  - `IsPlateLike_Rectangle_True`.
  - `IsPlateLike_HpBulbSection_False` (area of HP120x6 ≈ 120×6 + 22×15 vs rect 120×22).
  - `MinAreaRect_RotatedRectangle` (a 10×300 rectangle rotated 30° ⇒ width 10, length 300).
  - `Region_FromPolyline_Rotated_Accepted`.
  - `Region_FromPolyline_Open_Refused`.
  - `Region_FromPolyline_Bulge_Refused`.
  - `Region_FromPolyline_Trapezoid_Refused`.
  - `Region_Circle_Contains`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(detail): region and AM_8 rules`.

### Task 6: Rules: cut line → section frame

**Files:** `Services/Detail/SectionFrame.cs`, `tests/MCG_3DPanel.Tests/SectionFrameTests.cs`

**Interfaces (produces):**
```csharp
static P2 SectionFrame.Snap(P2 start, P2 raw);   // keep the larger of |dx|,|dy|, zero the other
public sealed class SectionFrame {
  public P2 Start, End;          // cut line clipped to the region (plan coords)
  public P2 Right;               // unit, viewer's right = look × Z  (look=+Y ⇒ Right=+X)
  public P2 Look;                // unit, from the line toward the side click
  public double Length, Depth;   // Depth = distance from the line, along Look, to the far side of the region
  public bool Vertical;          // cut line vertical in plan ⇒ "90°ROTATED"
  public static SectionFrame From(P2 a, P2 b, P2 side, DetailRegion region); // null if side within 1 mm of the line, a==b, or the line misses the region
  public P2 ToSection(P2 planPt, double z);  // section 2D coords: x = along Right from the left end, y = z (deck plate on top)
}
```

- [ ] **Step 1: Failing tests:**
  - `Snap_MostlyHorizontal`.
  - `Frame_LookUp_RightIsPlusX`.
  - `Frame_LookDown_RightIsMinusX`.
  - `Frame_Vertical_Flag`.
  - `Frame_ClipsToRegion` (line longer than the rectangle ⇒ Length = rectangle width).
  - `Frame_DepthToFarEdge`.
  - `Frame_SideOnLine_ReturnsNull`.
  - `Frame_LineOutsideRegion_ReturnsNull`.
  - `ToSection_LeftEndIsZero`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(detail): section frame from cut line`.

### Task 7: Rules: Continuous symbol and `$`

**Files:** `Services/Detail/SectionSymbolRules.cs`, `tests/MCG_3DPanel.Tests/SectionSymbolRulesTests.cs`

**Interfaces (produces):**
```csharp
public sealed class CutMember   { public string Type; public double MinSigned, MaxSigned; /* solid extent along Look, relative to plane */ public double Thickness; public P2 CutCenter; public P2 CutAxis; /* section coords; axis along the plate's height */ }
public sealed class ParallelPlate { public string Type; public bool Visible; public P2 OutlineA, OutlineB; /* longest visible outline segment, section coords */ }
static bool   SectionSymbolRules.IsContinuous(CutMember m, double tol = 1.0); // Type != "HP" && MinSigned < -tol && MaxSigned > tol
static string SectionSymbolRules.ThicknessText(double t);                    // "(10.0)" — one decimal, invariant culture
static (P2 at, double rotation) SectionSymbolRules.ContinuousPlacement(CutMember m); // at = CutCenter; arrow perpendicular to the plate (rotation of CutAxis + 90°)
static P2?    SectionSymbolRules.WeldTracePlacement(ParallelPlate p, double gap = 15); // null when !Visible; else midpoint of A–B pushed `gap` to the right of A→B
```

- [ ] **Step 1: Failing tests:**
  - `Continuous_BothSides_True`.
  - `Continuous_EndsAtPlane_False` (MinSigned = −0.3).
  - `Continuous_Hp_False`.
  - `ThicknessText_Parenthesised` (`10` ⇒ `"(10.0)"`).
  - `WeldTrace_Hidden_Null`.
  - `WeldTrace_MidHeight` (vertical outline 0..−374 ⇒ y = −187).
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(detail): continuous symbol and weld-trace rules`.

### Task 8: Rules: XData record

**Files:** `Services/Detail/DetailRecord.cs`, `tests/MCG_3DPanel.Tests/DetailRecordTests.cs`

**Interfaces (produces):**
```csharp
public enum DetailKind { Plan, Section, View }
public sealed class DetailRecord {
  public DetailKind Kind; public string Panel = ""; public int Number; public string Letter = "";  // Letter only for Section
  public string AssyRefHandle = ""; public string BoundaryHandle = "";                              // AM_7 boundary on the plan (Plan/View)
  public List<double[]> Region = new();   /* rect corners or [cx,cy,r] */ public bool Circle;
  public double[] CutA, CutB, Side;       // Section only, plan coords
  public double Scale = 1; public string Title = "";
  public double BaseX, BaseY;             // plan coords of the block's base point
  public string ToJson(); public static DetailRecord FromJson(string s); // null on bad/empty JSON, never throws
}
```
In XData, the JSON is split into 255-character `ExtendedDataAsciiString` chunks after the RegApp name. Task 11 owns the split; this task only does JSON.

- [ ] **Step 1: Failing tests:** `RoundTrip_Section`, `RoundTrip_CircleView`, `FromJson_Garbage_Null`, `FromJson_Empty_Null`.
- [ ] **Step 2–4:** FAIL → implement → PASS.
- [ ] **Step 5: Commit** `feat(detail): detail XData record`.

### Task 9: Detail3DSource: find and read the 3D file

**Files:** `Commands/Detail/Detail3DSource.cs`

**Interfaces:**
- Consumes: `Detail3DMatch.*` (Task 3), `SourceFingerprintReader.Records` (Task 2), `PanelMeta`, `PanelCommands.FindOpen(path)`, `CorrectionXRecord.Read(tr, btrId, CorrectionXRecord.PanelKey)`.
- Produces:
```csharp
public sealed class Detail3DSource : IDisposable {
  public static Detail3DSource Open(Database db2D, string drawingPath, ObjectId assyRefId, Func<IList<string>, string> choose, out string why);
     // choose(paths) = UI to pick one when several match (null ⇒ cancelled). why ⇒ reason when null is returned.
  public StaleState Stale { get; }
  public string FilePath { get; }
  public Matrix3d PlanTo3D { get; }   // = srcRef3D.BlockTransform * assyRef2D.BlockTransform.Inverse()
  public double TopPlateThickness { get; }
  public IReadOnlyList<SolidItem> Solids { get; } // non-resident clones in 3D WCS (block-ref transform applied), Deleted rows skipped
  public void Dispose();               // disposes clones + side Database (not an open document's db)
}
public sealed class SolidItem { public Solid3d Solid; public string Type; public string Handle; }
```
- Rules:
  - Candidate files are `<panel>_3D*.dwg` in the 2D drawing's folder, where panel = `_Assy` name without `_Assy`, sanitized as in `Panel3DPath.Target`.
  - Each candidate's meta is read with a side `Database` (`ReadDwgFile`, `OpenForReadAndAllShare`), or from the open document's database when `PanelCommands.FindOpen` finds it (`OpenUnsaved = doc.Database` is modified: use `PanelCommands.IsSavedOnDisk(doc) == false`).
  - Then filter with `Detail3DMatch.Matching`. With one match, use it. With several, call `choose`. With none, `why = "No 3D file of <panel> next to this drawing. Build 3D first."`.
  - The current fingerprint is computed from the 2D drawing's `_Assy` BTR.

- [ ] **Step 1: Implement** (AutoCAD-bound, no unit test; the decisions inside are tested in Task 3).
- [ ] **Step 2: Temporary command `MCG_DetailProbe`** prints `FilePath`, `Stale`, solid count by type, `PlanTo3D`. Run it in AutoCAD on 7D-17P in four cases: 2D only; 3D open and clean; 3D open and dirty; 2D `_Assy` edited and saved. Expected states: Fresh, Fresh, Unsaved3D, Changed2D. Old 3D file without a fingerprint ⇒ Unknown.
- [ ] **Step 3: Remove the probe command; commit** `feat(detail): locate and read the panel 3D file`.

### Task 10: SectionEngine

**Files:** `Commands/Detail/SectionEngine.cs`

**Interfaces:**
- Consumes: `Detail3DSource.Solids`, `DetailRegion`, `SectionFrame`, spike findings (Task 1).
- Produces:
```csharp
public sealed class CutFace   { public string Type; public string Handle; public List<P2> Outline; /* closed, section-plane 2D coords */ public double MinSigned, MaxSigned; }
public sealed class EdgeCurve { public Curve Curve; /* 2D in section-plane coords, Z=0, not db-resident */ public bool OnBoundary; /* lies on the clip box side ⇒ break line */ public string Type; public string Handle; }
public sealed class SectionResult { public List<CutFace> Cut = new(); public List<EdgeCurve> Visible = new(); public List<ParallelPlate> Parallel = new(); /* Section only */ }
static SectionResult SectionEngine.Plan(Detail3DSource src, DetailRegion regionPlan);           // plane Z=-1, look -Z, box = region prism Z∈[-1, -(maxDepth)]; output coords = plan coords
static SectionResult SectionEngine.Section(Detail3DSource src, DetailRegion regionPlan, SectionFrame f); // box = region prism ∩ slab(Start..End, 0..Depth along Look), Z all; output = SectionFrame.ToSection coords
static List<P2>      SectionEngine.TopPlateOutline(Detail3DSource src, DetailRegion regionPlan); // TopPlate cut at Z=+t/2, clipped (for AM_7)
```
- Rules:
  - Only solids whose extents meet the box are processed.
  - The TopPlate is excluded from `Plan` (the Detail Plan shows structure; the plate edge comes from `TopPlateOutline`). It is included in `Section`.
  - `OnBoundary` = every point of the curve within 0.5 mm of a box side face that is **not** the section plane.
  - Region conversion: Spline ⇒ polyline approximation with chord error 0.1 mm.
  - Each `CutFace` gets the min/max signed extent of its **unclipped** solid along the look direction. This feeds `IsContinuous`.
- `Parallel` (feeds the `$` rule) lists every non-HP solid that the plane does **not** cut but that lies inside the box, and whose plan footprint is parallel to the cut line within 1°. The footprint is the solid's cut at Z = −(its height)/2 run through `MinAreaRect`. `Visible` = at least one non-boundary `EdgeCurve` of that solid. `OutlineA/B` = the end points of its longest such curve.
- `CutMember` (Task 7) is built in the composer from each `CutFace`:
  - Thickness = `MinAreaRect(Outline).width`;
  - CutCenter = rectangle centre;
  - CutAxis = rectangle `axis`.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: Temporary command `MCG_DetailEngineProbe`** that draws `Plan` and `Section` results raw (cut AM_0, visible AM_4, boundary red) at +20000 X for the DET.1 region of 7D-16P and cut line 1B-1B. Compare by eye with the hand `7D-16P_DET.1_1B-1B`. The tool must additionally show collars, scallops and snips. Expected: same members, same positions (±1 mm).
- [ ] **Step 3: Remove the probe; commit** `feat(detail): section engine on 3D solids`.

### Task 11: DetailComposer + DetailWriter

**Files:** `Commands/Detail/DetailComposer.cs`, `Commands/Detail/DetailWriter.cs`

**Interfaces:**
- Consumes: Tasks 4–8 and 10, `PlanViewBlockSource.Prepare/Apply/NotLive`, `SymbolBlockShapes` (add a `Continious symbol` shape: two opposite arrow leaders, as in the 7D-16P dump: leader (−59,0)→(12.5,0) and (41.5,0)→(−30.3,0), layer `Mechanical-AM_5`).
- Produces:
```csharp
static List<Entity> DetailComposer.PlanEntities(SectionResult r, List<P2> topPlate, IEnumerable<Entity> twoDItems, P2 basePt); // block-local coords (plan − base)
static List<Entity> DetailComposer.SectionEntities(SectionResult r, SectionFrame f, out P2 basePt);                          // incl. symbols
static List<Entity> DetailComposer.TwoDItems(Transaction tr, BlockReference assyRef, DetailRegion region);                // plan coords
static List<Entity> DetailComposer.SectionMarks(SectionFrame f, int n, string letter, P2 basePt);                          // DET-block-local
static List<string> DetailWriter.Missing(Database db);        // layers listed in Global Constraints that the drawing lacks
static ObjectId     DetailWriter.DefineBlock(Transaction tr, Database db, string name, IEnumerable<Entity> ents, DetailRecord rec); // replaces contents if the name exists AND carries MCG3D_DETAIL; a hand-made block with that name ⇒ name gets suffix "_MCG"
static bool         DetailWriter.Place(Editor ed, Transaction tr, ObjectId btrId, double scale, string prompt, out BlockReference br); // EntityJig, false on Esc
static void         DetailWriter.AddTitle(Transaction tr, BlockReference br, string text, double below = 150);   // AM_1 h50, centred under the insert's extents
static void         DetailWriter.Tag(Entity e, DetailRecord rec);   // XData MCG3D_DETAIL, JSON chunked by 255
static DetailRecord DetailWriter.Read(Entity e);                    // null if absent/bad
```
- Composer rules:
  - Cut faces: outline polyline AM_0.
  - If `IsPlateLike`: AM_8 lines along `MinAreaRect.axis` at `Offsets(width)`, clipped to the outline.
  - Visible edges: `OnBoundary` ⇒ polyline AM_4 with linetype `amzigzag` (Continuous plus a note when the linetype is missing). Otherwise AM_0.
  - Deduplicate collinear overlapping segments with tolerance 0.05 mm.
- 2D items rules:
  - Walk `_Assy` children except `<panel>_Top Plate`, `<panel>_Structure`, `<panel>_Lashing Hole`. Descend into any block named `<panel>_*`.
  - Other block references are leaves, kept whole if their insertion point is inside the region. Layer: `AM_0` when they come from `<panel>_Hole cut out`, `AM_2` otherwise.
  - Curves from containers: clip to the region. `Mechanical-AM_9` is skipped. Polylines from `<panel>_Wire guide support` ⇒ AM_7 with constant width 3.
- Section marks:
  - Reproduce the arrow of the sample block `A$C1A6307D4` (dump 7D-16P). Read its definition from the dump and draw it as entities on AM_10.
  - One at each end of the cut line, pointing along `Look`, and MText letter `nA` h 30 beside each.
- Transaction rule: callers open **one** transaction for compose + define + place + title + label. `Place` returning false ⇒ the caller aborts the transaction (nothing remains).

- [ ] **Step 1: Implement composer + writer.**
- [ ] **Step 2: Unit-test `SymbolBlockShapes` addition**: add `Has_ContinuousSymbol` to `SymbolBlockShapesTests.cs` and expect PASS.
- [ ] **Step 3: `deploy.bat`, expect Build succeeded. Commit** `feat(detail): compose and write detail blocks`.

### Task 12: Commands: Detail Plan and Detail View

**Files:** `Commands/Detail/DetailCommands.cs`

**Interfaces:**
- Consumes: Tasks 9–11.
- Produces: `[CommandMethod("MCG_DetailPlan")]`, `[CommandMethod("MCG_DetailView")]`, `static void DetailCommands.Request(string command)` (palette → `SendStringToExecute`), `public static event Action<string,bool> Finished` (same as `PlanViewCommand.Finished`).

**Flow (Detail Plan):**
1. Pre-selection or prompt: "Select the AM_7 rectangle of the detail:". It must be on `Mechanical-AM_7` and parse with `DetailRegion.FromPolyline`; otherwise re-prompt with the reason.
2. The `_Assy` reference whose extents contain the region centre. None ⇒ "The rectangle is not on a panel."
3. `Detail3DSource.Open`. Stale ≠ Fresh/Unknown ⇒ Continue/Cancel dialog (`MessageBox` YesNo, text names the reason).
4. `n = NextDetailNumber`.
5. Run `SectionEngine.Plan`. No cut faces and no visible curves ⇒ stop "No structure inside the boundary. Nothing was drawn."
6. One transaction: define `<panel>_DET.n`; `Place` ("Insertion point of DETAIL n:"); title `DETAIL n` under it; label `DET.n` (AM_1, h 150) next to the boundary's top-right corner; tag the boundary with the record (BoundaryHandle = its handle); commit.
7. Status: "DETAIL n created from <3D file>. <notes>".

**Detail View differences:**
- It accepts a circle too (`DetailRegion.FromCircle`).
- It prompts `Scale <1>:` (a positive real number) and `Name <DETAIL n>:`.
- The insert scale is the typed value. The title is the name exactly as typed, with `%%U` prefixed.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: `deploy.bat`, then run in AutoCAD on `Plan - CAS-0062239.dwg` (7D-16P):** draw the DET.1 rectangle, run `MCG_DetailPlan`, place. Expected:
  - the block shows webs with AM_8;
  - HP bulbs are on AM_0;
  - collars and scallops are present;
  - fittings are on AM_2;
  - zigzag lines appear where the frame cuts members.
  - Esc during placement ⇒ `UNDO`-free check: no new block definition (`-PURGE` lists nothing new) and no label.
- [ ] **Step 3: Commit** `feat(detail): Detail Plan and Detail View commands`.

### Task 13: Command: Section Detail

**Files:** `Commands/Detail/DetailCommands.cs`

**Flow:**
1. Select a block reference whose `DetailWriter.Read` gives `Kind == Plan`. A Detail View ⇒ "Sections are only made on Detail Plans." Other blocks ⇒ re-prompt.
2. `GetPoint` start, `GetPoint` end (rubber band from start, `Snap` applied), `GetPoint` viewing side. Points are converted WCS → DET-block-local → plan coords (+Base). Each prompt cancels on Esc.
3. `SectionFrame.From` null ⇒ say why and re-ask the side or the end point.
4. Letter = `NextSectionLetter` over the letters in existing section records of DET n.
5. Run `SectionEngine.Section`. Empty ⇒ stop "The cut line crosses no structure. Nothing was drawn."
6. One transaction:
   - add the marks to the DET block definition (tag them);
   - define `<panel>_DET.n_nL-nL`;
   - `Place`;
   - title `nL-nL` or `nL-nL  90°ROTATED` under it;
   - commit.

- [ ] **Step 1: Implement.**
- [ ] **Step 2: AutoCAD check on the Task 12 DET.1:** cut where the hand 1B-1B is. Expected:
  - cut deck plate with 1 AM_8 line;
  - 10 mm web with 2 lines;
  - HP true section;
  - visible bracket edge on AM_0;
  - `(10.0)` continuous symbol on the continuing web;
  - `$` beside the x = 887 end line.
- [ ] **Step 3: Commit** `feat(detail): Section Detail command`.

### Task 14: Update Details

**Files:** `Commands/Detail/DetailCommands.cs`

**Flow:**
1. Collect every `MCG3D_DETAIL`-tagged block reference in Model Space, grouped by panel.
2. Run `Detail3DSource.Open` once per panel, with the stale prompt once per panel.
3. Confirm: "Update N details? Changes you made inside these blocks will be replaced; everything outside them is kept." Continue/Cancel.
4. One transaction for all:
   - **Plan/View:** region from the boundary handle if it is alive, otherwise from the record. Note "boundary of DETAIL n was erased — used the stored region". Regenerate the block contents (`DefineBlock` replaces them) and re-add this DET's section marks from the section records. Move the `DET.n` label to the boundary's current corner.
   - **Section:** recompute the frame with the stored cut line and the current DET region. Frame null or engine empty ⇒ leave the block empty, report "nL-nL: the cut line no longer crosses structure".
   - Compare the old and new entity sets (count + rounded extents) to list the changed details.
5. Status: "Updated N details (changed: DETAIL 1, 1A-1A…). Check dimensions on changed details."

- [ ] **Step 1: Implement.**
- [ ] **Step 2: AutoCAD check:**
  - stretch the DET.1 rectangle, then Update ⇒ DET.1 covers the new area, its insert has not moved, the user's dimension placed outside is unchanged;
  - erase the boundary ⇒ Update uses the stored region and says so.
- [ ] **Step 3: Commit** `feat(detail): Update Details`.

### Task 15: Palette menu, checklist, final build

**Files:** `UI/PanelResultView.xaml`, `UI/PanelResultView.xaml.cs`, `docs/ux-checklist.md`

- [ ] **Step 1: Add 4 `MenuItem`s** under `MnuPlanView`, after a `Separator`:

  | MenuItem | Header | Tooltip |
  |---|---|---|
  | `MnuDetailPlan` | `Detail Plan` | "Cut a 1:10 detail from an AM_7 rectangle on the Plan View" |
  | `MnuSectionDetail` | `Section Detail` | "Cut a section through a Detail Plan" |
  | `MnuDetailView` | `Detail View` | "Cut a detail at any scale from an AM_7 circle or rectangle" |
  | `MnuUpdateDetails` | `Update Details` | "Redraw every detail from the current 3D file" |

  - All four have `ShowOnDisabled=True`.
  - In `BtnCreateDrawing_Click`, they are enabled when the active drawing is **not** a 3D file (`!Panel3DFile.HoldsSourceReference(db)`) and has at least one `*_Assy` reference with a matching 3D candidate file present. Use a cheap check: a file named `<panel>_3D*.dwg` exists in the drawing's folder; full matching happens in the command.
  - Disabled tooltip: "Open the 2D drawing of a panel that has a 3D file".
  - `Update Details` additionally requires a tagged block.
  - Clicks call `DetailCommands.Request("MCG_DetailPlan ")`, and so on.
  - Status shows `DetailCommands.Finished` like `PlanViewCommand.Finished`.
- [ ] **Step 2: Add `## K. Create Drawing › Details (2026-11-16)`** to `docs/ux-checklist.md`. Write K1–K12 as check lines, one per Review Focus item and per command flow, plus:
  - Esc at each prompt leaves nothing;
  - the 3D file is open and dirty;
  - the boundary is erased before Update;
  - a hand-made `DET.n` block is present;
  - a missing `amzigzag` linetype is reported;
  - K10: a region with no structure.
- [ ] **Step 3: `deploy.bat` and `dotnet test tests/MCG_3DPanel.Tests`.** Expected: Build succeeded; all tests pass.
- [ ] **Step 4: Commit** `feat(detail): Create Drawing menu items for details`.
