using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.Services.Update;
using Xunit;

public class KeyRemapTests
{
    static readonly Dictionary<string, string> M = new Dictionary<string, string> { { "2B7B3", "A1" }, { "2B53D", "A2" }, { "1B", "C9" } };

    [Fact] public void Plain_Remapped() => Assert.Equal("A1#0", KeyRemap.Remap("2B7B3#0", M));
    [Fact] public void LowerCaseHandle_Remapped() => Assert.Equal("A1#2", KeyRemap.Remap("2b7b3#2", M));
    [Fact] public void Generated_AllTokensRemapped() => Assert.Equal("CollarPlate:A1#0~A2#1", KeyRemap.Remap("CollarPlate:2B7B3#0~2B53D#1", M));
    [Fact] public void OneTokenMissing_Null() => Assert.Null(KeyRemap.Remap("CollarPlate:2B7B3#0~FFFF#1", M));
    [Fact] public void HexTailOfWord_NotAToken() => Assert.Equal("C9#0/GirderFlange#0", KeyRemap.Remap("1B#0/GirderFlange#0", M));
    [Fact] public void NoToken_Unchanged() => Assert.Equal("HpEndBracketB:x", KeyRemap.Remap("HpEndBracketB:x", M));

    [Fact]
    public void Corrections_KeptAndDropped()
    {
        string json = CorrectionJson.Serialize(new Dictionary<string, PartCorrection>
        {
            { "2B7B3#0", new PartCorrection { Kind = "Web" } },
            { "FFFF#0", new PartCorrection { FlipDir = true } },
        }, new[] { "2B53D#1" });
        int kept = 0, dropped = 0;
        string r = KeyRemap.Corrections(json, M, ref kept, ref dropped);
        Assert.Equal(2, kept);
        Assert.Equal(1, dropped);
        CorrectionJson.Deserialize(r, out var parts, out var deleted);
        Assert.Equal("Web", parts["A1#0"].Kind);
        Assert.False(parts.ContainsKey("FFFF#0"));
        Assert.Contains("A2#1", deleted);
    }

    [Fact]
    public void Names_MergeGroupLosesMember()
    {
        string json = NamesJson.Serialize(new Dictionary<string, string> { { "2B7B3#0", "G1" } }, new[] { "FFFF#0" },
            new[] { new[] { "2B7B3#0", "2B53D#0", "EEEE#0" } }, new[] { "2B53D#1" });
        int kept = 0, dropped = 0;
        string r = KeyRemap.Names(json, M, ref kept, ref dropped);
        Assert.Equal(4, kept);      // tên + 2 thành viên merge + split
        Assert.Equal(2, dropped);   // deleted FFFF + thành viên merge EEEE
        var names = NamesJson.Deserialize(r, out var deleted, out var merges, out var splits);
        Assert.Equal("G1", names["A1#0"]);
        Assert.Empty(deleted);
        var g = Assert.Single(merges);
        Assert.True(g.SetEquals(new[] { "A1#0", "A2#0" }));
        Assert.Contains("A2#1", splits);
    }

    [Fact]
    public void EmptyJson_Unchanged()
    {
        int kept = 0, dropped = 0;
        Assert.Null(KeyRemap.Corrections(null, M, ref kept, ref dropped));
        Assert.Equal("", KeyRemap.Names("", M, ref kept, ref dropped));
        Assert.Equal(0, kept + dropped);
    }
}

public class RemapNoteTests
{
    [Fact] public void NothingStored_NoNote() => Assert.Null(KeyRemap.Note(0, 0, new List<(string, MCG_3DPanel.Services.Detail.P2)>()));

    [Fact]
    public void KeptDroppedAndShift()
    {
        var s = new List<(string, MCG_3DPanel.Services.Detail.P2)> { ("7D-16P_Assy", new MCG_3DPanel.Services.Detail.P2(100.25, -50)) };
        Assert.Equal("5 edits kept on unchanged parts; 2 dropped because their 2D part changed.\nThe base point of 7D-16P_Assy moved by (100.3, -50).",
            KeyRemap.Note(5, 2, s));
    }

    [Fact]
    public void ShiftOnly_NoEdits_OnlyShiftLine()
    {
        var s = new List<(string, MCG_3DPanel.Services.Detail.P2)> { ("B", new MCG_3DPanel.Services.Detail.P2(10, 0)) };
        Assert.Equal("The base point of B moved by (10, 0).", KeyRemap.Note(0, 0, s));
    }
}

public class RemapNoteCompactTests
{
    static MCG_3DPanel.Services.Detail.P2 P(double x, double y) => new MCG_3DPanel.Services.Detail.P2(x, y);

    [Fact]
    public void NegativeZero_PrintedAsZero() =>
        Assert.Equal("The base point of B moved by (0, 10).", KeyRemap.Note(0, 0, new List<(string, MCG_3DPanel.Services.Detail.P2)> { ("B", P(-0.04, 10)) }));

    [Fact]
    public void ManyShifts_OneLine()
    {
        var s = new List<(string, MCG_3DPanel.Services.Detail.P2)> { ("A", P(5, 0)), ("B", P(0, 120)), ("C", P(3, 4)) };
        Assert.Equal("The base point moved in 3 blocks (largest: B by (0, 120)).", KeyRemap.Note(0, 0, s));
    }

    [Fact]
    public void AllKept_NoneDropped_NoCountLine() =>
        Assert.Null(KeyRemap.Note(3, 0, new List<(string, MCG_3DPanel.Services.Detail.P2)>()));
}
