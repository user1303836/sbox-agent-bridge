using System;
using System.Linq;
using Editor;

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

		session.Save( saveAs );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor scene save requested",
			verified = new
			{
				scene = session.Scene.Name,
				saveAs,
				hasUnsavedChanges = session.HasUnsavedChanges
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

	private sealed class PlayStateSnapshot
	{
		public string Scene { get; set; }
		public bool HasUnsavedChanges { get; set; }
		public bool IsPlaying { get; set; }
		public bool HasGameSession { get; set; }
		public string GameSession { get; set; }
		public object[] ReadErrors { get; set; }
	}
}
