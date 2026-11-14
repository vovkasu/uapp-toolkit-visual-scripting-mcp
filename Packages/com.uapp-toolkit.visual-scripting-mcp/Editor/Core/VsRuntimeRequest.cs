using System;
using Newtonsoft.Json.Linq;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal sealed class VsRuntimeRequest
    {
        public int? InstanceId { get; private set; }
        public string SessionId { get; private set; }
        public string Scope { get; private set; }
        public VsGraphRequest PageRequest { get; private set; }

        public static VsRuntimeRequest Parse(JObject parameters, bool variables)
        {
            var input = new JObject();
            foreach (var property in (parameters ?? new JObject()).Properties())
            {
                var name = property.Name;
                switch (name)
                {
                    case "InstanceId": name = "instance_id"; break;
                    case "PlaySessionId": name = "play_session_id"; break;
                    case "Scope": name = "scope"; break;
                    case "ParentElementGuids": name = "parent_element_guids"; break;
                    case "PageSize": name = "page_size"; break;
                    case "Cursor": name = "cursor"; break;
                }
                if (name != "instance_id" && name != "play_session_id" && name != "parent_element_guids" &&
                    name != "page_size" && name != "cursor" && !(variables && name == "scope"))
                    throw new ArgumentException("Unknown runtime parameter.");
                if (input.ContainsKey(name)) throw new ArgumentException("Duplicate parameter alias.");
                input[name] = property.Value.DeepClone();
            }
            var request = new VsRuntimeRequest { Scope = "all" };
            if (input["instance_id"] != null)
            {
                if (input["instance_id"].Type != JTokenType.Integer ||
                    !int.TryParse(input["instance_id"].ToString(), out var id) || id == 0)
                    throw new ArgumentException("instance_id must be a nonzero integer returned by runtime discovery.");
                request.InstanceId = id;
            }
            if (input["play_session_id"] != null)
            {
                if (input["play_session_id"].Type != JTokenType.String || !Guid.TryParse((string)input["play_session_id"], out var session))
                    throw new ArgumentException("play_session_id must be a session GUID.");
                request.SessionId = session.ToString("N");
            }
            if (variables && !request.InstanceId.HasValue) throw new ArgumentException("instance_id is required.");
            if (request.InstanceId.HasValue && request.SessionId == null) throw new ArgumentException("play_session_id is required with instance_id.");
            if (input["scope"] != null)
            {
                if (input["scope"].Type != JTokenType.String) throw new ArgumentException("scope must be a string.");
                request.Scope = (string)input["scope"];
                if (request.Scope != "all" && request.Scope != "graph" && request.Scope != "object" && request.Scope != "scene" &&
                    request.Scope != "application" && request.Scope != "saved") throw new ArgumentException("Unknown variable scope.");
            }
            var paging = new JObject { ["target"] = (request.SessionId ?? VsRuntimeSession.Current) + ":" + request.InstanceId + ":" + request.Scope };
            foreach (var key in new[] { "page_size", "cursor", "parent_element_guids" })
                if (input[key] != null) paging[key] = input[key].DeepClone();
            request.PageRequest = VsGraphRequest.Parse(paging, true);
            if (!request.InstanceId.HasValue && request.PageRequest.ParentGuids.Count != 0)
                throw new ArgumentException("A nested graph path requires instance_id.");
            return request;
        }
    }
}
