namespace TPSBR.UI
{
	using System;
	using System.Collections.Generic;
	using UnityEngine;
	using UnityEngine.EventSystems;
	using UnityEngine.InputSystem;
	using UnityEngine.InputSystem.Controls;

	public sealed class UIMobileInputView : UIView
	{
		// PUBLIC MEMBERS

		public Vector2 Move     { get; set; }
		public Vector2 Look     { get; set; }
		public bool    Fire     { get; set; }
		public bool    Jump     { get; set; }
		public bool    Interact { get; set; }

		// PRIVATE MEMBERS

		[SerializeField]
		private bool          _resetMoveJoystickAfterMove;
		[SerializeField]
		private float         _joystickRadius;
		[SerializeField]
		private bool          _moveJoystickOrigin;

		[Header("References")]
		[SerializeField]
		private UIBehaviour   _root;
		[SerializeField]
		private RectTransform _move;
		[SerializeField]
		private RectTransform _look;
		[SerializeField]
		private RectTransform _jump;
		[SerializeField]
		private RectTransform _fire;
		[SerializeField]
		private RectTransform _interact;
		[SerializeField]
		private RectTransform _joystick;
		[SerializeField]
		private UIBehaviour   _joystickOrigin;

		private const int NoPointer = -1;

		private int     _movePointerID = NoPointer;
		private Vector2 _movePosition;
		private Vector2 _moveOrigin;

		private int     _lookPointerID = NoPointer;

		private int     _firePointerID     = NoPointer;
		private int     _jumpPointerID     = NoPointer;
		private int     _interactPointerID = NoPointer;
		private bool    _isFiring;
		private bool    _isJumping;
		private bool    _isInteracting;

		private Vector2 _joystickInitialPosition;

		private Rect    _moveRect;
		private Rect    _lookRect;
		private Rect    _fireRect;
		private Rect    _jumpRect;
		private Rect    _interactRect;

		private List<RectTransform> _ignoredAreas    = new List<RectTransform>();
		private List<Rect>          _ignoredRects    = new List<Rect>();

		private Dictionary<int, Vector2> _touchPositionsByPointer = new Dictionary<int, Vector2>();
		private Dictionary<int, Vector2> _touchDeltasByPointer = new Dictionary<int, Vector2>();

		private InputActionAsset _actionsAsset;
		private InputAction _pointAction;
		private InputAction _deltaAction;
		private InputAction _clickAction;
		private bool _isSubscribed;
		private bool _isMobileInputEnabled;

		// PUBLIC METHODS

		public void RegisterIgnoredArea(RectTransform transform)
		{
			if (_ignoredAreas.Contains(transform) == true)
				return;

			_ignoredAreas.Add(transform);
			_ignoredRects.Add(GetScreenSpaceRect(transform));
		}

		public void UnregisterIgnoredArea(RectTransform transform)
		{
			int index = _ignoredAreas.IndexOf(transform);
			if (index < 0)
				return;

			_ignoredAreas.RemoveBySwap(index);
			_ignoredRects.RemoveBySwap(index);
		}

		// UIView INTERFACE

		protected override void OnInitialize()
		{
			_joystickInitialPosition = _joystick.position;
			_joystickOrigin.CanvasGroup.alpha = 0.0f;

			ToggleState(false);
			ResetRuntimeState();

			_isMobileInputEnabled = ShouldProcessMobileInput();
			if (_isMobileInputEnabled == true)
			{
				ResolveInputActions();
				SubscribeToInputActions();
			}
		}

		protected override void OnVisible()
		{
			_moveRect     = GetScreenSpaceRect(_move);
			_lookRect     = GetScreenSpaceRect(_look);
			_fireRect     = GetScreenSpaceRect(_fire);
			_jumpRect     = GetScreenSpaceRect(_jump);
			_interactRect = GetScreenSpaceRect(_interact);

			_movePointerID = NoPointer;
			_lookPointerID = NoPointer;

			_firePointerID     = NoPointer;
			_jumpPointerID     = NoPointer;
			_interactPointerID = NoPointer;

			_isFiring      = false;
			_isJumping     = false;
			_isInteracting = false;

			Fire     = false;
			Jump     = false;
			Interact = false;
		}

		protected override void OnHidden()
		{
			ResetRuntimeState();
		}

		protected override void OnDeinitialize()
		{
			UnsubscribeFromInputActions();
			ResetRuntimeState();
		}

