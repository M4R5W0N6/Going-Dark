namespace TPSBR
{
	using UnityEngine;
	using FusionAnimator;
	using Fusion.Addons.KCC;
	using Fusion.Addons.AnimationController;

	[DefaultExecutionOrder(3)]
	public sealed partial class CharacterAnimationController : AnimationController
	{
		private const float UPPER_BODY_EQUIP_ARM_TIME       = 0.4f;
		private const float UPPER_BODY_UNEQUIP_DISARM_TIME  = 0.5f;
		private const float UPPER_BODY_UNEQUIP_SWITCH_TIME  = 1.0f;
		private const float UPPER_BODY_THROW_START_TIME     = 0.2f;
		private const float UPPER_BODY_GRENDE_EQUIP_TIME    = 0.5f;
		private const float UPPER_BODY_GRENDE_THROW_FIRE_TIME = 0.45f;
		private const float UPPER_BODY_RELOAD_EXIT_TIME     = 0.9f;
		private const float UPPER_BODY_RELOAD_RETURN_TIME   = 0.05f;
		private const float SHOOT_TRIGGER_DURATION          = 0.05f;

		// PRIVATE MEMBERS

		[SerializeField]
		private FusionAnimatorGraphAsset _fusionAnimatorGraph;
		[SerializeField]
		private bool _useFusionGraphMode;
		[SerializeField]
		private Transform       _leftHand;
		[SerializeField]
		private Transform       _leftLowerArm;
		[SerializeField]
		private Transform       _leftUpperArm;
		[SerializeField][Range(0.0f, 1.0f)]
		private float           _aimSnapPower = 0.5f;

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
		private bool            _autoFusionGraphMode;
		private int             _legacyArmPendingTick = int.MinValue;
		private int             _legacyDisarmTick     = int.MinValue;
		private int             _legacyFireTick       = int.MinValue;

		private bool UseFusionGraphMode => (_useFusionGraphMode == true || _autoFusionGraphMode == true) && _fusionAnimatorGraph != null;

		// PUBLIC METHODS

		public bool CanJump()
		{
			if (UseFusionGraphMode == true)
				return CanJumpFusion();

			if (_fullBody.IsActive() == true)
			{
				if (_fullBody.Jump.IsActive(true) == true)
					return false;
				if (_fullBody.Fall.IsActive(true) == true)
					return false;
				if (_fullBody.Dead.IsActive(true) == true)
					return false;
				if (_fullBody.Jetpack.IsActive(true) == true)
					return false;
			}

			return true;
		}

		public bool CanSwitchWeapons(bool force)
		{
			if (UseFusionGraphMode == true)
				return CanSwitchWeaponsFusion(force);

			if (_fullBody.IsActive() == true)
			{
				if (_fullBody.Dead.IsActive() == true)
					return false;
				if (_fullBody.Jetpack.IsActive() == true)
					return false;
			}

			if (_upperBody.IsActive() == true)
			{
				if (_upperBody.Grenade.IsActive() == true && _upperBody.Grenade.CanSwitchWeapon() == false)
					return false;
				if (force == false && (_upperBody.Equip.IsActive() == true || _upperBody.Unequip.IsActive() == true))
					return false;
			}

			return true;
		}

		public void SetDead(bool isDead)
		{
			if (UseFusionGraphMode == true)
			{
				SetDeadFusion(isDead);
				return;
			}

			bool currentlyDead = _fullBody != null && _fullBody.Dead.IsActive();
			if (currentlyDead == isDead)
				return;

			if (isDead == true)
			{
				_fullBody.Dead.Activate(0.2f);

				if (_kcc.Data.IsGrounded == true)
				{
					_kcc.SetColliderLayer(LayerMask.NameToLayer("Ignore Raycast"));
					_kcc.SetCollisionLayerMask(_kcc.Settings.CollisionLayerMask & ~(1 << LayerMask.NameToLayer("AgentKCC")));
				}

				_upperBody.DeactivateAllStates(0.2f, true);
				_look.DeactivateAllStates(0.2f, true);
			}
			else
			{
				_fullBody.Dead.Deactivate(0.2f);
				_kcc.SetShape(EKCCShape.Capsule);
			}
		}

		public bool StartFire()
		{
			if (UseFusionGraphMode == true)
				return StartFireFusion();

			if (_fullBody.Dead.IsActive() == true)
					return false;
			if (_upperBody.HasActiveState() == true)
				return false;

			_shoot.Shoot.SetAnimationTime(0.0f);
			_shoot.Shoot.Activate(0.2f);
			return true;
		}

		public void ProcessThrow(bool start, bool hold)
		{
			if (UseFusionGraphMode == true)
			{
				ProcessThrowFusion(start, hold);
				return;
			}

			_upperBody.Grenade.ProcessThrow(start, hold);
			if (_upperBody.Grenade.ConsumeThrowStarted() == true)
			{
				QueueLegacyFire(UPPER_BODY_GRENDE_THROW_FIRE_TIME);
			}
		}

		public bool StartReload()
		{
			if (UseFusionGraphMode == true)
				return StartReloadFusion();

			if (_upperBody.Grenade.IsActive() == true)
				return _upperBody.Grenade.ProcessReload();

			if (_fullBody.Dead.IsActive() == true)
				return false;
			if (_upperBody.Reload.IsActive() == true)
				return true;
			if (_upperBody.HasActiveState() == true)
				return false;

			_upperBody.Reload.Activate(0.2f);
			return true;
		}

		public void SwitchWeapons()
		{
			if (UseFusionGraphMode == true)
			{
				SwitchWeaponsFusion();
				return;
			}

			_upperBody.Reload.Deactivate(0.2f);

			if (_weapons.PendingWeapon is ThrowableWeapon)
			{
				_upperBody.Grenade.Equip();
				QueueLegacyArmPending(UPPER_BODY_GRENDE_EQUIP_TIME);
				return;
			}

			if (_weapons.PendingWeaponSlot > 0)
			{
				_weapons.DisarmCurrentWeapon();
				QueueLegacyArmPending(UPPER_BODY_EQUIP_ARM_TIME);

				_upperBody.Equip.SetAnimationTime(0.0f);
				_upperBody.Equip.Activate(0.2f);
			}
			else
			{
				_upperBody.Unequip.SetAnimationTime(0.0f);
				_upperBody.Unequip.Activate(0.2f);
				QueueLegacyDisarm(UPPER_BODY_UNEQUIP_DISARM_TIME);
			}
		}

		internal void NotifyLegacyEquipStateActivated()
		{
			if (UseFusionGraphMode == true)
				return;

			QueueLegacyArmPending(UPPER_BODY_EQUIP_ARM_TIME);
		}

		public void Turn(float angle)
		{
			if (UseFusionGraphMode == true)
			{
				TurnFusion(angle);
				return;
			}

			_lowerBody.Turn.Refresh(angle);
		}

		public void RefreshSnapping()
		{
			SnapWeapon();
		}

		// AnimationController INTERFACE

		protected override void OnSpawned()
		{
			if (UseFusionGraphMode == true)
			{
				OnSpawnedFusion();
				return;
			}

			ClearLegacyGameplayQueue();

			if (HasStateAuthority == true)
			{
				Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
			}

			_locomotion.Move.Activate(0.0f);

			if (_weapons.IsSwitchingWeapon() == true)
			{
				SwitchWeapons();
			}
		}

		protected override void OnFixedUpdate()
		{
			if (UseFusionGraphMode == true)
			{
				OnFixedUpdateFusion();
				return;
			}

			ProcessLegacyGameplayQueue();

			if (_jetpack.IsActive == true && _fullBody.Jetpack.IsActive() == false)
			{
				_upperBody.Reload.Deactivate(0.2f);
				_upperBody.DeactivateAllStates(0.1f, true);

				_weapons.DisarmCurrentWeapon();
				ClearLegacyGameplayQueue();

				_fullBody.Jetpack.Activate(0.1f);
			}
			else if (_jetpack.IsActive == false && _fullBody.Jetpack.IsActive() == true)
			{
				_fullBody.Jetpack.Deactivate(0.1f);

				SwitchWeapons(); // Equip pending weapon
			}
		}

		protected override void OnEvaluate()
		{
			if (UseFusionGraphMode == true)
			{
				OnEvaluateFusion();
				return;
			}

			SnapWeapon();
		}

		protected override void OnInterpolate()
		{
			if (UseFusionGraphMode == true)
			{
				OnInterpolateFusion();
			}
		}

		protected override bool UseBuiltInLayerEvaluation => UseFusionGraphMode == false;

		// MonoBehaviour INTERFACE

		protected override void Awake()
		{
			base.Awake();

			_kcc        = this.GetComponentNoAlloc<KCC>();
			_agent      = this.GetComponentNoAlloc<Agent>();
			_weapons    = this.GetComponentNoAlloc<Weapons>();
			_jetpack    = this.GetComponentNoAlloc<Jetpack>();

			if (_fusionAnimatorGraph != null && _useFusionGraphMode == false && (Layers == null || Layers.Count == 0))
			{
				_autoFusionGraphMode = true;
			}

			if (UseFusionGraphMode == true)
			{
				AwakeFusion();
				return;
			}

			_locomotion = FindLayer<LocomotionLayer>();
			_fullBody   = FindLayer<FullBodyLayer>();
			_lowerBody  = FindLayer<LowerBodyLayer>();
			_upperBody  = FindLayer<UpperBodyLayer>();
			_shoot      = FindLayer<ShootLayer>();
			_look       = FindLayer<LookLayer>();

			if (_kcc != null && _locomotion != null)
			{
				_kcc.MoveState = _locomotion.FindState<MoveState>();
			}
		}

		// PRIVATE METHODS

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
				Quaternion targetRotation = Quaternion.LookRotation(_agent.Context.Camera.transform.position + _agent.Context.Camera.transform.forward * 100.0f - weaponHandle.position);

				float   snapPower    = Mathf.Clamp(Mathf.Abs(_kcc.FixedData.LookPitch) / 60.0f, _aimSnapPower, 1.0f);
				Vector3 snapRotation = Quaternion.Slerp(handleRotation, targetRotation, snapPower).eulerAngles;

				snapRotation.y = targetRotation.eulerAngles.y;

				weaponHandle.rotation = Quaternion.Euler(snapRotation);
			}
			else
			{
				weaponHandle.rotation = Quaternion.LookRotation(_kcc.FixedData.LookDirection);
			}

			Transform leftHandTarget = _weapons.CurrentWeapon.LeftHandTarget;
			if (leftHandTarget != null)
			{
				bool leftSide = _agent.LeftSide;

				Vector3    leftHandLocalPosition       = _leftLowerArm.InverseTransformPoint(_leftHand.position);
				Vector3    leftHandTargetLocalPosition = _leftLowerArm.InverseTransformPoint(leftHandTarget.position);
				Quaternion leftLowerArmRotation        = Quaternion.FromToRotation(leftHandLocalPosition, leftHandTargetLocalPosition);

				_leftLowerArm.rotation *= leftSide == true ? Quaternion.Inverse(leftLowerArmRotation) : leftLowerArmRotation;

				for (int i = 0; i < 2; ++i)
				{
					Vector3    leftLowerArmOffset              = leftHandTarget.position - _leftHand.position;
					Vector3    leftLowerArmTargetPosition      = _leftLowerArm.position + leftLowerArmOffset;
					Vector3    leftLowerArmLocalPosition       = _leftUpperArm.InverseTransformPoint(_leftLowerArm.position);
					Vector3    leftLowerArmTargetLocalPosition = _leftUpperArm.InverseTransformPoint(leftLowerArmTargetPosition);
					Quaternion leftUpperArmRotation            = Quaternion.FromToRotation(leftLowerArmLocalPosition, leftLowerArmTargetLocalPosition);

					_leftUpperArm.rotation *= leftSide == true ? Quaternion.Inverse(leftUpperArmRotation) : leftUpperArmRotation;

					leftHandLocalPosition       = _leftLowerArm.InverseTransformPoint(_leftHand.position);
					leftHandTargetLocalPosition = _leftLowerArm.InverseTransformPoint(leftHandTarget.position);
					leftLowerArmRotation        = Quaternion.FromToRotation(leftHandLocalPosition, leftHandTargetLocalPosition);

					_leftLowerArm.rotation *= leftSide == true ? Quaternion.Inverse(leftLowerArmRotation) : leftLowerArmRotation;
				}

				_leftHand.position = leftHandTarget.position;
				_leftHand.rotation = leftHandTarget.rotation;
			}
		}

		private bool CanSnapHand()
		{
			if (UseFusionGraphMode == true)
				return CanSnapHandFusion();

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

		public new void SetInterlacedEvaluation(EEvaluationTarget target, int frames, int seed)
		{
			if (UseFusionGraphMode == true)
				return;

			base.SetInterlacedEvaluation(target, frames, seed);
		}

		private void QueueLegacyArmPending(float delaySeconds)
		{
			QueueLegacyTick(ref _legacyArmPendingTick, delaySeconds);
		}

		private void QueueLegacyDisarm(float delaySeconds)
		{
			QueueLegacyTick(ref _legacyDisarmTick, delaySeconds);
		}

		private void QueueLegacyFire(float delaySeconds)
		{
			QueueLegacyTick(ref _legacyFireTick, delaySeconds);
		}

		private void QueueLegacyTick(ref int targetTick, float delaySeconds)
		{
			int scheduledTick = GetCurrentTick() + SecondsToTicks(delaySeconds);
			if (targetTick == int.MinValue || scheduledTick < targetTick)
			{
				targetTick = scheduledTick;
			}
		}

		private void ProcessLegacyGameplayQueue()
		{
			int tick = GetCurrentTick();

			if (_legacyDisarmTick != int.MinValue && tick >= _legacyDisarmTick)
			{
				_weapons.DisarmCurrentWeapon();
				_legacyDisarmTick = int.MinValue;
			}

			if (_legacyArmPendingTick != int.MinValue && tick >= _legacyArmPendingTick)
			{
				_weapons.ArmPendingWeapon();
				_legacyArmPendingTick = int.MinValue;
			}

			if (_legacyFireTick != int.MinValue && tick >= _legacyFireTick)
			{
				_weapons.Fire();
				_legacyFireTick = int.MinValue;
			}
		}

		private void ClearLegacyGameplayQueue()
		{
			_legacyArmPendingTick = int.MinValue;
			_legacyDisarmTick = int.MinValue;
			_legacyFireTick = int.MinValue;
		}

		private int GetCurrentTick()
		{
			return Runner != null ? Runner.Tick.Raw : 0;
		}

		private int SecondsToTicks(float seconds)
		{
			float deltaTime = Runner != null ? Runner.DeltaTime : 0.02f;
			return Mathf.Max(1, Mathf.CeilToInt(seconds / Mathf.Max(0.0001f, deltaTime)));
		}

	}
}
