using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class SceneHandlers
{
	private static readonly JsonSerializerOptions JsonOptions = new()
	{
		PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
		WriteIndented = false
	};

	private static readonly HashSet<string> BatchAllowedActions = new( StringComparer.OrdinalIgnoreCase )
	{
		"editor.context",
		"editor.get_selection",
		"editor.set_selection",
		"editor.save_scene",
		"editor.frame_object",
		"editor.feedback",
		"scene.summary",
		"scene.hierarchy",
		"scene.find",
		"scene.details",
		"gameobject.get",
		"gameobject.create",
		"gameobject.rename",
		"gameobject.set_transform",
		"gameobject.set_enabled",
		"gameobject.duplicate",
		"gameobject.reparent",
		"gameobject.place_asset",
		"component.list_types",
		"component.list_on_gameobject",
		"component.get",
		"component.get_properties",
		"component.add",
		"component.set_enabled",
		"component.set_property",
		"component.validate_property",
		"asset.assign_model",
		"asset.assign_material",
		"asset.set_material_property",
		"asset.get_orientation_override",
		"sound.assign",
		"physics.add_physics",
		"physics.add_collider",
		"physics.add_joint",
		"physics.raycast",
		"prefab.instantiate"
	};

	public static BridgeResponse Summary( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );
		var session = resolution.Session;
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
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
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
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );
		var session = resolution.Session;
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
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
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
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload );
		var session = resolution.Session;
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
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				scene = session.Scene.Name,
				count = results.Length,
				results
			}
		} );
	}

	public static BridgeResponse Details( BridgeRequest request )
	{
		var session = HandlerUtil.RequireTargetSession( request.Payload );
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Scene object details read",
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse Batch( BridgeRequest request )
	{
		HandlerUtil.RequireSession();

		var operationsElement = RequireOperations( request.Payload );
		var stopOnError = HandlerUtil.GetBool( request.Payload, "stopOnError", true );
		var maxOperations = HandlerUtil.GetInt( request.Payload, "maxOperations", 50 );
		var aliases = new Dictionary<string, JsonNode?>( StringComparer.OrdinalIgnoreCase );
		var results = new List<object>();
		var successCount = 0;
		var failureCount = 0;
		var stopped = false;
		var index = 0;

		if ( maxOperations < 1 || maxOperations > 50 )
			throw new InvalidOperationException( "scene.batch maxOperations must be between 1 and 50." );

		if ( operationsElement.GetArrayLength() > maxOperations )
			throw new InvalidOperationException( $"scene.batch received {operationsElement.GetArrayLength()} operations, but maxOperations is {maxOperations}." );

		foreach ( var operationElement in operationsElement.EnumerateArray() )
		{
			var action = "";
			var key = "";

			try
			{
				action = HandlerUtil.GetRequiredString( operationElement, "action" );
				key = HandlerUtil.GetString( operationElement, "key" );

				if ( !BatchAllowedActions.Contains( action ) )
					throw new InvalidOperationException( $"Action '{action}' is not allowed in scene.batch v0." );

				if ( !string.IsNullOrWhiteSpace( key ) && aliases.ContainsKey( key ) )
					throw new InvalidOperationException( $"Duplicate batch key '{key}'." );

				var payload = ResolveOperationPayload( operationElement, aliases );
				var response = CommandDispatcher.Dispatch( new BridgeRequest
				{
					Id = $"{request.Id}:{index}",
					Action = action,
					Payload = payload
				} );

				if ( response.Ok )
				{
					successCount++;

					if ( !string.IsNullOrWhiteSpace( key ) )
						aliases[key] = JsonSerializer.SerializeToNode( response.Result, JsonOptions );

					results.Add( new
					{
						index,
						key,
						action,
						ok = true,
						result = response.Result
					} );
				}
				else
				{
					failureCount++;
					results.Add( BuildBatchFailure( index, key, action, response.Error?.Message ?? "Unknown bridge error", response.Error?.Suggestion ) );

					if ( stopOnError )
					{
						stopped = true;
						break;
					}
				}
			}
			catch ( Exception ex )
			{
				failureCount++;
				results.Add( BuildBatchFailure( index, key, action, ex.Message ) );

				if ( stopOnError )
				{
					stopped = true;
					break;
				}
			}

			index++;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = failureCount == 0 ? "Scene batch completed" : "Scene batch completed with failures",
			verified = new
			{
				completed = failureCount == 0,
				stopOnError,
				stopped,
				requestedCount = operationsElement.GetArrayLength(),
				executedCount = successCount + failureCount,
				successCount,
				failureCount,
				aliases = aliases.Keys.OrderBy( x => x ).ToArray(),
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

	private static JsonElement RequireOperations( JsonElement payload )
	{
		if ( payload.ValueKind == JsonValueKind.Object )
		{
			if ( payload.TryGetProperty( "operations", out var operations ) && operations.ValueKind == JsonValueKind.Array )
				return operations;

			if ( payload.TryGetProperty( "steps", out var steps ) && steps.ValueKind == JsonValueKind.Array )
				return steps;
		}

		throw new InvalidOperationException( "scene.batch requires an operations array." );
	}

	private static JsonElement ResolveOperationPayload( JsonElement operationElement, IReadOnlyDictionary<string, JsonNode?> aliases )
	{
		JsonNode payloadNode = new JsonObject();

		if ( operationElement.ValueKind == JsonValueKind.Object && operationElement.TryGetProperty( "payload", out var payloadElement ) )
		{
			if ( payloadElement.ValueKind != JsonValueKind.Object )
				throw new InvalidOperationException( "Batch operation payload must be an object." );

			payloadNode = JsonNode.Parse( payloadElement.GetRawText() ) ?? new JsonObject();
		}

		var resolved = ResolveReferences( payloadNode, aliases );

		using var document = JsonDocument.Parse( (resolved ?? new JsonObject()).ToJsonString( JsonOptions ) );
		return document.RootElement.Clone();
	}

	private static JsonNode? ResolveReferences( JsonNode? node, IReadOnlyDictionary<string, JsonNode?> aliases )
	{
		if ( node is null )
			return null;

		if ( node is JsonObject obj )
		{
			if ( obj.Count == 1 && obj.TryGetPropertyValue( "$ref", out var refNode ) )
			{
				var reference = refNode?.GetValue<string>() ?? "";
				return CloneNode( ResolveReferencePath( aliases, reference ) );
			}

			var resolved = new JsonObject();

			foreach ( var item in obj )
			{
				resolved[item.Key] = ResolveReferences( item.Value, aliases );
			}

			return resolved;
		}

		if ( node is JsonArray arr )
		{
			var resolved = new JsonArray();

			foreach ( var item in arr )
			{
				resolved.Add( ResolveReferences( item, aliases ) );
			}

			return resolved;
		}

		if ( node is JsonValue value && value.TryGetValue<string>( out var stringValue ) && TryParseStringReference( stringValue, out var referencePath ) )
			return CloneNode( ResolveReferencePath( aliases, referencePath ) );

		return CloneNode( node );
	}

	private static bool TryParseStringReference( string value, out string referencePath )
	{
		referencePath = "";

		if ( value.Length < 2 || value[0] != '$' )
			return false;

		referencePath = value[1..];
		return referencePath.Contains( '.', StringComparison.Ordinal );
	}

	private static JsonNode? ResolveReferencePath( IReadOnlyDictionary<string, JsonNode?> aliases, string reference )
	{
		var parts = reference.Split( '.', StringSplitOptions.RemoveEmptyEntries );

		if ( parts.Length < 2 )
			throw new InvalidOperationException( $"Batch reference '{reference}' must use alias.path syntax, such as root.verified.id." );

		if ( !aliases.TryGetValue( parts[0], out var current ) )
			throw new InvalidOperationException( $"Batch reference '{reference}' uses unknown alias '{parts[0]}'." );

		for ( var i = 1; i < parts.Length; i++ )
		{
			if ( current is JsonObject obj && obj.TryGetPropertyValue( parts[i], out var child ) )
			{
				current = child;
				continue;
			}

			if ( current is JsonArray arr && int.TryParse( parts[i], out var index ) && index >= 0 && index < arr.Count )
			{
				current = arr[index];
				continue;
			}

			throw new InvalidOperationException( $"Batch reference '{reference}' could not resolve segment '{parts[i]}'." );
		}

		return current;
	}

	private static JsonNode? CloneNode( JsonNode? node )
	{
		if ( node is null )
			return null;

		return JsonNode.Parse( node.ToJsonString( JsonOptions ) );
	}

	private static object BuildBatchFailure( int index, string key, string action, string message, string? suggestion = null )
	{
		return new
		{
			index,
			key,
			action,
			ok = false,
			error = new
			{
				message,
				suggestion
			}
		};
	}
}
