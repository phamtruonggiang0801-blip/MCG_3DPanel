// Phân tích file .dump.json (dumpVersion 2) — tóm tắt style và nội dung Model Space.
using System.Text.Json;
using System.Globalization;

var inv = CultureInfo.InvariantCulture;
string F(double d) => d.ToString("0.##", inv);
string S(JsonElement e, string k) => e.TryGetProperty(k, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() : v.ToString()) : "";
double D(JsonElement e, string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;

foreach (var path in args)
{
    using var doc = JsonDocument.Parse(File.ReadAllText(path).TrimStart((char)0xFEFF));
    var root = doc.RootElement;
    Console.WriteLine($"\n################ {S(root, "drawing")}");
    var blocks = root.GetProperty("blocks").EnumerateArray().ToList();
    var byName = blocks.ToDictionary(b => S(b, "block"), b => b);

    // Style ĐANG DÙNG trong Model Space + mọi block
    var usedDim = new Dictionary<string, int>(); var usedTxt = new Dictionary<string, int>(); var usedLayer = new Dictionary<string, int>();
    void Inc(Dictionary<string, int> d, string k) { d[k] = d.TryGetValue(k, out var n) ? n + 1 : 1; }
    foreach (var b in blocks)
        foreach (var e in b.GetProperty("entities").EnumerateArray())
        {
            if (S(e, "dimStyle") != "") Inc(usedDim, S(e, "dimStyle"));
            if (S(e, "style") != "" && S(e, "type") is "Text" or "MText") Inc(usedTxt, S(e, "style"));
        }

    var t = root.GetProperty("tables");
    Console.WriteLine("sysvars: " + t.GetProperty("sysvars").ToString().Replace("\n", "").Replace("  ", ""));
    Console.WriteLine("dimStyles used: " + string.Join(", ", usedDim.Select(k => $"{k.Key}×{k.Value}")));
    foreach (var ds in t.GetProperty("dimStyles").EnumerateArray())
    {
        if (!usedDim.ContainsKey(S(ds, "name")) && S(ds, "current") != "True" && S(ds, "current") != "true") continue;
        Console.WriteLine($"  DS {S(ds, "name")}: SCALE={S(ds, "DIMSCALE")} TXT={S(ds, "DIMTXT")} TXSTY={S(ds, "DIMTXSTY")} ASZ={S(ds, "DIMASZ")} BLK={S(ds, "DIMBLK")}/{S(ds, "DIMBLK1")}/{S(ds, "DIMBLK2")} EXO={S(ds, "DIMEXO")} EXE={S(ds, "DIMEXE")} GAP={S(ds, "DIMGAP")} TAD={S(ds, "DIMTAD")} TIH={S(ds, "DIMTIH")} TOH={S(ds, "DIMTOH")} DEC={S(ds, "DIMDEC")} RND={S(ds, "DIMRND")} ZIN={S(ds, "DIMZIN")} DSEP={S(ds, "DIMDSEP")} CLRD/E/T={S(ds, "DIMCLRD")}/{S(ds, "DIMCLRE")}/{S(ds, "DIMCLRT")} DLI={S(ds, "DIMDLI")} FXL={S(ds, "DIMFXLON")}:{S(ds, "DIMFXL")} TMOVE={S(ds, "DIMTMOVE")} ATFIT={S(ds, "DIMATFIT")}");
    }
    Console.WriteLine("textStyles used: " + string.Join(", ", usedTxt.Select(k => $"{k.Key}×{k.Value}")));
    foreach (var ts in t.GetProperty("textStyles").EnumerateArray())
        if (usedTxt.ContainsKey(S(ts, "name")) || usedDim.Count > 0 && false)
            Console.WriteLine($"  TS {S(ts, "name")}: font={S(ts, "font")} face={S(ts, "typeFace")} h={S(ts, "height")} w={S(ts, "widthFactor")}");
    Console.WriteLine("layers: " + string.Join(" | ", t.GetProperty("layers").EnumerateArray()
        .Where(l => S(l, "name").StartsWith("Mechanical-AM") || S(l, "name") == "0")
        .Select(l => $"{S(l, "name").Replace("Mechanical-", "")} c{S(l, "color")} {S(l, "linetype")} {S(l, "lineweight").Replace("LineWeight", "")}{(S(l, "frozen") == "True" || S(l, "frozen") == "true" ? " FROZEN" : "")}{(S(l, "plot") == "False" || S(l, "plot") == "false" ? " NOPLOT" : "")}")));

    // Model Space
    var ms = blocks.First(b => S(b, "isModelSpace") is "True" or "true");
    var ents = ms.GetProperty("entities").EnumerateArray().ToList();
    Console.WriteLine($"MODEL SPACE: {ents.Count} entities");
    foreach (var g in ents.GroupBy(e => S(e, "type") + " " + S(e, "layer").Replace("Mechanical-", "")).OrderBy(g => g.Key))
        Console.Write($"{g.Key}×{g.Count()}; ");
    Console.WriteLine();

    // Block ref trong MS (không phải ký hiệu lặp)
    Console.WriteLine("BlockRefs in MS:");
    foreach (var g in ents.Where(e => S(e, "type") == "BlockRef").GroupBy(e => S(e, "name")).OrderBy(g => g.Key))
    {
        var f = g.First();
        string ext = "";
        Console.WriteLine($"  {g.Key} ×{g.Count()}  first@({F(D(f, "x"))},{F(D(f, "y"))}) rot={F(D(f, "rot"))} s={F(D(f, "sx"))} L={S(f, "layer").Replace("Mechanical-", "")}{ext}");
    }

    // Text trong MS
    Console.WriteLine("Texts in MS (layer/style/h → samples):");
    foreach (var g in ents.Where(e => S(e, "type") is "Text" or "MText").GroupBy(e => $"{S(e, "type")} {S(e, "layer").Replace("Mechanical-", "")} {S(e, "style")} h={F(D(e, "h"))}").OrderBy(g => g.Key))
    {
        var samples = g.Select(e => S(e, "text").Replace("\r", " ").Replace("\n", " ")).Distinct().Take(12).Select(s => s.Length > 60 ? s[..60] + "…" : s);
        Console.WriteLine($"  {g.Key} ×{g.Count()}: {string.Join(" | ", samples)}");
    }

    // Dim trong MS
    Console.WriteLine("Dims in MS:");
    foreach (var g in ents.Where(e => S(e, "type") == "Dim").GroupBy(e => $"{S(e, "layer").Replace("Mechanical-", "")} {S(e, "dimStyle")} {S(e, "dimType")}").OrderBy(g => g.Key))
    {
        var ov = g.Where(e => e.TryGetProperty("overrides", out _)).SelectMany(e => e.GetProperty("overrides").EnumerateObject().Select(p => $"{p.Name}={p.Value}")).GroupBy(x => x).Select(x => $"{x.Key}×{x.Count()}");
        var txt = g.Select(e => S(e, "text")).Where(x => x != "" && x != "<>").GroupBy(x => x).Select(x => $"\"{x.Key}\"×{x.Count()}");
        var rots = g.Select(e => F(D(e, "rot"))).GroupBy(x => x).Select(x => $"rot{x.Key}×{x.Count()}");
        Console.WriteLine($"  {g.Key} ×{g.Count()} {string.Join(" ", rots)}\n     overrides: {string.Join(", ", ov)}\n     texts: {string.Join(", ", txt)}");
        var meas = g.Select(e => F(D(e, "meas"))).GroupBy(x => x).OrderByDescending(x => x.Count()).Take(15).Select(x => $"{x.Key}×{x.Count()}");
        Console.WriteLine($"     meas: {string.Join(" ", meas)}");
    }
    foreach (var e in ents.Where(e => S(e, "type") is "Leader" or "MLeader").Take(3))
        Console.WriteLine($"  {S(e, "type")} {S(e, "layer")} style={S(e, "dimStyle")}{S(e, "style")} text={S(e, "text")}");

    // Block con có tên chứa '_' cấp 1 (view)
    Console.WriteLine("Named blocks (non-trivial, with dims/text):");
    foreach (var b in blocks.Where(b => S(b, "space") == "block"))
    {
        var be = b.GetProperty("entities").EnumerateArray().ToList();
        if (be.Count < 3) continue;
        int dims = be.Count(e => S(e, "type") == "Dim"), refs = be.Count(e => S(e, "type") == "BlockRef"), pl = be.Count(e => S(e, "type") == "Polyline"), txt = be.Count(e => S(e, "type") is "Text" or "MText");
        if (dims + refs + pl < 3) continue;
        var refNames = be.Where(e => S(e, "type") == "BlockRef").GroupBy(e => S(e, "name")).Select(g => $"{g.Key}×{g.Count()}");
        var lays = be.GroupBy(e => S(e, "layer").Replace("Mechanical-", "")).Select(g => $"{g.Key}×{g.Count()}");
        Console.WriteLine($"  [{S(b, "block")}] n={be.Count} pl={pl} dims={dims} txt={txt} refs: {string.Join(",", refNames.Take(8))} | layers: {string.Join(",", lays)}");
    }
}
