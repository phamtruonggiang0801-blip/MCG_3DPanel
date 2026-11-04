using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.UI;

namespace MCG_3DPanel.Services.Classify
{
    /// <summary>
    /// BƯỚC 2a — Tách GIRDER END (GE) / GIRDER BRACKET (GB) ra khỏi tầng Web DÀI (≥ <see cref="ClassifyUtil.WEB_MIN_LEN"/>)
    /// có đầu chạm HP/FB. Chạy SAU <see cref="WebBracketSplitter"/>, TRƯỚC <see cref="GirderEndDetector"/>.
    ///
    /// Quy tắc (kỹ sư xác nhận 2026-11-03, skill girder-end §3b/§8 + girder-bracket):
    ///  · L = khoảng cách từ đầu end tại HP/FB tới MÉP GẦN của Web VUÔNG GÓC (chỉ tính <see cref="PartType.Web"/>).
    ///  · <b>Max Stiff</b> = <see cref="GirderEndDetector.HpMaxSpan"/> (fallback <see cref="ClassifyUtil.GE_MAX_L"/> khi =0).
    ///  · L &lt; Max Stiff ⇒ <b>GE</b>: cắt tại đó, đoạn sát HP/FB = GE (Code "GE-HP"/"GE-FB").
    ///  · L ≥ Max Stiff (hoặc không có Web vuông góc) ⇒ <b>GB</b>: vùng GB = từ đầu end tới HP xuyên GẦN NHẤT + 100mm
    ///    (+ <see cref="ClassifyUtil.GB_TOE_PAD"/> đoạn đáy thẳng) — Code "GB-HP"/"GB-FB"; phần còn lại = Web chữ nhật.
    ///    Mốc toe = HP/FB xuyên HOẶC dừng cách mặt web ≤ 50mm (<see cref="NearestToeAnchor"/>, kỹ sư 2026-11-04).
    ///    Không có mốc trước Web vuông góc ⇒ giữ Web, ghi Notes.
    /// GE/GB + Web còn lại là 1 tấm liền: <see cref="PanelPart.JoinWithWeb"/> để Build3DService hợp nhất.
    /// </summary>
    internal static class GirderEndSplitter
    {
        private const double PERP_COS = 0.30;   // |cos| ≤ ngưỡng ⇒ vuông góc (Web ⊥ girder)
        private const double HP_COS = 0.70;     // |cos| ≤ ngưỡng ⇒ HP/FB cắt ngang (không chạy dọc theo girder)

