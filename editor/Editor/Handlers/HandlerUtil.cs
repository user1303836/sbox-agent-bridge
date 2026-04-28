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
			isRoot = go.IsRoot,
			isValid = go.IsValid,
			isDestroyed = go.IsDestroyed,
			parent = go.Parent is null ? null : new
			{
				id = go.Parent.Id.ToString(),
				name = go.Parent.Name
			},
			localPosition = ToJson( go.LocalPosition ),
			localRotation = ToJson( go.LocalRotation ),
			localScale = ToJson( go.LocalScale ),
			position = ToJson( go.WorldPosition ),
			rotation = ToJson( go.WorldRotation ),
			scale = ToJson( go.WorldScale ),
			components = go.Components.GetAll().Select( c => c.GetType().Name ).ToArray(),
			componentDetails = go.Components.GetAll().Select( DescribeComponent ).ToArray(),
			childCount = go.Children.Count
		};
	}

	public static object DescribeComponent( Component component )
	{
		var type = component.GetType();

		return new
		{
			id = component.Id.ToString(),
			type = type.Name,
			fullType = type.FullName,
			enabled = component.Enabled,
			active = component.Active
		};
	}

	public static object DescribeSelectionItem( object item )
	{
		if ( item is GameObject go )
		{
			return new
			{
				type = "GameObject",
				gameObject = DescribeGameObject( go )
			};
		}

		if ( item is Component component )
		{
			return new
			{
				type = "Component",
				component = DescribeComponent( component ),
				gameObject = DescribeGameObject( component.GameObject )
			};
		}

		var type = item.GetType();

		return new
		{
			type = type.FullName ?? type.Name,
			value = item.ToString() ?? ""
		};
	}

	public static object ToJson( Vector3 value )
	{
		return new { x = value.x, y = value.y, z = value.z };
	}

	public static object ToJson( Rotation value )
	{
		var angles = value.Angles();

		return new
		{
			x = value.x,
			y = value.y,
			z = value.z,
			w = value.w,
			angles = new
			{
				pitch = angles.pitch,
				yaw = angles.yaw,
				roll = angles.roll
			}
		};
	}

	public static GameObject RequireGameObject( Scene scene, JsonElement payload, string propertyName = "id" )
	{
		var id = GetString( payload, propertyName );
		return RequireGameObjectById( scene, id, propertyName );
	}

	public static GameObject RequireGameObjectById( Scene scene, string id, string propertyName = "id" )
	{
		if ( string.IsNullOrWhiteSpace( id ) )
			throw new InvalidOperationException( $"Missing required payload property '{propertyName}'." );

		if ( !Guid.TryParse( id, out var guid ) )
			throw new InvalidOperationException( $"Payload property '{propertyName}' must be a GameObject GUID." );

		var go = scene.Directory.FindByGuid( guid );

		if ( go is null || !go.IsValid || go.IsDestroyed )
			throw new InvalidOperationException( $"No active GameObject found for id '{id}'." );

		return go;
	}

	public static string GetRequiredString( JsonElement payload, string name )
	{
		var value = GetString( payload, name );

		if ( string.IsNullOrWhiteSpace( value ) )
			throw new InvalidOperationException( $"Missing required payload property '{name}'." );

		return value;
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

	public static bool GetRequiredBool( JsonElement payload, string name )
	{
		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) )
		{
			if ( value.ValueKind == JsonValueKind.True )
				return true;

			if ( value.ValueKind == JsonValueKind.False )
				return false;
		}

		throw new InvalidOperationException( $"Missing required boolean payload property '{name}'." );
	}

	public static string[] GetStringArray( JsonElement payload, string name )
	{
		if ( payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty( name, out var value ) || value.ValueKind != JsonValueKind.Array )
			return Array.Empty<string>();

		return value.EnumerateArray()
			.Where( x => x.ValueKind == JsonValueKind.String )
			.Select( x => x.GetString() ?? "" )
			.Where( x => !string.IsNullOrWhiteSpace( x ) )
			.ToArray();
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

	public static Rotation? GetRotation( JsonElement payload, string name )
	{
		if ( payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty( name, out var value ) || value.ValueKind != JsonValueKind.Object )
			return null;

		var hasPitch = value.TryGetProperty( "pitch", out var pitchElement );
		var hasYaw = value.TryGetProperty( "yaw", out var yawElement );
		var hasRoll = value.TryGetProperty( "roll", out var rollElement );

		if ( hasPitch || hasYaw || hasRoll )
		{
			var pitch = hasPitch && pitchElement.TryGetSingle( out var pitchValue ) ? pitchValue : 0f;
			var yaw = hasYaw && yawElement.TryGetSingle( out var yawValue ) ? yawValue : 0f;
			var roll = hasRoll && rollElement.TryGetSingle( out var rollValue ) ? rollValue : 0f;

			return Rotation.From( pitch, yaw, roll );
		}

		var x = value.TryGetProperty( "x", out var xElement ) && xElement.TryGetSingle( out var xValue ) ? xValue : 0f;
		var y = value.TryGetProperty( "y", out var yElement ) && yElement.TryGetSingle( out var yValue ) ? yValue : 0f;
		var z = value.TryGetProperty( "z", out var zElement ) && zElement.TryGetSingle( out var zValue ) ? zValue : 0f;
		var w = value.TryGetProperty( "w", out var wElement ) && wElement.TryGetSingle( out var wValue ) ? wValue : 1f;

		return new Rotation( x, y, z, w );
	}
}
