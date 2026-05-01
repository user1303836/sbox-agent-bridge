# Fresh Agent Handoff

Last updated: 2026-05-01

This file is the continuity note for a fresh agent taking over work on `sbox-agent-bridge`. It summarizes the project intent, local setup, what has been built, what was learned, and the best next steps.

## Project Intent

`sbox-agent-bridge` is a provider-neutral MCP server plus s&box editor bridge. The goal is to let any MCP-capable agent inspect, control, and automate a live s&box editor session through narrow, observable, undoable actions.

The project should feel useful and capable, not apologetic. The user wants an agent-first interface to the s&box editor: point Claude, Codex, Kimi, or another agent at the live editor and ask it to help build scenes and game systems.

The ARPG game in the local test project is not the product. It is a testbed used to expose missing bridge functionality.

## Important Local Paths

- Bridge repo: `C:\Users\hidd3n\Desktop\development\sbox-agent-bridge`
- s&box research/docs repo: `C:\Users\hidd3n\Desktop\development\sandbox`
- Live test project: `C:\Users\hidd3n\Documents\s&box projects\testproject`
- Main test scene: `C:\Users\hidd3n\Documents\s&box projects\testproject\Assets\scenes\minimal.scene`
- Installed bridge library in test project: `C:\Users\hidd3n\Documents\s&box projects\testproject\Libraries\sbox_agent_bridge`
- Local bridge IPC root: `%TEMP%\sbox-agent-bridge`
- Camera captures: `%TEMP%\sbox-agent-bridge\captures`

## Grounding Rules For s&box Work

Before writing or editing s&box game code, use the grounding docs in the `sandbox` repo:

1. `C:\Users\hidd3n\Desktop\development\sandbox\README.md`
2. `C:\Users\hidd3n\Desktop\development\sandbox\docs\01-sbox-platform-primer.md`
3. `C:\Users\hidd3n\Desktop\development\sandbox\docs\02-development-primer-for-csharp-engineers.md`
4. `C:\Users\hidd3n\Desktop\development\sandbox\docs\04-agent-workflows-and-guardrails.md`

If API details matter, query:

```powershell
python scripts/sbox_api_lookup.py <TypeOrMember>
```

from `C:\Users\hidd3n\Desktop\development\sandbox`.

Do not guess Unity, Godot, or Garry's Mod APIs. s&box games are C#, with Scenes, GameObjects, Components, `[Property]`, and s&box-specific lifecycle methods.

## Architecture Summary

The repo intentionally keeps the editor bridge and MCP server together:

```text
Agent / MCP client
  <-> TypeScript MCP server over stdio
    <-> file IPC under %TEMP%/sbox-agent-bridge
      <-> s&box editor bridge runtime and Agent Bridge dock
        <-> live SceneEditorSession.Active / All / GameSession
```

The bridge runtime lives in `editor/`. It is installed into an s&box project as `Libraries/sbox_agent_bridge`. It auto-starts on editor frames once the editor assembly compiles and loads. The dock is visible at `View -> Agent Bridge` when the editor library is healthy.

The MCP server lives in `mcp-server/`. It forwards MCP tool calls to bridge actions through file IPC.

## Current Tool Surface

The bridge currently exposes MCP tools for:

- `editor`: status, context, tabs, open scene, selection, save, undo, redo, frame, play, stop, logs, compile status, combined feedback
- `scene`: summary, hierarchy, find, details, bounded batches
- `gameobject`: get, create, rename, transform, enable, destroy, duplicate, reparent
- `component`: list types, list on object, inspect, inspect properties, add, remove, enable, set property, validate property
- `script`: create, edit, delete
- `asset`: search, get info, inspect model/material, preview model, assign model/material, create material, set material source/runtime properties
- `visual`: capture active camera to PNG with luminance stats
- `sound`: list, get info, inspect scene components, create event, assign, preview
- `physics`: inspect, add collider, add rigidbody, add joint, raycast
- `prefab`: create, list, get info, instantiate, inspect instance metadata

`scene.batch` can compose existing actions with `$ref` alias substitution and per-operation results.

## Verified Recently

As of 2026-05-01, direct local checks verified:

