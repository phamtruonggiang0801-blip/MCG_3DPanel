# GB toe anchor + Result tab corrections — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (Phase 0) GB nhận đủ mốc toe (HP/FB xuyên hoặc dừng ≤ 50mm) và có lỗ CO + collar cho HP xuyên vùng GB;
(Phase 1) tab Result theo họ kết cấu, Kind dropdown, đảo chiều HP/Bracket, sửa xong tự rebuild panel.

**Architecture:** Correction lưu trong bộ nhớ, khoá bằng `SourceKey` ổn định của part 2D. Correction được áp
**bên trong** `ClassificationService` (tách GE/GB, mã bracket, bụng HP, đổi PartType). Rebuild = phân loại lại
bản 2D (kể cả block reference đã xoá khi Keep) → build → thay block 3D cũ, chạy trong một lệnh AutoCAD, nên
mỗi lần rebuild là một bước Undo. Logic danh mục họ/Kind viết thuần C# (không phụ thuộc AutoCAD) để test được.

**Tech Stack:** C# / .NET 8 (`net8.0-windows`), AutoCAD 2026 managed API, WPF; xUnit cho phần thuần.

**Spec:** [docs/superpowers/specs/2026-11-04-result-tab-corrections-design.md](../specs/2026-11-04-result-tab-corrections-design.md)

## Global Constraints

- `GB_ANCHOR_GAP = 50.0` mm: HP/FB dừng cách mặt web ≤ 50 được tính là mốc toe.
- Toe = đường tâm HP/FB + `GB_TOE_PAST_HP` (100) + `GB_TOE_PAD` (60). Không đổi hằng số hiện có.
- GB chỉ xuất hiện khi L ≥ Max Stiff (quy tắc hiện tại, không đổi).
- Không hiện "GB?" ở bất kỳ đâu trên tab Result. Không hiện CutOut.
- Đổi OB ↔ IB qua dropdown ⇒ hướng gấp tự đảo. Nút ⇄ đảo thêm một lần (hai lần đảo triệt tiêu nhau).
- Correction chỉ nhớ trong phiên mở bản vẽ.
- Dropdown chỉ đổi trong cùng họ. GE đứng riêng thuộc họ Web, GB đứng riêng thuộc họ Bracket.
- Rebuild: không hỏi chọn panel, không hỏi Keep, chỉ build lại panel có correction thay đổi, giữ dòng đang chọn.
- Chữ trên UI viết tiếng Anh, theo các nhãn/tooltip đang có. Comment code viết tiếng Việt, theo quy ước của repo.
- Build xác minh: `dotnet build -c Debug` → `0 Error(s)`.
- Plugin chỉ chạy trong AutoCAD (`acdbmgd` không nạp được ngoài AutoCAD). Phần đụng tới AutoCAD được kiểm bằng
  checklist trong AutoCAD và build report JSON. Chỉ phần thuần (Task 4) có unit test.

## Review Focus

1. **Web có 2 đầu** (một đầu GE, đầu kia GB, hoặc một đầu HP và đầu kia FB): Kind hiện đúng cả hai mã. Chọn
   "Web" thì cả hai đầu thôi tách. Kind hiện tại luôn có mặt trong dropdown. → test `Describe_WebTwoEnds_*` (Task 4).
2. **Bản 2D không còn** (đã PURGE, hoặc đã Save rồi mở lại): rebuild không xoá block 3D cũ, thanh trạng thái báo
   "Source 2D block no longer exists — cannot rebuild." → checklist Task 6.
3. **Build lỗi khi rebuild** (exception, hoặc ra 0 solid): giữ nguyên block 3D cũ, correction vẫn còn, báo lỗi.
   → checklist Task 6.
4. **Ctrl+Z sau khi rebuild:** 3D quay về bản cũ. Bấm vào dòng có solid đã bị xoá thì không crash, chỉ báo
   "Solid no longer exists — rebuild or re-run Build 3D." → checklist Task 7.
5. **Build nhiều panel một lúc rồi rebuild một panel:** các panel khác không bị đụng tới. Tên block / cột Panel của
   panel được rebuild giữ nguyên. → checklist Task 6.

---

## Phase 0 — GB

### Task 1: Mốc toe GB = HP/FB xuyên hoặc dừng ≤ 50mm

**Files:**
- Modify: `Services/Classify/ClassifyUtil.cs` (cạnh `GB_TOE_PAD`, dòng ~34)
- Modify: `Services/Classify/GirderEndSplitter.cs:57-66` (chỗ gọi) và `:132-154` (thay `NearestCrossingHp`)

