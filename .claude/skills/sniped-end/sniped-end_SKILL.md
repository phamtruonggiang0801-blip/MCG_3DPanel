---
name: sniped-end
description: Quy cách SNIPED END của Flat Bar (FB) VÀ HP — vát chéo góc ở đầu tự do (không hàn Web/HP/FB nào khác). Công thức rise/run (FLAT_TOP + góc 30°/60° cố định) + quy tắc tách FB tại chỗ giao HP/Web trước khi xét đầu tự do. Đọc khi nhận diện hoặc dựng FlatBar / HP.
---

# Sniped End — vát chéo đầu tự do của Flat Bar / HP

**Sniped End KHÔNG áp dụng cho mọi đầu** — chỉ đầu nào **THỰC SỰ TỰ DO** (không hàn Web/Bracket/
GirderEnd/HP/FB nào khác) mới bị vát. Đầu chạm 1 member khác giữ nguyên vuông sắc (coi như đã "nối") —
xác nhận kỹ sư, 2026-08-26.

**FLAT_TOP = 15mm** cho Flat Bar (`FlatBarBuilder.FLAT_TOP`). Góc vát 30°/60° + `rise=h−15`,
`run=rise·tan60°`.

## ⚠️ HP Sniped End — CÓ ĐIỀU KIỆN (2026-09-04, xác nhận kỹ sư)

`HpBuilder` **KHÔNG** vát đầu tự do bình thường nữa. HP chỉ vát khi đầu HP là chỗ **"End 30"** (kết thúc
cách 1 Web ~⟂ đúng 30mm) **VÀ** bracket **OB/IB gần nhất trên HP cách đầu HP < `reach`**
(`reach = max(180, 30 + 1.5·b)`) → không đủ chỗ đặt bracket "B" → vát góc dưới-ngoài (flat 15 + 30°/60°).

- "End 30" + `dist(OB/IB → đầu HP) ≥ reach` → **bracket "B"**, KHÔNG vát.
- "End 30" + `dist(OB/IB → đầu HP) < reach` → **vát** (SnipHpEnd), KHÔNG bracket "B".
- KHÔNG phải "End 30" nhưng **TỰ DO** (`GeometryUtil.IsFreeEnd`, không chạm member nào trong 30mm) →
  **vát** (VD stub HP chéo ở góc cong panel — 2026-09-04, kỹ sư hỏi `(99041.6, 16420.4)`).
- KHÔNG phải "End 30" + chạm 1 member → giữ vuông sắc.

Quyết định: `HpEndBracketBuilder.WantsEndBracket(ctx, hp, he, other, out isEnd30)`. Chi tiết bracket "B":
**§5**. `hp_end_brkt_1` "IF≥50" · `hp_end_brkt_2` "IF<50" (`Bracket B.dwg`). Phần §1–§3g dưới đây chỉ còn
áp dụng cho **Flat Bar** và **BackingPlate** (hình học vát 15/30-60 dùng chung).

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

## 3e. FB đứt tại HP **VÀ mọi member AM_0** + CHỪA KHE = bề dày member (xác nhận kỹ sư 2026-08-27)

Mở rộng mục 3: FB không chỉ đứt tại HP mà tại **MỌI member AM_0 đi xuyên ngang** (Web dài, web-tier
ngắn, Bracket, ClosedPlate — kỹ sư liệt kê rõ; TRỪ lỗ khoét CutOut). Và 2 đoạn FB mới **cách nhau đúng
bề dày member cắt ngang** (HP t6 → khe 6mm; Web t10 → khe 10mm) — mỗi đoạn dừng tại 1 MẶT của member,
không phải tại đường tâm.

**Cài (2026-08-27)**:
- `ClassifyUtil.CrossesThrough(p, m, out double tCross)` viết lại: giao 2 ĐƯỜNG TÂM (`Endpoints`) thay
  vì chiếu bbox — thêm 2 điều kiện: (a) **|cos(góc 2 trục)| ≤ 0.85** → loại member chạy gần SONG SONG
  cạnh FB (Web dọc bên FB không phải "đi ngang qua"); (b) giao điểm phải nằm TRONG `m` (cách 2 đầu `m`
  ≥ `max(FB.thk, 3)`) → loại **T-junction** (member kết thúc TẠI FB, FB vẫn liền). `tCross` = vị trí
  giao chiếu lên trục FB (từ `e1`), làm TÂM khe.
