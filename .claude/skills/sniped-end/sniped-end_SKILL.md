---
name: sniped-end
description: Quy cách SNIPED END của Flat Bar (FB) — vát chéo góc ở đầu tự do (không hàn Web/HP/FB nào khác). Công thức rise/run (member−15, góc 30°/60° cố định) + quy tắc tách FB tại chỗ giao HP trước khi xét đầu tự do. Đọc khi nhận diện hoặc dựng FlatBar.
---

# Sniped End — vát chéo đầu tự do của Flat Bar

**Sniped End KHÔNG áp dụng cho mọi đầu FB** — chỉ đầu nào **THỰC SỰ TỰ DO** (không hàn Web/Bracket/
GirderEnd/HP/FB nào khác) mới bị vát. Đầu chạm 1 member khác (Web, HP, FB khác…) giữ nguyên vuông sắc
(coi như đã "nối", không cần vát) — xác nhận kỹ sư, 2026-08-26.

Phân tích từ `FB Sniped End.dump.json` (dump qua `MCG_DumpDrawing`) — không có Attribute/NOTE ghi
công thức sẵn (khác `GE.dwg`), suy ra bằng cách đo trực tiếp polyline mẫu `1F851` (pentagon trong
hộp chú thích "SYMBOL IN TOP VIEW") + xác nhận từng bước với kỹ sư qua hỏi-đáp.

## 1. Hình học (elevation, mặt phẳng đứng của FB)

FB đứng thẳng đứng (như Web): cạnh **TRÊN** (Z=0) **hàn vào Top Plate** — luôn nguyên vẹn, chạy hết
chiều dài, góc vuông sắc kể cả ở đầu tự do. Cạnh **DƯỚI** (Z=−h_fb, `h_fb` = chiều cao FB theo spec
`cao×dày`) là cạnh **tự do** — chỉ cạnh này (và góc của nó với đầu-tự-do) bị vát.

Local: x = dọc member (0 tại 1 đầu, L=`MemberLength` tại đầu kia); y: 0 = trên/hàn, −h_fb = dưới/tự do.

Không vát (đầu không tự do, giữ nguyên như trước khi có tính năng này):
```
(0,0) ── top (hàn) ──→ (L,0)
  │                        │
(0,−h_fb) ←── bottom (tự do) ── (L,−h_fb)
```

Vát ở 1 đầu tự do (ví dụ đầu x=0) — thay góc vuông `(0,−h_fb)` bằng 2 đỉnh:
```
(0,0) ──────────────────────────────── (L,0)
 │  15mm (FLAT_TOP, cố định)
 ╲
  ╲  ← vát 30°/60° (chéo)
(run,−h_fb) ──── bottom (tự do) ──── (L,−h_fb)
```

| Tham số | Công thức | Ghi chú |
|---|---|---|
| **FLAT_TOP** | **15mm cố định** | đoạn thẳng đứng còn lại sát cạnh hàn, KHÔNG phụ thuộc `h_fb` |
| **rise** | `h_fb − 15` | = phần chiều cao bị vát — đúng khuôn "member−15" đã dùng cho Bracket (Dtop)/GE (Dend) |
| **run** | `rise × tan(60°)` = `rise × √3` | góc vát **30° so cạnh dài / 60° so cạnh đứng — CỐ ĐỊNH**, không phụ thuộc quy cách FB (chỉ `rise`/`run` scale theo `h_fb`) |

Đo được từ `1F851` (x∈[1100.77,1664.77] y∈[4956.62,5036.62], vẽ phóng to minh hoạ không theo tỷ lệ
FB thật): Δx=112.58 (run), Δy=65 (rise, ứng `h_fb=80`⇒`rise=80−15=65` ✔), góc đo `Dim
LineAngularDimension2 = 1.0472 rad = 60.0000°` khớp `atan(65/112.58)=29.99°≈30°` ✔.

