# Tripping Bracket — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Mỗi đoạn dầm (web có flange, 2 đầu chạm dầm liên tục có flange, không phải dầm ngoài cùng) có ≥ 3 HP xuyên qua ⇒ 1 bracket B dưới HP gần giữa đoạn, phía chiều đổ, đảo phía được ở tab Result.

**Architecture:** Luật thuần (`TrippingBracketRules`, unit test) + builder AutoCAD `TrippingBracketBuilder` (khuôn `WebCollarPlateBuilder`); hình tấm dùng chung với bracket B (`HpEndBracketBuilder.BuildPlate`, polygon từ luật thuần). Đảo phía qua `PartCorrection.FlipDir` lưu theo `FlipKey` của solid sinh thêm.

**Tech Stack:** C# AutoCAD .NET (net8.0-windows), xUnit, WPF (tab Result).

**Spec:** `docs/superpowers/specs/2026-11-04-tripping-bracket-design.md`

## Global Constraints

- Bracket B: `Ht = WebHeight − b`, `reach = max(180, 30 + 1.5b)`, `R=50`, `topL=50`, `e15=15`, `off2=30`; `overhang ≥ 50` ⇒ brkt_1 `d1 = clamp(overhang−15, 15, reach−15)`, đáy y=0; ngược lại brkt_2 `d1=15`, đáy y=30. Tấm đồng phẳng thân HP (căn giữa). Mép đứng tại mặt web dầm (không khe).
- Đoạn dầm: cắt web run tại chỗ 1 web run có flange KHÁC phủ giao điểm (liên tục) mà run này KHÔNG phủ (đứt). Chỉ đoạn có 2 đầu là điểm cắt. ⚠️ `WebRuns` gom web thẳng hàng khe ≤ 20 (`TOL_GAP`) ⇒ run có thể xuyên qua dầm liên tục — PHẢI tự cắt.
- HP xuyên = `CrossesThrough` + kẹp `tCross ∈ [b/2, L−b/2]` (như `WebHpCutoutBuilder`).
- ≥ 3 HP ⇒ 1 bracket, gần giữa (giữa 2 mặt dầm liên tục = mút part thật của đoạn); hoà ≤ 1mm ⇒ phía chiều đổ dọc trục dầm.
- Ngoài cùng: run có flange cùng phương (|cos| ≥ 0.7) có `PerpCenter` min/max (tol 50) ⇒ bỏ.
- Chiều đổ tại P: `(1, dozY)`, `dozY` = Port −1 / Starboard +1 / Center `P.Y ≤ center.Y ? +1 : −1`, `center = TopPlate.Centroid`. Phía = dấu `doz·n_web`, FlipDir ⇒ đảo.
- Khoá: `TrippingBracket:<beam spine SourceKey>~<hp SourceKey>` (DeleteKey = FlipKey).
- File thuần không `using Autodesk.*`.

## Review Focus

- Run gom xuyên dầm liên tục (1B5E6+1B629 cách 6mm) ⇒ phải ra 2 đoạn. → `Segments_SplitAtCuts` (Task 1) + đọc code cắt (Task 3).
- Đoạn chỉ 1 đầu chạm dầm liên tục (đầu kia tự do/biên không flange) ⇒ không bracket. → `Segments_RequireCutAtBothEnds` (Task 1).
- Refactor bracket B không đổi polygon. → `BracketBPolygon_MatchesLegacyOrder` (Task 1).
- Dòng tripping trên tab Result đảo được, xoá được. → `Describe_TrippingBracket_CanFlip` (Task 2).
- HP xiên qua dầm (không ⟂) ⇒ localX theo trục HP, gốc trên tuyến HP tại mặt web. (Task 3, đọc code)

---

### Task 1: Luật thuần + polygon bracket B dùng chung

