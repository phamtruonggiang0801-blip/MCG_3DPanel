using System;
using System.IO;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using MCG_3DPanel.Services;

namespace MCG_3DPanel.Commands
{
    /// <summary>Tìm bản vẽ 2D đã dựng ra panel của file 3D (spec "Finding the 2D drawing"): đường dẫn lưu trong PanelMeta, phải chứa
    /// reference có handle = SourceRefHandle với định nghĩa = SourceBtrHandle. Không khớp / file cũ chưa lưu đường dẫn ⇒ hỏi chọn file,
    /// kiểm lại, rồi lưu đường dẫn mới vào file 3D.</summary>
    public static class Source2DLocator
    {
        /// <returns>Đường dẫn đã kiểm; null ⇒ <paramref name="reason"/> (người dùng huỷ hoặc file không khớp).</returns>
        public static string Find(Document doc3D, BuildResult build, out string reason)
        {
            reason = null;
            string refH = build.SourceRefId.Handle.ToString(), btrH = build.SourceBtrId.Handle.ToString();
            string panel = build.PanelBlockName ?? "panel";

            if (!string.IsNullOrEmpty(build.SourcePath) && Holds(build.SourcePath, refH, btrH)) return build.SourcePath;

            string dir = Path.GetDirectoryName(string.IsNullOrEmpty(build.SourcePath) ? doc3D.Name : build.SourcePath);
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = $"Select the 2D drawing of {panel}", Filter = "Drawing (*.dwg)|*.dwg",
                InitialDirectory = Directory.Exists(dir) ? dir : "",
            };
            if (dlg.ShowDialog() != true) { reason = "Create Drawing cancelled."; return null; }
            string picked = dlg.FileName;
            if (!Holds(picked, refH, btrH))
            {
                reason = $"{Path.GetFileName(picked)} does not contain the 2D block {panel} was built from.";
                return null;
            }
            // Lưu đường dẫn mới vào file 3D (meta MCG3D_PANEL) — lần sau không hỏi lại.
            build.SourcePath = picked;
            using (doc3D.LockDocument()) PanelCommands.WritePanelMeta(doc3D.Database, build);
            return picked;
        }

        /// <summary>File chứa BlockReference handle <paramref name="refH"/> trỏ tới định nghĩa handle <paramref name="btrH"/>. Đang mở ⇒ đọc
        /// database của document; chưa mở ⇒ ReadDwgFile chỉ đọc. Không ném.</summary>
        public static bool Holds(string path, string refH, string btrH)
        {
            try
            {
                if (!File.Exists(path)) return false;
                Document open = PanelCommands.FindOpen(path);
                if (open != null) return Check(open.Database, refH, btrH);
                using (var db = new Database(false, true))
                {
                    db.ReadDwgFile(path, FileOpenMode.OpenForReadAndAllShare, true, "");
                    return Check(db, refH, btrH);
                }
            }
            catch { return false; }
        }

        private static bool Check(Database db, string refH, string btrH)
        {
            if (!db.TryGetObjectId(new Handle(Convert.ToInt64(refH, 16)), out ObjectId id) || id.IsErased) return false;
            using (Transaction tr = db.TransactionManager.StartOpenCloseTransaction())
            {
                if (!(tr.GetObject(id, OpenMode.ForRead) is BlockReference br)) return false;
                bool ok = string.Equals(br.BlockTableRecord.Handle.ToString(), btrH, StringComparison.OrdinalIgnoreCase);
                tr.Commit();
                return ok;
            }
        }
    }
}
