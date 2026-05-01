using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class EditorHandlers
{
	public static BridgeResponse Status( BridgeRequest request )
	{
		var session = HandlerUtil.ActiveSession;

		return BridgeResponse.Success( request.Id, new
		{
			message = "Bridge status read",
			verified = new
			{
				bridge = "sbox-agent-bridge",
				version = BridgeRuntime.Version,
				running = BridgeRuntime.IsRunning,
				ipcRoot = BridgeRuntime.IpcRoot,
				hasActiveSession = session is not null,
				activeScene = session?.Scene?.Name,
				isPlaying = session?.IsPlaying ?? false
			}
		} );
	}

	public static BridgeResponse Doctor( BridgeRequest request )
	{
		var maxDiagnostics = HandlerUtil.GetInt( request.Payload, "maxDiagnostics", 10 );
		var maxLines = HandlerUtil.GetInt( request.Payload, "maxLines", 40 );
		var mcpServerVersion = HandlerUtil.GetString( request.Payload, "mcpServerVersion" );
		var checks = new List<DoctorCheck>();
		var session = HandlerUtil.ActiveSession;
		var tabs = DescribeTabsSnapshot();
		var compileHealth = EditorFeedbackState.GetCompileHealth();
		var ipc = DescribeIpcHealth();
		var bridgeLogs = EditorFeedbackState.DescribeLogs( maxLines, "Agent Bridge", "all" );
		var project = DescribeProject();

		AddCheck(
			checks,
			"bridge-running",
			BridgeRuntime.IsRunning ? "pass" : "fail",
			BridgeRuntime.IsRunning ? "Bridge runtime is running." : "Bridge runtime is stopped.",
			BridgeRuntime.IsRunning ? "" : "Open View > Agent Bridge and click Start Bridge, or reload the editor library."
		);

		AddCheck(
			checks,
			"ipc-writable",
			ipc.Writable ? "pass" : "fail",
			ipc.Writable ? "Bridge IPC directories are writable." : $"Bridge IPC is not writable: {ipc.WriteError}",
			ipc.Writable ? "" : "Check filesystem permissions for the IPC root or set SBOX_AGENT_BRIDGE_IPC to a writable folder."
		);

		AddCheck(
			checks,
			"active-session",
			session is null ? "fail" : "pass",
			session is null ? "No active editor scene session." : $"Active scene session: {SafeRead( () => session.Scene?.Name ?? "" )}",
			session is null ? "Open a scene in the s&box editor before running scene or component tools." : ""
		);

		AddCheck(
			checks,
			"compile-status",
			compileHealth.ObservedGroupCount == 0 ? "warn" : compileHealth.ErrorCount > 0 ? "fail" : compileHealth.IsBuilding || compileHealth.NeedsBuild ? "warn" : "pass",
			compileHealth.ObservedGroupCount == 0
				? "No compile group has been observed since the bridge loaded."
				: $"Compile groups observed: {compileHealth.ObservedGroupCount}; errors: {compileHealth.ErrorCount}; warnings: {compileHealth.WarningCount}.",
			compileHealth.ObservedGroupCount == 0
				? "Trigger a compile/hotload or reopen the project if you need compiler diagnostics."
				: compileHealth.ErrorCount > 0
					? "Run editor.compile_status with maxDiagnostics and fix compiler errors before testing."
					: compileHealth.IsBuilding || compileHealth.NeedsBuild
						? "Wait for compile to settle with editor.wait_compile before mutating the scene."
						: ""
		);

		AddCheck(
			checks,
			"stale-play-tabs",
			tabs.PlayingEditorTabCount == 0 ? "pass" : "warn",
			tabs.PlayingEditorTabCount == 0 ? "No playing editor sessions are currently reported." : $"{tabs.PlayingEditorTabCount} editor session(s) still report play mode.",
			tabs.PlayingEditorTabCount == 0 ? "" : "Run editor.recover_scene or editor.stop with stopAll:true before smoke tests."
		);

		AddCheck(
			checks,
			"source-scene",
			session is not null && !string.IsNullOrWhiteSpace( SafeRead( () => session.Scene?.Source?.ResourcePath ?? "" ) ) ? "pass" : "warn",
			session is null
				? "No active scene to check for a source path."
				: string.IsNullOrWhiteSpace( SafeRead( () => session.Scene?.Source?.ResourcePath ?? "" ) )
					? "Active scene has no source path."
					: $"Active scene source: {SafeRead( () => session.Scene?.Source?.ResourcePath ?? "" )}.",
			session is null || !string.IsNullOrWhiteSpace( SafeRead( () => session.Scene?.Source?.ResourcePath ?? "" ) ) ? "" : "Save the scene once or open a sourced scene before testing save/reload workflows."
		);

		var overall = checks.Any( x => x.Status == "fail" )
			? "fail"
			: checks.Any( x => x.Status == "warn" )
				? "warn"
				: "pass";

		return BridgeResponse.Success( request.Id, new
		{
			message = overall == "pass" ? "Bridge doctor passed" : overall == "warn" ? "Bridge doctor found warnings" : "Bridge doctor found failures",
			verified = new
			{
				overall,
				bridge = new
				{
					name = "sbox-agent-bridge",
					version = BridgeRuntime.Version,
					running = BridgeRuntime.IsRunning,
					ipcRoot = BridgeRuntime.IpcRoot
				},
				mcp = new
				{
					version = string.IsNullOrWhiteSpace( mcpServerVersion ) ? "unknown-direct-ipc" : mcpServerVersion
				},
				project,
				ipc,
				tabs,
				compileHealth,
				compileStatus = EditorFeedbackState.DescribeCompileStatus( maxDiagnostics ),
				bridgeLogs,
				checks,
				nextSuggestedAction = GetDoctorNextAction( checks )
			}
		} );
	}

	public static BridgeResponse Context( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var selection = session.GetSelection().Select( HandlerUtil.DescribeSelectionItem ).ToArray();
		var activeTab = DescribeEditorTab( session, FindSessionIndex( session ), true );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor context read",
			verified = new
			{
				scene = session.Scene.Name,
				hasUnsavedChanges = session.HasUnsavedChanges,
				isPlaying = session.IsPlaying,
				activeTab,
				selectionCount = selection.Length,
				selection
			}
		} );
	}

	public static BridgeResponse Tabs( BridgeRequest request )
	{
		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor tabs read",
			verified = DescribeTabsSnapshot()
		} );
	}

	public static BridgeResponse ActivateTab( BridgeRequest request )
	{
		var sessions = SceneEditorSession.All.ToArray();
		var before = SceneEditorSession.Active;
		var bringToFront = HandlerUtil.GetBool( request.Payload, "bringToFront", true );
		var index = HandlerUtil.GetInt( request.Payload, "index", -1 );
		var id = HandlerUtil.GetString( request.Payload, "id" );
		var path = NormalizeTabPath( HandlerUtil.GetString( request.Payload, "path" ) );
		var scene = HandlerUtil.GetString( request.Payload, "scene" );

		SceneEditorSession? match = null;
		string matchMode;

		if ( index >= 0 )
		{
			if ( index >= sessions.Length )
				throw new InvalidOperationException( $"No editor tab exists at index {index}." );

			match = sessions[index];
			matchMode = "index";
		}
		else if ( !string.IsNullOrWhiteSpace( id ) )
		{
			match = sessions.FirstOrDefault( x => string.Equals( GetSessionId( x ), id, StringComparison.OrdinalIgnoreCase ) );
			matchMode = "id";
		}
		else if ( !string.IsNullOrWhiteSpace( path ) )
		{
			match = sessions.FirstOrDefault( x => string.Equals( NormalizeTabPath( GetSessionSourcePath( x ) ), path, StringComparison.OrdinalIgnoreCase ) );
			matchMode = "path";
		}
		else if ( !string.IsNullOrWhiteSpace( scene ) )
		{
			match = sessions.FirstOrDefault( x =>
				string.Equals( x.Scene?.Name ?? "", scene, StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( x.Scene?.Source?.ResourceName ?? "", scene, StringComparison.OrdinalIgnoreCase )
			);
			matchMode = "scene";
		}
		else
		{
			throw new InvalidOperationException( "activate_tab requires one of: index, id, path, or scene." );
		}

		if ( match is null )
			throw new InvalidOperationException( "No editor tab matched the requested selector." );

		match.MakeActive( bringToFront );
		var after = SceneEditorSession.Active ?? match;

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor tab activated",
			verified = new
			{
				matchMode,
				bringToFront,
				requested = new
				{
					index,
					id,
					path,
					scene
				},
				before = before is null ? null : DescribeEditorTab( before, FindSessionIndex( before, sessions ), ReferenceEquals( before, SceneEditorSession.Active ) ),
				activated = DescribeEditorTab( match, FindSessionIndex( match, sessions ), ReferenceEquals( match, SceneEditorSession.Active ) ),
				after = DescribeEditorTab( after, FindSessionIndex( after ), ReferenceEquals( after, SceneEditorSession.Active ) )
			}
		} );
	}

	public static BridgeResponse OpenScene( BridgeRequest request )
	{
		var path = HandlerUtil.GetRequiredString( request.Payload, "path" ).Replace( '\\', '/' ).TrimStart( '/' );
		var bringToFront = HandlerUtil.GetBool( request.Payload, "bringToFront", true );
		var forceReload = HandlerUtil.GetBool( request.Payload, "forceReload", false );
		var discardUnsaved = HandlerUtil.GetBool( request.Payload, "discardUnsaved", false );
		var result = OpenSceneCore( path, bringToFront, forceReload, discardUnsaved );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor scene opened",
			verified = result
		} );
	}

	public static BridgeResponse RecoverScene( BridgeRequest request )
	{
		var before = DescribeTabsSnapshot();
		var stopAll = HandlerUtil.GetBool( request.Payload, "stopAll", true );
		var bringToFront = HandlerUtil.GetBool( request.Payload, "bringToFront", true );
		var forceReload = HandlerUtil.GetBool( request.Payload, "forceReload", true );
		var discardUnsaved = HandlerUtil.GetBool( request.Payload, "discardUnsaved", false );
		var requestedPath = HandlerUtil.GetString( request.Payload, "path" );
		var path = string.IsNullOrWhiteSpace( requestedPath ) ? FindRecoverScenePath() : requestedPath;
		object stopResult = null;

		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidOperationException( "recover_scene requires a path when no sourced editor scene can be inferred from open tabs." );

		if ( stopAll )
			stopResult = StopAll( request ).Result;

		var openResult = OpenSceneCore( path, bringToFront, forceReload, discardUnsaved );
		var after = DescribeTabsSnapshot();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor scene recovery requested",
			verified = new
			{
				requestedPath,
				resolvedPath = path,
				stopAll,
				bringToFront,
				forceReload,
				discardUnsaved,
				before,
				stop = stopResult,
				open = openResult,
				after
			}
		} );
	}

	private static object OpenSceneCore( string path, bool bringToFront, bool forceReload, bool discardUnsaved )
	{
		path = (path ?? "").Replace( '\\', '/' ).TrimStart( '/' );
		var sceneFile = ResourceLibrary.Get<SceneFile>( path );
		var resolution = "resource-library";

		if ( sceneFile is null || !sceneFile.IsValid )
		{
			var asset = AssetSystem.FindByPath( path );
			if ( asset is null && path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
				asset = AssetSystem.FindByPath( path.Substring( "assets/".Length ) );

			if ( asset is not null )
			{
				sceneFile = asset.LoadResource<SceneFile>();
				resolution = $"asset-system:{asset.RelativePath}";
			}
		}

		if ( sceneFile is null || !sceneFile.IsValid )
			throw new InvalidOperationException( $"Could not load scene resource '{path}'." );

		var existing = SceneEditorSession.Resolve( sceneFile );

		if ( existing is null )
		{
			EditorScene.OpenScene( sceneFile );
			existing = SceneEditorSession.Resolve( sceneFile ) ?? SceneEditorSession.Active;
		}
		else if ( forceReload )
		{
			if ( existing.HasUnsavedChanges && !discardUnsaved )
				throw new InvalidOperationException( $"Scene '{path}' has unsaved changes; refusing forceReload without discardUnsaved:true." );

			existing.Destroy();
			EditorScene.OpenScene( sceneFile );
			existing = SceneEditorSession.Resolve( sceneFile ) ?? SceneEditorSession.Active;
		}

		if ( existing is null )
			throw new InvalidOperationException( $"Scene '{path}' was opened but no editor session was available." );

		existing.MakeActive( bringToFront );

		return new
		{
			requestedPath = path,
			bringToFront,
			forceReload,
			discardUnsaved,
			resolution,
			scene = existing.Scene.Name,
			hasUnsavedChanges = existing.HasUnsavedChanges,
			source = HandlerUtil.DescribeResourceReference( existing.Scene.Source )
		};
	}

	public static BridgeResponse PlayState( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor play state read",
			verified = DescribePlayState( resolution.Session, resolution )
		} );
	}

	public static BridgeResponse Play( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "editor" );
		var session = resolution.Session;
		var wasPlaying = session.IsPlaying;
		Exception transitionException = null;

		if ( !session.IsPlaying )
		{
			try
			{
				session.SetPlaying( session.Scene );
			}
			catch ( Exception ex )
			{
				transitionException = ex;
			}
		}

		var playState = DescribePlayState( session, resolution );

		return BridgeResponse.Success( request.Id, new
		{
			message = wasPlaying ? "Editor was already in play mode" : "Editor play mode requested",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				wasPlaying,
				transitionPolicy = "state-readback",
				expectedIsPlaying = true,
				transitionPending = !playState.IsPlaying,
				transitionException = transitionException is null ? null : new
				{
					message = transitionException.Message,
					stateChangedDespiteException = TryReadIsPlaying( session, false )
				},
				playState
			}
		} );
	}

	public static BridgeResponse Stop( BridgeRequest request )
	{
		var stopAll = HandlerUtil.GetBool( request.Payload, "stopAll", false ) || HandlerUtil.GetBool( request.Payload, "all", false );
		if ( stopAll )
			return StopAll( request );

		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "editor" );
		var session = resolution.SourceSession ?? resolution.Session;
		var wasPlaying = session.IsPlaying;
		Exception transitionException = null;

		if ( session.IsPlaying )
		{
			try
			{
				session.StopPlaying();
			}
			catch ( Exception ex )
			{
				transitionException = ex;
			}
		}

		var playState = DescribePlayState( session );

		return BridgeResponse.Success( request.Id, new
		{
			message = wasPlaying ? "Editor stop play mode requested" : "Editor was already stopped",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				stopSession = HandlerUtil.DescribeSession( session ),
				wasPlaying,
				transitionPolicy = "state-readback",
				expectedIsPlaying = false,
				transitionPending = playState.IsPlaying,
				transitionException = transitionException is null ? null : new
				{
					message = transitionException.Message,
					stateChangedDespiteException = !TryReadIsPlaying( session, true )
				},
				playState
			}
		} );
	}

	private static BridgeResponse StopAll( BridgeRequest request )
	{
		var sessions = SceneEditorSession.All.ToArray();
		var derivedGameSessionCount = sessions.Count( session => session is GameEditorSession gameSession && gameSession.Parent is not null );
		var orphanGameSessionCount = sessions.Count( session => session is GameEditorSession gameSession && gameSession.Parent is null );
		var stopTargets = new System.Collections.Generic.List<SceneEditorSession>();

		foreach ( var session in sessions )
		{
			var target = session is GameEditorSession gameSession && gameSession.Parent is not null
				? gameSession.Parent
				: session;

			if ( target is GameEditorSession )
				continue;

			if ( !TryReadIsPlaying( target, false ) )
				continue;

			if ( stopTargets.Any( existing => ReferenceEquals( existing, target ) ) )
				continue;

			stopTargets.Add( target );
		}

		var attempts = stopTargets
			.Select( session =>
			{
				Exception transitionException = null;

				try
				{
					session.StopPlaying();
				}
				catch ( Exception ex )
				{
					transitionException = ex;
				}

				var playState = DescribePlayState( session );

				return new
				{
					session = HandlerUtil.DescribeSession( session ),
					transitionPending = playState.IsPlaying,
					transitionException = transitionException is null ? null : new
					{
						message = transitionException.Message,
						stateChangedDespiteException = !TryReadIsPlaying( session, true )
					},
					playState
				};
			} )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = attempts.Length > 0 ? "Editor stop all play sessions requested" : "No playing editor sessions found",
			verified = new
			{
				stopAll = true,
				transitionPolicy = "state-readback",
				expectedIsPlaying = false,
				sessionCount = sessions.Length,
				derivedGameSessionCount,
				orphanGameSessionCount,
				attemptedCount = attempts.Length,
				transitionPending = attempts.Any( attempt => attempt.transitionPending ),
				sessions = attempts,
				activePlayState = SceneEditorSession.Active is null ? null : DescribePlayState( SceneEditorSession.Active )
			}
		} );
	}

	public static BridgeResponse Logs( BridgeRequest request )
	{
		var maxLines = HandlerUtil.GetInt( request.Payload, "maxLines", 100 );
		var contains = HandlerUtil.GetString( request.Payload, "contains" );
		var level = HandlerUtil.GetString( request.Payload, "level", "all" );
		var afterIndex = HandlerUtil.GetInt( request.Payload, "afterIndex", -1 );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor logs read",
			verified = EditorFeedbackState.DescribeLogs( maxLines, contains, level, afterIndex )
		} );
	}

	public static BridgeResponse CompileStatus( BridgeRequest request )
	{
		var maxDiagnostics = HandlerUtil.GetInt( request.Payload, "maxDiagnostics", 20 );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor compile status read",
			verified = EditorFeedbackState.DescribeCompileStatus( maxDiagnostics )
		} );
	}

	public static BridgeResponse Feedback( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );
		var session = resolution.Session;
		var maxDiagnostics = HandlerUtil.GetInt( request.Payload, "maxDiagnostics", 20 );
		var maxLines = HandlerUtil.GetInt( request.Payload, "maxLines", 100 );
		var contains = HandlerUtil.GetString( request.Payload, "contains" );
		var level = HandlerUtil.GetString( request.Payload, "level", "all" );
		var afterIndex = HandlerUtil.GetInt( request.Payload, "afterIndex", -1 );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor feedback read",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				playState = DescribePlayState( session, resolution ),
				compileStatus = EditorFeedbackState.DescribeCompileStatus( maxDiagnostics ),
				logs = EditorFeedbackState.DescribeLogs( maxLines, contains, level, afterIndex )
			}
		} );
	}

	public static BridgeResponse GetSelection( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var selection = session.GetSelection().Select( HandlerUtil.DescribeSelectionItem ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor selection read",
			verified = new
			{
				count = selection.Length,
				selection
			}
		} );
	}

	public static BridgeResponse SetSelection( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();

		if ( request.Payload.ValueKind != System.Text.Json.JsonValueKind.Object || !request.Payload.TryGetProperty( "ids", out var idsElement ) || idsElement.ValueKind != System.Text.Json.JsonValueKind.Array )
			throw new System.InvalidOperationException( "set_selection requires an ids array. Use an empty array to clear the selection." );

		var ids = HandlerUtil.GetStringArray( request.Payload, "ids" );
		var gameObjects = ids.Select( id => HandlerUtil.RequireGameObjectById( session.Scene, id ) ).ToArray();

		session.PushUndoSelection();
		session.Selection.Clear();

		foreach ( var go in gameObjects )
		{
			session.Selection.Add( go );
		}

		var selection = session.GetSelection().Select( HandlerUtil.DescribeSelectionItem ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor selection set",
			verified = new
			{
				requestedIds = ids,
				count = selection.Length,
				selection
			}
		} );
	}

	public static BridgeResponse SaveScene( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var saveAs = HandlerUtil.GetBool( request.Payload, "saveAs", false );
		var dryRun = HandlerUtil.GetBool( request.Payload, "dryRun", false );
		var before = DescribeSaveState( session );

		if ( dryRun )
		{
			return BridgeResponse.Success( request.Id, new
			{
				message = "Editor scene save checked",
				verified = new
				{
					dryRun,
					saveAs,
					saveAttempted = false,
					saveVerified = false,
					skippedReason = "dryRun was true",
					before,
					after = before
				}
			} );
		}

		if ( !saveAs && !before.HasSourcePath )
		{
			return BridgeResponse.Success( request.Id, new
			{
				message = "Editor scene save skipped",
				verified = new
				{
					dryRun,
					saveAs,
					saveAttempted = false,
					saveVerified = false,
					skippedReason = "Active scene has no source path. Save the scene once in the editor, or call with saveAs:true for a human-visible save-as flow.",
					before,
					after = before
				}
			} );
		}

		session.Save( saveAs );
		var after = DescribeSaveState( session );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor scene save requested",
			verified = new
			{
				dryRun,
				saveAs,
				saveAttempted = true,
				saveVerified = !after.HasUnsavedChanges,
				skippedReason = "",
				before,
				after
			}
		} );
	}

	public static BridgeResponse Undo( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var undone = session.UndoSystem.Undo();

		return BridgeResponse.Success( request.Id, new
		{
			message = undone ? "Editor undo applied" : "Editor undo had nothing to apply",
			verified = new
			{
				undone,
				hasUnsavedChanges = session.HasUnsavedChanges
			}
		} );
	}

	public static BridgeResponse Redo( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var redone = session.UndoSystem.Redo();

		return BridgeResponse.Success( request.Id, new
		{
			message = redone ? "Editor redo applied" : "Editor redo had nothing to apply",
			verified = new
			{
				redone,
				hasUnsavedChanges = session.HasUnsavedChanges
			}
		} );
	}

	public static BridgeResponse FrameObject( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );

		session.FrameTo( go.GetBounds() );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor framed GameObject",
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	private static PlayStateSnapshot DescribePlayState( SceneEditorSession session, HandlerUtil.SessionResolution? resolution = null )
	{
		var readErrors = new System.Collections.Generic.List<object>();
		var scene = "";
		var hasUnsavedChanges = false;
		var isPlaying = false;
		var hasGameSession = false;
		var gameSession = "";
		object gameSessionDetails = null;

		try
		{
			scene = session.Scene?.Name ?? "";
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "scene", ex );
		}

		try
		{
			hasUnsavedChanges = session.HasUnsavedChanges;
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "hasUnsavedChanges", ex );
		}

		try
		{
			isPlaying = session.IsPlaying;
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "isPlaying", ex );
		}

		try
		{
			var currentGameSession = session.GameSession;
			hasGameSession = currentGameSession is not null;
			gameSession = currentGameSession?.ToString() ?? "";
			gameSessionDetails = currentGameSession is null ? null : DescribeGameSession( currentGameSession );
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "gameSession", ex );
		}

		return new PlayStateSnapshot
		{
			Scene = scene,
			HasUnsavedChanges = hasUnsavedChanges,
			IsPlaying = isPlaying,
			HasGameSession = hasGameSession,
			GameSession = gameSession,
			GameSessionDetails = gameSessionDetails,
			TargetSession = resolution is null ? null : HandlerUtil.DescribeSessionResolution( resolution ),
			ReadErrors = readErrors.ToArray()
		};
	}

	private static object DescribeGameSession( SceneEditorSession gameSession )
	{
		var readErrors = new System.Collections.Generic.List<object>();
		var sceneName = "";
		var sourcePath = "";
		var objectCount = 0;
		var componentCount = 0;

		try
		{
			sceneName = gameSession.Scene?.Name ?? "";
			sourcePath = gameSession.Scene?.Source?.ResourcePath ?? "";

			if ( gameSession.Scene is not null )
			{
				var objects = HandlerUtil.WalkSceneObjects( gameSession.Scene ).ToArray();
				objectCount = objects.Length;
				componentCount = objects.Sum( x => x.Components.GetAll().Count() );
			}
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "scene", ex );
		}

		var parent = gameSession is GameEditorSession gameEditorSession && gameEditorSession.Parent is not null
			? new
			{
				id = GetSessionId( gameEditorSession.Parent ),
				scene = gameEditorSession.Parent.Scene?.Name ?? "",
				sourcePath = GetSessionSourcePath( gameEditorSession.Parent )
			}
			: null;

		return new
		{
			type = gameSession.GetType().FullName ?? gameSession.GetType().Name,
			id = GetSessionId( gameSession ),
			scene = sceneName,
			sourcePath,
			isPlaying = TryReadIsPlaying( gameSession, false ),
			objectCount,
			componentCount,
			parent,
			readErrors = readErrors.ToArray()
		};
	}

	private static bool TryReadIsPlaying( SceneEditorSession session, bool fallback )
	{
		try
		{
			return session.IsPlaying;
		}
		catch
		{
			return fallback;
		}
	}

	private static void AddReadError( System.Collections.Generic.List<object> readErrors, string field, Exception ex )
	{
		readErrors.Add( new
		{
			field,
			message = ex.Message
		} );
	}

	private static SaveStateSnapshot DescribeSaveState( SceneEditorSession session )
	{
		var readErrors = new System.Collections.Generic.List<object>();
		var scene = "";
		var hasUnsavedChanges = false;
		Resource? source = null;

		try
		{
			scene = session.Scene?.Name ?? "";
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "scene", ex );
		}

		try
		{
			hasUnsavedChanges = session.HasUnsavedChanges;
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "hasUnsavedChanges", ex );
		}

		try
		{
			source = session.Scene?.Source;
		}
		catch ( Exception ex )
		{
			AddReadError( readErrors, "source", ex );
		}

		var sourcePath = source?.ResourcePath ?? "";

		return new SaveStateSnapshot
		{
			Scene = scene,
			HasUnsavedChanges = hasUnsavedChanges,
			HasSource = source is not null,
			HasSourcePath = !string.IsNullOrWhiteSpace( sourcePath ),
			SourcePath = sourcePath,
			Source = HandlerUtil.DescribeResourceReference( source ),
			ReadErrors = readErrors.ToArray()
		};
	}

	private static TabsSnapshot DescribeTabsSnapshot()
	{
		var sessions = SceneEditorSession.All.ToArray();
		var active = SceneEditorSession.Active;
		var tabs = sessions
			.Select( ( session, index ) => DescribeEditorTab( session, index, ReferenceEquals( session, active ) ) )
			.ToArray();
		var editorTabs = sessions.Where( session => session is not GameEditorSession ).ToArray();
		var gameSessionTabs = sessions.Where( session => session is GameEditorSession ).ToArray();
		var playingEditorTabs = editorTabs.Where( session => TryReadIsPlaying( session, false ) || SafeRead( () => session.GameSession is not null, false ) ).ToArray();
		var duplicateSourcePaths = editorTabs
			.Select( GetSessionSourcePath )
			.Where( path => !string.IsNullOrWhiteSpace( path ) )
			.GroupBy( path => NormalizeTabPath( path ), StringComparer.OrdinalIgnoreCase )
			.Where( group => group.Count() > 1 )
			.Select( group => new DuplicateSourcePathSnapshot
			{
				SourcePath = group.Key,
				Count = group.Count()
			} )
			.ToArray();

		return new TabsSnapshot
		{
			Count = tabs.Length,
			ActiveIndex = active is null ? -1 : FindSessionIndex( active, sessions ),
			ActiveId = active is null ? "" : GetSessionId( active ),
			EditorTabCount = editorTabs.Length,
			GameSessionTabCount = gameSessionTabs.Length,
			PlayingEditorTabCount = playingEditorTabs.Length,
			DuplicateSourcePaths = duplicateSourcePaths,
			Tabs = tabs
		};
	}

	private static string FindRecoverScenePath()
	{
		var active = SceneEditorSession.Active;
		var activeSource = active is GameEditorSession gameSession && gameSession.Parent is not null
			? GetSessionSourcePath( gameSession.Parent )
			: active is null ? "" : GetSessionSourcePath( active );

		if ( !string.IsNullOrWhiteSpace( activeSource ) )
			return activeSource;

		return SceneEditorSession.All
			.Where( session => session is not GameEditorSession )
			.Select( GetSessionSourcePath )
			.FirstOrDefault( path => !string.IsNullOrWhiteSpace( path ) ) ?? "";
	}

	private static object DescribeProject()
	{
		return new
		{
			assetsPath = SafeRead( () => Project.Current.GetAssetsPath(), "" ),
			currentDirectory = SafeRead( () => Environment.CurrentDirectory, "" )
		};
	}

	private static IpcHealthSnapshot DescribeIpcHealth()
	{
		var snapshot = new IpcHealthSnapshot
		{
			Root = BridgeRuntime.IpcRoot,
			RequestPath = BridgeRuntime.RequestPath,
			ResponsePath = BridgeRuntime.ResponsePath,
			RootExists = Directory.Exists( BridgeRuntime.IpcRoot ),
			RequestPathExists = Directory.Exists( BridgeRuntime.RequestPath ),
			ResponsePathExists = Directory.Exists( BridgeRuntime.ResponsePath )
		};

		try
		{
			Directory.CreateDirectory( BridgeRuntime.RequestPath );
			Directory.CreateDirectory( BridgeRuntime.ResponsePath );
			var probePath = Path.Combine( BridgeRuntime.ResponsePath, $".doctor-{Guid.NewGuid():N}.tmp" );
			File.WriteAllText( probePath, "ok" );
			File.Delete( probePath );

			snapshot.RootExists = Directory.Exists( BridgeRuntime.IpcRoot );
			snapshot.RequestPathExists = Directory.Exists( BridgeRuntime.RequestPath );
			snapshot.ResponsePathExists = Directory.Exists( BridgeRuntime.ResponsePath );
			snapshot.Writable = true;
		}
		catch ( Exception ex )
		{
			snapshot.Writable = false;
			snapshot.WriteError = ex.Message;
		}

		return snapshot;
	}

	private static void AddCheck( List<DoctorCheck> checks, string id, string status, string message, string suggestion )
	{
		checks.Add( new DoctorCheck
		{
			Id = id,
			Status = status,
			Message = message,
			Suggestion = suggestion
		} );
	}

	private static string GetDoctorNextAction( IEnumerable<DoctorCheck> checks )
	{
		var first = checks.FirstOrDefault( check => check.Status == "fail" || check.Status == "warn" );
		return first is null || string.IsNullOrWhiteSpace( first.Suggestion )
			? "Run a read-only scene summary, then a focused smoke test if you are preparing for external testing."
			: first.Suggestion;
	}

	private static T SafeRead<T>( Func<T> read, T fallback = default )
	{
		try
		{
			return read();
		}
		catch
		{
			return fallback;
		}
	}

	private static object DescribeEditorTab( SceneEditorSession session, int index, bool isActive )
	{
		var sourcePath = GetSessionSourcePath( session );
		var playState = DescribePlayState( session );

		return new
		{
			index,
			id = GetSessionId( session ),
			isActive,
			isGameSession = session is GameEditorSession,
			parent = session is GameEditorSession gameEditorSession && gameEditorSession.Parent is not null
				? new
				{
					id = GetSessionId( gameEditorSession.Parent ),
					index = FindSessionIndex( gameEditorSession.Parent ),
					scene = gameEditorSession.Parent.Scene?.Name ?? "",
					sourcePath = GetSessionSourcePath( gameEditorSession.Parent )
				}
				: null,
			scene = session.Scene?.Name ?? "",
			isPrefabSession = session.IsPrefabSession,
			shouldUpdate = session.ShouldUpdate,
			hasUnsavedChanges = session.HasUnsavedChanges,
			hasSourcePath = !string.IsNullOrWhiteSpace( sourcePath ),
			sourcePath,
			source = HandlerUtil.DescribeResourceReference( session.Scene?.Source ),
			playState
		};
	}

	private static int FindSessionIndex( SceneEditorSession session )
	{
		return FindSessionIndex( session, SceneEditorSession.All.ToArray() );
	}

	private static int FindSessionIndex( SceneEditorSession session, SceneEditorSession[] sessions )
	{
		for ( var i = 0; i < sessions.Length; i++ )
		{
			if ( ReferenceEquals( sessions[i], session ) )
				return i;
		}

		return -1;
	}

	private static string GetSessionId( SceneEditorSession session )
	{
		return session.Scene?.Id.ToString() ?? "";
	}

	private static string GetSessionSourcePath( SceneEditorSession session )
	{
		return session.Scene?.Source?.ResourcePath ?? "";
	}

	private static string NormalizeTabPath( string path )
	{
		path = (path ?? "").Replace( '\\', '/' ).Trim().TrimStart( '/' );

		if ( path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			path = path.Substring( "assets/".Length );

		return path;
	}

	private sealed class PlayStateSnapshot
	{
		public string Scene { get; set; }
		public bool HasUnsavedChanges { get; set; }
		public bool IsPlaying { get; set; }
		public bool HasGameSession { get; set; }
		public string GameSession { get; set; }
		public object GameSessionDetails { get; set; }
		public object TargetSession { get; set; }
		public object[] ReadErrors { get; set; }
	}

	private sealed class SaveStateSnapshot
	{
		public string Scene { get; set; }
		public bool HasUnsavedChanges { get; set; }
		public bool HasSource { get; set; }
		public bool HasSourcePath { get; set; }
		public string SourcePath { get; set; }
		public object? Source { get; set; }
		public object[] ReadErrors { get; set; }
	}

	private sealed class TabsSnapshot
	{
		public int Count { get; set; }
		public int ActiveIndex { get; set; }
		public string ActiveId { get; set; }
		public int EditorTabCount { get; set; }
		public int GameSessionTabCount { get; set; }
		public int PlayingEditorTabCount { get; set; }
		public DuplicateSourcePathSnapshot[] DuplicateSourcePaths { get; set; }
		public object[] Tabs { get; set; }
	}

	private sealed class DuplicateSourcePathSnapshot
	{
		public string SourcePath { get; set; }
		public int Count { get; set; }
	}

	private sealed class IpcHealthSnapshot
	{
		public string Root { get; set; }
		public string RequestPath { get; set; }
		public string ResponsePath { get; set; }
		public bool RootExists { get; set; }
		public bool RequestPathExists { get; set; }
		public bool ResponsePathExists { get; set; }
		public bool Writable { get; set; }
		public string WriteError { get; set; } = "";
	}

	private sealed class DoctorCheck
	{
		public string Id { get; set; }
		public string Status { get; set; }
		public string Message { get; set; }
		public string Suggestion { get; set; }
	}
}