- `FlatBarSplitter.Split(fbParts, hpParts, crossMembers)` — thêm tham số; `crossMembers` = `am0` TRỪ
  CutOut (lọc trong `ClassificationService` bằng đúng điều kiện CutOut: cùng block Top Plate + nằm hẳn
  trong biên — vì lúc gọi `am0` còn `Unknown`). Mỗi cut mang `half = m.FootprintThickness/2`; đoạn con
  dựng từ `tStart = prevCut.t + prevCut.half` tới `tEnd = cut.t − cut.half` (bỏ đoạn < 1mm).
- `ClassificationService`: chuyển lời gọi `FlatBarSplitter.Split` xuống SAU 2 vòng phân loại CutOut
  (để lọc được lỗ khoét) nhưng vẫn TRƯỚC vòng `am0` chính / `WebBracketSplitter` (vì `touches`/
  `IsBracketPattern` cần list FB đã tách — giữ nguyên thứ tự đó).

⚠️ **CHƯA test trực quan** — build 7D-17C xem: (1) FB cắt tại Web có khe đúng; (2) `CrossesThrough`
chặt hơn (loại T-junction) không làm MẤT split HP nào kỹ sư mong đợi; (3) không over-split ở web-tier.

## 3f. `HpMaxSpan` — quy tắc "1 khoảng HP" (ĐÃ CHỐT, xác nhận kỹ sư 2026-08-27)

**Triệu chứng** (7D-17C): 4 FB song song đầu tự do ở `y≈27009` — chỉ 3/4 được Snip; `390FF`
(totalPlanLen 4274) không, vì `hpSpan` cũ = **5372** (`390FF` 4274 < 5372 → `lengthOk=false`).

**5372 từ đâu**: = khoảng cách X giữa **2 HP bulb CHÉO** ở khúc cong panel (`38EB8` cx≈91021 &
`390E6` cx≈96393). Bbox 2 cái này cao > rộng → `IsHorizontal=false` → bị xếp nhầm vào "nhóm HP đứng"
cùng `38EE9`(99039)+cụm mép(104355/104515); "max adjacent gap" lấy 96393−91021 = 5372.

**Quy tắc kỹ sư (chốt)** — `GirderEndDetector.HpMaxSpan(ClassificationResult cls)`:
1. `hpSpan` = khoảng cách **LỚN NHẤT** giữa 2 đường HP **liền kề**.
2. Nếu GIỮA 2 đường HP đó có 1 **Web** (girder CÙNG PHƯƠNG, `MemberLength > WEB_MIN_LEN`) → 2 HP đó
   KHÔNG "liền kề" → **BỎ** khoảng đó.
3. Panel có cả HP dọc + ngang → lấy khoảng của phương **nhiều đường HP hơn**.
4. (Ngầm) HP CHÉO (`ClassifyUtil.IsDiagonalBloatedBbox`) bị loại hẳn — thanh viền/vát góc, không field.

7D-17C: ngang 33 đường HP ≫ đứng 3 → dùng ngang. Các gap lớn 826/867/970 đều có 1 girder Web ngang
ở giữa (`Y≈16206 / 27243 / 28181`) → bỏ; mọi gap sạch còn lại ≈ **775** → `hpSpan = 775`. `390FF`
(4274 ≥ 775) → **vát**. Stub 160–540mm (< 775) → không vát. Đúng "4/4".

⚠️ `HpMaxSpan` giờ nhận `ClassificationResult` (cần cả Web) — caller `FlatBarBuilder` đã đổi. Chạy ở
build-phase nên Web đã `PartType.Web`.

## 3g. Sniped End cho BackingPlate — flat=15mm SÁT CẠNH KHÔNG VÁT, chéo suốt phần còn lại (ĐÃ ĐỐI CHIẾU MẪU, 2026-08-28)

Khác FlatBar/HP (1 cạnh HÀN cố định vuông sắc suốt chiều dài + 1 cạnh TỰ DO bị vát 1 góc/đầu):
BackingPlate KHÔNG có cạnh nào "hàn cố định" theo chiều cao — cả cạnh trên (gần Top Plate) lẫn cạnh
dưới (gần Flange) đều THỤT 30mm, không chạm gì (`PlateBuilder.ZParams`).

