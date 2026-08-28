---
name: web-hp-cutout
description: "Cutout on Web cho HP" (chi tiết CO B — Cut Out Bulb-profile, CAS.dump.json) — chỗ HP xuyên NGANG qua tấm Web thì web có lỗ hình tiết diện HP để HP đi qua. Quy cách gap=0 + bo góc đáy R (TABLE 1). Đọc khi dựng 3D lỗ khoét web hoặc chỉnh WebHpCutoutBuilder.
---

# Cutout on Web cho HP — lỗ HP xuyên qua web (CO B)

Nguồn: `CAS.dump.json` (`c:\RoRoWF\...\ARAS\Template\CAS.dwt`, dump qua `MCG_DumpDrawing`) + 4 hình chi
tiết kỹ sư gửi (2026-08-28): **CO B** (Cut Out Bulb-profile), **TABLE 1**, **DET.A** (vát mép hàn),
**LUG B1R** (tấm collar). Block `HP120x6-scaloop` trong template chính là chi tiết CO cho HP120×6
(tên block gây nhầm — KHÔNG phải "scallop đầu HP").

## 1. Bản chất

HP là nẹp chạy dài; nơi nó **xuyên NGANG qua 1 tấm Web** (girder/khung ngang) thì Web phải có **LỖ
KHOÉT** hình tiết diện HP để HP đi qua liên tục. Trong bản dựng 3D:

- **HP** vẫn dựng LIỀN suốt chiều dài (`HpBuilder` — không đổi).
- **Web** (`PlateBuilder`) bị **TRỪ (BoolSubtract)** 1 "dao cắt" hình tiết diện HP tại mỗi chỗ giao.
- LỖ ≠ part riêng → `WebHpCutoutBuilder` gọi tường minh từ `Build3DService` SAU vòng lặp chính
  (cần solid Web sẵn) — CÙNG mô hình với `CutOutBuilder` ↔ Top Plate.

## 2. Quy cách hình học (xác nhận kỹ sư 2026-08-28)

Nguồn: block `HP120x6-scaloop` (CAS.dwt) — **hatch = phần lỗ khoét**. KHÔNG phải "ôm sát tiết diện HP"
(bản đầu làm sai) — mà là **khe rộng phía BỤNG + chân "foot" R lệch quanh bulb**.

Hệ trục KHỚP `HpSection`: gốc (0,0) = góc **LƯNG ∩ ĐẦU**; `+X` → BỤNG; `−Y` xuống. HP120×6 (b=120,
c=17, r=5, R=30): `W = (c+r)+R = 52`, `dUp = b−R = 90`.

```
 X=0(LƯNG)              X=W=(c+r)+R
Y=0   ●━━━━━━━━━━━━━━━━━●        ← mặt dưới Top Plate; mép LƯNG (X=0) trùng mặt LƯNG HP (gap 0)
      ┃                ┃
      ┃  phần TRÊN:    ┃         chữ nhật X∈[0,W]
      ┃  X∈[0,W]       ┃
Y=−dUp┃╌╌╌╌╌╌╌╌╌╌╌╌╌╌  ┃        ← dUp = b−R  (mức "90" HP120; block E8, chỉ là nét dựng)
      ╲   foot         ┃
(−R,−b) nửa cung R     ┃         foot = offset đoạn [(0,−b)→(c+r,−b)] ra bán kính R
Y=−b    ╲___________   ●        ← "120" = b ; cung ¼ R (tâm (c+r,−b)) nối lên mép BỤNG
Y=−(b+R)     ╲______╱           ← đáy phẳng
          X=−R      X=c+r
```

**Polygon 6 đỉnh (CW, `HpCutoutSection`):**

| # | (X, Y) | bulge (cạnh → kế) |
|---|---|---|
| 1 | (0, 0) | 0 |
| 2 | (W, 0) | 0 |
| 3 | (W, −b) | −0.4142 — cung ¼ R, tâm (c+r, −b) |
| 4 | (c+r, −(b+R)) | 0 |
| 5 | (0, −(b+R)) | −1.0 — nửa cung R, tâm (0,−b), qua (−R,−b) |
| 6 | (0, −(b−R)) | 0 → mặt LƯNG lên (0,0) |

