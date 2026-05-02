# Status

This document tracks the current verified state of `sbox-agent-bridge`. The README stays focused on what the project enables and how to get started; this file keeps the more detailed engineering status.

## Current Verification Snapshot

- Date: 2026-05-02
- Environment: Windows, local s&box editor
- Test project: Minimal Game-derived local project with ARPG and boxing bridge testbeds
- Bridge install path: `Libraries/sbox_agent_bridge`
- Transport: file IPC at `%TEMP%/sbox-agent-bridge-fresh-mvp` for latest full-suite verification

## Verified Locally

- The editor library compiles and the **Agent Bridge** dock appears in s&box.
- The bridge runtime starts automatically once the editor library loads and listens through local file IPC; the dock displays status and manual start/stop controls.
- The editor bridge and MCP server both honor `SBOX_AGENT_BRIDGE_IPC`, which allows isolated IPC roots for fresh-project or multi-editor walkthroughs.
- `bridge.doctor` reports editor bridge/MCP versions, IPC writability, active project/session health, compile health, recent bridge logs, stale play-tab warnings, and an actionable next step. It is the first check for external testers.
- The MCP server can read editor status, active context, selection, scene summaries, hierarchy, GameObject details, component lists, and component properties.
- GameObject mutations are undo-scoped and read back after the edit: create, rename, transform, enable/disable, reparent, and duplicate.
- `gameobject.create` can optionally parent a new object by `parentId`; this was verified through `scene.batch`.
- `gameobject.destroy` is reverified through the focused category smokes and MVP smoke. It still uses the native editor delete/undo path, so cleanup scripts fall back to disabling objects if destroy fails in a stale editor session.
- Component mutations are undo-scoped and read back after the edit: add, remove, enable/disable, and set property.
- `component.add` can add local compiled game components by exact C# type name through a serialized-probe fallback that resolves the runtime type, then calls s&box `GameObject.AddComponent<T>()` on the target. Live verified with `AgentBridgeArpgFixture` on ARPG fixture objects without duplicating existing components.
- Component property metadata includes explicit JSON-shape hints, attributes, enum values, and reference targets for agents.
- Component property values can be dry-run validated through `component.validate_property` or `component.set_property` with `dryRun: true`.
- `component.set_property` is live-smoked against `AgentBridgeMutationFixture` for string, bool, integer, float/double, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject` reference, and `Component` reference values. Local component property mutation was also live verified against `AgentBridgeArpgFixture`.
- Resource-backed component properties can be validated and set from asset paths; live IPC verified `ModelRenderer.Model` with `models/dev/plane_blend.vmdl` and `ModelRenderer.MaterialOverride` with `materials/dev/reflectivity_30.vmat`.
- `editor.save_scene` reports before/after save state, source path, skipped reason, and whether a save was verified. Direct IPC verified dry-run, safe no-source skip behavior, and an actual disk write against the sourced `scenes/minimal.scene` test scene.
- `editor.project_info`, `editor.new_scene`, and `editor.save_scene_as` are live-verified through `smoke:bootstrap`. The smoke reads active project paths, creates a blank editor scene, creates a marker object, saves the scene to `scenes/agent_bridge/smoke/bootstrap_smoke.scene`, reloads it, verifies persisted marker read-back, and restores the original sourced scene.
- `scripts/create-minimal-sbox-project.ps1` instantiates a fresh Minimal Game project from the local s&box `game.minimal` template. It was run locally to create `agent_bridge_mvp_fresh`, then `scripts/install-editor-bridge.ps1` installed the bridge into that fresh project directory.
- `scripts/start-sbox-project.ps1` launches a selected `.sbproj` through `sbox-dev.exe sbox-launcher.dll sbox-dev.dll -project ...` and can set an isolated `SBOX_AGENT_BRIDGE_IPC` root for the launched editor. This was live-verified against `local.agent_bridge_mvp_fresh`.
- `editor.open_scene` opens sourced scene resources and supports `forceReload` for recovering an already-open scene after play/stop session staleness.
- `scene.batch` runs a bounded list of existing bridge actions with `$ref` aliases. Direct IPC verified a parent/child create, component add, model/material property writes, save-state check, and details read-back.
- `scene.batch` now composes broader authoring actions. The ARPG fixture pass verified object create/reparent/duplicate/rename/enable, selection/focus, model/material assignment, sound assignment, collider/rigidbody/joint creation, material-property mutation, and raycast read-back in batches.
- Asset/material/resource helpers are live-verified: `asset.search`, `asset.get_info`, `asset.list_types`, `asset.cloud_packages`, `asset.create_resource`, `asset.assign_model`, `asset.create_material`, `asset.inspect_material`, `asset.set_material_source_property`, `asset.assign_material`, `asset.set_material_property`, and `asset.preview_model`.
- `asset.inspect_model` is live-verified against the generated ARPG prop kit. It returns model/render/physics bounds, material slots, common orientation candidates, candidate ground offsets, footprints, and explicit caveats about geometry versus semantic uprightness.
- Spatial placement v1 is live-verified: `asset.get_orientation_override`, `asset.set_orientation_override`, and `gameobject.place_asset` store project-local orientation metadata and use it to place model assets with ground alignment. Live IPC verified a cursed obelisk override with `pitch: 90`, placed a grounded obelisk, saved `scenes/minimal.scene`, force-reloaded it, and read back the persisted `ModelRenderer`.
- `visual.capture_camera` is live-verified against the active ARPG camera. `asset.preview_model` is live-verified against the runtime GameSession for isolated model/material previews. Both write PNGs under `%TEMP%/sbox-agent-bridge/captures` and return luminance stats for visibility/readability checks.
- Sound helpers are live-verified: `sound.list`, `sound.get_info`, `sound.inspect`, `sound.create_event`, `sound.assign`, and `sound.preview`.
- Physics helpers are live-verified for `physics.inspect`, colliders, rigidbodies, joints, and raycasts. Joint component creation and collision read-back work, but target assignment remains limited.
- Prefab helpers are live-verified: `prefab.create`, `prefab.list`, `prefab.get_info`, `prefab.instantiate`, and `prefab.inspect_instance`. Instantiation now remaps prefab GUIDs to fresh instance GUIDs so `__PrefabIdToInstanceId` and transform override patch metadata are available immediately after bridge-created instances.
- Editor feedback-loop actions are live-smoked for play state, play/stop, compile status, recent logs, combined feedback, and MCP-side wait helpers. `editor.logs` and `editor.feedback` support `afterIndex` cursor reads so agents can establish a baseline and then inspect only new log lines. Read actions now support `targetSession: runtime` for live `GameSession` inspection, and `editor.stop` supports `stopAll: true` for clearing stale play sessions before smoke tests.
- Runtime feedback is live-verified through `runtime.list_test_actions` and `runtime.run_test_action`. The verified property protocol lets component-authored test hooks execute deterministic actions and return JSON read-back from the live GameSession. `mcp-server/test/runtime-feedback-smoke.ts` verifies wait helpers, ARPG state, inventory, damage, and restore flow.
- The clean-room boxing walkthrough is live-verified through `npm run walkthrough:boxing`. It creates `Code/BoxingDemo/BoxingDemoController.cs` through `script.create`, adds the local component by exact type name, saves the scene, enters play mode, verifies jab/block/dodge/knockdown/TKO/decision runtime actions, and captures the generated broadcast camera by GameObject id.
- `smoke:capability-gaps` is live-verified. It creates/edits/deletes a scratch script with new compile sequence waits, verifies controlled compile-error diagnostics and recovery, verifies `SkinnedModelRenderer` plus `CitizenAnimationHelper` setup against `models/citizen/citizen.vmdl`, and verifies a basic particle stack with property read-back.
- `smoke:mvp-suite` is live-verified. It creates its own saved scene and runs bootstrap, MVP, asset/material, asset-resource/cloud, physics, sound, network, prefab, matrix-core, project-file/input, script-introspection, reference, and capability-gap smokes against that scene, so the external tester gate no longer depends on `scenes/minimal.scene`.
- Reference helpers are live-verified through `smoke:reference`: `reference.search` scans installed XML docs, `reference.type` reflects loaded C# types while preferring s&box assemblies over system name collisions, `reference.console` reads editor console variables, and `reference.whitelist` inspects whitelist metadata when available.
- Source-domain marker detection is live-verified through `smoke:scripts`: `script.analyze` identifies physics events, network snapshot/visibility interfaces, rendering/UI/asset/world/animation/service/media/editor/input markers, plus the existing lifecycle and attribute analysis.
- Network metadata helpers are live-verified through `smoke:network`: `network.connections` reads the local/host connection and permission fields, `network.inspect_object` reads GameObject network/accessor metadata, and `network.set_object_mode` mutates `NetworkMode`, orphan behavior, owner transfer, and always-transmit with read-back. `[Sync]`, `[Rpc.*]`, and `IGameObjectNetworkEvents` are covered at source-analysis level; dedicated servers and multi-client replication are still outside the current bridge surface.
- Runtime property-protocol failures now unwrap target invocation exceptions with component/action context. The boxing walkthrough also confirmed that property-protocol components should ignore empty `AgentBridgeTestAction` assignments, because s&box scene deserialization can replay serialized empty property values.
- Rendering-oriented component mutation is live-verified through the ARPG visual pass: AmbientLight, PointLight, SpotLight, DirectionalLight, DecalRenderer, PostProcessVolume, FilmGrain, Tonemapping, Bloom, Vignette, ColorAdjustments, and basic ParticleEffect/ParticleEmitter/ParticleRenderer settings.
- GitHub Actions runs metadata validation, TypeScript typecheck, tests, and MCP server build.

## Current Limitations

- The editor bridge must be installed into each s&box project that should expose live editor access.
- The s&box editor must be open and the bridge editor library must compile/load. The dock is useful for status, but the IPC pump now starts automatically after the assembly loads.
- If the dock is missing after restart, check `sbox-dev.log` for bridge compile failures. Duplicate fixture scripts are a known cause: keep only `Libraries/sbox_agent_bridge/Code/TestFixtures/AgentBridgeMutationFixture.cs`.
- CI does not run a real s&box editor, so live editor behavior is verified with local smoke tests.
- `editor.save_scene` disk writes are verified for sourced scenes. Untitled scenes are guarded to avoid surprise save-as UI; noninteractive path-based writes should use `editor.save_scene_as`.
- `gameobject.duplicate` is currently shallow: it copies name, enabled state, transform, and parent, but not components or children.
- `component.list_types` still does not enumerate local game component types through editor-side `Game.TypeLibrary.GetTypes(typeof(Component))`. Existing local component instances can be inspected, and `component.add` can add a local component by exact compiled type name, but type discovery needs a separate local-component source.
- `component.set_property` does not yet support collection/list editing. Resource reference support is implemented for `Sandbox.Resource` subclasses, with live coverage so far on model, material, and sound-event properties.
- Particle authoring is partial beyond the basic stack. `ParticleEffect`, `ParticleConeEmitter`, `ParticleSpriteRenderer`, and `ParticleLightRenderer` add/configure/read-back flows are verified, but complex s&box particle wrapper types such as `ParticleFloat`, `ParticleVector3`, and `ParticleGradient` are not settable yet.
- `physics.add_joint` creates joint components, but target assignment is not wired because the verified `Joint.Object2` property is read-only.
- `editor.compile_status` only tracks compile groups observed after the bridge library has loaded.
- `editor.logs` tails `sbox-dev.log`; raw lines are exact log output, while the level field is inferred from text. Cursor indexes are file line indexes, not structured log-event ids, and large ranges can still be tail-truncated.
- Runtime/game-session reads are now targetable and `editor.wait_runtime` removes the need for fixed sleeps. `editor.recover_scene` stops stale play sessions and reopens/reactivates a sourced scene; `editor.open_scene` still supports `discardUnsaved:true` with `forceReload:true` for scratch/test-scene recovery.
- `visual.capture_camera` captures camera output, not the editor/game viewport overlay. Runtime UI self-report is available through test actions, but generic viewport/HUD pixel capture and panel hierarchy inspection remain open. In multi-camera scenes, callers should pass `cameraComponentId` or `gameObjectId`; the boxing walkthrough found that default camera selection can choose an existing scene camera instead of a generated gameplay camera.
- Shell-driven OS keypresses are not reliable enough to verify game input. Deterministic runtime test actions are now available; focused viewport input injection remains future work.
- There is no in-editor bridge action yet for switching the current project. Fresh-project tests can instantiate/install a project from scripts and launch a separate editor process with `scripts/start-sbox-project.ps1`; if testers use an already-running editor, they still need to open the target `.sbproj` themselves.
- Script authoring is currently full-file create/edit. The boxing walkthrough was workable with a fixture file, but large gameplay scripts would benefit from structured patch support or source-aware edit helpers.
- Spatial placement now has a live-verified v1 override-backed placement path, but the override data still needs to be seeded for the full generated prop kit. Renderer bounds still cannot confirm that an asset is semantically upright without human or vision confirmation.
- Local game component types, including `AgentBridgeMutationFixture`, are not visible through editor-side `Game.TypeLibrary` enumeration in every editor session. The live smoke script skips fixture-backed mutation unless `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1` is set, but exact-name `component.add` can still add compiled local components in verified cases.
- `npm run smoke:mvp-suite` is the preferred broad external-tester gate. It passed against both the existing test project and the generated fresh Minimal Game project `local.agent_bridge_mvp_fresh`, including the reference and network metadata steps. `smoke:mvp` remains useful as a narrower pass against an existing saved scene. The older `smoke:live` remains useful for broad regression checks but has more historical fixture assumptions.
- If a Windows shell hits npm shim issues, direct execution through the installed Node runtime works; TypeScript check/build and the bridge-client tests can be run with direct `node` commands.
- Do not add local components by deserializing a modified full target `GameObject` JSON blob. Live testing showed that this duplicates existing components; the safe fallback uses a temporary empty probe only to resolve the local runtime type, then adds the component with `GameObject.AddComponent<T>()`.

## Next Larger Milestones

- Run the tester quickstart with an external human from a clean clone and a fresh minimal s&box project.
- Add first-class open/switch project automation if a reliable s&box editor API or CLI entry point is verified.
- Seed persistent asset orientation overrides for the generated ARPG prop kit and add isolated preview captures for ambiguous assets.
- Editor feedback loop refinements: structured live log events, viewport/HUD capture, and focused viewport input injection.
- Improve local game component type discovery so agents can list project components before adding them by exact name.
- Continue asset, prefab, sound, physics, and runtime-feedback workflows through the ARPG POC.
