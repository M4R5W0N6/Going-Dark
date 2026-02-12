using System.Collections.Generic;
using Fusion;
using UnityEngine;
using LocalData;

public class PlayerData : MonoBehaviour
{
    private static List<PlayerData> players;
    public static List<PlayerData> Players
    {
        get
        {
            players = new List<PlayerData>(FindObjectsByType<PlayerData>(FindObjectsSortMode.None));

            return players;
        }
    }

    public static PlayerData GetPlayer(ulong ownerId)
    {
        List<PlayerData> currentPlayers = Players;
        for (int i = 0; i < currentPlayers.Count; i++)
        {
            if (currentPlayers[i] != null && currentPlayers[i].NetworkOwnerId == ownerId)
                return currentPlayers[i];
        }

        return GetSingleActiveFallback(currentPlayers);
    }

    private static PlayerData ownerPlayer;
    public static PlayerData OwnerPlayer
    {
        get
        {
            ownerPlayer = LocalPlayer;
            return ownerPlayer;
        }
    }

    private static PlayerData localPlayer;
    public static PlayerData LocalPlayer
    {
        get
        {
            List<PlayerData> currentPlayers = Players;
            localPlayer = ResolveLocalPlayer(currentPlayers, localPlayer);
            return localPlayer;
        }
    }

    [SerializeField]
    private bool isLocalPlayer;
    public bool IsLocalPlayer
    {
        get => isLocalPlayer;
        set
        {
            isLocalPlayer = value;

            if (value)
                SetLocalPlayer(this);
            else if (localPlayer == this)
                localPlayer = null;
        }
    }

    [SerializeField]
    private ulong networkOwnerId;
    public ulong NetworkOwnerId => networkOwnerId;

    public static void SetLocalPlayer(PlayerData player)
    {
        localPlayer = player;
        ownerPlayer = player;
    }

    public void SetNetworkOwnerId(ulong ownerId)
    {
        networkOwnerId = ownerId;
    }

    private void Start() { }
    private void OnDisable()
    {
        if (localPlayer == this)
            localPlayer = null;

        if (ownerPlayer == this)
            ownerPlayer = null;
    }

    private void OnDestroy()
    {
        if (localPlayer == this)
            localPlayer = null;

        if (ownerPlayer == this)
            ownerPlayer = null;
    }

    private void Update()
    {
        if (LocalPlayer != this)
            return;

        Camera camera = CustomUtilities.GetBestCamera(transform);
        CameraPosition.Value = camera ? camera.transform.position : Vector3.zero;
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

    private static bool IsValidCachedLocalPlayer(PlayerData player)
    {
        return player != null && player.isActiveAndEnabled && player.gameObject.activeInHierarchy;
    }

    private static PlayerData ResolveLocalPlayer(List<PlayerData> currentPlayers, PlayerData previousSelection)
    {
        List<PlayerData> localCandidates = new List<PlayerData>();

        for (int i = 0; i < currentPlayers.Count; i++)
        {
            PlayerData player = currentPlayers[i];
            if (player == null || !player.isLocalPlayer || !player.isActiveAndEnabled || !player.gameObject.activeInHierarchy)
                continue;

            localCandidates.Add(player);
        }

        if (localCandidates.Count == 1)
            return localCandidates[0];

        if (localCandidates.Count > 1)
        {
            PlayerData sceneMatch = FindCandidateInPresentationScene(localCandidates);
            if (sceneMatch != null)
                return sceneMatch;

            if (previousSelection != null)
            {
                for (int i = 0; i < localCandidates.Count; i++)
                {
                    if (localCandidates[i] == previousSelection)
                        return previousSelection;
                }
            }

            PlayerData lowestOwnerIdCandidate = null;
            ulong lowestOwnerId = ulong.MaxValue;
            for (int i = 0; i < localCandidates.Count; i++)
            {
                PlayerData candidate = localCandidates[i];
                if (candidate == null)
                    continue;

                if (candidate.networkOwnerId < lowestOwnerId)
                {
                    lowestOwnerId = candidate.networkOwnerId;
                    lowestOwnerIdCandidate = candidate;
                }
            }

            if (lowestOwnerIdCandidate != null)
                return lowestOwnerIdCandidate;
        }

        return GetSingleActiveFallback(currentPlayers);
    }

    private static PlayerData FindCandidateInPresentationScene(List<PlayerData> localCandidates)
    {
        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return null;

        int cameraSceneHandle = mainCamera.gameObject.scene.handle;
        for (int i = 0; i < localCandidates.Count; i++)
        {
            PlayerData candidate = localCandidates[i];
            if (candidate == null)
                continue;

            if (candidate.gameObject.scene.handle == cameraSceneHandle)
                return candidate;

            if (!candidate.TryGetOwningRunner(out NetworkRunner runner))
                continue;

            if (runner.gameObject.scene.handle == cameraSceneHandle || runner.SimulationUnityScene.handle == cameraSceneHandle)
                return candidate;
        }

        return null;
    }

    private bool TryGetOwningRunner(out NetworkRunner runner)
    {
        runner = null;

        if (!TryGetComponent(out NetworkObject networkObject) || networkObject == null)
            return false;

        if (networkObject.Runner == null || !networkObject.Runner.IsRunning)
            return false;

        runner = networkObject.Runner;
        return true;
    }

    private static PlayerData GetSingleActiveFallback(List<PlayerData> currentPlayers)
    {
        PlayerData fallback = null;
        int activeCount = 0;

        for (int i = 0; i < currentPlayers.Count; i++)
        {
            PlayerData player = currentPlayers[i];
            if (player == null || !player.isActiveAndEnabled || !player.gameObject.activeInHierarchy)
                continue;

            fallback = player;
            activeCount++;
            if (activeCount > 1)
                return null;
        }

        return fallback;
    }
}
