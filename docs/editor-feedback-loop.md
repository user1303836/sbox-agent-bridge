# Editor Feedback Loop

The bridge feedback loop is designed around one rule: every signal should say where it came from. Agents should be able to tell the difference between authoritative editor state, observed compiler state, and raw log output.

## Current Feedback Actions

- `editor.play_state`: authoritative read from `SceneEditorSession.Active`.
- `editor.play`: calls `SceneEditorSession.SetPlaying(...)` and returns a play-state read-back.
- `editor.stop`: calls `SceneEditorSession.StopPlaying()` and returns a play-state read-back.
- `editor.compile_status`: reports compile groups observed by the bridge through s&box `compile.started` events.
- `editor.logs`: tails the editor `sbox-dev.log` file.
- `editor.feedback`: returns play state, compile status, and recent logs in one response.

## Signal Accuracy

`editor.play_state`, `editor.play`, and `editor.stop` are authoritative for the active editor session. The bridge reads `IsPlaying`, `GameSession`, active scene name, and unsaved-change state directly from `SceneEditorSession.Active`.

`editor.compile_status` is event-observed. It tracks `CompileGroup` instances from `compile.started`, then reads their current `IsBuilding`, `NeedsBuild`, compiler build status, and Roslyn diagnostics. If no compile has started since the bridge library loaded, the action returns zero observed groups with an explicit note instead of pretending the project has no errors.

`editor.logs` is a file-tail view of `Environment.CurrentDirectory/logs/sbox-dev.log`. The raw log line is the authoritative value. The `level` field is inferred from text so agents can filter obvious errors/warnings, but it should not be treated as a perfect structured log level.

## Agent Loop

Recommended agent flow after source or scene changes:

1. Call `editor.feedback` before the change to capture baseline play/compile/log context.
2. Make one small source or scene change.
3. If source changed, wait for hotload and call `editor.compile_status` or `editor.feedback`.
4. If compile diagnostics are present, fix those before adding features.
5. If compile is clean enough to test, call `editor.play`.
6. Call `editor.feedback` while playing to inspect play state and recent errors.
7. Call `editor.stop` before returning to editor-scene mutations.

## Open Work

- Add a wait action for compile/hotload completion instead of requiring clients to poll.
- Capture structured live `LogEvent` entries if a stable public hook is verified for editor libraries.
- Separate runtime/game-session inspection from editor-scene inspection so agents do not confuse play-mode objects with editable scene objects.
- Add viewport or screenshot feedback once a reliable editor capture path is verified.
