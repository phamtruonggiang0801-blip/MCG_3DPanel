---
name: panel-parts-reference
description: Nguồn chân lý cho tool MCG_Build3DPanel — quy tắc nhận diện, thông số, và công thức định vị 3D của 5 loại đối tượng trong Panel (Top Plate, Web, Flange, Flat Bar, HP). Đọc skill này TRƯỚC khi viết/sửa bất kỳ logic phân loại hoặc dựng 3D nào.
---

# Panel Parts Reference — Đặc tả 5 loại đối tượng

Tool `MCG_Build3DPanel` nhận 1 Block (Panel) là **Plan View 2D**, bên trong chứa nhiều
đối tượng (và có thể Nesting Block). Nhiệm vụ: **nhận diện** từng đối tượng → **extrude**
lên 3D theo thông số người dùng → **định vị** theo trục Z → gom thành **1 Block 3D**.

- Đơn vị: **mm**. Panel luôn phẳng trên mặt phẳng **XY (Z=0)**, không nghiêng.
- Mọi đối tượng bộ phận là **Closed LWPolyline** (không phải block con có tên riêng).
- Phân loại **CHỈ dựa vào Layer + Color** (không dùng tên block/attribute).

## 1. Bảng nhận diện (identification)

Chỉ xét **Closed LWPolyline** (bỏ qua line/text/dimension/nesting-block khác).

| Loại        | Điều kiện nhận diện                                          | Chiều dày footprint |
|-------------|-------------------------------------------------------------|---------------------|
| Top Plate   | layer `0` **và** là polyline kín có **AREA lớn nhất**       | = `t` (input)       |
| Web         | layer `Mechanical-AM_0`                                     | cạnh ngắn (~6)      |
| Flange      | layer `Mechanical-AM_11` (**bất kể màu**)                   | = `t_flange` (input)|
| Flat Bar    | layer `Mechanical-AM_3` **và** colorIndex **≠ 30**         | cạnh ngắn (~6)      |
| HP          | layer `Mechanical-AM_3` **và** colorIndex **= 30**         | cạnh ngắn (~6)      |

**Lưu ý phân biệt quan trọng (đã kiểm chứng bằng dữ liệu thật — panel `7D-09C_Assy`):**
- **Top Plate**: layer `0` chứa RẤT nhiều noise (≈1166 entity, 18 polyline kín). Top Plate là cái
  **area lớn nhất** — thực đo area ≈ 181.182.199 mm², cái kế tiếp chỉ ≈ 4.468 → chọn `max(area)` là
  tuyệt đối an toàn. Top Plate thường **nhiều đỉnh** (v=22) do có notch/cắt góc, KHÔNG chỉ 4 đỉnh.
- **Flange nhận theo LAYER, KHÔNG lọc theo màu**: thực tế màu thường = 3 (green explicit), đôi khi 256.
  Chiều rộng flange (100/120/350…) lấy theo Plan.
- **WEB nhiều tầng chiều dày (QUAN TRỌNG):** trên **cùng 1 đường Web dài**, chiều dày có thể đổi dọc
  theo chiều dài (6, 10, 15, 20…). Người vẽ tách thành **nhiều Closed LWPolyline collinear nối tiếp
  nhau** (mỗi đoạn 1 chiều rộng = 1 chiều dày). **Tất cả các đoạn này VẪN LÀ WEB**, KHÔNG phải Bracket.
  → 1 "Web logic" = 1 chuỗi các polyline collinear + nối đầu-cuối trên layer Web.
  **Đã cài (ClassificationService):** group union-find các polyline layer Web nếu **cùng phương**
  (ngang/đứng) + **thẳng hàng** (lệch tâm ngang ≤ (t1+t2)/2 + 8mm) + **nối/chồng theo trục dài** (khe ≤ 20mm).
  Mỗi run có Flange rectangle chạy dọc (overlap + dài ≥ 40% flange) → **Web**; không → **Bracket**.
  Thực đo `7D-09C_Assy`: **9 Web run** (có run tiers 6+10), **10 Bracket**, **9 Flange** + **4 Face Plate**,
  FlatBar 14, HP 29. (Trước khi group: 37 polyline layer Web → dễ đếm nhầm 28 "bracket".)
- **⚠️ SỬA (quan trọng):** OB Bracket = polyline AM_0 **NGẮN (<~1000) + dày 6**, KỂ CẢ khi collinear với web.
  KHÔNG dùng collinear để gộp OB vào web. Đoạn ngắn dày **≠6 (thk10)** mới là **web-tier** (thuộc Web).
  Thực đo 7D-09C: **20 OB** (thk6 ngắn) + 8 web-tier (thk10) + 9 web dài. Chi tiết + kích thước 3 leg:
  xem [[bracket-identification]] (leg-flange = (rộng Flange−dày Web)/2−15; leg-topplate = span; cao = Web H).
- ~~Bracket = polyline layer Web KHÔNG collinear~~ (cách cũ sai — đã thay bằng quy tắc chiều dày ở trên).
  Các loại: **OB** (Outside, dày 6), **OB1** (dày 10), **IB / IB1** (Inside), **FB** (Flatbar Bracket)…
  Chiều dày phân biệt phần SỐ (6/10…), nhưng OB vs IB vs FB (có thể cùng dày) cần thêm dấu hiệu (text/vị trí)
  → xem mục 7. Nhận diện mã chi tiết là Backlog.
- Flat Bar và HP **cùng layer** `Mechanical-AM_3` → phân biệt bằng **colorIndex 30 = HP**, còn lại = Flat Bar.
- **Flat Bar — đầu tự do vát chéo (Sniped End)**: đầu FB không hàn Web/HP/FB nào khác bị vát chéo góc
  dưới (cạnh hàn Top Plate luôn vuông sắc). FB vẽ liên tục xuyên qua HP trên Plan chỉ là quy ước vẽ
  nhanh — thực tế phải tách tại chỗ giao HP trước khi xét đầu tự do. Chi tiết công thức + cài đặt:
  xem [[sniped-end]].
- **FACE PLATE (trên layer Flange):** Flange bình thường là **hình chữ nhật kín**. Nhưng ở **góc** hoặc
  vài **vị trí đặc biệt giữa Flange**, do yêu cầu kỹ thuật, phần đó bị **cắt rời & dựng lại KHÔNG vuông**
  → gọi là **Face Plate** (vd polyline v=13, bbox 500×800 trên layer `Mechanical-AM_11`). Face Plate nằm
  cùng layer Flange nhưng **không phải rectangle** → tách bằng test độ vuông/số đỉnh.
- **Chiều dày footprint = cạnh NGẮN** của polyline (member thường axis-aligned, dày ~6mm).
- Thuật toán Region→Extrude theo Z (mục 4) không phụ thuộc hướng member → xử lý được cả member xoay nghiêng.

## 2. Thông số người dùng nhập (chung cho cả Panel)

| Loại      | Thông số nhập                | Chiều dày (thickness)        | Chiều dài            |
|-----------|------------------------------|------------------------------|----------------------|
| Top Plate | chiều dày `t`                | = `t` (input)                | theo Plan            |
| Web       | chiều cao `H`                | = **chiều rộng LWPolyline**  | theo Plan            |
| Flange    | chiều dày `t_flange`         | = `t_flange` (input)         | theo Plan (rộng+dài) |
| Flat Bar  | quy cách `cao × dày` (vd `120*6`) | = dày (spec), = rộng Plan | theo Plan            |
| HP        | quy cách `cao × dày` (vd `120*6`) | = dày (spec), = rộng Plan | theo Plan            |

