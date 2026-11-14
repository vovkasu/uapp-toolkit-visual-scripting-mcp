# Changelog

All notable changes to this package are documented here.

## [1.1.0] - 2026-10-30

### Added

- Optional Unity CLI / Pipeline adapter exposing all 14 operations in the `vs` group.
- Conditional adapter assemblies selected by package version defines and constraints.
- Bridge-independent tests for assembly isolation, command discovery, and request forwarding.

### Changed

- Moved command registration and CoplayDev parameter schemas out of the shared core.
- Removed CoplayDev from mandatory package dependencies. Install either bridge or both separately.
- Core C# handlers and response schema remain unchanged. CoplayDev Parameters metadata
  classes now belong to `UAppToolKit.VisualScriptingMcp.CoplayDev.Editor`.
- Translated package documentation and related development reports into English.

### Validation

- All 91 tests passed with neither bridge, CoplayDev only, Pipeline only, and both.
- Main-project regression passed 91/91; final adapter checks passed 7/7.
- Verified live CoplayDev, Unity CLI, and Unity CLI MCP stdio requests.

## [1.0.0] - 2026-10-30

### Added

- Reusable UPM package with explicit Visual Scripting, CoplayDev Unity MCP, and
  Newtonsoft.Json dependencies.
- Separate Editor-only production and test assemblies.
- Fourteen MCP commands for graph discovery, reading, validation, runtime inspection,
  bounded tracing, targeted editing, cloning, object bindings, and project settings.
- Custom C# node template and installation/workflow documentation.
- Explicit `visual_scripting_not_initialized` error for settings requests before
  initial Visual Scripting setup, without implicitly generating nodes.

### Changed

- Moved the original project-local tool into this package, preserving asset GUIDs.
- Removed game-specific namespaces, keys, and examples from the package.
- Changed the C# namespace to `UAppToolKit.VisualScriptingMcp.Editor` and the response
  schema identifier to `uapp-toolkit.visual-scripting-mcp/1`. Clients that validate
  the previous schema string or use the old C# namespace must update those references.
  MCP command names and parameter structures are unchanged.

### Validation

- All 84 tests passed in the original project and in a separate empty project.
- All 14 MCP commands registered; a live diagnostics request succeeded.
- No package source files were included in Player compilation.
- Verified with Unity 6000.3.16f1, Visual Scripting 1.9.11, CoplayDev 10.1.0,
  and Newtonsoft.Json 3.2.2. Remote Git installation and other versions were not tested.
