using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FOW;

[RequireComponent(typeof(Volume))]
public class FogOfWarVolume : MonoBehaviour, IEventListener
{
    private Volume volume;

    private void Awake()
    {
        TryGetComponent(out volume);

        volume.weight = 0f;
    }

    public void RoundStartCallback()
    {
        volume.weight = 1f;
    }

    public void RoundEndCallback()
    {
        volume.weight = 0f;
    }
}
