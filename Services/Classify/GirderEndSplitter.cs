using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Models;

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
    ///    Không có HP xuyên trước Web vuông góc ⇒ giữ Web, Code "GB?", ghi Notes.
    /// GE/GB + Web còn lại là 1 tấm liền: <see cref="PanelPart.JoinWithWeb"/> để Build3DService hợp nhất.
    /// </summary>
    internal static class GirderEndSplitter
    {
        private const double PERP_COS = 0.30;   // |cos| ≤ ngưỡng ⇒ vuông góc (Web ⊥ girder)
        private const double HP_COS = 0.70;     // |cos| ≤ ngưỡng ⇒ HP/FB cắt ngang (không chạy dọc theo girder)

        public static void Apply(ClassificationResult result, List<PanelPart> hpParts, List<PanelPart> fbParts)
        {
            double maxStiff = GirderEndDetector.HpMaxSpan(result);
            if (maxStiff <= 0) maxStiff = ClassifyUtil.GE_MAX_L;

            var work = new Queue<PanelPart>(result.OfType(PartType.Web)
                .Where(p => p.MemberLength >= ClassifyUtil.WEB_MIN_LEN && ClassifyUtil.IsRectangle(p)));

            while (work.Count > 0)
            {
                var p = work.Dequeue();
                var (e1, e2) = ClassifyUtil.Endpoints(p);

                foreach (var (e, far) in new[] { (e1, e2), (e2, e1) })
                {
                    string kind = TouchKind(p, e, hpParts, fbParts, out PanelPart endMember);
                    if (kind == null) continue;

                    Vector3d dir = (far - e).GetNormal();
                    double t = NearestPerpWebEdge(p, e, dir, result);       // −1 nếu không có
                    bool isGe = t > 0 && t < maxStiff;
                    double zone;                                            // chiều dài vùng GE/GB tính từ đầu end
                    string tag;

                    if (isGe)
                    {
                        zone = t; tag = "GE";
                    }
                    else
                    {
                        double limit = t > 0 ? t : p.MemberLength;
                        double s = NearestCrossingHp(p, e, dir, limit, hpParts, endMember);
                        if (s < 0)
                        {
                            p.Code = "GB?";
                            result.Notes.Add($"[GirderEndSplitter] {p.Id.Handle}: đầu ({e.X:0},{e.Y:0}) chạm {kind}, L " +
                                (t > 0 ? $"={t:0}" : "(không có Web vuông góc)") + $" ≥ Max Stiff {maxStiff:0} → GB nhưng KHÔNG có HP xuyên " +
                                "để định vị toe → giữ Web chữ nhật.");
                            continue;
                        }
                        zone = s + ClassifyUtil.GB_TOE_PAST_HP + ClassifyUtil.GB_TOE_PAD; tag = "GB";
                    }

                    if (zone >= p.MemberLength - 1.0)
                    {
                        p.Type = PartType.GirderEnd;
                        p.Code = tag + "-" + kind;
                        result.Notes.Add($"[GirderEndSplitter] {p.Id.Handle}: cả mảnh là {p.Code} (zone={zone:0}, MaxStiff={maxStiff:0}).");
                        break;
                    }

                    var ge = Segment(p, e, dir, 0, zone, PartType.GirderEnd);
                    ge.Code = tag + "-" + kind;
                    var rest = Segment(p, e, dir, zone, p.MemberLength, PartType.Web);
                    rest.GroupId = p.GroupId;
                    ge.GroupId = -1;
                    ge.JoinWithWeb = rest;

                    int idx = result.Parts.IndexOf(p);
                    if (idx >= 0) result.Parts[idx] = ge; else result.Parts.Add(ge);
                    result.Parts.Add(rest);
                    result.Notes.Add($"[GirderEndSplitter] {p.Id.Handle} (thk {p.FootprintThickness:0}, dài {p.MemberLength:0}) " +
                        $"đầu ({e.X:0},{e.Y:0}) chạm {kind}: L=" + (t > 0 ? $"{t:0}" : "—") + $" MaxStiff={maxStiff:0} → {ge.Code} zone={zone:0} " +
                        $"+ Web còn lại {rest.MemberLength:0}.");
                    work.Enqueue(rest);   // đầu KIA của phần còn lại có thể cũng chạm HP/FB
                    break;
                }
            }
        }

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

        /// <summary>Khoảng s (dọc dir từ e) tới đường tâm HP xuyên GẦN NHẤT (không phải HP/FB đầu end) trong (0, limit).
        /// −1 nếu không có. HP "xuyên" = cắt ngang trục p, giao điểm nằm trong đoạn HP.</summary>
        private static double NearestCrossingHp(PanelPart p, Point3d e, Vector3d dir, double limit,
            List<PanelPart> hpParts, PanelPart endMember)
        {
            double best = -1;
            foreach (var h in hpParts)
            {
                if (ReferenceEquals(h, endMember) || h.MemberLength <= ClassifyUtil.HP_MIN_LEN) continue;
                if (!Cross(p, h)) continue;
                var (a, b) = ClassifyUtil.Endpoints(h);
                double hx = b.X - a.X, hy = b.Y - a.Y;
                double den = dir.X * hy - dir.Y * hx;
                if (Math.Abs(den) < 1e-9) continue;
                double ax = a.X - e.X, ay = a.Y - e.Y;
                double s = (ax * hy - ay * hx) / den;          // tham số dọc dir
                double u = (ax * dir.Y - ay * dir.X) / den;     // tham số dọc HP [0,1]
                if (u < -0.02 || u > 1.02) continue;
                if (s <= ClassifyUtil.ADJ_TOL * 2 || s >= limit) continue;
                if (best < 0 || s < best) best = s;
            }
            return best;
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
                Id = src.Id, IdPath = src.IdPath, Type = type,
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
