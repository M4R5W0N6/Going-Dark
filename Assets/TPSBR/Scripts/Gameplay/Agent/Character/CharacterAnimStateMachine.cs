namespace TPSBR
{
	using System;
	using Fusion.Addons.KCC;
	using Unity.VisualScripting;
	using UnityEngine;

	public sealed class CharacterAnimStateMachine
	{
		private const string VS_EVENT_ON_SPAWNED          = "AnimSM.OnSpawned";
		private const string VS_EVENT_ON_FIXED_UPDATE     = "AnimSM.OnFixedUpdate";
		private const string VS_EVENT_CAN_JUMP            = "AnimSM.CanJump";
		private const string VS_EVENT_CAN_SWITCH_WEAPONS  = "AnimSM.CanSwitchWeapons";
		private const string VS_EVENT_SET_DEAD            = "AnimSM.SetDead";
		private const string VS_EVENT_START_FIRE          = "AnimSM.StartFire";
		private const string VS_EVENT_PROCESS_THROW       = "AnimSM.ProcessThrow";
		private const string VS_EVENT_START_RELOAD        = "AnimSM.StartReload";
		private const string VS_EVENT_SWITCH_WEAPONS      = "AnimSM.SwitchWeapons";
		private const string VS_EVENT_TURN                = "AnimSM.Turn";

		private const string VS_VAR_RESULT                = "AnimSM.Result";
		private const string VS_VAR_SKIP_DEFAULT          = "AnimSM.SkipDefault";
		private const string VS_VAR_REQUEST_FORCE         = "AnimSM.Request.Force";
		private const string VS_VAR_REQUEST_IS_DEAD       = "AnimSM.Request.IsDead";
		private const string VS_VAR_REQUEST_START         = "AnimSM.Request.Start";
		private const string VS_VAR_REQUEST_HOLD          = "AnimSM.Request.Hold";
		private const string VS_VAR_REQUEST_ANGLE         = "AnimSM.Request.Angle";

		private const string VS_VAR_IS_DEAD               = "AnimSM.State.IsDead";
		private const string VS_VAR_IS_JETPACK_ACTIVE     = "AnimSM.State.IsJetpackActive";
		private const string VS_VAR_IS_JUMPING            = "AnimSM.State.IsJumping";
		private const string VS_VAR_IS_RELOADING          = "AnimSM.State.IsReloading";
		private const string VS_VAR_IS_EQUIPPING          = "AnimSM.State.IsEquipping";
		private const string VS_VAR_IS_UNEQUIPPING        = "AnimSM.State.IsUnequipping";
		private const string VS_VAR_IS_THROWING           = "AnimSM.State.IsThrowing";
		private const string VS_VAR_IS_TURNING            = "AnimSM.State.IsTurning";
		private const string VS_VAR_CURRENT_WEAPON_SLOT   = "AnimSM.State.CurrentWeaponSlot";
		private const string VS_VAR_PENDING_WEAPON_SLOT   = "AnimSM.State.PendingWeaponSlot";
		private const string VS_VAR_IS_GROUNDED           = "AnimSM.State.IsGrounded";

		private CharacterAnimationController _controller;
		private KCC                          _kcc;
		private Weapons                      _weapons;
		private Jetpack                      _jetpack;
		private LocomotionLayer              _locomotion;
		private FullBodyLayer                _fullBody;
		private UpperBodyLayer               _upperBody;
		private LowerBodyLayer               _lowerBody;
		private ShootLayer                   _shoot;
		private LookLayer                    _look;

		private ScriptMachine        _scriptMachine;
		private VariableDeclarations _variables;

		public void Initialize(CharacterAnimationController controller, KCC kcc, Weapons weapons, Jetpack jetpack, LocomotionLayer locomotion, FullBodyLayer fullBody, UpperBodyLayer upperBody, LowerBodyLayer lowerBody, ShootLayer shoot, LookLayer look)
		{
			_controller = controller;
			_kcc        = kcc;
			_weapons    = weapons;
			_jetpack    = jetpack;
			_locomotion = locomotion;
			_fullBody   = fullBody;
			_upperBody  = upperBody;
			_lowerBody  = lowerBody;
			_shoot      = shoot;
			_look       = look;

			_scriptMachine = _controller != null ? _controller.GetComponent<ScriptMachine>() : null;
			_variables     = _controller != null && Variables.ExistOnObject(_controller) == true ? Variables.Object(_controller) : null;

			PublishStateVariables();
		}

		public bool CanJump()
		{
			PublishStateVariables();

			bool defaultValue = CanJumpDefault();

			if (HasVisualScriptingGraph() == true)
			{
				_variables.Set(VS_VAR_RESULT, defaultValue);
				CustomEvent.Trigger(_controller.gameObject, VS_EVENT_CAN_JUMP);
				return GetBoolVariable(VS_VAR_RESULT, defaultValue);
			}

			return defaultValue;
		}

		public bool CanSwitchWeapons(bool force)
		{
			PublishStateVariables();

			bool defaultValue = CanSwitchWeaponsDefault(force);

			if (HasVisualScriptingGraph() == true)
			{
				_variables.Set(VS_VAR_REQUEST_FORCE, force);
				_variables.Set(VS_VAR_RESULT, defaultValue);
				CustomEvent.Trigger(_controller.gameObject, VS_EVENT_CAN_SWITCH_WEAPONS);
				return GetBoolVariable(VS_VAR_RESULT, defaultValue);
			}

			return defaultValue;
		}

		public void SetDead(bool isDead)
		{
			PublishStateVariables();

			if (ShouldSkipDefault(VS_EVENT_SET_DEAD, VS_VAR_REQUEST_IS_DEAD, isDead) == true)
			{
				PublishStateVariables();
				return;
			}

			SetDeadDefault(isDead);
			PublishStateVariables();
		}

		public bool StartFire()
		{
			PublishStateVariables();

			if (ShouldSkipDefault(VS_EVENT_START_FIRE) == true)
			{
				return GetBoolVariable(VS_VAR_RESULT, false);
			}

			bool result = StartFireDefault();
			PublishStateVariables();
			return result;
		}

		public void ProcessThrow(bool start, bool hold)
		{
			PublishStateVariables();

			if (ShouldSkipDefault(VS_EVENT_PROCESS_THROW, VS_VAR_REQUEST_START, start, VS_VAR_REQUEST_HOLD, hold) == true)
			{
				PublishStateVariables();
				return;
			}

			ProcessThrowDefault(start, hold);
			PublishStateVariables();
		}

		public bool StartReload()
		{
			PublishStateVariables();

			if (ShouldSkipDefault(VS_EVENT_START_RELOAD) == true)
			{
				return GetBoolVariable(VS_VAR_RESULT, false);
			}

			bool result = StartReloadDefault();
			PublishStateVariables();
			return result;
		}

		public void SwitchWeapons()
		{
			PublishStateVariables();

			if (ShouldSkipDefault(VS_EVENT_SWITCH_WEAPONS) == true)
			{
				PublishStateVariables();
				return;
			}

			SwitchWeaponsDefault();
			PublishStateVariables();
		}

		public void Turn(float angle)
		{
			PublishStateVariables();

			if (ShouldSkipDefault(VS_EVENT_TURN, VS_VAR_REQUEST_ANGLE, angle) == true)
			{
				PublishStateVariables();
				return;
			}

			TurnDefault(angle);
			PublishStateVariables();
		}

		public void OnSpawned()
		{
			if (_locomotion != null && _locomotion.Move != null)
			{
				_locomotion.Move.Activate(0.0f);
			}

			if (_weapons != null && _weapons.IsSwitchingWeapon() == true)
			{
				SwitchWeaponsDefault();
			}

			PublishStateVariables();

			if (HasVisualScriptingGraph() == true)
			{
				CustomEvent.Trigger(_controller.gameObject, VS_EVENT_ON_SPAWNED);
			}
		}

		public void OnFixedUpdate()
		{
			UpdateJetpackStateDefault();
			PublishStateVariables();

			if (HasVisualScriptingGraph() == true)
			{
				CustomEvent.Trigger(_controller.gameObject, VS_EVENT_ON_FIXED_UPDATE);
			}
		}

		private bool CanJumpDefault()
		{
			if (_fullBody != null)
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

			if (IsJumpingStateActive() == true)
				return false;

			return true;
		}

		private bool CanSwitchWeaponsDefault(bool force)
		{
			if (_fullBody != null && _fullBody.IsActive() == true)
			{
				if (_fullBody.Dead.IsActive() == true)
					return false;
				if (_fullBody.Jetpack.IsActive() == true)
					return false;
			}

			if (_upperBody != null && _upperBody.IsActive() == true)
			{
				if (_upperBody.Grenade.IsActive() == true && _upperBody.Grenade.CanSwitchWeapon() == false)
					return false;
				if (force == false && (_upperBody.Equip.IsActive() == true || _upperBody.Unequip.IsActive() == true))
					return false;
			}

			return true;
		}

		private void SetDeadDefault(bool isDead)
		{
			if (_fullBody == null)
				return;

			if (isDead == true)
			{
				_fullBody.Dead.Activate(0.2f);

				if (_kcc != null && _kcc.Data.IsGrounded == true)
				{
					_kcc.SetColliderLayer(LayerMask.NameToLayer("Ignore Raycast"));
					_kcc.SetCollisionLayerMask(_kcc.Settings.CollisionLayerMask & ~(1 << LayerMask.NameToLayer("AgentKCC")));
				}

				_upperBody?.DeactivateAllStates(0.2f, true);
				_look?.DeactivateAllStates(0.2f, true);
			}
			else
			{
				_fullBody.Dead.Deactivate(0.2f);
				_kcc?.SetShape(EKCCShape.Capsule);
			}
		}

		private bool StartFireDefault()
		{
			if (_fullBody != null && _fullBody.Dead.IsActive() == true)
				return false;
			if (_upperBody != null && _upperBody.HasActiveState() == true)
				return false;

			if (_shoot != null)
			{
				_shoot.Shoot.SetAnimationTime(0.0f);
				_shoot.Shoot.Activate(0.2f);
			}

			return true;
		}

		private void ProcessThrowDefault(bool start, bool hold)
		{
			_upperBody?.Grenade.ProcessThrow(start, hold);
		}

		private bool StartReloadDefault()
		{
			if (_upperBody != null && _upperBody.Grenade.IsActive() == true)
			{
				return _upperBody.Grenade.ProcessReload();
			}

			if (_fullBody != null && _fullBody.Dead.IsActive() == true)
				return false;
			if (_upperBody != null && _upperBody.Reload.IsActive() == true)
				return true;
			if (_upperBody != null && _upperBody.HasActiveState() == true)
				return false;

			_upperBody?.Reload.Activate(0.2f);
			return true;
		}

		private void SwitchWeaponsDefault()
		{
			if (_upperBody == null)
				return;

			_upperBody.Reload.Deactivate(0.2f);

			if (_weapons != null && _weapons.PendingWeapon is ThrowableWeapon)
			{
				_upperBody.Grenade.Equip();
				return;
			}

			if (_weapons != null && _weapons.PendingWeaponSlot > 0)
			{
				_weapons.DisarmCurrentWeapon();
				_upperBody.Equip.SetAnimationTime(0.0f);
				_upperBody.Equip.Activate(0.2f);
			}
			else
			{
				_upperBody.Unequip.SetAnimationTime(0.0f);
				_upperBody.Unequip.Activate(0.2f);
			}
		}

		private void TurnDefault(float angle)
		{
			if (_lowerBody == null)
				return;

			_lowerBody.Turn.Refresh(angle);
		}

		private void UpdateJetpackStateDefault()
		{
			if (_fullBody == null || _jetpack == null)
				return;

			if (_jetpack.IsActive == true)
			{
				if (_fullBody.Jetpack.IsActive() == false)
				{
					_upperBody?.Reload.Deactivate(0.2f);
					_weapons?.DisarmCurrentWeapon();
					_fullBody.Jetpack.Activate(0.1f);
				}
			}
			else
			{
				if (_fullBody.Jetpack.IsActive() == true)
				{
					_fullBody.Jetpack.Deactivate(0.1f);
					SwitchWeaponsDefault();
				}
			}
		}

		private bool IsDeadStateActive()
		{
			return _fullBody != null && _fullBody.Dead.IsActive() == true;
		}

		private bool IsJetpackStateActive()
		{
			return (_jetpack != null && _jetpack.IsActive == true) || (_fullBody != null && _fullBody.Jetpack.IsActive() == true);
		}

		private bool IsJumpingStateActive()
		{
			return _fullBody != null && (_fullBody.Jump.IsActive() == true || _fullBody.Fall.IsActive() == true || _fullBody.Land.IsActive() == true);
		}

		private bool HasVisualScriptingGraph()
		{
			return _controller != null && _scriptMachine != null && _scriptMachine.enabled == true && _variables != null;
		}

		private bool ShouldSkipDefault(string eventName)
		{
			if (HasVisualScriptingGraph() == false)
				return false;

			_variables.Set(VS_VAR_SKIP_DEFAULT, false);
			CustomEvent.Trigger(_controller.gameObject, eventName);

			return GetBoolVariable(VS_VAR_SKIP_DEFAULT, false);
		}

		private bool ShouldSkipDefault(string eventName, string variableName1, object value1)
		{
			if (HasVisualScriptingGraph() == false)
				return false;

			_variables.Set(variableName1, value1);
			_variables.Set(VS_VAR_SKIP_DEFAULT, false);
			CustomEvent.Trigger(_controller.gameObject, eventName);

			return GetBoolVariable(VS_VAR_SKIP_DEFAULT, false);
		}

		private bool ShouldSkipDefault(string eventName, string variableName1, object value1, string variableName2, object value2)
		{
			if (HasVisualScriptingGraph() == false)
				return false;

			_variables.Set(variableName1, value1);
			_variables.Set(variableName2, value2);
			_variables.Set(VS_VAR_SKIP_DEFAULT, false);
			CustomEvent.Trigger(_controller.gameObject, eventName);

			return GetBoolVariable(VS_VAR_SKIP_DEFAULT, false);
		}

		private bool GetBoolVariable(string variableName, bool defaultValue)
		{
			if (_variables == null || _variables.IsDefined(variableName) == false)
				return defaultValue;

			object value;
			try
			{
				value = _variables.Get(variableName);
			}
			catch
			{
				return defaultValue;
			}

			if (value is bool boolValue)
				return boolValue;

			try
			{
				return Convert.ToBoolean(value);
			}
			catch
			{
				return defaultValue;
			}
		}

		private void PublishStateVariables()
		{
			if (_variables == null)
				return;

			_variables.Set(VS_VAR_IS_DEAD, IsDeadStateActive());
			_variables.Set(VS_VAR_IS_JETPACK_ACTIVE, IsJetpackStateActive());
			_variables.Set(VS_VAR_IS_JUMPING, IsJumpingStateActive());
			_variables.Set(VS_VAR_IS_RELOADING, _upperBody != null && _upperBody.Reload.IsActive() == true);
			_variables.Set(VS_VAR_IS_EQUIPPING, _upperBody != null && _upperBody.Equip.IsActive() == true);
			_variables.Set(VS_VAR_IS_UNEQUIPPING, _upperBody != null && _upperBody.Unequip.IsActive() == true);
			_variables.Set(VS_VAR_IS_THROWING, _upperBody != null && _upperBody.Grenade.IsActive() == true);
			_variables.Set(VS_VAR_IS_TURNING, _lowerBody != null && Mathf.Abs(_lowerBody.Turn.RemainingTime) > 0.001f);
			_variables.Set(VS_VAR_CURRENT_WEAPON_SLOT, _weapons != null ? _weapons.CurrentWeaponSlot : -1);
			_variables.Set(VS_VAR_PENDING_WEAPON_SLOT, _weapons != null ? _weapons.PendingWeaponSlot : -1);
			_variables.Set(VS_VAR_IS_GROUNDED, _kcc != null && _kcc.FixedData.IsGrounded);
		}
	}
}
