using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Unity.VisualScripting;
using UnityEditorInternal;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsSettingsStorage
    {
        // VS exposes metadata publicly, but keeps its live backing asset internal.
        // Save that existing asset so unknown keys and object references survive.
        private static readonly PropertyInfo AssetProperty = typeof(PluginConfiguration).GetProperty(
            "projectSettingsAsset", BindingFlags.Instance | BindingFlags.NonPublic);

        internal static void CheckAvailable()
        {
            if (AssetProperty == null || AssetProperty.PropertyType != typeof(DictionaryAsset) ||
                AssetProperty.GetGetMethod(true) == null)
                throw new VsGraphException("settings_storage_unavailable", "The Visual Scripting settings storage API is unavailable; no settings were changed.");
        }

        internal static DictionaryAsset Resolve(PluginConfiguration configuration)
        {
            CheckAvailable();
            if (!(AssetProperty.GetValue(configuration) is DictionaryAsset asset) || asset == null)
                throw new VsGraphException("settings_storage_unavailable", "The Visual Scripting settings backing asset is unavailable.");
            return asset;
        }

        internal static void Save(DictionaryAsset asset, IEnumerable<string> changedKeys)
        {
            // SaveProjectSettingsAsset(true) still uses delayCall in VS 1.9.11.
            // A background GUI Editor can service MCP without draining that queue.
            var path = PluginPaths.projectSettings;
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            InternalEditorUtility.SaveToSerializedFileAndForget(new Object[] { asset }, path, true);

            var loaded = InternalEditorUtility.LoadSerializedFileAndForget(path);
            try
            {
                if (loaded == null || loaded.Length != 1 || !(loaded[0] is DictionaryAsset saved))
                    throw new VsGraphException("settings_save_failed", "The saved Visual Scripting settings could not be read back.");
                foreach (var key in changedKeys)
                    if (!saved.TryGetValue(key, out var actual) || !Equal(asset[key], actual))
                        throw new VsGraphException("settings_save_failed", "A saved Visual Scripting setting did not match its applied value.");
            }
            finally
            {
                if (loaded != null)
                    foreach (var item in loaded)
                        if (item != null && item != asset) Object.DestroyImmediate(item);
            }
        }

        private static bool Equal(object expected, object actual)
        {
            if (expected is IEnumerable left && actual is IEnumerable right)
                return left.Cast<object>().SequenceEqual(right.Cast<object>());
            return Equals(expected, actual);
        }
    }
}
