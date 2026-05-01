using System;
using System.IO;
using System.Text;
using System.Text.Json;
using Sandbox;

namespace SboxAgentBridge.Editor;

public static class BridgeRuntime
{
	public const string Version = "0.1.0";

	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = true
	};

	public static string IpcRoot { get; } = Path.Combine( Path.GetTempPath(), "sbox-agent-bridge" );
	public static string RequestPath => Path.Combine( IpcRoot, "requests" );
	public static string ResponsePath => Path.Combine( IpcRoot, "responses" );
	public static bool IsRunning { get; private set; }

	public static void Start()
	{
		Directory.CreateDirectory( RequestPath );
		Directory.CreateDirectory( ResponsePath );
		IsRunning = true;
		Log.Info( $"sbox Agent Bridge listening via file IPC: {IpcRoot}" );
	}

	public static void Stop()
	{
		IsRunning = false;
		Log.Info( "sbox Agent Bridge stopped" );
	}

	public static void Pump()
	{
		if ( !IsRunning )
			return;

		if ( !Directory.Exists( RequestPath ) )
			return;

		foreach ( var file in Directory.GetFiles( RequestPath, "request-*.json" ) )
		{
			ProcessRequestFile( file );
		}
	}

	private static void ProcessRequestFile( string file )
	{
		BridgeRequest? request = null;
		var shouldDelete = false;

		try
		{
			string json;

			try
			{
				json = File.ReadAllText( file, Encoding.UTF8 );
			}
			catch ( IOException )
			{
				return;
			}
			catch ( UnauthorizedAccessException )
			{
				return;
			}

			shouldDelete = true;
			request = JsonSerializer.Deserialize<BridgeRequest>( json, JsonOptions );

			if ( request is null || string.IsNullOrWhiteSpace( request.Id ) || string.IsNullOrWhiteSpace( request.Action ) )
			{
				WriteResponse( BridgeResponse.Fail( request?.Id ?? "unknown", "Invalid bridge request", "Expected id, action, and payload." ) );
				return;
			}

			var response = CommandDispatcher.Dispatch( request );
			WriteResponse( response );
		}
		catch ( Exception ex )
		{
			WriteResponse( BridgeResponse.Fail( request?.Id ?? Path.GetFileNameWithoutExtension( file ), ex.Message ) );
		}
		finally
		{
			if ( shouldDelete )
			{
				try
				{
					File.Delete( file );
				}
				catch ( Exception ex )
				{
					Log.Warning( $"Failed to delete processed bridge request: {ex.Message}" );
				}
			}
		}
	}

	private static void WriteResponse( BridgeResponse response )
	{
		Directory.CreateDirectory( ResponsePath );

		var path = Path.Combine( ResponsePath, $"response-{response.Id}.json" );
		var tempPath = Path.Combine( ResponsePath, $".response-{response.Id}.{Guid.NewGuid():N}.tmp" );
		var json = JsonSerializer.Serialize( response, JsonOptions );
		File.WriteAllText( tempPath, json, new UTF8Encoding( false ) );

		if ( File.Exists( path ) )
		{
			File.Delete( path );
		}

		File.Move( tempPath, path );
	}
}
