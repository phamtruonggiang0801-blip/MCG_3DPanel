---
name: ui-guidelines
description: Chuẩn thiết kế UI cho plugin AutoCAD của MCG (WPF PaletteSet). Đọc TRƯỚC khi tạo/sửa bất kỳ giao diện WPF, PaletteSet, hay control nào.
---

# UI Guidelines — Plugin AutoCAD MCG

Nguyên tắc thiết kế giao diện cho mọi plugin AutoCAD của team (WPF host trong PaletteSet).

## Nguyên tắc (thứ tự ưu tiên)

1. **Thân thiện với người dùng — QUAN TRỌNG NHẤT.** Người dùng hiểu ngay phải làm gì; các bước
   đánh số rõ (1, 2, 3); nút hành động chính nổi bật; có dòng trạng thái phản hồi sau mỗi thao tác;
   thông báo lỗi bằng ngôn ngữ dễ hiểu, không phải mã lỗi thô.
2. **Đơn giản.** Chỉ hiển thị thứ cần thiết. Không nhồi nhét tuỳ chọn. Gom nhóm hợp lý.
3. **Ngôn ngữ: TIẾNG ANH** cho toàn bộ text trên UI (label, button, tooltip, status).
   (Comment code có thể tiếng Việt; text hiển thị cho người dùng luôn tiếng Anh.)
4. **Đồng bộ ngoại quan với MCG_FittingManagement** (theme SÁNG — thay cho theme tối trước đây):
   - Mọi style nằm ở `UI/PanelStyles.xaml` — bản sao CÓ CHỦ Ý của
     `MCG_FittingManagement/Views/FittingManagement/FittingStyles.xaml`, giữ nguyên tên key + giá trị.
     KHÔNG viết style inline trong view; cần style mới thì thêm vào cuối PanelStyles.xaml.
   - View: `Background="#FFF2F2F2"`, `FontSize="11"`, `Grid Margin="4"`, không header tiêu đề riêng.
   - GroupBox tiêu đề VIẾT HOA (`STEP 1 — ...`). Mỗi bước chỉ 1 nút `PrimaryButtonStyle` (xanh #005E9E),
     nút phụ để xám mặc định. Chữ nút ngắn, giải thích đưa vào `ToolTip`.
   - Tham số: lưới 2 cột nhãn (`ParamLabelStyle`) | ô nhập (cao 24).
   - Trạng thái: `StatusTextStyle` dưới đáy MỖI tab, set qua `StatusText.Set` (xanh #005E9E / lỗi #C00000).
   - DataGrid: `PanelDataGridStyle` + ContextMenu + cột nút ⌖ zoom; dòng user sửa → chữ #0050C8 SemiBold.
   - Control TRONG ô DataGrid: style `CellButton` / `CellToggle` / `CellCombo` + `ResultCellStyle` (cao 20, margin 0).

**HÀNH VI (đa drawing, không lỗi im lặng, checklist trước khi báo xong): BẮT BUỘC đọc [[ux-guardrails]].**

## PaletteSet — quy tắc bắt buộc

- **Luôn cố định ở CẠNH TRÁI** (`Dock = DockSides.Left`).
- **KHÔNG dockable tự do**: giới hạn `DockEnabled = DockSides.Left` (không cho dock phải/trên/dưới).
  Bỏ style `ShowAutoHideButton`; giữ tối giản (chỉ `ShowCloseButton`).
- Mỗi màn hình lớn là 1 **tab gốc của PaletteSet** (`AddVisual`), KHÔNG lồng `TabControl` WPF;
  chuyển tab bằng `PaletteSet.Activate(index)`.
- Đặt **Guid cố định** cho PaletteSet để AutoCAD nhớ vị trí/kích thước giữa các phiên.
- Đặt bề rộng hợp lý qua `MinimumSize` (vd rộng ~320).

## Tương tác với AutoCAD từ nút Palette

- **KHÔNG dùng lệnh (`CommandMethod`) ẩn cho nút UI** — gọi thẳng logic từ event handler.
- Pick entity (`Editor.GetEntity`) gọi trực tiếp trong handler (AutoCAD ở trạng thái quiescent → OK).
- Mọi thao tác **ghi database** phải bọc trong `using (doc.LockDocument())` (tránh eLockViolation).
- Sau thao tác luôn cập nhật dòng **status** trên UI để người dùng biết kết quả.

## Mẫu áp dụng (tham khảo)

`UI/PaletteManager.cs` (dock trái, non-dockable, 2 tab gốc Input/Result) + `UI/PanelInputView.xaml`,
`UI/PanelResultView.xaml`, `UI/PanelStyles.xaml` trong project MCG_3DPanel.
