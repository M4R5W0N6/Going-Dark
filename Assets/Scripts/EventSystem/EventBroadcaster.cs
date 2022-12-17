using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.EventSystems;

public class EventBroadcaster : MonoBehaviour
{
    private static List<IEventListener> listeners;
    public static List<IEventListener> Listeners
    {
        get
        {
            listeners = (List<IEventListener>)FindObjectsOfType<MonoBehaviour>().OfType<IEventListener>();
            return listeners;
        }
    }

    public IEnumerator SubscribeToEvents()
    {
        RoundManager.EventRoundStart += RoundStartCallback;
        RoundManager.EventRoundEnd += RoundEndCallback;
        RoundManager.EventPlayerSpawn += PlayerSpawnCallback;
        RoundManager.EventPlayerDespawn += PlayerDespawnCallback;
        RoundManager.EventCharacterSpawn += CharacterSpawnCallback;
        RoundManager.EventCharacterDespawn += CharacterDespawnCallback;

        yield return new WaitUntil(() => LobbyRelayManager.Instance);

        LobbyRelayManager.Instance.MatchFound += NetworkMatchFoundCallback;
        LobbyRelayManager.Instance.UpdateState += NetworkUpdateCallback;

        yield return new WaitUntil(() => NetworkManager.Singleton);

        NetworkManager.Singleton.OnClientConnectedCallback += ClientConnectedCallback;
        NetworkManager.Singleton.OnClientDisconnectCallback += ClientDisconnectedCallback;
        NetworkManager.Singleton.OnServerStarted += ServerStartedCallback;
        NetworkManager.Singleton.OnTransportFailure += TransportFailureCallback;
    }

    public void UnsubscribeToEvents()
    {
        RoundManager.EventRoundStart -= RoundStartCallback;
        RoundManager.EventRoundEnd -= RoundEndCallback;
        RoundManager.EventPlayerSpawn -= PlayerSpawnCallback;
        RoundManager.EventPlayerDespawn -= PlayerDespawnCallback;
        RoundManager.EventCharacterSpawn -= CharacterSpawnCallback;
        RoundManager.EventCharacterDespawn -= CharacterDespawnCallback;

        if (LobbyRelayManager.Instance)
        {
            LobbyRelayManager.Instance.MatchFound -= NetworkMatchFoundCallback;
            LobbyRelayManager.Instance.UpdateState -= NetworkUpdateCallback;
        }

        if (NetworkManager.Singleton)
        {
            NetworkManager.Singleton.OnClientConnectedCallback -= ClientConnectedCallback;
            NetworkManager.Singleton.OnClientDisconnectCallback -= ClientDisconnectedCallback;
            NetworkManager.Singleton.OnServerStarted -= ServerStartedCallback;
            NetworkManager.Singleton.OnTransportFailure -= TransportFailureCallback;
        }
    }

    public void SubscribeToPlayerVariables(PlayerData player)
    {
        player.InputMove.OnValueChanged += InputMoveCallback;
        player.InputLook.OnValueChanged += InputLookCallback;
        player.InputFire.OnValueChanged += InputFireCallback;
        player.InputReload.OnValueChanged += InputReloadCallback;
        player.InputAim.OnValueChanged += InputAimCallback;
        player.InputLean.OnValueChanged += InputLeanCallback;
        player.InputSprint.OnValueChanged += InputSprintCallback;
        player.CharacterIsReloading.OnValueChanged += CharacterIsReloadingCallback;
    }
    public void UnsubscribeToPlayerVariables(PlayerData player)
    {
        player.InputMove.OnValueChanged -= InputMoveCallback;
        player.InputLook.OnValueChanged -= InputLookCallback;
        player.InputFire.OnValueChanged -= InputFireCallback;
        player.InputReload.OnValueChanged -= InputReloadCallback;
        player.InputAim.OnValueChanged -= InputAimCallback;
        player.InputLean.OnValueChanged -= InputLeanCallback;
        player.InputSprint.OnValueChanged -= InputSprintCallback;
        player.CharacterIsReloading.OnValueChanged -= CharacterIsReloadingCallback;
    }

