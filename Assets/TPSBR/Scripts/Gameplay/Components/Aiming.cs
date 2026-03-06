namespace TPSBR
{
	using UnityEngine;
	using Fusion;

	[DefaultExecutionOrder(-9)]
	public sealed class Aiming : ContextBehaviour
	{
		public Vector3 TargetPosition         { get; private set; }
		public Vector3 ScreenHitPoint         { get; private set; }
		public Vector3 FireHitPoint           { get; private set; }
		public Vector3 AimPoint               { get; private set; }
		public Vector3 AimOrigin              { get; private set; }
		public bool    IsUndesiredTargetPoint { get; private set; }
		public bool    IsCrosshairOccluded    { get; private set; }
		public bool    IsCrosshairOnHiddenLayer { get; private set; }
		public bool    IsCrosshairOnAgent { get; private set; }

		[SerializeField]
		private float _aimDistance = 40f;
		[SerializeField]
		private float _cameraRayDistance = 500f;
		[SerializeField]
		private LayerMask _cameraHitMask = 0;
		[SerializeField]
		private bool _spawnDebugHitPointProxies = true;
		[SerializeField]
		[Min(0.01f)]
		private float _debugHitPointProxyScale = 0.15f;
		[SerializeField]
		private bool _surfaceDeflection = true;
		[SerializeField, Range(1, 16)]
		private int _surfaceDeflectionIterations = 6;
		[SerializeField, Min(0.05f)]
		private float _surfaceDeflectionStepDistance = 0.6f;
		[SerializeField, Min(0.001f)]
		private float _surfaceDeflectionSurfaceOffset = 0.02f;
		[SerializeField, Min(0.05f)]
		private float _surfaceDeflectionMaxTravelDistance = 8.0f;

		[Networked]
		private Vector3 _replicatedAimRayOrigin { get; set; }
		[Networked]
		private Vector3 _replicatedAimRayDirection { get; set; }
		[Networked]
		private NetworkBool _replicatedHasAimRay { get; set; }
		[Networked]
		private Vector3 _replicatedFireOrigin { get; set; }
		[Networked]
		private Vector3 _replicatedCameraHitPoint { get; set; }
		[Networked]
		private Vector3 _replicatedFireHitPoint { get; set; }
		[Networked]
		private NetworkBool _replicatedHasAimHitPoints { get; set; }
		[Networked]
		private NetworkBool _replicatedAimIsUndesiredTargetPoint { get; set; }

		private Health     _health;
		private Weapons    _weapons;
		private Character  _character;
		private RaycastHit[] _aimHits = new RaycastHit[16];
		private Transform  _debugProxyRoot;
		private Transform  _originProxy;
		private Transform  _targetPositionProxy;
		private Transform  _screenHitPointProxy;
		private Transform  _fireHitPointProxy;
		private bool       _loggedMissingAuthoritativeAimRay;
		private bool       _loggedMissingAuthoritativeHitPoints;

		public Vector3 GetTargetPoint(bool checkReachability, bool resolveRenderHistory)
		{
			if (_character == null || _weapons == null || Runner == null)
			{
				var fallbackPosition = transform.position + transform.forward * 500f;
				if (checkReachability == true)
				{
					IsUndesiredTargetPoint = true;
				}
				IsCrosshairOccluded = true;
				IsCrosshairOnHiddenLayer = false;
				IsCrosshairOnAgent = false;
				return fallbackPosition;
			}

			if (TryGetAimPipeline(resolveRenderHistory, out _, out _, out _, out Vector3 fireHitPoint, out bool isUndesiredTargetPoint) == false)
			{
				IsCrosshairOccluded = true;
				IsCrosshairOnHiddenLayer = false;
				IsCrosshairOnAgent = false;
				return transform.position + transform.forward * Mathf.Max(1.0f, _aimDistance);
			}

			IsUndesiredTargetPoint = isUndesiredTargetPoint;
			// Gameplay-authoritative targeting always uses the resolved fire point.
			// (This includes any surface deflection applied in the aim pipeline.)
			return fireHitPoint;
		}

		public bool TryGetCrosshairAndHitPoints(bool resolveRenderHistory, out Vector3 fireOrigin, out Vector3 cameraHitPoint, out Vector3 characterHitPoint, out bool isUndesiredTargetPoint)
		{
			fireOrigin = default;
			cameraHitPoint = default;
			characterHitPoint = default;
			isUndesiredTargetPoint = false;

			if (resolveRenderHistory == true)
			{
				bool hasLocalInputAuthority = HasLocalInputAimingAuthority() == true;
				if (HasStateAuthority == true || hasLocalInputAuthority == true)
				{
					if (TryGetAimPipeline(true, out fireOrigin, out _, out cameraHitPoint, out characterHitPoint, out isUndesiredTargetPoint) == true)
						return true;
				}

				return TryGetReplicatedAuthoritativeHitPoints(out fireOrigin, out cameraHitPoint, out characterHitPoint, out isUndesiredTargetPoint);
			}

			if (TryGetAimPipeline(resolveRenderHistory, out fireOrigin, out _, out cameraHitPoint, out characterHitPoint, out isUndesiredTargetPoint) == false)
				return false;

			return true;
		}

		public bool TryGetCrosshairRay(bool resolveRenderHistory, out Vector3 rayOrigin, out Vector3 rayDirection)
		{
			rayOrigin = default;
			rayDirection = default;

			return TryResolveCrosshairRay(resolveRenderHistory, out rayOrigin, out rayDirection);
		}

		public bool HasDeterministicLookAtSource(bool resolveRenderHistory)
		{
			if (resolveRenderHistory == false)
				return true;

			if (TryGetAuthoritativeInputAimRay(out _, out _) == true)
				return true;

			return _replicatedHasAimHitPoints == true;
		}

		public void GetAimPose(bool resolveRenderHistory, out Vector3 origin, out Vector3 point)
		{
			if (TryGetAimPipeline(resolveRenderHistory, out origin, out _, out point, out _, out _) == false)
			{
				Vector3 direction;
				if (TryGetAimDirection(resolveRenderHistory, out direction) == false)
				{
					direction = transform.forward;
				}

				if (direction.sqrMagnitude <= 0.0001f)
				{
					direction = Vector3.forward;
				}

				direction.Normalize();
				point = origin + direction * Mathf.Max(1.0f, _aimDistance);
			}

			AimOrigin = origin;
			AimPoint = point;
		}

		public bool TryGetTargetPosition(bool resolveRenderHistory, out Vector3 origin, out Vector3 targetPosition)
		{
			origin = default;
			targetPosition = default;

			if (_character == null)
				return false;

			TransformData fireTransform = _character.GetFireTransform(resolveRenderHistory);
			origin = fireTransform.Position;

			if (TryGetDirectionalGoalPoint(resolveRenderHistory, origin, out targetPosition) == false)
				return false;

			TargetPosition = targetPosition;
			return true;
		}

		public bool TryGetObservedCameraPose(bool resolveRenderHistory, out Vector3 position, out Vector3 direction)
		{
			position = default;
			direction = default;

			if (_character == null)
				return false;

			bool hasLocalRenderAuthority = IsLocallyObservedAimingOwner() == true &&
				Context != null &&
				Context.Camera != null;

			if (resolveRenderHistory == false && hasLocalRenderAuthority == true)
			{
				Context.Camera.SyncForGameplayRender();
				Context.Camera.GetPostBlendCameraPose(out Vector3 postBlendPosition, out Quaternion postBlendRotation, out _);
				position = postBlendPosition;
				direction = postBlendRotation * Vector3.forward;
				bool invalidLocalPosition = float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z);
				if (invalidLocalPosition == false && direction.sqrMagnitude > 0.0001f)
				{
					direction.Normalize();
					return true;
				}

				return false;
			}

			TransformData replicatedCamera = _character.GetCameraTransform(resolveRenderHistory);
			position = replicatedCamera.Position;
			direction = replicatedCamera.Rotation * Vector3.forward;

			bool invalidPosition = float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z);
			if (invalidPosition == true || direction.sqrMagnitude <= 0.0001f)
				return false;

			direction.Normalize();
			return true;
		}

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			CleanupDebugHitPointProxies();
		}

		public override void FixedUpdateNetwork()
		{
			if (HasStateAuthority == false)
				return;

			if (TryGetAuthoritativeInputAimRay(out Vector3 rayOrigin, out Vector3 rayDirection) == true)
			{
				_replicatedAimRayOrigin = rayOrigin;
				_replicatedAimRayDirection = rayDirection;
				_replicatedHasAimRay = true;
				_loggedMissingAuthoritativeAimRay = false;
			}
			else if (_loggedMissingAuthoritativeAimRay == false)
			{
				Debug.LogWarning($"[{nameof(Aiming)}] Missing authoritative aim ray in FixedUpdateNetwork for state authority object {Object?.Id}.", this);
				_loggedMissingAuthoritativeAimRay = true;
			}

			if (TryGetAimPipeline(true, out Vector3 fireOrigin, out _, out Vector3 cameraHitPoint, out Vector3 fireHitPoint, out bool isUndesiredTargetPoint) == true)
			{
				_replicatedFireOrigin = fireOrigin;
				_replicatedCameraHitPoint = cameraHitPoint;
				_replicatedFireHitPoint = fireHitPoint;
				_replicatedAimIsUndesiredTargetPoint = isUndesiredTargetPoint;
				_replicatedHasAimHitPoints = true;
				_loggedMissingAuthoritativeHitPoints = false;
			}
			else if (_loggedMissingAuthoritativeHitPoints == false)
			{
				Debug.LogWarning($"[{nameof(Aiming)}] Missing authoritative hitpoint chain in FixedUpdateNetwork for state authority object {Object?.Id}.", this);
				_loggedMissingAuthoritativeHitPoints = true;
			}
		}

		public override void Render()
		{
			if (_character == null || _health == null)
				return;
			if (_health.IsAlive == false)
				return;

			bool hasAimPipeline = TryGetAimPipeline(false, out _, out _, out _, out _, out bool isUndesiredTargetPoint);
			if (hasAimPipeline == true)
			{
				IsUndesiredTargetPoint = isUndesiredTargetPoint;
				return;
			}

			IsUndesiredTargetPoint = true;
			IsCrosshairOccluded = true;
			IsCrosshairOnHiddenLayer = false;
			IsCrosshairOnAgent = false;
			SetDebugHitPointProxiesActive(false);
		}

		private void Awake()
		{
			_health    = GetComponent<Health>();
			_weapons   = GetComponent<Weapons>();
			_character = GetComponent<Character>();
		}

		private void OnDisable()
		{
			SetDebugHitPointProxiesActive(false);
		}

		private bool TryGetAimDirection(bool resolveRenderHistory, out Vector3 direction)
		{
			direction = default;

			if (_character == null)
				return false;

			if (_character.CharacterController != null)
			{
				Quaternion lookRotation;
				if (resolveRenderHistory == false)
				{
					lookRotation = _character.CharacterController.RenderData.LookRotation;
				}
				else
				{
					lookRotation = _character.CharacterController.Data.LookRotation;
				}

				if (float.IsNaN(lookRotation.x) == false &&
					float.IsNaN(lookRotation.y) == false &&
					float.IsNaN(lookRotation.z) == false &&
					float.IsNaN(lookRotation.w) == false)
				{
					direction = lookRotation * Vector3.forward;
					if (direction.sqrMagnitude > 0.0001f)
					{
						direction.Normalize();
						return true;
					}
				}
			}

			Transform cameraHandle = _character.GetCameraHandle();
			if (cameraHandle != null)
			{
				direction = cameraHandle.forward;
				if (direction.sqrMagnitude > 0.0001f)
				{
					direction.Normalize();
					return true;
				}
			}

			return false;
		}

		private bool TryGetCameraAimHitPoint(bool resolveRenderHistory, out Vector3 cameraPosition, out Vector3 point)
		{
			cameraPosition = default;
			point = default;
			IsCrosshairOnHiddenLayer = false;
			IsCrosshairOnAgent = false;
			if (TryResolveCrosshairRay(resolveRenderHistory, out cameraPosition, out Vector3 cameraRayDirection) == false)
				return false;

			LayerMask cameraHitMask = ResolveCameraHitMask();
			LayerMask cameraTargetHitMask = cameraHitMask;
			if (_weapons != null)
			{
				cameraTargetHitMask |= _weapons.HitMask;
			}

			float cameraRayDistance = Mathf.Max(1.0f, _cameraRayDistance);
			Vector3 cameraRayTarget = cameraPosition + cameraRayDirection * cameraRayDistance;
			int hiddenLayer = LayerMask.NameToLayer("Hidden");
			LayerMask crosshairStateMask = cameraTargetHitMask;
			if (hiddenLayer >= 0)
			{
				crosshairStateMask |= (1 << hiddenLayer);
			}

			if (TryResolvePhysicsRaycastHit(cameraPosition, cameraRayTarget, crosshairStateMask, out RaycastHit crosshairHit) == true &&
				crosshairHit.collider != null)
			{
				if (hiddenLayer >= 0)
				{
					IsCrosshairOnHiddenLayer = IsLayerInHierarchy(crosshairHit.collider.transform, hiddenLayer);
				}
			}

			if (_weapons != null && _weapons.HitMask.value != 0 &&
				TryResolveLagCompensatedRaycastHit(cameraPosition, cameraRayTarget, _weapons.HitMask, out LagCompensatedHit agentHit) == true &&
				agentHit.Hitbox != null &&
				agentHit.Hitbox.Root != null)
			{
				IsCrosshairOnAgent = Object == null || agentHit.Hitbox.Root.gameObject != Object.gameObject;
			}

			point = cameraRayTarget;
			if (TryResolveRaycastHitPoint(resolveRenderHistory, cameraPosition, cameraRayTarget, cameraTargetHitMask, out Vector3 targetHitPoint) == true)
			{
				point = targetHitPoint;
			}

			return true;
		}

		private bool TryResolveCrosshairRay(bool resolveRenderHistory, out Vector3 cameraPosition, out Vector3 cameraRayDirection)
		{
			cameraPosition = default;
			cameraRayDirection = default;

			if (_character == null)
				return false;

			bool hasLocalRenderAuthority = IsLocallyObservedAimingOwner() == true &&
				Context != null &&
				Context.Camera != null;

			if (resolveRenderHistory == true)
			{
				if (TryGetAuthoritativeInputAimRay(out cameraPosition, out cameraRayDirection) == true)
					return true;

				return TryGetReplicatedAuthoritativeAimRay(out cameraPosition, out cameraRayDirection);
			}

			if (resolveRenderHistory == false && hasLocalRenderAuthority == true)
			{
				Context.Camera.SyncForGameplayRender();
				Context.Camera.GetPostBlendCameraPose(out Vector3 postBlendPosition, out Quaternion postBlendRotation, out float fieldOfView);
				cameraPosition = postBlendPosition;

				if (TryResolveCrosshairDirection(postBlendRotation, fieldOfView, out cameraRayDirection) == false)
					return false;

				return true;
			}

			return TryGetObservedCrosshairRay(resolveRenderHistory, out cameraPosition, out cameraRayDirection);
		}

		private bool TryGetReplicatedAuthoritativeAimRay(out Vector3 rayOrigin, out Vector3 rayDirection)
		{
			rayOrigin = default;
			rayDirection = default;

			if (_replicatedHasAimRay == false)
				return false;

			rayOrigin = _replicatedAimRayOrigin;
			rayDirection = _replicatedAimRayDirection;
			bool invalidOrigin = float.IsNaN(rayOrigin.x) || float.IsNaN(rayOrigin.y) || float.IsNaN(rayOrigin.z);
			if (invalidOrigin == true || rayDirection.sqrMagnitude <= 0.0001f)
				return false;

			rayDirection.Normalize();
			return true;
		}

		private bool TryGetReplicatedAuthoritativeHitPoints(out Vector3 fireOrigin, out Vector3 cameraHitPoint, out Vector3 fireHitPoint, out bool isUndesiredTargetPoint)
		{
			fireOrigin = default;
			cameraHitPoint = default;
			fireHitPoint = default;
			isUndesiredTargetPoint = false;

			if (_replicatedHasAimHitPoints == false)
				return false;

			fireOrigin = _replicatedFireOrigin;
			cameraHitPoint = _replicatedCameraHitPoint;
			fireHitPoint = _replicatedFireHitPoint;
			isUndesiredTargetPoint = _replicatedAimIsUndesiredTargetPoint;

			bool invalidFireOrigin = float.IsNaN(fireOrigin.x) || float.IsNaN(fireOrigin.y) || float.IsNaN(fireOrigin.z);
			bool invalidCameraHitPoint = float.IsNaN(cameraHitPoint.x) || float.IsNaN(cameraHitPoint.y) || float.IsNaN(cameraHitPoint.z);
			bool invalidFireHitPoint = float.IsNaN(fireHitPoint.x) || float.IsNaN(fireHitPoint.y) || float.IsNaN(fireHitPoint.z);
			if (invalidFireOrigin == true || invalidCameraHitPoint == true || invalidFireHitPoint == true)
				return false;

			return true;
		}

		private bool TryGetObservedCrosshairRay(bool resolveRenderHistory, out Vector3 cameraPosition, out Vector3 cameraRayDirection)
		{
			cameraPosition = default;
			cameraRayDirection = default;

			if (_character == null)
				return false;

			TransformData observedCamera = _character.GetCameraTransform(resolveRenderHistory);
			cameraPosition = observedCamera.Position;
			bool invalidPosition = float.IsNaN(cameraPosition.x) || float.IsNaN(cameraPosition.y) || float.IsNaN(cameraPosition.z);
			if (invalidPosition == true)
				return false;

			float fieldOfView = ResolveCrosshairFieldOfView();
			if (TryResolveCrosshairDirection(observedCamera.Rotation, fieldOfView, out cameraRayDirection) == false)
				return false;

			return true;
		}

		private float ResolveCrosshairFieldOfView()
		{
			if (_character != null)
			{
				float currentFov = _character.CurrentFOV;
				if (float.IsNaN(currentFov) == false && currentFov > 0.01f)
					return currentFov;

				float desiredFov = _character.DesiredFOV;
				if (float.IsNaN(desiredFov) == false && desiredFov > 0.01f)
					return desiredFov;

				float baseFov = _character.BaseFOV;
				if (float.IsNaN(baseFov) == false && baseFov > 0.01f)
					return baseFov;
			}

			if (Context != null && Context.Camera != null && Context.Camera.Camera != null)
			{
				float cameraFov = Context.Camera.Camera.fieldOfView;
				if (float.IsNaN(cameraFov) == false && cameraFov > 0.01f)
					return cameraFov;
			}

			return 60.0f;
		}

		private bool TryResolveCrosshairDirection(Quaternion cameraRotation, float fieldOfView, out Vector3 direction)
		{
			direction = cameraRotation * Vector3.forward;

			Vector2 crosshairViewport = new Vector2(0.5f, 0.5f);
			SceneContext sceneContext = Context;
			if (sceneContext != null)
			{
				if (sceneContext.HasUICrosshairViewport == true)
				{
					crosshairViewport = sceneContext.UICrosshairViewport;
				}
				else if (sceneContext.HasUICrosshairViewportX == true)
				{
					crosshairViewport = new Vector2(sceneContext.UICrosshairViewportX, 0.5f);
				}
			}

			crosshairViewport.x = Mathf.Clamp01(crosshairViewport.x);
			crosshairViewport.y = Mathf.Clamp01(crosshairViewport.y);

			float aspect = 0.0f;
			Camera outputCamera = sceneContext != null && sceneContext.Camera != null ? sceneContext.Camera.Camera : null;
			if (outputCamera != null)
			{
				Rect pixelRect = outputCamera.pixelRect;
				if (pixelRect.width > 0.01f && pixelRect.height > 0.01f)
				{
					aspect = pixelRect.width / pixelRect.height;
				}
				else if (outputCamera.aspect > 0.0001f)
				{
					aspect = outputCamera.aspect;
				}
			}

			if (aspect <= 0.0001f && Screen.height > 0)
			{
				aspect = (float)Screen.width / Screen.height;
			}

			if (aspect > 0.0001f && fieldOfView > 0.0f && float.IsNaN(fieldOfView) == false)
			{
				float tanHalfVerticalFov = Mathf.Tan(fieldOfView * Mathf.Deg2Rad * 0.5f);
				if (tanHalfVerticalFov > 0.0001f && float.IsNaN(tanHalfVerticalFov) == false)
				{
					float normalizedX = crosshairViewport.x * 2.0f - 1.0f;
					float normalizedY = crosshairViewport.y * 2.0f - 1.0f;

					Vector3 localDirection = new Vector3(
						normalizedX * tanHalfVerticalFov * aspect,
						normalizedY * tanHalfVerticalFov,
						1.0f);

					if (localDirection.sqrMagnitude > 0.0001f)
					{
						direction = cameraRotation * localDirection.normalized;
					}
				}
			}

			if (direction.sqrMagnitude <= 0.0001f)
				return false;

			direction.Normalize();
			return true;
		}

		private bool TryGetAuthoritativeInputAimRay(out Vector3 rayOrigin, out Vector3 rayDirection)
		{
			rayOrigin = default;
			rayDirection = default;

			if (TryGetRunnerInputAimRay(out rayOrigin, out rayDirection) == true)
				return true;

			if (IsProxy == true)
				return false;

			if (_character == null || _character.Agent == null || _character.Agent.AgentInput == null)
				return false;

			GameplayInput fixedInput = _character.Agent.AgentInput.FixedInput;
			if (fixedInput.HasAimRay == false)
				return false;

			rayOrigin = fixedInput.AimRayOrigin;
			rayDirection = fixedInput.AimRayDirection;

			bool invalidOrigin = float.IsNaN(rayOrigin.x) || float.IsNaN(rayOrigin.y) || float.IsNaN(rayOrigin.z);
			if (invalidOrigin == true || rayDirection.sqrMagnitude <= 0.0001f)
				return false;

			rayDirection.Normalize();
			return true;
		}

		private bool TryGetRunnerInputAimRay(out Vector3 rayOrigin, out Vector3 rayDirection)
		{
			rayOrigin = default;
			rayDirection = default;

			if (Runner == null || Object == null || Object.InputAuthority == PlayerRef.None)
				return false;

			if (Runner.TryGetInputForPlayer(Object.InputAuthority, out GameplayInput input) == false || input.HasAimRay == false)
				return false;

			rayOrigin = input.AimRayOrigin;
			rayDirection = input.AimRayDirection;

			bool invalidOrigin = float.IsNaN(rayOrigin.x) || float.IsNaN(rayOrigin.y) || float.IsNaN(rayOrigin.z);
			if (invalidOrigin == true || rayDirection.sqrMagnitude <= 0.0001f)
				return false;

			rayDirection.Normalize();
			return true;
		}

		private LayerMask ResolveCameraHitMask()
		{
			if (_cameraHitMask.value != 0)
				return _cameraHitMask;

			LayerMask hitMask = _weapons != null
				? (_weapons.HitMask | ObjectLayerMask.Default)
				: Physics.DefaultRaycastLayers;

			int hiddenLayer = LayerMask.NameToLayer("Hidden");
			if (hiddenLayer >= 0)
			{
				hitMask |= (1 << hiddenLayer);
			}

			int localLayer = LayerMask.NameToLayer("Local");
			if (localLayer >= 0)
			{
				hitMask &= ~(1 << localLayer);
			}

			return hitMask;
		}

		private bool TryGetDirectionalGoalPoint(bool resolveRenderHistory, Vector3 fireOrigin, out Vector3 directionalGoalPoint)
		{
			directionalGoalPoint = default;

			if (TryGetAimDirection(resolveRenderHistory, out Vector3 lookDirection) == true)
			{
				directionalGoalPoint = fireOrigin + lookDirection * Mathf.Max(1.0f, _cameraRayDistance);
				return true;
			}

			if (TryGetObservedCameraPose(resolveRenderHistory, out Vector3 cameraPosition, out Vector3 cameraDirection) == true)
			{
				directionalGoalPoint = cameraPosition + cameraDirection * Mathf.Max(1.0f, _cameraRayDistance);
				return true;
			}

			return false;
		}

		private bool TryGetAimPipeline(bool resolveRenderHistory, out Vector3 fireOrigin, out Vector3 targetPosition, out Vector3 screenHitPoint, out Vector3 fireHitPoint, out bool isUndesiredTargetPoint)
		{
			fireOrigin = default;
			targetPosition = default;
			screenHitPoint = default;
			fireHitPoint = default;
			isUndesiredTargetPoint = false;
			IsCrosshairOccluded = false;

			if (_character == null || _weapons == null || Runner == null)
				return false;

			TransformData fireTransform = _character.GetFireTransform(resolveRenderHistory);
			fireOrigin = fireTransform.Position;

			if (TryGetDirectionalGoalPoint(resolveRenderHistory, fireOrigin, out targetPosition) == false)
			{
				if (TryGetAimDirection(resolveRenderHistory, out Vector3 fallbackDirection) == false || fallbackDirection.sqrMagnitude <= 0.0001f)
				{
					fallbackDirection = transform.forward;
				}

				fallbackDirection.Normalize();
				targetPosition = fireOrigin + fallbackDirection * Mathf.Max(1.0f, _aimDistance);
			}

			Vector3 rawScreenHitPoint = default;
			bool hasCameraHitPoint = TryGetCameraAimHitPoint(resolveRenderHistory, out Vector3 cameraPosition, out rawScreenHitPoint);
			if (hasCameraHitPoint == false)
				return false;

			Vector3 originToTarget = targetPosition - fireOrigin;
			float originToTargetDistance = originToTarget.magnitude;
			Quaternion originRotation = originToTargetDistance > 0.0001f
				? Quaternion.LookRotation(originToTarget / originToTargetDistance, Vector3.up)
				: fireTransform.Rotation;

			screenHitPoint = rawScreenHitPoint;

			LayerMask observableHitMask = ResolveCameraHitMask();
			LayerMask reachabilityHitMask = observableHitMask | _weapons.HitMask;

			fireHitPoint = screenHitPoint;
			if (TryResolveRaycastHitPoint(resolveRenderHistory, fireOrigin, screenHitPoint, _weapons.HitMask, out Vector3 raycastFireHitPoint) == true)
			{
				fireHitPoint = raycastFireHitPoint;
			}
			
			if (_surfaceDeflection == true &&
				TryResolvePhysicsRaycastHit(fireOrigin, screenHitPoint, observableHitMask, out RaycastHit surfaceHit) == true)
			{
				float desiredDistance = Vector3.Distance(fireOrigin, screenHitPoint);
				bool hasInterveningOccluder = surfaceHit.distance + 0.01f < desiredDistance;
				if (hasInterveningOccluder == true &&
					TryResolveSurfaceDeflectedTargetPoint(resolveRenderHistory, cameraPosition, fireOrigin, screenHitPoint, observableHitMask, reachabilityHitMask, surfaceHit, out Vector3 deflectedTargetPoint) == true)
				{
					fireHitPoint = deflectedTargetPoint;
				}
			}

			bool isOccludedFromFireOrigin = (screenHitPoint - fireHitPoint).sqrMagnitude > 0.01f;
			bool cannotReach = _weapons.CurrentWeapon != null && _weapons.CurrentWeapon.CanFireToPosition(fireOrigin, ref fireHitPoint, _weapons.HitMask) == false;
			isUndesiredTargetPoint = isOccludedFromFireOrigin || cannotReach;
			IsCrosshairOccluded = isOccludedFromFireOrigin;

			TargetPosition = targetPosition;
			ScreenHitPoint = screenHitPoint;
			FireHitPoint = fireHitPoint;
			AimOrigin = fireOrigin;
			AimPoint = screenHitPoint;
			UpdateDebugHitPointProxies(fireOrigin, originRotation, targetPosition, screenHitPoint, fireHitPoint);

			return true;
		}

		private bool TryResolveRaycastHitPoint(bool resolveRenderHistory, Vector3 origin, Vector3 destination, LayerMask hitMask, out Vector3 hitPoint)
		{
			hitPoint = default;

			if (Runner == null)
				return false;

			Vector3 direction = destination - origin;
			float distance = direction.magnitude;
			if (distance <= 0.001f)
				return false;

			direction /= distance;

			bool canUseLagCompensation = Object != null &&
				Runner.LagCompensation != null &&
				Runner.LagCompensation.enabled == true;

			if (canUseLagCompensation == true)
			{
				if (Runner.LagCompensation.Raycast(origin, direction, distance, Object.InputAuthority,
					out LagCompensatedHit lagCompensatedHit, hitMask,
					HitOptions.IncludePhysX | HitOptions.SubtickAccuracy | HitOptions.IgnoreInputAuthority) == true)
				{
					hitPoint = lagCompensatedHit.Point;
					return true;
				}
			}

			PhysicsScene physicsScene = Runner.GetPhysicsScene();
			int hitCount = physicsScene.Raycast(origin, direction, _aimHits, distance, hitMask, QueryTriggerInteraction.Ignore);
			if (hitCount <= 0)
				return false;

			RaycastUtility.Sort(_aimHits, hitCount);
			for (int i = 0; i < hitCount; ++i)
			{
				RaycastHit candidate = _aimHits[i];
				if (candidate.collider == null)
					continue;

				if (IsSelfCollider(candidate.collider) == true)
					continue;

				hitPoint = candidate.point;
				return true;
			}

			return false;
		}

		private bool TryResolveSurfaceDeflectedTargetPoint(bool resolveRenderHistory, Vector3 cameraPosition, Vector3 fireOrigin, Vector3 desiredTargetPoint, LayerMask observableMask, LayerMask reachabilityMask, RaycastHit firstSurfaceHit, out Vector3 deflectedTargetPoint)
		{
			deflectedTargetPoint = default;

			Vector3 crosshairDirection = desiredTargetPoint - cameraPosition;
			if (crosshairDirection.sqrMagnitude <= 0.0001f)
				return false;
			crosshairDirection.Normalize();

			Vector3 fireDirection = fireOrigin - cameraPosition;
			if (fireDirection.sqrMagnitude <= 0.0001f)
				return false;
			fireDirection.Normalize();

			Vector3 fallbackNormal = firstSurfaceHit.normal.sqrMagnitude > 0.0001f
				? firstSurfaceHit.normal.normalized
				: -crosshairDirection;
			if (fallbackNormal.sqrMagnitude <= 0.0001f)
				fallbackNormal = Vector3.up;

			int sampleCount = Mathf.Max(4, _surfaceDeflectionIterations * 8);
			float cameraRayDistance = Mathf.Max(1.0f, _cameraRayDistance);
			float surfaceOffset = Mathf.Max(0.001f, _surfaceDeflectionSurfaceOffset);

			for (int i = 0; i <= sampleCount; ++i)
			{
				float t = sampleCount > 0 ? (float)i / sampleCount : 0.0f;
				Vector3 sampleDirection = Vector3.Slerp(crosshairDirection, fireDirection, t);
				if (sampleDirection.sqrMagnitude <= 0.0001f)
					continue;
				sampleDirection.Normalize();

				Vector3 sampleDestination = cameraPosition + sampleDirection * cameraRayDistance;
				Vector3 samplePoint = sampleDestination;
				if (TryResolveRaycastHitPoint(resolveRenderHistory, cameraPosition, sampleDestination, observableMask, out Vector3 cameraHitPoint) == true)
					samplePoint = cameraHitPoint;

				if (firstSurfaceHit.collider != null)
				{
					Vector3 projectedSurfacePoint = firstSurfaceHit.collider.ClosestPoint(samplePoint);
					Vector3 outward = samplePoint - projectedSurfacePoint;
					if (outward.sqrMagnitude <= 0.0001f)
					{
						outward = fallbackNormal;
					}
					else
					{
						outward.Normalize();
					}

					Vector3 surfaceCandidate = projectedSurfacePoint + outward * surfaceOffset;
					if (IsTargetReachable(resolveRenderHistory, fireOrigin, surfaceCandidate, reachabilityMask) == true)
					{
						deflectedTargetPoint = surfaceCandidate;
						return true;
					}
				}

				if (IsTargetReachable(resolveRenderHistory, fireOrigin, samplePoint, reachabilityMask) == true)
				{
					deflectedTargetPoint = samplePoint;
					return true;
				}
			}

			return false;
		}

		private bool IsTargetReachable(bool resolveRenderHistory, Vector3 origin, Vector3 target, LayerMask hitMask)
		{
			if (resolveRenderHistory == true)
			{
				if (TryResolveRaycastHitPoint(resolveRenderHistory, origin, target, hitMask, out Vector3 hitPoint) == false)
					return true;

				return (hitPoint - target).sqrMagnitude <= 0.01f;
			}

			if (TryResolvePhysicsRaycastHitPoint(origin, target, hitMask, out Vector3 physicsHitPoint) == false)
				return true;

			return (physicsHitPoint - target).sqrMagnitude <= 0.01f;
		}

		private bool TryResolvePhysicsRaycastHitPoint(Vector3 origin, Vector3 destination, LayerMask hitMask, out Vector3 hitPoint)
		{
			hitPoint = default;
			if (TryResolvePhysicsRaycastHit(origin, destination, hitMask, out RaycastHit hit) == false)
				return false;

			hitPoint = hit.point;
			return true;
		}

		private bool TryResolvePhysicsRaycastHit(Vector3 origin, Vector3 destination, LayerMask hitMask, out RaycastHit hit)
		{
			hit = default;

			if (Runner == null)
				return false;

			Vector3 direction = destination - origin;
			float distance = direction.magnitude;
			if (distance <= 0.001f)
				return false;

			direction /= distance;

			PhysicsScene physicsScene = Runner.GetPhysicsScene();
			int hitCount = physicsScene.Raycast(origin, direction, _aimHits, distance, hitMask, QueryTriggerInteraction.Ignore);
			if (hitCount <= 0)
				return false;

			RaycastUtility.Sort(_aimHits, hitCount);
			for (int i = 0; i < hitCount; ++i)
			{
				RaycastHit candidate = _aimHits[i];
				if (candidate.collider == null)
					continue;

				if (IsSelfCollider(candidate.collider) == true)
					continue;

				hit = candidate;
				return true;
			}

			return false;
		}

		private bool TryResolveLagCompensatedRaycastHit(Vector3 origin, Vector3 destination, LayerMask hitMask, out LagCompensatedHit hit)
		{
			hit = default;

			if (Runner == null || Object == null)
				return false;

			if (Runner.LagCompensation == null || Runner.LagCompensation.enabled == false)
				return false;

			Vector3 direction = destination - origin;
			float distance = direction.magnitude;
			if (distance <= 0.001f)
				return false;

			direction /= distance;

			return Runner.LagCompensation.Raycast(origin, direction, distance, Object.InputAuthority, out hit, hitMask,
				HitOptions.IncludePhysX | HitOptions.SubtickAccuracy | HitOptions.IgnoreInputAuthority);
		}

		private bool IsSelfCollider(Collider collider)
		{
			if (collider == null)
				return false;

			Transform colliderTransform = collider.transform;
			if (colliderTransform == null)
				return false;

			return colliderTransform.IsChildOf(transform);
		}

		private static bool IsLayerInHierarchy(Transform transform, int layer)
		{
			if (transform == null || layer < 0)
				return false;

			Transform current = transform;
			while (current != null)
			{
				if (current.gameObject.layer == layer)
					return true;

				current = current.parent;
			}

			return false;
		}

		private void UpdateDebugHitPointProxies(Vector3 origin, Quaternion originRotation, Vector3 targetPosition, Vector3 screenHitPoint, Vector3 fireHitPoint)
		{
			if (_spawnDebugHitPointProxies == false || Application.isPlaying == false)
			{
				SetDebugHitPointProxiesActive(false);
				return;
			}
			if (IsLocalDebugOwner() == false)
			{
				SetDebugHitPointProxiesActive(false);
				return;
			}

			EnsureDebugHitPointProxies();
			if (_originProxy != null)
			{
				_originProxy.position = origin;
				_originProxy.rotation = originRotation;
			}
			if (_targetPositionProxy != null)
			{
				_targetPositionProxy.position = targetPosition;
				_targetPositionProxy.rotation = originRotation;
			}
			if (_screenHitPointProxy != null)
			{
				_screenHitPointProxy.position = screenHitPoint;
				_screenHitPointProxy.rotation = originRotation;
			}
			if (_fireHitPointProxy != null)
			{
				_fireHitPointProxy.position = fireHitPoint;
				_fireHitPointProxy.rotation = originRotation;
			}

			SetDebugHitPointProxiesActive(true);
		}

		private bool IsLocalDebugOwner()
		{
			return IsLocallyObservedAimingOwner();
		}

		private bool IsLocallyObservedAimingOwner()
		{
			return Agent.IsLocalObservedInputOwner(_character != null ? _character.Agent : null);
		}

		private bool HasLocalInputAimingAuthority()
		{
			Agent agent = _character != null ? _character.Agent : null;
			if (agent == null || agent.HasInputAuthority == false)
				return false;

			SceneContext context = agent.Context;
			return context != null && context.HasInput == true;
		}

		private void EnsureDebugHitPointProxies()
		{
			if (_debugProxyRoot == null)
			{
				GameObject root = new GameObject($"{name}_AimDebug");
				_debugProxyRoot = root.transform;

				UnityEngine.SceneManagement.Scene owningScene = gameObject.scene;
				if (owningScene.IsValid() == true && owningScene.isLoaded == true)
				{
					UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(root, owningScene);
				}
			}

			if (_originProxy == null)
			{
				_originProxy = CreateDebugProxy("Origin", Color.white);
			}

			if (_targetPositionProxy == null)
			{
				_targetPositionProxy = CreateDebugProxy("TargetPosition", Color.cyan);
			}

			if (_screenHitPointProxy == null)
			{
				_screenHitPointProxy = CreateDebugProxy("ScreenHitPoint", Color.green);
			}

			if (_fireHitPointProxy == null)
			{
				_fireHitPointProxy = CreateDebugProxy("FireHitPoint", Color.red);
			}

			float debugScale = Mathf.Max(0.01f, _debugHitPointProxyScale);
			if (_originProxy != null)
			{
				_originProxy.localScale = Vector3.one * debugScale;
			}
			if (_targetPositionProxy != null)
			{
				_targetPositionProxy.localScale = Vector3.one * debugScale;
			}
			if (_screenHitPointProxy != null)
			{
				_screenHitPointProxy.localScale = Vector3.one * debugScale;
			}
			if (_fireHitPointProxy != null)
			{
				_fireHitPointProxy.localScale = Vector3.one * debugScale;
			}
		}

		private Transform CreateDebugProxy(string proxyName, Color color)
		{
			GameObject proxy = GameObject.CreatePrimitive(PrimitiveType.Sphere);
			proxy.name = proxyName;
			proxy.layer = LayerMask.NameToLayer("Ignore Raycast");
			proxy.transform.SetParent(_debugProxyRoot, false);
			proxy.transform.localScale = Vector3.one * Mathf.Max(0.01f, _debugHitPointProxyScale);

			Renderer renderer = proxy.GetComponent<Renderer>();
			if (renderer != null)
			{
				Material material = renderer.material;
				material.color = color;
			}

			Collider proxyCollider = proxy.GetComponent<Collider>();
			if (proxyCollider != null)
			{
				Destroy(proxyCollider);
			}

			return proxy.transform;
		}

		private void SetDebugHitPointProxiesActive(bool active)
		{
			if (_debugProxyRoot != null && _debugProxyRoot.gameObject.activeSelf != active)
			{
				_debugProxyRoot.gameObject.SetActive(active);
			}
		}

		private void CleanupDebugHitPointProxies()
		{
			if (_debugProxyRoot != null)
			{
				Destroy(_debugProxyRoot.gameObject);
			}

			_debugProxyRoot = null;
			_originProxy = null;
			_targetPositionProxy = null;
			_screenHitPointProxy = null;
			_fireHitPointProxy = null;
		}
	}
}
