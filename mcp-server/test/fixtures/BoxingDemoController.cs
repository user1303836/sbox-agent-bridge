using System;
using System.Collections.Generic;
using System.Text.Json;
using Sandbox;
using Sandbox.UI;

public sealed class BoxingDemoController : Component, Component.ExecuteInEditor
{
	private const string RuntimeRootName = "Boxing POC Runtime Root";
	private const float RingHalfWidth = 520f;
	private const float RingHalfDepth = 340f;
	private const float FighterRadius = 34f;
	private const float MinFighterSpacing = 62f;

	[Property, Group( "Match" )] public int MaxRounds { get; set; } = 3;
	[Property, Group( "Match" )] public float RoundDuration { get; set; } = 75f;
	[Property, Group( "Match" )] public float BreakDuration { get; set; } = 5f;
	[Property, Group( "Match" )] public float KnockdownDuration { get; set; } = 4f;
	[Property, Group( "Player" )] public float PlayerMoveSpeed { get; set; } = 235f;
	[Property, Group( "Opponent" )] public float OpponentMoveSpeed { get; set; } = 178f;
	[Property, Group( "Bridge Testbed" )] public bool RunInEditorForBridge { get; set; } = true;
	[Property, Group( "Bridge Testbed" )] public string AgentBridgeTestActions { get; private set; } = "boxing.state|boxing.jab|boxing.cross|boxing.hook|boxing.block|boxing.dodge|boxing.damage_opponent|boxing.damage_player|boxing.advance|boxing.force_decision|boxing.reset";
	[Property, Group( "Bridge Testbed" )] public string AgentBridgeTestPayloadJson { get; set; } = "{}";
	[Property, Group( "Bridge Testbed" )] public string AgentBridgeTestResultJson { get; private set; } = "";
	[Property, Group( "Bridge Testbed" )]
	public string AgentBridgeTestAction
	{
		get => "";
		set
		{
			if ( string.IsNullOrWhiteSpace( value ) )
				return;

			AgentBridgeTestResultJson = JsonSerializer.Serialize( RunAgentBridgeTestAction( value, AgentBridgeTestPayloadJson ) );
		}
	}

	private GameObject _runtimeRoot;
	private GameObject _cameraObject;
	private CameraComponent _camera;
	private ScreenPanel _screenPanel;
	private Panel _hudRoot;
	private Label _scoreboardLabel;
	private Label _eventLabel;
	private Label _controlLabel;
	private Panel _playerHealthFill;
	private Panel _playerStaminaFill;
	private Panel _opponentHealthFill;
	private Panel _opponentStaminaFill;

	private BoxerState _player;
	private BoxerState _opponent;
	private readonly List<ImpactEffect> _effects = new();
	private bool _worldBuilt;
	private MatchPhase _phase = MatchPhase.Fighting;
	private float _clock;
	private float _roundElapsed;
	private float _phaseUntil;
	private int _round = 1;
	private string _lastEvent = "Opening bell.";
	private uint _rng = 0xC0FFEE12u;
	private float _nextHudRetry;
	private float _nextAiThink;
	private float _aiAggression = 0.55f;

	protected override void OnStart()
	{
		if ( ShouldRunDemo() )
			BuildWorld();
	}

	protected override void OnUpdate()
	{
		if ( !ShouldRunDemo() )
			return;

		if ( !_worldBuilt )
			BuildWorld();

		UpdateGame( Time.Delta, true );
	}

	protected override void OnDestroy()
	{
		DestroyRuntimeRoot();
	}

	private bool ShouldRunDemo()
	{
		return Game.IsPlaying || RunInEditorForBridge;
	}

	private void BuildWorld()
	{
		DestroyRuntimeRoot();

		_rng = (uint)(Time.Now * 1000f) ^ 0xA11CE5u;
		_effects.Clear();
		_clock = 0f;
		_roundElapsed = 0f;
		_phaseUntil = 0f;
		_round = 1;
		_phase = MatchPhase.Fighting;
		_lastEvent = "Opening bell. J jab, K cross, L hook, F block, Space slip.";
		_nextAiThink = 0.6f;
		_aiAggression = 0.55f;

		_runtimeRoot = new GameObject( true, RuntimeRootName );
		_runtimeRoot.Flags |= GameObjectFlags.NotSaved;

		BuildArena();
		_player = CreateBoxer( "Player", new Vector3( -50f, 0f, 0f ), new Color( 0.15f, 0.30f, 0.78f, 1f ), true );
		_opponent = CreateBoxer( "Opponent", new Vector3( 50f, 0f, 0f ), new Color( 0.74f, 0.12f, 0.12f, 1f ), false );
		FaceFighters();
		BuildCamera();
		BuildHud();

		Mouse.Visibility = MouseVisibility.Visible;
		_worldBuilt = true;
		Log.Info( "Boxing POC built: movement, punches, block, dodge, AI, rounds, knockdowns, KO/TKO, and decision scoring are active." );
	}

	private void DestroyRuntimeRoot()
	{
		_worldBuilt = false;

		if ( _runtimeRoot is not null && _runtimeRoot.IsValid && !_runtimeRoot.IsDestroyed )
			_runtimeRoot.Destroy();

		_runtimeRoot = null;
		_cameraObject = null;
		_camera = null;
		_screenPanel = null;
		_hudRoot = null;
		_scoreboardLabel = null;
		_eventLabel = null;
		_controlLabel = null;
		_playerHealthFill = null;
		_playerStaminaFill = null;
		_opponentHealthFill = null;
		_opponentStaminaFill = null;
		_player = null;
		_opponent = null;
		_effects.Clear();
	}

	private object RunAgentBridgeTestAction( string action, string payloadJson )
	{
		if ( !_worldBuilt )
			BuildWorld();

		using var document = JsonDocument.Parse( string.IsNullOrWhiteSpace( payloadJson ) ? "{}" : payloadJson );
		var payload = document.RootElement;

