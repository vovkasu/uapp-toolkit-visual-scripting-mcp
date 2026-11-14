# Visual Scripting Demo

Import **Visual Scripting Demo** from the package's **Samples** tab in Unity Package Manager. In the Project window, find it under `Assets/Samples/UAppToolKit Visual Scripting MCP/<package-version>/Visual Scripting Demo`. Use the folder shown in your project for every example containing `<sample-folder>`.

Open `Scenes/GraphShowcase.unity` and enter Play Mode. Press a row's button to run its graph. Its result and run count come from the graph owner's Variables component.

| Row | Graph owner | Graph source |
| --- | --- | --- |
| Scene Embed | Scene object | Embedded Script Graph |
| Scene Macro | Scene object | External Script Graph asset |
| Prefab Embed | Prefab instance | Script Graph embedded in the prefab |
| Prefab Macro A and B | Two instances of one prefab | Shared external Script Graph asset |
| State Machine | Scene object | State Graph with embedded Flow Graphs |

Run Prefab Macro A twice, then Prefab Macro B once. Their counters are independent. Run the State Machine twice to see Ready → Completed → Ready. Open `Scenes/DiagnoseAndRepair.unity` to investigate an event-name mismatch, `Scenes/EditAndCompose.unity` to try bindings, or `Scenes/CustomNodeLab.unity` to run the sample C# Unit. The scenes run without an MCP connection. The command exercises work with either adapter.

## Connect a command adapter

Install a supported CoplayDev Unity MCP or Unity CLI / Pipeline bridge and connect it to this Editor project. CoplayDev accepts the JSON examples in this guide as the `parameters` object of its `execute_custom_tool` command. For example, pass these arguments to `execute_custom_tool`.

```json
{"tool_name":"vs_list_graphs","parameters":{"scope":"loaded_scenes"}}
```

Unity CLI removes the `vs_` prefix and places the operation in the `vs` group. Pass the same JSON as a single `--parameters` argument.

```bash
unity command vs list_graphs --parameters '{"scope":"loaded_scenes"}' --project-path /path/to/project --format json
unity command vs diagnostics --project-path /path/to/project --format json
```

When using `unity mcp --project-path /path/to/project`, discover the published tool schema. Pipeline's MCP adapter can advertise `parameters` as JSON text, so use the published name and argument type. Inspect the core `success` and `data.error`, including when a transport wraps the core response. For successful calls, the operation payload is under the core response's `data.result`. Replace all angle-bracket placeholders with values from your own project and responses.

## Discover and inspect graphs

1. In idle Edit Mode, open the saved `GraphShowcase.unity` scene. Call `vs_diagnostics` with `{}`. Check the project, active scene, and the 14 advertised capabilities.
2. Call `vs_list_graphs` with each request below. Compare asset, prefab, and scene entries. A machine in Macro mode exposes `macro_target`, the GlobalObjectId of its external graph. The two Macro prefab instances have different owner IDs and share the same `macro_target`.

```json
{"scope":"assets","folder":"<sample-folder>","page_size":2}
```

```json
{"scope":"prefabs","folder":"<sample-folder>"}
```

```json
{"scope":"loaded_scenes"}
```

3. Call `vs_read_graph` with a returned `target`. Follow `data.next_cursor` to the end by adding `cursor` to the same request. Inspect graph and object definitions, node port keys, and connections. To read a nested graph directly, take a nested record's `child_path` and pass it as `parent_element_guids`. The State Graph exposes states and transitions as records.

```json
{"target":"<target>","max_depth":2,"page_size":5}
```

```json
{"target":"<target>","parent_element_guids":["<parent element GUID>"],"max_depth":2}
```

4. Call `vs_validate_graph` on a working graph and read `validation_summary` on the first page. Use enough `max_depth` to see `valid`. Repeat on the clone source with `max_depth: 0` to see `inconclusive` when the nested graphs are outside the inspection scope. `EditOnly/MissingNestedMacro.asset` produces `invalid` and a `missing_graph` finding. It is independent of the playable scenes.

```json
{"target":"<nested graph source target>","max_depth":0}
```

## Inspect a running graph and repair an event

1. Open `DiagnoseAndRepair.unity`. In Edit Mode, find its scene machine with `vs_list_graphs`. Use its `macro_target` as the direct graph asset target for `vs_read_graph` and `vs_validate_graph`. Record the `CustomEvent` node's `guid`, its `defaults.name` value `AdvanceTutoral`, and `edit_revision`. The graph can pass structural validation with this spelling.
2. Enter Play Mode. Call `vs_runtime_status` with `{}` and find `Repair Station` by name and scene. Use that record's numeric `instance_id` and string `play_session_id` for the next calls. Query `vs_runtime_variables` and check that the Object Variable `Completed` begins at `false`. In the showcase, select `Scene Embed` and use `scope: "all"` to inspect its Graph and Object Variables, `VsDemo.SceneTitle` under Scene, and `VsDemo.ApplicationStatus` under Application. Saved scope reports the values in Visual Scripting's merged runtime memory, which may be empty in a new project. Select `Scene Macro` and pass an executed subgraph's `child_path` to inspect its nested Graph Variables.