**Interfaces:**
- Produces: `ClassifyUtil.GB_ANCHOR_GAP = 50.0`;
  `private static double NearestToeAnchor(PanelPart p, Point3d e, Vector3d dir, double limit, List<PanelPart> hpParts, List<PanelPart> fbParts, PanelPart endMember, out PanelPart anchor, out string how)`.
  Trả về s (khoảng dọc `dir` tới đường tâm mốc), hoặc −1 nếu không có mốc. `how` ∈ `"HP xuyên"`, `"HP dừng {gap:0}mm"`,
  `"FB xuyên"`, `"FB dừng {gap:0}mm"`.

- [ ] **Step 1: Thêm hằng `GB_ANCHOR_GAP = 50.0`** vào `ClassifyUtil`, kèm comment: "HP/FB dừng cách mặt web ≤ 50 vẫn làm mốc toe GB (kỹ sư 2026-11-04)".
- [ ] **Step 2: Viết `NearestToeAnchor`** thay cho `NearestCrossingHp`. Ứng viên = `hpParts` (lọc `MemberLength > HP_MIN_LEN`) cộng `fbParts` (không lọc chiều dài), bỏ `endMember`, chỉ giữ những part thoả `Cross(p, m)`. Với mỗi ứng viên, tính s, u như code hiện tại rồi:
  - Nhận là **xuyên** nếu `u ∈ [-0.02, 1.02]`.
  - Nếu không xuyên: `gap = min(DistToPart(m1, p), DistToPart(m2, p))` (m1, m2 = `Endpoints(m)`). Nhận là **dừng** nếu `gap ≤ GB_ANCHOR_GAP`.
  - Giữ điều kiện `s ∈ (2·ADJ_TOL, limit)`. Chọn s nhỏ nhất.

  FB xuyên web đã bị `FlatBarSplitter` cắt thành hai đoạn dừng ở mặt web, nên sẽ được nhận theo nhánh "dừng" (gap ≈ 0). Ghi nhãn `how` là "FB xuyên" khi gap ≤ 1mm và có một đoạn FB khác cùng `Id` nằm ở phía bên kia web; còn lại ghi "FB dừng".
- [ ] **Step 3: Đổi chỗ gọi.** Thay `NearestCrossingHp(...)` bằng `NearestToeAnchor(..., fbParts, endMember, out var anchor, out var how)`. Note thành công đổi thành `... → {ge.Code} zone={zone:0} (mốc toe {anchor.Id.Handle} {how}) ...`. Note thất bại đổi chữ "KHÔNG có HP xuyên" thành "KHÔNG có HP/FB xuyên hoặc dừng ≤ 50mm". Riêng mã `p.Code = "GB?"` thì chưa bỏ ở task này (Task 7 xử lý).
- [ ] **Step 4: Build.** Chạy `dotnet build -c Debug`. Kỳ vọng: `0 Error(s)`.
- [ ] **Step 5: Kiểm trong AutoCAD** (deploy theo skill `deploy-and-reload`). Dùng bản vẽ có GB: mẫu CAS (FB cách web 863.9, HP cắt giữa cách 643.9), cộng một panel do kỹ sư cung cấp có HP dừng 30mm và một panel có FB làm mốc. Chạy `MCG_Build3DPanel` rồi mở build report JSON. Kỳ vọng:
  - Mẫu CAS vẫn ra cùng zone như trước (`mốc toe … HP xuyên`).
  - Ca HP dừng 30mm ra GB với `HP dừng 30mm`.
  - Ca FB ra GB với `FB …`.
  - Không còn note "giữ Web chữ nhật" ở các ca này.
- [ ] **Step 6: Commit**

```bash
git add Services/Classify/ClassifyUtil.cs Services/Classify/GirderEndSplitter.cs
git commit -m "feat(gb): accept HP/FB crossing or stopping within 50mm as GB toe anchor"
```

### Task 2: Lỗ CO + Collar Plate cho HP xuyên vùng GB

**Files:**
- Modify: `Services/Build3DService.cs:118` (danh sách solid được khoét)
- Modify: `Services/Builders/WebCollarPlateBuilder.cs:62` (danh sách run)

**Interfaces:**
- Consumes: part `GirderEnd` có `Code` bắt đầu bằng `"GB"` và `JoinWithWeb != null` (do `GirderEndSplitter` tạo).

