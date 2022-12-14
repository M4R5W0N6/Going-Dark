using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Unity.Netcode;
using FOW;

public class FogOfWarEntity : NetworkBehaviour
{
    private FogOfWarRevealer revealer;

    [SerializeField]
    private bool isPeripheral;

    private void Awake()
    {
        TryGetComponent(out revealer);
    }

    public override void OnNetworkSpawn()
    {
        if (revealer)
            revealer.enabled = IsOwner || !isPeripheral;
    }

    public override void OnNetworkDespawn()
    {
        if (revealer)
            revealer.enabled = false;
    }
}
