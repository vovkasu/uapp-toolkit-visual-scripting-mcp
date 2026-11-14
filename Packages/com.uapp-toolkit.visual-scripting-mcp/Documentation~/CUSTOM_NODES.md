# Creating Visual Scripting C# nodes

Generate nodes by creating ordinary C# files; a separate MCP command is not needed.
The tool remains Editor-only. Place game Units outside every Editor folder, for
example in `Assets/Scripts/VisualScripting/`, using .NET Standard 2.1 and existing
runtime dependencies. Do not add packages/asmdefs just for a single node.

## Workflow

1. Inspect existing Unit classes and uses of the required behavior. Reuse suitable
   target-project nodes instead of duplicating them.
2. Create a `Unit` class with stable port keys and Requirement/Succession relations.
   Mark ports `[DoNotSerialize]`; keep serialized configuration separate from
   transient runtime state. Changing port keys on a node already in use requires
   checking existing graphs.
3. Request refresh/compilation through the Unity bridge and wait for idle Edit Mode
   after domain reload. Check Console. Do not regenerate nodes with C# errors present.
4. Read `vs_get_settings`. Custom Units in Assembly-CSharp usually require no
   additions: the assembly is already included, and VS discovers standalone Units
   automatically. `typeOptions` registers value types and reflected members, not
   every Unit subclass.
5. Only if needed, add an assembly/type through `vs_update_settings` preview/apply.
   Supply the exact `AssemblyQualifiedName` from a loaded runtime assembly.
   Editor types are rejected, including those inside generics/arrays.
6. After successful compilation, call `vs_update_settings` with `changes={}`,
   the current `expected_revision`, and `regenerate_nodes=true`: preview first,
   then `dry_run=false`. Poll `vs_get_settings.result.regeneration` until
   completed/failed/interrupted. This is a separate expensive operation, not
   triggered automatically by reading/changing the lists. Check error/warning
   counters, Console, and the required type's presence in node options.
7. Create the node in a separate test graph through C#/Editor APIs. Check its
   definition, ports, and serialization, then execute it in an isolated Play scene.
   `UnitBase.Subset` needs a valid GraphReference for the selected graph; otherwise
   contextual package options may throw.
8. Test ordinary and edge values. Remove temporary test assets, restore the original
   scene, and include the actual node's `.meta` file in the commit.

`vs_edit_graph` currently accepts only its own limited set of node types. Create
arbitrary Units through C#/Editor APIs. Reader and validator can inspect the
resulting graph without extending the allowlist.

## Template

[HasAssignedObjectUnit.cs.txt](../Samples~/CustomNode/HasAssignedObjectUnit.cs.txt)
is a text template, not a runtime component of the tool. Copy it to a suitable
runtime path with the `.cs` extension, check class name uniqueness, adapt it,
and compile. Its `isAssigned` output is false for a null or destroyed Unity object
on `target`, and true for a live object. It uses Unity null semantics without
object searches, stored state, or callback references to Flow.

Nodes with game effects must use the target project's architecture and services.
This template only checks a reference and introduces no game services or state.
