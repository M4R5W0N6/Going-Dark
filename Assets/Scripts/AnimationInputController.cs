using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using RootMotion.FinalIK;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(Animator))]
public class AnimationInputController : NetworkEventListener_NetworkBehaviour
{
    private Animator animationController;
    private Vector2 currentMove;
    private Vector2 currentLook;

    private FullBodyBipedIK fullBodyIK;

    [SerializeField]
    private float   lerpSpeed = 2.5f, 
                    walkSpeed = 0.2f,
                    sprintSpeed = 1f,
                    ikSpeed = 25f;

    [SerializeField]
    private Transform targetIK;

    private float blendGoal, blendValue;

    private void Awake()
    {
        TryGetComponent(out animationController);
        TryGetComponent(out fullBodyIK);
    }

    private void Update()
    {
        // Do IK locally
        blendGoal = OwnerPlayer.CharacterIsReloading.Value ? 0f : 1f;
        blendValue = Mathf.Lerp(blendValue, blendGoal, Time.deltaTime * ikSpeed);

        fullBodyIK.solver.bodyEffector.positionWeight = blendValue * 0.01f;
        fullBodyIK.solver.leftHandEffector.positionWeight = blendValue;
        fullBodyIK.solver.leftHandEffector.rotationWeight = blendValue;
        fullBodyIK.solver.leftArmChain.pull = blendValue;
        fullBodyIK.solver.leftArmChain.bendConstraint.weight = blendValue;
        fullBodyIK.solver.leftArmMapping.weight = blendValue;

        if (!OwnerPlayer.IsOwner)
            return;

        Vector2 inputLook = OwnerPlayer.InputLook.Value;
        inputLook.x *= 55f;
        inputLook.y *= -25f;

        currentMove = Vector2.Lerp(currentMove, OwnerPlayer.InputMove.Value * (OwnerPlayer.InputSprint.Value ? sprintSpeed : walkSpeed), Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, inputLook, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        // Do IK locally
        Vector3 posIK = Vector3.Lerp(OwnerPlayer.CharacterTargetPosition.Value, OwnerPlayer.CharacterRaycastPosition.Value,
            CustomUtilities.DefaultScalarDistance / Vector3.Distance(OwnerPlayer.CharacterTargetPosition.Value, OwnerPlayer.CharacterRaycastPosition.Value));
        targetIK.position = Vector3.Lerp(targetIK.position, posIK, Time.fixedDeltaTime * ikSpeed);

        if (!OwnerPlayer.IsOwner)
            return;

        animationController.SetFloat("Horizontal", currentMove.x);
        animationController.SetFloat("Vertical", currentMove.y);

        animationController.SetFloat("InputMagnitude", OwnerPlayer.InputMove.Value.magnitude);

        animationController.SetFloat("WalkStartAngle", CustomUtilities.GetAngleFromVector(OwnerPlayer.InputMove.Value));
        animationController.SetFloat("WalkStopAngle", CustomUtilities.GetAngleFromVector(currentMove));

        animationController.SetFloat("HorAimAngle", currentLook.x);
        animationController.SetFloat("VerAimAngle", currentLook.y);

        if (OwnerPlayer.InputMove.Value.magnitude > 0f)
        {
            animationController.SetBool("IsStopRU", false);
            animationController.SetBool("IsStopLU", false);
        }
        else
        {
            animationController.SetBool("IsStopRU", animationController.GetFloat("IsRU") > 0f);
            animationController.SetBool("IsStopLU", animationController.GetFloat("IsRU") <= 0f);
        }

        animationController.SetBool("IsShoot", OwnerPlayer.InputFire.Value);
        animationController.SetBool("IsReload", OwnerPlayer.CharacterIsReloading.Value);
    }

    protected override void InputReloadCallback(bool previousValue, bool currentValue)
    {
        if (!OwnerPlayer.IsOwner)
            return;

        if (OwnerPlayer.CharacterIsReloading.Value == true)
            return;

        OwnerPlayer.CharacterIsReloading.Value = true;

        animationController.SetBool("IsReload", true);

        StartCoroutine(WaitForReloadFinish());
    }
    private IEnumerator WaitForReloadFinish()
    {
        yield return new WaitUntil(() => animationController.GetBool("IsReload") == false);

        OwnerPlayer.CharacterIsReloading.Value = false;
    }
}
