---
name: girder-bracket
description: Quy cách GIRDER BRACKET (GB) — đầu Web (girder) tại HP/FB khi khoảng L tới Web vuông góc ≥ Max Stiff (HpMaxSpan). Số đo trích CAS.dump.json (DETAIL TITLE "GIRDER BRACKET (GB)") + xác nhận kỹ sư 2026-11-03. Đọc khi nhận diện/dựng GB.
---

# Girder Bracket (GB) — trích `CAS.dump.json` + xác nhận kỹ sư (2026-11-03)

Dump: 2 mẫu GB giống nhau, `DETAIL TITLE` = `GIRDER BRACKET (GB)`, 4 NOTE rỗng (điều kiện nhận diện KHÔNG nằm trong dump — lấy từ kỹ sư).

## 1. Điều kiện (ĐÃ XÁC NHẬN)

- Đầu End của Girder tại **HP hoặc FB** (cả hai đều áp dụng GB).
- **GE ↔ GB phân bởi Max Stiff** = `GirderEndDetector.HpMaxSpan(cls)` (quy tắc "1 khoảng HP", skill [[sniped-end]] §3f):
  **L < Max Stiff → GE · L ≥ Max Stiff → GB**. L = đầu End → mép gần Web vuông góc ([[girder-end]] §3b).
  Không có Web vuông góc trong Max Stiff ⇒ cũng là GB.
- ⚠️ **"775" KHÔNG phải hằng số** — chỉ là Max Stiff của panel mẫu 7D-17C (DIM "L<775mm"). 7D-16C có `hpSpan=918`.
  Code hiện dùng `ClassifyUtil.GE_MAX_L = 775` (cố định) ⇒ **SAI cho panel khác**, cần đổi sang `HpMaxSpan` (fallback 775 khi =0).
- **Mốc toe** (kỹ sư 2026-11-04): HP **hoặc FB**, **xuyên** qua web HOẶC **dừng** cách mặt web ≤ `GB_ANCHOR_GAP` = 50mm
  (`GirderEndSplitter.NearestToeAnchor`). Toe luôn = đường tâm mốc + 100 (+60). Không có mốc ⇒ giữ Web chữ nhật, ghi Notes.
- Chi tiết đi kèm trong vùng GB (kỹ sư 2026-11-04):
  · HP xuyên GB ⇒ lỗ CO + Collar Plate như HP xuyên web (đã cài 2026-11-04: GB được khoét TRƯỚC khi hợp nhất + nối vào web run của collar).
  · HP dừng cách 30 ⇒ Bracket B dưới HP như HP end (`HpEndBracketBuilder` coi GirderEnd là vách) — CHƯA kiểm trực quan.
  · FB xuyên GB ⇒ ngắt theo bề dày GB (`FlatBarSplitter`, sẵn có) — CHƯA kiểm trực quan.
  · FB dừng cách 30 ⇒ Sniped End (`FlatBarBuilder`, sẵn có) — CHƯA kiểm trực quan.
- HP xuyên qua web trong đoạn GB ⇒ **lỗ CO** (`WebHpCutoutBuilder`) + **Collar Plate** theo [[web-hp-cutout]] §7. Note gốc "web < 300" KHÔNG
  phải gate hiện tại (xem §7a: rule chính luôn áp dụng 2 lỗ quanh junction; web<300 → collar MỌI lỗ chỉ khi có H riêng từng web — chưa làm).

## 2. Hình học đường bao (đo từ dump; gốc = mặt dưới deck, x=804.7 = mặt FB)

| Thành phần | Giá trị | Ghi chú |
|---|---|---|
| Mẫu bản vẽ | deck 6 · web 6×379 · flange đáy 15×319 · tổng 400 | **H của web = theo người dùng nhập** (`WebHeight`), KHÔNG cố định 379/394 |
| Mặt đầu (sát HP/FB) | **Dend = member − 30** (FB80 → 50) | **mọi GB**; 30 = chừa chỗ hàn Flange lên BỤNG GB (xác nhận) |
| Cung đầu | **R30**, tâm trên mặt member, tâm = đáy member − 15 | cung 90°→45.8° nối free-edge |
| Free-edge | thẳng, nghiêng ≈47° (mẫu dài 418) | từ cung đầu tới toe đáy |
| Toe đáy | ~~R50~~ → **góc SẮC** (kỹ sư chốt 2026-11-04) | vị trí chân GIỮ theo HP (mục 3); flange 80×10 chạy tới đúng góc chân, cắt vuông |
| Đáy | phẳng sát mặt trên flange tới chân web | |
| Flange GB | **80 × 10** (nhãn dim "80x<>", xác nhận) | hàn dọc free-edge, dày 10 (offset vuông góc đo 10.0) |
| Snip đầu cao | polyline mẫu: cạnh **30–20–30**, rộng 50 | **CHƯA CHỐT** snip cho flange 80 (giữ 15@30° mỗi bên → phẳng 50?) |

Mẫu: FB cách web 863.9 (≥ Max Stiff 775), HP cắt giữa cách web 643.9.

## 3. Quy tắc ĐÃ CHỐT thêm (kỹ sư 2026-11-03) + TRẠNG THÁI CODE

- **Snip đầu cao Flange 80×10: 60°–20–60°** (mỗi bên cạnh vát 60° cắt 30 ngang, phẳng 20).
- **Flange GB LUÔN có** (không theo ngưỡng D).
- **GB dày 10/20**: Dend/R/flange theo GE (không đổi theo thk).
- **Toe đáy**: tâm GB toe cách đường tâm HP xuyên gần nhất **100** (dim "100" CAS.dump.json, đo 1124.7−1024.7=100.0) — suy từ dump, kỹ sư chưa xác nhận lời.
- **Cài đặt** (build OK, CHƯA test trực quan): `GirderEndSplitter` (Max Stiff = `HpMaxSpan`, GE nếu L<MaxStiff, GB nếu ≥ hoặc không có Web ⊥; vùng GB = s_HP+100+60 từ đầu end), `GeProfileService.GbOutline` (Dend=member−30, R30 đầu ledge=0, toe SẮC, Lb=60), `GirderEndBuilder.BuildGb` + `AddFlangeSnip`. Không có mốc toe (HP/FB xuyên hoặc dừng ≤ 50) → giữ Web, ghi Notes.
- **2026-11-04**: GE/GB có flange ⇒ chân KHÔNG thụt 15 / KHÔNG bo — với GB: **giữ vị trí chân theo HP**, chỉ bỏ R50 thành góc sắc;
  flange 80×10 dừng đúng góc chân, cắt vuông; snip đầu cao 60°–20–60° giữ nguyên. Xem [[girder-end]] §3–4.
- **2026-11-04**: flange 80×10 là **SOLID ĐỘC LẬP** (không union vào GB), Layer/Color theo Flange girder gốc — như GE ([[girder-end]] §4).

## 4. CHƯA CHỐT

- [ ] Toe = tâm HP + 100 hay mặt HP + 100 (sai số ≤ 6mm).
- [ ] Cung đầu R30: tâm = đáy member − 15 (đo dump) — builder chỉ đặt tiếp tuyến mặt đầu (ledge=0), CHƯA kiểm với dump.
- [ ] Dịch vị trí khi `HpMaxSpan`=0 → fallback 775.

Liên quan: [[girder-end]] · [[web-hp-cutout]] · [[bracket-identification]] (GB KHÔNG phải Bracket) · [[sniped-end]] (Max Stiff = HpMaxSpan).