| Điểm | Chốt |
|---|---|
| **Mép LƯNG** (X=0, Y∈[−(b−R),0]) | trùng mặt LƯNG HP — **gap = 0 phía lưng** |
| **Mép BỤNG** | `W = (c + r) + R` (xác nhận kỹ sư) |
| **R** | TABLE 1, cột R — `LookupCoRadius(b)` |
| **Chiều sâu lỗ** | = chiều dày web — cắt XUYÊN SUỐT (`extrude = webThk + 2·MARGIN`, MARGIN=5) |
| **HP kết thúc TẠI web** | **GIỮ NGUYÊN web, KHÔNG khoét** |
| **DET.A** (vát mép `AxA`/`BxB`, typical 10×10, khe chân 1–2mm) | vát mép HÀN — **BỎ QUA** trong solid |
| **LUG B1R** (collar dày `tl=tw`, cao `B1`, vát đỉnh 45°, `OL=30`/`tl×4`) | chi tiết RIÊNG — **NGOÀI phạm vi** |

### TABLE 1 — chỉ dùng cột **R**

Cột `h1, h2, B1, B2, B3, B4` = tham số của **LUG** → **KHÔNG dùng** cho lỗ khoét (xác nhận kỹ sư).
`B1 ≈ round5(H − min(H/4,100)) ≈ 0.75·H` = chiều cao LUG, ghi lại để tham khảo khi làm LUG sau.

| H | R | | H | R |
|---|---|---|---|---|
| 80 | 15 | | 240 | 35 |
| 100 | 20 | | 260 | 35 |
| 120 | 30 | | 280 | 35 |
| 140 | 30 | | 300 | 35 |
| 160 | 35 | | 320 | 35 |
| 180 | 35 | | 340 | 35 |
| 200 | 35 | | 370 | 35 |
| 220 | 35 | | 400 | 35 |
|  |  | | 430 | 35 |

`R = 15/20/30/30` rồi **35 CỐ ĐỊNH từ H≥160**. HP60 (không có hàng TABLE 1) → lấy mục H gần nhất = 80 → R=15.
Bảng bậc thang, **KHÔNG nội suy** (khác bảng c/r trong [[hp-profile]]).

## 3. Phát hiện chỗ khoét

`ClassifyUtil.CrossesThrough(hp, web, out tCross)` (đã có sẵn — dùng chung với `FlatBarSplitter`,
xem [[sniped-end]] mục 3e). `p = HP` (⇒ `tCross` đo dọc trục HP từ `e1`), `m = Web`:

- Loại **gần song song** (|cos| > 0.85) — HP chạy dọc cạnh web không phải "xuyên qua".
- Loại **T-junction** — Web kết thúc TẠI HP (đó là GirderEnd, không phải xuyên qua).
- `tCross` cách 2 đầu HP ≥ `FB_SPLIT_MARGIN` (20mm) — thêm kẹp **`tCross ∈ [b/2, L−b/2]`** trong
  `WebHpCutoutBuilder`: HP kết thúc gần web ⇒ không khoét.

1 HP xuyên nhiều Web / 2 HP xuyên 1 Web → vòng lặp lồng (Web × HP) xử lý hết. Web nhiều tầng dày
(web-tier) = nhiều `PanelPart` Web riêng → chỉ part CHỨA điểm giao bị cắt (các part collinear khác có
`s` ngoài `[marg, Lm−marg]` nên `CrossesThrough=false`).

## 4. Đặt dao cắt

CÙNG hệ trục local với `HpBuilder` để lỗ khớp đúng HP solid:
`local X → belly` (ép ⟂ axis), `local Y → +Z thế giới` (tiết diện y ≤ 0 ⇒ lỗ thõng xuống Z0),
`local Z → trục HP` = hướng extrude.

- Gốc tiết diện = mép **LƯNG** HP (local x=0) tại **điểm giao 2 đường tâm** (`e1 + (e2−e1)̂·tCross`),
  Z=0, lùi `½·span` dọc trục để quét đối xứng qua web. `span = webThk + 2·MARGIN`.
