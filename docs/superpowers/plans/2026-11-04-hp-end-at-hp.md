# HP End tại HP — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Khi đầu 1 HP chạm thân 1 HP khác, trừ khỏi HP đầu 1 dao cắt (mặt hàn + cung R30 thoát bulb) theo quy cách CAS "HP TO HP END", đúng cả khi HP xiên trong mặt bằng.

**Architecture:** Toán thuần (không AutoCAD) nằm trong `HpEndAtHpGeometry` để unit test được: biên dạng dao cắt, nhận diện cặp (HP đầu, HP chạy), nửa chiều dài extrude. `HpEndAtHpBuilder` (internal static, khuôn `WebHpCutoutBuilder`) bọc phần AutoCAD: dựng Region → đặt theo mặt cắt HP chạy → extrude dọc trục HP chạy → BoolSubtract khỏi solid HP đầu. `Build3DService` gọi sau vòng dựng chính.

**Tech Stack:** C# / AutoCAD .NET API (net8.0-windows), xUnit (`tests/MCG_3DPanel.Tests`, chỉ link file thuần).

**Spec:** `docs/superpowers/specs/2026-11-04-hp-end-at-hp-design.md`

## Global Constraints

- `R = 30` cố định mọi cỡ HP. Khoảng lùi = `p + R`; `p = c + r − t_run` (INSIDE), `p = 0` (OUTSIDE).
- `d = b − dx`, `dx = b − r − (3r − t + c)/√3`; `c, r` tra `HpProfileService.LookupCR(b)` (bảng, KHÔNG dùng r=4.5 của block).
- Không vát 5×5. Không override ở tab Result. Test trực quan do người dùng tự làm.
- Ngưỡng góc `|axis_h · axis_m| ≤ 0.70`; ngoài ngưỡng → không cắt + dòng cảnh báo Report.
- Mối nối đầu-đầu (≤ 25mm tới đầu mút HP khác) → không áp dụng.
- Lỗi 1 cặp → bỏ cặp đó, ghi Report, không làm vỡ bản dựng.
- File thuần KHÔNG `using Autodesk.*` (test project không nạp được acdbmgd).
- Code/comment tiếng Việt theo phong cách hiện có; mỗi đối tượng 1 file (skill module-per-object).

## Review Focus

- HP đầu vẽ LẤN qua mặt tiếp xúc (đầu mút nằm trên/qua đường tâm HP chạy) → vẫn nhận, phía xác định theo đầu kia; phần lấn bị cắt (W). → test `Match_EndpointOvershootsCenterline_StillMatchesSameSide` (Task 2).
- Cả 2 HP cùng xiên (không song song X/Y) → khớp như trường hợp vuông xoay khung. → test `Match_BothDiagonal_RotatedFrame` (Task 2).
- Đầu HP rơi gần đầu mút HP chạy (góc chữ L) → không cắt, giữ vuông. → test `Match_NearRunEnd_Rejected` (Task 2).
- HP nhỏ đến mức `b − d − R ≤ 0` → `CutterOutline` trả null, builder bỏ qua + Report. → test `CutterOutline_NoWeldLength_ReturnsNull` (Task 1).
- HP chạy thiếu `BellyDir` → bỏ cặp, Report. → test `Match_NoBelly_Rejected` (Task 2).

---

### Task 1: Biên dạng dao cắt + nửa span (toán thuần)

**Files:**
- Create: `Services/Builders/HpEndAtHpGeometry.cs`
- Modify: `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj` (thêm `<Compile Include="..\..\Services\Builders\HpEndAtHpGeometry.cs" Link="HpEndAtHpGeometry.cs" />`)
- Test: `tests/MCG_3DPanel.Tests/HpEndAtHpGeometryTests.cs`

**Interfaces:**
- Produces (namespace `MCG_3DPanel.Services.Builders`, `public static class HpEndAtHpGeometry`):
  - `const double R = 30.0, MARGIN = 2.0, SPAN_MARGIN = 10.0`
  - `static double BulbHeight(double b, double t, double c, double r)` → `b − dx`
  - `static double BulbProtrusion(double c, double r, double t)` → `c + r − t`
  - `static List<(double X, double Y, double Bulge)> CutterOutline(double b, double tRun, double c, double r, bool inside)` — hệ x'–y của spec §5b; null nếu tham số vô lý hoặc `b − d − R ≤ 0`.
  - `static double HalfSpan(double tEnd, double tRun, double c, double r, bool inside, double cosTheta)`

