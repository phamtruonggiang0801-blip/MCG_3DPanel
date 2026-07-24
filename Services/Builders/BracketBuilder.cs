using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

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
            return part.Code.StartsWith("OB") || part.Code.StartsWith("IB") || part.Code.StartsWith("FB");
        }

        /// <summary>
        /// Chiều cao mặt đầu span (Dtop) + ledge theo LOẠI bracket:
        ///  - OB/OB1: Dtop = HP height − 15, ledge 0.
        ///  - IB/IB1: Dtop = HP height − 30, ledge 15 (đoạn ngang đầu span trước R30, bọc bulb).
        ///  - FB/FB1: Dtop = FlatBar height − 15, ledge 0.
        /// </summary>
        public static void KneeDtop(string code, PanelInput input, out double dtop, out double ledge)
        {
            code = code ?? "";
            if (code.StartsWith("FB")) { dtop = input.FlatBarHeight - 15.0; ledge = 0.0; }
            else if (code.StartsWith("IB")) { dtop = input.HpHeight - 30.0; ledge = 15.0; }
            else { dtop = input.HpHeight - 15.0; ledge = 0.0; }   // OB (mặc định)
        }

        /// <summary>Các tham số suy ra của 1 bracket (dùng chung cho dựng hình + report).</summary>
        public struct KneeParams
        {
            public double S, Huse, F, Thk, Dtop, Ledge, Overhang, FreeEdge;
            public bool Under50, Flanged;
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
            k.F = k.Under50 ? 15.0 : Math.Min(k.Overhang - 15.0, k.S - 30.0);
            k.Huse = k.Under50 ? input.WebHeight - 30.0 : input.WebHeight;

            KneeDtop(part.Code, input, out double dtop, out double ledge);
            k.Dtop = dtop; k.Ledge = ledge;

            // free-edge (toe-flange→toe-HP); flanged → có gấp lip (thk6 ≥350 / thk10 ≥600).
            k.FreeEdge = Math.Sqrt((k.S - k.F) * (k.S - k.F) + (k.Huse - k.Dtop) * (k.Huse - k.Dtop));
            k.Flanged = k.FreeEdge >= (k.Thk >= 8.0 ? 600.0 : 350.0);
            return k;
        }

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            try
            {
                var k = Solve(part, ctx.Cls, ctx.Input);
                if (k.S < 1e-6 || k.Huse < 1e-6 || k.Thk < 1e-6) return null;

                var outline = ctx.Profile.BracketOutline(
                    k.Under50, k.S, k.Huse, k.F, k.Thk, k.Dtop, k.Ledge, k.Flanged);

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
                    $"center=({part.Centroid.X:0},{part.Centroid.Y:0}) | {bendStatus} lumps={GeometryUtil.CountLumps(sol)}");
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
            return $"bend(fe={freeEdgeLen:0} lipW={lipW:0} Rb={Rb:0} diag={diagLen:0} unionThrew={threw})";
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
            Extents3d b = part.Bounds;
            double cx = (b.MinPoint.X + b.MaxPoint.X) / 2.0;
            double cy = (b.MinPoint.Y + b.MaxPoint.Y) / 2.0;
            Point3d e1, e2;
            if (part.IsHorizontal)
            {
                e1 = new Point3d(b.MinPoint.X, cy, 0);
                e2 = new Point3d(b.MaxPoint.X, cy, 0);
            }
            else
            {
                e1 = new Point3d(cx, b.MinPoint.Y, 0);
                e2 = new Point3d(cx, b.MaxPoint.Y, 0);
            }

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
        /// Overhang = TẤM ĐỠ (Flange HOẶC Face Plate) vươn xa nhất theo memberDir tính từ anchor.
        /// Chọn tấm đỡ = Flange/FacePlate gần bracket nhất (bracket nằm TRÊN tấm nào).
        ///  - Flange (chữ nhật): chiếu 4 góc bbox (xử lý cả flange lệch).
        ///  - Face Plate (loe, nv&gt;5): RAY-CAST dọc memberDir từ anchor → mép FacePlate xa nhất TẠI dải bracket
        ///    (bbox không đại diện vì loe không đều). Xem skill panel-parts-reference (Face Plate).
        /// Fallback (không tấm đỡ): đối xứng.
        /// </summary>
        public static double OverhangProj(PanelPart part, ClassificationResult cls)
        {
            AnchorDir(part, cls, out Point3d anchor, out Vector3d dir, out _);

            // Tấm đỡ gần nhất: xét cả Flange lẫn FacePlate.
            PanelPart support = null; double best = double.MaxValue;
            foreach (var f in cls.OfType(PartType.Flange))
            { double d = GeometryUtil.DistToBox(part.Centroid, f.Bounds); if (d < best) { best = d; support = f; } }
            foreach (var fp in cls.OfType(PartType.FacePlate))
            { double d = GeometryUtil.DistToBox(part.Centroid, fp.Bounds); if (d < best) { best = d; support = fp; } }

            if (support == null)
                return (NearestFlangeWidth(part, cls) - part.FootprintThickness) / 2.0;

            // FacePlate loe (có đỉnh thật) → ray-cast mép thật; ngược lại chiếu góc bbox.
            if (support.Type == PartType.FacePlate && support.Vertices != null && support.Vertices.Length >= 3)
            {
                double t = RayMaxHit(anchor, dir, support.Vertices);
                if (t > 0) return t;
            }

            Extents3d b = support.Bounds;
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
        /// Ray từ <paramref name="o"/> theo <paramref name="dir"/> cắt biên polygon; trả t LỚN NHẤT (t&gt;0)
        /// = khoảng cách tới mép polygon xa nhất theo hướng dir (tại đúng dải ngang của ray). 0 nếu không cắt.
        /// </summary>
        private static double RayMaxHit(Point3d o, Vector3d dir, Point3d[] v)
        {
            double px = -dir.Y, py = dir.X;               // pháp tuyến ray (để giải giao)
            double tMax = 0;
            int n = v.Length;
            for (int i = 0; i < n; i++)
            {
                Point3d a = v[i], b = v[(i + 1) % n];
                double ex = b.X - a.X, ey = b.Y - a.Y;
                double denom = ex * px + ey * py;
                if (Math.Abs(denom) < 1e-9) continue;      // cạnh song song ray
                double s = ((o.X - a.X) * px + (o.Y - a.Y) * py) / denom;   // vị trí trên cạnh [0,1]
                if (s < -1e-6 || s > 1 + 1e-6) continue;
                double hx = a.X + s * ex, hy = a.Y + s * ey;
                double t = (hx - o.X) * dir.X + (hy - o.Y) * dir.Y;         // khoảng dọc ray
                if (t > tMax) tMax = t;
            }
            return tMax;
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
