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
		private const float EPSILON = 0.0001f;
		private const float MAX_VISION_SPOT_ANGLE = 177.0f;
		private const float BLEND_MAX_FOV_MULTIPLIER = 1.67f;
		private const int BLEND_COOKIE_SIZE = 128;
		private const string DEFAULT_OBJECT_LAYER_NAME = "Default";
		private const string HIDDEN_OBJECT_LAYER_NAME = "Hidden";

		private enum LookAtMode
		{
			Fixed,
			Dynamic,
			Hybrid,
			Blend,
			Procedural,
		}

		[Header("Vision FoV Mapping")]
		[SerializeField]
		[Tooltip("Maps camera FoV ratio to vision-cone FoV ratio. X = CurrentFoV / BaseFoV, Y = VisionFoV / BaseFoV.")]
		private AnimationCurve _visionFovRatioCurve = AnimationCurve.Linear(0.0f, 0.0f, 2.0f, 2.0f);
		[SerializeField]
		[Tooltip("Fixed uses fire transform forward. Dynamic uses fire-origin to fire-hit look-at. Hybrid blends fixed and dynamic directions. Blend computes an enclosing cone for fixed+dynamic. Procedural keeps both camera-hit and fire-hit directions inside the cone when possible, otherwise aims between them.")]
		private LookAtMode _lookAtMode = LookAtMode.Dynamic;
		[SerializeField]
		[Tooltip("Applies runtime ellipsoidal cookie shaping while in Blend mode.")]
		private bool _useEllipsoidalCookie = true;
		private float _hybridResponseSpeed = 8.0f;

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
		private float _baseMappedViewAngle = 60.0f;
		private float _initialInnerOuterDelta;
		private bool _hasCachedInitialAngles;
		private bool _didLogMissingAgent;
		private bool _didLogMissingCullingLayers;
		private bool _didLogMissingAimRay;
		private bool _hasHybridState;
		private Quaternion _hybridSmoothedRotation = Quaternion.identity;
		private float _hybridSmoothedAspect = 1.0f;
		private bool _hasBlendState;
		private Vector3 _blendSmoothedDynamicDirection = Vector3.forward;
		private float _blendAimPriority;
		private Texture2D _blendCookieTexture;
		private Color32[] _blendCookiePixels;
		private float _lastBlendCookieMinorAxis = -1.0f;

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
				_hasHybridState = false;
				_hybridSmoothedAspect = 1.0f;
				_hasBlendState = false;
				_blendAimPriority = 0.0f;
				ClearBlendCookie();
				if (_blendCookieTexture != null)
				{
				if (Application.isPlaying)
				{
					Destroy(_blendCookieTexture);
				}
				else
				{
					DestroyImmediate(_blendCookieTexture);
				}

				_blendCookieTexture = null;
				_blendCookiePixels = null;
				_lastBlendCookieMinorAxis = -1.0f;
			}
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

			int defaultLayer = LayerMask.NameToLayer(DEFAULT_OBJECT_LAYER_NAME);
			int hiddenLayer = LayerMask.NameToLayer(HIDDEN_OBJECT_LAYER_NAME);

			int cullingMask = 0;
			if (defaultLayer >= 0)
			{
				cullingMask |= 1 << defaultLayer;
			}
			if (hiddenLayer >= 0)
			{
				cullingMask |= 1 << hiddenLayer;
			}

			if (cullingMask != 0)
			{
				int constrainedMask = _spotLight.cullingMask & cullingMask;
				if (constrainedMask == 0)
				{
					// Safety fallback if current mask doesn't overlap allowed layers.
					constrainedMask = cullingMask;
				}

				_spotLight.cullingMask = constrainedMask;
				return;
			}

			if (_didLogMissingCullingLayers == false)
			{
				Debug.LogWarning($"[AgentVision] Could not resolve object layers '{DEFAULT_OBJECT_LAYER_NAME}'/'{HIDDEN_OBJECT_LAYER_NAME}'. Vision light culling mask was not constrained.", this);
				_didLogMissingCullingLayers = true;
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

			float defaultFov = Mathf.Max(1.0f, _character.BaseFOV);
			float currentFov = Mathf.Max(1.0f, _character.CurrentFOV);
			float inputRatio = currentFov / defaultFov;
			float mappedRatio = _visionFovRatioCurve != null ? _visionFovRatioCurve.Evaluate(inputRatio) : inputRatio;
			if (float.IsNaN(mappedRatio) == true || float.IsInfinity(mappedRatio) == true)
			{
				mappedRatio = inputRatio;
			}

			float mappedFov = defaultFov * Mathf.Max(0.01f, mappedRatio);
			float viewAngle = Mathf.Clamp(mappedFov, 1.0f, MAX_VISION_SPOT_ANGLE);
			_baseMappedViewAngle = viewAngle;
			if (Mathf.Abs(_lastAppliedViewAngle - viewAngle) <= 0.001f)
				return;

			_lastAppliedViewAngle = viewAngle;
			ApplySpotLightAngles(viewAngle);
		}

		private void ApplySpotLightAngles(float spotAngle)
		{
			if (_spotLight == null)
				return;

			float clampedSpotAngle = Mathf.Clamp(spotAngle, 1.0f, MAX_VISION_SPOT_ANGLE);
			if (Mathf.Abs(_spotLight.spotAngle - clampedSpotAngle) > 0.001f)
			{
				_spotLight.spotAngle = clampedSpotAngle;
			}

			float targetInnerAngle = Mathf.Max(0.0f, clampedSpotAngle - _initialInnerOuterDelta);
			if (Mathf.Abs(_spotLight.innerSpotAngle - targetInnerAngle) > 0.001f)
			{
				_spotLight.innerSpotAngle = targetInnerAngle;
			}
		}

		private void ApplyHybridSpotlightFovCompensation(float hybridAspect)
		{
			float baseViewAngle = _lastAppliedViewAngle > 0.0f ? _lastAppliedViewAngle : _baseMappedViewAngle;
			baseViewAngle = Mathf.Clamp(baseViewAngle, 1.0f, MAX_VISION_SPOT_ANGLE);

			float compensation = 1.0f;
			if (compensation <= 0.0001f)
			{
				ApplySpotLightAngles(baseViewAngle);
				return;
			}

			float clampedAspect = Mathf.Max(1.0f, hybridAspect);
			float tanHalfBase = Mathf.Tan(baseViewAngle * Mathf.Deg2Rad * 0.5f);
			if (tanHalfBase <= EPSILON || float.IsNaN(tanHalfBase))
			{
				ApplySpotLightAngles(baseViewAngle);
				return;
			}

			float fullCompensatedScale = 1.0f / Mathf.Sqrt(clampedAspect);
			float tanHalfScale = Mathf.Lerp(1.0f, fullCompensatedScale, compensation);
			float compensatedTanHalf = tanHalfBase * tanHalfScale;
			float compensatedViewAngle = Mathf.Atan(compensatedTanHalf) * Mathf.Rad2Deg * 2.0f;
			if (float.IsNaN(compensatedViewAngle) || float.IsInfinity(compensatedViewAngle))
			{
				compensatedViewAngle = baseViewAngle;
			}

			ApplySpotLightAngles(compensatedViewAngle);
		}

		private float GetUnifiedLerpFactor()
		{
			float dt = Mathf.Max(0.0f, Time.unscaledDeltaTime);
			float response = Mathf.Max(0.0f, _hybridResponseSpeed);
			return response <= EPSILON ? 1.0f : (dt > 0.0f ? 1.0f - Mathf.Exp(-response * dt) : 1.0f);
		}

		private static void BuildEnclosingCone(Vector3 fixedDirection, float fixedFovDeg, Vector3 dynamicDirection, float dynamicFovDeg, out Vector3 axis, out float fovDeg)
		{
			if (fixedDirection.sqrMagnitude <= EPSILON)
				fixedDirection = Vector3.forward;
			if (dynamicDirection.sqrMagnitude <= EPSILON)
				dynamicDirection = fixedDirection;

			fixedDirection.Normalize();
			dynamicDirection.Normalize();

			float fixedHalf = Mathf.Clamp(fixedFovDeg, 1.0f, MAX_VISION_SPOT_ANGLE) * Mathf.Deg2Rad * 0.5f;
			float dynamicHalf = Mathf.Clamp(dynamicFovDeg, 1.0f, MAX_VISION_SPOT_ANGLE) * Mathf.Deg2Rad * 0.5f;
			float dot = Mathf.Clamp(Vector3.Dot(fixedDirection, dynamicDirection), -1.0f, 1.0f);
			float separation = Mathf.Acos(dot);

			if (fixedHalf >= separation + dynamicHalf - EPSILON)
			{
				axis = fixedDirection;
				fovDeg = Mathf.Clamp(fixedFovDeg, 1.0f, MAX_VISION_SPOT_ANGLE);
				return;
			}

			if (dynamicHalf >= separation + fixedHalf - EPSILON)
			{
				axis = dynamicDirection;
				fovDeg = Mathf.Clamp(dynamicFovDeg, 1.0f, MAX_VISION_SPOT_ANGLE);
				return;
			}

			float enclosingHalf = 0.5f * (separation + fixedHalf + dynamicHalf);
			float t = separation > EPSILON ? Mathf.Clamp01((enclosingHalf - fixedHalf) / separation) : 0.0f;

			axis = Vector3.Slerp(fixedDirection, dynamicDirection, t);
			if (axis.sqrMagnitude <= EPSILON)
			{
				axis = fixedDirection;
			}
			axis.Normalize();

			fovDeg = Mathf.Clamp(enclosingHalf * Mathf.Rad2Deg * 2.0f, 1.0f, MAX_VISION_SPOT_ANGLE);
		}

		private void EnsureBlendCookieTexture()
		{
			if (_blendCookieTexture != null)
				return;

			_blendCookieTexture = new Texture2D(BLEND_COOKIE_SIZE, BLEND_COOKIE_SIZE, TextureFormat.RGBA32, true, true)
			{
				name = "VisionBlendCookieRuntime",
				filterMode = FilterMode.Trilinear,
				wrapMode = TextureWrapMode.Clamp
			};
			_blendCookieTexture.anisoLevel = 4;
			_blendCookiePixels = new Color32[BLEND_COOKIE_SIZE * BLEND_COOKIE_SIZE];
			_lastBlendCookieMinorAxis = -1.0f;
		}

		private void ClearBlendCookie()
		{
			if (_spotLight != null && _blendCookieTexture != null && _spotLight.cookie == _blendCookieTexture)
			{
				_spotLight.cookie = null;
			}
		}

		private void ApplyBlendCookie(float minorAxis)
		{
			if (_spotLight == null)
				return;

			minorAxis = Mathf.Clamp(minorAxis, 0.01f, 1.0f);
			EnsureBlendCookieTexture();
			if (_blendCookieTexture == null || _blendCookiePixels == null)
				return;

			if (Mathf.Abs(_lastBlendCookieMinorAxis - minorAxis) > 0.0025f)
			{
				float invMinorAxis = 1.0f / Mathf.Max(minorAxis, 0.0001f);
				float solidCoreRadius = 0.96f;
				float feather = 0.03f;
				float featherEnd = solidCoreRadius + feather;

				for (int y = 0; y < BLEND_COOKIE_SIZE; ++y)
				{
					float v = ((y + 0.5f) / BLEND_COOKIE_SIZE) * 2.0f - 1.0f;

					for (int x = 0; x < BLEND_COOKIE_SIZE; ++x)
					{
						float u = ((x + 0.5f) / BLEND_COOKIE_SIZE) * 2.0f - 1.0f;
						float oy = v * invMinorAxis;
						float distance = Mathf.Sqrt(u * u + oy * oy);
						float mask = distance <= solidCoreRadius
							? 1.0f
							: 1.0f - Mathf.SmoothStep(solidCoreRadius, featherEnd, distance);

						byte v8 = (byte)Mathf.RoundToInt(Mathf.Clamp01(mask) * 255.0f);
						_blendCookiePixels[y * BLEND_COOKIE_SIZE + x] = new Color32(v8, v8, v8, 255);
					}
				}

				_blendCookieTexture.SetPixels32(_blendCookiePixels);
				_blendCookieTexture.Apply(true, false);
				_lastBlendCookieMinorAxis = minorAxis;
			}

			if (_spotLight.cookie != _blendCookieTexture)
			{
				_spotLight.cookie = _blendCookieTexture;
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
			return Agent.IsLocalObservedInputOwner(_agent);
		}

		private void SyncSpotLightPose(bool isLocalControlled)
		{
			if (isLocalControlled == false || _spotLight == null || _character == null)
				return;

			TransformData fireTransform = _character.GetFireTransform(false);
			Vector3 firePosition = fireTransform.Position;
			Quaternion targetRotation = fireTransform.Rotation;
			Vector3 fixedDirection = fireTransform.Rotation * Vector3.forward;
			if (fixedDirection.sqrMagnitude <= EPSILON)
			{
				fixedDirection = transform.forward;
			}
			if (fixedDirection.sqrMagnitude <= EPSILON)
			{
				fixedDirection = Vector3.forward;
			}
			fixedDirection.Normalize();

			if (_lookAtMode != LookAtMode.Fixed)
			{
				const bool resolveRenderHistory = true;
				if (_agent == null || _agent.Aiming == null ||
					_agent.Aiming.TryGetCrosshairAndHitPoints(resolveRenderHistory, out Vector3 aimFireOrigin, out Vector3 cameraHitPoint, out Vector3 characterHitPoint, out _) == false)
				{
					if (_didLogMissingAimRay == false && ShouldLogAimResolveFailure(_agent != null ? _agent.Aiming : null, resolveRenderHistory) == true)
					{
						Debug.LogWarning("[AgentVision] Failed to resolve deterministic crosshair hit point. Vision cone pose update skipped.", this);
						_didLogMissingAimRay = true;
					}
					return;
				}

				firePosition = aimFireOrigin;
				Vector3 cameraPosition;
				Quaternion cameraRotation;
				if (TryGetObservedCameraPose(out cameraPosition, out cameraRotation) == false)
				{
					TransformData cameraTransform = _character.GetCameraTransform(false);
					cameraPosition = cameraTransform.Position;
					cameraRotation = cameraTransform.Rotation;
				}

				Vector3 cameraUp = cameraRotation * Vector3.up;
				if (cameraUp.sqrMagnitude <= EPSILON)
				{
					cameraUp = Vector3.up;
				}

				Vector3 dynamicDirection;
				if (_lookAtMode == LookAtMode.Dynamic)
				{
					ClearBlendCookie();
					dynamicDirection = characterHitPoint - firePosition;
					if (dynamicDirection.sqrMagnitude <= EPSILON)
						return;
					dynamicDirection.Normalize();

					targetRotation = Quaternion.LookRotation(dynamicDirection, cameraUp);
					_hasHybridState = false;
					_hybridSmoothedAspect = 1.0f;
					ApplySpotLightAngles(_baseMappedViewAngle);
				}
				else if (_lookAtMode == LookAtMode.Hybrid)
				{
					ClearBlendCookie();
					// Hybrid should blend fixed forward with the same dynamic target used by gameplay/undesired marker.
					dynamicDirection = characterHitPoint - firePosition;
					if (dynamicDirection.sqrMagnitude <= EPSILON)
					{
						dynamicDirection = cameraHitPoint - cameraPosition;
					}
					if (dynamicDirection.sqrMagnitude <= EPSILON)
					{
						dynamicDirection = cameraRotation * Vector3.forward;
					}
					if (dynamicDirection.sqrMagnitude <= EPSILON)
						return;
					dynamicDirection.Normalize();

					Vector3 centerDirection = fixedDirection + dynamicDirection;
					if (centerDirection.sqrMagnitude <= EPSILON)
					{
						centerDirection = fixedDirection;
					}
					centerDirection.Normalize();

					Vector3 splitVector = Vector3.ProjectOnPlane(dynamicDirection - fixedDirection, centerDirection);
					Vector3 majorAxis = splitVector.sqrMagnitude > EPSILON ? splitVector.normalized : Vector3.ProjectOnPlane(dynamicDirection, centerDirection);
					if (majorAxis.sqrMagnitude <= EPSILON)
					{
						majorAxis = Vector3.Cross(cameraUp, centerDirection);
					}
					if (majorAxis.sqrMagnitude <= EPSILON)
					{
						majorAxis = Vector3.Cross(Vector3.up, centerDirection);
					}
					if (majorAxis.sqrMagnitude <= EPSILON)
					{
						majorAxis = Vector3.right;
					}
					majorAxis.Normalize();

					Vector3 upAxis = Vector3.Cross(centerDirection, majorAxis);
					if (upAxis.sqrMagnitude <= EPSILON)
					{
						upAxis = cameraUp;
					}
					if (upAxis.sqrMagnitude <= EPSILON)
					{
						upAxis = Vector3.up;
					}
					upAxis.Normalize();

					float separationAngle = Vector3.Angle(fixedDirection, dynamicDirection);
					float maxSeparation = 25.0f;
					float separation01 = Mathf.Clamp01(separationAngle / maxSeparation);
					separation01 = separation01 * separation01 * (3.0f - 2.0f * separation01);
					float targetAspect = Mathf.Lerp(1.0f, 1.75f, separation01);

					Quaternion hybridTargetRotation = Quaternion.LookRotation(centerDirection, upAxis);
					float lerpFactor = GetUnifiedLerpFactor();

					if (_hasHybridState == false)
					{
						_hybridSmoothedRotation = hybridTargetRotation;
						_hybridSmoothedAspect = targetAspect;
						_hasHybridState = true;
					}
					else
					{
						_hybridSmoothedRotation = Quaternion.Slerp(_hybridSmoothedRotation, hybridTargetRotation, lerpFactor);
						_hybridSmoothedAspect = Mathf.Lerp(_hybridSmoothedAspect, targetAspect, lerpFactor);
					}

					targetRotation = _hybridSmoothedRotation;
					ApplySpotLightAngles(_baseMappedViewAngle);
				}
					else if (_lookAtMode == LookAtMode.Blend)
					{
					Vector3 rawDynamicDirection = characterHitPoint - firePosition;
					if (rawDynamicDirection.sqrMagnitude <= EPSILON)
					{
						rawDynamicDirection = cameraHitPoint - cameraPosition;
					}
					if (rawDynamicDirection.sqrMagnitude <= EPSILON)
					{
						rawDynamicDirection = cameraRotation * Vector3.forward;
					}
					if (rawDynamicDirection.sqrMagnitude <= EPSILON)
						return;
					rawDynamicDirection.Normalize();

					float lerpFactor = GetUnifiedLerpFactor();
					if (_hasBlendState == false)
					{
						_blendSmoothedDynamicDirection = rawDynamicDirection;
						_hasBlendState = true;
					}
					else
					{
						_blendSmoothedDynamicDirection = Vector3.Slerp(_blendSmoothedDynamicDirection, rawDynamicDirection, lerpFactor);
						if (_blendSmoothedDynamicDirection.sqrMagnitude > EPSILON)
						{
							_blendSmoothedDynamicDirection.Normalize();
						}
						else
						{
							_blendSmoothedDynamicDirection = rawDynamicDirection;
						}
					}

					float baseViewAngle = _lastAppliedViewAngle > 0.0f ? _lastAppliedViewAngle : _baseMappedViewAngle;
					baseViewAngle = Mathf.Clamp(baseViewAngle, 1.0f, MAX_VISION_SPOT_ANGLE);

						BuildEnclosingCone(fixedDirection, baseViewAngle, _blendSmoothedDynamicDirection, baseViewAngle, out Vector3 blendDirection, out float blendViewAngle);

						float blendMaxViewAngle = Mathf.Min(MAX_VISION_SPOT_ANGLE, Mathf.Max(1.0f, baseViewAngle * BLEND_MAX_FOV_MULTIPLIER));
						blendViewAngle = Mathf.Clamp(blendViewAngle, 1.0f, blendMaxViewAngle);
						bool isAiming = IsAgentAiming();
						float targetAimPriority = isAiming == true ? 1.0f : 0.0f;
						_blendAimPriority = Mathf.Lerp(_blendAimPriority, targetAimPriority, lerpFactor);
						_blendAimPriority = Mathf.Clamp01(_blendAimPriority);

						Vector3 blendMajorAxis = Vector3.ProjectOnPlane(_blendSmoothedDynamicDirection - fixedDirection, blendDirection);
						if (blendMajorAxis.sqrMagnitude <= EPSILON)
						{
							blendMajorAxis = Vector3.ProjectOnPlane(cameraRotation * Vector3.right, blendDirection);
					}
					if (blendMajorAxis.sqrMagnitude <= EPSILON)
					{
						blendMajorAxis = Vector3.Cross(cameraUp, blendDirection);
					}
					if (blendMajorAxis.sqrMagnitude <= EPSILON)
					{
						blendMajorAxis = Vector3.right;
					}
					blendMajorAxis.Normalize();

					Vector3 blendUp = Vector3.Cross(blendDirection, blendMajorAxis);
					if (blendUp.sqrMagnitude <= EPSILON)
					{
						blendUp = cameraUp;
					}
					if (blendUp.sqrMagnitude <= EPSILON)
					{
						blendUp = Vector3.up;
						}
						blendUp.Normalize();

						Vector3 prioritizedDirection = Vector3.Slerp(blendDirection, _blendSmoothedDynamicDirection, _blendAimPriority);
						if (prioritizedDirection.sqrMagnitude <= EPSILON)
						{
							prioritizedDirection = blendDirection;
						}
						prioritizedDirection.Normalize();

						targetRotation = Quaternion.LookRotation(prioritizedDirection, blendUp);
						_hasHybridState = false;
						_hybridSmoothedAspect = 1.0f;
						float prioritizedViewAngle = Mathf.Lerp(blendViewAngle, baseViewAngle, _blendAimPriority);
						ApplySpotLightAngles(prioritizedViewAngle);
						if (_useEllipsoidalCookie == true)
						{
							float minorAxis = Mathf.Clamp01(baseViewAngle / Mathf.Max(baseViewAngle, prioritizedViewAngle));
							ApplyBlendCookie(minorAxis);
						}
						else
						{
						ClearBlendCookie();
					}
				}
					else
					{
						ClearBlendCookie();
						_hasBlendState = false;
						_blendAimPriority = 0.0f;
						Vector3 crosshairWorldDirection = characterHitPoint - firePosition;
					if (crosshairWorldDirection.sqrMagnitude <= EPSILON)
					{
						crosshairWorldDirection = fixedDirection;
					}
					if (crosshairWorldDirection.sqrMagnitude <= EPSILON)
					{
						crosshairWorldDirection = cameraRotation * Vector3.forward;
					}
					if (crosshairWorldDirection.sqrMagnitude <= EPSILON)
						return;
					crosshairWorldDirection.Normalize();

					Vector3 screenHitDirection = cameraHitPoint - firePosition;
					if (screenHitDirection.sqrMagnitude <= EPSILON)
					{
						screenHitDirection = cameraRotation * Vector3.forward;
					}
					if (screenHitDirection.sqrMagnitude <= EPSILON)
					{
						screenHitDirection = crosshairWorldDirection;
					}
					screenHitDirection.Normalize();

					float viewAngle = _spotLight != null && _spotLight.spotAngle > 0.0f ? _spotLight.spotAngle : _baseMappedViewAngle;
					float halfConeAngle = Mathf.Clamp(viewAngle, 1.0f, 179.0f) * 0.5f;

					// Prefer gameplay-authoritative direction if it still keeps both points inside the cone.
					Vector3 proceduralDirection = crosshairWorldDirection;
					bool canContainBothFromWorldDirection = Vector3.Angle(proceduralDirection, screenHitDirection) <= halfConeAngle;
					if (canContainBothFromWorldDirection == false)
					{
						// If both cannot be contained from authoritative direction, point directly between them.
						Vector3 betweenDirection = crosshairWorldDirection + screenHitDirection;
						if (betweenDirection.sqrMagnitude <= EPSILON)
						{
							betweenDirection = crosshairWorldDirection;
						}
						proceduralDirection = betweenDirection.normalized;
					}

					targetRotation = Quaternion.LookRotation(proceduralDirection, cameraUp);
					_hasHybridState = false;
					_hybridSmoothedAspect = 1.0f;
					ApplySpotLightAngles(_baseMappedViewAngle);
				}
			}
			else
			{
				ClearBlendCookie();
				_hasHybridState = false;
				_hybridSmoothedAspect = 1.0f;
				_hasBlendState = false;
				_blendAimPriority = 0.0f;
				ApplySpotLightAngles(_baseMappedViewAngle);
			}
			if (Vector3.Distance(_spotLight.transform.position, firePosition) > 0.0001f)
			{
				_spotLight.transform.position = firePosition;
			}
			if (Quaternion.Angle(_spotLight.transform.rotation, targetRotation) > 0.01f)
			{
				_spotLight.transform.rotation = targetRotation;
			}
			
			_didLogMissingAimRay = false;
		}

		private bool ShouldLogAimResolveFailure(Aiming aiming, bool resolveRenderHistory)
		{
			if (aiming == null || _agent == null)
				return false;
			if (_agent.Object == null || _agent.Runner == null || _agent.Object.IsValid == false || _agent.Object.IsInSimulation == false)
				return false;
			if (aiming.HasDeterministicLookAtSource(resolveRenderHistory) == false)
				return false;

			return true;
		}

		private bool IsAgentAiming()
		{
			if (_character == null || _character.CharacterController == null)
				return false;

			return _character.CharacterController.Data.Aim == true;
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
