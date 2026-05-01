using System;
using System.Linq;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class PhysicsHandlers
{
	public static BridgeResponse Inspect( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "active" );
		var go = HandlerUtil.RequireGameObject( resolution.Session.Scene, request.Payload, "gameObjectId" );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Physics components inspected",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				gameObject = HandlerUtil.DescribeGameObject( go ),
				rigidbodies = go.Components.GetAll().OfType<Rigidbody>().Select( DescribeRigidbody ).ToArray(),
				colliders = go.Components.GetAll().OfType<Collider>().Select( DescribeCollider ).ToArray(),
				joints = go.Components.GetAll().OfType<Joint>().Select( DescribeJoint ).ToArray()
			}
		} );
	}

	public static BridgeResponse AddPhysics( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var body = go.Components.Get<Rigidbody>();

		if ( body is null )
		{
			using ( session.UndoScope( "Agent Bridge: Add Physics" ).WithComponentCreations().Push() )
			{
				body = go.Components.Create<Rigidbody>();
				ConfigureRigidbody( body, request );
			}
		}
		else
		{
			using ( session.UndoScope( "Agent Bridge: Add Physics" ).WithComponentChanges( body ).Push() )
			{
				ConfigureRigidbody( body, request );
			}
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Physics body added",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				component = HandlerUtil.DescribeComponent( body )
			}
		} );
	}

	public static BridgeResponse AddCollider( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var type = HandlerUtil.GetString( request.Payload, "type", "box" ).ToLowerInvariant();
		Collider collider;

		using ( session.UndoScope( "Agent Bridge: Add Collider" ).WithComponentCreations().Push() )
		{
			collider = type switch
			{
				"box" => ConfigureBox( go.Components.Create<BoxCollider>(), request ),
				"sphere" => ConfigureSphere( go.Components.Create<SphereCollider>(), request ),
				"capsule" => ConfigureCapsule( go.Components.Create<CapsuleCollider>(), request ),
				_ => throw new InvalidOperationException( "Collider type must be one of: box, sphere, capsule." )
			};

			collider.Static = HandlerUtil.GetBool( request.Payload, "static", collider.Static );
			collider.IsTrigger = HandlerUtil.GetBool( request.Payload, "trigger", collider.IsTrigger );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Collider added",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				component = HandlerUtil.DescribeComponent( collider )
			}
		} );
	}

	public static BridgeResponse AddJoint( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var target = HandlerUtil.GetOptionalGameObject( session.Scene, request.Payload, "targetGameObjectId" );
		var type = HandlerUtil.GetString( request.Payload, "type", "fixed" ).ToLowerInvariant();
		Joint joint;

		using ( session.UndoScope( "Agent Bridge: Add Joint" ).WithComponentCreations().Push() )
		{
			joint = type switch
			{
				"fixed" => go.Components.Create<FixedJoint>(),
				"hinge" => go.Components.Create<HingeJoint>(),
				"spring" => go.Components.Create<SpringJoint>(),
				"ball" => go.Components.Create<BallJoint>(),
				"slider" => go.Components.Create<SliderJoint>(),
				_ => throw new InvalidOperationException( "Joint type must be one of: fixed, hinge, spring, ball, slider." )
			};

			joint.EnableCollision = HandlerUtil.GetBool( request.Payload, "enableCollision", joint.EnableCollision );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Joint added",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				target = target is null ? null : HandlerUtil.DescribeGameObject( target ),
				component = HandlerUtil.DescribeComponent( joint ),
				notes = target is null ? "" : "Joint target read-back is exposed, but direct target assignment is not wired in v0 because Joint.Object2 is read-only in the editor API."
			}
		} );
	}

	public static BridgeResponse Raycast( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var from = HandlerUtil.GetVector3( request.Payload, "from" ) ?? throw new InvalidOperationException( "raycast requires a from vector." );
		var to = HandlerUtil.GetVector3( request.Payload, "to" ) ?? throw new InvalidOperationException( "raycast requires a to vector." );
		var trace = session.Scene.Trace.Ray( from, to );

		if ( HandlerUtil.GetBool( request.Payload, "renderMeshes", false ) )
			trace = trace.UseRenderMeshes( true );

		var result = trace.Run();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Scene raycast completed",
			verified = DescribeTraceResult( result )
		} );
	}

	private static BoxCollider ConfigureBox( BoxCollider collider, BridgeRequest request )
	{
		collider.Scale = HandlerUtil.GetVector3( request.Payload, "scale" ) ?? collider.Scale;
		collider.Center = HandlerUtil.GetVector3( request.Payload, "center" ) ?? collider.Center;
		return collider;
	}

	private static SphereCollider ConfigureSphere( SphereCollider collider, BridgeRequest request )
	{
		collider.Radius = HandlerUtil.GetFloat( request.Payload, "radius", collider.Radius );
		collider.Center = HandlerUtil.GetVector3( request.Payload, "center" ) ?? collider.Center;
		return collider;
	}

	private static CapsuleCollider ConfigureCapsule( CapsuleCollider collider, BridgeRequest request )
	{
		collider.Radius = HandlerUtil.GetFloat( request.Payload, "radius", collider.Radius );
		collider.Start = HandlerUtil.GetVector3( request.Payload, "start" ) ?? collider.Start;
		collider.End = HandlerUtil.GetVector3( request.Payload, "end" ) ?? collider.End;
		return collider;
	}

	private static object DescribeTraceResult( SceneTraceResult result )
	{
		return new
		{
			hit = result.Hit,
			startedSolid = result.StartedSolid,
			fraction = result.Fraction,
			distance = result.Distance,
			startPosition = HandlerUtil.ToJson( result.StartPosition ),
			endPosition = HandlerUtil.ToJson( result.EndPosition ),
			hitPosition = HandlerUtil.ToJson( result.HitPosition ),
			normal = HandlerUtil.ToJson( result.Normal ),
			gameObject = result.GameObject is null ? null : HandlerUtil.DescribeGameObject( result.GameObject ),
			component = result.Component is null ? null : HandlerUtil.DescribeComponent( result.Component ),
			collider = result.Collider is null ? null : HandlerUtil.DescribeComponent( result.Collider )
		};
	}

	private static object DescribeRigidbody( Rigidbody body )
	{
		return new
		{
			component = HandlerUtil.DescribeComponent( body ),
			gravity = body.Gravity,
			motionEnabled = body.MotionEnabled,
			massOverride = body.MassOverride
		};
	}

	private static object DescribeCollider( Collider collider )
	{
		return new
		{
			component = HandlerUtil.DescribeComponent( collider ),
			staticCollider = collider.Static,
			isTrigger = collider.IsTrigger,
			shape = DescribeColliderShape( collider )
		};
	}

	private static object DescribeColliderShape( Collider collider )
	{
		return collider switch
		{
			BoxCollider box => new
			{
				type = "box",
				scale = HandlerUtil.ToJson( box.Scale ),
				center = HandlerUtil.ToJson( box.Center )
			},
			SphereCollider sphere => new
			{
				type = "sphere",
				radius = sphere.Radius,
				center = HandlerUtil.ToJson( sphere.Center )
			},
			CapsuleCollider capsule => new
			{
				type = "capsule",
				radius = capsule.Radius,
				start = HandlerUtil.ToJson( capsule.Start ),
				end = HandlerUtil.ToJson( capsule.End )
			},
			_ => new
			{
				type = collider.GetType().Name
			}
		};
	}

	private static object DescribeJoint( Joint joint )
	{
		var target = Safe( () => joint.Object2, null );

		return new
		{
			component = HandlerUtil.DescribeComponent( joint ),
			enableCollision = joint.EnableCollision,
			target = target is null ? null : HandlerUtil.DescribeGameObject( target )
		};
	}

	private static void ConfigureRigidbody( Rigidbody body, BridgeRequest request )
	{
		body.Gravity = HandlerUtil.GetBool( request.Payload, "gravity", body.Gravity );
		body.MotionEnabled = HandlerUtil.GetBool( request.Payload, "motionEnabled", body.MotionEnabled );
		var mass = HandlerUtil.GetFloat( request.Payload, "mass", -1f );
		if ( mass > 0f )
			body.MassOverride = mass;
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