- s&box bridge editor compile is green with zero errors in the live test project.
- `asset.inspect_model` works on `models/agent_bridge/arpg_props/cursed_obelisk.vmdl`.
- `asset.inspect_material`, `asset.set_material_source_property`, and `asset.preview_model` are live-verified through `mcp-server/test/asset-material-smoke.ts`. The preview path should target `runtime` after `editor.play`/`editor.wait_runtime`; stopped editor preview captures can render black on the current s&box build.
- `prefab.instantiate` now remaps prefab GUIDs to fresh instance GUIDs and preserves `__PrefabIdToInstanceId`. `prefab.inspect_instance` is live-verified through `mcp-server/test/prefab-instance-smoke.ts`, including source binding, id-map count, and name/position/rotation override patch samples.
- `physics.inspect` is live-verified through `mcp-server/test/physics-smoke.ts`, along with Rigidbody/collider/joint creation and a raycast against the temporary smoke collider.
- `sound.inspect` is live-verified through `mcp-server/test/sound-smoke.ts`, along with sound event creation/info, SoundPointComponent assignment read-back, and a valid playing preview handle.
- `visual.capture_camera` captures the active main camera to PNG and returns luminance stats.
- Spatial placement v1 works: `asset.set_orientation_override` and `asset.get_orientation_override` store/read `Assets/agent_bridge/orientation_overrides.json`, and `gameobject.place_asset` placed a cursed obelisk with a stored `pitch: 90` override, saved `scenes/minimal.scene`, force-reloaded it, and read back the persisted `ModelRenderer`.
- TypeScript check/build pass when invoked directly through the installed Node runtime.
- MCP bridge-client and wait-helper tests pass.

The most recent pushed bridge commit before this handoff pass was:

```text
3cfd460 Add feedback loop wait helpers
```

## Windows Node/NPM Note

This Windows shell may not have `npm` on PATH, and `npm run check` can hit an `Access is denied` shim issue. Direct Node execution works. The installed runtime path used successfully was:

```text
C:\Users\hidd3n\AppData\Local\Microsoft\WinGet\Packages\OpenJS.NodeJS.LTS_Microsoft.Winget.Source_8wekyb3d8bbwe\node-v24.15.0-win-x64\node.exe
```

Useful commands:

```powershell
cd C:\Users\hidd3n\Desktop\development\sbox-agent-bridge\mcp-server
& 'C:\Users\hidd3n\AppData\Local\Microsoft\WinGet\Packages\OpenJS.NodeJS.LTS_Microsoft.Winget.Source_8wekyb3d8bbwe\node-v24.15.0-win-x64\node.exe' .\node_modules\typescript\bin\tsc -p tsconfig.json --noEmit
& 'C:\Users\hidd3n\AppData\Local\Microsoft\WinGet\Packages\OpenJS.NodeJS.LTS_Microsoft.Winget.Source_8wekyb3d8bbwe\node-v24.15.0-win-x64\node.exe' .\node_modules\typescript\bin\tsc -p tsconfig.json
& 'C:\Users\hidd3n\AppData\Local\Microsoft\WinGet\Packages\OpenJS.NodeJS.LTS_Microsoft.Winget.Source_8wekyb3d8bbwe\node-v24.15.0-win-x64\node.exe' .\node_modules\tsx\dist\cli.mjs --test .\test\bridge-client.test.ts
```

## Direct IPC Helper

When the editor is open and the bridge is running, this PowerShell helper sends direct bridge actions without going through MCP:

```powershell
function Invoke-Bridge($Action, $Payload=@{}) {
  $ipc = Join-Path $env:TEMP 'sbox-agent-bridge'; $id = [guid]::NewGuid().ToString('N')
  $reqDir = Join-Path $ipc 'requests'; $respDir = Join-Path $ipc 'responses'
  New-Item -ItemType Directory -Force -Path $reqDir | Out-Null
  New-Item -ItemType Directory -Force -Path $respDir | Out-Null
  $body = @{ id=$id; action=$Action; payload=$Payload } | ConvertTo-Json -Depth 80
  $tmp = Join-Path $reqDir ".request-$id.tmp"; $path = Join-Path $reqDir "request-$id.json"
  [System.IO.File]::WriteAllText($tmp,$body,[System.Text.UTF8Encoding]::new($false))
  Move-Item -LiteralPath $tmp -Destination $path -Force
  $resp = Join-Path $respDir "response-$id.json"; $deadline=(Get-Date).AddSeconds(30)
  while((Get-Date)-lt $deadline -and -not(Test-Path $resp)){Start-Sleep -Milliseconds 100}
  if(-not(Test-Path $resp)){throw "No bridge response for $Action"}
  Get-Content -Raw $resp | ConvertFrom-Json
}
```

