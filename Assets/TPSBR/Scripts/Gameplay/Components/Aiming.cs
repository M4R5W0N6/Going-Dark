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

		private Health     _health;
		private Weapons    _weapons;
		private Character  _character;
		private RaycastHit[] _aimHits = new RaycastHit[16];
		private Transform  _debugProxyRoot;
		private Transform  _originProxy;
		private Transform  _targetPositionProxy;
		private Transform  _screenHitPointProxy;
		private Transform  _fireHitPointProxy;

		public Vector3 GetTargetPoint(bool checkReachability, bool resolveRenderHistory)
		{
			if (_character == null || _weapons == null || Runner == null)
			{
				var fallbackPosition = transform.position + transform.forward * 500f;
				if (checkReachability == true)
				{
					IsUndesiredTargetPoint = true;
				}
				return fallbackPosition;
			}

			if (TryGetAimPipeline(resolveRenderHistory, out _, out _, out Vector3 screenHitPoint, out Vector3 fireHitPoint, out bool isUndesiredTargetPoint) == false)
				return transform.position + transform.forward * Mathf.Max(1.0f, _aimDistance);

			IsUndesiredTargetPoint = isUndesiredTargetPoint;
			return checkReachability == true ? fireHitPoint : screenHitPoint;
		}

		public bool TryGetCrosshairAndHitPoints(bool resolveRenderHistory, out Vector3 fireOrigin, out Vector3 cameraHitPoint, out Vector3 characterHitPoint, out bool isUndesiredTargetPoint)
		{
			fireOrigin = default;
			cameraHitPoint = default;
			characterHitPoint = default;
			isUndesiredTargetPoint = false;

			if (TryGetAimPipeline(resolveRenderHistory, out fireOrigin, out _, out cameraHitPoint, out characterHitPoint, out isUndesiredTargetPoint) == false)
				return false;

			return true;
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

			bool hasLocalInputAuthority = _character.HasInputAuthority == true &&
				Context != null &&
				Context.HasInput == true &&
				Context.Camera != null;

			if (resolveRenderHistory == false && hasLocalInputAuthority == true)
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

		private bool TryGetCameraAimHitPoint(bool resolveRenderHistory, Vector3 targetPosition, out Vector3 cameraPosition, out Vector3 point)
		{
			cameraPosition = default;
			point = default;
			Vector3 cameraRayDirection = default;
			bool hasExplicitCameraRayDirection = false;

			bool requiresAuthoritativeInputRay = resolveRenderHistory == true &&
				Object != null &&
				Object.InputAuthority != PlayerRef.None;

			if (requiresAuthoritativeInputRay == true)
			{
				if (TryGetAuthoritativeInputAimRay(out cameraPosition, out cameraRayDirection) == false)
					return false;

				hasExplicitCameraRayDirection = true;
			}

			bool hasLocalInputAuthority = _character != null &&
				_character.HasInputAuthority == true &&
				Context != null &&
				Context.HasInput == true;

			// Use local post-blend CM pose only for the locally controlled agent.
			// Remote agents must use their own replicated camera transform.
			if (hasExplicitCameraRayDirection == false &&
				resolveRenderHistory == false &&
				hasLocalInputAuthority == true &&
				Context != null &&
				Context.Camera != null)
			{
				Context.Camera.SyncForGameplayRender();
				Context.Camera.GetPostBlendCameraPose(out Vector3 postBlendPosition, out _, out _);
				cameraPosition = postBlendPosition;
			}
			else if (hasExplicitCameraRayDirection == false)
			{
				if (_character == null)
					return false;

				TransformData observedCamera = _character.GetCameraTransform(resolveRenderHistory);
				cameraPosition = observedCamera.Position;
			}

			if (float.IsNaN(cameraPosition.x) == true || float.IsNaN(cameraPosition.y) == true || float.IsNaN(cameraPosition.z) == true)
				return false;

			LayerMask cameraHitMask = ResolveCameraHitMask();
			if (hasExplicitCameraRayDirection == false)
			{
				Vector3 cameraToTarget = targetPosition - cameraPosition;
				if (cameraToTarget.sqrMagnitude <= 0.0001f)
					return false;

				cameraRayDirection = cameraToTarget.normalized;
			}

			float cameraRayDistance = Mathf.Max(1.0f, _cameraRayDistance);
			Vector3 cameraRayTarget = cameraPosition + cameraRayDirection * cameraRayDistance;

			point = cameraRayTarget;
			if (TryResolveRaycastHitPoint(resolveRenderHistory, cameraPosition, cameraRayTarget, cameraHitMask, out Vector3 targetHitPoint) == true)
			{
				point = targetHitPoint;
			}

			return true;
		}

		private bool TryGetAuthoritativeInputAimRay(out Vector3 rayOrigin, out Vector3 rayDirection)
		{
			rayOrigin = default;
			rayDirection = default;

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
			bool hasCameraHitPoint = TryGetCameraAimHitPoint(resolveRenderHistory, targetPosition, out _, out rawScreenHitPoint);
			if (hasCameraHitPoint == false)
				return false;

			Vector3 originToTarget = targetPosition - fireOrigin;
			float originToTargetDistance = originToTarget.magnitude;
			Quaternion originRotation = originToTargetDistance > 0.0001f
				? Quaternion.LookRotation(originToTarget / originToTargetDistance, Vector3.up)
				: fireTransform.Rotation;

			screenHitPoint = rawScreenHitPoint;

			fireHitPoint = screenHitPoint;
			if (TryResolveRaycastHitPoint(resolveRenderHistory, fireOrigin, screenHitPoint, _weapons.HitMask, out Vector3 raycastFireHitPoint) == true)
			{
				fireHitPoint = raycastFireHitPoint;
			}

			bool isOccludedFromFireOrigin = (screenHitPoint - fireHitPoint).sqrMagnitude > 0.01f;
			bool cannotReach = _weapons.CurrentWeapon != null && _weapons.CurrentWeapon.CanFireToPosition(fireOrigin, ref fireHitPoint, _weapons.HitMask) == false;
			isUndesiredTargetPoint = isOccludedFromFireOrigin || cannotReach;

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

		private bool IsSelfCollider(Collider collider)
		{
			if (collider == null)
				return false;

			Transform colliderTransform = collider.transform;
			if (colliderTransform == null)
				return false;

			return colliderTransform.IsChildOf(transform);
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
			return _character != null &&
				_character.HasInputAuthority == true &&
				Context != null &&
				Context.HasInput == true;
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
