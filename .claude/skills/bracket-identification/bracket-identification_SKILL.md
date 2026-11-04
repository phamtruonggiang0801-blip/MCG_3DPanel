---
name: bracket-identification
description: Đặc tả nhận diện Bracket (OB/IB/FB) và KÍCH THƯỚC 3 leg của bracket knee (top-plate/flange/web). Đọc khi làm logic phân loại Bracket hoặc dựng hình bracket. Đã kiểm chứng trên 7D-09C_Assy.
---

# Bracket Identification — OB / IB / FB + kích thước leg

## 1. Nhận diện OB (ĐÃ KIỂM CHỨNG — thực đo 7D-09C_Assy: **20 OB**, 0 IB)

Trên layer Web `Mechanical-AM_0`, xét Closed LWPolyline theo **chiều dài** và **có CHẠM HP/FlatBar không**:

| Điều kiện (AM_0) | Loại |
|---|---|
| Dài (len > ~1000) | **Web chính** |
| Ngắn (len < ~1000) **và** CHẠM HP/FlatBar (adj bbox ~12mm) | **Bracket** (OB/OB1/FB — MỌI bậc dày) |
| Ngắn **và** KHÔNG chạm HP/FB (đoạn web inline) | **Web-tier** (đoạn web đổi chiều dày — vẫn là Web) |

**QUAN TRỌNG — discriminator (ĐÃ SỬA lại lần 2):** Phân biệt Bracket vs Web-tier bằng **CHẠM HP/FlatBar**, KHÔNG bằng chiều dày.
- ❌ Bản cũ dùng "thk6 = bracket" → **OB1 (bracket dày 10) bị nuốt thành web-tier** (dựng chữ nhật). Đã bỏ.
- ❌ KHÔNG dùng collinear: nhiều OB (334/399) collinear/nối đầu web NHƯNG vẫn là OB. Web-tier & OB đều có thể collinear;
  chỉ khác **web-tier KHÔNG chạm HP/FB**, OB thì có.
- ✅ Thực đo 140753: 26 bracket (21 thk6 + 5 thk10 OB1: len 200/470×2/520×2 — đều adjHP), 4 web-tier (thk10 len300 — không adjHP).
- ⚠️ Rủi ro: web-tier ở cạnh HP có thể adjHP giả. Dữ liệu hiện tại tách sạch; nếu gặp panel sai, thêm điều kiện "trục dài vuông góc web" (bracket chĩa ra HP) hoặc "không collinear với long-web THẬT sát".

Thực đo 7D-09C (inspect 2026-07-22): 38 polyline AM_0 = 9 web dài + **21 OB (thk6: 334×5, 399×5, 474×5, 524×5, 500×1)** + 8 web-tier (thk10).
(Bản cũ nhận nhầm chỉ 10 OB vì gộp 334/399 vào web.) **Panel này 0 IB** — cái 500 lẻ cũng là OB (ốp lưng HP).

## 2. Kích thước bracket knee (hình thang đứng, elevation)

Bracket OB = tấm knee đứng hình thang, 3 cạnh:

| Leg | Nghĩa | Kích thước |
|---|---|---|
| **Leg trên** (Top Plate, Z=0) | span / độ vươn | = **chiều dài plan** của piece (334–524) |
| **Leg dưới** (Flange, Z=−H) | chân trên flange | = **(rộng Flange − dày Web)/2 − 15** |
| **Cạnh Web** (đứng) | chiều cao | = **Web height H** |
| Cạnh chéo tự do | nối đầu leg-trên → đầu leg-dưới | (+ toe bo, flange notch cố định) |

**Công thức leg-on-flange (ĐÃ SỬA — xử lý flange LỆCH):**
`F = overhang − 15`, với **overhang = flange (gần nhất) vươn xa nhất theo hướng bracket (memberDir) từ anchor**.
- KHÔNG dùng `(flangeWidth − webThk)/2` — công thức đó chỉ đúng khi flange **đối xứng** qua web. Nếu flange
  **lệch** (vd một bên 15, bên kia 172), phải lấy overhang **phía bracket** (chiếu 4 góc bbox flange lên memberDir,
  lấy max). Cài: `OverhangProj` (Build3DService).
- Flange 120 đối xứng → overhang 57 → F=42. Flange 350/193 → phía dài 172 → F=157.
- **overhang < 50 → under-50** (F=15, đẩy chân cao cách flange 30 — không hàn). Vd flange lệch phía 15mm.
- Thực đo 115851: S=700 (flange lệch 15/172, bracket phía 15) → **overhang=15 → UNDER-50** ✓.

→ overhang tính theo hình học thật (anchor=đầu chạm Web, memberDir=hướng ra HP/mép). Xem [[parametric-profiles]].