```json
{"instance_id":123,"play_session_id":"<current Play session>","scope":"object"}
```

3. Call `vs_trace_start`, press the row's button, then call `vs_trace_read`. Look for a `custom_event` record named `AdvanceTutorial` and check that `Completed` is still `false`. Keep `data.next_cursor` for the next call to trace read. Call `vs_trace_stop` with `{"trace_id":"<trace id>"}` before leaving Play Mode. Trace records show event delivery and sampled debug data. The variable and Game View provide the evidence of completion.

```json
{"instance_id":123,"play_session_id":"<current Play session>","event_names":["AdvanceTutorial"]}
```

```json
{"trace_id":"<trace id>","page_size":10}
```

4. Back in idle Edit Mode, reread the external graph asset and use its current `edit_revision`. Call `vs_edit_graph` with this preview and check the spelling in the diff.

```json
{
  "target":"<repair graph asset target>",
  "expected_revision":"<edit_revision from current vs_read_graph>",
  "dry_run":true,
  "operations":[
    {"op":"set_default","guid":"<CustomEvent node GUID>","port":"name","value":{"type":"string","value":"AdvanceTutorial"}}
  ]
}
```

5. Send the same request with `dry_run: false`. Reread the graph. Use Unity Undo once and check for `AdvanceTutoral`. Redo and check for `AdvanceTutorial`. Save the changed graph asset from Unity and run `vs_validate_graph` again. If the revision changed before apply, reread and prepare a fresh preview.
6. Enter Play Mode, rediscover the machine for the new session, and press the same button. The visible result changes and `vs_runtime_variables` reports `Completed: true`. To replay the repair exercise, use the same edit procedure in Edit Mode to restore the original spelling and save the asset.

## Clone and bind a graph

1. In the Project window create a scratch folder under `Assets`. Find `Graphs/SharedCounter.asset`, which has both embedded and Macro children. Call `vs_clone_graph` with this preview.

```json
{
  "target":"<clone source graph target>",
  "destination":"Assets/VisualScriptingDemoScratch/SharedCopy.asset",
  "macro_policy":"share_macros",
  "dry_run":true
}
```

2. Compare the reported `shared_macros` and repeat with a different filename and `macro_policy: "embed_macros"`. For each preview you want to apply, send the same request with `dry_run: false` and `expected_revision` from that preview's `data.result.revision`. A successful apply saves the new asset and gives its `new_target`. Validate both copies and read their nested records. The shared copy retains the child's `macro_target`. The embedded copy owns an embedded child, while other project asset references are still shared. The clone command creates an asset immediately. Delete scratch clones from the Project window to reset this part of the exercise after restoring the bindings.
3. Open `Scenes/EditAndCompose.unity` in Edit Mode. Discover `Binding Station` in `loaded_scenes` and use `vs_bind_object` to preview assigning a Flow Graph clone.

```json
{
  "target":"<scene ScriptMachine target>",
  "kind":"graph",
  "reference":"<clone new_target>",
  "dry_run":true
}
```

4. Apply with this bind preview's `revision` and `dry_run: false`, then reopen the graph details and check that `vs_list_graphs` reports `source: "Macro"` and the new `macro_target`. Enter Play Mode and run Binding Station. It should report “Shared macro ran.” Return to Edit Mode and use Undo to restore the station's embedded reference-check graph.
5. Read Binding Station with `vs_read_graph` and take the GlobalObjectId of Reference Beacon from `DemoReference` in the Object Variable record's `value.object_id`. Use `kind: "variable"` and JSON `null` to clear the existing declaration. Preview and apply with its current `revision`. Run the station to see “Reference cleared.” Back in Edit Mode, use the recorded GlobalObjectId for a new preview and apply. Run again to see “Reference assigned.” Keep scene references within the owner's scene. A regular prefab asset accepts compatible persistent asset references.

```json
{
  "target":"<machine target>",
  "kind":"variable",
  "variable_name":"DemoReference",
  "reference":"<compatible object's GlobalObjectId>",
  "dry_run":true
}
```

6. Verify Unity Undo restores each assignment. Save only the changed scene or prefab if keeping the changes. The same workflow can bind a State Graph asset to a State Machine; the target and reference must have matching graph types.

