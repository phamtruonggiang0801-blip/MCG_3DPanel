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

## 2b. NHẬN DIỆN trong panel 2D — GE vs Web-tier vs Bracket (v4 — quy tắc ĐẦU MÚT, thay hẳn collinear)

⚠️ **v1** (collinear + liên tục) → **v2** (kích thước + collinear-với-Web-dài) → **v3** (v2 + "tier-anchor"
bắc cầu qua khe) — cả 3 đều dựa trên **collinear theo trục X/Y**, ngày càng vá thêm case nhưng vẫn có kẽ
hở (v3 vẫn sai 1 case — `38DF2`, member CHÉO không khớp bất kỳ test collinear trục-thẳng nào).

**v4 bỏ hẳn collinear, dùng đúng ĐỊNH NGHĨA VẬT LÝ của mã bracket** (theo xác nhận trực tiếp từ kỹ sư,
2026-08-25): *"OB là mã có 1 đầu gắn vào Web, 1 đầu gắn vào lưng HP"* — và ngược lại, **nếu 1 mảnh AM_0
ngắn có CẢ HAI đầu đều gắn vào Web/tier/Plate khác (không đầu nào chạm HP/FB) thì nó KHÔNG PHẢI bracket
— chỉ là đoạn nối tiếp của Web, dù có collinear hay không, dù dài/dày thế nào**.

`ClassifyUtil.IsBracketPattern(p, otherAm0, hpParts, fbParts, tol)`:
1. Lấy **2 điểm mút thật** của `p` — trung điểm 2 cạnh NGẮN, tính từ `Vertices` thật (`ClassifyUtil.Endpoints`)
   — **không phải bbox**, nên đúng cả member CHÉO.
2. Với mỗi đầu, đo khoảng cách tới **đoạn tâm thật** (`ClassifyUtil.DistToSegment`, không phải bbox) của
   từng member AM_0 khác / HP / FlatBar.
3. `IsBracketPattern` = true **CHỈ KHI** đúng 1 đầu gần (≤ `ADJ_TOL`) 1 member AM_0 khác **VÀ** đầu kia
   gần 1 HP/FlatBar.

Đây là điều kiện DUY NHẤT `WebBracketSplitter` dùng để quyết định Bracket vs Web — không còn `webTier`,
không còn `CollinearWithLongWeb`, không còn khái niệm "tier-anchor" ở bước này nữa (tất cả bị thay bởi
1 hàm). `GirderEndDetector` theo đó cũng đơn giản lại: **không cần loại-trừ collinear-với-tier nữa** —
mọi mảnh còn là `Bracket` khi tới `GirderEndDetector` chắc chắn đã có đúng 1 đầu chạm HP/FB thật (vì
`WebBracketSplitter` đảm bảo), nên chỉ cần xét đủ dài+dày là thành GE.

| Điều kiện (mảnh AM_0 ngắn, `< WEB_MIN_LEN`) | Loại |
|---|---|
| `IsBracketPattern` = false (2 đầu đều AM_0, hoặc không đầu nào chạm gì đủ gần) | **Web** |
| `IsBracketPattern` = true, NHƯNG `len<GE_MIN_LEN` hoặc `thk<GE_MIN_THK` | **Bracket** (OB/IB/FB…) |
| `IsBracketPattern` = true VÀ đủ dài+dày | **GE** (Code `GE-HP`/`GE-FB` theo đầu chạm) |

**Kiểm chứng panel 7D-17C (Center)**: **khớp TUYỆT ĐỐI 108/108 part AM_0** (đối chiếu CSV review tay
kỹ sư ngày 2026-08-25, bản mới nhất) — dùng ĐÚNG 1 quy tắc đơn giản này tự sửa cả 3 case sai từng phải
vá riêng lẻ qua 3 đợt: `38DF7` (GE giả), `3916E` (OB1 giả), **và cả `38DF2`** (member CHÉO — thứ mà v3
không giải được vì cần khái niệm "nối qua khúc cua", giờ không cần nữa vì đầu mút lấy từ `Vertices` thật
đã tự đúng bất kể hướng). **0 sai lệch** trên 46 Bracket hiện có + mọi Web/GE khác.

⚠️ **Lưu ý khi cài đặt `IsBracketPattern`**: tham số "member AM_0 khác" (`otherAm0`) **PHẢI** là TOÀN BỘ
AM_0 của panel, **KHÔNG PHẢI** `webCands` (phần chưa quyết định) — 1 đầu bracket có thể chạm đúng 1
ClosedPlate/BackingPlate đã bị `ClassificationService` tách ra TRƯỚC khi `WebBracketSplitter` chạy; nếu
chỉ truyền `webCands`, "mốc" đó biến mất và bracket bị gán nhầm thành Web. **Lỗi này đã xảy ra thật**
(`38EF9` chạm `38E4B`/ClosedPlate ở 1 đầu, bị gán nhầm Web khi lần đầu cài đặt chỉ truyền `webCands`) —
sửa bằng cách `WebBracketSplitter.Split` nhận thêm tham số `allAm0` riêng (xem code, 2026-08-25).

