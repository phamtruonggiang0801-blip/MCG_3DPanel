# Create Drawing › Plan View Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** From the active 3D panel file, a **Create Drawing ▾ › Plan View** command writes names, symbols,
dimensions, typical sections X-X / Y-Y, the lashing-hole plan and the fixed sheet items into the 2D drawing the panel
was built from. It redoes them on every run.

**Architecture:**
- A pure C# layer (`Services/PlanView/`, no `Autodesk.*`, linked into the xunit tests) turns a `PlanViewInput`
  snapshot into a `PlanViewModel`: a flat list of texts, dimensions, block inserts and section geometry, in 2D-block
  coordinates and in sheet coordinates.
- An AutoCAD layer (`Commands/PlanView*.cs`):
  - builds the snapshot from the 3D file (session build, Naming, corrections, AM_9 drafts of the 2D block copy);
  - finds and opens the 2D drawing;
  - checks resources, removes its previous output, detects hand-drawn annotation;
  - writes the model with an XData mark.

**Tech Stack:** C# / .NET 8, AutoCAD 2026 Mechanical .NET API, WPF palette, xunit (`tests/MCG_3DPanel.Tests`).

**Spec:** `docs/superpowers/specs/2026-11-10-create-drawing-plan-view-design.md`

## Global Constraints