Đầu nào **tự do** thì vát đầu đó — có thể 0, 1, hoặc 2 đầu vát trên cùng 1 đoạn FB (2 đầu vát ⇒
outline 6 đỉnh thay vì 4/5).

## 2. Điều kiện "tự do" — và bẫy quan trọng: FB vẽ liên tục xuyên qua HP

**Xác nhận kỹ sư (quan trọng nhất, ảnh hưởng tới PHÂN LOẠI chứ không chỉ dựng hình):** trên Plan
View, người vẽ 2D thường kéo **1 polyline FB liên tục xuyên qua HP** cho nhanh, nhưng **THỰC TẾ FB
KHÔNG liên tục** — nó **kết thúc đúng tại vị trí giao với HP** ở cả 2 phía. Nếu không xử lý, code sẽ
không thấy 2 đầu "ảo" này (chúng nằm giữa 1 polyline duy nhất, không phải 1 trong 2 endpoint thật của
polyline) → tưởng nhầm cả đoạn dài là "không có đầu tự do" hoặc dựng 1 khối liên tục xuyên qua HP
(sai vật lý).

**Giải pháp — `Services/Classify/FlatBarSplitter.cs`** (chạy trong `ClassificationService`, SAU khi
route layer xong, TRƯỚC khi FB được thêm vào `result.Parts`):
- Với mỗi FB, tìm mọi HP **CẮT NGANG THẬT giữa thân** (`ClassifyUtil.CrossesThrough` — bbox chồng lấn
  thật + điểm cắt chiếu lên trục dài FB nằm nghiêm ngặt cách 2 đầu mút ≥ `FB_SPLIT_MARGIN` (20mm) —
  phân biệt với "chạm ở đầu mút", vốn không cần tách).
- Tách polyline gốc thành N+1 `PanelPart` con dọc trục dài, mỗi đoạn có 2 đầu mút THẬT riêng.
- Không HP nào cắt ngang thật → trả nguyên polyline gốc (hành vi không đổi).

Sau khi tách, đầu tự do được xác định bằng `GeometryUtil.IsFreeEnd` (Services/Builders) — true nếu
điểm đó **không** nằm gần (`ClassifyUtil.DistToSegment ≤ FB_FREE_END_TOL`) bất kỳ Web/Bracket/
GirderEnd/HP/FB nào khác (loại trừ chính đoạn đang xét). **Ngưỡng `FB_FREE_END_TOL = 30mm`** (xác
nhận kỹ sư, 2026-08-26 — RIÊNG cho FlatBar, khác `ADJ_TOL=12mm` dùng cho Bracket/GE): đầu FB cách
member khác ≤ 30mm ⇒ coi như đã "nối", KHÔNG vát; > 30mm ⇒ tự do, vát. Đầu FB sinh ra do tách tại chỗ
giao HP tự động nằm sát HP đó (trong 30mm) → tự động được coi là **không tự do**, không cần cờ đánh
dấu riêng.

## 3. Cài đặt (2026-08-26)

- **`Services/Classify/ClassifyUtil.cs`**: `CrossesThrough(p, h)` — bbox chồng lấn thật
  (`OverlapArea > 0`) + điểm cắt (chiếu centroid `h` lên trục dài `p`) cách 2 đầu mút `p` ≥
  `FB_SPLIT_MARGIN` (20mm, hằng số mới).
- **`Services/Classify/FlatBarSplitter.cs`** (mới) — `Split(fbParts, hpParts)`: tách từng FB tại các
  điểm `CrossesThrough`, trả `List<PanelPart>` (segment mới KHÔNG dùng lại DB entity gốc để clone
  Region nữa — builder tự dựng outline từ tham số hình học, xem dưới).
- **`Services/ClassificationService.cs`**: case `LAYER_FBHP` nhánh FlatBar không `result.Parts.Add`
  ngay nữa — gom vào `fbParts`, gọi `FlatBarSplitter.Split` rồi mới `result.Parts.AddRange(fbParts)`
  (TRƯỚC khi `fbParts` được dùng ở `WebBracketSplitter`/`GirderEndDetector`/`BracketCoder` — các bước
  này nhận đúng list ĐÃ TÁCH, không cần sửa gì thêm vì chỉ dùng `DistToSegment`/`Adjacent`).