        /// <param name="corrections">Correction người dùng (theo <see cref="PanelPart.SourceKey"/>): ép Web / GE / GB.
        /// Ép không được (thiếu Web ⊥ cho GE, thiếu mốc toe cho GB) ⇒ giữ quyết định tự động + ghi Notes.</param>
        public static void Apply(ClassificationResult result, List<PanelPart> hpParts, List<PanelPart> fbParts,
            IReadOnlyDictionary<string, PartCorrection> corrections = null)
        {
            double maxStiff = GirderEndDetector.HpMaxSpan(result);
            if (maxStiff <= 0) maxStiff = ClassifyUtil.GE_MAX_L;

            var work = new Queue<PanelPart>(result.OfType(PartType.Web)
                .Where(p => p.MemberLength >= ClassifyUtil.WEB_MIN_LEN && ClassifyUtil.IsRectangle(p)));

            while (work.Count > 0)
            {
                var p = work.Dequeue();
                var (e1, e2) = ClassifyUtil.Endpoints(p);
                var intent = IntentOf(p, corrections);

                foreach (var (e, far) in new[] { (e1, e2), (e2, e1) })
                {
                    string kind = TouchKind(p, e, hpParts, fbParts, out PanelPart endMember);
                    if (kind == null) continue;
                    if (!p.EndTouchKinds.Contains(kind)) p.EndTouchKinds.Add(kind);

                    Vector3d dir = (far - e).GetNormal();
                    double t = NearestPerpWebEdge(p, e, dir, result);       // −1 nếu không có
                    // Mốc toe tính cho MỌI đầu chạm (kể cả khi tự động ra GE) — tab Result cần biết có ép GB được không.
                    double s = NearestToeAnchor(p, e, dir, t > 0 ? t : p.MemberLength, hpParts, fbParts, endMember,
                        out var anchor, out var how);
                    if (t > 0) p.GeAvailable = true;
                    if (s >= 0) p.GbAvailable = true;

                    bool isGe = t > 0 && t < maxStiff;
                    if (intent == CorrectionIntent.ForceWeb)
                    {
                        result.Notes.Add($"[Correction] {p.SourceKey}: ép Web → không tách GE/GB ở đầu ({e.X:0},{e.Y:0}).");
                        continue;
                    }
                    if (intent == CorrectionIntent.ForceGE)
                    {
                        if (t > 0) isGe = true;
                        else result.Notes.Add($"[Correction] {p.SourceKey}: ép GE không áp được (không có Web vuông góc) → giữ tự động.");
                    }
                    if (intent == CorrectionIntent.ForceGB)
                    {
                        if (s >= 0) isGe = false;
                        else result.Notes.Add($"[Correction] {p.SourceKey}: ép GB không áp được (không có mốc toe) → giữ tự động.");
                    }

                    double zone;                                            // chiều dài vùng GE/GB tính từ đầu end
                    string tag;
                    string anchorInfo = "";                                 // GB: mốc toe đã dùng (ghi Notes)

                    if (isGe)
                    {
                        zone = t; tag = "GE";
                    }
                    else
                    {
                        if (s < 0)
                        {
                            result.Notes.Add($"[GirderEndSplitter] {p.Id.Handle}: đầu ({e.X:0},{e.Y:0}) chạm {kind}, L " +
                                (t > 0 ? $"={t:0}" : "(không có Web vuông góc)") + $" ≥ Max Stiff {maxStiff:0} → GB nhưng KHÔNG có HP/FB xuyên " +
                                "hoặc dừng ≤ 50mm để định vị toe → giữ Web chữ nhật.");
                            continue;
                        }
                        zone = s + ClassifyUtil.GB_TOE_PAST_HP + ClassifyUtil.GB_TOE_PAD; tag = "GB";
                        anchorInfo = $" (mốc toe {anchor.Id.Handle} {how})";
                    }

                    if (zone >= p.MemberLength - 1.0)
                    {
                        p.Type = PartType.GirderEnd;
                        p.Code = tag + "-" + kind;
                        result.Notes.Add($"[GirderEndSplitter] {p.Id.Handle}: cả mảnh là {p.Code} (zone={zone:0}, MaxStiff={maxStiff:0}){anchorInfo}.");
                        break;
                    }

                    var ge = Segment(p, e, dir, 0, zone, PartType.GirderEnd);
                    ge.Code = tag + "-" + kind;
                    var rest = Segment(p, e, dir, zone, p.MemberLength, PartType.Web);
                    rest.EndTouchKinds = new List<string>(p.EndTouchKinds);
                    rest.GeAvailable = p.GeAvailable; rest.GbAvailable = p.GbAvailable;
                    rest.GroupId = p.GroupId;
                    ge.GroupId = -1;
                    ge.JoinWithWeb = rest;

                    // p có thể là phần còn lại của LẦN TÁCH TRƯỚC (đầu kia) — GE/GB trước đó đang trỏ JoinWithWeb vào p,
                    // nhưng p sắp bị thay khỏi Parts ⇒ trỏ sang `rest` (đoạn kề GE/GB đó), nếu không GE/GB đầu kia
                    // không hợp nhất / không vào web run của collar (review 2026-11-04).
                    foreach (var prev in result.Parts.Where(x => ReferenceEquals(x.JoinWithWeb, p)))
                        prev.JoinWithWeb = rest;

                    int idx = result.Parts.IndexOf(p);
                    if (idx >= 0) result.Parts[idx] = ge; else result.Parts.Add(ge);
                    result.Parts.Add(rest);
                    result.Notes.Add($"[GirderEndSplitter] {p.Id.Handle} (thk {p.FootprintThickness:0}, dài {p.MemberLength:0}) " +
                        $"đầu ({e.X:0},{e.Y:0}) chạm {kind}: L=" + (t > 0 ? $"{t:0}" : "—") + $" MaxStiff={maxStiff:0} → {ge.Code} zone={zone:0}{anchorInfo} " +
                        $"+ Web còn lại {rest.MemberLength:0}.");
                    work.Enqueue(rest);   // đầu KIA của phần còn lại có thể cũng chạm HP/FB
                    break;
                }
            }
        }

