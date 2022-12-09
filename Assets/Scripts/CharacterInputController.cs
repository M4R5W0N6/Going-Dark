using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody), typeof(PlayerData))]
public class CharacterInputController : NetworkBehaviour
{
    [SerializeField]
    private float   lerpSpeed = 10f, 
                    moveSpeed = 0.025f, 
                    turnSpeed = 7.5f, 
                    pitchSpeed = 2.5f, 
                    sprintSpeed = 2f;

    [SerializeField]
    private float pitchMin = -30f, pitchMax = 30f;
    [SerializeField]
    private Transform pitchOrigin;

    private Vector2 currentMove;
    private Vector2 currentLook;
    private float currentPitch;

    private Rigidbody rigidbody;
    private PlayerData playerData;

    private void Awake()
    {
        TryGetComponent(out rigidbody);
        TryGetComponent(out playerData);
    }

    public override void OnNetworkSpawn()
    {
        NetworkEventManager.PlayerSpawnServerRpc(OwnerClientId);
    }

    private void Update()
    {
        if (!IsLocalPlayer)
            return;

        currentMove = Vector2.Lerp(currentMove, playerData.InputMove.Value, Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, playerData.InputLook.Value, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        if (!IsLocalPlayer)
        {
            if (LayerMask.LayerToName(gameObject.layer) == "Player")
                CustomUtilities.SetLayerRecursively(gameObject, "Default");

            return;
        }
        else
        {
            if (LayerMask.LayerToName(gameObject.layer) != "Player")
                CustomUtilities.SetLayerRecursively(gameObject, "Player");
        }

        rigidbody.AddRelativeForce(playerData.CharacterMove.Value.x, 0f, playerData.CharacterMove.Value.y);

        transform.Rotate(Vector3.up, playerData.CharacterTurn.Value.y * turnSpeed, Space.Self);

        pitchOrigin.localRotation = Quaternion.Euler(playerData.CharacterTurn.Value.x, 0f, 0f);

        playerData.CharacterMove.Value = currentMove * moveSpeed * (playerData.InputSprint.Value ? sprintSpeed : 1f);

        currentPitch += currentLook.y * pitchSpeed;
        currentPitch = Mathf.Clamp(currentPitch, pitchMin, pitchMax);

        playerData.CharacterTurn.Value = new Vector2(currentPitch, currentLook.x);

        // setup raycast
        int layerMask = 1 << LayerMask.NameToLayer("Player");
        layerMask = ~layerMask;

        playerData.CharacterTargetPosition.Value = (pitchOrigin.forward * CustomUtilities.DefaultScalarDistance) + transform.position;
        Vector3 forward = Vector3.Normalize(playerData.CharacterTargetPosition.Value - playerData.CameraPosition.Value);

        // check if camera has line of sight to reticle
        RaycastHit screenHit;
        if (!Physics.Raycast(playerData.CameraPosition.Value, forward, out screenHit, Mathf.Infinity, layerMask))
        {
            screenHit.point = playerData.CameraPosition.Value + forward * CustomUtilities.DefaultScalarDistance;
        }

        // check if origin has line of sight
        forward = Vector3.Normalize(screenHit.point - pitchOrigin.position);

        RaycastHit muzzleHit;
        if (!Physics.Raycast(pitchOrigin.position, forward, out muzzleHit, Mathf.Infinity, layerMask))
        {
            muzzleHit.point = screenHit.point;
        }

        playerData.CharacterOriginPosition.Value = pitchOrigin.position;
        playerData.CharacterRaycastPosition.Value = muzzleHit.point;
        playerData.CharacterIsOnTarget.Value = Vector3.Distance(screenHit.point, muzzleHit.point) < CustomUtilities.DefaultRaycastThreshold; // replace with FoW sample point (v1.4) ?
    }
}
