# Actual AI responses: Unity CLI / Pipeline MCP

Initial walkthrough: 2026-11-17; GUI persistence follow-up: 2026-11-18 (Europe/Kyiv). These are actual assistant answers, not the README example answers. Local file links are rendered as historical paths; screenshot links point to retained copies. Answer text is otherwise unchanged. Local paths and session IDs refer to disposable validation projects. Prompt 1 is the fresh-context repeat after adapter metadata fixes; prompt 8 is the completed ordinary, unfocused GUI repeat after the persistence fix. Other prompts are from the initial sequential walkthrough. See [the validation report](../DEMO_PROMPT_VALIDATION.md) for methodology, independent checks, and initial limitations.

## 1. Ask the AI to discover and explain graphs

**Exact prompt**

```text
Inspect the imported Visual Scripting Demo in this Unity project. Confirm the project and active scene with diagnostics, then discover graph assets, prefab machines, and loaded scene machines. Identify Embed and Macro sources and explain which owners share a graph. Read SharedCounter's nested graphs and the State Machine, following all pages and child paths. Explain the event-to-result flow and list the scene controls I can use to verify it.
```

**Actual response**

Confirmed through Unity Pipeline MCP: project `/private/tmp/uapp-prompt-runs/VsDemoPipelineSettingsAI`, Unity **6000.3.16f1**, Visual Scripting **1.9.11**. The active scene is **GraphShowcase**, at `Assets/Samples/UAppToolKit Visual Scripting MCP/1.1.0/Visual Scripting Demo/Scenes/GraphShowcase.unity`. Diagnostics show idle Edit Mode, no compilation/update/Play transition, no warnings, and a clean scene. The debug provider is bound; full execution tracing is not supported.

Discovery completed in all three scopes, with no remaining cursors:

- **Five graph assets:** SharedCounter (Flow), SharedStep (Flow), DemoStates (State), Repair (Flow), and EditOnly/MissingNestedMacro (Flow).
- **Two prefab machines:** PrefabEmbed uses **Embed**, titled “Prefab embedded counter”; PrefabMacro uses **Macro → SharedCounter**.
- **Six loaded scene machines:** listed below. The scene also contains Demo Camera, Demo Panel, and Showcase Variables.

| Loaded owner | Source | Panel event | Result defined by its flow |
|---|---|---|---|
| Scene Embed | Embed | RunDemo | Scene Embed ran |
| Scene Macro | Macro → SharedCounter | RunDemo | Shared macro ran |
| Prefab Embed | Embed, from PrefabEmbed | RunDemo | Prefab Embed ran |
| Prefab Macro A | Macro → SharedCounter, from PrefabMacro | RunDemo | Shared macro ran |
| Prefab Macro B | Macro → SharedCounter, from PrefabMacro | RunDemo | Shared macro ran |
| State Machine | Macro → DemoStates | AdvanceState | Ready / Completed |

**Sharing:** PrefabMacro, Scene Macro, Prefab Macro A, and Prefab Macro B reference the same SharedCounter asset. Its nested Macro references the separate SharedStep asset. Embed stores the graph within its machine or parent graph; Scene Embed and PrefabEmbed have their own definitions. Prefab Embed is an instance of the embedded prefab, rather than a SharedCounter macro owner. Shared graph definitions do not merge owner variables: every loaded machine has its own Object Variables, initially `RunCount = 0` and `Result = "Ready"`.

**SharedCounter flow:** the owner’s zero-argument `RunDemo` Custom Event triggers `RunCount = RunCount + 1`, then enters the **Embedded step**, then the **Shared macro step**. Both child graphs expose `enter` and `exit` control ports. The embedded child writes the owner’s `DemoEmbeddedStep = "Embedded step ran"` and exits; the Macro child resolves to SharedStep, writes the owner’s `Result = "Shared macro ran"`, and exits. These writes use Object scope. I followed both explicit child paths; neither has further children.

Scene Embed and Prefab Embed also increment their own RunCount on RunDemo, then write their respective result strings shown above. Scene Embed has an additional Start flow that writes Scene variable `VsDemo.SceneTitle = "Graph Showcase"`, followed by Application variable `VsDemo.ApplicationStatus = "Showcase active"`.

**State Machine flow:** DemoStates contains two embedded Flow States and two embedded transition graphs. **Ready** is the start state; its OnEnterState writes `Result = "Ready"`. **Completed** writes `RunCount = RunCount + 1`, then `Result = "Completed"` on entry. Each transition graph listens for `AdvanceState` and connects its event trigger to TriggerStateTransition: one goes Ready → Completed, the other Completed → Ready. Consequently, successive clicks should produce Completed / 1 run, Ready / 1 run, Completed / 2 runs. I followed both state child paths and both transition child paths.

For pagination, SharedCounter and State Machine each yielded **37 records across four pages** at page size 10. I also read all six child paths separately, following every returned cursor to `null`; every final response was complete.

To verify in Unity, enter Play Mode and use the Demo Panel in the Game view. Each of the six named cards has a **Run graph** button (`runGraph0`–`runGraph5`, in the table’s order), a run count, and a result label. The first five buttons send RunDemo to their own owner; the State Machine button sends AdvanceState. Run Macro A independently of Macro B to check that they share graph behavior while keeping separate counts. Inspect the selected owner’s Object Variables to see DemoEmbeddedStep, and inspect Scene/Application variables for Scene Embed’s Start writes. The panel refreshes its displayed variables every 100 ms.

This was a definition inspection in Edit Mode; I did not execute the graphs. No Unity assets or settings were changed, and the final diagnostics still show the scene clean.

## 2. Ask the AI to distinguish validation results

**Exact prompt**

