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
- GameObject mutations are undo-scoped and read back after the edit: create, rename, transform, enable/disable, reparent, duplicate, and destroy.
- Component mutations are undo-scoped and read back after the edit: add, remove, enable/disable, and set property.
- Component property metadata includes explicit JSON-shape hints for agents.
- Component property values can be dry-run validated through `component.validate_property` or `component.set_property` with `dryRun: true`.
- `component.set_property` is live-smoked against `AgentBridgeMutationFixture` for string, bool, integer, float/double, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject` reference, and `Component` reference values.
- GitHub Actions runs metadata validation, TypeScript typecheck, tests, and MCP server build.

## Current Limitations

- The editor bridge must be installed into each s&box project that should expose live editor access.
- The s&box editor must be open and the Agent Bridge dock must be running.
- CI does not run a real s&box editor, so live editor behavior is verified with local smoke tests.
- `gameobject.duplicate` is currently shallow: it copies name, enabled state, transform, and parent, but not components or children.
- `component.set_property` does not yet support resource references or collection/list editing.

## Next Larger Milestones

- Batch scene operations for common create/configure/verify workflows.
- Editor feedback loop: play mode, compile/hotload status, logs, and errors.
- Asset and prefab discovery/instantiation workflows.
