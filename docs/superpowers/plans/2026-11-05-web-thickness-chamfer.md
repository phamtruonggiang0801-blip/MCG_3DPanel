# Web Thickness-Change Chamfer — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans. Steps use checkbox (`- [ ]`) syntax.

**Goal:** Taper the thicker web 1:4 (L = 4·Δt) on each stepped face at in-line web joints with Δt ≥ 4.

**Architecture:** Pure geometry (`WebThicknessChamferGeometry`, linked into tests) detects joints from 2D centrelines
and thicknesses and returns plan-view wedge polygons. `WebChamferBuilder` (AutoCAD) extrudes each wedge over the web
height and subtracts it from the final thick-web solid, after the GE/GB merge in `Build3DService`.

**Tech Stack:** C# net8.0-windows, AutoCAD .NET, xUnit.

**Spec:** `docs/superpowers/specs/2026-11-05-web-thickness-chamfer-design.md`

## Global Constraints
- MIN_DT = 4, RATIO = 4, END_TOL = 5, FLUSH_TOL = 0.5, PAR_COS = 0.999.
- Scope: PartType Web, ClosedPlate, GirderEnd (a merged GE/GB maps to its web's solid).
- ux-guardrails: a failed boolean gives `ctx.Warn` and the web is kept. One report line per joint.

## Review Focus
1. Two parallel webs side by side (not end-to-end) must not match. Pinned by `Match_SideBySide_Null`.
2. A thick web joined to thin webs at both ends gets two chamfers, at the right ends. Pinned by `Match_UsesNearestEnds`.
3. An oblique (non-axis) joint gives the wedge in the right world position. Pinned by `Wedge_Rotated45`.
4. GE merged into its own web (same t) gives no chamfer, because Δt = 0. Pinned by `Match_SameThickness_Null`.
5. A 2D footprint that already has the taper drawn: the subtract is harmless (the wedge region is empty). Reviewer check.

### Task 1: Pure geometry
**Files:** Create `Services/Builders/WebThicknessChamferGeometry.cs`. Create
`tests/MCG_3DPanel.Tests/WebThicknessChamferTests.cs`. Link the geometry file in the test csproj.

**Interfaces (produces):**
- `public struct WebSeg { double Ax, Ay, Bx, By, T; }` — the centreline ends and the thickness.
- `public sealed class ChamferJoint { int Thick, Thin; double Jx, Jy, Ux, Uy, Nx, Ny, StepPlus, StepMinus, Dt, L; }`
  - `U` is a unit vector from the joint into the thick web.
  - `N = (−Uy, Ux)`.
  - `StepPlus` / `StepMinus` are the steps on the +N / −N faces of the thick web.
- `public static ChamferJoint Match(WebSeg a, WebSeg b)` returns null when the pair is not a joint (R2).
- `public static List<(double X, double Y)> Wedge(ChamferJoint j, int side, double margin)` returns the quad
  `(−m, h−s−k·m)`, `(L, h)`, `(L, h+m)`, `(−m, h+m)` in the local (U, side·N) frame, mapped to world coordinates.
  Here h = t_thick/2, s = the step on that side, k = s/L.

Tests (7D-16C numbers):
- `Match_HorizontalFlushTop`: 26965 (13623.8,993.8)→(13973.8,993.8) t10 with 26964 (2583.8,995.8)→(13623.8,995.8) t6.
  Expect Thick = 0, Dt = 4, L = 16, StepPlus = 0, StepMinus = 4, J = (13623.8, 993.8).
- `Match_VerticalT20T10`: 26976 (2278.8,9268.9)→(2278.8,9668.9) t10 with 269A0 (2273.8,9668.9)→(2273.8,10868.9) t20.
  Expect Thick = 1, L = 40, U = (0,1), N = (−1,0), StepPlus = 10, StepMinus = 0.
- `Match_Centred_StepsHalf`: same centreline, t10 and t6. Expect steps 2 and 2.
- `Match_DtBelow4_Null` (t8 with t6), `Match_SameThickness_Null`, `Match_Perpendicular_Null`, `Match_Gap10_Null`,
  `Match_SideBySide_Null` (two parallel plates whose lengths overlap), `Match_UsesNearestEnds`.
- `Wedge_FlushTop_Points`: for the first case, side −1, m = 1. The slope passes through (13623.8, 992.8) at the joint
  and (13639.8, 988.8) at L.
- `Wedge_Rotated45`: the same joint rotated 45°. The points rotate the same way.

### Task 2: Builder + wiring
**Files:** Create `Services/Builders/WebChamferBuilder.cs`. Modify `Services/Build3DService.cs`, after the GE merge
loop.
- `internal static int Apply(BuildContext ctx, List<(PanelPart part, Solid3d sol)> webs)`.
  - `WebSeg` comes from `ClassifyUtil.Endpoints` + `FootprintThickness`.
  - Check every pair with `Match`. Dedupe by (thick SourceKey, joint point).
  - For each side with step > FLUSH_TOL: build the wedge `Polyline` at Z = −WebHeight − 10, then
    `Region` → `CreateExtrudedSolid` (+Z, WebHeight + 20) → `BoolSubtract` from the thick solid.
  - Report line: `[WebChamfer] {thick h} t{tK} ↔ {thin h} t{tN}: Δt={Dt} L={L} sides={+|−|±}`.
  - Wrap each joint in try/catch with `ctx.Warn`.
- In Build3DService, collect Web, ClosedPlate and GirderEnd parts from `builtMap`. A GirderEnd with `JoinWithWeb`
  that was merged uses `builtMap[JoinWithWeb].sol`. Refresh `FillGeometry` for changed webs.
- Verify with `dotnet build MCG_3DPanel.csproj -c Debug` and `dotnet test tests/MCG_3DPanel.Tests`.
- Docs: a new skill `.claude/skills/web-thickness-chamfer`, a memory file, and the 🔍 rows of the ux-checklist.
