using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using UnityEditor;
using UnityEngine;

namespace FusionAnimator.Editor
{
    public sealed partial class FusionAnimatorGraphCanvasWindow : EditorWindow
    {
        private enum PreviewGamepadScalarBinding
        {
            None = 0,
            LeftStickX = 1,
            LeftStickY = 2,
            RightStickX = 3,
            RightStickY = 4,
            LeftTrigger = 5,
            RightTrigger = 6,
            DpadX = 7,
            DpadY = 8,
            ButtonSouth = 9,
            ButtonEast = 10,
            ButtonWest = 11,
            ButtonNorth = 12,
            LeftShoulder = 13,
            RightShoulder = 14,
            Start = 15,
            Select = 16,
            LeftStickPress = 17,
            RightStickPress = 18,
            LeftStickMagnitude = 19,
            RightStickMagnitude = 20,
            DpadMagnitude = 21,
        }

        private enum PreviewGamepadVector2Binding
        {
            None = 0,
            LeftStick = 1,
            RightStick = 2,
            Dpad = 3,
        }

        [Serializable]
        private sealed class PreviewParameterEntry
        {
            public string ParameterId;
            public bool BoolValue;
            public int IntValue;
            public float FloatValue;
            public Vector2 Vector2Value;
            public PreviewGamepadScalarBinding ScalarBinding;
            public PreviewGamepadVector2Binding Vector2Binding;
            public float BindingScale = 1.0f;
            public FusionAnimatorConditionOperator BoolInputOperator = FusionAnimatorConditionOperator.Greater;
            public float BoolInputCompareValue = 0.5f;
        }

        private struct PreviewGamepadSnapshot
        {
            public bool IsConnected;
            public Vector2 LeftStick;
            public Vector2 RightStick;
            public float LeftTrigger;
            public float RightTrigger;
            public Vector2 Dpad;
            public bool ButtonSouth;
            public bool ButtonEast;
            public bool ButtonWest;
            public bool ButtonNorth;
            public bool LeftShoulder;
            public bool RightShoulder;
            public bool Start;
            public bool Select;
            public bool LeftStickPress;
            public bool RightStickPress;
        }

        private struct PreviewMotionSample
        {
            public AnimationClip Clip;
            public float Weight;
            public float TimeScale;
            public bool Loop;
            public float ExplicitNormalizedTime;
        }

        private enum PreviewMotionLoopMode
        {
            PerSample = 0,
            ForceLoop = 1,
            ForceClamp = 2,
        }

        [SerializeField] private List<PreviewParameterEntry> _previewParameterEntries = new List<PreviewParameterEntry>();
        [SerializeField] private bool _previewShowMiniMap = true;
        [SerializeField] private float _previewButtonThreshold = 0.5f;
        [SerializeField] private bool _previewEnabled = true;
        [SerializeField] private bool _previewPlay = true;
        [SerializeField] private float _previewPlaySpeed = 1.0f;
        private float _previewTime;
        private float _previewBlendFromTime;
        private float _previewStateElapsed;
        private string _previewActiveStateId;
        private string _previewBlendFromStateId;
        private string _previewBlendToStateId;
        private string _previewLoopTransitionId;
        private float _previewBlendElapsed;
        private float _previewBlendDuration;
        private double _previewLastEditorTime;
        private GameObject _previewTarget;
        [SerializeField] private float _previewCameraOrbitYaw = 35.0f;
        [SerializeField] private float _previewCameraOrbitPitch = 18.0f;
        [SerializeField] private float _previewCameraOrbitDistanceScale = 2.8f;
        [SerializeField] private Vector3 _previewCameraOrbitTargetOffset = Vector3.zero;
        private string _previewStatus = "No preview target assigned.";
        private readonly List<PreviewMotionSample> _previewMotionSamplesA = new List<PreviewMotionSample>();
        private readonly List<PreviewMotionSample> _previewMotionSamplesB = new List<PreviewMotionSample>();
        private readonly List<AnimationClip> _previewRenderClipsA = new List<AnimationClip>();
        private readonly List<float> _previewRenderTimesA = new List<float>();
        private readonly List<float> _previewRenderWeightsA = new List<float>();
        private readonly List<AnimationClip> _previewRenderClipsB = new List<AnimationClip>();
        private readonly List<float> _previewRenderTimesB = new List<float>();
        private readonly List<float> _previewRenderWeightsB = new List<float>();
        private readonly List<string> _previewActiveMarkerStateIds = new List<string>();
        private readonly List<string> _previewBlendMarkerStateIds = new List<string>();
        private readonly List<string> _previewActiveMarkerLayerIds = new List<string>();
        private readonly FusionAnimatorParameterStore _previewRuntimeParameters = new FusionAnimatorParameterStore();
        private FusionAnimatorRuntimeEvaluator _previewRuntimeEvaluator;
        private FusionAnimatorGraphAsset _previewRuntimeGraph;
        private FusionAnimatorGraphAsset _previewRuntimeParametersAsset;
        private string _previewRuntimeLayerId;
        private string _previewRuntimeScopePath;
        private string _previewRuntimeDefaultStateId;
        private float _previewStepDeltaTime;
        private bool _previewResolvedByRuntime;
        private FusionAnimatorRuntimeGraphInstance _previewRuntimeGraphInstance;
        private FusionAnimatorGraphAsset _previewRuntimeGraphInstanceAsset;
        private readonly List<AnimationClip> _previewLayerTempClips = new List<AnimationClip>();
        private readonly List<float> _previewLayerTempTimes = new List<float>();
        private readonly List<float> _previewLayerTempWeights = new List<float>();
        private readonly List<List<AnimationClip>> _previewLayerStackClips = new List<List<AnimationClip>>();
        private readonly List<List<float>> _previewLayerStackTimes = new List<List<float>>();
        private readonly List<List<float>> _previewLayerStackWeights = new List<List<float>>();
        private readonly List<FusionAnimatorGraphView.PreviewLayerPoseInput> _previewLayerStackInputs = new List<FusionAnimatorGraphView.PreviewLayerPoseInput>();

        private static readonly Type InputActionReferenceType = ResolveInputActionReferenceType();
        private static readonly Type InputSystemType = ResolveInputSystemType();
        private static readonly MethodInfo InputSystemUpdateMethod = ResolveInputSystemUpdateMethod();
        private static readonly Type InputUpdateTypeType = ResolveInputUpdateType();
        private static readonly MethodInfo InputSystemUpdateWithTypeMethod = ResolveInputSystemUpdateWithTypeMethod();
        private static readonly MethodInfo InputSystemFindControlsMethod = ResolveInputSystemFindControlsMethod();
        private static readonly object EditorInputUpdateEnumValue = ResolveEditorInputUpdateEnumValue();
        private static readonly FusionAnimatorConditionOperator[] PreviewBoolInputOperators =
        {
            FusionAnimatorConditionOperator.IsTrue,
            FusionAnimatorConditionOperator.IsFalse,
            FusionAnimatorConditionOperator.Equal,
            FusionAnimatorConditionOperator.NotEqual,
            FusionAnimatorConditionOperator.Greater,
            FusionAnimatorConditionOperator.GreaterOrEqual,
            FusionAnimatorConditionOperator.Less,
            FusionAnimatorConditionOperator.LessOrEqual,
        };
        private static readonly string[] PreviewBoolInputOperatorLabels = PreviewBoolInputOperators
            .Select(FormatConditionOperatorLabel)
            .ToArray();

        private void ApplyPreviewCameraStateToGraphView()
        {
            if (_graphView == null)
            {
                return;
            }

            _previewCameraOrbitDistanceScale = Mathf.Clamp(_previewCameraOrbitDistanceScale, 0.65f, 6.0f);
            _previewCameraOrbitPitch = Mathf.Clamp(_previewCameraOrbitPitch, -80.0f, 80.0f);
            _graphView.SetPreviewCameraState(
                _previewCameraOrbitYaw,
                _previewCameraOrbitPitch,
                _previewCameraOrbitDistanceScale,
                _previewCameraOrbitTargetOffset);
        }

        private void CapturePreviewCameraStateFromGraphView()
        {
            if (_graphView == null)
            {
                return;
            }

            _graphView.GetPreviewCameraState(
                out _previewCameraOrbitYaw,
                out _previewCameraOrbitPitch,
                out _previewCameraOrbitDistanceScale,
                out _previewCameraOrbitTargetOffset);
        }

        private void ResetIntegratedPreview()
        {
            EnsurePreviewEntries();
            InvalidatePreviewRuntimeSimulation();
            _previewTime = 0.0f;
            _previewBlendFromTime = 0.0f;
            _previewStateElapsed = 0.0f;
            _previewActiveStateId = null;
            _previewBlendFromStateId = null;
            _previewBlendToStateId = null;
            _previewLoopTransitionId = null;
            _previewBlendElapsed = 0.0f;
            _previewBlendDuration = 0.0f;
            _previewLastEditorTime = EditorApplication.timeSinceStartup;
            _previewStatus = _previewTarget == null ? "No preview target assigned." : "Preview ready.";
            _graphView?.SetPreviewBackgroundStatus(_previewStatus);
            _graphView?.SetMiniMapVisible(_previewShowMiniMap);
            _graphView?.ClearPreviewRuntimeMarkers();
            RefreshPreviewToolbarValues();
        }

        private void InvalidatePreviewRuntimeSimulation()
        {
            _previewRuntimeEvaluator = null;
            _previewRuntimeGraph = null;
            _previewRuntimeParametersAsset = null;
            _previewRuntimeParameters.Clear();
            _previewRuntimeLayerId = null;
            _previewRuntimeScopePath = null;
            _previewRuntimeDefaultStateId = null;
            _previewRuntimeGraphInstance = null;
            _previewRuntimeGraphInstanceAsset = null;
            _previewResolvedByRuntime = false;
            _previewStepDeltaTime = 0.0f;
        }

        private void StopPreviewSampling()
        {
            if (AnimationMode.InAnimationMode())
            {
                AnimationMode.StopAnimationMode();
            }
        }

        private bool HasExplicitPreviewSelection()
        {
            return _selectedState != null ||
                   _selectedTransition != null ||
                   string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false;
        }

        private bool IsOverviewRuntimeScope()
        {
            if (_graph == null || _graph.Layers == null || _graph.Layers.Count == 0)
            {
                return false;
            }

            bool selectedLayer = _selectedLayerIndex >= 0 && _selectedLayerIndex < _graph.Layers.Count;
            bool activeLayer = string.IsNullOrWhiteSpace(_activeLayerId) == false;
            return selectedLayer == false && activeLayer == false;
        }

        private void EnsurePreviewRuntimeGraphInstance()
        {
            bool needsRebuild = _previewRuntimeGraphInstance == null ||
                                ReferenceEquals(_previewRuntimeGraphInstanceAsset, _graph) == false;
            if (needsRebuild == false)
            {
                return;
            }

            _previewRuntimeGraphInstance = _graph != null ? new FusionAnimatorRuntimeGraphInstance(_graph) : null;
            _previewRuntimeGraphInstanceAsset = _graph;
        }

        private static void AppendRenderData(
            List<AnimationClip> targetClips,
            List<float> targetTimes,
            List<float> targetWeights,
            List<AnimationClip> sourceClips,
            List<float> sourceTimes,
            List<float> sourceWeights,
            float scale)
        {
            if (targetClips == null || targetTimes == null || targetWeights == null ||
                sourceClips == null || sourceTimes == null || sourceWeights == null)
            {
                return;
            }

            if (scale <= 0.000001f)
            {
                return;
            }

            int count = Mathf.Min(sourceClips.Count, sourceTimes.Count, sourceWeights.Count);
            for (int i = 0; i < count; ++i)
            {
                AnimationClip clip = sourceClips[i];
                float weight = sourceWeights[i] * scale;
                if (clip == null || weight <= 0.000001f)
                {
                    continue;
                }

                targetClips.Add(clip);
                targetTimes.Add(sourceTimes[i]);
                targetWeights.Add(weight);
            }
        }

        private void EnsureLayerStackRenderBuffers(
            int index,
            out List<AnimationClip> clips,
            out List<float> sampleTimes,
            out List<float> sampleWeights)
        {
            while (_previewLayerStackClips.Count <= index)
            {
                _previewLayerStackClips.Add(new List<AnimationClip>());
                _previewLayerStackTimes.Add(new List<float>());
                _previewLayerStackWeights.Add(new List<float>());
            }

            clips = _previewLayerStackClips[index];
            sampleTimes = _previewLayerStackTimes[index];
            sampleWeights = _previewLayerStackWeights[index];
            clips.Clear();
            sampleTimes.Clear();
            sampleWeights.Clear();
        }

        private static bool IsStateLoopingForPreview(FusionAnimatorStateDefinition state)
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
                if (clip != null && clip.Loop == false)
                {
                    return false;
                }
            }

