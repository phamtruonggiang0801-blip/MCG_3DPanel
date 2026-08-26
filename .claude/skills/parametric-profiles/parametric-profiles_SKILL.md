---
name: parametric-profiles
description: Cơ chế định nghĩa PROFILE THAM SỐ (biên dạng 2D) cho Bracket/HP… để dựng 3D đúng hình thái thay vì hình chữ nhật. Đọc khi làm Build3DService cho các loại có hình dạng đặc biệt.
---

# Parametric Profiles — biên dạng 2D tham số

Một số đối tượng KHÔNG phải hình chữ nhật đơn giản khi dựng 3D:
- **HP**: tiết diện bulb (Holland Profile) — sweep dọc chiều dài.
- **Bracket** (OB/OB1/IB/FB…): tấm phẳng nhưng MẶT (elevation) là hình gusset (vát góc, bo cung, snipe).

Thay vì extrude hình chữ nhật, ta dựng **biên dạng thật** rồi extrude/sweep theo chiều dày (bracket)
hoặc theo chiều dài (HP).

> Trạng thái: **ĐÃ CHỐT hướng = DWG profile-library** (user vẽ biên dạng thành block, tool đọc + scale
> lúc chạy). **Làm Bracket trước.** Chờ user gửi bản vẽ bracket để chốt quy ước + tham số.

## 0. Hướng đã chọn: DWG PROFILE-LIBRARY

- 1 file DWG thư viện (đề xuất `C:\CustomTools\Autocad\MCG_Profiles.dwg`) chứa **mỗi loại 1 block**.
- Tên block theo quy ước: `MCG_BR_OB`, `MCG_BR_IB`, `MCG_BR_FB`… (hoặc 1 block generic nếu cùng hình).
- Trong block: **1 Closed LWPolyline** = biên dạng gusset, vẽ ở **mặt phẳng elevation**
  (X = dọc member, Y = lên theo Z). Gốc (0,0) tại **điểm neo** quy ước (vd góc Web∩đáy).
- Tool: đọc block → **scale** về kích thước bracket thật (L từ plan, H từ Web height) → xoay/tịnh tiến
  về vị trí bracket → extrude theo chiều dày.
- **Cần chốt kiểu scale:** proportional (scale X,Y độc lập — méo cung/vát) HAY giữ vài dim cố định
  (vát/bo hằng số) — tùy hình bracket thật của user.

## 1. Định dạng template (đề xuất)

Mỗi profile = 1 outline kín, toạ độ mỗi đỉnh là **công thức theo tham số**:

```json
{
  "name": "BracketOB",
  "plane": "elevation",        // mặt phẳng chứa (dọc-member) × (Z lên)
  "extrudeBy": "thickness",    // bracket: extrude theo chiều dày; HP: "length"
  "params": {
    "L": "derived: chiều dài footprint trong plan",
    "H": "derived: chiều cao Web",
    "c": 50,                   // fixed: vát góc
    "R": 30                    // fixed: bán kính bo
  },
  "outline": [
    { "x": "0",   "y": "0" },
    { "x": "L",   "y": "0" },
    { "x": "L",   "y": "H-c" },
    { "x": "L-c", "y": "H",   "bulge": -0.4142 },   // cung 1/4 (nếu có)
    { "x": "0",   "y": "H" }
  ]
}
```

- **params**: `derived` = suy từ panel/member (L từ plan, H từ Web height…); số = **fixed** (theo bản vẽ gốc).
- **outline**: đỉnh theo thứ tự, `x`/`y` là biểu thức theo params; `bulge` cho cung (tùy chọn).
- Tool evaluate params cho từng bracket cụ thể → dựng LWPolyline biên dạng → đặt vào mặt phẳng đứng
  của bracket (theo hướng member) → extrude theo chiều dày.

## 2. Trục local của profile

- **x** = dọc theo member (hướng dài của bracket trong plan).
- **y** = hướng **lên** (trục Z thế giới), gốc y=0 ở mặt dưới Top Plate (Z=0), tăng xuống hay lên tùy quy ước.
- Tool tự xoay/tịnh tiến profile về đúng vị trí+hướng của bracket (lấy từ plan) rồi extrude chiều dày.

## 3. Cần user cung cấp (để mã hóa template)

Với MỖI loại có hình đặc biệt (bắt đầu từ **Bracket**):
1. **Bản vẽ 2D generic** (ảnh hoặc DWG) của biên dạng, có **ghi kích thước + tên tham số**.
2. Tham số nào **fixed** (hằng theo bản vẽ) vs **derived** (suy từ panel: H=Web height, L=plan length…).
3. Có cung/bo/vát không (để set `bulge`).
4. Điểm neo: cạnh nào áp vào Web, cạnh nào áp vào HP/Top Plate.

