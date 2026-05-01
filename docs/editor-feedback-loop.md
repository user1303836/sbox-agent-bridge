# Editor Feedback Loop

The bridge feedback loop is designed around one rule: every signal should say where it came from. Agents should be able to tell the difference between authoritative editor state, observed compiler state, and raw log output.

## Current Feedback Actions

- `editor.play_state`: authoritative read from the resolved `SceneEditorSession`.
- `editor.play`: calls `SceneEditorSession.SetPlaying(...)` and returns a play-state read-back.
- `editor.stop`: calls `SceneEditorSession.StopPlaying()` and returns a play-state read-back.
- `editor.compile_status`: reports compile groups observed by the bridge through s&box `compile.started` events.
- `editor.logs`: tails the editor `sbox-dev.log` file and supports `afterIndex` cursor reads.
- `editor.feedback`: returns play state, compile status, and recent logs in one response.
- `runtime.list_test_actions`: lists component-authored deterministic runtime test hooks in the live game session.
- `runtime.run_test_action`: invokes a selected runtime test hook and returns parsed state/action results.

## Signal Accuracy

`editor.play_state`, `editor.play`, and `editor.stop` are authoritative for the resolved editor session. The bridge reads `IsPlaying`, `GameSession`, active scene name, and unsaved-change state directly from the selected `SceneEditorSession`. Read actions can pass `targetSession: "runtime"` to resolve the live `GameSession` instead of the active editor tab. When a game session is available, play state includes `gameSessionDetails` with the runtime session type, scene name, source path, object count, component count, and parent editor-session information.

`editor.compile_status` is event-observed. It tracks `CompileGroup` instances from `compile.started`, then reads their current `IsBuilding`, `NeedsBuild`, compiler build status, and Roslyn diagnostics. If no compile has started since the bridge library loaded, the action returns zero observed groups with an explicit note instead of pretending the project has no errors.

`editor.logs` is a file-tail view of `Environment.CurrentDirectory/logs/sbox-dev.log`. The raw log line is the authoritative value. The `level` field is inferred from text so agents can filter obvious errors/warnings, but it should not be treated as a perfect structured log level. Each entry has a stable file line `index`; callers can pass `afterIndex` to `editor.logs` or `editor.feedback` and should use `verified.logs.nextAfterIndex` from a baseline read before testing a change.

## Agent Loop

Recommended agent flow after source or scene changes:

1. Call `editor.feedback` before the change to capture baseline play/compile/log context.
2. Make one small source or scene change.
3. If source changed, wait for hotload and call `editor.compile_status` or `editor.feedback`.
4. If compile diagnostics are present, fix those before adding features.
5. If compile is clean enough to test, call `editor.play`.
6. Call `editor.feedback` with `targetSession: "runtime"` while playing and the baseline `afterIndex` to inspect the live game session and only new errors.
7. Use `runtime.list_test_actions` / `runtime.run_test_action` for deterministic gameplay or UI assertions instead of shell-level keypresses.
8. Call `editor.stop` before returning to editor-scene mutations, or `editor.stop` with `stopAll: true` before a smoke test that must clear stale play sessions.
9. If scene reads become empty or stale after play/stop, call `editor.open_scene` with `forceReload: true` for the saved scene path before continuing editor-scene mutations. Scratch/test scenes may also pass `discardUnsaved: true` when a stale duplicate tab blocks reload.

## Current ARPG POC Finding

The first ARPG POC showed that play-mode state and scene inspection were not fully aligned. That specific runtime targeting gap is now partially closed: `scene.summary`, `scene.hierarchy`, `scene.find`, `scene.details`, `gameobject.get`, component read actions, `editor.feedback`, and `visual.capture_camera` accept `targetSession: "runtime"` for live `GameSession` reads.

The ARPG testbed also exposes component-authored runtime test actions, and the focused smoke verifies inventory open, damage, restore, skill listing, and zombie count without relying on OS input focus. This is a deterministic self-report path, not generic viewport/HUD capture. The same smoke exposed that the current ScreenPanel child panels are not being built in the testbed, which is now visible through bridge read-back.

`editor.open_scene` supports `forceReload: true` to recover the editable scene, and `discardUnsaved: true` for scratch/test scenes where a stale unsaved duplicate tab blocks reload. This is a recovery path; post-transition wait helpers and editor-scene restoration are still needed.

## Open Work

- Add a wait action for compile/hotload completion instead of requiring clients to poll.
- Capture structured live `LogEvent` entries if a stable public hook is verified for editor libraries.
- Add post-transition wait and restoration helpers so stale duplicate scene tabs do not require manual `open_scene` recovery.
- Broaden runtime/game-session inspection beyond component-authored self-report where stable APIs exist.
- Extend visual feedback beyond `visual.capture_camera` with annotated captures, isolated asset previews, viewport/HUD capture, and capture comparison workflows.
- Add focused viewport input injection if direct game-input verification is still needed after deterministic runtime actions.
