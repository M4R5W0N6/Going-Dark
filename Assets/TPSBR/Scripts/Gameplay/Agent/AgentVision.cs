using Fusion;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace TPSBR
{
	[AddComponentMenu("GOING DARK/Agent Vision")]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Light))]
	public sealed class AgentVision : MonoBehaviour
	{
		private const float TPSBR_TARGET_RAY_DISTANCE = 500.0f;
		private const string LOCAL_OBJECT_LAYER_NAME = "Local";

		public uint VisionLightLayerMask
		{
			get
			{
				if (_spotLight == null)
					_spotLight = GetComponent<Light>();
				if (_spotLightData == null && _spotLight != null)
					_spotLightData = _spotLight.GetComponent<HDAdditionalLightData>();

				if (_spotLightData != null)
					return (uint)_spotLightData.lightlayersMask;

				if (_spotLight != null)
					return unchecked((uint)_spotLight.renderingLayerMask);

				return 1u;
			}
		}

		private Agent _agent;
		private Character _character;
		private Light _spotLight;
		private HDAdditionalLightData _spotLightData;
		private bool _hasAppliedState;
		private bool _lastLightEnabled;
		private float _lastAppliedViewAngle = -1.0f;
		private float _initialInnerOuterDelta;
		private bool _hasCachedInitialAngles;
		private bool _didLogMissingAgent;
		private bool _didLogMissingLocalLayer;

		private void Awake()
		{
			Initialize();
		}

		private void OnEnable()
		{
			Initialize();
			if (enabled == false)
				return;

			_hasAppliedState = false;
			_lastLightEnabled = false;

			if (Application.isPlaying == true && _spotLight != null)
			{
				// Keep it off until authority/state is resolved for this instance.
				_spotLight.enabled = false;
			}

			ApplyState(force: true);
		}

		private void LateUpdate()
		{
			if (EnsureReady() == false)
				return;

			ApplyLocalLayerCullingMask();
			SyncSpotLightViewAngle();
			ApplyState(force: false);
		}

		private void OnDisable()
		{
			_hasAppliedState = false;
			_lastLightEnabled = false;
			if (_spotLight != null)
			{
				_spotLight.enabled = false;
			}
		}

		private void Initialize()
		{
			if (_spotLight == null)
				_spotLight = GetComponent<Light>();
			if (_spotLightData == null && _spotLight != null)
				_spotLightData = _spotLight.GetComponent<HDAdditionalLightData>();
			if (_agent == null)
				_agent = GetComponentInParent<Agent>();
			if (_character == null && _agent != null)
				_character = _agent.Character;
			ApplyLocalLayerCullingMask();

			CacheInitialSpotlightAngles();
			EnsureReady();
		}

		private void ApplyLocalLayerCullingMask()
		{
			if (_spotLight == null)
				return;

			int localLayer = LayerMask.NameToLayer(LOCAL_OBJECT_LAYER_NAME);
			if (localLayer >= 0)
			{
				_spotLight.cullingMask &= ~(1 << localLayer);
				return;
			}

			if (_didLogMissingLocalLayer == false)
			{
				Debug.LogWarning($"[AgentVision] Object layer '{LOCAL_OBJECT_LAYER_NAME}' not found. Vision light will not exclude local visuals by culling mask.", this);
				_didLogMissingLocalLayer = true;
			}
		}

		private bool EnsureReady()
		{
			if (_spotLight == null)
			{
				Debug.LogWarning("[AgentVision] No Light found on this GameObject.", this);
				enabled = false;
				return false;
			}

			if (_agent == null)
			{
				if (_didLogMissingAgent == false)
				{
					Debug.LogWarning("[AgentVision] No Agent found in parent hierarchy. Disabling.", this);
					_didLogMissingAgent = true;
				}

				enabled = false;
				return false;
			}

			return true;
		}

		private void CacheInitialSpotlightAngles()
		{
			if (_spotLight == null || _hasCachedInitialAngles == true)
				return;

			_initialInnerOuterDelta = Mathf.Max(0.0f, _spotLight.spotAngle - _spotLight.innerSpotAngle);
			_hasCachedInitialAngles = true;
		}

		private void SyncSpotLightViewAngle()
		{
			if (_character == null || _spotLight == null)
				return;
			if (_hasCachedInitialAngles == false)
				return;

			float viewAngle = _character.CurrentFOV;
			if (Mathf.Abs(_lastAppliedViewAngle - viewAngle) <= 0.001f)
				return;

			_lastAppliedViewAngle = viewAngle;

			if (Mathf.Abs(_spotLight.spotAngle - viewAngle) > 0.001f)
			{
				_spotLight.spotAngle = viewAngle;
				_spotLight.innerSpotAngle = Mathf.Max(0.0f, viewAngle - _initialInnerOuterDelta);
			}
		}

		private void ApplyState(bool force)
		{
			if (Application.isPlaying == false || _agent == null || _spotLight == null)
				return;

			bool isLocalControlled = IsLocalVisionOwner();
			bool lightEnabled = isLocalControlled;

			SyncSpotLightPose(isLocalControlled);

			if (force == false && _hasAppliedState && _lastLightEnabled == lightEnabled)
				return;

			_hasAppliedState = true;
			_lastLightEnabled = lightEnabled;
			_spotLight.enabled = lightEnabled;
		}

		private bool IsLocalVisionOwner()
		{
			if (_agent == null || _agent.HasInputAuthority == false)
				return false;

			SceneContext context = _agent.Context;
			if (context == null || context.HasInput == false)
				return false;

			Agent observedAgent = context.ObservedAgent;
			if (observedAgent != null)
				return observedAgent == _agent;

			// Observed agent may be briefly unset during handoff/respawn.
			// Use local active agent as a strict fallback, still scoped to this local context.
			if (context.NetworkGame != null && context.LocalPlayerRef.IsRealPlayer == true)
			{
				Player localPlayer = context.NetworkGame.GetPlayer(context.LocalPlayerRef);
				if (localPlayer != null && localPlayer.ActiveAgent != null)
				{
					return localPlayer.ActiveAgent == _agent;
				}
			}

			return false;
		}

		private void SyncSpotLightPose(bool isLocalControlled)
		{
			if (isLocalControlled == false || _spotLight == null || _character == null)
				return;

			TransformData fireTransform = _character.GetFireTransform(false);
			Vector3 cameraPosition;
			Quaternion cameraRotation;
			if (TryGetObservedCameraPose(out cameraPosition, out cameraRotation) == false)
			{
				TransformData cameraTransform = _character.GetCameraTransform(false);
				cameraPosition = cameraTransform.Position;
				cameraRotation = cameraTransform.Rotation;
			}

			Vector3 targetPoint = fireTransform.Position + transform.forward * TPSBR_TARGET_RAY_DISTANCE;
			if (_agent != null && _agent.Aiming != null)
			{
				if (_agent.Aiming.TryGetTargetPosition(false, out _, out Vector3 targetPosition) == true)
				{
					targetPoint = targetPosition;
				}
				else
				{
					_agent.Aiming.GetAimPose(false, out _, out targetPoint);
				}
			}

			Vector3 direction = targetPoint - fireTransform.Position;
			if (direction.sqrMagnitude <= 0.0001f)
				return;

			Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, cameraRotation * Vector3.up);
			if (Vector3.Distance(_spotLight.transform.position, fireTransform.Position) > 0.0001f)
			{
				_spotLight.transform.position = fireTransform.Position;
			}
			if (Quaternion.Angle(_spotLight.transform.rotation, targetRotation) > 0.01f)
			{
				_spotLight.transform.rotation = targetRotation;
			}
		}

		private bool TryGetObservedCameraPose(out Vector3 position, out Quaternion rotation)
		{
			position = default;
			rotation = default;

			if (_character == null)
				return false;

			TransformData fallbackCamera = _character.GetCameraTransform(false);
			SceneContext sceneContext = _agent != null ? _agent.Context : null;
			bool hasLocalInputAuthority = _character.HasInputAuthority == true &&
				sceneContext != null &&
				sceneContext.HasInput == true &&
				sceneContext.Camera != null &&
				IsLocalVisionOwner() == true;

			if (hasLocalInputAuthority == true)
			{
				sceneContext.Camera.SyncForGameplayRender();
				sceneContext.Camera.GetPostBlendCameraPose(out Vector3 postBlendPosition, out Quaternion postBlendRotation, out _);
				position = postBlendPosition;
				rotation = postBlendRotation;

				bool invalidLocalPosition =
					float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z);
				bool invalidLocalRotation =
					float.IsNaN(rotation.x) || float.IsNaN(rotation.y) || float.IsNaN(rotation.z) || float.IsNaN(rotation.w);
				if (invalidLocalPosition == false && invalidLocalRotation == false && rotation != default)
					return true;

				return false;
			}

			position = fallbackCamera.Position;
			rotation = fallbackCamera.Rotation;

			bool invalidPosition = float.IsNaN(position.x) || float.IsNaN(position.y) || float.IsNaN(position.z);
			bool invalidRotation = float.IsNaN(rotation.x) || float.IsNaN(rotation.y) || float.IsNaN(rotation.z) || float.IsNaN(rotation.w);
			if (invalidPosition == true || invalidRotation == true || rotation == default)
				return false;

			return true;
		}
	}
}
