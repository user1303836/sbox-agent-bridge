using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class GameObjectHandlers
{
	public static BridgeResponse Get( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject read",
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse Create( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var name = HandlerUtil.GetString( request.Payload, "name", "Agent Object" );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );
		var parent = HandlerUtil.GetOptionalGameObject( session.Scene, request.Payload );
		var keepWorldPosition = HandlerUtil.GetBool( request.Payload, "keepWorldPosition", true );

		GameObject go;

		var undo = session.UndoScope( "Agent Bridge: Create GameObject" ).WithGameObjectCreations();

		if ( parent is not null )
			undo.WithGameObjectChanges( parent, GameObjectUndoFlags.Children );

		using ( undo.Push() )
		{
			go = session.Scene.CreateObject( true );
			go.Name = name;
			go.MakeNameUnique();

			if ( position.HasValue )
				go.WorldPosition = position.Value;

			if ( parent is not null )
				go.SetParent( parent, keepWorldPosition );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject created",
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse Rename( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var name = HandlerUtil.GetRequiredString( request.Payload, "name" );
		var makeUnique = HandlerUtil.GetBool( request.Payload, "makeUnique", true );
		var previous = HandlerUtil.DescribeGameObject( go );

		using ( session.UndoScope( "Agent Bridge: Rename GameObject" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push() )
		{
			go.Name = name;

			if ( makeUnique )
				go.MakeNameUnique();
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject renamed",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse SetTransform( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );
		var rotation = HandlerUtil.GetRotation( request.Payload, "rotation" );
		var scale = HandlerUtil.GetVector3( request.Payload, "scale" );
		var previous = HandlerUtil.DescribeGameObject( go );

		if ( !position.HasValue && !rotation.HasValue && !scale.HasValue )
			throw new System.InvalidOperationException( "Set transform requires at least one of position, rotation, or scale." );

		using ( session.UndoScope( "Agent Bridge: Set GameObject Transform" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push() )
		{
			if ( position.HasValue )
				go.WorldPosition = position.Value;

			if ( rotation.HasValue )
				go.WorldRotation = rotation.Value;

			if ( scale.HasValue )
				go.WorldScale = scale.Value;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject transform set",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse SetEnabled( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var enabled = HandlerUtil.GetRequiredBool( request.Payload, "enabled" );
		var previous = HandlerUtil.DescribeGameObject( go );

		using ( session.UndoScope( "Agent Bridge: Set GameObject Enabled" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push() )
		{
			go.Enabled = enabled;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject enabled state set",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse Destroy( BridgeRequest request )
	{
		using var scope = SceneEditorSession.Scope();
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var id = go.Id.ToString();
		object previous;

		try
		{
			previous = HandlerUtil.DescribeGameObject( go );
		}
		catch ( System.Exception ex )
		{
			throw new System.InvalidOperationException( $"Failed to describe GameObject before destroy: {ex.Message}", ex );
		}

		try
		{
			EditorScene.Selection.Clear();
			EditorScene.Selection.Add( go );
			SceneEditorMenus.Delete();
		}
		catch ( System.Exception ex )
		{
			throw new System.InvalidOperationException( $"Failed to destroy GameObject through editor delete command: {ex.Message}", ex );
		}

		try
		{
			session.Scene.ProcessDeletes();
		}
		catch ( System.Exception ex )
		{
			throw new System.InvalidOperationException( $"Destroyed GameObject but failed to process scene deletes: {ex.Message}", ex );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject destroyed",
			previous,
			verified = HandlerUtil.DescribeDestroyedGameObject( session.Scene, id )
		} );
	}

	public static BridgeResponse Duplicate( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var source = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var name = HandlerUtil.GetString( request.Payload, "name" );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );
		var offset = HandlerUtil.GetVector3( request.Payload, "offset" );
		GameObject clone;

		using ( session.UndoScope( "Agent Bridge: Duplicate GameObject" ).WithGameObjectCreations().Push() )
		{
			clone = session.Scene.CreateObject( source.Enabled );

			if ( string.IsNullOrWhiteSpace( name ) )
				clone.Name = $"{source.Name} Copy";
			else
				clone.Name = name;

			clone.MakeNameUnique();
			clone.WorldPosition = source.WorldPosition;
			clone.WorldRotation = source.WorldRotation;
			clone.WorldScale = source.WorldScale;

			if ( source.Parent is not null )
				clone.SetParent( source.Parent, true );

			if ( position.HasValue )
				clone.WorldPosition = position.Value;
			else if ( offset.HasValue )
				clone.WorldPosition += offset.Value;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject shallow duplicated",
			shallow = true,
			copiedComponents = false,
			source = HandlerUtil.DescribeGameObject( source ),
			verified = HandlerUtil.DescribeGameObject( clone )
		} );
	}

	public static BridgeResponse Reparent( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var parent = HandlerUtil.GetOptionalGameObject( session.Scene, request.Payload );
		var keepWorldPosition = HandlerUtil.GetBool( request.Payload, "keepWorldPosition", true );
		var previous = HandlerUtil.DescribeGameObject( go );

		if ( parent is not null )
		{
			if ( parent.Id == go.Id )
				throw new System.InvalidOperationException( "A GameObject cannot be parented to itself." );

			var ancestor = parent.Parent;

			while ( ancestor is not null )
			{
				if ( ancestor.Id == go.Id )
					throw new System.InvalidOperationException( "A GameObject cannot be parented under one of its own descendants." );

				ancestor = ancestor.Parent;
			}
		}

		using ( session.UndoScope( "Agent Bridge: Reparent GameObject" ).WithGameObjectChanges( go, GameObjectUndoFlags.All ).Push() )
		{
			go.SetParent( parent!, keepWorldPosition );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject reparented",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}
}