- `Region` từ `HpCutoutSection` → `AlignCoordinateSystem` → `CreateExtrudedSolid(axis·span)` →
  `sol.BooleanOperation(BoolSubtract, cutter)`.
- Thiếu `BellyDir` / tiết diện lỗi / web solid null → **bỏ qua cặp đó**, web giữ nguyên, ghi Report
  (resilient từng cặp — giống `CutOutBuilder`).

## 5. Cài đặt (2026-08-28)

- **`Services/HpProfileService.cs`**:
  - `CoRadiusTable` (17 hàng H→R) + `LookupCoRadius(b, out coR)` (static, như `LookupCR`).
  - `HpCutoutSection(b,t,c,r,coR)` (interface `IHpProfileService`) = polygon 6 đỉnh (mục 2), KHÔNG
    dùng `HpSection` (chỉ fallback về `HpSection` khi tham số vô lý). `t` chưa dùng.
- **`Services/Builders/WebHpCutoutBuilder.cs`** (mới, `internal static`, KHÔNG `IPartBuilder`):
  `ApplyWebCutouts(ctx, IReadOnlyList<(PanelPart web, Solid3d sol)>)` → `List<string>` khoá
  `"webHandle~hpHandle"` đã trừ thành công.
- **`Services/Build3DService.cs`**: gom `webSolids` trong vòng lặp (như `topPlateSolid`); sau vòng lặp
  gọi `WebHpCutoutBuilder.ApplyWebCutouts` + `FillGeometry` lại các web.

## 6. TODO / chưa chốt

- [x] ~~Hình dạng "ôm sát tiết diện HP + bo góc đáy"~~ — **SAI, đã sửa 2026-08-28** sang polygon 6 đỉnh
  (khe rộng BỤNG + foot R), sau khi kỹ sư gửi block `HP120x6-scaloop` + xác nhận `W = (c+r)+R`.
- [ ] **CHƯA test trực quan trong AutoCAD** — build 1 panel có HP xuyên Web (VD 7D-*), kiểm mắt:
  (1) lỗ đúng chỗ giao, xuyên suốt web; (2) hình khớp block `HP120x6-scaloop` (khe BỤNG rộng `W`,
  foot R lệch sau LƯNG); (3) mép LƯNG lỗ sát mặt LƯNG HP; (4) HP kết thúc tại web KHÔNG bị khoét;
  (5) không khoét nhầm khi HP chạy dọc cạnh web.
- [ ] **`W = (c+r) + R`** — xác nhận kỹ sư cho HP120 (52≈53). CHƯA đối chiếu cỡ khác (đặc biệt HP lớn:
  HP430 → W = 82+35 = 117). Nếu lệch → có thể `W` cố định hoặc công thức khác.
- [ ] **Ngưỡng `tCross ∈ [b/2, L−b/2]`** cho "HP kết thúc tại web" là phỏng đoán — chưa có số chốt.
- [ ] **Mép LƯNG lỗ trùng mặt LƯNG HP** (gap=0): 2 solid riêng, không boolean với nhau — chỉ trùng
  thị giác (= đường hàn). Nếu AutoCAD render z-fighting, cân nhắc lùi mép LƯNG lỗ vào 0.1–0.5mm.
- [ ] **`dUp = b − R`** (mức mở của foot) suy từ HP120 (E8 = 90 = 120−30). Chưa đối chiếu cỡ khác.
- [ ] **LUG B1R** (tấm collar) — chi tiết riêng, làm sau: solid MỚI dày `tw`, cao `B1` (TABLE 1),
  vát đỉnh 45°, overlap HP `OL=max(30, tw×4)`, hàn vào web+HP (dạng như Bracket/BackingPlate).

Liên quan: [[hp-profile]] (tiết diện `b,t,c,r` + bảng tra) · [[panel-parts-reference]] (mục 5a — CutOut
Top Plate, cùng mô hình boolean-trừ) · [[sniped-end]] (mục 3e — `CrossesThrough` dùng chung) ·
[[parametric-profiles]] (pattern Region→Extrude→AlignCoordinateSystem).