```text
Validate SharedCounter with enough depth to inspect its nested graphs, then validate it with max_depth 0. Also validate EditOnly/MissingNestedMacro.asset. Read every report page and explain each summary status, finding, and coverage limit. Use the observed structural findings to explain what each result establishes.
```

**Actual response**

SharedCounter is **structurally valid with nested coverage**, **inconclusive at depth zero**, and MissingNestedMacro is **invalid because a nested graph is missing**.

I read every report page using `page_size: 5`: SharedCounter at depth 8 returned two pages / six records; depth 0 returned one page / four records; MissingNestedMacro at depth 8 returned one page / three records. Every final page had `next_cursor: null`.

| Validation | Summary status | Errors | Finding warnings | Inspection complete | Final `data.complete` |
|---|---|---:|---:|---|---|
| SharedCounter, `max_depth: 8` | valid | 0 | 0 | true | true |
| SharedCounter, `max_depth: 0` | inconclusive | 0 | 0 | false | false |
| EditOnly/MissingNestedMacro.asset, `max_depth: 8` | invalid | 1 | 1 | false | false |

For **SharedCounter at depth 8**, inspection includes its embedded step and the SharedStep macro. The five findings are all informational:

- Root **Set Variable / RunCount**: Object-scope `variable_dependency` requiring runtime context.
- Root **Get Variable / RunCount**: a separate Object-scope dependency on the same default variable name.
- Embedded step **Set Variable / DemoEmbeddedStep**: Object-scope dependency on the executing owner.
- Shared macro step **Set Variable / Result**: Object-scope dependency on the executing owner.
- Root **Custom Event / RunDemo**: `event_dependency`; its sender may be outside this graph, so a counterpart inside the graph is not required.

These establish that the accessible structure, including both nested graphs, passed the validator’s supported checks. The dependency notes do not count as errors or warnings. Object variable declarations and values require a runtime owner, and the event requires an external sender; structural validity does not establish successful execution or the expected gameplay result.

The first deep report page had `more_pages_available` and `data.complete: false` because one finding remained on page two. Its summary already had `inspection_complete: true`. After reading page two, `data.complete` became true.

For **SharedCounter at depth 0**, the three findings are the root RunCount setter dependency, RunDemo event dependency, and RunCount getter dependency. The nested DemoEmbeddedStep and Result findings are absent because those graph bodies were outside coverage.

The envelope reports `max_depth_reached` and `validation_incomplete`. These are coverage warnings, separate from the summary’s zero finding warnings. No structural errors were proven in the inspected root, but the excluded nested graphs prevent a valid summary, so the status is **inconclusive**. `data.complete: false` with no next cursor means coverage is limited; there are no unread report pages.

For **MissingNestedMacro**, the complete returned report contains:

- **Warning — `node_not_defined`** at root element `3e7e34a7-c53d-4a1a-89f8-9c40b0dffbc1`: ports are unavailable. The validator does not call `Define`, so checks requiring those ports are limited.
- **Error — `missing_graph`** at child path `[3e7e34a7-c53d-4a1a-89f8-9c40b0dffbc1]`: the nested graph is missing. This is a proven structural defect.

The envelope warnings are `undefined_units_present`, `missing_graph`, and `validation_incomplete`. Inspection cannot cover the absent graph or unavailable ports, so both completeness fields are false. Nevertheless, the proven missing-graph error makes the summary **invalid**, rather than inconclusive. The report establishes the missing graph; it does not establish why the reference is missing or what the absent graph should contain.

All three commands returned `success: true`, meaning validation ran successfully. Graph correctness is expressed by the summary status and findings. The project remains in Edit Mode; validation made no graph changes.

## 3. Ask the AI to diagnose the stalled graph

**Exact prompt**

```text
In DiagnoseAndRepair, Repair Station's Run graph button leaves the station waiting. Find the cause and report the evidence before making a repair. Read its graph and inspect the Demo Panel component's configured event through the Unity bridge. Validate the graph. In Play Mode, discover Repair Station again, read its variables, start a bounded trace before reproducing the button action, and compare the received event with the graph handler. Stop the trace and report the values and visible result you observed. Suggest the smallest graph edit.
```

**Actual response**

The evidence identifies an **event-name mismatch**: Repair Station’s button sends `Demo.Run`, while its graph listens for `Demo.Disabled`. I reproduced the waiting result and made no repair.

In the clean `DiagnoseAndRepair` scene, discovery found Repair Station using the **Repair.asset Macro** (“Repair the Custom Event”). I read all 18 graph records. Its Custom Event node, `675383ae-e838-4280-aa1b-249da73c1bcc`, has default `name = "Demo.Disabled"`. The graph-definition variable `ExpectedEvent` is already `Demo.Run`.

Through the Unity bridge, I inspected the Demo Panel component’s serialized row 0:

- Label and Target: `Repair Station`
- EventName: `Demo.Run`

The graph’s connected handler path would set Object `RunCount` to the literal **1**, then `Completed` to **true**, then `Result` to **Tutorial advanced**. This path is connected to the mismatched handler.

Structural validation at `max_depth: 8` returned **valid**, with **0 errors**, **0 warnings**, and `inspection_complete: true`. I read the entire five-record report. Its four informational findings describe Object dependencies on Result, Completed, and RunCount, plus an event dependency. The validator allows an event sender outside the graph; structural validity does not verify that this panel’s event name matches the handler.

In Play Mode, I rediscovered Repair Station as ScriptMachine instance **33644**, using the new Play session `90b5f432cc3345769ca2b52881067259`. It was active, enabled, listening, and had graph/runtime data present. I read its existing Object variables before reproduction, then started a **64-record bounded trace** before activating the actual `runGraph0` UI Toolkit button through a navigation-submit event.