## Try the graph edit operations

Duplicate `Graphs/SharedStep.asset` into your scratch folder for the operation batches. Use the embedded machine on the Prefab Embed instance in `EditAndCompose` to inspect a scene-instance override. For all batches, call `vs_read_graph` on the exact target first and use the returned `edit_revision`. Preview each batch, inspect the diff, then apply with `dry_run: false`. The embedded machine's edit appears as an override on that instance. To edit a shared Macro, use the asset's direct target.

On the scratch copy of SharedStep, try the following batch. Generate distinct fresh GUIDs for the Literal, If, and connection. Apply the two batches separately. The second uses the resulting revision from a new read.

```json
[
  {"op":"add_node","guid":"<new Literal GUID>","type":"Literal","x":400,"y":400,"config":{"value":{"type":"bool","value":true}}},
  {"op":"add_node","guid":"<new If GUID>","type":"If","x":650,"y":400},
  {"op":"configure_node","guid":"<same Literal GUID>","config":{"value":{"type":"bool","value":false}}},
  {"op":"move_node","guid":"<same If GUID>","x":700,"y":440},
  {"op":"connect","guid":"<new connection GUID>","kind":"value","source_node":"<same Literal GUID>","source_port":"output","destination_node":"<same If GUID>","destination_port":"condition"},
  {"op":"set_variable","name":"DemoScratch","value":{"type":"int","value":7}}
]
```

```json
[
  {"op":"disconnect","guid":"<same connection GUID>"},
  {"op":"remove_node","guid":"<same If GUID>"},
  {"op":"remove_node","guid":"<same Literal GUID>"},
  {"op":"remove_variable","name":"DemoScratch"}
]
```

The repair exercise uses the remaining operation, `set_default`. To try an embedded subgraph edit, pass its `child_path` as `parent_element_guids` and keep the root's `edit_revision`. The edit has its own Unity Undo step. Save and reload the affected asset or scene to check persistence.

## Work with settings and a custom Unit

Use a disposable Unity project for this exercise because Visual Scripting settings apply to the whole project. Open a graph and wait for initial setup. Call `vs_get_settings` and record `data.result.settings.aot_safe_mode` and `data.result.revision`. Preview flipping the setting, inspect the diff, apply, then reread it. Restore the original value by repeating the same preview and apply sequence with the new revision. Allow the Editor to finish saving and check the reported value.

```json
{
  "expected_revision":"<current settings revision>",
  "changes":{"aot_safe_mode":true},
  "dry_run":true
}
```

Use the opposite of your recorded value for the first apply. The settings response's `save_status: "scheduled_by_visual_scripting"` means the write is scheduled.

Open `Scenes/CustomNodeLab.unity`. The two embedded graphs use the compiled `Demo Has Reference` Unit. It takes a Unity object on `target` and returns a boolean on `isAssigned`. To rebuild the node library, preview and then apply the following request. Read `vs_get_settings` until `data.result.regeneration.state` becomes `completed`, `failed`, or `interrupted`. Check the Console and the node's availability and ports in the Visual Scripting graph editor. In Play Mode, run Assigned Reference and Empty Reference. The cards should report “Reference assigned” and “Reference cleared.”

```json
{
  "expected_revision":"<current settings revision>",
  "changes":{},
  "regenerate_nodes":true,
  "dry_run":true
}
```

## Command reference

| CoplayDev `tool_name` | Unity CLI `vs` operation | Exercise |
| --- | --- | --- |
| `vs_diagnostics` | `diagnostics` | Discover and inspect graphs |
| `vs_list_graphs` | `list_graphs` | Discover and inspect graphs |
| `vs_read_graph` | `read_graph` | Discover and inspect graphs |
| `vs_validate_graph` | `validate_graph` | Discover and inspect graphs |
| `vs_runtime_status` | `runtime_status` | Inspect a running graph and repair an event |
| `vs_runtime_variables` | `runtime_variables` | Inspect a running graph and repair an event |
| `vs_trace_start` | `trace_start` | Inspect a running graph and repair an event |
| `vs_trace_read` | `trace_read` | Inspect a running graph and repair an event |
| `vs_trace_stop` | `trace_stop` | Inspect a running graph and repair an event |
| `vs_edit_graph` | `edit_graph` | Repair an event and try the graph edit operations |
| `vs_clone_graph` | `clone_graph` | Clone and bind a graph |
| `vs_bind_object` | `bind_object` | Clone and bind a graph |
| `vs_get_settings` | `get_settings` | Work with settings and a custom Unit |
| `vs_update_settings` | `update_settings` | Work with settings and a custom Unit |

See the package's **Documentation** for the full request contract, supported target contexts, and adapter versions.
