using UnityEngine;
using UnityEngine.InputSystem;

/// Simple local-only input driver that writes Input System values into PlayerData
[RequireComponent(typeof(PlayerData))]
[RequireComponent(typeof(PlayerInput))]
public class LocalInputDriver : MonoBehaviour
{
    private PlayerInput playerInput;
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
        TryGetComponent(out playerInput);
        TryGetComponent(out player);
    }

    private void OnEnable()
    {
        if (playerInput == null) return;

        var actions = playerInput.actions;
        moveAction = actions["Move"];lookAction = actions["Look"];fireAction = actions["Fire"];reloadAction = actions["Reload"];aimAction = actions["Aim"];leanAction = actions["Lean"];sprintAction = actions["Sprint"]; 

        moveAction?.Enable();
        lookAction?.Enable();
        fireAction?.Enable();
        reloadAction?.Enable();
        aimAction?.Enable();
        leanAction?.Enable();
        sprintAction?.Enable();
    }

    private void OnDisable()
    {
        moveAction?.Disable();
        lookAction?.Disable();
        fireAction?.Disable();
        reloadAction?.Disable();
        aimAction?.Disable();
        leanAction?.Disable();
        sprintAction?.Disable();
    }

    private void Update()
    {
        if (player == null)
        {
            player = PlayerData.OwnerPlayer;
            if (player == null) return;
        }

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
            // Map [-1,1] → [0,1]
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


