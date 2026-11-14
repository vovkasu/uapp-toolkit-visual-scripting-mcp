# UAppToolKit Visual Scripting MCP

Editor-only commands for reading graphs, diagnosing Play Mode behavior, targeted
editing, cloning, object bindings, and Visual Scripting project settings. Written
in C# for .NET Standard 2.1, using Unity Visual Scripting with optional CoplayDev MCP and Unity CLI / Pipeline
adapters. Both adapters use the same core; there is no separate runtime agent.

Verified base: Unity 6000.3.16f1 and VS 1.9.11. Adapter versions and setup are
documented in [ADAPTERS.md](Documentation~/ADAPTERS.md).
Tool code lives in a separate Editor-only assembly under `Editor/`. Generated game
Unit classes must live outside Editor folders; the `.cs.txt` template does not compile.

## Installation in another project

Requires Unity 6000.3+ with API Compatibility Level set to .NET Standard 2.1.
The tested combination is Unity 6000.3.16f1, Visual Scripting 1.9.11,
Newtonsoft.Json 3.2.2, and the selected bridge versions documented below. Other versions need verification;
debug data inspection accesses internal Visual Scripting APIs through reflection.

Choose either CoplayDev Unity MCP, Unity CLI with `com.unity.pipeline`, or both.
Install the chosen bridge separately; neither bridge is a mandatory dependency.
Visual Scripting and Newtonsoft.Json remain regular UPM dependencies. Without a
bridge, the core compiles and its C# API works, but no transport commands are registered.
Adapters enable automatically through asmdef versionDefines/defineConstraints; no
manual Player Settings symbols are needed. See [ADAPTERS.md](Documentation~/ADAPTERS.md).

Copy **the entire package folder** to `Packages/com.uapp-toolkit.visual-scripting-mcp`
for an embedded installation. Once the source is published, the package can also
be installed using a Git URL with `?path=<package path>#<tag>`; the repository owner
provides the URL. Remove the old tool copy from `Assets/Editor` when migrating:
keeping both copies causes type conflicts. The package does not depend on any
particular game's classes, scenes, tags, or settings.

Tests are separate from the production assembly and require Unity Test Framework
(install it separately when testing without a bridge that provides it). For a Git/local
dependency, add the package name to the `testables` array in the project's
`Packages/manifest.json`. Embedded package tests are available in Editor Test Runner.
The validation runner and fixtures live only in the test assembly.

Namespace: `UAppToolKit.VisualScriptingMcp.Editor`.
Response schema: `uapp-toolkit.visual-scripting-mcp/1`.
The 14 CoplayDev command names and shared request structures are unchanged.
Unity CLI exposes the same operations under the `vs` group.
Release history: [CHANGELOG.md](CHANGELOG.md).

In a new project, open a Visual Scripting graph and wait for initial setup before
working with settings. Until then, settings commands return
`visual_scripting_not_initialized`; reading does not start node generation implicitly.

## Connecting through CoplayDev

1. Install the dependencies and package as described above; wait for successful compilation.
2. Open **Window → MCP for Unity**, start the existing HTTP Local server and
   Editor connection. The tested endpoint is `http://127.0.0.1:8080/mcp`;
   check the current panel rather than assuming this port is always used.
3. Connect the MCP client to the endpoint. If several Editors are running, select
   the target project's instance with `set_active_instance` using the current instance list.
4. Use resources/list to discover and read the advertised project, Editor state,
   and custom tool resources. For the tested bridge these are
   `mcpforunity://project/info`, `mcpforunity://editor/state`, and
   `mcpforunity://custom-tools`. Do not reuse stale instance/session IDs.
5. Verify the project path and the 14 commands below, then execute:

```json
{"tool_name":"vs_diagnostics","parameters":{}}
```

These are arguments to **execute_custom_tool**. VS commands are registered as
project-scoped custom tools; the client may not display them as separate top-level
tools. Having Unity Pipeline installed does not establish a CoplayDev connection.
If custom tools are missing, check compilation/Console, the MCP panel connection,
and reload the catalog. Reload may briefly disconnect the bridge: wait for readiness
and reconnection instead of blindly retrying a write.

