# CONTEXT — MCG_3DPanel

Tool AutoCAD 2023 (.NET 4.8, WPF): nhận 1 Block Panel (Plan View 2D) → nhận diện các
bộ phận → extrude lên 3D theo thông số người dùng → gom thành 1 Block 3D.

Quy tắc nhận diện / thông số / định vị: xem skill **`.claude/skills/panel-parts-reference`**
(nguồn chân lý, luôn đọc trước khi sửa logic phân loại/dựng 3D).

## Kiến trúc thư mục

| Thư mục | Nội dung |
|---|---|
| `Commands/` | `PanelCommands.cs` — IExtensionApplication + lệnh `MCG_Build3DPanel`, `MCG_InspectPanel` |
| `UI/` | `PaletteManager.cs` (Singleton PaletteSet), `PanelBuilderControl.xaml(.cs)` (WPF) |
| `Services/` | `IInspectorService`/`InspectorService` (duyệt đệ quy block → JSON). Sắp có `ClassificationService`, `Build3DService` |
| `Models/` | POCO (sắp có: `PartType`, `PanelPart`, `PanelInput`) |
| `Utilities/` | `Json.cs` — `JsonWriter` + `JsonObject` (serializer tối giản, không NuGet) |
| gốc | `MCG_3DPanel.csproj`, `PackageContents.xml`, `deploy.bat` |

## Môi trường

- AutoCAD 2023 tại `C:\Program Files\Autodesk\AutoCAD 2023\` (ref `AcMgd`, `AcDbMgd`, `AcCoreMgd`, Private=false).
- Deploy: `deploy.bat` → build → copy bundle vào `%APPDATA%\Autodesk\ApplicationPlugins\MCG_3DPanel.bundle`.
- Lệnh: `MCG_Build3DPanel` (mở palette), `MCG_InspectPanel` (pick → xuất JSON ra Desktop).

## Kế hoạch Phase

- **Phase 0 — Scaffold + Inspector** ✅ *(đã xong, chờ test trong AutoCAD)*: khung project, palette,
  nút Inspect duyệt đệ quy (kể cả nesting) → JSON. Mục đích: lấy dữ liệu thật để **xác nhận
  bảng nhận diện** trước khi dựng 3D.
- **Phase 1 — Nhận diện**: `Models` + `ClassificationService` áp bảng Layer/Color (skill reference).
- **Phase 2 — Dựng 3D + Preview**: `Build3DService` (Region→Extrude→Move) + preview.
- **Phase 3 — Assemble & Replace**: gom Block 3D, thay block 2D, layer `Mechanical-AM_0` ByLayer.
- **Phase 4 — Hoàn thiện**: xử lý lỗi, edge cases.

Backlog (park): HP bulb profile, Bracket (OB/IB/FB + mã dày), Girder End, web nhiều chiều cao,
FB nhiều quy cách — chi tiết trong skill `panel-parts-reference`.
