using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class PlayerInputListener : MonoBehaviour
{
    protected bool isSprinting;

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
    protected virtual void Update()
    {
        if (!PlayerInput)
            PlayerInput = FindObjectOfType<PlayerInput>();
    }

    private void InitPlayerInput()
    {
        PlayerInput.actions["Move"].started += OnMove;
        PlayerInput.actions["Look"].started += OnLook;
        PlayerInput.actions["Fire"].started += OnFire;
        PlayerInput.actions["Reload"].started += OnReload;
        PlayerInput.actions["Aim"].started += OnAim;
        PlayerInput.actions["Lean"].started += OnLean;
        PlayerInput.actions["Sprint"].started += OnSprint;
        PlayerInput.actions["Escape"].started += OnEscape;

        PlayerInput.actions["Move"].performed += OnMove;
        PlayerInput.actions["Look"].performed += OnLook;
        PlayerInput.actions["Fire"].performed += OnFire;
        PlayerInput.actions["Reload"].performed += OnReload;
        PlayerInput.actions["Aim"].performed += OnAim;
        PlayerInput.actions["Lean"].performed += OnLean;
        PlayerInput.actions["Sprint"].performed += OnSprint;
        PlayerInput.actions["Escape"].performed += OnEscape;

        PlayerInput.actions["Move"].canceled += OnMove;
        PlayerInput.actions["Look"].canceled += OnLook;
        PlayerInput.actions["Fire"].canceled += OnFire;
        PlayerInput.actions["Reload"].canceled += OnReload;
        PlayerInput.actions["Aim"].canceled += OnAim;
        PlayerInput.actions["Lean"].canceled += OnLean; 
        PlayerInput.actions["Sprint"].canceled += OnSprint;
        PlayerInput.actions["Escape"].canceled += OnEscape;
    }
    private void OnDisable()
    {
        if (!PlayerInput)
            return;

        PlayerInput.actions["Move"].started -= OnMove;
        PlayerInput.actions["Look"].started -= OnLook;
        PlayerInput.actions["Fire"].started -= OnFire;
        PlayerInput.actions["Reload"].started -= OnReload;
        PlayerInput.actions["Aim"].started -= OnAim;
        PlayerInput.actions["Lean"].started -= OnLean;
        PlayerInput.actions["Sprint"].started -= OnSprint;
        PlayerInput.actions["Escape"].started -= OnEscape;

        PlayerInput.actions["Move"].performed -= OnMove;
        PlayerInput.actions["Look"].performed -= OnLook;
        PlayerInput.actions["Fire"].performed -= OnFire;
        PlayerInput.actions["Reload"].performed -= OnReload;
        PlayerInput.actions["Aim"].performed -= OnAim;
        PlayerInput.actions["Lean"].performed -= OnLean;
        PlayerInput.actions["Sprint"].performed -= OnSprint;
        PlayerInput.actions["Escape"].performed -= OnEscape;

        PlayerInput.actions["Move"].canceled -= OnMove;
        PlayerInput.actions["Look"].canceled -= OnLook;
        PlayerInput.actions["Fire"].canceled -= OnFire;
        PlayerInput.actions["Reload"].canceled -= OnReload;
        PlayerInput.actions["Aim"].canceled -= OnAim;
        PlayerInput.actions["Lean"].canceled -= OnLean;
        PlayerInput.actions["Sprint"].canceled -= OnSprint;
        PlayerInput.actions["Escape"].canceled -= OnEscape;
    }

    protected virtual void OnMove(InputAction.CallbackContext context) { }
    protected virtual void OnLook(InputAction.CallbackContext context) { }
    protected virtual void OnFire(InputAction.CallbackContext context) { }
    protected virtual void OnReload(InputAction.CallbackContext context) { }
    protected virtual void OnAim(InputAction.CallbackContext context) { }
    protected virtual void OnLean(InputAction.CallbackContext context) { }
    protected virtual void OnSprint(InputAction.CallbackContext context) 
    { 
        if (context.performed)
        {
            isSprinting = true;
        }
        else if (context.canceled)
        {
            isSprinting = false;
        }
    }
    protected virtual void OnEscape(InputAction.CallbackContext context) { }

}
