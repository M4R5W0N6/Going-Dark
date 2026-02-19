namespace TPSBR
{
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.InputSystem;

	/// <summary>
	/// Converts a normalized Vector2 value into absolute screen-space UI point coordinates.
	/// </summary>
	#if UNITY_EDITOR
	[UnityEditor.InitializeOnLoad]
	#endif
	public sealed class NormalizedToAbsolute : InputProcessor<Vector2>
	{
		public float OutputScale = 1.0f;
		public float Deadzone = 0.08f;
		public bool InvertY = true;

		private const float MIN_VALUE = 0.0001f;

		private int _cachedFrame = -1;
		private int _cachedScreenWidth;
		private int _cachedScreenHeight;
		private float _cachedScale;
		private float _scaleX;
		private float _scaleY;

		private sealed class CursorState
		{
			public Vector2 Position;
			public int ScreenWidth;
			public int ScreenHeight;
		}

		private static readonly Dictionary<int, CursorState> _cursorStatesByDeviceId = new();

		static NormalizedToAbsolute()
		{
			Register();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register()
		{
			InputSystem.RegisterProcessor<NormalizedToAbsolute>();
		}

		public override Vector2 Process(Vector2 value, InputControl control)
		{
			int screenWidth = Screen.width;
			int screenHeight = Screen.height;

			if (screenWidth <= 1 || screenHeight <= 1)
				return value;

			float deadzoneSqr = Deadzone * Deadzone;
			if (deadzoneSqr > 0.0f && value.sqrMagnitude <= deadzoneSqr)
			{
				return GetCursorState(screenWidth, screenHeight, GetDeviceId(control)).Position;
			}

			float sensitivityScale = GetSensitivityScale();
			UpdateScaleCache(sensitivityScale, screenWidth, screenHeight);

			CursorState cursor = GetCursorState(screenWidth, screenHeight, GetDeviceId(control));
			float x = cursor.Position.x + (value.x * _scaleX);
			float y = cursor.Position.y + (value.y * _scaleY * (InvertY == true ? -1.0f : 1.0f));

			float maxX = Mathf.Max(0.0f, (float)screenWidth - 1.0f);
			float maxY = Mathf.Max(0.0f, (float)screenHeight - 1.0f);

			if (x < 0.0f)
				x = 0.0f;
			else if (x > maxX)
				x = maxX;

			if (y < 0.0f)
				y = 0.0f;
			else if (y > maxY)
				y = maxY;

			cursor.Position.x = x;
			cursor.Position.y = y;
			return cursor.Position;
		}

		private float GetSensitivityScale()
		{
			if (Global.RuntimeSettings == null)
				return 1.0f;

			return Mathf.Max(MIN_VALUE, Global.RuntimeSettings.Sensitivity);
		}

		private void UpdateScaleCache(float sensitivityScale, int screenWidth, int screenHeight)
		{
			float outputScale = Mathf.Max(MIN_VALUE, OutputScale * Mathf.Max(MIN_VALUE, sensitivityScale));
			if (_cachedFrame == Time.frameCount &&
				_cachedScreenWidth == screenWidth &&
				_cachedScreenHeight == screenHeight &&
				_cachedScale == outputScale)
			{
				return;
			}

			float width = (float)Mathf.Max(1, screenWidth);
			float height = (float)Mathf.Max(1, screenHeight);
			float oneOverScale = 1.0f / outputScale;

			_scaleX = width * oneOverScale;
			_scaleY = height * oneOverScale;

			_cachedScale = outputScale;
			_cachedScreenWidth = screenWidth;
			_cachedScreenHeight = screenHeight;
			_cachedFrame = Time.frameCount;
		}

		private static int GetDeviceId(InputControl control)
		{
			if (control != null && control.device != null)
				return control.device.deviceId;

			return -1;
		}

		private static CursorState GetCursorState(int screenWidth, int screenHeight, int deviceId)
		{
			if (_cursorStatesByDeviceId.TryGetValue(deviceId, out CursorState cursorState) == false)
			{
				cursorState = new CursorState();
				cursorState.Position = new Vector2(screenWidth * 0.5f, screenHeight * 0.5f);
				cursorState.ScreenWidth = screenWidth;
				cursorState.ScreenHeight = screenHeight;
				_cursorStatesByDeviceId[deviceId] = cursorState;
				return cursorState;
			}

			if (cursorState.ScreenWidth != screenWidth || cursorState.ScreenHeight != screenHeight)
			{
				cursorState.Position.x = Mathf.Clamp(cursorState.Position.x, 0.0f, Mathf.Max(0.0f, screenWidth - 1.0f));
				cursorState.Position.y = Mathf.Clamp(cursorState.Position.y, 0.0f, Mathf.Max(0.0f, screenHeight - 1.0f));
				cursorState.ScreenWidth = screenWidth;
				cursorState.ScreenHeight = screenHeight;
			}

			return cursorState;
		}
	}
}
