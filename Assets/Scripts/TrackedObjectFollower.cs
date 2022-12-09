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

    private PlayerData currentPlayer;


    private void Awake()
    {
        NetworkEventManager.EventPlayerSpawn += OnPlayerSpawn;
    }

    private void OnPlayerSpawn(ulong id)
    {
        PlayerData newPlayer = PlayerData.GetPlayer(id);

        if (newPlayer.IsLocalPlayer)
            currentPlayer = newPlayer;
    }

    private void FixedUpdate()
    {
        Vector3 trackedPosition = transform.position;
        switch (typeToTrack)
        {
            case TrackedObjectType.NONE:
                break;
            case TrackedObjectType.PLAYER_ORIGIN:
                if (!currentPlayer) return;

                trackedPosition = currentPlayer.CharacterOriginPosition.Value;

                break;
            case TrackedObjectType.PLAYER_TARGET:
                if (!currentPlayer) return;

                trackedPosition = currentPlayer.CharacterTargetPosition.Value;

                break;
            case TrackedObjectType.PLAYER_RAYCAST:
                if (!currentPlayer) return;

                trackedPosition = currentPlayer.CharacterRaycastPosition.Value;

                break;
            default:
                break;
        }

        transform.rotation = Quaternion.Lerp(transform.rotation, currentPlayer.transform.rotation, Time.fixedDeltaTime * moveSpeed);
        transform.position = Vector3.Lerp(transform.position, trackedPosition + currentPlayer.transform.TransformVector(offset), Time.fixedDeltaTime * moveSpeed);
    }
}
