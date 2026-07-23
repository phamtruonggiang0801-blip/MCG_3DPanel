# SESSION LOG — MCG_3DPanel

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
