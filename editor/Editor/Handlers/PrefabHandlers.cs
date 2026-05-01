using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class PrefabHandlers
{
	public static BridgeResponse Create( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var source = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var relativePath = NormalizePrefabPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var absolutePath = ResolveAssetPath( EnsureExtension( relativePath, ".prefab" ) );
		var overwrite = HandlerUtil.GetBool( request.Payload, "overwrite", false );
		var showInMenu = HandlerUtil.GetBool( request.Payload, "showInMenu", false );
		var menuPath = HandlerUtil.GetString( request.Payload, "menuPath" );
		var menuIcon = HandlerUtil.GetString( request.Payload, "menuIcon" );
		var bindSource = HandlerUtil.GetBool( request.Payload, "bindSource", false );

		if ( File.Exists( absolutePath ) && !overwrite )
			throw new InvalidOperationException( $"Prefab '{relativePath}' already exists. Pass overwrite:true to replace it." );

		Directory.CreateDirectory( Path.GetDirectoryName( absolutePath ) ?? Project.Current.GetAssetsPath() );

		var asset = File.Exists( absolutePath )
			? AssetSystem.FindByPath( relativePath ) ?? AssetSystem.RegisterFile( absolutePath )
			: AssetSystem.CreateResource( "prefab", absolutePath );

		if ( asset is null )
			throw new InvalidOperationException( $"Could not create or register prefab asset '{relativePath}'." );

		var prefab = new PrefabFile
		{
			RootObject = source.Serialize( new GameObject.SerializeOptions { Cloning = true } ),
			ShowInMenu = showInMenu,
			MenuPath = menuPath,
			MenuIcon = menuIcon
		};

		if ( !asset.SaveToDisk( prefab ) )
			throw new InvalidOperationException( $"Prefab '{relativePath}' could not be saved to disk." );

		asset.Compile( true );

		if ( bindSource )
		{
			using ( session.UndoScope( "Agent Bridge: Bind Prefab Source" ).WithGameObjectChanges( source, GameObjectUndoFlags.Properties ).Push() )
			{
				source.SetPrefabSource( relativePath );
			}
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Prefab created",
			verified = new
			{
				asset = DescribePrefabAsset( asset ),
				prefab = DescribePrefab( prefab ),
				source = HandlerUtil.DescribeGameObject( source ),
				bindSource
			}
		} );
	}

	public static BridgeResponse List( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var maxResults = HandlerUtil.GetInt( request.Payload, "maxResults", 50 );
		var results = AssetSystem.All
			.Where( asset => !asset.IsDeleted )
			.Where( asset => string.Equals( asset.AssetType?.ResourceType?.FullName, typeof( PrefabFile ).FullName, StringComparison.OrdinalIgnoreCase ) || string.Equals( asset.AssetType?.FileExtension, "prefab", StringComparison.OrdinalIgnoreCase ) )
			.Where( asset => string.IsNullOrWhiteSpace( query ) || (asset.Name ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) || (asset.RelativePath ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) )
			.Take( maxResults )
			.Select( DescribePrefabAsset )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Prefabs listed",
			verified = new
			{
				query,
				count = results.Length,
				results
			}
		} );
	}

	public static BridgeResponse GetInfo( BridgeRequest request )
	{
		var path = NormalizePrefabPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var prefab = RequirePrefab( path );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Prefab info read",
			verified = DescribePrefab( prefab )
		} );
	}

	public static BridgeResponse InspectInstance( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "active" );
		var go = HandlerUtil.RequireGameObject( resolution.Session.Scene, request.Payload, "gameObjectId" );
		var includeSerialized = HandlerUtil.GetBool( request.Payload, "includeSerialized", false );
		var maxSamples = Math.Clamp( HandlerUtil.GetInt( request.Payload, "maxSamples", 10 ), 0, 100 );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Prefab instance inspected",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				instance = DescribePrefabInstance( go, includeSerialized, maxSamples )
			}
		} );
	}

	public static BridgeResponse Instantiate( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var path = NormalizePrefabPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var prefab = RequirePrefab( path );

		var position = HandlerUtil.GetVector3( request.Payload, "position" ) ?? Vector3.Zero;
		var rotation = HandlerUtil.GetRotation( request.Payload, "rotation" ) ?? Rotation.Identity;
		var scale = HandlerUtil.GetVector3( request.Payload, "scale" ) ?? Vector3.One;
		var parent = HandlerUtil.GetOptionalGameObject( session.Scene, request.Payload );
		var name = HandlerUtil.GetString( request.Payload, "name", prefab.ResourceName );
		GameObject instance;

		if ( prefab.RootObject is null )
			throw new InvalidOperationException( $"Prefab '{path}' has no root GameObject to instantiate." );

		var rootObject = JsonNode.Parse( prefab.RootObject.ToJsonString() )?.AsObject();
		if ( rootObject is null )
			throw new InvalidOperationException( $"Prefab '{path}' root object could not be cloned for deserialization." );

		PreparePrefabInstanceRootObject( rootObject, path );

		using ( session.UndoScope( "Agent Bridge: Instantiate Prefab" ).WithGameObjectCreations().Push() )
		{
			instance = session.Scene.CreateObject( true );
			instance.Deserialize( rootObject, new GameObject.DeserializeOptions
			{
				TransformOverride = new Transform( position, rotation, scale )
			} );

			if ( parent is not null )
				instance.SetParent( parent, true );

			instance.Name = name;
			instance.MakeNameUnique();
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Prefab instantiated",
			verified = new
			{
				prefab = DescribePrefab( prefab ),
				gameObject = HandlerUtil.DescribeGameObject( instance )
			}
		} );
	}

	private static object DescribePrefabAsset( Asset asset )
	{
		return new
		{
			name = asset.Name,
			path = asset.Path,
			relativePath = asset.RelativePath,
			assetType = asset.AssetType?.FriendlyName ?? "",
			isCompiled = asset.IsCompiled,
			isCompiledAndUpToDate = asset.IsCompiledAndUpToDate,
			isCompileFailed = asset.IsCompileFailed
		};
	}

	private static object DescribePrefabInstance( GameObject go, bool includeSerialized, int maxSamples )
	{
		var rootObject = SerializeGameObject( go );
		var prefabPath = GetJsonString( rootObject, "__Prefab" );
		var patch = rootObject["__PrefabInstancePatch"] as JsonObject;
		var prefabIdMap = rootObject["__PrefabIdToInstanceId"] as JsonObject;

		return new
		{
			gameObject = HandlerUtil.DescribeGameObject( go ),
			isPrefabInstance = !string.IsNullOrWhiteSpace( prefabPath ),
			prefabPath,
			prefabAsset = string.IsNullOrWhiteSpace( prefabPath ) ? null : TryDescribePrefabAsset( prefabPath ),
			patch = DescribePrefabPatch( patch, includeSerialized, maxSamples ),
			prefabIdToInstanceId = new
			{
				count = prefabIdMap?.Count ?? 0,
				json = includeSerialized && prefabIdMap is not null ? prefabIdMap.ToJsonString() : null
			},
			serializedJson = includeSerialized ? rootObject.ToJsonString() : null
		};
	}

	private static JsonObject SerializeGameObject( GameObject go )
	{
		var serialized = go.Serialize( new GameObject.SerializeOptions { Cloning = false } );
		var rootObject = JsonNode.Parse( serialized.ToJsonString() )?.AsObject();

		if ( rootObject is null )
			throw new InvalidOperationException( $"GameObject '{go.Name}' could not be serialized for prefab inspection." );

		return rootObject;
	}

	private static object? TryDescribePrefabAsset( string path )
	{
		var asset = AssetSystem.FindByPath( path );
		if ( asset is null && path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			asset = AssetSystem.FindByPath( path.Substring( "assets/".Length ) );

		return asset is null ? null : DescribePrefabAsset( asset );
	}

	private static object DescribePrefabPatch( JsonObject? patch, bool includeSerialized, int maxSamples )
	{
		return new
		{
			exists = patch is not null,
			addedObjectCount = CountPatchArray( patch, "AddedObjects" ),
			removedObjectCount = CountPatchArray( patch, "RemovedObjects" ),
			propertyOverrideCount = CountPatchArray( patch, "PropertyOverrides" ),
			movedObjectCount = CountPatchArray( patch, "MovedObjects" ),
			addedObjectSamples = DescribePatchObjectSamples( patch, "AddedObjects", maxSamples ),
			removedObjectSamples = DescribePatchObjectSamples( patch, "RemovedObjects", maxSamples ),
			propertyOverrideSamples = DescribePropertyOverrideSamples( patch, maxSamples ),
			movedObjectSamples = DescribePatchObjectSamples( patch, "MovedObjects", maxSamples ),
			json = includeSerialized && patch is not null ? patch.ToJsonString() : null
		};
	}

	private static int CountPatchArray( JsonObject? patch, string propertyName )
	{
		return patch is not null && patch[propertyName] is JsonArray array ? array.Count : 0;
	}

	private static object[] DescribePatchObjectSamples( JsonObject? patch, string propertyName, int maxSamples )
	{
		if ( patch is null || patch[propertyName] is not JsonArray array || maxSamples <= 0 )
			return Array.Empty<object>();

		return array
			.Take( maxSamples )
			.Select( node => DescribePatchObjectSample( node ) )
			.Cast<object>()
			.ToArray();
	}

	private static object DescribePatchObjectSample( JsonNode? node )
	{
		var obj = node as JsonObject;
		var id = obj?["Id"] as JsonObject;

		return new
		{
			type = GetJsonString( id, "Type" ),
			id = GetJsonString( id, "IdValue" ),
			name = GetJsonString( obj, "Name" ),
			guid = GetJsonString( obj, "__guid" ),
			json = node?.ToJsonString()
		};
	}

	private static object[] DescribePropertyOverrideSamples( JsonObject? patch, int maxSamples )
	{
		if ( patch is null || patch["PropertyOverrides"] is not JsonArray array || maxSamples <= 0 )
			return Array.Empty<object>();

		return array
			.Take( maxSamples )
			.Select( node =>
			{
				var obj = node as JsonObject;
				var target = obj?["Target"] as JsonObject;

				return new
				{
					targetType = GetJsonString( target, "Type" ),
					targetId = GetJsonString( target, "IdValue" ),
					property = GetJsonString( obj, "Property" ),
					value = DescribePatchValue( obj?["Value"] )
				};
			} )
			.Cast<object>()
			.ToArray();
	}

	private static string GetJsonString( JsonObject? obj, string propertyName )
	{
		if ( obj is null || !obj.TryGetPropertyValue( propertyName, out var node ) || node is null )
			return "";

		return node.ToString();
	}

	private static object? DescribePatchValue( JsonNode? node )
	{
		if ( node is null )
			return null;

		if ( node is JsonValue value )
		{
			if ( value.TryGetValue<string>( out var stringValue ) )
				return stringValue;

			if ( value.TryGetValue<bool>( out var boolValue ) )
				return boolValue;

			if ( value.TryGetValue<double>( out var numberValue ) )
				return numberValue;
		}

		return node.ToJsonString();
	}

	private static PrefabFile RequirePrefab( string path )
	{
		var prefab = PrefabFile.Load( path );

		if ( prefab is not null && prefab.IsValid )
			return prefab;

		var asset = AssetSystem.FindByPath( path );
		if ( asset is null && path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			asset = AssetSystem.FindByPath( path.Substring( "assets/".Length ) );

		if ( asset is not null )
		{
			try
			{
				prefab = asset.LoadResource<PrefabFile>();
			}
			catch
			{
				prefab = null;
			}
		}

		if ( prefab is null || !prefab.IsValid )
			throw new InvalidOperationException( $"Prefab '{path}' could not be loaded." );

		return prefab;
	}

	private static void PreparePrefabInstanceRootObject( JsonObject rootObject, string path )
	{
		var idMap = new Dictionary<string, string>( StringComparer.OrdinalIgnoreCase );
		CollectGuidMap( rootObject, idMap );
		RewriteGuidReferences( rootObject, idMap );

		rootObject["__Prefab"] = path;
		rootObject["__PrefabInstancePatch"] = CreateEmptyPrefabPatch();

		var mapObject = new JsonObject();
		foreach ( var (sourceId, instanceId) in idMap )
		{
			mapObject[sourceId] = instanceId;
		}

		rootObject["__PrefabIdToInstanceId"] = mapObject;
	}

	private static void CollectGuidMap( JsonNode? node, Dictionary<string, string> idMap )
	{
		if ( node is JsonObject obj )
		{
			if ( obj.TryGetPropertyValue( "__guid", out var guidNode ) && TryGetJsonString( guidNode, out var guid ) && Guid.TryParse( guid, out _ ) && !idMap.ContainsKey( guid ) )
				idMap[guid] = Guid.NewGuid().ToString();

			foreach ( var child in obj.Select( x => x.Value ).ToArray() )
			{
				CollectGuidMap( child, idMap );
			}

			return;
		}

		if ( node is JsonArray arr )
		{
			foreach ( var child in arr.ToArray() )
			{
				CollectGuidMap( child, idMap );
			}
		}
	}

	private static void RewriteGuidReferences( JsonNode? node, IReadOnlyDictionary<string, string> idMap )
	{
		if ( node is JsonObject obj )
		{
			foreach ( var property in obj.ToArray() )
			{
				if ( (property.Key == "__guid" || property.Key == "IdValue") && TryGetJsonString( property.Value, out var guid ) && idMap.TryGetValue( guid, out var replacement ) )
				{
					obj[property.Key] = replacement;
					continue;
				}

				RewriteGuidReferences( property.Value, idMap );
			}

			return;
		}

		if ( node is JsonArray arr )
		{
			foreach ( var child in arr.ToArray() )
			{
				RewriteGuidReferences( child, idMap );
			}
		}
	}

	private static JsonObject CreateEmptyPrefabPatch()
	{
		return new JsonObject
		{
			["AddedObjects"] = new JsonArray(),
			["RemovedObjects"] = new JsonArray(),
			["PropertyOverrides"] = new JsonArray(),
			["MovedObjects"] = new JsonArray()
		};
	}

	private static bool TryGetJsonString( JsonNode? node, out string value )
	{
		if ( node is JsonValue jsonValue && jsonValue.TryGetValue<string>( out var stringValue ) )
		{
			value = stringValue;
			return true;
		}

		value = "";
		return false;
	}

	private static object DescribePrefab( PrefabFile prefab )
	{
		return new
		{
			resource = HandlerUtil.DescribeResourceReference( prefab ),
			showInMenu = prefab.ShowInMenu,
			menuPath = prefab.MenuPath,
			menuIcon = prefab.MenuIcon,
			hasRootObject = prefab.RootObject is not null,
			rootObjectJson = prefab.RootObject?.ToJsonString()
		};
	}

	private static string NormalizePrefabPath( string path )
	{
		path = (path ?? "").Replace( '\\', '/' ).Trim().TrimStart( '/' );

		if ( path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			path = path.Substring( "assets/".Length );

		if ( path.Split( '/' ).Any( part => part == ".." ) )
			throw new InvalidOperationException( "Prefab path cannot contain '..' segments." );

		return path;
	}

	private static string EnsureExtension( string path, string extension )
	{
		return path.EndsWith( extension, StringComparison.OrdinalIgnoreCase ) ? path : path + extension;
	}

	private static string ResolveAssetPath( string relativePath )
	{
		var assetRoot = Path.GetFullPath( Project.Current.GetAssetsPath() );
		var absolutePath = Path.GetFullPath( Path.Combine( assetRoot, relativePath.Replace( '/', Path.DirectorySeparatorChar ) ) );

		if ( !absolutePath.StartsWith( assetRoot, StringComparison.OrdinalIgnoreCase ) )
			throw new InvalidOperationException( "Resolved prefab path escaped the project Assets directory." );

		return absolutePath;
	}
}
