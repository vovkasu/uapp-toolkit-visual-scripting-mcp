# Visual Scripting Demo: actual AI prompt validation

Date: 2026-11-17. Unity 6000.3.16f1, Visual Scripting 1.9.11,
CoplayDev MCP 10.1.0, Unity CLI 1.0.1 / Pipeline 1.0.0.

## Method

The eight prompt blocks were extracted from the sample README and supplied
verbatim to AI agents. Each walkthrough used one exclusive MCP channel and its
own disposable Unity project. The agents did not receive the expected-response
blocks, scripted solutions, or the independent verification helpers. They chose
their own tool calls, inspected returned data, performed the requested changes,
and wrote actual answers. The generic client only initialized MCP, selected the
exact project, relayed requests, and retained request/response transcripts.

Samples were imported with Unity Package Manager's Sample API. Prompts 1–7 ran
sequentially in two GUI Editors, with each stated scene prepared beforehand.
Prompt 8 used two additional disposable projects because it changes project
settings. Its incomplete GUI attempts were retained and repeated in batchmode.
Prompt 1 was also repeated by fresh AI contexts after improving adapter metadata.
All eight prompt texts remained unchanged.

The coordinator checked the answers against MCP transcripts and Unity state.
Independent Unity readback and actual UI button events verified the shared
counters/state transitions, validation statuses, stalled graph, saved repair,
restored bindings, and the saved edit copy/prefab restoration. The AI agents also used the selected bridge for ordinary
Unity actions: component inspection, Play Mode, Undo/Redo, saving, Console, and
button callbacks. Package graph mutations used the `vs_*` APIs.

## Results

| # | Prompt | CoplayDev MCP | Pipeline MCP | Observed evidence |
| --- | --- | --- | --- | --- |
| 1 | Discover and explain | Passed | Passed | Five graph assets, two prefab machines, six scene machines; Embed/Macro sharing, both SharedCounter children and all four State child paths. Independent Play check: A twice/B once gives 2/1; Ready → Completed → Ready. Definition-derived predictions are identified as predictions. |
| 2 | Validate | Passed | Passed | SharedCounter deep: valid, 0 errors/warnings, complete. Depth 0: inconclusive/incomplete. MissingNestedMacro: invalid, `missing_graph`, one error and one finding warning. All report pages read. |
| 3 | Diagnose | Passed | Passed | Button sends `Demo.Run`; handler listens for `Demo.Disabled`. Trace started before reproduction and observed `Demo.Run`; Completed=false, RunCount=0, waiting UI. Trace stopped; no repair made. |
| 4 | Repair and replay | Passed | Passed | One handler-default edit, reviewed preview/apply, Undo/Redo, saved asset and valid structural report. Fresh Play: Completed=true, RunCount=1, Result/UI=`Tutorial advanced`. Independently replayed in both projects. |
| 5 | Runtime variables | Passed | Passed | All five Scene Embed scopes available; scene/application values observed. Saved is empty merged runtime memory, not a disk claim. Both executed Macro child Graph scopes match definitions; Object effects show `Shared macro ran` and `Embedded step ran`. |
| 6 | Clone and bind | Passed | Passed | Both clone policies saved/read/validated. Shared copy retains SharedStep; embedded copy has no nested Macro reference. Three real runs show `Reference cleared`, `Reference assigned`, `Shared macro ran`, each count 1. Original Embed, null Macro and exact Beacon reference restored and independently checked. |
| 7 | Edit operations and prefab override | Passed | Passed | All nine edit operations; first batch adds two nodes, a connection and variable, changes setter name, moves/configures nodes. Cleanup restores the graph. Undo/Redo and saved reload verified; copy valid 0/0. Instance move creates `_data._json` override; Undo removes it. Independent final copy and prefab revision checks pass in both projects. |
| 8 | Settings and custom Unit | Passed in batchmode | Passed in batchmode | Live and disk AOT sequence true → false → Undo true → Redo false → restored true. Regeneration jobs completed with 0 errors/warnings. Unit library and typed ports confirmed. Both cards produced assigned/cleared results, count 1; finished clean Edit Mode. |

Read the captured [CoplayDev answers](DemoPromptValidation/coplay-answers.md) and
[Pipeline answers](DemoPromptValidation/pipeline-answers.md), including exact
prompts, observed IDs, values, diffs, jobs, pagination and limitations. The initial
incomplete settings answers are retained after the completed answers. Screenshots
referenced by those answers are retained alongside them.

## Problems found and changes

- CoplayDev inferred `JObject Changes` as an array because it implements
  `IEnumerable`; native object requests failed before reaching the handler.
  The adapter's metadata property now uses `object`, while the core still
  validates a JSON object. A regression test checks the actual CoplayDev tool
  discovery metadata.
- Pipeline's opaque JSON argument description did not expose discovery scopes
  or nested-path fields. The first AI guessed `scene` and several path names,
  then recovered through the contract. Command metadata now lists the actual
  scopes and `parent_element_guids`, and identifies `child_path` as its input.
- A CoplayDev AI passed null as the first cursor, which its generated native
  schema rejects. Both adapter descriptions and the sample guide now say to
  omit the first cursor, follow nonempty `next_cursor`, and stop at null.

The source package compiled and its full EditMode suite passed:
**92 passed, 0 failed, 0 skipped**, including the new adapter-schema regression.
The [native Test Runner summary](DemoPromptValidation/regression.json) records
the exact result; raw XML and MCP transcripts were retained locally with the
validation projects under `/private/tmp/uapp-prompt-runs`.

## Limits and recovery evidence

- In the unfocused GUI settings runs, live apply/Undo/Redo succeeded but the
  intermediate false value was not confirmed on disk before reload. The agents
  reported that gap. Visual Scripting's public save API schedules a delayed
  Editor callback. Batchmode repeats verified every disk transition using that
  normal asynchronous save, without a private synchronous-save workaround.
  The sample README documents this observed limitation.
- CoplayDev's already-running server retained its old generated native schema
  while older project adapters remained connected. The documented
  `execute_custom_tool` route accepted object requests and completed the full
  settings exercise. After closing the older adapter and restarting that server,
  a fresh native schema published `Changes` as an object. A direct native
  object-shaped preview succeeded (`applied: false`), and a fresh settings read
  confirmed the original revision/value unchanged. The captured CoplayDev
  answers include this final verification after the earlier limitation.
- Play/domain reload briefly removed the exact Editor instance from either
  bridge. Agents waited, reconnected and rediscovered the same project/session;
  they did not send requests to a different project or switch channels.
- Successful acceptance follows recovery where necessary; it does not mean
  every exploratory call succeeded. The fresh discovery repeats avoided the
  original scope/path/null-cursor errors. Temporary `editor_busy` responses and
  a bridge inspection script's missing namespace were corrected before the
  final complete reads.
- GUI startup/reloads logged a Unity QuickSearch `SearchDatabase`
  `ArgumentOutOfRangeException`, recorded before sample actions. The completed
  batchmode settings repeats had empty Consoles. This Editor indexing issue
  remains outside the package fix.
- Temporary scenes can be reserialized by binding/Undo round trips. Restoration
  checks graph records and assignments, rather than promising byte-identical
  YAML. Source sample assets retain the intended broken repair fixture.
- This validates Editor workflows. It is not a Player build or target-device
  certification, and structural validation does not establish runtime success.
