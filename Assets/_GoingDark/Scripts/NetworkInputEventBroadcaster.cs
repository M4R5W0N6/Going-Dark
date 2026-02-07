using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;

public class NetworkInputEventBroadcaster : MonoBehaviour
{
    [SerializeField]
    private bool AnyClientInput;
    [SerializeField]
    private bool allowSinglePlayerFallback = true;

    private static int activeBroadcasterCount;
    private bool CanProcessInput => AnyClientInput || (allowSinglePlayerFallback && activeBroadcasterCount <= 1);

    private void OnEnable()
    {
        activeBroadcasterCount++;
    }

    private PlayerInput playerInput;
    public PlayerInput PlayerInput
    {

        get
        {
            return playerInput;
        }
        set
        {
            playerInput = value;

            InitPlayerInput();
        }
    }
    private void Update()
    {
        if (!PlayerInput)
            PlayerInput = FindFirstObjectByType<PlayerInput>();
    }

    private void InitPlayerInput()
    {
        PlayerInput.actions["Move"].started += OnLocalPlayerMove;
        PlayerInput.actions["Look"].started += OnLocalPlayerLook;
        PlayerInput.actions["Fire"].started += OnLocalPlayerFire;
        PlayerInput.actions["Reload"].started += OnLocalPlayerReload;
        PlayerInput.actions["Aim"].started += OnLocalPlayerAim;
        PlayerInput.actions["Lean"].started += OnLocalPlayerLean;
        PlayerInput.actions["Sprint"].started += OnLocalPlayerSprint;
        PlayerInput.actions["Escape"].started += OnLocalPlayerEscape;

        PlayerInput.actions["Move"].performed += OnLocalPlayerMove;
        PlayerInput.actions["Look"].performed += OnLocalPlayerLook;
        PlayerInput.actions["Fire"].performed += OnLocalPlayerFire;
        PlayerInput.actions["Reload"].performed += OnLocalPlayerReload;
        PlayerInput.actions["Aim"].performed += OnLocalPlayerAim;
        PlayerInput.actions["Lean"].performed += OnLocalPlayerLean;
        PlayerInput.actions["Sprint"].performed += OnLocalPlayerSprint;
        PlayerInput.actions["Escape"].performed += OnLocalPlayerEscape;

        PlayerInput.actions["Move"].canceled += OnLocalPlayerMove;
        PlayerInput.actions["Look"].canceled += OnLocalPlayerLook;
        PlayerInput.actions["Fire"].canceled += OnLocalPlayerFire;
        PlayerInput.actions["Reload"].canceled += OnLocalPlayerReload;
        PlayerInput.actions["Aim"].canceled += OnLocalPlayerAim;
        PlayerInput.actions["Lean"].canceled += OnLocalPlayerLean;
        PlayerInput.actions["Sprint"].canceled += OnLocalPlayerSprint;
        PlayerInput.actions["Escape"].canceled += OnLocalPlayerEscape;
    }
    private void OnDisable()
    {
        activeBroadcasterCount = Mathf.Max(0, activeBroadcasterCount - 1);

        if (!PlayerInput)
            return;

        PlayerInput.actions["Move"].started -= OnLocalPlayerMove;
        PlayerInput.actions["Look"].started -= OnLocalPlayerLook;
        PlayerInput.actions["Fire"].started -= OnLocalPlayerFire;
        PlayerInput.actions["Reload"].started -= OnLocalPlayerReload;
        PlayerInput.actions["Aim"].started -= OnLocalPlayerAim;
        PlayerInput.actions["Lean"].started -= OnLocalPlayerLean;
        PlayerInput.actions["Sprint"].started -= OnLocalPlayerSprint;
        PlayerInput.actions["Escape"].started -= OnLocalPlayerEscape;

        PlayerInput.actions["Move"].performed -= OnLocalPlayerMove;
        PlayerInput.actions["Look"].performed -= OnLocalPlayerLook;
        PlayerInput.actions["Fire"].performed -= OnLocalPlayerFire;
        PlayerInput.actions["Reload"].performed -= OnLocalPlayerReload;
        PlayerInput.actions["Aim"].performed -= OnLocalPlayerAim;
        PlayerInput.actions["Lean"].performed -= OnLocalPlayerLean;
        PlayerInput.actions["Sprint"].performed -= OnLocalPlayerSprint;
        PlayerInput.actions["Escape"].performed -= OnLocalPlayerEscape;

        PlayerInput.actions["Move"].canceled -= OnLocalPlayerMove;
        PlayerInput.actions["Look"].canceled -= OnLocalPlayerLook;
        PlayerInput.actions["Fire"].canceled -= OnLocalPlayerFire;
        PlayerInput.actions["Reload"].canceled -= OnLocalPlayerReload;
        PlayerInput.actions["Aim"].canceled -= OnLocalPlayerAim;
        PlayerInput.actions["Lean"].canceled -= OnLocalPlayerLean;
        PlayerInput.actions["Sprint"].canceled -= OnLocalPlayerSprint;
        PlayerInput.actions["Escape"].canceled -= OnLocalPlayerEscape;
    }


