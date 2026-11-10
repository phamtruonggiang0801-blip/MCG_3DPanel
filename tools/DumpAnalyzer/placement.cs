// Đo quy tắc đặt nhãn / kích thước / hình chiếu trên tờ Plan View từ file .dump.json (dumpVersion 2).
// Chạy: dotnet run placement.cs -- <dump1.json> <dump2.json> ...   (mọi giá trị in ra là mm GIẤY = model / tỉ lệ)
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

var inv = CultureInfo.InvariantCulture;
string F(double d) => double.IsNaN(d) ? "-" : d.ToString("0.0", inv);
var rows = new Dictionary<string, List<(string dwg, double v)>>();
void Rec(string rule, string dwg, double v) { if (!rows.TryGetValue(rule, out var l)) rows[rule] = l = new(); l.Add((dwg, v)); }

foreach (var path in args)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path).TrimStart((char)0xFEFF));
    var root = doc.RootElement;
    string dwg = Path.GetFileNameWithoutExtension(path).Replace(".dump", "");
    var blocks = root.GetProperty("blocks").EnumerateArray().ToDictionary(b => S(b, "block"), b => b);
    var ms = blocks.Values.First(b => S(b, "isModelSpace") is "True" or "true");
    var msEnts = ms.GetProperty("entities").EnumerateArray().ToList();

    var assy = msEnts.First(e => S(e, "type") == "BlockRef" && S(e, "name").EndsWith("_Assy") && S(e, "layer").EndsWith("AM_2"));
    string panel = S(assy, "name")[..^"_Assy".Length];
    var assyX = Xf.Of(assy);

    // Khung A1 chứa điểm chèn Assy ⇒ tỉ lệ + gốc khung
    var frame = msEnts.Where(e => S(e, "type") == "BlockRef" && S(e, "name") == "A1")
        .Select(e => (x: D(e, "x"), y: D(e, "y"), s: Math.Abs(D(e, "sx"))))
        .Where(f => assyX.Tx >= f.x && assyX.Tx <= f.x + 841 * f.s && assyX.Ty >= f.y && assyX.Ty <= f.y + 594 * f.s)
        .OrderBy(f => f.s).First();
    double sc = frame.s;

    // Top Plate = polyline diện tích lớn nhất trong <panel>_Top Plate (chèn trong Assy)
    var tpRef = blocks[panel + "_Assy"].GetProperty("entities").EnumerateArray().First(e => S(e, "name") == panel + "_Top Plate");
    var tpX = assyX.Then(Xf.Of(tpRef));
    var tp = Bbox(LargestPoly(blocks[panel + "_Top Plate"]).Select(tpX.Apply));

    // Member trong Structure (đổi ra MS): polyline kín, dài
    var stRef = blocks[panel + "_Assy"].GetProperty("entities").EnumerateArray().First(e => S(e, "name") == panel + "_Structure");
    var stX = assyX.Then(Xf.Of(stRef));
    var members = blocks[panel + "_Structure"].GetProperty("entities").EnumerateArray()
        .Where(e => S(e, "type") == "Polyline")
        .Select(e => (layer: S(e, "layer"), bb: Bbox(Verts(e).Select(stX.Apply))))
        .Where(m => Math.Max(m.bb.w, m.bb.h) > 300).ToList();

    // 1. Nhãn: vị trí so với member gần nhất cùng phương
    foreach (var t in msEnts.Where(e => S(e, "type") == "Text"))
    {
        string txt = S(t, "text");
        bool girder = Regex.IsMatch(txt, @"^[TL]\d+[a-z]?$"), stiff = Regex.IsMatch(txt, @"^(HT|HL)\d+$|^BS$");
        if (!girder && !stiff) continue;
        double x = D(t, "x"), y = D(t, "y");
        bool wantT = txt.StartsWith("T") || txt.StartsWith("HT");   // T: chạy theo Y (đứng)
        var cand = members.Where(m => wantT ? m.bb.h > m.bb.w : m.bb.w > m.bb.h)
            .Where(m => girder ? m.layer.EndsWith("AM_0") : !m.layer.EndsWith("AM_0"))
            .OrderBy(m => wantT ? Math.Abs(m.bb.cx - x) : Math.Abs(m.bb.cy - y)).FirstOrDefault();
        if (cand.layer == null) continue;
        string k = girder ? "girder" : "stiffener";
        double perp = wantT ? x - cand.bb.cx : y - cand.bb.cy;                 // + = phải (T) / trên (L)
        double alongFromEnd = wantT ? y - cand.bb.maxY : x - cand.bb.minX;     // T: so với đầu TRÊN; L: so với đầu TRÁI
        double toPlateEdge = wantT ? y - tp.maxY : x - tp.minX;
        Rec($"label.{k}.{(wantT ? "T" : "L")}.perpFromCenterline", dwg, perp / sc);
        Rec($"label.{k}.{(wantT ? "T" : "L")}.fromMemberEnd(T:top,L:left)", dwg, alongFromEnd / sc);
        Rec($"label.{k}.{(wantT ? "T" : "L")}.fromPlateEdge(T:top,L:left)", dwg, toPlateEdge / sc);
        Rec($"label.{k}.{(wantT ? "T" : "L")}.rotDeg", dwg, D(t, "rot") * 180 / Math.PI);
    }

    // 2. Chuỗi kích thước: khoảng cách đường kích thước tới mép Top Plate, theo phía
    foreach (var lay in new[] { "AM_4", "AM_5" })
    {
        var offs = new Dictionary<string, SortedSet<double>>();
        foreach (var d in msEnts.Where(e => S(e, "type") == "Dim" && S(e, "layer").EndsWith(lay) && S(e, "dimType") == "RotatedDimension"))
        {
            var dl = P(d, "dimLine"); if (dl == null) continue;
            double r = D(d, "rot"); bool horiz = Math.Abs(Math.Sin(r)) < 0.01;
            string side; double off;
            if (horiz) { if (dl.Value.y > tp.maxY) { side = "top"; off = dl.Value.y - tp.maxY; } else if (dl.Value.y < tp.minY) { side = "bottom"; off = tp.minY - dl.Value.y; } else continue; }
            else { if (dl.Value.x < tp.minX) { side = "left"; off = tp.minX - dl.Value.x; } else if (dl.Value.x > tp.maxX) { side = "right"; off = dl.Value.x - tp.maxX; } else continue; }
            // chỉ kích thước của mặt bằng chính (bỏ mặt bằng lỗ lashing bên phải, mặt cắt)
            if (horiz && (dl.Value.x < tp.minX - 50 || dl.Value.x > tp.maxX + 50)) continue;
            if (!horiz && (dl.Value.y < tp.minY - 50 || dl.Value.y > tp.maxY + 50)) continue;
            if (off / sc > 60) continue;
            if (!offs.TryGetValue(side, out var set)) offs[side] = set = new SortedSet<double>();
            set.Add(Math.Round(off / sc, 1));
        }
        foreach (var (side, set) in offs)
        {
            var l = set.ToList();
            // gộp các offset gần nhau (< 1 mm giấy) = cùng 1 chuỗi
            var chains = new List<double>(); foreach (var v in l) if (chains.Count == 0 || v - chains[^1] > 1) chains.Add(v);
            Rec($"dim.{lay}.{side}.firstChain", dwg, chains[0]);
            for (int i = 1; i < chains.Count; i++) Rec($"dim.{lay}.{side}.chainSpacing", dwg, chains[i] - chains[i - 1]);
            Rec($"dim.{lay}.{side}.chainCount", dwg, chains.Count);
        }
    }

    // 3. Mặt cắt / mặt bằng lỗ lashing / M.D / tiêu đề / member list / ghi chú dung sai
    var xx = msEnts.FirstOrDefault(e => S(e, "type") == "BlockRef" && Regex.IsMatch(S(e, "name"), @"_X-X$"));
    var yy = msEnts.FirstOrDefault(e => S(e, "type") == "BlockRef" && Regex.IsMatch(S(e, "name"), @"_Y-Y$"));
    if (xx.ValueKind != JsonValueKind.Undefined)
    {
        Rec("section.X.insertAbovePlateTop", dwg, (D(xx, "y") - tp.maxY) / sc);
        Rec("section.X.insertXminusPlateMinX", dwg, (D(xx, "x") - tp.minX) / sc);
    }
    if (yy.ValueKind != JsonValueKind.Undefined)
    {
        Rec("section.Y.plateMinXminusInsertX", dwg, (tp.minX - D(yy, "x")) / sc);
        Rec("section.Y.insertYminusPlateMinY", dwg, (D(yy, "y") - tp.minY) / sc);
        Rec("section.Y.rotDeg", dwg, D(yy, "rot") * 180 / Math.PI);
    }
    var lashTp = msEnts.Where(e => S(e, "type") == "BlockRef" && S(e, "name") == panel + "_Top Plate").ToList();
    if (lashTp.Count > 0)
    {
        var lx = Xf.Of(lashTp[0]);
        var ltp = Bbox(LargestPoly(blocks[panel + "_Top Plate"]).Select(lx.Apply));
        Rec("lashing.gapPlateToPlate", dwg, (ltp.minX - tp.maxX) / sc);
        Rec("lashing.dyPlateMinY", dwg, (ltp.minY - tp.minY) / sc);
        foreach (var mdName in new[] { "M.D1", "M.D2" })
        {
            var md = msEnts.FirstOrDefault(e => S(e, "type") == "BlockRef" && S(e, "name") == mdName);
            if (md.ValueKind == JsonValueKind.Undefined) continue;
            Rec($"{mdName}.xFromLashPlateMinX", dwg, (D(md, "x") - ltp.minX) / sc);
            Rec($"{mdName}.yFromLashPlateMaxY", dwg, (D(md, "y") - ltp.maxY) / sc);
        }
        foreach (var t in msEnts.Where(e => S(e, "type") == "MText" && S(e, "text").StartsWith("LASHING HOLES PLAN")))
        { Rec("title.lashing.xFromLashPlateMinX", dwg, (D(t, "x") - ltp.minX) / sc); Rec("title.lashing.yFromLashPlateMinY", dwg, (D(t, "y") - ltp.minY) / sc); Rec("title.lashing.attach", dwg, AttachCode(S(t, "attach"))); }
    }
    foreach (var t in msEnts.Where(e => S(e, "type") == "MText" && S(e, "text").StartsWith("PLAN VIEW")))
    { Rec("title.plan.xFromPlateMinX", dwg, (D(t, "x") - tp.minX) / sc); Rec("title.plan.yFromPlateMinY", dwg, (D(t, "y") - tp.minY) / sc); Rec("title.plan.attach", dwg, AttachCode(S(t, "attach"))); Rec("title.plan.widthPaper", dwg, D(t, "width") / sc); }
    foreach (var t in msEnts.Where(e => S(e, "type") == "MText" && S(e, "text").StartsWith("TOP PLATE THICKNESS")))
    { Rec("memberList.xFromFrame", dwg, (D(t, "x") - frame.x) / sc); Rec("memberList.yFromFrame", dwg, (D(t, "y") - frame.y) / sc); Rec("memberList.widthPaper", dwg, D(t, "width") / sc); Rec("memberList.attach", dwg, AttachCode(S(t, "attach"))); }
    var tol = msEnts.FirstOrDefault(e => S(e, "type") == "BlockRef" && S(e, "name") == "Measurements_Tolerances vFuture");
    if (tol.ValueKind != JsonValueKind.Undefined) { Rec("tolNote.xFromFrame", dwg, (D(tol, "x") - frame.x) / sc); Rec("tolNote.yFromFrame", dwg, (D(tol, "y") - frame.y) / sc); }
    Rec("scale", dwg, sc);
    Console.Error.WriteLine($"{dwg}: panel {panel}, scale {sc}, plate [{F(tp.minX)},{F(tp.minY)}]-[{F(tp.maxX)},{F(tp.maxY)}], members {members.Count}");
}