- **`Services/Builders/GeometryUtil.cs`**: `IsFreeEnd(pt, self, cls, tol)` — true nếu không gần
  Web/Bracket/GirderEnd/HP/FlatBar nào khác (loại trừ self). Gọi với `tol = ClassifyUtil.FB_FREE_END_TOL`
  (30mm, hằng số riêng cho FlatBar — KHÔNG dùng chung `ADJ_TOL` 12mm của Bracket/GE).
- **`Services/Builders/FlatBarBuilder.cs`** (mới, `IPartBuilder`, đăng ký TRƯỚC `PlateBuilder` trong
  `Build3DService`): tính `free1`/`free2` qua `ClassifyUtil.Endpoints` + `IsFreeEnd`; cả 2 đầu không
  tự do → trả `null` (fallback `PlateBuilder` dựng hộp, hành vi cũ nguyên vẹn); ngược lại dựng outline
  (mục 1) qua `GeometryUtil.RegionFrom` → extrude theo `FootprintThickness` → đặt vào vị trí thật bằng
  `Matrix3d.AlignCoordinateSystem` (cùng pattern `BracketBuilder.BracketPlacement`, anchor = `e1`,
  hướng = `e2−e1`).

## 3b. ⚠️ Bug — segment KHÔNG đầu nào tự do bị dựng NGUYÊN VẸN cả thanh FB gốc (ĐÃ SỬA, 2026-08-26)

**Triệu chứng thật** (build panel `7D-17C_Assy`, xem `report`): 1 FB dài bị `FlatBarSplitter` tách
đúng 8 đoạn tại 7 chỗ giao HP, nhưng report cho **7 dòng `FlatBar` cùng 1 handle với bbox GIỐNG HỆT
nhau, đúng bằng bbox của CẢ THANH GỐC** (VD handle `390FB`: cả 7 dòng đều `minY=29417.4 maxY=37367.4`
— chiều dài ~8000mm — dù mỗi đoạn thật chỉ dài vài trăm–ngàn mm). Hậu quả quan sát: WireFrame thấy có
vát (đúng, ở 2 đoạn ĐẦU/CUỐI có đầu tự do) nhưng lẫn lộn với nhiều khối "ma" chồng lấn nguyên thanh;
Shaded không thấy vát vì khối vát nhỏ bị các khối "ma" to hơn che khuất/chồng lấn.

**Nguyên nhân:** bản đầu của `FlatBarBuilder.Build` `return null` khi **cả 2 đầu đoạn đều KHÔNG tự
do** (đúng cho mọi đoạn Ở GIỮA — nằm giữa 2 chỗ giao HP liên tiếp) — Dispatch rơi xuống
`PlateBuilder`, và `PlateBuilder.Extrude` **clone THẲNG polyline gốc từ DB theo `part.Id`** (nguyên
vẹn cả chiều dài gốc), KHÔNG hề biết đoạn đã bị cắt — vì `FlatBarSplitter.BuildSegment` cố tình giữ
`Id`/`Transform` của polyline gốc (chỉ để tham khảo/report, xem mục 3), nhưng `PlateBuilder` lại DÙNG
đúng `Id` đó để dựng hình. 1 Id gốc giờ ứng với NHIỀU đoạn ⇒ mọi đoạn "không đầu nào tự do" (thường là
ĐA SỐ khi FB cắt qua nhiều HP) đều bị dựng thành bản sao windows nguyên vẹn CẢ THANH, chồng lấn nhau.

