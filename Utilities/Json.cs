using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace MCG_3DPanel.Utilities
{
    /// <summary>
    /// Object JSON giữ nguyên THỨ TỰ key theo lúc thêm. Dùng: new JsonObject { {"k", v}, ... }
    /// </summary>
    public class JsonObject : List<KeyValuePair<string, object>>
    {
        public void Add(string key, object value) => Add(new KeyValuePair<string, object>(key, value));
    }

    /// <summary>
    /// Serializer JSON tối giản (không phụ thuộc NuGet), đủ dùng để xuất dữ liệu inspect.
    /// Hỗ trợ: null, bool, số, string, IDictionary&lt;string,object&gt;, IEnumerable.
    /// Dùng IDictionary để giữ THỨ TỰ key => dùng chung với listExtension OrderedDictionary-like.
    /// </summary>
    public static class JsonWriter
    {
        public static string Serialize(object value)
        {
            var sb = new StringBuilder();
            Write(sb, value, 0);
            return sb.ToString();
        }

        private static void Write(StringBuilder sb, object value, int indent)
        {
            switch (value)
            {
                case null:
                    sb.Append("null");
                    break;
                case bool b:
                    sb.Append(b ? "true" : "false");
                    break;
                case string s:
                    WriteString(sb, s);
                    break;
                case double d:
                    sb.Append(d.ToString("0.####", CultureInfo.InvariantCulture));
                    break;
                case float f:
                    sb.Append(f.ToString("0.####", CultureInfo.InvariantCulture));
                    break;
                case int i:
                    sb.Append(i.ToString(CultureInfo.InvariantCulture));
                    break;
                case long l:
                    sb.Append(l.ToString(CultureInfo.InvariantCulture));
                    break;
                case JsonObject jo:
                    WriteObject(sb, jo, indent);
                    break;
                case IDictionary<string, object> map:
                    WriteObject(sb, new List<KeyValuePair<string, object>>(map), indent);
                    break;
                case IEnumerable list:
                    WriteArray(sb, list, indent);
                    break;
                default:
                    WriteString(sb, value.ToString());
                    break;
            }
        }

        private static void WriteObject(StringBuilder sb, IList<KeyValuePair<string, object>> map, int indent)
        {
            if (map.Count == 0) { sb.Append("{}"); return; }
            sb.Append("{\n");
            string pad = new string(' ', (indent + 1) * 2);
            for (int n = 0; n < map.Count; n++)
            {
                sb.Append(pad);
                WriteString(sb, map[n].Key);
                sb.Append(": ");
                Write(sb, map[n].Value, indent + 1);
                if (n < map.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(new string(' ', indent * 2)).Append('}');
        }

        private static void WriteArray(StringBuilder sb, IEnumerable list, int indent)
        {
            var items = new List<object>();
            foreach (var it in list) items.Add(it);
            if (items.Count == 0) { sb.Append("[]"); return; }
            sb.Append("[\n");
            string pad = new string(' ', (indent + 1) * 2);
            for (int k = 0; k < items.Count; k++)
            {
                sb.Append(pad);
                Write(sb, items[k], indent + 1);
                if (k < items.Count - 1) sb.Append(',');
                sb.Append('\n');
            }
            sb.Append(new string(' ', indent * 2)).Append(']');
        }

        private static void WriteString(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }
}
