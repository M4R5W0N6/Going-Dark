using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;
using Unity.Netcode;
using MyBox;

public class NetworkInputEventBroadcaster : NetworkBehaviour
{
    [SerializeField]
    private bool AnyClientInput;

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
        if (!(AnyClientInput || IsOwner))
            return;

        Vector2 value = Vector2.zero;

        if (context.performed)
        {
            value = context.ReadValue<Vector2>();

            Local_MoveEvent?.Invoke(value);

            OnMoveServerRpc(value, NetworkManager.LocalClientId);
        }
        else if (context.canceled)
        {
            Local_MoveEvent?.Invoke(value);

            OnMoveServerRpc(value, NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnMoveServerRpc(Vector2 value, ulong clientID)
    {
        if (!IsServer)
            return;

        Server_MoveEvent?.Invoke(value);

        OnMoveClientRpc(value, clientID);
    }
    [ClientRpc]
    private void OnMoveClientRpc(Vector2 value, ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_MoveEvent?.Invoke(value);
    }
    #endregion
    #region OnLook
    private void OnLocalPlayerLook(InputAction.CallbackContext context)
    {
        if (!(AnyClientInput || IsOwner))
            return;

        Vector2 value = Vector2.zero;

        if (context.performed)
        {
            value = context.ReadValue<Vector2>();

            Local_LookEvent?.Invoke(value);

            OnLookServerRpc(value, NetworkManager.LocalClientId);
        }
        else if (context.canceled)
        {
            Local_LookEvent?.Invoke(value);

            OnLookServerRpc(value, NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnLookServerRpc(Vector2 value, ulong clientID)
    {
        if (!IsServer)
            return;

        Server_LookEvent?.Invoke(value);

        OnLookClientRpc(value, clientID);
    }
    [ClientRpc]
    private void OnLookClientRpc(Vector2 value, ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_LookEvent?.Invoke(value);
    }
    #endregion
    #region OnFire
    private void OnLocalPlayerFire(InputAction.CallbackContext context)
    {
        if (!(AnyClientInput || IsOwner))
            return;

        if (context.performed)
        {
            Local_FireEvent?.Invoke();

            OnFireServerRpc(NetworkManager.LocalClientId);
        }
        else if (context.canceled)
        {
            Local_FireEndEvent?.Invoke();

            OnFireEndServerRpc(NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnFireServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_FireEvent?.Invoke();

        OnFireClientRpc(clientID);
    }
    [ClientRpc]
    private void OnFireClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_FireEvent?.Invoke();
    }
    [ServerRpc]
    private void OnFireEndServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_FireEndEvent?.Invoke();

        OnFireEndClientRpc(clientID);
    }
    [ClientRpc]
    private void OnFireEndClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_FireEndEvent?.Invoke();
    }
    #endregion
    #region OnReload
    private void OnLocalPlayerReload(InputAction.CallbackContext context)
    {
        if (!(AnyClientInput || IsOwner))
            return;

        if (context.performed)
        {
            Local_ReloadEvent?.Invoke();

            OnReloadServerRpc(NetworkManager.LocalClientId);
        }
        else if (context.canceled)
        {
            Local_ReloadEndEvent?.Invoke();

            OnReloadEndServerRpc(NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnReloadServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_ReloadEvent?.Invoke();

        OnReloadClientRpc(clientID);
    }
    [ClientRpc]
    private void OnReloadClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_ReloadEvent?.Invoke();
    }
    [ServerRpc]
    private void OnReloadEndServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_ReloadEndEvent?.Invoke();

        OnReloadEndClientRpc(clientID);
    }
    [ClientRpc]
    private void OnReloadEndClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_ReloadEndEvent?.Invoke();
    }
    #endregion
    #region OnAim
    private void OnLocalPlayerAim(InputAction.CallbackContext context)
    {
        if (!(AnyClientInput || IsOwner))
            return;

        if (context.performed)
        {
            Local_AimEvent?.Invoke();

            OnAimServerRpc(NetworkManager.LocalClientId);
        }
        else if (context.canceled)
        {
            Local_AimEndEvent?.Invoke();

            OnAimEndServerRpc(NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnAimServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_AimEvent?.Invoke();

        OnAimClientRpc(clientID);
    }
    [ClientRpc]
    private void OnAimClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_AimEvent?.Invoke();
    }
    [ServerRpc]
    private void OnAimEndServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_AimEndEvent?.Invoke();

        OnAimEndClientRpc(clientID);
    }
    [ClientRpc]
    private void OnAimEndClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_AimEndEvent?.Invoke();
    }
    #endregion
    #region OnLean
    private void OnLocalPlayerLean(InputAction.CallbackContext context)
    {
        if (!(AnyClientInput || IsOwner))
            return;

        if (context.performed)
        {
            float value = context.ReadValue<float>() * 0.5f + 0.5f;

            Local_LeanEvent?.Invoke(value);

            OnLeanServerRpc(value, NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnLeanServerRpc(float value, ulong clientID)
    {
        if (!IsServer)
            return;

        Server_LeanEvent?.Invoke(value);

        OnLeanClientRpc(value, clientID);
    }
    [ClientRpc]
    private void OnLeanClientRpc(float value, ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_LeanEvent?.Invoke(value);
    }
    #endregion
    #region OnSprint
    private void OnLocalPlayerSprint(InputAction.CallbackContext context)
    {
        if (!(AnyClientInput || IsOwner))
            return;

        if (context.performed)
        {
            Local_SprintEvent?.Invoke();

            OnSprintServerRpc(NetworkManager.LocalClientId);
        }
        else if (context.canceled)
        {
            Local_SprintEndEvent?.Invoke();

            OnSprintEndServerRpc(NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnSprintServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_SprintEvent?.Invoke();

        OnSprintClientRpc(clientID);
    }
    [ClientRpc]
    private void OnSprintClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_SprintEvent?.Invoke();
    }
    [ServerRpc]
    private void OnSprintEndServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_SprintEvent?.Invoke();

        OnSprintEndClientRpc(clientID);
    }
    [ClientRpc]
    private void OnSprintEndClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
            Client_SprintEvent?.Invoke();
    }
    #endregion
    #region OnEscape
    private void OnLocalPlayerEscape(InputAction.CallbackContext context)
    {
        if (!(AnyClientInput || IsOwner))
            return;

        if (context.performed)
        {
            Local_EscapeEvent?.Invoke();

            OnEscapeServerRpc(NetworkManager.LocalClientId);
        }
    }
    [ServerRpc]
    private void OnEscapeServerRpc(ulong clientID)
    {
        if (!IsServer)
            return;

        Server_EscapeEvent?.Invoke();

        OnEscapeClientRpc(clientID);
    }
    [ClientRpc]
    private void OnEscapeClientRpc(ulong clientID)
    {
        if (clientID == NetworkManager.LocalClientId)
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
        CLIENTS
    }

    [Separator("Input Events")]
    public NetworkEventType BroadcastTarget = NetworkEventType.NONE;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent_Vector2 Server_MoveEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent_Vector2 Server_LookEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_FireEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_FireEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_ReloadEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_ReloadEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_AimEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_AimEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent_Float Server_LeanEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_SprintEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_SprintEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.SERVER)]
    public UnityEvent Server_EscapeEvent;

    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent_Vector2 Client_MoveEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent_Vector2 Client_LookEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_FireEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_FireEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_ReloadEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_ReloadEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_AimEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_AimEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent_Float Client_LeanEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_SprintEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_SprintEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.CLIENTS)]
    public UnityEvent Client_EscapeEvent;

    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent_Vector2 Local_MoveEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent_Vector2 Local_LookEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_FireEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_FireEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_ReloadEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_ReloadEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_AimEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_AimEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent_Float Local_LeanEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_SprintEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_SprintEndEvent;
    [ConditionalField(nameof(BroadcastTarget), false, NetworkEventType.LOCAL)]
    public UnityEvent Local_EscapeEvent;
    #endregion
}

[Serializable]
public class UnityEvent_Vector2 : UnityEvent<Vector2> { }
[Serializable]
public class UnityEvent_Float : UnityEvent<float> { }
