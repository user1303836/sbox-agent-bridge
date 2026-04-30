# Roadmap

This roadmap is a living plan. Update it whenever a capability is added, verified, rejected, or split into smaller work.

## Mission

Let MCP-capable agents interact with the s&box editor like careful human collaborators: able to inspect the current project, make undoable changes, verify the result, and iterate from real editor feedback.

## Guiding Principles

- **Live state first**: agents should inspect the editor before mutating it.
- **Small actions**: prefer narrow actions with explicit arguments over generic code execution.
- **Undoable mutations**: scene edits should use editor undo scopes.
- **Verified results**: every mutation should return a `verified` read-back payload.
- **Grounded APIs**: use official docs, API schema, and live editor testing before relying on an s&box API.
- **Human-visible behavior**: the dock/logs should make bridge activity understandable.
- **Transport separation**: keep MCP protocol concerns outside the editor bridge where possible.

## Milestone 0: Proof Of Life

Status: complete.

Acceptance criteria:

- s&box library compiles in a fresh minimal project.
- Agent Bridge dock appears in the editor.
- File IPC starts.
- `bridge.status` returns active editor state.
- `scene.summary` reads live scene data.
- `gameobject.create` creates an object in the active editor scene.
- Follow-up `scene.find` verifies the created object.

## Milestone 1: MCP End-To-End

Goal: verify the actual MCP server path, not only direct file IPC.

Acceptance criteria:

- MCP client can call `editor.status`.
- MCP client can call `scene.summary`.
- MCP client can create one GameObject and verify it with `scene.find`.
- README includes a minimal MCP configuration example known to work.
- Troubleshooting notes cover the dock-open requirement and IPC path.

## Milestone 2: Core Scene Editing

Goal: give agents basic editor hands for GameObjects.

Status: in progress. Verified so far: selection read/set, object details, id-targeted read, rename, transform edits, enabled-state edits, frame object, save-state reporting, undo/redo, reparent, shallow duplicate, and batch scene v0. `gameobject.destroy` was previously verified but is currently blocked in this editor session.

Candidate actions:

- `editor.save_scene` - verified for save-state reporting and no-source guard; actual disk-write verification needs a sourced scene
- `editor.open_scene` - verified for opening sourced scenes and force-reloading stale open sessions
- `editor.undo` - verified
- `editor.redo` - verified
- `editor.frame_object` - verified
- `editor.get_selection` - verified
- `editor.set_selection` - verified
- `gameobject.destroy` - blocked pending fresh-session recheck or safer delete strategy
- `gameobject.get` - verified
- `gameobject.rename` - verified
- `gameobject.set_transform` - verified
- `gameobject.set_enabled` - verified
- `gameobject.reparent` - verified
- `gameobject.duplicate` - verified as a shallow scene-attached duplicate
- `scene.batch` - verified for bounded action lists with `$ref` aliasing

Acceptance criteria:

- All mutations use undo scopes where applicable.
- Actions prefer ids/GUIDs over names.
- Every mutation has a read-back verification payload.
- Batches expose each operation result and stop on the first failure by default.
- Capability matrix records live verification status.

## Milestone 3: Components

Goal: let agents add and configure the behavior/rendering/physics building blocks that make scene editing useful.

Status: in progress. Verified so far: component type discovery, component listing on a GameObject, id-targeted component reads, property metadata/value/schema inspection, dry-run property validation, component add/remove, enabled-state mutation, typed property mutation, and resource-backed property mutation.

Candidate actions:

- `component.list_types` - verified
- `component.list_on_gameobject` - verified
- `component.get` - verified
- `component.get_properties` - verified
- `component.add` - verified
- `component.remove` - verified
- `component.set_property` - verified through `AgentBridgeMutationFixture` for common scalar/math/reference shapes and live-verified on built-in `ModelRenderer` resource properties
- `component.validate_property` - verified for valid conversion, invalid rejection, and no-mutation read-back
- `component.set_enabled` - verified

Acceptance criteria:

- Type lookup uses s&box type metadata rather than hardcoded assumptions.
- Property read/write supports primitives, enums, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, colors, object/component references, and `Sandbox.Resource` references by asset path.
- Property metadata exposes settable JSON shapes before mutation.
- Dry-run validation can convert and resolve a candidate property value without mutating the scene.
- Set-property failures return actionable type-conversion errors.
- Read-back verifies the property value after a set.

Remaining gaps:

- Collection/list property editing.
- Full component/child cloning for `gameobject.duplicate`.

## Milestone 4: Assets And Prefabs

Goal: connect scene operations to actual project content.

Candidate actions:

- `asset.search`
- `asset.browse`
- `asset.open`
- `asset.dependencies`
- `prefab.instantiate`
- `prefab.inspect`
- `prefab.break_link`
- `prefab.save_overrides`

Acceptance criteria:

- Agents can discover valid asset paths before using them.
- Prefab instantiation returns created object ids.
- Asset actions do not guess filesystem paths when editor asset APIs can resolve them.

## Milestone 4.5: Minimal Game POC

Goal: build the smallest real playable scene through the bridge and use the rough edges to drive the next capability work.

Status: first ARPG vertical slice created in the local test project. See [poc-arpg-first-pass.md](poc-arpg-first-pass.md).

Candidate acceptance criteria:

- The agent can inspect the scene, create/configure a minimal playable setup, save or explicitly report why save is unavailable, and read editor feedback.
- The POC records every bridge limitation discovered while building it.
- Missing capabilities are added to the roadmap before broad editor/tool-surface expansion.

## Milestone 5: Feedback Loop

Goal: support edit-compile-play-debug loops.

Status: in progress. Verified so far: authoritative play state, play start/stop with read-back, recent log tailing, compile status from observed compile events, and a combined `editor.feedback` action.

Candidate actions:

- `editor.play` - verified
- `editor.stop` - verified
- `editor.play_state` - verified
- `editor.logs` - verified
- `editor.compile_status` - verified
- `editor.feedback` - verified
- `compile.wait`

Acceptance criteria:

- Agents can detect hotload/compile failures without relying on screenshots.
- Play mode can be started/stopped safely.
- Runtime state inspection is separated from editor-scene inspection.

Remaining gaps:

- Wait/poll helper for compile completion.
- Structured live log-event capture, if a stable editor-library hook is verified.
- Dedicated runtime/game-session inspection while playing.
- Timestamp/cursor-based logs so stale errors do not look current.
- Reverify destructive scene edits after play/stop transitions. The current editor session exposed a null reference in the native GameObject delete/undo path.

## Milestone 6: Rich Editor Access

Goal: expand toward high-coverage editor workflows while preserving safety and observability.

Candidate areas:

- viewport screenshots/camera capture;
- model/material/light/audio helpers;
- project settings and input bindings;
- collision rules;
- terrain/mesh tooling;
- navmesh helpers;
- cloud/package queries;
- ActionGraph inspection if a reliable textual API exists.

Acceptance criteria:

- Each area gets its own capability matrix section.
- High-risk operations are opt-in and clearly documented.
- Tool responses stay compact enough for agents to use repeatedly.

## Non-Goals For Now

- Running arbitrary untrusted code through the bridge.
- Replacing normal source-code editing tools.
- Automating publishing/export flows before basic edit/test loops are stable.
- Pretending CI can validate live editor behavior without s&box installed.
