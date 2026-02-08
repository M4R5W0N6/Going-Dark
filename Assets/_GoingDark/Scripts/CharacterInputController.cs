using System.Collections.Generic;
using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class CharacterInputController : MonoBehaviour
{
    private static List<CharacterInputController> characters;
    public static List<CharacterInputController> Characters
    {
        get
        {
            characters = new List<CharacterInputController>(FindObjectsByType<CharacterInputController>(FindObjectsSortMode.None));

            return characters;
        }
    }

    private static CharacterInputController ownerCharacter;
    public static CharacterInputController OwnerCharacter
    {
        get
        {
            if (IsValidOwnerCharacter(ownerCharacter))
                return ownerCharacter;

            ownerCharacter = null;

            List<CharacterInputController> characters = Characters;

            for (int i = 0; i < characters.Count; i++)
            {
                if (!IsValidOwnerCharacter(characters[i]))
                    continue;

                if (characters[i].playerData.IsLocalPlayer)
                {
                    ownerCharacter = characters[i];
                    return ownerCharacter;
                }
            }

            ownerCharacter = characters.Count > 0 ? characters[0] : null;

            return ownerCharacter;
        }
    }
    public static CharacterInputController GetCharacter(ulong ownerId)
    {
        List<CharacterInputController> characters = Characters;
        for (int i = 0; i < characters.Count; i++)
        {
            if (characters[i] != null && characters[i].playerData != null && characters[i].playerData.NetworkOwnerId == ownerId)
                return characters[i];
        }

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

    private Rigidbody characterRigidbody;
    private PlayerData playerData;

    private void Awake()
    {
        TryGetComponent(out characterRigidbody);

        if (!TryGetComponent(out playerData))
            playerData = gameObject.AddComponent<PlayerData>();
    }

    private void Start() { }
    private void OnDisable()
    {
        if (ownerCharacter == this)
            ownerCharacter = null;
    }

    private void OnDestroy()
    {
        if (ownerCharacter == this)
            ownerCharacter = null;
    }

    private void Update()
    {
        var player = playerData;
        if (player == null)
            return;

        currentMove = Vector2.Lerp(currentMove, player.InputMove.Value, Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, player.InputLook.Value, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        var player = playerData;
        if (player == null)
            return;

        if (LayerMask.LayerToName(gameObject.layer) != "Player")
            CustomUtilities.SetLayerRecursively(gameObject, "Player");

        if (characterRigidbody)
            characterRigidbody.AddRelativeForce(player.CharacterMove.Value.x, 0f, player.CharacterMove.Value.y);

        transform.Rotate(Vector3.up, player.CharacterTurn.Value.y * turnSpeed, Space.Self);

        if (pitchOrigin)
            pitchOrigin.localRotation = Quaternion.Euler(player.CharacterTurn.Value.x, 0f, 0f);

        player.CharacterMove.Value = currentMove * moveSpeed * (player.InputSprint.Value ? sprintSpeed : 1f);

        currentPitch += currentLook.y * pitchSpeed * GameSettings.Sensitivity;
        currentPitch = Mathf.Clamp(currentPitch, pitchMin, pitchMax);

        player.CharacterTurn.Value = new Vector2(currentPitch, currentLook.x * GameSettings.Sensitivity);

        // setup raycast
        int layerMask = 1 << LayerMask.NameToLayer("Player");
        layerMask = ~layerMask;

        Vector3 cameraPosition = player.CameraPosition.Value;
        if (Camera.main != null)
            cameraPosition = Camera.main.transform.position;

        if (pitchOrigin)
            player.CharacterTargetPosition.Value = (pitchOrigin.forward * CustomUtilities.DefaultScalarDistance) + transform.position;
        else
            player.CharacterTargetPosition.Value = transform.position + (transform.forward * CustomUtilities.DefaultScalarDistance);

        Vector3 forward = Vector3.Normalize(player.CharacterTargetPosition.Value - cameraPosition);

        // check if camera has line of sight to reticle
        RaycastHit screenHit;
        if (!Physics.Raycast(cameraPosition, forward, out screenHit, Mathf.Infinity, layerMask))
            screenHit.point = cameraPosition + forward * CustomUtilities.DefaultScalarDistance;

        // check if origin has line of sight
        Vector3 origin = pitchOrigin ? pitchOrigin.position : transform.position;
        Vector3 muzzlePoint = screenHit.point;
        forward = Vector3.Normalize(screenHit.point - origin);
        RaycastHit tmpHit;
        if (Physics.Raycast(origin, forward, out tmpHit, Mathf.Infinity, layerMask))
            muzzlePoint = tmpHit.point;

        player.CharacterOriginPosition.Value = origin;
        player.CharacterRaycastPosition.Value = muzzlePoint;
        player.CharacterIsOnTarget.Value = Vector3.Distance(screenHit.point, muzzlePoint) <= CustomUtilities.DefaultRaycastThreshold;
    }

    private static bool IsValidOwnerCharacter(CharacterInputController character)
    {
        if (character == null || !character.isActiveAndEnabled)
            return false;

        if (character.playerData == null)
            character.TryGetComponent(out character.playerData);

        return character.playerData != null;
    }
}
