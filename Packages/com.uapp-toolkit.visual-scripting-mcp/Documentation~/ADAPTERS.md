# Optional MCP adapters

One package contains the core and two independently compiled Editor-only adapters.
The core owns validation, editing, settings, Play session IDs, and the trace buffer.
Adapters only register commands and forward requests; they do not duplicate state.

## Assemblies and activation

| Folder | Assembly | Additional reference | Package version condition |
| --- | --- | --- | --- |
| Editor/Core | UAppToolKit.VisualScriptingMcp.Editor | Visual Scripting and Newtonsoft.Json | Always available |
| Editor/CoplayDev | UAppToolKit.VisualScriptingMcp.CoplayDev.Editor | MCPForUnity.Editor | com.coplaydev.unity-mcp `[10.1.0,11.0.0)` |
| Editor/UnityCli | UAppToolKit.VisualScriptingMcp.UnityCli.Editor | Unity.Pipeline.Attributes | com.unity.pipeline `0.9.1-exp.1` or later |

Each adapter's asmdef creates its own version symbol and uses it as a defineConstraint:
`UAPP_VS_MCP_COPLAYDEV` or `UAPP_VS_MCP_UNITY_CLI`. The symbol belongs to that assembly;
there are no global scripting symbols to configure and no bridge conditionals in core code.
These conditions control whether the adapter compiles. The minimum Pipeline
version has no upper bound. Future releases still need to provide the APIs
referenced by the adapter. An absent or out-of-range bridge excludes its adapter.

`references` links assemblies; it does not install packages. `versionDefines`
checks installed UPM package versions, not the externally installed Unity CLI binary.
Neither bridge appears in this package's mandatory dependencies.

## Installation choices

| Installed bridges | Compiled adapters | Exposed operations |
| --- | --- | --- |
| Neither | None | Core C# API only |
| CoplayDev only | CoplayDev | 14 CoplayDev custom tools |
| Pipeline only | UnityCli | 14 Pipeline commands, available through CLI/MCP |
| Both | Both | Both transports, sharing one core and one active trace |

Install bridge packages using their normal installation instructions. CoplayDev's
Git source must be provided by the consuming project; this package does not attempt
to resolve it from a registry. Unity CLI requires an installed CLI binary plus
`com.unity.pipeline` in the project. Updating the CLI alone does not update Pipeline.
For tests, also install Unity Test Framework and enable this package in `testables`
when consuming it as a Git/local dependency. Core tests do not reference either bridge.

## Command mapping

Every CoplayDev `vs_<operation>` maps to Pipeline group `vs`, command `<operation>`.
Examples: `vs_diagnostics` → `vs diagnostics`; `vs_read_graph` → `vs read_graph`;
`vs_trace_start` → `vs trace_start`. All 14 operations use this mapping.
Pipeline registration explicitly requires Unity's main thread.

CoplayDev keeps its existing tool schemas and `execute_custom_tool` route:

```json
{"tool_name":"vs_read_graph","parameters":{"target":"<target>","max_depth":2}}
```

Unity CLI takes one structured JSON-valued flag rather than positional JSON:

```bash
unity command vs read_graph --parameters '{"target":"<target>","max_depth":2}' --project-path /path/to/project --format json
unity command vs diagnostics --project-path /path/to/project --format json
```

For diagnostics, get_settings, list_graphs, and runtime_status, `parameters` may
be omitted. Other commands require the object. Core validation remains authoritative
for fields, aliases, types, explicit nulls, limits, preview defaults, and revisions.
Pipeline binds the value to JObject. Unity CLI beta.15 and 1.0.0 advertise this
parameter as a **string** in the MCP catalog, so MCP clients should send JSON text:

```json
{"name":"vs_read_graph","arguments":{"parameters":"{\"target\":\"<target>\",\"max_depth\":2}"}}
```

The individual keys and nested operation schemas remain specified in
[CONTRACT.md](CONTRACT.md). This avoids depending on clients accepting object values
against a string schema.

Use `unity mcp --project-path /path/to/project` to expose Pipeline to an MCP client.
Rediscover its current tool catalog and use the published name/schema rather than
assuming CoplayDev's dispatch tool exists. Multiple installed bridges do not call
one another or create duplicate core observers.

## Responses and shared state

CoplayDev preserves the existing response envelope. Pipeline returns the same
object inside its own `result`. For example, a handled VS validation failure is:

```json
{
  "success": true,
  "result": {
    "success": false,
    "error": "Request validation message",
    "data": {
      "schema_version": "uapp-toolkit.visual-scripting-mcp/1",
      "error": {"code":"invalid_arguments","message":"Request validation message"}
    }
  }
}
```

This is an abbreviated example. The outer success means the handler returned
without throwing; the inner success is the VS operation result. Inspect both.
Unity CLI 1.0.0 prints the core response directly with `--format json`.
CLI beta.15 places it in `data.result`. Both MCP stdio versions return the core
response as JSON text in `content`. Parse it and inspect `success` and `data.error`.
Explicit nulls, pagination,
revision tokens, and errors remain part of the unchanged core contract.

A trace started through either adapter is the same trace visible to the other.
The one-active-trace rule applies across both transports. Play transitions/reload
invalidate IDs for both. Preview on one transport and apply on the other use the
same revision checks; conflicting edits cannot bypass them by changing bridges.

## Validation

With Pipeline and Unity CLI 1.0.0, all 14 commands passed live checks through
both direct CLI calls and MCP stdio. Each transport completed 26 successful
operations and 14 expected validation failures. The run exercised graph edits,
cloning, bindings, settings restoration, and tracing in Play Mode with domain
reload disabled.

Run the package's EditMode suite in four independent projects with neither bridge,
CoplayDev only, Pipeline only, and both. The tests check adapter activation,
actual bridge command discovery, main-thread registration, all-command error/input
forwarding, core dependencies, and Player exclusion, alongside graph/runtime tests.
Use existing empty-project manifests with local references to the tested dependency
versions for offline reproduction; do not remove bridges from a working game merely
to test the matrix. The repository report records exact runs and transport checks.
