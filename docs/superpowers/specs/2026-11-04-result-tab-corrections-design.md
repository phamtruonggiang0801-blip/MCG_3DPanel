# Tab Result: phân họ kết cấu + sửa chủng loại / chiều đổ + Rebuild

Ngày: 2026-11-04 · Trạng thái: chờ duyệt

## 1. Mục đích

Tab Result hiện chỉ liệt kê solid với nhãn kỹ thuật (`web8`, `OB12`, `GE-HP`…) và một ô "Correct type"
gõ tự do. Giá trị đó chỉ ra CSV, không ảnh hưởng gì tới 3D. Kỹ sư cần:

1. Đọc bảng theo đúng **họ kết cấu** quen dùng (Topplate / Web / Flange / Bracket / HP / FB / Collar Plate).
2. **Sửa chủng loại** trong vài trường hợp đặc biệt khi tool nhận sai.
3. **Sửa chiều đổ** của HP (hướng bụng) và Bracket (hướng gấp lip).
4. **Dựng lại 3D** theo các correction đó.

## 2. Mục tiêu / Không làm / Thất bại nếu

- **Mục tiêu:**
  - (Phần 0) sửa nhận diện mốc toe GB và chi tiết đi kèm trong vùng GB (mục 8).
  - Bảng theo họ; Kind là dropdown; nút đảo chiều cho HP và Bracket.
  - Sửa xong là tự build lại cả panel với correction (mục 9).
  - Export/Copy ghi đủ cột mới.
- **Không làm:**
  - Sửa chiều đổ cho Web/FB/Flange.
  - Hiện "GB?" (bỏ hẳn: không làm Kind, không làm cờ).
  - Hiện CutOut.
  - Lưu correction qua phiên làm việc (đóng bản vẽ là mất).
- **Thất bại nếu:**
  - Rebuild chỉ sửa đúng một solid mà làm lệch các chi tiết phụ thuộc (collar plate, lỗ HP trên web, bracket B,
    mã OB/IB của bracket bên cạnh).
  - Correction bị mất sau một lần rebuild.

## 3. Danh mục họ → Kind

| Family   | Kind có thể chọn (dropdown)                                         | Nguồn hiện tại                               | Size / Info              |
|----------|---------------------------------------------------------------------|----------------------------------------------|--------------------------|
| Topplate | Topplate (cố định)                                                  | `PartType.TopPlate`                          | `t…`                     |
| Web      | Web · Web + GE-HP · Web + GE-FB · Web + GB-HP · Web + GB-FB · GE-HP · GE-FB · Closed Plate | `Web`, Web có `MergedCodes`, `GirderEnd` mã GE, `ClosedPlate` | `t…` |
| Flange   | Main Flange · Flange GE/GB-HP · Flange GE/GB-FB · Faceplate          | `Flange`, solid `GirderFlange`, `FacePlate`   | `b*t`                    |
| Bracket  | OB · IB · BF · GB · B · Backing Plate                               | `Bracket`, `GirderEnd` mã GB đứng riêng, `HpEndBracketB`, `BackingPlate` | `t…` |
| HP       | HP (cố định)                                                        | `HP`                                         | `h*t`                    |
| FB       | FB (cố định)                                                        | `FlatBar`                                    | `b*t` + cờ `sniped`      |
| Collar   | Collar Plate (cố định)                                              | solid `CollarPlate`                          | `t…`                     |

Dropdown chỉ cho đổi **trong cùng họ** (vd không cho đổi HP → Web). Đây là lựa chọn thiết kế của Claude:
đổi sang loại khác hẳn hình học sẽ cho ra 3D vô nghĩa.

GE đứng riêng xếp họ Web, GB đứng riêng xếp họ Bracket. Cách xếp này theo đúng danh sách kỹ sư đưa ra.

## 4. Bảng mới

