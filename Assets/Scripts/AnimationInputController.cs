using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using RootMotion.FinalIK;
using Unity.Netcode;
using System.Collections;

[RequireComponent(typeof(Animator))]
public class AnimationInputController : NetworkBehaviour, IEventListener
{
    private Animator animationController;
    private Vector2 currentMove;
    private Vector2 currentLook;

    private FullBodyBipedIK fullBodyIK;

    [SerializeField]
    private float   lerpSpeed = 2.5f, 
                    walkSpeed = 0.2f,
                    sprintSpeed = 1f,
                    aimIKSpeed = 25f,
                    leanIKSpeed = 2f;

    [SerializeField]
    private Transform targetIKAim, targetIKLean;
    private Vector3 leanGoal;
    private float blendGoal, blendValue;

    private void Awake()
    {
        TryGetComponent(out animationController);
        TryGetComponent(out fullBodyIK);
    }

    private void Update()
    {
        // Do IK locally
        blendGoal = PlayerData.OwnerPlayer.CharacterIsReloading.Value ? 0f : 1f;
        blendValue = Mathf.Lerp(blendValue, blendGoal, Time.deltaTime * aimIKSpeed);

        fullBodyIK.solver.bodyEffector.positionWeight = blendValue * 0.01f;
        fullBodyIK.solver.leftHandEffector.positionWeight = blendValue;
        fullBodyIK.solver.leftHandEffector.rotationWeight = blendValue;
        fullBodyIK.solver.leftArmChain.pull = blendValue;
        fullBodyIK.solver.leftArmChain.bendConstraint.weight = blendValue;
        fullBodyIK.solver.leftArmMapping.weight = blendValue;

        if (!PlayerData.OwnerPlayer.IsOwner)
            return;

        Vector2 inputLook = PlayerData.OwnerPlayer.InputLook.Value;
        inputLook.x *= 55f;
        inputLook.y *= -25f;

        currentMove = Vector2.Lerp(currentMove, PlayerData.OwnerPlayer.InputMove.Value * (PlayerData.OwnerPlayer.InputSprint.Value ? sprintSpeed : walkSpeed), Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, inputLook, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        // Do IK locally
        Vector3 posIK = Vector3.Lerp(PlayerData.OwnerPlayer.CharacterTargetPosition.Value, PlayerData.OwnerPlayer.CharacterRaycastPosition.Value,
            CustomUtilities.DefaultScalarDistance / Vector3.Distance(PlayerData.OwnerPlayer.CharacterTargetPosition.Value, PlayerData.OwnerPlayer.CharacterRaycastPosition.Value));
        targetIKAim.position = Vector3.Lerp(targetIKAim.position, posIK, Time.fixedDeltaTime * aimIKSpeed);
        targetIKLean.localPosition = Vector3.Lerp(targetIKLean.localPosition, leanGoal, Time.fixedDeltaTime * leanIKSpeed);

        if (!PlayerData.OwnerPlayer.IsOwner)
            return;

        animationController.SetFloat("Horizontal", currentMove.x);
        animationController.SetFloat("Vertical", currentMove.y);

        animationController.SetFloat("InputMagnitude", PlayerData.OwnerPlayer.InputMove.Value.magnitude);

        animationController.SetFloat("WalkStartAngle", CustomUtilities.GetAngleFromVector(PlayerData.OwnerPlayer.InputMove.Value));
        animationController.SetFloat("WalkStopAngle", CustomUtilities.GetAngleFromVector(currentMove));

        animationController.SetFloat("HorAimAngle", currentLook.x);
        animationController.SetFloat("VerAimAngle", currentLook.y);

        if (PlayerData.OwnerPlayer.InputMove.Value.magnitude > 0f)
        {
            animationController.SetBool("IsStopRU", false);
            animationController.SetBool("IsStopLU", false);
        }
        else
        {
            animationController.SetBool("IsStopRU", animationController.GetFloat("IsRU") > 0f);
            animationController.SetBool("IsStopLU", animationController.GetFloat("IsRU") <= 0f);
        }

        animationController.SetBool("IsShoot", PlayerData.OwnerPlayer.InputFire.Value);
        animationController.SetBool("IsReload", PlayerData.OwnerPlayer.CharacterIsReloading.Value);
    }

    public void InputLeanCallback(float previousValue, float currentValue)
    {
        leanGoal = new Vector3(currentValue - 0.5f, 0.5f, 0.5f);
    }

    public void InputReloadCallback(bool previousValue, bool currentValue)
    {
        if (!PlayerData.OwnerPlayer.IsOwner)
            return;

        if (PlayerData.OwnerPlayer.CharacterIsReloading.Value == true)
            return;

        PlayerData.OwnerPlayer.CharacterIsReloading.Value = true;

        animationController.SetBool("IsReload", true);

        StartCoroutine(WaitForReloadFinish());
    }
    private IEnumerator WaitForReloadFinish()
    {
        yield return new WaitUntil(() => animationController.GetBool("IsReload") == false);

        PlayerData.OwnerPlayer.CharacterIsReloading.Value = false;
    }
}
