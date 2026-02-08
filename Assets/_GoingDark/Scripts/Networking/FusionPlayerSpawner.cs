using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Sockets;
using UnityEngine;

[RequireComponent(typeof(NetworkRunner))]
public class FusionPlayerSpawner : MonoBehaviour, INetworkRunnerCallbacks
{
    [SerializeField]
    private NetworkPrefabRef playerPrefab;

    [SerializeField]
    private Transform[] spawnPoints;

    [SerializeField]
    private bool disableOfflineSceneCharacters = true;

    private readonly Dictionary<PlayerRef, NetworkObject> spawnedPlayers = new Dictionary<PlayerRef, NetworkObject>();
    private readonly List<GameObject> disabledScenePlayers = new List<GameObject>();

    private NetworkRunner runner;
    private bool callbacksRegistered;
    private bool sceneCharactersDisabled;

    private void Awake()
    {
        TryGetComponent(out runner);
    }

    private void OnEnable()
    {
        RegisterCallbacks();
    }

    private void OnDisable()
    {
        UnregisterCallbacks();
    }

    private void RegisterCallbacks()
    {
        if (callbacksRegistered || runner == null)
            return;

        runner.AddCallbacks(this);
        callbacksRegistered = true;
    }

    private void UnregisterCallbacks()
    {
        if (!callbacksRegistered || runner == null)
            return;

        runner.RemoveCallbacks(this);
        callbacksRegistered = false;
    }

    private bool CanSpawnForPlayer(NetworkRunner currentRunner, PlayerRef player)
    {
        if (currentRunner == null || !player.IsRealPlayer)
            return false;

        if (currentRunner.GameMode == GameMode.Shared)
            return player == currentRunner.LocalPlayer;

        return currentRunner.IsServer;
    }

    private Vector3 GetSpawnPosition(PlayerRef player)
    {
        if (spawnPoints != null && spawnPoints.Length > 0)
        {
            int index = Mathf.Abs(player.RawEncoded) % spawnPoints.Length;
            Transform point = spawnPoints[index];
            if (point != null)
                return point.position;
        }

        int encoded = Mathf.Max(1, player.RawEncoded);
        float x = (encoded % 4) * 2f;
        float z = (encoded / 4) * 2f;
        return transform.position + new Vector3(x, 0f, z);
    }

    private void DisableOfflineSceneCharactersIfNeeded()
    {
        if (!disableOfflineSceneCharacters || sceneCharactersDisabled)
            return;

        CharacterInputController[] characters = FindObjectsByType<CharacterInputController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        for (int i = 0; i < characters.Length; i++)
        {
            CharacterInputController character = characters[i];
            if (character == null)
                continue;

            NetworkObject networkObject = character.GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsValid)
                continue;

            GameObject characterObject = character.gameObject;
            if (!characterObject.activeSelf)
                continue;

            disabledScenePlayers.Add(characterObject);
            characterObject.SetActive(false);
        }

        sceneCharactersDisabled = true;
    }

    private void RestoreOfflineSceneCharacters()
    {
        for (int i = 0; i < disabledScenePlayers.Count; i++)
        {
            GameObject disabled = disabledScenePlayers[i];
            if (disabled != null)
                disabled.SetActive(true);
        }

        disabledScenePlayers.Clear();
        sceneCharactersDisabled = false;
    }

    public void OnPlayerJoined(NetworkRunner currentRunner, PlayerRef player)
    {
        if (!CanSpawnForPlayer(currentRunner, player))
            return;

        if (spawnedPlayers.ContainsKey(player))
            return;

        if (!playerPrefab.IsValid)
        {
            Debug.LogError("FusionPlayerSpawner has no valid player prefab reference.");
            return;
        }

        DisableOfflineSceneCharactersIfNeeded();

        Vector3 spawnPosition = GetSpawnPosition(player);
        NetworkObject spawned = currentRunner.Spawn(playerPrefab, spawnPosition, Quaternion.identity, player);
        if (spawned == null)
        {
            Debug.LogError($"Failed to spawn player object for {player}. Ensure the prefab is in Fusion's prefab table.");
            return;
        }

        spawnedPlayers[player] = spawned;
        currentRunner.SetPlayerObject(player, spawned);
    }

    public void OnPlayerLeft(NetworkRunner currentRunner, PlayerRef player)
    {
        if (!spawnedPlayers.TryGetValue(player, out NetworkObject spawned))
            return;

        spawnedPlayers.Remove(player);

        if (spawned != null && spawned.HasStateAuthority)
            currentRunner.Despawn(spawned);

        if (spawnedPlayers.Count == 0)
            RestoreOfflineSceneCharacters();
    }

    public void OnShutdown(NetworkRunner currentRunner, ShutdownReason shutdownReason)
    {
        spawnedPlayers.Clear();
        RestoreOfflineSceneCharacters();
    }

    public void OnInput(NetworkRunner currentRunner, NetworkInput input) { }
    public void OnInputMissing(NetworkRunner currentRunner, PlayerRef player, NetworkInput input) { }
    public void OnConnectedToServer(NetworkRunner currentRunner) { }
    public void OnDisconnectedFromServer(NetworkRunner currentRunner, NetDisconnectReason reason) { }
    public void OnConnectRequest(NetworkRunner currentRunner, NetworkRunnerCallbackArgs.ConnectRequest request, byte[] token) { }
    public void OnConnectFailed(NetworkRunner currentRunner, NetAddress remoteAddress, NetConnectFailedReason reason) { }
    public void OnUserSimulationMessage(NetworkRunner currentRunner, SimulationMessagePtr message) { }
    public void OnSessionListUpdated(NetworkRunner currentRunner, List<SessionInfo> sessionList) { }
    public void OnCustomAuthenticationResponse(NetworkRunner currentRunner, Dictionary<string, object> data) { }
    public void OnHostMigration(NetworkRunner currentRunner, HostMigrationToken hostMigrationToken) { }
    public void OnReliableDataReceived(NetworkRunner currentRunner, PlayerRef player, ReliableKey key, ArraySegment<byte> data) { }
    public void OnReliableDataProgress(NetworkRunner currentRunner, PlayerRef player, ReliableKey key, float progress) { }
    public void OnSceneLoadDone(NetworkRunner currentRunner) { }
    public void OnSceneLoadStart(NetworkRunner currentRunner) { }
    public void OnObjectEnterAOI(NetworkRunner currentRunner, NetworkObject obj, PlayerRef player) { }
    public void OnObjectExitAOI(NetworkRunner currentRunner, NetworkObject obj, PlayerRef player) { }
}
