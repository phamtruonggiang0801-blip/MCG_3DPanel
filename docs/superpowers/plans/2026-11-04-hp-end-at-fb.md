# HP End tại FB — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Đầu HP chạm thân FB → trừ dao cắt (mặt hàn `h_fb−15` + vát 30°; FB cao ⇒ biên dạng HP–HP p=0) khỏi HP đầu.

**Architecture:** Mở rộng `HpEndAtHpGeometry` (toán thuần, unit test) + `HpEndAtHpBuilder` (AutoCAD): ứng viên chạy gồm HP và FlatBar; dao cắt cách A trong mặt cắt member chạy.

**Tech Stack:** C# AutoCAD .NET (net8.0-windows), xUnit.

**Spec:** `docs/superpowers/specs/2026-11-04-hp-end-at-fb-design.md` (nền: `2026-11-04-hp-end-at-hp-design.md`)

## Global Constraints

- 15 và 30° cố định mọi cỡ. Mặt hàn `yW = −(h_fb − 15)`; vát tới `((b − (h_fb−15))·√3, −b)`.
- `h_fb − 15 ≥ b` ⇒ `CutterOutline(b, tFb, c, r, inside:false)` (HP–HP p=0).
- Nhận diện = luật HP–HP, FB: không kiểm noBelly, `Inside=false`, `p=0`.
- Phủ hết bề rộng HP đầu kể cả bulb. File toán thuần không `using Autodesk.*`.

## Review Focus

- `h_fb ≤ 15` (mặt hàn ≤ 0) → null, bỏ qua + Report. → `FbCutterOutline_TooShortFb_Null` (Task 1).
- `h_fb − 15` đúng bằng `b` → chuyển sang R30 (biên). → `FbCutterOutline_TallFb_UsesR30Outside` dùng h_fb=135 (Task 1).
- Đầu HP chạm cả 1 HP và 1 FB → chọn gần nhất. (builder, AutoCAD — kiểm bằng đọc code, Task 2)
- `FlatBarHeight` = 0 (spec FB trống) → bỏ ứng viên FB, Report 1 lần. (Task 2)
- Chiều dài extrude phủ vát dài ~100mm khi xiên. → `HalfSpanX_UsesCutterReach` (Task 1).

---

### Task 1: Toán thuần

**Files:** Modify `Services/Builders/HpEndAtHpGeometry.cs`; Test `tests/MCG_3DPanel.Tests/HpEndAtFbTests.cs`

**Interfaces — Produces:**
- `const double FB_WELD_CLEAR = 15.0`
- `static List<(double X,double Y,double Bulge)> FbCutterOutline(double b, double hFb, double tFb, double c, double r)` — `(−W,+M) → (0,+M) → (0,yW) → (xB,−b−M) → (−W,−b−M)`, `W = tFb+c+r+M`, `xB = (b+M−(hFb−15))·√3`; `hFb−15 ≥ b` ⇒ `CutterOutline(b,tFb,c,r,false)`; `hFb−15 ≤ 0` hoặc tham số ≤ 0 ⇒ null.
- `static double HalfSpanX(double tEnd, double c, double r, double xMax, double cosTheta)` = `(c+r+tEnd)/sinθ + xMax·|cosθ|/sinθ + SPAN_MARGIN`; `HalfSpan` gọi lại nó với `xMax = p+R+W`.
- `Match(..., double tEnd = 0, bool runIsFb = false)` — `runIsFb` ⇒ bỏ noBelly, `Inside=false`.

- [ ] **Step 1: Failing tests**
```csharp
[Fact] void FbCutterOutline_Fb80_Hp120()   // 5 đỉnh: (-30,2) (0,2) (0,-65) (98.7269,-122) (-30,-122), mọi bulge 0
[Fact] void FbCutterOutline_ChamferMatchesDump()   // đường (0,-65)→o[3] đi qua (95.2628,-120) (sai số 1e-3)
[Fact] void FbCutterOutline_TallFb_UsesR30Outside() // hFb=135 ⇒ 6 đỉnh == CutterOutline(120,6,17,5,false)
[Fact] void FbCutterOutline_TooShortFb_Null()       // hFb=15 ⇒ null
[Fact] void HalfSpanX_UsesCutterReach()             // HalfSpanX(6,17,5,98.7269,0)=38; cos=0.5 ⇒ 28/s+98.7269·0.5/s+10, s=sin60
[Fact] void HalfSpan_EqualsHalfSpanXWithPRW()       // HalfSpan(6,6,17,5,true,0.5) == HalfSpanX(6,17,5,16+30+30,0.5)
[Fact] void Match_Fb_NoBellyNeeded_Ok()             // run (0,0)-(2000,0) t=6 belly (0,0) runIsFb ⇒ Ok, !Inside
[Fact] void Match_Fb_FarUsesNoBulb()                // end (1000,15) ⇒ "far" (15 > 3+0+10)
```
- [ ] **Step 2:** `dotnet test tests/MCG_3DPanel.Tests --filter HpEndAtFbTests` → FAIL (compile).
- [ ] **Step 3:** Implement Interfaces.
- [ ] **Step 4:** `dotnet test tests/MCG_3DPanel.Tests` → PASS toàn bộ.
- [ ] **Step 5:** Commit `feat(hp-end-at-fb): FB cutter outline + detection`.

### Task 2: Builder + service

**Files:** Modify `Services/HpProfileService.cs` (`IHpProfileService.HpEndAtFbCutter(double b, double hFb, double tFb, double c, double r)` → `List<VertexBulge>`), `Services/Builders/HpEndAtHpBuilder.cs`.

- `FindRun`: ứng viên = `ctx.Cls.OfType(HP)` + `OfType(FlatBar)` (FB chỉ khi `ctx.Input.FlatBarHeight > 0`; = 0 ⇒ 1 dòng Report `SKIP FB: FlatBarHeight=0`); FB gọi `Match(..., runIsFb:true)`; giữ gần nhất.
- `BuildCutter`: FB ⇒ `HpEndAtFbCutter(b, hFb, run.FootprintThickness, c, r)`; `half = HalfSpanX(tEnd, c, r, xMax, cos)` với `xMax = max|X|` các đỉnh section (áp cho cả HP, thay `HalfSpan`).
- Report FB: `HP-end-at-FB h=.. m=.. θ=.. hfb=.. weld=<hFb−15 hoặc b−d−R> mode=CHAMFER|R30`.
- `TouchesRunningHp` tự tính cả FB (qua `FindRun`).
- [ ] **Step 1:** Implement. **Step 2:** `dotnet build MCG_3DPanel.csproj -c Debug` → Build succeeded. **Step 3:** `dotnet test tests/MCG_3DPanel.Tests` → PASS. **Step 4:** Commit `feat(hp-end-at-fb): cut ending HP at flat bar`.

### Task 3: Skill

**Files:** `.claude/skills/hp-end-at-hp/hp-end-at-hp_SKILL.md` — thêm "§7 HP end tại FB" (nguồn dump `1F20B`, quy cách, quyết định kỹ sư 2026-11-04, cài đặt); description thêm "+ HP end tại FB".
- [ ] **Step 1:** Viết. **Step 2:** `grep -n "HP end tại FB" .claude/skills/hp-end-at-hp/hp-end-at-hp_SKILL.md` có kết quả. **Step 3:** Commit `docs(skill): HP end at FB`.