		switch ( action )
		{
			case "boxing.state":
				return DescribeState( "boxing.state" );
			case "boxing.jab":
				TryPunch( _player, _opponent, PunchKind.Jab, true );
				return DescribeState( "boxing.jab" );
			case "boxing.cross":
				TryPunch( _player, _opponent, PunchKind.Cross, true );
				return DescribeState( "boxing.cross" );
			case "boxing.hook":
				TryPunch( _player, _opponent, PunchKind.Hook, true );
				return DescribeState( "boxing.hook" );
			case "boxing.block":
				StartBlock( _player, GetPayloadFloat( payload, "seconds", 1.1f ) );
				return DescribeState( "boxing.block" );
			case "boxing.dodge":
				StartDodge( _player, GetPayloadFloat( payload, "direction", 1f ) >= 0f ? 1f : -1f );
				return DescribeState( "boxing.dodge" );
			case "boxing.damage_opponent":
				ApplyDirectDamage( _opponent, _player, GetPayloadFloat( payload, "amount", 35f ), "bridge damage" );
				return DescribeState( "boxing.damage_opponent" );
			case "boxing.damage_player":
				ApplyDirectDamage( _player, _opponent, GetPayloadFloat( payload, "amount", 25f ), "bridge damage" );
				return DescribeState( "boxing.damage_player" );
			case "boxing.advance":
				AdvanceSimulation( GetPayloadFloat( payload, "seconds", 3f ) );
				return DescribeState( "boxing.advance" );
			case "boxing.force_decision":
				ForceDecision();
				return DescribeState( "boxing.force_decision" );
			case "boxing.reset":
				ResetMatch();
				return DescribeState( "boxing.reset" );
			default:
				return new
				{
					action,
					error = "Unknown boxing test action",
					available = AgentBridgeTestActions
				};
		}
	}

	private void UpdateGame( float dt, bool readInput )
	{
		if ( dt <= 0f )
			return;

		dt = MathF.Min( dt, 0.1f );
		_clock += dt;

		if ( _hudRoot is null && Time.Now >= _nextHudRetry )
			BuildHud();

		if ( _phase == MatchPhase.Fighting )
		{
			_roundElapsed += dt;

			if ( readInput )
				HandlePlayerInput( dt );

			UpdateOpponentAi( dt );
			SeparateFighters();

			if ( _roundElapsed >= RoundDuration )
				EndRound();
		}
		else if ( _phase == MatchPhase.Knockdown )
		{
			if ( _clock >= _phaseUntil )
				ResolveKnockdown();
		}
		else if ( _phase == MatchPhase.RoundBreak )
		{
			if ( _clock >= _phaseUntil )
				StartNextRound();
		}

		UpdateFighter( _player, dt );
		UpdateFighter( _opponent, dt );
		UpdateEffects( dt );
		FaceFighters();
		UpdateCamera();
		UpdateHud();
	}

	private void HandlePlayerInput( float dt )
	{
		var move = Vector3.Zero;

		if ( ActionDown( "Forward", "w" ) || ActionDown( "MoveForward", "w" ) )
			move.x += 1f;
		if ( ActionDown( "Backward", "s" ) || ActionDown( "MoveBackward", "s" ) )
			move.x -= 1f;
		if ( ActionDown( "Left", "a" ) || ActionDown( "MoveLeft", "a" ) )
			move.y += 1f;
		if ( ActionDown( "Right", "d" ) || ActionDown( "MoveRight", "d" ) )
			move.y -= 1f;

		if ( move.Length > 0.01f )
			MoveBoxer( _player, move.Normal * PlayerMoveSpeed * dt );

		if ( ActionDown( "Run", "shift" ) || ActionDown( "Block", "f" ) )
			StartBlock( _player, 0.18f );

		if ( ActionPressed( "Jump", "space" ) || ActionPressed( "Dodge", "space" ) )
			StartDodge( _player, _player.Body.WorldPosition.y <= _opponent.Body.WorldPosition.y ? -1f : 1f );

		if ( ActionPressed( "Attack1", "mouse1" ) || ActionPressed( "Jab", "j" ) )
			TryPunch( _player, _opponent, PunchKind.Jab, false );

		if ( ActionPressed( "Attack2", "mouse2" ) || ActionPressed( "Cross", "k" ) )
			TryPunch( _player, _opponent, PunchKind.Cross, false );

		if ( ActionPressed( "Hook", "l" ) || ActionPressed( "Reload", "r" ) )
			TryPunch( _player, _opponent, PunchKind.Hook, false );

		if ( ActionPressed( "Reset", "enter" ) )
			ResetMatch();
	}

	private void UpdateOpponentAi( float dt )
	{
		if ( _clock < _nextAiThink )
			return;

		_nextAiThink = _clock + RandRange( 0.32f, 0.78f );

		var distance = FlatDistance( _opponent.Body.WorldPosition, _player.Body.WorldPosition );
		var healthPressure = 1f - Clamp01( _opponent.Health / _opponent.MaxHealth );
		var playerBlocking = _player.BlockUntil > _clock;
		_aiAggression = Clamp( 0.48f + healthPressure * 0.22f + (_opponent.ScoreThisRound < _player.ScoreThisRound ? 0.10f : 0f), 0.34f, 0.82f );

		if ( _opponent.Stamina < 22f || (playerBlocking && Rand01() < 0.45f) )
		{
			StartBlock( _opponent, RandRange( 0.45f, 0.9f ) );
			MoveBoxer( _opponent, RetreatDirection( _opponent, _player ) * RandRange( 22f, 52f ) );
			return;
		}

		if ( distance > 118f )
		{
			MoveBoxer( _opponent, ApproachDirection( _opponent, _player ) * RandRange( 42f, 82f ) );
			return;
		}

		if ( distance < 70f && Rand01() < 0.32f )
		{
			StartDodge( _opponent, Rand01() < 0.5f ? -1f : 1f );
			return;
		}

		if ( Rand01() < _aiAggression )
		{
			var roll = Rand01();
			if ( roll < 0.48f )
				TryPunch( _opponent, _player, PunchKind.Jab, false );
			else if ( roll < 0.80f )
				TryPunch( _opponent, _player, PunchKind.Cross, false );
			else
				TryPunch( _opponent, _player, PunchKind.Hook, false );
		}
	}

