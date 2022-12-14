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
        Vector3 trackedPosition = transform.position;
        switch (typeToTrack)
        {
            case TrackedObjectType.NONE:
                break;
            case TrackedObjectType.PLAYER_ORIGIN:
                if (!PlayerData.OwnerPlayer) return;

                trackedPosition = PlayerData.OwnerPlayer.CharacterOriginPosition.Value;

                break;
            case TrackedObjectType.PLAYER_TARGET:
                if (!PlayerData.OwnerPlayer) return;

                trackedPosition = PlayerData.OwnerPlayer.CharacterTargetPosition.Value;

                break;
            case TrackedObjectType.PLAYER_RAYCAST:
                if (!PlayerData.OwnerPlayer) return;

                trackedPosition = PlayerData.OwnerPlayer.CharacterRaycastPosition.Value;

                break;
            default:
                break;
        }

        if (!CharacterInputController.OwnerCharacter)
            return;

        transform.rotation = Quaternion.Lerp(transform.rotation, CharacterInputController.OwnerCharacter.transform.rotation, Time.fixedDeltaTime * moveSpeed);
        transform.position = Vector3.Lerp(transform.position, trackedPosition + CharacterInputController.OwnerCharacter.transform.TransformVector(offset), Time.fixedDeltaTime * moveSpeed);
    }
}