Trace `f0636dbf64ad496c8cad23ecefc279e5` recorded **Custom Event `Demo.Run`**, with **0 arguments**, at sequence **4**, frame **7781**. This establishes receipt on the traced owner; the graph definition’s handler name is `Demo.Disabled`.

| Observation | Before button | After button |
|---|---|---|
| Object RunCount | 0 | 0 |
| Object Completed | false | false |
| Object Result | `Waiting for Demo.Run` | `Waiting for Demo.Run` |
| Visible station result | Waiting for Demo.Run | Waiting for Demo.Run |
| Visible count | 0 runs | 0 runs |

I stopped the trace successfully: `active: false`, reason `requested`. Its final report contained all five records, `has_more: false`, **0 lost records**, and **0 dropped records**. Debug coverage was available and untruncated. Tracing samples debug state every 0.1 seconds and observes owner-scoped Custom Events; it is not a complete execution history, and event receipt alone does not prove handler execution. The diagnosis combines the observed event, the mismatched configured handler, and the unchanged runtime values and UI.

Console inspection also found existing Unity Editor SearchDatabase exceptions, including the same stack during Play entry. I did not observe a Repair graph exception in the inspected output.

The smallest suggested graph edit is to change **only the Custom Event node’s `name` default from `Demo.Disabled` to `Demo.Run` in Repair.asset**. With `vs_edit_graph`, that is one `set_default` operation on node `675383ae-e838-4280-aa1b-249da73c1bcc`, port `name`, value `{ "type": "string", "value": "Demo.Run" }`. Because the station uses a Macro, target the graph asset directly and obtain its fresh `edit_revision` before previewing an edit.

After that edit, repeat the same button action to verify `RunCount = 1`, `Completed = true`, and visible `Tutorial advanced`. Those are the graph’s predicted corrected results; no fix was applied or tested here. I restored Edit Mode, and final diagnostics confirms DiagnoseAndRepair remains clean.

## 4. Ask the AI to repair and replay

**Exact prompt**

```text
Repair the diagnosed event mismatch by changing Repair.asset's Custom Event handler to the event configured on the button. Exit Play Mode, reread the graph, and use the current edit_revision for a set_default preview. Inspect the diff, apply the change, verify Undo and Redo, save that graph, and validate it again. Enter a fresh Play session, rediscover Repair Station, press the same button, and report the actual Completed, RunCount, Result, and Game View output. Exit Play Mode when verification is complete.
```

**Actual response**

Repaired and saved Repair.asset (`/private/tmp/uapp-prompt-runs/VsDemoPipelineAI/Assets/Samples/UAppToolKit Visual Scripting MCP/1.1.0/Visual Scripting Demo/Graphs/Repair.asset`). Its Custom Event now listens for **Demo.Run**, matching the Demo Panel button’s configured event.

After exiting Play Mode, I reread the graph and used its current `edit_revision` (`DBA18E0E68D275176FD76FCDD056314ABDE1E5567BFA845816474715EDF975F5`) for a `set_default` preview on node `675383ae-e838-4280-aa1b-249da73c1bcc`, port `name`. I inspected the diff: one node default changed from `Demo.Disabled` to `Demo.Run`. I then applied that same operation.

Undo and Redo were verified with graph reads:

- **Undo:** handler returned to `Demo.Disabled`, with the original edit revision.
- **Redo:** handler returned to `Demo.Run`, with the applied revision.

I saved only Repair.asset through `AssetDatabase.SaveAssetIfDirty`; its dirty flag changed from true to false. Validation at `max_depth: 8` then returned **valid**, **0 errors**, **0 warnings**, and **complete inspection**. I read the entire report; its remaining findings are informational Object-variable and event dependencies.

In a fresh Play session, I rediscovered Repair Station using the new session token, confirmed the initial waiting values, and activated the same **Run graph** button (`runGraph0`) through its UI Toolkit submit action.

| Actual observation | Before button | After button |
|---|---|---|
| Completed | false | **true** |
| RunCount | 0 | **1** |
| Result | Waiting for Demo.Run | **Tutorial advanced** |
| Game View result | Waiting for Demo.Run | **Tutorial advanced** |
| Game View count | 0 runs | **1 run** |

The runtime variable read and captured Game view agree:

![Repair Station showing Tutorial advanced and 1 run](images/pipeline-repair-verified.png)

Verification is complete and Play Mode is exited. Final diagnostics shows idle Edit Mode and a clean DiagnoseAndRepair scene. The Console still contains the previously observed Unity Editor SearchDatabase exceptions; compilation is not failing, and the inspected output showed no Repair graph exception.

## 5. Ask the AI to inspect runtime variables

**Exact prompt**

```text
Read the showcase's graph paths, then enter Play Mode and inspect existing runtime variables. For Scene Embed, show Graph, Object, Scene, Application, and Saved scopes. Run Scene Macro and inspect the Graph Variables in each executed child using its child_path. Compare graph definition values with the runtime observations. Report the machine and Play session you selected, each scope's availability, and any pagination or observation limits. Finish in Edit Mode.
```

**Actual response**

Inspected the existing runtime variables in `GraphShowcase`, ran Scene Macro once, and finished in **Edit Mode** with the scene clean.

The selected Play session was `67fdc7666de042678f34e425828fe904`:

| Selected machine | Component instance ID | Graph |
|---|---:|---|
| Scene Embed | 33980 | Scene embedded counter |
| Scene Macro | 34026 | Shared Counter |

Both were active, enabled, listening, and had existing runtime data. These IDs and the session token identify the observations below; they must be rediscovered for a later Play session.

