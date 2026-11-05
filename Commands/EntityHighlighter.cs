using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Geometry;
using AcadApp = Autodesk.AutoCAD.ApplicationServices.Application;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Highlight + zoom tới 1 entity (kể cả LỒNG trong block) theo <see cref="FullSubentityPath"/>.
    /// Dùng cho grid "Result": click 1 dòng → sáng đúng đối tượng trên bản vẽ.
    /// Gọi từ WPF modeless nên tự LockDocument.
    /// </summary>
    internal static class EntityHighlighter
    {
        private const string LOG = "[EntityHighlighter]";
        private static List<ObjectId[]> _last;   // các đường dẫn đang sáng (chọn nhiều dòng) — để tắt trước khi bật cái mới
        private static Database _lastDb;   // drawing chứa _last — lưu lúc highlight, KHÔNG đọc lại từ id (id có thể đã free)

        /// <summary>Highlight solid theo <paramref name="path"/>; <paramref name="zoom"/> = true ⇒ zoom vào bbox (nút ⌖ / menu
        /// Zoom to Object). Chọn dòng trên tab Result chỉ highlight, KHÔNG zoom (kỹ sư 2026-11-05).
        /// Trả null khi thành công, hoặc THÔNG BÁO cho người dùng (solid thuộc drawing khác — vd build 716C rồi sang
        /// drawing mới build 716P; lỗi AutoCAD) — KHÔNG nuốt lỗi im lặng (ux-guardrails).</summary>
        public static string Show(ObjectId[] path, Extents3d bounds, bool zoom = true)
            => ShowMany(path == null ? new List<ObjectId[]>() : new List<ObjectId[]> { path }, bounds, zoom);

        /// <summary>Highlight NHIỀU solid (chọn nhiều dòng kiểu Excel — kỹ sư 2026-11-05); <paramref name="bounds"/> = bbox
        /// HỢP của vùng chọn (zoom). Đường dẫn thuộc drawing khác bị bỏ qua và BÁO (ux-guardrails luật 1).</summary>
        public static string ShowMany(IReadOnlyList<ObjectId[]> paths, Extents3d bounds, bool zoom = true)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return "No drawing is open.";
            var valid = (paths ?? new List<ObjectId[]>()).Where(p => p != null && p.Length > 0 && !p[0].IsNull).ToList();
            if (valid.Count == 0) { Clear(); return "This row has no solid to highlight."; }
            var here = valid.Where(p => p[0].Database == doc.Database).ToList();
            int other = valid.Count - here.Count;
            if (here.Count == 0)
            {
                Clear();
                return (valid.Count == 1 ? "This row belongs" : "These rows belong")
                       + " to another drawing — switch to that drawing, or run Build 3D / Load 3D here.";
            }

            try
            {
                int failed;
                using (doc.LockDocument())
                {
                    ClearCore(doc);
                    _last = here;   // ghi TRƯỚC khi bật: lỗi giữa chừng vẫn tắt được các path đã sáng (review)
                    _lastDb = doc.Database;
                    failed = SetHighlight(doc, here, true);
                    if (failed > 0) doc.Editor.WriteMessage($"\n{LOG} {failed} object(s) could not be highlighted.");
                    if (zoom && !ZoomTo(doc.Editor, bounds)) { doc.Editor.UpdateScreen(); return "Highlighted, but could not zoom to it (see the AutoCAD command line)."; }
                }
                doc.Editor.UpdateScreen();
                if (failed > 0) return $"{failed} selected object{(failed > 1 ? "s" : "")} could not be highlighted (see the AutoCAD command line).";
                return other > 0 ? $"{other} selected row{(other > 1 ? "s are" : " is")} in another drawing — not highlighted." : null;
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG} Show lỗi: {ex.Message}");
                doc.Editor.WriteMessage($"\n{LOG} {ex.Message}");
                return $"Could not highlight this object: {ex.Message}";
            }
        }

        // ─── Đánh dấu 1 ĐIỂM (đầu thanh HP/FB khi rê chuột lên nút Sniped End) — vòng tròn transient ───
        private static Circle _marker;
        private const double MARKER_R = 80.0;   // mm — đủ thấy ở zoom panel, không che chi tiết

        public static void ShowPoint(Point3d pt)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                ClearPoint();
                _marker = new Circle(pt, Vector3d.ZAxis, MARKER_R) { ColorIndex = 1 };
                Autodesk.AutoCAD.GraphicsInterface.TransientManager.CurrentTransientManager.AddTransient(
                    _marker, Autodesk.AutoCAD.GraphicsInterface.TransientDrawingMode.DirectTopmost, 128,
                    new IntegerCollection());
                doc.Editor.UpdateScreen();
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} ShowPoint lỗi: {ex.Message}"); }
        }

        public static void ClearPoint()
        {
            if (_marker == null) return;
            try
            {
                Autodesk.AutoCAD.GraphicsInterface.TransientManager.CurrentTransientManager.EraseTransient(
                    _marker, new IntegerCollection());
                _marker.Dispose();
                AcadApp.DocumentManager.MdiActiveDocument?.Editor.UpdateScreen();
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} ClearPoint lỗi: {ex.Message}"); }
            _marker = null;
        }

        /// <summary>Tắt highlight hiện tại (nếu có).</summary>
        public static void Clear()
        {
            // Lấy + XOÁ state TRƯỚC mọi thứ có thể thất bại (không có drawing, LockDocument lỗi) — review I3.
            var (last, lastDb) = Take();
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || last == null) return;
            try
            {
                using (doc.LockDocument()) Unhighlight(doc, last, lastDb);
                doc.Editor.UpdateScreen();
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} Clear lỗi: {ex.Message}"); }
        }

        /// <summary>Drawing sắp đóng (DocumentToBeDestroyed): quên highlight của nó mà KHÔNG chạm vào id — sau khi
        /// Database giải phóng, đọc id cũ là truy cập bộ nhớ đã free (AutoCAD có thể crash).</summary>
        public static void ForgetDatabase(Database db)
        {
            if (db != null && ReferenceEquals(_lastDb, db)) { _last = null; _lastDb = null; }
        }

        private static (List<ObjectId[]> paths, Database db) Take()
        {
            var r = (_last, _lastDb);
            _last = null; _lastDb = null;
            return r;
        }

        /// <summary>Tắt highlight cũ trên ĐÚNG drawing chứa nó (_last là static, dùng chung mọi drawing: mở id của
        /// drawing A bằng transaction drawing B ⇒ eWrongDatabase làm hỏng mọi highlight sau đó — lỗi 2026-11-05).
        /// Drawing đó đã đóng ⇒ bỏ qua. Luôn xoá _last.</summary>
        private static void ClearCore(Document doc)
        {
            var (last, lastDb) = Take();
            Unhighlight(doc, last, lastDb);
        }

        private static void Unhighlight(Document doc, List<ObjectId[]> last, Database lastDb)
        {
            if (last == null || last.Count == 0 || lastDb == null) return;
            try
            {
                Document owner = ReferenceEquals(lastDb, doc.Database) ? doc : FindDocument(lastDb);
                if (owner == null) return;   // drawing đã đóng
                void Off() => SetHighlight(owner, last, false);
                if (owner == doc) Off();
                else using (owner.LockDocument()) Off();
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} ClearCore lỗi: {ex.Message}"); }
        }

        private static Document FindDocument(Database db)
        {
            foreach (Document d in AcadApp.DocumentManager) if (d.Database == db) return d;
            return null;
        }

        /// <summary>Bật/tắt highlight NHIỀU đường dẫn trong MỘT transaction (chọn vài trăm dòng — review: trước đó mỗi
        /// đường dẫn 1 transaction). Trả số đường dẫn lỗi (đã bỏ qua) để caller báo người dùng.</summary>
        private static int SetHighlight(Document doc, IEnumerable<ObjectId[]> paths, bool on)
        {
            int failed = 0;
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                foreach (var path in paths)
                {
                    try
                    {
                        // ⚠️ Highlight entity LỒNG phải gọi trên entity NGOÀI CÙNG (BlockReference = path[0]),
                        // truyền cả FullSubentityPath — KHÔNG gọi trên polyline lá.
                        if (path == null || path.Length == 0) continue;
                        ObjectId rootId = path[0];
                        if (rootId.IsNull || rootId.IsErased) continue;
                        var root = tr.GetObject(rootId, OpenMode.ForRead) as Entity;
                        if (root == null) continue;
                        if (path.Length == 1) { if (on) root.Highlight(); else root.Unhighlight(); }
                        else
                        {
                            var sp = new FullSubentityPath(path, new SubentityId(SubentityType.Null, IntPtr.Zero));
                            if (on) root.Highlight(sp, false); else root.Unhighlight(sp, false);
                        }
                    }
                    catch (System.Exception ex) { failed++; Debug.WriteLine($"{LOG} SetHighlight lỗi: {ex.Message}"); }
                }
                tr.Commit();
            }
            return failed;
        }

        /// <summary>Zoom viewport hiện tại vào bbox WCS + lề để giữ ngữ cảnh. Chiếu bbox sang DCS của view
        /// hiện tại ⇒ đúng cả plan-view (2D) lẫn view 3D/isometric (solid sau Build).</summary>
        private static bool ZoomTo(Editor ed, Extents3d ext)
        {
            try
            {
                using (ViewTableRecord view = ed.GetCurrentView())
                {
                    Matrix3d wcsToDcs = (Matrix3d.Rotation(-view.ViewTwist, view.ViewDirection, view.Target)
                                         * Matrix3d.Displacement(view.Target - Point3d.Origin)
                                         * Matrix3d.PlaneToWorld(view.ViewDirection)).Inverse();

                    // Bbox trong DCS = bao 8 góc bbox WCS đã chiếu. Khởi tạo bằng góc đầu — Extents3d
                    // mặc định là (0,0,0) nên AddPoint sẽ kéo cả gốc toạ độ vào.
                    Point3d a = ext.MinPoint, b = ext.MaxPoint;
                    Point3d first = a.TransformBy(wcsToDcs);
                    var dcs = new Extents3d(first, first);
                    foreach (double x in new[] { a.X, b.X })
                    foreach (double y in new[] { a.Y, b.Y })
                    foreach (double z in new[] { a.Z, b.Z })
                        dcs.AddPoint(new Point3d(x, y, z).TransformBy(wcsToDcs));

                    double w = dcs.MaxPoint.X - dcs.MinPoint.X;
                    double h = dcs.MaxPoint.Y - dcs.MinPoint.Y;
                    if (w <= 0 && h <= 0) { ed.WriteMessage($"{LOG} This object has no extents to zoom to.\n"); return false; }

                    const double margin = 4.0;   // hiện cả phần xung quanh để định vị
                    view.CenterPoint = new Point2d((dcs.MinPoint.X + dcs.MaxPoint.X) / 2.0,
                                                   (dcs.MinPoint.Y + dcs.MaxPoint.Y) / 2.0);
                    view.Width = Math.Max(w * margin, 200.0);
                    view.Height = Math.Max(h * margin, 200.0);
                    ed.SetCurrentView(view);
                    return true;
                }
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG} ZoomTo lỗi: {ex.Message}");
                ed.WriteMessage($"\n{LOG} ZoomTo: {ex.Message}");
                return false;
            }
        }
    }
}