- Never open for write, modify or erase any entity inside the `<panel>_Assy` block tree in the 2D drawing
  (handles key the 3D file's corrections).
- Never create layers, dim styles, text styles or symbol block definitions. A missing one stops the run with a list.
  The only definitions created are the tool's own section blocks, `<panel>_X-X_MCG` and `<panel>_Y-Y_MCG`.
- Layers used, verbatim:
  - `Mechanical-AM_0`: symbols, frame items;
  - `Mechanical-AM_1`: girder names, view titles;
  - `Mechanical-AM_4`: lashing dimensions;
  - `Mechanical-AM_5`: structure dimensions;
  - `Mechanical-AM_6`: stiffener names, B/S/GE/GB/OB text, member list, tolerance note;
  - `0`: section geometry inside the section blocks.
- Dim styles: `AM_ISO$0` (linear) and `AM_ISO$3` (diameter). Every dimension sets the overrides
  `DIMSCALE = sheet scale` and `DIMTMOVE = 1`.
- Text style `STANDARD`. Paper heights:
  - girder names and view titles 5 mm;
  - stiffener names and symbols 3 mm;
  - member list 3.5 mm.

  Model height = paper height × sheet scale (1:50 gives 250 / 150 / 175).
- Symbol blocks used, verbatim: `CS_horl_up_right`, `thk_sym`, `M.D1`, `M.D2`, `Rec. for tolerance`,
  `Measurements_Tolerances vFuture`, plus the profile blocks named by size (`HP120x6`, `BS 80x6`, …).
  Symbols are inserted at scale = sheet scale / 50.
- Overall Top Plate dimension text verbatim: `<>{}{\H0.71x;\C3;%%p4}{{}}TOP PLATE`. Lashing diameter text:
  `<>-LASHING HOLES`.
- View titles verbatim: `PLAN VIEW\P(ALL FITTINGS SHOWN ARE FOR REFERENCE LINE)`, `LASHING HOLES PLAN`.
- Every entity the tool writes carries XData app `MCG3D_PLANVIEW` with one string value = the panel's
  `SourceBtrHandle`.
- The 2D drawing is never saved by the tool. User-facing text is English.
- AM_9 drafts are copied as they are. A panel with none gets only the overall Top Plate dimensions and the lashing
  diameter.
- After switching documents, never use the previous document's `Editor` (it crashed AutoCAD before).
- Build only with `deploy.bat` (fixed BuildStamp; see the csproj comment).

## Review Focus

1. **Several `A1` frames in one drawing** (7D-16P has 4, 7D-17C has 10): the sheet is the frame whose extents contain
   the `<panel>_Assy` insertion point; none ⇒ stop with "No sheet frame (A1) around {panel}." Test: Task 3.
2. **The `_Assy` reference was moved or rotated in the 2D drawing after Build:** everything placed in block
   coordinates follows the reference's current transform. Test: Task 3.
3. **An HP/BS size without a profile block in the drawing** (e.g. `HP140x7`): the run stops before writing and lists
   the missing block names; nothing is drawn. Test: Task 7.
4. **Running twice, including after Keep both:** the second run leaves exactly one set of tool output. Hand-drawn
   items kept by Keep both are not detected again as "new". Test: Task 9.
5. **The 2D file is read-only, locked by another user, or open with unsaved changes:**
   - read-only or locked ⇒ stop with "{file} is read-only or in use.";
   - unsaved changes ⇒ continue in the open document (spec, my call).

   Test: Task 10 checklist row.

---

### Task 1: Placement study → `PlanViewLayout` constants

The spec leaves placement to the builder. This task measures it from the six reference dumps. The values feed
Tasks 4–8, and **the engineer reviews the rule table before Task 4 starts**.

**Files:**
- Create: `tools/DumpAnalyzer/an.cs` (copy of the session analyzer; `dotnet run an.cs -- <dump.json>...`)
- Create: `tools/DumpAnalyzer/placement.cs`
- Create: `docs/superpowers/specs/2026-11-10-plan-view-placement.md`
- Create: `Services/PlanView/PlanViewLayout.cs`

**Interfaces:**
- Produces: `public static class PlanViewLayout`, with every distance in **paper mm** (model = paper × scale):
  - `double LabelOffsetGirder`, `double LabelOffsetStiffener`: from the member centerline to the text insertion point,
    and which end/side;
  - `double ChainFirstOffset`, `double ChainSpacing`: dimension line distance from the Top Plate edge, and between
    chains;
  - `double SectionGapX`, `double SectionGapY`: plan extents to the section block;
  - `double LashingPlanGap`: plan to the lashing-plan inserts;
  - `double MdOffsetX`, `double MdOffsetY`: lashing plan to `M.D1`; `double MdSpacing`: `M.D1` to `M.D2`;
  - `double TitleOffset`: view extents to the view title;
  - `double MemberListX`, `double MemberListY`: relative to the frame origin;
  - `double ToleranceNoteX = 32.031`, `double ToleranceNoteY = 109.714` (= `(1601.55, 5485.71) / 50`).

Source: 7D-15P, 7D-16P, 7D-17P, 7D-16C, 7D-15C at 1:50, and 7D-17C at 1:75 as a check.

- [ ] **Step 1: Measure.** For each rule, `placement.cs` prints one value per drawing (in paper mm) and the spread:
  - label position relative to its member;
  - dimension-line offsets per side;
  - section and lashing-plan gaps;
  - title offsets;
  - `M.D1/M.D2`, member list and tolerance-note positions.

  Run: `dotnet run tools/DumpAnalyzer/placement.cs -- "<the six dump paths>"`
  Expected: one table per rule.
- [ ] **Step 2: Write the rule table** in `2026-11-10-plan-view-placement.md`: one row per constant with the value
  chosen (median), the spread, and each drawing that disagrees by more than 2 mm paper.
- [ ] **Step 3: Write `PlanViewLayout.cs`** with those values (pure C#, header `// ⚠️ File THUẦN C#`), and link it in
  `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj`.
- [ ] **Step 4: Engineer review.** Show the table; wait for approval or corrections before Task 4.
- [ ] **Step 5: Commit** `docs: placement rules for Plan View`.

### Task 2: Store the 2D drawing path in the 3D file

**Files:**
- Modify: `Services/Corrections/PanelMeta.cs` (add property)
- Modify: `Commands/PanelCommands.cs:786-815` (`WritePanelMeta`)
- Test: `tests/MCG_3DPanel.Tests/PanelMetaTests.cs`

**Interfaces:**
- Produces: `PanelMeta.SourcePath` (`string`, default `""`). Optional: `FromJson` keeps older JSON valid.

`WritePanelMeta` needs the path of the saved 2D drawing used by Build/Update. It is the `path` argument already passed
to `Panel3DFile.Create(file, ref2D, path, carry)`. Thread it through `BuildResult` as `public string SourcePath { get;
set; }` (in `Services/IBuild3DService.cs`), set where Build/Update create the file.

- [ ] **Step 1: Failing tests.**
  - `FromJson_OldJsonWithoutSourcePath_IsEmpty`: JSON without the key ⇒ `SourcePath == ""`.
  - `ToJson_RoundTrips_SourcePath`: the value `C:\a\7D-17P.dwg` survives a round trip.
- [ ] **Step 2: Run** `dotnet test tests/MCG_3DPanel.Tests --filter PanelMetaTests` → FAIL (no property).
- [ ] **Step 3: Implement** the property, the `?? ""` normalisation in `FromJson`, `BuildResult.SourcePath`, and the
  write in `WritePanelMeta`.
- [ ] **Step 4: Run** the same command → PASS. Run `deploy.bat` → `Build succeeded.`
- [ ] **Step 5: Commit** `feat(panel): store the 2D drawing path in the 3D file`.

### Task 3: Plan View model and sheet geometry

**Files:**
- Create: `Services/PlanView/PlanViewModel.cs`
- Create: `Services/PlanView/SheetGeometry.cs`
- Test: `tests/MCG_3DPanel.Tests/PlanViewSheetTests.cs`
- Modify: the tests csproj (link both files)

**Interfaces:**
- Produces (in `PlanViewModel.cs`, namespace `MCG_3DPanel.Services.PlanView`, all coordinates as `double X, Y`):
  - `enum PvSpace { Block, Sheet }`: `Block` = 2D panel-block coordinates (the writer applies the `_Assy` transform),
    `Sheet` = Model Space.
  - `sealed class PvText { PvSpace Space; double X, Y; string Text; string Layer; double PaperHeight; double Rotation;
    bool IsMText; string Category; }`. `Category` is one of `"Name"`, `"Symbol"`, `"Title"`, `"MemberList"`.
  - `sealed class PvDim { PvSpace Space; string Kind /* "Rotated" | "Diametric" */; double X1, Y1, X2, Y2, LineX,
    LineY, Rotation; string Layer; string DimStyle; string TextOverride /* null = "<>" */; }`
  - `sealed class PvInsert { PvSpace Space; string Block; double X, Y, Rotation, Scale; string Layer; }`.
    `Scale` is the factor before sheet scaling; the writer multiplies by scale/50 for symbols. `Rec. for tolerance`,
    `A1`-family blocks use `Scale` as-is.
  - `sealed class PvSection { string BlockName; double X, Y, Rotation; List<PvRect> Plates; List<PvInsert> Profiles;
    List<PvInsert> ThkSymbols; List<PvDim> Dims; }` and `sealed class PvRect { double X0, Y0, X1, Y1; }`.
  - `sealed class PlanViewModel { List<PvText> Texts; List<PvDim> Dims; List<PvInsert> Inserts; List<PvSection>
    Sections; List<string> RequiredBlocks; }`. `RequiredBlocks` is distinct, filled by each rule.
- Produces (`SheetGeometry.cs`):
  - `static int ScaleFromFrame(double frameScaleX)` returns `(int)Math.Round(Math.Abs(frameScaleX))`.
  - `static int? PickFrame(IReadOnlyList<(double MinX, double MinY, double MaxX, double MaxY, double Scale)> frames,
    double assyX, double assyY)` returns the index of the frame containing the point; `null` if none. Several ⇒ the
    smallest.
  - `static (double X, double Y) ToSheet(double bx, double by, double[] blockTransform2D)`, with the 2D affine
    `[a, b, c, d, tx, ty]` taken from the `_Assy` reference.
  - `static double ModelHeight(double paperMm, int scale) => paperMm * scale`.

- [ ] **Step 1: Failing tests.**
  - `ScaleFromFrame_50_and_75`: inputs 50 and −75 ⇒ 50 and 75.
  - `PickFrame_SecondOfFour_7D16P`: four frames at x offsets 0 / 42050 / 84100 / 126150 (each 42050 × 29700); Assy
    at (8071.26, 9832.41) ⇒ index 0. A point in the third ⇒ 2. Outside all ⇒ null.
  - `ToSheet_FollowsMovedAndRotatedReference`: transform rotation 90°, offset (1000, 2000); block point (10, 0) ⇒
    (1000, 2010).
  - `ModelHeight_5mm_at_75_is_375`.
- [ ] **Step 2: Run** `dotnet test tests/MCG_3DPanel.Tests --filter PlanViewSheetTests` → FAIL.
- [ ] **Step 3: Implement** both files.
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(planview): model types and sheet geometry`.

### Task 4: Snapshot input and the dimension rule (AM_9 drafts → sheet)

**Files:**
- Create: `Services/PlanView/PlanViewInput.cs`
- Create: `Services/PlanView/DraftDimRules.cs`
- Test: `tests/MCG_3DPanel.Tests/DraftDimRulesTests.cs`

**Interfaces:**
- Produces (`PlanViewInput.cs`), all coordinates in 2D-block space:
  - `sealed class PvMember { string Name; string Kind /* "Web" | "HP" | "FB" | "Flange" */; string Dir /* "T" | "L"
    */; double X0, Y0, X1, Y1; double Thickness, Height; string Spec; string ProfileBlock /* "HP120x6" | "BS 80x6" |
    null */; bool Deleted; }`. One per live naming piece; merged lines share one `Name`.
  - `sealed class PvDraft { string Source /* "LashingHole" | "Structure" */; double X1, Y1, X2, Y2, Rotation; }`
  - `sealed class PvMark { string Text; double X, Y; }`. Used for B, S, GE/GB, OB.
  - `sealed class PvLashing { double Diameter; double X, Y; }`. One hole, for the diameter dimension.
  - `sealed class PlanViewInput { string Panel; int Position /* PanelPosition as int */; double PlateThickness;
    (double MinX, double MinY, double MaxX, double MaxY) TopPlate; List<PvMember> Members; List<PvDraft> Drafts;
    List<PvMark> Marks; PvLashing Lashing; }`
- Produces (`DraftDimRules.cs`): `static List<PvDim> FromDrafts(PlanViewInput input)` plus
  `static List<PvDim> Overall(PlanViewInput input)`.

**Rules (spec + Task 1):**
- Layer: `LashingHole` ⇒ `Mechanical-AM_4`, `Structure` ⇒ `Mechanical-AM_5`. Style `AM_ISO$0`, `Space = Block`.
- Same axis (`Rotation`) and the same interval projected on it as the draft.
- Definition points are moved onto the nearest Top Plate edge perpendicular to the axis.
- The dimension line goes outside that edge at `ChainFirstOffset` (+ `ChainSpacing` per further chain). Drafts on the
  same axis and side whose intervals touch end-to-end form one chain.
- `Overall`: two dims (length along X, width along Y) on `Mechanical-AM_5` with
  `TextOverride = "<>{}{\H0.71x;\C3;%%p4}{{}}TOP PLATE"`, one chain further out than the outermost chain on that side.
  Plus one `Diametric` dim on `Mechanical-AM_5`, style `AM_ISO$3`, `TextOverride = "<>-LASHING HOLES"`, when `Lashing`
  is set.

- [ ] **Step 1: Failing tests** (drafts taken from 7D-17P `CAS-0062242`, Lashing Hole block, Top Plate 16298.8 ×
  13356.2):
  - `LashingDraft_KeepsInterval_MovesToEdge_AM4`: draft (2629.891, −6974.488)–(3269.891, −6974.488), rot 0 ⇒ one dim
    on AM_4, X interval [2629.891, 3269.891], both Y on the nearest plate edge, line Y beyond that edge by
    `ChainFirstOffset × 50`.
  - `StructureDraft_GoesToAM5`.
  - `TouchingDrafts_FormOneChain_SameLineY`: three 640 drafts end to end ⇒ three dims, one line Y.
  - `Overall_TwoDims_WithToleranceText`.
  - `NoDrafts_GivesOnlyOverallAndDiameter` (spec decision 4).
- [ ] **Step 2: Run** `--filter DraftDimRulesTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(planview): sheet dimensions from AM_9 drafts`.

### Task 5: Name labels and member list

**Files:**
- Create: `Services/PlanView/LabelRules.cs`
- Test: `tests/MCG_3DPanel.Tests/LabelRulesTests.cs`

**Interfaces:**
- Consumes: `PlanViewInput`, `PvMember`, `PlanViewLayout`.
- Produces:
  - `static List<PvText> Names(PlanViewInput input)`: one per distinct non-empty `Name` among non-deleted members.
    - Web/Flange names (T#, L#) go on `Mechanical-AM_1` at 5 mm.
    - HP/FB names (HT#, HL#, BS) go on `Mechanical-AM_6` at 3 mm.
    - Placed at the longest piece by the Task 1 rule; `Category = "Name"`.
  - `static PvText MemberList(PlanViewInput input)`: an MText on `Mechanical-AM_6`, 3.5 mm, `Space = Sheet`, at
    (`MemberListX`, `MemberListY`) × scale from the frame origin. Lines, in this order, with the exact wording of the
    reference sheet:

    ```
    TOP PLATE THICKNESS: {t:0.0}
    STIFFENERS (HL & HT): {HP spec}
    TRANSVERSAL GIRDERS {names} : {spec}
    LONGITUDINAL GIRDERS {names} : {spec}
    ```

    One girder line per distinct `Spec`. Names are comma-joined in name order (`L2,L3,L5,L6`).

- [ ] **Step 1: Failing tests.**
  - `Names_GirderOnAM1_StiffenerOnAM6`.
  - `Names_MergedLinesShareOneLabel`.
  - `Names_DeletedMemberHasNoLabel`.
  - `MemberList_7D17P`: members as in the 7D-17P reference ⇒ the text equals the reference member-list lines
    (`TRANSVERSAL GIRDERS T1,T2 : WEB 374x6.0/FLANGE 380x20.0`, `LONGITUDINAL GIRDERS L1,L7 : WEB 374x6.0/FLANGE
    250x20.0`, `L2,L3,L5,L6 : … 170x20.0`, `L4 : … 150x20.0`).
- [ ] **Step 2: Run** `--filter LabelRulesTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(planview): name labels and member list`.

### Task 6: Symbols (B, S, GE/GB, OB, continuous)

**Files:**
- Create: `Services/PlanView/SymbolRules.cs`
- Test: `tests/MCG_3DPanel.Tests/SymbolRulesTests.cs`

**Interfaces:**
- Consumes: `PlanViewInput.Marks`, `PlanViewInput.Members`, `PlanViewInput.Position`.
- Produces:
  - `static List<PvText> Marks(PlanViewInput input)`: each `PvMark` becomes text on `Mechanical-AM_6`, 3 mm,
    `Category = "Symbol"`, offset by the Task 1 rule.
  - `static List<PvInsert> Continuous(PlanViewInput input)`: one `CS_horl_up_right` on `Mechanical-AM_0` at every
    crossing where member A (HP, FB or Web) crosses the centerline of Web B, and A's centerline extends more than
    `B.Thickness` past B on both sides (A is continuous).
    - Insert at the crossing point, rotated to B's axis, flipped to the build-direction side.
    - Build-direction side follows the `PanelPosition` axis rules in `Models/PanelPosition.cs`:
      - T webs: side toward +X;
      - L webs: Center ⇒ toward the panel centre Y, Port ⇒ −Y, Starboard ⇒ +Y.
    - Adds `CS_horl_up_right` to `RequiredBlocks`.

`PlanViewInput.Marks` is filled by Task 10 (`PlanViewSnapshot`) from the 3D results:
- `B` at each TrippingBracket solid's bounds centre;
- `S` at each sniped end (`SnipLo`/`SnipHi`, end point of the piece);
- the Result-tab Kind (`ResultCatalog.ShortKind` of the row Kind) at each GE/GB row's part centre;
- the bracket code (`OB`, `OB1`, …) at each Bracket part.

- [ ] **Step 1: Failing tests.**
  - `Continuous_7D17P_Gives95`: 19 HT spanning all 5 inner L webs ⇒ 95 inserts. The outer webs L1/L7, where the HT
    ends, give none.
  - `Continuous_GirderThroughGirder`: a T web crossing an L web that is split at it ⇒ 1.
  - `Continuous_EndingMember_None`.
  - `Side_Port_vs_Starboard_Flips`.
  - `Marks_TextLayerHeight`.
- [ ] **Step 2: Run** `--filter SymbolRulesTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(planview): symbols B, S, GE/GB, OB and continuous members`.

### Task 7: Typical sections X-X / Y-Y

**Files:**
- Create: `Services/PlanView/SectionRules.cs`
- Test: `tests/MCG_3DPanel.Tests/SectionRulesTests.cs`

**Interfaces:**
- Consumes: `PlanViewInput`, `PlanViewLayout.SectionGapX/Y`.
- Produces: `static PvSection Build(PlanViewInput input, char which /* 'X' | 'Y' */, double planMinX, double planMinY,
  double planMaxX, double planMaxY)`. Plan extents are in sheet coordinates; the writer passes them.

**Rules (spec + references, block-local section coordinates, Y up = top of plate):**
- `X` collects every non-deleted member with `Dir == "T"`; `Y` every one with `Dir == "L"`. A member's position = its
  centerline coordinate across the section (X for T members, Y for L). Members sharing a `Name` (merged) appear once,
  at the longest piece.
- Top Plate: rect from 0 to the plate length along the section × `PlateThickness`, top at y = 0.
- Web: rect of `Thickness` × `Height` hanging below the plate, centred on the position. Height = the main web height
  (spec, my call).
- Flange: rect of width `Thickness` (the flange width) × flange thickness (`Height` of the Flange member) under its
  web.
- HP/FB: `PvInsert` of `ProfileBlock` at (position, −PlateThickness), rotation 0, scale 1.
- One `thk_sym` per plate rect, in sheet space, scale = sheet scale / 50.
- Dims: spacing between consecutive HP/FB positions on `Mechanical-AM_5`, one chain below the section.
- Placement:
  - `X`: insertion above the plan at `planMaxY + SectionGapY × scale`, X = plan `MinX`.
  - `Y`: rotated 90°, left of the plan at `planMinX − SectionGapX × scale`.
- `BlockName = "{panel}_X-X_MCG"` or `"{panel}_Y-Y_MCG"`.
- Every distinct `ProfileBlock` goes into `RequiredBlocks`.

- [ ] **Step 1: Failing tests.**
  - `X_7D17P_TwoGirdersAnd22HP`: T1, T2 webs (374×6, flange 380×20) + 22 HP ⇒ 4 web/flange rects + 1 plate rect +
    22 `HP120x6` inserts.
  - `Y_7D17P_FlangeWidths`: seven L flanges ⇒ widths 250/170/170/150/170/170/250.
  - `DeletedMember_NotInSection`.
  - `MergedMembers_AppearOnce`.
  - `UnknownProfile_ListedInRequiredBlocks`: `HP140x7` ⇒ present in `RequiredBlocks` (Review Focus 3).
- [ ] **Step 2: Run** `--filter SectionRulesTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(planview): typical sections X-X and Y-Y`.

### Task 8: Lashing-hole plan, fixed items, and the full model

**Files:**
- Create: `Services/PlanView/SheetItemRules.cs`
- Create: `Services/PlanView/PlanViewBuilder.cs`
- Test: `tests/MCG_3DPanel.Tests/PlanViewBuilderTests.cs`

**Interfaces:**
- Produces:
  - `static List<PvInsert> LashingPlan(string panel, double planMaxX, double planY, int scale)`:
    - `{panel}_Top Plate` + `{panel}_Lashing Hole` at `(planMaxX + LashingPlanGap × scale, planY)`;
    - `M.D1`, `M.D2` by `MdOffset/MdSpacing`.
  - `static List<PvInsert> Fixed(double frameX, double frameY, int scale)`:
    - `Measurements_Tolerances vFuture` at frame origin + `(ToleranceNoteX, ToleranceNoteY) × scale`, on
      `Mechanical-AM_6`.
  - `static List<PvText> Titles(...)`: the two view titles, `Mechanical-AM_1`, 5 mm, `IsMText = true`.
  - `static List<PvInsert> ToleranceBoxes(IReadOnlyList<PvDim> overall, int scale)`: one `Rec. for tolerance` per
    overall dim from `DraftDimRules.Overall`, at that dim's text position, `Scale = scale`.
  - `static PlanViewModel PlanViewBuilder.Build(PlanViewInput input, SheetContext sheet)`, with `sealed class
    SheetContext { int Scale; double FrameX, FrameY; double PlanMinX, PlanMinY, PlanMaxX, PlanMaxY; double[]
    AssyTransform; }`. It calls Tasks 4–8 and fills `RequiredBlocks`.

The lashing-plan AM_4 dims are the `LashingHole` drafts again, offset by the lashing-plan insertion
(`DraftDimRules.FromDrafts` with a translation; add the optional parameter `(double dx, double dy) shift = default`).

- [ ] **Step 1: Failing tests.**
  - `Build_7D17P_Counts`: synthetic 7D-17P input ⇒
    - names = 9 girder + all stiffener names;
    - continuous = 95;
    - 2 sections;
    - inserts include `M.D1`, `M.D2`, `Measurements_Tolerances vFuture`, 2 × `Rec. for tolerance`;
    - `RequiredBlocks` ⊇ {`CS_horl_up_right`, `thk_sym`, `HP120x6`, `BS 80x6`, `M.D1`, `M.D2`}.
  - `Fixed_ToleranceNote_At1to75`: frame (0, 0), scale 75 ⇒ (2402.33, 8228.57) ±0.05 (the 7D-17C reference).
- [ ] **Step 2: Run** `--filter PlanViewBuilderTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** the full suite `dotnet test tests/MCG_3DPanel.Tests` → all PASS.
- [ ] **Step 5: Commit** `feat(planview): lashing plan, fixed items, full model`.

### Task 9: Own-output marking and hand-drawn detection (pure part)

**Files:**
- Create: `Services/PlanView/HandDrawnRules.cs`
- Test: `tests/MCG_3DPanel.Tests/HandDrawnRulesTests.cs`

**Interfaces:**
- Produces:
  - `sealed class PvExisting { string Type /* "Text" | "MText" | "Dim" | "BlockRef" */; string Layer; string Text;
    string BlockName; double X1, Y1, X2, Y2, Rotation; bool HasOwnMark; }`
  - `static bool IsOwn(PvExisting e)` ⇒ `HasOwnMark`.
  - `static bool IsReplaceable(PvExisting e, string panel, IReadOnlyList<PvDraft> draftsInSheetSpace,
    (double, double, double, double) topPlateSheet)`. True for:
    - Text/MText on `Mechanical-AM_1` / `Mechanical-AM_6` matching `^(T|L|HT|HL)\d+[a-z]?$|^(BS|B|S|GB)$|^(OB|GE)\w*$`;
    - Dims on `Mechanical-AM_4` / `AM_5` whose axis and interval match a draft (±1 mm), or the overall Top Plate
      length/width;
    - BlockRefs `{panel}_X-X`, `{panel}_Y-Y`, `T.{panel}_X-X`, `T.{panel}_Y-Y`;
    - BlockRefs `{panel}_Top Plate` / `{panel}_Lashing Hole` **in Model Space** (not inside `_Assy`);
    - BlockRefs `CS_horl_up_right`, `CS_symbol`, `thk_sym`, `M.D1`, `M.D2`, `Rec. for tolerance`,
      `Measurements_Tolerances vFuture`.

    Everything else is false: `DETAIL 1`, `1A-1A`, `REF. DIM`, `(<>)` dims, chain totals.
  - Keep both is remembered outside this class. Task 10 writes XRecord `MCG3D_PLANVIEW_KEEP_{SourceBtrHandle}` in the
    Named Objects Dictionary of the 2D drawing. When it exists, the run skips detection and the dialog (Review
    Focus 4). **Replace** deletes it.

- [ ] **Step 1: Failing tests.**
  - `Replaceable_Labels` (`T1`, `HT12`, `L6`, `T6a`, `BS`, `B`, `OB1`, `GE1` ⇒ true; `DETAIL 1`, `1A-1A`,
    `FACE PLATE` ⇒ false).
  - `Replaceable_DimMatchingDraft_True`.
  - `ReferenceDim_ParenText_False`.
  - `LashingPlanInsideAssy_False`.
  - `OwnMarked_IsOwn`.
- [ ] **Step 2: Run** `--filter HandDrawnRulesTests` → FAIL.
- [ ] **Step 3: Implement.**
- [ ] **Step 4: Run** → PASS.
- [ ] **Step 5: Commit** `feat(planview): own-output and hand-drawn detection rules`.

### Task 10: AutoCAD side: snapshot, locate, write (the command)

**Files:**
- Create: `Commands/PlanViewSnapshot.cs`: `static PlanViewInput Read(Database db3D, BuildResult build, out string
  error)`. It runs in the 3D file before switching and reads:
  - members: `NamingSource.FromBuild` + `ResolveNames`, live pieces, `Spec`, sizes via `NamingSource.ApplySizes`.
    `ProfileBlock` comes from the HP/FB spec (`HP 120x6.0` ⇒ `HP120x6`, `FB 80x6.0` ⇒ `BS 80x6`), the spelling of the
    reference blocks;
  - AM_9 drafts: every `Dimension` on `Mechanical-AM_9` in `{panel}_Structure` / `{panel}_Lashing Hole` under the
    source block, transformed into 2D-block coordinates;
  - marks: see Task 6;
  - Top Plate extents and the lashing diameter (first `Circle` in `{panel}_Lashing Hole`).
- Create: `Commands/Source2DLocator.cs`: `static string Find(Database db3D, PanelMeta meta, out string reason)`.
  - Checks `meta.SourcePath`. The file must exist and its DB (open document, else `ReadDwgFile` read-only) must hold a
    `BlockReference` whose handle = `SourceRefHandle` and whose BTR handle = `SourceBtrHandle`.
  - Otherwise it asks with an Open-file dialog, titled `Select the 2D drawing of {panel}`, checks again, and stores the
    new path into the meta XRecord of the 3D file.
- Create: `Commands/PlanViewWriter.cs`:
  - `static List<string> Missing(Database db2D, PlanViewModel m)`: layers, dim styles, `STANDARD`, every
    `RequiredBlocks` name.
  - `static int EraseOwn(Database db2D, string btrHandle)`.
  - `static List<ObjectId> FindHandDrawn(Database db2D, ...)`, using `HandDrawnRules`, within the frame extents and
    outside the `_Assy` tree.
  - `static void Write(Database db2D, PlanViewModel m, SheetContext s, string btrHandle)`: one transaction; XData
    `MCG3D_PLANVIEW` (register `RegAppTableRecord` if absent; the only table record the tool may add); dims with
    `DimensionStyle` = the named style + `Dimscale` + `Dimtmove` overrides; section blocks (re)defined as
    `{panel}_X-X_MCG` / `_Y-Y_MCG`.
- Create: `Commands/PlanViewCommand.cs`: `[CommandMethod("MCG_PlanView", CommandFlags.Session)]`, plus
  `static void Request()` (stores the active 3D doc and calls `SendStringToExecute("MCG_PlanView ")` like
  `StepExport.Request`). Flow = spec Flow steps 1–10:
  - unsaved 3D file ⇒ reuse `UnsavedFileDialog.AskSave` and the existing Save-and-Update save routine;
  - open the 2D drawing with `DocumentManager.Open(path, false)` or activate the open document;
  - read-only or locked ⇒ `"{file} is read-only or in use."`;
  - hand-drawn found ⇒ the new dialog `UI/HandDrawnDialog.xaml` with buttons **Replace** / **Keep both** / **Cancel**
    and the text `{n} hand-drawn items found on the {panel} sheet.`;
  - status `Plan View: {n} labels, {m} dimensions, sections X-X / Y-Y.`, raised through
    `public static event Action<string, bool> Finished`.
- Modify: `MCG_3DPanel.csproj` only if new XAML needs it (SDK globbing normally covers it).

- [ ] **Step 1:** Implement the four files and the dialog. `deploy.bat` → `Build succeeded.`
- [ ] **Step 2: Manual run in AutoCAD 2026** on a copy of `CAS-0062242.dwg` (7D-17P) and its 3D file. Check:
  1. First run on the hand-drawn sheet ⇒ dialog shows; **Replace** ⇒ hand labels and lashing plan gone; tool output
     present; `DETAIL`/`(<>)` items untouched; `_Assy` handles unchanged (dump before/after: same handles in
     `7D-17P_Structure`).
  2. Second run ⇒ no dialog, one set of output (Review Focus 4).
  3. **Keep both** on a fresh copy, then run again ⇒ no dialog the second time, hand items still present, one tool
     set.
  4. Move and rotate the `_Assy` reference, run ⇒ labels follow (Review Focus 2).
  5. Rename the `HP120x6` block in the 2D file, run ⇒ stop listing `HP120x6`, nothing written (Review Focus 3).
  6. Make the 2D file read-only, run ⇒ the message from Review Focus 5.
  7. `MCG_DumpDrawing` of the result vs the reference dump: counts per layer within ±10 %.
- [ ] **Step 3: Commit** `feat(planview): Create Drawing Plan View command`.

### Task 11: Palette button, checklist, session log

**Files:**
- Modify: `UI/PanelResultView.xaml:45-55`: add `Button x:Name="BtnCreateDrawing" Content="Create Drawing ▾"` with
  `ToolbarPrimaryButtonStyle`, a ContextMenu with `MenuItem x:Name="MnuPlanView" Header="Plan View"`, and
  `ToolTip="Write names, symbols, dimensions and sections into the 2D drawing"`.
- Modify: `UI/PanelResultView.xaml.cs`:
  - `BtnCreateDrawing_Click` opens the menu as `BtnExportMenu_Click` does;
  - `MnuPlanView_Click` calls `PlanViewCommand.Request()`;
  - the item is disabled with ToolTip `Open a 3D panel file first` when the active drawing has no 3D panel (same test
    as `MnuExportStep`);
  - subscribe `PlanViewCommand.Finished` to `SetStatus`.
- Modify: `docs/ux-checklist.md`: section **J. Create Drawing** with the seven Task 10 checks plus "button disabled
  outside a 3D file".
- Modify: `SESSION_LOG.md`: session entry.

- [ ] **Step 1:** Implement. `deploy.bat` → `Build succeeded.`
- [ ] **Step 2:** In AutoCAD: the button sits right of Export ▾. Disabled in a 2D drawing with a tooltip; enabled in a
  3D file; Plan View runs Task 10's flow.
- [ ] **Step 3: Commit** `feat(ui): Create Drawing dropdown with Plan View`.
