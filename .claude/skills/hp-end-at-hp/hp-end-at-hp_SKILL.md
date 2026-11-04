---
name: hp-end-at-hp
description: Quy cách "HP TO HP END" + "HP TO FB END" (§7: mặt hàn h_fb−15 + vát 30°, FB cao ⇒ R30) — đầu HP kết thúc tại lưng/bụng (thân) HP khác: mặt hàn trên + cung R30 cố định, khoảng lùi p+30 thoát bulb (BULB INSIDE p=c+r−t, OUTSIDE p=0); nhận diện + dao cắt theo mặt cắt HP chạy (đúng cả HP xiên). Đọc khi dựng/nhận diện đầu HP chạm thân HP hoặc FB khác.
---

# HP End tại HP — đầu HP kết thúc tại lưng/bụng HP khác

**HP đầu** = HP dừng lại; **HP chạy** = HP liên tục mà HP đầu chạm vào THÂN (không phải đầu-đầu).
Spec: `docs/superpowers/specs/2026-11-04-hp-end-at-hp-design.md`.

## 1. Nguồn

`CAS.dwt` → `CAS.dump.json` (lệnh `MCG_DumpDrawing`), `*Model_Space`, MText **"HP TO HP END"**:
- Hình **BULB INSIDE** (y≈1381–1507): bụng HP chạy quay về HP đầu. Polyline `1F247` (đỉnh + mặt hàn),
  `1F251` (ngang 15.5 → cung R30 → đứng), block `HP_120x6_sv` sx=+1 tại (785.53,1501.04).
- Hình **BULB OUTSIDE** (y≈1040–1166): lưng HP chạy quay về HP đầu. Polyline `1F255` có **đỉnh trùng**
  (= đoạn `p` dài 0) → cung R30 → đứng; block `HP_120x6_sv` sx=−1.
- Dim (chỉ có `meas`, không có vị trí): Rotated 30, Radial 30.
- Tâm cung có dấu chữ thập AM_7: INSIDE (807.03,1399.69) = (mặt web + 15.5, đáy + 18.66); OUTSIDE
  (791.53,1058.53) = (mặt web, đáy + 18.66).

## 2. Quy cách (xác nhận kỹ sư 2026-11-04)

Hệ **mặt cắt HP chạy**: `x'` = pháp tuyến từ mặt tiếp xúc (mặt web HP chạy phía HP đầu) hướng VÀO HP
đầu; `y` = Z (0 = mặt dưới Top Plate, −b = đáy).

| Ký hiệu | Giá trị |
|---|---|
| `R` | **30 — cố định mọi cỡ HP** |
| `d` | `b − dx`, `dx = b − r − (3r − t + c)/√3` — `c, r` **theo bảng HP.csv** (`LookupCR`), KHÔNG r=4.5 của block |
| `p` | INSIDE: `c + r − t` (HP120×6 = 16) · OUTSIDE: `0` |
| Khoảng lùi | `p + R` — đổi theo bề rộng bulb |
| Mặt hàn | từ `y=0` xuống `y = −(b − d − R)` (HP120×6 theo bảng ≈ 70; block mẫu 71.34) |

```
 Tấm ══════════════════════════
     ┃│ HP đầu (mặt bên)
 HP  ┃│  ← mặt hàn, x'=0, y: 0 → −(b−d−R)
 chạy┃└──p──╮   ← cung R30, tâm (p, −(b−d))
     ┃bulb   ╲___
     ┃            │  ← x' = p+R xuống đáy (cắt luôn đầu bulb HP đầu)
 ────┸────────────┴── y = −b
```

Bỏ qua (kỹ sư): **vát 5×5** góc trên (chỉ có ở hình OUTSIDE, line (791.53,1154.87)→(796.53,1159.87)) —
KHÔNG đưa vào 3D. Khe 1.31mm trong `1F247` (1431 vs 1429.69) = **lỗi vẽ**. Line AM_3 (vết bulb HP đầu,
16.4 vs 18.66 — vẽ ẩu) — không dùng. **Không** có override ở tab Result.

## 3. Nhận diện — `HpEndAtHpGeometry.Match` (toán thuần, có unit test)

Mỗi đầu mút `he` (`ClassifyUtil.Endpoints`) của HP đầu × mỗi HP chạy `m`. Dừng ở lý do đầu tiên:
1. `degenerate` — trục ~0.
   **"Thò" (overhang)** = phần HP chạy vượt quá **MẶT WEB** HP đầu (±t_h/2/sinθ dọc trục `m`, KHÔNG tính
   bulb) tới mỗi đầu mút `m`. `OVERHANG_MIN = 5`.
