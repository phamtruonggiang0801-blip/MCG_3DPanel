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
                    if (sol == null) continue;

                    // Giữ nguyên Layer + Color gốc của đối tượng 2D (view tổng quát rõ hơn).
                    sol.Layer = part.Layer;
                    sol.ColorIndex = part.ColorIndex;
                    btr.AppendEntity(sol);
                    tr.AddNewlyCreatedDBObject(sol, true);
                    made++;
                    FillGeometry(info, sol);

                    if (part.Type == PartType.TopPlate) { topPlateSolid = sol; topPlateInfo = info; }
                    if (part.Type == PartType.Web) webSolids.Add((part, sol, info));
                }

                // Lỗ khoét trên Web cho HP xuyên qua (CO B) — trừ boolean thẳng vào solid Web đã dựng,
                // KHÔNG phải part riêng (giống CutOut ↔ Top Plate). Xem WebHpCutoutBuilder / skill web-hp-cutout.
                if (webSolids.Count > 0)
                {
                    var webCutOk = WebHpCutoutBuilder.ApplyWebCutouts(
                        ctx, webSolids.Select(w => (w.part, w.sol)).ToList());
                    if (webCutOk.Count > 0)
                        foreach (var w in webSolids) FillGeometry(w.info, w.sol); // cập nhật bbox/thể tích SAU khi khoét
                }

                // Collar Plate / LUG B1R — SOLID MỚI (không boolean) hàn phía bụng HP quanh lỗ CO, tại
                // 2 lỗ gần mỗi junction web-ngang × web-dọc, chỉ khi WebHeight < 300. Xem skill web-hp-cutout §7.
                foreach (var (collar, tag) in WebCollarPlateBuilder.BuildCollarPlates(ctx))
                {
                    btr.AppendEntity(collar);
                    tr.AddNewlyCreatedDBObject(collar, true);
                    made++;
                    string ch = collar.Handle.ToString();
                    var cInfo = new BuiltPartInfo { Handle = ch, Type = "CollarPlate", Code = tag, Built = true };
                    result.Parts.Add(cInfo);
                    FillGeometry(cInfo, collar);
                    ctx.Report.Add($"[Collar] solid {ch} = {tag}");
                }

                // Bracket đầu HP mã "B" — SOLID MỚI, nơi HP kết thúc cách 1 Web ngang 30mm (KHÔNG xuyên
                // qua). Thay Sniped End HP (đã bỏ). Xem skill sniped-end §5.
                foreach (var (brkt, tag) in HpEndBracketBuilder.BuildHpEndBrackets(ctx))
                {
                    btr.AppendEntity(brkt);
                    tr.AddNewlyCreatedDBObject(brkt, true);
                    made++;
                    string ch = brkt.Handle.ToString();
                    var bInfo = new BuiltPartInfo { Handle = ch, Type = "HpEndBracketB", Code = tag, Built = true };
                    result.Parts.Add(bInfo);
                    FillGeometry(bInfo, brkt);
                    ctx.Report.Add($"[BracketB] solid {ch} = {tag}");
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

                result.BlockRefId = bref.ObjectId;
                result.BtrId = btrId;
                result.SolidCount = made;
                result.BlockName = name;
                result.Report.AddRange(ctx.Report);

                tr.Commit();
            }
            Debug.WriteLine($"{LOG_PREFIX} Dựng {result.SolidCount} solid -> block {result.BlockName}.");
            return result;
        }

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
                return PlateBuilder.Extrude(ctx.Tr, part, ex, mz);
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