- [ ] **Step 1: Khoét lỗ CO cho GB.** Ở `Build3DService`, ngoài part `PartType.Web`, đưa thêm part `GirderEnd` có `Code` bắt đầu bằng `"GB"` vào danh sách truyền cho `WebHpCutoutBuilder.ApplyWebCutouts`. Bước này chạy **trước** vòng hợp nhất GE/GB (dòng ~133), nên lỗ được khoét trên solid GB rồi mới hợp nhất vào Web. Dùng danh sách riêng `cutSolids`, để `webSolids` vẫn chỉ chứa Web, vì chỗ khác có thể dùng `webSolids` với giả định đó.
- [ ] **Step 2: Collar cho GB.** Trong `BuildCollarPlates`, sau khi lấy `runs = ctx.Cls.WebRuns()`, nối mỗi part GB (cùng điều kiện như Step 1) vào run chứa `JoinWithWeb` của nó. Như vậy GB đi qua cùng quy tắc junction / `WebHeight < 300` như web thường (spec: "như HP xuyên web"). Không sửa `ClassificationResult.WebRuns()`.
- [ ] **Step 3: Build.** Chạy `dotnet build -c Debug`. Kỳ vọng: `0 Error(s)`.
- [ ] **Step 4: Kiểm trong AutoCAD** trên mẫu CAS. Kỳ vọng:
  - Solid Web+GB có lỗ CO tại HP cắt giữa (cách web 643.9).
  - Report có dòng `[WebHpCutout]` cho cặp GB~HP đó.
  - Có collar khi điều kiện junction / `WebHeight < 300` thoả.
  - GB vẫn hợp nhất thành 1 lump (`[GE merge] … lumps=1`).
- [ ] **Step 5: Commit**

```bash
git add Services/Build3DService.cs Services/Builders/WebCollarPlateBuilder.cs
git commit -m "feat(gb): cut HP CO and build collar plates inside the GB zone"
```

### Task 3: Kiểm chứng Bracket B / FB ngắt / Sniped trong vùng GB + cập nhật skill

**Files:**
- Modify: `.claude/skills/girder-bracket/girder-bracket_SKILL.md` (dòng 18 và 42)
- Modify: `SESSION_LOG.md`

- [ ] **Step 1: Kiểm trong AutoCAD** trên các panel của Task 1. Kỳ vọng:
  - HP dừng 30mm trước GB có **Bracket B** (report `[HpEndBracket] … × GirderEnd`).
  - FB xuyên GB bị ngắt theo đúng bề dày GB.
  - FB dừng 30mm có **Sniped End**.

  Ca nào không đạt thì dừng lại, ghi vào `SESSION_LOG.md` kèm handle, và báo kỹ sư. Không tự sửa ngoài plan.
- [ ] **Step 2: Cập nhật skill girder-bracket.**
  - Mục 1: mốc toe = HP/FB xuyên hoặc dừng ≤ 50mm; HP xuyên ⇒ CO + collar (đã cài).
  - Mục cài đặt: thay câu "GB không có HP xuyên → GB?" bằng "không có mốc ⇒ giữ Web, ghi Notes".
  - Ghi lại kết quả Step 1.
- [ ] **Step 3: Commit**

```bash
git add .claude/skills/girder-bracket/girder-bracket_SKILL.md SESSION_LOG.md
git commit -m "docs(gb): record toe anchor rule and GB-zone verification"
```

---

## Phase 1 — Tab Result

### Task 4: Danh mục họ / Kind (thuần C#) + test

**Files:**
- Create: `UI/ResultCatalog.cs` (namespace `MCG_3DPanel.UI`; **không** `using Autodesk.*`)
- Create: `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj` (xUnit, `net8.0-windows`, `<Compile Include="..\..\UI\ResultCatalog.cs" Link="ResultCatalog.cs" />`)
- Create: `tests/MCG_3DPanel.Tests/ResultCatalogTests.cs`
- Modify: `MCG_3DPanel.csproj`: thêm `<Compile Remove="tests/**" />`

**Interfaces:**
- Produces:

```csharp
public enum ResultFamily { Topplate, Web, Flange, Bracket, HP, FB, Collar }
public enum CorrectionIntent { None, ForceWeb, ForceGE, ForceGB, ClosedPlate, Flange, FacePlate, BracketOB, BracketIB, BracketBF, BackingPlate }
public sealed class KindOption { public string Text; public bool Enabled; public string Tooltip; }
public sealed class RowDescriptor {
    public ResultFamily Family; public string Kind; public List<KindOption> Options; // Options rỗng ⇒ Kind chỉ đọc
    public bool CanFlip; }
public static class ResultCatalog {
    // partType = PartType.ToString() khi có part 2D, null với solid sinh thêm; solidType = "CollarPlate"/"HpEndBracketB"/"GirderFlange".
    // endKinds = loại member ở các đầu web chạm HP/FB ("HP"/"FB"), geAvailable/gbAvailable = có zone GE / có mốc toe GB.
    public static RowDescriptor Describe(string partType, string code, string solidType,
        IReadOnlyList<string> mergedCodes, IReadOnlyList<string> endKinds, bool geAvailable, bool gbAvailable);
    public static CorrectionIntent ParseIntent(string kind);
    public const string NoGbAnchorTip = "No crossing or nearby HP/FB to locate the GB toe";
    public const string NoGeZoneTip = "No perpendicular web to end the GE";
}
```

Bảng ánh xạ (theo spec mục 3):

| Đầu vào | Family | Kind | Options | CanFlip |
|---|---|---|---|---|
| `TopPlate` | Topplate | `Topplate` | — | no |
| `Web`, merged=[] | Web | `Web` | `Web`; với endKinds ≠ ∅: `Web + GE-{k}`, `Web + GB-{k}`; `Closed Plate` | no |
| `Web`, merged=[GE-HP] | Web | `Web + GE-HP` | như trên | no |
| `ClosedPlate` | Web | `Closed Plate` | như dòng Web | no |
| `GirderEnd` `GE-k` | Web | `GE-k` | `GE-k`, `GB-k`, `Web` | no |
| `GirderEnd` `GB-k` | Bracket | `GB-k` | `GE-k`, `GB-k`, `Web` | no |
| `Flange` / `FacePlate` | Flange | `Main Flange` / `Faceplate` | `Main Flange`, `Faceplate` | no |
| null + `GirderFlange`, code `GE-HP` | Flange | `Flange GE/GB-HP` | — | no |
| `Bracket`, code `OB12` | Bracket | `OB` | `OB`, `IB`, `BF`, `Backing Plate` | yes |
| `BackingPlate` | Bracket | `Backing Plate` | như Bracket | no |
| null + `HpEndBracketB` | Bracket | `B` | — | no |
| `HP` | HP | `HP` | — | yes |
| `FlatBar` | FB | `FB` | — | no |
| null + `CollarPlate` | Collar | `Collar Plate` | — | no |

`{k}` = các endKinds khác nhau, nối bằng `" + GE-"`. Ví dụ endKinds [HP, FB] ⇒ `Web + GE-HP + GE-FB`. Kind hiện tại luôn có trong Options; nếu chưa có thì chèn lên đầu. Option GE bị `Enabled=false` khi `!geAvailable` (Tooltip `NoGeZoneTip`), option GB bị `Enabled=false` khi `!gbAvailable` (Tooltip `NoGbAnchorTip`). `ParseIntent`: `Web` → ForceWeb; bắt đầu bằng `Web + GE` hoặc `GE-` → ForceGE; bắt đầu bằng `Web + GB` hoặc `GB-` → ForceGB; `Closed Plate`, `Main Flange`, `Faceplate`, `OB`, `IB`, `BF`, `Backing Plate` → enum tương ứng; còn lại → None.

- [ ] **Step 1: Tạo test project và `Compile Remove`.** Chạy `dotnet build -c Debug` ở root. Kỳ vọng: plugin vẫn `0 Error(s)` (không nuốt file trong tests).
- [ ] **Step 2: Viết test thất bại** trong `ResultCatalogTests.cs`:

