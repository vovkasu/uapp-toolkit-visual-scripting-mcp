# Visual Scripting MCP v1 contract

## Implemented commands

The contract below describes core operations shared by both adapters. CoplayDev
uses `vs_*` names; Unity CLI uses group `vs` with the prefix removed and accepts
the same request as a `parameters` object. Pipeline places the core response in
its `result`; transport success alone does not imply operation success. See
[ADAPTERS.md](ADAPTERS.md) for installation and response mapping.

`vs_diagnostics` is a read-only command with no parameters. With CoplayDev, call it through
`execute_custom_tool(tool_name="vs_diagnostics", parameters={})` when the server
registers it as a project-scoped tool. Check `mcpforunity://custom-tools` first.
Both `null` and `{}` are accepted; any supplied parameter returns `invalid_arguments`.

`VsDiagnostics` validates the request, `VsDiagnosticsService` collects information,
and `VsResponse` builds the common response. Production code and tests belong to
separate Editor-only assemblies: `UAppToolKit.VisualScriptingMcp.Editor` and
`UAppToolKit.VisualScriptingMcp.Editor.Tests`. Optional transport assemblies are
`UAppToolKit.VisualScriptingMcp.CoplayDev.Editor` and
`UAppToolKit.VisualScriptingMcp.UnityCli.Editor`. The core has no bridge references.
The tool has no runtime components. Diagnostics do not modify scenes. Write commands
modify only the explicitly selected owner or create a new asset. The tool keeps a
session token and bounded trace buffer in the Editor and subscribes to Play Mode
transitions and assembly reloads.

## Common response

- `success`: whether the command succeeded.
- `error`: an error string or null, for compatibility with the MCP bridge.
- `data.schema_version`: `uapp-toolkit.visual-scripting-mcp/1`.
- `data.editor`: current mode (edit/play), is_paused, is_compiling, is_updating,
  and is_play_mode_transition_pending. This is a snapshot at execution time,
  not a promise that the Editor will be ready for the next command.
- `data.capabilities`: `vs_diagnostics`, `vs_list_graphs`, `vs_read_graph`,
  `vs_validate_graph`, `vs_runtime_status`, `vs_runtime_variables`, `vs_trace_start`,
  `vs_trace_read`, `vs_trace_stop`, `vs_edit_graph`, `vs_clone_graph`, `vs_bind_object`,
  `vs_get_settings`, `vs_update_settings`.
- `data.warnings`: an array of warning code strings.
- `data.complete`: completeness within the requested scope, not success.
  Diagnostics return true because their response has a fixed set of fields.
- `data.next_cursor`: null or an opaque cursor for the next page.
- `data.result`: command data, or null on failure.
- `data.error`: `{code, message}` or null. Invalid argument values are not echoed.

Diagnostics return the Assets path, Unity/VS versions, active scene path and dirty
flag, and the presence of a debug binding. `full_execution_trace_supported=false`:
a binding does not imply full tracing support. The VS version can be null if
Unity cannot associate the assembly with a package.

## Settings and C# nodes (stage 9)

Commands require idle Edit Mode. They support a limited set of project settings in
`ProjectSettings/VisualScriptingSettings.asset`. They do not modify EditorPrefs,
switch the build target, migrate package versions, or reset settings to defaults.

`vs_get_settings` accepts no parameters; null/{} are valid. It returns `settings`:
`aot_safe_mode` (bool), `assemblies` (assembly names), `types` (assembly-qualified
type names), a SHA-256 `revision` of these three values, and `regeneration`.
Existing list order is preserved. Reading does not generate nodes.

`vs_update_settings` parameters:

- `expected_revision`: required, from get_settings or a preview. This is a settings
  revision, not a graph revision. A mismatch returns revision_conflict before writing.
- `changes`: an object supporting `aot_safe_mode` (bool), `add_assemblies`,
  `remove_assemblies`, `add_types`, and `remove_types` (string arrays).
  Omitted values are preserved. Adding an existing item or removing a missing item
  is a no-op. Duplicates and adding/removing the same item in one request are rejected.
- `dry_run`: defaults to true; set false to apply after reviewing the preview.
- `regenerate_nodes`: defaults to false. True explicitly requests a full rebuild
  after applying changes. `changes={}` runs it without changing settings.

