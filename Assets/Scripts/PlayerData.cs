using System.Collections.Generic;
using System.Linq;

using UnityEngine;
using Unity.Netcode;

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

    [HideInInspector]
    public NetworkVariable<Vector2> Move = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // force to apply to player character
    [HideInInspector]
    public NetworkVariable<Vector2> Turn = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // how much the player is rotated (origin.pitch, character.yaw)
    [HideInInspector]
    public NetworkVariable<Vector3> OriginPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);  // where the player's raycast is determined from
    [HideInInspector]
    public NetworkVariable<bool> IsOnTarget = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // whether the player is going to shoot where they're aiming
    [HideInInspector]
    public NetworkVariable<Vector3> TargetPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // where the player is aiming (in worldspace)
    [HideInInspector]
    public NetworkVariable<Vector3> RaycastPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // where the player is going to shoot (in worldspace)
}
