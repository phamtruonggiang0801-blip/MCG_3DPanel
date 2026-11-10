// So sánh tờ Plan View do tool xuất với tờ làm tay (cùng panel, cùng toạ độ _Assy). Mọi khoảng cách in ra là mm GIẤY (/ tỉ lệ).
// Chạy: dotnet run compare.cs -- <manual.dump.json> <tool.dump.json> [scale=50]
using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;

var inv = CultureInfo.InvariantCulture;
string F(double d) => double.IsNaN(d) ? "-" : d.ToString("0.0", inv);
double sc = args.Length > 2 ? double.Parse(args[2], inv) : 50;
var man = Load(args[0]); var tool = Load(args[1]);

Console.WriteLine($"=== COUNTS (manual vs tool) per type+layer");
var keys = man.ms.Concat(tool.ms).Select(Key).Distinct().OrderBy(k => k);
foreach (var k in keys)
{
    int a = man.ms.Count(e => Key(e) == k), b = tool.ms.Count(e => Key(e) == k);
    Console.WriteLine($"  {k,-45} {a,5} {b,5} {(a == b ? "" : "  <-- " + (b - a).ToString("+0;-0"))}");
}

// ─── Chữ: multiset theo nội dung + lệch vị trí ───
Console.WriteLine("\n=== TEXT (Text/MText) by layer+string: manual / tool, position delta of nearest same-string (paper mm)");
foreach (var lay in new[] { "Mechanical-AM_1", "Mechanical-AM_6", "Mechanical-AM_0", "Mechanical-AM_9" })
{
    var mt = man.ms.Where(e => (S(e, "type") is "Text" or "MText") && S(e, "layer") == lay).ToList();
    var tt = tool.ms.Where(e => (S(e, "type") is "Text" or "MText") && S(e, "layer") == lay).ToList();
    if (mt.Count + tt.Count == 0) continue;
    Console.WriteLine($"-- {lay}: manual {mt.Count}, tool {tt.Count}");
    foreach (var g in mt.Select(T).Concat(tt.Select(T)).Distinct().OrderBy(x => x, StringComparer.Ordinal))
    {
        var a = mt.Where(e => T(e) == g).ToList(); var b = tt.Where(e => T(e) == g).ToList();
        string delta = "";
        if (a.Count > 0 && b.Count > 0)
        {
            var ds = a.Select(e => { var n = b.OrderBy(x => Dist(Pos(e), Pos(x))).First(); return (dx: (Pos(n).x - Pos(e).x) / sc, dy: (Pos(n).y - Pos(e).y) / sc); }).ToList();
            delta = $" dx[{F(ds.Min(d => d.dx))}..{F(ds.Max(d => d.dx))}] dy[{F(ds.Min(d => d.dy))}..{F(ds.Max(d => d.dy))}]";
            var ja = a.Select(e => S(e, "justify") + S(e, "attach")).Distinct(); var jb = b.Select(e => S(e, "justify") + S(e, "attach")).Distinct();
            delta += $" just m:{string.Join("/", ja)} t:{string.Join("/", jb)}";
        }
        if (a.Count != b.Count || delta.Length > 0 && (Math.Abs(0) > 0 || true))
            Console.WriteLine($"   '{Trunc(g, 40)}' {a.Count}/{b.Count}{delta}");
    }
}

