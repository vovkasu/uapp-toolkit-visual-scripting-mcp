using System;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsObjectBindingService
    {
        private const string UndoName = "Bind Visual Scripting object via MCP";

        public static JObject Execute(JObject input)
        {
            var owner = VsComposeSupport.Resolve(VsEditValues.Text(input, "target")) as MonoBehaviour;
            if (!(owner is IMachine machine)) throw new VsGraphException("unsupported_context", "Target must be a ScriptMachine or StateMachine component.");
            CheckOwner(owner);
            if (!input.ContainsKey("reference")) throw new ArgumentException("reference is required; use explicit null to clear a variable.");
            var reference = input["reference"].Type == JTokenType.Null ? null : VsComposeSupport.Resolve(VsEditValues.Text(input, "reference"));
            VsComposeSupport.CheckReference(owner, reference);
            var kind = VsEditValues.Text(input, "kind");
            Object changedObject;
            Action apply;
            Action restore;
            JToken before;
            string state;
            var changed = true;
            if (kind == "graph")
            {
                if (input.ContainsKey("variable_name")) throw new ArgumentException("variable_name is only valid for variable binding.");
                if (!(reference is IMacro macro) || !EditorUtility.IsPersistent(reference) || !machine.nest.macroType.IsInstanceOfType(reference))
                    throw new VsGraphException("type_mismatch", "Graph reference must be a persistent macro matching the machine graph type.");
                var source = machine.nest.source;
                var oldMacro = machine.nest.macro;
                before = new JObject { ["source"] = source.ToString(), ["macro"] = VsComposeSupport.Describe(oldMacro as Object),
                    ["embedded_graph_preserved"] = machine.nest.embed != null };
                state = VsComposeSupport.SerializedFingerprint(machine.nest);
                changed = source != GraphSource.Macro || !ReferenceEquals(oldMacro, macro);
                changedObject = owner;
                apply = () => { machine.nest.macro = macro; machine.nest.source = GraphSource.Macro; };
                restore = () => { machine.nest.macro = oldMacro; machine.nest.source = source; };
            }
            else if (kind == "variable")
            {
                var name = VsEditValues.Text(input, "variable_name");
                if (!owner.TryGetComponent<Variables>(out var variables)) throw new VsGraphException("not_found", "Existing Variables component is required; none was added.");
                var declarations = variables.declarations;
                var exists = declarations.IsDefined(name);
                var previous = exists ? declarations.Get(name) : null;
                CheckVariableType(exists ? declarations.GetDeclaration(name) : null, reference);
                var snapshot = new VsGraphSnapshot();
                before = new JObject { ["exists"] = exists, ["value"] = previous is Object obj ? (JToken)VsComposeSupport.Describe(obj) : VsGraphValues.Read(previous, snapshot) };
                state = VsComposeSupport.SerializedFingerprint(declarations);
                var backup = declarations.Serialize();
                changed = !exists || !ReferenceEquals(previous, reference);
                changedObject = variables;
                apply = () => declarations.Set(name, reference);
                restore = () => RestoreDeclarations(declarations, (VariableDeclarations)backup.Deserialize());
            }
            else throw new ArgumentException("kind must be graph or variable.");

            var revision = VsComposeSupport.Fingerprint(input, state, reference == null ? "null" : reference.GetType().AssemblyQualifiedName);
            VsComposeSupport.CheckRevision(input, revision);
            var result = new JObject { ["target"] = input["target"].DeepClone(), ["kind"] = kind,
                ["variable_name"] = input["variable_name"]?.DeepClone(), ["dry_run"] = input["dry_run"].DeepClone(),
                ["revision"] = revision, ["before"] = before, ["after"] = VsComposeSupport.Describe(reference),
                ["changed"] = changed, ["applied"] = !(bool)input["dry_run"] && changed, ["saved"] = false,
                ["graph_assignment_source"] = kind == "graph" ? "Macro" : null, ["undo_name"] = UndoName };
            if (!(bool)input["dry_run"] && changed) Commit(owner, changedObject, apply, restore);
            return VsResponse.Success(result);
        }

        private static void CheckOwner(MonoBehaviour owner)
        {
            if (!(owner is ScriptMachine) && !(owner is StateMachine)) throw new VsGraphException("unsupported_context", "Only built-in ScriptMachine and StateMachine owners are supported.");
            if (EditorUtility.IsPersistent(owner))
            {
                var path = AssetDatabase.GetAssetPath(owner);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsOpenForEdit(owner)) throw new VsGraphException("read_only", "Prefab must be editable inside Assets.");
                if (PrefabUtility.GetPrefabAssetType(owner) != PrefabAssetType.Regular || PrefabUtility.IsPartOfPrefabInstance(owner))
                    throw new VsGraphException("unsupported_context", "Prefab model, variant and nested instance asset targets are not supported.");
            }
            else if (!owner.gameObject.scene.IsValid() || !owner.gameObject.scene.isLoaded || string.IsNullOrEmpty(owner.gameObject.scene.path) ||
                PrefabStageUtility.GetPrefabStage(owner.gameObject) != null)
                throw new VsGraphException("unsupported_context", "Owner must be in a saved loaded regular scene; Prefab Stage is not supported.");
        }

        private static void CheckVariableType(VariableDeclaration declaration, Object reference)
        {
            if (declaration == null) return;
            if (declaration.value != null && !(declaration.value is Object)) throw new VsGraphException("type_mismatch", "Object binding cannot overwrite a scalar variable.");
            var name = declaration.typeHandle.Identification;
            if (string.IsNullOrEmpty(name)) return;
            var type = Type.GetType(name, false);
            if (type == null) RuntimeCodebase.TryDeserializeType(name, out type);
            if (type == null || (!typeof(Object).IsAssignableFrom(type) && type != typeof(object)) || (reference != null && !type.IsInstanceOfType(reference)))
                throw new VsGraphException("type_mismatch", "Reference does not match the variable's declared object type.");
        }

        private static void RestoreDeclarations(VariableDeclarations destination, VariableDeclarations backup)
        {
            destination.Clear();
            foreach (var variable in backup)
            {
                destination.Set(variable.name, variable.value);
                destination.GetDeclaration(variable.name).typeHandle = variable.typeHandle;
            }
        }

        private static void Commit(Component owner, Object target, Action apply, Action restore)
        {
            var wasDirty = EditorUtility.IsDirty(target);
            ((ISerializationCallbackReceiver)target).OnBeforeSerialize();
            Undo.IncrementCurrentGroup();
            var group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoName);
            try
            {
                Undo.RegisterCompleteObjectUndo(target, UndoName);
                apply();
                ((ISerializationCallbackReceiver)target).OnBeforeSerialize();
                if (PrefabUtility.IsPartOfPrefabInstance(target)) PrefabUtility.RecordPrefabInstancePropertyModifications(target);
                EditorUtility.SetDirty(target);
                if (!EditorUtility.IsPersistent(owner)) EditorSceneManager.MarkSceneDirty(owner.gameObject.scene);
                Undo.CollapseUndoOperations(group);
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                restore();
                ((ISerializationCallbackReceiver)target).OnBeforeSerialize();
                if (!wasDirty) EditorUtility.ClearDirty(target);
                throw;
            }
            finally { Undo.IncrementCurrentGroup(); }
        }
    }
}
