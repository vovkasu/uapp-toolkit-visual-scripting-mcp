using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsGraphEditService
    {
        private const int MaximumGraphCharacters = 2 * 1024 * 1024;
        private const int MaximumDiffBytes = 192 * 1024;
        private const string UndoName = "Edit Visual Scripting graph via MCP";

        public static string Revision(IGraph graph)
        {
            var serialized = graph.Serialize();
            var text = new StringBuilder(serialized.json);
            foreach (var reference in serialized.objectReferences)
                text.Append('|').Append(reference == null ? "null" : GlobalObjectId.GetGlobalObjectIdSlow(reference).ToString());
            using (var hash = SHA256.Create())
                return BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))).Replace("-", string.Empty);
        }

        public static JObject Execute(VsEditRequest request)
        {
            var root = VsGraphCatalog.Resolve(request.Graph.Target);
            var owner = (Object)root;
            CheckOwner(root, owner);
            var revision = Revision(root.childGraph);
            if (!string.Equals(revision, request.ExpectedRevision, StringComparison.Ordinal))
                throw new VsGraphException("revision_conflict", "Graph changed. Read it again and repeat dry run.");
            var serialized = root.childGraph.Serialize();
            if (serialized.json.Length > MaximumGraphCharacters) throw new VsGraphException("edit_limit", "Graph exceeds the 2M-character editing limit.");
            var copy = (IGraph)serialized.Deserialize();
            if (Revision(copy) != revision)
                throw new VsGraphException("clone_not_lossless", "VS serialization changed the graph during cloning; no edit was applied.");
            var original = Select(root.childGraph, request.Graph.ParentGuids);
            var edited = Select(copy, request.Graph.ParentGuids);
            var before = Snapshot(original);
            VsGraphEditOperations.Apply(edited, request.Operations);
            var after = Snapshot(edited);
            var validation = VsGraphValidator.Validate(after, request.Graph.Target);
            var summary = (JObject)validation.Records.First();
            if ((int)summary["error_count"] > 0) throw new VsGraphException("validation_failed", "The resulting selected graph failed structural validation.");
            var diff = Diff(before, after);
            if (Encoding.UTF8.GetByteCount(diff.ToString(Formatting.None)) > MaximumDiffBytes)
                throw new VsGraphException("edit_limit", "Preview exceeds the diff limit; use a smaller operation batch.");
            var nextRevision = Revision(copy);
            var changed = revision != nextRevision;
            var result = new JObject { ["target"] = request.Graph.Target, ["parent_element_guids"] = request.Graph.ParentGuids.DeepClone(),
                ["dry_run"] = request.DryRun, ["applied"] = !request.DryRun && changed, ["saved"] = false,
                ["previous_revision"] = revision, ["edit_revision"] = request.DryRun ? revision : nextRevision,
                ["proposed_revision"] = nextRevision, ["diff"] = diff, ["validation"] = summary.DeepClone(),
                ["validation_scope"] = "selected_flow_graph_only", ["undo_name"] = UndoName };
            if (!request.DryRun && changed) Commit(root, owner, copy, revision);
            var response = VsResponse.Success(result);
            var warnings = (JArray)response["data"]["warnings"];
            foreach (var warning in validation.Warnings) warnings.Add(warning.DeepClone());
            response["data"]["complete"] = (bool)summary["inspection_complete"];
            return response;
        }

        private static void CheckOwner(IGraphRoot root, Object owner)
        {
            if (owner is Component component)
            {
                if (EditorUtility.IsPersistent(owner) || !component.gameObject.scene.IsValid() || !component.gameObject.scene.isLoaded ||
                    PrefabStageUtility.GetPrefabStage(component.gameObject) != null)
                    throw new VsGraphException("unsupported_context", "Edit a graph asset or an embedded machine in a loaded regular scene. Prefab assets/stages are read-only in this version.");
                if (!(root is IGraphNester nester) || nester.nest.source != GraphSource.Embed)
                    throw new VsGraphException("shared_macro", "Target the machine macro asset directly; no implicit shared-asset edits.");
            }
            else if (!(root is IMacro) || !EditorUtility.IsPersistent(owner))
                throw new VsGraphException("unsupported_context", "Only saved graph assets and loaded scene machines are editable.");
            var path = AssetDatabase.GetAssetPath(owner);
            if (!string.IsNullOrEmpty(path) && (!path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsOpenForEdit(owner)))
                throw new VsGraphException("read_only", "The graph asset is not editable inside Assets.");
        }

        private static FlowGraph Select(IGraph graph, JArray path)
        {
            foreach (var token in path)
            {
                var parent = graph?.elements.FirstOrDefault(element => element.guid.ToString() == (string)token) as IGraphParentElement;
                if (parent == null) throw new VsGraphException("not_found", "Embedded graph path was not found.");
                if (!(parent is IGraphNester nester) || nester.nest.source != GraphSource.Embed)
                    throw new VsGraphException("shared_macro", "Nested shared macros must be targeted directly.");
                graph = parent.childGraph;
            }
            return graph as FlowGraph ?? throw new VsGraphException("unsupported_graph", "Select a Flow Graph; State Graph topology edits are not supported.");
        }

        private static VsGraphSnapshot Snapshot(FlowGraph graph)
        {
            var snapshot = new VsGraphSnapshot();
            VsGraphReader.Visit(graph, new JArray(), 0, new HashSet<IGraph>(), snapshot);
            return snapshot;
        }

        private static JArray Diff(VsGraphSnapshot before, VsGraphSnapshot after)
        {
            var oldRecords = before.Records.ToDictionary(Key, token => token);
            var newRecords = after.Records.ToDictionary(Key, token => token);
            var keys = new SortedSet<string>(oldRecords.Keys, StringComparer.Ordinal);
            keys.UnionWith(newRecords.Keys);
            var diff = new JArray();
            foreach (var key in keys)
            {
                oldRecords.TryGetValue(key, out var oldRecord);
                newRecords.TryGetValue(key, out var newRecord);
                if (!JToken.DeepEquals(oldRecord, newRecord))
                    diff.Add(new JObject { ["element"] = key, ["change"] = oldRecord == null ? "added" : newRecord == null ? "removed" : "modified",
                        ["before"] = oldRecord?.DeepClone(), ["after"] = newRecord?.DeepClone() });
            }
            return diff;
        }

        private static string Key(JToken record) => record["kind"] + ":" + (record["guid"] ?? record["name"] ?? record["key"] ?? record["title"]) + ":" + record["scope"];

        private static void Assign(IGraphRoot root, IGraph graph)
        {
            if (root is IMacro macro) macro.graph = graph;
            else ((IGraphNester)root).nest.embed = graph;
        }

        private static void Commit(IGraphRoot root, Object owner, IGraph prepared, string expectedRevision)
        {
            if (Revision(root.childGraph) != expectedRevision) throw new VsGraphException("revision_conflict", "Graph changed during preparation.");
            var original = root.childGraph;
            var wasDirty = EditorUtility.IsDirty(owner);
            var component = owner as Component;
            ((ISerializationCallbackReceiver)owner).OnBeforeSerialize();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            try
            {
                Undo.RegisterCompleteObjectUndo(owner, UndoName);
                Assign(root, prepared);
                ((ISerializationCallbackReceiver)owner).OnBeforeSerialize();
                if (PrefabUtility.IsPartOfPrefabInstance(owner)) PrefabUtility.RecordPrefabInstancePropertyModifications(owner);
                EditorUtility.SetDirty(owner);
                if (component != null) EditorSceneManager.MarkSceneDirty(component.gameObject.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                Assign(root, original);
                ((ISerializationCallbackReceiver)owner).OnBeforeSerialize();
                if (!wasDirty) EditorUtility.ClearDirty(owner);
                throw;
            }
            finally { Undo.IncrementCurrentGroup(); }
        }
    }
}
