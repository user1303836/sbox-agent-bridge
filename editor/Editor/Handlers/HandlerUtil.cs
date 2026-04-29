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
		var description = Game.TypeLibrary.GetType( type );

		return new
		{
			id = component.Id.ToString(),
			type = type.Name,
			fullType = type.FullName,
			title = description?.Title ?? type.Name,
			description = description?.Description ?? "",
			group = description?.Group ?? "",
			enabled = component.Enabled,
			active = component.Active,
			propertyCount = description?.Properties.Length ?? 0
		};
	}

	public static object DescribeComponentType( TypeDescription type )
	{
		var properties = type.Properties
			.Where( IsReadableProperty )
			.ToArray();

		var inspectorProperties = properties
			.Where( IsInspectorProperty )
			.ToArray();

		return new
		{
			name = type.Name,
			fullName = type.FullName,
			title = type.Title,
			description = type.Description,
			group = type.Group,
			icon = type.Icon,
			isAbstract = type.IsAbstract,
			isGenericType = type.IsGenericType,
			propertyCount = properties.Length,
			inspectorPropertyCount = inspectorProperties.Length
		};
	}

	public static object DescribePropertyMetadata( PropertyDescription property )
	{
		return new
		{
			name = property.Name,
			title = property.Title,
			description = property.Description,
			group = property.Group,
			type = property.PropertyType.Name,
			fullType = property.PropertyType.FullName,
			canRead = property.CanRead,
			canWrite = property.CanWrite,
			readOnly = property.ReadOnly,
			isPublic = property.IsPublic,
			isStatic = property.IsStatic,
			isIndexer = property.IsIndexer,
			isInspectorProperty = IsInspectorProperty( property )
		};
	}

	public static object DescribePropertyValue( Component component, PropertyDescription property )
	{
		try
		{
			return new
			{
				metadata = DescribePropertyMetadata( property ),
				value = DescribeValue( property.GetValue( component ) )
			};
		}
		catch ( Exception ex )
		{
			return new
			{
				metadata = DescribePropertyMetadata( property ),
				error = ex.Message
			};
		}
	}

	public static object DescribeValue( object? value )
	{
		if ( value is null )
			return new { type = "null", value = (object?)null };

		return value switch
		{
			string stringValue => new { type = "string", value = stringValue },
			bool boolValue => new { type = "bool", value = boolValue },
			int intValue => new { type = "int", value = intValue },
			uint uintValue => new { type = "uint", value = uintValue },
			long longValue => new { type = "long", value = longValue },
			ulong ulongValue => new { type = "ulong", value = ulongValue },
			short shortValue => new { type = "short", value = shortValue },
			ushort ushortValue => new { type = "ushort", value = ushortValue },
			byte byteValue => new { type = "byte", value = byteValue },
			sbyte sbyteValue => new { type = "sbyte", value = sbyteValue },
			float floatValue => new { type = "float", value = floatValue },
			double doubleValue => new { type = "double", value = doubleValue },
			decimal decimalValue => new { type = "decimal", value = decimalValue },
			Enum enumValue => new { type = value.GetType().FullName ?? value.GetType().Name, value = enumValue.ToString() },
			Vector2 vector2 => new { type = "Vector2", value = new { x = vector2.x, y = vector2.y } },
			Vector3 vector3 => new { type = "Vector3", value = ToJson( vector3 ) },
			Rotation rotation => new { type = "Rotation", value = ToJson( rotation ) },
			Transform transform => new
			{
				type = "Transform",
				value = new
				{
					position = ToJson( transform.Position ),
					rotation = ToJson( transform.Rotation ),
					scale = ToJson( transform.Scale )
				}
			},
			Color color => new
			{
				type = "Color",
				value = new
				{
					r = color.r,
					g = color.g,
					b = color.b,
					a = color.a,
					hex = color.Hex
				}
			},
			GameObject go => new { type = "GameObject", value = new { id = go.Id.ToString(), name = go.Name } },
			Component component => new { type = "Component", value = new { id = component.Id.ToString(), type = component.GetType().Name, gameObjectId = component.GameObject.Id.ToString() } },
			Type type => new { type = "Type", value = type.FullName ?? type.Name },
			_ => new
			{
				type = value.GetType().FullName ?? value.GetType().Name,
				value = value.ToString() ?? "",
				serialized = false
			}
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

	public static GameObject? GetOptionalGameObject( Scene scene, JsonElement payload, string propertyName = "parentId" )
	{
		var id = GetString( payload, propertyName );

		if ( string.IsNullOrWhiteSpace( id ) )
			return null;

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

	public static Component RequireComponent( Scene scene, JsonElement payload, string propertyName = "id" )
	{
		var id = GetString( payload, propertyName );

		if ( string.IsNullOrWhiteSpace( id ) )
			throw new InvalidOperationException( $"Missing required payload property '{propertyName}'." );

		if ( !Guid.TryParse( id, out var guid ) )
			throw new InvalidOperationException( $"Payload property '{propertyName}' must be a Component GUID." );

		var component = scene.Directory.FindComponentByGuid( guid );

		if ( component is null || !component.IsValid )
			throw new InvalidOperationException( $"No active Component found for id '{id}'." );

		return component;
	}

	public static object DescribeDestroyedGameObject( Scene scene, string id )
	{
		var exists = Guid.TryParse( id, out var guid ) && scene.Directory.FindByGuid( guid ) is { IsValid: true, IsDestroyed: false };

		return new
		{
			id,
			exists,
			destroyed = !exists
		};
	}

	public static string GetRequiredString( JsonElement payload, string name )
	{
		var value = GetString( payload, name );

		if ( string.IsNullOrWhiteSpace( value ) )
			throw new InvalidOperationException( $"Missing required payload property '{name}'." );

		return value;
	}

	public static bool IsReadableProperty( PropertyDescription property )
	{
		return property.CanRead && !property.IsIndexer && !property.IsStatic;
	}

	public static bool IsInspectorProperty( PropertyDescription property )
	{
		return property.HasAttribute( typeof( PropertyAttribute ) );
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
