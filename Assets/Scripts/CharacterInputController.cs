using UnityEngine;
using UnityEngine.InputSystem;
using Unity.Netcode;

[RequireComponent(typeof(Rigidbody), typeof(NetworkObject))]
public class CharacterInputController : PlayerInputListener
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

    private Vector2 currentMove, currentMoveGoal;
    private Vector2 currentLook, currentLookGoal;
    private float currentPitch;

    private bool isSprinting;

    private Rigidbody rigidbody;
    private NetworkObject networkObject;
    private PlayerData playerData;

    private void Awake()
    {
        TryGetComponent(out rigidbody);
        TryGetComponent(out networkObject);
        TryGetComponent(out playerData);
    }

    private void Start()
    {
        NetworkEventManager.PlayerSpawnServerRpc(playerData.OwnerClientId);
    }

    protected override void Update()
    {
        base.Update();

        if (!PlayerData.LocalPlayer)
            return;

        currentMove = Vector2.Lerp(currentMove, currentMoveGoal, Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, currentLookGoal, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        if (!PlayerData.LocalPlayer)
            return;

        if (!networkObject.IsLocalPlayer)
            return;

        playerData.Move.Value = currentMove * moveSpeed * (isSprinting ? sprintSpeed : 1f);
        //playerData.Move.Value = Camera.main.transform.TransformDirection(playerData.Move.Value);

        currentPitch += currentLook.y * pitchSpeed;
        currentPitch = Mathf.Clamp(currentPitch, pitchMin, pitchMax);

        playerData.Turn.Value = new Vector2(currentPitch, currentLook.x);

        // setup raycast
        int layerMask = 1 << LayerMask.NameToLayer("Player");
        layerMask = ~layerMask;

        Vector3 forward = Vector3.Normalize(pitchOrigin.forward * CustomUtilities.DefaultScalarDistance - Camera.main.transform.position);

        // check if camera has line of sight to reticle
        RaycastHit screenHit;
        if (!Physics.Raycast(Camera.main.transform.position, forward, out screenHit, Mathf.Infinity, layerMask))
        {
            screenHit.point = Camera.main.transform.position + forward * CustomUtilities.DefaultScalarDistance;
        }

        // check if origin has line of sight
        forward = Vector3.Normalize(screenHit.point - pitchOrigin.position);

        RaycastHit muzzleHit;
        if (!Physics.Raycast(pitchOrigin.position, forward, out muzzleHit, Mathf.Infinity, layerMask))
        {
            muzzleHit.point = screenHit.point;
        }

        playerData.OriginPosition.Value = pitchOrigin.position;
        playerData.TargetPosition.Value = screenHit.point;
        playerData.RaycastPosition.Value = muzzleHit.point;
        playerData.IsOnTarget.Value = Vector3.Distance(screenHit.point, muzzleHit.point) < CustomUtilities.DefaultRaycastThreshold; // replace with FoW sample point (v1.4) ?

        rigidbody.AddRelativeForce(playerData.Move.Value.x, 0f, playerData.Move.Value.y);

        transform.Rotate(Vector3.up, playerData.Turn.Value.y * turnSpeed, Space.Self);

        pitchOrigin.localRotation = Quaternion.Euler(playerData.Turn.Value.x, 0f, 0f);
    }

    protected override void OnMove(InputAction.CallbackContext context)
    {
        if (!playerData.IsLocalPlayer)
            return;

        if (context.performed)
        {
            currentMoveGoal = context.ReadValue<Vector2>();
        }
        else if (context.canceled)
        {
            currentMoveGoal = Vector2.zero;
        }
    }
    protected override void OnLook(InputAction.CallbackContext context)
    {
        if (!playerData.IsLocalPlayer)
            return;

        if (context.performed)
        {
            currentLookGoal = context.ReadValue<Vector2>();
        }
        else if (context.canceled)
        {
            currentLookGoal = Vector2.zero;
        }
    }
    protected override void OnSprint(InputAction.CallbackContext context)
    {
        if (!playerData.IsLocalPlayer)
            return;

        if (context.performed)
        {
            isSprinting = true;
        }
        else if (context.canceled)
        {
            isSprinting = false;
        }
    }

    protected override void OnLean(InputAction.CallbackContext context)
    {
        if (!playerData.IsLocalPlayer)
            return;

        if (context.performed)
        {
            //transform.localScale = new Vector3(Mathf.Sign(context.ReadValue<float>()) * 1f, 1f, 1f);
        }
    }
}
