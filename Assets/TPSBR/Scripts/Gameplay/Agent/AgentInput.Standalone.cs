using Fusion.Addons.KCC;

namespace TPSBR
{
	using UnityEngine;

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
				_renderLookInputRaw = _lookAction.ReadValue<Vector2>();
				Vector2 lookDelta = _renderLookInputRaw * 0.075f;
				lookRotationDelta = InputUtility.GetSmoothLookRotationDelta(_smoothLookRotationDelta, new Vector2(-lookDelta.y, lookDelta.x), GetLookSensitivity(), _lookResponsivity, _lookAction.activeControl);
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
