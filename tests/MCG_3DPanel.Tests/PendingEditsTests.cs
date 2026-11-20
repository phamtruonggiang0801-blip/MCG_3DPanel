using System.Collections.Generic;
using System.Linq;
using MCG_3DPanel.Naming;
using MCG_3DPanel.Services.Corrections;
using MCG_3DPanel.Services.Update;
using Xunit;

public class PendingEditsTests
{
    static string Corr(Dictionary<string, PartCorrection> parts, params string[] deleted) =>
        CorrectionJson.Serialize(parts ?? new Dictionary<string, PartCorrection>(), deleted);

    static string Names(Dictionary<string, string> names, string[] deleted = null, string[][] merges = null, string[] splits = null) =>
        NamesJson.Serialize(names ?? new Dictionary<string, string>(), deleted, merges, splits);

    [Fact]
    public void Diff_Same_ReturnsNull()
    {
        string c = Corr(new Dictionary<string, PartCorrection> { { "A#0", new PartCorrection { FlipDir = true } } });
        string n = Names(new Dictionary<string, string> { { "A#0", "P1" } });
        Assert.Null(PendingEdits.Diff(c, c, n, n));
        Assert.Null(PendingEdits.Diff(null, Corr(null), null, Names(null)));
        Assert.Equal(0, PendingEdits.Count(null));
    }

    [Fact]
    public void KindChange_AppliedOverCarry()
    {
        string cur = Corr(new Dictionary<string, PartCorrection> { { "2B7B3#0", new PartCorrection { Kind = "Bracket" } } });
        string p = PendingEdits.Diff(Corr(null), cur, null, null);
        string carry = Corr(new Dictionary<string, PartCorrection>
        {
            { "2B7B3#0", new PartCorrection { Kind = "Web" } },
            { "AAA#0", new PartCorrection { FlipDir = true } },
        });
        PendingEdits.Apply(p, carry, null, out string co, out _);
        CorrectionJson.Deserialize(co, out var parts, out _);
        Assert.Equal("Bracket", parts["2B7B3#0"].Kind);
        Assert.True(parts["AAA#0"].FlipDir);
    }

    [Fact]
    public void Cleared_RemovesKeyFromCarry()
    {
        string b = Corr(new Dictionary<string, PartCorrection> { { "B#0", new PartCorrection { FlipDir = true } } });
        string p = PendingEdits.Diff(b, Corr(null), null, null);
        PendingEdits.Apply(p, b, null, out string co, out _);
        CorrectionJson.Deserialize(co, out var parts, out _);
        Assert.False(parts.ContainsKey("B#0"));
    }

    [Fact]
    public void DeleteAndRestore()
    {
        string b = Corr(null, "D#0");
        string cur = Corr(null, "C#0");
        string p = PendingEdits.Diff(b, cur, null, null);
        PendingEdits.Apply(p, Corr(null, "D#0"), null, out string co, out _);
        CorrectionJson.Deserialize(co, out _, out var deleted);
        Assert.Equal(new[] { "C#0" }, deleted.ToArray());
    }

    [Fact]
    public void Names_SetClearMerge()
    {
        string b = Names(new Dictionary<string, string> { { "F#0", "OLD" } }, merges: new[] { new[] { "M#0", "M#1" } });
        string cur = Names(new Dictionary<string, string> { { "E#0", "P1" } }, merges: new[] { new[] { "M#0", "M#2" } });
        string p = PendingEdits.Diff(null, null, b, cur);
        string carry = Names(new Dictionary<string, string> { { "F#0", "OLD" }, { "G#0", "KEEP" } }, merges: new[] { new[] { "X#0", "X#1" } });
        PendingEdits.Apply(p, null, carry, out _, out string no);
        var names = NamesJson.Deserialize(no, out _, out var merges);
        Assert.Equal("P1", names["E#0"]);
        Assert.False(names.ContainsKey("F#0"));
        Assert.Equal("KEEP", names["G#0"]);
        // Nhóm đổi trên bản 2D thêm vào; nhóm của file 3D (X) giữ — review 2026-11-20 (không thay nguyên khối)
        Assert.Equal(2, merges.Count);
        Assert.Contains(merges, g => g.SetEquals(new[] { "M#0", "M#2" }));
        Assert.Contains(merges, g => g.SetEquals(new[] { "X#0", "X#1" }));

        // merges không đổi ⇒ carry giữ merges của nó
        string p2 = PendingEdits.Diff(null, null, b, Names(new Dictionary<string, string> { { "E#0", "P1" } }, merges: new[] { new[] { "M#0", "M#1" } }));
        PendingEdits.Apply(p2, null, carry, out _, out string no2);
        NamesJson.Deserialize(no2, out _, out var merges2);
        Assert.True(merges2[0].SetEquals(new[] { "X#0", "X#1" }));
    }