- Web **không nhập chiều dày** — lấy chiều rộng của LWPolyline trong Plan.
- Flange **không nhập chiều rộng** — lấy theo Plan; chỉ nhập chiều dày.

## 3. Định vị 3D theo trục Z (datum = mặt DƯỚI Top Plate = Z 0)

Quy ước: mặt **dưới** Top Plate là `Z = 0`. Top Plate extrude **lên (+Z)**, mọi thứ khác **xuống (−Z)**.

| Loại      | Khoảng Z (từ → đến)              | Ghi chú                                  |
|-----------|----------------------------------|------------------------------------------|
| Top Plate | `[0, +t]`                        | đi lên                                    |
| Web       | `[−H, 0]`                        | đứng; đầu trên chạm mặt dưới Top Plate    |
| Flat Bar  | `[−h_fb, 0]`                     | đứng; đầu trên chạm mặt dưới Top Plate    |
| HP        | `[−h_hp, 0]`                     | đứng; đầu trên chạm mặt dưới Top Plate    |
| Flange    | `[−H − t_flange, −H]`            | nằm ngang; mặt trên = mặt dưới Web        |

Web / Flat Bar / HP **đứng thẳng đứng** (bản nằm trong mặt phẳng chứa trục Z).
Flange **nằm ngang** (bản rộng theo phương ngang).

## 4. Thuật toán dựng solid (thống nhất cho mọi loại)

Vì footprint 2D trong Plan đã đúng, mọi loại dựng bằng **Region → Extrude theo Z → Move tới cao độ**:

1. Từ Closed LWPolyline tạo `Region`.
2. Extrude Region theo phương Z một đoạn = chiều dày/chiều cao tương ứng:
   - Top Plate: `+t`
   - Web: `−H`   ·  Flat Bar: `−h_fb`   ·  HP: `−h_hp`  (footprint mỏng → thành bản đứng)
   - Flange: `−t_flange`
3. Move solid tới đúng khoảng Z ở mục 3 (Top Plate top=+t; các bản đứng top=0; Flange top=−H).

> Với bản đứng (Web/FB/HP): extrude footprint mỏng theo Z chính là "dựng tường" —
> chiều dày = bề rộng footprint, chiều cao = đoạn extrude. Không cần dựng box thủ công.

## 4b. WEB — chuyển bậc CHIỀU DÀY: taper **1:4** (đã xác nhận)

Web đổi chiều dày (web-tier) KHÔNG cắt vuông mà **vát chuyển tiếp theo tỉ lệ 1:4**
(lệch ngang 1 ⟶ chạy dọc 4).

- Thực đo panel: đoạn web thk **6 → 10** (chênh 4mm) có đoạn vát dài **16mm** dọc trục ⇒ **4 : 16 = 1 : 4** ✔.
- Công thức: `chiều dài vát = 4 × |Δthickness|` (Δ = hiệu 2 bậc dày).
- Vát nằm ở **một bên** (bên dày thêm), cạnh còn lại thẳng.
- ⚠️ Vì có vát, footprint web-tier là polyline **5 đỉnh** (không phải 4) — đừng nhầm là hình khác.
- Áp dụng cho **mọi** chỗ web đổi bậc dày, kể cả tại Girder End (xem [[girder-end]] khi có).

## 4c. FACE PLATE làm "tấm đỡ" Bracket/GE (thay Flange khi bracket nằm trên nó)

Face Plate = AM_11 **KHÔNG chữ nhật** (nv > 5, hình loe ở góc panel). Bracket/GE nằm trên FacePlate
**vẫn theo quy tắc toe cách mép gần nhất 15mm** — nhưng phải đo theo **mép FacePlate**, KHÔNG phải Flange.

- **Chọn tấm đỡ** = Flange **hoặc** FacePlate có `DistToBox` tới bracket nhỏ nhất (tấm bracket nằm trên).
  Bracket góc: FacePlate d≈0–120 (rộng ~500) vs Flange d≈430–488 (rộng 120) → phải lấy FacePlate.
- **FacePlate loe KHÔNG đều** ⇒ overhang = **RAY-CAST** dọc `memberDir` từ anchor, lấy mép polygon xa nhất
  **TẠI đúng dải ngang của bracket** (`BracketBuilder.RayMaxHit`). Dùng bbox sẽ lấy nhầm mép ở chỗ loe nhất
  (thực đo: cùng FacePlate, mép biến thiên 500→430 theo vị trí Y).
- `F = overhang − 15` (như Flange). Cần lưu **`PanelPart.Vertices`** (đỉnh thật) — `PartCollector` đã set.
- **Phần loe VƯỢT quá toe-HP** ⇒ **KẸP** `F ≤ S − R30` (Bracket) / `Lb ≤ L − R30` (GE):
  chân đứng, toe-flange cách toe-HP đúng **R30**. Tránh chân vươn quá vị trí HP.
- GE dùng chung `OverhangProj` nên tự hưởng lợi. Xem [[bracket-identification]], [[girder-end]].

## 4d. TẤM trên layer Web — BackingPlate / ClosedPlate (`PlateDetector`)

2 loại tấm KHÔNG phải Web/Bracket nhưng cùng layer `Mechanical-AM_0`:
- **BackingPlate**: tấm đệm hàn TRÊN web — footprint nhỏ (≤80mm), đè ≥80% diện tích lên 1 Web dài,
  KHÔNG đè FacePlate.
- **ClosedPlate**: tấm kín chữ nhật, không dài như girder (`MemberLength < GE_MIN_LEN`), đè lên FacePlate,
  không phải tier girder (strip mỏng collinear web dài bị loại trừ).

⚠️ **`PlateDetector.IsClosedPlate` dùng overlap BBOX (`ClassifyUtil.OverlapArea`), KHÔNG phải polygon
thật** — chỉ AN TOÀN khi mảnh **KHÔNG chạm** HP/FB (`!touches`, ngưỡng 25%). **KHÔNG áp ngưỡng này cho
mảnh có chạm** — FacePlate loe (không chữ nhật) có bbox rộng hơn nhiều hình dạng thật, nên hầu hết Bracket
nhỏ nằm gần FacePlate sẽ "chồng lấn bbox ≥25%" dù không thật sự là tấm (kiểm chứng: làm vậy khiến 20/29
Bracket flip sai, thay vì đúng 5). Dùng `PlateDetector.IsClosedPlateStrict` (đa giác THẬT qua
`ClassifyUtil.PolygonOverlapFraction`, ngưỡng 90%) cho trường hợp này — xem mục dưới.

### Chạm giả do bbox member CHÉO (`ClassifyUtil.IsDiagonalBloatedBbox` / `HasOnlyWeakTouch`)

**Phát hiện quan trọng, ảnh hưởng mọi chỗ dùng `Adjacent()`** (không riêng ClosedPlate): `Adjacent()` so
bbox trục thẳng (X/Y). Với 1 HP/FB **CHÉO** (không song song trục), bbox trục thẳng của nó **rộng hơn
nhiều** tiết diện thật (VD panel 7D-17C: HP chéo bbox 5296×10386 trong khi dày thật chỉ 6mm) — bất cứ
mảnh nào rơi vào vùng bbox thừa đó đều bị `Adjacent()` báo "chạm" dù hình học thật không hề chạm.

