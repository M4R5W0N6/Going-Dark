using UnityEngine;

namespace TPSBR
{
	public class SceneCamera : SceneService
	{
		// PUBLIC MEMBERS

		public Camera      Camera        => _camera;
		public ShakeEffect ShakeEffect   => _shakeEffect;
		public bool        EnableCamera  { get; set; } = true;

		// PRIVATE MEMBERS

		[SerializeField]
		private Camera _camera;
		[SerializeField]
		private AudioListener _audioListener;
		[SerializeField]
		private ShakeEffect _shakeEffect;

		private int _cameraCullingMask;
		private int _fowLayer = -1;

		// SceneService INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			_cameraCullingMask = _camera.cullingMask;
			_fowLayer = LayerMask.NameToLayer("FoW");
			if (_fowLayer >= 0)
			{
				_cameraCullingMask |= 1 << _fowLayer;
			}
		}

		protected override void OnTick()
		{
			if (Scene is Gameplay)
			{
				_audioListener.enabled = Context.HasInput;
				_camera.enabled = Context.HasInput;

				// We are just switching culling mask as disabling would mean more complex camera setup to not stop UI rendering
				int cullingMask = _cameraCullingMask;
				if (_fowLayer >= 0)
				{
					cullingMask |= 1 << _fowLayer;
				}
				_camera.cullingMask = EnableCamera == true ? cullingMask : 0;
			}
		}
	}
}