For a manual JSON-RPC client, use tools/call with name=execute_custom_tool after
standard MCP initialize + notifications/initialized and the current session header.
Do not commit local client settings, session IDs, or logs.

## Connecting through Unity CLI / Pipeline

Install Unity CLI and `com.unity.pipeline` separately. Once the Editor is ready:

```bash
unity command vs diagnostics --project-path /path/to/project --format json
unity command vs list_graphs --parameters '{"scope":"assets","folder":"Assets"}' --project-path /path/to/project --format json
```

Run `unity mcp --project-path /path/to/project` for MCP clients. Use its discovered
command schema: operations map from `vs_read_graph` to `vs read_graph` on the CLI,
with the shared request under `parameters` (JSON text in the MCP schema).
Do not assume CoplayDev's
`execute_custom_tool` exists on the Unity CLI bridge.

Pipeline wraps the shared response in `result`. Check both transport success and
`result.success`; `result.data.error` contains VS operation errors. A handled
validation error can have transport success=true and operation success=false.
CLI 1.0.0 prints the core response directly with `--format json`. CLI beta.15
places it in `data.result`. The MCP stdio server returns core JSON in `content`.
See [ADAPTERS.md](Documentation~/ADAPTERS.md).

## Commands

| Command | Purpose | Mode |
| --- | --- | --- |
| vs_diagnostics | Project, versions, Editor state, capabilities | Edit / Play |
| vs_list_graphs | Assets, prefab owners, loaded scenes | Edit |
| vs_read_graph | Nodes, ports, connections, nesting, definitions, edit_revision | Edit |
| vs_validate_graph | Structural findings and inspection completeness | Edit |
| vs_runtime_status | Machines, session IDs, existing graph data | Play |
| vs_runtime_variables | Live variables for a selected machine and scopes | Play |
| vs_trace_start | Start bounded observation of a machine | Play |
| vs_trace_read | Read buffered records and losses by cursor | Until trace reset |
| vs_trace_stop | Unsubscribe and retain the available buffer | Until trace reset |
| vs_edit_graph | Preview/apply a supported Flow Graph edit batch | Edit |
| vs_clone_graph | Create an asset with an explicit macro policy | Edit |
| vs_bind_object | Assign a macro or Object Variable Unity reference | Edit |
| vs_get_settings | AOT, assembly/type lists, revision, regeneration status | Edit |
| vs_update_settings | Targeted settings edits and explicit node regeneration | Edit |

Edit means idle Edit Mode. Compilation, imports, and transitions can return
editor_busy. Use the existing Unity bridge/Editor to control Play Mode, scenes,
saving, and Custom Events; VS read commands do not perform these actions.

## Read and explain a tutorial

Find the asset or owner first; a name is not an ID:

```json
{"tool_name":"vs_list_graphs","parameters":{"scope":"assets","folder":"Assets/Tutorials","page_size":50}}
{"tool_name":"vs_read_graph","parameters":{"target":"<target from records>","max_depth":2,"page_size":100}}
{"tool_name":"vs_validate_graph","parameters":{"target":"<same target>","max_depth":2,"page_size":100}}
```

Responses include `success`, `data.result`, `data.warnings`, `data.complete`,
`data.next_cursor`, and `data.error`. If next_cursor is present, repeat the same
request with cursor until all pages are read. When complete=false without a cursor,
read warnings: a traversal/size limit was reached, so the entire graph was not
inspected. Use the reader's `parent_element_guids` to select a nested graph.

Explain entry events → control connections → conditions/loops → effects and
dependencies. Identify macro targets, variable scopes, C# calls, and unread branches.
`validation_summary.status=valid` does not guarantee correct game logic: a structurally
valid event name can still contain a typo.

## Diagnose a stall in Play Mode

Before entering Play Mode, read the graph and retain its IDs. After entering,
rediscover the machine using scene context rather than its name alone:

