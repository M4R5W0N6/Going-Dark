using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;
using Unity.Netcode;
using MyBox;

public class AuthoritativeInputHandler : MonoBehaviour
{
    [SerializeField, Tooltip("Leave empty for no ownership conditional on local event broadcasts")]
    private NetworkObject NetworkObjectForLocalEvents;

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
            PlayerInput = FindObjectOfType<PlayerInput>();
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
        Vector2 value = Vector2.zero;

        if (context.performed)
        {
            value = context.ReadValue<Vector2>();

            OnMoveServerRpc(value);

            if (NetworkObjectForLocalEvents)
                if (NetworkObjectForLocalEvents.IsOwner)
                    Local_MoveEvent?.Invoke(value);

        }
        else if (context.canceled)
        {
            OnMoveServerRpc(value);

            if (NetworkObjectForLocalEvents)
                if (NetworkObjectForLocalEvents.IsOwner)
                    Local_MoveEvent?.Invoke(value);
        }
    }
    [ServerRpc]
    private void OnMoveServerRpc(Vector2 value)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnMoveClientRpc(value);

        Server_MoveEvent?.Invoke(value);
    }
    [ClientRpc]
    private void OnMoveClientRpc(Vector2 value)
    {
        Client_MoveEvent?.Invoke(value);
    }
    #endregion
    #region OnLook
    private void OnLocalPlayerLook(InputAction.CallbackContext context)
    {
        if (NetworkObjectForLocalEvents)
            if (!NetworkObjectForLocalEvents.IsOwner)
                return;

        Vector2 value = Vector2.zero;

        if (context.performed)
        {
            value = context.ReadValue<Vector2>();

            OnLookServerRpc(value);

            Local_LookEvent?.Invoke(value);

        }
        else if (context.canceled)
        {
            OnLookServerRpc(value);

            Local_LookEvent?.Invoke(value);
        }
    }
    [ServerRpc]
    private void OnLookServerRpc(Vector2 value)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnLookClientRpc(value);

        Server_LookEvent?.Invoke(value);
    }
    [ClientRpc]
    private void OnLookClientRpc(Vector2 value)
    {
        Client_LookEvent?.Invoke(value);
    }
    #endregion
    #region OnFire
    private void OnLocalPlayerFire(InputAction.CallbackContext context)
    {
        if (NetworkObjectForLocalEvents)
            if (!NetworkObjectForLocalEvents.IsOwner)
                return;

        if (context.performed)
        {
            OnFireServerRpc();

            Local_FireEvent?.Invoke();
        }
        else if (context.canceled)
        {
            OnFireEndServerRpc();

            Local_FireEndEvent?.Invoke();
        }
    }
    [ServerRpc]
    private void OnFireServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnFireClientRpc();

        Server_FireEvent?.Invoke();
    }
    [ClientRpc]
    private void OnFireClientRpc()
    {
        Client_FireEvent?.Invoke();
    }
    [ServerRpc]
    private void OnFireEndServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnFireEndClientRpc();

        Server_FireEndEvent?.Invoke();
    }
    [ClientRpc]
    private void OnFireEndClientRpc()
    {
        Client_FireEndEvent?.Invoke();
    }
    #endregion
    #region OnReload
    private void OnLocalPlayerReload(InputAction.CallbackContext context)
    {
        if (NetworkObjectForLocalEvents)
            if (!NetworkObjectForLocalEvents.IsOwner)
                return;

        if (context.performed)
        {
            OnReloadServerRpc();

            Local_ReloadEvent?.Invoke();
        }
        else if (context.canceled)
        {
            OnReloadEndServerRpc();

            Local_ReloadEndEvent?.Invoke();
        }
    }
    [ServerRpc]
    private void OnReloadServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnReloadClientRpc();

        Server_ReloadEvent?.Invoke();
    }
    [ClientRpc]
    private void OnReloadClientRpc()
    {
        Client_ReloadEvent?.Invoke();
    }
    [ServerRpc]
    private void OnReloadEndServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnReloadEndClientRpc();

        Server_ReloadEndEvent?.Invoke();
    }
    [ClientRpc]
    private void OnReloadEndClientRpc()
    {
        Client_ReloadEndEvent?.Invoke();
    }
    #endregion
    #region OnAim
    private void OnLocalPlayerAim(InputAction.CallbackContext context)
    {
        if (NetworkObjectForLocalEvents)
            if (!NetworkObjectForLocalEvents.IsOwner)
                return;

        if (context.performed)
        {
            OnAimServerRpc();

            Local_AimEvent?.Invoke();
        }
        else if (context.canceled)
        {
            OnAimEndServerRpc();

            Local_AimEndEvent?.Invoke();
        }
    }
    [ServerRpc]
    private void OnAimServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnAimClientRpc();

        Server_AimEvent?.Invoke();
    }
    [ClientRpc]
    private void OnAimClientRpc()
    {
        Client_AimEvent?.Invoke();
    }
    [ServerRpc]
    private void OnAimEndServerRpc()
    {
        OnAimEndClientRpc();

        if (!NetworkManager.Singleton.IsServer)
            Server_AimEndEvent?.Invoke();
    }
    [ClientRpc]
    private void OnAimEndClientRpc()
    {
        Client_AimEndEvent?.Invoke();
    }
    #endregion
    #region OnLean
    private void OnLocalPlayerLean(InputAction.CallbackContext context)
    {
        if (NetworkObjectForLocalEvents)
            if (!NetworkObjectForLocalEvents.IsOwner)
                return;

        if (context.performed)
        {
            float value = context.ReadValue<float>() * 0.5f + 0.5f;

            OnLeanServerRpc(value);

            Local_LeanEvent?.Invoke(value);
        }
    }
    [ServerRpc]
    private void OnLeanServerRpc(float value)
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnLeanClientRpc(value);

        Server_LeanEvent?.Invoke(value);
    }
    [ClientRpc]
    private void OnLeanClientRpc(float value)
    {
        Client_LeanEvent?.Invoke(value);
    }
    #endregion
    #region OnSprint
    private void OnLocalPlayerSprint(InputAction.CallbackContext context)
    {
        if (NetworkObjectForLocalEvents)
            if (!NetworkObjectForLocalEvents.IsOwner)
                return;

        if (context.performed)
        {
            OnSprintServerRpc();

            Local_SprintEvent?.Invoke();
        }
        else if (context.canceled)
        {
            OnSprintEndServerRpc();

            Local_SprintEndEvent?.Invoke();
        }
    }
    [ServerRpc]
    private void OnSprintServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnSprintClientRpc();

        Server_SprintEvent?.Invoke();
    }
    [ClientRpc]
    private void OnSprintClientRpc()
    {
        Client_SprintEvent?.Invoke();
    }
    [ServerRpc]
    private void OnSprintEndServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnSprintEndClientRpc();

        Server_SprintEvent?.Invoke();
    }
    [ClientRpc]
    private void OnSprintEndClientRpc()
    {
        Client_SprintEvent?.Invoke();
    }
    #endregion
    #region OnEscape
    private void OnLocalPlayerEscape(InputAction.CallbackContext context)
    {
        if (NetworkObjectForLocalEvents)
            if (!NetworkObjectForLocalEvents.IsOwner)
                return;

        if (context.performed)
        {
            OnEscapeServerRpc();

            Local_EscapeEvent?.Invoke();
        }
    }
    [ServerRpc]
    private void OnEscapeServerRpc()
    {
        if (!NetworkManager.Singleton.IsServer)
            return;

        OnEscapeClientRpc();

        Server_EscapeEvent?.Invoke();
    }
    [ClientRpc]
    private void OnEscapeClientRpc()
    {
        Client_EscapeEvent?.Invoke();
    }
    #endregion
    #endregion

    #region EventHookups
    public enum NetworkEventType
    {
        NONE,
        LOCAL,
        SERVER,
        CLIENT
    }

    [Separator("Input Events")]
    public NetworkEventType FilterEvents = NetworkEventType.NONE;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent_Vector2 Server_MoveEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent_Vector2 Server_LookEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_FireEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_FireEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_ReloadEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_ReloadEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_AimEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_AimEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent_Float Server_LeanEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_SprintEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_SprintEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.SERVER)]
    public UnityEvent Server_EscapeEvent;

    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent_Vector2 Client_MoveEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent_Vector2 Client_LookEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_FireEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_FireEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_ReloadEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_ReloadEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_AimEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_AimEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent_Float Client_LeanEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_SprintEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_SprintEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.CLIENT)]
    public UnityEvent Client_EscapeEvent;

    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent_Vector2 Local_MoveEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent_Vector2 Local_LookEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_FireEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_FireEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_ReloadEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_ReloadEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_AimEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_AimEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent_Float Local_LeanEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_SprintEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_SprintEndEvent;
    [ConditionalField(nameof(FilterEvents), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_EscapeEvent;
    #endregion
}

[Serializable]
public class UnityEvent_Vector2 : UnityEvent<Vector2> { }
[Serializable]
public class UnityEvent_Float : UnityEvent<float> { }
