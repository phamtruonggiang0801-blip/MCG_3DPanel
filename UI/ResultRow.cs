using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;

namespace MCG_3DPanel.UI
{
    /// <summary>
    /// 1 dòng của grid "Result": Handle đối tượng · loại tool nhận diện · loại người dùng sửa lại.
    /// <see cref="Path"/>/<see cref="Bounds"/> giữ để highlight + zoom entity LỒNG trong block.
    /// </summary>
    public class ResultRow
    {
        public string Handle { get; set; }         // handle của polyline nguồn (duy nhất trong DWG)
        public string DetectedType { get; set; }    // loại tool nhận diện: web6 / OB / topplate …
        public string CorrectedType { get; set; }   // người dùng điền lại (để trống = đồng ý)

        // Không hiện lên grid — phục vụ highlight/zoom.
        internal ObjectId[] Path { get; set; }
        internal Extents3d Bounds { get; set; }

        /// <summary>Dựng danh sách dòng từ kết quả phân loại (bỏ part không có Id hợp lệ).</summary>
        public static List<ResultRow> From(ClassificationResult result)
        {
            if (result == null) return new List<ResultRow>();
            return result.Parts
                .Where(p => !p.Id.IsNull)
                .OrderBy(p => Rank(p.Type)).ThenBy(p => Label(p))
                .Select(p => new ResultRow
                {
                    Handle = p.Id.Handle.ToString(),
                    DetectedType = Label(p),
                    CorrectedType = "",
                    Path = p.IdPath ?? new[] { p.Id },
                    Bounds = p.Bounds,
                })
                .ToList();
        }

        /// <summary>Nhãn loại hiển thị: Web/Bracket/GE dùng dày hoặc mã, còn lại là tên loại.</summary>
        public static string Label(PanelPart p)
        {
            switch (p.Type)
            {
                case PartType.TopPlate: return "topplate";
                case PartType.Web: return "web" + Round(p.FootprintThickness);
                case PartType.Flange: return "flange";
                case PartType.FacePlate: return "faceplate";
                case PartType.FlatBar: return "flatbar";
                case PartType.HP: return "hp";
                case PartType.Bracket: return string.IsNullOrEmpty(p.Code) ? "bracket" : p.Code;
                case PartType.GirderEnd: return string.IsNullOrEmpty(p.Code) ? "GE" : p.Code;
                case PartType.CutOut: return "cutout";
                case PartType.ClosedPlate: return "closed plate";
                case PartType.BackingPlate: return "backing plate";
                default: return "unknown";
            }
        }

        private static int Round(double v) => (int)Math.Round(v);

        // Thứ tự nhóm cho dễ đọc: kết cấu chính → chi tiết → bracket/GE.
        private static int Rank(PartType t)
        {
            switch (t)
            {
                case PartType.TopPlate: return 0;
                case PartType.Web: return 1;
                case PartType.Flange: return 2;
                case PartType.FacePlate: return 3;
                case PartType.FlatBar: return 4;
                case PartType.HP: return 5;
                case PartType.GirderEnd: return 6;
                case PartType.Bracket: return 7;
                case PartType.CutOut: return 8;
                case PartType.ClosedPlate: return 8;
                case PartType.BackingPlate: return 8;
                default: return 9;
            }
        }
    }
}
