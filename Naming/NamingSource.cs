using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Corrections;

namespace MCG_3DPanel.Naming
{
    /// <summary>1 panel đang hiện trên tab Naming: tuyến + đường dẫn highlight của từng đoạn (theo SourceKey).</summary>
    public sealed class NamingPanel
    {
        public string PanelKey { get; set; }
        public string PanelName { get; set; }
        public Database Db { get; set; }
        public ObjectId SourceBtrId { get; set; }
        public PanelPosition Position { get; set; }
        /// <summary>Đoạn để tính lại tuyến khi đổi Position (không phân loại lại).</summary>
        public List<NamingPiece> Pieces { get; set; } = new List<NamingPiece>();
        public IReadOnlyList<NamingLine> Lines { get; set; } = new NamingLine[0];
        /// <summary>SourceKey → đường dẫn highlight (nhiều part có thể chung SourceKey, vd GE tách từ Web).</summary>
        public Dictionary<string, List<ObjectId[]>> Paths { get; } = new Dictionary<string, List<ObjectId[]>>();
        public Dictionary<string, Extents3d> Bounds { get; } = new Dictionary<string, Extents3d>();
        /// <summary>true = highlight solid 3D (panel từ Build); false = polyline 2D.</summary>
        public bool From3D { get; set; }
        /// <summary>BuildResult gốc (panel từ Build) — để thay đúng panel khi rebuild.</summary>
        public BuildResult Build { get; set; }
        /// <summary>Thông số đã dùng cho quy cách trong tên.</summary>
        public PanelInput Input { get; set; }

        /// <summary>Tính lại tuyến + tên (tự sinh, rồi tên người dùng đã lưu).</summary>
        public void Recompute()
        {
            Lines = StructureNaming.Lines(Pieces, Position, NameStore.Deleted(PanelKey));
            ResolveNames();
        }

        /// <summary>Chỉ gắn lại tên người dùng (tuyến giữ nguyên đối tượng) — sau khi đổi tên.</summary>
        public void ResolveNames() => StructureNaming.Resolve(Lines, NameStore.For(PanelKey));
    }

    /// <summary>Chuyển part đã phân loại → <see cref="NamingPanel"/>. Hình học lấy trong HỆ BLOCK 2D (nghịch đảo
    /// block transform) để dời/xoay block không đổi tên.</summary>
    public static class NamingSource
    {
        public static NamingKind? KindOf(PartType t)
        {
            switch (t)
            {
                case PartType.Web:
                case PartType.GirderEnd: return NamingKind.Web;
                case PartType.HP: return NamingKind.HP;
                case PartType.FlatBar: return NamingKind.FB;
                case PartType.Flange: return NamingKind.Flange;   // chỉ cho quy cách tuyến Web (revision 1)
                default: return null;
            }
        }

        /// <summary>Panel từ 1 lần Build/rebuild/Load 3D — highlight theo solid 3D.</summary>
        public static NamingPanel FromBuild(Transaction tr, BuildResult b)
        {
            if (b == null || b.SourceBtrId.IsNull) return null;
            NameStore.EnsureLoaded(tr, b.SourceBtrId);
            var p = new NamingPanel
            {
                PanelKey = CorrectionStore.PanelKey(b.SourceBtrId),
                PanelName = b.PanelBlockName,
                Db = b.SourceBtrId.Database,
                SourceBtrId = b.SourceBtrId,
                Position = b.Position,
                From3D = b.Kept && !b.BlockRefId.IsNull,
                Build = b,
            };
            if (!p.From3D)
            {
                // Bỏ 3D (No) ⇒ block 2D vẫn còn: highlight polyline 2D.
                Fill(p, b.ClassifiedParts, b.SourceTransform.Inverse(), b.Input, part =>
                    (part.IdPath != null && part.IdPath.Length > 0 ? part.IdPath : null, (Extents3d?)part.Bounds));
                p.Recompute();
                return p;
            }
            var solidOf = new Dictionary<PanelPart, BuiltSolid>();
            foreach (var s in b.Solids)
                if (s.Part != null && !s.Deleted && !s.SolidId.IsNull && !solidOf.ContainsKey(s.Part)) solidOf[s.Part] = s;
            Fill(p, b.ClassifiedParts, b.SourceTransform.Inverse(), b.Input, part =>
            {
                // GE hợp nhất vào Web ⇒ solid của Web đó.
                if (!solidOf.TryGetValue(part, out var s) && part.JoinWithWeb != null) solidOf.TryGetValue(part.JoinWithWeb, out s);
                return s == null ? (null, (Extents3d?)null) : (new[] { b.BlockRefId, s.SolidId }, s.Bounds);
            });
            p.Recompute();
            return p;
        }

