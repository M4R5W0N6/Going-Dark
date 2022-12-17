using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class RoundManager : NetworkBehaviour, IEventListener
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
    private GameObject playerPrefab, characterPrefab;

    public override void OnNetworkSpawn()
    {
        SpawnPlayerServerRpc(NetworkManager.LocalClientId);
    }

    public override void OnNetworkDespawn()
    {
        DespawnCharacterServerRpc(NetworkManager.LocalClientId);
    }

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

    public void RoundStartCallback()
    {
        SpawnCharacterServerRpc(NetworkManager.LocalClientId);
    }

    public void RoundEndCallback()
    {
        DespawnCharacterServerRpc(NetworkManager.LocalClientId);
    }


    // params: (ulong)OwnerID
    #region PlayerSpawnEvent
    [ServerRpc(RequireOwnership = false)] //server owns this object but client can request a spawn
    public void SpawnPlayerServerRpc(ulong clientId)
    {
        GameObject player = Instantiate(playerPrefab);
        player.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, false);
    }
    public delegate void PlayerSpawnedDelegate(ulong ownerID);
    public static event PlayerSpawnedDelegate EventPlayerSpawn;
    [ServerRpc(RequireOwnership = false)]
    public void OnPlayerSpawnServerRpc(ulong ownerID)
    {
        OnPlayerSpawnClientRpc(ownerID);
    }
    [ClientRpc]
    public void OnPlayerSpawnClientRpc(ulong ownerID)
    {
        EventPlayerSpawn?.Invoke(ownerID);
    }
    #endregion
    // params: (ulong)OwnerID, (ulong)EnemyOwnerID 
    #region PlayerDespawnEvent
    [ServerRpc(RequireOwnership = false)] //server owns this object but client can request a spawn
    public void DespawnPlayerServerRpc(ulong clientId)
    {
        PlayerData player = PlayerData.GetPlayer(clientId);
        if (player)
            player.NetworkObject.Despawn(true);
    }
    public delegate void PlayerDespawnDelegate(ulong ownerID);
    public static event PlayerDespawnDelegate EventPlayerDespawn;
    [ServerRpc(RequireOwnership = false)]
    public void OnPlayerDespawnServerRpc(ulong ownerID)
    {
        OnPlayerDespawnClientRpc(ownerID);
    }
    [ClientRpc]
    public void OnPlayerDespawnClientRpc(ulong ownerID)
    {
        EventPlayerDespawn?.Invoke(ownerID);
    }
    #endregion

    // params: (ulong)OwnerID
    #region CharacterSpawnEvent
    [ServerRpc(RequireOwnership = false)] //server owns this object but client can request a spawn
    public void SpawnCharacterServerRpc(ulong clientId)
    {
        GameObject character = Instantiate(characterPrefab);
        character.GetComponent<NetworkObject>().SpawnAsPlayerObject(clientId, true);
    }
    public delegate void CharacterSpawnDelegate(ulong ownerID);
    public static event CharacterSpawnDelegate EventCharacterSpawn;
    [ServerRpc(RequireOwnership = false)]
    public void OnCharacterSpawnServerRpc(ulong ownerID)
    {
        OnCharacterSpawnClientRpc(ownerID);
    }
    [ClientRpc]
    public void OnCharacterSpawnClientRpc(ulong ownerID)
    {
        EventCharacterSpawn?.Invoke(ownerID);
    }
    #endregion
    // params: (ulong)OwnerID, (ulong)EnemyOwnerID 
    #region CharacterDespawnEvent
    [ServerRpc(RequireOwnership = false)] //server owns this object but client can request a spawn
    public void DespawnCharacterServerRpc(ulong clientId)
    {
        CharacterInputController character = CharacterInputController.GetCharacter(clientId);
        if (character)
            character.NetworkObject.Despawn(true);
    }
    public delegate void CharacterDespawnDelegate(ulong ownerID, ulong enemyID);
    public static event CharacterDespawnDelegate EventCharacterDespawn;
    [ServerRpc(RequireOwnership = false)]
    public void OnCharacterDespawnServerRpc(ulong ownerID, ulong enemyID)
    {
        OnCharacterDespawnClientRpc(ownerID, enemyID);
    }
    [ClientRpc]
    public void OnCharacterDespawnClientRpc(ulong ownerID, ulong enemyID)
    {
        EventCharacterDespawn?.Invoke(ownerID, enemyID);
    }
    #endregion

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
}
