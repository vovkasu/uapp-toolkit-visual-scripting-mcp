using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_diagnostics", Description = "Read-only Visual Scripting MCP capabilities and live Editor status. Takes no parameters; does not run graphs or enter Play Mode.")]
    public static class VsDiagnostics
    {
        public static JObject HandleCommand(JObject parameters) => Core.VsDiagnostics.HandleCommand(parameters);
    }
}