- Dấu hiệu: `min(bbox.w, bbox.h) > FootprintThickness × 1.5 + 5` (`IsDiagonalBloatedBbox`).
- **KHÔNG thể** đơn giản "sửa `Adjacent()` cho hết chạm giả" — phần lớn Bracket THẬT cũng chạm đúng
  các HP chéo này (dãy bracket lặp lại dọc theo 1 HP chéo thật). Chỉ áp dụng cho **quyết định ClosedPlate**:
  nếu MỌI match của `Adjacent()` đều là bbox-chéo-phồng **HOẶC** member khớp lại **NGẮN HƠN** chính mảnh
  đang xét (phi lý làm "chỗ hàn" — bracket luôn hàn vào member DÀI hơn nó) ⇒ `HasOnlyWeakTouch` = true,
  chạm đó không đủ tin cậy để coi chắc chắn là Bracket → mới xét `IsClosedPlateStrict`.
- **Kiểm chứng 7D-17C** (đối chiếu CSV review tay của kỹ sư, ngày 2026-08-25): đúng 5/5 case OB/OB1 sai
  (lẽ ra là Closed Plate) đều rơi vào nhóm `HasOnlyWeakTouch` + overlap polygon ≥90%; không cướp nhầm
  ~24 Bracket khác đang overlap bbox 14–57% với FacePlate gần đó (dưới ngưỡng 90% polygon).
- ✅ **`38DF2` (member CHÉO ở khúc cua panel) — ĐÃ SỬA (2026-08-25, v4)**: không sửa `Collinear()` như
  dự đoán ban đầu — thay vào đó, `WebBracketSplitter` bỏ hẳn cơ chế collinear cho quyết định Bracket
  vs Web, chuyển sang quy tắc **đầu-mút vật lý** (`ClassifyUtil.IsBracketPattern`): đúng 1 đầu mút thật
  (từ `Vertices`, không phải bbox) chạm member AM_0 khác, đầu kia chạm HP/FlatBar. Quy tắc này tự đúng
  cho member chéo (không phụ thuộc trục X/Y) nên `38DF2` (cả 2 đầu chạm Web, không đầu nào chạm HP/FB)
  tự động thành Web, không cần khái niệm "nối qua khúc cua". Xem chi tiết đầy đủ ở [[girder-end]] mục 2b
  (nơi quy tắc này được thiết kế, vì ban đầu phát hiện qua việc sửa GE) — khớp tuyệt đối 108/108 part
  AM_0 trên 7D-17C, thay thế `WEB_TIER_LEN`/tier-anchor ở bước Bracket-vs-Web (2 thứ đó vẫn còn dùng
  riêng ở `PlateDetector`, không liên quan quyết định này nữa).

### ⚠️ Bug bbox chéo LAN SANG tầng DỰNG 3D (`GeometryUtil.MinDistToWeb`) — ĐÃ SỬA (2026-08-25)

Bug "bbox member chéo phồng" **không chỉ ở bước phân loại** — phát hiện thêm 1 chỗ khác ở
`Services/Builders/GeometryUtil.cs` (`MinDistToWeb`, dùng bởi `BracketBuilder.AnchorDir` để xác định
đầu nào của bracket "neo" vào Web thật): dùng `DistToBox` (bbox) thay vì đoạn tâm thật.

- **Hậu quả thật** (kiểm chứng 7D-17C, build report 2026-08-25): 13 Bracket neo vào 1 Web **CHÉO**
  (VD `38EB8` bbox 4115×8067 dù tiết diện thật chỉ 6mm) — bbox phồng khiến CẢ HAI đầu bracket đều
  "gần Web như nhau" theo `DistToBox`, làm `AnchorDir` (dòng `if (w1 <= w2)`) chọn nhầm đầu neo (coin-flip
  theo sai số số học) ⇒ **bracket dựng LẬT NGƯỢC** (`38ECB,38ECC,38ECD,38ECE,38ECF,38ED0,39396-3939C`),
  hoặc chọn đúng đầu nhưng điểm neo không thật sự nằm trên mép Web (`393B9`: đúng chiều nhưng **lưng
  không khớp Web**).
- **Sửa**: `MinDistToWeb` đổi sang `ClassifyUtil.DistToSegment` (đoạn tâm thật từ `Vertices`, cùng hàm
  dùng cho `IsBracketPattern` ở mục classification) — `ClassifyUtil` tuy `internal` nhưng cùng assembly
  nên gọi thẳng được từ `GeometryUtil`, không cần expose thêm gì.
- **Bài học**: khi tìm thấy 1 bug "dùng bbox thay vì hình học thật", PHẢI rà tất cả chỗ dùng
  `Extents3d`/`Bounds`/`DistToBox` cho mục đích "khoảng cách/kề nhau" trong TOÀN BỘ codebase (không chỉ
  nơi phát hiện đầu tiên) — bug này tồn tại độc lập ở 2 tầng khác nhau (`Services/Classify/` và
  `Services/Builders/`) dùng chung 1 nguyên nhân gốc nhưng không ai liên kết code với nhau.
- ✅ **Đã test trực quan lượt 1 (2026-08-25)**: fix trên giải quyết đúng hướng (GE `38E42` build lại
  được) nhưng người dùng phát hiện thêm **2 bug khác cùng họ** ở chính `BracketBuilder`, xem 2 mục dưới.

### ⚠️ Bug #2 — `OverhangProj` "chiếu đỉnh xa nhất" sai bản chất với Flange dài/chéo (ĐÃ SỬA, 2026-08-25)

**Lần sửa ĐẦU (bbox→Vertices) SAI, không giải quyết được gì** — bài học quan trọng: đã đổi nhánh Flange
từ chiếu góc `Bounds` sang chiếu `Vertices` thật, tưởng cùng họ bug với #1/#3, nhưng build lại **giá trị
`overhang` KHÔNG ĐỔI MỘT CHÚT** (vẫn 1198mm). Lý do: bracket `39396` nằm NGANG, `dir=(1,0)` THUẦN TRỤC —
với hướng chiếu thuần trục, chiếu bbox và chiếu Vertices thật cho **kết quả giống hệt nhau** (bbox.maxX
vốn dĩ = X lớn nhất trong các đỉnh thật, theo đúng định nghĩa bbox). Bbox không phải nguyên nhân ở đây.

**Nguyên nhân THẬT** (tính tay lại từ đỉnh thật `38EA1`, xác nhận khớp `overhang=1197.5≈1198` quan sát
được): `OverhangProj` lấy **đỉnh xa nhất theo dir** trong 4 đỉnh support — với 1 Flange CHÉO **DÀI**
chạy dọc qua nhiều bracket, đỉnh xa nhất theo `dir` gần như luôn là **đầu XA của CẢ DẢI flange** (gần hết
chiều dài ~1670mm của nó), chứ KHÔNG PHẢI mép thật tại đúng vị trí bracket đang xét — sai về bản chất
phép đo, không phải sai dữ liệu (bbox hay Vertices đều chiếu ra đỉnh SAI này như nhau). Tính tay ray-cast
(đi từ anchor dọc `dir`, dừng ở mép polygon ĐẦU TIÊN gặp) cho đúng **79.2mm** — khớp bracket cùng loại.

**Sửa đúng**: bỏ hẳn "chiếu đỉnh xa nhất" (cả bbox lẫn Vertices) cho nhánh Flange — dùng CHUNG
`RayMaxHit` (ray-cast mép thật) với nhánh FacePlate (vốn đã đúng từ trước, đó là lý do FacePlate không
bị bug này). Chỉ giữ chiếu góc bbox làm fallback cuối cùng khi thiếu `Vertices`.