Sau khi có → mã hóa vào file data (skill này hoặc `Profiles/*.json`) + viết `ProfileService` sinh biên dạng.

## 5. Bracket OB / OB1 — trích từ OB-OB1.dwg (mục tiêu: ĐẦY ĐỦ CHUẨN)

Bản vẽ = 2 standard detail cạnh nhau (đọc qua lệnh `MCG_DumpDrawing`). Outline bracket ở layer
`Mechanical-AM_0`; AM_8 = đường offset chiều dày; AM_4 = mặt Web; AM_7 = center mark; AM_5 = dim/nhãn.

| Thuộc tính | OB | OB1 |
|---|---|---|
| Chiều dày tấm (MTEXT) | **6.0** | **10.0** |
| Flanged IF cạnh ≥ | **350** | **600** |
| Rb min (bo góc) | 12 / 20 (đo R19/R8) | 12 / 20 |
| Giới hạn chiều dài | **L < 775** | L < 775 |

**Hình thái:** tấm hình **L (knee/gusset)** — 1 leg áp **Web**, 1 leg áp **Top Plate**, góc tự do **bo cung**
(toe: 2 cạnh vuông nối bằng arc, thấy ở dump cạnh 45×75 + R19). Khi cạnh ≥ ngưỡng → thêm **flange**
(tiết diện L, 2 chân ~82, bulge ±0.414 = cung 90°).

**Quy tắc footprint bracket trong plan (user cung cấp):**
- Footprint = (khoảng cách từ **mặt Web gần nhất** → **mép Flange**) **− 15mm**.
- **Nếu** khoảng cách đó **< 50mm** → footprint = **15mm**, đặt **cách flange 30mm** lên.
  (Khớp dim "IF<50"=30 và các dim 15 trong dump.)

**GENERATOR 3 THAM SỐ (S, H, F) — DỰNG PARAMETRIC (KHÔNG dùng template cứng nữa):**

Bracket knee cần **3 tham số độc lập**, hệ toạ độ local: (0,0)=Web∩TopPlate, +X dọc TopPlate, −Y=xuống:
- **S** = leg-on-TopPlate (cạnh trên, y=0) = chiều dài plan (334–524).
- **H** = chiều cao (cạnh đáy ở y=−H). Truyền vào là **Huse** (over50=WebH; under50=WebH−30).
- **F** = leg-on-Flange (cạnh đáy) = **overhang−15** (overhang tính projection — xem [[bracket-identification]]).

**2 CHẾ ĐỘ TOE theo FLANGED (có gấp lip/bend hay không) — ĐÃ KIỂM CHỨNG block explode:**
- **FLANGED** (free-edge ≥ 350 OB / 600 OB1 → có bend): toe =
  **R30 (cắt lõm vào thân) → đoạn thẳng 18mm (∥ free-edge) → Rb → đoạn DÀY (⊥ free-edge, = thk) → free-edge (bend)**.
  **SCALE theo chiều dày:** `Rb = 2·thk` (12@thk6 / 20@thk10), đoạn-dày = `thk` (6@thk6 / 10@thk10). Đoạn 18mm cố định.
  Cài `FlangedToe` (ProfileService, PARAMETRIC — khớp block `OB_type1` thk6 từng điểm, tự đúng cho OB1 thk10).
  13 đỉnh: `(0,0)(S,0)(S,−Dtop)[R30] tR30T [18mm] p1T[Rb] p2T [dày] p3T [free] p3B [dày] p2B[Rb] p1B [18mm] tR30B[R30] (F,−H)(0,−H)`.
  - Đoạn-dày = **chiều dày tấm**, ⊥ free-edge (dot=0); sau đó free-edge = đường FOLD (TryAddBendLip đắp lip dọc đây).
  - under-50 flanged (hiếm): giữ **template** `OB_type2` (thk6) — `TemplateUnder50`.
