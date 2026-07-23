---
name: module-per-object
description: Quy tắc MODULE HÓA theo đối tượng cho MCG_3DPanel — mỗi bộ phận panel (TopPlate/Web/Flange/HP/Bracket…) và mỗi bước pipeline nằm ở 1 file riêng, service chỉ điều phối. Đọc TRƯỚC khi thêm loại đối tượng mới, thêm bước xử lý, hoặc khi 1 file phình > ~250 dòng.
---

# Module hóa theo đối tượng — MCG_3DPanel

## 1. Nguyên tắc

> **Service = ĐIỀU PHỐI. File con = 1 đối tượng / 1 bước. Không trộn.**

| Quy tắc | Ý nghĩa |
|---|---|
| **1 đối tượng = 1 file** | Thêm loại mới (vd Girder End) ⇒ thêm 1 file builder, **không sửa** file loại khác. |
| **Service không chứa hình học** | `Build3DService`/`ClassificationService` chỉ mở transaction, duyệt, chọn module, gom kết quả. |
| **Tiện ích dùng chung ra file riêng** | `GeometryUtil`, `ClassifyUtil` — tránh copy-paste giữa các module. |
| **Hằng số quy ước ở 1 nơi** | Layer/màu/dung sai trong `ClassifyUtil`; đổi quy ước bản vẽ chỉ sửa 1 chỗ. |
| **Fallback có trật tự** | Module chuyên biệt trước, module tổng quát cuối → hỏng 1 loại KHÔNG vỡ cả bản dựng. |
| **Report do module tự ghi** | Mỗi module thêm dòng vào `BuildContext.Report`; nơi gọi quyết định có xuất file không. |
| **Ngưỡng tách** | File > **~250 dòng** hoặc có **≥2 nhóm trách nhiệm** ⇒ tách. |

## 2. Cấu trúc hiện tại

```
Services/
  Build3DService.cs          ĐIỀU PHỐI dựng 3D (126 dòng)
  Builders/
    IPartBuilder.cs          hợp đồng + BuildContext (transaction, cls, input, profile, report)
    GeometryUtil.cs          tiện ích chung (DistToBox, MinDistToWeb, CountLumps, RegionFrom…)
    PlateBuilder.cs          TopPlate · Web · Flange · FacePlate · FlatBar  (+ fallback prism)
    HpBuilder.cs             HP — sweep tiết diện bulb thật theo BellyDir
    BracketBuilder.cs        Bracket knee OB/IB/FB — outline + bend lip + đặt mặt đứng

  ClassificationService.cs   ĐIỀU PHỐI phân loại (90 dòng)
  Classify/
    ClassifyUtil.cs          hằng số layer/màu/dung sai + DistToBox/Adjacent
    PartCollector.cs         B1: đọc DWG (đệ quy block) → PanelPart (hình học WCS)
    WebBracketSplitter.cs    B2: AM_0 → Web chính / Bracket / web-tier
    BracketCoder.cs          B3: mã OB/IB/FB + đóng dấu BellyDir lên HP

  ProfileService.cs          biên dạng Bracket knee (tham số)
  HpProfileService.cs        tiết diện HP bulb + bảng tra

Commands/
  PanelCommands.cs           vòng đời + điều phối Inspect/Classify/Build
  DrawingDumpCommand.cs      lệnh chẩn đoán MCG_DumpDrawing
  ReportOutput.cs            xuất file: đường dẫn, mở file, report JSON/TXT
```

**Kết quả tách (2026-07-23):** `Build3DService` **551 → 126**; `ClassificationService` **405 → 90**;
`PanelCommands` **377 → ~165**. Không đổi hành vi (build xanh sau mỗi bước).

## 3. Thêm 1 LOẠI ĐỐI TƯỢNG mới (vd Girder End)

1. Thêm giá trị vào `Models/PartType.cs`.
2. `Classify/`: nhận diện trong `ClassificationService` (định tuyến layer) hoặc thêm bước riêng nếu phức tạp.
3. `Builders/GirderEndBuilder.cs`: implement `IPartBuilder`
   (`Name`, `CanBuild(part)`, `Build(ctx, part)` → `Solid3d` hoặc `null`).
4. Đăng ký vào danh sách `_builders` của `Build3DService` — **đặt TRƯỚC `PlateBuilder`** (chuyên biệt trước).
5. Hình học tham số phức tạp ⇒ tách tiếp thành `XxxProfileService` (như `ProfileService` / `HpProfileService`).

**KHÔNG** thêm `if (type == …)` vào `Build3DService`.

## 4. Thêm 1 BƯỚC xử lý mới

Thêm file trong `Classify/` (hoặc thư mục pipeline tương ứng) rồi gọi từ service theo đúng thứ tự.
Mỗi bước: **input rõ ràng → mutate `ClassificationResult` → không biết bước khác tồn tại**.

## 5. Lợi ích khi debug

- Lỗi hình học 1 loại ⇒ mở đúng 1 file, không lội 500 dòng.
- Fallback: builder trả `null` ⇒ `Dispatch` ghi `[fallback]` vào report và thử builder kế
  (vd HP thiếu `BellyDir` ⇒ vẫn ra hộp chữ nhật thay vì mất hẳn).
- Bật report: bỏ comment `ReportOutput.WriteBuildReport(build, ed)` trong `PanelCommands.RunBuild`.
- Verify hình học **ngoài AutoCAD**: viết console app trong scratchpad mô phỏng công thức, so với dump bản vẽ
  (đã dùng cho toe R30, tiết diện HP, quy tắc bụng HP).

## 6. Checklist review

- [ ] File mới < ~250 dòng, chỉ 1 trách nhiệm?
- [ ] Service điều phối có bị chèn hình học không?
- [ ] Logic lặp ≥2 nơi ⇒ đã đưa vào `*Util` chưa?
- [ ] Hằng số quy ước bản vẽ có nằm trong `ClassifyUtil` không (không rải rác)?
- [ ] Builder mới đăng ký ĐÚNG THỨ TỰ ưu tiên chưa?
- [ ] Có đường fallback khi module mới thất bại chưa?
- [ ] Build xanh sau MỖI bước tách (không gộp nhiều thay đổi rồi mới build)?

Liên quan: [[panel-parts-reference]] · [[bracket-identification]] · [[parametric-profiles]] · [[hp-profile]]
