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
			"editor.save_scene" => EditorHandlers.SaveScene( request ),
			"editor.undo" => EditorHandlers.Undo( request ),
			"editor.redo" => EditorHandlers.Redo( request ),
			"editor.frame_object" => EditorHandlers.FrameObject( request ),
			"scene.summary" => SceneHandlers.Summary( request ),
			"scene.hierarchy" => SceneHandlers.Hierarchy( request ),
			"scene.find" => SceneHandlers.Find( request ),
			"scene.details" => SceneHandlers.Details( request ),
			"gameobject.get" => GameObjectHandlers.Get( request ),
			"gameobject.create" => GameObjectHandlers.Create( request ),
			"gameobject.rename" => GameObjectHandlers.Rename( request ),
			"gameobject.set_transform" => GameObjectHandlers.SetTransform( request ),
			"gameobject.set_enabled" => GameObjectHandlers.SetEnabled( request ),
			"gameobject.destroy" => GameObjectHandlers.Destroy( request ),
			"gameobject.duplicate" => GameObjectHandlers.Duplicate( request ),
			"gameobject.reparent" => GameObjectHandlers.Reparent( request ),
			"component.list_types" => ComponentHandlers.ListTypes( request ),
			"component.list_on_gameobject" => ComponentHandlers.ListOnGameObject( request ),
			"component.get" => ComponentHandlers.Get( request ),
			"component.get_properties" => ComponentHandlers.GetProperties( request ),
			"component.add" => ComponentHandlers.Add( request ),
			"component.remove" => ComponentHandlers.Remove( request ),
			"component.set_enabled" => ComponentHandlers.SetEnabled( request ),
			"component.set_property" => ComponentHandlers.SetProperty( request ),
			_ => BridgeResponse.Fail(
				request.Id,
				$"Unknown bridge action '{request.Action}'",
				"Use one of: bridge.status, editor.context, editor.get_selection, editor.set_selection, editor.save_scene, editor.undo, editor.redo, editor.frame_object, scene.summary, scene.hierarchy, scene.find, scene.details, gameobject.get, gameobject.create, gameobject.rename, gameobject.set_transform, gameobject.set_enabled, gameobject.destroy, gameobject.duplicate, gameobject.reparent, component.list_types, component.list_on_gameobject, component.get, component.get_properties, component.add, component.remove, component.set_enabled, component.set_property."
			)
		};
	}
}