**Bài học**: 2 bug trông giống nhau (cùng liên quan hình học tấm đỡ) nhưng gốc rễ KHÁC HẲN — #1/#3 là
"dùng bbox thay hình học thật", còn #2 là "phép đo (max-projection) không phù hợp về mặt vật lý với tấm
đỡ dài/chéo, bất kể lấy dữ liệu từ đâu". Trước khi công bố 1 fix là đúng, PHẢI tính tay lại bằng số liệu
thật để xác nhận giá trị mới KHỚP kỳ vọng — không chỉ tin vào "cùng họ bug nên chắc cùng cách sửa".

### ⚠️ Bug bbox chéo #3 — `MinDistToWeb` lọc bỏ Web-tier NGẮN (ĐÃ SỬA, 2026-08-25)

`GeometryUtil.MinDistToWeb` (bản đã sửa DistToSegment ở trên) vẫn giữ ngưỡng cũ `WebMainMinLen=1000mm`
("chỉ xét Web chính, bỏ web-tier ngắn") — khiến Web-tier NGẮN THẬT (VD `38DF2`, dài 486.7mm — 1 mảnh Web
chéo hợp lệ, xem mục 4d) hoàn toàn KHÔNG được `AnchorDir` xét tới dù là chỗ hàn thật gần nhất. Kết quả:
`AnchorDir` rơi về fallback "via=center" (đối xứng qua tâm TopPlate) — neo sai điểm dù có thể vẫn chọn
đúng đầu, gây "lưng bracket không nằm sát Web" (kiểm chứng thật: `393B9`, neo đúng lẽ ra vào `38DF2`).
**Sửa**: bỏ hẳn ngưỡng độ dài — `MinDistToWeb` giờ xét MỌI `PartType.Web` (dài hay web-tier ngắn).

✅ **Test trực quan lượt 2 (2026-08-25)**: fix Flange (ray-cast) ĐÚNG — người dùng xác nhận "F đúng".
`393B9` lưng vẫn CHƯA khớp Web dù đã bỏ ngưỡng độ dài → lộ thêm bug #4 (mục dưới).

### ⚠️ Bug #4 — `AnchorDir` dùng bbox+`IsHorizontal` ép bracket CHÉO về trục ngang/dọc (ĐÃ SỬA, 2026-08-25)

Gốc rễ thật của việc `393B9` "lưng không khớp Web": bản thân `393B9` là 1 **bracket CHÉO thật**
(kiểm chứng đỉnh thật: bbox 163.1×87.1 — KHÔNG phải strip mỏng ngang; trục dài lệch **~27°** so với
phương ngang, vuông góc với 1 HP CHÉO gần đó — đúng như người dùng mô tả "2D vẽ vuông góc HP"). Nhưng
`BracketBuilder.AnchorDir` tính 2 đầu mút bằng `part.Bounds` + `part.IsHorizontal` (nhị phân ngang/dọc
suy từ bbox `dx≥dy`) — bbox rộng hơn cao nên bị ép thành **"ngang"**, hai đầu mút bị đặt CÙNG Y (mất hẳn
góc chéo 27° thật) ⇒ `memberDir` sai hướng ⇒ đặt 3D bị "xoay lại" theo trục gần đó (Web) thay vì giữ
đúng góc vuông-HP như bản vẽ gốc. Đây chính là câu hỏi của người dùng: "2D vuông góc HP, sao 3D lại vuông
góc Web?" — trả lời: vì `IsHorizontal` (nhị phân) đã âm thầm xoá góc chéo thật trước khi tới bước đặt 3D.

**Sửa**: `AnchorDir` đổi sang `ClassifyUtil.Endpoints(part)` (đỉnh thật, cùng hàm dùng cho
`IsBracketPattern`/`OverhangProj`) thay vì `part.Bounds`+`IsHorizontal`. Tính tay xác nhận: đầu neo mới
cách `38DF2` (Web-tier chéo) chỉ **~5mm** (trong `393B9`'s trước đó không neo được vào đâu do cả bbox
lẫn IsHorizontal đều sai), giữ đúng góc chéo ~27° khi đặt vào 3D.

