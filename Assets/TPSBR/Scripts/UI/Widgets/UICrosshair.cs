using DG.Tweening;
using UnityEngine;
using TMPro;

namespace TPSBR.UI
{
	public class UICrosshair : UIWidget
	{
		// PRIVATE MEMBERS

		[SerializeField]
		private RectTransform _resizingGroup;
		[SerializeField]
		private RectTransform _crosshairWorldRoot;
		[SerializeField]
		private CanvasGroup _crosshairRootGroup;
		[SerializeField]
		private CanvasGroup _unarmedGroup;
		[SerializeField]
		private CanvasGroup _armedGroup;
		[SerializeField]
		private GameObject _sniperScope;
		[SerializeField]
		private UIBehaviour _undesiredFirePosition;
		[SerializeField]
		private float _undesiredFireCrosshairAlpha = 0.6f;

		[Header("Audio")]
		[SerializeField]
		private AudioSetup _hitPerformed;
		[SerializeField]
		private AudioSetup _criticalHitPerformed;

		[Header("Hit Feedback")]
		[SerializeField]
		private CanvasGroup _hitGroup;
		[SerializeField]
		private CanvasGroup _criticalHitGroup;
		[SerializeField]
		private CanvasGroup _fatalHitGroup;
		[SerializeField]
		private float _hitGroupDelay = 0.15f;
		[SerializeField]
		private float _hitGroupFadeInDuration = 0.1f;
		[SerializeField]
		private float _hitGroupFadeOutDuration = 0.8f;

		[Header("Dispersion")]
		[SerializeField]
		private float _sizeDispersion0 = 24f;
		[SerializeField]
		private float _sizeDispersion50 = 1000f;
		[SerializeField]
		private float _changeSpeed = 20f;
		[Header("Crosshair Follow")]
		[SerializeField][Range(0.0f, 120.0f)]
		private float _worldRootFollowSpeed = 48.0f;

		[Header("Distance Scaling")]
		[SerializeField]
		private bool _scaleByWorldDistance = true;
		[SerializeField][Range(0.01f, 1000.0f)]
		private float _distanceScaleMinDistance = 1.0f;
		[SerializeField][Range(0.01f, 1000.0f)]
		private float _distanceScaleMaxDistance = 100.0f;
		[SerializeField][Range(0.01f, 5.0f)]
		private float _distanceScaleAtMinDistance = 1.0f;
		[SerializeField][Range(0.01f, 5.0f)]
		private float _distanceScaleAtMaxDistance = 0.1f;

		[Header("Difference Scaling")]
		[SerializeField]
		private bool _scaleCrosshairByHitDelta = true;
		[SerializeField][Range(0.0f, 50.0f)]
		private float _hitDeltaMinDistance = 0.0f;
		[SerializeField][Range(0.01f, 50.0f)]
		private float _hitDeltaMaxDistance = 5.0f;
		[SerializeField][Range(0.01f, 5.0f)]
		private float _hitDeltaScaleAtMinDistance = 1.0f;
		[SerializeField][Range(0.01f, 5.0f)]
		private float _hitDeltaScaleAtMaxDistance = 1.35f;

		private float _defaultSize;
		private int _lastSoundFrame;

		private Vector2 _targetSize;
		private Vector3 _crosshairBaseScale = Vector3.one;
		private Vector3 _undesiredFireBaseScale = Vector3.one;
		private bool _hasCachedBaseScales;
		private Canvas _rootCanvas;
		private RectTransform _canvasRect;

		// PUBLIC METHODS

		public void HitPerformed(HitData hitData)
		{
			PlayEffect(hitData.IsCritical == true ? _criticalHitPerformed : _hitPerformed);

			var hitGroup = hitData.IsFatal == true ? _fatalHitGroup : (hitData.IsCritical == true ? _criticalHitGroup : _hitGroup);
			DOTween.Kill(hitGroup);

			hitGroup.DOFade(1f, _hitGroupFadeInDuration).SetDelay(_hitGroupDelay);
			hitGroup.DOFade(0f, _hitGroupFadeOutDuration).SetDelay(_hitGroupDelay + _hitGroupFadeInDuration + 0.1f);
		}

