using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class UIInputListener : MonoBehaviour
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
    protected virtual void Update()
    {
        if (!PlayerInput)
            PlayerInput = FindObjectOfType<PlayerInput>();
    }

    private void InitPlayerInput()
    {
        PlayerInput.actions["Escape"].started += OnEscape;

        PlayerInput.actions["Escape"].performed += OnEscape;

        PlayerInput.actions["Escape"].canceled += OnEscape;
    }
    private void OnDisable()
    {
        if (!PlayerInput)
            return;

        PlayerInput.actions["Escape"].started -= OnEscape;

        PlayerInput.actions["Escape"].performed -= OnEscape;

        PlayerInput.actions["Escape"].canceled -= OnEscape;
    }

    protected virtual void OnEscape(InputAction.CallbackContext context) { }

}
