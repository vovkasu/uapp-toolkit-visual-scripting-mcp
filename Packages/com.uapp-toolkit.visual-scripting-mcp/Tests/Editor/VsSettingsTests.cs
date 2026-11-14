using System;
using System.Collections;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using Unity.VisualScripting;
using UnityEditor;
using UnityEngine.TestTools;

namespace UAppToolKit.VisualScriptingMcp.Editor.Tests
{
    public class VsSettingsTests
    {
        private JObject _original;

        [OneTimeSetUp]
        public void InitializeVisualScriptingForSettingsTests()
        {
            if (!PluginContainer.initialized)
            {
                var response = VsGetSettings.HandleCommand(null);
                Assert.That((string)response["data"]["error"]["code"], Is.EqualTo("visual_scripting_not_initialized"));
                Assert.That(PluginContainer.initialized, Is.False);
                // Explicit test setup, also needed in projects that have never opened a graph.
                VSUsageUtility.isVisualScriptingUsed = true;
            }
        }

        [SetUp]
        public void SetUp() => _original = VsSettingsService.Snapshot();

        [TearDown]
        public void TearDown()
        {
            if (_original == null) return;
            VsSettingsService.Apply(VsSettingsService.Snapshot(), _original);
            VsSettingsUndo.ClearRecords();
        }

        private JObject Request(JObject changes) => new JObject
        {
            ["changes"] = changes, ["expected_revision"] = VsSettingsPlan.Revision(_original)
        };

        [Test]
        public void ReadAndPreviewDoNotChangeSettingsOrScheduleRegeneration()
        {
            var job = VsNodeRegeneration.Read();
            var read = VsGetSettings.HandleCommand(null);
            Assert.That((bool)read["success"], Is.True);
            var response = VsUpdateSettings.HandleCommand(Request(new JObject { ["aot_safe_mode"] = !(bool)_original["aot_safe_mode"] }));
            Assert.That((bool)response["success"], Is.True, response.ToString());
            Assert.That(((JObject)response["data"]["result"]["diff"]).Properties().Select(property => property.Name), Is.EqualTo(new[] { "aot_safe_mode" }));
            Assert.That(JToken.DeepEquals(_original, VsSettingsService.Snapshot()), Is.True);
            Assert.That(JToken.DeepEquals(job, VsNodeRegeneration.Read()), Is.True);
        }

        [Test]
        public void StaleRevisionRejectsEntireRequest()
        {
            var request = Request(new JObject { ["aot_safe_mode"] = false });
            request["expected_revision"] = new string('0', 64);
            Assert.That((string)VsUpdateSettings.HandleCommand(request)["data"]["error"]["code"], Is.EqualTo("revision_conflict"));
            Assert.That(JToken.DeepEquals(_original, VsSettingsService.Snapshot()), Is.True);
        }

        [TestCase("unknown", true)]
        [TestCase("aot_safe_mode", "false")]
        [TestCase("add_types", "not-an-array")]
        public void UnsupportedOrWronglyTypedChangeRejectsWithoutMutation(string key, object value)
        {
            var changes = new JObject { ["aot_safe_mode"] = !(bool)_original["aot_safe_mode"], [key] = JToken.FromObject(value) };
            var response = VsUpdateSettings.HandleCommand(Request(changes));
            Assert.That((string)response["data"]["error"]["code"], Is.EqualTo("invalid_arguments"));
            Assert.That(JToken.DeepEquals(_original, VsSettingsService.Snapshot()), Is.True);
        }

        [Test]
        public void TypeAndAssemblyAddsAreValidatedAndPreserveExistingOrder()
        {
            var assembly = typeof(Uri).Assembly.GetName().Name;
            var type = typeof(Uri).AssemblyQualifiedName;
            var before = new JObject { ["aot_safe_mode"] = true, ["assemblies"] = new JArray("Existing"), ["types"] = new JArray() };
            var request = VsSettingsPlan.Parse(new JObject { ["expected_revision"] = VsSettingsPlan.Revision(before),
                ["changes"] = new JObject { ["add_assemblies"] = new JArray(assembly), ["add_types"] = new JArray(type) } });
            var after = VsSettingsPlan.Prepare(before, request);
            Assert.That(after["assemblies"].Values<string>(), Is.EqualTo(new[] { "Existing", assembly }));
            Assert.That(after["types"].Values<string>(), Is.EqualTo(new[] { type }));
            request["expected_revision"] = VsSettingsPlan.Revision(after);
            Assert.That(JToken.DeepEquals(after, VsSettingsPlan.Prepare(after, request)), Is.True);
        }

        [Test]
        public void RemovalIsExactAndAllowsCleaningUnavailableAssemblyNames()
        {
            var before = new JObject { ["aot_safe_mode"] = true, ["assemblies"] = new JArray("Missing", "Keep"), ["types"] = new JArray() };
            var request = VsSettingsPlan.Parse(new JObject { ["expected_revision"] = VsSettingsPlan.Revision(before),
                ["changes"] = new JObject { ["remove_assemblies"] = new JArray("Missing") } });
            var after = VsSettingsPlan.Prepare(before, request);
            Assert.That(after["assemblies"].Values<string>(), Is.EqualTo(new[] { "Keep" }));
            Assert.That(before["assemblies"].Count(), Is.EqualTo(2));
        }

