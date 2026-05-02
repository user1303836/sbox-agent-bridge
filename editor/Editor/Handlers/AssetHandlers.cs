using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
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

	public static BridgeResponse ListTypes( BridgeRequest request )
	{
		var query = HandlerUtil.GetString( request.Payload, "query" );
		var onlyGameResources = HandlerUtil.GetBool( request.Payload, "onlyGameResources", false );
		var includeHidden = HandlerUtil.GetBool( request.Payload, "includeHidden", true );
		var maxResults = ClampInt( HandlerUtil.GetInt( request.Payload, "maxResults", 200 ), 1, 500 );

		var allTypes = AssetType.All
			.Where( type => includeHidden || !type.HiddenByDefault )
			.Where( type => !onlyGameResources || type.IsGameResource )
			.Where( type => string.IsNullOrWhiteSpace( query ) || MatchesAssetTypeQuery( type, query ) )
			.ToArray();

		var results = allTypes
			.OrderBy( type => type.Category ?? "" )
			.ThenBy( type => type.FriendlyName ?? "" )
			.Take( maxResults )
			.Select( DescribeAssetType )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Asset types listed",
			verified = new
			{
				query,
				onlyGameResources,
				includeHidden,
				count = results.Length,
				totalMatched = allTypes.Length,
				gameResourceCount = allTypes.Count( type => type.IsGameResource ),
				results
			}
		} );
	}

	public static BridgeResponse CloudPackages( BridgeRequest request )
	{
		var includeInstalled = HandlerUtil.GetBool( request.Payload, "includeInstalled", true );
		var includeReferenced = HandlerUtil.GetBool( request.Payload, "includeReferenced", true );
		var maxResults = ClampInt( HandlerUtil.GetInt( request.Payload, "maxResults", 100 ), 1, 500 );

		var installed = includeInstalled
			? SafePackageArray( () => AssetSystem.GetInstalledPackages() )
			: Array.Empty<Package>();

		var referenced = includeReferenced
			? SafePackageArray( () => AssetSystem.GetReferencedPackages() )
			: Array.Empty<Package>();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Cloud packages inspected",
			verified = new
			{
				includeInstalled,
				includeReferenced,
				installedCount = installed.Length,
				referencedCount = referenced.Length,
				installed = installed.Take( maxResults ).Select( DescribePackage ).ToArray(),
				referenced = referenced.Take( maxResults ).Select( DescribePackage ).ToArray(),
				truncated = installed.Length > maxResults || referenced.Length > maxResults
			}
		} );
	}

	public static BridgeResponse CreateResource( BridgeRequest request )
	{
		var requestedType = HandlerUtil.GetString( request.Payload, "assetType" );
		if ( string.IsNullOrWhiteSpace( requestedType ) )
			requestedType = HandlerUtil.GetRequiredString( request.Payload, "type" );

		var assetType = NormalizeAssetTypeExtension( requestedType );
		var relativePath = NormalizeAssetPath( HandlerUtil.GetRequiredString( request.Payload, "path" ) );
		var overwrite = HandlerUtil.GetBool( request.Payload, "overwrite", false );
		var normalizedPath = NormalizeAssetPath( EnsureExtension( relativePath, "." + assetType ) );
		var absolutePath = ResolveAssetPath( normalizedPath );
		var compiledPath = absolutePath + "_c";

		if ( File.Exists( absolutePath ) || File.Exists( compiledPath ) )
		{
			if ( !overwrite )
				throw new InvalidOperationException( $"Resource '{normalizedPath}' already exists. Pass overwrite:true to replace it." );

			File.Delete( absolutePath );
			File.Delete( compiledPath );
		}

		Directory.CreateDirectory( Path.GetDirectoryName( absolutePath ) ?? Project.Current.GetAssetsPath() );

		var asset = AssetSystem.CreateResource( assetType, absolutePath );
		if ( asset is null )
			throw new InvalidOperationException( $"AssetSystem.CreateResource('{assetType}', '{absolutePath}') returned null." );

		asset.Compile( true );
		AssetSystem.RegisterFile( absolutePath );

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameResource asset created",
			verified = new
			{
				requestedType,
				assetType,
				path = normalizedPath,
				asset = DescribeAssetDetails( asset )
			}
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

	public static BridgeResponse InspectMaterial( BridgeRequest request )
	{
		var path = GetMaterialPath( request );
		var asset = RequireAsset( path );
		var material = Material.Load( path );

		if ( material is null || !material.IsValid )
			throw new InvalidOperationException( $"Material '{path}' could not be loaded." );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Material inspected",
			verified = DescribeMaterial( path, asset, material )
		} );
	}

	public static BridgeResponse SetMaterialSourceProperty( BridgeRequest request )
	{
		var path = GetMaterialPath( request );
		var asset = RequireAsset( path );
		var sourceFile = ResolveMaterialSourceFile( path, asset );
		var property = HandlerUtil.GetRequiredString( request.Payload, "property" );

		if ( string.IsNullOrWhiteSpace( sourceFile ) || !File.Exists( sourceFile ) )
			throw new InvalidOperationException( $"Material '{path}' does not have a readable source file." );

		if ( request.Payload.ValueKind != JsonValueKind.Object || !request.Payload.TryGetProperty( "value", out var value ) )
			throw new InvalidOperationException( "set_material_source_property requires a value property." );

		var formattedValue = FormatMaterialSourceValue( value );
		var before = InspectMaterialSource( sourceFile );
		var replaced = UpsertVmatProperty( sourceFile, property, formattedValue );
		asset.Compile( true );

		var material = Material.Load( path );
		if ( material is null || !material.IsValid )
			throw new InvalidOperationException( $"Material '{path}' was updated but could not be reloaded." );

		return BridgeResponse.Success( request.Id, new
		{
			message = replaced ? "Material source property updated" : "Material source property inserted",
			verified = new
			{
				path,
				property,
				value = formattedValue,
				replaced,
				sourceFile,
				before,
				after = DescribeMaterial( path, asset, material )
			}
		} );
	}

	public static BridgeResponse PreviewModel( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "active" );
		var session = resolution.Session;
		var rawPath = HandlerUtil.GetString( request.Payload, "modelPath" );
		if ( string.IsNullOrWhiteSpace( rawPath ) )
			rawPath = HandlerUtil.GetString( request.Payload, "path" );

		if ( string.IsNullOrWhiteSpace( rawPath ) )
			throw new InvalidOperationException( "asset.preview_model requires a modelPath or path payload property." );

		var path = NormalizeAssetPath( rawPath );
		var materialPath = NormalizeAssetPath( HandlerUtil.GetString( request.Payload, "materialPath" ) );
		var width = ClampInt( HandlerUtil.GetInt( request.Payload, "width", 640 ), 64, 2048 );
		var height = ClampInt( HandlerUtil.GetInt( request.Payload, "height", 360 ), 64, 2048 );
		var name = SanitizeFileName( HandlerUtil.GetString( request.Payload, "name", "model-preview" ) );
		var scale = HandlerUtil.GetVector3( request.Payload, "scale" ) ?? Vector3.One;
		var pitch = HandlerUtil.GetFloat( request.Payload, "pitch", 0f );
		var yaw = HandlerUtil.GetFloat( request.Payload, "yaw", 35f );
		var roll = HandlerUtil.GetFloat( request.Payload, "roll", 0f );
		var model = Model.Load( path );

		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidOperationException( $"Model '{path}' could not be loaded." );

		Material? material = null;
		if ( !string.IsNullOrWhiteSpace( materialPath ) )
		{
			material = Material.Load( materialPath );
			if ( material is null || !material.IsValid )
				throw new InvalidOperationException( $"Material '{materialPath}' could not be loaded." );
		}

		var preview = EnsurePreviewRig( session.Scene );
		ConfigurePreviewRig( preview, model, material, scale, Rotation.From( pitch, yaw, roll ) );

		using var bitmap = new Sandbox.Bitmap( width, height, false );
		SetPreviewNotSaved( preview, false );
		preview.Camera.IsMainCamera = true;
		try
		{
			preview.Camera.RenderToBitmap( bitmap );
		}
		finally
		{
			preview.Camera.IsMainCamera = false;
			SetPreviewNotSaved( preview, true );
			preview.Root.Enabled = false;
		}

		var pixels = bitmap.GetPixels32();
		var luminance = AnalyzeLuminance( pixels );
		var pngBytes = bitmap.ToPng();
		var captureDirectory = Path.Combine( Path.GetTempPath(), "sbox-agent-bridge", "captures" );
		Directory.CreateDirectory( captureDirectory );

		var fileName = $"{DateTime.UtcNow:yyyyMMdd-HHmmss}-{name}-{Guid.NewGuid():N}.png";
		var filePath = Path.Combine( captureDirectory, fileName );
		File.WriteAllBytes( filePath, pngBytes );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Model preview captured",
			verified = new
			{
				path = filePath,
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				width,
				height,
				byteCount = pngBytes.Length,
				luminance,
				modelPath = path,
				materialPath,
				model = HandlerUtil.DescribeResourceReference( model ),
				material = material is null ? null : HandlerUtil.DescribeResourceReference( material ),
				previewRig = new
				{
					root = HandlerUtil.DescribeGameObject( preview.Root ),
					modelObject = HandlerUtil.DescribeGameObject( preview.ModelObject ),
					cameraObject = HandlerUtil.DescribeGameObject( preview.Camera.GameObject ),
					renderer = HandlerUtil.DescribeComponent( preview.Renderer ),
					camera = HandlerUtil.DescribeComponent( preview.Camera ),
					notSaved = true
				},
				camera = new
				{
					position = HandlerUtil.ToJson( preview.Camera.GameObject.WorldPosition ),
					rotation = HandlerUtil.ToJson( preview.Camera.GameObject.WorldRotation ),
					orthographic = preview.Camera.Orthographic,
					orthographicHeight = preview.Camera.OrthographicHeight
				},
				transform = new
				{
					scale = HandlerUtil.ToJson( scale ),
					rotation = new { pitch, yaw, roll }
				},
				bounds = new
				{
					model = HandlerUtil.DescribeBBox( model.Bounds ),
					render = HandlerUtil.DescribeBBox( model.RenderBounds )
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

	private static object DescribeMaterial( string path, Asset asset, Material material )
	{
		var sourceFile = ResolveMaterialSourceFile( path, asset );

		return new
		{
			path,
			asset = DescribeAssetDetails( asset ),
			material = HandlerUtil.DescribeResourceReference( material ),
			source = InspectMaterialSource( sourceFile )
		};
	}

	private static object InspectMaterialSource( string sourceFile )
	{
		if ( string.IsNullOrWhiteSpace( sourceFile ) || !File.Exists( sourceFile ) )
		{
			return new
			{
				sourceFile,
				exists = false,
				propertyCount = 0,
				properties = Array.Empty<object>(),
				textures = Array.Empty<object>(),
				colors = Array.Empty<object>(),
				scalars = Array.Empty<object>()
			};
		}

		var properties = File.ReadAllLines( sourceFile )
			.Select( TryParseVmatKeyValue )
			.Where( x => x is not null )
			.Select( x => x! )
			.ToArray();

		return new
		{
			sourceFile,
			exists = true,
			propertyCount = properties.Length,
			properties = properties.Select( DescribeMaterialProperty ).ToArray(),
			textures = properties.Where( x => x.Key.StartsWith( "Texture", StringComparison.OrdinalIgnoreCase ) ).Select( DescribeMaterialProperty ).ToArray(),
			colors = properties.Where( x => x.Key.Contains( "Color", StringComparison.OrdinalIgnoreCase ) || IsVectorValue( x.Value, 3 ) || IsVectorValue( x.Value, 4 ) ).Select( DescribeMaterialProperty ).ToArray(),
			scalars = properties.Where( x => x.Key.StartsWith( "g_fl", StringComparison.OrdinalIgnoreCase ) || x.Key.StartsWith( "g_n", StringComparison.OrdinalIgnoreCase ) || x.Key.StartsWith( "g_b", StringComparison.OrdinalIgnoreCase ) ).Select( DescribeMaterialProperty ).ToArray()
		};
	}

	private static string ResolveMaterialSourceFile( string path, Asset asset )
	{
		var sourceFile = SafePath( () => asset.GetSourceFile( false ) );
		if ( !string.IsNullOrWhiteSpace( sourceFile ) && File.Exists( sourceFile ) )
			return sourceFile;

		var resolved = ResolveAssetPath( path );
		if ( File.Exists( resolved ) )
			return resolved;

		if ( !string.IsNullOrWhiteSpace( asset.AbsolutePath ) && File.Exists( asset.AbsolutePath ) )
			return asset.AbsolutePath;

		return sourceFile;
	}

	private static MaterialProperty? TryParseVmatKeyValue( string line )
	{
		line = (line ?? "").Trim();
		if ( !line.StartsWith( "\"", StringComparison.Ordinal ) )
			return null;

		var firstEnd = line.IndexOf( '"', 1 );
		if ( firstEnd <= 1 )
			return null;

		var secondStart = line.IndexOf( '"', firstEnd + 1 );
		if ( secondStart < 0 )
			return null;

		var secondEnd = line.IndexOf( '"', secondStart + 1 );
		if ( secondEnd <= secondStart )
			return null;

		return new MaterialProperty
		{
			Key = line.Substring( 1, firstEnd - 1 ),
			Value = line.Substring( secondStart + 1, secondEnd - secondStart - 1 )
		};
	}

	private static object DescribeMaterialProperty( MaterialProperty property )
	{
		return new
		{
			key = property.Key,
			value = property.Value,
			kind = ClassifyMaterialProperty( property ),
			vector = ParseVectorValue( property.Value ),
			number = TryParseFloat( property.Value, out var number ) ? number : (float?)null
		};
	}

	private static string ClassifyMaterialProperty( MaterialProperty property )
	{
		if ( property.Key.StartsWith( "Texture", StringComparison.OrdinalIgnoreCase ) )
			return "texture";

		if ( property.Key.Contains( "Color", StringComparison.OrdinalIgnoreCase ) || IsVectorValue( property.Value, 3 ) || IsVectorValue( property.Value, 4 ) )
			return "colorOrVector";

		if ( TryParseFloat( property.Value, out _ ) )
			return "number";

		return "string";
	}

	private static bool IsVectorValue( string value, int expectedLength )
	{
		return ParseVectorValue( value ).Length == expectedLength;
	}

	private static float[] ParseVectorValue( string value )
	{
		value = (value ?? "").Trim();
		if ( !value.StartsWith( "[", StringComparison.Ordinal ) || !value.EndsWith( "]", StringComparison.Ordinal ) )
			return Array.Empty<float>();

		return value.Trim( '[', ']' )
			.Split( new[] { ' ', '\t', ',' }, StringSplitOptions.RemoveEmptyEntries )
			.Select( item => TryParseFloat( item, out var number ) ? number : float.NaN )
			.Where( number => !float.IsNaN( number ) )
			.ToArray();
	}

	private static bool TryParseFloat( string value, out float number )
	{
		return float.TryParse( value, NumberStyles.Float, CultureInfo.InvariantCulture, out number );
	}

	private static bool UpsertVmatProperty( string sourceFile, string property, string value )
	{
		var lines = File.ReadAllLines( sourceFile ).ToList();
		var replacement = $"\t\"{EscapeVmat( property )}\" \"{EscapeVmat( value )}\"";

		for ( var i = 0; i < lines.Count; i++ )
		{
			var parsed = TryParseVmatKeyValue( lines[i] );
			if ( parsed is not null && string.Equals( parsed.Key, property, StringComparison.Ordinal ) )
			{
				lines[i] = replacement;
				File.WriteAllLines( sourceFile, lines, new System.Text.UTF8Encoding( false ) );
				return true;
			}
		}

		var insertAt = lines.FindLastIndex( line => line.Trim() == "}" );
		if ( insertAt < 0 )
			insertAt = lines.Count;

		lines.Insert( insertAt, replacement );
		File.WriteAllLines( sourceFile, lines, new System.Text.UTF8Encoding( false ) );
		return false;
	}

	private static string FormatMaterialSourceValue( JsonElement value )
	{
		return value.ValueKind switch
		{
			JsonValueKind.True => "1",
			JsonValueKind.False => "0",
			JsonValueKind.Number when value.TryGetInt32( out var intValue ) => intValue.ToString( CultureInfo.InvariantCulture ),
			JsonValueKind.Number when value.TryGetSingle( out var floatValue ) => floatValue.ToString( "0.######", CultureInfo.InvariantCulture ),
			JsonValueKind.String => value.GetString() ?? "",
			JsonValueKind.Array => FormatNumberArray( value.EnumerateArray().ToArray() ),
			JsonValueKind.Object => FormatMaterialObjectValue( value ),
			_ => throw new InvalidOperationException( "Material source property values support bool, number, string, numeric arrays, resource path objects, color objects, and vector objects." )
		};
	}

	private static string FormatMaterialObjectValue( JsonElement value )
	{
		var path = HandlerUtil.GetString( value, "path" );
		if ( string.IsNullOrWhiteSpace( path ) )
			path = HandlerUtil.GetString( value, "resourcePath" );

		if ( !string.IsNullOrWhiteSpace( path ) )
			return NormalizeAssetPath( path );

		if ( value.TryGetProperty( "r", out var r ) || value.TryGetProperty( "g", out _ ) || value.TryGetProperty( "b", out _ ) )
		{
			var color = new[]
			{
				ReadFloat( value, "r", 1f ),
				ReadFloat( value, "g", 1f ),
				ReadFloat( value, "b", 1f ),
				ReadFloat( value, "a", 1f )
			};
			return FormatNumberArray( color );
		}

		if ( value.TryGetProperty( "x", out _ ) || value.TryGetProperty( "y", out _ ) || value.TryGetProperty( "z", out _ ) || value.TryGetProperty( "w", out _ ) )
		{
			var numbers = new List<float>
			{
				ReadFloat( value, "x", 0f ),
				ReadFloat( value, "y", 0f )
			};

			if ( value.TryGetProperty( "z", out _ ) )
				numbers.Add( ReadFloat( value, "z", 0f ) );

			if ( value.TryGetProperty( "w", out _ ) )
				numbers.Add( ReadFloat( value, "w", 0f ) );

			return FormatNumberArray( numbers.ToArray() );
		}

		throw new InvalidOperationException( "Unsupported material source object value. Use {path}, {resourcePath}, {r,g,b,a}, or {x,y,z,w}." );
	}

	private static float ReadFloat( JsonElement value, string property, float fallback )
	{
		if ( value.ValueKind == JsonValueKind.Object && value.TryGetProperty( property, out var element ) && element.TryGetSingle( out var result ) )
			return result;

		return fallback;
	}

	private static string FormatNumberArray( JsonElement[] elements )
	{
		return FormatNumberArray( elements.Select( element =>
		{
			if ( !element.TryGetSingle( out var number ) )
				throw new InvalidOperationException( "Material source numeric arrays can only contain numbers." );

			return number;
		} ).ToArray() );
	}

	private static string FormatNumberArray( IReadOnlyList<float> numbers )
	{
		return "[" + string.Join( " ", numbers.Select( number => number.ToString( "0.###", CultureInfo.InvariantCulture ) ) ) + "]";
	}

	private static PreviewRig EnsurePreviewRig( Scene scene )
	{
		var root = HandlerUtil.WalkSceneObjects( scene ).FirstOrDefault( go => go.Name == "__AgentBridgePreviewRig" );
		if ( root is null )
		{
			root = scene.CreateObject( true );
			root.Name = "__AgentBridgePreviewRig";
		}

		root.Enabled = true;
		root.Flags |= GameObjectFlags.NotSaved;
		root.WorldPosition = new Vector3( 0f, 0f, 4096f );

		var modelObject = EnsureChild( scene, root, "__AgentBridgePreviewModel" );
		var cameraObject = EnsureChild( scene, root, "__AgentBridgePreviewCamera" );
		var ambientObject = EnsureChild( scene, root, "__AgentBridgePreviewAmbient" );
		var keyObject = EnsureChild( scene, root, "__AgentBridgePreviewKey" );

		var renderer = modelObject.Components.Get<ModelRenderer>() ?? modelObject.Components.Create<ModelRenderer>();
		var camera = cameraObject.Components.Get<CameraComponent>() ?? cameraObject.Components.Create<CameraComponent>();
		var ambient = ambientObject.Components.Get<AmbientLight>() ?? ambientObject.Components.Create<AmbientLight>();
		var key = keyObject.Components.Get<DirectionalLight>() ?? keyObject.Components.Create<DirectionalLight>();

		return new PreviewRig
		{
			Root = root,
			ModelObject = modelObject,
			Renderer = renderer,
			Camera = camera,
			Ambient = ambient,
			KeyLight = key,
			KeyObject = keyObject
		};
	}

	private static GameObject EnsureChild( Scene scene, GameObject root, string name )
	{
		var child = root.Children.FirstOrDefault( go => go.Name == name );
		if ( child is null )
		{
			child = scene.CreateObject( true );
			child.Name = name;
			child.SetParent( root, false );
		}

		child.Enabled = true;
		child.Flags |= GameObjectFlags.NotSaved;
		return child;
	}

	private static void ConfigurePreviewRig( PreviewRig preview, Model model, Material? material, Vector3 scale, Rotation rotation )
	{
		var localBounds = model.RenderBounds.Transform( new Transform( Vector3.Zero, rotation, scale ) );
		var size = localBounds.Size;
		var maxExtent = MathF.Max( 16f, MathF.Max( size.x, MathF.Max( size.y, size.z ) ) );
		var groundOffset = -localBounds.Mins.z;
		var target = preview.Root.WorldPosition + new Vector3( 0f, 0f, groundOffset + size.z * 0.5f );
		var cameraDistance = maxExtent * 2.2f + 96f;
		var cameraPosition = target + new Vector3( -cameraDistance, -cameraDistance, cameraDistance * 0.68f );

		preview.ModelObject.LocalPosition = Vector3.Up * groundOffset;
		preview.ModelObject.LocalRotation = rotation;
		preview.ModelObject.LocalScale = scale;
		preview.Renderer.Enabled = false;
		preview.Renderer.Model = model;
		preview.Renderer.MaterialOverride = material;
		preview.Renderer.Enabled = true;

		preview.Camera.GameObject.WorldPosition = cameraPosition;
		preview.Camera.GameObject.WorldRotation = Rotation.LookAt( (target - cameraPosition).Normal, Vector3.Up );
		preview.Camera.IsMainCamera = false;
		preview.Camera.Orthographic = true;
		preview.Camera.OrthographicHeight = maxExtent * 1.45f + 32f;
		preview.Camera.FieldOfView = 45f;
		preview.Camera.EnablePostProcessing = false;

		preview.Ambient.Color = new Color( 0.55f, 0.57f, 0.62f, 1f );
		preview.KeyObject.WorldRotation = Rotation.From( 58f, 35f, 0f );
		preview.KeyLight.LightColor = new Color( 0.95f, 0.92f, 0.86f, 1f );
		preview.KeyLight.SkyColor = new Color( 0.20f, 0.24f, 0.30f, 1f );
		preview.KeyLight.Shadows = false;
	}

	private static void SetPreviewNotSaved( PreviewRig preview, bool notSaved )
	{
		SetNotSaved( preview.Root, notSaved );
		SetNotSaved( preview.ModelObject, notSaved );
		SetNotSaved( preview.Camera.GameObject, notSaved );
		SetNotSaved( preview.Ambient.GameObject, notSaved );
		SetNotSaved( preview.KeyObject, notSaved );
	}

	private static void SetNotSaved( GameObject go, bool notSaved )
	{
		if ( notSaved )
		{
			go.Flags |= GameObjectFlags.NotSaved;
		}
		else
		{
			go.Flags &= ~GameObjectFlags.NotSaved;
		}
	}

	private static object AnalyzeLuminance( Color32[] pixels )
	{
		if ( pixels.Length == 0 )
		{
			return new
			{
				sampleCount = 0,
				average = 0d,
				min = 0d,
				max = 0d,
				darkPixelRatio = 0d,
				brightPixelRatio = 0d
			};
		}

		double total = 0d;
		double min = 1d;
		double max = 0d;
		var darkPixels = 0;
		var brightPixels = 0;

		foreach ( var pixel in pixels )
		{
			var luminance = ((0.2126d * pixel.r) + (0.7152d * pixel.g) + (0.0722d * pixel.b)) / 255d;
			total += luminance;

			if ( luminance < min )
				min = luminance;

			if ( luminance > max )
				max = luminance;

			if ( luminance < 0.08d )
				darkPixels++;

			if ( luminance > 0.85d )
				brightPixels++;
		}

		return new
		{
			sampleCount = pixels.Length,
			average = Math.Round( total / pixels.Length, 4 ),
			min = Math.Round( min, 4 ),
			max = Math.Round( max, 4 ),
			darkPixelRatio = Math.Round( (double)darkPixels / pixels.Length, 4 ),
			brightPixelRatio = Math.Round( (double)brightPixels / pixels.Length, 4 )
		};
	}

	private static int ClampInt( int value, int min, int max )
	{
		if ( value < min )
			return min;

		if ( value > max )
			return max;

		return value;
	}

	private static string SanitizeFileName( string value )
	{
		var chars = (value ?? "asset")
			.Select( ch => char.IsLetterOrDigit( ch ) || ch is '-' or '_' ? ch : '_' )
			.ToArray();

		var result = new string( chars ).Trim( '_' );
		return string.IsNullOrWhiteSpace( result ) ? "asset" : result;
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

	private static bool MatchesAssetTypeQuery( AssetType assetType, string query )
	{
		return (assetType.FriendlyName ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) ||
			(assetType.FileExtension ?? "").Contains( query.TrimStart( '.' ), StringComparison.OrdinalIgnoreCase ) ||
			(assetType.Category ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) ||
			(assetType.ResourceType?.Name ?? "").Contains( query, StringComparison.OrdinalIgnoreCase ) ||
			(assetType.ResourceType?.FullName ?? "").Contains( query, StringComparison.OrdinalIgnoreCase );
	}

	private static Asset RequireAsset( string path )
	{
		var asset = AssetSystem.FindByPath( path );
		if ( asset is null && path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			asset = AssetSystem.FindByPath( path.Substring( "assets/".Length ) );

		if ( asset is null )
		{
			var absolutePath = ResolveAssetPath( path );
			if ( File.Exists( absolutePath ) )
				asset = AssetSystem.RegisterFile( absolutePath ) ?? AssetSystem.FindByPath( path );
		}

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

	private static object DescribeAssetType( AssetType assetType )
	{
		return new
		{
			friendlyName = assetType.FriendlyName,
			fileExtension = assetType.FileExtension,
			fileExtensions = ReadStringEnumerableProperty( assetType, "FileExtensions" ),
			category = assetType.Category ?? "",
			hiddenByDefault = assetType.HiddenByDefault,
			isSimpleAsset = assetType.IsSimpleAsset,
			hasDependencies = assetType.HasDependencies,
			prefersIconThumbnail = assetType.PrefersIconThumbnail,
			isGameResource = assetType.IsGameResource,
			resourceType = assetType.ResourceType?.FullName ?? "",
			flags = assetType.Flags.ToString(),
			hasEditor = assetType.HasEditor
		};
	}

	private static object DescribePackage( Package package )
	{
		var references = ReadStringEnumerableProperty( package, "PackageReferences" );
		var editorReferences = ReadStringEnumerableProperty( package, "EditorReferences" );

		return new
		{
			fullIdent = ReadStringProperty( package, "FullIdent" ),
			ident = ReadStringProperty( package, "Ident" ),
			org = DescribePackageOrganization( ReadObjectProperty( package, "Org" ) ),
			title = ReadStringProperty( package, "Title" ),
			summary = ReadStringProperty( package, "Summary" ),
			typeName = ReadStringProperty( package, "TypeName" ),
			packageType = ReadStringProperty( package, "PackageType" ),
			isRemote = ReadBoolProperty( package, "IsRemote" ),
			isPublic = ReadBoolProperty( package, "Public" ),
			archived = ReadBoolProperty( package, "Archived" ),
			canEdit = ReadBoolProperty( package, "CanEdit" ),
			fileSize = ReadStringProperty( package, "FileSize" ),
			url = ReadStringProperty( package, "Url" ),
			updated = ReadStringProperty( package, "Updated" ),
			created = ReadStringProperty( package, "Created" ),
			packageReferenceCount = references.Length,
			packageReferences = references.Take( 20 ).ToArray(),
			editorReferenceCount = editorReferences.Length,
			editorReferences = editorReferences.Take( 20 ).ToArray()
		};
	}

	private static object? DescribePackageOrganization( object? org )
	{
		if ( org is null )
			return null;

		return new
		{
			ident = ReadStringProperty( org, "Ident" ),
			title = ReadStringProperty( org, "Title" )
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

	private static Package[] SafePackageArray( Func<IEnumerable<Package>> getter )
	{
		try
		{
			return getter()?.Where( package => package is not null ).ToArray() ?? Array.Empty<Package>();
		}
		catch
		{
			return Array.Empty<Package>();
		}
	}

	private static string NormalizeAssetTypeExtension( string assetType )
	{
		assetType = (assetType ?? "").Trim().TrimStart( '.' );

		if ( string.IsNullOrWhiteSpace( assetType ) )
			throw new InvalidOperationException( "Asset type extension cannot be empty." );

		if ( assetType.Contains( '/' ) || assetType.Contains( '\\' ) || assetType.Contains( "..", StringComparison.Ordinal ) )
			throw new InvalidOperationException( "Asset type extension cannot contain path separators or '..'." );

		return assetType;
	}

	private static object? ReadObjectProperty( object instance, string name )
	{
		try
		{
			return instance.GetType().GetProperty( name )?.GetValue( instance );
		}
		catch
		{
			return null;
		}
	}

	private static string ReadStringProperty( object instance, string name )
	{
		var value = ReadObjectProperty( instance, name );
		return value?.ToString() ?? "";
	}

	private static bool? ReadBoolProperty( object instance, string name )
	{
		var value = ReadObjectProperty( instance, name );
		return value switch
		{
			bool boolValue => boolValue,
			null => null,
			_ when bool.TryParse( value.ToString(), out var parsed ) => parsed,
			_ => null
		};
	}

	private static string[] ReadStringEnumerableProperty( object instance, string name )
	{
		var value = ReadObjectProperty( instance, name );
		if ( value is null )
			return Array.Empty<string>();

		if ( value is string stringValue )
			return new[] { stringValue };

		if ( value is IEnumerable enumerable )
		{
			return enumerable
				.Cast<object?>()
				.Select( item => item?.ToString() ?? "" )
				.Where( item => !string.IsNullOrWhiteSpace( item ) )
				.ToArray();
		}

		return new[] { value.ToString() ?? "" };
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

	private static string GetMaterialPath( BridgeRequest request )
	{
		var rawPath = HandlerUtil.GetString( request.Payload, "materialPath" );
		if ( string.IsNullOrWhiteSpace( rawPath ) )
			rawPath = HandlerUtil.GetString( request.Payload, "path" );

		if ( string.IsNullOrWhiteSpace( rawPath ) )
			throw new InvalidOperationException( "This action requires a materialPath or path payload property." );

		return NormalizeAssetPath( rawPath );
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

	private sealed class MaterialProperty
	{
		public string Key { get; init; } = "";
		public string Value { get; init; } = "";
	}

	private sealed class PreviewRig
	{
		public GameObject Root { get; init; }
		public GameObject ModelObject { get; init; }
		public ModelRenderer Renderer { get; init; }
		public CameraComponent Camera { get; init; }
		public AmbientLight Ambient { get; init; }
		public DirectionalLight KeyLight { get; init; }
		public GameObject KeyObject { get; init; }
	}
}
