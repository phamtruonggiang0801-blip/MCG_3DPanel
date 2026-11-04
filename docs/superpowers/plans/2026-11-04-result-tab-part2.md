# Result tab Part 2 — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Sniped End bật/tắt theo từng đầu (HP + FB), Delete/Restore solid, correction lưu trong DWG, rebuild sau
khi mở lại bản vẽ (kèm nút Load 3D…), Export STEP mỗi panel một file.

**Architecture:** Mở rộng `PartCorrection` / `CorrectionStore` của Phần 1. Correction được tuần tự hoá JSON (thuần
C#, test được), ghi vào XRecord của **định nghĩa block 2D** bên trong lệnh rebuild/build (có document lock, nằm
trong cùng bước Undo). Block 3D mang một XRecord metadata (handle định nghĩa 2D, ma trận chèn, PanelInput,
PanelPosition), nên sau khi mở lại bản vẽ tool có thể chèn tạm block 2D để phân loại lại.

**Tech Stack:** C# / .NET 8, AutoCAD 2026 managed API (+ AutoCAD Mechanical `STEPOUT`), WPF, System.Text.Json, xUnit.

**Spec:** [docs/superpowers/specs/2026-11-04-result-tab-corrections-design.md](../specs/2026-11-04-result-tab-corrections-design.md), mục 11–15.

## Global Constraints

- Nhãn đầu: thanh có |dx| ≥ |dy| ⇒ `L` (X nhỏ) / `R`; ngược lại ⇒ `B` (Y nhỏ) / `T`. Lưu theo **lo/hi**, không
  theo thứ tự đỉnh.
- Bỏ vát ở đầu HP "End 30" ⇒ để vuông, không ép Bracket B.
- Delete chỉ xoá solid, không nhận diện lại. Dòng bị xoá vẫn hiện (gạch ngang).
- Correction gắn với **định nghĩa block 2D** (mọi lần chèn dùng chung).
- XRecord keys: `MCG3D_CORRECTIONS` (trên BTR 2D), `MCG3D_SOURCE` (trên BTR 3D). JSON chia khúc ≤ 250 ký tự, mỗi
  khúc là một TypedValue mã 1.
- Export STEP: mỗi panel một file `<PanelName>.stp`, chọn thư mục một lần. Không có `STEPOUT` ⇒ nút mờ, tooltip
  "Requires AutoCAD Mechanical (STEPOUT)".
- Verify: `dotnet build -c Debug` → `0 Error(s)`; `dotnet test tests/MCG_3DPanel.Tests` → all pass.

## Review Focus

1. **Undo một rebuild** cũng hoàn tác XRecord correction, trong khi bộ nhớ đệm vẫn giữ correction mới. Hành vi mong
   đợi: rebuild kế tiếp ghi lại đúng cache; bảng không crash. → checklist Task 10.
2. **Bản vẽ chỉ đọc / không ghi được XRecord:** correction vẫn áp trong phiên, thanh trạng thái báo
   "Corrections could not be saved in the drawing." → checklist Task 10.
3. **Mở lại bản vẽ rồi Load 3D… với block 3D cũ (build trước Phần 2, không có `MCG3D_SOURCE`):** báo
   "This 3D block has no source info — re-run Build 3D." và không xoá gì. → checklist Task 11.