**Đối chiếu mẫu thật** (`CAS.dump.json`, `*Model_Space`, polyline `1F255`, layer `Mechanical-AM_0`,
6 đỉnh — khớp CHÍNH XÁC kích thước part test `38ED5` L=50, H=314):
```
V0(1403.4476,903.908) V1(1438.4476,843.2862) V2(1438.4476,650.5298)
V3(1403.4476,589.908) V4(1388.4476,589.908) V5(1388.4476,903.908)
```
Quy về local (gốc = V4, x=dọc trục dài, y=cao): `(15,0)→(50,-60.62)→(50,-253.38)→(15,-314)→(0,-314)→(0,0)`.
Đo tay: cạnh TRÁI (x=0, V4-V5) dài ĐỦ 314 = H — KHÔNG vát, còn nguyên vẹn (đại diện đầu KHÔNG tự do,
"lưng sát Web" = mép còn nguyên). Cạnh TRÊN từ (0,0) đi 15mm phẳng tới (15,0) — ĐÂY mới là `flat=15`,
nằm SÁT cạnh không vát, KHÔNG nằm ở đầu tự do. Từ (15,0), đường CHÉO kéo dài suốt phần CÒN LẠI của
chiều dài (`reach = L−15 = 35`) chạm THẲNG mép đầu tự do (x=50) — góc đó bị cắt trụi, không giữ flat.
Độ dốc: `run (trục cao) = reach·tan(60°)` = 35×1.7320 = 60.62 — khớp khít toạ độ Y đo được
(903.908−60.6218=843.2862 ✓).

**⚠️ Đã sửa SAI 2 lần trước khi khớp mẫu**: lần 1 vát cả `rise=H−FLAT` (đường chéo khổng lồ, sai vì
tấm này không cạnh nào "hàn cố định" để làm chuẩn trừ). Lần 2 đặt `flat=15` NGAY TẠI đầu tự do (dọc
trục dài) rồi suy trục cao qua `tan60°` (rập khuôn kiểu "chamfer góc nhỏ") — kỹ sư đo thực tế ra ~24mm
(không phải 15) → SAI, vì flat KHÔNG nằm ở đầu tự do mà nằm sát đầu KHÔNG vát (ngược hẳn vị trí).

**Cài** (`Services/Builders/BackingPlateBuilder.cs`, đăng ký TRƯỚC `PlateBuilder`, cùng pattern
`FlatBarBuilder`: outline elevation local → Region → extrude `FootprintThickness` → `AlignCoordinateSystem`
→ dịch `(0,0,−30)`):
- Đúng 1 đầu tự do (VD `38ED5`: free1=True): `flat=15` đặt sát đầu KHÔNG tự do, `reach=L−15`,
  `run=reach·tan60°` (kẹp `≤H/2`), đường chéo chạm thẳng mép đầu tự do (không giữ flat ở đó).
- KHÔNG đầu nào tự do: hộp phẳng nguyên vẹn (như cũ).
- **CẢ 2 đầu tự do**: CHƯA có mẫu đối chiếu (mẫu chỉ có 1 đầu neo) — phỏng đoán AN TOÀN: chia đôi
  chiều dài, dải flat 15mm nằm GIỮA tấm, mỗi nửa tự áp `reach=L/2−7.5` độc lập. **Cần xác nhận riêng**
  nếu gặp panel có backing plate cả 2 đầu tự do.

⚠️ Vẫn nên kiểm mắt trong AutoCAD 1 lần nữa để xác nhận khớp mẫu — đặc biệt case cả 2 đầu tự do (chưa
có dữ liệu đối chiếu).

## 4. Chưa chốt / TODO

- [ ] **BackingPlate Sniped End** — xem mục 3g, đã sửa 1 lần theo số đo thực tế, VẪN chưa đối chiếu
      bản vẽ mẫu — có thể còn cần sửa tiếp sau khi kỹ sư xem/đo lại bản dựng 3D mới.
- [x] ~~**FB cắt ngang FB khác hoặc Web**~~ — xác nhận kỹ sư 2026-08-27: đứt tại MỌI member AM_0 (mục 3e).
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

---

## 5. Bracket đầu HP — mã "B" (`Bracket B.dwg`, đang PHÂN TÍCH 2026-09-04)

Thay cho Sniped End (đã bỏ): nơi HP **kết thúc cách 1 Web ngang một khe ~30mm**, thêm 1 **gusset knee**
nối **Web ngang + Flange** đỡ đầu HP. Nguồn: `Bracket B.dump.json` (`*Model_Space`, 4 detail) —
2 block `hp_end_brkt_1` / `hp_end_brkt_2` + dims.

### 5a. Hình học (elevation — mặt phẳng chứa TRỤC HP + Z) — ĐÃ CHỐT tham số 2026-09-04

