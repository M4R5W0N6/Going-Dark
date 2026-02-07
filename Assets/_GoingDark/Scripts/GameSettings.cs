using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class GameSettings : MonoBehaviour
{
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

    #region StaticVariables
    //private static bool m_IsLocked;
    //public static bool IsLocked
    //{
    //    get
    //    {
    //        return m_IsLocked;
    //    }
    //    set
    //    {
    //        if (m_IsLocked == value)
    //            return;

    //        m_IsLocked = value;

    //        Cursor.lockState = CursorLockMode.None;
    //        Cursor.lockState = m_IsLocked ? CursorLockMode.Locked : CursorLockMode.Confined;
    //        Cursor.visible = !m_IsLocked;

    //        //Screen.SetResolution(Screen.currentResolution.width, Screen.currentResolution.height, m_IsLocked);
    //    }
    //}

    //private static bool m_InvertY;
    //public static bool InvertY
    //{
    //    get
    //    {
    //        return m_InvertY;
    //    }
    //    set
    //    {
    //        if (m_InvertY == value)
    //            return;

    //        m_InvertY = value;
    //    }
    //}

    //public static readonly float ControllerSensitivityModifier = 100f;

    public static readonly float DefaultSensitivity = 0.05f;
    private static float m_Sensitivity;
    public static float Sensitivity
    {
        get
        {

            return /*(IsOnController ? ControllerSensitivityModifier : 1f) **/ m_Sensitivity != 0f ? m_Sensitivity : DefaultSensitivity;
        }
        set
        {
            if (m_Sensitivity == value)
                return;

            m_Sensitivity = value;
        }
    }
    public static readonly float DefaultAimSensitivity = 0.5f;
    private static float m_AimSensitivity;
    public static float AimSensitivity
    {
        get
        {
            return /*(IsOnController ? ControllerSensitivityModifier : 1f) **/ (m_AimSensitivity != 0f ? m_AimSensitivity : DefaultAimSensitivity) * Sensitivity;
        }
        set
        {
            if (m_AimSensitivity == value)
                return;

            m_AimSensitivity = value;
        }
    }
    //public static bool IsOnController;

    #endregion

    //private void Awake()
    //{
    //    PlayerInput playerInput = FindObjectOfType<PlayerInput>();

    //    if (!playerInput)
    //    {
    //        Debug.LogWarning("GameSettings: found no PlayerInput object in scene, will not subscribe to callbacks !!");

    //        return;
    //    }

    //    playerInput.onControlsChanged += OnControlsChanged;
    //}

    ////Method called  when a device change event is fired
    //private static void OnControlsChanged(PlayerInput input)
    //{
    //    switch (input.currentControlScheme)
    //    {
    //        //Gamepad
    //        case "Gamepad":
    //            IsOnController = true;
    //            break;

    //        //Keyboard & Mouse
    //        case "Keyboard&Mouse":
    //            IsOnController = false;
    //            break;

    //        //Else
    //        default:
    //            break;
    //    }

    //    Debug.Log($"Controls switched to {input.currentControlScheme}");
    //}
}