```csharp
[Fact] public void Describe_Bracket_OB12_IsBracketOB_CanFlip() {
    var d = ResultCatalog.Describe("Bracket", "OB12", null, new string[0], new string[0], false, false);
    Assert.Equal(ResultFamily.Bracket, d.Family); Assert.Equal("OB", d.Kind); Assert.True(d.CanFlip);
    Assert.Equal(new[] { "OB", "IB", "BF", "Backing Plate" }, d.Options.Select(o => o.Text)); }
[Fact] public void Describe_WebMergedGeHp_ListsGeGbClosed() {
    var d = ResultCatalog.Describe("Web", "", null, new[] { "GE-HP" }, new[] { "HP" }, true, true);
    Assert.Equal("Web + GE-HP", d.Kind);
    Assert.Equal(new[] { "Web", "Web + GE-HP", "Web + GB-HP", "Closed Plate" }, d.Options.Select(o => o.Text)); }
[Fact] public void Describe_WebNoGbAnchor_DisablesGb() {
    var d = ResultCatalog.Describe("Web", "", null, new string[0], new[] { "HP" }, true, false);
    var gb = d.Options.Single(o => o.Text == "Web + GB-HP");
    Assert.False(gb.Enabled); Assert.Equal(ResultCatalog.NoGbAnchorTip, gb.Tooltip); }
[Fact] public void Describe_WebTwoEnds_MixedKindIsPresent() {
    var d = ResultCatalog.Describe("Web", "", null, new[] { "GE-HP", "GB-FB" }, new[] { "HP", "FB" }, true, true);
    Assert.Equal("Web + GE-HP + GB-FB", d.Kind); Assert.Equal(d.Kind, d.Options[0].Text);
    Assert.Contains(d.Options, o => o.Text == "Web + GE-HP + GE-FB"); }
[Fact] public void Describe_StandaloneGb_IsBracketFamily() {
    Assert.Equal(ResultFamily.Bracket, ResultCatalog.Describe("GirderEnd", "GB-FB", null, new string[0], new string[0], true, true).Family); }
[Fact] public void Describe_WebCodeGbQuestion_ShowsPlainWeb() {
    Assert.Equal("Web", ResultCatalog.Describe("Web", "GB?", null, new string[0], new[] { "HP" }, true, false).Kind); }
[Fact] public void Describe_Generated_AreReadOnly() {
    Assert.Empty(ResultCatalog.Describe(null, "", "CollarPlate", new string[0], new string[0], false, false).Options);
    Assert.Equal("Flange GE/GB-HP", ResultCatalog.Describe(null, "GE-HP", "GirderFlange", new string[0], new string[0], false, false).Kind);
    Assert.Equal("B", ResultCatalog.Describe(null, "", "HpEndBracketB", new string[0], new string[0], false, false).Kind); }
[Theory]
[InlineData("Web", CorrectionIntent.ForceWeb)] [InlineData("Web + GE-HP", CorrectionIntent.ForceGE)]
[InlineData("GB-FB", CorrectionIntent.ForceGB)] [InlineData("IB", CorrectionIntent.BracketIB)]
[InlineData("Backing Plate", CorrectionIntent.BackingPlate)] [InlineData("Topplate", CorrectionIntent.None)]
public void ParseIntent_Maps(string kind, CorrectionIntent expected) => Assert.Equal(expected, ResultCatalog.ParseIntent(kind));
```

- [ ] **Step 3: Chạy test, phải fail.** Chạy `dotnet test tests/MCG_3DPanel.Tests`. Kỳ vọng: lỗi biên dịch, `ResultCatalog` chưa có.
- [ ] **Step 4: Cài `ResultCatalog`** theo bảng ánh xạ.
- [ ] **Step 5: Chạy test, phải pass.** Chạy `dotnet test tests/MCG_3DPanel.Tests`. Kỳ vọng: tất cả pass. Chạy thêm `dotnet build -c Debug` ở root. Kỳ vọng: `0 Error(s)`.
- [ ] **Step 6: Commit**

```bash
git add UI/ResultCatalog.cs tests/ MCG_3DPanel.csproj
git commit -m "feat(result): pure family/kind catalog with unit tests"
```

### Task 5: SourceKey + kho correction + áp correction trong phân loại

**Files:**
- Modify: `Services/PanelPart.cs`: thêm thuộc tính cho `PanelPart`
- Create: `Services/Corrections/PartCorrection.cs`, `Services/Corrections/CorrectionStore.cs`
- Modify: `Services/ClassificationService.cs:24` (chữ ký, mở `openErased`), `:173-183` (gán key + áp đổi loại)
- Modify: `Services/Classify/GirderEndSplitter.cs` (ép GE/GB/Web; ghi endKinds/availability; `Segment` copy key)
- Modify: `Services/Classify/BracketCoder.cs:45` (đảo bụng HP), `:66` (ghi đè mã bracket)
- Modify: `Services/Builders/BracketBuilder.cs:282` (`BendFoldSign` nhân −1 khi `FlipFold`)

