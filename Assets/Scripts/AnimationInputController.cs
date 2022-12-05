using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;

[RequireComponent(typeof(Animator))]
public class AnimationInputController : PlayerInputListener
{
    private Animator animationController;
    private Vector2 currentMove, currentMoveGoal;
    private Vector2 currentLook, currentLookGoal;

    private Rig aimingRig;
    private float rigBlendGoal;

    [SerializeField]
    private float   lerpSpeed = 2.5f, 
                    walkSpeed = 0.2f,
                    sprintSpeed = 1f;

    private void Awake()
    {
        TryGetComponent(out animationController);

        aimingRig = GetComponentInChildren<Rig>();
    }

    protected override void Update()
    {
        base.Update();

        currentMove = Vector2.Lerp(currentMove, currentMoveGoal * (isSprinting ? sprintSpeed : walkSpeed), Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, currentLookGoal, Time.deltaTime * lerpSpeed);

        rigBlendGoal = animationController.GetBool("IsReload") ? 0f : 1f;
        aimingRig.weight = Mathf.Lerp(aimingRig.weight, rigBlendGoal, Time.deltaTime * lerpSpeed);
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
