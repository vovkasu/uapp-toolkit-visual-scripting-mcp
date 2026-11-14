using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.VisualScripting;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsGraphTests
    {
        [TestCase(0)]
        [TestCase(201)]
        public void InvalidPageSizeIsRejected(int size)
        {
            Assert.Throws<ArgumentException>(() => VsGraphRequest.Parse(new JObject { ["page_size"] = size }, false));
        }

        [Test]
        public void TransportParameterAliasesNormalizeAndDuplicatesAreRejected()
        {
            Assert.That(VsGraphRequest.Parse(new JObject { ["PageSize"] = 7 }, false).PageSize, Is.EqualTo(7));
            Assert.Throws<ArgumentException>(() => VsGraphRequest.Parse(new JObject { ["PageSize"] = 7, ["page_size"] = 8 }, false));
        }

        [Test]
        public void PagingReturnsRemainingRecordsAndRejectsChangedSnapshot()
        {
            var snapshot = new VsGraphSnapshot();
            snapshot.Add(new JObject { ["kind"] = "test", ["value"] = 1 });
            snapshot.Add(new JObject { ["kind"] = "test", ["value"] = 2 });
            var input = new JObject { ["page_size"] = 1 };
            var first = snapshot.Page(VsGraphRequest.Parse(input, false), "list");
            input["cursor"] = first["data"]["next_cursor"].DeepClone();
            var second = snapshot.Page(VsGraphRequest.Parse(input, false), "list");
            Assert.That((int)second["data"]["result"]["records"][0]["value"], Is.EqualTo(2));
            Assert.That((bool)second["data"]["complete"], Is.True);
            snapshot.Add(new JObject { ["value"] = 3 });
            var error = Assert.Throws<VsGraphException>(() => snapshot.Page(VsGraphRequest.Parse(input, false), "list"));
            Assert.That(error.Code, Is.EqualTo("stale_cursor"));
        }

        [Test]
        public void LiteralAndGraphVariableAreReadWithoutRunningFlow()
        {
            var graph = new FlowGraph();
            var literal = new Literal(typeof(int), 42);
            graph.units.Add(literal);
            graph.variables.Set("answer", 42);
            var snapshot = new VsGraphSnapshot();
            VsGraphReader.Visit(graph, new JArray(), 2, new HashSet<IGraph>(), snapshot);
            var records = (JArray)snapshot.Page(VsGraphRequest.Parse(null, false), "read")["data"]["result"]["records"];
            Assert.That(records.Count, Is.EqualTo(3));
            Assert.That((int)records[1]["value"], Is.EqualTo(42));
            Assert.That((string)records[2]["guid"], Is.EqualTo(literal.guid.ToString()));
            Assert.That((int)records[2]["configuration"]["Literal._value"], Is.EqualTo(42));
        }

        [Test]
        public void ValueConnectionPreservesNodeIdsAndPortKeys()
        {
            var graph = new FlowGraph();
            var literal = new Literal(typeof(string), "answer");
            var variable = new GetVariable { kind = VariableKind.Graph };
            graph.units.Add(literal);
            graph.units.Add(variable);
            literal.output.ConnectToValid(variable.name);
            var snapshot = new VsGraphSnapshot();
            VsGraphReader.Visit(graph, new JArray(), 2, new HashSet<IGraph>(), snapshot);
            var records = (JArray)snapshot.Page(VsGraphRequest.Parse(null, false), "read")["data"]["result"]["records"];
            JObject connection = null;
            foreach (var record in records)
                if ((string)record["kind"] == "connection") connection = (JObject)record;
            Assert.That(connection, Is.Not.Null);
            Assert.That((string)connection["source"]["node_guid"], Is.EqualTo(literal.guid.ToString()));
            Assert.That((string)connection["destination"]["port_key"], Is.EqualTo("name"));
        }

        [Test]
        public void ExistingAncestorStopsRecursiveTraversal()
        {
            var graph = new FlowGraph();
            var snapshot = new VsGraphSnapshot();
            VsGraphReader.Visit(graph, new JArray(), 2, new HashSet<IGraph> { graph }, snapshot);
            var data = snapshot.Page(VsGraphRequest.Parse(null, false), "read")["data"];
            Assert.That(data["warnings"].Values<string>(), Does.Contain("nested_graph_cycle"));
            Assert.That((bool)data["complete"], Is.False);
        }

        [Test]
        public void DepthLimitKeepsNestedReferenceAndMarksIncomplete()
        {
            var graph = new FlowGraph();
            graph.units.Add(SubgraphUnit.WithInputOutput());
            var snapshot = new VsGraphSnapshot();
            VsGraphReader.Visit(graph, new JArray(), 0, new HashSet<IGraph>(), snapshot);
            var data = snapshot.Page(VsGraphRequest.Parse(null, false), "read")["data"];
            Assert.That(data["warnings"].Values<string>(), Does.Contain("max_depth_reached"));
            Assert.That(data["result"]["records"][1]["child_path"], Is.Not.Null);
        }

        [Test]
        public void ComputedSerializedPropertyIsNotEvaluated()
        {
            var node = new GetterProbeUnit();
            VsGraphValues.Configuration(node, new VsGraphSnapshot());
            Assert.That(node.GetterCalls, Is.Zero);
        }

        [Test]
        public void OversizedRecordIsSummarizedWithWarning()
        {
            var snapshot = new VsGraphSnapshot();
            snapshot.Add(new JObject { ["kind"] = "node", ["guid"] = "test", ["value"] = new string('x', 256 * 1024) });
            var data = snapshot.Page(VsGraphRequest.Parse(null, false), "read")["data"];
            Assert.That((bool)data["result"]["records"][0]["omitted"], Is.True);
            Assert.That(data["warnings"].Values<string>(), Does.Contain("oversized_record_summarized"));
        }

        private sealed class GetterProbeUnit : Unit
        {
            public int GetterCalls { get; private set; }
            [Serialize]
            public int Computed { get { GetterCalls++; return 1; } }
            protected override void Definition() { }
        }
    }
}
