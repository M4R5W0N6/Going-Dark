using UnityEngine;
using Unity.Cinemachine;

namespace TPSBR
{
	public class SceneCamera : SceneService
	{
		// PUBLIC MEMBERS

		public Camera      Camera
		{
			get
			{
				return ResolveRenderCamera();
			}
		}
		public ShakeEffect ShakeEffect   => _shakeEffect;
		public bool        EnableCamera  { get; set; } = true;
		public bool        UsesCinemachine => _cinemachineCamera != null;
		public Transform   CameraTransform
		{
			get
			{
				Camera outputCamera = ResolveRenderCamera();
				if (outputCamera != null)
					return outputCamera.transform;

				return transform;
			}
		}
		public void GetPostBlendCameraPose(out Vector3 position, out Quaternion rotation, out float fieldOfView)
		{
			if (TryGetActiveCinemachinePose(out position, out rotation, out fieldOfView, out string failureReason) == false)
			{
				throw new System.InvalidOperationException($"[SceneCamera] GetPostBlendCameraPose failed: {failureReason}");
			}

			_postBlendCameraPosition = position;
			_postBlendCameraRotation = rotation;
			_postBlendFieldOfView = fieldOfView;
		}

		/// <summary>
		/// Ensures Cinemachine targets and camera output are synced for the current Unity frame.
		/// Call this from local render-time gameplay code before sampling Context.Camera.Camera.
		/// </summary>
		public void SyncForGameplayRender()
		{
			EnsureCinemachineSyncedForCurrentFrame();
		}

		// PRIVATE MEMBERS

		[SerializeField]
		private Camera _camera;
		[SerializeField]
		private AudioListener _audioListener;
		[SerializeField]
		private ShakeEffect _shakeEffect;
		private CinemachineCamera _cinemachineCamera => _cinemachineBrain != null ? _cinemachineBrain.ActiveVirtualCamera as CinemachineCamera : null;
		private CinemachineBrain _cinemachineBrain => _camera != null ? _camera.GetComponent(typeof(CinemachineBrain)) as CinemachineBrain : null;
		[Header("Cinemachine")]
		[SerializeField]
		private float _leftCameraSide = 0.0f;
		[SerializeField]
		private float _rightCameraSide = 1.0f;
		[SerializeField]
		private bool _useAimTargetGroup = true;
		[SerializeField]
		private CinemachineTargetGroup _aimGroup;
		[SerializeField]
		private Transform _aimOrigin;
		[SerializeField]
		private Transform _aimTarget;
		[SerializeField]
		[Range(0.0f, 1.0f)]
		[Tooltip("Blends lean offset axis: 0 = VerticalArmLength only, 1 = CameraDistance only.")]
		private float _leanAxis = 0.5f;

		private int _cameraCullingMask;
		private CinemachineThirdPersonFollow _thirdPersonFollow;
		private CinemachineImpulseListener _impulseListener;
		private Agent _rigStateAgent;
		private Transform _rigStateAnchor;
		private bool _rigStateLeftSide;
		private bool _hasRigState;
		private bool _hasRigTargets;
		private float _targetShoulderX;
		private float _targetVerticalArmLength;
		private float _targetCameraDistance;
		private int _lastRigLerpFrame = -1;
		private Agent _scopedVisualsAgent;
		private AgentVisionVisuals _scopedVisuals;
		private bool _hadInputLastTick;
		private bool _hasAimProxyData;
		private int _lastCinemachineSyncFrame = -1;
		private bool _isSyncingCinemachine;
		private Vector3 _postBlendCameraPosition;
		private Quaternion _postBlendCameraRotation = Quaternion.identity;
		private float _postBlendFieldOfView = 60.0f;

		// SceneService INTERFACE

		protected override void OnInitialize()
		{
			base.OnInitialize();

			ResolveCinemachineReferences();

			Camera outputCamera = Camera;
			if (outputCamera != null)
			{
				_cameraCullingMask = outputCamera.cullingMask;
			}

			_hadInputLastTick = Context != null && Context.HasInput;
		}

