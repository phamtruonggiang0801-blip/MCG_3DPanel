using System.Collections.Generic;
using System.Diagnostics;
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
            new PlateBuilder(),     // TopPlate/Web/Flange/FacePlate/FlatBar + fallback
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

                int made = 0;
                foreach (var part in cls.Parts)
                {
                    Solid3d sol = Dispatch(ctx, part);
                    if (sol == null) continue;

                    // Giữ nguyên Layer + Color gốc của đối tượng 2D (view tổng quát rõ hơn).
                    sol.Layer = part.Layer;
                    sol.ColorIndex = part.ColorIndex;
                    btr.AppendEntity(sol);
                    tr.AddNewlyCreatedDBObject(sol, true);
                    made++;
                }

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
