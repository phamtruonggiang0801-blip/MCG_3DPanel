using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.Detail;
using Xunit;

namespace MCG_3DPanel.Tests
{
    public class DetailUpdateRulesTests
    {
        static DetailRegion Rect(double x0, double y0, double x1, double y1)
        {
            var v = new List<(double, double, double)> { (x0, y0, 0), (x1, y0, 0), (x1, y1, 0), (x0, y1, 0) };
            return DetailRegion.FromPolyline(v, true, out _);
        }

        static DetailRecord Rec(DetailKind k, string panel, int n, string letter = "") =>
            new DetailRecord { Kind = k, Panel = panel, Number = n, Letter = letter };

        [Fact]
        public void UpdateCommand_AllowedFromPalette()
        {
            Assert.True(DetailCommandRules.IsCommand("MCG_DetailUpdate"));
            Assert.False(DetailCommandRules.IsCommand("MCG_DetailUpdate ERASE"));
        }

        // ─── Phát hiện thay đổi ────────────────────────────────────────────────

        [Fact]
        public void Signature_SameEntitiesAnyOrderAndSubMillimetreNoise_Equal()
        {
            var a = DetailUpdateRules.Signature(new[]
            {
                ("LINE", "Mechanical-AM_0", new[] { 0.0, 0, 100, 0 }),
                ("LWPOLYLINE", "Mechanical-AM_4", new[] { 10.0, 10, 20, 30 }),
            });
            var b = DetailUpdateRules.Signature(new[]
            {
                ("LWPOLYLINE", "Mechanical-AM_4", new[] { 10.0001, 9.9999, 20.2, 29.8 }),
                ("LINE", "Mechanical-AM_0", new[] { 0.0, 0, 100.3, 0 }),
            });
            Assert.False(DetailUpdateRules.Changed(a, b));
        }

        [Fact]
        public void Signature_DifferentCountExtentsOrLayer_Changed()
        {
            var one = ("LINE", "Mechanical-AM_0", new[] { 0.0, 0, 100, 0 });
            var baseSig = DetailUpdateRules.Signature(new[] { one });
            Assert.True(DetailUpdateRules.Changed(baseSig, DetailUpdateRules.Signature(new[] { one, one })));
            Assert.True(DetailUpdateRules.Changed(baseSig,
                DetailUpdateRules.Signature(new[] { ("LINE", "Mechanical-AM_0", new[] { 0.0, 0, 102, 0 }) })));
            Assert.True(DetailUpdateRules.Changed(baseSig,
                DetailUpdateRules.Signature(new[] { ("LINE", "Mechanical-AM_4", new[] { 0.0, 0, 100, 0 }) })));
            Assert.True(DetailUpdateRules.Changed(baseSig, DetailUpdateRules.Signature(new (string, string, double[])[0])));
        }

        [Fact]
        public void Signature_EntityWithoutExtents_CountsByKindAndLayer()
        {
            var a = DetailUpdateRules.Signature(new[] { ("MTEXT", "Mechanical-AM_10", (double[])null) });
            var b = DetailUpdateRules.Signature(new[] { ("MTEXT", "Mechanical-AM_10", (double[])null) });
            Assert.False(DetailUpdateRules.Changed(a, b));
            Assert.True(DetailUpdateRules.Changed(a, DetailUpdateRules.Signature(new (string, string, double[])[0])));
        }

        // ─── Nhãn / tiêu đề ────────────────────────────────────────────────────

        [Fact]
        public void IsLabelOf_SamePanelAndNumber()
        {
            var det = Rec(DetailKind.Plan, "7D-16P", 1);
            Assert.True(DetailUpdateRules.IsLabelOf(Rec(DetailKind.Plan, "7D-16P", 1), det));
            Assert.True(DetailUpdateRules.IsLabelOf(Rec(DetailKind.View, "7D-16P", 1), Rec(DetailKind.View, "7D-16P", 1)));
            Assert.False(DetailUpdateRules.IsLabelOf(Rec(DetailKind.Plan, "7D-16P", 2), det));
            Assert.False(DetailUpdateRules.IsLabelOf(Rec(DetailKind.Plan, "7D-16C", 1), det));
            Assert.False(DetailUpdateRules.IsLabelOf(Rec(DetailKind.Section, "7D-16P", 1, "A"), det));
            Assert.False(DetailUpdateRules.IsLabelOf(null, det));
            Assert.False(DetailUpdateRules.IsLabelOf(Rec(DetailKind.Plan, "7D-16P", 1), Rec(DetailKind.Section, "7D-16P", 1, "A")));
        }

        [Fact]
        public void SameDetail_KindPanelNumberLetter()
        {
            Assert.True(DetailUpdateRules.SameDetail(Rec(DetailKind.Section, "P", 1, "A"), Rec(DetailKind.Section, "P", 1, "a")));
            Assert.False(DetailUpdateRules.SameDetail(Rec(DetailKind.Section, "P", 1, "A"), Rec(DetailKind.Section, "P", 1, "B")));
            Assert.False(DetailUpdateRules.SameDetail(Rec(DetailKind.Plan, "P", 1), Rec(DetailKind.Section, "P", 1, "A")));
            Assert.False(DetailUpdateRules.SameDetail(Rec(DetailKind.Plan, "P", 1), Rec(DetailKind.Plan, "Q", 1)));
            Assert.False(DetailUpdateRules.SameDetail(null, Rec(DetailKind.Plan, "Q", 1)));
        }