foreach (var (rule, l) in rows.OrderBy(k => k.Key))
{
    var vs = l.Select(p => p.v).OrderBy(v => v).ToList();
    double med = vs[vs.Count / 2];
    var outl = l.Where(p => Math.Abs(p.v - med) > 2).Select(p => p.dwg).Distinct();
    Console.WriteLine($"{rule,-48} n={l.Count,4} median={F(med),8} min={F(vs[0]),8} max={F(vs[^1]),8}  off>2: {string.Join(",", outl)}");
}

static double AttachCode(string a) => a switch { "TopLeft" => 1, "TopCenter" => 2, "TopRight" => 3, "MiddleLeft" => 4, "MiddleCenter" => 5, "BottomLeft" => 7, "BottomCenter" => 8, _ => 0 };
static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString()) : "";
static double D(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
static (double x, double y)? P(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? (v[0].GetDouble(), v[1].GetDouble()) : null;
static IEnumerable<(double x, double y)> Verts(JsonElement pl) => pl.GetProperty("vertices").EnumerateArray().Select(v => (v.GetProperty("x").GetDouble(), v.GetProperty("y").GetDouble()));
static List<(double x, double y)> LargestPoly(JsonElement blk) => blk.GetProperty("entities").EnumerateArray()
    .Where(e => S(e, "type") == "Polyline").Select(e => Verts(e).ToList())
    .OrderByDescending(v => { var b = Bbox(v); return b.w * b.h; }).First();
static (double minX, double minY, double maxX, double maxY, double w, double h, double cx, double cy) Bbox(IEnumerable<(double x, double y)> pts)
{
    var l = pts.ToList(); double a = l.Min(p => p.x), b = l.Min(p => p.y), c = l.Max(p => p.x), d = l.Max(p => p.y);
    return (a, b, c, d, c - a, d - b, (a + c) / 2, (b + d) / 2);
}

/// <summary>Phép biến đổi affine 2D của 1 block reference (chèn, xoay, scale).</summary>
record Xf(double A, double B, double C, double Dd, double Tx, double Ty)
{
    public static Xf Of(JsonElement br)
    {
        double r = br.GetProperty("rot").GetDouble(), sx = br.GetProperty("sx").GetDouble(), sy = br.GetProperty("sy").GetDouble();
        return new Xf(Math.Cos(r) * sx, -Math.Sin(r) * sy, Math.Sin(r) * sx, Math.Cos(r) * sy, br.GetProperty("x").GetDouble(), br.GetProperty("y").GetDouble());
    }
    public (double x, double y) Apply((double x, double y) p) => (A * p.x + B * p.y + Tx, C * p.x + Dd * p.y + Ty);
    /// <summary>this ∘ inner: áp inner trước rồi tới this.</summary>
    public Xf Then(Xf inner) => new Xf(A * inner.A + B * inner.C, A * inner.B + B * inner.Dd, C * inner.A + Dd * inner.C, C * inner.B + Dd * inner.Dd,
        A * inner.Tx + B * inner.Ty + Tx, C * inner.Tx + Dd * inner.Ty + Ty);
}
