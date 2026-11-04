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

---

# Phần 2 (bổ sung 2026-11-04, sau vòng 1)

Kỹ sư chốt sau khi xem bản đầu. Thay thế điểm "correction chỉ nhớ trong phiên" ở mục 2 và mục 7.1.

## 11. Sniped End theo từng đầu (HP + FB)

- Cột **Sniped End** (thay cột Flags) hiện 2 nút bật/tắt theo hướng thanh:
  - thanh ngang: `◀ L` / `R ▶`;
  - thanh đứng: `▼ B` / `T ▲`;
  - thanh chéo: xếp theo X (L = đầu X nhỏ).
- Nút sáng = đầu đó đang vát. Rê chuột lên nút ⇒ highlight đầu đó trên bản vẽ. Bấm ⇒ đổi vát/không vát rồi tự
  rebuild (như mục 9).
- Áp cho **cả HP lẫn FB**, theo cả hai chiều: **bỏ** vát ở đầu tool vát, **thêm** vát ở đầu tool không vát.
- Bỏ vát ở đầu HP "End 30" ⇒ **để vuông, không thêm gì** (không ép Bracket B) — kỹ sư 2026-11-04.
- Builder: `HpBuilder.SnipEnd` / `FlatBarBuilder` (`free1`/`free2`) tôn trọng override từng đầu. Override thắng
  quyết định tự động. Ghi lại đầu nào thực sự vát lên part để bảng hiển thị.

## 12. Delete / Restore solid

- Chuột phải → **Delete**: chỉ xoá đúng solid đó, **không nhận diện lại** gì, không có hiệu ứng dây chuyền.
  Ghi thành correction, nên rebuild sau không làm nó quay lại, và Export STEP không có nó.
- Dòng bị xoá vẫn hiện trên bảng (gạch ngang, mờ). Chuột phải → **Restore** ⇒ rebuild có lại solid.
- Áp được cho cả solid sinh thêm (Collar Plate, Bracket B, Flange GE/GB). Khoá = SourceKey của part sinh ra nó +
  loại chi tiết + thứ tự (vd `393B9#0/CollarPlate#1`).
- Cài đặt: Build3DService bỏ solid có khoá nằm trong danh sách xoá, **sau** mọi bước boolean/hợp nhất.

## 13. Lưu correction trong DWG (không cần file ngoài)

- Mỗi lần sửa ⇒ correction của panel được ghi ngay vào **định nghĩa block 2D** (XRecord trong ExtensionDictionary
  của BlockTableRecord 2D). Lưu bản vẽ bình thường (Ctrl+S) là lưu luôn correction. Copy block sang bản vẽ khác thì
  correction đi theo.
- Build 3D block đó (lần sau, sau khi mở lại) ⇒ tự đọc correction và áp ngay từ lần build đầu.
- Correction gắn với **định nghĩa block**: mọi lần chèn của cùng block 2D dùng chung correction (kỹ sư chấp nhận;
  nếu sau này cần riêng từng lần chèn thì báo lại).
- Kho trong bộ nhớ (`CorrectionStore`) đổi khoá thành định nghĩa block 2D; DWG là nguồn gốc, bộ nhớ chỉ là cache.
- Không lưu được XRecord (bản vẽ chỉ đọc, block bị khoá…) ⇒ báo trên thanh trạng thái, correction vẫn áp trong phiên.

## 14. Rebuild sau khi đóng và mở lại bản vẽ

- Block 3D ghi kèm (XData hoặc XRecord) **handle định nghĩa block 2D + ma trận chèn + PanelInput + PanelPosition**.
- Mở lại bản vẽ, block reference 2D đã xoá khi Keep không còn ⇒ rebuild **chèn tạm** block 2D bằng ma trận đã lưu,
  phân loại, build, rồi xoá block tạm. Người dùng không thấy bước này. Vẫn là một bước Undo.
- Tab Result cần nút/cách nạp lại block 3D đã có (sau khi mở lại, bảng trống): **chọn block 3D trên bản vẽ ⇒ nạp
  lên bảng** (lựa chọn của Claude: nút "Load 3D…" trên thanh công cụ tab Result).
- Định nghĩa block 2D đã bị PURGE ⇒ báo "Source 2D block no longer exists — cannot rebuild." như cũ.

## 15. Export STEP

- Nút **Export STEP…** trên thanh công cụ tab Result. Xuất **mỗi panel ra một file riêng**, tên theo panel
  (`P101.stp`, `P102.stp`…), vào thư mục người dùng chọn một lần (mặc định thư mục của bản vẽ).
- Nội dung = các solid hiện có trong block 3D (đã sửa, đã xoá solid).
- Dùng lệnh `STEPOUT` của AutoCAD Mechanical (`Acadm/atf_step_out.arx` có trên máy, kiểm 2026-11-04). Cách chạy cụ
  thể (FILEDIA, chọn đối tượng, xuất từ block hay explode tạm) để người làm thử trong AutoCAD.
- Không có `STEPOUT` (AutoCAD thường, không phải profile Mechanical) ⇒ nút mờ, tooltip "Requires AutoCAD Mechanical
  (STEPOUT)".
