# Tool Limitations

This file records bridge tools that are blocked, partial, or easy for an agent to misuse. Keep it current when live editor testing discovers a new limitation or retires an old one.

Last updated: 2026-04-30

## Blocked

| Tool / Action | Why It Is Blocked | Current Workaround | Next Investigation |
|---|---|---|---|
| `gameobject.destroy` | After play-mode testing, the current editor session hit a native editor delete/undo null reference. The tool was previously verified, but destructive edits need fresh-session reverification before agents should rely on it. | Avoid destructive scene cleanup in this session. Prefer disabling or moving objects until delete is reverified. | Re-test in a fresh editor process; if it still fails, implement a safer delete path or quarantine delete behind an explicit high-risk flag. |
| `script.delete` | The implementation exists, but live deletion was not exercised because deleting local project files should be an explicit, intentional act. | Leave scratch scripts in place or ask for deletion confirmation. | Add a dedicated scratch-file smoke path that creates, edits, deletes, and verifies one known temporary script. |
| Automated editor CI | GitHub Actions cannot currently launch and control a real s&box editor client. | Keep unit/typecheck CI for MCP code and run live editor smoke tests locally. | Investigate headless/editor automation support if s&box exposes a stable route. |

## Partial Or Caveated

| Tool / Action | What Works | Limitation | Next Investigation |
|---|---|---|---|
| `component.list_types` | Built-in/editor-visible component discovery works through `Game.TypeLibrary.GetTypes(typeof(Component))`. | Local game components such as `AgentBridgeArpgFixture` still do not appear in this list, even after instances exist in the scene. | Add a secondary local-script/component discovery source, likely from compiled scene instances and/or project C# source scanning, clearly labeled separately from TypeLibrary results. |
| `component.add` | Built-in components use TypeLibrary. Local compiled game components can now be added by exact C# type name through a serialized probe that resolves the runtime type, followed by normal `GameObject.AddComponent<T>()`. Live verified with `AgentBridgeArpgFixture` on four ARPG fixture objects. | The local fallback depends on the type being compiled and deserializable by s&box. It does not make local types discoverable through `component.list_types`. | Reverify in a fresh project and add smoke coverage for adding a fixture component to an existing object without duplicating existing components. |
| `component.set_property` | Primitives, enums, math types, colors, resource references, GameObject references, and Component references are verified. Local component property mutation was live verified on `AgentBridgeArpgFixture`. | Collection/list editing is not supported yet. | Add explicit list/array schema support only after verifying s&box property setter behavior for collections. |
| `gameobject.duplicate` | Creates a shallow scene-attached duplicate with name, enabled state, transform, and parent. | Does not clone components or children. | Either expose the limitation prominently or implement a component/child clone path with safe GUID regeneration. |
| `physics.add_joint` | Adds joint components. | Target assignment is not wired because the verified `Joint.Object2` API is read-only. | Search for the editor-facing joint target property or a serialized property route that does not corrupt the component. |
| `editor.logs` | Tails exact raw lines from `sbox-dev.log` and infers log levels. | No cursor/timestamp filter yet, so stale errors can appear beside current feedback. | Add a cursor/since-token response contract and teach `editor.feedback` to report only new log lines when requested. |
| `editor.feedback` | Combines play state, compile status, and recent logs. | Runtime/game-session inspection is not reliable yet; scene reads can still target the editor scene rather than live play-session state. | Separate editor-scene inspection from runtime-session inspection and expose which session every read targets. |
| `editor.play` / `editor.stop` | Direct play/stop actions return immediate read-back and have been smoke tested. | Play transitions can temporarily report inconsistent `GameSession` state. | Add wait/poll helpers for transition settle and runtime session availability. |
| `sound.get_info` | Project-created sound event inspection works. | Some built-in sound metadata fields throw before the handle/resource is loaded, so those fields are best-effort. | Add per-field error reporting instead of dropping metadata. |
| Local MCP build/test on this Windows shell | CI and previous local runs cover TypeScript build/test. | Current shell reports `Access is denied` for the available `node.exe` shim and has no `npm` on PATH. | Fix local Node/npm availability or rely on GitHub Actions for MCP server verification. |

## Important Retired Finding

Do not add local components by deserializing a modified full target `GameObject` JSON blob. Live testing showed that `GameObject.Deserialize` appended duplicate existing components on the target object. The safe fallback added on 2026-04-30 uses a temporary empty probe only to resolve the local runtime type, then adds the component to the real target through `GameObject.AddComponent<T>()`.

Direct IPC callers must provide complete `Vector3` payloads. A live ARPG prop placement pass showed that partial vectors such as `{ "z": 0 }` could silently zero omitted axes before validation; bridge parsing now rejects vector objects unless numeric `x`, `y`, and `z` fields are all present.
