using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using RootMotion.FinalIK;
using Unity.Netcode;

[RequireComponent(typeof(Animator))]
public class AnimationInputController : PlayerInputListener
{
    private NetworkObject networkObject;
    private PlayerData playerData;
    private Animator animationController;
    private Vector2 currentMove, currentMoveGoal;
    private Vector2 currentLook, currentLookGoal;
    private bool isSprinting;

    //private Rig aimingRig;
    //private float rigBlendGoal;

    private FullBodyBipedIK fullBodyIK;

    [SerializeField]
    private float   lerpSpeed = 2.5f, 
                    walkSpeed = 0.2f,
                    sprintSpeed = 1f;

    [SerializeField]
    private Transform targetIK;

    private float blendGoal, blendValue;

    private void Awake()
    {
        TryGetComponent(out animationController);
        TryGetComponent(out fullBodyIK);

        networkObject = GetComponentInParent<NetworkObject>();
        playerData = GetComponentInParent<PlayerData>();

        if (!networkObject || !playerData)
            enabled = false;

        //aimingRig = GetComponentInChildren<Rig>();
    }

    protected override void Update()
    {
        base.Update();

        //if (!PlayerData.LocalPlayer)
        //    return;

        //if (!networkObject.IsLocalPlayer)
        //    return;

        currentMove = Vector2.Lerp(currentMove, currentMoveGoal * (isSprinting ? sprintSpeed : walkSpeed), Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, currentLookGoal, Time.deltaTime * lerpSpeed);

        //rigBlendGoal = animationController.GetBool("IsReload") ? 0f : 1f;

        blendGoal = animationController.GetBool("IsReload") ? 0f : 1f;
        blendValue = Mathf.Lerp(blendValue, blendGoal, Time.deltaTime * lerpSpeed);

        fullBodyIK.solver.bodyEffector.positionWeight = blendValue * 0.01f;
        fullBodyIK.solver.leftHandEffector.positionWeight = blendValue;
        fullBodyIK.solver.leftHandEffector.rotationWeight = blendValue;
        fullBodyIK.solver.leftArmChain.pull = blendValue;
        fullBodyIK.solver.leftArmChain.bendConstraint.weight = blendValue;
        fullBodyIK.solver.leftArmMapping.weight = blendValue;

        //aimingRig.weight = Mathf.Lerp(aimingRig.weight, rigBlendGoal, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        Vector3 posIK = Vector3.Lerp(playerData.TargetPosition.Value, playerData.RaycastPosition.Value,
            CustomUtilities.DefaultScalarDistance / Vector3.Distance(playerData.TargetPosition.Value, playerData.RaycastPosition.Value));
        targetIK.position = Vector3.Lerp(targetIK.position, posIK, Time.fixedDeltaTime * lerpSpeed);


        if (!networkObject.IsLocalPlayer)
            return;


        animationController.SetFloat("Horizontal", currentMove.x);
        animationController.SetFloat("Vertical", currentMove.y);

        animationController.SetFloat("InputMagnitude", currentMoveGoal.magnitude);

        animationController.SetFloat("WalkStartAngle", GetAngleFromVector(currentMoveGoal));
        animationController.SetFloat("WalkStopAngle", GetAngleFromVector(currentMove));

        animationController.SetFloat("HorAimAngle", currentLook.x);
        animationController.SetFloat("VerAimAngle", currentLook.y);
    }

    //private float CalculateYaw()
    //{
    //    return Vector3.Angle(PlayerData.LocalPlayer.Direction.Value, PlayerData.LocalPlayer.Turn.Value);
    //}

    protected override void OnMove(InputAction.CallbackContext context)
    {
        if (!networkObject.IsLocalPlayer)
            return;

        if (context.performed)
        {
            animationController.SetBool("IsStopRU", false);
            animationController.SetBool("IsStopLU", false);

            currentMoveGoal = context.ReadValue<Vector2>();
        }
        else if(context.canceled)
        {
            animationController.SetBool("IsStopRU", animationController.GetFloat("IsRU") > 0f);
            animationController.SetBool("IsStopLU", animationController.GetFloat("IsRU") <= 0f);

            currentMoveGoal = Vector2.zero;
        }
    }
    protected override void OnLook(InputAction.CallbackContext context)
    {
        if (!networkObject.IsLocalPlayer)
            return;

        if (context.performed)
        {
            currentLookGoal.x = context.ReadValue<Vector2>().x * 55f;
            currentLookGoal.y = context.ReadValue<Vector2>().y * -25f;
        }
        else if (context.canceled)
        {
            currentLookGoal.x = 0f;
        }
    }
    protected override void OnFire(InputAction.CallbackContext context)
    {
        if (!networkObject.IsLocalPlayer)
            return;

        if (context.performed)
        {
            animationController.SetBool("IsShoot", true);
        }
        else if (context.canceled)
        {
            animationController.SetBool("IsShoot", false);
        }
    }
    protected override void OnSprint(InputAction.CallbackContext context)
    {
        if (!playerData.IsLocalPlayer)
            return;

        if (context.performed)
        {
            isSprinting = true;
        }
        else if (context.canceled)
        {
            isSprinting = false;
        }
    }
    protected override void OnReload(InputAction.CallbackContext context)
    {
        if (!networkObject.IsLocalPlayer)
            return;

        if (context.performed)
        {
            animationController.SetBool("IsReload", true);
        }
        else if (context.canceled)
        {
            //animationController.SetBool("IsReload", false);
        }
    }
    protected override void OnLean(InputAction.CallbackContext context)
    {
        if (!networkObject.IsLocalPlayer)
            return;

        if (context.performed)
        {
            // set bool to transition to mirrored animation states ?
        }
    }

    public static float GetAngleFromVector(Vector2 vector)
    {
        return Mathf.Abs(Mathf.Atan2(vector.x, vector.y) * Mathf.Rad2Deg) * Mathf.Sign(vector.x);
    }
}