		public void UpdateCrosshair(Agent agent)
		{
			bool hasProjectionData = TryGetProjectionData(out Rect projectionPixelRect, out float projectionAspect);
			bool hasPostBlendCameraPose = TryGetPostBlendCameraPose(out Vector3 postBlendCameraPosition, out Quaternion postBlendCameraRotation, out float postBlendFieldOfView);

			bool hasScreenHitPoint = false;
			bool hasFireHitPoint = false;
			Vector3 screenHitPoint = default;
			Vector3 fireHitPoint = default;
			bool isUndesiredTargetPoint = false;
			float crosshairDeltaScale = 1.0f;

			bool canUseLocalAimData = agent != null && agent.HasInputAuthority == true && Context != null && Context.HasInput == true;
			if (canUseLocalAimData == true && agent.Interactions != null)
			{
				if (agent.Interactions.TryGetCrosshairAndHitPoints(false, out _, out screenHitPoint, out fireHitPoint, out isUndesiredTargetPoint) == true)
				{
					hasScreenHitPoint = true;
					hasFireHitPoint = true;

					float hitDelta = Vector3.Distance(screenHitPoint, fireHitPoint);
					crosshairDeltaScale = EvaluateHitDeltaScale(hitDelta);
				}
			}

			if (_crosshairWorldRoot != null && hasProjectionData == true && hasPostBlendCameraPose == true && hasScreenHitPoint == true)
			{
				// Main crosshair is always projected from the local peer ScreenHitPoint.
				SetUIElementScreenPosition(_crosshairWorldRoot, projectionPixelRect, projectionAspect, postBlendCameraPosition, postBlendCameraRotation, postBlendFieldOfView, screenHitPoint, _worldRootFollowSpeed);
				ApplyDistanceScale(_crosshairWorldRoot, _crosshairBaseScale, Vector3.Distance(postBlendCameraPosition, screenHitPoint), crosshairDeltaScale);
			}
			else
			{
				SetCrosshairToScreenCenter(projectionPixelRect);
				ApplyDistanceScale(_crosshairWorldRoot, _crosshairBaseScale, 0.0f, 1.0f);
			}

			var weapon = agent.Weapons.CurrentWeapon;
			float size = _defaultSize;

			bool weaponValid = weapon != null && weapon.Object.IsValid;

			if (weaponValid == true && weapon is FirearmWeapon firearmWeapon)
			{
				size = Mathf.Lerp(_sizeDispersion0, _sizeDispersion50, firearmWeapon.TotalDispersion / 50f);
			}

			_targetSize = new Vector2(size, size);
			_resizingGroup.sizeDelta = Vector2.Lerp(_resizingGroup.sizeDelta, _targetSize, Time.deltaTime * _changeSpeed);

			bool showScope = weaponValid == true && weapon.HitType == EHitType.Sniper && agent.Character.CharacterController.Data.Aim == true;

			_armedGroup.SetVisibility(showScope == false && weaponValid);
			_unarmedGroup.SetVisibility(weaponValid == false);

			_sniperScope.SetActive(showScope);

			bool showUndesiredFirePosition = weaponValid == true && isUndesiredTargetPoint == true;
			bool showFireHitMarker = weaponValid == true && hasProjectionData == true && hasPostBlendCameraPose == true && hasFireHitPoint == true;
			_undesiredFirePosition.SetActive(showFireHitMarker);

			if (showFireHitMarker == true)
			{
				// Obstructed/impact indicator is projected from the local peer FireHitPoint.
				if (SetUIElementScreenPosition(_undesiredFirePosition.transform, projectionPixelRect, projectionAspect, postBlendCameraPosition, postBlendCameraRotation, postBlendFieldOfView, fireHitPoint, 0.0f) == false)
				{
					_undesiredFirePosition.SetActive(false);
				}

				ApplyDistanceScale(_undesiredFirePosition.transform, _undesiredFireBaseScale, Vector3.Distance(postBlendCameraPosition, fireHitPoint), 1.0f);
			}
			else
			{
				ApplyDistanceScale(_undesiredFirePosition.transform, _undesiredFireBaseScale, 0.0f, 1.0f);
			}

			_crosshairRootGroup.alpha = Mathf.Lerp(_crosshairRootGroup.alpha, showUndesiredFirePosition == true ? _undesiredFireCrosshairAlpha : 1f, Time.deltaTime * 8f);
		}

