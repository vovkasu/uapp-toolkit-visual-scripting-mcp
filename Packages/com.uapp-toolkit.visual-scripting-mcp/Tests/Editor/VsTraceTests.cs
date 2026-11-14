using System;
using System.Collections;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsTraceTests
    {
        [Test]
        public void DebugReaderSelectsNestedContextWithoutCreatingData()
        {
            var owner = new GameObject("Trace debug fixture");
            owner.SetActive(false);
            var machine = owner.AddComponent<ScriptMachine>();
            var roots = (IDictionary)typeof(GraphDebugDataProvider).GetField("rootDatas", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var previous = roots.Contains(machine) ? roots[machine] : null;
            try
            {
                var graph = new FlowGraph();
                var nested = new SubgraphUnit();
                nested.nest.source = GraphSource.Embed;
                nested.nest.embed = new FlowGraph();
                graph.units.Add(nested);
                var fault = new Throw();
                nested.nest.embed.units.Add(fault);
                var root = new GraphDebugData(graph);
                roots[machine] = root;
                var path = new JArray(nested.guid.ToString());
                Assert.That((bool)VsTraceDebugReader.Capture(machine, path)["available"], Is.False);
                var child = root.GetOrCreateChildGraphData(nested);
                var debug = (IUnitDebugData)child.GetOrCreateElementData(fault);
                debug.runtimeException = new MissingReferenceException("Controlled missing dependency");
                debug.lastInvokeFrame = 12;
                var snapshot = VsTraceDebugReader.Capture(machine, path);
                Assert.That((bool)snapshot["available"], Is.True);
                Assert.That(((JArray)snapshot["records"]).Count, Is.EqualTo(1));
                Assert.That((string)snapshot["records"][0]["element_guid"], Is.EqualTo(fault.guid.ToString()));
                Assert.That((string)snapshot["records"][0]["exception_type"], Is.EqualTo(typeof(MissingReferenceException).FullName));
                Assert.That((int)snapshot["records"][0]["last_invoke_frame"], Is.EqualTo(12));
                Assert.That(JToken.DeepEquals(snapshot, VsTraceDebugReader.Capture(machine, path)), Is.True);
                Assert.That(machine.graphData, Is.Null);
            }
            finally
            {
                roots.Remove(machine);
                if (previous != null) roots[machine] = previous;
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void RingReportsOverwrittenRecordsAndContinuesAfterCursor()
        {
            var buffer = new VsTraceBuffer(2);
            buffer.Add(new JObject { ["kind"] = "one" });
            var cursor = (string)buffer.Read(null, 1)["cursor"];
            buffer.Add(new JObject { ["kind"] = "two" });
            buffer.Add(new JObject { ["kind"] = "three" });
            buffer.Add(new JObject { ["kind"] = "four" });
            var page = buffer.Read(cursor, 1);
            Assert.That((long)page["lost_records"], Is.EqualTo(1));
            Assert.That((long)page["dropped_total"], Is.EqualTo(2));
            Assert.That((string)page["records"][0]["kind"], Is.EqualTo("three"));
            Assert.That((bool)page["has_more"], Is.True);
            var last = buffer.Read((string)page["cursor"], 1);
            Assert.That((string)last["records"][0]["kind"], Is.EqualTo("four"));
            Assert.That((long)last["lost_records"], Is.Zero);
            Assert.That((bool)last["has_more"], Is.False);
            Assert.That(((JArray)buffer.Read((string)last["cursor"], 1)["records"]).Count, Is.Zero);
        }

        [Test]
        public void CursorCannotCrossTraceSessions()
        {
            var first = new VsTraceBuffer(2);
            var next = new VsTraceBuffer(2);
            var exception = Assert.Throws<VsGraphException>(() => next.Read((string)first.Read(null, 1)["cursor"], 1));
            Assert.That(exception.Code, Is.EqualTo("stale_cursor"));
        }

        [Test]
        public void BufferCopiesRecordsAndBoundsOversizedPayloads()
        {
            var buffer = new VsTraceBuffer(2);
            var record = new JObject { ["kind"] = "original" };
            buffer.Add(record);
            record["kind"] = "mutated";
            buffer.Add(new JObject { ["kind"] = "large", ["text"] = new string('x', 9000) });
            var page = buffer.Read(null, 2);
            Assert.That((string)page["records"][0]["kind"], Is.EqualTo("original"));
            Assert.That((string)page["records"][1]["omitted"], Is.EqualTo("record_byte_limit"));
        }

        [TestCase("not-base64")]
        [TestCase("")]
        public void MalformedCursorIsRejected(string cursor)
        {
            Assert.Throws<ArgumentException>(() => new VsTraceBuffer(2).Read(cursor, 1));
        }

        [TestCase("{\"capacity\":1}")]
        [TestCase("{\"event_names\":[null]}")]
        [TestCase("{\"event_names\":\"test\"}")]
        [TestCase("{\"InstanceId\":1,\"instance_id\":1}")]
        [TestCase("{}")]
        public void InvalidStartArgumentsAreRejected(string json)
        {
            Assert.Throws<ArgumentException>(() => VsTraceRequest.Parse(JObject.Parse(json), "start"));
        }

        [Test]
        public void ReadLimitsAndStopArgumentsAreEnforced()
        {
            var id = Guid.NewGuid().ToString("N");
            Assert.Throws<ArgumentException>(() => VsTraceRequest.Parse(new JObject { ["TraceId"] = id, ["PageSize"] = 21 }, "read"));
            Assert.Throws<ArgumentException>(() => VsTraceRequest.Parse(new JObject { ["TraceId"] = id, ["Cursor"] = "x" }, "stop"));
            Assert.That(VsTraceRequest.Parse(new JObject { ["TraceId"] = id }, "read").TraceId, Is.EqualTo(id));
        }

        [Test]
        public void EditModeStartAndUnknownTraceReturnStructuredErrors()
        {
            var start = VsTraceStart.HandleCommand(new JObject { ["InstanceId"] = -42, ["PlaySessionId"] = Guid.NewGuid().ToString() });
            Assert.That((string)start["data"]["error"]["code"], Is.EqualTo("not_playing"));
            var read = VsTraceRead.HandleCommand(new JObject { ["TraceId"] = Guid.NewGuid().ToString() });
            Assert.That((string)read["data"]["error"]["code"], Is.EqualTo("stale_trace"));
        }
    }
}
