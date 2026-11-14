using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    // Persist native Test Runner evidence even when a MCP request is lost on reload.
    [InitializeOnLoad]
    public static class VsValidationRunner
    {
        private const string RunKey = "VsMcp.Validation.Run";
        private const string ResultKey = "VsMcp.Validation.Result";

        static VsValidationRunner() => TestRunnerApi.RegisterTestCallback(new Results());

        public static string Run(bool workflowOnly = false)
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating || !string.IsNullOrEmpty(SessionState.GetString(RunKey, "")))
                throw new InvalidOperationException("Wait for idle Edit Mode and completion of the previous validation run.");
            var path = "Library/VisualScriptingMcp/Validation-" + Guid.NewGuid().ToString("N");
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            SessionState.SetString(RunKey, path);
            SessionState.SetString(ResultKey, path + ".json");
            var api = ScriptableObject.CreateInstance<TestRunnerApi>();
            try
            {
                api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode,
                    groupNames = new[] { workflowOnly ? "^UAppToolKit\\.VisualScriptingMcp\\.Editor\\.Tests\\.VsWorkflowTests\\." : "^UAppToolKit\\.VisualScriptingMcp\\.Editor\\.Tests\\." } }));
                return path + ".json";
            }
            catch { SessionState.EraseString(RunKey); throw; }
            finally { UnityEngine.Object.DestroyImmediate(api); }
        }

        public static string LastResultPath => SessionState.GetString(ResultKey, "");

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor testsToRun) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var path = SessionState.GetString(RunKey, "");
                if (string.IsNullOrEmpty(path)) return;
                try
                {
                    TestRunnerApi.SaveResultToFile(result, path + ".xml");
                    var failures = new JArray();
                    CollectFailures(result, failures);
                    File.WriteAllText(path + ".json", new JObject { ["state"] = result.ResultState,
                        ["passed"] = result.PassCount, ["failed"] = result.FailCount, ["skipped"] = result.SkipCount,
                        ["inconclusive"] = result.InconclusiveCount, ["seconds"] = result.Duration,
                        ["finished_utc"] = result.EndTime.ToUniversalTime().ToString("O"), ["failures"] = failures }.ToString());
                }
                finally { SessionState.EraseString(RunKey); }
            }

            private static void CollectFailures(ITestResultAdaptor result, JArray failures)
            {
                if (result.HasChildren)
                    foreach (var child in result.Children) CollectFailures(child, failures);
                else if (result.ResultState != "Passed")
                    failures.Add(new JObject { ["test"] = result.FullName, ["state"] = result.ResultState,
                        ["message"] = result.Message, ["stack"] = result.StackTrace });
            }
        }
    }
}
