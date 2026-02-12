using Fusion;
using UnityEngine;
using UnityEngine.Rendering.HighDefinition;

namespace TPSBR
{
	[AddComponentMenu("GOING DARK/Agent Sight")]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Light))]
	public sealed class AgentSight : MonoBehaviour
	{
		private const float TPSBR_TARGET_RAY_DISTANCE = 500.0f;

		public uint SightLightLayerMask
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

		private void Awake()
		{
			Initialize();
		}

		private void OnEnable()
		{
			Initialize();
			if (enabled == false)
				return;

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

			CacheInitialSpotlightAngles();
			EnsureReady();
		}

		private bool EnsureReady()
		{
			if (_spotLight == null)
			{
				Debug.LogWarning("[AgentSight] No Light found on this GameObject.", this);
				enabled = false;
				return false;
			}

			if (_agent == null)
			{
				if (_didLogMissingAgent == false)
				{
					Debug.LogWarning("[AgentSight] No Agent found in parent hierarchy. Disabling.", this);
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

			SceneContext context = _agent.Context;
			bool isLocalControlled = context != null ? (_agent.HasInputAuthority && context.HasInput) : _agent.HasInputAuthority;
			bool lightEnabled = isLocalControlled;

			SyncSpotLightPose(isLocalControlled);

			if (force == false && _hasAppliedState && _lastLightEnabled == lightEnabled)
				return;

			_hasAppliedState = true;
			_lastLightEnabled = lightEnabled;
			_spotLight.enabled = lightEnabled;
		}

		private void SyncSpotLightPose(bool isLocalControlled)
		{
			if (isLocalControlled == false || _spotLight == null || _character == null)
				return;

			TransformData fireTransform = _character.GetFireTransform(false);
			TransformData cameraTransform = _character.GetCameraTransform(false);
			Vector3 cameraForward = cameraTransform.Rotation * Vector3.forward;
			Vector3 targetPoint = cameraTransform.Position + cameraForward * TPSBR_TARGET_RAY_DISTANCE;

			bool isScopedSniper = _character.CharacterController != null
				&& _character.CharacterController.Data.Aim == true
				&& _agent != null
				&& _agent.Weapons != null
				&& _agent.Weapons.CurrentWeapon != null
				&& _agent.Weapons.CurrentWeapon.HitType == EHitType.Sniper;

			if (isScopedSniper == true && _agent.Interactions != null)
			{
				targetPoint = _agent.Interactions.GetTargetPoint(false, false);
			}

			Vector3 direction = targetPoint - fireTransform.Position;
			if (direction.sqrMagnitude <= 0.0001f)
				return;

			Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, cameraTransform.Rotation * Vector3.up);
			if (Vector3.Distance(_spotLight.transform.position, fireTransform.Position) > 0.0001f)
			{
				_spotLight.transform.position = fireTransform.Position;
			}
			if (Quaternion.Angle(_spotLight.transform.rotation, targetRotation) > 0.01f)
			{
				_spotLight.transform.rotation = targetRotation;
			}
		}
	}
}
