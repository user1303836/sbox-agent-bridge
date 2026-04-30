# ARPG POC First Pass

Date: 2026-04-29

This note captures the first isometric action-RPG vertical slice built through the bridge against the local test project at:

`C:\Users\hidd3n\Documents\s&box projects\testproject`

## Implemented In Test Project

- Added `Code/ArpgDemo/ArpgDemoController.cs`.
- Added an `Inventory` input action bound to `i` in `ProjectSettings/Input.config`.
- Added a saved `ARPG Demo Controller` GameObject with an `ArpgDemoController` component to `Assets/scenes/minimal.scene`.

The controller builds the playable slice at runtime:

- Isometric follow camera.
- Click-to-move warrior with sword visual.
- Left-click primary attack and right-click alternate attack.
- Shift-click stand-ground attacks.
- Health, energy, coin count, and inventory HUD.
- Melee range checks, damage variance, critical hits, attack intervals, and alternate energy cost.
- Wandering/chasing zombie NPCs with slower melee attacks.
- Zombie death, delayed respawn, 50% coin drops, and coin pickup.
- Procedural gore chunks, blood pools, and temporary visual cleanup.
- Darker arena props using built-in/cached models.

## Verification

- `editor.compile_status` reports `local.testproject` with `buildSuccess: true` and `errorCount: 0`.
- `component.list_types` finds `ArpgDemoController`.
- `scene.batch` created the controller object, added the component, and ran `editor.save_scene`.
- `editor.save_scene` reported `saveVerified: true` for `scenes/minimal.scene`.
- `editor.open_scene` with `forceReload: true` reloads `scenes/minimal.scene` after play/stop session staleness and verifies:
  - `rootCount: 10`
  - `componentCount: 21`
  - `ArpgDemoController: 1`

## Bridge Lessons

- `editor.open_scene` needed a `forceReload` mode. After play/stop, the active editor session can remain present but expose an empty scene to bridge reads until the sourced scene is reopened from disk.
- The current play feedback loop is not enough for runtime inspection. `editor.play` can report play mode started, but follow-up scene reads may target a stale editor session instead of the live game session. This is a blocker for agent confidence during game POCs.
- `editor.logs` needs cursor/timestamp support. Right now it tails the file and can surface stale compile/runtime errors as if they are current unless the caller uses very specific filters.
- `ModelRenderer` should be created disabled until a valid model is assigned, then enabled. Creating it enabled with no model can assert in s&box.

## POC Gaps

- The visual asset pass uses built-in/cached citizen/dev models and procedural props. A real distributable sample should include an explicit free asset pipeline or a documented s&box asset dependency.
- Sound hooks exist as component properties, but defaults are empty because the local project does not yet include reliable ambient/zombie/weapon sound events. A proper sound asset import/use path is still needed.
- The bridge cannot yet inspect runtime-only objects created during play. For the next POC iteration, add a runtime feedback channel or play-session scene targeting so an agent can verify the warrior, zombies, HUD, loot, and gore state directly.
- Combat has simple state/visuals rather than animation graph integration. Animation graph/property support should come after runtime feedback is trustworthy.

## Recommended Next Bridge Work

1. Add play-session-aware scene reads, or a separate runtime snapshot command that can inspect the live game session.
2. Add timestamp/cursor-based log reads so feedback ignores stale errors.
3. Add a small runtime self-report component/tool path for gameplay state: player health, energy, position, zombie count, loot count, and last event.
4. Add resource/asset import and selection helpers for models, decals, particles, and sounds.
