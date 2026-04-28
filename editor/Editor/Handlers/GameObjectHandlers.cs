using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class GameObjectHandlers
{
	public static BridgeResponse Create( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var name = HandlerUtil.GetString( request.Payload, "name", "Agent Object" );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );

		GameObject go;

		using ( session.UndoScope( "Agent Bridge: Create GameObject" ).WithGameObjectCreations().Push() )
		{
			go = new GameObject( true, name );
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
}
