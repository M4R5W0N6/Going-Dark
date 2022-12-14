using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FOW;

[RequireComponent(typeof(Volume))]
public class FogOfWarVolume : NetworkEventListener_MonoBehaviour
{
    private Volume volume;

    private void Awake()
    {
        TryGetComponent(out volume);

        volume.weight = 0f;
    }

    protected override void RoundStartCallback()
    {
        volume.weight = 1f;
    }

    protected override void RoundEndCallback()
    {
        volume.weight = 0f;
    }
}
