using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine.SceneManagement;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsDiagnosticsTests
    {
        [Test]
        public void UnknownArgumentsReturnStructuredErrorWithoutEchoingValues()
        {
            var response = VsDiagnostics.HandleCommand(new JObject { ["unexpected"] = "private-value" });

            Assert.That((bool)response["success"], Is.False);
            Assert.That((string)response["data"]["error"]["code"], Is.EqualTo("invalid_arguments"));
            Assert.That(response["data"]["result"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(response.ToString(), Does.Not.Contain("private-value"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void EmptyRequestReportsOnlyImplementedCapabilitiesWithoutDirtyingScene(bool useNull)
        {
            var scene = SceneManager.GetActiveScene();
            var wasDirty = scene.isDirty;
            var response = VsDiagnostics.HandleCommand(useNull ? null : new JObject());
            var data = response["data"];

            Assert.That((bool)response["success"], Is.True);
            Assert.That((string)data["schema_version"], Is.EqualTo("uapp-toolkit.visual-scripting-mcp/1"));
            Assert.That(data["capabilities"].Values<string>(), Is.EquivalentTo(new[] { "vs_diagnostics", "vs_list_graphs", "vs_read_graph", "vs_validate_graph", "vs_runtime_status", "vs_runtime_variables", "vs_trace_start", "vs_trace_read", "vs_trace_stop", "vs_edit_graph", "vs_clone_graph", "vs_bind_object", "vs_get_settings", "vs_update_settings" }));
            Assert.That((bool)data["result"]["full_execution_trace_supported"], Is.False);
            Assert.That(data["error"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(data["next_cursor"].Type, Is.EqualTo(JTokenType.Null));
            Assert.That(scene.isDirty, Is.EqualTo(wasDirty));
        }
    }
}
