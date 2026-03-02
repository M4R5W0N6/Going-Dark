namespace TPSBR
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.Animations;
	using UnityEngine.Playables;
	using Fusion;
	using Fusion.Addons.KCC;
	using Fusion.Addons.AnimationController;
	using FusionAnimator;
	using RootMotion.FinalIK;

	[DefaultExecutionOrder(3)]
	public sealed class CharacterAnimationController : AnimationController
	{
		private enum FusionLayerRole
		{
			Base,
			LowerBody,
			UpperBody,
			Shoot,
			FullBody,
			Look,
			Top,
		}

		private const float UPPER_BODY_EQUIP_ARM_TIME = 0.4f;
		private const float UPPER_BODY_UNEQUIP_DISARM_TIME = 0.5f;
		private const float UPPER_BODY_UNEQUIP_SWITCH_TIME = 1.0f;
		private const float UPPER_BODY_RELOAD_DISARM_TIME = 0.05f;
		private const float UPPER_BODY_THROW_START_TIME = 0.2f;
		private const float UPPER_BODY_GRENDE_EQUIP_TIME = 0.5f;
		private const float UPPER_BODY_GRENDE_THROW_FIRE_TIME = 0.45f;
		private const float UPPER_BODY_RELOAD_EXIT_TIME = 0.9f;
		private const float UPPER_BODY_RELOAD_RETURN_TIME = 0.05f;
		private const float SHOOT_TRIGGER_DURATION = 0.05f;
		// PRIVATE MEMBERS

		[SerializeField]
		private Transform       _leftHand;
		[SerializeField]
		private Transform       _leftLowerArm;
		[SerializeField]
		private Transform       _leftUpperArm;
		[SerializeField]
		private FullBodyBipedIK _fullBodyIK;
		[SerializeField]
		private bool            _enableLeftHandIK = false;
		[SerializeField][Range(0.0f, 1.0f)]
		private float           _aimSnapPower = 0.5f;
		[SerializeField]
		private FusionAnimatorGraphAsset _fusionAnimatorGraph;

		private KCC             _kcc;
		private Agent           _agent;
		private Weapons         _weapons;
		private Jetpack         _jetpack;

		private LocomotionLayer _locomotion;
		private FullBodyLayer   _fullBody;
		private LowerBodyLayer  _lowerBody;
		private UpperBodyLayer  _upperBody;
		private ShootLayer      _shoot;
		private LookLayer       _look;
		private CharacterAnimStateMachine _stateMachine = new CharacterAnimStateMachine();

		private FusionAnimatorRuntimeGraphInstance _fusionRuntimeGraphInstance;
		private FusionAnimatorGraphAsset           _fusionRuntimeGraphAsset;
		private readonly FusionAnimatorParameterStore _fusionParameters = new FusionAnimatorParameterStore();
		private readonly Dictionary<string, FusionAnimatorStateDefinition> _fusionStatesById = new Dictionary<string, FusionAnimatorStateDefinition>(StringComparer.Ordinal);
		private readonly Dictionary<string, string> _fusionLayerIdsByName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, string> _fusionLayerIdsByAlias = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<FusionLayerRole, string> _fusionLayerIdsByRole = new Dictionary<FusionLayerRole, string>();
		private readonly Dictionary<string, FusionAnimatorParameterDefinition> _fusionParameterDefinitionsById = new Dictionary<string, FusionAnimatorParameterDefinition>(StringComparer.Ordinal);
		private readonly Dictionary<string, string> _fusionBoundRuntimeParameterIds = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly Dictionary<string, FusionPlayableLayerBinding> _fusionPlayableLayersById = new Dictionary<string, FusionPlayableLayerBinding>(StringComparer.Ordinal);
		private readonly Dictionary<string, FusionPlayableStateBinding> _fusionPlayableStatesById = new Dictionary<string, FusionPlayableStateBinding>(StringComparer.Ordinal);
		private readonly FusionMoveSpeedProvider _fusionMoveSpeedProvider = new FusionMoveSpeedProvider();
		private FusionAnimatorGraphAsset _fusionPlayableGraphAsset;
		private bool _fusionPlayablesInitialized;

		[Networked]
		private NetworkBool _fusionIsDead { get; set; }
		[Networked]
		private NetworkBool _fusionReloadPending { get; set; }
		[Networked]
		private NetworkBool _fusionUnequipPending { get; set; }
			[Networked]
			private NetworkBool _fusionEquipPending { get; set; }
			[Networked]
			private NetworkBool _fusionGrenadeEquipPending { get; set; }
			[Networked]
			private NetworkBool _fusionThrowStartPending { get; set; }
		[Networked]
		private NetworkBool _fusionThrowHold { get; set; }
		[Networked]
		private float _fusionThrowStartTimer { get; set; }
		[Networked]
		private NetworkBool _fusionShootPending { get; set; }
		[Networked]
		private float _fusionShootTimer { get; set; }
		private bool _fusionUnequipDisarmApplied;
		private bool _fusionEquipArmApplied;
		private bool _fusionGrenadeEquipArmApplied;
		private bool _fusionGrenadeArmProjectileApplied;
		private bool _fusionGrenadeThrowFireApplied;
		private string _fusionUpperBodyStateId = string.Empty;
		private bool _fusionJetpackSwitchQueued;
		private bool _fusionJetpackDisarmApplied;
		[Networked]
		private byte _fusionJetpackResumeWeaponSlot { get; set; }
		[Networked]
		private byte _fusionLastArmedWeaponSlot { get; set; }
		[Networked]
		private byte _fusionWeaponCycleTargetSlot { get; set; }
		[Networked]
		private NetworkBool _fusionWeaponCycleActive { get; set; }
		[Networked]
		private float _fusionTurnDirection { get; set; }
		[Networked]
		private float _fusionTurnRemainingTime { get; set; }
		[Networked]
		private float _fusionTurnAnimationTime { get; set; }
		private readonly List<FusionRootTransformBinding> _fusionRootTransformBindings = new List<FusionRootTransformBinding>(8);

		private sealed class FusionPlayableLayerBinding
		{
			public FusionAnimatorLayerDefinition LayerDefinition;
			public AnimationMixerPlayable Mixer;
			public int ControllerPort = -1;
			public readonly List<string> StateIds = new List<string>(32);
		}

		private sealed class FusionPlayableStateBinding
		{
			public FusionAnimatorStateDefinition StateDefinition;
			public string LayerId;
			public int LayerPort = -1;
			public AnimationMixerPlayable Mixer;
			public readonly List<AnimationClipPlayable> ClipPlayables = new List<AnimationClipPlayable>(16);
			public readonly List<FusionAnimatorClipSlot> ClipSlots = new List<FusionAnimatorClipSlot>(16);
			public readonly List<FusionAnimatorBlendTreeChild> BlendTreeChildren = new List<FusionAnimatorBlendTreeChild>(16);
			public float[] WeightBuffer;
		}

		private sealed class FusionRootTransformBinding
		{
			public Transform Transform;
			public Vector3 LocalPosition;
			public Quaternion LocalRotation;
			public Vector3 LocalScale;
		}

		private sealed class FusionMoveSpeedProvider : IMoveSpeedProvider
		{
			private const float BaseSpeedScale = 2.0f;

			private Weapons _weapons;
			private readonly Dictionary<int, DirectionalSpeedTable> _tablesBySlot = new Dictionary<int, DirectionalSpeedTable>(4);
			private float _fallbackMaxMagnitude = 1.0f;

			private sealed class DirectionalSpeedTable
			{
				public float[] Angles = Array.Empty<float>();
				public float[] Magnitudes = Array.Empty<float>();
				public float MaxMagnitude = 1.0f;
			}

			public void Initialize(Weapons weapons, FusionAnimatorGraphAsset graph)
			{
				_weapons = weapons;
				RebuildTables(graph);
			}

			public void RebuildTables(FusionAnimatorGraphAsset graph)
			{
				_tablesBySlot.Clear();
				_fallbackMaxMagnitude = 1.0f;

				if (graph == null || graph.States == null)
				{
					return;
				}

				for (int i = 0; i < graph.States.Count; ++i)
				{
					FusionAnimatorStateDefinition state = graph.States[i];
					if (state == null ||
						state.MotionType != FusionAnimatorMotionType.BlendTree ||
						state.BlendTree == null ||
						state.BlendTree.Children == null ||
						state.BlendTree.Children.Count == 0)
					{
						continue;
					}

					if (IsLocomotionSpeedState(state.Name) == false)
					{
						continue;
					}

					int slotIndex = ResolveSlotIndexFromStateName(state.Name);
					DirectionalSpeedTable table = BuildDirectionalSpeedTable(state.BlendTree.Children);
					if (table == null)
					{
						continue;
					}

					if (_tablesBySlot.TryGetValue(slotIndex, out DirectionalSpeedTable existingTable) == true && existingTable != null)
					{
						MergeDirectionalSpeedTable(existingTable, table);
					}
					else
					{
						_tablesBySlot[slotIndex] = table;
					}

					_fallbackMaxMagnitude = Mathf.Max(_fallbackMaxMagnitude, table.MaxMagnitude);
				}
			}

			public float GetBaseSpeed(Vector2 localNormalizedDirection, float multiplier)
			{
				if (localNormalizedDirection.sqrMagnitude <= 0.000001f)
				{
					return 0.0f;
				}

				if (Mathf.Approximately(multiplier, 0.0f))
				{
					multiplier = GetMultiplier();
				}

				float maxBaseSpeed = ResolveMaxBaseSpeed(localNormalizedDirection.normalized);
				return maxBaseSpeed * Mathf.Max(0.0f, multiplier) * BaseSpeedScale;
			}

			private float ResolveMaxBaseSpeed(Vector2 localNormalizedDirection)
			{
				int slotIndex = ResolveCurrentSlotIndex();
				if (_tablesBySlot.TryGetValue(slotIndex, out DirectionalSpeedTable table) == false)
				{
					_tablesBySlot.TryGetValue(0, out table);
				}

				if (table == null || table.Angles == null || table.Magnitudes == null || table.Angles.Length == 0 || table.Magnitudes.Length == 0)
				{
					return _fallbackMaxMagnitude;
				}

				if (table.Angles.Length == 1 || table.Magnitudes.Length == 1)
				{
					return Mathf.Max(0.0f, table.MaxMagnitude);
				}

				float angle = Vector2.SignedAngle(Vector2.up, localNormalizedDirection);
				int sampleCount = Mathf.Min(table.Angles.Length, table.Magnitudes.Length);
				int nextIndex = 0;
				while (nextIndex < sampleCount && table.Angles[nextIndex] < angle)
				{
					nextIndex++;
				}

				int fromIndex = nextIndex - 1;
				if (fromIndex < 0)
				{
					fromIndex = sampleCount - 1;
				}

				if (nextIndex >= sampleCount)
				{
					nextIndex = 0;
				}

				float fromAngle = table.Angles[fromIndex];
				float toAngle = table.Angles[nextIndex];
				float queryAngle = angle;

				if (nextIndex == 0)
				{
					toAngle += 360.0f;
				}

				if (queryAngle < fromAngle)
				{
					queryAngle += 360.0f;
				}

				float t = Mathf.Approximately(fromAngle, toAngle) ? 0.0f : Mathf.Clamp01((queryAngle - fromAngle) / (toAngle - fromAngle));
				float fromMagnitude = table.Magnitudes[fromIndex];
				float toMagnitude = table.Magnitudes[nextIndex];
				return Mathf.Max(0.0f, Mathf.Lerp(fromMagnitude, toMagnitude, t));
			}

			private int ResolveCurrentSlotIndex()
			{
				int slot = _weapons != null ? _weapons.CurrentWeaponSlot : 0;
				if (slot > 2)
				{
					slot = 0;
				}

				if (slot < 0)
				{
					slot = 0;
				}

				return slot;
			}

			private static int ResolveSlotIndexFromStateName(string stateName)
			{
				if (string.IsNullOrWhiteSpace(stateName))
				{
					return 0;
				}

				int open = stateName.LastIndexOf('(');
				int close = stateName.LastIndexOf(')');
				if (open >= 0 && close > open)
				{
					string slotLabel = stateName.Substring(open + 1, close - open - 1).Trim();
					if (slotLabel.Equals("Unarmed", StringComparison.OrdinalIgnoreCase))
					{
						return 0;
					}
					if (slotLabel.Equals("Pistol", StringComparison.OrdinalIgnoreCase))
					{
						return 1;
					}
					if (slotLabel.Equals("Rifle", StringComparison.OrdinalIgnoreCase))
					{
						return 2;
					}
				}

				if (stateName.IndexOf("Pistol", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return 1;
				}
				if (stateName.IndexOf("Rifle", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return 2;
				}

				return 0;
			}

			private static bool IsLocomotionSpeedState(string stateName)
			{
				if (string.IsNullOrWhiteSpace(stateName))
				{
					return false;
				}

				string canonicalName = GetCanonicalFusionStateName(stateName);
				if (string.IsNullOrWhiteSpace(canonicalName))
				{
					return false;
				}

				if (canonicalName.IndexOf("Look", StringComparison.OrdinalIgnoreCase) >= 0 ||
					canonicalName.IndexOf("Turn", StringComparison.OrdinalIgnoreCase) >= 0 ||
					canonicalName.IndexOf("Jump", StringComparison.OrdinalIgnoreCase) >= 0 ||
					canonicalName.IndexOf("Fall", StringComparison.OrdinalIgnoreCase) >= 0 ||
					canonicalName.IndexOf("Land", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return false;
				}

				if (canonicalName.IndexOf("Move", StringComparison.OrdinalIgnoreCase) >= 0 ||
					canonicalName.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
					canonicalName.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return true;
				}

				return stateName.StartsWith("Move/", StringComparison.OrdinalIgnoreCase);
			}

			private static DirectionalSpeedTable BuildDirectionalSpeedTable(List<FusionAnimatorBlendTreeChild> children)
			{
				if (children == null || children.Count == 0)
				{
					return null;
				}

				var angleToMagnitude = new Dictionary<int, float>(16);
				float maxMagnitude = 0.0f;

				for (int i = 0; i < children.Count; ++i)
				{
					FusionAnimatorBlendTreeChild child = children[i];
					if (child == null)
					{
						continue;
					}

					float magnitude = child.Position.magnitude;
					if (magnitude <= 0.0001f)
					{
						continue;
					}

					Vector2 direction = child.Position.normalized;
					float angle = Vector2.SignedAngle(Vector2.up, direction);
					int key = Mathf.RoundToInt(angle * 1000.0f);

					if (angleToMagnitude.TryGetValue(key, out float existingMagnitude) == false || magnitude > existingMagnitude)
					{
						angleToMagnitude[key] = magnitude;
					}

					maxMagnitude = Mathf.Max(maxMagnitude, magnitude);
				}

				if (angleToMagnitude.Count == 0)
				{
					return null;
				}

				var entries = new List<KeyValuePair<int, float>>(angleToMagnitude);
				entries.Sort((a, b) => a.Key.CompareTo(b.Key));

				var table = new DirectionalSpeedTable
				{
					Angles = new float[entries.Count],
					Magnitudes = new float[entries.Count],
					MaxMagnitude = Mathf.Max(0.0f, maxMagnitude),
				};

				for (int i = 0; i < entries.Count; ++i)
				{
					table.Angles[i] = entries[i].Key / 1000.0f;
					table.Magnitudes[i] = Mathf.Max(0.0f, entries[i].Value);
				}

				return table;
			}

			private static void MergeDirectionalSpeedTable(DirectionalSpeedTable target, DirectionalSpeedTable source)
			{
				if (target == null || source == null)
				{
					return;
				}

				var angleToMagnitude = new Dictionary<int, float>(32);

				for (int i = 0; i < target.Angles.Length && i < target.Magnitudes.Length; ++i)
				{
					int key = Mathf.RoundToInt(target.Angles[i] * 1000.0f);
					angleToMagnitude[key] = Mathf.Max(0.0f, target.Magnitudes[i]);
				}

				for (int i = 0; i < source.Angles.Length && i < source.Magnitudes.Length; ++i)
				{
					int key = Mathf.RoundToInt(source.Angles[i] * 1000.0f);
					float sourceMagnitude = Mathf.Max(0.0f, source.Magnitudes[i]);
					if (angleToMagnitude.TryGetValue(key, out float existingMagnitude) == false || sourceMagnitude > existingMagnitude)
					{
						angleToMagnitude[key] = sourceMagnitude;
					}
				}

				if (angleToMagnitude.Count == 0)
				{
					return;
				}

				var entries = new List<KeyValuePair<int, float>>(angleToMagnitude);
				entries.Sort((a, b) => a.Key.CompareTo(b.Key));

				target.Angles = new float[entries.Count];
				target.Magnitudes = new float[entries.Count];
				for (int i = 0; i < entries.Count; ++i)
				{
					target.Angles[i] = entries[i].Key / 1000.0f;
					target.Magnitudes[i] = entries[i].Value;
				}

				target.MaxMagnitude = Mathf.Max(Mathf.Max(0.0f, target.MaxMagnitude), Mathf.Max(0.0f, source.MaxMagnitude));
			}

			private float GetMultiplier()
			{
				switch (_weapons != null ? _weapons.CurrentWeaponSlot : 0)
				{
					case 0: { return 1.0f; }
					case 1: { return 0.95f; }
					case 2: { return 0.9f; }
				}

				return 0.95f;
			}
		}

		// PUBLIC METHODS

		public bool CanJump()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				return CanJumpFusion();
			}

			return _stateMachine.CanJump();
		}

		public bool CanSwitchWeapons(bool force)
		{
			if (UseFusionAnimatorRuntime == true)
			{
				return CanSwitchWeaponsFusion(force);
			}

			return _stateMachine.CanSwitchWeapons(force);
		}

		public void SetDead(bool isDead)
		{
			if (UseFusionAnimatorRuntime == true)
			{
				SetDeadFusion(isDead);
				return;
			}

			_stateMachine.SetDead(isDead);
		}

		public bool StartFire()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				return StartFireFusion();
			}

			return _stateMachine.StartFire();
		}

		public void ProcessThrow(bool start, bool hold)
		{
			if (UseFusionAnimatorRuntime == true)
			{
				ProcessThrowFusion(start, hold);
				return;
			}

			_stateMachine.ProcessThrow(start, hold);
		}

		public bool StartReload()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				return StartReloadFusion();
			}

			return _stateMachine.StartReload();
		}

		public void SwitchWeapons()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				SwitchWeaponsFusion();
				return;
			}

			_stateMachine.SwitchWeapons();
		}

		public void Turn(float angle)
		{
			if (UseFusionAnimatorRuntime == true)
			{
				TurnFusion(angle);
				return;
			}

			_stateMachine.Turn(angle);
		}

		public void RefreshSnapping()
		{
			SnapWeapon();
		}

		// AnimationController INTERFACE

		protected override bool UseBuiltInLayerEvaluation => UseFusionAnimatorRuntime == false;

		protected override void OnSpawned()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			}
			else if (HasStateAuthority == true)
			{
				Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			}

			if (UseFusionAnimatorRuntime == false)
			{
				_stateMachine.OnSpawned();
			}

			if (UseFusionAnimatorRuntime == true)
			{
				EnsureFusionAnimatorRuntimeInitialized();
				EnsureFusionPlayableGraphBindings();
				ApplyFusionRootMotionPolicy(forceRefreshBindings: true);
				_fusionRuntimeGraphInstance?.Reset();
				if (HasStateAuthority == true)
				{
					ResetFusionRuntimeRequests();

					if (_weapons != null && _weapons.IsSwitchingWeapon() == true)
					{
						SwitchWeaponsFusion();
					}
				}
				else
				{
					ResetFusionUpperBodySideEffectFlags();
				}
			}
		}

		protected override void OnFixedUpdate()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				OnFusionFixedUpdate();
				return;
			}

			_stateMachine.OnFixedUpdate();
		}

		protected override void OnEvaluate()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				ApplyFusionRootMotionPolicy();
			}

			SnapWeapon();
		}

		protected override void OnInterpolate()
		{
			if (UseFusionAnimatorRuntime == false)
				return;

			OnFusionRenderUpdate();
		}

		// MonoBehaviour INTERFACE

		protected override void Awake()
		{
			base.Awake();

			_kcc        = this.GetComponentNoAlloc<KCC>();
			_agent      = this.GetComponentNoAlloc<Agent>();
			_weapons    = this.GetComponentNoAlloc<Weapons>();
			_jetpack    = this.GetComponentNoAlloc<Jetpack>();
			_fullBodyIK = _fullBodyIK != null ? _fullBodyIK : this.GetComponentNoAlloc<FullBodyBipedIK>();

			_locomotion = FindLayer<LocomotionLayer>();
			_fullBody   = FindLayer<FullBodyLayer>();
			_lowerBody  = FindLayer<LowerBodyLayer>();
			_upperBody  = FindLayer<UpperBodyLayer>();
			_shoot      = FindLayer<ShootLayer>();
			_look       = FindLayer<LookLayer>();

			_fusionMoveSpeedProvider.Initialize(_weapons, _fusionAnimatorGraph);
			if (_kcc != null)
			{
				_kcc.MoveState = UseFusionAnimatorRuntime == true
					? _fusionMoveSpeedProvider
					: _locomotion != null ? _locomotion.FindState<MoveState>() : null;
			}

			_stateMachine.Initialize(this, _kcc, _weapons, _jetpack, _locomotion, _fullBody, _upperBody, _lowerBody, _shoot, _look);
			EnsureFusionAnimatorRuntimeInitialized();
			ApplyFusionRootMotionPolicy(forceRefreshBindings: true);
		}

		// PRIVATE METHODS

		private bool UseFusionAnimatorRuntime
		{
			get
			{
				return _fusionAnimatorGraph != null;
			}
		}

		private void EnsureFusionAnimatorRuntimeInitialized()
		{
			if (UseFusionAnimatorRuntime == false)
			{
				if (_kcc != null)
				{
					_kcc.MoveState = _locomotion != null ? _locomotion.FindState<MoveState>() : null;
				}

				ClearFusionPlayableGraphBindings();
				_fusionRuntimeGraphInstance = null;
				_fusionRuntimeGraphAsset = null;
				_fusionStatesById.Clear();
				_fusionLayerIdsByName.Clear();
				_fusionLayerIdsByAlias.Clear();
				_fusionLayerIdsByRole.Clear();
				_fusionParameterDefinitionsById.Clear();
				_fusionBoundRuntimeParameterIds.Clear();
				_fusionTurnDirection = 0.0f;
				ClearFusionRootMotionBindings();
				ResetFusionUpperBodySideEffectFlags();
				return;
			}

			if (_kcc != null)
			{
				_kcc.MoveState = _fusionMoveSpeedProvider;
			}

			if (ReferenceEquals(_fusionRuntimeGraphAsset, _fusionAnimatorGraph) == true &&
				_fusionRuntimeGraphInstance != null)
			{
				ApplyFusionRootMotionPolicy();
				return;
			}

			ClearFusionPlayableGraphBindings();
			_fusionRuntimeGraphAsset = _fusionAnimatorGraph;
			_fusionMoveSpeedProvider.Initialize(_weapons, _fusionAnimatorGraph);
			_fusionRuntimeGraphInstance = new FusionAnimatorRuntimeGraphInstance(_fusionAnimatorGraph);
			_fusionParameters.SetDefaults(_fusionAnimatorGraph);
			RebuildFusionAnimatorBindings();
			ApplyFusionRootMotionPolicy(forceRefreshBindings: true);
		}

		private void RebuildFusionAnimatorBindings()
		{
			_fusionStatesById.Clear();
			_fusionLayerIdsByName.Clear();
			_fusionLayerIdsByAlias.Clear();
			_fusionLayerIdsByRole.Clear();
			_fusionParameterDefinitionsById.Clear();
			_fusionBoundRuntimeParameterIds.Clear();

			if (_fusionAnimatorGraph == null)
			{
				return;
			}

			if (_fusionAnimatorGraph.Parameters != null)
			{
				for (int i = 0; i < _fusionAnimatorGraph.Parameters.Count; ++i)
				{
					FusionAnimatorParameterDefinition parameter = _fusionAnimatorGraph.Parameters[i];
					if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
					{
						continue;
					}

					_fusionParameterDefinitionsById[parameter.Id] = parameter;
				}
			}

			if (_fusionAnimatorGraph.Layers != null)
			{
				for (int i = 0; i < _fusionAnimatorGraph.Layers.Count; ++i)
				{
					FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
					if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
					{
						continue;
					}

					if (string.IsNullOrWhiteSpace(layer.Name) == false)
					{
						_fusionLayerIdsByName[layer.Name] = layer.Id;
					}
				}
			}

			if (_fusionAnimatorGraph.States == null)
			{
				RebuildFusionLayerRoleBindings();
				RebuildFusionRuntimeParameterBindings();
				return;
			}

			for (int i = 0; i < _fusionAnimatorGraph.States.Count; ++i)
			{
				FusionAnimatorStateDefinition state = _fusionAnimatorGraph.States[i];
				if (state == null || string.IsNullOrWhiteSpace(state.Id))
				{
					continue;
				}

				_fusionStatesById[state.Id] = state;
			}

			RebuildFusionLayerRoleBindings();
			RebuildFusionRuntimeParameterBindings();
		}

		private void RebuildFusionLayerRoleBindings()
		{
			_fusionLayerIdsByAlias.Clear();
			_fusionLayerIdsByRole.Clear();

			if (_fusionAnimatorGraph == null || _fusionAnimatorGraph.Layers == null)
			{
				return;
			}

			for (int i = 0; i < _fusionAnimatorGraph.Layers.Count; ++i)
			{
				FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
				if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
				{
					continue;
				}

				AddFusionLayerAlias(layer.Name, layer.Id);
			}

			string baseLayerId = ResolveFusionLayerIdByAliases("base", "default", "locomotion");
			if (string.IsNullOrWhiteSpace(baseLayerId))
			{
				baseLayerId = ResolveFusionFirstLayerIdByPriority();
			}
			if (string.IsNullOrWhiteSpace(baseLayerId))
			{
				baseLayerId = ResolveFusionLayerIdByRoleScore(FusionLayerRole.Base);
			}

			string lowerBodyLayerId = ResolveFusionLayerIdByAliases("lowerbody", "lower", "locomotion");
			if (string.IsNullOrWhiteSpace(lowerBodyLayerId))
			{
				lowerBodyLayerId = ResolveFusionLayerIdByRoleScore(FusionLayerRole.LowerBody);
			}
			if (string.IsNullOrWhiteSpace(lowerBodyLayerId))
			{
				lowerBodyLayerId = baseLayerId;
			}

			string upperBodyLayerId = ResolveFusionLayerIdByAliases("upperbody", "upper", "weapon");
			if (string.IsNullOrWhiteSpace(upperBodyLayerId))
			{
				upperBodyLayerId = ResolveFusionLayerIdByRoleScore(FusionLayerRole.UpperBody);
			}

			string shootLayerId = ResolveFusionLayerIdByAliases("shoot", "shootlayer");
			if (string.IsNullOrWhiteSpace(shootLayerId))
			{
				shootLayerId = ResolveFusionLayerIdByRoleScore(FusionLayerRole.Shoot);
			}
			if (string.IsNullOrWhiteSpace(shootLayerId))
			{
				shootLayerId = upperBodyLayerId;
			}

			string lookLayerId = ResolveFusionLayerIdByAliases("look", "looklayer");
			if (string.IsNullOrWhiteSpace(lookLayerId))
			{
				lookLayerId = ResolveFusionLayerIdByRoleScore(FusionLayerRole.Look);
			}

			string topLayerId = ResolveFusionLayerIdByAliases("top");
			if (string.IsNullOrWhiteSpace(topLayerId))
			{
				topLayerId = ResolveFusionLayerIdByRoleScore(FusionLayerRole.Top);
			}

			string fullBodyLayerId = ResolveFusionLayerIdByAliases("fullbody", "full");
			if (string.IsNullOrWhiteSpace(fullBodyLayerId))
			{
				fullBodyLayerId = ResolveFusionLayerIdByRoleScore(FusionLayerRole.FullBody);
			}
			if (string.IsNullOrWhiteSpace(fullBodyLayerId))
			{
				fullBodyLayerId = string.IsNullOrWhiteSpace(topLayerId) == false ? topLayerId : baseLayerId;
			}

			SetFusionLayerRole(FusionLayerRole.Base, baseLayerId, "base");
			SetFusionLayerRole(FusionLayerRole.LowerBody, lowerBodyLayerId, "lowerbody", "lower");
			SetFusionLayerRole(FusionLayerRole.UpperBody, upperBodyLayerId, "upperbody", "upper");
			SetFusionLayerRole(FusionLayerRole.Shoot, shootLayerId, "shoot");
			SetFusionLayerRole(FusionLayerRole.Look, lookLayerId, "look");
			SetFusionLayerRole(FusionLayerRole.Top, topLayerId, "top");
			SetFusionLayerRole(FusionLayerRole.FullBody, fullBodyLayerId, "fullbody", "full");
		}

		private string ResolveFusionFirstLayerIdByPriority()
		{
			if (_fusionAnimatorGraph == null || _fusionAnimatorGraph.Layers == null || _fusionAnimatorGraph.Layers.Count == 0)
			{
				return string.Empty;
			}

			string firstLayerId = string.Empty;
			int firstPriority = int.MaxValue;
			int firstOrder = int.MaxValue;

			for (int i = 0; i < _fusionAnimatorGraph.Layers.Count; ++i)
			{
				FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
				if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
				{
					continue;
				}

				int priority = layer.Priority;
				if (string.IsNullOrWhiteSpace(firstLayerId) ||
					priority < firstPriority ||
					(priority == firstPriority && i < firstOrder))
				{
					firstLayerId = layer.Id;
					firstPriority = priority;
					firstOrder = i;
				}
			}

			return firstLayerId;
		}

		private string ResolveFusionLayerIdByAliases(params string[] aliases)
		{
			if (aliases == null || aliases.Length == 0)
			{
				return string.Empty;
			}

			for (int i = 0; i < aliases.Length; ++i)
			{
				string alias = aliases[i];
				if (string.IsNullOrWhiteSpace(alias))
				{
					continue;
				}

				if (_fusionLayerIdsByName.TryGetValue(alias, out string exactLayerId) == true &&
					string.IsNullOrWhiteSpace(exactLayerId) == false)
				{
					return exactLayerId;
				}

				string normalizedAlias = NormalizeFusionParameterToken(alias);
				if (string.IsNullOrWhiteSpace(normalizedAlias) == false &&
					_fusionLayerIdsByAlias.TryGetValue(normalizedAlias, out string normalizedLayerId) == true &&
					string.IsNullOrWhiteSpace(normalizedLayerId) == false)
				{
					return normalizedLayerId;
				}
			}

			return string.Empty;
		}

		private string ResolveFusionLayerIdByRoleScore(FusionLayerRole role)
		{
			if (_fusionStatesById == null || _fusionStatesById.Count == 0)
			{
				return string.Empty;
			}

			var scoresByLayerId = new Dictionary<string, int>(StringComparer.Ordinal);

			foreach (KeyValuePair<string, FusionAnimatorStateDefinition> pair in _fusionStatesById)
			{
				FusionAnimatorStateDefinition state = pair.Value;
				if (state == null || string.IsNullOrWhiteSpace(state.LayerId))
				{
					continue;
				}

				int score = GetFusionLayerRoleScore(role, state);
				if (score <= 0)
				{
					continue;
				}

				if (scoresByLayerId.TryGetValue(state.LayerId, out int currentScore) == true)
				{
					scoresByLayerId[state.LayerId] = currentScore + score;
				}
				else
				{
					scoresByLayerId[state.LayerId] = score;
				}
			}

			string bestLayerId = string.Empty;
			int bestScore = int.MinValue;
			int bestPriority = int.MaxValue;
			int bestOrder = int.MaxValue;

			foreach (KeyValuePair<string, int> pair in scoresByLayerId)
			{
				if (pair.Value <= 0)
				{
					continue;
				}

				if (TryGetFusionLayerSortKey(pair.Key, out int priority, out int order) == false)
				{
					priority = int.MaxValue;
					order = int.MaxValue;
				}

				if (pair.Value > bestScore ||
					(pair.Value == bestScore && priority < bestPriority) ||
					(pair.Value == bestScore && priority == bestPriority && order < bestOrder))
				{
					bestLayerId = pair.Key;
					bestScore = pair.Value;
					bestPriority = priority;
					bestOrder = order;
				}
			}

			return bestLayerId;
		}

		private int GetFusionLayerRoleScore(FusionLayerRole role, FusionAnimatorStateDefinition state)
		{
			if (state == null)
			{
				return 0;
			}

			string canonicalName = GetCanonicalFusionStateName(state.Name);
			string fullName = state.Name ?? string.Empty;

			switch (role)
			{
				case FusionLayerRole.Base:
				{
					if (string.Equals(canonicalName, "Idle", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Move", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Turn", StringComparison.OrdinalIgnoreCase) == true)
					{
						return 2;
					}

					if (canonicalName.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
						canonicalName.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return 2;
					}

					return 0;
				}
				case FusionLayerRole.LowerBody:
				{
					if (string.Equals(canonicalName, "Turn", StringComparison.OrdinalIgnoreCase) == true)
					{
						return 6;
					}

					if (canonicalName.IndexOf("Walk", StringComparison.OrdinalIgnoreCase) >= 0 ||
						canonicalName.IndexOf("Run", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return 4;
					}

					if (fullName.StartsWith("Move/", StringComparison.OrdinalIgnoreCase) == true)
					{
						return 4;
					}

					return 0;
				}
				case FusionLayerRole.UpperBody:
				{
					if (fullName.StartsWith("Grenade/", StringComparison.OrdinalIgnoreCase) == true)
					{
						return 8;
					}

					if (string.Equals(canonicalName, "Equip", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Unequip", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Reload", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Aim", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Shoot", StringComparison.OrdinalIgnoreCase) == true)
					{
						return 5;
					}

					return 0;
				}
				case FusionLayerRole.Shoot:
				{
					if (state.Presentation != null && state.Presentation.Semantic == FusionAnimatorStateSemantic.ShootOverlay)
					{
						return 10;
					}

					if (string.Equals(canonicalName, "Shoot", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "ShootState", StringComparison.OrdinalIgnoreCase) == true)
					{
						return 7;
					}

					return 0;
				}
				case FusionLayerRole.Look:
				{
					if (state.Presentation != null && state.Presentation.Semantic == FusionAnimatorStateSemantic.LookPose)
					{
						return 10;
					}

					if (string.Equals(canonicalName, "Look", StringComparison.OrdinalIgnoreCase) == true ||
						fullName.IndexOf("Look", StringComparison.OrdinalIgnoreCase) >= 0)
					{
						return 6;
					}

					return 0;
				}
				case FusionLayerRole.Top:
				case FusionLayerRole.FullBody:
				{
					if (string.Equals(canonicalName, "Death", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Dead", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Jetpack", StringComparison.OrdinalIgnoreCase) == true)
					{
						return 8;
					}

					if (string.Equals(canonicalName, "Jump", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Fall", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Land", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Start_Jump", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "Loop_Jump", StringComparison.OrdinalIgnoreCase) == true ||
						string.Equals(canonicalName, "End_Jump", StringComparison.OrdinalIgnoreCase) == true)
					{
						return role == FusionLayerRole.FullBody ? 5 : 2;
					}

					return 0;
				}
				default:
				{
					return 0;
				}
			}
		}

		private bool TryGetFusionLayerSortKey(string layerId, out int priority, out int order)
		{
			priority = int.MaxValue;
			order = int.MaxValue;

			if (_fusionAnimatorGraph == null || _fusionAnimatorGraph.Layers == null || string.IsNullOrWhiteSpace(layerId))
			{
				return false;
			}

			for (int i = 0; i < _fusionAnimatorGraph.Layers.Count; ++i)
			{
				FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
				if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
				{
					continue;
				}

				if (string.Equals(layer.Id, layerId, StringComparison.Ordinal) == false)
				{
					continue;
				}

				priority = layer.Priority;
				order = i;
				return true;
			}

			return false;
		}

		private void SetFusionLayerRole(FusionLayerRole role, string layerId, params string[] aliases)
		{
			if (string.IsNullOrWhiteSpace(layerId))
			{
				return;
			}

			_fusionLayerIdsByRole[role] = layerId;

			if (aliases == null)
			{
				return;
			}

			for (int i = 0; i < aliases.Length; ++i)
			{
				AddFusionLayerAlias(aliases[i], layerId);
			}
		}

		private void AddFusionLayerAlias(string alias, string layerId)
		{
			if (string.IsNullOrWhiteSpace(alias) || string.IsNullOrWhiteSpace(layerId))
			{
				return;
			}

			string normalizedAlias = NormalizeFusionParameterToken(alias);
			if (string.IsNullOrWhiteSpace(normalizedAlias))
			{
				return;
			}

			_fusionLayerIdsByAlias[normalizedAlias] = layerId;
		}

		private bool TryResolveFusionLayerId(string layerNameOrAlias, out string layerId)
		{
			layerId = string.Empty;
			if (string.IsNullOrWhiteSpace(layerNameOrAlias))
			{
				return false;
			}

			if (_fusionLayerIdsByName.TryGetValue(layerNameOrAlias, out string exactLayerId) == true &&
				string.IsNullOrWhiteSpace(exactLayerId) == false)
			{
				layerId = exactLayerId;
				return true;
			}

			string normalized = NormalizeFusionParameterToken(layerNameOrAlias);
			if (string.IsNullOrWhiteSpace(normalized) == false &&
				_fusionLayerIdsByAlias.TryGetValue(normalized, out string aliasLayerId) == true &&
				string.IsNullOrWhiteSpace(aliasLayerId) == false)
			{
				layerId = aliasLayerId;
				return true;
			}

			if (TryGetFusionLayerRoleFromToken(normalized, out FusionLayerRole role) == true &&
				_fusionLayerIdsByRole.TryGetValue(role, out string roleLayerId) == true &&
				string.IsNullOrWhiteSpace(roleLayerId) == false)
			{
				layerId = roleLayerId;
				return true;
			}

			if (_fusionAnimatorGraph != null && _fusionAnimatorGraph.Layers != null)
			{
				for (int i = 0; i < _fusionAnimatorGraph.Layers.Count; ++i)
				{
					FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
					if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
					{
						continue;
					}

					if (string.Equals(layer.Id, layerNameOrAlias, StringComparison.Ordinal) == true)
					{
						layerId = layer.Id;
						return true;
					}
				}
			}

			return false;
		}

		private static bool TryGetFusionLayerRoleFromToken(string token, out FusionLayerRole role)
		{
			role = default;
			if (string.IsNullOrWhiteSpace(token))
			{
				return false;
			}

			switch (token)
			{
				case "base":
				{
					role = FusionLayerRole.Base;
					return true;
				}
				case "lower":
				case "lowerbody":
				{
					role = FusionLayerRole.LowerBody;
					return true;
				}
				case "upper":
				case "upperbody":
				{
					role = FusionLayerRole.UpperBody;
					return true;
				}
				case "shoot":
				{
					role = FusionLayerRole.Shoot;
					return true;
				}
				case "full":
				case "fullbody":
				{
					role = FusionLayerRole.FullBody;
					return true;
				}
				case "look":
				{
					role = FusionLayerRole.Look;
					return true;
				}
				case "top":
				{
					role = FusionLayerRole.Top;
					return true;
				}
				default:
				{
					return false;
				}
			}
		}

		private void RebuildFusionRuntimeParameterBindings()
		{
			_fusionBoundRuntimeParameterIds.Clear();

			BindFusionRuntimeParameter("param_weapon_slot", FusionAnimatorParameterType.Int, "param_weapon_slot", "weaponslot", "currentweaponslot");
			BindFusionRuntimeParameter("param_pending_weapon_slot", FusionAnimatorParameterType.Int, "param_pending_weapon_slot", "pendingweaponslot", "pendingweapon");
			BindFusionRuntimeParameter("param_state_weapon", FusionAnimatorParameterType.Int, "state_weapon", "weaponstate");

			BindFusionRuntimeParameter("param_move_x", FusionAnimatorParameterType.Float, "param_move_x", "input_move_x", "movex", "horizontal");
			BindFusionRuntimeParameter("param_move_y", FusionAnimatorParameterType.Float, "param_move_y", "input_move_y", "movey", "vertical");
			BindFusionRuntimeParameter("param_move_vector2", FusionAnimatorParameterType.Vector2, "param_move", "movevector", "movement", "move");
			BindFusionRuntimeParameter("param_input_move_vector2", FusionAnimatorParameterType.Vector2, "input_move", "inputmove");
			BindFusionRuntimeParameter("param_input_look_vector2", FusionAnimatorParameterType.Vector2, "input_look", "inputlook");
			BindFusionRuntimeParameter("param_input_aim", FusionAnimatorParameterType.Bool, "input_aim", "inputaim");
			BindFusionRuntimeParameter("param_input_shoot", FusionAnimatorParameterType.Trigger, "input_shoot", "inputshoot");
			BindFusionRuntimeParameter("param_input_reload", FusionAnimatorParameterType.Trigger, "input_reload", "inputreload");
			BindFusionRuntimeParameter("param_input_jump", FusionAnimatorParameterType.Trigger, "input_jump", "inputjump");
			BindFusionRuntimeParameter("param_input_throw", FusionAnimatorParameterType.Trigger, "input_throw", "inputthrow");
			BindFusionRuntimeParameter("param_look_pitch", FusionAnimatorParameterType.Float, "param_look_pitch", "lookpitch", "pitch");
			BindFusionRuntimeParameter("param_turn_direction", FusionAnimatorParameterType.Float, "param_turn_direction", "turndirection", "turn");

			BindFusionRuntimeParameter("param_is_dead", FusionAnimatorParameterType.Bool, "param_is_dead", "state_isdead", "isdead", "dead");
			BindFusionRuntimeParameter("param_is_jetpack_active", FusionAnimatorParameterType.Bool, "param_is_jetpack_active", "state_jetpack", "isjetpackactive", "jetpackactive");
			BindFusionRuntimeParameter("param_is_grounded", FusionAnimatorParameterType.Bool, "param_is_grounded", "state_isgrounded", "isgrounded", "grounded");
			BindFusionRuntimeParameter("param_has_jumped", FusionAnimatorParameterType.Bool, "param_has_jumped", "hasjumped", "jumped");
			BindFusionRuntimeParameter("param_is_reloading", FusionAnimatorParameterType.Bool, "param_is_reloading", "state_isreloading", "isreloading", "reloading");
			BindFusionRuntimeParameter("param_is_equipping", FusionAnimatorParameterType.Bool, "param_is_equipping", "isequipping", "equipping");
			BindFusionRuntimeParameter("param_is_unequipping", FusionAnimatorParameterType.Bool, "param_is_unequipping", "isunequipping", "unequipping");
			BindFusionRuntimeParameter("param_equip_trigger", FusionAnimatorParameterType.Trigger, "param_equip_trigger", "equiptrigger", "triggerequip");
			BindFusionRuntimeParameter("param_unequip_trigger", FusionAnimatorParameterType.Trigger, "param_unequip_trigger", "unequiptrigger", "triggerunequip");
			BindFusionRuntimeParameter("param_is_throwing", FusionAnimatorParameterType.Bool, "param_is_throwing", "state_isthrowing", "isthrowing", "throwing");
			BindFusionRuntimeParameter("param_state_is_shooting", FusionAnimatorParameterType.Bool, "state_isshooting", "isshooting");
			BindFusionRuntimeParameter("param_state_is_sprinting", FusionAnimatorParameterType.Bool, "state_issprinting", "issprinting");
			BindFusionRuntimeParameter("param_is_turning", FusionAnimatorParameterType.Bool, "param_is_turning", "isturning", "turning");
			BindFusionRuntimeParameter("param_shoot_trigger", FusionAnimatorParameterType.Trigger, "param_shoot_trigger", "shoottrigger", "shoot");
			BindFusionRuntimeParameter("param_throw_start", FusionAnimatorParameterType.Trigger, "param_throw_start", "throwstart");
			BindFusionRuntimeParameter("param_throw_hold", FusionAnimatorParameterType.Bool, "param_throw_hold", "throwhold");
			BindFusionRuntimeParameter("param_grenade_equip", FusionAnimatorParameterType.Bool, "param_grenade_equip", "grenadeequip");
		}

		private void BindFusionRuntimeParameter(string bindingKey, FusionAnimatorParameterType expectedType, params string[] aliases)
		{
			string resolvedId = ResolveFusionParameterId(expectedType, aliases);
			if (string.IsNullOrWhiteSpace(resolvedId))
			{
				return;
			}

			_fusionBoundRuntimeParameterIds[bindingKey] = resolvedId;
		}

		private string ResolveFusionParameterId(FusionAnimatorParameterType expectedType, params string[] aliases)
		{
			if (_fusionParameterDefinitionsById == null || _fusionParameterDefinitionsById.Count == 0)
			{
				return string.Empty;
			}

			if (aliases == null || aliases.Length == 0)
			{
				return string.Empty;
			}

			for (int i = 0; i < aliases.Length; ++i)
			{
				string alias = aliases[i];
				if (string.IsNullOrWhiteSpace(alias))
				{
					continue;
				}

				if (_fusionParameterDefinitionsById.TryGetValue(alias, out FusionAnimatorParameterDefinition exact) == true &&
					exact != null &&
					IsCompatibleFusionParameterType(exact.Type, expectedType))
				{
					return exact.Id;
				}
			}

			var normalizedAliases = new List<string>(aliases.Length);
			for (int i = 0; i < aliases.Length; ++i)
			{
				string normalized = NormalizeFusionParameterToken(aliases[i]);
				if (string.IsNullOrWhiteSpace(normalized) == false)
				{
					normalizedAliases.Add(normalized);
				}
			}

			if (normalizedAliases.Count == 0)
			{
				return string.Empty;
			}

			foreach (KeyValuePair<string, FusionAnimatorParameterDefinition> pair in _fusionParameterDefinitionsById)
			{
				FusionAnimatorParameterDefinition definition = pair.Value;
				if (definition == null || IsCompatibleFusionParameterType(definition.Type, expectedType) == false)
				{
					continue;
				}

				string normalizedId = NormalizeFusionParameterToken(definition.Id);
				string normalizedName = NormalizeFusionParameterToken(definition.Name);
				for (int i = 0; i < normalizedAliases.Count; ++i)
				{
					string alias = normalizedAliases[i];
					if (string.Equals(normalizedId, alias, StringComparison.Ordinal) ||
						string.Equals(normalizedName, alias, StringComparison.Ordinal))
					{
						return definition.Id;
					}
				}
			}

			return string.Empty;
		}

		private static bool IsCompatibleFusionParameterType(FusionAnimatorParameterType actualType, FusionAnimatorParameterType expectedType)
		{
			if (actualType == expectedType)
			{
				return true;
			}

			// Allow trigger bindings to target legacy bool parameters for backwards-compatible graphs.
			if (expectedType == FusionAnimatorParameterType.Trigger && actualType == FusionAnimatorParameterType.Bool)
			{
				return true;
			}

			return false;
		}

		private static string NormalizeFusionParameterToken(string value)
		{
			if (string.IsNullOrWhiteSpace(value))
			{
				return string.Empty;
			}

			char[] chars = value.ToLowerInvariant().ToCharArray();
			int write = 0;
			for (int i = 0; i < chars.Length; ++i)
			{
				char c = chars[i];
				if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
				{
					chars[write++] = c;
				}
			}

			return write > 0 ? new string(chars, 0, write) : string.Empty;
		}

		private void SetFusionRuntimeInt(string bindingKey, int value)
		{
			if (TryGetFusionBoundRuntimeParameterId(bindingKey, out string parameterId) == false)
			{
				return;
			}

			_fusionParameters.SetInt(parameterId, value);
		}

		private void SetFusionRuntimeFloat(string bindingKey, float value)
		{
			if (TryGetFusionBoundRuntimeParameterId(bindingKey, out string parameterId) == false)
			{
				return;
			}

			_fusionParameters.SetFloat(parameterId, value);
		}

		private void SetFusionRuntimeBool(string bindingKey, bool value)
		{
			if (TryGetFusionBoundRuntimeParameterId(bindingKey, out string parameterId) == false)
			{
				return;
			}

			_fusionParameters.SetBool(parameterId, value);
		}

		private void SetFusionRuntimeVector2(string bindingKey, Vector2 value)
		{
			if (TryGetFusionBoundRuntimeParameterId(bindingKey, out string parameterId) == false)
			{
				return;
			}

			_fusionParameters.SetVector2(parameterId, value);
		}

		private bool TryGetFusionBoundRuntimeParameterId(string bindingKey, out string parameterId)
		{
			parameterId = string.Empty;
			if (string.IsNullOrWhiteSpace(bindingKey))
			{
				return false;
			}

			if (_fusionBoundRuntimeParameterIds.TryGetValue(bindingKey, out parameterId) == true &&
				string.IsNullOrWhiteSpace(parameterId) == false)
			{
				return true;
			}

			return false;
		}

		private void ApplyFusionRootMotionPolicy(bool forceRefreshBindings = false)
		{
			if (Animator == null)
			{
				ClearFusionRootMotionBindings();
				return;
			}

			if (UseFusionAnimatorRuntime == false || _fusionAnimatorGraph == null)
			{
				ClearFusionRootMotionBindings();
				return;
			}

			bool applyRootMotion = _fusionAnimatorGraph.ApplyRootMotion;
			if (Animator.applyRootMotion != applyRootMotion)
			{
				Animator.applyRootMotion = applyRootMotion;
			}

			if (applyRootMotion)
			{
				ClearFusionRootMotionBindings();
				return;
			}

			bool needsCapture = forceRefreshBindings || _fusionRootTransformBindings.Count == 0;
			if (needsCapture == false)
			{
				for (int i = 0; i < _fusionRootTransformBindings.Count; ++i)
				{
					FusionRootTransformBinding binding = _fusionRootTransformBindings[i];
					if (binding == null || binding.Transform == null)
					{
						needsCapture = true;
						break;
					}
				}
			}

			if (needsCapture)
			{
				CaptureFusionRootMotionBindings();
			}

			RestoreFusionRootMotionBindings();
		}

		private void CaptureFusionRootMotionBindings()
		{
			_fusionRootTransformBindings.Clear();
			if (Animator == null)
			{
				return;
			}

			HashSet<Transform> trackedTransforms = new HashSet<Transform>();

			Transform hips = Animator.GetBoneTransform(HumanBodyBones.Hips);
			TryAddFusionRootMotionBinding(hips, trackedTransforms);
			Transform cursor = hips != null ? hips.parent : null;
			while (cursor != null && cursor != Animator.transform)
			{
				TryAddFusionRootMotionBinding(cursor, trackedTransforms);
				cursor = cursor.parent;
			}

			TryAddFusionRootMotionBinding(ResolveFusionSkeletonRoot(Animator), trackedTransforms);
		}

		private void ClearFusionRootMotionBindings()
		{
			_fusionRootTransformBindings.Clear();
		}

		private void RestoreFusionRootMotionBindings()
		{
			for (int i = 0; i < _fusionRootTransformBindings.Count; ++i)
			{
				FusionRootTransformBinding binding = _fusionRootTransformBindings[i];
				if (binding == null || binding.Transform == null)
				{
					continue;
				}

				binding.Transform.localPosition = binding.LocalPosition;
			}
		}

		private void TryAddFusionRootMotionBinding(Transform transform, ISet<Transform> trackedTransforms)
		{
			if (transform == null || trackedTransforms == null || trackedTransforms.Add(transform) == false)
			{
				return;
			}

			// Never pin the networked character root when suppressing clip root motion.
			if (transform == this.transform || transform == Animator.transform)
			{
				return;
			}

			_fusionRootTransformBindings.Add(new FusionRootTransformBinding
			{
				Transform = transform,
				LocalPosition = transform.localPosition,
				LocalRotation = transform.localRotation,
				LocalScale = transform.localScale,
			});
		}

		private static Transform ResolveFusionSkeletonRoot(Animator animator)
		{
			if (animator == null)
			{
				return null;
			}

			Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
			if (hips == null)
			{
				return null;
			}

			Transform skeletonRoot = hips;
			while (skeletonRoot.parent != null && skeletonRoot.parent != animator.transform)
			{
				skeletonRoot = skeletonRoot.parent;
			}

			return skeletonRoot;
		}

		private void ClearFusionPlayableGraphBindings()
		{
			if (Mixer.IsValid() == true)
			{
				foreach (KeyValuePair<string, FusionPlayableLayerBinding> pair in _fusionPlayableLayersById)
				{
					FusionPlayableLayerBinding binding = pair.Value;
					if (binding == null)
					{
						continue;
					}

					if (binding.ControllerPort >= 0 && binding.ControllerPort < Mixer.GetInputCount())
					{
						Mixer.DisconnectInput(binding.ControllerPort);
					}
				}
			}

			_fusionPlayableLayersById.Clear();
			_fusionPlayableStatesById.Clear();
			_fusionPlayableGraphAsset = null;
			_fusionPlayablesInitialized = false;
		}

		private void EnsureFusionPlayableGraphBindings()
		{
			if (UseFusionAnimatorRuntime == false || _fusionAnimatorGraph == null)
			{
				ClearFusionPlayableGraphBindings();
				return;
			}

			if (_fusionRuntimeGraphInstance == null || Graph.IsValid() == false || Mixer.IsValid() == false)
			{
				return;
			}

			if (_fusionPlayablesInitialized == true &&
				ReferenceEquals(_fusionPlayableGraphAsset, _fusionAnimatorGraph) == true)
			{
				return;
			}

			BuildFusionPlayableGraphBindings();
		}

		private void BuildFusionPlayableGraphBindings()
		{
			ClearFusionPlayableGraphBindings();

			if (_fusionAnimatorGraph == null || Graph.IsValid() == false || Mixer.IsValid() == false)
			{
				return;
			}

			var orderedLayers = new List<FusionAnimatorLayerDefinition>();
			var layerOrderById = new Dictionary<string, int>(StringComparer.Ordinal);
			if (_fusionAnimatorGraph.Layers != null)
			{
				for (int i = 0; i < _fusionAnimatorGraph.Layers.Count; ++i)
				{
					FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
					if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
					{
						continue;
					}

					orderedLayers.Add(layer);
					if (layerOrderById.ContainsKey(layer.Id) == false)
					{
						layerOrderById.Add(layer.Id, i);
					}
				}
			}

			orderedLayers.Sort((a, b) =>
			{
				int priorityCompare = a.Priority.CompareTo(b.Priority);
				if (priorityCompare != 0)
				{
					return priorityCompare;
				}

				int aOrder = (a != null && string.IsNullOrWhiteSpace(a.Id) == false && layerOrderById.TryGetValue(a.Id, out int aIndex)) ? aIndex : int.MaxValue;
				int bOrder = (b != null && string.IsNullOrWhiteSpace(b.Id) == false && layerOrderById.TryGetValue(b.Id, out int bIndex)) ? bIndex : int.MaxValue;
				return aOrder.CompareTo(bOrder);
			});

			var statesByLayer = new Dictionary<string, List<FusionAnimatorStateDefinition>>(StringComparer.Ordinal);
			if (_fusionAnimatorGraph.States != null)
			{
				for (int i = 0; i < _fusionAnimatorGraph.States.Count; ++i)
				{
					FusionAnimatorStateDefinition state = _fusionAnimatorGraph.States[i];
					if (state == null || string.IsNullOrWhiteSpace(state.Id) || string.IsNullOrWhiteSpace(state.LayerId))
					{
						continue;
					}

					if (statesByLayer.TryGetValue(state.LayerId, out List<FusionAnimatorStateDefinition> layerStates) == false)
					{
						layerStates = new List<FusionAnimatorStateDefinition>(16);
						statesByLayer.Add(state.LayerId, layerStates);
					}

					layerStates.Add(state);
				}
			}

			for (int layerIndex = 0; layerIndex < orderedLayers.Count; ++layerIndex)
			{
				FusionAnimatorLayerDefinition layerDefinition = orderedLayers[layerIndex];
				if (statesByLayer.TryGetValue(layerDefinition.Id, out List<FusionAnimatorStateDefinition> layerStates) == false ||
					layerStates == null ||
					layerStates.Count == 0)
				{
					continue;
				}

				var layerBinding = new FusionPlayableLayerBinding();
				layerBinding.LayerDefinition = layerDefinition;
				layerBinding.Mixer = AnimationMixerPlayable.Create(Graph, 0);
				float initialLayerWeight = IsFusionLayerRuntimeEnabled(layerDefinition) == true && layerDefinition.EnabledByDefault == true ? Mathf.Clamp01(layerDefinition.DefaultWeight) : 0.0f;
				layerBinding.ControllerPort = Mixer.AddInput(layerBinding.Mixer, 0, initialLayerWeight);

				Mixer.SetLayerAdditive((uint)layerBinding.ControllerPort, layerDefinition.BlendMode == FusionAnimatorLayerBlendMode.Additive);
				if (layerDefinition.AvatarMask != null)
				{
					Mixer.SetLayerMaskFromAvatarMask((uint)layerBinding.ControllerPort, layerDefinition.AvatarMask);
				}

				for (int stateIndex = 0; stateIndex < layerStates.Count; ++stateIndex)
				{
					FusionAnimatorStateDefinition stateDefinition = layerStates[stateIndex];
					FusionPlayableStateBinding stateBinding = CreateFusionPlayableStateBinding(stateDefinition, layerDefinition.Id);
					if (stateBinding == null)
					{
						continue;
					}

					stateBinding.LayerPort = layerBinding.Mixer.AddInput(stateBinding.Mixer, 0, 0.0f);
					layerBinding.StateIds.Add(stateDefinition.Id);
					_fusionPlayableStatesById[stateDefinition.Id] = stateBinding;
				}

				_fusionPlayableLayersById[layerDefinition.Id] = layerBinding;
			}

			_fusionPlayableGraphAsset = _fusionAnimatorGraph;
			_fusionPlayablesInitialized = true;
		}

		private FusionPlayableStateBinding CreateFusionPlayableStateBinding(FusionAnimatorStateDefinition stateDefinition, string layerId)
		{
			if (stateDefinition == null || string.IsNullOrWhiteSpace(stateDefinition.Id))
			{
				return null;
			}

			var stateBinding = new FusionPlayableStateBinding();
			stateBinding.StateDefinition = stateDefinition;
			stateBinding.LayerId = layerId;
			stateBinding.Mixer = AnimationMixerPlayable.Create(Graph, 0);

			if (stateDefinition.MotionType == FusionAnimatorMotionType.BlendTree &&
				stateDefinition.BlendTree != null &&
				stateDefinition.BlendTree.Children != null)
			{
				for (int i = 0; i < stateDefinition.BlendTree.Children.Count; ++i)
				{
					FusionAnimatorBlendTreeChild child = stateDefinition.BlendTree.Children[i];
					AnimationClip childClip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, child, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
					if (child == null || childClip == null)
					{
						continue;
					}

					AnimationClipPlayable clipPlayable = AnimationClipPlayable.Create(Graph, childClip);
					clipPlayable.SetApplyFootIK(false);
					clipPlayable.SetApplyPlayableIK(false);
					clipPlayable.SetSpeed(0.0f);
					clipPlayable.SetTime(0.0f);

					stateBinding.Mixer.AddInput(clipPlayable, 0, 0.0f);
					stateBinding.ClipPlayables.Add(clipPlayable);
					stateBinding.BlendTreeChildren.Add(child);
				}
			}
			else if (stateDefinition.Clips != null)
			{
				for (int i = 0; i < stateDefinition.Clips.Count; ++i)
				{
					FusionAnimatorClipSlot clipSlot = stateDefinition.Clips[i];
					AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, clipSlot, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
					if (clipSlot == null || clip == null)
					{
						continue;
					}

					AnimationClipPlayable clipPlayable = AnimationClipPlayable.Create(Graph, clip);
					clipPlayable.SetApplyFootIK(false);
					clipPlayable.SetApplyPlayableIK(false);
					clipPlayable.SetSpeed(0.0f);
					clipPlayable.SetTime(0.0f);

					stateBinding.Mixer.AddInput(clipPlayable, 0, 0.0f);
					stateBinding.ClipPlayables.Add(clipPlayable);
					stateBinding.ClipSlots.Add(clipSlot);
				}
			}

			stateBinding.WeightBuffer = new float[stateBinding.ClipPlayables.Count];
			return stateBinding;
		}

		private bool TryGetFusionPlayableStateBinding(string stateId, out FusionPlayableStateBinding stateBinding)
		{
			if (string.IsNullOrWhiteSpace(stateId) == false &&
				_fusionPlayableStatesById.TryGetValue(stateId, out stateBinding) == true &&
				stateBinding != null)
			{
				return true;
			}

			stateBinding = null;
			return false;
		}

		private bool IsFusionLayerRuntimeEnabled(FusionAnimatorLayerDefinition layerDefinition)
		{
			return layerDefinition != null;
		}

		private void ApplyFusionRuntimeToPlayables()
		{
			if (_fusionRuntimeGraphInstance == null || _fusionPlayableLayersById.Count == 0)
			{
				return;
			}

			foreach (KeyValuePair<string, FusionPlayableLayerBinding> pair in _fusionPlayableLayersById)
			{
				string layerId = pair.Key;
				FusionPlayableLayerBinding layerBinding = pair.Value;
				if (layerBinding == null || layerBinding.Mixer.IsValid() == false || layerBinding.LayerDefinition == null)
				{
					continue;
				}

				bool layerEnabled = IsFusionLayerRuntimeEnabled(layerBinding.LayerDefinition);
				float baseLayerWeight = layerEnabled == true && layerBinding.LayerDefinition.EnabledByDefault == true ? Mathf.Clamp01(layerBinding.LayerDefinition.DefaultWeight) : 0.0f;
				float effectiveLayerWeight = 0.0f;

				for (int i = 0; i < layerBinding.StateIds.Count; ++i)
				{
					string stateId = layerBinding.StateIds[i];
					if (TryGetFusionPlayableStateBinding(stateId, out FusionPlayableStateBinding stateBinding) == false ||
						stateBinding.LayerPort < 0 ||
						stateBinding.LayerPort >= layerBinding.Mixer.GetInputCount())
					{
						continue;
					}

					layerBinding.Mixer.SetInputWeight(stateBinding.LayerPort, 0.0f);
					if (stateBinding.Mixer.IsValid() == true)
					{
						for (int n = 0; n < stateBinding.ClipPlayables.Count; ++n)
						{
							stateBinding.Mixer.SetInputWeight(n, 0.0f);
						}
					}
				}

				if (layerEnabled == false)
				{
					if (layerBinding.ControllerPort >= 0 && layerBinding.ControllerPort < Mixer.GetInputCount())
					{
						Mixer.SetInputWeight(layerBinding.ControllerPort, 0.0f);
					}
					continue;
				}

				FusionAnimatorRuntimeEvaluator evaluator = _fusionRuntimeGraphInstance.GetLayerEvaluator(layerId);
				if (evaluator == null)
				{
					if (layerBinding.ControllerPort >= 0 && layerBinding.ControllerPort < Mixer.GetInputCount())
					{
						Mixer.SetInputWeight(layerBinding.ControllerPort, 0.0f);
					}
					continue;
				}

				float blendAlpha = evaluator.IsBlending ? Mathf.Clamp01(evaluator.BlendAlpha) : 1.0f;
				float fromWeight = evaluator.IsBlending ? 1.0f - blendAlpha : 0.0f;
				float currentWeight = evaluator.IsBlending ? blendAlpha : 1.0f;
				bool hasPoseContribution = false;

				if (fromWeight > 0.0001f &&
					TryGetFusionPlayableStateBinding(evaluator.BlendFromStateId, out FusionPlayableStateBinding fromBinding) == true &&
					string.Equals(fromBinding.LayerId, layerId, StringComparison.Ordinal))
				{
					hasPoseContribution |= ApplyFusionPlayableStateSample(layerBinding, fromBinding, fromWeight, evaluator.BlendFromStateTime);
				}

				if (TryGetFusionPlayableStateBinding(evaluator.CurrentStateId, out FusionPlayableStateBinding currentBinding) == true &&
					string.Equals(currentBinding.LayerId, layerId, StringComparison.Ordinal))
				{
					hasPoseContribution |= ApplyFusionPlayableStateSample(layerBinding, currentBinding, currentWeight, evaluator.CurrentStateTime);
				}

				if (hasPoseContribution == true)
				{
					effectiveLayerWeight = baseLayerWeight;
				}

				if (layerBinding.ControllerPort >= 0 && layerBinding.ControllerPort < Mixer.GetInputCount())
				{
					Mixer.SetInputWeight(layerBinding.ControllerPort, effectiveLayerWeight);
				}
			}
		}

		private bool ApplyFusionPlayableStateSample(FusionPlayableLayerBinding layerBinding, FusionPlayableStateBinding stateBinding, float stateWeight, float stateTimeSeconds)
		{
			if (layerBinding == null || stateBinding == null || stateWeight <= 0.0001f || stateBinding.Mixer.IsValid() == false)
			{
				return false;
			}

			RefreshFusionPlayableBindingClips(stateBinding);

			if (stateBinding.ClipPlayables.Count == 0)
			{
				if (stateBinding.LayerPort >= 0 && stateBinding.LayerPort < layerBinding.Mixer.GetInputCount())
				{
					layerBinding.Mixer.SetInputWeight(stateBinding.LayerPort, 0.0f);
				}
				return false;
			}

			if (stateBinding.LayerPort >= 0 && stateBinding.LayerPort < layerBinding.Mixer.GetInputCount())
			{
				layerBinding.Mixer.SetInputWeight(stateBinding.LayerPort, Mathf.Clamp01(stateWeight));
			}

			bool hasClipContribution = false;
			if (stateBinding.StateDefinition != null &&
				stateBinding.StateDefinition.MotionType == FusionAnimatorMotionType.BlendTree &&
				stateBinding.BlendTreeChildren.Count == stateBinding.ClipPlayables.Count)
			{
				bool isDirectionalPoseTimeState = IsFusionDirectionalPoseTimeBlendTree(stateBinding.StateDefinition);
				bool isLookPoseState = isDirectionalPoseTimeState == false && IsFusionLookPoseState(stateBinding.StateDefinition);
				bool isTurnState = IsFusionTurnState(stateBinding.StateDefinition);
				if (isTurnState == true &&
					stateBinding.StateDefinition.Presentation != null &&
					stateBinding.StateDefinition.Presentation.Semantic == FusionAnimatorStateSemantic.TurnInPlace)
				{
					hasClipContribution = ApplyFusionTurnStatePresentationSamples(stateBinding);
					if (hasClipContribution == false &&
						stateBinding.LayerPort >= 0 &&
						stateBinding.LayerPort < layerBinding.Mixer.GetInputCount())
					{
						layerBinding.Mixer.SetInputWeight(stateBinding.LayerPort, 0.0f);
					}

					return hasClipContribution;
				}

				float lookPitch = isLookPoseState == true ? GetFusionFloatParameterValue("param_look_pitch") : 0.0f;
				float directionalPoseTime01 = isDirectionalPoseTimeState == true
					? ResolveFusionDirectionalPoseTimeNormalized(stateBinding.StateDefinition.BlendTree, stateBinding.BlendTreeChildren)
					: 0.0f;
				float turnSpeedScale = isTurnState == true ? Mathf.Clamp01(Mathf.Abs(GetFusionFloatParameterValue("param_turn_direction"))) : 1.0f;
				int lookPoseNode = 0;
				float lookPose01 = 0.0f;
				if (isLookPoseState == true)
				{
					float lookOffset = stateBinding.StateDefinition.Presentation != null &&
						stateBinding.StateDefinition.Presentation.Semantic == FusionAnimatorStateSemantic.LookPose
						? stateBinding.StateDefinition.Presentation.Offset
						: (stateBinding.StateDefinition.BlendTree != null ? stateBinding.StateDefinition.BlendTree.InputOffsetX : 0.0f);
					float lookPower = stateBinding.StateDefinition.Presentation != null &&
						stateBinding.StateDefinition.Presentation.Semantic == FusionAnimatorStateSemantic.LookPose
						? stateBinding.StateDefinition.Presentation.Power
						: (stateBinding.StateDefinition.BlendTree != null ? stateBinding.StateDefinition.BlendTree.InputPowerX : 1.0f);
					if (lookPower <= 0.0001f)
					{
						lookPower = 1.0f;
					}

					float lookAngle = (lookPitch + 720.0f + lookOffset) % 360.0f;
					if (lookAngle > 180.0f && stateBinding.ClipPlayables.Count > 1)
					{
						lookPoseNode = 1;
						lookAngle = 360.0f - lookAngle;
					}

					lookAngle = Mathf.Clamp(Mathf.Pow(lookAngle, lookPower), 0.0f, 90.0f);
					lookPose01 = stateBinding.ClipPlayables.Count > 1 ? lookAngle / 90.0f : 0.0f;
					if (float.IsNaN(lookPose01))
					{
						lookPose01 = 0.0f;
					}
				}
				if (stateBinding.WeightBuffer == null || stateBinding.WeightBuffer.Length != stateBinding.ClipPlayables.Count)
				{
					stateBinding.WeightBuffer = new float[stateBinding.ClipPlayables.Count];
				}

				Array.Clear(stateBinding.WeightBuffer, 0, stateBinding.WeightBuffer.Length);
				ResolveBlendTreeWeights(stateBinding.StateDefinition.BlendTree, stateBinding.BlendTreeChildren, stateBinding.WeightBuffer);

				float totalWeight = 0.0f;
				for (int i = 0; i < stateBinding.WeightBuffer.Length; ++i)
				{
					stateBinding.WeightBuffer[i] = Mathf.Max(0.0f, stateBinding.WeightBuffer[i]);
					totalWeight += stateBinding.WeightBuffer[i];
				}

				if (totalWeight <= 0.000001f)
				{
					stateBinding.WeightBuffer[0] = 1.0f;
					totalWeight = 1.0f;
				}

				float inverseTotalWeight = 1.0f / totalWeight;
				for (int i = 0; i < stateBinding.ClipPlayables.Count; ++i)
				{
					AnimationClipPlayable clipPlayable = stateBinding.ClipPlayables[i];
					FusionAnimatorBlendTreeChild child = stateBinding.BlendTreeChildren[i];
					float childWeight = stateBinding.WeightBuffer[i] * inverseTotalWeight;
					float playbackSpeed = Mathf.Max(0.01f, child != null ? child.TimeScale : 1.0f);
					if (isTurnState == true)
					{
						playbackSpeed *= Mathf.Max(0.1f, turnSpeedScale);
					}
					float forcedClipTimeSeconds = -1.0f;
					bool isLooping = true;
					if (isLookPoseState == true)
					{
						childWeight = i == lookPoseNode ? 1.0f : 0.0f;
						forcedClipTimeSeconds = lookPose01;
						isLooping = false;
					}
					else if (isDirectionalPoseTimeState == true)
					{
						AnimationClip directionalClip = clipPlayable.GetAnimationClip();
						float clipLength = directionalClip != null ? Mathf.Max(0.0f, directionalClip.length) : 0.0f;
						forcedClipTimeSeconds = clipLength * directionalPoseTime01;
						isLooping = false;
					}

					AnimationClip childClip = clipPlayable.GetAnimationClip();
					hasClipContribution |= ApplyFusionPlayableClipSample(stateBinding, i, clipPlayable, childClip, stateTimeSeconds, playbackSpeed, childWeight, isLooping, forcedClipTimeSeconds);
				}

				if (hasClipContribution == false &&
					stateBinding.LayerPort >= 0 &&
					stateBinding.LayerPort < layerBinding.Mixer.GetInputCount())
				{
					layerBinding.Mixer.SetInputWeight(stateBinding.LayerPort, 0.0f);
				}

				return hasClipContribution;
			}

			if (IsFusionShootOverlayState(stateBinding.StateDefinition) == true && stateBinding.ClipPlayables.Count >= 2)
			{
				hasClipContribution = ApplyFusionShootOverlayStatePresentationSamples(stateBinding, stateTimeSeconds);

				if (hasClipContribution == false &&
					stateBinding.LayerPort >= 0 &&
					stateBinding.LayerPort < layerBinding.Mixer.GetInputCount())
				{
					layerBinding.Mixer.SetInputWeight(stateBinding.LayerPort, 0.0f);
				}

				return hasClipContribution;
			}

			for (int i = 0; i < stateBinding.ClipPlayables.Count; ++i)
			{
				AnimationClipPlayable clipPlayable = stateBinding.ClipPlayables[i];
				FusionAnimatorClipSlot clipSlot = i < stateBinding.ClipSlots.Count ? stateBinding.ClipSlots[i] : null;
				float playbackSpeed = Mathf.Max(0.01f, FusionAnimatorClipBindingUtility.ResolveSpeed(_fusionAnimatorGraph, clipSlot, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter));
				float clipWeight = i == 0 ? 1.0f : 0.0f;
				bool isLooping = FusionAnimatorClipBindingUtility.ResolveLoop(_fusionAnimatorGraph, clipSlot, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
				AnimationClip clip = clipPlayable.GetAnimationClip();
				hasClipContribution |= ApplyFusionPlayableClipSample(stateBinding, i, clipPlayable, clip, stateTimeSeconds, playbackSpeed, clipWeight, isLooping, -1.0f);
			}

			if (hasClipContribution == false &&
				stateBinding.LayerPort >= 0 &&
				stateBinding.LayerPort < layerBinding.Mixer.GetInputCount())
			{
				layerBinding.Mixer.SetInputWeight(stateBinding.LayerPort, 0.0f);
			}

			return hasClipContribution;
		}

		private void RefreshFusionPlayableBindingClips(FusionPlayableStateBinding stateBinding)
		{
			if (stateBinding == null ||
				stateBinding.StateDefinition == null ||
				stateBinding.ClipPlayables == null ||
				stateBinding.ClipPlayables.Count == 0 ||
				stateBinding.Mixer.IsValid() == false ||
				Graph.IsValid() == false)
			{
				return;
			}

			if (stateBinding.StateDefinition.MotionType == FusionAnimatorMotionType.BlendTree)
			{
				int count = Mathf.Min(stateBinding.BlendTreeChildren.Count, stateBinding.ClipPlayables.Count);
				for (int i = 0; i < count; ++i)
				{
					FusionAnimatorBlendTreeChild child = stateBinding.BlendTreeChildren[i];
					AnimationClip desiredClip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, child, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
					ReplaceFusionPlayableClip(stateBinding, i, desiredClip);
				}
			}
			else
			{
				int count = Mathf.Min(stateBinding.ClipSlots.Count, stateBinding.ClipPlayables.Count);
				for (int i = 0; i < count; ++i)
				{
					FusionAnimatorClipSlot slot = stateBinding.ClipSlots[i];
					AnimationClip desiredClip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, slot, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
					ReplaceFusionPlayableClip(stateBinding, i, desiredClip);
				}
			}
		}

		private void ReplaceFusionPlayableClip(FusionPlayableStateBinding stateBinding, int clipIndex, AnimationClip desiredClip)
		{
			if (stateBinding == null ||
				stateBinding.ClipPlayables == null ||
				clipIndex < 0 ||
				clipIndex >= stateBinding.ClipPlayables.Count ||
				desiredClip == null ||
				stateBinding.Mixer.IsValid() == false ||
				Graph.IsValid() == false)
			{
				return;
			}

			AnimationClipPlayable existingPlayable = stateBinding.ClipPlayables[clipIndex];
			AnimationClip existingClip = existingPlayable.IsValid() ? existingPlayable.GetAnimationClip() : null;
			if (ReferenceEquals(existingClip, desiredClip))
			{
				return;
			}

			float preservedWeight = 0.0f;
			if (clipIndex < stateBinding.Mixer.GetInputCount())
			{
				preservedWeight = stateBinding.Mixer.GetInputWeight(clipIndex);
				stateBinding.Mixer.DisconnectInput(clipIndex);
			}

			if (existingPlayable.IsValid())
			{
				Graph.DestroyPlayable(existingPlayable);
			}

			AnimationClipPlayable replacement = AnimationClipPlayable.Create(Graph, desiredClip);
			replacement.SetApplyFootIK(false);
			replacement.SetApplyPlayableIK(false);
			replacement.SetSpeed(0.0f);
			replacement.SetTime(0.0f);

			if (clipIndex < stateBinding.Mixer.GetInputCount())
			{
				Graph.Connect(replacement, 0, stateBinding.Mixer, clipIndex);
				stateBinding.Mixer.SetInputWeight(clipIndex, preservedWeight);
			}
			else
			{
				stateBinding.Mixer.AddInput(replacement, 0, preservedWeight);
			}

			stateBinding.ClipPlayables[clipIndex] = replacement;
		}

		private bool ApplyFusionShootOverlayStatePresentationSamples(FusionPlayableStateBinding stateBinding, float stateTimeSeconds)
		{
			if (stateBinding == null || stateBinding.ClipPlayables == null || stateBinding.ClipPlayables.Count < 2)
			{
				return false;
			}

			ResolveFusionShootOverlayClipIndices(stateBinding.ClipSlots, out int idleIndex, out int shootIndex);

			FusionAnimatorClipSlot shootSlot = shootIndex >= 0 && shootIndex < stateBinding.ClipSlots.Count ? stateBinding.ClipSlots[shootIndex] : null;
			AnimationClip shootClip = shootIndex >= 0 && shootIndex < stateBinding.ClipPlayables.Count
				? stateBinding.ClipPlayables[shootIndex].GetAnimationClip()
				: null;
			float shootClipLength = shootClip != null ? Mathf.Max(0.0f, shootClip.length) : 0.0f;
			float shootSpeed = Mathf.Max(0.0001f, FusionAnimatorClipBindingUtility.ResolveSpeed(_fusionAnimatorGraph, shootSlot, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter));
			bool shootLoop = FusionAnimatorClipBindingUtility.ResolveLoop(_fusionAnimatorGraph, shootSlot, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);

			float shootAnimationTime01 = 0.0f;
			if (shootClipLength > 0.0001f)
			{
				shootAnimationTime01 = Mathf.Max(0.0f, stateTimeSeconds) * shootSpeed / shootClipLength;
				shootAnimationTime01 = shootLoop == true ? Mathf.Repeat(shootAnimationTime01, 1.0f) : Mathf.Clamp01(shootAnimationTime01);
			}

			float overlayWeight = ResolveFusionOverlayWeight(stateBinding.StateDefinition, 1.0f);
			bool hasContribution = false;
			for (int i = 0; i < stateBinding.ClipPlayables.Count; ++i)
			{
				AnimationClipPlayable clipPlayable = stateBinding.ClipPlayables[i];
				AnimationClip clip = clipPlayable.GetAnimationClip();

				float clipWeight = 0.0f;
				float explicitTime = 0.0f;
				bool isLooping = false;
				if (i == idleIndex)
				{
					clipWeight = 1.0f - overlayWeight;
					explicitTime = shootAnimationTime01;
				}
				else if (i == shootIndex)
				{
					clipWeight = overlayWeight;
					explicitTime = shootClipLength > 0.0001f ? shootAnimationTime01 * shootClipLength : 0.0f;
					isLooping = shootLoop;
				}

				hasContribution |= ApplyFusionPlayableClipSample(
					stateBinding,
					i,
					clipPlayable,
					clip,
					stateTimeSeconds: 0.0f,
					playbackSpeed: 1.0f,
					clipWeight: clipWeight,
					isLooping: isLooping,
					explicitClipTimeSeconds: explicitTime);
			}

			return hasContribution;
		}

		private bool ApplyFusionTurnStatePresentationSamples(FusionPlayableStateBinding stateBinding)
		{
			if (stateBinding == null || stateBinding.ClipPlayables == null || stateBinding.BlendTreeChildren == null)
			{
				return false;
			}

			if (ResolveFusionTurnBlendTreeChildIndices(stateBinding.BlendTreeChildren, out int leftIndex, out int idleIndex, out int rightIndex) == false)
			{
				return false;
			}

			float animationPower = ResolveFusionOverlayWeight(stateBinding.StateDefinition, 1.0f);
			float animationTime01 = Mathf.Repeat(_fusionTurnAnimationTime, 1.0f);
			bool turningLeft = _fusionTurnRemainingTime <= 0.0f;
			int turnIndex = turningLeft == true ? leftIndex : rightIndex;

			bool hasContribution = false;
			for (int i = 0; i < stateBinding.ClipPlayables.Count; ++i)
			{
				AnimationClipPlayable clipPlayable = stateBinding.ClipPlayables[i];
				FusionAnimatorBlendTreeChild child = i < stateBinding.BlendTreeChildren.Count ? stateBinding.BlendTreeChildren[i] : null;

				float clipWeight = 0.0f;
				if (i == idleIndex)
				{
					clipWeight = 1.0f - animationPower;
				}
				else if (i == turnIndex)
				{
					clipWeight = animationPower;
				}

				AnimationClip childClip = clipPlayable.GetAnimationClip();
				float clipLength = childClip != null ? Mathf.Max(0.0f, childClip.length) : 0.0f;
				float explicitTime = clipLength > 0.0001f ? clipLength * animationTime01 : 0.0f;
				hasContribution |= ApplyFusionPlayableClipSample(
					stateBinding,
					i,
					clipPlayable,
					childClip,
					stateTimeSeconds: 0.0f,
					playbackSpeed: 1.0f,
					clipWeight: clipWeight,
					isLooping: true,
					explicitClipTimeSeconds: explicitTime);
			}

			return hasContribution;
		}

		private static bool ResolveFusionTurnBlendTreeChildIndices(
			List<FusionAnimatorBlendTreeChild> children,
			out int leftIndex,
			out int idleIndex,
			out int rightIndex)
		{
			leftIndex = -1;
			idleIndex = -1;
			rightIndex = -1;

			if (children == null || children.Count == 0)
			{
				return false;
			}

			float leftDistance = float.MaxValue;
			float idleDistance = float.MaxValue;
			float rightDistance = float.MaxValue;

			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				if (child == null)
				{
					continue;
				}

				float x = Mathf.Abs(child.Position.x) > 0.0001f || Mathf.Abs(child.Position.y) > 0.0001f
					? child.Position.x
					: child.Threshold;

				float toLeft = Mathf.Abs(x - (-1.0f));
				if (toLeft < leftDistance)
				{
					leftDistance = toLeft;
					leftIndex = i;
				}

				float toIdle = Mathf.Abs(x);
				if (toIdle < idleDistance)
				{
					idleDistance = toIdle;
					idleIndex = i;
				}

				float toRight = Mathf.Abs(x - 1.0f);
				if (toRight < rightDistance)
				{
					rightDistance = toRight;
					rightIndex = i;
				}
			}

			if (leftIndex < 0)
			{
				leftIndex = 0;
			}
			if (idleIndex < 0)
			{
				idleIndex = leftIndex;
			}
			if (rightIndex < 0)
			{
				rightIndex = leftIndex;
			}

			return true;
		}

		private static void ResolveFusionShootOverlayClipIndices(
			List<FusionAnimatorClipSlot> clipSlots,
			out int idleIndex,
			out int shootIndex)
		{
			idleIndex = 0;
			shootIndex = 1;

			if (clipSlots == null || clipSlots.Count == 0)
			{
				return;
			}

			for (int i = 0; i < clipSlots.Count; ++i)
			{
				FusionAnimatorClipSlot clipSlot = clipSlots[i];
				if (clipSlot == null || string.IsNullOrWhiteSpace(clipSlot.Slot))
				{
					continue;
				}

				if (string.Equals(clipSlot.Slot, "Idle", StringComparison.OrdinalIgnoreCase))
				{
					idleIndex = i;
				}
				else if (string.Equals(clipSlot.Slot, "Shoot", StringComparison.OrdinalIgnoreCase))
				{
					shootIndex = i;
				}
			}

			if (shootIndex < 0 || shootIndex >= clipSlots.Count)
			{
				shootIndex = Mathf.Clamp(1, 0, Mathf.Max(0, clipSlots.Count - 1));
			}
			if (idleIndex < 0 || idleIndex >= clipSlots.Count)
			{
				idleIndex = 0;
			}
		}

		private static float ResolveFusionOverlayWeight(FusionAnimatorStateDefinition stateDefinition, float fallback)
		{
			if (stateDefinition == null || stateDefinition.Presentation == null)
			{
				return Mathf.Clamp01(fallback);
			}

			return Mathf.Clamp01(stateDefinition.Presentation.OverlayWeight);
		}

		private static bool ApplyFusionPlayableClipSample(
			FusionPlayableStateBinding stateBinding,
			int clipIndex,
			AnimationClipPlayable clipPlayable,
			AnimationClip clip,
			float stateTimeSeconds,
			float playbackSpeed,
			float clipWeight,
			bool isLooping,
			float explicitClipTimeSeconds)
		{
			if (stateBinding == null || stateBinding.Mixer.IsValid() == false || clipPlayable.IsValid() == false)
			{
				return false;
			}

			float clipLength = clip != null ? Mathf.Max(0.0f, clip.length) : 0.0f;
			float clipTime;
			if (explicitClipTimeSeconds >= 0.0f)
			{
				clipTime = explicitClipTimeSeconds;
			}
			else
			{
				clipTime = Mathf.Max(0.0f, stateTimeSeconds) * Mathf.Max(0.01f, playbackSpeed);
			}
			if (clipLength > 0.0001f)
			{
				clipTime = isLooping ? Mathf.Repeat(clipTime, clipLength) : Mathf.Clamp(clipTime, 0.0f, clipLength);
			}
			else
			{
				clipTime = 0.0f;
			}

			clipPlayable.SetSpeed(0.0f);
			clipPlayable.SetTime(clipTime);
			float safeWeight = clip != null ? Mathf.Clamp01(clipWeight) : 0.0f;
			if (clipIndex >= 0 && clipIndex < stateBinding.Mixer.GetInputCount())
			{
				stateBinding.Mixer.SetInputWeight(clipIndex, safeWeight);
			}

			return safeWeight > 0.0001f;
		}

		private static bool IsFusionLookPoseState(FusionAnimatorStateDefinition stateDefinition)
		{
			if (stateDefinition == null || stateDefinition.MotionType != FusionAnimatorMotionType.BlendTree)
			{
				return false;
			}

			if (stateDefinition.Presentation != null &&
				stateDefinition.Presentation.Semantic == FusionAnimatorStateSemantic.LookPose)
			{
				return true;
			}

			string canonicalName = GetCanonicalFusionStateName(stateDefinition.Name);
			if (string.Equals(canonicalName, "Look", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(canonicalName, "LookState", StringComparison.OrdinalIgnoreCase) == true)
			{
				return true;
			}

			return stateDefinition.BlendTree != null &&
				string.Equals(stateDefinition.BlendTree.ParameterXId, "param_look_pitch", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsFusionTurnState(FusionAnimatorStateDefinition stateDefinition)
		{
			if (stateDefinition == null || stateDefinition.MotionType != FusionAnimatorMotionType.BlendTree)
			{
				return false;
			}

			if (stateDefinition.Presentation != null &&
				stateDefinition.Presentation.Semantic == FusionAnimatorStateSemantic.TurnInPlace)
			{
				return true;
			}

			string canonicalName = GetCanonicalFusionStateName(stateDefinition.Name);
			if (string.Equals(canonicalName, "Turn", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(canonicalName, "TurnState", StringComparison.OrdinalIgnoreCase) == true)
			{
				return true;
			}

			return stateDefinition.BlendTree != null &&
				string.Equals(stateDefinition.BlendTree.ParameterXId, "param_turn_direction", StringComparison.OrdinalIgnoreCase);
		}

		private static bool IsFusionShootOverlayState(FusionAnimatorStateDefinition stateDefinition)
		{
			if (stateDefinition == null || stateDefinition.MotionType != FusionAnimatorMotionType.Clip)
			{
				return false;
			}

			if (stateDefinition.Presentation != null &&
				stateDefinition.Presentation.Semantic == FusionAnimatorStateSemantic.ShootOverlay)
			{
				return true;
			}

			string canonicalName = GetCanonicalFusionStateName(stateDefinition.Name);
			return string.Equals(canonicalName, "Shoot", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(canonicalName, "ShootState", StringComparison.OrdinalIgnoreCase) == true;
		}

		private static bool IsFusionDirectionalPoseTimeBlendTree(FusionAnimatorStateDefinition stateDefinition)
		{
			return stateDefinition != null &&
				stateDefinition.MotionType == FusionAnimatorMotionType.BlendTree &&
				stateDefinition.BlendTree != null &&
				stateDefinition.BlendTree.Type == FusionAnimatorBlendTreeType.DirectionalPoseTime2D;
		}

		private void ResolveBlendTreeWeights(
			FusionAnimatorBlendTreeDefinition blendTree,
			List<FusionAnimatorBlendTreeChild> children,
			float[] weights)
		{
			if (blendTree == null || children == null || weights == null || children.Count == 0 || weights.Length < children.Count)
			{
				return;
			}

			switch (blendTree.Type)
			{
				case FusionAnimatorBlendTreeType.OneD:
					ResolveOneDWeights(blendTree, children, weights);
					break;
				case FusionAnimatorBlendTreeType.TwoDSimpleDirectional:
					ResolveTwoDSimpleDirectionalWeights(blendTree, children, weights);
					break;
				case FusionAnimatorBlendTreeType.TwoDFreeformDirectional:
					ResolveTwoDFreeformDirectionalWeights(blendTree, children, weights);
					break;
				case FusionAnimatorBlendTreeType.TwoDFreeformCartesian:
					ResolveTwoDFreeformCartesianWeights(blendTree, children, weights);
					break;
				case FusionAnimatorBlendTreeType.Direct:
					ResolveDirectWeights(blendTree, children, weights);
					break;
				case FusionAnimatorBlendTreeType.DirectionalPoseTime2D:
					ResolveDirectionalPoseTimeWeights(blendTree, children, weights);
					break;
				default:
					weights[0] = 1.0f;
					break;
			}
		}

		private void ResolveOneDWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			float x = GetFusionFloatParameterValue(blendTree.ParameterXId);
			var order = new List<int>(children.Count);
			for (int i = 0; i < children.Count; ++i)
			{
				order.Add(i);
			}

			order.Sort((a, b) =>
			{
				float ax = Mathf.Abs(children[a].Position.x) > 0.0001f || Mathf.Abs(children[a].Position.y) > 0.0001f ? children[a].Position.x : children[a].Threshold;
				float bx = Mathf.Abs(children[b].Position.x) > 0.0001f || Mathf.Abs(children[b].Position.y) > 0.0001f ? children[b].Position.x : children[b].Threshold;
				return ax.CompareTo(bx);
			});

			int firstIndex = order[0];
			int lastIndex = order[order.Count - 1];
			float firstX = Mathf.Abs(children[firstIndex].Position.x) > 0.0001f || Mathf.Abs(children[firstIndex].Position.y) > 0.0001f ? children[firstIndex].Position.x : children[firstIndex].Threshold;
			float lastX = Mathf.Abs(children[lastIndex].Position.x) > 0.0001f || Mathf.Abs(children[lastIndex].Position.y) > 0.0001f ? children[lastIndex].Position.x : children[lastIndex].Threshold;

			if (x <= firstX)
			{
				weights[firstIndex] = 1.0f;
				return;
			}

			if (x >= lastX)
			{
				weights[lastIndex] = 1.0f;
				return;
			}

			for (int i = 0; i < order.Count - 1; ++i)
			{
				int leftIndex = order[i];
				int rightIndex = order[i + 1];
				float leftX = Mathf.Abs(children[leftIndex].Position.x) > 0.0001f || Mathf.Abs(children[leftIndex].Position.y) > 0.0001f ? children[leftIndex].Position.x : children[leftIndex].Threshold;
				float rightX = Mathf.Abs(children[rightIndex].Position.x) > 0.0001f || Mathf.Abs(children[rightIndex].Position.y) > 0.0001f ? children[rightIndex].Position.x : children[rightIndex].Threshold;
				if (x < leftX || x > rightX)
				{
					continue;
				}

				float span = Mathf.Max(0.0001f, rightX - leftX);
				float t = Mathf.Clamp01((x - leftX) / span);
				weights[leftIndex] = 1.0f - t;
				weights[rightIndex] = t;
				return;
			}

			weights[firstIndex] = 1.0f;
		}

		private void ResolveTwoDSimpleDirectionalWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			Vector2 input = GetFusionTwoDBlendTreeInputValue(blendTree);
			const float epsilon = 0.0001f;
			float inputMagnitude = input.magnitude;
			if (inputMagnitude <= epsilon)
			{
				if (TryFindCenterChild(children, out int centerAtRestIndex))
				{
					weights[centerAtRestIndex] = 1.0f;
				}
				else
				{
					float fallbackWeight = 1.0f / Mathf.Max(1, children.Count);
					for (int i = 0; i < children.Count; ++i)
					{
						weights[i] = fallbackWeight;
					}
				}

				return;
			}

			Vector2 inputDirection = input / inputMagnitude;
			if (BuildDirectionalChildren(children, out List<int> directionalIndices, out List<float> directionalAnglesDegrees, out int centerIndex) == false)
			{
				if (centerIndex >= 0)
				{
					weights[centerIndex] = 1.0f;
				}
				else
				{
					weights[0] = 1.0f;
				}

				return;
			}

			ResolveDirectionalAngularWeights(inputDirection, directionalIndices, directionalAnglesDegrees, weights);

			if (centerIndex >= 0)
			{
				float directionalFactor = Mathf.Clamp01(inputMagnitude);
				for (int i = 0; i < directionalIndices.Count; ++i)
				{
					int childIndex = directionalIndices[i];
					weights[childIndex] *= directionalFactor;
				}

				weights[centerIndex] += 1.0f - directionalFactor;
			}
		}

		private void ResolveTwoDFreeformDirectionalWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			Vector2 input = GetFusionTwoDBlendTreeInputValue(blendTree);
			const float epsilon = 0.0001f;
			float inputMagnitude = input.magnitude;
			if (inputMagnitude <= epsilon)
			{
				if (TryFindCenterChild(children, out int centerAtRestIndex))
				{
					weights[centerAtRestIndex] = 1.0f;
				}
				else
				{
					weights[0] = 1.0f;
				}

				return;
			}

			Vector2 inputDirection = input / inputMagnitude;
			if (BuildDirectionalChildren(children, out List<int> directionalIndices, out List<float> directionalAnglesDegrees, out int centerIndex) == false)
			{
				if (centerIndex >= 0)
				{
					weights[centerIndex] = 1.0f;
				}
				else
				{
					weights[0] = 1.0f;
				}

				return;
			}

			List<List<int>> lanes = BuildDirectionalLanes(children, directionalIndices, directionalAnglesDegrees);
			if (lanes.Count == 0)
			{
				if (centerIndex >= 0)
				{
					weights[centerIndex] = 1.0f;
				}
				else
				{
					weights[0] = 1.0f;
				}

				return;
			}

			var laneAngles = new List<float>(lanes.Count);
			for (int lane = 0; lane < lanes.Count; ++lane)
			{
				List<int> laneChildren = lanes[lane];
				if (laneChildren == null || laneChildren.Count == 0)
				{
					laneAngles.Add(0.0f);
					continue;
				}

				Vector2 direction = children[laneChildren[0]].Position.normalized;
				float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
				laneAngles.Add(NormalizeAngle360(angle));
			}

			if (lanes.Count == 1)
			{
				AccumulateLaneRadialWeights(children, lanes[0], inputMagnitude, 1.0f, weights);
			}
			else
			{
				ResolveDirectionalNeighborLanes(inputDirection, laneAngles, out int leftLane, out int rightLane, out float laneT);
				AccumulateLaneRadialWeights(children, lanes[leftLane], inputMagnitude, 1.0f - laneT, weights);
				AccumulateLaneRadialWeights(children, lanes[rightLane], inputMagnitude, laneT, weights);
			}

			if (centerIndex >= 0)
			{
				float directionalFactor = Mathf.Clamp01(inputMagnitude);
				float accumulatedDirectional = 0.0f;
				for (int i = 0; i < directionalIndices.Count; ++i)
				{
					int childIndex = directionalIndices[i];
					weights[childIndex] *= directionalFactor;
					accumulatedDirectional += weights[childIndex];
				}

				if (accumulatedDirectional > epsilon)
				{
					float inverse = 1.0f / accumulatedDirectional;
					for (int i = 0; i < directionalIndices.Count; ++i)
					{
						int childIndex = directionalIndices[i];
						weights[childIndex] *= inverse * directionalFactor;
					}
				}

				weights[centerIndex] += 1.0f - directionalFactor;
			}
		}

		private void ResolveTwoDFreeformCartesianWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			Vector2 input = GetFusionTwoDBlendTreeInputValue(blendTree);
			const float epsilon = 0.0001f;

			int exactIndex = -1;
			float bestDistance = float.MaxValue;
			for (int i = 0; i < children.Count; ++i)
			{
				float distance = Vector2.SqrMagnitude(input - children[i].Position);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					exactIndex = i;
				}
			}

			if (bestDistance <= epsilon * epsilon && exactIndex >= 0)
			{
				weights[exactIndex] = 1.0f;
				return;
			}

			const int nearestCount = 4;
			var nearestIndices = new List<int>(nearestCount);
			var nearestDistances = new List<float>(nearestCount);
			for (int i = 0; i < children.Count; ++i)
			{
				float distanceSquared = Vector2.SqrMagnitude(input - children[i].Position);
				float distance = Mathf.Sqrt(distanceSquared);

				if (nearestIndices.Count < nearestCount)
				{
					nearestIndices.Add(i);
					nearestDistances.Add(distance);
					continue;
				}

				int farthestSlot = 0;
				float farthestDistance = nearestDistances[0];
				for (int slot = 1; slot < nearestDistances.Count; ++slot)
				{
					if (nearestDistances[slot] > farthestDistance)
					{
						farthestDistance = nearestDistances[slot];
						farthestSlot = slot;
					}
				}

				if (distance < farthestDistance)
				{
					nearestIndices[farthestSlot] = i;
					nearestDistances[farthestSlot] = distance;
				}
			}

			float total = 0.0f;
			for (int i = 0; i < nearestIndices.Count; ++i)
			{
				int childIndex = nearestIndices[i];
				float distance = nearestDistances[i];
				float weight = 1.0f / Mathf.Max(epsilon * epsilon, distance * distance);
				weights[childIndex] = weight;
				total += weight;
			}

			if (total <= epsilon)
			{
				weights[exactIndex >= 0 ? exactIndex : 0] = 1.0f;
				return;
			}

			float inverseTotal = 1.0f / total;
			for (int i = 0; i < nearestIndices.Count; ++i)
			{
				int childIndex = nearestIndices[i];
				weights[childIndex] *= inverseTotal;
			}
		}

		private void ResolveDirectionalPoseTimeWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			Vector2 input = GetFusionTwoDBlendTreeInputValue(blendTree);
			const float epsilon = 0.0001f;
			float inputMagnitude = input.magnitude;
			if (inputMagnitude <= epsilon)
			{
				if (TryFindCenterChild(children, out int centerAtRestIndex))
				{
					weights[centerAtRestIndex] = 1.0f;
				}
				else
				{
					weights[0] = 1.0f;
				}

				return;
			}

			Vector2 inputDirection = input / inputMagnitude;
			if (BuildDirectionalChildren(children, out List<int> directionalIndices, out List<float> directionalAnglesDegrees, out int centerIndex) == false)
			{
				if (centerIndex >= 0)
				{
					weights[centerIndex] = 1.0f;
				}
				else
				{
					weights[0] = 1.0f;
				}

				return;
			}

			ResolveDirectionalAngularWeights(inputDirection, directionalIndices, directionalAnglesDegrees, weights);
		}

		private static bool TryFindCenterChild(List<FusionAnimatorBlendTreeChild> children, out int centerIndex)
		{
			centerIndex = -1;
			if (children == null)
			{
				return false;
			}

			const float epsilon = 0.0001f;
			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				if (child == null)
				{
					continue;
				}

				if (child.Position.sqrMagnitude <= epsilon * epsilon)
				{
					centerIndex = i;
					return true;
				}
			}

			return false;
		}

		private static bool BuildDirectionalChildren(
			List<FusionAnimatorBlendTreeChild> children,
			out List<int> directionalIndices,
			out List<float> directionalAnglesDegrees,
			out int centerIndex)
		{
			directionalIndices = new List<int>();
			directionalAnglesDegrees = new List<float>();
			centerIndex = -1;
			if (children == null || children.Count == 0)
			{
				return false;
			}

			const float epsilon = 0.0001f;
			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				if (child == null)
				{
					continue;
				}

				if (child.Position.sqrMagnitude <= epsilon * epsilon)
				{
					if (centerIndex < 0)
					{
						centerIndex = i;
					}
					continue;
				}

				directionalIndices.Add(i);
				directionalAnglesDegrees.Add(NormalizeAngle360(Mathf.Atan2(child.Position.y, child.Position.x) * Mathf.Rad2Deg));
			}

			if (directionalIndices.Count == 0)
			{
				return false;
			}

			for (int i = 0; i < directionalIndices.Count - 1; ++i)
			{
				for (int j = i + 1; j < directionalIndices.Count; ++j)
				{
					if (directionalAnglesDegrees[j] < directionalAnglesDegrees[i])
					{
						float tmpAngle = directionalAnglesDegrees[i];
						directionalAnglesDegrees[i] = directionalAnglesDegrees[j];
						directionalAnglesDegrees[j] = tmpAngle;

						int tmpIndex = directionalIndices[i];
						directionalIndices[i] = directionalIndices[j];
						directionalIndices[j] = tmpIndex;
					}
				}
			}

			return true;
		}

		private static List<List<int>> BuildDirectionalLanes(
			List<FusionAnimatorBlendTreeChild> children,
			List<int> directionalIndices,
			List<float> directionalAnglesDegrees)
		{
			var lanes = new List<List<int>>();
			var laneAngles = new List<float>();
			const float laneToleranceDegrees = 8.0f;

			for (int i = 0; i < directionalIndices.Count; ++i)
			{
				int childIndex = directionalIndices[i];
				float childAngle = directionalAnglesDegrees[i];
				int matchedLane = -1;
				for (int lane = 0; lane < laneAngles.Count; ++lane)
				{
					if (Mathf.Abs(Mathf.DeltaAngle(laneAngles[lane], childAngle)) <= laneToleranceDegrees)
					{
						matchedLane = lane;
						break;
					}
				}

				if (matchedLane < 0)
				{
					matchedLane = lanes.Count;
					lanes.Add(new List<int>());
					laneAngles.Add(childAngle);
				}

				lanes[matchedLane].Add(childIndex);
			}

			for (int lane = 0; lane < lanes.Count; ++lane)
			{
				List<int> laneChildren = lanes[lane];
				for (int i = 0; i < laneChildren.Count - 1; ++i)
				{
					for (int j = i + 1; j < laneChildren.Count; ++j)
					{
						float radiusA = children[laneChildren[i]].Position.magnitude;
						float radiusB = children[laneChildren[j]].Position.magnitude;
						if (radiusB < radiusA)
						{
							int tmp = laneChildren[i];
							laneChildren[i] = laneChildren[j];
							laneChildren[j] = tmp;
						}
					}
				}
			}

			for (int i = 0; i < lanes.Count - 1; ++i)
			{
				for (int j = i + 1; j < lanes.Count; ++j)
				{
					float ai = NormalizeAngle360(Mathf.Atan2(children[lanes[i][0]].Position.y, children[lanes[i][0]].Position.x) * Mathf.Rad2Deg);
					float aj = NormalizeAngle360(Mathf.Atan2(children[lanes[j][0]].Position.y, children[lanes[j][0]].Position.x) * Mathf.Rad2Deg);
					if (aj < ai)
					{
						List<int> tmpLane = lanes[i];
						lanes[i] = lanes[j];
						lanes[j] = tmpLane;
					}
				}
			}

			return lanes;
		}

		private static void ResolveDirectionalAngularWeights(Vector2 inputDirection, List<int> directionalIndices, List<float> directionalAnglesDegrees, float[] weights)
		{
			if (directionalIndices == null || directionalIndices.Count == 0)
			{
				return;
			}

			if (directionalIndices.Count == 1)
			{
				weights[directionalIndices[0]] = 1.0f;
				return;
			}

			ResolveDirectionalNeighborLanes(inputDirection, directionalAnglesDegrees, out int leftSlot, out int rightSlot, out float t);
			weights[directionalIndices[leftSlot]] += 1.0f - t;
			weights[directionalIndices[rightSlot]] += t;
		}

		private static void ResolveDirectionalNeighborLanes(
			Vector2 inputDirection,
			List<float> sortedAnglesDegrees,
			out int leftSlot,
			out int rightSlot,
			out float t)
		{
			float inputAngle = NormalizeAngle360(Mathf.Atan2(inputDirection.y, inputDirection.x) * Mathf.Rad2Deg);
			rightSlot = 0;
			while (rightSlot < sortedAnglesDegrees.Count && sortedAnglesDegrees[rightSlot] < inputAngle)
			{
				++rightSlot;
			}

			if (rightSlot >= sortedAnglesDegrees.Count)
			{
				rightSlot = 0;
			}

			leftSlot = (rightSlot - 1 + sortedAnglesDegrees.Count) % sortedAnglesDegrees.Count;
			float leftAngle = sortedAnglesDegrees[leftSlot];
			float rightAngle = sortedAnglesDegrees[rightSlot];
			float angleForLerp = inputAngle;
			if (rightSlot == 0)
			{
				rightAngle += 360.0f;
			}

			if (angleForLerp < leftAngle)
			{
				angleForLerp += 360.0f;
			}

			float span = Mathf.Max(0.0001f, rightAngle - leftAngle);
			t = Mathf.Clamp01((angleForLerp - leftAngle) / span);
		}

		private static void AccumulateLaneRadialWeights(List<FusionAnimatorBlendTreeChild> children, List<int> laneChildren, float inputMagnitude, float laneWeight, float[] weights)
		{
			if (laneChildren == null || laneChildren.Count == 0 || laneWeight <= 0.000001f)
			{
				return;
			}

			if (laneChildren.Count == 1)
			{
				weights[laneChildren[0]] += laneWeight;
				return;
			}

			int firstChild = laneChildren[0];
			int lastChild = laneChildren[laneChildren.Count - 1];
			float firstRadius = children[firstChild].Position.magnitude;
			float lastRadius = children[lastChild].Position.magnitude;

			if (inputMagnitude <= firstRadius + 0.0001f)
			{
				weights[firstChild] += laneWeight;
				return;
			}

			if (inputMagnitude >= lastRadius - 0.0001f)
			{
				weights[lastChild] += laneWeight;
				return;
			}

			for (int i = 0; i < laneChildren.Count - 1; ++i)
			{
				int leftChild = laneChildren[i];
				int rightChild = laneChildren[i + 1];
				float leftRadius = children[leftChild].Position.magnitude;
				float rightRadius = children[rightChild].Position.magnitude;
				if (inputMagnitude < leftRadius || inputMagnitude > rightRadius)
				{
					continue;
				}

				float span = Mathf.Max(0.0001f, rightRadius - leftRadius);
				float radialT = Mathf.Clamp01((inputMagnitude - leftRadius) / span);
				weights[leftChild] += laneWeight * (1.0f - radialT);
				weights[rightChild] += laneWeight * radialT;
				return;
			}

			weights[firstChild] += laneWeight;
		}

		private static float NormalizeAngle360(float angle)
		{
			angle %= 360.0f;
			if (angle < 0.0f)
			{
				angle += 360.0f;
			}

			return angle;
		}

		private void ResolveDirectWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			float total = 0.0f;
			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				string parameterId = string.IsNullOrWhiteSpace(child.DirectParameterId) ? blendTree.DirectBlendParameterId : child.DirectParameterId;
				float value = Mathf.Max(0.0f, GetFusionFloatParameterValue(parameterId));
				weights[i] = value;
				total += value;
			}

			if (total <= 0.000001f)
			{
				weights[0] = 1.0f;
			}
		}

		private Vector2 GetFusionTwoDBlendTreeInputValue(FusionAnimatorBlendTreeDefinition blendTree)
		{
			if (blendTree == null)
			{
				return Vector2.zero;
			}

			if (TryGetFusionVector2ParameterValue(blendTree.ParameterVector2Id, out Vector2 explicitVector2Input))
			{
				return explicitVector2Input;
			}

			bool hasVectorX = TryGetFusionVector2ParameterValue(blendTree.ParameterXId, out Vector2 vectorXInput);
			bool hasVectorY = TryGetFusionVector2ParameterValue(blendTree.ParameterYId, out Vector2 vectorYInput);

			if (hasVectorX && hasVectorY)
			{
				if (string.Equals(blendTree.ParameterXId, blendTree.ParameterYId, StringComparison.Ordinal))
				{
					return vectorXInput;
				}

				return new Vector2(vectorXInput.x, vectorYInput.y);
			}

			if (hasVectorX)
			{
				return vectorXInput;
			}

			if (hasVectorY)
			{
				return vectorYInput;
			}

			return new Vector2(GetFusionFloatParameterValue(blendTree.ParameterXId), GetFusionFloatParameterValue(blendTree.ParameterYId));
		}

		private float ResolveFusionDirectionalPoseTimeNormalized(
			FusionAnimatorBlendTreeDefinition blendTree,
			List<FusionAnimatorBlendTreeChild> children)
		{
			if (blendTree == null)
			{
				return 0.0f;
			}

			float rawPoseTime;
			if (string.IsNullOrWhiteSpace(blendTree.PoseTimeParameterId))
			{
				rawPoseTime = GetFusionTwoDBlendTreeInputValue(blendTree).magnitude;
				float defaultRange = ResolveDirectionalPoseTimeInputRange(children);
				rawPoseTime /= defaultRange;
			}
			else
			{
				rawPoseTime = GetFusionFloatParameterValue(blendTree.PoseTimeParameterId);
			}

			return EvaluatePoseTime01(rawPoseTime, blendTree.InputOffsetX, blendTree.InputPowerX);
		}

		private static float EvaluatePoseTime01(float rawValue, float offset, float power)
		{
			float normalized = Mathf.Abs(rawValue);
			normalized += offset;
			normalized = Mathf.Max(0.0f, normalized);
			float safePower = power <= 0.0001f ? 1.0f : power;
			normalized = Mathf.Pow(normalized, safePower);
			return Mathf.Clamp01(normalized);
		}

		private static float ResolveDirectionalPoseTimeInputRange(List<FusionAnimatorBlendTreeChild> children)
		{
			if (children == null || children.Count == 0)
			{
				return 1.0f;
			}

			float maxThresholdMagnitude = 0.0f;
			float maxPositionMagnitude = 0.0f;
			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				if (child == null)
				{
					continue;
				}

				maxThresholdMagnitude = Mathf.Max(maxThresholdMagnitude, Mathf.Abs(child.Threshold));
				maxPositionMagnitude = Mathf.Max(maxPositionMagnitude, child.Position.magnitude);
			}

			if (maxThresholdMagnitude > 0.0001f)
			{
				return maxThresholdMagnitude;
			}

			if (maxPositionMagnitude > 0.0001f)
			{
				return maxPositionMagnitude;
			}

			return 1.0f;
		}

		private bool TryGetFusionVector2ParameterValue(string parameterId, out Vector2 value)
		{
			value = Vector2.zero;
			if (string.IsNullOrWhiteSpace(parameterId))
			{
				return false;
			}

			if (FusionAnimatorParameterReferenceUtility.TryParse(parameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false)
			{
				return false;
			}

			if (component != FusionAnimatorParameterComponent.None)
			{
				return false;
			}

			if (_fusionParameterDefinitionsById.TryGetValue(baseParameterId, out FusionAnimatorParameterDefinition definition) == false ||
				definition == null ||
				definition.Type != FusionAnimatorParameterType.Vector2)
			{
				return false;
			}

			return _fusionParameters.TryGetVector2(baseParameterId, out value);
		}

		private float GetFusionFloatParameterValue(string parameterId)
		{
			if (string.IsNullOrWhiteSpace(parameterId))
			{
				return 0.0f;
			}

			if (FusionAnimatorParameterReferenceUtility.TryParse(parameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false)
			{
				return 0.0f;
			}

			if (component != FusionAnimatorParameterComponent.None &&
				(_fusionParameterDefinitionsById.TryGetValue(baseParameterId, out FusionAnimatorParameterDefinition definition) == false ||
				 definition == null ||
				 definition.Type != FusionAnimatorParameterType.Vector2))
			{
				return 0.0f;
			}

			if (_fusionParameters.TryGetFloat(baseParameterId, out float value) == true)
			{
				return value;
			}

			if (_fusionParameters.TryGetInt(baseParameterId, out int intValue) == true)
			{
				return intValue;
			}

			if (_fusionParameters.TryGetBool(baseParameterId, out bool boolValue) == true)
			{
				return boolValue ? 1.0f : 0.0f;
			}

			if (_fusionParameters.TryGetVector2(baseParameterId, out Vector2 vectorValue) == true)
			{
				switch (component)
				{
					case FusionAnimatorParameterComponent.X:
						return vectorValue.x;
					case FusionAnimatorParameterComponent.Y:
						return vectorValue.y;
					default:
						return vectorValue.magnitude;
				}
			}

			return 0.0f;
		}

		private bool EvaluateFusionBindingCondition(FusionAnimatorConditionDefinition condition)
		{
			if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
			{
				return false;
			}

			if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false)
			{
				return false;
			}

			if (_fusionParameterDefinitionsById.TryGetValue(baseParameterId, out FusionAnimatorParameterDefinition parameter) == false ||
				parameter == null)
			{
				return false;
			}

			if (component != FusionAnimatorParameterComponent.None && parameter.Type != FusionAnimatorParameterType.Vector2)
			{
				return false;
			}

			return FusionAnimatorRuntimeEvaluator.EvaluateCondition(condition, parameter, _fusionParameters, false, component);
		}

		private int? ResolveFusionBindingClipIndexParameter(string parameterReference)
		{
			if (string.IsNullOrWhiteSpace(parameterReference))
			{
				return null;
			}

			if (FusionAnimatorParameterReferenceUtility.TryParse(parameterReference, out string baseParameterId, out FusionAnimatorParameterComponent component) == false)
			{
				return null;
			}

			if (component != FusionAnimatorParameterComponent.None)
			{
				return null;
			}

			if (_fusionParameterDefinitionsById.TryGetValue(baseParameterId, out FusionAnimatorParameterDefinition parameter) == false ||
				parameter == null ||
				parameter.Type != FusionAnimatorParameterType.Int)
			{
				return null;
			}

			if (_fusionParameters.TryGetInt(baseParameterId, out int value))
			{
				return value;
			}

			return parameter.DefaultInt;
		}

		private void ResetFusionRuntimeRequests()
		{
			_fusionIsDead = false;
			_fusionReloadPending = false;
			_fusionUnequipPending = false;
			_fusionEquipPending = false;
			_fusionGrenadeEquipPending = false;
			_fusionThrowStartPending = false;
			_fusionThrowHold = false;
			_fusionThrowStartTimer = 0.0f;
			_fusionShootPending = false;
			_fusionShootTimer = 0.0f;
			_fusionTurnDirection = 0.0f;
			_fusionTurnRemainingTime = 0.0f;
			_fusionTurnAnimationTime = 0.0f;
			ResetFusionUpperBodySideEffectFlags();
			_fusionJetpackSwitchQueued = false;
			_fusionJetpackDisarmApplied = false;
			_fusionJetpackResumeWeaponSlot = 0;
			_fusionLastArmedWeaponSlot = 0;
			_fusionWeaponCycleTargetSlot = 0;
			_fusionWeaponCycleActive = false;
		}

		private void ResetFusionUpperBodySideEffectFlags()
		{
			_fusionUnequipDisarmApplied = false;
			_fusionEquipArmApplied = false;
			_fusionGrenadeEquipArmApplied = false;
			_fusionGrenadeArmProjectileApplied = false;
			_fusionGrenadeThrowFireApplied = false;
			_fusionUpperBodyStateId = string.Empty;
		}

		private bool CanJumpFusion()
		{
			if (_fusionIsDead == true)
				return false;
			if (IsFusionJetpackStateActive() == true)
				return false;
			if (IsFusionJumpingStateActive() == true)
				return false;

			return true;
		}

		private bool CanSwitchWeaponsFusion(bool force)
		{
			if (_fusionIsDead == true)
				return false;
			if (IsFusionJetpackStateActive() == true)
				return false;
			if (CanSwitchWeaponsFromGrenadeState() == false)
				return false;

			if (force == true)
				return true;

			if (_fusionWeaponCycleActive == true)
			{
				return false;
			}

			string currentUpperBodyState = GetFusionCurrentStateCanonical("UpperBody");
			if (string.Equals(currentUpperBodyState, "Equip", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(currentUpperBodyState, "Unequip", StringComparison.OrdinalIgnoreCase) == true)
			{
				return false;
			}

			return _fusionEquipPending == false && _fusionUnequipPending == false;
		}

		private void SetDeadFusion(bool isDead)
		{
			if (HasStateAuthority == false)
			{
				return;
			}

			_fusionIsDead = isDead;

			_fusionReloadPending = false;
			_fusionUnequipPending = false;
			_fusionEquipPending = false;
			_fusionGrenadeEquipPending = false;
			_fusionThrowStartPending = false;
			_fusionThrowHold = false;
			_fusionThrowStartTimer = 0.0f;
			_fusionShootPending = false;
			_fusionShootTimer = 0.0f;
			_fusionTurnDirection = 0.0f;
			_fusionTurnRemainingTime = 0.0f;
			_fusionTurnAnimationTime = 0.0f;
			_fusionGrenadeEquipPending = false;
			_fusionJetpackSwitchQueued = false;
			_fusionJetpackDisarmApplied = false;
			_fusionJetpackResumeWeaponSlot = 0;
			_fusionLastArmedWeaponSlot = 0;
			_fusionWeaponCycleTargetSlot = 0;
			_fusionWeaponCycleActive = false;
			ResetFusionUpperBodySideEffectFlags();

			if (isDead == true)
			{
				if (_kcc != null && _kcc.Data.IsGrounded == true)
				{
					_kcc.SetColliderLayer(LayerMask.NameToLayer("Ignore Raycast"));
					_kcc.SetCollisionLayerMask(_kcc.Settings.CollisionLayerMask & ~(1 << LayerMask.NameToLayer("AgentKCC")));
				}
			}
			else
			{
				if (_kcc != null)
				{
					_kcc.SetShape(EKCCShape.Capsule);
				}
			}
		}

		private bool StartFireFusion()
		{
			if (CanProcessFusionGameplayRequests() == false)
				return false;

			if (_fusionIsDead == true)
				return false;
			if (IsFusionUpperBodyBlockingFire() == true)
				return false;

			_fusionShootPending = true;
			_fusionShootTimer = Mathf.Max(_fusionShootTimer, SHOOT_TRIGGER_DURATION);
			return true;
		}

		private bool IsFusionUpperBodyBlockingFire()
		{
			if (_fusionReloadPending == true ||
				_fusionUnequipPending == true ||
				_fusionEquipPending == true ||
				_fusionGrenadeEquipPending == true ||
				_fusionThrowStartPending == true ||
				_fusionThrowHold == true)
			{
				return true;
			}

			if (TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition upperBodyState, out _) == false ||
				upperBodyState == null ||
				string.IsNullOrWhiteSpace(upperBodyState.Name) == true)
			{
				return false;
			}

			if (upperBodyState.Name.StartsWith("Grenade/", StringComparison.OrdinalIgnoreCase) == true)
			{
				return true;
			}

			string canonicalState = GetCanonicalFusionStateName(upperBodyState.Name);
			if (string.Equals(canonicalState, "Idle", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(canonicalState, "Aim", StringComparison.OrdinalIgnoreCase) == true ||
				canonicalState.IndexOf("Aim", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return false;
			}

			return true;
		}

		private void ProcessThrowFusion(bool start, bool hold)
		{
			if (CanProcessFusionGameplayRequests() == false)
			{
				return;
			}

			if (_fusionIsDead == true)
				return;
			if (IsFusionJetpackStateActive() == true)
				return;

			bool requestThrow = start == true || hold == true;
			bool canFire = _weapons == null || _weapons.CanFireWeapon(start) == true;
			bool hasGrenadeState = TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition upperState, out float upperStateTime) == true &&
				upperState != null &&
				string.IsNullOrWhiteSpace(upperState.Name) == false &&
				upperState.Name.StartsWith("Grenade/", StringComparison.OrdinalIgnoreCase) == true;

			bool isGrenadeThrow = hasGrenadeState && upperState.Name.StartsWith("Grenade/Throw", StringComparison.OrdinalIgnoreCase);
			bool isGrenadeEquip = hasGrenadeState && upperState.Name.StartsWith("Grenade/Equip", StringComparison.OrdinalIgnoreCase);
			bool isGrenadeReload = hasGrenadeState && upperState.Name.StartsWith("Grenade/Reload", StringComparison.OrdinalIgnoreCase);
			bool isGrenadeArm = hasGrenadeState && upperState.Name.StartsWith("Grenade/Arm", StringComparison.OrdinalIgnoreCase);

			if (isGrenadeThrow == true && upperStateTime < 0.95f)
			{
				return;
			}

			if (isGrenadeEquip == true && upperStateTime < 0.8f)
			{
				return;
			}

			if (isGrenadeReload == true && upperStateTime < 0.8f)
			{
				return;
			}

			if (isGrenadeArm == true && hold == false && canFire == false)
			{
				// Match GrenadeState.ProcessThrow(): stay armed until weapon can throw.
				_fusionThrowHold = true;
				return;
			}

			if (requestThrow == true && canFire == false)
			{
				_fusionThrowHold = false;
				_fusionThrowStartPending = false;
				_fusionThrowStartTimer = 0.0f;
				return;
			}

			if (hasGrenadeState == false && requestThrow == true)
			{
				_fusionGrenadeEquipPending = true;
				_fusionReloadPending = false;
				_fusionUnequipPending = false;
				_fusionEquipPending = false;
				_fusionShootPending = false;
				_fusionShootTimer = 0.0f;
			}
			else if (hasGrenadeState == true)
			{
				_fusionGrenadeEquipPending = false;
			}

			_fusionThrowHold = hold;
			if (start == true)
			{
				_fusionThrowStartPending = true;
				_fusionThrowStartTimer = Mathf.Max(_fusionThrowStartTimer, UPPER_BODY_THROW_START_TIME);
			}
			else if (hold == false)
			{
				_fusionThrowStartPending = false;
				_fusionThrowStartTimer = 0.0f;
			}
		}

		private bool StartReloadFusion()
		{
			if (CanProcessFusionGameplayRequests() == false)
				return false;

			if (_fusionIsDead == true)
				return false;

			string currentUpperBodyState = GetFusionCurrentStateCanonical("UpperBody");
			if (_fusionReloadPending == true ||
				string.Equals(currentUpperBodyState, "Reload", StringComparison.OrdinalIgnoreCase) == true)
				return true;

			if (TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition upperBodyStateDefinition, out float upperBodyNormalizedTime) == true &&
				upperBodyStateDefinition != null &&
				string.IsNullOrWhiteSpace(upperBodyStateDefinition.Name) == false &&
				upperBodyStateDefinition.Name.StartsWith("Grenade/", StringComparison.OrdinalIgnoreCase) == true)
			{
				if (upperBodyStateDefinition.Name.StartsWith("Grenade/Throw", StringComparison.OrdinalIgnoreCase) == true &&
					upperBodyNormalizedTime < UPPER_BODY_RELOAD_EXIT_TIME)
				{
					return false;
				}
			}
			else if (IsFusionUpperBodyAnyActive() == true)
			{
				return false;
			}

			_fusionReloadPending = true;
			_fusionThrowStartPending = false;
			_fusionThrowHold = false;
			_fusionThrowStartTimer = 0.0f;
			_fusionShootPending = false;
			_fusionShootTimer = 0.0f;
			_fusionGrenadeEquipPending = false;
			return true;
		}

		private void SwitchWeaponsFusion()
		{
			if (CanProcessFusionGameplayRequests() == false)
			{
				return;
			}

			if (_fusionIsDead == true)
				return;
			if (IsFusionJetpackStateActive() == true)
				return;
			if (CanSwitchWeaponsFromGrenadeState() == false)
				return;

			_fusionReloadPending = false;
			_fusionUnequipPending = false;
			_fusionEquipPending = false;
			_fusionGrenadeEquipPending = false;
			_fusionThrowStartPending = false;
			_fusionThrowHold = false;
			_fusionThrowStartTimer = 0.0f;
			_fusionShootPending = false;
			_fusionShootTimer = 0.0f;
			_fusionWeaponCycleTargetSlot = 0;
			_fusionWeaponCycleActive = false;
			ResetFusionUpperBodySideEffectFlags();

			if (IsPendingThrowableWeapon() == true)
			{
				_fusionGrenadeEquipPending = true;
				return;
			}

			if (_weapons == null)
			{
				return;
			}

			int requestedSlot = Mathf.Clamp(_weapons.PendingWeaponSlot, 0, 255);
			int currentSlot = Mathf.Clamp(_weapons.CurrentWeaponSlot, 0, 255);
			if (requestedSlot == currentSlot)
			{
				return;
			}

			_fusionWeaponCycleTargetSlot = (byte)requestedSlot;
			_fusionWeaponCycleActive = true;

			// Hold the outgoing slot while unequip plays; swap to target slot at switch-time.
			_weapons.SetPendingWeapon(currentSlot);
			_fusionUnequipPending = true;
			_fusionEquipPending = false;
		}

		private void TurnFusion(float angle)
		{
			if (CanProcessFusionGameplayRequests() == false)
			{
				return;
			}

			if (Mathf.Abs(angle) <= 0.0001f)
				return;

			if (TryGetFusionTurnPresentationState(out FusionAnimatorStateDefinition turnStateDefinition, out FusionAnimatorStatePresentationDefinition turnPresentation) == true &&
				turnPresentation != null)
			{
				float turnSpeed = Mathf.Max(0.0001f, turnPresentation.TurnSpeed);
				float maxMagnitude = Mathf.Max(0.0001f, turnPresentation.MaxMagnitude);

				if (angle < 0.0f && _fusionTurnRemainingTime <= 0.0f)
				{
					_fusionTurnRemainingTime = Mathf.Clamp(_fusionTurnRemainingTime + angle * turnSpeed, -maxMagnitude, 0.0f);
				}
				else if (angle > 0.0f && _fusionTurnRemainingTime >= 0.0f)
				{
					_fusionTurnRemainingTime = Mathf.Clamp(_fusionTurnRemainingTime + angle * turnSpeed, 0.0f, maxMagnitude);
				}

				_fusionTurnDirection = NormalizeFusionTurnDirection(_fusionTurnRemainingTime, maxMagnitude);
				return;
			}

			if (angle < 0.0f && _fusionTurnDirection <= 0.0f)
			{
				_fusionTurnDirection = Mathf.Clamp(_fusionTurnDirection + angle, -1.0f, 0.0f);
			}
			else if (angle > 0.0f && _fusionTurnDirection >= 0.0f)
			{
				_fusionTurnDirection = Mathf.Clamp(_fusionTurnDirection + angle, 0.0f, 1.0f);
			}
		}

		private void AdvanceFusionTurnDirection()
		{
			if (TryGetFusionTurnPresentationState(out FusionAnimatorStateDefinition turnStateDefinition, out FusionAnimatorStatePresentationDefinition turnPresentation) == true &&
				turnPresentation != null)
			{
				float maxMagnitude = Mathf.Max(0.0001f, turnPresentation.MaxMagnitude);
				float remaining = Mathf.Abs(_fusionTurnRemainingTime);
				if (remaining <= 0.0001f)
				{
					_fusionTurnRemainingTime = 0.0f;
					_fusionTurnDirection = 0.0f;
					return;
				}

				float blendSpeed = Mathf.Max(0.0001f, turnPresentation.BlendSpeed);
				float remainingDeltaTime = DeltaTime * Mathf.Max(0.5f, remaining);
				bool turningLeft = _fusionTurnRemainingTime <= 0.0f;
				if (turningLeft == true)
				{
					_fusionTurnRemainingTime = Mathf.Clamp(_fusionTurnRemainingTime + remainingDeltaTime * blendSpeed, -maxMagnitude, 0.0f);
				}
				else
				{
					_fusionTurnRemainingTime = Mathf.Clamp(_fusionTurnRemainingTime - remainingDeltaTime * blendSpeed, 0.0f, maxMagnitude);
				}

				_fusionTurnDirection = NormalizeFusionTurnDirection(_fusionTurnRemainingTime, maxMagnitude);

				float turnClipSpeed = ResolveFusionTurnClipSpeed(turnStateDefinition, turningLeft);
				float turnClipLength = ResolveFusionTurnClipLength(turnStateDefinition, turningLeft);
				float animationDeltaTime = DeltaTime * remaining;
				if (turnClipLength > 0.0001f)
				{
					_fusionTurnAnimationTime = Mathf.Repeat(
						_fusionTurnAnimationTime + animationDeltaTime * Mathf.Max(0.0001f, turnClipSpeed) / turnClipLength,
						1.0f);
				}
				else
				{
					_fusionTurnAnimationTime = 0.0f;
				}

				return;
			}

			float remainingDirection = Mathf.Abs(_fusionTurnDirection);
			if (remainingDirection <= 0.0001f)
			{
				_fusionTurnDirection = 0.0f;
				return;
			}

			float delta = DeltaTime * Mathf.Max(0.5f, remainingDirection);
			if (_fusionTurnDirection <= 0.0f)
			{
				_fusionTurnDirection = Mathf.Clamp(_fusionTurnDirection + delta, -1.0f, 0.0f);
			}
			else
			{
				_fusionTurnDirection = Mathf.Clamp(_fusionTurnDirection - delta, 0.0f, 1.0f);
			}
		}

		private void OnFusionFixedUpdate()
		{
			EnsureFusionAnimatorRuntimeInitialized();
			EnsureFusionPlayableGraphBindings();
			if (_fusionRuntimeGraphInstance == null)
			{
				return;
			}

			HandleFusionJetpackState();
			if (HasStateAuthority == true)
			{
				AdvanceFusionTurnDirection();
			}
			SyncFusionParametersBeforeStep();
			_fusionRuntimeGraphInstance.Step(Mathf.Max(0.0f, DeltaTime), _fusionParameters);

			ApplyFusionRuntimeToPlayables();
			ApplyFusionUpperBodyGameplaySideEffects();

			string upperBodyStateCanonical = GetFusionCurrentStateCanonical("UpperBody");
			string shootStateCanonical = GetFusionCurrentStateCanonical("Shoot");
			if (HasStateAuthority == true &&
				TryGetFusionCurrentState("UpperBody", out _, out _, out float upperBodyNormalizedTime) == true)
			{
				if (string.Equals(upperBodyStateCanonical, "Reload", StringComparison.Ordinal) && upperBodyNormalizedTime >= 0.05f)
				{
					_fusionReloadPending = false;
				}
				if (string.Equals(upperBodyStateCanonical, "Unequip", StringComparison.Ordinal))
				{
					bool hasPendingWeapon = _weapons != null && _weapons.PendingWeaponSlot > 0;
					if (hasPendingWeapon == false &&
						upperBodyNormalizedTime >= UPPER_BODY_UNEQUIP_DISARM_TIME &&
						_fusionWeaponCycleActive == false)
					{
						_fusionUnequipPending = false;
						_fusionWeaponCycleTargetSlot = 0;
						_fusionWeaponCycleActive = false;
					}
				}
				if (string.Equals(upperBodyStateCanonical, "Throw", StringComparison.Ordinal) && upperBodyNormalizedTime >= UPPER_BODY_THROW_START_TIME)
				{
					_fusionThrowStartPending = false;
				}
			}
			else if (HasStateAuthority == true &&
			         _fusionEquipPending == false &&
			         _fusionUnequipPending == false &&
			         _fusionWeaponCycleActive == false &&
			         _weapons != null &&
			         _weapons.PendingWeaponSlot > 0 &&
			         _weapons.CurrentWeaponSlot == 0 &&
			         IsFusionUpperBodyGrenadeActive() == false &&
			         string.Equals(upperBodyStateCanonical, "Unequip", StringComparison.OrdinalIgnoreCase) == false &&
			         string.Equals(upperBodyStateCanonical, "Equip", StringComparison.OrdinalIgnoreCase) == false)
			{
				if (IsPendingThrowableWeapon() == true)
				{
					_fusionGrenadeEquipPending = true;
				}
				else
				{
					_fusionEquipPending = true;
				}
			}

			if (HasStateAuthority == true &&
				TryGetFusionCurrentState("Shoot", out _, out _, out float shootNormalizedTime) == true)
			{
				if (string.Equals(shootStateCanonical, "Shoot", StringComparison.Ordinal) == true && shootNormalizedTime >= SHOOT_TRIGGER_DURATION)
				{
					_fusionShootPending = false;
					_fusionShootTimer = 0.0f;
				}
			}

			if (HasStateAuthority == true && _fusionThrowStartPending == true)
			{
				_fusionThrowStartTimer = Mathf.Max(0.0f, _fusionThrowStartTimer - Mathf.Max(0.0f, DeltaTime));
				if (_fusionThrowStartTimer <= 0.0f)
				{
					_fusionThrowStartPending = false;
				}
			}

			if (HasStateAuthority == true && _fusionShootPending == true)
			{
				_fusionShootTimer = Mathf.Max(0.0f, _fusionShootTimer - Mathf.Max(0.0f, DeltaTime));
				if (_fusionShootTimer <= 0.0f)
				{
					_fusionShootPending = false;
				}
			}
		}

		private void OnFusionRenderUpdate()
		{
			EnsureFusionAnimatorRuntimeInitialized();
			EnsureFusionPlayableGraphBindings();
			if (_fusionRuntimeGraphInstance == null)
			{
				return;
			}

			ApplyFusionRuntimeToPlayables();
		}

		private void SyncFusionParametersBeforeStep()
		{
			int currentWeaponSlot = _weapons != null ? _weapons.CurrentWeaponSlot : 0;
			int pendingWeaponSlot = _weapons != null ? _weapons.PendingWeaponSlot : -1;
			string currentUpperBodyState = GetFusionCurrentStateCanonical("UpperBody");
			string currentLowerBodyState = GetFusionCurrentStateCanonical("LowerBody");

			Vector3 moveDirection = Vector3.forward;
			float moveMagnitude = 0.0f;
			if (_kcc != null)
			{
				KCCData fixedData = _kcc.FixedData;
				Vector3 inputDirection = GetFusionPlanarDirection(fixedData.InputDirection);
				Vector3 kinematicDirection = GetFusionPlanarDirection(fixedData.KinematicDirection);
				Vector3 desiredDirection = GetFusionPlanarDirection(fixedData.DesiredVelocity);
				Vector3 realDirection = fixedData.RealVelocity.OnlyXZ();

				if (HasInputAuthority == true || HasStateAuthority == true)
				{
					if (inputDirection.IsAlmostZero(0.025f) == false)
					{
						moveDirection = inputDirection.normalized;
					}
					else if (kinematicDirection.IsAlmostZero(0.025f) == false)
					{
						moveDirection = kinematicDirection.normalized;
					}
					else if (desiredDirection.IsAlmostZero(0.025f) == false)
					{
						moveDirection = desiredDirection.normalized;
					}

					float realVelocityMagnitude = fixedData.RealVelocity.OnlyXZ().magnitude;
					float desiredVelocityMagnitude = fixedData.DesiredVelocity.OnlyXZ().magnitude;
					float kinematicVelocityMagnitude = fixedData.KinematicVelocity.OnlyXZ().magnitude;
					moveMagnitude = Mathf.Min(realVelocityMagnitude, Mathf.Max(kinematicVelocityMagnitude, desiredVelocityMagnitude));
				}
				else
				{
					if (realDirection.IsAlmostZero(0.025f) == false)
					{
						moveDirection = realDirection.normalized;
					}

					moveMagnitude = fixedData.RealSpeed;
				}
			}
			moveMagnitude = Mathf.Max(0.0f, moveMagnitude);

			Vector3 localMove = transform.InverseTransformDirection(moveDirection).OnlyXZ();
			if (localMove.sqrMagnitude > 0.0001f)
			{
				localMove.Normalize();
			}

			if (_agent != null && _agent.Runner != null && _agent.LeftSide == true)
			{
				localMove.x = -localMove.x;
			}

			float moveX = localMove.x * moveMagnitude;
			float moveY = localMove.z * moveMagnitude;
			Vector2 velocityMoveVector2 = new Vector2(moveX, moveY);
			Vector2 inputMoveVector2 = Vector2.zero;
			Vector2 inputLookVector2 = GetFusionLookInputVector2();
			bool inputAim = _kcc != null && _kcc.FixedData.Aim;
			if (_agent != null && _agent.AgentInput != null)
			{
				GameplayInput fixedInput = _agent.AgentInput.FixedInput;
				inputMoveVector2 = fixedInput.MoveDirection;
				inputAim = fixedInput.Aim;
			}

			if (_kcc != null)
			{
				inputAim = _kcc.FixedData.Aim;
			}

			if (_agent != null && _agent.Runner != null && _agent.LeftSide == true)
			{
				inputMoveVector2.x = -inputMoveVector2.x;
			}

			// Proxies do not own input history, fallback to velocity to keep remote motion responsive.
			if (HasInputAuthority == false && HasStateAuthority == false && inputMoveVector2.sqrMagnitude <= 0.000001f)
			{
				inputMoveVector2 = velocityMoveVector2;
			}

			inputLookVector2.x = Mathf.Clamp(inputLookVector2.x, -1.0f, 1.0f);
			inputLookVector2.y = Mathf.Clamp(inputLookVector2.y, -1.0f, 1.0f);

			float lookPitch = _kcc != null ? _kcc.FixedData.LookPitch : 0.0f;
			float realSpeed = _kcc != null ? _kcc.FixedData.RealSpeed : 0.0f;
			bool canTurnPose = realSpeed < 0.1f;
			float turnDirection = canTurnPose == true ? _fusionTurnDirection : 0.0f;
			bool isJetpackActive = IsFusionJetpackStateActive();
			bool isGrounded = _kcc != null && _kcc.FixedData.IsGrounded;
			bool hasJumped = (_kcc != null && _kcc.FixedData.HasJumped) ||
				(_kcc != null && _kcc.FixedData.IsGrounded == false && _kcc.FixedData.RealVelocity.y > 0.1f) ||
				IsFusionJumpingStateActive() == true;
			bool isTurning = (Mathf.Abs(_fusionTurnDirection) > 0.001f && canTurnPose == true) ||
				string.Equals(currentLowerBodyState, "Turn", StringComparison.OrdinalIgnoreCase) == true;
			bool isThrowing = IsFusionUpperBodyGrenadeActive() || _fusionGrenadeEquipPending == true;
			bool isUnequipping = _fusionUnequipPending == true ||
				string.Equals(currentUpperBodyState, "Unequip", StringComparison.OrdinalIgnoreCase) == true;
			bool isEquipping = _fusionEquipPending == true ||
				string.Equals(currentUpperBodyState, "Equip", StringComparison.OrdinalIgnoreCase) == true;
			bool isReloading = _fusionReloadPending == true ||
				string.Equals(currentUpperBodyState, "Reload", StringComparison.OrdinalIgnoreCase) == true;
			bool shootTrigger = _fusionShootPending == true || _fusionShootTimer > 0.0f;
			bool throwTrigger = _fusionThrowStartPending == true || _fusionThrowStartTimer > 0.0f;
			bool reloadTrigger = _fusionReloadPending == true;
			bool jumpTrigger = _kcc != null && _kcc.FixedData.HasJumped;
			bool isShooting = shootTrigger == true ||
				string.Equals(currentUpperBodyState, "Shoot", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(GetFusionCurrentStateCanonical("Shoot"), "Shoot", StringComparison.OrdinalIgnoreCase) == true;
			bool isSprinting = inputMoveVector2.sqrMagnitude >= 0.5625f && inputAim == false;
			int stateWeaponSlot = currentWeaponSlot;
			int graphWeaponSlot = currentWeaponSlot;
			int graphPendingWeaponSlot = pendingWeaponSlot;
			if (isUnequipping == true)
			{
				if (stateWeaponSlot <= 0 && _weapons != null)
				{
					stateWeaponSlot = _weapons.PreviousWeaponSlot;
				}
			}
			else if (isEquipping == true)
			{
				if (pendingWeaponSlot > 0)
				{
					stateWeaponSlot = pendingWeaponSlot;
				}
				else if (stateWeaponSlot <= 0 && _weapons != null)
				{
					stateWeaponSlot = _weapons.PreviousWeaponSlot;
				}
			}

			stateWeaponSlot = Mathf.Max(0, stateWeaponSlot);
			if (isUnequipping == true)
			{
				// Keep all weapon-id driven graph params locked to the outgoing weapon while Unequip plays.
				graphWeaponSlot = stateWeaponSlot;
				graphPendingWeaponSlot = stateWeaponSlot;
			}
			else if (isEquipping == true)
			{
				// Keep all weapon-id driven graph params locked to the incoming weapon while Equip plays.
				int lockedEquipSlot = stateWeaponSlot;
				if (lockedEquipSlot <= 0)
				{
					lockedEquipSlot = graphPendingWeaponSlot > 0 ? graphPendingWeaponSlot : graphWeaponSlot;
				}

				lockedEquipSlot = Mathf.Max(0, lockedEquipSlot);
				graphWeaponSlot = lockedEquipSlot;
				graphPendingWeaponSlot = lockedEquipSlot;
				stateWeaponSlot = lockedEquipSlot;
			}

			if (HasStateAuthority == true && isJetpackActive == false)
			{
				int slotForHistory = currentWeaponSlot > 0 ? currentWeaponSlot : pendingWeaponSlot;
				if (slotForHistory > 0)
				{
					_fusionLastArmedWeaponSlot = (byte)Mathf.Clamp(slotForHistory, 1, 255);
				}
			}

			if (isJetpackActive == true)
			{
				// Jetpack owns full-body presentation and suppresses jump/fall branches.
				isGrounded = true;
				hasJumped = false;
				jumpTrigger = false;
				isSprinting = false;
			}

			_fusionParameters.SetInt("param_weapon_slot", graphWeaponSlot);
			_fusionParameters.SetInt("param_pending_weapon_slot", graphPendingWeaponSlot);
			_fusionParameters.SetInt("param_state_weapon", stateWeaponSlot);
			_fusionParameters.SetFloat("param_move_x", moveX);
			_fusionParameters.SetFloat("param_move_y", moveY);
			_fusionParameters.SetVector2("param_move_vector2", velocityMoveVector2);
			_fusionParameters.SetVector2("param_input_move_vector2", inputMoveVector2);
			_fusionParameters.SetVector2("param_input_look_vector2", inputLookVector2);
			_fusionParameters.SetBool("param_input_aim", inputAim);
			_fusionParameters.SetFloat("param_look_pitch", lookPitch);
			_fusionParameters.SetFloat("param_turn_direction", turnDirection);
			_fusionParameters.SetBool("param_is_dead", _fusionIsDead);
			_fusionParameters.SetBool("param_is_jetpack_active", isJetpackActive);
			_fusionParameters.SetBool("param_is_grounded", isGrounded);
			_fusionParameters.SetBool("param_has_jumped", hasJumped);
			_fusionParameters.SetBool("param_state_is_shooting", isShooting);
			_fusionParameters.SetBool("param_state_is_sprinting", isSprinting);
			_fusionParameters.SetBool("param_is_reloading", isReloading);
			_fusionParameters.SetBool("param_is_equipping", isEquipping);
			_fusionParameters.SetBool("param_is_unequipping", isUnequipping);
			_fusionParameters.SetBool("param_equip_trigger", _fusionEquipPending);
			_fusionParameters.SetBool("param_unequip_trigger", _fusionUnequipPending);
			_fusionParameters.SetBool("param_is_throwing", isThrowing);
			_fusionParameters.SetBool("param_is_turning", isTurning);
			_fusionParameters.SetBool("param_input_shoot", shootTrigger);
			_fusionParameters.SetBool("param_input_reload", reloadTrigger);
			_fusionParameters.SetBool("param_input_jump", jumpTrigger);
			_fusionParameters.SetBool("param_input_throw", throwTrigger);
			_fusionParameters.SetBool("param_shoot_trigger", shootTrigger);
			_fusionParameters.SetBool("param_throw_start", throwTrigger);
			_fusionParameters.SetBool("param_throw_hold", _fusionThrowHold);
			_fusionParameters.SetBool("param_grenade_equip", _fusionGrenadeEquipPending);

			SetFusionRuntimeInt("param_weapon_slot", graphWeaponSlot);
			SetFusionRuntimeInt("param_pending_weapon_slot", graphPendingWeaponSlot);
			SetFusionRuntimeInt("param_state_weapon", stateWeaponSlot);
			SetFusionRuntimeFloat("param_move_x", moveX);
			SetFusionRuntimeFloat("param_move_y", moveY);
			SetFusionRuntimeVector2("param_move_vector2", velocityMoveVector2);
			SetFusionRuntimeVector2("param_input_move_vector2", inputMoveVector2);
			SetFusionRuntimeVector2("param_input_look_vector2", inputLookVector2);
			SetFusionRuntimeBool("param_input_aim", inputAim);
			SetFusionRuntimeFloat("param_look_pitch", lookPitch);
			SetFusionRuntimeFloat("param_turn_direction", turnDirection);
			SetFusionRuntimeBool("param_is_dead", _fusionIsDead);
			SetFusionRuntimeBool("param_is_jetpack_active", isJetpackActive);
			SetFusionRuntimeBool("param_is_grounded", isGrounded);
			SetFusionRuntimeBool("param_has_jumped", hasJumped);
			SetFusionRuntimeBool("param_state_is_shooting", isShooting);
			SetFusionRuntimeBool("param_state_is_sprinting", isSprinting);
			SetFusionRuntimeBool("param_is_reloading", isReloading);
			SetFusionRuntimeBool("param_is_equipping", isEquipping);
			SetFusionRuntimeBool("param_is_unequipping", isUnequipping);
			SetFusionRuntimeBool("param_equip_trigger", _fusionEquipPending);
			SetFusionRuntimeBool("param_unequip_trigger", _fusionUnequipPending);
			SetFusionRuntimeBool("param_is_throwing", isThrowing);
			SetFusionRuntimeBool("param_is_turning", isTurning);
			SetFusionRuntimeBool("param_input_shoot", shootTrigger);
			SetFusionRuntimeBool("param_input_reload", reloadTrigger);
			SetFusionRuntimeBool("param_input_jump", jumpTrigger);
			SetFusionRuntimeBool("param_input_throw", throwTrigger);
			SetFusionRuntimeBool("param_shoot_trigger", shootTrigger);
			SetFusionRuntimeBool("param_throw_start", throwTrigger);
			SetFusionRuntimeBool("param_throw_hold", _fusionThrowHold);
			SetFusionRuntimeBool("param_grenade_equip", _fusionGrenadeEquipPending);
		}

		private void ApplyFusionUpperBodyGameplaySideEffects()
		{
			if (HasStateAuthority == false)
			{
				return;
			}

			if (_weapons == null)
			{
				return;
			}

			if (TryGetFusionCurrentState("UpperBody", out FusionAnimatorRuntimeEvaluator evaluator, out FusionAnimatorStateDefinition stateDefinition, out float normalizedTime) == false ||
				stateDefinition == null)
			{
				ResetFusionUpperBodySideEffectFlags();
				return;
			}

			if (string.Equals(_fusionUpperBodyStateId, evaluator.CurrentStateId, StringComparison.Ordinal) == false)
			{
				_fusionUpperBodyStateId = evaluator.CurrentStateId ?? string.Empty;
				_fusionUnequipDisarmApplied = false;
				_fusionEquipArmApplied = false;
				_fusionGrenadeEquipArmApplied = false;
				_fusionGrenadeArmProjectileApplied = false;
				_fusionGrenadeThrowFireApplied = false;
			}

			string stateName = stateDefinition.Name ?? string.Empty;
			string canonicalStateName = GetCanonicalFusionStateName(stateName);

			if (string.Equals(canonicalStateName, "Unequip", StringComparison.OrdinalIgnoreCase))
			{
				if (_fusionUnequipDisarmApplied == false && normalizedTime >= UPPER_BODY_UNEQUIP_DISARM_TIME)
				{
					_weapons.DisarmCurrentWeapon();
					_fusionUnequipDisarmApplied = true;
				}

				if (normalizedTime >= UPPER_BODY_UNEQUIP_SWITCH_TIME)
				{
					if (_fusionWeaponCycleActive == true)
					{
						int cycleTargetSlot = _fusionWeaponCycleTargetSlot;
						if (cycleTargetSlot != _weapons.PendingWeaponSlot)
						{
							_weapons.SetPendingWeapon(cycleTargetSlot);
						}

						_fusionEquipPending = true;
					}
					else
					{
						_fusionEquipPending = false;
					}

					_fusionUnequipPending = false;
				}

				return;
			}

			if (string.Equals(canonicalStateName, "Equip", StringComparison.OrdinalIgnoreCase))
			{
				if (_fusionEquipArmApplied == false && normalizedTime >= UPPER_BODY_EQUIP_ARM_TIME)
				{
					_weapons.ArmPendingWeapon();
					_fusionEquipPending = false;
					_fusionWeaponCycleTargetSlot = 0;
					_fusionWeaponCycleActive = false;
					_fusionEquipArmApplied = true;
				}

				return;
			}

			if (stateName.StartsWith("Grenade/Equip", StringComparison.OrdinalIgnoreCase))
			{
				if (_fusionGrenadeEquipArmApplied == false && normalizedTime >= UPPER_BODY_GRENDE_EQUIP_TIME)
				{
					_weapons.ArmPendingWeapon();
					_fusionGrenadeEquipPending = false;
					_fusionGrenadeEquipArmApplied = true;
				}

				return;
			}

			if (stateName.StartsWith("Grenade/Arm", StringComparison.OrdinalIgnoreCase))
			{
				if (_fusionGrenadeArmProjectileApplied == false)
				{
					if (_weapons.CurrentWeapon is ThrowableWeapon throwableWeapon)
					{
						throwableWeapon.ArmProjectile();
					}

					_fusionGrenadeArmProjectileApplied = true;
				}

				return;
			}

			if (stateName.StartsWith("Grenade/Throw", StringComparison.OrdinalIgnoreCase))
			{
				if (_fusionGrenadeThrowFireApplied == false && normalizedTime >= UPPER_BODY_GRENDE_THROW_FIRE_TIME)
				{
					_weapons.Fire();
					_fusionGrenadeThrowFireApplied = true;
				}

				return;
			}
		}

		private void HandleFusionJetpackState()
		{
			if (HasStateAuthority == false)
			{
				return;
			}

			if (_weapons == null)
			{
				return;
			}

			bool isJetpackActive = IsFusionJetpackStateActive();
			if (isJetpackActive == true)
			{
				if (_fusionJetpackSwitchQueued == false)
				{
					int resumeSlot = _weapons.CurrentWeaponSlot;
					if (resumeSlot <= 0)
					{
						resumeSlot = _weapons.PendingWeaponSlot;
					}
					if (resumeSlot <= 0)
					{
						resumeSlot = _fusionLastArmedWeaponSlot;
					}
					if (resumeSlot < 0)
					{
						resumeSlot = 0;
					}

					_fusionJetpackResumeWeaponSlot = (byte)Mathf.Clamp(resumeSlot, 0, 255);
					_fusionJetpackSwitchQueued = true;
				}

				if (_fusionJetpackDisarmApplied == false && _weapons.CurrentWeaponSlot != 0)
				{
					_weapons.DisarmCurrentWeapon();
					_fusionJetpackDisarmApplied = true;
				}
				if (_weapons.CurrentWeaponSlot == 0)
				{
					_fusionJetpackDisarmApplied = true;
				}

				_fusionReloadPending = false;
				_fusionUnequipPending = false;
				_fusionEquipPending = false;
				_fusionGrenadeEquipPending = false;
				_fusionThrowStartPending = false;
				_fusionThrowHold = false;
				_fusionThrowStartTimer = 0.0f;
				_fusionShootPending = false;
				_fusionShootTimer = 0.0f;
				_fusionTurnDirection = 0.0f;
				_fusionTurnRemainingTime = 0.0f;
				_fusionTurnAnimationTime = 0.0f;
				_fusionWeaponCycleTargetSlot = 0;
				_fusionWeaponCycleActive = false;
				return;
			}

			if (_fusionJetpackSwitchQueued == true)
			{
				int slotToRestore = _fusionJetpackResumeWeaponSlot;
				if (slotToRestore <= 0)
				{
					slotToRestore = _fusionLastArmedWeaponSlot;
				}

				_fusionJetpackSwitchQueued = false;
				_fusionJetpackDisarmApplied = false;
				_fusionJetpackResumeWeaponSlot = 0;

				if (slotToRestore > 0 && _weapons.HasWeapon(slotToRestore, false) == true)
				{
					_fusionLastArmedWeaponSlot = (byte)Mathf.Clamp(slotToRestore, 1, 255);
					_weapons.SetPendingWeapon(slotToRestore);
					_fusionReloadPending = false;
					_fusionUnequipPending = false;
					_fusionEquipPending = false;
					_fusionGrenadeEquipPending = false;
					_fusionThrowStartPending = false;
					_fusionThrowHold = false;
					_fusionThrowStartTimer = 0.0f;
					_fusionShootPending = false;
					_fusionShootTimer = 0.0f;
					_fusionTurnDirection = 0.0f;
					_fusionTurnRemainingTime = 0.0f;
					_fusionTurnAnimationTime = 0.0f;
					_fusionWeaponCycleTargetSlot = 0;
					_fusionWeaponCycleActive = false;

					if (IsPendingThrowableWeapon() == true)
					{
						_fusionGrenadeEquipPending = true;
					}
					else
					{
						_weapons.DisarmCurrentWeapon();
						_fusionEquipPending = true;
					}

					ResetFusionUpperBodySideEffectFlags();
				}
			}
		}

		private bool IsFusionJetpackStateActive()
		{
			if (_jetpack != null && _jetpack.IsActive == true)
			{
				return true;
			}

			return IsFusionStateCanonicalActive("Jetpack");
		}

		private bool CanSwitchWeaponsFromGrenadeState()
		{
			if (TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition stateDefinition, out _) == false ||
				stateDefinition == null ||
				string.IsNullOrWhiteSpace(stateDefinition.Name) == true)
			{
				return true;
			}

			if (stateDefinition.Name.StartsWith("Grenade/", StringComparison.OrdinalIgnoreCase) == false)
			{
				return true;
			}

			return stateDefinition.Name.StartsWith("Grenade/Throw", StringComparison.OrdinalIgnoreCase) == false;
		}

		private bool IsPendingThrowableWeapon()
		{
			if (_weapons == null)
			{
				return false;
			}

			if (_weapons.PendingWeapon is ThrowableWeapon)
			{
				return true;
			}

			return _weapons.PendingWeaponSlot > 2;
		}

		private bool TryGetFusionTurnPresentationState(out FusionAnimatorStateDefinition turnStateDefinition, out FusionAnimatorStatePresentationDefinition turnPresentation)
		{
			turnStateDefinition = null;
			turnPresentation = null;

			if (_fusionStatesById == null || _fusionStatesById.Count == 0)
			{
				return false;
			}

			TryResolveFusionLayerId("LowerBody", out string lowerBodyLayerId);

			int targetSlot = _weapons != null ? _weapons.CurrentWeaponSlot : 0;
			if (targetSlot > 2)
			{
				targetSlot = 1; // TurnState uses pistol variant for grenades.
			}
			if (targetSlot < 0)
			{
				targetSlot = 0;
			}

			FusionAnimatorStateDefinition fallbackState = null;
			foreach (KeyValuePair<string, FusionAnimatorStateDefinition> pair in _fusionStatesById)
			{
				FusionAnimatorStateDefinition state = pair.Value;
				if (state == null)
				{
					continue;
				}

				if (string.IsNullOrWhiteSpace(lowerBodyLayerId) == false &&
					string.Equals(state.LayerId, lowerBodyLayerId, StringComparison.Ordinal) == false)
				{
					continue;
				}

				if (string.Equals(GetCanonicalFusionStateName(state.Name), "Turn", StringComparison.OrdinalIgnoreCase) == false)
				{
					continue;
				}

				if (fallbackState == null)
				{
					fallbackState = state;
				}

				int slotIndex = ResolveFusionStateVariantSlotIndex(state.Name);
				if (slotIndex == targetSlot)
				{
					turnStateDefinition = state;
					break;
				}
			}

			if (turnStateDefinition == null)
			{
				turnStateDefinition = fallbackState;
			}

			if (turnStateDefinition == null ||
				turnStateDefinition.Presentation == null ||
				turnStateDefinition.Presentation.Semantic != FusionAnimatorStateSemantic.TurnInPlace)
			{
				return false;
			}

			turnPresentation = turnStateDefinition.Presentation;
			return true;
		}

		private static float NormalizeFusionTurnDirection(float remainingTime, float maxMagnitude)
		{
			float safeMax = Mathf.Max(0.0001f, maxMagnitude);
			return Mathf.Clamp(remainingTime / safeMax, -1.0f, 1.0f);
		}

		private static int ResolveFusionStateVariantSlotIndex(string stateName)
		{
			if (string.IsNullOrWhiteSpace(stateName))
			{
				return 0;
			}

			int open = stateName.LastIndexOf('(');
			int close = stateName.LastIndexOf(')');
			if (open >= 0 && close > open)
			{
				string slotLabel = stateName.Substring(open + 1, close - open - 1).Trim();
				if (slotLabel.Equals("Unarmed", StringComparison.OrdinalIgnoreCase))
				{
					return 0;
				}
				if (slotLabel.Equals("Pistol", StringComparison.OrdinalIgnoreCase))
				{
					return 1;
				}
				if (slotLabel.Equals("Rifle", StringComparison.OrdinalIgnoreCase))
				{
					return 2;
				}
			}

			if (stateName.IndexOf("Pistol", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 1;
			}
			if (stateName.IndexOf("Rifle", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 2;
			}

			return 0;
		}

		private float ResolveFusionTurnClipSpeed(FusionAnimatorStateDefinition stateDefinition, bool turningLeft)
		{
			if (TryResolveFusionTurnBlendTreeChild(stateDefinition, turningLeft, out FusionAnimatorBlendTreeChild child) == false || child == null)
			{
				return 1.0f;
			}

			return Mathf.Max(0.0001f, child.TimeScale);
		}

		private float ResolveFusionTurnClipLength(FusionAnimatorStateDefinition stateDefinition, bool turningLeft)
		{
			if (TryResolveFusionTurnBlendTreeChild(stateDefinition, turningLeft, out FusionAnimatorBlendTreeChild child) == false || child == null)
			{
				return 1.0f;
			}

			AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, child, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
			return clip != null ? Mathf.Max(0.0001f, clip.length) : 1.0f;
		}

		private static bool TryResolveFusionTurnBlendTreeChild(FusionAnimatorStateDefinition stateDefinition, bool turningLeft, out FusionAnimatorBlendTreeChild child)
		{
			child = null;
			if (stateDefinition == null ||
				stateDefinition.MotionType != FusionAnimatorMotionType.BlendTree ||
				stateDefinition.BlendTree == null ||
				stateDefinition.BlendTree.Children == null ||
				stateDefinition.BlendTree.Children.Count == 0)
			{
				return false;
			}

			float targetX = turningLeft == true ? -1.0f : 1.0f;
			float bestDistance = float.MaxValue;
			FusionAnimatorBlendTreeChild bestChild = null;

			for (int i = 0; i < stateDefinition.BlendTree.Children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild candidate = stateDefinition.BlendTree.Children[i];
				if (candidate == null)
				{
					continue;
				}

				float x = Mathf.Abs(candidate.Position.x) > 0.0001f || Mathf.Abs(candidate.Position.y) > 0.0001f
					? candidate.Position.x
					: candidate.Threshold;
				float distance = Mathf.Abs(x - targetX);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					bestChild = candidate;
				}
			}

			child = bestChild;
			return child != null;
		}

		private bool IsFusionJumpingStateActive()
		{
			return IsFusionStateCanonicalActive(
				"Jump",
				"Fall",
				"Land",
				"Start_Jump",
				"Loop_Jump",
				"End_Jump");
		}

		private bool IsFusionStateCanonicalActive(params string[] canonicalStateNames)
		{
			if (_fusionRuntimeGraphInstance == null ||
				_fusionAnimatorGraph == null ||
				_fusionAnimatorGraph.Layers == null ||
				canonicalStateNames == null ||
				canonicalStateNames.Length == 0)
			{
				return false;
			}

			for (int i = 0; i < _fusionAnimatorGraph.Layers.Count; ++i)
			{
				FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
				if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
				{
					continue;
				}

				FusionAnimatorRuntimeEvaluator evaluator = _fusionRuntimeGraphInstance.GetLayerEvaluator(layer.Id);
				if (evaluator == null)
				{
					continue;
				}

				if (TryGetGraphStateDefinition(evaluator.CurrentStateId, out FusionAnimatorStateDefinition stateDefinition) == false ||
					stateDefinition == null)
				{
					continue;
				}

				string canonical = GetCanonicalFusionStateName(stateDefinition.Name);
				if (string.IsNullOrWhiteSpace(canonical))
				{
					continue;
				}

				for (int canonicalIndex = 0; canonicalIndex < canonicalStateNames.Length; ++canonicalIndex)
				{
					string targetCanonical = canonicalStateNames[canonicalIndex];
					if (string.IsNullOrWhiteSpace(targetCanonical))
					{
						continue;
					}

					if (string.Equals(canonical, targetCanonical, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
				}
			}

			return false;
		}

		private bool IsFusionUpperBodyAnyActive()
		{
			if (TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition stateDefinition, out _) == false ||
				stateDefinition == null ||
				string.IsNullOrWhiteSpace(stateDefinition.Name) == true)
			{
				return false;
			}

			string canonicalState = GetCanonicalFusionStateName(stateDefinition.Name);
			if (string.IsNullOrWhiteSpace(canonicalState) == true ||
				string.Equals(canonicalState, "Idle", StringComparison.OrdinalIgnoreCase) == true ||
				string.Equals(canonicalState, "Aim", StringComparison.OrdinalIgnoreCase) == true ||
				canonicalState.IndexOf("Aim", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return false;
			}

			return true;
		}

		private bool CanProcessFusionGameplayRequests()
		{
			return HasStateAuthority == true || HasInputAuthority == true;
		}

		private bool IsFusionUpperBodyGrenadeActive()
		{
			if (_fusionGrenadeEquipPending == true || _fusionThrowStartPending == true || _fusionThrowHold == true)
			{
				return true;
			}

			if (TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition stateDefinition, out _) == true &&
				stateDefinition != null &&
				string.IsNullOrWhiteSpace(stateDefinition.Name) == false &&
				stateDefinition.Name.StartsWith("Grenade/", StringComparison.OrdinalIgnoreCase) == true)
			{
				return true;
			}

			return false;
		}

		private bool IsFusionGrenadeThrowStateActive()
		{
			if (_fusionThrowStartPending == true)
			{
				return true;
			}

			if (TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition stateDefinition, out _) == false ||
				stateDefinition == null ||
				string.IsNullOrWhiteSpace(stateDefinition.Name) == true)
			{
				return false;
			}

			return stateDefinition.Name.StartsWith("Grenade/Throw", StringComparison.OrdinalIgnoreCase);
		}

		private bool TryGetFusionLayerEvaluator(string layerName, out FusionAnimatorRuntimeEvaluator evaluator)
		{
			evaluator = null;
			if (_fusionRuntimeGraphInstance == null || string.IsNullOrWhiteSpace(layerName))
			{
				return false;
			}

			if (TryResolveFusionLayerId(layerName, out string layerId) == false ||
				string.IsNullOrWhiteSpace(layerId))
			{
				return false;
			}

			evaluator = _fusionRuntimeGraphInstance.GetLayerEvaluator(layerId);
			return evaluator != null;
		}

		private bool TryGetFusionCurrentState(string layerName, out FusionAnimatorRuntimeEvaluator evaluator, out FusionAnimatorStateDefinition stateDefinition, out float normalizedTime)
		{
			evaluator = null;
			stateDefinition = null;
			normalizedTime = 0.0f;

			if (TryGetFusionLayerEvaluator(layerName, out evaluator) == false || evaluator == null)
			{
				return false;
			}

			if (TryGetGraphStateDefinition(evaluator.CurrentStateId, out stateDefinition) == false || stateDefinition == null)
			{
				return false;
			}

			float referenceLengthSeconds = ResolveFusionStateReferenceLengthSeconds(stateDefinition);
			if (referenceLengthSeconds > 0.0001f)
			{
				normalizedTime = evaluator.CurrentStateTime / referenceLengthSeconds;
				normalizedTime = IsFusionStateLooping(stateDefinition) == true ? Mathf.Repeat(normalizedTime, 1.0f) : Mathf.Clamp01(normalizedTime);
			}

			return true;
		}

		private string GetFusionCurrentStateCanonical(string layerName)
		{
			if (TryGetFusionCurrentState(layerName, out _, out FusionAnimatorStateDefinition stateDefinition, out _) == false ||
				stateDefinition == null)
			{
				return string.Empty;
			}

			return GetCanonicalFusionStateName(stateDefinition.Name);
		}

		private bool TryGetGraphStateDefinition(string stateId, out FusionAnimatorStateDefinition stateDefinition)
		{
			if (string.IsNullOrWhiteSpace(stateId) == false &&
				_fusionStatesById.TryGetValue(stateId, out stateDefinition) == true &&
				stateDefinition != null)
			{
				return true;
			}

			stateDefinition = null;
			return false;
		}

		private static string GetCanonicalFusionStateName(string stateName)
		{
			if (string.IsNullOrWhiteSpace(stateName))
			{
				return string.Empty;
			}

			string canonical = stateName.Trim();
			int scopeSeparator = canonical.LastIndexOf('/');
			if (scopeSeparator >= 0 && scopeSeparator + 1 < canonical.Length)
			{
				canonical = canonical.Substring(scopeSeparator + 1);
			}

			int variantSeparator = canonical.IndexOf(" (", StringComparison.Ordinal);
			if (variantSeparator > 0)
			{
				canonical = canonical.Substring(0, variantSeparator);
			}

			return canonical.Trim();
		}

		private float ResolveFusionStateReferenceLengthSeconds(FusionAnimatorStateDefinition state)
		{
			if (state == null)
			{
				return 0.0f;
			}

			float maxLengthSeconds = 0.0f;
			if (state.MotionType == FusionAnimatorMotionType.BlendTree &&
				state.BlendTree != null &&
				state.BlendTree.Children != null)
			{
				for (int i = 0; i < state.BlendTree.Children.Count; ++i)
				{
					FusionAnimatorBlendTreeChild child = state.BlendTree.Children[i];
					AnimationClip childClip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, child, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
					if (childClip == null)
					{
						continue;
					}

					maxLengthSeconds = Mathf.Max(maxLengthSeconds, Mathf.Max(0.0f, childClip.length));
				}
			}
			else if (state.Clips != null)
			{
				for (int i = 0; i < state.Clips.Count; ++i)
				{
					FusionAnimatorClipSlot clipSlot = state.Clips[i];
					AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, clipSlot, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter);
					if (clip == null)
					{
						continue;
					}

					maxLengthSeconds = Mathf.Max(maxLengthSeconds, Mathf.Max(0.0f, clip.length));
				}
			}

			return maxLengthSeconds;
		}

		private bool IsFusionStateLooping(FusionAnimatorStateDefinition state)
		{
			if (state == null)
			{
				return true;
			}

			if (state.MotionType == FusionAnimatorMotionType.BlendTree)
			{
				return true;
			}

			if (state.Clips == null || state.Clips.Count == 0)
			{
				return true;
			}

			for (int i = 0; i < state.Clips.Count; ++i)
			{
				FusionAnimatorClipSlot clip = state.Clips[i];
				if (clip != null && FusionAnimatorClipBindingUtility.ResolveLoop(_fusionAnimatorGraph, clip, EvaluateFusionBindingCondition, ResolveFusionBindingClipIndexParameter) == false)
				{
					return false;
				}
			}

			return true;
		}

		private static Vector3 GetFusionPlanarDirection(Vector3 direction)
		{
			if (Mathf.Abs(direction.z) <= 0.0001f && Mathf.Abs(direction.y) > 0.0001f)
			{
				return new Vector3(direction.x, 0.0f, direction.y);
			}

			return direction.OnlyXZ();
		}

		private Vector2 GetFusionLookInputVector2()
		{
			Vector3 aimDirection = Vector3.zero;

			if (_agent != null && _agent.Aiming != null)
			{
				if (_agent.Aiming.TryGetCrosshairAndHitPoints(true, out Vector3 fireOrigin, out _, out Vector3 characterHitPoint, out _) == true)
				{
					Vector3 origin = fireOrigin;
					if (origin.sqrMagnitude <= 0.0001f)
					{
						origin = transform.position;
					}

					aimDirection = characterHitPoint - origin;
				}

				if (aimDirection.sqrMagnitude <= 0.0001f)
				{
					_agent.Aiming.GetAimPose(true, out Vector3 origin, out Vector3 targetPoint);
					aimDirection = targetPoint - origin;
				}
			}

			if (aimDirection.sqrMagnitude <= 0.0001f && _weapons != null && _weapons.CurrentWeaponHandle != null)
			{
				aimDirection = _weapons.CurrentWeaponHandle.forward;
			}

			if (aimDirection.sqrMagnitude <= 0.0001f && _kcc != null)
			{
				Quaternion lookRotation = _kcc.FixedData.LookRotation;
				if (float.IsNaN(lookRotation.x) == false &&
					float.IsNaN(lookRotation.y) == false &&
					float.IsNaN(lookRotation.z) == false &&
					float.IsNaN(lookRotation.w) == false)
				{
					aimDirection = lookRotation * Vector3.forward;
				}
			}

			if (aimDirection.sqrMagnitude <= 0.0001f)
			{
				aimDirection = transform.forward;
			}

			aimDirection.Normalize();
			Vector3 localAimDirection = transform.InverseTransformDirection(aimDirection);
			return new Vector2(
				Mathf.Clamp(localAimDirection.x, -1.0f, 1.0f),
				Mathf.Clamp(localAimDirection.y, -1.0f, 1.0f));
		}

		private void SnapWeapon()
		{
			if (ApplicationSettings.IsBatchServer == true)
				return;
			if (_weapons.CurrentWeapon == null || CanSnapHand() == false)
				return;

			Transform weaponHandle = _weapons.CurrentWeaponHandle;
			if (HasInputAuthority == true || _agent.IsObserved == true)
			{
				weaponHandle.localRotation = _weapons.CurrentWeaponBaseRotation;

				Quaternion handleRotation = weaponHandle.rotation;
				Vector3 targetPoint = weaponHandle.position + transform.forward * 100.0f;
				if (_agent.Aiming != null)
				{
					if (_agent.Aiming.TryGetCrosshairAndHitPoints(false, out _, out _, out Vector3 characterHitPoint, out _) == true)
					{
						targetPoint = characterHitPoint;
					}
					else
					{
						_agent.Aiming.GetAimPose(false, out _, out targetPoint);
					}
				}
				else
				{
					Vector2 lookRotation = _kcc.Data.GetLookRotation(true, true);
					Vector3 lookDirection = Quaternion.Euler(lookRotation.x, lookRotation.y, 0.0f) * Vector3.forward;
					if (lookDirection.sqrMagnitude > 0.0001f)
					{
						targetPoint = weaponHandle.position + lookDirection.normalized * 100.0f;
					}
				}

				Quaternion targetRotation = Quaternion.LookRotation(targetPoint - weaponHandle.position);

				float   snapPower    = Mathf.Clamp(Mathf.Abs(_kcc.FixedData.LookPitch) / 60.0f, _aimSnapPower, 1.0f);
				Vector3 snapRotation = Quaternion.Slerp(handleRotation, targetRotation, snapPower).eulerAngles;

				snapRotation.y = targetRotation.eulerAngles.y;

				weaponHandle.rotation = Quaternion.Euler(snapRotation);
			}
			else
			{
				Vector2 lookRotation = _kcc.FixedData.GetLookRotation(true, true);
				Vector3 lookDirection = Quaternion.Euler(lookRotation.x, lookRotation.y, 0.0f) * Vector3.forward;
				if (lookDirection.sqrMagnitude <= 0.0001f)
				{
					lookDirection = transform.forward;
				}

				weaponHandle.rotation = Quaternion.LookRotation(lookDirection);
			}

			Transform leftHandTarget = _weapons.CurrentWeapon.LeftHandTarget;
			if (_enableLeftHandIK == false)
				return;

			if (_fullBodyIK != null)
			{
				var leftEffector = _fullBodyIK.solver.leftHandEffector;

				if (leftHandTarget != null)
				{
					leftEffector.position = leftHandTarget.position;
					leftEffector.rotation = leftHandTarget.rotation;
					leftEffector.positionWeight = 1.0f;
					leftEffector.rotationWeight = 1.0f;

					_fullBodyIK.solver.leftArmChain.pull = 1.0f;
					_fullBodyIK.solver.leftArmChain.bendConstraint.weight = 1.0f;
					_fullBodyIK.solver.leftArmMapping.weight = 1.0f;
					return;
				}

				leftEffector.positionWeight = 0.0f;
				leftEffector.rotationWeight = 0.0f;
				_fullBodyIK.solver.leftArmChain.pull = 0.0f;
				_fullBodyIK.solver.leftArmChain.bendConstraint.weight = 0.0f;
				_fullBodyIK.solver.leftArmMapping.weight = 0.0f;
			}

			if (leftHandTarget != null && _leftHand != null && _leftLowerArm != null && _leftUpperArm != null)
			{
				Vector3    leftHandLocalPosition       = _leftLowerArm.InverseTransformPoint(_leftHand.position);
				Vector3    leftHandTargetLocalPosition = _leftLowerArm.InverseTransformPoint(leftHandTarget.position);
				Quaternion leftLowerArmRotation        = Quaternion.FromToRotation(leftHandLocalPosition, leftHandTargetLocalPosition);

				_leftLowerArm.rotation = leftLowerArmRotation * _leftLowerArm.rotation;

				for (int i = 0; i < 1; ++i)
				{
					Vector3    leftLowerArmOffset              = leftHandTarget.position - _leftHand.position;
					Vector3    leftLowerArmTargetPosition      = _leftLowerArm.position + leftLowerArmOffset;
					Vector3    leftLowerArmLocalPosition       = _leftUpperArm.InverseTransformPoint(_leftLowerArm.position);
					Vector3    leftLowerArmTargetLocalPosition = _leftUpperArm.InverseTransformPoint(leftLowerArmTargetPosition);
					Quaternion leftUpperArmRotation            = Quaternion.FromToRotation(leftLowerArmLocalPosition, leftLowerArmTargetLocalPosition);

					_leftUpperArm.rotation = leftUpperArmRotation * _leftUpperArm.rotation;

					leftHandLocalPosition       = _leftLowerArm.InverseTransformPoint(_leftHand.position);
					leftHandTargetLocalPosition = _leftLowerArm.InverseTransformPoint(leftHandTarget.position);
					leftLowerArmRotation        = Quaternion.FromToRotation(leftHandLocalPosition, leftHandTargetLocalPosition);

					_leftLowerArm.rotation = leftLowerArmRotation * _leftLowerArm.rotation;
				}

				_leftHand.position = Vector3.Lerp(_leftHand.position, leftHandTarget.position, 0.75f);
				_leftHand.rotation = Quaternion.Slerp(_leftHand.rotation, leftHandTarget.rotation, 0.75f);
			}
		}

		private bool CanSnapHand()
		{
			if (UseFusionAnimatorRuntime == true)
			{
				if (_fusionIsDead == true)
					return false;
				if (_jetpack != null && _jetpack.IsActive == true)
					return false;

				if (TryGetFusionCurrentState("UpperBody", out _, out FusionAnimatorStateDefinition stateDefinition, out float normalizedTime) == true &&
					stateDefinition != null)
				{
					string canonical = GetCanonicalFusionStateName(stateDefinition.Name);
					if (string.Equals(canonical, "Reload", StringComparison.OrdinalIgnoreCase) == true)
						return normalizedTime >= 0.85f;
					if (string.Equals(canonical, "Equip", StringComparison.OrdinalIgnoreCase) == true)
						return normalizedTime >= 0.75f;
					if (string.Equals(canonical, "Unequip", StringComparison.OrdinalIgnoreCase) == true)
						return false;
					if (string.IsNullOrWhiteSpace(stateDefinition.Name) == false &&
						stateDefinition.Name.StartsWith("Grenade/", StringComparison.OrdinalIgnoreCase) == true)
						return false;
				}

				return true;
			}

			if (_fullBody == null || _upperBody == null)
				return true;

			if (_fullBody.Dead.IsActive() == true || _fullBody.Jetpack.IsActive() == true)
				return false;

			if (_upperBody.HasActiveState() == true)
			{
				if (_upperBody.Reload.IsFinished(0.85f) == true)
					return true;
				if (_upperBody.Equip.IsFinished(0.75f) == true)
					return true;

				return false;
			}

			return true;
		}
	}
}

