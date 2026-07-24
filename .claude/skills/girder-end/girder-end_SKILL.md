---
name: girder-end
description: Quy cách GIRDER END (GE) — đầu Web (girder) tại vị trí gặp HP hoặc Flat Bar. Mã GE1..GE6, hình học (Dend, R50, R30, free-edge), ngưỡng D chọn có/không flange 50×10 + snip. Đọc khi nhận diện hoặc dựng GE.
---

# Girder End (GE) — đầu Web tại HP / Flat Bar

**GE KHÔNG phải tấm hàn thêm** (khác Bracket) — là **phần Web được hàn NỐI TIẾP với girder**,
thu hẹp dần về phía đầu gặp HP/FB. ⇒ **cùng layer với Web** (`Mechanical-AM_0`), không có mã trên bản vẽ 2D.

- Chỉ có GE **khi đầu Web gặp HP hoặc Flat Bar**. Gặp Web khác / mép panel → Web giữ **hình chữ nhật** bình thường.
- Web nhiều tầng dày: **tầng nào là tầng END thì áp quy tắc theo chiều dày của tầng đó**.
- 1 đầu Web chỉ gặp HP **hoặc** FB (không đồng thời).

## 1. Bảng mã

| Mã | Đầu End gặp | Có flange? |
|---|---|---|
| **GE1** | **lưng HP** | không |
| **GE2** | **lưng HP** | **có** (50×10) |
| **GE3** | **bụng HP** (bulb) | không |
| **GE4** | **bụng HP** (bulb) | **có** |
| **GE5** | **Flat Bar** | không |
| **GE6** | **Flat Bar** | **có** |

Cặp lẻ/chẵn phân biệt bằng **D** (mục 3). ⚠️ **GE3/GE4 CHƯA có trong bản vẽ** — khi cần, tham chiếu
cách né bulb của **IB bracket** ([[bracket-identification]] mục 4b: Dtop = HP−30 + ledge 15).

## 2. Hình học (elevation, mặt phẳng đứng của Web)

Local: gốc **(0,0)** = chỗ GE **nối tiếp Web**; cạnh phải x=0 cao **H** (= Web height);
**−X** = ra phía đầu HP/FB; y=0 = mặt dưới Top Plate.

Đường bao (đi vòng):
```
(0,0) ─ cạnh trên L ─→ (−L,0)
      ↓ MẶT ĐẦU cao Dend
(−L,−Dend) ─ ledge ngang ─→ [R50] ─→ free-edge THẲNG ─→ [R30 hoặc góc sắc] ─→ cạnh đáy Lb ─→ (0,−H)
```

| Tham số | Giá trị | Ghi chú |
|---|---|---|
| **H** | Web height (374) | cạnh nối tiếp web |
| **Dend** | **= chiều cao member − 15** | HP120→**105** · FB80→**65**. **GIỐNG `Dtop` của Bracket** |
| **L** | khoảng cách từ **mép Web gần nhất vuông góc với GE** | lấy từ hình học panel |
| **Lb** | cạnh đáy | = `L − D` (không được ghi kích thước trên bản vẽ) |
| **D** | **toe-end-flange → toe-end-hp/fb**, đo **THẲNG (toe-tới-toe)** | `D = √((L−Lb)² + (H−Dend)²)` — xem mục 3 |
| **R50** | cung đầu HP/FB, **luôn 50** | tâm = `(−L + ledge, −Dend − 50)`, **tiếp tuyến ledge ngang** |
| **R30** | toe đáy, **chỉ bản KHÔNG flange** | tâm **NẰM TRÊN cạnh đáy**, cách góc 30 — **y hệt toe Bracket** |
| ledge | đoạn ngang tại y=−Dend | **suy ra** từ tiếp tuyến R50 (không cố định) |

**Kiểm chứng block** (GE.dwg): R50 = 50.01/50.05/49.98/50.01 · R30 = 30.15/30.06 ·
tâm R30 GE1 = (−138.9, −374.03) ⇒ đúng trên cạnh đáy y=−374, cách góc (−108.758) **đúng 30** ✔

## 2b. NHẬN DIỆN trong panel 2D — GE vs GB vs Bracket

Cả 3 đều là **AM_0 ngắn + chạm HP/FB** ở đầu tự do. Phân biệt bằng quan hệ với **Web chính**:

| Điều kiện | Loại |
|---|---|
| **Collinear** với web chính **VÀ LIÊN TỤC** (nối tới web, **KHÔNG bị web VUÔNG GÓC chặn**) | **GE** |
| **Collinear** nhưng **BỊ WEB VUÔNG GÓC NGẮT** + `L > HPmaxspan` | **GB** — *chưa định nghĩa, giai đoạn sau* |
| **Collinear** nhưng bị web vuông góc ngắt + `L ≤ HPmaxspan` | **Bracket** (OB) |
| **KHÔNG collinear** với web chính | **Bracket** (OB/IB/FB) |