I first read the graph definitions at `max_depth: 8`. Scene Embed and Prefab Embed have only their root path `[]`. Scene Macro and the two prefab macro instances share SharedCounter, whose root has two child paths:

- Embedded step: `[16e8279e-3c09-43ae-bf2b-3210e50f9911]`
- Shared macro step, referencing SharedStep: `[6614b2d7-b358-4f95-9b46-ac2d825bbbea]`

The State Machine’s DemoStates graph also has four child paths: Ready `[3082009f-bcc7-4d86-baa9-7071658c64fb]`, Completed `[729b2314-2564-465e-ba88-d5c1f5d8e57a]`, Ready→Completed transition `[918b14d3-d13b-40ad-bc8d-5641d554addf]`, and Completed→Ready transition `[03f51fd7-ea85-491e-a9e9-02b24c4f7b2c]`.

For **Scene Embed**, I queried each scope separately before running Scene Macro:

| Scope | Available | Existing runtime values |
|---|---|---|
| Graph, root `[]` | true | `DemoGraphRole = "Scene embedded counter"` |
| Object | true | `RunCount = 0`; `Result = "Ready"` |
| Scene | true | `VsDemo.SceneTitle = "Graph Showcase"` |
| Application | true | `VsDemo.ApplicationStatus = "Showcase active"` |
| Saved | true | No variable records; the existing scope is empty |

Graph, Object, Scene, and Application reported `storage: runtime_memory`. Scene scope’s actual SceneVariables owner was instance **34010**. For Application and Saved, the returned owner ID **33980** identifies the requesting machine’s context, rather than a separate global component.

Saved reported **`merged_runtime_memory_not_disk`**. Its availability does not establish any persisted disk contents. The read did not load, create, or save variables.

Scene Embed’s runtime Graph value matches the saved `graph_definition` value, and its Object values match its saved Object definitions because I did not run its button. Its Scene and Application values agree with the graph’s Start-flow assignments; those are separate scopes from Graph Variables.

I then activated Scene Macro’s **Run graph** button (`runGraph1`) once. Its card displayed **Shared macro ran / 1 run**. Using the same selected component and Play session, I passed each returned `child_path` as `parent_element_guids` to read that child’s Graph Variables:

| Graph context | Available | Definition value | Observed runtime Graph value |
|---|---|---|---|
| Root `[]` | true | `DemoGraphRole = "External graph shared by several owners"` | Same |
| Embedded step `[16e8279e-3c09-43ae-bf2b-3210e50f9911]` | true | `DemoStepRole = "Embedded step"` | Same |
| Shared macro step `[6614b2d7-b358-4f95-9b46-ac2d825bbbea]` | true | `DemoStepRole = "Shared macro step"` | Same |

These were existing runtime scopes, all with `storage: runtime_memory`; definition values were not substituted for unavailable data. The role variables remain unchanged. Execution effects appear in Scene Macro’s **Object** scope: `RunCount = 1`, `Result = "Shared macro ran"`, and `DemoEmbeddedStep = "Embedded step ran"`. Its saved Object definitions were RunCount 0 and Result Ready; DemoEmbeddedStep was produced at runtime. Graph metadata matching its definition alone would not prove execution, while these Object values and the visible result provide the observed outcome.

Discovery and runtime machine discovery each used two pages of five records; I followed both cursors. All four unique graph-definition reads completed in one page each with no depth warnings. Every scope read completed in one page (`page_size: 5`, at most four records), with `complete: true`, no warnings, and no remaining cursor.

The runtime reads are snapshots at different observed frames, not one atomic snapshot or an execution history. Scene Embed scope reads occurred at frames 5542–5707; Scene Macro’s post-run scope reads at frames 9314–9965. No stale cursors or truncation occurred. Values changing between pages could require rereading, but no scope needed pagination here. Final diagnostics confirms idle Edit Mode and a clean GraphShowcase scene.

## 6. Ask the AI to clone and bind graphs and objects

**Exact prompt**

```text
Create a scratch folder under Assets. Preview and clone SharedCounter with share_macros and embed_macros into separate new assets. Read and validate both copies, then explain their nested graph references. On Binding Station, record DemoReference's existing object ID, clear the reference with a bind preview/apply, save this scene, and run the station. Return to Edit Mode, reassign the recorded reference with a fresh preview/apply, save, and run it again. Finally preview/apply binding one clone as the station's graph, save, and run it. Report the three visible results and which nested graphs remain shared. Finish in Edit Mode and restore the original station assignments and save the scene.
```

**Actual response**

Created both scratch clones, observed all three requested results, and restored Binding Station’s original assignments. The project is back in **Edit Mode**, and EditAndCompose is saved and clean.

The new assets are:

| Clone | Policy | Nested graph references |
|---|---|---|
| SharedCounter-Share.asset (`/private/tmp/uapp-prompt-runs/VsDemoPipelineAI/Assets/VisualScriptingScratch/SharedCounter-Share.asset`) | `share_macros` | Copies the root and Embedded step; retains the external SharedStep macro |
| SharedCounter-Embed.asset (`/private/tmp/uapp-prompt-runs/VsDemoPipelineAI/Assets/VisualScriptingScratch/SharedCounter-Embed.asset`) | `embed_macros` | Copies the root and both child graphs; both children are Embed with no macro targets |

I inspected each clone preview and applied it with that preview’s own revision. Both assets were saved by the clone command. The share policy reported **2 copied graphs / 20 elements**, with one shared macro; the embed policy reported **3 copied graphs / 27 elements**, with no shared macros.

Readback confirms the same child paths in both copies:

- `[16e8279e-3c09-43ae-bf2b-3210e50f9911]`: Embedded step, **Embed** in both.
- `[6614b2d7-b358-4f95-9b46-ac2d825bbbea]`: Shared macro step, **Macro** in the share copy and **Embed** in the embed copy.

The share copy’s second child still references the original SharedStep asset, `GlobalObjectId_V1-3-f124f7c72df2e4954af380c66d24c260-11400000-0`. Consequently, SharedStep remains shared with the original SharedCounter and its other owners. The copied roots and embedded children are independent copies. In the embed copy, SharedStep’s graph content is embedded, so it has no remaining external nested macro reference. Other non-graph asset references are preserved by both policies.

I read and validated both copies at `max_depth: 8`. Each graph read returned all 37 records; each validation returned all six records, **valid**, **0 errors**, **0 warnings**, and `inspection_complete: true`. All reads completed with no remaining cursor. The informational findings concern Object variables RunCount, DemoEmbeddedStep and Result, plus the external event dependency.

Before changing Binding Station, I recorded its original source as **Embed**, graph title **Inspect Object Variable Reference**, macro assignment **null**, and `DemoReference` as **Reference Beacon** with object ID:

`GlobalObjectId_V1-2-cb3486f5b30174533bcd6376dd791fc1-353236918-0`

Each binding change used a preview, inspection of before/after values, and an apply with that preview’s revision. I saved EditAndCompose before each fresh Play session, rediscovered Binding Station, and activated its actual **Run graph** button (`runGraph0`).

| Test | Actual visible result | Actual runtime values |
|---|---|---|
| Clear DemoReference to null | **Reference cleared / 1 run** | `RunCount = 1`, `Result = "Reference cleared"`, `DemoReference = null` |
| Reassign the recorded Reference Beacon | **Reference assigned / 1 run** | `RunCount = 1`, `Result = "Reference assigned"`, reference points to Reference Beacon |
| Bind SharedCounter-Share as the station’s Macro | **Shared macro ran / 1 run** | `RunCount = 1`, `Result = "Shared macro ran"`, `DemoEmbeddedStep = "Embedded step ran"`; Beacon reference remains assigned |

For final restoration, I exited Play Mode and used **Unity Undo (`Undo.PerformUndo`)** on the graph-binding operation. The preserved embedded graph became active again. Discovery and graph readback confirmed the original **Embed** graph, `DemoReference` pointing to the exact recorded object ID, and saved Object definitions `RunCount = 0`, `Result = "Ready"`. Public machine-binding readback confirmed source **Embed** and macro **null**. I saved the scene successfully; final diagnostics reports idle Edit Mode and `active_scene_dirty: false`.

The two requested clone assets remain in `Assets/VisualScriptingScratch`. Clone asset creation does not support Unity Undo; assignment Undo did work and was verified here.

## 7. Ask the AI to edit nodes and connections

**Exact prompt**

```text
Duplicate SharedStep into a new Assets scratch folder for an edit exercise. Read the copy's current edit_revision before each batch. Preview and apply adding a bool Literal and an If node, configuring the Literal, moving the If, connecting the Literal output to the If condition, changing the Result setter's name input to DemoScratchResult, and setting a DemoScratch Graph Variable. Read back the changes and verify Undo/Redo. In a second batch, disconnect the new connection, remove both new nodes, remove DemoScratch, and restore the original setter name. Save and reload the copy and validate it. Also preview/apply moving an existing node in Prefab Embed's scene instance, verify the prefab override and Undo, and restore it. Report the actual diffs and final validation results.
```

**Actual response**

Completed the edit exercise in SharedStep-Exercise.asset (`/private/tmp/uapp-prompt-runs/VsDemoPipelineAI/Assets/VisualScriptingEditExercise/SharedStep-Exercise.asset`), saved and reloaded the restored copy, and restored Prefab Embed’s scene instance. Final diagnostics confirms idle **Edit Mode** and a clean, saved EditAndCompose scene.

I created a new `Assets/VisualScriptingEditExercise` folder and duplicated SharedStep through a clone preview/apply. Before each edit batch, I read the copy’s current `edit_revision` and used that exact revision for its preview and apply.

The first batch used revision `960AD2B89F7BECF61750E169C6427A3A252222EC54E43D1CB24AD7245CD288AB`. Its preview and applied diff contained these net changes:

| Element | Actual change |
|---|---|
| Bool Literal `e9744c52-91f1-4f7c-98ca-816ebc4d4401` | Added at `(0, 500)`, Boolean value **true** |
| If `f57e8d60-238c-472a-a1ba-2d8085b151d8` | Added, final position **`(460, 440)`** |
| Value connection `6931ec0e-318f-4239-8046-a1bf8ee55462` | Added: Literal `output` → If `condition` |
| Existing setter `ba537690-3f1b-43fd-9014-0c78274c7280` | `name` default: `Result` → **`DemoScratchResult`** |
| Graph Variable `DemoScratch` | Added with Boolean value **true** |
| Graph element count | **7 → 10** |

Within that batch I created the Literal as false and configured it to true, and created the If at `(500, 500)` before moving it. The diff reports their final added states. Preview and apply both validated successfully with zero errors and warnings, and readback confirmed every change.

**Undo** restored the original seven elements, Result setter name, and original revision, with no new nodes, connection, or DemoScratch variable. **Redo** restored all additions and revision `8F50F1032604C486D6ED12E3115DC1CF37671593D49EAF272A0C307551FC69C4`. I verified both through graph reads.

Using that freshly read revision, the second batch explicitly disconnected the new connection first, removed both new nodes, removed DemoScratch, and changed the setter name back to Result. Its six diff records reversed the first batch’s net changes, including **10 → 7** elements. Preview and apply returned the original edit revision.

