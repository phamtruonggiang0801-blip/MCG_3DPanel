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
- [ ] **LUG B1R** (tấm collar) — xem **§7**. Quy tắc + hình học đã chốt qua Hatch; `WebCollarPlateBuilder`
  ĐÃ CÀI (2026-08-28) — **CHƯA test trực quan**; còn 1 mâu thuẫn B1 cần kỹ sư xác nhận (§7c).

## 7. LUG B1R — Collar Plate (đã cài 2026-08-28, chưa test)

Tấm bù (compensation / collar plate) hàn quanh lỗ CO nơi HP xuyên web — khôi phục tính liên tục của web.
Nguồn: **Hatch `1F3F2`** (`layer DM4_STEEL_CUTOUT_RASTER`, `*Model_Space` của `CAS.dump.json`, HP120) —
biên polyline 9 đỉnh = mặt cắt THẬT tấm collar. Hình "LUG B1R (LUG FOR BULB-PROFILE)" + TABLE 1 tham khảo.

### 7-bis. Ép tay theo HP (kỹ sư 2026-11-05)

Tab Result, dòng HP: dropdown **Collar: Auto / On / Off** (`PartCorrection.Collar`, khoá = SourceKey HP). **On** = collar ở
MỌI lỗ CO của HP (mọi web nó xuyên); mặt web về phía junction gần nhất trên cùng web dọc, không có junction ⇒ về
tâm panel. **Off** = bỏ hết collar của HP. KHÔNG tính lại Auto của HP khác. `WebCollarPlateBuilder`: `Force()` +
danh sách `forced` xử lý sau vòng run; `BuildOne(..., sideRef, ...)`.

### 7a. Quy tắc VỊ TRÍ (xác nhận kỹ sư 2026-08-28)

Note gốc:

```text
ADD COLLAR PLATE IF WEB HEIGHT IS LESS THAN 300mm
AND AT EACH TRANSVERSE/LONGITUDINAL WEB ON THE OUTMOST 2 HP SLOTS
AND AT THE PARTIAL TRANSVERSE WEB WHICH IS IN LINE WITH CORNER SUPPORTS
```

Diễn giải đã chốt với kỹ sư (2026-08-28) — **3 dòng note = 3 rule CỘNG THÊM, ĐỘC LẬP** (KHÔNG phải cổng chung):

- **Rule chính (note 2) — LUÔN áp dụng, KHÔNG phụ thuộc chiều cao web:** HP (chạy NGANG) xuyên qua
  **Web dọc**. Tại mỗi chỗ một **Web ngang** (chạy ∥ HP) giao vào Web dọc, chỉ **2 lỗ HP gần điểm giao
  đó nhất** (trên Web dọc) được thêm 1 Collar Plate. HP thứ 3 trở đi (xa hơn) — **chỉ cutout, KHÔNG collar**.
- **note 1 ("web < 300"):** kỹ sư chốt "vẫn chỉ 2 HP quanh junction" ⇒ với tool hiện tại (1 `WebHeight`
  chung) **KHÔNG thêm gì** so với rule chính → builder **BỎ hẳn gate WebHeight**. Ghi lại cho tương lai:
  khi có H riêng từng web, web < 300 → collar ở **MỌI** lỗ HP của web đó.
- **note 3 (partial transverse web ∥ corner support):** **BỎ tạm thời** — block Plan 2D không chứa
  corner support / cột chống; cũng chưa có cờ "web cụt (partial)". Ghi lại, làm khi có dữ liệu.
- Collar là **1 tấm** (không phải 2), nằm **phía BỤNG HP**, dày **`tl = tw`** (chiều dày Web dọc).

Cài đặt (`Services/Builders/WebCollarPlateBuilder.cs`, `internal static`, KHÔNG `IPartBuilder` — như
`WebHpCutoutBuilder`): `BuildCollarPlates(ctx)` → `List<(Solid3d sol, string tag)>` (đã set Layer/Color
theo web dọc). `Build3DService` gọi SAU `WebHpCutoutBuilder.ApplyWebCutouts` → `btr.AppendEntity` +
ghi Report `[Collar] solid <handle> = <tag>` + `BuiltPartInfo.Code = tag` (truy vết "vì sao có collar này":
web dọc × HP × web ngang × phía L1/L2/R1/R2 × jpos/s).