		protected override void OnTick()
		{
			if (Scene is Gameplay)
			{
				bool hasInput = Context.HasInput;
				bool cameraEnabled = hasInput && EnableCamera;
				Camera outputCamera = ResolveRenderCamera();

				if (hasInput != _hadInputLastTick)
				{
					OnInputOwnershipChanged(hasInput);
					_hadInputLastTick = hasInput;
				}

				if (_audioListener != null)
				{
					_audioListener.enabled = hasInput;
				}
				if (outputCamera != null)
				{
					outputCamera.enabled = hasInput;
				}
				if (_camera != null && _camera != outputCamera)
				{
					_camera.enabled = hasInput;
				}
				CinemachineBrain runtimeBrain = ResolveCinemachineBrain();
				if (runtimeBrain != null)
				{
					runtimeBrain.enabled = cameraEnabled;
				}
				CinemachineCamera runtimeCamera = ResolveActiveCinemachineCamera();
				if (runtimeCamera != null)
				{
					runtimeCamera.enabled = cameraEnabled;
				}

				// We are just switching culling mask as disabling would mean more complex camera setup to not stop UI rendering
				int cullingMask = _cameraCullingMask;
				if (outputCamera != null)
				{
					outputCamera.cullingMask = cameraEnabled == true ? cullingMask : 0;
				}
				if (_camera != null && _camera != outputCamera)
				{
					_camera.cullingMask = cameraEnabled == true ? cullingMask : 0;
				}

			}
		}

		private void OnInputOwnershipChanged(bool hasInput)
		{
			if (hasInput == true)
			{
				ResolveCinemachineReferences();

				// Force recalculation on peer switch so runtime CM components/targets are rebound cleanly.
				_thirdPersonFollow = null;
				_hasRigState = false;
				_hasRigTargets = false;
				_lastRigLerpFrame = -1;
				_lastCinemachineSyncFrame = -1;
			}
		}

		protected override void OnLateTick()
		{
			if (Scene is Gameplay == false)
				return;

			if (Context.HasInput == false || EnableCamera == false)
				return;

			EnsureCinemachineSyncedForCurrentFrame();
		}

		private void UpdateCinemachineTargets()
		{
			ResolveCinemachineReferences();

			CinemachineCamera runtimeCamera = ResolveActiveCinemachineCamera();

			if (runtimeCamera == null)
				return;

			Agent observedAgent = ResolveObservedAgent();
			UpdateScopedLocalVisuals(observedAgent);

			// Keep Cinemachine Follow/LookAt references inspector-driven.
			// We only sync proxy target transforms used by your configured targets/groups.
			if (_useAimTargetGroup == true)
			{
				_hasAimProxyData = TryUpdateAimTargetGroup(observedAgent);
			}
			else
			{
				_hasAimProxyData = false;
			}

			CinemachineThirdPersonFollow runtimeFollow = null;
			runtimeFollow = runtimeCamera.GetComponent<CinemachineThirdPersonFollow>();
			if (runtimeFollow != null)
			{
				_thirdPersonFollow = runtimeFollow;
			}

			if (_thirdPersonFollow != null)
			{
				ApplyThirdPersonFollowRigFromCharacterAnchor(observedAgent);

				float desiredCameraSide;
				bool hasDesiredCameraSide = false;

				if (Context != null && Context.HasAutoLeanSide == true)
				{
					float inputBlend = Mathf.Clamp01(Context.AutoLeanSide);
					desiredCameraSide = Mathf.Lerp(_leftCameraSide, _rightCameraSide, inputBlend);
					hasDesiredCameraSide = true;
				}
				else if (observedAgent != null)
				{
					desiredCameraSide = observedAgent.LeftSide == true ? _leftCameraSide : _rightCameraSide;
					hasDesiredCameraSide = true;
				}
				else
				{
					desiredCameraSide = _thirdPersonFollow.CameraSide;
				}

				if (hasDesiredCameraSide == true)
				{
					ApplyCameraSide(desiredCameraSide);
				}
			}
		}

		private void ApplyCameraSide(float desiredCameraSide)
		{
			if (_thirdPersonFollow == null)
				return;

			desiredCameraSide = Mathf.Clamp01(desiredCameraSide);
			if (Mathf.Abs(_thirdPersonFollow.CameraSide - desiredCameraSide) > 0.000001f)
			{
				_thirdPersonFollow.CameraSide = desiredCameraSide;
			}
		}