Top-level fields also accept PascalCase aliases from the MCP schema and `dryRun`.
Keys inside changes use snake_case. Limits: 32K request characters and up to 64
entries in each array. Added assemblies must be loaded runtime assemblies with
exact names. Types must resolve using their exact AssemblyQualifiedName; open
generics, pointer/by-ref/void types, and Editor dependencies in the type, array
element, or generic arguments are rejected. Missing old assemblies may be explicitly
removed; other entries are preserved. A type whose assembly is not in the node
library produces a warning; its assembly is not added automatically. Existing
Editor entries are not silently removed.

Preview returns a per-field before/after `diff`, `revision`, `result_revision`,
and `warnings`; apply also returns `applied`. The entire request is validated first.
Writes use PluginConfigurationItemMetadata.value/Save and Codebase.UpdateSettings.
Changed settings are saved synchronously through Unity's native serialized-file
API, using Visual Scripting's existing backing DictionaryAsset. Apply returns
`save_status=saved` only after an independent disk read confirms the changed
values. Preview and no-op requests return `save_status=not_requested`.
The same persistence path is used by Undo/Redo, without waiting for a GUI update,
window focus or batchmode. A guarded compatibility check accesses the internal
VS backing-asset property before mutation; metadata.Save retains VS's normal
recovery when Unity has unloaded that asset. Other keys and object references
are preserved in the whole asset; serialized JSON is not manually rewritten.

Unity Undo/Redo supports affected values in the current domain through an
Editor-only token and the same settings API. It does not undo C# changes or the
node database. Before reload, only these tokens' history is cleared. If an affected
setting was changed externally, Undo does not overwrite it: it logs a warning and
removes the conflicting token. Changes to other fields are preserved. Another
node rebuild may be needed after Undo.

`regeneration` states: `not_requested`, `pending`, `running`, `completed`, `failed`,
or `interrupted`. A job has `job_id`, `settings_revision`, and later errors/warnings.
It starts once on an Editor update. UnitBase.Rebuild blocks the main thread
synchronously, so bridge requests may wait or time out while it runs. Poll
get_settings instead of submitting another apply. New writes are rejected while
a job is pending/running. Compilation errors block apply. If the Editor is busy
before the job starts, the job fails and requires another explicit request.
Reload marks unfinished jobs interrupted. Completed means the API returned without
error logs and the database exists, not that Play Mode or a Player build succeeded;
verify the required node. Status describes the last job, whose revision may differ
from current settings.

Errors: invalid_arguments, revision_conflict, unsupported_type, editor_busy,
compilation_failed, settings_failed, visual_scripting_not_initialized. Settings
saving and database generation are separate operations: rebuild failure does not
roll back settings already applied.

If Visual Scripting has not been initialized in a new project, settings commands
return `visual_scripting_not_initialized`. Open a VS graph and wait for package
setup before retrying. Reading does not silently activate VS or generate nodes.

See [CUSTOM_NODES.md](CUSTOM_NODES.md) for the Unit template, compilation checks,
port verification, and Play Mode testing. There is no separate C# generation command.

## Cloning and object bindings (stage 8)

Both commands require idle Edit Mode. `dry_run=true` by default. Obtain a preview,
then repeat with `dry_run=false` and `expected_revision=data.result.revision`
**from that command's preview**. This is not the reader's edit_revision. The revision
covers request parameters and affected state; any change requires another preview.
Unknown or duplicate parameters are rejected. The request limit is 8K characters.
PascalCase aliases from the MCP schema and `dryRun` are supported.

### vs_clone_graph

- `target`: the source GlobalObjectId from discovery/reader.
- `parent_element_guids`: optional path to a selected nested graph.
- `destination`: a new `.asset` in an existing `Assets/` folder.
  Existing files, meta files, and folders are not overwritten; traversal is rejected.
- `macro_policy`: required; either `share_macros` or `embed_macros`.

