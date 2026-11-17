# Settings persistence in the background GUI Editor

Follow-up: 2026-11-18 (Europe/Kyiv), fix `8edd66b`.
Unity 6000.3.16f1 / Visual Scripting 1.9.11; CoplayDev 10.1.0 and Pipeline 1.0.0.

## Confirmed cause

The original problem was reproduced through both MCP channels in ordinary
unfocused GUI Editors. Both reported `Application.isBatchMode=false`,
`InternalEditorUtility.isApplicationActive=false`, and initialized/used Visual
Scripting. After applying AOT true → false:

| Observation | CoplayDev MCP | Pipeline MCP |
| --- | --- | --- |
| Live AOT | false | false |
| File AOT | true | true |
| Queued `SerializeProjectSettingsAssetToDisk` delegates | 1 | 1 |
| Custom serialization in progress | false | false |

Visual Scripting 1.9.11's `SaveProjectSettingsAsset(true)` still adds its save
delegate to `EditorApplication.delayCall`. The argument bypasses the dirty check;
it does not make the write synchronous. The background Editors serviced MCP
commands without draining this GUI callback queue. The usage flag and an active
custom serializer were ruled out by the observations above.

A direct diagnostic save of the existing DictionaryAsset through the native
serialized-file API immediately persisted false while the Editor remained
unfocused and its original delayed callback remained queued. This isolated the
blocked flush from the setting mutation and serialization itself.

## Fix

`VsSettingsStorage` uses the same native serialized-file API as Visual Scripting
to persist its existing whole DictionaryAsset before an operation returns.
It then loads an independent copy and checks the changed values. Successful
changed apply returns `save_status=saved`; preview/no-op returns `not_requested`.
Undo and Redo reuse the same verified write path.

The compatibility check for VS's internal backing-asset property runs before
mutation. The actual asset is resolved after public metadata.Save, retaining
VS's normal reload/create behavior if Unity unloaded it. The dictionary is not
reconstructed from the API's three supported settings: foreign keys, favorites,
other metadata and object references remain in the whole asset. No serialized
JSON is patched, global delayed callbacks executed, focus changed, or package
versions upgraded to make saving work.

## Regression evidence

Two synchronous regression cases cover a live backing asset and a deliberately
unloaded backing asset. They seed and independently verify a disk baseline,
then check the disk immediately after Apply, Undo and Redo without any yield or
Editor tick. They also verify the foreign sentinel key, favorites, list order,
and the recreated asset in the unload case. A separate type-list test now
checks immediate disk persistence and Undo.

Both final cases failed against the old implementation with **Expected false,
actual true**. With the fix, the full native EditMode suite passed in both
ordinary GUI Editors: **94 passed, 0 failed, 0 skipped** in each. These are two
runs of the same 94 tests. The native summaries are retained alongside the demo
validation evidence. Compilation had no errors; the existing CS0618 warning in
VsRuntimeReader was unchanged.

## Exact prompt 8 through both MCP channels

The unchanged eighth README prompt was repeated by separate fresh AI contexts
through exclusive MCP channels, without example answers or solutions. The
ordinary Editors stayed unfocused. Neither agent used direct save workarounds,
batchmode or window activation.

| Check | CoplayDev MCP | Pipeline MCP |
| --- | --- | --- |
| Full prompt 8 in ordinary unfocused GUI | Passed | Passed |
| Apply / Undo / Redo / restore: live and disk | false / true / false / true | false / true / false / true |
| Restored whole-file SHA-256 matches baseline | Yes | Yes |
| Explicit regeneration | Completed, 0 errors/warnings | Completed, 0 errors/warnings |
| Typed Unit in node library | Unity Object → Boolean | Unity Object → Boolean |
| Actual assigned / empty card results | Reference assigned / Reference cleared | Reference assigned / Reference cleared |
| Actual card counts | 1 / 1 | 1 / 1 |
| Final state | Clean idle Edit Mode, original settings | Clean idle Edit Mode, original settings |

The same regeneration jobs completed after the clients recovered from a long
synchronous rebuild: `36ddbf04f9fd46fb88963bca39c0a60e` (CoplayDev) and
`80397ceac46541ee86d493ac79ea2db8` (Pipeline). The bridge polling timeout or
temporary disconnection did not trigger a second apply. Final Consoles had
zero errors/warnings; earlier transport messages and an exploratory null-context
library query are disclosed in the actual answers. No requested save, job or
runtime verification remained incomplete.

Read the actual [CoplayDev response](DemoPromptValidation/coplay-answers.md#8-ask-the-ai-to-work-with-settings-and-a-custom-unit)
and [Pipeline response](DemoPromptValidation/pipeline-answers.md#8-ask-the-ai-to-work-with-settings-and-a-custom-unit).
Both include timestamped disk reads, focus/mode flags, file hashes and runtime
results. Earlier incomplete GUI and successful batchmode responses remain as
historical evidence after the current answers.

The coordinator then quit and reopened both ordinary GUI Editors independently.
Both still reported AOT true, batch=false and Editor inactive, and the settings
file retained the exact original SHA-256. The [restart readback](DemoPromptValidation/gui-restart-verification.json)
records these results. Native regression summaries: [CoplayDev GUI project](DemoPromptValidation/coplay-gui-regression-94.json)
and [Pipeline GUI project](DemoPromptValidation/pipeline-gui-regression-94.json);
the [pre-fix failures](DemoPromptValidation/gui-save-regression-before.json)
show that both immediate-persistence cases detect the old defect.

`InternalEditorUtility.isApplicationActive` measures Editor application activity
in these checks. `Application.isFocused` was observed true in Edit Mode while
the Editor was inactive and its focused window null, so it was not treated as
proof that Unity was the OS foreground application.