⚠️ **2026-11-04 — đo tại 2 MẶT bản thép, lấy MIN** (`BracketBuilder.FaceMinHit`): flange có mép **XIÊN** so với
trục member thì mép tại 1 mặt bản thép gần hơn tại đường tâm ~`lệch / tan(góc)` (flange 24°: ×2.2) ⇒ đo chỉ đường
tâm làm toe không lùi đủ 15, thậm chí lòi ra ngoài mép (bracket B `20E72` trên flange xiên `1B637`). Áp dụng:
OB/IB/GE (`OverhangProj`: tia tại −t/2, 0, +t/2) và bracket B (`FlangeOverhang`: tại 2 mặt thật — sym `[−t/2,+t/2]`,
không sym `[+t/2,+3t/2]` theo bụng HP). Flange ⟂ trục member: kết quả không đổi.

**Điều kiện GẤP LIP (free-edge — ĐÃ SỬA):**
`free-edge = √((S−F)² + (Huse−105)²)` — khoảng cách **toe-tới-toe**: toe-HP ở `(S,−105)`
(đáy vùng flange đầu span, Z=−105 **cố định** theo template — KHÔNG phải Z=0), toe-flange ở `(F,−Huse)`.
- ⚠️ KHÔNG dùng `√((S−F)²+Huse²)` (góc-tới-góc) — sai vì toe-HP không ở đỉnh.
- **Gấp lip khi free-edge ≥ 350 (OB) / ≥ 600 (OB1); nhỏ hơn → KHÔNG gấp.**
- Thực đo 135349: S=200 under-50 → √(185²+239²)=**302 < 350 → NO BEND** ✓. S=334 → 322 (no bend); 399→362, 474→509, 524→552 (bend).

## 3. FB vs OB/IB — theo ĐẦU KIA của bracket

- Đầu kia chạm **Flat Bar member** (AM_3, ≠màu30) → **FB (Flatbar Bracket)**.
- Đầu kia chạm **HP** (AM_3, màu30) → **OB / IB**.
- Adjacency bbox (tol ~12mm).

⚠️ **Mã CODE THẬT trên `PanelPart.Code` là `"BF"`, KHÔNG PHẢI `"FB"`** (`BracketCoder.cs` đổi tên
2026-08-25, "FB" chỉ còn là tên gọi/label trong tài liệu này + tên block `BF_type1/2`). Bug đã xảy ra
thật: `BracketBuilder.CanBuild`/`KneeDtop` còn kiểm `StartsWith("FB")` theo tài liệu cũ → mọi bracket
FlatBar bị `CanBuild=false`, rơi về hộp chữ nhật (`PlateBuilder` fallback) thay vì hình knee thật — sửa
bằng cách đổi 2 chỗ đó sang `StartsWith("BF")` (2026-08-25). **Nếu sửa/thêm code liên quan tới bracket
FlatBar, LUÔN kiểm tra đúng chuỗi `"BF"` trong `BracketCoder.cs`, không tin theo label "FB" trong skill.**

## 4. CHIỀU ĐỔ kết cấu theo loại Panel (dùng cho HƯỚNG GẤP lip + OB/IB)

**Trục X: LUÔN trái → phải (+X) cho MỌI loại panel.** Trục Y khác nhau:

| Loại Panel           | Trục Y                                    | Trục X       |
|----------------------|-------------------------------------------|--------------|
| Panel Center         | trên & dưới đổ **về GIỮA** `sign(cY−pY)`  | trái → phải  |
| Panel Port Side      | **trên → dưới** (−Y)                       | trái → phải  |
| Panel Starboard Side | **dưới → lên** (+Y)                        | trái → phải  |

Cài `BendFoldSign` (hướng gấp lip): `chiềuĐổ = (dozX=+1, dozY)`; so `normal_tấm · chiềuĐổ` → chọn local Z ±.
⚠️ X KHÔNG phải "về tâm" — luôn +X (bản cũ nhầm nên lip gấp sai, đã sửa).

### OB vs IB — ĐÃ BẬT (`ObOrIb`/`BellyDir`), kiểm chứng trên panel thật

- OB = bracket ốp phía **LƯNG** HP (mặt phẳng); IB = ốp phía **BỤNG** (mặt có bulb).
- Test: `s = (brk.Centroid − hp.Centroid) · bellyDir`; **s ≥ 0 → IB**, ngược lại **OB**.
- **HP xét = HP gần ĐẦU TỰ DO của bracket** (đầu xa Web) — không phải nearest-centroid. Cài `FreeEnd`/`NearestPart`.

**`BellyDir` (2 nhánh):**

| HP | Bụng |
|---|---|
| **NGOÀI CÙNG** theo trục vuông góc (min/max, tol 50) | **RA NGOÀI** (xa tâm panel) — *override, mọi panel type* |
| **bên trong** — HP đứng (chạy Y) | **+X** (chiều đổ X luôn +1) |
| **bên trong** — HP ngang (chạy X) | Center: `sign(cY−hpY)` (về giữa) · **Port: −Y** · **Starboard: +Y** |

- ✅ **Kiểm chứng** (inspect 20260723_113742, 25 bracket): **Center → 25 OB / 0 IB** (khớp user xác nhận),
  **Port → 24 OB / 1 IB** (bracket len500 tại ~(78759,9592)), **Starboard → 25 OB / 0 IB**.
