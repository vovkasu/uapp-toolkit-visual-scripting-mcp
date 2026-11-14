using System;
using System.IO;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    [InitializeOnLoad]
    internal static class VsNodeRegeneration
    {
        private const string StateKey = "UAppToolKit.VisualScriptingMcp.NodeRegeneration";
        private static JObject _state;

        static VsNodeRegeneration()
        {
            _state = JObject.Parse(SessionState.GetString(StateKey, "{\"state\":\"not_requested\"}"));
            if ((string)_state["state"] == "pending" || (string)_state["state"] == "running")
            {
                _state["state"] = "interrupted";
                _state["message"] = "Domain reload interrupted regeneration; request it again after compilation.";
                Save();
            }
        }

        public static JObject Read() => (JObject)_state.DeepClone();

        public static void CheckAvailable()
        {
            if ((string)_state["state"] == "pending" || (string)_state["state"] == "running")
                throw new VsGraphException("editor_busy", "Node regeneration is pending or running. Poll vs_get_settings.");
            if (EditorUtility.scriptCompilationFailed)
                throw new VsGraphException("compilation_failed", "Fix C# compilation errors before applying VS settings or regenerating nodes.");
        }

        public static void Schedule(string revision)
        {
            _state = new JObject { ["state"] = "pending", ["job_id"] = Guid.NewGuid().ToString("N"), ["settings_revision"] = revision };
            Save();
            EditorApplication.update += Run;
        }

        private static void Run()
        {
            EditorApplication.update -= Run;
            var errors = 0;
            var warnings = 0;
            UnityEngine.Application.LogCallback onLog = (message, stack, type) =>
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert) errors++;
                if (type == LogType.Warning) warnings++;
            };
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode || EditorUtility.scriptCompilationFailed)
                    throw new InvalidOperationException("Editor is not ready. Retry after successful compilation/import.");
                if ((string)_state["settings_revision"] != VsSettingsPlan.Revision(VsSettingsService.Snapshot()))
                    throw new InvalidOperationException("Settings changed after scheduling. Preview again.");
                _state["state"] = "running";
                Save();
                UnityEngine.Application.logMessageReceived += onLog;
                UnitBase.Rebuild();
                _state["state"] = errors == 0 && File.Exists(BoltFlow.Paths.unitOptions) ? "completed" : "failed";
                _state["message"] = "Rebuild returned; status covers database generation, not graph execution or Player compatibility.";
            }
            catch (Exception exception)
            {
                _state["state"] = "failed";
                _state["message"] = exception.Message;
            }
            finally
            {
                UnityEngine.Application.logMessageReceived -= onLog;
                _state["errors"] = errors;
                _state["warnings"] = warnings;
                Save();
            }
        }

        private static void Save() => SessionState.SetString(StateKey, _state.ToString(Newtonsoft.Json.Formatting.None));
    }
}