            return true;
        }

        private static PreviewMotionLoopMode ResolveStateLoopMode(FusionAnimatorStateDefinition state)
        {
            return IsStateLoopingForPreview(state) ? PreviewMotionLoopMode.ForceLoop : PreviewMotionLoopMode.ForceClamp;
        }

        private static float ResolveSampleTime(float motionTime, float clipLength, PreviewMotionLoopMode loopMode)
        {
            float safeLength = Mathf.Max(0.01f, clipLength);
            switch (loopMode)
            {
                case PreviewMotionLoopMode.ForceClamp:
                    return Mathf.Clamp(motionTime, 0.0f, safeLength);
                case PreviewMotionLoopMode.ForceLoop:
                    return Mathf.Repeat(motionTime, safeLength);
                default:
                    return Mathf.Repeat(motionTime, safeLength);
            }
        }

        private bool TryRenderRuntimeLayerStackPreview()
        {
            if (_graph == null || _graph.Layers == null || _graph.Layers.Count == 0)
            {
                return false;
            }

            bool previewSingleLayer = false;
            string previewSingleLayerId = string.Empty;

            string contextLayerId = _activeLayerId;
            string contextScopePath = _activeScopePath;
            if (_graphView != null)
            {
                _graphView.GetRenderContext(out contextLayerId, out contextScopePath);
            }

            contextLayerId = string.IsNullOrWhiteSpace(contextLayerId) ? string.Empty : contextLayerId.Trim();
            string activeScopePath = NormalizeScopePath(contextScopePath);
            if (string.IsNullOrWhiteSpace(contextLayerId) == false &&
                string.IsNullOrWhiteSpace(activeScopePath) == false)
            {
                // Nested scopes use dedicated runtime evaluator path for scope-accurate transitions.
                return false;
            }

            if (string.IsNullOrWhiteSpace(contextLayerId) == false &&
                string.IsNullOrWhiteSpace(activeScopePath))
            {
                previewSingleLayerId = contextLayerId;
            }

            // If an overview layer node is explicitly selected, preview only that layer.
            if (string.IsNullOrWhiteSpace(previewSingleLayerId) &&
                string.IsNullOrWhiteSpace(contextLayerId) &&
                string.IsNullOrWhiteSpace(activeScopePath) &&
                _selectedState == null &&
                _selectedTransition == null &&
                string.IsNullOrWhiteSpace(_selectedLayerScopePath) &&
                string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) &&
                _graphView != null &&
                _graphView.TryGetSelectedLayerNodeId(out string selectedOverviewLayerId) &&
                string.IsNullOrWhiteSpace(selectedOverviewLayerId) == false)
            {
                previewSingleLayerId = selectedOverviewLayerId;
            }

            previewSingleLayer = string.IsNullOrWhiteSpace(previewSingleLayerId) == false;

            EnsurePreviewRuntimeGraphInstance();
            if (_previewRuntimeGraphInstance == null)
            {
                return false;
            }

            SyncPreviewRuntimeParameters();
            _previewRuntimeGraphInstance.Step(_previewStepDeltaTime, _previewRuntimeParameters, null, true);
            SyncPreviewEntriesFromRuntimeParameters();

            List<FusionAnimatorLayerDefinition> orderedLayers = new List<FusionAnimatorLayerDefinition>(_graph.Layers.Count);
            Dictionary<string, int> layerOrderById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null &&
                    (previewSingleLayer
                        ? string.Equals(layer.Id, previewSingleLayerId, StringComparison.Ordinal)
                        : true) &&
                    string.IsNullOrWhiteSpace(layer.Id) == false)
                {
                    orderedLayers.Add(layer);
                    if (layerOrderById.ContainsKey(layer.Id) == false)
                    {
                        layerOrderById.Add(layer.Id, i);
                    }
                }
            }

            orderedLayers.Sort((a, b) =>
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

            _previewLayerStackInputs.Clear();
            _previewActiveMarkerStateIds.Clear();
            _previewBlendMarkerStateIds.Clear();
            _previewActiveMarkerLayerIds.Clear();

            FusionAnimatorStateDefinition topState = null;
            FusionAnimatorRuntimeEvaluator topEvaluator = null;
            float topReferenceLength = 0.0f;
            float topStateSampleTime = 0.0f;
            int composedLayers = 0;
            List<KeyValuePair<string, string>> activeLayerStateLines = new List<KeyValuePair<string, string>>(_graph.Layers.Count);
            Dictionary<string, string> layerClipSummaryByLayerId = new Dictionary<string, string>(StringComparer.Ordinal);

            for (int i = 0; i < orderedLayers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = orderedLayers[i];
                if (layer == null)
                {
                    continue;
                }

                float layerWeight = Mathf.Clamp01(layer.DefaultWeight);
                if (layerWeight <= 0.000001f)
                {
                    continue;
                }

                FusionAnimatorRuntimeEvaluator evaluator = _previewRuntimeGraphInstance.GetLayerEvaluator(layer.Id);
                if (evaluator == null)
                {
                    continue;
                }

                FusionAnimatorStateDefinition currentState = FindStateById(evaluator.CurrentStateId);
                FusionAnimatorStateDefinition blendFromState = evaluator.IsBlending
                    ? FindStateById(evaluator.BlendFromStateId)
                    : null;
                bool hasExitBlendOnly = currentState == null &&
                                        evaluator.IsBlending &&
                                        blendFromState != null &&
                                        string.IsNullOrWhiteSpace(evaluator.BlendToStateId);
                if (currentState == null && hasExitBlendOnly == false)
                {
                    continue;
                }

                if (currentState != null &&
                    _previewActiveMarkerStateIds.Contains(currentState.Id) == false)
                {
                    _previewActiveMarkerStateIds.Add(currentState.Id);
                }

                if ((currentState != null || hasExitBlendOnly) &&
                    _previewActiveMarkerLayerIds.Contains(layer.Id) == false)
                {
                    _previewActiveMarkerLayerIds.Add(layer.Id);
                }

                FusionAnimatorStateDefinition statusState = currentState ?? blendFromState;
                if (statusState != null)
                {
                    string statusLayerDisplayName = string.IsNullOrWhiteSpace(layer.Name) ? layer.Id : layer.Name;
                    string stateDisplayName = statusState.Name ?? string.Empty;
                    int stateNameSeparator = stateDisplayName.LastIndexOf('/');
                    if (stateNameSeparator >= 0 && stateNameSeparator < stateDisplayName.Length - 1)
                    {
                        stateDisplayName = stateDisplayName.Substring(stateNameSeparator + 1);
                    }

                    float statusTime = currentState != null
                        ? Mathf.Max(0.0f, evaluator.CurrentStateTime)
                        : Mathf.Max(0.0f, evaluator.BlendFromStateTime);
                    if (TryResolvePreviewMotion(statusState, _previewMotionSamplesA, out float statusReferenceLength, out _, out _))
                    {
                        statusReferenceLength = Mathf.Max(0.01f, statusReferenceLength);
                        float statusSampleTime = ResolveSampleTime(statusTime, statusReferenceLength, ResolveStateLoopMode(statusState));
                        activeLayerStateLines.Add(new KeyValuePair<string, string>(layer.Id, string.Format(
                            "{0}.{1} t={2:0.00}/{3:0.00}",
                            statusLayerDisplayName,
                            stateDisplayName,
                            statusSampleTime,
                            statusReferenceLength)));
                    }
                    else
                    {
                        activeLayerStateLines.Add(new KeyValuePair<string, string>(layer.Id, string.Format(
                            "{0}.{1} t={2:0.00}",
                            statusLayerDisplayName,
                            stateDisplayName,
                            statusTime)));
                    }
                }

                if (evaluator.IsBlending)
                {
                    if (string.IsNullOrWhiteSpace(evaluator.BlendFromStateId) == false &&
                        _previewBlendMarkerStateIds.Contains(evaluator.BlendFromStateId) == false)
                    {
                        _previewBlendMarkerStateIds.Add(evaluator.BlendFromStateId);
                    }

                    if (string.IsNullOrWhiteSpace(evaluator.BlendToStateId) == false &&
                        _previewBlendMarkerStateIds.Contains(evaluator.BlendToStateId) == false)
                    {
                        _previewBlendMarkerStateIds.Add(evaluator.BlendToStateId);
                    }
                }

                float blendAlpha = evaluator.IsBlending ? Mathf.Clamp01(evaluator.BlendAlpha) : 1.0f;
                float toScale = currentState != null ? (evaluator.IsBlending ? blendAlpha : 1.0f) : 0.0f;
                float fromScale = evaluator.IsBlending ? (1.0f - blendAlpha) : 0.0f;
                EnsureLayerStackRenderBuffers(
                    composedLayers,
                    out List<AnimationClip> layerClips,
                    out List<float> layerTimes,
                    out List<float> layerWeights);

                if (fromScale > 0.000001f)
                {
                    FusionAnimatorStateDefinition fromState = blendFromState ?? FindStateById(evaluator.BlendFromStateId);
                    if (fromState != null &&
                        TryResolvePreviewMotion(fromState, _previewMotionSamplesB, out float fromReferenceLength, out _, out _))
                    {
                        PreviewMotionLoopMode fromLoopMode = ResolveStateLoopMode(fromState);
                        BuildMotionRenderData(
                            _previewMotionSamplesB,
                            Mathf.Max(0.0f, evaluator.BlendFromStateTime),
                            _previewLayerTempClips,
                            _previewLayerTempTimes,
                            _previewLayerTempWeights,
                            fromLoopMode);

                        AppendRenderData(
                            layerClips,
                            layerTimes,
                            layerWeights,
                            _previewLayerTempClips,
                            _previewLayerTempTimes,
                            _previewLayerTempWeights,
                            fromScale);

                        if (currentState == null || toScale <= 0.000001f)
                        {
                            topState = fromState;
                            topEvaluator = evaluator;
                            topReferenceLength = fromReferenceLength;
                            topStateSampleTime = Mathf.Max(0.0f, evaluator.BlendFromStateTime);
                        }
                    }
                }

                if (toScale > 0.000001f &&
                    TryResolvePreviewMotion(currentState, _previewMotionSamplesA, out float referenceLength, out _, out _))
                {
                    PreviewMotionLoopMode toLoopMode = ResolveStateLoopMode(currentState);
                    BuildMotionRenderData(
                        _previewMotionSamplesA,
                        Mathf.Max(0.0f, evaluator.CurrentStateTime),
                        _previewLayerTempClips,
                        _previewLayerTempTimes,
                        _previewLayerTempWeights,
                        toLoopMode);

                    AppendRenderData(
                        layerClips,
                        layerTimes,
                        layerWeights,
                        _previewLayerTempClips,
                        _previewLayerTempTimes,
                        _previewLayerTempWeights,
                        toScale);

                    topState = currentState;
                    topEvaluator = evaluator;
                    topReferenceLength = referenceLength;
                    topStateSampleTime = Mathf.Max(0.0f, evaluator.CurrentStateTime);
                }

                if (layerClips.Count == 0)
                {
                    continue;
                }

                _previewLayerStackInputs.Add(new FusionAnimatorGraphView.PreviewLayerPoseInput(
                    layer,
                    layerClips,
                    layerTimes,
                    layerWeights,
                    layerWeight,
                    previewSingleLayer));

                Dictionary<string, float> clipInfluenceByName = new Dictionary<string, float>(StringComparer.Ordinal);
                int clipCount = Math.Min(layerClips.Count, layerWeights.Count);
                for (int clipIndex = 0; clipIndex < clipCount; ++clipIndex)
                {
                    AnimationClip clip = layerClips[clipIndex];
                    if (clip == null)
                    {
                        continue;
                    }

                    float clipInfluence = Mathf.Max(0.0f, layerWeights[clipIndex]) * layerWeight;
                    if (clipInfluence <= 0.000001f)
                    {
                        continue;
                    }

                    string clipName = string.IsNullOrWhiteSpace(clip.name) ? "<clip>" : clip.name;
                    if (clipInfluenceByName.TryGetValue(clipName, out float existingInfluence))
                    {
                        clipInfluenceByName[clipName] = existingInfluence + clipInfluence;
                    }
                    else
                    {
                        clipInfluenceByName.Add(clipName, clipInfluence);
                    }
                }

                if (clipInfluenceByName.Count > 0)
                {
                    List<KeyValuePair<string, float>> sortedClipInfluences = new List<KeyValuePair<string, float>>(clipInfluenceByName.Count);
                    foreach (KeyValuePair<string, float> pair in clipInfluenceByName)
                    {
                        sortedClipInfluences.Add(pair);
                    }

                    sortedClipInfluences.Sort((a, b) => b.Value.CompareTo(a.Value));
                    int maxClipEntries = Math.Min(3, sortedClipInfluences.Count);
                    List<string> clipParts = new List<string>(maxClipEntries + 1);
                    for (int clipIndex = 0; clipIndex < maxClipEntries; ++clipIndex)
                    {
                        KeyValuePair<string, float> clipPair = sortedClipInfluences[clipIndex];
                        clipParts.Add(clipPair.Key);
                    }

                    if (sortedClipInfluences.Count > maxClipEntries)
                    {
                        clipParts.Add(string.Format("+{0} more", sortedClipInfluences.Count - maxClipEntries));
                    }

                    layerClipSummaryByLayerId[layer.Id] = string.Join(", ", clipParts);
                }
                else
                {
                    layerClipSummaryByLayerId[layer.Id] = "<no sampled clip>";
                }

                ++composedLayers;
            }

            if (_previewLayerStackInputs.Count == 0 || _previewTarget == null)
            {
                _graphView?.ClearPreviewRuntimeMarkers();
                return false;
            }

            _previewResolvedByRuntime = true;
            _previewRuntimeEvaluator = topEvaluator;
            _previewRuntimeLayerId = topState != null ? topState.LayerId : string.Empty;
            _previewRuntimeScopePath = "<overview>";
            _previewRuntimeDefaultStateId = string.Empty;

            List<string> statusLines = new List<string>(4 + activeLayerStateLines.Count)
            {
                string.Format("Layer Stack | Layers: {0}", Mathf.Max(1, composedLayers))
            };

            if (activeLayerStateLines.Count > 0)
            {
                statusLines.Add("Active States:");
                for (int i = 0; i < activeLayerStateLines.Count; ++i)
                {
                    KeyValuePair<string, string> activeLine = activeLayerStateLines[i];
                    string clipSummary = layerClipSummaryByLayerId.TryGetValue(activeLine.Key, out string resolvedClipSummary)
                        ? resolvedClipSummary
                        : "<no sampled clip>";
                    statusLines.Add(string.Format("{0}\t{1}", activeLine.Value, clipSummary));
                }
            }
            else if (topState != null && topEvaluator != null)
            {
                _previewActiveStateId = topState.Id;
                _previewStateElapsed = Mathf.Max(0.0f, topEvaluator.CurrentStateElapsed);
                _previewTime = Mathf.Max(0.0f, topStateSampleTime);
                topReferenceLength = Mathf.Max(0.01f, topReferenceLength);
                PreviewMotionLoopMode topLoopMode = ResolveStateLoopMode(topState);
                float topTime = ResolveSampleTime(_previewTime, topReferenceLength, topLoopMode);
                string topClipSummary = "<no sampled clip>";
                if (string.IsNullOrWhiteSpace(topState.LayerId) == false &&
                    layerClipSummaryByLayerId.TryGetValue(topState.LayerId, out string resolvedTopClipSummary))
                {
                    topClipSummary = resolvedTopClipSummary;
                }

                statusLines.Add(string.Format(
                    "Active: {0} t={1:0.00}/{2:0.00}\t{3}",
                    topState.Name,
                    topTime,
                    topReferenceLength,
                    topClipSummary));
            }

            _previewStatus = string.Join("\n", statusLines);

            _graphView?.SetPreviewBackgroundStatus(_previewStatus);
            _graphView?.SetPreviewRuntimeMarkers(
                _previewActiveMarkerStateIds,
                _previewBlendMarkerStateIds,
                _previewActiveMarkerLayerIds);
            _graphView?.UpdatePreviewRenderLayerStack(
                _previewTarget,
                _previewLayerStackInputs);

            return true;
        }

        private void UpdatePreviewPlayback()
        {
            if (_graph == null)
            {
                _graphView?.SetPreviewBackgroundStatus(null);
                _graphView?.ClearPreviewRender();
                _graphView?.ClearPreviewRuntimeMarkers();
                return;
            }

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                StopPreviewSampling();
                _previewStatus = "Preview simulation is edit-mode only.";
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                _graphView?.ClearPreviewRender();
                _graphView?.ClearPreviewRuntimeMarkers();
                return;
            }

            if (_previewTarget != null && _previewTarget.Equals(null))
            {
                _previewTarget = null;
                RefreshPreviewToolbarValues();
            }

            EnsurePreviewEntries();

            UpdatePreviewEntriesFromInputActions();
            _inspector?.MarkDirtyRepaint();

            double now = EditorApplication.timeSinceStartup;
            if (_previewLastEditorTime <= 0.0)
            {
                _previewLastEditorTime = now;
            }

            float delta = (float)Math.Max(0.0, now - _previewLastEditorTime);
            _previewLastEditorTime = now;
            if (delta <= 0.000001f && _previewEnabled && _previewPlay)
            {
                delta = 1.0f / 60.0f;
            }
            _previewStepDeltaTime = delta * Mathf.Max(0.01f, _previewPlaySpeed);
            _previewResolvedByRuntime = false;

            if (_previewEnabled == false)
            {
                _previewStatus = "Preview disabled.";
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                _graphView?.ClearPreviewRender();
                _graphView?.ClearPreviewRuntimeMarkers();
                return;
            }

            if (_previewPlay == false)
            {
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                return;
            }

            Repaint();

            if (HasExplicitPreviewSelection() == false &&
                TryRenderRuntimeLayerStackPreview())
            {
                return;
            }

            _graphView?.ClearPreviewRuntimeMarkers();

            _previewStateElapsed += delta;
            if (IsPreviewBlendActive())
            {
                _previewBlendElapsed += delta;
            }

            if (TryResolvePreviewState(out FusionAnimatorStateDefinition state, out FusionAnimatorStateDefinition blendFromState, out float blendWeight) == false)
            {
                _previewStatus = "No preview state available.";
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                _graphView?.ClearPreviewRender();
                _graphView?.ClearPreviewRuntimeMarkers();
                return;
            }

            if (TryResolvePreviewMotion(state, _previewMotionSamplesA, out float referenceLength, out float playbackScale, out string motionLabel) == false)
            {
                _previewStatus = string.Format("State '{0}' has no previewable motion clip.", state.Name);
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                _graphView?.ClearPreviewRender();
                return;
            }

            if (_previewTarget == null)
            {
                _previewStatus = string.Format("Assign Preview Target to play '{0}' ({1}).", motionLabel, state.Name);
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                _graphView?.ClearPreviewRender();
                return;
            }

            bool blending = blendFromState != null && blendWeight >= 0.0f && blendWeight <= 1.0f;
            float blendFromReferenceLength = 0.0f;
            float blendFromPlaybackScale = 1.0f;
            string blendFromLabel = string.Empty;
            if (blending && TryResolvePreviewMotion(blendFromState, _previewMotionSamplesB, out blendFromReferenceLength, out blendFromPlaybackScale, out blendFromLabel) == false)
            {
                blending = false;
            }

            PreviewMotionLoopMode destinationLoopMode = HasExplicitPreviewSelection()
                ? PreviewMotionLoopMode.ForceLoop
                : ResolveStateLoopMode(state);
             
            float clipLength = Mathf.Max(0.01f, referenceLength);
            float destinationMotionTime;
            float destinationSampleTime;
            if (_previewResolvedByRuntime)
            {
                destinationMotionTime = _previewTime;
                destinationSampleTime = ResolveSampleTime(destinationMotionTime, clipLength, destinationLoopMode);
            }
            else
            {
                _previewTime += delta * Mathf.Max(0.01f, _previewPlaySpeed);
                destinationMotionTime = _previewTime;
                destinationSampleTime = ResolveSampleTime(destinationMotionTime, clipLength, destinationLoopMode);
                if (_selectedTransition != null)
                {
                    destinationMotionTime += Mathf.Clamp01(_selectedTransition.StartOffsetNormalized) * clipLength;
                    destinationSampleTime = ResolveSampleTime(destinationMotionTime, clipLength, destinationLoopMode);
                }
            }
             
            BuildMotionRenderData(
                _previewMotionSamplesA,
                destinationMotionTime,
                _previewRenderClipsA,
                _previewRenderTimesA,
                _previewRenderWeightsA,
                destinationLoopMode);

            if (blending)
            {
                float fromLength = Mathf.Max(0.01f, blendFromReferenceLength);
                float fromMotionTime;
                float fromSampleTime;
                PreviewMotionLoopMode fromLoopMode = HasExplicitPreviewSelection()
                    ? PreviewMotionLoopMode.ForceLoop
                    : ResolveStateLoopMode(blendFromState);
                if (_previewResolvedByRuntime)
                {
                    fromMotionTime = _previewBlendFromTime;
                    fromSampleTime = ResolveSampleTime(fromMotionTime, fromLength, fromLoopMode);
                }
                else
                {
                    _previewBlendFromTime += delta * Mathf.Max(0.01f, _previewPlaySpeed);
                    fromMotionTime = _previewBlendFromTime;
                    fromSampleTime = ResolveSampleTime(fromMotionTime, fromLength, fromLoopMode);
                }
                BuildMotionRenderData(
                    _previewMotionSamplesB,
                    fromMotionTime,
                    _previewRenderClipsB,
                    _previewRenderTimesB,
                    _previewRenderWeightsB,
                    fromLoopMode);

                _previewStatus = string.Format(
                    "Blend: {0} -> {1} ({2:P0}) | Out:{3:0.00}/{4:0.00} In:{5:0.00}/{6:0.00}",
                    string.IsNullOrWhiteSpace(blendFromLabel) ? blendFromState.Name : blendFromLabel,
                    string.IsNullOrWhiteSpace(motionLabel) ? state.Name : motionLabel,
                    blendWeight,
                    fromSampleTime,
                    fromLength,
                    destinationSampleTime,
                    clipLength);
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                _graphView?.UpdatePreviewRenderBlendedWeighted(
                    _previewTarget,
                    _previewRenderClipsB,
                    _previewRenderTimesB,
                    _previewRenderWeightsB,
                    _previewRenderClipsA,
                    _previewRenderTimesA,
                    _previewRenderWeightsA,
                    blendWeight);
            }
            else
            {
                _previewStatus = string.Format("State: {0} | Clip: {1} | t={2:0.00}/{3:0.00}", state.Name, motionLabel, destinationSampleTime, clipLength);
                _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                _graphView?.UpdatePreviewRenderWeighted(
                    _previewTarget,
                    _previewRenderClipsA,
                    _previewRenderTimesA,
                    _previewRenderWeightsA);
            }
        }

        private void DrawIntegratedPreviewPanel()
        {
            EnsurePreviewEntries();

            EditorGUILayout.LabelField("Preview", EditorStyles.boldLabel);
            _previewButtonThreshold = EditorGUILayout.Slider(new GUIContent("Activation Threshold", "Threshold/deadzone used when converting gamepad scalar values (including stick magnitude bindings) to bool/trigger preview values."), _previewButtonThreshold, 0.01f, 0.99f);
            MessageType statusType = _previewTarget == null ? MessageType.Warning : MessageType.Info;
            EditorGUILayout.HelpBox(_previewStatus, statusType);
            EditorGUILayout.LabelField(
                new GUIContent("Active State Id", "Current preview-simulated state id when no explicit state is selected."),
                new GUIContent(string.IsNullOrWhiteSpace(_previewActiveStateId) ? "<none>" : _previewActiveStateId));
            EditorGUILayout.LabelField(
                new GUIContent("State Elapsed", "Time in current simulated preview state (seconds)."),
                new GUIContent(_previewStateElapsed.ToString("0.000")));
            EditorGUILayout.LabelField(
                new GUIContent("Simulation", "Preview simulation backend currently used for edit-mode playback."),
                new GUIContent(_previewResolvedByRuntime ? "Runtime Evaluator" : "Explicit Override"));

            if (_previewRuntimeEvaluator != null)
            {
                EditorGUILayout.Space(4.0f);
                EditorGUILayout.LabelField("Runtime Evaluator", EditorStyles.boldLabel);
                EditorGUILayout.LabelField(
                    new GUIContent("Layer", "Active layer id currently being simulated by runtime evaluator."),
                    new GUIContent(string.IsNullOrWhiteSpace(_previewRuntimeLayerId) ? "<none>" : _previewRuntimeLayerId));
                EditorGUILayout.LabelField(
                    new GUIContent("Scope", "Active scope path currently being simulated by runtime evaluator."),
                    new GUIContent(string.IsNullOrWhiteSpace(_previewRuntimeScopePath) ? "<root>" : _previewRuntimeScopePath));
                EditorGUILayout.LabelField(
                    new GUIContent("Default", "Resolved default state id used for evaluator reset/context."),
                    new GUIContent(string.IsNullOrWhiteSpace(_previewRuntimeDefaultStateId) ? "<none>" : _previewRuntimeDefaultStateId));
                EditorGUILayout.LabelField(
                    new GUIContent("Transition", "Active transition id in evaluator, if blending/transitioning."),
                    new GUIContent(string.IsNullOrWhiteSpace(_previewRuntimeEvaluator.ActiveTransitionId) ? "<none>" : _previewRuntimeEvaluator.ActiveTransitionId));
                EditorGUILayout.LabelField(
                    new GUIContent("Blend", "Current runtime blend alpha."),
                    new GUIContent(_previewRuntimeEvaluator.BlendAlpha.ToString("0.000")));
            }

            if (GUILayout.Button(new GUIContent("Reset Preview Defaults", "Reset all temporary parameter values to graph defaults.")))
            {
                ApplyPreviewDefaults();
                _previewTime = 0.0f;
                _previewBlendFromTime = 0.0f;
                _previewStateElapsed = 0.0f;
                _previewActiveStateId = null;
                _previewBlendFromStateId = null;
                _previewBlendToStateId = null;
                _previewLoopTransitionId = null;
                _previewBlendElapsed = 0.0f;
                _previewBlendDuration = 0.0f;
            }
        }

        private void DrawParameterPreviewOverride(FusionAnimatorParameterDefinition parameter)
        {
            if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
            {
                return;
            }

            EnsurePreviewEntries();
            PreviewParameterEntry entry = FindPreviewEntry(parameter.Id);
            if (entry == null)
            {
                return;
            }

            EditorGUILayout.Space(10.0f);
            EditorGUILayout.LabelField("Preview Override", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                case FusionAnimatorParameterType.Trigger:
                    entry.BoolValue = EditorGUILayout.Toggle(new GUIContent("Temp Value", "Temporary preview value used in edit-mode transition simulation."), entry.BoolValue);
                    entry.ScalarBinding = (PreviewGamepadScalarBinding)EditorGUILayout.EnumPopup(
                        new GUIContent("Scalar Binding", "Scalar binding sampled for bool/trigger conversion when no Vector2 Binding is selected."),
                        entry.ScalarBinding);
                    entry.Vector2Binding = (PreviewGamepadVector2Binding)EditorGUILayout.EnumPopup(
                        new GUIContent("Vector2 Binding", "Optional Vector2 binding sampled for magnitude-based conversion (takes precedence over Scalar Binding)."),
                        entry.Vector2Binding);
                    entry.BindingScale = EditorGUILayout.FloatField(new GUIContent("Binding Scale", "Scale multiplier applied to bound input before conversion."), entry.BindingScale);

                    entry.BoolInputOperator = DrawPreviewBoolInputOperatorField(entry.BoolInputOperator);
                    if (UsesPreviewBoolInputCompareValue(entry.BoolInputOperator))
                    {
                        entry.BoolInputCompareValue = EditorGUILayout.FloatField(new GUIContent("Compare Value", "Comparison target value used by the selected operator."), entry.BoolInputCompareValue);
                    }
                    break;
                case FusionAnimatorParameterType.Int:
                    entry.IntValue = EditorGUILayout.IntField(new GUIContent("Temp Value", "Temporary preview value used in edit-mode transition simulation."), entry.IntValue);
                    entry.ScalarBinding = (PreviewGamepadScalarBinding)EditorGUILayout.EnumPopup(new GUIContent("Gamepad Binding", "Direct gamepad control mapped to this preview parameter."), entry.ScalarBinding);
                    entry.BindingScale = EditorGUILayout.FloatField(new GUIContent("Binding Scale", "Scale multiplier applied to gamepad scalar input before writing this preview value."), entry.BindingScale);
                    break;
                case FusionAnimatorParameterType.Float:
                    entry.FloatValue = EditorGUILayout.FloatField(new GUIContent("Temp Value", "Temporary preview value used in edit-mode transition simulation."), entry.FloatValue);
                    entry.ScalarBinding = (PreviewGamepadScalarBinding)EditorGUILayout.EnumPopup(new GUIContent("Gamepad Binding", "Direct gamepad control mapped to this preview parameter."), entry.ScalarBinding);
                    entry.BindingScale = EditorGUILayout.FloatField(new GUIContent("Binding Scale", "Scale multiplier applied to gamepad scalar input before writing this preview value."), entry.BindingScale);
                    break;
                case FusionAnimatorParameterType.Vector2:
                    entry.Vector2Value = EditorGUILayout.Vector2Field(new GUIContent("Temp Value", "Temporary preview value used in edit-mode transition simulation."), entry.Vector2Value);
                    entry.Vector2Binding = (PreviewGamepadVector2Binding)EditorGUILayout.EnumPopup(new GUIContent("Gamepad Binding", "Direct gamepad vector mapped to this preview parameter."), entry.Vector2Binding);
                    entry.BindingScale = EditorGUILayout.FloatField(new GUIContent("Binding Scale", "Scale multiplier applied to gamepad vector input before writing this preview value."), entry.BindingScale);
                    break;
            }

            if (EditorGUI.EndChangeCheck())
            {
                PersistPreviewInputBinding(parameter, entry);
            }
        }

        private static FusionAnimatorConditionOperator DrawPreviewBoolInputOperatorField(FusionAnimatorConditionOperator current)
        {
            FusionAnimatorConditionOperator normalized = NormalizePreviewBoolInputOperator(current);
            int index = 0;
            for (int i = 0; i < PreviewBoolInputOperators.Length; ++i)
            {
                if (PreviewBoolInputOperators[i] == normalized)
                {
                    index = i;
                    break;
                }
            }

            int nextIndex = EditorGUILayout.Popup(
                new GUIContent("Operator", "Operator used to convert bound input into a bool/trigger value."),
                index,
                PreviewBoolInputOperatorLabels);
            if (nextIndex < 0 || nextIndex >= PreviewBoolInputOperators.Length)
            {
                return normalized;
            }

            return PreviewBoolInputOperators[nextIndex];
        }

        private static FusionAnimatorConditionOperator NormalizePreviewBoolInputOperator(FusionAnimatorConditionOperator value)
        {
            for (int i = 0; i < PreviewBoolInputOperators.Length; ++i)
            {
                if (PreviewBoolInputOperators[i] == value)
                {
                    return value;
                }
            }

            return FusionAnimatorConditionOperator.Greater;
        }

        private static bool UsesPreviewBoolInputCompareValue(FusionAnimatorConditionOperator op)
        {
            return op != FusionAnimatorConditionOperator.IsTrue && op != FusionAnimatorConditionOperator.IsFalse;
        }

        private static string FormatConditionOperatorLabel(FusionAnimatorConditionOperator op)
        {
            switch (op)
            {
                case FusionAnimatorConditionOperator.IsTrue: return "Is True";
                case FusionAnimatorConditionOperator.IsFalse: return "Is False";
                case FusionAnimatorConditionOperator.Equal: return "Equal";
                case FusionAnimatorConditionOperator.NotEqual: return "Not Equal";
                case FusionAnimatorConditionOperator.Greater: return "Greater";
                case FusionAnimatorConditionOperator.GreaterOrEqual: return "Greater Or Equal";
                case FusionAnimatorConditionOperator.Less: return "Less";
                case FusionAnimatorConditionOperator.LessOrEqual: return "Less Or Equal";
                default: return op.ToString();
            }
        }

        private void EnsurePreviewEntries()
        {
            if (_graph == null)
            {
                _previewParameterEntries.Clear();
                return;
            }

            EnsureGraphCollections();

            HashSet<string> validIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < _graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
                {
                    continue;
                }

                validIds.Add(parameter.Id);
                PreviewParameterEntry existing = FindPreviewEntry(parameter.Id);
                if (existing != null)
                {
                    ApplyPersistedPreviewBinding(parameter, existing);
                    continue;
                }

                _previewParameterEntries.Add(new PreviewParameterEntry
                {
                    ParameterId = parameter.Id,
                    BoolValue = parameter.DefaultBool,
                    IntValue = parameter.DefaultInt,
                    FloatValue = parameter.DefaultFloat,
                    Vector2Value = parameter.DefaultVector2,
                });

                PreviewParameterEntry added = FindPreviewEntry(parameter.Id);
                if (added != null)
                {
                    ApplyPersistedPreviewBinding(parameter, added);
                }
            }

            _previewParameterEntries.RemoveAll(entry => entry == null || string.IsNullOrWhiteSpace(entry.ParameterId) || validIds.Contains(entry.ParameterId) == false);
        }

        private static void ApplyPersistedPreviewBinding(FusionAnimatorParameterDefinition parameter, PreviewParameterEntry entry)
        {
            if (parameter == null || entry == null)
            {
                return;
            }

            entry.BindingScale = parameter.PreviewInputScale <= 0.0001f ? 1.0f : parameter.PreviewInputScale;
            entry.ScalarBinding = PreviewGamepadScalarBinding.None;
            entry.Vector2Binding = PreviewGamepadVector2Binding.None;
            entry.BoolInputOperator = NormalizePreviewBoolInputOperator(parameter.PreviewBoolInputOperator);
            entry.BoolInputCompareValue = parameter.PreviewBoolInputCompareValue;

            if (string.IsNullOrWhiteSpace(parameter.PreviewInputBinding))
            {
                return;
            }

            string raw = parameter.PreviewInputBinding.Trim();
            if (raw.StartsWith("scalar:", StringComparison.OrdinalIgnoreCase))
            {
                string value = raw.Substring("scalar:".Length);
                if (Enum.TryParse(value, true, out PreviewGamepadScalarBinding scalarBinding))
                {
                    entry.ScalarBinding = scalarBinding;
                }
            }
            else if (raw.StartsWith("vector2:", StringComparison.OrdinalIgnoreCase))
            {
                string value = raw.Substring("vector2:".Length);
                if (Enum.TryParse(value, true, out PreviewGamepadVector2Binding vectorBinding))
                {
                    entry.Vector2Binding = vectorBinding;
                }
            }
        }

        private void PersistPreviewInputBinding(FusionAnimatorParameterDefinition parameter, PreviewParameterEntry entry)
        {
            if (parameter == null || entry == null)
            {
                return;
            }

            string bindingValue;
            bool usesVector2Binding =
                parameter.Type == FusionAnimatorParameterType.Vector2 ||
                ((parameter.Type == FusionAnimatorParameterType.Bool || parameter.Type == FusionAnimatorParameterType.Trigger) &&
                 entry.Vector2Binding != PreviewGamepadVector2Binding.None);
            if (usesVector2Binding)
            {
                bindingValue = entry.Vector2Binding == PreviewGamepadVector2Binding.None
                    ? string.Empty
                    : string.Format("vector2:{0}", entry.Vector2Binding);
            }
            else
            {
                bindingValue = entry.ScalarBinding == PreviewGamepadScalarBinding.None
                    ? string.Empty
                    : string.Format("scalar:{0}", entry.ScalarBinding);
            }

            float bindingScale = entry.BindingScale <= 0.0001f ? 1.0f : entry.BindingScale;
            FusionAnimatorConditionOperator boolInputOperator = NormalizePreviewBoolInputOperator(entry.BoolInputOperator);
            float boolInputCompareValue = entry.BoolInputCompareValue;
            if (string.Equals(parameter.PreviewInputBinding, bindingValue, StringComparison.Ordinal) &&
                Mathf.Approximately(parameter.PreviewInputScale, bindingScale) &&
                parameter.PreviewBoolInputOperator == boolInputOperator &&
                Mathf.Approximately(parameter.PreviewBoolInputCompareValue, boolInputCompareValue))
            {
                return;
            }

            if (_graph != null)
            {
                Undo.RecordObject(_graph, "Set FusionAnimator Preview Input Binding");
            }

            parameter.PreviewInputBinding = bindingValue;
            parameter.PreviewInputScale = bindingScale;
            parameter.PreviewBoolInputOperator = boolInputOperator;
            parameter.PreviewBoolInputCompareValue = boolInputCompareValue;
            if (_graph != null)
            {
                EditorUtility.SetDirty(_graph);
                AssetDatabase.SaveAssets();
            }

            MarkGraphDirty();
        }

        private void ApplyPreviewDefaults()
        {
            if (_graph == null || _graph.Parameters == null)
            {
                return;
            }

            for (int i = 0; i < _graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
                {
                    continue;
                }

                PreviewParameterEntry entry = FindPreviewEntry(parameter.Id);
                if (entry == null)
                {
                    continue;
                }

                entry.BoolValue = parameter.DefaultBool;
                entry.IntValue = parameter.DefaultInt;
                entry.FloatValue = parameter.DefaultFloat;
                entry.Vector2Value = parameter.DefaultVector2;
            }
        }

        private void UpdatePreviewEntriesFromInputActions()
        {
            if (TryReadGamepadSnapshot(out PreviewGamepadSnapshot snapshot) == false || snapshot.IsConnected == false)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < _previewParameterEntries.Count; ++i)
            {
                PreviewParameterEntry entry = _previewParameterEntries[i];
                if (entry == null)
                {
                    continue;
                }

                FusionAnimatorParameterDefinition parameter = FindParameterById(entry.ParameterId);
                if (parameter == null)
                {
                    continue;
                }

                switch (parameter.Type)
                {
                    case FusionAnimatorParameterType.Bool:
                    case FusionAnimatorParameterType.Trigger:
                        if (TryResolvePreviewBoolInputFromBinding(entry, snapshot, out bool next))
                        {
                            if (parameter.Type == FusionAnimatorParameterType.Bool && parameter.Invert)
                            {
                                next = next == false;
                            }

                            if (entry.BoolValue != next)
                            {
                                entry.BoolValue = next;
                                changed = true;
                            }
                        }
                        break;
                    case FusionAnimatorParameterType.Int:
                        if (entry.ScalarBinding != PreviewGamepadScalarBinding.None)
                        {
                            int nextInt = Mathf.RoundToInt(ReadScalarBinding(snapshot, entry.ScalarBinding) * entry.BindingScale);
                            if (entry.IntValue != nextInt)
                            {
                                entry.IntValue = nextInt;
                                changed = true;
                            }
                        }
                        break;
                    case FusionAnimatorParameterType.Float:
                        if (entry.ScalarBinding != PreviewGamepadScalarBinding.None)
                        {
                            float nextFloat = ReadScalarBinding(snapshot, entry.ScalarBinding) * entry.BindingScale;
                            if (Mathf.Abs(entry.FloatValue - nextFloat) > 0.0001f)
                            {
                                entry.FloatValue = nextFloat;
                                changed = true;
                            }
                        }
                        break;
                    case FusionAnimatorParameterType.Vector2:
                        if (entry.Vector2Binding != PreviewGamepadVector2Binding.None)
                        {
                            Vector2 nextVector2 = ReadVector2Binding(snapshot, entry.Vector2Binding) * entry.BindingScale;
                            if ((entry.Vector2Value - nextVector2).sqrMagnitude > 0.00000001f)
                            {
                                entry.Vector2Value = nextVector2;
                                changed = true;
                            }
                        }
                    break;
                }
            }

            // Keep runtime evaluator state/time continuous while parameters change.
            // Parameter values are consumed on the next Step without rebuilding simulation context.
            _ = changed;
        }

        private static bool TryResolvePreviewBoolInputFromBinding(PreviewParameterEntry entry, PreviewGamepadSnapshot snapshot, out bool value)
        {
            value = false;
            if (entry == null)
            {
                return false;
            }

            float lhs;
            float rhs = entry.BoolInputCompareValue;
            if (entry.Vector2Binding != PreviewGamepadVector2Binding.None)
            {
                Vector2 sampled = ReadVector2Binding(snapshot, entry.Vector2Binding) * entry.BindingScale;
                lhs = sampled.magnitude;
            }
            else
            {
                if (entry.ScalarBinding == PreviewGamepadScalarBinding.None)
                {
                    return false;
                }

                lhs = ReadScalarBinding(snapshot, entry.ScalarBinding) * entry.BindingScale;
            }

            FusionAnimatorConditionOperator op = NormalizePreviewBoolInputOperator(entry.BoolInputOperator);
            switch (op)
            {
                case FusionAnimatorConditionOperator.IsTrue:
                    value = Mathf.Abs(lhs) > 0.000001f;
                    return true;
                case FusionAnimatorConditionOperator.IsFalse:
                    value = Mathf.Abs(lhs) <= 0.000001f;
                    return true;
                default:
                    value = CompareNumeric(lhs, rhs, op);
                    return true;
            }
        }

        private static float ReadScalarBinding(PreviewGamepadSnapshot snapshot, PreviewGamepadScalarBinding binding)
        {
            switch (binding)
            {
                case PreviewGamepadScalarBinding.LeftStickX: return snapshot.LeftStick.x;
                case PreviewGamepadScalarBinding.LeftStickY: return snapshot.LeftStick.y;
                case PreviewGamepadScalarBinding.RightStickX: return snapshot.RightStick.x;
                case PreviewGamepadScalarBinding.RightStickY: return snapshot.RightStick.y;
                case PreviewGamepadScalarBinding.LeftStickMagnitude: return Mathf.Clamp01(snapshot.LeftStick.magnitude);
                case PreviewGamepadScalarBinding.RightStickMagnitude: return Mathf.Clamp01(snapshot.RightStick.magnitude);
                case PreviewGamepadScalarBinding.LeftTrigger: return snapshot.LeftTrigger;
                case PreviewGamepadScalarBinding.RightTrigger: return snapshot.RightTrigger;
                case PreviewGamepadScalarBinding.DpadX: return snapshot.Dpad.x;
                case PreviewGamepadScalarBinding.DpadY: return snapshot.Dpad.y;
                case PreviewGamepadScalarBinding.DpadMagnitude: return Mathf.Clamp01(snapshot.Dpad.magnitude);
                case PreviewGamepadScalarBinding.ButtonSouth: return snapshot.ButtonSouth ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.ButtonEast: return snapshot.ButtonEast ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.ButtonWest: return snapshot.ButtonWest ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.ButtonNorth: return snapshot.ButtonNorth ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.LeftShoulder: return snapshot.LeftShoulder ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.RightShoulder: return snapshot.RightShoulder ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.Start: return snapshot.Start ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.Select: return snapshot.Select ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.LeftStickPress: return snapshot.LeftStickPress ? 1.0f : 0.0f;
                case PreviewGamepadScalarBinding.RightStickPress: return snapshot.RightStickPress ? 1.0f : 0.0f;
                default: return 0.0f;
            }
        }

        private static Vector2 ReadVector2Binding(PreviewGamepadSnapshot snapshot, PreviewGamepadVector2Binding binding)
        {
            switch (binding)
            {
                case PreviewGamepadVector2Binding.LeftStick: return snapshot.LeftStick;
                case PreviewGamepadVector2Binding.RightStick: return snapshot.RightStick;
                case PreviewGamepadVector2Binding.Dpad: return snapshot.Dpad;
                default: return Vector2.zero;
            }
        }

        private static bool TryReadGamepadSnapshot(out PreviewGamepadSnapshot snapshot)
        {
            snapshot = default;
            return TryReadXInputSnapshot(out snapshot);
        }