I saved the copy with `AssetDatabase.SaveAssetIfDirty`, unloaded it with `Resources.UnloadAsset`, and loaded it again through `AssetDatabase.LoadAssetAtPath`. Reload succeeded and the asset was clean. A graph read after reload again showed the original revision and structure.

For **Prefab Embed’s scene instance**, I read its current revision and previewed/applied one `move_node` operation on Custom Event `856f0475-9dd5-4dd7-b1b9-593b90b3e37f`: **`(0, 0) → (32, 48)`**. The diff contained only that position change, and readback confirmed it.

Native `PrefabUtility.GetPropertyModifications` inspection showed a new **ScriptMachine override at `_data._json`**, in addition to the eleven existing name/transform modification records. After **Undo**, graph readback restored `(0, 0)` and the original instance edit revision. The ScriptMachine override disappeared, and the original eleven modification records remained. I then saved the restored scene successfully.

| Final validation at depth 8 | Status | Errors | Warnings | Inspection complete |
|---|---|---:|---:|---|
| Saved, reloaded scratch copy | **valid** | 0 | 0 | true |
| Restored Prefab Embed scene instance | **valid** | 0 | 0 | true |

I read both complete reports. The copy has one informational Object Result dependency; the prefab instance has informational dependencies for RunCount get/set, Result, and its external event. No report pages remained (`complete: true`, `next_cursor: null`). These results establish supported structural validity. The requested scratch copy remains saved with its original graph content, and the temporary prefab graph override is removed.

## 8. Ask the AI to work with settings and a custom Unit

**Exact prompt**

```text
Inspect the sample Demo Has Reference C# Unit, its graph usage, and its typed ports. Read Visual Scripting settings and record their current values. Preview and apply flipping AOT Safe Mode, verify the saved setting and Undo/Redo, then restore and verify the original value. Explicitly preview/apply node regeneration and poll the existing job until it finishes. Check the Console and confirm the Unit is available in the node library. Run both CustomNodeLab cards and report their actual results. Finish in Edit Mode and report any incomplete save, job, or runtime verification.
```

**Actual response**

Completed the requested checks in Unity 6000.3.16f1 / Visual Scripting 1.9.11 on CustomNodeLab. All Unity inspection and actions used Pipeline MCP through the stdio relay. The Editor stayed in ordinary GUI mode and unfocused.

The C# Unit is `UAppToolKit.VisualScriptingMcp.Samples.DemoHasReference`, titled “Demo Has Reference,” category `UAppToolKit/Demo`. Its `target` ValueInput is `UnityEngine.Object`, default null, with AllowsNull; `isAssigned` ValueOutput is `System.Boolean`. Definition declares Requirement(target, isAssigned) and evaluates `flow.GetValue<Object>(Target) != null`. There are no control ports. Both loaded embedded graphs contain a defined instance with those exact typed ports and no definition error. Each graph reads Object Variable DemoReference into target, routes isAssigned into an If, and handles CheckReference by incrementing RunCount and writing Result. Assigned Reference starts with the scene GameObject Reference Beacon; Empty Reference starts with null.

Visual Scripting settings initially had AOT Safe Mode=true, 62 assembly entries and 51 type entries. Their exact initial supported values are recorded below. Initial and restored settings revision: `79391F7D2576C24F1655F329E22760DD7656E1099F3F2A9E48018C00DEE2F847`. Storage: `ProjectSettings/VisualScriptingSettings.asset`; Undo scope: current_domain_settings_only. Initial regeneration state was not_requested.

I previewed and applied only AOT Safe Mode true→false using vs_update_settings and the expected settings revision. Preview reported only that diff; apply reported applied=true/save_status=saved. I used ordinary Unity Undo.PerformUndo and Undo.PerformRedo, reading the actual disk file in the same C# bridge invocation immediately after each operation. I then previewed/applied false→true and read the actual disk file on the immediately following bridge invocation. No custom save, file rewrite, focus activation, or batch-mode workaround was used.

| Check | Disk read UTC, 2026-11-17 | File modified UTC | Saved aotSafeMode |
| --- | --- | --- | --- |
| Baseline | 22:11:16.850657 | 22:07:55.6690201 | true |
| Apply | 22:11:33.758276 | 22:11:30.0042759 | false |
| Undo | 22:11:48.436617 | 22:11:48.1784601 | true |
| Redo | 22:11:55.542286 | 22:11:55.4101684 | false |
| Restoration | 22:12:19.271050 | 22:12:14.7753021 | true |
| Final, after Play Mode | 22:15:53.959553 | 22:12:14.7753021 | true |

Undo operation began at 22:11:48.160862Z and its disk read completed about 276 ms later; Redo began at 22:11:55.405437Z and its disk read completed about 137 ms later. Apply and restoration disk reads were the next bridge requests, about 3.754 s and 4.496 s after the file modification respectively. Every listed snapshot had Application.isBatchMode=false, InternalEditorUtility.isApplicationActive=false, Application.isFocused=false and EditorWindow.focusedWindow=null.

Baseline, Undo, restoration and final file SHA-256 were identical: `96F0DE0F215D082157ACB40894640C088849CBE8A669B0F5567266F5BC1238F1` (4539 bytes). Apply and Redo had SHA-256 `639819E92D59288B551FF0A5E9BEE533C13CF36E39953B6181F881EBF032E5D7` (4540 bytes). vs_get_settings agreed after each operation; final settings including ordered assemblies/types equal the baseline exactly.

I explicitly previewed node regeneration with changes={} / regenerate_nodes=true, then applied it once. Job `80397ceac46541ee86d493ac79ea2db8` went from pending to completed on the restored revision, errors=0/warnings=0. One vs_get_settings poll received a 504 after the bridge's 60-second timeout while Unity's synchronous rebuild blocked requests. I submitted another read-only poll of that same job, which returned completed; I did not request a duplicate rebuild. The completed message limits its claim to database generation, not graph execution or Player compatibility.

