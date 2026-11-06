# Naming skew lines Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Collinear skew Web / HP / FB pieces form one Naming line, numbered by the whole line's centre, with the correct flange size and highlight.

**Architecture:** Skew pieces (more than 1° off both axes) get their own grouping path in `StructureNaming.Lines`, seeded on the longest piece and tested by angle and perpendicular distance. Axis-aligned pieces keep today's code path untouched.

**Tech Stack:** C# (.NET Framework plugin), xUnit. `StructureNaming.cs` is pure C#, linked into the test project.

**Spec:** `docs/superpowers/specs/2026-11-06-naming-skew-lines-design.md`

## Global Constraints

- No `using Autodesk.*` in `Naming/StructureNaming.cs`.
- `LINE_TOL` stays 15 mm. New `ANGLE_TOL = 1.0` (degrees).
- Panels with no skew piece must give exactly today's result: every existing `StructureNamingTests` test passes unmodified.
- No D# series. Skew lines stay in T / L / HT / HL; FB stays "BS".
- A skew piece never joins an axis-aligned line, and a skew flange never attaches to an axis-aligned web, or the reverse.
- Comments in Vietnamese, matching the file.

## Review Focus

1. Two collinear pieces near 45° fall on different sides of the dominant-axis test → still one line. (Task 1: `Skew_Near45_StaysOneLine`)
2. A skew piece's midpoint lies within 15 mm of a straight web's X → the two stay separate. (Task 1: `Skew_SameCenterAsStraight_NotMerged`)
3. Two parallel skew lines 190 mm apart, as Web and HP on 7D-17C → two lines. (Task 1: `Cas7D17C_HpIsSeparateLine`)
4. A user Del Names the longest skew piece → the remaining pieces still form one line. (Task 1: `Skew_DeletedLongest_RestStayOneLine`)
5. A user Merges a skew line with a straight line → no crash, flanges of both parts stay, Center is the longest piece's. (Task 2: `Merge_SkewWithStraight_KeepsBothFlanges`)

---

### Task 1: Group and number skew lines

**Files:**
- Modify: `Naming/StructureNaming.cs` (`NamingPiece`, `Lines`, `Make`, new helper)
- Test: `tests/MCG_3DPanel.Tests/StructureNamingTests.cs`

**Interfaces:**
- Produces, on `NamingPiece` (all `internal`): `double AngleDeg` (0 ≤ a < 180), `bool IsSkew`, `bool Parallel(NamingPiece o)` (angle difference ≤ `ANGLE_TOL`), `double PerpDist(NamingPiece o)` (distance from `o`'s midpoint to this piece's infinite line).
- Produces: `public const double ANGLE_TOL = 1.0` on `StructureNaming`.

- [ ] **Step 1: Write the failing tests**

Add `using System;` and a fixture, coordinates copied from CAS.dump.json block `7D-17C_Structure`:

```csharp
internal static List<NamingPiece> Cas7D17C()
{
    var l = new List<NamingPiece>();
    Action<NamingKind, double, double, double, double, double, string> a =
        (k, x0, y0, x1, y1, t, key) => l.Add(Spec(P(k, x0, y0, x1, y1, t, key)));
    // Web xiên (4 đoạn)
    a(NamingKind.Web, 281.6, -895.7, 502.7, -1329.3, 10, "1ADF9");
    a(NamingKind.Web, 4541.6, -9250.8, 504.5, -1328.4, 6, "1AE23");
    a(NamingKind.Web, 6380.7, -12864.3, 4539.8, -9251.7, 10, "1AE5C");
    a(NamingKind.Web, 6382.5, -12863.4, 10492.1, -20928.1, 6, "1AE5D");
    // HP xiên (3 đoạn)
    a(NamingKind.HP, 83.8, -921.5, 4696.6, -9974.0, 6, "1AE8A");
    a(NamingKind.HP, 4801.0, -10178.9, 5012.3, -10593.5, 6, "1AE8B");
    a(NamingKind.HP, 5116.7, -10798.5, 10407.7, -21181.9, 6, "1AE8C");
    // Web thẳng
    a(NamingKind.Web, 2438.1, -5110.4, 2438.1, -469.0, 6, "1AE47");
    a(NamingKind.Web, 8397.4, -10074.2, 8397.4, -469.0, 6, "1AE07");
    a(NamingKind.Web, 8397.4, -16804.8, 8397.4, -11017.7, 6, "1AE3C");
    return l;
}
```

Tests:

```csharp
[Fact] public void Cas7D17C_SkewWebIsOneLine()
{
    var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center);
    Assert.Equal(new[] { "T1", "T2", "T3" }, Names(l, NamingKind.Web, NamingDir.T));
    var t2 = Line(l, "T2");
    Assert.Equal(new[] { "1ADF9", "1AE23", "1AE5C", "1AE5D" }, t2.Pieces.Select(p => p.Key).OrderBy(k => k).ToArray());
    Assert.Equal(5386.85, t2.Center, 1);   // giữa X 281.6..10492.1
    Assert.Equal(2438.1, Line(l, "T1").Center, 1);
    Assert.Equal(8397.4, Line(l, "T3").Center, 1);
}

[Fact] public void Cas7D17C_HpIsSeparateLine()
{
    var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center);
    Assert.Equal(new[] { "HT1" }, Names(l, NamingKind.HP, NamingDir.T));
    Assert.Equal(3, Line(l, "HT1").Pieces.Count);
    Assert.Equal(5245.75, Line(l, "HT1").Center, 1);
}

[Fact] public void Skew_SameCenterAsStraight_NotMerged()
{
    var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 1000, 0, 1000, 5000), P(NamingKind.Web, 0, 5000, 2000, 1000) }, PanelPosition.Center);
    Assert.Equal(2, l.Count);
}

[Fact] public void Skew_Near45_StaysOneLine()
{
    // Đoạn đầu nghiêng về L, đoạn sau về T, cùng 1 đường trong 1°
    var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 0, 0, 1000, 999), P(NamingKind.Web, 1100, 1099, 2100, 2100) }, PanelPosition.Center);
    Assert.Single(l);
}

[Fact] public void Skew_ParallelFarApart_TwoLines()
{
    var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 0, 0, 1000, 500), P(NamingKind.Web, 0, 100, 1000, 600) }, PanelPosition.Center);
    Assert.Equal(2, l.Count);
}

[Fact] public void Skew_DeletedLongest_RestStayOneLine()
{
    var l = StructureNaming.Lines(Cas7D17C(), PanelPosition.Center, new[] { "1AE5D" });
    var skew = l.Single(x => x.Kind == NamingKind.Web && x.Pieces.Any(p => p.Key == "1AE23"));
    Assert.Equal(3, skew.Pieces.Count);
}

[Fact] public void Skew_NearAxis_KeepsAxisPath()
{
    // Lệch 0.14° như flange thật ⇒ không phải xiên, tính như đoạn thẳng trục
    var l = StructureNaming.Lines(new[] { P(NamingKind.Web, 0, 0, 1000, 2.4), P(NamingKind.Web, 2000, 3, 3000, 5) }, PanelPosition.Center);
    Assert.Single(l);
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/MCG_3DPanel.Tests --filter "FullyQualifiedName~StructureNamingTests"`
Expected: FAIL on `Cas7D17C_SkewWebIsOneLine`, `Cas7D17C_HpIsSeparateLine`, `Skew_Near45_StaysOneLine`, `Skew_DeletedLongest_RestStayOneLine`.

- [ ] **Step 3: Implement**

In `NamingPiece` add the members listed under Interfaces. `IsSkew`: `AngleDeg` is more than `ANGLE_TOL` from 0, 90 and 180.

In `Lines`, the existing `Dir` loop filters on `!p.IsSkew` for Web + HP and for FB. After that loop, group the skew pieces, across both directions so a 45° line cannot split:

```csharp
// Xiên: seed = đoạn dài nhất còn lại; nhập nếu song song + đường tâm lệch ≤ LINE_TOL. Không chuỗi trôi.
private static IEnumerable<List<NamingPiece>> ClusterSkew(IEnumerable<NamingPiece> pieces)
{
    var groups = new List<List<NamingPiece>>();
    foreach (var p in pieces.OrderByDescending(p => p.Length))
    {
        var g = groups.FirstOrDefault(c => c[0].Parallel(p) && c[0].PerpDist(p) <= LINE_TOL);
        if (g != null) g.Add(p); else groups.Add(new List<NamingPiece> { p });
    }
    return groups;
}
```

Call it once for Web + HP and once for FB. For each group: kind rule as the Web + HP branch, `dir` = the group's longest piece's `Dir`, then `Make`.