- *Collinear* = cùng phương + tim ngang trùng (dung sai ≈ (dày_a+dày_b)/2 + 8).
- **"Liên tục"** = nối tới web chính **và KHÔNG có web VUÔNG GÓC nằm trong khe**. Web vuông góc cắt ngang
  ⟹ **KHÔNG** liên tục (dù khe chỉ ~1 chiều dày). Đây là điểm phân biệt then chốt.
- **Hở là do web khác đi qua**, KHÔNG phải mảnh "bị ngắn": web vuông góc dày `t` ⟹ hở ≈ `t`
  (334/399 hở 6 = web ngang dày 6 cắt; 470/520 hở 310 = web dọc + web-tier chen).

**Kiểm chứng panel 7D-09C** (inspect 152653, 25 mảnh AM_0 ngắn chạm HP/FB):

| Loại | SL | Chi tiết |
|---|---|---|
| **GE** | **1** | len **830**, dày **10**, (84083, 22712) — collinear + LIỀN web, không bị chặn ✔ |
| **Bracket (OB)** | 11 + (334/399…) | 474×5 · 524×5 · 500×1 (không collinear); 334/399 (collinear nhưng L≤HPmaxspan) |
| **GB** (giai đoạn sau) | 470×2 · 520 | collinear, bị web dọc ngắt, **L > HPmaxspan** |

⚠️ **OB vs GB cùng là "collinear + bị web vuông góc ngắt"** — phân biệt DUY NHẤT bằng **`L > HPmaxspan`**.
`HPmaxspan` = span lớn nhất cho phép của HP (**user cần cung cấp giá trị**). 334/399 (L≤) → OB; 470/520 (L>) → GB.
Hiện GB **chưa làm** ⟹ 470/520 tạm giữ như OB.

## 3. NGƯỠNG D — chọn có/không flange (ĐÃ XÁC NHẬN bằng Attribute + DIM trong GE.dwg)

**Note gốc (block attribute `[NOTE]`, đọc qua MCG_DumpDrawing JSON):**
```
GIRDER END WITHOUT FLANGE IF:  THICKNESS=6mm & D<350,  OR  THICKNESS=10mm & D<600
GIRDER END WITH FLANGE 50x10 IF:  THICKNESS=6mm & D>=350,  OR  THICKNESS=10mm & D>=600
```

**Số đo trực tiếp từ DIM (GE.dwg model space):**
- `D`: GE1=**314.3** · GE5=**349.15** · GE2=**624.87** · GE6=**639.59** (khớp công thức `√((L−Lb)²+(H−Dend)²)`).
- `L`: 271.32 / 734 / 271.32 / 730 — DIM ghi **`L<775mm`** ⇒ **giới hạn L, và GB khi `L ≥ 775`** (=HPmaxspan≈bước HP 775).
- `50 <>` (đoạn 50) · `10 "50x<>"` (Flange **50×10**) · `15`/`30` (clearance lưng/bụng + snip).
- HP block đặt cách GE đúng **L** (GE1@1331, HP@1060 → 271) ⇒ **L = web→lưng HP, KHÔNG trừ 15 ngang** (15 là Dend đứng).


`D = √((L − Lb)² + (H − Dend)²)` — **toe-tới-toe**, ĐÚNG BẰNG công thức free-edge của Bracket.
- `toe-end-flange = (Lb, −H)` — Lb = **mép cuối Flange girder − 15** (giống `F = overhang − 15` của Bracket).
- `toe-end-hp/fb = (L, −Dend)` — L đo tới **LƯNG/BỤNG HP/FB** (không phải mép), giống Bracket.

| Chiều dày girder | KHÔNG flange | CÓ flange 50×10 |
|---|---|---|
| **6 mm** | D **< 350** | D **≥ 350** |
| **10 mm** | D **< 600** | D **≥ 600** |

⚠️ **Trùng đúng ngưỡng gấp lip của Bracket** (thk6→350, thk10→600) ⇒ dùng chung hằng số.

**Kiểm chứng 4 block** (girder thk 6) — dự đoán khớp thực tế 4/4:

| | L | Lb | Dend | ΔX | ΔY | **D** | ngưỡng 350 | flange |
|---|---|---|---|---|---|---|---|---|
| GE1 | 271.316 | 108.758 | 105 | 162.6 | 269 | **314.3** | < | không ✔ |
| GE2 | 734 | 170 | 105 | 564 | 269 | **624.9** | ≥ | có ✔ |
| GE5 | 271.316 | 108.758 | 65 | 162.6 | 309 | **349.15** | < (thiếu 0.85!) | không ✔ |
| GE6 | 730 | 170 | 65 | 560 | 309 | **639.6** | ≥ | có ✔ |

✅ **GE1 D = 314.3 khớp chính xác số 314 user cung cấp.**
✅ **GE5 D = 349.15 — chỉ thiếu ngưỡng 0.85mm** ⇒ bản vẽ cố tình vẽ **trường hợp biên**, xác nhận công thức tuyệt đối.

**Bản KHÔNG flange** (GE1/3/5): toe đáy bo **R30**.
**Bản CÓ flange** (GE2/4/6): toe đáy **góc SẮC** (không bo).