        /// <summary>Panel chỉ phân loại 2D (nút Select panel) — highlight polyline 2D.</summary>
        public static NamingPanel FromBlock(Transaction tr, ObjectId blockRef, PanelPosition pos, PanelInput input, ClassificationResult cls)
        {
            var br = (BlockReference)tr.GetObject(blockRef, OpenMode.ForRead);
            NameStore.EnsureLoaded(tr, br.BlockTableRecord);
            var p = new NamingPanel
            {
                PanelKey = CorrectionStore.PanelKey(br.BlockTableRecord),
                PanelName = cls.PanelBlockName,
                Db = blockRef.Database,
                SourceBtrId = br.BlockTableRecord,
                Position = pos,
                From3D = false,
            };
            Fill(p, cls.Parts, br.BlockTransform.Inverse(), input, part =>
                (part.IdPath != null && part.IdPath.Length > 0 ? part.IdPath : null, (Extents3d?)part.Bounds));
            p.Recompute();
            return p;
        }

        /// <summary>Quy cách từng đoạn (revision 1): Web cao theo Input, dày 2D · Flange rộng 2D, dày Input · HP theo
        /// quy cách Input · FB cao riêng (correction tab Result) hoặc Input, dày 2D.</summary>
        private static void Fill(NamingPanel p, IEnumerable<PanelPart> parts, Matrix3d toBlock, PanelInput input,
            System.Func<PanelPart, (ObjectId[] path, Extents3d? bounds)> pathOf)
        {
            foreach (var part in parts ?? Enumerable.Empty<PanelPart>())
            {
                var kind = KindOf(part.Type);
                if (kind == null) continue;
                if (string.IsNullOrEmpty(part.SourceKey) && kind != NamingKind.Flange) continue;
                var (a, b) = MCG_3DPanel.Services.Classify.ClassifyUtil.Endpoints(part);
                a = a.TransformBy(toBlock);
                b = b.TransformBy(toBlock);
                var piece = new NamingPiece
                {
                    Key = part.SourceKey ?? ("F" + part.Id.Handle),
                    Kind = kind.Value,
                    X0 = a.X, Y0 = a.Y, X1 = b.X, Y1 = b.Y,
                    Thickness = part.FootprintThickness,
                };
                p.Pieces.Add(piece);
                if (kind == NamingKind.Flange) continue;   // flange không có dòng riêng ⇒ không cần highlight
                var (path, bounds) = pathOf(part);
                if (path == null) continue;
                if (!p.Paths.TryGetValue(part.SourceKey, out var list)) p.Paths[part.SourceKey] = list = new List<ObjectId[]>();
                if (!list.Any(x => x.SequenceEqual(path))) list.Add(path);
                if (bounds is Extents3d e)
                {
                    if (p.Bounds.TryGetValue(part.SourceKey, out var old)) { old.AddExtents(e); p.Bounds[part.SourceKey] = old; }
                    else p.Bounds[part.SourceKey] = e;
                }
            }
            ApplySizes(p, input);
        }

        /// <summary>Gán quy cách theo <paramref name="input"/> (gọi lại khi tab Input đổi — review R1 #2). HP: quy cách
        /// Input (cả chiều dày); Web/FB/Flange: chiều dày/bề rộng giữ theo 2D.</summary>
        public static void ApplySizes(NamingPanel p, PanelInput input)
        {
            input = input ?? new PanelInput();
            p.Input = input;
            PanelInput.TryParseSpec(input.HpSpec, out double hpH, out double hpT);
            var corr = CorrectionStore.For(p.PanelKey);
            foreach (var piece in p.Pieces)
            {
                switch (piece.Kind)
                {
                    case NamingKind.Web: piece.Height = input.WebHeight; break;
                    case NamingKind.Flange: piece.Height = input.FlangeThickness; break;
                    case NamingKind.HP: piece.Height = hpH; piece.Thickness = hpT; break;
                    case NamingKind.FB:
                        piece.Height = corr.TryGetValue(piece.Key, out var c) && c?.FbHeight is double h && h > 0
                            ? h : input.FlatBarHeight;
                        break;
                }
            }
        }
    }
}
