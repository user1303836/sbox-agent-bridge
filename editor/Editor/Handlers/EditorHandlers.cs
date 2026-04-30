using System;
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
				version = "0.1.0",
				running = BridgeRuntime.IsRunning,
				ipcRoot = BridgeRuntime.IpcRoot,
				hasActiveSession = session is not null,
				activeScene = session?.Scene?.Name,
				isPlaying = session?.IsPlaying ?? false
			}
		} );
	}

	public static BridgeResponse Context( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var selection = session.GetSelection().Select( HandlerUtil.DescribeSelectionItem ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor context read",
			verified = new
			{
				scene = session.Scene.Name,
				hasUnsavedChanges = session.HasUnsavedChanges,
				isPlaying = session.IsPlaying,
				selectionCount = selection.Length,
				selection
			}
		} );
	}

	public static BridgeResponse OpenScene( BridgeRequest request )
	{
		var path = HandlerUtil.GetRequiredString( request.Payload, "path" ).Replace( '\\', '/' ).TrimStart( '/' );
		var bringToFront = HandlerUtil.GetBool( request.Payload, "bringToFront", true );
		var forceReload = HandlerUtil.GetBool( request.Payload, "forceReload", false );
		var sceneFile = ResourceLibrary.Get<SceneFile>( path );

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
			if ( existing.HasUnsavedChanges )
				throw new InvalidOperationException( $"Scene '{path}' has unsaved changes; refusing forceReload." );

			existing.Destroy();
			EditorScene.OpenScene( sceneFile );
			existing = SceneEditorSession.Resolve( sceneFile ) ?? SceneEditorSession.Active;
		}

		if ( existing is null )
			throw new InvalidOperationException( $"Scene '{path}' was opened but no editor session was available." );

		existing.MakeActive( bringToFront );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor scene opened",
			verified = new
			{
				requestedPath = path,
				bringToFront,
				forceReload,
				scene = existing.Scene.Name,
				hasUnsavedChanges = existing.HasUnsavedChanges,
				source = HandlerUtil.DescribeResourceReference( existing.Scene.Source )
			}
		} );
	}

	public static BridgeResponse PlayState( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor play state read",
			verified = DescribePlayState( session )
		} );
	}

	public static BridgeResponse Play( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
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

		var playState = DescribePlayState( session );

		return BridgeResponse.Success( request.Id, new
		{
			message = wasPlaying ? "Editor was already in play mode" : "Editor play mode requested",
			verified = new
			{
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
		var session = HandlerUtil.RequireSession();
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

	public static BridgeResponse Logs( BridgeRequest request )
	{
		var maxLines = HandlerUtil.GetInt( request.Payload, "maxLines", 100 );
		var contains = HandlerUtil.GetString( request.Payload, "contains" );
		var level = HandlerUtil.GetString( request.Payload, "level", "all" );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor logs read",
			verified = EditorFeedbackState.DescribeLogs( maxLines, contains, level )
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
		var session = HandlerUtil.RequireSession();
		var maxDiagnostics = HandlerUtil.GetInt( request.Payload, "maxDiagnostics", 20 );
		var maxLines = HandlerUtil.GetInt( request.Payload, "maxLines", 100 );
		var contains = HandlerUtil.GetString( request.Payload, "contains" );
		var level = HandlerUtil.GetString( request.Payload, "level", "all" );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor feedback read",
			verified = new
			{
				playState = DescribePlayState( session ),
				compileStatus = EditorFeedbackState.DescribeCompileStatus( maxDiagnostics ),
				logs = EditorFeedbackState.DescribeLogs( maxLines, contains, level )
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

	private static PlayStateSnapshot DescribePlayState( SceneEditorSession session )
	{
		var readErrors = new System.Collections.Generic.List<object>();
		var scene = "";
		var hasUnsavedChanges = false;
		var isPlaying = false;
		var hasGameSession = false;
		var gameSession = "";

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
			ReadErrors = readErrors.ToArray()
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

	private sealed class PlayStateSnapshot
	{
		public string Scene { get; set; }
		public bool HasUnsavedChanges { get; set; }
		public bool IsPlaying { get; set; }
		public bool HasGameSession { get; set; }
		public string GameSession { get; set; }
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
}