## 4. FLANGE của GE (bản chẵn)

- **50 rộng × 10 dày**, hàn dọc **free-edge** (mép dưới nghiêng), nằm **ngoài** đường bao, chạy tới góc đáy.
- Trong elevation thấy là **dải dày 10** áp sát free-edge (đồng phương).
- **SNIP END** ở **đầu phía CAO** (phía HP/FB): vát **15mm** (đo vuông góc) ở **30°**, **CẢ HAI bên**
  ⇒ cạnh vát dài `15/sin30° = 30`, còn lại **20mm phẳng** giữa. Kiểm chứng: `15 + 20 + 15 = 50` ✔
  (block AM_7 đo được cạnh **30–20–30**).

## 5. Số liệu 4 block tham chiếu (GE.dwg, girder thk 6)

| | GE1 | GE2 | GE5 | GE6 |
|---|---|---|---|---|
| Dend | 105 (HP120) | 105 | 65 (FB80) | 65 |
| L | 271.316 | 734 | 271.316 | 730 |
| Lb | 108.758 | 170 | 108.758 | 170 |
| **D** | **162.6** → <350 | **564** → ≥350 | **162.6** | **560** |
| toe đáy | R30 | sắc | R30 | sắc |
| flange | – | 50×10 | – | 50×10 |
| ledge | 17.019 | 37.707 | 14.769 | 36.035 |

## 6. Liên hệ với Bracket — DÙNG LẠI được

| | Bracket | GE |
|---|---|---|
| Dend/Dtop | member − 15 | **giống hệt** |
| Toe đáy | R30, tâm trên cạnh đáy | **giống hệt** |
| Cung đầu member | R30 | **R50** (khác) |
| Ngưỡng thk6/thk10 | 350 / 600 (gấp lip) | 350 / 600 (có flange) — **cùng số** |
| Gia cường | gấp lip 70/100 | **flange 50×10 hàn ngoài** (không gấp) |

⇒ Generator GE nên tái dụng khung dựng của `ProfileService.FlangedToe` (xem [[parametric-profiles]]).

## 6b. TRẠNG THÁI CÀI ĐẶT (v1)

- ✅ **Nhận diện**: `GirderEndDetector` — mảnh AM_0 ngắn chạm HP/FB + collinear + **liền web** (khe < 3mm)
  → `PartType.GirderEnd`, Code `GE-HP`/`GE-FB`. Chạy giữa `WebBracketSplitter` và `BracketCoder`.
  Kiểm chứng panel: đúng **1 GE = 830** (dày 10).
- ✅ **HPmaxspan tự tính**: `GirderEndDetector.HpMaxSpan` — max khoảng cách đường HP song song liền kề,
  nhóm ≥3 đường (bỏ biên). Panel 7D-09C ≈ **911** ⟹ **0 GB** (mọi mảnh L < 911; 470/520 = OB).
- ✅ **Hình học PARAMETRIC** (`GeProfileService`, thay template): dựng R30(Lb)→free-edge→R50→đoạn 50→span-face
  theo công thức tangent-from-P + R50 fillet tại P=(L−50,−Dend). **Kiểm chứng khớp block GE1/GE2 từng đỉnh**
  (tanLedge 254.3, tanFree 208.3, R50c 254.3/−155, bR50 0.3, T 111.2, bR30 0.1). Dend scale = member−15.
- ✅ **Flange 50×10** (`GirderEndBuilder.AddFlange`): bar dọc free-edge, 50 rộng (z, căn giữa web) × 10 dày,
  union vào web khi `flanged`. `GirderEndBuilder` tính D = √((L−Lb)²+(H−Dend)²) → chọn flange theo ngưỡng.
- ⚠️ **v1 flange chưa snip 15@30** (bar thẳng); Dend bụng (GE3/4, −30) chờ mẫu; `L`=pieceLength, `Lb`=overhang−15
  (kẹp ≤ L−R30) — cần **test mắt** hiệu chỉnh.

## 7. Chưa chốt / TODO

- [ ] **GE3/GE4** (bụng HP): chưa có bản vẽ — chờ; tham chiếu IB (Dtop=HP−30 + ledge 15).
- [ ] **Lb** suy từ đâu khi dựng tự động? (biết `L` và `D` thì `Lb = L − D`, nhưng D lấy từ hình học nào?)
- [ ] Nhận diện GE trong panel 2D: hiện GE **cùng layer Web**, footprint là chữ nhật ⇒ cần tiêu chí
      "đầu Web + chạm HP/FB + nối tiếp web" để tách khỏi Web thường và khỏi Bracket.
- [ ] Bản vẽ dump **không có MODEL SPACE / DIM** (chỉ đọc được block definition) — nếu cần thêm note,
      phải sửa `DrawingDumpCommand` để dump cả **Layout/Paper Space**.

Liên quan: [[panel-parts-reference]] (mục 4b taper web 1:4) · [[bracket-identification]] · [[parametric-profiles]] · [[hp-profile]]
