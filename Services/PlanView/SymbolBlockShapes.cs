using System.Collections.Generic;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>1 nét trong định nghĩa block. Kind "Line" (2 điểm) | "Polyline" (kín) | "Solid" (4 điểm) | "MText" | "BlockRef".
    /// Color 256 = ByLayer.</summary>
    public sealed class ShapeItem
    {
        public string Kind { get; set; }
        public string Layer { get; set; }
        public int Color { get; set; } = 256;
        public List<(double, double)> Points { get; set; } = new List<(double, double)>();
        public string Text { get; set; }
        public double Height { get; set; }
        public double Width { get; set; }
        public string Attach { get; set; } = "TopLeft";
        public double Rotation { get; set; }
        public string Block { get; set; }
        public double LtScale { get; set; } = 1;
    }

    public sealed class LayerSpec
    {
        public string Name { get; set; }
        public short Color { get; set; }
        public string Linetype { get; set; }
        /// <summary>Lineweight theo 1/100 mm (35 = 0.35 mm).</summary>
        public int LineweightHundredths { get; set; }
    }

    /// <summary>Block ký hiệu tờ Plan View mà tool tự vẽ khi bản vẽ 2D lẫn CAS.dwt đều không có (kỹ sư 2026-11-10).
    /// Hình học, layer, màu và chữ chép nguyên từ dump CAS-0062242 (7D-17P, dumpVersion 2).</summary>
    public static class SymbolBlockShapes
    {
        private static readonly Dictionary<string, List<ShapeItem>> Catalog = new Dictionary<string, List<ShapeItem>>
        {
            ["CS_symbol"] = new List<ShapeItem>
            {
                L3(-220.5457, 0, 220.5457, 0), L3(99.9619, -49.9475, 220.5458, 0), L3(99.9619, 49.9474, 99.9619, -49.9475),
                S3((99.9619, 49.9474), (99.9619, -49.9475), (220.5454, -0.0002), (99.9619, 49.9474)),
                L3(-99.9619, 49.9474, -99.9619, -49.9475),
                S3((-99.9619, 49.9474), (-99.9619, -49.9475), (-220.5454, -0.0002), (-99.9619, 49.9474)),
            },
            ["CS_horl_up_right"] = new List<ShapeItem>
            {
                new ShapeItem { Kind = "BlockRef", Layer = "Mechanical-AM_0", Block = "CS_symbol", Rotation = 3.14159265358979 },
            },
            ["thk_sym"] = new List<ShapeItem>
            {
                L("Mechanical-AM_4", 37.5, 301.7553, 37.5, 151.7553, 0.5), L("Mechanical-AM_4", 37.5, 235.0068, 0, 212.5097, 0.5),
                L("Mechanical-AM_4", 37.5, 200.5097, 0, 178.0068, 0.5), L("Mechanical-AM_4", 37.5, 270.2582, 0, 247.7553, 0.5),
                L("Mechanical-AM_4", 0, 301.7553, 0, 43.9792, 0.5),
            },
            ["Rec. for tolerance"] = new List<ShapeItem>
            {
                P("Mechanical-AM_8", 0.5, (-19.9995, -1.0149), (20.0005, -1.0149), (20.0005, -9.0149), (-19.9995, -9.0149)),
            },
            ["M.D1"] = Diagonal("D1"),
            ["M.D2"] = Diagonal("D2"),
            ["Measurements_Tolerances vFuture"] = new List<ShapeItem>
            {
                new ShapeItem
                {
                    Kind = "MText", Layer = "Mechanical-AM_6", Points = { (-34.9116, -82.1469) }, Height = 150, Width = 0, Attach = "TopLeft",
                    Text = "\\pxi-3,l4,sm1,t4;U.N.O.\t- UNLESS NOTED OTHERWISE{\\L\\P\\pi0,l0,tz;\\P}MEASUREMENTS IN BOXES TO BE CHECKED AFTER MANUFACTURING" +
                           "\\P\\PLOCAL DEFORMATIONS AT TOP PLATE FIELDS TO BE CHECKED FOLLOWING\\PSTD-1219 - DEFORMATIONS AT PLATE PANELS" +
                           "\\PCHECKED AND SIGNED BY/DATE:\\P\\POVERALL FLATNESS/DEFLECTION TO BE CHECKED FOLLOWING\\PSTD-1172 - OVERALL FLATNESS" +
                           "\\PSEE SPECIFIC TOLERANCE IN DRAWING \"NOTES & COMMON DET.\"\\PMEASURES TO BE TAKEN AT BEAM CROSSINGS" +
                           "\\PCHECKED AND SIGNED BY/DATE:\\P\\PHOGGING TO BE CHECKED FOLLOWING TOL. IN \"NOTES & COMMON DET.\"" +
                           "\\PCHECKED AND SIGNED BY/DATE:\\P\\PCOPY OF MEASURMENT PROTOCOL TO BE RETURNED TO MacGREGOR",
                },
                L("Mechanical-AM_6", 2880.8873, -1881.2991, 4920.9993, -1881.2991, 1),
                L("Mechanical-AM_6", 2880.8873, -4139.2462, 4920.9993, -4139.2462, 1),
                L("Mechanical-AM_6", 2880.8873, -3387.6085, 4920.9993, -3387.6085, 1),
            },
        };

        /// <summary>Thuộc tính layer theo CAS.dwt — tạo khi bản vẽ thiếu (lựa chọn (a) của kỹ sư).</summary>
        private static readonly Dictionary<string, LayerSpec> Layers = new Dictionary<string, LayerSpec>
        {
            ["Mechanical-AM_0"] = new LayerSpec { Name = "Mechanical-AM_0", Color = 7, Linetype = "Continuous", LineweightHundredths = 40 },
            ["Mechanical-AM_3"] = new LayerSpec { Name = "Mechanical-AM_3", Color = 6, Linetype = "AM_ISO02W050", LineweightHundredths = 35 },
            ["Mechanical-AM_4"] = new LayerSpec { Name = "Mechanical-AM_4", Color = 3, Linetype = "Continuous", LineweightHundredths = 25 },
            ["Mechanical-AM_6"] = new LayerSpec { Name = "Mechanical-AM_6", Color = 2, Linetype = "Continuous", LineweightHundredths = 35 },
            ["Mechanical-AM_8"] = new LayerSpec { Name = "Mechanical-AM_8", Color = 1, Linetype = "Continuous", LineweightHundredths = 25 },
            ["Mechanical-AM_7"] = new LayerSpec { Name = "Mechanical-AM_7", Color = 4, Linetype = "AM_ISO08W050", LineweightHundredths = 25 },
            ["posb"] = new LayerSpec { Name = "posb", Color = 7, Linetype = "Continuous", LineweightHundredths = -3 },   // ByLineWeightDefault
        };

        public static bool Has(string block) => block != null && Catalog.ContainsKey(block);
        public static IReadOnlyList<ShapeItem> Get(string block) => Catalog[block];
        public static LayerSpec Layer(string name) => name != null && Layers.TryGetValue(name, out var l) ? l : null;

        /// <summary>Block lồng phải có trước (CS_horl_up_right chèn CS_symbol).</summary>
        public static IReadOnlyList<string> Dependencies(string block)
        {
            var r = new List<string>();
            foreach (var s in Catalog[block]) if (s.Kind == "BlockRef" && !r.Contains(s.Block)) r.Add(s.Block);
            return r;
        }

        private static List<ShapeItem> Diagonal(string label) => new List<ShapeItem>
        {
            new ShapeItem { Kind = "MText", Layer = "Mechanical-AM_6", Points = { (494.8139, -56.7488) }, Text = label, Height = 165, Width = 202.4789, LtScale = 0.5 },
            P("Mechanical-AM_4", 0.5, (0, 0), (1125, 0), (1125, -250), (0, -250)),
            P("Mechanical-AM_4", 0.5, (0, -250), (1125, -250), (1125, -500), (0, -500)),
            P("Mechanical-AM_4", 0.5, (0, -500), (1125, -500), (1125, -750), (0, -750)),
            new ShapeItem { Kind = "MText", Layer = "Mechanical-AM_6", Points = { (181.5587, -300.629) }, Text = "D1-D2<10", Height = 165, Width = 202.4789, LtScale = 0.5 },
            L("Mechanical-AM_6", 148.702, -305.0727, 148.702, -455.0727, 0.5),
            L("Mechanical-AM_6", 732.1271, -305.0727, 732.1271, -455.0727, 0.5),
        };

        private static ShapeItem L(string layer, double x0, double y0, double x1, double y1, double lts)
            => new ShapeItem { Kind = "Line", Layer = layer, Points = { (x0, y0), (x1, y1) }, LtScale = lts };
        private static ShapeItem L3(double x0, double y0, double x1, double y1)
            => new ShapeItem { Kind = "Line", Layer = "Mechanical-AM_3", Color = 32, Points = { (x0, y0), (x1, y1) } };
        private static ShapeItem S3(params (double, double)[] p)
            => new ShapeItem { Kind = "Solid", Layer = "Mechanical-AM_3", Color = 32, Points = new List<(double, double)>(p) };
        private static ShapeItem P(string layer, double lts, params (double, double)[] p)
            => new ShapeItem { Kind = "Polyline", Layer = layer, Points = new List<(double, double)>(p), LtScale = lts };
    }
}