- `web dọc` = **web run** (gộp tier) — `RunSpan` = đoạn tâm đầy đủ + trục (spine = tier dài nhất).
- lỗ CO = `ClassifyUtil.CrossesThrough(hp, tierPart, tCross)` + kẹp `tCross ∈ [b/2, L−b/2]` (HP kết thúc
  tại web → bỏ, giống `WebHpCutoutBuilder`).
- `web ngang` = web run KHÁC, `|cos(trục)| ≤ 0.34` (~⟂), đoạn tâm 2 run cắt nhau (`LineIntersect`) tại
  điểm: (a) trong `[−0.03, 1.03]·len` web DỌC (cho phép junction ở đầu mút web dọc = khung kết thúc tại
  girder); (b) **TRONG thân web NGANG** (`sNgang ∈ [60, len−60]`) — web ngang phải ĐI XUYÊN, KHÔNG phải
  kết thúc tại web dọc (girder ngang chạm khung biên ⇒ loại). Fix 2026-08-28 sau khi kỹ sư báo collar sai.
- **slot cap**: lỗ HP chỉ nhận collar nếu `|s − jpos| ≤ 2.6× bước HP median` (sàn 2200mm) — tránh "lỗ gần
  nhất" thực ra cách junction hàng mét (web dọc grouped span dài, đoạn quanh junction không có lỗ).
- mỗi junction → `SLOTS_PER_SIDE = 2` lỗ HP liền kề **MỖI PHÍA** (2 lỗ `s ≤ jpos` gần nhất + 2 lỗ
  `s > jpos` gần nhất). KHÔNG phải "N gần nhất theo |Δ|" (2 web ngang sát nhau ⇒ bỏ sót HP ngoài).
  Dedupe `HashSet<"webHandle~hpHandle">` (HP chung 2 junction chỉ dựng 1 lần). VD 2 web ngang A,B
  chung HP_giữa: A→{HP_trái1,HP_trái2,HP_giữa,HP_phải1}, B→{HP_giữa,HP_trái1,HP_phải1,HP_phải2} ⇒
  union {trái2,trái1,giữa,phải1,phải2}.

### 7b. Kích thước hình học — `HpProfileService.CollarPlateSection` (KHỚP 1:1 Hatch `1F3F2`)

Hệ trục KHỚP `HpCutoutSection` (gốc = LƯNG∩ĐẦU HP, +X→BỤNG, −Y xuống). HP `b`, `c`, `r`, `t` (dày thân);
`R = coR` (`LookupCoRadius(b)` — theo **bulb**); `W = (c+r)+R`; `HC = 10` (vát gót 10×10);
`B1 = LookupB1(b)` = TABLE 1 cột B1 **tra theo b (HP height)** — xác nhận kỹ sư: "HP120 ⇒ B1 = 90".

Polygon **8 đỉnh CCW** (bulge dương, đúng thứ tự Hatch). HP120 (b120,c17,r5,coR30 ⇒ W52, B1=90) khớp Hatch:

| # | (X, Y) HP120 | công thức | bulge→kế |
|---|---|---|---|
| 1 | (83, −60)      | `(W+R, −B1+R)`               | 0 |
| 2 | (83, −52.4)    | `(W+R, −(HC+R√2))`           | +0.1989 (¼ cung R: mép bụng ↔ vát 45°) |
| 3 | (74.2, −31.2)  | `(W+R/√2, −(HC+R√2)+R/√2)`   | 0 (đường vát 45°) |
| 4 | (43, 0)        | `(W−HC, 0)`                  | 0 (top-flat trên mặt dưới Top Plate) |
| 5 | (16, 0)        | `(t+HC, 0)`                  | 0 (vát gót 10×10) |
| 6 | (6, −10)       | `(t, −HC)`                  | 0 (mép GÓT = mặt bụng thân HP) |
| 7 | (6, −90)       | `(t, −B1)`                   | 0 (đáy collar) |
| 8 | (53, −90)      | `(W, −B1)`                   | +0.4142 (¼ cung R góc bụng-đáy) |

