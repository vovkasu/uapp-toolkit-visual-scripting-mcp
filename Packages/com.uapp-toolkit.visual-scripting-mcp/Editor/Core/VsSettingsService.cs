using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsSettingsService
    {
        private static readonly Dictionary<string, string> MetadataNames = new Dictionary<string, string>
        {
            ["aot_safe_mode"] = nameof(BoltCoreConfiguration.aotSafeMode),
            ["assemblies"] = nameof(BoltCoreConfiguration.assemblyOptions),
            ["types"] = nameof(BoltCoreConfiguration.typeOptions)
        };

        internal static JObject Snapshot()
        {
            if (!PluginContainer.initialized || BoltCore.instance == null)
                throw new VsGraphException("visual_scripting_not_initialized", "Open a Visual Scripting graph to initialize the package, wait for setup to finish, then retry. Reading settings does not initialize Visual Scripting or generate nodes.");
            var configuration = BoltCore.Configuration;
            return new JObject
            {
                ["aot_safe_mode"] = configuration.aotSafeMode,
                ["assemblies"] = new JArray(configuration.assemblyOptions.Select(value => value.ToString())),
                ["types"] = new JArray(configuration.typeOptions.Select(value => value?.AssemblyQualifiedName))
            };
        }

        public static JObject Read()
        {
            var settings = Snapshot();
            return new JObject { ["settings"] = settings, ["revision"] = VsSettingsPlan.Revision(settings),
                ["scope"] = "project", ["storage"] = "ProjectSettings/VisualScriptingSettings.asset",
                ["regeneration"] = VsNodeRegeneration.Read(), ["undo_scope"] = "current_domain_settings_only" };
        }

        public static JObject Update(JObject input)
        {
            var before = Snapshot();
            var after = VsSettingsPlan.Prepare(before, input);
            var diff = Diff(before, after);
            var dryRun = (bool)input["dry_run"];
            var regenerate = (bool)input["regenerate_nodes"];
            if (!dryRun)
            {
                VsNodeRegeneration.CheckAvailable();
                if (diff.Count > 0) VsSettingsUndo.Commit(before, after);
                if (regenerate) VsNodeRegeneration.Schedule(VsSettingsPlan.Revision(after));
            }
            var warnings = new JArray();
            foreach (var typeName in after["types"].Values<string>())
            {
                var type = typeName == null ? null : Type.GetType(typeName, false);
                if (type == null || !after["assemblies"].Values<string>().Contains(type.Assembly.GetName().Name))
                    warnings.Add("Type is unresolved or its assembly is not explicitly in the node library: " + typeName);
            }
            return new JObject { ["dry_run"] = dryRun, ["revision"] = VsSettingsPlan.Revision(before),
                ["result_revision"] = VsSettingsPlan.Revision(after), ["diff"] = diff, ["applied"] = !dryRun && diff.Count > 0,
                ["save_status"] = !dryRun && diff.Count > 0 ? "saved" : "not_requested",
                ["regenerate_nodes"] = regenerate, ["regeneration"] = VsNodeRegeneration.Read(),
                ["warnings"] = warnings, ["undo_scope"] = "current_domain_settings_only" };
        }

        internal static JObject Diff(JObject before, JObject after)
        {
            var diff = new JObject();
            foreach (var property in before.Properties())
                if (!JToken.DeepEquals(property.Value, after[property.Name]))
                    diff[property.Name] = new JObject { ["before"] = property.Value.DeepClone(), ["after"] = after[property.Name].DeepClone() };
            return diff;
        }

        internal static void Apply(JObject expected, JObject desired)
        {
            var diff = Diff(expected, desired);
            var current = Snapshot();
            foreach (var property in diff.Properties())
                if (!JToken.DeepEquals(current[property.Name], expected[property.Name]))
                    throw new VsGraphException("revision_conflict", "An affected setting changed outside this operation; no settings were overwritten.");
            // Prepare all typed values before touching package metadata.
            var values = new Dictionary<string, object>();
            foreach (var property in diff.Properties()) values[property.Name] = TypedValue(property.Name, desired[property.Name]);
            try { Write(values); }
            catch
            {
                Write(values.Keys.ToDictionary(key => key, key => TypedValue(key, expected[key])));
                throw;
            }
        }

        private static object TypedValue(string key, JToken value)
        {
            if (key == "aot_safe_mode") return (bool)value;
            if (key == "assemblies") return value.Values<string>().Select(name => new LooseAssemblyName(name)).ToList();
            var types = value.Values<string>().Select(name => name == null ? null : Type.GetType(name, false)).ToList();
            if (types.Any(type => type == null)) throw new VsGraphException("unsupported_type", "Existing type list contains unresolved entries; repair it explicitly before changing the type list.");
            return types;
        }

        private static void Write(Dictionary<string, object> values)
        {
            if (values.Count == 0) return;
            var configuration = BoltCore.Configuration;
            VsSettingsStorage.CheckAvailable();
            foreach (var pair in values)
            {
                var metadata = configuration.GetMetadata(MetadataNames[pair.Key]);
                metadata.value = pair.Value;
                metadata.Save();
            }
            // Metadata.Save also reloads a backing asset unloaded by Unity.
            VsSettingsStorage.Save(VsSettingsStorage.Resolve(configuration),
                values.Keys.Select(key => configuration.GetMetadata(MetadataNames[key]).key));
            Codebase.UpdateSettings();
        }
    }
}
