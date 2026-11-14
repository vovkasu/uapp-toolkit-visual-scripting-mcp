using System;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsRuntimeReader
    {
        public static VsGraphSnapshot Status(VsRuntimeRequest request)
        {
            var snapshot = new VsGraphSnapshot();
            if (request.InstanceId.HasValue)
                AddStatus(Resolve(request.InstanceId.Value), request.PageRequest.ParentGuids, snapshot);
            else
                foreach (var component in Object.FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.InstanceID))
                {
                    if (snapshot.IsFull) { snapshot.Warn("record_limit_reached"); break; }
                    if (component is IMachine && IsRuntime(component)) AddStatus(component, new JArray(), snapshot);
                }
            return snapshot;
        }

        private static void AddStatus(MonoBehaviour component, JArray path, VsGraphSnapshot snapshot)
        {
            var context = Context(component, path);
            var record = Identity(component);
            record["kind"] = "machine_status";
            record["path"] = path.DeepClone();
            record["active_self"] = component.gameObject.activeSelf;
            record["active_in_hierarchy"] = component.gameObject.activeInHierarchy;
            record["enabled"] = component.enabled;
            record["graph_type"] = context.Graph?.GetType().FullName;
            record["graph_title"] = context.Graph?.title;
            record["graph_present"] = context.Graph != null;
            record["runtime_data_present"] = context.Data != null;
            record["is_listening"] = context.Data is IGraphEventListenerData listener ? new JValue(listener.isListening) : JValue.CreateNull();
            var activeStates = new JArray();
            if (context.Graph is StateGraph stateGraph && context.Data != null)
                foreach (var state in stateGraph.states)
                    if (context.Data.TryGetElementData(state, out var data) && data is State.Data stateData && stateData.isActive)
                        activeStates.Add(state.guid.ToString());
            record["active_state_guids"] = activeStates;
            snapshot.Add(record);
        }

        public static VsGraphSnapshot Variables(VsRuntimeRequest request)
        {
            var owner = Resolve(request.InstanceId.Value);
            var context = Context(owner, request.PageRequest.ParentGuids);
            var snapshot = new VsGraphSnapshot();
            if (Includes(request, "graph"))
                AddVariables("graph", context.Data is IGraphDataWithVariables graphData ? graphData.variables : null, owner, request.PageRequest.ParentGuids, snapshot);
            if (Includes(request, "object"))
                AddVariables("object", owner.TryGetComponent<Unity.VisualScripting.Variables>(out var variables) ? variables.declarations : null, owner, new JArray(), snapshot);
            if (Includes(request, "scene"))
            {
                var found = false;
                foreach (var root in owner.gameObject.scene.GetRootGameObjects())
                    foreach (var sceneVariables in root.GetComponentsInChildren<SceneVariables>(true))
                    {
                        found = true;
                        AddVariables("scene", sceneVariables.TryGetComponent<Unity.VisualScripting.Variables>(out var declarations) ? declarations.declarations : null,
                            sceneVariables, new JArray(), snapshot);
                    }
                if (!found) AddVariables("scene", null, owner, new JArray(), snapshot);
            }
            if (Includes(request, "application")) AddVariables("application", ApplicationVariables.runtime, owner, new JArray(), snapshot);
            if (Includes(request, "saved")) AddVariables("saved", SavedVariables.merged, owner, new JArray(), snapshot);
            return snapshot;
        }

        private static bool Includes(VsRuntimeRequest request, string scope) => request.Scope == "all" || request.Scope == scope;

        private static void AddVariables(string scope, VariableDeclarations variables, Component owner, JArray path, VsGraphSnapshot snapshot)
        {
            snapshot.Add(new JObject { ["kind"] = "variable_scope", ["scope"] = scope, ["path"] = path.DeepClone(),
                ["owner_instance_id"] = owner.GetInstanceID(), ["available"] = variables != null,
                ["storage"] = scope == "saved" ? "merged_runtime_memory_not_disk" : "runtime_memory" });
            if (variables == null) return;
            foreach (var variable in variables)
            {
                if (snapshot.IsFull) { snapshot.Warn("record_limit_reached"); break; }
                snapshot.Add(new JObject { ["kind"] = "runtime_variable", ["scope"] = scope,
                    ["path"] = path.DeepClone(), ["owner_instance_id"] = owner.GetInstanceID(),
                    ["name"] = variable.name, ["value"] = Value(variable.value, snapshot) });
            }
        }

        private static JToken Value(object value, VsGraphSnapshot snapshot)
        {
            if (!(value is Object obj)) return VsGraphValues.Read(value, snapshot);
            if (obj == null) return new JObject { ["missing_unity_object"] = true };
            var record = Identity(obj);
            record["persistent_asset"] = EditorUtility.IsPersistent(obj);
            record["asset_path"] = AssetDatabase.GetAssetPath(obj);
            return record;
        }

        private static JObject Identity(Object obj)
        {
            var gameObject = obj as GameObject;
            if (obj is Component component) gameObject = component.gameObject;
            return new JObject { ["instance_id"] = obj.GetInstanceID(), ["play_session_id"] = VsRuntimeSession.Current,
                ["name"] = obj.name, ["type"] = obj.GetType().FullName,
                ["scene_path"] = gameObject != null ? gameObject.scene.path : null,
                ["scene_name"] = gameObject != null ? gameObject.scene.name : null };
        }

        private static bool IsRuntime(MonoBehaviour component) => !EditorUtility.IsPersistent(component) && component.gameObject.scene.IsValid() && component.gameObject.scene.isLoaded;

        internal static MonoBehaviour Resolve(int id)
        {
            var component = EditorUtility.InstanceIDToObject(id) as MonoBehaviour;
            if (component == null || !(component is IMachine) || !IsRuntime(component))
                throw new VsGraphException("stale_reference", "The runtime machine no longer exists or is not a scene instance.");
            return component;
        }

        private static RuntimeContext Context(MonoBehaviour component, JArray path)
        {
            var machine = (IMachine)component;
            var graph = machine.childGraph;
            var data = machine.graphData;
            foreach (var token in path)
            {
                IGraphParentElement parent = null;
                if (graph != null)
                    foreach (var element in graph.elements)
                        if (element.guid.ToString() == (string)token) parent = element as IGraphParentElement;
                if (parent == null) throw new VsGraphException("not_found", "Nested graph path was not found.");
                IGraphData childData = null;
                data?.TryGetChildGraphData(parent, out childData);
                graph = parent.childGraph;
                data = childData;
            }
            return new RuntimeContext { Graph = graph, Data = data };
        }

        private sealed class RuntimeContext
        {
            public IGraph Graph { get; set; }
            public IGraphData Data { get; set; }
        }
    }
}
