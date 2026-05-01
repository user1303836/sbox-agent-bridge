using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using Sandbox;

namespace SboxAgentBridge.Editor;

internal static class RuntimeHandlers
{
	private const string ListMethodName = "AgentBridgeListTestActions";
	private const string ActionsPropertyName = "AgentBridgeTestActions";
	private const string ActionPropertyName = "AgentBridgeTestAction";
	private const string PayloadPropertyName = "AgentBridgeTestPayloadJson";
	private const string ResultPropertyName = "AgentBridgeTestResultJson";
	private static readonly string[] RunMethodNames = { "AgentBridgeRunTestAction", "AgentBridgeTestAction" };

	public static BridgeResponse ListTestActions( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "runtime" );
		var componentType = HandlerUtil.GetString( request.Payload, "componentType" );
		var includeAllCandidates = HandlerUtil.GetBool( request.Payload, "includeAllCandidates", false ) || !string.IsNullOrWhiteSpace( componentType );
		var components = FindTestActionComponents( resolution.Session.Scene, componentType, includeAllCandidates )
			.Select( component => new
			{
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( component.GameObject ),
				actions = ReadActionNames( component ),
				propertyProtocol = DescribePropertyProtocol( component ),
				methods = DescribeMethods( component )
			} )
			.ToArray();

