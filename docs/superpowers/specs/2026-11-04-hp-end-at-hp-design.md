# HP End tại HP (đầu HP kết thúc tại lưng/bụng HP khác) — Design

Ngày: 2026-11-04 · Nguồn quy cách: `CAS.dwt` → `CAS.dump.json` (Model Space, nhãn "HP TO HP END").

## 1. Mục đích

Khi 1 HP (**HP đầu**) kết thúc chạm THÂN 1 HP khác (**HP chạy** — liên tục qua chỗ đó), 3D hiện dựng
HP đầu thẳng vuông tới đầu mút 2D → chồng/hở với HP chạy và không có cắt thoát bulb. Cần dựng đúng
quy cách CAS: HP đầu hàn vào mặt web HP chạy phần trên, phần dưới cắt lùi + cung R30 thoát bulb.
Đồng thời lập skill `hp-end-at-hp` lưu quy cách.

## 2. Quy cách hình học (giải mã từ dump, kỹ sư xác nhận 2026-11-04)

Hệ tọa độ **mặt cắt HP chạy** (mặt phẳng ⟂ trục HP chạy, chứa Z):
`x'` = pháp tuyến từ **mặt tiếp xúc** (mặt web HP chạy phía HP đầu) hướng VÀO HP đầu; `y` = Z thế giới
(0 = mặt dưới Top Plate, −b = đáy HP).

| Ký hiệu | Giá trị | Nguồn |
|---|---|---|
| `R` | **30, cố định mọi cỡ HP** | kỹ sư |
| `d` | chiều cao đoạn bulb = `b − dx`, `dx = b − r − (3r − t + c)/√3` | `HpProfileService` theo bảng (kỹ sư đồng ý; block mẫu dùng r=4.5 ⇒ 18.66, bảng r=5 ⇒ ~20.0) |
| `p` | BULB INSIDE (bụng HP chạy quay về HP đầu): `c + r − t` · BULB OUTSIDE: `0` | dump (OUTSIDE: đỉnh trùng trong polyline `1F255`) |
| Khoảng lùi | `p + R` — đổi theo bề rộng bulb | kỹ sư |

