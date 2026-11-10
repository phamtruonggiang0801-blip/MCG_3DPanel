using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Block profile cho mặt cắt, đặt tên theo template CAS.dwt: "HP{b}x{t}" (bulb flat) và "BS {h}x{t}" (flat bar).
    /// Bản vẽ lẫn template đều không có quy cách đó ⇒ tool tự vẽ (kỹ sư 2026-11-10).</summary>
    public static class ProfileBlocks
    {
        private static readonly Regex Name = new Regex(@"^(HP|BS )(\d+(?:\.\d+)?)x(\d+(?:\.\d+)?)$", RegexOptions.CultureInvariant);

        /// <param name="kind">"HP" hoặc "BS".</param>
        public static bool TryParse(string name, out string kind, out double height, out double thickness)
        {
            kind = null; height = thickness = 0;
            var m = Name.Match(name ?? "");
            if (!m.Success) return false;
            height = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
            thickness = double.Parse(m.Groups[3].Value, CultureInfo.InvariantCulture);
            if (height <= 0 || thickness <= 0) return false;
            kind = m.Groups[1].Value.Trim();
            return true;
        }

        /// <summary>Quy cách cho HpProfileService.TryGetSection ("cao*dày").</summary>
        public static string HpSpec(double height, double thickness)
            => height.ToString("0.###", CultureInfo.InvariantCulture) + "*" + thickness.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Flat bar như block "BS 80x6" của CAS.dwt: gốc góc trên-trái, rộng t sang +X, cao h xuống −Y.</summary>
        public static List<(double, double)> FlatBarOutline(double height, double thickness)
            => new List<(double, double)> { (0, 0), (thickness, 0), (thickness, -height), (0, -height) };
    }
}
