using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;
using Unity.Netcode;

public class CameraInputController : MonoBehaviour, IEventListener
{
    [SerializeField]
    private CinemachineVirtualCamera menuCamera, defaultCamera, aimingCamera;
    private Cinemachine3rdPersonFollow defaultFollow, aimingFollow;
    private float leanAmount = 1f;
    [SerializeField]
    private float leanSpeed = 2f;

    private void Awake()
    {
        defaultFollow = defaultCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
        aimingFollow = aimingCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();

        SwitchToCamera(menuCamera);
    }

    private void FixedUpdate()
    {
        defaultFollow.CameraSide = Mathf.Lerp(defaultFollow.CameraSide, leanAmount, Time.fixedDeltaTime * leanSpeed);
        aimingFollow.CameraSide = Mathf.Lerp(aimingFollow.CameraSide, leanAmount, Time.fixedDeltaTime * leanSpeed);
    }

    #region EventCallbacks
    public void InputAimCallback(bool previousValue, bool currentValue)
    {
        if (currentValue)
        {
            if (GameManager.IsInRound)
            {
                SwitchToCamera(aimingCamera);
            }
        }
        else
        {
            if (GameManager.IsInRound)
            {
                SwitchToCamera(defaultCamera);
            }
        }
    }
    public void InputLeanCallback(float previousValue, float currentValue)
    {
        if (GameManager.IsInRound)
        {
            leanAmount = currentValue;
        }
    }

    public void RoundStartCallback()
    {
        SwitchToCamera(defaultCamera);
    }
    public void RoundEndCallback()
    {
        SwitchToCamera(menuCamera);
    }
    #endregion

    private void SwitchToCamera(CinemachineVirtualCamera camera)
    {
        menuCamera.gameObject.SetActive(menuCamera == camera);
        defaultCamera.gameObject.SetActive(defaultCamera == camera);
        aimingCamera.gameObject.SetActive(aimingCamera == camera);
    }
}