    [Fact]
    public void Merge_On2D_KeepsGroupMadeIn3D()
    {
        string p = PendingEdits.Diff(null, null, Names(null), Names(null, merges: new[] { new[] { "C#0", "D#0" } }));
        string carry = Names(null, merges: new[] { new[] { "A#0", "B#0" } });
        PendingEdits.Apply(p, null, carry, out _, out string no);
        NamesJson.Deserialize(no, out _, out var merges);
        Assert.Equal(2, merges.Count);
        Assert.Contains(merges, g => g.SetEquals(new[] { "A#0", "B#0" }));
        Assert.Contains(merges, g => g.SetEquals(new[] { "C#0", "D#0" }));
    }

    [Fact]
    public void UnmergeOnly_CountedAndRemovesOnlyThatGroup()
    {
        string b = Names(null, merges: new[] { new[] { "C#0", "D#0" } });
        string p = PendingEdits.Diff(null, null, b, Names(null));
        Assert.NotNull(p);
        Assert.True(PendingEdits.Count(p) > 0);
        Assert.Contains("C#0", PendingEdits.Keys(p));
        string carry = Names(null, merges: new[] { new[] { "A#0", "B#0" }, new[] { "C#0", "D#0" } });
        PendingEdits.Apply(p, null, carry, out _, out string no);
        NamesJson.Deserialize(no, out _, out var merges);
        Assert.Single(merges);
        Assert.True(merges[0].SetEquals(new[] { "A#0", "B#0" }));
    }

    [Fact]
    public void Splits_AddAndRemove_KeepOthers()
    {
        string b = Names(null, splits: new[] { "S#0" });
        string p = PendingEdits.Diff(null, null, b, Names(null, splits: new[] { "T#0" }));
        Assert.Contains("S#0", PendingEdits.Keys(p));
        Assert.Contains("T#0", PendingEdits.Keys(p));
        PendingEdits.Apply(p, null, Names(null, splits: new[] { "S#0", "U#0" }), out _, out string no);
        NamesJson.Deserialize(no, out _, out _, out var splits);
        Assert.True(splits.SetEquals(new[] { "T#0", "U#0" }));
    }

    [Fact]
    public void Keys_ListsEveryTouchedKey()
    {
        string bc = Corr(new Dictionary<string, PartCorrection> { { "B#0", new PartCorrection { FlipDir = true } } }, "D#0");
        string cc = Corr(new Dictionary<string, PartCorrection> { { "A#0", new PartCorrection { Kind = "Web" } } }, "C#0");
        string bn = Names(new Dictionary<string, string> { { "F#0", "X" } }, new[] { "H#0" });
        string cn = Names(new Dictionary<string, string> { { "E#0", "Y" } }, new[] { "I#0" });
        string p = PendingEdits.Diff(bc, cc, bn, cn);
        var keys = PendingEdits.Keys(p);
        foreach (var k in new[] { "A#0", "B#0", "C#0", "D#0", "E#0", "F#0", "H#0", "I#0" }) Assert.Contains(k, keys);
        Assert.Contains("a#0", keys);   // không phân biệt hoa thường
        Assert.Equal(8, PendingEdits.Count(p));
    }

    [Fact]
    public void ApplyThenRemap_MovesPendingToNewHandles()
    {
        string cur = Corr(new Dictionary<string, PartCorrection> { { "2B7B3#0", new PartCorrection { Kind = "Bracket" } } });
        string p = PendingEdits.Diff(null, cur, null, null);
        PendingEdits.Apply(p, Corr(null), null, out string co, out _);
        int kept = 0, dropped = 0;
        string r = KeyRemap.Corrections(co, new Dictionary<string, string> { { "2B7B3", "9F001" } }, ref kept, ref dropped);
        CorrectionJson.Deserialize(r, out var parts, out _);
        Assert.Equal("Bracket", parts["9F001#0"].Kind);
    }

    [Fact]
    public void KeyRemap_Handles()
    {
        Assert.Equal(new[] { "2B7B3", "2B53D" }, KeyRemap.Handles("CollarPlate:2B7B3#0~2B53D#1").ToArray());
        Assert.Equal(new[] { "1B" }, KeyRemap.Handles("1B#0/GirderFlange#0").ToArray());
        Assert.Empty(KeyRemap.Handles(null));
    }
}

public class PendingCountsTests
{
    [Fact]
    public void Counts_SplitsNamesFromGeometry()
    {
        string bc = CorrectionJson.Serialize(new Dictionary<string, PartCorrection>(), new string[0]);
        string cc = CorrectionJson.Serialize(new Dictionary<string, PartCorrection> { { "A#0", new PartCorrection { FlipDir = true } } }, new[] { "B#0" });
        string bn = NamesJson.Serialize(new Dictionary<string, string>());
        string cn = NamesJson.Serialize(new Dictionary<string, string> { { "C#0", "T1" } }, null, new[] { new[] { "D#0", "E#0" } });
        var (names, geometry) = PendingEdits.Counts(PendingEdits.Diff(bc, cc, bn, cn));
        Assert.Equal(3, names);      // C#0, D#0, E#0
        Assert.Equal(2, geometry);   // A#0, B#0
        Assert.Equal((0, 0), PendingEdits.Counts(null));
    }
}
