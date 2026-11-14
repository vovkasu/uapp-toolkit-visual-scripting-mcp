using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;
using UnityEditor;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    internal static class VsGraphReader
    {
        public static VsGraphSnapshot Read(VsGraphRequest request)
        {
            var root = VsGraphCatalog.Resolve(request.Target);
            var graph = root.childGraph;
            foreach (var token in request.ParentGuids)
            {
                if (graph == null) throw new VsGraphException("not_found", "Nested graph is missing.");
                IGraphParentElement parent = null;
                foreach (var element in graph.elements)
                    if (element.guid.ToString() == (string)token) parent = element as IGraphParentElement;
                if (parent == null) throw new VsGraphException("not_found", "Parent element was not found.");
                graph = parent.childGraph;
            }
            var snapshot = new VsGraphSnapshot();
            if (root is UnityEngine.Component owner && owner.TryGetComponent<Variables>(out var ownerVariables))
            {
                foreach (var variable in ownerVariables.declarations)
                    snapshot.Add(new JObject { ["kind"] = "variable", ["path"] = new JArray(),
                        ["scope"] = "object_definition", ["name"] = variable.name,
                        ["value"] = VsGraphValues.Read(variable.value, snapshot) });
            }
            Visit(graph, request.ParentGuids, request.MaxDepth, new HashSet<IGraph>(), snapshot);
            return snapshot;
        }

        internal static void Visit(IGraph graph, JArray path, int depth, HashSet<IGraph> ancestors, VsGraphSnapshot snapshot)
        {
            if (graph == null)
            {
                snapshot.Add(new JObject { ["kind"] = "missing_graph", ["path"] = path.DeepClone() });
                snapshot.Warn("missing_graph");
                return;
            }
            if (!ancestors.Add(graph)) { snapshot.Warn("nested_graph_cycle"); return; }
            try
            {
                snapshot.Add(new JObject { ["kind"] = "graph", ["path"] = path.DeepClone(),
                    ["type"] = graph.GetType().FullName, ["title"] = graph.title, ["summary"] = graph.summary,
                    ["element_count"] = graph.elements.Count });
                if (!(graph is FlowGraph) && !(graph is StateGraph)) snapshot.Warn("unsupported_graph_type");
                if (graph is FlowGraph flowGraph)
                {
                    var definitions = new List<IUnitPortDefinition>();
                    definitions.AddRange(flowGraph.controlInputDefinitions);
                    definitions.AddRange(flowGraph.controlOutputDefinitions);
                    definitions.AddRange(flowGraph.valueInputDefinitions);
                    definitions.AddRange(flowGraph.valueOutputDefinitions);
                    foreach (var definition in definitions)
                    {
                        var portRecord = new JObject { ["kind"] = "port_definition", ["path"] = path.DeepClone(),
                            ["key"] = definition.key, ["label"] = definition.label, ["summary"] = definition.summary,
                            ["type"] = definition.GetType().Name, ["is_valid"] = definition.isValid };
                        if (definition is ValuePortDefinition valueDefinition)
                            portRecord["value_type"] = valueDefinition.type?.AssemblyQualifiedName;
                        if (definition is ValueInputDefinition inputDefinition && inputDefinition.hasDefaultValue)
                            portRecord["default"] = VsGraphValues.Read(inputDefinition.defaultValue, snapshot);
                        snapshot.Add(portRecord);
                    }
                }
                if (graph is IGraphWithVariables variables)
                    foreach (var variable in variables.variables)
                        snapshot.Add(new JObject { ["kind"] = "variable", ["path"] = path.DeepClone(),
                            ["scope"] = "graph_definition", ["name"] = variable.name,
                            ["value"] = VsGraphValues.Read(variable.value, snapshot) });

                var elements = new List<IGraphElement>();
                foreach (var element in graph.elements) elements.Add(element);
                elements.Sort((left, right) => left.guid.CompareTo(right.guid));
                foreach (var element in elements)
                {
                    if (snapshot.IsFull) { snapshot.Warn("record_limit_reached"); break; }
                    var record = new JObject { ["kind"] = "element", ["guid"] = element.guid.ToString(),
                        ["path"] = path.DeepClone(), ["type"] = element.GetType().FullName,
                        ["configuration"] = VsGraphValues.Configuration(element, snapshot) };
                    if (element is IUnit unit) ReadUnit(unit, record, snapshot);
                    else if (element is IUnitConnection connection)
                    {
                        record["kind"] = "connection";
                        try
                        {
                            record["source"] = PortReference(connection.source);
                            record["destination"] = PortReference(connection.destination);
                        }
                        catch (Exception)
                        {
                            record["endpoints_unresolved"] = true;
                            snapshot.Warn("connection_endpoints_unresolved");
                        }
                    }
                    else if (element is IState state)
                    {
                        record["kind"] = "state";
                        record["is_start"] = state.isStart;
                        record["position"] = new JArray(state.position.x, state.position.y);
                    }
                    else if (element is IStateTransition transition)
                    {
                        record["kind"] = "transition";
                        record["source_guid"] = transition.source?.guid.ToString();
                        record["destination_guid"] = transition.destination?.guid.ToString();
                    }
                    else if (element is GraphGroup) record["kind"] = "group";
                    else snapshot.Warn("unclassified_graph_elements");

                    if (element is IGraphParentElement parent)
                    {
                        var childPath = (JArray)path.DeepClone();
                        childPath.Add(element.guid.ToString());
                        record["child_path"] = childPath;
                        if (element is IGraphNester nester)
                        {
                            record["source"] = nester.nest.source.ToString();
                            record["macro_target"] = nester.nest.macro is Object macro ? GlobalObjectId.GetGlobalObjectIdSlow(macro).ToString() : null;
                        }
                        snapshot.Add(record);
                        if (depth == 0) snapshot.Warn("max_depth_reached");
                        else Visit(parent.childGraph, childPath, depth - 1, ancestors, snapshot);
                    }
                    else snapshot.Add(record);
                }
            }
            finally { ancestors.Remove(graph); }
        }

        private static void ReadUnit(IUnit unit, JObject record, VsGraphSnapshot snapshot)
        {
            record["kind"] = "node";
            record["position"] = new JArray(unit.position.x, unit.position.y);
            record["is_defined"] = unit.isDefined;
            record["is_control_root"] = unit.isControlRoot;
            record["definition_error"] = unit.definitionException?.Message;
            if (!unit.isDefined) snapshot.Warn("undefined_units_present");
            var defaults = new JObject();
            foreach (var pair in unit.defaultValues) defaults[pair.Key] = VsGraphValues.Read(pair.Value, snapshot);
            record["defaults"] = defaults;
            var ports = new JArray();
            foreach (var port in unit.ports)
                ports.Add(new JObject { ["key"] = port.key, ["kind"] = port.GetType().Name,
                    ["direction"] = port is IUnitInputPort ? "input" : "output",
                    ["value_type"] = port is IUnitValuePort valuePort ? valuePort.type?.AssemblyQualifiedName : null });
            record["ports"] = ports;
        }

        private static JObject PortReference(IUnitPort port)
        {
            return port == null ? null : new JObject { ["node_guid"] = port.unit.guid.ToString(), ["port_key"] = port.key };
        }
    }
}
