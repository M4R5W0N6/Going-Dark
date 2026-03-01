using System;
using System.Collections.Generic;
using UnityEngine;

namespace FusionAnimator
{
    [Serializable]
    public sealed class FusionAnimatorScopeUtilityNodeLayout
    {
        [SerializeField] public string LayerId;
        [SerializeField] public string ScopePath;
        [SerializeField] public bool HasScopeNodePosition;
        [SerializeField] public Vector2 ScopeNodePosition;
        [SerializeField] public Vector2 EntryNodePosition = new Vector2(-300.0f, -120.0f);
        [SerializeField] public Vector2 AnyNodePosition = new Vector2(-300.0f, 20.0f);
        [SerializeField] public Vector2 ExitNodePosition = new Vector2(300.0f, -40.0f);
    }

    [Serializable]
    public sealed class FusionAnimatorScopeTransitionSuppression
    {
        [SerializeField] public string TransitionId;
        [SerializeField] public string LayerId;
        [SerializeField] public string ScopePath;
    }

    [CreateAssetMenu(menuName = "Fusion/Fusion Animator/Animation Graph", fileName = "FusionAnimatorGraph")]
    public sealed class FusionAnimatorGraphAsset : ScriptableObject
    {
        public const string SpecialNodeEntryId = "__entry__";
        public const string SpecialNodeAnyId = "__any__";
        public const string SpecialNodeExitId = "__exit__";
        public const string ScopeSentinelStateLeafName = "__fa_scope_anchor__";

        [SerializeField] public string GraphId;
        [SerializeField] public string DisplayName = "Fusion Animator Graph";
        [SerializeField] public string EntryStateId;
        [SerializeField] public bool ApplyRootMotion;
        [SerializeField] public Vector2 EntryNodePosition = new Vector2(-300.0f, -120.0f);
        [SerializeField] public Vector2 AnyNodePosition = new Vector2(-300.0f, 20.0f);
        [SerializeField] public Vector2 ExitNodePosition = new Vector2(300.0f, -40.0f);
        [Header("Editor Session")]
        [SerializeField] public UnityEngine.Object PreviewSource;
        [SerializeField] public GameObject PreviewTarget;
        [SerializeField] public string PreviewTargetGlobalObjectId;

        [Header("Authoring Data")]
        [SerializeField] public List<FusionAnimatorParameterDefinition> Parameters = new List<FusionAnimatorParameterDefinition>();
        [SerializeField] public List<FusionAnimatorBindingGroupDefinition> BindingGroups = new List<FusionAnimatorBindingGroupDefinition>();
        [SerializeField] public List<FusionAnimatorClipBindingDefinition> ClipBindings = new List<FusionAnimatorClipBindingDefinition>();
        [SerializeField] public List<FusionAnimatorLayerDefinition> Layers = new List<FusionAnimatorLayerDefinition>();
        [SerializeField] public List<FusionAnimatorStateDefinition> States = new List<FusionAnimatorStateDefinition>();
        [SerializeField] public List<FusionAnimatorTransitionDefinition> Transitions = new List<FusionAnimatorTransitionDefinition>();
        [SerializeField] public List<FusionAnimatorScopeUtilityNodeLayout> ScopeUtilityNodeLayouts = new List<FusionAnimatorScopeUtilityNodeLayout>();
        [SerializeField] public List<FusionAnimatorScopeTransitionSuppression> ScopeTransitionSuppressions = new List<FusionAnimatorScopeTransitionSuppression>();

        private void OnValidate()
        {
            if (string.IsNullOrWhiteSpace(GraphId))
            {
                GraphId = NewId("graph");
            }
        }

        public static string NewId(string prefix)
        {
            return string.Format("{0}_{1}", prefix, Guid.NewGuid().ToString("N"));
        }
    }
}
