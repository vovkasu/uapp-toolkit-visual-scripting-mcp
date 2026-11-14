using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsGraphCatalog
    {
        public static VsGraphSnapshot List(VsGraphRequest request)
        {
            var snapshot = new VsGraphSnapshot();
            if (request.Scope == "loaded_scenes")
            {
                for (var index = 0; index < SceneManager.sceneCount; index++)
                {
                    var scene = SceneManager.GetSceneAt(index);
                    if (!scene.isLoaded) continue;
                    if (string.IsNullOrEmpty(scene.path)) { snapshot.Warn("unsaved_scene_skipped"); continue; }
                    foreach (var root in scene.GetRootGameObjects())
                        AddMachines(root, "scene", snapshot);
                }
                return snapshot;
            }
            if (!AssetDatabase.IsValidFolder(request.Folder) ||
                (request.Folder != "Assets" && !request.Folder.StartsWith("Assets/", StringComparison.Ordinal)))
                throw new ArgumentException("folder must be an existing folder inside Assets.");

            var ids = new SortedSet<string>(StringComparer.Ordinal);
            if (request.Scope == "prefabs")
                ids.UnionWith(AssetDatabase.FindAssets("t:Prefab", new[] { request.Folder }));
            else
            {
                ids.UnionWith(AssetDatabase.FindAssets("t:ScriptGraphAsset", new[] { request.Folder }));
                ids.UnionWith(AssetDatabase.FindAssets("t:StateGraphAsset", new[] { request.Folder }));
            }
            foreach (var id in ids)
            {
                if (snapshot.IsFull) { snapshot.Warn("record_limit_reached"); break; }
                var path = AssetDatabase.GUIDToAssetPath(id);
                if (request.Scope == "prefabs")
                {
                    var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab != null) AddMachines(prefab, "prefab", snapshot);
                    continue;
                }
                foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
                    if (asset is IGraphRoot graphRoot) snapshot.Add(Describe(asset, graphRoot, "asset", snapshot));
            }
            return snapshot;
        }

        private static void AddMachines(GameObject root, string context, VsGraphSnapshot snapshot)
        {
            // Dynamic discovery is the purpose of this Editor tool; no runtime polling.
            foreach (var component in root.GetComponentsInChildren<MonoBehaviour>(true))
            {
                if (snapshot.IsFull) { snapshot.Warn("record_limit_reached"); return; }
                if (component is IMachine machine)
                    snapshot.Add(Describe(component, machine, context, snapshot));
            }
        }

        private static JObject Describe(Object owner, IGraphRoot root, string context, VsGraphSnapshot snapshot)
        {
            var identity = GlobalObjectId.GetGlobalObjectIdSlow(owner);
            var target = identity.ToString();
            if (identity.targetObjectId == 0) snapshot.Warn("owner_without_persistent_id");
            var graph = root.childGraph;
            var record = new JObject
            {
                ["kind"] = "graph_owner", ["target"] = target, ["context_kind"] = context,
                ["asset_guid"] = identity.assetGUID.ToString(), ["local_file_id"] = identity.targetObjectId.ToString(),
                ["asset_path"] = AssetDatabase.GUIDToAssetPath(identity.assetGUID.ToString()),
                ["owner_name"] = owner.name, ["graph_type"] = graph?.GetType().FullName,
                ["title"] = graph?.title, ["element_count"] = graph?.elements.Count ?? 0
            };
            if (root is IGraphNester nester)
            {
                record["source"] = nester.nest.source.ToString();
                record["macro_target"] = nester.nest.macro is Object macro ? GlobalObjectId.GetGlobalObjectIdSlow(macro).ToString() : null;
            }
            return record;
        }

        public static IGraphRoot Resolve(string target)
        {
            if (!GlobalObjectId.TryParse(target, out var identity))
                throw new ArgumentException("target must be a GlobalObjectId returned by vs_list_graphs.");
            var path = AssetDatabase.GUIDToAssetPath(identity.assetGUID.ToString());
            if (path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                var scene = SceneManager.GetSceneByPath(path);
                if (!scene.IsValid() || !scene.isLoaded)
                    throw new VsGraphException("not_found", "The owner scene is not loaded; no scene was opened.");
            }
            var owner = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(identity);
            if (!(owner is IGraphRoot root))
                throw new VsGraphException("not_found", "Graph asset or machine owner was not found.");
            return root;
        }
    }
}
