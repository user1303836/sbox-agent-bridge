# Status

This document tracks the current verified state of `sbox-agent-bridge`. The README stays focused on what the project enables and how to get started; this file keeps the more detailed engineering status.

## Current Verification Snapshot

- Date: 2026-04-30
- Environment: Windows, local s&box editor
- Test project: minimal s&box project
- Bridge install path: `Libraries/sbox_agent_bridge`
- Transport: file IPC at `%TEMP%/sbox-agent-bridge`

## Verified Locally

- The editor library compiles and the **Agent Bridge** dock appears in s&box.
- The dock listens through local file IPC.
- The MCP server can read editor status, active context, selection, scene summaries, hierarchy, GameObject details, component lists, and component properties.
- GameObject mutations are undo-scoped and read back after the edit: create, rename, transform, enable/disable, reparent, and duplicate.
- `gameobject.destroy` was previously verified, but the current editor session now reports a null reference in the native delete/undo path after play-mode testing. Treat it as blocked until reverified in a fresh session or replaced with a safer delete strategy.
- Component mutations are undo-scoped and read back after the edit: add, remove, enable/disable, and set property.
- Component property metadata includes explicit JSON-shape hints for agents.
- Component property values can be dry-run validated through `component.validate_property` or `component.set_property` with `dryRun: true`.
- `component.set_property` is live-smoked against `AgentBridgeMutationFixture` for string, bool, integer, float/double, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject` reference, and `Component` reference values.
- Editor feedback-loop actions are live-smoked for play state, play/stop, compile status, recent logs, and combined feedback.
- GitHub Actions runs metadata validation, TypeScript typecheck, tests, and MCP server build.

## Current Limitations

- The editor bridge must be installed into each s&box project that should expose live editor access.
- The s&box editor must be open and the Agent Bridge dock must be running.
- CI does not run a real s&box editor, so live editor behavior is verified with local smoke tests.
- `gameobject.duplicate` is currently shallow: it copies name, enabled state, transform, and parent, but not components or children.
- `component.set_property` does not yet support resource references or collection/list editing.
- `editor.compile_status` only tracks compile groups observed after the bridge library has loaded.
- `editor.logs` tails `sbox-dev.log`; raw lines are exact log output, while the level field is inferred from text.
- `AgentBridgeMutationFixture` is not visible through `Game.TypeLibrary` in every editor session. The live smoke script skips fixture-backed mutation unless `SBOX_AGENT_BRIDGE_REQUIRE_FIXTURE=1` is set.
- The full live smoke is currently blocked in this editor session by the `gameobject.destroy` delete/undo null reference. Direct feedback-loop actions were verified separately.

## Next Larger Milestones

- Batch scene operations for common create/configure/verify workflows.
- Editor feedback loop refinements: wait-for-compile, structured live log events, and runtime/game-session inspection.
- Asset and prefab discovery/instantiation workflows.
