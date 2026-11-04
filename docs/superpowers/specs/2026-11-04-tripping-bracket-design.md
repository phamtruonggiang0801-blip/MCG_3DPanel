# Tripping Bracket (bracket B dưới HP giữa đoạn dầm) — Design

Ngày: 2026-11-04 · Nguồn: `CAS.dwt` → `CAS.dump.json` (`*Model_Space`, 40 entity, mặt bằng).
Quy tắc kỹ sư xác nhận 2026-11-04. **Tạm thời** — kỹ sư sẽ test trên nhiều panel rồi đánh giá lại quy tắc.

## 1. Mục đích / nguồn gốc

Đăng kiểm: cứ khoảng **3 lần bước stiffener lớn nhất** dầm phải có gối chống vặn xoắn (tripping). Ghi chú bản vẽ:

> ADD 1 TRIPPING BRACKET BELOW HP IF 3 OR MORE CONTINUOUS STIFFENER IN BEAM SEGMENT. ADD THIS BRACKET
> CLOSEST TO BEAM SEGMENT MIDDLE. FOR DETAIL SEE HP BRACKET DETAIL.

Tương tự Collar Plate (add cho lỗ khoét HP liền kề junction): tripping bracket add cho HP gần giữa đoạn của
các dầm **bên trong** panel — dầm ngoài cùng không cần (đầu đã có gối đỡ chống xoắn).

## 2. Giải mã dump

- Dầm (ngang): web y 1620.86–1623.86, flange ẩn AM_11 rộng 75 (polyline `1F224`, y 1584.86–1659.86).
- 2 dầm liên tục nhãn **"CG"** (dọc) x 457–460 (flange 110) và 1462–1465 (flange 50); dim **1005 "BEAM SEGMENT"**
  = 457.1 → 1462.1.
- 4 HP xuyên web dầm (`Continuous_symbol` tại mỗi giao): tâm x 633.6 / 833.6 / 1043.6 / 1273.6.
- Text **"B"** cạnh HP 1043.6, phía −y web dầm. Giữa đoạn ≈ 959.6 ⇒ HP 1043.6 gần nhất (84) ✔.

## 3. Quy tắc (kỹ sư 2026-11-04)

| # | Quy tắc |
|---|---|
| R1 | **Dầm** = Web có Flange (web run, gộp tier thẳng hàng như Collar Plate). |
| R2 | **Đoạn dầm** = dầm KHÔNG liên tục, bị ngắt bởi dầm liên tục khác: web run có flange mà **CẢ 2 đầu kết thúc chạm mặt bên** của 1 web run có flange khác (dầm liên tục — kể cả dầm biên, web xiên). Chỉ tính dầm/girder **có flange** cắt ngang làm gối. |
| R3 | **Continuous stiffener** = chỉ HP xuyên qua web đoạn dầm (có lỗ CO — `ClassifyUtil.CrossesThrough` + kẹp như `WebHpCutoutBuilder`); KHÔNG tính FB. |
| R4 | Đoạn có **≥ 3** HP ⇒ **đúng 1** tripping bracket, tại HP có giao điểm **gần điểm giữa đoạn** nhất (giữa 2 mặt dầm liên tục). |
| R5 | **Hoà** (2 HP cách giữa bằng nhau, ≤ 1mm) ⇒ chọn HP về phía **chiều đổ** dọc trục dầm. |
| R6 | **Dầm ngoài cùng** (min/max theo phương vuông góc trục, tol 50 — như HP ngoài cùng) ⇒ KHÔNG thêm. |
| R7 | **Hình dạng = bracket B hiện có** (`hp_end_brkt_1/2`, `Bracket B.dwg`, skill sniped-end §5a): `thk` = dày HP, `Ht = WebHeight − b`, `reach = max(180, 30+1.5b)`, R50/topL/e15/off2; selector theo `overhang` flange dầm phía bracket (≥ 50 ⇒ brkt_1 `d1 = overhang−15`; < 50 ⇒ brkt_2 `d1 = 15`, đáy nâng 30). **Khác bracket B:** mép đứng hàn THẲNG mặt web dầm (không khe 30 — HP liên tục). |
| R8 | **Đặt:** tấm đứng **đồng phẳng thân HP** (tâm trùng tuyến HP, không lệch), dưới HP (đỉnh tại đáy HP `Z=−b`), đáy trên flange dầm. **Phía** web dầm = **chiều đổ kết cấu** (`PanelPosition`: X luôn +1; Y Center về tâm / Port −Y / Starboard +Y — skill bracket-identification) chiếu lên pháp tuyến web dầm. |
| R9 | Tab Result: **chỉ đảo phía** (sang bên kia web dầm). |

