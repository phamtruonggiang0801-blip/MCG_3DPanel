# Điều khiển 3D ngay trên bản vẽ 2D — thiết kế

Ngày: 2026-11-20 · Trạng thái: chờ kỹ sư duyệt

## Mục đích và lý do

Người dùng dùng tool để **ra bản vẽ nhanh** (Plan View, Detail), không để xem 3D. Các quyết định trên tab Result / Naming
(Kind, ⇄, Snip, Tripping, Collar, tên) người dùng tự xác định được khi nhìn **Plan View trên bản vẽ 2D** — không cần nhìn 3D.
Hiện tại muốn sửa phải mở file `<panel>_3D.dwg`, sửa (mỗi lần sửa rebuild ngay), rồi Create Drawing để quay về 2D. Mục tiêu:
làm toàn bộ việc đó **từ bản vẽ 2D chính thức**, file 3D chỉ là chỗ chứa dữ liệu và dựng hình phía sau.

## Hiện trạng (liên quan)

- Bản vẽ 2D chính thức: tab Input (Build 3D / Update). Tab Result + Naming rỗng (`PanelSession.For` không thấy panel 3D).
- File `_3D.dwg`: tab Result + Naming đầy đủ; mọi sửa ghi `CorrectionStore` / `NameStore` (XRecord MCG3D_CORRECTIONS /
  MCG3D_NAMES trên định nghĩa block 2D trong file 3D) rồi `RequestRebuild` ngay.
- Khoá sửa đổi `{hex}#{n}` = handle entity 2D gốc; file 3D giữ nguyên handle của block 2D ⇒ cùng một khoá dùng được ở cả 2 file.
- Update từ 2D (`PanelCommands.UpdateFile` → `PrepareUpdate`): đọc corrections/names của file 3D cũ (`OldPanelData`), đổi khoá
  nếu block đã DC lại (`RemapCarry`), dựng file 3D mới mang theo chúng.
- Plan View vẽ chồng lên reference `_Assy` gốc trong 2D (XData MCG3D_PLANVIEW) ⇒ entity 2D gốc của part chính là hình người
  dùng thấy trên Plan View. Plan View hiện chạy từ file 3D đang mở (`PlanViewCommand.Create(doc3D)`).
- Detail (Plan/View/Section/Update) đã chạy trong bản vẽ 2D, đọc file 3D bằng `ReadDwgFile` (`Detail3DSource`), có kiểm
  fingerprint block 2D ↔ `PanelMeta.SourceFingerprint`.

## Mục tiêu

1. Đang ở bản vẽ 2D có `_Assy` + file 3D tương ứng: tab **Result** và **Naming** hiện đủ dòng của panel (đọc ngầm file 3D,
   không mở lên màn hình).
2. Mọi thao tác sửa trên 2 tab đó (Kind, ⇄, Snip, Tripping, Collar, Clear Correction; đặt / xoá / gộp / tách tên) làm được từ
   2D, **tức thì**: chỉ ghi vào danh sách **chờ** trong bản vẽ 2D, không rebuild.
3. Dòng có sửa chờ được đánh dấu rõ (giá trị mới + dấu "chờ Update"); thanh trạng thái báo số sửa đang chờ.
4. Pick / Zoom / chọn dòng ⇒ highlight **entity 2D gốc** của part (thấy ngay trên Plan View); Pick trên bản vẽ ⇒ chọn dòng.
5. Một nút **Update** (cách A — sửa nhiều chỗ rồi cập nhật một lần): dựng lại file 3D có áp các sửa chờ, vẽ lại Plan View,
   cập nhật các Detail đã có, xoá danh sách chờ, quay về bản vẽ 2D.

## Không làm (non-goals)

- Không bỏ điều khiển trong file 3D — mở file 3D vẫn sửa / rebuild như cũ.
- Không dựng 3D ngầm sau mỗi lần sửa (cách 2 — đã loại); không gộp 3D lại vào bản 2D (cách 3 — ngược quyết định tách file).
- Không hiện hình 3D trong bản vẽ 2D.
- Panel chưa có file 3D: vẫn phải Build 3D lần đầu như hiện tại.

## Anti-goals (làm vậy là hỏng dù chạy được)

- Sửa trên 2D bị mất âm thầm (đóng bản vẽ, Update lỗi, DC lại block) mà không báo.
- Sửa chờ áp nhầm sang part khác.
- Update để lại file 3D / Plan View / Detail lệch nhau mà không nói rõ cái nào chưa cập nhật.
- Nút hiện ra nhưng bấm không có tác dụng gì (ux-quality-bar).

## Phương án đã cân nhắc

1. **Sửa chờ ở 2D, Update một lần áp vào 3D** — CHỌN. Sửa tức thì; tái dùng Update + Plan View + Detail Update đang chạy ổn;
   giá: lúc Update file 3D được mở/đóng; dòng chờ cần đánh dấu.
2. Rebuild 3D ngầm sau mỗi sửa (database phụ) — loại: dựng 3D hiện gắn với document đang mở, viết lại lớn + rủi ro; vẫn phải
   bấm vẽ lại bản vẽ nên lợi ích thêm nhỏ.
