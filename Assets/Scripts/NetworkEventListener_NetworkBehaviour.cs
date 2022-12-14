using System.Collections;
using Unity.Netcode;
using UnityEngine;

public abstract class NetworkEventListener_NetworkBehaviour : NetworkBehaviour
{
    [SerializeField, GetSet("Owner")]
    private PlayerData ownerPlayer;
    public PlayerData OwnerPlayer
    {
        get
        {
            if (ownerPlayer == null)
                ownerPlayer = PlayerData.GetPlayer(NetworkObject.OwnerClientId);

            return ownerPlayer;
        }
    }

    private void OnEnable()
    {
        StartCoroutine(SubscribeToEvents());
    }
    private void OnDisable()
    {
        UnsubscribeToEvents();
    }

    private IEnumerator SubscribeToEvents()
    {
        RoundManager.EventRoundStart += RoundStartCallback;
        RoundManager.EventRoundEnd += RoundEndCallback;

        RoundManager.EventPlayerSpawn += PlayerSpawnCallback;
        RoundManager.EventPlayerSpawn += SubscribeToPlayerVariables;

        RoundManager.EventPlayerDespawn += PlayerDespawnCallback;
        RoundManager.EventPlayerDespawn += UnsubscribeToPlayerVariables;

        yield return new WaitUntil(() => LobbyRelayManager.Instance);

        LobbyRelayManager.Instance.MatchFound += NetworkMatchFoundCallback;
        LobbyRelayManager.Instance.UpdateState += NetworkUpdateCallback;

        yield return new WaitUntil(() => NetworkManager.Singleton);

        NetworkManager.Singleton.OnClientConnectedCallback += ClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += ClientDisconnectCallback;
        NetworkManager.Singleton.OnServerStarted += ServerStartedCallback;
        NetworkManager.Singleton.OnTransportFailure += TransportFailureCallback;
    }

    private void UnsubscribeToEvents()
    {
        RoundManager.EventRoundStart -= RoundStartCallback;
        RoundManager.EventRoundEnd -= RoundEndCallback;

        RoundManager.EventPlayerSpawn -= PlayerSpawnCallback;
        RoundManager.EventPlayerSpawn -= SubscribeToPlayerVariables;

        RoundManager.EventPlayerDespawn -= PlayerDespawnCallback;
        RoundManager.EventPlayerDespawn -= UnsubscribeToPlayerVariables;

        if (LobbyRelayManager.Instance)
        {
            LobbyRelayManager.Instance.MatchFound -= NetworkMatchFoundCallback;
            LobbyRelayManager.Instance.UpdateState -= NetworkUpdateCallback;
        }

        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= ClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback -= ClientDisconnectCallback;
            NetworkManager.Singleton.OnServerStarted -= ServerStartedCallback;
            NetworkManager.Singleton.OnTransportFailure -= TransportFailureCallback;
        }
    }

    private void SubscribeToPlayerVariables(ulong playerID)
    {
        if (OwnerPlayer != PlayerData.GetPlayer(playerID))
            return;

        OwnerPlayer.InputMove.OnValueChanged += InputMoveCallback;
        OwnerPlayer.InputLook.OnValueChanged += InputLookCallback;
        OwnerPlayer.InputFire.OnValueChanged += InputFireCallback;
        OwnerPlayer.InputReload.OnValueChanged += InputReloadCallback;
        OwnerPlayer.InputAim.OnValueChanged += InputAimCallback;
        OwnerPlayer.InputLean.OnValueChanged += InputLeanCallback;
        OwnerPlayer.InputSprint.OnValueChanged += InputSprintCallback;
        OwnerPlayer.CharacterIsReloading.OnValueChanged += CharacterIsReloadingCallback;
    }
    private void UnsubscribeToPlayerVariables(ulong playerID, ulong enemyID)
    {
        if (OwnerPlayer != PlayerData.GetPlayer(playerID))
            return;

        OwnerPlayer.InputMove.OnValueChanged -= InputMoveCallback;
        OwnerPlayer.InputLook.OnValueChanged -= InputLookCallback;
        OwnerPlayer.InputFire.OnValueChanged -= InputFireCallback;
        OwnerPlayer.InputReload.OnValueChanged -= InputReloadCallback;
        OwnerPlayer.InputAim.OnValueChanged -= InputAimCallback;
        OwnerPlayer.InputLean.OnValueChanged -= InputLeanCallback;
        OwnerPlayer.InputSprint.OnValueChanged -= InputSprintCallback;
        OwnerPlayer.CharacterIsReloading.OnValueChanged -= CharacterIsReloadingCallback;
    }

    protected virtual void InputMoveCallback(Vector2 previousValue, Vector2 currentValue) { }
    protected virtual void InputLookCallback(Vector2 previousValue, Vector2 currentValue) { }
    protected virtual void InputFireCallback(bool previousValue, bool currentValue) { }
    protected virtual void InputReloadCallback(bool previousValue, bool currentValue) { }
    protected virtual void InputAimCallback(bool previousValue, bool currentValue) { }
    protected virtual void InputLeanCallback(float previousValue, float currentValue) { }
    protected virtual void InputSprintCallback(bool previousValue, bool currentValue) { }
    protected virtual void CharacterIsReloadingCallback(bool previousValue, bool currentValue) { }

    protected virtual void PlayerDespawnCallback(ulong playerID, ulong enemyID) { }
    protected virtual void PlayerSpawnCallback(ulong playerID) { }
    protected virtual void RoundEndCallback() { }
    protected virtual void RoundStartCallback() { }
    protected virtual void ClientConnectedCallback(ulong obj) { }
    protected virtual void ClientDisconnectCallback(ulong obj) { }
    protected virtual void ServerStartedCallback() { }
    protected virtual void TransportFailureCallback() { }
    protected virtual void NetworkUpdateCallback(string state) { }
    protected virtual void NetworkMatchFoundCallback() { }
}
