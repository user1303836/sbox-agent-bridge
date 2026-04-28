using System.Linq;
using System.Text.Json;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class SceneHandlers
{
	public static BridgeResponse Summary( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var objects = HandlerUtil.WalkSceneObjects( session.Scene ).ToArray();
		var components = objects.SelectMany( x => x.Components.GetAll() ).ToArray();

		var componentCounts = components
			.GroupBy( x => x.GetType().Name )
			.OrderByDescending( x => x.Count() )
			.ThenBy( x => x.Key )
			.Select( x => new { type = x.Key, count = x.Count() } )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Scene summary read",
			verified = new
			{
				scene = session.Scene.Name,
				rootCount = session.Scene.Children.Count,
				objectCount = objects.Length,
				enabledObjectCount = objects.Count( x => x.Enabled ),
				disabledObjectCount = objects.Count( x => !x.Enabled ),
				componentCount = components.Length,
				componentCounts
			}
		} );
	}

	public static BridgeResponse Hierarchy( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var includeDisabled = HandlerUtil.GetBool( request.Payload, "includeDisabled", true );
		var maxDepth = HandlerUtil.GetInt( request.Payload, "maxDepth", 8 );
		var maxNodes = HandlerUtil.GetInt( request.Payload, "maxNodes", 200 );

		var nodes = session.Scene.Children
			.SelectMany( x => BuildNode( x, includeDisabled, maxDepth, 0 ) )
			.Take( maxNodes )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Scene hierarchy read",
			verified = new
			{
				scene = session.Scene.Name,
				includeDisabled,
				maxDepth,
				maxNodes,
				nodes
			}
		} );
	}

	public static BridgeResponse Find( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var nameContains = HandlerUtil.GetString( request.Payload, "nameContains" );
		var componentContains = HandlerUtil.GetString( request.Payload, "componentContains" );
		var includeDisabled = HandlerUtil.GetBool( request.Payload, "includeDisabled", true );
		var maxResults = HandlerUtil.GetInt( request.Payload, "maxResults", 50 );

		var query = HandlerUtil.WalkSceneObjects( session.Scene );

		if ( !includeDisabled )
			query = query.Where( x => x.Enabled );

		if ( !string.IsNullOrWhiteSpace( nameContains ) )
			query = query.Where( x => x.Name.Contains( nameContains, System.StringComparison.OrdinalIgnoreCase ) );

		if ( !string.IsNullOrWhiteSpace( componentContains ) )
		{
			query = query.Where( x => x.Components.GetAll().Any( c => c.GetType().Name.Contains( componentContains, System.StringComparison.OrdinalIgnoreCase ) ) );
		}

		var results = query.Take( maxResults ).Select( HandlerUtil.DescribeGameObject ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Scene search complete",
			verified = new
			{
				scene = session.Scene.Name,
				count = results.Length,
				results
			}
		} );
	}

	private static System.Collections.Generic.IEnumerable<object> BuildNode( GameObject go, bool includeDisabled, int maxDepth, int depth )
	{
		if ( !includeDisabled && !go.Enabled )
			yield break;

		yield return new
		{
			id = go.Id.ToString(),
			name = go.Name,
			depth,
			enabled = go.Enabled,
			componentCount = go.Components.Count,
			childCount = go.Children.Count
		};

		if ( depth >= maxDepth )
			yield break;

		foreach ( var child in go.Children )
		{
			foreach ( var item in BuildNode( child, includeDisabled, maxDepth, depth + 1 ) )
			{
				yield return item;
			}
		}
	}
}