**Files:** Create `Services/Builders/TrippingBracketRules.cs`; link vào `tests/MCG_3DPanel.Tests/MCG_3DPanel.Tests.csproj`; Test `tests/MCG_3DPanel.Tests/TrippingBracketRulesTests.cs`.

**Produces** (`public static class TrippingBracketRules`):
- `const double TIE_TOL = 1.0; const int MIN_HP = 3;`
- `static List<(double lo, double hi)> Segments(IReadOnlyList<double> cuts)` — cuts đã sort/khử trùng (≤ 1mm) ⇒ các khoảng giữa 2 cut liên tiếp.
- `static int? PickHp(IReadOnlyList<double> s, double mid, double tieSign)` — `s.Count < 3` ⇒ null; ngược lại chỉ số `|s−mid|` nhỏ nhất, hoà (≤ TIE_TOL) ⇒ chọn `sign(s−mid) == sign(tieSign)`.
- `static int Side(double dozDotN, bool flip)` — `dozDotN ≥ 0 ? +1 : −1`, flip ⇒ đảo.
- `static (bool big, double d1, double baseY) SelectBracketB(double overhang, bool haveFlange, double reach)`.
- `static List<(double X, double Y, double Bulge)> BracketBPolygon(double reach, double Ht, double d1, double baseY)` — thứ tự HIỆN TẠI của `HpEndBracketBuilder.Build`: `(reach,Ht−15,0) (d1,baseY,0) (0,baseY,0) (0,Ht−50,+0.41421356) (50,Ht,0) (reach,Ht,0)`.

- [ ] **Step 1: Failing tests**
```csharp
[Fact] void Segments_SplitAtCuts()            // cuts {0, 9605, 11311} ⇒ [(0,9605),(9605,11311)]
[Fact] void Segments_RequireCutAtBothEnds()   // cuts {500} ⇒ rỗng
[Fact] void PickHp_DumpExample()              // s {633.6,833.6,1043.6,1273.6}, mid 959.6 ⇒ 2
[Fact] void PickHp_LessThan3_Null()           // {100,200} ⇒ null
[Fact] void PickHp_Tie_FollowsDoz()           // {100,300,500,700} mid 400 ⇒ tieSign +1 ⇒ 2 (500); −1 ⇒ 1 (300)
[Fact] void Side_DozAndFlip()                 // (0.5,false)=+1 (−0.5,false)=−1 (0.5,true)=−1
[Fact] void SelectBracketB_Overhang()         // (37,true,210) ⇒ (false,15,30); (64,true,210) ⇒ (true,49,0); (300,true,210) ⇒ (true,195,0); (80,false,210) ⇒ (false,15,30)
[Fact] void BracketBPolygon_MatchesLegacyOrder() // (210,254,15,30) ⇒ 6 đỉnh đúng thứ tự trên, bulge đỉnh 4 = 0.41421356
```
- [ ] **Step 2:** `dotnet test tests/MCG_3DPanel.Tests --filter TrippingBracketRulesTests` → FAIL (compile).
- [ ] **Step 3:** Implement; `HpEndBracketBuilder` dùng `SelectBracketB` + `BracketBPolygon` (thay code tại dòng ~201-203, ~317-325).
- [ ] **Step 4:** `dotnet test tests/MCG_3DPanel.Tests` PASS; `dotnet build MCG_3DPanel.csproj -c Debug` succeeded.
- [ ] **Step 5:** Commit `feat(tripping-bracket): pure rules + shared bracket B polygon`.

### Task 2: Tab Result — đảo phía solid sinh thêm