4. **Thêm vát ở đầu HP đang có Bracket B:** Bracket B ở đầu đó bị bỏ (vát thay bracket, cùng quy tắc "End 30:
   bracket HOẶC vát"). → checklist Task 8.
5. **Export STEP khi có panel đã bị xoá hết solid:** bỏ qua panel đó, báo tên. → checklist Task 12.

---

### Task 8: Sniped End theo từng đầu

**Files:**
- Modify: `UI/ResultCatalog.cs` (thêm `EndLabels`), `tests/MCG_3DPanel.Tests/ResultCatalogTests.cs`
- Modify: `Services/Corrections/PartCorrection.cs` (`bool? SnipLo, SnipHi`)
- Modify: `Services/PanelPart.cs` (thay `bool Sniped` bằng `bool SnipLo, SnipHi`, tức thực tế đã vát)
- Create: `Services/Builders/SnipOverride.cs`: `static bool Decide(PanelPart p, Point3d end, bool auto, BuildContext ctx)`
  (tra correction lo/hi cho đầu `end`, trả override ?? auto) và `static bool IsLo(PanelPart p, Point3d end)`
- Modify: `HpBuilder.cs:89-90`, `FlatBarBuilder.cs:42-100` (dùng `snip1`/`snip2` thay `free && lengthOk`),
  `HpEndBracketBuilder.cs` (bỏ B ở đầu bị ép vát)
- Modify: `IPartBuilder.cs` (`BuildContext.Corrections`), `Build3DService.Build` (nhận corrections)
- Modify: `UI/ResultRow.cs`, `UI/PanelResultView.xaml(.cs)`, `Commands/EntityHighlighter.cs` (`ShowPoint(Point3d)` transient)

**Interfaces:**
- Produces:
  - `ResultCatalog.EndLabels(double dx, double dy) -> (string lo, string hi)`, trả `("L","R")` hoặc `("B","T")`.
  - `BuildContext.Corrections : IReadOnlyDictionary<string, PartCorrection>`.
  - `ResultRow`: `SnipLoText`, `SnipHiText`, `SnipLoOn`, `SnipHiOn`, `CanSnip`, `internal Point3d EndLo, EndHi`.

- [ ] Test: `EndLabels_Horizontal_LR` (`EndLabels(10,1) == ("L","R")`); `EndLabels_Vertical_BT`; `EndLabels_Diagonal45_LR` (`EndLabels(5,5) == ("L","R")`). Chạy test, kỳ vọng FAIL. Cài đặt, chạy lại, kỳ vọng PASS.
- [ ] Builder: vát của đầu = `SnipOverride.Decide(...)`. Ghi kết quả thực tế vào `part.SnipLo` / `part.SnipHi`. HpEndBracketBuilder bỏ qua đầu có override `true`.
- [ ] UI: cột "Sniped End" có 2 ToggleButton (`◀ L` / `R ▶`, hoặc `▼ B` / `T ▲`). Chỉ hiện với HP và FB.
  - Rê chuột lên nút ⇒ `EntityHighlighter.ShowPoint(end)`.
  - Bấm ⇒ `SnipLo` (hoặc `SnipHi`) = !trạng thái thực tế ⇒ lưu correction ⇒ rebuild.
  - Bấm lại để về đúng giá trị tự động ⇒ xoá override (lưu `null`). Lựa chọn thiết kế: giữ override cho tới khi người dùng Clear Correction.
- [ ] Build + test, rồi commit `feat(result): per-end sniped end toggles for HP and FB`.

### Task 9: Delete / Restore solid

**Files:** `Services/IBuild3DService.cs` (`BuiltSolid.DeleteKey`, `bool Deleted`), `Services/Build3DService.cs`,
`Services/Corrections/CorrectionStore.cs` (`HashSet<string> Deleted(panelKey)`, `SetDeleted(panelKey, key, bool)`),
`UI/ResultRow.cs`, `UI/PanelResultView.xaml(.cs)`.

- [ ] **DeleteKey:**
  - Solid của part = `part.SourceKey`.
  - Extra (Flange GE/GB) = `$"{part.SourceKey}/{x.Type}#{i}"`.
  - Collar = `$"CollarPlate:{tag}"`; Bracket B = `$"HpEndBracketB:{tag}"`.
- [ ] **Build3DService:** ngay trước khi chèn BlockReference, solid có `DeleteKey` nằm trong danh sách xoá ⇒ `Erase()`, `Deleted = true`, giữ trong `Solids`. Bounds lấy trước khi xoá.
- [ ] **Row bị xoá:** chữ xám, gạch ngang. Kind không sửa được. Bấm vào dòng chỉ báo "Deleted — right-click Restore."
- [ ] **Menu chuột phải:**
  - **Delete** (ẩn khi row.Deleted) ⇒ `SetDeleted(true)` ⇒ rebuild.
  - **Restore** (chỉ hiện khi row.Deleted) ⇒ `SetDeleted(false)` ⇒ rebuild.
- [ ] Build, rồi commit `feat(result): delete/restore a solid as a correction`.

### Task 10: Correction lưu trong DWG

**Files:**
- Create: `Services/Corrections/CorrectionJson.cs` (thuần, link vào tests): `string Serialize(IReadOnlyDictionary<string, PartCorrection> parts, IEnumerable<string> deleted)`, `void Deserialize(string json, out Dictionary<string, PartCorrection> parts, out HashSet<string> deleted)`
- Create: `Services/Corrections/CorrectionXRecord.cs`: `static string Read(Transaction tr, ObjectId btrId, string key)`, `static void Write(Transaction tr, ObjectId btrId, string key, string text)` (chia khúc 250 ký tự)
- Modify: `CorrectionStore` (khoá theo BTR 2D: `PanelKey(ObjectId btr2D)`; `EnsureLoaded(Transaction, btr2D)`; `Save(Transaction, btr2D)`)
- Modify: `IBuild3DService.cs` (`BuildResult.SourceBtrId`, `Matrix3d SourceTransform`), `Commands/PanelCommands.cs` (BuildOne đọc BTR 2D + transform, EnsureLoaded trước Classify; RebuildOne Save trước Build)
- Modify: `tests/.../MCG_3DPanel.Tests.csproj` (link 2 file thuần mới), `CorrectionJsonTests.cs`

- [ ] Test `RoundTrip_KindFlipSnipDeleted`: correction `{Kind="IB", FlipDir=true, OriginalKind="OB", SnipLo=false, SnipHi=null}`, cộng deleted `{"CollarPlate:a~b"}` ⇒ Serialize rồi Deserialize ⇒ giống hệt. Test `Deserialize_Empty_GivesEmpty` (`""` và `null`). Chạy, kỳ vọng FAIL. Cài đặt, kỳ vọng PASS.
- [ ] XRecord + nối vào BuildOne/RebuildOne. Ghi lỗi ⇒ trả thông điệp "Corrections could not be saved in the drawing.", ghép vào status của `Rebuilt`.
- [ ] Build + test, rồi commit `feat(result): persist corrections in the 2D block definition`.

### Task 11: Rebuild sau khi mở lại + Load 3D…

**Files:** `Services/Corrections/SourceMeta.cs` (POCO thuần + JSON: `SourceBtrHandle`, `double[16] Transform`,
`PanelInput Input`, `PanelPosition Position`), `Commands/PanelCommands.cs`, `Services/Build3DService.cs`
(ghi `MCG3D_SOURCE` lên BTR 3D), `UI/PanelResultView.xaml(.cs)` (nút "Load 3D…").

- [ ] Test `SourceMeta_RoundTrip` (thuần). FAIL, rồi PASS.
- [ ] **Build ghi `MCG3D_SOURCE` lên BTR 3D.** Lúc rebuild, nếu `SourceRefId` không mở được (sau khi mở lại bản vẽ), tool:
  - chèn tạm `BlockReference(SourceBtrId)` với `SourceTransform` vào Model Space;
  - phân loại và build;
  - xoá block tạm.

  Tất cả nằm trong lệnh rebuild.
- [ ] **Lệnh `MCG_Load3DPanel` (nút "Load 3D…"):**
  - Chọn các block `MCG3D_*`, đọc `MCG3D_SOURCE`, dựng `BuildResult` tạm (`BlockRefId`, `BtrId`, `BlockName`, meta), rồi gọi `RebuildOne` cho từng block. Như vậy bảng có đủ thông tin part và correction (lựa chọn thiết kế: Load = rebuild từ 2D, cùng kết quả).
  - Block thiếu meta ⇒ báo theo Review Focus 3.
- [ ] Build, rồi commit `feat(result): rebuild after reopen and Load 3D`.

### Task 12: Export STEP

**Files:** `Commands/StepExport.cs`, `UI/PanelResultView.xaml(.cs)` (nút "Export STEP…").

- [ ] **Kiểm lệnh:** `Autodesk.AutoCAD.Internal.Utils.IsCommandNameInUse("STEPOUT") != CommandTypeFlags.NoneCnt`. Không có ⇒ nút mờ, kèm tooltip.
- [ ] **Lệnh `MCG_ExportStep`:**
  - Mỗi panel còn solid: đặt `FILEDIA=0`, chọn block 3D (pickfirst), chạy `ed.Command("_.STEPOUT", ...)` với tên file `<dir>\<PanelName>.stp`, rồi khôi phục FILEDIA.
  - Chuỗi prompt cụ thể của `STEPOUT` phải thử trong AutoCAD Mechanical. Nếu STEPOUT không nhận block reference: explode tạm vào một bản sao, xuất xong thì xoá bản sao.
- [ ] **Status:** "Exported N STEP files to <dir>", kèm danh sách panel bị bỏ qua.
- [ ] Build, rồi commit `feat(result): export each panel to its own STEP file`.
