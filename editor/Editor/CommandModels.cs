using System.Text.Json;

namespace SboxAgentBridge.Editor;

internal sealed class BridgeRequest
{
	public string Id { get; set; } = "";
	public string Action { get; set; } = "";
	public JsonElement Payload { get; set; }
}

internal sealed class BridgeResponse
{
	public string Id { get; set; } = "";
	public bool Ok { get; set; }
	public object? Result { get; set; }
	public BridgeError? Error { get; set; }

	public static BridgeResponse Success( string id, object result )
	{
		return new BridgeResponse
		{
			Id = id,
			Ok = true,
			Result = result
		};
	}

	public static BridgeResponse Fail( string id, string message, string? suggestion = null )
	{
		return new BridgeResponse
		{
			Id = id,
			Ok = false,
			Error = new BridgeError
			{
				Message = message,
				Suggestion = suggestion
			}
		};
	}
}

internal sealed class BridgeError
{
	public string Message { get; set; } = "";
	public string? Suggestion { get; set; }
}
