using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class HandlerUtil
{
	public static SceneEditorSession? ActiveSession => SceneEditorSession.Active;

	public static SceneEditorSession RequireSession()
	{
		return ActiveSession ?? throw new InvalidOperationException( "No active editor scene session." );
	}

	public static IEnumerable<GameObject> WalkSceneObjects( Scene scene )
	{
		foreach ( var child in scene.Children )
		{
			foreach ( var item in WalkSubtree( child ) )
			{
				yield return item;
			}
		}
	}

	public static IEnumerable<GameObject> WalkSubtree( GameObject root )
	{
		yield return root;

		foreach ( var child in root.Children )
		{
			foreach ( var item in WalkSubtree( child ) )
			{
				yield return item;
			}
		}
	}

	public static object DescribeGameObject( GameObject go )
	{
		return new
		{
			id = go.Id.ToString(),
			name = go.Name,
			enabled = go.Enabled,
			active = go.Active,
			position = ToJson( go.WorldPosition ),
			components = go.Components.GetAll().Select( c => c.GetType().Name ).ToArray(),
			childCount = go.Children.Count
		};
	}

	public static object ToJson( Vector3 value )
	{
		return new { x = value.x, y = value.y, z = value.z };
	}

	public static string GetString( JsonElement payload, string name, string fallback = "" )
	{
		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.ValueKind == JsonValueKind.String )
			return value.GetString() ?? fallback;

		return fallback;
	}

	public static int GetInt( JsonElement payload, string name, int fallback )
	{
		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.TryGetInt32( out var result ) )
			return result;

		return fallback;
	}

	public static bool GetBool( JsonElement payload, string name, bool fallback )
	{
		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) )
		{
			if ( value.ValueKind == JsonValueKind.True )
				return true;

			if ( value.ValueKind == JsonValueKind.False )
				return false;
		}

		return fallback;
	}

	public static Vector3? GetVector3( JsonElement payload, string name )
	{
		if ( payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty( name, out var value ) || value.ValueKind != JsonValueKind.Object )
			return null;

		var x = value.TryGetProperty( "x", out var xElement ) && xElement.TryGetSingle( out var xValue ) ? xValue : 0f;
		var y = value.TryGetProperty( "y", out var yElement ) && yElement.TryGetSingle( out var yValue ) ? yValue : 0f;
		var z = value.TryGetProperty( "z", out var zElement ) && zElement.TryGetSingle( out var zValue ) ? zValue : 0f;

		return new Vector3( x, y, z );
	}
}
