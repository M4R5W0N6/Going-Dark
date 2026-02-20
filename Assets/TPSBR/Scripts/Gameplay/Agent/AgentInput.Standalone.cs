using Fusion.Addons.KCC;

namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.InputSystem;

	public sealed partial class AgentInput
	{
		partial void ProcessStandaloneInput(bool isInputPoll)
		{
			Vector2 moveDirection;
			Vector2 lookRotationDelta;

			moveDirection     = Vector2.zero;
			lookRotationDelta = Vector2.zero;

			if (_moveAction != null)
			{
				moveDirection = _moveAction.ReadValue<Vector2>();
			}
			else
			{
				moveDirection = default;
			}
			_renderMoveInputRaw = moveDirection;

			if (_lookAction != null)
			{
				Vector2 rawLookInput = _lookAction.ReadValue<Vector2>();
				InputControl activeLookControl = _lookAction.activeControl;

				Vector2 lookInputForRotation = rawLookInput * InputUtility.GetLookDeviceNormalizationScale(activeLookControl);

				Vector2 lookDelta = lookInputForRotation * InputUtility.GetLookDeltaScale();
				float lookSensitivity = InputUtility.GetGameplayLookSensitivity(GetLookSensitivity());
				lookRotationDelta = InputUtility.GetSmoothLookRotationDelta(_smoothLookRotationDelta, new Vector2(-lookDelta.y, lookDelta.x), lookSensitivity, _lookResponsivity, activeLookControl);
				_renderLookInputRaw = InputUtility.GetAutoLeanLookInput(rawLookInput, activeLookControl);
			}

			if (_agent.Character.CharacterController.FixedData.Aim == true)
			{
				lookRotationDelta *= GetAimSensitivity();
			}

			if (moveDirection.sqrMagnitude > 1.0f)
			{
				moveDirection.Normalize();
			}

			_renderInput.MoveDirection     = moveDirection;
			_renderInput.LookRotationDelta = lookRotationDelta;
			_renderInput.Jump              = _jumpAction != null && _jumpAction.IsPressed();
			_renderInput.Aim               = _aimAction != null && _aimAction.IsPressed();
			_renderInput.Attack            = _fireAction != null && _fireAction.IsPressed();
			_renderInput.Reload            = _reloadAction != null && _reloadAction.IsPressed();
			_renderInput.Interact          = _interactAction != null && _interactAction.IsPressed();
			_renderInput.Weapon            = GetWeaponInput();
			_renderInput.ToggleJetpack     = _abilityAction != null && _abilityAction.IsPressed();
			_renderInput.Thrust            = _jumpAction != null && _jumpAction.IsPressed();
		}
	}
}
