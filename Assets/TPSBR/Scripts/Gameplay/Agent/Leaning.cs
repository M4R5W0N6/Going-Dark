namespace TPSBR
{
	using UnityEngine;

	[DefaultExecutionOrder(-10)]
	public sealed class Leaning : ContextBehaviour
	{
		private const float MinLeanDuration = 0.05f;
		private const float MaxLeanDuration = 2.0f;
		private const float Epsilon = 0.0001f;

		private enum ELeanDirection
		{
			Left = -1,
			Right = 1,
		}

		[Header("Input Influence")]
		[SerializeField][Range(0.0f, 2.0f)][Tooltip("Strength of manual lean buttons. 0 disables manual lean, 2 doubles manual lean strength.")]
		private float _manualInfluence = 1.0f;
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("Strength of move input contribution to automatic lean.")]
		private float _moveInfluence = 1.0f;
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("Strength of look input contribution to automatic lean.")]
		private float _lookInfluence = 1.0f;
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("Additional opposing-input lean strength. 0 keeps base move+look behavior. 1 enforces full opposing support even if move/look influence are zero.")]
		private float _opposingInfluence = 0.0f;
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("Automatic anti-lean while aiming/ADS. 0 keeps full auto lean, 1 fully suppresses auto lean.")]
		private float _aimingAntiLean = 1.0f;

		[Header("Lean Motion")]
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("Overall lean speed scale. 0 is slowest, 1 is fastest.")]
		private float _leanSpeed = 0.5f;
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("How strongly lean resets toward nearest side (-1 or 1).")]
		private float _resetInfluence = 1.0f;

		[SerializeField, HideInInspector]
		private float _currentLean = 1.0f;
		[SerializeField, HideInInspector]
		private float _lastLeanSign = 1.0f;

		public float CurrentLean => _currentLean;
		public float LeanSide => Mathf.Clamp01((_currentLean + 1.0f) * 0.5f);

		public void ResetLean()
		{
			_currentLean = 1.0f;
			_lastLeanSign = 1.0f;
		}

		public float UpdateLeaning(Vector2 moveInput, Vector2 lookInput, bool leanLeftPressed, bool leanRightPressed, bool isAiming, float deltaTime)
		{
			float manualInput = GetManualInput(leanLeftPressed, leanRightPressed);
			bool hasManualInput = Mathf.Abs(manualInput) > Epsilon;

			bool hasAutomaticInput = TryGetAutomaticLeanInput(moveInput, lookInput, out float currentMove, out float currentLook, out float automaticInput);
			float aimingAutoScale = isAiming == true ? 1.0f - Mathf.Clamp01(_aimingAntiLean) : 1.0f;
			automaticInput *= aimingAutoScale;
			currentMove *= aimingAutoScale;
			currentLook *= aimingAutoScale;

			float drivingInput = hasManualInput == true ? manualInput : automaticInput;
			bool hasDrivingInput = Mathf.Abs(drivingInput) > Epsilon || hasAutomaticInput == true;
			float leanDelta = 0.0f;

			if (hasDrivingInput == true)
			{
				float leanSpeed = GetLeanSpeedAtMaxInput();
				leanDelta -= drivingInput * leanSpeed * deltaTime;
			}

			float currentLeanSign = Mathf.Abs(_currentLean) > Epsilon ? Mathf.Sign(_currentLean) : _lastLeanSign;
			float nearestLean = currentLeanSign < 0.0f ? -1.0f : 1.0f;
			float resetDistance = Mathf.Abs(nearestLean - _currentLean);
			float resetDirection = Mathf.Sign(nearestLean - _currentLean);
			float maxInputMagnitude = Mathf.Max(
				Mathf.Abs(manualInput),
				Mathf.Max(Mathf.Abs(drivingInput), Mathf.Max(Mathf.Abs(currentMove), Mathf.Abs(currentLook))));
			float inverseInputMagnitude = 1.0f - Mathf.Clamp01(maxInputMagnitude);
			float resetStrength = Mathf.Clamp01(_resetInfluence) * inverseInputMagnitude;
			leanDelta += resetDirection * resetStrength * resetDistance * GetLeanSpeedAtMaxInput() * deltaTime;

			_currentLean += leanDelta;
			_currentLean = Mathf.Clamp(_currentLean, -1.0f, 1.0f);
			if (Mathf.Abs(_currentLean) > Epsilon)
			{
				_lastLeanSign = Mathf.Sign(_currentLean);
			}

			return LeanSide;
		}

		private bool TryGetAutomaticLeanInput(Vector2 moveInput, Vector2 lookInput, out float currentMove, out float currentLook, out float drivingInput)
		{
			drivingInput = 0.0f;

			float moveAxis = GetMoveHorizontalInput(moveInput);
			float lookAxis = GetLookHorizontalInput(lookInput);
			bool hasRawMoveAxis = Mathf.Abs(moveAxis) > Epsilon;
			bool hasRawLookAxis = Mathf.Abs(lookAxis) > Epsilon;
			bool hasOpposingInputs = hasRawMoveAxis == true && hasRawLookAxis == true && Mathf.Sign(moveAxis) != Mathf.Sign(lookAxis);

			currentMove = GetWeightedSignedInput(moveAxis, _moveInfluence);
			currentLook = GetWeightedSignedInput(lookAxis, _lookInfluence);

			bool hasMove = Mathf.Abs(currentMove) > Epsilon;
			bool hasLook = Mathf.Abs(currentLook) > Epsilon;

			if (hasMove == false && hasLook == false && (hasOpposingInputs == false || _opposingInfluence <= Epsilon))
				return false;

			float moveLeanInput = -currentMove;
			float lookLeanInput = -currentLook;
			float combinedLeanInput = moveLeanInput + lookLeanInput;

			if (hasOpposingInputs == true && _opposingInfluence > Epsilon)
			{
				float opposingDirection = -Mathf.Sign(moveAxis);
				if (opposingDirection == 0.0f)
				{
					opposingDirection = -Mathf.Sign(lookAxis);
				}

				if (opposingDirection != 0.0f)
				{
					float opposingMagnitude = Mathf.Min(Mathf.Abs(moveAxis), Mathf.Abs(lookAxis));
					if (opposingMagnitude > Epsilon)
					{
						float targetMagnitude = Mathf.Abs(combinedLeanInput) + opposingMagnitude;
						float targetLeanInput = opposingDirection * targetMagnitude;
						combinedLeanInput = Mathf.Lerp(combinedLeanInput, targetLeanInput, Mathf.Clamp01(_opposingInfluence));
					}
				}
			}

			drivingInput = combinedLeanInput;
			return Mathf.Abs(drivingInput) > Epsilon;
		}

		private float GetManualInput(bool leanLeftPressed, bool leanRightPressed)
		{
			if (leanLeftPressed == leanRightPressed)
				return 0.0f;

			ELeanDirection direction = leanLeftPressed == true ? ELeanDirection.Left : ELeanDirection.Right;
			float clampedManualInfluence = Mathf.Clamp(_manualInfluence, 0.0f, 2.0f);
			return direction == ELeanDirection.Left ? clampedManualInfluence : -clampedManualInfluence;
		}

		private float GetLeanSpeedAtMaxInput()
		{
			float completionTime = Mathf.Lerp(MaxLeanDuration, MinLeanDuration, Mathf.Clamp01(_leanSpeed));
			return completionTime > 0.0f ? (2.0f / completionTime) : 0.0f;
		}

		private float GetWeightedSignedInput(float signedInput, float influence)
		{
			float magnitude = Mathf.Abs(signedInput);
			if (magnitude <= Epsilon)
				return 0.0f;

			float scaledInfluence = Mathf.Clamp01(influence);
			if (scaledInfluence <= Epsilon)
				return 0.0f;

			float inputSign = Mathf.Sign(signedInput);
			return inputSign * magnitude * scaledInfluence;
		}

		private float GetMoveHorizontalInput(Vector2 input)
		{
			return Mathf.Clamp(input.x, -1.0f, 1.0f);
		}

		private float GetLookHorizontalInput(Vector2 input)
		{
			return input.x;
		}
	}
}
