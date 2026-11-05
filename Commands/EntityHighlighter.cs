using System;
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
        private static ObjectId[] _last;   // đường dẫn đang sáng — để tắt trước khi bật cái mới

        /// <summary>Highlight solid theo <paramref name="path"/>; <paramref name="zoom"/> = true ⇒ zoom vào bbox (nút ⌖ / menu
        /// Zoom to Object). Chọn dòng trên tab Result chỉ highlight, KHÔNG zoom (kỹ sư 2026-11-05).
        /// Trả null khi thành công, hoặc THÔNG BÁO cho người dùng (solid thuộc drawing khác — vd build 716C rồi sang
        /// drawing mới build 716P; lỗi AutoCAD) — KHÔNG nuốt lỗi im lặng (ux-guardrails).</summary>
        public static string Show(ObjectId[] path, Extents3d bounds, bool zoom = true)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return "No drawing is open.";
            if (path == null || path.Length == 0) return "This row has no solid to highlight.";
            if (path[0].Database != doc.Database)
            {
                Clear();
                return "This row belongs to another drawing — switch to that drawing, or run Build 3D / Load 3D here.";
            }

            try
            {
                using (doc.LockDocument())
                {
                    ClearCore(doc);
                    SetHighlight(doc, path, true);
                    _last = path;
                    if (zoom && !ZoomTo(doc.Editor, bounds)) { doc.Editor.UpdateScreen(); return "Highlighted, but could not zoom to it (see the AutoCAD command line)."; }
                }
                doc.Editor.UpdateScreen();
                return null;
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
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            try
            {
                using (doc.LockDocument()) ClearCore(doc);
                doc.Editor.UpdateScreen();
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} Clear lỗi: {ex.Message}"); }
        }

        /// <summary>Tắt highlight cũ trên ĐÚNG drawing chứa nó (_last là static, dùng chung mọi drawing: mở id của
        /// drawing A bằng transaction drawing B ⇒ eWrongDatabase làm hỏng mọi highlight sau đó — lỗi 2026-11-05).
        /// Drawing đó đã đóng ⇒ bỏ qua. Luôn xoá _last.</summary>
        private static void ClearCore(Document doc)
        {
            var last = _last;
            _last = null;
            if (last == null || last.Length == 0 || last[0].IsNull) return;
            try
            {
                Database db = last[0].Database;
                Document owner = db == doc.Database ? doc : FindDocument(db);
                if (owner == null) return;
                if (owner == doc) SetHighlight(owner, last, false);
                else using (owner.LockDocument()) SetHighlight(owner, last, false);
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} ClearCore lỗi: {ex.Message}"); }
        }

        private static Document FindDocument(Database db)
        {
            foreach (Document d in AcadApp.DocumentManager) if (d.Database == db) return d;
            return null;
        }

        private static void SetHighlight(Document doc, ObjectId[] path, bool on)
        {
            using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
            {
                // ⚠️ Highlight entity LỒNG phải gọi trên entity NGOÀI CÙNG (BlockReference = path[0]),
                // truyền cả FullSubentityPath — KHÔNG gọi trên polyline lá.
                ObjectId rootId = path[0];
                if (rootId.IsNull || rootId.IsErased) { tr.Commit(); return; }

                var root = tr.GetObject(rootId, OpenMode.ForRead) as Entity;
                if (root != null)
                {
                    if (path.Length == 1)
                    {
                        if (on) root.Highlight(); else root.Unhighlight();
                    }
                    else
                    {
                        var sp = new FullSubentityPath(path, new SubentityId(SubentityType.Null, IntPtr.Zero));
                        if (on) root.Highlight(sp, false); else root.Unhighlight(sp, false);
                    }
                }
                tr.Commit();
            }
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
                    if (w <= 0 && h <= 0) return false;

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