// ─── Kích thước: ghép theo trục + khoảng (toạ độ tờ) ───
Console.WriteLine("\n=== DIMENSIONS by layer: matched by axis+interval; for matched: dim-line offset diff, text diff");
foreach (var lay in new[] { "Mechanical-AM_4", "Mechanical-AM_5" })
{
    var md = man.ms.Where(e => S(e, "type") == "Dim" && S(e, "layer") == lay).ToList();
    var td = tool.ms.Where(e => S(e, "type") == "Dim" && S(e, "layer") == lay).ToList();
    var used = new HashSet<int>(); var manualOnly = new List<string>(); var lineDiffs = new List<double>(); int textDiff = 0, styleDiff = 0;
    foreach (var e in md)
    {
        var iv = Iv(e); int hit = -1;
        for (int i = 0; i < td.Count; i++) if (!used.Contains(i) && iv != null && Iv(td[i]) is { } j && SameIv(iv.Value, j)) { hit = i; break; }
        if (hit < 0) { manualOnly.Add($"{F(D(e, "meas"))}{(S(e, "text") is var t && t != "" && t != "<>" ? "'" + Trunc(t, 18) + "'" : "")}@{Where(e)}"); continue; }
        used.Add(hit);
        var a = Line(e); var b = Line(td[hit]);
        if (a != null && b != null) lineDiffs.Add(Math.Round(((iv!.Value.horiz ? b.Value.y - a.Value.y : b.Value.x - a.Value.x)) / sc, 1));
        if (S(e, "text") != S(td[hit], "text")) textDiff++;
        if (S(e, "dimStyle") != S(td[hit], "dimStyle")) styleDiff++;
    }
    var toolOnly = td.Where((e, i) => !used.Contains(i)).Select(e => $"{F(D(e, "meas"))}@{Where(e)}").ToList();
    Console.WriteLine($"-- {lay}: manual {md.Count}, tool {td.Count}, matched {used.Count}; text differs {textDiff}, style differs {styleDiff}");
    Console.WriteLine($"   dim-line shift (tool-manual, paper mm, along normal): {string.Join(" ", lineDiffs.GroupBy(x => x).OrderByDescending(g => g.Count()).Take(12).Select(g => g.Key + "×" + g.Count()))}");
    Console.WriteLine($"   manual only ({manualOnly.Count}): {string.Join(" ", manualOnly.Take(40))}");
    Console.WriteLine($"   tool only ({toolOnly.Count}): {string.Join(" ", toolOnly.Take(40))}");
    var mo = md.Where(e => e.TryGetProperty("overrides", out _)).SelectMany(e => e.GetProperty("overrides").EnumerateObject().Select(p => p.Name + "=" + p.Value)).GroupBy(x => x).Select(g => g.Key + "×" + g.Count());
    var to = td.Where(e => e.TryGetProperty("overrides", out _)).SelectMany(e => e.GetProperty("overrides").EnumerateObject().Select(p => p.Name + "=" + p.Value)).GroupBy(x => x).Select(g => g.Key + "×" + g.Count());
    Console.WriteLine($"   overrides manual: {string.Join(", ", mo)}\n   overrides tool:   {string.Join(", ", to)}");
}

// ─── Block ref: theo tên, ghép gần nhất ───
Console.WriteLine("\n=== BLOCK REFS by name: manual/tool, nearest-match offsets (paper mm), rot/scale differences");
var names = man.ms.Concat(tool.ms).Where(e => S(e, "type") == "BlockRef").Select(e => Norm(S(e, "name"))).Distinct().OrderBy(x => x);
foreach (var n in names)
{
    var a = man.ms.Where(e => S(e, "type") == "BlockRef" && Norm(S(e, "name")) == n).ToList();
    var b = tool.ms.Where(e => S(e, "type") == "BlockRef" && Norm(S(e, "name")) == n).ToList();
    string info = "";
    if (a.Count > 0 && b.Count > 0)
    {
        var ds = a.Select(e => { var m = b.OrderBy(x => Dist(Pos(e), Pos(x))).First(); return (d: Dist(Pos(e), Pos(m)) / sc, dx: (Pos(m).x - Pos(e).x) / sc, dy: (Pos(m).y - Pos(e).y) / sc, rot: Math.Round((D(m, "rot") - D(e, "rot")) * 180 / Math.PI), sx: $"{F(D(e, "sx"))}/{F(D(m, "sx"))}"); }).ToList();
        int close = ds.Count(d => d.d < 2);
        info = $" within2mm {close}/{a.Count}; dx[{F(ds.Min(d => d.dx))}..{F(ds.Max(d => d.dx))}] dy[{F(ds.Min(d => d.dy))}..{F(ds.Max(d => d.dy))}] drot {string.Join(",", ds.Select(d => d.rot).Distinct())} sx m/t {string.Join(",", ds.Select(d => d.sx).Distinct().Take(4))}";
    }
    Console.WriteLine($"  {n,-34} {a.Count,4}/{b.Count,-4}{info}");
}