		// MONOBEHAVIOUR

		private void Awake()
		{
			_defaultSize = _resizingGroup.sizeDelta.x;
			_rootCanvas = GetComponentInParent<Canvas>();
			_canvasRect = _rootCanvas != null ? _rootCanvas.transform as RectTransform : null;
			if (_crosshairWorldRoot == null)
			{
				_crosshairWorldRoot = _resizingGroup != null ? _resizingGroup : transform as RectTransform;
			}

			CacheBaseScales();
		}

		// PRIVATE MEMBERS

		private void PlayEffect(AudioSetup setup)
		{
			if (Time.frameCount == _lastSoundFrame)
				return; // Play only one sound per frame

			SceneUI.PlaySound(setup);
			_lastSoundFrame = Time.frameCount;
		}

		private Camera ResolveProjectionCamera()
		{
			if (Context == null || Context.Camera == null)
				return null;

			return Context.Camera.Camera;
		}

		private bool TryGetProjectionData(out Rect pixelRect, out float aspect)
		{
			pixelRect = default;
			aspect = 0.0f;

			if (_rootCanvas != null)
			{
				Rect canvasPixelRect = _rootCanvas.pixelRect;
				if (canvasPixelRect.width > 0.01f && canvasPixelRect.height > 0.01f)
				{
					pixelRect = canvasPixelRect;
					aspect = canvasPixelRect.width / canvasPixelRect.height;
					return true;
				}
			}

			Camera projectionCamera = ResolveProjectionCamera();
			if (projectionCamera == null)
				return false;

			pixelRect = projectionCamera.pixelRect;
			aspect = projectionCamera.aspect;
			return pixelRect.width > 0.01f && pixelRect.height > 0.01f && aspect > 0.0001f;
		}

		private bool TryGetPostBlendCameraPose(out Vector3 position, out Quaternion rotation, out float fieldOfView)
		{
			position = default;
			rotation = Quaternion.identity;
			fieldOfView = 60.0f;

			if (Context == null || Context.Camera == null)
				return false;

			Context.Camera.SyncForGameplayRender();
			Context.Camera.GetPostBlendCameraPose(out position, out rotation, out fieldOfView);
			return true;
		}

		private void CacheBaseScales()
		{
			if (_hasCachedBaseScales == true)
				return;

			if (_crosshairWorldRoot != null)
			{
				_crosshairBaseScale = _crosshairWorldRoot.localScale;
			}

			if (_undesiredFirePosition != null)
			{
				_undesiredFireBaseScale = _undesiredFirePosition.transform.localScale;
			}

			_hasCachedBaseScales = true;
		}

		private void ApplyDistanceScale(Transform target, Vector3 baseScale, float worldDistance, float extraScale)
		{
			if (target == null)
				return;

			CacheBaseScales();

			float distanceScale = 1.0f;
			if (_scaleByWorldDistance == true)
			{
				float minDistance = Mathf.Min(_distanceScaleMinDistance, _distanceScaleMaxDistance);
				float maxDistance = Mathf.Max(_distanceScaleMinDistance, _distanceScaleMaxDistance);
				float range = maxDistance - minDistance;

				float lerpFactor = range > 0.0001f ? Mathf.Clamp01((worldDistance - minDistance) / range) : 0.0f;
				distanceScale = Mathf.Lerp(_distanceScaleAtMinDistance, _distanceScaleAtMaxDistance, lerpFactor);
			}

			float totalScale = Mathf.Max(0.0001f, distanceScale * Mathf.Max(0.0001f, extraScale));
			Vector3 targetScale = baseScale * totalScale;

			if ((target.localScale - targetScale).sqrMagnitude > 0.000001f)
			{
				target.localScale = targetScale;
			}
		}

		private float EvaluateHitDeltaScale(float hitDeltaDistance)
		{
			if (_scaleCrosshairByHitDelta == false)
				return 1.0f;

			float minDistance = Mathf.Min(_hitDeltaMinDistance, _hitDeltaMaxDistance);
			float maxDistance = Mathf.Max(_hitDeltaMinDistance, _hitDeltaMaxDistance);
			float range = maxDistance - minDistance;

			float lerpFactor = range > 0.0001f ? Mathf.Clamp01((hitDeltaDistance - minDistance) / range) : 0.0f;
			return Mathf.Lerp(_hitDeltaScaleAtMinDistance, _hitDeltaScaleAtMaxDistance, lerpFactor);
		}

