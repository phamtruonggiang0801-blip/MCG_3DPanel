using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.Services.PlanView;
using MCG_3DPanel.UI;

namespace MCG_3DPanel.Commands
{
    /// <summary>Đọc ảnh chụp 1 panel từ file 3D (build trong phiên, tab Naming, correction, kích thước nháp AM_9 của bản sao block 2D)
    /// ⇒ <see cref="PlanViewInput"/> ở toạ độ block 2D. Chạy TRƯỚC khi chuyển sang bản vẽ 2D (spec Flow bước 4).</summary>
    public static class PlanViewSnapshot
    {
        private const string DraftLayer = "Mechanical-AM_9";

        public static PlanViewInput Read(Database db3D, BuildResult build, out string error)
        {
            error = null;
            if (build == null || build.SourceBtrId.IsNull || build.SourceRefId.IsNull) { error = "This drawing has no 3D panel."; return null; }
            Matrix3d toBlock = build.SourceTransform.Inverse();
            string panelBlock = build.PanelBlockName ?? "";
            var input = new PlanViewInput
            {
                Panel = panelBlock.EndsWith("_Assy", StringComparison.Ordinal) ? panelBlock.Substring(0, panelBlock.Length - 5) : panelBlock,
                Position = (int)build.Position,
                PlateThickness = build.Input?.TopPlateThickness ?? 6,
            };

            using (Transaction tr = db3D.TransactionManager.StartTransaction())
            {
                ReadMembers(tr, build, input);
                ReadTopPlate(build, toBlock, input, ref error);
                ReadDrafts(tr, build.SourceBtrId, input);
                ReadMarks(build, toBlock, input);
                tr.Commit();
            }
            return error == null ? input : null;
        }

        // ─── Thanh: theo tab Naming (tên, tuyến, quy cách), xoá theo correction Delete ───
        private static void ReadMembers(Transaction tr, BuildResult build, PlanViewInput input)
        {
            NamingPanel p = NamingSource.FromBuild(tr, build);
            if (p == null) return;
            p.ResolveNames();
            var deleted = new HashSet<string>(CorrectionStore.Deleted(CorrectionStore.PanelKey(build.SourceBtrId)) ?? Array.Empty<string>());
            PanelInput.TryParseSpec(build.Input?.HpSpec, out double hpH, out double hpT);

            var partByKey = new Dictionary<string, PanelPart>();
            foreach (var part in build.ClassifiedParts) if (!string.IsNullOrEmpty(part.SourceKey) && !partByKey.ContainsKey(part.SourceKey)) partByKey[part.SourceKey] = part;
            Matrix3d toBlock = build.SourceTransform.Inverse();
            int lineNo = 0;
            foreach (NamingLine line in p.Lines)
            {
                lineNo++;
                foreach (NamingPiece piece in line.Pieces)
                {
                    string kind = piece.Kind.ToString();   // Web | HP | FB | Flange
                    input.Members.Add(new PvMember
                    {
                        Line = lineNo, Name = line.Name, Kind = kind, Dir = line.Dir.ToString(),
                        X0 = piece.X0, Y0 = piece.Y0, X1 = piece.X1, Y1 = piece.Y1,
                        Thickness = piece.Thickness, Height = piece.Height, Spec = line.Spec,
                        ProfileBlock = piece.Kind == NamingKind.HP ? "HP" + Num(hpH) + "x" + Num(hpT)
                                     : piece.Kind == NamingKind.FB ? "BS " + Num(piece.Height) + "x" + Num(piece.Thickness) : null,
                        Deleted = piece.Key != null && deleted.Contains(piece.Key),
                    });
                    if (piece.Kind == NamingKind.HP && piece.Key != null && partByKey.TryGetValue(piece.Key, out var hp) && hp.BellyDir.Length > 1e-9)
                    {
                        var bd = hp.BellyDir.TransformBy(toBlock).GetNormal();
                        input.Members[input.Members.Count - 1].BellyX = bd.X;
                        input.Members[input.Members.Count - 1].BellyY = bd.Y;
                    }
                }
                foreach (NamingPiece f in line.Flanges)
                    input.Members.Add(new PvMember
                    {
                        Line = lineNo, Name = line.Name, Kind = "Flange", Dir = line.Dir.ToString(),
                        X0 = f.X0, Y0 = f.Y0, X1 = f.X1, Y1 = f.Y1, Thickness = f.Thickness, Height = f.Height, Spec = line.Spec,
                        Deleted = f.Key != null && deleted.Contains(f.Key),
                    });
            }
        }