Tấm phẳng đứng, cùng mặt phẳng thân HP. Hệ local: gốc = (mặt gần Web ngang) ∩ (mặt trên Flange);
`+x` = ngược trục HP (vào khoang, về phía thân HP); `+y` = lên (+Z).

| ký hiệu | ý nghĩa | công thức (ĐÃ CHỐT) | HP120/H374 |
|---|---|---|---|
| `thk` | bề dày tấm | **= `HP.FootprintThickness`** | 6 |
| `Ht` | chiều cao (đáy Flange → đáy HP) | **`WebHeight − b`** (đáy HP = `Z=−b`) | 254 |
| `reach` | vươn dọc trục HP | **`max(180, 30 + 1.5·b)`** | 210 |
| `gap` | HP lùi khỏi Web ngang | **30** cố định | 30 |
| `R` | cung toe lõm (góc Web–đỉnh) | **50** cố định | 50 |
| `topL` | mép trên bắt đầu (sau cung R) | **= `R` = 50** | 50 |
| `e15` | mép đầu tự do đứng | **15** cố định | 15 |
| `off2` | brkt_2: đáy cách Flange | **30** cố định | 30 |
| `d1` | mép hàn dọc Flange | **2 biến thể theo `overhang`** (như OB/IB under-50) | 149 |

**Selector + `d1`** (`overhang` = mép Flange phía HP − mặt Web ngang phía HP):
- `overhang ≥ 50` → **`hp_end_brkt_1`**, `d1 = overhang − 15` (kiểm: 164−15 = 149 ✔), đáy hàn Flange (y=0).
- `overhang < 50` → **`hp_end_brkt_2`**, `d1 = 15`, đáy NÂNG `off2 = 30` phía trên Flange.

Polygon brkt_1 (CCW): `(0,0)→(d1,0)→(reach, Ht−e15)→(reach, Ht)→(topL, Ht) —cung R lõm→ (0, Ht−R)→(0,0)`.
Polygon brkt_2 (CCW): `(0,off2)→(d1,off2)→(reach, Ht−e15)→(reach, Ht)→(topL, Ht) —cung R→ (0, Ht−R)→ (0,off2)`.

### 5b. Phát hiện vị trí "End 30mm" + chọn bracket vs snipe

1. Đầu mút HP (`ClassifyUtil.Endpoints`) cách 1 **member ngang** một khoảng `|nearFace − 30| ≤ 16` dọc
   trục HP (hiệu chỉnh xiên), `|cos góc trục| ≤ 0.70` → chấp nhận member XIÊN. → "End 30". (HP xuyên QUA
   web = lỗ CO — xem [[web-hp-cutout]]. Case này HP DỪNG trước member.)
   **"Member ngang" = `PartType.Web` + `GirderEnd` + `ClosedPlate`** (`HpEndBracketBuilder.TRANSVERSE_TYPES`,
   sửa 2026-09-04): vài bản vẽ (vd **4D-03P**) vẽ vách/cột biên — nơi HP dừng lại 30mm — thành cụm **tấm
   kín**, `PlateDetector` xếp `ClosedPlate` chứ không phải `Web`. Chỉ xét `Web` thì đầu HP đó bị coi "tự
   do" → Sniped End thay vì bracket B (kỹ sư báo: HP end `(1696.78, 1823.82)` / `(12886.79, 1823.82)`).
2. Tính `dKnee` = khoảng cách từ đầu-bám-HP của bracket **OB/IB** gần nhất (ngồi trên HP đó) tới đầu HP.
3. `dKnee ≥ reach` → **bracket "B"**. `dKnee < reach` → **Sniped End** (HpBuilder). Không có OB/IB →
   `dKnee = ∞` → luôn bracket "B".

### 5c. Đặt 3D

- `hpAxis` = trục HP; `hpEnd` = đầu mút gần Web; Web ngang ở `hpEnd + gap·hpAxis` (gap≈30).
- Gốc local = giao (mặt gần Web ngang) × (mặt trên Flange = `Z = −WebHeight`), trên tuyến thân HP.
- `local x → −hpAxis` (vào khoang), `local y → +Z`, `local z → phương bề dày tấm`.
- **Mặt đặt tấm** (SỬA 2026-09-04 sau test): **CÓ backing mặt đối diện** (HP/Web/FlatBar/Bracket/GE
  khác kết thúc mặt bên kia cùng Web ngang) → tấm **NẰM NGAY DƯỚI HP** (đồng phẳng thân HP, tâm trùng
  tuyến HP, không lệch). **KHÔNG backing** → tấm **lệch `1·thk` (6mm) về phía BỤNG** (KHÔNG phải phía
  lưng như bản đầu).
