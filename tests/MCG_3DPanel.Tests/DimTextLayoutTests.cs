using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Chữ kích thước chật (tờ 7D-17C): giữa khoảng, nâng ra phía ngoài 4.5 mm, chữ chật liền nhau so le 2 tầng — không chồng.</summary>
    public class DimTextLayoutTests
    {
        private const int Sc = 75;
        private static PvDim H(double x1, double x2, double lineY = 1000) => new PvDim { X1 = x1, Y1 = 0, X2 = x2, Y2 = 0, LineX = x2, LineY = lineY, Rotation = 0 };

        [Fact] public void Fits_LeftToAutoCad()
        {
            var d = H(0, 1000);   // 13.3 mm giấy ⇒ "1000" (9.6 mm) lọt
            DimTextLayout.Arrange(new[] { d }, Sc);
            Assert.Null(d.TextX);
        }

        [Fact] public void Tight_CentredAndLiftedOutward()
        {
            var d = H(0, 350);    // 4.7 mm giấy < "350" 7.2 mm
            DimTextLayout.Arrange(new[] { d }, Sc);
            Assert.Equal(175, d.TextX.Value, 6);
            Assert.Equal(1000 + DimTextLayout.Lift * Sc, d.TextY.Value, 6);
            var below = H(0, 350, -1000);
            DimTextLayout.Arrange(new[] { below }, Sc);
            Assert.Equal(-1000 - DimTextLayout.Lift * Sc, below.TextY.Value, 6);   // đường kích thước phía dưới ⇒ nâng xuống
        }

        [Fact] public void TightNeighbours_Staggered_NoOverlap()
        {
            var ds = new[] { H(0, 395), H(395, 790), H(790, 1185), H(1185, 1580) };   // tờ 17C: 395, 395, 395… trên chuỗi lashing
            DimTextLayout.Arrange(ds, Sc);
            Assert.All(ds, d => Assert.NotNull(d.TextY));
            Assert.True(ds.Select(d => d.TextY.Value).Distinct().Count() == 2);
            for (int i = 0; i < ds.Length; i++)
                for (int j = i + 1; j < ds.Length; j++)
                {
                    if (Math.Abs(ds[i].TextY.Value - ds[j].TextY.Value) > 1e-6) continue;
                    double w = DimTextLayout.Width(ds[i]) * Sc;
                    Assert.True(Math.Abs(ds[i].TextX.Value - ds[j].TextX.Value) >= w, "2 chữ cùng tầng chồng nhau");
                }
        }

        [Fact] public void Vertical_RightChain_LiftsRight()
        {
            var d = new PvDim { X1 = 0, Y1 = 0, X2 = 0, Y2 = 300, LineX = 1400, LineY = 300, Rotation = Math.PI / 2 };
            DimTextLayout.Arrange(new[] { d }, Sc);
            Assert.Equal(1400 + DimTextLayout.Lift * Sc, d.TextX.Value, 6);
            Assert.Equal(150, d.TextY.Value, 6);
        }
    }

    public class SectionDraftFilterTests
    {
        /// <summary>Tờ 17C: nháp Structure 800 đo tới x 359.1 (không có thanh nào trên mặt cắt ở đó) không được chép vào X-X.</summary>
        [Fact] public void StructureDraft_EndNotOnMember_Skipped()
        {
            var i = new PlanViewInput { Panel = "P", TopPlate = (0, 0, 10000, 8000), PlateThickness = 6 };
            PvMember M(int l, string k, double x, string prof = null) => new PvMember { Line = l, Name = "n", Kind = k, Dir = "T", X0 = x, Y0 = 0, X1 = x, Y1 = 8000, Thickness = 6, Height = k == "Web" ? 374 : 80, ProfileBlock = prof };
            i.Members.Add(M(1, "Web", 1000)); i.Members.Add(M(2, "Web", 9000));
            i.Members.Add(M(3, "FB", 3000, "BS 80x6")); i.Members.Add(M(4, "FB", 5000, "BS 80x6"));
            i.Drafts.Add(new PvDraft { Source = "Structure", X1 = 3000, Y1 = 100, X2 = 5000, Y2 = 100, Rotation = 0 });   // FB → FB: giữ
            i.Drafts.Add(new PvDraft { Source = "Structure", X1 = 4200, Y1 = 100, X2 = 5000, Y2 = 100, Rotation = 0 });   // 800 tới chỗ trống: bỏ
            var s = SectionRules.Build(i, 'X', new SheetContext { Scale = 75, PlanMinX = 0, PlanMinY = 0, PlanMaxX = 10000, PlanMaxY = 8000, AssyTransform = new[] { 1.0, 0, 0, 1, 0, 0 } });
            Assert.DoesNotContain(s.Dims, d => Math.Abs(Math.Abs(d.X2 - d.X1) - 800) < 0.01);
            Assert.Contains(s.Dims, d => Math.Abs(Math.Abs(d.X2 - d.X1) - 2000) < 0.01 || Math.Abs(Math.Abs(d.X2 - d.X1) - 2003) < 4);
        }
    }
}
