using System;
using System.Collections.Generic;
using UnityEngine;

namespace FusionAnimator
{
    public interface IFusionAnimatorStateLogic
    {
        bool CanTransition(
            FusionAnimatorRuntimeEvaluator evaluator,
            FusionAnimatorTransitionDefinition transition,
            FusionAnimatorStateDefinition fromState,
            FusionAnimatorStateDefinition toState,
            IFusionAnimatorParameterSource parameters);

        void OnStateEntered(
            FusionAnimatorRuntimeEvaluator evaluator,
            FusionAnimatorStateDefinition state,
            FusionAnimatorTransitionDefinition viaTransition);

        void OnStateExited(
            FusionAnimatorRuntimeEvaluator evaluator,
            FusionAnimatorStateDefinition state,
            FusionAnimatorTransitionDefinition viaTransition);
    }

    public sealed class FusionAnimatorRuntimeEvaluator
    {
        private readonly FusionAnimatorGraphAsset _graph;
        private readonly Func<FusionAnimatorStateDefinition, bool> _stateFilter;
        private readonly Dictionary<string, FusionAnimatorStateDefinition> _allStatesById = new Dictionary<string, FusionAnimatorStateDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, FusionAnimatorStateDefinition> _statesById = new Dictionary<string, FusionAnimatorStateDefinition>(StringComparer.Ordinal);
        private readonly Dictionary<string, FusionAnimatorParameterDefinition> _parametersById = new Dictionary<string, FusionAnimatorParameterDefinition>(StringComparer.Ordinal);
        private readonly List<FusionAnimatorTransitionDefinition> _anyTransitions = new List<FusionAnimatorTransitionDefinition>(32);
        private readonly Dictionary<string, List<FusionAnimatorTransitionDefinition>> _transitionsByFromStateId = new Dictionary<string, List<FusionAnimatorTransitionDefinition>>(StringComparer.Ordinal);
        private string _defaultStateId;
        private bool _explicitDefaultStateProvided;
        private bool _applyPreviewOnlyResultsThisStep;

        public string CurrentStateId { get; private set; }
        public float CurrentStateElapsed { get; private set; }
        public float CurrentStateTime { get; private set; }
        public string ActiveTransitionId { get; private set; }
        public string BlendFromStateId { get; private set; }
        public float BlendFromStateTime { get; private set; }
        public string BlendToStateId { get; private set; }
        public float BlendDurationSeconds { get; private set; }
        public float BlendElapsedSeconds { get; private set; }
        public bool IsBlending => string.IsNullOrWhiteSpace(BlendFromStateId) == false &&
                                  BlendDurationSeconds > 0.0001f;
        public float BlendAlpha => IsBlending ? Mathf.Clamp01(BlendElapsedSeconds / Mathf.Max(0.0001f, BlendDurationSeconds)) : 1.0f;

        public FusionAnimatorRuntimeEvaluator(
            FusionAnimatorGraphAsset graph,
            Func<FusionAnimatorStateDefinition, bool> stateFilter = null,
            string explicitDefaultStateId = null)
        {
            _graph = graph;
            _stateFilter = stateFilter;
            BuildCache(explicitDefaultStateId);
            Reset(_defaultStateId);
        }

        public FusionAnimatorStateDefinition CurrentState => FindState(CurrentStateId);
        public FusionAnimatorStateDefinition BlendFromState => FindState(BlendFromStateId);
        public FusionAnimatorStateDefinition BlendToState => FindState(BlendToStateId);

        public void Reset(string stateId = null, float startTime = 0.0f)
        {
            string resolved = ResolveInitialStateId(stateId, null, false);
            CurrentStateId = resolved;
            CurrentStateElapsed = 0.0f;
            CurrentStateTime = Mathf.Max(0.0f, startTime);
            ActiveTransitionId = null;
            ClearBlend();
        }