**Interfaces:**
- Consumes: `ResultCatalog.ParseIntent`, `CorrectionIntent` (Task 4).
- Produces:
  - `PanelPart`: `string SourceKey`, `bool FlipFold`, `List<string> EndTouchKinds`, `bool GeAvailable`, `bool GbAvailable`.
  - `public sealed class PartCorrection { public string Kind; public bool FlipDir; }` (Kind null/rỗng ⇒ không đổi loại).
  - `public static class CorrectionStore { IReadOnlyDictionary<string, PartCorrection> For(string panelKey); void Set(string panelKey, string sourceKey, PartCorrection c); void Clear(string panelKey, string sourceKey); }`. `panelKey` = handle của block reference 2D nguồn. Chỉ lưu trong bộ nhớ.
  - `ClassificationResult Classify(ObjectId blockRefId, PanelPosition position, IReadOnlyDictionary<string, PartCorrection> corrections = null)`.

- [ ] **Step 1: Gán `SourceKey`.** Ngay sau `WebBracketSplitter.Split` (dòng ~173), trước `GirderEndSplitter.Apply`: gom mọi part trong `result.Parts`, `hpParts`, `fbParts`, rồi nhóm theo `Id`. Mỗi nhóm giữ nguyên thứ tự xuất hiện, gán `SourceKey = $"{Id.Handle}#{i}"`. `GirderEndSplitter.Segment` copy `SourceKey` từ part nguồn, nên GE/GB và phần Web còn lại dùng chung key. Thứ tự gom phải tất định với cùng một bản 2D. Không dùng `HashSet` / `Dictionary` để duyệt.
- [ ] **Step 2: Áp đổi loại** (ngay sau Step 1). Với part có correction, `ParseIntent(Kind)` ra intent nào thì xử lý như sau:
  - `ClosedPlate` / `ForceWeb` trên ClosedPlate → đổi `Type` sang ClosedPlate / Web.
  - `Flange` / `FacePlate` → đổi `Type`.
  - `BackingPlate` → `Type = BackingPlate`.
  - `BracketOB` / `IB` / `BF` trên BackingPlate → `Type = Bracket`.

  Ghi Note `[Correction] {SourceKey}: {oldType} → {newType}`.
- [ ] **Step 3: Ép GE/GB/Web trong `GirderEndSplitter`.**
  - Ghi nhận: mỗi đầu web chạm HP/FB thì thêm `kind` vào `p.EndTouchKinds`. `GeAvailable |= t > 0`. `GbAvailable |= NearestToeAnchor(...) ≥ 0`. Tính anchor cho mọi đầu chạm, kể cả khi nhánh chọn ra GE.
  - Áp intent của `p.SourceKey`:
    - `ForceWeb`: không tách; GirderEnd đứng riêng đổi về `Type = Web`, `Code = null`.
    - `ForceGE`: tách GE nếu `t > 0`, với zone = t.
    - `ForceGB`: đi nhánh GB nếu có anchor.
    - Không thể ép (thiếu t hoặc thiếu anchor): giữ quyết định tự động và ghi Note `[Correction] … không áp được`.
  - Phần Web còn lại (`rest`) được copy `SourceKey`, `EndTouchKinds`, `GeAvailable`, `GbAvailable`, nên ép xong vẫn áp cho đầu kia.
- [ ] **Step 4: HP và Bracket trong `BracketCoder`.**
  - Ngay sau `h.BellyDir = BellyDir(...)`: nếu correction của `h.SourceKey` có `FlipDir` thì `h.BellyDir = -h.BellyDir`. Mã OB/IB tính sau đó dùng bụng đã đảo.
  - Sau `brk.Code = baseCode + SuffixFor(...)`: nếu intent là `BracketOB` / `IB` / `BF` thì thay `baseCode` bằng loại mới, giữ suffix. Nếu cặp (cũ, mới) là OB↔IB thì `brk.FlipFold = !brk.FlipFold`.
  - Sau đó: nếu correction có `FlipDir` thì `brk.FlipFold = !brk.FlipFold`.
- [ ] **Step 5: `BendFoldSign`.** Trả về `part.FlipFold ? -sign : sign`.
- [ ] **Step 6: Mở bản đã xoá.** Trong `Classify`, dùng `tr.GetObject(blockRefId, OpenMode.ForRead, true)` (openErased) cho block reference 2D.
- [ ] **Step 7: Build.** Chạy `dotnet build -c Debug`. Kỳ vọng: `0 Error(s)`.
- [ ] **Step 8: Commit**

```bash
git add Services/
git commit -m "feat(result): stable SourceKey and in-classification corrections (kind, HP belly, bracket fold)"
```

### Task 6: Rebuild một panel (lệnh nội bộ, một bước Undo)