		private void ApplyThirdPersonFollowRigFromCharacterAnchor(Agent observedAgent)
		{
			if (_thirdPersonFollow == null || observedAgent == null || observedAgent.Character == null)
				return;

			Transform anchor = ResolveActiveCameraAnchor(observedAgent);
			if (anchor == null)
				return;

			bool leftSide = observedAgent.LeftSide;

			// Keep targets anchored to camera-state transitions, then lerp camera rig values each frame.
			if (_hasRigState == false || _rigStateAgent != observedAgent || _rigStateAnchor != anchor || _rigStateLeftSide != leftSide)
			{
				Vector3 localOffset = anchor.localPosition;
				_targetShoulderX = localOffset.x;
				_targetVerticalArmLength = localOffset.y;
				_targetCameraDistance = Mathf.Abs(localOffset.z);
				_hasRigTargets = true;

				_rigStateAgent = observedAgent;
				_rigStateAnchor = anchor;
				_rigStateLeftSide = leftSide;
				_hasRigState = true;
			}

			ApplyRigLerpTowardTargets(observedAgent);
		}

		private void ApplyRigLerpTowardTargets(Agent observedAgent)
		{
			if (_thirdPersonFollow == null || _hasRigTargets == false)
				return;

			if (_lastRigLerpFrame == Time.frameCount)
				return;

			_lastRigLerpFrame = Time.frameCount;

			float lerpSpeed = ResolveRigLerpSpeed(observedAgent);
			float t = Mathf.Clamp01(Mathf.Max(0.0f, lerpSpeed) * Time.deltaTime);
			if (t <= 0.0f)
				return;

			Vector3 shoulderOffset = _thirdPersonFollow.ShoulderOffset;
			float shoulderX = Mathf.Lerp(shoulderOffset.x, _targetShoulderX, t);
			if (Mathf.Abs(shoulderX - _targetShoulderX) <= 0.0001f)
			{
				shoulderX = _targetShoulderX;
			}
			if (Mathf.Abs(shoulderOffset.x - shoulderX) > 0.000001f)
			{
				shoulderOffset.x = shoulderX;
				_thirdPersonFollow.ShoulderOffset = shoulderOffset;
			}

			float inverseLeanContribution = 0.0f;
			if (IsSniperAds(observedAgent) == false)
			{
				float centeredCameraSide = Mathf.Abs(_thirdPersonFollow.CameraSide - 0.5f);
				inverseLeanContribution = Mathf.Clamp01(1.0f - (2.0f * centeredCameraSide));
			}

			float leanAxis = Mathf.Clamp01(_leanAxis);
			float verticalLeanContribution = inverseLeanContribution * (1.0f - leanAxis);
			float depthLeanContribution = inverseLeanContribution * leanAxis;
			float targetVerticalArmLength = _targetVerticalArmLength + verticalLeanContribution;
			float verticalArmLength = Mathf.Lerp(_thirdPersonFollow.VerticalArmLength, targetVerticalArmLength, t);
			if (Mathf.Abs(verticalArmLength - targetVerticalArmLength) <= 0.0001f)
			{
				verticalArmLength = targetVerticalArmLength;
			}
			if (Mathf.Abs(_thirdPersonFollow.VerticalArmLength - verticalArmLength) > 0.0001f)
			{
				_thirdPersonFollow.VerticalArmLength = verticalArmLength;
			}

			float targetCameraDistance = _targetCameraDistance + depthLeanContribution;
			float cameraDistance = Mathf.Lerp(_thirdPersonFollow.CameraDistance, targetCameraDistance, t);
			if (Mathf.Abs(cameraDistance - targetCameraDistance) <= 0.0001f)
			{
				cameraDistance = targetCameraDistance;
			}
			if (Mathf.Abs(_thirdPersonFollow.CameraDistance - cameraDistance) > 0.0001f)
			{
				_thirdPersonFollow.CameraDistance = cameraDistance;
			}
		}

