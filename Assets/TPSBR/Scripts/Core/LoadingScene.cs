using TPSBR.UI;
using UnityEngine;
using UnityEngine.InputSystem;
using TMPro;

namespace TPSBR
{
	public class LoadingScene : MonoBehaviour
	{
		// PUBLIC MEMBERS

		public bool IsFading => _activeFader != null && _activeFader.IsFinished == false;

		// PRIVATE MEMBERS

		[SerializeField]
		private UIFader _fadeInObject;
		[SerializeField]
		private UIFader _fadeOutObject;
		[SerializeField]
		private TextMeshProUGUI _status;
		[SerializeField]
		private TextMeshProUGUI _statusDescription;
		[SerializeField]
		private UIYesNoDialogView _dialog;

		private UIFader _activeFader;
		private InputActionAsset _actionsAsset;
		private InputAction _escapeAction;

		// PUBLIC METHODS

		public void FadeIn()
		{
			_fadeInObject.SetActive(true);
			_fadeOutObject.SetActive(false);

			_activeFader = _fadeInObject;
		}

		public void FadeOut()
		{
			_dialog.Close_Internal();

			_fadeInObject.SetActive(false);
			_fadeOutObject.SetActive(true);

			_activeFader = _fadeOutObject;
		}

		// MONOBEHAVIOUR

		protected void Awake()
		{
			_fadeInObject.SetActive(false);
			_fadeOutObject.SetActive(false);

			_dialog.Initialize(null, null);
		}

		protected void Update()
		{
			ResolveInputActions();

			_status.text = Global.Networking.Status;
			_statusDescription.text = Global.Networking.StatusDescription;

			if (_escapeAction != null && _escapeAction.WasPressedThisFrame())
			{
				_dialog.Open_Internal();

				Cursor.lockState = CursorLockMode.None;
				Cursor.visible   = true;

				_dialog.HasClosed += (result) =>
				{
					if (result == true)
					{
						Global.Networking.StopGame();
					}
				};
			}
		}

		protected void OnDestroy()
		{
			if (_dialog != null)
			{
				_dialog.Deinitialize();
			}
		}

		private void ResolveInputActions()
		{
			if (_actionsAsset == null)
			{
				_actionsAsset = InputActionsResolver.ResolveActionAsset();
			}

			if (_actionsAsset == null)
				return;

			_escapeAction ??= InputActionsResolver.FindAndEnable(_actionsAsset, "Escape");
		}
	}
}
