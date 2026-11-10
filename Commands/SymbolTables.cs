using Autodesk.AutoCAD.DatabaseServices;

namespace MCG_3DPanel.Commands
{
    /// <summary>Tra bảng ký hiệu theo tên, CHỈ bản ghi còn sống. <c>SymbolTable.Has(name)</c> vẫn true với bản ghi đã xoá (vd block chép
    /// từ CAS.dwt trong 1 lần chạy bị huỷ transaction ⇒ rollback thành erased), còn <c>table[name]</c> bỏ qua bản ghi xoá và ném
    /// eKeyNotFound (lỗi Plan View 2026-11-10: PlanViewWriter.InsertBlock).</summary>
    internal static class SymbolTables
    {
        public static ObjectId Live(SymbolTable table, string name)
        {
            if (string.IsNullOrEmpty(name) || !table.Has(name)) return ObjectId.Null;
            try
            {
                ObjectId id = table[name];
                return id.IsErased ? ObjectId.Null : id;
            }
            catch (Autodesk.AutoCAD.Runtime.Exception) { return ObjectId.Null; }   // chỉ còn bản ghi đã xoá
        }

        public static bool HasLive(SymbolTable table, string name) => !Live(table, name).IsNull;

        /// <summary>Bản ghi ĐÃ XOÁ mang tên <paramref name="name"/> vẫn giữ tên ⇒ chép từ template (Ignore) bị bỏ qua / tạo mới báo trùng tên.
        /// Đổi tên chúng (khôi phục tạm → đổi tên → xoá lại) để tên trống cho bản ghi mới. Trả số bản ghi đã đổi.</summary>
        public static int FreeErasedName(Transaction tr, SymbolTable table, string name)
        {
            int n = 0;
            foreach (ObjectId id in table.IncludingErased)
            {
                if (!id.IsErased) continue;
                var rec = (SymbolTableRecord)tr.GetObject(id, OpenMode.ForWrite, true);
                if (!string.Equals(rec.Name, name, System.StringComparison.OrdinalIgnoreCase)) continue;
                rec.Erase(false);
                rec.Name = name + "$erased$" + rec.Handle;
                rec.Erase(true);
                n++;
            }
            return n;
        }

        /// <summary>Như indexer, nhưng báo rõ tên khi thiếu (thay cho eKeyNotFound trống trơn).</summary>
        public static ObjectId Require(SymbolTable table, string name, string kind)
        {
            ObjectId id = Live(table, name);
            if (id.IsNull) throw new System.InvalidOperationException($"{kind} '{name}' is missing from the drawing.");
            return id;
        }
    }
}