Thứ tự đỉnh (spec §5b), `p = inside ? BulbProtrusion : 0`, `W = tRun + c + r + MARGIN`, `yW = −(b−d−R)`, `yC = −(b−d)`:
`(−W,+M,0) → (0,+M,0) → (0,yW,0) → (p,yW, −tan(π/8)) → (p+R,yC,0) → (p+R,−b−M,0) → (−W,−b−M,0)`.
Nếu `p = 0` bỏ đỉnh `(p,yW)` trùng `(0,yW)` — gán bulge cung cho `(0,yW)` (không để đoạn 0 dài).
Cung đi từ đỉnh (90°) sang phải (0°) quanh tâm `(p,yC)` = chiều kim đồng hồ ⇒ bulge **âm** `−0.41421`.

`HalfSpan = (c + r + tEnd)/sinθ + (p + R + W)·|cosθ|/sinθ + SPAN_MARGIN`, `sinθ = √(1−cos²)`.

- [ ] **Step 1: Write the failing tests**

```csharp
public class HpEndAtHpGeometryTests
{
    const double Tol = 1e-3;
    // HP120×6 theo bảng: c=17, r=5 ⇒ dx=99.98889, d=20.01111, p=16
    [Fact] public void BulbHeight_Hp120x6_Table() =>
        Assert.Equal(20.0111, HpEndAtHpGeometry.BulbHeight(120, 6, 17, 5), 3);

    [Fact] public void BulbHeight_MatchesDumpBlock_R45()   // block HP_120x6_sv: mặt hàn 71.34
        => Assert.InRange(120 - HpEndAtHpGeometry.BulbHeight(120, 6, 17, 4.5) - 30, 71.29, 71.39);

    [Fact] public void CutterOutline_Inside_Hp120()
    {
        var o = HpEndAtHpGeometry.CutterOutline(120, 6, 17, 5, inside: true);
        // 7 đỉnh: (-30,2) (0,2) (0,-69.9889) (16,-69.9889,bulge -0.41421) (46,-99.9889) (46,-122) (-30,-122)
        Assert.Equal(7, o.Count);
        Assert.Equal((16.0, -69.9889), (o[3].X, Math.Round(o[3].Y, 4)));
        Assert.Equal(-0.41421, o[3].Bulge, 4);
        Assert.Equal(46.0, o[4].X, 3); Assert.Equal(-99.9889, o[4].Y, 3);
        Assert.Equal(-122.0, o[5].Y, 3); Assert.Equal(-30.0, o[0].X, 3);
    }

    [Fact] public void CutterOutline_Outside_Hp120()
    {
        var o = HpEndAtHpGeometry.CutterOutline(120, 6, 17, 5, inside: false);
        // 6 đỉnh: cung bắt đầu tại (0,-69.9889), kết thúc (30,-99.9889)
        Assert.Equal(6, o.Count);
        Assert.Equal(0.0, o[2].X, 3); Assert.Equal(-0.41421, o[2].Bulge, 4);
        Assert.Equal(30.0, o[3].X, 3); Assert.Equal(-99.9889, o[3].Y, 3);
    }

    [Fact] public void CutterOutline_Hp60_And_Hp430_NotDegenerate()
    {
        Assert.NotNull(HpEndAtHpGeometry.CutterOutline(60, 5, 13, 3.5, true));     // mặt hàn ≈ 15.8
        Assert.NotNull(HpEndAtHpGeometry.CutterOutline(430, 14, 62.5, 19.5, true));
    }

    [Fact] public void CutterOutline_NoWeldLength_ReturnsNull()
        => Assert.Null(HpEndAtHpGeometry.CutterOutline(40, 5, 13, 3.5, true));

    [Fact] public void HalfSpan_Perpendicular_And_Oblique()
    {
        Assert.Equal(17 + 5 + 6 + 10, HpEndAtHpGeometry.HalfSpan(6, 6, 17, 5, true, 0.0), 3);
        Assert.True(HpEndAtHpGeometry.HalfSpan(6, 6, 17, 5, true, 0.5) > 38 / Math.Sin(Math.PI / 3));
    }
}
```

