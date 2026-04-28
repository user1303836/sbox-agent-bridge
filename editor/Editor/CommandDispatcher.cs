namespace SboxAgentBridge.Editor;

internal static class CommandDispatcher
{
	public static BridgeResponse Dispatch( BridgeRequest request )
	{
		return request.Action switch
		{
			"bridge.status" => EditorHandlers.Status( request ),
			"editor.context" => EditorHandlers.Context( request ),
			"scene.summary" => SceneHandlers.Summary( request ),
			"scene.hierarchy" => SceneHandlers.Hierarchy( request ),
			"scene.find" => SceneHandlers.Find( request ),
			"gameobject.create" => GameObjectHandlers.Create( request ),
			_ => BridgeResponse.Fail(
				request.Id,
				$"Unknown bridge action '{request.Action}'",
				"Use one of: bridge.status, editor.context, scene.summary, scene.hierarchy, scene.find, gameobject.create."
			)
		};
	}
}
