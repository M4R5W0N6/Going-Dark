using FOW;
using Fusion;
using UnityEngine;
using UnityEngine.Rendering;

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
		[SerializeField]
		private FogOfWarRevealer[] _revealers;
		[SerializeField]
		private FogOfWarHider[] _hiders;
		[SerializeField]
		private bool _useHiderComponents = false;
		[SerializeField]
		private bool _usePartialHiderMaterials = false;
		[SerializeField]
		private bool _assignVisualRenderersToFoWLayer = true;
		[SerializeField]
		private string _visualRendererLayerName = "FoW";
		[SerializeField]
		private Transform _visualsRoot;
		[SerializeField]
		private Material _partialHiderOpaqueTemplate;
		[SerializeField]
		private Material _partialHiderTransparentTemplate;
		[SerializeField]
		private bool _forceTransparentPartialShader = true;
		[SerializeField]
		private bool _swapMaterialsAtRuntime = true;

		private Agent _agent;
		private bool _hasAppliedState;
		private bool _lastRevealerEnabled;
		private bool _lastHiderEnabled;
		private bool _lastPartialHiderEnabled;
		private bool _partialMaterialsInitialized;
		private bool _visualRendererLayersAssigned;
		private PartialHiderRegisterer _partialHiderRegisterer;

		private void Awake()
		{
			_agent = GetComponent<Agent>();
			CacheComponents();
			EnsureVisualRendererLayers();
			EnsurePartialHiderReady();
		}

		private void OnEnable()
		{
			CacheComponents();
			EnsureVisualRendererLayers();
			EnsurePartialHiderReady();
			ApplyState(force: true);
		}

		private void LateUpdate()
		{
			ApplyState(force: false);
		}

		private void OnDisable()
		{
			if (_partialHiderRegisterer != null)
			{
				_partialHiderRegisterer.enabled = false;
			}

			_hasAppliedState = false;
		}

		private void CacheComponents()
		{
			if (_revealers == null || _revealers.Length == 0)
			{
				_revealers = GetComponentsInChildren<FogOfWarRevealer>(true);
			}

			if (_hiders == null || _hiders.Length == 0)
			{
				_hiders = GetComponentsInChildren<FogOfWarHider>(true);
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

		private void EnsureVisualRendererLayers()
		{
			if (_assignVisualRenderersToFoWLayer == false)
				return;
			if (_visualRendererLayersAssigned)
				return;
			if (_visualsRoot == null)
				return;

			int targetLayer = LayerMask.NameToLayer(_visualRendererLayerName);
			if (targetLayer < 0)
			{
				Debug.LogWarning($"[AgentFoW] Layer '{_visualRendererLayerName}' does not exist.", this);
				return;
			}

			Renderer[] renderers = _visualsRoot.GetComponentsInChildren<Renderer>(true);
			for (int i = 0; i < renderers.Length; i++)
			{
				Renderer renderer = renderers[i];
				if (renderer == null)
					continue;

				renderer.gameObject.layer = targetLayer;
			}

			_visualRendererLayersAssigned = true;
		}

		private void ApplyState(bool force)
		{
			if (Application.isPlaying == false)
				return;

			if (_agent == null)
				_agent = GetComponent<Agent>();
			if (_agent == null)
				return;

			bool disableAll = false;

			var runner = _agent.Runner;
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
			bool hiderEnabled = disableAll == false && isLocalControlled == false;
			bool partialHiderEnabled = false;

			if (_useHiderComponents == false)
			{
				hiderEnabled = false;
			}

			if (_usePartialHiderMaterials)
			{
				EnsurePartialHiderReady();
				hiderEnabled = false;
				partialHiderEnabled = disableAll == false;
			}

			if (force == false && _hasAppliedState && _lastRevealerEnabled == revealerEnabled && _lastHiderEnabled == hiderEnabled && _lastPartialHiderEnabled == partialHiderEnabled)
				return;

			_hasAppliedState = true;
			_lastRevealerEnabled = revealerEnabled;
			_lastHiderEnabled = hiderEnabled;
			_lastPartialHiderEnabled = partialHiderEnabled;

			for (int i = 0; i < _revealers.Length; i++)
			{
				FogOfWarRevealer revealer = _revealers[i];
				if (revealer != null)
				{
					revealer.enabled = revealerEnabled;
				}
			}

			for (int i = 0; i < _hiders.Length; i++)
			{
				FogOfWarHider hider = _hiders[i];
				if (hider != null)
				{
					hider.enabled = hiderEnabled;
				}
			}

			if (_partialHiderRegisterer != null)
			{
				_partialHiderRegisterer.enabled = partialHiderEnabled;
			}
		}

		private void EnsurePartialHiderReady()
		{
			if (_usePartialHiderMaterials == false)
				return;
			if (_visualsRoot == null)
				return;

			if (_partialHiderRegisterer == null)
			{
				_partialHiderRegisterer = _visualsRoot.GetComponent<PartialHiderRegisterer>();
				if (_partialHiderRegisterer == null)
				{
					_partialHiderRegisterer = _visualsRoot.gameObject.AddComponent<PartialHiderRegisterer>();
				}
			}

			if (_partialMaterialsInitialized)
				return;

			Renderer[] renderers = _visualsRoot.GetComponentsInChildren<Renderer>(true);
			if (renderers == null || renderers.Length == 0)
				return;

			bool hasTemplates = _partialHiderOpaqueTemplate != null || _partialHiderTransparentTemplate != null;
			if (hasTemplates == false)
			{
				Debug.LogWarning("[AgentFoW] Partial hider templates are not assigned. Materials will not be converted to FoW shaders.", this);
			}

			System.Collections.Generic.List<Material> uniqueMaterials = new System.Collections.Generic.List<Material>(64);
			System.Collections.Generic.HashSet<Material> uniqueMaterialSet = new System.Collections.Generic.HashSet<Material>();

			for (int i = 0; i < renderers.Length; i++)
			{
				Renderer renderer = renderers[i];
				if (renderer == null)
					continue;

				Material[] materials = _swapMaterialsAtRuntime ? renderer.materials : renderer.sharedMaterials;
				bool hasChanges = false;

				for (int j = 0; j < materials.Length; j++)
				{
					Material sourceMaterial = materials[j];
					if (sourceMaterial == null)
						continue;

					Material partialTemplate = GetPartialTemplateForMaterial(sourceMaterial);
					if (_swapMaterialsAtRuntime && partialTemplate != null && sourceMaterial.shader != partialTemplate.shader)
					{
						Material convertedMaterial = new Material(partialTemplate);
						convertedMaterial.name = $"{sourceMaterial.name} [FoW]";
						convertedMaterial.CopyPropertiesFromMaterial(sourceMaterial);
						ApplyTemplateSurfaceSettings(convertedMaterial, partialTemplate);
						materials[j] = convertedMaterial;
						sourceMaterial = convertedMaterial;
						hasChanges = true;
					}

					if (uniqueMaterialSet.Add(sourceMaterial))
					{
						uniqueMaterials.Add(sourceMaterial);
					}
				}

				if (hasChanges)
				{
					renderer.materials = materials;
				}
			}

			_partialHiderRegisterer.MaterialsToInitialize = uniqueMaterials.ToArray();
			if (_partialHiderRegisterer.enabled)
			{
				// Ensure materials are registered even if the component was already enabled
				// before MaterialsToInitialize was populated.
				_partialHiderRegisterer.RegisterMaterials();
			}
			_partialMaterialsInitialized = true;
		}

		private Material GetPartialTemplateForMaterial(Material sourceMaterial)
		{
			if (sourceMaterial == null)
				return null;
			if (_forceTransparentPartialShader && _partialHiderTransparentTemplate != null)
				return _partialHiderTransparentTemplate;

			bool isTransparent = sourceMaterial.renderQueue >= (int)RenderQueue.Transparent;
			if (sourceMaterial.HasProperty("_Surface"))
			{
				isTransparent |= sourceMaterial.GetFloat("_Surface") > 0.5f;
			}

			if (isTransparent)
			{
				if (_partialHiderTransparentTemplate != null)
					return _partialHiderTransparentTemplate;
				return _partialHiderOpaqueTemplate;
			}

			if (_partialHiderOpaqueTemplate != null)
				return _partialHiderOpaqueTemplate;
			return _partialHiderTransparentTemplate;
		}

		private static void ApplyTemplateSurfaceSettings(Material target, Material template)
		{
			if (target == null || template == null)
				return;

			target.renderQueue = template.renderQueue;

			CopyIfPresent(target, template, "_Surface");
			CopyIfPresent(target, template, "_AlphaClip");
			CopyIfPresent(target, template, "_Cutoff");
			CopyIfPresent(target, template, "_SrcBlend");
			CopyIfPresent(target, template, "_DstBlend");
			CopyIfPresent(target, template, "_Blend");
			CopyIfPresent(target, template, "_ZWrite");
			CopyIfPresent(target, template, "_Cull");
			CopyIfPresent(target, template, "_QueueOffset");

			target.DisableKeyword("_SURFACE_TYPE_OPAQUE");
			target.DisableKeyword("_ALPHAPREMULTIPLY_ON");
			target.EnableKeyword("_ALPHATEST_ON");
			target.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
		}

		private static void CopyIfPresent(Material target, Material template, string propertyName)
		{
			if (target.HasProperty(propertyName) == false || template.HasProperty(propertyName) == false)
				return;

			target.SetFloat(propertyName, template.GetFloat(propertyName));
		}
	}
}
