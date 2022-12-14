using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class RoundManager : NetworkEventListener_NetworkBehaviour
{
    private static RoundManager instance;
    public static RoundManager Instance
    {
        get
        {
            if (!instance)
                instance = FindObjectOfType<RoundManager>();

            return instance; 
        }
    }

    [SerializeField]
    private GameObject characterControllerPrefab;

    public void RoundStart()
    {
        if (!GameManager.IsInRound)
            RoundStartServerRpc();
    }
    public void RoundEnd()
    {
        if (GameManager.IsInRound)
            RoundEndServerRpc();
    }

    protected override void RoundStartCallback()
    {
        SpawnPlayerServerRpc(NetworkManager.LocalClientId);
    }

    protected override void RoundEndCallback()
    {
        DespawnPlayerServerRpc(NetworkManager.LocalClientId);
    }

    [ServerRpc(RequireOwnership = false)] //server owns this object but client can request a spawn
    public void SpawnPlayerServerRpc(ulong clientId)
    {
        GameObject newPlayer = Instantiate(characterControllerPrefab);
        newPlayer.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }

    [ServerRpc(RequireOwnership = false)] //server owns this object but client can request a spawn
    public void DespawnPlayerServerRpc(ulong clientId)
    {
        CharacterInputController character = CharacterInputController.GetCharacter(clientId);
        if (character)
            character.NetworkObject.Despawn(true);
    }

    //private IEnumerator SpawnPlayer()
    //{
    //    NetworkObject character = Instantiate(characterControllerPrefab, transform).GetComponent<NetworkObject>();

    //    NetworkObject player = PlayerData.LocalPlayer.NetworkObject;

    //    character.TrySetParent(player);

    //    yield return new WaitUntil(() => character.transform.parent == PlayerData.LocalPlayer.transform);

    //    character.Spawn();
    //}

    #region RoundStartEvent
    public delegate void RoundStartDelegate();
    public static event RoundStartDelegate EventRoundStart;
    [ServerRpc]
    public void RoundStartServerRpc()
    {
        OnRoundStartClientRpc();
    }
    [ClientRpc]
    public void OnRoundStartClientRpc()
    {
        EventRoundStart?.Invoke();
    }
    #endregion

    #region RoundEndEvent
    public delegate void RoundEndDelegate();
    public static event RoundEndDelegate EventRoundEnd;
    [ServerRpc]
    public void RoundEndServerRpc()
    {
        OnRoundEndClientRpc();
    }
    [ClientRpc]
    public void OnRoundEndClientRpc()
    {
        EventRoundEnd?.Invoke();
    }
    #endregion

    // params: (ulong)PlayerID
    #region PlayerSpawnEvent
    public delegate void PlayerSpawnDelegate(ulong playerID);
    public static event PlayerSpawnDelegate EventPlayerSpawn;
    [ServerRpc(RequireOwnership = false)]
    public void PlayerSpawnServerRpc(ulong playerID)
    {
        OnPlayerSpawnClientRpc(playerID);
    }
    [ClientRpc]
    public void OnPlayerSpawnClientRpc(ulong playerID)
    {
        EventPlayerSpawn?.Invoke(playerID);
    }
    #endregion

    // params: (ulong)PlayerID, (ulong)EnemyID 
    #region PlayerKillEvent
    public delegate void PlayerDespawnDelegate(ulong playerID, ulong enemyID);
    public static event PlayerDespawnDelegate EventPlayerDespawn;
    [ServerRpc(RequireOwnership = false)]
    public void PlayerDespawnServerRpc(ulong playerID, ulong enemyID)
    {
        OnPlayerDespawnClientRpc(playerID, enemyID);
    }
    [ClientRpc]
    public void OnPlayerDespawnClientRpc(ulong playerID, ulong enemyID)
    {
        EventPlayerDespawn?.Invoke(playerID, enemyID);
    }
    #endregion
}
