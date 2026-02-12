using Fusion;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace TPSBR
{
	[AddComponentMenu("GOING DARK/Agent Vision Visuals")]
	[DisallowMultipleComponent]
	public sealed class AgentVisionVisuals : MonoBehaviour
	{
		private readonly Dictionary<Renderer, uint> _originalRendererMasks = new Dictionary<Renderer, uint>(64);
		private readonly Dictionary<Renderer, int> _originalObjectLayers = new Dictionary<Renderer, int>(64);
		private readonly List<Renderer> _renderers = new List<Renderer>(64);
		private readonly List<Renderer> _weaponRenderers = new List<Renderer>(32);

		[SerializeField]
		private LayerMask _remoteVisualObjectLayerMask;

		private Agent _agent;
		private AgentVision _agentVision;
		private Weapons _weapons;
		private bool _didLogMissingAgent;
		private bool _didLogMissingVision;

		private void Awake()
		{
			Initialize();
		}

		private void OnEnable()
		{
			Initialize();
			if (enabled == false)
				return;

			ApplyVisualLayerMask();
		}

		private void LateUpdate()
		{
			if (EnsureReady() == false)
				return;

			ApplyVisualLayerMask();
		}

		private void OnDisable()
		{
			RestoreOriginalMasks();
		}

		private void Initialize()
		{
			if (_agent == null)
				_agent = GetComponentInParent<Agent>();
			if (_agentVision == null && _agent != null)
				_agentVision = _agent.GetComponentInChildren<AgentVision>(true);
			if (_weapons == null && _agent != null)
				_weapons = _agent.Weapons;

			EnsureReady();
		}

		private bool EnsureReady()
		{
			if (_agent == null)
			{
				if (_didLogMissingAgent == false)
				{
					Debug.LogWarning("[AgentVisionVisuals] No Agent found in parent hierarchy. Disabling.", this);
					_didLogMissingAgent = true;
				}

				enabled = false;
				return false;
			}

			if (_agentVision == null)
			{
				if (_didLogMissingVision == false)
				{
					Debug.LogWarning("[AgentVisionVisuals] No AgentVision found in parent hierarchy. Disabling.", this);
					_didLogMissingVision = true;
				}

				enabled = false;
				return false;
			}

			return true;
		}

		private void ApplyVisualLayerMask()
		{
			if (Application.isPlaying == false || _agent == null || _agentVision == null)
				return;

			SceneContext context = _agent.Context;
			bool isLocalControlled = context != null ? (_agent.HasInputAuthority && context.HasInput) : _agent.HasInputAuthority;
			uint visionMask = _agentVision.VisionLightLayerMask;
			int overrideObjectLayer = ResolveSingleLayer(_remoteVisualObjectLayerMask);

			CollectRenderers();

			for (int i = 0; i < _renderers.Count; ++i)
			{
				Renderer renderer = _renderers[i];
				if (renderer == null)
					continue;

				if (_originalRendererMasks.ContainsKey(renderer) == false)
				{
					_originalRendererMasks.Add(renderer, renderer.renderingLayerMask);
				}
				if (_originalObjectLayers.ContainsKey(renderer) == false)
				{
					_originalObjectLayers.Add(renderer, renderer.gameObject.layer);
				}

				uint originalMask = _originalRendererMasks[renderer];
				uint desiredMask = isLocalControlled ? visionMask : (originalMask | visionMask);
				if (renderer.renderingLayerMask != desiredMask)
				{
					renderer.renderingLayerMask = desiredMask;
				}

				int originalObjectLayer = _originalObjectLayers[renderer];
				int desiredObjectLayer = isLocalControlled == true ? originalObjectLayer : (overrideObjectLayer >= 0 ? overrideObjectLayer : originalObjectLayer);
				if (renderer.gameObject.layer != desiredObjectLayer)
				{
					renderer.gameObject.layer = desiredObjectLayer;
				}
			}
		}

		private void CollectRenderers()
		{
			_renderers.Clear();
			GetComponentsInChildren(true, _renderers);

			if (_weapons == null)
				return;

			_weaponRenderers.Clear();
			_weapons.GetComponentsInChildren(true, _weaponRenderers);
			for (int i = 0; i < _weaponRenderers.Count; ++i)
			{
				Renderer weaponRenderer = _weaponRenderers[i];
				if (weaponRenderer == null)
					continue;

				if (_renderers.Contains(weaponRenderer) == false)
				{
					_renderers.Add(weaponRenderer);
				}
			}
		}

		private void RestoreOriginalMasks()
		{
			foreach (KeyValuePair<Renderer, uint> pair in _originalRendererMasks)
			{
				Renderer renderer = pair.Key;
				if (renderer == null)
					continue;

				if (renderer.renderingLayerMask != pair.Value)
				{
					renderer.renderingLayerMask = pair.Value;
				}
			}

			foreach (KeyValuePair<Renderer, int> pair in _originalObjectLayers)
			{
				Renderer renderer = pair.Key;
				if (renderer == null)
					continue;

				if (renderer.gameObject.layer != pair.Value)
				{
					renderer.gameObject.layer = pair.Value;
				}
			}
		}

		private static int ResolveSingleLayer(LayerMask layerMask)
		{
			int bits = layerMask.value;
			if (bits == 0)
				return -1;

			for (int i = 0; i < 32; ++i)
			{
				if ((bits & (1 << i)) != 0)
					return i;
			}

			return -1;
		}
	}
}
