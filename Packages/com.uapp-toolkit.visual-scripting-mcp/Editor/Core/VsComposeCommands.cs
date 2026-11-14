using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsCloneGraph
    {
        public static JObject HandleCommand(JObject parameters) => VsComposeCommands.Execute(parameters, true);
    }

    public static class VsBindObject
    {
        public static JObject HandleCommand(JObject parameters) => VsComposeCommands.Execute(parameters, false);
    }

    internal static class VsComposeCommands
    {
        public static JObject Execute(JObject parameters, bool clone)
        {
            try
            {
                if (EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating)
                    throw new VsGraphException("editor_busy", "Graph composition requires idle Edit Mode.");
                var input = VsComposeSupport.Parse(parameters, clone);
                return clone ? VsGraphCloneService.Execute(input) : VsObjectBindingService.Execute(input);
            }
            catch (VsGraphException exception) { return VsResponse.Failure(exception.Code, exception.Message); }
            catch (ArgumentException exception) { return VsResponse.Failure("invalid_arguments", exception.Message); }
            catch (Exception exception) { return VsResponse.Failure("composition_failed", "Graph composition failed: " + exception.GetType().Name); }
        }
    }
}
