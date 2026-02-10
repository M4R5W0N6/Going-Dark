using FOW;
using Fusion;
using UnityEngine;
using UnityEngine.Serialization;

namespace TPSBR
{
	[AddComponentMenu("GOING DARK/Agent Fog of War")]
	[DisallowMultipleComponent]
	[RequireComponent(typeof(Agent))]
	public sealed class AgentFoW : MonoBehaviour
	{
		[SerializeField]
		private bool _disableOnDedicatedServer = true;
		[SerializeField]
		private bool _disableWhenPeerNotVisible = true;

		[Header("Revealers")]
		[SerializeField]
		private FogOfWarRevealer[] _revealers;
		[SerializeField, FormerlySerializedAs("_syncViewAngleToCharacterFoV")]
		private bool SyncFov = true;
		[SerializeField]
		private Vector2 _viewAngleClamp = new Vector2(1.0f, 360.0f);

		[Header("Visuals")]
		[SerializeField, FormerlySerializedAs("_assignVisualRenderersToFoWLayer")]
		private bool _autoAssignVisualLayers = true;
		[SerializeField, FormerlySerializedAs("_visualRendererLayerName")]
		private string _visualLayerName = "FoW";
		[SerializeField]
		private Transform _visualsRoot;

		private Agent _agent;
		private Character _character;
		private bool _hasAppliedState;
		private bool _lastRevealerEnabled;
		private float _lastAppliedViewAngle = -1.0f;
		private bool _loggedMissingLayer;

		private void Awake()
		{
			CacheComponents();
			RefreshVisualLayers();
		}

		private void OnEnable()
		{
			CacheComponents();
			RefreshVisualLayers();
			ApplyState(force: true);
		}

		private void LateUpdate()
		{
			CacheComponents();
			RefreshVisualLayers();
			SyncRevealerViewAngle();
			ApplyState(force: false);
		}

		private void OnDisable()
		{
			_hasAppliedState = false;
		}

		private void CacheComponents()
		{
			if (_agent == null)
			{
				_agent = GetComponent<Agent>();
			}

			if (_character == null && _agent != null)
			{
				_character = _agent.Character;
			}

			if (_revealers == null || _revealers.Length == 0)
			{
				_revealers = GetComponentsInChildren<FogOfWarRevealer>(true);
			}

			if (_visualsRoot == null)
			{
				Transform foundVisualsRoot = transform.Find("VisualsRoot");
				if (foundVisualsRoot != null)
				{
					_visualsRoot = foundVisualsRoot;
				}
			}
		}

		private void RefreshVisualLayers()
		{
			if (_autoAssignVisualLayers == false || _visualsRoot == null)
				return;

			int targetLayer = LayerMask.NameToLayer(_visualLayerName);
			if (targetLayer < 0)
			{
				if (_loggedMissingLayer == false)
				{
					Debug.LogWarning($"[AgentFoW] Layer '{_visualLayerName}' does not exist.", this);
					_loggedMissingLayer = true;
				}
				return;
			}

			_loggedMissingLayer = false;

			Renderer[] renderers = _visualsRoot.GetComponentsInChildren<Renderer>(true);
			for (int i = 0; i < renderers.Length; i++)
			{
				Renderer renderer = renderers[i];
				if (renderer == null)
					continue;

				GameObject rendererObject = renderer.gameObject;
				if (rendererObject.layer != targetLayer)
				{
					rendererObject.layer = targetLayer;
				}
			}
		}

		private void SyncRevealerViewAngle()
		{
			if (SyncFov == false)
				return;
			if (_character == null || _revealers == null || _revealers.Length == 0)
				return;

			float inputFov = _character.CurrentFOV;
			float baseFov = _character.BaseFOV;
			float dynamicMultiplier = baseFov > 0.001f ? inputFov / baseFov : 1.0f;
			float weightedFov = inputFov * dynamicMultiplier;

			float aspect = Screen.height > 0 ? (float)Screen.width / Screen.height : 16.0f / 9.0f;
			float viewAngle = 2.0f * Mathf.Atan(Mathf.Tan(weightedFov * 0.5f * Mathf.Deg2Rad) * aspect) * Mathf.Rad2Deg;

			viewAngle = Mathf.Clamp(viewAngle, _viewAngleClamp.x, _viewAngleClamp.y);
			if (Mathf.Abs(_lastAppliedViewAngle - viewAngle) <= 0.001f)
				return;

			_lastAppliedViewAngle = viewAngle;

			for (int i = 0; i < _revealers.Length; i++)
			{
				FogOfWarRevealer revealer = _revealers[i];
				if (revealer == null)
					continue;

				if (Mathf.Abs(revealer.ViewAngle - viewAngle) > 0.001f)
				{
					revealer.ViewAngle = viewAngle;
				}
			}
		}

		private void ApplyState(bool force)
		{
			if (Application.isPlaying == false)
				return;
			if (_agent == null || _revealers == null)
				return;

			bool disableAll = false;

			NetworkRunner runner = _agent.Runner;
			bool isDedicatedServer = ApplicationSettings.IsBatchServer || (runner != null && runner.Mode == SimulationModes.Server);
			if (_disableOnDedicatedServer && isDedicatedServer)
			{
				disableAll = true;
			}

			SceneContext context = _agent.Context;
			if (_disableWhenPeerNotVisible && context != null && context.IsVisible == false)
			{
				disableAll = true;
			}

			bool isLocalControlled = context != null ? (_agent.HasInputAuthority && context.HasInput) : _agent.HasInputAuthority;
			bool revealerEnabled = disableAll == false && isLocalControlled;

			if (force == false && _hasAppliedState && _lastRevealerEnabled == revealerEnabled)
				return;

			_hasAppliedState = true;
			_lastRevealerEnabled = revealerEnabled;

			for (int i = 0; i < _revealers.Length; i++)
			{
				FogOfWarRevealer revealer = _revealers[i];
				if (revealer != null)
				{
					revealer.enabled = revealerEnabled;
				}
			}
		}
	}
}