The actual regenerated UnitBase.options library was loaded with 31,211 options. It contained one matching DemoHasReference option, category UAppToolKit/Demo, search label “Demo Has Reference,” zero control inputs/outputs, value input type UnityEngine.Object and value output type System.Boolean.

In Play Mode, both discovered ScriptMachines were active, enabled, listening and had runtime data. I read the actual panel rows and enabled Run graph buttons, then invoked DemoPanel.Trigger(0) and Trigger(1), the same public method wired by those buttons. Both returned true. Before execution both UI cards showed Ready / 0 runs. After execution:

| Actual card | UI result | UI count | Independently read runtime Object Variables |
| --- | --- | --- | --- |
| Assigned Reference | Reference assigned | 1 run | RunCount=1; Result="Reference assigned"; DemoReference=Reference Beacon |
| Empty Reference | Reference cleared | 1 run | RunCount=1; Result="Reference cleared"; DemoReference=null |

Runtime verification used vs_runtime_variables against the discovered instance IDs and play_session_id, and returned complete=true. Results were produced by the live graphs, not inferred from their definitions.

The Console buffer contained historical entries from before this run (8 errors, 4 warnings, 22 logs through cursor 34). I retained them. No new entries appeared after cursor 34 during these checks; the final live Console ground truth had 0 errors, 0 warnings, 0 logs and compilationFailed=false.

Final verification at approximately 22:15:54Z: Edit Mode, stopped, ready, unpaused, not compiling/updating and no pending Play transition; CustomNodeLab scene dirty=false; Run In Background restored to false. No save, regeneration job, or requested card runtime verification remains incomplete. The resolved poll timeout is recorded above. Physical mouse interaction, a Player build, AOT compilation and full execution tracing were not tested; full_execution_trace_supported=false.

Exact initial supported settings (final values are identical):

```json
{
  "aot_safe_mode": true,
  "assemblies": [
    "mscorlib",
    "Assembly-CSharp-firstpass",
    "Assembly-CSharp",
    "UnityEngine",
    "UnityEngine.CoreModule",
    "UnityEngine.InputModule",
    "UnityEngine.ClusterInputModule",
    "UnityEngine.InputLegacyModule",
    "UnityEngine.PhysicsModule",
    "UnityEngine.Physics2DModule",
    "UnityEngine.TerrainPhysicsModule",
    "UnityEngine.VehiclesModule",
    "UnityEngine.AudioModule",
    "UnityEngine.AnimationModule",
    "UnityEngine.VideoModule",
    "UnityEngine.DirectorModule",
    "UnityEngine.Timeline",
    "UnityEngine.ParticleSystemModule",
    "UnityEngine.ParticlesLegacyModule",
    "UnityEngine.WindModule",
    "UnityEngine.ClothModule",
    "UnityEngine.TilemapModule",
    "UnityEngine.SpriteMaskModule",
    "UnityEngine.TerrainModule",
    "UnityEngine.ImageConversionModule",
    "UnityEngine.TextRenderingModule",
    "UnityEngine.ClusterRendererModule",
    "UnityEngine.ScreenCaptureModule",
    "UnityEngine.AIModule",
    "UnityEngine.UI",
    "UnityEngine.UIModule",
    "UnityEngine.IMGUIModule",
    "UnityEngine.UIElementsModule",
    "UnityEngine.StyleSheetsModule",
    "UnityEngine.VR",
    "UnityEngine.VRModule",
    "UnityEngine.ARModule",
    "UnityEngine.HoloLens",
    "UnityEngine.SpatialTracking",
    "UnityEngine.GoogleAudioSpatializer",
    "UnityEngine.Networking",
    "UnityEngine.Analytics",
    "UnityEngine.Advertisements",
    "UnityEngine.Purchasing",
    "UnityEngine.UnityConnectModule",
    "UnityEngine.UnityAnalyticsModule",
    "UnityEngine.GameCenterModule",
    "UnityEngine.AccessibilityModule",
    "UnityEngine.AndroidJNIModule",
    "UnityEngine.AssetBundleModule",
    "UnityEngine.FileSystemHttpModule",
    "UnityEngine.JSONSerializeModule",
    "UnityEngine.UmbraModule",
    "Unity.Timeline",
    "Unity.Timeline.Editor",
    "Cinemachine",
    "com.unity.cinemachine.editor",
    "Unity.InputSystem",
    "Unity.TextMeshPro",
    "Unity.VisualScripting.Core",
    "Unity.VisualScripting.Flow",
    "Unity.VisualScripting.State"
  ],
  "types": [
    "System.Object, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
    "System.Boolean, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
    "System.Int32, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
    "System.Single, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
    "System.String, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
    "UnityEngine.Vector2, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Vector3, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Vector4, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Quaternion, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Matrix4x4, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Rect, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Bounds, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Color, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.AnimationCurve, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.LayerMask, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Ray, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Ray2D, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.RaycastHit, UnityEngine.PhysicsModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.RaycastHit2D, UnityEngine.Physics2DModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.ContactPoint, UnityEngine.PhysicsModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.ContactPoint2D, UnityEngine.Physics2DModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.ParticleCollisionEvent, UnityEngine.ParticleSystemModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.SceneManagement.Scene, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Application, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Resources, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Mathf, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Debug, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Input, UnityEngine.InputLegacyModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Touch, UnityEngine.InputLegacyModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Screen, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Cursor, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Time, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Random, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Physics, UnityEngine.PhysicsModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Physics2D, UnityEngine.Physics2DModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.SceneManagement.SceneManager, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.GUI, UnityEngine.IMGUIModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.GUILayout, UnityEngine.IMGUIModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.GUIUtility, UnityEngine.IMGUIModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Audio.AudioMixerGroup, UnityEngine.AudioModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.AI.NavMesh, UnityEngine.AIModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.Gizmos, UnityEngine.CoreModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.AnimatorStateInfo, UnityEngine.AnimationModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.EventSystems.BaseEventData, UnityEngine.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.EventSystems.PointerEventData, UnityEngine.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "UnityEngine.EventSystems.AxisEventData, UnityEngine.UI, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null",
    "System.Collections.IList, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
    "System.Collections.IDictionary, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089",
    "Unity.VisualScripting.AotList, Unity.VisualScripting.Core, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "Unity.VisualScripting.AotDictionary, Unity.VisualScripting.Core, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null",
    "System.Exception, mscorlib, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089"
  ]
}
```

