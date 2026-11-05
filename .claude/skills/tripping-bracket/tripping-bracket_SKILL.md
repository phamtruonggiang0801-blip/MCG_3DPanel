---
name: tripping-bracket
description: Quy tắc Tripping Bracket — bracket "B" dưới HP xuyên web dầm có flange, chọn sao cho khoảng giữa các gối dọc mỗi ĐOẠN DẦM ≤ 3 × bước stiffener max, đi từ đầu chiều đổ (Center: 2 đầu vào tâm); tab Result đảo phía và ép Auto/On/Off theo HP. Đọc khi nhận diện/dựng tripping bracket, đoạn dầm, hoặc sửa bracket B dùng chung.
---

# Tripping Bracket — bracket "B" chống vặn xoắn dọc đoạn dầm

Luật hiện hành: **kỹ sư 2026-11-05** sau khi test panel 7D-16C (thay luật "1 bracket gần giữa đoạn" ngày 2026-11-04).
Spec: `docs/superpowers/specs/2026-11-05-tripping-bracket-spacing-design.md` (hình/phía: `2026-11-04-tripping-bracket-design.md`).

## 1. Nguồn gốc

Đăng kiểm: khoảng giữa các gối chống vặn xoắn dọc dầm **≤ 3 × bước stiffener lớn nhất**. Ghi chú cũ trong
`CAS.dump.json` ("1 bracket closest to beam segment middle") KHÔNG còn dùng — kỹ sư xác nhận sai trên 7D-16C.

## 2. Quy tắc (kỹ sư 2026-11-05)

| # | Quy tắc |
|---|---|
| R1 | Chỉ đặt tại giao điểm **HP xuyên web dầm có flange** (có lỗ CO; không tính FB). Không còn luật "dầm ngoài cùng": web biên HP dừng tại đó (7D-16C: `26963`, `26968`) tự không có bracket. |
| R2 | **Đoạn dầm** = phần dầm giữa 2 gối liên tiếp; gối = dầm liên tục có flange cắt ngang **hoặc mút của chính dầm** (dừng ở girder / faceplate / web biên). |
| R3 | L = 3 × s_max; s_max = khoảng lớn nhất giữa 2 HP kề nhau trên cùng 1 đoạn, **max toàn panel**. Từ gối đầu, chọn HP **xa nhất** cách gối ≤ L (+1mm), HP đó thành gối mới, lặp tới khi khoảng còn lại ≤ L. Không HP nào trong L ⇒ lấy HP kế tiếp. Đoạn ≤ L ⇒ không bracket. |
| R4 | Chiều đi = **chiều đổ**: X trái→phải; Port Y trên→dưới; Starboard Y dưới→lên; **Center 2 đầu vào tâm** (TopPlate centroid) — đoạn qua tâm chia đôi, mỗi nửa chỉ chọn HP của mình; giữa gần nhau chấp nhận. |
| R5 | Tab Result, dòng **HP**: nút **Tripping Auto → On → Off** (`PartCorrection.Tripping`). On = mọi giao điểm R1 của HP; Off = bỏ hết; **không tính lại** Auto. Xoá từng bracket = menu Delete; ⇄ đảo phía. |
| R6 | **Hình = bracket B** (`hp_end_brkt_1/2`, [[sniped-end]] §5a), mép đứng hàn thẳng mặt web dầm; tấm đồng phẳng thân HP, dưới HP (`Z=−b`) tới `Z=−WebHeight`; **phía** = chiều đổ (tính tại giao điểm) chiếu lên pháp tuyến web. |

**7D-16C (Center, centroid Y≈7678, s_max 775 ⇒ L 2325):**

| Đoạn | HP chọn |
|---|---|
| Vùng dưới, dầm x≈4226/6166/8106/10046/11986 (Y 995–10607) | 3007, 5332, 7651 (nửa dưới) + 8385 (nửa trên) ⇒ 20 |
| Vùng trên, dầm x≈11986 (tới Y 13940) | 12260 |
| Vùng trên, dầm x≈10046 (tới Y 13088) | 11004 |
| Vùng trên, dầm x≈6166 / 8106 | không (đoạn ≤ L) |

Danh sách kỹ sư đưa lúc đầu (26875/26879/2687B/2687E/26889 = đếm từ Y nhỏ) chỉ khớp khi coi là Starboard; kỹ sư
chọn chấp nhận kết quả luật Center, chỉnh tay ở tab Result. HP 7651 cách tâm ~27mm ⇒ nhạy với centroid.

## 3. Cài đặt

- **Luật thuần** `Services/Builders/TrippingBracketRules.cs` (test `TrippingBracketRulesTests`, số thật 7D-16C):
  `Segments(cuts, runLen, endTol)`, `MaxSpacing`, `PickBySpacing(s, lo, hi, L, startSign, split)`, `Side`,
  `SelectBracketB`, `BracketBPolygon` (2 hàm cuối dùng chung với bracket B "End 30" — `HpEndBracketBuilder`).
- **Builder** `Services/Builders/TrippingBracketBuilder.cs` — **2 lượt**: (1) span từ `WebRuns`, dầm = run có
  Flange ∥ tại tâm 1 part; điểm cắt = run có flange khác (|cos| ≤ 0.7) mà CG phủ giao điểm còn dầm không phủ;
  `Segments` thêm 2 mút run; HP = `CrossesThrough` + kẹp `[b/2, L−b/2]`. (2) L từ `MaxSpacing` mọi đoạn;
  startSign = dấu `doz(mid)·trục`; Center ⇒ `split = (center.Y − P1.Y)/axis.Y`; áp `Tripping` theo HP SourceKey.
  Đặt `P = X + away·(tW/2)/cos`; `HpEndBracketBuilder.BuildPlate(..., centered: true)`; Layer/Color = web.
- ⚠️ **Bẫy:** `WebRuns` gom web thẳng hàng khe ≤ 20 ⇒ 2 đoạn 2 bên 1 dầm liên tục thành 1 run — builder TỰ CẮT.
- **Khoá:** `TrippingBracket:<web SourceKey>~<HP SourceKey>` = `DeleteKey` = `FlipKey`. Ép theo HP lưu trên
  SourceKey của HP (`ResultRow.CanTrip/TripLabel`, `PanelResultView.BtnTrip_Click`).
- Report: `s_max=.. → L=..`, mỗi bracket `dầm .. đoạn s=[..] · HP .. s=.. Δ=../L=.. (auto|tay On) · side=..`,
  `SKIP .. không cần (.. HP, dài .. ≤ L)`.

## 4. Theo dõi

- [ ] 1 đoạn thiếu HP (khe lớn) làm s_max ⇒ L của CẢ panel phình — xem dòng `s_max` trong report.
- [ ] HP quá xiên so với web dầm (cos < 0.3) ⇒ bỏ, không Report riêng.
- [ ] Test thêm panel Port/Starboard.
