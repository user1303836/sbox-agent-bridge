namespace SboxAgentBridge.Editor;

internal static class CommandDispatcher
{
	public static BridgeResponse Dispatch( BridgeRequest request )
	{
		return request.Action switch
		{
			"bridge.status" => EditorHandlers.Status( request ),
			"editor.context" => EditorHandlers.Context( request ),
			"editor.get_selection" => EditorHandlers.GetSelection( request ),
			"editor.set_selection" => EditorHandlers.SetSelection( request ),
			"scene.summary" => SceneHandlers.Summary( request ),
			"scene.hierarchy" => SceneHandlers.Hierarchy( request ),
			"scene.find" => SceneHandlers.Find( request ),
			"scene.details" => SceneHandlers.Details( request ),
			"gameobject.get" => GameObjectHandlers.Get( request ),
			"gameobject.create" => GameObjectHandlers.Create( request ),
			"gameobject.rename" => GameObjectHandlers.Rename( request ),
			"gameobject.set_transform" => GameObjectHandlers.SetTransform( request ),
			"gameobject.set_enabled" => GameObjectHandlers.SetEnabled( request ),
			_ => BridgeResponse.Fail(
				request.Id,
				$"Unknown bridge action '{request.Action}'",
				"Use one of: bridge.status, editor.context, editor.get_selection, editor.set_selection, scene.summary, scene.hierarchy, scene.find, scene.details, gameobject.get, gameobject.create, gameobject.rename, gameobject.set_transform, gameobject.set_enabled."
			)
		};
	}
}
