using System;
using System.Collections.Generic;
using UnityEngine;

namespace FusionAnimator
{
    public sealed class FusionAnimatorRuntimeGraphInstance
    {
        private readonly FusionAnimatorGraphAsset _graph;
        private readonly List<FusionAnimatorLayerDefinition> _orderedLayers = new List<FusionAnimatorLayerDefinition>(8);
        private readonly Dictionary<string, FusionAnimatorRuntimeEvaluator> _evaluatorsByLayerId = new Dictionary<string, FusionAnimatorRuntimeEvaluator>(StringComparer.Ordinal);

        public FusionAnimatorRuntimeGraphInstance(FusionAnimatorGraphAsset graph)
        {
            _graph = graph;
            BuildLayers();
        }

        public void Reset()
        {
            for (int i = 0; i < _orderedLayers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _orderedLayers[i];
                if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
                {
                    continue;
                }

                if (_evaluatorsByLayerId.TryGetValue(layer.Id, out FusionAnimatorRuntimeEvaluator evaluator))
                {
                    evaluator.Reset();
                }
            }
        }

        public void Step(
            float deltaTime,
            IFusionAnimatorParameterSource parameters,
            IFusionAnimatorStateLogic logic = null,
            bool applyPreviewOnlyResults = false)
        {
            for (int i = 0; i < _orderedLayers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _orderedLayers[i];
                if (layer == null || layer.EnabledByDefault == false || string.IsNullOrWhiteSpace(layer.Id))
                {
                    continue;
                }

                if (_evaluatorsByLayerId.TryGetValue(layer.Id, out FusionAnimatorRuntimeEvaluator evaluator))
                {
                    evaluator.Step(deltaTime, parameters, logic, applyPreviewOnlyResults);
                }
            }

            // Triggers are one-shot pulses; if no transition consumed them this frame,
            // expire them so they cannot fire later after context/scope changes.
            if (parameters is FusionAnimatorParameterStore store)
            {
                store.ExpireUnconsumedTriggers();
            }
        }

        public FusionAnimatorRuntimeEvaluator GetLayerEvaluator(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            return _evaluatorsByLayerId.TryGetValue(layerId, out FusionAnimatorRuntimeEvaluator evaluator) ? evaluator : null;
        }

        private void BuildLayers()
        {
            _orderedLayers.Clear();
            _evaluatorsByLayerId.Clear();
            if (_graph == null || _graph.Layers == null || _graph.Layers.Count == 0)
            {
                return;
            }

            Dictionary<string, int> layerOrderById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
                {
                    continue;
                }

                _orderedLayers.Add(layer);
                if (layerOrderById.ContainsKey(layer.Id) == false)
                {
                    layerOrderById.Add(layer.Id, i);
                }
            }

            _orderedLayers.Sort((a, b) =>
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

                int aOrder = (a != null && string.IsNullOrWhiteSpace(a.Id) == false && layerOrderById.TryGetValue(a.Id, out int aIndex)) ? aIndex : int.MaxValue;
                int bOrder = (b != null && string.IsNullOrWhiteSpace(b.Id) == false && layerOrderById.TryGetValue(b.Id, out int bIndex)) ? bIndex : int.MaxValue;
                return aOrder.CompareTo(bOrder);
            });

