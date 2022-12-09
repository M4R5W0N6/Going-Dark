using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Cinemachine;
using Unity.Netcode;

public class CameraInputController : MonoBehaviour
{
    [SerializeField]
    private CinemachineVirtualCamera defaultCamera, aimingCamera;
    private Cinemachine3rdPersonFollow defaultFollow, aimingFollow;
    private float leanAmount = 1f;
    [SerializeField]
    private float leanSpeed = 2f;

    private void Awake()
    {
        defaultFollow = defaultCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
        aimingFollow = aimingCamera.GetCinemachineComponent<Cinemachine3rdPersonFollow>();
    }

    public void OnAim()
    {
        defaultCamera.gameObject.SetActive(false);
        aimingCamera.gameObject.SetActive(true);
    }
    public void OnAimEnd()
    {
        defaultCamera.gameObject.SetActive(true);
        aimingCamera.gameObject.SetActive(false);
    }
    public void OnLean(float value)
    {
        leanAmount = value;
    }

    private void FixedUpdate()
    {
        defaultFollow.CameraSide = Mathf.Lerp(defaultFollow.CameraSide, leanAmount, Time.fixedDeltaTime * leanSpeed);
        aimingFollow.CameraSide = Mathf.Lerp(aimingFollow.CameraSide, leanAmount, Time.fixedDeltaTime * leanSpeed);
    }
}