        [Fact]
        public void SectionsOf_OnlySectionsOfThatPanelAndNumber_ByLetter()
        {
            var recs = new[]
            {
                Rec(DetailKind.Section, "P", 1, "B"), Rec(DetailKind.Section, "P", 2, "A"), Rec(DetailKind.Plan, "P", 1),
                Rec(DetailKind.Section, "Q", 1, "A"), Rec(DetailKind.Section, "P", 1, "A"), Rec(DetailKind.Section, "P", 1, "A"),
            };
            var got = DetailUpdateRules.SectionsOf(recs, Rec(DetailKind.Plan, "P", 1)).Select(r => r.Letter).ToList();
            Assert.Equal(new[] { "A", "B" }, got);
        }

        // ─── Dời nhãn / giữ gốc section ────────────────────────────────────────

        [Fact]
        public void LabelShift_FollowsTopRightCorner()
        {
            var s = DetailUpdateRules.LabelShift(Rect(0, 0, 100, 50), Rect(0, 0, 160, 80), 50);
            Assert.Equal(60, s.X, 6); Assert.Equal(30, s.Y, 6);
            var none = DetailUpdateRules.LabelShift(Rect(0, 0, 100, 50), Rect(0, 0, 100, 50), 50);
            Assert.Equal(0, none.Length, 9);
        }

        [Fact]
        public void SectionBase_KeepsPlanToBlockMappingWhenStartMoves()
        {
            // Đường cắt ngang y=20, nhìn +Y ⇒ Right = (1,0) … start dời từ x=0 sang x=-40 (vùng rộng ra bên trái)
            var oldFrame = SectionFrame.From(new P2(-1000, 20), new P2(1000, 20), new P2(0, 100), Rect(0, 0, 100, 50));
            var newFrame = SectionFrame.From(new P2(-1000, 20), new P2(1000, 20), new P2(0, 100), Rect(-40, 0, 100, 50));
            var oldBase = new P2(50, 300);
            var b = DetailUpdateRules.SectionBase(oldBase, oldFrame, newFrame).Value;
            // điểm plan x=70: cũ (70-0)-50 = 20; mới (70-(-40)) - b.X phải = 20
            double xOld = newFrame.ToSection(new P2(70, 20), 0).X;
            Assert.Equal(20, xOld - b.X, 6);
            Assert.Equal(300, b.Y, 6);
        }

        [Fact]
        public void SectionBase_NoOldFrame_Null()
        {
            var f = SectionFrame.From(new P2(-1000, 20), new P2(1000, 20), new P2(0, 100), Rect(0, 0, 100, 50));
            Assert.Null(DetailUpdateRules.SectionBase(new P2(1, 2), null, f));
            Assert.Null(DetailUpdateRules.SectionBase(new P2(1, 2), f, null));
        }

        // ─── Tên / thông báo ───────────────────────────────────────────────────

        [Fact]
        public void DisplayName_DetailOrSection()
        {
            Assert.Equal("DETAIL 3", DetailUpdateRules.DisplayName(Rec(DetailKind.Plan, "P", 3)));
            Assert.Equal("DETAIL 4", DetailUpdateRules.DisplayName(Rec(DetailKind.View, "P", 4)));
            Assert.Equal("1A-1A", DetailUpdateRules.DisplayName(Rec(DetailKind.Section, "P", 1, "A")));
        }

        [Fact]
        public void Messages_Verbatim()
        {
            Assert.Equal("Update 3 details? Changes you made inside these blocks will be replaced; everything outside them is kept.",
                DetailUpdateRules.ConfirmQuestion(3));
            Assert.Equal("boundary of DETAIL 2 was erased — used the stored region", DetailUpdateRules.ErasedNote(2));
            Assert.Equal("1A-1A: the cut line no longer crosses structure",
                DetailUpdateRules.EmptySectionNote(Rec(DetailKind.Section, "P", 1, "A")));
        }

        [Fact]
        public void Status_ListsChangedAndNotes()
        {
            Assert.Equal("Updated 3 details (changed: DETAIL 1, 1A-1A). Check dimensions on changed details.",
                DetailUpdateRules.Status(3, new[] { "DETAIL 1", "1A-1A" }, null));
            Assert.Equal("Updated 2 details (no changes).", DetailUpdateRules.Status(2, new string[0], null));
            Assert.Equal("Updated 1 detail (no changes). Notes: boundary of DETAIL 1 was erased — used the stored region.",
                DetailUpdateRules.Status(1, null, new[] { DetailUpdateRules.ErasedNote(1) }));
        }

        [Fact]
        public void Order_PlansBeforeSections_ByPanelNumberLetter()
        {
            var recs = new[]
            {
                Rec(DetailKind.Section, "P", 1, "B"), Rec(DetailKind.View, "P", 2), Rec(DetailKind.Section, "P", 1, "A"),
                Rec(DetailKind.Plan, "P", 1), Rec(DetailKind.Plan, "A", 5),
            };
            var got = DetailUpdateRules.Order(recs, r => r).Select(DetailUpdateRules.DisplayName).ToList();
            Assert.Equal(new[] { "DETAIL 5", "DETAIL 1", "DETAIL 2", "1A-1A", "1B-1B" }, got);
        }
    }
}
