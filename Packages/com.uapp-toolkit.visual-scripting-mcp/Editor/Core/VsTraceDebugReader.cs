using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using Unity.VisualScripting;

namespace UAppToolKit.VisualScriptingMcp.Editor
{
    // VS 1.9.11 exposes only creating getters. Read existing dictionaries without replacing bindings.
    internal static class VsTraceDebugReader
    {
        private const int MaximumElements = 512;
        private const int MaximumDepth = 8;
        private const int MaximumExceptionLength = 2048;
        private static readonly FieldInfo RootData = typeof(GraphDebugDataProvider).GetField("rootDatas", BindingFlags.Static | BindingFlags.NonPublic);
        private static readonly PropertyInfo ElementData = typeof(GraphDebugData).GetProperty("elementsData", BindingFlags.Instance | BindingFlags.NonPublic);
        private static readonly PropertyInfo ChildData = typeof(GraphDebugData).GetProperty("childrenGraphsData", BindingFlags.Instance | BindingFlags.NonPublic);

        public static JObject Capture(IMachine machine, JArray path)
        {
            var records = new JArray();
            var result = new JObject { ["available"] = false, ["truncated"] = false, ["records"] = records };
            if (RootData == null || ElementData == null || ChildData == null)
            {
                result["reason"] = "unsupported_debug_layout";
                return result;
            }
            var roots = RootData.GetValue(null) as IDictionary;
            var data = roots != null && roots.Contains(machine) ? roots[machine] as GraphDebugData : null;
            foreach (var guid in path)
            {
                GraphDebugData child = null;
                if (data != null)
                    foreach (DictionaryEntry entry in (IDictionary)ChildData.GetValue(data))
                        if (((IGraphElement)entry.Key).guid.ToString() == (string)guid) child = entry.Value as GraphDebugData;
                data = child;
            }
            if (data == null) { result["reason"] = "no_existing_debug_data"; return result; }
            result["available"] = true;
            Visit(data, path, records, result, 0, new VisitBudget());
            return result;
        }

        private static void Visit(GraphDebugData data, JArray path, JArray records, JObject result, int depth, VisitBudget budget)
        {
            if (budget.Remaining-- <= 0) { result["truncated"] = true; return; }
            foreach (DictionaryEntry entry in (IDictionary)ElementData.GetValue(data))
            {
                if (records.Count >= MaximumElements) { result["truncated"] = true; return; }
                var element = (IGraphElement)entry.Key;
                var debug = (IGraphElementDebugData)entry.Value;
                var record = new JObject { ["kind"] = "debug_sample", ["path"] = path.DeepClone(),
                    ["element_guid"] = element.guid.ToString(), ["element_type"] = element.GetType().FullName };
                if (debug is IUnitDebugData unit) { record["last_invoke_frame"] = unit.lastInvokeFrame; record["last_invoke_time"] = unit.lastInvokeTime; }
                if (debug is IUnitConnectionDebugData connection) { record["last_invoke_frame"] = connection.lastInvokeFrame; record["last_invoke_time"] = connection.lastInvokeTime; }
                if (element is IUnitConnection ports)
                {
                    record["source_node"] = ports.source.unit.guid.ToString();
                    record["source_port"] = ports.source.key;
                    record["destination_node"] = ports.destination.unit.guid.ToString();
                    record["destination_port"] = ports.destination.key;
                }
                var exception = debug.runtimeException;
                if (exception != null)
                {
                    record["exception_type"] = exception.GetType().FullName;
                    var message = exception.Message;
                    record["exception_message"] = message.Length <= MaximumExceptionLength ? message : message.Substring(0, MaximumExceptionLength);
                }
                records.Add(record);
            }
            var children = (IDictionary)ChildData.GetValue(data);
            if (depth >= MaximumDepth)
            {
                if (children.Count != 0) result["truncated"] = true;
                return;
            }
            foreach (DictionaryEntry entry in children)
            {
                if (records.Count >= MaximumElements || budget.Remaining <= 0) { result["truncated"] = true; return; }
                if (!(entry.Value is GraphDebugData child)) continue;
                var childPath = (JArray)path.DeepClone();
                childPath.Add(((IGraphElement)entry.Key).guid.ToString());
                Visit(child, childPath, records, result, depth + 1, budget);
            }
        }

        private sealed class VisitBudget
        {
            public int Remaining { get; set; } = MaximumElements;
        }
    }
}
