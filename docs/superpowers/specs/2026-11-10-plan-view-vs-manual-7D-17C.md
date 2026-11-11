# Plan View — tool vs tờ tay 7D-17C (1:75), 2026-11-10

Nguồn: tay `CAS-0068762.dump.json`, tool `Drawing2.dump.json`. Cùng A1 (0,0) ×75, `7D-17C_Assy` tại (12330.9, 36104.7).
Mọi khoảng cách = mm giấy. Tấm 7D-17C **không chữ nhật**: góc dưới-trái bị cắt bằng cạnh xiên (165,468)→(298,185);
mép trên (y 481.4) và mép phải (x 376.9) là cạnh thẳng dài.

## A. Lỗi của tool (sửa chắc chắn)

| # | Hiện tượng | Nguyên nhân | Sửa |
|---|---|---|---|
| A1 | Mặt bằng lỗ lashing: lỗ lệch tấm (42.2, −140.3) mm | `<panel>_Lashing Hole` lồng trong `_Assy` tại (3170.36, −10522.73) (17P: 0,0); tool chèn trùng điểm Top Plate | Chèn theo transform lồng thật Lashing Hole/Top Plate trong `_Assy` |
| A2 | Kích thước bao 15586.1 / **998.8** (tay 16385.3 / 22465.5) | "dải 5 % sát cạnh trái/dưới" chỉ đúng với tấm chữ nhật | Mỗi chiều chọn phía có cạnh thẳng dài nhất (17C: dài ở TRÊN, rộng ở PHẢI; 17P: dưới/trái); `Rec. for tolerance` theo kích thước |
| A3 | Mặt cắt X-X/Y-Y: tấm chỉ 236→377 nhưng thanh trải 183→376; kích thước 3992.4, 6280.3, 5960.1 đo tới "đầu tấm" ảo | Cắt giữa panel trên tấm xiên; thanh gom từ cả panel | Tấm mặt cắt = dây cung dài nhất (tay: X-X 15925 theo mép trên, Y-Y 22465.5 theo mép phải) — **cần kỹ sư duyệt** (trước đó chốt "cắt giữa") |
| A4 | Kích thước rác 5.0 (469.5→469.6) chuỗi Y-Y | 2 đoạn web lệch 5 mm (vát) thành 2 điểm | Gộp điểm chuỗi cách nhau < 1 mm giấy |
| A5 | Chuỗi bước X-X thiếu nửa trái (182–241) | hệ quả A3 | hết khi sửa A3 |

## B. Khác do 3D / Naming (không phải Plan View)

- Naming tab: girder trái cùng mang tên **T3** (tay: T1…T9 trái→phải) ⇒ nhãn T1–T3 và member list lệch
  (tay tách "T1 : …FLANGE 150", tool tách "T3"). Web tại y≈469.5 tên **HL32** (Kind Web, layer AM_1); tay: L4, L5 tại 476.
- B: tay 101, tool 75. 34 chữ B tay xếp lưới cạnh T3–T8, hàng y ≈ 457/429/406/375/309/279/258/228/203 — 3D không có tripping ở đó.
- OB/OB1: lệch 5/9 vị trí (bracket 3D khác). GE1 (tay) vs GE ×2 + S (tool).

## C. Khác phong cách giữa 2 tờ tay (17P vs 17C)

| Mục | 17P | 17C |
|---|---|---|
| Ký hiệu liên tục | `CS_horl_up_right` (AM_0) | `CS_symbol` rot 0, AM_6, ×1.5 — vị trí khớp tool 145/151 trong 1 mm |
| Layer kích thước lỗ lashing | AM_4 | AM_5 |
| Tick chuỗi mặt cắt | theo nháp | xiên gần hết |
| Kích thước bao | dưới / trái | trên / phải (theo cạnh thẳng) |

## D. Vị trí: hằng số 17P không chuyển sang 17C

| Phần tử | Tay 17C (so với mép tấm) | Tool | Ghi chú |
|---|---|---|---|
| Kích thước dài | trên +37.4 | dưới 25.2 | A2 |
| Nhãn T / HT / BS | +46.2 / +47.6 / +52.3 | +21.0 / +25.2 / +31.8 | tay = kích thước bao + 8.8 |
| Mặt cắt X-X | +56.0 | +36.2 | |
| Chuỗi bước / girder X-X | cắt +9.7 / +24.6 | +15.8 / +25.3 | |
| Mặt cắt Y-Y | −69.7 | −46.3 | |
| Nhãn HL / L | Y-Y +5.9 / +7.6…22.7 (lệch tay) | +2.8 / +9.6 | |
| Chuỗi bước / girder Y-Y | cắt +12.1 / +26.4 | +12 / +21.9 | bước khớp |
| Tiêu đề Plan View | đáy tấm −36.6, lệch phải ~30 | −50.7, giữa tấm | |
| Tấm lashing | mép trái x 463.8 | 513.9 | |