		protected override void OnTick()
		{
			bool shouldProcessMobileInput = ShouldProcessMobileInput();
			if (_isMobileInputEnabled != shouldProcessMobileInput)
			{
				_isMobileInputEnabled = shouldProcessMobileInput;
				if (_isMobileInputEnabled == true)
				{
					ResolveInputActions();
				}
				else
				{
					UnsubscribeFromInputActions();
				}
			}

			if (_isMobileInputEnabled == false)
			{
				ToggleState(false);
				return;
			}

			if (_root.CanvasGroup.IsActive() == false && Context.LocalPlayerRef == Context.ObservedPlayerRef && Context.LocalPlayerRef.IsRealPlayer == true)
			{
				ToggleState(true);
			}
			else if (_root.CanvasGroup.IsActive() == true && (Context.LocalPlayerRef != Context.ObservedPlayerRef || Context.LocalPlayerRef.IsRealPlayer == false))
			{
				ToggleState(false);
			}

			_moveRect     = GetScreenSpaceRect(_move);
			_lookRect     = GetScreenSpaceRect(_look);
			_fireRect     = GetScreenSpaceRect(_fire);
			_jumpRect     = GetScreenSpaceRect(_jump);
			_interactRect = GetScreenSpaceRect(_interact);

			ResolveInputActions();
			SubscribeToInputActions();

			Move = default;
			Look = default;

			if (_movePointerID >= 0)
			{
				if (_touchPositionsByPointer.TryGetValue(_movePointerID, out Vector2 movePointerPosition) == true)
				{
					_movePosition = movePointerPosition;

					Vector2 direction = _movePosition - _moveOrigin;
					float   scaledRadius = _joystickRadius * transform.lossyScale.x;

					if (scaledRadius > 0.0f && direction.sqrMagnitude > scaledRadius * scaledRadius)
					{
						if (_moveJoystickOrigin == true)
						{
							_joystick.position = _movePosition;
							_moveOrigin       = _movePosition - scaledRadius * direction.normalized;
							_joystickOrigin.transform.position = _moveOrigin;
						}
						else
						{
							_joystick.position = _moveOrigin + direction.normalized * scaledRadius;
						}
					}
					else
					{
						_joystick.position = _movePosition;
					}

					Move = InputUtility.PixelsToCentimeters(_movePosition - _moveOrigin);
				}
			}

			if (_lookPointerID >= 0)
			{
				if (_touchDeltasByPointer.TryGetValue(_lookPointerID, out Vector2 lookDelta) == true)
				{
					Look += InputUtility.PixelsToCentimeters(lookDelta);
					_touchDeltasByPointer[_lookPointerID] = default;
				}

				if (_isFiring      == true) { Fire     = true; }
				if (_isJumping     == true) { Jump     = true; }
				if (_isInteracting == true) { Interact = true; }
			}
			else
			{
				Fire          = false; _isFiring      = false;
				Jump          = false; _isJumping     = false;
				Interact      = false; _isInteracting = false;
			}
		}

		// PRIVATE METHODS

		private void ResolveInputActions()
		{
			if (_isMobileInputEnabled == false)
				return;

			if (_actionsAsset == null)
			{
				_actionsAsset = InputActionsResolver.ResolveActionAsset();
			}

			if (_actionsAsset == null)
				return;

			_pointAction ??= InputActionsResolver.FindAndEnable(_actionsAsset, "UI/Point");
			_deltaAction ??= InputActionsResolver.FindAndEnable(_actionsAsset, "UI/Delta");
			_clickAction ??= InputActionsResolver.FindAndEnable(_actionsAsset, "UI/Click");
		}

		private void SubscribeToInputActions()
		{
			if (_isMobileInputEnabled == false)
				return;

			if (_isSubscribed == true)
				return;

			if (_pointAction == null || _deltaAction == null || _clickAction == null)
				return;

			_pointAction.performed += OnPointAction;
			_pointAction.canceled  += OnPointEnded;

			_deltaAction.performed += OnDeltaAction;

			_clickAction.performed += OnClickAction;
			_clickAction.canceled  += OnClickAction;

			_isSubscribed = true;
		}

		private void UnsubscribeFromInputActions()
		{
			if (_isSubscribed == false)
				return;

			if (_pointAction != null)
			{
				_pointAction.performed -= OnPointAction;
				_pointAction.canceled  -= OnPointEnded;
			}

			if (_deltaAction != null)
			{
				_deltaAction.performed -= OnDeltaAction;
			}

			if (_clickAction != null)
			{
				_clickAction.performed -= OnClickAction;
				_clickAction.canceled  -= OnClickAction;
			}

			_isSubscribed = false;
		}