    public void InputMoveCallback(Vector2 previousValue, Vector2 currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            InputMoveEvent data = new InputMoveEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    InputMoveEvent.Delegate);
        }
    }
    public void InputLookCallback(Vector2 previousValue, Vector2 currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            InputLookEvent data = new InputLookEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    InputLookEvent.Delegate);
        }
    }
    public void InputFireCallback(bool previousValue, bool currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            InputFireEvent data = new InputFireEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    InputFireEvent.Delegate);
        }
    }
    public void InputReloadCallback(bool previousValue, bool currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            InputReloadEvent data = new InputReloadEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    InputReloadEvent.Delegate);
        }
    }
    public void InputAimCallback(bool previousValue, bool currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            InputAimEvent data = new InputAimEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    InputAimEvent.Delegate);
        }
    }
    public void InputLeanCallback(float previousValue, float currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            InputLeanEvent data = new InputLeanEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    InputLeanEvent.Delegate);
        }
    }
    public void InputSprintCallback(bool previousValue, bool currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            InputSprintEvent data = new InputSprintEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    InputSprintEvent.Delegate);
        }
    }
    public void CharacterIsReloadingCallback(bool previousValue, bool currentValue) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            CharacterIsReloadingEvent data = new CharacterIsReloadingEvent(
                              EventSystem.current,
                              previousValue,
                              currentValue);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    CharacterIsReloadingEvent.Delegate);
        }
    }
    public void CharacterDespawnCallback(ulong playerID, ulong enemyID) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            CharacterDespawnEvent data = new CharacterDespawnEvent(
                              EventSystem.current,
                              playerID,
                              enemyID);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    CharacterDespawnEvent.Delegate);
        }
    }
    public void CharacterSpawnCallback(ulong playerID) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            CharacterSpawnEvent data = new CharacterSpawnEvent(
                              EventSystem.current,
                              playerID);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    CharacterSpawnEvent.Delegate);
        }
    }
    public void PlayerDespawnCallback(ulong playerID)
    {
        PlayerData player = PlayerData.GetPlayer(playerID);
        if (player == PlayerData.LocalPlayer)
            UnsubscribeToPlayerVariables(player);

        for (int i = 0; i < Listeners.Count; i++)
        {
            PlayerDespawnEvent data = new PlayerDespawnEvent(
                              EventSystem.current,
                              playerID);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    PlayerDespawnEvent.Delegate);
        }
    }
    public void PlayerSpawnCallback(ulong playerID)
    {
        PlayerData player = PlayerData.GetPlayer(playerID);
        if (player == PlayerData.LocalPlayer)
            SubscribeToPlayerVariables(player);

        for (int i = 0; i < Listeners.Count; i++)
        {
            PlayerSpawnEvent data = new PlayerSpawnEvent(
                              EventSystem.current,
                              playerID);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    PlayerSpawnEvent.Delegate);
        }
    }
    public void RoundEndCallback() 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            RoundEndEvent data = new RoundEndEvent(
                              EventSystem.current);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    RoundEndEvent.Delegate);
        }
    }
    public void RoundStartCallback() 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            RoundStartEvent data = new RoundStartEvent(
                              EventSystem.current);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    RoundStartEvent.Delegate);
        }
    }
    public void ClientConnectedCallback(ulong obj) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            ClientConnectedEvent data = new ClientConnectedEvent(
                              EventSystem.current,
                              obj);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    ClientConnectedEvent.Delegate);
        }
    }
    public void ClientDisconnectedCallback(ulong obj) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            ClientDisconnectedEvent data = new ClientDisconnectedEvent(
                              EventSystem.current,
                              obj);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    ClientDisconnectedEvent.Delegate);
        }
    }
    public void ServerStartedCallback() 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            ServerStartedEvent data = new ServerStartedEvent(
                              EventSystem.current);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    ServerStartedEvent.Delegate);
        }
    }
    public void TransportFailureCallback() 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            TransportFailureEvent data = new TransportFailureEvent(
                              EventSystem.current);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    TransportFailureEvent.Delegate);
        }
    }
    public void NetworkUpdateCallback(string state) 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            NetworkUpdateEvent data = new NetworkUpdateEvent(
                              EventSystem.current,
                              state);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    NetworkUpdateEvent.Delegate);
        }
    }
    public void NetworkMatchFoundCallback() 
    {
        for (int i = 0; i < Listeners.Count; i++)
        {
            NetworkMatchFoundEvent data = new NetworkMatchFoundEvent(
                              EventSystem.current);

            ExecuteEvents.Execute(
                                    Listeners[i].gameObject,
                                    data,
                                    NetworkMatchFoundEvent.Delegate);
        }
    }
}