Ví dụ 7D-16C (build report 2026-11-04): đoạn dầm `1B6A5`/`1B5E6`/`1B5EB`/`1B5F4`/`1B5F0` (giữa `1B5CD` và
`1B602`), 12 HP mỗi đoạn ⇒ bracket tại `1B60D` (y 6507, cách giữa 306); `1B5CC`/`1B5E4` ngoài cùng ⇒ không;
`1B62A` (giữa `1B602` và web xiên `1B601`) 3 HP ⇒ có.

## 4. Phương án

- **Chọn:** builder mới `TrippingBracketBuilder` (`internal static`, khuôn `WebCollarPlateBuilder` — web run,
  `CrossesThrough`) + **tách phần dựng tấm** (polygon brkt_1/2 + đặt 3D) khỏi `HpEndBracketBuilder` thành hàm
  dùng chung, để 2 loại bracket không lệch nhau.
- Loại: gộp vào `HpEndBracketBuilder` (trộn 2 luật phát hiện khác hẳn); chỉ ghi chú 2D (không đúng mục tiêu 3D).

## 5. Thiết kế

- **Toán thuần** (`Services/Builders/TrippingBracketRules.cs`, không AutoCAD, unit test): chọn HP từ danh sách
  tham số dọc trục `s` các giao HP × đoạn + `[sA, sB]` 2 mặt dầm liên tục + dấu chiều đổ dọc trục ⇒ chỉ số HP
  hoặc null (< 3); phía (dấu) từ chiều đổ · pháp tuyến web + cờ flip; selector brkt_1/2 + `d1` từ `overhang`.
- **Builder:** dầm = web run có flange (flange chạy dọc web run, như `FlangeOverhang` bracket B); đoạn = 2 đầu
  run cách mặt 1 web run có flange khác ≤ tol (≈ dày web + 10) và tia đầu mút cắt đúng thân run đó; ngoài cùng
  theo R6; HP xuyên qua theo R3; dựng tấm qua hàm dùng chung (gốc = mặt web dầm phía bracket ∩ mặt trên flange,
  `x → ra xa web dầm dọc trục HP`, `y → +Z`, tâm tấm trên tuyến HP). Layer/Color = web đoạn dầm (như bracket B).
- **Khoá + đảo phía:** `DeleteKey = FlipKey = "TrippingBracket:<beam SourceKey>~<hp SourceKey>"`. Correction
  `FlipDir` lưu trong `CorrectionStore` theo khoá này; builder đọc `ctx.Cls.Corrections[key]?.FlipDir` ⇒ đổi phía.
  Tab Result: dòng solid sinh thêm có `FlipKey` ⇒ `CanFlip = true`, nút ⇄ ghi correction theo `FlipKey`
  (thay vì `SourceKey` part 2D). Xoá dòng dùng cơ chế DeleteKey sẵn có.
- **Report:** `[TrippingBracket] beam=.. hp=.. nHP=.. s=../mid=.. side=± brkt_1|2 overhang=..`; đoạn < 3 HP /
  ngoài cùng ⇒ 1 dòng `SKIP` lý do.
- **Skill mới** `tripping-bracket` (quy tắc §3, nguồn §2, cài đặt, "TẠM — chờ test nhiều panel").

## 6. Không làm / phản mục tiêu

- Không: chuyển bracket sang HP khác trên tab Result (chỉ đảo phía); FB tính stiffener; >1 bracket/đoạn.
- Phản mục tiêu: làm đổi bracket B "End 30" hiện có (tách hàm dựng tấm phải giữ nguyên kết quả — so khớp polygon
  trước/sau bằng test); bracket chồng lên Collar Plate / lỗ CO (collar nằm `Y ≥ −B1` > `−b`, bracket từ `−b`
  xuống ⇒ không chồng — kiểm khi đặt); đoạn dầm sai (dầm liên tục bị coi là đoạn).

## 7. Kiểm thử

Unit (toán thuần): chọn HP (4 HP dump ⇒ HP#3; < 3 HP ⇒ null; hoà ⇒ theo chiều đổ ±); phía + flip; selector
brkt (overhang 37 ⇒ brkt_2 d1=15; 64 ⇒ brkt_1 d1=49). Polygon dùng chung: test khớp polygon bracket B hiện có
(trước/sau refactor). Build + suite. Trực quan: kỹ sư test nhiều panel.
