namespace TPSBR
{
	using System;
	using System.Collections.Generic;
	using Fusion;
	using FusionAnimator;
	using Fusion.Addons.KCC;
	using Fusion.Addons.AnimationController;
	using UnityEngine;
	using UnityEngine.Animations;
	using UnityEngine.Playables;

	public sealed partial class CharacterAnimationController
	{
		private const int   MAX_FUSION_GRAPH_LAYERS = 4;
		private const float FUSION_MIN_CLIP_LENGTH  = 0.01f;
		private const float FUSION_WEIGHT_EPSILON   = 0.000001f;
		private const float LOOK_YAW_NORMALIZATION  = 45.0f;
		private const float LOOK_PITCH_NORMALIZATION = 90.0f;
		private const float LOOK_YAW_INPUT_DEADZONE = 1.0f;
		private const float SPRINT_SPEED_THRESHOLD  = 4.0f;
		private const float TRIGGER_PULSE_DURATION  = 0.06f;

		private int _fusionShootTriggerUntilTick = int.MinValue;
		private int _fusionReloadTriggerTick = int.MinValue;
		private int _fusionThrowTriggerTick = int.MinValue;
		private int _fusionEquipTriggerTick = int.MinValue;
		private bool _fusionNetIsThrowing;

		private readonly int[] _fusionNetCurrentStateIndices = new int[MAX_FUSION_GRAPH_LAYERS];
		private readonly float[] _fusionNetCurrentStateTimes = new float[MAX_FUSION_GRAPH_LAYERS];
		private readonly int[] _fusionNetBlendFromStateIndices = new int[MAX_FUSION_GRAPH_LAYERS];
		private readonly float[] _fusionNetBlendFromStateTimes = new float[MAX_FUSION_GRAPH_LAYERS];
		private readonly int[] _fusionNetBlendToStateIndices = new int[MAX_FUSION_GRAPH_LAYERS];
		private readonly float[] _fusionNetBlendDurations = new float[MAX_FUSION_GRAPH_LAYERS];
		private readonly float[] _fusionNetBlendElapsed = new float[MAX_FUSION_GRAPH_LAYERS];

		private struct FusionClipPose
		{
			public AnimationClip Clip;
			public float         Time;
			public float         Weight;
		}

		private struct FusionMotionSample
		{
			public AnimationClip Clip;
			public float         Weight;
			public float         TimeScale;
			public bool          Loop;
			public float         ExplicitNormalizedTime;
			public bool          UseSignedSpeedPlayback;
			public float         SignedSpeedScale;
		}

		private struct FusionLayerSnapshot
		{
			public int   CurrentStateIndex;
			public float CurrentStateTime;
			public int   BlendFromStateIndex;
			public float BlendFromStateTime;
			public int   BlendToStateIndex;
			public float BlendDuration;
			public float BlendElapsed;

			public bool HasBlend => BlendFromStateIndex >= 0 && BlendDuration > FUSION_WEIGHT_EPSILON;

			public static FusionLayerSnapshot Empty => new FusionLayerSnapshot
			{
				CurrentStateIndex = -1,
				CurrentStateTime = 0.0f,
				BlendFromStateIndex = -1,
				BlendFromStateTime = 0.0f,
				BlendToStateIndex = -1,
				BlendDuration = 0.0f,
				BlendElapsed = 0.0f,
			};
		}

		private struct FusionSignedSpeedPlayback
		{
			public float LastStateTime;
			public float ClipTime;
			public bool  Initialized;
		}

		private sealed class FusionLayerRuntime
		{
			public int                          LayerIndex;
			public string                       LayerId;
			public string                       LayerName;
			public bool                         EnabledByDefault;
			public float                        DefaultWeight;
			public AvatarMask                   AvatarMask;
			public FusionAnimatorLayerBlendMode BlendMode;
			public FusionAnimatorRuntimeEvaluator Evaluator;

			public AnimationMixerPlayable Mixer;
			public int                    MixerInputIndex;

			public readonly List<FusionAnimatorStateDefinition> States = new List<FusionAnimatorStateDefinition>(16);
			public readonly Dictionary<string, int>             StateIndexById = new Dictionary<string, int>(StringComparer.Ordinal);
			public readonly Dictionary<AnimationClip, int>      InputIndexByClip = new Dictionary<AnimationClip, int>();
			public readonly Dictionary<AnimationClip, AnimationClipPlayable> PlayableByClip = new Dictionary<AnimationClip, AnimationClipPlayable>();

			public readonly List<FusionClipPose> PreviousFixedPoses = new List<FusionClipPose>(16);
			public readonly List<FusionClipPose> CurrentFixedPoses  = new List<FusionClipPose>(16);
			public readonly List<FusionClipPose> RenderPoses        = new List<FusionClipPose>(16);

			public int PreviousTick = int.MinValue;
			public int CurrentTick  = int.MinValue;
		}

		private FusionAnimatorRuntimeGraphInstance _fusionRuntimeGraph;
		private FusionAnimatorParameterStore _fusionParameters;
		private readonly List<FusionLayerRuntime> _fusionLayers = new List<FusionLayerRuntime>(MAX_FUSION_GRAPH_LAYERS);
		private readonly Dictionary<string, FusionAnimatorParameterDefinition> _fusionParameterById = new Dictionary<string, FusionAnimatorParameterDefinition>(StringComparer.Ordinal);
		private readonly Dictionary<string, string> _fusionParameterIdByName = new Dictionary<string, string>(StringComparer.Ordinal);
		private readonly List<FusionMotionSample> _fusionMotionSamplesA = new List<FusionMotionSample>(16);
		private readonly List<FusionMotionSample> _fusionMotionSamplesB = new List<FusionMotionSample>(16);
		private readonly Dictionary<AnimationClip, FusionClipPose> _fusionPoseLookup = new Dictionary<AnimationClip, FusionClipPose>();
		private readonly Dictionary<int, FusionSignedSpeedPlayback> _fusionSignedSpeedPlaybackByKey = new Dictionary<int, FusionSignedSpeedPlayback>(64);
		private readonly FusionLayerSnapshot[] _fusionSnapshots = new FusionLayerSnapshot[MAX_FUSION_GRAPH_LAYERS];

		private int _fusionBaseLayerIndex  = -1;
		private int _fusionUpperLayerIndex = -1;

		private string _fusionInputMoveId;
		private string _fusionInputLookId;
		private string _fusionInputAimId;
		private string _fusionInputShootId;
		private string _fusionInputReloadId;
		private string _fusionInputJumpId;
		private string _fusionInputThrowId;
		private string _fusionStateDeadId;
		private string _fusionStateShootingId;
		private string _fusionStateReloadingId;
		private string _fusionStateGroundedId;
		private string _fusionStateThrowingId;
		private string _fusionStateLookAtId;
		private string _fusionStateJetpackId;
		private string _fusionStateSprintingId;
		private string _fusionStateWeaponId;
		private string _fusionStateEquipTriggerId;

		private float _fusionFixedLookYawDelta;
		private float _fusionRenderLookYawDelta;
		private float _fusionPreviousFixedLookYaw;
		private float _fusionPreviousRenderLookYaw;
		private bool  _fusionHasPreviousFixedLookYaw;
		private bool  _fusionHasPreviousRenderLookYaw;
		private bool  _fusionLocalIsThrowing;
		private bool  _fusionThrowInputHeld;
		private bool  _fusionDead;
		private bool  _fusionPreviousJetpackActive;
		private bool  _fusionWasResimulation;
		private bool  _fusionPreviousThrowingState;
		private bool  _fusionPreviousReloadingState;
		private ThrowableWeapon _fusionPreviousThrowableWeapon;
		private float _fusionPreviousFixedLookPitch;
		private float _fusionPreviousRenderLookPitch;
		private bool  _fusionHasPreviousFixedLookPitch;
		private bool  _fusionHasPreviousRenderLookPitch;
		private float _fusionProxyPreviousLookYaw;
		private float _fusionProxyPreviousLookPitch;
		private bool  _fusionHasProxyPreviousLook;
		private Vector2 _fusionProxyLookDelta;
		private bool  _fusionLoggedLookAtResolveFailure;
		private bool  _fusionLoggedGraphStateFailure;

		private int _fusionLocalShootTriggerUntilTick = int.MinValue;
		private int _fusionLocalReloadTriggerTick     = int.MinValue;
		private int _fusionLocalThrowTriggerTick      = int.MinValue;
		private int _fusionLocalEquipTriggerTick      = int.MinValue;

		private void AwakeFusion()
		{
			_fusionRuntimeGraph = _fusionAnimatorGraph != null ? new FusionAnimatorRuntimeGraphInstance(_fusionAnimatorGraph) : null;
			_fusionParameters = new FusionAnimatorParameterStore();
			if (_fusionAnimatorGraph != null)
			{
				_fusionParameters.SetDefaults(_fusionAnimatorGraph);
			}

			BuildFusionParameterLookup();
			BuildFusionLayerMetadata();
			ResetFusionLocalState();

			if (_kcc != null)
			{
				LocomotionLayer locomotionLayer = FindLayer<LocomotionLayer>();
				if (locomotionLayer != null)
				{
					_kcc.MoveState = locomotionLayer.FindState<MoveState>();
				}
			}
		}

		private void OnSpawnedFusion()
		{
			if (HasStateAuthority == true && Animator != null)
			{
				Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			}

			ClearLegacyGameplayQueue();

			// Networked properties are valid only after Spawned().
			_fusionPreviousJetpackActive = IsJetpackActiveSafe();

			// Legacy layers can still exist on hybrid prefabs; force them inactive in Fusion mode.
			IList<AnimationLayer> layers = Layers;
			for (int i = 0, count = layers != null ? layers.Count : 0; i < count; ++i)
			{
				AnimationLayer legacyLayer = layers[i];
				if (legacyLayer == null)
					continue;

				legacyLayer.DeactivateAllStates(0.0f, true);
				legacyLayer.Deactivate(0.0f);
			}

			CreateFusionLayerPlayables();

			if (_fusionRuntimeGraph != null)
			{
				_fusionRuntimeGraph.Reset();
			}

			for (int i = 0; i < _fusionSnapshots.Length; ++i)
			{
				_fusionSnapshots[i] = FusionLayerSnapshot.Empty;
			}

			InitializeFusionNetworkSnapshots();
		}

		private void OnFixedUpdateFusion()
		{
			if (_fusionLayers.Count == 0 || _fusionParameters == null || _fusionRuntimeGraph == null)
			{
				if (_fusionLoggedGraphStateFailure == false)
				{
					Debug.LogError($"[{nameof(CharacterAnimationController)}] Fusion graph is not initialized correctly (layers={_fusionLayers.Count}, parametersNull={_fusionParameters == null}, runtimeGraphNull={_fusionRuntimeGraph == null}) on {name}.", this);
					_fusionLoggedGraphStateFailure = true;
				}
				return;
			}

			_fusionLoggedGraphStateFailure = false;

			ProcessLegacyGameplayQueue();

			if (_jetpack != null)
			{
				bool jetpackActive = IsJetpackActiveSafe();
				if (jetpackActive == true && _fusionPreviousJetpackActive == false)
				{
					ClearLegacyGameplayQueue();
					_fusionThrowInputHeld = false;
					_fusionLocalIsThrowing = false;
					if (HasStateAuthority == true)
					{
						_fusionNetIsThrowing = false;
					}

					if (_weapons != null)
					{
						_weapons.DisarmCurrentWeapon();
					}
				}
				else if (jetpackActive == false && _fusionPreviousJetpackActive == true)
				{
					SwitchWeaponsFusion();
				}

				_fusionPreviousJetpackActive = jetpackActive;
			}

			SyncFusionParameters(interpolated: false);
			_fusionRuntimeGraph?.Step(Runner != null ? Runner.DeltaTime : 0.02f, _fusionParameters, null, false);
			CaptureFusionSnapshotsFromEvaluators();
			WriteFusionSnapshotsToNetwork();

			BuildFixedFusionPoses();
			ProcessFusionGameplayParity();

			ApplyFixedFusionPoses();

			_fusionFixedLookYawDelta = 0.0f;
			_fusionWasResimulation = Runner != null && Runner.IsResimulation == true;
		}

