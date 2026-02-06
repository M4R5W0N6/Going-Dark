using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Events;

public class LobbyRelayManager : MonoBehaviour
{
    // Singletons
    public static LobbyRelayManager _instance;
    public static LobbyRelayManager Instance => _instance;

    private string _lobbyId;

    // Notify state update
    public UnityAction<string> UpdateState;
    // Notify Match found
    public UnityAction MatchFound;
    //// Notify Match joined
    //public UnityAction MatchJoined;
    //// Notify Match left
    //public UnityAction MatchLeft;

    private void Awake()
    {
        // Just a basic singleton
        if (_instance is null)
        {
            _instance = this;
            return;
        }

        Destroy(this);
    }

    private void Start()
    {
        // Local-only mode; immediately invoke MatchFound
        UpdateState?.Invoke("Local mode");
        MatchFound?.Invoke();
    }

    //private void Update()
    //{
    //    if (GameManager.IsInRound)
    //    {
    //        foreach (var entry in /* network removed */ new System.Collections.Generic.Dictionary<ulong, object>())
    //        {
    //            Debug.Log(entry.Value.name);
    //        }
    //    }
    //}

    private void ClientConnected(ulong id) { }

    #region UnityLogin
    private void SetupEvents() { }
    #endregion

    #region Lobby
    public void FindMatch()
    {
        UpdateState?.Invoke("Local match started");
        MatchFound?.Invoke();
    }

    private void CreateMatch()
    {
        UpdateState?.Invoke("Local match created");
    }

    private IEnumerator HeartbeatLobbyCoroutine(string lobbyId, float waitTimeSeconds) { yield break; }

    private void OnDestroy()
    {
        // Nothing to cleanup in local mode
    }


    #endregion

    /// <summary>
    /// ReleaseHostData represents the necessary information
    /// for a Host to host a game on Relay
    /// </summary>
    public struct RelayHostData { }
    public struct RelayJoinData { }
}