- `HpMaxSpan` (bên dưới) **không được `GirderEndDetector.Apply` dùng** — giữ lại cho **GB** (giai đoạn sau).
- `ClassifyUtil.CollinearWithLongWeb`/`WEB_TIER_LEN` **vẫn còn dùng** — nhưng chỉ ở `PlateDetector`
  (loại-trừ tier khỏi ClosedPlate), KHÔNG còn liên quan gì tới quyết định Bracket-vs-Web nữa.

⚠️ **Giả thuyết cũ về ngưỡng `GE_MIN_THK=10` loại nhầm GE thk6 — vẫn CHƯA quan sát thấy trong dữ liệu
thật** (mục 3 xác nhận GE1/GE5 trong `GE.dwg` là thk6 — rủi ro lý thuyết, không phải lỗi đã xảy ra).

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

## 6b. TRẠNG THÁI CÀI ĐẶT (v4)

- ✅ **Nhận diện (v4 — xem mục 2b)**: `WebBracketSplitter` quyết định Bracket-vs-Web bằng đúng 1 quy
  tắc `ClassifyUtil.IsBracketPattern` (đầu mút thật, không collinear). `GirderEndDetector` chạy SAU,
  chỉ còn lọc `MemberLength ≥ GE_MIN_LEN(600)` + `FootprintThickness ≥ GE_MIN_THK(10)` trên các mảnh
  ĐÃ chắc chắn là Bracket thật (đúng 1 đầu chạm HP/FB) → `PartType.GirderEnd`, Code `GE-HP`/`GE-FB`.
  **Không còn nhánh loại-trừ collinear/tier-anchor trong `GirderEndDetector` nữa** — thừa, vì
  `WebBracketSplitter` đã đảm bảo điều đó từ trước. **Kiểm chứng 7D-17C: khớp tuyệt đối 108/108 part.**
- ✅ **Web-tier / member CHÉO**: không còn xử lý riêng — `IsBracketPattern` dùng đầu mút từ `Vertices`
  thật nên tự đúng cho MỌI hướng member (kể cả chéo), không cần khái niệm tier-anchor hay collinear nữa.
- ⚠️ **HPmaxspan / GB**: `GirderEndDetector.HpMaxSpan` vẫn tồn tại nhưng **không còn được `Apply` gọi**.
  Hàm này giờ là **code chết** trừ khi **GB** (giai đoạn sau) cần dùng lại.
- ✅ **Hình học PARAMETRIC** (`GeProfileService`, thay template): dựng R30(Lb)→free-edge→R50→đoạn 50→span-face
  theo công thức tangent-from-P + R50 fillet tại P=(L−50,−Dend). **Kiểm chứng khớp block GE1/GE2 từng đỉnh**
  (tanLedge 254.3, tanFree 208.3, R50c 254.3/−155, bR50 0.3, T 111.2, bR30 0.1). Dend scale = member−15.
- ✅ **Flange 50×10** (`GirderEndBuilder.AddFlange`): bar dọc free-edge, 50 rộng (z, căn giữa web) × 10 dày,
  union vào web khi `flanged`. `GirderEndBuilder` tính D = √((L−Lb)²+(H−Dend)²) → chọn flange theo ngưỡng.
- ⚠️ **v1 flange chưa snip 15@30** (bar thẳng); Dend bụng (GE3/4, −30) chờ mẫu; `L`=pieceLength, `Lb`=overhang−15
  (kẹp ≤ L−R30) — cần **test mắt** hiệu chỉnh.

## 6c. Toe-end Flange khi Flange/FacePlate đỡ DÀI HƠN member — squareCorner (xác nhận kỹ sư, 2026-08-25)

**Quy tắc kỹ sư xác nhận**: Toe-end HP (đầu trên, cạnh L/R50) **luôn thiết kế giống mọi GE khác** — không
có case đặc biệt. Toe-end Flange (đầu dưới, cạnh Lb) — **khi Flange/FacePlate đỡ vươn dài hơn cả member**,
chỉ cần **dựng thẳng đứng góc vuông 90°**, không cần R30.

