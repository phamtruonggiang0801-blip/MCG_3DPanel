namespace MCG_3DPanel.Models
{
    /// <summary>
    /// Vị trí Panel — quyết định "chiều đổ" kết cấu (để chia OB/IB).
    /// Xem skill bracket-identification.
    /// </summary>
    public enum PanelPosition
    {
        Center = 0,     // Y: về tâm Panel; X: trái→phải
        Port,           // Y: trên→dưới;   X: trái→phải
        Starboard,      // Y: dưới→lên;     X: trái→phải
    }
}
