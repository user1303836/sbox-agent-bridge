using System.Text.Json;
using Sandbox;

[Title( "Agent Bridge Mutation Fixture" )]
[Group( "Agent Bridge" )]
public sealed class AgentBridgeMutationFixture : Component
{
	public enum FixtureMode
	{
		Idle,
		Active,
		Complete
	}

	[Property] public string StringValue { get; set; } = "";
	[Property] public bool BoolValue { get; set; }
	[Property] public int IntValue { get; set; }
	[Property] public uint UIntValue { get; set; }
	[Property] public long LongValue { get; set; }
	[Property] public float FloatValue { get; set; }
	[Property] public double DoubleValue { get; set; }
	[Property] public FixtureMode EnumValue { get; set; }
	[Property] public Vector2 Vector2Value { get; set; }
	[Property] public Vector3 Vector3Value { get; set; }
	[Property] public Rotation RotationValue { get; set; } = Rotation.Identity;
	[Property] public Angles AnglesValue { get; set; }
	[Property] public Transform TransformValue { get; set; }
	[Property] public Color ColorValue { get; set; } = new( 1f, 1f, 1f, 1f );
	[Property] public Model ModelValue { get; set; }
	[Property] public Material MaterialValue { get; set; }
	[Property] public Texture TextureValue { get; set; }
	[Property] public SoundEvent SoundEventValue { get; set; }
	[Property] public GameObject GameObjectReference { get; set; }
	[Property] public Component ComponentReference { get; set; }
	[Property, Group( "Agent Bridge" )] public string AgentBridgeTestActions { get; private set; } = "fixture.echo|fixture.state|fixture.set_string";
	[Property, Group( "Agent Bridge" )] public string AgentBridgeTestPayloadJson { get; set; } = "{}";
	[Property, Group( "Agent Bridge" )] public string AgentBridgeTestResultJson { get; private set; } = "";
	[Property, Group( "Agent Bridge" )]
	public string AgentBridgeTestAction
	{
		get => "";
		set => AgentBridgeTestResultJson = JsonSerializer.Serialize( RunFixtureTestAction( value, AgentBridgeTestPayloadJson ) );
	}

	public string[] AgentBridgeListTestActions()
	{
		return new[]
		{
			"fixture.echo",
			"fixture.state",
			"fixture.set_string"
		};
	}

	private object RunFixtureTestAction( string action, string payloadJson )
	{
		return action switch
		{
			"fixture.echo" => new
			{
				action,
				payloadJson,
				stringValue = StringValue,
				intValue = IntValue,
				boolValue = BoolValue
			},
			"fixture.state" => DescribeAgentBridgeState(),
			"fixture.set_string" => SetStringFromPayload( payloadJson ),
			_ => throw new System.InvalidOperationException( $"Unknown fixture test action '{action}'." )
		};
	}

	private object DescribeAgentBridgeState()
	{
		return new
		{
			stringValue = StringValue,
			boolValue = BoolValue,
			intValue = IntValue,
			floatValue = FloatValue,
			gameObject = GameObject?.Name ?? ""
		};
	}

	private object SetStringFromPayload( string payloadJson )
	{
		using var document = JsonDocument.Parse( string.IsNullOrWhiteSpace( payloadJson ) ? "{}" : payloadJson );
		var value = document.RootElement.TryGetProperty( "value", out var valueElement ) && valueElement.ValueKind == JsonValueKind.String
			? valueElement.GetString() ?? ""
			: "";

		StringValue = value;

		return new
		{
			stringValue = StringValue
		};
	}
}
