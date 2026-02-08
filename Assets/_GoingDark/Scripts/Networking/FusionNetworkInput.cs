using Fusion;
using UnityEngine;

public struct FusionNetworkInput : INetworkInput
{
    public const int FireButton = 0;
    public const int ReloadButton = 1;
    public const int AimButton = 2;
    public const int SprintButton = 3;

    public Vector2 Move;
    public Vector2 Look;
    public float Lean;
    public NetworkButtons Buttons;
}
