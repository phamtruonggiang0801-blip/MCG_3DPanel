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

        /// <summary>Tắt highlight cũ, bật highlight + zoom entity ở <paramref name="path"/>.</summary>
        public static void Show(ObjectId[] path, Extents3d bounds)
        {
            Document doc = AcadApp.DocumentManager.MdiActiveDocument;
            if (doc == null || path == null || path.Length == 0) return;

            try
            {
                using (doc.LockDocument())
                {
                    ClearCore(doc);
                    SetHighlight(doc, path, true);
                    _last = path;
                    ZoomTo(doc.Editor, bounds);
                }
                doc.Editor.UpdateScreen();
            }
            catch (System.Exception ex)
            {
                Debug.WriteLine($"{LOG} Show lỗi: {ex.Message}");
            }
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

        private static void ClearCore(Document doc)
        {
            if (_last != null) { SetHighlight(doc, _last, false); _last = null; }
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
        private static void ZoomTo(Editor ed, Extents3d ext)
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
                    if (w <= 0 && h <= 0) return;

                    const double margin = 4.0;   // hiện cả phần xung quanh để định vị
                    view.CenterPoint = new Point2d((dcs.MinPoint.X + dcs.MaxPoint.X) / 2.0,
                                                   (dcs.MinPoint.Y + dcs.MaxPoint.Y) / 2.0);
                    view.Width = Math.Max(w * margin, 200.0);
                    view.Height = Math.Max(h * margin, 200.0);
                    ed.SetCurrentView(view);
                }
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} ZoomTo lỗi: {ex.Message}"); }
        }
    }
}
