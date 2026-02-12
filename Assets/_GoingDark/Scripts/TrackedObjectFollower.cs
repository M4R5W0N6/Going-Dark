using UnityEngine;

public class TrackedObjectFollower : MonoBehaviour
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
    private Vector3 offset;

    private void FixedUpdate()
    {
        PlayerData player = PlayerData.OwnerPlayer;
        if (player == null)
            return;

        Vector3 trackedPosition = transform.position;
        switch (typeToTrack)
        {
            case TrackedObjectType.NONE:
                break;
            case TrackedObjectType.PLAYER_ORIGIN:
                trackedPosition = player.CharacterOriginPosition.Value;

                break;
            case TrackedObjectType.PLAYER_TARGET:
                trackedPosition = player.CharacterTargetPosition.Value;

                break;
            case TrackedObjectType.PLAYER_RAYCAST:
                trackedPosition = player.CharacterRaycastPosition.Value;

                break;
            default:
                break;
        }

        Transform ownerTransform = player.transform;
        transform.rotation = Quaternion.Lerp(transform.rotation, ownerTransform.rotation, Time.fixedDeltaTime * moveSpeed);
        transform.position = Vector3.Lerp(transform.position, trackedPosition + ownerTransform.TransformVector(offset), Time.fixedDeltaTime * moveSpeed);
    }
}
