using System;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsListGraphs
    {
        public static JObject HandleCommand(JObject parameters) => VsGraphCommands.Execute(parameters, false);
    }

    public static class VsReadGraph
    {
        public static JObject HandleCommand(JObject parameters) => VsGraphCommands.Execute(parameters, true);
    }

    internal static class VsGraphCommands
    {
        public static JObject Execute(JObject parameters, bool read, bool validate = false)
        {
            try
            {
                var request = VsGraphRequest.Parse(parameters, read);
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlayingOrWillChangePlaymode)
                    return VsResponse.Failure("editor_busy", "Graph definition inspection requires idle Edit Mode.");
                var snapshot = read ? VsGraphReader.Read(request) : VsGraphCatalog.List(request);
                if (validate) snapshot = VsGraphValidator.Validate(snapshot, request.Target);
                var response = snapshot.Page(request, validate ? "validate" : read ? "read" : "list");
                if (read && !validate)
                    response["data"]["result"]["edit_revision"] = VsGraphEditService.Revision(VsGraphCatalog.Resolve(request.Target).childGraph);
                return response;
            }
            catch (VsGraphException exception)
            {
                return VsResponse.Failure(exception.Code, exception.Message);
            }
            catch (ArgumentException exception)
            {
                return VsResponse.Failure("invalid_arguments", exception.Message);
            }
            catch (Exception exception)
            {
                return VsResponse.Failure("inspection_failed", "Graph inspection failed: " + exception.GetType().Name);
            }
        }
    }

    internal sealed class VsGraphException : Exception
    {
        public string Code { get; }
        public VsGraphException(string code, string message) : base(message) { Code = code; }
    }
}
