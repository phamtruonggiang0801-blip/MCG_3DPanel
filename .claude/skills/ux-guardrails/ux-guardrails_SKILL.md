---
name: ux-guardrails
description: Bắt buộc khi sửa UI (palette, tab Result, highlight/zoom, correction), code giữ ObjectId/Document/Database, hoặc thêm catch trong builder/command — 3 luật chặn lớp lỗi UX (đa drawing, lỗi im lặng, style ô grid) + chạy docs/ux-checklist.md trước khi báo xong.
---

# UX guardrails — chặn LỚP lỗi, không vá từng chỗ

Kỹ sư (2026-11-05): lỗi UX nhỏ làm người dùng đánh giá thấp sản phẩm ⇒ mọi nhánh chạm UI phải qua 3 luật dưới +
`docs/ux-checklist.md`. Bổ sung cho [[ui-guidelines]] (ngoại quan) — skill này về HÀNH VI.

## Luật 1 — Mọi ObjectId thuộc 1 drawing

AutoCAD có NHIỀU drawing mở cùng lúc; palette + static là DÙNG CHUNG.
- Trước khi mở/sửa/highlight 1 `ObjectId`: kiểm `id.Database == MdiActiveDocument.Database`. Khác ⇒ KHÔNG đụng,
  báo người dùng ("belongs to another drawing — switch…").
- State static giữ ObjectId (vd `EntityHighlighter._last`) phải: lưu kèm `Database` LÚC GHI (không đọc lại `id.Database`
  sau này — Database đã free ⇒ crash), lấy+xoá state TRƯỚC khi LockDocument, dọn trên ĐÚNG drawing chủ (`FindDocument(db)`),
  luôn xoá kể cả khi lỗi, và được dọn ở `DocumentToBeDeactivated` / `DocumentToBeDestroyed`.
- `UI/PaletteManager.cs` đã nối: `DocumentToBeDeactivated` (tắt highlight/marker), `DocumentBecameCurrent`
  (`PanelResultView.OnDocumentActivated` — báo bảng thuộc drawing khác), `DocumentToBeDestroyed` (bỏ build).
- Mẫu đã đúng: `PanelCommands.RequestRebuild`, `StepExport.Request` (lọc theo `Database`).

⚠️ Bài học: build 716C (drawing A) → drawing mới build 716P ⇒ `_last` (id của A) mở bằng transaction của B
⇒ eWrongDatabase ⇒ catch nuốt ⇒ highlight/zoom chết vĩnh viễn, không báo gì.

## Luật 2 — Không lỗi im lặng trên đường người dùng thao tác

- `catch` chỉ `Debug.WriteLine` CHỈ được ở đường dọn dẹp/hover (ClearPoint, Clear, marker). Mọi thao tác người
  dùng BẤM (highlight, zoom, export, mở file, rebuild) ⇒ trả thông điệp và hiện trên thanh trạng thái (`SetStatus(...,
  error: true)`) hoặc `ed.WriteMessage`.
  - Mẫu: `EntityHighlighter.Show` trả `string` (null = OK); `ReportOutput.OpenFile` trả lý do lỗi.
- Builder: lỗi làm MẤT hình học (khoét, collar, bracket, cắt HP, part hỏng) ⇒ `ctx.Warn(msg)` (vào Report +
  `Warnings`), KHÔNG `ctx.Report.Add`. Tab Result hiện "⚠ N 2D part(s) not built (…) · M build warning(s)";
  JSON có `notBuilt` + `warnings`.
  - KHÔNG đếm đôi: part đã thành `notBuilt` (Built=false) thì catch của nó ghi `ctx.Report`, không Warn. Lỗi KHÔNG mất
    hình học (vd GE merge giữ 2 solid) ⇒ `ctx.Report`. Part `Unknown` không tính là lỗi.
  - Rebuild có lỗi ⇒ tự ghi report riêng (`PanelCommands.RebuildOne`) để "see …json" trỏ đúng file.
- Thông điệp: tiếng Anh, nói người dùng PHẢI LÀM GÌ, không mã lỗi thô.

## Luật 3 — Control trong ô DataGrid dùng style Cell*

Style chung (`Button` cao 26 + margin dưới 5, `ComboBox` 24 + margin 4) phá layout trong ô (nút lệch cao, combo bị
bóp mất mũi tên). Trong ô grid LUÔN dùng `CellButton` / `CellToggle` / `CellCombo` + `ResultCellStyle`
(`UI/PanelStyles.xaml`): cao 20, margin 0, căn giữa dọc. Không đặt Height/Margin rời rạc từng control.

## Quy trình mỗi nhánh chạm UI / ObjectId / catch

1. Plan: Review Focus phải có các ca đa drawing + lỗi im lặng liên quan.
2. Trước khi báo xong: đi từng dòng `docs/ux-checklist.md` trên code ⇒ ✔ / ✘ (sửa) / 🔍 (chỉ thấy trong AutoCAD);
   liệt kê 🔍 trong tin nhắn cuối để kỹ sư chạy.
3. Prompt reviewer cuối: kèm đường dẫn checklist, yêu cầu kiểm các dòng diff chạm tới.
