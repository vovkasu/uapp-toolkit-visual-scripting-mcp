using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    public static class VsEditGraph
    {
        public static JObject HandleCommand(JObject parameters)
        {
            try
            {
                if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.isPlaying || EditorApplication.isPlayingOrWillChangePlaymode)
                    throw new VsGraphException("editor_busy", "Graph editing requires idle Edit Mode.");
                var request = VsEditRequest.Parse(parameters);
                return VsGraphEditService.Execute(request);
            }
            catch (VsGraphException exception) { return VsResponse.Failure(exception.Code, exception.Message); }
            catch (ArgumentException exception) { return VsResponse.Failure("invalid_arguments", exception.Message); }
            catch (Exception exception) { return VsResponse.Failure("edit_failed", "Graph edit failed: " + exception.GetType().Name); }
        }
    }

    internal sealed class VsEditRequest
    {
        public VsGraphRequest Graph { get; private set; }
        public string ExpectedRevision { get; private set; }
        public bool DryRun { get; private set; }
        public JArray Operations { get; private set; }

        public static VsEditRequest Parse(JObject parameters)
        {
            if (parameters == null || parameters.ToString(Formatting.None).Length > 65536)
                throw new ArgumentException("An edit request up to 64K characters is required.");
            var input = new JObject();
            foreach (var property in parameters.Properties())
            {
                var key = property.Name;
                switch (key)
                {
                    case "Target": key = "target"; break;
                    case "ParentElementGuids": key = "parent_element_guids"; break;
                    case "ExpectedRevision": key = "expected_revision"; break;
                    case "DryRun": case "dryRun": key = "dry_run"; break;
                    case "Operations": key = "operations"; break;
                }
                if (key != "target" && key != "parent_element_guids" && key != "expected_revision" && key != "dry_run" && key != "operations")
                    throw new ArgumentException("Unknown edit parameter.");
                if (input.ContainsKey(key)) throw new ArgumentException("Duplicate edit parameter alias.");
                input[key] = property.Value.DeepClone();
            }
            if (input["expected_revision"]?.Type != JTokenType.String || ((string)input["expected_revision"]).Length != 64)
                throw new ArgumentException("expected_revision from vs_read_graph is required.");
            if (input["dry_run"] != null && input["dry_run"].Type != JTokenType.Boolean)
                throw new ArgumentException("dry_run must be boolean.");
            if (!(input["operations"] is JArray operations) || operations.Count == 0 || operations.Count > 32)
                throw new ArgumentException("operations must contain 1-32 operations.");
            var graph = new JObject { ["target"] = input["target"]?.DeepClone() };
            if (input["parent_element_guids"] != null) graph["parent_element_guids"] = input["parent_element_guids"].DeepClone();
            return new VsEditRequest { Graph = VsGraphRequest.Parse(graph, true), ExpectedRevision = (string)input["expected_revision"],
                DryRun = (bool?)input["dry_run"] ?? true, Operations = operations };
        }
    }
}
