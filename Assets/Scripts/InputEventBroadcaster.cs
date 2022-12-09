using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Events;
using Unity.Netcode;

public class InputEventBroadcaster : MonoBehaviour
{
    private PlayerInput playerInput;
    public PlayerInput PlayerInput {

        get
        {
            return playerInput;
        }
        set
        {
            playerInput = value;

            InitPlayerInput();
        }
    }
    private void Update()
    {
        if (!PlayerInput)
            PlayerInput = FindObjectOfType<PlayerInput>();
    }

    private void InitPlayerInput()
    {
        PlayerInput.actions["Move"].started += OnMove.Invoke;
        PlayerInput.actions["Look"].started += OnLook.Invoke;
        PlayerInput.actions["Fire"].started += OnFire.Invoke;
        PlayerInput.actions["Reload"].started += OnReload.Invoke;
        PlayerInput.actions["Aim"].started += OnAim.Invoke;
        PlayerInput.actions["Lean"].started += OnLean.Invoke;
        PlayerInput.actions["Sprint"].started += OnSprint.Invoke;
        PlayerInput.actions["Escape"].started += OnEscape.Invoke;

        PlayerInput.actions["Move"].performed += OnMove.Invoke;
        PlayerInput.actions["Look"].performed += OnLook.Invoke;
        PlayerInput.actions["Fire"].performed += OnFire.Invoke;
        PlayerInput.actions["Reload"].performed += OnReload.Invoke;
        PlayerInput.actions["Aim"].performed += OnAim.Invoke;
        PlayerInput.actions["Lean"].performed += OnLean.Invoke;
        PlayerInput.actions["Sprint"].performed += OnSprint.Invoke;
        PlayerInput.actions["Escape"].performed += OnEscape.Invoke;

        PlayerInput.actions["Move"].canceled += OnMove.Invoke;
        PlayerInput.actions["Look"].canceled += OnLook.Invoke;
        PlayerInput.actions["Fire"].canceled += OnFire.Invoke;
        PlayerInput.actions["Reload"].canceled += OnReload.Invoke;
        PlayerInput.actions["Aim"].canceled += OnAim.Invoke;
        PlayerInput.actions["Lean"].canceled += OnLean.Invoke; 
        PlayerInput.actions["Sprint"].canceled += OnSprint.Invoke;
        PlayerInput.actions["Escape"].canceled += OnEscape.Invoke;
    }
    private void OnDisable()
    {
        if (!PlayerInput)
            return;

        PlayerInput.actions["Move"].started -= OnMove.Invoke;
        PlayerInput.actions["Look"].started -= OnLook.Invoke;
        PlayerInput.actions["Fire"].started -= OnFire.Invoke;
        PlayerInput.actions["Reload"].started -= OnReload.Invoke;
        PlayerInput.actions["Aim"].started -= OnAim.Invoke;
        PlayerInput.actions["Lean"].started -= OnLean.Invoke;
        PlayerInput.actions["Sprint"].started -= OnSprint.Invoke;
        PlayerInput.actions["Escape"].started -= OnEscape.Invoke;

        PlayerInput.actions["Move"].performed -= OnMove.Invoke;
        PlayerInput.actions["Look"].performed -= OnLook.Invoke;
        PlayerInput.actions["Fire"].performed -= OnFire.Invoke;
        PlayerInput.actions["Reload"].performed -= OnReload.Invoke;
        PlayerInput.actions["Aim"].performed -= OnAim.Invoke;
        PlayerInput.actions["Lean"].performed -= OnLean.Invoke;
        PlayerInput.actions["Sprint"].performed -= OnSprint.Invoke;
        PlayerInput.actions["Escape"].performed -= OnEscape.Invoke;

        PlayerInput.actions["Move"].canceled -= OnMove.Invoke;
        PlayerInput.actions["Look"].canceled -= OnLook.Invoke;
        PlayerInput.actions["Fire"].canceled -= OnFire.Invoke;
        PlayerInput.actions["Reload"].canceled -= OnReload.Invoke;
        PlayerInput.actions["Aim"].canceled -= OnAim.Invoke;
        PlayerInput.actions["Lean"].canceled -= OnLean.Invoke;
        PlayerInput.actions["Sprint"].canceled -= OnSprint.Invoke;
        PlayerInput.actions["Escape"].canceled -= OnEscape.Invoke;
    }

    public InputEvent OnMove;
    public InputEvent OnLook;
    public InputEvent OnFire;
    public InputEvent OnReload;
    public InputEvent OnAim;
    public InputEvent OnLean;
    public InputEvent OnSprint;
    public InputEvent OnEscape;
}

[System.Serializable]
public class InputEvent : UnityEvent<InputAction.CallbackContext> { }
