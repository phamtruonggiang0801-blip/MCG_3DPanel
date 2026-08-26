using System.Collections.Generic;
using System.Linq;
using System.Text;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// 1 bộ phận đã nhận diện: tham chiếu tới LWPolyline gốc + hình học WCS đã tính sẵn.
    /// Là "carrier" giữa ClassificationService (Phase 1) và Build3DService (Phase 2),
    /// nên mang kiểu hình học của AutoCAD (không phải POCO thuần).
    /// </summary>
    public class PanelPart
    {
        public ObjectId Id { get; set; }

        // Chuỗi ObjectId từ Block Reference NGOÀI CÙNG (trong Model Space) → … block lồng … → polyline lá.
        // Cần để highlight entity LỒNG trong block qua FullSubentityPath (Id trần không chọn được).
        public ObjectId[] IdPath { get; set; }

        public PartType Type { get; set; }

        // Các part cùng 1 Web logic (chuỗi collinear) chia sẻ GroupId. -1 = độc lập.
        public int GroupId { get; set; } = -1;

        // Hướng long-axis: true nếu nằm ngang (dx >= dy theo bbox WCS).
        // ⚠️ Chỉ có nghĩa với member SONG SONG TRỤC. Member CHÉO phải dùng <see cref="AxisDir"/>.
        public bool IsHorizontal { get; set; }

        // Trục dài THẬT (đơn vị, Z=0) lấy từ cạnh dài của polyline — đúng cả với member CHÉO.
        // Bbox KHÔNG suy ra được hướng khi member nằm chéo.
        public Vector3d AxisDir { get; set; }
        // Toạ độ tâm theo phương NGẮN (Y nếu ngang, X nếu đứng) — để test collinear.
        public double PerpCenter { get; set; }

        // Mã bracket (OB/OB1/IB/IB1/FB…) — chỉ set cho PartType.Bracket; null với loại khác.
        public string Code { get; set; }

        // Hướng BỤNG (bulb) — chỉ set cho PartType.HP (ClassificationService tính theo chiều đổ +
        // override "HP ngoài cùng đổ ra ngoài"). Zero nếu chưa xác định. Xem skill hp-profile.
        public Vector3d BellyDir { get; set; }

        public string Layer { get; set; }
        public int ColorIndex { get; set; }
        public int VertexCount { get; set; }

        public double Area { get; set; }              // diện tích footprint (mm²)
        public double FootprintThickness { get; set; } // cạnh NGẮN của rectangle (mm)
        public double MemberLength { get; set; }       // cạnh DÀI của rectangle (mm) — của RIÊNG part này (sau khi tách, nếu có)

        // Chỉ set cho PartType.FlatBar (FlatBarSplitter) — TỔNG chiều dài đường vẽ LIÊN TỤC gốc trong
        // Plan View (TRƯỚC khi tách tại chỗ giao HP) — xác nhận kỹ sư: người vẽ 2D kéo 1 polyline liên
        // tục xuyên qua HP, "chiều dài tổng" để so ngưỡng khoảng HP (Sniped End) là chiều dài đường vẽ
        // đó, KHÔNG PHẢI MemberLength của riêng từng đoạn sau khi tách. Xem skill sniped-end.
        public double FbTotalPlanLength { get; set; }

        public Point3d Centroid { get; set; }          // tâm bbox (WCS)
        public Extents3d Bounds { get; set; }          // bbox (WCS)
        public Point3d[] Vertices { get; set; }         // đỉnh polyline (WCS) — cần cho FacePlate loe (chiếu mép thật)
        public Matrix3d Transform { get; set; }        // transform tích luỹ về WCS (dùng cho Build)
    }

    /// <summary>Kết quả phân loại 1 panel.</summary>
    public class ClassificationResult
    {
        public string PanelBlockName { get; set; }
        public List<PanelPart> Parts { get; } = new List<PanelPart>();
        public PanelPart TopPlate { get; set; }

        public IEnumerable<PanelPart> OfType(PartType t) => Parts.Where(p => p.Type == t);

        /// <summary>Các Web logic: gom part Web theo GroupId (mỗi nhóm = 1 web nhiều tầng dày).</summary>
        public IEnumerable<List<PanelPart>> WebRuns()
        {
            return OfType(PartType.Web).GroupBy(p => p.GroupId).Select(g => g.ToList());
        }

        /// <summary>
        /// Đếm "logic": Web = số run (nhóm collinear), các loại khác = số polyline.
        /// </summary>
        public Dictionary<PartType, int> Counts()
        {
            var d = Parts.Where(p => p.Type != PartType.Web)
                         .GroupBy(p => p.Type).ToDictionary(g => g.Key, g => g.Count());
            int webRuns = WebRuns().Count();
            if (webRuns > 0) d[PartType.Web] = webRuns;
            return d;
        }

        /// <summary>Chuỗi tóm tắt số lượng từng loại, để hiện trên status bar.</summary>
        public string Summary()
        {
            var order = new[] { PartType.TopPlate, PartType.Web, PartType.Flange, PartType.FacePlate, PartType.FlatBar, PartType.HP, PartType.Bracket, PartType.GirderEnd, PartType.CutOut, PartType.ClosedPlate, PartType.BackingPlate };
            var counts = Counts();
            var sb = new StringBuilder();
            foreach (var t in order)
            {
                counts.TryGetValue(t, out int n);
                if (sb.Length > 0) sb.Append("  ");
                sb.Append(t).Append(':').Append(n);
            }
            return sb.ToString();
        }
    }
}
