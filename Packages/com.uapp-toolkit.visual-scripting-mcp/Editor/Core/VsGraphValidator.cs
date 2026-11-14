using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal sealed class VsGraphValidator
    {
        private readonly VsGraphSnapshot _output = new VsGraphSnapshot();
        private readonly Dictionary<string, JObject> _elements = new Dictionary<string, JObject>();
        private readonly string _target;
        private int _errors;
        private int _warnings;
        private bool _incomplete;
        private bool _sourceIncomplete;

        private VsGraphValidator(string target) { _target = target; }

        public static VsGraphSnapshot Validate(VsGraphSnapshot source, string target)
        {
            var validator = new VsGraphValidator(target);
            return validator.Run(source);
        }

        private VsGraphSnapshot Run(VsGraphSnapshot source)
        {
            var summary = new JObject { ["kind"] = "validation_summary", ["target"] = _target };
            _output.Add(summary);
            foreach (var warning in source.Warnings)
            {
                _incomplete = true;
                _sourceIncomplete = true;
                _output.Warn((string)warning);
            }
            foreach (JObject record in source.Records)
            {
                if (record["guid"] == null) continue;
                var key = Key(record, (string)record["guid"]);
                if (_elements.ContainsKey(key)) Finding("error", "duplicate_element_guid", record, "Element GUID is duplicated in this graph.");
                else _elements.Add(key, record);
            }
            foreach (JObject record in source.Records)
            {
                if (_output.IsFull) { _incomplete = true; _output.Warn("record_limit_reached"); break; }
                CheckRecord(record);
            }
            summary["status"] = _errors > 0 ? "invalid" : _incomplete ? "inconclusive" : "valid";
            summary["error_count"] = _errors;
            summary["warning_count"] = _warnings;
            summary["inspection_complete"] = !_incomplete;
            if (_incomplete) _output.Warn("validation_incomplete");
            return _output;
        }

        private void CheckRecord(JObject record)
        {
            var kind = (string)record["kind"];
            if (kind == "missing_graph") Finding("error", "missing_graph", record, "The selected or nested graph is missing.");
            if ((bool?)record["omitted"] == true) { _incomplete = true; return; }
            if (kind == "port_definition" && (bool?)record["is_valid"] == false)
                Finding("error", "invalid_port_definition", record, "Graph port definition is invalid.", (string)record["key"]);
            if (kind == "node")
            {
                if ((string)record["type"] == typeof(MissingType).FullName)
                    Finding("error", "missing_node_type", record, "Visual Scripting preserved a missing node type placeholder.");
                else if (!string.IsNullOrEmpty((string)record["definition_error"]))
                    Finding("error", "node_definition_failed", record, "Node definition failed; inspect definition_error in vs_read_graph.");
                else if ((bool?)record["is_defined"] != true)
                {
                    _incomplete = true;
                    Finding("warning", "node_not_defined", record, "Ports are unavailable; the validator does not call Define.");
                }
                foreach (JObject port in (JArray)record["ports"])
                {
                    var portKind = (string)port["kind"];
                    if (portKind == "InvalidInput" || portKind == "InvalidOutput")
                        Finding("error", "invalid_port", record, "Node contains an invalid port.", (string)port["key"]);
                    if ((portKind == "ValueInput" || portKind == "ValueOutput") && ResolveType((string)port["value_type"]) == null)
                        Finding("error", "missing_port_type", record, "Value port type cannot be resolved.", (string)port["key"]);
                }
                Dependencies(record);
            }
            if (kind == "connection") CheckConnection(record);
            if (kind == "transition")
            {
                foreach (var endpoint in new[] { "source_guid", "destination_guid" })
                    if (!_elements.TryGetValue(Key(record, (string)record[endpoint]), out var state) || (string)state["kind"] != "state")
                        Finding(_sourceIncomplete ? "warning" : "error", "missing_transition_state", record, "Transition endpoint state is absent from the inspected snapshot.", endpoint);
            }
            CheckReferences(record, record);
        }

        private void CheckConnection(JObject connection)
        {
            if ((string)connection["type"] == typeof(InvalidConnection).FullName)
                Finding("error", "invalid_connection", connection, "Visual Scripting marks this connection as invalid.");
            if ((bool?)connection["endpoints_unresolved"] == true)
            {
                Finding("error", "unresolved_connection", connection, "Connection endpoints could not be read.");
                return;
            }
            var source = FindPort(connection, "source", "output");
            var destination = FindPort(connection, "destination", "input");
            if (source == null || destination == null) return;
            var valueConnection = (string)connection["type"] == typeof(ValueConnection).FullName;
            var controlConnection = (string)connection["type"] == typeof(ControlConnection).FullName;
            if (valueConnection && ((string)source["kind"] != "ValueOutput" || (string)destination["kind"] != "ValueInput") ||
                controlConnection && ((string)source["kind"] != "ControlOutput" || (string)destination["kind"] != "ControlInput"))
            {
                Finding("error", "connection_port_kind_mismatch", connection, "Connection kind does not match its ports.");
                return;
            }
            if (!valueConnection) return;
            var sourceType = ResolveType((string)source["value_type"]);
            var destinationType = ResolveType((string)destination["value_type"]);
            if (sourceType == null || destinationType == null) return;
            if (!ConversionUtility.CanConvert(sourceType, destinationType, false))
                Finding("error", "incompatible_value_types", connection, "Visual Scripting cannot convert the source type to the destination type.");
            else if (!ConversionUtility.CanConvert(sourceType, destinationType, true))
                Finding("warning", "runtime_conversion_required", connection, "Conversion depends on the runtime value; it is not a proven structural error.");
        }

        private JObject FindPort(JObject connection, string endpoint, string direction)
        {
            var reference = connection[endpoint] as JObject;
            if (reference == null || !_elements.TryGetValue(Key(connection, (string)reference["node_guid"]), out var node))
            {
                Finding(_sourceIncomplete ? "warning" : "error", "missing_connection_node", connection, "Connection node is absent from the inspected snapshot.", endpoint);
                return null;
            }
            var key = (string)reference["port_key"];
            if ((string)node["kind"] != "node")
            {
                Finding("error", "connection_endpoint_not_node", connection, "Connection endpoint references a non-node element.", key);
                return null;
            }
            if (node["ports"] is JArray ports)
                foreach (JObject port in ports)
                    if ((string)port["key"] == key && (string)port["direction"] == direction) return port;
            var unknown = (bool?)node["is_defined"] != true;
            if (unknown) _incomplete = true;
            Finding(unknown ? "warning" : "error", unknown ? "port_unavailable" : "missing_connection_port", connection,
                unknown ? "Endpoint node is undefined; port existence cannot be established." : "Connection port is absent or has the wrong direction.", key);
            return null;
        }

        private void Dependencies(JObject node)
        {
            var configuration = node["configuration"] as JObject;
            var scope = (string)configuration?["UnifiedVariableUnit.kind"];
            if (scope != null)
                Finding("info", "variable_dependency", node, "Variable may be supplied or changed at runtime; absence in a definition is not an error.", "name",
                    new JObject { ["scope"] = scope, ["default_name"] = node["defaults"]?["name"]?.DeepClone(),
                        ["owner_target"] = _target, ["resolution"] = "requires_runtime_context" });
            if (configuration?["MemberUnit.member"] is JObject member)
            {
                Finding("info", "member_dependency", node, "Referenced C# member; no invocation performed.", null, (JObject)member.DeepClone());
                if (ResolveType((string)member["declaring_type"]) == null)
                    Finding("error", "missing_member_type", node, "The referenced C# declaring type cannot be resolved.");
            }
            var type = (string)node["type"];
            if (type == typeof(CustomEvent).FullName || type == typeof(TriggerCustomEvent).FullName)
                Finding("info", "event_dependency", node, "Event sender or receiver may be outside this graph; no counterpart is required here.", "name");
        }

        private void CheckReferences(JToken token, JObject owner)
        {
            if (token is JObject value && (bool?)value["missing_unity_object"] == true)
                Finding("error", "destroyed_object_reference", owner, "A serialized value references a destroyed Unity object.");
            if (token is JContainer container)
                foreach (var child in container.Children()) CheckReferences(child, owner);
        }

        private void Finding(string severity, string code, JObject owner, string message, string port = null, JObject details = null)
        {
            if (_output.IsFull) { _incomplete = true; _output.Warn("record_limit_reached"); return; }
            if (severity == "error") _errors++;
            if (severity == "warning") _warnings++;
            _output.Add(new JObject { ["kind"] = "finding", ["severity"] = severity, ["code"] = code,
                ["target"] = _target, ["path"] = owner["path"]?.DeepClone() ?? new JArray(),
                ["element_guid"] = owner["guid"]?.DeepClone(), ["port_key"] = port,
                ["variable_name"] = owner["name"]?.DeepClone(),
                ["message"] = message, ["details"] = details });
        }

        private static string Key(JObject record, string guid) => record["path"]?.ToString(Formatting.None) + ":" + guid;

        private static Type ResolveType(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            var qualifiedType = Type.GetType(name, false);
            if (qualifiedType != null) return qualifiedType;
            return RuntimeCodebase.TryDeserializeType(name, out var type) ? type : null;
        }
    }
}
