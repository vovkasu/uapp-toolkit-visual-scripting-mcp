using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_clone_graph", Description = "Preview or create a new graph asset from an existing graph. Explicit share_macros or embed_macros policy. Apply requires expected_revision from this command preview. Never overwrites assets.")]
    public static class VsCloneGraph
    {
        public class Parameters
        {
            [ToolParameter("Source graph owner GlobalObjectId", Required = true)]
            public string Target { get; set; }
            [ToolParameter("Optional selected nested graph path", Required = false)]
            public string[] ParentElementGuids { get; set; }
            [ToolParameter("New .asset path in an existing Assets folder", Required = true)]
            public string Destination { get; set; }
            [ToolParameter("share_macros or embed_macros", Required = true)]
            public string MacroPolicy { get; set; }
            [ToolParameter("Preview only", Required = false, DefaultValue = "true")]
            public bool DryRun { get; set; }
            [ToolParameter("Revision from clone preview; required on apply", Required = false)]
            public string ExpectedRevision { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsCloneGraph.HandleCommand(parameters);
    }

    [McpForUnityTool("vs_bind_object", Description = "Preview or assign a macro graph or Object Variable reference on a scene/prefab machine. Apply requires expected_revision from binding preview. Undo supported; assignments are not auto-saved.")]
    public static class VsBindObject
    {
        public class Parameters
        {
            [ToolParameter("ScriptMachine/StateMachine component GlobalObjectId", Required = true)]
            public string Target { get; set; }
            [ToolParameter("graph or variable", Required = true)]
            public string Kind { get; set; }
            [ToolParameter("Referenced object GlobalObjectId; null clears a variable", Required = true)]
            public string Reference { get; set; }
            [ToolParameter("Object Variable name; required for variable kind", Required = false)]
            public string VariableName { get; set; }
            [ToolParameter("Preview only", Required = false, DefaultValue = "true")]
            public bool DryRun { get; set; }
            [ToolParameter("Revision from binding preview; required on apply", Required = false)]
            public string ExpectedRevision { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsBindObject.HandleCommand(parameters);
    }
}