2. `joint` — `he` ≤ 25 (`JOINT_TOL` = `HP_JOINT_TOL`) tới 1 đầu mút `m` **VÀ** (thò ở đầu đó < 5 HOẶC
   thẳng hàng `|cos| > 0.70`) ⇒ mối nối đầu-đầu / góc L khít (sniped-end §5b).
   ⚠️ **Kỹ sư 2026-11-04 (panel 7D-16C, khung HP nhỏ `1B63E`/`1B640`/`1B641`/`1B645`/`1B648`/`1B610`):**
   HP chạy **thò ~15mm** qua mặt web HP đầu = **HP end tại HP, CẮT R30** — kể cả khi bulb HP đầu quay về
   đầu HP chạy (bulb gần ngang/nhô ~1mm qua đầu HP chạy). Bản đầu loại hết các đầu này vì đầu mút chỉ cách
   đầu HP chạy ~18mm (≤ 25 ⇒ "joint").
3. `noBelly` — `m` thiếu `BellyDir`.
4. Phía: `Normal` = ⟂ trục `m`, hướng về **đầu kia** của HP đầu (đầu mút vẽ lấn qua đường tâm vẫn đúng
   phía). `Inside` = `BellyDir_m · Normal > 0`.
5. `far` — `DistToSegment > t_m/2 + p + 10` (`+p`: phía bụng người vẽ có thể dừng ở mép bulb).
6. `nearRunEnd` — thò < 5 ở 1 trong 2 phía (HP chạy dừng ngang mặt web HP đầu = góc chữ L thật).
7. `angle` — `|axis_h · axis_m| > 0.70` (θ < ~45°) ⇒ không cắt, Report `SKIP angle`, giữ vuông.
Nhiều `m` hợp lệ → lấy `Dist` nhỏ nhất.

## 4. Dao cắt — `HpEndAtHpBuilder` (phương án A, kỹ sư chọn 2026-11-04)

- Biên dạng `HpEndAtHpGeometry.CutterOutline` (bọc `HpProfileService.HpEndAtHpCutter` → `VertexBulge`):
  `(−W,+M) → (0,+M) → (0,yW) → (p,yW)⌒ → (p+R,yC) → (p+R,−b−M) → (−W,−b−M)`; `W = t_m + c + r + 2`
  (phủ phần HP đầu vẽ lấn qua mặt tiếp xúc), cung bulge **−tan(π/8)** (CW). OUTSIDE bỏ đỉnh trùng.
  null khi `b − d − R ≤ 0`.
- Hệ local: X → `Normal`, Y → +Z, Z → `Normal × Z` (thuận tay, ±trục `m`). Gốc = `Foot + Normal·t_m/2`
  (mặt tiếp xúc), lùi `HalfSpan` dọc trục. Extrude `2·HalfSpan` dọc trục **HP chạy** → BoolSubtract khỏi
  solid HP đầu. `HalfSpan = (c+r+t_h)/sinθ + (p+R+W)·|cotθ| + 10`.
- **Vẽ dừng HỤT** mặt web (`Gap` > 0.5, vd dừng ở mép bulb): trước khi cắt, `BoolUnite` 1 hộp dải web `t_h × b`
  dài `FillLength = gap/sinθ + (t_h/2)·|cotθ| + 2` từ đầu mút về phía HP chạy; phần dưới bị dao cắt xén lại.
- Xiên: khe hở tới bulb luôn đúng 30 đo ⟂ HP chạy; trên mặt đứng HP đầu khoảng lùi dài ra (p+30)/sinθ;
  mặt đầu HP đầu bám mặt web xiên.

## 5. Cài đặt (2026-11-04)

- `Services/Builders/HpEndAtHpGeometry.cs` — toán thuần (link vào `tests/MCG_3DPanel.Tests`).
  Test: `HpEndAtHpGeometryTests`, `HpEndAtHpMatchTests`.