```json
{"tool_name":"vs_runtime_status","parameters":{}}
{"tool_name":"vs_runtime_variables","parameters":{"instance_id":123,"play_session_id":"<from status>","scope":"object"}}
{"tool_name":"vs_trace_start","parameters":{"instance_id":123,"play_session_id":"<from status>","event_names":["AdvanceTutorial"],"capacity":512}}
{"tool_name":"vs_trace_read","parameters":{"trace_id":"<from start>","page_size":20}}
{"tool_name":"vs_trace_stop","parameters":{"trace_id":"<same trace_id>"}}
```

123 is a placeholder; use the actual component instance_id. Start tracing **before**
the action you want to reproduce. Read additional pages using cursor and account
for has_more/lost_records and observation limits. Use the existing read_console
command; capture the error baseline before clearing anything.

Check whether the machine is enabled/active/listening, runtime data exist, references
are assigned, and the event reached the intended owner. Compare its name with the
graph's default/name input. Look for exceptions and node GUIDs in debug samples.

A Custom Event receipt does not prove its graph handler ran. An absent record does
not prove the graph is waiting for an event. Tracing samples debug data rather than
recording every transition. Play/Stop and reload invalidate runtime IDs and traces.

## Repair, save, and verify again

Exit Play Mode. Read the graph again and use **edit_revision**, not the page revision.
For example, preview a Custom Event name correction:

```json
{"tool_name":"vs_edit_graph","parameters":{"target":"<asset target>","expected_revision":"<edit_revision>","operations":[{"op":"set_default","guid":"<CustomEvent GUID>","port":"name","value":{"type":"string","value":"AdvanceTutorial"}}]}}
```

The default is dry_run=true. Review the diff and validation, then submit the same
request with dry_run=false. On revision_conflict, reread and prepare another preview.
Validate again, check Undo/Redo, and save **only** the changed asset/scene through
standard Unity API/UI. Edit does not save automatically. Start a new Play session,
rediscover IDs, repeat the action, and verify the final state. After a write timeout,
read the state first: the operation may have completed despite the lost response.

## Copies, bindings, and settings

- `vs_clone_graph`: target, destination in an existing Assets folder, and required
  macro_policy=`share_macros` or `embed_macros`. Preview first, then apply using
  its revision. Only the new asset is saved; asset creation is not covered by
  Unity Undo. Non-graph asset references remain shared even with embed_macros.
- `vs_bind_object`: a machine component target, kind=`graph`/`variable`, and a
  reference GlobalObjectId; variable also requires variable_name. Explicit JSON
  null clears the value. Uses an existing Variables component without adding one.
  Apply requires its preview revision; assignments support Undo without autosaving.
- `vs_get_settings` → `vs_update_settings` with changes and expected_revision.
  The package saves settings asynchronously; scheduled is not saved. Let the
  Editor process UI updates, activating its window if necessary, and verify the
  file before closing/reloading. Regenerate_nodes=true starts an expensive job;
  poll regeneration until completed/failed/interrupted instead of repeating apply.
  Settings Undo lasts until domain reload and does not undo database generation.
- New C# nodes: see [CUSTOM_NODES.md](Documentation~/CUSTOM_NODES.md) for the template
  and compilation → node library → ports → Play Mode verification workflow.

## Limits and validation

Unsupported: arbitrary C# types in vs_edit_graph, State Graph topology changes,
prefab asset/stage editing through edit, persistent edits in Play Mode, scene
references in prefab assets, and cross-scene object bindings. Supported contexts
differ between edit/clone/bind; consult [CONTRACT.md](Documentation~/CONTRACT.md)
rather than transferring one command's permissions to another.
The tool does not promise full execution tracing, correct third-party graphs,
or a successful game build.

Run tests in Unity Test Runner → EditMode, filtered by
`UAppToolKit.VisualScriptingMcp.Editor.Tests`. VsWorkflowTests enters/exits Play Mode
twice in an isolated scene, restored by the test runner. Save user work first.
Tests are in a separate Editor-only test assembly. To preserve native XML/JSON
results across reload, call
`UAppToolKit.VisualScriptingMcp.Editor.Tests.VsValidationRunner.Run()`.
The MCP job status may remain stale after reload. Tests create their own temporary
graphs and do not need game assets. Reports are saved to
`Library/VisualScriptingMcp/Validation-<run>.json` and `.xml`.
