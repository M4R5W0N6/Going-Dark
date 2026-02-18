namespace TPSBR
{
	using System;
	using UnityEngine;
	using Fusion;

	[DefaultExecutionOrder(-8)]
	public sealed class Interactions : ContextBehaviour
	{
		// PUBLIC MEMBERS

		public IInteraction InteractionTarget      { get; private set; }
		public Vector3      TargetPoint            { get; private set; }
		public Vector3      TargetPosition         { get; private set; }
		public Vector3      ScreenHitPoint         { get; private set; }
		public Vector3      FireHitPoint           { get; private set; }
		public Vector3      AimPoint               { get; private set; }
		public Vector3      AimOrigin              { get; private set; }
		public bool         IsUndesiredTargetPoint { get; private set; }

		public float        ItemDropTime => _itemDropTime;

		[Networked, HideInInspector]
		public TickTimer    DropItemTimer { get; private set; }

		public event Action<string> InteractionFailed;

		// PRIVATE MEMBERS

		[SerializeField]
		private LayerMask _interactionMask;
		[SerializeField]
		private float     _interactionDistance = 2f;
		[SerializeField]
		private float     _interactionPrecisionRadius = 0.3f;
		[SerializeField]
		private float     _itemDropTime;
		[SerializeField]
		private float     _aimDistance = 40f;
		[SerializeField]
		private float     _cameraRayDistance = 500f;
		[SerializeField]
		private LayerMask _cameraHitMask = 0;
		[SerializeField]
		private bool _spawnDebugHitPointProxies = true;
		[SerializeField]
		[Min(0.01f)]
		private float _debugHitPointProxyScale = 0.15f;

		private Health       _health;
		private Weapons      _weapons;
		private Character    _character;
		private RaycastHit[] _interactionHits = new RaycastHit[10];
		private RaycastHit[] _aimHits = new RaycastHit[16];
		private Transform    _debugProxyRoot;
		private Transform    _originProxy;
		private Transform    _targetPositionProxy;
		private Transform    _screenHitPointProxy;
		private Transform    _fireHitPointProxy;

		// PUBLIC METHODS

		public void TryInteract(bool interact, bool hold)
		{
			if (hold == false)
			{
				DropItemTimer = default;
				return;
			}

			if (_weapons.IsSwitchingWeapon() == true)
			{
				DropItemTimer = default;
				return;
			}

			if (_weapons.CurrentWeapon != null && _weapons.CurrentWeapon.IsBusy() == true)
			{
				DropItemTimer = default;
				return;
			}

			if (HasStateAuthority == false)
				return;

			UpdateInteractionTarget();

			if (InteractionTarget == null)
			{
				if (DropItemTimer.IsRunning == false && _weapons.CurrentWeaponSlot > 0 && interact == true)
				{
					DropItemTimer = TickTimer.CreateFromSeconds(Runner, _itemDropTime);
				}

				if (DropItemTimer.Expired(Runner) == true)
				{
					DropItemTimer = default;
					_weapons.DropCurrentWeapon();
				}

				return;
			}

			if (interact == false)
				return;

			if (InteractionTarget is DynamicPickup dynamicPickup && dynamicPickup.Provider is Weapon pickupWeapon)
			{
				_weapons.Pickup(dynamicPickup, pickupWeapon);
			}
			else if (InteractionTarget is WeaponPickup weaponPickup)
			{
				_weapons.Pickup(weaponPickup);
			}
			else if (InteractionTarget is ItemBox itemBox)
			{
				itemBox.Open();
			}
			else if (InteractionTarget is StaticPickup staticPickup)
			{
				bool success = staticPickup.TryConsume(gameObject, out string result);
				if (success == false && result.HasValue() == true)
				{
					RPC_InteractionFailed(result);
				}
			}
		}

		public Vector3 GetTargetPoint(bool checkReachability, bool resolveRenderHistory)
		{
			// Can happen during partial startup/misconfigured Fusion config.
			// Return a deterministic fallback instead of spamming NullReference exceptions.
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

		/// <summary>
		/// Returns the desired world-space target position generated from the look input orbit.
		/// </summary>
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

		// NetworkBehaviour INTERFACE

		public override void Despawned(NetworkRunner runner, bool hasState)
		{
			InteractionFailed = null;
			CleanupDebugHitPointProxies();
		}

		public override void Render()
		{
			if (_character.HasInputAuthority == false)
			{
				InteractionTarget = null;
				return;
			}

			if (_health.IsAlive == false)
			{
				InteractionTarget = null;
				return;
			}

			UpdateInteractionTarget();

			TargetPoint = GetTargetPoint(true, false);
		}

		// MonoBehaviour INTERFACE

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

		// PRIVATE METHODS

		private void UpdateInteractionTarget()
		{
			InteractionTarget = null;

			Vector3 cameraPosition;
			Vector3 cameraDirection;
			if (TryGetObservedCameraPose(false, out cameraPosition, out cameraDirection) == false)
			{
				var cameraTransform = _character.GetCameraTransform(false);
				cameraPosition = cameraTransform.Position;
				GetAimPose(false, out _, out Vector3 fallbackAimPoint);
				cameraDirection = (fallbackAimPoint - cameraPosition).normalized;
				if (cameraDirection.sqrMagnitude <= 0.0001f)
				{
					if (TryGetAimDirection(false, out cameraDirection) == false)
					{
						cameraDirection = transform.forward;
					}
				}
			}
			else
			{
				GetAimPose(false, out _, out Vector3 aimPoint);
				Vector3 directionToAimPoint = aimPoint - cameraPosition;
				if (directionToAimPoint.sqrMagnitude > 0.0001f)
				{
					cameraDirection = directionToAimPoint.normalized;
				}
			}

			var physicsScene = Runner.GetPhysicsScene();
			int hitCount = physicsScene.SphereCast(cameraPosition, _interactionPrecisionRadius, cameraDirection, _interactionHits, _interactionDistance, _interactionMask, QueryTriggerInteraction.Ignore);

			if (hitCount == 0)
				return;

			RaycastHit validHit = default;

			// Try to pick object that is directly in the center of the crosshair
			if (physicsScene.Raycast(cameraPosition, cameraDirection, out RaycastHit raycastHit, _interactionDistance, _interactionMask, QueryTriggerInteraction.Ignore) == true && raycastHit.collider.gameObject.layer == ObjectLayer.Interaction)
			{
				validHit = raycastHit;
			}
			else
			{
				RaycastUtility.Sort(_interactionHits, hitCount);

				for (int i = 0; i < hitCount; i++)
				{
					var hit = _interactionHits[i];

					if (hit.collider.gameObject.layer == ObjectLayer.Default)
						return; // Something is blocking interaction

					if (hit.collider.gameObject.layer == ObjectLayer.Interaction)
					{
						validHit = hit;
						break;
					}
				}
			}

			var collider = validHit.collider;

			if (collider == null)
				return;

			var interaction = collider.GetComponent<IInteraction>();
			if (interaction == null)
			{
				interaction = collider.GetComponentInParent<IInteraction>();
			}

			if (interaction != null && interaction.IsActive == true)
			{
				InteractionTarget = interaction;
			}
		}

		private bool TryGetObservedCameraPose(bool resolveRenderHistory, out Vector3 position, out Vector3 direction)
		{
			position = default;
			direction = default;

			if (_character == null || Context == null || Context.Camera == null || _character.HasInputAuthority == false)
				return false;

			// In local render phase we need CM output pose after the current frame target updates.
			if (resolveRenderHistory == false)
			{
				Context.Camera.SyncForGameplayRender();
				Context.Camera.GetPostBlendCameraPose(out Vector3 postBlendPosition, out Quaternion postBlendRotation, out _);
				position = postBlendPosition;
				direction = postBlendRotation * Vector3.forward;
				if (direction.sqrMagnitude > 0.0001f)
				{
					direction.Normalize();
					return true;
				}
			}

			TransformData fallbackCamera = _character.GetCameraTransform(resolveRenderHistory);
			Camera projectionCamera = resolveRenderHistory == false ? Context.Camera.Camera : null;
			if (projectionCamera == null)
			{
				position = fallbackCamera.Position;
				if (TryGetAimDirection(resolveRenderHistory, out direction) == false)
				{
					direction = transform.forward;
				}
				return direction.sqrMagnitude > 0.0001f;
			}

			Transform cameraTransform = projectionCamera.transform;
			position = cameraTransform.position;
			direction = cameraTransform.forward;
			if (direction.sqrMagnitude <= 0.0001f && TryGetAimDirection(resolveRenderHistory, out direction) == false)
			{
				direction = transform.forward;
			}

			bool invalidPosition = float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z);
			if (invalidPosition == true || direction.sqrMagnitude <= 0.0001f)
			{
				position = fallbackCamera.Position;
				if (TryGetAimDirection(resolveRenderHistory, out direction) == false)
				{
					direction = transform.forward;
				}
			}

			if (direction.sqrMagnitude <= 0.0001f)
				return false;

			direction.Normalize();

			return true;
		}

		private bool TryGetAimDirection(bool resolveRenderHistory, out Vector3 direction)
		{
			direction = default;

			if (_character == null)
				return false;

			if (_character.CharacterController != null)
			{
				Quaternion lookRotation;
				if (_character.HasInputAuthority == true && resolveRenderHistory == false)
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
			bool hasPostBlendCameraPosition = false;

			if (Context == null || Context.Camera == null)
				return false;

			if (resolveRenderHistory == false)
			{
				Context.Camera.SyncForGameplayRender();
				Context.Camera.GetPostBlendCameraPose(out Vector3 postBlendPosition, out _, out _);
				cameraPosition = postBlendPosition;
				hasPostBlendCameraPosition = true;
			}

			Camera outputCamera = Context.Camera.Camera;
			if (outputCamera == null)
				return false;

			if (hasPostBlendCameraPosition == false)
			{
				cameraPosition = outputCamera.transform.position;
			}

			LayerMask cameraHitMask = ResolveCameraHitMask();

			Vector3 cameraToTarget = targetPosition - cameraPosition;
			if (cameraToTarget.sqrMagnitude <= 0.0001f)
				return false;

			point = targetPosition;
			if (TryResolveRaycastHitPoint(resolveRenderHistory, cameraPosition, targetPosition, cameraHitMask, out Vector3 targetHitPoint) == true)
			{
				point = targetHitPoint;
			}

			return true;
		}

		private LayerMask ResolveCameraHitMask()
		{
			// Manual override from inspector always wins.
			if (_cameraHitMask.value != 0)
				return _cameraHitMask;

			LayerMask hitMask = _weapons != null
				? (_weapons.HitMask | ObjectLayerMask.Default)
				: Physics.DefaultRaycastLayers;

			// Ensure ScreenHitPoint can lock hidden-enemy colliders as well.
			int hiddenLayer = LayerMask.NameToLayer("Hidden");
			if (hiddenLayer >= 0)
			{
				hitMask |= (1 << hiddenLayer);
			}

			// Never let local-only first-person visuals block the camera ray.
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

			// Primary source: directional goal derived from look rotation only (UI-independent).
			if (TryGetAimDirection(resolveRenderHistory, out Vector3 lookDirection) == true)
			{
				directionalGoalPoint = fireOrigin + lookDirection * Mathf.Max(1.0f, _cameraRayDistance);
				return true;
			}

			// Fallback to observed camera forward if look rotation is unavailable.
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
			bool canUseLocalRenderCamera = _character.HasInputAuthority == true &&
				resolveRenderHistory == false &&
				Context != null &&
				Context.HasInput == true;
			bool hasCameraHitPoint = canUseLocalRenderCamera == true &&
				TryGetCameraAimHitPoint(resolveRenderHistory, targetPosition, out _, out rawScreenHitPoint);
			if (hasCameraHitPoint == false)
			{
				rawScreenHitPoint = targetPosition;
			}

			Vector3 originToTarget = targetPosition - fireOrigin;
			float originToTargetDistance = originToTarget.magnitude;
			Quaternion originRotation = originToTargetDistance > 0.0001f
				? Quaternion.LookRotation(originToTarget / originToTargetDistance, Vector3.up)
				: fireTransform.Rotation;

			// Keep ScreenHitPoint as the raw world-space camera raycast hit.
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

			bool canUseLagCompensation = resolveRenderHistory == true &&
				Object != null &&
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

			// Keep proxies local-only so each peer draws only its own aim pipeline.
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

		private void EnsureDebugHitPointProxies()
		{
			if (_debugProxyRoot == null)
			{
				GameObject root = new GameObject($"{name}_LocalAimDebug");
				_debugProxyRoot = root.transform;
				// Keep debug root unparented to avoid inheriting player yaw/pitch.
				// Put it in the same scene as this agent for multi-peer isolation.
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

		private bool IsLocalDebugOwner()
		{
			if (Object == null || Runner == null)
				return false;

			PlayerRef runnerLocalPlayer = Runner.LocalPlayer;
			if (runnerLocalPlayer != PlayerRef.None && Object.InputAuthority == runnerLocalPlayer)
				return true;

			if (Context != null && Context.LocalPlayerRef.IsRealPlayer == true && Object.InputAuthority == Context.LocalPlayerRef)
				return true;

			return false;
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

		// RPCs

		[Rpc(RpcSources.StateAuthority, RpcTargets.All, Channel = RpcChannel.Reliable)]
		private void RPC_InteractionFailed(string reason)
		{
			InteractionFailed?.Invoke(reason);
		}
	}
}