            for (int i = 0; i < _orderedLayers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _orderedLayers[i];
                if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
                {
                    continue;
                }

                string layerId = layer.Id;
                Func<FusionAnimatorStateDefinition, bool> layerFilter = state =>
                    state != null && string.Equals(state.LayerId, layerId, StringComparison.Ordinal);

                string defaultStateId = ResolveLayerDefaultStateId(layerId);
                FusionAnimatorRuntimeEvaluator evaluator = new FusionAnimatorRuntimeEvaluator(_graph, layerFilter, defaultStateId);
                _evaluatorsByLayerId[layerId] = evaluator;
            }
        }

        private string ResolveLayerDefaultStateId(string layerId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            FusionAnimatorTransitionDefinition bestEntryTransition = null;
            bool hasEntryTransition = false;
            bool hasConditionalEntryTransition = false;
            if (_graph.Transitions != null)
            {
                for (int i = 0; i < _graph.Transitions.Count; ++i)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                    if (transition == null ||
                        transition.Mute ||
                        string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    FusionAnimatorStateDefinition destination = FindState(transition.ToStateId);
                    if (destination == null ||
                        string.Equals(destination.LayerId, layerId, StringComparison.Ordinal) == false ||
                        string.IsNullOrWhiteSpace(GetStateScopePath(destination.Name)) == false)
                    {
                        continue;
                    }

                    hasEntryTransition = true;
                    if (transition.Conditions != null && transition.Conditions.Count > 0)
                    {
                        hasConditionalEntryTransition = true;
                        continue;
                    }

                    if (bestEntryTransition == null || transition.Priority < bestEntryTransition.Priority)
                    {
                        bestEntryTransition = transition;
                    }
                }
            }

            if (bestEntryTransition != null)
            {
                return bestEntryTransition.ToStateId;
            }

            if (hasEntryTransition == false)
            {
                // Layers that are driven only by Any-state routes (e.g. one-shot overlays such as Death)
                // must remain inactive until a transition condition actually passes.
                if (HasAnyTransitionIntoTopLevelState(layerId) == true)
                {
                    return string.Empty;
                }

                return ResolveFallbackLayerDefaultStateId(layerId);
            }

            if (hasConditionalEntryTransition == true)
            {
                return string.Empty;
            }

            return string.Empty;
        }

        private bool HasAnyTransitionIntoTopLevelState(string layerId)
        {
            if (_graph == null || _graph.Transitions == null || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            for (int i = 0; i < _graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null ||
                    transition.Mute ||
                    string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                FusionAnimatorStateDefinition destination = FindState(transition.ToStateId);
                if (destination == null ||
                    string.Equals(destination.LayerId, layerId, StringComparison.Ordinal) == false ||
                    string.IsNullOrWhiteSpace(GetStateScopePath(destination.Name)) == false)
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        private string ResolveFallbackLayerDefaultStateId(string layerId)
        {
            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(layerId))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(_graph.EntryStateId) == false)
            {
                FusionAnimatorStateDefinition graphEntryState = FindState(_graph.EntryStateId);
                if (graphEntryState != null &&
                    string.Equals(graphEntryState.LayerId, layerId, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(GetStateScopePath(graphEntryState.Name)))
                {
                    return graphEntryState.Id;
                }
            }

            FusionAnimatorStateDefinition bestCandidate = null;
            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition candidate = _graph.States[i];
                if (candidate == null ||
                    string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal) == false ||
                    string.IsNullOrWhiteSpace(GetStateScopePath(candidate.Name)) == false)
                {
                    continue;
                }

                if (bestCandidate == null ||
                    candidate.NodePosition.y < bestCandidate.NodePosition.y ||
                    (Mathf.Approximately(candidate.NodePosition.y, bestCandidate.NodePosition.y) &&
                     candidate.NodePosition.x < bestCandidate.NodePosition.x))
                {
                    bestCandidate = candidate;
                }
            }

            return bestCandidate != null ? bestCandidate.Id : string.Empty;
        }

        private FusionAnimatorStateDefinition FindState(string stateId)
        {
            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(stateId))
            {
                return null;
            }

            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state != null && string.Equals(state.Id, stateId, StringComparison.Ordinal))
                {
                    return state;
                }
            }

            return null;
        }

        private static string GetStateScopePath(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return string.Empty;
            }

            int slash = stateName.LastIndexOf('/');
            if (slash <= 0)
            {
                return string.Empty;
            }

            return stateName.Substring(0, slash);
        }
    }
}
