namespace TPSBR
{
	using UnityEngine.InputSystem;

	using UnityEngine;
	using Fusion.Addons.KCC;

	public static partial class InputUtility
	{
		// CONSTANTS

		private const float INCH_TO_CM = 2.54f;
		private const float LOOK_DELTA_SCALE = 0.075f;
		private const float CONTROLLER_LOOK_REFERENCE_HZ = 125.0f;
		private const float MOUSE_AUTO_LEAN_REFERENCE_DELTA = 8.0f;

		// PUBLIC METHODS

		public static Vector2 GetSmoothLookRotationDelta(SmoothVector2 smoothVector, Vector2 lookRotationDelta, float sensitivity, float responsivity)
		{
			return GetSmoothLookRotationDelta(smoothVector, lookRotationDelta, sensitivity, responsivity, null);
		}

		public static Vector2 GetSmoothLookRotationDelta(
			SmoothVector2 smoothVector,
			Vector2 lookRotationDelta,
			float sensitivity,
			float responsivity,
			InputControl lookInputControl)
		{
			lookRotationDelta *= sensitivity;
			lookRotationDelta *= GetLookInputSensitivityScale(lookInputControl);

			// If the look rotation responsivity is enabled, calculate average delta instead.
			if (responsivity > 0.0f)
			{
				// Kill any rotation in opposite direction for instant direction flip.
				smoothVector.FilterValues(lookRotationDelta.x < 0.0f, lookRotationDelta.x > 0.0f, lookRotationDelta.y < 0.0f, lookRotationDelta.y > 0.0f);

				// Add or update value for current frame.
				smoothVector.AddValue(Time.frameCount, Time.unscaledDeltaTime, lookRotationDelta);

				// Calculate smooth look rotation delta.
				lookRotationDelta = smoothVector.CalculateSmoothValue(responsivity, Time.unscaledDeltaTime);
			}

			return lookRotationDelta;
		}

		public static float GetLookInputSensitivityScale()
		{
			return 1.0f;
		}

		public static float GetLookInputSensitivityScale(InputControl lookInputControl)
		{
			return 1.0f;
		}

		public static float GetControllerLookDeltaScale()
		{
			return Time.unscaledDeltaTime * CONTROLLER_LOOK_REFERENCE_HZ;
		}

		public static float GetLookDeltaScale()
		{
			return LOOK_DELTA_SCALE;
		}

		public static float GetGameplayLookSensitivity(float sensitivity)
		{
			return Mathf.Max(0.0f, sensitivity);
		}

		public static float GetLookDeviceNormalizationScale(InputControl lookInputControl)
		{
			if (lookInputControl != null && lookInputControl.device is Gamepad)
				return GetControllerLookDeltaScale();

			return 1.0f;
		}

		public static Vector2 GetAutoLeanLookInput(Vector2 rawLookInput, InputControl lookInputControl)
		{
			if (lookInputControl != null && lookInputControl.device is Gamepad)
			{
				return new Vector2(
					Mathf.Clamp(rawLookInput.x, -1.0f, 1.0f),
					Mathf.Clamp(rawLookInput.y, -1.0f, 1.0f));
			}

			float referenceDelta = Mathf.Max(0.0001f, MOUSE_AUTO_LEAN_REFERENCE_DELTA);
			return new Vector2(
				Mathf.Clamp(rawLookInput.x / referenceDelta, -1.0f, 1.0f),
				Mathf.Clamp(rawLookInput.y / referenceDelta, -1.0f, 1.0f));
		}

		public static float PixelsToCentimeters(float pixels)
		{
			return (pixels * INCH_TO_CM) / Screen.dpi;
		}

		public static Vector2 PixelsToCentimeters(Vector2 pixels)
		{
			return (pixels * INCH_TO_CM) / Screen.dpi;
		}
	}
}
