---
name: structure-naming
description: Tab Naming — tên kết cấu tự sinh theo tuyến (Web T#/L#, HP HT#/HL#, Flat Bar "BS"), người dùng sửa được, lưu XRecord MCG3D_NAMES trên block 2D. Đọc khi sửa quy tắc gom tuyến / đánh số / lưu tên, hoặc khi dùng tên cho bản vẽ 2D.
---

# Structure naming (tab Naming, kỹ sư chốt 2026-11-05)

Nguồn: `CAS.dump.json` panel 7D-16C — người vẽ ghi Text cạnh mặt cắt `7D-16C_X-X` / `7D-16C_Y-Y` (tỉ lệ 1:1 với plan):

| Nhãn | Chi tiết | Chạy theo | Đếm |
|---|---|---|---|
| T1…T7 (+T6a) | Web AM_0 | Y | trái → phải |
| L1…L4 | Web | X | dưới → lên |
| HT1, HT2 | HP (AM_3, màu 30) | Y | trái → phải |
| HL1…HL18 | HP | X | dưới → lên |
| BS | Flat Bar | bất kỳ | không đánh số |

## Quy tắc (code: `Naming/StructureNaming.cs`, thuần C#, test `StructureNamingTests`)
- Thành viên: Web + GirderEnd = Web; HP; FlatBar = FB. Loại khác (Bracket, Flange, Closed Plate…) **không đặt tên** (để trống).
  Web ngắn ở mép thường đã là Bracket nên tự rơi ra ngoài.
- Hình học trong **hệ block 2D** (nghịch đảo block transform) — dời/xoay block không đổi tên.
- Hướng = trục gần trục dài nhất (đoạn chéo theo trục gần nhất). Web + HP cùng hướng, tâm lệch ≤ **15 mm** = 1 tuyến
  (không cần liền). Loại tuyến = loại có **tổng dài lớn hơn** (HP 1B2E9 nằm trên L3 ⇒ tên L3). Tâm tuyến = tâm đoạn dài nhất.
- FB chỉ gộp với FB, tâm ≤ 15 **và** khe ≤ **30 mm** (FB tách tại HP vẫn là 1 dòng). Tên luôn "BS".
- Theo hướng tàu (vị trí panel): T/HT lái → mũi (mũi ở X+ ⇒ X tăng); L/HL tâm tàu → mạn: Center/Port Y tăng,
  Starboard Y giảm. 4 bộ đếm riêng.

## Lưu tên
- Chỉ lưu tên người dùng gõ, theo SourceKey từng đoạn, JSON (`NamesJson`) trong XRecord **`MCG3D_NAMES`** của BTR 2D
  (`NameStore`, cache theo `CorrectionStore.PanelKey`). Đổi tên tuyến ⇒ ghi mọi đoạn. Trống ⇒ về tên tự sinh.
- Tên tự sinh tính lại mỗi lần nạp ⇒ thêm 1 web giữa chừng làm số dịch, tên gõ tay giữ nguyên.
- Nhiều tên khác nhau trên 1 tuyến ⇒ tên nhiều đoạn nhất. Đổi tên KHÔNG rebuild.

## Giới hạn đã biết
- Tuyến chèn thêm kiểu **T6a** không suy được (ra T7, T7 ra T8) ⇒ người dùng sửa tay.
- Đoạn HP/Web ngắn lạc (HP góc panel) tạo tuyến riêng và **làm dịch số** — chưa lọc theo chiều dài (chờ kỹ sư quyết sau
  khi test thật).

## UI (`UI/PanelNamingView`)
- Select panel… = chỉ phân loại 2D (`PanelCommands.ClassifyOnly`); sau Build/rebuild/Load 3D tab tự nạp (`BuildResult.ClassifiedParts`).
- Highlight: solid 3D nếu panel đã Keep, polyline 2D nếu chưa/bỏ 3D. Enter = đổi tên cả vùng chọn; rời ô = chỉ dòng đó.
- ux-guardrails: dòng của drawing khác không ghi gì và báo tên drawing; checklist mục G.
