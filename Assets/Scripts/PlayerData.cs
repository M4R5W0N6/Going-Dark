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

    private void Update()
    {
        if (IsOwner)
        {
            CameraPosition.Value = Camera.main.transform.position;
        }
    }

    /// Misc Data (from owning client)
    #region MiscData
    [Header("Misc Data")]
    public NetworkVariable<Vector3> CameraPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // where the player's local camera instance is (in worldspace)
    #endregion

    /// Input Data (from owning client)
    #region InputData
    [Header("Input Data")]
    public NetworkVariable<Vector2> InputMove = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void SetInputMove(Vector2 value) { InputMove.Value = value; }

    public NetworkVariable<Vector2> InputLook = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void SetInputLook(Vector2 value) { InputLook.Value = value; }

    public NetworkVariable<bool> InputFire = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void SetInput_IsFiring() { InputFire.Value = true; }
    public void SetInput_IsNotFiring() { InputFire.Value = false; }

    public NetworkVariable<bool> InputReload = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void SetInput_IsReloading() { InputReload.Value = true; }
    public void SetInput_IsNotReloading() { InputReload.Value = false; }

    public NetworkVariable<bool> InputAim = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void SetInput_IsAiming() { InputAim.Value = true; }
    public void SetInput_IsNotAiming() { InputAim.Value = false; }

    public NetworkVariable<float> InputLean = new NetworkVariable<float>(1f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void SetInput_Lean(float value) { InputLean.Value = value; }

    public NetworkVariable<bool> InputSprint = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    public void SetInput_IsSprinting() { InputSprint.Value = true; }
    public void SetInput_IsNotSprinting() { InputSprint.Value = false; }

    //[SerializeField, GetSet("Escape")]
    //private bool inputEscape;
    //public bool InputEscape { get { return NetworkInputEscape.Value; } set { inputEscape = value; if (NetworkObject.IsLocalPlayer) NetworkInputEscape.Value = value; } }
    //private NetworkVariable<bool> NetworkInputEscape = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);
    #endregion

    /// Character Data (from owning client -- should be server for authoritative schema)
    #region CharacterData
    [Header("Character Data")]
    public NetworkVariable<Vector2> CharacterMove = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // force to apply to player character
    public NetworkVariable<Vector2> CharacterTurn = new NetworkVariable<Vector2>(Vector2.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // how much the player is rotated (origin.pitch, character.yaw)
    public NetworkVariable<Vector3> CharacterOriginPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner);  // where the player's raycast is determined from
    public NetworkVariable<Vector3> CharacterTargetPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // where the player is aiming (in worldspace)
    public NetworkVariable<Vector3> CharacterRaycastPosition = new NetworkVariable<Vector3>(Vector3.zero, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // where the player is going to shoot (in worldspace)
    public NetworkVariable<bool> CharacterIsOnTarget = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // whether the player is going to shoot where they're aiming
    public NetworkVariable<bool> CharacterIsReloading = new NetworkVariable<bool>(false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Owner); // if the player is done reloading (in worldspace)
    #endregion
}
