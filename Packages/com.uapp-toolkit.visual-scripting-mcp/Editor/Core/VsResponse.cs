using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsResponse
    {
        private const string SchemaVersion = "uapp-toolkit.visual-scripting-mcp/1";

        public static JObject Success(JObject result)
        {
            return Create(result, null, null);
        }

        public static JObject Failure(string code, string message)
        {
            return Create(null, code, message);
        }

        private static JObject Create(JObject result, string errorCode, string errorMessage)
        {
            return new JObject
            {
                ["success"] = errorCode == null,
                ["error"] = errorMessage,
                ["data"] = new JObject
                {
                    ["schema_version"] = SchemaVersion,
                    ["editor"] = new JObject
                    {
                        ["mode"] = EditorApplication.isPlaying ? "play" : "edit",
                        ["is_paused"] = EditorApplication.isPaused,
                        ["is_compiling"] = EditorApplication.isCompiling,
                        ["is_updating"] = EditorApplication.isUpdating,
                        ["is_play_mode_transition_pending"] =
                            EditorApplication.isPlayingOrWillChangePlaymode != EditorApplication.isPlaying
                    },
                    ["capabilities"] = new JArray("vs_diagnostics", "vs_list_graphs", "vs_read_graph", "vs_validate_graph", "vs_runtime_status", "vs_runtime_variables", "vs_trace_start", "vs_trace_read", "vs_trace_stop", "vs_edit_graph", "vs_clone_graph", "vs_bind_object", "vs_get_settings", "vs_update_settings"),
                    ["warnings"] = new JArray(),
                    ["complete"] = true,
                    ["next_cursor"] = JValue.CreateNull(),
                    ["result"] = result,
                    ["error"] = errorCode == null ? JValue.CreateNull() : (JToken)new JObject
                    {
                        ["code"] = errorCode,
                        ["message"] = errorMessage
                    }
                }
            };
        }
    }
}
