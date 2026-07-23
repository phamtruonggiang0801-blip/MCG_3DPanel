using Autodesk.AutoCAD.DatabaseServices;

namespace MCG_3DPanel.Services
{
    /// <summary>
    /// Interface cho InspectorService.
    /// Duyệt đệ quy toàn bộ đối tượng bên trong 1 Block Panel (kể cả Nesting Block)
    /// và xuất dữ liệu thật ra file JSON để đối chiếu quy tắc nhận diện.
    /// </summary>
    public interface IInspectorService
    {
        /// <summary>
        /// Inspect block đã chọn, ghi JSON ra <paramref name="outputPath"/>.
        /// Trả về đường dẫn file đã ghi.
        /// </summary>
        string InspectToJson(ObjectId blockRefId, string outputPath);
    }
}
