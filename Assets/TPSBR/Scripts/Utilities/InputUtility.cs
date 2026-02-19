namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine.InputSystem;

	using UnityEngine;
	using Fusion.Addons.KCC;

	public static partial class InputUtility
	{
		// CONSTANTS

		private const float INCH_TO_CM = 2.54f;

		// PUBLIC METHODS

		public static Vector2 GetSmoothLookRotationDelta(SmoothVector2 smoothVector, Vector2 lookRotationDelta, float sensitivity, float responsivity)
		{
			lookRotationDelta *= sensitivity;

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

		public static float PixelsToCentimeters(float pixels)
		{
			return (pixels * INCH_TO_CM) / Screen.dpi;
		}

		public static Vector2 PixelsToCentimeters(Vector2 pixels)
		{
			return (pixels * INCH_TO_CM) / Screen.dpi;
		}
	}

	/// <summary>
	/// Normalizes raw pointer delta to a stick-like Vector2 range for look input.
	/// </summary>
	public sealed class MouseLookNormalizationProcessor : InputProcessor<Vector2>
	{
		// Configured for a full-stick equivalent to be reached around 10% of the screen span at 100 DPI.
		public float OutputScale = 15.0f;
		public float HorizontalScreenSpan = 0.1f;
		public float VerticalScreenSpan = 0.1f;
		public float Deadzone = 0.0f;
		public float DpiFallback = 96.0f;

		public override Vector2 Process(Vector2 value, InputControl control)
		{
			float dpi = Screen.dpi > 0.0f ? Screen.dpi : Mathf.Max(0.0001f, DpiFallback);
			Vector2 cm = value * (2.54f / dpi);

			float screenWidthCm = Mathf.Max(1.0f, Screen.width * (2.54f / dpi));
			float screenHeightCm = Mathf.Max(1.0f, Screen.height * (2.54f / dpi));
			float horizontalSpan = Mathf.Max(0.0001f, screenWidthCm * Mathf.Max(0.0001f, HorizontalScreenSpan));
			float verticalSpan = Mathf.Max(0.0001f, screenHeightCm * Mathf.Max(0.0001f, VerticalScreenSpan));
			float normalizedOutputScale = Mathf.Max(0.0001f, OutputScale);

			Vector2 normalized = new Vector2(cm.x * (normalizedOutputScale / horizontalSpan), cm.y * (normalizedOutputScale / verticalSpan));
			normalized.x = ApplyDeadzoneAndClamp(normalized.x);
			normalized.y = ApplyDeadzoneAndClamp(normalized.y);

			return normalized;
		}

		public override string ToString()
		{
			return $"MouseLookNormalization(outputScale={OutputScale},horizontalScreenSpan={HorizontalScreenSpan},verticalScreenSpan={VerticalScreenSpan},deadzone={Deadzone})";
		}

		private float ApplyDeadzoneAndClamp(float value)
		{
			float abs = Mathf.Abs(value);
			if (abs <= Deadzone)
			{
				return 0.0f;
			}

			if (abs > OutputScale)
			{
				return OutputScale * Mathf.Sign(value);
			}

			return value;
		}
	}

	/// <summary>
	/// Converts a right-stick Vector2 value into absolute screen-space UI point coordinates.
	/// </summary>
	public sealed class GamepadToUIScreenPointProcessor : InputProcessor<Vector2>
	{
		public float CursorSpeedPxPerSecond = 1600.0f;
		public bool InvertY = true;
		public float Deadzone = 0.08f;

		private static readonly Dictionary<int, Vector2> _cursorPositionsByDeviceId = new();

		public override Vector2 Process(Vector2 value, InputControl control)
		{
			if (Screen.width <= 1 || Screen.height <= 1)
				return value;

			int deviceId = control != null && control.device != null
				? control.device.deviceId
				: -1;

			Vector2 cursor = GetOrInitCursor(deviceId);

			if (value.sqrMagnitude <= Deadzone * Deadzone)
			{
				return cursor;
			}

			float dt = Mathf.Max(0.0001f, Time.unscaledDeltaTime);
			float xDelta = value.x * CursorSpeedPxPerSecond * dt;
			float yDelta = value.y * CursorSpeedPxPerSecond * dt * (InvertY ? -1.0f : 1.0f);

			cursor.x = Mathf.Clamp(cursor.x + xDelta, 0.0f, Mathf.Max(0.0f, (float)Screen.width - 1.0f));
			cursor.y = Mathf.Clamp(cursor.y + yDelta, 0.0f, Mathf.Max(0.0f, (float)Screen.height - 1.0f));

			_cursorPositionsByDeviceId[deviceId] = cursor;
			return cursor;
		}

		private static Vector2 GetOrInitCursor(int deviceId)
		{
			if (_cursorPositionsByDeviceId.TryGetValue(deviceId, out Vector2 cursor) == true)
				return cursor;

			float halfWidth = Mathf.Max(1.0f, (float)Screen.width * 0.5f);
			float halfHeight = Mathf.Max(1.0f, (float)Screen.height * 0.5f);

			cursor = new Vector2(halfWidth, halfHeight);
			_cursorPositionsByDeviceId[deviceId] = cursor;
			return cursor;
		}
	}
}
