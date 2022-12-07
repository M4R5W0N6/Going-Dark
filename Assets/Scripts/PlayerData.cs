using System.Collections.Generic;
using System.Linq;
using Unity.Netcode;
using UnityEngine;

public class PlayerData : NetworkBehaviour
{
    // add find by id function?

    public static PlayerData GetPlayer(ulong id)
    {
        List<PlayerData> players = new List<PlayerData>(FindObjectsOfType<PlayerData>());

        players = players.Where(x => x.GetComponent<NetworkBehaviour>().OwnerClientId == id).ToList();

        return players.Count > 0 ? players[0] : null;
    }

    public static PlayerData LocalPlayer
    {
        get
        {
            List<PlayerData> players = new List<PlayerData>(FindObjectsOfType<PlayerData>());

            players = players.Where(x => x.IsLocalPlayer).ToList();

            return players.Count > 0 ? players[0] : null;
        }
    }

    [SerializeField, GetSet("Move")]
    private Vector2 move;
    public Vector2 Move { get { return NetworkMove.Value; } set { move = value; if (NetworkObject.IsLocalPlayer) NetworkMove.Value = value; } }
    private NetworkVariable<Vector2> NetworkMove = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // force to apply to player character

    [SerializeField, GetSet("Turn")]
    private Vector2 turn;
    public Vector2 Turn { get { return NetworkTurn.Value; } set { turn = value; if (NetworkObject.IsLocalPlayer) NetworkTurn.Value = value; } }
    private NetworkVariable<Vector2> NetworkTurn = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // how much the player is rotated (origin.pitch, character.yaw)

    [SerializeField, GetSet("Origin Position")]
    private Vector3 originPosition;
    public Vector3 OriginPosition { get { return NetworkOriginPosition.Value; } set { originPosition = value; if (NetworkObject.IsLocalPlayer) NetworkOriginPosition.Value = value; } }
    private NetworkVariable<Vector3> NetworkOriginPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);  // where the player's raycast is determined from

    [SerializeField, GetSet("Is On Target")]
    private bool isOnTarget;
    public bool IsOnTarget { get { return NetworkIsOnTarget.Value; } set { isOnTarget = value; if (NetworkObject.IsLocalPlayer) NetworkIsOnTarget.Value = value; } }
    private NetworkVariable<bool> NetworkIsOnTarget = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // whether the player is going to shoot where they're aiming

    [SerializeField, GetSet("Target Position")]
    private Vector3 targetPosition;
    public Vector3 TargetPosition { get { return NetworkTargetPosition.Value; } set { targetPosition = value; if (NetworkObject.IsLocalPlayer) NetworkTargetPosition.Value = value; } }
    private NetworkVariable<Vector3> NetworkTargetPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // where the player is aiming (in worldspace)

    [SerializeField, GetSet("Raycast Position")]
    private Vector3 raycastPosition;
    public Vector3 RaycastPosition { get { return NetworkRaycastPosition.Value; } set { raycastPosition = value; if (NetworkObject.IsLocalPlayer) NetworkRaycastPosition.Value = value; } }
    private NetworkVariable<Vector3> NetworkRaycastPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // where the player is going to shoot (in worldspace)
}