FlowGraph/StateGraph is copied through VS Serialize/Deserialize into a new
ScriptGraphAsset/StateGraphAsset. The new asset gets a new GUID; internal element
GUIDs and connections are preserved. The source is unchanged. Embedded graphs are
copied. `share_macros` preserves active nested macro references; `embed_macros`
recursively replaces them with independent embedded copies. Cycles are rejected
when making independent copies. Other asset references, including variable values,
remain shared: the policy does not duplicate every project dependency.
Scene, temporary, and destroyed references are rejected in the new asset.

Preview returns `source`, `destination`, `macro_policy`, `graph_type`, `copied_graphs`,
`copied_elements`, `shared_macros`, and `revision`. The revision covers the prepared
copy, including macro contents for embed_macros. With share_macros, shared macro
asset contents remain external dependencies. Apply additionally returns `new_target`,
`edit_revision`, and `saved=true`. Only the new asset is saved. Asset creation does
not support Unity Undo (`asset_creation_undo_supported=false`); assignment Undo
is supported separately.

Validation preserves serialized structure but does not guarantee source graph
execution (`validation=structure_preserved_not_execution_validated`). Use
vs_validate_graph and Play Mode verification. Limits: depth 8, 256 copied graphs,
20000 elements, and 2M serialized characters per graph.

### vs_bind_object

- `target`: the GlobalObjectId of a ScriptMachine/StateMachine component.
- `kind=graph`: `reference` is the GlobalObjectId of a persistent macro of the
  matching type. The machine switches to Macro; its previous background embedded
  graph is preserved.
- `kind=variable`: `variable_name` is an Object Variable name; `reference` is a
  Unity object's GlobalObjectId. Explicit JSON null clears the value while keeping
  its declaration. A variable may be created, but the Variables component must
  already exist. The command does not add components or overwrite scalar variables.
  The declared typeHandle is checked for compatibility.

Supported owners are machines in saved, loaded scenes (including prefab instances)
and editable regular prefab assets inside Assets. Prefab Stage, model/variant
prefabs, and nested instance targets inside prefab assets are unsupported.
Persistent assets are valid references. Scene references are allowed only within
the owner's scene. Scene references in prefab assets and cross-scene references
are rejected; scenes are not opened implicitly.

Preview returns `before`, `after`, `changed`, and `revision`; apply adds `applied`.
A separate Undo group restores assignments. Serialization, dirty flags, and prefab
instance overrides are updated. `saved=false`: save the specific scene/prefab
separately. The revision covers the entire nest or Object declarations. Exceptions
roll back only this operation's assignments; the scene may remain dirty.
The serialized binding state limit is 2M characters.

Errors: `invalid_arguments`, `editor_busy`, `not_found`, `read_only`,
`revision_conflict`, `unsupported_context`, `type_mismatch`,
`cross_context_reference`, `destination_exists`, `unsupported_graph`,
`missing_graph`, `graph_cycle`, `clone_limit`, `composition_limit`,
`clone_not_lossless`, `composition_failed`.

## Targeted editing (stage 7)

`vs_edit_graph` requires idle Edit Mode. Parameters:

- `target`: the GlobalObjectId of a graph asset or scene machine from discovery.
- `parent_element_guids`: optional path to an embedded Flow Graph.
- `expected_revision`: **edit_revision** from `vs_read_graph.data.result`, not
  the page revision. It covers the owner's entire root graph and its Unity
  references, regardless of max_depth/page_size. A shared macro along the path
  is not edited.
- `dry_run`: defaults to true; `dryRun` and `DryRun` are also supported. Review the
  preview, then repeat the same batch with false and the same revision.
- `operations`: 1–32 operations; up to 64K request characters. Operation fields
  use snake_case. Top-level parameters also accept PascalCase MCP schema aliases.

Supported operations:

| op | Fields besides op | Effect |
| --- | --- | --- |
| add_node | guid, type, x, y, config? | Create a node with the supplied new GUID |
| configure_node | guid, config | Change supported node settings and redefine its ports |
| move_node | guid, x, y | Change position |
| set_default | guid, port, value | Set the default of an existing value input |
| set_variable | name, value | Create/update a Graph Definition variable |
| remove_variable | name | Explicitly remove a Graph Definition variable |
| connect | guid, kind, source_node, source_port, destination_node, destination_port | Create a control/value connection |
| disconnect | guid | Explicitly remove a connection |
| remove_node | guid | Remove a node; disconnect all its connections using separate operations first |

