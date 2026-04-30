using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
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
			isInspectorProperty = IsInspectorProperty( property ),
			attributes = property.Attributes.Select( x => x.GetType().Name ).ToArray(),
			typeConversionSupported = IsSetPropertyTypeSupported( property.PropertyType ),
			setPropertySupported = property.CanWrite && !property.ReadOnly && !property.IsIndexer && !property.IsStatic && IsSetPropertyTypeSupported( property.PropertyType ),
			schema = DescribePropertySchema( property.PropertyType )
		};
	}

	public static object DescribePropertySchema( Type targetType )
	{
		var nullableType = Nullable.GetUnderlyingType( targetType );
		var effectiveType = nullableType ?? targetType;
		var supported = IsSetPropertyTypeSupported( targetType );

		return new
		{
			kind = GetPropertyKind( effectiveType ),
			nullable = !targetType.IsValueType || nullableType is not null,
			targetType = effectiveType.FullName ?? effectiveType.Name,
			acceptedJson = GetAcceptedJsonShapes( effectiveType ),
			example = GetJsonExample( effectiveType ),
			enumValues = effectiveType.IsEnum ? Enum.GetNames( effectiveType ) : Array.Empty<string>(),
			reference = DescribeReferenceTarget( effectiveType ),
			supported,
			unsupportedReason = supported ? null : GetUnsupportedPropertyReason( effectiveType )
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
			Angles angles => new { type = "Angles", value = new { pitch = angles.pitch, yaw = angles.yaw, roll = angles.roll } },
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
			Resource resource => new { type = value.GetType().FullName ?? value.GetType().Name, value = DescribeResourceReference( resource ) },
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

	public static object? DescribeResourceReference( Resource? resource )
	{
		if ( resource is null )
			return null;

		return new
		{
			type = resource.GetType().FullName ?? resource.GetType().Name,
			path = resource.ResourcePath,
			name = resource.ResourceName,
			id = resource.ResourceId,
			isValid = resource.IsValid
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

	public static TypeDescription RequireComponentType( JsonElement payload, string propertyName = "type" )
	{
		var typeName = GetRequiredString( payload, propertyName );
		var type = FindComponentType( typeName );

		if ( type is null || !type.IsValid )
			throw new InvalidOperationException( $"No Component type found for '{typeName}'." );

		ValidateComponentTypeForCreation( type );

		return type;
	}

	public static TypeDescription? FindComponentType( string typeName )
	{
		var type = Game.TypeLibrary.GetType( typeName, typeof( Component ) )
			?? Game.TypeLibrary.GetType( typeName, true )
			?? Game.TypeLibrary.GetTypes( typeof( Component ) ).FirstOrDefault( x =>
				string.Equals( x.Name, typeName, StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( x.FullName, typeName, StringComparison.OrdinalIgnoreCase ) ||
				string.Equals( x.Title, typeName, StringComparison.OrdinalIgnoreCase )
			);

		if ( type is null || !type.IsValid )
		{
			var runtimeType = FindComponentRuntimeType( typeName );
			if ( runtimeType is not null )
				type = Game.TypeLibrary.GetType( runtimeType );
		}

		return type is { IsValid: true } ? type : null;
	}

	public static void ValidateComponentTypeForCreation( TypeDescription type )
	{
		if ( type.IsAbstract )
			throw new InvalidOperationException( $"Component type '{type.FullName}' is abstract and cannot be added." );

		if ( type.IsGenericType )
			throw new InvalidOperationException( $"Component type '{type.FullName}' is generic and cannot be added directly." );

		if ( !typeof( Component ).IsAssignableFrom( type.TargetType ) )
			throw new InvalidOperationException( $"Type '{type.FullName}' is not a Component." );
	}

	private static Type? FindComponentRuntimeType( string typeName )
	{
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
		{
			foreach ( var type in GetLoadableTypes( assembly ) )
			{
				if ( type.IsAbstract || type.IsGenericType || !typeof( Component ).IsAssignableFrom( type ) )
					continue;

				if ( string.Equals( type.Name, typeName, StringComparison.OrdinalIgnoreCase ) ||
					string.Equals( type.FullName, typeName, StringComparison.OrdinalIgnoreCase ) )
				{
					return type;
				}
			}
		}

		return null;
	}

	private static IEnumerable<Type> GetLoadableTypes( Assembly assembly )
	{
		try
		{
			return assembly.GetTypes();
		}
		catch ( ReflectionTypeLoadException ex )
		{
			return ex.Types.Where( x => x is not null )!;
		}
		catch
		{
			return Array.Empty<Type>();
		}
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

	public static object DescribeDestroyedComponent( Scene scene, string id )
	{
		var exists = Guid.TryParse( id, out var guid ) && scene.Directory.FindComponentByGuid( guid ) is { IsValid: true };

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

	public static PropertyDescription RequireProperty( Component component, JsonElement payload )
	{
		var propertyName = GetRequiredString( payload, "property" );
		var includeAll = GetBool( payload, "includeAll", false );
		var type = Game.TypeLibrary.GetType( component.GetType() );
		var properties = type.Properties.Where( IsReadableProperty );

		if ( !includeAll )
			properties = properties.Where( IsInspectorProperty );

		var property = properties.FirstOrDefault( x =>
			string.Equals( x.Name, propertyName, StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( x.Title, propertyName, StringComparison.OrdinalIgnoreCase )
		);

		if ( property is null )
			throw new InvalidOperationException( $"No readable {(includeAll ? "" : "inspector ")}property '{propertyName}' found on component '{component.GetType().Name}'." );

		if ( !property.CanWrite || property.ReadOnly )
			throw new InvalidOperationException( $"Property '{property.Name}' on component '{component.GetType().Name}' is read-only." );

		return property;
	}

	public static object ValidatePropertyValue( Component component, PropertyDescription property, JsonElement value, Scene scene )
	{
		var converted = ConvertJsonValue( value, property.PropertyType, scene );

		return new
		{
			component = DescribeComponent( component ),
			gameObject = DescribeGameObject( component.GameObject ),
			property = DescribePropertyMetadata( property ),
			current = DescribePropertyValue( component, property ),
			converted = DescribeValue( converted ),
			mutationApplied = false,
			valid = true
		};
	}

	public static object? ConvertJsonValue( JsonElement value, Type targetType, Scene scene )
	{
		var nullableType = Nullable.GetUnderlyingType( targetType );

		if ( nullableType is not null )
		{
			if ( value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined )
				return null;

			targetType = nullableType;
		}

		if ( value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined )
		{
			if ( !targetType.IsValueType )
				return null;

			throw new InvalidOperationException( $"Cannot assign null to non-nullable value type '{targetType.Name}'." );
		}

		if ( targetType == typeof( string ) )
			return value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.ToString();

		if ( targetType == typeof( bool ) )
			return value.ValueKind switch
			{
				JsonValueKind.True => true,
				JsonValueKind.False => false,
				JsonValueKind.String when bool.TryParse( value.GetString(), out var result ) => result,
				_ => throw new InvalidOperationException( "Expected a boolean value." )
			};

		if ( targetType.IsEnum )
			return ConvertEnum( value, targetType );

		if ( targetType == typeof( int ) )
			return GetIntegralValue<int>( value, x => checked((int)x) );

		if ( targetType == typeof( uint ) )
			return GetUnsignedIntegralValue<uint>( value, x => checked((uint)x) );

		if ( targetType == typeof( long ) )
			return GetIntegralValue<long>( value, x => x );

		if ( targetType == typeof( ulong ) )
			return GetUnsignedIntegralValue<ulong>( value, x => x );

		if ( targetType == typeof( short ) )
			return GetIntegralValue<short>( value, x => checked((short)x) );

		if ( targetType == typeof( ushort ) )
			return GetUnsignedIntegralValue<ushort>( value, x => checked((ushort)x) );

		if ( targetType == typeof( byte ) )
			return GetUnsignedIntegralValue<byte>( value, x => checked((byte)x) );

		if ( targetType == typeof( sbyte ) )
			return GetIntegralValue<sbyte>( value, x => checked((sbyte)x) );

		if ( targetType == typeof( float ) )
			return value.GetSingle();

		if ( targetType == typeof( double ) )
			return value.GetDouble();

		if ( targetType == typeof( decimal ) )
			return value.GetDecimal();

		if ( targetType == typeof( Vector2 ) )
			return ConvertVector2( value );

		if ( targetType == typeof( Vector3 ) )
			return ConvertVector3( value );

		if ( targetType == typeof( Rotation ) )
			return ConvertRotation( value );

		if ( targetType == typeof( Angles ) )
			return ConvertAngles( value );

		if ( targetType == typeof( Transform ) )
			return ConvertTransform( value );

		if ( targetType == typeof( Color ) )
			return ConvertColor( value );

		if ( typeof( Resource ).IsAssignableFrom( targetType ) )
			return ConvertResource( value, targetType );

		if ( targetType == typeof( GameObject ) )
			return RequireGameObjectById( scene, GetReferenceId( value ), "value" );

		if ( typeof( Component ).IsAssignableFrom( targetType ) )
		{
			var component = RequireComponentById( scene, GetReferenceId( value ), "value" );

			if ( !targetType.IsAssignableFrom( component.GetType() ) )
				throw new InvalidOperationException( $"Component '{component.Id}' is '{component.GetType().Name}', not assignable to '{targetType.Name}'." );

			return component;
		}

		throw new InvalidOperationException( $"Property type '{targetType.FullName ?? targetType.Name}' is not supported by component.set_property yet." );
	}

	public static bool IsSetPropertyTypeSupported( Type targetType )
	{
		var nullableType = Nullable.GetUnderlyingType( targetType );
		var effectiveType = nullableType ?? targetType;

		return effectiveType == typeof( string ) ||
			effectiveType == typeof( bool ) ||
			effectiveType.IsEnum ||
			effectiveType == typeof( int ) ||
			effectiveType == typeof( uint ) ||
			effectiveType == typeof( long ) ||
			effectiveType == typeof( ulong ) ||
			effectiveType == typeof( short ) ||
			effectiveType == typeof( ushort ) ||
			effectiveType == typeof( byte ) ||
			effectiveType == typeof( sbyte ) ||
			effectiveType == typeof( float ) ||
			effectiveType == typeof( double ) ||
			effectiveType == typeof( decimal ) ||
			effectiveType == typeof( Vector2 ) ||
			effectiveType == typeof( Vector3 ) ||
			effectiveType == typeof( Rotation ) ||
			effectiveType == typeof( Angles ) ||
			effectiveType == typeof( Transform ) ||
			effectiveType == typeof( Color ) ||
			typeof( Resource ).IsAssignableFrom( effectiveType ) ||
			effectiveType == typeof( GameObject ) ||
			typeof( Component ).IsAssignableFrom( effectiveType );
	}

	private static string GetPropertyKind( Type targetType )
	{
		if ( targetType == typeof( string ) )
			return "string";

		if ( targetType == typeof( bool ) )
			return "bool";

		if ( targetType.IsEnum )
			return "enum";

		if ( targetType == typeof( int ) ||
			targetType == typeof( uint ) ||
			targetType == typeof( long ) ||
			targetType == typeof( ulong ) ||
			targetType == typeof( short ) ||
			targetType == typeof( ushort ) ||
			targetType == typeof( byte ) ||
			targetType == typeof( sbyte ) )
			return "integer";

		if ( targetType == typeof( float ) ||
			targetType == typeof( double ) ||
			targetType == typeof( decimal ) )
			return "number";

		if ( targetType == typeof( Vector2 ) )
			return "vector2";

		if ( targetType == typeof( Vector3 ) )
			return "vector3";

		if ( targetType == typeof( Rotation ) )
			return "rotation";

		if ( targetType == typeof( Angles ) )
			return "angles";

		if ( targetType == typeof( Transform ) )
			return "transform";

		if ( targetType == typeof( Color ) )
			return "color";

		if ( typeof( Resource ).IsAssignableFrom( targetType ) )
			return "resourceReference";

		if ( targetType == typeof( GameObject ) )
			return "gameObjectReference";

		if ( typeof( Component ).IsAssignableFrom( targetType ) )
			return "componentReference";

		return "unsupported";
	}

	private static string[] GetAcceptedJsonShapes( Type targetType )
	{
		if ( targetType == typeof( string ) )
			return new[] { "string", "any non-null JSON value converted to string" };

		if ( targetType == typeof( bool ) )
			return new[] { "boolean", "string 'true' or 'false'" };

		if ( targetType.IsEnum )
			return new[] { "string enum name", "integer enum value" };

		if ( GetPropertyKind( targetType ) == "integer" )
			return new[] { "integer", "integer string" };

		if ( GetPropertyKind( targetType ) == "number" )
			return new[] { "number" };

		if ( targetType == typeof( Vector2 ) )
			return new[] { "object { x: number, y: number }" };

		if ( targetType == typeof( Vector3 ) )
			return new[] { "object { x: number, y: number, z: number }" };

		if ( targetType == typeof( Rotation ) )
			return new[] { "object { pitch?: number, yaw?: number, roll?: number }", "object { x: number, y: number, z: number, w: number }" };

		if ( targetType == typeof( Angles ) )
			return new[] { "object { pitch: number, yaw: number, roll: number }" };

		if ( targetType == typeof( Transform ) )
			return new[] { "object { position?: Vector3, rotation?: Rotation, scale?: Vector3 }" };

		if ( targetType == typeof( Color ) )
			return new[] { "string color such as '#336699' or '#336699CC'", "object { r: number, g: number, b: number, a?: number }" };

		if ( typeof( Resource ).IsAssignableFrom( targetType ) )
			return new[] { "string resource path", "object { path: string }", "object { resourcePath: string }" };

		if ( targetType == typeof( GameObject ) )
			return new[] { "string GameObject id", "object { id: string }" };

		if ( typeof( Component ).IsAssignableFrom( targetType ) )
			return new[] { "string Component id", "object { id: string }" };

		return Array.Empty<string>();
	}

	private static object? GetJsonExample( Type targetType )
	{
		if ( targetType == typeof( string ) )
			return "hello";

		if ( targetType == typeof( bool ) )
			return true;

		if ( targetType.IsEnum )
			return Enum.GetNames( targetType ).FirstOrDefault() ?? "";

		if ( GetPropertyKind( targetType ) == "integer" )
			return 1;

		if ( GetPropertyKind( targetType ) == "number" )
			return 1.5;

		if ( targetType == typeof( Vector2 ) )
			return new { x = 1, y = 2 };

		if ( targetType == typeof( Vector3 ) )
			return new { x = 1, y = 2, z = 3 };

		if ( targetType == typeof( Rotation ) )
			return new { pitch = 0, yaw = 90, roll = 0 };

		if ( targetType == typeof( Angles ) )
			return new { pitch = 0, yaw = 90, roll = 0 };

		if ( targetType == typeof( Transform ) )
		{
			return new
			{
				position = new { x = 0, y = 0, z = 0 },
				rotation = new { pitch = 0, yaw = 0, roll = 0 },
				scale = new { x = 1, y = 1, z = 1 }
			};
		}

		if ( targetType == typeof( Color ) )
			return new { r = 1, g = 1, b = 1, a = 1 };

		if ( typeof( Resource ).IsAssignableFrom( targetType ) )
			return GetResourceExample( targetType );

		if ( targetType == typeof( GameObject ) )
			return "gameobject-guid";

		if ( typeof( Component ).IsAssignableFrom( targetType ) )
			return "component-guid";

		return null;
	}

	private static object? DescribeReferenceTarget( Type targetType )
	{
		if ( targetType == typeof( GameObject ) )
		{
			return new
			{
				kind = "GameObject",
				type = targetType.FullName ?? targetType.Name
			};
		}

		if ( typeof( Component ).IsAssignableFrom( targetType ) )
		{
			return new
			{
				kind = "Component",
				type = targetType.FullName ?? targetType.Name
			};
		}

		if ( typeof( Resource ).IsAssignableFrom( targetType ) )
		{
			return new
			{
				kind = "Resource",
				type = targetType.FullName ?? targetType.Name,
				pathProperty = "ResourcePath"
			};
		}

		return null;
	}

	private static string GetUnsupportedPropertyReason( Type targetType )
	{
		if ( targetType.IsArray )
			return "Array properties are not supported by component.set_property yet.";

		if ( typeof( System.Collections.IEnumerable ).IsAssignableFrom( targetType ) && targetType != typeof( string ) )
			return "Collection properties are not supported by component.set_property yet.";

		return $"Property type '{targetType.FullName ?? targetType.Name}' is not supported by component.set_property yet.";
	}

	public static Component RequireComponentById( Scene scene, string id, string propertyName = "id" )
	{
		if ( string.IsNullOrWhiteSpace( id ) )
			throw new InvalidOperationException( $"Missing required payload property '{propertyName}'." );

		if ( !Guid.TryParse( id, out var guid ) )
			throw new InvalidOperationException( $"Payload property '{propertyName}' must be a Component GUID." );

		var component = scene.Directory.FindComponentByGuid( guid );

		if ( component is null || !component.IsValid )
			throw new InvalidOperationException( $"No active Component found for id '{id}'." );

		return component;
	}

	private static object ConvertEnum( JsonElement value, Type targetType )
	{
		if ( value.ValueKind == JsonValueKind.String )
			return Enum.Parse( targetType, value.GetString() ?? "", true );

		if ( value.ValueKind == JsonValueKind.Number && value.TryGetInt64( out var intValue ) )
			return Enum.ToObject( targetType, intValue );

		throw new InvalidOperationException( $"Expected enum name or numeric value for '{targetType.Name}'." );
	}

	private static T GetIntegralValue<T>( JsonElement value, Func<long, T> convert )
	{
		if ( value.ValueKind == JsonValueKind.Number && value.TryGetInt64( out var number ) )
			return convert( number );

		if ( value.ValueKind == JsonValueKind.String && long.TryParse( value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) )
			return convert( parsed );

		throw new InvalidOperationException( "Expected an integer value." );
	}

	private static T GetUnsignedIntegralValue<T>( JsonElement value, Func<ulong, T> convert )
	{
		if ( value.ValueKind == JsonValueKind.Number && value.TryGetUInt64( out var number ) )
			return convert( number );

		if ( value.ValueKind == JsonValueKind.String && ulong.TryParse( value.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed ) )
			return convert( parsed );

		throw new InvalidOperationException( "Expected an unsigned integer value." );
	}

	private static string GetReferenceId( JsonElement value )
	{
		if ( value.ValueKind == JsonValueKind.String )
			return value.GetString() ?? "";

		if ( value.ValueKind == JsonValueKind.Object && value.TryGetProperty( "id", out var idElement ) && idElement.ValueKind == JsonValueKind.String )
			return idElement.GetString() ?? "";

		throw new InvalidOperationException( "Expected a reference id string or an object with an id property." );
	}

	private static string GetResourcePath( JsonElement value )
	{
		if ( value.ValueKind == JsonValueKind.String )
			return value.GetString() ?? "";

		if ( value.ValueKind == JsonValueKind.Object )
		{
			if ( value.TryGetProperty( "path", out var pathElement ) && pathElement.ValueKind == JsonValueKind.String )
				return pathElement.GetString() ?? "";

			if ( value.TryGetProperty( "resourcePath", out var resourcePathElement ) && resourcePathElement.ValueKind == JsonValueKind.String )
				return resourcePathElement.GetString() ?? "";
		}

		throw new InvalidOperationException( "Expected a resource path string or an object with a path/resourcePath property." );
	}

	private static object ConvertResource( JsonElement value, Type targetType )
	{
		var path = GetResourcePath( value );

		if ( string.IsNullOrWhiteSpace( path ) )
			throw new InvalidOperationException( "Resource path cannot be empty." );

		var resource = LoadResource( path, targetType );

		if ( resource is null )
			throw new InvalidOperationException( $"Could not load resource '{path}' as '{targetType.Name}'." );

		if ( !targetType.IsAssignableFrom( resource.GetType() ) )
			throw new InvalidOperationException( $"Resource '{path}' loaded as '{resource.GetType().Name}', not assignable to '{targetType.Name}'." );

		if ( !resource.IsValid )
			throw new InvalidOperationException( $"Resource '{path}' loaded as '{targetType.Name}' but is not valid." );

		return resource;
	}

	private static Resource? LoadResource( string path, Type targetType )
	{
		if ( targetType == typeof( Model ) )
			return Model.Load( path );

		if ( targetType == typeof( Material ) )
			return Material.Load( path );

		if ( targetType == typeof( Texture ) )
			return Texture.Load( path, true );

		var method = typeof( ResourceLibrary ).GetMethods()
			.FirstOrDefault( x =>
			{
				if ( x.Name != "Get" || !x.IsGenericMethodDefinition )
					return false;

				var parameters = x.GetParameters();
				return parameters.Length == 1 && parameters[0].ParameterType == typeof( string );
			} );

		if ( method is null )
			throw new InvalidOperationException( "Could not find ResourceLibrary.Get<T>(string)." );

		return method.MakeGenericMethod( targetType ).Invoke( null, new object[] { path } ) as Resource;
	}

	private static object GetResourceExample( Type targetType )
	{
		if ( targetType == typeof( Model ) )
			return "models/citizen/citizen.vmdl";

		if ( targetType == typeof( Material ) )
			return "materials/dev/reflectivity_30.vmat";

		if ( targetType == typeof( Texture ) )
			return "textures/cubemaps/default2.vtex";

		if ( targetType == typeof( SoundEvent ) )
			return "sounds/ui/buttonclick.sound";

		return "path/to/resource";
	}

	private static Vector2 ConvertVector2( JsonElement value )
	{
		if ( value.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( "Expected a Vector2 object with x and y properties." );

		var x = value.TryGetProperty( "x", out var xElement ) && xElement.TryGetSingle( out var xValue ) ? xValue : 0f;
		var y = value.TryGetProperty( "y", out var yElement ) && yElement.TryGetSingle( out var yValue ) ? yValue : 0f;

		return new Vector2( x, y );
	}

	private static Vector3 ConvertVector3( JsonElement value )
	{
		if ( value.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( "Expected a Vector3 object with x, y, and z properties." );

		var x = value.TryGetProperty( "x", out var xElement ) && xElement.TryGetSingle( out var xValue ) ? xValue : 0f;
		var y = value.TryGetProperty( "y", out var yElement ) && yElement.TryGetSingle( out var yValue ) ? yValue : 0f;
		var z = value.TryGetProperty( "z", out var zElement ) && zElement.TryGetSingle( out var zValue ) ? zValue : 0f;

		return new Vector3( x, y, z );
	}

	private static Rotation ConvertRotation( JsonElement value )
	{
		if ( value.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( "Expected a Rotation object." );

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

	private static Angles ConvertAngles( JsonElement value )
	{
		if ( value.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( "Expected an Angles object with pitch, yaw, and roll properties." );

		var pitch = value.TryGetProperty( "pitch", out var pitchElement ) && pitchElement.TryGetSingle( out var pitchValue ) ? pitchValue : 0f;
		var yaw = value.TryGetProperty( "yaw", out var yawElement ) && yawElement.TryGetSingle( out var yawValue ) ? yawValue : 0f;
		var roll = value.TryGetProperty( "roll", out var rollElement ) && rollElement.TryGetSingle( out var rollValue ) ? rollValue : 0f;

		return new Angles( pitch, yaw, roll );
	}

	private static Transform ConvertTransform( JsonElement value )
	{
		if ( value.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( "Expected a Transform object." );

		var position = value.TryGetProperty( "position", out var positionElement ) ? ConvertVector3( positionElement ) : Vector3.Zero;
		var rotation = value.TryGetProperty( "rotation", out var rotationElement ) ? ConvertRotation( rotationElement ) : Rotation.Identity;
		var scale = value.TryGetProperty( "scale", out var scaleElement ) ? ConvertVector3( scaleElement ) : new Vector3( 1f, 1f, 1f );

		return new Transform( position, rotation, scale );
	}

	private static Color ConvertColor( JsonElement value )
	{
		if ( value.ValueKind == JsonValueKind.String )
		{
			var parsed = Color.Parse( value.GetString() ?? "" );

			if ( parsed.HasValue )
				return parsed.Value;

			throw new InvalidOperationException( "Could not parse color string." );
		}

		if ( value.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( "Expected a Color string or object with r, g, b, and optional a properties." );

		var r = value.TryGetProperty( "r", out var rElement ) && rElement.TryGetSingle( out var rValue ) ? rValue : 1f;
		var g = value.TryGetProperty( "g", out var gElement ) && gElement.TryGetSingle( out var gValue ) ? gValue : 1f;
		var b = value.TryGetProperty( "b", out var bElement ) && bElement.TryGetSingle( out var bValue ) ? bValue : 1f;
		var a = value.TryGetProperty( "a", out var aElement ) && aElement.TryGetSingle( out var aValue ) ? aValue : 1f;

		return new Color( r, g, b, a );
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

	public static float GetFloat( JsonElement payload, string name, float fallback )
	{
		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.TryGetSingle( out var result ) )
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

		if ( !value.TryGetProperty( "x", out var xElement ) || !xElement.TryGetSingle( out var xValue ) ||
			!value.TryGetProperty( "y", out var yElement ) || !yElement.TryGetSingle( out var yValue ) ||
			!value.TryGetProperty( "z", out var zElement ) || !zElement.TryGetSingle( out var zValue ) )
		{
			throw new InvalidOperationException( $"Payload property '{name}' must include numeric x, y, and z fields." );
		}

		return new Vector3( xValue, yValue, zValue );
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
