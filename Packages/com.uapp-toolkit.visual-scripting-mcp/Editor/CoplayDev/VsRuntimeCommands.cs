using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_runtime_status", Description = "Read existing VS machine state in Play Mode. Omit instance_id to discover machines; pass returned instance_id AND play_session_id to inspect one. Does not activate machines.")]
    public static class VsRuntimeStatus
    {
        public class Parameters
        {
            [ToolParameter("Machine component instance ID; omit for discovery", Required = false)]
            public int? InstanceId { get; set; }
            [ToolParameter("Session GUID returned by discovery; required with instance ID", Required = false)]
            public string PlaySessionId { get; set; }
            [ToolParameter("Nested parent element GUID path", Required = false)]
            public string[] ParentElementGuids { get; set; }
            [ToolParameter("Page size, 1-200", Required = false, DefaultValue = "50")]
            public int PageSize { get; set; }
            [ToolParameter("Cursor from previous response", Required = false)]
            public string Cursor { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsRuntimeStatus.HandleCommand(parameters);
    }

    [McpForUnityTool("vs_runtime_variables", Description = "Read existing runtime variables of a selected machine. Requires instance_id and play_session_id. Scopes: all, graph, object, scene, application, saved. No fallback to definitions and no creation or saving.")]
    public static class VsRuntimeVariables
    {
        public class Parameters : VsRuntimeStatus.Parameters
        {
            [ToolParameter("Variable scope", Required = false, DefaultValue = "all")]
            public string Scope { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsRuntimeVariables.HandleCommand(parameters);
    }
}