- [ ] **Step 2: Run to verify fail** — `dotnet test tests/MCG_3DPanel.Tests --filter HpEndAtHpGeometryTests` → FAIL (compile: `HpEndAtHpGeometry` không tồn tại).
- [ ] **Step 3: Implement** các thành viên trong Interfaces ở `Services/Builders/HpEndAtHpGeometry.cs` (chỉ `using System; using System.Collections.Generic;`). Tham số vô lý (`b,t,c,r ≤ 0`, `c ≤ t`) → null.
- [ ] **Step 4: Run to verify pass** — cùng lệnh → PASS toàn bộ.
- [ ] **Step 5: Commit** — `git add Services/Builders/HpEndAtHpGeometry.cs tests/MCG_3DPanel.Tests && git commit -m "feat(hp-end-at-hp): cutter outline geometry"`

---

### Task 2: Nhận diện cặp (HP đầu, HP chạy) (toán thuần)

**Files:**
- Modify: `Services/Builders/HpEndAtHpGeometry.cs`
- Test: `tests/MCG_3DPanel.Tests/HpEndAtHpMatchTests.cs`

**Interfaces:**
- Consumes: `BulbProtrusion` (Task 1).
- Produces:
  - `const double TOL_TOUCH = 10.0, END_MARGIN = 20.0, JOINT_TOL = 25.0, MAX_COS = 0.70`
  - `public sealed class HpEndMatch { bool Ok; string Reject; bool Inside; double CosTheta; double Dist; (double X,double Y) Foot; (double X,double Y) Normal; (double X,double Y) RunAxis; }`
    - `Reject ∈ { null, "joint", "far", "nearRunEnd", "angle", "noBelly", "degenerate" }`
    - `Foot` = chân vuông góc của đầu mút trên ĐƯỜNG TÂM HP chạy; `Normal` = pháp tuyến đơn vị ⟂ trục HP chạy hướng về phía HP đầu; `RunAxis` = đơn vị `runA→runB`; `CosTheta = |axis_h·RunAxis|`; `Dist` = khoảng cách đầu mút tới đoạn tâm HP chạy.
  - `static HpEndMatch Match((double X,double Y) end, (double X,double Y) otherEnd, (double X,double Y) runA, (double X,double Y) runB, double tRun, (double X,double Y) runBelly, double c, double r)`

Thứ tự kiểm (dừng ở lý do đầu tiên): degenerate (độ dài ~0) → joint (`end` ≤ 25 tới `runA` hoặc `runB`) → noBelly (`|runBelly| < 1e-9`) → phía: `Normal = sign((otherEnd − Foot)·perp)·perp` → `Inside = runBelly·Normal > 0` → far (`Dist > tRun/2 + (Inside ? BulbProtrusion(c,r,tRun) : 0) + TOL_TOUCH`) → nearRunEnd (tham số dọc trục `s ∉ [END_MARGIN, L − END_MARGIN]`) → angle (`CosTheta > MAX_COS`) → Ok.

- [ ] **Step 1: Write the failing tests** (HP chạy: `runA=(0,0)`, `runB=(2000,0)`, `tRun=6`, `c=17`, `r=5`)

