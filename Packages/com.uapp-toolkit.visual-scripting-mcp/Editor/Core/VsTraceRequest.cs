using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal sealed class VsTraceRequest
    {
        public const int MaximumCapacity = 2048;
        public const int MaximumPageSize = 20;
        public VsRuntimeRequest Runtime { get; private set; }
        public int Capacity { get; private set; } = 512;
        public int PageSize { get; private set; } = MaximumPageSize;
        public string TraceId { get; private set; }
        public string Cursor { get; private set; }
        public HashSet<string> EventNames { get; } = new HashSet<string>(StringComparer.Ordinal);

        public static VsTraceRequest Parse(JObject input, string operation)
        {
            var values = new JObject();
            foreach (var property in (input ?? new JObject()).Properties())
            {
                var key = property.Name;
                switch (key)
                {
                    case "InstanceId": key = "instance_id"; break;
                    case "PlaySessionId": key = "play_session_id"; break;
                    case "ParentElementGuids": key = "parent_element_guids"; break;
                    case "Capacity": key = "capacity"; break;
                    case "EventNames": key = "event_names"; break;
                    case "TraceId": key = "trace_id"; break;
                    case "Cursor": key = "cursor"; break;
                    case "PageSize": key = "page_size"; break;
                }
                var allowed = operation == "start"
                    ? key == "instance_id" || key == "play_session_id" || key == "parent_element_guids" || key == "capacity" || key == "event_names"
                    : key == "trace_id" || (operation == "read" && (key == "cursor" || key == "page_size"));
                if (!allowed || values.ContainsKey(key)) throw new ArgumentException("Unknown or duplicate trace parameter.");
                values[key] = property.Value.DeepClone();
            }
            var request = new VsTraceRequest();
            if (operation == "start")
            {
                if (values["capacity"] != null) request.Capacity = Integer(values["capacity"], 64, MaximumCapacity);
                values.Remove("capacity");
                if (values["event_names"] != null)
                {
                    if (!(values["event_names"] is JArray names) || names.Count > 32) throw new ArgumentException("event_names must contain up to 32 names.");
                    foreach (var name in names)
                    {
                        if (name.Type != JTokenType.String || string.IsNullOrEmpty((string)name) || ((string)name).Length > 256)
                            throw new ArgumentException("Event names must be nonempty strings up to 256 characters.");
                        request.EventNames.Add((string)name);
                    }
                }
                values.Remove("event_names");
                request.Runtime = VsRuntimeRequest.Parse(values, false);
                if (!request.Runtime.InstanceId.HasValue) throw new ArgumentException("instance_id and play_session_id are required.");
            }
            else
            {
                if (values["trace_id"]?.Type != JTokenType.String || !Guid.TryParse((string)values["trace_id"], out var id))
                    throw new ArgumentException("trace_id is required and must be a GUID.");
                request.TraceId = id.ToString("N");
                if (values["cursor"] != null)
                {
                    if (values["cursor"].Type != JTokenType.String) throw new ArgumentException("cursor must be a string.");
                    request.Cursor = (string)values["cursor"];
                }
                if (values["page_size"] != null) request.PageSize = Integer(values["page_size"], 1, MaximumPageSize);
            }
            return request;
        }

        private static int Integer(JToken token, int minimum, int maximum)
        {
            if (token.Type != JTokenType.Integer || !int.TryParse(token.ToString(), out var value) || value < minimum || value > maximum)
                throw new ArgumentException("Trace numeric argument is outside its allowed range.");
            return value;
        }
    }
}
