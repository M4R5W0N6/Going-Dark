using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class MidpointFollower : MonoBehaviour
{
    [SerializeField]
    private Transform origin, target;
    [SerializeField]
    private float lerpSpeed = 1f, radius = 100f;

    private void FixedUpdate()
    {
        Vector3 pos = Vector3.Lerp(origin.position, target.position, radius / Vector3.Distance(origin.position, target.position));
        transform.position = Vector3.Lerp(transform.position, pos, Time.fixedDeltaTime * lerpSpeed);    
    }
}
