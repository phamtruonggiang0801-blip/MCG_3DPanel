using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Classify;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Dựng FlatBar (FB): hộp chữ nhật như <see cref="PlateBuilder"/>, NHƯNG vát chéo góc dưới ở đầu
    /// (theo chiều dài member) THỰC SỰ TỰ DO — không chạm Web/Bracket/GirderEnd/HP/FlatBar nào khác
    /// (kể cả 2 đầu mới sinh ra do <see cref="FlatBarSplitter"/> tách tại chỗ FB giao HP). Xem skill
    /// sniped-end (đã chốt hình học bằng phân tích DIM trong `FB Sniped End.dwg` + xác nhận kỹ sư).
    ///
    /// Cạnh TRÊN (Z=0, hàn Top Plate) LUÔN giữ nguyên vuông sắc, chạy hết chiều dài — chỉ cạnh DƯỚI
    /// (Z=−h_fb, tự do) bị vát, và CHỈ ở đầu THỰC SỰ tự do, VÀ CHỈ khi cả đoạn đủ dài ≥ 1 khoảng HP
    /// (<see cref="GirderEndDetector.HpMaxSpan"/> — xác nhận kỹ sư 2026-08-26, tránh vát mẩu FB quá
    /// ngắn). Tự dựng CẢ hộp phẳng (đầu không tự do/đoạn quá ngắn) — KHÔNG rơi về
    /// <see cref="PlateBuilder"/> (xem ghi chú tại <see cref="PlateBuilder.CanBuild"/>).
    /// </summary>
    public class FlatBarBuilder : IPartBuilder
    {
        private const string LOG = "[FlatBarBuilder]";
        private const double FLAT_TOP = 15.0;      // đoạn thẳng đứng còn lại ở đầu vát, sát cạnh hàn (cố định)
        private static readonly double RUN_FACTOR = Math.Tan(60.0 * Math.PI / 180.0); // góc vát 60°/30° cố định

        public string Name => "FlatBar";

        public bool CanBuild(PanelPart part) => part.Type == PartType.FlatBar;

        public Solid3d Build(BuildContext ctx, PanelPart part)
        {
            try
            {
                double L = part.MemberLength, thk = part.FootprintThickness, hfb = ctx.Input.FlatBarHeight;
                if (L < 1e-6 || thk < 1e-6 || hfb < 1e-6) return null;

                var (e1, e2) = ClassifyUtil.Endpoints(part);
                bool free1 = GeometryUtil.IsFreeEnd(e1, part, ctx.Cls, ClassifyUtil.FB_FREE_END_TOL);
                bool free2 = GeometryUtil.IsFreeEnd(e2, part, ctx.Cls, ClassifyUtil.FB_FREE_END_TOL);

                // Chỉ Snip End khi TỔNG chiều dài đường vẽ GỐC (Plan View, TRƯỚC khi FlatBarSplitter
                // tách — xác nhận kỹ sư 2026-08-26: "chiều dài tổng" = đường vẽ LIÊN TỤC gốc, KHÔNG phải
                // chiều dài từng đoạn sau tách) đủ ≥ 1 "khoảng HP" (GirderEndDetector.HpMaxSpan: max
                // khoảng 2 HP liền kề, bỏ khoảng có Web ở giữa, lấy phương nhiều HP hơn). FB ngắn hơn 1
                // khoảng HP giữ hộp vuông ở MỌI đoạn. HpMaxSpan=0 (thiếu dữ liệu) ⇒ KHÔNG chặn (vẫn vát).
                double hpSpan = GirderEndDetector.HpMaxSpan(ctx.Cls);
                bool lengthOk = part.FbTotalPlanLength >= hpSpan;
                // ⚠️ KHÔNG return null khi cả 2 đầu không tự do — outline bên dưới đã tự suy biến đúng
                // thành hộp chữ nhật phẳng (nhánh else của free1/free2). Từng trả null ở đây để nhường
                // PlateBuilder dựng hộp — SAI với đoạn FB đã bị FlatBarSplitter tách: PlateBuilder.Extrude
                // clone THẲNG polyline gốc từ DB theo part.Id (nguyên vẹn, KHÔNG theo đúng đoạn đã cắt),
                // vì 1 Id gốc giờ ứng với nhiều đoạn (xem FlatBarSplitter) — mỗi đoạn giữa 2 chỗ giao HP
                // (không đầu nào tự do) sẽ bị dựng thành BẢN SAO NGUYÊN VẸN của CẢ THANH FB gốc, chồng lấn
                // lên mọi đoạn khác. Luôn tự dựng ở đây (kể cả hộp phẳng) để dùng đúng MemberLength/Vertices
                // của riêng đoạn đang xét — không bao giờ rơi về PlateBuilder cho FlatBar nữa.

                // rise/run theo đúng khuôn "member−15" của Bracket/GE; góc 30°/60° CỐ ĐỊNH (không phụ
                // thuộc quy cách FB) — xác nhận kỹ sư, xem skill sniped-end.
                double rise = Math.Max(0.0, hfb - FLAT_TOP);
                double run = rise * RUN_FACTOR;
                bool clamped = false;
                // Override Sniped End từng đầu (tab Result) thắng quyết định tự động (tự do && đủ dài).
                bool snip1 = SnipOverride.Decide(ctx.Cls, part, e1, free1 && lengthOk);
                bool snip2 = SnipOverride.Decide(ctx.Cls, part, e2, free2 && lengthOk);
                int freeCount = (snip1 ? 1 : 0) + (snip2 ? 1 : 0);
                if (run * freeCount > L)
                {
                    // Đoạn quá ngắn so với vát (2 đầu vát chồng nhau, hoặc 1 đầu vát dài hơn cả đoạn) —
                    // kẹp an toàn L/2, CHƯA có mẫu bản vẽ xác nhận case biên này (xem TODO skill sniped-end).
                    run = L / 2.0;
                    clamped = true;
                }

                // Outline elevation local: x=dọc member (0..L từ e1->e2), y: 0=trên/hàn, −hfb=dưới/tự do.
                var outline = new List<VertexBulge>(6);
                outline.Add(new VertexBulge(0, 0, 0));
                outline.Add(new VertexBulge(L, 0, 0));
                // Đầu tự do: từ cạnh hàn (Z=0) đi THẲNG XUỐNG đúng FLAT_TOP=15mm trước (KHÔNG phải
                // rise=hfb−15) — đó là đoạn "sát cạnh hàn" còn nguyên; RỒI mới vát chéo (rise, run) xuống
                // gặp cạnh dưới (tự do, Z=−hfb). Nhầm 2 giá trị này (từng vẽ tụt xuống −(hfb−15) trước
                // khi vát) khiến hình vát sai hẳn — xem skill sniped-end mục 1 (đối chiếu pentagon `1F851`).
                if (snip2)
                {
                    outline.Add(new VertexBulge(L, -FLAT_TOP, 0));
                    outline.Add(new VertexBulge(L - run, -hfb, 0));
                }
                else
                {
                    outline.Add(new VertexBulge(L, -hfb, 0));
                }
                if (snip1)
                {
                    outline.Add(new VertexBulge(run, -hfb, 0));
                    outline.Add(new VertexBulge(0, -FLAT_TOP, 0));
                }
                else
                {
                    outline.Add(new VertexBulge(0, -hfb, 0));
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

                ctx.Report.Add(
                    $"FlatBar {part.Id.Handle} L={L:0} totalPlanLen={part.FbTotalPlanLength:0} thk={thk:0} h={hfb:0} " +
                    $"free1={free1} free2={free2} hpSpan={hpSpan:0} lengthOk={lengthOk} rise={rise:0} " +
                    $"run={run:0.#}{(clamped ? " (clamped)" : "")} " +
                    $"center=({part.Centroid.X:0},{part.Centroid.Y:0}) lumps={GeometryUtil.CountLumps(sol)}");
                return sol;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{LOG} lỗi ({part.Id.Handle}): {ex.Message}");
                ctx.Warn($"FlatBar {part.Id.Handle} buildError: {ex.Message}");
                return null;
            }
        }
    }
}