**Sửa:** bỏ hẳn nhánh `return null` đó — outline elevation (mục 1) đã TỰ suy biến đúng thành hộp chữ
nhật phẳng khi `free1=false && free2=false` (nhánh `else` của từng đầu), nên `FlatBarBuilder` giờ luôn
tự dựng lấy (kể cả hộp không vát) từ `part.MemberLength`/`Vertices` CỦA RIÊNG đoạn đang xét, không bao
giờ cần tới `PlateBuilder` nữa. Đồng thời bỏ hẳn `PartType.FlatBar` khỏi `PlateBuilder.CanBuild`/
`ZParams` (và khỏi fallback cuối cùng của `Build3DService.Dispatch`) — đảm bảo KHÔNG CÒN đường nào có
thể quay lại clone DB entity gốc cho FlatBar, kể cả khi `FlatBarBuilder` lỗi thật (region tự cắt…) —
trường hợp đó giờ chỉ đơn thuần bị bỏ qua (built=false, ghi report), an toàn hơn hẳn so với dựng sai.

**Bài học (cùng họ với các bug bbox-chéo ở [[panel-parts-reference]]):** bất kỳ builder nào dùng
`PlateBuilder.Extrude`/clone theo `part.Id` đều ngầm giả định **1 Id = 1 hình dạng đầy đủ, chưa bị
chia cắt** — giả định này ĐÚNG cho mọi loại khác nhưng SAI ngay khi 1 bước phân loại nào đó (ở đây:
`FlatBarSplitter`) tách 1 entity gốc thành nhiều `PanelPart`. Bất kỳ loại nào sau này bị tách tương tự
đều phải tự dựng từ `Vertices`/tham số hình học riêng, KHÔNG được rơi về `PlateBuilder` làm fallback.

## 3c. ⚠️ Bug — nhầm đỉnh vát: tụt `(h_fb−15)` trước rồi mới vát, thay vì đúng 15mm (ĐÃ SỬA, 2026-08-26)

Bản code đầu tiên viết đỉnh tại đầu tự do là `(L, −(hfb−FLAT_TOP))` — tụt hẳn `hfb−15` (VD 85mm nếu
hfb=100) ngay tại đầu mút TRƯỚC khi vát, thay vì đúng mục 1: từ cạnh hàn đi thẳng xuống ĐÚNG 15mm rồi
mới bắt đầu vát chéo xuống cạnh dưới. Kết quả: hình vát bị lộn — đoạn thẳng đứng ở đầu dài
`hfb−15` (gần như cả chiều cao) thay vì chỉ 15mm, sai hẳn hình dạng đối chiếu `1F851`.

**Sửa:** đổi 2 đỉnh của mỗi đầu vát từ `-(hfb - FLAT_TOP)` thành `-FLAT_TOP` (`FlatBarBuilder.Build`,
outline elevation) — khớp đúng mục 1/bảng công thức phía trên (tài liệu vốn đã ghi đúng, chỉ code
viết nhầm 2 biến `rise`/`FLAT_TOP` cho nhau khi đặt toạ độ đỉnh).

## 3d. Điều kiện chiều dài — chỉ Snip End khi đoạn ≥ 1 khoảng HP (xác nhận kỹ sư, 2026-08-26)

FB chỉ được vát khi **TỔNG chiều dài đường vẽ GỐC trong Plan View** (không phải chiều dài riêng của
từng đoạn sau khi tách) đủ **≥ 1 "khoảng HP"** — khoảng cách **LỚN NHẤT** giữa 2 đường HP song song
liền kề trong panel.

⚠️ **Sửa lần 2 (2026-08-26) — nhầm "tổng" với "từng đoạn":** bản đầu so `part.MemberLength` (chiều dài
CỦA RIÊNG 1 đoạn sau khi `FlatBarSplitter` tách) với `hpSpan` — SAI, vì mỗi đoạn sau khi tách thường
NGẮN HƠN cả 1 khoảng HP (bản chất tách là chia nhỏ 1 đường dài thành nhiều đoạn ~1 khoảng HP mỗi đoạn),
nên hầu như mọi đoạn đều bị chặn không vát. Xác nhận đúng của kỹ sư: **"chiều dài tổng"** = chiều dài
đường vẽ LIÊN TỤC GỐC (trước khi tách) — người vẽ 2D kéo 1 polyline liên tục xuyên qua HP/Web, và đó
mới là "chiều dài tổng" dùng để so ngưỡng, ÁP DỤNG CHUNG cho MỌI đoạn con sinh ra từ nó.

