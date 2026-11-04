namespace MCG_3DPanel.Services.Corrections
{
    /// <summary>Correction người dùng đặt cho 1 part 2D trên tab Result.</summary>
    public sealed class PartCorrection
    {
        /// <summary>Kind mới (xem <see cref="UI.ResultCatalog"/>); null/rỗng ⇒ không đổi loại.</summary>
        public string Kind { get; set; }
        /// <summary>Đảo chiều đổ: HP → đảo bụng; Bracket → đảo hướng gấp lip.</summary>
        public bool FlipDir { get; set; }

        public bool IsEmpty => string.IsNullOrWhiteSpace(Kind) && !FlipDir;
    }
}