        public void Step(
            float deltaTime,
            IFusionAnimatorParameterSource parameters,
            IFusionAnimatorStateLogic logic = null,
            bool applyPreviewOnlyResults = false)
        {
            _applyPreviewOnlyResultsThisStep = applyPreviewOnlyResults;
            float dt = Mathf.Max(0.0f, deltaTime);

            if (string.IsNullOrWhiteSpace(CurrentStateId))
            {
                if (IsBlending)
                {
                    BlendElapsedSeconds += dt;
                    BlendFromStateTime += dt;
                    if (BlendElapsedSeconds < Mathf.Max(0.0001f, BlendDurationSeconds))
                    {
                        return;
                    }

                    ClearBlend();
                }

                if (TryEnterFromAnyTransitions(parameters, logic))
                {
                    return;
                }

                CurrentStateId = ResolveInitialStateId(null, parameters, false);
                CurrentStateElapsed = 0.0f;
                CurrentStateTime = 0.0f;
                if (string.IsNullOrWhiteSpace(CurrentStateId))
                {
                    ClearBlend();
                    return;
                }
            }

            FusionAnimatorStateDefinition currentState = FindState(CurrentStateId);
            if (currentState == null)
            {
                CurrentStateId = ResolveInitialStateId(null, parameters, false);
                CurrentStateElapsed = 0.0f;
                CurrentStateTime = 0.0f;
                ClearBlend();
                if (string.IsNullOrWhiteSpace(CurrentStateId))
                {
                    return;
                }

                currentState = FindState(CurrentStateId);
                if (currentState == null)
                {
                    return;
                }
            }

            float previousStateTime = CurrentStateTime;
            CurrentStateElapsed += dt;
            CurrentStateTime += dt;
            string currentStateScopePath = NormalizeScopePath(GetStateScopePath(currentState.Name));

            if (IsBlending)
            {
                BlendElapsedSeconds += dt;
                BlendFromStateTime += dt;
                if (BlendElapsedSeconds >= Mathf.Max(0.0001f, BlendDurationSeconds))
                {
                    ClearBlend();
                }
                else
                {
                    return;
                }
            }

            if (currentState.CanTransitionOut == false ||
                CurrentStateElapsed < ResolveStateMinDurationSeconds(currentState, parameters))
            {
                return;
            }

            bool hasSolo = false;
            List<FusionAnimatorTransitionDefinition> localTransitions = GetOrderedTransitionsFromState(CurrentStateId);
            for (int i = 0; i < localTransitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = localTransitions[i];
                if (transition != null && transition.Mute == false && transition.Solo)
                {
                    hasSolo = true;
                    break;
                }
            }

            float currentReferenceLength = ResolveReferenceLengthSeconds(currentState, parameters);
            bool hasReferenceLength = currentReferenceLength > 0.0001f;
            bool currentStateIsLooping = IsStateLooping(currentState, parameters);
            float previousRawNormalizedTime = 0.0f;
            float currentRawNormalizedTime = 0.0f;
            if (hasReferenceLength)
            {
                previousRawNormalizedTime = previousStateTime / currentReferenceLength;
                currentRawNormalizedTime = CurrentStateTime / currentReferenceLength;
            }

            for (int i = 0; i < localTransitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = localTransitions[i];
                if (transition == null || transition.Mute)
                {
                    continue;
                }

                if (ShouldSkipAnyScopeReentryTransition(transition, currentState))
                {
                    continue;
                }

                if (string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal) &&
                    IsAnyTransitionActiveForScope(transition, currentStateScopePath) == false)
                {
                    continue;
                }

                if (hasSolo && transition.Solo == false)
                {
                    continue;
                }

                if (transition.HasExitTime)
                {
                    if (hasReferenceLength == false)
                    {
                        continue;
                    }

                    if (HasCrossedExitThreshold(
                            currentStateIsLooping,
                            previousRawNormalizedTime,
                            currentRawNormalizedTime,
                            transition.ExitTimeNormalized) == false)
                    {
                        continue;
                    }
                }

                if (TransitionConditionsPass(transition, parameters) == false)
                {
                    continue;
                }

                float transitionBlendDuration = ResolveBlendDurationSeconds(transition, currentState, parameters);
                FusionAnimatorStateDefinition nextState = ResolveTransitionTargetState(transition.ToStateId, parameters);
                if (nextState == null)
                {
                    if (string.Equals(transition.ToStateId, FusionAnimatorGraphAsset.SpecialNodeExitId, StringComparison.Ordinal))
                    {
                        ConsumeTriggerConditions(transition, parameters);
                        ApplyTransitionPreviewResults(transition, parameters, _applyPreviewOnlyResultsThisStep);

                        if (logic != null)
                        {
                            logic.OnStateExited(this, currentState, transition);
                        }

                        ActiveTransitionId = transition.Id;
                        if (TryResolveExitContinuationState(currentState, parameters, out FusionAnimatorStateDefinition continuationState, out float continuationStartTime))
                        {
                            if (transitionBlendDuration > 0.0001f)
                            {
                                BlendFromStateId = currentState.Id;
                                BlendFromStateTime = CurrentStateTime;
                                BlendToStateId = continuationState.Id;
                                BlendElapsedSeconds = 0.0f;
                                BlendDurationSeconds = transitionBlendDuration;
                            }
                            else
                            {
                                ClearBlend();
                            }

                            CurrentStateId = continuationState.Id;
                            CurrentStateElapsed = 0.0f;
                            CurrentStateTime = continuationStartTime;

                            if (logic != null)
                            {
                                logic.OnStateEntered(this, continuationState, transition);
                            }

                            return;
                        }

                        if (transitionBlendDuration > 0.0001f)
                        {
                            BlendFromStateId = currentState.Id;
                            BlendFromStateTime = CurrentStateTime;
                            BlendToStateId = string.Empty;
                            BlendElapsedSeconds = 0.0f;
                            BlendDurationSeconds = transitionBlendDuration;
                        }
                        else
                        {
                            ClearBlend();
                        }

                        CurrentStateId = string.Empty;
                        CurrentStateElapsed = 0.0f;
                        CurrentStateTime = 0.0f;
                        return;
                    }

                    continue;
                }

                if (string.Equals(nextState.Id, currentState.Id, StringComparison.Ordinal))
                {
                    continue;
                }

                if (logic != null && logic.CanTransition(this, transition, currentState, nextState, parameters) == false)
                {
                    continue;
                }

                ConsumeTriggerConditions(transition, parameters);
                ApplyTransitionPreviewResults(transition, parameters, _applyPreviewOnlyResultsThisStep);

                float nextReferenceLength = ResolveReferenceLengthSeconds(nextState, parameters);
                float nextStartTime = Mathf.Clamp01(transition.StartOffsetNormalized) * Mathf.Max(0.0f, nextReferenceLength);

                if (logic != null)
                {
                    logic.OnStateExited(this, currentState, transition);
                }

                ActiveTransitionId = transition.Id;
                if (transitionBlendDuration > 0.0001f)
                {
                    BlendFromStateId = currentState.Id;
                    BlendFromStateTime = CurrentStateTime;
                    BlendToStateId = nextState.Id;
                    BlendElapsedSeconds = 0.0f;
                    BlendDurationSeconds = transitionBlendDuration;
                }
                else
                {
                    ClearBlend();
                }

                CurrentStateId = nextState.Id;
                CurrentStateElapsed = 0.0f;
                CurrentStateTime = nextStartTime;

                if (logic != null)
                {
                    logic.OnStateEntered(this, nextState, transition);
                }

                return;
            }
        }

