using System;
using System.Collections.Generic;
using UnityEngine;

namespace FusionAnimator
{
    public enum FusionAnimatorValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2,
    }

    public readonly struct FusionAnimatorValidationIssue
    {
        public readonly FusionAnimatorValidationSeverity Severity;
        public readonly string Context;
        public readonly string Message;

        public FusionAnimatorValidationIssue(FusionAnimatorValidationSeverity severity, string context, string message)
        {
            Severity = severity;
            Context = context;
            Message = message;
        }
    }

    public static class FusionAnimatorValidator
    {
        public static List<FusionAnimatorValidationIssue> Validate(FusionAnimatorGraphAsset graph)
        {
            List<FusionAnimatorValidationIssue> issues = new List<FusionAnimatorValidationIssue>();

            if (graph == null)
            {
                issues.Add(new FusionAnimatorValidationIssue(FusionAnimatorValidationSeverity.Error, "Graph", "Graph asset is null."));
                return issues;
            }

            if (string.IsNullOrWhiteSpace(graph.GraphId))
            {
                issues.Add(new FusionAnimatorValidationIssue(FusionAnimatorValidationSeverity.Error, "Graph", "GraphId is missing."));
            }

            if (graph.Layers == null || graph.Layers.Count == 0)
            {
                issues.Add(new FusionAnimatorValidationIssue(FusionAnimatorValidationSeverity.Error, "Layers", "At least one layer is required."));
            }

            if (graph.States == null || graph.States.Count == 0)
            {
                issues.Add(new FusionAnimatorValidationIssue(FusionAnimatorValidationSeverity.Error, "States", "At least one state is required."));
            }

            HashSet<string> layerIds = ValidateUniqueIds(
                issues,
                graph.Layers,
                layer => layer == null ? null : layer.Id,
                layer => layer == null ? "<null>" : layer.Name,
                "Layer");

            HashSet<string> parameterIds = ValidateUniqueIds(
                issues,
                graph.Parameters,
                parameter => parameter == null ? null : parameter.Id,
                parameter => parameter == null ? "<null>" : parameter.Name,
                "Parameter");

            HashSet<string> clipBindingIds = ValidateUniqueIds(
                issues,
                graph.ClipBindings,
                binding => binding == null ? null : binding.Id,
                binding => binding == null ? "<null>" : binding.Name,
                "Clip Binding");

            if (graph.ClipBindings != null)
            {
                for (int bindingIndex = 0; bindingIndex < graph.ClipBindings.Count; ++bindingIndex)
                {
                    FusionAnimatorClipBindingDefinition binding = graph.ClipBindings[bindingIndex];
                    if (binding == null)
                    {
                        continue;
                    }

                    binding.MigrateLegacyClip();
                    string bindingDisplayName = string.IsNullOrWhiteSpace(binding.Name) ? "Binding" : binding.Name;
                    ValidateConditionList(issues, parameterIds, graph, bindingDisplayName, "Binding", binding.Conditions);
                    if (string.IsNullOrWhiteSpace(binding.ClipIndexParameterId) == false)
                    {
                        if (FusionAnimatorParameterReferenceUtility.TryParse(binding.ClipIndexParameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false ||
                            component != FusionAnimatorParameterComponent.None ||
                            parameterIds.Contains(baseParameterId) == false)
                        {
                            issues.Add(new FusionAnimatorValidationIssue(
                                FusionAnimatorValidationSeverity.Error,
                                bindingDisplayName,
                                string.Format("Binding index parameter '{0}' does not exist.", binding.ClipIndexParameterId)));
                        }
                        else
                        {
                            FusionAnimatorParameterDefinition indexParameter = FindParameterById(graph, baseParameterId);
                            if (indexParameter == null || indexParameter.Type != FusionAnimatorParameterType.Int)
                            {
                                issues.Add(new FusionAnimatorValidationIssue(
                                    FusionAnimatorValidationSeverity.Error,
                                    bindingDisplayName,
                                    string.Format("Binding index parameter '{0}' must reference an Int parameter.", binding.ClipIndexParameterId)));
                            }
                        }
                    }

                    bool hasVariantClip = false;
                    if (binding.Clips != null)
                    {
                        for (int optionIndex = 0; optionIndex < binding.Clips.Count; ++optionIndex)
                        {
                            FusionAnimatorClipBindingSlot option = binding.Clips[optionIndex];
                            string optionName = option != null && string.IsNullOrWhiteSpace(option.Slot) == false
                                ? option.Slot
                                : string.Format("Slot {0}", optionIndex + 1);
                            ValidateConditionList(
                                issues,
                                parameterIds,
                                graph,
                                string.Format("{0}/{1}", bindingDisplayName, optionName),
                                "Binding Clip Slot",
                                option != null ? option.Conditions : null);

                            if (option?.Clip != null)
                            {
                                hasVariantClip = true;
                            }
                        }
                    }

                    if (hasVariantClip == false)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            string.IsNullOrWhiteSpace(binding.Name) ? "Binding" : binding.Name,
                            "Binding has no clips configured."));
                    }
                }
            }

            HashSet<string> stateIds = ValidateUniqueIds(
                issues,
                graph.States,
                state => state == null ? null : state.Id,
                state => state == null ? "<null>" : state.Name,
                "State");

            Dictionary<string, FusionAnimatorStateDefinition> statesById = new Dictionary<string, FusionAnimatorStateDefinition>(StringComparer.Ordinal);
            if (graph.States != null)
            {
                for (int stateIndex = 0; stateIndex < graph.States.Count; ++stateIndex)
                {
                    FusionAnimatorStateDefinition state = graph.States[stateIndex];
                    if (state == null || string.IsNullOrWhiteSpace(state.Id) || statesById.ContainsKey(state.Id))
                    {
                        continue;
                    }

                    statesById.Add(state.Id, state);
                }
            }

            ValidateUniqueIds(
                issues,
                graph.Transitions,
                transition => transition == null ? null : transition.Id,
                transition => transition == null ? "<null>" : transition.Name,
                "Transition");

            if (!string.IsNullOrWhiteSpace(graph.EntryStateId) && !stateIds.Contains(graph.EntryStateId))
            {
                issues.Add(new FusionAnimatorValidationIssue(
                    FusionAnimatorValidationSeverity.Error,
                    "EntryState",
                    string.Format("EntryStateId '{0}' does not match any state.", graph.EntryStateId)));
            }

            for (int i = 0, count = graph.States.Count; i < count; ++i)
            {
                FusionAnimatorStateDefinition state = graph.States[i];
                if (state == null)
                {
                    issues.Add(new FusionAnimatorValidationIssue(FusionAnimatorValidationSeverity.Error, "State", string.Format("State index {0} is null.", i)));
                    continue;
                }

                if (IsScopeSentinelState(state))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(state.LayerId))
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        state.Name,
                        "LayerId is missing."));
                }
                else if (!layerIds.Contains(state.LayerId))
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        state.Name,
                        string.Format("LayerId '{0}' does not exist.", state.LayerId)));
                }

                if (state.MotionType == FusionAnimatorMotionType.BlendTree)
                {
                    if (state.BlendTree == null || state.BlendTree.Children == null || state.BlendTree.Children.Count == 0)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "BlendTree has no children configured."));
                    }
                    else
                    {
                        bool hasResolvableClip = false;
                        for (int childIndex = 0; childIndex < state.BlendTree.Children.Count; ++childIndex)
                        {
                            FusionAnimatorBlendTreeChild child = state.BlendTree.Children[childIndex];
                            if (child == null)
                            {
                                continue;
                            }

                            if (child.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
                            {
                                if (string.IsNullOrWhiteSpace(child.BindingId))
                                {
                                    issues.Add(new FusionAnimatorValidationIssue(
                                        FusionAnimatorValidationSeverity.Warning,
                                        state.Name,
                                        string.Format("BlendTree child '{0}' uses Binding mode but has no BindingId.", string.IsNullOrWhiteSpace(child.Name) ? "<unnamed>" : child.Name)));
                                }
                                else if (clipBindingIds.Contains(child.BindingId) == false)
                                {
                                    issues.Add(new FusionAnimatorValidationIssue(
                                        FusionAnimatorValidationSeverity.Warning,
                                        state.Name,
                                        string.Format("BlendTree child '{0}' references missing binding '{1}'.", string.IsNullOrWhiteSpace(child.Name) ? "<unnamed>" : child.Name, child.BindingId)));
                                }
                            }

                            if (FusionAnimatorClipBindingUtility.ResolveClip(graph, child) != null)
                            {
                                hasResolvableClip = true;
                            }
                        }

                        if (hasResolvableClip == false)
                        {
                            issues.Add(new FusionAnimatorValidationIssue(
                                FusionAnimatorValidationSeverity.Warning,
                                state.Name,
                                "BlendTree has no resolvable child clips configured."));
                        }

                        if (state.BlendTree.Type == FusionAnimatorBlendTreeType.DirectionalPoseTime2D)
                        {
                            bool hasDirectionalInput =
                                string.IsNullOrWhiteSpace(state.BlendTree.ParameterVector2Id) == false ||
                                string.IsNullOrWhiteSpace(state.BlendTree.ParameterXId) == false ||
                                string.IsNullOrWhiteSpace(state.BlendTree.ParameterYId) == false;
                            if (hasDirectionalInput == false)
                            {
                                issues.Add(new FusionAnimatorValidationIssue(
                                    FusionAnimatorValidationSeverity.Warning,
                                    state.Name,
                                    "DirectionalPoseTime2D requires directional input parameter(s) (Parameter XY or Parameter X/Y)."));
                            }
                        }
                    }
                }
                else
                {
                    bool hasClip = false;
                    if (state.Clips != null)
                    {
                        for (int clipIndex = 0; clipIndex < state.Clips.Count; ++clipIndex)
                        {
                            FusionAnimatorClipSlot clipSlot = state.Clips[clipIndex];
                            if (clipSlot == null)
                            {
                                continue;
                            }

                            if (clipSlot.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
                            {
                                if (string.IsNullOrWhiteSpace(clipSlot.BindingId))
                                {
                                    issues.Add(new FusionAnimatorValidationIssue(
                                        FusionAnimatorValidationSeverity.Warning,
                                        state.Name,
                                        string.Format("Clip slot '{0}' uses Binding mode but has no BindingId.", string.IsNullOrWhiteSpace(clipSlot.Slot) ? "<unnamed>" : clipSlot.Slot)));
                                }
                                else if (clipBindingIds.Contains(clipSlot.BindingId) == false)
                                {
                                    issues.Add(new FusionAnimatorValidationIssue(
                                        FusionAnimatorValidationSeverity.Warning,
                                        state.Name,
                                        string.Format("Clip slot '{0}' references missing binding '{1}'.", string.IsNullOrWhiteSpace(clipSlot.Slot) ? "<unnamed>" : clipSlot.Slot, clipSlot.BindingId)));
                                }
                            }

                            if (FusionAnimatorClipBindingUtility.ResolveClip(graph, clipSlot) != null)
                            {
                                hasClip = true;
                            }
                        }
                    }

                    if (hasClip == false)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "No clips configured."));
                    }
                }

                ValidateSemanticStateShape(issues, graph, state);
            }

            for (int i = 0, count = graph.Transitions.Count; i < count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = graph.Transitions[i];
                if (transition == null)
                {
                    issues.Add(new FusionAnimatorValidationIssue(FusionAnimatorValidationSeverity.Error, "Transition", string.Format("Transition index {0} is null.", i)));
                    continue;
                }

                if (statesById.TryGetValue(transition.FromStateId, out FusionAnimatorStateDefinition fromState) && IsScopeSentinelState(fromState))
                {
                    continue;
                }

                if (statesById.TryGetValue(transition.ToStateId, out FusionAnimatorStateDefinition toState) && IsScopeSentinelState(toState))
                {
                    continue;
                }

                if (!IsValidFromStateId(transition.FromStateId, stateIds))
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        transition.Name,
                        string.Format("FromStateId '{0}' is invalid.", transition.FromStateId)));
                }

                if (!IsValidToStateId(transition.ToStateId, stateIds))
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        transition.Name,
                        string.Format("ToStateId '{0}' is invalid.", transition.ToStateId)));
                }

                bool isEntryTransition = string.Equals(
                    transition.FromStateId,
                    FusionAnimatorGraphAsset.SpecialNodeEntryId,
                    StringComparison.Ordinal);

                bool hasConditions = transition.Conditions != null && transition.Conditions.Count > 0;
                bool exitTimeGated = transition.HasExitTime == true;
                if (hasConditions == false && isEntryTransition == false && exitTimeGated == false)
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Warning,
                        transition.Name,
                        "Transition has no conditions and will always be eligible."));
                }
                else if (hasConditions == true)
                {
                    for (int conditionIndex = 0; conditionIndex < transition.Conditions.Count; ++conditionIndex)
                    {
                        FusionAnimatorConditionDefinition condition = transition.Conditions[conditionIndex];
                        if (condition == null)
                        {
                            issues.Add(new FusionAnimatorValidationIssue(
                                FusionAnimatorValidationSeverity.Error,
                                transition.Name,
                                string.Format("Condition index {0} is null.", conditionIndex)));
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(condition.ParameterId))
                        {
                            issues.Add(new FusionAnimatorValidationIssue(
                                FusionAnimatorValidationSeverity.Error,
                                transition.Name,
                                string.Format("Condition index {0} has no parameter.", conditionIndex)));
                        }
                        else
                        {
                            if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false ||
                                parameterIds.Contains(baseParameterId) == false)
                            {
                                issues.Add(new FusionAnimatorValidationIssue(
                                    FusionAnimatorValidationSeverity.Error,
                                    transition.Name,
                                    string.Format("Condition parameter '{0}' does not exist.", condition.ParameterId)));
                            }
                            else if (component != FusionAnimatorParameterComponent.None)
                            {
                                FusionAnimatorParameterDefinition baseParameter = FindParameterById(graph, baseParameterId);
                                if (baseParameter == null || baseParameter.Type != FusionAnimatorParameterType.Vector2)
                                {
                                    issues.Add(new FusionAnimatorValidationIssue(
                                        FusionAnimatorValidationSeverity.Error,
                                        transition.Name,
                                        string.Format("Condition parameter '{0}' uses component selection, but '{1}' is not a Vector2 parameter.", condition.ParameterId, baseParameterId)));
                                }
                            }
                        }
                    }
                }
            }

            if (issues.Count == 0)
            {
                issues.Add(new FusionAnimatorValidationIssue(
                    FusionAnimatorValidationSeverity.Info,
                    "Validation",
                    "Graph is valid."));
            }

            return issues;
        }

        private static void ValidateConditionList(
            List<FusionAnimatorValidationIssue> issues,
            HashSet<string> parameterIds,
            FusionAnimatorGraphAsset graph,
            string ownerLabel,
            string ownerType,
            List<FusionAnimatorConditionDefinition> conditions)
        {
            if (conditions == null || conditions.Count == 0)
            {
                return;
            }

            for (int conditionIndex = 0; conditionIndex < conditions.Count; ++conditionIndex)
            {
                FusionAnimatorConditionDefinition condition = conditions[conditionIndex];
                if (condition == null)
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        ownerLabel,
                        string.Format("{0} condition index {1} is null.", ownerType, conditionIndex)));
                    continue;
                }

                if (string.IsNullOrWhiteSpace(condition.ParameterId))
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        ownerLabel,
                        string.Format("{0} condition index {1} has no parameter.", ownerType, conditionIndex)));
                    continue;
                }

                if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false ||
                    parameterIds.Contains(baseParameterId) == false)
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        ownerLabel,
                        string.Format("{0} condition parameter '{1}' does not exist.", ownerType, condition.ParameterId)));
                    continue;
                }

                if (component != FusionAnimatorParameterComponent.None)
                {
                    FusionAnimatorParameterDefinition baseParameter = FindParameterById(graph, baseParameterId);
                    if (baseParameter == null || baseParameter.Type != FusionAnimatorParameterType.Vector2)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Error,
                            ownerLabel,
                            string.Format("{0} condition parameter '{1}' uses component selection, but '{2}' is not a Vector2 parameter.", ownerType, condition.ParameterId, baseParameterId)));
                    }
                }
            }
        }

        private static FusionAnimatorParameterDefinition FindParameterById(FusionAnimatorGraphAsset graph, string parameterId)
        {
            if (graph == null || graph.Parameters == null || string.IsNullOrWhiteSpace(parameterId))
            {
                return null;
            }

            for (int i = 0; i < graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = graph.Parameters[i];
                if (parameter != null && string.Equals(parameter.Id, parameterId, StringComparison.Ordinal))
                {
                    return parameter;
                }
            }

            return null;
        }

        private static bool IsScopeSentinelState(FusionAnimatorStateDefinition state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Name))
            {
                return false;
            }

            int separator = state.Name.LastIndexOf('/');
            string leaf = separator >= 0 ? state.Name.Substring(separator + 1) : state.Name;
            return string.Equals(leaf, FusionAnimatorGraphAsset.ScopeSentinelStateLeafName, StringComparison.Ordinal);
        }

        private static void ValidateSemanticStateShape(List<FusionAnimatorValidationIssue> issues, FusionAnimatorGraphAsset graph, FusionAnimatorStateDefinition state)
        {
            if (issues == null || state == null || state.Presentation == null)
            {
                return;
            }

            switch (state.Presentation.Semantic)
            {
                case FusionAnimatorStateSemantic.LookPose:
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Info,
                        state.Name,
                        "LookPose semantic is legacy. Prefer BlendTree Type DirectionalPoseTime2D for extensible authoring/preview parity."));

                    if (state.MotionType != FusionAnimatorMotionType.BlendTree)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "LookPose semantic expects a BlendTree state."));
                        return;
                    }

                    if (state.BlendTree == null || state.BlendTree.Children == null || state.BlendTree.Children.Count == 0)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "LookPose semantic requires at least one BlendTree child."));
                    }

                    if (state.BlendTree != null &&
                        string.Equals(state.BlendTree.ParameterXId, "param_look_pitch", StringComparison.OrdinalIgnoreCase) == false)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "LookPose semantic should use parameter 'param_look_pitch'."));
                    }

                    break;
                }
                case FusionAnimatorStateSemantic.TurnInPlace:
                {
                    if (state.MotionType != FusionAnimatorMotionType.BlendTree)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "TurnInPlace semantic expects a BlendTree state."));
                        return;
                    }

                    if (state.BlendTree == null || state.BlendTree.Children == null || state.BlendTree.Children.Count < 3)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "TurnInPlace semantic expects idle/left/right blend children."));
                    }
                    else
                    {
                        bool hasLeft = false;
                        bool hasIdle = false;
                        bool hasRight = false;
                        for (int i = 0; i < state.BlendTree.Children.Count; ++i)
                        {
                            FusionAnimatorBlendTreeChild child = state.BlendTree.Children[i];
                            if (child == null)
                            {
                                continue;
                            }

                            float x = Mathf.Abs(child.Position.x) > 0.0001f || Mathf.Abs(child.Position.y) > 0.0001f
                                ? child.Position.x
                                : child.Threshold;

                            if (x < -0.5f)
                            {
                                hasLeft = true;
                            }
                            else if (x > 0.5f)
                            {
                                hasRight = true;
                            }
                            else
                            {
                                hasIdle = true;
                            }
                        }

                        if (hasLeft == false || hasIdle == false || hasRight == false)
                        {
                            issues.Add(new FusionAnimatorValidationIssue(
                                FusionAnimatorValidationSeverity.Warning,
                                state.Name,
                                "TurnInPlace semantic should include left, idle, and right blend children."));
                        }
                    }

                    if (state.BlendTree != null &&
                        string.Equals(state.BlendTree.ParameterXId, "param_turn_direction", StringComparison.OrdinalIgnoreCase) == false)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "TurnInPlace semantic should use parameter 'param_turn_direction'."));
                    }

                    if (state.Presentation.MaxMagnitude <= 0.0001f ||
                        state.Presentation.BlendSpeed <= 0.0001f ||
                        state.Presentation.TurnSpeed <= 0.0001f)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "TurnInPlace semantic requires positive MaxMagnitude, BlendSpeed, and TurnSpeed."));
                    }

                    break;
                }
                case FusionAnimatorStateSemantic.ShootOverlay:
                {
                    if (state.MotionType != FusionAnimatorMotionType.Clip)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "ShootOverlay semantic expects a Clip state."));
                        return;
                    }

                    int configuredClipCount = 0;
                    if (state.Clips != null)
                    {
                        for (int i = 0; i < state.Clips.Count; ++i)
                        {
                            FusionAnimatorClipSlot clipSlot = state.Clips[i];
                            if (clipSlot != null && FusionAnimatorClipBindingUtility.ResolveClip(graph, clipSlot) != null)
                            {
                                ++configuredClipCount;
                            }
                        }
                    }

                    if (configuredClipCount < 2)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "ShootOverlay semantic expects both idle and shoot clips."));
                    }
                    else
                    {
                        bool hasIdle = false;
                        bool hasShoot = false;
                        for (int i = 0; i < state.Clips.Count; ++i)
                        {
                            FusionAnimatorClipSlot clipSlot = state.Clips[i];
                            if (clipSlot == null || string.IsNullOrWhiteSpace(clipSlot.Slot))
                            {
                                continue;
                            }

                            if (string.Equals(clipSlot.Slot, "Idle", StringComparison.OrdinalIgnoreCase))
                            {
                                hasIdle = true;
                            }
                            else if (string.Equals(clipSlot.Slot, "Shoot", StringComparison.OrdinalIgnoreCase))
                            {
                                hasShoot = true;
                            }
                        }

                        if (hasIdle == false || hasShoot == false)
                        {
                            issues.Add(new FusionAnimatorValidationIssue(
                                FusionAnimatorValidationSeverity.Warning,
                                state.Name,
                                "ShootOverlay semantic should include clip slots named 'Idle' and 'Shoot'."));
                        }
                    }

                    if (state.Presentation.OverlayWeight < 0.0f || state.Presentation.OverlayWeight > 1.0f)
                    {
                        issues.Add(new FusionAnimatorValidationIssue(
                            FusionAnimatorValidationSeverity.Warning,
                            state.Name,
                            "ShootOverlay semantic expects OverlayWeight in range [0, 1]."));
                    }

                    break;
                }
            }
        }

        private static bool IsValidFromStateId(string stateId, HashSet<string> stateIds)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                return false;
            }

            if (stateIds.Contains(stateId))
            {
                return true;
            }

            return stateId == FusionAnimatorGraphAsset.SpecialNodeAnyId || stateId == FusionAnimatorGraphAsset.SpecialNodeEntryId;
        }

        private static bool IsValidToStateId(string stateId, HashSet<string> stateIds)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                return false;
            }

            if (stateIds.Contains(stateId))
            {
                return true;
            }

            return stateId == FusionAnimatorGraphAsset.SpecialNodeExitId;
        }

        private static HashSet<string> ValidateUniqueIds<T>(
            List<FusionAnimatorValidationIssue> issues,
            List<T> items,
            Func<T, string> getId,
            Func<T, string> getName,
            string label)
        {
            HashSet<string> ids = new HashSet<string>(StringComparer.Ordinal);

            if (items == null)
            {
                return ids;
            }

            for (int i = 0, count = items.Count; i < count; ++i)
            {
                T item = items[i];
                string id = getId(item);
                string name = getName(item);

                if (string.IsNullOrWhiteSpace(id))
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        label,
                        string.Format("{0} '{1}' is missing Id.", label, name)));
                    continue;
                }

                if (!ids.Add(id))
                {
                    issues.Add(new FusionAnimatorValidationIssue(
                        FusionAnimatorValidationSeverity.Error,
                        label,
                        string.Format("Duplicate {0} Id '{1}'.", label, id)));
                }
            }

            return ids;
        }
    }
}