- `Services/Builders/HpEndAtHpBuilder.cs` — `Apply(ctx, hpSolids)` → khoá `"hpHandle~runHandle"`.
- `Services/HpProfileService.cs` — `IHpProfileService.HpEndAtHpCutter`.
- `Services/Build3DService.cs` — gom `hpSolids`, gọi sau vòng dựng chính, `FillGeometry` lại.
- Report: `[HpEndAtHp] HP-end-at-HP h=.. m=.. INSIDE|OUTSIDE θ=.. p=.. d=..` / `SKIP angle` / `SKIP noBelly`.
- `HpBuilder.SnipEnd`: đầu `HpEndAtHpBuilder.TouchesRunningHp` ⇒ **không vát** (dung sai Match > `FB_FREE_END_TOL`=30 từ HP140 — tránh vát + cắt chồng). `HpEndBracketBuilder` không đổi
  (HP ∉ `TRANSVERSE_TYPES` ⇒ không "End 30").

## 6. Chưa test / theo dõi

- [ ] **Chưa test trực quan trong AutoCAD** (người dùng tự test) — kiểm INSIDE, OUTSIDE, xiên ~60°.
- [ ] Nếu solid HP đầu bị cắt hụt/thừa ở ca xiên → xem `HalfSpan` và chiều `Normal`.

Liên quan: [[hp-profile]] · [[sniped-end]] · [[web-hp-cutout]].

## 7. HP end tại FB — "HP TO FB END" (kỹ sư 2026-11-04)

Spec: `docs/superpowers/specs/2026-11-04-hp-end-at-fb-design.md`.

**Nguồn:** `CAS.dump.json` `*Model_Space`, MText "HP TO FB END". Tấm 6 (y 1648.66–1654.66); FB chạy = polyline
`1F20B` 6×80 (x 568.34–574.34); HP120 mặt bên, mặt đầu áp mặt FB x=574.34 từ đỉnh xuống y=1583.66 (= đáy FB
+ **15**, dim Rotated 15); line vát (574.34,1583.66)→(669.60,1528.66) dx 95.26 dy 55 = **30° so với đáy HP**
(dim góc 0.5236).

**Quy cách** (hệ mặt cắt FB chạy, như §2):

```
 Tấm ═══════════════════════════
   ┃│ HP đầu
 FB┃│   ← mặt hàn x'=0: 0 → −(h_fb − 15)
   ┃ ╲    ← vát 30° so với đáy HP, tới ((b−h_fb+15)·√3, −b)
 ──┸15 ╲________ đáy HP
```

- **15 và 30° CỐ ĐỊNH mọi cỡ FB/HP.** Vát phủ hết bề rộng HP đầu kể cả bulb (như `SnipHpEnd`).
- **FB cao** (`h_fb − 15 ≥ b`): dùng biên dạng HP–HP **p = 0** (§2, R30, lùi 30).
- `h_fb ≤ 15` ⇒ không dựng dao cắt (Report). `FlatBarHeight = 0` ⇒ không xét FB (Report `SKIP FB`).
- FB không bulb ⇒ không INSIDE/OUTSIDE. `h_fb` = `ctx.Input.FlatBarHeight` (1 giá trị/panel).

**Nhận diện:** y hệt §3 với `runIsFb: true` — bỏ `noBelly`, `Inside=false`, `p=0` (far = `t_fb/2 + 10`);
thò ≥ 5 qua mặt web HP đầu, joint 25, góc ≥ 45° giữ nguyên. Ứng viên chạy = mọi HP + mọi đoạn FlatBar
(sau `FlatBarSplitter` — chữ T HP dừng tại FB KHÔNG làm FB đứt); lấy gần nhất.

**Dao cắt:** cách A (§4) trong mặt cắt FB, extrude dọc trục FB. Biên dạng
`HpEndAtHpGeometry.FbCutterOutline` = `(−W,+M) → (0,+M) → (0,−(h_fb−15)) → ((b+M−(h_fb−15))·√3, −b−M) → (−W,−b−M)`,
`W = t_fb + c + r + 2`. Span: `HalfSpanX(t_h, c, r, xMax, cosθ)` với `xMax` = |X| lớn nhất của dao (vát ~100mm).
Nối dài khi vẽ hụt (Gap) + không vát Sniped End: dùng chung với HP–HP.

**Cài đặt:** `HpEndAtHpGeometry.FbCutterOutline/HalfSpanX/Match(runIsFb)` (test `HpEndAtFbTests`),
`HpProfileService.HpEndAtFbCutter`, `HpEndAtHpBuilder.Runs/FindRun/BuildCutter`; `Build3DService` gọi khi ≥ 1 HP.
Report: `[HpEndAtHp] HP-end-at-FB h=.. m=.. θ=.. hfb=.. weld=.. mode=CHAMFER|R30`.

- [ ] **Chưa test trực quan** (người dùng tự test).