- **KHÔNG flanged**: **chỉ R30** (không Rb/đoạn). Cài `R30Only`.
- Build3DService tính `flanged = freeEdge ≥ bendMin` + truyền `thk` → `BracketOutline(...,thk,flanged)`.
- ⚠️ **squareCorner** (xác nhận kỹ sư, 2026-08-25): khi **Flange/FacePlate đỡ vươn dài hơn cả bracket**
  (`overhang−15 > S−R30`, tức `F` bị kẹp trần `S−30`) → **bỏ R30 dưới**, hạ **THẲNG ĐỨNG** từ tiếp điểm
  R30 trên xuống đáy, gặp đáy tại **góc vuông 90°** (không "ôm" theo `F` nữa). Toe trên (R30 gần
  HP/span, `Dtop`) **giữ nguyên không đổi** — chỉ Toe-end Flange (dưới) đổi. Quy tắc **chỉ áp dụng
  GE + Bracket** (2 loại có khái niệm toe/overhang) — **KHÔNG áp dụng ClosedPlate/BackingPlate/Plate**
  (các loại này extrude thẳng, không có toe). Cùng nguyên lý với GE (xem [[girder-end]] mục 6c) —
  `ProfileService.R30Only` nhánh `squareCorner`: `u=(0,−1)`, tiếp điểm = `cTop + (−R,0)`, rồi thẳng
  đứng xuống `y=−H`. Kiểm tay (S=594,Dtop=105,F kẹp 564): tiếp điểm `(564,−135)` → thẳng xuống
  `(564,−374)` → `(0,−374)`, bulge góc trên `0.414` (cung 90° chuẩn) — không tự cắt, không vượt đáy.

**NGUYÊN LÝ TOE-END R30 (dùng cho cả 2 chế độ — kiểm chứng block explode, khớp 1:1):**
- **Mỗi toe-end = 1 cung R30 CỐ ĐỊNH** (mọi chiều dày OB/OB1 — R30 KHÔNG theo dày, khác Rb).
- **Free-edge (cạnh flanged) = ĐƯỜNG THẲNG tiếp tuyến chung của 2 cung R30.**
- R30 bo **LÕM CẮT VÀO thân** (bulge **DƯƠNG** trong outline CW): cung gặp leg ~**vuông góc**, kéo free-edge
  peel khỏi flange ~25-30mm rồi lượn vào. ⚠️ **KHÔNG phải lồi cắt mũi** (bản sai trước: tâm đặt lệch → phình ra).
- ⚠️ **R12/R20 (Rb) KHÔNG nằm trong outline phẳng** — chỉ là bán kính GẤP LIP ngoài mặt phẳng (TryAddBendLip).
  (Block gốc CÓ thêm R12 ogee giữa R30 và free-edge; bản clean bỏ R12, free-edge tiếp tuyến thẳng R30.)

**TÂM R30 — mấu chốt (block `OB_type1`/`BF_type1`, layer 0/AM_0):**
- Tâm top = **`(S, −Dtop−R)`** — NẰM TRÊN đường mặt span (x=S), 30 DƯỚI góc `(S,−Dtop)`. **Dtop=105** (OB).
- Tâm bottom = **`(F+R, −H)`** — NẰM TRÊN đường flange (y=−H), 30 NGOÀI góc `(F,−H)`.
- ⚠️ KHÔNG đặt `(S−R,−Dtop)`/`(F,−H+R)` (lệch vuông góc vào thân) — đó là bản lỗi phình ra.

**Cài `ProfileService.BracketOutline`:**
1. Tâm cTop=`(S,−Dtop−R)`, cBot=`(F+R,−H)`. Cung gặp leg tại `(S,−Dtop)`/`(F,−H)` (điểm trên cùng/trái cùng của cung).
2. Free-edge = tiếp tuyến ngoài chung 2 cung → song song đường nối tâm, lệch R theo pháp tuyến **n HƯỚNG VỀ gốc O**
   (về thân) — `if ((O−cTop)·n < 0) n=−n`. Tiếp điểm free = `tâm + R·n`.
3. Outline CW: `(0,0)→(S,0)→(S,−Dtop)[R30+]→tFreeTop→[free]→tFreeBot[R30+]→(F,−H)→(0,−H)→` đóng.
   Bulge = `tan(sweep/4)`, sweep từ tiếp-điểm-leg→tiếp-điểm-free (ra **dương**).

Kiểm (S=594,H=374,F=148.9): tâm (594,−135)/(179,−374); tiếp điểm (579,−109)/(164,−348); bulge +0.131/+0.268
→ **khớp CHÍNH XÁC** block `OB_type1` v1(b0.132)/v2, v9(b0.265)/v10. Cùng công thức over-50 & under-50 (khác F,H).

**Block tham chiếu (OB-OB1.dwg, sau explode):** `OB_type1`=over-50 (13 đỉnh, S594/H374/F149, R30+R12),
`OB_type2`=under-50 (12 đỉnh, S547/H344/F15). `BF_type1/2`=Flatbar bracket (có ARC R30 tường minh: `c=(547,−95)r30`,
`c=(115,−374)r30`). `IB_type1/2`=IB (panel hiện chưa có IB). `HP_120x6`, `hp_end_brkt`… = HP + bracket khác.

