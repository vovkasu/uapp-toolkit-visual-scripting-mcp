using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.VisualScripting;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsGraphValidatorTests
    {
        [Test]
        public void ValidLiteralGraphPassesWithoutErrors()
        {
            var graph = new FlowGraph();
            graph.units.Add(new Literal(typeof(int), 42));
            var source = new VsGraphSnapshot();
            VsGraphReader.Visit(graph, new JArray(), 2, new HashSet<IGraph>(), source);
            Assert.That((string)Summary(VsGraphValidator.Validate(source, "test"))["status"], Is.EqualTo("valid"));
        }

        [TestCase("missing", "output", "missing_connection_node")]
        [TestCase("source", "missing", "missing_connection_port")]
        public void MissingEndpointProducesLocatedError(string sourceId, string port, string code)
        {
            var source = ConnectedSnapshot();
            foreach (JObject record in source.Records)
                if ((string)record["kind"] == "connection")
                {
                    record["source"]["node_guid"] = sourceId;
                    record["source"]["port_key"] = port;
                }
            var report = VsGraphValidator.Validate(source, "owner-id");
            var finding = Find(report, code);
            Assert.That((string)finding["severity"], Is.EqualTo("error"));
            Assert.That((string)finding["target"], Is.EqualTo("owner-id"));
            Assert.That((string)finding["element_guid"], Is.EqualTo("connection"));
        }

        [Test]
        public void IncompatibleValueTypesAreRejected()
        {
            var source = ConnectedSnapshot(typeof(UnityEngine.Vector3), typeof(UnityEngine.GameObject));
            Assert.That(Find(VsGraphValidator.Validate(source, "test"), "incompatible_value_types"), Is.Not.Null);
        }

        [Test]
        public void UnresolvedValuePortTypeIsAnError()
        {
            var source = new VsGraphSnapshot();
            var node = Node("node", "value", "output", typeof(int));
            node["ports"][0]["value_type"] = "Missing.Namespace.Type, Missing.Assembly";
            source.Add(node);
            Assert.That(Find(VsGraphValidator.Validate(source, "test"), "missing_port_type"), Is.Not.Null);
        }

        [Test]
        public void TransitionToMissingStateIsAnError()
        {
            var source = new VsGraphSnapshot();
            source.Add(new JObject { ["kind"] = "transition", ["guid"] = "transition", ["path"] = new JArray(),
                ["source_guid"] = "missing", ["destination_guid"] = "missing" });
            Assert.That(Find(VsGraphValidator.Validate(source, "test"), "missing_transition_state"), Is.Not.Null);
        }

        [Test]
        public void InvalidConnectionIsReportedExplicitly()
        {
            var source = ConnectedSnapshot();
            foreach (JObject record in source.Records)
                if ((string)record["kind"] == "connection") record["type"] = typeof(InvalidConnection).FullName;
            Assert.That(Find(VsGraphValidator.Validate(source, "test"), "invalid_connection"), Is.Not.Null);
        }

        [Test]
        public void MissingNodeTypePlaceholderIsAnError()
        {
            var source = new VsGraphSnapshot();
            var node = Node("missing", "output", "output", typeof(int));
            node["type"] = typeof(MissingType).FullName;
            source.Add(node);
            Assert.That((string)Find(VsGraphValidator.Validate(source, "test"), "missing_node_type")["severity"], Is.EqualTo("error"));
        }

        [Test]
        public void RuntimeVariableAndExternalEventDoNotFailValidation()
        {
            var source = new VsGraphSnapshot();
            var node = Node("variable", "output", "output", typeof(int));
            node["configuration"] = new JObject { ["UnifiedVariableUnit.kind"] = "Object" };
            node["defaults"] = new JObject { ["name"] = "AssignedAtRuntime", ["target"] = JValue.CreateNull() };
            source.Add(node);
            var eventNode = Node("event", "output", "output", typeof(int));
            eventNode["type"] = typeof(CustomEvent).FullName;
            source.Add(eventNode);
            var report = VsGraphValidator.Validate(source, "test");
            Assert.That((string)Summary(report)["status"], Is.EqualTo("valid"));
            Assert.That(Find(report, "variable_dependency"), Is.Not.Null);
            Assert.That(Find(report, "event_dependency"), Is.Not.Null);
        }

        [Test]
        public void TruncatedSnapshotDoesNotProveNodeIsMissing()
        {
            var source = new VsGraphSnapshot();
            source.Warn("record_limit_reached");
            source.Add(Connection());
            var report = VsGraphValidator.Validate(source, "test");
            Assert.That((string)Summary(report)["status"], Is.EqualTo("inconclusive"));
            Assert.That((int)Summary(report)["error_count"], Is.Zero);
        }

        [Test]
        public void OmittedNodeProducesInconclusiveReportInsteadOfThrowing()
        {
            var source = new VsGraphSnapshot();
            source.Add(new JObject { ["kind"] = "node", ["guid"] = "oversized", ["omitted"] = true });
            Assert.That((string)Summary(VsGraphValidator.Validate(source, "test"))["status"], Is.EqualTo("inconclusive"));
        }

        [Test]
        public void MissingNestedGraphReportsItsPath()
        {
            var source = new VsGraphSnapshot();
            VsGraphReader.Visit(null, new JArray("parent-guid"), 2, new HashSet<IGraph>(), source);
            var finding = Find(VsGraphValidator.Validate(source, "test"), "missing_graph");
            Assert.That((string)finding["path"][0], Is.EqualTo("parent-guid"));
        }

        [Test]
        public void DestroyedReferenceIsReportedButNullDefaultIsNot()
        {
            var source = new VsGraphSnapshot();
            source.Add(new JObject { ["kind"] = "variable", ["path"] = new JArray(),
                ["value"] = new JObject { ["missing_unity_object"] = true } });
            source.Add(new JObject { ["kind"] = "variable", ["path"] = new JArray(), ["value"] = JValue.CreateNull() });
            Assert.That((int)Summary(VsGraphValidator.Validate(source, "test"))["error_count"], Is.EqualTo(1));
        }

        private static VsGraphSnapshot ConnectedSnapshot(System.Type sourceType = null, System.Type destinationType = null)
        {
            var source = new VsGraphSnapshot();
            source.Add(Node("source", "output", "output", sourceType ?? typeof(int)));
            source.Add(Node("destination", "input", "input", destinationType ?? typeof(int)));
            source.Add(Connection());
            return source;
        }

        private static JObject Node(string guid, string port, string direction, System.Type type)
        {
            return new JObject { ["kind"] = "node", ["guid"] = guid, ["path"] = new JArray(),
                ["type"] = typeof(Literal).FullName, ["is_defined"] = true,
                ["configuration"] = new JObject(), ["defaults"] = new JObject(),
                ["ports"] = new JArray(new JObject { ["key"] = port, ["direction"] = direction,
                    ["kind"] = direction == "output" ? "ValueOutput" : "ValueInput", ["value_type"] = type.AssemblyQualifiedName }) };
        }

        private static JObject Connection()
        {
            return new JObject { ["kind"] = "connection", ["guid"] = "connection", ["path"] = new JArray(),
                ["type"] = typeof(ValueConnection).FullName,
                ["source"] = new JObject { ["node_guid"] = "source", ["port_key"] = "output" },
                ["destination"] = new JObject { ["node_guid"] = "destination", ["port_key"] = "input" } };
        }

        private static JObject Summary(VsGraphSnapshot report)
        {
            foreach (JObject record in report.Records) if ((string)record["kind"] == "validation_summary") return record;
            Assert.Fail("Summary missing.");
            return null;
        }

        private static JObject Find(VsGraphSnapshot report, string code)
        {
            foreach (JObject record in report.Records) if ((string)record["code"] == code) return record;
            Assert.Fail("Finding missing: " + code);
            return null;
        }
    }
}