		private void OnInterpolateFusion()
		{
			if (_fusionLayers.Count == 0 || _fusionParameters == null || _fusionRuntimeGraph == null)
				return;

			SyncFusionParameters(interpolated: true);
			EnsureFusionRenderSnapshots();
			BuildRenderFusionPoses();

			ApplyRenderFusionPoses();

			_fusionRenderLookYawDelta = Mathf.Lerp(_fusionRenderLookYawDelta, 0.0f, 0.5f);
		}

		private void OnEvaluateFusion()
		{
			SnapWeapon();
		}

		private bool CanJumpFusion()
		{
			if (_fusionDead == true)
				return false;
			if (IsJetpackActiveSafe() == true)
				return false;

			string baseStateName = GetCurrentLayerStateName(_fusionBaseLayerIndex);
			if (string.Equals(baseStateName, "Start_Jump", StringComparison.Ordinal) == true)
				return false;
			if (string.Equals(baseStateName, "Loop_Jump", StringComparison.Ordinal) == true)
				return false;
			if (string.Equals(baseStateName, "End_Jump", StringComparison.Ordinal) == true)
				return false;

			return true;
		}

		private bool CanSwitchWeaponsFusion(bool force)
		{
			if (_fusionDead == true)
				return false;
			if (IsJetpackActiveSafe() == true)
				return false;
			if (IsWeaponReloading() == true)
				return false;

			if (force == false && IsFusionWeaponSwitchInProgress() == true)
				return false;

			return true;
		}

		private void SetDeadFusion(bool isDead)
		{
			if (_fusionDead == isDead)
				return;

			_fusionDead = isDead;

			if (isDead == true)
			{
				if (_kcc != null && _kcc.Data.IsGrounded == true)
				{
					_kcc.SetColliderLayer(LayerMask.NameToLayer("Ignore Raycast"));
					_kcc.SetCollisionLayerMask(_kcc.Settings.CollisionLayerMask & ~(1 << LayerMask.NameToLayer("AgentKCC")));
				}

				ClearLegacyGameplayQueue();
				_fusionThrowInputHeld = false;
				_fusionLocalIsThrowing = false;
				if (HasStateAuthority == true)
				{
					_fusionNetIsThrowing = false;
				}
			}
			else if (_kcc != null)
			{
				_kcc.SetShape(EKCCShape.Capsule);
			}
		}

		private bool StartFireFusion()
		{
			if (_fusionDead == true)
				return false;
			if (IsJetpackActiveSafe() == true)
				return false;
			if (IsWeaponReloading() == true)
				return false;
			if (_fusionThrowInputHeld == true || ResolveThrowingState() == true)
				return false;
			if (IsFusionWeaponSwitchInProgress() == true)
				return false;

			if (TryGetUpperStateInfo(out string upperStateName, out float upperNormalizedTime) == true)
			{
				if (upperStateName.IndexOf("Reload", StringComparison.OrdinalIgnoreCase) >= 0)
					return false;

				if (upperStateName.IndexOf("Shoot", StringComparison.OrdinalIgnoreCase) >= 0 && upperNormalizedTime < 0.98f)
					return false;
			}

			int tick = Runner != null ? Runner.Tick.Raw : 0;
			int durationTicks = Mathf.Max(1, Mathf.CeilToInt(SHOOT_TRIGGER_DURATION / Mathf.Max(0.0001f, Runner != null ? Runner.DeltaTime : 0.02f)));

			_fusionLocalShootTriggerUntilTick = tick + durationTicks;
			if (HasStateAuthority == true)
			{
				_fusionShootTriggerUntilTick = _fusionLocalShootTriggerUntilTick;
			}

			return true;
		}

		private void ProcessThrowFusion(bool start, bool hold)
		{
			bool hasThrowable = _weapons != null && (_weapons.PendingWeapon is ThrowableWeapon);
			if (hasThrowable == false)
			{
				_fusionThrowInputHeld = false;
				_fusionLocalIsThrowing = false;
				if (HasStateAuthority == true)
				{
					_fusionNetIsThrowing = false;
				}

				return;
			}

			_fusionThrowInputHeld = hold;

			if (start == true)
			{
				if (_weapons != null && _weapons.CurrentWeapon is ThrowableWeapon throwableWeapon)
				{
					throwableWeapon.ArmProjectile();
				}

				SetShootTriggerFromThrowRelease();
				QueueLegacyFire(UPPER_BODY_GRENDE_THROW_FIRE_TIME);
			}
		}

		private bool StartReloadFusion()
		{
			if (_fusionDead == true)
				return false;

			SetReloadTriggerTick();
			return true;
		}

		private void SwitchWeaponsFusion()
		{
			SetEquipTriggerTick();

			bool pendingThrowable = _weapons != null && _weapons.PendingWeapon is ThrowableWeapon;
			if (pendingThrowable == true)
			{
				SetThrowContextTriggerTick();
				_fusionLocalIsThrowing = true;
				if (HasStateAuthority == true)
				{
					_fusionNetIsThrowing = true;
				}

				_legacyArmPendingTick = int.MinValue;
				_weapons?.ArmPendingWeapon();
				return;
			}

			_fusionLocalIsThrowing = false;
			if (HasStateAuthority == true)
			{
				_fusionNetIsThrowing = false;
			}

			if (_weapons != null && GetPendingWeaponSlotSafe() > 0)
			{
				_weapons.DisarmCurrentWeapon();
				QueueLegacyArmPending(UPPER_BODY_EQUIP_ARM_TIME);
			}
			else
			{
				QueueLegacyDisarm(UPPER_BODY_UNEQUIP_DISARM_TIME);
			}
		}

		private void TurnFusion(float angle)
		{
			if (Mathf.Abs(angle) < LOOK_YAW_INPUT_DEADZONE)
			{
				angle = 0.0f;
			}

			if (Runner != null && Runner.Stage != default)
			{
				_fusionFixedLookYawDelta = angle;
			}
			else
			{
				_fusionRenderLookYawDelta = angle;
			}
		}

		private bool CanSnapHandFusion()
		{
			if (_fusionDead == true || IsJetpackActiveSafe() == true)
				return false;

			if (TryGetUpperStateInfo(out string upperStateName, out float normalizedTime) == false)
				return true;

			if (string.Equals(upperStateName, "Reload", StringComparison.Ordinal) == true)
				return normalizedTime >= 0.85f;

			if (upperStateName.StartsWith("Cycle Weapon/Equip", StringComparison.Ordinal) == true || string.Equals(upperStateName, "Grenade/Equip Grenade", StringComparison.Ordinal) == true)
				return normalizedTime >= 0.75f;

			if (upperStateName.StartsWith("Cycle Weapon/", StringComparison.Ordinal) == true)
				return false;
			if (upperStateName.StartsWith("Grenade/", StringComparison.Ordinal) == true)
				return false;

			return true;
		}

		private void BuildFusionParameterLookup()
		{
			_fusionParameterById.Clear();
			_fusionParameterIdByName.Clear();

			if (_fusionAnimatorGraph == null || _fusionAnimatorGraph.Parameters == null)
				return;

			for (int i = 0, count = _fusionAnimatorGraph.Parameters.Count; i < count; ++i)
			{
				FusionAnimatorParameterDefinition parameter = _fusionAnimatorGraph.Parameters[i];
				if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
					continue;

				if (_fusionParameterById.ContainsKey(parameter.Id) == false)
				{
					_fusionParameterById.Add(parameter.Id, parameter);
				}

				string normalizedName = NormalizeAnimatorName(parameter.Name);
				if (string.IsNullOrWhiteSpace(normalizedName) == false && _fusionParameterIdByName.ContainsKey(normalizedName) == false)
				{
					_fusionParameterIdByName.Add(normalizedName, parameter.Id);
				}
			}

			_fusionInputMoveId       = FindFusionParameterId("Input_Move");
			_fusionInputLookId       = FindFusionParameterId("Input_Look");
			_fusionInputAimId        = FindFusionParameterId("Input_Aim");
			_fusionInputShootId      = FindFusionParameterId("Input_Shoot");
			_fusionInputReloadId     = FindFusionParameterId("Input_Reload");
			_fusionInputJumpId       = FindFusionParameterId("Input_Jump");
			_fusionInputThrowId      = FindFusionParameterId("Input_Throw");
			_fusionStateDeadId       = FindFusionParameterId("State_IsDead");
			_fusionStateShootingId   = FindFusionParameterId("State_IsShooting");
			_fusionStateReloadingId  = FindFusionParameterId("State_IsReloading");
			_fusionStateGroundedId   = FindFusionParameterId("State_IsGrounded");
			_fusionStateThrowingId   = FindFusionParameterId("State_IsThrowing");
			_fusionStateLookAtId     = FindFusionParameterId("State_LookAt");
			_fusionStateJetpackId    = FindFusionParameterId("State_Jetpack");
			_fusionStateSprintingId  = FindFusionParameterId("State_IsSprinting");
			_fusionStateWeaponId     = FindFusionParameterId("State_Weapon");
			_fusionStateEquipTriggerId = FindFusionParameterId("State_EquipTrigger");
		}