- Thêm field mới **`PanelPart.FbTotalPlanLength`** (chỉ set cho `PartType.FlatBar`) — `FlatBarSplitter`
  tính 1 lần (`L = Endpoints(p) trước khi tách`) rồi gán CHO MỌI đoạn con (và cho chính part gốc nếu
  không bị tách). `FlatBarBuilder` so `part.FbTotalPlanLength ≥ hpSpan` (KHÔNG phải `part.MemberLength`
  — field đó vẫn dùng riêng để tính hình học vát, run/rise, của từng đoạn).
- Tái dùng thẳng `GirderEndDetector.HpMaxSpan(hpParts)` — hàm này TỪNG là code chết (chỉ ghi chú "chờ
  dùng cho GB giai đoạn sau"), giờ dùng luôn cho Sniped End. Không cần sửa hàm đó.
- `HpMaxSpan` trả **0** nếu không đủ dữ liệu (<3 đường HP phân biệt) ⇒ coi như KHÔNG chặn gì (`L≥0`
  luôn đúng) — panel ít HP vẫn vát bình thường theo đầu tự do.
- ⚠️ **Chưa rõ**: "1 khoảng HP" có phải luôn là giá trị LỚN NHẤT toàn panel, hay nên là khoảng cách HP
  CỤC BỘ gần đúng vị trí FB đó (2 cách cho kết quả khác nhau nếu panel có vùng thưa/dày HP không đều)
  — đã chọn phương án LỚN NHẤT toàn panel theo xác nhận kỹ sư, nhưng CHƯA test trên panel có bố trí HP
  không đều để xác nhận đủ.

## 4. Chưa chốt / TODO

- [ ] **FB cắt ngang FB khác hoặc Web** (không phải HP) — user mới xác nhận case **HP**; chưa rõ 2
      trường hợp này có cần tách tương tự không (`FlatBarSplitter` hiện CHỈ xét `hpParts`).
- [x] ~~**Đoạn FB rất ngắn**~~ — một phần đã trả lời bằng điều kiện `L≥hpSpan` (mục 3d): đoạn quá ngắn
      (thường ngắn hơn cả khoảng HP) sẽ không được vát nữa, tránh hẳn case chồng vát 2 đầu trong đa số
      trường hợp thực tế. Vẫn giữ kẹp an toàn `run=L/2` trong `FlatBarBuilder` làm lưới an toàn cuối
      cùng cho trường hợp hiếm `lengthOk=true` nhưng `L` vẫn ngắn hơn `2×run`.
- [ ] **Đầu FB chạm mép Top Plate** (biên panel) mà không chạm Web/HP/FB nào khác — hiện bị coi là
      "tự do" (sẽ vát) theo đúng nghĩa đen "không hàn gì" — chưa hỏi/xác nhận đây có phải case thật
      hay chỉ là biên panel (có thể cần xử lý khác, ví dụ không vát vì được Flange/mép đỡ).
- [ ] Chưa test trực quan trong AutoCAD (build thử 1 panel có FB cắt qua HP + có đầu tự do, kiểm mắt
      hình vát 30°/60°, còn 15mm cạnh trên, và FB tách đúng đoạn tại mỗi HP).

Liên quan: [[panel-parts-reference]] (mục 1 bảng nhận diện Flat Bar) · [[girder-end]] (cùng khuôn
`member−15`, khác ở góc vát cố định thay vì R30/R50) · [[parametric-profiles]] (pattern
Region→Extrude→AlignCoordinateSystem dùng chung).
