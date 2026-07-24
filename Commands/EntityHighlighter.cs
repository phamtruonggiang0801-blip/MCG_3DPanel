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

        /// <summary>Zoom viewport hiện tại vào bbox (WCS, plan-view) + lề để giữ ngữ cảnh.</summary>
        private static void ZoomTo(Editor ed, Extents3d ext)
        {
            try
            {
                double w = ext.MaxPoint.X - ext.MinPoint.X;
                double h = ext.MaxPoint.Y - ext.MinPoint.Y;
                if (w <= 0 && h <= 0) return;

                double cx = (ext.MinPoint.X + ext.MaxPoint.X) / 2.0;
                double cy = (ext.MinPoint.Y + ext.MaxPoint.Y) / 2.0;
                const double margin = 4.0;   // hiện cả phần xung quanh để định vị
                double vw = Math.Max(w * margin, 200.0);
                double vh = Math.Max(h * margin, 200.0);

                using (ViewTableRecord view = ed.GetCurrentView())
                {
                    view.CenterPoint = new Point2d(cx, cy);
                    view.Width = vw;
                    view.Height = vh;
                    ed.SetCurrentView(view);
                }
            }
            catch (System.Exception ex) { Debug.WriteLine($"{LOG} ZoomTo lỗi: {ex.Message}"); }
        }
    }
}