```
Family | Kind (dropdown)  | Size/Info | Chiều đổ | Flags  | ⌖
Web    | Web + GE-HP      | t8        |          |        |
Bracket| OB               | t12       | ⇄ [+]    |        |
HP     | HP               | 200*10    | ⇄ [+X]   |        |
FB     | FB               | 100*10    |          | sniped |
```

- Cột Panel và Handle giữ như hiện nay (Panel chỉ hiện khi có nhiều panel).
- Bỏ cột "Correct type".
- Dòng có correction (Kind khác Kind gốc, hoặc đã đảo chiều) → chữ xanh SemiBold, như hiện nay.
- Menu chuột phải "Clear Correction" xoá cả Kind lẫn chiều đổ của dòng.
- Thanh trạng thái đếm theo họ / Kind.
- Thanh công cụ: `Export CSV` · `Copy`. Không có nút Rebuild: sửa xong là tự rebuild (mục 9).

## 5. Chiều đổ

- **HP:** nút ⇄ đảo dấu `PanelPart.BellyDir`. Phải áp **ngay sau khi BracketCoder tính BellyDir**, để mọi thứ
  dùng hướng bụng (mã OB/IB bracket, collar plate, lỗ HP trên web, bracket B) đều tính theo hướng mới.
- **Bracket:** nút ⇄ đảo dấu `BracketBuilder.BendFoldSign`.
- **Đổi OB ↔ IB qua dropdown thì hướng gấp tự đảo theo** (kỹ sư xác nhận 2026-11-04). Nút ⇄ dùng khi muốn giữ mã
  nhưng đảo hướng gấp. Đổi OB → IB rồi bấm thêm ⇄ thì hai lần đảo triệt tiêu nhau.

## 6. Cách làm (Approaches)

| Cách | Nội dung | Đánh đổi |
|---|---|---|
| A | Chỉ đổi nhãn, ghi ra CSV | Nhanh, nhưng 3D vẫn sai |
| **B (đã chọn)** | Lưu correction → phân loại lại + áp correction → build lại **cả panel** | Đúng dây chuyền phụ thuộc; cần giữ bản 2D để rebuild |
| C | Chỉ sửa riêng solid được chọn | Loại: làm lệch collar / lỗ web / bracket B / mã bracket bên cạnh |

## 7. Thiết kế cách B

1. **Khoá correction** = (handle block 2D nguồn, handle polyline 2D, chỉ số đoạn sau khi tách). Không dùng handle
   3D vì handle 3D đổi sau mỗi lần build. Kho lưu nằm trong bộ nhớ phiên (static, cạnh PaletteManager).
2. **Áp correction trong phân loại**, đúng ở bước tool ra quyết định, không áp sau cùng:
   - Web ↔ GE/GB → `GirderEndSplitter` tôn trọng loại bị ép (ép tách / ép không tách).
   - Mã bracket → `BracketCoder` (sau khi tính, ghi đè mã; đổi OB↔IB kéo theo đảo hướng gấp).
   - Belly HP → `BracketCoder.Apply`, ngay sau khi gán `BellyDir`.
   - Đổi loại trong họ (Closed Plate, Faceplate, Backing Plate…) → đổi `PartType` trước khi build.
3. **Rebuild** = `Classify(2D, position, overrides)` → `Build` → thay block 3D cũ → nạp lại tab Result,
   correction vẫn còn hiển thị. Chi tiết trải nghiệm ở mục 9.
4. **Giữ bản 2D nguồn.** Khi Keep, tool đang xoá block reference 2D ([PanelCommands.cs:92](../../../Commands/PanelCommands.cs)).
   Cách đã chọn (6a): giữ nguyên việc xoá, lúc rebuild thì đọc lại reference đã xoá bằng `openErased`.
   Cách này dùng được trong suốt phiên mở bản vẽ.
5. **Export/Copy:** cột `Panel, Handle, Family, DetectedKind, Kind, Size, Flags, DirFlipped`.