**Vì sao cần rẽ nhánh riêng** (không chỉ là lựa chọn thẩm mỹ): công thức R30 gốc (`Solve`, nhánh
`else`) đặt tâm `Cb=(Lb+R30, −H)` rồi tính tiếp tuyến từ `P=(L−50,−Dend)`. Khi overhang (từ
`OverhangProj`/`RayUnionHit`, xem [[panel-parts-reference]] mục Bug #6) **vượt trần kẹp** `L−R30`
— tức Flange/FacePlate đỡ dài hơn cả member — `Lb` bị kẹp sát `L−30`, khiến `Cb.X → L` và vòng tròn R30
(bán kính 30 quanh `Cb`) **lấn xuống dưới đáy thật `−H`**. Kiểm chứng tay GE `38E42`
(L=823.5, H=374, Dend=105, union=848.4→Lb kẹp 793.5): `BotTangent.y` tính ra **≈−376.2**, vượt quá
`−H=−374` khoảng **2.2mm** → outline phải quay ngược lên `FlangeToe.y=−374`, tạo nếp tự cắt gần đáy
→ nguy cơ `RegionFrom` lại trả null, GE biến mất lần nữa (y hệt lỗi gốc, khác nguyên nhân).

**Điều kiện kích hoạt** (`GirderEndBuilder.Build`): `squareCorner = (overhang − 15) > L − 30` — tức raw
Lb (trước khi kẹp) đã vượt trần, y hệt điều kiện "Flange dài hơn member". Khi `true`: ép `flanged=false`
(bỏ hẳn khả năng hàn thêm flange 50×10 — free-edge lúc này chỉ còn 1 đoạn thẳng đứng ngắn, không phải
cạnh chéo dài để hàn dọc theo).

**Hình học** (`GeProfileService.Solve`, nhánh `squareCorner`): hướng free-edge `u=(0,−1)` (thẳng đứng)
thay vì tiếp tuyến R30 → R50 fillet tại `P` vẫn tính bình thường (dùng chung code) → sau R50, **hạ thẳng
đứng từ `TanFree` xuống `y=−H`** (không R30, không "vươn" theo `Lb`), gặp đáy tại góc vuông. Với số liệu
`38E42`: `u=(0,−1)` ⇒ `TanFree=(773.5,−155)` (đúng `P.X` vì hạ thẳng đứng) ⇒ corner **(773.5,−374)** —
không còn overshoot, không tự cắt. `38E45` (Lb=724 < L−30=776) **không chạm nhánh này**, vẫn dùng R30
như cũ — đúng tinh thần "Toe-end HP giống nhau, chỉ Toe-end Flange khác khi Flange dài hơn".

## 7. Chưa chốt / TODO

- [x] ~~GE/tier/Bracket nhầm lẫn (3 case: `38DF7`, `3916E`, `38DF2`)~~ — **đã sửa (v4, 2026-08-25)**:
      thay toàn bộ heuristic collinear (v1→v3) bằng 1 quy tắc đầu-mút vật lý duy nhất
      (`ClassifyUtil.IsBracketPattern`, xem mục 2b). Khớp tuyệt đối 108/108 part AM_0 trên 7D-17C.
- [ ] **`GE_MIN_THK=10` vẫn loại thẳng mọi GE thk6 tiềm năng** (mục 3 xác nhận GE1/GE5 trong `GE.dwg`
      là thk6) — **chưa quan sát thấy** lỗi thật do việc này trên dữ liệu hiện có, nhưng cần kiểm tra
      panel khác có GE thk6 thật trước khi coi ngưỡng này là chốt.
- [ ] **v4 mới kiểm chứng trên 1 panel (7D-17C)** — dù quy tắc đầu-mút tổng quát hơn hẳn v1-v3 (không
      còn phụ thuộc collinear/hướng member), vẫn nên chạy thêm panel khác (đặc biệt panel có nhiều
      member CHÉO hơn) để chắc chắn `ADJ_TOL=12` đủ chặt/lỏng cho mọi trường hợp.
- [ ] **GB** (đầu Web không chạm HP/FB nhưng dài bất thường, có thể là lỗi vẽ hoặc trường hợp đặc biệt
      chưa gặp) — chưa thiết kế tiêu chí; `HpMaxSpan` giữ lại chờ dùng.
- [ ] **GE3/GE4** (bụng HP): chưa có bản vẽ — chờ; tham chiếu IB (Dtop=HP−30 + ledge 15).
- [x] ~~**Lb** suy từ đâu khi dựng tự động?~~ — **đã trả lời (2026-08-25)**: `Lb = OverhangProj(part,cls) − 15`
      (ray-cast qua union Flange+FacePlate, [[panel-parts-reference]] Bug #6), kẹp `≤ L−R30`; khi kẹp trần
      (Flange đỡ dài hơn member) → `squareCorner` thay R30 (mục 6c). **Chưa test trong AutoCAD** — cần build
      lại panel 7D-17C, kiểm `38E42` còn `built:true` và report có `squareCorner=True`.
- [ ] Bản vẽ dump **không có MODEL SPACE / DIM** (chỉ đọc được block definition) — nếu cần thêm note,
      phải sửa `DrawingDumpCommand` để dump cả **Layout/Paper Space**.

Liên quan: [[panel-parts-reference]] (mục 4b taper web 1:4) · [[bracket-identification]] · [[parametric-profiles]] · [[hp-profile]]
