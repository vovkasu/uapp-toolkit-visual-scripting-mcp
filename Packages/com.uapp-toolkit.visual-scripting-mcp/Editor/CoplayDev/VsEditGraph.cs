using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_edit_graph", Description = "Edit a Flow Graph in idle Edit Mode. Defaults to dry run. Requires edit_revision from vs_read_graph. Validates on a copy, preserves GUIDs, registers Undo and leaves the owner dirty without saving. Shared macros must be targeted directly.")]
    public static class VsEditGraph
    {
        public class Parameters
        {
            [ToolParameter("Graph asset or scene machine GlobalObjectId", Required = true)]
            public string Target { get; set; }
            [ToolParameter("Embedded parent GUID path; cannot cross a shared macro", Required = false)]
            public string[] ParentElementGuids { get; set; }
            [ToolParameter("edit_revision returned by vs_read_graph", Required = true)]
            public string ExpectedRevision { get; set; }
            [ToolParameter("Preview only; set false to apply", Required = false, DefaultValue = "true")]
            public bool DryRun { get; set; }
            [ToolParameter("Up to 32 explicit operations; see CONTRACT.md", Required = true)]
            public JArray Operations { get; set; }
        }

        public static JObject HandleCommand(JObject parameters) => Core.VsEditGraph.HandleCommand(parameters);
    }
}
