namespace MCG_3DPanel.Models
{
    /// <summary>
    /// 5 loại bộ phận chính của Panel + Bracket (tách từ layer Web) + Unknown.
    /// Quy tắc nhận diện: xem skill panel-parts-reference.
    /// </summary>
    public enum PartType
    {
        Unknown = 0,
        TopPlate,
        Web,
        Flange,
        FlatBar,
        HP,
        Bracket,
        FacePlate,
        GirderEnd,   // đầu Web (girder) tại HP/FB — nối tiếp Web, xem skill girder-end

        CutOut,       // lỗ khoét trên Top Plate — loop kín cùng block Top Plate, không chạm mép
        ClosedPlate,  // tấm kín (rộng×dài plan, cao = building height) — AM_0 chữ nhật, nằm trên FacePlate
        BackingPlate, // tấm đệm hàn trên web — AM_0 có snip 2 đầu (15mm → chéo 60° so cạnh đứng), cách TP/flange 30mm
    }
}