		private bool SetUIElementScreenPosition(Transform targetTransform, Rect projectionPixelRect, float projectionAspect, Vector3 cameraPosition, Quaternion cameraRotation, float cameraFieldOfView, Vector3 worldPosition, float followSpeed)
		{
			if (targetTransform == null)
				return false;
			if (TryProjectWorldToScreen(cameraPosition, cameraRotation, cameraFieldOfView, projectionPixelRect, projectionAspect, worldPosition, out Vector3 screenPosition) == false)
				return false;

			return SetUIElementScreenPosition(targetTransform, screenPosition, followSpeed);
		}

		private bool SetUIElementScreenPosition(Transform targetTransform, Vector3 screenPosition, float followSpeed)
		{
			if (targetTransform == null)
				return false;

			RectTransform targetRect = targetTransform as RectTransform;
			if (targetRect == null)
				return false;
			if (_canvasRect == null)
			{
				targetTransform.position = screenPosition;
				return true;
			}

			Camera uiCamera = _rootCanvas != null && _rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay ? _rootCanvas.worldCamera : null;
			if (RectTransformUtility.ScreenPointToLocalPointInRectangle(_canvasRect, screenPosition, uiCamera, out Vector2 localPoint) == false)
				return false;

			followSpeed = Mathf.Max(0.0f, followSpeed);
			if (followSpeed <= 0.0f)
			{
				targetRect.anchoredPosition = localPoint;
				return true;
			}

			float t = 1.0f - Mathf.Exp(-followSpeed * Time.unscaledDeltaTime);
			targetRect.anchoredPosition = Vector2.Lerp(targetRect.anchoredPosition, localPoint, t);
			return true;
		}

		private bool TryProjectWorldToScreen(Vector3 cameraPosition, Quaternion cameraRotation, float cameraFieldOfView, Rect projectionPixelRect, float projectionAspect, Vector3 worldPosition, out Vector3 screenPosition)
		{
			screenPosition = default;
			if (cameraFieldOfView <= 0.0f || float.IsNaN(cameraFieldOfView) == true)
				return false;
			if (projectionAspect <= 0.0001f || float.IsNaN(projectionAspect) == true)
				return false;
			if (projectionPixelRect.width <= 0.01f || projectionPixelRect.height <= 0.01f)
				return false;

			Vector3 cameraSpace = Quaternion.Inverse(cameraRotation) * (worldPosition - cameraPosition);
			if (cameraSpace.z <= 0.0001f)
				return false;

			float tanHalfVerticalFov = Mathf.Tan(cameraFieldOfView * Mathf.Deg2Rad * 0.5f);
			if (tanHalfVerticalFov <= 0.0001f || float.IsNaN(tanHalfVerticalFov) == true)
				return false;

			float normalizedX = cameraSpace.x / (cameraSpace.z * tanHalfVerticalFov * projectionAspect);
			float normalizedY = cameraSpace.y / (cameraSpace.z * tanHalfVerticalFov);

			float viewportX = normalizedX * 0.5f + 0.5f;
			float viewportY = normalizedY * 0.5f + 0.5f;

			screenPosition = new Vector3(
				projectionPixelRect.x + viewportX * projectionPixelRect.width,
				projectionPixelRect.y + viewportY * projectionPixelRect.height,
				cameraSpace.z);

			if (float.IsNaN(screenPosition.x) == true || float.IsNaN(screenPosition.y) == true)
				return false;

			return true;
		}

		private void SetCrosshairToScreenCenter(Rect projectionPixelRect)
		{
			if (_crosshairWorldRoot == null)
				return;
			if (projectionPixelRect.width <= 0.01f || projectionPixelRect.height <= 0.01f)
				return;

			Vector3 center = new Vector3(projectionPixelRect.x + projectionPixelRect.width * 0.5f, projectionPixelRect.y + projectionPixelRect.height * 0.5f, 0.0f);
			SetUIElementScreenPosition(_crosshairWorldRoot, center, 0.0f);
		}
	}
}
