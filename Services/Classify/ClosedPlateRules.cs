using System.Collections.Generic;
using System.Linq;

namespace MCG_3DPanel.Services.Classify
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Luật tấm kín theo THÂN tấm và mốc vuông góc cho GE (kỹ sư 2026-11-06, 7D-17P `1B5FA`, `1B608`).</summary>
    public static class ClosedPlateRules
    {
        /// <summary>Thân tấm dài <paramref name="length"/> có web vuông góc chạm ở CẢ HAI nửa. Tấm vắt qua đầu 2 web, nhô ra
        /// ngoài: đo tại đầu mút không phản ánh việc nó kẹp giữa 2 web.</summary>
        /// <param name="offsets">Vị trí đầu web vuông góc dọc tấm, tính từ 1 đầu mút.</param>
        public static bool BridgesBothHalves(IEnumerable<double> offsets, double length)
        {
            var list = offsets.ToList();
            return list.Any(o => o < length / 2) && list.Any(o => o >= length / 2);
        }

        /// <summary>Khoảng tới mốc vuông góc của GE: ưu tiên Web; chỉ khi không có Web mới dùng Closed Plate, để GE đã có Web
        /// không đổi chiều dài (7D-17P `1B5FF` giữ 469). −1 nếu không có mốc.</summary>
        public static double PerpEdge(double webEdge, double closedPlateEdge) => webEdge > 0 ? webEdge : closedPlateEdge;
    }
}