	private bool TryPunch( BoxerState attacker, BoxerState target, PunchKind kind, bool force )
	{
		if ( _phase != MatchPhase.Fighting && !force )
			return false;

		var spec = GetPunchSpec( kind );
		if ( !force && _clock < attacker.NextPunchAt )
			return false;

		if ( attacker.Stamina < spec.StaminaCost * 0.35f )
		{
			_lastEvent = $"{attacker.Name} is too tired to throw.";
			return false;
		}

		attacker.PunchesThrown++;
		attacker.Stamina = MathF.Max( 0f, attacker.Stamina - spec.StaminaCost );
		attacker.NextPunchAt = _clock + spec.Cooldown;
		attacker.PunchUntil = _clock + spec.VisualTime;
		attacker.LastPunch = kind;

		var distance = FlatDistance( attacker.Body.WorldPosition, target.Body.WorldPosition );
		var inRange = distance <= spec.Range;
		var inFront = IsFacingTarget( attacker, target, spec.MinDot );
		var slipped = target.DodgeUntil > _clock;

		if ( !inRange || !inFront || slipped )
		{
			_lastEvent = slipped
				? $"{target.Name} slipped the {spec.Name}."
				: $"{attacker.Name} missed a {spec.Name}.";
			SpawnImpact( target.Body.WorldPosition + Vector3.Up * 75f, new Color( 0.78f, 0.78f, 0.78f, 0.75f ), 0.22f );
			return false;
		}

		var blocked = target.BlockUntil > _clock && target.Guard > 0f && IsFacingTarget( target, attacker, -0.15f );
		var damage = spec.Damage * (0.75f + Clamp01( attacker.Stamina / attacker.MaxStamina ) * 0.35f);
		var score = spec.Score;

		if ( blocked )
		{
			target.Guard = MathF.Max( 0f, target.Guard - spec.GuardDamage );
			damage *= target.Guard <= 0f ? 0.55f : 0.25f;
			score *= 0.35f;
			_lastEvent = $"{target.Name} blocked {attacker.Name}'s {spec.Name}.";
			if ( target.Guard <= 0f )
			{
				target.StunUntil = _clock + 0.55f;
				_lastEvent = $"{attacker.Name} broke {target.Name}'s guard.";
			}
		}
		else
		{
			attacker.PunchesLanded++;
			_lastEvent = $"{attacker.Name} landed a {spec.Name} for {damage:0} damage.";
		}

		target.Health = MathF.Max( -30f, target.Health - damage );
		target.LastHitAt = _clock;
		attacker.ScoreThisRound += score;
		SpawnImpact( target.Body.WorldPosition + Vector3.Up * RandRange( 58f, 92f ), blocked ? new Color( 0.88f, 0.78f, 0.36f, 0.92f ) : new Color( 1f, 0.23f, 0.10f, 0.95f ), blocked ? 0.32f : 0.46f );

		if ( target.Health <= 0f )
			StartKnockdown( target, attacker );

		return true;
	}

	private void ApplyDirectDamage( BoxerState target, BoxerState attacker, float amount, string source )
	{
		if ( _phase == MatchPhase.Finished )
			return;

		target.Health = MathF.Max( -30f, target.Health - MathF.Max( 0f, amount ) );
		target.LastHitAt = _clock;
		attacker.ScoreThisRound += amount * 0.15f;
		_lastEvent = $"{source}: {target.Name} took {amount:0} damage.";
		SpawnImpact( target.Body.WorldPosition + Vector3.Up * 82f, new Color( 1f, 0.15f, 0.08f, 0.95f ), 0.52f );

		if ( target.Health <= 0f )
			StartKnockdown( target, attacker );
	}

	private void StartKnockdown( BoxerState downed, BoxerState standing )
	{
		downed.Knockdowns++;
		standing.ScoreThisRound += 10f;
		downed.DownUntil = _clock + KnockdownDuration;
		downed.BlockUntil = 0f;
		downed.DodgeUntil = 0f;
		_phase = MatchPhase.Knockdown;
		_phaseUntil = downed.DownUntil;
		_lastEvent = $"{standing.Name} scored a knockdown. Count started.";

		if ( downed.Knockdowns >= 3 || downed.Health <= -12f )
			FinishMatch( standing, $"{standing.Name} wins by TKO" );
	}

	private void ResolveKnockdown()
	{
		if ( _phase == MatchPhase.Finished )
			return;

		var downed = _player.DownUntil > _clock - 0.1f ? _player : _opponent;
		downed.Health = MathF.Max( 36f, downed.MaxHealth * 0.42f );
		downed.Stamina = MathF.Max( 42f, downed.MaxStamina * 0.46f );
		downed.Guard = MathF.Max( 35f, downed.MaxGuard * 0.45f );
		downed.DownUntil = 0f;
		ResetFighterPositions();
		_phase = MatchPhase.Fighting;
		_lastEvent = $"{downed.Name} beat the count.";
	}

	private void EndRound()
	{
		AwardRoundScore();

		if ( _round >= MaxRounds )
		{
			ForceDecision();
			return;
		}

		_phase = MatchPhase.RoundBreak;
		_phaseUntil = _clock + BreakDuration;
		_lastEvent = $"Round {_round} over. Corners.";
	}

	private void AwardRoundScore()
	{
		var playerRound = _player.ScoreThisRound + _player.Knockdowns * 3f;
		var opponentRound = _opponent.ScoreThisRound + _opponent.Knockdowns * 3f;

		if ( MathF.Abs( playerRound - opponentRound ) < 0.1f )
		{
			_player.TotalScore += 10;
			_opponent.TotalScore += 10;
		}
		else if ( playerRound > opponentRound )
		{
			_player.TotalScore += 10;
			_opponent.TotalScore += _opponent.Knockdowns > _player.Knockdowns ? 9 : 8;
		}
		else
		{
			_opponent.TotalScore += 10;
			_player.TotalScore += _player.Knockdowns > _opponent.Knockdowns ? 9 : 8;
		}
	}

