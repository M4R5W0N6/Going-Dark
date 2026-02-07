using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;

public class CameraInputController : MonoBehaviour, IEventListener
{
    [SerializeField]
    private CinemachineVirtualCamera menuCamera, defaultCamera, aimingCamera;
    private Cinemachine3rdPersonFollow defaultFollow, aimingFollow;
    private float leanAmount = 1f;
    private bool isRoundActive = true;
    private bool isAiming;
    private CinemachineVirtualCamera activeCamera;
    [SerializeField]
    private float leanSpeed = 2f;

    private void Awake()
    {
        if (defaultCamera)
            defaultFollow = defaultCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
        if (aimingCamera)
            aimingFollow = aimingCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();

        // Switch directly to gameplay camera in single-player
        SwitchToCamera(defaultCamera);
    }

    private void FixedUpdate()
    {
        var player = PlayerData.OwnerPlayer;
        if (player != null)
        {
            leanAmount = player.InputLean.Value;
            isAiming = player.InputAim.Value;

            if (isRoundActive)
                SwitchToCamera(isAiming ? aimingCamera : defaultCamera);
        }

        if (defaultFollow != null)
            defaultFollow.CameraSide = Mathf.Lerp(defaultFollow.CameraSide, leanAmount, Time.fixedDeltaTime * leanSpeed);
        if (aimingFollow != null)
            aimingFollow.CameraSide = Mathf.Lerp(aimingFollow.CameraSide, leanAmount, Time.fixedDeltaTime * leanSpeed);
    }

    #region EventCallbacks
    public void InputAimCallback(bool previousValue, bool currentValue)
    {
        isAiming = currentValue;
        if (isRoundActive)
            SwitchToCamera(isAiming ? aimingCamera : defaultCamera);
    }
    public void InputLeanCallback(float previousValue, float currentValue)
    {
        leanAmount = currentValue;
    }

    public void RoundStartCallback()
    {
        isRoundActive = true;
        SwitchToCamera(isAiming ? aimingCamera : defaultCamera);
    }
    public void RoundEndCallback()
    {
        isRoundActive = false;
        SwitchToCamera(menuCamera);
    }
    #endregion

    private void SwitchToCamera(CinemachineVirtualCamera camera)
    {
        if (activeCamera == camera)
            return;

        activeCamera = camera;

        if (menuCamera)
            menuCamera.gameObject.SetActive(menuCamera == camera);
        if (defaultCamera)
            defaultCamera.gameObject.SetActive(defaultCamera == camera);
        if (aimingCamera)
            aimingCamera.gameObject.SetActive(aimingCamera == camera);
    }
}
