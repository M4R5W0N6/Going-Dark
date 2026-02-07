using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
public interface IEventListener : IEventSystemHandler
{
    public GameObject gameObject { get; }

    public void InputMoveCallback(InputMoveEvent eventData) { }
    public void InputLookCallback(InputLookEvent eventData) { }
    public void InputFireCallback(InputFireEvent eventData) { }
    public void InputReloadCallback(InputReloadEvent eventData) { }
    public void InputAimCallback(InputAimEvent eventData) { }
    public void InputLeanCallback(InputLeanEvent eventData) { }
    public void InputSprintCallback(InputSprintEvent eventData) { }
    public void CharacterIsReloadingCallback(CharacterIsReloadingEvent eventData) { }
    public void CharacterDespawnCallback(CharacterDespawnEvent eventData) { }
    public void CharacterSpawnCallback(CharacterSpawnEvent eventData) { }
    public void PlayerDespawnCallback(PlayerDespawnEvent eventData) { }
    public void PlayerSpawnCallback(PlayerSpawnEvent eventData) { }
    public void RoundEndCallback(RoundEndEvent eventData) { }
    public void RoundStartCallback(RoundStartEvent eventData) { }
    public void ClientConnectedCallback(ClientConnectedEvent eventData) { }
    public void ClientDisconnectedCallback(ClientDisconnectedEvent eventData) { }
    public void ServerStartedCallback(ServerStartedEvent eventData) { }
    public void TransportFailureCallback(TransportFailureEvent eventData) { }
    public void NetworkUpdateCallback(NetworkUpdateEvent eventData) { }
    public void NetworkMatchFoundCallback(NetworkMatchFoundEvent eventData) { }
}