**Files:** `UI/ResultCatalog.cs` (Describe solidType `"TrippingBracket"` ⇒ `ResultFamily.Bracket`, Kind `"Tripping B"`, `CanFlip = true`, chỉ đọc kind); `Services/IBuild3DService.cs` (`BuiltSolid.FlipKey`); `UI/ResultRow.cs` (`p == null && s.FlipKey != null` ⇒ `CanFlip = d.CanFlip && !s.Deleted`, `SourceKey = s.FlipKey`, `Flipped` từ `CorrectionStore.Get(panelKey, s.FlipKey)`); Test `tests/MCG_3DPanel.Tests/ResultCatalogTests.cs`.
- [ ] **Step 1:** Failing test `Describe_TrippingBracket_CanFlip` (Family Bracket, Kind "Tripping B", CanFlip true, Options rỗng/null).
- [ ] **Step 2:** RED. **Step 3:** Implement. **Step 4:** test + build PASS. **Step 5:** Commit `feat(tripping-bracket): flip side from Result tab`.

### Task 3: Builder + tích hợp

**Files:** Create `Services/Builders/TrippingBracketBuilder.cs`; Modify `Services/Builders/HpEndBracketBuilder.cs` (`Build` → `internal static Solid3d BuildPlate(Point3d p, Vector3d localX, Vector3d thkDir, double t, double reach, double Ht, double d1, double baseY, double H, bool centered)`; `FlangeOverhang` → `internal`), `Services/Build3DService.cs` (sau vòng bracket B: append solid, `BuiltSolid { Type="TrippingBracket", DeleteKey=key, FlipKey=key }`, Report `[TrippingBracket] solid <h> = <tag>`).

`TrippingBracketBuilder.Build(BuildContext ctx) → List<(Solid3d sol, string tag, string key)>`:
1. `b, H` hợp lệ, `Ht > 65`; runs = `ctx.Cls.WebRuns()`; flange = `OfType(Flange)`; supports = Flange + FacePlate.
2. Run có flange: 1 Flange ∥ (|cos| ≥ 0.7) cách đường tâm run ≤ `flangeWidth/2 + 5`, phủ điểm xét.
3. Cắt: với mỗi run có flange KHÁC (|cos| ≤ 0.7): giao 2 đường tâm `ix` trong span cả 2 (±20); CG phủ `ix` (1 part CG chứa ix, cách 2 mút ≥ t_beam) && beam KHÔNG phủ (không part beam chứa ix cách 2 mút ≥ t_cg) && CG có flange tại ix ⇒ cut `s(ix)`.
4. `Segments(cuts)`; ngoài cùng (R6) ⇒ bỏ cả run; mỗi đoạn: HP `CrossesThrough` part thuộc run có `s ∈ (lo, hi)`; mid = trung điểm mút part thật trong đoạn; `doz` tại mid; `PickHp(s, mid, doz·u)`.
5. Đặt: `X` = giao HP×web; `n` = pháp tuyến web; `side = Side(doz·n, FlipDir(key))`; `awayDir = ±hpAxis` cùng dấu `side·n`; `P = X + awayDir·(tWeb/2)/|awayDir·n|`; overhang = `FlangeOverhang(supports, P, −awayDir, belly, {−t/2,t/2})`; `SelectBracketB`; `BuildPlate(P, awayDir, belly, t, reach, Ht, d1, baseY, H, centered:true)`; Layer/Color = part web chứa X.
6. Report mỗi đoạn: chọn / `SKIP <n> HP` / `SKIP ngoài cùng`.
- [ ] **Step 1:** Implement. **Step 2:** `dotnet build MCG_3DPanel.csproj -c Debug` succeeded. **Step 3:** `dotnet test tests/MCG_3DPanel.Tests` PASS. **Step 4:** Commit `feat(tripping-bracket): builder`.

### Task 4: Skill

**Files:** Create `.claude/skills/tripping-bracket/tripping-bracket_SKILL.md` (nguồn dump, R1–R9, ví dụ 7D-16C, cài đặt, bẫy `WebRuns` TOL_GAP, "TẠM — chờ test nhiều panel"); 1 dòng liên kết trong `sniped-end` §5.
- [ ] **Step 1:** Viết. **Step 2:** grep "tripping-bracket" ra 2 file. **Step 3:** Commit `docs(skill): tripping-bracket`.
