using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using FOW;

[RequireComponent(typeof(Volume))]
public class FogOfWarVolume : MonoBehaviour, IEventListener
{
    private Volume volume;
    private float targetWeight;

    private void Awake()
    {
        TryGetComponent(out volume);

        SetFogEnabled(GameManager.IsInRound);
    }

    private void Update()
    {
        // Keep FoW rendering in sync even when round events are not broadcast.
        SetFogEnabled(GameManager.IsInRound);
    }

    public void RoundStartCallback()
    {
        SetFogEnabled(true);
    }

    public void RoundEndCallback()
    {
        SetFogEnabled(false);
    }

    private void SetFogEnabled(bool enabled)
    {
        if (volume == null)
            return;

        targetWeight = enabled ? 1f : 0f;
        if (!Mathf.Approximately(volume.weight, targetWeight))
            volume.weight = targetWeight;
    }
}
