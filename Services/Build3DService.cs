using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Builders;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// ĐIỀU PHỐI dựng 3D — KHÔNG chứa hình học của bộ phận nào.
    /// Nhiệm vụ: mở transaction, tạo Block 3D, duyệt part → chọn builder → gom solid → commit.
    /// Hình học nằm ở <c>Services/Builders/*</c>, mỗi đối tượng 1 file (xem skill module-per-object).
    ///
    /// Thứ tự builder = ĐỘ ƯU TIÊN: builder chuyên biệt trước, <see cref="PlateBuilder"/> cuối làm fallback
    /// (vd HP không dựng được tiết diện bulb → vẫn ra hộp chữ nhật, không vỡ bản dựng).
    /// Datum = mặt DƯỚI Top Plate = Z0. Solid GIỮ NGUYÊN layer + màu gốc của đối tượng 2D.
    /// </summary>
    public class Build3DService : IBuild3DService
    {
        private const string LOG_PREFIX = "[Build3DService]";

        private readonly List<IPartBuilder> _builders = new List<IPartBuilder>
        {
            new GirderEndBuilder(), // GE — đầu Web tại HP/FB
            new BracketBuilder(),   // Bracket có mã knee (OB/IB/FB)
            new HpBuilder(),        // HP tiết diện bulb thật
            new FlatBarBuilder(),   // FlatBar — tự dựng TOÀN BỘ (vát Sniped End ở đầu tự do, hộp phẳng nếu không)
            new BackingPlateBuilder(), // BackingPlate — vát góc Sniped End ở đầu tự do (2 góc/đầu), hộp phẳng nếu không
            new PlateBuilder(),     // TopPlate/Web/Flange/FacePlate/ClosedPlate + fallback (kể cả BackingPlate)
        };

        private readonly IProfileService _profile = new ProfileService();
        private readonly IHpProfileService _hpProfile = new HpProfileService();
        private readonly IGeProfileService _geProfile = new GeProfileService();

        public BuildResult Build(Database db, ClassificationResult cls, PanelInput input, PanelPosition position)
        {
            var result = new BuildResult();
            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var bt = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForWrite);
                string name = UniqueBlockName(bt, "MCG3D_" + Sanitize(cls.PanelBlockName));

                var btr = new BlockTableRecord { Name = name };
                ObjectId btrId = bt.Add(btr);
                tr.AddNewlyCreatedDBObject(btr, true);

                var ctx = new BuildContext
                {
                    Tr = tr,
                    Cls = cls,
                    Input = input,
                    Position = position,
                    Profile = _profile,
                    HpProfile = _hpProfile,
                    GeProfile = _geProfile,
                };

                // Chẩn đoán từ khâu phân loại (part bị bỏ vì phi lý…) — đưa lên đầu report build.
                if (cls?.Notes != null && cls.Notes.Count > 0)
                    ctx.Report.AddRange(cls.Notes);

                int made = 0;
                Solid3d topPlateSolid = null;
                BuiltPartInfo topPlateInfo = null;
                var webSolids = new List<(PanelPart part, Solid3d sol, BuiltPartInfo info)>();
                var hpSolids = new List<(PanelPart part, Solid3d sol, BuiltPartInfo info)>();
                var builtMap = new Dictionary<PanelPart, (Solid3d sol, BuiltPartInfo info)>();
                foreach (var part in cls.Parts)
                {
                    string handle = part.Id.Handle.ToString();

                    if (part.Type == PartType.CutOut)
                    {
                        // Xử lý riêng bên dưới — trừ vào Top Plate, không tự thành solid ở vòng này.
                        result.Parts.Add(new BuiltPartInfo { Handle = handle, Type = part.Type.ToString(), Code = part.Code ?? "", Built = false });
                        continue;
                    }

                    Solid3d sol = Dispatch(ctx, part);
                    var info = new BuiltPartInfo { Handle = handle, Type = part.Type.ToString(), Code = part.Code ?? "", Built = sol != null };
                    result.Parts.Add(info);
                    if (sol == null)
                    {
                        foreach (var x in ctx.Extras) x.Solid.Dispose();   // part chính hỏng → bỏ luôn solid phụ
                        ctx.Extras.Clear();
                        continue;
                    }

                    // Giữ nguyên Layer + Color gốc của đối tượng 2D (view tổng quát rõ hơn).
                    sol.Layer = part.Layer;
                    sol.ColorIndex = part.ColorIndex;
                    btr.AppendEntity(sol);
                    tr.AddNewlyCreatedDBObject(sol, true);
                    made++;
                    FillGeometry(info, sol);
                    builtMap[part] = (sol, info);
                    result.Solids.Add(new BuiltSolid { SolidId = sol.ObjectId, Part = part, Type = part.Type.ToString(), DeleteKey = part.SourceKey });

                    // Solid phụ ĐỘC LẬP builder sinh kèm (flange GE/GB) — Layer/Color builder đã gán sẵn
                    // (theo Flange girder gốc), KHÔNG ghi đè bằng layer của part.
                    int extraIdx = 0;   // DeleteKey solid phụ = "{SourceKey part}/{Type}#{i}"
                    foreach (var x in ctx.Extras)
                    {
                        btr.AppendEntity(x.Solid);
                        tr.AddNewlyCreatedDBObject(x.Solid, true);
                        made++;
                        string xh = x.Solid.Handle.ToString();
                        var xInfo = new BuiltPartInfo { Handle = xh, Type = x.Type, Code = x.Code, Built = true };
                        result.Parts.Add(xInfo);
                        FillGeometry(xInfo, x.Solid);
                        result.Solids.Add(new BuiltSolid { SolidId = x.Solid.ObjectId, Type = x.Type, Code = x.Code, DeleteKey = $"{part.SourceKey}/{x.Type}#{extraIdx++}" });
                        ctx.Report.Add($"[{x.Type}] solid {xh} của {part.Code} {handle} · layer={x.Solid.Layer}");
                    }
                    ctx.Extras.Clear();

                    if (part.Type == PartType.TopPlate) { topPlateSolid = sol; topPlateInfo = info; }
                    if (part.Type == PartType.Web) webSolids.Add((part, sol, info));
                    if (part.Type == PartType.HP) hpSolids.Add((part, sol, info));
                }

                // HP end tại HP ("HP TO HP END", CAS.dwt) — trừ dao cắt (mặt hàn + cung R30 thoát bulb) khỏi
                // HP đầu chạm thân HP khác. Xem HpEndAtHpBuilder / skill hp-end-at-hp.
                if (hpSolids.Count > 0)   // 1 HP vẫn có thể end tại FB ("HP TO FB END")
                {
                    var hpCutOk = HpEndAtHpBuilder.Apply(ctx, hpSolids.Select(x => (x.part, x.sol)).ToList());
                    if (hpCutOk.Count > 0)
                        foreach (var x in hpSolids) FillGeometry(x.info, x.sol);   // cập nhật bbox/thể tích SAU khi cắt
                }

                // Lỗ khoét trên Web cho HP xuyên qua (CO B) — trừ boolean thẳng vào solid Web đã dựng,
                // KHÔNG phải part riêng (giống CutOut ↔ Top Plate). Xem WebHpCutoutBuilder / skill web-hp-cutout.
                // Vùng GB (đầu của cùng tấm web) cũng được khoét — TRƯỚC khi hợp nhất vào Web (kỹ sư 2026-11-04).
                var cutSolids = webSolids
                    .Concat(builtMap.Where(k => IsGbZone(k.Key)).Select(k => (part: k.Key, sol: k.Value.sol, info: k.Value.info)))
                    .ToList();
                if (cutSolids.Count > 0)
                {
                    var webCutOk = WebHpCutoutBuilder.ApplyWebCutouts(
                        ctx, cutSolids.Select(w => (w.part, w.sol)).ToList());
                    if (webCutOk.Count > 0)
                        foreach (var w in cutSolids) FillGeometry(w.info, w.sol); // cập nhật bbox/thể tích SAU khi khoét
                }

                // GE + Web còn lại vốn là 1 tấm liền (GE chỉ là cách gọi tên đầu girder) — GirderEndSplitter
                // cắt để tính hình học; ở đây HỢP NHẤT lại thành 1 solid (sau CO để lỗ HP khoét xong trên Web).
                foreach (var kv in builtMap.Where(k => k.Key.Type == PartType.GirderEnd && k.Key.JoinWithWeb != null).ToList())
                {
                    if (!builtMap.TryGetValue(kv.Key.JoinWithWeb, out var web)) continue;
                    var ge = kv.Value;
                    try
                    {
                        ObjectId geId = ge.sol.ObjectId;
                        web.sol.BooleanOperation(BooleanOperationType.BoolUnite, ge.sol);   // ge.sol bị tiêu thụ
                        ge.sol.Erase();
                        result.Solids.RemoveAll(s => s.SolidId == geId);
                        result.Solids.Find(s => s.SolidId == web.sol.ObjectId)?.MergedCodes.Add(kv.Key.Code);
                        made--;
                        ge.info.Code = $"{kv.Key.Code} → hợp nhất vào Web {kv.Key.JoinWithWeb.Id.Handle}";
                        FillGeometry(web.info, web.sol);
                        ctx.Report.Add($"[GE merge] GE {ge.info.Handle} L={kv.Key.MemberLength:0} hợp nhất vào Web {web.info.Handle} " +
                            $"→ 1 solid liền (lumps={GeometryUtil.CountLumps(web.sol)}).");
                    }
                    catch (System.Exception ex)
                    {
                        ctx.Report.Add($"[GE merge] không hợp nhất được GE {ge.info.Handle} vào Web {web.info.Handle}: {ex.Message} — giữ 2 solid.");
                    }
                }

                // Vát 1:4 mối nối web thẳng hàng khác chiều dày (kỹ sư 2026-11-05) — SAU khi gộp GE/GB vào web: part
                // GE/GB đã gộp dùng solid web chứa nó. Xem WebChamferBuilder / skill web-thickness-chamfer.
                var chamferWebs = new List<(PanelPart part, Solid3d sol)>();
                foreach (var kv in builtMap)
                {
                    var t = kv.Key.Type;
                    if (t != PartType.Web && t != PartType.ClosedPlate && t != PartType.GirderEnd) continue;
                    Solid3d s = kv.Value.sol;
                    if ((s == null || s.IsErased) && kv.Key.JoinWithWeb != null && builtMap.TryGetValue(kv.Key.JoinWithWeb, out var host))
                        s = host.sol;
                    if (s == null || s.IsErased) continue;
                    chamferWebs.Add((kv.Key, s));
                }
                var chamfered = WebChamferBuilder.Apply(ctx, chamferWebs);
                foreach (var kv in builtMap)
                    if (chamfered.Contains(kv.Value.sol)) FillGeometry(kv.Value.info, kv.Value.sol);   // bbox/thể tích SAU khi vát

                // Collar Plate / LUG B1R — SOLID MỚI (không boolean) hàn phía bụng HP quanh lỗ CO, tại
                // 2 lỗ gần mỗi junction web-ngang × web-dọc (+ ép On/Off theo HP ở tab Result). Xem skill web-hp-cutout §7.
                foreach (var (collar, tag, ckey) in WebCollarPlateBuilder.BuildCollarPlates(ctx))
                {
                    btr.AppendEntity(collar);
                    tr.AddNewlyCreatedDBObject(collar, true);
                    made++;
                    string ch = collar.Handle.ToString();
                    var cInfo = new BuiltPartInfo { Handle = ch, Type = "CollarPlate", Code = tag, Built = true };
                    result.Parts.Add(cInfo);
                    FillGeometry(cInfo, collar);
                    result.Solids.Add(new BuiltSolid { SolidId = collar.ObjectId, Type = "CollarPlate", DeleteKey = ckey });
                    ctx.Report.Add($"[Collar] solid {ch} = {tag}");
                }

                // Bracket đầu HP mã "B" — SOLID MỚI, nơi HP kết thúc cách 1 Web ngang 30mm (KHÔNG xuyên
                // qua). Thay Sniped End HP (đã bỏ). Xem skill sniped-end §5.
                foreach (var (brkt, tag, bkey) in HpEndBracketBuilder.BuildHpEndBrackets(ctx))
                {
                    btr.AppendEntity(brkt);
                    tr.AddNewlyCreatedDBObject(brkt, true);
                    made++;
                    string ch = brkt.Handle.ToString();
                    var bInfo = new BuiltPartInfo { Handle = ch, Type = "HpEndBracketB", Code = tag, Built = true };
                    result.Parts.Add(bInfo);
                    FillGeometry(bInfo, brkt);
                    result.Solids.Add(new BuiltSolid { SolidId = brkt.ObjectId, Type = "HpEndBracketB", DeleteKey = bkey });
                    ctx.Report.Add($"[BracketB] solid {ch} = {tag}");
                }

                // Tripping Bracket — bracket B dưới HP, khoảng gối dọc đoạn dầm ≤ 3 × bước stiffener max (kỹ sư 2026-11-05).
                // Đảo phía trên tab Result qua FlipKey (= DeleteKey). Xem TrippingBracketBuilder / skill tripping-bracket.
                foreach (var (tb, tag, tkey) in TrippingBracketBuilder.Build(ctx))
                {
                    btr.AppendEntity(tb);
                    tr.AddNewlyCreatedDBObject(tb, true);
                    made++;
                    string th = tb.Handle.ToString();
                    var tInfo = new BuiltPartInfo { Handle = th, Type = "TrippingBracket", Code = tag, Built = true };
                    result.Parts.Add(tInfo);
                    FillGeometry(tInfo, tb);
                    result.Solids.Add(new BuiltSolid { SolidId = tb.ObjectId, Type = "TrippingBracket", DeleteKey = tkey, FlipKey = tkey });
                    ctx.Report.Add($"[TrippingBracket] solid {th} = {tag}");
                }

                // CutOut = lỗ khoét — trừ (boolean) thẳng vào solid Top Plate đã dựng, không phải part riêng.
                if (topPlateSolid != null)
                {
                    var cutOk = CutOutBuilder.ApplyCutOuts(ctx, topPlateSolid, input.TopPlateThickness);
                    foreach (var info in result.Parts)
                        if (cutOk.Contains(info.Handle)) info.Built = true;
                    if (topPlateInfo != null) FillGeometry(topPlateInfo, topPlateSolid); // cập nhật bbox/thể tích SAU khi khoét
                }
                else if (cls.OfType(PartType.CutOut).Any())
                    ctx.Report.Add("[CutOut] Không có solid Top Plate để khoét — bỏ qua toàn bộ CutOut.");

                var ms = (BlockTableRecord)tr.GetObject(
                    SymbolUtilityServices.GetBlockModelSpaceId(db), OpenMode.ForWrite);
                var bref = new BlockReference(Point3d.Origin, btrId) { Layer = "0" };
                ms.AppendEntity(bref);
                tr.AddNewlyCreatedDBObject(bref, true);

                // Bbox + thể tích cuối của từng solid (sau mọi boolean) — tab Result zoom, report JSON.
                foreach (var s in result.Solids)
                {
                    var solid = (Solid3d)tr.GetObject(s.SolidId, OpenMode.ForRead);
                    try { s.Bounds = solid.GeometricExtents; }
                    catch { /* để Bounds rỗng — row vẫn highlight được, chỉ không zoom */ }
                    try { s.Volume = solid.MassProperties.Volume; }
                    catch { /* để null nếu AutoCAD không tính được (hình phức tạp) */ }
                }

                // Delete (tab Result): chỉ xoá đúng solid có DeleteKey trong danh sách — SAU mọi boolean/hợp nhất,
                // không nhận diện lại gì. Giữ dòng trong Solids (Deleted=true) để bảng hiện gạch ngang + Restore.
                if (cls.DeletedKeys != null && cls.DeletedKeys.Count > 0)
                    foreach (var s in result.Solids.Where(x => x.DeleteKey != null && cls.DeletedKeys.Contains(x.DeleteKey)))
                    {
                        tr.GetObject(s.SolidId, OpenMode.ForWrite).Erase();
                        s.Deleted = true;
                        made--;
                        ctx.Report.Add($"[Delete] solid {s.SolidId.Handle} ({s.Type} {s.DeleteKey}) xoá theo tab Result.");
                    }

                result.BlockRefId = bref.ObjectId;
                result.BtrId = btrId;
                result.SolidCount = made;
                result.BlockName = name;
                result.Report.AddRange(ctx.Report);
                result.Warnings.AddRange(ctx.Warnings);

                tr.Commit();
            }
            Debug.WriteLine($"{LOG_PREFIX} Dựng {result.SolidCount} solid -> block {result.BlockName}.");
            return result;
        }

        /// <summary>Part GB (GirderEndSplitter) — tách từ 1 tấm Web HOẶC phủ cả tấm (JoinWithWeb null) — được
        /// khoét lỗ CO + dựng collar như Web (spec 2026-11-04 §8.2). Chỉ GirderEndSplitter sinh mã "GB".</summary>
        internal static bool IsGbZone(PanelPart p) =>
            p.Type == PartType.GirderEnd && (p.Code ?? "").StartsWith("GB");

        /// <summary>Đọc bbox WCS + thể tích thật của solid vào <paramref name="info"/> — im lặng bỏ qua
        /// nếu AutoCAD không tính được (không làm hỏng build). Dùng để chẩn đoán "built=true nhưng thực
        /// ra rỗng/lệch chỗ" (VD Top Plate bị CutOut khoét lố mất gần hết).</summary>
        private static void FillGeometry(BuiltPartInfo info, Solid3d sol)
        {
            try
            {
                Extents3d ext = sol.GeometricExtents;
                info.MinX = ext.MinPoint.X; info.MinY = ext.MinPoint.Y; info.MinZ = ext.MinPoint.Z;
                info.MaxX = ext.MaxPoint.X; info.MaxY = ext.MaxPoint.Y; info.MaxZ = ext.MaxPoint.Z;
            }
            catch { /* để null nếu không lấy được bounds */ }

            try { info.Volume = sol.MassProperties.Volume; }
            catch { /* để null nếu AutoCAD không tính được (hình phức tạp) */ }
        }

        /// <summary>Chọn builder đầu tiên nhận part; nếu builder trả null thì thử builder kế (fallback).</summary>
        private Solid3d Dispatch(BuildContext ctx, PanelPart part)
        {
            foreach (var b in _builders)
            {
                if (!b.CanBuild(part)) continue;
                Solid3d sol = b.Build(ctx, part);
                if (sol != null) return sol;
                ctx.Report.Add($"[fallback] {b.Name} không dựng được {part.Type} {part.Code} → thử builder kế.");
            }
            // Fallback cuối: prism theo ZParams (bracket không mã, HP thiếu belly…).
            if (PlateBuilder.ZParams(part.Type, ctx.Input, out double ex, out double mz) && System.Math.Abs(ex) > 1e-6)
                return part.FromVertices ? PlateBuilder.ExtrudeVertices(part, ex, mz, ctx.Report)
                                         : PlateBuilder.Extrude(ctx.Tr, part, ex, mz);
            return null;
        }

        private static string UniqueBlockName(BlockTable bt, string baseName)
        {
            if (!bt.Has(baseName)) return baseName;
            for (int i = 1; ; i++)
            {
                string n = baseName + "_" + i;
                if (!bt.Has(n)) return n;
            }
        }

        private static string Sanitize(string s)
        {
            if (string.IsNullOrEmpty(s)) return "Panel";
            var chars = s.ToCharArray();
            for (int i = 0; i < chars.Length; i++)
            {
                char c = chars[i];
                bool ok = char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '$';
                if (!ok) chars[i] = '_';
            }
            return new string(chars);
        }
    }
}
