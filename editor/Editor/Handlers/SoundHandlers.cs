using System;
using System.IO;
using System.Linq;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class SoundHandlers
{
	public static BridgeResponse List( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var kind = HandlerUtil.GetString( request.Payload, "kind" ).ToLowerInvariant();
		var maxResults = HandlerUtil.GetInt( request.Payload, "maxResults", 50 );

		var results = AssetSystem.All
			.Where( asset => !asset.IsDeleted )
			.Where( IsSoundAsset )
			.Where( asset => string.IsNullOrWhiteSpace( kind ) || MatchesSoundKind( asset, kind ) )
			.Where( asset => string.IsNullOrWhiteSpace( query ) || MatchesQuery( asset, query ) )
			.Take( maxResults )
			.Select( DescribeSoundAsset )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Sounds listed",
			verified = new
			{
				query,
				kind,
				count = results.Length,
				results
			}
		} );
	}

	public static BridgeResponse GetInfo( BridgeRequest request )
	{
		var path = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var asset = RequireAsset( path );
		var soundEvent = TryLoadSoundEvent( asset );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Sound info read",
			verified = new
			{
				asset = DescribeSoundAsset( asset ),
				soundEvent = soundEvent is null ? null : DescribeSoundEvent( soundEvent )
			}
		} );
	}

	public static BridgeResponse Inspect( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "active" );
		var go = HandlerUtil.RequireGameObject( resolution.Session.Scene, request.Payload, "gameObjectId" );
		var components = go.Components.GetAll().OfType<SoundPointComponent>().Select( DescribeSoundComponent ).ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Sound components inspected",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				gameObject = HandlerUtil.DescribeGameObject( go ),
				count = components.Length,
				components
			}
		} );
	}

	public static BridgeResponse CreateEvent( BridgeRequest request )
	{
		var relativePath = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var absolutePath = ResolveAssetPath( EnsureExtension( relativePath, ".sound" ) );
		var overwrite = HandlerUtil.GetBool( request.Payload, "overwrite", false );
		var soundFilePath = HandlerUtil.GetString( request.Payload, "soundFilePath" );
		var volume = HandlerUtil.GetFloat( request.Payload, "volume", 0.75f );
		var pitch = HandlerUtil.GetFloat( request.Payload, "pitch", 1.0f );
		var decibels = HandlerUtil.GetInt( request.Payload, "decibels", 0 );

		if ( File.Exists( absolutePath ) && !overwrite )
			throw new InvalidOperationException( $"Sound event '{relativePath}' already exists. Pass overwrite:true to replace it." );

		Directory.CreateDirectory( Path.GetDirectoryName( absolutePath ) ?? Project.Current.GetAssetsPath() );

		var asset = File.Exists( absolutePath )
			? AssetSystem.FindByPath( relativePath ) ?? AssetSystem.RegisterFile( absolutePath )
			: AssetSystem.CreateResource( "sound", absolutePath );

		if ( asset is null )
			throw new InvalidOperationException( $"Could not create or register sound event asset '{relativePath}'." );

		var soundEvent = string.IsNullOrWhiteSpace( soundFilePath )
			? new SoundEvent()
			: new SoundEvent( NormalizeAssetPath( soundFilePath ), volume );

		soundEvent.Volume = new RangedFloat( volume );
		soundEvent.Pitch = new RangedFloat( pitch );
		soundEvent.Decibels = decibels;

		if ( soundEvent.Sounds is null )
		{
			soundEvent.Sounds = new();
		}

		if ( !string.IsNullOrWhiteSpace( soundFilePath ) && soundEvent.Sounds.Count == 0 )
		{
			var soundFile = SoundFile.Load( NormalizeAssetPath( soundFilePath ) );
			if ( soundFile is null || !soundFile.IsValid )
				throw new InvalidOperationException( $"Sound file '{soundFilePath}' could not be loaded." );

			soundEvent.Sounds.Add( soundFile );
		}

		if ( !asset.SaveToDisk( soundEvent ) )
			throw new InvalidOperationException( $"Sound event '{relativePath}' could not be saved to disk." );

		asset.Compile( true );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Sound event created",
			verified = new
			{
				asset = DescribeSoundAsset( asset ),
				soundEvent = DescribeSoundEvent( soundEvent )
			}
		} );
	}

	public static BridgeResponse Assign( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var eventPath = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "eventPath" ) );
		var soundEvent = RequireSoundEvent( eventPath );
		var component = go.Components.Get<SoundPointComponent>();
		var playOnStart = HandlerUtil.GetBool( request.Payload, "playOnStart", component?.PlayOnStart ?? false );
		var repeat = HandlerUtil.GetBool( request.Payload, "repeat", component?.Repeat ?? false );
		var force2d = HandlerUtil.GetBool( request.Payload, "force2d", component?.Force2d ?? false );
		var volume = HandlerUtil.GetFloat( request.Payload, "volume", component?.Volume ?? 1.0f );
		var pitch = HandlerUtil.GetFloat( request.Payload, "pitch", component?.Pitch ?? 1.0f );

		if ( component is null )
		{
			using ( session.UndoScope( "Agent Bridge: Assign Sound" ).WithComponentCreations().Push() )
			{
				component = go.Components.Create<SoundPointComponent>();
				ConfigureSoundComponent( component, soundEvent, playOnStart, repeat, force2d, volume, pitch );
			}
		}
		else
		{
			using ( session.UndoScope( "Agent Bridge: Assign Sound" ).WithComponentChanges( component ).Push() )
			{
				ConfigureSoundComponent( component, soundEvent, playOnStart, repeat, force2d, volume, pitch );
			}
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Sound assigned",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				component = HandlerUtil.DescribeComponent( component ),
				soundEvent = DescribeSoundEvent( soundEvent )
			}
		} );
	}

	public static BridgeResponse Preview( BridgeRequest request )
	{
		var eventPath = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "eventPath" ) );
		var soundEvent = RequireSoundEvent( eventPath );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );
		var fadeIn = HandlerUtil.GetFloat( request.Payload, "fadeIn", 0f );
		var handle = position.HasValue
			? Sound.Play( soundEvent, position.Value, fadeIn )
			: Sound.Play( soundEvent, fadeIn );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Sound preview started",
			verified = new
			{
				soundEvent = DescribeSoundEvent( soundEvent ),
				handle = new
				{
					isValid = Safe( () => handle.IsValid, false ),
					isPlaying = Safe( () => handle.IsPlaying, false ),
					isStopped = Safe( () => handle.IsStopped, false ),
					name = Safe( () => handle.Name, "" ),
					volume = Safe( () => handle.Volume, 0f ),
					pitch = Safe( () => handle.Pitch, 0f ),
					position = Safe( () => HandlerUtil.ToJson( handle.Position ), (object?)null )
				}
			}
		} );
	}

	private static void ConfigureSoundComponent( SoundPointComponent component, SoundEvent soundEvent, bool playOnStart, bool repeat, bool force2d, float volume, float pitch )
	{
		component.SoundEvent = soundEvent;
		component.PlayOnStart = playOnStart;
		component.Repeat = repeat;
		component.Force2d = force2d;
		component.Volume = volume;
		component.Pitch = pitch;
	}

	private static object DescribeSoundComponent( SoundPointComponent component )
	{
		return new
		{
			component = HandlerUtil.DescribeComponent( component ),
			soundEvent = component.SoundEvent is null ? null : DescribeSoundEvent( component.SoundEvent ),
			playOnStart = component.PlayOnStart,
			repeat = component.Repeat,
			force2d = component.Force2d,
			volume = component.Volume,
			pitch = component.Pitch
		};
	}

	private static bool IsSoundAsset( Asset asset )
	{
		return string.Equals( asset.AssetType?.ResourceType?.FullName, typeof( SoundEvent ).FullName, StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( asset.AssetType?.ResourceType?.FullName, typeof( SoundFile ).FullName, StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( asset.AssetType?.FileExtension, "sound", StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( asset.AssetType?.FileExtension, "wav", StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( asset.AssetType?.FileExtension, "mp3", StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( asset.AssetType?.FileExtension, "ogg", StringComparison.OrdinalIgnoreCase );
	}

	private static bool MatchesSoundKind( Asset asset, string kind )
	{
		return kind switch
		{
			"event" or "soundevent" => string.Equals( asset.AssetType?.ResourceType?.FullName, typeof( SoundEvent ).FullName, StringComparison.OrdinalIgnoreCase ) || string.Equals( asset.AssetType?.FileExtension, "sound", StringComparison.OrdinalIgnoreCase ),
			"file" or "soundfile" => string.Equals( asset.AssetType?.ResourceType?.FullName, typeof( SoundFile ).FullName, StringComparison.OrdinalIgnoreCase ),
			_ => IsSoundAsset( asset )
		};
	}

	private static bool MatchesQuery( Asset asset, string query )
	{
		return (asset.Name ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) ||
			(asset.Path ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) ||
			(asset.RelativePath ?? "").Contains( query, StringComparison.OrdinalIgnoreCase );
	}

	private static Asset RequireAsset( string path )
	{
		var asset = AssetSystem.FindByPath( path );
		if ( asset is null && path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			asset = AssetSystem.FindByPath( path.Substring( "assets/".Length ) );

		if ( asset is null )
			throw new InvalidOperationException( $"Asset '{path}' was not found." );

		return asset;
	}

	private static SoundEvent RequireSoundEvent( string path )
	{
		var asset = RequireAsset( path );
		var soundEvent = TryLoadSoundEvent( asset );

		if ( soundEvent is null || !soundEvent.IsValid )
			throw new InvalidOperationException( $"Sound event '{path}' could not be loaded." );

		return soundEvent;
	}

	private static SoundEvent? TryLoadSoundEvent( Asset asset )
	{
		try
		{
			return asset.LoadResource<SoundEvent>();
		}
		catch
		{
			return null;
		}
	}

	private static object DescribeSoundAsset( Asset asset )
	{
		return new
		{
			name = asset.Name,
			path = asset.Path,
			relativePath = asset.RelativePath,
			assetType = asset.AssetType?.FriendlyName ?? "",
			extension = asset.AssetType?.FileExtension ?? "",
			resourceType = asset.AssetType?.ResourceType?.FullName ?? "",
			isCloud = asset.IsCloud,
			isCompiled = asset.IsCompiled,
			isCompiledAndUpToDate = asset.IsCompiledAndUpToDate,
			isCompileFailed = asset.IsCompileFailed
		};
	}

	private static object DescribeSoundEvent( SoundEvent soundEvent )
	{
		return new
		{
			resource = HandlerUtil.DescribeResourceReference( soundEvent ),
			volume = DescribeRange( soundEvent.Volume ),
			pitch = DescribeRange( soundEvent.Pitch ),
			decibels = soundEvent.Decibels,
			selectionMode = soundEvent.SelectionMode.ToString(),
			distanceAttenuation = soundEvent.DistanceAttenuation,
			distance = soundEvent.Distance,
			occlusion = soundEvent.Occlusion,
			reflections = soundEvent.Reflections,
			sounds = (soundEvent.Sounds ?? new()).Select( sound => new
			{
				name = Safe( () => sound.ResourceName, "" ),
				path = Safe( () => sound.ResourcePath, "" ),
				isValid = Safe( () => sound.IsValid, false ),
				isLoaded = Safe( () => sound.IsLoaded, false ),
				isValidForPlayback = Safe( () => sound.IsValidForPlayback, false ),
				duration = Safe( () => sound.Duration, 0f ),
				channels = Safe( () => sound.Channels, 0 ),
				rate = Safe( () => sound.Rate, 0 )
			} ).ToArray()
		};
	}

	private static object DescribeRange( RangedFloat range )
	{
		return new
		{
			min = range.Min,
			max = range.Max,
			fixedValue = range.FixedValue
		};
	}

	private static string NormalizeAssetPath( string path )
	{
		path = (path ?? "").Replace( '\\', '/' ).Trim().TrimStart( '/' );

		if ( path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			path = path.Substring( "assets/".Length );

		if ( path.Split( '/' ).Any( part => part == ".." ) )
			throw new InvalidOperationException( "Asset path cannot contain '..' segments." );

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
			throw new InvalidOperationException( "Resolved sound path escaped the project Assets directory." );

		return absolutePath;
	}

	private static T Safe<T>( Func<T> getter, T fallback )
	{
		try
		{
			return getter();
		}
		catch
		{
			return fallback;
		}
	}
}
