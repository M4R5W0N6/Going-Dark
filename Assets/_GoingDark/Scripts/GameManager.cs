using UnityEngine;

public class GameManager : MonoBehaviour, IEventListener
{
    public static bool IsInRound = true; // kept for compatibility; default true in single-player

    [SerializeField]
    private GameObject roundManagerPrefab;

    public void ServerStartedCallback() { }

    public void RoundStartCallback() { IsInRound = true; }
    public void RoundEndCallback() { IsInRound = false; }
}