		private static float ResolveRigLerpSpeed(Agent observedAgent)
		{
			if (observedAgent != null && observedAgent.Character != null)
				return observedAgent.Character.FOVChangeSpeed;

			return 20.0f;
		}

		private static Transform ResolveActiveCameraAnchor(Agent observedAgent)
		{
			if (observedAgent == null || observedAgent.Character == null)
				return null;

			CharacterView view = observedAgent.Character.ThirdPersonView;
			if (view == null)
				return null;

			if (observedAgent.Jetpack != null && observedAgent.Jetpack.IsActive == true && view.JetpackCameraTransform != null)
				return view.JetpackCameraTransform;

			bool isAiming = observedAgent.Character.CharacterController != null &&
				observedAgent.Character.CharacterController.Data.Aim == true;

			if (isAiming == true)
			{
				bool sniperAiming = observedAgent.Weapons != null &&
					observedAgent.Weapons.CurrentWeapon != null &&
					observedAgent.Weapons.CurrentWeapon.HitType == EHitType.Sniper;

				if (sniperAiming == true && view.SniperAimCameraTransform != null)
					return view.SniperAimCameraTransform;

				if (view.AimCameraTransform != null)
					return view.AimCameraTransform;
			}

			if (view.DefaultCameraTransform != null)
				return view.DefaultCameraTransform;

			if (view.AimCameraTransform != null)
				return view.AimCameraTransform;

			return view.CameraTransformHead;
		}

		private Agent ResolveObservedAgent()
		{
			if (Context == null)
				return null;

			Agent observedAgent = Context.ObservedAgent;
			if (observedAgent != null)
				return observedAgent;

			if (Context.NetworkGame == null || Context.LocalPlayerRef.IsRealPlayer == false)
				return null;

			Player localPlayer = Context.NetworkGame.GetPlayer(Context.LocalPlayerRef);
			if (localPlayer != null)
			{
				Agent activeAgent = localPlayer.ActiveAgent;
				if (activeAgent != null)
					return activeAgent;
			}

			// Multipeer fallback: resolve the local input-authority agent bound to this scene context.
			Agent[] agents = FindObjectsByType<Agent>(FindObjectsInactive.Include, FindObjectsSortMode.None);
			for (int i = 0; i < agents.Length; ++i)
			{
				Agent agent = agents[i];
				if (agent == null)
					continue;
				if (agent.Context != Context)
					continue;
				if (agent.HasInputAuthority == false)
					continue;

				return agent;
			}

			return null;
		}

		private static bool IsSniperAds(Agent observedAgent)
		{
			if (observedAgent == null || observedAgent.Character == null || observedAgent.Weapons == null)
				return false;
			if (observedAgent.Character.CharacterController == null || observedAgent.Character.CharacterController.Data.Aim == false)
				return false;
			if (observedAgent.Weapons.CurrentWeapon == null)
				return false;

			return observedAgent.Weapons.CurrentWeapon.HitType == EHitType.Sniper;
		}

		private void UpdateScopedLocalVisuals(Agent observedAgent)
		{
			if (observedAgent != _scopedVisualsAgent)
			{
				RestoreScopedLocalVisuals();
				_scopedVisualsAgent = observedAgent;
				_scopedVisuals = ResolveLocalVisionVisuals(observedAgent);
			}
			else if (_scopedVisuals == null && observedAgent != null)
			{
				_scopedVisuals = ResolveLocalVisionVisuals(observedAgent);
			}

			if (_scopedVisuals != null)
			{
				bool hideLocalVisuals = ShouldHideLocalVisualsForScopedSniper(observedAgent);
				_scopedVisuals.SetForceHideLocalVisuals(hideLocalVisuals);
			}
		}

		private static AgentVisionVisuals ResolveLocalVisionVisuals(Agent observedAgent)
		{
			if (observedAgent == null)
				return null;

			return observedAgent.GetComponentInChildren<AgentVisionVisuals>(true);
		}

		private bool ShouldHideLocalVisualsForScopedSniper(Agent observedAgent)
		{
			if (Context == null || Context.HasInput == false)
				return false;
			if (observedAgent == null || observedAgent.HasInputAuthority == false)
				return false;

			return IsSniperAds(observedAgent);
		}

