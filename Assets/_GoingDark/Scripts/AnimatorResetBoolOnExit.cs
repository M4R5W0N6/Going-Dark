using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class AnimatorResetBoolOnExit : StateMachineBehaviour
{
    [SerializeField]
    private string booleanVariableName;

    override public void OnStateExit(Animator animator, AnimatorStateInfo stateInfo, int layerIndex)
    {
        animator.SetBool(booleanVariableName, false);
    }
}