		return BridgeResponse.Success( request.Id, new
		{
			message = "Runtime test actions listed",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				componentType,
				includeAllCandidates,
				count = components.Length,
				components
			}
		} );
	}

	public static BridgeResponse RunTestAction( BridgeRequest request )
	{
		var resolution = HandlerUtil.RequireSessionResolution( request.Payload, "runtime" );
		var action = HandlerUtil.GetRequiredString( request.Payload, "testAction" );
		var payloadJson = ReadNestedPayloadJson( request.Payload );
		var component = ResolveTestActionComponent( resolution.Session.Scene, request.Payload, action );
		var method = FindRunMethod( component );
		var protocol = FindPropertyProtocol( component );
		var invocation = method is not null
			? InvokeRunMethod( component, method, action, payloadJson )
			: InvokePropertyProtocol( component, protocol, action, payloadJson );

		return BridgeResponse.Success( request.Id, new
		{
			message = "Runtime test action invoked",
			verified = new
			{
				targetSession = HandlerUtil.DescribeSessionResolution( resolution ),
				testAction = action,
				payloadJson,
				component = HandlerUtil.DescribeComponent( component ),
				gameObject = HandlerUtil.DescribeGameObject( component.GameObject ),
				invocationMode = invocation.Mode,
				method = method is null ? null : new
				{
					name = method.Name,
					returnType = method.ReturnType.FullName ?? method.ReturnType.Name,
					parameterCount = method.GetParameters().Length
				},
				propertyProtocol = DescribePropertyProtocol( component ),
				result = invocation.Result,
				resultJson = invocation.ResultJson
			}
		} );
	}

	private static Component[] FindTestActionComponents( Scene scene, string componentType = "", bool includeAllCandidates = false )
	{
		IEnumerable<Component> components = HandlerUtil.WalkSceneObjects( scene )
			.SelectMany( go => go.Components.GetAll() );

		if ( !string.IsNullOrWhiteSpace( componentType ) )
		{
			components = components.Where( component =>
				Contains( component.GetType().Name, componentType ) ||
				Contains( component.GetType().FullName, componentType )
			);
		}

		if ( !includeAllCandidates )
			components = components.Where( component => FindRunMethod( component ) is not null || FindListMethod( component ) is not null || FindPropertyProtocol( component ).CanRun );

		return components.ToArray();
	}

	private static Component ResolveTestActionComponent( Scene scene, JsonElement payload, string action )
	{
		var componentId = HandlerUtil.GetString( payload, "componentId" );
		if ( !string.IsNullOrWhiteSpace( componentId ) )
		{
			var component = HandlerUtil.RequireComponentById( scene, componentId, "componentId" );
			if ( FindRunMethod( component ) is null && !FindPropertyProtocol( component ).CanRun )
				throw new InvalidOperationException( $"Component '{componentId}' does not expose a runtime test-action method." );

			return component;
		}

		IEnumerable<Component> components = FindTestActionComponents( scene );

		var gameObjectId = HandlerUtil.GetString( payload, "gameObjectId" );
		if ( !string.IsNullOrWhiteSpace( gameObjectId ) )
		{
			var go = HandlerUtil.RequireGameObjectById( scene, gameObjectId, "gameObjectId" );
			components = go.Components.GetAll().Where( component => FindRunMethod( component ) is not null || FindPropertyProtocol( component ).CanRun );
		}

		var componentType = HandlerUtil.GetString( payload, "componentType" );
		if ( !string.IsNullOrWhiteSpace( componentType ) )
		{
			components = components.Where( component =>
				Contains( component.GetType().Name, componentType ) ||
				Contains( component.GetType().FullName, componentType )
			);
		}

		var candidates = components.ToArray();
		if ( candidates.Length == 0 )
			throw new InvalidOperationException( "No runtime component with an Agent Bridge test-action method matched the request." );

		var explicitActionCandidates = candidates
			.Where( component => ReadActionNames( component ).Any( listed => string.Equals( listed, action, StringComparison.OrdinalIgnoreCase ) ) )
			.ToArray();

		if ( explicitActionCandidates.Length == 1 )
			return explicitActionCandidates[0];

		if ( explicitActionCandidates.Length > 1 )
			throw new InvalidOperationException( $"Multiple runtime test-action components expose action '{action}'. Pass componentId, gameObjectId, or componentType." );

		if ( candidates.Length == 1 )
			return candidates[0];

		throw new InvalidOperationException( "Multiple runtime test-action components matched the request. Pass componentId, gameObjectId, or componentType." );
	}

	private static MethodInfo? FindRunMethod( Component component )
	{
		return RunMethodNames
			.SelectMany( name => component.GetType().GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance ).Where( method => method.Name == name ) )
			.FirstOrDefault( IsSupportedRunMethod );
	}

	private static MethodInfo? FindListMethod( Component component )
	{
		return component.GetType()
			.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
			.FirstOrDefault( method => method.Name == ListMethodName && method.GetParameters().Length == 0 );
	}

	private static bool IsSupportedRunMethod( MethodInfo method )
	{
		var parameters = method.GetParameters();
		if ( parameters.Length == 0 )
			return true;

		if ( parameters.Length == 1 )
			return parameters[0].ParameterType == typeof( string );

		return parameters.Length == 2 &&
			parameters[0].ParameterType == typeof( string ) &&
			parameters[1].ParameterType == typeof( string );
	}

	private static RuntimeInvocationResult InvokeRunMethod( Component component, MethodInfo method, string action, string payloadJson )
	{
		var parameters = method.GetParameters();
		object?[] args = parameters.Length switch
		{
			0 => Array.Empty<object?>(),
			1 => new object?[] { action },
			2 => new object?[] { action, payloadJson },
			_ => throw new InvalidOperationException( $"Unsupported test-action method signature on '{method.Name}'." )
		};

		try
		{
			return new RuntimeInvocationResult
			{
				Mode = "method",
				Result = method.Invoke( component, args ),
				ResultJson = ""
			};
		}
		catch ( TargetInvocationException ex ) when ( ex.InnerException is not null )
		{
			throw new InvalidOperationException( ex.InnerException.Message, ex.InnerException );
		}
	}

	private static string[] ReadActionNames( Component component )
	{
		var method = FindListMethod( component );
		if ( method is not null )
		{
			try
			{
				var result = method.Invoke( component, Array.Empty<object>() );
				if ( result is string single )
					return SplitActionNames( single );

				if ( result is IEnumerable enumerable )
				{
					return enumerable
						.Cast<object?>()
						.Select( item => item?.ToString() ?? "" )
						.Where( item => !string.IsNullOrWhiteSpace( item ) )
						.Distinct( StringComparer.OrdinalIgnoreCase )
						.OrderBy( item => item )
						.ToArray();
				}
			}
			catch
			{
				return Array.Empty<string>();
			}
		}

		var protocol = FindPropertyProtocol( component );
		if ( protocol.Actions is not null )
		{
			try
			{
				return SplitActionNames( protocol.Actions.GetValue( component ) as string ?? "" );
			}
			catch
			{
				return Array.Empty<string>();
			}
		}

		return Array.Empty<string>();
	}

	private static RuntimeInvocationResult InvokePropertyProtocol( Component component, PropertyProtocol protocol, string action, string payloadJson )
	{
		if ( !protocol.CanRun )
			throw new InvalidOperationException( $"Component '{component.GetType().Name}' does not expose a supported runtime test-action method or property protocol." );

		try
		{
			protocol.Payload?.SetValue( component, payloadJson );
			protocol.Action.SetValue( component, action );

			var resultJson = protocol.Result?.GetValue( component ) as string ?? "";

			return new RuntimeInvocationResult
			{
				Mode = "propertyProtocol",
				Result = ParseResultJson( resultJson ),
				ResultJson = resultJson
			};
		}
		catch ( TargetInvocationException ex ) when ( ex.InnerException is not null )
		{
			throw new InvalidOperationException( $"Runtime test-action property protocol failed on '{component.GetType().Name}' for action '{action}': {ex.InnerException.Message}", ex.InnerException );
		}
		catch ( Exception ex ) when ( ex is not InvalidOperationException )
		{
			throw new InvalidOperationException( $"Runtime test-action property protocol failed on '{component.GetType().Name}' for action '{action}': {ex.Message}", ex );
		}
	}

	private static PropertyProtocol FindPropertyProtocol( Component component )
	{
		var type = Game.TypeLibrary.GetType( component.GetType() );
		var properties = type?.Properties ?? Array.Empty<PropertyDescription>();

		var action = FindStringProperty( properties, ActionPropertyName );
		var payload = FindStringProperty( properties, PayloadPropertyName );
		var result = FindStringProperty( properties, ResultPropertyName );
		var actions = FindStringProperty( properties, ActionsPropertyName );

		return new PropertyProtocol
		{
			Action = action is not null && action.CanWrite && !action.ReadOnly ? action : null,
			Payload = payload is not null && payload.CanWrite && !payload.ReadOnly ? payload : null,
			Result = result is not null && result.CanRead ? result : null,
			Actions = actions is not null && actions.CanRead ? actions : null
		};
	}

	private static PropertyDescription? FindStringProperty( PropertyDescription[] properties, string name )
	{
		return properties.FirstOrDefault( property =>
			string.Equals( property.Name, name, StringComparison.Ordinal ) &&
			property.PropertyType == typeof( string )
		);
	}

	private static object DescribePropertyProtocol( Component component )
	{
		var protocol = FindPropertyProtocol( component );

		return new
		{
			canRun = protocol.CanRun,
			hasActions = protocol.Actions is not null,
			hasPayload = protocol.Payload is not null,
			hasAction = protocol.Action is not null,
			hasResult = protocol.Result is not null,
			propertyNames = new
			{
				actions = ActionsPropertyName,
				payload = PayloadPropertyName,
				action = ActionPropertyName,
				result = ResultPropertyName
			}
		};
	}

	private static string[] SplitActionNames( string value )
	{
		if ( string.IsNullOrWhiteSpace( value ) )
			return Array.Empty<string>();

		return value
			.Split( new[] { ',', ';', '|', '\n', '\r', '\t', ' ' }, StringSplitOptions.RemoveEmptyEntries )
			.Select( item => item.Trim() )
			.Where( item => !string.IsNullOrWhiteSpace( item ) )
			.Distinct( StringComparer.OrdinalIgnoreCase )
			.OrderBy( item => item )
			.ToArray();
	}

	private static object? ParseResultJson( string resultJson )
	{
		if ( string.IsNullOrWhiteSpace( resultJson ) )
			return null;

		try
		{
			return JsonNode.Parse( resultJson );
		}
		catch
		{
			return resultJson;
		}
	}

	private static object[] DescribeMethods( Component component )
	{
		return component.GetType()
			.GetMethods( BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance )
			.Where( method => method.Name.Contains( "AgentBridge", StringComparison.OrdinalIgnoreCase ) )
			.Select( method => new
			{
				name = method.Name,
				supported = method.Name == ListMethodName || IsSupportedRunMethod( method ),
				parameterTypes = method.GetParameters().Select( parameter => parameter.ParameterType.FullName ?? parameter.ParameterType.Name ).ToArray(),
				returnType = method.ReturnType.FullName ?? method.ReturnType.Name,
				declaringType = method.DeclaringType?.FullName ?? ""
			} )
			.ToArray();
	}

	private static string ReadNestedPayloadJson( JsonElement payload )
	{
		if ( payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( "payload", out var nested ) )
			return nested.GetRawText();

		return "{}";
	}

	private static bool Contains( string value, string query )
	{
		return !string.IsNullOrWhiteSpace( value ) && value.Contains( query, StringComparison.OrdinalIgnoreCase );
	}

	private sealed class RuntimeInvocationResult
	{
		public string Mode { get; set; } = "";
		public object? Result { get; set; }
		public string ResultJson { get; set; } = "";
	}

	private sealed class PropertyProtocol
	{
		public PropertyDescription? Actions { get; set; }
		public PropertyDescription? Payload { get; set; }
		public PropertyDescription? Action { get; set; }
		public PropertyDescription? Result { get; set; }
		public bool CanRun => Action is not null && Result is not null;
	}
}