        [Test]
        public void EditorTypesAndMissingAssembliesAreRejected()
        {
            foreach (var changes in new[] {
                new JObject { ["add_types"] = new JArray(typeof(EditorWindow).AssemblyQualifiedName) },
                new JObject { ["add_types"] = new JArray(typeof(System.Collections.Generic.List<EditorWindow>).AssemblyQualifiedName) },
                new JObject { ["add_types"] = new JArray(typeof(EditorWindow[]).AssemblyQualifiedName) },
                new JObject { ["add_types"] = new JArray("System.String") },
                new JObject { ["add_assemblies"] = new JArray("No.Such.Assembly") } })
                Assert.That((string)VsUpdateSettings.HandleCommand(Request(changes))["data"]["error"]["code"], Is.EqualTo("unsupported_type"));
        }

        [Test]
        public void DuplicateAndContradictoryListEditsAreRejected()
        {
            foreach (var changes in new[] {
                new JObject { ["add_assemblies"] = new JArray("x", "x") },
                new JObject { ["add_assemblies"] = new JArray("x"), ["remove_assemblies"] = new JArray("x") } })
                Assert.That((string)VsUpdateSettings.HandleCommand(Request(changes))["data"]["error"]["code"], Is.EqualTo("invalid_arguments"));
        }

        [Test]
        public void AliasesWorkButDuplicateOrMissingParametersFail()
        {
            var alias = new JObject { ["Changes"] = new JObject(), ["ExpectedRevision"] = VsSettingsPlan.Revision(_original), ["DryRun"] = true };
            Assert.That((bool)VsUpdateSettings.HandleCommand(alias)["success"], Is.True);
            alias["dry_run"] = true;
            Assert.That((bool)VsUpdateSettings.HandleCommand(alias)["success"], Is.False);
            Assert.That((bool)VsUpdateSettings.HandleCommand(new JObject { ["changes"] = new JObject() })["success"], Is.False);
            Assert.That((bool)VsGetSettings.HandleCommand(new JObject { ["unknown"] = true })["success"], Is.False);
        }

        [UnityTest]
        public IEnumerator ApplyUndoRedoTouchesOnlyRequestedSetting()
        {
            var unrelated = BoltCore.Configuration.GetMetadata(nameof(BoltCoreConfiguration.favoriteMembers)).value.Serialize().json;
            var request = Request(new JObject { ["aot_safe_mode"] = !(bool)_original["aot_safe_mode"] });
            request["dry_run"] = false;
            var response = VsUpdateSettings.HandleCommand(request);
            Assert.That((bool)response["success"], Is.True, response.ToString());
            yield return null;
            Assert.That(BoltCore.Configuration.aotSafeMode, Is.EqualTo(!(bool)_original["aot_safe_mode"]));
            Undo.PerformUndo();
            Assert.That(JToken.DeepEquals(_original, VsSettingsService.Snapshot()), Is.True);
            Undo.PerformRedo();
            Assert.That(BoltCore.Configuration.aotSafeMode, Is.EqualTo(!(bool)_original["aot_safe_mode"]));
            Assert.That(JToken.DeepEquals(_original["types"], VsSettingsService.Snapshot()["types"]), Is.True);
            Assert.That(JToken.DeepEquals(_original["assemblies"], VsSettingsService.Snapshot()["assemblies"]), Is.True);
            Assert.That(BoltCore.Configuration.GetMetadata(nameof(BoltCoreConfiguration.favoriteMembers)).value.Serialize().json, Is.EqualTo(unrelated));
        }

        [Test]
        public void ApplyConflictDoesNotOverwriteAnExternalChange()
        {
            var expected = (JObject)_original.DeepClone();
            expected["aot_safe_mode"] = !(bool)expected["aot_safe_mode"];
            Assert.Throws<VsGraphException>(() => VsSettingsService.Apply(expected, _original));
            Assert.That(JToken.DeepEquals(_original, VsSettingsService.Snapshot()), Is.True);
        }

        [UnityTest]
        public IEnumerator TypeListApplyAndUndoPreserveOtherSettings()
        {
            var name = typeof(Uri).AssemblyQualifiedName;
            var contains = _original["types"].Values<string>().Contains(name);
            var request = Request(new JObject { [contains ? "remove_types" : "add_types"] = new JArray(name) });
            request["dry_run"] = false;
            var response = VsUpdateSettings.HandleCommand(request);
            Assert.That((bool)response["success"], Is.True, response.ToString());
            yield return null;
            Assert.That(BoltCore.Configuration.typeOptions.Contains(typeof(Uri)), Is.EqualTo(!contains));
            Assert.That(JToken.DeepEquals(_original["assemblies"], VsSettingsService.Snapshot()["assemblies"]), Is.True);
            Assert.That(BoltCore.Configuration.aotSafeMode, Is.EqualTo((bool)_original["aot_safe_mode"]));
            Undo.PerformUndo();
            Assert.That(JToken.DeepEquals(_original, VsSettingsService.Snapshot()), Is.True);
        }
    }
}
