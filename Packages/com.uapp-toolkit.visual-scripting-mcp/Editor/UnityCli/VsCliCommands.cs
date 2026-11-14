using Newtonsoft.Json.Linq;
using Unity.Pipeline.Commands;
using Core = UAppToolKit.VisualScriptingMcp.Editor;

namespace UAppToolKit.VisualScriptingMcp.UnityCli.Editor
{
    public static class VsCliCommands
    {
        private const string Group = "vs";

        [CliCommand(Group, "get_settings", "Read supported Visual Scripting project settings, revision and node regeneration status. No mutation.", MainThreadRequired = true)]
        public static JObject GetSettings(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md")] JObject parameters = null)
            => Core.VsGetSettings.HandleCommand(parameters);

        [CliCommand(Group, "update_settings", "Preview or apply explicit VS project settings changes. Apply requires settings revision. Optional regenerate_nodes schedules node database rebuild; poll vs_get_settings. Does not generate C#.", MainThreadRequired = true)]
        public static JObject UpdateSettings(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsUpdateSettings.HandleCommand(parameters);

        [CliCommand(Group, "runtime_status", "Read existing VS machine state in Play Mode. Omit instance_id to discover machines; pass returned instance_id AND play_session_id to inspect one. Does not activate machines.", MainThreadRequired = true)]
        public static JObject RuntimeStatus(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md")] JObject parameters = null)
            => Core.VsRuntimeStatus.HandleCommand(parameters);

        [CliCommand(Group, "runtime_variables", "Read existing runtime variables of a selected machine. Requires instance_id and play_session_id. Scopes: all, graph, object, scene, application, saved. No fallback to definitions and no creation or saving.", MainThreadRequired = true)]
        public static JObject RuntimeVariables(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsRuntimeVariables.HandleCommand(parameters);

        [CliCommand(Group, "trace_start", "Start one bounded Editor-only trace for a runtime machine. Samples existing debug data and observes owner Custom Events. Not a complete execution history.", MainThreadRequired = true)]
        public static JObject TraceStart(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsTraceStart.HandleCommand(parameters);

        [CliCommand(Group, "trace_read", "Read bounded trace records after a cursor. Reports overwritten records and observation limits. Read does not consume records.", MainThreadRequired = true)]
        public static JObject TraceRead(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsTraceRead.HandleCommand(parameters);

        [CliCommand(Group, "trace_stop", "Stop trace subscriptions. Buffer remains readable until another trace, Play transition or reload. Stop is idempotent.", MainThreadRequired = true)]
        public static JObject TraceStop(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsTraceStop.HandleCommand(parameters);

        [CliCommand(Group, "diagnostics", "Read-only Visual Scripting MCP capabilities and live Editor status. Takes no parameters; does not run graphs or enter Play Mode.", MainThreadRequired = true)]
        public static JObject Diagnostics(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md")] JObject parameters = null)
            => Core.VsDiagnostics.HandleCommand(parameters);

        [CliCommand(Group, "edit_graph", "Edit a Flow Graph in idle Edit Mode. Defaults to dry run. Requires edit_revision from vs_read_graph. Validates on a copy, preserves GUIDs, registers Undo and leaves the owner dirty without saving. Shared macros must be targeted directly.", MainThreadRequired = true)]
        public static JObject EditGraph(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsEditGraph.HandleCommand(parameters);

        [CliCommand(Group, "clone_graph", "Preview or create a new graph asset from an existing graph. Explicit share_macros or embed_macros policy. Apply requires expected_revision from this command preview. Never overwrites assets.", MainThreadRequired = true)]
        public static JObject CloneGraph(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsCloneGraph.HandleCommand(parameters);

        [CliCommand(Group, "bind_object", "Preview or assign a macro graph or Object Variable reference on a scene/prefab machine. Apply requires expected_revision from binding preview. Undo supported; assignments are not auto-saved.", MainThreadRequired = true)]
        public static JObject BindObject(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsBindObject.HandleCommand(parameters);

        [CliCommand(Group, "validate_graph", "Read-only structural graph validation in idle Edit Mode. Same arguments as vs_read_graph. Reports errors separately from runtime dependencies and incomplete inspection. Does not execute or repair graphs.", MainThreadRequired = true)]
        public static JObject ValidateGraph(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsValidateGraph.HandleCommand(parameters);

        [CliCommand(Group, "list_graphs", "Read-only graph discovery in assets, prefabs or loaded scenes. Returns stable target IDs. Edit Mode only. Unloaded scenes are never opened.", MainThreadRequired = true)]
        public static JObject ListGraphs(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md")] JObject parameters = null)
            => Core.VsListGraphs.HandleCommand(parameters);

        [CliCommand(Group, "read_graph", "Read graph definition as paged records: nodes, ports, connections, variables and nested graphs. No Flow evaluation. Edit Mode only.", MainThreadRequired = true)]
        public static JObject ReadGraph(
            [CliArg("parameters", "VS request as JSON; MCP accepts JSON text. Fields: Documentation~/CONTRACT.md", Required = true)] JObject parameters)
            => Core.VsReadGraph.HandleCommand(parameters);
    }
}
