---
name: hp-profile
description: Bộ dữ liệu + hình học HP BULB FLAT (Holland Profile) — tham số b/t/c/r/vát 30°, bảng tra 18 cỡ (60–430), công thức dựng tiết diện, generator HpProfileService. Đọc khi dựng HP 3D, tra kích thước bulb, hoặc cần hướng bụng/lưng HP.
---

# HP Bulb Flat (Holland Profile) — dữ liệu & hình học

HP = thanh dẹt có **bulb** (gờ phình) ở MỘT đầu. Trong panel: HP hàn đứng, đầu phẳng lên Top Plate,
bulb ở đầu dưới. Mặt phẳng = **LƯNG**; mặt có bulb = **BỤNG** → quyết định OB/IB
(xem [[bracket-identification]] mục 4, `BellyDir`).

## 1. Tham số hình học

| Ký hiệu | Nghĩa | Ghi chú |
|---|---|---|
| **b** | chiều cao toàn phần | tra bảng / từ spec `"120*6"` |
| **t** | chiều dày web | từ spec |
| **c** | **bề rộng ĐÁY BULB tính từ LƯNG (x=0)** | ⚠️ KHÔNG phải phần nhô khỏi web |
| **r** | bán kính bulb **=** bán kính cung chuyển tiếp (bằng nhau) | tra bảng |
| **r1** | bo góc mép | không có trong bảng; mặc định 0 (sắc) |
| **30°** | góc **vát** nối web → bulb | **HẰNG SỐ** mọi cỡ |

**SUY RA (không cần tra thêm):**

```
tâm cung bulb   xB = c
bulb rộng nhất  = c + r
đoạn web thẳng  dx = b − r − (3r − t + c)/√3
đoạn bulb       d  = b − dx
```

`dx` suy từ điều kiện: **vát 30° là tiếp tuyến chung TRONG** của 2 cung bán kính r
(cung LÕM chuyển tiếp tâm `A=(t+r,−dx)` ↔ cung LỒI bulb tâm `B=(c,−(b−r))`) — 2 tâm nằm **hai phía**
đường vát, mỗi bên cách `r`: `(A−B)·n = 2r`, `n=(½, √3/2)`.

## 2. Dựng tiết diện

Local: gốc (0,0) = góc **LƯNG–ĐẦU**; **+X → BỤNG**; **−Y** xuống theo b. Outline kín, duyệt **CW**.

| # | Từ → đến | Loại | bulge |
|---|---|---|---|
| 1 | (0,0) → (t,0) | mép đầu (hàn lên Top Plate) | 0 |
| 2 | (t,0) → (t,−dx) | mặt web | 0 |
| 3 | (t,−dx) → P3 | **cung LÕM** r, tâm `A=(t+r,−dx)`, 180°→240° | **+tan15° = +0.2680** |
| 4 | P3 → P4 | **vát 30°**, hướng `(cos−30°, sin−30°)` | 0 |
| 5 | P4 → (c,−b) | **cung BULB (lồi)** r, tâm `B=(c,−(b−r))`, 60°→270° CW 150° | **−tan37.5° = −0.7673** |
| 6 | (c,−b) → (0,−b) | mép đáy bulb | 0 |
| 7 | (0,−b) → (0,0) | **LƯNG** (mặt phẳng) | 0 |

`P3 = A + r·(cos240°, sin240°)` · `P4 = B + r·(cos60°, sin60°)`

## 3. BẢNG TRA (nguồn `HP.csv`) — **c, r CHỈ phụ thuộc b**, KHÔNG phụ thuộc t

| b | c | r | t hợp lệ | | b | c | r | t hợp lệ |
|---|---|---|---|---|---|---|---|---|
| 60 | 13 | 3.5 | 4–6 | | 240 | 34 | 10 | 10–12 |
| 80 | 14 | 4 | 5–8 | | 260 | 37 | 11 | 10–12 |
| 100 | 15.5 | 4.5 | 6–8 | | 280 | 40 | 12 | 11–13 |
| 120 | 17 | 5 | 6–8 | | 300 | 43 | 13 | 11–13 |
| 140 | 19 | 5.5 | 7–9 | | 320 | 46 | 14 | 12–14 |
| 160 | 22 | 6 | 7–9 | | 340 | 49 | 15 | 12–14 |
| 180 | 25 | 7 | 8–11 | | 370 | 53.5 | 16.5 | 13–15 |
| 200 | 28 | 8 | 9–12 | | 400 | 58 | 18 | 14–16 |
| 220 | 31 | 9 | 10–12 | | 430 | 62.5 | 19.5 | 14–20 |

## 4. ĐÁNH GIÁ CHẤT LƯỢNG DỮ LIỆU `HP.csv`

**Cột thật:** `Size, b, t, c, r, Section(cm²), Weight(kg/m), Area(m²/m), dx(cm), Ixx(cm⁴), Wxx(cm³)`

✅ **Tin dùng được:**
- `c`, `r` **biến thiên rất đều theo b** (c: +3/20mm; r: +0.5/20mm dưới 160, +1/20mm trên 160) → nội bộ nhất quán, không có outlier.
- `c` khớp **CHÍNH XÁC** block `HP_120x6_sv` của MacGregor: CSV c=17 = mép đáy bulb `(0,−120)→(17,−120)` trong block ✔.
- 58 dòng phủ 18 cỡ b (60→430) × các t hợp lệ.

⚠️ **Cạm bẫy / khác biệt — ĐÃ xử lý:**

