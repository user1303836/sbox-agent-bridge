using System.Linq;

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
}