#if UNITY_EDITOR_WIN
        [StructLayout(LayoutKind.Sequential)]
        private struct XInputState
        {
            public uint PacketNumber;
            public XInputGamepad Gamepad;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct XInputGamepad
        {
            public ushort Buttons;
            public byte LeftTrigger;
            public byte RightTrigger;
            public short ThumbLX;
            public short ThumbLY;
            public short ThumbRX;
            public short ThumbRY;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState14(uint userIndex, out XInputState state);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputGetState")]
        private static extern uint XInputGetState910(uint userIndex, out XInputState state);

        private const ushort XInputButtonDpadUp = 0x0001;
        private const ushort XInputButtonDpadDown = 0x0002;
        private const ushort XInputButtonDpadLeft = 0x0004;
        private const ushort XInputButtonDpadRight = 0x0008;
        private const ushort XInputButtonStart = 0x0010;
        private const ushort XInputButtonBack = 0x0020;
        private const ushort XInputButtonLeftThumb = 0x0040;
        private const ushort XInputButtonRightThumb = 0x0080;
        private const ushort XInputButtonLeftShoulder = 0x0100;
        private const ushort XInputButtonRightShoulder = 0x0200;
        private const ushort XInputButtonA = 0x1000;
        private const ushort XInputButtonB = 0x2000;
        private const ushort XInputButtonX = 0x4000;
        private const ushort XInputButtonY = 0x8000;

        private const int XInputLeftThumbDeadzone = 7849;
        private const int XInputRightThumbDeadzone = 8689;
        private const byte XInputTriggerThreshold = 30;

        private static bool TryReadXInputSnapshot(out PreviewGamepadSnapshot snapshot)
        {
            snapshot = default;
            for (uint user = 0; user < 4; ++user)
            {
                if (TryGetXInputState(user, out XInputState state) == false)
                {
                    continue;
                }

                XInputGamepad pad = state.Gamepad;
                snapshot.IsConnected = true;
                snapshot.LeftStick = new Vector2(
                    NormalizeThumb(pad.ThumbLX, XInputLeftThumbDeadzone),
                    NormalizeThumb(pad.ThumbLY, XInputLeftThumbDeadzone));
                snapshot.RightStick = new Vector2(
                    NormalizeThumb(pad.ThumbRX, XInputRightThumbDeadzone),
                    NormalizeThumb(pad.ThumbRY, XInputRightThumbDeadzone));
                snapshot.LeftTrigger = NormalizeTrigger(pad.LeftTrigger);
                snapshot.RightTrigger = NormalizeTrigger(pad.RightTrigger);
                snapshot.Dpad = new Vector2(
                    ((pad.Buttons & XInputButtonDpadRight) != 0 ? 1.0f : 0.0f) - ((pad.Buttons & XInputButtonDpadLeft) != 0 ? 1.0f : 0.0f),
                    ((pad.Buttons & XInputButtonDpadUp) != 0 ? 1.0f : 0.0f) - ((pad.Buttons & XInputButtonDpadDown) != 0 ? 1.0f : 0.0f));
                snapshot.ButtonSouth = (pad.Buttons & XInputButtonA) != 0;
                snapshot.ButtonEast = (pad.Buttons & XInputButtonB) != 0;
                snapshot.ButtonWest = (pad.Buttons & XInputButtonX) != 0;
                snapshot.ButtonNorth = (pad.Buttons & XInputButtonY) != 0;
                snapshot.LeftShoulder = (pad.Buttons & XInputButtonLeftShoulder) != 0;
                snapshot.RightShoulder = (pad.Buttons & XInputButtonRightShoulder) != 0;
                snapshot.Start = (pad.Buttons & XInputButtonStart) != 0;
                snapshot.Select = (pad.Buttons & XInputButtonBack) != 0;
                snapshot.LeftStickPress = (pad.Buttons & XInputButtonLeftThumb) != 0;
                snapshot.RightStickPress = (pad.Buttons & XInputButtonRightThumb) != 0;
                return true;
            }

            return false;
        }

        private static bool TryGetXInputState(uint userIndex, out XInputState state)
        {
            state = default;
            try
            {
                if (XInputGetState14(userIndex, out state) == 0)
                {
                    return true;
                }
            }
            catch
            {
                // ignored, fallback below
            }

            try
            {
                if (XInputGetState910(userIndex, out state) == 0)
                {
                    return true;
                }
            }
            catch
            {
                // ignored
            }

            return false;
        }

        private static float NormalizeThumb(short value, int deadzone)
        {
            int abs = Mathf.Abs(value);
            if (abs <= deadzone)
            {
                return 0.0f;
            }

            int sign = value >= 0 ? 1 : -1;
            float normalized = (abs - deadzone) / (32767.0f - deadzone);
            return Mathf.Clamp(normalized * sign, -1.0f, 1.0f);
        }

        private static float NormalizeTrigger(byte value)
        {
            if (value <= XInputTriggerThreshold)
            {
                return 0.0f;
            }

            return (value - XInputTriggerThreshold) / (255.0f - XInputTriggerThreshold);
        }
#else
        private static bool TryReadXInputSnapshot(out PreviewGamepadSnapshot snapshot)
        {
            snapshot = default;
            return false;
        }
#endif

        private bool TryResolvePreviewState(out FusionAnimatorStateDefinition state, out FusionAnimatorStateDefinition blendFromState, out float blendWeight)
        {
            _previewResolvedByRuntime = false;
            blendFromState = null;
            blendWeight = 0.0f;
            state = _selectedState;
            if (state != null)
            {
                ClearPreviewBlend();
                _previewActiveStateId = state.Id;
                return true;
            }

            if (_selectedTransition != null)
            {
                if (string.Equals(_previewLoopTransitionId, _selectedTransition.Id, StringComparison.Ordinal) == false)
                {
                    _previewLoopTransitionId = _selectedTransition.Id;
                    _previewTime = 0.0f;
                    _previewBlendFromTime = 0.0f;
                    _previewStateElapsed = 0.0f;
                }

                FusionAnimatorStateDefinition fromState = FindStateById(_selectedTransition.FromStateId);
                FusionAnimatorStateDefinition toState = FindStateById(_selectedTransition.ToStateId) ?? fromState;
                if (fromState == null)
                {
                    fromState = toState;
                }

                if (toState == null)
                {
                    return false;
                }

                ClearPreviewBlend();
                state = toState;
                _previewActiveStateId = state.Id;

                if (fromState != null && string.Equals(fromState.Id, toState.Id, StringComparison.Ordinal) == false)
                {
                    float loopDuration = ResolveTransitionBlendDurationSeconds(_selectedTransition, fromState);
                    if (loopDuration > 0.0001f)
                    {
                        blendFromState = fromState;
                        blendWeight = Mathf.Repeat(_previewStateElapsed, loopDuration) / loopDuration;
                    }
                }

                return true;
            }

            if (string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false)
            {
                FusionAnimatorStateDefinition entryTargetState = FindStateById(_selectedEntryLinkTargetStateId);
                if (entryTargetState != null)
                {
                    ClearPreviewBlend();
                    _previewLoopTransitionId = null;
                    _previewStateElapsed = 0.0f;
                    _previewTime = 0.0f;
                    _previewActiveStateId = entryTargetState.Id;
                    state = entryTargetState;
                    return true;
                }
            }

            if (TryResolvePreviewStateUsingRuntime(out state, out blendFromState, out blendWeight))
            {
                return true;
            }

            ClearPreviewBlend();
            _previewLoopTransitionId = null;
            return false;
        }

        private FusionAnimatorStateDefinition ResolveScopeDefaultPreviewState(string layerId, string scopePath)
        {
            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            string normalizedScope = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath.Trim();

            if (string.IsNullOrWhiteSpace(normalizedScope) && string.IsNullOrWhiteSpace(_graph.EntryStateId) == false)
            {
                FusionAnimatorStateDefinition rootDefault = FindStateById(_graph.EntryStateId);
                if (rootDefault != null &&
                    string.Equals(rootDefault.LayerId, layerId, StringComparison.Ordinal) &&
                    string.IsNullOrWhiteSpace(GetStateScopePathFromName(rootDefault.Name)))
                {
                    return rootDefault;
                }
            }

            if (_graph.Transitions != null)
            {
                for (int i = 0; i < _graph.Transitions.Count; ++i)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                    if (transition == null || string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    FusionAnimatorStateDefinition destinationState = FindStateById(transition.ToStateId);
                    if (destinationState == null || string.Equals(destinationState.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string destinationScope = GetStateScopePathFromName(destinationState.Name);
                    if (string.Equals(destinationScope, normalizedScope, StringComparison.OrdinalIgnoreCase))
                    {
                        return destinationState;
                    }
                }
            }

            FusionAnimatorStateDefinition fallback = null;
            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition candidate = _graph.States[i];
                if (candidate == null || string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string candidateScope = GetStateScopePathFromName(candidate.Name);
                if (string.Equals(candidateScope, normalizedScope, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                if (fallback == null || string.Compare(candidate.Name, fallback.Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    fallback = candidate;
                }
            }

            return fallback;
        }

        private static bool IsStateInPreviewContext(FusionAnimatorStateDefinition state, string layerId, string selectedScopePath)
        {
            if (state == null || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            if (string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
            {
                return false;
            }

            string normalizedSelectedScope = NormalizeScopePath(selectedScopePath);
            if (string.IsNullOrWhiteSpace(normalizedSelectedScope))
            {
                return true;
            }

            string stateScope = NormalizeScopePath(GetStateScopePathFromName(state.Name));
            if (string.Equals(stateScope, normalizedSelectedScope, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return stateScope.StartsWith(normalizedSelectedScope + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeScopePath(string scopePath)
        {
            if (string.IsNullOrWhiteSpace(scopePath))
            {
                return string.Empty;
            }

            return scopePath.Trim().Trim('/');
        }

        private bool IsPreviewBlendActive()
        {
            return string.IsNullOrWhiteSpace(_previewBlendFromStateId) == false &&
                   string.IsNullOrWhiteSpace(_previewBlendToStateId) == false &&
                   _previewBlendDuration > 0.0001f;
        }

        private void ClearPreviewBlend()
        {
            _previewBlendFromStateId = null;
            _previewBlendToStateId = null;
            _previewBlendElapsed = 0.0f;
            _previewBlendDuration = 0.0f;
            _previewBlendFromTime = 0.0f;
        }

        private void ResetPreviewToDefaultSimulation()
        {
            _previewLoopTransitionId = null;
            _previewActiveStateId = null;
            _previewStateElapsed = 0.0f;
            _previewTime = 0.0f;
            _previewBlendFromTime = 0.0f;
            ClearPreviewBlend();
            InvalidatePreviewRuntimeSimulation();
        }

        private float ResolveTransitionBlendDurationSeconds(FusionAnimatorTransitionDefinition transition, FusionAnimatorStateDefinition fromState)
        {
            if (transition == null)
            {
                return 0.0f;
            }

            float duration = Mathf.Max(0.0f, transition.BlendDurationSeconds);
            if (transition.FixedDuration == false &&
                fromState != null &&
                TryResolvePreviewMotion(fromState, _previewMotionSamplesA, out float referenceLength, out _, out _))
            {
                duration *= Mathf.Max(0.01f, referenceLength);
            }

            return duration;
        }

        private bool TryResolvePreviewStateUsingRuntime(
            out FusionAnimatorStateDefinition state,
            out FusionAnimatorStateDefinition blendFromState,
            out float blendWeight)
        {
            state = null;
            blendFromState = null;
            blendWeight = 0.0f;

            if (_graph == null || _graph.States == null || _graph.States.Count == 0)
            {
                return false;
            }

            if (TryResolvePreviewSimulationContext(out string layerId, out string scopePath, out string defaultStateId) == false)
            {
                return false;
            }

            EnsurePreviewRuntimeEvaluator(layerId, scopePath, defaultStateId);
            if (_previewRuntimeEvaluator == null)
            {
                return false;
            }

            SyncPreviewRuntimeParameters();
            _previewRuntimeEvaluator.Step(_previewStepDeltaTime, _previewRuntimeParameters, null, true);
            _previewRuntimeParameters.ExpireUnconsumedTriggers();
            SyncPreviewEntriesFromRuntimeParameters();

            FusionAnimatorStateDefinition currentState = FindStateById(_previewRuntimeEvaluator.CurrentStateId);
            if (currentState == null)
            {
                return false;
            }

            state = currentState;
            _previewResolvedByRuntime = true;
            _previewActiveStateId = currentState.Id;
            _previewStateElapsed = Mathf.Max(0.0f, _previewRuntimeEvaluator.CurrentStateElapsed);
            _previewTime = Mathf.Max(0.0f, _previewRuntimeEvaluator.CurrentStateTime);

            if (_previewRuntimeEvaluator.IsBlending)
            {
                blendFromState = FindStateById(_previewRuntimeEvaluator.BlendFromStateId);
                blendWeight = _previewRuntimeEvaluator.BlendAlpha;
                _previewBlendFromStateId = _previewRuntimeEvaluator.BlendFromStateId;
                _previewBlendToStateId = _previewRuntimeEvaluator.BlendToStateId;
                _previewBlendDuration = _previewRuntimeEvaluator.BlendDurationSeconds;
                _previewBlendElapsed = _previewRuntimeEvaluator.BlendElapsedSeconds;
                _previewBlendFromTime = Mathf.Max(0.0f, _previewRuntimeEvaluator.BlendFromStateTime);
            }
            else
            {
                ClearPreviewBlend();
            }

            _previewActiveMarkerStateIds.Clear();
            _previewBlendMarkerStateIds.Clear();
            _previewActiveMarkerLayerIds.Clear();
            if (string.IsNullOrWhiteSpace(state.Id) == false)
            {
                _previewActiveMarkerStateIds.Add(state.Id);
            }

            if (string.IsNullOrWhiteSpace(_previewRuntimeLayerId) == false)
            {
                _previewActiveMarkerLayerIds.Add(_previewRuntimeLayerId);
            }

            if (_previewRuntimeEvaluator.IsBlending)
            {
                if (string.IsNullOrWhiteSpace(_previewRuntimeEvaluator.BlendFromStateId) == false)
                {
                    _previewBlendMarkerStateIds.Add(_previewRuntimeEvaluator.BlendFromStateId);
                }

                if (string.IsNullOrWhiteSpace(_previewRuntimeEvaluator.BlendToStateId) == false)
                {
                    _previewBlendMarkerStateIds.Add(_previewRuntimeEvaluator.BlendToStateId);
                }
            }

            _graphView?.SetPreviewRuntimeMarkers(
                _previewActiveMarkerStateIds,
                _previewBlendMarkerStateIds,
                _previewActiveMarkerLayerIds);

            return true;
        }

        private bool TryResolvePreviewSimulationContext(
            out string layerId,
            out string scopePath,
            out string defaultStateId)
        {
            layerId = null;
            scopePath = string.Empty;
            defaultStateId = null;

            if (_graph == null || _graph.States == null || _graph.States.Count == 0)
            {
                return false;
            }

            if (_graph.Layers != null && _selectedLayerIndex >= 0 && _selectedLayerIndex < _graph.Layers.Count)
            {
                layerId = _graph.Layers[_selectedLayerIndex]?.Id;
                scopePath = NormalizeScopePath(_selectedLayerScopePath);
            }

            if (string.IsNullOrWhiteSpace(layerId) == false)
            {
                defaultStateId = ResolveRuntimeContextDefaultStateId(layerId, scopePath);
                return true;
            }

            if (string.IsNullOrWhiteSpace(_activeLayerId) == false)
            {
                layerId = _activeLayerId;
                scopePath = NormalizeScopePath(_activeScopePath);
                defaultStateId = ResolveRuntimeContextDefaultStateId(layerId, scopePath);
                return true;
            }

            FusionAnimatorStateDefinition entryState = FindStateById(_graph.EntryStateId);
            if (entryState != null)
            {
                layerId = entryState.LayerId;
                scopePath = NormalizeScopePath(GetStateScopePathFromName(entryState.Name));
                defaultStateId = ResolveRuntimeContextDefaultStateId(layerId, scopePath);
                return true;
            }

            if (_graph.States.Count > 0)
            {
                FusionAnimatorStateDefinition firstState = _graph.States[0];
                if (firstState != null)
                {
                    layerId = firstState.LayerId;
                    scopePath = NormalizeScopePath(GetStateScopePathFromName(firstState.Name));
                    defaultStateId = ResolveRuntimeContextDefaultStateId(layerId, scopePath);
                    return true;
                }
            }

            return false;
        }

        private string ResolveRuntimeContextDefaultStateId(string layerId, string scopePath)
        {
            if (string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            if (HasEntryTransitionsInScope(layerId, normalizedScope))
            {
                // Let runtime evaluator resolve conditional entry links from live parameter values.
                return null;
            }

            FusionAnimatorStateDefinition fallback = ResolveScopeDefaultPreviewState(layerId, normalizedScope);
            return fallback != null ? fallback.Id : null;
        }

        private bool HasEntryTransitionsInScope(string layerId, string scopePath)
        {
            if (_graph == null || _graph.Transitions == null || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            for (int i = 0; i < _graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null || transition.Mute ||
                    string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                FusionAnimatorStateDefinition destinationState = FindStateById(transition.ToStateId);
                if (destinationState == null ||
                    string.Equals(destinationState.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string destinationScope = NormalizeScopePath(GetStateScopePathFromName(destinationState.Name));
                if (string.Equals(destinationScope, normalizedScope, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void EnsurePreviewRuntimeEvaluator(string layerId, string scopePath, string defaultStateId)
        {
            string normalizedLayer = layerId ?? string.Empty;
            string normalizedScope = NormalizeScopePath(scopePath);
            string normalizedDefault = defaultStateId ?? string.Empty;

            bool needsRebuild = _previewRuntimeEvaluator == null ||
                                ReferenceEquals(_previewRuntimeGraph, _graph) == false ||
                                string.Equals(_previewRuntimeLayerId, normalizedLayer, StringComparison.Ordinal) == false ||
                                string.Equals(_previewRuntimeScopePath, normalizedScope, StringComparison.OrdinalIgnoreCase) == false ||
                                string.Equals(_previewRuntimeDefaultStateId, normalizedDefault, StringComparison.Ordinal) == false;

            if (needsRebuild == false)
            {
                return;
            }

            Func<FusionAnimatorStateDefinition, bool> filter = state =>
            {
                if (state == null || string.Equals(state.LayerId, normalizedLayer, StringComparison.Ordinal) == false)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(normalizedScope))
                {
                    return true;
                }

                string stateScope = NormalizeScopePath(GetStateScopePathFromName(state.Name));
                return string.Equals(stateScope, normalizedScope, StringComparison.OrdinalIgnoreCase) ||
                       stateScope.StartsWith(normalizedScope + "/", StringComparison.OrdinalIgnoreCase);
            };

            _previewRuntimeEvaluator = new FusionAnimatorRuntimeEvaluator(
                _graph,
                filter,
                string.IsNullOrWhiteSpace(normalizedDefault) ? null : normalizedDefault);
            _previewRuntimeGraph = _graph;
            _previewRuntimeLayerId = normalizedLayer;
            _previewRuntimeScopePath = normalizedScope;
            _previewRuntimeDefaultStateId = normalizedDefault;
        }

        private void SyncPreviewRuntimeParameters()
        {
            if (_graph == null || _graph.Parameters == null)
            {
                _previewRuntimeParametersAsset = null;
                _previewRuntimeParameters.Clear();
                return;
            }

            if (ReferenceEquals(_previewRuntimeParametersAsset, _graph) == false)
            {
                _previewRuntimeParameters.SetDefaults(_graph);
                _previewRuntimeParametersAsset = _graph;
            }

            for (int i = 0; i < _graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
                {
                    continue;
                }

                PreviewParameterEntry entry = FindPreviewEntry(parameter.Id);
                if (entry == null)
                {
                    continue;
                }

                switch (parameter.Type)
                {
                    case FusionAnimatorParameterType.Bool:
                    case FusionAnimatorParameterType.Trigger:
                        _previewRuntimeParameters.SetBool(parameter.Id, entry.BoolValue);
                        break;
                    case FusionAnimatorParameterType.Int:
                        _previewRuntimeParameters.SetInt(parameter.Id, entry.IntValue);
                        break;
                    case FusionAnimatorParameterType.Float:
                        _previewRuntimeParameters.SetFloat(parameter.Id, entry.FloatValue);
                        break;
                    case FusionAnimatorParameterType.Vector2:
                        _previewRuntimeParameters.SetVector2(parameter.Id, entry.Vector2Value);
                        break;
                }
            }
        }

        private void SyncPreviewEntriesFromRuntimeParameters()
        {
            if (_graph == null || _graph.Parameters == null)
            {
                return;
            }

            bool changed = false;
            for (int i = 0; i < _graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
                {
                    continue;
                }

                PreviewParameterEntry entry = FindPreviewEntry(parameter.Id);
                if (entry == null)
                {
                    continue;
                }

                switch (parameter.Type)
                {
                    case FusionAnimatorParameterType.Bool:
                    case FusionAnimatorParameterType.Trigger:
                    {
                        if (_previewRuntimeParameters.TryGetBool(parameter.Id, out bool boolValue) &&
                            entry.BoolValue != boolValue)
                        {
                            entry.BoolValue = boolValue;
                            changed = true;
                        }
                        break;
                    }
                    case FusionAnimatorParameterType.Int:
                    {
                        if (_previewRuntimeParameters.TryGetInt(parameter.Id, out int intValue) &&
                            entry.IntValue != intValue)
                        {
                            entry.IntValue = intValue;
                            changed = true;
                        }
                        break;
                    }
                    case FusionAnimatorParameterType.Float:
                    {
                        if (_previewRuntimeParameters.TryGetFloat(parameter.Id, out float floatValue) &&
                            Mathf.Abs(entry.FloatValue - floatValue) > 0.0001f)
                        {
                            entry.FloatValue = floatValue;
                            changed = true;
                        }
                        break;
                    }
                    case FusionAnimatorParameterType.Vector2:
                    {
                        if (_previewRuntimeParameters.TryGetVector2(parameter.Id, out Vector2 vector2Value) &&
                            (entry.Vector2Value - vector2Value).sqrMagnitude > 0.00000001f)
                        {
                            entry.Vector2Value = vector2Value;
                            changed = true;
                        }
                        break;
                    }
                }
            }

            if (changed)
            {
                _inspector?.MarkDirtyRepaint();
            }
        }

        private bool TryResolvePreviewMotion(
            FusionAnimatorStateDefinition state,
            List<PreviewMotionSample> samples,
            out float referenceLength,
            out float speed,
            out string label)
        {
            referenceLength = 0.0f;
            speed = 1.0f;
            label = string.Empty;
            if (samples == null)
            {
                return false;
            }

            samples.Clear();
            if (state == null)
            {
                return false;
            }

            if (state.MotionType == FusionAnimatorMotionType.BlendTree && state.BlendTree != null)
            {
                ResolveBlendTreeSamples(state.BlendTree, samples);
                if (samples.Count > 0)
                {
                    float accumulatedLength = 0.0f;
                    float accumulatedSpeed = 0.0f;
                    int contributingCount = 0;
                    for (int i = 0; i < samples.Count; ++i)
                    {
                        PreviewMotionSample sample = samples[i];
                        if (sample.Clip == null)
                        {
                            continue;
                        }

                        accumulatedLength += Mathf.Max(0.01f, sample.Clip.length);
                        accumulatedSpeed += Mathf.Max(0.01f, sample.TimeScale);
                        ++contributingCount;
                    }

                    if (contributingCount <= 0)
                    {
                        return false;
                    }

                    float invCount = 1.0f / contributingCount;
                    referenceLength = Mathf.Max(0.01f, accumulatedLength * invCount);
                    speed = Mathf.Max(0.01f, accumulatedSpeed * invCount);
                    label = samples.Count == 1
                        ? samples[0].Clip.name
                        : string.Format("BlendTree[{0}]", samples.Count);
                    return true;
                }
            }

            if (state.Clips == null)
            {
                return false;
            }

            for (int i = 0; i < state.Clips.Count; ++i)
            {
                FusionAnimatorClipSlot slot = state.Clips[i];
                AnimationClip resolvedClip = FusionAnimatorClipBindingUtility.ResolveClip(_graph, slot, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                if (slot == null || resolvedClip == null)
                {
                    continue;
                }

                samples.Add(new PreviewMotionSample
                {
                    Clip = resolvedClip,
                    Weight = 1.0f,
                    TimeScale = Mathf.Max(0.01f, FusionAnimatorClipBindingUtility.ResolveSpeed(_graph, slot, EvaluateCondition, ResolvePreviewBindingClipIndexParameter)),
                    Loop = FusionAnimatorClipBindingUtility.ResolveLoop(_graph, slot, EvaluateCondition, ResolvePreviewBindingClipIndexParameter),
                    ExplicitNormalizedTime = -1.0f,
                });
                referenceLength = Mathf.Max(0.01f, resolvedClip.length);
                speed = Mathf.Max(0.01f, FusionAnimatorClipBindingUtility.ResolveSpeed(_graph, slot, EvaluateCondition, ResolvePreviewBindingClipIndexParameter));
                label = resolvedClip.name;
                return true;
            }

            return false;
        }

        private void ResolveBlendTreeSamples(FusionAnimatorBlendTreeDefinition blendTree, List<PreviewMotionSample> samples)
        {
            if (samples == null)
            {
                return;
            }

            samples.Clear();
            if (blendTree == null || blendTree.Children == null || blendTree.Children.Count == 0)
            {
                return;
            }

            List<FusionAnimatorBlendTreeChild> validChildren = new List<FusionAnimatorBlendTreeChild>();
            List<AnimationClip> validChildClips = new List<AnimationClip>();
            for (int i = 0; i < blendTree.Children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = blendTree.Children[i];
                AnimationClip childClip = FusionAnimatorClipBindingUtility.ResolveClip(_graph, child, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                if (child != null && childClip != null)
                {
                    validChildren.Add(child);
                    validChildClips.Add(childClip);
                }
            }

            if (validChildren.Count == 0)
            {
                return;
            }

            float[] weights = new float[validChildren.Count];
            float explicitPoseTime01 = -1.0f;
            switch (blendTree.Type)
            {
                case FusionAnimatorBlendTreeType.OneD:
                    ResolveOneDWeights(blendTree, validChildren, weights);
                    break;
                case FusionAnimatorBlendTreeType.TwoDSimpleDirectional:
                    ResolveTwoDSimpleDirectionalWeights(blendTree, validChildren, weights);
                    break;
                case FusionAnimatorBlendTreeType.TwoDFreeformDirectional:
                    ResolveTwoDFreeformDirectionalWeights(blendTree, validChildren, weights);
                    break;
                case FusionAnimatorBlendTreeType.TwoDFreeformCartesian:
                    ResolveTwoDFreeformCartesianWeights(blendTree, validChildren, weights);
                    break;
                case FusionAnimatorBlendTreeType.Direct:
                    ResolveDirectWeights(blendTree, validChildren, weights);
                    break;
                case FusionAnimatorBlendTreeType.DirectionalPoseTime2D:
                    ResolveDirectionalPoseTimeWeights(blendTree, validChildren, weights);
                    explicitPoseTime01 = ResolvePreviewDirectionalPoseTimeNormalized(blendTree, validChildren);
                    break;
                default:
                    weights[0] = 1.0f;
                    break;
            }

            float total = 0.0f;
            for (int i = 0; i < weights.Length; ++i)
            {
                weights[i] = Mathf.Max(0.0f, weights[i]);
                total += weights[i];
            }

            if (total <= 0.000001f)
            {
                weights[0] = 1.0f;
                total = 1.0f;
            }

            float invTotal = 1.0f / total;
            for (int i = 0; i < validChildren.Count; ++i)
            {
                float normalizedWeight = weights[i] * invTotal;
                AnimationClip childClip = validChildClips[i];
                samples.Add(new PreviewMotionSample
                {
                    Clip = childClip,
                    Weight = normalizedWeight,
                    TimeScale = Mathf.Max(0.01f, validChildren[i].TimeScale),
                    Loop = childClip.isLooping,
                    ExplicitNormalizedTime = explicitPoseTime01,
                });
            }
        }

        private void ResolveOneDWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
        {
            float x = GetPreviewFloatValue(blendTree.ParameterXId);
            List<int> order = new List<int>(children.Count);
            for (int i = 0; i < children.Count; ++i)
            {
                order.Add(i);
            }

            order.Sort((a, b) =>
            {
                float ax = Mathf.Abs(children[a].Position.x) > 0.0001f || Mathf.Abs(children[a].Position.y) > 0.0001f
                    ? children[a].Position.x
                    : children[a].Threshold;
                float bx = Mathf.Abs(children[b].Position.x) > 0.0001f || Mathf.Abs(children[b].Position.y) > 0.0001f
                    ? children[b].Position.x
                    : children[b].Threshold;
                return ax.CompareTo(bx);
            });

            int firstIndex = order[0];
            int lastIndex = order[order.Count - 1];
            float firstX = Mathf.Abs(children[firstIndex].Position.x) > 0.0001f || Mathf.Abs(children[firstIndex].Position.y) > 0.0001f
                ? children[firstIndex].Position.x
                : children[firstIndex].Threshold;
            float lastX = Mathf.Abs(children[lastIndex].Position.x) > 0.0001f || Mathf.Abs(children[lastIndex].Position.y) > 0.0001f
                ? children[lastIndex].Position.x
                : children[lastIndex].Threshold;

            if (x <= firstX)
            {
                weights[firstIndex] = 1.0f;
                return;
            }

            if (x >= lastX)
            {
                weights[lastIndex] = 1.0f;
                return;
            }

            for (int i = 0; i < order.Count - 1; ++i)
            {
                int leftIndex = order[i];
                int rightIndex = order[i + 1];
                float leftX = Mathf.Abs(children[leftIndex].Position.x) > 0.0001f || Mathf.Abs(children[leftIndex].Position.y) > 0.0001f
                    ? children[leftIndex].Position.x
                    : children[leftIndex].Threshold;
                float rightX = Mathf.Abs(children[rightIndex].Position.x) > 0.0001f || Mathf.Abs(children[rightIndex].Position.y) > 0.0001f
                    ? children[rightIndex].Position.x
                    : children[rightIndex].Threshold;
                if (x < leftX || x > rightX)
                {
                    continue;
                }

                float span = Mathf.Max(0.0001f, rightX - leftX);
                float t = Mathf.Clamp01((x - leftX) / span);
                weights[leftIndex] = 1.0f - t;
                weights[rightIndex] = t;
                return;
            }

            weights[firstIndex] = 1.0f;
        }

        private void ResolveTwoDSimpleDirectionalWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
        {
            Vector2 input = GetPreviewTwoDBlendTreeInputValue(blendTree);
            const float epsilon = 0.0001f;
            float inputMagnitude = input.magnitude;
            if (inputMagnitude <= epsilon)
            {
                if (TryFindCenterChild(children, out int centerAtRestIndex))
                {
                    weights[centerAtRestIndex] = 1.0f;
                }
                else
                {
                    float fallbackWeight = 1.0f / Mathf.Max(1, children.Count);
                    for (int i = 0; i < children.Count; ++i)
                    {
                        weights[i] = fallbackWeight;
                    }
                }

                return;
            }

            Vector2 inputDirection = input / inputMagnitude;
            if (BuildDirectionalChildren(children, out List<int> directionalIndices, out List<float> directionalAnglesDegrees, out int centerIndex) == false)
            {
                if (centerIndex >= 0)
                {
                    weights[centerIndex] = 1.0f;
                }
                else
                {
                    weights[0] = 1.0f;
                }

                return;
            }

            ResolveDirectionalAngularWeights(inputDirection, directionalIndices, directionalAnglesDegrees, weights);

            if (centerIndex >= 0)
            {
                float directionalFactor = Mathf.Clamp01(inputMagnitude);
                for (int i = 0; i < directionalIndices.Count; ++i)
                {
                    int childIndex = directionalIndices[i];
                    weights[childIndex] *= directionalFactor;
                }

                weights[centerIndex] += 1.0f - directionalFactor;
            }
        }

        private void ResolveTwoDFreeformDirectionalWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
        {
            Vector2 input = GetPreviewTwoDBlendTreeInputValue(blendTree);
            const float epsilon = 0.0001f;
            float inputMagnitude = input.magnitude;
            if (inputMagnitude <= epsilon)
            {
                if (TryFindCenterChild(children, out int centerAtRestIndex))
                {
                    weights[centerAtRestIndex] = 1.0f;
                }
                else
                {
                    weights[0] = 1.0f;
                }

                return;
            }

            Vector2 inputDirection = input / inputMagnitude;
            if (BuildDirectionalChildren(children, out List<int> directionalIndices, out List<float> directionalAnglesDegrees, out int centerIndex) == false)
            {
                if (centerIndex >= 0)
                {
                    weights[centerIndex] = 1.0f;
                }
                else
                {
                    weights[0] = 1.0f;
                }

                return;
            }

            List<List<int>> lanes = BuildDirectionalLanes(children, directionalIndices, directionalAnglesDegrees);
            if (lanes.Count == 0)
            {
                if (centerIndex >= 0)
                {
                    weights[centerIndex] = 1.0f;
                }
                else
                {
                    weights[0] = 1.0f;
                }

                return;
            }

            List<float> laneAngles = new List<float>(lanes.Count);
            for (int lane = 0; lane < lanes.Count; ++lane)
            {
                List<int> laneChildren = lanes[lane];
                if (laneChildren == null || laneChildren.Count == 0)
                {
                    laneAngles.Add(0.0f);
                    continue;
                }

                Vector2 direction = children[laneChildren[0]].Position.normalized;
                float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
                laneAngles.Add(NormalizeAngle360(angle));
            }

            if (lanes.Count == 1)
            {
                AccumulateLaneRadialWeights(children, lanes[0], inputMagnitude, 1.0f, weights);
            }
            else
            {
                ResolveDirectionalNeighborLanes(inputDirection, laneAngles, out int leftLane, out int rightLane, out float laneT);
                AccumulateLaneRadialWeights(children, lanes[leftLane], inputMagnitude, 1.0f - laneT, weights);
                AccumulateLaneRadialWeights(children, lanes[rightLane], inputMagnitude, laneT, weights);
            }

            if (centerIndex >= 0)
            {
                float directionalFactor = Mathf.Clamp01(inputMagnitude);
                float accumulatedDirectional = 0.0f;
                for (int i = 0; i < directionalIndices.Count; ++i)
                {
                    int childIndex = directionalIndices[i];
                    weights[childIndex] *= directionalFactor;
                    accumulatedDirectional += weights[childIndex];
                }

                if (accumulatedDirectional > epsilon)
                {
                    float inv = 1.0f / accumulatedDirectional;
                    for (int i = 0; i < directionalIndices.Count; ++i)
                    {
                        int childIndex = directionalIndices[i];
                        weights[childIndex] *= inv * directionalFactor;
                    }
                }

                weights[centerIndex] += 1.0f - directionalFactor;
            }
        }

        private void ResolveTwoDFreeformCartesianWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
        {
            Vector2 input = GetPreviewTwoDBlendTreeInputValue(blendTree);
            const float epsilon = 0.0001f;

            int exactIndex = -1;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < children.Count; ++i)
            {
                float distance = Vector2.SqrMagnitude(input - children[i].Position);
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    exactIndex = i;
                }
            }

            if (bestDistance <= epsilon * epsilon && exactIndex >= 0)
            {
                weights[exactIndex] = 1.0f;
                return;
            }

            const int nearestCount = 4;
            List<int> nearestIndices = new List<int>(nearestCount);
            List<float> nearestDistances = new List<float>(nearestCount);
            for (int i = 0; i < children.Count; ++i)
            {
                float distanceSquared = Vector2.SqrMagnitude(input - children[i].Position);
                float distance = Mathf.Sqrt(distanceSquared);

                if (nearestIndices.Count < nearestCount)
                {
                    nearestIndices.Add(i);
                    nearestDistances.Add(distance);
                    continue;
                }

                int farthestSlot = 0;
                float farthestDistance = nearestDistances[0];
                for (int slot = 1; slot < nearestDistances.Count; ++slot)
                {
                    if (nearestDistances[slot] > farthestDistance)
                    {
                        farthestDistance = nearestDistances[slot];
                        farthestSlot = slot;
                    }
                }

                if (distance < farthestDistance)
                {
                    nearestIndices[farthestSlot] = i;
                    nearestDistances[farthestSlot] = distance;
                }
            }

            float total = 0.0f;
            for (int i = 0; i < nearestIndices.Count; ++i)
            {
                int childIndex = nearestIndices[i];
                float d = nearestDistances[i];
                float w = 1.0f / Mathf.Max(epsilon * epsilon, d * d);
                weights[childIndex] = w;
                total += w;
            }

            if (total <= epsilon)
            {
                weights[exactIndex >= 0 ? exactIndex : 0] = 1.0f;
                return;
            }

            float invTotal = 1.0f / total;
            for (int i = 0; i < nearestIndices.Count; ++i)
            {
                int childIndex = nearestIndices[i];
                weights[childIndex] *= invTotal;
            }
        }

        private void ResolveDirectionalPoseTimeWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
        {
            Vector2 input = GetPreviewTwoDBlendTreeInputValue(blendTree);
            const float epsilon = 0.0001f;
            float inputMagnitude = input.magnitude;
            if (inputMagnitude <= epsilon)
            {
                if (TryFindCenterChild(children, out int centerAtRestIndex))
                {
                    weights[centerAtRestIndex] = 1.0f;
                }
                else
                {
                    weights[0] = 1.0f;
                }

                return;
            }

            Vector2 inputDirection = input / inputMagnitude;
            if (BuildDirectionalChildren(children, out List<int> directionalIndices, out List<float> directionalAnglesDegrees, out int centerIndex) == false)
            {
                if (centerIndex >= 0)
                {
                    weights[centerIndex] = 1.0f;
                }
                else
                {
                    weights[0] = 1.0f;
                }

                return;
            }

            ResolveDirectionalAngularWeights(inputDirection, directionalIndices, directionalAnglesDegrees, weights);
        }

        private float ResolvePreviewDirectionalPoseTimeNormalized(
            FusionAnimatorBlendTreeDefinition blendTree,
            List<FusionAnimatorBlendTreeChild> children)
        {
            if (blendTree == null)
            {
                return 0.0f;
            }

            float rawPoseTime;
            if (string.IsNullOrWhiteSpace(blendTree.PoseTimeParameterId))
            {
                rawPoseTime = GetPreviewTwoDBlendTreeInputValue(blendTree).magnitude;
                float defaultRange = ResolveDirectionalPoseTimeInputRange(children);
                rawPoseTime /= defaultRange;
            }
            else
            {
                rawPoseTime = GetPreviewFloatValue(blendTree.PoseTimeParameterId);
            }

            return EvaluatePoseTime01(rawPoseTime, blendTree.InputOffsetX, blendTree.InputPowerX);
        }

        private static bool TryFindCenterChild(List<FusionAnimatorBlendTreeChild> children, out int centerIndex)
        {
            centerIndex = -1;
            if (children == null)
            {
                return false;
            }

            const float epsilon = 0.0001f;
            for (int i = 0; i < children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = children[i];
                if (child == null)
                {
                    continue;
                }

                if (child.Position.sqrMagnitude <= epsilon * epsilon)
                {
                    centerIndex = i;
                    return true;
                }
            }

            return false;
        }

        private static bool BuildDirectionalChildren(
            List<FusionAnimatorBlendTreeChild> children,
            out List<int> directionalIndices,
            out List<float> directionalAnglesDegrees,
            out int centerIndex)
        {
            directionalIndices = new List<int>();
            directionalAnglesDegrees = new List<float>();
            centerIndex = -1;
            if (children == null || children.Count == 0)
            {
                return false;
            }

            const float epsilon = 0.0001f;
            for (int i = 0; i < children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = children[i];
                if (child == null)
                {
                    continue;
                }

                if (child.Position.sqrMagnitude <= epsilon * epsilon)
                {
                    if (centerIndex < 0)
                    {
                        centerIndex = i;
                    }
                    continue;
                }

                directionalIndices.Add(i);
                directionalAnglesDegrees.Add(NormalizeAngle360(Mathf.Atan2(child.Position.y, child.Position.x) * Mathf.Rad2Deg));
            }

            if (directionalIndices.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < directionalIndices.Count - 1; ++i)
            {
                for (int j = i + 1; j < directionalIndices.Count; ++j)
                {
                    if (directionalAnglesDegrees[j] < directionalAnglesDegrees[i])
                    {
                        float tmpAngle = directionalAnglesDegrees[i];
                        directionalAnglesDegrees[i] = directionalAnglesDegrees[j];
                        directionalAnglesDegrees[j] = tmpAngle;

                        int tmpIndex = directionalIndices[i];
                        directionalIndices[i] = directionalIndices[j];
                        directionalIndices[j] = tmpIndex;
                    }
                }
            }

            return true;
        }

        private static List<List<int>> BuildDirectionalLanes(
            List<FusionAnimatorBlendTreeChild> children,
            List<int> directionalIndices,
            List<float> directionalAnglesDegrees)
        {
            var lanes = new List<List<int>>();
            var laneAngles = new List<float>();
            const float laneToleranceDegrees = 8.0f;

            for (int i = 0; i < directionalIndices.Count; ++i)
            {
                int childIndex = directionalIndices[i];
                float childAngle = directionalAnglesDegrees[i];
                int matchedLane = -1;
                for (int lane = 0; lane < laneAngles.Count; ++lane)
                {
                    if (Mathf.Abs(Mathf.DeltaAngle(laneAngles[lane], childAngle)) <= laneToleranceDegrees)
                    {
                        matchedLane = lane;
                        break;
                    }
                }

                if (matchedLane < 0)
                {
                    matchedLane = lanes.Count;
                    lanes.Add(new List<int>());
                    laneAngles.Add(childAngle);
                }

                lanes[matchedLane].Add(childIndex);
            }

            for (int lane = 0; lane < lanes.Count; ++lane)
            {
                List<int> laneChildren = lanes[lane];
                for (int i = 0; i < laneChildren.Count - 1; ++i)
                {
                    for (int j = i + 1; j < laneChildren.Count; ++j)
                    {
                        float radiusA = children[laneChildren[i]].Position.magnitude;
                        float radiusB = children[laneChildren[j]].Position.magnitude;
                        if (radiusB < radiusA)
                        {
                            int tmp = laneChildren[i];
                            laneChildren[i] = laneChildren[j];
                            laneChildren[j] = tmp;
                        }
                    }
                }
            }

            for (int i = 0; i < lanes.Count - 1; ++i)
            {
                for (int j = i + 1; j < lanes.Count; ++j)
                {
                    float ai = NormalizeAngle360(Mathf.Atan2(children[lanes[i][0]].Position.y, children[lanes[i][0]].Position.x) * Mathf.Rad2Deg);
                    float aj = NormalizeAngle360(Mathf.Atan2(children[lanes[j][0]].Position.y, children[lanes[j][0]].Position.x) * Mathf.Rad2Deg);
                    if (aj < ai)
                    {
                        List<int> tmpLane = lanes[i];
                        lanes[i] = lanes[j];
                        lanes[j] = tmpLane;
                    }
                }
            }

            return lanes;
        }

        private static void ResolveDirectionalAngularWeights(
            Vector2 inputDirection,
            List<int> directionalIndices,
            List<float> directionalAnglesDegrees,
            float[] weights)
        {
            if (directionalIndices == null || directionalIndices.Count == 0)
            {
                return;
            }

            if (directionalIndices.Count == 1)
            {
                weights[directionalIndices[0]] = 1.0f;
                return;
            }

            ResolveDirectionalNeighborLanes(inputDirection, directionalAnglesDegrees, out int leftSlot, out int rightSlot, out float t);
            weights[directionalIndices[leftSlot]] += 1.0f - t;
            weights[directionalIndices[rightSlot]] += t;
        }

        private static void ResolveDirectionalNeighborLanes(
            Vector2 inputDirection,
            List<float> sortedAnglesDegrees,
            out int leftSlot,
            out int rightSlot,
            out float t)
        {
            float inputAngle = NormalizeAngle360(Mathf.Atan2(inputDirection.y, inputDirection.x) * Mathf.Rad2Deg);
            rightSlot = 0;
            while (rightSlot < sortedAnglesDegrees.Count && sortedAnglesDegrees[rightSlot] < inputAngle)
            {
                ++rightSlot;
            }

            if (rightSlot >= sortedAnglesDegrees.Count)
            {
                rightSlot = 0;
            }

            leftSlot = (rightSlot - 1 + sortedAnglesDegrees.Count) % sortedAnglesDegrees.Count;
            float leftAngle = sortedAnglesDegrees[leftSlot];
            float rightAngle = sortedAnglesDegrees[rightSlot];
            float angleForLerp = inputAngle;
            if (rightSlot == 0)
            {
                rightAngle += 360.0f;
            }

            if (angleForLerp < leftAngle)
            {
                angleForLerp += 360.0f;
            }

            float span = Mathf.Max(0.0001f, rightAngle - leftAngle);
            t = Mathf.Clamp01((angleForLerp - leftAngle) / span);
        }

        private static void AccumulateLaneRadialWeights(
            List<FusionAnimatorBlendTreeChild> children,
            List<int> laneChildren,
            float inputMagnitude,
            float laneWeight,
            float[] weights)
        {
            if (laneChildren == null || laneChildren.Count == 0 || laneWeight <= 0.000001f)
            {
                return;
            }

            if (laneChildren.Count == 1)
            {
                weights[laneChildren[0]] += laneWeight;
                return;
            }

            int firstChild = laneChildren[0];
            int lastChild = laneChildren[laneChildren.Count - 1];
            float firstRadius = children[firstChild].Position.magnitude;
            float lastRadius = children[lastChild].Position.magnitude;

            if (inputMagnitude <= firstRadius + 0.0001f)
            {
                weights[firstChild] += laneWeight;
                return;
            }

            if (inputMagnitude >= lastRadius - 0.0001f)
            {
                weights[lastChild] += laneWeight;
                return;
            }

            for (int i = 0; i < laneChildren.Count - 1; ++i)
            {
                int leftChild = laneChildren[i];
                int rightChild = laneChildren[i + 1];
                float leftRadius = children[leftChild].Position.magnitude;
                float rightRadius = children[rightChild].Position.magnitude;
                if (inputMagnitude < leftRadius || inputMagnitude > rightRadius)
                {
                    continue;
                }

                float span = Mathf.Max(0.0001f, rightRadius - leftRadius);
                float radialT = Mathf.Clamp01((inputMagnitude - leftRadius) / span);
                weights[leftChild] += laneWeight * (1.0f - radialT);
                weights[rightChild] += laneWeight * radialT;
                return;
            }

            weights[firstChild] += laneWeight;
        }

        private static float NormalizeAngle360(float angle)
        {
            angle %= 360.0f;
            if (angle < 0.0f)
            {
                angle += 360.0f;
            }

            return angle;
        }

        private static float EvaluatePoseTime01(float rawValue, float offset, float power)
        {
            float normalized = Mathf.Abs(rawValue);
            normalized += offset;
            normalized = Mathf.Max(0.0f, normalized);
            float safePower = power <= 0.0001f ? 1.0f : power;
            normalized = Mathf.Pow(normalized, safePower);
            return Mathf.Clamp01(normalized);
        }

        private static float ResolveDirectionalPoseTimeInputRange(List<FusionAnimatorBlendTreeChild> children)
        {
            if (children == null || children.Count == 0)
            {
                return 1.0f;
            }

            float maxThresholdMagnitude = 0.0f;
            float maxPositionMagnitude = 0.0f;
            for (int i = 0; i < children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = children[i];
                if (child == null)
                {
                    continue;
                }

                maxThresholdMagnitude = Mathf.Max(maxThresholdMagnitude, Mathf.Abs(child.Threshold));
                maxPositionMagnitude = Mathf.Max(maxPositionMagnitude, child.Position.magnitude);
            }

            if (maxThresholdMagnitude > 0.0001f)
            {
                return maxThresholdMagnitude;
            }

            if (maxPositionMagnitude > 0.0001f)
            {
                return maxPositionMagnitude;
            }

            return 1.0f;
        }

        private void ResolveDirectWeights(FusionAnimatorBlendTreeDefinition blendTree, List<FusionAnimatorBlendTreeChild> children, float[] weights)
        {
            float total = 0.0f;
            for (int i = 0; i < children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = children[i];
                string parameterId = string.IsNullOrWhiteSpace(child.DirectParameterId) ? blendTree.DirectBlendParameterId : child.DirectParameterId;
                float value = Mathf.Max(0.0f, GetPreviewFloatValue(parameterId));
                weights[i] = value;
                total += value;
            }

            if (total <= 0.000001f)
            {
                weights[0] = 1.0f;
            }
        }

        private static void BuildMotionRenderData(
            List<PreviewMotionSample> samples,
            float motionTime,
            List<AnimationClip> clips,
            List<float> sampleTimes,
            List<float> weights,
            PreviewMotionLoopMode loopMode = PreviewMotionLoopMode.PerSample)
        {
            clips.Clear();
            sampleTimes.Clear();
            weights.Clear();
            if (samples == null || samples.Count == 0)
            {
                return;
            }

            for (int i = 0; i < samples.Count; ++i)
            {
                PreviewMotionSample sample = samples[i];
                if (sample.Clip == null || sample.Weight <= 0.000001f)
                {
                    continue;
                }

                float clipLength = Mathf.Max(0.01f, sample.Clip.length);
                float clipTime = motionTime * Mathf.Max(0.01f, sample.TimeScale);
                bool shouldLoop;
                switch (loopMode)
                {
                    case PreviewMotionLoopMode.ForceLoop:
                        shouldLoop = true;
                        break;
                    case PreviewMotionLoopMode.ForceClamp:
                        shouldLoop = false;
                        break;
                    default:
                        shouldLoop = sample.Loop;
                        break;
                }

                if (sample.ExplicitNormalizedTime >= 0.0f)
                {
                    clipTime = Mathf.Clamp01(sample.ExplicitNormalizedTime) * clipLength;
                }
                else
                {
                    clipTime = shouldLoop ? Mathf.Repeat(clipTime, clipLength) : Mathf.Clamp(clipTime, 0.0f, clipLength);
                }
                clips.Add(sample.Clip);
                sampleTimes.Add(clipTime);
                weights.Add(sample.Weight);
            }
        }

        private bool TransitionConditionsPass(FusionAnimatorTransitionDefinition transition)
        {
            if (transition == null || transition.Conditions == null || transition.Conditions.Count == 0)
            {
                return true;
            }

            for (int i = 0; i < transition.Conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = transition.Conditions[i];
                if (condition != null && EvaluateCondition(condition) == false)
                {
                    return false;
                }
            }

            return true;
        }

        private bool EvaluateCondition(FusionAnimatorConditionDefinition condition)
        {
            if (TryResolvePreviewParameterReference(condition != null ? condition.ParameterId : null, out string parameterId, out FusionAnimatorParameterComponent component) == false)
            {
                return false;
            }

            FusionAnimatorParameterDefinition parameter = FindParameterById(parameterId);
            if (parameter == null)
            {
                return false;
            }

            if (component != FusionAnimatorParameterComponent.None && parameter.Type != FusionAnimatorParameterType.Vector2)
            {
                return false;
            }

            PreviewParameterEntry entry = FindPreviewEntry(parameterId);
            if (entry == null)
            {
                return false;
            }

            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                case FusionAnimatorParameterType.Trigger:
                {
                    bool value = entry.BoolValue;
                    switch (condition.Operator)
                    {
                        case FusionAnimatorConditionOperator.IsTrue:
                            return value;
                        case FusionAnimatorConditionOperator.IsFalse:
                            return value == false;
                        case FusionAnimatorConditionOperator.Equal:
                            return value == condition.BoolValue;
                        case FusionAnimatorConditionOperator.NotEqual:
                            return value != condition.BoolValue;
                        default:
                            return false;
                    }
                }
                case FusionAnimatorParameterType.Int:
                {
                    float lhs = condition.UseAbsoluteValue ? Mathf.Abs(entry.IntValue) : entry.IntValue;
                    return CompareNumeric(lhs, condition.IntValue, condition.Operator);
                }
                case FusionAnimatorParameterType.Float:
                {
                    float lhs = condition.UseAbsoluteValue ? Mathf.Abs(entry.FloatValue) : entry.FloatValue;
                    return CompareNumeric(lhs, condition.FloatValue, condition.Operator);
                }
                case FusionAnimatorParameterType.Vector2:
                {
                    float lhs;
                    switch (component)
                    {
                        case FusionAnimatorParameterComponent.X:
                            lhs = entry.Vector2Value.x;
                            break;
                        case FusionAnimatorParameterComponent.Y:
                            lhs = entry.Vector2Value.y;
                            break;
                        default:
                            lhs = entry.Vector2Value.magnitude;
                            break;
                    }

                    if (condition.UseAbsoluteValue)
                    {
                        lhs = Mathf.Abs(lhs);
                    }

                    switch (condition.Operator)
                    {
                        case FusionAnimatorConditionOperator.IsTrue:
                            return lhs > 0.000001f;
                        case FusionAnimatorConditionOperator.IsFalse:
                            return lhs <= 0.000001f;
                        default:
                            return CompareNumeric(lhs, condition.FloatValue, condition.Operator);
                    }
                }
                default:
                    return false;
            }
        }

        private Vector2 GetPreviewTwoDBlendTreeInputValue(FusionAnimatorBlendTreeDefinition blendTree)
        {
            if (blendTree == null)
            {
                return Vector2.zero;
            }

            if (TryGetPreviewVector2ParameterValue(blendTree.ParameterVector2Id, out Vector2 explicitVector2Input))
            {
                return explicitVector2Input;
            }

            bool hasVectorX = TryGetPreviewVector2ParameterValue(blendTree.ParameterXId, out Vector2 vectorXInput);
            bool hasVectorY = TryGetPreviewVector2ParameterValue(blendTree.ParameterYId, out Vector2 vectorYInput);
            if (hasVectorX && hasVectorY)
            {
                if (string.Equals(blendTree.ParameterXId, blendTree.ParameterYId, StringComparison.Ordinal))
                {
                    return vectorXInput;
                }

                return new Vector2(vectorXInput.x, vectorYInput.y);
            }

            if (hasVectorX)
            {
                return vectorXInput;
            }

            if (hasVectorY)
            {
                return vectorYInput;
            }

            return new Vector2(GetPreviewFloatValue(blendTree.ParameterXId), GetPreviewFloatValue(blendTree.ParameterYId));
        }

        private bool TryGetPreviewVector2ParameterValue(string parameterId, out Vector2 value)
        {
            value = Vector2.zero;
            if (string.IsNullOrWhiteSpace(parameterId))
            {
                return false;
            }

            if (TryResolvePreviewParameterReference(parameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false ||
                component != FusionAnimatorParameterComponent.None)
            {
                return false;
            }

            FusionAnimatorParameterDefinition parameter = FindParameterById(baseParameterId);
            if (parameter == null || parameter.Type != FusionAnimatorParameterType.Vector2)
            {
                return false;
            }

            PreviewParameterEntry entry = FindPreviewEntry(baseParameterId);
            if (entry == null)
            {
                return false;
            }

            value = entry.Vector2Value;
            return true;
        }

        private float GetPreviewFloatValue(string parameterId)
        {
            if (string.IsNullOrWhiteSpace(parameterId))
            {
                return 0.0f;
            }

            if (TryResolvePreviewParameterReference(parameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false)
            {
                return 0.0f;
            }

            PreviewParameterEntry entry = FindPreviewEntry(baseParameterId);
            FusionAnimatorParameterDefinition parameter = FindParameterById(baseParameterId);
            if (entry == null || parameter == null)
            {
                return 0.0f;
            }

            if (component != FusionAnimatorParameterComponent.None && parameter.Type != FusionAnimatorParameterType.Vector2)
            {
                return 0.0f;
            }

            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                    return entry.BoolValue ? 1.0f : 0.0f;
                case FusionAnimatorParameterType.Trigger:
                    return entry.BoolValue ? 1.0f : 0.0f;
                case FusionAnimatorParameterType.Int:
                    return entry.IntValue;
                case FusionAnimatorParameterType.Float:
                    return entry.FloatValue;
                case FusionAnimatorParameterType.Vector2:
                {
                    switch (component)
                    {
                        case FusionAnimatorParameterComponent.X:
                            return entry.Vector2Value.x;
                        case FusionAnimatorParameterComponent.Y:
                            return entry.Vector2Value.y;
                        default:
                            return entry.Vector2Value.magnitude;
                    }
                }
                default:
                    return 0.0f;
            }
        }

        private PreviewParameterEntry FindPreviewEntry(string parameterId)
        {
            for (int i = 0; i < _previewParameterEntries.Count; ++i)
            {
                PreviewParameterEntry entry = _previewParameterEntries[i];
                if (entry != null && string.Equals(entry.ParameterId, parameterId, StringComparison.Ordinal))
                {
                    return entry;
                }
            }

            return null;
        }

        private FusionAnimatorParameterDefinition FindParameterById(string parameterId)
        {
            if (TryResolvePreviewParameterReference(parameterId, out string baseParameterId, out _) == false ||
                _graph == null ||
                _graph.Parameters == null)
            {
                return null;
            }

            for (int i = 0; i < _graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter != null && string.Equals(parameter.Id, baseParameterId, StringComparison.Ordinal))
                {
                    return parameter;
                }
            }

            return null;
        }

        private static bool TryResolvePreviewParameterReference(
            string parameterReference,
            out string parameterId,
            out FusionAnimatorParameterComponent component)
        {
            return FusionAnimatorParameterReferenceUtility.TryParse(parameterReference, out parameterId, out component);
        }

        private FusionAnimatorStateDefinition FindStateById(string stateId)
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

        private static bool CompareNumeric(float lhs, float rhs, FusionAnimatorConditionOperator op)
        {
            switch (op)
            {
                case FusionAnimatorConditionOperator.Equal:
                    return Mathf.Approximately(lhs, rhs);
                case FusionAnimatorConditionOperator.NotEqual:
                    return Mathf.Approximately(lhs, rhs) == false;
                case FusionAnimatorConditionOperator.Greater:
                    return lhs > rhs;
                case FusionAnimatorConditionOperator.GreaterOrEqual:
                    return lhs >= rhs;
                case FusionAnimatorConditionOperator.Less:
                    return lhs < rhs;
                case FusionAnimatorConditionOperator.LessOrEqual:
                    return lhs <= rhs;
                default:
                    return false;
            }
        }

        private static Type ResolveInputActionReferenceType()
        {
            Type resolved = Type.GetType("UnityEngine.InputSystem.InputActionReference, Unity.InputSystem");
            if (resolved != null)
            {
                return resolved;
            }

            return Type.GetType("UnityEngine.InputSystem.InputActionReference, Unity.InputSystem.Editor");
        }

        private static Type ResolveInputSystemType()
        {
            return Type.GetType("UnityEngine.InputSystem.InputSystem, Unity.InputSystem");
        }

        private static MethodInfo ResolveInputSystemUpdateMethod()
        {
            if (InputSystemType == null)
            {
                return null;
            }

            return InputSystemType.GetMethod("Update", BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        }

        private static Type ResolveInputUpdateType()
        {
            return Type.GetType("UnityEngine.InputSystem.LowLevel.InputUpdateType, Unity.InputSystem");
        }

        private static MethodInfo ResolveInputSystemUpdateWithTypeMethod()
        {
            if (InputSystemType == null || InputUpdateTypeType == null)
            {
                return null;
            }

            return InputSystemType.GetMethod("Update", BindingFlags.Public | BindingFlags.Static, null, new[] { InputUpdateTypeType }, null);
        }

        private static MethodInfo ResolveInputSystemFindControlsMethod()
        {
            if (InputSystemType == null)
            {
                return null;
            }

            try
            {
                MethodInfo[] methods = InputSystemType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < methods.Length; ++i)
                {
                    MethodInfo method = methods[i];
                    if (method == null || method.Name != "FindControls" || method.IsGenericMethodDefinition)
                    {
                        continue;
                    }

                    ParameterInfo[] parameters = method.GetParameters();
                    if (parameters == null || parameters.Length != 1)
                    {
                        continue;
                    }

                    if (parameters[0].ParameterType == typeof(string))
                    {
                        return method;
                    }
                }

                return null;
            }
            catch
            {
                return null;
            }
        }

        private static object ResolveEditorInputUpdateEnumValue()
        {
            if (InputUpdateTypeType == null)
            {
                return null;
            }

            try
            {
                return Enum.Parse(InputUpdateTypeType, "Editor", true);
            }
            catch
            {
                return null;
            }
        }

        private static void UpdateInputSystemInEditor()
        {
            if (EditorApplication.isPlaying || (InputSystemUpdateWithTypeMethod == null && InputSystemUpdateMethod == null))
            {
                return;
            }

            try
            {
                if (InputSystemUpdateWithTypeMethod != null && EditorInputUpdateEnumValue != null)
                {
                    InputSystemUpdateWithTypeMethod.Invoke(null, new[] { EditorInputUpdateEnumValue });
                }
            }
            catch
            {
                // ignored, try fallback overload below
            }

            try
            {
                if (InputSystemUpdateMethod != null)
                {
                    InputSystemUpdateMethod.Invoke(null, null);
                }
            }
            catch
            {
                // Keep preview responsive even if InputSystem update invocation fails.
            }
        }

        private static bool TryReadInputActionValue(UnityEngine.Object inputActionReference, out float value)
        {
            value = 0.0f;
            if (inputActionReference == null)
            {
                return false;
            }

            try
            {
                PropertyInfo actionProperty = inputActionReference.GetType().GetProperty("action", BindingFlags.Instance | BindingFlags.Public);
                if (actionProperty == null)
                {
                    return false;
                }

                object action = actionProperty.GetValue(inputActionReference, null);
                if (action == null)
                {
                    return false;
                }

                MethodInfo enableMethod = action.GetType().GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public);
                enableMethod?.Invoke(action, null);

                MethodInfo[] methods = action.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
                for (int i = 0; i < methods.Length; ++i)
                {
                    MethodInfo method = methods[i];
                    if (method.Name == "ReadValue" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                    {
                        object rawValue = method.MakeGenericMethod(typeof(float)).Invoke(action, null);
                        if (rawValue is float floatValue)
                        {
                            value = floatValue;
                            return true;
                        }
                    }
                }

                MethodInfo readValueAsObjectMethod = action.GetType().GetMethod("ReadValueAsObject", BindingFlags.Instance | BindingFlags.Public);
                if (readValueAsObjectMethod != null)
                {
                    object rawObjectValue = readValueAsObjectMethod.Invoke(action, null);
                    if (rawObjectValue is float rawFloat)
                    {
                        value = rawFloat;
                        return true;
                    }
                }
            }
            catch
            {
                // ignored, fallback below
            }

            return TryReadInputScalarFromBindings(inputActionReference, out value);
        }

        private static bool TryReadInputActionVector2(UnityEngine.Object inputActionReference, out Vector2 value)
        {
            value = Vector2.zero;
            if (inputActionReference == null)
            {
                return false;
            }

            try
            {
                PropertyInfo actionProperty = inputActionReference.GetType().GetProperty("action", BindingFlags.Instance | BindingFlags.Public);
                if (actionProperty == null)
                {
                    return false;
                }

                object action = actionProperty.GetValue(inputActionReference, null);
                if (action == null)
                {
                    return false;
                }

                MethodInfo enableMethod = action.GetType().GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public);
                enableMethod?.Invoke(action, null);

                MethodInfo[] methods = action.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
                for (int i = 0; i < methods.Length; ++i)
                {
                    MethodInfo method = methods[i];
                    if (method.Name == "ReadValue" && method.IsGenericMethodDefinition && method.GetParameters().Length == 0)
                    {
                        object rawValue = method.MakeGenericMethod(typeof(Vector2)).Invoke(action, null);
                        if (rawValue is Vector2 vector2Value)
                        {
                            value = vector2Value;
                            return true;
                        }
                    }
                }

                MethodInfo readValueAsObjectMethod = action.GetType().GetMethod("ReadValueAsObject", BindingFlags.Instance | BindingFlags.Public);
                if (readValueAsObjectMethod != null)
                {
                    object rawObjectValue = readValueAsObjectMethod.Invoke(action, null);
                    if (rawObjectValue is Vector2 rawVector2)
                    {
                        value = rawVector2;
                        return true;
                    }
                }
            }
            catch
            {
                // ignored, fallback below
            }

            return TryReadInputVector2FromBindings(inputActionReference, out value);
        }

        private static bool TryReadInputScalarFromBindings(UnityEngine.Object inputActionReference, out float value)
        {
            value = 0.0f;
            if (TryResolveAction(inputActionReference, out object action) == false)
            {
                return false;
            }

            if (TryReadInputVector2FromBindings(inputActionReference, out Vector2 vector2Value))
            {
                value = vector2Value.magnitude;
                return true;
            }

            if (TryReadBindingControls(action, out List<object> values) == false)
            {
                return false;
            }

            float best = 0.0f;
            bool hasAny = false;
            for (int i = 0; i < values.Count; ++i)
            {
                if (TryConvertToFloat(values[i], out float scalar) == false)
                {
                    continue;
                }

                if (hasAny == false || Mathf.Abs(scalar) > Mathf.Abs(best))
                {
                    best = scalar;
                    hasAny = true;
                }
            }

            value = best;
            return hasAny;
        }

        private static bool TryReadInputVector2FromBindings(UnityEngine.Object inputActionReference, out Vector2 value)
        {
            value = Vector2.zero;
            if (TryResolveAction(inputActionReference, out object action) == false)
            {
                return false;
            }

            Vector2 aggregate = Vector2.zero;
            bool hasAny = TryReadCompositeVectorFromBindings(action, out aggregate);

            if (TryReadBindingControls(action, out List<object> values) == false)
            {
                value = aggregate;
                return hasAny;
            }

            for (int i = 0; i < values.Count; ++i)
            {
                object raw = values[i];
                if (raw is Vector2 vector2)
                {
                    aggregate += vector2;
                    hasAny = true;
                    continue;
                }

                if (TryConvertToFloat(raw, out float scalar))
                {
                    if (Mathf.Abs(scalar) > Mathf.Abs(aggregate.x))
                    {
                        aggregate.x = scalar;
                    }

                    hasAny = true;
                }
            }

            if (hasAny == false)
            {
                return false;
            }

            value = aggregate;
            return true;
        }

        private static bool TryReadCompositeVectorFromBindings(object action, out Vector2 value)
        {
            value = Vector2.zero;
            if (action == null || InputSystemType == null)
            {
                return false;
            }

            try
            {
                PropertyInfo bindingsProperty = action.GetType().GetProperty("bindings", BindingFlags.Instance | BindingFlags.Public);
                System.Collections.IEnumerable bindings = bindingsProperty != null ? bindingsProperty.GetValue(action, null) as System.Collections.IEnumerable : null;
                if (bindings == null)
                {
                    return false;
                }

                bool hasAny = false;
                foreach (object binding in bindings)
                {
                    if (binding == null)
                    {
                        continue;
                    }

                    Type bindingType = binding.GetType();
                    if (TryGetBindingFlag(bindingType, binding, "isPartOfComposite") == false)
                    {
                        continue;
                    }

                    string path = TryGetBindingPath(bindingType, binding, "effectivePath");
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        path = TryGetBindingPath(bindingType, binding, "path");
                    }

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    if (TryResolveControlsForPath(path, out List<object> controls) == false)
                    {
                        continue;
                    }

                    float bestScalar = 0.0f;
                    bool hasScalar = false;
                    for (int controlIndex = 0; controlIndex < controls.Count; ++controlIndex)
                    {
                        object control = controls[controlIndex];
                        if (control == null)
                        {
                            continue;
                        }

                        MethodInfo readValueAsObjectMethod = control.GetType().GetMethod("ReadValueAsObject", BindingFlags.Instance | BindingFlags.Public);
                        if (readValueAsObjectMethod == null)
                        {
                            continue;
                        }

                        object raw = readValueAsObjectMethod.Invoke(control, null);
                        if (TryConvertToFloat(raw, out float scalar) == false)
                        {
                            continue;
                        }

                        if (hasScalar == false || Mathf.Abs(scalar) > Mathf.Abs(bestScalar))
                        {
                            bestScalar = scalar;
                            hasScalar = true;
                        }
                    }

                    if (hasScalar == false)
                    {
                        continue;
                    }

                    string partName = TryGetBindingPath(bindingType, binding, "name");
                    string lowerName = string.IsNullOrWhiteSpace(partName) ? string.Empty : partName.ToLowerInvariant();
                    if (lowerName.Contains("up"))
                    {
                        value.y += bestScalar;
                        hasAny = true;
                    }
                    else if (lowerName.Contains("down"))
                    {
                        value.y -= bestScalar;
                        hasAny = true;
                    }
                    else if (lowerName.Contains("left"))
                    {
                        value.x -= bestScalar;
                        hasAny = true;
                    }
                    else if (lowerName.Contains("right"))
                    {
                        value.x += bestScalar;
                        hasAny = true;
                    }
                    else if (lowerName.Contains("positive"))
                    {
                        value.x += bestScalar;
                        hasAny = true;
                    }
                    else if (lowerName.Contains("negative"))
                    {
                        value.x -= bestScalar;
                        hasAny = true;
                    }
                }

                return hasAny;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryResolveAction(UnityEngine.Object inputActionReference, out object action)
        {
            action = null;
            if (inputActionReference == null || InputSystemType == null)
            {
                return false;
            }

            try
            {
                PropertyInfo actionProperty = inputActionReference.GetType().GetProperty("action", BindingFlags.Instance | BindingFlags.Public);
                action = actionProperty != null ? actionProperty.GetValue(inputActionReference, null) : null;
                if (action == null)
                {
                    return false;
                }

                PropertyInfo mapProperty = action.GetType().GetProperty("actionMap", BindingFlags.Instance | BindingFlags.Public);
                object actionMap = mapProperty != null ? mapProperty.GetValue(action, null) : null;
                if (actionMap != null)
                {
                    PropertyInfo assetProperty = actionMap.GetType().GetProperty("asset", BindingFlags.Instance | BindingFlags.Public);
                    object actionAsset = assetProperty != null ? assetProperty.GetValue(actionMap, null) : null;
                    MethodInfo assetEnableMethod = actionAsset != null ? actionAsset.GetType().GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public) : null;
                    assetEnableMethod?.Invoke(actionAsset, null);

                    MethodInfo mapEnableMethod = actionMap.GetType().GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public);
                    mapEnableMethod?.Invoke(actionMap, null);
                }

                MethodInfo enableMethod = action.GetType().GetMethod("Enable", BindingFlags.Instance | BindingFlags.Public);
                enableMethod?.Invoke(action, null);
                return true;
            }
            catch
            {
                action = null;
                return false;
            }
        }

        private static bool TryReadBindingControls(object action, out List<object> values)
        {
            values = null;
            if (action == null)
            {
                return false;
            }

            try
            {
                PropertyInfo bindingsProperty = action.GetType().GetProperty("bindings", BindingFlags.Instance | BindingFlags.Public);
                if (bindingsProperty == null)
                {
                    return false;
                }

                System.Collections.IEnumerable bindings = bindingsProperty.GetValue(action, null) as System.Collections.IEnumerable;
                if (bindings == null)
                {
                    return false;
                }

                values = new List<object>();
                foreach (object binding in bindings)
                {
                    if (binding == null)
                    {
                        continue;
                    }

                    Type bindingType = binding.GetType();
                    bool isComposite = TryGetBindingFlag(bindingType, binding, "isComposite");
                    bool isPartOfComposite = TryGetBindingFlag(bindingType, binding, "isPartOfComposite");
                    if (isComposite)
                    {
                        continue;
                    }
                    if (isPartOfComposite)
                    {
                        continue;
                    }

                    string path = TryGetBindingPath(bindingType, binding, "effectivePath");
                    if (string.IsNullOrWhiteSpace(path))
                    {
                        path = TryGetBindingPath(bindingType, binding, "path");
                    }

                    if (string.IsNullOrWhiteSpace(path))
                    {
                        continue;
                    }

                    if (TryResolveControlsForPath(path, out List<object> controls) == false)
                    {
                        continue;
                    }

                    for (int controlIndex = 0; controlIndex < controls.Count; ++controlIndex)
                    {
                        object control = controls[controlIndex];
                        if (control == null)
                        {
                            continue;
                        }

                        MethodInfo readValueAsObjectMethod = control.GetType().GetMethod("ReadValueAsObject", BindingFlags.Instance | BindingFlags.Public);
                        if (readValueAsObjectMethod == null)
                        {
                            continue;
                        }

                        object raw = readValueAsObjectMethod.Invoke(control, null);
                        if (raw != null)
                        {
                            values.Add(raw);
                        }
                    }
                }
            }
            catch
            {
                return false;
            }

            return values != null && values.Count > 0;
        }

        private static bool TryResolveControlsForPath(string path, out List<object> controls)
        {
            controls = null;
            if (InputSystemType == null || string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            try
            {
                if (InputSystemFindControlsMethod != null)
                {
                    object readOnlyArray = InputSystemFindControlsMethod.Invoke(null, new object[] { path });
                    if (TryExtractControlsFromReadOnlyArray(readOnlyArray, out controls))
                    {
                        return true;
                    }
                }

                MethodInfo findControlMethod = InputSystemType.GetMethod("FindControl", BindingFlags.Public | BindingFlags.Static, null, new[] { typeof(string) }, null);
                if (findControlMethod == null)
                {
                    return false;
                }

                object control = findControlMethod.Invoke(null, new object[] { path });
                if (control == null)
                {
                    return false;
                }

                controls = new List<object> { control };
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool TryExtractControlsFromReadOnlyArray(object readOnlyArray, out List<object> controls)
        {
            controls = null;
            if (readOnlyArray == null)
            {
                return false;
            }

            try
            {
                Type arrayType = readOnlyArray.GetType();
                PropertyInfo countProperty = arrayType.GetProperty("Count", BindingFlags.Instance | BindingFlags.Public) ??
                                             arrayType.GetProperty("length", BindingFlags.Instance | BindingFlags.Public);
                PropertyInfo indexProperty = arrayType.GetProperty("Item", BindingFlags.Instance | BindingFlags.Public, null, null, new[] { typeof(int) }, null);
                if (countProperty == null || indexProperty == null)
                {
                    return false;
                }

                object rawCount = countProperty.GetValue(readOnlyArray, null);
                if (rawCount is int count == false || count <= 0)
                {
                    return false;
                }

                controls = new List<object>(count);
                for (int i = 0; i < count; ++i)
                {
                    object control = indexProperty.GetValue(readOnlyArray, new object[] { i });
                    if (control != null)
                    {
                        controls.Add(control);
                    }
                }

                return controls.Count > 0;
            }
            catch
            {
                controls = null;
                return false;
            }
        }

        private static bool TryConvertToFloat(object raw, out float value)
        {
            value = 0.0f;
            if (raw is float f)
            {
                value = f;
                return true;
            }

            if (raw is double d)
            {
                value = (float)d;
                return true;
            }

            if (raw is int i)
            {
                value = i;
                return true;
            }

            if (raw is bool b)
            {
                value = b ? 1.0f : 0.0f;
                return true;
            }

            return false;
        }

        private static bool TryGetBindingFlag(Type bindingType, object binding, string propertyName)
        {
            PropertyInfo property = bindingType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.PropertyType == typeof(bool))
            {
                object raw = property.GetValue(binding, null);
                if (raw is bool flagValue)
                {
                    return flagValue;
                }
            }

            return false;
        }

        private static string TryGetBindingPath(Type bindingType, object binding, string propertyName)
        {
            PropertyInfo property = bindingType.GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null && property.PropertyType == typeof(string))
            {
                return property.GetValue(binding, null) as string;
            }

            return null;
        }
    }
}
