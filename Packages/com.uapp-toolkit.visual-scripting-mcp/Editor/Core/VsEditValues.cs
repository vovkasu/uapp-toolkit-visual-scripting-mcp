using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsEditValues
    {
        public static object Read(JToken token)
        {
            if (!(token is JObject value)) throw new ArgumentException("Values require {type,value}.");
            Keys(value, "type", "value");
            var type = Text(value, "type");
            var data = value["value"];
            if (data == null) throw new ArgumentException("value is required.");
            switch (type)
            {
                case "null": if (data.Type == JTokenType.Null) return null; break;
                case "string": if (data.Type == JTokenType.String && ((string)data).Length <= 4096) return (string)data; break;
                case "bool": if (data.Type == JTokenType.Boolean) return (bool)data; break;
                case "int":
                    if (data.Type == JTokenType.Integer && int.TryParse(data.ToString(), out var integer)) return integer;
                    break;
                case "float":
                    if (IsNumber(data) && float.TryParse(data.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var single) && !float.IsNaN(single) && !float.IsInfinity(single)) return single;
                    break;
                case "double":
                    if (IsNumber(data) && double.TryParse(data.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var number) && !double.IsNaN(number) && !double.IsInfinity(number)) return number;
                    break;
            }
            throw new ArgumentException("Unsupported or mismatched typed value. Supported: null, string, bool, int, float, double.");
        }

        public static float Number(JObject input, string key)
        {
            return (float)Read(new JObject { ["type"] = "float", ["value"] = input[key]?.DeepClone() });
        }

        private static bool IsNumber(JToken token) => token.Type == JTokenType.Integer || token.Type == JTokenType.Float;

        public static string Text(JObject input, string key)
        {
            if (input[key]?.Type != JTokenType.String || string.IsNullOrEmpty((string)input[key]) || ((string)input[key]).Length > 2048)
                throw new ArgumentException(key + " must be a nonempty string up to 2048 characters.");
            return (string)input[key];
        }

        public static Guid GuidValue(JObject input, string key)
        {
            if (!Guid.TryParse(Text(input, key), out var guid) || guid == Guid.Empty) throw new ArgumentException("Invalid element GUID.");
            return guid;
        }

        public static void Keys(JObject input, params string[] allowed)
        {
            foreach (var property in input.Properties())
                if (Array.IndexOf(allowed, property.Name) < 0) throw new ArgumentException("Unknown operation field: " + property.Name);
        }
    }
}
