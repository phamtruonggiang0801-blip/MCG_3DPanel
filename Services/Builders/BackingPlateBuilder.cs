using System;
using System.Collections.Generic;
using System.Diagnostics;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dựng BackingPlate: đứng thẳng như Web, THỤT VÀO 30mm mỗi đầu theo Z (cách Top Plate/Flange —
    /// xem <see cref="PlateBuilder.ZParams"/>), NHƯNG vát góc ở đầu THỰC SỰ TỰ DO (không chạm Web/
    /// Bracket/GirderEnd/HP/FlatBar nào khác).
    ///
    /// ⚠️ SỬA (2026-08-28, lần 3) — ĐỐI CHIẾU mẫu thật `CAS.dump.json` (`*Model_Space`, polyline
    /// `1F255`, layer `Mechanical-AM_0`, 6 đỉnh, khớp CHÍNH XÁC kích thước part test `38ED5` L=50/H=314):
    /// đo tay ra `flat=15mm` KHÔNG nằm ở đầu TỰ DO (2 bản trước đặt sai chỗ) — mà nằm SÁT CẠNH KHÔNG
    /// VÁT (đầu kia, "phía lưng sát Web" = cạnh còn nguyên vẹn/không cắt, đại diện "chỗ chắc chắn"),
    /// rồi đường CHÉO kéo dài suốt phần CÒN LẠI của chiều dài (`reach = L − 15`) để chạm ĐÚNG mép của
    /// đầu tự do (không giữ flat ở đầu tự do — góc đó bị cắt trụi, chỉ còn lại cạnh cao đã rút ngắn
    /// giữa 2 đường chéo trên/dưới). Độ dốc: `run (trục cao) = reach (trục dài) · tan(60°)` — khớp
    /// đúng đỉnh mẫu (reach=35 → run=35·tan60°=60.62, đúng khít toạ độ Y đo được).
    ///
    /// Với `H=314` (dày `WebHeight−60`), vát tại 1 đầu chiếm `run≈60.6mm` mỗi góc (trên+dưới) — vẫn
    /// còn 1 cạnh cao nguyên ở giữa (`H−2·run≈192.8mm`, khớp đỉnh mẫu). CẢ 2 đầu tự do (không có "đầu
    /// neo" cố định) → CHƯA có mẫu đối chiếu — dùng phỏng đoán AN TOÀN: chia đôi chiều dài, mỗi nửa tự
    /// áp công thức trên với `reach = L/2 − flat/2` (2 đường chéo đối xứng gặp nhau ở 1 dải flat 15mm
    /// giữa tấm) — CẦN xác nhận riêng nếu gặp panel có backing plate cả 2 đầu tự do.
    /// </summary>
    public class BackingPlateBuilder : IPartBuilder
    {
        private const string LOG = "[BackingPlateBuilder]";
        private const double FLAT = 15.0;                                     // sát cạnh KHÔNG vát — xác nhận kỹ sư + đối chiếu CAS.dump.json
        private static readonly double TAN60 = Math.Tan(60.0 * Math.PI / 180.0);
        private const double TOP_SETBACK = 30.0;                              // khớp PlateBuilder.ZParams (moveZ=-30)

        public string Name => "BackingPlate";

        public bool CanBuild(PanelPart part) => part.Type == PartType.BackingPlate;

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            try
            {
                double L = part.MemberLength, thk = part.FootprintThickness;
                double H = ctx.Input.WebHeight - 2.0 * TOP_SETBACK;           // = PlateBuilder.ZParams: extrude=-(WebHeight-60)
                if (L < 1e-6 || thk < 1e-6 || H < 1e-6) return null;

                var (e1, e2) = ClassifyUtil.Endpoints(part);
                bool free1 = GeometryUtil.IsFreeEnd(e1, part, ctx.Cls, ClassifyUtil.FB_FREE_END_TOL);
                bool free2 = GeometryUtil.IsFreeEnd(e2, part, ctx.Cls, ClassifyUtil.FB_FREE_END_TOL);

                // reach (dọc trục DÀI, từ điểm bắt đầu vát tới mép TỰ DO) → run (dọc trục CAO) = reach·tan60°,
                // kẹp run ≤ H/2 (không cho 2 đường chéo trên/dưới chồng nhau) bằng cách kẹp ngược lại reach.
                double RunFrom(double reach)
                {
                    reach = Math.Max(0.0, reach);
                    double run = reach * TAN60;
                    return Math.Min(run, H / 2.0);
                }

                List<VertexBulge> outline;
                if (free1 && free2)
                {
                    // CẢ 2 đầu tự do — không có đầu "neo" cố định để đo flat=15 từ đó. Phỏng đoán AN
                    // TOÀN (CHƯA có mẫu đối chiếu): chia đôi chiều dài, dải flat 15mm nằm GIỮA tấm.
                    double half = L / 2.0, margin = FLAT / 2.0;
                    double reach = Math.Max(0.0, half - margin);
                    double run = RunFrom(reach);
                    outline = new List<VertexBulge>(8)
                    {
                        new VertexBulge(half - margin, 0, 0),
                        new VertexBulge(half + margin, 0, 0),
                        new VertexBulge(L, -run, 0),
                        new VertexBulge(L, -(H - run), 0),
                        new VertexBulge(half + margin, -H, 0),
                        new VertexBulge(half - margin, -H, 0),
                        new VertexBulge(0, -(H - run), 0),
                        new VertexBulge(0, -run, 0),
                    };
                }
                else if (free1)   // đầu e1 (x=0) tự do — đầu e2 (x=L) là "neo", đo flat=15 từ đó
                {
                    double reach = Math.Max(0.0, L - FLAT);
                    double run = RunFrom(reach);
                    double flatX = L - FLAT;
                    outline = new List<VertexBulge>(6)
                    {
                        new VertexBulge(flatX, 0, 0),
                        new VertexBulge(0, -run, 0),
                        new VertexBulge(0, -(H - run), 0),
                        new VertexBulge(flatX, -H, 0),
                        new VertexBulge(L, -H, 0),
                        new VertexBulge(L, 0, 0),
                    };
                }
                else if (free2)   // đầu e2 (x=L) tự do — đầu e1 (x=0) là "neo" — khớp ĐÚNG mẫu CAS.dump.json
                {
                    double reach = Math.Max(0.0, L - FLAT);
                    double run = RunFrom(reach);
                    outline = new List<VertexBulge>(6)
                    {
                        new VertexBulge(FLAT, 0, 0),
                        new VertexBulge(L, -run, 0),
                        new VertexBulge(L, -(H - run), 0),
                        new VertexBulge(FLAT, -H, 0),
                        new VertexBulge(0, -H, 0),
                        new VertexBulge(0, 0, 0),
                    };
                }
                else               // không đầu nào tự do — hộp phẳng nguyên vẹn
                {
                    outline = new List<VertexBulge>(4)
                    {
                        new VertexBulge(0, 0, 0),
                        new VertexBulge(L, 0, 0),
                        new VertexBulge(L, -H, 0),
                        new VertexBulge(0, -H, 0),
                    };
                }

                Region reg = GeometryUtil.RegionFrom(outline);
                if (reg == null)
                {
                    ctx.Report.Add($"{LOG} {part.Id.Handle} L={L:0} thk={thk:0} free1={free1} free2={free2} buildError: RegionFrom(outline)=null");
                    return null;
                }

                var sol = new Solid3d();
                var sob = new SweepOptionsBuilder();
                sol.CreateExtrudedSolid(reg, new Vector3d(0, 0, thk), sob.ToSweepOptions());
                reg.Dispose();
                sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -thk / 2.0)));   // căn giữa dày

                Vector3d dir = (e2 - e1).GetNormal();
                Vector3d normal = dir.CrossProduct(Vector3d.ZAxis);
                normal = normal.Length > 1e-9 ? normal.GetNormal() : Vector3d.YAxis;
                sol.TransformBy(Matrix3d.AlignCoordinateSystem(
                    Point3d.Origin, Vector3d.XAxis, Vector3d.YAxis, Vector3d.ZAxis,
                    e1, dir, Vector3d.ZAxis, normal));
                sol.TransformBy(Matrix3d.Displacement(new Vector3d(0, 0, -TOP_SETBACK)));  // thụt 30mm dưới Top Plate

                ctx.Report.Add(
                    $"BackingPlate {part.Id.Handle} L={L:0} thk={thk:0} H={H:0} free1={free1} free2={free2} " +
                    $"flat={FLAT:0} center=({part.Centroid.X:0},{part.Centroid.Y:0}) lumps={GeometryUtil.CountLumps(sol)}");
                return sol;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG} lỗi ({part.Id.Handle}): {ex.Message}");
                ctx.Warn($"BackingPlate {part.Id.Handle} buildError: {ex.Message}");
                return null;
            }
        }
    }
}
