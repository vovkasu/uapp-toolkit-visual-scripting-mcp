using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsRuntimeStatus
    {
        public static JObject HandleCommand(JObject parameters) => VsRuntimeCommands.Execute(parameters, false);
    }

    public static class VsRuntimeVariables
    {
        public static JObject HandleCommand(JObject parameters) => VsRuntimeCommands.Execute(parameters, true);
    }

    internal static class VsRuntimeCommands
    {
        public static JObject Execute(JObject parameters, bool variables)
        {
            JObject response;
            try
            {
                var request = VsRuntimeRequest.Parse(parameters, variables);
                if (!EditorApplication.isPlaying) throw new VsGraphException("not_playing", "Runtime inspection requires Play Mode.");
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || !EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new VsGraphException("editor_busy", "Wait for a stable Play Mode state.");
                if (request.SessionId != null) VsRuntimeSession.Require(request.SessionId);
                var snapshot = variables ? VsRuntimeReader.Variables(request) : VsRuntimeReader.Status(request);
                response = snapshot.Page(request.PageRequest, variables ? "runtime_variables" : "runtime_status");
            }
            catch (VsGraphException exception) { response = VsResponse.Failure(exception.Code, exception.Message); }
            catch (ArgumentException exception) { response = VsResponse.Failure("invalid_arguments", exception.Message); }
            catch (Exception exception) { response = VsResponse.Failure("inspection_failed", "Runtime inspection failed: " + exception.GetType().Name); }
            response["data"]["play_session_id"] = VsRuntimeSession.Current;
            response["data"]["observed_frame"] = UnityEngine.Time.frameCount;
            return response;
        }
    }
}