Example checks:

```powershell
Invoke-Bridge 'bridge.status'
Invoke-Bridge 'editor.context'
Invoke-Bridge 'asset.inspect_model' @{ modelPath='models/agent_bridge/arpg_props/cursed_obelisk.vmdl'; includeMaterials=$true }
Invoke-Bridge 'visual.capture_camera' @{ width=640; height=360; name='handoff-check' }
```

## Installing Edited Bridge Files Into The Live Project

Repo edits under `editor/` do not automatically update the open s&box project. Copy changed editor files into:

```text
C:\Users\hidd3n\Documents\s&box projects\testproject\Libraries\sbox_agent_bridge
```

For small edits, copy only the changed files. For larger edits, recopy `editor/` into the installed library. Let s&box hotload, then call `editor.compile_status`.

## ARPG Testbed State

The local `testproject` contains an isometric action-RPG prototype in `Code/ArpgDemo/ArpgDemoController.cs` and saved scene `Assets/scenes/minimal.scene`.

Gameplay features currently include:

- Isometric follow camera.
- Click-to-move warrior.
- Primary and alternate melee attacks with variance and crits.
- Shift-click hold-position attacking.
- Health and energy orbs, inventory, action bar, tooltips, minimap, buffs/debuffs.
- Four hotkeyed skills: Whirlwind, Executioner's Cut, Blood Rush, War Cry.
- Zombies with wandering/chasing/attacking, health, death, loot, respawn.
- Status effects, War Cry damage reduction/energy restore, Blood Rush stun.
- Procedural gore chunks and blood visuals.
- Manual 2D collision for player, zombies, and props.
- Generated dark-fantasy prop kit imported into `Assets/models/agent_bridge/arpg_props/`.
- Lighting/post-processing pass to make the scene brighter and more readable.
- 2026-05-01 ARPG feature slice: bottom-filled health/energy orbs, raised terrain helpers/geometry, Whirlwind on hotkey `1`, citizen animation graph setup, sword attachment to the citizen `hold_R`/`hand_R` attachment when available, elite zombies, item loot tables, 10x5 grid inventory, equipment slots, drag-to-equip, and item stat tooltips.

The ARPG is intentionally rough. Use it to test bridge capabilities, not as an end product.

## Important Lessons Learned

