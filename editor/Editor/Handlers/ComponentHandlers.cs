using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class ComponentHandlers
{
	public static BridgeResponse ListTypes( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var includeAbstract = HandlerUtil.GetBool( request.Payload, "includeAbstract", false );
		var includeRuntimeAssemblies = HandlerUtil.GetBool( request.Payload, "includeRuntimeAssemblies", true );
		var maxResults = HandlerUtil.GetInt( request.Payload, "maxResults", 100 );

		var types = Game.TypeLibrary.GetTypes( typeof( Component ) )
			.Where( x => x.IsValid )
			.Where( x => includeAbstract || !x.IsAbstract )
			.Where( x => !x.IsGenericType );

		if ( !string.IsNullOrWhiteSpace( query ) )
		{
			types = types.Where( x =>
				Contains( x.Name, query ) ||
				Contains( x.FullName, query ) ||
				Contains( x.Title, query ) ||
				Contains( x.Group, query )
			);
		}

		var orderedTypeLibraryTypes = types
			.OrderBy( x => x.Group )
			.ThenBy( x => x.Title )
			.ThenBy( x => x.Name )
			.ToArray();

		var knownFullNames = new HashSet<string>(
			orderedTypeLibraryTypes.Select( x => x.FullName ).Where( x => !string.IsNullOrWhiteSpace( x ) ),
			StringComparer.OrdinalIgnoreCase
		);

		var runtimeTypes = includeRuntimeAssemblies
			? EnumerateRuntimeComponentTypes()
				.Where( x => includeAbstract || !x.IsAbstract )
				.Where( x => !x.IsGenericType )
				.Where( x => !knownFullNames.Contains( x.FullName ?? "" ) )
				.Where( x => string.IsNullOrWhiteSpace( query ) || Contains( x.Name, query ) || Contains( x.FullName, query ) )
				.OrderBy( x => x.Namespace )
				.ThenBy( x => x.Name )
				.ToArray()
			: Array.Empty<Type>();

		var allResults = orderedTypeLibraryTypes
			.Select( x => DescribeComponentType( x, "typeLibrary" ) )
			.Concat( runtimeTypes.Select( x => DescribeRuntimeComponentType( x, "runtimeAssembly" ) ) )
			.ToArray();

		var results = allResults
			.Take( maxResults )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component types listed",
			verified = new
			{
				query,
				includeAbstract,
				includeRuntimeAssemblies,
				maxResults,
				typeLibraryTotal = orderedTypeLibraryTypes.Length,
				runtimeAssemblyTotal = runtimeTypes.Length,
				total = allResults.Length,
				count = results.Length,
				results
			}
		} );
	}

	private static object DescribeComponentType( TypeDescription type, string source )
	{
		return new
		{
			source,
			name = type.Name,
			fullName = type.FullName,
			title = type.Title,
			description = type.Description,
			group = type.Group,
			icon = type.Icon,
			isAbstract = type.IsAbstract,
			isGenericType = type.IsGenericType,
			propertyCount = type.Properties.Count( HandlerUtil.IsReadableProperty ),
			inspectorPropertyCount = type.Properties.Count( x => HandlerUtil.IsReadableProperty( x ) && HandlerUtil.IsInspectorProperty( x ) )
		};
	}

	private static object DescribeRuntimeComponentType( Type type, string source )
	{
		var description = Game.TypeLibrary.GetType( type );

		return new
		{
			source,
			name = type.Name,
			fullName = type.FullName ?? type.Name,
			title = description?.Title ?? type.Name,
			description = description?.Description ?? "",
			group = description?.Group ?? "",
			icon = description?.Icon ?? "",
			isAbstract = type.IsAbstract,
			isGenericType = type.IsGenericType,
			propertyCount = description?.Properties.Count( HandlerUtil.IsReadableProperty ) ?? 0,
			inspectorPropertyCount = description?.Properties.Count( x => HandlerUtil.IsReadableProperty( x ) && HandlerUtil.IsInspectorProperty( x ) ) ?? 0
		};
	}

	private static IEnumerable<Type> EnumerateRuntimeComponentTypes()
	{
		foreach ( var assembly in AppDomain.CurrentDomain.GetAssemblies() )
		{
			Type[] types;

			try
			{
				types = assembly.GetTypes();
			}
			catch ( ReflectionTypeLoadException ex )
			{
				types = ex.Types.Where( x => x is not null ).Cast<Type>().ToArray();
			}
			catch
			{
				continue;
			}

			foreach ( var type in types )
			{
				if ( typeof( Component ).IsAssignableFrom( type ) )
					yield return type;
			}
		}
	}

	public static BridgeResponse ListOnGameObject( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );
		var session = resolution.Session;
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var components = go.Components.GetAll().Select( HandlerUtil.DescribeComponent ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject components listed",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				gameObject = HandlerUtil.DescribeGameObject( go ),
				count = components.Length,
				components
			}
		} );
	}

	public static BridgeResponse Get( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );
		var session = resolution.Session;
		var component = HandlerUtil.RequireComponent( session.Scene, request.Payload );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component read",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( component.GameObject )
			}
		} );
	}

	public static BridgeResponse GetProperties( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );
		var session = resolution.Session;
		var component = HandlerUtil.RequireComponent( session.Scene, request.Payload );
		var includeAll = HandlerUtil.GetBool( request.Payload, "includeAll", false );
		var maxProperties = HandlerUtil.GetInt( request.Payload, "maxProperties", 100 );
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var type = Game.TypeLibrary.GetType( component.GetType() );

		var properties = type.Properties
			.Where( HandlerUtil.IsReadableProperty );

		if ( !includeAll )
			properties = properties.Where( HandlerUtil.IsInspectorProperty );

		if ( !string.IsNullOrWhiteSpace( query ) )
		{
			properties = properties.Where( x =>
				Contains( x.Name, query ) ||
				Contains( x.Title, query ) ||
				Contains( x.Group, query ) ||
				Contains( x.PropertyType.Name, query ) ||
				Contains( x.PropertyType.FullName, query )
			);
		}

		var ordered = properties
			.OrderBy( x => x.Group )
			.ThenBy( x => x.Order )
			.ThenBy( x => x.Name )
			.ToArray();

		var results = ordered
			.Take( maxProperties )
			.Select( x => HandlerUtil.DescribePropertyValue( component, x ) )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component properties read",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( component.GameObject ),
				includeAll,
				query,
				maxProperties,
				total = ordered.Length,
				count = results.Length,
				properties = results
			}
		} );
	}

	public static BridgeResponse Add( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var typeName = HandlerUtil.GetRequiredString( request.Payload, "type" );
		var type = HandlerUtil.FindComponentType( typeName );
		var startEnabled = HandlerUtil.GetBool( request.Payload, "startEnabled", true );
		Component component;
		string creationMode;

		using ( session.UndoScope( "Agent Bridge: Add Component" ).WithComponentCreations().Push() )
		{
			if ( type is not null )
			{
				HandlerUtil.ValidateComponentTypeForCreation( type );
				component = go.Components.Create( type, startEnabled );
				creationMode = "typeLibrary";
			}
			else
			{
				component = AddLocalComponentBySerializedProbe( go, typeName, startEnabled );
				creationMode = "serializedProbe";
			}
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component added",
			verified = new
			{
				requestedType = typeName,
				creationMode,
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( go )
			}
		} );
	}

	public static BridgeResponse Remove( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var component = HandlerUtil.RequireComponent( session.Scene, request.Payload );
		var id = component.Id.ToString();
		var previous = new
		{
			component = HandlerUtil.DescribeComponent( component ),
			gameObject = HandlerUtil.DescribeGameObject( component.GameObject )
		};

		using ( session.UndoScope( "Agent Bridge: Remove Component" ).WithComponentDestructions( component ).Push() )
		{
			component.Destroy();
		}

		session.Scene.ProcessDeletes();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component removed",
			previous,
			verified = HandlerUtil.DescribeDestroyedComponent( session.Scene, id )
		} );
	}

	public static BridgeResponse SetEnabled( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var component = HandlerUtil.RequireComponent( session.Scene, request.Payload );
		var enabled = HandlerUtil.GetRequiredBool( request.Payload, "enabled" );
		var previous = HandlerUtil.DescribeComponent( component );

		using ( session.UndoScope( "Agent Bridge: Set Component Enabled" ).WithComponentChanges( component ).Push() )
		{
			component.Enabled = enabled;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component enabled state set",
			previous,
			verified = new
			{
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( component.GameObject )
			}
		} );
	}

	public static BridgeResponse SetProperty( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var component = HandlerUtil.RequireComponent( session.Scene, request.Payload );
		var property = HandlerUtil.RequireProperty( component, request.Payload );
		var valueElement = RequireValueElement( request.Payload, "component.set_property" );
		var dryRun = HandlerUtil.GetBool( request.Payload, "dryRun", false );

		if ( dryRun )
			return BuildValidationResponse( request, session, component, property, valueElement, "Component property value validated" );

		var previous = HandlerUtil.DescribePropertyValue( component, property );
		var converted = HandlerUtil.ConvertJsonValue( valueElement, property.PropertyType, session.Scene );

		using ( session.UndoScope( "Agent Bridge: Set Component Property" ).WithComponentChanges( component ).Push() )
		{
			property.SetValue( component, converted );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component property set",
			previous,
			verified = new
			{
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( component.GameObject ),
				property = HandlerUtil.DescribePropertyValue( component, property )
			}
		} );
	}

	public static BridgeResponse ValidateProperty( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var component = HandlerUtil.RequireComponent( session.Scene, request.Payload );
		var property = HandlerUtil.RequireProperty( component, request.Payload );
		var valueElement = RequireValueElement( request.Payload, "component.validate_property" );

		return BuildValidationResponse( request, session, component, property, valueElement, "Component property value validated" );
	}

	private static BridgeResponse BuildValidationResponse( BridgeRequest request, SceneEditorSession session, Component component, PropertyDescription property, JsonElement valueElement, string message )
	{
		return BridgeResponse.Success( request.Id, new
		{
			message,
			verified = HandlerUtil.ValidatePropertyValue( component, property, valueElement, session.Scene )
		} );
	}

	private static JsonElement RequireValueElement( JsonElement payload, string action )
	{
		if ( payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty( "value", out var valueElement ) )
			throw new InvalidOperationException( $"{action} requires a value payload property." );

		return valueElement;
	}

	private static Component AddLocalComponentBySerializedProbe( GameObject go, string typeName, bool startEnabled )
	{
		var runtimeType = ResolveLocalComponentRuntimeTypeBySerializedProbe( go.Scene, typeName );
		var method = GetGenericAddComponentMethod().MakeGenericMethod( runtimeType );

		try
		{
			return (Component)method.Invoke( go, new object[] { startEnabled } )!;
		}
		catch ( TargetInvocationException ex ) when ( ex.InnerException is not null )
		{
			throw new InvalidOperationException( $"Resolved local Component type '{runtimeType.FullName}', but AddComponent failed: {ex.InnerException.Message}", ex.InnerException );
		}
	}

	private static MethodInfo GetGenericAddComponentMethod()
	{
		var method = typeof( GameObject ).GetMethods()
			.FirstOrDefault( x =>
				x.Name == nameof( GameObject.AddComponent ) &&
				x.IsGenericMethodDefinition &&
				x.GetParameters().Length == 1 &&
				x.GetParameters()[0].ParameterType == typeof( bool ) );

		return method ?? throw new InvalidOperationException( "Could not find GameObject.AddComponent<T>(bool) for local component creation." );
	}

	private static Type ResolveLocalComponentRuntimeTypeBySerializedProbe( Scene scene, string typeName )
	{
		var componentId = Guid.NewGuid();
		var probe = scene.CreateObject( true );
		probe.Name = "Agent Bridge Component Type Probe";
		probe.Flags |= GameObjectFlags.Hidden | GameObjectFlags.NotSaved;
		var stage = "create probe";

		try
		{
			stage = "build probe json";
			var node = new JsonObject();
			var components = new JsonArray();
			node["Components"] = components;

			components.Add( new JsonObject
			{
				["__type"] = typeName,
				["__guid"] = JsonValue.Create( componentId ),
				["__enabled"] = false,
				["Flags"] = 0L
			} );

			stage = "deserialize probe";
			probe.Deserialize( node, new GameObject.DeserializeOptions() );

			stage = "read probe component";
			var component = probe.Components.GetAll().FirstOrDefault( x => x.Id == componentId )
				?? probe.Components.GetAll().FirstOrDefault( x => ComponentTypeMatches( x, typeName ) );

			if ( component is null || !component.IsValid )
				throw new InvalidOperationException( $"No Component type found for '{typeName}' through TypeLibrary, and serialized probe did not produce a live component." );

			var runtimeType = component.GetType();
			if ( runtimeType == typeof( MissingComponent ) || !typeof( Component ).IsAssignableFrom( runtimeType ) || !ComponentTypeMatches( component, typeName ) )
				throw new InvalidOperationException( $"No Component type found for '{typeName}' through TypeLibrary. Serialized probe produced '{runtimeType.FullName}', which is not a usable matching component type." );

			return runtimeType;
		}
		catch ( Exception ex ) when ( ex is not InvalidOperationException )
		{
			throw new InvalidOperationException( $"No Component type found for '{typeName}' through TypeLibrary, and serialized probe failed during {stage}: {ex.Message}", ex );
		}
		finally
		{
			if ( probe.IsValid && !probe.IsDestroyed )
				probe.DestroyImmediate();
		}
	}

	private static bool ComponentTypeMatches( Component component, string typeName )
	{
		var type = component.GetType();
		return string.Equals( type.Name, typeName, StringComparison.OrdinalIgnoreCase )
			|| string.Equals( type.FullName, typeName, StringComparison.OrdinalIgnoreCase );
	}

	private static bool Contains( string? value, string query )
	{
		return value?.Contains( query, StringComparison.OrdinalIgnoreCase ) ?? false;
	}
}
