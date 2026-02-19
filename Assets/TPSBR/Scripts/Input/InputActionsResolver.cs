using UnityEngine;
using UnityEngine.InputSystem;

namespace TPSBR
{
	public static class InputActionsResolver
	{
		private static bool _didLogMissingActionAsset;

		public static InputActionAsset ResolveActionAsset()
		{
			InputActionAsset configuredActions = Global.Settings != null ? Global.Settings.PlayerInputActions : null;
			if (configuredActions != null)
				return configuredActions;

			if (_didLogMissingActionAsset == false)
			{
				_didLogMissingActionAsset = true;
				Debug.LogError("Global.Settings.PlayerInputActions is not assigned. Configure it in Global Settings before input is used.");
			}
			return null;
		}

		public static InputAction FindAndEnable(InputActionAsset asset, string actionName)
		{
			if (asset == null || string.IsNullOrWhiteSpace(actionName))
				return null;

			InputAction action = asset.FindAction(actionName, false);
			if (action != null && action.enabled == false)
			{
				action.Enable();
			}

			return action;
		}
	}
}
