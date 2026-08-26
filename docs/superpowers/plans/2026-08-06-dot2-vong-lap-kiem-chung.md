# Đợt 2 — Đóng vòng lặp kiểm chứng (Regression Harness) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Biến mỗi panel mà kỹ sư đã kiểm bằng mắt một lần thành một regression test chạy được bằng một lệnh, không cần mở AutoCAD.

**Architecture:** Tách phần phân loại thuần (không đụng database) khỏi phần đọc DWG. Thêm lệnh AutoCAD dump danh sách `PanelPart` thô ra JSON. Một console runner headless nạp lại JSON đó, chạy đúng logic phân loại của plugin, rồi so kết quả với file CSV golden mà kỹ sư đã sửa tay trong tab Result. Runner tham chiếu thẳng `MCG_3DPanel.dll` nên **không có logic nhân bản** — nó chạy chính code sản phẩm.

**Tech Stack:** C# 8, .NET Framework 4.8, x64. AutoCAD 2023 .NET API (AcDbMgd) — chỉ dùng kiểu hình học, không cần tiến trình AutoCAD. `System.Web.Extensions` (BCL) để đọc JSON. Không NuGet.

## Global Constraints

- Target framework `net48`, `LangVersion 8.0`, `PlatformTarget x64` — giống plugin.
- **KHÔNG thêm package NuGet nào** vào `MCG_3DPanel.csproj`. Plugin nạp vào tiến trình AutoCAD; xung đột assembly là rủi ro thật.
- Assembly AutoCAD (`AcDbMgd` v.v.) luôn `<Private>false</Private>` trong `MCG_3DPanel.csproj`. Chỉ project runner mới được copy `AcDbMgd.dll` về output.
- Mọi thay đổi trong `Services/` và `Commands/` phải giữ nguyên hành vi phân loại hiện tại. Task 7 chứng minh điều này bằng golden test.
- Đơn vị mm. Panel luôn phẳng trên mặt phẳng XY → mọi toạ độ Z trong dump ghi là `0`.
- Comment/tài liệu viết tiếng Việt, khớp phong cách hiện có của codebase.
- Sau mỗi task: `dotnet build -c Release` phải 0 error, 0 warning.

## Đã kiểm chứng trước khi viết plan

Ba giả định then chốt đã được chạy thử thật (không phải suy đoán):

1. `Point3d`, `Vector3d`, `Matrix3d`, `Extents3d`, `ObjectId.Null` hoạt động **ngoài** tiến trình AutoCAD — với điều kiện `AcDbMgd.dll` nằm cạnh exe **và** thư mục `C:\Program Files\Autodesk\AutoCAD 2023` có trên PATH khi chạy.
2. `PanelPart` và `ClassificationResult` (từ `MCG_3DPanel.dll`) khởi tạo và chạy được headless.
3. `new JavaScriptSerializer().Deserialize<T>(json)` (`System.Web.Extensions`, có sẵn trong net48) đọc được JSON → **không cần viết parser tay**.

Task 1 kiểm lại điều 1+2 trong chính repo trước khi làm bất cứ việc gì khác, vì đó là giả định rủi ro nhất.

## File Structure

**Tạo mới — trong plugin (`MCG_3DPanel.csproj`):**

| File | Trách nhiệm |
|---|---|
| `Services/Replay/PartDump.cs` | DTO phẳng của 1 `PanelPart` + `ToDump()` / `ToPart()`. Hai chiều nằm cạnh nhau để không lệch nhau. |
| `Services/Replay/PartsDumpWriter.cs` | Ghi `List<PanelPart>` → JSON (dùng `JsonWriter` sẵn có). |
| `Commands/DumpPartsCommand.cs` | Lệnh `MCG_DumpParts`: pick block → collect → ghi JSON. |

**Tạo mới — runner (project riêng, KHÔNG nạp vào AutoCAD):**

| File | Trách nhiệm |
|---|---|
| `tools/PanelReplay/PanelReplay.csproj` | Console net48 x64, ref `MCG_3DPanel.dll` + `AcDbMgd.dll` + `System.Web.Extensions`. |
| `tools/PanelReplay/Program.cs` | CLI: `probe` / `selftest` / `check`. Exit code 0 = pass. |
| `tools/PanelReplay/Assert.cs` | Bộ assert tối giản cho selftest (không NuGet). |
| `tools/PanelReplay/PartsDumpReader.cs` | Đọc JSON dump → `List<PanelPart>`. |
| `tools/PanelReplay/GoldenCsv.cs` | Đọc `Handle,DetectedType,CorrectedType` → map handle → nhãn kỳ vọng. |
| `tools/PanelReplay/Comparer.cs` | So kết quả phân loại với golden → danh sách sai lệch. |
| `tools/PanelReplay/SelfTest.cs` | Unit test cho reader/golden/comparer. |

**Sửa:**

| File | Sửa gì |
|---|---|
| `Services/PanelPart.cs` | Thêm `Handle` (string) và `OwnerKey` (string). |
| `Services/Classify/PartCollector.cs` | Set `Handle` + `OwnerKey` khi dựng part. |
| `Services/ClassificationService.cs` | `OwnerBlock` đổi sang so `OwnerKey`; tách `ClassifyParts` public. |

**Vì sao cần `OwnerKey`:** `ClassificationService.OwnerBlock` hiện trả `IdPath[1]` (kiểu `ObjectId`). Khi replay headless, `IdPath` là null → mọi part trả `ObjectId.Null` → `OwnerBlock(p) == topKey` **luôn đúng** → mọi polyline nằm trong Top Plate bị gán nhầm `CutOut`. Đây là lỗi thật của harness nếu không sửa, nên `OwnerKey` là điều kiện tiên quyết, không phải tuỳ chọn.

---

### Task 1: Runner nạp được kiểu AutoCAD headless

Task rủi ro nhất, làm trước. Nếu bước này hỏng thì toàn bộ plan phải đổi hướng (chuyển harness vào trong AutoCAD).

**Files:**
- Create: `tools/PanelReplay/PanelReplay.csproj`
- Create: `tools/PanelReplay/Program.cs`
- Modify: `.gitignore`

**Interfaces:**
- Consumes: `MCG_3DPanel.dll` từ `bin/Release/net48/` (phải build plugin trước).
- Produces: exe `tools/PanelReplay/bin/Release/net48/PanelReplay.exe` với lệnh con `probe`.

- [ ] **Step 1: Tạo project runner**

Tạo `tools/PanelReplay/PanelReplay.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net48</TargetFramework>
    <LangVersion>8.0</LangVersion>
    <Platforms>x64</Platforms>
    <PlatformTarget>x64</PlatformTarget>
    <AssemblyName>PanelReplay</AssemblyName>
    <RootNamespace>PanelReplay</RootNamespace>
  </PropertyGroup>

  <ItemGroup>
    <Reference Include="System.Web.Extensions" />
    <!-- Copy AcDbMgd về output: runner chạy NGOÀI AutoCAD nên phải tự có assembly này.
         Native dep resolve qua SetDllDirectory trong Program.Main. -->
    <Reference Include="AcDbMgd">
      <HintPath>C:\Program Files\Autodesk\AutoCAD 2023\AcDbMgd.dll</HintPath>
      <Private>true</Private>
    </Reference>
    <Reference Include="MCG_3DPanel">
      <HintPath>..\..\bin\Release\net48\MCG_3DPanel.dll</HintPath>
      <Private>true</Private>
    </Reference>
  </ItemGroup>

</Project>
```

- [ ] **Step 2: Viết Program.cs với lệnh `probe`**