Biên dạng đầu HP đầu (trên x'–y):
1. `x'=0` từ `y=0` xuống `y = −(b − d − R)` — mặt hàn vào web HP chạy (HP120: ~70mm).
2. Ngang ra tới `x'=p` (OUTSIDE: độ dài 0).
3. Cung R30, tâm `(p, −(b−d))`, từ `(p, −(b−d−R))` tới `(p+R, −(b−d))`.
4. Thẳng đứng `x'=p+R` xuống đáy `y=−b` (cắt luôn đầu bulb của chính HP đầu).

Bỏ qua: vát 5×5 góc trên (chỉ có ở hình OUTSIDE) — **không đưa vào 3D** (kỹ sư). Khe 1.31mm trong
polyline `1F247` = lỗi vẽ. Line AM_3 (vết bulb HP đầu) — không dùng.

## 3. Mục tiêu / Không làm / Phản mục tiêu

- **Mục tiêu:** nhận diện đầu HP chạm thân HP khác; trừ khối dao cắt khỏi HP đầu theo §2; đúng cả HP
  **xiên trong mặt bằng** (HP chạy và/hoặc HP đầu không song song X/Y); ghi Report; skill mới.
- **Không làm (v1):** vát 5×5; override thủ công ở tab Result (xem §8); HP khác cỡ trong 1 panel
  (`ctx.Input.HpHeight` là 1 giá trị/panel — giữ nguyên giả định).
- **Phản mục tiêu:** làm sai các nhánh đầu HP đã chốt — mối nối đầu-đầu (`HP_JOINT_TOL`), "End 30" +
  bracket B, Sniped End; cắt nhầm HP chạy; làm vỡ bản dựng khi thiếu dữ liệu (phải bỏ qua cặp lỗi).

## 4. Phương án đã cân nhắc

- **A (CHỌN — kỹ sư 2026-11-04): dao cắt định nghĩa trong MẶT CẮT HP CHẠY**, extrude dọc trục HP chạy,
  BoolSubtract khỏi HP đầu. Mặt đầu HP đầu bám mặt web (xiên) HP chạy; khe hở tới bulb luôn đúng R=30
  đo ⟂ HP chạy. θ=90° khớp 1:1 mẫu CAS. Cùng khuôn với `WebHpCutoutBuilder`.
- B: dao cắt trên mặt phẳng đứng của HP đầu (kiểu `SnipHpEnd`) — đơn giản nhưng chỉ đúng θ=90°: xiên
  thì khe hở còn `(p+R)·sinθ`, mặt đầu vuông trục gây hở/chồng `t·cotθ`. Loại.

## 5. Thiết kế

### 5a. Nhận diện (`HpEndAtHpDetector` hoặc static trong builder mới)
Với mỗi HP `h` và mỗi đầu mút `he` (`ClassifyUtil.Endpoints`), tìm HP `m ≠ h` thỏa:
1. **Không** phải mối nối đầu-đầu: `he` cách cả 2 đầu mút `m` > `HP_JOINT_TOL` (25) — tái dùng logic
   `HpEndBracketBuilder.JoinsOtherHp`.
2. `DistToSegment(he, m) ≤ t_m/2 + p + TOL_TOUCH` (TOL_TOUCH = 10, *quyết định của tôi*; cộng `p` vì phía
   bụng người vẽ có thể dừng HP đầu ở mép bulb thay vì mặt web) và hình chiếu `he`
   lên trục `m` nằm trong `[margin, L_m − margin]` (margin = `FB_SPLIT_MARGIN` 20).
3. Góc: `|axis_h · axis_m| ≤ 0.70` (θ ≥ ~45°, cùng ngưỡng "End 30" — *quyết định của tôi*, kỹ sư chưa
   phản đối). Ngoài ngưỡng → không cắt, ghi cảnh báo Report.
4. `h` không cắt XUYÊN `m` (thân `h` chỉ nằm 1 phía `m`) — loại giao chữ thập.
5. Nhiều ứng viên → lấy `m` gần `he` nhất.

INSIDE/OUTSIDE: `side` = dấu của `(centroid_h − hình chiếu lên trục m) · perp_m`; INSIDE nếu
`BellyDir_m` cùng phía với `h`. Thiếu `BellyDir_m` → bỏ qua cặp (Report).

### 5b. Dao cắt (`HpEndAtHpBuilder`, `internal static`, như `WebHpCutoutBuilder`)
`HpProfileService` thêm `HpEndAtHpCutter(b, t, c, r, inside)` → `List<VertexBulge>` trong hệ x'–y:
```
(−W, +M) → (0, +M) → (0, −(b−d−R)) → (p, −(b−d−R)) ⌒R(tâm (p,−(b−d))) → (p+R, −(b−d))
→ (p+R, −b−M) → (−W, −b−M) → đóng
```
`W` = `t_m + c + r + M` (phủ phần 2D của HP đầu vẽ lấn qua mặt tiếp xúc), `M` = margin 2.
Đặt: `local X → n` (pháp tuyến mặt tiếp xúc hướng về HP đầu), `local Y → +Z`, `local Z → axis_m`;
gốc = chân vuông góc của `he` trên mặt tiếp xúc (mặt web `m` phía `h`: lưng nếu OUTSIDE, mặt web phía
bụng nếu INSIDE), Z=0, lùi `½·span` dọc `axis_m`. `span = (t_h + c + r)/sinθ + 2·M_span` (phủ hết bề rộng
HP đầu kể cả bulb, cả khi xiên). Chỉ trừ khỏi solid HP đầu ⇒ HP chạy không bị đụng.

Cung R: bulge `tan(90°/4) = 0.4142`, dấu theo chiều duyệt (kiểm bằng test hình học).

### 5c. Tích hợp
- `Build3DService`: gom `hpSolids` (như `webSolids`); sau vòng dựng gọi
  `HpEndAtHpBuilder.Apply(ctx, hpSolids)` rồi `FillGeometry` lại các HP bị cắt. Lỗi 1 cặp → bỏ cặp, ghi Report.
- `HpBuilder.SnipEnd`: đầu đã nhận "HP end tại HP" giữ nguyên hành vi hiện tại (`IsFreeEnd` = false ⇒ không
  vát; HP không thuộc `TRANSVERSE_TYPES` ⇒ không "End 30") — chỉ cần test xác nhận, không sửa.
- Report: `HP-end-at-HP h=<handle> m=<handle> INSIDE|OUTSIDE θ=<deg> p=.. d=..`.

### 5d. Skill mới `.claude/skills/hp-end-at-hp/hp-end-at-hp_SKILL.md`
Quy cách §2 + hình phác, nhận diện §5a, đặt dao §5b, liên kết [[hp-profile]] [[sniped-end]]
[[web-hp-cutout]]; mục "Chưa chốt". Cập nhật mục liên quan trong `sniped-end` (§5b mối nối HP–HP) để
trỏ sang skill mới.

## 6. Kiểm thử
- Unit (thuần hình học, không AutoCAD): `HpEndAtHpCutter` cho HP120×6 INSIDE/OUTSIDE — đỉnh khớp §2
  (đối chiếu dump: mặt hàn 71.34 với r=4.5; theo bảng r=5); bulge/chiều duyệt đúng; HP60 & HP430 không suy biến.
- Unit nhận diện: vuông góc INSIDE/OUTSIDE, xiên 60°, xiên 30° (bị loại), nối đầu-đầu (bị loại), giao chữ thập (bị loại).
- Trực quan trong AutoCAD trên panel thật có HP–HP (§8).

## 7. Để lại cho người làm
Tên lớp/hàm cụ thể, cấu trúc test theo khuôn `tests/` hiện có, giá trị margin nhỏ.

## 8. Chưa chốt (cần kỹ sư)
- Có cần override bật/tắt cắt ở tab Result như Sniped End (`SnipOverride`) không? (v1: không)
- Bản vẽ panel thật có HP end tại HP (cả trường hợp xiên) để test trực quan.
