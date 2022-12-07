using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public static class NetworkEventManager
{
    public delegate void PlayerSpawnDelegate(ulong id);

    public static event PlayerSpawnDelegate EventPlayerSpawn;

    [ServerRpc]
    public static void PlayerSpawnServerRpc(ulong id)
    {
        EventPlayerSpawn(id);
        OnPlayerSpawnClientRpc(id);
    }

    [ClientRpc]
    public static void OnPlayerSpawnClientRpc(ulong id)
    {
        EventPlayerSpawn(id);
    }
}