Tạo `tools/PanelReplay/Program.cs`. Lưu ý `RunProbe` bị đánh dấu `NoInlining` và tách khỏi `Main`: JIT chỉ resolve kiểu AutoCAD khi bắt đầu biên dịch method chứa chúng, nên phải gọi `SetDllDirectory` xong mới được chạm vào.

```csharp
using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace PanelReplay
{
    /// <summary>
    /// Runner kiểm chứng phân loại panel — chạy NGOÀI AutoCAD.
    /// Nạp chính MCG_3DPanel.dll nên không nhân bản logic: sai lệch phát hiện được là sai lệch thật.
    /// </summary>
    internal static class Program
    {
        /// <summary>Thư mục AutoCAD — nơi có native dep của AcDbMgd (acge*.dll, acdb*.dll…).</summary>
        private const string AcadDir = @"C:\Program Files\Autodesk\AutoCAD 2023";

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetDllDirectory(string lpPathName);

        private static int Main(string[] args)
        {
            if (!Directory.Exists(AcadDir))
            {
                Console.Error.WriteLine($"KHÔNG thấy thư mục AutoCAD: {AcadDir}");
                return 2;
            }
            // PHẢI gọi trước khi chạm bất kỳ kiểu AutoCAD nào.
            SetDllDirectory(AcadDir);

            string cmd = args.Length > 0 ? args[0] : "help";
            switch (cmd)
            {
                case "probe": return RunProbe();
                default:
                    Console.WriteLine("Dùng: PanelReplay probe");
                    return 1;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunProbe()
        {
            var a = new Autodesk.AutoCAD.Geometry.Point3d(0, 47, 0);
            var b = new Autodesk.AutoCAD.Geometry.Point3d(1000, 53, 0);
            var ext = new Autodesk.AutoCAD.DatabaseServices.Extents3d(a, b);

            var part = new MCG_3DPanel.Services.PanelPart
            {
                Layer = "Mechanical-AM_0",
                FootprintThickness = 6,
                MemberLength = 1000,
                Bounds = ext,
                Centroid = new Autodesk.AutoCAD.Geometry.Point3d(500, 50, 0),
            };
            var res = new MCG_3DPanel.Services.ClassificationResult();
            res.Parts.Add(part);

            Console.WriteLine($"AcGe OK   : bbox {ext.MinPoint} -> {ext.MaxPoint}");
            Console.WriteLine($"Plugin OK : {res.Summary()}");
            Console.WriteLine("PROBE PASS");
            return 0;
        }
    }
}
```

- [ ] **Step 3: Chặn output runner khỏi git**

Thêm vào cuối `.gitignore`:

```gitignore
# Runner kiểm chứng (build output)
/tools/PanelReplay/bin/
/tools/PanelReplay/obj/
```

- [ ] **Step 4: Build plugin rồi build runner**

Chạy:
```bash
cd "c:/Users/truonph/Desktop/MCG/AutoCAD/MCG_3DPanel"
dotnet build -c Release
dotnet build tools/PanelReplay/PanelReplay.csproj -c Release
```
Kỳ vọng: cả hai `0 Error(s)`.

- [ ] **Step 5: Chạy probe — đây là bài test của task này**

Chạy:
```bash
./tools/PanelReplay/bin/Release/net48/PanelReplay.exe probe
```
Kỳ vọng in ra `PROBE PASS` và exit code 0.

Nếu báo `Could not load file or assembly 'Acdbmgd'`: kiểm `AcDbMgd.dll` có trong thư mục output không (`<Private>true</Private>`). Nếu vẫn hỏng sau khi đã có file, thử chạy với PATH thủ công để tách biệt nguyên nhân:
```bash
PATH="/c/Program Files/Autodesk/AutoCAD 2023:$PATH" ./tools/PanelReplay/bin/Release/net48/PanelReplay.exe probe
```
Nếu cách này chạy được mà `SetDllDirectory` thì không, đổi Step 2 sang phát hành kèm file `PanelReplay.cmd` set PATH rồi gọi exe — **dừng lại báo cáo trước khi đi tiếp**, vì nó đổi cách gọi ở Task 7.

- [ ] **Step 6: Commit**

```bash
git add tools/PanelReplay/PanelReplay.csproj tools/PanelReplay/Program.cs .gitignore
git commit -m "test: add headless PanelReplay runner skeleton with AutoCAD type probe"
```

---

### Task 2: Bộ assert tối giản + khung selftest

**Files:**
- Create: `tools/PanelReplay/Assert.cs`
- Create: `tools/PanelReplay/SelfTest.cs`
- Modify: `tools/PanelReplay/Program.cs`

**Interfaces:**
- Produces:
  - `PanelReplay.Assert.Eq<T>(T actual, T expected, string name)` — so sánh bằng `EqualityComparer<T>.Default`.
  - `PanelReplay.Assert.EqNum(double actual, double expected, string name, double tol = 1e-6)`
  - `PanelReplay.Assert.True(bool cond, string name)`
  - `PanelReplay.Assert.Failures` (int), `PanelReplay.Assert.Checks` (int)
  - `PanelReplay.SelfTest.RunAll()` → int (0 = pass)

- [ ] **Step 1: Viết test đầu tiên (sẽ fail vì chưa có Assert)**

Tạo `tools/PanelReplay/SelfTest.cs`:

```csharp
using System;

namespace PanelReplay
{
    /// <summary>Unit test nội bộ của runner. Không dùng NuGet — giữ đúng nguyên tắc của repo.</summary>
    internal static class SelfTest
    {
        public static int RunAll()
        {
            Assert.Reset();
            AssertItself();
            return Assert.Report();
        }

        /// <summary>Test cho chính bộ assert — nếu nó sai thì mọi test khác vô nghĩa.</summary>
        private static void AssertItself()
        {
            Assert.True(true, "True(true) phải pass");
            Assert.Eq("x", "x", "Eq chuỗi bằng nhau");
            Assert.EqNum(1.0000001, 1.0, "EqNum trong dung sai", 1e-3);
        }
    }
}
```

- [ ] **Step 2: Chạy để xác nhận fail**

Chạy: `dotnet build tools/PanelReplay/PanelReplay.csproj -c Release`
Kỳ vọng: FAIL, `error CS0103: The name 'Assert' does not exist in the current context`.

- [ ] **Step 3: Viết Assert.cs**

Tạo `tools/PanelReplay/Assert.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PanelReplay
{
    /// <summary>Bộ assert tối giản: đếm check/fail, in dòng đỏ khi lệch. Không phụ thuộc framework test.</summary>
    internal static class Assert
    {
        public static int Checks { get; private set; }
        public static int Failures { get; private set; }

        public static void Reset() { Checks = 0; Failures = 0; }

        public static void True(bool cond, string name)
        {
            Checks++;
            if (cond) return;
            Failures++;
            Console.Error.WriteLine($"  FAIL {name}: kỳ vọng true");
        }

        public static void Eq<T>(T actual, T expected, string name)
        {
            Checks++;
            if (EqualityComparer<T>.Default.Equals(actual, expected)) return;
            Failures++;
            Console.Error.WriteLine($"  FAIL {name}: được '{actual}', kỳ vọng '{expected}'");
        }

        public static void EqNum(double actual, double expected, string name, double tol = 1e-6)
        {
            Checks++;
            if (Math.Abs(actual - expected) <= tol) return;
            Failures++;
            Console.Error.WriteLine($"  FAIL {name}: được {actual}, kỳ vọng {expected} (tol {tol})");
        }

        /// <summary>In tổng kết, trả exit code (0 = pass).</summary>
        public static int Report()
        {
            if (Failures == 0)
            {
                Console.WriteLine($"SELFTEST PASS — {Checks} check.");
                return 0;
            }
            Console.Error.WriteLine($"SELFTEST FAIL — {Failures}/{Checks} check lỗi.");
            return 1;
        }
    }
}
```