**Files:**
- Modify: `Services/IBuild3DService.cs`: `BuildResult` thêm `ObjectId SourceRefId`, `PanelInput Input`, `PanelPosition Position`, `double BuildSeconds`
- Modify: `Commands/PanelCommands.cs`: tách `BuildOne`, thêm lệnh rebuild
- Modify: `UI/PaletteManager.cs`: giữ danh sách build hiện tại, nối sự kiện

**Interfaces:**
- Consumes: `CorrectionStore.For`, `Classify(..., corrections)` (Task 5).
- Produces:
  - `internal static BuildResult BuildOne(Database db, ObjectId src2D, PanelInput input, PanelPosition pos)`: phân loại với `CorrectionStore.For(src2D.Handle.ToString())`, build, gán `SourceRefId` / `Input` / `Position` / `BuildSeconds`. `RunBuild` dùng lại hàm này.
  - `public static void RequestRebuild(BuildResult current)`: ghi yêu cầu vào static, rồi `doc.SendStringToExecute("MCG_Rebuild3DPanel ", true, false, false)`.
  - `[CommandMethod("MCG_Rebuild3DPanel", CommandFlags.NoHistory)]`: thực thi yêu cầu, rồi phát `public static event Action<BuildResult /*old*/, BuildResult /*new*/> Rebuilt`.

- [ ] **Step 1: Tách `BuildOne`** ra khỏi vòng lặp của `RunBuild`. Hành vi `MCG_Build3DPanel` giữ nguyên (vẫn hỏi Keep).
- [ ] **Step 2: Viết lệnh rebuild.**
  - Kiểm `SourceRefId` còn mở được (openErased) và định nghĩa block chưa bị xoá. Nếu không: báo "Source 2D block no longer exists — cannot rebuild." và dừng.
  - `BuildOne` → nếu exception hoặc `SolidCount == 0`: xoá bản mới, giữ bản cũ, báo lỗi.
  - Thành công: xoá block reference 3D cũ và BTR cũ, đổi tên BTR mới về tên block cũ, đặt `Kept = true`.
  - Không hỏi Keep. Không ghi build report JSON (chỉ `MCG_Build3DPanel` mới ghi).
  - Đo `BuildSeconds`.
- [ ] **Step 3: PaletteManager.** Giữ `List<BuildResult> _builds`. Khi nhận `Rebuilt(old, new)`: thay `old` bằng `new` trong danh sách rồi gọi `_result.Load(_builds, …)` (Task 7 định nghĩa tham số giữ dòng chọn).
- [ ] **Step 4: Build.** Chạy `dotnet build -c Debug`. Kỳ vọng: `0 Error(s)`.
- [ ] **Step 5: Kiểm trong AutoCAD.** Tạm gọi `RequestRebuild` từ một nút debug, hoặc làm sau Task 7. Kỳ vọng:
  - Block cũ bị thay, tên giữ nguyên.
  - Một lần Ctrl+Z trả về bản cũ.
  - Ca Review Focus 2, 3, 5 đạt.
  - Ghi `BuildSeconds` thực tế vào `SESSION_LOG.md`. Nếu > 2s thì báo kỹ sư (spec mục 9).
- [ ] **Step 6: Commit**

```bash
git add Services/IBuild3DService.cs Commands/PanelCommands.cs UI/PaletteManager.cs SESSION_LOG.md
git commit -m "feat(result): in-place single-panel rebuild as one undoable command"
```

### Task 7: Giao diện tab Result mới

**Files:**
- Modify: `UI/ResultRow.cs` (viết lại phần nhãn bằng `ResultCatalog`, bỏ `CorrectedType`)
- Modify: `UI/PanelResultView.xaml` (cột mới), `UI/PanelResultView.xaml.cs` (dropdown/⇄/rebuild/export)
- Modify: `Services/PanelPart.cs` (`bool Sniped`), `Services/Builders/FlatBarBuilder.cs` (đặt `part.Sniped = true` khi vát)
- Modify: `Services/Classify/GirderEndSplitter.cs:62`: bỏ `p.Code = "GB?"` (giữ Note)

**Interfaces:**
- Consumes: `ResultCatalog.Describe` (Task 4); `CorrectionStore` (Task 5); `PanelCommands.RequestRebuild`, `BuildResult.SourceRefId`, `BuildSeconds` (Task 6).
- Produces: `ResultRow` có `Family`, `Kind`, `List<KindOption> Options`, `bool KindEditable`, `string Size`, `bool CanFlip`, `string DirText`, `string Flags`, `bool IsCorrected`, `internal string PanelKey`, `internal string SourceKey`;
  `public void Load(List<BuildResult> builds, string keepPanelKey = null, string keepSourceKey = null)`.