		private void RestoreScopedLocalVisuals()
		{
			if (_scopedVisuals != null)
			{
				_scopedVisuals.SetForceHideLocalVisuals(false);
			}
		}

		private bool TryUpdateAimTargetGroup(Agent observedAgent)
		{
			if (observedAgent == null || observedAgent.Character == null || observedAgent.Aiming == null)
				return false;

			ResolveAimTargetGroupReference();
			ResolveAimProxyTargetReferences();

			if (_aimGroup == null || _aimOrigin == null || _aimTarget == null)
				return false;

			Vector3 fireOrigin;
			Vector3 targetPoint;
			if (observedAgent.Aiming.TryGetTargetPosition(false, out fireOrigin, out targetPoint) == true)
			{
				Vector3 direction = targetPoint - fireOrigin;
				if (direction.sqrMagnitude <= 0.0001f)
				{
					direction = observedAgent.transform.forward;
				}

				Quaternion originRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
				_aimOrigin.SetPositionAndRotation(fireOrigin, originRotation);
				_aimTarget.SetPositionAndRotation(targetPoint, originRotation);
				_hasAimProxyData = true;
				return true;
			}
			
			observedAgent.Aiming.GetAimPose(false, out fireOrigin, out targetPoint);
			Vector3 fallbackDirection = targetPoint - fireOrigin;
			if (fallbackDirection.sqrMagnitude <= 0.0001f)
			{
				fallbackDirection = observedAgent.transform.forward;
			}

			Quaternion fallbackRotation = Quaternion.LookRotation(fallbackDirection.normalized, Vector3.up);
			_aimOrigin.SetPositionAndRotation(fireOrigin, fallbackRotation);
			_aimTarget.SetPositionAndRotation(targetPoint, fallbackRotation);
			_hasAimProxyData = true;
			return true;
		}

		private void ResolveAimTargetGroupReference()
		{
			if (_aimGroup != null)
				return;

			Transform searchRoot = transform.parent != null ? transform.parent : transform;
			_aimGroup = searchRoot.GetComponentInChildren<CinemachineTargetGroup>(true);
		}

		private void ResolveAimProxyTargetReferences()
		{
			if (_aimGroup != null)
			{
				var targets = _aimGroup.Targets;
				if (_aimOrigin == null && targets.Count > 0)
				{
					_aimOrigin = targets[0].Object as Transform;
				}

				if (_aimTarget == null && targets.Count > 1)
				{
					_aimTarget = targets[1].Object as Transform;
				}
			}

			Transform parent = transform.parent != null ? transform.parent : transform;

			if (_aimOrigin == null)
			{
				Transform existingOrigin = parent.Find("TargetOrigin");
				if (existingOrigin == null)
				{
					existingOrigin = parent.Find("Character");
				}
				if (existingOrigin != null)
				{
					_aimOrigin = existingOrigin;
				}
			}

			if (_aimTarget == null)
			{
				Transform existingCrosshair = parent.Find("TargetCrosshair");
				if (existingCrosshair == null)
				{
					existingCrosshair = parent.Find("Crosshair");
				}
				if (existingCrosshair != null)
				{
					_aimTarget = existingCrosshair;
				}
			}
		}

		private void ResolveCinemachineReferences()
		{
			Camera runnerSceneCamera = TryResolveRunnerSceneCamera();
			if (runnerSceneCamera != null)
			{
				_camera = runnerSceneCamera;
			}

			if (_camera == null)
			{
				_camera = GetComponentInChildren<Camera>(true);
			}

			if (_camera != null)
			{
				AudioListener cameraAudioListener = _camera.GetComponent<AudioListener>();
				if (cameraAudioListener != null)
				{
					_audioListener = cameraAudioListener;
				}
			}

			CinemachineBrain resolvedBrain = ResolveCinemachineBrain();
			if (resolvedBrain != null && _camera != null && resolvedBrain.ControlledObject != _camera.gameObject)
			{
				// CM3 brain can live on a different GameObject; bind it explicitly to the render camera.
				resolvedBrain.ControlledObject = _camera.gameObject;
			}

			CinemachineCamera runtimeCamera = ResolveActiveCinemachineCamera();
			if (runtimeCamera != null && _thirdPersonFollow == null)
			{
				_thirdPersonFollow = runtimeCamera.GetComponent<CinemachineThirdPersonFollow>();
			}
			EnsureImpulseListener(runtimeCamera);
		}

