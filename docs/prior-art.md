# Prior Art

This project should stay provider-neutral, but nearby projects are useful research.

## Sbox-Claude

Source: <https://github.com/LouSputthole/Sbox-Claude>

Reviewed on 2026-04-28. The project is aimed at Claude Code specifically and is licensed GPL-3.0, while this repository is MIT. We should treat it as prior art and field notes, not as a source to copy implementation code from.

Useful neutral takeaways:

- File IPC is a practical first transport for s&box editor integration.
- The bridge belongs in a project-local `Libraries/` editor library during development.
- Scene/editor APIs need main-thread editor processing.
- Frame-driven processing must be owned by editor code that has compiled and loaded. This repo now auto-starts the bridge from an editor-frame pump; the dock remains useful as a status/control surface.
- IPC writes should avoid UTF-8 BOMs, and clients should defensively strip BOMs.
- Broad capability matrices are useful, but each action should still be verified against the local s&box API schema and a live editor.
- Type-discovery/reflection tools are a good future direction because they help agents look up real component/property/method shapes instead of guessing.
- Tests should separate CI-safe protocol/MCP coverage from local live-editor smoke checks.

Provider-neutral ideas to consider:

- `editor.frame_object` / focus workflows.
- Component/property discovery before property mutation.
- Asset and prefab search before instantiation.
- Play-mode state tracking with explicit editor/runtime separation.
- A scripted local smoke harness that runs against an already-open editor.

Things not to copy directly:

- Claude-specific setup, naming, prompts, or assumptions.
- GPL-licensed implementation code.
- Large all-in-one bridge files; this repo should keep smaller modules and narrow actions.
