using System.Collections.Generic;
using Fusion;
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
            PlayerData localPlayer = PlayerData.LocalPlayer;
            if (IsValidOwnerCharacter(ownerCharacter) && ownerCharacter.playerData == localPlayer)
                return ownerCharacter;

            ownerCharacter = null;

            List<CharacterInputController> characters = Characters;
            if (localPlayer != null)
            {
                for (int i = 0; i < characters.Count; i++)
                {
                    if (!IsValidOwnerCharacter(characters[i]))
                        continue;

                    if (characters[i].playerData == localPlayer)
                    {
                        ownerCharacter = characters[i];
                        return ownerCharacter;
                    }
                }
            }

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

            ownerCharacter = GetSingleActiveFallback(characters);

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

        return GetSingleActiveFallback(characters);
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
    private NetworkObject networkObject;

    private void Awake()
    {
        TryGetComponent(out characterRigidbody);
        TryGetComponent(out networkObject);

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
        if (IsDrivenByFusionRunner())
            return;

        SimulateStateAuthorityStep();
    }

    public void SimulateStateAuthorityStep()
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

        Vector3 origin = pitchOrigin ? pitchOrigin.position : transform.position;
        Vector3 cameraPosition = player.CameraPosition.Value;
        Camera camera = player.IsLocalPlayer ? CustomUtilities.GetBestCamera(transform) : null;
        if (camera != null)
        {
            cameraPosition = camera.transform.position;
            player.CameraPosition.Value = cameraPosition;
        }
        else if (cameraPosition == Vector3.zero)
        {
            Vector3 fallbackDirection = pitchOrigin ? pitchOrigin.forward : transform.forward;
            cameraPosition = origin + fallbackDirection;
        }

        if (pitchOrigin)
            player.CharacterTargetPosition.Value = origin + (pitchOrigin.forward * CustomUtilities.DefaultScalarDistance);
        else
            player.CharacterTargetPosition.Value = origin + (transform.forward * CustomUtilities.DefaultScalarDistance);

        Vector3 forward = Vector3.Normalize(player.CharacterTargetPosition.Value - cameraPosition);

        // check if camera has line of sight to reticle
        RaycastHit screenHit;
        if (!TryRaycast(cameraPosition, forward, out screenHit, Mathf.Infinity, layerMask))
            screenHit.point = cameraPosition + forward * CustomUtilities.DefaultScalarDistance;

        // check if origin has line of sight
        Vector3 muzzlePoint = screenHit.point;
        forward = Vector3.Normalize(screenHit.point - origin);
        RaycastHit tmpHit;
        if (TryRaycast(origin, forward, out tmpHit, Mathf.Infinity, layerMask))
            muzzlePoint = tmpHit.point;

        player.CharacterOriginPosition.Value = origin;
        player.CharacterRaycastPosition.Value = muzzlePoint;
        player.CharacterIsOnTarget.Value = Vector3.Distance(screenHit.point, muzzlePoint) <= CustomUtilities.DefaultRaycastThreshold;
    }

    private static bool IsValidOwnerCharacter(CharacterInputController character)
    {
        if (character == null || !character.isActiveAndEnabled || !character.gameObject.activeInHierarchy)
            return false;

        if (character.playerData == null)
            character.TryGetComponent(out character.playerData);

        return character.playerData != null;
    }

    private bool TryRaycast(Vector3 origin, Vector3 direction, out RaycastHit hit, float maxDistance, int layerMask)
    {
        if (TryGetRunnerPhysicsScene(out PhysicsScene physicsScene))
            return physicsScene.Raycast(origin, direction, out hit, maxDistance, layerMask, QueryTriggerInteraction.Ignore);

        return Physics.Raycast(origin, direction, out hit, maxDistance, layerMask, QueryTriggerInteraction.Ignore);
    }

    private bool TryGetRunnerPhysicsScene(out PhysicsScene physicsScene)
    {
        physicsScene = default;

        if (networkObject == null)
            TryGetComponent(out networkObject);

        if (networkObject == null || networkObject.Runner == null)
            return false;

        physicsScene = networkObject.Runner.GetPhysicsScene();
        return physicsScene.IsValid();
    }

    private bool IsDrivenByFusionRunner()
    {
        if (networkObject == null)
            TryGetComponent(out networkObject);

        return networkObject != null && networkObject.Runner != null && networkObject.Runner.IsRunning;
    }

    private static CharacterInputController GetSingleActiveFallback(List<CharacterInputController> currentCharacters)
    {
        CharacterInputController fallback = null;
        int activeCount = 0;

        for (int i = 0; i < currentCharacters.Count; i++)
        {
            CharacterInputController character = currentCharacters[i];
            if (!IsValidOwnerCharacter(character))
                continue;

            fallback = character;
            activeCount++;
            if (activeCount > 1)
                return null;
        }

        return fallback;
    }
}