		private Camera GetOutputCamera()
		{
			CinemachineBrain runtimeBrain = ResolveCinemachineBrain();
			if (runtimeBrain != null)
			{
				if (runtimeBrain.OutputCamera != null)
					return runtimeBrain.OutputCamera;

				GameObject controlledObject = runtimeBrain.ControlledObject;
				if (controlledObject != null && controlledObject.TryGetComponent(out Camera controlledCamera) == true)
					return controlledCamera;
			}

			Camera runnerSceneCamera = TryResolveRunnerSceneCamera();
			if (runnerSceneCamera != null)
				return runnerSceneCamera;

			return _camera;
		}

		private Camera ResolveRenderCamera()
		{
			ResolveCinemachineReferences();

			// Always prefer the Cinemachine brain output camera so all
			// aim/UI projection paths share the exact render transform.
			Camera outputCamera = GetOutputCamera();
			if (outputCamera != null)
				return outputCamera;

			return _camera;
		}

		private void EnsureCinemachineSyncedForCurrentFrame()
		{
			if (Scene is Gameplay == false)
				return;
			if (Application.isPlaying == false)
				return;
			if (_isSyncingCinemachine == true)
				return;
			if (_lastCinemachineSyncFrame == Time.frameCount)
				return;
			if (Context == null || Context.HasInput == false || EnableCamera == false)
				return;

			ResolveCinemachineReferences();
			CinemachineBrain runtimeBrain = ResolveCinemachineBrain();
			if (runtimeBrain == null || runtimeBrain.enabled == false)
				return;

			_isSyncingCinemachine = true;
			try
			{
				UpdateCinemachineTargets();

				CachePostBlendCameraPose();
				_lastCinemachineSyncFrame = Time.frameCount;
			}
			finally
			{
				_isSyncingCinemachine = false;
			}
		}

		private Camera TryResolveRunnerSceneCamera()
		{
			if (Context == null || Context.Runner == null)
				return null;

			var runnerScene = Context.Runner.SimulationUnityScene;
			if (runnerScene.IsValid() == false || runnerScene.isLoaded == false)
				return null;

			Camera taggedMainCamera = null;
			Camera brainCamera = null;
			Camera enabledBrainCamera = null;

			var sceneCameras = runnerScene.GetComponents<Camera>(true);
			for (int i = 0; i < sceneCameras.Count; ++i)
			{
				Camera sceneCamera = sceneCameras[i];
				if (sceneCamera == null)
					continue;

				if (taggedMainCamera == null && sceneCamera.CompareTag("MainCamera") == true)
				{
					taggedMainCamera = sceneCamera;
				}

				CinemachineBrain sceneCameraBrain = sceneCamera.GetComponent<CinemachineBrain>();
				if (sceneCameraBrain == null)
					continue;

				if (sceneCameraBrain.ControlledObject == sceneCamera.gameObject)
				{
					if (sceneCamera.enabled == true)
						return sceneCamera;

					if (brainCamera == null)
					{
						brainCamera = sceneCamera;
					}
				}

				if (enabledBrainCamera == null && sceneCamera.enabled == true)
				{
					enabledBrainCamera = sceneCamera;
				}

				if (brainCamera == null)
				{
					brainCamera = sceneCamera;
				}
			}

			var sceneBrains = runnerScene.GetComponents<CinemachineBrain>(true);
			for (int i = 0; i < sceneBrains.Count; ++i)
			{
				CinemachineBrain sceneBrain = sceneBrains[i];
				if (sceneBrain == null)
					continue;

				Camera outputCamera = sceneBrain.OutputCamera;
				if (outputCamera != null)
				{
					if (outputCamera.enabled == true)
						return outputCamera;

					if (brainCamera == null)
					{
						brainCamera = outputCamera;
					}
				}

				GameObject controlledObject = sceneBrain.ControlledObject;
				if (controlledObject != null && controlledObject.TryGetComponent(out Camera controlledCamera) == true)
				{
					if (controlledCamera.enabled == true)
						return controlledCamera;

					if (brainCamera == null)
					{
						brainCamera = controlledCamera;
					}
				}
			}

			if (enabledBrainCamera != null)
				return enabledBrainCamera;
			if (brainCamera != null)
				return brainCamera;
			if (taggedMainCamera != null)
				return taggedMainCamera;

			return runnerScene.FindMainCamera();
		}

