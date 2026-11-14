using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsValidateGraph
    {
        public static JObject HandleCommand(JObject parameters) => VsGraphCommands.Execute(parameters, true, true);
    }
}
