using Fusion;
using System.Collections.Generic;
using UnityEngine;

namespace TPSBR
{
	[AddComponentMenu("GOING DARK/Agent Sight Visuals")]
	[DisallowMultipleComponent]
	public sealed class AgentSightVisuals : MonoBehaviour
	{
		private readonly Dictionary<Renderer, uint> _originalRendererMasks = new Dictionary<Renderer, uint>(64);
		private readonly List<Renderer> _renderers = new List<Renderer>(64);

		private Agent _agent;
		private AgentSight _agentSight;
		private bool _didLogMissingAgent;
		private bool _didLogMissingSight;

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
			if (_agentSight == null && _agent != null)
				_agentSight = _agent.GetComponentInChildren<AgentSight>(true);

			EnsureReady();
		}

		private bool EnsureReady()
		{
			if (_agent == null)
			{
				if (_didLogMissingAgent == false)
				{
					Debug.LogWarning("[AgentSightVisuals] No Agent found in parent hierarchy. Disabling.", this);
					_didLogMissingAgent = true;
				}

				enabled = false;
				return false;
			}

			if (_agentSight == null)
			{
				if (_didLogMissingSight == false)
				{
					Debug.LogWarning("[AgentSightVisuals] No AgentSight found in parent hierarchy. Disabling.", this);
					_didLogMissingSight = true;
				}

				enabled = false;
				return false;
			}

			return true;
		}

		private void ApplyVisualLayerMask()
		{
			if (Application.isPlaying == false || _agent == null || _agentSight == null)
				return;

			SceneContext context = _agent.Context;
			bool isLocalControlled = context != null ? (_agent.HasInputAuthority && context.HasInput) : _agent.HasInputAuthority;
			uint sightMask = _agentSight.SightLightLayerMask;

			_renderers.Clear();
			GetComponentsInChildren(true, _renderers);

			for (int i = 0; i < _renderers.Count; ++i)
			{
				Renderer renderer = _renderers[i];
				if (renderer == null)
					continue;

				if (_originalRendererMasks.ContainsKey(renderer) == false)
				{
					_originalRendererMasks.Add(renderer, renderer.renderingLayerMask);
				}

				uint originalMask = _originalRendererMasks[renderer];
				uint desiredMask = isLocalControlled ? sightMask : (originalMask | sightMask);
				if (renderer.renderingLayerMask != desiredMask)
				{
					renderer.renderingLayerMask = desiredMask;
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
		}
	}
}
