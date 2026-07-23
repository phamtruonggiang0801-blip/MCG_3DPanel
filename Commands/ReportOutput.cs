using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
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

        /// <summary>Mở file bằng ứng dụng mặc định (nuốt lỗi — chỉ tiện ích).</summary>
        public static void OpenFile(string path)
        {
            try { Process.Start(new ProcessStartInfo(path) { UseShellExecute = true }); }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG_PREFIX} Không mở được file: {ex.Message}"); }
        }

        /// <summary>
        /// Ghi report CHẨN ĐOÁN của lần dựng (mỗi builder tự thêm dòng vào <c>BuildResult.Report</c>)
        /// ra .txt trên Desktop rồi mở. Mặc định KHÔNG gọi — bật khi cần debug hình học.
        /// </summary>
        public static void WriteBuildReport(BuildResult build, Autodesk.AutoCAD.EditorInput.Editor ed)
        {
            if (build == null || build.Report.Count == 0) return;
            string rp = BuildOutputPath("BuildReport").Replace(".json", ".txt");
            File.WriteAllText(rp, string.Join("\n", build.Report), System.Text.Encoding.UTF8);
            ed?.WriteMessage($"\n{LOG_PREFIX} Build report: {rp}\n");
            OpenFile(rp);
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

            var root = new JsonObject
            {
                { "panelBlockName", result.PanelBlockName },
                { "summary", result.Summary() },
                { "counts", counts },
                { "webRuns", webRuns },
                { "flanges", flanges },
                { "facePlates", facePlates },
                { "bracketByCode", bracketByCode },
                { "brackets", brackets },
                { "flatBarCount", result.OfType(PartType.FlatBar).Count() },
                { "hpCount", result.OfType(PartType.HP).Count() },
            };
            File.WriteAllText(path, JsonWriter.Serialize(root), System.Text.Encoding.UTF8);
        }

        private static double RunLength(List<PanelPart> run)
        {
            var ext = run[0].Bounds;
            for (int i = 1; i < run.Count; i++) ext.AddExtents(run[i].Bounds);
            return Math.Max(ext.MaxPoint.X - ext.MinPoint.X, ext.MaxPoint.Y - ext.MinPoint.Y);
        }

        private static string BBoxStr(PanelPart p)
        {
            double w = p.Bounds.MaxPoint.X - p.Bounds.MinPoint.X;
            double h = p.Bounds.MaxPoint.Y - p.Bounds.MinPoint.Y;
            return $"{Math.Round(w, 1)}x{Math.Round(h, 1)}";
        }
    }
}