**Bài học tích luỹ (bug #1/#3/#4 cùng họ, #2 khác họ)**: `PanelPart.IsHorizontal` là 1 SIMPLIFICATION
(nhị phân ngang/dọc từ bbox) chỉ AN TOÀN cho member THẬT SỰ song song trục — bất kỳ chỗ nào dùng nó (hay
`part.Bounds` nói chung) để suy luận HƯỚNG/ĐẦU MÚT cho mục đích định vị/kề nhau đều cần rà lại xem member
có thể chéo hay không. `ClassifyUtil.Endpoints`/`DistToSegment` (dựa `Vertices` thật) là bộ thay thế
CHUẨN, đã dùng nhất quán ở `IsBracketPattern`, `OverhangProj`, `MinDistToWeb`, và giờ cả `AnchorDir`.

✅ **Test trực quan lượt 3 (2026-08-25)**: `393B9` ĐÚNG — người dùng xác nhận "Đã fix". Phát hiện thêm
bug #5 ở GE (mục dưới), KHÔNG liên quan `AnchorDir`.

### ⚠️ Bug #5 — `RayMaxHit` lấy giao điểm XA NHẤT thay vì GẦN NHẤT (SỬA LẦN 1 — CHƯA ĐỦ)

`BracketBuilder.RayMaxHit` (dùng bởi `OverhangProj`) lấy `t` **LỚN NHẤT** trong các giao điểm ray×biên
polygon. Với polygon LỒI (đa số Flange/FacePlate đơn giản) ray chỉ cắt biên ĐÚNG 1 LẦN nên min=max,
không lộ bug — nhưng FacePlate không lồi (VD `393C2` — 10 đỉnh, có "tháp" nhô lên) khiến ray cắt biên
2 LẦN. Sửa lần 1: đổi sang lấy `t` **NHỎ NHẤT** (>0). **Test trực quan xác nhận SAI HƯỚNG KHÁC**: GE
`38E42` build được nhưng toe tụt VÀO TRONG 1 FacePlate thay vì ra tới FacePlate ngoài cùng — câu hỏi
"tại sao tụt vào trong" của người dùng lộ ra nguyên nhân THẬT sâu hơn (mục #6).

### ⚠️ Bug #6 — `OverhangProj` chỉ xét 1 tấm đỡ, không "đi xuyên" qua NHIỀU tấm KỀ NHAU (ĐÃ SỬA, 2026-08-25)

Vị trí GE `38E42` có **2 FacePlate liền kề, chung mép thật** (`38EBD` — tấm TRONG, bao quanh điểm neo;
`393C2` — tấm NGOÀI, bắt đầu đúng nơi `38EBD` kết thúc, tại `Y=37731.8`). `OverhangProj` (kể cả sau khi
sửa #5) chỉ chọn **1 tấm gần centroid nhất** (`38EBD`) rồi ray-cast VÀO ĐÚNG TẤM ĐÓ — ray đi từ điểm neo,
tới đúng ranh giới `38EBD`/`393C2` (357.4mm) là DỪNG (t nhỏ nhất tính ĐÚNG cho riêng `38EBD`), dù thực tế
`393C2` liền ngay sau đó và toe THẬT phải đi tiếp ra hết `393C2` (837… ~848.4mm). Kết quả: toe "tụt vào
trong" đúng như người dùng mô tả — không phải do t nhỏ-nhất-hay-lớn-nhất, mà do **chưa từng xét khả năng
có tấm THỨ HAI nối tiếp**.

**Sửa đúng (thay thế hẳn cách tiếp cận cũ)**: `OverhangProj` không còn "chọn 1 tấm gần nhất" — ray-cast
qua **TẤT CẢ** Flange+FacePlate, gom mọi giao điểm (`RayUnionHit`+`CollectCrossings`, đã bỏ hẳn
`RayMaxHit`), ghép thành các khoảng [vào,ra] theo luật chẵn-lẻ, NỐI các khoảng kề/chồng nhau (dung sai
5mm), trả về mép NGOÀI của khoảng liên tục CHỨA điểm neo — tự động "đi xuyên" qua bao nhiêu tấm liền kề
cũng được, không giới hạn ở 1 tấm. Tính tay xác nhận khớp: `38EBD` cho khoảng (−171.7, 357.4), `393C2`
cho khoảng (357.4, 848.4) — 2 khoảng nối liền tại đúng 357.4 → gộp thành (−171.7, 848.4) → kết quả
**848.4mm**, đúng mép ngoài `393C2` (tấm ngoài cùng) như người dùng mong đợi.

**Bài học tích luỹ (6 bug cùng 1 dạng)**: bug #1/#3/#4 giả định member LUÔN song song trục; #2 giả định
"chiếu đỉnh xa nhất" luôn đúng; #5 giả định support LUÔN lồi; #6 giả định CHỈ CÓ 1 tấm đỡ liên quan. Tất
cả đều là "hàm hình học viết đúng cho trường hợp ĐƠN GIẢN (trục thẳng / 1 support lồi, cô lập) nhưng âm
thầm sai khi gặp trường hợp KHÔNG đơn giản" — và vì trường hợp đơn giản chiếm đa số nên bug ẩn rất lâu,
chỉ lộ khi đúng 1 bracket/GE cụ thể rơi vào đúng trường hợp biên. **Bug #5→#6 còn cho thấy: sửa xong 1
lớp của vấn đề (t min/max) không có nghĩa đã hết — luôn hỏi tiếp "giả định nào KHÁC có thể vẫn còn sai"
thay vì dừng lại ngay khi build không còn báo lỗi.** Khi thêm hình học mới, tự hỏi: "nếu input không đơn
giản như tôi hình dung (chéo, không lồi, nhiều tấm ghép…) thì hàm này còn đúng không?"

### ⚠️ Bug #7 — `RayUnionHit` ghép cặp chẵn/lẻ TRỘN CHUNG nhiều tấm → union ẢO qua tấm KHÔNG liên quan (ĐÃ SỬA, 2026-08-25)

Bản sửa Bug #6 (`RayUnionHit`) gom **tất cả** t từ **mọi** tấm Flange+FacePlate vào 1 danh sách, sort, rồi
ghép cặp chẵn/lẻ (`(t[0],t[1]), (t[2],t[3])…`) trên danh sách TRỘN CHUNG đó — với lý luận "mỗi tấm tự nó
luôn chẵn nên trộn chung vẫn chẵn tổng thể". Lý luận này **SAI**: tổng số giao điểm đúng là chẵn, nhưng
**thứ tự ghép cặp sau khi trộn không còn tương ứng với tấm nào** nếu tia đi xuyên qua **tấm khác không hề
liền/chồng với tấm đang neo** — 2 mốc t của tấm A và 2 mốc t của tấm B xa nhau, không liên quan, nhưng khi
sort chung rồi ghép "(mốc 1,mốc 2),(mốc 3,mốc 4)…" theo VỊ TRÍ chứ không theo TẤM, dễ ghép nhầm 1 mốc của A
với 1 mốc của B, tạo ra "khoảng" ảo nối 2 tấm rời rạc thành 1 — dù chúng cách nhau xa, có `GAP_TOL` cũng
không cứu được vì bản thân KHOẢNG đã sai từ bước ghép cặp, không phải bước gộp khoảng.

**Hậu quả thật**: các OB bracket `S=204` (VD `3939C`, x∈[97995.8,98199.4] y≈19133.2) từng đúng
`overhang≈79mm` (bản `RayMaxHit` min-t, chỉ xét 1 tấm) — sau khi đổi `RayUnionHit` (bản trộn chung) lại
nhảy lên **1163–1523mm**.

**Sửa lần 1 (ghép cặp riêng từng tấm)**: đây là bug THẬT và cần sửa (ghép cặp chẵn/lẻ theo vị trí sau khi
trộn chung t của nhiều tấm có thể ghép nhầm) — nhưng **KHÔNG PHẢI nguyên nhân của case `3939C`** cụ thể
(ghi nhầm ở bản trước): 4 Flange thẳng đứng (`99129/100764/102399/104035`) đều nằm ở phía **NGƯỢC hướng
`dir`** của bracket này (`dir=(−1,0)`, các Flange này ở x lớn hơn anchor) nên tự chúng KHÔNG interleave
với tấm đỡ thật — sửa lần 1 không đổi kết quả case này (đúng kiểu "Lỗi vẫn y hệt" — xem bài học cuối mục).

**Sửa lần 2 (bug thật gây `1163mm`) — early-return sai khi nhóm KHÔNG chứa gốc**: vòng lặp gộp khoảng có
điều kiện `if (curLo ≤ GAP_TOL) return …` khi chuyển sang nhóm mới — nhưng **số âm bất kỳ luôn ≤ GAP_TOL
(5)**, nên 1 nhóm nằm **HOÀN TOÀN phía sau gốc** (cả `lo` VÀ `hi` đều âm rất xa, VD tấm đỡ khác ở tận phía
ngược `dir`) vẫn khớp điều kiện này và bị `return` SỚM (trả **0**, vì `hi<0`) — bỏ qua mọi nhóm còn lại,
**kể cả nhóm ĐÚNG chứa gốc nằm sau nó trong danh sách**. Kiểm chứng thật (`3939C`, anchor=(98199.4,
19133.2), free=(97995.8,19133.2) ⇒ **dir=(−1,0)**, không phải `+X` như đoán ban đầu — xác định qua
`MinDistToWeb` thật: đầu `x=98199.4` cách Web chéo `38EB8` chỉ **4.4mm** (chạm), đầu kia **185.9mm**):
tấm đỡ THẬT là Flange chéo `38ED1` (verts chéo, thk=150) cho khoảng **(−89.4, 79.4)** — CHỨA gốc, đúng
79.4mm khớp giá trị lịch sử — nhưng 4 Flange thẳng kể trên (mọi khoảng đều ÂM, ở xa hơn theo chiều ngược
`dir`) đứng TRƯỚC nó sau khi sort theo `lo` → nhóm xa nhất bị `return 0` oan trước khi vòng lặp chạm tới
`38ED1`. `RayUnionHit`=0 ⇒ `OverhangProj` rơi về **fallback bbox** (mục đích cho case "không tấm nào ray
cắt được", hiếm) — lấy góc bbox AXIS-ALIGNED của `38ED1` (tấm CHÉO, bbox "phồng" theo kiểu Bug #1) →
overhang ảo **1162.8mm** (khớp báo cáo 1163).

**Sửa**: thêm điều kiện `curHi ≥ −GAP_TOL` (gốc thật sự nằm TRONG khoảng, không chỉ "lo ≤ 0") trước khi
return; nhóm không chứa gốc (dù `lo` âm hay dương) chỉ đơn thuần bị **bỏ qua**, vòng lặp tiếp tục xét nhóm
sau — không dừng sớm. Ghép cặp riêng từng tấm (sửa lần 1) VẪN GIỮ (đúng, cần thiết, dù không phải nguyên
nhân case này) — 2 bug độc lập, cùng nằm trong `RayUnionHit`, cả 2 đều đã sửa (2026-08-25).

**Bài học quan trọng nhất**: khi user báo **"Lỗi vẫn y hệt"**, đừng coi bản sửa trước là "chưa đủ mạnh"
rồi lặp lại đúng hướng — TRA LẠI TỪ ĐẦU bằng số liệu thật của ĐÚNG case bị báo lỗi (không suy diễn từ case
tương tự đã gặp trước): ở đây, việc đoán `dir=(+X)` theo suy luận vị trí Flange lân cận (không kiểm
`AnchorDir` thật) dẫn tới 1 lý thuyết SAI hoàn toàn (nêu đúng 3 Flange nhưng chúng không liên quan gì tới
bug thật) — trong khi bug thật nằm ở 1 nhánh code hoàn toàn khác (early-return) mà lý thuyết đầu không hề
chạm tới. Luôn tính `AnchorDir` (endpoint + `MinDistToWeb`) TRƯỚC, đừng đoán hướng tia.

### ⚠️ Bug #8 — `PartCollector` bỏ sót polyline "nhìn thì kín" nhưng `Closed=false` → Top Plate biến mất (ĐÃ SỬA, 2026-08-25)

`PartCollector.Collect` chỉ nhận entity thoả `ent is Polyline pl && pl.Closed` — bỏ qua MỌI polyline có
flag `Closed=false`, kể cả khi hình dạng nhìn RÕ RÀNG là kín (người vẽ tự khép vòng bằng cách kéo điểm
cuối trùng điểm đầu, thay vì bật flag Closed trong AutoCAD). Bug thật đã xảy ra với chính **biên Top
Plate** của panel 7D-17C: kiểm chứng bằng file dump (`MCG_DumpDrawing`) — polyline `38DDC` (layer `"0"`,
**65 đỉnh**, đúng layer `LAYER_TOPPLATE`) có `"closed": false`, nhưng đỉnh ĐẦU `(15.3534,−197.4544)` và
đỉnh CUỐI `(15.3531,−197.4544)` lệch **0.0003mm** (làm tròn nổi khi vẽ) — về hình học là 1 vòng kín hoàn
chỉnh. Vì bị lọc bỏ, `topCands` (layer 0) chỉ còn lại 2 mảnh nhỏ KHÔNG liên quan (gần 1 lỗ khoét cạnh đó)
— `ClassificationService` chọn "Area lớn nhất trong 2 mảnh nhỏ đó" làm TopPlate ⇒ dựng ra tấm **2194×2674mm**
thay vì cả panel. Hậu quả: sau Build 3D, Top Plate "biến mất" (thực ra có dựng, nhưng bé tí, lọt thỏm giữa
panel — dễ tưởng nhầm là mất hẳn). **Không phải lỗi mới** — kiểm tra ngược mọi file classify JSON từ đầu
buổi (kể cả file đầu tiên `094558`) đều cho CÙNG kích thước 2194×2674mm, chứng tỏ đây là lỗi drawing-level
tồn tại từ trước, không phải regression của bất kỳ thay đổi code nào trong buổi.

**Cách chẩn đoán** (khi nghi ngờ 1 part "biến mất" mà không rõ vì sao — không đoán, dump thật): chạy lệnh
`MCG_DumpDrawing`, tìm block con tương ứng (VD `"7D-17C_Top Plate"`), đọc TỪNG polyline coi layer + `closed`
+ so sánh đỉnh đầu/cuối bằng mắt — nhanh hơn nhiều so với đoán từ triệu chứng.

**Sửa**: `PartCollector` thêm `IsVisuallyClosed(pl)` — chấp nhận polyline có đỉnh đầu ≈ đỉnh cuối
(khoảng cách ≤ 1e-3mm) NHƯ closed thật, dùng `pl.Closed || IsVisuallyClosed(pl)` làm điều kiện thu thập.
An toàn cho bước dựng hình sau (`PlateBuilder.Extrude`) vì nó `Region.CreateFromCurves` trên ENTITY GỐC
(clone trực tiếp từ database), không phải dựng lại từ `Vertices` — AutoCAD Region vốn dung nạp polyline
"nhìn kín" (đỉnh đầu/cuối trùng) dù flag Closed=false, hành vi chuẩn của lệnh REGION.

### ⚠️ Bug #9 — CutOut khác trong CÙNG block Top Plate bị rơi vào ClosedPlate/Web; lỗ tròn (Circle) bị bỏ sót hoàn toàn (ĐÃ SỬA, 2026-08-25)

Sau khi sửa Bug #8 (TopPlate nhận đúng biên `38DDC`), lộ ra tiếp: panel 7D-17C còn **6 lỗ khoét dạng
polyline khác** (`38DDD` 1 bát giác lớn, `38DE0`..`38DE4` 5 hình chữ nhật nhỏ ~370×64mm) **cùng nằm trong
block `"7D-17C_Top Plate"`** với biên `38DDC` và 2 lỗ đã nhận đúng (`38DDA`/`38DDB`) — nhưng bị rơi vào
`ClosedPlate`/`Web` (đứng thẳng, `extrude = −WebHeight`, tức **lao xuống 374mm** thay vì bị TRỪ như lỗ
6mm) vì check "cùng owner block với Top Plate → CutOut" đặt **CUỐI CÙNG** trong `ClassificationService`,
sau các heuristic hình học `IsBackingPlate`/`IsClosedPlate` (dựa trên overlap/kề với Web/FacePlate — 1 lỗ
khoét tình cờ nằm ĐÈ lên 1 Web/FacePlate bên dưới trong plan view vẫn khớp nhầm các test này).

**Xác nhận kỹ sư (2026-08-25)**: *"Lỗ khoét của Top Plate sẽ được để cùng Polyline của Block Top Plate"*
— đây là QUY ƯỚC TỔ CHỨC BẢN VẼ chắc chắn, không phải suy đoán hình học. `OwnerBlock(p) == topKey` (cùng
block với biên Top Plate) là tín hiệu đáng tin hơn hẳn overlap/kề — sửa bằng cách chuyển check này lên
**ĐẦU TIÊN** trong vòng lặp `am0` ([ClassificationService.cs:101-124](Services/ClassificationService.cs#L101-L124)).
Trước đây đặt cuối vì lúc đó `top` còn SAI (Bug #8, trỏ nhầm sang entity ở block Structure) nên đặt sớm
từng "cướp nhầm" 1 ClosedPlate/Bracket thật — giờ `top` đã đúng block Top Plate CHUYÊN DÙNG (chỉ chứa
biên+lỗ+mốc COG, không lẫn Web/Bracket thật) nên rủi ro đó không còn.

**Lỗ tròn (`Circle`) bị bỏ sót HOÀN TOÀN**: cùng block còn có **8 lỗ Ø80** vẽ bằng entity `Circle` (không
phải `Polyline`) — `PartCollector.Collect` trước đây chỉ nhận `ent is Polyline`, im lặng bỏ qua MỌI entity
khác, kể cả `Circle` dù rất đơn giản/hợp lệ. Sửa: thêm nhánh nhận `Circle` (`BuildPartFromCircle`) — không
cần `Vertices` (StrictlyInside chỉ dùng `Bounds`; dựng hình dùng `Region.CreateFromCurves` thẳng trên
entity Circle gốc qua `PlateBuilder.Extrude`, không qua `Vertices` tái tạo).

**Bài học**: khi 1 quy tắc phân loại dựa vào **VỊ TRÍ TỔ CHỨC trong bản vẽ** (cùng block với X) thay vì
thuần hình học, quy tắc đó thường ĐÁNG TIN HƠN heuristic hình học (overlap/kề/khoảng cách) — nhưng dễ bị
đặt SAI THỨ TỰ (sau các check "an toàn hơn" tưởng tượng) nếu người viết code không tin tưởng đủ vào tín
hiệu tổ chức đó. Cũng nhắc lại bài học `PartCollector` (Bug #8): **luôn hỏi "còn loại entity nào khác
(Circle/Arc/Spline/Region/Hatch…) có thể mang cùng ý nghĩa mà code chưa xử lý?"** khi 1 tính năng "vẫn còn
thiếu vài case" — đừng dừng lại sau khi sửa được 1 loại entity.

**Tác dụng phụ của việc thêm Circle**: mở khả năng nhận `Circle` kéo theo 1 lỗ khoét ẢO — block **`COG`**
(mốc trọng tâm: `Hatch` + 2 `Line` bắt chéo + `Circle` r=250, TẤT CẢ trên layer **"0"**, giống hệt layer
Top Plate) nằm NESTED trong block Top Plate (VD ở (10099.8,−9241.8) local) — `Circle` bên trong nó cũng
"cùng owner block với biên Top Plate" nên bị nhận nhầm CutOut, tạo lỗ Ø500 ảo đúng tại mốc COG. Xác nhận
kỹ sư: *"Block COG không được đưa vào đây, bỏ qua"*. Sửa: `PartCollector.Collect` bỏ qua HẲN (không đệ
quy vào) mọi nested `BlockReference` có tên **"COG"** (không phân biệt hoa/thường) — trước khi đệ quy, so
tên bằng `GetName`. Bài học: mở rộng khả năng nhận diện 1 loại entity mới (đây: `Circle`) có thể vô tình
"đánh thức" nội dung annotation/mốc vẽ vốn trước đó vô hại vì bị bỏ qua toàn bộ (annotation block thường
DÙNG CHUNG layer với hình học thật, ví dụ layer "0", nên không thể lọc bằng layer) — cần lọc bằng TÊN BLOCK
khi phát hiện case cụ thể, không có cách tổng quát an toàn hơn nếu bản vẽ không có quy ước layer riêng cho
annotation.

### ⚠️ Bug #10 — Bụng HP viền mép quay SAI tại góc LÕM (notch) — "xa tâm panel" không đúng cục bộ (ĐANG XÁC MINH, 2026-08-26)

`BracketCoder.BellyDir` (mục xác định hướng bụng HP) dùng `outward = hp.Centroid − TopPlate.Centroid`
("xa tâm panel") làm hướng RA NGOÀI cho HP ngoài cùng — xấp xỉ này **chỉ đúng khi biên LỒI đơn giản**.
Panel 7D-17C có nhiều khấc/notch cắt vào biên (xem Bug #8/#9) — tại 1 điểm nằm SÁT khấc, "xa tâm panel
nói chung" vẫn đúng hướng ra xa panel, nhưng có thể KHÔNG đúng hướng ra khỏi VẬT LIỆU THẬT tại chỗ khấc bẻ
ngược biên cục bộ (góc lõm). Kiểm chứng thật: `38EF2`/`393C0`/`393A1` (panel 7D-17C, đều là HP viền mép
theo xác nhận kỹ sư — *"Là những HP nằm ngoài cùng viền theo mép top plate, kể cả là HP chéo và song song
với trục"*) đều quay bụng sai sau khi sửa Bug #8 (biên Top Plate đúng làm lộ ra góc khấc thật).

Bước sửa 1 (dùng `DistToPolygonBoundary` để xác định "ngoài cùng") đã đúng nhưng CHƯA ĐỦ — chỉ sửa được
việc XÁC ĐỊNH có phải HP ngoài cùng hay không, không sửa HƯỚNG ra ngoài (vẫn dùng "xa tâm panel"), và chỉ
áp dụng cho HP song song trục (HP chéo vẫn luôn coi "ngoài cùng" rồi lấy hướng theo tâm panel — sai y hệt
tại khấc). Sửa đúng: thêm `ClassifyUtil.LocalOutwardNormal(pt, poly)` — tìm CẠNH biên gần điểm nhất, lấy
pháp tuyến CỦA CHÍNH CẠNH ĐÓ, xác định dấu bằng cách thử bước nhỏ (2mm) rồi kiểm `PointInPolygon` (nếu
bước đó vẫn trong đa giác → đổi dấu). Cách này đúng CỤC BỘ bất kể biên lồi/lõm, và áp dụng thống nhất cho
CẢ HP chéo lẫn song song trục (trước đây 2 nhánh code khác nhau, cùng sai tại khấc theo cùng 1 lý do).

**Xác nhận thật**: `38EF2`/`393A1`/`393C0` đã quay đúng sau build. Nhưng lộ regression MỚI: `393A0`
(HP chéo khác, cùng cụm góc/khấc) quay SAI — vì bản sửa đầu tiên lỡ **gate luôn cả HP chéo** qua
`DistToPolygonBoundary ≤ EDGE_POLY_TOL(150)`, trong khi `393A0` cách biên thật **172.9mm** (VỪA quá 150)
→ bị loại khỏi nhánh "ngoài cùng", rơi về chiều-đổ (sai). Root cause: nhầm lẫn 2 việc ĐỘC LẬP — "có phải
HP ngoài cùng" (determination) và "hướng ra ngoài là gì" (direction). Quy tắc GỐC "HP chéo = thanh
viền/vát góc → LUÔN coi ngoài cùng" (không cần gate khoảng cách, vì bản chất chéo ĐÃ LÀ tín hiệu viền/vát
đủ mạnh) vẫn ĐÚNG và cần giữ nguyên — chỉ HƯỚNG (trước đây "xa tâm panel", giờ `LocalOutwardNormal`) mới
cần sửa. Sửa lại: `!axisAligned → edge=true` KHÔNG ĐIỀU KIỆN (khôi phục, đặt TRƯỚC check khoảng cách);
`DistToPolygonBoundary` giờ CHỈ dùng để xác định "ngoài cùng" cho HP SONG SONG TRỤC (vốn không có quy ước
sẵn như HP chéo). `LocalOutwardNormal` vẫn áp dụng cho hướng của CẢ 2 loại khi đã xác định là edge.

**Bài học**: khi sửa 1 bug bằng cách thêm điều kiện MỚI vào 1 nhánh code, kiểm tra kỹ điều kiện đó có vô
tình SIẾT LẠI (thu hẹp) 1 quy tắc CŨ vốn đang đúng ở nhánh khác hay không — đặc biệt khi 2 quy tắc
(determination vs direction) bị gộp chung 1 biến `edge`/1 nhánh `if` như ở đây.

## 5. Đầu ra

- Solid đặt Layer **`Mechanical-AM_0`**, Color **ByLayer**.
- Gom toàn bộ solid thành **1 Block 3D**.
- **Thay thế** block 2D gốc (không giữ song song).
- Có bước **Preview** trước khi commit.
- Xử lý **1 panel / lần**; người dùng **Pick** block qua WPF Palette rồi bấm **Build**.

### 5a. Dựng 3D cho ClosedPlate / BackingPlate / CutOut (`Services/Builders`) — ĐÃ CÀI (2026-08-25)

3 `PartType` này được thêm/tinh chỉnh ở đợt phân loại (2026-08-25) nhưng ban đầu **không builder nào
xử lý** (`Build3DService.Dispatch` trả null im lặng, không cảnh báo) — panel dựng xong thiếu mất các
tấm này mà không ai biết. Đã lấp bằng quy tắc hình học **xác nhận từ người dùng**:

- **ClosedPlate**: qua `PlateBuilder` (thêm case) — đứng thẳng như Web, `extrude = −WebHeight`, không dịch.
- **BackingPlate**: qua `PlateBuilder` — đứng thẳng như Web nhưng **THỤT VÀO 30mm mỗi đầu**: cách mặt
  dưới Top Plate 30mm, cách Flange 30mm ⇒ `extrude = −(WebHeight−60)`, `moveZ = −30`.
- **CutOut**: KHÔNG qua `IPartBuilder` (không tự thành 1 solid độc lập) — `CutOutBuilder.ApplyCutOuts`
  dựng "dao cắt" từ outline CutOut (extrude dư `MARGIN=5mm` mỗi đầu qua hết chiều dày Top Plate, tránh
  lỗi mặt trùng khít) rồi `Solid3d.BooleanOperation(BoolSubtract)` **trừ thẳng vào solid Top Plate** đã
  dựng. Gọi tường minh từ `Build3DService.Build` SAU vòng lặp dựng chính (cần solid Top Plate có sẵn).
  Lỗi ở 1 cutout không làm hỏng cutout khác — ghi vào Report, Top Plate coi như chưa khoét lỗ đó.

⚠️ **Chưa test trực quan trong AutoCAD** — cần Build thử panel thật (VD 7D-17C) để xác nhận hình dạng.

## 6. Backlog (đã ghi nhận — phát triển sau, KHÔNG làm ở v1)

- ✅ ~~CutOut layer AM_0 quá rộng~~ **ĐÃ SỬA (2026-08-25)**: chuyển check CutOut (AM_0) xuống **CUỐI**
  chuỗi quyết định trong `ClassificationService` (sau BackingPlate, ClosedPlate cả 2 nhánh) thay vì đầu
  tiên — giờ chỉ là **fallback** khi không khớp loại nào khác (và thêm `!touches` — mảnh có chạm HP/FB
  luôn được ưu tiên xét Bracket trước). Kiểm chứng 7D-17C: `38EA3`/`38ED0` đều tự giải quyết đúng qua
  `IsClosedPlateStrict`/`WebBracketSplitter` khi có cơ hội chạy trước.
- ✅ ~~Member CHÉO khiến Bracket/Web-tier nhầm lẫn~~ **ĐÃ SỬA (2026-08-25, v4)** — xem mục 4d. Không
  còn cần "nối tiếp qua khúc cua" như dự đoán ban đầu; quy tắc đầu-mút vật lý mới (`IsBracketPattern`)
  tự đúng cho member chéo vì dùng `Vertices` thật, không phụ thuộc trục X/Y.
- **BellyDir HP nội bộ vs biên cục bộ** (`BracketCoder.BellyDir`, mục "chiều đổ"): 2 case `38ECA`/`390E6`
  (IB lẽ ra OB) đều chạm HP `38EE9` (cx=99038.6, vertical). **Đào sâu (2026-08-25)**: `38EE9` **KHÔNG**
  gần `vMinX`/`vMaxX` toàn cục (5 HP đứng dài trong panel: X = 91021/96393/**99039**/104355/104515 —
  `38EE9` nằm giữa, cách biên gần nhất **5476mm**, xa hơn `EDGE_TOL(50)` rất nhiều) ⇒ giả thuyết "mép
  cục bộ do panel không lồi" (ghi trước đó) **KHÔNG đúng** với case này — code hiện tại thực sự coi nó là
  HP "nội bộ" một cách hợp lý theo logic X-extent, chỉ là kết quả sai theo thực tế bản vẽ. Nguyên nhân
  gốc chưa rõ (có thể quy tắc "chiều đổ" `(hp.cy≤center.y?+1:−1)` không đúng cho khu vực cục bộ này).
  CHƯA sửa — cần thêm panel khác có case IB/OB tương tự trước khi thiết kế lại rule.

  **Điều tra nhánh phụ (2026-08-25) — ĐÃ GIẢI THÍCH, KHÔNG mở đường sửa #6**: quét lệch `Bounds` (`p.Bounds`,
  dùng cho `w`/`h`) vs bbox suy từ `Vertices` trên **toàn bộ 254 part** → chỉ đúng **2 part lệch**, và
  CẢ HAI đều là layer **`0`** (`39383` TopPlate, `3936D` CutOut) — mọi part AM_0/AM_3/AM_11 (Web, Bracket,
  HP, FlatBar, Flange, FacePlate…) khớp tuyệt đối. Không phải bug rotation/transform ảnh hưởng cả block
  (nếu vậy sẽ lệch mọi part) — đặc trưng riêng layer 0. Giải thích hợp lý nhất: TopPlate/CutOut layer-0
  có **góc bo (bulge)**, và `PartCollector.BuildPart` lấy `Vertices` chỉ từ đỉnh thẳng
  (`pl.GetPoint3dAt`), bỏ qua bulge → bbox suy từ `Vertices` bị THIẾU phần cong lồi ra; `Bounds`
  (`pl.GeometricExtents`, có tính bulge) mới là dữ liệu ĐÚNG. **Không ảnh hưởng** logic hiện có: không
  chỗ nào dùng `Vertices` của layer-0 (StrictlyInside dùng `Bounds`, đúng); `PolygonOverlapFraction`
  (nguyên nhân 4) chỉ test với FacePlate — FacePlate không nằm trong danh sách lệch → các fix ClosedPlate
  vẫn an toàn. HP liên quan `38ECA`/`390E6` là layer `AM_3`, không phải layer 0 → phát hiện này KHÔNG áp
  dụng được để sửa #6. **Ghi nhận làm giới hạn đã biết**: nếu sau này cần polygon-accurate cho 1 part
  layer 0 (TopPlate/CutOut), phải lấy điểm dọc theo cung thật (`Polyline.GetPointAtParameter`…) thay vì
  chỉ đỉnh thẳng — `Vertices` hiện tại KHÔNG đáng tin cho polyline có bulge.
- **HP Bulb Profile**: hiện đơn giản hóa HP thành flat bar/L. Sau này xây skill riêng
  tự vẽ biên dạng bầu Holland Profile (cong ở mép) từ quy cách `cao × dày`.
- **Bracket** (cùng layer Web, không có Flange): nhiều loại — Outside Bracket (OB, dày 6),
  OB1 (dày 10), Inside Bracket (IB), Flatbar Bracket (FB)... phân biệt bằng mã + chiều dày.
  Cần skill riêng nhận biết mã. Hiện đơn giản hóa thành hình chữ nhật.
- **Girder End (GE)**: đầu end của Web dài hơn đầu end của Flange. Cần skill riêng.
- **Web nhiều chiều cao khác nhau** trong 1 panel: hiện dùng chung 1 `H`. Sau mở rộng nhập riêng.
- **Flat Bar nhiều quy cách** trong 1 panel (2–3 loại): tìm cách để người dùng chỉ định
  FB có quy cách khác biệt. Hiện dùng chung 1 quy cách.
