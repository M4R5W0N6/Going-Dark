using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LocalData;

public class PlayerData : MonoBehaviour, IEventListener
{
    private static List<PlayerData> players;
    public static List<PlayerData> Players
    {
        get
        {
            players = new List<PlayerData>(FindObjectsOfType<PlayerData>());

            return players;
        }
    }

    public static PlayerData GetPlayer(ulong ownerId)
    {
        // Single-player: just return the first player
        return Players.Count > 0 ? Players[0] : null;
    }

    private static PlayerData ownerPlayer;
    public static PlayerData OwnerPlayer
    {
        get
        {
            if (ownerPlayer)
                return ownerPlayer;

            ownerPlayer = Players.Count > 0 ? Players[0] : null;
            return ownerPlayer;
        }
    }

    private static PlayerData localPlayer;
    public static PlayerData LocalPlayer
    {
        get
        {
            if (localPlayer)
                return localPlayer;

            localPlayer = Players.Count > 0 ? Players[0] : null;
            return localPlayer;
        }
    }

    private void Start() { }
    private void OnDestroy() { }

    private void Update()
    {
        CameraPosition.Value = Camera.main ? Camera.main.transform.position : Vector3.zero;
    }

    public void CharacterSpawnCallback(ulong playerID)
    {
        CharacterIsReloading.Value = false;

        InputLean.ForceNotify();
    }

    /// Misc Data (from owning client)
    #region MiscData
    [Header("Misc Data")]
    public ObservableVariable<Vector3> CameraPosition = new ObservableVariable<Vector3>(Vector3.zero); // where the player's local camera instance is (in worldspace)
    #endregion

    /// Input Data (from owning client)
    #region InputData
    [Header("Input Data")]
    public ObservableVariable<Vector2> InputMove = new ObservableVariable<Vector2>(Vector2.zero);
    public void SetInputMove(Vector2 value) { InputMove.Value = value; }

    public ObservableVariable<Vector2> InputLook = new ObservableVariable<Vector2>(Vector2.zero);
    public void SetInputLook(Vector2 value) { InputLook.Value = value; }

    public ObservableVariable<bool> InputFire = new ObservableVariable<bool>(false);
    public void SetInput_IsFiring() { InputFire.Value = true; }
    public void SetInput_IsNotFiring() { InputFire.Value = false; }

    public ObservableVariable<bool> InputReload = new ObservableVariable<bool>(false);
    public void SetInput_IsReloading() { InputReload.Value = true; }
    public void SetInput_IsNotReloading() { InputReload.Value = false; }

    public ObservableVariable<bool> InputAim = new ObservableVariable<bool>(false);
    public void SetInput_IsAiming() { InputAim.Value = true; }
    public void SetInput_IsNotAiming() { InputAim.Value = false; }

    public ObservableVariable<float> InputLean = new ObservableVariable<float>(1f);
    public void SetInput_Lean(float value) { InputLean.Value = value; }

    public ObservableVariable<bool> InputSprint = new ObservableVariable<bool>(false);
    public void SetInput_IsSprinting() { InputSprint.Value = true; }
    public void SetInput_IsNotSprinting() { InputSprint.Value = false; }

    //[SerializeField, GetSet("Escape")]
    //private bool inputEscape;
    //public bool InputEscape { get { return NetworkInputEscape.Value; } set { inputEscape = value; NetworkInputEscape.Value = value; } }
    //private ObservableVariable<bool> NetworkInputEscape = new ObservableVariable<bool>(false);
    #endregion

    /// Character Data (from owning client -- should be server for authoritative schema)
    #region CharacterData
    [Header("Character Data")]
    public ObservableVariable<Vector2> CharacterMove = new ObservableVariable<Vector2>(Vector2.zero); // force to apply to player character
    public ObservableVariable<Vector2> CharacterTurn = new ObservableVariable<Vector2>(Vector2.zero); // how much the player is rotated (origin.pitch, character.yaw)
    public ObservableVariable<Vector3> CharacterOriginPosition = new ObservableVariable<Vector3>(Vector3.zero);  // where the player's raycast is determined from
    public ObservableVariable<Vector3> CharacterTargetPosition = new ObservableVariable<Vector3>(Vector3.zero); // where the player is aiming (in worldspace)
    public ObservableVariable<Vector3> CharacterRaycastPosition = new ObservableVariable<Vector3>(Vector3.zero); // where the player is going to shoot (in worldspace)
    public ObservableVariable<bool> CharacterIsOnTarget = new ObservableVariable<bool>(false); // whether the player is going to shoot where they're aiming
    public ObservableVariable<bool> CharacterIsReloading = new ObservableVariable<bool>(false); // if the player is done reloading (in worldspace)
    #endregion
}