- [ ] **Step 4: Nối lệnh `selftest` vào CLI**

Trong `tools/PanelReplay/Program.cs`, sửa khối `switch` thành:

```csharp
            switch (cmd)
            {
                case "probe": return RunProbe();
                case "selftest": return SelfTest.RunAll();
                default:
                    Console.WriteLine("Dùng: PanelReplay probe | selftest");
                    return 1;
            }
```

- [ ] **Step 5: Chạy để xác nhận pass**

Chạy:
```bash
dotnet build tools/PanelReplay/PanelReplay.csproj -c Release
./tools/PanelReplay/bin/Release/net48/PanelReplay.exe selftest
```
Kỳ vọng: `SELFTEST PASS — 3 check.` và exit code 0.

- [ ] **Step 6: Commit**

```bash
git add tools/PanelReplay/Assert.cs tools/PanelReplay/SelfTest.cs tools/PanelReplay/Program.cs
git commit -m "test: add minimal assert helper and selftest harness to PanelReplay"
```

---

### Task 3: `PanelPart.Handle` + `OwnerKey`, và `OwnerBlock` dùng `OwnerKey`

Điều kiện tiên quyết cho replay (xem giải thích ở mục File Structure). Thay đổi bảo toàn hành vi trong AutoCAD.

**Files:**
- Modify: `Services/PanelPart.cs`
- Modify: `Services/Classify/PartCollector.cs`
- Modify: `Services/ClassificationService.cs:136-159`

**Interfaces:**
- Consumes: không có (task nền).
- Produces:
  - `MCG_3DPanel.Services.PanelPart.Handle` (string) — handle của polyline nguồn, `""` nếu không có.
  - `MCG_3DPanel.Services.PanelPart.OwnerKey` (string) — khoá block con cấp 1, `""` nếu part nằm trực tiếp trong block ngoài cùng.

- [ ] **Step 1: Thêm 2 field vào PanelPart**

Trong `Services/PanelPart.cs`, chèn ngay sau khai báo `IdPath` (dòng 21):

```csharp
        // Handle của polyline nguồn dạng chuỗi. Giữ RIÊNG khỏi Id vì khi replay headless
        // (runner PanelReplay) Id là ObjectId.Null — chuỗi này mới là danh tính bền vững.
        public string Handle { get; set; } = "";

        // Khoá BLOCK CON cấp 1 chứa part (chuỗi hoá từ IdPath[1]). "" = nằm trực tiếp trong
        // block ngoài cùng. Dùng chuỗi thay ObjectId để so sánh được cả khi replay ngoài AutoCAD.
        public string OwnerKey { get; set; } = "";
```

- [ ] **Step 2: Set 2 field trong PartCollector**

Trong `Services/Classify/PartCollector.cs`, hàm `Collect` (dòng 40-51), thay khối tạo part:

```csharp
                if (ent is Polyline pl && pl.Closed)
                {
                    var part = BuildPart(pl, accum);
                    if (part != null)
                    {
                        var full = new ObjectId[pathPrefix.Count + 1];
                        pathPrefix.CopyTo(full);
                        full[pathPrefix.Count] = pl.ObjectId;
                        part.IdPath = full;
                        part.Handle = pl.ObjectId.Handle.ToString();
                        // Block con cấp 1 = IdPath[1]; cần ít nhất [ngoài cùng, blockcon, polyline].
                        part.OwnerKey = full.Length >= 3 ? full[1].Handle.ToString() : "";
                        outParts.Add(part);
                    }
                }
```

- [ ] **Step 3: Đổi `OwnerBlock` sang `OwnerKey`**

Trong `Services/ClassificationService.cs`, thay hàm `OwnerBlock` (dòng 156-159) bằng:

```csharp
        /// <summary>Khoá block con cấp 1 chứa part. Chuỗi (không phải ObjectId) để replay headless
        /// so sánh được — xem tools/PanelReplay.</summary>
        private static string OwnerBlock(PanelPart p) => p.OwnerKey ?? "";
```

Rồi sửa 3 chỗ dùng kiểu `ObjectId` cho khoá này:

Dòng 82 — đổi:
```csharp
                ObjectId topKey = top != null ? OwnerBlock(top) : ObjectId.Null;
```
thành:
```csharp
                string topKey = top != null ? OwnerBlock(top) : "";
```

Dòng 144-152 trong `FilterToPanelBlocks` — đổi:
```csharp
            ObjectId topKey = OwnerBlock(top);
            ObjectId webKey = all.Where(p => p.Layer == ClassifyUtil.LAYER_WEB)
                                 .GroupBy(OwnerBlock)
                                 .OrderByDescending(g => g.Sum(p => p.MemberLength))
                                 .Select(g => g.Key)
                                 .DefaultIfEmpty(topKey)
                                 .First();

            var keep = new HashSet<ObjectId> { topKey, webKey };
```
thành:
```csharp
            string topKey = OwnerBlock(top);
            string webKey = all.Where(p => p.Layer == ClassifyUtil.LAYER_WEB)
                               .GroupBy(OwnerBlock)
                               .OrderByDescending(g => g.Sum(p => p.MemberLength))
                               .Select(g => g.Key)
                               .DefaultIfEmpty(topKey)
                               .First();

            var keep = new HashSet<string> { topKey, webKey };
```

- [ ] **Step 4: Build kiểm tra**

Chạy: `dotnet build -c Release`
Kỳ vọng: `0 Error(s)`, `0 Warning(s)`.

Nếu còn lỗi `ObjectId` không đổi được sang `string`, tìm chỗ còn sót bằng:
```bash
grep -n "topKey\|webKey\|OwnerBlock" Services/ClassificationService.cs
```

- [ ] **Step 5: Commit**

```bash
git add Services/PanelPart.cs Services/Classify/PartCollector.cs Services/ClassificationService.cs
git commit -m "refactor: identify parts and owner blocks by string key for headless replay"
```

---

### Task 4: Tách `ClassifyParts` thuần khỏi transaction

**Files:**
- Modify: `Services/ClassificationService.cs:24-128`

**Interfaces:**
- Consumes: `PanelPart.OwnerKey` (Task 3).
- Produces: `public ClassificationResult ClassifyParts(List<PanelPart> all, PanelPosition position, string panelBlockName)` trên `MCG_3DPanel.Services.ClassificationService` — **không** đụng `Transaction`/`Database`, gọi được headless.

- [ ] **Step 1: Tách hàm**

Trong `Services/ClassificationService.cs`, thay toàn bộ hàm `Classify` (dòng 24-128) bằng hai hàm sau. Nội dung `ClassifyParts` giữ **nguyên xi** logic cũ từ `FilterToPanelBlocks` trở đi — chỉ đổi vỏ:

