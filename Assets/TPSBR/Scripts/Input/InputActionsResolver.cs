using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace TPSBR
{
	public static class InputActionsResolver
	{
		public static InputActionAsset ResolveActionAsset()
		{
			PlayerInput playerInput = Object.FindFirstObjectByType<PlayerInput>(FindObjectsInactive.Include);
			if (playerInput != null && playerInput.actions != null)
				return playerInput.actions;

			InputSystemUIInputModule inputModule = Object.FindFirstObjectByType<InputSystemUIInputModule>(FindObjectsInactive.Include);
			if (inputModule != null)
				return inputModule.actionsAsset;

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
