using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Harvestline.Core.Save
{
    /// <summary>
    /// A minimal, dependency-free JSON tree. The spec mandates JSON saves with zero
    /// third-party runtime dependencies (spec §8), and Unity's built-in JsonUtility
    /// cannot round-trip dictionaries or polymorphic lists cleanly — so the save layer
    /// owns a small, deterministic serializer instead. Numbers are written with the
    /// invariant culture and round-trip ("R") formatting.
    /// </summary>
    public abstract class JsonValue
    {
        public static JsonObject Object() => new JsonObject();
        public static JsonArray Array() => new JsonArray();
        public static JsonValue Of(string s) => new JsonString(s);
        public static JsonValue Of(double d) => new JsonNumber(d);
        public static JsonValue Of(long l) => new JsonNumber(l);
        public static JsonValue Of(bool b) => new JsonBool(b);

        public string ToJsonString(bool indent = false)
        {
            var sb = new StringBuilder();
            Write(sb, indent, 0);
            return sb.ToString();
        }

        internal abstract void Write(StringBuilder sb, bool indent, int depth);

        public static JsonValue Parse(string text)
        {
            int pos = 0;
            var v = ParseValue(text, ref pos);
            SkipWhitespace(text, ref pos);
            if (pos != text.Length) throw new FormatException($"Trailing characters at {pos}.");
            return v;
        }

        // ---- parsing ----
        private static JsonValue ParseValue(string s, ref int pos)
        {
            SkipWhitespace(s, ref pos);
            if (pos >= s.Length) throw new FormatException("Unexpected end of input.");
            char c = s[pos];
            switch (c)
            {
                case '{': return ParseObject(s, ref pos);
                case '[': return ParseArray(s, ref pos);
                case '"': return new JsonString(ParseString(s, ref pos));
                case 't':
                case 'f': return ParseBool(s, ref pos);
                case 'n': Expect(s, ref pos, "null"); return JsonNull.Instance;
                default: return ParseNumber(s, ref pos);
            }
        }

        private static JsonObject ParseObject(string s, ref int pos)
        {
            var obj = new JsonObject();
            pos++; // {
            SkipWhitespace(s, ref pos);
            if (s[pos] == '}') { pos++; return obj; }
            while (true)
            {
                SkipWhitespace(s, ref pos);
                string key = ParseString(s, ref pos);
                SkipWhitespace(s, ref pos);
                if (s[pos] != ':') throw new FormatException($"Expected ':' at {pos}.");
                pos++;
                obj[key] = ParseValue(s, ref pos);
                SkipWhitespace(s, ref pos);
                char c = s[pos++];
                if (c == '}') break;
                if (c != ',') throw new FormatException($"Expected ',' or '}}' at {pos - 1}.");
            }
            return obj;
        }

        private static JsonArray ParseArray(string s, ref int pos)
        {
            var arr = new JsonArray();
            pos++; // [
            SkipWhitespace(s, ref pos);
            if (s[pos] == ']') { pos++; return arr; }
            while (true)
            {
                arr.Add(ParseValue(s, ref pos));
                SkipWhitespace(s, ref pos);
                char c = s[pos++];
                if (c == ']') break;
                if (c != ',') throw new FormatException($"Expected ',' or ']' at {pos - 1}.");
            }
            return arr;
        }

        private static string ParseString(string s, ref int pos)
        {
            if (s[pos] != '"') throw new FormatException($"Expected string at {pos}.");
            pos++;
            var sb = new StringBuilder();
            while (true)
            {
                char c = s[pos++];
                if (c == '"') break;
                if (c == '\\')
                {
                    char e = s[pos++];
                    switch (e)
                    {
                        case '"': sb.Append('"'); break;
                        case '\\': sb.Append('\\'); break;
                        case '/': sb.Append('/'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'u':
                            int code = int.Parse(s.Substring(pos, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                            sb.Append((char)code);
                            pos += 4;
                            break;
                        default: throw new FormatException($"Bad escape '\\{e}' at {pos - 1}.");
                    }
                }
                else sb.Append(c);
            }
            return sb.ToString();
        }

        private static JsonValue ParseNumber(string s, ref int pos)
        {
            int start = pos;
            while (pos < s.Length && "+-0123456789.eE".IndexOf(s[pos]) >= 0) pos++;
            string num = s.Substring(start, pos - start);
            if (num.Length == 0) throw new FormatException($"Invalid number at {start}.");
            return new JsonNumber(double.Parse(num, CultureInfo.InvariantCulture));
        }

        private static JsonValue ParseBool(string s, ref int pos)
        {
            if (s[pos] == 't') { Expect(s, ref pos, "true"); return new JsonBool(true); }
            Expect(s, ref pos, "false");
            return new JsonBool(false);
        }

        private static void Expect(string s, ref int pos, string literal)
        {
            if (pos + literal.Length > s.Length || s.Substring(pos, literal.Length) != literal)
                throw new FormatException($"Expected '{literal}' at {pos}.");
            pos += literal.Length;
        }

        private static void SkipWhitespace(string s, ref int pos)
        {
            while (pos < s.Length && char.IsWhiteSpace(s[pos])) pos++;
        }

        internal static void WriteEscaped(StringBuilder sb, string s)
        {
            sb.Append('"');
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\b': sb.Append("\\b"); break;
                    case '\f': sb.Append("\\f"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < ' ') sb.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
        }
    }

    public sealed class JsonObject : JsonValue
    {
        // Insertion-ordered so serialization is deterministic.
        private readonly List<string> _keys = new List<string>();
        private readonly Dictionary<string, JsonValue> _map = new Dictionary<string, JsonValue>(StringComparer.Ordinal);

        public JsonValue this[string key]
        {
            get => _map[key];
            set { if (!_map.ContainsKey(key)) _keys.Add(key); _map[key] = value; }
        }

        public bool Has(string key) => _map.ContainsKey(key);
        public IReadOnlyList<string> Keys => _keys;

        public string GetString(string key) => ((JsonString)_map[key]).Value;
        public double GetDouble(string key) => ((JsonNumber)_map[key]).Value;
        public long GetLong(string key) => (long)((JsonNumber)_map[key]).Value;
        public int GetInt(string key) => (int)((JsonNumber)_map[key]).Value;
        public ulong GetULong(string key) => ulong.Parse(((JsonString)_map[key]).Value, CultureInfo.InvariantCulture);
        public bool GetBool(string key) => ((JsonBool)_map[key]).Value;
        public JsonObject GetObject(string key) => (JsonObject)_map[key];
        public JsonArray GetArray(string key) => (JsonArray)_map[key];

        public JsonObject Set(string key, JsonValue value) { this[key] = value; return this; }

        internal override void Write(StringBuilder sb, bool indent, int depth)
        {
            sb.Append('{');
            for (int i = 0; i < _keys.Count; i++)
            {
                if (i > 0) sb.Append(',');
                NewLine(sb, indent, depth + 1);
                WriteEscaped(sb, _keys[i]);
                sb.Append(':');
                if (indent) sb.Append(' ');
                _map[_keys[i]].Write(sb, indent, depth + 1);
            }
            if (_keys.Count > 0) NewLine(sb, indent, depth);
            sb.Append('}');
        }

        private static void NewLine(StringBuilder sb, bool indent, int depth)
        {
            if (!indent) return;
            sb.Append('\n');
            for (int i = 0; i < depth; i++) sb.Append("  ");
        }
    }

    public sealed class JsonArray : JsonValue
    {
        private readonly List<JsonValue> _items = new List<JsonValue>();
        public IReadOnlyList<JsonValue> Items => _items;
        public int Count => _items.Count;
        public JsonValue this[int i] => _items[i];
        public JsonArray Add(JsonValue v) { _items.Add(v); return this; }

        internal override void Write(StringBuilder sb, bool indent, int depth)
        {
            sb.Append('[');
            for (int i = 0; i < _items.Count; i++)
            {
                if (i > 0) sb.Append(',');
                if (indent) { sb.Append('\n'); for (int k = 0; k < depth + 1; k++) sb.Append("  "); }
                _items[i].Write(sb, indent, depth + 1);
            }
            if (indent && _items.Count > 0) { sb.Append('\n'); for (int k = 0; k < depth; k++) sb.Append("  "); }
            sb.Append(']');
        }
    }

    public sealed class JsonString : JsonValue
    {
        public string Value { get; }
        public JsonString(string v) => Value = v;
        internal override void Write(StringBuilder sb, bool indent, int depth) => WriteEscaped(sb, Value);
    }

    public sealed class JsonNumber : JsonValue
    {
        public double Value { get; }
        public JsonNumber(double v) => Value = v;
        internal override void Write(StringBuilder sb, bool indent, int depth) =>
            sb.Append(Value.ToString("R", CultureInfo.InvariantCulture));
    }

    public sealed class JsonBool : JsonValue
    {
        public bool Value { get; }
        public JsonBool(bool v) => Value = v;
        internal override void Write(StringBuilder sb, bool indent, int depth) => sb.Append(Value ? "true" : "false");
    }

    public sealed class JsonNull : JsonValue
    {
        public static readonly JsonNull Instance = new JsonNull();
        private JsonNull() { }
        internal override void Write(StringBuilder sb, bool indent, int depth) => sb.Append("null");
    }
}
