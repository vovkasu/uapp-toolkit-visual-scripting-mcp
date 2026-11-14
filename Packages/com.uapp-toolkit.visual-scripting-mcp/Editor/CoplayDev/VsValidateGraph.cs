using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_validate_graph", Description = "Read-only structural graph validation in idle Edit Mode. Same arguments as vs_read_graph. Reports errors separately from runtime dependencies and incomplete inspection. Does not execute or repair graphs.")]
    public static class VsValidateGraph
    {
        public class Parameters : VsReadGraph.Parameters { }

        public static JObject HandleCommand(JObject parameters) => Core.VsValidateGraph.HandleCommand(parameters);
    }
}
