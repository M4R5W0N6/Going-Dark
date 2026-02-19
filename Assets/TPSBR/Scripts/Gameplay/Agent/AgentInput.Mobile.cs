namespace TPSBR
{
	using UnityEngine;
	using TPSBR.UI;

	public sealed partial class AgentInput
	{
		partial void ProcessMobileInput(bool isInputPoll)
		{
			// Very basic mobile input, not all actions are implemented.

			Vector2 moveDirection;
			Vector2 lookRotationDelta;

			if (_mobileInputView == null)
			{
				if (Context != null && Context.UI != null)
				{
					_mobileInputView = Context.UI.Get<UIMobileInputView>();
				}

				return;
			}

			const float mobileSensitivityMultiplier = 32.0f;

			_renderMoveInputRaw = _mobileInputView.Move;
			_renderLookInputRaw = _mobileInputView.Look;
			moveDirection     = _renderMoveInputRaw;
			if (moveDirection.sqrMagnitude > 1.0f)
			{
				moveDirection.Normalize();
			}
			lookRotationDelta = InputUtility.GetSmoothLookRotationDelta(_smoothLookRotationDelta, new Vector2(-_renderLookInputRaw.y, _renderLookInputRaw.x) * mobileSensitivityMultiplier, Global.RuntimeSettings.Sensitivity, _lookResponsivity);

			_mobileInputView.Look = default;

			if (_agent.Character.CharacterController.FixedData.Aim == true)
			{
				lookRotationDelta *= Global.RuntimeSettings.AimSensitivity;
			}

			_renderInput.MoveDirection     = moveDirection;
			_renderInput.LookRotationDelta = lookRotationDelta;
			_renderInput.Jump              = _mobileInputView.Jump;
			_renderInput.Attack            = _mobileInputView.Fire;
			_renderInput.Interact          = _mobileInputView.Interact;
		}
	}
}
