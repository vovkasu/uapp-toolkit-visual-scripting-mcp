using System;
using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal sealed class VsGraphRequest
    {
        public const int DefaultPageSize = 50;
        public const int MaximumPageSize = 200;
        public const int DefaultDepth = 2;
        public const int MaximumDepth = 8;
        public string Scope { get; private set; }
        public string Folder { get; private set; }
        public string Target { get; private set; }
        public string Cursor { get; private set; }
        public JArray ParentGuids { get; private set; }
        public int PageSize { get; private set; }
        public int MaxDepth { get; private set; }

        public static VsGraphRequest Parse(JObject parameters, bool read)
        {
            var input = new JObject();
            foreach (var property in (parameters ?? new JObject()).Properties())
            {
                var name = property.Name;
                switch (name)
                {
                    case "Scope": name = "scope"; break;
                    case "Folder": name = "folder"; break;
                    case "Target": name = "target"; break;
                    case "Cursor": name = "cursor"; break;
                    case "PageSize": name = "page_size"; break;
                    case "MaxDepth": name = "max_depth"; break;
                    case "ParentElementGuids": name = "parent_element_guids"; break;
                }
                if (input.ContainsKey(name)) throw new ArgumentException("Duplicate parameter alias.");
                input[name] = property.Value.DeepClone();
            }
            foreach (var property in input.Properties())
            {
                var allowed = property.Name == "page_size" || property.Name == "cursor" ||
                    (read ? property.Name == "target" || property.Name == "parent_element_guids" || property.Name == "max_depth"
                        : property.Name == "scope" || property.Name == "folder");
                if (!allowed)
                    throw new ArgumentException("Unknown parameter.");
            }

            var request = new VsGraphRequest
            {
                PageSize = ReadInteger(input, "page_size", DefaultPageSize, 1, MaximumPageSize),
                MaxDepth = ReadInteger(input, "max_depth", DefaultDepth, 0, MaximumDepth),
                Cursor = ReadString(input, "cursor", null),
                Target = ReadString(input, "target", null),
                Scope = ReadString(input, "scope", "assets"),
                Folder = ReadString(input, "folder", "Assets"),
                ParentGuids = new JArray()
            };
            if (read && string.IsNullOrWhiteSpace(request.Target))
                throw new ArgumentException("target is required; use an ID returned by vs_list_graphs.");
            if (!read && request.Scope != "assets" && request.Scope != "prefabs" && request.Scope != "loaded_scenes")
                throw new ArgumentException("scope must be assets, prefabs or loaded_scenes.");
            if (input["parent_element_guids"] != null)
            {
                if (!(input["parent_element_guids"] is JArray path) || path.Count > MaximumDepth)
                    throw new ArgumentException("parent_element_guids must be an array of at most 8 GUIDs.");
                foreach (var item in path)
                {
                    if (item.Type != JTokenType.String || !Guid.TryParse((string)item, out var guid))
                        throw new ArgumentException("Invalid parent element GUID.");
                    request.ParentGuids.Add(guid.ToString());
                }
            }
            return request;
        }

        private static string ReadString(JObject input, string name, string fallback)
        {
            var value = input[name];
            if (value == null)
                return fallback;
            const int maximumLength = 2048;
            if (value.Type != JTokenType.String || ((string)value).Length > maximumLength)
                throw new ArgumentException(name + " must be a string of at most 2048 characters.");
            return (string)value;
        }

        private static int ReadInteger(JObject input, string name, int fallback, int minimum, int maximum)
        {
            var value = input[name];
            if (value == null)
                return fallback;
            if (value.Type != JTokenType.Integer || !int.TryParse(value.ToString(), out var number) || number < minimum || number > maximum)
                throw new ArgumentException(name + " is outside the supported integer range.");
            return number;
        }
    }
}