`value` always uses the form `{"type":"int","value":7}`. Supported types: null,
string (up to 4096 characters), bool, int, float, double. NaN/Infinity are rejected.
A default's type must match the port type (or be compatible with object), without
implicit conversion. This command does not write Unity references, complex objects,
or Object/Scene/Application/Saved variables.

Supported add_node types: If, Null, ForEach, WaitForSeconds, CustomEvent,
TriggerCustomEvent, GetVariable, SetVariable, Literal, GetMember, SetMember,
InvokeMember. Arbitrary C# types are not constructed. Supported config:

- GetVariable/SetVariable: `{"kind":"Object"}`; VS VariableKind values are accepted.
- CustomEvent/TriggerCustomEvent: `{"argument_count":0}` (0–10).
- Literal: `{"value":{"type":"int","value":7}}`. An existing literal's type
  cannot change. Use the Null node for null.
- Member nodes: `{"member":{"type":"System.String","name":"Length"}}`.
  For methods, supply `parameter_types` as an array of type names, including an
  empty array for a method without arguments. Assembly-qualified names are accepted.
- Other listed nodes have no supported config fields; use set_default for their
  inputs. Defaults are set in separate operations after creation. The command
  does not invoke member fields or methods.

Example preview after reading a graph:

```json
{
  "target": "<target from discovery>",
  "expected_revision": "<edit_revision from vs_read_graph>",
  "dry_run": true,
  "operations": [
    {"op":"set_variable","name":"delay","value":{"type":"float","value":0.5}}
  ]
}
```

Before changing the owner, the entire root graph is copied through VS
Serialize/Deserialize, checked for an identical round-trip, and the full operation
batch is applied to the copy. The selected Flow Graph then undergoes structural
validation. Implicit loss of existing GUIDs is forbidden. Incompatible port types,
occupied inputs/outputs, and missing GUIDs/ports are rejected; existing connections
are not automatically replaced. Potential runtime conversions may produce validator
warnings. This is not an execution guarantee.

The response includes a diff with added/removed/modified and before/after values,
previous_revision, proposed_revision, current edit_revision, dry_run, applied,
saved=false, and validation scoped to the selected Flow Graph. Nested graphs make
that validation coverage incomplete; run vs_validate_graph at the required depth
separately. Limits: 2M serialized characters and a 192 KiB diff.

Apply registers a separate Undo group with RegisterCompleteObjectUndo, serializes
the owner, and records prefab overrides for scene instances. Commit failure
restores the graph and rolls back only its own Undo group. An unexpected Unity
callback failure may leave the scene dirty, but no file is saved. Preflight failure
does not modify the owner at all. Save the specific asset or scene through Editor
API/UI after reviewing the result. The command does not call global SaveAssets or
SaveOpenScenes.

Supported v1 contexts: saved graph assets inside Assets and embedded machines in
regular loaded scenes, including scene prefab instances. Shared macros require a
direct asset target. Prefab assets, Prefab Stage, graph source changes, State Graph
topology, adding State/Transition/Subgraph elements, and creating C# types are
unsupported. Embedded Flow Graphs inside State Graphs are supported.

Errors: invalid_arguments, editor_busy, not_found, revision_conflict,
unsupported_context, unsupported_graph, shared_macro, read_only, edit_limit,
clone_not_lossless, implicit_removal, validation_failed, edit_failed.
Reread on a revision conflict. A clone_not_lossless response leaves the graph unedited.

## Bounded tracing (stage 6)

Only one trace can be active per Editor, in Play Mode. Tracing does not modify or
execute graphs or force debugging on. Read Console through the existing
`read_console`; the tool does not create another Console subscription.