Đáy collar `Y = −B1` (HP120: −90 — trùng `−(b−R)` chỉ do coR≈b/4; HP khác thì `B1 ≠ b−R`). **KHÔNG có
"váy"** kéo dưới B1. Tấm KHÔNG chồng thân/bầu HP (mép gót `X=t`) → **KHÔNG cần trừ lỗ**. Extrude `tl`
dọc trục HP, áp 1 MẶT web (phía web ngang), ra ngoài. Mép bụng `X = W+R` (lấn qua mép lỗ CO ~`R`).

### 7c. CHƯA chốt / cần test

- [x] ~~MÂU THUẪN B1~~ **GIẢI QUYẾT 2026-08-28**: TABLE 1 cột "H" = **chiều cao HP** (giống cột R /
  `CoRadiusTable`), KHÔNG phải chiều cao web. HP120 ⇒ B1 = 90 = đáy Hatch. `CollarPlateSection` dùng
  `−B1` (= `LookupB1(b)`); HP120 trùng `−(b−R)` chỉ do trùng số. Hình "LUG B1R": "H" trong hình cũng
  là chiều cao HP (`B1 + H/4 = H` ⇒ `B1 = 0.75·HP`, `H/4` = vùng foot dưới B1). Không có váy riêng.
- [x] ~~Gate WebHeight~~ **BỎ 2026-08-28** — kỹ sư chốt 3 rule cộng thêm độc lập, rule "2 HP quanh
  junction" LUÔN áp dụng bất kể chiều cao web. (Lần build đầu 28/08 bị gate `WebHeight=374 ≥ 300` chặn
  → 0 collar; đã bỏ.)
- [ ] **CHƯA test trực quan** — build lại panel HP-xuyên-Web bất kỳ. Kiểm: (1) collar đúng 2 lỗ quanh
  junction web-ngang; (2) hình khớp Hatch (mép gót sát mặt bụng thân HP, mép bụng lấn web ~R, vát
  45°+10×10, đáy tại −B1); (3) đúng 1 mặt web, không đè HP; (4) report
  `[WebCollarPlateBuilder] N web run · M lỗ CO · K junction → P Collar Plate`.
- [ ] **Junction web-ngang chưa test** — logic `LineIntersect` + kẹp `[−0.03,1.03]` +
  `dOnNgang ≤ 2·max(thk,ADJ_TOL)` chưa chạy trên panel thật.
- [ ] **Mặt nào của Web dọc** — code chọn mặt về phía **Web ngang** (`(wn.center−cross)·hpAxis`). Giả
  định — cần xác nhận.
- [x] ~~"2 lỗ gần nhất theo |Δ|"~~ **SỬA 2026-08-28 (2 lượt)**: (1) → 1 lỗ liền kề mỗi phía; (2) kỹ sư
  chốt **2 lỗ mỗi phía** (`SLOTS_PER_SIDE=2`). Junction đơn lẻ → 4 collar; 2 junction sát nhau → 5
  (xem VD §7a). Chưa xác nhận junction đơn lẻ có nên là 4 hay 2.
- [ ] **`W = (c+r)+R`** dùng chung với lỗ CO (skill §2, HP120 52≈53). Cỡ khác chưa đối chiếu.
- [ ] TABLE 1 cột `h1, h2, B2, B3, B4` — biến thể LUG khác (B2R/B3R/B4R?); note chỉ yêu cầu **B1R**.
- [x] `MCG_DumpDrawing` xuất biên Hatch (`DrawingDumpCommand`, nhánh `Hatch` → `loops[]`) — 2026-08-28.
- [x] `HpProfileService.CollarPlateSection` + `WebCollarPlateBuilder` + wire `Build3DService` — 2026-08-28.

Liên quan: [[hp-profile]] (tiết diện `b,t,c,r` + bảng tra) · [[panel-parts-reference]] (mục 5a — CutOut
Top Plate, cùng mô hình boolean-trừ) · [[sniped-end]] (mục 3e — `CrossesThrough` dùng chung) ·
[[parametric-profiles]] (pattern Region→Extrude→AlignCoordinateSystem).