        private static CorrectionIntent IntentOf(PanelPart p, IReadOnlyDictionary<string, PartCorrection> corrections) =>
            corrections != null && p.SourceKey != null && corrections.TryGetValue(p.SourceKey, out var c)
                ? ResultCatalog.ParseIntent(c.Kind) : CorrectionIntent.None;

        private static bool Cross(PanelPart p, PanelPart h) => h.AxisDir.Length < 1e-9 || p.AxisDir.Length < 1e-9
            || Math.Abs(h.AxisDir.GetNormal().DotProduct(p.AxisDir.GetNormal())) <= HP_COS;

        /// <summary>"HP" / "FB" nếu đầu <paramref name="e"/> chạm HP/FB cắt ngang trục p; null nếu không.</summary>
        private static string TouchKind(PanelPart p, Point3d e, List<PanelPart> hpParts, List<PanelPart> fbParts, out PanelPart member)
        {
            member = hpParts.FirstOrDefault(h => Cross(p, h) && ClassifyUtil.DistToPart(e, h) <= ClassifyUtil.ADJ_TOL);
            if (member != null) return "HP";
            member = fbParts.FirstOrDefault(f => Cross(p, f) && ClassifyUtil.DistToPart(e, f) <= ClassifyUtil.ADJ_TOL);
            return member != null ? "FB" : null;
        }

        /// <summary>Khoảng t (dọc <paramref name="dir"/> từ đầu <paramref name="e"/>) tới mép GẦN của Web
        /// vuông góc, đầu Web chạm thân p. −1 nếu không có.</summary>
        private static double NearestPerpWebEdge(PanelPart p, Point3d e, Vector3d dir, ClassificationResult result)
        {
            double best = -1;
            foreach (var w in result.OfType(PartType.Web))
            {
                if (ReferenceEquals(w, p) || w.Vertices == null || w.Vertices.Length < 3) continue;
                if (w.AxisDir.Length < 1e-9) continue;
                if (Math.Abs(w.AxisDir.GetNormal().DotProduct(dir)) > PERP_COS) continue;

                var (w1, w2) = ClassifyUtil.Endpoints(w);
                if (ClassifyUtil.DistToPart(w1, p) > ClassifyUtil.ADJ_TOL
                 && ClassifyUtil.DistToPart(w2, p) > ClassifyUtil.ADJ_TOL) continue;   // không ngắt tại p

                double t = w.Vertices.Min(v => (v - e).DotProduct(dir));
                if (t <= 1.0) continue;
                if (best < 0 || t < best) best = t;
            }
            return best;
        }

