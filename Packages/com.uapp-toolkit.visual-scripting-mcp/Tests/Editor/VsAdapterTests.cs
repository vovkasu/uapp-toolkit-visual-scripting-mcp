using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsAdapterTests
    {
        private const string CoreAssembly = "UAppToolKit.VisualScriptingMcp.Editor";
        private const string CoplayAssembly = "UAppToolKit.VisualScriptingMcp.CoplayDev.Editor";
        private const string CliAssembly = "UAppToolKit.VisualScriptingMcp.UnityCli.Editor";
        private static readonly Type[] Commands =
        {
            typeof(VsDiagnostics), typeof(VsListGraphs), typeof(VsReadGraph), typeof(VsValidateGraph),
            typeof(VsRuntimeStatus), typeof(VsRuntimeVariables), typeof(VsTraceStart), typeof(VsTraceRead),
            typeof(VsTraceStop), typeof(VsEditGraph), typeof(VsCloneGraph), typeof(VsBindObject),
            typeof(VsGetSettings), typeof(VsUpdateSettings)
        };

        [Test]
        public void CoreHasNoBridgeOrTestDependenciesAndPackageIsEditorOnly()
        {
            var references = typeof(VsDiagnostics).Assembly.GetReferencedAssemblies().Select(a => a.Name).ToArray();
            Assert.That(references, Does.Not.Contain("MCPForUnity.Editor"));
            Assert.That(references.Any(n => n.StartsWith("Unity.Pipeline", StringComparison.Ordinal)), Is.False);
            Assert.That(references, Does.Not.Contain("Assembly-CSharp"));
            Assert.That(references, Does.Not.Contain("nunit.framework"));
            var playerFiles = CompilationPipeline.GetAssemblies(AssembliesType.Player).SelectMany(a => a.sourceFiles);
            Assert.That(playerFiles.Any(p => p.Contains("com.uapp-toolkit.visual-scripting-mcp")), Is.False);
        }

        [TestCase(CoplayAssembly, "UAPP_VS_MCP_COPLAYDEV")]
        [TestCase(CliAssembly, "UAPP_VS_MCP_UNITY_CLI")]
        public void OnlyAdaptersEnabledByTheirVersionConstraintsAreLoaded(string assemblyName, string symbol)
        {
            // Test assembly has the same version conditions, but no bridge assembly references.
            var tests = CompilationPipeline.GetAssemblies().Single(a => a.name == typeof(VsAdapterTests).Assembly.GetName().Name);
            var expected = tests.defines.Contains(symbol);
            var adapter = FindAssembly(assemblyName);
            Assert.That(adapter != null, Is.EqualTo(expected), assemblyName);
            if (adapter == null) return;
            Assert.That(adapter.GetReferencedAssemblies().Select(a => a.Name), Does.Contain(CoreAssembly));
            Assert.That(adapter.GetReferencedAssemblies().Select(a => a.Name),
                Does.Not.Contain(assemblyName == CoplayAssembly ? CliAssembly : CoplayAssembly));
        }

        [TestCase(CoplayAssembly)]
        [TestCase(CliAssembly)]
        public void AvailableAdapterForwardsEveryCommandWithoutChangingErrorsOrInput(string assemblyName)
        {
            var adapter = FindAssembly(assemblyName);
            if (adapter == null) return; // Absence is asserted by the configuration test above.
            foreach (var command in Commands)
            {
                var input = new JObject { ["unknown_parameter"] = true };
                var before = input.DeepClone();
                var expected = (JObject)command.GetMethod("HandleCommand").Invoke(null, new object[] { input });
                var method = Handler(adapter, command);
                var actual = (JObject)method.Invoke(null, new object[] { input });
                Assert.That((bool)actual["success"], Is.False, command.Name);
                Assert.That(JToken.DeepEquals(actual, expected), Is.True, command.Name);
                Assert.That(JToken.DeepEquals(input, before), Is.True, command.Name);
            }
            var diagnostics = (JObject)Handler(adapter, typeof(VsDiagnostics)).Invoke(null, new object[] { null });
            Assert.That((bool)diagnostics["success"], Is.True);
            Assert.That((string)diagnostics["data"]["schema_version"], Is.EqualTo("uapp-toolkit.visual-scripting-mcp/1"));
        }

        [TestCase(CoplayAssembly)]
        [TestCase(CliAssembly)]
        public void AvailableBridgeDiscoversAllFourteenCommands(string assemblyName)
        {
            var adapter = FindAssembly(assemblyName);
            if (adapter == null) return;
            IEnumerable entries;
            string prefix;
            string nameProperty;
            if (assemblyName == CoplayAssembly)
            {
                var discovery = Type.GetType("MCPForUnity.Editor.Services.ToolDiscoveryService, MCPForUnity.Editor", true);
                entries = (IEnumerable)discovery.GetMethod("DiscoverAllTools").Invoke(Activator.CreateInstance(discovery), null);
                prefix = "vs_";
                nameProperty = "Name";
            }
            else
            {
                var registry = Type.GetType("Unity.Pipeline.Commands.CommandRegistry, Unity.Pipeline", true);
                entries = (IEnumerable)registry.GetMethod("DiscoverCommands").Invoke(null, null);
                prefix = "vs ";
                nameProperty = "FullName";
                foreach (var command in Commands)
                {
                    var attribute = Handler(adapter, command).GetCustomAttributes(false)
                        .Single(a => a.GetType().Name == "CliCommandAttribute");
                    Assert.That(attribute.GetType().GetProperty("MainThreadRequired").GetValue(attribute), Is.True);
                    var parameter = Handler(adapter, command).GetParameters().Single();
                    var argument = parameter.GetCustomAttributes(false).Single(a => a.GetType().Name == "CliArgAttribute");
                    Assert.That(argument.GetType().GetProperty("Required").GetValue(argument),
                        Is.EqualTo(!parameter.HasDefaultValue), command.Name);
                }
            }
            var names = entries.Cast<object>().Select(e => (string)e.GetType().GetProperty(nameProperty).GetValue(e))
                .Where(n => n.StartsWith(prefix, StringComparison.Ordinal)).ToArray();
            Assert.That(names.Length, Is.EqualTo(Commands.Length));
            Assert.That(names.Distinct().Count(), Is.EqualTo(Commands.Length));
        }

        [Test]
        public void CoplaySettingsChangesAreDiscoveredAsAnObject()
        {
            if (FindAssembly(CoplayAssembly) == null) return;
            var discoveryType = Type.GetType("MCPForUnity.Editor.Services.ToolDiscoveryService, MCPForUnity.Editor", true);
            var discovery = Activator.CreateInstance(discoveryType);
            var metadata = discoveryType.GetMethod("GetToolMetadata").Invoke(discovery, new object[] { "vs_update_settings" });
            var parameters = (IEnumerable)metadata.GetType().GetProperty("Parameters").GetValue(metadata);
            var changes = parameters.Cast<object>().Single(parameter =>
                (string)parameter.GetType().GetProperty("Name").GetValue(parameter) == "Changes");
            Assert.That(changes.GetType().GetProperty("Type").GetValue(changes), Is.EqualTo("object"));
        }

        private static System.Reflection.Assembly FindAssembly(string name) =>
            AppDomain.CurrentDomain.GetAssemblies().SingleOrDefault(a => a.GetName().Name == name);

        private static MethodInfo Handler(System.Reflection.Assembly adapter, Type command) =>
            adapter.GetName().Name == CoplayAssembly
                ? adapter.GetType("UAppToolKit.VisualScriptingMcp.CoplayDev.Editor." + command.Name, true).GetMethod("HandleCommand")
                : adapter.GetType("UAppToolKit.VisualScriptingMcp.UnityCli.Editor.VsCliCommands", true).GetMethod(command.Name.Substring(2));
    }
}
