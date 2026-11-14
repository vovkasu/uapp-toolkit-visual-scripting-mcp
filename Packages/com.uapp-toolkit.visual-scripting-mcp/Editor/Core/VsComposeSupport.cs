using System;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsComposeSupport
    {
        public static JObject Parse(JObject parameters, bool clone)
        {
            const int maximumRequestCharacters = 8192;
            if (parameters == null || parameters.ToString(Formatting.None).Length > maximumRequestCharacters)
                throw new ArgumentException("A composition request up to 8K characters is required.");
            var result = new JObject();
            foreach (var property in parameters.Properties())
            {
                var key = property.Name;
                switch (key)
                {
                    case "Target": key = "target"; break;
                    case "ParentElementGuids": key = "parent_element_guids"; break;
                    case "Destination": key = "destination"; break;
                    case "MacroPolicy": key = "macro_policy"; break;
                    case "Kind": key = "kind"; break;
                    case "Reference": key = "reference"; break;
                    case "VariableName": key = "variable_name"; break;
                    case "DryRun": case "dryRun": key = "dry_run"; break;
                    case "ExpectedRevision": key = "expected_revision"; break;
                }
                var common = key == "target" || key == "dry_run" || key == "expected_revision";
                var specific = clone ? key == "parent_element_guids" || key == "destination" || key == "macro_policy"
                    : key == "kind" || key == "reference" || key == "variable_name";
                if ((!common && !specific) || result.ContainsKey(key)) throw new ArgumentException("Unknown or duplicate composition parameter.");
                result[key] = property.Value.DeepClone();
            }
            VsEditValues.Text(result, "target");
            if (result["dry_run"] != null && result["dry_run"].Type != JTokenType.Boolean) throw new ArgumentException("dry_run must be boolean.");
            if (result["dry_run"] == null) result["dry_run"] = true;
            if (result["expected_revision"] != null && (result["expected_revision"].Type != JTokenType.String || ((string)result["expected_revision"]).Length != 64))
                throw new ArgumentException("expected_revision must be a preview revision.");
            if (!(bool)result["dry_run"] && result["expected_revision"] == null)
                throw new ArgumentException("Apply requires expected_revision from this command preview.");
            return result;
        }

        public static string Fingerprint(JObject input, params string[] state)
        {
            var query = (JObject)input.DeepClone();
            query.Remove("dry_run");
            query.Remove("expected_revision");
            // Fixed query field order makes aliases and caller JSON property order immaterial.
            var ordered = new JObject();
            foreach (var key in new[] { "target", "parent_element_guids", "destination", "macro_policy", "kind", "reference", "variable_name" })
                if (query[key] != null) ordered[key] = query[key].DeepClone();
            var text = new StringBuilder(ordered.ToString(Formatting.None));
            foreach (var item in state) text.Append('\n').Append(item);
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", string.Empty);
        }

        public static string SerializedFingerprint(object value)
        {
            const int maximumCharacters = 2 * 1024 * 1024;
            var data = value.Serialize();
            if (data.json.Length > maximumCharacters) throw new VsGraphException("composition_limit", "Binding state exceeds 2M serialized characters.");
            var text = new StringBuilder(data.json);
            foreach (var reference in data.objectReferences)
                text.Append('|').Append(reference == null ? "null" : Id(reference));
            return text.ToString();
        }

        public static void CheckRevision(JObject input, string revision)
        {
            if (input["expected_revision"] != null && (string)input["expected_revision"] != revision)
                throw new VsGraphException("revision_conflict", "Source, destination, binding or request changed. Repeat preview.");
        }

        public static string Id(Object value) => GlobalObjectId.GetGlobalObjectIdSlow(value).ToString();

        public static Object Resolve(string target)
        {
            if (!GlobalObjectId.TryParse(target, out var id) || id.targetObjectId == 0) throw new ArgumentException("A persistent GlobalObjectId is required.");
            var path = AssetDatabase.GUIDToAssetPath(id.assetGUID.ToString());
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded) throw new VsGraphException("not_found", "Reference scene must already be loaded.");
            }
            var value = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(id);
            if (value == null) throw new VsGraphException("not_found", "Referenced object was not found.");
            return value;
        }

        public static GameObject GameObjectOf(Object value) => value is Component component ? component.gameObject : value as GameObject;

        public static JObject Describe(Object value) => value == null ? null : new JObject
        {
            ["target"] = Id(value), ["type"] = value.GetType().FullName, ["name"] = value.name,
            ["asset_path"] = AssetDatabase.GetAssetPath(value), ["scene_path"] = GameObjectOf(value)?.scene.path
        };

        public static void CheckReference(Component owner, Object reference)
        {
            if (reference == null) return;
            if (EditorUtility.IsPersistent(reference)) return;
            var referenceObject = GameObjectOf(reference);
            if (EditorUtility.IsPersistent(owner)) throw new VsGraphException("cross_context_reference", "A prefab asset cannot reference a scene object.");
            if (referenceObject == null || !referenceObject.scene.IsValid() || !referenceObject.scene.isLoaded ||
                referenceObject.scene != owner.gameObject.scene || PrefabStageUtility.GetPrefabStage(referenceObject) != null)
                throw new VsGraphException("cross_context_reference", "Only persistent assets or objects in the owner's loaded scene are allowed.");
        }
    }
}
