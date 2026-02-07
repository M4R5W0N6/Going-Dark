using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Animations.Rigging;
using RootMotion.FinalIK;
using System.Collections;

[RequireComponent(typeof(Animator))]
public class AnimationInputController : MonoBehaviour, IEventListener
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
    private bool reloadPressedLastFrame;
    private Coroutine reloadCoroutine;

    private void Awake()
    {
        TryGetComponent(out animationController);
        TryGetComponent(out fullBodyIK);
    }

    private void Update()
    {
        var player = PlayerData.OwnerPlayer;
        if (player == null || animationController == null || fullBodyIK == null)
        {
            reloadPressedLastFrame = false;
            return;
        }
        blendGoal = player.CharacterIsReloading.Value ? 0f : 1f;
        blendValue = Mathf.Lerp(blendValue, blendGoal, Time.deltaTime * aimIKSpeed);

        fullBodyIK.solver.bodyEffector.positionWeight = blendValue * 0.01f;
        fullBodyIK.solver.leftHandEffector.positionWeight = blendValue;
        fullBodyIK.solver.leftHandEffector.rotationWeight = blendValue;
        fullBodyIK.solver.leftArmChain.pull = blendValue;
        fullBodyIK.solver.leftArmChain.bendConstraint.weight = blendValue;
        fullBodyIK.solver.leftArmMapping.weight = blendValue;

        // Local-only mode; always run for the local player

        Vector2 inputLook = player.InputLook.Value;
        inputLook.x *= 55f;
        inputLook.y *= -25f;

        currentMove = Vector2.Lerp(currentMove, player.InputMove.Value * (player.InputSprint.Value ? sprintSpeed : walkSpeed), Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, inputLook, Time.deltaTime * lerpSpeed);

        leanGoal = new Vector3(player.InputLean.Value - 0.5f, 0.5f, 0.5f);

        bool isReloadPressed = player.InputReload.Value;
        if (isReloadPressed && !reloadPressedLastFrame)
            TryStartReload(player);
        reloadPressedLastFrame = isReloadPressed;
    }

    private void FixedUpdate()
    {
        var player = PlayerData.OwnerPlayer;
        if (player == null || animationController == null || fullBodyIK == null || targetIKAim == null || targetIKLean == null)
            return;
        Vector3 posIK = Vector3.Lerp(player.CharacterTargetPosition.Value, player.CharacterRaycastPosition.Value,
            CustomUtilities.DefaultScalarDistance / Vector3.Distance(player.CharacterTargetPosition.Value, player.CharacterRaycastPosition.Value));
        if (targetIKAim)
            targetIKAim.position = Vector3.Lerp(targetIKAim.position, posIK, Time.fixedDeltaTime * aimIKSpeed);
        if (targetIKLean)
            targetIKLean.localPosition = Vector3.Lerp(targetIKLean.localPosition, leanGoal, Time.fixedDeltaTime * leanIKSpeed);

        // Local-only mode; always run for the local player

        animationController.SetFloat("Horizontal", currentMove.x);
        animationController.SetFloat("Vertical", currentMove.y);

        animationController.SetFloat("InputMagnitude", player.InputMove.Value.magnitude);

        animationController.SetFloat("WalkStartAngle", CustomUtilities.GetAngleFromVector(player.InputMove.Value));
        animationController.SetFloat("WalkStopAngle", CustomUtilities.GetAngleFromVector(currentMove));

        animationController.SetFloat("HorAimAngle", currentLook.x);
        animationController.SetFloat("VerAimAngle", currentLook.y);

        if (player.InputMove.Value.magnitude > 0f)
        {
            animationController.SetBool("IsStopRU", false);
            animationController.SetBool("IsStopLU", false);
        }
        else
        {
            animationController.SetBool("IsStopRU", animationController.GetFloat("IsRU") > 0f);
            animationController.SetBool("IsStopLU", animationController.GetFloat("IsRU") <= 0f);
        }

        animationController.SetBool("IsShoot", player.InputFire.Value);
        animationController.SetBool("IsReload", player.CharacterIsReloading.Value);
    }

    public void InputLeanCallback(float previousValue, float currentValue)
    {
        leanGoal = new Vector3(currentValue - 0.5f, 0.5f, 0.5f);
    }

    public void InputReloadCallback(bool previousValue, bool currentValue)
    {
        if (!currentValue)
            return;

        TryStartReload(PlayerData.OwnerPlayer);
    }

    private void TryStartReload(PlayerData player)
    {
        if (player == null || animationController == null)
            return;
        if (player.CharacterIsReloading.Value)
            return;

        player.CharacterIsReloading.Value = true;

        animationController.SetBool("IsReload", true);

        if (reloadCoroutine != null)
            StopCoroutine(reloadCoroutine);
        reloadCoroutine = StartCoroutine(WaitForReloadFinish(player));
    }

    private IEnumerator WaitForReloadFinish(PlayerData player)
    {
        yield return new WaitUntil(() => animationController == null || animationController.GetBool("IsReload") == false);

        if (player != null)
            player.CharacterIsReloading.Value = false;

        reloadCoroutine = null;
    }
}