```csharp
[Fact] public void Match_Perpendicular_Inside()   // belly (0,1), HP đầu ở phía +Y, đầu mút tại mặt bụng (y=3)
{ var m = Match((1000,3),(1000,800),(0,0),(2000,0),6,(0,1),17,5);
  Assert.True(m.Ok); Assert.True(m.Inside); Assert.Equal(1.0, m.Normal.Y, 6); Assert.Equal(0.0, m.CosTheta, 6); }

[Fact] public void Match_Perpendicular_Outside()  // belly (0,-1)
{ var m = Match((1000,3),(1000,800),(0,0),(2000,0),6,(0,-1),17,5); Assert.True(m.Ok); Assert.False(m.Inside); }

[Fact] public void Match_StopsAtBulbTip_Inside_StillTouches()   // y = 3+16+5 = 24 ≤ 3+16+10
  => Assert.True(Match((1000,24),(1000,800),(0,0),(2000,0),6,(0,1),17,5).Ok);

[Fact] public void Match_Far_Rejected_Outside()                 // y=20 > 3+0+10
  => Assert.Equal("far", Match((1000,20),(1000,800),(0,0),(2000,0),6,(0,-1),17,5).Reject);

[Fact] public void Match_EndpointOvershootsCenterline_StillMatchesSameSide()
{ var m = Match((1000,-2),(1000,800),(0,0),(2000,0),6,(0,1),17,5);
  Assert.True(m.Ok); Assert.Equal(1.0, m.Normal.Y, 6); }

[Fact] public void Match_Joint_Rejected() => Assert.Equal("joint", Match((10,3),(10,800),(0,0),(2000,0),6,(0,1),17,5).Reject);
// s=1990 > L−20, cách runB 29.7 (> JOINT_TOL), Dist 28 ≤ 3+16+10 ⇒ chỉ lý do nearRunEnd
[Fact] public void Match_NearRunEnd_Rejected() => Assert.Equal("nearRunEnd", Match((1990,28),(1990,800),(0,0),(2000,0),6,(0,1),17,5).Reject);

[Fact] public void Match_Oblique60_Ok_Oblique30_Angle()
{ Assert.True(Match((1000,3),(1000+800*Math.Cos(Math.PI/3),3+800*Math.Sin(Math.PI/3)),(0,0),(2000,0),6,(0,1),17,5).Ok);
  Assert.Equal("angle", Match((1000,3),(1000+800*Math.Cos(Math.PI/6),3+800*Math.Sin(Math.PI/6)),(0,0),(2000,0),6,(0,1),17,5).Reject); }

[Fact] public void Match_BothDiagonal_RotatedFrame()   // xoay toàn bộ ca vuông INSIDE 37°, kết quả không đổi
{ /* rot(p) = (x cos37 − y sin37, x sin37 + y cos37) cho end, otherEnd, runA, runB, belly */
  // Assert: Ok, Inside, CosTheta≈0, Normal ≈ rot((0,1)) (sai số 1e-6)
}

[Fact] public void Match_NoBelly_Rejected() => Assert.Equal("noBelly", Match((1000,3),(1000,800),(0,0),(2000,0),6,(0,0),17,5).Reject);
```
(`Match` = `HpEndAtHpGeometry.Match`; implementer viết thân `Match_BothDiagonal_RotatedFrame` theo comment.)

- [ ] **Step 2: Run to verify fail** — `dotnet test tests/MCG_3DPanel.Tests --filter HpEndAtHpMatchTests` → FAIL (compile).
- [ ] **Step 3: Implement** `HpEndMatch` + `Match` theo thứ tự kiểm ở trên.
- [ ] **Step 4: Run to verify pass** — `dotnet test tests/MCG_3DPanel.Tests` → PASS toàn bộ (cả test cũ).
- [ ] **Step 5: Commit** — `git commit -am "feat(hp-end-at-hp): detect HP end at running HP"` (add file test mới).

---

### Task 3: Builder AutoCAD + tích hợp Build3DService

**Files:**
- Modify: `Services/HpProfileService.cs` — thêm vào `IHpProfileService` + lớp: `List<VertexBulge> HpEndAtHpCutter(double b, double tRun, double c, double r, bool inside)` (đổi tuple của `HpEndAtHpGeometry.CutterOutline` sang `VertexBulge`; null → null).
- Create: `Services/Builders/HpEndAtHpBuilder.cs`
- Modify: `Services/Build3DService.cs:68-136` — gom `hpSolids` như `webSolids`; sau khối `WebHpCutoutBuilder` gọi builder rồi `FillGeometry` lại các HP bị cắt.

**Interfaces:**
- Consumes: `HpEndAtHpGeometry.Match`, `HalfSpan`, `HpEndMatch` (Task 1–2); `HpProfileService.LookupCR`; `ClassifyUtil.Endpoints`; `GeometryUtil.RegionFrom`; `PanelPart.FootprintThickness/AxisDir/BellyDir/Id`.
- Produces: `internal static class HpEndAtHpBuilder { public static List<string> Apply(BuildContext ctx, IReadOnlyList<(PanelPart hp, Solid3d sol)> hpSolids); }` — trả khoá `"hpHandle~runHandle"` đã trừ thành công.

