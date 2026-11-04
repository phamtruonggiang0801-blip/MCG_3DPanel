# HP End tại FB (đầu HP kết thúc tại thân Flat Bar) — Design (ngắn)

Ngày: 2026-11-04 · Nguồn: `CAS.dwt` → `CAS.dump.json`, `*Model_Space`, MText **"HP TO FB END"**.
Mở rộng tính năng [HP end tại HP](2026-11-04-hp-end-at-hp-design.md) — dùng lại khung nhận diện + dao cắt cách A.

## 1. Quy cách (giải mã dump, kỹ sư xác nhận 2026-11-04)

Dump: tấm 6 (y 1648.66–1654.66); FB chạy = polyline `1F20B` 6×80 (x 568.34–574.34, y 1568.66–1648.66);
HP đầu HP120 mặt bên, mặt đầu áp mặt FB x=574.34 từ đỉnh xuống y=1583.66 (= đáy FB + **15**, dim Rotated 15),
rồi line vát (574.34,1583.66)→(669.60,1528.66): dx 95.26, dy 55, **30° so với đáy HP** (dim góc 0.5236).

Hệ mặt cắt FB chạy (x' từ mặt tiếp xúc vào HP đầu, y = Z, 0 = mặt dưới tấm, −b = đáy HP):
- **Mặt hàn** `x'=0`, `y: 0 → yW = −(h_fb − 15)` — **15 và 30° cố định mọi cỡ** (kỹ sư).
- **Vát 30°** so với đáy HP: từ `(0, yW)` tới `((b − (h_fb − 15))·√3, −b)` (HP120+FB80 ⇒ 95.26 — khớp dump).
- **FB cao** (`h_fb − 15 ≥ b`): dùng biên dạng HP–HP OUTSIDE (`p = 0`, mặt hàn tới `−(b−d−R)`, cung R30, lùi 30) (kỹ sư: "làm giống HP to HP end").
- Vát / cắt **phủ hết bề rộng HP đầu kể cả bulb** (như `SnipHpEnd`). FB không bulb ⇒ không INSIDE/OUTSIDE.

## 2. Approaches

Chỉ 1 cách hợp lý (kỹ sư chọn cách A cho cả FB xiên): dao cắt trong **mặt cắt FB chạy**, extrude dọc trục FB,
BoolSubtract khỏi HP đầu — dùng lại nguyên khung `HpEndAtHpBuilder` (cách B đã loại ở spec HP–HP: sai khi xiên).

## 3. Thiết kế

- **Toán thuần** (`HpEndAtHpGeometry`, có unit test):
  - `FbCutterOutline(b, hFb, tFb, c, r)` → `(−W,+M) → (0,+M) → (0,yW) → (xB, −b−M) → (−W, −b−M)`, `xB = (b + M − (h_fb−15))·√3`
    (kéo đường vát qua đáy M), `W = tFb + c + r + M`. `h_fb − 15 ≥ b` ⇒ trả `CutterOutline(b, tFb, c, r, inside:false)`. Tham số vô lý ⇒ null.
  - `HalfSpan` tổng quát theo bề rộng x' lớn nhất của dao cắt (vát FB vươn ~100mm, không phải p+R).
  - `Match(..., runIsFb: true)`: bỏ kiểm `noBelly`, `Inside=false`, `p=0` — mọi luật khác GIỮ NGUYÊN (far `t_fb/2 + 10`, thò ≥ 5 qua mặt web HP đầu, joint 25, góc ≥ 45°).
- **Builder** (`HpEndAtHpBuilder`): ứng viên chạy = HP **và** `PartType.FlatBar` (các đoạn sau `FlatBarSplitter`); lấy gần nhất.
  FB ⇒ `HpProfileService.HpEndAtFbCutter(...)`; `ctx.Input.FlatBarHeight ≤ 0` ⇒ bỏ FB, Report. Nối dài khi vẽ hụt (Gap) dùng chung.
  `TouchesRunningHp` tính cả FB ⇒ đầu HP chạm FB **không vát Sniped End** kiểu cũ.
- Report: `[HpEndAtHp] HP-end-at-FB h=.. m=.. θ=.. hfb=.. weld=..` (FB cao: `… mode=R30`).
- **Skill**: thêm mục "HP end tại FB" vào `hp-end-at-hp` (nguồn dump, quy cách, quyết định kỹ sư).

## 4. Không làm
FB kết thúc tại HP (chiều ngược) — đã có luật FB riêng (sniped-end §3e). Vát 5×5 / override tab Result — như HP–HP.
