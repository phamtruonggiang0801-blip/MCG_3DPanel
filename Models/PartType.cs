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
    }
}