| Vấn đề | Chi tiết |
|---|---|
| Cột **`dx` KHÔNG phải dx hình học** | Là **vị trí trọng tâm, đơn vị cm** (HP120×6 → 7.21cm). `dx` hình học phải TÍNH bằng công thức mục 1. Rất dễ nhầm. |
| **`r` lệch block 0.5mm** | CSV HP120×6 **r=5**, block MacGregor vẽ **r=4.5**. Với r=5 → `dx=99.99≈100` (tròn đẹp, gợi ý CSV đúng); r=4.5 → dx=101.355 (khớp block 101.345). **Chênh 0.5mm — bỏ qua được với mô hình 3D.** Đang dùng **CSV** (phủ đủ cỡ). |
| Cột `b`, `t` là **chuỗi dung sai** | `" 120 ±1,5 "`, `"6 +0,7 / -0,3 "` → KHÔNG parse được trực tiếp. Lấy b,t từ cột **`Size`** (`"120 x 6"`). |
| File **1 dòng dài**, hàng ngăn bằng space | Không phải CSV chuẩn — cần parse thủ công. |
| `Ixx` lớn có **dấu phẩy nghìn trong ngoặc kép** | `"6,757"` = 6757 (không phải 6.757). |
| Dấu thập phân trộn `,` và `.` | Dung sai dùng `,` (EU), số liệu dùng `.`. |
| Ký tự lỗi encoding | `(cm?)`, `?4`, `�` — không ảnh hưởng cột c/r. |

❌ **KHÔNG có trong CSV:** `r1` (bo góc mép) và góc vát 30° → lấy từ **bản vẽ parameter** (30° là hằng số; r1 mặc định 0).

## 5. Generator — `HpProfileService`

- `HpSection(b, t, c, r, r1=0)` → `List<VertexBulge>` (outline kín CW). `r1>0` → bo 3 góc mép (bulge −0.4142).
- `LookupCR(b, out c, out r, out estimated)` — tra bảng theo **b**; b ngoài bảng → **nội suy tuyến tính** + `estimated=true`.
- `TryGetSection(spec, out section, out estimated)` — spec `"cao*dày"`, dùng `PanelInput.TryParseSpec`.
- Tham số vô lý (`dx ≤ r`, `c ≤ t`) → fallback tiết diện chữ nhật `b×t` (không vỡ hình).

**Kiểm chứng** (b=120, t=6, c=17, r=4.5): dx **101.355** / block 101.345 · A **(10.5,−101.355)** / (10.5,−101.345) ·
P3 **(8.25,−105.252)** / (8.25,−105.242) · B **(17,−115.5)** / (17,−115.5) · bulbMax **21.5** / 21.5 → lệch **< 0.01mm** ✔

## 5b. ⚠️ HP nằm CHÉO (thanh viền / vát góc) — bẫy đã gặp

Panel thật có **HP chéo** (vd 4 đoạn dài 300 ở 4 góc, nghiêng ~2.8°). Ba cái bẫy:

| Bẫy | Hậu quả | Cách xử lý |
|---|---|---|
| **bbox không suy ra hướng** | `IsHorizontal` vô nghĩa; trục ≠ X/Y | Dùng **`PanelPart.AxisDir`** — trục THẬT lấy từ cạnh dài của polyline (`PartCollector.AxisOf`) |
| **bbox "dày" giả** | HP chéo có bbox-thickness 20+ dù **dày thật = 6** → dễ tưởng "mảnh vụn" rồi lọc mất | `RectDims` tính dày/dài từ ĐỈNH (đúng); đừng lọc HP theo bbox |
| **Lọc theo chiều dài** | `HP_MIN_LEN=500` từng loại các đoạn 300 khỏi việc gán `BellyDir` → HpBuilder thiếu bụng → **rơi về hộp chữ nhật** | Gán `BellyDir` cho **MỌI** HP; `HP_MIN_LEN` chỉ dùng cho biên ngoài cùng + ghép bracket |

**`BellyDir` tổng quát** (`BracketCoder`): bụng = **pháp tuyến của `AxisDir`**, chọn dấu:
HP **ngoài cùng hoặc CHÉO** → ra ngoài (xa tâm panel); HP bên trong → chiếu **chiều đổ** `(+X, dozY)` lên pháp tuyến.
Tương đương hoàn toàn quy tắc cũ với HP song song trục.

**`HpBuilder`** ép `belly ⟂ axis` trước khi `AlignCoordinateSystem` (hệ phải trực giao, lệch nhỏ cũng ném lỗi).

## 6. Checklist

- [x] Tham số + công thức suy dx, xB (kiểm chứng vs block).
- [x] Bảng tra 18 cỡ b từ HP.csv + đánh giá chất lượng dữ liệu.
- [x] Generator `HpProfileService` + nội suy + fallback.
- [ ] `r1` theo cỡ (chưa có nguồn).
- [ ] Nối vào Build3DService: sweep tiết diện dọc trục HP, **+X trỏ về BỤNG** theo `BellyDir`.
- [x] `HpCutoutSection(b,t,c,r,coR)` + `LookupCoRadius` — tiết diện LỖ KHOÉT web cho HP xuyên qua
      (polygon 6 đỉnh: khe BỤNG rộng W=(c+r)+R + foot R). Xem [[web-hp-cutout]].

`d = b − dx` (chiều cao đoạn bulb) và `p = c + r − t` (bulb nhô khỏi web) dùng ở [[hp-end-at-hp]] (HP end tại HP).

Liên quan: [[bracket-identification]] (bụng/lưng → OB/IB), [[parametric-profiles]] (cơ chế profile tham số),
[[panel-parts-reference]] (HP = AM_3 màu 30), [[module-per-object]] (tách builder theo đối tượng),
[[web-hp-cutout]] (lỗ HP xuyên Web — dùng lại tiết diện HP).