        private static void ReadTopPlate(BuildResult build, Matrix3d toBlock, PlanViewInput input, ref string error)
        {
            var plate = build.ClassifiedParts.FirstOrDefault(x => x.Type == PartType.TopPlate);
            if (plate == null) { error = "No Top Plate found in the 2D panel."; return; }
            var e = plate.Bounds;
            var pts = new[] { e.MinPoint, new Point3d(e.MaxPoint.X, e.MinPoint.Y, 0), e.MaxPoint, new Point3d(e.MinPoint.X, e.MaxPoint.Y, 0) }
                .Select(q => q.TransformBy(toBlock)).ToList();
            input.TopPlate = (pts.Min(q => q.X), pts.Min(q => q.Y), pts.Max(q => q.X), pts.Max(q => q.Y));
            if (plate.Vertices != null)
                foreach (var v in plate.Vertices) { var b = v.TransformBy(toBlock); input.PlateOutline.Add((b.X, b.Y)); }
        }

        // ─── Nháp AM_9 trong <panel>_Structure / <panel>_Lashing Hole (lồng trong block 2D) + 1 lỗ lashing ───
        private static void ReadDrafts(Transaction tr, ObjectId assyBtrId, PlanViewInput input)
        {
            var assy = (BlockTableRecord)tr.GetObject(assyBtrId, OpenMode.ForRead);
            Point3d? topAt = null, lashAt = null;
            foreach (ObjectId id in assy)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) continue;
                var btr = (BlockTableRecord)tr.GetObject(br.BlockTableRecord, OpenMode.ForRead);
                if (btr.Name.EndsWith("_Top Plate", StringComparison.Ordinal)) { topAt = topAt ?? br.Position; if (input.Cog == null) ReadCog(tr, btr, br.BlockTransform, input); }
                string source = btr.Name.EndsWith("_Structure", StringComparison.Ordinal) ? "Structure"
                              : btr.Name.EndsWith("_Lashing Hole", StringComparison.Ordinal) ? "LashingHole" : null;
                if (source == null) continue;
                if (source == "LashingHole") { input.HasLashingHoleBlock = true; lashAt = lashAt ?? br.Position; }
                Matrix3d xf = br.BlockTransform;
                foreach (ObjectId eid in btr)
                {
                    var ent = tr.GetObject(eid, OpenMode.ForRead) as Entity;
                    if (ent is Dimension dim && dim.Layer == DraftLayer)
                    {
                        Point3d a, b, ln; double rot;
                        if (dim is RotatedDimension rd) { a = rd.XLine1Point; b = rd.XLine2Point; ln = rd.DimLinePoint; rot = rd.Rotation; }
                        else if (dim is AlignedDimension ad) { a = ad.XLine1Point; b = ad.XLine2Point; ln = ad.DimLinePoint; rot = Math.Atan2(b.Y - a.Y, b.X - a.X); }
                        else continue;
                        Point3d pa = a.TransformBy(xf), pb = b.TransformBy(xf);
                        Vector3d u = new Vector3d(Math.Cos(rot), Math.Sin(rot), 0).TransformBy(xf);
                        string blk1 = "", blk2 = "";
                        try { blk1 = NameOf(tr, dim.Dimblk1); blk2 = NameOf(tr, dim.Dimblk2); } catch { }
                        input.Drafts.Add(new PvDraft
                        {
                            Source = source, X1 = pa.X, Y1 = pa.Y, X2 = pb.X, Y2 = pb.Y, Rotation = Math.Atan2(u.Y, u.X),
                            LineX = ln.TransformBy(xf).X, LineY = ln.TransformBy(xf).Y,
                            Text = dim.DimensionText == "<>" ? "" : dim.DimensionText ?? "", Tmove = dim.Dimtmove == 1,
                            Oblique = blk1.Equals("_Oblique", StringComparison.OrdinalIgnoreCase) && blk2.Equals("_Oblique", StringComparison.OrdinalIgnoreCase),
                        });
                    }
                    else if (source == "LashingHole" && ent is Circle c)
                    {
                        Point3d pc = c.Center.TransformBy(xf);
                        if (input.Lashing == null) input.Lashing = new PvLashing { Diameter = 2 * c.Radius * Math.Abs(br.ScaleFactors.X), X = pc.X, Y = pc.Y };
                        input.Lashing.Holes.Add((pc.X, pc.Y));   // mọi lỗ: chọn lỗ ghi Ø + hàng/cột lỗ cho kích thước cục bộ
                    }
                }
            }
            // Lashing Hole lồng trong _Assy có điểm chèn riêng (tờ 7D-17C: 3170.4, −10522.7 so với Top Plate) — mặt bằng lỗ lashing chèn
            // lại cả 2 block phải giữ đúng độ lệch này.
            if (topAt.HasValue && lashAt.HasValue)
                input.LashingBlockOffset = (lashAt.Value.X - topAt.Value.X, lashAt.Value.Y - topAt.Value.Y);
            if (topAt.HasValue) input.TopPlateBlockAt = (topAt.Value.X, topAt.Value.Y);
        }

        // ─── Ký hiệu: B (Tripping), S (Sniped End), Kind GE/GB (cột Kind tab Result), mã bracket OB… ───
        private static void ReadMarks(BuildResult build, Matrix3d toBlock, PlanViewInput input)
        {
            foreach (BuiltSolid s in build.Solids.Where(x => !x.Deleted))
            {
                PanelPart p = s.Part;
                var d = ResultCatalog.Describe(p?.Type.ToString(), p != null ? p.Code : s.Code, p == null ? s.Type : null,
                    s.MergedCodes, p?.EndTouchKinds, p?.GeAvailable ?? false, p?.GbAvailable ?? false);
                string kind = ResultCatalog.ShortKind(d.Kind) ?? "";

                if (p == null && (s.Type == "TrippingBracket" || s.Type == "HpEndBracketB"))   // tờ tay: B = tripping + bracket đầu HP, neo tại HP
                    input.Marks.Add(BAtHp(build, s, toBlock) ?? new PvMark { Text = "B", X = Centre(s.Bounds, toBlock).X, Y = Centre(s.Bounds, toBlock).Y });
                else if (kind.StartsWith("GE", StringComparison.Ordinal) || kind.StartsWith("GB", StringComparison.Ordinal))
                    AddAt(input, kind, Centre(s.Bounds, toBlock));
                else if (p != null && p.Type == PartType.Bracket && !string.IsNullOrEmpty(p.Code))
                    { AddAt(input, p.Code, Centre(s.Bounds, toBlock)); input.Marks[input.Marks.Count - 1].IsBracket = true; }

                if (p != null && (p.Type == PartType.HP || p.Type == PartType.FlatBar) && (p.SnipLo || p.SnipHi))
                {
                    var (a, b) = Services.Classify.ClassifyUtil.Endpoints(p);
                    Point3d ba = a.TransformBy(toBlock), bb = b.TransformBy(toBlock);
                    bool horiz = Math.Abs(bb.X - ba.X) >= Math.Abs(bb.Y - ba.Y);
                    bool aIsLo = horiz ? ba.X <= bb.X : ba.Y <= bb.Y;
                    Point3d lo = aIsLo ? ba : bb, hi = aIsLo ? bb : ba;
                    Vector3d dir = (hi - lo).GetNormal();
                    if (p.SnipLo) input.Marks.Add(new PvMark { Text = "S", X = lo.X, Y = lo.Y, DirX = -dir.X, DirY = -dir.Y });
                    if (p.SnipHi) input.Marks.Add(new PvMark { Text = "S", X = hi.X, Y = hi.Y, DirX = dir.X, DirY = dir.Y });
                }
            }
        }

        /// <summary>Chữ B neo tại HP như tờ tay 7D-17P. Tripping (khoá "TrippingBracket:{web}~{hp}"): giao điểm HP × web, đẩy về phía
        /// bracket dọc HP. Bracket đầu HP (khoá "HpEndBracketB:{hp}@lo|hi"): đầu HP đó, đẩy vào trong dọc HP. Không tìm được ⇒ null.</summary>
        private static PvMark BAtHp(BuildResult build, BuiltSolid s, Matrix3d toBlock)
        {
            string key = s.DeleteKey ?? "";
            PanelPart Find(string k) => build.ClassifiedParts.FirstOrDefault(x => x.SourceKey == k);
            (Point3d a, Point3d b) Ends(PanelPart part)
            {
                var (a, b) = Services.Classify.ClassifyUtil.Endpoints(part);
                return (a.TransformBy(toBlock), b.TransformBy(toBlock));
            }
            PvMark Mark(Point3d anchor, Vector3d along)
            {
                bool vertical = Math.Abs(along.Y) >= Math.Abs(along.X);
                return new PvMark { Text = "B", X = anchor.X, Y = anchor.Y, AtHp = true, DirX = along.X, DirY = along.Y,
                                    PerpX = vertical ? -1 : 0, PerpY = vertical ? 0 : -1 };
            }
            try
            {
                if (key.StartsWith("TrippingBracket:", StringComparison.Ordinal))
                {
                    var ks = key.Substring("TrippingBracket:".Length).Split('~');
                    if (ks.Length != 2) return null;
                    PanelPart web = Find(ks[0]), hp = Find(ks[1]);
                    if (web == null || hp == null) return null;
                    var (h0, h1) = Ends(hp); var (w0, w1) = Ends(web);
                    Vector3d u = (h1 - h0).GetNormal(), v = (w1 - w0).GetNormal();
                    double cross = u.X * v.Y - u.Y * v.X;
                    if (Math.Abs(cross) < 1e-6) return null;
                    double t = ((w0.X - h0.X) * v.Y - (w0.Y - h0.Y) * v.X) / cross;
                    Point3d c = h0 + u * t;
                    Point3d centre = Centre(s.Bounds, toBlock);
                    return Mark(c, (centre - c).DotProduct(u) >= 0 ? u : -u);
                }
                if (key.StartsWith("HpEndBracketB:", StringComparison.Ordinal))
                {
                    string rest = key.Substring("HpEndBracketB:".Length);
                    int at = rest.LastIndexOf('@');
                    if (at < 0) return null;
                    PanelPart hp = Find(rest.Substring(0, at));
                    if (hp == null) return null;
                    var (a, b) = Ends(hp);
                    bool horiz = Math.Abs(b.X - a.X) >= Math.Abs(b.Y - a.Y);
                    bool aIsLo = horiz ? a.X <= b.X : a.Y <= b.Y;
                    Point3d lo = aIsLo ? a : b, hi = aIsLo ? b : a;
                    bool atLo = rest.Substring(at + 1) == "lo";
                    return Mark(atLo ? lo : hi, ((atLo ? hi : lo) - (atLo ? lo : hi)).GetNormal());
                }
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { }
            return null;
        }

        /// <summary>Block "COG" đầu tiên trong &lt;panel&gt;_Top Plate: tâm (hệ block 2D) + bán kính vòng tròn của block × scale.</summary>
        private static void ReadCog(Transaction tr, BlockTableRecord topPlate, Matrix3d xf, PlanViewInput input)
        {
            foreach (ObjectId id in topPlate)
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference cog)) continue;
                var def = (BlockTableRecord)tr.GetObject(cog.BlockTableRecord, OpenMode.ForRead);
                if (!string.Equals(def.Name, "COG", StringComparison.OrdinalIgnoreCase)) continue;
                double r = 0;
                foreach (ObjectId eid in def)
                    if (tr.GetObject(eid, OpenMode.ForRead) is Circle c) { r = c.Radius * Math.Abs(cog.ScaleFactors.X); break; }
                Point3d p = cog.Position.TransformBy(xf);
                input.Cog = (p.X, p.Y, r);
                return;
            }
        }

        private static string NameOf(Transaction tr, ObjectId id)
            => id.IsNull ? "" : ((BlockTableRecord)tr.GetObject(id, OpenMode.ForRead)).Name;

        private static void AddAt(PlanViewInput input, string text, Point3d p) => input.Marks.Add(new PvMark { Text = text, X = p.X, Y = p.Y });

        private static Point3d Centre(Extents3d e, Matrix3d toBlock)
            => new Point3d((e.MinPoint.X + e.MaxPoint.X) / 2, (e.MinPoint.Y + e.MaxPoint.Y) / 2, 0).TransformBy(toBlock);

        private static string Num(double v) => v.ToString("0.#", CultureInfo.InvariantCulture);
    }
}