    #region EventBroadcastLogic
    #region OnMove
    private void OnLocalPlayerMove(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        Vector2 value = Vector2.zero;

        if (context.performed)
        {
            value = context.ReadValue<Vector2>();

            Local_MoveEvent?.Invoke(value);

            Server_MoveEvent?.Invoke(value);
        }
        else if (context.canceled)
        {
            Local_MoveEvent?.Invoke(value);

            Server_MoveEvent?.Invoke(value);
        }
    }
    
    #endregion
    #region OnLook
    private void OnLocalPlayerLook(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        Vector2 value = Vector2.zero;

        if (context.performed)
        {
            value = context.ReadValue<Vector2>();

            Local_LookEvent?.Invoke(value);

            Server_LookEvent?.Invoke(value);
        }
        else if (context.canceled)
        {
            Local_LookEvent?.Invoke(value);

            Server_LookEvent?.Invoke(value);
        }
    }
    
    #endregion
    #region OnFire
    private void OnLocalPlayerFire(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        if (context.performed)
        {
            Local_FireEvent?.Invoke();

            Server_FireEvent?.Invoke();
        }
        else if (context.canceled)
        {
            Local_FireEndEvent?.Invoke();

            Server_FireEndEvent?.Invoke();
        }
    }
    
    #endregion
    #region OnReload
    private void OnLocalPlayerReload(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        if (context.performed)
        {
            Local_ReloadEvent?.Invoke();

            Server_ReloadEvent?.Invoke();
        }
        else if (context.canceled)
        {
            Local_ReloadEndEvent?.Invoke();

            Server_ReloadEndEvent?.Invoke();
        }
    }
    
    #endregion
    #region OnAim
    private void OnLocalPlayerAim(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        if (context.performed)
        {
            Local_AimEvent?.Invoke();

            Server_AimEvent?.Invoke();
        }
        else if (context.canceled)
        {
            Local_AimEndEvent?.Invoke();

            Server_AimEndEvent?.Invoke();
        }
    }
    
    #endregion
    #region OnLean
    private void OnLocalPlayerLean(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        if (context.performed)
        {
            float value = context.ReadValue<float>() * 0.5f + 0.5f;

            Local_LeanEvent?.Invoke(value);

            Server_LeanEvent?.Invoke(value);
        }
    }
    
    #endregion
    #region OnSprint
    private void OnLocalPlayerSprint(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        if (context.performed)
        {
            Local_SprintEvent?.Invoke();

            Server_SprintEvent?.Invoke();
        }
        else if (context.canceled)
        {
            Local_SprintEndEvent?.Invoke();

            Server_SprintEndEvent?.Invoke();
        }
    }
    
    #endregion
    #region OnEscape
    private void OnLocalPlayerEscape(InputAction.CallbackContext context)
    {
        if (!CanProcessInput)
            return;

        if (context.performed)
        {
            Local_EscapeEvent?.Invoke();

            Server_EscapeEvent?.Invoke();
        }
    }
    
    #endregion
    #endregion

    #region EventHookups
    public enum NetworkEventType
    {
        NONE,
        LOCAL,
        SERVER,
        CLIENTS
    }

    [Header("Input Events")]
    public NetworkEventType BroadcastTarget = NetworkEventType.NONE;

    [Header("Server Events")]
    public UnityEvent_Vector2 Server_MoveEvent;
    public UnityEvent_Vector2 Server_LookEvent;
    public UnityEvent Server_FireEvent;
    public UnityEvent Server_FireEndEvent;
    public UnityEvent Server_ReloadEvent;
    public UnityEvent Server_ReloadEndEvent;
    public UnityEvent Server_AimEvent;
    public UnityEvent Server_AimEndEvent;
    public UnityEvent_Float Server_LeanEvent;
    public UnityEvent Server_SprintEvent;
    public UnityEvent Server_SprintEndEvent;
    public UnityEvent Server_EscapeEvent;

    [Header("Client Events")]
    public UnityEvent_Vector2 Client_MoveEvent;
    public UnityEvent_Vector2 Client_LookEvent;
    public UnityEvent Client_FireEvent;
    public UnityEvent Client_FireEndEvent;
    public UnityEvent Client_ReloadEvent;
    public UnityEvent Client_ReloadEndEvent;
    public UnityEvent Client_AimEvent;
    public UnityEvent Client_AimEndEvent;
    public UnityEvent_Float Client_LeanEvent;
    public UnityEvent Client_SprintEvent;
    public UnityEvent Client_SprintEndEvent;
    public UnityEvent Client_EscapeEvent;

    [Header("Local Events")]
    public UnityEvent_Vector2 Local_MoveEvent;
    public UnityEvent_Vector2 Local_LookEvent;
    public UnityEvent Local_FireEvent;
    public UnityEvent Local_FireEndEvent;
    public UnityEvent Local_ReloadEvent;
    public UnityEvent Local_ReloadEndEvent;
    public UnityEvent Local_AimEvent;
    public UnityEvent Local_AimEndEvent;
    public UnityEvent_Float Local_LeanEvent;
    public UnityEvent Local_SprintEvent;
    public UnityEvent Local_SprintEndEvent;
    public UnityEvent Local_EscapeEvent;
    #endregion
}

[Serializable]
public class UnityEvent_Vector2 : UnityEvent<Vector2> { }
[Serializable]
public class UnityEvent_Float : UnityEvent<float> { }
