# Visual Scripting Demo

Use this sample to ask an AI assistant to inspect, diagnose, and edit Unity Visual Scripting graphs through the package. Each exercise includes a copyable prompt and an example of the expected response. The examples describe the shipped sample's behavior. The assistant should report its actual observations, current identifiers, and any step it could not complete.

## Import and connect

Import **Visual Scripting Demo** from the package's **Samples** tab in Unity Package Manager. Find it under `Assets/Samples/UAppToolKit Visual Scripting MCP/<package-version>/Visual Scripting Demo`.

The Unity project in this repository includes CoplayDev MCP `10.1.0` and Unity Pipeline `1.0.0`. For CoplayDev, open **Window → MCP for Unity**, start the local HTTP server and Editor session, and connect your AI client to the endpoint shown there. When importing this sample into another project, install your chosen bridge as described in the package's adapter documentation.

Connect your AI client to either CoplayDev Unity MCP or Unity CLI / Pipeline in the same project. The assistant uses this package's `vs_*` commands for graphs and the connected Unity bridge for opening scenes, inspecting ordinary components, pressing buttons or sending events, controlling Play Mode, reading Console, and saving. When a needed action is unavailable, perform that action in Unity and let the assistant continue. The scenes can also run directly in Play Mode.

Start each exercise from the stated scene and mode. Replace `<sample-folder>` with the imported folder if using the command examples below. The prompts let the assistant discover the folder and IDs itself.

