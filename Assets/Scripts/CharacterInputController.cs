using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody))]
public class CharacterInputController : MonoBehaviour, IEventListener
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
            if (ownerCharacter)
                return ownerCharacter;

            List<CharacterInputController> characters = Characters;

            // Single-player: first instance is the owner

            ownerCharacter = characters.Count > 0 ? characters[0] : null;

            return ownerCharacter;
        }
    }
    public static CharacterInputController GetCharacter(ulong ownerId)
    {
        List<CharacterInputController> characters = Characters;
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

    private void Awake()
    {
        TryGetComponent(out characterRigidbody);
        // Ensure a PlayerData exists on the player
        if (!TryGetComponent<PlayerData>(out _))
        {
            gameObject.AddComponent<PlayerData>();
        }
    }

    private void Start() { }
    private void OnDestroy() { }

    private void Update()
    {
        var player = PlayerData.OwnerPlayer;
        if (player == null)
            return;

        currentMove = Vector2.Lerp(currentMove, player.InputMove.Value, Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, player.InputLook.Value, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        var player = PlayerData.OwnerPlayer;
        if (player == null)
            return;

        if (LayerMask.LayerToName(gameObject.layer) != "Player")
            CustomUtilities.SetLayerRecursively(gameObject, "Player");

        if (characterRigidbody)
            characterRigidbody.AddRelativeForce(player.CharacterMove.Value.x, 0f, player.CharacterMove.Value.y);

        transform.Rotate(Vector3.up, player.CharacterTurn.Value.y * turnSpeed, Space.Self);

        if (pitchOrigin)
            pitchOrigin.localRotation = Quaternion.Euler(player.CharacterTurn.Value.x, 0f, 0f);

        PlayerData.LocalPlayer.CharacterMove.Value = currentMove * moveSpeed * (player.InputSprint.Value ? sprintSpeed : 1f);

        currentPitch += currentLook.y * pitchSpeed * GameSettings.Sensitivity;
        currentPitch = Mathf.Clamp(currentPitch, pitchMin, pitchMax);

        PlayerData.LocalPlayer.CharacterTurn.Value = new Vector2(currentPitch, currentLook.x * GameSettings.Sensitivity);

        // setup raycast
        int layerMask = 1 << LayerMask.NameToLayer("Player");
        layerMask = ~layerMask;

        if (pitchOrigin)
            player.CharacterTargetPosition.Value = (pitchOrigin.forward * CustomUtilities.DefaultScalarDistance) + transform.position;
        Vector3 forward = Vector3.Normalize(player.CharacterTargetPosition.Value - player.CameraPosition.Value);

        // check if camera has line of sight to reticle
        RaycastHit screenHit;
        if (!Physics.Raycast(player.CameraPosition.Value, forward, out screenHit, Mathf.Infinity, layerMask))
        {
            screenHit.point = player.CameraPosition.Value + forward * CustomUtilities.DefaultScalarDistance;
        }

        // check if origin has line of sight
        Vector3 muzzlePoint = screenHit.point;
        if (pitchOrigin)
        {
            forward = Vector3.Normalize(screenHit.point - pitchOrigin.position);
            RaycastHit tmpHit;
            if (Physics.Raycast(pitchOrigin.position, forward, out tmpHit, Mathf.Infinity, layerMask))
            {
                muzzlePoint = tmpHit.point;
            }
            else
            {
                muzzlePoint = screenHit.point;
            }
        }

        if (pitchOrigin)
            player.CharacterOriginPosition.Value = pitchOrigin.position;
        player.CharacterRaycastPosition.Value = muzzlePoint;
        player.CharacterIsOnTarget.Value = Vector3.Distance(screenHit.point, muzzlePoint) < CustomUtilities.DefaultRaycastThreshold;
    }
}
