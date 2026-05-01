using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class AssetHandlers
{
	public static BridgeResponse Search( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var type = HandlerUtil.GetString( request.Payload, "type" );
		var maxResults = HandlerUtil.GetInt( request.Payload, "maxResults", 50 );

		var results = AssetSystem.All
			.Where( asset => !asset.IsDeleted )
			.Where( asset => string.IsNullOrWhiteSpace( query ) || MatchesQuery( asset, query ) )
			.Where( asset => string.IsNullOrWhiteSpace( type ) || MatchesType( asset, type ) )
			.Take( maxResults )
			.Select( DescribeAsset )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Assets searched",
			verified = new
			{
				query,
				type,
				count = results.Length,
				results
			}
		} );
	}

	public static BridgeResponse GetInfo( BridgeRequest request )
	{
		var path = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var asset = RequireAsset( path );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Asset info read",
			verified = DescribeAssetDetails( asset )
		} );
	}

	public static BridgeResponse InspectModel( BridgeRequest request )
	{
		var rawPath = HandlerUtil.GetString( request.Payload, "modelPath" );
		if ( string.IsNullOrWhiteSpace( rawPath ) )
			rawPath = HandlerUtil.GetString( request.Payload, "path" );

		if ( string.IsNullOrWhiteSpace( rawPath ) )
			throw new InvalidOperationException( "asset.inspect_model requires a modelPath or path payload property." );

		var path = NormalizeAssetPath( rawPath );
		var scale = HandlerUtil.GetVector3( request.Payload, "scale" ) ?? new Vector3( 1f, 1f, 1f );
		var yaw = HandlerUtil.GetFloat( request.Payload, "yaw", 0f );
		var includeMaterials = HandlerUtil.GetBool( request.Payload, "includeMaterials", true );
		var model = Model.Load( path );

		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidOperationException( $"Model '{path}' could not be loaded." );

		var asset = AssetSystem.FindByPath( path );
		var candidates = GetOrientationCandidates( yaw )
			.Select( candidate => DescribeOrientationCandidate( candidate, model.RenderBounds, scale ) )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Model inspected",
			verified = new
			{
				path,
				asset = asset is null ? null : DescribeAssetDetails( asset ),
				model = HandlerUtil.DescribeResourceReference( model ),
				scale = HandlerUtil.ToJson( scale ),
				bounds = new
				{
					model = HandlerUtil.DescribeBBox( model.Bounds ),
					render = HandlerUtil.DescribeBBox( model.RenderBounds ),
					physics = HandlerUtil.DescribeBBox( model.PhysicsBounds )
				},
				materials = includeMaterials ? DescribeModelMaterials( model ) : null,
				orientationCandidates = candidates,
				limitations = new[]
				{
					"Bounds describe geometry, not semantic up direction; use capture_camera or human/editor feedback to confirm final orientation.",
					"groundOffsetZ is the local Z offset needed to put the candidate's minimum bound on the ground plane."
				}
			}
		} );
	}

	public static BridgeResponse GetOrientationOverride( BridgeRequest request )
	{
		var path = GetModelPath( request );
		var record = OrientationOverrideStore.Get( path );

		return BridgeResponse.Success( request.Id, new
		{
			message = record is null ? "Model orientation override not found" : "Model orientation override read",
			verified = new
			{
				modelPath = path,
				found = record is not null,
				storage = OrientationOverrideStore.DescribeStorage(),
				orientationOverride = record is null ? null : OrientationOverrideStore.DescribeRecord( record )
			}
		} );
	}

	public static BridgeResponse SetOrientationOverride( BridgeRequest request )
	{
		var path = GetModelPath( request );
		var model = Model.Load( path );

		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidOperationException( $"Model '{path}' could not be loaded." );

		var baseRotation = ReadOrientationAngles( request.Payload, "baseRotation", new OrientationAngles() );
		var calculatedGroundOffset = HandlerUtil.CalculateGroundOffsetZ( model.RenderBounds, HandlerUtil.ToRotation( baseRotation ), Vector3.One );
		var groundOffset = HandlerUtil.GetFloat( request.Payload, "groundOffsetZ", float.NaN );

		if ( float.IsNaN( groundOffset ) )
			groundOffset = HandlerUtil.GetFloat( request.Payload, "groundOffset", float.NaN );

		if ( float.IsNaN( groundOffset ) )
			groundOffset = calculatedGroundOffset;

		var record = OrientationOverrideStore.Set( new OrientationOverrideRecord
		{
			ModelPath = path,
			BaseRotation = baseRotation,
			GroundOffsetZ = groundOffset,
			ForwardAxis = HandlerUtil.GetString( request.Payload, "forwardAxis", "+Y" ),
			Confidence = HandlerUtil.GetString( request.Payload, "confidence", "agent_verified" ),
			Source = HandlerUtil.GetString( request.Payload, "source", "agent" ),
			Notes = HandlerUtil.GetString( request.Payload, "notes" )
		} );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Model orientation override saved",
			verified = new
			{
				storage = OrientationOverrideStore.DescribeStorage(),
				orientationOverride = OrientationOverrideStore.DescribeRecord( record ),
				calculated = new
				{
					groundOffsetZ = calculatedGroundOffset,
					bounds = HandlerUtil.DescribeBBox( model.RenderBounds )
				}
			}
		} );
	}

	public static BridgeResponse AssignModel( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var path = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "modelPath" ) );
		var renderer = go.Components.Get<ModelRenderer>();
		var model = Model.Load( path );

		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidOperationException( $"Model '{path}' could not be loaded." );

		if ( renderer is null )
		{
			using ( session.UndoScope( "Agent Bridge: Assign Model" ).WithComponentCreations().Push() )
			{
				renderer = go.Components.Create<ModelRenderer>();
				renderer.Model = model;
			}
		}
		else
		{
			using ( session.UndoScope( "Agent Bridge: Assign Model" ).WithComponentChanges( renderer ).Push() )
			{
				renderer.Model = model;
			}
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Model assigned",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				component = HandlerUtil.DescribeComponent( renderer ),
				model = HandlerUtil.DescribeResourceReference( model )
			}
		} );
	}

	public static BridgeResponse AssignMaterial( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var renderer = ResolveRenderer( session, request );
		var path = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "materialPath" ) );
		var material = Material.Load( path );

		if ( material is null || !material.IsValid )
			throw new InvalidOperationException( $"Material '{path}' could not be loaded." );

		using ( session.UndoScope( "Agent Bridge: Assign Material" ).WithComponentChanges( renderer ).Push() )
		{
			renderer.MaterialOverride = material;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Material assigned",
			verified = new
			{
				component = HandlerUtil.DescribeComponent( renderer ),
				gameObject = HandlerUtil.DescribeGameObject( renderer.GameObject ),
				material = HandlerUtil.DescribeResourceReference( material )
			}
		} );
	}

	public static BridgeResponse CreateMaterial( BridgeRequest request )
	{
		var relativePath = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var name = HandlerUtil.GetString( request.Payload, "name", Path.GetFileNameWithoutExtension( relativePath ) );
		var shader = HandlerUtil.GetString( request.Payload, "shader", "shaders/complex.shader" );
		var color = Color.Parse( HandlerUtil.GetString( request.Payload, "color", "white" ) ) ?? Color.White;
		var overwrite = HandlerUtil.GetBool( request.Payload, "overwrite", false );
		var absolutePath = ResolveAssetPath( EnsureExtension( relativePath, ".vmat" ) );

		if ( File.Exists( absolutePath ) && !overwrite )
			throw new InvalidOperationException( $"Material '{relativePath}' already exists. Pass overwrite:true to replace it." );

		Directory.CreateDirectory( Path.GetDirectoryName( absolutePath ) ?? Project.Current.GetAssetsPath() );
		File.WriteAllText( absolutePath, CreateVmatSource( name, shader, color ), new System.Text.UTF8Encoding( false ) );

		var asset = AssetSystem.RegisterFile( absolutePath ) ?? AssetSystem.FindByPath( relativePath );

		if ( asset is null )
			throw new InvalidOperationException( $"AssetSystem could not register material '{relativePath}'." );

		asset.Compile( true );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Material created",
			verified = DescribeAssetDetails( asset )
		} );
	}

	public static BridgeResponse SetMaterialProperty( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var renderer = ResolveRenderer( session, request );
		var property = HandlerUtil.GetRequiredString( request.Payload, "property" );
		var material = renderer.MaterialOverride;

		if ( material is null || !material.IsValid )
			throw new InvalidOperationException( "Target renderer has no valid MaterialOverride. Assign a material first." );

		if ( request.Payload.ValueKind != System.Text.Json.JsonValueKind.Object || !request.Payload.TryGetProperty( "value", out var value ) )
			throw new InvalidOperationException( "set_material_property requires a value property." );

		var success = value.ValueKind switch
		{
			System.Text.Json.JsonValueKind.True => material.Set( property, true ),
			System.Text.Json.JsonValueKind.False => material.Set( property, false ),
			System.Text.Json.JsonValueKind.Number when value.TryGetInt32( out var intValue ) => material.Set( property, intValue ),
			System.Text.Json.JsonValueKind.Number when value.TryGetSingle( out var floatValue ) => material.Set( property, floatValue ),
			System.Text.Json.JsonValueKind.String => material.Set( property, Color.Parse( value.GetString() ?? "" ) ?? Color.White ),
			_ => throw new InvalidOperationException( "Material property v0 supports bool, number, or color string values." )
		};

		return BridgeResponse.Success( request.Id, new
		{
			message = success ? "Material property set" : "Material property rejected by material",
			verified = new
			{
				success,
				property,
				component = HandlerUtil.DescribeComponent( renderer ),
				material = HandlerUtil.DescribeResourceReference( material )
			}
		} );
	}

	private static ModelRenderer ResolveRenderer( SceneEditorSession session, BridgeRequest request )
	{
		var componentId = HandlerUtil.GetString( request.Payload, "componentId" );
		if ( !string.IsNullOrWhiteSpace( componentId ) )
		{
			var component = HandlerUtil.RequireComponentById( session.Scene, componentId, "componentId" );
			if ( component is ModelRenderer rendererById )
				return rendererById;

			throw new InvalidOperationException( $"Component '{componentId}' is not a ModelRenderer." );
		}

		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		return go.Components.Get<ModelRenderer>() ?? go.Components.Create<ModelRenderer>();
	}

	private static bool MatchesQuery( Asset asset, string query )
	{
		return (asset.Name ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) ||
			(asset.Path ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) ||
			(asset.RelativePath ?? "").Contains( query, StringComparison.OrdinalIgnoreCase );
	}

	private static bool MatchesType( Asset asset, string type )
	{
		var assetType = asset.AssetType;
		return string.Equals( assetType?.FriendlyName ?? "", type, StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( assetType?.FileExtension ?? "", type.TrimStart( '.' ), StringComparison.OrdinalIgnoreCase ) ||
			string.Equals( assetType?.ResourceType?.Name ?? "", type, StringComparison.OrdinalIgnoreCase );
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

	private static object DescribeAsset( Asset asset )
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

	private static object DescribeAssetDetails( Asset asset )
	{
		return new
		{
			asset = DescribeAsset( asset ),
			absolutePath = asset.AbsolutePath,
			hasSourceFile = asset.HasSourceFile,
			hasCompiledFile = asset.HasCompiledFile,
			sourceFile = SafePath( () => asset.GetSourceFile( false ) ),
			compiledFile = SafePath( () => asset.GetCompiledFile( false ) ),
			hasUnsavedChanges = asset.HasUnsavedChanges,
			tags = asset.Tags.GetAll()
		};
	}

	private sealed class OrientationCandidate
	{
		public string Name { get; init; } = "";
		public float Pitch { get; init; }
		public float Yaw { get; init; }
		public float Roll { get; init; }
	}

	private static OrientationCandidate[] GetOrientationCandidates( float yaw )
	{
		return new[]
		{
			new OrientationCandidate { Name = "asImported", Pitch = 0f, Yaw = yaw, Roll = 0f },
			new OrientationCandidate { Name = "pitch90", Pitch = 90f, Yaw = yaw, Roll = 0f },
			new OrientationCandidate { Name = "pitchMinus90", Pitch = -90f, Yaw = yaw, Roll = 0f },
			new OrientationCandidate { Name = "roll90", Pitch = 0f, Yaw = yaw, Roll = 90f },
			new OrientationCandidate { Name = "rollMinus90", Pitch = 0f, Yaw = yaw, Roll = -90f },
			new OrientationCandidate { Name = "roll180", Pitch = 0f, Yaw = yaw, Roll = 180f }
		};
	}

	private static object DescribeOrientationCandidate( OrientationCandidate candidate, BBox sourceBounds, Vector3 scale )
	{
		var rotation = Rotation.From( candidate.Pitch, candidate.Yaw, candidate.Roll );
		var transform = new Transform( Vector3.Zero, rotation, scale );
		var bounds = sourceBounds.Transform( transform );
		var groundOffset = -bounds.Mins.z;
		var groundedBounds = bounds.Translate( Vector3.Up * groundOffset );
		var size = bounds.Size;

		return new
		{
			name = candidate.Name,
			rotation = new
			{
				pitch = candidate.Pitch,
				yaw = candidate.Yaw,
				roll = candidate.Roll,
				quaternion = HandlerUtil.ToJson( rotation )
			},
			bounds = HandlerUtil.DescribeBBox( bounds ),
			groundedBounds = HandlerUtil.DescribeBBox( groundedBounds ),
			groundOffsetZ = groundOffset,
			height = size.z,
			footprint = new
			{
				x = size.x,
				y = size.y,
				area = size.x * size.y
			},
			spatialHeuristic = new
			{
				tallestAxisAfterRotation = GetTallestAxis( size ),
				likelyStandingCandidate = size.z >= size.x && size.z >= size.y
			}
		};
	}

	private static string GetTallestAxis( Vector3 size )
	{
		if ( size.z >= size.x && size.z >= size.y )
			return "z";

		if ( size.x >= size.y )
			return "x";

		return "y";
	}

	private static object DescribeModelMaterials( Model model )
	{
		try
		{
			var materials = model.Materials
				.Select( HandlerUtil.DescribeResourceReference )
				.ToArray();

			return new
			{
				count = materials.Length,
				items = materials
			};
		}
		catch ( Exception ex )
		{
			return new
			{
				count = 0,
				items = Array.Empty<object>(),
				error = ex.Message
			};
		}
	}

	private static string SafePath( Func<string> getter )
	{
		try
		{
			return getter() ?? "";
		}
		catch
		{
			return "";
		}
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

	private static string GetModelPath( BridgeRequest request )
	{
		var rawPath = HandlerUtil.GetString( request.Payload, "modelPath" );
		if ( string.IsNullOrWhiteSpace( rawPath ) )
			rawPath = HandlerUtil.GetString( request.Payload, "path" );

		if ( string.IsNullOrWhiteSpace( rawPath ) )
			throw new InvalidOperationException( "This action requires a modelPath or path payload property." );

		return OrientationOverrideStore.NormalizeModelPath( rawPath );
	}

	private static OrientationAngles ReadOrientationAngles( JsonElement payload, string propertyName, OrientationAngles fallback )
	{
		var source = payload;

		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( propertyName, out var nested ) )
		{
			if ( nested.ValueKind != JsonValueKind.Object )
				throw new InvalidOperationException( $"{propertyName} must be an object with pitch, yaw, and roll fields." );

			source = nested;
		}

		return new OrientationAngles
		{
			Pitch = HandlerUtil.GetFloat( source, "pitch", fallback.Pitch ),
			Yaw = HandlerUtil.GetFloat( source, "yaw", fallback.Yaw ),
			Roll = HandlerUtil.GetFloat( source, "roll", fallback.Roll )
		};
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
			throw new InvalidOperationException( "Resolved asset path escaped the project Assets directory." );

		return absolutePath;
	}

	private static string CreateVmatSource( string name, string shader, Color color )
	{
		return string.Join( Environment.NewLine, new[]
		{
			"// Created by sbox-agent-bridge",
			"\"Layer0\"",
			"{",
			$"\t\"shader\" \"{EscapeVmat( shader )}\"",
			"\t\"g_flAmbientOcclusionDirectDiffuse\" \"0.000000\"",
			"\t\"g_flAmbientOcclusionDirectSpecular\" \"0.000000\"",
			"\t\"TextureAmbientOcclusion\" \"materials/default/default_ao.tga\"",
			"\t\"g_flModelTintAmount\" \"1.000000\"",
			$"\t\"g_vColorTint\" \"{FormatColorVector( color )}\"",
			"\t\"TextureColor\" \"materials/default/default_color.tga\"",
			"\t\"g_flFadeExponent\" \"1.000000\"",
			"\t\"g_bFogEnabled\" \"1\"",
			"\t\"g_flMetalness\" \"0.000000\"",
			"\t\"TextureNormal\" \"materials/default/default_normal.tga\"",
			"\t\"g_flRoughnessScaleFactor\" \"1.000000\"",
			"\t\"TextureRoughness\" \"materials/default/default_rough.tga\"",
			"\t\"g_nScaleTexCoordUByModelScaleAxis\" \"0\"",
			"\t\"g_nScaleTexCoordVByModelScaleAxis\" \"0\"",
			"\t\"g_vTexCoordOffset\" \"[0.000 0.000]\"",
			"\t\"g_vTexCoordScale\" \"[1.000 1.000]\"",
			"\t\"g_vTexCoordScrollSpeed\" \"[0.000 0.000]\"",
			"}",
			""
		} );
	}

	private static string FormatColorVector( Color color )
	{
		return $"[{color.r:0.000000} {color.g:0.000000} {color.b:0.000000} {color.a:0.000000}]";
	}

	private static string EscapeVmat( string value )
	{
		return (value ?? "").Replace( "\\", "\\\\" ).Replace( "\"", "\\\"" );
	}
}
