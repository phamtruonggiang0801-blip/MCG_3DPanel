using System;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Mặt cắt điển hình (kỹ sư 2026-11-10): gom mọi thanh 1 phương; xoá trong 3D ⇒ bỏ; gộp ⇒ 1 lần.</summary>
    public class SectionRulesTests
    {
        private const int Scale = 50;
        private static int _line;

        private static PvMember M(string kind, double x0, double y0, double x1, double y1, double t, double h, string profile = null, int? line = null, bool deleted = false)
            => new PvMember { Line = line ?? ++_line, Name = "n", Kind = kind, Dir = Math.Abs(x1 - x0) >= Math.Abs(y1 - y0) ? "L" : "T",
                              X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Thickness = t, Height = h, ProfileBlock = profile, Deleted = deleted };

        /// <summary>7D-17P thu gọn: T1, T2 (web 374×6, flange 380×20), 22 HT HP120x6 bước 775, 7 web L với flange 250/170/170/150/170/170/250.</summary>
        private static PlanViewInput Panel7D17P()
        {
            var i = new PlanViewInput { Panel = "7D-17P", TopPlate = (0, 0, 15940, 12952), PlateThickness = 6 };
            i.Members.Add(M("Web", 287, 0, 287, 12952, 6, 374, line: 1)); i.Members.Add(M("Flange", 287, 0, 287, 12952, 380, 20, line: 1));
            i.Members.Add(M("Web", 15403, 0, 15403, 12952, 6, 374, line: 2)); i.Members.Add(M("Flange", 15403, 0, 15403, 12952, 380, 20, line: 2));
            for (int k = 0; k < 22; k++) { double x = 60 + 775 * k; i.Members.Add(M("HP", x, 400, x, 12550, 6, 120, "HP120x6")); }
            double[] ys = { 407, 2452, 4497, 6542, 8587, 10632, 12688 };
            double[] fw = { 250, 170, 170, 150, 170, 170, 250 };
            for (int k = 0; k < 7; k++)
            {
                i.Members.Add(M("Web", 0, ys[k], 15940, ys[k], 6, 374, line: 100 + k));
                i.Members.Add(M("Flange", 0, ys[k], 15940, ys[k], fw[k], 20, line: 100 + k));
            }
            return i;
        }

        private static PvSection X() => SectionRules.Build(Panel7D17P(), 'X', 1000, 2000, 1000 + 15940, 2000 + 12952, Scale, 0);
        private static PvSection Y() => SectionRules.Build(Panel7D17P(), 'Y', 1000, 2000, 1000 + 15940, 2000 + 12952, Scale, 0);

        [Fact] public void X_7D17P_TwoGirdersAnd22HP()
        {
            var s = X();
            Assert.Equal("7D-17P_X-X_MCG", s.BlockName);
            Assert.Equal(1 + 2 + 2, s.Plates.Count);                      // tấm + 2 web + 2 flange
            Assert.Equal(22, s.Profiles.Count(p => p.Block == "HP120x6"));
            var plate = s.Plates[0];
            Assert.Equal(15940, plate.X1 - plate.X0, 3);
            Assert.Equal(-6, plate.Y0, 3); Assert.Equal(0, plate.Y1, 3);
            var web = s.Plates.Single(p => Math.Abs(p.X1 - p.X0 - 6) < 1e-6 && p.X0 < 1000);
            Assert.Equal(284, web.X0, 3); Assert.Equal(-6 - 374, web.Y0, 3); Assert.Equal(-6, web.Y1, 3);
            var flange = s.Plates.Single(p => Math.Abs(p.X1 - p.X0 - 380) < 1e-6 && p.X0 < 1000);
            Assert.Equal(-6 - 374 - 20, flange.Y0, 3);
            Assert.All(s.Profiles, p => Assert.Equal(-6, p.Y, 3));
        }

        [Fact] public void X_Insertion_AbovePlan()
        {
            var s = X();
            Assert.Equal(1000, s.X, 3);
            Assert.Equal(2000 + 12952 + PlanViewLayout.SectionGapY * Scale, s.Y, 3);
            Assert.Equal(0, s.Rotation, 6);
        }

        [Fact] public void X_Insertion_ClearsLabelRows()
        {
            var s = SectionRules.Build(Panel7D17P(), 'X', 1000, 2000, 16940, 14952, Scale, 66);
            Assert.Equal(14952 + (66 + PlanViewLayout.SectionBeyondLabels) * Scale, s.Y, 3);
        }

        [Fact] public void Y_7D17P_FlangeWidths_Rotated_LeftOfPlan()
        {
            var s = Y();
            Assert.Equal("7D-17P_Y-Y_MCG", s.BlockName);
            var widths = s.Plates.Where(p => Math.Abs(p.Y1 - p.Y0 - 20) < 1e-6).OrderBy(p => p.X0).Select(p => Math.Round(p.X1 - p.X0)).ToArray();
            Assert.Equal(new double[] { 250, 170, 170, 150, 170, 170, 250 }, widths);
            Assert.Equal(Math.PI / 2, s.Rotation, 6);
            Assert.Equal(1000 - PlanViewLayout.SectionGapX * Scale, s.X, 3);
            Assert.Equal(2000, s.Y, 3);
        }

        [Fact] public void DeletedMember_NotInSection()
        {
            var i = Panel7D17P();
            i.Members.First(m => m.Kind == "HP").Deleted = true;
            Assert.Equal(21, SectionRules.Build(i, 'X', 0, 0, 15940, 12952, Scale, 0).Profiles.Count);
        }

        [Fact] public void MergedMembers_AppearOnce()
        {
            var i = Panel7D17P();
            i.Members.Add(M("HP", 9000, 0, 9000, 300, 6, 120, "HP120x6", line: 555));
            i.Members.Add(M("HP", 9000, 400, 9000, 12000, 6, 120, "HP120x6", line: 555));
            Assert.Equal(23, SectionRules.Build(i, 'X', 0, 0, 15940, 12952, Scale, 0).Profiles.Count);
        }

        [Fact] public void ThkSymbols_OnePerPlate_ScaledToSheet()
        {
            var s = X();
            Assert.Equal(s.Plates.Count, s.ThkSymbols.Count);
            Assert.All(s.ThkSymbols, t => { Assert.Equal("thk_sym", t.Block); Assert.Equal(PvSpace.Sheet, t.Space); Assert.Equal(1, t.Scale); });
        }

        [Fact] public void SpacingDims_BetweenProfiles_AM5()
        {
            var s = X();
            Assert.Equal(21, s.Dims.Count);
            Assert.All(s.Dims, d => { Assert.Equal("Mechanical-AM_5", d.Layer); Assert.Equal(PvSpace.Sheet, d.Space); });
            Assert.All(s.Dims, d => Assert.Equal(775, Math.Abs(d.X2 - d.X1), 3));
        }

        [Fact] public void UnknownProfile_ListedInRequiredBlocks()
        {
            var i = Panel7D17P();
            i.Members.Add(M("HP", 9100, 0, 9100, 12000, 7, 140, "HP140x7"));
            var m = new PlanViewModel();
            SectionRules.Require(SectionRules.Build(i, 'X', 0, 0, 15940, 12952, Scale, 0), m);
            Assert.Contains("HP140x7", m.RequiredBlocks);
            Assert.Contains("thk_sym", m.RequiredBlocks);
        }
    }
}
