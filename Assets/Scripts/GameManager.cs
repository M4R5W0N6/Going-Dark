using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class GameManager : NetworkEventListener_MonoBehaviour
{
    public static bool IsInRound;

    [SerializeField]
    private GameObject roundManagerPrefab;

    protected override void ServerStartedCallback()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        NetworkObject roundManager = Instantiate(roundManagerPrefab, Vector3.zero, Quaternion.identity).GetComponent<NetworkObject>();
        roundManager.Spawn(false);
    }

    protected override void RoundStartCallback()
    {
        IsInRound = true;
    }
    protected override void RoundEndCallback()
    {
        IsInRound = false;
    }
}
