using System.Collections;
using UnityEngine;

public abstract class NetworkEventListener_MonoBehaviour : MonoBehaviour
{
    [SerializeField, GetSet("Local")]
    private PlayerData localPlayer;
    public PlayerData LocalPlayer
    {
        get
        {
            localPlayer = PlayerData.LocalPlayer;

            return localPlayer;
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
        yield break;
    }

    private void UnsubscribeToEvents()
    {
        // nothing to unsubscribe in single-player

        // No networking to unsubscribe in local mode
    }

    private void SubscribeToPlayerVariables()
    {
        LocalPlayer.InputMove.OnValueChanged += InputMoveCallback;
        LocalPlayer.InputLook.OnValueChanged += InputLookCallback;
        LocalPlayer.InputFire.OnValueChanged += InputFireCallback;
        LocalPlayer.InputReload.OnValueChanged += InputReloadCallback;
        LocalPlayer.InputAim.OnValueChanged += InputAimCallback;
        LocalPlayer.InputLean.OnValueChanged += InputLeanCallback;
        LocalPlayer.InputSprint.OnValueChanged += InputSprintCallback;
        LocalPlayer.CharacterIsReloading.OnValueChanged += CharacterIsReloadingCallback;
    }
    private void UnsubscribeToPlayerVariables()
    {
        LocalPlayer.InputMove.OnValueChanged -= InputMoveCallback;
        LocalPlayer.InputLook.OnValueChanged -= InputLookCallback;
        LocalPlayer.InputFire.OnValueChanged -= InputFireCallback;
        LocalPlayer.InputReload.OnValueChanged -= InputReloadCallback;
        LocalPlayer.InputAim.OnValueChanged -= InputAimCallback;
        LocalPlayer.InputLean.OnValueChanged -= InputLeanCallback;
        LocalPlayer.InputSprint.OnValueChanged -= InputSprintCallback;
        LocalPlayer.CharacterIsReloading.OnValueChanged -= CharacterIsReloadingCallback;
    }

    protected virtual void InputMoveCallback(Vector2 previousValue, Vector2 currentValue) { }
    protected virtual void InputLookCallback(Vector2 previousValue, Vector2 currentValue) { }
    protected virtual void InputFireCallback(bool previousValue, bool currentValue) { }
    protected virtual void InputReloadCallback(bool previousValue, bool currentValue) { }
    protected virtual void InputAimCallback(bool previousValue, bool currentValue) { }
    protected virtual void InputLeanCallback(float previousValue, float currentValue) { }
    protected virtual void InputSprintCallback(bool previousValue, bool currentValue) { }
    protected virtual void CharacterIsReloadingCallback(bool previousValue, bool currentValue) { }

    protected virtual void CharacterDespawnCallback(ulong playerID, ulong enemyID) { }
    protected virtual void CharacterSpawnCallback(ulong playerID) { }
    protected virtual void PlayerDespawnCallback(ulong playerID)
    {
        if (PlayerData.GetPlayer(playerID) == LocalPlayer)
            UnsubscribeToPlayerVariables();
    }
    protected virtual void PlayerSpawnCallback(ulong playerID)
    {
        if (PlayerData.GetPlayer(playerID) == LocalPlayer)
            SubscribeToPlayerVariables();
    }
    protected virtual void RoundEndCallback() { }
    protected virtual void RoundStartCallback() { }
    protected virtual void ClientConnectedCallback(ulong obj) { }
    protected virtual void ClientDisconnectCallback(ulong obj) { }
    protected virtual void ServerStartedCallback() { }
    protected virtual void TransportFailureCallback() { }
    protected virtual void NetworkUpdateCallback(string state) { }
    protected virtual void NetworkMatchFoundCallback() { }
}
