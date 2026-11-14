using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsGraphEditOperations
    {
        public static void Apply(FlowGraph graph, JArray operations)
        {
            var originalIds = new HashSet<Guid>(graph.elements.Select(element => element.guid));
            var removedIds = new HashSet<Guid>();
            foreach (var token in operations)
            {
                if (!(token is JObject operation)) throw new ArgumentException("Each operation must be an object.");
                var op = VsEditValues.Text(operation, "op");
                switch (op)
                {
                    case "add_node":
                        VsEditValues.Keys(operation, "op", "guid", "type", "config", "x", "y");
                        if (operation["config"] != null && !(operation["config"] is JObject)) throw new ArgumentException("config must be an object.");
                        var guid = VsEditValues.GuidValue(operation, "guid");
                        if (graph.elements.Any(element => element.guid == guid)) throw new ArgumentException("Element GUID already exists.");
                        var unit = Create(VsEditValues.Text(operation, "type"), operation["config"] as JObject);
                        unit.guid = guid;
                        unit.position = Position(operation);
                        graph.units.Add(unit);
                        unit.Define();
                        break;
                    case "configure_node":
                        VsEditValues.Keys(operation, "op", "guid", "config");
                        var configured = Node(graph, operation);
                        Configure(configured, operation["config"] as JObject);
                        configured.Define();
                        break;
                    case "move_node":
                        VsEditValues.Keys(operation, "op", "guid", "x", "y");
                        Node(graph, operation).position = Position(operation);
                        break;
                    case "set_default":
                        VsEditValues.Keys(operation, "op", "guid", "port", "value");
                        var target = Node(graph, operation);
                        var key = VsEditValues.Text(operation, "port");
                        if (!target.valueInputs.Contains(key)) throw new ArgumentException("Value input port was not found.");
                        var input = target.valueInputs[key];
                        var value = VsEditValues.Read(operation["value"]);
                        if (!input.hasDefaultValue) throw new ArgumentException("This input does not support a default value.");
                        if (value == null ? input.type.IsValueType && Nullable.GetUnderlyingType(input.type) == null : !input.type.IsInstanceOfType(value))
                            throw new ArgumentException("Default value type must match the input type.");
                        target.defaultValues[key] = value;
                        break;
                    case "set_variable":
                        VsEditValues.Keys(operation, "op", "name", "value");
                        graph.variables.Set(VsEditValues.Text(operation, "name"), VsEditValues.Read(operation["value"]));
                        break;
                    case "remove_variable":
                        VsEditValues.Keys(operation, "op", "name");
                        var name = VsEditValues.Text(operation, "name");
                        if (!graph.variables.IsDefined(name)) throw new ArgumentException("Graph variable was not found.");
                        var retained = graph.variables.Where(variable => variable.name != name).ToArray();
                        graph.variables.Clear();
                        foreach (var variable in retained)
                        {
                            graph.variables.Set(variable.name, variable.value);
                            graph.variables.GetDeclaration(variable.name).typeHandle = variable.typeHandle;
                        }
                        break;
                    case "connect":
                        Connect(graph, operation);
                        break;
                    case "disconnect":
                        VsEditValues.Keys(operation, "op", "guid");
                        var connectionId = VsEditValues.GuidValue(operation, "guid");
                        var connection = graph.elements.FirstOrDefault(element => element.guid == connectionId);
                        if (!(connection is IUnitConnection) && !(connection is InvalidConnection)) throw new ArgumentException("Connection was not found.");
                        graph.elements.Remove(connection);
                        removedIds.Add(connectionId);
                        break;
                    case "remove_node":
                        VsEditValues.Keys(operation, "op", "guid");
                        var removed = Node(graph, operation);
                        if (removed.ports.Any(port => port.hasAnyConnection)) throw new ArgumentException("Disconnect all node ports explicitly before removing it.");
                        graph.units.Remove(removed);
                        removedIds.Add(removed.guid);
                        break;
                    default: throw new ArgumentException("Unsupported graph edit operation.");
                }
            }
            foreach (var id in originalIds)
                if (!removedIds.Contains(id) && !graph.elements.Any(element => element.guid == id))
                    throw new VsGraphException("implicit_removal", "An operation would remove or replace an existing element implicitly. Disconnect affected ports explicitly first.");
            if (graph.invalidConnections.Count != 0 || graph.units.Any(unit => !unit.isDefined || unit.definitionException != null))
                throw new VsGraphException("validation_failed", "The edited Flow Graph contains undefined nodes or invalid connections.");
        }

        private static Vector2 Position(JObject operation) => new Vector2(VsEditValues.Number(operation, "x"), VsEditValues.Number(operation, "y"));

        private static Unit Node(FlowGraph graph, JObject operation)
        {
            var id = VsEditValues.GuidValue(operation, "guid");
            return graph.units.FirstOrDefault(unit => unit.guid == id) as Unit ?? throw new ArgumentException("Node GUID was not found.");
        }

        private static Unit Create(string type, JObject config)
        {
            Unit unit;
            switch (type)
            {
                case "If": unit = new If(); break;
                case "Null": unit = new Null(); break;
                case "ForEach": unit = new ForEach(); break;
                case "WaitForSeconds": unit = new WaitForSecondsUnit(); break;
                case "CustomEvent": unit = new CustomEvent(); break;
                case "TriggerCustomEvent": unit = new TriggerCustomEvent(); break;
                case "GetVariable": unit = new GetVariable(); break;
                case "SetVariable": unit = new SetVariable(); break;
                case "GetMember": unit = new GetMember(ReadMember(config)); break;
                case "SetMember": unit = new SetMember(ReadMember(config)); break;
                case "InvokeMember": unit = new InvokeMember(ReadMember(config)); break;
                case "Literal":
                    var value = VsEditValues.Read(config?["value"]);
                    if (value == null) throw new ArgumentException("Use Null for a null literal.");
                    unit = new Literal(value.GetType(), value);
                    break;
                default: throw new ArgumentException("Unsupported node type. Arbitrary type construction is not allowed.");
            }
            if (config != null) Configure(unit, config);
            return unit;
        }

        private static void Configure(Unit unit, JObject config)
        {
            if (config == null) throw new ArgumentException("config must be an object.");
            if (unit is MemberUnit memberUnit)
            {
                VsEditValues.Keys(config, "member");
                memberUnit.member = ReadMember(config);
            }
            else if (unit is UnifiedVariableUnit variable)
            {
                VsEditValues.Keys(config, "kind");
                if (!Enum.TryParse(VsEditValues.Text(config, "kind"), out VariableKind kind) || !Enum.IsDefined(typeof(VariableKind), kind))
                    throw new ArgumentException("Unknown variable kind.");
                variable.kind = kind;
            }
            else if (unit is Literal literal)
            {
                VsEditValues.Keys(config, "value");
                var value = VsEditValues.Read(config["value"]);
                if (value == null || value.GetType() != literal.type) throw new ArgumentException("Literal type cannot be changed in place.");
                literal.value = value;
            }
            else if (unit is CustomEvent customEvent || unit is TriggerCustomEvent)
            {
                VsEditValues.Keys(config, "argument_count");
                if (config["argument_count"]?.Type != JTokenType.Integer || !int.TryParse(config["argument_count"].ToString(), out var count) || count < 0 || count > 10)
                    throw new ArgumentException("argument_count must be 0-10.");
                if (unit is CustomEvent evt) evt.argumentCount = count;
                else ((TriggerCustomEvent)unit).argumentCount = count;
            }
            else if (config.Count != 0) throw new ArgumentException("No configurable fields supported for this node.");
        }

        private static Member ReadMember(JObject config)
        {
            if (!(config?["member"] is JObject member)) throw new ArgumentException("member configuration is required.");
            VsEditValues.Keys(member, "type", "name", "parameter_types");
            var type = ResolveType(VsEditValues.Text(member, "type"));
            Type[] parameterTypes = null;
            if (member["parameter_types"] != null)
            {
                if (!(member["parameter_types"] is JArray parameters) || parameters.Count > 16 || parameters.Any(token => token.Type != JTokenType.String))
                    throw new ArgumentException("parameter_types must be an array of up to 16 type names.");
                parameterTypes = parameters.Select(token => ResolveType((string)token)).ToArray();
            }
            return new Member(type, VsEditValues.Text(member, "name"), parameterTypes);
        }

        private static Type ResolveType(string name)
        {
            var type = Type.GetType(name, false);
            if (type == null) RuntimeCodebase.TryDeserializeType(name, out type);
            if (type == null || type.ContainsGenericParameters || type.IsPointer || type.IsByRef) throw new ArgumentException("Member type could not be resolved.");
            return type;
        }

        private static void Connect(FlowGraph graph, JObject operation)
        {
            VsEditValues.Keys(operation, "op", "guid", "kind", "source_node", "source_port", "destination_node", "destination_port");
            var id = VsEditValues.GuidValue(operation, "guid");
            if (graph.elements.Any(element => element.guid == id)) throw new ArgumentException("Connection GUID already exists.");
            var source = Node(graph, new JObject { ["guid"] = operation["source_node"] });
            var destination = Node(graph, new JObject { ["guid"] = operation["destination_node"] });
            var sourceKey = VsEditValues.Text(operation, "source_port");
            var destinationKey = VsEditValues.Text(operation, "destination_port");
            switch (VsEditValues.Text(operation, "kind"))
            {
                case "control":
                    if (!source.controlOutputs.Contains(sourceKey) || !destination.controlInputs.Contains(destinationKey)) throw new ArgumentException("Control port not found.");
                    if (source.controlOutputs[sourceKey].hasValidConnection) throw new ArgumentException("Disconnect the existing control connection first.");
                    graph.controlConnections.Add(new ControlConnection(source.controlOutputs[sourceKey], destination.controlInputs[destinationKey]) { guid = id });
                    break;
                case "value":
                    if (!source.valueOutputs.Contains(sourceKey) || !destination.valueInputs.Contains(destinationKey)) throw new ArgumentException("Value port not found.");
                    if (destination.valueInputs[destinationKey].hasValidConnection) throw new ArgumentException("Disconnect the existing value connection first.");
                    if (!ConversionUtility.CanConvert(source.valueOutputs[sourceKey].type, destination.valueInputs[destinationKey].type, false))
                        throw new ArgumentException("Value port types are incompatible.");
                    graph.valueConnections.Add(new ValueConnection(source.valueOutputs[sourceKey], destination.valueInputs[destinationKey]) { guid = id });
                    break;
                default: throw new ArgumentException("Connection kind must be control or value.");
            }
        }
    }
}