        /// <summary>Khoảng s (dọc dir từ e) tới đường tâm MỐC TOE GB gần nhất trong (2·ADJ_TOL, limit); −1 nếu không có.
        /// Mốc = HP (dài &gt; HP_MIN_LEN) hoặc FB cắt ngang trục p (không phải member đầu end), và:
        ///  · XUYÊN: giao điểm với trục p nằm trong đoạn member; hoặc
        ///  · DỪNG: đầu member cách mặt web ≤ <see cref="ClassifyUtil.GB_ANCHOR_GAP"/> (kỹ sư 2026-11-04).
        /// FB xuyên web đã bị FlatBarSplitter cắt thành 2 đoạn dừng ở mặt web → nhận qua nhánh DỪNG (gap≈0).
        /// Toe luôn tính từ ĐƯỜNG TÂM mốc (+100 +60) như HP xuyên.</summary>
        private static double NearestToeAnchor(PanelPart p, Point3d e, Vector3d dir, double limit,
            List<PanelPart> hpParts, List<PanelPart> fbParts, PanelPart endMember, out PanelPart anchor, out string how)
        {
            double best = -1;
            anchor = null; how = null;
            var cands = hpParts.Where(h => h.MemberLength > ClassifyUtil.HP_MIN_LEN).Select(h => (m: h, isHp: true))
                .Concat(fbParts.Select(f => (m: f, isHp: false)));
            foreach (var (m, isHp) in cands)
            {
                if (ReferenceEquals(m, endMember) || !Cross(p, m)) continue;
                var (a, b) = ClassifyUtil.Endpoints(m);
                double hx = b.X - a.X, hy = b.Y - a.Y;
                double den = dir.X * hy - dir.Y * hx;
                if (Math.Abs(den) < 1e-9) continue;
                double ax = a.X - e.X, ay = a.Y - e.Y;
                double s = (ax * hy - ay * hx) / den;          // tham số dọc dir
                double u = (ax * dir.Y - ay * dir.X) / den;     // tham số dọc member [0,1]
                if (s <= ClassifyUtil.ADJ_TOL * 2 || s >= limit) continue;

                string tag;
                if (u >= -0.02 && u <= 1.02) tag = isHp ? "HP xuyên" : "FB xuyên";
                else
                {
                    double gap = NearEndGap(m, p);
                    if (gap > ClassifyUtil.GB_ANCHOR_GAP) continue;
                    // FB xuyên đã bị cắt 2 đoạn cùng Id, cả 2 sát mặt web → vẫn gọi "FB xuyên".
                    bool fbThrough = !isHp && gap <= 1.0
                        && fbParts.Any(o => !ReferenceEquals(o, m) && o.Id == m.Id && NearEndGap(o, p) <= 1.0);
                    tag = fbThrough ? "FB xuyên" : $"{(isHp ? "HP" : "FB")} dừng {gap:0}mm";
                }
                if (best < 0 || s < best) { best = s; anchor = m; how = tag; }
            }
            return best;
        }

        /// <summary>Khoảng cách từ đầu mút GẦN của <paramref name="m"/> tới cạnh thật của web <paramref name="p"/>.</summary>
        private static double NearEndGap(PanelPart m, PanelPart p)
        {
            var (a, b) = ClassifyUtil.Endpoints(m);
            return Math.Min(ClassifyUtil.DistToPart(a, p), ClassifyUtil.DistToPart(b, p));
        }

        /// <summary>Đoạn con [t0,t1] dọc dir từ origin của rectangle p. FromVertices=true: builder dựng
        /// từ Vertices (không clone polyline gốc).</summary>
        private static PanelPart Segment(PanelPart src, Point3d origin, Vector3d dir, double t0, double t1, PartType type)
        {
            Point3d a = origin + dir * t0, b = origin + dir * t1;
            var perp = new Vector3d(-dir.Y, dir.X, 0) * (src.FootprintThickness / 2.0);
            var verts = new[] { a + perp, b + perp, b - perp, a - perp };
            double minX = verts.Min(v => v.X), maxX = verts.Max(v => v.X);
            double minY = verts.Min(v => v.Y), maxY = verts.Max(v => v.Y);
            var centroid = new Point3d((a.X + b.X) / 2.0, (a.Y + b.Y) / 2.0, 0);
            bool horiz = (maxX - minX) >= (maxY - minY);
            return new PanelPart
            {
                Id = src.Id, IdPath = src.IdPath, Type = type, SourceKey = src.SourceKey,
                Layer = src.Layer, ColorIndex = src.ColorIndex, VertexCount = 4,
                FootprintThickness = src.FootprintThickness, MemberLength = t1 - t0,
                Area = (t1 - t0) * src.FootprintThickness,
                Centroid = centroid,
                Bounds = new Extents3d(new Point3d(minX, minY, 0), new Point3d(maxX, maxY, 0)),
                Vertices = verts, Transform = src.Transform, AxisDir = dir,
                IsHorizontal = horiz, PerpCenter = horiz ? centroid.Y : centroid.X,
                FromVertices = true,
            };
        }
    }
}