Thuật toán `Apply`:
1. `b = ctx.Input.HpHeight` (≤0 → Report, return rỗng); `LookupCR(b, out c, out r, out _)`.
2. Với mỗi `(h, sol)`, mỗi đầu mút `he` của `h` (`Endpoints`, đầu kia = `otherEnd`), xét mọi HP `m ≠ h` trong `ctx.Cls.OfType(PartType.HP)`: `Match(...)` với `tRun = m.FootprintThickness`, `runBelly = m.BellyDir`. Chọn `Ok` có `Dist` nhỏ nhất. Không có `Ok` nhưng có `"angle"` → Report cảnh báo `HP-end-at-HP SKIP angle h=.. m=.. θ=..`.
3. Dao cắt: `ctx.HpProfile.HpEndAtHpCutter(b, tRun, c, r, inside)` (null → Report, bỏ) → `RegionFrom`.
   Hệ local: `X → n` (Normal), `Y → +Z`, `Z → n × ZAxis` (ép hệ thuận tay; song song ±RunAxis). Gốc = `Foot + n·tRun/2 − Zlocal·halfSpan`, Z=0, `halfSpan = HalfSpan(h.FootprintThickness, tRun, c, r, inside, CosTheta)`. `AlignCoordinateSystem` → `CreateExtrudedSolid(Zlocal·2·halfSpan)` → `sol.BooleanOperation(BooleanOperationType.BoolSubtract, cutter)`.
4. try/catch từng cặp (khuôn `WebHpCutoutBuilder`), dispose Region/cutter. Report thành công: `HP-end-at-HP h=<handle> m=<handle> INSIDE|OUTSIDE θ=<deg:0> p=<p:0.#> d=<d:0.##>`.

- [ ] **Step 1: Implement** `HpEndAtHpCutter` + `HpEndAtHpBuilder.Apply` + tích hợp `Build3DService`.
- [ ] **Step 2: Build** — `dotnet build -c Debug` (gốc repo) → `Build succeeded`, 0 error, không warning mới trong 3 file trên.
- [ ] **Step 3: Tests** — `dotnet test tests/MCG_3DPanel.Tests` → PASS toàn bộ.
- [ ] **Step 4: Xác nhận không đổi hành vi đầu HP khác** — đọc lại `HpBuilder.SnipEnd` / `HpEndBracketBuilder.WantsEndBracket`: đầu chạm HP ⇒ `IsFreeEnd=false` (không vát), HP ∉ `TRANSVERSE_TYPES` (không End 30). Không sửa; ghi 1 dòng xác nhận vào commit message.
- [ ] **Step 5: Commit** — `git commit -am "feat(hp-end-at-hp): subtract R30 bulb-clearance cutter from ending HP"`

---

### Task 4: Skill `hp-end-at-hp` + liên kết tài liệu

**Files:**
- Create: `.claude/skills/hp-end-at-hp/hp-end-at-hp_SKILL.md` (frontmatter `name: hp-end-at-hp`, `description:` 1 dòng tiếng Việt: quy cách HP TO HP END — đầu HP kết thúc tại lưng/bụng HP khác, R30 + khoảng lùi p+30, nhận diện + dao cắt theo mặt cắt HP chạy; đọc khi dựng/nhận diện đầu HP chạm HP).
- Modify: `.claude/skills/sniped-end/sniped-end_SKILL.md` §5b — thêm 1 dòng dưới gạch "Đầu HP trùng đầu 1 HP khác": đầu HP chạm THÂN HP khác → xem [[hp-end-at-hp]].
- Modify: `.claude/skills/hp-profile/hp-profile_SKILL.md` — dòng liên kết `d = b − dx`, `p = c + r − t` dùng ở [[hp-end-at-hp]].

Nội dung skill (nguồn: spec §2, §5a–5c): nguồn dump (`CAS.dump.json` Model Space, polyline `1F247`/`1F251`/`1F255`, block `HP_120x6_sv`); bảng tham số R/d/p/khoảng lùi; hình phác ASCII; quyết định kỹ sư 2026-11-04 (R30 cố định, không vát 5×5, p/d theo bảng, khe 1.31 = lỗi vẽ, không override); nhận diện (thứ tự Reject); đặt dao (hệ local, halfSpan); file cài đặt (`HpEndAtHpGeometry`, `HpEndAtHpBuilder`, `HpProfileService.HpEndAtHpCutter`, `Build3DService`); mục "Chưa test trực quan".

- [ ] **Step 1: Viết skill + 2 dòng liên kết.**
- [ ] **Step 2: Kiểm** — `grep -n "hp-end-at-hp" .claude/skills/*/*_SKILL.md` → có ở 3 file.
- [ ] **Step 3: Commit** — `git add .claude/skills && git commit -m "docs(skill): hp-end-at-hp"`
