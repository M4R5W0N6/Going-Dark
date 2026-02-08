using Fusion;
using FOW;
using RootMotion.FinalIK;
using UnityEngine;

[RequireComponent(typeof(NetworkObject))]
[RequireComponent(typeof(PlayerData))]
[RequireComponent(typeof(CharacterInputController))]
public class FusionPlayerBridge : NetworkBehaviour
{
    [SerializeField]
    private PlayerData playerData;

    [SerializeField]
    private CharacterInputController characterInputController;

    [SerializeField]
    private AnimationInputController animationInputController;

    [SerializeField]
    private Rigidbody characterRigidbody;
    private FogOfWarRevealer[] fogRevealers;
    private FogOfWarHider[] fogHiders;
    private GrounderFBBIK grounderIK;

    [Networked]
    private Vector3 NetworkPosition { get; set; }

    [Networked]
    private Quaternion NetworkRotation { get; set; }

    [Networked]
    private Vector2 NetworkInputMove { get; set; }

    [Networked]
    private Vector2 NetworkInputLook { get; set; }

    [Networked]
    private float NetworkInputLean { get; set; }

    [Networked]
    private NetworkBool NetworkInputFire { get; set; }

    [Networked]
    private NetworkBool NetworkInputReload { get; set; }

    [Networked]
    private NetworkBool NetworkInputAim { get; set; }

    [Networked]
    private NetworkBool NetworkInputSprint { get; set; }

    [Networked]
    private Vector2 NetworkCharacterMove { get; set; }

    [Networked]
    private Vector2 NetworkCharacterTurn { get; set; }

    [Networked]
    private Vector3 NetworkCharacterOriginPosition { get; set; }

    [Networked]
    private Vector3 NetworkCharacterTargetPosition { get; set; }

    [Networked]
    private Vector3 NetworkCharacterRaycastPosition { get; set; }

    [Networked]
    private NetworkBool NetworkCharacterIsOnTarget { get; set; }

    [Networked]
    private NetworkBool NetworkCharacterIsReloading { get; set; }

    private bool lastHasStateAuthority;
    private bool lastHasInputAuthority;
    private bool lastIsPresentationRunner;

    private void Awake()
    {
        ResolveReferences();
    }

    public override void Spawned()
    {
        ResolveReferences();
        ApplyAuthorityState(force: true);

        if (Object.HasStateAuthority)
            CaptureState();
        else
            ApplyReplicatedState();
    }

    public override void FixedUpdateNetwork()
    {
        ApplyAuthorityState(force: false);

        if (!Object.HasStateAuthority || playerData == null)
            return;

        if (GetInput(out FusionNetworkInput input))
            ApplyInput(in input);
        else
            ApplyInput(default);

        if (characterInputController != null)
            characterInputController.SimulateStateAuthorityStep();

        CaptureState();
    }

    public override void Render()
    {
        ApplyAuthorityState(force: false);

        if (!Object.HasStateAuthority)
            ApplyReplicatedState();
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (playerData != null && playerData.IsLocalPlayer)
            playerData.IsLocalPlayer = false;
    }

    private void ResolveReferences()
    {
        if (playerData == null)
            TryGetComponent(out playerData);

        if (characterInputController == null)
            TryGetComponent(out characterInputController);

        if (animationInputController == null)
            animationInputController = GetComponentInChildren<AnimationInputController>(true);

        if (characterRigidbody == null)
            TryGetComponent(out characterRigidbody);

        if (fogRevealers == null || fogRevealers.Length == 0)
            fogRevealers = GetComponentsInChildren<FogOfWarRevealer>(true);

        if (fogHiders == null || fogHiders.Length == 0)
            fogHiders = GetComponentsInChildren<FogOfWarHider>(true);

        if (grounderIK == null)
            grounderIK = GetComponentInChildren<GrounderFBBIK>(true);
    }

    private void ApplyAuthorityState(bool force)
    {
        bool hasStateAuthority = Object != null && Object.HasStateAuthority;
        bool hasInputAuthority = Object != null && Object.HasInputAuthority;
        bool isPresentationRunner = IsPresentationRunner();

        if (!force &&
            lastHasStateAuthority == hasStateAuthority &&
            lastHasInputAuthority == hasInputAuthority &&
            lastIsPresentationRunner == isPresentationRunner)
            return;

        lastHasStateAuthority = hasStateAuthority;
        lastHasInputAuthority = hasInputAuthority;
        lastIsPresentationRunner = isPresentationRunner;

        if (playerData != null)
        {
            bool shouldBeLocal = hasInputAuthority && isPresentationRunner;
            if (playerData.IsLocalPlayer != shouldBeLocal)
                playerData.IsLocalPlayer = shouldBeLocal;

            if (Object != null)
                playerData.SetNetworkOwnerId(ToOwnerId(Object.InputAuthority));
        }

        if (characterInputController != null)
            characterInputController.enabled = hasStateAuthority;

        if (characterRigidbody != null)
            characterRigidbody.isKinematic = !hasStateAuthority;

        if (grounderIK != null)
        {
            grounderIK.enabled = hasInputAuthority && isPresentationRunner;
        }

        if (fogRevealers != null)
        {
            for (int i = 0; i < fogRevealers.Length; i++)
            {
                FogOfWarRevealer revealer = fogRevealers[i];
                if (revealer != null)
                    revealer.enabled = hasInputAuthority && isPresentationRunner;
            }
        }

        if (fogHiders != null)
        {
            for (int i = 0; i < fogHiders.Length; i++)
            {
                FogOfWarHider hider = fogHiders[i];
                if (hider != null)
                    hider.enabled = !hasInputAuthority && isPresentationRunner;
            }
        }
    }

