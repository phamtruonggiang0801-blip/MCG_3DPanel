using System.Collections.Generic;
using Autodesk.AutoCAD.DatabaseServices;

namespace MCG_3DPanel.Services.Corrections
{
    /// <summary>
    /// Kho correction trong BỘ NHỚ (chỉ sống trong phiên mở bản vẽ — kỹ sư chốt 2026-11-04).
    /// Khoá: panelKey (block reference 2D nguồn, xem <see cref="PanelKey"/>) → SourceKey của part 2D.
    /// </summary>
    public static class CorrectionStore
    {
        private static readonly Dictionary<string, Dictionary<string, PartCorrection>> _store =
            new Dictionary<string, Dictionary<string, PartCorrection>>();

        private static readonly IReadOnlyDictionary<string, PartCorrection> Empty = new Dictionary<string, PartCorrection>();

        /// <summary>Khoá panel = Database đang mở + handle block reference 2D (handle trùng được giữa 2 bản vẽ).</summary>
        public static string PanelKey(ObjectId src2D) =>
            src2D.IsNull ? "" : $"{src2D.Database.UnmanagedObject.ToInt64():X}:{src2D.Handle}";

        public static IReadOnlyDictionary<string, PartCorrection> For(string panelKey) =>
            panelKey != null && _store.TryGetValue(panelKey, out var d) ? d : Empty;

        public static PartCorrection Get(string panelKey, string sourceKey) =>
            sourceKey != null && For(panelKey).TryGetValue(sourceKey, out var c) ? c : null;

        public static void Set(string panelKey, string sourceKey, PartCorrection c)
        {
            if (string.IsNullOrEmpty(panelKey) || string.IsNullOrEmpty(sourceKey)) return;
            if (c == null || c.IsEmpty) { Clear(panelKey, sourceKey); return; }
            if (!_store.TryGetValue(panelKey, out var d)) _store[panelKey] = d = new Dictionary<string, PartCorrection>();
            d[sourceKey] = c;
        }

        /// <summary>Bản vẽ sắp đóng: bỏ mọi correction của Database đó (con trỏ có thể bị DB mới dùng lại).</summary>
        public static void ClearDatabase(Database db)
        {
            if (db == null) return;
            string prefix = $"{db.UnmanagedObject.ToInt64():X}:";
            foreach (var k in new List<string>(_store.Keys))
                if (k.StartsWith(prefix)) _store.Remove(k);
        }

        public static void Clear(string panelKey, string sourceKey)
        {
            if (panelKey != null && sourceKey != null && _store.TryGetValue(panelKey, out var d)) d.Remove(sourceKey);
        }
    }
}