```csharp
        public ClassificationResult Classify(ObjectId blockRefId, PanelPosition position)
        {
            Database db = blockRefId.Database;
            var all = new List<PanelPart>();
            string blockName;

            using (Transaction tr = db.TransactionManager.StartTransaction())
            {
                var topRef = (BlockReference)tr.GetObject(blockRefId, OpenMode.ForRead);
                blockName = PartCollector.GetName(tr, topRef);

                // 1. Thu thập (bước DUY NHẤT cần database).
                PartCollector.Collect(tr, topRef, topRef.BlockTransform, all);
                Debug.WriteLine($"{LOG_PREFIX} Thu {all.Count} closed polyline.");
                tr.Commit();
            }

            return ClassifyParts(all, position, blockName);
        }

        /// <summary>
        /// Phân loại THUẦN từ danh sách part đã thu thập — KHÔNG đụng Database/Transaction.
        /// Nhờ vậy runner headless (tools/PanelReplay) chạy được đúng logic này để kiểm chứng hồi quy.
        /// <paramref name="all"/> là output thô của <see cref="PartCollector.Collect"/> (CHƯA lọc block).
        /// </summary>
        public ClassificationResult ClassifyParts(List<PanelPart> all, PanelPosition position, string panelBlockName)
        {
            var result = new ClassificationResult { PanelBlockName = panelBlockName };

            // 1b. Lọc về ĐÚNG panel: chỉ giữ part thuộc 2 block con (chứa Top Plate + chứa Web chính).
            FilterToPanelBlocks(all);
            Debug.WriteLine($"{LOG_PREFIX} Sau lọc block panel: {all.Count} part.");

            // Định tuyến theo LAYER (+ màu cho AM_3). AM_0 HOÃN lại để tách tấm/cutout trước web.
            var topCands = new List<PanelPart>();
            var am0 = new List<PanelPart>();
            var webCands = new List<PanelPart>();
            var hpParts = new List<PanelPart>();
            var fbParts = new List<PanelPart>();

            foreach (var p in all)
            {
                switch (p.Layer)
                {
                    case ClassifyUtil.LAYER_TOPPLATE:
                        topCands.Add(p);
                        break;
                    case ClassifyUtil.LAYER_FLANGE:
                        p.Type = ClassifyUtil.IsRectangle(p) ? PartType.Flange : PartType.FacePlate;
                        result.Parts.Add(p);
                        break;
                    case ClassifyUtil.LAYER_WEB:
                        am0.Add(p);
                        break;
                    case ClassifyUtil.LAYER_FBHP:
                        if (p.ColorIndex == ClassifyUtil.COLOR_HP) { p.Type = PartType.HP; hpParts.Add(p); }
                        else { p.Type = PartType.FlatBar; fbParts.Add(p); }
                        result.Parts.Add(p);
                        break;
                }
            }

            // Top Plate = AREA lớn nhất trên layer 0.
            PanelPart top = null;
            if (topCands.Count > 0)
            {
                top = topCands.OrderByDescending(p => p.Area).First();
                top.Type = PartType.TopPlate;
                result.TopPlate = top;
                result.Parts.Add(top);
            }
            string topKey = top != null ? OwnerBlock(top) : "";
            var faceplates = result.OfType(PartType.FacePlate).ToList();
            var longWebs = am0.Where(p => p.MemberLength > ClassifyUtil.WEB_MIN_LEN).ToList();

            // CutOut layer-0: polyline layer 0 khác Top Plate, cùng block Top Plate, nằm HẲN trong biên.
            if (top != null)
                foreach (var p in topCands)
                {
                    if (ReferenceEquals(p, top)) continue;
                    if (OwnerBlock(p) == topKey && ClassifyUtil.StrictlyInside(p, top, ClassifyUtil.EDGE_TOL))
                    { p.Type = PartType.CutOut; result.Parts.Add(p); }
                }

            // AM_0: tách CutOut / BackingPlate / ClosedPlate; phần còn lại mới đưa vào Web/Bracket.
            foreach (var p in am0)
            {
                if (top != null && OwnerBlock(p) == topKey
                    && ClassifyUtil.StrictlyInside(p, top, ClassifyUtil.EDGE_TOL))
                { p.Type = PartType.CutOut; result.Parts.Add(p); continue; }

                if (PlateDetector.IsBackingPlate(p, faceplates, longWebs))
                { p.Type = PartType.BackingPlate; result.Parts.Add(p); continue; }

                bool touches = hpParts.Any(h => ClassifyUtil.Adjacent(p, h, ClassifyUtil.ADJ_TOL))
                            || fbParts.Any(f => ClassifyUtil.Adjacent(p, f, ClassifyUtil.ADJ_TOL));
                if (!touches && PlateDetector.IsClosedPlate(p, faceplates, longWebs))
                { p.Type = PartType.ClosedPlate; result.Parts.Add(p); continue; }

                webCands.Add(p);
            }

            // 2. Web vs Bracket
            WebBracketSplitter.Split(webCands, hpParts, fbParts, result);

            // 2b. Tách Girder End (GE) khỏi Bracket
            GirderEndDetector.Apply(result, hpParts, fbParts);

            // 3. Mã bracket + hướng bụng HP
            Point3d center = result.TopPlate?.Centroid ?? Point3d.Origin;
            BracketCoder.Apply(result, hpParts, fbParts, position, center);

            Debug.WriteLine($"{LOG_PREFIX} {result.Summary()}");
            return result;
        }
```

- [ ] **Step 2: Cập nhật interface**

Mở `Services/IClassificationService.cs`. Nếu interface khai báo `Classify`, thêm `ClassifyParts` vào cho khớp:

```csharp
        /// <summary>Phân loại thuần từ part đã thu thập — không cần Database (dùng cho replay headless).</summary>
        ClassificationResult ClassifyParts(List<PanelPart> all, PanelPosition position, string panelBlockName);
```

Nhớ `using System.Collections.Generic;` ở đầu file nếu chưa có.

- [ ] **Step 3: Build kiểm tra**

Chạy: `dotnet build -c Release`
Kỳ vọng: `0 Error(s)`, `0 Warning(s)`.

- [ ] **Step 4: Kiểm tra hồi quy thủ công một lần**

Đây là lần cuối phải kiểm bằng tay — từ Task 7 trở đi việc này tự động.

1. `deploy.bat`
2. Trong AutoCAD: NETLOAD → `MCG_Build3DPanel` → **Classify Panel** trên panel 7D-17C.
3. Ghi lại dòng Summary trên status bar.

Kỳ vọng: số đếm từng loại **giống hệt** trước khi refactor. Nếu lệch → refactor đã đổi hành vi, quay lại Step 1 đối chiếu từng dòng.

- [ ] **Step 5: Commit**

```bash
git add Services/ClassificationService.cs Services/IClassificationService.cs
git commit -m "refactor: extract pure ClassifyParts from database transaction"
```

---

### Task 5: DTO `PartDump` + round-trip

**Files:**
- Create: `Services/Replay/PartDump.cs`
- Modify: `tools/PanelReplay/SelfTest.cs`

**Interfaces:**
- Consumes: `PanelPart.Handle`, `PanelPart.OwnerKey` (Task 3).
- Produces: `MCG_3DPanel.Services.Replay.PartDump` với:
  - field public: `handle, ownerKey, layer, color, nv, area, thk, len, horiz, perp, cx, cy, ax, ay, minx, miny, maxx, maxy` và `double[][] verts`
  - `static PartDump From(PanelPart p)`
  - `PanelPart ToPart()`

Ghi chú thiết kế: tên field cố tình viết thường ngắn gọn vì chúng là **tên khoá JSON**; `JavaScriptSerializer` map theo tên không phân biệt hoa thường nhưng giữ thống nhất cho dễ đọc file. Mọi toạ độ Z ghi `0` (panel luôn phẳng trên XY). `Transform` không dump vì phân loại không dùng tới — `ToPart()` set `Matrix3d.Identity`.

- [ ] **Step 1: Viết test round-trip (sẽ fail)**

Trong `tools/PanelReplay/SelfTest.cs`, thêm `using` và gọi hàm test mới:

```csharp
using System;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Replay;
```

Sửa `RunAll` thành:

```csharp
        public static int RunAll()
        {
            Assert.Reset();
            AssertItself();
            PartDumpRoundTrip();
            return Assert.Report();
        }
```

Và thêm hàm:

```csharp
        /// <summary>PanelPart -> PartDump -> PanelPart phải giữ nguyên mọi trường phân loại dùng tới.</summary>
        private static void PartDumpRoundTrip()
        {
            var src = new PanelPart
            {
                Handle = "38E4A",
                OwnerKey = "38DD0",
                Layer = "Mechanical-AM_0",
                ColorIndex = 256,
                VertexCount = 4,
                Area = 6000.0,
                FootprintThickness = 6.0,
                MemberLength = 1000.0,
                IsHorizontal = true,
                PerpCenter = 50.0,
                Centroid = new Point3d(500, 50, 0),
                AxisDir = new Vector3d(1, 0, 0),
                Bounds = new Extents3d(new Point3d(0, 47, 0), new Point3d(1000, 53, 0)),
                Vertices = new[]
                {
                    new Point3d(0, 47, 0), new Point3d(1000, 47, 0),
                    new Point3d(1000, 53, 0), new Point3d(0, 53, 0),
                },
            };

            PanelPart r = PartDump.From(src).ToPart();

            Assert.Eq(r.Handle, "38E4A", "roundtrip Handle");
            Assert.Eq(r.OwnerKey, "38DD0", "roundtrip OwnerKey");
            Assert.Eq(r.Layer, "Mechanical-AM_0", "roundtrip Layer");
            Assert.Eq(r.ColorIndex, 256, "roundtrip ColorIndex");
            Assert.Eq(r.VertexCount, 4, "roundtrip VertexCount");
            Assert.EqNum(r.Area, 6000.0, "roundtrip Area");
            Assert.EqNum(r.FootprintThickness, 6.0, "roundtrip FootprintThickness");
            Assert.EqNum(r.MemberLength, 1000.0, "roundtrip MemberLength");
            Assert.True(r.IsHorizontal, "roundtrip IsHorizontal");
            Assert.EqNum(r.PerpCenter, 50.0, "roundtrip PerpCenter");
            Assert.EqNum(r.Centroid.X, 500.0, "roundtrip Centroid.X");
            Assert.EqNum(r.AxisDir.X, 1.0, "roundtrip AxisDir.X");
            Assert.EqNum(r.Bounds.MinPoint.Y, 47.0, "roundtrip Bounds.MinPoint.Y");
            Assert.EqNum(r.Bounds.MaxPoint.X, 1000.0, "roundtrip Bounds.MaxPoint.X");
            Assert.Eq(r.Vertices.Length, 4, "roundtrip số đỉnh");
            Assert.EqNum(r.Vertices[1].X, 1000.0, "roundtrip đỉnh[1].X");
        }
```

- [ ] **Step 2: Chạy để xác nhận fail**

Chạy: `dotnet build tools/PanelReplay/PanelReplay.csproj -c Release`
Kỳ vọng: FAIL, `error CS0234: ... 'Replay' does not exist in the namespace 'MCG_3DPanel.Services'`.

- [ ] **Step 3: Viết PartDump.cs**

Tạo `Services/Replay/PartDump.cs`:

```csharp
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.Geometry;

namespace MCG_3DPanel.Services.Replay
{
    /// <summary>
    /// DTO phẳng của 1 <see cref="PanelPart"/> — đủ để chạy lại TOÀN BỘ bước phân loại ngoài AutoCAD.
    /// Tên field chính là tên khoá JSON nên cố tình ngắn.
    ///
    /// KHÔNG dump <see cref="PanelPart.Transform"/> (chỉ Build3D dùng) và các trường KẾT QUẢ
    /// (Type/Code/GroupId/BellyDir) — chúng là thứ ta muốn tính lại để kiểm chứng.
    /// Panel luôn phẳng trên XY nên mọi Z ghi 0. Xem tools/PanelReplay.
    /// </summary>
    public class PartDump
    {
        public string handle { get; set; }
        public string ownerKey { get; set; }
        public string layer { get; set; }
        public int color { get; set; }
        public int nv { get; set; }
        public double area { get; set; }
        public double thk { get; set; }
        public double len { get; set; }
        public bool horiz { get; set; }
        public double perp { get; set; }
        public double cx { get; set; }
        public double cy { get; set; }
        public double ax { get; set; }
        public double ay { get; set; }
        public double minx { get; set; }
        public double miny { get; set; }
        public double maxx { get; set; }
        public double maxy { get; set; }
        public double[][] verts { get; set; }

        public static PartDump From(PanelPart p)
        {
            var d = new PartDump
            {
                handle = p.Handle ?? "",
                ownerKey = p.OwnerKey ?? "",
                layer = p.Layer ?? "",
                color = p.ColorIndex,
                nv = p.VertexCount,
                area = p.Area,
                thk = p.FootprintThickness,
                len = p.MemberLength,
                horiz = p.IsHorizontal,
                perp = p.PerpCenter,
                cx = p.Centroid.X,
                cy = p.Centroid.Y,
                ax = p.AxisDir.X,
                ay = p.AxisDir.Y,
                minx = p.Bounds.MinPoint.X,
                miny = p.Bounds.MinPoint.Y,
                maxx = p.Bounds.MaxPoint.X,
                maxy = p.Bounds.MaxPoint.Y,
            };

            var src = p.Vertices;
            d.verts = new double[src == null ? 0 : src.Length][];
            for (int i = 0; i < d.verts.Length; i++)
                d.verts[i] = new[] { src[i].X, src[i].Y };
            return d;
        }

        public PanelPart ToPart()
        {
            var pts = new Point3d[verts == null ? 0 : verts.Length];
            for (int i = 0; i < pts.Length; i++)
                pts[i] = new Point3d(verts[i][0], verts[i][1], 0);

            return new PanelPart
            {
                Id = ObjectId.Null,
                IdPath = null,
                Handle = handle ?? "",
                OwnerKey = ownerKey ?? "",
                Type = Models.PartType.Unknown,
                Layer = layer ?? "",
                ColorIndex = color,
                VertexCount = nv,
                Area = area,
                FootprintThickness = thk,
                MemberLength = len,
                IsHorizontal = horiz,
                PerpCenter = perp,
                Centroid = new Point3d(cx, cy, 0),
                AxisDir = new Vector3d(ax, ay, 0),
                Bounds = new Extents3d(new Point3d(minx, miny, 0), new Point3d(maxx, maxy, 0)),
                Vertices = pts,
                Transform = Matrix3d.Identity,
            };
        }
    }
}
```

- [ ] **Step 4: Chạy để xác nhận pass**

Chạy:
```bash
dotnet build -c Release
dotnet build tools/PanelReplay/PanelReplay.csproj -c Release
./tools/PanelReplay/bin/Release/net48/PanelReplay.exe selftest
```
Kỳ vọng: `SELFTEST PASS — 19 check.`

- [ ] **Step 5: Commit**

```bash
git add Services/Replay/PartDump.cs tools/PanelReplay/SelfTest.cs
git commit -m "feat: add PartDump DTO with PanelPart round-trip"
```

---

### Task 6: Lệnh `MCG_DumpParts` xuất dump JSON

**Files:**
- Create: `Services/Replay/PartsDumpWriter.cs`
- Create: `Commands/DumpPartsCommand.cs`

**Interfaces:**
- Consumes: `PartDump.From` (Task 5), `PartCollector.Collect`, `ReportOutput.BuildOutputPath` / `OpenFile`.
- Produces:
  - `MCG_3DPanel.Services.Replay.PartsDumpWriter.Write(string path, string panelBlockName, System.Collections.Generic.List<PanelPart> parts)`
  - Lệnh AutoCAD `MCG_DumpParts`.

