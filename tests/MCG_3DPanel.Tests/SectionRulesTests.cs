using System;
using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Services.PlanView;
using Xunit;

namespace MCG_3DPanel.Tests
{
    /// <summary>Mặt cắt theo tờ mẫu 7D-17P (CAS-0062242): đường cắt giữa panel, gốc = đầu tấm trên đường cắt, profile tại mặt lưng và lật
    /// theo hướng bầu, thk_sym mỗi thanh, kích thước 3 mức. Tọa độ tham chiếu lấy từ tờ tay.</summary>
    public class SectionRulesTests
    {
        private const int Scale = 50;
        private static int _line;

        private static PvMember M(string kind, double x0, double y0, double x1, double y1, double t, double h, string profile = null,
            int? line = null, bool deleted = false, double bellyX = 0, double bellyY = 0)
            => new PvMember { Line = line ?? ++_line, Name = "n", Kind = kind, Dir = Math.Abs(x1 - x0) >= Math.Abs(y1 - y0) ? "L" : "T",
                              X0 = x0, Y0 = y0, X1 = x1, Y1 = y1, Thickness = t, Height = h, ProfileBlock = profile, Deleted = deleted,
                              BellyX = bellyX, BellyY = bellyY };

        /// <summary>7D-17P thu gọn, hệ block (gốc = điểm chèn _Assy 5019.76, 22476.66).</summary>
        private static PlanViewInput Panel7D17P()
        {
            var i = new PlanViewInput { Panel = "7D-17P", TopPlate = (-359.06, -12891.26, 15939.84, 500.84), PlateThickness = 6 };
            i.PlateOutline.AddRange(new List<(double, double)>
            {
                (-359.06, -12891.26), (15939.84, -12891.26), (15939.84, 61), (15939.84, 500.84), (14000, 500.84), (14000, 61),
                (264.8, 61), (264.8, 464.9), (-359.06, 464.9), (-359.06, 61), (0, 61), (0, -12500), (-359.06, -12500),
            });
            i.Members.Add(M("Web", 287, -12891, 287, 61, 6, 374, line: 1)); i.Members.Add(M("Flange", 287, -12891, 287, 61, 380, 20, line: 1));
            i.Members.Add(M("Web", 15403.4, -12891, 15403.4, 61, 6, 374, line: 2)); i.Members.Add(M("Flange", 15403.4, -12891, 15403.4, 61, 380, 20, line: 2));
            i.Members.Add(M("HP", 57, -12800, 57, 0, 6, 120, "HP120x6", bellyX: -1));                 // HT đầu: bầu về −X ⇒ lật
            for (int k = 1; k < 21; k++) { double x = 793 + 775 * (k - 1); i.Members.Add(M("HP", x, -12800, x, 0, 6, 120, "HP120x6", bellyX: 1)); }
            double[] ys = { -12482, -10437, -8392, -6347, -4302, -2257, -202 };
            double[] fw = { 250, 170, 170, 150, 170, 170, 250 };
            for (int k = 0; k < 7; k++)
            {
                i.Members.Add(M("Web", 0, ys[k], 15939.84, ys[k], 6, 374, line: 100 + k));
                i.Members.Add(M("Flange", 0, ys[k], 15939.84, ys[k], fw[k], 20, line: 100 + k));
            }
            // Nháp Structure: 775 giữa 2 HT (mặt lưng 790 → 1565, hệ block)
            i.Drafts.Add(new PvDraft { Source = "Structure", X1 = 790, Y1 = -100, X2 = 1565, Y2 = -100, Rotation = 0, Tmove = true });
            // 2045 giữa L1 và L2 (mặt web)
            i.Drafts.Add(new PvDraft { Source = "Structure", X1 = 5000, Y1 = -12482, X2 = 5000, Y2 = -10437, Rotation = Math.PI / 2, Tmove = true });
            return i;
        }

        private static SheetContext Sheet() => new SheetContext
        {
            Scale = Scale, FrameX = 0, FrameY = 0, PlanMinX = 4660.7, PlanMinY = 9585.4, PlanMaxX = 20959.6, PlanMaxY = 22977.5,
            AssyTransform = new[] { 1.0, 0, 0, 1, 5019.76, 22476.66 },
        };

        private static PvSection X() => SectionRules.Build(Panel7D17P(), 'X', Sheet());
        private static PvSection Y() => SectionRules.Build(Panel7D17P(), 'Y', Sheet());

        [Fact] public void X_Origin_AtAssy_CutPlateLength_7D17P()
        {
            var s = X();
            Assert.Equal("7D-17P_X-X_MCG", s.BlockName);
            Assert.Equal(5019.76, s.X, 2);                                      // tờ tay: 5019.8
            Assert.Equal(22977.5 + PlanViewLayout.SectionXAbove * Scale, s.Y, 2); // tờ tay: 24789.8
            var plate = s.Plates[0];
            Assert.Equal(15939.84, plate.X1 - plate.X0, 2);                     // tờ tay: 15939.8
            Assert.Equal(0, plate.Y0, 3); Assert.Equal(6, plate.Y1, 3);          // X-X: tấm 0…t
        }

        [Fact] public void X_Profiles_AtBackFace_MirroredByBelly()
        {
            var p = X().Profiles.OrderBy(q => q.X).ToList();
            Assert.Equal(21, p.Count);
            Assert.True(p[0].MirrorX); Assert.Equal(60, p[0].X, 3);             // tờ tay: HP120x6@60.0 lật
            Assert.False(p[1].MirrorX); Assert.Equal(790, p[1].X, 3);           // tờ tay: HP120x6@790.0
            Assert.All(p, q => Assert.Equal(0, q.Y, 3));
        }