- `vs_trace_start`: requires `instance_id` and `play_session_id` from runtime
  discovery. Optional: `parent_element_guids`, `capacity` (64–2048, default 512),
  `event_names` (up to 32 strings of 256 characters; an empty list means all Custom
  Events on the owner's GameObject). Returns `trace_id` and the first page.
  A second active trace returns `trace_already_running`; stop the first one first.
- `vs_trace_read`: requires `trace_id`; optional `cursor` and `page_size` (1–20,
  default 20). Reading does not remove records. `data.next_cursor` / `result.cursor`
  are retained even on empty pages; use them for the next poll. `has_more` means
  more records are already buffered, not that future events are guaranteed.
- `vs_trace_stop`: requires `trace_id`; repeated stops are valid. The buffer remains
  readable until another start, a Play transition, or reload. Continue from your
  last read cursor; stop returns the first page.
- PascalCase MCP schema aliases are supported. Unknown/duplicate arguments are
  rejected. Old trace IDs return `stale_trace`; another trace's cursor returns
  `stale_cursor`. Start also uses runtime command errors.

Record kinds: `trace_started`, `machine_state`, `debug_coverage`, `debug_sample`,
`custom_event`, `trace_stopped`, and `observer_error` on observer failure.
Each record has sequence, observed_frame, and observed_time (Editor timeSinceStartup).
Owner and Play session context are shared by the response. debug_sample contains
the path, element GUID, available lastInvoke data, exceptions, and connection nodes
and ports. VS last_invoke_time uses a different clock from observed_time.
Custom Event payloads are not copied: only the name and argument count are recorded.

Polling runs through EditorApplication.update at most once per 0.1 seconds and
records snapshot changes, not every invocation. Initial data are marked baseline
and may describe execution before start. A narrow reflection adapter reads existing
VS 1.9.11 debug dictionaries only; bindings are not replaced and GetOrCreate is not
called. Unsupported layouts or absent data are explained in debug_coverage;
read failures stop the observer. The debug path includes the selected graph and its
descendants, limited to 512 elements / 512 contexts and 8 levels below the selected
graph. Exceeding a limit is marked truncated.

Records are limited to 8 KiB and pages to 20 records. Oversized records are replaced
with an omitted marker. The ring buffer evicts old records: lost_records counts
losses relative to the supplied cursor, while dropped_total counts from trace start.
Overflow produces `trace_records_lost`. complete is always false;
full_execution_trace=false and partial_observation describe observation limits.

Do not infer a complete invocation order, exact wait point, or the reason an event
is absent. Repeated executions between polls collapse into one observation.
Custom Events are filtered by object, not nested graph, and may have no handler.
An exception in an earlier EventBus handler can prevent the observer from receiving
the event. The last node is not necessarily waiting for an event. An exception in
debug data is a retained observation, not necessarily a new error; the Unity Editor
may clear it before a poll.

Exiting Play Mode, starting compilation, and beforeAssemblyReload unsubscribe and
clear the buffer. Owner destruction unsubscribes but retains the buffer with
stop_reason=owner_destroyed. There are no global runtime components or disk trace
writes. Rediscover runtime owners and start a new trace after a session change.

Practical diagnosis: run structural `vs_validate_graph` in Edit Mode, inspect
runtime status/variables for assigned references, trace observed events, states,
and exceptions, then inspect Console. An event's absence during observation does
not prove the tutorial is waiting for it.

## Runtime inspection (stage 5)

Both commands require stable Play Mode, including pause. Errors: `not_playing`,
`editor_busy`, `invalid_arguments`, `not_found`, `stale_reference`, `stale_cursor`,
`inspection_failed`. The common response adds `data.play_session_id` and
`data.observed_frame`. This is a snapshot, not execution history.

- `vs_runtime_status({})` finds Script/State Machines in loaded scenes, including
  inactive objects. Returns the component instance_id, session token, scene,
  active/enabled flags, graph, runtime data presence, is_listening, and active State GUIDs.
- To select a machine, supply `instance_id` and `play_session_id` from discovery.
  Do not use a GameObject or asset ID in place of the component ID. IDs are valid
  only in the current Play session and until destruction; rediscover after reload.
- `vs_runtime_variables` requires the same pair. `scope`: all (default), graph,
  object, scene, application, saved. `variable_scope.available=false` means existing
  runtime declarations are absent; definition values are not substituted.
- `parent_element_guids`: a nested graph path from `vs_read_graph`, accepted only
  with a selected machine. For variables it affects Graph scope. Reads use
  TryGetChildGraphData/TryGetElementData and do not create uninitialized contexts.
- `page_size` (1–200, default 50) and `cursor` use the shared response limits.
  Value changes between pages cause stale_cursor; pause the Editor and restart
  reading if needed. observed_frame is not part of the revision.
- PascalCase MCP schema names (`InstanceId`, `PlaySessionId`, `Scope`,
  `ParentElementGuids`, `PageSize`, `Cursor`) are also supported.

Unity references include instance_id, type, scene, persistent_asset, and asset_path.
A destroyed reference is `{missing_unity_object:true}`; ordinary null stays null.
Object scope uses the existing Variables component. Scene scope enumerates existing
SceneVariables in the owner's scene. Its owner_instance_id identifies SceneVariables;
for global scopes it identifies the requesting machine's context.
Saved scope reads only `SavedVariables.merged` (`merged_runtime_memory_not_disk`);
Application reads `ApplicationVariables.runtime`. Commands do not call Load/Save,
create components, activate machines, or execute Flow/Custom Events. Active states
require existing runtime data; an empty list without runtime_data_present does
not prove the graph has completed or failed.

## Discovery and reading (stage 3)

Both commands require idle Edit Mode. Play Mode, compilation, and asset updates
return editor_busy. They do not run Flow or change graphs. Loading existing assets
uses AssetDatabase and may invoke normal Unity/VS deserialization callbacks.

`vs_list_graphs` parameters and results:

- `scope`: assets (default), prefabs, or loaded_scenes.
- `folder`: an existing folder inside Assets; defaults to Assets. Unused for
  loaded_scenes. Prefer a narrow folder in large projects.
- `page_size`, `cursor`: pagination.
- Returns graph owners: target, context_kind, asset_guid, local_file_id, asset_path,
  owner_name, graph_type, title, element_count, source/macro_target.
- assets searches ScriptGraphAsset/StateGraphAsset; prefabs searches machines in
  prefab asset hierarchies, including inactive objects; loaded_scenes searches
  machines in saved, already loaded scenes. Unloaded scenes are not opened;
  unsaved scenes are skipped with a warning. Prefab Stage is not traversed separately.

`vs_read_graph` parameters and results:

- `target`: the exact GlobalObjectId from discovery (owner or standalone macro).
- `parent_element_guids`: nested graph path, default []; at most 8 GUIDs.
- `max_depth`: expansion depth relative to the selected graph, default 2.
- `page_size`, `cursor`: pagination.
- `result.records`: flat records of kinds graph, port_definition, variable, node,
  connection, state, transition, group, and element for unclassified elements.
  `path` identifies nesting context; a node GUID is unique only within its graph.
- Nodes include type, coordinates, ports and value types, default values,
  is_defined, is_control_root, definition_error, and serialized configuration.
  Connections include node GUIDs and port keys. State Graphs include is_start
  and transitions.
- Nested elements include child_path, source, and macro_target. Traversal is
  depth-bounded and cycle-protected; unknown graphs/elements produce warnings.
- Variables use scope=graph_definition or object_definition: saved configuration,
  not runtime values. Missing Variables components are not added.
- Configuration reads Serialize/SerializeAs fields and backing fields of serialized
  auto-properties. Computed getters are not invoked. Supported values: primitives,
  strings, enum, Type, Unity references, vectors, Rect/Color/Quaternion, Guid,
  element references, Member, and List<string>. Other values return `{type, omitted:true}`.
  Name lists are limited to 256 entries and strings to 4096 characters; limits
  produce warnings.
- Member includes declaring_type, name, and parameter_types so an AI client can
  locate the called C# code. Game methods and getter values are not executed.
- Undefined units are not redefined with Define/EnsureDefined; incomplete ports
  are explicit. Unresolved endpoints do not prevent reading other records.

CoplayDev 10.1.0 publishes Parameters properties in PascalCase. Commands therefore
accept both the snake_case names above and Scope/Folder/Target/PageSize/Cursor/
MaxDepth/ParentElementGuids. Supplying both aliases for one argument is rejected.

Example sequence through execute_custom_tool:

```json
{"tool_name":"vs_list_graphs","parameters":{"scope":"prefabs","folder":"Assets/Tutorials","page_size":50}}
{"tool_name":"vs_read_graph","parameters":{"target":"<target from discovery>","max_depth":0,"page_size":50}}
{"tool_name":"vs_read_graph","parameters":{"target":"<same target>","parent_element_guids":["<state GUID>"],"max_depth":2}}
```

## Structural validation (stage 4)

`vs_validate_graph` accepts the same arguments and aliases as vs_read_graph:
target, parent_element_guids, max_depth, page_size, cursor. It requires idle Edit
Mode and does not invoke Define/Flow, call game methods, or repair graphs.

The first record on the first page is validation_summary:

- status=valid: no errors in the accessible structure and traversal is not incomplete.
- status=invalid: structural errors are proven, even if traversal is incomplete.
- status=inconclusive: no errors proven, but data/traversal is incomplete.
- error_count, warning_count: finding counts by severity.
- inspection_complete: completeness of inspection, independent of report page count.

`success=true` means the command ran successfully, not that the graph is correct.
Read summary.status and every page. data.complete describes response completeness
and limits. valid covers supported structural checks only; it does not guarantee
correct game logic or successful Play Mode execution.

Other records are findings: severity (error/warning/info), code, target, path,
element_guid, port_key, variable_name, message, details. Each finding identifies
its owner and graph path; element GUID and port are populated where applicable.

Checks include MissingType placeholders, definitionException, missing port types
and C# call declaring types, InvalidInput/InvalidOutput/InvalidConnection, missing
connection nodes/ports, incompatible port kinds and value types, transitions to
missing states, duplicate GUIDs, missing nested graphs, and values containing
detectably destroyed Unity references. Conversion checks use
ConversionUtility.CanConvert metadata without converting values.

An incomplete snapshot cannot prove an endpoint is absent; that produces a warning.
A unit without ready ports and without definitionException is a warning, not an
error. Ordinary null may mean Self, an optional argument, or a future runtime
injection and is not classified as a broken reference. A missing Unity reference
already deserialized as ordinary null cannot be distinguished from intentional null.

variable_dependency, member_dependency, and event_dependency use severity=info.
Variable details include scope, default_name, owner_target, and graph path.
default_name is a hint from default values; the input may be connected dynamically.
An absent variable declaration or paired event handler is not an error: it may
come from runtime/C# or another graph. Declaring type existence is checked, but
there is no separate exhaustive method overload search; MemberUnit definition
errors are reported through definitionException.

The validator analyzes the reader's snapshot and inherits its limits on complex
values, depth, and record count. It does not repair or execute anything.

## Identifiers and extension points

Identifiers:

- Asset: `asset_guid` (Unity GUID); paths are display information. Distinguish
  subassets additionally by local file ID.
- Scene/prefab owner: `global_object_id`, `context_kind`, and scene/prefab GUID.
  Do not promise stable IDs for unsaved scenes/objects; report a limitation or
  a temporary identifier for the current session.
- Runtime owner: `instance_id` plus `play_session_id`. References become stale
  after Play/Stop or domain reload. Session management is provided by runtime commands.
- Embedded/nested graph: owner/asset identifier plus ordered `parent_element_guids`.
  Nodes use GUIDs within the selected graph.
- Object names are not unique identifiers.

Implemented pagination and limits:

- `page_size`: default 50, range 1–200; invalid values are rejected.
- `max_depth`: default 2, range 0–8; cycle protection is required.
- Page record budget: 240 KiB UTF-8, leaving room within 256 KiB for the envelope.
  MCP may additionally duplicate responses in content/structuredContent. Oversized
  elements become summaries with warnings; JSON is never truncated as raw text.
- At most 20000 snapshot records and 32 unique warnings. The record limit makes
  the snapshot incomplete; continuation beyond that limit is not promised.
- `next_cursor` is tied to the request (including page_size) and the SHA-256 hash
  of the returned snapshot. Snapshot changes between pages return stale_cursor.
  Changes to hidden/omitted values are not guaranteed to be detected; this revision
  does not authorize edits.
- Remaining pages or truncated data mean `complete=false`; warnings explain why.
  Reaching max_depth does not imply complete graph traversal.
- Read errors: invalid_arguments, not_found, stale_cursor, editor_busy,
  inspection_failed. Unsupported data produce warnings. Runtime and write commands
  use their separate revisions/sessions described above.

Snapshots are rebuilt for every request; there is no persistent cache. Large search
scopes can take time even with a small page size.
