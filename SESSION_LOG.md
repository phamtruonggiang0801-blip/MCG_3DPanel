# SESSION LOG — MCG_3DPanel

## Session 2026-09-04c — Fix Collar Plate khổng lồ/lệch chỗ (test 701P/701S)

**Triệu chứng:** 4/12 Collar Plate của 701S dài ~2–9 m, đặt ngoài panel. Log: `tl=1777.3` / `tl=9039.1`
(đáng ra ~6). Nguồn: web dọc `6875E` (polygon 15931×9039mm ~ đường bao panel, lọt layer AM_0 → nhận
nhầm `Web`) và `6303E` (web chữ L, `PartCollector.RectDims` với >5 đỉnh fallback bbox `min(dx,dy)` = 1777).
`WebCollarPlateBuilder.BuildOne` dùng `web.FootprintThickness` vừa làm bề dày vừa làm quãng extrude +
offset → solid dài hàng mét. Cùng giá trị hỏng vào `WebHpCutoutBuilder` (`webThk=9039.1`).

**Fix A (gốc — phân loại):**
- `ClassifyUtil.MinWidth(v[])` — bề rộng nhỏ nhất thật qua pháp tuyến mọi cạnh (đúng strip L/chéo/vát).
- `PartCollector.RectDims` — polygon >5 đỉnh: `thickness = min(MinWidth, bbox-min)` thay vì bbox-min.
  → `6303E` ra ~6–10mm (đúng). 4/5 đỉnh giữ nguyên `OrientedBox` (không đụng 108/108 đã verify).
- `ClassifyUtil.WEB_MAX_THK = 40` + gate ở `WebBracketSplitter.Split`: polygon AM_0 dày > 40mm →
  `PartType.Unknown` (Build3DService bỏ qua) + `ClassificationResult.Notes`. CutOut/BackingPlate/
  ClosedPlate đã tách TRƯỚC nên không bị đụng. → `6875E` không còn dựng khối đặc 37.8e9.
- `Notes` surface lên cả classify report (`notes`) lẫn build report (đầu mảng `report`).

**Fix B (phòng vệ — builder):** `WebCollarPlateBuilder` (gom candidate + `BuildOne`) và
`WebHpCutoutBuilder` bỏ web có `FootprintThickness` ngoài `[1, WEB_MAX_THK]` + ghi Report.

Build lại 701P/701S: collar OK (21 tấm, `tl=6`/`10`, vol ~34k). `75EA5`/`6875E` → Unknown, không dựng.

**Fix C — Top Plate 701-series mất (test 142210):** Sau khi bỏ slab `6875E`, lộ ra boong 3D bé xíu
(Ø80, vol 30159) — **bug có sẵn**: mọi build 701P/701S (135627…142124) đều vậy, chỉ bị slab che.
Nguyên nhân: bản vẽ 701 vẽ tấm boong trên layer **Mechanical-AM_0** (7D-17C vẽ trên "0"); layer "0"
chỉ còn mốc Ø80 → `topCands.OrderBy(Area).First()` ra mốc. Kỹ sư xác nhận 2026-09-04: `75EA5` = Top
Plate, mốc Ø80 = lỗ khoét (CutOut).
- `ClassificationService.FindFullSpanPlate` — polyline AM_0 phủ ≥75% mỗi chiều panel + đặc (area ≥40%
  bbox). Nếu có + Top Plate layer-0 (nếu có) nhỏ hơn ×0.25 → promote thành `TopPlate`, mốc layer-0
  trong biên → `CutOut`, mốc ngoài biên → `Unknown`. Ghi `Notes`. Guard `×0.25` → 7D-17C không dính.

Build Debug (Rebuild) OK, 0 error / 0 CS-warning. **Chưa test trực quan Fix C** — build lại 701P/701S:
(1) boong 3D phủ toàn panel dày = TopPlateThickness; (2) lỗ Ø80 khoét trên boong; (3) collar vẫn OK.
Còn tồn: `longWebs` (ClassificationService) — nếu KHÔNG promote thì `6875E` vẫn trong đó (ảnh hưởng nhẹ
PlateDetector); sau promote thì đã `am0.Remove`.

## Session 2026-09-04b — Bỏ Snip End HP + phân tích Bracket "B" đầu HP