		private void BuildFusionLayerMetadata()
		{
			_fusionLayers.Clear();
			_fusionBaseLayerIndex = -1;
			_fusionUpperLayerIndex = -1;

			if (_fusionAnimatorGraph == null || _fusionAnimatorGraph.Layers == null)
				return;

			List<FusionAnimatorLayerDefinition> orderedLayers = new List<FusionAnimatorLayerDefinition>(_fusionAnimatorGraph.Layers.Count);
			for (int i = 0, count = _fusionAnimatorGraph.Layers.Count; i < count; ++i)
			{
				FusionAnimatorLayerDefinition layer = _fusionAnimatorGraph.Layers[i];
				if (layer != null && string.IsNullOrWhiteSpace(layer.Id) == false)
				{
					orderedLayers.Add(layer);
				}
			}

			orderedLayers.Sort((a, b) =>
			{
				int byPriority = a.Priority.CompareTo(b.Priority);
				if (byPriority != 0)
					return byPriority;

				return string.CompareOrdinal(a.Id, b.Id);
			});

			for (int i = 0, count = Mathf.Min(MAX_FUSION_GRAPH_LAYERS, orderedLayers.Count); i < count; ++i)
			{
				FusionAnimatorLayerDefinition layer = orderedLayers[i];
				FusionLayerRuntime runtime = new FusionLayerRuntime
				{
					LayerIndex = i,
					LayerId = layer.Id,
					LayerName = layer.Name ?? string.Empty,
					EnabledByDefault = layer.EnabledByDefault,
					DefaultWeight = layer.DefaultWeight,
					AvatarMask = layer.AvatarMask,
					BlendMode = layer.BlendMode,
					Evaluator = _fusionRuntimeGraph != null ? _fusionRuntimeGraph.GetLayerEvaluator(layer.Id) : null,
					MixerInputIndex = -1,
				};

				if (_fusionAnimatorGraph.States != null)
				{
					for (int stateIndex = 0, stateCount = _fusionAnimatorGraph.States.Count; stateIndex < stateCount; ++stateIndex)
					{
						FusionAnimatorStateDefinition state = _fusionAnimatorGraph.States[stateIndex];
						if (state == null || string.IsNullOrWhiteSpace(state.Id) == true)
							continue;
						if (string.Equals(state.LayerId, layer.Id, StringComparison.Ordinal) == false)
							continue;
						if (IsScopeSentinelState(state) == true)
							continue;

						int runtimeStateIndex = runtime.States.Count;
						runtime.States.Add(state);
						runtime.StateIndexById[state.Id] = runtimeStateIndex;
					}
				}

				_fusionLayers.Add(runtime);

				if (string.Equals(runtime.LayerName, "Base", StringComparison.Ordinal))
				{
					_fusionBaseLayerIndex = runtime.LayerIndex;
				}
				else if (string.Equals(runtime.LayerName, "Upper", StringComparison.Ordinal))
				{
					_fusionUpperLayerIndex = runtime.LayerIndex;
				}
			}
		}

		private void CreateFusionLayerPlayables()
		{
			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				FusionLayerRuntime layer = _fusionLayers[i];
				layer.Mixer = AnimationMixerPlayable.Create(Graph, 0);
				layer.MixerInputIndex = Mixer.AddInput(layer.Mixer, 0, layer.EnabledByDefault == true ? layer.DefaultWeight : 0.0f);

				if (layer.AvatarMask != null)
				{
					Mixer.SetLayerMaskFromAvatarMask((uint)layer.MixerInputIndex, layer.AvatarMask);
				}

				Mixer.SetLayerAdditive((uint)layer.MixerInputIndex, layer.BlendMode == FusionAnimatorLayerBlendMode.Additive);
			}
		}

		private void ResetFusionLocalState()
		{
			_fusionFixedLookYawDelta = 0.0f;
			_fusionRenderLookYawDelta = 0.0f;
			_fusionPreviousFixedLookYaw = 0.0f;
			_fusionPreviousRenderLookYaw = 0.0f;
			_fusionHasPreviousFixedLookYaw = false;
			_fusionHasPreviousRenderLookYaw = false;
			_fusionLocalIsThrowing = false;
			_fusionThrowInputHeld = false;
			_fusionDead = false;
			_fusionPreviousJetpackActive = false;
			_fusionWasResimulation = false;
			_fusionPreviousThrowingState = false;
			_fusionPreviousReloadingState = false;
			_fusionPreviousThrowableWeapon = null;
			_fusionPreviousFixedLookPitch = 0.0f;
			_fusionPreviousRenderLookPitch = 0.0f;
			_fusionHasPreviousFixedLookPitch = false;
			_fusionHasPreviousRenderLookPitch = false;
			_fusionProxyPreviousLookYaw = 0.0f;
			_fusionProxyPreviousLookPitch = 0.0f;
			_fusionHasProxyPreviousLook = false;
			_fusionProxyLookDelta = Vector2.zero;
			_fusionLoggedLookAtResolveFailure = false;

			_fusionLocalShootTriggerUntilTick = int.MinValue;
			_fusionLocalReloadTriggerTick = int.MinValue;
			_fusionLocalThrowTriggerTick = int.MinValue;
			_fusionLocalEquipTriggerTick = int.MinValue;
			_fusionSignedSpeedPlaybackByKey.Clear();
		}

		private void InitializeFusionNetworkSnapshots()
		{
			_fusionShootTriggerUntilTick = int.MinValue;
			_fusionReloadTriggerTick = int.MinValue;
			_fusionThrowTriggerTick = int.MinValue;
			_fusionEquipTriggerTick = int.MinValue;
			_fusionNetIsThrowing = false;

			for (int i = 0; i < MAX_FUSION_GRAPH_LAYERS; ++i)
			{
				_fusionNetCurrentStateIndices[i] = -1;
				_fusionNetCurrentStateTimes[i] = 0.0f;
				_fusionNetBlendFromStateIndices[i] = -1;
				_fusionNetBlendFromStateTimes[i] = 0.0f;
				_fusionNetBlendToStateIndices[i] = -1;
				_fusionNetBlendDurations[i] = 0.0f;
				_fusionNetBlendElapsed[i] = 0.0f;
			}
		}

