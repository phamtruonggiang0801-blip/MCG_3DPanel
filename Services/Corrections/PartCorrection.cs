namespace MCG_3DPanel.Services.Corrections
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests để test tuần tự hoá JSON).

    /// <summary>Correction người dùng đặt cho 1 part 2D trên tab Result.</summary>
    public sealed class PartCorrection
    {
        /// <summary>Kind mới (xem <see cref="UI.ResultCatalog"/>); null/rỗng ⇒ không đổi loại.</summary>
        public string Kind { get; set; }
        /// <summary>Đảo chiều đổ: HP → đảo bụng; Bracket → đảo hướng gấp lip.</summary>
        public bool FlipDir { get; set; }
        /// <summary>Kind tool nhận diện TRƯỚC correction đầu tiên — cột DetectedKind khi Export; chọn lại đúng
        /// Kind này ⇒ huỷ correction Kind.</summary>
        public string OriginalKind { get; set; }
        /// <summary>Sniped End ép theo từng đầu (HP/FB): lo = đầu L (ngang) / B (đứng), hi = R / T.
        /// null = để tool tự quyết; true = vát; false = để vuông (End 30 cũng không ép bracket B).</summary>
        public bool? SnipLo { get; set; }
        public bool? SnipHi { get; set; }
        /// <summary>Tripping bracket ép theo HP (kỹ sư 2026-11-05): null = Auto (luật 3 × bước max); true = đặt ở
        /// MỌI giao điểm HP xuyên web dầm có flange; false = bỏ hết bracket trên HP này. Không tính lại Auto.</summary>
        public bool? Tripping { get; set; }
        /// <summary>Collar Plate ép theo HP (kỹ sư 2026-11-05): null = Auto; true = collar ở MỌI lỗ CO của HP; false = bỏ hết.</summary>
        public bool? Collar { get; set; }
        /// <summary>Chiều cao riêng của 1 FB (mm, kỹ sư 2026-11-05 — chiều dày vẫn theo 2D); null = theo tab Input.</summary>
        public double? FbHeight { get; set; }

        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsEmpty => string.IsNullOrWhiteSpace(Kind) && !FlipDir && SnipLo == null && SnipHi == null && Tripping == null && Collar == null && FbHeight == null;
    }
}