Định dạng file:
```json
{
  "panelBlockName": "7D-17C_Assy",
  "partCount": 312,
  "parts": [ { "handle": "...", "ownerKey": "...", ... } ]
}
```

- [ ] **Step 1: Viết PartsDumpWriter**

Tạo `Services/Replay/PartsDumpWriter.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;
using MCG_3DPanel.Utilities;

namespace MCG_3DPanel.Services.Replay
{
    /// <summary>
    /// Ghi danh sách <see cref="PanelPart"/> THÔ (output của PartCollector, CHƯA lọc block) ra JSON
    /// để runner headless chạy lại. Chỉ ghi — phần đọc nằm ở tools/PanelReplay (dùng BCL JSON).
    /// </summary>
    public static class PartsDumpWriter
    {
        public static void Write(string path, string panelBlockName, List<PanelPart> parts)
        {
            var arr = new List<object>(parts.Count);
            foreach (var p in parts)
            {
                var d = PartDump.From(p);
                var verts = new List<object>(d.verts.Length);
                foreach (var v in d.verts) verts.Add(new List<object> { v[0], v[1] });

                arr.Add(new JsonObject
                {
                    { "handle", d.handle }, { "ownerKey", d.ownerKey },
                    { "layer", d.layer }, { "color", d.color }, { "nv", d.nv },
                    { "area", d.area }, { "thk", d.thk }, { "len", d.len },
                    { "horiz", d.horiz }, { "perp", d.perp },
                    { "cx", d.cx }, { "cy", d.cy }, { "ax", d.ax }, { "ay", d.ay },
                    { "minx", d.minx }, { "miny", d.miny }, { "maxx", d.maxx }, { "maxy", d.maxy },
                    { "verts", verts },
                });
            }

            var root = new JsonObject
            {
                { "panelBlockName", panelBlockName ?? "" },
                { "partCount", parts.Count },
                { "parts", arr },
            };
            File.WriteAllText(path, JsonWriter.Serialize(root), Encoding.UTF8);
        }
    }
}
```

- [ ] **Step 2: Viết lệnh MCG_DumpParts**

Tạo `Commands/DumpPartsCommand.cs`:

```csharp
using System.Collections.Generic;
using Autodesk.AutoCAD.ApplicationServices;
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
using Autodesk.AutoCAD.Runtime;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Classify;
using MCG_3DPanel.Services.Replay;

namespace MCG_3DPanel.Commands
{
    /// <summary>
    /// Lệnh <c>MCG_DumpParts</c> — xuất danh sách PanelPart THÔ ra JSON để kiểm chứng hồi quy
    /// bằng runner headless (tools/PanelReplay). Không phân loại, không dựng gì.
    /// </summary>
    public class DumpPartsCommand
    {
        private const string LOG_PREFIX = "[MCG_3DPanel]";

        [CommandMethod("MCG_DumpParts")]
        public void DumpParts()
        {
            Document doc = Application.DocumentManager.MdiActiveDocument;
            if (doc == null) return;
            Editor ed = doc.Editor;

            var peo = new PromptEntityOptions("\nChọn Block Panel để dump part:");
            peo.SetRejectMessage("\nĐối tượng phải là 1 Block Reference (INSERT).");
            peo.AddAllowedClass(typeof(BlockReference), exactMatch: false);
            PromptEntityResult per = ed.GetEntity(peo);
            if (per.Status != PromptStatus.OK) { ed.WriteMessage("\nĐã huỷ.\n"); return; }

            using (doc.LockDocument())
            {
                var all = new List<PanelPart>();
                string blockName;
                using (Transaction tr = doc.Database.TransactionManager.StartTransaction())
                {
                    var topRef = (BlockReference)tr.GetObject(per.ObjectId, OpenMode.ForRead);
                    blockName = PartCollector.GetName(tr, topRef);
                    PartCollector.Collect(tr, topRef, topRef.BlockTransform, all);
                    tr.Commit();
                }

                string outPath = ReportOutput.BuildOutputPath("Parts");
                PartsDumpWriter.Write(outPath, blockName, all);
                ed.WriteMessage($"\n{LOG_PREFIX} Đã dump {all.Count} part:\n  {outPath}\n");
                ReportOutput.OpenFile(outPath);
            }
        }
    }
}
```

- [ ] **Step 3: Mở quyền truy cập cho ReportOutput**

`ReportOutput` đang là `internal static` — cùng assembly nên `DumpPartsCommand` dùng được, không cần sửa. Xác nhận bằng build.

Chạy: `dotnet build -c Release`
Kỳ vọng: `0 Error(s)`, `0 Warning(s)`.

- [ ] **Step 4: Sinh dump thật cho panel mẫu**

1. `deploy.bat`
2. Trong AutoCAD: NETLOAD → mở bản vẽ panel `7D-17C` → gõ `MCG_DumpParts` → pick block panel.
3. File `MCG_PanelParts_<timestamp>.json` xuất ra Desktop.
4. Chép vào repo:

```bash
mkdir -p samples/7D-17C
cp ~/Desktop/MCG_PanelParts_*.json samples/7D-17C/parts.json
```

Kỳ vọng: `parts.json` mở được, có khoá `partCount` > 0 và mảng `parts` cùng số phần tử.

- [ ] **Step 5: Commit**

```bash
git add Services/Replay/PartsDumpWriter.cs Commands/DumpPartsCommand.cs samples/7D-17C/parts.json
git commit -m "feat: add MCG_DumpParts command exporting raw PanelPart list to JSON"
```

---

### Task 7: Reader + golden CSV + comparer + lệnh `check`

Task cuối, khép vòng lặp.

**Files:**
- Create: `tools/PanelReplay/PartsDumpReader.cs`
- Create: `tools/PanelReplay/GoldenCsv.cs`
- Create: `tools/PanelReplay/Comparer.cs`
- Modify: `tools/PanelReplay/SelfTest.cs`
- Modify: `tools/PanelReplay/Program.cs`
- Create: `samples/7D-17C/golden.csv`
- Create: `docs/regression-testing.md`

**Interfaces:**
- Consumes: `PartDump.ToPart()` (Task 5), `ClassificationService.ClassifyParts` (Task 4), `MCG_3DPanel.UI.ResultRow.Label(PanelPart)`.
- Produces:
  - `PanelReplay.PartsDumpReader.Read(string path, out string panelBlockName)` → `List<PanelPart>`
  - `PanelReplay.GoldenCsv.Read(string path)` → `Dictionary<string,string>` (handle → nhãn kỳ vọng)
  - `PanelReplay.Comparer.Compare(ClassificationResult, Dictionary<string,string>)` → `List<string>` (mô tả sai lệch, rỗng = pass)

Ngữ nghĩa golden: nhãn kỳ vọng = `CorrectedType` nếu khác rỗng, ngược lại `DetectedType` — đúng như comment sẵn có trong `ResultRow` ("để trống = đồng ý").

- [ ] **Step 1: Viết test cho GoldenCsv và Comparer (sẽ fail)**

Trong `tools/PanelReplay/SelfTest.cs`, thêm `using System.Collections.Generic;` và `using System.IO;`, sửa `RunAll`:

```csharp
        public static int RunAll()
        {
            Assert.Reset();
            AssertItself();
            PartDumpRoundTrip();
            GoldenCsvParsing();
            ComparerDetectsMismatch();
            return Assert.Report();
        }
```

Thêm 2 hàm test:

```csharp
        /// <summary>CorrectedType khác rỗng thì thắng; rỗng thì lấy DetectedType. Field có dấu phẩy phải bọc "".</summary>
        private static void GoldenCsvParsing()
        {
            string tmp = Path.Combine(Path.GetTempPath(), "mcg_golden_test.csv");
            File.WriteAllText(tmp,
                "Handle,DetectedType,CorrectedType\n" +
                "38E4A,web10,\n" +
                "38ED5,web10,backing plate\n" +
                "38DDC,topplate,\n");

            var g = GoldenCsv.Read(tmp);
            File.Delete(tmp);

            Assert.Eq(g.Count, 3, "golden: số dòng");
            Assert.Eq(g["38E4A"], "web10", "golden: CorrectedType rỗng -> DetectedType");
            Assert.Eq(g["38ED5"], "backing plate", "golden: CorrectedType thắng");
            Assert.Eq(g["38DDC"], "topplate", "golden: dòng cuối");
        }

        /// <summary>Comparer phải bắt được cả sai nhãn lẫn handle thiếu.</summary>
        private static void ComparerDetectsMismatch()
        {
            var res = new ClassificationResult();
            res.Parts.Add(new PanelPart
            {
                Handle = "AAA", Type = MCG_3DPanel.Models.PartType.Web,
                FootprintThickness = 6.0, Layer = "Mechanical-AM_0",
            });

            var okGolden = new Dictionary<string, string> { { "AAA", "web6" } };
            Assert.Eq(Comparer.Compare(res, okGolden).Count, 0, "comparer: khớp -> 0 sai lệch");

            var badGolden = new Dictionary<string, string> { { "AAA", "OB" } };
            Assert.Eq(Comparer.Compare(res, badGolden).Count, 1, "comparer: sai nhãn -> 1 sai lệch");

            var missGolden = new Dictionary<string, string> { { "AAA", "web6" }, { "ZZZ", "hp" } };
            Assert.Eq(Comparer.Compare(res, missGolden).Count, 1, "comparer: handle thiếu -> 1 sai lệch");
        }
```

- [ ] **Step 2: Chạy để xác nhận fail**

Chạy: `dotnet build tools/PanelReplay/PanelReplay.csproj -c Release`
Kỳ vọng: FAIL, `error CS0103: The name 'GoldenCsv' does not exist` (và `Comparer`).

- [ ] **Step 3: Viết GoldenCsv.cs**

Tạo `tools/PanelReplay/GoldenCsv.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace PanelReplay
{
    /// <summary>
    /// Đọc file golden xuất từ tab Result của palette: <c>Handle,DetectedType,CorrectedType</c>.
    /// Nhãn KỲ VỌNG = CorrectedType nếu kỹ sư đã sửa, ngược lại là DetectedType ("để trống = đồng ý").
    /// </summary>
    internal static class GoldenCsv
    {
        public static Dictionary<string, string> Read(string path)
        {
            var map = new Dictionary<string, string>();
            string[] lines = File.ReadAllLines(path, Encoding.UTF8);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (i == 0 && line.TrimStart('\uFEFF').StartsWith("Handle")) continue;  // header (có thể kèm BOM)
                if (string.IsNullOrWhiteSpace(line)) continue;

                List<string> f = SplitCsv(line);
                if (f.Count < 2) continue;

                string handle = f[0].Trim().TrimStart('\uFEFF');
                string detected = f[1].Trim();
                string corrected = f.Count > 2 ? f[2].Trim() : "";
                if (handle.Length == 0) continue;

                map[handle] = corrected.Length > 0 ? corrected : detected;
            }
            return map;
        }

        /// <summary>Tách 1 dòng CSV, hiểu field bọc dấu " và "" escape bên trong.</summary>
        private static List<string> SplitCsv(string line)
        {
            var fields = new List<string>();
            var sb = new StringBuilder();
            bool quoted = false;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"') { sb.Append('"'); i++; }
                        else quoted = false;
                    }
                    else sb.Append(c);
                }
                else if (c == '"') quoted = true;
                else if (c == ',') { fields.Add(sb.ToString()); sb.Clear(); }
                else sb.Append(c);
            }
            fields.Add(sb.ToString());
            return fields;
        }
    }
}
```

- [ ] **Step 4: Viết Comparer.cs**

Tạo `tools/PanelReplay/Comparer.cs`:

```csharp
using System.Collections.Generic;
using MCG_3DPanel.Services;
using MCG_3DPanel.UI;

namespace PanelReplay
{
    /// <summary>
    /// So kết quả phân loại với golden. Dùng thẳng <see cref="ResultRow.Label"/> — đúng nhãn
    /// mà grid hiển thị và người dùng đã sửa, nên hai bên không bao giờ lệch quy ước.
    /// </summary>
    internal static class Comparer
    {
        public static List<string> Compare(ClassificationResult result, Dictionary<string, string> golden)
        {
            var diffs = new List<string>();

            var actual = new Dictionary<string, string>();
            foreach (var p in result.Parts)
            {
                if (string.IsNullOrEmpty(p.Handle)) continue;
                actual[p.Handle] = ResultRow.Label(p);
            }

            foreach (var kv in golden)
            {
                if (!actual.TryGetValue(kv.Key, out string got))
                {
                    diffs.Add($"THIẾU   {kv.Key}: golden '{kv.Value}' nhưng không có trong kết quả");
                    continue;
                }
                if (got != kv.Value)
                    diffs.Add($"SAI     {kv.Key}: được '{got}', golden '{kv.Value}'");
            }

            foreach (var kv in actual)
                if (!golden.ContainsKey(kv.Key))
                    diffs.Add($"THỪA    {kv.Key}: kết quả '{kv.Value}' nhưng golden không có");

            return diffs;
        }
    }
}
```

- [ ] **Step 5: Viết PartsDumpReader.cs**

Tạo `tools/PanelReplay/PartsDumpReader.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Web.Script.Serialization;
using MCG_3DPanel.Services;
using MCG_3DPanel.Services.Replay;

namespace PanelReplay
{
    /// <summary>Đọc file dump của <c>MCG_DumpParts</c> → danh sách PanelPart (dùng BCL, không NuGet).</summary>
    internal static class PartsDumpReader
    {
        private class Root
        {
            public string panelBlockName { get; set; }
            public int partCount { get; set; }
            public PartDump[] parts { get; set; }
        }

        public static List<PanelPart> Read(string path, out string panelBlockName)
        {
            string json = File.ReadAllText(path, Encoding.UTF8);
            var ser = new JavaScriptSerializer { MaxJsonLength = int.MaxValue };
            Root root = ser.Deserialize<Root>(json);

            panelBlockName = root.panelBlockName ?? "";
            var parts = new List<PanelPart>(root.parts == null ? 0 : root.parts.Length);
            if (root.parts != null)
                foreach (var d in root.parts) parts.Add(d.ToPart());
            return parts;
        }
    }
}
```

- [ ] **Step 6: Chạy selftest để xác nhận pass**

Chạy:
```bash
dotnet build tools/PanelReplay/PanelReplay.csproj -c Release
./tools/PanelReplay/bin/Release/net48/PanelReplay.exe selftest
```
Kỳ vọng: `SELFTEST PASS — 26 check.`

- [ ] **Step 7: Nối lệnh `check` vào CLI**

Trong `tools/PanelReplay/Program.cs`, sửa `switch`:

```csharp
            switch (cmd)
            {
                case "probe": return RunProbe();
                case "selftest": return SelfTest.RunAll();
                case "check":
                    if (args.Length < 3)
                    {
                        Console.Error.WriteLine("Dùng: PanelReplay check <parts.json> <golden.csv> [Center|Port|Starboard]");
                        return 2;
                    }
                    return RunCheck(args[1], args[2], args.Length > 3 ? args[3] : "Center");
                default:
                    Console.WriteLine("Dùng: PanelReplay probe | selftest | check <parts.json> <golden.csv> [position]");
                    return 1;
            }
```

Thêm hàm (cùng class `Program`):

```csharp
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static int RunCheck(string dumpPath, string goldenPath, string positionName)
        {
            if (!File.Exists(dumpPath)) { Console.Error.WriteLine($"Không thấy dump: {dumpPath}"); return 2; }
            if (!File.Exists(goldenPath)) { Console.Error.WriteLine($"Không thấy golden: {goldenPath}"); return 2; }

            if (!Enum.TryParse(positionName, true, out MCG_3DPanel.Models.PanelPosition position))
            {
                Console.Error.WriteLine($"Panel position không hợp lệ: {positionName}");
                return 2;
            }

            var parts = PartsDumpReader.Read(dumpPath, out string blockName);
            var result = new MCG_3DPanel.Services.ClassificationService()
                .ClassifyParts(parts, position, blockName);

            var golden = GoldenCsv.Read(goldenPath);
            var diffs = Comparer.Compare(result, golden);

            Console.WriteLine($"Panel   : {blockName} ({positionName})");
            Console.WriteLine($"Part    : {parts.Count} dump -> {result.Parts.Count} phân loại");
            Console.WriteLine($"Summary : {result.Summary()}");

            if (diffs.Count == 0)
            {
                Console.WriteLine($"CHECK PASS — khớp toàn bộ {golden.Count} handle trong golden.");
                return 0;
            }

            Console.Error.WriteLine($"CHECK FAIL — {diffs.Count} sai lệch:");
            foreach (string d in diffs) Console.Error.WriteLine("  " + d);
            return 1;
        }
```

Thêm `using System.Collections.Generic;` vào đầu `Program.cs` nếu chưa có.

- [ ] **Step 8: Tạo golden thật cho 7D-17C**

1. `deploy.bat`, NETLOAD trong AutoCAD.
2. `MCG_Build3DPanel` → chọn đúng **Panel position** của 7D-17C → **Classify Panel** → pick block.
3. Sang tab **Result**, rà từng dòng. Dòng nào sai thì điền loại đúng vào cột **Correct type** (dùng đúng chữ mà `ResultRow.Label` sinh ra: `topplate`, `web6`, `web10`, `flange`, `faceplate`, `flatbar`, `hp`, `OB`, `OB1`, `IB`, `BF`, `GE-HP`, `GE-FB`, `cutout`, `closed plate`, `backing plate`).
4. Bấm **Export** → lưu CSV.
5. Chép vào repo:

```bash
cp ~/Desktop/MCG_PanelResult_*.csv samples/7D-17C/golden.csv
```

- [ ] **Step 9: Chạy check thật — bài test cuối của plan**

Chạy (đổi `Center` cho đúng position của panel):
```bash
./tools/PanelReplay/bin/Release/net48/PanelReplay.exe check samples/7D-17C/parts.json samples/7D-17C/golden.csv Center
```

Kỳ vọng một trong hai:
- `CHECK PASS` → harness chạy đúng, phân loại hiện tại khớp golden.
- `CHECK FAIL` kèm danh sách sai lệch → **đây cũng là thành công của Task này**: harness vừa bắt được đúng những chỗ kỹ sư đã sửa tay. Ghi lại danh sách; nó chính là backlog cho Đợt 1/Đợt 4.

Chỉ coi là hỏng nếu: exit code 2, exception, hoặc số handle lệch nhau bất thường (vd 0 handle khớp) — khi đó nghi `OwnerKey`/filter block sai, kiểm lại Task 3.

- [ ] **Step 10: Viết tài liệu dùng**

Tạo `docs/regression-testing.md`:

```markdown
# Kiểm chứng hồi quy phân loại panel

Mỗi panel đã kiểm bằng mắt một lần → thành test chạy lại được vô hạn lần, KHÔNG cần mở AutoCAD.

## Thêm 1 panel vào bộ test

1. Trong AutoCAD: `MCG_DumpParts` → pick block panel → lưu `samples/<tên-panel>/parts.json`.
2. `MCG_Build3DPanel` → Classify → tab **Result** → sửa cột **Correct type** ở dòng sai →
   **Export** → lưu `samples/<tên-panel>/golden.csv`.
3. Commit cả 2 file.

## Chạy test

```bash
dotnet build -c Release
dotnet build tools/PanelReplay/PanelReplay.csproj -c Release

# Test nội bộ của runner
./tools/PanelReplay/bin/Release/net48/PanelReplay.exe selftest

# Kiểm 1 panel (position: Center | Port | Starboard)
./tools/PanelReplay/bin/Release/net48/PanelReplay.exe check \
    samples/7D-17C/parts.json samples/7D-17C/golden.csv Center
```

Exit code: `0` = pass · `1` = có sai lệch · `2` = lỗi tham số/file.

## Chạy tất cả panel

```bash
for d in samples/*/; do
  [ -f "$d/parts.json" ] && [ -f "$d/golden.csv" ] || continue
  echo "=== $d ==="
  ./tools/PanelReplay/bin/Release/net48/PanelReplay.exe check "$d/parts.json" "$d/golden.csv" Center
done
```

## Sau khi đổi ngưỡng nhận diện

Đổi bất kỳ hằng số nào trong `Services/Classify/ClassifyUtil.cs` hay `PlateDetector.cs` →
build lại → chạy `check` cho **mọi** panel trong `samples/`. Ngưỡng vừa sửa cho panel mới mà
làm hỏng panel cũ sẽ hiện ra ngay, thay vì im lặng như trước.

## Ghi chú kỹ thuật

Runner chạy ngoài AutoCAD nhưng vẫn nạp `AcDbMgd.dll` (kiểu hình học) — nó gọi `SetDllDirectory`
tới `C:\Program Files\Autodesk\AutoCAD 2023` để tìm native dep. Máy cài AutoCAD ở đường dẫn khác
phải sửa hằng `AcadDir` trong `tools/PanelReplay/Program.cs`.

Runner tham chiếu thẳng `MCG_3DPanel.dll` và gọi `ClassificationService.ClassifyParts` — **không
nhân bản logic phân loại**. Vì vậy `dotnet build -c Release` (plugin) phải chạy TRƯỚC khi build runner.
```

- [ ] **Step 11: Commit**

```bash
git add tools/PanelReplay/PartsDumpReader.cs tools/PanelReplay/GoldenCsv.cs \
        tools/PanelReplay/Comparer.cs tools/PanelReplay/SelfTest.cs tools/PanelReplay/Program.cs \
        samples/7D-17C/golden.csv docs/regression-testing.md
git commit -m "feat: add golden-file regression check to PanelReplay runner"
```

---

## Sau khi xong plan này

Bộ harness mở đường cho các việc còn lại của review:

1. **Thêm 7D-09C vào `samples/`** — panel đầu tiên từng dùng để chỉnh ngưỡng. Có nó mới phát hiện được ngưỡng chỉnh cho 7D-17C có phá 7D-09C không.
2. **Đợt 1 (sửa lỗi BF bracket + `GE_MIN_THK`)** — giờ sửa xong chạy `check` là biết ngay có phá gì không.
3. **Đợt 3 (gom `SpecConstants`)** — refactor thuần, `check` bảo chứng không đổi hành vi.

Thứ tự này cố ý: harness làm **trước** khi sửa lỗi, để bản thân bản sửa cũng được kiểm chứng.