    private bool IsPresentationRunner()
    {
        if (Object == null || Object.Runner == null || !Object.Runner.IsRunning)
            return true;

        Camera mainCamera = Camera.main;
        if (mainCamera == null)
            return Object.Runner.GetVisible();

        int cameraSceneHandle = mainCamera.gameObject.scene.handle;
        if (gameObject.scene.handle == cameraSceneHandle)
            return true;

        return Object.Runner.SimulationUnityScene.handle == cameraSceneHandle;
    }

    private static ulong ToOwnerId(PlayerRef playerRef)
    {
        return playerRef.RawEncoded > 0 ? (ulong)(uint)playerRef.RawEncoded : 0ul;
    }

    private void ApplyInput(in FusionNetworkInput input)
    {
        if (playerData == null)
            return;

        playerData.SetInputMove(input.Move);
        playerData.SetInputLook(input.Look);
        playerData.SetInput_Lean(input.Lean);

        if (input.Buttons.IsSet(FusionNetworkInput.FireButton))
            playerData.SetInput_IsFiring();
        else
            playerData.SetInput_IsNotFiring();

        if (input.Buttons.IsSet(FusionNetworkInput.ReloadButton))
            playerData.SetInput_IsReloading();
        else
            playerData.SetInput_IsNotReloading();

        if (input.Buttons.IsSet(FusionNetworkInput.AimButton))
            playerData.SetInput_IsAiming();
        else
            playerData.SetInput_IsNotAiming();

        if (input.Buttons.IsSet(FusionNetworkInput.SprintButton))
            playerData.SetInput_IsSprinting();
        else
            playerData.SetInput_IsNotSprinting();
    }

    private void CaptureState()
    {
        if (playerData == null)
            return;

        NetworkPosition = transform.position;
        NetworkRotation = transform.rotation;

        NetworkInputMove = playerData.InputMove.Value;
        NetworkInputLook = playerData.InputLook.Value;
        NetworkInputLean = playerData.InputLean.Value;
        NetworkInputFire = playerData.InputFire.Value;
        NetworkInputReload = playerData.InputReload.Value;
        NetworkInputAim = playerData.InputAim.Value;
        NetworkInputSprint = playerData.InputSprint.Value;

        NetworkCharacterMove = playerData.CharacterMove.Value;
        NetworkCharacterTurn = playerData.CharacterTurn.Value;
        NetworkCharacterOriginPosition = playerData.CharacterOriginPosition.Value;
        NetworkCharacterTargetPosition = playerData.CharacterTargetPosition.Value;
        NetworkCharacterRaycastPosition = playerData.CharacterRaycastPosition.Value;
        NetworkCharacterIsOnTarget = playerData.CharacterIsOnTarget.Value;
        NetworkCharacterIsReloading = playerData.CharacterIsReloading.Value;
    }

    private void ApplyReplicatedState()
    {
        transform.SetPositionAndRotation(NetworkPosition, NetworkRotation);

        if (playerData == null)
            return;

        playerData.InputMove.Value = NetworkInputMove;
        playerData.InputLook.Value = NetworkInputLook;
        playerData.InputLean.Value = NetworkInputLean;
        playerData.InputFire.Value = NetworkInputFire;
        playerData.InputReload.Value = NetworkInputReload;
        playerData.InputAim.Value = NetworkInputAim;
        playerData.InputSprint.Value = NetworkInputSprint;

        playerData.CharacterMove.Value = NetworkCharacterMove;
        playerData.CharacterTurn.Value = NetworkCharacterTurn;
        playerData.CharacterOriginPosition.Value = NetworkCharacterOriginPosition;
        playerData.CharacterTargetPosition.Value = NetworkCharacterTargetPosition;
        playerData.CharacterRaycastPosition.Value = NetworkCharacterRaycastPosition;
        playerData.CharacterIsOnTarget.Value = NetworkCharacterIsOnTarget;
        playerData.CharacterIsReloading.Value = NetworkCharacterIsReloading;
    }
}
