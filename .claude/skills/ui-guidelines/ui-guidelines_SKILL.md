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
4. **Phù hợp AutoCAD.** Màu sắc, kích thước, font hoà hợp với môi trường AutoCAD (nền tối):
   - Nền tối (`#2B2B2B`–`#333333`), chữ sáng (`#EAEAEA`), accent xanh AutoCAD (`#3C6EB4`).
   - Font `Segoe UI`, cỡ 12–13; tiêu đề 15–16.
   - Nút cao vừa phải (padding ~8×6), bo góc nhẹ (4px).

## PaletteSet — quy tắc bắt buộc

- **Luôn cố định ở CẠNH TRÁI** (`Dock = DockSides.Left`).
- **KHÔNG dockable tự do**: giới hạn `DockEnabled = DockSides.Left` (không cho dock phải/trên/dưới).
  Bỏ style `ShowAutoHideButton`; giữ tối giản (chỉ `ShowCloseButton`).
- Đặt **Guid cố định** cho PaletteSet để AutoCAD nhớ vị trí/kích thước giữa các phiên.
- Đặt bề rộng hợp lý qua `MinimumSize` (vd rộng ~320).

## Tương tác với AutoCAD từ nút Palette

- **KHÔNG dùng lệnh (`CommandMethod`) ẩn cho nút UI** — gọi thẳng logic từ event handler.
- Pick entity (`Editor.GetEntity`) gọi trực tiếp trong handler (AutoCAD ở trạng thái quiescent → OK).
- Mọi thao tác **ghi database** phải bọc trong `using (doc.LockDocument())` (tránh eLockViolation).
- Sau thao tác luôn cập nhật dòng **status** trên UI để người dùng biết kết quả.

## Mẫu áp dụng (tham khảo)

`UI/PaletteManager.cs` (dock trái, non-dockable) + `UI/PanelBuilderControl.xaml` (các bước đánh số,
tiếng Anh, màu AutoCAD, status line) trong project MCG_3DPanel.