	private void StartNextRound()
	{
		_round++;
		_roundElapsed = 0f;
		_player.ScoreThisRound = 0f;
		_opponent.ScoreThisRound = 0f;
		_player.Guard = _player.MaxGuard;
		_opponent.Guard = _opponent.MaxGuard;
		_player.Stamina = MathF.Min( _player.MaxStamina, _player.Stamina + 28f );
		_opponent.Stamina = MathF.Min( _opponent.MaxStamina, _opponent.Stamina + 28f );
		ResetFighterPositions();
		_phase = MatchPhase.Fighting;
		_lastEvent = $"Round {_round} begins.";
	}

	private void ForceDecision()
	{
		if ( _phase == MatchPhase.Finished )
			return;

		AwardRoundScore();

		if ( _player.TotalScore == _opponent.TotalScore )
		{
			_phase = MatchPhase.Finished;
			_lastEvent = $"Majority draw: {_player.TotalScore}-{_opponent.TotalScore}.";
			return;
		}

		var winner = _player.TotalScore > _opponent.TotalScore ? _player : _opponent;
		FinishMatch( winner, $"{winner.Name} wins by decision {_player.TotalScore}-{_opponent.TotalScore}" );
	}

	private void FinishMatch( BoxerState winner, string message )
	{
		_phase = MatchPhase.Finished;
		_lastEvent = message;
		winner.Winner = true;
	}

	private void AdvanceSimulation( float seconds )
	{
		var remaining = Clamp( seconds, 0f, 180f );
		while ( remaining > 0.001f )
		{
			var step = MathF.Min( 1f / 30f, remaining );
			UpdateGame( step, false );
			remaining -= step;
		}
	}

	private void StartBlock( BoxerState boxer, float seconds )
	{
		if ( _phase != MatchPhase.Fighting )
			return;

		boxer.BlockUntil = MathF.Max( boxer.BlockUntil, _clock + Clamp( seconds, 0.1f, 2.5f ) );
		boxer.Guard = MathF.Min( boxer.MaxGuard, boxer.Guard + 8f );
	}

	private void StartDodge( BoxerState boxer, float direction )
	{
		if ( _phase != MatchPhase.Fighting || _clock < boxer.NextDodgeAt || boxer.Stamina < 10f )
			return;

		boxer.Stamina = MathF.Max( 0f, boxer.Stamina - 10f );
		boxer.DodgeUntil = _clock + 0.42f;
		boxer.NextDodgeAt = _clock + 0.85f;
		MoveBoxer( boxer, new Vector3( 0f, direction >= 0f ? 82f : -82f, 0f ) );
		_lastEvent = $"{boxer.Name} slipped off line.";
	}

	private void UpdateFighter( BoxerState boxer, float dt )
	{
		if ( boxer is null || boxer.Body is null || !boxer.Body.IsValid )
			return;

		if ( _phase == MatchPhase.Fighting )
		{
			var staminaRegen = boxer.BlockUntil > _clock ? 10f : 18f;
			boxer.Stamina = MathF.Min( boxer.MaxStamina, boxer.Stamina + staminaRegen * dt );
			boxer.Guard = MathF.Min( boxer.MaxGuard, boxer.Guard + 11f * dt );
		}

		UpdateFighterVisual( boxer );
	}

	private void UpdateFighterVisual( BoxerState boxer )
	{
		var down = boxer.DownUntil > _clock || (_phase == MatchPhase.Finished && !boxer.Winner && boxer.Health <= 0f);
		var blocking = boxer.BlockUntil > _clock;
		var dodging = boxer.DodgeUntil > _clock;
		var punching = boxer.PunchUntil > _clock;
		var recentlyHit = _clock - boxer.LastHitAt < 0.18f;

		if ( down )
		{
			var yawTarget = boxer == _player ? _opponent : _player;
			var direction = yawTarget.Body.WorldPosition - boxer.Body.WorldPosition;
			boxer.Body.WorldRotation = Rotation.LookAt( direction.WithZ( 0f ).Normal, Vector3.Up ) * Rotation.FromRoll( 78f );
		}

		if ( boxer.TorsoRenderer is not null )
		{
			var tint = boxer.BaseTint;
			if ( blocking )
				tint = new Color( 0.22f, 0.34f, 0.48f, 1f );
			else if ( dodging )
				tint = new Color( 0.30f, 0.40f, 0.82f, 1f );
			else if ( recentlyHit )
				tint = new Color( 1f, 0.38f, 0.24f, 1f );
			boxer.TorsoRenderer.Tint = tint;
		}

		if ( boxer.LeftArm is null || boxer.RightArm is null )
			return;

		boxer.LeftArm.LocalPosition = new Vector3( 12f, 18f, 64f );
		boxer.RightArm.LocalPosition = new Vector3( 12f, -18f, 64f );
		boxer.LeftArm.LocalScale = new Vector3( 0.16f, 0.12f, 0.42f );
		boxer.RightArm.LocalScale = new Vector3( 0.16f, 0.12f, 0.42f );
		boxer.LeftArm.LocalRotation = Rotation.FromPitch( 0f );
		boxer.RightArm.LocalRotation = Rotation.FromPitch( 0f );

		if ( blocking )
		{
			boxer.LeftArm.LocalPosition = new Vector3( 24f, 10f, 82f );
			boxer.RightArm.LocalPosition = new Vector3( 24f, -10f, 82f );
			boxer.LeftArm.LocalRotation = Rotation.FromPitch( -18f );
			boxer.RightArm.LocalRotation = Rotation.FromPitch( -18f );
		}
		else if ( punching )
		{
			var rightHand = boxer.LastPunch != PunchKind.Jab;
			var arm = rightHand ? boxer.RightArm : boxer.LeftArm;
			arm.LocalPosition = new Vector3( boxer.LastPunch == PunchKind.Hook ? 36f : 48f, rightHand ? -16f : 16f, 70f );
			arm.LocalScale = boxer.LastPunch == PunchKind.Hook ? new Vector3( 0.42f, 0.10f, 0.14f ) : new Vector3( 0.46f, 0.10f, 0.12f );
			arm.LocalRotation = boxer.LastPunch == PunchKind.Hook ? Rotation.FromYaw( rightHand ? -24f : 24f ) : Rotation.Identity;
		}
	}

