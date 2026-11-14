using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsSettingsPlan
    {
        private const int MaximumRequestCharacters = 32768;
        private const int MaximumListChanges = 64;

        public static JObject Parse(JObject input)
        {
            if (input == null || input.ToString(Formatting.None).Length > MaximumRequestCharacters)
                throw new ArgumentException("A settings request up to 32K characters is required.");
            var result = new JObject();
            foreach (var property in input.Properties())
            {
                var key = property.Name;
                switch (key)
                {
                    case "Changes": key = "changes"; break;
                    case "ExpectedRevision": key = "expected_revision"; break;
                    case "DryRun": case "dryRun": key = "dry_run"; break;
                    case "RegenerateNodes": key = "regenerate_nodes"; break;
                }
                if (result.ContainsKey(key) || (key != "changes" && key != "expected_revision" && key != "dry_run" && key != "regenerate_nodes"))
                    throw new ArgumentException("Unknown or duplicate settings parameter.");
                result[key] = property.Value.DeepClone();
            }
            if (!(result["changes"] is JObject)) throw new ArgumentException("changes must be an object.");
            if (VsEditValues.Text(result, "expected_revision").Length != 64) throw new ArgumentException("A settings revision is required.");
            foreach (var key in new[] { "dry_run", "regenerate_nodes" })
            {
                if (result[key] == null) result[key] = key == "dry_run";
                if (result[key].Type != JTokenType.Boolean) throw new ArgumentException("Settings flags must be boolean.");
            }
            return result;
        }

        public static string Revision(JObject settings)
        {
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(settings.ToString(Formatting.None)))).Replace("-", string.Empty);
        }

        public static JObject Prepare(JObject before, JObject input)
        {
            if ((string)input["expected_revision"] != Revision(before)) throw new VsGraphException("revision_conflict", "Settings changed. Read settings and repeat preview.");
            var after = (JObject)before.DeepClone();
            var changes = (JObject)input["changes"];
            foreach (var property in changes.Properties())
            {
                if (property.Name == "aot_safe_mode")
                {
                    if (property.Value.Type != JTokenType.Boolean) throw new ArgumentException("aot_safe_mode must be boolean.");
                    after["aot_safe_mode"] = property.Value.DeepClone();
                }
                else if (property.Name != "add_assemblies" && property.Name != "remove_assemblies" && property.Name != "add_types" && property.Name != "remove_types")
                    throw new ArgumentException("Unsupported project setting.");
            }
            EditList(after, changes, "assemblies", ResolveAssembly);
            EditList(after, changes, "types", ResolveTypeName);
            return after;
        }

        private static void EditList(JObject after, JObject changes, string key, Func<string, string> resolve)
        {
            var add = Strings(changes["add_" + key]);
            var remove = Strings(changes["remove_" + key]);
            if (add.Intersect(remove, StringComparer.Ordinal).Any()) throw new ArgumentException("Cannot add and remove the same entry.");
            var list = after[key].Values<string>().ToList();
            list.RemoveAll(value => remove.Contains(value));
            foreach (var value in add)
            {
                var resolved = resolve(value);
                if (!list.Contains(resolved)) list.Add(resolved);
            }
            after[key] = new JArray(list);
        }

        private static string[] Strings(JToken value)
        {
            if (value == null) return Array.Empty<string>();
            if (!(value is JArray array) || array.Count > MaximumListChanges || array.Any(item => item.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)item)))
                throw new ArgumentException("List changes require up to 64 non-empty strings.");
            var values = array.Values<string>().ToArray();
            if (values.Distinct(StringComparer.Ordinal).Count() != values.Length) throw new ArgumentException("Duplicate list change.");
            return values;
        }

        private static string ResolveAssembly(string name)
        {
            var matches = Codebase.assemblies.Where(assembly => assembly.GetName().Name == name && !assembly.IsDynamic).ToArray();
            if (matches.Length != 1 || Codebase.editorAssemblies.Contains(matches[0]))
                throw new VsGraphException("unsupported_type", "Assembly must be a loaded, unambiguous runtime assembly.");
            return name;
        }

        internal static Type ResolveType(string name)
        {
            var type = Type.GetType(name, false);
            if (type == null || type.AssemblyQualifiedName != name || type == typeof(void) || type.ContainsGenericParameters || type.IsPointer || type.IsByRef ||
                !IsRuntimeType(type))
                throw new VsGraphException("unsupported_type", "Type must be a loaded runtime type with its exact assembly-qualified name, without open generic parameters.");
            return type;
        }

        private static string ResolveTypeName(string name) => ResolveType(name).AssemblyQualifiedName;

        private static bool IsRuntimeType(Type type) => !type.Assembly.IsDynamic && Codebase.assemblies.Contains(type.Assembly) &&
            !Codebase.editorAssemblies.Contains(type.Assembly) && (!type.HasElementType || IsRuntimeType(type.GetElementType())) &&
            (!type.IsGenericType || type.GetGenericArguments().All(IsRuntimeType));
    }
}