		private CinemachineBrain TryResolveBrainForCamera(Camera targetCamera)
		{
			if (targetCamera == null)
				return null;

			CinemachineBrain directBrain = targetCamera.GetComponent<CinemachineBrain>();
			if (directBrain != null)
				return directBrain;

			if (Context == null || Context.Runner == null)
				return null;

			var runnerScene = Context.Runner.SimulationUnityScene;
			if (runnerScene.IsValid() == false || runnerScene.isLoaded == false)
				return null;

			var sceneBrains = runnerScene.GetComponents<CinemachineBrain>(true);
			for (int i = 0; i < sceneBrains.Count; ++i)
			{
				CinemachineBrain sceneBrain = sceneBrains[i];
				if (sceneBrain == null)
					continue;

				if (sceneBrain.OutputCamera == targetCamera)
					return sceneBrain;

				GameObject controlledObject = sceneBrain.ControlledObject;
				if (controlledObject == targetCamera.gameObject)
					return sceneBrain;
			}

			return null;
		}

		private CinemachineBrain ResolveCinemachineBrain()
		{
			CinemachineBrain directBrain = _cinemachineBrain;
			if (directBrain != null)
				return directBrain;

			if (_camera != null)
			{
				CinemachineBrain linkedBrain = TryResolveBrainForCamera(_camera);
				if (linkedBrain != null)
					return linkedBrain;
			}

			Transform searchRoot = transform.parent != null ? transform.parent : transform;
			return searchRoot.GetComponentInChildren<CinemachineBrain>(true);
		}

		private void CachePostBlendCameraPose()
		{
			if (TryGetActiveCinemachinePose(out Vector3 position, out Quaternion rotation, out float fieldOfView, out _) == false)
			{
				Debug.LogError("[SceneCamera] CachePostBlendCameraPose failed: active Cinemachine pose is unavailable.", this);
				return;
			}

			_postBlendCameraPosition = position;
			_postBlendCameraRotation = rotation;
			_postBlendFieldOfView = fieldOfView;
		}

		private bool TryGetActiveCinemachinePose(out Vector3 position, out Quaternion rotation, out float fieldOfView, out string failureReason)
		{
			position = default;
			rotation = Quaternion.identity;
			fieldOfView = 60.0f;
			failureReason = default;

			ResolveCinemachineReferences();

			CinemachineBrain runtimeBrain = ResolveCinemachineBrain();
			if (runtimeBrain == null)
			{
				failureReason = "No CinemachineBrain found.";
				return false;
			}

			ICinemachineCamera activeVirtualCamera = runtimeBrain.ActiveVirtualCamera;
			if (activeVirtualCamera == null)
			{
				failureReason = "ActiveVirtualCamera is null.";
				return false;
			}

			CameraState state = activeVirtualCamera.State;
			position = state.GetFinalPosition();
			rotation = state.GetFinalOrientation();
			fieldOfView = state.Lens.FieldOfView;

			if (float.IsNaN(position.x) == true || float.IsNaN(position.y) == true || float.IsNaN(position.z) == true)
			{
				failureReason = "ActiveVirtualCamera state position is NaN.";
				return false;
			}
			if (float.IsNaN(rotation.x) == true || float.IsNaN(rotation.y) == true || float.IsNaN(rotation.z) == true || float.IsNaN(rotation.w) == true)
			{
				failureReason = "ActiveVirtualCamera state rotation is NaN.";
				return false;
			}
			if (float.IsNaN(fieldOfView) == true || fieldOfView <= 0.0f)
			{
				failureReason = $"ActiveVirtualCamera state FoV is invalid ({fieldOfView}).";
				return false;
			}

			return true;
		}

