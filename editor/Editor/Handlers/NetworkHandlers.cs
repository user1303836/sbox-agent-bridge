using System;
using System.Linq;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class NetworkHandlers
{
	public static BridgeResponse Connections( BridgeRequest request )
	{
		var connections = Safe( () => Connection.All?.ToArray() ?? Array.Empty<Connection>(), Array.Empty<Connection>() );
		var local = Safe( () => Connection.Local, null );
		var host = Safe( () => Connection.Host, null );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Network connections inspected",
			verified = new
			{
				count = connections.Length,
				local = local is null ? null : DescribeConnection( local ),
				host = host is null ? null : DescribeConnection( host ),
				connections = connections.Select( DescribeConnection ).ToArray()
			}
		} );
	}

	public static BridgeResponse InspectObject( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "active" );
		var go = HandlerUtil.RequireGameObject( resolution.Session.Scene, request.Payload, "gameObjectId" );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Network object inspected",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				gameObject = HandlerUtil.DescribeGameObject( go ),
				network = DescribeNetwork( go )
			}
		} );
	}

	public static BridgeResponse SetObjectMode( BridgeRequest request )
	{
		var session = HandlerUtil.RequireSession();
		var go = HandlerUtil.RequireGameObject( session.Scene, request.Payload, "gameObjectId" );
		var before = DescribeNetwork( go );

		using ( session.UndoScope( "Agent Bridge: Set Network Object Mode" ).WithGameObjectChanges( go, GameObjectUndoFlags.Properties ).Push() )
		{
			var mode = HandlerUtil.GetString( request.Payload, "networkMode" );
			if ( !string.IsNullOrWhiteSpace( mode ) )
				go.NetworkMode = Enum.Parse<NetworkMode>( mode, true );

			var ownerTransfer = HandlerUtil.GetString( request.Payload, "ownerTransfer" );
			if ( !string.IsNullOrWhiteSpace( ownerTransfer ) )
				go.Network.SetOwnerTransfer( Enum.Parse<OwnerTransfer>( ownerTransfer, true ) );

			var orphaned = HandlerUtil.GetString( request.Payload, "networkOrphaned" );
			if ( !string.IsNullOrWhiteSpace( orphaned ) )
				go.Network.SetOrphanedMode( Enum.Parse<NetworkOrphaned>( orphaned, true ) );

			if ( HandlerUtil.HasProperty( request.Payload, "alwaysTransmit" ) )
				go.Network.AlwaysTransmit = HandlerUtil.GetBool( request.Payload, "alwaysTransmit", go.Network.AlwaysTransmit );
		}

		return BridgeResponse.Success( request.Id, new
		{
			message = "Network object mode set",
			verified = new
			{
				gameObject = HandlerUtil.DescribeGameObject( go ),
				before,
				after = DescribeNetwork( go )
			}
		} );
	}

	private static object DescribeNetwork( GameObject go )
	{
		var network = go.Network;

		return new
		{
			networkMode = go.NetworkMode.ToString(),
			ownerTransfer = Safe( () => network.OwnerTransfer.ToString(), "" ),
			networkOrphaned = Safe( () => network.NetworkOrphaned.ToString(), "" ),
			networkFlags = Safe( () => network.Flags.ToString(), "" ),
			alwaysTransmit = Safe( () => network.AlwaysTransmit, false ),
			networkRoot = Safe( () => network.RootGameObject is null ? null : new
			{
				id = network.RootGameObject.Id.ToString(),
				name = network.RootGameObject.Name
			}, null ),
			accessor = new
			{
				active = Safe( () => network.Active, false ),
				rootGameObject = Safe( () => network.RootGameObject is null ? null : new
				{
					id = network.RootGameObject.Id.ToString(),
					name = network.RootGameObject.Name
				}, null ),
				isOwner = Safe( () => network.IsOwner, false ),
				ownerId = Safe( () => network.OwnerId.ToString(), "" ),
				isCreator = Safe( () => network.IsCreator, false ),
				creatorId = Safe( () => network.CreatorId.ToString(), "" ),
				isProxy = Safe( () => network.IsProxy, false ),
				ownerConnection = Safe( () => network.OwnerConnection is null ? null : DescribeConnection( network.OwnerConnection ), null ),
				owner = Safe( () => network.Owner is null ? null : DescribeConnection( network.Owner ), null ),
				ownerTransfer = Safe( () => network.OwnerTransfer.ToString(), "" ),
				networkOrphaned = Safe( () => network.NetworkOrphaned.ToString(), "" ),
				flags = Safe( () => network.Flags.ToString(), "" ),
				alwaysTransmit = Safe( () => network.AlwaysTransmit, false ),
				interpolation = Safe( () => network.Interpolation, false )
			}
		};
	}

	private static object DescribeConnection( Connection connection )
	{
		var hostId = Safe( () => Connection.Host.Id.ToString(), "" );
		var connectionId = Safe( () => connection.Id.ToString(), "" );

		return new
		{
			id = connectionId,
			canSpawnObjects = Safe( () => connection.CanSpawnObjects, false ),
			canRefreshObjects = Safe( () => connection.CanRefreshObjects, false ),
			canDestroyObjects = Safe( () => connection.CanDestroyObjects, false ),
			isConnecting = Safe( () => connection.IsConnecting, false ),
			isActive = Safe( () => connection.IsActive, false ),
			state = GetOptionalPropertyString( connection, "State" ),
			ping = Safe( () => connection.Ping.ToString(), "" ),
			isHost = !string.IsNullOrWhiteSpace( connectionId ) && string.Equals( connectionId, hostId, StringComparison.OrdinalIgnoreCase )
		};
	}

	private static string GetOptionalPropertyString( object instance, string propertyName )
	{
		try
		{
			var property = instance.GetType().GetProperty( propertyName );
			return property?.GetValue( instance )?.ToString() ?? "";
		}
		catch
		{
			return "";
		}
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
