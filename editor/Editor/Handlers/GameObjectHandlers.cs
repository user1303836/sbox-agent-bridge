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

		GameObject go;

		using ( session.UndoScope( "Agent Bridge: Create GameObject" ).WithGameObjectCreations().Push() )
		{
			go = session.Scene.CreateObject( true );
			go.Name = name;
			go.MakeNameUnique();

			if ( position.HasValue )
				go.WorldPosition = position.Value;
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
}
