using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.CoplayDev.Editor
{
    [McpForUnityTool("vs_trace_start", Description = "Start one bounded Editor-only trace for a runtime machine. Samples existing debug data and observes owner Custom Events. Not a complete execution history.")]
    public static class VsTraceStart
    {
        public class Parameters
        {
            [ToolParameter("Machine component ID from runtime discovery", Required = true)]
            public int InstanceId { get; set; }
            [ToolParameter("Current play session GUID", Required = true)]
            public string PlaySessionId { get; set; }
            [ToolParameter("Selected nested graph path; debug includes descendants", Required = false)]
            public string[] ParentElementGuids { get; set; }
            [ToolParameter("Ring capacity, 64-2048", Required = false, DefaultValue = "512")]
            public int Capacity { get; set; }
            [ToolParameter("Up to 32 Custom Event names; empty means all on owner", Required = false)]
            public string[] EventNames { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsTraceStart.HandleCommand(parameters);
    }

    [McpForUnityTool("vs_trace_read", Description = "Read bounded trace records after a cursor. Reports overwritten records and observation limits. Read does not consume records.")]
    public static class VsTraceRead
    {
        public class Parameters
        {
            [ToolParameter("Trace ID returned by start", Required = true)]
            public string TraceId { get; set; }
            [ToolParameter("Cursor returned by previous read", Required = false)]
            public string Cursor { get; set; }
            [ToolParameter("Records per page, 1-20", Required = false, DefaultValue = "20")]
            public int PageSize { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsTraceRead.HandleCommand(parameters);
    }

    [McpForUnityTool("vs_trace_stop", Description = "Stop trace subscriptions. Buffer remains readable until another trace, Play transition or reload. Stop is idempotent.")]
    public static class VsTraceStop
    {
        public class Parameters
        {
            [ToolParameter("Trace ID returned by start", Required = true)]
            public string TraceId { get; set; }
        }
        public static JObject HandleCommand(JObject parameters) => Core.VsTraceStop.HandleCommand(parameters);
    }
}
