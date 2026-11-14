using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsComposeTests
    {
        private string _folder;
        private ScriptGraphAsset _source;
        private Scene _scene;
        private ScriptMachine _owner;
        private GameObject _reference;

        [SetUp]
        public void SetUp()
        {
            _folder = "Assets/__VsComposeTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", _folder.Substring("Assets/".Length));
            var harness = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(harness.path)) EditorSceneManager.SaveScene(harness, _folder + "/Harness.unity");
            _scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            _source = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            _source.graph = new FlowGraph();
            var literal = new Literal(typeof(bool), true);
            var branch = new If();
            _source.graph.units.Add(literal);
            _source.graph.units.Add(branch);
            literal.Define();
            branch.Define();
            _source.graph.valueConnections.Add(new ValueConnection(literal.output, branch.condition));
            AssetDatabase.CreateAsset(_source, _folder + "/Source.asset");
            AssetDatabase.SaveAssetIfDirty(_source);
            var owner = new GameObject("Owner");
            SceneManager.MoveGameObjectToScene(owner, _scene);
            _owner = owner.AddComponent<ScriptMachine>();
            _owner.nest.source = GraphSource.Macro;
            _owner.nest.macro = _source;
            _reference = new GameObject("Reference");
            SceneManager.MoveGameObjectToScene(_reference, _scene);
            EditorSceneManager.SaveScene(_scene, _folder + "/Scene.unity");
        }

        [TearDown]
        public void TearDown()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                foreach (var root in _scene.GetRootGameObjects())
                    foreach (var component in root.GetComponentsInChildren<Component>(true))
                        if (component != null) Undo.ClearUndo(component);
                EditorSceneManager.CloseScene(_scene, true);
            }
            AssetDatabase.DeleteAsset(_folder);
        }

        private JObject CloneRequest(string policy = "share_macros", Object source = null, string file = "Copy.asset") => new JObject
        {
            ["target"] = VsComposeSupport.Id(source != null ? source : _source),
            ["destination"] = _folder + "/" + file, ["macro_policy"] = policy
        };

        private JObject BindRequest(string kind, Object reference, string name = null, Object owner = null)
        {
            var request = new JObject { ["target"] = VsComposeSupport.Id(owner != null ? owner : _owner), ["kind"] = kind,
                ["reference"] = reference == null ? JValue.CreateNull() : new JValue(VsComposeSupport.Id(reference)) };
            if (name != null) request["variable_name"] = name;
            return request;
        }

        private static JObject Apply(JObject request, bool clone)
        {
            var preview = clone ? VsCloneGraph.HandleCommand(request) : VsBindObject.HandleCommand(request);
            Success(preview);
            request["expected_revision"] = preview["data"]["result"]["revision"].DeepClone();
            request["dry_run"] = false;
            var applied = clone ? VsCloneGraph.HandleCommand(request) : VsBindObject.HandleCommand(request);
            Success(applied);
            return applied;
        }

        private static void Success(JObject response) => Assert.That((bool)response["success"], Is.True, response.ToString());
        private static string Error(JObject response) => (string)response["data"]["error"]["code"];

        [Test]
        public void ClonePreviewDoesNotWriteAndCopyPreservesInternalGuids()
        {
            var request = CloneRequest();
            var revision = VsGraphEditService.Revision(_source.graph);
            var dirty = EditorUtility.IsDirty(_source);
            Success(VsCloneGraph.HandleCommand(request));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(_folder + "/Copy.asset"), Is.Null);
            var response = Apply(request, true);
            var copy = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(_folder + "/Copy.asset");
            Assert.That((string)response["data"]["result"]["new_target"], Is.Not.EqualTo(VsComposeSupport.Id(_source)));
            Assert.That(copy.graph.elements.Select(element => element.guid), Is.EquivalentTo(_source.graph.elements.Select(element => element.guid)));
            Assert.That(copy.graph.valueConnections.Single().destination.key, Is.EqualTo("condition"));
            copy.graph.units.OfType<Literal>().Single().value = false;
            Assert.That((bool)_source.graph.units.OfType<Literal>().Single().value, Is.True);
            Assert.That(VsGraphEditService.Revision(_source.graph), Is.EqualTo(revision));
            Assert.That(EditorUtility.IsDirty(_source), Is.EqualTo(dirty));
        }

        [TestCase("share_macros")]
        [TestCase("embed_macros")]
        public void NestedMacroPolicyIsExplicitAndSourceRemainsUnchanged(string policy)
        {
            var parent = CreateParent();
            var original = VsGraphEditService.Revision(parent.graph);
            Apply(CloneRequest(policy, parent), true);
            var copy = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(_folder + "/Copy.asset");
            var nested = copy.graph.units.OfType<SubgraphUnit>().Single();
            Assert.That(nested.nest.source, Is.EqualTo(policy == "share_macros" ? GraphSource.Macro : GraphSource.Embed));
            _source.graph.units.OfType<Literal>().Single().value = false;
            Assert.That((bool)nested.nest.graph.units.OfType<Literal>().Single().value, Is.EqualTo(policy == "embed_macros"));
            Assert.That(VsGraphEditService.Revision(parent.graph), Is.EqualTo(original));
        }

        private ScriptGraphAsset CreateParent()
        {
            var parent = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            parent.graph = new FlowGraph();
            var subgraph = new SubgraphUnit();
            subgraph.nest.source = GraphSource.Macro;
            subgraph.nest.macro = _source;
            parent.graph.units.Add(subgraph);
            subgraph.Define();
            AssetDatabase.CreateAsset(parent, _folder + "/Parent.asset");
            AssetDatabase.SaveAssetIfDirty(parent);
            return parent;
        }

        [Test]
        public void IndependentCloneRevisionIncludesMacroDependencies()
        {
            var request = CloneRequest("embed_macros", CreateParent());
            var preview = VsCloneGraph.HandleCommand(request);
            Success(preview);
            request["expected_revision"] = preview["data"]["result"]["revision"].DeepClone();
            request["dry_run"] = false;
            _source.graph.variables.Set("external", 42);
            Assert.That(Error(VsCloneGraph.HandleCommand(request)), Is.EqualTo("revision_conflict"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(_folder + "/Copy.asset"), Is.Null);
        }

        [Test]
        public void IndependentCloneRejectsMacroCyclesWithoutCreatingAssets()
        {
            var recursive = new SubgraphUnit();
            recursive.nest.source = GraphSource.Macro;
            recursive.nest.macro = _source;
            _source.graph.units.Add(recursive);
            Assert.That(Error(VsCloneGraph.HandleCommand(CloneRequest("embed_macros"))), Is.EqualTo("graph_cycle"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(_folder + "/Copy.asset"), Is.Null);
        }

        [Test]
        public void CloneRejectsSceneReferencesAndExistingDestinations()
        {
            Assert.That(Error(VsCloneGraph.HandleCommand(CloneRequest(file: "Source.asset"))), Is.EqualTo("destination_exists"));
            _source.graph.variables.Set("SceneReference", _reference);
            Assert.That(Error(VsCloneGraph.HandleCommand(CloneRequest())), Is.EqualTo("cross_context_reference"));
            Assert.That(AssetDatabase.LoadMainAssetAtPath(_folder + "/Copy.asset"), Is.Null);
        }

        [Test]
        public void GraphAssignmentSupportsUndoAndRejectsWrongGraphType()
        {
            Apply(CloneRequest(), true);
            var copy = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(_folder + "/Copy.asset");
            Apply(BindRequest("graph", copy), false);
            Assert.That(_owner.nest.macro, Is.SameAs(copy));
            Undo.PerformUndo();
            Assert.That(_owner.nest.macro, Is.SameAs(_source));
            Undo.PerformRedo();
            Assert.That(_owner.nest.macro, Is.SameAs(copy));
            var state = ScriptableObject.CreateInstance<StateGraphAsset>();
            AssetDatabase.CreateAsset(state, _folder + "/State.asset");
            Assert.That(Error(VsBindObject.HandleCommand(BindRequest("graph", state))), Is.EqualTo("type_mismatch"));
        }

        [Test]
        public void VariableBindingPreviewClearAndUndoPreserveComponents()
        {
            var variables = _owner.GetComponent<Variables>();
            var componentCount = _owner.GetComponents<Component>().Length;
            var request = BindRequest("variable", _reference, "Dependency");
            Success(VsBindObject.HandleCommand(request));
            Assert.That(variables.declarations.IsDefined("Dependency"), Is.False);
            Apply(request, false);
            Assert.That(variables.declarations.Get("Dependency"), Is.SameAs(_reference));
            Undo.PerformUndo();
            Assert.That(variables.declarations.IsDefined("Dependency"), Is.False);
            Undo.PerformRedo();
            Assert.That(variables.declarations.Get("Dependency"), Is.SameAs(_reference));
            Apply(BindRequest("variable", null, "Dependency"), false);
            Assert.That(variables.declarations.Get("Dependency"), Is.Null);
            Assert.That(_owner.GetComponents<Component>().Length, Is.EqualTo(componentCount));
        }

        [Test]
        public void VariableRevisionDetectsInterveningEditsAndTypeMismatch()
        {
            var variables = _owner.GetComponent<Variables>();
            var request = BindRequest("variable", _reference, "Dependency");
            var preview = VsBindObject.HandleCommand(request);
            Success(preview);
            request["expected_revision"] = preview["data"]["result"]["revision"].DeepClone();
            request["dry_run"] = false;
            variables.declarations.Set("Other", 1);
            Assert.That(Error(VsBindObject.HandleCommand(request)), Is.EqualTo("revision_conflict"));
            Assert.That(Error(VsBindObject.HandleCommand(BindRequest("variable", _reference, "Other"))), Is.EqualTo("type_mismatch"));
            variables.declarations.Set("Typed", null);
            variables.declarations.GetDeclaration("Typed").typeHandle = new SerializableType(typeof(Transform).AssemblyQualifiedName);
            Assert.That(Error(VsBindObject.HandleCommand(BindRequest("variable", _reference, "Typed"))), Is.EqualTo("type_mismatch"));
        }

        [Test]
        public void CrossSceneAndSceneToPrefabReferencesAreRejected()
        {
            var otherScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            try
            {
                var other = new GameObject("Other Scene");
                SceneManager.MoveGameObjectToScene(other, otherScene);
                EditorSceneManager.SaveScene(otherScene, _folder + "/Other.unity");
                Assert.That(Error(VsBindObject.HandleCommand(BindRequest("variable", other, "Dependency"))), Is.EqualTo("cross_context_reference"));
                var prefab = PrefabUtility.SaveAsPrefabAsset(_owner.gameObject, _folder + "/Owner.prefab");
                Assert.That(Error(VsBindObject.HandleCommand(BindRequest("variable", _reference, "Dependency", prefab.GetComponent<ScriptMachine>()))), Is.EqualTo("cross_context_reference"));
            }
            finally { EditorSceneManager.CloseScene(otherScene, true); }
        }

        [Test]
        public void PrefabAssetBindingsPersistAndUndoRestoresGraph()
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(_owner.gameObject, _folder + "/Owner.prefab");
            var machine = prefab.GetComponent<ScriptMachine>();
            var variables = prefab.GetComponent<Variables>();
            try
            {
                Apply(CloneRequest(), true);
                var copy = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(_folder + "/Copy.asset");
                Apply(BindRequest("graph", copy, owner: machine), false);
                Undo.PerformUndo();
                Assert.That(machine.nest.macro, Is.SameAs(_source));
                Undo.PerformRedo();
                Assert.That(machine.nest.macro, Is.SameAs(copy));
                Apply(BindRequest("variable", prefab, "Self", machine), false);
                PrefabUtility.SavePrefabAsset(prefab);
                var contents = PrefabUtility.LoadPrefabContents(_folder + "/Owner.prefab");
                try
                {
                    Assert.That(contents.GetComponent<ScriptMachine>().nest.macro, Is.SameAs(copy));
                    Assert.That(contents.GetComponent<Variables>().declarations.Get("Self"), Is.SameAs(contents));
                }
                finally { PrefabUtility.UnloadPrefabContents(contents); }
            }
            finally { Undo.ClearUndo(machine); Undo.ClearUndo(variables); }
        }

        [Test]
        public void ScenePrefabVariableBindingRecordsOverridesAndSurvivesReload()
        {
            var prefab = PrefabUtility.SaveAsPrefabAsset(_owner.gameObject, _folder + "/Owner.prefab");
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, _scene);
            instance.name = "Instance";
            EditorSceneManager.SaveScene(_scene);
            var machine = instance.GetComponent<ScriptMachine>();
            Apply(BindRequest("variable", _reference, "Dependency", machine), false);
            Assert.That(PrefabUtility.GetPropertyModifications(instance).Any(modification => modification.propertyPath.Contains("_data")), Is.True);
            Assert.That(prefab.GetComponent<Variables>().declarations.IsDefined("Dependency"), Is.False);
            EditorSceneManager.SaveScene(_scene);
            Undo.ClearUndo(instance.GetComponent<Variables>());
            EditorSceneManager.CloseScene(_scene, true);
            _scene = EditorSceneManager.OpenScene(_folder + "/Scene.unity", OpenSceneMode.Additive);
            var loaded = _scene.GetRootGameObjects().Single(root => root.name == "Instance");
            var reference = _scene.GetRootGameObjects().Single(root => root.name == "Reference");
            Assert.That(loaded.GetComponent<Variables>().declarations.Get("Dependency"), Is.SameAs(reference));
        }

        [Test]
        public void ApplyRequiresPreviewRevisionAndTraversalIsRejected()
        {
            var request = CloneRequest();
            request["dry_run"] = false;
            Assert.That(Error(VsCloneGraph.HandleCommand(request)), Is.EqualTo("invalid_arguments"));
            request = CloneRequest(file: "../Escaped.asset");
            Assert.That(Error(VsCloneGraph.HandleCommand(request)), Is.EqualTo("invalid_arguments"));
            var bind = BindRequest("variable", _reference, "Dependency");
            bind["DryRun"] = false;
            Assert.That(Error(VsBindObject.HandleCommand(bind)), Is.EqualTo("invalid_arguments"));
        }
    }
}