- **`HpBuilder`**: bỏ hẳn Sniped End (`SnipHpEnd` + hằng số `SNIP_*`) — đầu HP giờ VUÔNG SẮC (xác nhận kỹ sư).
- **`deploy.bat`**: sửa trỏ `bin\Debug\` (không còn `net48\`) — nguyên nhân crash NETLOAD `0xC0000005`
  (nạp nhầm DLL net48 vào AutoCAD 2026). Xoá `bin\Debug\net48\` cũ.
- **Bracket "B" đầu HP** (`Bracket B.dump.json` → skill `sniped-end` §5): gusset knee nơi HP dừng đúng
  30mm trước Web ngang. Kỹ sư chốt hết tham số (`thk`=dày HP, `reach`=max(180,30+1.5b), `Ht`=WebHeight−b,
  `d1` 2 biến thể theo overhang như OB/IB, mặt đặt đơn/cặp). **ĐÃ CÀI** `HpEndBracketBuilder` + wire
  `Build3DService`, build OK — **chưa test trực quan**.

## Session 2026-09-04 — Migrate .NET 8 cho AutoCAD 2026

### Đã làm
- `MCG_3DPanel.csproj`: `net48` → `net8.0-windows`; `EnableDynamicLoading` + `RollForward` + `CopyLocalLockFileAssemblies=true`; `AppendTargetFrameworkToOutputPath` → false; output cố định `bin\$(Config)\`.
- Ref API AutoCAD **2026** (`$(AcadPath)`, auto-detect 2026→2025): `AcCoreMgd`/`AcDbMgd`/`AcMgd`/`AcDbMgdBrep`.
- `NoWarn CA1416` + demote `MSB3277` (AcDbMgdBrep/AcTcMgd kéo Microsoft.VisualBasic 10.1 / System.Drawing cũ — vô hại).
- `PackageContents.xml`: `SeriesMin=SeriesMax=R24.2` → `SeriesMin=R25.0 SeriesMax=R25.1`.
- Thêm target `DeployToCustomTools` (Release): deploy NGUYÊN thư mục + `PackageContents.xml` vào `C:\CustomTools\Autocad\MCG_3DPanel\`.
- Build `dotnet build -c Debug` → **0 error, 0 warning**.

### Trạng thái
- Bản .NET 8 chỉ chạy AutoCAD 2025+. Chưa test trong AutoCAD 2026 thật.

### Bước tiếp theo
- NETLOAD + bundle autoload trong AutoCAD 2026 → lệnh `MCG_Build3DPanel`, dựng 3D, xuất CSV.

---

## ⚡ Trạng thái hiện tại

**Phase 2 (Build3DService) — CODE XONG, build OK, chờ test dựng 3D trong AutoCAD.**

Build Debug/Release: 0 error. Phase 0 (Inspector) + Phase 1 (Classification + Bracket OB/IB/FB) đã xong.

Đã bổ sung: solid **GIỮ NGUYÊN layer + màu gốc** (không ép Mechanical-AM_0). Bracket cao = Web (xác nhận),
Face Plate như Flange (xác nhận), Web tầng cùng cao H (xác nhận).

### Làm ngay tiếp theo
1. **Test dựng 3D**: `deploy.bat` → NETLOAD → `MCG_Build3DPanel` → nhập thông số + Panel position →
   **Build 3D** → pick panel → chọn **Yes/No** (giữ 3D & xoá 2D / bỏ). Kiểm hình + layer/màu.
2. **Parametric profiles (Bracket trước)**: hướng đã chốt = **DWG profile-library** (xem skill
   `parametric-profiles`). CHỜ user gửi bản vẽ bracket + dim fixed/scale → định quy ước DWG + viết ProfileService.
3. **Collar Plate / LUG B1R** (2026-08-28): ĐÃ CÀI đủ (build OK, **chưa test trực quan**) —
   `MCG_DumpDrawing` xuất biên Hatch, `HpProfileService.CollarPlateSection` (khớp 1:1 Hatch `1F3F2`,
   đáy = −B1 tra theo chiều cao HP), `WebCollarPlateBuilder` (2 HP quanh junction web-ngang, KHÔNG gate
   WebHeight) + wire `Build3DService`. Skill `web-hp-cutout` §7. Test: build lại panel HP-xuyên-Web bất kỳ.

## Trạng thái từng file

| File | Trạng thái |
|---|---|
| `MCG_3DPanel.csproj` | ✅ net48/x64, ref AutoCAD 2023, UseWPF |
| `Commands/PanelCommands.cs` | ✅ IExtensionApplication + 2 lệnh |
| `UI/PaletteManager.cs` | ✅ Singleton PaletteSet |
| `UI/PanelBuilderControl.xaml(.cs)` | ✅ Inspect hoạt động; Build disabled (Phase 2) |
| `Services/IInspectorService.cs` + `InspectorService.cs` | ✅ duyệt đệ quy → JSON |
| `Utilities/Json.cs` | ✅ `JsonWriter` + `JsonObject` |
| `PackageContents.xml`, `deploy.bat` | ✅ deploy OK |
| `.claude/skills/panel-parts-reference` | ✅ nguồn chân lý 5 loại đối tượng |
| `Models/*`, `ClassificationService`, `Build3DService` | ❌ Phase 1+ |

## 🗒️ Session Log Chi Tiết

### 2026-07-22 — Phase 2.4 (under-50 + bend mép flange)
- Phân tích lại OB-OB1.dwg. **Under-50** (overhang<50): F=15, height = webH−30 (cách flange 30) — template under-50 đã encode.
- **Bend (mép gấp lip)**: free-edge = **√((S−F)²+H²)** (toe-flange→toe-HP) ≥ **350(OB)/600(OB1)** → gấp lip
  rộng **70(OB)/100(OB1)**, dày=dày tấm, union vào tấm. Công thức khớp dim dump (581≈583) ✓.
- Code: `Build3DService.TryAddBendLip` (dựng lip dọc cạnh nghiêng + BoolUnite); `Huse=webH−30` cho under50.
- Kiểm chứng: 21/21 OB panel 7D-09C có freeEdge 414-610 ≥350 → **đều bend**. Build OK. Cập nhật skill parametric-profiles.


### 2026-07-21 — Phase 2.3 (đúng 20 OB + leg-on-flange 42/157)
- Phân tích lại inspect JSON: OB = AM_0 **ngắn(<1000)+dày6** (KHÔNG gộp collinear vào web) → **20 OB**;
  thk10 ngắn = web-tier. Bracket knee 3 leg: S=span(plan len 334-524), H=Web height, F=leg-on-flange.
- **F = (rộng Flange − dày Web)/2 − 15** (overhang<50→under50, F=15). Map bracket→flange gần nhất (bbox).
- Code: `ClassificationService.SplitWebAndBrackets` (thay collinear-classify); `ProfileService.BracketOutline(under50,S,H,F)`
  generator 3-leg (giữ flange/toe cố định); `Build3DService` tính F + report thêm S/F/flangeW.
- **Kiểm chứng console**: 20 OB · F=42×10 (flange120) · F=157×10 (flange350) — khớp spec. Build OK.
- Cập nhật 3 skill (bracket-identification, parametric-profiles, panel-parts-reference). **Chờ test trực quan.**


### 2026-07-21 — Phase 2.1 (Bracket knee profile)
- Đọc OB-OB1.dwg + MCG_Profiles.dwg qua `MCG_DumpDrawing` (nâng để dump cả block definition). AcCoreConsole
  headless treo trong sandbox → dùng lệnh chạy trong AutoCAD thật.
- Trích 2 block `MCG_OB_Flange-over-50` (594×374) / `under-50` (547×344) → `ProfileService` generator:
  giữ flange/bo cố định, giãn 2 leg theo L (footprint) & H (Web height); bulge không méo.
- `Build3DService` nhánh Bracket: sinh outline → extrude chiều dày (6/10) căn giữa → AlignCoordinateSystem
  đặt vào mặt đứng (anchor=đầu xa HP/FB; +X→HP; −Y=Z xuống). Giữ layer/màu gốc.
- **CHỜ test trực quan**: hướng knee (có thể lật), under-50, độ chính xác vị trí. Build OK.


### 2026-07-21 — Phase 1.6b (sửa OB/IB — bụng ra xa tâm)
- User báo 5 IB sai (panel thật 0 IB). Dump hình học: 10 bracket đều ở phía TRONG (hướng tâm) của 2 HP mép
  (trái X≈95404, phải X≈108060, tâm X=101732) → phải toàn OB.
- Lỗi: `BellyDir` HP đứng dùng bụng=+X cố định → HP mép trái bị gán nhầm IB.
- Sửa `BellyDir` (v1.1): bụng = **ra xa tâm panel** (đúng "HP đổ ra ngoài"). Kết quả: 10/10 OB, 0 IB ✔.
- `PanelPosition` tạm chưa dùng (giữ dropdown) — chờ mẫu có IB thật để tinh chỉnh HP nội bộ.

### 2026-07-21 — Phase 1.6 (engine Bracket OB/IB/FB)
- Model `PanelPosition` (Center/Port/Starboard) + `PanelPart.Code`. Dropdown Panel position trên UI.
- FB vs OB/IB = đầu kia bracket chạm Flat Bar member (FB) hay HP (OB/IB) — adjacency bbox tol 12mm.
  Kiểm chứng: 10/10 bracket panel này là Web↔HP (OB/IB), 0 FB.
- OB/IB theo chiều đổ (`BellyDir`/`ObOrIb`): HP đứng→bụng +X; HP ngang→Port −Y/Starboard +Y/Center về tâm.
  Suffix theo bậc dày (`ThicknessSuffix` 6→""/10→"1"/15→"2"/20→"3"). Sanity: 524×6→OB, 474×6→IB (Center).
- Report thêm `bracketByCode` + `code` mỗi bracket. Build OK.
- **CHỜ user**: xác nhận bảng thickness→suffix đầy đủ + kiểm tra trực quan OB/IB (đảo dấu nếu ngược).

### 2026-07-21 — Phase 1.5 (gộp Web + Face Plate + breakdown)
- Ghi 2 fact vào skill: **Web nhiều tầng dày** (nhiều polyline collinear = 1 Web) + **Face Plate**
  (polyline không-vuông trên layer Flange). Tạo skill `bracket-identification` (OB/IB theo chiều đổ + panel type).
- ClassificationService: gộp Web collinear (union-find, cùng phương + thẳng hàng + nối trục), tách Face Plate
  (Flange = rectangle; non-rect = FacePlate), thêm `PartType.FacePlate`, `PanelPart.GroupId`.
- Report chi tiết: webRuns (tiers chiều dày), flange widths, facePlates, brackets. Kiểm chứng lại trên JSON:
  **9 Web run** (có run tiers 6+10) · **10 Bracket** · **9 Flange** · **4 Face Plate** · FB 14 · HP 29.
- CÒN THIẾU cho mã Bracket OB/IB/FB (xem skill bracket-identification checklist): bảng thickness→suffix,
  cách biết loại Panel, engine "lưng/bụng" HP/FB, cách nhận FB. Build OK.

### 2026-07-21 — Phase 1 (ClassificationService)
- Models: `PartType` (enum), `PanelInput` (POCO, parse spec "120*6").
- Services: `PanelPart` + `ClassificationResult` (carrier hình học WCS), `ClassificationService`.
- Nhận diện theo Layer/Color; Top Plate = max area layer 0; Web vs Bracket = centroid trong bbox Flange
  **VÀ** length ≥ 40% length flange (`WEB_LENGTH_RATIO`).
- Nút **Classify Panel** trên palette → `PanelCommands.RunClassify()` → report JSON (counts + parts) + status bar.
- **Mô phỏng trên JSON thật (7D-09C_Assy)**: TopPlate 1 · Web 9 · Bracket 28 · Flange 13 · FlatBar 14 · HP 29.
- Build Debug + Release OK. **Chờ user test visual trong CAD** để xác nhận Web/Bracket đúng.

### Làm ngay tiếp theo (sau khi user xác nhận)
→ **Phase 2 — Build3DService**: Region→Extrude→Move theo bảng định vị Z + Preview. Đọc `PanelInput` từ palette.

### 2026-07-21 — Phase 0.1 (điều chỉnh sau khi có dữ liệu thật)
- Inspect panel thật `7D-09C_Assy` (3273 entity) → phân tích JSON, **kiểm chứng bảng nhận diện**:
  - Top Plate = polyline kín **area lớn nhất** trên layer 0 (181M vs 4.5k → an toàn tuyệt đối), có thể v=22.
  - Flange nhận theo **layer Mechanical-AM_11** thôi (màu thực = 3, KHÔNG phải ByLayer).
  - FB = AM_3 color≠30; HP = AM_3 color=30; Web = AM_0 (lẫn Bracket + đoạn ngắn).
  - Chiều dày = cạnh ngắn footprint (~6). → cập nhật skill `panel-parts-reference`.
- **Debug DLL kèm timestamp** (`MCG_3DPanel_yyyyMMdd_HHmmss.dll`) → NETLOAD hot-reload không cần restart;
  Release giữ tên ổn định cho bundle. `deploy.bat` (Debug: clipboard netload path) / `deploy.bat release` (bundle).
- **Bỏ lệnh `MCG_InspectPanel`** → nút Inspect gọi thẳng `PanelCommands.RunInspect()` (LockDocument khi đọc DB).
- **Redesign UI**: tiếng Anh, đánh số bước, màu AutoCAD tối, status bar. Palette **fix cạnh trái** (Dock/DockEnabled=Left).
- Tạo skill `ui-guidelines` (chuẩn UI: đơn giản, tiếng Anh, thân thiện, hợp AutoCAD, palette trái non-dockable).

### 2026-07-21 — Phase 0
- Chốt spec 5 loại đối tượng (Layer/Color, thông số, định vị Z) → viết skill `panel-parts-reference`.
- Scaffold project SDK-style net48 + WPF, ref AutoCAD 2023.
- Viết Inspector: pick block → duyệt đệ quy (kể cả nesting block) → xuất JSON (summary histogram
  theo layer|color + cây entity đầy đủ: type, handle, layer, color, closed, area, vertices WCS, bbox).
- Build OK. Deploy bundle OK.
- Quyết định: assembly/namespace = `MCG_3DPanel`; Inspector xuất JSON ra Desktop rồi mở.
