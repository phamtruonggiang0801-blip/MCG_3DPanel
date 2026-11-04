using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dựng BRACKET knee (OB/OB1 · IB/IB1 · FB/FB1): sinh outline elevation từ
    /// <see cref="IProfileService"/> → extrude chiều dày → gấp lip (bend) → đặt vào mặt đứng.
    ///
    /// Khác biệt giữa 3 loại CHỈ ở <c>Dtop</c> + <c>ledge</c> (xem <see cref="KneeDtop"/>);
    /// toe/bend dùng chung. Xem skill bracket-identification (mục 4b) + parametric-profiles.
    /// </summary>
    public class BracketBuilder : IPartBuilder
    {
        private const string LOG = "[BracketBuilder]";

        public string Name => "Bracket";

        /// <summary>Chỉ nhận bracket ĐÃ có mã knee; bracket không rõ mã để PlateBuilder dựng hộp.</summary>
        public bool CanBuild(PanelPart part)
        {
            if (part.Type != PartType.Bracket || part.Code == null) return false;
            return part.Code.StartsWith("OB") || part.Code.StartsWith("IB") || part.Code.StartsWith("BF");
        }

        /// <summary>
        /// Chiều cao mặt đầu span (Dtop) + ledge theo LOẠI bracket:
        ///  - OB/OB1: Dtop = HP height − 15, ledge 0.
        ///  - IB/IB1: Dtop = HP height − 30, ledge 15 (đoạn ngang đầu span trước R30, bọc bulb).
        ///  - BF/BF1: Dtop = FlatBar height − 15, ledge 0 (mã "FB" cũ, xem BracketCoder).
        /// </summary>
        public static void KneeDtop(string code, PanelInput input, out double dtop, out double ledge)
        {
            code = code ?? "";
            if (code.StartsWith("BF")) { dtop = input.FlatBarHeight - 15.0; ledge = 0.0; }
            else if (code.StartsWith("IB")) { dtop = input.HpHeight - 30.0; ledge = 15.0; }
            else { dtop = input.HpHeight - 15.0; ledge = 0.0; }   // OB (mặc định)
        }

        /// <summary>Các tham số suy ra của 1 bracket (dùng chung cho dựng hình + report).</summary>
        public struct KneeParams
        {
            public double S, Huse, F, Thk, Dtop, Ledge, Overhang, FreeEdge;
            public bool Under50, Flanged, SquareCorner;
        }

        public static KneeParams Solve(PanelPart part, ClassificationResult cls, PanelInput input)
        {
            var k = new KneeParams();
            k.S = part.MemberLength;
            k.Thk = part.FootprintThickness;

            // leg-on-Flange: overhang = flange vươn xa theo hướng bracket (xử lý cả flange LỆCH);
            // F = overhang − 15; overhang<50 → under50 (F=15, nâng đáy cách flange 30).
            k.Overhang = OverhangProj(part, cls);
            k.Under50 = k.Overhang < 50.0;
            // F = overhang − 15; KẸP ≤ S − R30 (FacePlate loe vượt qua toe-HP → chân đứng cách toe-HP R30).
            // Vượt trần (Flange/FacePlate đỡ dài hơn cả bracket) → squareCorner: bỏ R30 dưới, dựng thẳng
            // đứng góc vuông 90° (xem ProfileService.R30Only, giống squareCorner của GE — skill girder-end 6c).
            double rawF = k.Overhang - 15.0;
            k.SquareCorner = !k.Under50 && rawF > k.S - 30.0;
            k.F = k.Under50 ? 15.0 : Math.Min(rawF, k.S - 30.0);
            k.Huse = k.Under50 ? input.WebHeight - 30.0 : input.WebHeight;

            KneeDtop(part.Code, input, out double dtop, out double ledge);
            k.Dtop = dtop; k.Ledge = ledge;

            // free-edge (toe-flange→toe-HP); flanged → có gấp lip (thk6 ≥350 / thk10 ≥600).
            // squareCorner → free-edge chỉ còn đoạn thẳng đứng ngắn (không phải cạnh chéo dài) → không gấp lip.
            k.FreeEdge = Math.Sqrt((k.S - k.F) * (k.S - k.F) + (k.Huse - k.Dtop) * (k.Huse - k.Dtop));
            k.Flanged = !k.SquareCorner && k.FreeEdge >= (k.Thk >= 8.0 ? 600.0 : 350.0);
            return k;
        }

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            try
            {
                var k = Solve(part, ctx.Cls, ctx.Input);
                if (k.S < 1e-6 || k.Huse < 1e-6 || k.Thk < 1e-6) return null;

                var outline = ctx.Profile.BracketOutline(
                    k.Under50, k.S, k.Huse, k.F, k.Thk, k.Dtop, k.Ledge, k.Flanged, k.SquareCorner);

                Region reg = GeometryUtil.RegionFrom(outline);
                if (reg == null) return null;

                var sol = new Solid3d();
                var sob = new SweepOptionsBuilder();
                sol.CreateExtrudedSolid(reg, new Vector3d(0, 0, k.Thk), sob.ToSweepOptions());
                reg.Dispose();

                // BEND: gấp lip khi flanged. Hướng gấp = về CHIỀU ĐỔ (theo panel type).
                int foldSign = BendFoldSign(part, ctx.Cls, ctx.Position);
                string bendStatus = TryAddBendLip(sol, outline, k.Thk, k.FreeEdge, foldSign);

                sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -k.Thk / 2.0))); // căn giữa dày
                sol.TransformBy(BracketPlacement(part, ctx.Cls));                          // đặt vào mặt đứng

                ctx.Report.Add(
                    $"Bracket code={part.Code} S={k.S:0} thk={k.Thk:0} Dtop={k.Dtop:0} ledge={k.Ledge:0} " +
                    $"overhang={k.Overhang:0} under50={k.Under50} F={k.F:0} Huse={k.Huse:0} flanged={k.Flanged} " +
                    $"squareCorner={k.SquareCorner} center=({part.Centroid.X:0},{part.Centroid.Y:0}) | " +
                    $"{bendStatus} lumps={GeometryUtil.CountLumps(sol)}");
                ctx.Report.Add("   OUTLINE " + DescribeOutline(outline));
                return sol;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG} lỗi ({part.Id.Handle}): {ex.Message}");
                ctx.Report.Add($"Bracket code={part.Code} buildError: {ex.Message}");
                return null;
            }
        }

        // ─── Gấp lip (bend) ───────────────────────────────────────────────

        /// <summary>
        /// Gấp mép (bend lip) khi cạnh chéo tự do >= ngưỡng (thk6 ≥350 / thk10 ≥600).
        /// Lip rộng 70/100, gấp vuông góc tấm, union vào tấm. Xem parametric-profiles mục 5b.
        /// </summary>
        public static string TryAddBendLip(Solid3d plate, List<VertexBulge> outline, double thk, double freeEdgeLen, int foldSign)
        {
            double bendMin = thk >= 8.0 ? 600.0 : 350.0;
            if (freeEdgeLen < bendMin) return $"noBend(fe={freeEdgeLen:0}<{bendMin:0})";
            double lipW = thk >= 8.0 ? 100.0 : 70.0;
            double Rb = 2.0 * thk;         // bán kính bending TRONG; NGOÀI = Rb + thk

            // Cạnh CHÉO (nghiêng nhất) làm trục extrude lip.
            int n = outline.Count;
            double bestDiag = -1; int di = 0;
            for (int i = 0; i < n; i++)
            {
                Point2d p0 = outline[i].Pt, q0 = outline[(i + 1) % n].Pt;
                double slant = Math.Min(Math.Abs(q0.X - p0.X), Math.Abs(q0.Y - p0.Y));
                if (slant > bestDiag) { bestDiag = slant; di = i; }
            }
            Point2d A2 = outline[di].Pt, B2 = outline[(di + 1) % n].Pt;
            Vector2d dv = B2 - A2; double diagLen = dv.Length;
            if (diagLen < 1e-6) return "noDiag";
            Vector2d dd = dv / diagLen;
            Vector3d dDir = new Vector3d(dd.X, dd.Y, 0);
            Vector3d nDir = new Vector3d(-dd.Y, dd.X, 0);
            Point3d A = new Point3d(A2.X, A2.Y, 0);

            // nDir phải hướng RA NGOÀI (xa tâm outline) để chân fillet chồng vào tấm → union liền khối.
            double cxo = 0, cyo = 0;
            foreach (var v in outline) { cxo += v.Pt.X; cyo += v.Pt.Y; }
            cxo /= outline.Count; cyo /= outline.Count;
            double mx = (A2.X + B2.X) / 2 - cxo, my = (A2.Y + B2.Y) / 2 - cyo;
            if (nDir.X * mx + nDir.Y * my < 0) nDir = nDir.Negate();

            // Tiết diện kim loại gấp (x=n, y=z): fillet trong Rb / ngoài Rb+thk, dày đều thk.
            // DỊCH +sh để chỉ CHẠM mép (chồng ~ov) → union dính, không double-thickness.
            // Gấp XUỐNG (foldSign<0): phản chiếu y→(thk−y) để tiếp tuyến đúng mặt dưới.
            double ov = 0.5, sh = Rb - ov;
            var csPts = new[]
            {
                new[] { -Rb + sh, thk,             0.41421356 },
                new[] { sh,       thk + Rb,        0.0        },
                new[] { sh,       thk + Rb + lipW, 0.0        },
                new[] { thk + sh, thk + Rb + lipW, 0.0        },
                new[] { thk + sh, thk + Rb,       -0.41421356 },
                new[] { -Rb + sh, 0,               0.0        },
            };
            var cs = new Polyline();
            for (int i = 0; i < csPts.Length; i++)
            {
                double yy = csPts[i][1], bb = csPts[i][2];
                if (foldSign < 0) { yy = thk - yy; bb = -bb; }
                cs.AddVertexAt(i, new Point2d(csPts[i][0], yy), bb, 0, 0);
            }
            cs.Closed = true;

            DBObjectCollection regs;
            try { regs = Region.CreateFromCurves(new DBObjectCollection { cs }); }
            catch { cs.Dispose(); return "regionFail"; }
            cs.Dispose();
            if (regs.Count == 0) return "regionEmpty";
            var reg = (Region)regs[0];
            for (int i = 1; i < regs.Count; i++) ((Region)regs[i]).Dispose();

            reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                A, nDir, Vector3d.ZAxis, dDir));

            var lip = new Solid3d();
            var sob = new SweepOptionsBuilder();
            try { lip.CreateExtrudedSolid(reg, dDir * diagLen, sob.ToSweepOptions()); }
            catch { reg.Dispose(); return "extrudeFail"; }
            reg.Dispose();

            bool threw = false;
            try { plate.BooleanOperation(BooleanOperationType.BoolUnite, lip); }
            catch { threw = true; }
            finally { lip.Dispose(); }

            // Sniped End ở 2 đầu lip theo chiều dài (dọc free-edge) — xác nhận kỹ sư 2026-08-26 (thay
            // cho chamfer 15×15 đơn giản ban đầu): tái dùng ĐÚNG công thức Sniped End của FlatBar (xem
            // FlatBarBuilder/skill sniped-end), coi lipW như "chiều cao" (h_fb) và mép NGOÀI lip (xa tấm
            // nhất) như "cạnh tự do". CHỈ có ý nghĩa nếu union lip vào tấm thành công.
            if (!threw) SnipLipEnds(plate, A, dDir, nDir, diagLen, thk, Rb, lipW, sh, foldSign);

            return $"bend(fe={freeEdgeLen:0} lipW={lipW:0} Rb={Rb:0} diag={diagLen:0} unionThrew={threw})";
        }

        // Sniped End — CÙNG hằng số với FlatBarBuilder (FLAT_TOP=15mm cố định, góc 30°/60° cố định).
        // Không dùng chung 1 nơi vì mỗi builder tự chứa (xem skill module-per-object) — đổi 1 trong 2
        // chỗ nhớ đổi luôn chỗ kia nếu công thức Sniped End đổi.
        private const double LIP_SNIP_FLAT = 15.0;
        private static readonly double LIP_SNIP_RUN_FACTOR = Math.Tan(60.0 * Math.PI / 180.0);

        /// <summary>
        /// Sniped End ở 2 đầu dải lip (theo chiều dài, dọc free-edge — tại length=0 gần <paramref name="a"/>
        /// và length=<paramref name="diagLen"/> ở đầu kia): mép NGOÀI lip (xa tấm nhất, "cạnh tự do" —
        /// y=thk+Rb+lipW trong tiết diện <see cref="TryAddBendLip"/>, mirror nếu <paramref name="foldSign"/>&lt;0)
        /// bị vát dần về gốc lip (chỗ nối bend, "cạnh hàn" — giữ nguyên) khi tới gần mỗi đầu, chỉ còn lại
        /// đúng <see cref="LIP_SNIP_FLAT"/>=15mm ngay sát mặt đầu — CÙNG công thức/góc với Sniped End của
        /// FlatBar (rise=lipW−15, run=rise×tan60°), coi <paramref name="lipW"/> như "chiều cao" (h_fb).
        /// Trừ (BoolSubtract) 2 nêm tam giác (quét hết bề rộng tiết diện lip theo <paramref name="nDir"/>,
        /// margin nhỏ để cắt xuyên sạch, không trùng mặt) khỏi <paramref name="plate"/>.
        /// ⚠️ CHƯA test trực quan trong AutoCAD.
        /// </summary>
        private static void SnipLipEnds(Solid3d plate, Point3d a, Vector3d dDir, Vector3d nDir,
            double diagLen, double thk, double Rb, double lipW, double sh, int foldSign)
        {
            const double MARGIN = 1.0;
            double rise = Math.Max(0.0, lipW - LIP_SNIP_FLAT);
            double run = rise * LIP_SNIP_RUN_FACTOR;
            if (2.0 * run > diagLen) run = diagLen / 2.0;   // lip quá ngắn — kẹp an toàn (giống FlatBarBuilder)

            double vBase = foldSign > 0 ? (thk + Rb) : -Rb;                  // gốc lip (nối bend) — KHÔNG đổi
            double vTip = foldSign > 0 ? (thk + Rb + lipW) : -(Rb + lipW);   // mép ngoài lip đầy đủ (xem csPts)
            double vFlat = vBase + foldSign * LIP_SNIP_FLAT;                 // mép ngoài CÒN LẠI ngay sát đầu (15mm)
            double uLo = -Rb + sh - MARGIN, uHi = thk + sh + MARGIN;         // quét hết bề rộng tiết diện + margin

            void CutAt(Point3d anchor, double dirSign)
            {
                if (run < 1e-6) return;
                var tri = new Polyline();
                tri.AddVertexAt(0, new Point2d(-dirSign * MARGIN, vTip + foldSign * MARGIN), 0, 0, 0);
                tri.AddVertexAt(1, new Point2d(dirSign * run, vTip + foldSign * MARGIN), 0, 0, 0);
                tri.AddVertexAt(2, new Point2d(-dirSign * MARGIN, vFlat), 0, 0, 0);
                tri.Closed = true;

                DBObjectCollection regs;
                try { regs = Region.CreateFromCurves(new DBObjectCollection { tri }); }
                catch { tri.Dispose(); return; }
                tri.Dispose();
                if (regs.Count == 0) return;
                var reg = (Region)regs[0];
                for (int i = 1; i < regs.Count; i++) ((Region)regs[i]).Dispose();

                reg.TransformBy(Matrix3d.AlignCoordinateSystem(
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    anchor + nDir * uLo, dDir, Vector3d.ZAxis, nDir));

                var cutter = new Solid3d();
                var sob = new SweepOptionsBuilder();
                try { cutter.CreateExtrudedSolid(reg, nDir * (uHi - uLo), sob.ToSweepOptions()); }
                catch { reg.Dispose(); return; }
                reg.Dispose();

                try { plate.BooleanOperation(BooleanOperationType.BoolSubtract, cutter); }
                catch { cutter.Dispose(); }
            }

            CutAt(a, 1.0);                      // đầu A (length=0), vát về phía +dDir (vào trong lip)
            CutAt(a + dDir * diagLen, -1.0);     // đầu B (length=diagLen), vát về phía −dDir
        }

        /// <summary>
        /// Dấu hướng gấp lip = về phía CHIỀU ĐỔ kết cấu theo panel type.
        /// Trục X: LUÔN trái→phải (+X). Trục Y: Center → về GIỮA · Port → −Y · Starboard → +Y.
        /// </summary>
        public static int BendFoldSign(PanelPart part, ClassificationResult cls, PanelPosition position)
        {
            AnchorDir(part, cls, out _, out Vector3d memberDir, out _);
            Vector3d normal = memberDir.CrossProduct(Vector3d.ZAxis);
            if (normal.Length < 1e-9) return 1;
            normal = normal.GetNormal();

            double dozY;
            switch (position)
            {
                case PanelPosition.Port: dozY = -1.0; break;
                case PanelPosition.Starboard: dozY = 1.0; break;
                default:
                    double cy = cls.TopPlate != null ? cls.TopPlate.Centroid.Y : 0.0;
                    dozY = (cy - part.Centroid.Y) >= 0 ? 1.0 : -1.0;
                    break;
            }
            return normal.DotProduct(new Vector3d(1.0, dozY, 0)) >= 0 ? 1 : -1;
        }

        // ─── Đặt vào mặt đứng ─────────────────────────────────────────────

        /// <summary>Ma trận đặt outline local (gốc=Web∩TopPlate, +X dọc member, −Y xuống) vào WCS.</summary>
        public static Matrix3d BracketPlacement(PanelPart part, ClassificationResult cls)
        {
            AnchorDir(part, cls, out Point3d anchor, out Vector3d memberDir, out _);
            Vector3d normal = memberDir.CrossProduct(Vector3d.ZAxis);
            if (normal.Length < 1e-9) normal = Vector3d.YAxis;
            return Matrix3d.AlignCoordinateSystem(
                Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                anchor, memberDir, Vector3d.ZAxis, normal.GetNormal());
        }

        /// <summary>
        /// Anchor = đầu CHẠM WEB dài; free = đầu kia (ra HP/mép). Robust theo web thật,
        /// không dùng heuristic "gần tâm". Fallback (không có web): đầu gần tâm panel.
        /// </summary>
        public static void AnchorDir(PanelPart part, ClassificationResult cls, out Point3d anchor, out Vector3d memberDir, out string via)
        {
            // ⚠️ Lấy đầu mút từ ĐỈNH THẬT (Vertices — ClassifyUtil.Endpoints), KHÔNG dùng
            // part.Bounds + part.IsHorizontal: IsHorizontal chỉ là nhị phân ngang/dọc suy từ bbox
            // (dx≥dy?), ép MỌI bracket về đúng trục X hoặc Y dù hình học thật CHÉO (VD bracket vuông
            // góc với 1 HP chéo — bbox rộng hơn cao (163×87) nên bị coi "ngang", trong khi trục thật
            // lệch ~27° so với phương ngang). Ép về trục sai làm hướng đặt 3D (memberDir) sai theo,
            // biểu hiện: bracket "xoay lại" theo Web/trục gần đó thay vì giữ đúng góc vuông-HP như bản
            // vẽ 2D gốc. Kiểm chứng thật: `393B9` — kỹ sư xác nhận 2D vuông góc HP, 3D lại vuông góc Web.
            (Point3d e1, Point3d e2) = ClassifyUtil.Endpoints(part);

            double w1 = GeometryUtil.MinDistToWeb(e1, cls), w2 = GeometryUtil.MinDistToWeb(e2, cls);
            Point3d free;
            if (w1 < double.MaxValue || w2 < double.MaxValue)
            {
                via = "web-end";
                if (w1 <= w2) { anchor = e1; free = e2; } else { anchor = e2; free = e1; }
            }
            else
            {
                via = "center";
                Point3d ctr = cls.TopPlate != null ? cls.TopPlate.Centroid : Point3d.Origin;
                if (GeometryUtil.Dist2d(e1, ctr) <= GeometryUtil.Dist2d(e2, ctr)) { anchor = e1; free = e2; }
                else { anchor = e2; free = e1; }
            }
            memberDir = (free - anchor).GetNormal();
        }

        // ─── Quan hệ với Flange ───────────────────────────────────────────

        /// <summary>Chiều rộng flange gần bracket nhất (fallback 120).</summary>
        public static double NearestFlangeWidth(PanelPart part, ClassificationResult cls)
        {
            double best = double.MaxValue, width = 120.0;
            foreach (var f in cls.OfType(PartType.Flange))
            {
                double d = GeometryUtil.DistToBox(part.Centroid, f.Bounds);
                if (d < best) { best = d; width = f.FootprintThickness; }
            }
            return width;
        }

        /// <summary>
        /// Tấm đỡ (Flange girder) mà <paramref name="part"/> đứng lên — gần điểm neo nhất (2D, tới bbox).
        /// Ưu tiên Flange; không có thì FacePlate; không có cả hai → null. Dùng để flange GE/GB mượn Layer/Color.
        /// </summary>
        public static PanelPart SupportPlate(PanelPart part, ClassificationResult cls)
        {
            AnchorDir(part, cls, out Point3d anchor, out _, out _);
            return NearestTo(anchor, cls.OfType(PartType.Flange)) ?? NearestTo(anchor, cls.OfType(PartType.FacePlate));
        }

        private static PanelPart NearestTo(Point3d p, IEnumerable<PanelPart> plates)
        {
            PanelPart best = null; double bestD = double.MaxValue;
            foreach (var s in plates)
            {
                Extents3d b = s.Bounds;
                double dx = Math.Max(0, Math.Max(b.MinPoint.X - p.X, p.X - b.MaxPoint.X));
                double dy = Math.Max(0, Math.Max(b.MinPoint.Y - p.Y, p.Y - b.MaxPoint.Y));
                double d = dx * dx + dy * dy;
                if (d < bestD) { bestD = d; best = s; }
            }
            return best;
        }

        /// <summary>
        /// Overhang = TẤM ĐỠ (Flange HOẶC Face Plate) vươn xa nhất theo memberDir tính từ anchor — ray-cast
        /// qua hợp các tấm đỡ liền nhau (<see cref="RayUnionHit"/>). Fallback: bbox tấm gần nhất / đối xứng.
        /// Đo tại CẢ 2 MẶT bản thép (±thk/2 quanh đường tâm — bracket/GE dựng căn giữa) + đường tâm, lấy MIN
        /// (<see cref="FaceMinHit"/>): flange có mép XIÊN so với trục member thì mép tại 1 mặt gần hơn tại đường
        /// tâm ~ (thk/2)/tan(góc) — đo chỉ đường tâm làm toe không lùi đủ 15 (bracket B 20E72, 2026-11-04).
        /// </summary>
        public static double OverhangProj(PanelPart part, ClassificationResult cls)
        {
            AnchorDir(part, cls, out Point3d anchor, out Vector3d dir, out _);
            var lateral = new Vector3d(-dir.Y, dir.X, 0);                       // ⟂ trục member, trong mặt bằng
            double half = part.FootprintThickness / 2.0;

            // Tấm đỡ gần nhất: xét cả Flange lẫn FacePlate.
            // Gom TẤT CẢ Flange+FacePlate (không chỉ 1 tấm gần nhất): nhiều tấm kề nhau (chung mép,
            // VD FacePlate trong ghép FacePlate ngoài) tạo thành 1 "sàn" liên tục — overhang phải đi
            // HẾT sàn liên tục đó, không dừng giữa chừng ở đường nối giữa 2 tấm. Kiểm chứng thật: GE
            // `38E42` neo trong `38EBD` (tấm trong), liền `393C2` (tấm ngoài) đúng tại ranh giới
            // Y=37731.8 — nếu chỉ ray-cast 1 tấm gần nhất, dừng NGAY tại ranh giới đó (tụt vào tấm
            // trong) thay vì đi tiếp ra mép ngoài thật của `393C2`.
            var supports = new List<PanelPart>(cls.OfType(PartType.Flange));
            supports.AddRange(cls.OfType(PartType.FacePlate));

            if (supports.Count == 0)
                return (NearestFlangeWidth(part, cls) - part.FootprintThickness) / 2.0;

            double union = FaceMinHit(anchor, dir, lateral, new[] { -half, 0.0, half }, supports);
            if (union > 0) return union;

            // Fallback bbox tấm gần nhất — chỉ khi không tấm nào ray cắt được (hiếm, sai số biên).
            PanelPart nearest = null; double best = double.MaxValue;
            foreach (var s in supports)
            { double d = GeometryUtil.DistToBox(part.Centroid, s.Bounds); if (d < best) { best = d; nearest = s; } }
            if (nearest == null) return (NearestFlangeWidth(part, cls) - part.FootprintThickness) / 2.0;

            Extents3d b = nearest.Bounds;
            double oh = double.MinValue;
            var corners = new[]
            {
                new Point3d(b.MinPoint.X, b.MinPoint.Y, 0), new Point3d(b.MaxPoint.X, b.MinPoint.Y, 0),
                new Point3d(b.MinPoint.X, b.MaxPoint.Y, 0), new Point3d(b.MaxPoint.X, b.MaxPoint.Y, 0),
            };
            foreach (var c in corners)
            {
                double pr = (c.X - anchor.X) * dir.X + (c.Y - anchor.Y) * dir.Y;
                if (pr > oh) oh = pr;
            }
            return oh;
        }

        /// <summary>
        /// Ray từ <paramref name="o"/> theo <paramref name="dir"/> đi qua HỢP (union) của MỌI tấm đỡ
        /// trong <paramref name="supports"/> — gộp các khoảng [vào,ra] của từng tấm (kể cả tấm KHÔNG
        /// LỒI, có thể vào/ra nhiều lần), nối các khoảng CHỒNG/KỀ NHAU (dung sai nhỏ), trả về mép NGOÀI
        /// của khoảng liên tục chứa gốc tia (t≈0). 0 nếu gốc không nằm trong tấm đỡ nào.
        /// </summary>
        /// <summary>
        /// <see cref="RayUnionHit"/> tại nhiều tia SONG SONG (gốc <paramref name="o"/> + <paramref name="lateral"/>·offset,
        /// thường = 2 mặt bản thép) → overhang NHỎ NHẤT ⇒ mọi điểm trên toe đều lùi đủ so với mép tấm đỡ.
        /// Tia trả 0 (gốc rơi ngoài tấm đỡ — sát đầu tấm) bị bỏ qua; mọi tia 0 → 0.
        /// </summary>
        internal static double FaceMinHit(Point3d o, Vector3d dir, Vector3d lateral, double[] offsets, List<PanelPart> supports)
        {
            if (lateral.Length > 1e-9) lateral = lateral.GetNormal();
            double best = double.MaxValue;
            foreach (double off in offsets)
            {
                double h = RayUnionHit(o + lateral * off, dir, supports);
                if (h > 1e-6 && h < best) best = h;
            }
            return best < double.MaxValue ? best : 0.0;
        }

        internal static double RayUnionHit(Point3d o, Vector3d dir, List<PanelPart> supports)
        {
            const double GAP_TOL = 5.0;   // 2 tấm cách nhau ≤ GAP_TOL coi như liền (sai số vẽ/đo).

            // ⚠️ Ghép cặp chẵn/lẻ PHẢI làm RIÊNG từng tấm (mỗi đa giác kín tự có số giao điểm chẵn,
            // và cặp (t[2i],t[2i+1]) CỦA CHÍNH NÓ mới đúng là khoảng vào/ra) — KHÔNG được trộn t của
            // nhiều tấm khác nhau vào 1 danh sách rồi ghép cặp chung: nếu ray đi xuyên qua 1 tấm đỡ
            // KHÔNG liên quan (xa, không chạm tấm neo) nằm xen giữa, các mốc t xen kẽ làm object ghép
            // cặp sai tấm, tạo "khoảng liên tục" ẢO nối tới tấm ở rất xa. Lỗi này đã xảy ra thật: nhiều
            // OB bracket (S=204, overhang đúng ~79mm khi chỉ xét 1 tấm) bị tính overhang tới 1198-1523mm
            // sau khi đổi sang trộn-chung — vì ray xuyên qua nhiều Flange RỜI RẠC nằm dọc theo hướng dir.
            var intervals = new List<(double lo, double hi)>();
            foreach (var s in supports)
            {
                if (s.Vertices == null || s.Vertices.Length < 3) continue;
                var ts = new List<double>();
                CollectCrossings(o, dir, s.Vertices, ts);
                if (ts.Count < 2) continue;
                ts.Sort();
                for (int i = 0; i + 1 < ts.Count; i += 2)
                    intervals.Add((ts[i], ts[i + 1]));
            }
            if (intervals.Count == 0) return 0;
            intervals.Sort((a, b) => a.lo.CompareTo(b.lo));

            // ⚠️ Khi chuyển nhóm, CHỈ được return nếu nhóm SẮP BỎ thật sự CHỨA gốc (curLo≤GAP_TOL VÀ
            // curHi≥−GAP_TOL) — thiếu vế `curHi≥−GAP_TOL` là bug thật đã xảy ra: 1 nhóm nằm HOÀN TOÀN
            // phía sau gốc (cả lo VÀ hi đều âm rất xa, VD tấm đỡ khác ở tận phía ngược hướng `dir`) vẫn
            // có `curLo≤GAP_TOL` (số âm luôn ≤ 5) nên bị return SỚM (0, vì hi<0) — bỏ qua mọi nhóm phía
            // sau còn lại, kể cả nhóm ĐÚNG chứa gốc. Kiểm chứng thật: bracket `3939C` (S=204) — tấm đỡ
            // thật `38ED1` (Flange chéo) cho khoảng (−89.4, 79.4) CHỨA gốc, nhưng 4 Flange thẳng khác ở
            // xa hơn theo chiều ngược `dir` (`99129/100764/102399/104035`, mọi khoảng đều âm) đứng
            // TRƯỚC nó trong danh sách sort — nhóm xa nhất bị return 0 oan, `OverhangProj` rơi về
            // fallback bbox (lấy góc bbox AXIS-ALIGNED của `38ED1`, tấm CHÉO) → overhang ảo **1163mm**
            // thay vì đúng 79.4mm. Sửa: nhóm không chứa gốc (dù lo≤GAP_TOL hay không) chỉ đơn thuần bị
            // BỎ QUA, tiếp tục xét nhóm sau — không return.
            double curLo = double.NaN, curHi = double.NaN;
            foreach (var iv in intervals)
            {
                if (double.IsNaN(curLo)) { curLo = iv.lo; curHi = iv.hi; continue; }
                if (iv.lo <= curHi + GAP_TOL) { curHi = Math.Max(curHi, iv.hi); }
                else
                {
                    if (curLo <= GAP_TOL && curHi >= -GAP_TOL) return curHi > 0 ? curHi : 0;
                    curLo = iv.lo; curHi = iv.hi;
                }
            }
            if (!double.IsNaN(curLo) && curLo <= GAP_TOL && curHi >= -GAP_TOL) return curHi > 0 ? curHi : 0;
            return 0;
        }

        /// <summary>Thêm mọi t (dọc theo <paramref name="dir"/> từ <paramref name="o"/>) nơi ray cắt
        /// biên polygon <paramref name="v"/> vào <paramref name="ts"/> (không lọc dấu — âm/dương đều
        /// giữ, để <see cref="RayUnionHit"/> tự xác định khoảng chứa gốc t≈0).</summary>
        private static void CollectCrossings(Point3d o, Vector3d dir, Point3d[] v, List<double> ts)
        {
            double px = -dir.Y, py = dir.X;
            int n = v.Length;
            for (int i = 0; i < n; i++)
            {
                Point3d a = v[i], b = v[(i + 1) % n];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double denom = ex * px + ey * py;
                if (Math.Abs(denom) < 1e-9) continue;
                double s = ((o.X - a.X) * px + (o.Y - a.Y) * py) / denom;
                if (s < -1e-6 || s > 1 + 1e-6) continue;
                double hx = a.X + s * ex, hy = a.Y + s * ey;
                double t = (hx - o.X) * dir.X + (hy - o.Y) * dir.Y;
                ts.Add(t);
            }
        }

        // ─── Chẩn đoán ────────────────────────────────────────────────────

        /// <summary>Mô tả outline (đỉnh + bán kính cung + chiều bo) để đối chiếu bản vẽ.</summary>
        public static string DescribeOutline(List<VertexBulge> o)
        {
            int n = o.Count;
            var sb = new StringBuilder();
            sb.Append($"n={n} verts[");
            for (int i = 0; i < n; i++)
                sb.Append($"({o[i].Pt.X:0.#},{o[i].Pt.Y:0.#}){(i < n - 1 ? " " : "")}");
            sb.Append("]");

            // Chỉ outline R30-only (7 đỉnh) mới có mốc cố định để so góc nhọn.
            if (n == 7)
            {
                Point2d fT = o[3].Pt, fB = o[4].Pt;
                double S = o[1].Pt.X, negH = o[6].Pt.Y, F = o[5].Pt.X;
                double dx = fB.X - fT.X, dy = fB.Y - fT.Y;
                double Rt = GeometryUtil.ArcRadius(o[2].Pt, o[3].Pt, o[2].Bulge);
                double Rb = GeometryUtil.ArcRadius(o[4].Pt, o[5].Pt, o[4].Bulge);
                double sharpBotX = Math.Abs(dy) > 1e-9 ? fT.X + dx * (negH - fT.Y) / dy : F;
                double sharpTopY = Math.Abs(dx) > 1e-9 ? fT.Y + dy * (S - fT.X) / dx : fT.Y;
                string botDir = fB.X > sharpBotX + 0.5 ? "OUT(phình ra)" : (fB.X < sharpBotX - 0.5 ? "IN(cắt vào)" : "≈");
                string topDir = fT.Y < sharpTopY - 0.5 ? "OUT(phình xuống)" : (fT.Y > sharpTopY + 0.5 ? "IN(cắt vào)" : "≈");
                sb.Append($" | freeEdge {fT.X:0.#},{fT.Y:0.#}→{fB.X:0.#},{fB.Y:0.#}");
                sb.Append($" | TOE-bot R={Rb:0.#} F={F:0.#} freeTan.x={fB.X:0.#} sharp.x={sharpBotX:0.#} {botDir}");
                sb.Append($" | TOE-top R={Rt:0.#} S={S:0.#} freeTan.y={fT.Y:0.#} sharp.y={sharpTopY:0.#} {topDir}");
            }
            return sb.ToString();
        }
    }
}