3. Gộp 3D trở lại bản vẽ 2D — loại: ngược quyết định tách `<panel>_3D.dwg`.

Kỹ sư chọn: cách A (sửa nhiều rồi Update một lần) và phương án 1.

## Thiết kế

### Đọc dòng từ file 3D khi đang ở 2D

- Khi bản vẽ 2D active (DocumentBecameCurrent / mở palette / sau Update): với mỗi reference `*_Assy` trong Model Space tìm
  file 3D như Detail đang làm; đọc ngầm (`ReadDwgFile`, chỉ đọc) ra danh sách dòng Result + Naming và corrections/names đã lưu.
- Dữ liệu dòng là **ảnh chụp** (không giữ ObjectId của database phụ — database phụ đóng ngay sau khi đọc). Mọi tham chiếu tới
  part đi qua khoá `{hex}#{n}` ⇒ ánh xạ sang entity 2D gốc trong bản vẽ đang mở.
- Cache theo (đường dẫn file 3D, thời điểm sửa file); file 3D đổi ⇒ đọc lại.

### Kiểm tra block 2D còn khớp file 3D

- So fingerprint block 2D hiện tại với `PanelMeta.SourceFingerprint` (cơ chế của Detail).
- Lệch (block đã sửa / DC lại sau lần Update cuối): vẫn hiện dòng nhưng **khoá sửa**, thanh trạng thái: "The 2D panel changed
  since the last Update — press Update first." (khoá cũ không còn chắc trỏ đúng part).

### Danh sách sửa chờ

- Lưu trong bản vẽ 2D: XRecord `MCG3D_PENDING` trên định nghĩa block `_Assy` (đi theo bản vẽ khi lưu). Nội dung cùng định dạng
  JSON với corrections + names (thêm / ghi đè / xoá theo khoá), để gộp bằng chính các hàm đọc/ghi hiện có.
- Hiển thị = giá trị đã lưu trong file 3D **phủ** bởi sửa chờ. Clear Correction trên dòng có sửa chờ ⇒ bỏ sửa chờ trước.
- Bản vẽ 2D chưa lưu mà đóng: sửa chờ mất theo bản vẽ — hộp thoại lưu của AutoCAD đã hỏi; không thêm cảnh báo riêng.

### Update

Nút Update (tab Result, ở 2D; tab Input giữ nút Build/Update hiện có — dùng chung một luồng):
1. Luồng Update hiện có (`PrepareUpdate` → đổi khoá nếu DC → dựng file 3D mới).
2. **Sau** đổi khoá: phủ sửa chờ lên corrections/names mang sang (sửa chờ thắng khi trùng khoá). Khoá chờ không còn part
   ⇒ bỏ, đếm vào dòng báo ("n pending edits dropped because their part no longer exists").
3. Dựng 3D xong: vẽ lại Plan View (nếu panel đã có Plan View — dùng luồng Replace hiện có, kể cả hộp thoại phần vẽ tay), rồi
   Detail Update cho các Detail đã có của panel.
4. Thành công bước 1–2 ⇒ xoá `MCG3D_PENDING`. Bước 3 lỗi ⇒ sửa đã nằm trong file 3D, chỉ báo bước nào chưa cập nhật.
5. Kết thúc ở bản vẽ 2D; file 3D đóng lại nếu trước Update nó chưa mở.

### Highlight / Pick

- Chọn dòng / Zoom ⇒ highlight entity 2D gốc (theo khoá) trong reference `_Assy`; Pick trên bản vẽ 2D ⇒ entity được chọn ⇒
  khoá ⇒ dòng. Part không có entity 2D riêng (sinh ra: Collar, Tripping, GirderFlange…) ⇒ highlight các entity trong khoá của nó.

## Những gì đã quyết

- Mục đích: ra bản vẽ nhanh; quyết định dựa trên Plan View (kỹ sư 2026-11-20).
- Cách A: sửa nhiều chỗ, Update một lần (kỹ sư).
- Phương án 1 (kỹ sư).

## Quyết định của Claude (kỹ sư kiểm khi đọc)

- Sửa chờ lưu trong bản vẽ 2D (XRecord trên block `_Assy`), không lưu vào file 3D cho tới khi Update.
- Sửa chờ thắng giá trị trong file 3D khi trùng khoá; nếu người dùng vừa sửa trong file 3D vừa có sửa chờ, sửa chờ đè lên.
- Block 2D lệch file 3D ⇒ khoá sửa trên 2D tới khi Update.
- Update tự vẽ lại Plan View (nếu đã có) và Detail Update (nếu đã có Detail).
- File 3D tự đóng sau Update nếu trước đó chưa mở.
- Tab Input giữ nguyên; thêm Update trên tab Result khi ở 2D.

## Để người dựng tự quyết

- Cách Plan View chạy khi file 3D không active (mở file 3D tạm hay đọc snapshot từ database phụ).
- Kiểu đánh dấu dòng chờ (màu / ký hiệu) theo `PanelStyles.xaml` và skill ui-guidelines.
- Cấu trúc cache đọc file 3D.