## 5b. UNDER-50 & BEND (mép gấp flange) — trích OB-OB1.dwg

**UNDER-50 (overhang < 50):** bracket KHÔNG chạm flange:
- `F = 15` (stub cạnh dưới, không phải overhang−15).
- Bracket **cách flange 30mm** → chiều cao dùng = **Web height − 30**. Template under-50 (RefH=344=374−30,
  RefF=15) đã encode. Gọi `BracketOutline(under50=true, S, H=webH−30, F=15)`.

**BEND — mép gấp 90° thành lip (flanged bracket):**
- **Điều kiện:** chiều dài **cạnh chéo tự do** (từ toe đầu-Flange đến toe đầu-HP) **≥ 350 (OB, dày6)** /
  **≥ 600 (OB1, dày10)**. (dump: "FLANGED IF ≥350" / "≥600".)
- **Lip:** mép gấp vuông góc khỏi tấm, chạy dọc cạnh chéo; **bề rộng 70 (OB) / 100 (OB1)**, **dày = dày tấm**.
- **Bán kính bending: Rb = bán kính TRONG (mặt lõm, CHƯA gồm chiều dày)** = 2 × chiều dày → OB(6)=**12**,
  OB1(10)=**20**. Bán kính **NGOÀI = Rb + chiều dày** (OB=18, OB1=30). (dump: "Rb min=12"/"Rb min=20".)
  ĐÃ CÀI: bracket là **1 tấm phẳng gấp lại** (dày ĐỀU thk) — lip = mép bracket gấp lên, dựng từ **tiết diện
  kim loại gấp có fillet** (arc trong Rb + arc ngoài Rb+thk) → extrude dọc cạnh chéo → union tấm.
  - `nDir` hướng **ra ngoài** (xa tâm outline).
  - Tiết diện **DỊCH +sh (=Rb−ov)** để chỉ CHẠM mép bracket (chồng ~ov=0.5mm) → union dính mà **KHÔNG bị
    double-thickness** ở chỗ nối (trước đó chồng nguyên Rb=12mm nên nhìn như 2 tấm ốp).
  - **Mặt TRONG↔TRONG, NGOÀI↔NGOÀI** (kim loại liền): arc trong Rb tiếp tuyến **mặt trong** bracket (mặt phía
    gấp), arc ngoài Rb+thk tiếp tuyến mặt ngoài. Gấp XUỐNG (foldSign<0): **phản chiếu tiết diện y→(thk−y)** để
    tiếp tuyến đúng mặt dưới; map trục Y cố định +Z (KHÔNG lật trục — trước lật trục làm bend lệch thk → inner
    chạm outer ở các bracket gấp xuống).
- **HƯỚNG GẤP = về phía CHIỀU ĐỔ kết cấu** (theo panel type — xem [[bracket-identification]] mục chiều đổ).
  **Trục X: LUÔN trái→phải (+X)** mọi panel. **Trục Y:** Center → **về giữa** (sign(centerY−partY));
  Port → **−Y** (trên→dưới); Starboard → **+Y** (dưới→lên). Cài `BendFoldSign`: so `normal_tấm · chiềuĐổ(dozX=+1,dozY)` chọn local Z ±.
- Ngưỡng & lipW theo dày: dày6→(bend≥350, lip70, Rb12); dày10→(bend≥600, lip100, Rb20).
- Dựng: lấy cạnh NGHIÊNG nhất outline → dải chữ nhật dọc cạnh (dày tấm) → extrude ±Z (theo foldSign) = lipW → union tấm.

**Build3DService**: sinh outline (S,H,F) → Region → extrude chiều dày (6) căn giữa → đặt mặt ĐỨNG
qua AlignCoordinateSystem. **Anchor = đầu CHẠM WEB DÀI** (`MinDistToWeb` per-end, robust — KHÔNG dùng
heuristic "gần tâm" vì có piece web nằm phía mép, vd piece 500 bị lật mặt); +X hướng ra HP/mép; −Y = Z xuống.

**TRẠNG THÁI CODE (cần sửa để khớp spec mới):**
- [ ] Classification: nhận **20 OB** = AM_0 ngắn thk6 (bỏ collinear-gộp-web). Hiện chỉ ra 10.
- [ ] Generator: thêm tham số **F** (leg-on-flange) — hiện chỉ có S(=L cũ) + H, F đang cố định theo block.
- [ ] Map mỗi OB → flange để lấy rộng flange → tính F.
- [x] Hướng: anchor = phía tâm panel (đã sửa). Runtime đọc block từ DWG: chưa (hardcode).

Liên quan: [[panel-parts-reference]] (mục 4 dựng solid), [[bracket-identification]] (loại bracket).
