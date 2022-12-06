using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameSettings : PlayerInputListener
{
    private void OnApplicationFocus(bool focus)
    {
        Cursor.lockState = CursorLockMode.Locked;

        Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, true);
    }

    protected override void OnEscape(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            Cursor.lockState = Cursor.lockState == CursorLockMode.Locked ? CursorLockMode.None : CursorLockMode.Locked;

            Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, !Screen.fullScreen);
        }
    }
}
