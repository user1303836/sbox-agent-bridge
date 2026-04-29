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
	[Property] public GameObject GameObjectReference { get; set; }
	[Property] public Component ComponentReference { get; set; }
}