In `Make`, `Center` is `longest.Center` unless every main piece is skew; then the midpoint of the min and max coordinate over all endpoints of `pieces`, X for T and Y for L.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/MCG_3DPanel.Tests`
Expected: PASS, including every existing test unmodified. `Lines_DiagonalGoesToNearestAxis` still gives `HL1`.

- [ ] **Step 5: Commit**

```bash
git add Naming/StructureNaming.cs tests/MCG_3DPanel.Tests/StructureNamingTests.cs
git commit -m "feat(naming): collinear skew pieces form one line, numbered by the whole line's centre"
```

### Task 2: Flanges on skew webs, docs

**Files:**
- Modify: `Naming/StructureNaming.cs` (`Make`: local `On`, the `lineFlanges` filter)
- Modify: `.claude/skills/structure-naming/structure-naming_SKILL.md`, `SESSION_LOG.md`
- Test: `tests/MCG_3DPanel.Tests/StructureNamingTests.cs`

**Interfaces:**
- Consumes: `NamingPiece.IsSkew`, `Parallel`, `PerpDist`, `Cas7D17C()` from Task 1.
- Produces: `internal bool OverlapsAlong(NamingPiece o)` on `NamingPiece` (projections on this piece's axis overlap).

- [ ] **Step 1: Write the failing tests**

Add a helper appending the fixture's flanges (`Height` 20; the thickness argument is the width):

```csharp
private static List<NamingPiece> Cas7D17CWithFlanges()
{
    var l = Cas7D17C();
    Action<double, double, double, double, double, string> f = (x0, y0, x1, y1, w, key) =>
    { var p = P(NamingKind.Flange, x0, y0, x1, y1, w, key); p.Height = 20; l.Add(p); };
    f(436.5, -1195.0, 2106.7, -4472.7, 150, "1AE46");
    f(2477.5, -5200.4, 4217.9, -8615.7, 150, "1AE49");
    f(5653.7, -11433.2, 5852.5, -11823.4, 150, "1AE4D");
    f(6438.3, -12973.0, 8056.0, -16147.5, 150, "1AE5E");
    f(10156.5, -20269.4, 8472.2, -16964.3, 150, "1AE75");
    f(2239.0, -4405.3, 2609.8, -5133.0, 447, "1AE45");   // tấm nối
    f(8608.5, -16894.9, 8192.2, -16078.1, 456, "1AE60"); // tấm nối
    f(2438.1, -4382.3, 2438.1, -1076.0, 80, "1AE39");
    f(8397.4, -16057.2, 8397.4, -11212.8, 80, "1AE27");
    f(8397.4, -9862.5, 8397.4, -656.0, 80, "1AE33");
    return l;
}
```

Tests:

```csharp
[Fact] public void Cas7D17C_SkewWebSize()
{
    var l = StructureNaming.Lines(Cas7D17CWithFlanges(), PanelPosition.Center);
    Assert.Equal("WEB 374x6.0/FLANGE 150x20.0", Line(l, "T2").Spec);
}

[Fact] public void Cas7D17C_FlangesNotCrossed()
{
    var l = StructureNaming.Lines(Cas7D17CWithFlanges(), PanelPosition.Center);
    Assert.Equal(new[] { "1AE45", "1AE46", "1AE49", "1AE4D", "1AE5E", "1AE60", "1AE75" }, Line(l, "T2").Flanges.Select(f => f.Key).OrderBy(k => k).ToArray());
    Assert.Equal(new[] { "1AE39" }, Line(l, "T1").Flanges.Select(f => f.Key).ToArray());
    Assert.Equal(new[] { "1AE27", "1AE33" }, Line(l, "T3").Flanges.Select(f => f.Key).OrderBy(k => k).ToArray());
}

[Fact] public void Merge_SkewWithStraight_KeepsBothFlanges()
{
    var merges = new[] { new[] { "1AE5D", "1AE47" } };
    var l = StructureNaming.Lines(Cas7D17CWithFlanges(), PanelPosition.Center, null, merges);
    var m = l.Single(x => x.IsMerged);
    Assert.Contains(m.Flanges, f => f.Key == "1AE75");
    Assert.Contains(m.Flanges, f => f.Key == "1AE39");
    Assert.Equal(8437.3, m.Center, 1);   // tuyến trộn xiên + thẳng ⇒ tâm đoạn dài nhất (1AE5D)
}
```

- [ ] **Step 2: Run the tests and confirm they fail**

Run: `dotnet test tests/MCG_3DPanel.Tests --filter "FullyQualifiedName~StructureNamingTests"`
Expected: FAIL on `Cas7D17C_SkewWebSize`, `Cas7D17C_FlangesNotCrossed` and `Merge_SkewWithStraight_KeepsBothFlanges`.

- [ ] **Step 3: Implement**

Rewrite `On(f, w)`: if `f.IsSkew != w.IsSkew` return false. Both axis: today's test plus `f.Dir == w.Dir`. Both skew: `w.Parallel(f) && w.PerpDist(f) <= f.Thickness / 2 + LINE_TOL && w.OverlapsAlong(f)`. Remove the `f.Dir == dir` filter from `lineFlanges`, since `On` now carries it. The size choice stays: flange on the longest piece first, longest flange first.

- [ ] **Step 4: Run all tests**

Run: `dotnet test tests/MCG_3DPanel.Tests`
Expected: PASS.

- [ ] **Step 5: Build the plugin**

Run: `dotnet build -c Debug MCG_3DPanel.csproj`
Expected: 0 errors.

- [ ] **Step 6: Update docs**

In the skill: replace the "Hướng" bullet's diagonal clause with the skew rule (1°, 15 mm, seed, whole-line centre) and add that flanges must be parallel and junction plates follow their parallel line. Add a 2026-11-06 entry to `SESSION_LOG.md`.

- [ ] **Step 7: Commit**

```bash
git add Naming/StructureNaming.cs tests/MCG_3DPanel.Tests/StructureNamingTests.cs .claude/skills/structure-naming/structure-naming_SKILL.md SESSION_LOG.md
git commit -m "feat(naming): flanges on skew webs — parallel, perpendicular distance, no crossing with straight webs"
```