- Agent bridge commands need active tab awareness. The bridge now has `editor.tabs` and `editor.activate_tab`.
- Direct IPC callers need schema validation too. Partial vectors such as `{ "z": 0 }` are now rejected because they previously zeroed omitted axes.
- `gameobject.destroy` was once verified but became unsafe in the current editor session after play-mode testing due to a native editor delete/undo null reference.
- Play mode and editor scene inspection require explicit targeting. Use `targetSession: "runtime"` for live game reads; default editor reads can still hit stale editor sessions after transitions.
- Runtime world construction in `ArpgDemoController` now also happens lazily from `OnUpdate` once `Game.IsPlaying` is true. This fixed a case where `OnStart` ran in the editor, returned early, and never built the runtime ARPG world after entering play mode.
- Citizen weapon attachment is better handled through model attachments. The current test project enables attachments for the player renderer and parents the sword to `hold_R`, falling back to `hand_R` or the old body-relative pose.
- `editor.open_scene` with `forceReload: true` recovers sourced scenes after stale/empty session states.
- Local compiled game components can be added by exact C# type name, but `component.list_types` still does not reliably enumerate local project components.
- Bounds help with geometry but do not prove semantic orientation. A prop can have sensible bounds while upside down.
- `visual.capture_camera` gives the agent an actual rendered feedback channel and brightness stats, but visual composition and semantic orientation still need human or vision-model confirmation.
- `visual.capture_camera` captures the world camera but not the screen UI overlay. Runtime UI state can now be verified through component-authored test actions, but generic panel hierarchy/pixel capture is still future work.
- 2026-05-01 ARPG UI/input pass clarified the project goal: ARPG feature work is only a bridge test harness. When a game feature is hard to verify, record and prioritize the missing bridge capability instead of polishing the game around the limitation.
- `editor.logs` and `editor.feedback` now support `afterIndex` cursor reads. Use `verified.logs.nextAfterIndex` from a baseline read before making a change, then pass it back to see only new log lines.
- Runtime reads now support `targetSession: "runtime"` for live `GameSession` targeting. `editor.stop` also supports `stopAll: true` for smoke-test cleanup, and MCP-side `editor.wait_compile`, `editor.wait_runtime`, and `editor.wait_stopped` helpers avoid fixed sleeps while transitions settle. Duplicate/stale editor tabs can still remain after transitions, so future work should add post-stop scene restoration.
- Prefab instances created through the bridge should preserve prefab id maps. The bridge now rewrites prefab `__guid` and `IdValue` references before deserialization instead of stripping all GUIDs, which makes `prefab.inspect_instance` useful immediately after instantiation.
- Shell-driven OS keypresses were not reliable enough to verify runtime input. Use `runtime.run_test_action` for deterministic component-authored verification; focused viewport input injection remains future work.
- The ARPG controller now exposes Agent Bridge runtime test actions for logical UI/gameplay state. The runtime smoke verified inventory open, damage, restore, skill list, and zombie count. It also exposed that the current ScreenPanel child panels are not built (`hud.root=false`), which is a testbed/UI implementation issue now visible through bridge read-back.

## Known Blockers And Caveats

- `gameobject.destroy`: blocked pending fresh editor recheck or safer delete strategy.
- `script.delete`: implemented but not live-smoked; use a scratch-file smoke before marking verified.
- Runtime inspection: `targetSession: runtime`, `editor.stop stopAll`, MCP-side wait helpers, and runtime test actions are verified, but duplicate stale tab restoration and generic runtime queries still need work.
- Logs: `afterIndex` cursor reads are available for `editor.logs` and `editor.feedback`; structured log-event capture is still future work.
- Local component discovery: exact-name add works, discovery is partial.
- Particle properties: simple bool/number/color settings work, complex particle wrapper types are not supported yet.
- Joint target assignment: blocked because verified `Joint.Object2` is read-only.
- Spatial reasoning: v1 orientation overrides, `gameobject.place_asset`, and runtime-targeted `asset.preview_model` captures are implemented and live-verified; the full generated prop kit still needs seeded human-verified overrides and richer contact-sheet previews.
- Prefab override metadata is inspectable, but save/apply override and break-link mutations are still future work.

## Best Next Work

The next best bridge tasks are:

- seed human-verified orientation overrides for the generated ARPG prop kit and add contact-sheet previews for ambiguous rotations;
- post-transition stale-tab restoration;
- viewport/HUD capture or generic panel hierarchy inspection;
- focused viewport input injection;
- fresh-session destructive edit verification;
- local component discovery.

## Documentation Map

Start with:

- `README.md`: user-facing setup and capabilities.
- `docs/status.md`: current verified state and limitations.
- `docs/capability-matrix.md`: per-action status.
- `docs/spatial-reasoning.md`: why visual/spatial feedback matters and what to build next.
- `docs/editor-feedback-loop.md`: play/compile/log feedback design.
- `docs/poc-arpg-first-pass.md`: ARPG testbed history and lessons.
- `docs/testing.md`: CI, local checks, and live smoke process.

## User Preferences Captured From The Work

- Keep the project provider-neutral. Do not bias toward Claude, Codex, or any specific model.
- Keep the README inviting and outcome-oriented. Move engineering status and caveats into `docs/`.
- Prefer narrow, observable, undoable tools over a dangerous "do anything" bridge.
- Keep the capability matrix honest: mark tools partial/blocked when live testing proves it.
- Use the ARPG POC to discover bridge gaps naturally.
- Push useful work directly to `main` during this early phase.