        [Fact] public void X_Webs_HangBelowZero()
        {
            var web = X().Plates.Single(r => Math.Abs(r.X1 - r.X0 - 6) < 1e-6 && r.X0 < 1000);
            Assert.Equal(284, web.X0, 3); Assert.Equal(-374, web.Y0, 3); Assert.Equal(0, web.Y1, 3);   // tờ tay [284.0,-374..290.0,0]
        }

        [Fact] public void Y_Rotated_LeftOfPlan_FromPlateStart()
        {
            var s = Y();
            Assert.Equal("7D-17P_Y-Y_MCG", s.BlockName);
            Assert.Equal(Math.PI / 2, s.Rotation, 6);
            Assert.Equal(4660.7 - PlanViewLayout.SectionYLeft * Scale, s.X, 2);  // tờ tay: 2392.0
            Assert.Equal(22476.66 - 12891.26, s.Y, 2);                            // tờ tay: 9585.3
            Assert.Equal(12952.26, s.Plates[0].X1 - s.Plates[0].X0, 2);          // tờ tay: 12952.3
            Assert.Equal(-6, s.Plates[0].Y0, 3); Assert.Equal(0, s.Plates[0].Y1, 3);
            var widths = s.Plates.Where(p => Math.Abs(p.Y1 - p.Y0 - 20) < 1e-6).OrderBy(p => p.X0).Select(p => Math.Round(p.X1 - p.X0)).ToArray();
            Assert.Equal(new double[] { 250, 170, 170, 150, 170, 170, 250 }, widths);
        }

        [Fact] public void ThkSym_OnePerMember_OnPlateLine()
        {
            var s = X();
            Assert.Equal(21 + 2, s.ThkSymbols.Count);
            Assert.All(s.ThkSymbols, t => { Assert.Equal("thk_sym", t.Block); Assert.Equal(PvSpace.Sheet, t.Space); Assert.Equal(s.Y, t.Y, 3); });
            Assert.True(s.ThkSymbols.OrderBy(t => t.X).First().MirrorX);         // tờ tay: (5079.8, sx −1)
        }

        [Fact] public void Dims_PitchDraftAtLevel_EndsOblique_GirderChainGenerated()
        {
            var s = X();
            var pitch = s.Dims.Single(d => Math.Abs(Math.Abs(d.X2 - d.X1) - 775) < 0.01 && Math.Abs(d.Rotation) < 1e-9);
            Assert.Equal(s.Y + PlanViewLayout.SecXPitch * Scale, pitch.LineY, 2);  // tờ tay: 25579.3
            Assert.Equal(5809.76, pitch.X1, 2);                                     // tờ tay: 5809.7
            var ends = s.Dims.Where(d => d.Oblique).ToList();
            Assert.Equal(2, ends.Count);
            Assert.Contains(ends, d => Math.Abs(Math.Abs(d.X2 - d.X1) - 60) < 0.01);  // tờ tay: 60.0
            var girder = s.Dims.Where(d => Math.Abs(d.LineY - (s.Y + PlanViewLayout.SecXGirder * Scale)) < 0.01).Select(d => Math.Round(Math.Abs(d.X2 - d.X1), 1)).ToList();
            Assert.Equal(new[] { 290.0, 15110.4 }, girder);                         // tờ tay: 290.0, 15110.5
            var h = s.Dims.Single(d => Math.Abs(d.Rotation - Math.PI / 2) < 1e-9);
            Assert.Equal(400, Math.Abs(h.Y2 - h.Y1), 3);                            // tờ tay: 400.0
        }

        [Fact] public void Y_GirderDraft_GoesToGirderLevel()
        {
            var s = Y();
            var g = s.Dims.Single(d => Math.Abs(Math.Abs(d.Y2 - d.Y1) - 2045) < 0.01);
            Assert.Equal(s.X - PlanViewLayout.SecYGirder * Scale, g.LineX, 2);      // tờ tay: 1298.0
        }

        [Fact] public void DeletedMember_NotInSection()
        {
            var i = Panel7D17P();
            i.Members.First(m => m.Kind == "HP").Deleted = true;
            Assert.Equal(20, SectionRules.Build(i, 'X', Sheet()).Profiles.Count);
        }

        [Fact] public void MergedMembers_AppearOnce()
        {
            var i = Panel7D17P();
            i.Members.Add(M("HP", 9000, -12000, 9000, -9000, 6, 120, "HP120x6", line: 555));
            i.Members.Add(M("HP", 9000, -8900, 9000, -100, 6, 120, "HP120x6", line: 555));
            Assert.Equal(22, SectionRules.Build(i, 'X', Sheet()).Profiles.Count);
        }

        [Fact] public void UnknownProfile_ListedInRequiredBlocks()
        {
            var i = Panel7D17P();
            i.Members.Add(M("HP", 9100, -12000, 9100, -100, 7, 140, "HP140x7"));
            var m = new PlanViewModel();
            SectionRules.Require(SectionRules.Build(i, 'X', Sheet()), m);
            Assert.Contains("HP140x7", m.RequiredBlocks);
            Assert.Contains("thk_sym", m.RequiredBlocks);
        }
    }
}
