using UnityEngine;
using UnityEngine.InputSystem;

public class CharacterInputController : PlayerInputListener
{
    private Vector3 currentMove, currentMoveGoal;
    private Vector2 currentLook, currentLookGoal;
    private float currentPitch = 0f;

    [SerializeField]
    private float   lerpSpeed = 10f, 
                    moveSpeed = 0.025f, 
                    turnSpeed = 7.5f, 
                    pitchSpeed = 2.5f, 
                    sprintSpeed = 2f;

    [SerializeField]
    private float pitchMin = -30f, pitchMax = 30f;


    [SerializeField]
    private Transform origin, target, hitTarget;


    protected override void Update()
    {
        base.Update();

        currentMove = Vector3.Lerp(currentMove, currentMoveGoal, Time.deltaTime * lerpSpeed);
        currentLook = Vector2.Lerp(currentLook, currentLookGoal, Time.deltaTime * lerpSpeed);
    }

    private void FixedUpdate()
    {
        // move (xz) self
        transform.Translate(currentMove * moveSpeed * (isSprinting ? sprintSpeed : 1f), Space.Self);
        
        // turn (yaw) self
        transform.Rotate(Vector3.up, currentLook.x * turnSpeed, Space.Self);

        // turn (pitch) origin
        currentPitch += currentLook.y * pitchSpeed;
        currentPitch = Mathf.Clamp(currentPitch, pitchMin, pitchMax);
        origin.localRotation = Quaternion.Euler(currentPitch, Mathf.Lerp(currentLook.x * turnSpeed, 0f, Time.fixedDeltaTime* lerpSpeed), 0f);
        target.position = origin.forward * CustomUtilities.DefaultScalarDistance;

        // setup raycast
        int layerMask = 1 << LayerMask.NameToLayer("Player");
        layerMask = ~layerMask;

        Vector3 forward = Vector3.Normalize(target.position - Camera.main.transform.position);

        // check if camera has line of sight to reticle
        RaycastHit screenHit;
        if (!Physics.Raycast(Camera.main.transform.position, forward, out screenHit, Mathf.Infinity, layerMask))
        {
            screenHit.point = Camera.main.transform.position + forward * CustomUtilities.DefaultScalarDistance;
        }

        // check if origin has line of sight
        forward = Vector3.Normalize(screenHit.point - origin.position);

        RaycastHit muzzleHit;
        if (!Physics.Raycast(origin.position, forward, out muzzleHit, Mathf.Infinity, layerMask))
        {
            muzzleHit.point = screenHit.point;
        }

        hitTarget.position = muzzleHit.point;


        // set player data
        PlayerData.LocalPlayer.Position = transform.position;
        PlayerData.LocalPlayer.Rotation = new Vector3(currentPitch, transform.rotation.y, 0f); // lean roll?
        PlayerData.LocalPlayer.Direction = transform.TransformDirection(forward);
        PlayerData.LocalPlayer.OriginPosition = origin.position;
        PlayerData.LocalPlayer.IsOnTarget = Vector3.Distance(screenHit.point, muzzleHit.point) < CustomUtilities.DefaultRaycastThreshold; // replace with FoW sample point (v1.4)
        PlayerData.LocalPlayer.TargetPosition = screenHit.point;
        PlayerData.LocalPlayer.RaycastPosition = muzzleHit.point;
    }

    protected override void OnMove(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            currentMoveGoal = new Vector3(context.ReadValue<Vector2>().x, 0f, context.ReadValue<Vector2>().y);
        }
        else if (context.canceled)
        {
            currentMoveGoal = Vector2.zero;
        }
    }
    protected override void OnLook(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            currentLookGoal = context.ReadValue<Vector2>();
        }
        else if (context.canceled)
        {
            currentLookGoal = Vector2.zero;
        }
    }

    protected override void OnLean(InputAction.CallbackContext context)
    {
        if (context.performed)
        {
            //transform.localScale = new Vector3(Mathf.Sign(context.ReadValue<float>()) * 1f, 1f, 1f);
        }
    }
}
