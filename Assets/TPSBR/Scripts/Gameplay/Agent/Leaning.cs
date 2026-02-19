namespace TPSBR
{
	using UnityEngine;

	[DefaultExecutionOrder(-10)]
	public sealed class Leaning : ContextBehaviour
	{
		[SerializeField][Range(0.0f, 10.0f)][Tooltip("Shoulder side change speed while directional input is active.")]
		private float _leanSpeed = 5.0f;
		[SerializeField][Range(0.0f, 10.0f)][Tooltip("Shoulder side reset speed when directional input is inactive.")]
		private float _resetSpeed = 5.0f;
		[SerializeField][Tooltip("Move input weighting by current lean. X=|currentLean|, Y=weight.")]
		private AnimationCurve _moveWeight = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
		[SerializeField][Tooltip("Look input weighting by current lean. X=|currentLean|, Y=weight.")]
		private AnimationCurve _lookWeight = AnimationCurve.Linear(0.0f, 1.0f, 1.0f, 1.0f);
		[SerializeField][Range(0.0f, 1.0f)][Tooltip("Minimum curve weight applied to automatic-lean move/look input.")]
		private float _weightFloor = 0.05f;
		[SerializeField][Range(0.0f, 5.0f)][Tooltip("Driving input override strength when explicit lean buttons are pressed.")]
		private float _overrideStrength = 2.0f;

		[SerializeField, HideInInspector]
		private float _currentLean = 1.0f;

		public float CurrentLean => _currentLean;
		public float LeanSide => Mathf.Clamp01((_currentLean + 1.0f) * 0.5f);

		public void ResetLean()
		{
			_currentLean = 1.0f;
		}

		public float UpdateLeaning(Vector2 moveInput, Vector2 lookInput, bool leanLeftPressed, bool leanRightPressed, float deltaTime)
		{
			bool hasDrivingInput = TryGetAutomaticLeanInput(moveInput, lookInput, leanLeftPressed, leanRightPressed, deltaTime, out _, out _, out float drivingInput);
			if (hasDrivingInput == true)
			{
				_currentLean -= drivingInput * Mathf.Max(0.0f, _leanSpeed) * deltaTime;
				_currentLean = Mathf.Clamp(_currentLean, -1.0f, 1.0f);
			}
			else
			{
				float nearestLean = _currentLean < 0.0f ? -1.0f : 1.0f;
				_currentLean = Mathf.MoveTowards(_currentLean, nearestLean, Mathf.Max(0.0f, _resetSpeed) * deltaTime);
			}

			return LeanSide;
		}

		private bool TryGetAutomaticLeanInput(Vector2 moveInput, Vector2 lookInput, bool leanLeftPressed, bool leanRightPressed, float deltaTime, out float currentMove, out float currentLook, out float drivingInput)
		{
			currentMove = GetWeightedSignedInput(GetNormalizedHorizontalInput(moveInput), _moveWeight, false, 0.0f);
			currentLook = GetWeightedSignedInput(GetNormalizedHorizontalInput(lookInput), _lookWeight, true, deltaTime);
			drivingInput = 0.0f;
			if (TryGetLeanButtonOverride(leanLeftPressed, leanRightPressed, out float overrideInput) == true)
			{
				drivingInput = overrideInput;
				return true;
			}

			bool hasMove = Mathf.Abs(currentMove) > 0.0001f;
			bool hasLook = Mathf.Abs(currentLook) > 0.0001f;
			if (hasMove == false && hasLook == false)
				return false;

			drivingInput = hasMove == true ? -currentMove : -currentLook;
			return true;
		}

		private bool TryGetLeanButtonOverride(bool leanLeftPressed, bool leanRightPressed, out float drivingInputOverride)
		{
			drivingInputOverride = 0.0f;

			if (leanLeftPressed == leanRightPressed)
				return false;

			if (_overrideStrength <= 0.0001f)
				return false;

			// Positive driving input moves lean toward left side, negative toward right side.
			drivingInputOverride = leanLeftPressed == true ? _overrideStrength : -_overrideStrength;
			return true;
		}

		private float GetWeightedSignedInput(float signedInput, AnimationCurve weightCurve, bool useLookFloor, float minimumMagnitude)
		{
			// For look-driven automatic lean, enforce a minimum magnitude based on frame time.
			// This is equivalent to Mathf.Max(abs(input), deltaTime) for the raw look axis.
			float floorMagnitude = useLookFloor == true ? minimumMagnitude : 0.0f;
			float magnitude = Mathf.Max(Mathf.Abs(signedInput), floorMagnitude);

			if (magnitude <= 0.0001f)
				return 0.0f;

			float inputSign = Mathf.Sign(signedInput);
			float leanPosition = Mathf.Clamp01((_currentLean * inputSign + 1.0f) * 0.5f);

			float weight = weightCurve != null ? weightCurve.Evaluate(leanPosition) : 1.0f;
			weight = Mathf.Clamp(weight, Mathf.Clamp01(_weightFloor), 1.0f);
			return inputSign * magnitude * weight;
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