The [command examples](#command-examples-for-the-assistant) and the package's `Documentation~/CONTRACT.md` provide request fields for the assistant. Discovery uses `scope: "assets"`, `"prefabs"`, or `"loaded_scenes"`. A returned `child_path` is passed as `parent_element_guids` for a nested read. Omit `cursor` on the first request, pass a nonempty `next_cursor` for the next page, and finish when it becomes null.

| Scene | What to observe |
| --- | --- |
| `Scenes/GraphShowcase.unity` | Embedded and external graphs on scene objects and prefab instances, independent counters, State Machine transitions |
| `Scenes/DiagnoseAndRepair.unity` | A button sends one event while its graph listens for another, followed by a repair and replay |
| `Scenes/EditAndCompose.unity` | Visible results of graph and Object Variable bindings, plus an embedded prefab instance for edits |
| `Scenes/CustomNodeLab.unity` | A custom C# Unit evaluates assigned and empty object references |

## Ask the AI to discover and explain graphs

Open `GraphShowcase.unity` in idle Edit Mode.

**Prompt**

```text
Inspect the imported Visual Scripting Demo in this Unity project. Confirm the project and active scene with diagnostics, then discover graph assets, prefab machines, and loaded scene machines. Identify Embed and Macro sources and explain which owners share a graph. Read SharedCounter's nested graphs and the State Machine, following all pages and child paths. Explain the event-to-result flow and list the scene controls I can use to verify it.
```

**Expected response example**

```text
The showcase has scene objects and prefab instances using both Embed and Macro graphs. Prefab Macro A and B have separate machine identities and share SharedCounter. SharedCounter includes an embedded child and a referenced SharedStep graph. Running A twice and B once should produce counts 2 and 1. State Machine should move Ready → Completed → Ready when run twice. My report includes the discovered asset paths, owner targets, and nested paths.
```

**Visible check** — Enter Play Mode and use the six cards. Their result text and counts come from the graph owners' Variables components. Scene Embed and Prefab Embed report their own names; Macro cards report `Shared macro ran`.

## Ask the AI to distinguish validation results

Return to idle Edit Mode with `GraphShowcase.unity` open.

**Prompt**

```text
Validate SharedCounter with enough depth to inspect its nested graphs, then validate it with max_depth 0. Also validate EditOnly/MissingNestedMacro.asset. Read every report page and explain each summary status, finding, and coverage limit. Use the observed structural findings to explain what each result establishes.
```

**Expected response example**

```text
SharedCounter is valid when its nested graphs are inspected. With max_depth 0, validation is inconclusive because those child graphs are outside the inspected depth. MissingNestedMacro is invalid and reports missing_graph. The summaries establish structural inspection results. The runtime exercises check the graph's visible behavior.
```

**Check** — Ask for the actual `validation_summary` and relevant finding paths. The deliberately incomplete graph is an Edit Mode fixture separate from the playable scenes.

## Ask the AI to diagnose the stalled graph

Open `DiagnoseAndRepair.unity` in idle Edit Mode. This prompt authorizes inspection and reproduction.

**Prompt**

```text
In DiagnoseAndRepair, Repair Station's Run graph button leaves the station waiting. Find the cause and report the evidence before making a repair. Read its graph and inspect the Demo Panel component's configured event through the Unity bridge. Validate the graph. In Play Mode, discover Repair Station again, read its variables, start a bounded trace before reproducing the button action, and compare the received event with the graph handler. Stop the trace and report the values and visible result you observed. Suggest the smallest graph edit.
```

**Expected response example**

```text
Demo Panel sends Demo.Run to Repair Station, while the Custom Event node in Repair.asset listens for Demo.Disabled. The graph passes structural validation. The trace observed Demo.Run; Completed stayed false and RunCount stayed 0, and Game View remained waiting. The handler name differs from the sender. I propose setting the graph's Custom Event name to Demo.Run. The report identifies the graph, event node, and observations from this session.
```

The names deliberately differ by a whole word so the comparison is easy to read.

| Configuration | Event name |
| --- | --- |
| Demo Panel button | `Demo.Run` |
| Graph handler before repair | `Demo.Disabled` |
| Graph handler after repair | `Demo.Run` |

The trace observes event receipt and sampled debug data. Completion is established by the owner variables and Game View result.

## Ask the AI to repair and replay

Continue after the diagnosis. This prompt authorizes the targeted graph change and verification.

**Prompt**

```text
Repair the diagnosed event mismatch by changing Repair.asset's Custom Event handler to the event configured on the button. Exit Play Mode, reread the graph, and use the current edit_revision for a set_default preview. Inspect the diff, apply the change, verify Undo and Redo, save that graph, and validate it again. Enter a fresh Play session, rediscover Repair Station, press the same button, and report the actual Completed, RunCount, Result, and Game View output. Exit Play Mode when verification is complete.
```

**Expected response example**

```text
The preview changed the handler from Demo.Disabled to Demo.Run. Apply, Undo, Redo, saving, and structural validation succeeded. In a fresh Play session, pressing the button set Completed to true, RunCount to 1, and Result to "Tutorial advanced". The Game View card showed that result. The report includes the saved asset path and any verification step that was unavailable.
```

**Visible check** — The repaired card displays `Tutorial advanced` and `1 run`. To repeat the diagnosis, ask the assistant to restore the handler to `Demo.Disabled` with preview/apply and save the graph. The button continues sending `Demo.Run`.

## Ask the AI to inspect runtime variables

Open `GraphShowcase.unity` in Edit Mode so the assistant can read nested graph paths before entering Play Mode.

**Prompt**

```text
Read the showcase's graph paths, then enter Play Mode and inspect existing runtime variables. For Scene Embed, show Graph, Object, Scene, Application, and Saved scopes. Run Scene Macro and inspect the Graph Variables in each executed child using its child_path. Compare graph definition values with the runtime observations. Report the machine and Play session you selected, each scope's availability, and any pagination or observation limits. Finish in Edit Mode.
```

**Expected response example**

```text
Scene Embed exposes its graph role and Object Variables. Scene scope contains VsDemo.SceneTitle = "Graph Showcase", and Application scope contains VsDemo.ApplicationStatus = "Showcase active". The executed children of Scene Macro expose their own Graph Variables. Saved scope reports Visual Scripting's merged runtime memory and may be empty. My report gives the values actually read and identifies unavailable contexts.
```

## Ask the AI to clone and bind graphs and objects

Open `EditAndCompose.unity` in idle Edit Mode. This exercise creates working copies under `Assets` and changes the Binding Station assignments.

**Prompt**

```text
Create a scratch folder under Assets. Preview and clone SharedCounter with share_macros and embed_macros into separate new assets. Read and validate both copies, then explain their nested graph references. On Binding Station, record DemoReference's existing object ID, clear the reference with a bind preview/apply, save this scene, and run the station. Return to Edit Mode, reassign the recorded reference with a fresh preview/apply, save, and run it again. Finally preview/apply binding one clone as the station's graph, save, and run it. Report the three visible results and which nested graphs remain shared. Finish in Edit Mode and restore the original station assignments and save the scene.
```

**Expected response example**

```text
The share_macros copy retains its reference to SharedStep. The embed_macros copy contains an independent embedded child. With DemoReference cleared, Binding Station reported "Reference cleared"; after reassignment it reported "Reference assigned". With the cloned SharedCounter assigned, it reported "Shared macro ran". My report includes the created asset paths, applied bindings, and whether restoration succeeded.
```

**Check** — The scene's `Reference Beacon` supplies the initial `DemoReference` value. Use the GlobalObjectId exposed by its Object Variable record or by the Unity bridge. Restore assignments through current previews or the applicable Unity Undo history. Save and recheck them before deleting scratch clones. Clone asset creation is immediately saved, and cleanup uses the Project window or normal Unity asset APIs.

## Ask the AI to edit nodes and connections

Open `EditAndCompose.unity` in idle Edit Mode. Use a scratch copy of the compact `Graphs/SharedStep.asset` for the operation batches.

**Prompt**

```text
Duplicate SharedStep into a new Assets scratch folder for an edit exercise. Read the copy's current edit_revision before each batch. Preview and apply adding a bool Literal and an If node, configuring the Literal, moving the If, connecting the Literal output to the If condition, changing the Result setter's name input to DemoScratchResult, and setting a DemoScratch Graph Variable. Read back the changes and verify Undo/Redo. In a second batch, disconnect the new connection, remove both new nodes, remove DemoScratch, and restore the original setter name. Save and reload the copy and validate it. Also preview/apply moving an existing node in Prefab Embed's scene instance, verify the prefab override and Undo, and restore it. Report the actual diffs and final validation results.
```

**Expected response example**

```text
The scratch copy exercised add_node, configure_node, move_node, connect, set_default, set_variable, disconnect, remove_node, and remove_variable. The new nodes and connection appeared in the readback, Undo/Redo reproduced the changes, and the cleanup batch removed them. The reloaded copy passed structural validation. The scene-instance edit produced a prefab override and Undo restored its previous position. My report includes the affected GUIDs and saved copy path.
```

The connection must be removed before its nodes. For nested embedded edits, pass the selected child's path while using the root owner's current `edit_revision`. A shared Macro is edited through its direct asset target.

## Ask the AI to work with settings and a custom Unit

Use an imported copy in a disposable project for this exercise because the settings apply to the project. Open a Visual Scripting graph and let initial setup complete, then open `CustomNodeLab.unity`.

The package saves settings before apply returns `save_status: "saved"` and verifies the changed values by reading the file back. Undo/Redo uses the same path. This exercise works in an ordinary, unfocused Unity Editor through either bridge; batchmode and window activation are not required. The assistant should still verify the saved values and report any failed check.

**Prompt**

```text
Inspect the sample Demo Has Reference C# Unit, its graph usage, and its typed ports. Read Visual Scripting settings and record their current values. Preview and apply flipping AOT Safe Mode, verify the saved setting and Undo/Redo, then restore and verify the original value. Explicitly preview/apply node regeneration and poll the existing job until it finishes. Check the Console and confirm the Unit is available in the node library. Run both CustomNodeLab cards and report their actual results. Finish in Edit Mode and report any incomplete save, job, or runtime verification.
```

**Expected response example**

```text
Demo Has Reference has a Unity object input named target and a bool output named isAssigned. AOT Safe Mode was changed and restored, with the saved value verified. Regeneration completed and the Unit appeared in the library with those ports. In Play Mode, Assigned Reference reported "Reference assigned" and Empty Reference reported "Reference cleared". My report records the actual regeneration status and any unavailable checks.
```

## Command examples for the assistant

CoplayDev calls `execute_custom_tool` with the command name and a `parameters` object. Unity CLI uses the `vs` group and removes the `vs_` prefix. With `unity mcp`, discover the published schema; it can require JSON text in `parameters`. Inspect the core `success` and `data.error`, including when a transport wraps the response. Successful operation data is under the core `data.result`.

```json
{"tool_name":"vs_list_graphs","parameters":{"scope":"loaded_scenes"}}
```

```bash
unity command vs list_graphs --parameters '{"scope":"loaded_scenes"}' --project-path /path/to/project --format json
unity command vs diagnostics --project-path /path/to/project --format json
```

For discovery, use `scope` values `assets`, `prefabs`, and `loaded_scenes`. The first two accept the imported `folder`. Omit `cursor` from the first request. When `data.next_cursor` contains a string, repeat the same request with that value as `cursor`; null marks the final page. A Macro owner exposes its external asset as `macro_target`. For nested reads, pass a returned `child_path` as `parent_element_guids`.

```json
{"scope":"assets","folder":"<sample-folder>","page_size":2}
```

```json
{"target":"<discovered target>","max_depth":2,"page_size":5}
```

```json
{"target":"<same target>","parent_element_guids":["<parent element GUID>"],"max_depth":2}
```

Runtime inspection requires the component's `instance_id` and `play_session_id` from current runtime discovery. Rediscover after Play transitions and reloads. Start tracing before the action, read using the returned cursor, and stop the observer when finished.

```json
{"instance_id":123,"play_session_id":"<current Play session>","scope":"object"}
```

```json
{"instance_id":123,"play_session_id":"<current Play session>","event_names":["Demo.Run"]}
```

```json
{"trace_id":"<trace id>","page_size":10}
```

Graph edit previews use the latest reader `edit_revision`. On a revision conflict, reread and prepare a fresh preview. Apply the same reviewed batch with `dry_run: false`, then save the changed asset or scene through Unity.

```json
{
  "target":"<Repair.asset target>",
  "expected_revision":"<current edit_revision>",
  "dry_run":true,
  "operations":[
    {"op":"set_default","guid":"<CustomEvent node GUID>","port":"name","value":{"type":"string","value":"Demo.Run"}}
  ]
}
```

Clone and bind apply requests use `expected_revision` from their own preview's `data.result.revision`. Clone destinations must be new `.asset` paths in an existing `Assets` folder. Object Variable bindings require an existing Variables component; explicit JSON `null` clears the reference. Regular prefab assets accept compatible persistent asset references, and scene object references stay within the owner's scene. Flow and State graph bindings must match the machine type.

```json
{"target":"<SharedCounter target>","destination":"Assets/VisualScriptingDemoScratch/SharedCopy.asset","macro_policy":"share_macros","dry_run":true}
```

```json
{"target":"<Binding Station machine target>","kind":"graph","reference":"<clone new_target>","dry_run":true}
```

```json
{"target":"<Binding Station machine target>","kind":"variable","variable_name":"DemoReference","reference":null,"dry_run":true}
```

Settings use their own current revision. `save_status: "saved"` confirms that changed values were written and read back before apply returned; preview/no-op returns `not_requested`. Explicit regeneration with `changes: {}` rebuilds the library without changing the setting lists. Poll `vs_get_settings` until `regeneration.state` is `completed`, `failed`, or `interrupted`.

```json
{"expected_revision":"<current settings revision>","changes":{"aot_safe_mode":true},"dry_run":true}
```

```json
{"expected_revision":"<current settings revision>","changes":{},"regenerate_nodes":true,"dry_run":true}
```

## Command reference

| CoplayDev `tool_name` | Unity CLI `vs` operation | Exercise |
| --- | --- | --- |
| `vs_diagnostics` | `diagnostics` | Discover and explain graphs |
| `vs_list_graphs` | `list_graphs` | Discover and explain graphs |
| `vs_read_graph` | `read_graph` | Discover and explain graphs |
| `vs_validate_graph` | `validate_graph` | Distinguish validation results |
| `vs_runtime_status` | `runtime_status` | Diagnose the stalled graph |
| `vs_runtime_variables` | `runtime_variables` | Inspect runtime variables |
| `vs_trace_start` | `trace_start` | Diagnose the stalled graph |
| `vs_trace_read` | `trace_read` | Diagnose the stalled graph |
| `vs_trace_stop` | `trace_stop` | Diagnose the stalled graph |
| `vs_edit_graph` | `edit_graph` | Repair and replay; edit nodes and connections |
| `vs_clone_graph` | `clone_graph` | Clone and bind graphs and objects |
| `vs_bind_object` | `bind_object` | Clone and bind graphs and objects |
| `vs_get_settings` | `get_settings` | Work with settings and a custom Unit |
| `vs_update_settings` | `update_settings` | Work with settings and a custom Unit |

See the package's **Documentation** for the full request contract, supported target contexts, and adapter versions.
