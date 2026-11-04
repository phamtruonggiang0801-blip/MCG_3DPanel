---
name: tripping-bracket
description: Quy tắc Tripping Bracket — 1 bracket "B" dưới HP gần giữa mỗi ĐOẠN DẦM (web có flange bị dầm liên tục có flange ngắt 2 đầu, không phải dầm ngoài cùng) có ≥ 3 HP liên tục xuyên qua; phía chiều đổ, đảo phía ở tab Result. Đọc khi nhận diện/dựng tripping bracket, đoạn dầm, hoặc sửa bracket B dùng chung.
---

# Tripping Bracket — bracket "B" chống vặn xoắn giữa đoạn dầm

⚠️ **TẠM (kỹ sư 2026-11-04):** quy tắc sẽ được kỹ sư test trên NHIỀU panel rồi đánh giá lại.
Spec: `docs/superpowers/specs/2026-11-04-tripping-bracket-design.md`.

## 1. Nguồn gốc

Đăng kiểm: cứ ~**3 lần bước stiffener lớn nhất** dầm phải có gối chống vặn xoắn. Ghi chú `CAS.dump.json`:

> ADD 1 TRIPPING BRACKET BELOW HP IF 3 OR MORE CONTINUOUS STIFFENER IN BEAM SEGMENT. ADD THIS BRACKET
> CLOSEST TO BEAM SEGMENT MIDDLE. FOR DETAIL SEE HP BRACKET DETAIL.

Giống Collar Plate (add tại lỗ HP liền kề junction — [[web-hp-cutout]] §7): tripping bracket add cho HP gần
giữa đoạn các dầm **bên trong**; dầm ngoài cùng không cần (đầu đã có gối đỡ).

**Dump (mặt bằng):** dầm ngang web y 1620.86–1623.86, flange ẩn AM_11 75 (`1F224`); 2 dầm liên tục nhãn "CG"
x 457–460 (flange 110) / 1462–1465 (flange 50); dim **1005 "BEAM SEGMENT"**; 4 HP (`Continuous_symbol`) tâm
633.6/833.6/1043.6/1273.6; text **"B"** cạnh HP 1043.6 phía −y (giữa ≈ 959.6 ⇒ gần nhất ✔).

## 2. Quy tắc (kỹ sư 2026-11-04)

| # | Quy tắc |
|---|---|
| R1 | **Dầm** = Web có Flange. |
| R2 | **Đoạn dầm** = dầm KHÔNG liên tục bị ngắt bởi 1 dầm LIÊN TỤC khác; chỉ dầm/girder **có flange** cắt ngang làm gối; đoạn phải có **CẢ 2 đầu** là gối (gồm dầm biên, web xiên). |
| R3 | Chỉ đếm **HP** xuyên web đoạn (có lỗ CO); KHÔNG tính FB. |
| R4 | ≥ 3 HP ⇒ **đúng 1** bracket tại HP gần **giữa đoạn** nhất (giữa 2 MẶT dầm liên tục). |
| R5 | Hoà (≤ 1mm) ⇒ HP về phía **chiều đổ** dọc trục dầm. |
| R6 | **Dầm ngoài cùng** (tâm run min/max theo pháp tuyến ± 50, trong các dầm cùng phương) ⇒ không thêm. |
| R7 | **Hình = bracket B hiện có** (`hp_end_brkt_1/2`, [[sniped-end]] §5a). Khác: mép đứng hàn THẲNG mặt web dầm (không khe 30 — HP liên tục). Selector theo flange dầm nhô phía bracket (≥ 50 brkt_1). |
| R8 | Tấm **đồng phẳng thân HP** (căn giữa), dưới HP (đỉnh `Z=−b`), đáy trên flange (`Z=−WebHeight`). **Phía** web dầm = **chiều đổ** (`(1, dozY)`; Port −1 / Starboard +1 / Center về tâm — [[bracket-identification]]) chiếu lên pháp tuyến web. |
| R9 | Tab Result: **chỉ đảo phía**. |

Ví dụ 7D-16C: đoạn `1B6A5`/`1B5E6`/`1B5EB`/`1B5F4`/`1B5F0` (giữa `1B5CD` và `1B602`, 12 HP) ⇒ HP `1B60D`;
`1B5CC`/`1B5E4` ngoài cùng ⇒ không; `1B62A` (giữa `1B602` và web xiên `1B601`, 3 HP) ⇒ HP `1B622`.

## 3. Cài đặt (2026-11-04, CHƯA test trực quan)

- **Luật thuần** `Services/Builders/TrippingBracketRules.cs` (test `TrippingBracketRulesTests`): `Segments`,
  `PickHp`, `Side`, `SelectBracketB`, `BracketBPolygon` — 2 hàm cuối **dùng chung với bracket B "End 30"**
  (`HpEndBracketBuilder` gọi lại; polygon giữ đúng thứ tự gốc).
- **Builder** `Services/Builders/TrippingBracketBuilder.cs`: span từ `WebRuns`; dầm = run có Flange ∥ phủ tâm;
  **điểm cắt** = run có flange KHÁC (|cos| ≤ 0.7) giao đường tâm, CG **phủ** giao điểm (part chứa ix, cách mút
  ≥ dày dầm) mà dầm **KHÔNG phủ** + CG có flange tại ix; mid = giữa 2 mặt CG; HP = `CrossesThrough` + kẹp
  `[b/2, L−b/2]`; đặt `P = X + away·(tW/2)/cos`, `away = ±trục HP` cùng phía; tấm qua
  `HpEndBracketBuilder.BuildPlate(..., centered: true)`; Layer/Color = web đoạn dầm.
- ⚠️ **Bẫy:** `WebRuns` gom web thẳng hàng khe ≤ `TOL_GAP` 20 ⇒ 2 đoạn dầm 2 bên 1 dầm liên tục (khe = dày
  dầm) thành 1 run (vd `1B5E6` + `1B629` qua `1B602`) — builder phải TỰ CẮT (bước điểm cắt ở trên).
- **Khoá / đảo phía:** `TrippingBracket:<web part SourceKey>~<HP SourceKey>` = `DeleteKey` = `FlipKey`
  (`BuiltSolid.FlipKey`). Tab Result: `ResultCatalog` "TrippingBracket" ⇒ Bracket / "Tripping B" / CanFlip;
  `ResultRow` dùng `FlipKey` làm khoá correction khi không có part 2D; builder đọc `Corrections[key].FlipDir`.
- Report: `[TrippingBracket] dầm .. đoạn s=[..] mid=.. · HP .. s=.. (nHP=..) · side=±n brkt_1|2 overhang=..`,
  `SKIP .. HP < 3`, `SKIP dầm ngoài cùng ..`; `Build3DService` gọi sau bracket B.

## 4. Chưa chốt / theo dõi

- [ ] Test nhiều panel (kỹ sư) — đánh giá lại R2/R4/R6.
- [ ] HP quá xiên so với web dầm (cos < 0.3) ⇒ hiện bỏ, không Report riêng.
- [ ] Đoạn dầm mà HP chọn trùng chỗ Collar Plate: collar `Y ≥ −B1` > `−b`, bracket từ `−b` xuống ⇒ không chồng (chưa kiểm trực quan).