- ⇒ **Đổi Panel Position ở combo box CÓ đổi mã**: chỉ HP **ngang BÊN TRONG** đổi bụng theo position
  (HP ngoài cùng bị override, HP đứng luôn +X) nên số IB đổi ít — đúng vật lý.
- ⚠️ Chỉ có ground truth cho **Center (0 IB)**. Port/Starboard chưa có panel thật đối chiếu — cần calibrate khi có.

## 4b. BỘ QUY TẮC 4 LOẠI BRACKET (OB/IB/FB + hậu tố dày) — trích block explode OB-OB1.dwg

**TOE/BEND GIỐNG HỆT NHAU cho CẢ 4 LOẠI** (đã đo từng điểm trên block):
`toe = R30 (cắt lõm) → đoạn 18mm (∥ free-edge) → Rb → đoạn dày(⊥, = thk) → free-edge (bend)`;
`Rb = 2·thk` (12@thk6 / 20@thk10); R30 tâm trên đường leg (top `(S,−Dtop−30)`, bot `(F+30,−H)`).
→ Dùng CHUNG `ProfileService.FlangedToe` (xem [[parametric-profiles]]). **Chỉ khác 3 điều: member đầu kia, Dtop, wrap.**

| Loại | Nối | Nhận diện đầu kia | **Dtop** (mặt span) | ledge | Block ref (S/H/F) |
|---|---|---|---|---|---|
| **OB / OB1** | Web↔**HP lưng** | HP (AM_3 màu30), phía **lưng** (mặt phẳng) | **HP−15** | 0 | OB_type1 594/374/149 · OB_type2 547/344/15 |
| **IB / IB1** | Web↔**HP bụng** | HP, phía **bụng** (mặt có bulb) | **HP−30** | **15** | IB_type1 769/374/135 · IB_type2 (flip) |
| **FB / FB1** | Web↔**Flat Bar** | Flat Bar (AM_3 **màu≠30**) | **FlatBar−15** | 0 | BF_type1 547/374/85 · BF_type2 547/344/15 |

- **Dtop ĐỘNG theo member** (KHÔNG cố định 105): HP120 → OB Dtop=105, IB=90, FB(FlatBar120)=105. Cài `KneeDtop` (Build3DService).
  Toe-HP dùng Dtop này (free-edge = √((S−F)²+(Huse−Dtop)²)).
- **IB ledge**: đoạn ngang **15mm** ở đầu span TRƯỚC R30 (bọc bulb). Outline: `(S,0)→(S,−Dtop)→(S−15,−Dtop)[R30]`.
  Tâm R30-top = `(S−15, −Dtop−30)`, tiếp tuyến ledge NGANG (OB tiếp tuyến mặt ĐỨNG). Cài trong `FlangedToe`/`R30Only`.
- **Hậu tố "1" = thk10**. `Rb`=2·thk, đoạn-dày=thk, ngưỡng bend 350→600 tự scale (xem [[parametric-profiles]]).
- **FB đã CÀI** (Dtop=FlatBar−15). **IB đã cài hình** (Dtop=HP−30 + ledge15) nhưng classification IB (belly) còn TẮT.
- **under-50** (hiếm): F=15, đáy nâng 30. Hiện dùng template thk6/Dtop105 (`TemplateUnder50`) — CHƯA theo Dtop động.
- ⚠️ **IB Dtop:** user rule = **HP−30** (HP120→90). Block IB_type1 lại vẽ Dtop=**71.3** (=HP_web 101.3 − 30, dùng chiều cao WEB
  chứ không phải HP tổng). Đã theo **rule user (HP−30)**; chờ panel IB thật để chốt (HP tổng hay HP web).
- **Panel 7D-09C: 0 IB, 0 FB** → rule từ block thư viện, chờ panel thật calibrate.

## 5. Hậu tố số (OB / OB1…) = theo bậc chiều dày

- `ThicknessSuffix`: 6→"" · 10→"1" · 15→"2" · 20→"3" (chờ xác nhận bậc khác). Panel này OB toàn dày 6 → mã "OB".

## 6. Checklist

- [x] Nhận diện OB = AM_0 ngắn + thk6 (KHÔNG gộp collinear vào web). ✔ (20 OB)
- [x] Leg-on-flange = (flangeW − webThk)/2 − 15. ✔
- [x] Leg-on-topplate = span (chiều dài plan). ✔
- [x] Chiều cao = Web height. ✔
- [ ] Map mỗi OB → flange tương ứng để lấy flangeWidth (v1: nearest flange).
- [ ] IB: chờ panel mẫu có IB.
- [ ] FB / OB1 / thk10-bracket: phân biệt web-tier vs bracket khi cùng thk10.

Liên quan: [[panel-parts-reference]] (Web/Bracket/Flange), [[parametric-profiles]] (generator knee 3 leg S/H/F).