	private void UpdateEffects( float dt )
	{
		for ( var i = _effects.Count - 1; i >= 0; i-- )
		{
			var effect = _effects[i];
			effect.Age += dt;

			if ( effect.Body is null || !effect.Body.IsValid || effect.Age >= effect.Duration )
			{
				if ( effect.Body is not null && effect.Body.IsValid && !effect.Body.IsDestroyed )
					effect.Body.Destroy();
				_effects.RemoveAt( i );
				continue;
			}

			effect.Body.WorldPosition += effect.Velocity * dt;
			var remaining = 1f - effect.Age / effect.Duration;
			effect.Body.WorldScale = effect.BaseScale * (0.55f + remaining * 0.75f);
			if ( effect.Renderer is not null )
				effect.Renderer.Tint = effect.Color.WithAlpha( effect.Color.a * remaining );
		}
	}

	private void MoveBoxer( BoxerState boxer, Vector3 delta )
	{
		if ( boxer?.Body is null || !boxer.Body.IsValid )
			return;

		var position = boxer.Body.WorldPosition + delta;
		position.x = Clamp( position.x, -RingHalfWidth + FighterRadius, RingHalfWidth - FighterRadius );
		position.y = Clamp( position.y, -RingHalfDepth + FighterRadius, RingHalfDepth - FighterRadius );
		position.z = 0f;
		boxer.Body.WorldPosition = position;
	}

	private void SeparateFighters()
	{
		var delta = _player.Body.WorldPosition - _opponent.Body.WorldPosition;
		delta = delta.WithZ( 0f );
		var distance = delta.Length;
		if ( distance >= MinFighterSpacing || distance <= 0.01f )
			return;

		var push = delta.Normal * ((MinFighterSpacing - distance) * 0.5f);
		MoveBoxer( _player, push );
		MoveBoxer( _opponent, -push );
	}

	private void ResetFighterPositions()
	{
		_player.Body.WorldPosition = new Vector3( -50f, 0f, 0f );
		_opponent.Body.WorldPosition = new Vector3( 50f, 0f, 0f );
		FaceFighters();
	}

	private void ResetMatch()
	{
		if ( !_worldBuilt || _player?.Body is null || _opponent?.Body is null )
		{
			BuildWorld();
			return;
		}

		_clock = 0f;
		_roundElapsed = 0f;
		_phaseUntil = 0f;
		_round = 1;
		_phase = MatchPhase.Fighting;
		_lastEvent = "Match reset. Opening bell.";
		_nextAiThink = 0.6f;
		_aiAggression = 0.55f;
		ResetBoxer( _player );
		ResetBoxer( _opponent );
		ResetFighterPositions();

		for ( var i = _effects.Count - 1; i >= 0; i-- )
		{
			if ( _effects[i].Body is not null && _effects[i].Body.IsValid && !_effects[i].Body.IsDestroyed )
				_effects[i].Body.Destroy();
		}
		_effects.Clear();
	}

	private void ResetBoxer( BoxerState boxer )
	{
		boxer.Health = boxer.MaxHealth;
		boxer.Stamina = boxer.MaxStamina;
		boxer.Guard = boxer.MaxGuard;
		boxer.NextPunchAt = 0f;
		boxer.NextDodgeAt = 0f;
		boxer.PunchUntil = 0f;
		boxer.BlockUntil = 0f;
		boxer.DodgeUntil = 0f;
		boxer.DownUntil = 0f;
		boxer.StunUntil = 0f;
		boxer.LastHitAt = -10f;
		boxer.PunchesThrown = 0;
		boxer.PunchesLanded = 0;
		boxer.Knockdowns = 0;
		boxer.TotalScore = 0;
		boxer.ScoreThisRound = 0f;
		boxer.Winner = false;
	}

	private void FaceFighters()
	{
		if ( _player?.Body is null || _opponent?.Body is null )
			return;

		FaceDirection( _player.Body, _opponent.Body.WorldPosition - _player.Body.WorldPosition );
		FaceDirection( _opponent.Body, _player.Body.WorldPosition - _opponent.Body.WorldPosition );
	}

	private void FaceDirection( GameObject body, Vector3 direction )
	{
		var flat = direction.WithZ( 0f );
		if ( flat.Length <= 0.01f )
			return;

		body.WorldRotation = Rotation.LookAt( flat.Normal, Vector3.Up );
	}

	private Vector3 ApproachDirection( BoxerState actor, BoxerState target )
	{
		var flat = (target.Body.WorldPosition - actor.Body.WorldPosition).WithZ( 0f );
		return flat.Length > 0.01f ? flat.Normal : Vector3.Right;
	}

	private Vector3 RetreatDirection( BoxerState actor, BoxerState target )
	{
		return -ApproachDirection( actor, target );
	}

	private bool IsFacingTarget( BoxerState actor, BoxerState target, float minDot )
	{
		var forward = actor.Body.WorldRotation.Forward.WithZ( 0f );
		var toTarget = (target.Body.WorldPosition - actor.Body.WorldPosition).WithZ( 0f );
		if ( forward.Length <= 0.01f || toTarget.Length <= 0.01f )
			return true;

		return forward.Normal.Dot( toTarget.Normal ) >= minDot;
	}

	private float FlatDistance( Vector3 a, Vector3 b )
	{
		return a.WithZ( 0f ).Distance( b.WithZ( 0f ) );
	}

