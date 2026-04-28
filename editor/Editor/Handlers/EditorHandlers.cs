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
		var selected = session.GetSelection().Select( x => x?.ToString() ?? "" ).Where( x => x.Length > 0 ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Editor context read",
			verified = new
			{
				scene = session.Scene.Name,
				hasUnsavedChanges = session.HasUnsavedChanges,
				isPlaying = session.IsPlaying,
				selection = selected
			}
		} );
	}
}
