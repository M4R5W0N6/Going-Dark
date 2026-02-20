namespace TPSBR
{
	using UnityEngine;
	using UnityEngine.InputSystem;

	/// <summary>
	/// Converts absolute screen-space input values into normalized coordinates.
	/// </summary>
	#if UNITY_EDITOR
	[UnityEditor.InitializeOnLoad]
	#endif
	public sealed class AbsoluteToNormalized : InputProcessor<Vector2>
	{
		public float OutputScale = 1.0f;
		public float Deadzone = 0.0f;
		public bool InvertY = true;

		private const float MIN_VALUE = 0.0001f;

		private int _cachedFrame = -1;
		private int _cachedScreenWidth;
		private int _cachedScreenHeight;
		private float _cachedScale;

		private float _scaleX;
		private float _scaleY;

		static AbsoluteToNormalized()
		{
			Register();
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
		private static void Register()
		{
			InputSystem.RegisterProcessor<AbsoluteToNormalized>();
		}

		public override Vector2 Process(Vector2 value, InputControl control)
		{
			int screenWidth = Screen.width;
			int screenHeight = Screen.height;

			if (screenWidth <= 1 || screenHeight <= 1)
				return value;

			float deadzoneSqr = Deadzone * Deadzone;
			if (deadzoneSqr > 0.0f && value.sqrMagnitude <= deadzoneSqr)
				return Vector2.zero;

			UpdateScaleCache(screenWidth, screenHeight);

			float y = value.y * _scaleY;
			if (InvertY == true)
			{
				y = -y;
			}

			return new Vector2(value.x * _scaleX, y);
		}

		private void UpdateScaleCache(int screenWidth, int screenHeight)
		{
			float outputScale = Mathf.Max(MIN_VALUE, OutputScale);

			if (_cachedFrame == Time.frameCount &&
				_cachedScreenWidth == screenWidth &&
				_cachedScreenHeight == screenHeight &&
				_cachedScale == outputScale)
			{
				return;
			}

			float width = Mathf.Max(1.0f, (float)screenWidth);
			float height = Mathf.Max(1.0f, (float)screenHeight);

			_scaleX = outputScale / width;
			_scaleY = outputScale / height;

			_cachedScale = outputScale;
			_cachedScreenWidth = screenWidth;
			_cachedScreenHeight = screenHeight;
			_cachedFrame = Time.frameCount;
		}
	}
}