		private void OnPointAction(InputAction.CallbackContext context)
		{
			int    pointerId = GetPointerIdFromControl(context.control);
			Vector2 point    = context.ReadValue<Vector2>();

			if (pointerId < 0)
				return;

			_touchPositionsByPointer[pointerId] = point;
		}

		private void OnPointEnded(InputAction.CallbackContext context)
		{
			int pointerId = GetPointerIdFromControl(context.control);

			if (pointerId < 0)
				return;

			_touchPositionsByPointer.Remove(pointerId);
			_touchDeltasByPointer.Remove(pointerId);

			if (pointerId == _movePointerID)
			{
				ClearMovePointer();
			}
			else if (pointerId == _lookPointerID)
			{
				ClearLookPointer();
			}

			OnTouchReleased(pointerId);
		}

		private void OnDeltaAction(InputAction.CallbackContext context)
		{
			int    pointerId = GetPointerIdFromControl(context.control);
			Vector2 delta    = context.ReadValue<Vector2>();

			if (pointerId < 0)
				return;

			if (_touchDeltasByPointer.ContainsKey(pointerId) == false)
			{
				_touchDeltasByPointer.Add(pointerId, delta);
			}
			else
			{
				_touchDeltasByPointer[pointerId] += delta;
			}
		}

		private void OnClickAction(InputAction.CallbackContext context)
		{
			int pointerId = GetPointerIdFromControl(context.control);

			if (pointerId < 0)
				return;

			float value = context.ReadValue<float>();

			if (value > 0.5f)
			{
				OnTouchPressed(pointerId, GetPointerPosition(pointerId, context));
			}
			else
			{
				OnTouchReleased(pointerId);
			}
		}

		private void OnTouchPressed(int pointerId, Vector2 pointerPosition)
		{
			if (_ignoredAreas.Count > 0)
			{
				for (int i = 0; i < _ignoredRects.Count; i++)
				{
					Rect ignoredRect = _ignoredRects[i];
					if (ignoredRect.Contains(pointerPosition) == true)
					{
						return;
					}
				}
			}

			if (pointerId == _movePointerID || pointerId == _lookPointerID || pointerId == _firePointerID || pointerId == _jumpPointerID || pointerId == _interactPointerID)
				return;

			if (_fireRect.Contains(pointerPosition) == true)
			{
				_firePointerID = pointerId;
				_isFiring      = true;
				Fire          = true;
			}
			else if (_jumpRect.Contains(pointerPosition) == true)
			{
				_jumpPointerID = pointerId;
				_isJumping    = true;
				Jump          = true;
			}
			else if (_interactRect.Contains(pointerPosition) == true)
			{
				_interactPointerID = pointerId;
				_isInteracting     = true;
				Interact           = true;
			}
			else if (_moveRect.Contains(pointerPosition) == true && _movePointerID < 0)
			{
				_movePointerID = pointerId;
				_movePosition  = pointerPosition;
				_moveOrigin    = _movePosition;
				_joystick.position = _movePosition;
				_joystickOrigin.RectTransform.position = _movePosition;
				_joystickOrigin.CanvasGroup.alpha = 1.0f;
			}
			else if (_lookRect.Contains(pointerPosition) == true && _lookPointerID < 0)
			{
				_lookPointerID = pointerId;
				_touchDeltasByPointer[_lookPointerID] = default;
			}
		}

		private void OnTouchReleased(int pointerId)
		{
			if (pointerId == _movePointerID)
			{
				ClearMovePointer();
			}
			else if (pointerId == _lookPointerID)
			{
				ClearLookPointer();
			}
			else if (pointerId == _firePointerID)
			{
				_firePointerID = NoPointer;
				_isFiring     = false;
				Fire         = false;
			}
			else if (pointerId == _jumpPointerID)
			{
				_jumpPointerID = NoPointer;
				_isJumping    = false;
				Jump          = false;
			}
			else if (pointerId == _interactPointerID)
			{
				_interactPointerID = NoPointer;
				_isInteracting    = false;
				Interact          = false;
			}
		}

		private void ClearMovePointer()
		{
			_movePointerID = NoPointer;
			if (_resetMoveJoystickAfterMove == true)
			{
				_joystick.position = _joystickInitialPosition;
			}
			else
			{
				_joystick.position = _moveOrigin;
			}

			_joystickOrigin.CanvasGroup.alpha = 0.0f;
			Move = default;
		}

		private void ClearLookPointer()
		{
			int pointerId = _lookPointerID;
			_lookPointerID = NoPointer;
			Look = default;

			_touchDeltasByPointer.Remove(pointerId);
		}

