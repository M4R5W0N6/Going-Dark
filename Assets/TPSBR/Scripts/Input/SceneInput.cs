using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;

namespace TPSBR
{
	[Flags]
	public enum ECursorStateSource
	{
		None,
		UI,
		Menu,
		Agent,
	}

	public class SceneInput : SceneService
	{
		// PUBLIC MEMBERS

		public bool IsCursorVisible => _cursorVisibilitySources != ECursorStateSource.None;

		// PRIVATE MEMBERS

		private List<IBackHandler> _backHandlers = new List<IBackHandler>();
		private ECursorStateSource _cursorVisibilitySources;

		private bool _hasInput;
		private InputActionAsset _actionsAsset;
		private InputAction _escapeAction;
		private InputAction _middleClickAction;
		private InputActionMap _uiActionMap;
		private InputSystemUIInputModule[] _uiInputModules;
		private bool _isUISystemInputEnabled;
		private bool _isUISystemInputInitialized;

		// PUBLIC METHODS

		public void RequestCursorVisibility(bool isVisible, ECursorStateSource source, bool force = true)
		{
			if (source == ECursorStateSource.None)
				return;

			var previousSources = _cursorVisibilitySources;

			if (isVisible == true)
			{
				_cursorVisibilitySources = _cursorVisibilitySources | source;
			}
			else
			{
				_cursorVisibilitySources = _cursorVisibilitySources & ~source;
			}

			if (_cursorVisibilitySources != previousSources || force == true)
			{
				RefreshCursor();
			}
		}

		public void ClearCursorLock()
		{
			_cursorVisibilitySources = ECursorStateSource.None;
			RefreshCursor();
		}

		public void TrigggerBackAction()
		{
			BackAction();
		}

		// SceneService INTERFACE

		protected override void OnActivate()
		{
			ResolveInputActions();
			UpdateUISystemInput();
		}

		protected override void OnTick()
		{
			base.OnTick();
			ResolveInputActions();
			UpdateUISystemInput();

			if (Context.Runner != null)
			{
				if (ApplicationSettings.IsStrippedBatch == true && ApplicationSettings.GenerateInput == true)
				{
					Context.Runner.ProvideInput = true;
				}
				else
				{
					Context.Runner.ProvideInput = Context.HasInput;
				}
			}

			if (Context.HasInput == true || Scene is Menu)
			{
				if (_escapeAction != null && _escapeAction.WasPressedThisFrame())
				{
					BackAction();
				}
			}

			if (Context.HasInput == true)
			{
				bool toggleCursor = _middleClickAction != null && _middleClickAction.WasPressedThisFrame();
				if (toggleCursor == true)
				{
					RequestCursorVisibility(IsCursorVisible == false, ECursorStateSource.Agent);
				}
			}

			if (_hasInput != Context.HasInput)
			{
				// Refresh cursor when input changed (e.g. when switching between peers)
				RefreshCursor();

				_hasInput = Context.HasInput;
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
			_middleClickAction ??= InputActionsResolver.FindAndEnable(_actionsAsset, "MiddleClick");
			_uiActionMap ??= _actionsAsset.FindActionMap("UI", false);
			if (_uiActionMap != null && IsCursorVisible == false && _uiActionMap.enabled == true)
			{
				_uiActionMap.Disable();
			}
		}

		private void UpdateUISystemInput()
		{
			if (_uiInputModules == null || _uiInputModules.Length == 0)
			{
				_uiInputModules = FindObjectsByType<InputSystemUIInputModule>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			}

			if (_uiActionMap != null)
			{
				if (IsCursorVisible == true && _uiActionMap.enabled == false)
				{
					_uiActionMap.Enable();
				}
				else if (IsCursorVisible == false && _uiActionMap.enabled == true)
				{
					_uiActionMap.Disable();
				}
			}

			// Keep UI input module active only when the cursor is visible so gameplay mouse aiming
			// doesn't pay extra per-frame pointer action processing.
			if (_uiInputModules != null && _uiInputModules.Length > 0)
			{
				bool shouldEnable = IsCursorVisible;
				if (_isUISystemInputInitialized == false || _isUISystemInputEnabled != shouldEnable)
				{
					for (int i = 0, count = _uiInputModules.Length; i < count; i++)
					{
						InputSystemUIInputModule uiInputModule = _uiInputModules[i];
						if (uiInputModule != null && uiInputModule.enabled != shouldEnable)
						{
							uiInputModule.enabled = shouldEnable;
						}
					}

					_isUISystemInputEnabled = shouldEnable;
					_isUISystemInputInitialized = true;
				}
			}
		}

		// PRIVATE METHODS

		private void BackAction()
		{
			if (_backHandlers.Count == 0)
			{
				Context.UI.GetAll(_backHandlers);
				_backHandlers.Add(Context.UI);

				_backHandlers.Sort((a, b) => b.Priority.CompareTo(a.Priority));
			}

			for (int i = 0, count = _backHandlers.Count; i < count; ++i)
			{
				IBackHandler handler = _backHandlers[i];
				if (handler.IsActive == true && handler.OnBackAction() == true)
					break;
			}
		}

		private void RefreshCursor()
		{
			if (IsActive == false)
				return;

			if (Context != null && Context.HasInput == false)
				return;

			if (IsCursorVisible == true)
			{
				Cursor.lockState = CursorLockMode.None;
				Cursor.visible   = true;
			}
			else
			{
				Cursor.lockState = CursorLockMode.Locked;
				Cursor.visible = false;
			}
		}
	}
}

