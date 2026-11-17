using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_list_graphs", Description = "Read-only graph discovery in assets, prefabs or loaded scenes. Returns stable target IDs. Edit Mode only. Unloaded scenes are never opened.")]
    public static class VsListGraphs
    {
        public class Parameters
        {
            [ToolParameter("assets, prefabs or loaded_scenes", Required = false, DefaultValue = "assets")]
            public string Scope { get; set; }
            [ToolParameter("Asset folder to search", Required = false, DefaultValue = "Assets")]
            public string Folder { get; set; }
            [ToolParameter("Page size, 1-200", Required = false, DefaultValue = "50")]
            public int PageSize { get; set; }
            [ToolParameter("Nonempty next_cursor from the previous page. Omit this field on the first page; stop when next_cursor is null.", Required = false)]
            public string Cursor { get; set; }
        }

        public static JObject HandleCommand(JObject parameters) => Core.VsListGraphs.HandleCommand(parameters);
    }

    [McpForUnityTool("vs_read_graph", Description = "Read graph definition as paged records: nodes, ports, connections, variables and nested graphs. No Flow evaluation. Edit Mode only.")]
    public static class VsReadGraph
    {
        public class Parameters
        {
            [ToolParameter("Exact target returned by vs_list_graphs")]
            public string Target { get; set; }
            [ToolParameter("Pass the returned child_path array to select a nested graph.", Required = false)]
            public string[] ParentElementGuids { get; set; }
            [ToolParameter("Nested depth, 0-8", Required = false, DefaultValue = "2")]
            public int MaxDepth { get; set; }
            [ToolParameter("Page size, 1-200", Required = false, DefaultValue = "50")]
            public int PageSize { get; set; }
            [ToolParameter("Nonempty next_cursor from the previous page. Omit this field on the first page; stop when next_cursor is null.", Required = false)]
            public string Cursor { get; set; }
        }

        public static JObject HandleCommand(JObject parameters) => Core.VsReadGraph.HandleCommand(parameters);
    }
}