	private void BuildArena()
	{
		CreateBox( "Mat canvas", new Vector3( 0f, 0f, -6f ), Rotation.Identity, new Vector3( 11.5f, 7.6f, 0.12f ), new Color( 0.13f, 0.15f, 0.18f, 1f ) );
		CreateBox( "Center logo", new Vector3( 0f, 0f, -2f ), Rotation.Identity, new Vector3( 2.5f, 1.35f, 0.025f ), new Color( 0.86f, 0.78f, 0.52f, 1f ) );

		for ( var side = -1; side <= 1; side += 2 )
		{
			CreateBox( "Ring rope north", new Vector3( 0f, RingHalfDepth * side, 62f ), Rotation.Identity, new Vector3( 11.0f, 0.08f, 0.06f ), new Color( 0.86f, 0.82f, 0.70f, 1f ) );
			CreateBox( "Ring rope mid north", new Vector3( 0f, RingHalfDepth * side, 42f ), Rotation.Identity, new Vector3( 11.0f, 0.08f, 0.06f ), new Color( 0.86f, 0.82f, 0.70f, 1f ) );
			CreateBox( "Ring rope low north", new Vector3( 0f, RingHalfDepth * side, 24f ), Rotation.Identity, new Vector3( 11.0f, 0.08f, 0.06f ), new Color( 0.86f, 0.82f, 0.70f, 1f ) );
			CreateBox( "Ring rope east", new Vector3( RingHalfWidth * side, 0f, 62f ), Rotation.Identity, new Vector3( 0.08f, 7.2f, 0.06f ), new Color( 0.86f, 0.82f, 0.70f, 1f ) );
			CreateBox( "Ring rope mid east", new Vector3( RingHalfWidth * side, 0f, 42f ), Rotation.Identity, new Vector3( 0.08f, 7.2f, 0.06f ), new Color( 0.86f, 0.82f, 0.70f, 1f ) );
			CreateBox( "Ring rope low east", new Vector3( RingHalfWidth * side, 0f, 24f ), Rotation.Identity, new Vector3( 0.08f, 7.2f, 0.06f ), new Color( 0.86f, 0.82f, 0.70f, 1f ) );
		}

		CreateBox( "Blue corner pad", new Vector3( -RingHalfWidth, -RingHalfDepth, 76f ), Rotation.Identity, new Vector3( 0.45f, 0.45f, 0.95f ), new Color( 0.08f, 0.16f, 0.75f, 1f ) );
		CreateBox( "Red corner pad", new Vector3( RingHalfWidth, RingHalfDepth, 76f ), Rotation.Identity, new Vector3( 0.45f, 0.45f, 0.95f ), new Color( 0.75f, 0.08f, 0.08f, 1f ) );
		CreateBox( "Neutral corner one", new Vector3( -RingHalfWidth, RingHalfDepth, 76f ), Rotation.Identity, new Vector3( 0.38f, 0.38f, 0.85f ), new Color( 0.90f, 0.87f, 0.72f, 1f ) );
		CreateBox( "Neutral corner two", new Vector3( RingHalfWidth, -RingHalfDepth, 76f ), Rotation.Identity, new Vector3( 0.38f, 0.38f, 0.85f ), new Color( 0.90f, 0.87f, 0.72f, 1f ) );

		var ambientObject = new GameObject( _runtimeRoot, true, "Boxing arena ambient" );
		ambientObject.Flags |= GameObjectFlags.NotSaved;
		var ambient = ambientObject.AddComponent<AmbientLight>( true );
		ambient.Color = new Color( 0.30f, 0.32f, 0.36f, 1f );

		var keyObject = new GameObject( _runtimeRoot, true, "Boxing arena key light" );
		keyObject.Flags |= GameObjectFlags.NotSaved;
		keyObject.WorldRotation = Rotation.From( 56f, 0f, 36f );
		var key = keyObject.AddComponent<DirectionalLight>( true );
		key.LightColor = new Color( 0.96f, 0.92f, 0.82f, 1f );
		key.SkyColor = new Color( 0.22f, 0.25f, 0.30f, 1f );
		key.Shadows = true;
	}

	private BoxerState CreateBoxer( string name, Vector3 position, Color tint, bool playerControlled )
	{
		var root = new GameObject( _runtimeRoot, true, name );
		root.Flags |= GameObjectFlags.NotSaved;
		root.WorldPosition = position;

		var torso = CreateChildBox( root, $"{name} torso", new Vector3( 0f, 0f, 56f ), Rotation.Identity, new Vector3( 0.48f, 0.30f, 0.78f ), tint );
		var head = CreateChildBox( root, $"{name} head", new Vector3( 8f, 0f, 106f ), Rotation.Identity, new Vector3( 0.28f, 0.24f, 0.28f ), new Color( 0.74f, 0.58f, 0.46f, 1f ) );
		var leftArm = CreateChildBox( root, $"{name} lead glove", new Vector3( 12f, 18f, 64f ), Rotation.Identity, new Vector3( 0.16f, 0.12f, 0.42f ), playerControlled ? new Color( 0.08f, 0.18f, 0.78f, 1f ) : new Color( 0.72f, 0.06f, 0.06f, 1f ) );
		var rightArm = CreateChildBox( root, $"{name} rear glove", new Vector3( 12f, -18f, 64f ), Rotation.Identity, new Vector3( 0.16f, 0.12f, 0.42f ), playerControlled ? new Color( 0.10f, 0.20f, 0.86f, 1f ) : new Color( 0.80f, 0.08f, 0.08f, 1f ) );
		CreateChildBox( root, $"{name} lead leg", new Vector3( -6f, 13f, 21f ), Rotation.Identity, new Vector3( 0.18f, 0.12f, 0.48f ), new Color( 0.08f, 0.08f, 0.10f, 1f ) );
		CreateChildBox( root, $"{name} rear leg", new Vector3( -6f, -13f, 21f ), Rotation.Identity, new Vector3( 0.18f, 0.12f, 0.48f ), new Color( 0.08f, 0.08f, 0.10f, 1f ) );

		return new BoxerState
		{
			Name = name,
			Body = root,
			Torso = torso,
			Head = head,
			LeftArm = leftArm,
			RightArm = rightArm,
			TorsoRenderer = torso.GetComponent<ModelRenderer>( true ),
			BaseTint = tint,
			MaxHealth = 100f,
			Health = 100f,
			MaxStamina = 100f,
			Stamina = 100f,
			MaxGuard = 100f,
			Guard = 100f,
			PlayerControlled = playerControlled
		};
	}

	private GameObject CreateBox( string name, Vector3 position, Rotation rotation, Vector3 scale, Color tint )
	{
		var go = new GameObject( _runtimeRoot, true, name );
		go.Flags |= GameObjectFlags.NotSaved;
		go.WorldPosition = position;
		go.WorldRotation = rotation;
		go.WorldScale = scale;

		var renderer = go.AddComponent<ModelRenderer>( false );
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = tint;
		renderer.Enabled = true;
		return go;
	}

