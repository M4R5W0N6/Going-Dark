using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FusionAnimator.Editor
{
    internal sealed class FusionAnimatorGraphView : GraphView
    {
        private const string EntryLinkEdgeId = "__entry_link__";
        private const float DefaultPreviewOrbitDistanceScale = 2.8f;
        private static readonly Vector2 EntryNodeOffset = new Vector2(-300.0f, -120.0f);
        private static readonly Vector2 AnyNodeOffset = new Vector2(-300.0f, 20.0f);
        private static readonly Vector2 ExitNodeOffset = new Vector2(300.0f, -40.0f);

        private sealed class StateNodeView
        {
            public Node Node;
            public Port Input;
            public Port Output;
            public Label LayerLabel;
            public Label MotionLabel;
            public VisualElement BlendTreeSummary;
            public Label RuntimeBadge;
        }

        [Serializable]
        private sealed class ClipboardState
        {
            public string Id;
            public string Name;
            public string LayerId;
            public string LayerName;
            public Vector2 NodePosition;
            public float MinDurationSeconds;
            public bool CanTransitionOut;
            public bool WriteDefaults;
            public FusionAnimatorMotionType MotionType;
            public List<FusionAnimatorClipSlot> Clips = new List<FusionAnimatorClipSlot>();
            public FusionAnimatorBlendTreeDefinition BlendTree;
            public FusionAnimatorStatePresentationDefinition Presentation;
        }

        [Serializable]
        private sealed class ClipboardLayer
        {
            public string Id;
            public string Name;
            public int Priority;
            public float DefaultWeight = 1.0f;
            public bool EnabledByDefault = true;
            public FusionAnimatorLayerBlendMode BlendMode = FusionAnimatorLayerBlendMode.Override;
            public AvatarMask AvatarMask;
            public int SyncedLayerIndex = -1;
            public bool SyncTiming;
            public bool IKPass;
        }

        [Serializable]
        private sealed class ClipboardParameter
        {
            public string Id;
            public string Name = "Parameter";
            public FusionAnimatorParameterType Type = FusionAnimatorParameterType.Float;
            public bool DefaultBool;
            public bool Invert;
            public int DefaultInt;
            public float DefaultFloat;
            public Vector2 DefaultVector2;
            public string PreviewInputBinding;
            public float PreviewInputScale = 1.0f;
            public FusionAnimatorPreviewBoolInputSource PreviewBoolInputSource = FusionAnimatorPreviewBoolInputSource.Float;
            public FusionAnimatorConditionOperator PreviewBoolInputOperator = FusionAnimatorConditionOperator.Greater;
            public float PreviewBoolInputCompareValue = 0.5f;
        }

        [Serializable]
        private sealed class ClipboardTransition
        {
            public string Name;
            public string FromStateId;
            public string ToStateId;
            public int Priority;
            public bool Mute;
            public bool Solo;
            public bool HasExitTime;
            public float ExitTimeNormalized;
            public float StartOffsetNormalized;
            public bool FixedDuration;
            public float BlendDurationSeconds;
            public FusionAnimatorInterruptionSource InterruptionSource;
            public bool CanInterrupt;
            public List<FusionAnimatorConditionDefinition> Conditions = new List<FusionAnimatorConditionDefinition>();
        }

        [Serializable]
        private sealed class ClipboardScope
        {
            public string LayerId;
            public string ScopePath;
            public bool HasScopeNodePosition;
            public Vector2 ScopeNodePosition;
            public Vector2 EntryNodePosition = new Vector2(-300.0f, -120.0f);
            public Vector2 AnyNodePosition = new Vector2(-300.0f, 20.0f);
            public Vector2 ExitNodePosition = new Vector2(300.0f, -40.0f);
        }

        [Serializable]
        private sealed class ClipboardPayload
        {
            public List<ClipboardLayer> Layers = new List<ClipboardLayer>();
            public List<ClipboardParameter> Parameters = new List<ClipboardParameter>();
            public List<ClipboardScope> Scopes = new List<ClipboardScope>();
            public List<ClipboardState> States = new List<ClipboardState>();
            public List<ClipboardTransition> Transitions = new List<ClipboardTransition>();
        }

        private sealed class ClipboardScopeRemap
        {
            public string SourceLayerId;
            public string SourceScopePath;
            public string DestinationLayerId;
            public string DestinationScopePath;
        }

        private sealed class PreviewRootBinding
        {
            public Transform Transform;
            public Vector3 LocalPosition;
            public Quaternion LocalRotation;
            public Vector3 LocalScale;
        }

        public readonly struct PreviewLayerPoseInput
        {
            public readonly FusionAnimatorLayerDefinition Layer;
            public readonly IList<AnimationClip> Clips;
            public readonly IList<float> SampleTimes;
            public readonly IList<float> SampleWeights;
            public readonly float LayerWeight;
            public readonly bool IgnoreAvatarMask;

            public PreviewLayerPoseInput(
                FusionAnimatorLayerDefinition layer,
                IList<AnimationClip> clips,
                IList<float> sampleTimes,
                IList<float> sampleWeights,
                float layerWeight,
                bool ignoreAvatarMask = false)
            {
                Layer = layer;
                Clips = clips;
                SampleTimes = sampleTimes;
                SampleWeights = sampleWeights;
                LayerWeight = layerWeight;
                IgnoreAvatarMask = ignoreAvatarMask;
            }
        }

        private FusionAnimatorGraphAsset _graph;
        private readonly Dictionary<string, StateNodeView> _stateViews = new Dictionary<string, StateNodeView>(StringComparer.Ordinal);
        private readonly Dictionary<string, Edge> _edgeViews = new Dictionary<string, Edge>(StringComparer.Ordinal);
        private bool _suppressChangeCallbacks;
        private string _searchFilter = string.Empty;
        private string _scopeFilter = string.Empty;
        private string _activeLayerId = string.Empty;
        private string _selectedTransitionId;
        private string _hoveredLayerId;
        private string _hoveredParameterId;
        private int _pasteIteration;
        private MiniMap _miniMap;
        private Label _previewBackgroundLabel;
        private Image _previewImage;
        private PreviewRenderUtility _previewRenderUtility;
        private GameObject _previewRenderSource;
        private GameObject _previewRenderInstance;
        private RenderTexture _previewRenderTexture;
        private bool _previewApplyRootMotion;
        private float _previewOrbitYaw = 35.0f;
        private float _previewOrbitPitch = 18.0f;
        private float _previewOrbitDistanceScale = DefaultPreviewOrbitDistanceScale;
        private Vector3 _previewOrbitTargetOffset = Vector3.zero;
        private float _previewLastBoundsRadius = 1.0f;
        private Vector3 _previewFocusAnchor = Vector3.zero;
        private bool _previewFocusAnchorInitialized;
        private VisualElement _previewCameraGizmo;
        private VisualElement _previewCameraGizmoAxes;
        private VisualElement _previewAxisX;
        private VisualElement _previewAxisY;
        private VisualElement _previewAxisZ;
        private bool _previewCameraGizmoDragging;
        private int _previewCameraGizmoButton = -1;
        private Vector2 _previewCameraGizmoLastPosition;
        private readonly List<PreviewRootBinding> _previewRootBindings = new List<PreviewRootBinding>();
        private readonly List<Transform> _previewBlendTransforms = new List<Transform>();
        private Vector3[] _previewBlendPositions;
        private Quaternion[] _previewBlendRotations;
        private Vector3[] _previewBlendScales;
        private Vector3[] _previewBlendAccumulatedPositions;
        private Vector3[] _previewBlendAccumulatedScales;
        private Vector4[] _previewBlendAccumulatedRotations;
        private int _previewBlendSourceInstanceId;
        private string[] _previewBlendTransformPaths;
        private Vector3[] _previewBlendBasePositions;
        private Quaternion[] _previewBlendBaseRotations;
        private Vector3[] _previewBlendBaseScales;
        private Vector3[] _previewBlendLayerPositions;
        private Quaternion[] _previewBlendLayerRotations;
        private Vector3[] _previewBlendLayerScales;
        private Vector3[] _previewBlendCompositePositions;
        private Quaternion[] _previewBlendCompositeRotations;
        private Vector3[] _previewBlendCompositeScales;
        private readonly Dictionary<AvatarMask, float[]> _previewAvatarMaskWeights = new Dictionary<AvatarMask, float[]>();
        private readonly Dictionary<string, string> _scopeNodePathById = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _layerNodeLayerIdById = new Dictionary<string, string>(StringComparer.Ordinal);
        private readonly HashSet<string> _visibleStateIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _programmaticEdgeRemovalIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _previewActiveStateIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _previewBlendStateIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _previewActiveLayerIds = new HashSet<string>(StringComparer.Ordinal);
        private bool _miniMapDefaultTopRightPending = true;
        private bool _allowNodeRemovalFromGraphChange;

        public Action<FusionAnimatorStateDefinition, FusionAnimatorTransitionDefinition> OnSelectionChanged;
        public Action<string> OnLayerNodeSelected;
        public Action OnGraphDirty;
        public Action OnBackgroundClicked;
        public Action<string, string> OnScopeChanged;
        public Action<string> OnScopeNodeRenameRequested;
        public Action OnPreviewCameraChanged;

        public FusionAnimatorGraphView()
        {
            style.flexGrow = 1.0f;
            SetupZoom(ContentZoomer.DefaultMinScale, ContentZoomer.DefaultMaxScale);

            this.AddManipulator(new ContentDragger());
            this.AddManipulator(new SelectionDragger());
            this.AddManipulator(new RectangleSelector());
            this.AddManipulator(new ClickSelector());

            _previewImage = new Image
            {
                scaleMode = ScaleMode.ScaleAndCrop,
                pickingMode = PickingMode.Ignore,
            };
            _previewImage.style.position = Position.Absolute;
            _previewImage.style.left = 0.0f;
            _previewImage.style.right = 0.0f;
            _previewImage.style.top = 0.0f;
            _previewImage.style.bottom = 0.0f;
            _previewImage.style.opacity = 0.55f;
            _previewImage.style.display = DisplayStyle.None;
            Insert(0, _previewImage);

            GridBackground grid = new GridBackground();
            grid.name = "fa-grid-background";
            Insert(1, grid);
            grid.StretchToParentSize();
            grid.SendToBack();
            grid.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 1.0f);
            grid.style.opacity = 1.0f;
            style.backgroundColor = new Color(0.13f, 0.13f, 0.13f, 1.0f);
            contentViewContainer.style.backgroundColor = Color.clear;

            BuildMiniMapOverlay();

            _previewBackgroundLabel = new Label();
            _previewBackgroundLabel.style.position = Position.Absolute;
            _previewBackgroundLabel.style.left = 10.0f;
            _previewBackgroundLabel.style.bottom = 10.0f;
            _previewBackgroundLabel.style.fontSize = 10.0f;
            _previewBackgroundLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            _previewBackgroundLabel.style.color = new Color(0.82f, 0.86f, 0.90f, 0.55f);
            _previewBackgroundLabel.style.paddingLeft = 6.0f;
            _previewBackgroundLabel.style.paddingRight = 6.0f;
            _previewBackgroundLabel.style.paddingTop = 2.0f;
            _previewBackgroundLabel.style.paddingBottom = 2.0f;
            _previewBackgroundLabel.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.38f);
            _previewBackgroundLabel.style.whiteSpace = WhiteSpace.Normal;
            _previewBackgroundLabel.style.maxWidth = new Length(72.0f, LengthUnit.Percent);
            _previewBackgroundLabel.pickingMode = PickingMode.Ignore;
            _previewBackgroundLabel.style.display = DisplayStyle.None;
            Add(_previewBackgroundLabel);
            BuildPreviewCameraGizmo();

            graphViewChanged = HandleGraphViewChanged;
            serializeGraphElements = SerializeGraphElements;
            canPasteSerializedData = CanPasteSerializedData;
            unserializeAndPaste = UnserializeAndPaste;
            this.RegisterCallback<MouseDownEvent>(OnMouseDown, TrickleDown.TrickleDown);
            this.RegisterCallback<MouseUpEvent>(OnMouseUpSelectionSync, TrickleDown.TrickleDown);
            this.RegisterCallback<KeyDownEvent>(OnKeyDown, TrickleDown.TrickleDown);
            this.RegisterCallback<ExecuteCommandEvent>(OnExecuteCommand, TrickleDown.TrickleDown);
            this.RegisterCallback<GeometryChangedEvent>(OnGeometryChanged);
        }

        private void OnMouseUpSelectionSync(MouseUpEvent evt)
        {
            if (evt == null || evt.button != 0)
            {
                return;
            }

            if (selection == null || selection.Count == 0)
            {
                OnSelectionChanged?.Invoke(null, null);
                return;
            }

            List<ISelectable> selectedElements = new List<ISelectable>(selection.Count);
            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is ISelectable selectable)
                {
                    selectedElements.Add(selectable);
                }
            }

            HandleSelectionChanged(selectedElements);
        }

        private void OnKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if ((evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace) &&
                SelectionContainsDeletableNode())
            {
                _allowNodeRemovalFromGraphChange = true;
            }
        }

        private void OnExecuteCommand(ExecuteCommandEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if ((string.Equals(evt.commandName, "Delete", StringComparison.Ordinal) ||
                 string.Equals(evt.commandName, "SoftDelete", StringComparison.Ordinal)) &&
                SelectionContainsDeletableNode())
            {
                _allowNodeRemovalFromGraphChange = true;
            }
        }

        private bool SelectionContainsDeletableNode()
        {
            if (selection == null || selection.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node)
                {
                    string nodeId = node.userData as string;
                    if (nodeId == FusionAnimatorGraphAsset.SpecialNodeEntryId ||
                        nodeId == FusionAnimatorGraphAsset.SpecialNodeAnyId ||
                        nodeId == FusionAnimatorGraphAsset.SpecialNodeExitId)
                    {
                        continue;
                    }

                    return true;
                }
            }

            return false;
        }

        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter)
        {
            List<Port> compatiblePorts = new List<Port>();

            ports.ForEach(port =>
            {
                if (port == startPort)
                {
                    return;
                }

                if (port.node == startPort.node)
                {
                    return;
                }

                if (port.direction == startPort.direction)
                {
                    return;
                }

                compatiblePorts.Add(port);
            });

            return compatiblePorts;
        }

        public void BindGraph(FusionAnimatorGraphAsset graph)
        {
            _graph = graph;
            EnsureActiveContext();
            RebuildFromGraphData();
        }

        public void SetSearchFilter(string filter)
        {
            _searchFilter = string.IsNullOrWhiteSpace(filter) ? string.Empty : filter.Trim().ToLowerInvariant();
            ApplySearchFilter();
        }

        public void SetScopePath(string scopePath)
        {
            string normalized = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath.Trim();
            if (string.Equals(_scopeFilter, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _scopeFilter = normalized;
            RebuildFromGraphData();
            OnScopeChanged?.Invoke(_activeLayerId, _scopeFilter);
        }

        public void SetRenderContext(string layerId, string scopePath)
        {
            string normalizedLayer = string.IsNullOrWhiteSpace(layerId) ? string.Empty : layerId.Trim();
            string normalizedScope = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath.Trim();

            bool changed =
                string.Equals(_activeLayerId, normalizedLayer, StringComparison.Ordinal) == false ||
                string.Equals(_scopeFilter, normalizedScope, StringComparison.Ordinal) == false;

            _activeLayerId = normalizedLayer;
            _scopeFilter = normalizedScope;
            EnsureActiveContext();
            if (changed)
            {
                RebuildFromGraphData();
            }
        }

        public void SetHoveredLayer(string layerId)
        {
            string normalized = string.IsNullOrWhiteSpace(layerId) ? null : layerId;
            if (string.Equals(_hoveredLayerId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _hoveredLayerId = normalized;
            RefreshNodeLayerHighlight();
        }

        public void SetHoveredParameter(string parameterId)
        {
            string normalized = string.IsNullOrWhiteSpace(parameterId) ? null : parameterId;
            if (string.Equals(_hoveredParameterId, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _hoveredParameterId = normalized;
            RefreshTransitionBadges();
        }

        public void SetSelectedTransition(string transitionId)
        {
            _selectedTransitionId = transitionId;
            RefreshTransitionBadges();
        }

        public bool SelectTransitionById(string transitionId, bool center = false)
        {
            if (string.IsNullOrWhiteSpace(transitionId) || _edgeViews.TryGetValue(transitionId, out Edge edge) == false || edge == null)
            {
                return false;
            }

            ClearSelection();
            AddToSelection(edge);
            if (center)
            {
                Node fromNode = edge.output != null ? edge.output.node as Node : null;
                Node toNode = edge.input != null ? edge.input.node as Node : null;
                if (toNode != null)
                {
                    CenterOnNode(toNode);
                }
                else if (fromNode != null)
                {
                    CenterOnNode(fromNode);
                }

                FrameSelection();
                schedule.Execute(() =>
                {
                    if (edge.panel != null)
                    {
                        FrameSelection();
                    }
                }).ExecuteLater(1);
            }

            return true;
        }

        public bool TryGetSelectedEntryLinkTargetStateId(out string stateId)
        {
            stateId = null;
            if (selection == null || selection.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Edge edge && string.Equals(edge.userData as string, EntryLinkEdgeId, StringComparison.Ordinal))
                {
                    string targetNodeId = edge.input?.node?.userData as string;
                    if (TryResolveTransitionEndpointForCreate(targetNodeId, out string resolvedStateId))
                    {
                        stateId = resolvedStateId;
                        return string.IsNullOrWhiteSpace(stateId) == false;
                    }
                }
            }

            return false;
        }

        public bool CenterOnState(string stateId)
        {
            if (string.IsNullOrWhiteSpace(stateId) || _stateViews.TryGetValue(stateId, out StateNodeView view) == false || view?.Node == null)
            {
                return false;
            }

            CenterOnNode(view.Node);
            return true;
        }

        public bool CenterOnScopeNode(string scopePath)
        {
            if (string.IsNullOrWhiteSpace(scopePath))
            {
                return false;
            }

            string nodeId = "__scope__:" + scopePath;
            if (_stateViews.TryGetValue(nodeId, out StateNodeView view) == false || view?.Node == null)
            {
                return false;
            }

            CenterOnNode(view.Node);
            return true;
        }

        public bool CenterOnLayerNode(string layerId)
        {
            if (string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in _layerNodeLayerIdById)
            {
                if (string.Equals(pair.Value, layerId, StringComparison.Ordinal) &&
                    _stateViews.TryGetValue(pair.Key, out StateNodeView view) &&
                    view?.Node != null)
                {
                    CenterOnNode(view.Node);
                    return true;
                }
            }

            return false;
        }

        public bool SelectScopeNodeByPath(string scopePath, bool center = false)
        {
            if (string.IsNullOrWhiteSpace(scopePath))
            {
                return false;
            }

            string nodeId = "__scope__:" + scopePath;
            if (_stateViews.TryGetValue(nodeId, out StateNodeView view) == false || view?.Node == null)
            {
                return false;
            }

            return SelectNode(view.Node, center);
        }

        public bool SelectLayerNodeByLayerId(string layerId, bool center = false)
        {
            if (string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            foreach (KeyValuePair<string, string> pair in _layerNodeLayerIdById)
            {
                if (string.Equals(pair.Value, layerId, StringComparison.Ordinal) &&
                    _stateViews.TryGetValue(pair.Key, out StateNodeView view) &&
                    view?.Node != null)
                {
                    return SelectNode(view.Node, center);
                }
            }

            return false;
        }

        public bool TryGetSelectedLayerNodeId(out string layerId)
        {
            layerId = null;
            if (selection == null || selection.Count != 1)
            {
                return false;
            }

            if (selection[0] is Node node)
            {
                string nodeId = node.userData as string;
                if (string.IsNullOrWhiteSpace(nodeId) == false &&
                    _layerNodeLayerIdById.TryGetValue(nodeId, out string resolvedLayerId) &&
                    string.IsNullOrWhiteSpace(resolvedLayerId) == false)
                {
                    layerId = resolvedLayerId;
                    return true;
                }
            }

            return false;
        }

        public void GetRenderContext(out string layerId, out string scopePath)
        {
            layerId = _activeLayerId ?? string.Empty;
            scopePath = _scopeFilter ?? string.Empty;
        }

        public bool TryGetSelectedScopePath(out string scopePath)
        {
            scopePath = null;
            if (selection == null || selection.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node)
                {
                    string nodeId = node.userData as string;
                    if (string.IsNullOrWhiteSpace(nodeId) == false &&
                        nodeId.StartsWith("__scope__:", StringComparison.Ordinal))
                    {
                        scopePath = nodeId.Substring("__scope__:".Length);
                        return string.IsNullOrWhiteSpace(scopePath) == false;
                    }
                }
            }

            return false;
        }

        public void SetPreviewBackgroundStatus(string status)
        {
            if (_previewBackgroundLabel == null)
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(status))
            {
                _previewBackgroundLabel.style.display = DisplayStyle.None;
                return;
            }

            _previewBackgroundLabel.text = status;
            _previewBackgroundLabel.style.display = DisplayStyle.Flex;
        }

        public void SetPreviewApplyRootMotion(bool applyRootMotion)
        {
            _previewApplyRootMotion = applyRootMotion;
            if (_previewRenderInstance != null)
            {
                ApplyPreviewRootMotion(_previewRenderInstance);
                ApplyPreviewRootMotionPolicy();
            }
        }

        public void GetPreviewCameraState(
            out float orbitYaw,
            out float orbitPitch,
            out float orbitDistanceScale,
            out Vector3 orbitTargetOffset)
        {
            orbitYaw = _previewOrbitYaw;
            orbitPitch = _previewOrbitPitch;
            orbitDistanceScale = _previewOrbitDistanceScale;
            orbitTargetOffset = _previewOrbitTargetOffset;
        }

        public void SetPreviewCameraState(
            float orbitYaw,
            float orbitPitch,
            float orbitDistanceScale,
            Vector3 orbitTargetOffset)
        {
            _previewOrbitYaw = Mathf.Repeat(orbitYaw + 180.0f, 360.0f) - 180.0f;
            _previewOrbitPitch = Mathf.Clamp(orbitPitch, -80.0f, 80.0f);
            _previewOrbitDistanceScale = Mathf.Clamp(orbitDistanceScale, 0.65f, 6.0f);
            _previewOrbitTargetOffset = orbitTargetOffset;
            UpdatePreviewCameraGizmoVisual();
        }

        public void SetPreviewRuntimeMarkers(
            IEnumerable<string> activeStateIds,
            IEnumerable<string> blendStateIds,
            IEnumerable<string> activeLayerIds)
        {
            CopyMarkerSet(_previewActiveStateIds, activeStateIds);
            CopyMarkerSet(_previewBlendStateIds, blendStateIds);
            CopyMarkerSet(_previewActiveLayerIds, activeLayerIds);
            RefreshPreviewRuntimeMarkers();
        }

        public void ClearPreviewRuntimeMarkers()
        {
            _previewActiveStateIds.Clear();
            _previewBlendStateIds.Clear();
            _previewActiveLayerIds.Clear();
            RefreshPreviewRuntimeMarkers();
        }

        public void UpdatePreviewRender(GameObject sourceTarget, AnimationClip clip, float sampleTime)
        {
            if (_previewImage == null || sourceTarget == null || clip == null)
            {
                ClearPreviewRender();
                return;
            }

            if (layout.width < 32.0f || layout.height < 32.0f)
            {
                return;
            }

            EnsurePreviewRenderer();
            EnsurePreviewRenderInstance(sourceTarget);
            EnsurePreviewRenderTexture();
            if (_previewRenderUtility == null || _previewRenderInstance == null || _previewRenderTexture == null)
            {
                return;
            }

            clip.SampleAnimation(_previewRenderInstance, sampleTime);
            ApplyPreviewRootMotionPolicy();
            ConfigurePreviewCamera(_previewRenderInstance);

            _previewRenderUtility.camera.targetTexture = _previewRenderTexture;
            _previewRenderUtility.camera.Render();
            _previewRenderUtility.camera.targetTexture = null;

            _previewImage.image = _previewRenderTexture;
            _previewImage.style.display = DisplayStyle.Flex;
        }

        public void UpdatePreviewRenderWeighted(
            GameObject sourceTarget,
            IList<AnimationClip> clips,
            IList<float> sampleTimes,
            IList<float> sampleWeights)
        {
            if (_previewImage == null || sourceTarget == null || clips == null || sampleTimes == null || sampleWeights == null)
            {
                ClearPreviewRender();
                return;
            }

            if (layout.width < 32.0f || layout.height < 32.0f)
            {
                return;
            }

            EnsurePreviewRenderer();
            EnsurePreviewRenderInstance(sourceTarget);
            EnsurePreviewRenderTexture();
            if (_previewRenderUtility == null || _previewRenderInstance == null || _previewRenderTexture == null)
            {
                return;
            }

            EnsureBlendBuffers();
            if (SampleWeightedLocalPose(clips, sampleTimes, sampleWeights) == false)
            {
                ClearPreviewRender();
                return;
            }

            ApplyPreviewRootMotionPolicy();
            ConfigurePreviewCamera(_previewRenderInstance);

            _previewRenderUtility.camera.targetTexture = _previewRenderTexture;
            _previewRenderUtility.camera.Render();
            _previewRenderUtility.camera.targetTexture = null;

            _previewImage.image = _previewRenderTexture;
            _previewImage.style.display = DisplayStyle.Flex;
        }

        public void UpdatePreviewRenderLayerStack(
            GameObject sourceTarget,
            IList<PreviewLayerPoseInput> layers)
        {
            if (_previewImage == null || sourceTarget == null || layers == null || layers.Count == 0)
            {
                ClearPreviewRender();
                return;
            }

            if (layout.width < 32.0f || layout.height < 32.0f)
            {
                return;
            }

            EnsurePreviewRenderer();
            EnsurePreviewRenderInstance(sourceTarget);
            EnsurePreviewRenderTexture();
            if (_previewRenderUtility == null || _previewRenderInstance == null || _previewRenderTexture == null)
            {
                return;
            }

            EnsureBlendBuffers();
            if (EnsureLayerStackBuffers() == false)
            {
                ClearPreviewRender();
                return;
            }

            _previewAvatarMaskWeights.Clear();
            ApplyStoredBasePose();
            CopyBasePoseToCompositePose();

            bool hasLayerPose = false;
            for (int i = 0; i < layers.Count; ++i)
            {
                PreviewLayerPoseInput layer = layers[i];
                float layerWeight = Mathf.Clamp01(layer.LayerWeight);
                if (layerWeight <= 0.000001f)
                {
                    continue;
                }

                if (layer.Clips == null || layer.SampleTimes == null || layer.SampleWeights == null)
                {
                    continue;
                }

                ApplyStoredBasePose();
                if (SampleWeightedLocalPose(layer.Clips, layer.SampleTimes, layer.SampleWeights) == false)
                {
                    continue;
                }

                CaptureCurrentPoseToLayerPose();
                ComposeLayerPose(layer.Layer, layerWeight, layer.IgnoreAvatarMask);
                hasLayerPose = true;
            }

            if (hasLayerPose == false)
            {
                ClearPreviewRender();
                return;
            }

            ApplyCompositePoseToTransforms();
            ApplyPreviewRootMotionPolicy();
            ConfigurePreviewCamera(_previewRenderInstance);

            _previewRenderUtility.camera.targetTexture = _previewRenderTexture;
            _previewRenderUtility.camera.Render();
            _previewRenderUtility.camera.targetTexture = null;

            _previewImage.image = _previewRenderTexture;
            _previewImage.style.display = DisplayStyle.Flex;
        }

        public void UpdatePreviewRenderBlendedWeighted(
            GameObject sourceTarget,
            IList<AnimationClip> fromClips,
            IList<float> fromSampleTimes,
            IList<float> fromSampleWeights,
            IList<AnimationClip> toClips,
            IList<float> toSampleTimes,
            IList<float> toSampleWeights,
            float blendAlpha)
        {
            if (_previewImage == null || sourceTarget == null)
            {
                ClearPreviewRender();
                return;
            }

            if (layout.width < 32.0f || layout.height < 32.0f)
            {
                return;
            }

            EnsurePreviewRenderer();
            EnsurePreviewRenderInstance(sourceTarget);
            EnsurePreviewRenderTexture();
            if (_previewRenderUtility == null || _previewRenderInstance == null || _previewRenderTexture == null)
            {
                return;
            }

            EnsureBlendBuffers();
            if (_previewBlendTransforms.Count == 0)
            {
                UpdatePreviewRenderWeighted(sourceTarget, toClips, toSampleTimes, toSampleWeights);
                return;
            }

            if (SampleWeightedLocalPose(fromClips, fromSampleTimes, fromSampleWeights) == false)
            {
                UpdatePreviewRenderWeighted(sourceTarget, toClips, toSampleTimes, toSampleWeights);
                return;
            }

            CaptureLocalPose();
            if (SampleWeightedLocalPose(toClips, toSampleTimes, toSampleWeights) == false)
            {
                ApplyPreviewRootMotionPolicy();
                ConfigurePreviewCamera(_previewRenderInstance);
                _previewRenderUtility.camera.targetTexture = _previewRenderTexture;
                _previewRenderUtility.camera.Render();
                _previewRenderUtility.camera.targetTexture = null;
                _previewImage.image = _previewRenderTexture;
                _previewImage.style.display = DisplayStyle.Flex;
                return;
            }

            ApplyLocalPoseBlend(Mathf.Clamp01(blendAlpha));
            ApplyPreviewRootMotionPolicy();
            ConfigurePreviewCamera(_previewRenderInstance);

            _previewRenderUtility.camera.targetTexture = _previewRenderTexture;
            _previewRenderUtility.camera.Render();
            _previewRenderUtility.camera.targetTexture = null;

            _previewImage.image = _previewRenderTexture;
            _previewImage.style.display = DisplayStyle.Flex;
        }

        public void UpdatePreviewRenderBlended(
            GameObject sourceTarget,
            AnimationClip fromClip,
            float fromSampleTime,
            AnimationClip toClip,
            float toSampleTime,
            float blendAlpha)
        {
            if (_previewImage == null || sourceTarget == null || fromClip == null || toClip == null)
            {
                ClearPreviewRender();
                return;
            }

            if (layout.width < 32.0f || layout.height < 32.0f)
            {
                return;
            }

            EnsurePreviewRenderer();
            EnsurePreviewRenderInstance(sourceTarget);
            EnsurePreviewRenderTexture();
            if (_previewRenderUtility == null || _previewRenderInstance == null || _previewRenderTexture == null)
            {
                return;
            }

            EnsureBlendBuffers();
            if (_previewBlendTransforms.Count == 0)
            {
                UpdatePreviewRender(sourceTarget, toClip, toSampleTime);
                return;
            }

            float clampedBlend = Mathf.Clamp01(blendAlpha);

            fromClip.SampleAnimation(_previewRenderInstance, fromSampleTime);
            CaptureLocalPose();
            toClip.SampleAnimation(_previewRenderInstance, toSampleTime);
            ApplyLocalPoseBlend(clampedBlend);

            ApplyPreviewRootMotionPolicy();
            ConfigurePreviewCamera(_previewRenderInstance);

            _previewRenderUtility.camera.targetTexture = _previewRenderTexture;
            _previewRenderUtility.camera.Render();
            _previewRenderUtility.camera.targetTexture = null;

            _previewImage.image = _previewRenderTexture;
            _previewImage.style.display = DisplayStyle.Flex;
        }

        public void ClearPreviewRender()
        {
            if (_previewImage != null)
            {
                _previewImage.image = null;
                _previewImage.style.display = DisplayStyle.None;
            }
        }

        public void DisposePreviewRender()
        {
            ClearPreviewRender();

            if (_previewHdrpVolumeObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewHdrpVolumeObject);
                _previewHdrpVolumeObject = null;
                _previewHdrpVolumeComponent = null;
            }

            if (_previewHdrpVolumeProfile != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewHdrpVolumeProfile);
                _previewHdrpVolumeProfile = null;
            }

            if (_previewRenderInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewRenderInstance);
                _previewRenderInstance = null;
            }

            _previewRenderSource = null;
            _previewRootBindings.Clear();
            _previewBlendTransforms.Clear();
            _previewBlendPositions = null;
            _previewBlendRotations = null;
            _previewBlendScales = null;
            _previewBlendAccumulatedPositions = null;
            _previewBlendAccumulatedRotations = null;
            _previewBlendAccumulatedScales = null;
            _previewBlendSourceInstanceId = 0;
            _previewBlendTransformPaths = null;
            _previewBlendBasePositions = null;
            _previewBlendBaseRotations = null;
            _previewBlendBaseScales = null;
            _previewBlendLayerPositions = null;
            _previewBlendLayerRotations = null;
            _previewBlendLayerScales = null;
            _previewBlendCompositePositions = null;
            _previewBlendCompositeRotations = null;
            _previewBlendCompositeScales = null;
            _previewAvatarMaskWeights.Clear();
            _previewFocusAnchor = Vector3.zero;
            _previewFocusAnchorInitialized = false;
            _previewLastBoundsRadius = 1.0f;

            if (_previewRenderUtility != null)
            {
                _previewRenderUtility.Cleanup();
                _previewRenderUtility = null;
            }

            if (_previewRenderTexture != null)
            {
                _previewRenderTexture.Release();
                UnityEngine.Object.DestroyImmediate(_previewRenderTexture);
                _previewRenderTexture = null;
            }
        }

        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            base.BuildContextualMenu(evt);

            Vector2 contextPosition = ResolveContextMenuContentPosition(evt);
            bool isOverviewScope = string.IsNullOrWhiteSpace(_activeLayerId);
            bool canRenameScopeNode = TryGetSingleSelectedScopeNodePath(out string selectedScopePath);

            evt.menu.AppendSeparator();
            evt.menu.AppendAction("Add State Here", action =>
            {
                AddStateAtPosition(contextPosition);
            }, isOverviewScope ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);

            evt.menu.AppendAction("Create/Sub-State Machine Here", action =>
            {
                AddSubStateMachineAtPosition(contextPosition);
            }, isOverviewScope ? DropdownMenuAction.Status.Disabled : DropdownMenuAction.Status.Normal);

            evt.menu.AppendAction("Add Layer Here", action =>
            {
                AddLayerAtPosition(contextPosition);
            }, isOverviewScope ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            evt.menu.AppendAction("Create/Move Entry Node Here", action =>
            {
                CreateOrMoveSpecialNode(FusionAnimatorGraphAsset.SpecialNodeEntryId, contextPosition);
            }, DropdownMenuAction.Status.Normal);

            evt.menu.AppendAction("Create/Move Any State Node Here", action =>
            {
                CreateOrMoveSpecialNode(FusionAnimatorGraphAsset.SpecialNodeAnyId, contextPosition);
            }, DropdownMenuAction.Status.Normal);

            evt.menu.AppendAction("Create/Move Exit Node Here", action =>
            {
                CreateOrMoveSpecialNode(FusionAnimatorGraphAsset.SpecialNodeExitId, contextPosition);
            }, DropdownMenuAction.Status.Normal);

            bool canCreateTransition = TryGetTwoSelectedStateIds(out string fromStateId, out string toStateId);
            evt.menu.AppendAction("Create Transition (Selected A -> B)", action =>
            {
                CreateTransition(fromStateId, toStateId);
            }, canCreateTransition ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            bool canSetDefaultState = TryGetSingleSelectedStateInCurrentScope(out string defaultStateCandidateId);
            evt.menu.AppendAction("Set as Layer Default State", action =>
            {
                SetStateAsLayerDefault(defaultStateCandidateId);
            }, canSetDefaultState ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            bool canSetDefaultLayer = TryGetSingleSelectedLayerNode(out string defaultLayerId);
            evt.menu.AppendAction("Set as Default Layer", action =>
            {
                SetLayerAsDefault(defaultLayerId);
            }, canSetDefaultLayer ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            evt.menu.AppendAction("Rename Sub-State Machine", action =>
            {
                OnScopeNodeRenameRequested?.Invoke(selectedScopePath);
            }, canRenameScopeNode ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);

            evt.menu.AppendAction("Delete Selected", action =>
            {
                DeleteSelectedElements();
            }, selection != null && selection.Count > 0 ? DropdownMenuAction.Status.Normal : DropdownMenuAction.Status.Disabled);
        }

        private bool TryGetSingleSelectedScopeNodePath(out string scopePath)
        {
            scopePath = null;
            if (selection == null || selection.Count != 1)
            {
                return false;
            }

            if (selection[0] is Node node)
            {
                string nodeId = node.userData as string;
                if (string.IsNullOrWhiteSpace(nodeId) == false &&
                    nodeId.StartsWith("__scope__:", StringComparison.Ordinal))
                {
                    scopePath = nodeId.Substring("__scope__:".Length);
                    return string.IsNullOrWhiteSpace(scopePath) == false;
                }
            }

            return false;
        }

        private Vector2 ResolveContextMenuContentPosition(ContextualMenuPopulateEvent evt)
        {
            if (evt == null)
            {
                return ResolveViewportCenterInContentSpace();
            }

            return this.ChangeCoordinatesTo(contentViewContainer, evt.localMousePosition);
        }

        private void CreateOrMoveSpecialNode(string specialNodeId, Vector2 position)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(specialNodeId))
            {
                return;
            }

            Undo.RecordObject(_graph, "Move FusionAnimator Special Node");
            if (SetSpecialNodePositionForCurrentScope(specialNodeId, position) == false)
            {
                return;
            }

            RebuildFromGraphData();
            OnGraphDirty?.Invoke();
            ApplySearchFilter();
        }

        private void OnMouseDown(MouseDownEvent evt)
        {
            if (evt == null || evt.button != 0)
            {
                return;
            }

            if (evt.shiftKey && evt.ctrlKey == false && evt.commandKey == false)
            {
                GraphElement clickedElement = ResolveShiftAdditiveElement(evt.target as VisualElement);
                if (clickedElement != null)
                {
                    if (selection != null && selection.Contains(clickedElement))
                    {
                        RemoveFromSelection(clickedElement);
                    }
                    else
                    {
                        AddToSelection(clickedElement);
                    }

                    evt.StopPropagation();
                    return;
                }

                // Keep existing selection on shift-click background to mirror ctrl-additive behavior.
                return;
            }

            if (ShouldTreatAsBackgroundClick(evt.target as VisualElement))
            {
                OnBackgroundClicked?.Invoke();
            }
        }

        private GraphElement ResolveShiftAdditiveElement(VisualElement target)
        {
            for (VisualElement current = target; current != null; current = current.parent)
            {
                if (current is Edge edge)
                {
                    return edge;
                }

                if (current is Node node)
                {
                    return node;
                }

                if (current == contentViewContainer || current == this || current is GridBackground || current is MiniMap)
                {
                    break;
                }
            }

            return null;
        }

        private bool ShouldTreatAsBackgroundClick(VisualElement target)
        {
            if (target == null)
            {
                return true;
            }

            for (VisualElement current = target; current != null; current = current.parent)
            {
                if (current is MiniMap)
                {
                    return false;
                }

                if (current == _previewCameraGizmo)
                {
                    return false;
                }

                if (current is GraphElement)
                {
                    return false;
                }

                if (current == contentViewContainer || current is GridBackground || current == this)
                {
                    return true;
                }
            }

            return true;
        }

        public void RebuildFromGraphData()
        {
            _programmaticEdgeRemovalIds.Clear();
            foreach (GraphElement element in graphElements)
            {
                if (element is Edge edge)
                {
                    string edgeId = edge.userData as string;
                    if (string.IsNullOrWhiteSpace(edgeId) == false)
                    {
                        _programmaticEdgeRemovalIds.Add(edgeId);
                    }
                }
            }

            _suppressChangeCallbacks = true;
            DeleteElements(graphElements.ToList());
            _stateViews.Clear();
            _edgeViews.Clear();
            _scopeNodePathById.Clear();
            _layerNodeLayerIdById.Clear();
            _visibleStateIds.Clear();

            if (_graph != null)
            {
                EnsureGraphCollections();
                EnsureActiveContext();
                if (string.IsNullOrWhiteSpace(_activeLayerId))
                {
                    BuildLayerOverview();
                }
                else
                {
                    BuildScopedLayerGraph();
                }
            }

            _suppressChangeCallbacks = false;
            _programmaticEdgeRemovalIds.Clear();
            RefreshTransitionBadges();
            ApplySearchFilter();
        }

        private void EnsureActiveContext()
        {
            if (_graph == null || _graph.Layers == null || _graph.Layers.Count == 0)
            {
                _activeLayerId = string.Empty;
                _scopeFilter = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return;
            }

            bool exists = false;
            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && string.Equals(layer.Id, _activeLayerId, StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }

            if (!exists)
            {
                _activeLayerId = string.Empty;
                _scopeFilter = string.Empty;
            }
        }

        private void BuildLayerOverview()
        {
            if (_graph == null || _graph.Layers == null)
            {
                return;
            }

            List<FusionAnimatorLayerDefinition> layers = _graph.Layers
                .Where(layer => layer != null && string.IsNullOrWhiteSpace(layer.Id) == false)
                .OrderBy(layer => layer.Priority)
                .ToList();

            const float startX = -100.0f;
            const float startY = -40.0f;
            const float stepX = 280.0f;
            const float stepY = 120.0f;
            const int perRow = 4;

            for (int i = 0; i < layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = layers[i];
                int row = i / perRow;
                int col = i % perRow;
                Vector2 pos = new Vector2(startX + col * stepX, startY + row * stepY);
                string nodeId = "__layer__:" + layer.Id;

                StateNodeView nodeView = CreateSpecialNodeView(
                    nodeId,
                    string.IsNullOrWhiteSpace(layer.Name) ? "Layer" : layer.Name,
                    "Layer root. Double-click to enter layer state machine view.",
                    hasInput: false,
                    hasOutput: false,
                    pos,
                    new Color(0.58f, 0.58f, 0.62f, 1.0f));
                if (i == 0)
                {
                    ApplyDefaultOutline(nodeView.Node);
                }

                _stateViews[nodeId] = nodeView;
                _layerNodeLayerIdById[nodeId] = layer.Id;
                RegisterNodeNavigationCallback(nodeView.Node, layer.Id, string.Empty);
                RegisterLayerNodeSelectionCallback(nodeView.Node, layer.Id);
                AddElement(nodeView.Node);
            }
        }

        private void BuildScopedLayerGraph()
        {
            if (_graph == null || string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return;
            }

            List<FusionAnimatorStateDefinition> layerStates = _graph.States
                .Where(state => state != null && string.Equals(state.LayerId, _activeLayerId, StringComparison.Ordinal))
                .ToList();

            List<FusionAnimatorStateDefinition> directStates = new List<FusionAnimatorStateDefinition>();
            HashSet<string> childScopes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < layerStates.Count; ++i)
            {
                FusionAnimatorStateDefinition state = layerStates[i];
                if (state == null)
                {
                    continue;
                }

                if (IsScopeSentinelState(state))
                {
                    string sentinelScopePath = GetStateScopePath(state.Name);
                    string sentinelChildScope = GetDirectChildScope(_scopeFilter, sentinelScopePath);
                    if (string.IsNullOrWhiteSpace(sentinelChildScope) == false)
                    {
                        childScopes.Add(sentinelChildScope);
                    }

                    continue;
                }

                string stateScope = GetStateScopePath(state.Name);
                if (string.Equals(stateScope, _scopeFilter, StringComparison.OrdinalIgnoreCase))
                {
                    directStates.Add(state);
                    continue;
                }

                string childScope = GetDirectChildScope(_scopeFilter, stateScope);
                if (string.IsNullOrWhiteSpace(childScope) == false)
                {
                    childScopes.Add(childScope);
                }
            }

            if (_graph.ScopeUtilityNodeLayouts != null)
            {
                for (int i = 0; i < _graph.ScopeUtilityNodeLayouts.Count; ++i)
                {
                    FusionAnimatorScopeUtilityNodeLayout layout = _graph.ScopeUtilityNodeLayouts[i];
                    if (layout == null || string.Equals(layout.LayerId, _activeLayerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string childScope = GetDirectChildScope(_scopeFilter, NormalizeScopePath(layout.ScopePath));
                    if (string.IsNullOrWhiteSpace(childScope) == false)
                    {
                        childScopes.Add(childScope);
                    }
                }
            }

            Vector2 scopeAnchor = ResolveScopedAnchor(directStates, layerStates);
            Vector2 entryPos = ResolveSpecialNodePositionForCurrentScope(FusionAnimatorGraphAsset.SpecialNodeEntryId, scopeAnchor);
            Vector2 anyPos = ResolveSpecialNodePositionForCurrentScope(FusionAnimatorGraphAsset.SpecialNodeAnyId, scopeAnchor);
            Vector2 exitPos = ResolveSpecialNodePositionForCurrentScope(FusionAnimatorGraphAsset.SpecialNodeExitId, scopeAnchor);

            StateNodeView entryView = CreateSpecialNodeView(
                FusionAnimatorGraphAsset.SpecialNodeEntryId,
                "Entry",
                "Scope entry point. Incoming transition designates default state for this scope.",
                hasInput: false,
                hasOutput: true,
                entryPos,
                new Color(0.27f, 0.78f, 0.36f, 1.0f));
            _stateViews[FusionAnimatorGraphAsset.SpecialNodeEntryId] = entryView;
            AddElement(entryView.Node);

            StateNodeView anyView = CreateSpecialNodeView(
                FusionAnimatorGraphAsset.SpecialNodeAnyId,
                "Any State",
                "Transitions that can fire from any active state in this scope.",
                hasInput: false,
                hasOutput: true,
                anyPos,
                new Color(0.29f, 0.63f, 0.95f, 1.0f));
            _stateViews[FusionAnimatorGraphAsset.SpecialNodeAnyId] = anyView;
            AddElement(anyView.Node);

            StateNodeView exitView = CreateSpecialNodeView(
                FusionAnimatorGraphAsset.SpecialNodeExitId,
                "Exit",
                "Scope terminal destination.",
                hasInput: true,
                hasOutput: false,
                exitPos,
                new Color(0.92f, 0.45f, 0.45f, 1.0f));
            _stateViews[FusionAnimatorGraphAsset.SpecialNodeExitId] = exitView;
            AddElement(exitView.Node);

            string currentScopeDefaultStateId = null;
            bool hasCurrentScopeDefault = TryGetScopeDefaultStateId(_activeLayerId, _scopeFilter, out currentScopeDefaultStateId);

            foreach (FusionAnimatorStateDefinition state in directStates)
            {
                bool isDefault = hasCurrentScopeDefault &&
                                 string.Equals(currentScopeDefaultStateId, state.Id, StringComparison.Ordinal);
                StateNodeView view = CreateStateNodeView(state, isDefault);
                _stateViews[state.Id] = view;
                _visibleStateIds.Add(state.Id);
                AddElement(view.Node);
            }

            List<string> sortedChildScopes = childScopes.OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToList();
            for (int i = 0; i < sortedChildScopes.Count; ++i)
            {
                string childScopePath = sortedChildScopes[i];
                Vector2 childPos = ResolveScopeNodePosition(childScopePath, layerStates, scopeAnchor, i);
                string scopeNodeId = "__scope__:" + childScopePath;
                string scopeLeaf = GetScopeLeafName(childScopePath);

                StateNodeView scopeView = CreateSpecialNodeView(
                    scopeNodeId,
                    string.IsNullOrWhiteSpace(scopeLeaf) ? "Sub-State" : scopeLeaf,
                    string.Format("Sub-state machine: {0}\nDouble-click to enter.", childScopePath),
                    hasInput: true,
                    hasOutput: true,
                    childPos,
                    new Color(0.52f, 0.52f, 0.62f, 1.0f));
                if (hasCurrentScopeDefault &&
                    IsDefaultScopeNodeForCurrentScope(currentScopeDefaultStateId, childScopePath))
                {
                    ApplyDefaultOutline(scopeView.Node);
                }

                _stateViews[scopeNodeId] = scopeView;
                _scopeNodePathById[scopeNodeId] = childScopePath;
                RegisterNodeNavigationCallback(scopeView.Node, _activeLayerId, childScopePath);
                AddElement(scopeView.Node);
            }

            for (int i = 0, count = _graph.Transitions.Count; i < count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null || string.IsNullOrWhiteSpace(transition.Id))
                {
                    continue;
                }

                if (IsTransitionSuppressedInCurrentScope(transition.Id))
                {
                    continue;
                }

                if (TryResolveTransitionEndpointsForCurrentScope(transition, out StateNodeView fromView, out StateNodeView toView) == false)
                {
                    continue;
                }

                AddTransitionEdge(transition.Id, transition, fromView, toView);
            }

            if (_stateViews.TryGetValue(FusionAnimatorGraphAsset.SpecialNodeEntryId, out StateNodeView entryNodeView) &&
                TryGetScopeDefaultStateId(_activeLayerId, _scopeFilter, out string scopeDefaultStateId))
            {
                bool hasExplicitEntry = false;
                for (int i = 0, count = _graph.Transitions.Count; i < count; ++i)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                    if (transition == null)
                    {
                        continue;
                    }

                    if (string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) &&
                        string.Equals(transition.ToStateId, scopeDefaultStateId, StringComparison.Ordinal))
                    {
                        hasExplicitEntry = true;
                        break;
                    }
                }

                if (hasExplicitEntry == false)
                {
                    StateNodeView defaultTargetView = null;
                    if (_stateViews.TryGetValue(scopeDefaultStateId, out StateNodeView defaultStateView))
                    {
                        defaultTargetView = defaultStateView;
                    }
                    else
                    {
                        FusionAnimatorStateDefinition defaultState = FindState(scopeDefaultStateId);
                        if (defaultState != null)
                        {
                            string defaultStateScope = GetStateScopePath(defaultState.Name);
                            string childScope = GetDirectChildScope(_scopeFilter, defaultStateScope);
                            if (string.IsNullOrWhiteSpace(childScope) == false)
                            {
                                string scopeNodeId = "__scope__:" + childScope;
                                _stateViews.TryGetValue(scopeNodeId, out defaultTargetView);
                            }
                        }
                    }

                    if (defaultTargetView != null)
                    {
                        AddTransitionEdge(EntryLinkEdgeId, null, entryNodeView, defaultTargetView);
                    }
                }
            }
        }

        private void RegisterNodeNavigationCallback(Node node, string layerId, string scopePath)
        {
            if (node == null)
            {
                return;
            }

            node.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt == null || evt.button != 0 || evt.clickCount != 2)
                {
                    return;
                }

                ClearSelection();
                OnSelectionChanged?.Invoke(null, null);
                _activeLayerId = string.IsNullOrWhiteSpace(layerId) ? string.Empty : layerId;
                _scopeFilter = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath;
                RebuildFromGraphData();
                OnScopeChanged?.Invoke(_activeLayerId, _scopeFilter);
                evt.StopPropagation();
            });
        }

        private void RegisterLayerNodeSelectionCallback(Node node, string layerId)
        {
            if (node == null || string.IsNullOrWhiteSpace(layerId))
            {
                return;
            }

            node.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt == null || evt.button != 0 || evt.clickCount != 1)
                {
                    return;
                }

                OnLayerNodeSelected?.Invoke(layerId);
            });
        }

        private bool TryResolveTransitionEndpointsForCurrentScope(
            FusionAnimatorTransitionDefinition transition,
            out StateNodeView fromView,
            out StateNodeView toView)
        {
            fromView = null;
            toView = null;

            if (transition == null)
            {
                return false;
            }

            if (TryGetTransitionUtilityScopePath(transition, out string transitionScopePath))
            {
                string currentScope = NormalizeScopePath(_scopeFilter);
                if (string.Equals(transitionScopePath, currentScope, StringComparison.OrdinalIgnoreCase) == false)
                {
                    return false;
                }
            }

            if (TryResolveEndpointNodeForCurrentScope(transition.FromStateId, true, out fromView, out string fromNodeId) == false)
            {
                return false;
            }

            if (TryResolveEndpointNodeForCurrentScope(transition.ToStateId, false, out toView, out string toNodeId) == false)
            {
                return false;
            }

            if (string.Equals(fromNodeId, toNodeId, StringComparison.Ordinal))
            {
                return false;
            }

            return true;
        }

        private bool TryGetSingleSelectedStateInCurrentScope(out string stateId)
        {
            stateId = null;
            if (selection == null || selection.Count == 0 || string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return false;
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node)
                {
                    string candidate = node.userData as string;
                    FusionAnimatorStateDefinition state = FindState(candidate);
                    if (state == null || _visibleStateIds.Contains(state.Id) == false)
                    {
                        continue;
                    }

                    stateId = state.Id;
                    return true;
                }
            }

            return false;
        }

        private bool TryGetSingleSelectedLayerNode(out string layerId)
        {
            layerId = null;
            if (selection == null || selection.Count != 1)
            {
                return false;
            }

            if (selection[0] is Node node)
            {
                string nodeId = node.userData as string;
                if (string.IsNullOrWhiteSpace(nodeId) == false &&
                    _layerNodeLayerIdById.TryGetValue(nodeId, out string selectedLayerId) &&
                    string.IsNullOrWhiteSpace(selectedLayerId) == false)
                {
                    layerId = selectedLayerId;
                    return true;
                }
            }

            return false;
        }

        private bool TryResolveEndpointNodeForCurrentScope(string stateId, bool isSource, out StateNodeView view, out string nodeId)
        {
            view = null;
            nodeId = null;
            if (string.IsNullOrWhiteSpace(stateId))
            {
                return false;
            }

            if (_stateViews.TryGetValue(stateId, out view))
            {
                nodeId = stateId;
                return isSource ? view.Output != null : view.Input != null;
            }

            FusionAnimatorStateDefinition state = FindState(stateId);
            if (state == null || string.Equals(state.LayerId, _activeLayerId, StringComparison.Ordinal) == false)
            {
                return false;
            }

            string stateScope = GetStateScopePath(state.Name);
            if (string.Equals(stateScope, _scopeFilter, StringComparison.OrdinalIgnoreCase))
            {
                if (_stateViews.TryGetValue(state.Id, out view))
                {
                    nodeId = state.Id;
                    return isSource ? view.Output != null : view.Input != null;
                }

                return false;
            }

            string childScope = GetDirectChildScope(_scopeFilter, stateScope);
            if (string.IsNullOrWhiteSpace(childScope))
            {
                return false;
            }

            string scopeNodeId = "__scope__:" + childScope;
            if (_stateViews.TryGetValue(scopeNodeId, out view))
            {
                nodeId = scopeNodeId;
                return isSource ? view.Output != null : view.Input != null;
            }

            return false;
        }

        private void SetStateAsLayerDefault(string stateId)
        {
            FusionAnimatorStateDefinition targetState = FindState(stateId);
            if (targetState == null || string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return;
            }

            Undo.RecordObject(_graph, "Set FusionAnimator Layer Default State");
            if (SetScopeDefaultStateInternal(targetState) == false)
            {
                return;
            }

            RebuildFromGraphData();
            OnGraphDirty?.Invoke();
        }

        private void SetLayerAsDefault(string layerId)
        {
            if (_graph == null || _graph.Layers == null || string.IsNullOrWhiteSpace(layerId))
            {
                return;
            }

            int sourceIndex = -1;
            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && string.Equals(layer.Id, layerId, StringComparison.Ordinal))
                {
                    sourceIndex = i;
                    break;
                }
            }

            if (sourceIndex <= 0)
            {
                return;
            }

            Undo.RecordObject(_graph, "Set FusionAnimator Default Layer");
            FusionAnimatorLayerDefinition movedLayer = _graph.Layers[sourceIndex];
            _graph.Layers.RemoveAt(sourceIndex);
            _graph.Layers.Insert(0, movedLayer);

            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null)
                {
                    layer.Priority = i;
                }
            }

            RebuildFromGraphData();
            OnGraphDirty?.Invoke();
            OnLayerNodeSelected?.Invoke(layerId);
        }

        private bool SetScopeDefaultStateInternal(FusionAnimatorStateDefinition targetState)
        {
            if (_graph == null || targetState == null || string.IsNullOrWhiteSpace(targetState.LayerId))
            {
                return false;
            }

            string layerId = targetState.LayerId;
            string targetScope = GetStateScopePath(targetState.Name);
            bool changed = false;
            for (int i = _graph.Transitions.Count - 1; i >= 0; --i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null || string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                FusionAnimatorStateDefinition destinationState = FindState(transition.ToStateId);
                if (destinationState == null)
                {
                    continue;
                }

                if (string.Equals(destinationState.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string destinationScope = GetStateScopePath(destinationState.Name);
                if (string.Equals(destinationScope, targetScope, StringComparison.OrdinalIgnoreCase))
                {
                    _graph.Transitions.RemoveAt(i);
                    changed = true;
                }
            }

            _graph.Transitions.Add(new FusionAnimatorTransitionDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("transition"),
                Name = "Entry",
                FromStateId = FusionAnimatorGraphAsset.SpecialNodeEntryId,
                ToStateId = targetState.Id,
                Priority = 0,
                HasExitTime = false,
                FixedDuration = true,
                BlendDurationSeconds = 0.0f,
                Conditions = new List<FusionAnimatorConditionDefinition>(),
            });
            changed = true;

            if (string.IsNullOrWhiteSpace(targetScope) && string.Equals(_graph.EntryStateId, targetState.Id, StringComparison.Ordinal) == false)
            {
                _graph.EntryStateId = targetState.Id;
                changed = true;
            }

            return changed;
        }

        private bool IsDefaultStateForCurrentScope(FusionAnimatorStateDefinition state)
        {
            if (state == null || string.IsNullOrWhiteSpace(_activeLayerId) || _graph == null || _graph.Transitions == null)
            {
                return false;
            }

            if (string.Equals(state.LayerId, _activeLayerId, StringComparison.Ordinal) == false)
            {
                return false;
            }

            string stateScope = GetStateScopePath(state.Name);
            if (string.Equals(stateScope, _scopeFilter, StringComparison.OrdinalIgnoreCase) == false)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_scopeFilter))
            {
                return string.Equals(_graph.EntryStateId, state.Id, StringComparison.Ordinal);
            }

            for (int i = 0; i < _graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null)
                {
                    continue;
                }

                if (string.Equals(transition.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) &&
                    string.Equals(transition.ToStateId, state.Id, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsDefaultScopeNodeForCurrentScope(string defaultStateId, string childScopePath)
        {
            if (string.IsNullOrWhiteSpace(defaultStateId) || string.IsNullOrWhiteSpace(childScopePath))
            {
                return false;
            }

            FusionAnimatorStateDefinition defaultState = FindState(defaultStateId);
            if (defaultState == null)
            {
                return false;
            }

            string defaultScope = GetStateScopePath(defaultState.Name);
            if (string.Equals(defaultScope, _scopeFilter, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string directChildScope = GetDirectChildScope(_scopeFilter, defaultScope);
            return string.Equals(directChildScope, childScopePath, StringComparison.OrdinalIgnoreCase);
        }

        private Vector2 ResolveScopedAnchor(List<FusionAnimatorStateDefinition> directStates, List<FusionAnimatorStateDefinition> layerStates)
        {
            List<FusionAnimatorStateDefinition> source = directStates != null && directStates.Count > 0 ? directStates : layerStates;
            if (source == null || source.Count == 0)
            {
                return Vector2.zero;
            }

            bool hasAny = false;
            float minX = 0.0f;
            float minY = 0.0f;
            float maxY = 0.0f;
            for (int i = 0; i < source.Count; ++i)
            {
                FusionAnimatorStateDefinition state = source[i];
                if (state == null)
                {
                    continue;
                }

                Vector2 p = state.NodePosition;
                if (!hasAny)
                {
                    minX = p.x;
                    minY = p.y;
                    maxY = p.y;
                    hasAny = true;
                }
                else
                {
                    minX = Mathf.Min(minX, p.x);
                    minY = Mathf.Min(minY, p.y);
                    maxY = Mathf.Max(maxY, p.y);
                }
            }

            return hasAny ? new Vector2(minX, (minY + maxY) * 0.5f) : Vector2.zero;
        }

        private static bool IsPositionNearlyEqual(Vector2 a, Vector2 b, float epsilon = 0.01f)
        {
            return Mathf.Abs(a.x - b.x) <= epsilon && Mathf.Abs(a.y - b.y) <= epsilon;
        }

        private Vector2 ResolveScopeNodePosition(string childScopePath, List<FusionAnimatorStateDefinition> layerStates, Vector2 anchor, int childIndex)
        {
            if (string.IsNullOrWhiteSpace(_activeLayerId) == false &&
                TryGetScopeUtilityNodeLayout(_activeLayerId, childScopePath, out FusionAnimatorScopeUtilityNodeLayout layout) &&
                layout != null &&
                layout.HasScopeNodePosition)
            {
                return layout.ScopeNodePosition;
            }

            if (layerStates != null && layerStates.Count > 0)
            {
                float sumX = 0.0f;
                float sumY = 0.0f;
                int count = 0;
                for (int i = 0; i < layerStates.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = layerStates[i];
                    if (state == null)
                    {
                        continue;
                    }

                    if (IsScopeSentinelState(state))
                    {
                        continue;
                    }

                    string scope = GetStateScopePath(state.Name);
                    if (string.Equals(scope, childScopePath, StringComparison.OrdinalIgnoreCase) == false &&
                        (scope.StartsWith(childScopePath + "/", StringComparison.OrdinalIgnoreCase) == false))
                    {
                        continue;
                    }

                    sumX += state.NodePosition.x;
                    sumY += state.NodePosition.y;
                    count++;
                }

                if (count > 0)
                {
                    return new Vector2(sumX / count - 140.0f, sumY / count);
                }
            }

            return anchor + new Vector2(120.0f * childIndex, 180.0f);
        }

        private static string GetDirectChildScope(string currentScope, string stateScope)
        {
            if (string.IsNullOrWhiteSpace(stateScope))
            {
                return string.Empty;
            }

            if (string.IsNullOrWhiteSpace(currentScope))
            {
                int firstSlash = stateScope.IndexOf('/');
                return firstSlash >= 0 ? stateScope.Substring(0, firstSlash) : stateScope;
            }

            if (string.Equals(stateScope, currentScope, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (stateScope.StartsWith(currentScope + "/", StringComparison.OrdinalIgnoreCase) == false)
            {
                return string.Empty;
            }

            string remainder = stateScope.Substring(currentScope.Length + 1);
            int slash = remainder.IndexOf('/');
            return slash >= 0 ? (currentScope + "/" + remainder.Substring(0, slash)) : stateScope;
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

        private static string GetScopeLeafName(string scopePath)
        {
            if (string.IsNullOrWhiteSpace(scopePath))
            {
                return string.Empty;
            }

            int separator = scopePath.LastIndexOf('/');
            return separator >= 0 ? scopePath.Substring(separator + 1) : scopePath;
        }

        private static string GetStateLeafName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return string.Empty;
            }

            int separator = stateName.LastIndexOf('/');
            return separator >= 0 ? stateName.Substring(separator + 1) : stateName;
        }

        private static bool IsScopeNodeId(string nodeId)
        {
            return string.IsNullOrWhiteSpace(nodeId) == false &&
                   nodeId.StartsWith("__scope__:", StringComparison.Ordinal);
        }

        private static bool IsScopeSentinelState(FusionAnimatorStateDefinition state)
        {
            return state != null && IsScopeSentinelStateName(state.Name);
        }

        private static bool IsScopeSentinelStateName(string stateName)
        {
            return string.Equals(
                GetStateLeafName(stateName),
                FusionAnimatorGraphAsset.ScopeSentinelStateLeafName,
                StringComparison.Ordinal);
        }

        private bool TryGetTransitionUtilityScopePath(FusionAnimatorTransitionDefinition transition, out string scopePath)
        {
            scopePath = string.Empty;
            if (transition == null)
            {
                return false;
            }

            bool fromSpecial = IsSpecialTransitionEndpoint(transition.FromStateId);
            bool toSpecial = IsSpecialTransitionEndpoint(transition.ToStateId);
            if (fromSpecial == false && toSpecial == false)
            {
                return false;
            }

            if (fromSpecial && toSpecial)
            {
                scopePath = NormalizeScopePath(_scopeFilter);
                return true;
            }

            string stateEndpointId = fromSpecial ? transition.ToStateId : transition.FromStateId;
            FusionAnimatorStateDefinition stateEndpoint = FindState(stateEndpointId);
            if (stateEndpoint == null)
            {
                return false;
            }

            string stateScope = NormalizeScopePath(GetStateScopePath(stateEndpoint.Name));
            if (IsScopeSentinelState(stateEndpoint))
            {
                scopePath = GetParentScopePath(stateScope);
                return true;
            }

            scopePath = stateScope;
            return true;
        }

        private static string BuildScopeSentinelStateName(string scopePath)
        {
            string normalizedScope = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                return FusionAnimatorGraphAsset.ScopeSentinelStateLeafName;
            }

            return normalizedScope + "/" + FusionAnimatorGraphAsset.ScopeSentinelStateLeafName;
        }

        private FusionAnimatorStateDefinition GetOrCreateScopeSentinelState(string layerId, string scopePath)
        {
            FusionAnimatorStateDefinition existing = FindScopeSentinelState(layerId, scopePath);
            if (existing != null)
            {
                return existing;
            }

            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                return null;
            }

            FusionAnimatorStateDefinition created = new FusionAnimatorStateDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("state"),
                Name = BuildScopeSentinelStateName(normalizedScope),
                LayerId = layerId,
                CanTransitionOut = true,
                NodePosition = ResolveDefaultSpecialNodePosition(FusionAnimatorGraphAsset.SpecialNodeExitId, Vector2.zero),
            };
            _graph.States.Add(created);
            return created;
        }

        private FusionAnimatorStateDefinition FindScopeSentinelState(string layerId, string scopePath)
        {
            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                return null;
            }

            string sentinelName = BuildScopeSentinelStateName(normalizedScope);
            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                if (string.Equals(state.Name, sentinelName, StringComparison.OrdinalIgnoreCase))
                {
                    return state;
                }
            }

            return null;
        }

        private bool IsTransitionSuppressedInCurrentScope(string transitionId)
        {
            return IsTransitionSuppressedInScope(transitionId, _activeLayerId, _scopeFilter);
        }

        private bool IsTransitionSuppressedInScope(string transitionId, string layerId, string scopePath)
        {
            if (_graph?.ScopeTransitionSuppressions == null || string.IsNullOrWhiteSpace(transitionId) || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            for (int i = 0; i < _graph.ScopeTransitionSuppressions.Count; ++i)
            {
                FusionAnimatorScopeTransitionSuppression suppression = _graph.ScopeTransitionSuppressions[i];
                if (suppression == null ||
                    string.Equals(suppression.TransitionId, transitionId, StringComparison.Ordinal) == false ||
                    string.Equals(suppression.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string suppressedScope = NormalizeScopePath(suppression.ScopePath);
                if (string.Equals(suppressedScope, normalizedScope, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private bool AddTransitionSuppressionForCurrentScope(string transitionId)
        {
            if (string.IsNullOrWhiteSpace(transitionId) || string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return false;
            }

            if (_graph.ScopeTransitionSuppressions == null)
            {
                _graph.ScopeTransitionSuppressions = new List<FusionAnimatorScopeTransitionSuppression>();
            }

            if (IsTransitionSuppressedInCurrentScope(transitionId))
            {
                return false;
            }

            _graph.ScopeTransitionSuppressions.Add(new FusionAnimatorScopeTransitionSuppression
            {
                TransitionId = transitionId,
                LayerId = _activeLayerId,
                ScopePath = NormalizeScopePath(_scopeFilter),
            });
            return true;
        }

        private bool RemoveTransitionSuppressionForCurrentScope(string transitionId)
        {
            if (_graph?.ScopeTransitionSuppressions == null || string.IsNullOrWhiteSpace(transitionId) || string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return false;
            }

            string normalizedScope = NormalizeScopePath(_scopeFilter);
            int before = _graph.ScopeTransitionSuppressions.Count;
            _graph.ScopeTransitionSuppressions.RemoveAll(suppression =>
                suppression == null ||
                (string.Equals(suppression.TransitionId, transitionId, StringComparison.Ordinal) &&
                 string.Equals(suppression.LayerId, _activeLayerId, StringComparison.Ordinal) &&
                 string.Equals(NormalizeScopePath(suppression.ScopePath), normalizedScope, StringComparison.OrdinalIgnoreCase)));
            return _graph.ScopeTransitionSuppressions.Count != before;
        }

        public void AddStateAtViewportCenter()
        {
            if (_graph == null)
            {
                return;
            }

            Vector2 center = ResolveViewportCenterInContentSpace();
            AddStateAtPosition(center);
        }

        private void AddStateAtPosition(Vector2 position)
        {
            if (_graph == null)
            {
                return;
            }

            EnsureGraphCollections();
            string layerId = GetDefaultLayerIdForCreation();
            if (string.IsNullOrWhiteSpace(layerId))
            {
                return;
            }

            Undo.RecordObject(_graph, "Add FusionAnimator State");
            string stateName = BuildUniqueStateName(layerId, "State", _scopeFilter);
            FusionAnimatorStateDefinition state = new FusionAnimatorStateDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("state"),
                Name = stateName,
                NodePosition = position,
                LayerId = layerId,
            };

            _graph.States.Add(state);

            _suppressChangeCallbacks = true;
            StateNodeView view = CreateStateNodeView(state);
            _stateViews[state.Id] = view;
            AddElement(view.Node);
            _suppressChangeCallbacks = false;

            OnGraphDirty?.Invoke();
            ApplySearchFilter();
        }

        private void AddLayerAtPosition(Vector2 position)
        {
            _ = position;
            if (_graph == null)
            {
                return;
            }

            EnsureGraphCollections();
            Undo.RecordObject(_graph, "Add FusionAnimator Layer");

            const string baseName = "Layer";
            string candidateName = baseName;
            int suffix = 1;
            while (_graph.Layers.Any(layer => layer != null && string.Equals(layer.Name, candidateName, StringComparison.OrdinalIgnoreCase)))
            {
                suffix++;
                candidateName = string.Format("{0} {1}", baseName, suffix);
            }

            FusionAnimatorLayerDefinition layerDefinition = new FusionAnimatorLayerDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("layer"),
                Name = candidateName,
                DefaultWeight = 1.0f,
                Priority = _graph.Layers.Count,
            };

            _graph.Layers.Add(layerDefinition);
            RebuildFromGraphData();
            OnLayerNodeSelected?.Invoke(layerDefinition.Id);
            OnGraphDirty?.Invoke();
        }

        private void AddSubStateMachineAtPosition(Vector2 position)
        {
            if (_graph == null)
            {
                return;
            }

            EnsureGraphCollections();
            string layerId = GetDefaultLayerIdForCreation();
            if (string.IsNullOrWhiteSpace(layerId))
            {
                return;
            }

            string childScopePath = BuildUniqueChildScopePath(layerId, _scopeFilter, "SubStateMachine");
            string stateName = BuildUniqueStateName(layerId, "State", childScopePath);

            Undo.RecordObject(_graph, "Add FusionAnimator Sub-State Machine");
            GetOrCreateScopeSentinelState(layerId, childScopePath);
            SetScopeNodePosition(layerId, childScopePath, position);
            FusionAnimatorStateDefinition state = new FusionAnimatorStateDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("state"),
                Name = stateName,
                LayerId = layerId,
                NodePosition = position + new Vector2(120.0f, 0.0f),
            };

            _graph.States.Add(state);
            SetScopeDefaultStateInternal(state);

            RebuildFromGraphData();
            OnGraphDirty?.Invoke();
        }

        private string GetDefaultLayerIdForCreation()
        {
            if (string.IsNullOrWhiteSpace(_activeLayerId) == false)
            {
                return _activeLayerId;
            }

            if (_graph == null || _graph.Layers == null)
            {
                return string.Empty;
            }

            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && string.IsNullOrWhiteSpace(layer.Id) == false)
                {
                    return layer.Id;
                }
            }

            return string.Empty;
        }

        private string BuildUniqueChildScopePath(string layerId, string parentScope, string baseName)
        {
            string normalizedParent = string.IsNullOrWhiteSpace(parentScope) ? string.Empty : parentScope.Trim().Trim('/');
            string seed = string.IsNullOrWhiteSpace(baseName) ? "SubStateMachine" : baseName.Trim();
            int suffix = 1;
            while (suffix < 1000)
            {
                string leaf = suffix == 1 ? seed : string.Format("{0} {1}", seed, suffix);
                string scopePath = string.IsNullOrWhiteSpace(normalizedParent) ? leaf : normalizedParent + "/" + leaf;

                bool exists = false;
                for (int i = 0; i < _graph.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string stateScope = GetStateScopePath(state.Name);
                    if (string.Equals(stateScope, scopePath, StringComparison.OrdinalIgnoreCase) ||
                        stateScope.StartsWith(scopePath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists == false)
                {
                    return scopePath;
                }

                suffix++;
            }

            return string.IsNullOrWhiteSpace(normalizedParent) ? seed : normalizedParent + "/" + seed;
        }

        private string BuildUniqueStateName(string layerId, string leafName, string scopePath)
        {
            string normalizedScope = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath.Trim().Trim('/');
            string baseLeaf = string.IsNullOrWhiteSpace(leafName) ? "State" : leafName.Trim();
            int suffix = 1;
            while (suffix < 1000)
            {
                string candidateLeaf = suffix == 1 ? baseLeaf : string.Format("{0} {1}", baseLeaf, suffix);
                string candidateName = string.IsNullOrWhiteSpace(normalizedScope) ? candidateLeaf : normalizedScope + "/" + candidateLeaf;
                bool exists = false;
                for (int i = 0; i < _graph.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    if (string.Equals(state.Name, candidateName, StringComparison.OrdinalIgnoreCase))
                    {
                        exists = true;
                        break;
                    }
                }

                if (exists == false)
                {
                    return candidateName;
                }

                suffix++;
            }

            return string.IsNullOrWhiteSpace(normalizedScope) ? baseLeaf : normalizedScope + "/" + baseLeaf;
        }

        public void RefreshNodeDisplay(FusionAnimatorStateDefinition state)
        {
            if (state == null || string.IsNullOrWhiteSpace(state.Id))
            {
                return;
            }

            if (_stateViews.TryGetValue(state.Id, out StateNodeView view) == false)
            {
                return;
            }

            view.Node.title = string.IsNullOrWhiteSpace(state.Name) ? "State" : state.Name;
            view.LayerLabel.text = GetLayerDisplayName(state.LayerId);
            if (view.MotionLabel != null)
            {
                view.MotionLabel.text = GetMotionDisplayName(state);
            }
            if (view.BlendTreeSummary != null)
            {
                RefreshBlendTreeSummary(view.BlendTreeSummary, state);
            }
            view.Node.tooltip = string.Format("State: {0}\nLayer: {1}\nMotion: {2}\nCan Transition Out: {3}", state.Name, GetLayerDisplayName(state.LayerId), state.MotionType, state.CanTransitionOut);

            Rect currentRect = view.Node.GetPosition();
            if (currentRect.position != state.NodePosition)
            {
                view.Node.SetPosition(new Rect(state.NodePosition, currentRect.size));
            }

            float targetHeight = state.MotionType == FusionAnimatorMotionType.BlendTree ? 180.0f : 130.0f;
            if (Mathf.Approximately(currentRect.height, targetHeight) == false)
            {
                view.Node.SetPosition(new Rect(view.Node.GetPosition().position, new Vector2(currentRect.width, targetHeight)));
            }
        }

        public void RefreshEdgeForTransition(FusionAnimatorTransitionDefinition transition)
        {
            if (transition == null || string.IsNullOrWhiteSpace(transition.Id) || _graph == null)
            {
                return;
            }

            Edge existingEdge = null;
            foreach (GraphElement element in graphElements)
            {
                if (element is Edge edge && string.Equals(edge.userData as string, transition.Id, StringComparison.Ordinal))
                {
                    existingEdge = edge;
                    break;
                }
            }

            if (existingEdge != null)
            {
                ConfigureEdgeVisual(existingEdge, transition);
                _edgeViews[transition.Id] = existingEdge;
                RefreshTransitionBadges();
                ApplySearchFilter();
                return;
            }

            if (_stateViews.TryGetValue(transition.FromStateId, out StateNodeView fromView) == false ||
                _stateViews.TryGetValue(transition.ToStateId, out StateNodeView toView) == false ||
                fromView.Output == null ||
                toView.Input == null)
            {
                RefreshTransitionBadges();
                ApplySearchFilter();
                return;
            }

            Edge replacement = fromView.Output.ConnectTo(toView.Input);
            replacement.userData = transition.Id;
            ConfigureEdgeVisual(replacement, transition);
            AddElement(replacement);
            _edgeViews[transition.Id] = replacement;
            RefreshTransitionBadges();
            ApplySearchFilter();
        }

        public bool TryGetCurrentSelection(out FusionAnimatorStateDefinition state, out FusionAnimatorTransitionDefinition transition)
        {
            state = null;
            transition = null;

            if (_graph == null || selection == null || selection.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Edge edge)
                {
                    transition = FindTransitionById(edge.userData as string);
                    return transition != null;
                }
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node)
                {
                    state = FindState(node.userData as string);
                    if (state != null)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        public int GetSelectionCount()
        {
            return selection != null ? selection.Count : 0;
        }

        public bool TryGetSelectedLayerIds(List<string> layerIds)
        {
            if (layerIds == null)
            {
                return false;
            }

            layerIds.Clear();
            if (_graph == null || selection == null || selection.Count == 0)
            {
                return false;
            }

            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Edge edge)
                {
                    FusionAnimatorTransitionDefinition transition = FindTransitionById(edge.userData as string);
                    if (transition == null)
                    {
                        continue;
                    }

                    FusionAnimatorStateDefinition fromState = FindState(transition.FromStateId);
                    FusionAnimatorStateDefinition toState = FindState(transition.ToStateId);
                    string transitionLayerId = fromState != null ? fromState.LayerId : (toState != null ? toState.LayerId : string.Empty);
                    if (string.IsNullOrWhiteSpace(transitionLayerId) == false && unique.Add(transitionLayerId))
                    {
                        layerIds.Add(transitionLayerId);
                    }

                    continue;
                }

                if (selection[i] is Node node)
                {
                    string nodeId = node.userData as string;
                    if (string.IsNullOrWhiteSpace(nodeId))
                    {
                        continue;
                    }

                    if (_layerNodeLayerIdById.TryGetValue(nodeId, out string layerNodeLayerId))
                    {
                        if (string.IsNullOrWhiteSpace(layerNodeLayerId) == false && unique.Add(layerNodeLayerId))
                        {
                            layerIds.Add(layerNodeLayerId);
                        }

                        continue;
                    }

                    if (nodeId.StartsWith("__scope__:", StringComparison.Ordinal))
                    {
                        if (string.IsNullOrWhiteSpace(_activeLayerId) == false && unique.Add(_activeLayerId))
                        {
                            layerIds.Add(_activeLayerId);
                        }

                        continue;
                    }

                    FusionAnimatorStateDefinition state = FindState(nodeId);
                    if (state != null &&
                        string.IsNullOrWhiteSpace(state.LayerId) == false &&
                        unique.Add(state.LayerId))
                    {
                        layerIds.Add(state.LayerId);
                    }
                }
            }

            return layerIds.Count > 0;
        }

        public bool TryGetSelectedStateIds(List<string> stateIds)
        {
            if (stateIds == null)
            {
                return false;
            }

            stateIds.Clear();
            if (_graph == null || selection == null || selection.Count == 0)
            {
                return false;
            }

            HashSet<string> unique = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node == false)
                {
                    continue;
                }

                string nodeId = node.userData as string;
                if (string.IsNullOrWhiteSpace(nodeId))
                {
                    continue;
                }

                FusionAnimatorStateDefinition state = FindState(nodeId);
                if (state != null &&
                    string.IsNullOrWhiteSpace(state.Id) == false &&
                    unique.Add(state.Id))
                {
                    stateIds.Add(state.Id);
                }
            }

            return stateIds.Count > 0;
        }

        public bool TryGetSelectedScopePaths(List<string> scopePaths)
        {
            if (scopePaths == null)
            {
                return false;
            }

            scopePaths.Clear();
            if (_graph == null || selection == null || selection.Count == 0)
            {
                return false;
            }

            HashSet<string> unique = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node == false)
                {
                    continue;
                }

                string nodeId = node.userData as string;
                if (string.IsNullOrWhiteSpace(nodeId) || nodeId.StartsWith("__scope__:", StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string scopePath = nodeId.Substring("__scope__:".Length) ?? string.Empty;
                if (unique.Add(scopePath))
                {
                    scopePaths.Add(scopePath);
                }
            }

            return scopePaths.Count > 0;
        }

        public bool TryGetSelectedLayerId(out string layerId)
        {
            layerId = null;
            if (_graph == null || selection == null || selection.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Edge)
                {
                    return false;
                }
            }

            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node)
                {
                    string nodeId = node.userData as string;
                    if (string.IsNullOrWhiteSpace(nodeId) == false &&
                        _layerNodeLayerIdById.TryGetValue(nodeId, out string resolvedLayerId))
                    {
                        layerId = resolvedLayerId;
                        return true;
                    }
                }
            }

            return false;
        }

        public void SelectStateById(string stateId)
        {
            SelectStateById(stateId, false);
        }

        public bool SelectStateById(string stateId, bool center)
        {
            if (string.IsNullOrWhiteSpace(stateId) || _stateViews.TryGetValue(stateId, out StateNodeView view) == false || view?.Node == null)
            {
                return false;
            }

            return SelectNode(view.Node, center);
        }

        private bool TryGetTwoSelectedStateIds(out string firstStateId, out string secondStateId)
        {
            firstStateId = null;
            secondStateId = null;

            if (selection == null || selection.Count < 2)
            {
                return false;
            }

            List<string> selectedStateIds = new List<string>(2);
            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is Node node)
                {
                    string nodeId = node.userData as string;
                    if (TryResolveTransitionEndpointForCreate(nodeId, out string stateId))
                    {
                        selectedStateIds.Add(stateId);
                        if (selectedStateIds.Count == 2)
                        {
                            break;
                        }
                    }
                }
            }

            if (selectedStateIds.Count < 2)
            {
                return false;
            }

            firstStateId = selectedStateIds[0];
            secondStateId = selectedStateIds[1];

            FusionAnimatorStateDefinition fromState = FindState(firstStateId);
            FusionAnimatorStateDefinition toState = FindState(secondStateId);

            if (fromState == null || toState == null || fromState.CanTransitionOut == false)
            {
                return false;
            }

            return FindTransitionByEndpoints(firstStateId, secondStateId) == null;
        }

        private void CreateTransition(string fromStateId, string toStateId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(fromStateId) || string.IsNullOrWhiteSpace(toStateId))
            {
                return;
            }

            if (TryResolveTransitionEndpointForCreate(fromStateId, out string resolvedFromStateId) == false ||
                TryResolveTransitionEndpointForCreate(toStateId, out string resolvedToStateId) == false)
            {
                return;
            }

            FusionAnimatorStateDefinition fromState = FindState(resolvedFromStateId);
            FusionAnimatorStateDefinition toState = FindState(resolvedToStateId);
            if (fromState == null || toState == null || fromState.CanTransitionOut == false)
            {
                return;
            }

            if (FindTransitionByEndpoints(resolvedFromStateId, resolvedToStateId) != null)
            {
                return;
            }

            Undo.RecordObject(_graph, "Create FusionAnimator Transition");
            FusionAnimatorTransitionDefinition transition = new FusionAnimatorTransitionDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("transition"),
                Name = "Transition",
                FromStateId = resolvedFromStateId,
                ToStateId = resolvedToStateId,
            };

            _graph.Transitions.Add(transition);
            RebuildFromGraphData();
            OnGraphDirty?.Invoke();
        }

        private void DeleteSelectedElements()
        {
            if (selection == null || selection.Count == 0)
            {
                return;
            }

            List<GraphElement> elements = new List<GraphElement>();
            for (int i = 0; i < selection.Count; ++i)
            {
                if (selection[i] is GraphElement graphElement)
                {
                    elements.Add(graphElement);
                }
            }

            if (elements.Count == 0)
            {
                return;
            }

            for (int i = 0; i < elements.Count; ++i)
            {
                if (elements[i] is Node)
                {
                    _allowNodeRemovalFromGraphChange = true;
                    break;
                }
            }

            DeleteElements(elements);
        }

        private void BuildMiniMapOverlay()
        {
            if (_miniMap != null && _miniMap.parent == this)
            {
                Remove(_miniMap);
            }

            _miniMap = new MiniMap
            {
                anchored = false,
            };
            _miniMap.SetPosition(new Rect(12.0f, 12.0f, 240.0f, 160.0f));
            Add(_miniMap);
            _miniMapDefaultTopRightPending = true;
        }

        public void SetMiniMapVisible(bool isVisible)
        {
            if (_miniMap == null)
            {
                return;
            }

            _miniMap.style.display = isVisible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void OnGeometryChanged(GeometryChangedEvent evt)
        {
            if (_miniMap == null || _miniMapDefaultTopRightPending == false)
            {
                return;
            }

            Rect miniMapRect = _miniMap.GetPosition();
            float width = miniMapRect.width > 1.0f ? miniMapRect.width : 240.0f;
            float height = miniMapRect.height > 1.0f ? miniMapRect.height : 160.0f;
            if (layout.width <= 1.0f || layout.height <= 1.0f)
            {
                return;
            }

            float x = Mathf.Clamp(layout.width - width - 12.0f, 0.0f, Mathf.Max(0.0f, layout.width - width));
            float y = Mathf.Clamp(12.0f, 0.0f, Mathf.Max(0.0f, layout.height - height));
            _miniMap.SetPosition(new Rect(x, y, width, height));
            _miniMapDefaultTopRightPending = false;
        }

        private void BuildPreviewCameraGizmo()
        {
            _previewCameraGizmo = new VisualElement
            {
                tooltip = "Preview camera gizmo. Left-drag: orbit, Right-drag: pan, Mouse wheel: zoom.",
            };
            _previewCameraGizmo.focusable = true;
            _previewCameraGizmo.style.position = Position.Absolute;
            _previewCameraGizmo.style.right = 12.0f;
            _previewCameraGizmo.style.bottom = 12.0f;
            _previewCameraGizmo.style.width = 78.0f;
            _previewCameraGizmo.style.height = 78.0f;
            _previewCameraGizmo.style.backgroundColor = new Color(0.08f, 0.08f, 0.08f, 0.52f);
            _previewCameraGizmo.style.borderTopColor = new Color(1.0f, 1.0f, 1.0f, 0.2f);
            _previewCameraGizmo.style.borderBottomColor = new Color(1.0f, 1.0f, 1.0f, 0.2f);
            _previewCameraGizmo.style.borderLeftColor = new Color(1.0f, 1.0f, 1.0f, 0.2f);
            _previewCameraGizmo.style.borderRightColor = new Color(1.0f, 1.0f, 1.0f, 0.2f);
            _previewCameraGizmo.style.borderTopWidth = 1.0f;
            _previewCameraGizmo.style.borderBottomWidth = 1.0f;
            _previewCameraGizmo.style.borderLeftWidth = 1.0f;
            _previewCameraGizmo.style.borderRightWidth = 1.0f;
            _previewCameraGizmo.style.borderTopLeftRadius = 4.0f;
            _previewCameraGizmo.style.borderTopRightRadius = 4.0f;
            _previewCameraGizmo.style.borderBottomLeftRadius = 4.0f;
            _previewCameraGizmo.style.borderBottomRightRadius = 4.0f;
            _previewCameraGizmo.style.alignItems = Align.Center;
            _previewCameraGizmo.style.justifyContent = Justify.Center;

            _previewCameraGizmoAxes = new VisualElement();
            _previewCameraGizmoAxes.style.position = Position.Absolute;
            _previewCameraGizmoAxes.style.left = 0.0f;
            _previewCameraGizmoAxes.style.right = 0.0f;
            _previewCameraGizmoAxes.style.top = 0.0f;
            _previewCameraGizmoAxes.style.bottom = 0.0f;
            _previewCameraGizmo.Add(_previewCameraGizmoAxes);

            _previewAxisX = new VisualElement();
            _previewAxisX.style.position = Position.Absolute;
            _previewAxisX.style.height = 2.0f;
            _previewAxisX.style.backgroundColor = new Color(0.88f, 0.28f, 0.28f, 0.95f);
            _previewCameraGizmoAxes.Add(_previewAxisX);

            _previewAxisY = new VisualElement();
            _previewAxisY.style.position = Position.Absolute;
            _previewAxisY.style.width = 2.0f;
            _previewAxisY.style.backgroundColor = new Color(0.3f, 0.84f, 0.32f, 0.95f);
            _previewCameraGizmoAxes.Add(_previewAxisY);

            _previewAxisZ = new VisualElement();
            _previewAxisZ.style.position = Position.Absolute;
            _previewAxisZ.style.height = 2.0f;
            _previewAxisZ.style.backgroundColor = new Color(0.3f, 0.5f, 0.95f, 0.95f);
            _previewCameraGizmoAxes.Add(_previewAxisZ);

            Label label = new Label("Pivot");
            label.style.position = Position.Absolute;
            label.style.left = 4.0f;
            label.style.bottom = 2.0f;
            label.style.fontSize = 9.0f;
            label.style.color = new Color(0.90f, 0.90f, 0.90f, 0.75f);
            label.pickingMode = PickingMode.Ignore;
            _previewCameraGizmo.Add(label);

            _previewCameraGizmo.RegisterCallback<MouseDownEvent>(OnPreviewCameraGizmoMouseDown);
            _previewCameraGizmo.RegisterCallback<MouseMoveEvent>(OnPreviewCameraGizmoMouseMove);
            _previewCameraGizmo.RegisterCallback<MouseUpEvent>(OnPreviewCameraGizmoMouseUp);
            _previewCameraGizmo.RegisterCallback<WheelEvent>(OnPreviewCameraGizmoWheel);
            _previewCameraGizmo.RegisterCallback<MouseEnterEvent>(OnPreviewCameraGizmoMouseEnter);
            _previewCameraGizmo.RegisterCallback<KeyDownEvent>(OnPreviewCameraGizmoKeyDown);
            Add(_previewCameraGizmo);
            UpdatePreviewCameraGizmoVisual();
        }

        private void OnPreviewCameraGizmoMouseDown(MouseDownEvent evt)
        {
            if (evt == null || (evt.button != 0 && evt.button != 1))
            {
                return;
            }

            _previewCameraGizmo.Focus();
            if (evt.clickCount >= 2)
            {
                RecenterPreviewPivot();
                evt.StopImmediatePropagation();
                return;
            }

            _previewCameraGizmoDragging = true;
            _previewCameraGizmoButton = evt.button;
            _previewCameraGizmoLastPosition = evt.localMousePosition;
            _previewCameraGizmo.CaptureMouse();
            evt.StopImmediatePropagation();
        }

        private void OnPreviewCameraGizmoMouseMove(MouseMoveEvent evt)
        {
            if (_previewCameraGizmoDragging == false || evt == null)
            {
                return;
            }

            Vector2 delta = evt.localMousePosition - _previewCameraGizmoLastPosition;
            _previewCameraGizmoLastPosition = evt.localMousePosition;

            if (_previewCameraGizmoButton == 0)
            {
                _previewOrbitYaw += delta.x * 0.38f;
                _previewOrbitYaw = Mathf.Repeat(_previewOrbitYaw + 180.0f, 360.0f) - 180.0f;
                _previewOrbitPitch = Mathf.Clamp(_previewOrbitPitch - delta.y * 0.38f, -80.0f, 80.0f);
            }
            else if (_previewCameraGizmoButton == 1)
            {
                float panScale = Mathf.Max(0.0006f, _previewLastBoundsRadius * 0.0035f);
                Vector3 right = _previewRenderUtility != null ? _previewRenderUtility.camera.transform.right : Vector3.right;
                Vector3 up = _previewRenderUtility != null ? _previewRenderUtility.camera.transform.up : Vector3.up;
                _previewOrbitTargetOffset += (-right * delta.x + up * delta.y) * panScale;
            }

            UpdatePreviewCameraGizmoVisual();
            OnPreviewCameraChanged?.Invoke();
            evt.StopImmediatePropagation();
        }

        private void OnPreviewCameraGizmoMouseUp(MouseUpEvent evt)
        {
            if (evt == null || _previewCameraGizmoDragging == false)
            {
                return;
            }

            _previewCameraGizmoDragging = false;
            _previewCameraGizmoButton = -1;
            if (_previewCameraGizmo.HasMouseCapture())
            {
                _previewCameraGizmo.ReleaseMouse();
            }

            evt.StopImmediatePropagation();
        }

        private void OnPreviewCameraGizmoWheel(WheelEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            _previewOrbitDistanceScale = Mathf.Clamp(_previewOrbitDistanceScale + evt.delta.y * 0.01f, 0.65f, 6.0f);
            UpdatePreviewCameraGizmoVisual();
            OnPreviewCameraChanged?.Invoke();
            evt.StopImmediatePropagation();
        }

        private void OnPreviewCameraGizmoMouseEnter(MouseEnterEvent evt)
        {
            _previewCameraGizmo.Focus();
        }

        private void OnPreviewCameraGizmoKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
            {
                return;
            }

            if (evt.keyCode == KeyCode.F)
            {
                RecenterPreviewPivot();
                evt.StopImmediatePropagation();
            }
        }

        private void RecenterPreviewPivot()
        {
            _previewOrbitTargetOffset = Vector3.zero;
            _previewOrbitDistanceScale = DefaultPreviewOrbitDistanceScale;
            UpdatePreviewCameraGizmoVisual();
            OnPreviewCameraChanged?.Invoke();
        }

        private new string SerializeGraphElements(IEnumerable<GraphElement> elements)
        {
            if (_graph == null || elements == null)
            {
                return string.Empty;
            }

            EnsureGraphCollections();

            HashSet<string> selectedStateIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> selectedTransitionIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> selectedScopePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (GraphElement element in elements)
            {
                if (element is Node node)
                {
                    string nodeId = node.userData as string;
                    if (string.IsNullOrWhiteSpace(nodeId))
                    {
                        continue;
                    }

                    if (IsScopeNodeId(nodeId))
                    {
                        string scopePath = NormalizeScopePath(nodeId.Substring("__scope__:".Length));
                        if (string.IsNullOrWhiteSpace(scopePath) == false)
                        {
                            selectedScopePaths.Add(scopePath);
                        }

                        continue;
                    }

                    if (nodeId == FusionAnimatorGraphAsset.SpecialNodeEntryId ||
                        nodeId == FusionAnimatorGraphAsset.SpecialNodeAnyId ||
                        nodeId == FusionAnimatorGraphAsset.SpecialNodeExitId)
                    {
                        continue;
                    }

                    if (_layerNodeLayerIdById.ContainsKey(nodeId))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(nodeId) == false)
                    {
                        selectedStateIds.Add(nodeId);
                    }
                }
                else if (element is Edge edge)
                {
                    string transitionId = edge.userData as string;
                    if (string.IsNullOrWhiteSpace(transitionId) == false)
                    {
                        selectedTransitionIds.Add(transitionId);
                    }
                }
            }

            if (selectedScopePaths.Count > 0 && string.IsNullOrWhiteSpace(_activeLayerId) == false && _graph.States != null)
            {
                for (int i = 0; i < _graph.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state == null ||
                        string.Equals(state.LayerId, _activeLayerId, StringComparison.Ordinal) == false ||
                        IsScopeSentinelState(state))
                    {
                        continue;
                    }

                    string stateScope = NormalizeScopePath(GetStateScopePath(state.Name));
                    if (string.IsNullOrWhiteSpace(stateScope))
                    {
                        continue;
                    }

                    foreach (string selectedScopePath in selectedScopePaths)
                    {
                        if (string.Equals(stateScope, selectedScopePath, StringComparison.OrdinalIgnoreCase) ||
                            stateScope.StartsWith(selectedScopePath + "/", StringComparison.OrdinalIgnoreCase))
                        {
                            selectedStateIds.Add(state.Id);
                            break;
                        }
                    }
                }
            }

            HashSet<string> selectedScopeRoots = ReduceToRootScopes(selectedScopePaths);
            HashSet<string> copiedScopePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (selectedScopeRoots.Count > 0 && string.IsNullOrWhiteSpace(_activeLayerId) == false)
            {
                foreach (string selectedScopeRoot in selectedScopeRoots)
                {
                    copiedScopePaths.Add(selectedScopeRoot);
                }

                if (_graph.States != null)
                {
                    for (int i = 0; i < _graph.States.Count; ++i)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[i];
                        if (state == null ||
                            string.Equals(state.LayerId, _activeLayerId, StringComparison.Ordinal) == false ||
                            IsScopeSentinelState(state))
                        {
                            continue;
                        }

                        string stateScope = NormalizeScopePath(GetStateScopePath(state.Name));
                        if (string.IsNullOrWhiteSpace(stateScope))
                        {
                            continue;
                        }

                        foreach (string selectedScopeRoot in selectedScopeRoots)
                        {
                            if (IsSameScopeOrChildPath(stateScope, selectedScopeRoot) == false)
                            {
                                continue;
                            }

                            AddScopePathAndAncestors(copiedScopePaths, stateScope, selectedScopeRoot);
                            break;
                        }
                    }
                }

                if (_graph.ScopeUtilityNodeLayouts != null)
                {
                    for (int i = 0; i < _graph.ScopeUtilityNodeLayouts.Count; ++i)
                    {
                        FusionAnimatorScopeUtilityNodeLayout layout = _graph.ScopeUtilityNodeLayouts[i];
                        if (layout == null || string.Equals(layout.LayerId, _activeLayerId, StringComparison.Ordinal) == false)
                        {
                            continue;
                        }

                        string layoutScope = NormalizeScopePath(layout.ScopePath);
                        if (string.IsNullOrWhiteSpace(layoutScope))
                        {
                            continue;
                        }

                        foreach (string selectedScopeRoot in selectedScopeRoots)
                        {
                            if (IsSameScopeOrChildPath(layoutScope, selectedScopeRoot) == false)
                            {
                                continue;
                            }

                            AddScopePathAndAncestors(copiedScopePaths, layoutScope, selectedScopeRoot);
                            break;
                        }
                    }
                }
            }

            ClipboardPayload payload = new ClipboardPayload();
            HashSet<string> referencedLayerIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> referencedParameterIds = new HashSet<string>(StringComparer.Ordinal);

            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state == null || selectedStateIds.Contains(state.Id) == false)
                {
                    continue;
                }

                ClipboardState clipboardState = new ClipboardState
                {
                    Id = state.Id,
                    Name = state.Name,
                    LayerId = state.LayerId,
                    LayerName = ResolveLayerNameById(state.LayerId),
                    NodePosition = state.NodePosition,
                    MinDurationSeconds = state.MinDurationSeconds,
                    CanTransitionOut = state.CanTransitionOut,
                    WriteDefaults = state.WriteDefaults,
                    MotionType = state.MotionType,
                    Clips = CloneClipSlots(state.Clips),
                    BlendTree = CloneBlendTree(state.BlendTree),
                    Presentation = CloneStatePresentation(state.Presentation),
                };
                payload.States.Add(clipboardState);
                if (string.IsNullOrWhiteSpace(state.LayerId) == false)
                {
                    referencedLayerIds.Add(state.LayerId);
                }

                CollectBlendTreeParameterIds(state.BlendTree, referencedParameterIds);
            }

            for (int i = 0; i < _graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null)
                {
                    continue;
                }

                bool transitionExplicitlySelected = selectedTransitionIds.Contains(transition.Id);
                bool transitionBetweenSelectedStates =
                    selectedStateIds.Contains(transition.FromStateId) &&
                    selectedStateIds.Contains(transition.ToStateId);
                bool transitionTouchesSelectedStateAndSpecialEndpoint =
                    (selectedStateIds.Contains(transition.FromStateId) && IsSpecialTransitionEndpoint(transition.ToStateId)) ||
                    (selectedStateIds.Contains(transition.ToStateId) && IsSpecialTransitionEndpoint(transition.FromStateId));

                if (transitionExplicitlySelected == false &&
                    transitionBetweenSelectedStates == false &&
                    transitionTouchesSelectedStateAndSpecialEndpoint == false)
                {
                    continue;
                }

                ClipboardTransition clipboardTransition = new ClipboardTransition
                {
                    Name = transition.Name,
                    FromStateId = transition.FromStateId,
                    ToStateId = transition.ToStateId,
                    Priority = transition.Priority,
                    Mute = transition.Mute,
                    Solo = transition.Solo,
                    HasExitTime = transition.HasExitTime,
                    ExitTimeNormalized = transition.ExitTimeNormalized,
                    StartOffsetNormalized = transition.StartOffsetNormalized,
                    FixedDuration = transition.FixedDuration,
                    BlendDurationSeconds = transition.BlendDurationSeconds,
                    InterruptionSource = transition.InterruptionSource,
                    CanInterrupt = transition.CanInterrupt,
                    Conditions = CloneConditions(transition.Conditions),
                };
                payload.Transitions.Add(clipboardTransition);
                CollectConditionParameterIds(transition.Conditions, referencedParameterIds);
            }

            if (copiedScopePaths.Count > 0 && string.IsNullOrWhiteSpace(_activeLayerId) == false)
            {
                List<string> orderedScopes = copiedScopePaths
                    .OrderBy(path => GetScopeDepth(path))
                    .ThenBy(path => path, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                for (int i = 0; i < orderedScopes.Count; ++i)
                {
                    string scopePath = orderedScopes[i];
                    if (string.IsNullOrWhiteSpace(scopePath))
                    {
                        continue;
                    }

                    ClipboardScope clipboardScope = new ClipboardScope
                    {
                        LayerId = _activeLayerId,
                        ScopePath = scopePath,
                    };

                    if (TryGetScopeUtilityNodeLayout(_activeLayerId, scopePath, out FusionAnimatorScopeUtilityNodeLayout layout))
                    {
                        clipboardScope.HasScopeNodePosition = layout.HasScopeNodePosition;
                        clipboardScope.ScopeNodePosition = layout.ScopeNodePosition;
                        clipboardScope.EntryNodePosition = layout.EntryNodePosition;
                        clipboardScope.AnyNodePosition = layout.AnyNodePosition;
                        clipboardScope.ExitNodePosition = layout.ExitNodePosition;
                    }

                    payload.Scopes.Add(clipboardScope);
                    referencedLayerIds.Add(_activeLayerId);
                }
            }

            if (payload.States.Count > 0 || payload.Scopes.Count > 0)
            {
                foreach (string layerId in referencedLayerIds)
                {
                    FusionAnimatorLayerDefinition layer = FindLayerById(layerId);
                    if (layer == null)
                    {
                        continue;
                    }

                    payload.Layers.Add(new ClipboardLayer
                    {
                        Id = layer.Id,
                        Name = layer.Name,
                        Priority = layer.Priority,
                        DefaultWeight = layer.DefaultWeight,
                        EnabledByDefault = layer.EnabledByDefault,
                        BlendMode = layer.BlendMode,
                        AvatarMask = layer.AvatarMask,
                        SyncedLayerIndex = layer.SyncedLayerIndex,
                        SyncTiming = layer.SyncTiming,
                        IKPass = layer.IKPass,
                    });
                }

                foreach (string parameterId in referencedParameterIds)
                {
                    FusionAnimatorParameterDefinition parameter = FindParameterById(parameterId);
                    if (parameter == null)
                    {
                        continue;
                    }

                    payload.Parameters.Add(new ClipboardParameter
                    {
                        Id = parameter.Id,
                        Name = parameter.Name,
                        Type = parameter.Type,
                        DefaultBool = parameter.DefaultBool,
                        Invert = parameter.Invert,
                        DefaultInt = parameter.DefaultInt,
                        DefaultFloat = parameter.DefaultFloat,
                        DefaultVector2 = parameter.DefaultVector2,
                        PreviewInputBinding = parameter.PreviewInputBinding,
                        PreviewInputScale = parameter.PreviewInputScale,
                        PreviewBoolInputSource = parameter.PreviewBoolInputSource,
                        PreviewBoolInputOperator = parameter.PreviewBoolInputOperator,
                        PreviewBoolInputCompareValue = parameter.PreviewBoolInputCompareValue,
                    });
                }
            }

            if (payload.States.Count == 0 && payload.Transitions.Count == 0 && payload.Scopes.Count == 0)
            {
                return string.Empty;
            }

            return JsonUtility.ToJson(payload, false);
        }

        private new bool CanPasteSerializedData(string data)
        {
            return TryDeserializeClipboardPayload(data, out ClipboardPayload payload) &&
                   ((payload.States != null && payload.States.Count > 0) ||
                    (payload.Scopes != null && payload.Scopes.Count > 0));
        }

        private void UnserializeAndPaste(string operationName, string data)
        {
            if (_graph == null || TryDeserializeClipboardPayload(data, out ClipboardPayload payload) == false)
            {
                return;
            }

            bool hasStates = payload.States != null && payload.States.Count > 0;
            bool hasScopes = payload.Scopes != null && payload.Scopes.Count > 0;
            if (hasStates == false && hasScopes == false)
            {
                return;
            }

            EnsureGraphCollections();

            ++_pasteIteration;
            Vector2 pasteBase = ResolveViewportCenterInContentSpace() + new Vector2(28.0f * _pasteIteration, 28.0f * _pasteIteration);

            Vector2 minPosition = Vector2.zero;
            if (hasStates)
            {
                minPosition = payload.States[0].NodePosition;
                for (int i = 1; i < payload.States.Count; ++i)
                {
                    Vector2 pos = payload.States[i].NodePosition;
                    if (pos.x < minPosition.x) minPosition.x = pos.x;
                    if (pos.y < minPosition.y) minPosition.y = pos.y;
                }
            }

            Undo.RecordObject(_graph, "Paste FusionAnimator Selection");

            bool hasCurrentLayerContext = string.IsNullOrWhiteSpace(_activeLayerId) == false;
            string targetScope = NormalizeScopePath(_scopeFilter);
            Dictionary<string, string> layerRemap = ResolveLayerRemapForPaste(payload, hasCurrentLayerContext ? _activeLayerId : string.Empty);
            Dictionary<string, string> parameterRemap = ResolveParameterRemapForPaste(payload);
            string sourceScopeRoot = ResolveCommonScopeRoot(payload.States);
            Dictionary<string, ClipboardScopeRemap> scopeRootRemap = ResolveScopeRootRemapForPaste(payload, layerRemap, hasCurrentLayerContext ? _activeLayerId : string.Empty, targetScope);
            ApplyPastedScopeLayouts(payload, scopeRootRemap, layerRemap, hasCurrentLayerContext ? _activeLayerId : string.Empty);

            Dictionary<string, string> idRemap = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < payload.States.Count; ++i)
            {
                ClipboardState source = payload.States[i];
                if (source == null)
                {
                    continue;
                }

                string newStateId = FusionAnimatorGraphAsset.NewId("state");
                idRemap[source.Id] = newStateId;

                string destinationLayerId = ResolveRemappedLayerId(layerRemap, source.LayerId, hasCurrentLayerContext ? _activeLayerId : string.Empty);
                if (string.IsNullOrWhiteSpace(destinationLayerId))
                {
                    destinationLayerId = GetDefaultLayerIdForCreation();
                }

                string sourceStateScope = NormalizeScopePath(GetStateScopePath(source.Name));
                string destinationScope;
                if (TryResolveMappedScopeForPaste(scopeRootRemap, source.LayerId, destinationLayerId, sourceStateScope, out string mappedSourceScopeRoot, out string mappedDestinationScopeRoot))
                {
                    string mappedRelativeScope = RemoveScopePrefix(sourceStateScope, mappedSourceScopeRoot);
                    destinationScope = CombineScopePaths(mappedDestinationScopeRoot, mappedRelativeScope);
                }
                else
                {
                    string relativeScope = RemoveScopePrefix(sourceStateScope, sourceScopeRoot);
                    destinationScope = CombineScopePaths(targetScope, relativeScope);
                }

                string destinationLeafName = GetStateLeafName(source.Name);
                string destinationStateName = BuildUniqueStateName(destinationLayerId, destinationLeafName, destinationScope);

                FusionAnimatorStateDefinition pastedState = new FusionAnimatorStateDefinition
                {
                    Id = newStateId,
                    Name = destinationStateName,
                    LayerId = destinationLayerId,
                    NodePosition = pasteBase + (source.NodePosition - minPosition),
                    MinDurationSeconds = source.MinDurationSeconds,
                    CanTransitionOut = source.CanTransitionOut,
                    WriteDefaults = source.WriteDefaults,
                    MotionType = source.MotionType,
                    Clips = CloneClipSlots(source.Clips),
                    BlendTree = CloneBlendTreeWithParameterRemap(source.BlendTree, parameterRemap),
                    Presentation = CloneStatePresentation(source.Presentation),
                };

                _graph.States.Add(pastedState);
            }

            if (payload.Transitions != null)
            {
                for (int i = 0; i < payload.Transitions.Count; ++i)
                {
                    ClipboardTransition sourceTransition = payload.Transitions[i];
                    if (sourceTransition == null)
                    {
                        continue;
                    }

                    if (TryResolveTransitionEndpointForPaste(sourceTransition.FromStateId, idRemap, out string mappedFrom) == false ||
                        TryResolveTransitionEndpointForPaste(sourceTransition.ToStateId, idRemap, out string mappedTo) == false)
                    {
                        continue;
                    }

                    if (FindTransitionByEndpoints(mappedFrom, mappedTo) != null)
                    {
                        continue;
                    }

                    FusionAnimatorTransitionDefinition pastedTransition = new FusionAnimatorTransitionDefinition
                    {
                        Id = FusionAnimatorGraphAsset.NewId("transition"),
                        Name = sourceTransition.Name,
                        FromStateId = mappedFrom,
                        ToStateId = mappedTo,
                        Priority = sourceTransition.Priority,
                        Mute = sourceTransition.Mute,
                        Solo = sourceTransition.Solo,
                        HasExitTime = sourceTransition.HasExitTime,
                        ExitTimeNormalized = sourceTransition.ExitTimeNormalized,
                        StartOffsetNormalized = sourceTransition.StartOffsetNormalized,
                        FixedDuration = sourceTransition.FixedDuration,
                        BlendDurationSeconds = sourceTransition.BlendDurationSeconds,
                        InterruptionSource = sourceTransition.InterruptionSource,
                        CanInterrupt = sourceTransition.CanInterrupt,
                        Conditions = CloneConditionsWithParameterRemap(sourceTransition.Conditions, parameterRemap),
                    };

                    _graph.Transitions.Add(pastedTransition);
                }
            }

            RebuildFromGraphData();
            OnGraphDirty?.Invoke();
        }

        private static bool IsSpecialTransitionEndpoint(string endpointId)
        {
            return string.Equals(endpointId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal) ||
                   string.Equals(endpointId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) ||
                   string.Equals(endpointId, FusionAnimatorGraphAsset.SpecialNodeExitId, StringComparison.Ordinal);
        }

        private static bool TryDeserializeClipboardPayload(string data, out ClipboardPayload payload)
        {
            payload = null;
            if (string.IsNullOrWhiteSpace(data))
            {
                return false;
            }

            string trimmed = data.TrimStart();
            if (trimmed.Length == 0 || trimmed[0] != '{')
            {
                return false;
            }

            try
            {
                payload = JsonUtility.FromJson<ClipboardPayload>(data);
            }
            catch (ArgumentException)
            {
                return false;
            }

            if (payload == null || payload.States == null)
            {
                return false;
            }

            payload.Layers ??= new List<ClipboardLayer>();
            payload.Parameters ??= new List<ClipboardParameter>();
            payload.Scopes ??= new List<ClipboardScope>();
            payload.Transitions ??= new List<ClipboardTransition>();
            return true;
        }

        private static List<FusionAnimatorClipSlot> CloneClipSlots(List<FusionAnimatorClipSlot> source)
        {
            List<FusionAnimatorClipSlot> clone = new List<FusionAnimatorClipSlot>();
            if (source == null)
            {
                return clone;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                FusionAnimatorClipSlot slot = source[i];
                if (slot == null)
                {
                    continue;
                }

                clone.Add(new FusionAnimatorClipSlot
                {
                    Slot = slot.Slot,
                    ReferenceMode = slot.ReferenceMode,
                    BindingId = slot.BindingId,
                    Clip = slot.Clip,
                    Speed = slot.Speed,
                    Loop = slot.Loop,
                });
            }

            return clone;
        }

        private static List<FusionAnimatorConditionDefinition> CloneConditions(List<FusionAnimatorConditionDefinition> source)
        {
            List<FusionAnimatorConditionDefinition> clone = new List<FusionAnimatorConditionDefinition>();
            if (source == null)
            {
                return clone;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = source[i];
                if (condition == null)
                {
                    continue;
                }

                clone.Add(new FusionAnimatorConditionDefinition
                {
                    ParameterId = condition.ParameterId,
                    Operator = condition.Operator,
                    UseAbsoluteValue = condition.UseAbsoluteValue,
                    BoolValue = condition.BoolValue,
                    IntValue = condition.IntValue,
                    FloatValue = condition.FloatValue,
                    Vector2Value = condition.Vector2Value,
                });
            }

            return clone;
        }

        private static List<FusionAnimatorConditionDefinition> CloneConditionsWithParameterRemap(
            List<FusionAnimatorConditionDefinition> source,
            Dictionary<string, string> parameterIdRemap)
        {
            List<FusionAnimatorConditionDefinition> clone = CloneConditions(source);
            if (clone == null || clone.Count == 0 || parameterIdRemap == null || parameterIdRemap.Count == 0)
            {
                return clone;
            }

            for (int i = 0; i < clone.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = clone[i];
                if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
                {
                    continue;
                }

                if (parameterIdRemap.TryGetValue(condition.ParameterId, out string mappedId) &&
                    string.IsNullOrWhiteSpace(mappedId) == false)
                {
                    condition.ParameterId = mappedId;
                    continue;
                }

                if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) &&
                    parameterIdRemap.TryGetValue(baseParameterId, out string remappedBaseId) &&
                    string.IsNullOrWhiteSpace(remappedBaseId) == false)
                {
                    condition.ParameterId = FusionAnimatorParameterReferenceUtility.Build(remappedBaseId, component);
                }
            }

            return clone;
        }

        private static FusionAnimatorBlendTreeDefinition CloneBlendTree(FusionAnimatorBlendTreeDefinition source)
        {
            FusionAnimatorBlendTreeDefinition clone = new FusionAnimatorBlendTreeDefinition();
            if (source == null)
            {
                return clone;
            }

            clone.Type = source.Type;
            clone.ParameterXId = source.ParameterXId;
            clone.ParameterYId = source.ParameterYId;
            clone.ParameterVector2Id = source.ParameterVector2Id;
            clone.PoseTimeParameterId = source.PoseTimeParameterId;
            clone.DirectBlendParameterId = source.DirectBlendParameterId;
            clone.InputOffsetX = source.InputOffsetX;
            clone.InputPowerX = source.InputPowerX;
            clone.NormalizeTimeScale = source.NormalizeTimeScale;
            clone.AutoDetectOnClipAssign = source.AutoDetectOnClipAssign;
            clone.Children = new List<FusionAnimatorBlendTreeChild>();

            if (source.Children != null)
            {
                for (int i = 0; i < source.Children.Count; ++i)
                {
                    FusionAnimatorBlendTreeChild child = source.Children[i];
                    if (child == null)
                    {
                        continue;
                    }

                    clone.Children.Add(new FusionAnimatorBlendTreeChild
                    {
                        Name = child.Name,
                        ReferenceMode = child.ReferenceMode,
                        BindingId = child.BindingId,
                        Clip = child.Clip,
                        Threshold = child.Threshold,
                        Position = child.Position,
                        DirectParameterId = child.DirectParameterId,
                        TimeScale = child.TimeScale,
                    });
                }
            }

            return clone;
        }

        private static FusionAnimatorBlendTreeDefinition CloneBlendTreeWithParameterRemap(
            FusionAnimatorBlendTreeDefinition source,
            Dictionary<string, string> parameterIdRemap)
        {
            FusionAnimatorBlendTreeDefinition clone = CloneBlendTree(source);
            if (clone == null || parameterIdRemap == null || parameterIdRemap.Count == 0)
            {
                return clone;
            }

            clone.ParameterXId = ResolveRemappedId(parameterIdRemap, clone.ParameterXId);
            clone.ParameterYId = ResolveRemappedId(parameterIdRemap, clone.ParameterYId);
            clone.ParameterVector2Id = ResolveRemappedId(parameterIdRemap, clone.ParameterVector2Id);
            clone.PoseTimeParameterId = ResolveRemappedId(parameterIdRemap, clone.PoseTimeParameterId);
            clone.DirectBlendParameterId = ResolveRemappedId(parameterIdRemap, clone.DirectBlendParameterId);

            if (clone.Children != null)
            {
                for (int i = 0; i < clone.Children.Count; ++i)
                {
                    FusionAnimatorBlendTreeChild child = clone.Children[i];
                    if (child == null || string.IsNullOrWhiteSpace(child.DirectParameterId))
                    {
                        continue;
                    }

                    child.DirectParameterId = ResolveRemappedId(parameterIdRemap, child.DirectParameterId);
                }
            }

            return clone;
        }

        private static FusionAnimatorStatePresentationDefinition CloneStatePresentation(FusionAnimatorStatePresentationDefinition source)
        {
            if (source == null)
            {
                return new FusionAnimatorStatePresentationDefinition();
            }

            return new FusionAnimatorStatePresentationDefinition
            {
                Semantic = source.Semantic,
                Offset = source.Offset,
                Power = source.Power,
                BlendSpeed = source.BlendSpeed,
                TurnSpeed = source.TurnSpeed,
                MaxMagnitude = source.MaxMagnitude,
                OverlayWeight = source.OverlayWeight,
            };
        }

        private static string ResolveRemappedId(Dictionary<string, string> idRemap, string sourceId)
        {
            if (string.IsNullOrWhiteSpace(sourceId) || idRemap == null || idRemap.Count == 0)
            {
                return sourceId;
            }

            if (idRemap.TryGetValue(sourceId, out string mappedId) && string.IsNullOrWhiteSpace(mappedId) == false)
            {
                return mappedId;
            }

            if (FusionAnimatorParameterReferenceUtility.TryParse(sourceId, out string baseParameterId, out FusionAnimatorParameterComponent component) &&
                idRemap.TryGetValue(baseParameterId, out string remappedBaseId) &&
                string.IsNullOrWhiteSpace(remappedBaseId) == false)
            {
                return FusionAnimatorParameterReferenceUtility.Build(remappedBaseId, component);
            }

            return sourceId;
        }

        private static void CollectBlendTreeParameterIds(FusionAnimatorBlendTreeDefinition blendTree, HashSet<string> ids)
        {
            if (blendTree == null || ids == null)
            {
                return;
            }

            AddParameterId(ids, blendTree.ParameterXId);
            AddParameterId(ids, blendTree.ParameterYId);
            AddParameterId(ids, blendTree.ParameterVector2Id);
            AddParameterId(ids, blendTree.PoseTimeParameterId);
            AddParameterId(ids, blendTree.DirectBlendParameterId);

            if (blendTree.Children == null)
            {
                return;
            }

            for (int i = 0; i < blendTree.Children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = blendTree.Children[i];
                if (child == null)
                {
                    continue;
                }

                AddParameterId(ids, child.DirectParameterId);
            }
        }

        private static void CollectConditionParameterIds(List<FusionAnimatorConditionDefinition> conditions, HashSet<string> ids)
        {
            if (conditions == null || ids == null)
            {
                return;
            }

            for (int i = 0; i < conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = conditions[i];
                if (condition == null)
                {
                    continue;
                }

                AddParameterId(ids, condition.ParameterId);
            }
        }

        private static void AddParameterId(HashSet<string> ids, string parameterId)
        {
            if (ids == null || string.IsNullOrWhiteSpace(parameterId))
            {
                return;
            }

            if (FusionAnimatorParameterReferenceUtility.TryParse(parameterId, out string baseParameterId, out _))
            {
                ids.Add(baseParameterId);
            }
            else
            {
                ids.Add(parameterId);
            }
        }

        private Dictionary<string, string> ResolveLayerRemapForPaste(ClipboardPayload payload, string forcedLayerId)
        {
            Dictionary<string, string> remap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_graph == null || payload == null || payload.States == null)
            {
                return remap;
            }

            HashSet<string> sourceLayerIds = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < payload.States.Count; ++i)
            {
                ClipboardState state = payload.States[i];
                if (state == null || string.IsNullOrWhiteSpace(state.LayerId))
                {
                    continue;
                }

                sourceLayerIds.Add(state.LayerId);
            }

            foreach (string sourceLayerId in sourceLayerIds)
            {
                if (string.IsNullOrWhiteSpace(sourceLayerId))
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(forcedLayerId) == false)
                {
                    remap[sourceLayerId] = forcedLayerId;
                    continue;
                }

                FusionAnimatorLayerDefinition existingById = FindLayerById(sourceLayerId);
                if (existingById != null)
                {
                    remap[sourceLayerId] = existingById.Id;
                    continue;
                }

                ClipboardLayer sourceLayer = FindClipboardLayerById(payload.Layers, sourceLayerId);
                string sourceLayerName = sourceLayer != null && string.IsNullOrWhiteSpace(sourceLayer.Name) == false
                    ? sourceLayer.Name
                    : FindClipboardStateLayerName(payload.States, sourceLayerId);
                FusionAnimatorLayerDefinition existingByName = FindLayerByName(sourceLayerName);
                if (existingByName != null)
                {
                    remap[sourceLayerId] = existingByName.Id;
                    continue;
                }

                string newLayerId = sourceLayerId;
                if (FindLayerById(newLayerId) != null || string.IsNullOrWhiteSpace(newLayerId))
                {
                    newLayerId = FusionAnimatorGraphAsset.NewId("layer");
                }

                FusionAnimatorLayerDefinition newLayer = new FusionAnimatorLayerDefinition
                {
                    Id = newLayerId,
                    Name = string.IsNullOrWhiteSpace(sourceLayerName) ? "Layer" : sourceLayerName,
                    Priority = _graph.Layers.Count,
                    DefaultWeight = sourceLayer != null ? sourceLayer.DefaultWeight : 1.0f,
                    EnabledByDefault = sourceLayer == null || sourceLayer.EnabledByDefault,
                    BlendMode = sourceLayer != null ? sourceLayer.BlendMode : FusionAnimatorLayerBlendMode.Override,
                    AvatarMask = sourceLayer != null ? sourceLayer.AvatarMask : null,
                    SyncedLayerIndex = sourceLayer != null ? sourceLayer.SyncedLayerIndex : -1,
                    SyncTiming = sourceLayer != null && sourceLayer.SyncTiming,
                    IKPass = sourceLayer != null && sourceLayer.IKPass,
                };

                _graph.Layers.Add(newLayer);
                remap[sourceLayerId] = newLayer.Id;
            }

            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null)
                {
                    layer.Priority = i;
                }
            }

            return remap;
        }

        private Dictionary<string, string> ResolveParameterRemapForPaste(ClipboardPayload payload)
        {
            Dictionary<string, string> remap = new Dictionary<string, string>(StringComparer.Ordinal);
            if (_graph == null || payload == null)
            {
                return remap;
            }

            HashSet<string> referencedParameterIds = new HashSet<string>(StringComparer.Ordinal);
            if (payload.States != null)
            {
                for (int i = 0; i < payload.States.Count; ++i)
                {
                    ClipboardState state = payload.States[i];
                    if (state == null)
                    {
                        continue;
                    }

                    CollectBlendTreeParameterIds(state.BlendTree, referencedParameterIds);
                }
            }

            if (payload.Transitions != null)
            {
                for (int i = 0; i < payload.Transitions.Count; ++i)
                {
                    ClipboardTransition transition = payload.Transitions[i];
                    if (transition == null)
                    {
                        continue;
                    }

                    CollectConditionParameterIds(transition.Conditions, referencedParameterIds);
                }
            }

            foreach (string sourceParameterId in referencedParameterIds)
            {
                if (string.IsNullOrWhiteSpace(sourceParameterId))
                {
                    continue;
                }

                FusionAnimatorParameterDefinition existingById = FindParameterById(sourceParameterId);
                if (existingById != null)
                {
                    remap[sourceParameterId] = existingById.Id;
                    continue;
                }

                ClipboardParameter sourceParameter = FindClipboardParameterById(payload.Parameters, sourceParameterId);
                FusionAnimatorParameterDefinition existingByName = FindParameterByNameAndType(
                    sourceParameter != null ? sourceParameter.Name : string.Empty,
                    sourceParameter != null ? sourceParameter.Type : FusionAnimatorParameterType.Float);
                if (existingByName != null)
                {
                    remap[sourceParameterId] = existingByName.Id;
                    continue;
                }

                if (sourceParameter == null)
                {
                    continue;
                }

                string newParameterId = sourceParameter.Id;
                if (string.IsNullOrWhiteSpace(newParameterId) || FindParameterById(newParameterId) != null)
                {
                    newParameterId = FusionAnimatorGraphAsset.NewId("param");
                }

                FusionAnimatorParameterDefinition created = new FusionAnimatorParameterDefinition
                {
                    Id = newParameterId,
                    Name = sourceParameter.Name,
                    Type = sourceParameter.Type,
                    DefaultBool = sourceParameter.DefaultBool,
                    Invert = sourceParameter.Invert,
                    DefaultInt = sourceParameter.DefaultInt,
                    DefaultFloat = sourceParameter.DefaultFloat,
                    DefaultVector2 = sourceParameter.DefaultVector2,
                    PreviewInputBinding = sourceParameter.PreviewInputBinding,
                    PreviewInputScale = sourceParameter.PreviewInputScale,
                    PreviewBoolInputSource = sourceParameter.PreviewBoolInputSource,
                    PreviewBoolInputOperator = sourceParameter.PreviewBoolInputOperator,
                    PreviewBoolInputCompareValue = sourceParameter.PreviewBoolInputCompareValue,
                };

                _graph.Parameters.Add(created);
                remap[sourceParameterId] = created.Id;
            }

            return remap;
        }

        private static bool TryResolveTransitionEndpointForPaste(string sourceEndpointId, Dictionary<string, string> pastedStateIdRemap, out string resolvedEndpointId)
        {
            resolvedEndpointId = string.Empty;
            if (string.IsNullOrWhiteSpace(sourceEndpointId))
            {
                return false;
            }

            if (string.Equals(sourceEndpointId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal) ||
                string.Equals(sourceEndpointId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) ||
                string.Equals(sourceEndpointId, FusionAnimatorGraphAsset.SpecialNodeExitId, StringComparison.Ordinal))
            {
                resolvedEndpointId = sourceEndpointId;
                return true;
            }

            return pastedStateIdRemap != null && pastedStateIdRemap.TryGetValue(sourceEndpointId, out resolvedEndpointId);
        }

        private static string ResolveCommonScopeRoot(List<ClipboardState> states)
        {
            if (states == null || states.Count == 0)
            {
                return string.Empty;
            }

            string common = null;
            for (int i = 0; i < states.Count; ++i)
            {
                ClipboardState state = states[i];
                if (state == null)
                {
                    continue;
                }

                string scope = NormalizeScopePath(GetStateScopePath(state.Name));
                if (common == null)
                {
                    common = scope;
                    continue;
                }

                common = GetCommonScopePrefix(common, scope);
                if (string.IsNullOrWhiteSpace(common))
                {
                    return string.Empty;
                }
            }

            return common ?? string.Empty;
        }

        private Dictionary<string, ClipboardScopeRemap> ResolveScopeRootRemapForPaste(
            ClipboardPayload payload,
            Dictionary<string, string> layerIdRemap,
            string forcedLayerId,
            string targetScope)
        {
            Dictionary<string, ClipboardScopeRemap> remap = new Dictionary<string, ClipboardScopeRemap>(StringComparer.Ordinal);
            if (_graph == null || payload?.Scopes == null || payload.Scopes.Count == 0)
            {
                return remap;
            }

            Dictionary<string, List<ClipboardScope>> scopesByLayer = new Dictionary<string, List<ClipboardScope>>(StringComparer.Ordinal);
            for (int i = 0; i < payload.Scopes.Count; ++i)
            {
                ClipboardScope scope = payload.Scopes[i];
                if (scope == null)
                {
                    continue;
                }

                string normalizedScope = NormalizeScopePath(scope.ScopePath);
                if (string.IsNullOrWhiteSpace(normalizedScope))
                {
                    continue;
                }

                string sourceLayerId = scope.LayerId ?? string.Empty;
                if (scopesByLayer.TryGetValue(sourceLayerId, out List<ClipboardScope> layerScopes) == false)
                {
                    layerScopes = new List<ClipboardScope>(8);
                    scopesByLayer.Add(sourceLayerId, layerScopes);
                }

                bool alreadyAdded = false;
                for (int existingIndex = 0; existingIndex < layerScopes.Count; ++existingIndex)
                {
                    ClipboardScope existing = layerScopes[existingIndex];
                    if (existing != null && string.Equals(existing.ScopePath, normalizedScope, StringComparison.OrdinalIgnoreCase))
                    {
                        alreadyAdded = true;
                        break;
                    }
                }

                if (alreadyAdded)
                {
                    continue;
                }

                scope.ScopePath = normalizedScope;
                layerScopes.Add(scope);
            }

            if (scopesByLayer.Count == 0)
            {
                return remap;
            }

            Dictionary<string, HashSet<string>> reservedScopesByLayer = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            HashSet<string> GetReservedScopes(string layerId)
            {
                if (reservedScopesByLayer.TryGetValue(layerId, out HashSet<string> reserved) == false)
                {
                    reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    reservedScopesByLayer.Add(layerId, reserved);
                }

                return reserved;
            }

            foreach (KeyValuePair<string, List<ClipboardScope>> pair in scopesByLayer)
            {
                string sourceLayerId = pair.Key;
                List<ClipboardScope> layerScopes = pair.Value;
                if (layerScopes == null || layerScopes.Count == 0)
                {
                    continue;
                }

                string destinationLayerId = ResolveRemappedLayerId(layerIdRemap, sourceLayerId, forcedLayerId);
                if (string.IsNullOrWhiteSpace(destinationLayerId))
                {
                    destinationLayerId = GetDefaultLayerIdForCreation();
                }

                if (string.IsNullOrWhiteSpace(destinationLayerId))
                {
                    continue;
                }

                List<ClipboardScope> ordered = layerScopes
                    .OrderBy(scope => GetScopeDepth(scope.ScopePath))
                    .ThenBy(scope => scope.ScopePath, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                HashSet<string> reservedScopes = GetReservedScopes(destinationLayerId);
                for (int i = 0; i < ordered.Count; ++i)
                {
                    ClipboardScope scope = ordered[i];
                    if (scope == null)
                    {
                        continue;
                    }

                    bool hasAncestor = false;
                    for (int ancestorIndex = 0; ancestorIndex < i; ++ancestorIndex)
                    {
                        ClipboardScope candidateAncestor = ordered[ancestorIndex];
                        if (candidateAncestor == null)
                        {
                            continue;
                        }

                        if (IsSameScopeOrChildPath(scope.ScopePath, candidateAncestor.ScopePath))
                        {
                            hasAncestor = true;
                            break;
                        }
                    }

                    if (hasAncestor)
                    {
                        continue;
                    }

                    string seedLeaf = GetScopeLeafName(scope.ScopePath);
                    if (string.IsNullOrWhiteSpace(seedLeaf))
                    {
                        seedLeaf = "SubStateMachine";
                    }

                    string destinationScope = BuildUniqueScopePathForPaste(destinationLayerId, targetScope, seedLeaf, reservedScopes);
                    reservedScopes.Add(destinationScope);

                    string remapKey = BuildScopeRemapKey(sourceLayerId, scope.ScopePath);
                    remap[remapKey] = new ClipboardScopeRemap
                    {
                        SourceLayerId = sourceLayerId,
                        SourceScopePath = scope.ScopePath,
                        DestinationLayerId = destinationLayerId,
                        DestinationScopePath = destinationScope,
                    };
                }

                for (int i = 0; i < ordered.Count; ++i)
                {
                    ClipboardScope scope = ordered[i];
                    if (scope == null)
                    {
                        continue;
                    }

                    string remapKey = BuildScopeRemapKey(sourceLayerId, scope.ScopePath);
                    if (remap.ContainsKey(remapKey))
                    {
                        continue;
                    }

                    string ancestorScope = GetParentScopePath(scope.ScopePath);
                    ClipboardScopeRemap ancestorRemap = null;
                    while (string.IsNullOrWhiteSpace(ancestorScope) == false)
                    {
                        string ancestorKey = BuildScopeRemapKey(sourceLayerId, ancestorScope);
                        if (remap.TryGetValue(ancestorKey, out ancestorRemap) && ancestorRemap != null)
                        {
                            break;
                        }

                        ancestorScope = GetParentScopePath(ancestorScope);
                    }

                    if (ancestorRemap == null)
                    {
                        continue;
                    }

                    string relativeScope = RemoveScopePrefix(scope.ScopePath, ancestorRemap.SourceScopePath);
                    string destinationScope = CombineScopePaths(ancestorRemap.DestinationScopePath, relativeScope);
                    remap[remapKey] = new ClipboardScopeRemap
                    {
                        SourceLayerId = sourceLayerId,
                        SourceScopePath = scope.ScopePath,
                        DestinationLayerId = ancestorRemap.DestinationLayerId,
                        DestinationScopePath = destinationScope,
                    };
                }
            }

            return remap;
        }

        private void ApplyPastedScopeLayouts(
            ClipboardPayload payload,
            Dictionary<string, ClipboardScopeRemap> scopeRemap,
            Dictionary<string, string> layerIdRemap,
            string forcedLayerId)
        {
            if (_graph == null || payload?.Scopes == null || payload.Scopes.Count == 0)
            {
                return;
            }

            for (int i = 0; i < payload.Scopes.Count; ++i)
            {
                ClipboardScope sourceScope = payload.Scopes[i];
                if (sourceScope == null)
                {
                    continue;
                }

                string normalizedSourceScopePath = NormalizeScopePath(sourceScope.ScopePath);
                if (string.IsNullOrWhiteSpace(normalizedSourceScopePath))
                {
                    continue;
                }

                string destinationLayerId = ResolveRemappedLayerId(layerIdRemap, sourceScope.LayerId, forcedLayerId);
                if (string.IsNullOrWhiteSpace(destinationLayerId))
                {
                    destinationLayerId = GetDefaultLayerIdForCreation();
                }

                if (string.IsNullOrWhiteSpace(destinationLayerId))
                {
                    continue;
                }

                string destinationScopePath = normalizedSourceScopePath;
                string remapKey = BuildScopeRemapKey(sourceScope.LayerId, normalizedSourceScopePath);
                if (scopeRemap != null &&
                    scopeRemap.TryGetValue(remapKey, out ClipboardScopeRemap mappedScope) &&
                    mappedScope != null &&
                    string.IsNullOrWhiteSpace(mappedScope.DestinationScopePath) == false)
                {
                    destinationLayerId = string.IsNullOrWhiteSpace(mappedScope.DestinationLayerId) ? destinationLayerId : mappedScope.DestinationLayerId;
                    destinationScopePath = mappedScope.DestinationScopePath;
                }

                FusionAnimatorScopeUtilityNodeLayout destinationLayout = GetOrCreateScopeUtilityNodeLayout(destinationLayerId, destinationScopePath);
                if (destinationLayout == null)
                {
                    continue;
                }

                destinationLayout.HasScopeNodePosition = sourceScope.HasScopeNodePosition;
                if (sourceScope.HasScopeNodePosition)
                {
                    destinationLayout.ScopeNodePosition = sourceScope.ScopeNodePosition;
                }

                destinationLayout.EntryNodePosition = sourceScope.EntryNodePosition;
                destinationLayout.AnyNodePosition = sourceScope.AnyNodePosition;
                destinationLayout.ExitNodePosition = sourceScope.ExitNodePosition;
            }
        }

        private static bool TryResolveMappedScopeForPaste(
            Dictionary<string, ClipboardScopeRemap> scopeRemap,
            string sourceLayerId,
            string destinationLayerId,
            string sourceScopePath,
            out string mappedSourceScopeRoot,
            out string mappedDestinationScopeRoot)
        {
            mappedSourceScopeRoot = string.Empty;
            mappedDestinationScopeRoot = string.Empty;
            if (scopeRemap == null || scopeRemap.Count == 0)
            {
                return false;
            }

            string normalizedSourceScope = NormalizeScopePath(sourceScopePath);
            if (string.IsNullOrWhiteSpace(normalizedSourceScope))
            {
                return false;
            }

            string normalizedSourceLayerId = sourceLayerId ?? string.Empty;
            ClipboardScopeRemap bestMatch = null;
            int bestDepth = -1;
            foreach (KeyValuePair<string, ClipboardScopeRemap> pair in scopeRemap)
            {
                ClipboardScopeRemap candidate = pair.Value;
                if (candidate == null)
                {
                    continue;
                }

                if (string.Equals(candidate.SourceLayerId ?? string.Empty, normalizedSourceLayerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(destinationLayerId) == false &&
                    string.Equals(candidate.DestinationLayerId, destinationLayerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                if (IsSameScopeOrChildPath(normalizedSourceScope, candidate.SourceScopePath) == false)
                {
                    continue;
                }

                int depth = GetScopeDepth(candidate.SourceScopePath);
                if (depth > bestDepth)
                {
                    bestDepth = depth;
                    bestMatch = candidate;
                }
            }

            if (bestMatch == null)
            {
                return false;
            }

            mappedSourceScopeRoot = NormalizeScopePath(bestMatch.SourceScopePath);
            mappedDestinationScopeRoot = NormalizeScopePath(bestMatch.DestinationScopePath);
            return string.IsNullOrWhiteSpace(mappedDestinationScopeRoot) == false;
        }

        private string BuildUniqueScopePathForPaste(string layerId, string parentScope, string baseLeafName, HashSet<string> reservedScopes)
        {
            string normalizedParent = NormalizeScopePath(parentScope);
            string seedLeaf = string.IsNullOrWhiteSpace(baseLeafName) ? "SubStateMachine" : baseLeafName.Trim();
            if (string.IsNullOrWhiteSpace(seedLeaf))
            {
                seedLeaf = "SubStateMachine";
            }

            for (int suffix = 1; suffix < 1000; ++suffix)
            {
                string candidateLeaf = suffix == 1 ? seedLeaf : string.Format("{0} {1}", seedLeaf, suffix);
                string candidateScopePath = CombineScopePaths(normalizedParent, candidateLeaf);
                if (DoesScopePathExistInLayer(layerId, candidateScopePath, reservedScopes) == false)
                {
                    return candidateScopePath;
                }
            }

            return CombineScopePaths(normalizedParent, seedLeaf);
        }

        private bool DoesScopePathExistInLayer(string layerId, string scopePath, ISet<string> reservedScopes)
        {
            string normalizedScope = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                return false;
            }

            if (reservedScopes != null)
            {
                foreach (string reserved in reservedScopes)
                {
                    string normalizedReserved = NormalizeScopePath(reserved);
                    if (string.IsNullOrWhiteSpace(normalizedReserved))
                    {
                        continue;
                    }

                    if (string.Equals(normalizedReserved, normalizedScope, StringComparison.OrdinalIgnoreCase) ||
                        normalizedReserved.StartsWith(normalizedScope + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (_graph?.States != null)
            {
                for (int i = 0; i < _graph.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string stateScope = NormalizeScopePath(GetStateScopePath(state.Name));
                    if (string.IsNullOrWhiteSpace(stateScope))
                    {
                        continue;
                    }

                    if (string.Equals(stateScope, normalizedScope, StringComparison.OrdinalIgnoreCase) ||
                        stateScope.StartsWith(normalizedScope + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            if (_graph?.ScopeUtilityNodeLayouts != null)
            {
                for (int i = 0; i < _graph.ScopeUtilityNodeLayouts.Count; ++i)
                {
                    FusionAnimatorScopeUtilityNodeLayout layout = _graph.ScopeUtilityNodeLayouts[i];
                    if (layout == null || string.Equals(layout.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string layoutScope = NormalizeScopePath(layout.ScopePath);
                    if (string.IsNullOrWhiteSpace(layoutScope))
                    {
                        continue;
                    }

                    if (string.Equals(layoutScope, normalizedScope, StringComparison.OrdinalIgnoreCase) ||
                        layoutScope.StartsWith(normalizedScope + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static string BuildScopeRemapKey(string layerId, string scopePath)
        {
            return string.Format("{0}|{1}", layerId ?? string.Empty, NormalizeScopePath(scopePath));
        }

        private static HashSet<string> ReduceToRootScopes(IEnumerable<string> scopePaths)
        {
            HashSet<string> roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (scopePaths == null)
            {
                return roots;
            }

            List<string> ordered = new List<string>();
            foreach (string scopePath in scopePaths)
            {
                string normalized = NormalizeScopePath(scopePath);
                if (string.IsNullOrWhiteSpace(normalized) == false && ordered.Contains(normalized, StringComparer.OrdinalIgnoreCase) == false)
                {
                    ordered.Add(normalized);
                }
            }

            ordered.Sort((lhs, rhs) =>
            {
                int depthCompare = GetScopeDepth(lhs).CompareTo(GetScopeDepth(rhs));
                if (depthCompare != 0)
                {
                    return depthCompare;
                }

                return string.Compare(lhs, rhs, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < ordered.Count; ++i)
            {
                string candidate = ordered[i];
                bool covered = false;
                foreach (string root in roots)
                {
                    if (IsSameScopeOrChildPath(candidate, root))
                    {
                        covered = true;
                        break;
                    }
                }

                if (covered == false)
                {
                    roots.Add(candidate);
                }
            }

            return roots;
        }

        private static void AddScopePathAndAncestors(HashSet<string> targetScopes, string scopePath, string rootScopePath)
        {
            if (targetScopes == null)
            {
                return;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            string normalizedRoot = NormalizeScopePath(rootScopePath);
            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(normalizedRoot) == false &&
                IsSameScopeOrChildPath(normalizedScope, normalizedRoot) == false)
            {
                return;
            }

            string cursor = normalizedScope;
            while (string.IsNullOrWhiteSpace(cursor) == false)
            {
                targetScopes.Add(cursor);
                if (string.IsNullOrWhiteSpace(normalizedRoot) == false &&
                    string.Equals(cursor, normalizedRoot, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                string nextCursor = GetParentScopePath(cursor);
                if (string.Equals(nextCursor, cursor, StringComparison.OrdinalIgnoreCase))
                {
                    break;
                }

                cursor = nextCursor;
            }

            if (string.IsNullOrWhiteSpace(normalizedRoot) == false)
            {
                targetScopes.Add(normalizedRoot);
            }
        }

        private static bool IsSameScopeOrChildPath(string candidateScopePath, string parentScopePath)
        {
            string normalizedCandidate = NormalizeScopePath(candidateScopePath);
            string normalizedParent = NormalizeScopePath(parentScopePath);
            if (string.IsNullOrWhiteSpace(normalizedCandidate) || string.IsNullOrWhiteSpace(normalizedParent))
            {
                return false;
            }

            if (string.Equals(normalizedCandidate, normalizedParent, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return normalizedCandidate.StartsWith(normalizedParent + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static int GetScopeDepth(string scopePath)
        {
            string normalized = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return 0;
            }

            int depth = 1;
            for (int i = 0; i < normalized.Length; ++i)
            {
                if (normalized[i] == '/')
                {
                    depth++;
                }
            }

            return depth;
        }

        private static string GetCommonScopePrefix(string lhs, string rhs)
        {
            lhs = NormalizeScopePath(lhs);
            rhs = NormalizeScopePath(rhs);

            if (string.IsNullOrWhiteSpace(lhs) || string.IsNullOrWhiteSpace(rhs))
            {
                return string.Empty;
            }

            string[] lhsParts = lhs.Split('/');
            string[] rhsParts = rhs.Split('/');
            int matchCount = Mathf.Min(lhsParts.Length, rhsParts.Length);
            int shared = 0;
            for (int i = 0; i < matchCount; ++i)
            {
                if (string.Equals(lhsParts[i], rhsParts[i], StringComparison.OrdinalIgnoreCase) == false)
                {
                    break;
                }

                shared++;
            }

            if (shared <= 0)
            {
                return string.Empty;
            }

            return string.Join("/", lhsParts, 0, shared);
        }

        private static string RemoveScopePrefix(string scopePath, string prefix)
        {
            string normalizedScope = NormalizeScopePath(scopePath);
            string normalizedPrefix = NormalizeScopePath(prefix);
            if (string.IsNullOrWhiteSpace(normalizedPrefix))
            {
                return normalizedScope;
            }

            if (string.Equals(normalizedScope, normalizedPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (normalizedScope.StartsWith(normalizedPrefix + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedScope.Substring(normalizedPrefix.Length + 1);
            }

            return normalizedScope;
        }

        private static string CombineScopePaths(string lhs, string rhs)
        {
            string left = NormalizeScopePath(lhs);
            string right = NormalizeScopePath(rhs);
            if (string.IsNullOrWhiteSpace(left))
            {
                return right;
            }

            if (string.IsNullOrWhiteSpace(right))
            {
                return left;
            }

            return left + "/" + right;
        }

        private static string ResolveRemappedLayerId(Dictionary<string, string> layerIdRemap, string sourceLayerId, string fallbackLayerId)
        {
            if (layerIdRemap != null &&
                string.IsNullOrWhiteSpace(sourceLayerId) == false &&
                layerIdRemap.TryGetValue(sourceLayerId, out string mappedLayerId) &&
                string.IsNullOrWhiteSpace(mappedLayerId) == false)
            {
                return mappedLayerId;
            }

            return fallbackLayerId;
        }

        private void ConfigureEdgeVisual(Edge edge, FusionAnimatorTransitionDefinition transition)
        {
            if (edge == null || transition == null)
            {
                return;
            }

            edge.capabilities |= Capabilities.Copiable;
            edge.tooltip = string.Empty;
            VisualElement badgeContainer = edge.Q<VisualElement>("fa-transition-badge-container");
            if (badgeContainer != null)
            {
                badgeContainer.RemoveFromHierarchy();
            }
        }

        private void RefreshTransitionBadges()
        {
            if (_graph == null)
            {
                return;
            }

            foreach (KeyValuePair<string, Edge> pair in _edgeViews)
            {
                string transitionId = pair.Key;
                Edge edge = pair.Value;
                if (edge == null)
                {
                    continue;
                }

                FusionAnimatorTransitionDefinition transition = FindTransitionById(transitionId);
                if (transition == null)
                {
                    continue;
                }

                ConfigureEdgeVisual(edge, transition);

                bool selected = string.IsNullOrWhiteSpace(_selectedTransitionId) == false &&
                                string.Equals(_selectedTransitionId, transitionId, StringComparison.Ordinal);

                bool matchesParameter = string.IsNullOrWhiteSpace(_hoveredParameterId) == false && TransitionUsesParameter(transition, _hoveredParameterId);
                Color edgeColor;
                if (selected)
                {
                    edgeColor = new Color(0.42f, 0.82f, 1.0f, 1.0f);
                    edge.style.opacity = 1.0f;
                }
                else if (matchesParameter)
                {
                    edgeColor = new Color(0.30f, 0.80f, 0.44f, 1.0f);
                    edge.style.opacity = 1.0f;
                }
                else if (string.IsNullOrWhiteSpace(_hoveredParameterId) == false)
                {
                    edgeColor = new Color(0.75f, 0.75f, 0.75f, 1.0f);
                    edge.style.opacity = 0.25f;
                }
                else
                {
                    edgeColor = new Color(0.86f, 0.86f, 0.86f, 1.0f);
                    edge.style.opacity = 1.0f;
                }

                if (edge.edgeControl != null)
                {
                    edge.edgeControl.inputColor = edgeColor;
                    edge.edgeControl.outputColor = edgeColor;
                    edge.edgeControl.MarkDirtyRepaint();
                }
            }
        }

        private static bool TransitionUsesParameter(FusionAnimatorTransitionDefinition transition, string parameterId)
        {
            if (transition == null || string.IsNullOrWhiteSpace(parameterId) || transition.Conditions == null)
            {
                return false;
            }

            FusionAnimatorParameterReferenceUtility.TryParse(parameterId, out string targetBaseId, out _);
            if (string.IsNullOrWhiteSpace(targetBaseId))
            {
                targetBaseId = parameterId;
            }

            for (int i = 0; i < transition.Conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = transition.Conditions[i];
                if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
                {
                    continue;
                }

                FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string conditionBaseId, out _);
                if (string.IsNullOrWhiteSpace(conditionBaseId))
                {
                    conditionBaseId = condition.ParameterId;
                }

                if (string.Equals(conditionBaseId, targetBaseId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        private void ApplySearchFilter()
        {
            bool hasFilter = string.IsNullOrWhiteSpace(_searchFilter) == false;

            foreach (KeyValuePair<string, StateNodeView> pair in _stateViews)
            {
                FusionAnimatorStateDefinition state = FindState(pair.Key);
                StateNodeView view = pair.Value;
                if (state == null || view == null || view.Node == null)
                {
                    continue;
                }

                bool visible = true;
                if (visible)
                {
                    visible = IsStateInScope(state);
                }

                if (hasFilter)
                {
                    string stateName = string.IsNullOrWhiteSpace(state.Name) ? string.Empty : state.Name.ToLowerInvariant();
                    string stateId = string.IsNullOrWhiteSpace(state.Id) ? string.Empty : state.Id.ToLowerInvariant();
                    string layerName = GetLayerNameById(state.LayerId);
                    visible =
                        stateName.Contains(_searchFilter) ||
                        stateId.Contains(_searchFilter) ||
                        layerName.Contains(_searchFilter);
                }

                view.Node.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }

            foreach (KeyValuePair<string, Edge> pair in _edgeViews)
            {
                Edge edge = pair.Value;
                if (edge == null)
                {
                    continue;
                }

                bool outputVisible = edge.output == null || edge.output.node.style.display != DisplayStyle.None;
                bool inputVisible = edge.input == null || edge.input.node.style.display != DisplayStyle.None;
                edge.style.display = (outputVisible && inputVisible) ? DisplayStyle.Flex : DisplayStyle.None;
            }

            RefreshNodeLayerHighlight();
            RefreshPreviewRuntimeMarkers();
        }

        private bool IsStateInScope(FusionAnimatorStateDefinition state)
        {
            if (state == null || string.IsNullOrWhiteSpace(_scopeFilter))
            {
                return true;
            }

            string stateScope = GetStateScopePath(state.Name);
            if (string.IsNullOrWhiteSpace(stateScope))
            {
                return false;
            }

            if (string.Equals(stateScope, _scopeFilter, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return stateScope.StartsWith(_scopeFilter + "/", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsScopePathDeleted(string candidateScopePath, HashSet<string> removedScopePaths)
        {
            if (removedScopePaths == null || removedScopePaths.Count == 0)
            {
                return false;
            }

            string normalizedCandidate = NormalizeScopePath(candidateScopePath);
            if (string.IsNullOrWhiteSpace(normalizedCandidate))
            {
                return false;
            }

            foreach (string removedScope in removedScopePaths)
            {
                if (string.IsNullOrWhiteSpace(removedScope))
                {
                    continue;
                }

                if (string.Equals(normalizedCandidate, removedScope, StringComparison.OrdinalIgnoreCase) ||
                    normalizedCandidate.StartsWith(removedScope + "/", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
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

            return stateName.Substring(0, separator).Trim();
        }

        private static string NormalizeScopePath(string scopePath)
        {
            if (string.IsNullOrWhiteSpace(scopePath))
            {
                return string.Empty;
            }

            return scopePath.Trim().Trim('/');
        }

        private void RefreshNodeLayerHighlight()
        {
            bool hasHoverLayer = string.IsNullOrWhiteSpace(_hoveredLayerId) == false;

            foreach (KeyValuePair<string, StateNodeView> pair in _stateViews)
            {
                FusionAnimatorStateDefinition state = FindState(pair.Key);
                StateNodeView view = pair.Value;
                if (state == null || view == null || view.Node == null || view.Node.style.display == DisplayStyle.None)
                {
                    continue;
                }

                bool isHighlighted = hasHoverLayer && string.Equals(state.LayerId, _hoveredLayerId, StringComparison.Ordinal);

                if (hasHoverLayer == false)
                {
                    view.Node.style.opacity = 1.0f;
                    continue;
                }

                view.Node.style.opacity = isHighlighted ? 1.0f : 0.35f;
            }
        }

        private void RefreshPreviewRuntimeMarkers()
        {
            foreach (KeyValuePair<string, StateNodeView> pair in _stateViews)
            {
                string nodeId = pair.Key;
                StateNodeView view = pair.Value;
                if (view == null || view.RuntimeBadge == null)
                {
                    continue;
                }

                bool isLayerNode = _layerNodeLayerIdById.TryGetValue(nodeId, out string layerId);
                bool isActive = isLayerNode
                    ? string.IsNullOrWhiteSpace(layerId) == false && _previewActiveLayerIds.Contains(layerId)
                    : _previewActiveStateIds.Contains(nodeId);
                bool isBlend = isLayerNode == false && _previewBlendStateIds.Contains(nodeId) && isActive == false;
                SetRuntimeBadgeState(view.RuntimeBadge, isActive, isBlend);
            }
        }

        private static void SetRuntimeBadgeState(Label badge, bool isActive, bool isBlend)
        {
            if (badge == null)
            {
                return;
            }

            if (isActive)
            {
                badge.text = "LIVE";
                badge.style.backgroundColor = new Color(0.32f, 0.78f, 0.40f, 0.95f);
                badge.style.display = DisplayStyle.Flex;
                return;
            }

            if (isBlend)
            {
                badge.text = "BLEND";
                badge.style.backgroundColor = new Color(0.95f, 0.72f, 0.26f, 0.95f);
                badge.style.display = DisplayStyle.Flex;
                return;
            }

            badge.style.display = DisplayStyle.None;
        }

        private static void CopyMarkerSet(HashSet<string> target, IEnumerable<string> source)
        {
            target.Clear();
            if (source == null)
            {
                return;
            }

            foreach (string id in source)
            {
                if (string.IsNullOrWhiteSpace(id))
                {
                    continue;
                }

                target.Add(id);
            }
        }

        private GraphViewChange HandleGraphViewChanged(GraphViewChange change)
        {
            if (_suppressChangeCallbacks || _graph == null)
            {
                return change;
            }

            bool allowNodeRemoval = _allowNodeRemovalFromGraphChange;
            _allowNodeRemovalFromGraphChange = false;
            bool dirty = false;
            bool undoRecorded = false;
            bool forceRebuild = false;

            void EnsureUndo()
            {
                if (undoRecorded)
                {
                    return;
                }

                Undo.RecordObject(_graph, "Edit FusionAnimator Graph");
                undoRecorded = true;
            }

            if (change.movedElements != null)
            {
                for (int i = 0, count = change.movedElements.Count; i < count; ++i)
                {
                    if (change.movedElements[i] is Node node)
                    {
                        string stateId = node.userData as string;
                        Vector2 movedPosition = node.GetPosition().position;
                        FusionAnimatorStateDefinition state = FindState(stateId);
                        if (state != null)
                        {
                            if (IsPositionNearlyEqual(state.NodePosition, movedPosition) == false)
                            {
                                EnsureUndo();
                                state.NodePosition = movedPosition;
                                dirty = true;
                            }
                        }
                        else if (stateId == FusionAnimatorGraphAsset.SpecialNodeEntryId)
                        {
                            EnsureUndo();
                            dirty |= SetSpecialNodePositionForCurrentScope(stateId, movedPosition);
                        }
                        else if (stateId == FusionAnimatorGraphAsset.SpecialNodeAnyId)
                        {
                            EnsureUndo();
                            dirty |= SetSpecialNodePositionForCurrentScope(stateId, movedPosition);
                        }
                        else if (stateId == FusionAnimatorGraphAsset.SpecialNodeExitId)
                        {
                            EnsureUndo();
                            dirty |= SetSpecialNodePositionForCurrentScope(stateId, movedPosition);
                        }
                        else if (IsScopeNodeId(stateId))
                        {
                            EnsureUndo();
                            string scopePath = stateId.Substring("__scope__:".Length);
                            dirty |= SetScopeNodePosition(_activeLayerId, scopePath, movedPosition);
                        }
                    }
                }
            }

            if (change.edgesToCreate != null)
            {
                List<Edge> duplicateEdges = null;

                for (int i = 0, count = change.edgesToCreate.Count; i < count; ++i)
                {
                    Edge edge = change.edgesToCreate[i];
                    string fromNodeId = edge.output?.node?.userData as string;
                    string toNodeId = edge.input?.node?.userData as string;
                    if (IsScopeNodeId(fromNodeId))
                    {
                        EnsureUndo();
                        string scopePath = fromNodeId.Substring("__scope__:".Length);
                        GetOrCreateScopeSentinelState(_activeLayerId, scopePath);
                        GetOrCreateScopeUtilityNodeLayout(_activeLayerId, scopePath);
                    }

                    if (IsScopeNodeId(toNodeId))
                    {
                        EnsureUndo();
                        string scopePath = toNodeId.Substring("__scope__:".Length);
                        GetOrCreateScopeSentinelState(_activeLayerId, scopePath);
                        GetOrCreateScopeUtilityNodeLayout(_activeLayerId, scopePath);
                    }

                    if (TryResolveTransitionEndpointForCreate(fromNodeId, out string fromStateId) == false ||
                        TryResolveTransitionEndpointForCreate(toNodeId, out string toStateId) == false)
                    {
                        if (duplicateEdges == null)
                        {
                            duplicateEdges = new List<Edge>();
                        }

                        duplicateEdges.Add(edge);
                        continue;
                    }

                    if (IsTransitionEndpointsValid(fromStateId, toStateId) == false)
                    {
                        if (duplicateEdges == null)
                        {
                            duplicateEdges = new List<Edge>();
                        }

                        duplicateEdges.Add(edge);
                        continue;
                    }

                    if (fromStateId == FusionAnimatorGraphAsset.SpecialNodeEntryId)
                    {
                        FusionAnimatorStateDefinition defaultTargetState = FindState(toStateId);
                        if (defaultTargetState != null)
                        {
                            EnsureUndo();
                            if (SetScopeDefaultStateInternal(defaultTargetState))
                            {
                                dirty = true;
                                forceRebuild = true;
                            }
                        }

                        if (duplicateEdges == null)
                        {
                            duplicateEdges = new List<Edge>();
                        }

                        duplicateEdges.Add(edge);
                        continue;
                    }

                    FusionAnimatorTransitionDefinition transition = FindTransitionByEndpoints(fromStateId, toStateId);
                    if (transition == null)
                    {
                        EnsureUndo();
                        transition = new FusionAnimatorTransitionDefinition
                        {
                            Id = FusionAnimatorGraphAsset.NewId("transition"),
                            Name = "Transition",
                            FromStateId = fromStateId,
                            ToStateId = toStateId,
                        };
                        _graph.Transitions.Add(transition);
                        dirty = true;
                    }
                    else
                    {
                        if (RemoveTransitionSuppressionForCurrentScope(transition.Id))
                        {
                            dirty = true;
                            forceRebuild = true;
                        }

                        if (duplicateEdges == null)
                        {
                            duplicateEdges = new List<Edge>();
                        }
                        duplicateEdges.Add(edge);
                        continue;
                    }

                    edge.userData = transition.Id;
                    ConfigureEdgeVisual(edge, transition);
                    _edgeViews[transition.Id] = edge;
                }

                if (duplicateEdges != null)
                {
                    for (int i = 0; i < duplicateEdges.Count; ++i)
                    {
                        change.edgesToCreate.Remove(duplicateEdges[i]);
                    }
                }
            }

            if (change.elementsToRemove != null)
            {
                HashSet<string> removedStateIds = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> removedLayerIds = new HashSet<string>(StringComparer.Ordinal);
                HashSet<string> removedScopePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                for (int i = 0, count = change.elementsToRemove.Count; i < count; ++i)
                {
                    if (change.elementsToRemove[i] is Node node)
                    {
                        string stateId = node.userData as string;
                        if (stateId == FusionAnimatorGraphAsset.SpecialNodeEntryId ||
                            stateId == FusionAnimatorGraphAsset.SpecialNodeAnyId ||
                            stateId == FusionAnimatorGraphAsset.SpecialNodeExitId)
                        {
                            continue;
                        }

                        if (allowNodeRemoval == false)
                        {
                            // Ignore non-explicit node removals; these can be emitted by UI churn
                            // and must never mutate authoring data.
                            continue;
                        }

                        if (_layerNodeLayerIdById.TryGetValue(stateId, out string removedLayerId) &&
                            string.IsNullOrWhiteSpace(removedLayerId) == false)
                        {
                            removedLayerIds.Add(removedLayerId);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(stateId) == false &&
                            stateId.StartsWith("__scope__:", StringComparison.Ordinal))
                        {
                            string removedScopePath = NormalizeScopePath(stateId.Substring("__scope__:".Length));
                            if (string.IsNullOrWhiteSpace(removedScopePath) == false)
                            {
                                removedScopePaths.Add(removedScopePath);
                            }

                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(stateId) == false)
                        {
                            removedStateIds.Add(stateId);
                        }
                    }
                }

                if (removedLayerIds.Count > 0)
                {
                    EnsureUndo();

                    for (int i = 0; i < _graph.States.Count; ++i)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[i];
                        if (state == null || string.IsNullOrWhiteSpace(state.LayerId))
                        {
                            continue;
                        }

                        if (removedLayerIds.Contains(state.LayerId))
                        {
                            removedStateIds.Add(state.Id);
                        }
                    }

                    int beforeLayers = _graph.Layers.Count;
                    _graph.Layers.RemoveAll(layer => layer == null || removedLayerIds.Contains(layer.Id));
                    if (_graph.Layers.Count != beforeLayers)
                    {
                        for (int i = 0; i < _graph.Layers.Count; ++i)
                        {
                            FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                            if (layer != null)
                            {
                                layer.Priority = i;
                            }
                        }

                        dirty = true;
                        forceRebuild = true;
                    }

                    if (_graph.ScopeUtilityNodeLayouts != null)
                    {
                        int beforeLayouts = _graph.ScopeUtilityNodeLayouts.Count;
                        _graph.ScopeUtilityNodeLayouts.RemoveAll(layout =>
                            layout == null ||
                            (string.IsNullOrWhiteSpace(layout.LayerId) == false && removedLayerIds.Contains(layout.LayerId)));
                        if (_graph.ScopeUtilityNodeLayouts.Count != beforeLayouts)
                        {
                            dirty = true;
                        }
                    }

                    if (_graph.ScopeTransitionSuppressions != null)
                    {
                        int beforeSuppressions = _graph.ScopeTransitionSuppressions.Count;
                        _graph.ScopeTransitionSuppressions.RemoveAll(suppression =>
                            suppression == null ||
                            (string.IsNullOrWhiteSpace(suppression.LayerId) == false && removedLayerIds.Contains(suppression.LayerId)));
                        if (_graph.ScopeTransitionSuppressions.Count != beforeSuppressions)
                        {
                            dirty = true;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_activeLayerId) == false && removedLayerIds.Contains(_activeLayerId))
                    {
                        _activeLayerId = string.Empty;
                        _scopeFilter = string.Empty;
                        forceRebuild = true;
                    }
                }

                if (removedScopePaths.Count > 0 && string.IsNullOrWhiteSpace(_activeLayerId) == false)
                {
                    EnsureUndo();

                    for (int i = 0; i < _graph.States.Count; ++i)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[i];
                        if (state == null ||
                            string.Equals(state.LayerId, _activeLayerId, StringComparison.Ordinal) == false)
                        {
                            continue;
                        }

                        if (IsScopePathDeleted(GetStateScopePath(state.Name), removedScopePaths))
                        {
                            removedStateIds.Add(state.Id);
                        }
                    }

                    if (_graph.ScopeUtilityNodeLayouts != null)
                    {
                        int beforeLayouts = _graph.ScopeUtilityNodeLayouts.Count;
                        _graph.ScopeUtilityNodeLayouts.RemoveAll(layout =>
                            layout == null ||
                            (string.Equals(layout.LayerId, _activeLayerId, StringComparison.Ordinal) &&
                             IsScopePathDeleted(layout.ScopePath, removedScopePaths)));
                        if (_graph.ScopeUtilityNodeLayouts.Count != beforeLayouts)
                        {
                            dirty = true;
                        }
                    }

                    if (_graph.ScopeTransitionSuppressions != null)
                    {
                        int beforeSuppressions = _graph.ScopeTransitionSuppressions.Count;
                        _graph.ScopeTransitionSuppressions.RemoveAll(suppression =>
                            suppression == null ||
                            (string.Equals(suppression.LayerId, _activeLayerId, StringComparison.Ordinal) &&
                             IsScopePathDeleted(suppression.ScopePath, removedScopePaths)));
                        if (_graph.ScopeTransitionSuppressions.Count != beforeSuppressions)
                        {
                            dirty = true;
                        }
                    }

                    if (removedStateIds.Count > 0)
                    {
                        dirty = true;
                        forceRebuild = true;
                    }
                }

                if (removedStateIds.Count > 0)
                {
                    EnsureUndo();
                    HashSet<string> removedScopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    for (int i = 0; i < _graph.States.Count; ++i)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[i];
                        if (state == null || removedStateIds.Contains(state.Id) == false || IsScopeSentinelState(state))
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(state.LayerId) == false && removedLayerIds.Contains(state.LayerId))
                        {
                            continue;
                        }

                        string removedScopePath = NormalizeScopePath(GetStateScopePath(state.Name));
                        if (string.IsNullOrWhiteSpace(removedScopePath))
                        {
                            continue;
                        }

                        if (IsScopePathDeleted(removedScopePath, removedScopePaths))
                        {
                            continue;
                        }

                        removedScopeKeys.Add((state.LayerId ?? string.Empty) + "|" + removedScopePath);
                    }

                    foreach (string removedScopeKey in removedScopeKeys)
                    {
                        int separator = removedScopeKey.IndexOf('|');
                        if (separator <= 0 || separator >= removedScopeKey.Length - 1)
                        {
                            continue;
                        }

                        string layerId = removedScopeKey.Substring(0, separator);
                        string scopePath = removedScopeKey.Substring(separator + 1);
                        if (GetOrCreateScopeUtilityNodeLayout(layerId, scopePath) != null)
                        {
                            dirty = true;
                        }

                        if (GetOrCreateScopeSentinelState(layerId, scopePath) != null)
                        {
                            dirty = true;
                        }
                    }

                    bool removedEntryState = false;
                    _graph.States.RemoveAll(state =>
                    {
                        bool remove = state == null || removedStateIds.Contains(state.Id);
                        if (remove &&
                            state != null &&
                            string.Equals(_graph.EntryStateId, state.Id, StringComparison.Ordinal))
                        {
                            removedEntryState = true;
                        }

                        return remove;
                    });
                    _graph.Transitions.RemoveAll(transition =>
                        transition == null ||
                        removedStateIds.Contains(transition.FromStateId) ||
                        removedStateIds.Contains(transition.ToStateId));

                    if (_graph.ScopeTransitionSuppressions != null)
                    {
                        _graph.ScopeTransitionSuppressions.RemoveAll(suppression =>
                            suppression == null ||
                            string.IsNullOrWhiteSpace(suppression.TransitionId) ||
                            _graph.Transitions.Any(transition =>
                                transition != null &&
                                string.Equals(transition.Id, suppression.TransitionId, StringComparison.Ordinal)) == false);
                    }

                    if (removedEntryState)
                    {
                        _graph.EntryStateId = string.Empty;
                    }

                    foreach (string stateId in removedStateIds)
                    {
                        _stateViews.Remove(stateId);
                    }

                    dirty = true;
                }

                for (int i = 0, count = change.elementsToRemove.Count; i < count; ++i)
                {
                    if (change.elementsToRemove[i] is Edge edge)
                    {
                        string transitionId = edge.userData as string;
                        string edgeFromNodeId = edge.output?.node?.userData as string;
                        string edgeToNodeId = edge.input?.node?.userData as string;
                        bool scopeProxyEdge = IsScopeNodeId(edgeFromNodeId) || IsScopeNodeId(edgeToNodeId);
                        bool hasReplacementEdge = false;
                        if (string.IsNullOrWhiteSpace(transitionId) == false)
                        {
                            foreach (GraphElement existingElement in graphElements)
                            {
                                if (existingElement is Edge existingEdge &&
                                    ReferenceEquals(existingEdge, edge) == false &&
                                    string.Equals(existingEdge.userData as string, transitionId, StringComparison.Ordinal))
                                {
                                    hasReplacementEdge = true;
                                    break;
                                }
                            }
                        }

                        if (hasReplacementEdge)
                        {
                            _programmaticEdgeRemovalIds.Remove(transitionId);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(transitionId) == false &&
                            _edgeViews.TryGetValue(transitionId, out Edge mappedEdge) &&
                            mappedEdge != null &&
                            ReferenceEquals(mappedEdge, edge) == false)
                        {
                            _programmaticEdgeRemovalIds.Remove(transitionId);
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(transitionId) == false && _programmaticEdgeRemovalIds.Remove(transitionId))
                        {
                            _edgeViews.Remove(transitionId);
                            continue;
                        }

                        if (scopeProxyEdge &&
                            string.IsNullOrWhiteSpace(transitionId) == false &&
                            string.Equals(transitionId, EntryLinkEdgeId, StringComparison.Ordinal) == false)
                        {
                            EnsureUndo();
                            if (AddTransitionSuppressionForCurrentScope(transitionId))
                            {
                                dirty = true;
                            }

                            _edgeViews.Remove(transitionId);
                            forceRebuild = true;
                            continue;
                        }

                        if (transitionId == EntryLinkEdgeId)
                        {
                            EnsureUndo();
                            _graph.EntryStateId = string.Empty;
                            dirty = true;
                            forceRebuild = true;
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(transitionId))
                        {
                            continue;
                        }

                        FusionAnimatorTransitionDefinition transitionToRemove = FindTransitionById(transitionId);
                        EnsureUndo();
                        int before = _graph.Transitions.Count;
                        _graph.Transitions.RemoveAll(transition => transition != null && transition.Id == transitionId);
                        if (_graph.Transitions.Count != before)
                        {
                            if (transitionToRemove != null &&
                                string.Equals(transitionToRemove.FromStateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal))
                            {
                                FusionAnimatorStateDefinition destination = FindState(transitionToRemove.ToStateId);
                                if (destination != null && string.IsNullOrWhiteSpace(GetStateScopePath(destination.Name)) &&
                                    string.Equals(_graph.EntryStateId, destination.Id, StringComparison.Ordinal))
                                {
                                    _graph.EntryStateId = string.Empty;
                                }
                            }

                            _edgeViews.Remove(transitionId);
                            dirty = true;
                        }
                    }
                }
            }

            if (dirty)
            {
                if (forceRebuild)
                {
                    RebuildFromGraphData();
                }

                OnGraphDirty?.Invoke();
                RefreshTransitionBadges();
                ApplySearchFilter();
            }

            return change;
        }

        private void HandleSelectionChanged(List<ISelectable> selectedElements)
        {
            if (_graph == null || selectedElements == null || selectedElements.Count == 0)
            {
                OnSelectionChanged?.Invoke(null, null);
                return;
            }

            if (selectedElements.Count != 1)
            {
                OnSelectionChanged?.Invoke(null, null);
                return;
            }

            for (int i = 0; i < selectedElements.Count; ++i)
            {
                if (selectedElements[i] is Edge selectedEdge)
                {
                    string transitionId = selectedEdge.userData as string;
                    OnSelectionChanged?.Invoke(null, FindTransitionById(transitionId));
                    return;
                }
            }

            for (int i = 0; i < selectedElements.Count; ++i)
            {
                if (selectedElements[i] is Node node)
                {
                    string nodeId = node.userData as string;
                    if (string.IsNullOrWhiteSpace(nodeId) == false && _layerNodeLayerIdById.TryGetValue(nodeId, out string layerId))
                    {
                        OnSelectionChanged?.Invoke(null, null);
                        OnLayerNodeSelected?.Invoke(layerId);
                        return;
                    }

                    OnSelectionChanged?.Invoke(FindState(nodeId), null);
                    return;
                }
            }

            OnSelectionChanged?.Invoke(null, null);
        }

        private Vector2 ResolveSpecialNodeAnchor()
        {
            if (_graph == null || _graph.States == null || _graph.States.Count == 0)
            {
                return Vector2.zero;
            }

            bool hasAny = false;
            float minX = 0.0f;
            float minY = 0.0f;
            float maxY = 0.0f;
            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state == null)
                {
                    continue;
                }

                Vector2 p = state.NodePosition;
                if (hasAny == false)
                {
                    minX = p.x;
                    minY = p.y;
                    maxY = p.y;
                    hasAny = true;
                    continue;
                }

                minX = Mathf.Min(minX, p.x);
                minY = Mathf.Min(minY, p.y);
                maxY = Mathf.Max(maxY, p.y);
            }

            if (hasAny == false)
            {
                return Vector2.zero;
            }

            return new Vector2(minX, (minY + maxY) * 0.5f);
        }

        private Vector2 ResolveSpecialNodePositionForCurrentScope(string specialNodeId, Vector2 scopeAnchor)
        {
            if (string.IsNullOrWhiteSpace(specialNodeId))
            {
                return scopeAnchor;
            }

            if (string.IsNullOrWhiteSpace(_activeLayerId) == false &&
                TryGetScopeUtilityNodeLayout(_activeLayerId, _scopeFilter, out FusionAnimatorScopeUtilityNodeLayout scopedLayout))
            {
                return GetSpecialNodePosition(scopedLayout, specialNodeId, scopeAnchor);
            }

            // Backward compatibility for existing assets authored before scoped utility-node persistence.
            if (string.IsNullOrWhiteSpace(_activeLayerId) == false &&
                string.IsNullOrWhiteSpace(NormalizeScopePath(_scopeFilter)))
            {
                return ResolveRootSpecialNodePosition(specialNodeId, scopeAnchor);
            }

            return ResolveDefaultSpecialNodePosition(specialNodeId, scopeAnchor);
        }

        private Vector2 ResolveRootSpecialNodePosition(string specialNodeId, Vector2 fallbackAnchor)
        {
            if (_graph == null)
            {
                return ResolveDefaultSpecialNodePosition(specialNodeId, fallbackAnchor);
            }

            Vector2 configuredPosition;
            switch (specialNodeId)
            {
                case FusionAnimatorGraphAsset.SpecialNodeEntryId:
                    configuredPosition = _graph.EntryNodePosition;
                    break;
                case FusionAnimatorGraphAsset.SpecialNodeAnyId:
                    configuredPosition = _graph.AnyNodePosition;
                    break;
                case FusionAnimatorGraphAsset.SpecialNodeExitId:
                    configuredPosition = _graph.ExitNodePosition;
                    break;
                default:
                    return fallbackAnchor;
            }

            if (configuredPosition != Vector2.zero)
            {
                return configuredPosition;
            }

            return ResolveDefaultSpecialNodePosition(specialNodeId, fallbackAnchor);
        }

        private static Vector2 ResolveDefaultSpecialNodePosition(string specialNodeId, Vector2 anchor)
        {
            switch (specialNodeId)
            {
                case FusionAnimatorGraphAsset.SpecialNodeEntryId:
                    return anchor + EntryNodeOffset;
                case FusionAnimatorGraphAsset.SpecialNodeAnyId:
                    return anchor + AnyNodeOffset;
                case FusionAnimatorGraphAsset.SpecialNodeExitId:
                    return anchor + ExitNodeOffset;
                default:
                    return anchor;
            }
        }

        private bool SetSpecialNodePositionForCurrentScope(string specialNodeId, Vector2 position)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(specialNodeId))
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(_activeLayerId) == false)
            {
                Vector2 scopeAnchor = ResolveSpecialNodeAnchor();
                Vector2 resolvedPosition = ResolveSpecialNodePositionForCurrentScope(specialNodeId, scopeAnchor);
                if (IsPositionNearlyEqual(resolvedPosition, position))
                {
                    return false;
                }

                FusionAnimatorScopeUtilityNodeLayout scopedLayout = GetOrCreateScopeUtilityNodeLayout(_activeLayerId, _scopeFilter);
                if (scopedLayout == null)
                {
                    return false;
                }

                SetSpecialNodePosition(scopedLayout, specialNodeId, position);
                return true;
            }

            switch (specialNodeId)
            {
                case FusionAnimatorGraphAsset.SpecialNodeEntryId:
                    if (IsPositionNearlyEqual(_graph.EntryNodePosition, position))
                    {
                        return false;
                    }
                    _graph.EntryNodePosition = position;
                    return true;
                case FusionAnimatorGraphAsset.SpecialNodeAnyId:
                    if (IsPositionNearlyEqual(_graph.AnyNodePosition, position))
                    {
                        return false;
                    }
                    _graph.AnyNodePosition = position;
                    return true;
                case FusionAnimatorGraphAsset.SpecialNodeExitId:
                    if (IsPositionNearlyEqual(_graph.ExitNodePosition, position))
                    {
                        return false;
                    }
                    _graph.ExitNodePosition = position;
                    return true;
                default:
                    return false;
            }
        }

        private bool SetScopeNodePosition(string layerId, string scopePath, Vector2 position)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalizedScope))
            {
                return false;
            }

            FusionAnimatorScopeUtilityNodeLayout layout = GetOrCreateScopeUtilityNodeLayout(layerId, normalizedScope);
            if (layout == null)
            {
                return false;
            }

            bool changed = layout.HasScopeNodePosition == false || IsPositionNearlyEqual(layout.ScopeNodePosition, position) == false;
            layout.HasScopeNodePosition = true;
            layout.ScopeNodePosition = position;
            return changed;
        }

        private bool TryGetScopeUtilityNodeLayout(string layerId, string scopePath, out FusionAnimatorScopeUtilityNodeLayout layout)
        {
            layout = null;
            if (_graph == null || _graph.ScopeUtilityNodeLayouts == null || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            for (int i = 0; i < _graph.ScopeUtilityNodeLayouts.Count; ++i)
            {
                FusionAnimatorScopeUtilityNodeLayout candidate = _graph.ScopeUtilityNodeLayouts[i];
                if (candidate == null || string.IsNullOrWhiteSpace(candidate.LayerId))
                {
                    continue;
                }

                if (string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string candidateScope = NormalizeScopePath(candidate.ScopePath);
                if (string.Equals(candidateScope, normalizedScope, StringComparison.OrdinalIgnoreCase))
                {
                    layout = candidate;
                    return true;
                }
            }

            return false;
        }

        private FusionAnimatorScopeUtilityNodeLayout GetOrCreateScopeUtilityNodeLayout(string layerId, string scopePath)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            if (_graph.ScopeUtilityNodeLayouts == null)
            {
                _graph.ScopeUtilityNodeLayouts = new List<FusionAnimatorScopeUtilityNodeLayout>();
            }

            if (TryGetScopeUtilityNodeLayout(layerId, scopePath, out FusionAnimatorScopeUtilityNodeLayout existing))
            {
                existing.ScopePath = NormalizeScopePath(existing.ScopePath);
                return existing;
            }

            string normalizedScope = NormalizeScopePath(scopePath);
            Vector2 anchor = ResolveSpecialNodeAnchor();
            FusionAnimatorScopeUtilityNodeLayout created = new FusionAnimatorScopeUtilityNodeLayout
            {
                LayerId = layerId,
                ScopePath = normalizedScope,
                EntryNodePosition = ResolveDefaultSpecialNodePosition(FusionAnimatorGraphAsset.SpecialNodeEntryId, anchor),
                AnyNodePosition = ResolveDefaultSpecialNodePosition(FusionAnimatorGraphAsset.SpecialNodeAnyId, anchor),
                ExitNodePosition = ResolveDefaultSpecialNodePosition(FusionAnimatorGraphAsset.SpecialNodeExitId, anchor),
            };

            _graph.ScopeUtilityNodeLayouts.Add(created);
            return created;
        }

        private static Vector2 GetSpecialNodePosition(FusionAnimatorScopeUtilityNodeLayout layout, string specialNodeId, Vector2 fallbackAnchor)
        {
            if (layout == null)
            {
                return ResolveDefaultSpecialNodePosition(specialNodeId, fallbackAnchor);
            }

            switch (specialNodeId)
            {
                case FusionAnimatorGraphAsset.SpecialNodeEntryId:
                    return layout.EntryNodePosition;
                case FusionAnimatorGraphAsset.SpecialNodeAnyId:
                    return layout.AnyNodePosition;
                case FusionAnimatorGraphAsset.SpecialNodeExitId:
                    return layout.ExitNodePosition;
                default:
                    return fallbackAnchor;
            }
        }

        private static void SetSpecialNodePosition(FusionAnimatorScopeUtilityNodeLayout layout, string specialNodeId, Vector2 position)
        {
            if (layout == null || string.IsNullOrWhiteSpace(specialNodeId))
            {
                return;
            }

            switch (specialNodeId)
            {
                case FusionAnimatorGraphAsset.SpecialNodeEntryId:
                    layout.EntryNodePosition = position;
                    break;
                case FusionAnimatorGraphAsset.SpecialNodeAnyId:
                    layout.AnyNodePosition = position;
                    break;
                case FusionAnimatorGraphAsset.SpecialNodeExitId:
                    layout.ExitNodePosition = position;
                    break;
            }
        }

        private void AddTransitionEdge(string edgeId, FusionAnimatorTransitionDefinition transition, StateNodeView fromView, StateNodeView toView)
        {
            if (fromView?.Output == null || toView?.Input == null)
            {
                return;
            }

            Edge edge = fromView.Output.ConnectTo(toView.Input);
            edge.userData = edgeId;
            if (transition != null)
            {
                ConfigureEdgeVisual(edge, transition);
                _edgeViews[edgeId] = edge;
            }
            else
            {
                edge.tooltip = string.Empty;
                Color edgeColor = new Color(0.42f, 0.82f, 1.0f, 1.0f);
                if (edge.edgeControl != null)
                {
                    edge.edgeControl.inputColor = edgeColor;
                    edge.edgeControl.outputColor = edgeColor;
                    edge.edgeControl.MarkDirtyRepaint();
                }
            }

            AddElement(edge);
        }

        private StateNodeView CreateSpecialNodeView(
            string id,
            string title,
            string tooltip,
            bool hasInput,
            bool hasOutput,
            Vector2 nodePosition,
            Color tint)
        {
            Node node = new Node();
            node.userData = id;
            node.title = title;
            node.tooltip = tooltip;
            node.capabilities |= Capabilities.Selectable | Capabilities.Movable | Capabilities.Snappable;
            node.style.backgroundColor = new Color(tint.r * 0.24f, tint.g * 0.24f, tint.b * 0.24f, 0.95f);
            node.style.borderLeftColor = tint;
            node.style.borderRightColor = tint;
            node.style.borderTopColor = tint;
            node.style.borderBottomColor = tint;
            node.style.borderLeftWidth = 2.0f;
            node.style.borderRightWidth = 2.0f;
            node.style.borderTopWidth = 2.0f;
            node.style.borderBottomWidth = 2.0f;

            Label runtimeBadge = CreateRuntimeBadgeLabel();
            node.titleContainer.Add(runtimeBadge);

            Port input = null;
            if (hasInput)
            {
                input = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
                input.portName = string.Empty;
                node.inputContainer.Add(input);
            }

            Port output = null;
            if (hasOutput)
            {
                output = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                output.portName = string.Empty;
                node.outputContainer.Add(output);
            }

            node.RefreshPorts();
            node.RefreshExpandedState();
            node.SetPosition(new Rect(nodePosition, new Vector2(180.0f, 76.0f)));

            return new StateNodeView
            {
                Node = node,
                Input = input,
                Output = output,
                RuntimeBadge = runtimeBadge,
            };
        }

        private StateNodeView CreateStateNodeView(FusionAnimatorStateDefinition state, bool isDefaultState = false)
        {
            Node node = new Node();
            node.userData = state.Id;
            string leafStateName = GetStateLeafName(state.Name);
            node.title = string.IsNullOrWhiteSpace(leafStateName) ? "State" : leafStateName;
            node.tooltip = string.Format("State: {0}\nLayer: {1}\nMotion: {2}\nCan Transition Out: {3}", state.Name, GetLayerDisplayName(state.LayerId), state.MotionType, state.CanTransitionOut);
            node.capabilities |= Capabilities.Movable | Capabilities.Deletable | Capabilities.Selectable | Capabilities.Copiable;
            if (isDefaultState)
            {
                ApplyDefaultOutline(node);
            }

            Label runtimeBadge = CreateRuntimeBadgeLabel();
            node.titleContainer.Add(runtimeBadge);

            Port input = node.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(bool));
            input.portName = string.Empty;
            input.tooltip = "Incoming transitions into this state.";
            node.inputContainer.Add(input);

            Port output = null;
            if (state.CanTransitionOut)
            {
                output = node.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, typeof(bool));
                output.portName = string.Empty;
                output.tooltip = "Outgoing transitions from this state.";
                node.outputContainer.Add(output);
            }
            else
            {
                Label noExit = new Label("No Exit");
                noExit.tooltip = "Can Transition Out is disabled for this state.";
                noExit.style.unityFontStyleAndWeight = FontStyle.Italic;
                noExit.style.fontSize = 10.0f;
                noExit.style.color = new Color(0.65f, 0.65f, 0.65f, 1.0f);
                node.outputContainer.Add(noExit);
            }

            Label layerLabel = new Label(GetLayerDisplayName(state.LayerId));
            layerLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            layerLabel.style.fontSize = 10.0f;
            layerLabel.style.color = new Color(0.67f, 0.67f, 0.67f, 1.0f);
            node.extensionContainer.Add(layerLabel);

            Label motionLabel = new Label(GetMotionDisplayName(state));
            motionLabel.style.unityFontStyleAndWeight = FontStyle.Italic;
            motionLabel.style.fontSize = 10.0f;
            motionLabel.style.color = new Color(0.62f, 0.84f, 0.98f, 1.0f);
            node.extensionContainer.Add(motionLabel);

            VisualElement blendTreeSummary = new VisualElement();
            blendTreeSummary.style.marginTop = 4.0f;
            blendTreeSummary.style.paddingLeft = 4.0f;
            blendTreeSummary.style.paddingRight = 4.0f;
            blendTreeSummary.style.paddingTop = 3.0f;
            blendTreeSummary.style.paddingBottom = 3.0f;
            blendTreeSummary.style.backgroundColor = new Color(0.09f, 0.14f, 0.19f, 0.9f);
            blendTreeSummary.style.borderLeftWidth = 1.0f;
            blendTreeSummary.style.borderRightWidth = 1.0f;
            blendTreeSummary.style.borderTopWidth = 1.0f;
            blendTreeSummary.style.borderBottomWidth = 1.0f;
            blendTreeSummary.style.borderLeftColor = new Color(0.36f, 0.6f, 0.78f, 0.9f);
            blendTreeSummary.style.borderRightColor = new Color(0.36f, 0.6f, 0.78f, 0.9f);
            blendTreeSummary.style.borderTopColor = new Color(0.36f, 0.6f, 0.78f, 0.9f);
            blendTreeSummary.style.borderBottomColor = new Color(0.36f, 0.6f, 0.78f, 0.9f);
            node.extensionContainer.Add(blendTreeSummary);
            RefreshBlendTreeSummary(blendTreeSummary, state);

            node.RefreshPorts();
            node.RefreshExpandedState();
            float nodeHeight = state.MotionType == FusionAnimatorMotionType.BlendTree ? 180.0f : 130.0f;
            node.SetPosition(new Rect(state.NodePosition, new Vector2(220.0f, nodeHeight)));

            return new StateNodeView
            {
                Node = node,
                Input = input,
                Output = output,
                LayerLabel = layerLabel,
                MotionLabel = motionLabel,
                BlendTreeSummary = blendTreeSummary,
                RuntimeBadge = runtimeBadge,
            };
        }

        private static Label CreateRuntimeBadgeLabel()
        {
            Label badge = new Label();
            badge.style.display = DisplayStyle.None;
            badge.style.fontSize = 8.0f;
            badge.style.unityFontStyleAndWeight = FontStyle.Bold;
            badge.style.color = new Color(0.07f, 0.08f, 0.09f, 1.0f);
            badge.style.marginLeft = 6.0f;
            badge.style.paddingLeft = 4.0f;
            badge.style.paddingRight = 4.0f;
            badge.style.paddingTop = 1.0f;
            badge.style.paddingBottom = 1.0f;
            badge.style.borderTopLeftRadius = 3.0f;
            badge.style.borderTopRightRadius = 3.0f;
            badge.style.borderBottomLeftRadius = 3.0f;
            badge.style.borderBottomRightRadius = 3.0f;
            badge.style.backgroundColor = new Color(0.32f, 0.78f, 0.40f, 0.95f);
            return badge;
        }

        private static void ApplyDefaultOutline(Node node)
        {
            if (node == null)
            {
                return;
            }

            Color defaultTint = new Color(0.95f, 0.58f, 0.16f, 1.0f);
            node.style.borderLeftColor = defaultTint;
            node.style.borderRightColor = defaultTint;
            node.style.borderTopColor = defaultTint;
            node.style.borderBottomColor = defaultTint;
            node.style.borderLeftWidth = 2.0f;
            node.style.borderRightWidth = 2.0f;
            node.style.borderTopWidth = 2.0f;
            node.style.borderBottomWidth = 2.0f;
        }

        private bool TryResolveTransitionEndpointForCreate(string nodeId, out string resolvedStateId)
        {
            resolvedStateId = null;
            if (string.IsNullOrWhiteSpace(nodeId))
            {
                return false;
            }

            if (nodeId == FusionAnimatorGraphAsset.SpecialNodeEntryId ||
                nodeId == FusionAnimatorGraphAsset.SpecialNodeAnyId ||
                nodeId == FusionAnimatorGraphAsset.SpecialNodeExitId)
            {
                resolvedStateId = nodeId;
                return true;
            }

            if (nodeId.StartsWith("__scope__:", StringComparison.Ordinal))
            {
                string scopePath = nodeId.Substring("__scope__:".Length);
                FusionAnimatorStateDefinition sentinelState = FindScopeSentinelState(_activeLayerId, scopePath);
                if (sentinelState != null)
                {
                    resolvedStateId = sentinelState.Id;
                    return true;
                }

                return TryGetScopeDefaultStateId(_activeLayerId, scopePath, out resolvedStateId);
            }

            if (FindState(nodeId) != null)
            {
                resolvedStateId = nodeId;
                return true;
            }

            return false;
        }

        private bool TryGetScopeDefaultStateId(string layerId, string scopePath, out string stateId)
        {
            stateId = null;
            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            string normalizedScope = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath.Trim();
            if (string.IsNullOrWhiteSpace(normalizedScope) &&
                string.IsNullOrWhiteSpace(_graph.EntryStateId) == false)
            {
                FusionAnimatorStateDefinition rootDefault = FindState(_graph.EntryStateId);
                if (rootDefault != null && string.Equals(rootDefault.LayerId, layerId, StringComparison.Ordinal))
                {
                    stateId = rootDefault.Id;
                    return true;
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

                    FusionAnimatorStateDefinition destinationState = FindState(transition.ToStateId);
                    if (destinationState == null || string.Equals(destinationState.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string destinationScope = GetStateScopePath(destinationState.Name);
                    if (string.Equals(destinationScope, normalizedScope, StringComparison.OrdinalIgnoreCase))
                    {
                        stateId = destinationState.Id;
                        return true;
                    }
                }
            }

            return false;
        }

        private FusionAnimatorStateDefinition FindFirstStateInScope(string layerId, string scopePath)
        {
            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            string normalizedScope = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath.Trim();
            FusionAnimatorStateDefinition best = null;
            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string stateScope = GetStateScopePath(state.Name);
                if (string.Equals(stateScope, normalizedScope, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                if (best == null || string.Compare(state.Name, best.Name, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    best = state;
                }
            }

            return best;
        }

        private bool IsTransitionEndpointsValid(string fromStateId, string toStateId)
        {
            if (string.IsNullOrWhiteSpace(fromStateId) || string.IsNullOrWhiteSpace(toStateId))
            {
                return false;
            }

            if (fromStateId == FusionAnimatorGraphAsset.SpecialNodeExitId)
            {
                return false;
            }

            if (toStateId == FusionAnimatorGraphAsset.SpecialNodeEntryId || toStateId == FusionAnimatorGraphAsset.SpecialNodeAnyId)
            {
                return false;
            }

            if (fromStateId == FusionAnimatorGraphAsset.SpecialNodeEntryId || fromStateId == FusionAnimatorGraphAsset.SpecialNodeAnyId)
            {
                if (fromStateId == FusionAnimatorGraphAsset.SpecialNodeEntryId)
                {
                    return FindState(toStateId) != null;
                }

                return toStateId == FusionAnimatorGraphAsset.SpecialNodeExitId || FindState(toStateId) != null;
            }

            FusionAnimatorStateDefinition fromState = FindState(fromStateId);
            if (fromState == null || fromState.CanTransitionOut == false)
            {
                return false;
            }

            if (toStateId == FusionAnimatorGraphAsset.SpecialNodeExitId)
            {
                return true;
            }

            return FindState(toStateId) != null;
        }

        private FusionAnimatorStateDefinition FindState(string stateId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(stateId) || _graph.States == null)
            {
                return null;
            }

            for (int i = 0, count = _graph.States.Count; i < count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state != null && state.Id == stateId)
                {
                    return state;
                }
            }

            return null;
        }

        private FusionAnimatorTransitionDefinition FindTransitionById(string transitionId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(transitionId) || _graph.Transitions == null)
            {
                return null;
            }

            for (int i = 0, count = _graph.Transitions.Count; i < count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition != null && transition.Id == transitionId)
                {
                    return transition;
                }
            }

            return null;
        }

        private FusionAnimatorTransitionDefinition FindTransitionByEndpoints(string fromStateId, string toStateId)
        {
            if (_graph == null || _graph.Transitions == null)
            {
                return null;
            }

            for (int i = 0, count = _graph.Transitions.Count; i < count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition == null)
                {
                    continue;
                }

                if (transition.FromStateId == fromStateId && transition.ToStateId == toStateId)
                {
                    return transition;
                }
            }

            return null;
        }

        private FusionAnimatorLayerDefinition FindLayerById(string layerId)
        {
            if (_graph == null || _graph.Layers == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && string.Equals(layer.Id, layerId, StringComparison.Ordinal))
                {
                    return layer;
                }
            }

            return null;
        }

        private FusionAnimatorLayerDefinition FindLayerByName(string layerName)
        {
            if (_graph == null || _graph.Layers == null || string.IsNullOrWhiteSpace(layerName))
            {
                return null;
            }

            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && string.Equals(layer.Name, layerName, StringComparison.OrdinalIgnoreCase))
                {
                    return layer;
                }
            }

            return null;
        }

        private string ResolveLayerNameById(string layerId)
        {
            FusionAnimatorLayerDefinition layer = FindLayerById(layerId);
            return layer != null ? layer.Name : string.Empty;
        }

        private FusionAnimatorParameterDefinition FindParameterById(string parameterId)
        {
            if (_graph == null || _graph.Parameters == null || string.IsNullOrWhiteSpace(parameterId))
            {
                return null;
            }

            if (FusionAnimatorParameterReferenceUtility.TryParse(parameterId, out string baseParameterId, out _) == false)
            {
                baseParameterId = parameterId;
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

        private FusionAnimatorParameterDefinition FindParameterByNameAndType(string parameterName, FusionAnimatorParameterType parameterType)
        {
            if (_graph == null || _graph.Parameters == null || string.IsNullOrWhiteSpace(parameterName))
            {
                return null;
            }

            for (int i = 0; i < _graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter == null)
                {
                    continue;
                }

                if (parameter.Type == parameterType &&
                    string.Equals(parameter.Name, parameterName, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter;
                }
            }

            return null;
        }

        private static ClipboardLayer FindClipboardLayerById(List<ClipboardLayer> layers, string layerId)
        {
            if (layers == null || string.IsNullOrWhiteSpace(layerId))
            {
                return null;
            }

            for (int i = 0; i < layers.Count; ++i)
            {
                ClipboardLayer layer = layers[i];
                if (layer != null && string.Equals(layer.Id, layerId, StringComparison.Ordinal))
                {
                    return layer;
                }
            }

            return null;
        }

        private static string FindClipboardStateLayerName(List<ClipboardState> states, string layerId)
        {
            if (states == null || string.IsNullOrWhiteSpace(layerId))
            {
                return string.Empty;
            }

            for (int i = 0; i < states.Count; ++i)
            {
                ClipboardState state = states[i];
                if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(state.LayerName) == false)
                {
                    return state.LayerName;
                }
            }

            return string.Empty;
        }

        private static ClipboardParameter FindClipboardParameterById(List<ClipboardParameter> parameters, string parameterId)
        {
            if (parameters == null || string.IsNullOrWhiteSpace(parameterId))
            {
                return null;
            }

            for (int i = 0; i < parameters.Count; ++i)
            {
                ClipboardParameter parameter = parameters[i];
                if (parameter != null && string.Equals(parameter.Id, parameterId, StringComparison.Ordinal))
                {
                    return parameter;
                }
            }

            return null;
        }

        private string GetLayerDisplayName(string layerId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(layerId) || _graph.Layers == null)
            {
                return "Layer: <none>";
            }

            for (int i = 0, count = _graph.Layers.Count; i < count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && layer.Id == layerId)
                {
                    return string.Format("Layer: {0}", layer.Name);
                }
            }

            return string.Format("Layer: {0}", layerId);
        }

        private string GetLayerNameById(string layerId)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(layerId) || _graph.Layers == null)
            {
                return string.Empty;
            }

            for (int i = 0, count = _graph.Layers.Count; i < count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && layer.Id == layerId)
                {
                    return string.IsNullOrWhiteSpace(layer.Name) ? string.Empty : layer.Name.ToLowerInvariant();
                }
            }

            return string.Empty;
        }

        private void EnsurePreviewRenderer()
        {
            if (_previewRenderUtility != null)
            {
                return;
            }

            _previewRenderUtility = new PreviewRenderUtility();
            _previewRenderUtility.cameraFieldOfView = 26.0f;
            _previewRenderUtility.camera.clearFlags = CameraClearFlags.SolidColor;
            _previewRenderUtility.camera.backgroundColor = new Color(0.11f, 0.11f, 0.11f, 1.0f);
            _previewRenderUtility.camera.allowHDR = true;
            _previewRenderUtility.camera.allowMSAA = true;
            _previewRenderUtility.camera.nearClipPlane = 0.01f;
            _previewRenderUtility.camera.farClipPlane = 200.0f;
            _previewRenderUtility.lights[0].intensity = 1.1f;
            _previewRenderUtility.lights[0].transform.rotation = Quaternion.Euler(35.0f, 35.0f, 0.0f);
            _previewRenderUtility.lights[1].intensity = 1.0f;
            _previewRenderUtility.lights[1].transform.rotation = Quaternion.Euler(340.0f, 218.0f, 177.0f);
            _previewRenderUtility.ambientColor = new Color(0.35f, 0.35f, 0.35f, 1.0f);
            EnsurePreviewHdrpDiffusionProfiles();
        }

        private void EnsurePreviewRenderInstance(GameObject sourceTarget)
        {
            if (_previewRenderUtility == null || sourceTarget == null)
            {
                return;
            }

            bool targetChanged = _previewRenderInstance == null ||
                                 _previewRenderSource == null ||
                                 ReferenceEquals(_previewRenderSource, sourceTarget) == false;

            if (targetChanged == false)
            {
                return;
            }

            if (_previewRenderInstance != null)
            {
                UnityEngine.Object.DestroyImmediate(_previewRenderInstance);
                _previewRenderInstance = null;
            }

            _previewRenderSource = sourceTarget;
            _previewRenderInstance = UnityEngine.Object.Instantiate(sourceTarget);
            _previewRenderInstance.hideFlags = HideFlags.HideAndDontSave;
            _previewRenderInstance.name = "FusionAnimatorPreviewInstance";
            _previewRenderInstance.transform.position = Vector3.zero;
            _previewRenderInstance.transform.rotation = Quaternion.identity;
            _previewRenderInstance.transform.localScale = Vector3.one;
            _previewFocusAnchor = Vector3.zero;
            _previewFocusAnchorInitialized = false;
            _previewLastBoundsRadius = 1.0f;
            CapturePreviewRootBindings(_previewRenderInstance);
            ApplyPreviewRootMotion(_previewRenderInstance);
            _previewRenderUtility.AddSingleGO(_previewRenderInstance);
            _previewBlendSourceInstanceId = 0;
        }

        private void EnsurePreviewRenderTexture()
        {
            int width = Mathf.Max(64, Mathf.RoundToInt(layout.width));
            int height = Mathf.Max(64, Mathf.RoundToInt(layout.height));

            if (_previewRenderTexture != null &&
                _previewRenderTexture.width == width &&
                _previewRenderTexture.height == height)
            {
                return;
            }

            if (_previewRenderTexture != null)
            {
                _previewRenderTexture.Release();
                UnityEngine.Object.DestroyImmediate(_previewRenderTexture);
            }

            RenderTextureFormat format = RenderTextureFormat.ARGB32;
            if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.DefaultHDR))
            {
                format = RenderTextureFormat.DefaultHDR;
            }
            else if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.ARGBHalf))
            {
                format = RenderTextureFormat.ARGBHalf;
            }

            _previewRenderTexture = new RenderTexture(width, height, 24, format)
            {
                hideFlags = HideFlags.HideAndDontSave,
                name = "FusionAnimatorPreviewRT",
                useMipMap = false,
                autoGenerateMips = false,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            _previewRenderTexture.Create();
        }

        private GameObject _previewHdrpVolumeObject;
        private Component _previewHdrpVolumeComponent;
        private ScriptableObject _previewHdrpVolumeProfile;

        private void EnsurePreviewHdrpDiffusionProfiles()
        {
            if (_previewRenderUtility == null)
            {
                return;
            }

            Type volumeType = ResolveRenderingVolumeType();
            Type volumeProfileType = ResolveRenderingVolumeProfileType();
            if (volumeType == null || volumeProfileType == null)
            {
                return;
            }

            Type diffusionProfileListType = ResolveHdrpDiffusionProfileListType();
            if (diffusionProfileListType == null)
            {
                return;
            }

            System.Array diffusionProfiles = CollectHdrpDiffusionProfilesFromScene(diffusionProfileListType);
            if (diffusionProfiles == null || diffusionProfiles.Length == 0)
            {
                return;
            }

            if (_previewHdrpVolumeObject == null)
            {
                _previewHdrpVolumeObject = new GameObject("FusionAnimatorPreviewHDRPVolume");
                _previewHdrpVolumeObject.hideFlags = HideFlags.HideAndDontSave;
                _previewHdrpVolumeComponent = _previewHdrpVolumeObject.AddComponent(volumeType);
                SetReflectedMemberValue(_previewHdrpVolumeComponent, "isGlobal", true);
                SetReflectedMemberValue(_previewHdrpVolumeComponent, "priority", 10000.0f);
                SetReflectedMemberValue(_previewHdrpVolumeComponent, "weight", 1.0f);
                SetReflectedMemberValue(_previewHdrpVolumeComponent, "blendDistance", 0.0f);
                _previewHdrpVolumeProfile = ScriptableObject.CreateInstance(volumeProfileType);
                _previewHdrpVolumeProfile.hideFlags = HideFlags.HideAndDontSave;
                SetReflectedMemberValue(_previewHdrpVolumeComponent, "sharedProfile", _previewHdrpVolumeProfile);
                _previewRenderUtility.AddSingleGO(_previewHdrpVolumeObject);
            }

            if (_previewHdrpVolumeProfile == null)
            {
                return;
            }

            IList components = GetReflectedMemberValue(_previewHdrpVolumeProfile, "components") as IList;
            if (components == null)
            {
                return;
            }

            object diffusionComponent = null;
            for (int i = 0, count = components.Count; i < count; ++i)
            {
                object component = components[i];
                if (component != null && component.GetType() == diffusionProfileListType)
                {
                    diffusionComponent = component;
                    break;
                }
            }

            if (diffusionComponent == null)
            {
                diffusionComponent = AddVolumeProfileComponent(_previewHdrpVolumeProfile, diffusionProfileListType);
            }

            if (diffusionComponent == null)
            {
                return;
            }

            AssignHdrpDiffusionProfiles(diffusionComponent, diffusionProfiles);
        }

        private static Type ResolveHdrpDiffusionProfileListType()
        {
            Type type = Type.GetType("UnityEngine.Rendering.HighDefinition.DiffusionProfileList, Unity.RenderPipelines.HighDefinition.Runtime");
            if (type != null)
            {
                return type;
            }

            return Type.GetType("UnityEngine.Rendering.HighDefinition.DiffusionProfileList, Unity.RenderPipelines.HighDefinition");
        }

        private static Type ResolveRenderingVolumeType()
        {
            Type type = Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core.Runtime");
            if (type != null)
            {
                return type;
            }

            return Type.GetType("UnityEngine.Rendering.Volume, Unity.RenderPipelines.Core");
        }

        private static Type ResolveRenderingVolumeProfileType()
        {
            Type type = Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core.Runtime");
            if (type != null)
            {
                return type;
            }

            return Type.GetType("UnityEngine.Rendering.VolumeProfile, Unity.RenderPipelines.Core");
        }

        private static object GetReflectedMemberValue(object instance, string memberName)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanRead)
            {
                return property.GetValue(instance, null);
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                return field.GetValue(instance);
            }

            return null;
        }

        private static bool SetReflectedMemberValue(object instance, string memberName, object value)
        {
            if (instance == null || string.IsNullOrWhiteSpace(memberName))
            {
                return false;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type type = instance.GetType();

            PropertyInfo property = type.GetProperty(memberName, flags);
            if (property != null && property.CanWrite)
            {
                property.SetValue(instance, value, null);
                return true;
            }

            FieldInfo field = type.GetField(memberName, flags);
            if (field != null)
            {
                field.SetValue(instance, value);
                return true;
            }

            return false;
        }

        private static object AddVolumeProfileComponent(ScriptableObject volumeProfile, Type componentType)
        {
            if (volumeProfile == null || componentType == null)
            {
                return null;
            }

            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Type profileType = volumeProfile.GetType();

            MethodInfo addWithOverride = profileType.GetMethod("Add", flags, null, new[] { typeof(Type), typeof(bool) }, null);
            if (addWithOverride != null)
            {
                return addWithOverride.Invoke(volumeProfile, new object[] { componentType, true });
            }

            MethodInfo addSimple = profileType.GetMethod("Add", flags, null, new[] { typeof(Type) }, null);
            if (addSimple != null)
            {
                return addSimple.Invoke(volumeProfile, new object[] { componentType });
            }

            return null;
        }

        private static System.Array CollectHdrpDiffusionProfilesFromScene(Type diffusionProfileListType)
        {
            if (diffusionProfileListType == null)
            {
                return null;
            }

            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            System.Reflection.FieldInfo parameterField = diffusionProfileListType.GetField("diffusionProfiles", flags);
            if (parameterField == null)
            {
                return null;
            }

            List<UnityEngine.Object> collectedProfiles = new List<UnityEngine.Object>();
            HashSet<int> profileIds = new HashSet<int>();
            Type elementType = null;

            Type volumeType = ResolveRenderingVolumeType();
            if (volumeType == null)
            {
                return null;
            }

            UnityEngine.Object[] volumes = Resources.FindObjectsOfTypeAll(volumeType);
            for (int i = 0, count = volumes != null ? volumes.Length : 0; i < count; ++i)
            {
                UnityEngine.Object volumeObject = volumes[i];
                if (volumeObject == null || EditorUtility.IsPersistent(volumeObject))
                {
                    continue;
                }

                ScriptableObject sharedProfile = GetReflectedMemberValue(volumeObject, "sharedProfile") as ScriptableObject;
                if (sharedProfile == null)
                {
                    continue;
                }

                IList components = GetReflectedMemberValue(sharedProfile, "components") as IList;
                if (components == null)
                {
                    continue;
                }

                for (int c = 0, componentCount = components.Count; c < componentCount; ++c)
                {
                    object component = components[c];
                    if (component == null || component.GetType() != diffusionProfileListType)
                    {
                        continue;
                    }

                    object parameter = parameterField.GetValue(component);
                    if (parameter == null)
                    {
                        continue;
                    }

                    Type parameterType = parameter.GetType();
                    FieldInfo valueField = parameterType.GetField("value", flags);
                    object valuesObject = valueField != null ? valueField.GetValue(parameter) : null;
                    if (valuesObject == null)
                    {
                        PropertyInfo valueProperty = parameterType.GetProperty("value", flags);
                        if (valueProperty != null && valueProperty.CanRead)
                        {
                            valuesObject = valueProperty.GetValue(parameter, null);
                        }
                    }

                    System.Array values = valuesObject as System.Array;
                    if (values == null)
                    {
                        continue;
                    }

                    if (elementType == null)
                    {
                        elementType = values.GetType().GetElementType();
                    }

                    for (int p = 0, profileCount = values.Length; p < profileCount; ++p)
                    {
                        UnityEngine.Object profileObject = values.GetValue(p) as UnityEngine.Object;
                        if (profileObject == null)
                        {
                            continue;
                        }

                        int profileId = profileObject.GetInstanceID();
                        if (profileIds.Add(profileId))
                        {
                            collectedProfiles.Add(profileObject);
                        }
                    }
                }
            }

            if (collectedProfiles.Count == 0 || elementType == null)
            {
                return null;
            }

            System.Array result = System.Array.CreateInstance(elementType, collectedProfiles.Count);
            for (int i = 0; i < collectedProfiles.Count; ++i)
            {
                result.SetValue(collectedProfiles[i], i);
            }

            return result;
        }

        private static void AssignHdrpDiffusionProfiles(object component, System.Array diffusionProfiles)
        {
            if (component == null || diffusionProfiles == null)
            {
                return;
            }

            System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic;
            Type componentType = component.GetType();
            FieldInfo parameterField = componentType.GetField("diffusionProfiles", flags);
            if (parameterField == null)
            {
                return;
            }

            object parameter = parameterField.GetValue(component);
            if (parameter == null)
            {
                return;
            }

            Type parameterType = parameter.GetType();
            FieldInfo overrideStateField = parameterType.GetField("overrideState", flags);
            if (overrideStateField != null && overrideStateField.FieldType == typeof(bool))
            {
                overrideStateField.SetValue(parameter, true);
            }

            FieldInfo valueField = parameterType.GetField("value", flags);
            if (valueField != null && valueField.FieldType.IsArray)
            {
                Type targetElementType = valueField.FieldType.GetElementType();
                System.Array converted = diffusionProfiles;
                if (converted.GetType() != valueField.FieldType)
                {
                    converted = System.Array.CreateInstance(targetElementType, diffusionProfiles.Length);
                    for (int i = 0; i < diffusionProfiles.Length; ++i)
                    {
                        object value = diffusionProfiles.GetValue(i);
                        if (value != null && targetElementType.IsInstanceOfType(value))
                        {
                            converted.SetValue(value, i);
                        }
                    }
                }

                valueField.SetValue(parameter, converted);
                return;
            }

            PropertyInfo valueProperty = parameterType.GetProperty("value", flags);
            if (valueProperty != null && valueProperty.CanWrite && valueProperty.PropertyType.IsArray)
            {
                Type targetElementType = valueProperty.PropertyType.GetElementType();
                System.Array converted = diffusionProfiles;
                if (converted.GetType() != valueProperty.PropertyType)
                {
                    converted = System.Array.CreateInstance(targetElementType, diffusionProfiles.Length);
                    for (int i = 0; i < diffusionProfiles.Length; ++i)
                    {
                        object value = diffusionProfiles.GetValue(i);
                        if (value != null && targetElementType.IsInstanceOfType(value))
                        {
                            converted.SetValue(value, i);
                        }
                    }
                }

                valueProperty.SetValue(parameter, converted, null);
            }
        }

        private void ConfigurePreviewCamera(GameObject renderRoot)
        {
            if (_previewRenderUtility == null || renderRoot == null)
            {
                return;
            }

            Vector3 currentCenter = renderRoot.transform.position;
            float currentRadius = Mathf.Max(0.35f, _previewLastBoundsRadius);
            Renderer[] renderers = renderRoot.GetComponentsInChildren<Renderer>(true);
            if (renderers != null && renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; ++i)
                {
                    bounds.Encapsulate(renderers[i].bounds);
                }

                currentCenter = bounds.center;
                currentRadius = Mathf.Max(0.35f, bounds.extents.magnitude);
            }

            if (_previewFocusAnchorInitialized == false)
            {
                _previewFocusAnchor = currentCenter;
                _previewFocusAnchorInitialized = true;
                _previewLastBoundsRadius = currentRadius;
            }
            else
            {
                _previewLastBoundsRadius = Mathf.Max(_previewLastBoundsRadius, currentRadius);
            }

            float focusRadius = Mathf.Max(0.35f, _previewLastBoundsRadius);
            Vector3 focusPoint = _previewFocusAnchor + _previewOrbitTargetOffset + new Vector3(0.0f, focusRadius * 0.05f, 0.0f);
            Quaternion orbitRotation = Quaternion.Euler(_previewOrbitPitch, _previewOrbitYaw, 0.0f);
            Vector3 orbitDirection = orbitRotation * Vector3.back;
            float orbitDistance = focusRadius * _previewOrbitDistanceScale;
            _previewRenderUtility.camera.transform.position = focusPoint + orbitDirection * orbitDistance;
            _previewRenderUtility.camera.transform.LookAt(focusPoint);
            _previewRenderUtility.camera.nearClipPlane = 0.01f;
            _previewRenderUtility.camera.farClipPlane = Mathf.Max(20.0f, focusRadius * 12.0f);
            UpdatePreviewCameraGizmoVisual();
        }

        private void ApplyPreviewRootMotion(GameObject renderRoot)
        {
            if (renderRoot == null)
            {
                return;
            }

            Animator[] animators = renderRoot.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; ++i)
            {
                Animator animator = animators[i];
                if (animator != null)
                {
                    animator.applyRootMotion = _previewApplyRootMotion;
                }
            }
        }

        private void CapturePreviewRootBindings(GameObject renderRoot)
        {
            _previewRootBindings.Clear();
            if (renderRoot == null)
            {
                return;
            }

            HashSet<Transform> trackedTransforms = new HashSet<Transform>();
            TryAddPreviewRootBinding(renderRoot.transform, trackedTransforms);

            Animator[] animators = renderRoot.GetComponentsInChildren<Animator>(true);
            for (int i = 0; i < animators.Length; ++i)
            {
                Animator animator = animators[i];
                if (animator == null)
                {
                    continue;
                }

                TryAddAnimatorRootMotionBindings(animator, trackedTransforms);
            }
        }

        private void TryAddAnimatorRootMotionBindings(Animator animator, ISet<Transform> trackedTransforms)
        {
            if (animator == null || trackedTransforms == null)
            {
                return;
            }

            TryAddPreviewRootBinding(animator.transform, trackedTransforms);

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            TryAddPreviewRootBinding(hips, trackedTransforms);
            Transform cursor = hips != null ? hips.parent : null;
            while (cursor != null)
            {
                TryAddPreviewRootBinding(cursor, trackedTransforms);
                if (cursor == animator.transform)
                {
                    break;
                }

                cursor = cursor.parent;
            }

            TryAddPreviewRootBinding(ResolveAnimatorSkeletonRoot(animator), trackedTransforms);
        }

        private void TryAddPreviewRootBinding(Transform transform, ISet<Transform> trackedTransforms)
        {
            if (transform == null || trackedTransforms == null || trackedTransforms.Add(transform) == false)
            {
                return;
            }

            _previewRootBindings.Add(new PreviewRootBinding
            {
                Transform = transform,
                LocalPosition = transform.localPosition,
                LocalRotation = transform.localRotation,
                LocalScale = transform.localScale,
            });
        }

        private static Transform ResolveAnimatorSkeletonRoot(Animator animator)
        {
            if (animator == null)
            {
                return null;
            }

            Transform hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            if (hips == null)
            {
                return null;
            }

            Transform skeletonRoot = hips;
            while (skeletonRoot.parent != null && skeletonRoot.parent != animator.transform)
            {
                skeletonRoot = skeletonRoot.parent;
            }

            return skeletonRoot;
        }

        private void ApplyPreviewRootMotionPolicy()
        {
            if (_previewApplyRootMotion || _previewRootBindings.Count == 0)
            {
                return;
            }

            for (int i = 0; i < _previewRootBindings.Count; ++i)
            {
                PreviewRootBinding binding = _previewRootBindings[i];
                if (binding == null || binding.Transform == null)
                {
                    continue;
                }

                binding.Transform.localPosition = binding.LocalPosition;
            }
        }

        private void EnsureBlendBuffers()
        {
            _previewBlendTransforms.Clear();
            if (_previewRenderInstance == null)
            {
                _previewBlendPositions = null;
                _previewBlendRotations = null;
                _previewBlendScales = null;
                _previewBlendAccumulatedPositions = null;
                _previewBlendAccumulatedRotations = null;
                _previewBlendAccumulatedScales = null;
                _previewBlendSourceInstanceId = 0;
                _previewBlendTransformPaths = null;
                _previewBlendBasePositions = null;
                _previewBlendBaseRotations = null;
                _previewBlendBaseScales = null;
                _previewBlendLayerPositions = null;
                _previewBlendLayerRotations = null;
                _previewBlendLayerScales = null;
                _previewBlendCompositePositions = null;
                _previewBlendCompositeRotations = null;
                _previewBlendCompositeScales = null;
                _previewAvatarMaskWeights.Clear();
                return;
            }

            int currentInstanceId = _previewRenderInstance.GetInstanceID();
            bool instanceChanged = _previewBlendSourceInstanceId != currentInstanceId;
            if (instanceChanged)
            {
                _previewBlendSourceInstanceId = currentInstanceId;
                _previewBlendTransformPaths = null;
                _previewBlendBasePositions = null;
                _previewBlendBaseRotations = null;
                _previewBlendBaseScales = null;
                _previewBlendLayerPositions = null;
                _previewBlendLayerRotations = null;
                _previewBlendLayerScales = null;
                _previewBlendCompositePositions = null;
                _previewBlendCompositeRotations = null;
                _previewBlendCompositeScales = null;
                _previewAvatarMaskWeights.Clear();
            }

            Transform[] transforms = _previewRenderInstance.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < transforms.Length; ++i)
            {
                if (transforms[i] != null)
                {
                    _previewBlendTransforms.Add(transforms[i]);
                }
            }

            int count = _previewBlendTransforms.Count;
            if (_previewBlendPositions == null || _previewBlendPositions.Length != count)
            {
                _previewBlendPositions = new Vector3[count];
                _previewBlendRotations = new Quaternion[count];
                _previewBlendScales = new Vector3[count];
                _previewBlendAccumulatedPositions = new Vector3[count];
                _previewBlendAccumulatedRotations = new Vector4[count];
                _previewBlendAccumulatedScales = new Vector3[count];
            }

            if (_previewBlendTransformPaths == null || _previewBlendTransformPaths.Length != count)
            {
                _previewBlendTransformPaths = new string[count];
                _previewAvatarMaskWeights.Clear();
            }

            for (int i = 0; i < count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                if (transform == null || _previewRenderInstance == null)
                {
                    _previewBlendTransformPaths[i] = string.Empty;
                    continue;
                }

                string path = AnimationUtility.CalculateTransformPath(transform, _previewRenderInstance.transform);
                _previewBlendTransformPaths[i] = NormalizeAvatarMaskPath(path);
            }

            bool basePoseNeedsCapture = instanceChanged ||
                                        _previewBlendBasePositions == null ||
                                        _previewBlendBasePositions.Length != count;
            if (EnsureLayerStackBuffers())
            {
                if (basePoseNeedsCapture)
                {
                    CaptureBasePoseFromTransforms();
                }
            }
        }

        private void CaptureLocalPose()
        {
            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                _previewBlendPositions[i] = transform.localPosition;
                _previewBlendRotations[i] = transform.localRotation;
                _previewBlendScales[i] = transform.localScale;
            }
        }

        private void ApplyLocalPoseBlend(float blendAlpha)
        {
            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                transform.localPosition = Vector3.Lerp(_previewBlendPositions[i], transform.localPosition, blendAlpha);
                transform.localRotation = Quaternion.Slerp(_previewBlendRotations[i], transform.localRotation, blendAlpha);
                transform.localScale = Vector3.Lerp(_previewBlendScales[i], transform.localScale, blendAlpha);
            }
        }

        private bool SampleWeightedLocalPose(IList<AnimationClip> clips, IList<float> sampleTimes, IList<float> sampleWeights)
        {
            if (_previewRenderInstance == null || clips == null || sampleTimes == null || sampleWeights == null)
            {
                return false;
            }

            int transformCount = _previewBlendTransforms.Count;
            if (transformCount == 0 ||
                _previewBlendAccumulatedPositions == null ||
                _previewBlendAccumulatedRotations == null ||
                _previewBlendAccumulatedScales == null ||
                _previewBlendAccumulatedPositions.Length != transformCount ||
                _previewBlendAccumulatedRotations.Length != transformCount ||
                _previewBlendAccumulatedScales.Length != transformCount)
            {
                return false;
            }

            int sampleCount = Mathf.Min(clips.Count, sampleTimes.Count, sampleWeights.Count);
            if (sampleCount <= 0)
            {
                return false;
            }

            for (int i = 0; i < transformCount; ++i)
            {
                _previewBlendAccumulatedPositions[i] = Vector3.zero;
                _previewBlendAccumulatedRotations[i] = Vector4.zero;
                _previewBlendAccumulatedScales[i] = Vector3.zero;
            }

            float totalWeight = 0.0f;
            bool hasSample = false;

            for (int i = 0; i < sampleCount; ++i)
            {
                AnimationClip clip = clips[i];
                if (clip == null)
                {
                    continue;
                }

                float weight = sampleWeights[i];
                if (weight <= 0.0f)
                {
                    continue;
                }

                if (_previewBlendBasePositions != null &&
                    _previewBlendBaseRotations != null &&
                    _previewBlendBaseScales != null &&
                    _previewBlendBasePositions.Length == transformCount &&
                    _previewBlendBaseRotations.Length == transformCount &&
                    _previewBlendBaseScales.Length == transformCount)
                {
                    ApplyStoredBasePose();
                }

                clip.SampleAnimation(_previewRenderInstance, sampleTimes[i]);
                AccumulateWeightedLocalPose(weight, hasSample == false);
                totalWeight += weight;
                hasSample = true;
            }

            if (hasSample == false || totalWeight <= 0.0f)
            {
                return false;
            }

            float inverseWeight = 1.0f / totalWeight;
            ApplyWeightedLocalPose(inverseWeight);
            return true;
        }

        private void AccumulateWeightedLocalPose(float sampleWeight, bool firstSample)
        {
            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                if (transform == null)
                {
                    continue;
                }

                _previewBlendAccumulatedPositions[i] += transform.localPosition * sampleWeight;
                _previewBlendAccumulatedScales[i] += transform.localScale * sampleWeight;

                Quaternion rotation = transform.localRotation;
                Vector4 rotationVector = new Vector4(rotation.x, rotation.y, rotation.z, rotation.w);
                if (firstSample == false && Vector4.Dot(_previewBlendAccumulatedRotations[i], rotationVector) < 0.0f)
                {
                    rotationVector = -rotationVector;
                }

                _previewBlendAccumulatedRotations[i] += rotationVector * sampleWeight;
            }
        }

        private void ApplyWeightedLocalPose(float inverseWeight)
        {
            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                if (transform == null)
                {
                    continue;
                }

                transform.localPosition = _previewBlendAccumulatedPositions[i] * inverseWeight;
                transform.localScale = _previewBlendAccumulatedScales[i] * inverseWeight;

                Vector4 accumulatedRotation = _previewBlendAccumulatedRotations[i] * inverseWeight;
                float magnitude = Mathf.Sqrt(
                    accumulatedRotation.x * accumulatedRotation.x +
                    accumulatedRotation.y * accumulatedRotation.y +
                    accumulatedRotation.z * accumulatedRotation.z +
                    accumulatedRotation.w * accumulatedRotation.w);

                if (magnitude > 0.0001f)
                {
                    float invMagnitude = 1.0f / magnitude;
                    transform.localRotation = new Quaternion(
                        accumulatedRotation.x * invMagnitude,
                        accumulatedRotation.y * invMagnitude,
                        accumulatedRotation.z * invMagnitude,
                        accumulatedRotation.w * invMagnitude);
                }
                else
                {
                    transform.localRotation = Quaternion.identity;
                }
            }
        }

        private bool EnsureLayerStackBuffers()
        {
            int transformCount = _previewBlendTransforms.Count;
            if (transformCount <= 0)
            {
                _previewBlendBasePositions = null;
                _previewBlendBaseRotations = null;
                _previewBlendBaseScales = null;
                _previewBlendLayerPositions = null;
                _previewBlendLayerRotations = null;
                _previewBlendLayerScales = null;
                _previewBlendCompositePositions = null;
                _previewBlendCompositeRotations = null;
                _previewBlendCompositeScales = null;
                return false;
            }

            if (_previewBlendBasePositions == null || _previewBlendBasePositions.Length != transformCount)
            {
                _previewBlendBasePositions = new Vector3[transformCount];
                _previewBlendBaseRotations = new Quaternion[transformCount];
                _previewBlendBaseScales = new Vector3[transformCount];
                _previewBlendLayerPositions = new Vector3[transformCount];
                _previewBlendLayerRotations = new Quaternion[transformCount];
                _previewBlendLayerScales = new Vector3[transformCount];
                _previewBlendCompositePositions = new Vector3[transformCount];
                _previewBlendCompositeRotations = new Quaternion[transformCount];
                _previewBlendCompositeScales = new Vector3[transformCount];
                _previewAvatarMaskWeights.Clear();
            }

            return true;
        }

        private void CaptureBasePoseFromTransforms()
        {
            if (_previewBlendBasePositions == null || _previewBlendBaseRotations == null || _previewBlendBaseScales == null)
            {
                return;
            }

            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                if (transform == null)
                {
                    _previewBlendBasePositions[i] = Vector3.zero;
                    _previewBlendBaseRotations[i] = Quaternion.identity;
                    _previewBlendBaseScales[i] = Vector3.one;
                    continue;
                }

                _previewBlendBasePositions[i] = transform.localPosition;
                _previewBlendBaseRotations[i] = transform.localRotation;
                _previewBlendBaseScales[i] = transform.localScale;
            }
        }

        private void ApplyStoredBasePose()
        {
            if (_previewBlendBasePositions == null || _previewBlendBaseRotations == null || _previewBlendBaseScales == null)
            {
                return;
            }

            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                if (transform == null)
                {
                    continue;
                }

                transform.localPosition = _previewBlendBasePositions[i];
                transform.localRotation = _previewBlendBaseRotations[i];
                transform.localScale = _previewBlendBaseScales[i];
            }
        }

        private void CopyBasePoseToCompositePose()
        {
            if (_previewBlendCompositePositions == null ||
                _previewBlendCompositeRotations == null ||
                _previewBlendCompositeScales == null ||
                _previewBlendBasePositions == null ||
                _previewBlendBaseRotations == null ||
                _previewBlendBaseScales == null)
            {
                return;
            }

            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                _previewBlendCompositePositions[i] = _previewBlendBasePositions[i];
                _previewBlendCompositeRotations[i] = _previewBlendBaseRotations[i];
                _previewBlendCompositeScales[i] = _previewBlendBaseScales[i];
            }
        }

        private void CaptureCurrentPoseToLayerPose()
        {
            if (_previewBlendLayerPositions == null || _previewBlendLayerRotations == null || _previewBlendLayerScales == null)
            {
                return;
            }

            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                if (transform == null)
                {
                    _previewBlendLayerPositions[i] = _previewBlendBasePositions != null && i < _previewBlendBasePositions.Length
                        ? _previewBlendBasePositions[i]
                        : Vector3.zero;
                    _previewBlendLayerRotations[i] = _previewBlendBaseRotations != null && i < _previewBlendBaseRotations.Length
                        ? _previewBlendBaseRotations[i]
                        : Quaternion.identity;
                    _previewBlendLayerScales[i] = _previewBlendBaseScales != null && i < _previewBlendBaseScales.Length
                        ? _previewBlendBaseScales[i]
                        : Vector3.one;
                    continue;
                }

                _previewBlendLayerPositions[i] = transform.localPosition;
                _previewBlendLayerRotations[i] = transform.localRotation;
                _previewBlendLayerScales[i] = transform.localScale;
            }
        }

        private void ApplyCompositePoseToTransforms()
        {
            if (_previewBlendCompositePositions == null || _previewBlendCompositeRotations == null || _previewBlendCompositeScales == null)
            {
                return;
            }

            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                if (transform == null)
                {
                    continue;
                }

                transform.localPosition = _previewBlendCompositePositions[i];
                transform.localRotation = _previewBlendCompositeRotations[i];
                transform.localScale = _previewBlendCompositeScales[i];
            }
        }

        private void ComposeLayerPose(FusionAnimatorLayerDefinition layer, float layerWeight, bool ignoreAvatarMask)
        {
            if (_previewBlendCompositePositions == null ||
                _previewBlendCompositeRotations == null ||
                _previewBlendCompositeScales == null ||
                _previewBlendLayerPositions == null ||
                _previewBlendLayerRotations == null ||
                _previewBlendLayerScales == null ||
                _previewBlendBasePositions == null ||
                _previewBlendBaseRotations == null ||
                _previewBlendBaseScales == null)
            {
                return;
            }

            float[] maskWeights = ignoreAvatarMask ? null : ResolveAvatarMaskWeights(layer != null ? layer.AvatarMask : null);
            bool additive = layer != null && layer.BlendMode == FusionAnimatorLayerBlendMode.Additive;
            for (int i = 0; i < _previewBlendTransforms.Count; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                bool rootLocked = IsPreviewRootLockedTransform(transform);

                float maskWeight = maskWeights != null && i < maskWeights.Length ? Mathf.Clamp01(maskWeights[i]) : 1.0f;
                float influence = layerWeight * maskWeight;
                if (influence <= 0.000001f)
                {
                    continue;
                }

                if (additive)
                {
                    Vector3 positionDelta = _previewBlendLayerPositions[i] - _previewBlendBasePositions[i];
                    Vector3 scaleDelta = _previewBlendLayerScales[i] - _previewBlendBaseScales[i];
                    Quaternion rotationDelta = _previewBlendLayerRotations[i] * Quaternion.Inverse(_previewBlendBaseRotations[i]);
                    if (rootLocked == false)
                    {
                        _previewBlendCompositePositions[i] += positionDelta * influence;
                    }

                    _previewBlendCompositeScales[i] += scaleDelta * influence;
                    _previewBlendCompositeRotations[i] *= Quaternion.Slerp(Quaternion.identity, rotationDelta, influence);
                }
                else
                {
                    if (rootLocked == false)
                    {
                        _previewBlendCompositePositions[i] = Vector3.Lerp(
                            _previewBlendCompositePositions[i],
                            _previewBlendLayerPositions[i],
                            influence);
                    }

                    _previewBlendCompositeScales[i] = Vector3.Lerp(
                        _previewBlendCompositeScales[i],
                        _previewBlendLayerScales[i],
                        influence);
                    _previewBlendCompositeRotations[i] = Quaternion.Slerp(
                        _previewBlendCompositeRotations[i],
                        _previewBlendLayerRotations[i],
                        influence);
                }
            }
        }

        private bool IsPreviewRootLockedTransform(Transform transform)
        {
            if (_previewApplyRootMotion || transform == null || _previewRootBindings.Count == 0)
            {
                return false;
            }

            for (int i = 0; i < _previewRootBindings.Count; ++i)
            {
                PreviewRootBinding binding = _previewRootBindings[i];
                if (binding != null && binding.Transform == transform)
                {
                    return true;
                }
            }

            return false;
        }

        private float[] ResolveAvatarMaskWeights(AvatarMask avatarMask)
        {
            if (avatarMask == null || _previewBlendTransforms.Count == 0)
            {
                return null;
            }

            int transformCount = _previewBlendTransforms.Count;
            if (_previewAvatarMaskWeights.TryGetValue(avatarMask, out float[] cached) &&
                cached != null &&
                cached.Length == transformCount)
            {
                return cached;
            }

            float[] weights = new float[transformCount];
            BuildAvatarMaskWeights(avatarMask, weights);
            _previewAvatarMaskWeights[avatarMask] = weights;
            return weights;
        }

        private void BuildAvatarMaskWeights(AvatarMask avatarMask, float[] targetWeights)
        {
            if (avatarMask == null || targetWeights == null || _previewBlendTransformPaths == null)
            {
                return;
            }

            int maskTransformCount = avatarMask.transformCount;
            if (maskTransformCount > 0)
            {
                // Prefer explicit transform paths first; they encode per-bone overrides
                // with higher fidelity than coarse humanoid body-part flags.
                Dictionary<string, bool> explicitPathStates = new Dictionary<string, bool>(maskTransformCount, StringComparer.OrdinalIgnoreCase);
                bool hasAnyActivePath = false;
                for (int i = 0; i < maskTransformCount; ++i)
                {
                    string path = NormalizeAvatarMaskPath(avatarMask.GetTransformPath(i));
                    bool isActive = avatarMask.GetTransformActive(i);
                    explicitPathStates[path] = isActive;
                    hasAnyActivePath |= isActive;
                }

                if (explicitPathStates.Count > 0)
                {
                    if (hasAnyActivePath == false)
                    {
                        Array.Clear(targetWeights, 0, targetWeights.Length);
                        return;
                    }

                    int resolvedPathCount = 0;
                    int activePathCount = 0;
                    for (int i = 0; i < targetWeights.Length; ++i)
                    {
                        string transformPath = _previewBlendTransformPaths[i] ?? string.Empty;
                        bool pathEnabled = IsPathEnabledByMask(transformPath, explicitPathStates, out bool hasResolvedPath);
                        if (hasResolvedPath)
                        {
                            resolvedPathCount++;
                            if (pathEnabled)
                            {
                                activePathCount++;
                            }

                            targetWeights[i] = pathEnabled ? 1.0f : 0.0f;
                        }
                        else
                        {
                            targetWeights[i] = float.NegativeInfinity;
                        }
                    }

                    if (activePathCount > 0)
                    {
                        if (resolvedPathCount < targetWeights.Length)
                        {
                            float[] humanoidFallbackWeights = new float[targetWeights.Length];
                            if (TryBuildHumanoidMaskWeights(avatarMask, humanoidFallbackWeights))
                            {
                                for (int i = 0; i < targetWeights.Length; ++i)
                                {
                                    if (float.IsNegativeInfinity(targetWeights[i]))
                                    {
                                        targetWeights[i] = humanoidFallbackWeights[i];
                                    }
                                }
                            }
                            else
                            {
                                for (int i = 0; i < targetWeights.Length; ++i)
                                {
                                    if (float.IsNegativeInfinity(targetWeights[i]))
                                    {
                                        targetWeights[i] = 0.0f;
                                    }
                                }
                            }
                        }

                        return;
                    }

                    // Explicit path data exists but could not resolve any active transform.
                    // Fall back to humanoid mapping for rigs whose transform paths differ.
                    if (TryBuildHumanoidMaskWeights(avatarMask, targetWeights))
                    {
                        return;
                    }

                    Array.Clear(targetWeights, 0, targetWeights.Length);
                    return;
                }
            }

            if (TryBuildHumanoidMaskWeights(avatarMask, targetWeights))
            {
                return;
            }

            if (maskTransformCount <= 0)
            {
                for (int i = 0; i < targetWeights.Length; ++i)
                {
                    targetWeights[i] = 1.0f;
                }
                return;
            }

            // Some rigs/masks can report transform entries that are unusable in preview.
            // Prefer visible fallback over muting the whole layer unexpectedly.
            for (int i = 0; i < targetWeights.Length; ++i)
            {
                targetWeights[i] = 1.0f;
            }
        }

        private static bool IsPathEnabledByMask(
            string transformPath,
            IDictionary<string, bool> explicitPathStates,
            out bool hasResolvedPath)
        {
            hasResolvedPath = false;
            if (explicitPathStates == null || explicitPathStates.Count == 0)
            {
                return false;
            }

            string normalizedPath = NormalizeAvatarMaskPath(transformPath);
            if (TryResolveMostSpecificExplicitPathState(explicitPathStates, normalizedPath, out bool resolvedState))
            {
                hasResolvedPath = true;
                return resolvedState;
            }

            return false;
        }

        private static bool TryResolveMostSpecificExplicitPathState(
            IDictionary<string, bool> explicitPathStates,
            string normalizedPath,
            out bool resolvedState)
        {
            resolvedState = false;
            if (explicitPathStates == null || explicitPathStates.Count == 0 || string.IsNullOrWhiteSpace(normalizedPath))
            {
                return false;
            }

            bool hasBestMatch = false;
            int bestSpecificity = -1;

            TryResolveCandidatePathState(
                explicitPathStates,
                normalizedPath,
                ref hasBestMatch,
                ref bestSpecificity,
                ref resolvedState);

            // Imported rigs can prepend extra root segments relative to AvatarMask paths.
            // Check suffixes, then keep the most specific resolved match.
            int searchStart = 0;
            while (searchStart >= 0 && searchStart < normalizedPath.Length)
            {
                int separatorIndex = normalizedPath.IndexOf('/', searchStart);
                if (separatorIndex < 0 || separatorIndex + 1 >= normalizedPath.Length)
                {
                    break;
                }

                string suffixPath = normalizedPath.Substring(separatorIndex + 1);
                TryResolveCandidatePathState(
                    explicitPathStates,
                    suffixPath,
                    ref hasBestMatch,
                    ref bestSpecificity,
                    ref resolvedState);

                searchStart = separatorIndex + 1;
            }

            return hasBestMatch;
        }

        private static void TryResolveCandidatePathState(
            IDictionary<string, bool> explicitPathStates,
            string candidatePath,
            ref bool hasBestMatch,
            ref int bestSpecificity,
            ref bool bestState)
        {
            if (explicitPathStates == null || explicitPathStates.Count == 0 || string.IsNullOrWhiteSpace(candidatePath))
            {
                return;
            }

            string currentPath = candidatePath;
            while (string.IsNullOrWhiteSpace(currentPath) == false)
            {
                if (explicitPathStates.TryGetValue(currentPath, out bool candidateState))
                {
                    int specificity = CountPathSegments(currentPath);
                    if (hasBestMatch == false || specificity > bestSpecificity)
                    {
                        hasBestMatch = true;
                        bestSpecificity = specificity;
                        bestState = candidateState;
                    }

                    return;
                }

                int separatorIndex = currentPath.LastIndexOf('/');
                if (separatorIndex <= 0)
                {
                    break;
                }

                currentPath = currentPath.Substring(0, separatorIndex);
            }
        }

        private static int CountPathSegments(string normalizedPath)
        {
            if (string.IsNullOrWhiteSpace(normalizedPath))
            {
                return 0;
            }

            int segmentCount = 1;
            for (int i = 0; i < normalizedPath.Length; ++i)
            {
                if (normalizedPath[i] == '/')
                {
                    ++segmentCount;
                }
            }

            return segmentCount;
        }

        private static string NormalizeAvatarMaskPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            return path.Trim().Trim('/');
        }

        private bool TryBuildHumanoidMaskWeights(AvatarMask avatarMask, float[] targetWeights)
        {
            if (avatarMask == null || targetWeights == null || _previewRenderInstance == null || _previewBlendTransforms.Count == 0)
            {
                return false;
            }

            Animator animator = _previewRenderInstance.GetComponentInChildren<Animator>(true);
            if (animator == null || animator.isHuman == false)
            {
                return false;
            }

            bool anyBodyPartDisabled = false;
            Array bodyParts = Enum.GetValues(typeof(AvatarMaskBodyPart));
            for (int i = 0; i < bodyParts.Length; ++i)
            {
                AvatarMaskBodyPart bodyPart = (AvatarMaskBodyPart)bodyParts.GetValue(i);
                if (bodyPart == AvatarMaskBodyPart.LastBodyPart)
                {
                    continue;
                }

                if (avatarMask.GetHumanoidBodyPartActive(bodyPart) == false)
                {
                    anyBodyPartDisabled = true;
                    break;
                }
            }

            if (anyBodyPartDisabled == false)
            {
                return false;
            }

            Dictionary<Transform, AvatarMaskBodyPart> bodyPartByBone = new Dictionary<Transform, AvatarMaskBodyPart>();
            RegisterHumanoidBone(animator, HumanBodyBones.Hips, AvatarMaskBodyPart.Body, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.Spine, AvatarMaskBodyPart.Body, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.Chest, AvatarMaskBodyPart.Body, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.UpperChest, AvatarMaskBodyPart.Body, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.Neck, AvatarMaskBodyPart.Head, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.Head, AvatarMaskBodyPart.Head, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.Jaw, AvatarMaskBodyPart.Head, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftEye, AvatarMaskBodyPart.Head, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightEye, AvatarMaskBodyPart.Head, bodyPartByBone);

            RegisterHumanoidBone(animator, HumanBodyBones.LeftUpperLeg, AvatarMaskBodyPart.LeftLeg, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftLowerLeg, AvatarMaskBodyPart.LeftLeg, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftFoot, AvatarMaskBodyPart.LeftLeg, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftToes, AvatarMaskBodyPart.LeftLeg, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightUpperLeg, AvatarMaskBodyPart.RightLeg, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightLowerLeg, AvatarMaskBodyPart.RightLeg, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightFoot, AvatarMaskBodyPart.RightLeg, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightToes, AvatarMaskBodyPart.RightLeg, bodyPartByBone);

            RegisterHumanoidBone(animator, HumanBodyBones.LeftShoulder, AvatarMaskBodyPart.LeftArm, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftUpperArm, AvatarMaskBodyPart.LeftArm, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftLowerArm, AvatarMaskBodyPart.LeftArm, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftHand, AvatarMaskBodyPart.LeftArm, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightShoulder, AvatarMaskBodyPart.RightArm, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightUpperArm, AvatarMaskBodyPart.RightArm, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightLowerArm, AvatarMaskBodyPart.RightArm, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightHand, AvatarMaskBodyPart.RightArm, bodyPartByBone);

            RegisterHumanoidBone(animator, HumanBodyBones.LeftThumbProximal, AvatarMaskBodyPart.LeftFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftIndexProximal, AvatarMaskBodyPart.LeftFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftMiddleProximal, AvatarMaskBodyPart.LeftFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftRingProximal, AvatarMaskBodyPart.LeftFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.LeftLittleProximal, AvatarMaskBodyPart.LeftFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightThumbProximal, AvatarMaskBodyPart.RightFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightIndexProximal, AvatarMaskBodyPart.RightFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightMiddleProximal, AvatarMaskBodyPart.RightFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightRingProximal, AvatarMaskBodyPart.RightFingers, bodyPartByBone);
            RegisterHumanoidBone(animator, HumanBodyBones.RightLittleProximal, AvatarMaskBodyPart.RightFingers, bodyPartByBone);

            for (int i = 0; i < targetWeights.Length; ++i)
            {
                Transform transform = _previewBlendTransforms[i];
                AvatarMaskBodyPart resolvedPart = ResolveHumanoidMaskBodyPart(transform, bodyPartByBone);
                targetWeights[i] = avatarMask.GetHumanoidBodyPartActive(resolvedPart) ? 1.0f : 0.0f;
            }

            return true;
        }

        private static void RegisterHumanoidBone(
            Animator animator,
            HumanBodyBones humanBone,
            AvatarMaskBodyPart bodyPart,
            Dictionary<Transform, AvatarMaskBodyPart> bodyPartByBone)
        {
            if (animator == null || bodyPartByBone == null)
            {
                return;
            }

            Transform boneTransform = animator.GetBoneTransform(humanBone);
            if (boneTransform == null)
            {
                return;
            }

            if (bodyPartByBone.ContainsKey(boneTransform) == false)
            {
                bodyPartByBone.Add(boneTransform, bodyPart);
            }
        }

        private static AvatarMaskBodyPart ResolveHumanoidMaskBodyPart(
            Transform transform,
            Dictionary<Transform, AvatarMaskBodyPart> bodyPartByBone)
        {
            for (Transform current = transform; current != null; current = current.parent)
            {
                if (bodyPartByBone != null &&
                    bodyPartByBone.TryGetValue(current, out AvatarMaskBodyPart resolvedPart))
                {
                    return resolvedPart;
                }
            }

            return AvatarMaskBodyPart.Root;
        }

        private void UpdatePreviewCameraGizmoVisual()
        {
            if (_previewCameraGizmoAxes == null || _previewAxisX == null || _previewAxisY == null || _previewAxisZ == null)
            {
                return;
            }

            float panRange = Mathf.Max(0.001f, _previewLastBoundsRadius * 1.75f);
            float panX = Mathf.Clamp(_previewOrbitTargetOffset.x / panRange, -1.0f, 1.0f) * 12.0f;
            float panY = Mathf.Clamp(_previewOrbitTargetOffset.y / panRange, -1.0f, 1.0f) * 12.0f;
            _previewCameraGizmoAxes.style.translate = new Translate(
                new Length(panX, LengthUnit.Pixel),
                new Length(-panY, LengthUnit.Pixel),
                0.0f);

            float scaleT = Mathf.InverseLerp(0.65f, 6.0f, _previewOrbitDistanceScale);
            float axisLength = Mathf.Lerp(24.0f, 16.0f, scaleT);
            Quaternion cameraRotation = Quaternion.Euler(_previewOrbitPitch, _previewOrbitYaw, 0.0f);
            Vector3 camRight = cameraRotation * Vector3.right;
            Vector3 camUp = cameraRotation * Vector3.up;
            Vector3 camForward = cameraRotation * Vector3.forward;

            SetProjectedAxis(_previewAxisX, Vector3.right, camRight, camUp, camForward, axisLength, new Color(0.88f, 0.28f, 0.28f, 0.95f));
            SetProjectedAxis(_previewAxisY, Vector3.up, camRight, camUp, camForward, axisLength, new Color(0.30f, 0.84f, 0.32f, 0.95f));
            SetProjectedAxis(_previewAxisZ, -Vector3.forward, camRight, camUp, camForward, axisLength, new Color(0.30f, 0.50f, 0.95f, 0.95f));
        }

        private static void SetProjectedAxis(
            VisualElement axisElement,
            Vector3 axisWorld,
            Vector3 cameraRight,
            Vector3 cameraUp,
            Vector3 cameraForward,
            float axisLength,
            Color baseColor)
        {
            if (axisElement == null)
            {
                return;
            }

            const float center = 39.0f;
            const float thickness = 2.0f;

            Vector2 projected = new Vector2(Vector3.Dot(axisWorld, cameraRight), Vector3.Dot(axisWorld, cameraUp));
            float projectedMagnitude = Mathf.Clamp01(projected.magnitude);
            Vector2 projectedDirection = projectedMagnitude > 0.0001f ? projected / projectedMagnitude : Vector2.right;
            float angle = -Mathf.Atan2(projectedDirection.y, projectedDirection.x) * Mathf.Rad2Deg;
            float depth = Vector3.Dot(axisWorld, cameraForward);
            float depthAlpha = Mathf.Lerp(0.42f, 1.0f, Mathf.InverseLerp(-1.0f, 1.0f, depth));
            float projectedLength = axisLength * projectedMagnitude;

            axisElement.style.left = center;
            axisElement.style.top = center - thickness * 0.5f;
            axisElement.style.width = projectedLength;
            axisElement.style.height = thickness;
            axisElement.style.transformOrigin = new TransformOrigin(new Length(0.0f, LengthUnit.Pixel), new Length(50.0f, LengthUnit.Percent), 0.0f);
            axisElement.style.rotate = new Rotate(new Angle(angle));
            axisElement.style.backgroundColor = new Color(baseColor.r, baseColor.g, baseColor.b, depthAlpha);
        }

        private static void RefreshBlendTreeSummary(VisualElement container, FusionAnimatorStateDefinition state)
        {
            if (container == null)
            {
                return;
            }

            container.Clear();
            bool show = state != null && state.MotionType == FusionAnimatorMotionType.BlendTree && state.BlendTree != null;
            container.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show == false)
            {
                return;
            }

            FusionAnimatorBlendTreeDefinition blendTree = state.BlendTree;
            Label title = new Label(string.Format("Tree: {0}", blendTree.Type));
            title.style.fontSize = 9.0f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.color = new Color(0.84f, 0.92f, 1.0f, 1.0f);
            container.Add(title);

            if (blendTree.Children == null || blendTree.Children.Count == 0)
            {
                Label empty = new Label("No children");
                empty.style.fontSize = 9.0f;
                empty.style.color = new Color(0.76f, 0.84f, 0.92f, 0.9f);
                container.Add(empty);
                return;
            }

            int displayed = 0;
            for (int i = 0; i < blendTree.Children.Count && displayed < 3; ++i)
            {
                FusionAnimatorBlendTreeChild child = blendTree.Children[i];
                if (child == null)
                {
                    continue;
                }

                Label row = new Label(string.Format("- {0}", string.IsNullOrWhiteSpace(child.Name) ? "Motion" : child.Name));
                row.style.fontSize = 8.0f;
                row.style.color = new Color(0.76f, 0.88f, 0.96f, 0.95f);
                container.Add(row);
                ++displayed;
            }

            if (blendTree.Children.Count > displayed)
            {
                Label more = new Label(string.Format("+{0} more", blendTree.Children.Count - displayed));
                more.style.fontSize = 8.0f;
                more.style.color = new Color(0.62f, 0.76f, 0.86f, 0.9f);
                container.Add(more);
            }
        }

        private static string GetMotionDisplayName(FusionAnimatorStateDefinition state)
        {
            if (state == null)
            {
                return "Motion: Clip";
            }

            if (state.MotionType == FusionAnimatorMotionType.BlendTree && state.BlendTree != null)
            {
                return string.Format("Motion: BlendTree ({0})", state.BlendTree.Type);
            }

            return "Motion: Clip";
        }

        private void CenterOnNode(Node node)
        {
            if (node == null || layout.width <= 1.0f || layout.height <= 1.0f)
            {
                return;
            }

            Rect nodeRect = node.GetPosition();
            Vector2 contentPoint = nodeRect.center;
            Vector3 currentScale = contentViewContainer.transform.scale;
            Vector2 viewportCenter = new Vector2(layout.width * 0.5f, layout.height * 0.5f);
            Vector3 nextPosition = new Vector3(
                viewportCenter.x - contentPoint.x * currentScale.x,
                viewportCenter.y - contentPoint.y * currentScale.y,
                0.0f);

            UpdateViewTransform(nextPosition, currentScale);
        }

        private bool SelectNode(Node node, bool center)
        {
            if (node == null)
            {
                return false;
            }

            ClearSelection();
            AddToSelection(node);
            if (center)
            {
                CenterOnNode(node);
                FrameSelection();
                schedule.Execute(() =>
                {
                    if (node.panel != null)
                    {
                        FrameSelection();
                    }
                }).ExecuteLater(1);
            }

            return true;
        }

        private Vector2 ResolveViewportCenterInContentSpace()
        {
            if (layout.width <= 0.0f || layout.height <= 0.0f)
            {
                return Vector2.zero;
            }

            Vector2 viewCenter = new Vector2(layout.width * 0.5f, layout.height * 0.5f);
            return ViewportToContentPosition(viewCenter);
        }

        private Vector2 ViewportToContentPosition(Vector2 viewportPosition)
        {
            Vector2 world = this.LocalToWorld(viewportPosition);
            return contentViewContainer.WorldToLocal(world);
        }

        private void EnsureGraphCollections()
        {
            if (_graph.Parameters == null)
            {
                _graph.Parameters = new List<FusionAnimatorParameterDefinition>();
            }

            if (_graph.ClipBindings == null)
            {
                _graph.ClipBindings = new List<FusionAnimatorClipBindingDefinition>();
            }
            else
            {
                for (int i = 0; i < _graph.ClipBindings.Count; ++i)
                {
                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[i];
                    binding?.MigrateLegacyClip();
                }
            }

            if (_graph.Layers == null)
            {
                _graph.Layers = new List<FusionAnimatorLayerDefinition>();
            }

            if (_graph.States == null)
            {
                _graph.States = new List<FusionAnimatorStateDefinition>();
            }

            if (_graph.Transitions == null)
            {
                _graph.Transitions = new List<FusionAnimatorTransitionDefinition>();
            }

            if (_graph.ScopeUtilityNodeLayouts == null)
            {
                _graph.ScopeUtilityNodeLayouts = new List<FusionAnimatorScopeUtilityNodeLayout>();
            }

            if (_graph.ScopeTransitionSuppressions == null)
            {
                _graph.ScopeTransitionSuppressions = new List<FusionAnimatorScopeTransitionSuppression>();
            }
        }
    }
}