		private CinemachineCamera GetLiveCinemachineCamera()
		{
			CinemachineBrain runtimeBrain = ResolveCinemachineBrain();
			if (runtimeBrain == null)
				return _cinemachineCamera;

			if (runtimeBrain.ActiveVirtualCamera is CinemachineCameraManagerBase managerCamera)
				return managerCamera.LiveChild as CinemachineCamera;

			return runtimeBrain.ActiveVirtualCamera as CinemachineCamera ?? _cinemachineCamera;
		}

		private CinemachineCamera ResolveActiveCinemachineCamera()
		{
			CinemachineCamera activeCamera = GetLiveCinemachineCamera();
			if (activeCamera != null)
				return activeCamera;

			Transform searchRoot = transform.parent != null ? transform.parent : transform;
			return searchRoot.GetComponentInChildren<CinemachineCamera>(true);
		}

		private void EnsureImpulseListener(CinemachineCamera runtimeCamera)
		{
			if (runtimeCamera == null)
			{
				_impulseListener = null;
				return;
			}

			if (_impulseListener == null || _impulseListener.ComponentOwner != runtimeCamera)
			{
				_impulseListener = runtimeCamera.GetComponent<CinemachineImpulseListener>();
				if (_impulseListener == null)
				{
					_impulseListener = runtimeCamera.gameObject.AddComponent<CinemachineImpulseListener>();
				}
			}

			if (_impulseListener != null)
			{
				_impulseListener.ChannelMask = ~0;
				_impulseListener.Gain = 1.0f;
				_impulseListener.Use2DDistance = false;
				_impulseListener.UseCameraSpace = true;
				_impulseListener.ApplyAfter = CinemachineCore.Stage.Noise;
			}
		}

		public void SetFieldOfView(float fieldOfView)
		{
			CinemachineCamera runtimeCamera = ResolveActiveCinemachineCamera();
			if (runtimeCamera != null)
			{
				LensSettings lens = runtimeCamera.Lens;
				if (Mathf.Abs(lens.FieldOfView - fieldOfView) > 0.001f)
				{
					lens.FieldOfView = fieldOfView;
					runtimeCamera.Lens = lens;
				}
			}

			if (_camera != null && Mathf.Abs(_camera.fieldOfView - fieldOfView) > 0.001f)
			{
				_camera.fieldOfView = fieldOfView;
			}
		}

		public float GetFieldOfView()
		{
			CinemachineCamera runtimeCamera = ResolveActiveCinemachineCamera();
			if (runtimeCamera != null)
				return runtimeCamera.Lens.FieldOfView;

			if (_camera != null)
				return _camera.fieldOfView;

			return 60.0f;
		}

		public bool TryGetAimProxyTargets(out Vector3 origin, out Vector3 crosshair)
		{
			origin = default;
			crosshair = default;

			ResolveAimTargetGroupReference();
			ResolveAimProxyTargetReferences();

			if (_hasAimProxyData == false)
				return false;
			if (_aimOrigin == null || _aimTarget == null)
				return false;

			origin = _aimOrigin.position;
			crosshair = _aimTarget.position;
			return true;
		}

		protected override void OnDeinitialize()
		{
			base.OnDeinitialize();

			_aimGroup = null;
			_aimOrigin = null;
			_aimTarget = null;
			RestoreScopedLocalVisuals();
			_scopedVisualsAgent = null;
			_scopedVisuals = null;
			_rigStateAgent = null;
			_rigStateAnchor = null;
			_rigStateLeftSide = false;
			_hasRigState = false;
			_hasRigTargets = false;
			_targetShoulderX = 0.0f;
			_targetVerticalArmLength = 0.0f;
			_targetCameraDistance = 0.0f;
			_lastRigLerpFrame = -1;
			_hadInputLastTick = false;
			_hasAimProxyData = false;
			_lastCinemachineSyncFrame = -1;
			_isSyncingCinemachine = false;
			_postBlendCameraPosition = default;
			_postBlendCameraRotation = Quaternion.identity;
			_postBlendFieldOfView = 60.0f;
		}
	}
}