- Region từ polygon (5a) → extrude `thk` → `AlignCoordinateSystem` (pattern chung, như `BracketBuilder`).

### 5d. Đã chốt (2026-09-04) — xem bảng 5a

`thk` = dày HP · `reach = max(180, 30+1.5b)` · `Ht = WebHeight − b` · `d1` 2 biến thể theo `overhang`
(≥50: `overhang−15` + đáy trên Flange; <50: `d1=15` + đáy nâng 30) · `gap/R/topL/e15/off2` hằng số ·
mặt đặt: 6mm sau lưng (đơn) / đồng phẳng lưng (cặp đối xứng).

### 5e. Chốt hết + cài đặt (2026-09-04)

- [x] `overhang` = (mép Flange về phía khoang HP, cùng hướng `reach`) − (mặt gần Web ngang). ✔
- [x] Detail 1 & 2 = chỉ ký hiệu hàn, KHÔNG solid → BỎ QUA. ✔
- [x] "bracket đối xứng" = có **HP HOẶC Web HOẶC mã** khác kết thúc ở mặt ĐỐI DIỆN cùng Web ngang
  (~thẳng hàng, trong `gap + wThk + tol`). Có → đồng phẳng lưng HP; không → lùi thêm `1·thk`. ✔
- [x] `gap` = **đúng 30mm** (dung sai phát hiện ±14). ✔
- [ ] "B" mã con theo dày — chưa hỏi, hiện dùng `thk = dày HP` cho mọi cỡ.

**Cài** (`Services/Builders/HpEndBracketBuilder.cs`, `internal static`, KHÔNG `IPartBuilder` — gọi sau
`WebCollarPlateBuilder` trong `Build3DService`, append solid + report `[BracketB] solid <handle> = <tag>`).
Solid **cùng Layer với Web ngang** (không phải HP).
- `FindTransverseWeb`: Web mà tuyến tâm HP cắt qua (`|cos| ≤ 0.94` — chấp nhận **Web XIÊN**, chỉ loại
  ~song song); `nearFace` = khoảng dọc trục HP tới mặt gần Web, hiệu chỉnh xiên `wThk/2 / |outward×wAxis|`;
  `|nearFace − 30| ≤ 16`.
- `FlangeOverhang`: Flange gần điểm giao nhất, chiếu `Vertices` lên −`outward` từ mặt Web ngang.
- `HasOppositeTermination`: quét HP/Web/FlatBar/Bracket/GE kết thúc mặt đối diện.
- `Build`: polygon 6 đỉnh (5a) → `RegionFrom` → `AlignCoordinateSystem`(localX=−outward, localY=+Z,
  localZ=`belly`) → extrude `belly·thk`. Gốc tại mặt Web ngang ∩ `Z=−WebHeight`; **có backing** → tâm
  trùng tuyến HP (`o = p − belly·t/2`); **không backing** → lệch thêm `belly·t`.

### 5f. Test lượt 1 (2026-09-04) — sửa

- Layer → theo **Web ngang** (trước: HP).
- Mặt đặt: **backing → tấm nằm giữa thân HP** (không lệch) · không backing → lệch `1·thk` về **BỤNG**
  (bản đầu lệch nhầm hướng + lệch cả khi có backing).
- `FlangeOverhang`: CHỈ xét flange **chạy dọc trục HP** (`|fAxis·hpAxis| ≥ 0.7`) — trước lấy nhầm flange
  chạy dọc Web ngang → `overhang` phồng 1500+ (d1 clamp 195, hình sai).
- `HasOppositeTermination`: nới `reachOpp = 2·GAP + wThk + 25`, `latTol = wThk + 30` (đối diện cũng lùi 30).
- `PERP_COS` 0.34 → **0.70** (bắt Web XIÊN tới ~45°, vẫn loại ~song song). ⚠️ Web CHÉO góc cong panel
  (`38E57`/`38EB8`, bbox phồng) hiện cũng qua — HP đầu biên panel nhận bracket. **Chưa biết đúng/sai** —
  hỏi kỹ sư.

⚠️ **Còn phải test:** (1) không lật/mirror; (2) brkt ở web xiên/chéo góc panel — đúng hay thừa;
(3) `sym=True` khi HP 2 phía Web ngang; (4) hình khớp `hp_end_brkt_1/2`.