		private void CaptureFusionSnapshotsFromEvaluators()
		{
			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				FusionLayerRuntime layer = _fusionLayers[i];
				FusionLayerSnapshot snapshot = FusionLayerSnapshot.Empty;

				if (layer.Evaluator != null)
				{
					snapshot.CurrentStateIndex = GetLayerStateIndex(layer, layer.Evaluator.CurrentStateId);
					snapshot.CurrentStateTime = Mathf.Max(0.0f, layer.Evaluator.CurrentStateTime);
					snapshot.BlendFromStateIndex = GetLayerStateIndex(layer, layer.Evaluator.BlendFromStateId);
					snapshot.BlendFromStateTime = Mathf.Max(0.0f, layer.Evaluator.BlendFromStateTime);
					snapshot.BlendToStateIndex = GetLayerStateIndex(layer, layer.Evaluator.BlendToStateId);
					snapshot.BlendDuration = Mathf.Max(0.0f, layer.Evaluator.BlendDurationSeconds);
					snapshot.BlendElapsed = Mathf.Max(0.0f, layer.Evaluator.BlendElapsedSeconds);
				}

				_fusionSnapshots[layer.LayerIndex] = snapshot;
			}
		}

		private void WriteFusionSnapshotsToNetwork()
		{
			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				FusionLayerSnapshot snapshot = _fusionSnapshots[i];
				_fusionNetCurrentStateIndices[i] = snapshot.CurrentStateIndex;
				_fusionNetCurrentStateTimes[i] = snapshot.CurrentStateTime;
				_fusionNetBlendFromStateIndices[i] = snapshot.BlendFromStateIndex;
				_fusionNetBlendFromStateTimes[i] = snapshot.BlendFromStateTime;
				_fusionNetBlendToStateIndices[i] = snapshot.BlendToStateIndex;
				_fusionNetBlendDurations[i] = snapshot.BlendDuration;
				_fusionNetBlendElapsed[i] = snapshot.BlendElapsed;
			}
		}

		private void ReadFusionSnapshotsFromNetwork()
		{
			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				_fusionSnapshots[i] = new FusionLayerSnapshot
				{
					CurrentStateIndex = _fusionNetCurrentStateIndices[i],
					CurrentStateTime = _fusionNetCurrentStateTimes[i],
					BlendFromStateIndex = _fusionNetBlendFromStateIndices[i],
					BlendFromStateTime = _fusionNetBlendFromStateTimes[i],
					BlendToStateIndex = _fusionNetBlendToStateIndices[i],
					BlendDuration = _fusionNetBlendDurations[i],
					BlendElapsed = _fusionNetBlendElapsed[i],
				};
			}
		}

		private void RestoreFusionEvaluatorsFromNetworkSnapshots()
		{
			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				FusionLayerRuntime layer = _fusionLayers[i];
				if (layer.Evaluator == null)
					continue;

				int currentStateIndex = _fusionNetCurrentStateIndices[i];
				if (currentStateIndex >= 0 && currentStateIndex < layer.States.Count)
				{
					FusionAnimatorStateDefinition state = layer.States[currentStateIndex];
					layer.Evaluator.Reset(state != null ? state.Id : string.Empty, Mathf.Max(0.0f, _fusionNetCurrentStateTimes[i]));
				}
				else
				{
					layer.Evaluator.Reset(string.Empty, 0.0f);
				}
			}
		}

		private void BuildFixedFusionPoses()
		{
			int currentTick = Runner != null ? Runner.Tick.Raw : 0;

			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				FusionLayerRuntime layer = _fusionLayers[i];

				CopyClipPoses(layer.CurrentFixedPoses, layer.PreviousFixedPoses);
				layer.PreviousTick = layer.CurrentTick;
				layer.CurrentTick = currentTick;

				layer.CurrentFixedPoses.Clear();
				ResolveLayerSnapshotPoses(layer, _fusionSnapshots[i], layer.CurrentFixedPoses);
			}
		}

		private void BuildRenderFusionPoses()
		{
			float alpha = Runner != null ? Runner.LocalAlpha : 1.0f;
			alpha = Mathf.Clamp01(alpha);

			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				FusionLayerRuntime layer = _fusionLayers[i];
				layer.RenderPoses.Clear();

				if (layer.PreviousTick == int.MinValue || layer.PreviousFixedPoses.Count == 0)
				{
					CopyClipPoses(layer.CurrentFixedPoses, layer.RenderPoses);
					continue;
				}

				BlendClipPoses(layer.PreviousFixedPoses, layer.CurrentFixedPoses, alpha, layer.RenderPoses);
			}
		}

		private void ApplyFixedFusionPoses()
		{
			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				ApplyLayerPoses(_fusionLayers[i], _fusionLayers[i].CurrentFixedPoses);
			}
		}

		private void ApplyRenderFusionPoses()
		{
			for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
			{
				ApplyLayerPoses(_fusionLayers[i], _fusionLayers[i].RenderPoses);
			}
		}

		private void ApplyLayerPoses(FusionLayerRuntime layer, List<FusionClipPose> poses)
		{
			int inputCount = layer.Mixer.GetInputCount();
			for (int i = 0; i < inputCount; ++i)
			{
				layer.Mixer.SetInputWeight(i, 0.0f);
			}

			for (int i = 0, count = poses.Count; i < count; ++i)
			{
				FusionClipPose pose = poses[i];
				if (pose.Clip == null || pose.Weight <= FUSION_WEIGHT_EPSILON)
					continue;

				int inputIndex = EnsureLayerClipPlayable(layer, pose.Clip);
				AnimationClipPlayable clipPlayable = layer.PlayableByClip[pose.Clip];
				clipPlayable.SetTime(pose.Time);
				layer.Mixer.SetInputWeight(inputIndex, pose.Weight);
			}

			float layerWeight = layer.EnabledByDefault == true ? layer.DefaultWeight : 0.0f;
			Mixer.SetInputWeight(layer.MixerInputIndex, layerWeight);
		}

		private int EnsureLayerClipPlayable(FusionLayerRuntime layer, AnimationClip clip)
		{
			if (layer.InputIndexByClip.TryGetValue(clip, out int inputIndex))
				return inputIndex;

			AnimationClipPlayable clipPlayable = AnimationClipPlayable.Create(Graph, clip);
			clipPlayable.SetApplyFootIK(false);
			clipPlayable.SetApplyPlayableIK(false);
			clipPlayable.SetSpeed(0.0f);

			inputIndex = layer.Mixer.AddInput(clipPlayable, 0, 0.0f);
			layer.InputIndexByClip.Add(clip, inputIndex);
			layer.PlayableByClip.Add(clip, clipPlayable);

			return inputIndex;
		}

		private void ResolveLayerSnapshotPoses(FusionLayerRuntime layer, FusionLayerSnapshot snapshot, List<FusionClipPose> poses)
		{
			poses.Clear();

			if (snapshot.HasBlend == true)
			{
				float blendAlpha = snapshot.BlendDuration > FUSION_WEIGHT_EPSILON ? Mathf.Clamp01(snapshot.BlendElapsed / snapshot.BlendDuration) : 1.0f;

				FusionAnimatorStateDefinition fromState = GetLayerStateByIndex(layer, snapshot.BlendFromStateIndex);
				if (fromState != null)
				{
					AppendStatePoses(fromState, snapshot.BlendFromStateTime, 1.0f - blendAlpha, poses);
				}

				FusionAnimatorStateDefinition toState = GetLayerStateByIndex(layer, snapshot.BlendToStateIndex);
				if (toState != null)
				{
					AppendStatePoses(toState, snapshot.CurrentStateTime, blendAlpha, poses);
				}
			}
			else
			{
				FusionAnimatorStateDefinition currentState = GetLayerStateByIndex(layer, snapshot.CurrentStateIndex);
				if (currentState != null)
				{
					AppendStatePoses(currentState, snapshot.CurrentStateTime, 1.0f, poses);
				}
			}

			NormalizePoseWeights(poses);
		}

		private void AppendStatePoses(FusionAnimatorStateDefinition state, float stateTime, float stateWeight, List<FusionClipPose> poses)
		{
			if (state == null || stateWeight <= FUSION_WEIGHT_EPSILON)
				return;

			_fusionMotionSamplesA.Clear();
			if (TryResolveStateMotionSamples(state, _fusionMotionSamplesA) == false)
				return;

			for (int i = 0, count = _fusionMotionSamplesA.Count; i < count; ++i)
			{
				FusionMotionSample sample = _fusionMotionSamplesA[i];
				if (sample.Clip == null || sample.Weight <= FUSION_WEIGHT_EPSILON)
					continue;

				float clipLength = Mathf.Max(FUSION_MIN_CLIP_LENGTH, sample.Clip.length);
				float clipTime;
				if (sample.UseSignedSpeedPlayback == true)
				{
					clipTime = ResolveSignedSpeedClipTime(state, stateTime, sample.TimeScale, sample.SignedSpeedScale, clipLength, sample.Loop);
				}
				else
				{
					clipTime = stateTime * Mathf.Max(FUSION_MIN_CLIP_LENGTH, sample.TimeScale);
				}

				if (sample.ExplicitNormalizedTime >= 0.0f)
				{
					clipTime = Mathf.Clamp01(sample.ExplicitNormalizedTime) * clipLength;
				}
				else
				{
					clipTime = sample.Loop == true ? Mathf.Repeat(clipTime, clipLength) : Mathf.Clamp(clipTime, 0.0f, clipLength);
				}

				AddOrAccumulatePose(poses, sample.Clip, clipTime, sample.Weight * stateWeight);
			}
		}

		private float ResolveSignedSpeedClipTime(FusionAnimatorStateDefinition state, float stateTime, float baseTimeScale, float signedSpeedScale, float clipLength, bool loop)
		{
			int key = ComposeSignedSpeedPlaybackKey(state);
			float safeBaseTimeScale = Mathf.Max(FUSION_MIN_CLIP_LENGTH, baseTimeScale);
			float safeSpeedScale = Mathf.Max(0.0f, Mathf.Abs(signedSpeedScale));

			if (_fusionSignedSpeedPlaybackByKey.TryGetValue(key, out FusionSignedSpeedPlayback playback) == false || playback.Initialized == false)
			{
				float seededTime = stateTime * safeBaseTimeScale * safeSpeedScale;
				playback.LastStateTime = stateTime;
				playback.ClipTime = loop == true ? Mathf.Repeat(seededTime, clipLength) : Mathf.Clamp(seededTime, 0.0f, clipLength);
				playback.Initialized = true;
				_fusionSignedSpeedPlaybackByKey[key] = playback;
				return playback.ClipTime;
			}

			if (Runner != null && Runner.IsResimulation == true)
			{
				return playback.ClipTime;
			}

			float stateDelta = stateTime - playback.LastStateTime;
			if (stateDelta < -0.0001f)
			{
				float reseededTime = stateTime * safeBaseTimeScale * safeSpeedScale;
				playback.ClipTime = loop == true ? Mathf.Repeat(reseededTime, clipLength) : Mathf.Clamp(reseededTime, 0.0f, clipLength);
			}
			else if (stateDelta > 0.0f)
			{
				playback.ClipTime += stateDelta * safeBaseTimeScale * safeSpeedScale;
				playback.ClipTime = loop == true ? Mathf.Repeat(playback.ClipTime, clipLength) : Mathf.Clamp(playback.ClipTime, 0.0f, clipLength);
			}

			playback.LastStateTime = stateTime;
			playback.Initialized = true;
			_fusionSignedSpeedPlaybackByKey[key] = playback;
			return playback.ClipTime;
		}

		private static int ComposeSignedSpeedPlaybackKey(FusionAnimatorStateDefinition state)
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + (state != null && string.IsNullOrWhiteSpace(state.Id) == false ? state.Id.GetHashCode() : 0);
				return hash;
			}
		}

		private void SyncFusionParameters(bool interpolated)
		{
			KCCData kccData = _kcc != null ? (interpolated == true ? _kcc.RenderData : _kcc.FixedData) : default;

			Vector2 move = ResolveMoveInput(kccData);
			Vector2 look = ResolveLookInput(kccData, interpolated);

			Vector2 lookAt = ResolveLookAtInput(interpolated);
			bool deadState = ResolveDeadStateFromHealth();

			SetFusionVector2(_fusionInputMoveId, move);
			SetFusionVector2(_fusionInputLookId, look);
			SetFusionLookAtParameter(lookAt);
			SetFusionBool(_fusionInputAimId, kccData.Aim);
			SetFusionBool(_fusionStateDeadId, deadState);
			SetFusionBool(_fusionStateGroundedId, kccData.IsGrounded);
			SetFusionBool(_fusionStateJetpackId, IsJetpackActiveSafe());
			bool hasMoveInput = kccData.InputDirection.OnlyXZ().IsAlmostZero(0.05f) == false;
			bool throwingState = ResolveThrowingState();
			bool reloadingState = IsWeaponReloading();
			bool autoReloadPending = IsWeaponAutoReloadPending();
			ThrowableWeapon currentThrowableWeapon = ResolveCurrentThrowableWeapon();
			SetFusionBool(_fusionStateSprintingId, kccData.IsGrounded == true && kccData.Aim == false && hasMoveInput == true);
			SetFusionBool(_fusionStateShootingId, IsWeaponFiring());
			SetFusionBool(_fusionStateReloadingId, reloadingState);
			SetFusionBool(_fusionStateThrowingId, throwingState);
			SetFusionInt(_fusionStateWeaponId, ResolveWeaponParameterValue());

			bool canPulseTriggers = interpolated == false;
			int currentTick = Runner != null ? Runner.Tick.Raw : 0;
			bool throwContextStarted = throwingState == true && _fusionPreviousThrowingState == false;
			bool reloadStarted = reloadingState == true && _fusionPreviousReloadingState == false;
			bool reloadAnimationActive = IsFusionReloadAnimationActive();
			bool throwableWeaponChanged =
				throwingState == true &&
				_fusionPreviousThrowableWeapon != null &&
				currentThrowableWeapon != null &&
				ReferenceEquals(_fusionPreviousThrowableWeapon, currentThrowableWeapon) == false;

			int shootUntilTick = Mathf.Max(_fusionLocalShootTriggerUntilTick, _fusionShootTriggerUntilTick);
			bool shootPulse = canPulseTriggers == true && shootUntilTick >= 0 && currentTick <= shootUntilTick;
			bool reloadPulse = canPulseTriggers == true && (IsTriggerPulseActive(currentTick, _fusionLocalReloadTriggerTick, _fusionReloadTriggerTick) || reloadStarted == true || ((reloadingState == true || autoReloadPending == true) && reloadAnimationActive == false));
			bool throwPulse = canPulseTriggers == true && (IsTriggerPulseActive(currentTick, _fusionLocalThrowTriggerTick, _fusionThrowTriggerTick) || throwContextStarted == true || throwableWeaponChanged == true);
			bool equipPulse = canPulseTriggers == true && IsTriggerPulseActive(currentTick, _fusionLocalEquipTriggerTick, _fusionEquipTriggerTick);
			bool jumpPulse = canPulseTriggers == true && kccData.HasJumped;

			if (reloadingState == true || autoReloadPending == true)
			{
				shootPulse = false;
			}

			SetFusionTrigger(_fusionInputShootId, shootPulse);
			SetFusionTrigger(_fusionInputReloadId, reloadPulse);
			SetFusionTrigger(_fusionInputThrowId, throwPulse);
			SetFusionTrigger(_fusionStateEquipTriggerId, equipPulse);
			SetFusionTrigger(_fusionInputJumpId, jumpPulse);

			if (canPulseTriggers == true)
			{
				_fusionPreviousThrowingState = throwingState;
				_fusionPreviousReloadingState = reloadingState;

				if (currentThrowableWeapon != null)
				{
					_fusionPreviousThrowableWeapon = currentThrowableWeapon;
				}
				else if (_fusionDead == true || IsJetpackActiveSafe() == true)
				{
					_fusionPreviousThrowableWeapon = null;
				}
			}
		}

		private Vector2 ResolveMoveInput(KCCData kccData)
		{
			Transform referenceTransform = _kcc != null ? _kcc.transform : transform;
			Vector3 sourceDirection = kccData.InputDirection.OnlyXZ();

			if (sourceDirection.IsAlmostZero(0.025f) == true)
			{
				if (HasInputAuthority == false && HasStateAuthority == false)
				{
					sourceDirection = kccData.RealVelocity.OnlyXZ();
				}
				else
				{
					sourceDirection = kccData.KinematicDirection.OnlyXZ();
				}
			}

			if (sourceDirection.IsAlmostZero(0.025f) == true)
				return Vector2.zero;

			Vector3 local = referenceTransform.InverseTransformDirection(sourceDirection).XZ0();
			if (_agent != null && _agent.LeftSide == true)
			{
				local.x = -local.x;
			}

			return new Vector2(Mathf.Clamp(local.x, -1.0f, 1.0f), Mathf.Clamp(local.y, -1.0f, 1.0f));
		}

		private Vector2 ResolveLookInput(KCCData kccData, bool interpolated)
		{
			if (HasLocalFusionInputAuthority() == false)
			{
				float yawDelta = ResolveLookYawDelta(kccData, interpolated);
				float pitchDelta = ResolveLookPitchDelta(kccData, interpolated);

				float proxyYaw = Mathf.Clamp(yawDelta / LOOK_YAW_NORMALIZATION, -1.0f, 1.0f);
				float proxyPitch = Mathf.Clamp(pitchDelta / LOOK_PITCH_NORMALIZATION, -1.0f, 1.0f);

				if (Mathf.Abs(proxyYaw) < 0.01f)
				{
					proxyYaw = 0.0f;
				}

				if (Mathf.Abs(proxyPitch) < 0.01f)
				{
					proxyPitch = 0.0f;
				}

				return new Vector2(proxyYaw, proxyPitch);
			}

			Vector2 lookRotationDelta = ResolveLookRotationDeltaInput(kccData, interpolated);
			float normalizedYaw = Mathf.Clamp(lookRotationDelta.y, -1.0f, 1.0f);
			float normalizedPitch = Mathf.Clamp(lookRotationDelta.x, -1.0f, 1.0f);

			if (Mathf.Abs(normalizedYaw) < 0.01f)
			{
				normalizedYaw = 0.0f;
			}

			if (Mathf.Abs(normalizedPitch) < 0.01f)
			{
				normalizedPitch = 0.0f;
			}

			return new Vector2(normalizedYaw, normalizedPitch);
		}

		private bool TryResolveLookYawFromTargetPosition(bool interpolated, out float normalizedYaw)
		{
			normalizedYaw = 0.0f;

			Aiming aiming = _agent != null ? _agent.Aiming : null;
			if (aiming == null)
				return false;

			bool resolveRenderHistory = ShouldResolveLookRenderHistory(interpolated);
			if (aiming.TryGetTargetPosition(resolveRenderHistory, out Vector3 lookOrigin, out Vector3 targetPosition) == false)
				return false;

			Transform referenceTransform = _kcc != null ? _kcc.transform : transform;
			Vector3 worldDirection = targetPosition - lookOrigin;
			worldDirection.y = 0.0f;
			if (worldDirection.sqrMagnitude <= 0.0001f)
				return false;

			Vector3 localDirection = referenceTransform.InverseTransformDirection(worldDirection.normalized);
			if (_agent != null && _agent.LeftSide == true)
			{
				localDirection.x = -localDirection.x;
			}

			float yawAngle = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
			normalizedYaw = Mathf.Clamp(yawAngle / LOOK_YAW_NORMALIZATION, -1.0f, 1.0f);

			if (Mathf.Abs(normalizedYaw) < 0.01f)
			{
				normalizedYaw = 0.0f;
			}

			return true;
		}

		private Vector2 ResolveLookAtInput(bool interpolated)
		{
			Aiming aiming = _agent != null ? _agent.Aiming : null;
			if (aiming == null)
				return Vector2.zero;

			bool hasLocalInputAuthority = HasLocalFusionInputAuthority() == true;
			bool resolveRenderHistory = ShouldResolveLookRenderHistory(interpolated);
			Transform referenceTransform = _kcc != null ? _kcc.transform : transform;

			if (aiming.TryGetCrosshairAndHitPoints(resolveRenderHistory, out Vector3 fireOrigin, out _, out Vector3 fireHitPoint, out _) == false)
			{
				if (_fusionLoggedLookAtResolveFailure == false && ShouldLogLookAtResolveFailure(aiming, resolveRenderHistory) == true)
				{
					Debug.LogWarning($"[{nameof(CharacterAnimationController)}] Failed to resolve crosshair hit points for State_LookAt (hasLocalInputAuthority={hasLocalInputAuthority}, resolveRenderHistory={resolveRenderHistory}).", this);
					_fusionLoggedLookAtResolveFailure = true;
				}

				return Vector2.zero;
			}

			_fusionLoggedLookAtResolveFailure = false;

			bool invalidOrigin = float.IsNaN(fireOrigin.x) || float.IsNaN(fireOrigin.y) || float.IsNaN(fireOrigin.z);
			if (invalidOrigin == true)
			{
				if (_fusionLoggedLookAtResolveFailure == false)
				{
					Debug.LogWarning($"[{nameof(CharacterAnimationController)}] Invalid fire origin for State_LookAt (hasLocalInputAuthority={hasLocalInputAuthority}, resolveRenderHistory={resolveRenderHistory}).", this);
					_fusionLoggedLookAtResolveFailure = true;
				}

				return Vector2.zero;
			}

			bool invalidHitPoint = float.IsNaN(fireHitPoint.x) || float.IsNaN(fireHitPoint.y) || float.IsNaN(fireHitPoint.z);
			if (invalidHitPoint == true)
				return Vector2.zero;

			Vector3 worldDirection = fireHitPoint - fireOrigin;
			if (worldDirection.sqrMagnitude <= 0.0001f)
				return Vector2.zero;

			Vector3 localDirection = referenceTransform.InverseTransformDirection(worldDirection.normalized);
			if (_agent != null && _agent.LeftSide == true)
			{
				localDirection.x = -localDirection.x;
			}

			Vector3 planarDirection = new Vector3(localDirection.x, 0.0f, localDirection.z);
			float planarMagnitude = planarDirection.magnitude;
			float yawAngle = Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg;
			float pitchAngle = Mathf.Atan2(localDirection.y, Mathf.Max(0.0001f, planarMagnitude)) * Mathf.Rad2Deg;

			float normalizedYaw = Mathf.Clamp(yawAngle / LOOK_YAW_NORMALIZATION, -1.0f, 1.0f);
			float normalizedPitch = Mathf.Clamp(pitchAngle / LOOK_PITCH_NORMALIZATION, -1.0f, 1.0f);
			return new Vector2(normalizedYaw, normalizedPitch);
		}

		private bool ShouldLogLookAtResolveFailure(Aiming aiming, bool resolveRenderHistory)
		{
			if (aiming == null)
				return false;
			if (Object == null || Runner == null || Object.IsValid == false || Object.IsInSimulation == false)
				return false;
			if (_fusionDead == true)
				return false;

			// Suppress startup/warmup noise until deterministic look sources are available.
			if (aiming.HasDeterministicLookAtSource(resolveRenderHistory) == false)
				return false;

			return true;
		}

		private bool HasLocalFusionInputAuthority()
		{
			if (_agent == null || _agent.HasInputAuthority == false)
				return false;

			SceneContext context = _agent.Context;
			return context != null && context.HasInput == true;
		}

		private bool IsLocalFusionRenderOwner()
		{
			return Agent.IsLocalObservedInputOwner(_agent);
		}

		private bool ShouldResolveLookRenderHistory(bool interpolated)
		{
			if (interpolated == false)
				return true;

			if (HasLocalFusionInputAuthority() == false)
				return true;

			return IsLocalFusionRenderOwner() == false;
		}

		private void EnsureFusionRenderSnapshots()
		{
			bool shouldForceRenderStep = Object != null && Object.IsInSimulation == false;
			if (shouldForceRenderStep == false)
			{
				for (int i = 0, count = _fusionLayers.Count; i < count; ++i)
				{
					if (_fusionLayers[i].CurrentFixedPoses.Count > 0)
						return;
				}

				// Spawn frame / late-join safety: if no fixed poses exist yet, seed once from render.
				shouldForceRenderStep = true;
			}

			if (shouldForceRenderStep == false)
				return;

			float deltaTime = Time.deltaTime;
			if (float.IsNaN(deltaTime) == true || deltaTime < 0.0f)
			{
				deltaTime = 0.0f;
			}

			_fusionRuntimeGraph.Step(deltaTime, _fusionParameters, null, false);
			CaptureFusionSnapshotsFromEvaluators();
			BuildFixedFusionPoses();
		}

		private float ResolveLookYawDelta(KCCData kccData, bool interpolated)
		{
			float currentYaw = kccData.LookYaw;
			float previousYaw;
			bool hasPreviousYaw;

			if (interpolated == true)
			{
				previousYaw = _fusionPreviousRenderLookYaw;
				hasPreviousYaw = _fusionHasPreviousRenderLookYaw;
				_fusionPreviousRenderLookYaw = currentYaw;
				_fusionHasPreviousRenderLookYaw = true;
			}
			else
			{
				previousYaw = _fusionPreviousFixedLookYaw;
				hasPreviousYaw = _fusionHasPreviousFixedLookYaw;
				_fusionPreviousFixedLookYaw = currentYaw;
				_fusionHasPreviousFixedLookYaw = true;
			}

			if (hasPreviousYaw == false)
				return 0.0f;

			float delta = Mathf.DeltaAngle(previousYaw, currentYaw);
			if (Mathf.Abs(delta) < LOOK_YAW_INPUT_DEADZONE)
				return 0.0f;

			return delta;
		}

		private float ResolveLookPitchDelta(KCCData kccData, bool interpolated)
		{
			float currentPitch = kccData.LookPitch;
			float previousPitch;
			bool hasPreviousPitch;

			if (interpolated == true)
			{
				previousPitch = _fusionPreviousRenderLookPitch;
				hasPreviousPitch = _fusionHasPreviousRenderLookPitch;
				_fusionPreviousRenderLookPitch = currentPitch;
				_fusionHasPreviousRenderLookPitch = true;
			}
			else
			{
				previousPitch = _fusionPreviousFixedLookPitch;
				hasPreviousPitch = _fusionHasPreviousFixedLookPitch;
				_fusionPreviousFixedLookPitch = currentPitch;
				_fusionHasPreviousFixedLookPitch = true;
			}

			if (hasPreviousPitch == false)
				return 0.0f;

			float delta = currentPitch - previousPitch;
			if (Mathf.Abs(delta) < LOOK_YAW_INPUT_DEADZONE)
				return 0.0f;

			return delta;
		}

		private Vector2 ResolveLookRotationDeltaInput(KCCData kccData, bool interpolated)
		{
			if (_agent == null || _agent.AgentInput == null)
				return Vector2.zero;
			if (HasLocalFusionInputAuthority() == false)
				return Vector2.zero;

			if (interpolated == true && IsLocalFusionRenderOwner() == true)
				return _agent.AgentInput.RenderInput.LookRotationDelta;

			return _agent.AgentInput.FixedInput.LookRotationDelta;
		}

		private ThrowableWeapon ResolveCurrentThrowableWeapon()
		{
			if (_weapons == null)
				return null;

			if (_weapons.CurrentWeapon is ThrowableWeapon currentThrowableWeapon)
				return currentThrowableWeapon;

			return _weapons.PendingWeapon as ThrowableWeapon;
		}

		private void SetFusionLookAtParameter(Vector2 lookAt)
		{
			if (string.IsNullOrWhiteSpace(_fusionStateLookAtId))
				return;

			if (_fusionParameterById.TryGetValue(_fusionStateLookAtId, out FusionAnimatorParameterDefinition parameter) == false || parameter == null)
				return;

			if (parameter.Type == FusionAnimatorParameterType.Vector2)
			{
				SetFusionVector2(_fusionStateLookAtId, lookAt);
				return;
			}

			SetFusionBool(_fusionStateLookAtId, lookAt.sqrMagnitude > 0.0001f);
		}

		private bool ResolveThrowingState()
		{
			if (_fusionDead == true || IsJetpackActiveSafe() == true)
				return false;

			if (_weapons != null)
			{
				return (_weapons.PendingWeapon is ThrowableWeapon) || (_weapons.CurrentWeapon is ThrowableWeapon);
			}

			if (HasStateAuthority == true)
				return _fusionLocalIsThrowing;
			if (HasInputAuthority == true)
				return _fusionLocalIsThrowing;

			return _fusionNetIsThrowing;
		}

		private bool ResolveDeadStateFromHealth()
		{
			if (_agent != null && _agent.Health != null)
			{
				try
				{
					return _agent.Health.IsAlive == false;
				}
				catch (InvalidOperationException)
				{
					// Networked value not readable yet; fall back to local controller state.
				}
			}

			return _fusionDead;
		}

		private bool IsFusionWeaponSwitchInProgress()
		{
			return _legacyArmPendingTick != int.MinValue || _legacyDisarmTick != int.MinValue;
		}

		private int ResolveWeaponParameterValue()
		{
			if (_weapons == null)
				return 0;

			if (IsFusionWeaponSwitchInProgress() == true)
			{
				int pendingSlot = GetPendingWeaponSlotSafe();
				if (pendingSlot > 0)
					return NormalizeWeaponSlot(pendingSlot);
			}

			if (ResolveThrowingState() == true)
			{
				int pendingSlot = GetPendingWeaponSlotSafe();
				if (pendingSlot > 0)
					return NormalizeWeaponSlot(pendingSlot);
			}

			return NormalizeWeaponSlot(GetCurrentWeaponSlotSafe());
		}

		private void ProcessFusionGameplayParity()
		{
			if (_weapons != null && (HasStateAuthority == true || HasInputAuthority == true) && _fusionDead == false && IsJetpackActiveSafe() == false)
			{
				bool throwableContextMissing = (_weapons.PendingWeapon is ThrowableWeapon) == false && (_weapons.CurrentWeapon is ThrowableWeapon) == false;
				if (throwableContextMissing == true && _fusionPreviousThrowableWeapon != null && _fusionPreviousThrowableWeapon.HasAmmo() == false)
				{
					int nextGrenadeSlot = _weapons.GetNextWeaponSlot(3, 4, true);
					if (nextGrenadeSlot >= 4 && _weapons.SwitchWeapon(nextGrenadeSlot) == true)
					{
						SwitchWeaponsFusion();
					}
				}
			}

			bool hasThrowableContext = _weapons != null && ((_weapons.PendingWeapon is ThrowableWeapon) || (_weapons.CurrentWeapon is ThrowableWeapon));
			bool isThrowing = hasThrowableContext == true && _fusionDead == false && IsJetpackActiveSafe() == false;
			_fusionLocalIsThrowing = isThrowing;

			if (isThrowing == false)
			{
				_fusionThrowInputHeld = false;
				_fusionLocalThrowTriggerTick = int.MinValue;
				if (HasStateAuthority == true)
				{
					_fusionThrowTriggerTick = int.MinValue;
				}
			}

			if (HasStateAuthority == true)
			{
				_fusionNetIsThrowing = _fusionLocalIsThrowing;
			}
		}

		private bool IsFusionReloadAnimationActive()
		{
			if (TryGetUpperStateInfo(out string upperStateName, out _) == false)
				return false;

			return upperStateName.IndexOf("Reload", StringComparison.OrdinalIgnoreCase) >= 0;
		}

		private bool TryGetUpperStateInfo(out string stateName, out float normalizedTime)
		{
			stateName = string.Empty;
			normalizedTime = 0.0f;

			if (_fusionUpperLayerIndex < 0 || _fusionUpperLayerIndex >= _fusionLayers.Count)
				return false;

			FusionLayerRuntime layer = _fusionLayers[_fusionUpperLayerIndex];
			FusionLayerSnapshot snapshot = _fusionSnapshots[_fusionUpperLayerIndex];
			FusionAnimatorStateDefinition state = GetLayerStateByIndex(layer, snapshot.CurrentStateIndex);
			if (state == null)
				return false;

			stateName = state.Name ?? string.Empty;
			float referenceLength = ResolveStateReferenceLengthSeconds(state);
			if (referenceLength > FUSION_WEIGHT_EPSILON)
			{
				normalizedTime = snapshot.CurrentStateTime / referenceLength;
			}
			else
			{
				normalizedTime = 0.0f;
			}

			return true;
		}

		private string GetCurrentLayerStateName(int layerIndex)
		{
			if (layerIndex < 0 || layerIndex >= _fusionLayers.Count)
				return string.Empty;

			FusionLayerRuntime layer = _fusionLayers[layerIndex];
			FusionLayerSnapshot snapshot = _fusionSnapshots[layerIndex];
			FusionAnimatorStateDefinition state = GetLayerStateByIndex(layer, snapshot.CurrentStateIndex);
			return state != null ? (state.Name ?? string.Empty) : string.Empty;
		}

		private float ResolveStateReferenceLengthSeconds(FusionAnimatorStateDefinition state)
		{
			if (state == null)
				return 0.0f;

			float maxLength = 0.0f;
			if (state.MotionType == FusionAnimatorMotionType.BlendTree && state.BlendTree != null && state.BlendTree.Children != null)
			{
				for (int i = 0, count = state.BlendTree.Children.Count; i < count; ++i)
				{
					FusionAnimatorBlendTreeChild child = state.BlendTree.Children[i];
					AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, child, EvaluateBindingCondition, ResolveBindingClipIndexParameter);
					if (clip == null)
						continue;

					maxLength = Mathf.Max(maxLength, Mathf.Max(0.0f, clip.length));
				}
			}
			else if (state.Clips != null)
			{
				for (int i = 0, count = state.Clips.Count; i < count; ++i)
				{
					FusionAnimatorClipSlot slot = state.Clips[i];
					AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, slot, EvaluateBindingCondition, ResolveBindingClipIndexParameter);
					if (clip == null)
						continue;

					maxLength = Mathf.Max(maxLength, Mathf.Max(0.0f, clip.length));
				}
			}

			return maxLength;
		}

		private static void CopyClipPoses(List<FusionClipPose> source, List<FusionClipPose> target)
		{
			target.Clear();
			for (int i = 0, count = source.Count; i < count; ++i)
			{
				target.Add(source[i]);
			}
		}

		private void BlendClipPoses(List<FusionClipPose> fromPoses, List<FusionClipPose> toPoses, float alpha, List<FusionClipPose> target)
		{
			target.Clear();
			_fusionPoseLookup.Clear();

			for (int i = 0, count = fromPoses.Count; i < count; ++i)
			{
				FusionClipPose pose = fromPoses[i];
				if (pose.Clip != null)
				{
					_fusionPoseLookup[pose.Clip] = pose;
				}
			}

			for (int i = 0, count = toPoses.Count; i < count; ++i)
			{
				FusionClipPose toPose = toPoses[i];
				if (toPose.Clip == null)
					continue;

				if (_fusionPoseLookup.TryGetValue(toPose.Clip, out FusionClipPose fromPose))
				{
					float clipLength = Mathf.Max(FUSION_MIN_CLIP_LENGTH, toPose.Clip.length);
					target.Add(new FusionClipPose
					{
						Clip = toPose.Clip,
						Time = InterpolateClipTime(fromPose.Time, toPose.Time, alpha, clipLength),
						Weight = Mathf.Lerp(fromPose.Weight, toPose.Weight, alpha),
					});

					_fusionPoseLookup.Remove(toPose.Clip);
				}
				else
				{
					target.Add(new FusionClipPose
					{
						Clip = toPose.Clip,
						Time = toPose.Time,
						Weight = toPose.Weight * alpha,
					});
				}
			}

			foreach (KeyValuePair<AnimationClip, FusionClipPose> remaining in _fusionPoseLookup)
			{
				FusionClipPose fromPose = remaining.Value;
				target.Add(new FusionClipPose
				{
					Clip = fromPose.Clip,
					Time = fromPose.Time,
					Weight = fromPose.Weight * (1.0f - alpha),
				});
			}

			NormalizePoseWeights(target);
		}

		private static float InterpolateClipTime(float fromTime, float toTime, float alpha, float clipLength)
		{
			float delta = toTime - fromTime;
			float halfLength = clipLength * 0.5f;
			if (Mathf.Abs(delta) > halfLength)
			{
				if (delta > 0.0f)
				{
					fromTime += clipLength;
				}
				else
				{
					toTime += clipLength;
				}

				float wrapped = Mathf.LerpUnclamped(fromTime, toTime, alpha);
				if (wrapped >= clipLength)
				{
					wrapped -= clipLength;
				}

				return wrapped;
			}

			return Mathf.LerpUnclamped(fromTime, toTime, alpha);
		}

		private static void NormalizePoseWeights(List<FusionClipPose> poses)
		{
			float totalWeight = 0.0f;
			for (int i = 0, count = poses.Count; i < count; ++i)
			{
				FusionClipPose pose = poses[i];
				pose.Weight = Mathf.Max(0.0f, pose.Weight);
				poses[i] = pose;
				totalWeight += pose.Weight;
			}

			if (totalWeight <= FUSION_WEIGHT_EPSILON)
				return;

			float invTotalWeight = 1.0f / totalWeight;
			for (int i = 0, count = poses.Count; i < count; ++i)
			{
				FusionClipPose pose = poses[i];
				pose.Weight *= invTotalWeight;
				poses[i] = pose;
			}
		}

		private static void AddOrAccumulatePose(List<FusionClipPose> poses, AnimationClip clip, float time, float weight)
		{
			for (int i = 0, count = poses.Count; i < count; ++i)
			{
				FusionClipPose pose = poses[i];
				if (pose.Clip != clip)
					continue;

				pose.Time = time;
				pose.Weight += weight;
				poses[i] = pose;
				return;
			}

			poses.Add(new FusionClipPose
			{
				Clip = clip,
				Time = time,
				Weight = weight,
			});
		}

		private FusionAnimatorStateDefinition GetLayerStateByIndex(FusionLayerRuntime layer, int stateIndex)
		{
			if (layer == null || stateIndex < 0 || stateIndex >= layer.States.Count)
				return null;

			return layer.States[stateIndex];
		}

		private int GetLayerStateIndex(FusionLayerRuntime layer, string stateId)
		{
			if (layer == null || string.IsNullOrWhiteSpace(stateId))
				return -1;

			return layer.StateIndexById.TryGetValue(stateId, out int stateIndex) ? stateIndex : -1;
		}

		private string FindFusionParameterId(string expectedName)
		{
			string normalized = NormalizeAnimatorName(expectedName);
			if (string.IsNullOrWhiteSpace(normalized))
				return string.Empty;

			return _fusionParameterIdByName.TryGetValue(normalized, out string parameterId) ? parameterId : string.Empty;
		}

		private void SetShootTriggerFromThrowRelease()
		{
			int tick = Runner != null ? Runner.Tick.Raw : 0;
			int durationTicks = Mathf.Max(1, Mathf.CeilToInt(SHOOT_TRIGGER_DURATION / Mathf.Max(0.0001f, Runner != null ? Runner.DeltaTime : 0.02f)));
			_fusionLocalShootTriggerUntilTick = tick + durationTicks;
			if (HasStateAuthority == true)
			{
				_fusionShootTriggerUntilTick = _fusionLocalShootTriggerUntilTick;
			}
		}

		private void SetReloadTriggerTick()
		{
			int untilTick = ResolveTriggerPulseUntilTick();
			_fusionLocalReloadTriggerTick = untilTick;
			if (HasStateAuthority == true)
			{
				_fusionReloadTriggerTick = untilTick;
			}
		}

		private void SetThrowTriggerTick()
		{
			int untilTick = ResolveTriggerPulseUntilTick();
			_fusionLocalThrowTriggerTick = untilTick;
			if (HasStateAuthority == true)
			{
				_fusionThrowTriggerTick = untilTick;
			}
		}

		private void SetThrowContextTriggerTick()
		{
			int untilTick = GetCurrentTick() + SecondsToTicks(Mathf.Max(UPPER_BODY_GRENDE_EQUIP_TIME, TRIGGER_PULSE_DURATION));
			_fusionLocalThrowTriggerTick = untilTick;
			if (HasStateAuthority == true)
			{
				_fusionThrowTriggerTick = untilTick;
			}
		}

		private void SetEquipTriggerTick()
		{
			int untilTick = ResolveTriggerPulseUntilTick();
			_fusionLocalEquipTriggerTick = untilTick;
			if (HasStateAuthority == true)
			{
				_fusionEquipTriggerTick = untilTick;
			}
		}

		private static bool IsTriggerPulseActive(int currentTick, int localUntilTick, int networkUntilTick)
		{
			int untilTick = Mathf.Max(localUntilTick, networkUntilTick);
			return untilTick >= 0 && currentTick <= untilTick;
		}

		private int ResolveTriggerPulseUntilTick()
		{
			int tick = Runner != null ? Runner.Tick.Raw : 0;
			float deltaTime = Runner != null ? Runner.DeltaTime : 0.02f;
			int durationTicks = Mathf.Max(1, Mathf.CeilToInt(TRIGGER_PULSE_DURATION / Mathf.Max(0.0001f, deltaTime)));
			return tick + durationTicks;
		}

		private bool IsWeaponFiring()
		{
			if (_weapons == null || (_weapons.CurrentWeapon is FirearmWeapon firearmWeapon) == false)
				return false;

			try
			{
				return firearmWeapon.IsFiring == true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		private bool IsWeaponReloading()
		{
			if (_weapons == null || (_weapons.CurrentWeapon is FirearmWeapon firearmWeapon) == false)
				return false;

			try
			{
				return firearmWeapon.IsReloading == true;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		private bool IsWeaponAutoReloadPending()
		{
			if (_weapons == null || (_weapons.CurrentWeapon is FirearmWeapon firearmWeapon) == false)
				return false;

			try
			{
				return firearmWeapon.IsReloading == false && firearmWeapon.MagazineAmmo <= 0 && firearmWeapon.WeaponAmmo > 0;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		private bool IsJetpackActiveSafe()
		{
			if (_jetpack == null)
				return false;

			try
			{
				return _jetpack.IsActive;
			}
			catch (InvalidOperationException)
			{
				return false;
			}
		}

		private int GetCurrentWeaponSlotSafe()
		{
			if (_weapons == null)
				return 0;

			try
			{
				return _weapons.CurrentWeaponSlot;
			}
			catch (InvalidOperationException)
			{
				return 0;
			}
		}

		private int GetPendingWeaponSlotSafe()
		{
			if (_weapons == null)
				return 0;

			try
			{
				return _weapons.PendingWeaponSlot;
			}
			catch (InvalidOperationException)
			{
				return 0;
			}
		}

		private static int NormalizeWeaponSlot(int slot)
		{
			if (slot > 2)
				return 1;
			if (slot < 0)
				return 0;

			return slot;
		}

		private void SetFusionBool(string parameterId, bool value)
		{
			if (string.IsNullOrWhiteSpace(parameterId) == false)
			{
				_fusionParameters.SetBool(parameterId, value);
			}
		}

		private void SetFusionInt(string parameterId, int value)
		{
			if (string.IsNullOrWhiteSpace(parameterId) == false)
			{
				_fusionParameters.SetInt(parameterId, value);
			}
		}

		private void SetFusionVector2(string parameterId, Vector2 value)
		{
			if (string.IsNullOrWhiteSpace(parameterId) == false)
			{
				_fusionParameters.SetVector2(parameterId, value);
			}
		}

		private void SetFusionTrigger(string parameterId, bool triggerNow)
		{
			if (string.IsNullOrWhiteSpace(parameterId))
				return;

			if (triggerNow == false)
			{
				_fusionParameters.SetBool(parameterId, false);
				return;
			}

			// Force edge each tick the trigger should be visible by the runtime evaluator.
			_fusionParameters.SetBool(parameterId, false);
			_fusionParameters.SetBool(parameterId, true);
		}

		private static bool IsScopeSentinelState(FusionAnimatorStateDefinition state)
		{
			if (state == null || string.IsNullOrWhiteSpace(state.Name))
				return false;

			int slash = state.Name.LastIndexOf('/');
			string leaf = slash >= 0 ? state.Name.Substring(slash + 1) : state.Name;
			return string.Equals(leaf, FusionAnimatorGraphAsset.ScopeSentinelStateLeafName, StringComparison.Ordinal);
		}

		private static string NormalizeAnimatorName(string name)
		{
			if (string.IsNullOrWhiteSpace(name))
				return string.Empty;

			char[] chars = name.ToCharArray();
			int length = 0;
			for (int i = 0; i < chars.Length; ++i)
			{
				char c = chars[i];
				if (char.IsLetterOrDigit(c) == false)
					continue;

				chars[length++] = char.ToLowerInvariant(c);
			}

			return length > 0 ? new string(chars, 0, length) : string.Empty;
		}

		private bool TryResolveStateMotionSamples(FusionAnimatorStateDefinition state, List<FusionMotionSample> samples)
		{
			samples.Clear();
			if (state == null)
				return false;

			if (state.MotionType == FusionAnimatorMotionType.BlendTree && state.BlendTree != null)
			{
				ResolveBlendTreeSamples(state.BlendTree, samples);
				return samples.Count > 0;
			}

			if (state.Clips == null || state.Clips.Count == 0)
				return false;

			int validCount = 0;
			for (int i = 0, count = state.Clips.Count; i < count; ++i)
			{
				FusionAnimatorClipSlot slot = state.Clips[i];
				AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, slot, EvaluateBindingCondition, ResolveBindingClipIndexParameter);
				if (slot == null || clip == null)
					continue;

				++validCount;

				samples.Add(new FusionMotionSample
				{
					Clip = clip,
					Weight = 1.0f,
					TimeScale = Mathf.Max(FUSION_MIN_CLIP_LENGTH, FusionAnimatorClipBindingUtility.ResolveSpeed(_fusionAnimatorGraph, slot, EvaluateBindingCondition, ResolveBindingClipIndexParameter)),
					Loop = FusionAnimatorClipBindingUtility.ResolveLoop(_fusionAnimatorGraph, slot, EvaluateBindingCondition, ResolveBindingClipIndexParameter),
					ExplicitNormalizedTime = -1.0f,
				});
			}

			if (validCount <= 0)
				return false;

			float inv = 1.0f / validCount;
			for (int i = 0, count = samples.Count; i < count; ++i)
			{
				FusionMotionSample sample = samples[i];
				sample.Weight *= inv;
				samples[i] = sample;
			}

			return samples.Count > 0;
		}

		private void ResolveBlendTreeSamples(FusionAnimatorBlendTreeDefinition blendTree, List<FusionMotionSample> samples)
		{
			samples.Clear();
			if (blendTree == null || blendTree.Children == null || blendTree.Children.Count == 0)
				return;

			List<FusionAnimatorBlendTreeChild> validChildren = new List<FusionAnimatorBlendTreeChild>(blendTree.Children.Count);
			List<AnimationClip> validChildClips = new List<AnimationClip>(blendTree.Children.Count);

			for (int i = 0, count = blendTree.Children.Count; i < count; ++i)
			{
				FusionAnimatorBlendTreeChild child = blendTree.Children[i];
				AnimationClip childClip = FusionAnimatorClipBindingUtility.ResolveClip(_fusionAnimatorGraph, child, EvaluateBindingCondition, ResolveBindingClipIndexParameter);
				if (child == null || childClip == null)
					continue;

				validChildren.Add(child);
				validChildClips.Add(childClip);
			}

			if (validChildren.Count == 0)
				return;

			float[] weights = new float[validChildren.Count];
			float explicitPoseTime01 = -1.0f;

			float oneDSignedSpeedScale = 1.0f;
			switch (blendTree.Type)
			{
				case FusionAnimatorBlendTreeType.OneD:
					ResolveOneDWeights(blendTree, validChildren, weights);
					break;
				case FusionAnimatorBlendTreeType.OneDSignedSpeed:
					ResolveOneDSignedSpeedWeights(blendTree, validChildren, weights, out oneDSignedSpeedScale);
					break;
				case FusionAnimatorBlendTreeType.TwoDFreeformCartesian:
					ResolveTwoDFreeformCartesianWeights(blendTree, validChildren, weights);
					break;
				case FusionAnimatorBlendTreeType.DirectionalPoseTime2D:
					ResolveDirectionalPoseTimeWeights(blendTree, validChildren, weights);
					explicitPoseTime01 = ResolveDirectionalPoseTimeNormalized(blendTree, validChildren);
					break;
				case FusionAnimatorBlendTreeType.TwoDSimpleDirectional:
					ResolveTwoDSimpleDirectionalWeights(blendTree, validChildren, weights);
					break;
				case FusionAnimatorBlendTreeType.TwoDFreeformDirectional:
					ResolveTwoDFreeformDirectionalWeights(blendTree, validChildren, weights);
					break;
				case FusionAnimatorBlendTreeType.Direct:
					ResolveDirectWeights(blendTree, validChildren, weights);
					break;
				default:
					weights[0] = 1.0f;
					break;
			}

			float totalWeight = 0.0f;
			for (int i = 0; i < weights.Length; ++i)
			{
				weights[i] = Mathf.Max(0.0f, weights[i]);
				totalWeight += weights[i];
			}

			if (totalWeight <= FUSION_WEIGHT_EPSILON)
			{
				weights[0] = 1.0f;
				totalWeight = 1.0f;
			}

			float invTotalWeight = 1.0f / totalWeight;
			for (int i = 0; i < validChildren.Count; ++i)
			{
				bool useSignedSpeedPlayback = blendTree.Type == FusionAnimatorBlendTreeType.OneDSignedSpeed;
				float timeScale = Mathf.Max(FUSION_MIN_CLIP_LENGTH, validChildren[i].TimeScale);
				if (useSignedSpeedPlayback == false)
				{
					timeScale = Mathf.Max(FUSION_MIN_CLIP_LENGTH, timeScale * oneDSignedSpeedScale);
				}

				samples.Add(new FusionMotionSample
				{
					Clip = validChildClips[i],
					Weight = weights[i] * invTotalWeight,
					TimeScale = timeScale,
					Loop = validChildClips[i].isLooping,
					ExplicitNormalizedTime = explicitPoseTime01,
					UseSignedSpeedPlayback = useSignedSpeedPlayback,
					SignedSpeedScale = oneDSignedSpeedScale,
				});
			}
		}

		private void ResolveOneDWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			float x = GetParameterFloat(blendTree.ParameterXId);
			List<int> order = new List<int>(children.Count);
			for (int i = 0, count = children.Count; i < count; ++i)
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
					continue;

				float span = Mathf.Max(0.0001f, rightX - leftX);
				float t = Mathf.Clamp01((x - leftX) / span);
				weights[leftIndex] = 1.0f - t;
				weights[rightIndex] = t;
				return;
			}

			weights[firstIndex] = 1.0f;
		}

		private void ResolveOneDSignedSpeedWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights, out float speedScale)
		{
			float x = GetParameterFloat(blendTree.ParameterXId);
			speedScale = Mathf.Abs(x);

			int selectedIndex = -1;
			float bestDistance = float.MaxValue;
			bool positiveSide = x > 0.0f;
			bool negativeSide = x < 0.0f;

			float GetChildX(int childIndex)
			{
				FusionAnimatorBlendTreeChild child = children[childIndex];
				return Mathf.Abs(child.Position.x) > 0.0001f || Mathf.Abs(child.Position.y) > 0.0001f ? child.Position.x : child.Threshold;
			}

			for (int i = 0, count = children.Count; i < count; ++i)
			{
				float childX = GetChildX(i);
				if (positiveSide == true && childX < 0.0f)
					continue;
				if (negativeSide == true && childX > 0.0f)
					continue;

				float distance = Mathf.Abs(childX - x);
				if (distance < bestDistance)
				{
					bestDistance = distance;
					selectedIndex = i;
				}
			}

			if (selectedIndex < 0)
			{
				for (int i = 0, count = children.Count; i < count; ++i)
				{
					float distance = Mathf.Abs(GetChildX(i) - x);
					if (distance < bestDistance)
					{
						bestDistance = distance;
						selectedIndex = i;
					}
				}
			}

			if (selectedIndex < 0 && children.Count > 0)
			{
				selectedIndex = 0;
			}

			if (selectedIndex >= 0)
			{
				weights[selectedIndex] = 1.0f;
			}
		}

		private void ResolveTwoDFreeformCartesianWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			Vector2 input = GetTwoDBlendTreeInputValue(blendTree);
			const float epsilon = 0.0001f;

			int exactIndex = -1;
			float bestDistance = float.MaxValue;

			for (int i = 0, count = children.Count; i < count; ++i)
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
			List<int> nearestIndices = new List<int>(nearestCount);
			List<float> nearestDistances = new List<float>(nearestCount);
			for (int i = 0, count = children.Count; i < count; ++i)
			{
				float distance = Vector2.Distance(input, children[i].Position);

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
			for (int i = 0, count = nearestIndices.Count; i < count; ++i)
			{
				int childIndex = nearestIndices[i];
				float d = nearestDistances[i];
				float w = 1.0f / Mathf.Max(epsilon * epsilon, d * d);
				weights[childIndex] = w;
				total += w;
			}

			if (total <= epsilon)
			{
				weights[exactIndex >= 0 ? exactIndex : 0] = 1.0f;
				return;
			}

			float invTotal = 1.0f / total;
			for (int i = 0, count = nearestIndices.Count; i < count; ++i)
			{
				int childIndex = nearestIndices[i];
				weights[childIndex] *= invTotal;
			}
		}

		private void ResolveDirectionalPoseTimeWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			Vector2 input = GetTwoDBlendTreeInputValue(blendTree);
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

		private float ResolveDirectionalPoseTimeNormalized(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children)
		{
			if (blendTree == null)
				return 0.0f;

			float rawPoseTime;
			if (string.IsNullOrWhiteSpace(blendTree.PoseTimeParameterId))
			{
				rawPoseTime = GetTwoDBlendTreeInputValue(blendTree).magnitude;
				float defaultRange = ResolveDirectionalPoseTimeInputRange(children);
				rawPoseTime /= defaultRange;
			}
			else
			{
				rawPoseTime = GetParameterFloat(blendTree.PoseTimeParameterId);
			}

			return EvaluatePoseTime01(rawPoseTime, blendTree.InputOffsetX, blendTree.InputPowerX);
		}

		private void ResolveTwoDSimpleDirectionalWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			Vector2 input = GetTwoDBlendTreeInputValue(blendTree);
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
			Vector2 input = GetTwoDBlendTreeInputValue(blendTree);
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

			List<float> laneAngles = new List<float>(lanes.Count);
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
					float inv = 1.0f / accumulatedDirectional;
					for (int i = 0; i < directionalIndices.Count; ++i)
					{
						int childIndex = directionalIndices[i];
						weights[childIndex] *= inv * directionalFactor;
					}
				}

				weights[centerIndex] += 1.0f - directionalFactor;
			}
		}

		private void ResolveDirectWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
		{
			float total = 0.0f;
			for (int i = 0, count = children.Count; i < count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				string parameterId = string.IsNullOrWhiteSpace(child.DirectParameterId) == true ? blendTree.DirectBlendParameterId : child.DirectParameterId;
				float value = Mathf.Max(0.0f, GetParameterFloat(parameterId));
				weights[i] = value;
				total += value;
			}

			if (total <= FUSION_WEIGHT_EPSILON && children.Count > 0)
			{
				weights[0] = 1.0f;
			}
		}

		private Vector2 GetTwoDBlendTreeInputValue(FusionAnimatorBlendTreeDefinition blendTree)
		{
			if (blendTree == null)
				return Vector2.zero;

			if (TryGetVector2ParameterValue(blendTree.ParameterVector2Id, out Vector2 explicitVector2Input) == true)
				return explicitVector2Input;

			bool hasVectorX = TryGetVector2ParameterValue(blendTree.ParameterXId, out Vector2 vectorXInput);
			bool hasVectorY = TryGetVector2ParameterValue(blendTree.ParameterYId, out Vector2 vectorYInput);
			if (hasVectorX == true && hasVectorY == true)
			{
				if (string.Equals(blendTree.ParameterXId, blendTree.ParameterYId, StringComparison.Ordinal))
					return vectorXInput;

				return new Vector2(vectorXInput.x, vectorYInput.y);
			}

			if (hasVectorX == true)
				return vectorXInput;

			if (hasVectorY == true)
				return vectorYInput;

			return new Vector2(GetParameterFloat(blendTree.ParameterXId), GetParameterFloat(blendTree.ParameterYId));
		}

		private bool TryGetVector2ParameterValue(string parameterReference, out Vector2 value)
		{
			value = Vector2.zero;
			if (string.IsNullOrWhiteSpace(parameterReference))
				return false;

			if (FusionAnimatorParameterReferenceUtility.TryParse(parameterReference, out string parameterId, out FusionAnimatorParameterComponent component) == false || component != FusionAnimatorParameterComponent.None)
				return false;

			if (_fusionParameterById.TryGetValue(parameterId, out FusionAnimatorParameterDefinition parameter) == false || parameter == null || parameter.Type != FusionAnimatorParameterType.Vector2)
				return false;

			if (_fusionParameters.TryGetVector2(parameterId, out value) == false)
			{
				value = parameter.DefaultVector2;
			}

			return true;
		}

		private float GetParameterFloat(string parameterReference)
		{
			if (string.IsNullOrWhiteSpace(parameterReference))
				return 0.0f;

			if (FusionAnimatorParameterReferenceUtility.TryParse(parameterReference, out string parameterId, out FusionAnimatorParameterComponent component) == false)
				return 0.0f;

			if (_fusionParameterById.TryGetValue(parameterId, out FusionAnimatorParameterDefinition parameter) == false || parameter == null)
				return 0.0f;

			switch (parameter.Type)
			{
				case FusionAnimatorParameterType.Bool:
				case FusionAnimatorParameterType.Trigger:
				{
					bool value = parameter.DefaultBool;
					_fusionParameters.TryGetBool(parameter.Id, out value);
					return value == true ? 1.0f : 0.0f;
				}
				case FusionAnimatorParameterType.Int:
				{
					int value = parameter.DefaultInt;
					_fusionParameters.TryGetInt(parameter.Id, out value);
					return value;
				}
				case FusionAnimatorParameterType.Float:
				{
					float value = parameter.DefaultFloat;
					_fusionParameters.TryGetFloat(parameter.Id, out value);
					return value;
				}
				case FusionAnimatorParameterType.Vector2:
				{
					Vector2 value = parameter.DefaultVector2;
					_fusionParameters.TryGetVector2(parameter.Id, out value);

					switch (component)
					{
						case FusionAnimatorParameterComponent.X: return value.x;
						case FusionAnimatorParameterComponent.Y: return value.y;
						default: return value.magnitude;
					}
				}
				default:
					return 0.0f;
			}
		}

		private bool EvaluateBindingCondition(FusionAnimatorConditionDefinition condition)
		{
			if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
				return false;

			if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string parameterId, out FusionAnimatorParameterComponent component) == false)
				return false;

			if (_fusionParameterById.TryGetValue(parameterId, out FusionAnimatorParameterDefinition parameter) == false || parameter == null)
				return false;

			return FusionAnimatorRuntimeEvaluator.EvaluateCondition(condition, parameter, _fusionParameters, false, component);
		}

		private int? ResolveBindingClipIndexParameter(string parameterReference)
		{
			if (string.IsNullOrWhiteSpace(parameterReference))
				return null;

			if (FusionAnimatorParameterReferenceUtility.TryParse(parameterReference, out string parameterId, out FusionAnimatorParameterComponent component) == false || component != FusionAnimatorParameterComponent.None)
				return null;

			if (_fusionParameterById.TryGetValue(parameterId, out FusionAnimatorParameterDefinition parameter) == false || parameter == null || parameter.Type != FusionAnimatorParameterType.Int)
				return null;

			if (_fusionParameters.TryGetInt(parameterId, out int sampled) == true)
				return sampled;

			return parameter.DefaultInt;
		}

		private static bool TryFindCenterChild(List<FusionAnimatorBlendTreeChild> children, out int centerIndex)
		{
			centerIndex = -1;
			if (children == null)
				return false;

			const float epsilon = 0.0001f;
			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				if (child == null)
					continue;

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
				return false;

			const float epsilon = 0.0001f;
			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				if (child == null)
					continue;

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
				return false;

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

		private static List<List<int>> BuildDirectionalLanes(List<FusionAnimatorBlendTreeChild> children, List<int> directionalIndices, List<float> directionalAnglesDegrees)
		{
			List<List<int>> lanes = new List<List<int>>();
			List<float> laneAngles = new List<float>();
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
				return;

			if (directionalIndices.Count == 1)
			{
				weights[directionalIndices[0]] = 1.0f;
				return;
			}

			ResolveDirectionalNeighborLanes(inputDirection, directionalAnglesDegrees, out int leftSlot, out int rightSlot, out float t);
			weights[directionalIndices[leftSlot]] += 1.0f - t;
			weights[directionalIndices[rightSlot]] += t;
		}

		private static void ResolveDirectionalNeighborLanes(Vector2 inputDirection, List<float> sortedAnglesDegrees, out int leftSlot, out int rightSlot, out float t)
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
			if (laneChildren == null || laneChildren.Count == 0 || laneWeight <= FUSION_WEIGHT_EPSILON)
				return;

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
					continue;

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
				return 1.0f;

			float maxThresholdMagnitude = 0.0f;
			float maxPositionMagnitude = 0.0f;
			for (int i = 0; i < children.Count; ++i)
			{
				FusionAnimatorBlendTreeChild child = children[i];
				if (child == null)
					continue;

				maxThresholdMagnitude = Mathf.Max(maxThresholdMagnitude, Mathf.Abs(child.Threshold));
				maxPositionMagnitude = Mathf.Max(maxPositionMagnitude, child.Position.magnitude);
			}

			if (maxThresholdMagnitude > 0.0001f)
				return maxThresholdMagnitude;
			if (maxPositionMagnitude > 0.0001f)
				return maxPositionMagnitude;

			return 1.0f;
		}
	}
}
