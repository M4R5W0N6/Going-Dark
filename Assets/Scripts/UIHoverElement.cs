using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(RawImage))]
public class UIHoverElement : MonoBehaviour
{
    public enum TrackedObjectType
    {
        NONE,
        PLAYER_ORIGIN,
        PLAYER_TARGET,
        PLAYER_RAYCAST
    };
    [SerializeField]
    private TrackedObjectType typeToTrack = TrackedObjectType.NONE;

    [SerializeField]
    private float moveSpeed = 2f;

    [SerializeField]
    private Color activeColor, inactiveColor;

    [SerializeField]
    private float colorSpeed = 2f;

    private RawImage imageComponent;
    private bool isActive;

    private void Awake()
    {
        TryGetComponent(out imageComponent);
    }

    private void FixedUpdate()
    {
        Vector3 trackedPosition = transform.position;
        switch (typeToTrack)
        {
            case TrackedObjectType.NONE:
                break;
            case TrackedObjectType.PLAYER_ORIGIN:
                trackedPosition = PlayerData.LocalPlayer.OriginPosition;
                break;
            case TrackedObjectType.PLAYER_TARGET:
                isActive = PlayerData.LocalPlayer.IsOnTarget;
                trackedPosition = PlayerData.LocalPlayer.TargetPosition;
                break;
            case TrackedObjectType.PLAYER_RAYCAST:
                isActive = !PlayerData.LocalPlayer.IsOnTarget;
                trackedPosition = PlayerData.LocalPlayer.RaycastPosition;
                break;
            default:
                break;
        }
        trackedPosition = CustomUtilities.GetScreenPosition(trackedPosition);

        transform.position = Vector3.Lerp(transform.position, trackedPosition, Time.fixedDeltaTime * moveSpeed);

        imageComponent.color = Color.Lerp(imageComponent.color, isActive ? activeColor : inactiveColor, Time.fixedDeltaTime * colorSpeed);
    }
}