Đề xuất: bố cục **xếp lớp theo hình học** — mỗi lớp (kích thước bao nếu ở phía đó → nhãn → mặt cắt → chuỗi bước → chuỗi girder)
đặt sau bao thực của lớp trước + khe cố định mm giấy, thay hằng số cách mép tấm.

## E. Có ở tay, tool không sinh

8 chữ `CG` (2 hàng × 4, y 336/342), `DET.5 (SCALE 1:25)` + khung AM_7, dấu chữ thập AM_7 + `RR_TRIANGLE` ×8 trên mặt bằng lashing,
1 ghi chú AMDTNOTE, 1 kích thước góc, đường chéo thứ 3 trên mặt bằng lashing.

## F. Quyết định kỹ sư + đã làm (2026-11-10)

Kỹ sư: (1) mặt cắt theo dây cung dài nhất — OK; (2) chuẩn công ty = 7D-17C; (3) bố cục xếp lớp; (4) CG = Continue Girder, ghi tại
girder liên tục cắt qua girder khác, không vẽ DET.5; (5) mục B ghi lại, làm sau.

| # | Đã làm | Nơi |
|---|---|---|
| A1 | Lashing Hole chèn theo độ lệch lồng thật trong `_Assy` (`LashingBlockOffset`) | `PlanViewSnapshot.ReadDrafts`, `SheetItemRules.LashingPlan` |
| A2 | Cạnh thẳng chính (`PlateGeometry.MainEdges`: nhóm cạnh ≤ 10 mm, lấy cạnh ngoài cùng); kích thước dài TRÊN khi cạnh dưới gãy (< 50 %), rộng PHẢI khi cạnh trái gãy; rộng = cạnh chính dưới → trên, mở tới đỉnh cạnh ngoài cùng | `PlateGeometry`, `DraftDimRules.Overall` |
| A3 | Tấm mặt cắt giữa 2 cạnh chính (X-X 15925, Y-Y 22465.5); đường chéo đo kiểm cùng hình chữ nhật | `SectionRules.Build`, `SheetItemRules.Diagonals` |
| A4 | Gộp điểm chuỗi cách ≤ 1 mm giấy; bỏ kích thước đầu mút ≤ 1 mm | `SectionRules.Dedupe` |
| D | `CS_symbol` AM_6 (0°/90°); kích thước lỗ lashing AM_5; mọi kích thước chuỗi mặt cắt tick xiên | `SymbolRules`, `DraftDimRules`, `SectionRules` |
| CG | Chữ `CG` AM_6 cao 3: dọc girder +0.9 mm, lệch 3.2 mm về girder song song gần nhất | `SymbolRules.ContinueGirder` |
| Bố cục | Hằng số 17C: nhãn T/HT/BS 46.2/47.6/52.3, X-X 56.0, chuỗi 9.7/24.6; Y-Y 68.8 (+20 khi kích thước rộng bên trái), HL/L = Y-Y + 5.9/16.8, chuỗi 12.1/26.4; kích thước bao 37.4 / 49.4; tiêu đề 36.1 dưới tấm (26 dưới kích thước dài nếu nó ở dưới), lashing 52.5; tấm lashing = mép phải mặt bằng chính + 36.5; chuỗi lashing trên 13.0, dọc 18.5 (phía cạnh thẳng), COG 25.1 / 28.5; "(<>)" ở MỌI đầu chuỗi chưa tới cạnh chính, không đổi nhãn đoạn cuối | `PlanViewLayout` + các rules |

Test: `PlanView7D17CTests` (đường bao thật 77 đỉnh của 7D-17C). Hệ quả với 7D-17P: tấm lashing dời trái 13.7 mm, nhãn/mặt cắt xa tấm hơn,
lashing AM_5 — theo chuẩn 17C. Chưa làm: nhãn L lệch tay từng dòng (tay 7.6…22.7), tiêu đề lệch phải ~28 mm trên tay 17C,
`Rec. for tolerance` tay 17C scale 50 (tool = tỉ lệ tờ 75), đường song song cạnh xiên + RR_TRIANGLE trên mặt bằng lashing.
Mục B (Naming T3/HL32, thiếu tripping) để sau.

## G. Lần chạy 2 (dump 16:05) và chỉnh tiếp

Kết quả: nhãn T/HT/BS/HL khớp 0 mm, tiêu đề đúng cao độ, Top Plate/Lashing Hole khớp 0 mm, CS_symbol 146/151 trong 2 mm,
kích thước AM_5 khớp 134/155 (82 trùng đường 0 mm). Chỉnh tiếp:

- Kích thước rộng 45.8 (đo lại từ cạnh 376.95); tấm lashing = mặt bằng chính + 40.2; chuỗi dọc lỗ lashing 19.4, COG 29.4 (đo từ cạnh 681.3).
- Mặt cắt: mỗi tuyến lấy đoạn THẲNG TRỤC dài nhất (girder biên T1 chạy dọc cạnh xiên — trước bị mất web, flange lạc ở 3332);
  2 web cách ≤ 1 mm giấy chỉ giữ web dài hơn (8382/8422); kích thước đầu mút giữ cả khi < 1 mm giấy (59.5, 60); chuỗi girder
  X-X không tick xiên, Y-Y xiên (như tay).
