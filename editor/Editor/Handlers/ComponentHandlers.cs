using System;
using System.Linq;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class ComponentHandlers
{
	public static BridgeResponse ListTypes( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var includeAbstract = HandlerUtil.GetBool( request.Payload, "includeAbstract", false );
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

		var ordered = types
			.OrderBy( x => x.Group )
			.ThenBy( x => x.Title )
			.ThenBy( x => x.Name )
			.ToArray();

		var results = ordered
			.Take( maxResults )
			.Select( HandlerUtil.DescribeComponentType )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component types listed",
			verified = new
			{
				query,
				includeAbstract,
				maxResults,
				total = ordered.Length,
				count = results.Length,
				results
			}
		} );
	}

	public static BridgeResponse ListOnGameObject( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var components = go.Components.GetAll().Select( HandlerUtil.DescribeComponent ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject components listed",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				count = components.Length,
				components
			}
		} );
	}

	public static BridgeResponse Get( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var component = HandlerUtil.RequireComponent( session.Scene, request.Payload );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component read",
			verified = new
			{
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( component.GameObject )
			}
		} );
	}

	public static BridgeResponse GetProperties( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
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
		var type = HandlerUtil.RequireComponentType( request.Payload );
		var startEnabled = HandlerUtil.GetBool( request.Payload, "startEnabled", true );
		Component component;

		using ( session.UndoScope( "Agent Bridge: Add Component" ).WithComponentCreations().Push() )
		{
			component = go.Components.Create( type, startEnabled );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Component added",
			verified = new
			{
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

		if ( request.Payload.ValueKind != System.Text.Json.JsonValueKind.Object || !request.Payload.TryGetProperty( "value", out var valueElement ) )
			throw new InvalidOperationException( "component.set_property requires a value payload property." );

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

	private static bool Contains( string? value, string query )
	{
		return value?.Contains( query, StringComparison.OrdinalIgnoreCase ) ?? false;
	}
}