	private GameObject CreateChildBox( GameObject parent, string name, Vector3 localPosition, Rotation localRotation, Vector3 localScale, Color tint )
	{
		var go = new GameObject( parent, true, name );
		go.Flags |= GameObjectFlags.NotSaved;
		go.LocalPosition = localPosition;
		go.LocalRotation = localRotation;
		go.LocalScale = localScale;

		var renderer = go.AddComponent<ModelRenderer>( false );
		renderer.Model = Model.Load( "models/dev/box.vmdl" );
		renderer.Tint = tint;
		renderer.Enabled = true;
		return go;
	}

	private void SpawnImpact( Vector3 position, Color color, float size )
	{
		var body = CreateBox( "Impact flash", position, Rotation.Identity, new Vector3( size, size, size ), color );
		_effects.Add( new ImpactEffect
		{
			Body = body,
			Renderer = body.GetComponent<ModelRenderer>( true ),
			BaseScale = body.WorldScale,
			Color = color,
			Duration = RandRange( 0.25f, 0.55f ),
			Velocity = new Vector3( RandRange( -10f, 20f ), RandRange( -35f, 35f ), RandRange( 60f, 120f ) )
		} );
	}

	private void BuildCamera()
	{
		_cameraObject = new GameObject( _runtimeRoot, true, "Broadcast camera" );
		_cameraObject.Flags |= GameObjectFlags.NotSaved;
		_camera = _cameraObject.AddComponent<CameraComponent>( true );
		_camera.IsMainCamera = true;
		_camera.Priority = 100;
		_camera.Orthographic = true;
		_camera.OrthographicHeight = 760f;
		_camera.BackgroundColor = new Color( 0.025f, 0.027f, 0.032f, 1f );
		_camera.ZNear = 8f;
		_camera.ZFar = 6000f;
		UpdateCamera();
	}

	private void UpdateCamera()
	{
		if ( _cameraObject is null || _player?.Body is null || _opponent?.Body is null )
			return;

		var midpoint = (_player.Body.WorldPosition + _opponent.Body.WorldPosition) * 0.5f + Vector3.Up * 45f;
		var position = midpoint + new Vector3( -520f, -620f, 520f );
		_cameraObject.WorldPosition = position;
		_cameraObject.WorldRotation = Rotation.LookAt( (midpoint - position).Normal, Vector3.Up );
	}

	private void BuildHud()
	{
		if ( _hudRoot is not null )
			return;

		if ( _screenPanel is null )
		{
			var hudObject = new GameObject( _runtimeRoot, true, "Boxing HUD" );
			hudObject.Flags |= GameObjectFlags.NotSaved;
			_screenPanel = hudObject.AddComponent<ScreenPanel>( true );
			_screenPanel.Scale = 1f;
		}

		_hudRoot = _screenPanel.GetPanel();
		if ( _hudRoot is null )
		{
			_nextHudRetry = Time.Now + 1f;
			return;
		}

		_hudRoot.Style.Set( "position: absolute; left: 0; right: 0; top: 0; bottom: 0; font-family: Poppins; pointer-events: none;" );
		_scoreboardLabel = CreateLabel( _hudRoot, "left: 50%; top: 22px; width: 620px; height: 64px; margin-left: -310px; padding: 10px 14px; font-size: 18px; font-weight: 800; color: #f4ead4; text-align: center; background-color: rgba(9, 10, 12, 0.82); border: 1px solid rgba(220, 196, 128, 0.72); border-radius: 4px; text-shadow: 1px 1px 2px #000;" );
		_eventLabel = CreateLabel( _hudRoot, "left: 50%; bottom: 112px; width: 660px; min-height: 42px; margin-left: -330px; padding: 9px 14px; font-size: 16px; font-weight: 700; color: #ffe1b0; text-align: center; background-color: rgba(10, 8, 7, 0.74); border: 1px solid rgba(192, 132, 58, 0.65); border-radius: 4px; text-shadow: 1px 1px 2px #000;" );
		_controlLabel = CreateLabel( _hudRoot, "left: 50%; bottom: 54px; width: 720px; height: 32px; margin-left: -360px; font-size: 14px; font-weight: 700; color: #d7c5a6; text-align: center; text-shadow: 1px 1px 2px #000;" );

		var playerBars = CreatePanel( _hudRoot, "position: absolute; left: 26px; top: 26px; width: 330px; height: 82px; padding: 12px; background-color: rgba(8, 10, 17, 0.80); border: 1px solid rgba(79, 119, 230, 0.74); border-radius: 4px;" );
		var opponentBars = CreatePanel( _hudRoot, "position: absolute; right: 26px; top: 26px; width: 330px; height: 82px; padding: 12px; background-color: rgba(17, 8, 8, 0.80); border: 1px solid rgba(230, 84, 72, 0.74); border-radius: 4px;" );
		_playerHealthFill = CreateBar( playerBars, 8, "#2f74ee" );
		_playerStaminaFill = CreateBar( playerBars, 42, "#d4c061" );
		_opponentHealthFill = CreateBar( opponentBars, 8, "#d84235" );
		_opponentStaminaFill = CreateBar( opponentBars, 42, "#d4c061" );
	}

	private Panel CreatePanel( Panel parent, string style )
	{
		var panel = new Panel();
		parent.AddChild( panel );
		panel.Style.Set( style );
		return panel;
	}

	private Label CreateLabel( Panel parent, string style )
	{
		var label = new Label();
		parent.AddChild( label );
		label.Style.Set( "position", "absolute" );
		label.Style.Set( style );
		return label;
	}

	private Panel CreateBar( Panel parent, int top, string color )
	{
		var shell = CreatePanel( parent, $"position: absolute; left: 12px; right: 12px; top: {top}px; height: 22px; background-color: rgba(2,2,3,0.86); border: 1px solid rgba(255,255,255,0.12); border-radius: 3px; overflow: hidden;" );
		return CreatePanel( shell, $"position: absolute; left: 0; top: 0; bottom: 0; width: 100%; background-color: {color};" );
	}

