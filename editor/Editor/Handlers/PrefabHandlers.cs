using System;
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

		StripSerializedGuids( rootObject );

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
			instance.SetPrefabSource( path );
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

	private static void StripSerializedGuids( JsonNode? node )
	{
		if ( node is JsonObject obj )
		{
			obj.Remove( "__guid" );

			foreach ( var child in obj.Select( x => x.Value ).ToArray() )
			{
				StripSerializedGuids( child );
			}

			return;
		}

		if ( node is JsonArray arr )
		{
			foreach ( var child in arr.ToArray() )
			{
				StripSerializedGuids( child );
			}
		}
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
