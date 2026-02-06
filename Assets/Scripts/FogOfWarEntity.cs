using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using FOW;

public class FogOfWarEntity : MonoBehaviour
{
    private FogOfWarRevealer revealer;
    private PlayerInput localPlayerInput;

    [SerializeField]
    private bool isPeripheral;

    private void Awake()
    {
        TryGetComponent(out revealer);
        localPlayerInput = GetComponentInParent<PlayerInput>();
    }

    private void OnEnable()
    {
        ApplyRevealerState();
    }

    private void Start()
    {
        // Re-evaluate after startup in case PlayerInput initializes after this component.
        ApplyRevealerState();
    }

    private void OnDisable()
    {
        if (revealer)
            revealer.enabled = false;
    }

    private void ApplyRevealerState()
    {
        if (!revealer)
            return;

        if (localPlayerInput == null)
            localPlayerInput = GetComponentInParent<PlayerInput>();

        bool isLocalControlled = localPlayerInput != null;
        revealer.enabled = isLocalControlled || !isPeripheral;
    }
}
