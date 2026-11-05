---
name: web-thickness-chamfer
description: Vát 1:4 tại mối nối 2 web thẳng hàng khác chiều dày (Δt ≥ 4, L = 4·Δt, chỉ mặt lệch bậc; thẳng tâm ⇒ 2 mặt Δt/2) — Web / Closed Plate / Web + GE/GB. Đọc khi sửa nhận diện mối nối web, khối nêm vát, hoặc thứ tự hậu xử lý solid web.
---

# Web thickness-change chamfer (vát 1:4)

Nguồn: `CAS.dwt` → `CAS.dump.json` `*Model_Space` (mặt cắt: tấm dày 10 / mỏng 5, 1 mặt flush, vát trên tấm dày,
dim `t` = bậc lệch và `4 x t` — hình KHÔNG đúng tỉ lệ (đo 15), chữ "4 x t" là luật). Spec:
`docs/superpowers/specs/2026-11-05-web-thickness-chamfer-design.md`.

## Quy tắc (kỹ sư 2026-11-05)

| # | Quy tắc |
|---|---|
| R1 | Chỉ web: `Web`, `ClosedPlate`, `GirderEnd` (GE/GB gộp vào web). KHÔNG flange/faceplate. |
| R2 | Mối nối = 2 web thẳng hàng (\|cos\| ≥ 0.999), nối đầu–đầu (khe/chồng ≤ 5), lệch ngang ≤ (t1+t2)/2, **Δt ≥ 4**. |
| R3 | **L = 4 × Δt** (bậc lệch) dọc trục tấm dày từ mối nối: t10↔t6 ⇒ 16; t20↔t10 ⇒ 40. |
| R4 | Bậc từng mặt từ footprint 2D; ≤ 0.5 ⇒ flush (không cắt); mặt lệch ⇒ vát từ `step` về 0 trên L; thẳng tâm ⇒ 2 mặt, mỗi mặt Δt/2. Cắt suốt chiều cao web. |

7D-16C: 10 mối nối, đều flush mặt PHÍA TRONG panel — 8× t10↔t6 (vd `26965`↔`26964`, Closed Plate `2695A`↔`2696E`),
2× t20↔t10 (`269A0`/`269A1` Web + GE-HP ↔ `26976`/`26978`).

## Cài đặt

- **Thuần** `Services/Builders/WebThicknessChamferGeometry.cs` (test `WebThicknessChamferTests`, số thật 7D-16C):
  `Match(WebSeg a, WebSeg b)` → `ChamferJoint` (J, U vào tấm dày, N = (−Uy, Ux), `StepPlus/StepMinus`, Dt, L,
  HalfThick) hoặc null; `Wedge(joint, side, margin)` → 4 đỉnh WCS: hệ (U, side·N) `(−m, h−s−k·m) → (L, h) →
  (L, h+m) → (−m, h+m)` (cạnh vát qua mặt tấm mỏng tại mối nối và mặt tấm dày tại L).
- **Builder** `Services/Builders/WebChamferBuilder.cs`: `WebSeg` từ `ClassifyUtil.Endpoints` + `FootprintThickness`;
  mọi cặp; bỏ cặp cùng solid (GE ↔ web của nó); dedupe theo (SourceKey tấm dày, J); khối nêm `Polyline` ở
  `Z = −WebHeight − 10`, đùn `+Z (WebHeight + 20)`, `BoolSubtract`. Lỗi ⇒ `ctx.Warn` (web giữ vuông).
- **Thứ tự** (`Build3DService`): CO cutout → gộp GE/GB vào web → **vát** → collar / bracket B / tripping. Part GE/GB
  đã gộp (solid IsErased) dùng `builtMap[JoinWithWeb].sol`.
- Report: `[WebChamfer] Web h t10 ↔ Web h t6: Δt=4 L=16 · −N step=4.` + tổng `N mối nối … vát 1:4`.

## Theo dõi

- [ ] Footprint 2D đã vẽ sẵn vát 1:4: khối nêm trùng vùng trống ⇒ trừ vô hại (chưa kiểm trực quan).
- [ ] Mối nối web chéo nhau / chữ T không thuộc luật (chỉ thẳng hàng).