// ─── Mặt cắt: nội dung block ───
Console.WriteLine("\n=== SECTIONS (block content)");
foreach (var (mn, tn) in new[] { ("7D-17P_X-X", "7D-17P_X-X_MCG"), ("7D-17P_Y-Y", "7D-17P_Y-Y_MCG") })
{
    foreach (var (lbl, db, nm) in new[] { ("manual", man, mn), ("tool", tool, tn) })
    {
        if (!db.blocks.TryGetValue(nm, out var blk)) { Console.WriteLine($"  {lbl} {nm}: missing"); continue; }
        var ents = blk.GetProperty("entities").EnumerateArray().ToList();
        var polys = ents.Where(e => S(e, "type") == "Polyline").Select(e => { var v = e.GetProperty("vertices").EnumerateArray().Select(p => (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble())).ToList(); return $"[{F(v.Min(p => p.Item1))},{F(v.Min(p => p.Item2))}..{F(v.Max(p => p.Item1))},{F(v.Max(p => p.Item2))}]{S(e, "layer").Replace("Mechanical-", "")}"; });
        var refs = ents.Where(e => S(e, "type") == "BlockRef").Select(e => $"{S(e, "name")}@{F(D(e, "x"))},{F(D(e, "y"))}{(D(e, "sx") < 0 ? "m" : "")}");
        Console.WriteLine($"  {lbl} {nm}: {ents.Count} ents; polys: {string.Join(" ", polys)}");
        Console.WriteLine($"     refs: {string.Join(" ", refs)}");
    }
}

// ─── MText chi tiết ───
Console.WriteLine("\n=== MTEXT details");
foreach (var (lbl, db) in new[] { ("manual", man), ("tool", tool) })
    foreach (var e in db.ms.Where(e => S(e, "type") == "MText"))
        Console.WriteLine($"  {lbl} {S(e, "layer").Replace("Mechanical-", "")} @{F(D(e, "x") / sc)},{F(D(e, "y") / sc)} h={F(D(e, "h"))} w={F(D(e, "width") / sc)} {S(e, "attach")} '{Trunc(S(e, "contents"), 160)}'");

// ─────────────────────────────────────────────────────────────
static (List<JsonElement> ms, Dictionary<string, JsonElement> blocks, JsonDocument doc) Load(string p)
{
    var doc = JsonDocument.Parse(File.ReadAllText(p).TrimStart((char)0xFEFF));
    var blocks = doc.RootElement.GetProperty("blocks").EnumerateArray().ToDictionary(b => S(b, "block"), b => b);
    var ms = blocks.Values.First(b => S(b, "isModelSpace") is "True" or "true").GetProperty("entities").EnumerateArray().ToList();
    return (ms, blocks, doc);
}
static string Key(JsonElement e) => S(e, "type") + " " + S(e, "layer").Replace("Mechanical-", "") + (S(e, "type") == "Dim" ? " " + S(e, "dimStyle") : "");
static string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString()! : v.ToString()) : "";
static double D(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
static (double x, double y)? P(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Array ? (v[0].GetDouble(), v[1].GetDouble()) : null;
static string T(JsonElement e) => (S(e, "type") == "MText" ? S(e, "text") : S(e, "text")).Replace("\r", " ").Replace("\n", " ").Trim();
static (double x, double y) Pos(JsonElement e) => S(e, "justify") != "" && !double.IsNaN(D(e, "alignX")) ? (D(e, "alignX"), D(e, "alignY")) : (D(e, "x"), D(e, "y"));
static double Dist((double x, double y) a, (double x, double y) b) => Math.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y));
static string Trunc(string s, int n) => s.Length > n ? s[..n] + "…" : s;
static string Norm(string n) => n.EndsWith("_MCG") ? n[..^4] + "(_MCG)" : n;
static (double lo, double hi, bool horiz)? Iv(JsonElement e)
{
    var a = P(e, "p1"); var b = P(e, "p2"); double r = D(e, "rot");
    if (a == null || b == null) return null;
    if (double.IsNaN(r)) r = Math.Atan2(b.Value.y - a.Value.y, b.Value.x - a.Value.x);
    bool h = Math.Abs(Math.Sin(r)) < 0.01;
    double pa = h ? a.Value.x : a.Value.y, pb = h ? b.Value.x : b.Value.y;
    return (Math.Min(pa, pb), Math.Max(pa, pb), h);
}
static bool SameIv((double lo, double hi, bool horiz) a, (double lo, double hi, bool horiz) b) => a.horiz == b.horiz && Math.Abs(a.lo - b.lo) < 1 && Math.Abs(a.hi - b.hi) < 1;
static (double x, double y)? Line(JsonElement e) => P(e, "dimLine");
static string Where(JsonElement e) { var l = P(e, "dimLine") ?? P(e, "textPos"); return l == null ? "?" : $"({(l.Value.x / 50):0},{(l.Value.y / 50):0})"; }