		private void ResetRuntimeState()
		{
			Move     = default;
			Look     = default;
			Fire     = false;
			Jump     = false;
			Interact = false;

			_movePointerID = NoPointer;
			_lookPointerID = NoPointer;
			_firePointerID = NoPointer;
			_jumpPointerID = NoPointer;
			_interactPointerID = NoPointer;

			_isFiring      = false;
			_isJumping     = false;
			_isInteracting = false;

			_movePosition = default;
			_moveOrigin   = default;

			_joystick.position = _joystickInitialPosition;
			_joystickOrigin.CanvasGroup.alpha = 0.0f;

			_touchPositionsByPointer.Clear();
			_touchDeltasByPointer.Clear();
		}

		private Vector2 GetPointerPosition(int pointerId, InputAction.CallbackContext context)
		{
			if (pointerId >= 0 && _touchPositionsByPointer.TryGetValue(pointerId, out Vector2 position) == true)
			{
				return position;
			}

			var pressedControl = context.control;
			if (pressedControl != null)
			{
				var device = pressedControl.device;
				if (device != null)
				{
					var positionControl = device["position"];
					if (positionControl != null && positionControl.valueType == typeof(Vector2))
					{
						return ((InputControl<Vector2>)positionControl).ReadValue();
					}
				}
			}

			if (_pointAction != null)
			{
				for (int i = 0, count = _pointAction.controls.Count; i < count; ++i)
				{
					InputControl control = _pointAction.controls[i];
					if (GetPointerIdFromControl(control) != pointerId)
					{
						continue;
					}

					if (control.valueType == typeof(Vector2))
					{
						return ((InputControl<Vector2>)control).ReadValue();
					}
				}
			}

			return default;
		}

		private int GetPointerIdFromControl(InputControl control)
		{
			if (control == null || string.IsNullOrEmpty(control.path) == true)
				return NoPointer;

			if (control is TouchControl touchControl)
			{
				return touchControl.touchId.ReadValue();
			}

			string path = control.path;
			const string touchPrefix = "touch";
			int touchPrefixIndex = path.IndexOf(touchPrefix, StringComparison.OrdinalIgnoreCase);
			if (touchPrefixIndex >= 0)
			{
				int startIndex = touchPrefixIndex + touchPrefix.Length;
				int endIndex = startIndex;
				while (endIndex < path.Length)
				{
					char character = path[endIndex];
					if (character < '0' || character > '9')
						break;

					++endIndex;
				}

				if (endIndex > startIndex && int.TryParse(path.Substring(startIndex, endIndex - startIndex), out int touchId) == true)
				{
					return touchId;
				}
			}

			if (path.IndexOf("/Mouse/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("<Mouse>", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 0;
			}

			if (path.IndexOf("/Pointer/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("<Pointer>", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 0;
			}

			if (path.IndexOf("/Pen/", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("<Pen>", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				return 0;
			}

			return NoPointer;
		}

		private void ToggleState(bool isActive)
		{
			_joystick.position = _joystickInitialPosition;

			_movePointerID = NoPointer;
			_movePosition  = default;
			_moveOrigin    = default;

			_lookPointerID = NoPointer;
			_firePointerID = NoPointer;
			_jumpPointerID = NoPointer;
			_interactPointerID = NoPointer;

			_isFiring      = false;
			_isJumping     = false;
			_isInteracting = false;
			Fire           = false;
			Jump           = false;
			Interact       = false;

			_touchPositionsByPointer.Clear();
			_touchDeltasByPointer.Clear();

			if (isActive == true)
			{
				SubscribeToInputActions();
			}
			else
			{
				UnsubscribeFromInputActions();
			}

			_root.CanvasGroup.SetActive(isActive);
		}

		private Rect GetScreenSpaceRect(RectTransform transform)
		{
			Canvas canvas = transform.GetComponent<Canvas>();
			if (canvas == null)
			{
				canvas = transform.GetComponentInParent<Canvas>();
			}

			Rect rect  = transform.rect;
			rect.size *= canvas.scaleFactor;

			if (canvas.renderMode == RenderMode.ScreenSpaceCamera)
			{
				rect.center = canvas.worldCamera.WorldToScreenPoint(transform.position);
			}
			else if (canvas.renderMode == RenderMode.ScreenSpaceOverlay)
			{
				rect.center =  transform.position;
			}

			return rect;
		}

		private bool ShouldProcessMobileInput()
		{
			return Application.isMobilePlatform || (Context.Settings != null && Context.Settings.SimulateMobileInput);
		}
	}
}
