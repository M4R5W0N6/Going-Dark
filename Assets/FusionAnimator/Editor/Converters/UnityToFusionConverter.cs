using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FusionAnimator.Editor
{
    internal sealed class UnityToFusionConverter : IFusionAnimatorGraphConverter
    {
        public string Id => "unity-to-fusion-graph";
        public string DisplayName => "Unity Animator -> FusionAnimatorGraph";

        public bool CanConvert(UnityEngine.Object source)
        {
            return source is AnimatorController;
        }

        public bool TryConvert(UnityEngine.Object source, FusionAnimatorGraphAsset target, out string message)
        {
            AnimatorController controller = source as AnimatorController;
            if (controller == null)
            {
                message = "Source is not an AnimatorController.";
                return false;
            }

            if (target == null)
            {
                message = "Target graph is null.";
                return false;
            }

            if (target.Parameters == null) target.Parameters = new List<FusionAnimatorParameterDefinition>();
            if (target.ClipBindings == null) target.ClipBindings = new List<FusionAnimatorClipBindingDefinition>();
            if (target.Layers == null) target.Layers = new List<FusionAnimatorLayerDefinition>();
            if (target.States == null) target.States = new List<FusionAnimatorStateDefinition>();
            if (target.Transitions == null) target.Transitions = new List<FusionAnimatorTransitionDefinition>();

            target.Parameters.Clear();
            target.ClipBindings.Clear();
            target.Layers.Clear();
            target.States.Clear();
            target.Transitions.Clear();

            var parameterIdByName = new Dictionary<string, string>(StringComparer.Ordinal);
            var stateIdByState = new Dictionary<AnimatorState, string>();
            int stateFallbackIndex = 0;
            int transitionFallbackIndex = 0;
            int transitionPriority = 0;
            string entryStateId = null;

            AnimatorControllerParameter[] controllerParameters = controller.parameters;
            for (int i = 0, count = controllerParameters.Length; i < count; ++i)
            {
                AnimatorControllerParameter src = controllerParameters[i];
                string id = BuildStableIdFromName("param", src.name, i);
                var parameter = new FusionAnimatorParameterDefinition
                {
                    Id = id,
                    Name = src.name,
                    Type = ConvertParameterType(src.type),
                    DefaultBool = src.defaultBool,
                    DefaultInt = src.defaultInt,
                    DefaultFloat = src.defaultFloat,
                    DefaultVector2 = Vector2.zero,
                };

                target.Parameters.Add(parameter);
                if (!string.IsNullOrWhiteSpace(src.name) && !parameterIdByName.ContainsKey(src.name))
                {
                    parameterIdByName.Add(src.name, parameter.Id);
                }
            }

            AnimatorControllerLayer[] controllerLayers = controller.layers;
            for (int i = 0, count = controllerLayers.Length; i < count; ++i)
            {
                AnimatorControllerLayer srcLayer = controllerLayers[i];
                string layerId = BuildStableId("layer", srcLayer.stateMachine, srcLayer.name, i);

                target.Layers.Add(new FusionAnimatorLayerDefinition
                {
                    Id = layerId,
                    Name = srcLayer.name,
                    Priority = i,
                    DefaultWeight = srcLayer.defaultWeight,
                    EnabledByDefault = true,
                    BlendMode = srcLayer.blendingMode == AnimatorLayerBlendingMode.Additive ? FusionAnimatorLayerBlendMode.Additive : FusionAnimatorLayerBlendMode.Override,
                    AvatarMask = srcLayer.avatarMask,
                    SyncedLayerIndex = srcLayer.syncedLayerIndex,
                    SyncTiming = srcLayer.syncedLayerAffectsTiming,
                    IKPass = srcLayer.iKPass,
                });

                CollectStatesRecursive(
                    srcLayer.stateMachine,
                    layerId,
                    Vector2.zero,
                    string.Empty,
                    target.States,
                    stateIdByState,
                    ref stateFallbackIndex,
                    ref entryStateId,
                    setEntryFromDefaultState: i == 0);
            }

            RemapBlendTreeParameterIds(target.States, parameterIdByName);

            for (int i = 0, count = controllerLayers.Length; i < count; ++i)
            {
                AnimatorControllerLayer srcLayer = controllerLayers[i];
                string layerId = target.Layers[i].Id;
                CollectTransitionsRecursive(
                    srcLayer.stateMachine,
                    target.Transitions,
                    parameterIdByName,
                    stateIdByState,
                    ref transitionFallbackIndex,
                    ref transitionPriority);
            }

            string resolvedRootEntryStateId = ResolveRootEntryStateId(controller, stateIdByState);
            if (string.IsNullOrWhiteSpace(resolvedRootEntryStateId) == false)
            {
                entryStateId = resolvedRootEntryStateId;
            }

            PruneEmptyPlaceholderStates(target, ref entryStateId);

            if (string.IsNullOrWhiteSpace(entryStateId) && target.States.Count > 0)
            {
                entryStateId = target.States[0].Id;
            }

            target.DisplayName = controller.name;
            if (string.IsNullOrWhiteSpace(target.GraphId))
            {
                target.GraphId = FusionAnimatorGraphAsset.NewId("graph");
            }

            target.EntryStateId = entryStateId ?? string.Empty;
            SetSpecialNodePositions(target);

            string resolvedDefaultStateName = string.Empty;
            if (string.IsNullOrWhiteSpace(target.EntryStateId) == false)
            {
                for (int i = 0; i < target.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = target.States[i];
                    if (state != null && string.Equals(state.Id, target.EntryStateId, StringComparison.Ordinal))
                    {
                        resolvedDefaultStateName = state.Name;
                        break;
                    }
                }
            }

            message = string.Format(
                "Converted '{0}' -> Parameters={1}, Layers={2}, States={3}, Transitions={4}, Entry='{5}'",
                controller.name,
                target.Parameters.Count,
                target.Layers.Count,
                target.States.Count,
                target.Transitions.Count,
                string.IsNullOrWhiteSpace(resolvedDefaultStateName) ? target.EntryStateId : resolvedDefaultStateName);
            return true;
        }

        private static void CollectStatesRecursive(
            AnimatorStateMachine stateMachine,
            string layerId,
            Vector2 offset,
            string stateMachinePath,
            List<FusionAnimatorStateDefinition> states,
            Dictionary<AnimatorState, string> stateIdByState,
            ref int fallbackStateIndex,
            ref string entryStateId,
            bool setEntryFromDefaultState)
        {
            ChildAnimatorState[] childStates = stateMachine.states;
            for (int i = 0, count = childStates.Length; i < count; ++i)
            {
                ChildAnimatorState child = childStates[i];
                AnimatorState state = child.state;
                if (state == null)
                {
                    continue;
                }

                if (!stateIdByState.TryGetValue(state, out string stateId))
                {
                    stateId = BuildStableId("state", state, state.name, fallbackStateIndex++);
                    stateIdByState.Add(state, stateId);
                    Vector2 childPosition = new Vector2(child.position.x, child.position.y);
                    string qualifiedName = string.IsNullOrWhiteSpace(stateMachinePath)
                        ? state.name
                        : string.Format("{0}/{1}", stateMachinePath, state.name);
                    states.Add(BuildStateDefinition(state, stateId, qualifiedName, layerId, offset + childPosition));
                }

                if (setEntryFromDefaultState && stateMachine.defaultState == state && string.IsNullOrWhiteSpace(entryStateId))
                {
                    entryStateId = stateId;
                }
            }

            ChildAnimatorStateMachine[] childMachines = stateMachine.stateMachines;
            for (int i = 0, count = childMachines.Length; i < count; ++i)
            {
                ChildAnimatorStateMachine childMachine = childMachines[i];
                if (childMachine.stateMachine == null)
                {
                    continue;
                }

                CollectStatesRecursive(
                    childMachine.stateMachine,
                    layerId,
                    offset + new Vector2(childMachine.position.x, childMachine.position.y),
                    string.IsNullOrWhiteSpace(stateMachinePath)
                        ? childMachine.stateMachine.name
                        : string.Format("{0}/{1}", stateMachinePath, childMachine.stateMachine.name),
                    states,
                    stateIdByState,
                    ref fallbackStateIndex,
                    ref entryStateId,
                    setEntryFromDefaultState: false);
            }
        }

        private static FusionAnimatorStateDefinition BuildStateDefinition(
            AnimatorState state,
            string stateId,
            string stateName,
            string layerId,
            Vector2 nodePosition)
        {
            var result = new FusionAnimatorStateDefinition
            {
                Id = stateId,
                Name = stateName,
                LayerId = layerId,
                NodePosition = nodePosition,
                MinDurationSeconds = 0.0f,
                CanTransitionOut = true,
                WriteDefaults = state.writeDefaultValues,
                MotionType = FusionAnimatorMotionType.Clip,
                Clips = new List<FusionAnimatorClipSlot>(1),
                BlendTree = new FusionAnimatorBlendTreeDefinition(),
            };

            AnimationClip clip = state.motion as AnimationClip;
            BlendTree blendTree = state.motion as BlendTree;

            if (blendTree != null)
            {
                result.MotionType = FusionAnimatorMotionType.BlendTree;
                result.BlendTree = ConvertBlendTree(blendTree);
                result.Clips.Add(new FusionAnimatorClipSlot
                {
                    Slot = "Default",
                    Clip = null,
                    Speed = state.speed,
                    Loop = true,
                });
            }
            else
            {
                result.MotionType = FusionAnimatorMotionType.Clip;
                result.Clips.Add(new FusionAnimatorClipSlot
                {
                    Slot = "Default",
                    Clip = clip,
                    Speed = state.speed,
                    Loop = true,
                });
            }

            return result;
        }

        private static FusionAnimatorBlendTreeDefinition ConvertBlendTree(BlendTree blendTree)
        {
            var result = new FusionAnimatorBlendTreeDefinition
            {
                Type = ConvertBlendTreeType(blendTree.blendType),
                ParameterXId = blendTree.blendParameter,
                ParameterYId = blendTree.blendParameterY,
                ParameterVector2Id = string.Empty,
                PoseTimeParameterId = string.Empty,
                DirectBlendParameterId = blendTree.blendParameter,
                NormalizeTimeScale = true,
                Children = new List<FusionAnimatorBlendTreeChild>(),
            };

            ChildMotion[] children = blendTree.children;
            for (int i = 0, count = children.Length; i < count; ++i)
            {
                ChildMotion child = children[i];
                Motion childMotion = child.motion;
                AnimationClip childClip = childMotion as AnimationClip;
                if (childClip == null && childMotion is BlendTree nestedTree)
                {
                    childClip = FindFirstAnimationClipRecursive(nestedTree);
                }

                result.Children.Add(new FusionAnimatorBlendTreeChild
                {
                    Name = childMotion != null ? childMotion.name : "Motion",
                    Clip = childClip,
                    Threshold = child.threshold,
                    Position = child.position,
                    DirectParameterId = child.directBlendParameter,
                    TimeScale = Mathf.Approximately(child.timeScale, 0.0f) ? 1.0f : child.timeScale,
                });
            }

            return result;
        }

        private static AnimationClip FindFirstAnimationClipRecursive(BlendTree blendTree)
        {
            if (blendTree == null)
            {
                return null;
            }

            ChildMotion[] children = blendTree.children;
            for (int i = 0, count = children.Length; i < count; ++i)
            {
                Motion motion = children[i].motion;
                if (motion is AnimationClip clip)
                {
                    return clip;
                }

                if (motion is BlendTree nestedTree)
                {
                    AnimationClip nestedClip = FindFirstAnimationClipRecursive(nestedTree);
                    if (nestedClip != null)
                    {
                        return nestedClip;
                    }
                }
            }

            return null;
        }

        private static void RemapBlendTreeParameterIds(
            List<FusionAnimatorStateDefinition> states,
            Dictionary<string, string> parameterIdByName)
        {
            if (states == null || parameterIdByName == null || parameterIdByName.Count == 0)
            {
                return;
            }

            for (int i = 0, count = states.Count; i < count; ++i)
            {
                FusionAnimatorStateDefinition state = states[i];
                if (state == null || state.MotionType != FusionAnimatorMotionType.BlendTree || state.BlendTree == null)
                {
                    continue;
                }

                state.BlendTree.ParameterXId = ResolveParameterId(state.BlendTree.ParameterXId, parameterIdByName);
                state.BlendTree.ParameterYId = ResolveParameterId(state.BlendTree.ParameterYId, parameterIdByName);
                state.BlendTree.ParameterVector2Id = ResolveParameterId(state.BlendTree.ParameterVector2Id, parameterIdByName);
                state.BlendTree.PoseTimeParameterId = ResolveParameterId(state.BlendTree.PoseTimeParameterId, parameterIdByName);
                state.BlendTree.DirectBlendParameterId = ResolveParameterId(state.BlendTree.DirectBlendParameterId, parameterIdByName);

                List<FusionAnimatorBlendTreeChild> children = state.BlendTree.Children;
                if (children == null)
                {
                    continue;
                }

                for (int childIndex = 0, childCount = children.Count; childIndex < childCount; ++childIndex)
                {
                    FusionAnimatorBlendTreeChild child = children[childIndex];
                    if (child == null)
                    {
                        continue;
                    }

                    child.DirectParameterId = ResolveParameterId(child.DirectParameterId, parameterIdByName);
                }
            }
        }

        private static void PruneEmptyPlaceholderStates(FusionAnimatorGraphAsset graph, ref string entryStateId)
        {
            if (graph == null || graph.States == null || graph.States.Count == 0)
            {
                return;
            }

            HashSet<string> removedStateIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = graph.States.Count - 1; i >= 0; --i)
            {
                FusionAnimatorStateDefinition state = graph.States[i];
                if (state == null)
                {
                    continue;
                }

                string leafName = GetLeafStateName(state.Name);
                bool isPlaceholder = string.Equals(leafName, "New State", StringComparison.OrdinalIgnoreCase);
                if (!isPlaceholder)
                {
                    continue;
                }

                bool hasClip = false;
                if (state.Clips != null)
                {
                    for (int clipIndex = 0, clipCount = state.Clips.Count; clipIndex < clipCount; ++clipIndex)
                    {
                        FusionAnimatorClipSlot slot = state.Clips[clipIndex];
                        if (slot != null && slot.Clip != null)
                        {
                            hasClip = true;
                            break;
                        }
                    }
                }

                bool hasBlendChildren = state.MotionType == FusionAnimatorMotionType.BlendTree &&
                                        state.BlendTree != null &&
                                        state.BlendTree.Children != null &&
                                        state.BlendTree.Children.Count > 0;

                if (!hasClip && !hasBlendChildren)
                {
                    if (!string.IsNullOrWhiteSpace(state.Id))
                    {
                        removedStateIds.Add(state.Id);
                    }

                    graph.States.RemoveAt(i);
                }
            }

            if (removedStateIds.Count == 0)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(entryStateId) && removedStateIds.Contains(entryStateId))
            {
                entryStateId = null;
            }

            if (graph.Transitions != null)
            {
                for (int i = graph.Transitions.Count - 1; i >= 0; --i)
                {
                    FusionAnimatorTransitionDefinition transition = graph.Transitions[i];
                    if (transition == null)
                    {
                        continue;
                    }

                    bool remove = (!string.IsNullOrWhiteSpace(transition.FromStateId) && removedStateIds.Contains(transition.FromStateId)) ||
                                  (!string.IsNullOrWhiteSpace(transition.ToStateId) && removedStateIds.Contains(transition.ToStateId));
                    if (remove)
                    {
                        graph.Transitions.RemoveAt(i);
                    }
                }
            }
        }

        private static string GetLeafStateName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return string.Empty;
            }

            int separator = stateName.LastIndexOf('/');
            return separator >= 0 ? stateName.Substring(separator + 1).Trim() : stateName.Trim();
        }

        private static string ResolveParameterId(string maybeNameOrId, Dictionary<string, string> parameterIdByName)
        {
            if (string.IsNullOrWhiteSpace(maybeNameOrId))
            {
                return string.Empty;
            }

            if (parameterIdByName.TryGetValue(maybeNameOrId, out string mappedId))
            {
                return mappedId;
            }

            return maybeNameOrId;
        }

        private static void CollectTransitionsRecursive(
            AnimatorStateMachine stateMachine,
            List<FusionAnimatorTransitionDefinition> transitionsOut,
            Dictionary<string, string> parameterIdByName,
            Dictionary<AnimatorState, string> stateIdByState,
            ref int fallbackTransitionIndex,
            ref int priority)
        {
            ChildAnimatorState[] childStates = stateMachine.states;
            for (int i = 0, count = childStates.Length; i < count; ++i)
            {
                ChildAnimatorState child = childStates[i];
                AnimatorState fromState = child.state;
                if (fromState == null || !stateIdByState.TryGetValue(fromState, out string fromId))
                {
                    continue;
                }

                AnimatorStateTransition[] transitions = fromState.transitions;
                for (int t = 0, transitionCount = transitions.Length; t < transitionCount; ++t)
                {
                    AnimatorStateTransition transition = transitions[t];
                    FusionAnimatorTransitionDefinition mapped = BuildTransition(
                        transition,
                        fromId,
                        parameterIdByName,
                        stateIdByState,
                        fallbackTransitionIndex++,
                        priority++);

                    if (mapped != null)
                    {
                        transitionsOut.Add(mapped);
                    }
                }
            }

            AnimatorStateTransition[] anyStateTransitions = stateMachine.anyStateTransitions;
            for (int i = 0, count = anyStateTransitions.Length; i < count; ++i)
            {
                AnimatorStateTransition transition = anyStateTransitions[i];
                FusionAnimatorTransitionDefinition mapped = BuildTransition(
                    transition,
                    FusionAnimatorGraphAsset.SpecialNodeAnyId,
                    parameterIdByName,
                    stateIdByState,
                    fallbackTransitionIndex++,
                    priority++);

                if (mapped != null)
                {
                    transitionsOut.Add(mapped);
                }
            }

            AnimatorTransition[] entryTransitions = stateMachine.entryTransitions;
            bool addedExplicitEntryTransition = false;
            for (int i = 0, count = entryTransitions.Length; i < count; ++i)
            {
                AnimatorTransition entry = entryTransitions[i];
                if (entry == null)
                {
                    continue;
                }

                string toId = ResolveDestinationStateId(entry.destinationState, entry.destinationStateMachine, stateIdByState);
                if (string.IsNullOrWhiteSpace(toId))
                {
                    continue;
                }

                transitionsOut.Add(new FusionAnimatorTransitionDefinition
                {
                    Id = BuildStableId("transition", entry, entry.name, fallbackTransitionIndex++),
                    Name = string.IsNullOrWhiteSpace(entry.name) ? "Entry" : entry.name,
                    FromStateId = FusionAnimatorGraphAsset.SpecialNodeEntryId,
                    ToStateId = toId,
                    Priority = priority++,
                    Mute = false,
                    Solo = false,
                    HasExitTime = false,
                    ExitTimeNormalized = 1.0f,
                    StartOffsetNormalized = 0.0f,
                    FixedDuration = true,
                    BlendDurationSeconds = 0.0f,
                    InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext,
                    CanInterrupt = true,
                    Conditions = new List<FusionAnimatorConditionDefinition>(),
                });
                addedExplicitEntryTransition = true;
            }

            if (addedExplicitEntryTransition == false &&
                stateMachine.defaultState != null &&
                stateIdByState.TryGetValue(stateMachine.defaultState, out string implicitDefaultStateId) &&
                string.IsNullOrWhiteSpace(implicitDefaultStateId) == false)
            {
                transitionsOut.Add(new FusionAnimatorTransitionDefinition
                {
                    Id = BuildStableId("transition", stateMachine, "ImplicitEntry", fallbackTransitionIndex++),
                    Name = "Entry",
                    FromStateId = FusionAnimatorGraphAsset.SpecialNodeEntryId,
                    ToStateId = implicitDefaultStateId,
                    Priority = priority++,
                    Mute = false,
                    Solo = false,
                    HasExitTime = false,
                    ExitTimeNormalized = 1.0f,
                    StartOffsetNormalized = 0.0f,
                    FixedDuration = true,
                    BlendDurationSeconds = 0.0f,
                    InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext,
                    CanInterrupt = true,
                    Conditions = new List<FusionAnimatorConditionDefinition>(),
                });
            }

            ChildAnimatorStateMachine[] childMachines = stateMachine.stateMachines;
            for (int i = 0, count = childMachines.Length; i < count; ++i)
            {
                ChildAnimatorStateMachine childMachine = childMachines[i];
                if (childMachine.stateMachine == null)
                {
                    continue;
                }

                CollectTransitionsRecursive(
                    childMachine.stateMachine,
                    transitionsOut,
                    parameterIdByName,
                    stateIdByState,
                    ref fallbackTransitionIndex,
                    ref priority);
            }
        }

        private static FusionAnimatorTransitionDefinition BuildTransition(
            AnimatorStateTransition transition,
            string fromStateId,
            Dictionary<string, string> parameterIdByName,
            Dictionary<AnimatorState, string> stateIdByState,
            int fallbackIndex,
            int priority)
        {
            if (transition == null)
            {
                return null;
            }

            string toStateId = ResolveDestinationStateId(transition.destinationState, transition.destinationStateMachine, stateIdByState);
            if (string.IsNullOrWhiteSpace(toStateId))
            {
                if (transition.isExit)
                {
                    toStateId = FusionAnimatorGraphAsset.SpecialNodeExitId;
                }
                else
                {
                    return null;
                }
            }

            var mappedConditions = new List<FusionAnimatorConditionDefinition>();
            AnimatorCondition[] conditions = transition.conditions;
            for (int i = 0, count = conditions.Length; i < count; ++i)
            {
                AnimatorCondition condition = conditions[i];
                if (string.IsNullOrWhiteSpace(condition.parameter))
                {
                    continue;
                }

                if (!parameterIdByName.TryGetValue(condition.parameter, out string parameterId))
                {
                    continue;
                }

                FusionAnimatorConditionOperator op = ConvertConditionMode(condition.mode);
                mappedConditions.Add(new FusionAnimatorConditionDefinition
                {
                    ParameterId = parameterId,
                    Operator = op,
                    BoolValue = op == FusionAnimatorConditionOperator.IsTrue,
                    IntValue = Mathf.RoundToInt(condition.threshold),
                    FloatValue = condition.threshold,
                    Vector2Value = new Vector2(condition.threshold, 0.0f),
                });
            }

            return new FusionAnimatorTransitionDefinition
            {
                Id = BuildStableId("transition", transition, transition.name, fallbackIndex),
                Name = string.IsNullOrWhiteSpace(transition.name) ? "Transition" : transition.name,
                FromStateId = fromStateId,
                ToStateId = toStateId,
                Priority = priority,
                Mute = transition.mute,
                Solo = transition.solo,
                HasExitTime = transition.hasExitTime,
                ExitTimeNormalized = transition.exitTime,
                StartOffsetNormalized = transition.offset,
                FixedDuration = transition.hasFixedDuration,
                BlendDurationSeconds = transition.duration,
                InterruptionSource = ConvertInterruptionSource(transition.interruptionSource),
                CanInterrupt = transition.orderedInterruption,
                Conditions = mappedConditions,
            };
        }

        private static string ResolveDestinationStateId(
            AnimatorState destinationState,
            AnimatorStateMachine destinationStateMachine,
            Dictionary<AnimatorState, string> stateIdByState)
        {
            if (destinationState != null && stateIdByState.TryGetValue(destinationState, out string destinationStateId))
            {
                return destinationStateId;
            }

            if (destinationStateMachine != null && destinationStateMachine.defaultState != null &&
                stateIdByState.TryGetValue(destinationStateMachine.defaultState, out string defaultStateId))
            {
                return defaultStateId;
            }

            return null;
        }

        private static FusionAnimatorParameterType ConvertParameterType(AnimatorControllerParameterType type)
        {
            switch (type)
            {
                case AnimatorControllerParameterType.Bool:
                    return FusionAnimatorParameterType.Bool;
                case AnimatorControllerParameterType.Int:
                    return FusionAnimatorParameterType.Int;
                case AnimatorControllerParameterType.Trigger:
                    return FusionAnimatorParameterType.Trigger;
                case AnimatorControllerParameterType.Float:
                default:
                    return FusionAnimatorParameterType.Float;
            }
        }

        private static FusionAnimatorConditionOperator ConvertConditionMode(AnimatorConditionMode mode)
        {
            switch (mode)
            {
                case AnimatorConditionMode.If:
                    return FusionAnimatorConditionOperator.IsTrue;
                case AnimatorConditionMode.IfNot:
                    return FusionAnimatorConditionOperator.IsFalse;
                case AnimatorConditionMode.Greater:
                    return FusionAnimatorConditionOperator.Greater;
                case AnimatorConditionMode.Less:
                    return FusionAnimatorConditionOperator.Less;
                case AnimatorConditionMode.Equals:
                    return FusionAnimatorConditionOperator.Equal;
                case AnimatorConditionMode.NotEqual:
                    return FusionAnimatorConditionOperator.NotEqual;
                default:
                    return FusionAnimatorConditionOperator.IsTrue;
            }
        }

        private static string ResolveRootEntryStateId(
            AnimatorController controller,
            Dictionary<AnimatorState, string> stateIdByState)
        {
            if (controller == null || controller.layers == null || controller.layers.Length == 0)
            {
                return null;
            }

            AnimatorStateMachine rootStateMachine = controller.layers[0].stateMachine;
            if (rootStateMachine == null)
            {
                return null;
            }

            if (rootStateMachine.defaultState != null &&
                stateIdByState.TryGetValue(rootStateMachine.defaultState, out string mappedDefaultStateId) &&
                string.IsNullOrWhiteSpace(mappedDefaultStateId) == false)
            {
                return mappedDefaultStateId;
            }

            string firstConditionedDestination = null;
            AnimatorTransition[] entryTransitions = rootStateMachine.entryTransitions;
            for (int i = 0, count = entryTransitions != null ? entryTransitions.Length : 0; i < count; ++i)
            {
                AnimatorTransition entryTransition = entryTransitions[i];
                if (entryTransition == null)
                {
                    continue;
                }

                string destinationStateId = ResolveDestinationStateId(
                    entryTransition.destinationState,
                    entryTransition.destinationStateMachine,
                    stateIdByState);
                if (string.IsNullOrWhiteSpace(destinationStateId))
                {
                    continue;
                }

                bool hasConditions = entryTransition.conditions != null && entryTransition.conditions.Length > 0;
                if (hasConditions == false)
                {
                    return destinationStateId;
                }

                if (string.IsNullOrWhiteSpace(firstConditionedDestination))
                {
                    firstConditionedDestination = destinationStateId;
                }
            }

            return firstConditionedDestination;
        }

        private static FusionAnimatorInterruptionSource ConvertInterruptionSource(TransitionInterruptionSource source)
        {
            switch (source)
            {
                case TransitionInterruptionSource.None:
                    return FusionAnimatorInterruptionSource.None;
                case TransitionInterruptionSource.Source:
                    return FusionAnimatorInterruptionSource.CurrentState;
                case TransitionInterruptionSource.Destination:
                    return FusionAnimatorInterruptionSource.NextState;
                case TransitionInterruptionSource.SourceThenDestination:
                    return FusionAnimatorInterruptionSource.CurrentThenNext;
                case TransitionInterruptionSource.DestinationThenSource:
                    return FusionAnimatorInterruptionSource.NextThenCurrent;
                default:
                    return FusionAnimatorInterruptionSource.CurrentThenNext;
            }
        }

        private static FusionAnimatorBlendTreeType ConvertBlendTreeType(BlendTreeType type)
        {
            switch (type)
            {
                case BlendTreeType.SimpleDirectional2D:
                    return FusionAnimatorBlendTreeType.TwoDSimpleDirectional;
                case BlendTreeType.FreeformDirectional2D:
                    return FusionAnimatorBlendTreeType.TwoDFreeformDirectional;
                case BlendTreeType.FreeformCartesian2D:
                    return FusionAnimatorBlendTreeType.TwoDFreeformCartesian;
                case BlendTreeType.Direct:
                    return FusionAnimatorBlendTreeType.Direct;
                case BlendTreeType.Simple1D:
                default:
                    return FusionAnimatorBlendTreeType.OneD;
            }
        }

        private static string BuildStableId(string prefix, UnityEngine.Object source, string fallbackName, int fallbackIndex)
        {
            if (source != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string guid, out long localId))
            {
                return string.Format("{0}_{1}", prefix, localId < 0 ? ("n" + (-localId)) : localId.ToString());
            }

            string safe = Sanitize(fallbackName);
            if (string.IsNullOrWhiteSpace(safe))
            {
                safe = fallbackIndex.ToString();
            }

            return string.Format("{0}_{1}", prefix, safe);
        }

        private static string BuildStableIdFromName(string prefix, string fallbackName, int fallbackIndex)
        {
            string safe = Sanitize(fallbackName);
            if (string.IsNullOrWhiteSpace(safe))
            {
                safe = fallbackIndex.ToString();
            }

            return string.Format("{0}_{1}", prefix, safe);
        }

        private static string Sanitize(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return string.Empty;
            }

            char[] chars = input.ToLowerInvariant().ToCharArray();
            for (int i = 0, count = chars.Length; i < count; ++i)
            {
                char c = chars[i];
                if ((c >= 'a' && c <= 'z') || (c >= '0' && c <= '9'))
                {
                    continue;
                }

                chars[i] = '_';
            }

            return new string(chars).Trim('_');
        }

        private static void SetSpecialNodePositions(FusionAnimatorGraphAsset graph)
        {
            if (graph.ScopeUtilityNodeLayouts == null)
            {
                graph.ScopeUtilityNodeLayouts = new List<FusionAnimatorScopeUtilityNodeLayout>();
            }
            else
            {
                graph.ScopeUtilityNodeLayouts.Clear();
            }

            if (graph.ScopeTransitionSuppressions == null)
            {
                graph.ScopeTransitionSuppressions = new List<FusionAnimatorScopeTransitionSuppression>();
            }
            else
            {
                graph.ScopeTransitionSuppressions.Clear();
            }

            float minX = 0.0f;
            float minY = 0.0f;
            float maxY = 0.0f;

            bool hasAnyState = false;
            List<FusionAnimatorStateDefinition> states = graph.States;
            for (int i = 0, count = states.Count; i < count; ++i)
            {
                FusionAnimatorStateDefinition state = states[i];
                if (state == null)
                {
                    continue;
                }

                if (!hasAnyState)
                {
                    minX = state.NodePosition.x;
                    minY = state.NodePosition.y;
                    maxY = state.NodePosition.y;
                    hasAnyState = true;
                }
                else
                {
                    minX = Mathf.Min(minX, state.NodePosition.x);
                    minY = Mathf.Min(minY, state.NodePosition.y);
                    maxY = Mathf.Max(maxY, state.NodePosition.y);
                }
            }

            if (!hasAnyState)
            {
                graph.EntryNodePosition = new Vector2(-300.0f, -120.0f);
                graph.AnyNodePosition = new Vector2(-300.0f, 20.0f);
                graph.ExitNodePosition = new Vector2(300.0f, -40.0f);
                return;
            }

            graph.EntryNodePosition = new Vector2(minX - 260.0f, minY - 80.0f);
            graph.AnyNodePosition = new Vector2(minX - 260.0f, maxY + 80.0f);
            graph.ExitNodePosition = new Vector2(minX + 260.0f, minY - 80.0f);
        }
    }
}
