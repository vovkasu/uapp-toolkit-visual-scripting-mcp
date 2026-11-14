using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_get_settings", Description = "Read supported Visual Scripting project settings, revision and node regeneration status. No mutation.")]
    public static class VsGetSettings
    {
        public static JObject HandleCommand(JObject parameters) => Core.VsGetSettings.HandleCommand(parameters);
    }

    [McpForUnityTool("vs_update_settings", Description = "Preview or apply explicit VS project settings changes. Apply requires settings revision. Optional regenerate_nodes schedules node database rebuild; poll vs_get_settings. Does not generate C#.")]
    public static class VsUpdateSettings
    {
        public class Parameters
        {
            [ToolParameter("Supported changes object; empty allowed for regeneration only", Required = true)]
            public JObject Changes { get; set; }
            [ToolParameter("Revision from vs_get_settings or preview", Required = true)]
            public string ExpectedRevision { get; set; }
            [ToolParameter("Preview only", Required = false, DefaultValue = "true")]
            public bool DryRun { get; set; }
            [ToolParameter("Explicitly schedule node database rebuild after apply", Required = false, DefaultValue = "false")]
            public bool RegenerateNodes { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsUpdateSettings.HandleCommand(parameters);
    }
}
