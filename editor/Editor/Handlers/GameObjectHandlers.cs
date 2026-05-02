using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Editor;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class GameObjectHandlers
{
	public static BridgeResponse Get( BridgeRequest request )
	{
		var session = HandlerUtil.RequireTargetSession( request.Payload );
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject read",
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse Create( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var name = HandlerUtil.GetString( request.Payload, "name", "Agent Object" );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );
		var parent = HandlerUtil.GetOptionalGameObject( session.Scene, request.Payload );
		var keepWorldPosition = HandlerUtil.GetBool( request.Payload, "keepWorldPosition", true );

		GameObject go;

		var undo = session.UndoScope( "Agent Bridge: Create GameObject" ).WithGameObjectCreations();

		if ( parent is not null )
			undo.WithGameObjectChanges( parent, GameObjectUndoFlags.Children );

		using ( undo.Push() )
		{
			go = session.Scene.CreateObject( true );
			go.Name = name;
			go.MakeNameUnique();

			if ( position.HasValue )
				go.WorldPosition = position.Value;

			if ( parent is not null )
				go.SetParent( parent, keepWorldPosition );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject created",
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse Rename( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var name = HandlerUtil.GetRequiredString( request.Payload, "name" );
		var makeUnique = HandlerUtil.GetBool( request.Payload, "makeUnique", true );
		var previous = HandlerUtil.DescribeGameObject( go );

		using ( session.UndoScope( "Agent Bridge: Rename GameObject" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push() )
		{
			go.Name = name;

			if ( makeUnique )
				go.MakeNameUnique();
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject renamed",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse SetTransform( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );
		var rotation = HandlerUtil.GetRotation( request.Payload, "rotation" );
		var scale = HandlerUtil.GetVector3( request.Payload, "scale" );
		var previous = HandlerUtil.DescribeGameObject( go );

		if ( !position.HasValue && !rotation.HasValue && !scale.HasValue )
			throw new System.InvalidOperationException( "Set transform requires at least one of position, rotation, or scale." );

		using ( session.UndoScope( "Agent Bridge: Set GameObject Transform" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push() )
		{
			if ( position.HasValue )
				go.WorldPosition = position.Value;

			if ( rotation.HasValue )
				go.WorldRotation = rotation.Value;

			if ( scale.HasValue )
				go.WorldScale = scale.Value;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject transform set",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse SetEnabled( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var enabled = HandlerUtil.GetRequiredBool( request.Payload, "enabled" );
		var previous = HandlerUtil.DescribeGameObject( go );

		using ( session.UndoScope( "Agent Bridge: Set GameObject Enabled" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push() )
		{
			go.Enabled = enabled;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject enabled state set",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse Destroy( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var id = go.Id.ToString();
		object previous;

		try
		{
			previous = HandlerUtil.DescribeGameObject( go );
		}
		catch ( System.Exception ex )
		{
			throw new System.InvalidOperationException( $"Failed to describe GameObject before destroy: {ex.Message}", ex );
		}

		using ( session.UndoScope( "Agent Bridge: Destroy GameObject" ).WithGameObjectDestructions( go ).Push() )
		{
			go.Destroy();
		}

		session.Scene.ProcessDeletes();

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject destroyed",
			previous,
			verified = HandlerUtil.DescribeDestroyedGameObject( session.Scene, id )
		} );
	}

	public static BridgeResponse Duplicate( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var source = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var name = HandlerUtil.GetString( request.Payload, "name" );
		var position = HandlerUtil.GetVector3( request.Payload, "position" );
		var offset = HandlerUtil.GetVector3( request.Payload, "offset" );
		GameObject clone;

		using ( session.UndoScope( "Agent Bridge: Duplicate GameObject" ).WithGameObjectCreations().Push() )
		{
			clone = session.Scene.CreateObject( source.Enabled );

			if ( string.IsNullOrWhiteSpace( name ) )
				clone.Name = $"{source.Name} Copy";
			else
				clone.Name = name;

			clone.MakeNameUnique();
			clone.WorldPosition = source.WorldPosition;
			clone.WorldRotation = source.WorldRotation;
			clone.WorldScale = source.WorldScale;

			if ( source.Parent is not null )
				clone.SetParent( source.Parent, true );

			if ( position.HasValue )
				clone.WorldPosition = position.Value;
			else if ( offset.HasValue )
				clone.WorldPosition += offset.Value;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject shallow duplicated",
			shallow = true,
			copiedComponents = false,
			source = HandlerUtil.DescribeGameObject( source ),
			verified = HandlerUtil.DescribeGameObject( clone )
		} );
	}

	public static BridgeResponse Reparent( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload );
		var parent = HandlerUtil.GetOptionalGameObject( session.Scene, request.Payload );
		var keepWorldPosition = HandlerUtil.GetBool( request.Payload, "keepWorldPosition", true );
		var previous = HandlerUtil.DescribeGameObject( go );

		if ( parent is not null )
		{
			if ( parent.Id == go.Id )
				throw new System.InvalidOperationException( "A GameObject cannot be parented to itself." );

			var ancestor = parent.Parent;

			while ( ancestor is not null )
			{
				if ( ancestor.Id == go.Id )
					throw new System.InvalidOperationException( "A GameObject cannot be parented under one of its own descendants." );

				ancestor = ancestor.Parent;
			}
		}

		using ( session.UndoScope( "Agent Bridge: Reparent GameObject" ).WithGameObjectChanges( go, GameObjectUndoFlags.All ).Push() )
		{
			go.SetParent( parent!, keepWorldPosition );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "GameObject reparented",
			previous,
			verified = HandlerUtil.DescribeGameObject( go )
		} );
	}

	public static BridgeResponse PlaceAsset( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var modelPath = OrientationOverrideStore.NormalizeModelPath( HandlerUtil.GetRequiredString( request.Payload, "modelPath" ) );
		var model = Model.Load( modelPath );

		if ( model is null || !model.IsValid || model.IsError )
			throw new InvalidOperationException( $"Model '{modelPath}' could not be loaded." );

		var materialPath = NormalizeAssetPath( HandlerUtil.GetString( request.Payload, "materialPath" ) );
		Material? material = null;

		if ( !string.IsNullOrWhiteSpace( materialPath ) )
		{
			material = Material.Load( materialPath );

			if ( material is null || !material.IsValid )
				throw new InvalidOperationException( $"Material '{materialPath}' could not be loaded." );
		}

		var orientationOverride = OrientationOverrideStore.Get( modelPath );
		var requireOrientationOverride = HandlerUtil.GetBool( request.Payload, "requireOrientationOverride", false );

		if ( orientationOverride is null && requireOrientationOverride )
			throw new InvalidOperationException( $"No orientation override exists for '{modelPath}'." );

		var overrideSource = orientationOverride is null ? "fallback-as-imported" : "stored-override";
		var baseRotation = orientationOverride?.BaseRotation ?? new OrientationAngles();

		if ( HasObjectProperty( request.Payload, "baseRotation" ) )
		{
			baseRotation = ReadOrientationAngles( request.Payload, "baseRotation", baseRotation );
			overrideSource = orientationOverride is null ? "payload-base-rotation" : "payload-base-rotation-over-stored-override";
		}

		var yaw = HandlerUtil.GetFloat( request.Payload, "yaw", 0f );
		var alignToGround = HandlerUtil.GetBool( request.Payload, "alignToGround", true );
		var position = HandlerUtil.GetVector3( request.Payload, "position" ) ?? Vector3.Zero;
		var scale = HandlerUtil.GetVector3( request.Payload, "scale" ) ?? Vector3.One;
		var parent = HandlerUtil.GetOptionalGameObject( session.Scene, request.Payload );
		var keepWorldPosition = HandlerUtil.GetBool( request.Payload, "keepWorldPosition", true );
		var name = HandlerUtil.GetString( request.Payload, "name", DefaultObjectName( modelPath ) );
		var rotation = HandlerUtil.ToRotation( baseRotation, yaw );
		var calculatedGroundOffset = HandlerUtil.CalculateGroundOffsetZ( model.RenderBounds, rotation, scale );
		var finalPosition = alignToGround ? position + Vector3.Up * calculatedGroundOffset : position;
		var predictedWorldBounds = model.RenderBounds.Transform( new Transform( finalPosition, rotation, scale ) );
		var rendererType = HandlerUtil.FindComponentType( nameof( ModelRenderer ) )
			?? throw new InvalidOperationException( "Could not resolve ModelRenderer component type." );

		GameObject go;
		ModelRenderer renderer;

		var undo = session.UndoScope( "Agent Bridge: Place Asset" )
			.WithGameObjectCreations()
			.WithComponentCreations();

		if ( parent is not null )
			undo.WithGameObjectChanges( parent, GameObjectUndoFlags.Children );

		using ( undo.Push() )
		{
			go = session.Scene.CreateObject( true );
			go.Name = name;
			go.MakeNameUnique();
			go.WorldPosition = finalPosition;
			go.WorldRotation = rotation;
			go.WorldScale = scale;

			if ( parent is not null )
				go.SetParent( parent, keepWorldPosition );

			renderer = go.Components.Create( rendererType, false ) as ModelRenderer
				?? throw new InvalidOperationException( "Created renderer was not a ModelRenderer." );
			renderer.Model = model;

			if ( material is not null )
				renderer.MaterialOverride = material;

			renderer.Enabled = true;
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Asset placed",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				component = HandlerUtil.DescribeComponent( renderer ),
				model = HandlerUtil.DescribeResourceReference( model ),
				material = material is null ? null : HandlerUtil.DescribeResourceReference( material ),
				placement = new
				{
					requestedPosition = HandlerUtil.ToJson( position ),
					finalPosition = HandlerUtil.ToJson( finalPosition ),
					scale = HandlerUtil.ToJson( scale ),
					yaw,
					baseRotation,
					finalRotation = HandlerUtil.ToJson( rotation ),
					alignToGround,
					calculatedGroundOffsetZ = calculatedGroundOffset,
					storedGroundOffsetZ = orientationOverride?.GroundOffsetZ,
					orientationSource = overrideSource,
					requireOrientationOverride,
					predictedWorldBounds = HandlerUtil.DescribeBBox( predictedWorldBounds ),
					readBackBounds = HandlerUtil.DescribeBBox( go.GetBounds() )
				},
				orientationOverride = orientationOverride is null ? null : OrientationOverrideStore.DescribeRecord( orientationOverride )
			}
		} );
	}

	private static bool HasObjectProperty( JsonElement payload, string name )
	{
		return payload.ValueKind == JsonValueKind.Object &&
			payload.TryGetProperty( name, out var value ) &&
			value.ValueKind == JsonValueKind.Object;
	}

	private static OrientationAngles ReadOrientationAngles( JsonElement payload, string propertyName, OrientationAngles fallback )
	{
		if ( payload.ValueKind != JsonValueKind.Object || !payload.TryGetProperty( propertyName, out var source ) )
			return fallback;

		if ( source.ValueKind != JsonValueKind.Object )
			throw new InvalidOperationException( $"{propertyName} must be an object with pitch, yaw, and roll fields." );

		return new OrientationAngles
		{
			Pitch = HandlerUtil.GetFloat( source, "pitch", fallback.Pitch ),
			Yaw = HandlerUtil.GetFloat( source, "yaw", fallback.Yaw ),
			Roll = HandlerUtil.GetFloat( source, "roll", fallback.Roll )
		};
	}

	private static string DefaultObjectName( string modelPath )
	{
		var fileName = modelPath.Split( '/' ).LastOrDefault() ?? "Placed Asset";
		return Path.GetFileNameWithoutExtension( fileName );
	}

	private static string NormalizeAssetPath( string path )
	{
		path = (path ?? "").Replace( '\\', '/' ).Trim().TrimStart( '/' );

		if ( path.StartsWith( "assets/", StringComparison.OrdinalIgnoreCase ) )
			path = path["assets/".Length..];

		if ( path.Split( '/' ).Any( part => part == ".." ) )
			throw new InvalidOperationException( "Asset path cannot contain '..' segments." );

		return path;
	}
}
