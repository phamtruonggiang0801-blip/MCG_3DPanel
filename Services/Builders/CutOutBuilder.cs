using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services.Builders
{
    /// <summary>
    /// Xử lý <see cref="PartType.CutOut"/> — KHÔNG implement <see cref="IPartBuilder"/> vì CutOut không
    /// tự dựng 1 solid độc lập: nó là LỖ, phải TRỪ (boolean subtract) thẳng vào solid Top Plate đã dựng
    /// (xác nhận người dùng 2026-08-25). Gọi tường minh từ <see cref="Build3DService"/> SAU khi Top Plate
    /// đã có solid, KHÔNG đi qua vòng lặp Dispatch theo từng part như các loại khác.
    /// </summary>
    internal static class CutOutBuilder
    {
        private const string LOG = "[CutOutBuilder]";

        // Margin extrude 2 đầu — tránh mặt trùng khít (coplanar) gây lỗi/rơi boolean khi trừ đúng khít
        // độ dày Top Plate.
        private const double MARGIN = 5.0;

        /// <summary>Trừ mọi <see cref="PartType.CutOut"/> của <paramref name="ctx"/> ra khỏi
        /// <paramref name="topPlateSolid"/> (sửa TRỰC TIẾP, không trả solid mới). Lỗi ở 1 cutout không làm
        /// hỏng các cutout khác — ghi vào Report và bỏ qua cutout đó (Top Plate coi như chưa khoét lỗ này).
        /// Trả về handle (chuỗi) của các cutout đã trừ THÀNH CÔNG — dùng để cập nhật report build JSON.</summary>
        public static List<string> ApplyCutOuts(BuildContext ctx, Solid3d topPlateSolid, double topPlateThickness)
        {
            var succeeded = new List<string>();
            var cutouts = ctx.Cls.OfType(PartType.CutOut).ToList();
            if (cutouts.Count == 0) return succeeded;

            double extrude = topPlateThickness + 2 * MARGIN;
            double moveZ = -MARGIN;

            foreach (var p in cutouts)
            {
                Solid3d cutter = null;
                try
                {
                    cutter = PlateBuilder.Extrude(ctx.Tr, p, extrude, moveZ, ctx.Report);
                    if (cutter == null)
                    {
                        ctx.Report.Add($"[CutOut] Không dựng được lỗ {p.Id.Handle} → bỏ qua (Top Plate giữ nguyên, không khoét).");
                        continue;
                    }
                    topPlateSolid.BooleanOperation(BooleanOperationType.BoolSubtract, cutter);
                    cutter = null; // BoolSubtract đã tiêu thụ (Dispose) cutter — tránh Dispose lần nữa ở finally.
                    succeeded.Add(p.Id.Handle.ToString());
                }
                catch (System.Exception ex)
                {
                    Debug.WriteLine($"{LOG} lỗi ({p.Id.Handle}): {ex.Message}");
                    ctx.Warn($"[CutOut] Lỗi khoét lỗ {p.Id.Handle}: {ex.Message} → bỏ qua.");
                }
                finally
                {
                    cutter?.Dispose();
                }
            }
            return succeeded;
        }
    }
}
