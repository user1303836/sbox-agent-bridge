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
- Troubleshooting notes cover editor-library compile/load failures, dock visibility, and IPC path.

## Milestone 1.5: External Tester MVP

Goal: make the bridge installable and diagnosable by testers who did not build the ARPG testbed.

Status: MVP candidate. Verified locally: fresh Minimal Game project creation script, install script, project launch script with isolated IPC, `bridge.doctor`, `editor.project_info`, `editor.new_scene`, `editor.save_scene_as`, `editor.recover_scene`, `npm run smoke:bootstrap`, `npm run smoke:mvp`, `npm run smoke:mvp-suite`, `npm run walkthrough:arpg`, `npm run walkthrough:boxing`, and `npm run audit:capabilities`.

Acceptance criteria:

- A tester can install the editor bridge with one PowerShell script.
- A tester can run a readiness check before mutating the scene.
- A tester can create a fresh Minimal Game project from the local s&box template.
- A tester can launch that project with an isolated bridge IPC root when another editor is already open.
- The MVP smoke covers the main safe workflow without relying on ARPG-specific runtime components.
- A bootstrap smoke can create, save, reload, and verify a new scene inside an opened or launched project.
- A suite-level smoke can run bootstrap plus focused asset/material, asset-resource/cloud, sound, physics, prefab, script/compile, reference, network, animation, particle, and matrix-gap checks against a suite-created scene.
- Clean-room gameplay walkthroughs can build and verify an ARPG and a second genre without relying on the original hand-built ARPG scene.
- Docs point testers to one quickstart instead of several overlapping engineering notes.

## Milestone 2: Core Scene Editing

Goal: give agents basic editor hands for GameObjects.

Status: in progress. Verified so far: tab/session listing and activation, selection read/set, object details, id-targeted read, rename, transform edits, enabled-state edits, frame object, save-state reporting, undo/redo, reparent, shallow duplicate, destroy with cleanup fallback, and batch scene v0.

Candidate actions:

- `editor.tabs` - verified
- `editor.activate_tab` - verified by source path while an unsaved untitled scene tab was also open
- `editor.project_info` - verified for active project metadata/path read-back
- `editor.new_scene` - verified for blank scene creation
- `editor.save_scene` - verified for save-state reporting, no-source guard, and actual disk-write verification against a sourced scene
- `editor.save_scene_as` - verified for noninteractive path-based scene writes, asset registration/compile, reload, and persisted object read-back
- `editor.open_scene` - verified for opening sourced scenes, resolving scenes through editor assets when needed, and force-reloading stale open sessions
- `editor.recover_scene` - verified for stop-all plus sourced scene reload/reactivation after play-mode transitions
- `editor.undo` - verified
- `editor.redo` - verified
- `editor.frame_object` - verified
- `editor.get_selection` - verified
- `editor.set_selection` - verified
- `gameobject.destroy` - partial; reverified through smokes, with cleanup scripts falling back to disable when native delete fails
- `gameobject.get` - verified
- `gameobject.rename` - verified
- `gameobject.set_transform` - verified
- `gameobject.set_enabled` - verified
- `gameobject.reparent` - verified
- `gameobject.duplicate` - verified as a shallow scene-attached duplicate
- `gameobject.place_asset` - verified; creates a renderer-backed model object using orientation overrides and ground alignment
- `scene.batch` - verified for bounded action lists with `$ref` aliasing

Acceptance criteria:

- All mutations use undo scopes where applicable.
- Agents can list open editor scene tabs/sessions and explicitly activate the intended one before reading or mutating scene state.
- Actions prefer ids/GUIDs over names.
- Every mutation has a read-back verification payload.
- Batches expose each operation result and stop on the first failure by default.
- Capability matrix records live verification status.

## Milestone 3: Components

Goal: let agents add and configure the behavior/rendering/physics building blocks that make scene editing useful.

Status: in progress. Verified so far: component type discovery for built-in/editor-visible components, component listing on a GameObject, id-targeted component reads, property metadata/value/schema inspection, dry-run property validation, component add/remove for visible component types, local game component add by exact compiled type name, enabled-state mutation, typed property mutation, resource-backed property mutation, citizen animation helper setup, and basic particle stack setup.

Candidate actions:

- `component.list_types` - partial; built-in/editor-visible components are listed, but local game components need a secondary discovery source
- `component.list_on_gameobject` - verified
- `component.get` - verified
- `component.get_properties` - verified
- `component.add` - verified for built-in/editor-visible types and local compiled game components by exact C# type name
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

- Local game component discovery. `component.add` can now add local compiled components by exact C# type name through a serialized-probe fallback, but `component.list_types` still cannot enumerate those local game types through editor-side `Game.TypeLibrary`.
- Collection/list property editing.
- Full component/child cloning for `gameobject.duplicate`.

## Milestone 4: Assets And Prefabs

Goal: connect scene operations to actual project content.

Status: in progress. Verified so far: asset search/info, model assignment, material creation/assignment/property mutation, sound list/info/create/assign/preview, collider/rigidbody/raycast helpers, prefab create/list/info/instantiate, basic particle stack component setup, and scene batches that compose these actions.

Candidate actions:

- `asset.search` - verified
- `asset.get_info` - verified
- `asset.list_types` - verified
- `asset.cloud_packages` - verified for installed/referenced package-cache reads
- `asset.create_resource` - verified for generic `.sound` GameResource creation/read-back
- `asset.get_orientation_override` - verified
- `asset.set_orientation_override` - verified
- `asset.assign_model` - verified
- `asset.create_material` - verified
- `asset.inspect_material` - verified
- `asset.set_material_source_property` - verified
- `asset.preview_model` - verified with runtime target
- `asset.assign_material` - verified
- `asset.set_material_property` - verified
- `sound.list` - verified
- `sound.get_info` - verified
- `sound.inspect` - verified
- `sound.create_event` - verified
- `sound.assign` - verified
- `sound.preview` - verified
- `physics.inspect` - verified
- `physics.add_collider` - verified
- `physics.add_physics` - verified
- `physics.add_joint` - partial; target assignment is blocked by read-only `Joint.Object2`
- `physics.raycast` - verified
- `prefab.create` - verified
- `prefab.list` - verified
- `prefab.get_info` - verified
- `prefab.instantiate` - verified; remaps prefab GUIDs and preserves instance id maps
- `prefab.inspect_instance` - verified; reads prefab path, patch counts/samples, and id maps
- `asset.browse`
- `asset.open`
- `asset.dependencies`
- `prefab.break_link`
- `prefab.save_overrides`

Acceptance criteria:

- Agents can discover valid asset paths before using them.
- Prefab instantiation returns created object ids and inspectable prefab instance metadata.
- Asset actions do not guess filesystem paths when editor asset APIs can resolve them.
- Generated project assets are re-discovered through `AssetSystem` before use.

## Milestone 4.5: Minimal Game POC

Goal: build the smallest real playable scene through the bridge and use the rough edges to drive the next capability work.

Status: superseded by the current smoke suites, runtime feedback checks, and clean-room gameplay walkthroughs documented in [status.md](status.md), [testing.md](testing.md), and [capability-matrix.md](capability-matrix.md).

Candidate acceptance criteria:

- The agent can inspect the scene, create/configure a minimal playable setup, save or explicitly report why save is unavailable, and read editor feedback.
- The POC records every bridge limitation discovered while building it.
- Missing capabilities are added to the roadmap before broad editor/tool-surface expansion.

## Milestone 5: Feedback Loop

Goal: support edit-compile-play-debug loops.

Status: in progress. Verified so far: authoritative play state, play start/stop with read-back, `editor.stop stopAll` cleanup, MCP-side wait helpers for compile/runtime/stopped transitions, target-session runtime reads, deterministic runtime test actions, recent log tailing with stable line-index cursors, compile status from observed compile events, and a combined `editor.feedback` action.

Candidate actions:

- `editor.play` - verified
- `editor.stop` - verified
- `editor.play_state` - verified
- `editor.logs` - verified, including `afterIndex` cursor reads
- `editor.compile_status` - verified
- `editor.feedback` - verified
- `bridge.doctor` - verified readiness check for bridge runtime, IPC, active session, compile health, stale play tabs, and source-scene state
- `editor.wait_compile` - verified MCP-side helper
- `editor.wait_runtime` - verified MCP-side helper
- `editor.wait_stopped` - verified MCP-side helper
- `runtime.list_test_actions` - verified
- `runtime.run_test_action` - verified

Acceptance criteria:

- Agents can detect hotload/compile failures without relying on screenshots.
- Play mode can be started/stopped safely.
- Runtime state inspection is separated from editor-scene inspection.
- Log reads can establish a baseline cursor before a change and report only new lines after that change.
- Wait helpers avoid fixed sleeps while compile/play/stop transitions settle.
- Component-authored runtime test actions can exercise gameplay/UI state without shell-level keypresses.

Remaining gaps:

- In-editor project switch automation. Fresh project files can be created by script and launched as a separate editor process with `scripts/start-sbox-project.ps1`, and scene bootstrap is covered by `editor.project_info`, `editor.new_scene`, `editor.save_scene_as`, and `smoke:bootstrap`. No bridge action switches the current editor process to a different project yet.
- Structured source edits. `script.create` and `script.edit` replace whole files; large gameplay scripts would be safer with patch/source-aware edit helpers.
- Structured live log-event capture, if a stable editor-library hook is verified.
- Generic runtime/game-session inspection beyond scene/component reads and component-authored self-report.
- Richer post-transition cleanup. `editor.recover_scene` covers the common saved-scene recovery path, but unsaved/prefab/custom editor session edge cases still need more coverage.
- Focused viewport input injection. Component-authored runtime test actions work, but shell-driven OS keypresses were not reliable enough to verify game input focus.
- Viewport/HUD capture. `visual.capture_camera` captures world cameras, but not the editor/game viewport UI overlay. Runtime UI self-report works for instrumented components; generic HUD verification needs viewport/window capture or panel hierarchy inspection.
- Camera target ambiguity. Multi-camera scenes should pass `cameraComponentId` or `gameObjectId`; default runtime capture can pick an existing scene camera instead of the generated gameplay camera.
- Reverify destructive scene edits after play/stop transitions. The current editor session exposed a null reference in the native GameObject delete/undo path.

## Milestone 6: Rich Editor Access

Goal: expand toward high-coverage editor workflows while preserving safety and observability.

Status: in progress. Verified so far: `asset.inspect_model` for model bounds/orientation candidates, `asset.preview_model` for runtime-targeted isolated model/material PNGs with luminance stats, `visual.capture_camera` for rendered camera PNGs, orientation override storage, and `gameobject.place_asset`.

Candidate areas:

- visual captures, annotated screenshots, and capture comparison;
- isolated model/material preview captures;
- viewport/HUD capture and runtime UI inspection;
- runtime test hooks and deterministic input/action injection;
- asset orientation metadata, bounds previews, orientation overrides, and high-level grounded placement helpers;
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