- Lashing: nháp cục bộ giữ chữ + DIMTMOVE của nháp; đầu chuỗi hở ≤ 1 mm giấy ⇒ kéo đoạn cuối tới cạnh, ghi "(<>)" (172.3 → 172.9)
  thay vì thêm kích thước 5.6; COG ngang ghi "<>".
- CS/CG: web girder tách đoạn tại web khác vẫn liên tục nếu FLANGE cùng tuyến liền qua giao điểm (L2 × T4); giao điểm cùng tuyến
  < 50 mm gộp 1 (trước nhân đôi ở L3 × T4).

Còn lại (không sửa, do tờ tay tự chỉnh): tiêu đề lệch phải ~29 mm; M.D1/M.D2 đặt tay; nhãn L lệch tay; kích thước lashing
cục bộ đặt tay (lệch 1–9 mm); 670/340.1/420 nháp ↔ tay chia lại 620/415.1/395; nháp 195 đầu chuỗi ngang tay hạ xuống 6.1 mm +
"(<>)" 694.9 tới lỗ đầu; tay không ghi CG ở T8 dù có CS; Rec. for tolerance dọc tay đặt lệch tâm 10.7 mm, scale 50.

## H. Phản hồi kỹ sư: "mặt cắt X-X, Y-Y chưa chuẩn; kích thước chồng chéo" (phân tích dump 16:05)

Mặt cắt:
- **Lỗi writer**: profile trong block mặt cắt chèn KHÔNG áp cờ lật (MirrorX) ⇒ HP không bao giờ lật (tay Y-Y lật 13 HP: HL1, HL21…HL31).
  Hệ quả phụ: điểm đo chuỗi bước tại mặt lưng HP lệch 6 mm (775 ↔ 769, 505 ↔ 499, 464.7 ↔ 470.7). Đã sửa (`PlanViewWriter.DefineSection`).
- Y-Y vẽ tấm −t…0 (quy ước 17P) ⇒ toàn bộ mặt cắt lệch 1 chiều dày vào trong; tay 17C: tấm 0…t như X-X. Đã đổi.
- X-X: girder biên T1 mất web, flange lạc (đã sửa ở mục G — đoạn thẳng trục); web thừa 8422 (gộp); nháp Structure "800" đo tới
  x 359.1 không có thanh ⇒ chỉ chép nháp có 2 đầu trên mép tấm / web / profile.
- Còn: HP "HT1" (x 261.3) chỉ có ở tool — thuộc mục B (Naming), không sửa ở Plan View.

Chồng chữ (hộp chữ thực rộng n × 2.4, cao 3 mm): tay 16 cặp, tool 16:05 33 cặp + 4 đoạn đường trùng. Nguyên nhân: chữ không lọt giữa
2 gióng ⇒ AutoCAD đẩy ra ngoài dọc đường ⇒ đè chữ bên cạnh; tay dùng DIMTMOVE = 1 + đặt chữ giữa khoảng, nâng 4.1–6.5 mm.
Quy tắc mới `DimTextLayout`: chữ chật ⇒ giữa khoảng, nâng 6.5 mm ra phía ngoài, chữ chật liền kề chạm nhau ⇒ tầng 2 (+3.8 mm).
Mô phỏng trên dump 16:05: 33 → 1 cặp (cặp còn lại + 4 đoạn trùng đều do lỗi X-X đã sửa).

## I. 7D-17P (dump 16:29): kích thước rộng, hình chiếu lọt khỏi khung

Khung trong A1 (AM_BOR) x 16…816, y 8…576 mm giấy. 7D-17P 1:50: tấm x 93.2…419.2 ⇒ lề trái chỉ 77 mm (17C: ~142 mm).
Bộ khoảng cách chuẩn 17C đặt kích thước rộng 45.8 + dời 20 ⇒ Y-Y ở x 4.4, chuỗi Y-Y x −7.7 / −22 (ngoài khung); X-X +56 (tay +36.2).
Tờ tay 17P vẫn dùng đúng bộ gọn đã hiệu chỉnh trước (Y-Y 45.3, X-X 36.2, rộng 13.2, dài 24.7, tấm lashing 469.4).

Sửa: `LayoutProfile` — Standard (17C) + Compact (17P); `PlanViewBuilder.Build` dựng Chuẩn, đo bao mọi nội dung (`Overflow`, chữ
kích thước ±5 mm), lọt khung ⇒ dựng lại Gọn, lấy bộ tràn ít hơn; vẫn tràn ⇒ thông báo cảnh báo số mm. Thông báo kết quả ghi bộ đã dùng.
Test: `LayoutFitTests` (17P ⇒ Gọn, trọn khung; 17C ⇒ Chuẩn).
