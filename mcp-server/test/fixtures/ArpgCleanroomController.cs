using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Sandbox;
using Sandbox.UI;

public sealed class ArpgCleanroomController : Component, Component.ExecuteInEditor
{
	private const string RuntimeRootName = "ARPG Cleanroom Runtime Root";
	private const int InventoryColumns = 10;
	private const int InventoryRows = 5;
	private const float ArenaHalfSize = 860f;
	private const float PlayerRadius = 28f;
	private const float ZombieRadius = 27f;
	private const float NpcRadius = 30f;

	[Property, Group( "Bridge Testbed" )] public bool RunInEditorForBridge { get; set; } = true;
	[Property, Group( "Bridge Testbed" )] public string AgentBridgeTestActions { get; private set; } =
		"arpg.state|arpg.create_character|arpg.reset_character_creation|arpg.damage_player|arpg.restore_player|arpg.spend_mana|arpg.restore_mana|arpg.open_inventory|arpg.toggle_inventory_hotkey|arpg.use_skill|arpg.advance|arpg.aggro_probe|arpg.kill_zombie|arpg.kill_elite|arpg.open_chest|arpg.talk_vendor|arpg.buy_item|arpg.sell_item|arpg.hover_item|arpg.drag_item|arpg.equip_item|arpg.force_health_orb_drop|arpg.pickup_health_orb|arpg.move_player";
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
	private Panel _characterPanel;
	private Panel _healthOrbFill;
	private Panel _manaOrbFill;
	private Panel _buffRow;
	private Panel _debuffRow;
	private Label _statusLabel;
	private Label _hotbarLabel;
	private Label _minimapLabel;
	private Label _inventoryLabel;
	private Label _dialogueLabel;
	private Label _tooltipLabel;
	private Label _vendorLabel;

	private PlayerState _player;
	private ChestState _chest;
	private NeutralNpcState _npc;
	private readonly List<MobState> _mobs = new();
	private readonly List<ItemState> _inventory = new();
	private readonly List<ItemState> _vendorItems = new();
	private readonly List<HealthOrbState> _healthOrbs = new();
	private readonly List<ImpactEffect> _effects = new();
	private readonly List<string> _lootLog = new();
	private GamePhase _phase = GamePhase.CharacterCreation;
	private bool _worldBuilt;
	private bool _characterCreated;
	private string _pendingClass = "Warrior";
	private string _pendingGender = "Male";
	private string _pendingName = "Asha";
	private string _lastAction = "arpg.boot";
	private string _lastEvent = "Choose a class, gender, and name.";
	private string _lastTooltip = "";
	private string _lastAnimation = "Idle";
	private uint _rng = 0xA4B5C6D7u;
	private float _nextHudRetry;

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

