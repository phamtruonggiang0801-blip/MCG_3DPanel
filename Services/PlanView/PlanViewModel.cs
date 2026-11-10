using System.Collections.Generic;

namespace MCG_3DPanel.Services.PlanView
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    /// <summary>Hệ toạ độ của 1 phần tử: Block = toạ độ block 2D panel (writer áp transform của reference _Assy),
    /// Sheet = Model Space.</summary>
    public enum PvSpace { Block, Sheet }

    /// <summary>Chữ (Text hoặc MText). Justify: "BottomCenter" | "MiddleLeft" | "MiddleCenter" | "TopCenter" | "TopLeft" | "BaseLeft".</summary>
    public sealed class PvText
    {
        public PvSpace Space { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public string Text { get; set; }
        public string Layer { get; set; }
        public double PaperHeight { get; set; }
        public double Rotation { get; set; }
        public bool IsMText { get; set; }
        /// <summary>Bề rộng MText (mm giấy); 0 = không giới hạn.</summary>
        public double PaperWidth { get; set; }
        public string Justify { get; set; } = "BaseLeft";
        /// <summary>"Name" | "Symbol" | "Title" | "MemberList".</summary>
        public string Category { get; set; }
    }

    /// <summary>Kích thước. Kind "Rotated": (X1,Y1)-(X2,Y2) đo theo Rotation, đường kích thước qua (LineX,LineY).
    /// Kind "Diametric": (X1,Y1)-(X2,Y2) là 2 đầu đường kính.</summary>
    public sealed class PvDim
    {
        public PvSpace Space { get; set; }
        public string Kind { get; set; } = "Rotated";
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public double LineX { get; set; }
        public double LineY { get; set; }
        public double Rotation { get; set; }
        public string Layer { get; set; }
        public string DimStyle { get; set; } = "AM_ISO$0";
        /// <summary>null = giá trị đo ("&lt;&gt;").</summary>
        public string TextOverride { get; set; }
        /// <summary>Override DIMTMOVE = 1 (tờ tay: có ở chuỗi chép từ nháp, không có ở kích thước ghi "&lt;&gt;").</summary>
        public bool Tmove { get; set; } = true;
        /// <summary>Mũi tên xiên (DIMBLK1/2 = _Oblique, DIMSAH) — kích thước đầu mút trên mặt cắt.</summary>
        public bool Oblique { get; set; }
    }

    /// <summary>Chèn block. Scale = hệ số TRƯỚC khi nhân tỉ lệ tờ/50 (ký hiệu); ScaleIsAbsolute = dùng nguyên Scale.</summary>
    public sealed class PvInsert
    {
        public PvSpace Space { get; set; }
        public string Block { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Rotation { get; set; }
        public double Scale { get; set; } = 1;
        public bool ScaleIsAbsolute { get; set; }
        /// <summary>Lật theo X (scale X âm) — ký hiệu đặt về phía ngược lại.</summary>
        public bool MirrorX { get; set; }
        public string Layer { get; set; }
    }

    /// <summary>Đường thẳng (đường chéo đo kiểm trên mặt bằng lỗ lashing…).</summary>
    public sealed class PvLine
    {
        public PvSpace Space { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public string Layer { get; set; }
    }

    /// <summary>Polyline kín (khung AM_8 quanh member list).</summary>
    public sealed class PvPolyline
    {
        public PvSpace Space { get; set; }
        public List<(double X, double Y)> Points { get; } = new List<(double X, double Y)>();
        public string Layer { get; set; }
    }

    /// <summary>Leader (mũi tên tại điểm đầu) — layer posb, dim style AM_ISO.</summary>
    public sealed class PvLeader
    {
        public PvSpace Space { get; set; }
        public List<(double X, double Y)> Points { get; } = new List<(double X, double Y)>();
        public string Layer { get; set; } = "posb";
    }

    public sealed class PvRect
    {
        public double X0 { get; set; }
        public double Y0 { get; set; }
        public double X1 { get; set; }
        public double Y1 { get; set; }
    }

    /// <summary>Mặt cắt điển hình: hình trong block (toạ độ cục bộ, chèn tại X,Y xoay Rotation) + thk_sym/kích thước ở Model Space.</summary>
    public sealed class PvSection
    {
        public string BlockName { get; set; }
        public double X { get; set; }
        public double Y { get; set; }
        public double Rotation { get; set; }
        public List<PvRect> Plates { get; } = new List<PvRect>();
        /// <summary>Profile HP/BS — toạ độ CỤC BỘ của block mặt cắt.</summary>
        public List<PvInsert> Profiles { get; } = new List<PvInsert>();
        public List<PvInsert> ThkSymbols { get; } = new List<PvInsert>();
        public List<PvDim> Dims { get; } = new List<PvDim>();
    }

    /// <summary>Toàn bộ nội dung tờ Plan View tool sẽ vẽ.</summary>
    public sealed class PlanViewModel
    {
        private readonly List<string> _required = new List<string>();
        public List<PvText> Texts { get; } = new List<PvText>();
        public List<PvDim> Dims { get; } = new List<PvDim>();
        public List<PvInsert> Inserts { get; } = new List<PvInsert>();
        public List<PvSection> Sections { get; } = new List<PvSection>();
        public List<PvLine> Lines { get; } = new List<PvLine>();
        public List<PvPolyline> Polylines { get; } = new List<PvPolyline>();
        public List<PvLeader> Leaders { get; } = new List<PvLeader>();
        /// <summary>Block phải có sẵn trong bản vẽ 2D (không tạo mới) — thiếu ⇒ dừng, không vẽ gì.</summary>
        public IReadOnlyList<string> RequiredBlocks => _required;

        public void Require(string block)
        {
            if (!string.IsNullOrEmpty(block) && !_required.Contains(block)) _required.Add(block);
        }
    }
}
