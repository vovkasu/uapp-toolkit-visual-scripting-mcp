using System;
using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsTraceStart
    {
        public static JObject HandleCommand(JObject parameters) => VsTraceCommands.Execute(parameters, "start");
    }

    public static class VsTraceRead
    {
        public static JObject HandleCommand(JObject parameters) => VsTraceCommands.Execute(parameters, "read");
    }

    public static class VsTraceStop
    {
        public static JObject HandleCommand(JObject parameters) => VsTraceCommands.Execute(parameters, "stop");
    }

    internal static class VsTraceCommands
    {
        public static JObject Execute(JObject parameters, string operation)
        {
            try
            {
                var request = VsTraceRequest.Parse(parameters, operation);
                switch (operation)
                {
                    case "start": return VsTraceSession.Start(request);
                    case "read": return VsTraceSession.Read(request);
                    case "stop": return VsTraceSession.Stop(request);
                    default: throw new ArgumentException("Unknown trace operation.");
                }
            }
            catch (VsGraphException exception) { return VsResponse.Failure(exception.Code, exception.Message); }
            catch (ArgumentException exception) { return VsResponse.Failure("invalid_arguments", exception.Message); }
            catch (Exception exception) { return VsResponse.Failure("inspection_failed", "Trace failed: " + exception.GetType().Name); }
        }
    }
}
