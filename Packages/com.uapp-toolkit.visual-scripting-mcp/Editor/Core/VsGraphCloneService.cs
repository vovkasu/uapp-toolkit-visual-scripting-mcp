using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsGraphCloneService
    {
        private const int MaximumDepth = 8;
        private const int MaximumElements = 20000;
        private const int MaximumGraphs = 256;
        private const int MaximumGraphCharacters = 2 * 1024 * 1024;

        public static JObject Execute(JObject input)
        {
            var graphRequest = new JObject { ["target"] = input["target"].DeepClone() };
            if (input["parent_element_guids"] != null) graphRequest["parent_element_guids"] = input["parent_element_guids"].DeepClone();
            var request = VsGraphRequest.Parse(graphRequest, true);
            var root = VsGraphCatalog.Resolve(request.Target);
            var source = root.childGraph;
            foreach (var token in request.ParentGuids)
            {
                var parent = source?.elements.FirstOrDefault(element => element.guid.ToString() == (string)token) as IGraphParentElement;
                if (parent == null) throw new VsGraphException("not_found", "Selected graph path was not found.");
                source = parent.childGraph;
            }
            var destination = VsEditValues.Text(input, "destination");
            CheckDestination(destination);
            var policy = VsEditValues.Text(input, "macro_policy");
            if (policy != "share_macros" && policy != "embed_macros") throw new ArgumentException("macro_policy must be share_macros or embed_macros.");
            var budget = new CopyBudget();
            var copy = Copy(source, policy, new HashSet<IGraph>(), budget, 0);
            var revision = VsComposeSupport.Fingerprint(input, VsGraphEditService.Revision(root.childGraph), VsGraphEditService.Revision(copy));
            VsComposeSupport.CheckRevision(input, revision);
            var result = new JObject { ["dry_run"] = input["dry_run"].DeepClone(), ["revision"] = revision,
                ["source"] = request.Target, ["destination"] = destination, ["macro_policy"] = policy,
                ["graph_type"] = copy.GetType().FullName, ["copied_graphs"] = budget.Graphs,
                ["copied_elements"] = budget.Elements, ["shared_macros"] = new JArray(budget.Shared.OrderBy(id => id)),
                ["new_target"] = null, ["saved"] = false, ["asset_creation_undo_supported"] = false,
                ["non_graph_asset_references"] = "preserved", ["validation"] = "structure_preserved_not_execution_validated" };
            if ((bool)input["dry_run"]) return VsResponse.Success(result);

            // Only the new asset is saved; the source and all referenced assets remain untouched.
            var asset = copy is FlowGraph ? (ScriptableObject)ScriptableObject.CreateInstance<ScriptGraphAsset>() : ScriptableObject.CreateInstance<StateGraphAsset>();
            var created = false;
            try
            {
                ((IMacro)asset).graph = copy;
                asset.name = Path.GetFileNameWithoutExtension(destination);
                CheckDestination(destination);
                AssetDatabase.CreateAsset(asset, destination);
                created = true;
                AssetDatabase.SaveAssetIfDirty(asset);
                result["new_target"] = VsComposeSupport.Id(asset);
                result["edit_revision"] = VsGraphEditService.Revision(copy);
                result["saved"] = true;
                return VsResponse.Success(result);
            }
            catch
            {
                if (created && AssetDatabase.LoadAssetAtPath<ScriptableObject>(destination) == asset) AssetDatabase.DeleteAsset(destination);
                else if (!EditorUtility.IsPersistent(asset)) Object.DestroyImmediate(asset);
                throw;
            }
        }

        private static void CheckDestination(string path)
        {
            if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !path.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                path.Contains('\\') || path.Split('/').Any(part => string.IsNullOrEmpty(part) || part == "." || part == "..") ||
                !AssetDatabase.IsValidFolder(Path.GetDirectoryName(path)?.Replace('\\', '/')))
                throw new ArgumentException("destination must be a .asset path in an existing Assets folder, without traversal.");
            if (File.Exists(path) || File.Exists(path + ".meta") || Directory.Exists(path) || AssetDatabase.LoadMainAssetAtPath(path) != null)
                throw new VsGraphException("destination_exists", "Destination or its meta already exists; no overwrite is allowed.");
        }

        private static IGraph Copy(IGraph source, string policy, HashSet<IGraph> ancestors, CopyBudget budget, int depth)
        {
            if (!(source is FlowGraph) && !(source is StateGraph)) throw new VsGraphException("unsupported_graph", "A Flow or State graph is required.");
            if (depth > MaximumDepth || ++budget.Graphs > MaximumGraphs || (budget.Elements += source.elements.Count) > MaximumElements)
                throw new VsGraphException("clone_limit", "Graph copy exceeds depth, graph count or element limits.");
            if (!ancestors.Add(source)) throw new VsGraphException("graph_cycle", "Independent copying cannot traverse cyclic graph nesting.");
            try
            {
                var data = source.Serialize();
                if (data.json.Length > MaximumGraphCharacters) throw new VsGraphException("clone_limit", "Graph serialization exceeds 2M characters.");
                foreach (var reference in data.objectReferences)
                    if (!ReferenceEquals(reference, null) && (reference == null || !EditorUtility.IsPersistent(reference)))
                        throw new VsGraphException("cross_context_reference", "A graph asset copy cannot contain scene, temporary or destroyed object references.");
                var copy = (IGraph)data.Deserialize();
                if (VsGraphEditService.Revision(source) != VsGraphEditService.Revision(copy))
                    throw new VsGraphException("clone_not_lossless", "VS serialization changed the source structure during copying.");
                foreach (var element in source.elements)
                {
                    if (!(element is IGraphNester sourceParent)) continue;
                    var copyParent = copy.elements.First(candidate => candidate.guid == element.guid) as IGraphNester;
                    if (sourceParent.nest.source == GraphSource.Macro && policy == "share_macros")
                    {
                        if (sourceParent.nest.macro is Object macro) budget.Shared.Add(VsComposeSupport.Id(macro));
                        continue;
                    }
                    if (sourceParent.nest.graph == null) throw new VsGraphException("missing_graph", "Nested graph is missing.");
                    var child = Copy(sourceParent.nest.graph, policy, ancestors, budget, depth + 1);
                    copyParent.nest.embed = child;
                    copyParent.nest.source = GraphSource.Embed;
                    if (policy == "embed_macros") copyParent.nest.macro = null;
                }
                // Nest callbacks must not silently replace or remove connection/node GUIDs.
                if (!new HashSet<Guid>(source.elements.Select(element => element.guid)).SetEquals(copy.elements.Select(element => element.guid)))
                    throw new VsGraphException("clone_not_lossless", "Nesting conversion changed element GUIDs.");
                return copy;
            }
            finally { ancestors.Remove(source); }
        }

        private sealed class CopyBudget
        {
            public int Graphs { get; set; }
            public int Elements { get; set; }
            public HashSet<string> Shared { get; } = new HashSet<string>(StringComparer.Ordinal);
        }
    }
}
