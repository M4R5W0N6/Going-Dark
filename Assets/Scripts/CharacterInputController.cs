using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody))]
public class CharacterInputController : NetworkBehaviour, IEventListener
{
    private static List<CharacterInputController> characters;
    public static List<CharacterInputController> Characters
    {
        get
        {
            characters = new List<CharacterInputController>(FindObjectsOfType<CharacterInputController>());

            return characters;
        }
    }

    private static CharacterInputController ownerCharacter;
    public static CharacterInputController OwnerCharacter
    {
        get
        {
            if (ownerCharacter)
                if (ownerCharacter.IsOwner)
                    return ownerCharacter;

            List<CharacterInputController> characters = Characters;

            characters = characters.Where(x => x.IsOwner).ToList();

            ownerCharacter = characters.Count > 0 ? characters[0] : null;

            return ownerCharacter;
        }
    }
    public static CharacterInputController GetCharacter(ulong ownerId)
    {
        List<CharacterInputController> characters = Characters;

        characters = characters.Where(x => x.GetComponent<NetworkBehaviour>().OwnerClientId == ownerId).ToList();

        return characters.Count > 0 ? characters[0] : null;
    }

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

    private void Awake()
    {
        TryGetComponent(out rigidbody);
    }

    public override void OnNetworkSpawn()
    {
        RoundManager.Instance.OnCharacterSpawnServerRpc(OwnerClientId);
    }
    public override void OnNetworkDespawn()
    {
        RoundManager.Instance.OnCharacterDespawnServerRpc(OwnerClientId, OwnerClientId);
    }

    private void Update()
    {
        if (!IsOwner)
            return;

        currentMove = Vector2.Lerp(currentMove, PlayerData.OwnerPlayer.InputMove.Value, Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, PlayerData.OwnerPlayer.InputLook.Value, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        if (!IsOwner)
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

        rigidbody.AddRelativeForce(PlayerData.OwnerPlayer.CharacterMove.Value.x, 0f, PlayerData.OwnerPlayer.CharacterMove.Value.y);

        transform.Rotate(Vector3.up, PlayerData.OwnerPlayer.CharacterTurn.Value.y * turnSpeed, Space.Self);

        pitchOrigin.localRotation = Quaternion.Euler(PlayerData.OwnerPlayer.CharacterTurn.Value.x, 0f, 0f);

        PlayerData.LocalPlayer.CharacterMove.Value = currentMove * moveSpeed * (PlayerData.OwnerPlayer.InputSprint.Value ? sprintSpeed : 1f);

        currentPitch += currentLook.y * pitchSpeed * GameSettings.Sensitivity;
        currentPitch = Mathf.Clamp(currentPitch, pitchMin, pitchMax);

        PlayerData.LocalPlayer.CharacterTurn.Value = new Vector2(currentPitch, currentLook.x * GameSettings.Sensitivity);

        // setup raycast
        int layerMask = 1 << LayerMask.NameToLayer("Player");
        layerMask = ~layerMask;

        PlayerData.OwnerPlayer.CharacterTargetPosition.Value = (pitchOrigin.forward * CustomUtilities.DefaultScalarDistance) + transform.position;
        Vector3 forward = Vector3.Normalize(PlayerData.OwnerPlayer.CharacterTargetPosition.Value - PlayerData.OwnerPlayer.CameraPosition.Value);

        // check if camera has line of sight to reticle
        RaycastHit screenHit;
        if (!Physics.Raycast(PlayerData.OwnerPlayer.CameraPosition.Value, forward, out screenHit, Mathf.Infinity, layerMask))
        {
            screenHit.point = PlayerData.OwnerPlayer.CameraPosition.Value + forward * CustomUtilities.DefaultScalarDistance;
        }

        // check if origin has line of sight
        forward = Vector3.Normalize(screenHit.point - pitchOrigin.position);

        RaycastHit muzzleHit;
        if (!Physics.Raycast(pitchOrigin.position, forward, out muzzleHit, Mathf.Infinity, layerMask))
        {
            muzzleHit.point = screenHit.point;
        }

        PlayerData.OwnerPlayer.CharacterOriginPosition.Value = pitchOrigin.position;
        PlayerData.OwnerPlayer.CharacterRaycastPosition.Value = muzzleHit.point;
        PlayerData.OwnerPlayer.CharacterIsOnTarget.Value = Vector3.Distance(screenHit.point, muzzleHit.point) < CustomUtilities.DefaultRaycastThreshold; // replace with FoW sample point (v1.4) ?
    }
}
