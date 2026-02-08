using UnityEngine;
using UnityEngine.InputSystem;

/// Simple local-only input driver that writes Input System values into PlayerData.
[RequireComponent(typeof(PlayerData))]
public class LocalInputDriver : MonoBehaviour
{
    [SerializeField]
    private PlayerInput scenePlayerInput;

    private PlayerData player;

    private InputAction moveAction;
    private InputAction lookAction;
    private InputAction fireAction;
    private InputAction reloadAction;
    private InputAction aimAction;
    private InputAction leanAction;
    private InputAction sprintAction;

    private void Awake()
    {
        TryGetComponent(out player);
    }

    private void OnEnable()
    {
        ResolveInputActions();
    }

    private void OnDisable()
    {
        moveAction = null;
        lookAction = null;
        fireAction = null;
        reloadAction = null;
        aimAction = null;
        leanAction = null;
        sprintAction = null;
    }

    private void ResolveInputActions()
    {
        if (scenePlayerInput == null || !scenePlayerInput.isActiveAndEnabled)
            scenePlayerInput = FindFirstObjectByType<PlayerInput>();

        if (scenePlayerInput == null || scenePlayerInput.actions == null)
            return;

        var actions = scenePlayerInput.actions;
        moveAction = actions.FindAction("Move", false);
        lookAction = actions.FindAction("Look", false);
        fireAction = actions.FindAction("Fire", false);
        reloadAction = actions.FindAction("Reload", false);
        aimAction = actions.FindAction("Aim", false);
        leanAction = actions.FindAction("Lean", false);
        sprintAction = actions.FindAction("Sprint", false);
    }

    private bool CanDriveThisPlayer()
    {
        if (player == null)
            return false;

        if (player.IsLocalPlayer)
            return true;

        return PlayerData.LocalPlayer == player;
    }

    private void Update()
    {
        if (player == null)
        {
            player = PlayerData.OwnerPlayer;
            if (player == null)
                return;
        }

        if (moveAction == null || lookAction == null)
            ResolveInputActions();

        if (!CanDriveThisPlayer())
            return;

        if (moveAction != null)
            player.SetInputMove(moveAction.ReadValue<Vector2>());

        if (lookAction != null)
            player.SetInputLook(lookAction.ReadValue<Vector2>());

        if (fireAction != null)
        {
            bool isFiring = fireAction.IsPressed();
            if (isFiring) player.SetInput_IsFiring(); else player.SetInput_IsNotFiring();
        }

        if (reloadAction != null)
        {
            bool isReload = reloadAction.IsPressed();
            if (isReload) player.SetInput_IsReloading(); else player.SetInput_IsNotReloading();
        }

        if (aimAction != null)
        {
            bool isAiming = aimAction.IsPressed();
            if (isAiming) player.SetInput_IsAiming(); else player.SetInput_IsNotAiming();
        }

        if (leanAction != null)
        {
            // Map [-1,1] to [0,1]
            float raw = leanAction.ReadValue<float>();
            float mapped = raw * 0.5f + 0.5f;
            player.SetInput_Lean(mapped);
        }

        if (sprintAction != null)
        {
            bool isSprint = sprintAction.IsPressed();
            if (isSprint) player.SetInput_IsSprinting(); else player.SetInput_IsNotSprinting();
        }
    }
}
