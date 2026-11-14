using System;
using System.IO;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    // Shared by the automated integration test and the documented MCP smoke test.
    public static class VsWorkflowFixture
    {
        public const string EventName = "AdvanceTutorial";
        public const string BrokenEventName = "AdvanceTutoral";
        public const string CompletedVariable = "Completed";
        public const string AssetName = "Tutorial.asset";

        public static ScriptGraphAsset CreateGraph(string folder)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || !folder.StartsWith("Assets/", StringComparison.Ordinal) || !AssetDatabase.IsValidFolder(folder))
                throw new ArgumentException("An existing Assets folder and Edit Mode are required.");
            var path = folder + "/" + AssetName;
            if (File.Exists(path) || File.Exists(path + ".meta")) throw new ArgumentException("Fixture destination already exists.");
            var asset = ScriptableObject.CreateInstance<ScriptGraphAsset>();
            asset.graph = new FlowGraph { title = "MCP Tutorial Workflow" };
            asset.graph.variables.Set("ExpectedEvent", EventName);
            var start = new CustomEvent { position = new Vector2(0, 0) };
            var complete = new SetVariable { kind = VariableKind.Object, position = new Vector2(300, 0) };
            var value = new Literal(typeof(bool), true) { position = new Vector2(0, 200) };
            asset.graph.units.Add(start);
            asset.graph.units.Add(complete);
            asset.graph.units.Add(value);
            start.Define();
            complete.Define();
            value.Define();
            start.defaultValues["name"] = BrokenEventName;
            complete.defaultValues["name"] = CompletedVariable;
            asset.graph.controlConnections.Add(new ControlConnection(start.trigger, complete.assign));
            asset.graph.valueConnections.Add(new ValueConnection(value.output, complete.input));
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssetIfDirty(asset);
            return asset;
        }
    }
}