- [ ] **Step 1: ResultRow.**
  - `Describe(...)` lấy từ `Part.Type`, `Part.Code`, `Solid.Type`, `MergedCodes`, `Part.EndTouchKinds`, `GeAvailable`, `GbAvailable`.
  - **Size:**
    - Topplate: `t{Input.TopPlateThickness}`.
    - Web, Closed Plate, Bracket, Backing Plate, GE/GB: `t{round FootprintThickness}`.
    - Flange, Faceplate: `{round FootprintThickness}*{Input.FlangeThickness}`.
    - HP: `Input.HpSpec`. FB: `Input.FlatBarSpec`.
    - Solid sinh thêm: rỗng.
  - **DirText:** HP `"flipped"` hoặc `""`; Bracket `"flipped"` khi correction có `FlipDir`.
  - **Flags:** `"sniped"` khi `Part.Sniped`.
  - **IsCorrected:** `CorrectionStore` có mục cho (PanelKey, SourceKey).
  - **Sắp xếp:** theo `Family`, rồi Kind, rồi Size.
  - Xoá `KindOf` / `Label` cũ, cùng mọi xử lý "GB?".
- [ ] **Step 2: XAML.**
  - Cột: `Panel` (ẩn khi chỉ có 1 panel), `Handle`, `Family`, `Kind`, `Size`, `Dir`, `Flags`, `⌖`.
  - Cột `Kind` là `DataGridTemplateColumn`:
    - `KindEditable` → `ComboBox` có `ItemsSource=Options`, mỗi item `IsEnabled={Enabled}` và `ToolTip={Tooltip}`.
    - Không editable → `TextBlock`.
  - Cột `Dir`: nút `⇄` hiện khi `CanFlip`, kèm `DirText`.
  - Bỏ cột "Correct type".
  - Cập nhật helper text: "Select a row to highlight. Change Kind or press ⇄ to correct; the panel rebuilds automatically."
  - Menu chuột phải "Clear Correction" giữ nguyên.
- [ ] **Step 3: Code-behind.**
  - Đổi Kind hoặc bấm ⇄ → `CorrectionStore.Set` → `PanelCommands.RequestRebuild(build of row)`. Chọn lại đúng Kind gốc thì `Clear`.
  - "Clear Correction" → `Clear` rồi rebuild.
  - Sau `Rebuilt`: `Load(builds, panelKey, sourceKey)` chọn lại dòng cũ, cuộn tới, highlight. Status: `Rebuilt {panel} in {BuildSeconds:0.0} s`.
  - Bấm dòng có solid đã bị xoá (sau Undo): báo "Solid no longer exists — rebuild or re-run Build 3D.", không gọi highlight.
  - Summary trạng thái: đếm theo Kind (bỏ `KindOrder` cũ).
  - Copy/Export: header `Panel,Handle,Family,DetectedKind,Kind,Size,Flags,DirFlipped`. `DetectedKind` là Kind khi chưa có correction; dòng có correction ghi Kind mới ở cột `Kind`.
- [ ] **Step 4: Sniped + bỏ GB?.** `FlatBarBuilder` đặt `part.Sniped = true` ở nhánh vát. `GirderEndSplitter` bỏ dòng `p.Code = "GB?"`.
- [ ] **Step 5: Build và test.** Chạy `dotnet build -c Debug` (kỳ vọng `0 Error(s)`), rồi `dotnet test tests/MCG_3DPanel.Tests` (kỳ vọng pass).
- [ ] **Step 6: Kiểm trong AutoCAD.** Kỳ vọng:
  - Bảng theo họ, không có "GB?".
  - FB có `sniped`.
  - Đổi OB → IB: mã đổi và lip gấp ngược, panel tự rebuild, dòng vẫn được chọn.
  - ⇄ trên HP: bụng đảo, collar và lỗ CO đổi phía, bracket lân cận có thể đổi OB/IB.
  - Web → Web + GE-HP rồi về lại Web: 3D đổi đúng. Option GB bị mờ ở web không có mốc.
  - Review Focus 4 đạt.
  - Export CSV đúng cột.
- [ ] **Step 7: Cập nhật `SESSION_LOG.md` và commit**

```bash
git add UI/ Services/ SESSION_LOG.md
git commit -m "feat(result): family/kind table with dropdown, direction flip and auto rebuild"
```
