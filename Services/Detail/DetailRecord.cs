using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MCG_3DPanel.Services.Detail
{
    // ⚠️ File THUẦN C# — KHÔNG using Autodesk.* (link sang tests/MCG_3DPanel.Tests).

    public enum DetailKind { Plan, Section, View }

    /// <summary>Bản ghi JSON gắn (XData) vào mỗi block/entity Detail do tool tạo, để Update Details dựng lại.
    /// Việc cắt JSON thành chunk 255 ký tự thuộc lớp XData, không phải ở đây.</summary>
    public sealed class DetailRecord
    {
        static readonly JsonSerializerOptions Opt = new JsonSerializerOptions
        {
            IncludeFields = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public DetailKind Kind;
        public string Panel = "";
        public int Number;
        public string Letter = "";            // chỉ Section
        public string AssyRefHandle = "";
        public string BoundaryHandle = "";    // biên AM_7 trên plan (Plan/View)
        public List<double[]> Region = new List<double[]>();   // góc chữ nhật hoặc [cx,cy,r]
        public bool Circle;
        public double[] CutA, CutB, Side;     // chỉ Section, toạ độ plan
        public double Scale = 1;
        public string Title = "";
        public double BaseX, BaseY;           // điểm gốc block trên plan

        public string ToJson() => JsonSerializer.Serialize(this, Opt);

        /// <summary>Dấu XData ngắn cho từng entity trong block Detail (bản ghi đầy đủ nằm trên block / reference / tiêu đề).</summary>
        public string Marker() =>
            $"{Kind} {Panel} DET.{Number}" + (Kind == DetailKind.Section ? $" {Number}{Letter}" : "");

        /// <summary>JSON rỗng/hỏng/thiếu Kind hoặc Panel/Section thiếu CutA-CutB-Side ⇒ null, không throw.</summary>
        public static DetailRecord FromJson(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            try
            {
                // Kind thiếu thì enum về Plan(0) im lặng ⇒ phải kiểm tra riêng.
                using (var doc = JsonDocument.Parse(s))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Object
                        || !doc.RootElement.TryGetProperty("Kind", out var k)
                        || k.ValueKind != JsonValueKind.String) return null;
                }
                var r = JsonSerializer.Deserialize<DetailRecord>(s, Opt);
                if (r == null || !Enum.IsDefined(typeof(DetailKind), r.Kind) || string.IsNullOrEmpty(r.Panel)) return null;
                r.Letter = r.Letter ?? "";
                r.AssyRefHandle = r.AssyRefHandle ?? "";
                r.BoundaryHandle = r.BoundaryHandle ?? "";
                r.Title = r.Title ?? "";
                r.Region = r.Region ?? new List<double[]>();
                if (r.Region.Contains(null)) return null;
                if (r.Kind == DetailKind.Section
                    && (r.CutA?.Length != 2 || r.CutB?.Length != 2 || r.Side?.Length != 2)) return null;
                return r;
            }
            catch (JsonException) { return null; }
        }
    }
}
