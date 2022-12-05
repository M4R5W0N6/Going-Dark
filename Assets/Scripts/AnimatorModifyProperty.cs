using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimatorModifyProperty : StateMachineBehaviour
{
    [SerializeField]
    private string  floatVariableName,
                    floatVariableTargetName;
    [SerializeField]
    private float   maxValue = 1f,
                    minSpeed = 0.1f,
                    maxSpeed = 1f;

    // OnStateEnter is called when a transition starts and the state machine starts to evaluate this state
    //override public void OnStateEnter(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    //{
    //    
    //}

    // OnStateUpdate is called on each Update frame between OnStateEnter and OnStateExit callbacks
    //override public void OnStateUpdate(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    //{
    //    
    //}

    // OnStateExit is called when a transition ends and the state machine finishes evaluating this state
    //override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    //{
    //    
    //}

    override public void OnStateMove(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetFloat(floatVariableTargetName, Mathf.Clamp(Mathf.Abs(animator.GetFloat(floatVariableName) / maxValue), minSpeed, maxSpeed));
    }

    // OnStateIK is called right after Animator.OnAnimatorIK()
    //override public void OnStateIK(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    //{
    //    // Implement code that sets up animation IK (inverse kinematics)
    //}
}
