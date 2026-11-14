using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsDiagnostics
    {
        public static JObject HandleCommand(JObject parameters)
        {
            if (parameters != null && parameters.HasValues)
                return VsResponse.Failure("invalid_arguments", "vs_diagnostics takes no parameters.");

            return VsResponse.Success(VsDiagnosticsService.Capture());
        }
    }
}
