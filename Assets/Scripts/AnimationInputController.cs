using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using RootMotion.FinalIK;

[RequireComponent(typeof(Animator))]
public class AnimationInputController : PlayerInputListener
{
    private Animator animationController;
    private Vector2 currentMove, currentMoveGoal;
    private Vector2 currentLook, currentLookGoal;

    //private Rig aimingRig;
    //private float rigBlendGoal;

    private FullBodyBipedIK fullBodyIK;

    [SerializeField]
    private float   lerpSpeed = 2.5f, 
                    walkSpeed = 0.2f,
                    sprintSpeed = 1f;

    private float blendGoal, blendValue;

    private void Awake()
    {
        TryGetComponent(out animationController);
        TryGetComponent(out fullBodyIK);
        //aimingRig = GetComponentInChildren<Rig>();
    }

    protected override void Update()
    {
        base.Update();

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
        animationController.SetFloat("Horizontal", currentMove.x);
        animationController.SetFloat("Vertical", currentMove.y);

        animationController.SetFloat("InputMagnitude", currentMoveGoal.magnitude);

        animationController.SetFloat("WalkStartAngle", GetAngleFromVector(currentMoveGoal));
        animationController.SetFloat("WalkStopAngle", GetAngleFromVector(currentMove));

        animationController.SetFloat("HorAimAngle", currentLook.x);
        animationController.SetFloat("VerAimAngle", currentLook.y);
    }

    private float CalculateYaw()
    {
        return Vector3.Angle(PlayerData.LocalPlayer.Direction, PlayerData.LocalPlayer.Rotation);
    }

    protected override void OnMove(InputAction.CallbackContext context)
    {
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
        if (context.performed)
        {
            animationController.SetBool("IsShoot", true);
        }
        else if (context.canceled)
        {
            animationController.SetBool("IsShoot", false);
        }
    }
    protected override void OnReload(InputAction.CallbackContext context)
    {
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
