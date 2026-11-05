using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
using MCG_3DPanel.UI;
using MCG_3DPanel.Utilities;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// XUẤT FILE kết quả (đường dẫn, mở file, report phân loại JSON).
    /// Tách khỏi <see cref="PanelCommands"/> để lệnh chỉ lo điều phối, không lo I/O + định dạng.
    /// Xem skill module-per-object.
    /// </summary>
    internal static class ReportOutput
    {
        private const string LOG_PREFIX = "[MCG_3DPanel]";

        /// <summary>Đường dẫn file kết quả trên Desktop, có timestamp: MCG_Panel{kind}_yyyyMMdd_HHmmss.json</summary>
        public static string BuildOutputPath(string kind)
        {
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string name = "MCG_Panel" + kind + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json";
            return Path.Combine(desktop, name);
        }

        /// <summary>Mở file bằng ứng dụng mặc định. Trả null nếu mở được, hoặc lý do để caller BÁO người dùng (file đã
        /// ghi xong — chỉ không mở được, vd chưa có app cho .csv/.json) — ux-guardrails.</summary>
        public static string OpenFile(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); return null; }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG_PREFIX} Không mở được file: {ex.Message}");
                return ex.Message;
            }
        }

        /// <summary>
        /// Ghi report KẾT QUẢ DỰNG 3D ra JSON — tự động sau mỗi Build, KHÔNG tự mở (chỉ để debug).
        /// 1 file cho cả lô; <c>panels</c> = 1 phần tử / panel đã chọn.
        /// <c>solids</c> = từng SOLID 3D trong block, dựng từ <see cref="ResultRow.From"/> nên handle / kind /
        /// type / thứ tự TRÙNG KHỚP tab Result và file CSV export. Handle 2D gốc chỉ là trường phụ
        /// <c>source2DHandle</c> để lần ngược về bản vẽ 2D. <c>notBuilt</c> = part 2D không ra solid
        /// (không có handle 3D). Kèm các dòng chẩn đoán builder (<c>BuildResult.Report</c>).
        /// </summary>
        public static void WriteBuildReport(List<BuildResult> builds, string path)
        {
            var root = new JsonObject
            {
                { "panelCount", builds.Count },
                { "solidCount", builds.Sum(b => b.Solids.Count) },
                { "panels", builds.Select(b => (object)BuildPanelJson(b)).ToList() },
            };
            File.WriteAllText(path, JsonWriter.Serialize(root), System.Text.Encoding.UTF8);
        }

        /// <summary>1 panel trong report lô Build.</summary>
        private static JsonObject BuildPanelJson(BuildResult build)
        {
            var solids = ResultRow.From(build, includeErased: true).Select(r => (object)new JsonObject
            {
                { "panel", r.Panel ?? "" },
                { "handle", r.Handle },
                { "family", r.Family.ToString() },
                { "kind", r.Kind ?? "" },
                { "size", r.Size ?? "" },
                { "flags", r.Flags ?? "" },
                { "sourceKey", r.SourceKey ?? "" },
                { "source2DHandle", r.Solid?.Part != null ? r.Solid.Part.Id.Handle.ToString() : "" },
                { "mergedCodes", (r.Solid?.MergedCodes ?? new List<string>()).Select(c => (object)c).ToList() },
                { "minX", Round(r.Bounds.MinPoint.X) }, { "minY", Round(r.Bounds.MinPoint.Y) }, { "minZ", Round(r.Bounds.MinPoint.Z) },
                { "maxX", Round(r.Bounds.MaxPoint.X) }, { "maxY", Round(r.Bounds.MaxPoint.Y) }, { "maxZ", Round(r.Bounds.MaxPoint.Z) },
                { "volume", Round(r.Solid?.Volume) },
            }).ToList();

            var notBuilt = build.Parts.Where(p => !p.Built).Select(p => (object)new JsonObject
            {
                { "source2DHandle", p.Handle }, { "type", p.Type }, { "code", p.Code ?? "" },
            }).ToList();

            return new JsonObject
            {
                { "panelBlockName", build.PanelBlockName ?? "" },
                { "blockName", build.BlockName ?? "" },
                { "kept", build.Kept },
                { "solidCount", solids.Count },
                { "notBuiltCount", notBuilt.Count },
                { "solids", solids },
                { "notBuilt", notBuilt },
                { "warningCount", build.Warnings.Count },
                { "warnings", build.Warnings.Select(w => (object)w).ToList() },
                { "report", build.Report.Select(r => (object)r).ToList() },
            };
        }

        /// <summary>Ghi report phân loại chi tiết ra JSON (breakdown theo từng loại/instance).</summary>
        public static void WriteClassifyReport(ClassificationResult result, string path)
        {
            var counts = new JsonObject();
            foreach (var kv in result.Counts())
                counts.Add(kv.Key.ToString(), kv.Value);

            // Web runs: mỗi run kèm danh sách chiều dày các tầng.
            var webRuns = new List<object>();
            int idx = 1;
            foreach (var run in result.WebRuns().OrderByDescending(r => RunLength(r)))
            {
                var tiers = run.GroupBy(p => Math.Round(p.FootprintThickness, 1))
                               .OrderBy(g => g.Key)
                               .Select(g => (object)new JsonObject { { "thickness", g.Key }, { "count", g.Count() } })
                               .ToList();
                webRuns.Add(new JsonObject
                {
                    { "web", "Web#" + idx++ },
                    { "segments", run.Count },
                    { "lengthApprox", Math.Round(RunLength(run), 1) },
                    { "thicknessTiers", tiers },
                });
            }

            var flanges = result.OfType(PartType.Flange)
                .OrderBy(p => p.FootprintThickness)
                .Select(p => (object)new JsonObject
                {
                    { "width", Math.Round(p.FootprintThickness, 1) },
                    { "length", Math.Round(p.MemberLength, 1) },
                    { "handle", p.Id.Handle.ToString() },
                }).ToList();

            var facePlates = result.OfType(PartType.FacePlate)
                .Select(p => (object)new JsonObject
                {
                    { "vertices", p.VertexCount },
                    { "bbox", BBoxStr(p) },
                    { "handle", p.Id.Handle.ToString() },
                }).ToList();

            var bracketByCode = new JsonObject();
            foreach (var g in result.OfType(PartType.Bracket).GroupBy(p => p.Code ?? "?").OrderBy(g => g.Key))
                bracketByCode.Add(g.Key, g.Count());

            var brackets = result.OfType(PartType.Bracket)
                .OrderBy(p => p.Code)
                .Select(p => (object)new JsonObject
                {
                    { "code", p.Code },
                    { "thickness", Math.Round(p.FootprintThickness, 1) },
                    { "length", Math.Round(p.MemberLength, 1) },
                    { "handle", p.Id.Handle.ToString() },
                }).ToList();

            // Danh sách TẤT CẢ part kèm hình học WCS + handle — để chẩn đoán chính xác (belly, kề nhau…).
            // "verts": toạ độ đỉnh polygon THẬT (không phải bbox) — cần để tính overlap chính xác với
            // FacePlate loe (bbox của FacePlate rộng hơn nhiều hình dạng thật, xem skill panel-parts-reference).
            var parts = result.Parts.Select(p => (object)new JsonObject
            {
                { "handle", p.Id.IsNull ? "" : p.Id.Handle.ToString() },
                { "type", p.Type.ToString() },
                { "code", p.Code ?? "" },
                { "layer", p.Layer }, { "color", p.ColorIndex }, { "nv", p.VertexCount },
                { "thk", Math.Round(p.FootprintThickness, 1) }, { "len", Math.Round(p.MemberLength, 1) },
                { "cx", Math.Round(p.Centroid.X, 1) }, { "cy", Math.Round(p.Centroid.Y, 1) },
                { "w", Math.Round(p.Bounds.MaxPoint.X - p.Bounds.MinPoint.X, 1) },
                { "h", Math.Round(p.Bounds.MaxPoint.Y - p.Bounds.MinPoint.Y, 1) },
                { "bx", Math.Round(p.BellyDir.X, 3) }, { "by", Math.Round(p.BellyDir.Y, 3) },
                { "verts", VertsArray(p) },
            }).ToList();

            var root = new JsonObject
            {
                { "panelBlockName", result.PanelBlockName },
                { "summary", result.Summary() },
                { "notes", result.Notes.Select(r => (object)r).ToList() },
                { "counts", counts },
                { "webRuns", webRuns },
                { "flanges", flanges },
                { "facePlates", facePlates },
                { "bracketByCode", bracketByCode },
                { "brackets", brackets },
                { "flatBarCount", result.OfType(PartType.FlatBar).Count() },
                { "hpCount", result.OfType(PartType.HP).Count() },
                { "parts", parts },
            };
            File.WriteAllText(path, JsonWriter.Serialize(root), System.Text.Encoding.UTF8);
        }

        private static object Round(double? v) => v.HasValue ? (object)Math.Round(v.Value, 1) : null;

        private static double RunLength(List<PanelPart> run)
        {
            var ext = run[0].Bounds;
            for (int i = 1; i < run.Count; i++) ext.AddExtents(run[i].Bounds);
            return Math.Max(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y);
        }

        private static List<object> VertsArray(PanelPart p)
        {
            var v = p.Vertices;
            if (v == null) return new List<object>();
            return v.Select(pt => (object)new List<object> { Math.Round(pt.X, 1), Math.Round(pt.Y, 1) }).ToList();
        }

        private static string BBoxStr(PanelPart p)
        {
            double w = p.Bounds.MaxPoint.X - p.Bounds.MinPoint.X;
            double h = p.Bounds.MaxPoint.Y - p.Bounds.MinPoint.Y;
            return $"{Math.Round(w, 1)}x{Math.Round(h, 1)}";
        }
    }
}
