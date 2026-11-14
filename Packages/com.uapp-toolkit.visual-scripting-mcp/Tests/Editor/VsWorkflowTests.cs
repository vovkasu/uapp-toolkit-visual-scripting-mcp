using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsWorkflowTests
    {
        private const string FolderKey = "VsMcp.WorkflowTest.Folder";
        private const string SessionKey = "VsMcp.WorkflowTest.Session";
        private const string TraceKey = "VsMcp.WorkflowTest.Trace";
        private const string OwnerName = "MCP Tutorial Owner";

        [UnityTest]
        public IEnumerator DiscoverDiagnoseRepairAndReplayTutorialAcrossPlaySessions()
        {
            var folder = "Assets/__VsWorkflowTest_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets", folder.Substring("Assets/".Length));
            SessionState.SetString(FolderKey, folder);
            // Unity's test runner owns/restores its scene setup. Never enter the game scene.
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var asset = VsWorkflowFixture.CreateGraph(folder);
            var owner = new GameObject(OwnerName);
            var machine = owner.AddComponent<ScriptMachine>();
            machine.nest.macro = asset;
            machine.nest.source = GraphSource.Macro;
            owner.GetComponent<Variables>().declarations.Set(VsWorkflowFixture.CompletedVariable, false);
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), folder + "/Fixture.unity");

            var found = Result(VsListGraphs.HandleCommand(new JObject { ["scope"] = "assets", ["folder"] = folder }));
            Assert.That(((JArray)found["records"]).Count, Is.EqualTo(1));
            var target = (string)found["records"][0]["target"];
            var read = Result(VsReadGraph.HandleCommand(new JObject { ["target"] = target }));
            Assert.That(read.ToString(), Does.Contain(VsWorkflowFixture.BrokenEventName));
            var validation = Result(VsValidateGraph.HandleCommand(new JObject { ["target"] = target }));
            Assert.That((int)validation["records"][0]["error_count"], Is.Zero, validation.ToString());

            yield return new EnterPlayMode(ExpectsDomainReload());
            var runtime = RuntimeOwner();
            SessionState.SetString(SessionKey, (string)runtime["play_session_id"]);
            var trace = Result(VsTraceStart.HandleCommand(new JObject { ["instance_id"] = runtime["instance_id"],
                ["play_session_id"] = runtime["play_session_id"], ["event_names"] = new JArray(VsWorkflowFixture.EventName) }));
            SessionState.SetString(TraceKey, (string)trace["trace_id"]);
            CustomEvent.Trigger(Owner().gameObject, VsWorkflowFixture.EventName);
            Assert.That(Completed(runtime), Is.False);
            var observed = Result(VsTraceRead.HandleCommand(new JObject { ["trace_id"] = SessionState.GetString(TraceKey, "") }));
            Assert.That(observed["records"].Any(record => (string)record["kind"] == "custom_event" &&
                (string)record["event_name"] == VsWorkflowFixture.EventName), Is.True);
            Result(VsTraceStop.HandleCommand(new JObject { ["trace_id"] = SessionState.GetString(TraceKey, "") }));
            yield return new ExitPlayMode();

            Assert.That((string)VsTraceRead.HandleCommand(new JObject { ["trace_id"] = SessionState.GetString(TraceKey, "") })["data"]["error"]["code"], Is.EqualTo("stale_trace"));
            var path = SessionState.GetString(FolderKey, "") + "/" + VsWorkflowFixture.AssetName;
            var editable = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(path);
            var editTarget = GlobalObjectId.GetGlobalObjectIdSlow(editable).ToString();
            var before = Result(VsReadGraph.HandleCommand(new JObject { ["target"] = editTarget }));
            var eventNode = editable.graph.units.OfType<CustomEvent>().Single();
            var edit = new JObject { ["target"] = editTarget, ["expected_revision"] = before["edit_revision"],
                ["operations"] = new JArray(new JObject { ["op"] = "set_default", ["guid"] = eventNode.guid.ToString(),
                    ["port"] = "name", ["value"] = new JObject { ["type"] = "string", ["value"] = VsWorkflowFixture.EventName } }) };
            Result(VsEditGraph.HandleCommand(edit));
            Assert.That((string)editable.graph.units.OfType<CustomEvent>().Single().defaultValues["name"], Is.EqualTo(VsWorkflowFixture.BrokenEventName));
            edit["dry_run"] = false;
            Result(VsEditGraph.HandleCommand(edit));
            Undo.PerformUndo();
            Assert.That((string)editable.graph.units.OfType<CustomEvent>().Single().defaultValues["name"], Is.EqualTo(VsWorkflowFixture.BrokenEventName));
            Undo.PerformRedo();
            Assert.That((string)editable.graph.units.OfType<CustomEvent>().Single().defaultValues["name"], Is.EqualTo(VsWorkflowFixture.EventName));
            AssetDatabase.SaveAssetIfDirty(editable);
            Assert.That((int)Result(VsValidateGraph.HandleCommand(new JObject { ["target"] = editTarget }))["records"][0]["error_count"], Is.Zero);

            yield return new EnterPlayMode(ExpectsDomainReload());
            var replay = RuntimeOwner();
            Assert.That((string)replay["play_session_id"], Is.Not.EqualTo(SessionState.GetString(SessionKey, "")));
            Assert.That((string)VsRuntimeStatus.HandleCommand(new JObject { ["instance_id"] = replay["instance_id"],
                ["play_session_id"] = SessionState.GetString(SessionKey, "") })["data"]["error"]["code"], Is.EqualTo("stale_reference"));
            Assert.That(Completed(replay), Is.False);
            CustomEvent.Trigger(Owner().gameObject, VsWorkflowFixture.EventName);
            Assert.That(Completed(replay), Is.True);
            yield return new ExitPlayMode();
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            if (UnityEditor.EditorApplication.isPlaying) yield return new ExitPlayMode();
            var folder = SessionState.GetString(FolderKey, "");
            if (!string.IsNullOrEmpty(folder))
            {
                EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
                var asset = AssetDatabase.LoadAssetAtPath<ScriptGraphAsset>(folder + "/" + VsWorkflowFixture.AssetName);
                if (asset != null) Undo.ClearUndo(asset);
                AssetDatabase.DeleteAsset(folder);
            }
            SessionState.EraseString(FolderKey);
            SessionState.EraseString(SessionKey);
            SessionState.EraseString(TraceKey);
        }

        private static bool ExpectsDomainReload() => !EditorSettings.enterPlayModeOptionsEnabled ||
            (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) == 0;

        private static ScriptMachine Owner() => SceneManager.GetActiveScene().GetRootGameObjects().Single(obj => obj.name == OwnerName).GetComponent<ScriptMachine>();

        private static JObject RuntimeOwner() => (JObject)Result(VsRuntimeStatus.HandleCommand(new JObject()))["records"]
            .Single(record => (string)record["name"] == OwnerName);

        private static bool Completed(JObject runtime)
        {
            var variables = Result(VsRuntimeVariables.HandleCommand(new JObject { ["instance_id"] = runtime["instance_id"],
                ["play_session_id"] = runtime["play_session_id"], ["scope"] = "object" }));
            return (bool)variables["records"].Single(record => (string)record["name"] == VsWorkflowFixture.CompletedVariable)["value"];
        }

        private static JObject Result(JObject response)
        {
            Assert.That((bool)response["success"], Is.True, response.ToString());
            return (JObject)response["data"]["result"];
        }
    }
}
