# sbox-agent-bridge

Tiny proof-of-concept bridge for letting MCP-capable coding agents inspect and carefully mutate the s&box editor.

The project is intentionally small: one editor-side bridge library and one external MCP server. The bridge knows how to talk to the live editor. The MCP server knows how to talk to agents.

## Status

Experimental POC. The first target is a reliable local loop:

1. Install the editor bridge into a s&box project `Libraries/` folder.
2. Open the Agent Bridge dock in the s&box editor.
3. Run the MCP server from `mcp-server/`.
4. Ask an agent for editor status, context, scene summary, hierarchy, or a very small test mutation.

### Verified Locally

Tested against a fresh minimal s&box project on 2026-04-28:

- The editor bridge library compiled and the **Agent Bridge** dock appeared in the editor.
- The dock started file IPC at `%TEMP%/sbox-agent-bridge`.
- Direct bridge requests for `bridge.status`, `scene.summary`, and `scene.find` returned live editor data.
- The first mutation, `gameobject.create`, created a GameObject in `SceneEditorSession.Active.Scene`.
- The created object was verified through a follow-up `scene.find` read-back.
- Core scene editing actions were verified through direct file IPC: `editor.context`, `editor.get_selection`, `editor.set_selection`, `scene.details`, `gameobject.get`, `gameobject.rename`, `gameobject.set_transform`, and `gameobject.set_enabled`.
- Extended scene editing actions were verified through direct file IPC: `editor.undo`, `editor.redo`, `editor.frame_object`, `gameobject.destroy`, `gameobject.duplicate`, and `gameobject.reparent`.
- Component discovery, inspection, and mutation actions were verified through direct file IPC: `component.list_types`, `component.list_on_gameobject`, `component.get`, `component.get_properties`, `component.add`, `component.remove`, `component.set_enabled`, and `component.set_property`.
- `component.set_property` is live-smoked against `AgentBridgeMutationFixture`, covering string, bool, integer, float/double, enum, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject` reference, and `Component` reference values.

This means the basic loop works: external process -> bridge request file -> s&box editor frame pump -> editor scene mutation -> verified response file.

## Project Docs

- [Roadmap](docs/roadmap.md)
- [Capability Matrix](docs/capability-matrix.md)
- [Testing Strategy](docs/testing.md)
- [Architecture](docs/architecture.md)
- [Protocol](docs/protocol.md)
- [Verified s&box APIs](docs/verified-sbox-apis.md)
- [Prior Art](docs/prior-art.md)
- [Contributing](CONTRIBUTING.md)

## Repository Layout

```text
editor/       s&box library/editor bridge source
mcp-server/   TypeScript MCP server that forwards tool calls to the bridge
schemas/      JSON schemas for bridge command/response envelopes
docs/         architecture, protocol, and verified s&box API notes
examples/     install and usage notes
```

## Design

The bridge uses file IPC for the first POC. It is less elegant than HTTP/SSE, but it is simple, debuggable, and avoids spending the first week arguing with transport details. The protocol is stable enough that a later HTTP/SSE transport can reuse the same command envelope.

Default IPC root:

```text
%TEMP%/sbox-agent-bridge/
  requests/
  responses/
```

## Initial Tool Surface

- `editor`: `status`, `context`, `get_selection`, `set_selection`
- `editor`: `save_scene`, `undo`, `redo`, `frame_object`
- `scene`: `summary`, `hierarchy`, `find`, `details`
- `gameobject`: `get`, `create`, `rename`, `set_transform`, `set_enabled`, `destroy`, `duplicate`, `reparent`
- `component`: `list_types`, `list_on_gameobject`, `get`, `get_properties`, `add`, `remove`, `set_enabled`, `set_property`

Every mutation should return a read-back `verified` payload. If a mutation cannot verify its own result, callers should treat it as incomplete.

`gameobject.duplicate` is currently a shallow, scene-attached duplicate: it copies name, enabled state, transform, and parent. Component and child cloning are tracked as future work under the component/prefab milestones.

`component.set_property` is intentionally typed and narrow. Current verified conversions include `string`, `bool`, numeric primitives, enums, `Vector2`, `Vector3`, `Rotation`, `Angles`, `Transform`, `Color`, `GameObject` references, and `Component` references. Resource references and collection editing are future work. The dedicated live-smoke fixture lives at `editor/Code/AgentBridgeMutationFixture.cs`.

## Capability Goal

The long-term goal is to let MCP-capable agents access as much of the s&box editor as a careful human collaborator can, while keeping operations observable, undoable, and grounded in live editor state.

That does not mean one unsafe "do anything" tool. The bridge should grow as a map of editor affordances:

- inspect active scene/session, selection, hierarchy, components, assets, logs, compile state, and play mode;
- mutate GameObjects, components, transforms, properties, prefabs, assets, and project settings through narrow actions;
- wrap scene mutations in editor undo scopes;
- read back state after every mutation;
- prefer object ids/GUIDs over guessed names;
- return actionable errors and suggestions;
- keep file/code edits in normal coding tools, and use the bridge for live editor state.

## Near-Term Roadmap

The living plan is tracked in [docs/roadmap.md](docs/roadmap.md), and implementation status is tracked in [docs/capability-matrix.md](docs/capability-matrix.md).

## Local Development

Install dependencies and build the MCP server:

```bash
cd mcp-server
npm install
npm run build
```

CI runs `npm ci`, `npm run check`, `npm test`, `npm run build`, and JSON metadata validation. Live s&box editor behavior still needs local smoke testing; see [docs/testing.md](docs/testing.md).

Run the opt-in live smoke check against an already-open editor bridge:

```bash
cd mcp-server
npm run smoke:live
```

Example MCP config after building:

```json
{
  "mcpServers": {
    "sbox-agent-bridge": {
      "command": "node",
      "args": ["C:/path/to/sbox-agent-bridge/mcp-server/dist/index.js"]
    }
  }
}
```

See [examples/minimal-sbox-project-install.md](examples/minimal-sbox-project-install.md) for editor bridge installation.

## Grounding

This repo is grounded in the official s&box docs/API schema:

- Editor projects can access editor tools and game code and are not sandboxed.
- `SceneEditorSession.Active` exposes the active editor scene/session.
- Scene edits should run on the editor main thread and use undo scopes.
- Normal game code is whitelisted, so this bridge belongs in editor/library code, not runtime gameplay components.

See [docs/verified-sbox-apis.md](docs/verified-sbox-apis.md).