		UpdateGame( MathF.Min( Time.Delta, 0.1f ), true );
	}

	protected override void OnDestroy()
	{
		DestroyRuntimeRoot();
	}

	private bool ShouldRunDemo()
	{
		return Game.IsPlaying || RunInEditorForBridge;
	}

	private object RunAgentBridgeTestAction( string action, string payloadJson )
	{
		if ( !_worldBuilt )
			BuildWorld();

		using var document = JsonDocument.Parse( string.IsNullOrWhiteSpace( payloadJson ) ? "{}" : payloadJson );
		var payload = document.RootElement;
		_lastAction = action;

		switch ( action )
		{
			case "arpg.state":
				return DescribeState( action );
			case "arpg.create_character":
				CreateCharacter(
					GetPayloadString( payload, "class", _pendingClass ),
					GetPayloadString( payload, "gender", _pendingGender ),
					GetPayloadString( payload, "name", _pendingName ) );
				return DescribeState( action );
			case "arpg.reset_character_creation":
				ResetCharacterCreation();
				return DescribeState( action );
			case "arpg.damage_player":
				EnsureInGame();
				DamagePlayer( GetPayloadFloat( payload, "amount", 20f ), "bridge damage" );
				return DescribeState( action );
			case "arpg.restore_player":
				EnsureInGame();
				_player.Health = _player.MaxHealth;
				_lastEvent = "Health restored to full.";
				UpdateHud();
				return DescribeState( action );
			case "arpg.spend_mana":
				EnsureInGame();
				SpendMana( GetPayloadFloat( payload, "amount", 20f ) );
				return DescribeState( action );
			case "arpg.restore_mana":
				EnsureInGame();
				_player.Mana = _player.MaxMana;
				_lastEvent = "Mana restored to full.";
				UpdateHud();
				return DescribeState( action );
			case "arpg.open_inventory":
				EnsureInGame();
				_player.InventoryOpen = true;
				_lastEvent = "Inventory opened.";
				UpdateHud();
				return DescribeState( action );
			case "arpg.toggle_inventory_hotkey":
				EnsureInGame();
				_player.InventoryOpen = !_player.InventoryOpen;
				_lastEvent = "Inventory toggled with I.";
				UpdateHud();
				return DescribeState( action );
			case "arpg.use_skill":
				EnsureInGame();
				UseSkill( GetPayloadString( payload, "skill", "left_click" ), GetPayloadBool( payload, "shift", false ) );
				return DescribeState( action );
			case "arpg.advance":
				EnsureInGame();
				AdvanceSimulation( GetPayloadFloat( payload, "seconds", 1f ) );
				return DescribeState( action );
			case "arpg.aggro_probe":
				EnsureInGame();
				RunAggroProbe( GetPayloadBool( payload, "near", false ) );
				return DescribeState( action );
			case "arpg.kill_zombie":
				EnsureInGame();
				KillMob( false, GetPayloadBool( payload, "forceHealthOrb", true ) );
				return DescribeState( action );
			case "arpg.kill_elite":
				EnsureInGame();
				KillMob( true, true );
				return DescribeState( action );
			case "arpg.open_chest":
				EnsureInGame();
				OpenChest();
				return DescribeState( action );
			case "arpg.talk_vendor":
				EnsureInGame();
				TalkVendor();
				return DescribeState( action );
			case "arpg.buy_item":
				EnsureInGame();
				BuyVendorItem( GetPayloadString( payload, "id", "vendor-wand" ) );
				return DescribeState( action );
			case "arpg.sell_item":
				EnsureInGame();
				SellItem( GetPayloadString( payload, "id", "" ) );
				return DescribeState( action );
			case "arpg.hover_item":
				EnsureInGame();
				HoverItem( GetPayloadString( payload, "id", "" ) );
				return DescribeState( action );
			case "arpg.drag_item":
				EnsureInGame();
				DragItem( GetPayloadString( payload, "id", "" ), GetPayloadInt( payload, "x", 0 ), GetPayloadInt( payload, "y", 0 ) );
				return DescribeState( action );
			case "arpg.equip_item":
				EnsureInGame();
				EquipItem( GetPayloadString( payload, "id", "" ) );
				return DescribeState( action );
			case "arpg.force_health_orb_drop":
				EnsureInGame();
				DropHealthOrb( _player.Position + new Vector3( 38f, 24f, 0f ), 35f );
				return DescribeState( action );
			case "arpg.pickup_health_orb":
				EnsureInGame();
				PickupHealthOrb();
				return DescribeState( action );
			case "arpg.move_player":
				EnsureInGame();
				MovePlayerTo( new Vector3( GetPayloadFloat( payload, "x", _player.Position.x ), GetPayloadFloat( payload, "y", _player.Position.y ), 0f ) );
				return DescribeState( action );
			default:
				return new
				{
					action,
					error = "Unknown ARPG test action",
					available = AgentBridgeTestActions
				};
		}
	}

	private void BuildWorld()
	{
		DestroyRuntimeRoot();

		_rng = (uint)(Time.Now * 1000f) ^ 0x51A7E11u;
		_effects.Clear();
		_runtimeRoot = new GameObject( GameObject, true, RuntimeRootName );
		_runtimeRoot.Flags |= GameObjectFlags.NotSaved;

		BuildLighting();
		BuildGround();
		BuildCamera();

		if ( !_characterCreated )
		{
			_phase = GamePhase.CharacterCreation;
			BuildCharacterPreview();
		}
		else
		{
			_phase = GamePhase.InGame;
			BuildStartingZone();
		}

		BuildHud();
		Mouse.Visibility = MouseVisibility.Visible;
		_worldBuilt = true;
		Log.Info( "ARPG clean-room runtime built through Agent Bridge." );
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
		_characterPanel = null;
		_healthOrbFill = null;
		_manaOrbFill = null;
		_buffRow = null;
		_debuffRow = null;
		_statusLabel = null;
		_hotbarLabel = null;
		_minimapLabel = null;
		_inventoryLabel = null;
		_dialogueLabel = null;
		_tooltipLabel = null;
		_vendorLabel = null;
		_mobs.Clear();
		_healthOrbs.Clear();
		_effects.Clear();
	}

	private void BuildLighting()
	{
		var ambientObject = new GameObject( _runtimeRoot, true, "ARPG ambient light" );
		ambientObject.Flags |= GameObjectFlags.NotSaved;
		var ambient = ambientObject.AddComponent<AmbientLight>( true );
		ambient.Color = new Color( 0.24f, 0.23f, 0.29f, 1f );

		var keyObject = new GameObject( _runtimeRoot, true, "ARPG moon key light" );
		keyObject.Flags |= GameObjectFlags.NotSaved;
		keyObject.WorldRotation = Rotation.From( 58f, 0f, 42f );
		var key = keyObject.AddComponent<DirectionalLight>( true );
		key.LightColor = new Color( 0.78f, 0.82f, 0.95f, 1f );
		key.SkyColor = new Color( 0.10f, 0.13f, 0.18f, 1f );
		key.Shadows = true;
	}

	private void BuildGround()
	{
		CreateBox( "Ashen starting-zone ground", new Vector3( 0f, 0f, -7f ), Rotation.Identity, new Vector3( 18f, 18f, 0.12f ), new Color( 0.10f, 0.115f, 0.105f, 1f ) );
		CreateBox( "Old road", new Vector3( 0f, -80f, -2f ), Rotation.FromYaw( 0f ), new Vector3( 3.8f, 16f, 0.03f ), new Color( 0.19f, 0.17f, 0.145f, 1f ) );
		CreateBox( "Camp circle", new Vector3( -230f, 190f, -1f ), Rotation.Identity, new Vector3( 3.0f, 2.2f, 0.035f ), new Color( 0.23f, 0.18f, 0.12f, 1f ) );

		for ( var i = 0; i < 12; i++ )
		{
			var angle = i * 30f;
			var radians = angle * MathF.PI / 180f;
			var x = MathF.Cos( radians ) * 740f;
			var y = MathF.Sin( radians ) * 700f;
			CreateBox( "Ruined boundary stone", new Vector3( x, y, 28f ), Rotation.FromYaw( angle ), new Vector3( 0.32f, 0.42f, 0.84f ), new Color( 0.18f, 0.18f, 0.20f, 1f ) );
		}
	}

	private void BuildCharacterPreview()
	{
		CreateBox( "Character creation warrior plinth", new Vector3( -120f, 0f, 18f ), Rotation.Identity, new Vector3( 0.7f, 0.7f, 0.3f ), new Color( 0.33f, 0.24f, 0.18f, 1f ) );
		CreateBox( "Character creation mage plinth", new Vector3( 120f, 0f, 18f ), Rotation.Identity, new Vector3( 0.7f, 0.7f, 0.3f ), new Color( 0.18f, 0.22f, 0.35f, 1f ) );
		CreateHumanoidVisual( "Warrior preview", new Vector3( -120f, 0f, 44f ), new Color( 0.58f, 0.14f, 0.08f, 1f ), "Sword Idle" );
		CreateHumanoidVisual( "Mage preview", new Vector3( 120f, 0f, 44f ), new Color( 0.16f, 0.28f, 0.78f, 1f ), "Arcane Idle" );
		_lastEvent = "Character creation ready: Warrior or Mage, Male or Female, custom name.";
	}

	private void BuildStartingZone()
	{
		_inventory.Clear();
		_vendorItems.Clear();
		_lootLog.Clear();
		_healthOrbs.Clear();

		_player.Body = CreateHumanoidVisual( $"{_player.Name} player", _player.Position, _player.Class == CharacterClass.Warrior ? new Color( 0.55f, 0.10f, 0.05f, 1f ) : new Color( 0.10f, 0.22f, 0.72f, 1f ), "Idle" );
		_chest = new ChestState
		{
			Position = new Vector3( RandRange( 160f, 360f ), RandRange( -320f, -180f ), 0f ),
			CoinsPerOpenMin = 18,
			CoinsPerOpenMax = 45,
			CanRepeat = true,
			Animation = "closed"
		};
		_chest.Body = CreateBox( "Repeatable coin chest", _chest.Position + Vector3.Up * 28f, Rotation.Identity, new Vector3( 0.56f, 0.36f, 0.34f ), new Color( 0.48f, 0.29f, 0.10f, 1f ) );

		_npc = new NeutralNpcState
		{
			Name = "Sister Kaela",
			Position = new Vector3( -250f, 210f, 0f ),
			Dialogue = "The dead are restless. Sell what you find, buy what keeps you alive.",
			Radius = NpcRadius,
			CollisionEnabled = true
		};
		_npc.Body = CreateHumanoidVisual( "Neutral vendor Sister Kaela", _npc.Position, new Color( 0.78f, 0.62f, 0.22f, 1f ), "Vendor Idle" );

		SpawnMobs();
		AddStarterInventory();
		AddVendorInventory();
		_lastEvent = $"{_player.Name} entered the ash road as a {_player.Gender.ToString().ToLowerInvariant()} {_player.Class}.";
	}

	private void SpawnMobs()
	{
		var positions = new[]
		{
			new Vector3( 260f, 160f, 0f ),
			new Vector3( 340f, 60f, 0f ),
			new Vector3( 430f, 210f, 0f ),
			new Vector3( 540f, -60f, 0f ),
			new Vector3( 620f, 150f, 0f ),
			new Vector3( 320f, -240f, 0f )
		};

		for ( var i = 0; i < positions.Length; i++ )
		{
			_mobs.Add( CreateMob( $"Zombie {i + 1}", positions[i], false ) );
		}

		_mobs.Add( CreateMob( "Rare elite: Gravebound Brute", new Vector3( 650f, 310f, 0f ), true ) );
	}

	private MobState CreateMob( string name, Vector3 position, bool elite )
	{
		var mob = new MobState
		{
			Id = $"mob-{_mobs.Count + 1}",
			Name = name,
			Position = position,
			Radius = elite ? 38f : ZombieRadius,
			MaxHealth = elite ? 240f : 68f,
			Health = elite ? 240f : 68f,
			Damage = elite ? 18f : 8f,
			MoveSpeed = elite ? 76f : 62f,
			AggroRadius = elite ? 350f : 280f,
			AttackRange = elite ? 56f : 44f,
			Elite = elite,
			Alive = true,
			CollisionEnabled = true,
			Animation = elite ? "Elite Idle" : "Zombie Idle"
		};
		mob.Body = elite
			? CreateHumanoidVisual( name, position, new Color( 0.55f, 0.07f, 0.62f, 1f ), "Elite Idle" )
			: CreateHumanoidVisual( name, position, new Color( 0.23f, 0.42f, 0.18f, 1f ), "Zombie Idle" );
		return mob;
	}

	private void AddStarterInventory()
	{
		AddInventoryItem( NewItem( "starter-sword", "Cracked Militia Sword", "Weapon", 1, 3, 12, 0, 0, "A battered blade, good enough for zombies.", 0, 0 ) );
		AddInventoryItem( NewItem( "starter-boots", "Traveler Boots", "Armor", 2, 2, 0, 2, 7, "Scuffed boots with enough grip for the ash road.", 2, 0 ) );
		_player.Coins = 35;
	}

	private void AddVendorInventory()
	{
		_vendorItems.Add( NewItem( "vendor-wand", "Emberglass Wand", "Weapon", 1, 3, 9, 0, 14, "A cheap focus sold by Sister Kaela.", 0, 0 ) );
		_vendorItems.Add( NewItem( "vendor-axe", "Iron Hatchet", "Weapon", 2, 3, 16, 0, 20, "A compact weapon for close fights.", 0, 0 ) );
		_vendorItems.Add( NewItem( "vendor-charm", "Minor Life Charm", "Charm", 1, 1, 0, 6, 12, "A chipped charm that hums faintly.", 0, 0 ) );
	}

	private void CreateCharacter( string className, string genderName, string name )
	{
		_pendingClass = NormalizeChoice( className, "Warrior", "Mage" );
		_pendingGender = NormalizeChoice( genderName, "Male", "Female" );
		_pendingName = string.IsNullOrWhiteSpace( name ) ? "Asha" : name.Trim();
		_characterCreated = true;
		_player = new PlayerState
		{
			Name = _pendingName,
			Class = string.Equals( _pendingClass, "Mage", StringComparison.OrdinalIgnoreCase ) ? CharacterClass.Mage : CharacterClass.Warrior,
			Gender = string.Equals( _pendingGender, "Female", StringComparison.OrdinalIgnoreCase ) ? Gender.Female : Gender.Male,
			Position = new Vector3( -80f, -60f, 0f ),
			Radius = PlayerRadius,
			MaxHealth = string.Equals( _pendingClass, "Mage", StringComparison.OrdinalIgnoreCase ) ? 90f : 125f,
			Health = string.Equals( _pendingClass, "Mage", StringComparison.OrdinalIgnoreCase ) ? 90f : 125f,
			MaxMana = string.Equals( _pendingClass, "Mage", StringComparison.OrdinalIgnoreCase ) ? 140f : 85f,
			Mana = string.Equals( _pendingClass, "Mage", StringComparison.OrdinalIgnoreCase ) ? 140f : 85f,
			MoveSpeed = 205f,
			CollisionEnabled = true,
			InventoryOpen = false,
			Animation = "Idle"
		};
		BuildWorld();
	}

	private void ResetCharacterCreation()
	{
		_characterCreated = false;
		_player = null;
		_phase = GamePhase.CharacterCreation;
		_lastTooltip = "";
		_lastAnimation = "Idle";
		BuildWorld();
	}

	private void EnsureInGame()
	{
		if ( !_characterCreated )
			CreateCharacter( _pendingClass, _pendingGender, _pendingName );
	}

	private void UpdateGame( float dt, bool readInput )
	{
		if ( dt <= 0f )
			return;

		if ( _hudRoot is null && Time.Now >= _nextHudRetry )
			BuildHud();

		if ( _phase != GamePhase.InGame || _player is null )
		{
			UpdateHud();
			return;
		}

		if ( readInput )
			HandleInput( dt );

		UpdateTimers( dt );
		UpdateMobs( dt );
		UpdateEffects( dt );
		UpdateVisuals();
		UpdateCamera();
		UpdateHud();
	}

	private void HandleInput( float dt )
	{
		if ( ActionPressed( "Inventory", "i" ) )
			_player.InventoryOpen = !_player.InventoryOpen;

		var move = Vector3.Zero;
		if ( ActionDown( "Forward", "w" ) )
			move.x += 1f;
		if ( ActionDown( "Backward", "s" ) )
			move.x -= 1f;
		if ( ActionDown( "Left", "a" ) )
			move.y += 1f;
		if ( ActionDown( "Right", "d" ) )
			move.y -= 1f;

		if ( move.Length > 0.01f && !_player.StationaryAttack && !_player.Casting )
			MovePlayerTo( _player.Position + move.Normal * _player.MoveSpeed * dt );

		var shift = ActionDown( "StationaryAttack", "shift" );
		if ( ActionPressed( "Attack1", "mouse1" ) )
			UseSkill( "left_click", shift );
		if ( ActionPressed( "Attack2", "mouse2" ) )
			UseSkill( "right_click", shift );
		if ( ActionPressed( "Skill1", "1" ) )
			UseSkill( "1", true );
		if ( ActionPressed( "Skill2", "2" ) )
			UseSkill( "2", true );
	}

	private void UpdateTimers( float dt )
	{
		_player.FireblastCooldown = MathF.Max( 0f, _player.FireblastCooldown - dt );
		_player.StationaryAttack = false;
		_player.CollisionDisabledWithEnemies = false;

		for ( var i = _player.Buffs.Count - 1; i >= 0; i-- )
		{
			_player.Buffs[i].Duration -= dt;
			if ( _player.Buffs[i].Name == "Whirlwind" )
			{
				_player.CollisionDisabledWithEnemies = true;
				SpendMana( 8f * dt );
				DamageMobsInRadius( _player.Position, 118f, 10f * dt, "Whirlwind tick", false, 0f );
			}

			if ( _player.Buffs[i].Duration <= 0f )
				_player.Buffs.RemoveAt( i );
		}

		for ( var i = _player.Debuffs.Count - 1; i >= 0; i-- )
		{
			_player.Debuffs[i].Duration -= dt;
			if ( _player.Debuffs[i].Duration <= 0f )
				_player.Debuffs.RemoveAt( i );
		}

		if ( _player.Casting )
		{
			_player.CastRemaining -= dt;
			_player.StationaryAttack = true;
			if ( _player.CastRemaining <= 0f )
				ResolveFrostbolt();
		}

		if ( _chest is not null && _chest.AnimationTimer > 0f )
		{
			_chest.AnimationTimer -= dt;
			if ( _chest.AnimationTimer <= 0f )
				_chest.Animation = "closed_ready_repeat";
		}
	}

	private void UpdateMobs( float dt )
	{
		foreach ( var mob in _mobs.Where( x => x.Alive ) )
		{
			for ( var i = mob.Debuffs.Count - 1; i >= 0; i-- )
			{
				mob.Debuffs[i].Duration -= dt;
				if ( mob.Debuffs[i].Duration <= 0f )
					mob.Debuffs.RemoveAt( i );
			}

			mob.Stunned = mob.Debuffs.Any( x => x.Name == "Stunned" && x.Duration > 0f );
			mob.Slowed = mob.Debuffs.Any( x => x.Name == "Chilled" && x.Duration > 0f );
			var distance = FlatDistance( mob.Position, _player.Position );
			mob.Aggroed = distance <= mob.AggroRadius;

			if ( !mob.Aggroed )
			{
				mob.Animation = mob.Elite ? "Elite Idle" : "Zombie Idle";
				continue;
			}

			if ( mob.Stunned )
			{
				mob.Animation = "Stunned";
				continue;
			}

			if ( distance > mob.AttackRange )
			{
				var speed = mob.MoveSpeed * (mob.Slowed ? 0.45f : 1f);
				var direction = (_player.Position - mob.Position).WithZ( 0f ).Normal;
				var candidate = mob.Position + direction * speed * dt;
				if ( !_player.CollisionDisabledWithEnemies )
					candidate = ResolveCollisionAgainstPlayer( candidate, mob.Radius );
				mob.Position = ClampArena( candidate );
				mob.Animation = mob.Slowed ? "Chilled Shamble" : "Zombie Shamble";
			}
			else
			{
				mob.AttackTimer -= dt;
				if ( mob.AttackTimer <= 0f )
				{
					mob.AttackTimer = mob.Elite ? 1.6f : 1.25f;
					DamagePlayer( mob.Damage, mob.Elite ? "elite slam" : "zombie bite" );
					mob.Animation = mob.Elite ? "Elite Slam" : "Zombie Claw";
				}
			}
		}
	}

	private Vector3 ResolveCollisionAgainstPlayer( Vector3 candidate, float radius )
	{
		var delta = candidate - _player.Position;
		var minDistance = radius + _player.Radius;
		if ( delta.WithZ( 0f ).Length >= minDistance || delta.WithZ( 0f ).Length <= 0.01f )
			return candidate;

		return _player.Position + delta.WithZ( 0f ).Normal * minDistance;
	}

	private void UpdateEffects( float dt )
	{
		for ( var i = _effects.Count - 1; i >= 0; i-- )
		{
			var effect = _effects[i];
			effect.Age += dt;
			if ( effect.Body is not null && effect.Body.IsValid )
			{
				effect.Body.WorldPosition += effect.Velocity * dt;
				var t = 1f - Clamp01( effect.Age / effect.Duration );
				effect.Body.WorldScale = effect.BaseScale * MathF.Max( 0.05f, t );
				if ( effect.Renderer is not null )
					effect.Renderer.Tint = effect.Color.WithAlpha( t );
			}

			if ( effect.Age >= effect.Duration )
			{
				if ( effect.Body is not null && effect.Body.IsValid && !effect.Body.IsDestroyed )
					effect.Body.Destroy();
				_effects.RemoveAt( i );
			}
		}
	}

	private void UseSkill( string skill, bool shiftHeld )
	{
		var normalized = (skill ?? "").Trim().ToLowerInvariant();
		_player.StationaryAttack = shiftHeld;
		var target = NearestAliveMob();

		if ( _player.Class == CharacterClass.Warrior )
		{
			if ( normalized is "left" or "left_click" or "mouse1" )
			{
				PerformDirectAttack( target, "Warrior Sword Slash", 13f, 72f, 0f, "standard attack", shiftHeld );
			}
			else if ( normalized is "right" or "right_click" or "mouse2" )
			{
				PerformDirectAttack( target, "Warrior Heavy Cleave", 23f, 86f, 8f, "alternate cleave", shiftHeld );
			}
			else if ( normalized is "1" or "skill1" or "whirlwind" )
			{
				if ( SpendMana( 14f ) )
				{
					AddOrRefreshBuff( _player.Buffs, "Whirlwind", 2.5f, "Spin through enemies; enemy collision disabled." );
					_player.CollisionDisabledWithEnemies = true;
					_player.Animation = "Warrior Whirlwind Spin";
					_lastAnimation = _player.Animation;
					DamageMobsInRadius( _player.Position, 118f, 14f, "Whirlwind opening hit", false, 0f );
					SpawnImpact( _player.Position + Vector3.Up * 55f, new Color( 0.75f, 0.22f, 0.10f, 1f ), 0.55f );
				}
			}
			else if ( normalized is "2" or "skill2" or "charge" )
			{
				if ( SpendMana( 16f ) )
				{
					var destination = target is null ? _player.Position + new Vector3( 130f, 0f, 0f ) : target.Position - (_player.Position - target.Position).WithZ( 0f ).Normal * 46f;
					MovePlayerTo( destination );
					_player.Animation = "Warrior Shoulder Charge";
					_lastAnimation = _player.Animation;
					DamageMobsInRadius( _player.Position, 105f, 18f, "Charge impact", true, 1.5f );
					SpawnImpact( _player.Position + Vector3.Up * 45f, new Color( 0.95f, 0.72f, 0.28f, 1f ), 0.72f );
				}
			}
		}
		else
		{
			if ( normalized is "left" or "left_click" or "mouse1" )
			{
				PerformDirectAttack( target, "Mage Staff Strike", 9f, 64f, 0f, "staff melee", shiftHeld );
			}
			else if ( normalized is "right" or "right_click" or "mouse2" )
			{
				PerformDirectAttack( target, "Mage Arcane Projectile", 16f, 420f, 6f, "arcane projectile", shiftHeld );
				SpawnImpact( target?.Position + Vector3.Up * 48f ?? _player.Position + new Vector3( 120f, 0f, 60f ), new Color( 0.35f, 0.48f, 1f, 1f ), 0.36f );
			}
			else if ( normalized is "1" or "skill1" or "frostbolt" )
			{
				if ( SpendMana( 18f ) )
				{
					_player.Casting = true;
					_player.CastRemaining = 1.0f;
					_player.PendingCast = "Frostbolt";
					_player.StationaryAttack = true;
					_player.Animation = "Mage Frostbolt One Second Cast";
					_lastAnimation = _player.Animation;
					_lastEvent = "Casting Frostbolt; player must stand still for 1 second.";
				}
			}
			else if ( normalized is "2" or "skill2" or "fireblast" )
			{
				if ( _player.FireblastCooldown > 0f )
				{
					_lastEvent = $"Fireblast is cooling down for {_player.FireblastCooldown:0.0}s.";
				}
				else if ( SpendMana( 20f ) )
				{
					_player.FireblastCooldown = 3f;
					PerformDirectAttack( target, "Mage Fireblast Instant", 34f, 460f, 0f, "fireblast", true );
					SpawnImpact( target?.Position + Vector3.Up * 62f ?? _player.Position + new Vector3( 120f, 0f, 62f ), new Color( 1f, 0.22f, 0.05f, 1f ), 0.64f );
				}
			}
		}

		UpdateHud();
	}

	private void PerformDirectAttack( MobState target, string animation, float damage, float range, float manaCost, string label, bool stationary )
	{
		if ( manaCost > 0f && !SpendMana( manaCost ) )
			return;

		_player.Animation = animation;
		_lastAnimation = animation;
		_player.StationaryAttack = stationary;
		if ( target is not null && target.Alive && FlatDistance( _player.Position, target.Position ) <= range )
		{
			DamageMob( target, damage, label, false, 0f );
			_lastEvent = $"{animation} hit {target.Name} for {damage:0}.";
		}
		else
		{
			_lastEvent = $"{animation} swung at empty air.";
		}
	}

	private void ResolveFrostbolt()
	{
		_player.Casting = false;
		_player.PendingCast = "";
		_player.Animation = "Mage Frostbolt Release";
		_lastAnimation = _player.Animation;
		var target = NearestAliveMob();
		var impact = target?.Position ?? _player.Position + new Vector3( 150f, 0f, 0f );
		DamageMobsInRadius( impact, 120f, 24f, "Frostbolt shatter", false, 0f );
		foreach ( var mob in _mobs.Where( x => x.Alive && FlatDistance( x.Position, impact ) <= 120f ) )
		{
			AddOrRefreshBuff( mob.Debuffs, "Chilled", 2.5f, "Movement speed reduced by Frostbolt." );
		}
		SpawnImpact( impact + Vector3.Up * 52f, new Color( 0.45f, 0.78f, 1f, 1f ), 0.62f );
		_lastEvent = "Frostbolt exploded after a 1 second cast, damaging and chilling enemies.";
	}

	private bool SpendMana( float amount )
	{
		if ( amount <= 0f )
			return true;

		if ( _player.Mana < amount )
		{
			_lastEvent = "Not enough mana.";
			return false;
		}

		_player.Mana = MathF.Max( 0f, _player.Mana - amount );
		return true;
	}

	private void DamagePlayer( float amount, string source )
	{
		_player.Health = MathF.Max( 0f, _player.Health - amount );
		_lastEvent = $"{source} dealt {amount:0} damage to {_player.Name}.";
		if ( _player.Health <= 0f )
			_player.Debuffs.Add( new TimedStatus { Name = "Near Death", Duration = 5f, Description = "Demo death prevention state." } );
	}

	private void DamageMobsInRadius( Vector3 center, float radius, float damage, string source, bool stun, float stunSeconds )
	{
		foreach ( var mob in _mobs.Where( x => x.Alive && FlatDistance( x.Position, center ) <= radius ) )
		{
			DamageMob( mob, damage, source, stun, stunSeconds );
		}
	}

	private void DamageMob( MobState mob, float damage, string source, bool stun, float stunSeconds )
	{
		mob.Health = MathF.Max( 0f, mob.Health - damage );
		mob.Animation = stun ? "Stunned" : "Hit React";
		if ( stun )
		{
			mob.Stunned = true;
			AddOrRefreshBuff( mob.Debuffs, "Stunned", stunSeconds, "Cannot move or attack." );
		}

		if ( mob.Health <= 0f )
		{
			mob.Alive = false;
			mob.Animation = mob.Elite ? "Elite Death" : "Zombie Death";
			mob.Body.Enabled = false;
			DropLootForMob( mob, true );
		}

		_lastEvent = $"{source} hit {mob.Name} for {damage:0}.";
	}

	private void KillMob( bool elite, bool forceHealthOrb )
	{
		var mob = _mobs.FirstOrDefault( x => x.Alive && x.Elite == elite ) ?? _mobs.FirstOrDefault( x => x.Elite == elite );
		if ( mob is null )
			return;

		if ( !mob.Alive )
		{
			DropLootForMob( mob, forceHealthOrb );
			return;
		}

		mob.Health = 0f;
		mob.Alive = false;
		mob.Animation = mob.Elite ? "Elite Death" : "Zombie Death";
		if ( mob.Body is not null && mob.Body.IsValid )
			mob.Body.Enabled = false;
		DropLootForMob( mob, forceHealthOrb );
	}

	private void DropLootForMob( MobState mob, bool forceHealthOrb )
	{
		var coins = mob.Elite ? RandInt( 80, 140 ) : RandInt( 8, 24 );
		_player.Coins += coins;
		_lootLog.Add( $"{mob.Name} dropped {coins} coins." );

		var item = mob.Elite
			? NewItem( $"elite-{RandInt( 1000, 9999 )}", "Gravebound Rare Maul", "Weapon", 2, 3, 26, 4, 68, "Elite rare weapon: +26 damage, +4 vitality.", 0, 0 )
			: NewItem( $"zombie-{RandInt( 1000, 9999 )}", Rand01() > 0.5f ? "Rot-Cleaver Axe" : "Torn Apprentice Sash", Rand01() > 0.5f ? "Weapon" : "Armor", 2, 3, 15, 2, 22, "Dropped by a zombie. Grid-sized Diablo-style item.", 0, 0 );
		AddInventoryItem( item );
		_lootLog.Add( $"{mob.Name} dropped {item.Name}." );

		if ( forceHealthOrb || Rand01() > 0.62f )
			DropHealthOrb( mob.Position + new Vector3( 20f, -20f, 0f ), mob.Elite ? 60f : 30f );
	}

	private void DropHealthOrb( Vector3 position, float amount )
	{
		_healthOrbs.Add( new HealthOrbState
		{
			Id = $"health-orb-{_healthOrbs.Count + 1}",
			Position = position,
			Amount = amount,
			PickedUp = false,
			Body = CreateBox( "Dropped health orb", position + Vector3.Up * 22f, Rotation.Identity, new Vector3( 0.22f, 0.22f, 0.22f ), new Color( 0.9f, 0.05f, 0.08f, 1f ) )
		} );
		_lootLog.Add( $"Health orb dropped for {amount:0} health." );
	}

	private void PickupHealthOrb()
	{
		var orb = _healthOrbs.FirstOrDefault( x => !x.PickedUp );
		if ( orb is null )
		{
			_lastEvent = "No health orb available.";
			return;
		}

		orb.PickedUp = true;
		_player.Health = MathF.Min( _player.MaxHealth, _player.Health + orb.Amount );
		if ( orb.Body is not null && orb.Body.IsValid )
			orb.Body.Enabled = false;
		_lastEvent = $"Picked up a health orb for {orb.Amount:0} health.";
	}

	private void OpenChest()
	{
		var coins = RandInt( _chest.CoinsPerOpenMin, _chest.CoinsPerOpenMax );
		_player.Coins += coins;
		_chest.OpenCount++;
		_chest.Animation = "lid_pop_coin_burst";
		_chest.AnimationTimer = 0.8f;
		_lootLog.Add( $"Repeatable chest opened for {coins} coins." );
		SpawnImpact( _chest.Position + Vector3.Up * 70f, new Color( 1f, 0.74f, 0.18f, 1f ), 0.48f );
		_lastEvent = $"Chest opened. {coins} coins burst out.";
	}

	private void TalkVendor()
	{
		_npc.DialogueOpen = true;
		_npc.VendorOpen = true;
		_lastEvent = _npc.Dialogue;
	}

	private void BuyVendorItem( string id )
	{
		TalkVendor();
		var item = _vendorItems.FirstOrDefault( x => x.Id == id ) ?? _vendorItems.FirstOrDefault();
		if ( item is null )
			return;

		if ( _player.Coins < item.Value )
		{
			_lastEvent = $"Not enough coins to buy {item.Name}.";
			return;
		}

		_player.Coins -= item.Value;
		var clone = NewItem( $"bought-{RandInt( 1000, 9999 )}", item.Name, item.Kind, item.Width, item.Height, item.Damage, item.Vitality, item.Value, item.Tooltip, 0, 0 );
		AddInventoryItem( clone );
		_lastEvent = $"Purchased {item.Name}.";
	}

	private void SellItem( string id )
	{
		var item = FindInventoryItem( id ) ?? _inventory.FirstOrDefault( x => !x.Equipped );
		if ( item is null )
		{
			_lastEvent = "No unequipped item to sell.";
			return;
		}

		_inventory.Remove( item );
		_player.Coins += Math.Max( 1, item.Value / 2 );
		_lastEvent = $"Sold {item.Name}.";
	}

	private void HoverItem( string id )
	{
		var item = FindInventoryItem( id ) ?? _inventory.LastOrDefault();
		_lastTooltip = item is null ? "" : $"{item.Name} | {item.Kind} | {item.Width}x{item.Height} | +{item.Damage} damage | +{item.Vitality} vitality | value {item.Value}";
		_lastEvent = item is null ? "No item tooltip." : $"Tooltip shown for {item.Name}.";
	}

	private void DragItem( string id, int x, int y )
	{
		var item = FindInventoryItem( id ) ?? _inventory.LastOrDefault();
		if ( item is null )
			return;

		if ( CanPlaceItem( item, x, y ) )
		{
			item.X = x;
			item.Y = y;
			item.Equipped = false;
			_lastEvent = $"Dragged {item.Name} to grid {x},{y}.";
		}
		else
		{
			_lastEvent = $"Cannot place {item.Name} at grid {x},{y}.";
		}
	}

	private void EquipItem( string id )
	{
		var item = FindInventoryItem( id ) ?? _inventory.FirstOrDefault( x => x.Kind == "Weapon" );
		if ( item is null )
			return;

		foreach ( var other in _inventory.Where( x => x.Kind == item.Kind ) )
			other.Equipped = false;

		item.Equipped = true;
		_lastEvent = $"Equipped {item.Name} with right click.";
	}

	private bool AddInventoryItem( ItemState item )
	{
		for ( var y = 0; y < InventoryRows; y++ )
		{
			for ( var x = 0; x < InventoryColumns; x++ )
			{
				if ( CanPlaceItem( item, x, y ) )
				{
					item.X = x;
					item.Y = y;
					_inventory.Add( item );
					return true;
				}
			}
		}

		_lastEvent = "Inventory full.";
		return false;
	}

	private bool CanPlaceItem( ItemState item, int x, int y )
	{
		if ( x < 0 || y < 0 || x + item.Width > InventoryColumns || y + item.Height > InventoryRows )
			return false;

		foreach ( var other in _inventory )
		{
			if ( ReferenceEquals( other, item ) || other.Equipped )
				continue;

			var overlap = x < other.X + other.Width && x + item.Width > other.X && y < other.Y + other.Height && y + item.Height > other.Y;
			if ( overlap )
				return false;
		}

		return true;
	}

	private ItemState FindInventoryItem( string id )
	{
		if ( !string.IsNullOrWhiteSpace( id ) )
			return _inventory.FirstOrDefault( x => x.Id == id );

		return null;
	}

	private ItemState NewItem( string id, string name, string kind, int width, int height, int damage, int vitality, int value, string tooltip, int x, int y )
	{
		return new ItemState
		{
			Id = id,
			Name = name,
			Kind = kind,
			Width = width,
			Height = height,
			Damage = damage,
			Vitality = vitality,
			Value = value,
			Tooltip = tooltip,
			X = x,
			Y = y
		};
	}

	private void RunAggroProbe( bool near )
	{
		var mob = _mobs.FirstOrDefault( x => x.Alive && !x.Elite ) ?? _mobs.FirstOrDefault( x => !x.Elite );
		if ( mob is null )
			return;

		_player.Position = near ? mob.Position + new Vector3( mob.AggroRadius - 24f, 0f, 0f ) : mob.Position + new Vector3( mob.AggroRadius + 120f, 0f, 0f );
		UpdateMobs( 0.05f );
		_lastEvent = near ? "Player moved inside zombie aggro radius." : "Player moved outside zombie aggro radius.";
	}

	private void AdvanceSimulation( float seconds )
	{
		var remaining = ClampFloat( seconds, 0f, 8f );
		while ( remaining > 0f )
		{
			var step = MathF.Min( 0.1f, remaining );
			UpdateGame( step, false );
			remaining -= step;
		}
	}

	private void MovePlayerTo( Vector3 position )
	{
		_player.Position = ClampArena( position.WithZ( 0f ) );
		if ( _player.Body is not null && _player.Body.IsValid )
			_player.Body.WorldPosition = _player.Position;
	}

	private Vector3 ClampArena( Vector3 value )
	{
		return new Vector3( ClampFloat( value.x, -ArenaHalfSize, ArenaHalfSize ), ClampFloat( value.y, -ArenaHalfSize, ArenaHalfSize ), 0f );
	}

	private MobState NearestAliveMob()
	{
		return _mobs
			.Where( x => x.Alive )
			.OrderBy( x => FlatDistance( x.Position, _player.Position ) )
			.FirstOrDefault();
	}

	private void AddOrRefreshBuff( List<TimedStatus> statuses, string name, float duration, string description )
	{
		var existing = statuses.FirstOrDefault( x => x.Name == name );
		if ( existing is null )
		{
			statuses.Add( new TimedStatus { Name = name, Duration = duration, Description = description } );
		}
		else
		{
			existing.Duration = duration;
			existing.Description = description;
		}
	}

	private GameObject CreateHumanoidVisual( string name, Vector3 position, Color tint, string animation )
	{
		var root = new GameObject( _runtimeRoot, true, name );
		root.Flags |= GameObjectFlags.NotSaved;
		root.WorldPosition = position;
		root.WorldRotation = Rotation.FromYaw( 0f );

		CreateChildBox( root, $"{name} torso", new Vector3( 0f, 0f, 54f ), Rotation.Identity, new Vector3( 0.42f, 0.30f, 0.72f ), tint );
		CreateChildBox( root, $"{name} head", new Vector3( 0f, 0f, 100f ), Rotation.Identity, new Vector3( 0.25f, 0.23f, 0.25f ), new Color( 0.70f, 0.54f, 0.42f, 1f ) );
		CreateChildBox( root, $"{name} left arm", new Vector3( 0f, 22f, 58f ), Rotation.Identity, new Vector3( 0.14f, 0.12f, 0.42f ), ScaleColor( tint, 0.86f ) );
		CreateChildBox( root, $"{name} right arm", new Vector3( 0f, -22f, 58f ), Rotation.Identity, new Vector3( 0.14f, 0.12f, 0.42f ), ScaleColor( tint, 0.86f ) );
		CreateChildBox( root, $"{name} left leg", new Vector3( -6f, 12f, 20f ), Rotation.Identity, new Vector3( 0.16f, 0.11f, 0.42f ), new Color( 0.08f, 0.08f, 0.10f, 1f ) );
		CreateChildBox( root, $"{name} right leg", new Vector3( -6f, -12f, 20f ), Rotation.Identity, new Vector3( 0.16f, 0.11f, 0.42f ), new Color( 0.08f, 0.08f, 0.10f, 1f ) );
		return root;
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
		var body = CreateBox( "ARPG impact flash", position, Rotation.Identity, new Vector3( size, size, size ), color );
		_effects.Add( new ImpactEffect
		{
			Body = body,
			Renderer = body.GetComponent<ModelRenderer>( true ),
			BaseScale = body.WorldScale,
			Color = color,
			Duration = RandRange( 0.25f, 0.65f ),
			Velocity = new Vector3( RandRange( -20f, 20f ), RandRange( -20f, 20f ), RandRange( 30f, 90f ) )
		} );
	}

	private void BuildCamera()
	{
		_cameraObject = new GameObject( _runtimeRoot, true, "ARPG Cleanroom Camera" );
		_cameraObject.Flags |= GameObjectFlags.NotSaved;
		_camera = _cameraObject.AddComponent<CameraComponent>( true );
		_camera.IsMainCamera = true;
		_camera.Priority = 120;
		_camera.Orthographic = true;
		_camera.OrthographicHeight = 920f;
		_camera.BackgroundColor = new Color( 0.025f, 0.030f, 0.035f, 1f );
		_camera.ZNear = 8f;
		_camera.ZFar = 7000f;
		UpdateCamera();
	}

	private void UpdateCamera()
	{
		if ( _cameraObject is null )
			return;

		var target = _player?.Position ?? Vector3.Zero;
		var focus = target + Vector3.Up * 80f;
		var position = focus + new Vector3( -620f, -760f, 650f );
		_cameraObject.WorldPosition = position;
		_cameraObject.WorldRotation = Rotation.LookAt( (focus - position).Normal, Vector3.Up );
	}

	private void BuildHud()
	{
		if ( _hudRoot is not null )
			return;

		if ( _screenPanel is null )
		{
			var hudObject = new GameObject( _runtimeRoot, true, "ARPG HUD" );
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
		_statusLabel = CreateLabel( _hudRoot, "left: 50%; top: 18px; width: 760px; min-height: 48px; margin-left: -380px; padding: 9px 12px; font-size: 17px; font-weight: 800; color: #f3e6ce; text-align: center; background-color: rgba(7,8,12,0.82); border: 1px solid rgba(188,147,82,0.7); border-radius: 4px; text-shadow: 1px 1px 2px #000;" );
		_dialogueLabel = CreateLabel( _hudRoot, "left: 50%; bottom: 142px; width: 700px; min-height: 38px; margin-left: -350px; padding: 8px 12px; font-size: 15px; font-weight: 700; color: #ffe7b8; text-align: center; background-color: rgba(11,8,6,0.72); border: 1px solid rgba(180,112,42,0.6); border-radius: 4px;" );
		_hotbarLabel = CreateLabel( _hudRoot, "left: 50%; bottom: 36px; width: 520px; height: 54px; margin-left: -260px; padding: 12px; font-size: 16px; font-weight: 800; color: #f1d09a; text-align: center; background-color: rgba(8,8,10,0.84); border: 1px solid rgba(155,121,73,0.72); border-radius: 4px;" );
		_minimapLabel = CreateLabel( _hudRoot, "right: 24px; top: 24px; width: 180px; height: 180px; padding: 10px; font-size: 13px; font-weight: 700; color: #cfe6d4; background-color: rgba(5,12,9,0.82); border: 1px solid rgba(93,154,109,0.75); border-radius: 4px;" );
		_inventoryLabel = CreateLabel( _hudRoot, "right: 24px; bottom: 230px; width: 260px; min-height: 110px; padding: 10px; font-size: 13px; color: #e5d9bd; background-color: rgba(8,7,6,0.86); border: 1px solid rgba(160,126,72,0.65); border-radius: 4px;" );
		_tooltipLabel = CreateLabel( _hudRoot, "left: 24px; bottom: 230px; width: 300px; min-height: 88px; padding: 10px; font-size: 13px; color: #f6e7c1; background-color: rgba(8,7,6,0.86); border: 1px solid rgba(190,160,92,0.72); border-radius: 4px;" );
		_vendorLabel = CreateLabel( _hudRoot, "left: 24px; top: 168px; width: 260px; min-height: 92px; padding: 10px; font-size: 13px; color: #e2d5ad; background-color: rgba(8,8,10,0.80); border: 1px solid rgba(142,120,80,0.66); border-radius: 4px;" );

		var healthOrb = CreatePanel( _hudRoot, "position: absolute; left: 48px; bottom: 34px; width: 112px; height: 112px; border-radius: 56px; background-color: rgba(40,0,0,0.90); border: 3px solid rgba(158,52,34,0.9); overflow: hidden;" );
		_healthOrbFill = CreatePanel( healthOrb, "position: absolute; left: 0; right: 0; bottom: 0; height: 100%; background-color: #b91f21;" );
		var manaOrb = CreatePanel( _hudRoot, "position: absolute; right: 48px; bottom: 34px; width: 112px; height: 112px; border-radius: 56px; background-color: rgba(0,7,34,0.90); border: 3px solid rgba(42,82,170,0.9); overflow: hidden;" );
		_manaOrbFill = CreatePanel( manaOrb, "position: absolute; left: 0; right: 0; bottom: 0; height: 100%; background-color: #2362d1;" );
		_buffRow = CreatePanel( _hudRoot, "position: absolute; left: 50%; bottom: 102px; width: 520px; height: 24px; margin-left: -260px; color: #d7f3b0;" );
		_debuffRow = CreatePanel( _hudRoot, "position: absolute; left: 50%; bottom: 126px; width: 520px; height: 24px; margin-left: -260px; color: #f3b0b0;" );

		if ( _phase == GamePhase.CharacterCreation )
			_characterPanel = CreatePanel( _hudRoot, "position: absolute; left: 50%; top: 96px; width: 520px; height: 168px; margin-left: -260px; padding: 14px; background-color: rgba(8,8,12,0.86); border: 1px solid rgba(190,150,85,0.74); border-radius: 4px;" );

		UpdateHud();
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

	private void UpdateHud()
	{
		if ( _statusLabel is null )
			return;

		if ( _phase == GamePhase.CharacterCreation )
		{
			_statusLabel.Text = $"Create Character | Class: {_pendingClass} | Gender: {_pendingGender} | Name: {_pendingName}";
			_hotbarLabel.Text = "Choose Warrior or Mage, Male or Female, then Create Character";
			_minimapLabel.Text = "Starting zone preview";
			_inventoryLabel.Text = "Inventory unlocks after creation.";
			_tooltipLabel.Text = "Diablo-style grid items, equipment, coins, and vendor trading are verified after creation.";
			_vendorLabel.Text = "";
			_dialogueLabel.Text = _lastEvent;
			SetOrbFill( _healthOrbFill, 1f );
			SetOrbFill( _manaOrbFill, 1f );
			return;
		}

		_statusLabel.Text = $"{_player.Name} the {_player.Gender} {_player.Class} | HP {_player.Health:0}/{_player.MaxHealth:0} | Mana {_player.Mana:0}/{_player.MaxMana:0} | Coins {_player.Coins} | {_lastAnimation}";
		_hotbarLabel.Text = string.Join( "  |  ", GetSkills().Select( skill => $"{skill.Key}: {skill.Name}" ) );
		_minimapLabel.Text = $"Minimap\nPlayer {_player.Position.x:0},{_player.Position.y:0}\nZombies {_mobs.Count( x => x.Alive && !x.Elite )}\nElite {(_mobs.Any( x => x.Alive && x.Elite ) ? "alive" : "down")}\nChest {_chest.OpenCount} opens";
		_inventoryLabel.Text = _player.InventoryOpen
			? $"Inventory {InventoryColumns}x{InventoryRows}\nCoins: {_player.Coins}\nItems: {_inventory.Count}\nEquipped: {string.Join( ", ", _inventory.Where( x => x.Equipped ).Select( x => x.Name ) )}"
			: "Inventory closed. Press I.";
		_tooltipLabel.Text = string.IsNullOrWhiteSpace( _lastTooltip ) ? "Mouse over an item to inspect stats." : _lastTooltip;
		_vendorLabel.Text = _npc is not null && _npc.VendorOpen ? $"Vendor: {_npc.Name}\n{string.Join( "\n", _vendorItems.Select( x => $"{x.Name} - {x.Value}c" ) )}" : "Vendor closed.";
		_dialogueLabel.Text = _lastEvent;
		SetOrbFill( _healthOrbFill, _player.Health / _player.MaxHealth );
		SetOrbFill( _manaOrbFill, _player.Mana / _player.MaxMana );
		_buffRow.Style.Set( "content", string.Join( " | ", _player.Buffs.Select( x => x.Name ) ) );
		_debuffRow.Style.Set( "content", string.Join( " | ", _player.Debuffs.Select( x => x.Name ) ) );
	}

	private void SetOrbFill( Panel panel, float value )
	{
		if ( panel is null )
			return;

		panel.Style.Set( "height", $"{Clamp01( value ) * 100f:0.0}%" );
	}

	private void UpdateVisuals()
	{
		if ( _player?.Body is not null && _player.Body.IsValid )
			_player.Body.WorldPosition = _player.Position;

		foreach ( var mob in _mobs )
		{
			if ( mob.Body is not null && mob.Body.IsValid )
				mob.Body.WorldPosition = mob.Position;
		}
	}

	private object DescribeState( string action )
	{
		return new
		{
			action,
			bridgeVerified = true,
			phase = _phase.ToString(),
			lastAction = _lastAction,
			lastEvent = _lastEvent,
			lastAnimation = _lastAnimation,
			characterCreation = new
			{
				availableClasses = new[] { "Warrior", "Mage" },
				availableGenders = new[] { "Male", "Female" },
				selectedClass = _pendingClass,
				selectedGender = _pendingGender,
				name = _pendingName
			},
			player = _player is null ? null : DescribePlayer(),
			ui = DescribeUi(),
			skills = _player is null ? Array.Empty<object>() : GetSkills().Select( DescribeSkill ).ToArray(),
			combat = new
			{
				playerSpeed = _player?.MoveSpeed ?? 0f,
				zombieCount = _mobs.Count( x => !x.Elite ),
				aliveZombies = _mobs.Count( x => x.Alive && !x.Elite ),
				eliteCount = _mobs.Count( x => x.Elite ),
				aliveEliteCount = _mobs.Count( x => x.Alive && x.Elite ),
				mobs = _mobs.Select( DescribeMob ).ToArray(),
				collision = DescribeCollision()
			},
			inventory = DescribeInventory(),
			chest = _chest is null ? null : new
			{
				position = ToVec( _chest.Position ),
				canRepeat = _chest.CanRepeat,
				openCount = _chest.OpenCount,
				animation = _chest.Animation,
				coinsPerOpenMin = _chest.CoinsPerOpenMin,
				coinsPerOpenMax = _chest.CoinsPerOpenMax
			},
			neutralNpc = _npc is null ? null : new
			{
				name = _npc.Name,
				position = ToVec( _npc.Position ),
				dialogue = _npc.Dialogue,
				dialogueOpen = _npc.DialogueOpen,
				vendorOpen = _npc.VendorOpen,
				collisionEnabled = _npc.CollisionEnabled,
				radius = _npc.Radius
			},
			loot = new
			{
				coins = _player?.Coins ?? 0,
				log = _lootLog.TakeLast( 8 ).ToArray(),
				healthOrbs = _healthOrbs.Select( x => new { id = x.Id, amount = x.Amount, pickedUp = x.PickedUp, position = ToVec( x.Position ) } ).ToArray()
			},
			camera = _cameraObject is null ? null : new
			{
				gameObjectId = _cameraObject.Id.ToString(),
				name = _cameraObject.Name,
				isMainCamera = _camera?.IsMainCamera ?? false,
				orthographic = _camera?.Orthographic ?? false
			}
		};
	}

	private object DescribePlayer()
	{
		return new
		{
			name = _player.Name,
			className = _player.Class.ToString(),
			gender = _player.Gender.ToString(),
			position = ToVec( _player.Position ),
			health = _player.Health,
			maxHealth = _player.MaxHealth,
			healthPercent = _player.Health / _player.MaxHealth,
			mana = _player.Mana,
			maxMana = _player.MaxMana,
			manaPercent = _player.Mana / _player.MaxMana,
			moveSpeed = _player.MoveSpeed,
			animation = _player.Animation,
			stationaryAttack = _player.StationaryAttack,
			casting = _player.Casting,
			castRemaining = _player.CastRemaining,
			fireblastCooldown = _player.FireblastCooldown,
			collisionEnabled = _player.CollisionEnabled,
			collisionDisabledWithEnemies = _player.CollisionDisabledWithEnemies,
			buffs = _player.Buffs.Select( DescribeStatus ).ToArray(),
			debuffs = _player.Debuffs.Select( DescribeStatus ).ToArray()
		};
	}

	private object DescribeUi()
	{
		return new
		{
			characterCreationScreen = _phase == GamePhase.CharacterCreation,
			healthOrb = _player is null ? new { exists = true, percent = 1f } : new { exists = true, percent = _player.Health / _player.MaxHealth },
			manaOrb = _player is null ? new { exists = true, percent = 1f } : new { exists = true, percent = _player.Mana / _player.MaxMana },
			hotkeyBar = GetSkills().Select( x => new { key = x.Key, skill = x.Name } ).ToArray(),
			inventoryOpen = _player?.InventoryOpen ?? false,
			minimap = new { exists = true, zombiePips = _mobs.Count( x => x.Alive ), chestPip = _chest is not null, npcPip = _npc is not null },
			buffRow = _player?.Buffs.Select( x => x.Name ).ToArray() ?? Array.Empty<string>(),
			debuffRow = _player?.Debuffs.Select( x => x.Name ).ToArray() ?? Array.Empty<string>(),
			vendorOpen = _npc?.VendorOpen ?? false,
			dialogueOpen = _npc?.DialogueOpen ?? false,
			tooltip = _lastTooltip
		};
	}

	private object DescribeInventory()
	{
		return new
		{
			open = _player?.InventoryOpen ?? false,
			columns = InventoryColumns,
			rows = InventoryRows,
			coins = _player?.Coins ?? 0,
			itemCount = _inventory.Count,
			items = _inventory.Select( x => new
			{
				id = x.Id,
				name = x.Name,
				kind = x.Kind,
				width = x.Width,
				height = x.Height,
				x = x.X,
				y = x.Y,
				damage = x.Damage,
				vitality = x.Vitality,
				value = x.Value,
				equipped = x.Equipped,
				tooltip = x.Tooltip
			} ).ToArray(),
			equipped = _inventory.Where( x => x.Equipped ).Select( x => new { id = x.Id, name = x.Name, kind = x.Kind } ).ToArray(),
			tooltip = _lastTooltip
		};
	}

	private object DescribeMob( MobState mob )
	{
		return new
		{
			id = mob.Id,
			name = mob.Name,
			elite = mob.Elite,
			alive = mob.Alive,
			position = ToVec( mob.Position ),
			health = mob.Health,
			maxHealth = mob.MaxHealth,
			damage = mob.Damage,
			moveSpeed = mob.MoveSpeed,
			aggroRadius = mob.AggroRadius,
			aggroed = mob.Aggroed,
			stunned = mob.Stunned,
			slowed = mob.Slowed,
			animation = mob.Animation,
			collisionEnabled = mob.CollisionEnabled,
			debuffs = mob.Debuffs.Select( DescribeStatus ).ToArray()
		};
	}

	private object DescribeCollision()
	{
		return new
		{
			defaultPlayerZombie = _player?.CollisionEnabled == true && _mobs.Any( x => x.CollisionEnabled && !x.Elite ),
			defaultPlayerNeutralNpc = _player?.CollisionEnabled == true && _npc?.CollisionEnabled == true,
			whirlwindDisablesEnemyCollision = _player?.Buffs.Any( x => x.Name == "Whirlwind" ) == true,
			collisionDisabledWithEnemies = _player?.CollisionDisabledWithEnemies ?? false,
			playerRadius = _player?.Radius ?? PlayerRadius,
			zombieRadius = ZombieRadius,
			npcRadius = NpcRadius
		};
	}

	private object DescribeSkill( SkillSpec skill )
	{
		return new
		{
			key = skill.Key,
			name = skill.Name,
			binding = skill.Binding,
			manaCost = skill.ManaCost,
			cooldown = skill.Cooldown,
			animation = skill.Animation,
			description = skill.Description
		};
	}

	private object DescribeStatus( TimedStatus status )
	{
		return new
		{
			name = status.Name,
			duration = MathF.Max( 0f, status.Duration ),
			description = status.Description
		};
	}

	private SkillSpec[] GetSkills()
	{
		if ( _player is null || _player.Class == CharacterClass.Warrior )
		{
			return new[]
			{
				new SkillSpec( "LMB", "Standard Attack", "left_click", 0f, 0f, "Warrior Sword Slash", "Basic weapon swing." ),
				new SkillSpec( "RMB", "Alt Attack", "right_click", 8f, 0f, "Warrior Heavy Cleave", "Harder melee hit." ),
				new SkillSpec( "1", "Whirlwind", "1", 14f, 0f, "Warrior Whirlwind Spin", "Deals radial damage over time and disables enemy collision while active." ),
				new SkillSpec( "2", "Charge", "2", 16f, 0f, "Warrior Shoulder Charge", "Rushes into mobs, damages them, and stuns for 1.5 seconds." )
			};
		}

		return new[]
		{
			new SkillSpec( "LMB", "Staff Strike", "left_click", 0f, 0f, "Mage Staff Strike", "Basic melee attack." ),
			new SkillSpec( "RMB", "Arcane Projectile", "right_click", 6f, 0f, "Mage Arcane Projectile", "Shoots a projectile at the cursor." ),
			new SkillSpec( "1", "Frostbolt", "1", 18f, 0f, "Mage Frostbolt One Second Cast", "Stand still for 1 second, then fire an AoE slowing ice bolt." ),
			new SkillSpec( "2", "Fireblast", "2", 20f, 3f, "Mage Fireblast Instant", "Instant single-target fire damage with a 3 second cooldown." )
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

	private string NormalizeChoice( string value, string first, string second )
	{
		if ( string.Equals( value, second, StringComparison.OrdinalIgnoreCase ) )
			return second;

		return first;
	}

	private string GetPayloadString( JsonElement payload, string name, string fallback )
	{
		return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.ValueKind == JsonValueKind.String
			? value.GetString() ?? fallback
			: fallback;
	}

	private float GetPayloadFloat( JsonElement payload, string name, float fallback )
	{
		return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.TryGetSingle( out var result )
			? result
			: fallback;
	}

	private int GetPayloadInt( JsonElement payload, string name, int fallback )
	{
		return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.TryGetInt32( out var result )
			? result
			: fallback;
	}

	private bool GetPayloadBool( JsonElement payload, string name, bool fallback )
	{
		return payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty( name, out var value ) && value.ValueKind is JsonValueKind.True or JsonValueKind.False
			? value.GetBoolean()
			: fallback;
	}

	private object ToVec( Vector3 value )
	{
		return new { x = value.x, y = value.y, z = value.z };
	}

	private float FlatDistance( Vector3 a, Vector3 b )
	{
		return a.WithZ( 0f ).Distance( b.WithZ( 0f ) );
	}

	private float Clamp01( float value )
	{
		return ClampFloat( value, 0f, 1f );
	}

	private float ClampFloat( float value, float min, float max )
	{
		if ( value < min )
			return min;

		if ( value > max )
			return max;

		return value;
	}

	private Color ScaleColor( Color color, float scale )
	{
		return new Color( color.r * scale, color.g * scale, color.b * scale, color.a );
	}

	private float Rand01()
	{
		_rng = _rng * 1664525u + 1013904223u;
		return (_rng & 0x00FFFFFF) / (float)0x01000000;
	}

	private int RandInt( int min, int max )
	{
		return min + (int)MathF.Floor( Rand01() * (max - min + 1) );
	}

	private float RandRange( float min, float max )
	{
		return min + (max - min) * Rand01();
	}

	private sealed class PlayerState
	{
		public string Name = "";
		public CharacterClass Class;
		public Gender Gender;
		public Vector3 Position;
		public float Radius;
		public float MaxHealth;
		public float Health;
		public float MaxMana;
		public float Mana;
		public float MoveSpeed;
		public bool CollisionEnabled;
		public bool CollisionDisabledWithEnemies;
		public bool InventoryOpen;
		public bool StationaryAttack;
		public bool Casting;
		public float CastRemaining;
		public string PendingCast = "";
		public float FireblastCooldown;
		public int Coins;
		public string Animation = "Idle";
		public GameObject Body;
		public readonly List<TimedStatus> Buffs = new();
		public readonly List<TimedStatus> Debuffs = new();
	}

	private sealed class MobState
	{
		public string Id = "";
		public string Name = "";
		public Vector3 Position;
		public float Radius;
		public float MaxHealth;
		public float Health;
		public float Damage;
		public float MoveSpeed;
		public float AggroRadius;
		public float AttackRange;
		public float AttackTimer;
		public bool Elite;
		public bool Alive;
		public bool Aggroed;
		public bool Stunned;
		public bool Slowed;
		public bool CollisionEnabled;
		public string Animation = "";
		public GameObject Body;
		public readonly List<TimedStatus> Debuffs = new();
	}

	private sealed class ChestState
	{
		public Vector3 Position;
		public bool CanRepeat;
		public int OpenCount;
		public int CoinsPerOpenMin;
		public int CoinsPerOpenMax;
		public string Animation = "";
		public float AnimationTimer;
		public GameObject Body;
	}

	private sealed class NeutralNpcState
	{
		public string Name = "";
		public Vector3 Position;
		public string Dialogue = "";
		public bool DialogueOpen;
		public bool VendorOpen;
		public float Radius;
		public bool CollisionEnabled;
		public GameObject Body;
	}

	private sealed class ItemState
	{
		public string Id = "";
		public string Name = "";
		public string Kind = "";
		public int Width;
		public int Height;
		public int X;
		public int Y;
		public int Damage;
		public int Vitality;
		public int Value;
		public bool Equipped;
		public string Tooltip = "";
	}

	private sealed class TimedStatus
	{
		public string Name = "";
		public float Duration;
		public string Description = "";
	}

	private sealed class HealthOrbState
	{
		public string Id = "";
		public Vector3 Position;
		public float Amount;
		public bool PickedUp;
		public GameObject Body;
	}

	private sealed class ImpactEffect
	{
		public GameObject Body;
		public ModelRenderer Renderer;
		public Vector3 BaseScale;
		public Vector3 Velocity;
		public Color Color;
		public float Age;
		public float Duration;
	}

	private readonly record struct SkillSpec( string Key, string Name, string Binding, float ManaCost, float Cooldown, string Animation, string Description );

	private enum GamePhase
	{
		CharacterCreation,
		InGame
	}

	private enum CharacterClass
	{
		Warrior,
		Mage
	}

	private enum Gender
	{
		Male,
		Female
	}
}
