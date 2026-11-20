using System;
using System.IO;

namespace MCG_3DPanel.Commands
{
    /// <summary>Vết chẩn đoán (2026-11-20, lỗi tràn stack khi Plan View từ bản 2D): mỗi dòng ghi thẳng xuống
    /// %TEMP%\MCG_3DPanel_trace.log — AutoCAD chết vì tràn stack thì vết vẫn còn. Không bao giờ ném.</summary>
    internal static class TraceLog
    {
        static readonly string PathLog = Path.Combine(Path.GetTempPath(), "MCG_3DPanel_trace.log");

        public static void Write(string msg)
        {
            try { File.AppendAllText(PathLog, $"{DateTime.Now:HH:mm:ss.fff} [{Environment.StackTrace.Split('\n').Length,4}] {msg}{Environment.NewLine}"); }
            catch { }
        }
    }
}
