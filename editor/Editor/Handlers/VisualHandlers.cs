using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class VisualHandlers
{
	public static BridgeResponse CaptureCamera( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var camera = ResolveCamera( session.Scene, request.Payload );
		var width = ClampInt( HandlerUtil.GetInt( request.Payload, "width", 1024 ), 64, 2048 );
		var height = ClampInt( HandlerUtil.GetInt( request.Payload, "height", 576 ), 64, 2048 );
		var name = SanitizeFileName( HandlerUtil.GetString( request.Payload, "name", "camera" ) );
		var captureDirectory = Path.Combine( Path.GetTempPath(), "sbox-agent-bridge", "captures" );

		Directory.CreateDirectory( captureDirectory );

		using var bitmap = new Sandbox.Bitmap( width, height, false );
		camera.RenderToBitmap( bitmap );

		var pixels = bitmap.GetPixels32();
		var luminance = AnalyzeLuminance( pixels );
		var pngBytes = bitmap.ToPng();
		var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{name}-{Guid.NewGuid():N}.png";
		var filePath = Path.Combine( captureDirectory, fileName );

		File.WriteAllBytes( filePath, pngBytes );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Camera captured",
			verified = new
			{
				path = filePath,
				width,
				height,
				byteCount = pngBytes.Length,
				camera = new
				{
					component = HandlerUtil.DescribeComponent( camera ),
					gameObject = HandlerUtil.DescribeGameObject( camera.GameObject ),
					isMainCamera = camera.IsMainCamera,
					fieldOfView = camera.FieldOfView,
					orthographic = camera.Orthographic,
					orthographicHeight = camera.OrthographicHeight,
					enablePostProcessing = camera.EnablePostProcessing
				},
				luminance
			}
		} );
	}

	private static CameraComponent ResolveCamera( Scene scene, JsonElement payload )
	{
		var componentId = HandlerUtil.GetString( payload, "cameraComponentId" );
		if ( !string.IsNullOrWhiteSpace( componentId ) )
		{
			var component = HandlerUtil.RequireComponentById( scene, componentId, "cameraComponentId" );
			if ( component is CameraComponent cameraById )
				return cameraById;

			throw new InvalidOperationException( $"Component '{componentId}' is not a CameraComponent." );
		}

		var gameObjectId = HandlerUtil.GetString( payload, "gameObjectId" );
		if ( !string.IsNullOrWhiteSpace( gameObjectId ) )
		{
			var go = HandlerUtil.RequireGameObjectById( scene, gameObjectId, "gameObjectId" );
			var cameraOnObject = go.Components.Get<CameraComponent>();

			if ( cameraOnObject is not null )
				return cameraOnObject;

			throw new InvalidOperationException( $"GameObject '{go.Name}' does not have a CameraComponent." );
		}

		var cameras = HandlerUtil.WalkSceneObjects( scene )
			.Select( go => go.Components.Get<CameraComponent>() )
			.Where( camera => camera is not null && camera.IsValid && camera.Enabled && camera.GameObject.Enabled )
			.ToArray();

		var mainCamera = cameras.FirstOrDefault( camera => camera!.IsMainCamera );
		var fallbackCamera = mainCamera ?? cameras.FirstOrDefault();

		if ( fallbackCamera is null )
			throw new InvalidOperationException( "No enabled CameraComponent found in the active scene." );

		return fallbackCamera;
	}

	private static object AnalyzeLuminance( Color32[] pixels )
	{
		if ( pixels.Length == 0 )
		{
			return new
			{
				sampleCount = 0,
				average = 0d,
				min = 0d,
				max = 0d,
				darkPixelRatio = 0d,
				brightPixelRatio = 0d
			};
		}

		double total = 0d;
		double min = 1d;
		double max = 0d;
		var darkPixels = 0;
		var brightPixels = 0;

		foreach ( var pixel in pixels )
		{
			var luminance = ((0.2126d * pixel.r) + (0.7152d * pixel.g) + (0.0722d * pixel.b)) / 255d;
			total += luminance;

			if ( luminance < min )
				min = luminance;

			if ( luminance > max )
				max = luminance;

			if ( luminance < 0.08d )
				darkPixels++;

			if ( luminance > 0.85d )
				brightPixels++;
		}

		return new
		{
			sampleCount = pixels.Length,
			average = Math.Round( total / pixels.Length, 4 ),
			min = Math.Round( min, 4 ),
			max = Math.Round( max, 4 ),
			darkPixelRatio = Math.Round( (double)darkPixels / pixels.Length, 4 ),
			brightPixelRatio = Math.Round( (double)brightPixels / pixels.Length, 4 )
		};
	}

	private static int ClampInt( int value, int min, int max )
	{
		if ( value < min )
			return min;

		if ( value > max )
			return max;

		return value;
	}

	private static string SanitizeFileName( string value )
	{
		var chars = (value ?? "camera")
			.Select( ch => char.IsLetterOrDigit( ch ) || ch is '-' or '_' ? ch : '_' )
			.ToArray();

		var result = new string( chars ).Trim( '_' );
		return string.IsNullOrWhiteSpace( result ) ? "camera" : result;
	}
}
