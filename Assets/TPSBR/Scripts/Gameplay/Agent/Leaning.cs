namespace TPSBR
{
	using UnityEngine;

	[DefaultExecutionOrder(-10)]
	public sealed class Leaning : ContextBehaviour
	{
		private const float LeanWeightFloor = 0.1f;
		private const float MinLeanDuration = 0.05f;
		private const float MaxLeanDuration = 2.0f;
		private const float ManualLeanOverrideStrength = 2.0f;

		private enum ELeanDirection
		{
			Left = -1,
			Right = 1,
		}

		[SerializeField][Tooltip("Enable lean from manual lean input.")]
		private bool _manualLean = true;
		[SerializeField][Tooltip("When true, lean from auto-input is only applied when move and look oppose.")]
		private bool _opposingOnly = false;
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("Normalized lean duration from 0 (fastest) to 1 (slowest).")]
		private float _leanDuration = 0.5f;
		[SerializeField][Range(0.0f, 2.0f)][Tooltip("Flat lean return speed toward the nearest side, applied every frame.")]
		private float _resetSpeed = 1.0f;

		[SerializeField]
		private AnimationCurve _moveCurve = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
		[SerializeField][Range(0.0f, 2.0f)][Tooltip("Multiplier applied to move auto-lean curve output.")]
		private float _moveWeight = 1.0f;
		[SerializeField]
		private AnimationCurve _lookCurve = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
		[SerializeField][Range(0.0f, 2.0f)][Tooltip("Multiplier applied to look auto-lean curve output.")]
		private float _lookWeight = 1.0f;

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

		public float UpdateLeaning(Vector2 moveInput, Vector2 lookInput, bool leanLeftPressed, bool leanRightPressed, float deltaTime)
		{
			bool hasDrivingInput = TryGetAutomaticLeanInput(moveInput, lookInput, leanLeftPressed, leanRightPressed, deltaTime, out float currentMove, out float currentLook, out float drivingInput);
			float leanDelta = 0.0f;

			if (hasDrivingInput == true)
			{
				float leanSpeed = GetLeanSpeedAtMaxInput();
				leanDelta -= drivingInput * leanSpeed * deltaTime;
			}

			float currentLeanSign = Mathf.Abs(_currentLean) > 0.0001f ? Mathf.Sign(_currentLean) : _lastLeanSign;
			float nearestLean = currentLeanSign < 0.0f ? -1.0f : 1.0f;
			float resetDistance = Mathf.Abs(nearestLean - _currentLean);
			float resetDirection = Mathf.Sign(nearestLean - _currentLean);
			float maxInputMagnitude = Mathf.Max(Mathf.Abs(currentMove), Mathf.Abs(currentLook));
			float inverseInputMagnitude = 1.0f - Mathf.Clamp01(maxInputMagnitude);
			leanDelta += resetDirection * _resetSpeed * inverseInputMagnitude * resetDistance * deltaTime;

			_currentLean += leanDelta;
			_currentLean = Mathf.Clamp(_currentLean, -1.0f, 1.0f);
			if (Mathf.Abs(_currentLean) > 0.0001f)
			{
				_lastLeanSign = Mathf.Sign(_currentLean);
			}

			return LeanSide;
		}

		private bool TryGetAutomaticLeanInput(Vector2 moveInput, Vector2 lookInput, bool leanLeftPressed, bool leanRightPressed, float deltaTime, out float currentMove, out float currentLook, out float drivingInput)
		{
			float moveAxis = GetNormalizedHorizontalInput(moveInput);
			float lookAxis = GetNormalizedHorizontalInput(lookInput);
			bool hasRawMoveAxis = Mathf.Abs(moveAxis) > 0.0001f;
			bool hasRawLookAxis = Mathf.Abs(lookAxis) > 0.0001f;

			currentMove = GetWeightedSignedInput(moveAxis, _moveCurve, _moveWeight, false, 0.0f);
			currentLook = GetWeightedSignedInput(lookAxis, _lookCurve, _lookWeight, true, deltaTime);
			drivingInput = 0.0f;
			if (TryGetLeanButtonOverride(leanLeftPressed, leanRightPressed, out float overrideInput) == true)
			{
				drivingInput = overrideInput;
				return true;
			}

			bool hasMove = Mathf.Abs(currentMove) > 0.0001f;
			bool hasLook = Mathf.Abs(currentLook) > 0.0001f;
			if (hasMove == false && hasLook == false)
			{
				return false;
			}

			float moveLeanInput = -currentMove;
			float lookLeanInput = -currentLook;

			float summedLeanInput = moveLeanInput + lookLeanInput;
			bool hasOpposingInputs = hasRawMoveAxis == true && hasRawLookAxis == true && Mathf.Sign(moveAxis) != Mathf.Sign(lookAxis);

			if (_opposingOnly == true)
			{
				if (hasOpposingInputs == false)
				{
					return false;
				}
			}
			else if (hasOpposingInputs == true)
			{
				summedLeanInput = Mathf.Abs(summedLeanInput) * Mathf.Sign(moveLeanInput);
			}

			if (hasMove == true && hasLook == true)
			{
				summedLeanInput *= 0.5f;
			}

			drivingInput = Mathf.Clamp(summedLeanInput, -1.0f, 1.0f);
			return true;
		}

		private bool TryGetLeanButtonOverride(bool leanLeftPressed, bool leanRightPressed, out float drivingInputOverride)
		{
			drivingInputOverride = 0.0f;
			if (_manualLean == false)
				return false;

			if (leanLeftPressed == leanRightPressed)
				return false;

			ELeanDirection direction = leanLeftPressed == true ? ELeanDirection.Left : ELeanDirection.Right;
			drivingInputOverride = direction == ELeanDirection.Left ? ManualLeanOverrideStrength : -ManualLeanOverrideStrength;
			return true;
		}

		private float GetLeanSpeedAtMaxInput()
		{
			float completionTime = Mathf.Lerp(MinLeanDuration, MaxLeanDuration, Mathf.Clamp01(_leanDuration));
			return completionTime > 0.0f ? (2.0f / completionTime) : 0.0f;
		}

		private float GetWeightedSignedInput(float signedInput, AnimationCurve weightCurve, float weight, bool useLookFloor, float minimumMagnitude)
		{
			// For look-driven automatic lean, enforce a minimum magnitude based on frame time.
			// This is equivalent to Mathf.Max(abs(input), deltaTime) for the raw look axis.
			float floorMagnitude = useLookFloor == true ? minimumMagnitude : 0.0f;
			float magnitude = Mathf.Max(Mathf.Abs(signedInput), floorMagnitude);

			if (magnitude <= 0.0001f)
				return 0.0f;

			float inputSign = Mathf.Sign(signedInput);
			float leanPosition = Mathf.Clamp01(Mathf.Abs(_currentLean));

			float curveValue = weightCurve != null ? weightCurve.Evaluate(leanPosition) : 1.0f;
			curveValue = Mathf.Max(curveValue, LeanWeightFloor);
			float scaledWeight = Mathf.Max(0.0f, weight);
			return inputSign * magnitude * curveValue * scaledWeight;
		}

		private float GetNormalizedHorizontalInput(Vector2 input)
		{
			if (input.sqrMagnitude > 1.0f)
			{
				input.Normalize();
			}

			return Mathf.Clamp(input.x, -1.0f, 1.0f);
		}
	}
}
