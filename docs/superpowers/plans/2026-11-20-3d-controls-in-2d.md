# Điều khiển 3D trên bản vẽ 2D — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đang ở bản vẽ 2D chính thức, tab Result + Naming hiện dòng của panel (đọc ngầm file `_3D.dwg`), sửa chỉ ghi "chờ"
trong bản 2D, một nút Update áp vào 3D rồi vẽ lại Plan View + Detail.

**Architecture:** Database phụ của file 3D (`Detail3DSource`) giữ mở theo bản vẽ 2D; `PanelSession` đọc nó ra `BuildResult`
như thường, gắn `RemoteLink`. Sửa trên tab đi vào `CorrectionStore`/`NameStore` của database phụ như cũ, nhưng thay vì
rebuild thì tính **diff** so với lúc đọc (`PendingEdits`, thuần C#) và ghi XRecord `MCG3D_PENDING` lên block `_Assy` trong
bản 2D. Update = luồng `UpdateFile` có sẵn + áp pending trước `RemapCarry`, rồi chuỗi Plan View → Detail Update → về 2D.

**Tech Stack:** C# / AutoCAD .NET API, WPF palette, System.Text.Json, xUnit (tests/MCG_3DPanel.Tests).

**Spec:** docs/superpowers/specs/2026-11-20-3d-controls-in-2d-design.md

## Global Constraints

- Build: `dotnet build -c Debug MCG_3DPanel.csproj -p:BuildStamp=$(date +%Y%m%d_%H%M%S)`; test: `cd tests/MCG_3DPanel.Tests && dotnet test`.
- File thuần C# (Services/Update/*) KHÔNG `using Autodesk.*` — test project link cả thư mục `Services\Update\*.cs`.
- Khoá sửa `{hex}#{n}` = handle 2D gốc; file 3D giữ nguyên handle block 2D ⇒ một khoá dùng được ở cả 2 file.
- XRecord pending: key `MCG3D_PENDING`, trên ĐỊNH NGHĨA block `_Assy` trong bản vẽ 2D (`CorrectionXRecord.Read/Write`).
- Status khi block 2D lệch file 3D (khoá sửa): `The 2D panel changed since the last Update — press Update first.`
- Sửa chờ thắng giá trị file 3D khi trùng khoá; khoá file 3D mà pending không đụng ⇒ giữ nguyên.
- Pending áp TRƯỚC `RemapCarry` (khoá pending là handle file 3D cũ).
- Handler palette / document: không để lọt exception (AutoCAD crash) — bọc như `PaletteManager.Guarded`.
- Văn bản UI tiếng Anh; không báo "0 …" (ux-quality-bar); file 3D không mở thì không được hiện lên màn hình trừ lúc Update.
- File 3D đang mở như document: dùng database của document (như `Detail3DSource.Load`), không đọc file đĩa lần hai.

## Review Focus

1. Bản 2D có pending rồi người dùng DC lại block (fingerprint lệch) rồi Update — pending phải đi qua đổi khoá, không áp sai part.
2. Người dùng mở file 3D, sửa trực tiếp ở đó, rồi quay lại bản 2D còn pending — dòng phải đọc lại (file đổi), pending vẫn đè đúng khoá của nó, sửa 3D khác khoá giữ.
3. Đóng bản vẽ 2D khi database phụ đang giữ — phải giải phóng, không crash; mở lại bản 2D đã lưu thì pending hiện lại.
4. Update lỗi giữa chừng (Plan View / Detail lỗi) — pending đã vào file 3D thì phải xoá pending, báo bước chưa cập nhật; Update bước 3D lỗi thì pending giữ nguyên.
5. Bản 2D có 2 reference `_Assy` khác nhau / không có file 3D — tab hiện đúng panel có file, panel không có file chỉ một dòng lý do, không lỗi.

---

### Task 1: PendingEdits — diff / apply thuần C#

**Files:**
- Create: `Services/Update/PendingEdits.cs`
- Modify: `Services/Update/KeyRemap.cs` (thêm `Handles`)
- Test: `tests/MCG_3DPanel.Tests/PendingEditsTests.cs`

**Interfaces:**
- Produces:
  - `static string PendingEdits.Diff(string baseCorr, string curCorr, string baseNames, string curNames)` — null nếu không khác.
  - `static void PendingEdits.Apply(string pending, string corr, string names, out string corrOut, out string namesOut)` — pending null/rỗng ⇒ trả nguyên.
  - `static int PendingEdits.Count(string pending)` — số khoá bị đụng (0 nếu null).
  - `static ISet<string> PendingEdits.Keys(string pending)` — mọi khoá bị đụng (để đánh dấu dòng), OrdinalIgnoreCase.
  - `static IEnumerable<string> KeyRemap.Handles(string key)` — các hex handle trong khoá (dùng regex `Token` sẵn có).

Định dạng pending (JSON, System.Text.Json, bỏ null):
`{ "Version":1, "Corr": { "Set":{key:PartCorrection}, "Cleared":[..], "Deleted":[..], "Restored":[..] },
   "Names": { "Set":{key:name}, "Cleared":[..], "Deleted":[..], "Undeleted":[..], "Merges":[[..]]|null, "Splits":[..]|null } }`
Merges/Splits là giá trị nguyên khối: khác base ⇒ ghi toàn bộ danh sách hiện tại; giống ⇒ null (Apply giữ của carry).
So sánh PartCorrection bằng chuỗi JSON đã serialize.

- [ ] **Step 1: Viết test fail** — `PendingEditsTests`:
  - `Diff_Same_ReturnsNull` — base == cur (cả 2 JSON) ⇒ null; `Count(null) == 0`.
  - `KindChange_AppliedOverCarry` — base `{}`; cur đặt `2B7B3#0` Kind="Bracket"; carry có `2B7B3#0` Kind="Web" và `AAA#0` Flip ⇒ Apply: `2B7B3#0` Kind Bracket, `AAA#0` vẫn còn.
  - `Cleared_RemovesKeyFromCarry` — base có `B#0`, cur không ⇒ Apply lên carry có `B#0` ⇒ không còn `B#0`.
  - `DeleteAndRestore` — cur thêm Deleted `C#0` và bỏ Deleted `D#0` ⇒ carry Deleted {D#0} ⇒ kết quả {C#0}.
  - `Names_SetClearMerge` — đặt tên `E#0`="P1", xoá tên `F#0`, merges đổi ⇒ carry nhận đúng; merges không đổi ⇒ carry giữ merges của nó.
  - `Keys_ListsEveryTouchedKey` — Keys chứa đủ khoá Set/Cleared/Deleted/Restored của cả 2 phần; `Count` bằng số đó.
  - `ApplyThenRemap_MovesPendingToNewHandles` — Apply rồi `KeyRemap.Corrections(..., map{2B7B3→9F001})` ⇒ khoá `9F001#0` Kind Bracket.
  - `KeyRemap_Handles` — `Handles("CollarPlate:2B7B3#0~2B53D#1")` = ["2B7B3","2B53D"]; `Handles("1B#0/GirderFlange#0")` = ["1B"].

- [ ] **Step 2: Chạy** `cd tests/MCG_3DPanel.Tests && dotnet test --filter PendingEditsTests` — Expected: FAIL (không biên dịch được, `PendingEdits` chưa có).
- [ ] **Step 3: Cài `PendingEdits` + `KeyRemap.Handles`** theo Interfaces; đọc/ghi qua `CorrectionJson` / `NamesJson` sẵn có.
- [ ] **Step 4: Chạy** test lọc rồi cả bộ — Expected: PASS, tổng = 740 + số test mới.
- [ ] **Step 5: Commit** `feat(pending): diff and apply edits made on the 2D drawing`

### Task 2: Đọc panel từ file 3D khi đang ở bản 2D (Remote3DSession)

**Files:**
- Create: `Commands/Remote3DSession.cs`
- Modify: `Commands/PanelSession.cs` (tách `internal static List<BuildResult> ReadAll(Database db, List<string> notes)` từ thân `For`)
- Modify: `Services/IBuild3DService.cs` (thêm `RemoteLink` + `BuildResult.Remote`)
- Modify: `Commands/Detail/Detail3DSource.cs` (cho chọn file không hỏi: `choose == null` ⇒ lấy file mới nhất, đã là `matches[0]`)
- Modify: `Services/Corrections/CorrectionStore.cs`, `Naming/NameStore.cs` (thêm `Snapshot(string panelKey) -> string` = JSON trạng thái cache, và `LoadJson(string panelKey, string json)` = thay cache bằng JSON, đánh dấu đã nạp)
- Modify: `Services/Corrections/CorrectionXRecord.cs` (`public const string PendingKey = "MCG3D_PENDING";`)

**Interfaces:**
- Consumes: Task 1 `PendingEdits.Apply/Keys`.
- Produces:
  - `class RemoteLink { string File3D; ObjectId AssyRef2D; ObjectId AssyBtr2D; bool Locked; string BaseCorr; string BaseNames; ISet<string> PendingKeys; }`; `BuildResult.Remote` (null = panel thật trong drawing này).
  - `static List<BuildResult> Remote3DSession.For2D(Document doc2D, out string message)` — mỗi reference `*_Assy` trong Model Space: `Detail3DSource.Open(db2D, doc2D.Name, assyRef, null, out why)`; giữ source theo (db2D, đường dẫn file, LastWriteTime) — file đổi ⇒ Dispose và đọc lại. `PanelSession.ReadAll(source.SourceDb, notes)`; với mỗi build: `BaseCorr/BaseNames = Snapshot(...)` ngay sau đọc, đọc XRecord pending từ `AssyBtr2D` trong db2D, `LoadJson` = `Apply(pending, base…)`, `PendingKeys = Keys(pending)`, `Locked = source.Stale == StaleState.Changed2D`. Không có file ⇒ không build, `why` vào message.
  - `static string Remote3DSession.SavePending(BuildResult b)` — `Diff(Base…, Snapshot…)` ⇒ ghi/xoá XRecord trong db2D (khoá document 2D), cập nhật `PendingKeys`; trả lỗi hoặc null.
  - `static ObjectId[] Remote3DSession.PathIn2D(BuildResult b, ObjectId[] idPath3D)` — đổi từng id theo handle sang db2D (`TryGetObjectId`), gốc = `AssyRef2D`; thiếu ⇒ null.
  - `static void Remote3DSession.Forget(Database db2D)` — Dispose mọi source của bản vẽ đó.

- [ ] **Step 1:** Tách `PanelSession.ReadAll` (không đổi hành vi `For`); chạy cả bộ test — Expected: PASS như trước.
- [ ] **Step 2:** Cài `RemoteLink`, `Snapshot/LoadJson`, `PendingKey`, `Remote3DSession` theo Interfaces.
- [ ] **Step 3:** Build — Expected: `Build succeeded`, 0 error; cả bộ test PASS.
- [ ] **Step 4: Commit** `feat(remote): read the panel of a 2D drawing from its 3D file`

### Task 3: Tab Result + Naming chạy trên bản 2D

**Files:**
- Modify: `UI/PaletteManager.cs` — `RefreshTabs`: `PanelSession.For` rỗng và drawing không phải file 3D ⇒ `Remote3DSession.For2D`; `DocumentToBeDestroyed` gọi `Remote3DSession.Forget(db)` (Guarded).
- Modify: `UI/ResultRow.cs`, `UI/NamingRow.cs` — `bool Pending` (dòng có khoá ∈ `PendingKeys`, xét cả `KeyRemap.Handles` của DeleteKey/FlipKey); `Path` của dòng remote = `PathIn2D(...)` theo `Part.IdPath`; dòng sinh thêm (Part null) ⇒ path = entity 2D của các handle trong khoá (`KeyRemap.Handles`), không có ⇒ null.
- Modify: `UI/PanelResultView.xaml(.cs)` — `RebuildMany`: build có `Remote` ⇒ không `RequestRebuild`, gọi `SavePending`, nạp lại dòng, status `"{n} edits waiting — press Update to apply them to the 3D and the drawings."`; `Remote.Locked` ⇒ cột sửa chỉ đọc + status hằng ở Global Constraints; nút **Update** trên toolbar chỉ hiện khi build remote, nhãn `Update ({n})` khi có pending, gọi Task 4. Cột đánh dấu dòng Pending theo `PanelStyles.xaml` (tooltip `Waiting for Update`).
- Modify: `UI/PanelNamingView.xaml.cs` — `SaveAll`: panel remote ⇒ `SavePending` thay `NameStore.Save` vào file 3D; khoá sửa khi Locked như Result.
- Modify: Pick (`SelectFromCad` cả 2 view) — remote: entity chọn ⇒ handle ⇒ dòng có `Part.IdPath` cuối cùng cùng handle.

- [ ] **Step 1:** Cài theo danh sách trên.
- [ ] **Step 2:** Build + cả bộ test — Expected: Build succeeded, PASS.
- [ ] **Step 3: Commit** `feat(ui): edit a panel from its 2D drawing, edits wait for Update`

### Task 4: Update từ bản 2D + vẽ lại bản vẽ

**Files:**
- Modify: `Commands/PanelCommands.cs`
  - `public static void RequestUpdateFrom2D(BuildResult remote)` — lưu yêu cầu tĩnh, `SendStringToExecute("MCG_UpdateFrom2D ")` trên document 2D; lệnh `[CommandMethod("MCG_UpdateFrom2D", CommandFlags.Session)]` chạy 1 `PanelJob { RefId = AssyRef2D, Target = File3D, Update = true }` qua `UpdateFile` bọc `IsBuilding`/`BuildFinished` như `RunBuild`.
  - `PrepareUpdate`: sau `ReadOld`, đọc pending từ bản 2D nguồn (`sourceFile` là document đang mở) ⇒ `PendingEdits.Apply` lên `carry.CorrectionsJson/NamesJson`; pending ≠ null ⇒ `HasEdits = true` (để `RemapCarry` chạy). Áp cho MỌI Update từ bản 2D (cả nút Build/Update ở tab Input).
  - Update 3D thành công ⇒ xoá XRecord pending trong bản 2D; thêm dòng `"{n} edits from the 2D drawing applied."` vào `run.Problems` khi n > 0. Thất bại ⇒ pending giữ nguyên.
- Create: `Commands/UpdateChain.cs` — sau Update 3D thành công (file 3D active): nếu bản 2D có Plan View của panel (entity XData `MCG3D_PLANVIEW` = btrHandle; thêm `PlanViewWriter.HasPlanView(Database db2D, string btrHandle)`) ⇒ `PlanViewCommand.Request()`; nghe `PlanViewCommand.Finished` ⇒ nếu `DetailCommands.HasPanelWith3DFile(doc2D, out hasDetail)` và `hasDetail` ⇒ chạy Detail Update (đổi `UpdateDetails` thành `internal`, gọi qua `DetailCommands.Request(DetailCommands.UpdateCommand)` trên doc2D); kết thúc ⇒ đóng file 3D nếu trước Update nó chưa mở (`CloseAndDiscard` — đã lưu), kích hoạt doc2D, status tổng: phần nào lỗi nêu tên phần đó (`"3D updated; Plan View failed: …"`).
- Test: `tests/MCG_3DPanel.Tests/UpdateChainRulesTests.cs` + `Services/Update/UpdateChainRules.cs` (thuần): `static string Summary(bool planView, string planErr, bool detail, string detailErr, int applied)`.

- [ ] **Step 1: Test fail** `UpdateChainRulesTests`:
  - `AllOk` ⇒ `"3D, Plan View and Details updated."`
  - `NoDrawings` (planView=false, detail=false) ⇒ `"3D updated."`
  - `PlanViewFailed` ⇒ `"3D updated; Plan View failed: X. Details were not updated."`
  - `DetailFailed` ⇒ `"3D and Plan View updated; Details failed: Y."`
  - `Applied3` thêm hậu tố `" 3 edits from the 2D drawing applied."`; applied 0 ⇒ không có hậu tố.
- [ ] **Step 2:** Chạy lọc — Expected: FAIL (chưa có `UpdateChainRules`).
- [ ] **Step 3:** Cài `UpdateChainRules`, rồi các thay đổi PanelCommands / UpdateChain / PlanViewWriter / DetailCommands.
- [ ] **Step 4:** Build + cả bộ test — Expected: Build succeeded, PASS.
- [ ] **Step 5: Commit** `feat(update): Update from the 2D drawing applies waiting edits and redraws`
- [ ] **Step 6: Thử trên AutoCAD (kỹ sư)** — bản 7D-16P chính thức đã có Plan View + Detail:
  1. Mở bản 2D (file 3D đóng) ⇒ Result + Naming có dòng; chọn dòng ⇒ sáng hình trên Plan View.
  2. Đổi ⇄ một HP + đặt tên một part ⇒ dòng đánh dấu chờ, không rebuild, nút `Update (2)`.
  3. Lưu, đóng, mở lại bản 2D ⇒ vẫn `Update (2)`.
  4. Update ⇒ 3D dựng lại, Plan View + Detail vẽ lại, về bản 2D, file 3D đóng, dòng hết dấu chờ.
  5. DC lại block đã sửa từ Global ⇒ tab khoá sửa với status lệch; Update ⇒ mở khoá.
