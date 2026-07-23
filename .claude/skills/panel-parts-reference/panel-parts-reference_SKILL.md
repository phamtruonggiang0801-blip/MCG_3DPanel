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

## 5. Đầu ra

- Solid đặt Layer **`Mechanical-AM_0`**, Color **ByLayer**.
- Gom toàn bộ solid thành **1 Block 3D**.
- **Thay thế** block 2D gốc (không giữ song song).
- Có bước **Preview** trước khi commit.
- Xử lý **1 panel / lần**; người dùng **Pick** block qua WPF Palette rồi bấm **Build**.

## 6. Backlog (đã ghi nhận — phát triển sau, KHÔNG làm ở v1)

- **HP Bulb Profile**: hiện đơn giản hóa HP thành flat bar/L. Sau này xây skill riêng
  tự vẽ biên dạng bầu Holland Profile (cong ở mép) từ quy cách `cao × dày`.
- **Bracket** (cùng layer Web, không có Flange): nhiều loại — Outside Bracket (OB, dày 6),
  OB1 (dày 10), Inside Bracket (IB), Flatbar Bracket (FB)... phân biệt bằng mã + chiều dày.
  Cần skill riêng nhận biết mã. Hiện đơn giản hóa thành hình chữ nhật.
- **Girder End (GE)**: đầu end của Web dài hơn đầu end của Flange. Cần skill riêng.
- **Web nhiều chiều cao khác nhau** trong 1 panel: hiện dùng chung 1 `H`. Sau mở rộng nhập riêng.
- **Flat Bar nhiều quy cách** trong 1 panel (2–3 loại): tìm cách để người dùng chỉ định
  FB có quy cách khác biệt. Hiện dùng chung 1 quy cách.
