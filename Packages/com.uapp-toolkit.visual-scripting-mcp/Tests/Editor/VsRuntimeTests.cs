using System;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsRuntimeTests
    {
        [TestCase("{\"instance_id\":42}")]
        [TestCase("{\"scope\":\"disk\"}")]
        [TestCase("{\"page_size\":201}")]
        [TestCase("{\"InstanceId\":42,\"instance_id\":42}")]
        [TestCase("{\"play_session_id\":\"invalid\"}")]
        public void InvalidRequestsAreRejected(string json)
        {
            Assert.Throws<ArgumentException>(() => VsRuntimeRequest.Parse(JObject.Parse(json), false));
        }

        [Test]
        public void VariablesRequireMachineAndSession()
        {
            Assert.Throws<ArgumentException>(() => VsRuntimeRequest.Parse(new JObject(), true));
            var id = Guid.NewGuid();
            var request = VsRuntimeRequest.Parse(new JObject { ["InstanceId"] = -42, ["PlaySessionId"] = id.ToString(), ["Scope"] = "saved" }, true);
            Assert.That(request.InstanceId, Is.EqualTo(-42));
            Assert.That(request.SessionId, Is.EqualTo(id.ToString("N")));
            Assert.That(request.Scope, Is.EqualTo("saved"));
        }

        [Test]
        public void EditModeReturnsNotPlaying()
        {
            var response = VsRuntimeStatus.HandleCommand(new JObject());
            Assert.That((string)response["data"]["error"]["code"], Is.EqualTo("not_playing"));
        }

        [Test]
        public void ReadingUnavailableGraphVariablesDoesNotCreateRuntimeData()
        {
            var owner = new GameObject("Runtime reader test");
            owner.SetActive(false);
            try
            {
                var machine = owner.AddComponent<ScriptMachine>();
                machine.nest.source = GraphSource.Embed;
                machine.nest.embed = new FlowGraph();
                machine.nest.embed.variables.Set("definitionOnly", 12);
                var request = VsRuntimeRequest.Parse(new JObject { ["instance_id"] = machine.GetInstanceID(),
                    ["play_session_id"] = Guid.NewGuid().ToString("N"), ["scope"] = "graph" }, true);
                var before = machine.graphData;
                var componentsBefore = owner.GetComponents<Component>().Length;
                var records = VsRuntimeReader.Variables(request).Records.ToArray();
                Assert.That(records.Length, Is.EqualTo(1));
                Assert.That((bool)records[0]["available"], Is.False);
                Assert.That(machine.graphData, Is.SameAs(before));
                Assert.That(owner.GetComponents<Component>().Length, Is.EqualTo(componentsBefore));
            }
            finally { Object.DestroyImmediate(owner); }
        }
    }
}