	private void UpdateHud()
	{
		if ( _scoreboardLabel is null )
			return;

		var timeLeft = MathF.Max( 0f, RoundDuration - _roundElapsed );
		_scoreboardLabel.Text = $"{_phase} | Round {_round}/{MaxRounds} | {timeLeft:0}s | Score {_player.TotalScore}-{_opponent.TotalScore} | KD {_player.Knockdowns}-{_opponent.Knockdowns}";
		_eventLabel.Text = _lastEvent;
		_controlLabel.Text = "WASD move | J jab | K cross | L hook | F/Shift block | Space slip | Enter reset";
		SetBar( _playerHealthFill, _player.Health / _player.MaxHealth );
		SetBar( _playerStaminaFill, _player.Stamina / _player.MaxStamina );
		SetBar( _opponentHealthFill, _opponent.Health / _opponent.MaxHealth );
		SetBar( _opponentStaminaFill, _opponent.Stamina / _opponent.MaxStamina );
	}

	private void SetBar( Panel panel, float value )
	{
		if ( panel is null )
			return;

		panel.Style.Set( "width", $"{Clamp01( value ) * 100f:0.0}%" );
	}

	private object DescribeState( string action )
	{
		return new
		{
			action,
			phase = _phase.ToString(),
			round = _round,
			roundTimeRemaining = MathF.Max( 0f, RoundDuration - _roundElapsed ),
			lastEvent = _lastEvent,
			winner = _player.Winner ? _player.Name : _opponent.Winner ? _opponent.Name : "",
			player = DescribeBoxer( _player ),
			opponent = DescribeBoxer( _opponent ),
			controls = new[] { "WASD move", "J jab", "K cross", "L hook", "F/Shift block", "Space slip", "Enter reset" },
			bridgeVerified = true
		};
	}

	private object DescribeBoxer( BoxerState boxer )
	{
		return new
		{
			boxer.Name,
			health = MathF.Round( boxer.Health, 1 ),
			stamina = MathF.Round( boxer.Stamina, 1 ),
			guard = MathF.Round( boxer.Guard, 1 ),
			scoreThisRound = MathF.Round( boxer.ScoreThisRound, 1 ),
			totalScore = boxer.TotalScore,
			knockdowns = boxer.Knockdowns,
			punchesThrown = boxer.PunchesThrown,
			punchesLanded = boxer.PunchesLanded,
			blocking = boxer.BlockUntil > _clock,
			dodging = boxer.DodgeUntil > _clock,
			down = boxer.DownUntil > _clock,
			winner = boxer.Winner,
			position = new
			{
				x = MathF.Round( boxer.Body.WorldPosition.x, 1 ),
				y = MathF.Round( boxer.Body.WorldPosition.y, 1 ),
				z = MathF.Round( boxer.Body.WorldPosition.z, 1 )
			}
		};
	}

	private PunchSpec GetPunchSpec( PunchKind kind )
	{
		return kind switch
		{
			PunchKind.Cross => new PunchSpec( "cross", 18f, 15f, 96f, 0.58f, 0.24f, 12f, 1.45f, 0.25f ),
			PunchKind.Hook => new PunchSpec( "hook", 24f, 20f, 82f, 0.78f, 0.30f, 18f, 1.9f, -0.05f ),
			_ => new PunchSpec( "jab", 10f, 8f, 106f, 0.32f, 0.18f, 8f, 0.9f, 0.18f )
		};
	}

	private bool ActionPressed( string action, string keyboardKey )
	{
		_ = action;
		return Sandbox.Input.Keyboard.Pressed( keyboardKey )
			|| Sandbox.Input.Keyboard.Pressed( keyboardKey.ToLowerInvariant() )
			|| Sandbox.Input.Keyboard.Pressed( keyboardKey.ToUpperInvariant() );
	}

	private bool ActionDown( string action, string keyboardKey )
	{
		_ = action;
		return Sandbox.Input.Keyboard.Down( keyboardKey )
			|| Sandbox.Input.Keyboard.Down( keyboardKey.ToLowerInvariant() )
			|| Sandbox.Input.Keyboard.Down( keyboardKey.ToUpperInvariant() );
	}

	private float GetPayloadFloat( JsonElement payload, string name, float fallback )
	{
		return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.TryGetSingle( out var result )
			? result
			: fallback;
	}

	private float Rand01()
	{
		_rng = _rng * 1664525u + 1013904223u;
		return (_rng & 0x00FFFFFF) / 16777215f;
	}

	private float RandRange( float min, float max )
	{
		return min + (max - min) * Rand01();
	}

	private float Clamp01( float value )
	{
		return Clamp( value, 0f, 1f );
	}

	private float Clamp( float value, float min, float max )
	{
		return MathF.Max( min, MathF.Min( max, value ) );
	}

	private enum MatchPhase
	{
		Fighting,
		Knockdown,
		RoundBreak,
		Finished
	}

	private enum PunchKind
	{
		Jab,
		Cross,
		Hook
	}

	private readonly record struct PunchSpec( string Name, float Damage, float StaminaCost, float Range, float Cooldown, float VisualTime, float GuardDamage, float Score, float MinDot );

	private sealed class BoxerState
	{
		public string Name;
		public bool PlayerControlled;
		public GameObject Body;
		public GameObject Torso;
		public GameObject Head;
		public GameObject LeftArm;
		public GameObject RightArm;
		public ModelRenderer TorsoRenderer;
		public Color BaseTint;
		public float MaxHealth;
		public float Health;
		public float MaxStamina;
		public float Stamina;
		public float MaxGuard;
		public float Guard;
		public float NextPunchAt;
		public float NextDodgeAt;
		public float PunchUntil;
		public float BlockUntil;
		public float DodgeUntil;
		public float DownUntil;
		public float StunUntil;
		public float LastHitAt = -10f;
		public PunchKind LastPunch;
		public int PunchesThrown;
		public int PunchesLanded;
		public int Knockdowns;
		public int TotalScore;
		public float ScoreThisRound;
		public bool Winner;
	}

	private sealed class ImpactEffect
	{
		public GameObject Body;
		public ModelRenderer Renderer;
		public Vector3 BaseScale;
		public Vector3 Velocity;
		public Color Color;
		public float Duration;
		public float Age;
	}
}