        private bool TryEnterFromAnyTransitions(IFusionAnimatorParameterSource parameters, IFusionAnimatorStateLogic logic)
        {
            if (_anyTransitions == null || _anyTransitions.Count == 0)
            {
                return false;
            }

            const string rootScopePath = "";
            bool hasSolo = false;
            for (int i = 0; i < _anyTransitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _anyTransitions[i];
                if (transition != null && transition.Mute == false && transition.Solo)
                {
                    hasSolo = true;
                    break;
                }
            }

            for (int i = 0; i < _anyTransitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _anyTransitions[i];
                if (transition == null || transition.Mute)
                {
                    continue;
                }

                if (IsAnyTransitionActiveForScope(transition, rootScopePath) == false)
                {
                    continue;
                }

                if (hasSolo && transition.Solo == false)
                {
                    continue;
                }

                if (TransitionConditionsPass(transition, parameters) == false)
                {
                    continue;
                }

                FusionAnimatorStateDefinition nextState = ResolveTransitionTargetState(transition.ToStateId, parameters);
                if (nextState == null)
                {
                    continue;
                }

                ConsumeTriggerConditions(transition, parameters);
                ApplyTransitionPreviewResults(transition, parameters, _applyPreviewOnlyResultsThisStep);

                float nextReferenceLength = ResolveReferenceLengthSeconds(nextState, parameters);
                float nextStartTime = Mathf.Clamp01(transition.StartOffsetNormalized) * Mathf.Max(0.0f, nextReferenceLength);

                ActiveTransitionId = transition.Id;
                CurrentStateId = nextState.Id;
                CurrentStateElapsed = 0.0f;
                CurrentStateTime = nextStartTime;
                ClearBlend();

                if (logic != null)
                {
                    logic.OnStateEntered(this, nextState, transition);
                }

                return true;
            }

            return false;
        }

        private bool TryResolveExitContinuationState(
            FusionAnimatorStateDefinition exitedState,
            IFusionAnimatorParameterSource parameters,
            out FusionAnimatorStateDefinition continuationState,
            out float continuationStartTime)
        {
            continuationState = null;
            continuationStartTime = 0.0f;
            if (exitedState == null)
            {
                return false;
            }

            string currentScope = NormalizeScopePath(GetStateScopePath(exitedState.Name));
            string parentScope = GetParentScopePath(currentScope);

            if (TryResolveAnyTransitionTargetStateForScope(parentScope, parameters, out FusionAnimatorTransitionDefinition anyTransition, out continuationState))
            {
                ConsumeTriggerConditions(anyTransition, parameters);
                ApplyTransitionPreviewResults(anyTransition, parameters, _applyPreviewOnlyResultsThisStep);

                float referenceLength = ResolveReferenceLengthSeconds(continuationState, parameters);
                continuationStartTime = Mathf.Clamp01(anyTransition.StartOffsetNormalized) * Mathf.Max(0.0f, referenceLength);
                return true;
            }

            if (TryResolveEntryTransitionTargetStateIdForScope(
                    parentScope,
                    parameters,
                    out string entryStateId,
                    out _,
                    out _) &&
                _statesById.TryGetValue(entryStateId, out continuationState) &&
                continuationState != null)
            {
                continuationStartTime = 0.0f;
                return true;
            }

            return false;
        }

        private bool TryResolveAnyTransitionTargetStateForScope(
            string scopePath,
            IFusionAnimatorParameterSource parameters,
            out FusionAnimatorTransitionDefinition matchedTransition,
            out FusionAnimatorStateDefinition targetState)
        {
            matchedTransition = null;
            targetState = null;
            if (_anyTransitions == null || _anyTransitions.Count == 0)
            {
                return false;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            bool hasSolo = false;
            for (int i = 0; i < _anyTransitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _anyTransitions[i];
                if (transition == null || transition.Mute)
                {
                    continue;
                }

                if (IsAnyTransitionActiveForScope(transition, normalizedScope) == false)
                {
                    continue;
                }

                if (transition.Solo)
                {
                    hasSolo = true;
                    break;
                }
            }

            for (int i = 0; i < _anyTransitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _anyTransitions[i];
                if (transition == null || transition.Mute)
                {
                    continue;
                }

                if (IsAnyTransitionActiveForScope(transition, normalizedScope) == false)
                {
                    continue;
                }

                if (hasSolo && transition.Solo == false)
                {
                    continue;
                }

                if (TransitionConditionsPass(transition, parameters) == false)
                {
                    continue;
                }

                FusionAnimatorStateDefinition nextState = ResolveTransitionTargetState(transition.ToStateId, parameters);
                if (nextState == null)
                {
                    continue;
                }

                matchedTransition = transition;
                targetState = nextState;
                return true;
            }

            return false;
        }

        public FusionAnimatorStateDefinition FindState(string stateId)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                return null;
            }

