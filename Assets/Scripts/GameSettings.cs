using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Services.Core;

public class GameSettings : MonoBehaviour
{
    private async void Awake()
    {
        try
        {
            await UnityServices.InitializeAsync();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
        }
    }

    private void OnApplicationFocus(bool focus)
    {
        Cursor.lockState = CursorLockMode.Confined;

        Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, true);
    }

    //protected override void OnFire(InputAction.CallbackContext context)
    //{
    //    if (context.performed)
    //    {
    //        Cursor.lockState = Screen.fullScreen ? CursorLockMode.Locked : CursorLockMode.Confined;
    //    }
    //}

    public void OnEscape(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, !Screen.fullScreen);

            //Cursor.lockState = Screen.fullScreen ? CursorLockMode.Locked : CursorLockMode.Confined;
        }
    }
}
