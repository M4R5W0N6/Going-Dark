using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;

public class GameManager : MonoBehaviour, IEventListener
{
    public static bool IsInRound;

    [SerializeField]
    private GameObject roundManagerPrefab;

    public void ServerStartedCallback()
    {
        if (!NetworkManager.Singleton.IsHost)
            return;

        NetworkObject roundManager = Instantiate(roundManagerPrefab, Vector3.zero, Quaternion.identity).GetComponent<NetworkObject>();
        roundManager.Spawn(false);
    }

    public void RoundStartCallback()
    {
        IsInRound = true;
    }
    public void RoundEndCallback()
    {
        IsInRound = false;
    }
}
