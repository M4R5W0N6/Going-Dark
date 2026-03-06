using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace FusionAnimator
{
    public enum FusionAnimatorParameterType
    {
        Bool = 0,
        Int = 1,
        Float = 2,
        Trigger = 3,
        Vector2 = 4,
    }

    public enum FusionAnimatorParameterComponent
    {
        None = 0,
        X = 1,
        Y = 2,
    }

    public enum FusionAnimatorLayerBlendMode
    {
        Override = 0,
        Additive = 1,
    }

    public enum FusionAnimatorInterruptionSource
    {
        None = 0,
        CurrentState = 1,
        NextState = 2,
        CurrentThenNext = 3,
        NextThenCurrent = 4,
    }

    public enum FusionAnimatorConditionOperator
    {
        IsTrue = 0,
        IsFalse = 1,
        Equal = 2,
        NotEqual = 3,
        Greater = 4,
        GreaterOrEqual = 5,
        Less = 6,
        LessOrEqual = 7,
    }

    public enum FusionAnimatorMotionType
    {
        Clip = 0,
        BlendTree = 1,
    }

    public enum FusionAnimatorClipReferenceMode
    {
        Direct = 0,
        Binding = 1,
    }

    public enum FusionAnimatorBlendTreeType
    {
        OneD = 0,
        OneDSignedSpeed = 6,
        TwoDSimpleDirectional = 1,
        TwoDFreeformDirectional = 2,
        TwoDFreeformCartesian = 3,
        Direct = 4,
        DirectionalPoseTime2D = 5,
    }

    public enum FusionAnimatorStateSemantic
    {
        None = 0,
        LookPose = 1,
        TurnInPlace = 2,
        ShootOverlay = 3,
    }

    public enum FusionAnimatorTransitionResultOperation
    {
        Set = 0,
        Cycle = 1,
    }

    [Serializable]
    public sealed class FusionAnimatorParameterDefinition
    {
        [SerializeField] public string Id;
        [SerializeField] public string Name = "Parameter";
        [SerializeField] public FusionAnimatorParameterType Type = FusionAnimatorParameterType.Float;
        [SerializeField] public bool DefaultBool;
        [SerializeField] public bool Invert;
        [SerializeField] public int DefaultInt;
        [SerializeField] public float DefaultFloat;
        [SerializeField] public Vector2 DefaultVector2;
        [SerializeField] public string PreviewInputBinding;
        [SerializeField] public float PreviewInputScale = 1.0f;
        [SerializeField] public FusionAnimatorConditionOperator PreviewBoolInputOperator = FusionAnimatorConditionOperator.Greater;
        [SerializeField] public float PreviewBoolInputCompareValue = 0.5f;
    }

    [Serializable]
    public sealed class FusionAnimatorLayerDefinition
    {
        [SerializeField] public string Id;
        [SerializeField] public string Name = "Layer";
        [SerializeField] public int Priority;
        [SerializeField] public float DefaultWeight = 1.0f;
        [SerializeField] public bool EnabledByDefault = true;
        [SerializeField] public FusionAnimatorLayerBlendMode BlendMode = FusionAnimatorLayerBlendMode.Override;
        [SerializeField] public AvatarMask AvatarMask;
        [SerializeField] public int SyncedLayerIndex = -1;
        [SerializeField] public bool SyncTiming;
        [SerializeField] public bool IKPass;
    }

    [Serializable]
    public sealed class FusionAnimatorClipSlot
    {
        [SerializeField] public string Slot = "Default";
        [SerializeField] public FusionAnimatorClipReferenceMode ReferenceMode = FusionAnimatorClipReferenceMode.Direct;
        [SerializeField] public string BindingId;
        [SerializeField] public AnimationClip Clip;
        [SerializeField] public float Speed = 1.0f;
        [SerializeField] public bool Loop = true;
    }

    [Serializable]
    public sealed class FusionAnimatorClipBindingSlot
    {
        [SerializeField] public string Slot = "Default";
        [SerializeField] public AnimationClip Clip;
        [SerializeField] public float Speed = 1.0f;
        [SerializeField] public bool Loop = true;
        [SerializeField] public List<FusionAnimatorConditionDefinition> Conditions = new List<FusionAnimatorConditionDefinition>();
    }

    [Serializable]
    public sealed class FusionAnimatorBindingGroupDefinition
    {
        [SerializeField] public string Id;
        [SerializeField] public string Name = "Group";
    }

    [Serializable]
    public sealed class FusionAnimatorClipBindingDefinition
    {
        [SerializeField] public string Id;
        [SerializeField] public string Name = "Binding";
        [SerializeField] public string GroupId;
        [SerializeField] public List<FusionAnimatorConditionDefinition> Conditions = new List<FusionAnimatorConditionDefinition>();
        [SerializeField] public string ClipIndexParameterId;
        [SerializeField] public List<FusionAnimatorClipBindingSlot> Clips = new List<FusionAnimatorClipBindingSlot>();
        [SerializeField, FormerlySerializedAs("Clip"), HideInInspector] private AnimationClip _legacyClip;

        public void MigrateLegacyClip()
        {
            if (_legacyClip == null)
            {
                return;
            }

            if (Clips == null)
            {
                Clips = new List<FusionAnimatorClipBindingSlot>();
            }

            if (Clips.Count == 0)
            {
                Clips.Add(new FusionAnimatorClipBindingSlot
                {
                    Slot = "Default",
                    Clip = _legacyClip,
                });
            }

            _legacyClip = null;
        }
    }

    [Serializable]
    public sealed class FusionAnimatorBlendTreeChild
    {
        [SerializeField] public string Name = "Motion";
        [SerializeField] public FusionAnimatorClipReferenceMode ReferenceMode = FusionAnimatorClipReferenceMode.Direct;
        [SerializeField] public string BindingId;
        [SerializeField] public AnimationClip Clip;
        [SerializeField] public float Threshold;
        [SerializeField] public Vector2 Position;
        [SerializeField] public string DirectParameterId;
        [SerializeField] public float TimeScale = 1.0f;
    }

    [Serializable]
    public sealed class FusionAnimatorBlendTreeDefinition
    {
        [SerializeField] public FusionAnimatorBlendTreeType Type = FusionAnimatorBlendTreeType.OneD;
        [SerializeField] public string ParameterXId;
        [SerializeField] public string ParameterYId;
        [SerializeField] public string ParameterVector2Id;
        [SerializeField] public string PoseTimeParameterId;
        [SerializeField] public string DirectBlendParameterId;
        [SerializeField] public float InputOffsetX;
        [SerializeField] public float InputPowerX = 1.0f;
        [SerializeField] public bool NormalizeTimeScale = true;
        [SerializeField] public bool AutoDetectOnClipAssign = false;
        [SerializeField] public List<FusionAnimatorBlendTreeChild> Children = new List<FusionAnimatorBlendTreeChild>();
    }

    [Serializable]
    public sealed class FusionAnimatorStatePresentationDefinition
    {
        [SerializeField] public FusionAnimatorStateSemantic Semantic = FusionAnimatorStateSemantic.None;
        [SerializeField] public float Offset;
        [SerializeField] public float Power = 1.0f;
        [SerializeField] public float BlendSpeed = 1.0f;
        [SerializeField] public float TurnSpeed = 1.0f;
        [SerializeField] public float MaxMagnitude = 1.0f;
        [SerializeField] public float OverlayWeight = 1.0f;
    }

    [Serializable]
    public sealed class FusionAnimatorStateDefinition
    {
        [SerializeField] public string Id;
        [SerializeField] public string Name = "State";
        [SerializeField] public string LayerId;
        [SerializeField] public Vector2 NodePosition;
        [SerializeField] public float MinDurationSeconds;
        [SerializeField] public bool CanTransitionOut = true;
        [SerializeField] public bool WriteDefaults;
        [SerializeField] public FusionAnimatorMotionType MotionType = FusionAnimatorMotionType.Clip;
        [SerializeField] public List<FusionAnimatorClipSlot> Clips = new List<FusionAnimatorClipSlot>();
        [SerializeField] public FusionAnimatorBlendTreeDefinition BlendTree = new FusionAnimatorBlendTreeDefinition();
        [SerializeField] public FusionAnimatorStatePresentationDefinition Presentation = new FusionAnimatorStatePresentationDefinition();
    }

    [Serializable]
    public sealed class FusionAnimatorConditionDefinition
    {
        [SerializeField] public string ParameterId;
        [SerializeField] public FusionAnimatorConditionOperator Operator = FusionAnimatorConditionOperator.IsTrue;
        [SerializeField] public bool UseAbsoluteValue;
        [SerializeField] public bool BoolValue;
        [SerializeField] public int IntValue;
        [SerializeField] public float FloatValue;
        [SerializeField] public Vector2 Vector2Value;
    }

    [Serializable]
    public sealed class FusionAnimatorTransitionResultDefinition
    {
        [SerializeField] public string ParameterId;
        [SerializeField] public FusionAnimatorTransitionResultOperation Operation = FusionAnimatorTransitionResultOperation.Set;
        [SerializeField] public bool BoolValue;
        [SerializeField] public int IntValue;
        [SerializeField] public float FloatValue;
        [SerializeField] public Vector2 Vector2Value;
        [SerializeField] public int CycleMinValue;
        [SerializeField] public int CycleMaxValue = 1;
    }

    [Serializable]
    public sealed class FusionAnimatorTransitionDefinition
    {
        [SerializeField] public string Id;
        [SerializeField] public string Name = "Transition";
        [SerializeField] public string FromStateId;
        [SerializeField] public string ToStateId;
        [SerializeField] public int Priority;
        [SerializeField] public bool Mute;
        [SerializeField] public bool Solo;
        [SerializeField] public bool HasExitTime;
        [SerializeField] public float ExitTimeNormalized = 1.0f;
        [SerializeField] public float StartOffsetNormalized;
        [SerializeField] public bool FixedDuration = true;
        [SerializeField] public float BlendDurationSeconds = 0.1f;
        [SerializeField] public FusionAnimatorInterruptionSource InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext;
        [SerializeField] public bool CanInterrupt = true;
        [SerializeField] public List<FusionAnimatorConditionDefinition> Conditions = new List<FusionAnimatorConditionDefinition>();
        [SerializeField] public List<FusionAnimatorTransitionResultDefinition> PreviewResults = new List<FusionAnimatorTransitionResultDefinition>();
    }

    public static class FusionAnimatorClipBindingUtility
    {
        public static FusionAnimatorClipBindingDefinition FindBinding(FusionAnimatorGraphAsset graph, string bindingId)
        {
            if (graph == null || graph.ClipBindings == null || string.IsNullOrWhiteSpace(bindingId))
            {
                return null;
            }

            for (int i = 0; i < graph.ClipBindings.Count; ++i)
            {
                FusionAnimatorClipBindingDefinition binding = graph.ClipBindings[i];
                if (binding == null || string.IsNullOrWhiteSpace(binding.Id))
                {
                    continue;
                }

                if (string.Equals(binding.Id, bindingId, StringComparison.Ordinal))
                {
                    binding.MigrateLegacyClip();
                    return binding;
                }
            }

            return null;
        }

        public static FusionAnimatorClipBindingSlot ResolveBindingClipSlot(
            FusionAnimatorGraphAsset graph,
            string bindingId,
            Func<FusionAnimatorConditionDefinition, bool> evaluateCondition = null,
            Func<string, int?> resolveIntParameter = null)
        {
            FusionAnimatorClipBindingDefinition binding = FindBinding(graph, bindingId);
            if (binding == null)
            {
                return null;
            }

            if (evaluateCondition != null &&
                HasConditions(binding.Conditions) &&
                ConditionsPass(binding.Conditions, evaluateCondition) == false)
            {
                return null;
            }

            if (binding.Clips == null || binding.Clips.Count == 0)
            {
                return null;
            }

            if (resolveIntParameter != null && string.IsNullOrWhiteSpace(binding.ClipIndexParameterId) == false)
            {
                int? rawSelectedIndex = resolveIntParameter(binding.ClipIndexParameterId);
                if (rawSelectedIndex.HasValue)
                {
                    int selectedIndex = Mathf.Clamp(rawSelectedIndex.Value, 0, binding.Clips.Count - 1);
                    FusionAnimatorClipBindingSlot selectedOption = binding.Clips[selectedIndex];
                    if (selectedOption == null)
                    {
                        return null;
                    }

                    if (HasConditions(selectedOption.Conditions) == false)
                    {
                        return selectedOption;
                    }

                    return ConditionsPass(selectedOption.Conditions, evaluateCondition) ? selectedOption : null;
                }
            }

            FusionAnimatorClipBindingSlot firstOption = null;
            FusionAnimatorClipBindingSlot firstUnconditional = null;
            for (int i = 0; i < binding.Clips.Count; ++i)
            {
                FusionAnimatorClipBindingSlot option = binding.Clips[i];
                if (option == null)
                {
                    continue;
                }

                if (firstOption == null)
                {
                    firstOption = option;
                }

                bool hasConditions = HasConditions(option.Conditions);
                if (hasConditions == false)
                {
                    if (firstUnconditional == null)
                    {
                        firstUnconditional = option;
                    }

                    continue;
                }

                if (ConditionsPass(option.Conditions, evaluateCondition))
                {
                    return option;
                }
            }

            if (firstUnconditional != null)
            {
                return firstUnconditional;
            }

            return firstOption;
        }

        public static AnimationClip ResolveClip(
            FusionAnimatorGraphAsset graph,
            FusionAnimatorClipSlot slot,
            Func<FusionAnimatorConditionDefinition, bool> evaluateCondition = null,
            Func<string, int?> resolveIntParameter = null)
        {
            if (slot == null)
            {
                return null;
            }

            if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
            {
                FusionAnimatorClipBindingSlot selected = ResolveBindingClipSlot(graph, slot.BindingId, evaluateCondition, resolveIntParameter);
                return selected != null ? selected.Clip : null;
            }

            return slot.Clip;
        }

        public static AnimationClip ResolveClip(
            FusionAnimatorGraphAsset graph,
            FusionAnimatorBlendTreeChild child,
            Func<FusionAnimatorConditionDefinition, bool> evaluateCondition = null,
            Func<string, int?> resolveIntParameter = null)
        {
            if (child == null)
            {
                return null;
            }

            if (child.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
            {
                FusionAnimatorClipBindingSlot selected = ResolveBindingClipSlot(graph, child.BindingId, evaluateCondition, resolveIntParameter);
                return selected != null ? selected.Clip : null;
            }

            return child.Clip;
        }

        public static float ResolveSpeed(
            FusionAnimatorGraphAsset graph,
            FusionAnimatorClipSlot slot,
            Func<FusionAnimatorConditionDefinition, bool> evaluateCondition = null,
            Func<string, int?> resolveIntParameter = null)
        {
            if (slot == null)
            {
                return 1.0f;
            }

            if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
            {
                FusionAnimatorClipBindingSlot selected = ResolveBindingClipSlot(graph, slot.BindingId, evaluateCondition, resolveIntParameter);
                if (selected != null)
                {
                    return selected.Speed;
                }
            }

            return slot.Speed;
        }

        public static bool ResolveLoop(
            FusionAnimatorGraphAsset graph,
            FusionAnimatorClipSlot slot,
            Func<FusionAnimatorConditionDefinition, bool> evaluateCondition = null,
            Func<string, int?> resolveIntParameter = null)
        {
            if (slot == null)
            {
                return true;
            }

            if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
            {
                FusionAnimatorClipBindingSlot selected = ResolveBindingClipSlot(graph, slot.BindingId, evaluateCondition, resolveIntParameter);
                if (selected != null)
                {
                    return selected.Loop;
                }
            }

            return slot.Loop;
        }

        private static bool HasConditions(List<FusionAnimatorConditionDefinition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < conditions.Count; ++i)
            {
                if (conditions[i] != null)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool ConditionsPass(
            List<FusionAnimatorConditionDefinition> conditions,
            Func<FusionAnimatorConditionDefinition, bool> evaluateCondition)
        {
            if (HasConditions(conditions) == false)
            {
                return true;
            }

            if (evaluateCondition == null)
            {
                return false;
            }

            for (int i = 0; i < conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = conditions[i];
                if (condition != null && evaluateCondition(condition) == false)
                {
                    return false;
                }
            }

            return true;
        }
    }

    public static class FusionAnimatorParameterReferenceUtility
    {
        private const string ComponentSeparator = ".";
        private const string XSuffix = ".x";
        private const string YSuffix = ".y";

        public static bool TryParse(
            string parameterReference,
            out string parameterId,
            out FusionAnimatorParameterComponent component)
        {
            parameterId = string.Empty;
            component = FusionAnimatorParameterComponent.None;
            if (string.IsNullOrWhiteSpace(parameterReference))
            {
                return false;
            }

            string trimmed = parameterReference.Trim();
            if (trimmed.EndsWith(XSuffix, StringComparison.OrdinalIgnoreCase))
            {
                parameterId = trimmed.Substring(0, trimmed.Length - XSuffix.Length);
                component = FusionAnimatorParameterComponent.X;
            }
            else if (trimmed.EndsWith(YSuffix, StringComparison.OrdinalIgnoreCase))
            {
                parameterId = trimmed.Substring(0, trimmed.Length - YSuffix.Length);
                component = FusionAnimatorParameterComponent.Y;
            }
            else
            {
                parameterId = trimmed;
            }

            if (string.IsNullOrWhiteSpace(parameterId))
            {
                parameterId = string.Empty;
                component = FusionAnimatorParameterComponent.None;
                return false;
            }

            return true;
        }

        public static string Build(string parameterId, FusionAnimatorParameterComponent component)
        {
            if (string.IsNullOrWhiteSpace(parameterId))
            {
                return string.Empty;
            }

            string trimmed = parameterId.Trim();
            switch (component)
            {
                case FusionAnimatorParameterComponent.X:
                    return trimmed + XSuffix;
                case FusionAnimatorParameterComponent.Y:
                    return trimmed + YSuffix;
                default:
                    return trimmed;
            }
        }

        public static string ResolveDisplayName(
            string parameterName,
            string parameterId,
            FusionAnimatorParameterComponent component)
        {
            string baseName = string.IsNullOrWhiteSpace(parameterName) == false ? parameterName : parameterId;
            if (string.IsNullOrWhiteSpace(baseName))
            {
                return string.Empty;
            }

            switch (component)
            {
                case FusionAnimatorParameterComponent.X:
                    return baseName + ".X";
                case FusionAnimatorParameterComponent.Y:
                    return baseName + ".Y";
                default:
                    return baseName;
            }
        }
    }
}
