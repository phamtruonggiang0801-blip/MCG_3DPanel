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
- Hướng = trục gần trục dài nhất. Đoạn **xiên** (lệch > **1°** so với cả 0°, 90°, 180°) gộp theo SEED: song song trong 1° + khoảng cách vuông góc ≤ **15 mm** tới đường thẳng của đoạn seed (dài nhất) ⇒ 1 tuyến xiên (không cần liền); không gộp với đoạn thẳng. Web + HP cùng hướng, tâm lệch ≤ **15 mm** = 1 tuyến
  (không cần liền). Loại tuyến = loại có **tổng dài lớn hơn** (HP 1B2E9 nằm trên L3 ⇒ tên L3). Tâm tuyến = tâm đoạn dài nhất; riêng tuyến toàn đoạn xiên = trung điểm phạm vi cả tuyến (đánh số theo tâm cả tuyến).
- FB chỉ gộp với FB, tâm ≤ 15 (cách xa vẫn 1 dòng — revision 1). Tên luôn "BS".
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
- Tab tự nạp theo file 3D đang active (nút Select panel… đã bỏ, xem mục cuối) và sau Build/rebuild (`BuildResult.ClassifiedParts`).
- Highlight: solid 3D của file 3D đang mở (không còn Keep; xem mục "Build 3D tách file" cuối skill). Enter = đổi tên cả vùng chọn; rời ô = chỉ dòng đó.
- ux-guardrails: dòng của drawing khác không ghi gì và báo tên drawing; checklist mục G.

## Revision 1 (kỹ sư 2026-11-05)
- Tên hiển thị/xuất = `<tag> <quy cách>` của đoạn DÀI NHẤT: `T1 WEB 374x6.0/FLANGE 100x20.0` (web cao Input × dày 2D;
  flange rộng 2D × dày Input, flange = Flange AM_11 có tâm trong nửa bề rộng + 15 quanh đường tâm web), `HL1 HP 120x6.0`
  (HP tab Input), `BS FB 80x6.0` (FB cao riêng/Input × dày 2D). Người dùng chỉ sửa tag (`StripSpec` cắt từ token `WEB|HP|FB <số>`). Panel Select panel lấy lại quy cách theo tab Input mỗi lần mở tab Naming.
- Grid chỉ Panel / Name / Kind; Zoom qua chuột phải.
- **Del Name**: ẩn tuyến + đánh số lại; lưu `Deleted` (SourceKey MỌI đoạn) trong `MCG3D_NAMES`; đoạn đã xoá bị BỎ TRƯỚC khi gom
  tuyến (đoạn xoá nhập vào tuyến khác không ẩn tuyến đó). "Restore deleted names (N)" trả lại mọi tuyến của các panel thuộc drawing đang mở. Đây cũng là cách xử lý
  đoạn ngắn lạc làm dịch số.
- BS/HP thẳng hàng cách xa vẫn 1 dòng (bỏ luật khe 30 mm của FB).

## Revision 2 (kỹ sư 2026-11-05)
- **Name** (sửa được, chỉ tag) và **Size** (chỉ đọc, theo đối tượng) là 2 cột riêng; Copy cũng 2 cột.
- Highlight Web kèm **mọi flange** của tuyến (`NamingLine.Flanges`). Flange thẳng: cùng phương, tâm trong nửa bề rộng + 15 quanh
  1 đoạn web thẳng và chồng trục. Flange xiên: **song song** (1°) với web xiên, khoảng cách vuông góc từ trung điểm tới đường web ≤ nửa bề rộng + 15, chồng theo trục web.
  Flange xiên không bao giờ gắn web thẳng và ngược lại; tấm nối (junction plate) đi theo tuyến song song của nó. Quy cách FLANGE ưu tiên flange trên đoạn web dài nhất.
- **Merge**: các dòng CÙNG loại + CÙNG phương trong 1 panel ⇒ 1 tuyến, 1 tên, 1 số (đánh số lại); lưu `Merges` (nhóm
  SourceKey) trong `MCG3D_NAMES`; nhóm chạm nhau thì nhập. **Unmerge** TÁCH dòng tự động: khoá tách lưu `Splits` trong `MCG3D_NAMES`, dòng ra khỏi nhóm tự động (và nhóm Merge chứa nó). **Reset to auto** trên 1 dòng đã tách nhập lại CẢ tuyến tự động chứa nó (`StructureNaming.AutoLineKeys` → xoá `Splits` của mọi key) và bỏ nhóm Merge chạm các key đó; Reset trên dòng Merge KHÔNG có key Split thì giữ nguyên như cũ. **Unmerge** luôn gỡ nhóm Merge còn sót trước khi Split (không để dòng gộp quay lại). **Merge** nhập lại các dòng đã tách. Kind hiện "Web (merged)".

## 2026-11-05 (sau revision 2)
- Bỏ nút **Select panel…** (và đường phân loại-2D-only `ClassifyOnly`/`FromBlock`), nút **Copy** trên thanh công cụ
  (Copy vẫn ở chuột phải + Ctrl+C) và ô **Position**: tab chỉ nạp panel từ Build 3D / rebuild, đánh số theo
  Position của **tab Input** (`PositionProvider`); mở tab Naming sau khi đổi Position ⇒ đánh số lại (`OnActivated`).

## Build 3D tách file (kỹ sư 2026-11-06, nhánh `feat/separate-3d-drawing`)
- **Build 3D** tạo file riêng `<tên block panel>_3D.dwg` cạnh file nguồn (`Commands/Panel3DFile.cs`, `Panel3DPath.cs`):
  đọc file nguồn ĐÃ LƯU từ đĩa bằng `Database.ReadDwgFile` (giữ handle; `Wblock()` làm mất hết, spike 3631/3631 so với 0),
  giữ block 2D của panel đó trên layer đóng băng/khoá `MCG3D_2D_SOURCE`, lưu rồi mở làm file làm việc và dựng ở đó.
  File nguồn không bị ghi. Nguồn chưa lưu / có thay đổi ⇒ dừng với `Save this drawing first, then Build 3D.`
- `SourceKey` = `{handle 2D}#{i}` nên tên, Merge/Split/Deleted và correction đi theo file; mọi thứ nằm trong XRecord
  trên block (`MCG3D_NAMES`, correction, và `MCG3D_PANEL` = Input, Position, handle solid/loại/khoá — `PanelMetaJson`).
- Mở file 3D (hoặc palette mở) ⇒ tab Result/Naming tự nạp từ `MCG3D_PANEL` + phân loại chỉ-đọc, KHÔNG ghi gì, đóng file
  không hỏi Save. Solid bị xoá tay ⇒ dòng bị bỏ, rebuild đem lại. Block 3D cũ không có `MCG3D_PANEL` ⇒ không liệt kê.
- File đích đã có (kể cả đang mở) ⇒ KHÔNG ghi đè: một hộp thoại Open / New copy (`_3D_2.dwg`…) / Cancel. Panel không ra
  solid ⇒ xoá file. Nhiều panel ⇒ mỗi panel một file, panel lỗi không dừng các panel khác.
- Trong file 3D, nút Input thành **Rebuild 3D**: áp ô nhập lên panel qua rebuild thường, giữ correction và tên.
- Đã xác nhận bằng spike trong AutoCAD: handle giữ nguyên, mở file từ palette và dựng tiếp được, phân loại đọc được
  tham chiếu trên layer đóng băng (332/332). Luồng Build đầy đủ CHƯA chạy end-to-end trong AutoCAD.