## 8. Phần 0 (làm TRƯỚC): sửa nhận diện / dựng GB

Độc lập với tab Result. GB chỉ xuất hiện khi L (đầu end tại HP/FB → Web vuông góc) ≥ Max Stiff (không đổi).

**8.1 Mốc toe.** `GirderEndSplitter.NearestCrossingHp` hiện chỉ nhận HP **xuyên** qua trục web. Mở rộng thành:

- HP **hoặc FB** cắt ngang (`Cross`), nằm trong khoảng (0, limit) dọc web, và thoả **một trong hai** điều kiện:
  - xuyên qua trục web; hoặc
  - **dừng cách** mặt web ≤ `GB_ANCHOR_GAP` = 50mm.
- Toe = **đường tâm HP/FB + 100** (+ 60 đoạn đáy), như ca HP xuyên (kỹ sư xác nhận 2026-11-04).
- Không tìm được mốc ⇒ giữ Web chữ nhật như hiện nay, chỉ ghi vào Notes, **không hiện gì trên tab Result**.

**8.2 Chi tiết đi kèm trong vùng GB** (kỹ sư xác nhận 2026-11-04):

| Ca | Cần có | Hiện trạng | Việc |
|---|---|---|---|
| HP xuyên qua GB | Lỗ CO + Collar Plate, như HP xuyên web | Chưa có: khoét chỉ chạy trên `PartType.Web` (Build3DService:118), collar chỉ duyệt `WebRuns()`; GB gộp vào web **sau** bước khoét | Đưa solid GB vào bước khoét + dựng collar (khoét trước hoặc sau khi hợp nhất đều được, miễn là có lỗ) |
| HP dừng cách ≤ 50mm | Bracket B dưới HP, như HP end | `HpEndBracketBuilder` đã coi `GirderEnd` là vách chắn | Kiểm chứng sau khi 8.1 tạo được GB |
| FB xuyên qua GB | Ngắt FB theo chiều dày GB | `FlatBarSplitter` đã ngắt tại mọi member AM_0 | Kiểm chứng |
| FB dừng cách ≤ 50mm | Sniped End | `FlatBarBuilder` đã có | Kiểm chứng |

Cập nhật skill `girder-bracket` (dòng "GB không có HP xuyên → GB?" và "HP xuyên ⇒ CO + collar") cho khớp code.

## 9. Trải nghiệm Rebuild (yêu cầu: kết quả ngay, không thao tác thêm)

- Sửa Kind (dropdown) hoặc bấm ⇄ ⇒ **tự rebuild ngay** panel đó. Không có nút riêng, không hỏi chọn panel,
  không hỏi Keep Yes/No.
- Chỉ rebuild **panel có correction thay đổi**. Block 3D được thay đúng chỗ.
- Bảng giữ dòng đang chọn và vị trí cuộn. Solid vừa sửa tự highlight.
- Mỗi lần rebuild là **một bước Undo**.
- Bản 2D: đọc lại block reference đã xoá khi Keep (`openErased`). Người dùng không thấy gì thay đổi. Nếu định nghĩa
  block đã mất (PURGE) ⇒ báo trên thanh trạng thái, không rebuild.
- Lựa chọn của Claude: nếu đo thực tế một lần rebuild > ~2 giây, gộp các thay đổi liên tiếp (debounce ~0,5 giây)
  để chỉ chờ một lần. Thời gian build một panel chưa đo.
- Web + GB ở chỗ không có mốc toe (mục 8.1) ⇒ lựa chọn đó **bị làm mờ** trong dropdown, kèm tooltip.

## 10. Để người làm tự quyết

- Tách chỉ số đoạn sao cho ổn định giữa các lần phân loại (cùng bản 2D → cùng thứ tự đoạn).
- Size/Info lấy từ đâu cho từng loại (FootprintThickness, bảng HP/FB, mã bracket).