## Earlier batchmode settings repeat

Historical result before the synchronous persistence fix. The current GUI result is prompt 8 above.

Inspected `DemoHasReference.cs`, both CustomNodeLab graph usages, and their typed ports. The Unit is “Demo Has Reference” under `UAppToolKit/Demo`: nullable `target` (`UnityEngine.Object`, default null) → `isAssigned` (`System.Boolean`), with a requirement from input to output and no control ports. It evaluates Unity Object non-null comparison. Both graphs wire Object `DemoReference` into `target` and `isAssigned` into If; Assigned Reference contains Reference Beacon, Empty Reference contains null.

Recorded current settings in `settings-original-08-final.json`: AOT Safe Mode true, 62 ordered assembly entries, 51 ordered type entries. Initial/final revision: `79391F7D2576C24F1655F329E22760DD7656E1099F3F2A9E48018C00DEE2F847`.

Previewed and applied true → false. Apply reported `scheduled_by_visual_scripting`; subsequent MCP file reads confirmed saved false. Undo produced live/saved true; Redo produced live/saved false. Previewed/applied restoration and verified live/saved true. The original true value also survived the Play Mode reload. All persistence checks used the normal asynchronous save; no synchronous-save workaround was used.

Explicitly previewed/applied regeneration with empty settings changes. Polled the existing job `919836da285049baa234e71ce8840011` to completed at the original revision: 0 errors, 0 warnings. Confirmed the Unit in the node library through public `UnitBase.Subset` using the actual graph context. Console was empty before and after runtime verification.

Ran both CustomNodeLab cards through DemoPanel’s public Trigger method, then read actual runtime Object Variables via vs_runtime_variables:

| Card | RunCount | Actual result |
| --- | --- | --- |
| Assigned Reference | 1 | Reference assigned |
| Empty Reference | 1 | Reference cleared |

Finished in stable Edit Mode, not compiling, scene clean, original AOT true in memory and on disk. No incomplete save, regeneration job, or card runtime verification remains. The batch editor initially opened an empty scene; CustomNodeLab was opened through the MCP bridge before inspection. No Player build was tested.

## Initial settings attempt

The first GUI attempt correctly reported incomplete disk persistence. It was first repeated in batchmode, then fixed and verified in ordinary GUI; see prompt 8 above.

Inspected `DemoHasReference.cs`, both CustomNodeLab graphs, and the live typed ports through Pipeline MCP. “Demo Has Reference” is under `UAppToolKit/Demo`: nullable `target` input (`UnityEngine.Object`, default null), Boolean `isAssigned` output, no control ports. The output uses Unity Object non-null comparison. Both graphs connect Object `DemoReference` to the Unit’s target and its Boolean output to If; they increment `RunCount`. Assigned Reference holds Reference Beacon; Empty Reference holds null.

Recorded the supported project settings: AOT Safe Mode initially `true`, 62 ordered assembly entries and 51 ordered type entries. Full values are in `settings-original-08.json`. Initial and final revision: `79391F7D2576C24F1655F329E22760DD7656E1099F3F2A9E48018C00DEE2F847`.

Previewed/applied AOT `true → false`, verified live false, verified Undo → true and Redo → false, then previewed/applied restoration and verified true. Disk verification is incomplete: apply reported `scheduled_by_visual_scripting`, but the settings file was initially absent even after focus/save/repaint. A second preview/apply of false before Play Mode still yielded saved `aotSafeMode=true` after the transition, and the final live setting was true again after reload. The flipped false value was not verified on disk. Final live and saved settings both equal the original true value.

Explicitly previewed/applied node regeneration with empty changes, then polled the existing job `e6e7a7b1376248a0bcbadf12df61b474` until completed at the original revision, with 0 errors and 0 warnings. Verified `Demo Has Reference` in the node library through `UnitBase.Subset` using the real graph context.

Ran both cards using DemoPanel’s public Trigger method in stable Play Mode and read actual runtime Object Variables through vs_runtime_variables:

| Card | RunCount | Result |
| --- | --- | --- |
| Assigned Reference | 1 | Reference assigned |
| Empty Reference | 1 | Reference cleared |

Console recorded an Input Manager deprecation warning and a QuickSearch SearchDatabase indexing ArgumentOutOfRangeException at line 445; the exception stack does not identify the custom Unit. Pipeline briefly returned EDITOR_NOT_RUNNING during the Play Mode reload, then reconnected; the card trigger was retried only after that failed transport call and both cards ran once.

Finished in stable Edit Mode, ready/not compiling, CustomNodeLab clean. No unfinished regeneration job or runtime card verification remains. Flipped-setting disk verification remains incomplete. No Player build was tested.
