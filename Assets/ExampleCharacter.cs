using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace TPSBR
{
    public class ExampleCharacter : MonoBehaviour
    {
        [SerializeField] private InputActionReference move;
        [SerializeField] private RectTransform crosshair;
        [FormerlySerializedAs("worldTarget")]
        [SerializeField] private Transform lookTarget;
        [FormerlySerializedAs("worldOpposite")]
        [SerializeField] private Transform backMarker;
        [FormerlySerializedAs("worldAxis")]
        [SerializeField] private Transform sideMarker;

        [SerializeField] private float speed = 1f;
        [SerializeField] private float rotationSpeed = 200f;
        [SerializeField] private float sensitivity = 1f;
        [SerializeField] private float maxLookDistance = 10f;
        [FormerlySerializedAs("oppositeDistanceFactor")]
        [SerializeField] private float backDistanceFactor = 0.5f;
        [FormerlySerializedAs("crossDistanceFactor")]
        [SerializeField] private float sideDistanceFactor = 0.5f;
        [SerializeField] private AnimationCurve rotationCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
        [SerializeField] private float directionSmoothTime = 0.05f;
        [SerializeField] private float minPitch = -40f;
        [SerializeField] private float maxPitch = 80f;

        private RectTransform _canvasRect;
        private Camera _mainCamera;
        private Vector3 _smoothedDirection = Vector3.forward;
        private Vector3 _directionVelocity = Vector3.zero;

        private void Awake()
        {
            _mainCamera = Camera.main;
            if (crosshair != null)
                _canvasRect = crosshair.parent as RectTransform;
            _smoothedDirection = transform.forward;
        }

        private void Update()
        {
            float deltaTime = Time.deltaTime;
            Vector2 screenPoint = GetMousePosition();
            UpdateCrosshair(screenPoint);
            MoveCharacter(deltaTime);
            UpdateWorldTargets(screenPoint, deltaTime);
        }

        private Vector2 GetMousePosition()
        {
            Vector2 screenPoint;
            if (Mouse.current != null)
            {
                screenPoint = Mouse.current.position.ReadValue();
            }
            else
            {
                screenPoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
            }

            return new Vector2(
                Screen.width > 0 ? Mathf.Repeat(screenPoint.x, Screen.width) : screenPoint.x,
                Screen.height > 0 ? Mathf.Repeat(screenPoint.y, Screen.height) : screenPoint.y
            );
        }

        private void UpdateCrosshair(Vector2 screenPoint)
        {
            if (crosshair == null || _canvasRect == null || _mainCamera == null)
                return;

            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPoint, _mainCamera, out Vector2 localPoint))
            {
                crosshair.anchoredPosition = localPoint;
            }
        }

        private void MoveCharacter(float deltaTime)
        {
            Vector2 input = move != null ? move.action.ReadValue<Vector2>() : Vector2.zero;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;
            forward.Normalize();

            Vector3 motion = (transform.right * input.x + forward * input.y) * speed * deltaTime;
            transform.position += motion;
        }

        private void UpdateWorldTargets(Vector2 screenPoint, float deltaTime)
        {
            if (_mainCamera == null || lookTarget == null)
                return;

            Ray ray = _mainCamera.ScreenPointToRay(screenPoint);
            float hitDistance = maxLookDistance;
            Vector3 hitPoint = ray.origin + ray.direction.normalized * hitDistance;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= Mathf.Epsilon)
                forward = Vector3.forward;
            forward.Normalize();

            lookTarget.position = transform.position + forward * hitDistance;

            Vector3 direction = hitPoint - transform.position;
            Vector3 clampedDirection = _smoothedDirection;
            if (direction.sqrMagnitude > Mathf.Epsilon)
            {
                Vector3 normalizedDirection = direction.normalized;
                _smoothedDirection = Vector3.SmoothDamp(_smoothedDirection, normalizedDirection, ref _directionVelocity, Mathf.Max(0.0001f, directionSmoothTime), Mathf.Infinity, deltaTime);
                float normalizedDistance = Mathf.Clamp01(hitDistance / maxLookDistance);
                float curveValue = rotationCurve != null ? rotationCurve.Evaluate(normalizedDistance) : 1f;
                float turnAmount = rotationSpeed * curveValue * deltaTime;
                float yaw = Mathf.Atan2(_smoothedDirection.x, _smoothedDirection.z);
                float pitch = Mathf.Asin(Mathf.Clamp(_smoothedDirection.y, -1f, 1f));
                float clampedPitch = Mathf.Clamp(pitch, Mathf.Deg2Rad * minPitch, Mathf.Deg2Rad * maxPitch);
                clampedDirection = new Vector3(
                    Mathf.Sin(yaw) * Mathf.Cos(clampedPitch),
                    Mathf.Sin(clampedPitch),
                    Mathf.Cos(yaw) * Mathf.Cos(clampedPitch)
                );
                Quaternion smoothedDesired = Quaternion.LookRotation(clampedDirection, Vector3.up);
                transform.rotation = Quaternion.RotateTowards(transform.rotation, smoothedDesired, turnAmount);
            }

            if (backMarker != null)
            {
                Vector3 inverseDirection = -clampedDirection.normalized;
                if (inverseDirection.sqrMagnitude <= Mathf.Epsilon)
                    inverseDirection = -transform.forward;

                float oppositeDistance = hitDistance * backDistanceFactor;
                backMarker.position = transform.position + inverseDirection * oppositeDistance;
            }

            if (sideMarker != null && crosshair != null && _canvasRect != null)
            {
                Vector2 canvasHalf = new Vector2(_canvasRect.rect.width * 0.5f, _canvasRect.rect.height * 0.5f);
                Vector2 normalized = new Vector2(
                    canvasHalf.x > 0f ? Mathf.Clamp(crosshair.anchoredPosition.x / canvasHalf.x, -1f, 1f) : 0f,
                    canvasHalf.y > 0f ? Mathf.Clamp(crosshair.anchoredPosition.y / canvasHalf.y, -1f, 1f) : 0f
                );

                float offsetX = normalized.x * sideDistanceFactor * hitDistance;
                sideMarker.position = transform.position + transform.right * offsetX;
                sideMarker.rotation = transform.rotation;
            }
        }
    }
}