            return _statesById.TryGetValue(stateId, out FusionAnimatorStateDefinition state) ? state : null;
        }

        public static bool EvaluateCondition(
            FusionAnimatorConditionDefinition condition,
            FusionAnimatorParameterDefinition parameter,
            IFusionAnimatorParameterSource source,
            bool consumeTrigger = false,
            FusionAnimatorParameterComponent parameterComponent = FusionAnimatorParameterComponent.None)
        {
            if (condition == null || parameter == null)
            {
                return false;
            }

            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                {
                    bool value = parameter.DefaultBool;
                    if (source != null)
                    {
                        source.TryGetBool(parameter.Id, out value);
                    }

                    switch (condition.Operator)
                    {
                        case FusionAnimatorConditionOperator.IsTrue: return value;
                        case FusionAnimatorConditionOperator.IsFalse: return value == false;
                        case FusionAnimatorConditionOperator.Equal: return value == condition.BoolValue;
                        case FusionAnimatorConditionOperator.NotEqual: return value != condition.BoolValue;
                        default: return false;
                    }
                }
                case FusionAnimatorParameterType.Trigger:
                {
                    if (source == null)
                    {
                        return false;
                    }

                    bool fired;
                    if (consumeTrigger)
                    {
                        if (source.TryConsumeTrigger(parameter.Id, out fired))
                        {
                            return fired;
                        }
                    }
                    else
                    {
                        if (source.TryPeekTrigger(parameter.Id, out fired))
                        {
                            return fired;
                        }
                    }

                    return false;
                }
                case FusionAnimatorParameterType.Int:
                {
                    int value = parameter.DefaultInt;
                    if (source != null)
                    {
                        source.TryGetInt(parameter.Id, out value);
                    }

                    float lhs = condition.UseAbsoluteValue ? Mathf.Abs(value) : value;
                    if (condition.Operator == FusionAnimatorConditionOperator.Range)
                    {
                        return CompareNumericRange(lhs, condition.RangeMin, condition.RangeMax);
                    }

                    return CompareNumeric(lhs, condition.IntValue, condition.Operator);
                }
                case FusionAnimatorParameterType.Float:
                {
                    float value = parameter.DefaultFloat;
                    if (source != null)
                    {
                        source.TryGetFloat(parameter.Id, out value);
                    }

                    float lhs = condition.UseAbsoluteValue ? Mathf.Abs(value) : value;
                    if (condition.Operator == FusionAnimatorConditionOperator.Range)
                    {
                        return CompareNumericRange(lhs, condition.RangeMin, condition.RangeMax);
                    }

                    return CompareNumeric(lhs, condition.FloatValue, condition.Operator);
                }
                case FusionAnimatorParameterType.Vector2:
                {
                    Vector2 value = parameter.DefaultVector2;
                    if (source != null)
                    {
                        source.TryGetVector2(parameter.Id, out value);
                    }

                    float magnitude;
                    switch (parameterComponent)
                    {
                        case FusionAnimatorParameterComponent.X:
                            magnitude = value.x;
                            break;
                        case FusionAnimatorParameterComponent.Y:
                            magnitude = value.y;
                            break;
                        default:
                            magnitude = value.magnitude;
                            break;
                    }

                    if (condition.UseAbsoluteValue)
                    {
                        magnitude = Mathf.Abs(magnitude);
                    }

                    switch (condition.Operator)
                    {
                        case FusionAnimatorConditionOperator.IsTrue: return magnitude > 0.000001f;
                        case FusionAnimatorConditionOperator.IsFalse: return magnitude <= 0.000001f;
                        case FusionAnimatorConditionOperator.Range: return CompareNumericRange(magnitude, condition.RangeMin, condition.RangeMax);
                        default: return CompareNumeric(magnitude, condition.FloatValue, condition.Operator);
                    }
                }
                default:
                    return false;
            }
        }

        private void BuildCache(string explicitDefaultStateId)
        {
            _allStatesById.Clear();
            _statesById.Clear();
            _parametersById.Clear();
            _anyTransitions.Clear();
            _transitionsByFromStateId.Clear();
            _defaultStateId = string.Empty;
            _explicitDefaultStateProvided = explicitDefaultStateId != null;

            if (_graph == null)
            {
                return;
            }

            if (_graph.Parameters != null)
            {
                for (int i = 0; i < _graph.Parameters.Count; ++i)
                {
                    FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
                    {
                        continue;
                    }

                    if (_parametersById.ContainsKey(parameter.Id) == false)
                    {
                        _parametersById.Add(parameter.Id, parameter);
                    }
                }
            }

            if (_graph.States != null)
            {
                for (int i = 0; i < _graph.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state == null || string.IsNullOrWhiteSpace(state.Id))
                    {
                        continue;
                    }

                    if (_allStatesById.ContainsKey(state.Id) == false)
                    {
                        _allStatesById.Add(state.Id, state);
                    }

                    if (IsScopeSentinelState(state))
                    {
                        continue;
                    }

                    if (_stateFilter != null && _stateFilter(state) == false)
                    {
                        continue;
                    }

                    if (_statesById.ContainsKey(state.Id) == false)
                    {
                        _statesById.Add(state.Id, state);
                    }
                }
            }

            if (_graph.Transitions != null)
            {
                for (int i = 0; i < _graph.Transitions.Count; ++i)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                    if (transition == null || transition.Mute)
                    {
                        continue;
                    }

                    bool transitionsToExit = string.Equals(transition.ToStateId, FusionAnimatorGraphAsset.SpecialNodeExitId, StringComparison.Ordinal);
                    bool transitionsToPlayableState =
                        _statesById.ContainsKey(transition.ToStateId) ||
                        IsScopeSentinelStateId(transition.ToStateId);
                    if (transitionsToExit == false && transitionsToPlayableState == false)
                    {
                        continue;
                    }

                    if (string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal))
                    {
                        _anyTransitions.Add(transition);
                        continue;
                    }

                    if (string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (_statesById.ContainsKey(transition.FromStateId) == false)
                    {
                        continue;
                    }

                    if (_transitionsByFromStateId.TryGetValue(transition.FromStateId, out List<FusionAnimatorTransitionDefinition> list) == false)
                    {
                        list = new List<FusionAnimatorTransitionDefinition>(8);
                        _transitionsByFromStateId.Add(transition.FromStateId, list);
                    }

                    list.Add(transition);
                }
            }

            if (_anyTransitions.Count > 1)
            {
                _anyTransitions.Sort(CompareTransitionPriority);
            }

            foreach (KeyValuePair<string, List<FusionAnimatorTransitionDefinition>> pair in _transitionsByFromStateId)
            {
                if (pair.Value.Count > 1)
                {
                    pair.Value.Sort(CompareTransitionPriority);
                }
            }

            _defaultStateId = ResolveInitialStateId(explicitDefaultStateId, null, false);
        }

        private string ResolveInitialStateId(string explicitStateId, IFusionAnimatorParameterSource parameters, bool allowFallback)
        {
            if (string.IsNullOrWhiteSpace(explicitStateId) == false && _statesById.ContainsKey(explicitStateId))
            {
                return explicitStateId;
            }

            bool hasEntryTransitions;
            bool hasConditionalEntryTransitions;
            if (TryResolveEntryTransitionTargetStateId(parameters, out string entryStateId, out hasEntryTransitions, out hasConditionalEntryTransitions))
            {
                return entryStateId;
            }

            if (hasEntryTransitions)
            {
                // Explicit entry links define initial-state routing for this scope.
                // If none match (e.g. conditional entries failed), stay unresolved.
                return string.Empty;
            }

            if (_explicitDefaultStateProvided == false &&
                string.IsNullOrWhiteSpace(_graph?.EntryStateId) == false &&
                _statesById.ContainsKey(_graph.EntryStateId))
            {
                return _graph.EntryStateId;
            }

            if (allowFallback == false)
            {
                return string.Empty;
            }

            foreach (KeyValuePair<string, FusionAnimatorStateDefinition> pair in _statesById)
            {
                return pair.Key;
            }

            return string.Empty;
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

        private bool IsScopeSentinelStateId(string stateId)
        {
            return string.IsNullOrWhiteSpace(stateId) == false &&
                   _allStatesById.TryGetValue(stateId, out FusionAnimatorStateDefinition state) &&
                   IsScopeSentinelState(state);
        }

        private bool TryResolveEntryTransitionTargetStateId(
            IFusionAnimatorParameterSource parameters,
            out string stateId,
            out bool hasEntryTransitions,
            out bool hasConditionalEntryTransitions)
        {
            return TryResolveEntryTransitionTargetStateIdForScope(
                string.Empty,
                parameters,
                out stateId,
                out hasEntryTransitions,
                out hasConditionalEntryTransitions);
        }

        private bool TryResolveEntryTransitionTargetStateIdForScope(
            string scopePath,
            IFusionAnimatorParameterSource parameters,
            out string stateId,
            out bool hasEntryTransitions,
            out bool hasConditionalEntryTransitions)
        {
            stateId = string.Empty;
            hasEntryTransitions = false;
            hasConditionalEntryTransitions = false;

            if (_graph?.Transitions == null)
            {
                return false;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            FusionAnimatorTransitionDefinition bestEntry = null;
            string bestEntryTargetStateId = string.Empty;

            for (int i = 0; i < _graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null || transition.Mute)
                {
                    continue;
                }

                if (string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                if (_allStatesById.TryGetValue(transition.ToStateId, out FusionAnimatorStateDefinition rawTargetState) &&
                    IsScopeSentinelState(rawTargetState))
                {
                    continue;
                }

                if (TryResolveTransitionTargetStateId(transition.ToStateId, parameters, out string resolvedTargetStateId) == false)
                {
                    continue;
                }

                if (_statesById.TryGetValue(resolvedTargetStateId, out FusionAnimatorStateDefinition destinationState) == false ||
                    destinationState == null)
                {
                    continue;
                }

                string destinationScope = NormalizeScopePath(GetStateScopePath(destinationState.Name));
                if (string.Equals(destinationScope, normalizedScope, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                hasEntryTransitions = true;

                bool hasConditions = transition.Conditions != null && transition.Conditions.Count > 0;
                if (hasConditions)
                {
                    hasConditionalEntryTransitions = true;
                    if (parameters == null || TransitionConditionsPass(transition, parameters) == false)
                    {
                        continue;
                    }
                }

                if (bestEntry == null || CompareTransitionPriority(transition, bestEntry) < 0)
                {
                    bestEntry = transition;
                    bestEntryTargetStateId = resolvedTargetStateId;
                }
            }

            if (bestEntry == null)
            {
                return false;
            }

            ConsumeTriggerConditions(bestEntry, parameters);
            ApplyTransitionPreviewResults(bestEntry, parameters, _applyPreviewOnlyResultsThisStep);
            stateId = bestEntryTargetStateId;
            return true;
        }

        private FusionAnimatorStateDefinition ResolveTransitionTargetState(string toStateId, IFusionAnimatorParameterSource parameters)
        {
            if (TryResolveTransitionTargetStateId(toStateId, parameters, out string resolvedStateId) == false)
            {
                return null;
            }

            return _statesById.TryGetValue(resolvedStateId, out FusionAnimatorStateDefinition state)
                ? state
                : null;
        }

        private bool TryResolveTransitionTargetStateId(
            string toStateId,
            IFusionAnimatorParameterSource parameters,
            out string resolvedStateId)
        {
            resolvedStateId = string.Empty;
            if (string.IsNullOrWhiteSpace(toStateId))
            {
                return false;
            }

            if (_statesById.ContainsKey(toStateId))
            {
                resolvedStateId = toStateId;
                return true;
            }

            return TryResolveScopeSentinelTargetStateId(toStateId, parameters, out resolvedStateId);
        }

        private bool TryResolveScopeSentinelTargetStateId(
            string sentinelStateId,
            IFusionAnimatorParameterSource parameters,
            out string resolvedStateId)
        {
            resolvedStateId = string.Empty;
            if (string.IsNullOrWhiteSpace(sentinelStateId))
            {
                return false;
            }

            if (_allStatesById.TryGetValue(sentinelStateId, out FusionAnimatorStateDefinition sentinel) == false ||
                IsScopeSentinelState(sentinel) == false)
            {
                return false;
            }

            string sentinelScope = NormalizeScopePath(GetStateScopePath(sentinel.Name));

            if (TryResolveEntryTransitionTargetStateIdForScope(
                    sentinelScope,
                    parameters,
                    out resolvedStateId,
                    out _,
                    out _))
            {
                return true;
            }

            return false;
        }

        private static string GetStateScopePath(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return string.Empty;
            }

            int separator = stateName.LastIndexOf('/');
            if (separator <= 0)
            {
                return string.Empty;
            }

            return stateName.Substring(0, separator);
        }

        private static string NormalizeScopePath(string scopePath)
        {
            if (string.IsNullOrWhiteSpace(scopePath))
            {
                return string.Empty;
            }

            string normalized = scopePath.Trim().Replace('\\', '/');
            while (normalized.Contains("//"))
            {
                normalized = normalized.Replace("//", "/");
            }

            if (normalized.StartsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.TrimStart('/');
            }

            if (normalized.EndsWith("/", StringComparison.Ordinal))
            {
                normalized = normalized.TrimEnd('/');
            }

            return normalized;
        }

        private bool IsAnyTransitionActiveForScope(FusionAnimatorTransitionDefinition transition, string currentScopePath)
        {
            if (transition == null)
            {
                return false;
            }

            if (TryGetAnyTransitionScopePath(transition, out string transitionScopePath) == false)
            {
                return true;
            }

            string normalizedCurrentScope = NormalizeScopePath(currentScopePath);
            if (string.IsNullOrWhiteSpace(transitionScopePath))
            {
                // Root-scope AnyState transitions apply only at root scope.
                return string.IsNullOrWhiteSpace(normalizedCurrentScope);
            }

            // Scoped AnyState transitions are local to their exact scope.
            // This prevents parent-scope AnyState transitions from stealing control while a child sub-state machine is active.
            return string.Equals(normalizedCurrentScope, transitionScopePath, StringComparison.OrdinalIgnoreCase);
        }

        private bool TryGetAnyTransitionScopePath(FusionAnimatorTransitionDefinition transition, out string scopePath)
        {
            scopePath = string.Empty;
            if (transition == null ||
                string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal) == false)
            {
                return false;
            }

            if (_allStatesById.TryGetValue(transition.ToStateId, out FusionAnimatorStateDefinition targetState) == false ||
                targetState == null)
            {
                return false;
            }

            string targetScope = NormalizeScopePath(GetStateScopePath(targetState.Name));
            if (IsScopeSentinelState(targetState))
            {
                scopePath = GetParentScopePath(targetScope);
                return true;
            }

            scopePath = targetScope;
            return true;
        }

        private bool ShouldSkipAnyScopeReentryTransition(
            FusionAnimatorTransitionDefinition transition,
            FusionAnimatorStateDefinition currentState)
        {
            if (transition == null ||
                currentState == null ||
                string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal) == false)
            {
                return false;
            }

            if (_allStatesById.TryGetValue(transition.ToStateId, out FusionAnimatorStateDefinition rawTarget) == false ||
                rawTarget == null ||
                IsScopeSentinelState(rawTarget) == false)
            {
                return false;
            }

            string targetScope = NormalizeScopePath(GetStateScopePath(rawTarget.Name));
            if (string.IsNullOrWhiteSpace(targetScope))
            {
                return false;
            }

            string currentScope = NormalizeScopePath(GetStateScopePath(currentState.Name));
            if (string.IsNullOrWhiteSpace(currentScope))
            {
                return false;
            }

            return string.Equals(currentScope, targetScope, StringComparison.OrdinalIgnoreCase) ||
                   currentScope.StartsWith(targetScope + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string GetParentScopePath(string scopePath)
        {
            string normalized = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            int separator = normalized.LastIndexOf('/');
            return separator >= 0 ? normalized.Substring(0, separator) : string.Empty;
        }

        private List<FusionAnimatorTransitionDefinition> GetOrderedTransitionsFromState(string stateId)
        {
            List<FusionAnimatorTransitionDefinition> merged = new List<FusionAnimatorTransitionDefinition>(16);
            if (_transitionsByFromStateId.TryGetValue(stateId, out List<FusionAnimatorTransitionDefinition> fromStateTransitions))
            {
                merged.AddRange(fromStateTransitions);
            }

            if (_anyTransitions.Count > 0)
            {
                merged.AddRange(_anyTransitions);
            }

            if (merged.Count > 1)
            {
                merged.Sort(CompareTransitionPriority);
            }

            return merged;
        }

        private bool TransitionConditionsPass(FusionAnimatorTransitionDefinition transition, IFusionAnimatorParameterSource parameters)
        {
            if (transition == null || transition.Conditions == null || transition.Conditions.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < transition.Conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = transition.Conditions[i];
                if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
                {
                    return false;
                }

                if (TryResolveConditionParameter(condition.ParameterId, out FusionAnimatorParameterDefinition parameter, out FusionAnimatorParameterComponent component) == false)
                {
                    return false;
                }

                if (EvaluateCondition(condition, parameter, parameters, false, component) == false)
                {
                    return false;
                }
            }

            return true;
        }

        private void ConsumeTriggerConditions(FusionAnimatorTransitionDefinition transition, IFusionAnimatorParameterSource parameters)
        {
            if (transition == null || transition.Conditions == null || transition.Conditions.Count == 0 || parameters == null)
            {
                return;
            }

            for (int i = 0; i < transition.Conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = transition.Conditions[i];
                if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
                {
                    continue;
                }

                if (TryResolveConditionParameter(condition.ParameterId, out FusionAnimatorParameterDefinition parameter, out FusionAnimatorParameterComponent component) == false ||
                    parameter == null ||
                    parameter.Type != FusionAnimatorParameterType.Trigger ||
                    component != FusionAnimatorParameterComponent.None)
                {
                    continue;
                }

                EvaluateCondition(condition, parameter, parameters, true, component);
            }
        }

        private bool TryResolveConditionParameter(
            string parameterReference,
            out FusionAnimatorParameterDefinition parameter,
            out FusionAnimatorParameterComponent component)
        {
            parameter = null;
            component = FusionAnimatorParameterComponent.None;
            if (string.IsNullOrWhiteSpace(parameterReference))
            {
                return false;
            }

            if (FusionAnimatorParameterReferenceUtility.TryParse(parameterReference, out string parameterId, out component) == false)
            {
                return false;
            }

            if (_parametersById.TryGetValue(parameterId, out parameter) == false || parameter == null)
            {
                return false;
            }

            if (component != FusionAnimatorParameterComponent.None && parameter.Type != FusionAnimatorParameterType.Vector2)
            {
                parameter = null;
                component = FusionAnimatorParameterComponent.None;
                return false;
            }

            return true;
        }

        private void ApplyTransitionPreviewResults(
            FusionAnimatorTransitionDefinition transition,
            IFusionAnimatorParameterSource parameters,
            bool applyPreviewOnlyResults)
        {
            if (applyPreviewOnlyResults == false ||
                transition?.PreviewResults == null ||
                transition.PreviewResults.Count == 0 ||
                parameters is FusionAnimatorParameterStore store == false)
            {
                return;
            }

            for (int i = 0; i < transition.PreviewResults.Count; ++i)
            {
                FusionAnimatorTransitionResultDefinition result = transition.PreviewResults[i];
                if (result == null || string.IsNullOrWhiteSpace(result.ParameterId))
                {
                    continue;
                }

                if (TryResolveConditionParameter(result.ParameterId, out FusionAnimatorParameterDefinition parameter, out FusionAnimatorParameterComponent component) == false ||
                    parameter == null ||
                    component != FusionAnimatorParameterComponent.None)
                {
                    continue;
                }

                switch (result.Operation)
                {
                    case FusionAnimatorTransitionResultOperation.Cycle:
                        ApplyCycleResult(store, parameter, result);
                        break;
                    case FusionAnimatorTransitionResultOperation.Set:
                    default:
                        ApplySetResult(store, parameter, result);
                        break;
                }
            }
        }

        private static void ApplySetResult(
            FusionAnimatorParameterStore store,
            FusionAnimatorParameterDefinition parameter,
            FusionAnimatorTransitionResultDefinition result)
        {
            if (store == null || parameter == null || result == null)
            {
                return;
            }

            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                case FusionAnimatorParameterType.Trigger:
                    store.SetBool(parameter.Id, result.BoolValue);
                    break;
                case FusionAnimatorParameterType.Int:
                    store.SetInt(parameter.Id, result.IntValue);
                    break;
                case FusionAnimatorParameterType.Float:
                    store.SetFloat(parameter.Id, result.FloatValue);
                    break;
                case FusionAnimatorParameterType.Vector2:
                    store.SetVector2(parameter.Id, result.Vector2Value);
                    break;
            }
        }

        private static void ApplyCycleResult(
            FusionAnimatorParameterStore store,
            FusionAnimatorParameterDefinition parameter,
            FusionAnimatorTransitionResultDefinition result)
        {
            if (store == null || parameter == null || result == null || parameter.Type != FusionAnimatorParameterType.Int)
            {
                return;
            }

            int min = result.CycleMinValue;
            int max = result.CycleMaxValue;
            if (max < min)
            {
                int tmp = min;
                min = max;
                max = tmp;
            }

            int current = parameter.DefaultInt;
            if (store.TryGetInt(parameter.Id, out int sampled))
            {
                current = sampled;
            }

            int next;
            if (current < min || current > max)
            {
                next = min;
            }
            else
            {
                next = current + 1;
                if (next > max)
                {
                    next = min;
                }
            }

            store.SetInt(parameter.Id, next);
        }

        private static int CompareTransitionPriority(FusionAnimatorTransitionDefinition a, FusionAnimatorTransitionDefinition b)
        {
            if (ReferenceEquals(a, b))
            {
                return 0;
            }

            if (a == null)
            {
                return 1;
            }

            if (b == null)
            {
                return -1;
            }

            int byPriority = a.Priority.CompareTo(b.Priority);
            if (byPriority != 0)
            {
                return byPriority;
            }

            return string.Compare(a.Id, b.Id, StringComparison.Ordinal);
        }

        private static bool CompareNumeric(float lhs, float rhs, FusionAnimatorConditionOperator op)
        {
            switch (op)
            {
                case FusionAnimatorConditionOperator.Equal: return Mathf.Approximately(lhs, rhs);
                case FusionAnimatorConditionOperator.NotEqual: return Mathf.Approximately(lhs, rhs) == false;
                case FusionAnimatorConditionOperator.Greater: return lhs > rhs;
                case FusionAnimatorConditionOperator.GreaterOrEqual: return lhs >= rhs;
                case FusionAnimatorConditionOperator.Less: return lhs < rhs;
                case FusionAnimatorConditionOperator.LessOrEqual: return lhs <= rhs;
                default: return false;
            }
        }

        private static bool CompareNumericRange(float value, float rangeMin, float rangeMax)
        {
            float min = Mathf.Min(rangeMin, rangeMax);
            float max = Mathf.Max(rangeMin, rangeMax);
            return value >= min && value <= max;
        }

        private float ResolveReferenceLengthSeconds(FusionAnimatorStateDefinition state, IFusionAnimatorParameterSource parameters)
        {
            if (state == null)
            {
                return 0.0f;
            }

            float maxLength = 0.0f;
            if (state.MotionType == FusionAnimatorMotionType.BlendTree &&
                state.BlendTree != null &&
                state.BlendTree.Children != null)
            {
                for (int i = 0; i < state.BlendTree.Children.Count; ++i)
                {
                    FusionAnimatorBlendTreeChild child = state.BlendTree.Children[i];
                    AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(
                        _graph,
                        child,
                        condition => EvaluateBindingCondition(condition, parameters),
                        parameterReference => ResolveBindingClipIndexParameter(parameterReference, parameters));
                    if (clip == null)
                    {
                        continue;
                    }

                    maxLength = Mathf.Max(maxLength, Mathf.Max(0.0f, clip.length));
                }
            }
            else if (state.Clips != null)
            {
                for (int i = 0; i < state.Clips.Count; ++i)
                {
                    FusionAnimatorClipSlot clipSlot = state.Clips[i];
                    AnimationClip clip = FusionAnimatorClipBindingUtility.ResolveClip(
                        _graph,
                        clipSlot,
                        condition => EvaluateBindingCondition(condition, parameters),
                        parameterReference => ResolveBindingClipIndexParameter(parameterReference, parameters));
                    if (clip == null)
                    {
                        continue;
                    }

                    maxLength = Mathf.Max(maxLength, Mathf.Max(0.0f, clip.length));
                }
            }

            return maxLength;
        }

        private bool IsStateLooping(FusionAnimatorStateDefinition state, IFusionAnimatorParameterSource parameters)
        {
            if (state == null)
            {
                return true;
            }

            if (state.MotionType == FusionAnimatorMotionType.BlendTree)
            {
                return true;
            }

            if (state.Clips == null || state.Clips.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < state.Clips.Count; ++i)
            {
                FusionAnimatorClipSlot clip = state.Clips[i];
                if (clip == null)
                {
                    continue;
                }

                bool isLooping = FusionAnimatorClipBindingUtility.ResolveLoop(
                    _graph,
                    clip,
                    condition => EvaluateBindingCondition(condition, parameters),
                    parameterReference => ResolveBindingClipIndexParameter(parameterReference, parameters));
                if (isLooping == false)
                {
                    return false;
                }
            }

            return true;
        }

        private bool EvaluateBindingCondition(FusionAnimatorConditionDefinition condition, IFusionAnimatorParameterSource parameters)
        {
            if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
            {
                return false;
            }

            if (TryResolveConditionParameter(condition.ParameterId, out FusionAnimatorParameterDefinition parameter, out FusionAnimatorParameterComponent component) == false)
            {
                return false;
            }

            return EvaluateCondition(condition, parameter, parameters, false, component);
        }

        private int? ResolveBindingClipIndexParameter(string parameterReference, IFusionAnimatorParameterSource parameters)
        {
            if (string.IsNullOrWhiteSpace(parameterReference) || parameters == null)
            {
                return null;
            }

            if (FusionAnimatorParameterReferenceUtility.TryParse(parameterReference, out string parameterId, out FusionAnimatorParameterComponent component) == false)
            {
                return null;
            }

            if (component != FusionAnimatorParameterComponent.None)
            {
                return null;
            }

            if (_parametersById.TryGetValue(parameterId, out FusionAnimatorParameterDefinition parameter) == false ||
                parameter == null ||
                parameter.Type != FusionAnimatorParameterType.Int)
            {
                return null;
            }

            if (parameters.TryGetInt(parameterId, out int value))
            {
                return value;
            }

            return parameter.DefaultInt;
        }

        private static bool HasCrossedExitThreshold(
            bool isLooping,
            float previousRawNormalizedTime,
            float currentRawNormalizedTime,
            float exitTimeNormalized)
        {
            if (currentRawNormalizedTime <= previousRawNormalizedTime + 0.000001f)
            {
                return false;
            }

            float exitThreshold = Mathf.Max(0.0f, exitTimeNormalized);
            if (exitThreshold <= 0.0f)
            {
                return true;
            }

            if (isLooping == false)
            {
                float clamped = Mathf.Clamp01(exitThreshold);
                return previousRawNormalizedTime < clamped && currentRawNormalizedTime >= clamped;
            }

            if (exitThreshold >= 1.0f)
            {
                return previousRawNormalizedTime < exitThreshold && currentRawNormalizedTime >= exitThreshold;
            }

            float wrappedThreshold = Mathf.Repeat(exitThreshold, 1.0f);
            float nextCrossing = Mathf.Floor(previousRawNormalizedTime - wrappedThreshold) + 1.0f + wrappedThreshold;
            return nextCrossing <= currentRawNormalizedTime + 0.000001f;
        }

        private float ResolveBlendDurationSeconds(
            FusionAnimatorTransitionDefinition transition,
            FusionAnimatorStateDefinition fromState,
            IFusionAnimatorParameterSource parameters)
        {
            if (transition == null)
            {
                return 0.0f;
            }

            float duration = Mathf.Max(0.0f, transition.BlendDurationSeconds);
            if (transition.FixedDuration == false)
            {
                duration *= Mathf.Max(0.01f, ResolveReferenceLengthSeconds(fromState, parameters));
            }

            return duration;
        }

        private float ResolveStateMinDurationSeconds(
            FusionAnimatorStateDefinition state,
            IFusionAnimatorParameterSource parameters)
        {
            if (state == null)
            {
                return 0.0f;
            }

            float normalizedDuration = Mathf.Max(0.0f, state.MinDurationSeconds);
            if (normalizedDuration <= 0.0f)
            {
                return 0.0f;
            }

            float referenceLength = ResolveReferenceLengthSeconds(state, parameters);
            if (referenceLength <= 0.0001f)
            {
                referenceLength = ResolveConfiguredReferenceLengthSeconds(state);
                if (referenceLength <= 0.0001f)
                {
                    return 0.0f;
                }
            }

            return normalizedDuration * referenceLength;
        }

        private float ResolveConfiguredReferenceLengthSeconds(FusionAnimatorStateDefinition state)
        {
            if (state == null)
            {
                return 0.0f;
            }

            float maxLength = 0.0f;
            if (state.MotionType == FusionAnimatorMotionType.BlendTree &&
                state.BlendTree != null &&
                state.BlendTree.Children != null)
            {
                for (int i = 0; i < state.BlendTree.Children.Count; ++i)
                {
                    FusionAnimatorBlendTreeChild child = state.BlendTree.Children[i];
                    if (child == null)
                    {
                        continue;
                    }

                    if (child.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
                    {
                        FusionAnimatorClipBindingDefinition binding = FusionAnimatorClipBindingUtility.FindBinding(_graph, child.BindingId);
                        maxLength = Mathf.Max(maxLength, ResolveConfiguredBindingLengthSeconds(binding));
                        continue;
                    }

                    if (child.Clip != null)
                    {
                        maxLength = Mathf.Max(maxLength, Mathf.Max(0.0f, child.Clip.length));
                    }
                }
            }
            else if (state.Clips != null)
            {
                for (int i = 0; i < state.Clips.Count; ++i)
                {
                    FusionAnimatorClipSlot slot = state.Clips[i];
                    if (slot == null)
                    {
                        continue;
                    }

                    if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Binding)
                    {
                        FusionAnimatorClipBindingDefinition binding = FusionAnimatorClipBindingUtility.FindBinding(_graph, slot.BindingId);
                        maxLength = Mathf.Max(maxLength, ResolveConfiguredBindingLengthSeconds(binding));
                        continue;
                    }

                    if (slot.Clip != null)
                    {
                        maxLength = Mathf.Max(maxLength, Mathf.Max(0.0f, slot.Clip.length));
                    }
                }
            }

            return maxLength;
        }

        private static float ResolveConfiguredBindingLengthSeconds(FusionAnimatorClipBindingDefinition binding)
        {
            if (binding == null || binding.Clips == null || binding.Clips.Count == 0)
            {
                return 0.0f;
            }

            float maxLength = 0.0f;
            for (int i = 0; i < binding.Clips.Count; ++i)
            {
                FusionAnimatorClipBindingSlot clipSlot = binding.Clips[i];
                if (clipSlot == null || clipSlot.Clip == null)
                {
                    continue;
                }

                maxLength = Mathf.Max(maxLength, Mathf.Max(0.0f, clipSlot.Clip.length));
            }

            return maxLength;
        }

        private void ClearBlend()
        {
            BlendFromStateId = null;
            BlendFromStateTime = 0.0f;
            BlendToStateId = null;
            BlendDurationSeconds = 0.0f;
            BlendElapsedSeconds = 0.0f;
        }
    }
}
