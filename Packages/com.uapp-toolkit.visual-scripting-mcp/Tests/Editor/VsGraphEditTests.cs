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
    public class VsGraphEditTests
    {
        private string _folder;
        private ScriptGraphAsset _asset;
        private Guid _literalId;

        [SetUp]
        public void SetUp()
        {
            _folder = "Assets/__VsEditTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", _folder.Substring("Assets/".Length));
            _asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            _asset.graph = new FlowGraph();
            var literal = new Literal(typeof(bool), false);
            _literalId = literal.guid;
            _asset.graph.units.Add(literal);
            literal.Define();
            AssetDatabase.CreateAsset(_asset, _folder + "/Graph.asset");
            AssetDatabase.SaveAssetIfDirty(_asset);
        }

        [TearDown]
        public void TearDown()
        {
            if (_asset != null) Undo.ClearUndo(_asset);
            AssetDatabase.DeleteAsset(_folder);
        }

        private JObject Request(JArray operations, bool dryRun = true, Object owner = null)
        {
            var target = owner != null ? owner : _asset;
            return new JObject { ["target"] = GlobalObjectId.GetGlobalObjectIdSlow(target).ToString(),
                ["expected_revision"] = VsGraphEditService.Revision(((IGraphRoot)target).childGraph),
                ["dry_run"] = dryRun, ["operations"] = operations };
        }

        private JObject SetLiteral(bool value) => new JObject { ["op"] = "configure_node", ["guid"] = _literalId.ToString(),
            ["config"] = new JObject { ["value"] = new JObject { ["type"] = "bool", ["value"] = value } } };

        private static void Success(JObject response) => Assert.That((bool)response["success"], Is.True, response.ToString());

        [Test]
        public void DryRunDoesNotChangeRevisionDirtyStateOrGraph()
        {
            var revision = VsGraphEditService.Revision(_asset.graph);
            var dirty = EditorUtility.IsDirty(_asset);
            var request = Request(new JArray(SetLiteral(true)));
            request.Remove("dry_run");
            var result = VsEditGraph.HandleCommand(request);
            Success(result);
            Assert.That((bool)result["data"]["result"]["dry_run"], Is.True);
            Assert.That(((JArray)result["data"]["warnings"]).Count, Is.Zero);
            Assert.That(((JArray)result["data"]["result"]["diff"]).Count, Is.GreaterThan(0));
            Assert.That(VsGraphEditService.Revision(_asset.graph), Is.EqualTo(revision));
            Assert.That(EditorUtility.IsDirty(_asset), Is.EqualTo(dirty));
        }

        [Test]
        public void ApplyPreservesGuidSupportsUndoRedoAndSurvivesReload()
        {
            Success(VsEditGraph.HandleCommand(Request(new JArray(SetLiteral(true)), false)));
            Assert.That((bool)((Literal)_asset.graph.units.Single()).value, Is.True);
            Assert.That(_asset.graph.units.Single().guid, Is.EqualTo(_literalId));
            Undo.PerformUndo();
            Assert.That((bool)((Literal)_asset.graph.units.Single()).value, Is.False);
            Undo.PerformRedo();
            Assert.That((bool)((Literal)_asset.graph.units.Single()).value, Is.True);
            AssetDatabase.SaveAssetIfDirty(_asset);
            Undo.ClearUndo(_asset);
            Resources.UnloadAsset(_asset);
            _asset = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(_folder + "/Graph.asset");
            Assert.That((bool)((Literal)_asset.graph.units.Single()).value, Is.True);
            Assert.That(_asset.graph.units.Single().guid, Is.EqualTo(_literalId));
        }

        [Test]
        public void LaterInvalidOperationDoesNotApplyEarlierOperation()
        {
            var before = VsGraphEditService.Revision(_asset.graph);
            var dirty = EditorUtility.IsDirty(_asset);
            var response = VsEditGraph.HandleCommand(Request(new JArray(SetLiteral(true),
                new JObject { ["op"] = "remove_node", ["guid"] = Guid.NewGuid().ToString() }), false));
            Assert.That((bool)response["success"], Is.False);
            Assert.That(VsGraphEditService.Revision(_asset.graph), Is.EqualTo(before));
            Assert.That(EditorUtility.IsDirty(_asset), Is.EqualTo(dirty));
        }

        [Test]
        public void StaleRevisionCannotOverwriteInterveningEdits()
        {
            var request = Request(new JArray(SetLiteral(true)), false);
            _asset.graph.variables.Set("external", 42);
            var response = VsEditGraph.HandleCommand(request);
            Assert.That((string)response["data"]["error"]["code"], Is.EqualTo("revision_conflict"));
            Assert.That(_asset.graph.variables.Get<int>("external"), Is.EqualTo(42));
            Assert.That((bool)((Literal)_asset.graph.units.Single()).value, Is.False);
        }

        [Test]
        public void AddConnectDisconnectAndExplicitRemovalAreValidated()
        {
            var branch = Guid.NewGuid().ToString();
            var connection = Guid.NewGuid().ToString();
            Success(VsEditGraph.HandleCommand(Request(new JArray(
                new JObject { ["op"] = "add_node", ["guid"] = branch, ["type"] = "If", ["x"] = 10, ["y"] = 20 },
                new JObject { ["op"] = "connect", ["guid"] = connection, ["kind"] = "value",
                    ["source_node"] = _literalId.ToString(), ["source_port"] = "output", ["destination_node"] = branch, ["destination_port"] = "condition" }), false)));
            var response = VsEditGraph.HandleCommand(Request(new JArray(new JObject { ["op"] = "remove_node", ["guid"] = branch }), false));
            Assert.That((bool)response["success"], Is.False);
            Assert.That(_asset.graph.valueConnections.Count, Is.EqualTo(1));
            Success(VsEditGraph.HandleCommand(Request(new JArray(
                new JObject { ["op"] = "disconnect", ["guid"] = connection },
                new JObject { ["op"] = "remove_node", ["guid"] = branch }), false)));
            Assert.That(_asset.graph.units.Count, Is.EqualTo(1));
            Assert.That(_asset.graph.valueConnections.Count, Is.Zero);
        }

        [Test]
        public void TypedDefaultsAndGraphVariablesAreChecked()
        {
            var wait = Guid.NewGuid().ToString();
            Success(VsEditGraph.HandleCommand(Request(new JArray(
                new JObject { ["op"] = "add_node", ["guid"] = wait, ["type"] = "WaitForSeconds", ["x"] = 0, ["y"] = 0 },
                new JObject { ["op"] = "set_default", ["guid"] = wait, ["port"] = "seconds", ["value"] = new JObject { ["type"] = "float", ["value"] = 2.5 } },
                new JObject { ["op"] = "set_variable", ["name"] = "count", ["value"] = new JObject { ["type"] = "int", ["value"] = 3 } }), false)));
            Assert.That(_asset.graph.variables.Get<int>("count"), Is.EqualTo(3));
            var invalid = VsEditGraph.HandleCommand(Request(new JArray(new JObject { ["op"] = "set_default", ["guid"] = wait,
                ["port"] = "seconds", ["value"] = new JObject { ["type"] = "string", ["value"] = "bad" } }), false));
            Assert.That((bool)invalid["success"], Is.False);
            Success(VsEditGraph.HandleCommand(Request(new JArray(new JObject { ["op"] = "remove_variable", ["name"] = "count" }), false)));
            Assert.That(_asset.graph.variables.IsDefined("count"), Is.False);
        }

        [Test]
        public void SharedMacroPathCannotModifyReferencedAsset()
        {
            var subgraph = new SubgraphUnit();
            subgraph.nest.source = GraphSource.Macro;
            subgraph.nest.macro = _asset;
            var container = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            container.graph = new FlowGraph();
            container.graph.units.Add(subgraph);
            AssetDatabase.CreateAsset(container, _folder + "/Container.asset");
            var request = Request(new JArray(SetLiteral(true)), false, container);
            request["parent_element_guids"] = new JArray(subgraph.guid.ToString());
            var response = VsEditGraph.HandleCommand(request);
            Assert.That((string)response["data"]["error"]["code"], Is.EqualTo("shared_macro"));
            Assert.That((bool)((Literal)_asset.graph.units.Single()).value, Is.False);
        }

        [Test]
        public void EmbeddedPathEditsOnlySelectedGraphAndMemberNodeCanBeCreated()
        {
            var nested = new SubgraphUnit();
            nested.nest.source = GraphSource.Embed;
            nested.nest.embed = (FlowGraph)_asset.graph.Serialize().Deserialize();
            _asset.graph.units.Add(nested);
            nested.Define();
            var request = Request(new JArray(SetLiteral(true), new JObject { ["op"] = "add_node",
                ["guid"] = Guid.NewGuid().ToString(), ["type"] = "GetMember", ["x"] = 10, ["y"] = 20,
                ["config"] = new JObject { ["member"] = new JObject { ["type"] = "System.String", ["name"] = "Length" } } }), false);
            request["parent_element_guids"] = new JArray(nested.guid.ToString());
            Success(VsEditGraph.HandleCommand(request));
            Assert.That((bool)_asset.graph.units.OfType<Literal>().Single().value, Is.False);
            var edited = _asset.graph.units.OfType<SubgraphUnit>().Single().nest.embed;
            Assert.That((bool)edited.units.OfType<Literal>().Single().value, Is.True);
            Assert.That(edited.units.OfType<GetMember>().Single().isDefined, Is.True);
        }

        [Test]
        public void IncompatibleValueConnectionLeavesOriginalGraphUntouched()
        {
            var source = Guid.NewGuid().ToString();
            var destination = Guid.NewGuid().ToString();
            var original = VsGraphEditService.Revision(_asset.graph);
            var response = VsEditGraph.HandleCommand(Request(new JArray(
                new JObject { ["op"] = "add_node", ["guid"] = source, ["type"] = "GetMember", ["x"] = 0, ["y"] = 0,
                    ["config"] = new JObject { ["member"] = new JObject { ["type"] = typeof(Transform).AssemblyQualifiedName, ["name"] = "position" } } },
                new JObject { ["op"] = "add_node", ["guid"] = destination, ["type"] = "If", ["x"] = 0, ["y"] = 0 },
                new JObject { ["op"] = "connect", ["guid"] = Guid.NewGuid().ToString(), ["kind"] = "value",
                    ["source_node"] = source, ["source_port"] = "value", ["destination_node"] = destination, ["destination_port"] = "condition" }), false));
            Assert.That((bool)response["success"], Is.False);
            Assert.That(VsGraphEditService.Revision(_asset.graph), Is.EqualTo(original));
        }

        [Test]
        public void ScenePrefabInstanceRecordsOverrideWithoutChangingPrefab()
        {
            var harness = SceneManager.GetActiveScene();
            if (string.IsNullOrEmpty(harness.path))
            {
                // Unity Test Runner restores the user's scene setup after the run.
                EditorSceneManager.SaveScene(harness, _folder + "/Harness.unity");
            }
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            ScriptMachine machine = null;
            try
            {
                var source = new GameObject("Embedded graph");
                SceneManager.MoveGameObjectToScene(source, scene);
                var sourceMachine = source.AddComponent<ScriptMachine>();
                sourceMachine.nest.source = GraphSource.Embed;
                sourceMachine.nest.embed = (FlowGraph)_asset.graph.Serialize().Deserialize();
                var prefab = PrefabUtility.SaveAsPrefabAsset(source, _folder + "/Machine.prefab");
                Object.DestroyImmediate(source);
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
                machine = instance.GetComponent<ScriptMachine>();
                EditorSceneManager.SaveScene(scene, _folder + "/Scene.unity");
                Success(VsEditGraph.HandleCommand(Request(new JArray(SetLiteral(true)), false, machine)));
                Assert.That(PrefabUtility.GetPropertyModifications(instance).Any(modification => modification.propertyPath.Contains("_data")), Is.True);
                Assert.That((bool)((Literal)prefab.GetComponent<ScriptMachine>().nest.embed.units.Single()).value, Is.False);
                Undo.PerformUndo();
                Assert.That((bool)((Literal)machine.nest.embed.units.Single()).value, Is.False);
                Undo.PerformRedo();
                Assert.That((bool)((Literal)machine.nest.embed.units.Single()).value, Is.True);
                EditorSceneManager.SaveScene(scene);
                Undo.ClearUndo(machine);
                EditorSceneManager.CloseScene(scene, true);
                scene = EditorSceneManager.OpenScene(_folder + "/Scene.unity", OpenSceneMode.Additive);
                machine = scene.GetRootGameObjects().Single().GetComponent<ScriptMachine>();
                Assert.That((bool)((Literal)machine.nest.embed.units.Single()).value, Is.True);
            }
            finally
            {
                if (machine != null) Undo.ClearUndo(machine);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }
    }
}
