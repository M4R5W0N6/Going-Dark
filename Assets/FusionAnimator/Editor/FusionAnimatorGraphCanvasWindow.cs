using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FusionAnimator.Editor
{
    public sealed partial class FusionAnimatorGraphCanvasWindow : EditorWindow
    {
        private enum LeftLibraryTab
        {
            Layers = 0,
            Parameters = 1,
            Bindings = 2,
        }

        private FusionAnimatorGraphAsset _graph;
        private FusionAnimatorGraphView _graphView;
        private IMGUIContainer _inspector;
        private IMGUIContainer _leftPanel;
        private VisualElement _leftPanelRoot;
        private VisualElement _inspectorRoot;
        private VisualElement _graphHost;
        private VisualElement _leftResizeHandle;
        private VisualElement _rightResizeHandle;
        private VisualElement _emptyStateOverlay;
        private VisualElement _scopeBreadcrumbRoot;
        private ObjectField _graphField;
        private ObjectField _convertSourceField;
        private ToolbarSearchField _searchField;
        private ToolbarButton _scopeMenu;
        private ToolbarToggle _previewEnabledToggle;
        private ToolbarToggle _previewPlayToggle;
        private ToolbarToggle _previewMinimapToggle;
        private FloatField _previewSpeedField;
        private ObjectField _previewTargetField;
        private Vector2 _leftScroll;
        private Vector2 _inspectorScroll;

        private FusionAnimatorStateDefinition _selectedState;
        private FusionAnimatorTransitionDefinition _selectedTransition;
        private string _selectedEntryLinkTargetStateId = string.Empty;
        private int _selectedParameterIndex = -1;
        private int _selectedBindingIndex = -1;
        private int _selectedLayerIndex = -1;
        private LeftLibraryTab _leftLibraryTab = LeftLibraryTab.Parameters;
        private string _activeLayerId = string.Empty;
        private string _activeScopePath = string.Empty;
        private bool _isLeftPanelResizing;
        private bool _isRightPanelResizing;
        private float _resizeStartMouseX;
        private float _resizeStartWidth;
        private float _leftPanelWidth = LeftPanelMinWidth;
        private float _rightPanelWidth = RightPanelMinWidth;
        private readonly List<string> _scratchSelectedLayerIds = new List<string>(8);
        private readonly List<string> _scratchSelectedStateIds = new List<string>(16);
        private readonly List<string> _scratchSelectedScopePaths = new List<string>(8);
        private int _suppressGraphSelectionChangedDepth;

        private const float LeftPanelMinWidth = 300.0f;
        private const float LeftPanelMaxWidth = 760.0f;
        private const float RightPanelMinWidth = 360.0f;
        private const float RightPanelMaxWidth = 920.0f;
        private const string LeftPanelWidthPrefKey = "FusionAnimator.GraphCanvas.LeftPanelWidth";
        private const string RightPanelWidthPrefKey = "FusionAnimator.GraphCanvas.RightPanelWidth";
        private const string CharacterAnimationControllerTypeName = "TPSBR.CharacterAnimationController";

        [MenuItem("Tools/Fusion/Fusion Animator Canvas", false, 253)]
        public static void Open()
        {
            FusionAnimatorGraphCanvasWindow window = GetWindow<FusionAnimatorGraphCanvasWindow>();
            window.titleContent = new GUIContent("Fusion Animator Canvas");
            window.minSize = new Vector2(1180.0f, 680.0f);
            window.Show();
        }

        public static void Open(FusionAnimatorGraphAsset graph)
        {
            Open();
            FusionAnimatorGraphCanvasWindow window = GetWindow<FusionAnimatorGraphCanvasWindow>();
            window.BindGraph(graph);
        }

        private void OnEnable()
        {
            Undo.undoRedoPerformed += OnUndoRedo;
            LoadPanelWidthPreferences();
            BuildUi();

            if (_graph == null && Selection.activeObject is FusionAnimatorGraphAsset selectedGraph)
            {
                BindGraph(selectedGraph);
            }
            else
            {
                BindGraph(_graph);
            }
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= OnUndoRedo;
            SavePanelWidthPreferences();
            StopPreviewSampling();
            if (_graphView != null)
            {
                CapturePreviewCameraStateFromGraphView();
                _graphView.OnSelectionChanged = null;
                _graphView.OnLayerNodeSelected = null;
                _graphView.OnGraphDirty = null;
                _graphView.OnBackgroundClicked = null;
                _graphView.OnScopeNodeRenameRequested = null;
                _graphView.OnPreviewCameraChanged = null;
                _graphView.DisposePreviewRender();
            }
        }

        private void OnUndoRedo()
        {
            SelectionSnapshot snapshot = CaptureSelectionSnapshot();
            _graphView?.RebuildFromGraphData();
            RestoreSelectionSnapshot(snapshot);
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private readonly struct SelectionSnapshot
        {
            public readonly string ActiveLayerId;
            public readonly string ActiveScopePath;
            public readonly string SelectedStateId;
            public readonly string SelectedTransitionId;
            public readonly string SelectedParameterId;
            public readonly string SelectedBindingId;
            public readonly string SelectedLayerId;
            public readonly string SelectedScopePath;
            public readonly string SelectedEntryLinkTargetStateId;

            public SelectionSnapshot(
                string activeLayerId,
                string activeScopePath,
                string selectedStateId,
                string selectedTransitionId,
                string selectedParameterId,
                string selectedBindingId,
                string selectedLayerId,
                string selectedScopePath,
                string selectedEntryLinkTargetStateId)
            {
                ActiveLayerId = activeLayerId;
                ActiveScopePath = activeScopePath;
                SelectedStateId = selectedStateId;
                SelectedTransitionId = selectedTransitionId;
                SelectedParameterId = selectedParameterId;
                SelectedBindingId = selectedBindingId;
                SelectedLayerId = selectedLayerId;
                SelectedScopePath = selectedScopePath;
                SelectedEntryLinkTargetStateId = selectedEntryLinkTargetStateId;
            }
        }

        private SelectionSnapshot CaptureSelectionSnapshot()
        {
            string selectedParameterId = null;
            if (_graph?.Parameters != null &&
                _selectedParameterIndex >= 0 &&
                _selectedParameterIndex < _graph.Parameters.Count)
            {
                selectedParameterId = _graph.Parameters[_selectedParameterIndex]?.Id;
            }

            string selectedLayerId = null;
            if (_graph?.Layers != null &&
                _selectedLayerIndex >= 0 &&
                _selectedLayerIndex < _graph.Layers.Count)
            {
                selectedLayerId = _graph.Layers[_selectedLayerIndex]?.Id;
            }

            string selectedBindingId = null;
            if (_graph?.ClipBindings != null &&
                _selectedBindingIndex >= 0 &&
                _selectedBindingIndex < _graph.ClipBindings.Count)
            {
                selectedBindingId = _graph.ClipBindings[_selectedBindingIndex]?.Id;
            }

            return new SelectionSnapshot(
                _activeLayerId,
                _activeScopePath,
                _selectedState?.Id,
                _selectedTransition?.Id,
                selectedParameterId,
                selectedBindingId,
                selectedLayerId,
                _selectedLayerScopePath,
                _selectedEntryLinkTargetStateId);
        }

        private void RestoreSelectionSnapshot(SelectionSnapshot snapshot)
        {
            if (_graph == null)
            {
                return;
            }

            EnsureGraphCollections();

            string restoredActiveLayerId = string.IsNullOrWhiteSpace(snapshot.ActiveLayerId) ? string.Empty : snapshot.ActiveLayerId;
            if (string.IsNullOrWhiteSpace(restoredActiveLayerId) == false && FindLayerIndexById(restoredActiveLayerId) < 0)
            {
                restoredActiveLayerId = string.Empty;
            }

            _activeLayerId = restoredActiveLayerId;
            _activeScopePath = string.IsNullOrWhiteSpace(snapshot.ActiveScopePath) ? string.Empty : snapshot.ActiveScopePath;
            _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
            RefreshScopeMenu();
            RefreshScopeBreadcrumb();

            _selectedState = null;
            _selectedTransition = null;
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedLayerIndex = -1;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            _graphView?.SetSelectedTransition(null);
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(null);

            if (string.IsNullOrWhiteSpace(snapshot.SelectedStateId) == false)
            {
                FusionAnimatorStateDefinition state = FindStateById(snapshot.SelectedStateId);
                if (state != null)
                {
                    _selectedState = state;
                    _selectedLayerIndex = FindLayerIndexById(state.LayerId);
                    _activeLayerId = state.LayerId ?? string.Empty;
                    _activeScopePath = GetStateScopePathFromName(state.Name);
                    _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
                    _graphView?.SelectStateById(state.Id);
                    FusionAnimatorEditorSelectionContext.SetSelection(_graph, state.Id, null);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(snapshot.SelectedTransitionId) == false)
            {
                FusionAnimatorTransitionDefinition transition = FindTransitionById(snapshot.SelectedTransitionId);
                if (transition != null)
                {
                    _selectedTransition = transition;
                    string layerId = FindStateById(transition.FromStateId)?.LayerId;
                    if (string.IsNullOrWhiteSpace(layerId))
                    {
                        layerId = FindStateById(transition.ToStateId)?.LayerId;
                    }

                    _selectedLayerIndex = FindLayerIndexById(layerId);
                    if (string.IsNullOrWhiteSpace(layerId) == false)
                    {
                        _activeLayerId = layerId;
                        _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
                    }

                    _graphView?.SetSelectedTransition(transition.Id);
                    FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, transition.Id);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(snapshot.SelectedEntryLinkTargetStateId) == false)
            {
                FusionAnimatorStateDefinition entryTarget = FindStateById(snapshot.SelectedEntryLinkTargetStateId);
                if (entryTarget != null)
                {
                    _selectedEntryLinkTargetStateId = entryTarget.Id;
                    _selectedLayerIndex = -1;
                    FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(snapshot.SelectedScopePath) == false)
            {
                string scopeLayerId = snapshot.SelectedLayerId;
                if (string.IsNullOrWhiteSpace(scopeLayerId))
                {
                    scopeLayerId = _activeLayerId;
                }

                if (string.IsNullOrWhiteSpace(scopeLayerId) == false && FindLayerIndexById(scopeLayerId) >= 0)
                {
                    _selectedLayerIndex = FindLayerIndexById(scopeLayerId);
                    _selectedLayerScopePath = snapshot.SelectedScopePath;
                    _activeLayerId = scopeLayerId;
                    _activeScopePath = GetStateScopePathFromName(snapshot.SelectedScopePath);
                    _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
                    _graphView?.SelectScopeNodeByPath(snapshot.SelectedScopePath);
                    FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(snapshot.SelectedParameterId) == false && _graph.Parameters != null)
            {
                for (int i = 0; i < _graph.Parameters.Count; ++i)
                {
                    FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                    if (parameter != null && string.Equals(parameter.Id, snapshot.SelectedParameterId, StringComparison.Ordinal))
                    {
                        _selectedParameterIndex = i;
                        _graphView?.SetHoveredParameter(parameter.Id);
                        FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
                        return;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(snapshot.SelectedBindingId) == false && _graph.ClipBindings != null)
            {
                for (int i = 0; i < _graph.ClipBindings.Count; ++i)
                {
                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[i];
                    if (binding != null && string.Equals(binding.Id, snapshot.SelectedBindingId, StringComparison.Ordinal))
                    {
                        _selectedBindingIndex = i;
                        _leftLibraryTab = LeftLibraryTab.Bindings;
                        FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
                        return;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(snapshot.SelectedLayerId) == false)
            {
                int layerIndex = FindLayerIndexById(snapshot.SelectedLayerId);
                if (layerIndex >= 0)
                {
                    _selectedLayerIndex = layerIndex;
                    _graphView?.SetHoveredLayer(snapshot.SelectedLayerId);
                    _graphView?.SelectLayerNodeByLayerId(snapshot.SelectedLayerId);
                    FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
                    return;
                }
            }

            FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
        }

        private FusionAnimatorTransitionDefinition FindTransitionById(string transitionId)
        {
            if (_graph?.Transitions == null || string.IsNullOrWhiteSpace(transitionId))
            {
                return null;
            }

            for (int i = 0; i < _graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = _graph.Transitions[i];
                if (transition != null && string.Equals(transition.Id, transitionId, StringComparison.Ordinal))
                {
                    return transition;
                }
            }

            return null;
        }

        private void OnSelectionChange()
        {
            if (_graph == null && Selection.activeObject is FusionAnimatorGraphAsset selectedGraph)
            {
                BindGraph(selectedGraph);
            }
        }

        private void Update()
        {
            if (_graphView == null)
            {
                return;
            }

            if (_graphView.TryGetSelectedLayerId(out string selectedLayerId))
            {
                if (FindLayerIndexById(selectedLayerId) != _selectedLayerIndex || _selectedLayerIndex < 0)
                {
                    OnGraphLayerNodeSelected(selectedLayerId);
                }
            }

            if (_graphView.TryGetCurrentSelection(out FusionAnimatorStateDefinition state, out FusionAnimatorTransitionDefinition transition))
            {
                if (ReferenceEquals(state, _selectedState) == false || ReferenceEquals(transition, _selectedTransition) == false)
                {
                    OnGraphSelectionChanged(state, transition);
                }
            }
            else
            {
                bool hasSpecialSelection = false;
                if (_graphView.TryGetSelectedEntryLinkTargetStateId(out string selectedEntryTargetStateId))
                {
                    hasSpecialSelection = true;
                    string normalizedEntryTarget = selectedEntryTargetStateId ?? string.Empty;
                    if (string.Equals(_selectedEntryLinkTargetStateId, normalizedEntryTarget, StringComparison.Ordinal) == false ||
                        _selectedState != null ||
                        _selectedTransition != null ||
                        string.IsNullOrWhiteSpace(_selectedLayerScopePath) == false)
                    {
                        OnGraphSelectionChanged(null, null);
                    }
                }
                else if (_graphView.TryGetSelectedScopePath(out string selectedScopePath))
                {
                    hasSpecialSelection = true;
                    string normalizedScopePath = selectedScopePath ?? string.Empty;
                    if (string.Equals(_selectedLayerScopePath, normalizedScopePath, StringComparison.OrdinalIgnoreCase) == false ||
                        _selectedState != null ||
                        _selectedTransition != null ||
                        string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false)
                    {
                        OnGraphSelectionChanged(null, null);
                    }
                }

                if (hasSpecialSelection == false &&
                    _graphView.TryGetSelectedLayerId(out _) == false &&
                    ShouldResolveBackgroundSelectionToActiveContext())
                {
                    OnGraphSelectionChanged(null, null);
                }
            }

            UpdatePreviewPlayback();
        }

        private bool ShouldResolveBackgroundSelectionToActiveContext()
        {
            if (_selectedParameterIndices.Count > 0 ||
                _selectedBindingIndices.Count > 0 ||
                _selectedLayerIndices.Count > 0 ||
                _selectedStateIds.Count > 0 ||
                _selectedScopeKeys.Count > 0)
            {
                return false;
            }

            if (_selectedParameterIndex >= 0)
            {
                return false;
            }

            if (_selectedBindingIndex >= 0 || _selectedBindingIndices.Count > 0 || string.IsNullOrWhiteSpace(_selectedBindingGroupId) == false)
            {
                return false;
            }

            if (_selectedLayerIndex >= 0 &&
                string.IsNullOrWhiteSpace(_selectedLayerScopePath) &&
                string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId))
            {
                return false;
            }

            if (_selectedState != null || _selectedTransition != null || string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false)
            {
                return true;
            }

            string desiredScopePath = NormalizeScopePath(_activeScopePath);
            int desiredLayerIndex = string.IsNullOrWhiteSpace(_activeLayerId) ? -1 : FindLayerIndexById(_activeLayerId);
            bool layerMatches = _selectedLayerIndex == desiredLayerIndex;
            bool scopeMatches = string.Equals(_selectedLayerScopePath, desiredScopePath, StringComparison.OrdinalIgnoreCase);
            return layerMatches == false || scopeMatches == false;
        }

        private void BuildUi()
        {
            rootVisualElement.Clear();
            rootVisualElement.style.flexDirection = FlexDirection.Column;
            rootVisualElement.style.flexGrow = 1.0f;

            Toolbar toolbar = new Toolbar();

            _graphField = new ObjectField("Graph")
            {
                objectType = typeof(FusionAnimatorGraphAsset),
                allowSceneObjects = false,
            };
            _graphField.tooltip = "Graph asset currently open in the FusionAnimator canvas.";
            _graphField.style.minWidth = 380.0f;
            _graphField.RegisterValueChangedCallback(evt =>
            {
                BindGraph(evt.newValue as FusionAnimatorGraphAsset);
            });
            toolbar.Add(_graphField);

            _convertSourceField = new ObjectField("Source")
            {
                objectType = typeof(UnityEngine.Object),
                allowSceneObjects = false,
            };
            _convertSourceField.tooltip = "Source asset for graph conversion (AnimatorController asset, or prefab with CharacterAnimationController).";
            _convertSourceField.style.minWidth = 280.0f;
            _convertSourceField.RegisterValueChangedCallback(evt =>
            {
                if (TryNormalizeConvertSource(evt.newValue, out UnityEngine.Object normalizedSource))
                {
                    _convertSourceField.SetValueWithoutNotify(normalizedSource);
                    PersistGraphSourceReference(normalizedSource, false);
                }
                else
                {
                    _convertSourceField.SetValueWithoutNotify(evt.previousValue);
                    if (evt.newValue != null)
                    {
                        EditorUtility.DisplayDialog(
                            "Fusion Animator Source",
                            "Source must be either:\n- an AnimatorController asset, or\n- a prefab containing CharacterAnimationController.",
                            "OK");
                    }
                }
            });
            _convertSourceField.SetEnabled(false);
            toolbar.Add(_convertSourceField);
            toolbar.Add(new Button(ShowSourceSelectionMenu) { text = "Pick", tooltip = "Pick source from compatible AnimatorControllers and CharacterAnimationController prefabs." });

            toolbar.Add(new Button(ShowConvertMenu) { text = "Convert", tooltip = "Convert selected source asset into the current FusionAnimator graph." });
            toolbar.Add(new Button(ValidateGraph) { text = "Validate", tooltip = "Run graph validation checks and print summary to console." });
            toolbar.Add(new Button(RepairGraphData) { text = "Repair", tooltip = "Repair hidden orphan data (missing layer references / invalid condition parameters) so issues become visible in canvas." });
            toolbar.Add(new Button(SaveGraph) { text = "Save", tooltip = "Save all pending graph edits to disk." });

            VisualElement toolbarSpacer = new VisualElement();
            toolbarSpacer.style.flexGrow = 1.0f;
            toolbar.Add(toolbarSpacer);
            _searchField = new ToolbarSearchField
            {
                tooltip = "Filter nodes by state name/id/layer name.",
            };
            _searchField.style.minWidth = 220.0f;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _graphView?.SetSearchFilter(evt.newValue);
            });
            toolbar.Add(_searchField);

            rootVisualElement.Add(toolbar);
            rootVisualElement.Add(BuildPreviewToolbar());

            VisualElement layout = new VisualElement();
            layout.style.flexDirection = FlexDirection.Row;
            layout.style.flexGrow = 1.0f;

            _leftPanelRoot = new VisualElement();
            _leftPanelRoot.style.width = _leftPanelWidth;
            _leftPanelRoot.style.minWidth = LeftPanelMinWidth;
            _leftPanelRoot.style.maxWidth = LeftPanelMaxWidth;
            _leftPanelRoot.style.paddingLeft = 8.0f;
            _leftPanelRoot.style.paddingRight = 8.0f;
            _leftPanelRoot.style.paddingTop = 8.0f;
            _leftPanelRoot.style.paddingBottom = 8.0f;
            _leftPanelRoot.style.position = Position.Relative;
            _leftPanelRoot.style.flexShrink = 0.0f;

            _leftPanel = new IMGUIContainer(DrawLeftPanel);
            _leftPanel.style.flexGrow = 1.0f;
            _leftPanel.style.flexShrink = 1.0f;
            _leftPanelRoot.Add(_leftPanel);
            _leftResizeHandle = BuildPanelResizeHandle(isLeftPanel: true);
            _leftPanelRoot.Add(_leftResizeHandle);
            layout.Add(_leftPanelRoot);

            _graphHost = new VisualElement();
            _graphHost.style.flexGrow = 1.0f;
            _graphHost.style.position = Position.Relative;
            _graphHost.style.overflow = Overflow.Hidden;

            _graphView = new FusionAnimatorGraphView();
            _graphView.style.flexGrow = 1.0f;
            _graphView.StretchToParentSize();
            _graphView.OnSelectionChanged = OnGraphSelectionChanged;
            _graphView.OnLayerNodeSelected = OnGraphLayerNodeSelected;
            _graphView.OnGraphDirty = MarkGraphDirty;
            _graphView.OnBackgroundClicked = OnCanvasBackgroundClicked;
            _graphView.OnScopeChanged = HandleGraphScopeChanged;
            _graphView.OnScopeNodeRenameRequested = OnGraphScopeNodeRenameRequested;
            _graphView.OnPreviewCameraChanged = CapturePreviewCameraStateFromGraphView;
            ApplyPreviewCameraStateToGraphView();
            _graphHost.Add(_graphView);

            _scopeBreadcrumbRoot = BuildScopeBreadcrumbBar();
            _scopeBreadcrumbRoot.style.position = Position.Absolute;
            _scopeBreadcrumbRoot.style.left = 8.0f;
            _scopeBreadcrumbRoot.style.top = 8.0f;
            _graphHost.Add(_scopeBreadcrumbRoot);

            _emptyStateOverlay = CreateEmptyStateOverlay();
            _graphHost.Add(_emptyStateOverlay);
            layout.Add(_graphHost);

            _inspectorRoot = new VisualElement();
            _inspectorRoot.style.width = _rightPanelWidth;
            _inspectorRoot.style.minWidth = RightPanelMinWidth;
            _inspectorRoot.style.maxWidth = RightPanelMaxWidth;
            _inspectorRoot.style.paddingLeft = 8.0f;
            _inspectorRoot.style.paddingRight = 8.0f;
            _inspectorRoot.style.paddingTop = 8.0f;
            _inspectorRoot.style.paddingBottom = 8.0f;
            _inspectorRoot.style.position = Position.Relative;
            _inspectorRoot.style.flexShrink = 0.0f;

            _inspector = new IMGUIContainer(DrawInspectorPanel);
            _inspector.style.flexGrow = 1.0f;
            _inspector.style.flexShrink = 1.0f;
            _inspectorRoot.Add(_inspector);
            _rightResizeHandle = BuildPanelResizeHandle(isLeftPanel: false);
            _inspectorRoot.Add(_rightResizeHandle);

            layout.Add(_inspectorRoot);
            rootVisualElement.Add(layout);
        }

        private VisualElement BuildPanelResizeHandle(bool isLeftPanel)
        {
            VisualElement handle = new VisualElement();
            handle.name = isLeftPanel ? "fa-left-panel-resize" : "fa-right-panel-resize";
            handle.style.position = Position.Absolute;
            handle.style.top = 0.0f;
            handle.style.bottom = 0.0f;
            handle.style.width = 6.0f;
            handle.style.backgroundColor = new Color(1.0f, 1.0f, 1.0f, 0.03f);
            if (isLeftPanel)
            {
                handle.style.right = 0.0f;
            }
            else
            {
                handle.style.left = 0.0f;
            }

            handle.RegisterCallback<MouseDownEvent>(evt =>
            {
                if (evt == null || evt.button != 0)
                {
                    return;
                }

                if (isLeftPanel)
                {
                    _isLeftPanelResizing = true;
                    _resizeStartWidth = _leftPanelRoot != null ? _leftPanelRoot.resolvedStyle.width : LeftPanelMinWidth;
                }
                else
                {
                    _isRightPanelResizing = true;
                    _resizeStartWidth = _inspectorRoot != null ? _inspectorRoot.resolvedStyle.width : RightPanelMinWidth;
                }

                _resizeStartMouseX = evt.mousePosition.x;
                handle.CaptureMouse();
                evt.StopPropagation();
            });

            handle.RegisterCallback<MouseMoveEvent>(evt =>
            {
                if (evt == null || (_isLeftPanelResizing == false && _isRightPanelResizing == false))
                {
                    return;
                }

                float delta = evt.mousePosition.x - _resizeStartMouseX;
                if (_isLeftPanelResizing && _leftPanelRoot != null)
                {
                    float width = Mathf.Clamp(_resizeStartWidth + delta, LeftPanelMinWidth, LeftPanelMaxWidth);
                    _leftPanelWidth = width;
                    _leftPanelRoot.style.width = width;
                }
                else if (_isRightPanelResizing && _inspectorRoot != null)
                {
                    float width = Mathf.Clamp(_resizeStartWidth - delta, RightPanelMinWidth, RightPanelMaxWidth);
                    _rightPanelWidth = width;
                    _inspectorRoot.style.width = width;
                }

                evt.StopPropagation();
            });

            handle.RegisterCallback<MouseUpEvent>(evt =>
            {
                if (evt == null || evt.button != 0)
                {
                    return;
                }

                if (_isLeftPanelResizing || _isRightPanelResizing)
                {
                    _isLeftPanelResizing = false;
                    _isRightPanelResizing = false;
                    if (handle.HasMouseCapture())
                    {
                        handle.ReleaseMouse();
                    }

                    SavePanelWidthPreferences();
                    evt.StopPropagation();
                }
            });

            return handle;
        }

        private void LoadPanelWidthPreferences()
        {
            _leftPanelWidth = Mathf.Clamp(EditorPrefs.GetFloat(LeftPanelWidthPrefKey, LeftPanelMinWidth), LeftPanelMinWidth, LeftPanelMaxWidth);
            _rightPanelWidth = Mathf.Clamp(EditorPrefs.GetFloat(RightPanelWidthPrefKey, RightPanelMinWidth), RightPanelMinWidth, RightPanelMaxWidth);
        }

        private void SavePanelWidthPreferences()
        {
            float leftWidth = _leftPanelRoot != null ? _leftPanelRoot.resolvedStyle.width : _leftPanelWidth;
            float rightWidth = _inspectorRoot != null ? _inspectorRoot.resolvedStyle.width : _rightPanelWidth;

            if (float.IsNaN(leftWidth) || leftWidth <= 0.0f)
            {
                leftWidth = _leftPanelWidth;
            }

            if (float.IsNaN(rightWidth) || rightWidth <= 0.0f)
            {
                rightWidth = _rightPanelWidth;
            }

            _leftPanelWidth = Mathf.Clamp(leftWidth, LeftPanelMinWidth, LeftPanelMaxWidth);
            _rightPanelWidth = Mathf.Clamp(rightWidth, RightPanelMinWidth, RightPanelMaxWidth);

            EditorPrefs.SetFloat(LeftPanelWidthPrefKey, _leftPanelWidth);
            EditorPrefs.SetFloat(RightPanelWidthPrefKey, _rightPanelWidth);
        }

        private void BindGraph(FusionAnimatorGraphAsset graph)
        {
            _graph = graph;
            _selectedState = null;
            _selectedTransition = null;
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedLayerIndex = -1;
            _previewTarget = ResolvePersistedPreviewTarget();

            if (_graphField != null)
            {
                _graphField.SetValueWithoutNotify(_graph);
            }

            _graphView?.BindGraph(graph);
            ApplyPreviewCameraStateToGraphView();
            _graphView?.SetPreviewApplyRootMotion(_graph != null && _graph.ApplyRootMotion);
            _graphView?.SetSelectedTransition(null);
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(null);
            if (_searchField != null)
            {
                _graphView?.SetSearchFilter(_searchField.value);
            }
            RefreshScopeMenu();
            EnsureActiveLayerContext();
            _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
            RefreshScopeBreadcrumb();

            FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
            ResetIntegratedPreview();

            bool hasGraph = _graph != null;
            if (_leftPanelRoot != null)
            {
                _leftPanelRoot.style.display = hasGraph ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_inspectorRoot != null)
            {
                _inspectorRoot.style.display = hasGraph ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_emptyStateOverlay != null)
            {
                _emptyStateOverlay.style.display = hasGraph ? DisplayStyle.None : DisplayStyle.Flex;
            }

            if (_graphView != null)
            {
                _graphView.SetEnabled(hasGraph);
                _graphView.style.display = hasGraph ? DisplayStyle.Flex : DisplayStyle.None;
            }

            if (_searchField != null)
            {
                _searchField.SetEnabled(hasGraph);
            }

            if (_previewTargetField != null)
            {
                _previewTargetField.SetEnabled(hasGraph);
            }

            if (_previewPlayToggle != null)
            {
                _previewPlayToggle.SetEnabled(hasGraph);
            }

            if (_previewEnabledToggle != null)
            {
                _previewEnabledToggle.SetEnabled(hasGraph);
            }

            if (_previewMinimapToggle != null)
            {
                _previewMinimapToggle.SetEnabled(hasGraph);
            }

            if (_previewSpeedField != null)
            {
                _previewSpeedField.SetEnabled(hasGraph);
            }

            if (_convertSourceField != null)
            {
                UnityEngine.Object sourceRef = _graph != null ? _graph.PreviewSource : null;
                if (TryNormalizeConvertSource(sourceRef, out UnityEngine.Object normalizedFromGraph))
                {
                    sourceRef = normalizedFromGraph;
                }
                else
                {
                    sourceRef = null;
                }

                if (sourceRef == null && Selection.activeObject != null)
                {
                    if (TryNormalizeConvertSource(Selection.activeObject, out UnityEngine.Object normalizedSelection))
                    {
                        sourceRef = normalizedSelection;
                    }
                }

                _convertSourceField.SetValueWithoutNotify(sourceRef);
            }

            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();

            if (_graphView != null && hasGraph)
            {
                FusionAnimatorGraphView graphView = _graphView;
                EditorApplication.delayCall += () =>
                {
                    if (graphView != null)
                    {
                        graphView.FrameAll();
                    }
                };
            }
        }

        private Toolbar BuildPreviewToolbar()
        {
            Toolbar toolbar = new Toolbar();
            toolbar.tooltip = "Preview controls for edit-mode animation simulation.";

            _previewEnabledToggle = new ToolbarToggle { text = "Preview", tooltip = "Enable or disable edit-mode preview rendering and simulation." };
            _previewEnabledToggle.RegisterValueChangedCallback(evt =>
            {
                _previewEnabled = evt.newValue;
                if (_previewEnabled == false)
                {
                    _previewStatus = "Preview disabled.";
                    _graphView?.SetPreviewBackgroundStatus(_previewStatus);
                    _graphView?.ClearPreviewRender();
                }
            });
            toolbar.Add(_previewEnabledToggle);

            _previewPlayToggle = new ToolbarToggle { text = "Play", tooltip = "Toggle preview playback in edit mode." };
            _previewPlayToggle.RegisterValueChangedCallback(evt =>
            {
                _previewPlay = evt.newValue;
            });
            toolbar.Add(_previewPlayToggle);

            _previewMinimapToggle = new ToolbarToggle { text = "MiniMap", tooltip = "Show or hide graph minimap." };
            _previewMinimapToggle.RegisterValueChangedCallback(evt =>
            {
                _previewShowMiniMap = evt.newValue;
                _graphView?.SetMiniMapVisible(_previewShowMiniMap);
            });
            toolbar.Add(_previewMinimapToggle);

            Label speedLabel = new Label("Speed");
            speedLabel.style.marginLeft = 10.0f;
            speedLabel.style.marginRight = 4.0f;
            toolbar.Add(speedLabel);

            _previewSpeedField = new FloatField
            {
                value = 1.0f,
                tooltip = "Global preview playback speed multiplier.",
            };
            _previewSpeedField.style.width = 74.0f;
            _previewSpeedField.RegisterValueChangedCallback(evt =>
            {
                _previewPlaySpeed = Mathf.Clamp(evt.newValue, 0.05f, 8.0f);
                if (Mathf.Approximately(_previewPlaySpeed, evt.newValue) == false)
                {
                    _previewSpeedField.SetValueWithoutNotify(_previewPlaySpeed);
                }
            });
            toolbar.Add(_previewSpeedField);

            Label targetLabel = new Label("Target");
            targetLabel.style.marginLeft = 10.0f;
            targetLabel.style.marginRight = 4.0f;
            toolbar.Add(targetLabel);

            _previewTargetField = new ObjectField
            {
                objectType = typeof(GameObject),
                allowSceneObjects = true,
                tooltip = "Scene GameObject used as preview rig target for edit-mode sampling.",
            };
            _previewTargetField.style.minWidth = 220.0f;
            _previewTargetField.RegisterValueChangedCallback(evt =>
            {
                _previewTarget = evt.newValue as GameObject;
                PersistGraphTargetReference(_previewTarget, false);
                _previewTime = 0.0f;
            });
            toolbar.Add(_previewTargetField);

            VisualElement spacer = new VisualElement();
            spacer.style.flexGrow = 1.0f;
            toolbar.Add(spacer);

            Button resetPreviewButton = new Button(() =>
            {
                _previewTime = 0.0f;
            })
            {
                text = "Reset",
                tooltip = "Reset preview time to the start of the current sampled motion.",
            };
            toolbar.Add(resetPreviewButton);

            RefreshPreviewToolbarValues();
            return toolbar;
        }

        private void RefreshPreviewToolbarValues()
        {
            if (_previewTarget != null && _previewTarget.Equals(null))
            {
                _previewTarget = null;
            }

            if (_previewPlayToggle != null)
            {
                _previewPlayToggle.SetValueWithoutNotify(_previewPlay);
            }

            if (_previewEnabledToggle != null)
            {
                _previewEnabledToggle.SetValueWithoutNotify(_previewEnabled);
            }

            if (_previewMinimapToggle != null)
            {
                _previewMinimapToggle.SetValueWithoutNotify(_previewShowMiniMap);
            }

            if (_previewSpeedField != null)
            {
                _previewSpeedField.SetValueWithoutNotify(_previewPlaySpeed);
            }

            if (_previewTargetField != null)
            {
                _previewTargetField.SetValueWithoutNotify(_previewTarget);
            }
        }

        private void AddStateAtCenter()
        {
            if (_graphView == null || _graph == null)
            {
                return;
            }

            _graphView.AddStateAtViewportCenter();
            MarkGraphDirty();
        }

        private void ValidateGraph()
        {
            if (_graph == null)
            {
                return;
            }

            List<FusionAnimatorValidationIssue> issues = FusionAnimatorValidator.Validate(_graph);
            int errorCount = issues.Count(issue => issue.Severity == FusionAnimatorValidationSeverity.Error);
            int warningCount = issues.Count(issue => issue.Severity == FusionAnimatorValidationSeverity.Warning);
            StringBuilder details = new StringBuilder(256);
            for (int i = 0, count = issues.Count; i < count; ++i)
            {
                FusionAnimatorValidationIssue issue = issues[i];
                details.Append('[')
                    .Append(issue.Severity)
                    .Append("] ")
                    .Append(issue.Context)
                    .Append(": ")
                    .Append(issue.Message)
                    .AppendLine();
            }

            if (errorCount > 0)
            {
                GetHiddenGraphIssueCounts(
                    out int orphanStateCount,
                    out int blankTransitionConditionCount,
                    out int invalidTransitionConditionReferenceCount);
                int hiddenIssueCount = orphanStateCount + blankTransitionConditionCount + invalidTransitionConditionReferenceCount;
                if (hiddenIssueCount > 0)
                {
                    details.AppendLine(string.Format(
                        "[Info] Repair: Hidden data issues detected in canvas UI summary. Click 'Repair' in the toolbar to auto-fix ({0} total).",
                        hiddenIssueCount));
                }

                Debug.LogError(string.Format("FusionAnimator validation failed for '{0}' with {1} error(s), {2} warning(s).\n{3}", _graph.name, errorCount, warningCount, details), _graph);
            }
            else if (warningCount > 0)
            {
                Debug.LogWarning(string.Format("FusionAnimator validation for '{0}' has {1} warning(s).\n{2}", _graph.name, warningCount, details), _graph);
            }
            else
            {
                Debug.Log(string.Format("FusionAnimator graph '{0}' is valid.\n{1}", _graph.name, details), _graph);
            }
        }

        private void RepairGraphData()
        {
            if (_graph == null)
            {
                return;
            }

            EnsureGraphCollections();
            bool undoRecorded = false;

            HashSet<string> layerIds = new HashSet<string>(StringComparer.Ordinal);
            string fallbackLayerId = string.Empty;
            if (_graph.Layers != null)
            {
                for (int layerIndex = 0; layerIndex < _graph.Layers.Count; ++layerIndex)
                {
                    FusionAnimatorLayerDefinition layer = _graph.Layers[layerIndex];
                    if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
                    {
                        continue;
                    }

                    if (layerIds.Add(layer.Id) && string.IsNullOrWhiteSpace(fallbackLayerId))
                    {
                        fallbackLayerId = layer.Id;
                    }
                }
            }

            HashSet<string> parameterIds = new HashSet<string>(StringComparer.Ordinal);
            HashSet<string> intParameterIds = new HashSet<string>(StringComparer.Ordinal);
            if (_graph.Parameters != null)
            {
                for (int parameterIndex = 0; parameterIndex < _graph.Parameters.Count; ++parameterIndex)
                {
                    FusionAnimatorParameterDefinition parameter = _graph.Parameters[parameterIndex];
                    if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
                    {
                        continue;
                    }

                    if (parameterIds.Add(parameter.Id) && parameter.Type == FusionAnimatorParameterType.Int)
                    {
                        intParameterIds.Add(parameter.Id);
                    }
                    else if (parameter.Type == FusionAnimatorParameterType.Int)
                    {
                        intParameterIds.Add(parameter.Id);
                    }
                }
            }

            HashSet<string> stateIds = new HashSet<string>(StringComparer.Ordinal);
            if (_graph.States != null)
            {
                for (int stateIndex = 0; stateIndex < _graph.States.Count; ++stateIndex)
                {
                    FusionAnimatorStateDefinition state = _graph.States[stateIndex];
                    if (state == null || string.IsNullOrWhiteSpace(state.Id))
                    {
                        continue;
                    }

                    stateIds.Add(state.Id);
                }
            }

            bool changed = false;
            int reassignedStateLayerCount = 0;
            int removedTransitionConditionCount = 0;
            int removedBindingConditionCount = 0;
            int removedBindingSlotConditionCount = 0;
            int resetInvalidBindingIndexParameterCount = 0;
            int removedInvalidTransitionCount = 0;
            int removedScopeLayoutCount = 0;
            int removedSuppressionCount = 0;

            if (_graph.States != null)
            {
                for (int stateIndex = 0; stateIndex < _graph.States.Count; ++stateIndex)
                {
                    FusionAnimatorStateDefinition state = _graph.States[stateIndex];
                    if (state == null || IsScopeSentinelStateName(state.Name))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(state.LayerId) == false &&
                        layerIds.Contains(state.LayerId))
                    {
                        continue;
                    }

                    string repairedLayerId = GuessLayerForStateRepair(state, layerIds, fallbackLayerId);
                    if (string.IsNullOrWhiteSpace(repairedLayerId))
                    {
                        continue;
                    }

                    if (string.Equals(state.LayerId, repairedLayerId, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (undoRecorded == false)
                    {
                        RecordUndo("Repair FusionAnimator Graph");
                        undoRecorded = true;
                    }

                    state.LayerId = repairedLayerId;
                    reassignedStateLayerCount++;
                    changed = true;
                }
            }

            if (_graph.Transitions != null)
            {
                for (int transitionIndex = _graph.Transitions.Count - 1; transitionIndex >= 0; --transitionIndex)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[transitionIndex];
                    if (transition == null)
                    {
                        if (undoRecorded == false)
                        {
                            RecordUndo("Repair FusionAnimator Graph");
                            undoRecorded = true;
                        }

                        _graph.Transitions.RemoveAt(transitionIndex);
                        removedInvalidTransitionCount++;
                        changed = true;
                        continue;
                    }

                    bool fromValid = IsRepairValidFromEndpoint(transition.FromStateId, stateIds);
                    bool toValid = IsRepairValidToEndpoint(transition.ToStateId, stateIds);
                    if (fromValid == false || toValid == false)
                    {
                        if (undoRecorded == false)
                        {
                            RecordUndo("Repair FusionAnimator Graph");
                            undoRecorded = true;
                        }

                        _graph.Transitions.RemoveAt(transitionIndex);
                        removedInvalidTransitionCount++;
                        changed = true;
                        continue;
                    }

                    if (CleanInvalidConditionReferences(transition.Conditions, parameterIds, out int removedCount))
                    {
                        if (undoRecorded == false)
                        {
                            RecordUndo("Repair FusionAnimator Graph");
                            undoRecorded = true;
                        }

                        removedTransitionConditionCount += removedCount;
                        changed = true;
                    }
                }
            }

            if (_graph.ClipBindings != null)
            {
                for (int bindingIndex = 0; bindingIndex < _graph.ClipBindings.Count; ++bindingIndex)
                {
                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[bindingIndex];
                    if (binding == null)
                    {
                        continue;
                    }

                    binding.MigrateLegacyClip();
                    if (CleanInvalidConditionReferences(binding.Conditions, parameterIds, out int removedBindingConditions))
                    {
                        if (undoRecorded == false)
                        {
                            RecordUndo("Repair FusionAnimator Graph");
                            undoRecorded = true;
                        }

                        removedBindingConditionCount += removedBindingConditions;
                        changed = true;
                    }

                    if (binding.Clips != null)
                    {
                        for (int slotIndex = 0; slotIndex < binding.Clips.Count; ++slotIndex)
                        {
                            FusionAnimatorClipBindingSlot slot = binding.Clips[slotIndex];
                            if (slot == null)
                            {
                                continue;
                            }

                            if (CleanInvalidConditionReferences(slot.Conditions, parameterIds, out int removedSlotConditions))
                            {
                                if (undoRecorded == false)
                                {
                                    RecordUndo("Repair FusionAnimator Graph");
                                    undoRecorded = true;
                                }

                                removedBindingSlotConditionCount += removedSlotConditions;
                                changed = true;
                            }
                        }
                    }

                    if (string.IsNullOrWhiteSpace(binding.ClipIndexParameterId) == false &&
                        IsValidIndexParameterReference(binding.ClipIndexParameterId, parameterIds, intParameterIds) == false)
                    {
                        if (undoRecorded == false)
                        {
                            RecordUndo("Repair FusionAnimator Graph");
                            undoRecorded = true;
                        }

                        binding.ClipIndexParameterId = string.Empty;
                        resetInvalidBindingIndexParameterCount++;
                        changed = true;
                    }
                }
            }

            if (_graph.ScopeUtilityNodeLayouts != null)
            {
                for (int layoutIndex = _graph.ScopeUtilityNodeLayouts.Count - 1; layoutIndex >= 0; --layoutIndex)
                {
                    FusionAnimatorScopeUtilityNodeLayout layout = _graph.ScopeUtilityNodeLayouts[layoutIndex];
                    if (layout == null ||
                        string.IsNullOrWhiteSpace(layout.LayerId) ||
                        layerIds.Contains(layout.LayerId))
                    {
                        continue;
                    }

                    if (undoRecorded == false)
                    {
                        RecordUndo("Repair FusionAnimator Graph");
                        undoRecorded = true;
                    }

                    _graph.ScopeUtilityNodeLayouts.RemoveAt(layoutIndex);
                    removedScopeLayoutCount++;
                    changed = true;
                }
            }

            if (_graph.ScopeTransitionSuppressions != null)
            {
                HashSet<string> transitionIds = new HashSet<string>(StringComparer.Ordinal);
                if (_graph.Transitions != null)
                {
                    for (int transitionIndex = 0; transitionIndex < _graph.Transitions.Count; ++transitionIndex)
                    {
                        FusionAnimatorTransitionDefinition transition = _graph.Transitions[transitionIndex];
                        if (transition != null && string.IsNullOrWhiteSpace(transition.Id) == false)
                        {
                            transitionIds.Add(transition.Id);
                        }
                    }
                }

                for (int suppressionIndex = _graph.ScopeTransitionSuppressions.Count - 1; suppressionIndex >= 0; --suppressionIndex)
                {
                    FusionAnimatorScopeTransitionSuppression suppression = _graph.ScopeTransitionSuppressions[suppressionIndex];
                    bool invalidSuppression = suppression == null ||
                        string.IsNullOrWhiteSpace(suppression.TransitionId) ||
                        transitionIds.Contains(suppression.TransitionId) == false ||
                        (string.IsNullOrWhiteSpace(suppression.LayerId) == false && layerIds.Contains(suppression.LayerId) == false);
                    if (invalidSuppression == false)
                    {
                        continue;
                    }

                    if (undoRecorded == false)
                    {
                        RecordUndo("Repair FusionAnimator Graph");
                        undoRecorded = true;
                    }

                    _graph.ScopeTransitionSuppressions.RemoveAt(suppressionIndex);
                    removedSuppressionCount++;
                    changed = true;
                }
            }

            if (changed == false)
            {
                Debug.Log(string.Format("FusionAnimator repair found no changes for '{0}'.", _graph.name), _graph);
                return;
            }
            _graphView?.RebuildFromGraphData();
            MarkGraphDirty();
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();

            StringBuilder summary = new StringBuilder(256);
            if (reassignedStateLayerCount > 0)
            {
                summary.AppendLine(string.Format("- Reassigned {0} state layer reference(s).", reassignedStateLayerCount));
            }

            if (removedTransitionConditionCount > 0)
            {
                summary.AppendLine(string.Format("- Removed {0} invalid transition condition(s).", removedTransitionConditionCount));
            }

            if (removedBindingConditionCount > 0)
            {
                summary.AppendLine(string.Format("- Removed {0} invalid binding condition(s).", removedBindingConditionCount));
            }

            if (removedBindingSlotConditionCount > 0)
            {
                summary.AppendLine(string.Format("- Removed {0} invalid binding-slot condition(s).", removedBindingSlotConditionCount));
            }

            if (resetInvalidBindingIndexParameterCount > 0)
            {
                summary.AppendLine(string.Format("- Cleared {0} invalid binding index parameter reference(s).", resetInvalidBindingIndexParameterCount));
            }

            if (removedInvalidTransitionCount > 0)
            {
                summary.AppendLine(string.Format("- Removed {0} invalid transition(s).", removedInvalidTransitionCount));
            }

            if (removedScopeLayoutCount > 0)
            {
                summary.AppendLine(string.Format("- Removed {0} stale scope utility layout record(s).", removedScopeLayoutCount));
            }

            if (removedSuppressionCount > 0)
            {
                summary.AppendLine(string.Format("- Removed {0} stale transition suppression record(s).", removedSuppressionCount));
            }

            Debug.Log(string.Format("FusionAnimator repair applied to '{0}'.\n{1}", _graph.name, summary), _graph);
        }

        private void GetHiddenGraphIssueCounts(
            out int orphanStateCount,
            out int blankTransitionConditionCount,
            out int invalidTransitionConditionReferenceCount)
        {
            orphanStateCount = 0;
            blankTransitionConditionCount = 0;
            invalidTransitionConditionReferenceCount = 0;

            if (_graph == null)
            {
                return;
            }

            EnsureGraphCollections();

            HashSet<string> layerIds = new HashSet<string>(StringComparer.Ordinal);
            if (_graph.Layers != null)
            {
                for (int layerIndex = 0; layerIndex < _graph.Layers.Count; ++layerIndex)
                {
                    FusionAnimatorLayerDefinition layer = _graph.Layers[layerIndex];
                    if (layer != null && string.IsNullOrWhiteSpace(layer.Id) == false)
                    {
                        layerIds.Add(layer.Id);
                    }
                }
            }

            HashSet<string> parameterIds = new HashSet<string>(StringComparer.Ordinal);
            if (_graph.Parameters != null)
            {
                for (int parameterIndex = 0; parameterIndex < _graph.Parameters.Count; ++parameterIndex)
                {
                    FusionAnimatorParameterDefinition parameter = _graph.Parameters[parameterIndex];
                    if (parameter != null && string.IsNullOrWhiteSpace(parameter.Id) == false)
                    {
                        parameterIds.Add(parameter.Id);
                    }
                }
            }

            if (_graph.States != null)
            {
                for (int stateIndex = 0; stateIndex < _graph.States.Count; ++stateIndex)
                {
                    FusionAnimatorStateDefinition state = _graph.States[stateIndex];
                    if (state == null || IsScopeSentinelStateName(state.Name))
                    {
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(state.LayerId) || layerIds.Contains(state.LayerId) == false)
                    {
                        orphanStateCount++;
                    }
                }
            }

            if (_graph.Transitions != null)
            {
                for (int transitionIndex = 0; transitionIndex < _graph.Transitions.Count; ++transitionIndex)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[transitionIndex];
                    if (transition?.Conditions == null)
                    {
                        continue;
                    }

                    for (int conditionIndex = 0; conditionIndex < transition.Conditions.Count; ++conditionIndex)
                    {
                        FusionAnimatorConditionDefinition condition = transition.Conditions[conditionIndex];
                        if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
                        {
                            blankTransitionConditionCount++;
                            continue;
                        }

                        if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string baseParameterId, out _) == false ||
                            parameterIds.Contains(baseParameterId) == false)
                        {
                            invalidTransitionConditionReferenceCount++;
                        }
                    }
                }
            }
        }

        private string GuessLayerForStateRepair(
            FusionAnimatorStateDefinition state,
            HashSet<string> validLayerIds,
            string fallbackLayerId)
        {
            if (_graph?.Layers != null && state != null)
            {
                if (string.IsNullOrWhiteSpace(state.Name) == false)
                {
                    for (int layerIndex = 0; layerIndex < _graph.Layers.Count; ++layerIndex)
                    {
                        FusionAnimatorLayerDefinition layer = _graph.Layers[layerIndex];
                        if (layer == null ||
                            string.IsNullOrWhiteSpace(layer.Id) ||
                            string.IsNullOrWhiteSpace(layer.Name) ||
                            validLayerIds.Contains(layer.Id) == false)
                        {
                            continue;
                        }

                        if (state.Name.IndexOf(layer.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return layer.Id;
                        }
                    }
                }

                string stateScopePath = GetStateScopePathFromName(state.Name);
                string scopeLeaf = GetScopeLeafName(stateScopePath);
                if (string.IsNullOrWhiteSpace(scopeLeaf) == false)
                {
                    for (int layerIndex = 0; layerIndex < _graph.Layers.Count; ++layerIndex)
                    {
                        FusionAnimatorLayerDefinition layer = _graph.Layers[layerIndex];
                        if (layer == null ||
                            string.IsNullOrWhiteSpace(layer.Id) ||
                            string.IsNullOrWhiteSpace(layer.Name) ||
                            validLayerIds.Contains(layer.Id) == false)
                        {
                            continue;
                        }

                        if (string.Equals(layer.Name, scopeLeaf, StringComparison.OrdinalIgnoreCase))
                        {
                            return layer.Id;
                        }
                    }
                }
            }

            return fallbackLayerId;
        }

        private static bool IsRepairValidFromEndpoint(string stateId, HashSet<string> validStateIds)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                return false;
            }

            if (string.Equals(stateId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal) ||
                string.Equals(stateId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal))
            {
                return true;
            }

            return validStateIds.Contains(stateId);
        }

        private static bool IsRepairValidToEndpoint(string stateId, HashSet<string> validStateIds)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                return false;
            }

            if (string.Equals(stateId, FusionAnimatorGraphAsset.SpecialNodeExitId, StringComparison.Ordinal))
            {
                return true;
            }

            return validStateIds.Contains(stateId);
        }

        private static bool IsValidIndexParameterReference(
            string parameterReferenceId,
            HashSet<string> validParameterIds,
            HashSet<string> validIntParameterIds)
        {
            if (string.IsNullOrWhiteSpace(parameterReferenceId))
            {
                return false;
            }

            if (FusionAnimatorParameterReferenceUtility.TryParse(parameterReferenceId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false)
            {
                return false;
            }

            if (component != FusionAnimatorParameterComponent.None)
            {
                return false;
            }

            if (validParameterIds.Contains(baseParameterId) == false)
            {
                return false;
            }

            return validIntParameterIds.Contains(baseParameterId);
        }

        private static bool CleanInvalidConditionReferences(
            List<FusionAnimatorConditionDefinition> conditions,
            HashSet<string> validParameterIds,
            out int removedCount)
        {
            removedCount = 0;
            if (conditions == null || conditions.Count == 0)
            {
                return false;
            }

            bool changed = false;
            for (int conditionIndex = conditions.Count - 1; conditionIndex >= 0; --conditionIndex)
            {
                FusionAnimatorConditionDefinition condition = conditions[conditionIndex];
                bool removeCondition = false;
                if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
                {
                    removeCondition = true;
                }
                else if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string baseParameterId, out _) == false ||
                         validParameterIds.Contains(baseParameterId) == false)
                {
                    removeCondition = true;
                }

                if (removeCondition == false)
                {
                    continue;
                }

                conditions.RemoveAt(conditionIndex);
                removedCount++;
                changed = true;
            }

            return changed;
        }

        private void SaveGraph()
        {
            if (_graph == null)
            {
                return;
            }

            EditorUtility.SetDirty(_graph);
            AssetDatabase.SaveAssets();
        }

        private void MarkGraphDirty()
        {
            if (_graph == null)
            {
                return;
            }

            EditorUtility.SetDirty(_graph);
            InvalidatePreviewRuntimeSimulation();
            RefreshScopeMenu();
            RefreshScopeBreadcrumb();
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private void PersistGraphSourceReference(UnityEngine.Object source, bool saveAsset)
        {
            if (_graph == null)
            {
                return;
            }

            if (TryNormalizeConvertSource(source, out UnityEngine.Object normalizedSource) == false)
            {
                normalizedSource = null;
            }
            source = normalizedSource;

            if (ReferenceEquals(_graph.PreviewSource, source))
            {
                return;
            }

            Undo.RecordObject(_graph, "Set FusionAnimator Source Reference");
            _graph.PreviewSource = source;
            EditorUtility.SetDirty(_graph);
            if (saveAsset)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private void PersistGraphTargetReference(GameObject target, bool saveAsset)
        {
            if (_graph == null)
            {
                return;
            }

            GameObject persistentTarget = null;
            string globalObjectId = string.Empty;
            if (target != null)
            {
                if (EditorUtility.IsPersistent(target))
                {
                    persistentTarget = target;
                }
                else
                {
                    GlobalObjectId targetId = GlobalObjectId.GetGlobalObjectIdSlow(target);
                    globalObjectId = targetId.ToString();
                }
            }

            if (ReferenceEquals(_graph.PreviewTarget, persistentTarget) &&
                string.Equals(_graph.PreviewTargetGlobalObjectId, globalObjectId, StringComparison.Ordinal))
            {
                return;
            }

            Undo.RecordObject(_graph, "Set FusionAnimator Target Reference");
            _graph.PreviewTarget = persistentTarget;
            _graph.PreviewTargetGlobalObjectId = globalObjectId;
            EditorUtility.SetDirty(_graph);
            if (saveAsset)
            {
                AssetDatabase.SaveAssets();
            }
        }

        private GameObject ResolvePersistedPreviewTarget()
        {
            if (_graph == null)
            {
                return null;
            }

            if (_graph.PreviewTarget != null)
            {
                return _graph.PreviewTarget;
            }

            if (string.IsNullOrWhiteSpace(_graph.PreviewTargetGlobalObjectId))
            {
                return null;
            }

            if (GlobalObjectId.TryParse(_graph.PreviewTargetGlobalObjectId, out GlobalObjectId objectId))
            {
                return GlobalObjectId.GlobalObjectIdentifierToObjectSlow(objectId) as GameObject;
            }

            return null;
        }

        private void RecordUndo(string action)
        {
            if (_graph == null)
            {
                return;
            }

            Undo.RecordObject(_graph, action);
        }

        private void OnGraphSelectionChanged(FusionAnimatorStateDefinition state, FusionAnimatorTransitionDefinition transition)
        {
            if (_suppressGraphSelectionChangedDepth > 0)
            {
                return;
            }

            if (_graphView != null && _graphView.GetSelectionCount() > 1)
            {
                _selectedParameterIndices.Clear();
                _selectedBindingIndices.Clear();
                _selectedLayerIndices.Clear();
                _selectedStateIds.Clear();
                _selectedScopeKeys.Clear();
                _selectedBindingGroupId = string.Empty;
                _selectedState = null;
                _selectedTransition = null;
                _selectedEntryLinkTargetStateId = string.Empty;
                _selectedLayerScopePath = string.Empty;
                _selectedParameterIndex = -1;
                _selectedBindingIndex = -1;
                if (_graphView.TryGetSelectedLayerIds(_scratchSelectedLayerIds))
                {
                    for (int i = 0; i < _scratchSelectedLayerIds.Count; ++i)
                    {
                        int layerIndex = FindLayerIndexById(_scratchSelectedLayerIds[i]);
                        if (layerIndex >= 0)
                        {
                            _selectedLayerIndices.Add(layerIndex);
                        }
                    }
                }

                if (_graphView.TryGetSelectedStateIds(_scratchSelectedStateIds))
                {
                    for (int i = 0; i < _scratchSelectedStateIds.Count; ++i)
                    {
                        FusionAnimatorStateDefinition selectedState = FindStateById(_scratchSelectedStateIds[i]);
                        if (selectedState == null || string.IsNullOrWhiteSpace(selectedState.Id))
                        {
                            continue;
                        }

                        _selectedStateIds.Add(selectedState.Id);
                        int selectedStateLayerIndex = FindLayerIndexById(selectedState.LayerId);
                        if (selectedStateLayerIndex >= 0)
                        {
                            _selectedLayerIndices.Add(selectedStateLayerIndex);
                        }
                    }
                }

                if (_graphView.TryGetSelectedScopePaths(_scratchSelectedScopePaths))
                {
                    string scopeLayerId = _activeLayerId ?? string.Empty;
                    for (int i = 0; i < _scratchSelectedScopePaths.Count; ++i)
                    {
                        string scopePath = _scratchSelectedScopePaths[i] ?? string.Empty;
                        _selectedScopeKeys.Add(BuildScopeSelectionKey(scopeLayerId, scopePath));
                    }
                }

                _selectedLayerIndex = ResolveFirstSelectedIndex(_selectedLayerIndices);
                _leftLibraryTab = LeftLibraryTab.Layers;
                _graphView.SetSelectedTransition(null);
                _graphView.SetHoveredLayer(null);
                _graphView.SetHoveredParameter(null);
                FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
                _leftPanel?.MarkDirtyRepaint();
                _inspector?.MarkDirtyRepaint();
                return;
            }

            _selectedParameterIndices.Clear();
            _selectedBindingIndices.Clear();
            _selectedLayerIndices.Clear();
            _selectedStateIds.Clear();
            _selectedScopeKeys.Clear();
            _selectedBindingGroupId = string.Empty;
            _selectedState = state;
            _selectedTransition = transition;
            _selectedEntryLinkTargetStateId = string.Empty;
            if (state != null || transition != null)
            {
                _selectedLayerScopePath = string.Empty;
            }

            if (state == null && transition == null)
            {
                bool resolvedSpecialSelection = false;
                if (_graphView != null && _graphView.TryGetSelectedEntryLinkTargetStateId(out string entryTargetStateId))
                {
                    _selectedEntryLinkTargetStateId = entryTargetStateId ?? string.Empty;
                    _selectedLayerScopePath = string.Empty;
                    _selectedLayerIndex = -1;
                    resolvedSpecialSelection = true;
                }
                else if (_graphView != null && _graphView.TryGetSelectedScopePath(out string selectedScopePath))
                {
                    _selectedLayerScopePath = selectedScopePath ?? string.Empty;
                    _selectedEntryLinkTargetStateId = string.Empty;
                    if (_selectedLayerIndex < 0 && string.IsNullOrWhiteSpace(_activeLayerId) == false)
                    {
                        _selectedLayerIndex = FindLayerIndexById(_activeLayerId);
                    }

                    if (string.IsNullOrWhiteSpace(_activeLayerId) == false &&
                        string.IsNullOrWhiteSpace(_selectedLayerScopePath) == false)
                    {
                        _selectedScopeKeys.Add(BuildScopeSelectionKey(_activeLayerId, _selectedLayerScopePath));
                    }

                    resolvedSpecialSelection = true;
                }

                if (resolvedSpecialSelection == false)
                {
                    if (string.IsNullOrWhiteSpace(_activeLayerId))
                    {
                        _selectedLayerIndex = -1;
                        _selectedLayerScopePath = string.Empty;
                    }
                    else
                    {
                        _selectedLayerIndex = FindLayerIndexById(_activeLayerId);
                        _selectedLayerScopePath = NormalizeScopePath(_activeScopePath);
                    }
                }
            }

            _graphView?.SetSelectedTransition(transition != null ? transition.Id : null);
            if (state != null)
            {
                _selectedStateIds.Add(state.Id);
                _selectedParameterIndex = -1;
                _selectedBindingIndex = -1;
                _selectedLayerIndex = FindLayerIndexById(state.LayerId);
            }
            else if (transition != null)
            {
                _selectedParameterIndex = -1;
                _selectedBindingIndex = -1;
                string transitionLayerId = FindStateById(transition.FromStateId)?.LayerId;
                if (string.IsNullOrWhiteSpace(transitionLayerId))
                {
                    transitionLayerId = FindStateById(transition.ToStateId)?.LayerId;
                }

                _selectedLayerIndex = FindLayerIndexById(transitionLayerId);
            }
            else if (string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false)
            {
                _selectedParameterIndex = -1;
                _selectedBindingIndex = -1;
                _selectedLayerIndex = -1;
            }

            string selectedStateId = state != null ? state.Id : null;
            string selectedTransitionId = transition != null ? transition.Id : null;
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(null);
            if (state == null &&
                transition == null &&
                string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) &&
                string.IsNullOrWhiteSpace(_selectedLayerScopePath) &&
                _selectedLayerIndex < 0)
            {
                ResetPreviewToDefaultSimulation();
            }

            FusionAnimatorEditorSelectionContext.SetSelection(_graph, selectedStateId, selectedTransitionId);
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private void WithSuppressedGraphSelectionChanged(Action action)
        {
            _suppressGraphSelectionChangedDepth++;
            try
            {
                action?.Invoke();
            }
            finally
            {
                _suppressGraphSelectionChangedDepth = Mathf.Max(0, _suppressGraphSelectionChangedDepth - 1);
            }
        }

        private void ClearGraphSelectionForLibraryInteraction()
        {
            if (_graphView == null)
            {
                return;
            }

            WithSuppressedGraphSelectionChanged(() =>
            {
                _graphView.ClearSelection();
            });
        }

        private void OnGraphLayerNodeSelected(string layerId)
        {
            ClearLibraryMultiSelectionState();
            _selectedState = null;
            _selectedTransition = null;
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            _selectedLayerIndex = FindLayerIndexById(layerId);
            if (_selectedLayerIndex < 0)
            {
                _selectedLayerIndex = -1;
            }

            _leftLibraryTab = LeftLibraryTab.Layers;
            _graphView?.SetSelectedTransition(null);
            _graphView?.SetHoveredLayer(_selectedLayerIndex >= 0 ? layerId : null);
            _graphView?.SetHoveredParameter(null);

            FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private void OnGraphScopeNodeRenameRequested(string scopePath)
        {
            if (_graph == null || string.IsNullOrWhiteSpace(scopePath) || string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return;
            }

            int layerIndex = FindLayerIndexById(_activeLayerId);
            if (layerIndex < 0)
            {
                return;
            }

            _selectedState = null;
            _selectedTransition = null;
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedEntryLinkTargetStateId = string.Empty;
            _selectedLayerIndex = layerIndex;
            _selectedLayerScopePath = NormalizeScopePath(scopePath);
            _focusScopeRenameField = true;
            _graphView?.SetSelectedTransition(null);
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private int FindLayerIndexById(string layerId)
        {
            if (_graph == null || _graph.Layers == null || string.IsNullOrWhiteSpace(layerId))
            {
                return -1;
            }

            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null && string.Equals(layer.Id, layerId, StringComparison.Ordinal))
                {
                    return i;
                }
            }

            return -1;
        }

        private void OnCanvasBackgroundClicked()
        {
            ClearLibraryMultiSelectionState();
            bool changed = false;
            if (_selectedState != null || _selectedTransition != null)
            {
                changed = true;
            }

            _selectedState = null;
            _selectedTransition = null;
            if (_selectedParameterIndex >= 0)
            {
                _selectedParameterIndex = -1;
                changed = true;
            }

            if (_selectedBindingIndex >= 0)
            {
                _selectedBindingIndex = -1;
                changed = true;
            }

            if (string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false)
            {
                _selectedEntryLinkTargetStateId = string.Empty;
                changed = true;
            }

            string normalizedActiveLayerId = string.IsNullOrWhiteSpace(_activeLayerId) ? string.Empty : _activeLayerId;
            string normalizedActiveScopePath = NormalizeScopePath(_activeScopePath);
            if (string.IsNullOrWhiteSpace(normalizedActiveLayerId))
            {
                if (_selectedLayerIndex != -1)
                {
                    _selectedLayerIndex = -1;
                    changed = true;
                }

                if (string.IsNullOrWhiteSpace(_selectedLayerScopePath) == false)
                {
                    _selectedLayerScopePath = string.Empty;
                    changed = true;
                }
            }
            else
            {
                int contextLayerIndex = FindLayerIndexById(normalizedActiveLayerId);
                if (_selectedLayerIndex != contextLayerIndex)
                {
                    _selectedLayerIndex = contextLayerIndex;
                    changed = true;
                }

                if (string.Equals(_selectedLayerScopePath, normalizedActiveScopePath, StringComparison.OrdinalIgnoreCase) == false)
                {
                    _selectedLayerScopePath = normalizedActiveScopePath;
                    changed = true;
                }
            }

            ClearGraphSelectionForLibraryInteraction();
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(null);
            if (changed)
            {
                ResetPreviewToDefaultSimulation();
                FusionAnimatorEditorSelectionContext.SetSelection(_graph, null, null);
                _leftPanel?.MarkDirtyRepaint();
                _inspector?.MarkDirtyRepaint();
            }
        }

        private void CreateGraphAsset()
        {
            const string defaultPath = "Assets/FusionAnimator/Graphs";
            EnsureFolder("Assets/FusionAnimator");
            EnsureFolder(defaultPath);

            string path = EditorUtility.SaveFilePanelInProject("Create FusionAnimator Graph", "FusionAnimatorGraph", "asset", "Create graph", defaultPath);
            if (string.IsNullOrWhiteSpace(path))
            {
                return;
            }

            FusionAnimatorGraphAsset graph = CreateInstance<FusionAnimatorGraphAsset>();
            graph.GraphId = FusionAnimatorGraphAsset.NewId("graph");
            graph.DisplayName = "Fusion Animator Graph";
            AssetDatabase.CreateAsset(graph, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Selection.activeObject = graph;
            BindGraph(graph);
        }

        private void RefreshScopeMenu()
        {
            if (_scopeMenu == null)
            {
                return;
            }

            if (_graph == null)
            {
                _scopeMenu.SetEnabled(false);
                _scopeMenu.text = "View: None";
                return;
            }

            EnsureActiveLayerContext();
            List<string> scopes = GetAvailableScopes(_activeLayerId);
            if (string.IsNullOrWhiteSpace(_activeLayerId))
            {
                _activeScopePath = string.Empty;
                _scopeMenu.SetEnabled(_graph.Layers != null && _graph.Layers.Count > 0);
                _scopeMenu.text = "View: Layers";
                return;
            }

            if (string.IsNullOrWhiteSpace(_activeScopePath) == false &&
                scopes.Contains(_activeScopePath, StringComparer.OrdinalIgnoreCase) == false)
            {
                _activeScopePath = string.Empty;
            }

            string layerDisplayName = GetLayerDisplayName(_activeLayerId);
            _scopeMenu.SetEnabled(true);
            _scopeMenu.text = string.IsNullOrWhiteSpace(_activeScopePath)
                ? ("View: " + layerDisplayName)
                : ("View: " + layerDisplayName + "/" + _activeScopePath);
        }

        private void ShowScopeMenu()
        {
            if (_graph == null)
            {
                return;
            }

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Layers/Overview"), string.IsNullOrWhiteSpace(_activeLayerId), () =>
            {
                SetActiveLayer(string.Empty);
            });

            if (_graph.Layers != null)
            {
                for (int i = 0; i < _graph.Layers.Count; ++i)
                {
                    FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                    if (layer == null || string.IsNullOrWhiteSpace(layer.Id))
                    {
                        continue;
                    }

                    string layerId = layer.Id;
                    string displayName = GetLayerDisplayName(layerId);
                    bool selected = string.Equals(layerId, _activeLayerId, StringComparison.Ordinal) &&
                                    string.IsNullOrWhiteSpace(_activeScopePath);
                    menu.AddItem(new GUIContent("Layers/" + displayName), selected, () =>
                    {
                        SetActiveLayer(layerId);
                    });
                }
            }

            if (string.IsNullOrWhiteSpace(_activeLayerId) == false)
            {
                menu.AddSeparator(string.Empty);
                menu.AddItem(new GUIContent("Scope/Root"), string.IsNullOrWhiteSpace(_activeScopePath), () =>
                {
                    SetScopePath(string.Empty);
                });

                List<string> scopes = GetAvailableScopes(_activeLayerId);
                for (int i = 0; i < scopes.Count; ++i)
                {
                    string scopePath = scopes[i];
                    bool selected = string.Equals(scopePath, _activeScopePath, StringComparison.OrdinalIgnoreCase);
                    menu.AddItem(new GUIContent("Scope/" + scopePath), selected, () =>
                    {
                        SetScopePath(scopePath);
                    });
                }
            }

            menu.ShowAsContext();
        }

        private List<string> GetAvailableScopes(string layerId)
        {
            HashSet<string> scopeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (_graph != null && _graph.States != null && string.IsNullOrWhiteSpace(layerId) == false)
            {
                for (int i = 0, count = _graph.States.Count; i < count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state == null || string.IsNullOrWhiteSpace(state.Name))
                    {
                        continue;
                    }

                    if (string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string scopePath = GetStateScopePathFromName(state.Name);
                    if (string.IsNullOrWhiteSpace(scopePath))
                    {
                        continue;
                    }

                    string[] parts = scopePath.Split('/');
                    string current = string.Empty;
                    for (int p = 0; p < parts.Length; ++p)
                    {
                        string part = parts[p].Trim();
                        if (string.IsNullOrWhiteSpace(part))
                        {
                            continue;
                        }

                        current = string.IsNullOrWhiteSpace(current) ? part : (current + "/" + part);
                        scopeSet.Add(current);
                    }
                }
            }

            return scopeSet.OrderBy(scope => scope, StringComparer.OrdinalIgnoreCase).ToList();
        }

        private void SetScopePath(string scopePath)
        {
            if (string.IsNullOrWhiteSpace(_activeLayerId))
            {
                _activeScopePath = string.Empty;
                _selectedLayerScopePath = string.Empty;
                _selectedEntryLinkTargetStateId = string.Empty;
                ClearGraphSelectionForLibraryInteraction();
                _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
                RefreshScopeMenu();
                RefreshScopeBreadcrumb();
                ResetPreviewToDefaultSimulation();
                return;
            }

            string normalized = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath.Trim();
            if (string.Equals(_activeScopePath, normalized, StringComparison.Ordinal))
            {
                return;
            }

            _activeScopePath = normalized;
            _selectedState = null;
            _selectedTransition = null;
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            ClearGraphSelectionForLibraryInteraction();
            _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
            RefreshScopeMenu();
            RefreshScopeBreadcrumb();
            ResetPreviewToDefaultSimulation();
        }

        private VisualElement BuildScopeBreadcrumbBar()
        {
            VisualElement root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;
            root.style.paddingLeft = 6.0f;
            root.style.paddingRight = 6.0f;
            root.style.paddingTop = 4.0f;
            root.style.paddingBottom = 4.0f;
            root.style.backgroundColor = new Color(0.10f, 0.10f, 0.10f, 0.72f);
            root.style.borderTopLeftRadius = 4.0f;
            root.style.borderTopRightRadius = 4.0f;
            root.style.borderBottomLeftRadius = 4.0f;
            root.style.borderBottomRightRadius = 4.0f;
            root.style.borderLeftWidth = 1.0f;
            root.style.borderRightWidth = 1.0f;
            root.style.borderTopWidth = 1.0f;
            root.style.borderBottomWidth = 1.0f;
            root.style.borderLeftColor = new Color(1.0f, 1.0f, 1.0f, 0.14f);
            root.style.borderRightColor = new Color(1.0f, 1.0f, 1.0f, 0.14f);
            root.style.borderTopColor = new Color(1.0f, 1.0f, 1.0f, 0.14f);
            root.style.borderBottomColor = new Color(1.0f, 1.0f, 1.0f, 0.14f);
            return root;
        }

        private void RefreshScopeBreadcrumb()
        {
            if (_scopeBreadcrumbRoot == null)
            {
                return;
            }

            _scopeBreadcrumbRoot.Clear();
            if (_graph == null)
            {
                _scopeBreadcrumbRoot.style.display = DisplayStyle.None;
                return;
            }

            _scopeBreadcrumbRoot.style.display = DisplayStyle.Flex;
            AddBreadcrumbButton("Overview", () =>
            {
                _activeLayerId = string.Empty;
                _activeScopePath = string.Empty;
                _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
                RefreshScopeMenu();
                RefreshScopeBreadcrumb();
            });

            if (string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return;
            }

            FusionAnimatorLayerDefinition activeLayer = FindLayerById(_activeLayerId);
            if (activeLayer == null)
            {
                return;
            }

            AddBreadcrumbSeparator();
            AddBreadcrumbButton(string.IsNullOrWhiteSpace(activeLayer.Name) ? activeLayer.Id : activeLayer.Name, () =>
            {
                _activeScopePath = string.Empty;
                _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
                RefreshScopeMenu();
                RefreshScopeBreadcrumb();
            });

            if (string.IsNullOrWhiteSpace(_activeScopePath))
            {
                return;
            }

            string[] scopeParts = _activeScopePath.Split('/');
            string cumulative = string.Empty;
            for (int i = 0; i < scopeParts.Length; ++i)
            {
                string part = scopeParts[i].Trim();
                if (string.IsNullOrWhiteSpace(part))
                {
                    continue;
                }

                cumulative = string.IsNullOrWhiteSpace(cumulative) ? part : cumulative + "/" + part;
                string path = cumulative;
                AddBreadcrumbSeparator();
                AddBreadcrumbButton(part, () =>
                {
                    _activeScopePath = path;
                    _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
                    RefreshScopeMenu();
                    RefreshScopeBreadcrumb();
                });
            }
        }

        private void AddBreadcrumbButton(string text, Action onClick)
        {
            Button button = new Button(() => onClick?.Invoke())
            {
                text = text,
            };
            button.style.unityTextAlign = TextAnchor.MiddleLeft;
            button.style.marginLeft = 0.0f;
            button.style.marginRight = 0.0f;
            button.style.paddingLeft = 4.0f;
            button.style.paddingRight = 4.0f;
            button.style.height = 20.0f;
            _scopeBreadcrumbRoot.Add(button);
        }

        private void AddBreadcrumbSeparator()
        {
            Label separator = new Label(">");
            separator.style.marginLeft = 4.0f;
            separator.style.marginRight = 4.0f;
            separator.style.unityTextAlign = TextAnchor.MiddleCenter;
            separator.style.color = new Color(0.78f, 0.78f, 0.78f, 1.0f);
            _scopeBreadcrumbRoot.Add(separator);
        }

        private void HandleGraphScopeChanged(string layerId, string scopePath)
        {
            _activeLayerId = string.IsNullOrWhiteSpace(layerId) ? string.Empty : layerId;
            _activeScopePath = string.IsNullOrWhiteSpace(scopePath) ? string.Empty : scopePath;
            _selectedState = null;
            _selectedTransition = null;
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            _selectedLayerIndex = FindLayerIndexById(_activeLayerId);
            RefreshScopeMenu();
            RefreshScopeBreadcrumb();
            ResetPreviewToDefaultSimulation();
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private void SetActiveLayer(string layerId)
        {
            string normalizedLayerId = string.IsNullOrWhiteSpace(layerId) ? string.Empty : layerId;
            if (string.Equals(_activeLayerId, normalizedLayerId, StringComparison.Ordinal) &&
                string.IsNullOrWhiteSpace(_activeScopePath))
            {
                return;
            }

            _activeLayerId = normalizedLayerId;
            _activeScopePath = string.Empty;
            _selectedState = null;
            _selectedTransition = null;
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            _selectedLayerIndex = FindLayerIndexById(_activeLayerId);
            ClearGraphSelectionForLibraryInteraction();
            _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
            RefreshScopeMenu();
            RefreshScopeBreadcrumb();
            ResetPreviewToDefaultSimulation();
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private void EnsureActiveLayerContext()
        {
            if (_graph == null || _graph.Layers == null || _graph.Layers.Count == 0)
            {
                _activeLayerId = string.Empty;
                _activeScopePath = string.Empty;
                return;
            }

            if (string.IsNullOrWhiteSpace(_activeLayerId))
            {
                return;
            }

            if (FindLayerById(_activeLayerId) != null)
            {
                return;
            }

            _activeLayerId = string.Empty;
            _activeScopePath = string.Empty;
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

        private string GetLayerDisplayName(string layerId)
        {
            FusionAnimatorLayerDefinition layer = FindLayerById(layerId);
            if (layer == null)
            {
                return string.IsNullOrWhiteSpace(layerId) ? "Layer" : layerId;
            }

            return string.IsNullOrWhiteSpace(layer.Name) ? layer.Id : layer.Name;
        }

        private static string GetStateScopePathFromName(string stateName)
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

            string scope = stateName.Substring(0, separator).Trim();
            return scope.Trim('/');
        }

        private static bool IsScopeSentinelStateName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return false;
            }

            int separator = stateName.LastIndexOf('/');
            string leaf = separator >= 0 ? stateName.Substring(separator + 1) : stateName;
            return string.Equals(leaf, FusionAnimatorGraphAsset.ScopeSentinelStateLeafName, StringComparison.Ordinal);
        }

        private static string GetScopeLeafName(string scopePath)
        {
            string normalized = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return string.Empty;
            }

            int separator = normalized.LastIndexOf('/');
            return separator >= 0 ? normalized.Substring(separator + 1) : normalized;
        }

        private static bool IsSameScopeOrChild(string candidateScopePath, string parentScopePath)
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

        private static string ReplaceScopePathPrefix(string scopePath, string fromScopePath, string toScopePath)
        {
            string normalizedScope = NormalizeScopePath(scopePath);
            string normalizedFrom = NormalizeScopePath(fromScopePath);
            string normalizedTo = NormalizeScopePath(toScopePath);
            if (string.IsNullOrWhiteSpace(normalizedScope) || string.IsNullOrWhiteSpace(normalizedFrom))
            {
                return normalizedScope;
            }

            if (string.Equals(normalizedScope, normalizedFrom, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTo;
            }

            if (normalizedScope.StartsWith(normalizedFrom + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTo + normalizedScope.Substring(normalizedFrom.Length);
            }

            return normalizedScope;
        }

        private static string ReplaceStateNameScopePrefix(string stateName, string fromScopePath, string toScopePath)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return stateName;
            }

            string normalizedFrom = NormalizeScopePath(fromScopePath);
            string normalizedTo = NormalizeScopePath(toScopePath);
            if (string.IsNullOrWhiteSpace(normalizedFrom))
            {
                return stateName;
            }

            if (string.Equals(stateName, normalizedFrom, StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTo;
            }

            if (stateName.StartsWith(normalizedFrom + "/", StringComparison.OrdinalIgnoreCase))
            {
                return normalizedTo + stateName.Substring(normalizedFrom.Length);
            }

            return stateName;
        }

        private bool TryRenameSubStateMachineScope(
            string layerId,
            string scopePath,
            string requestedLeafName,
            bool showValidationDialog,
            out string renamedScopePath)
        {
            renamedScopePath = NormalizeScopePath(scopePath);
            if (_graph == null || _graph.States == null || string.IsNullOrWhiteSpace(layerId))
            {
                return false;
            }

            string sourceScopePath = NormalizeScopePath(scopePath);
            if (string.IsNullOrWhiteSpace(sourceScopePath))
            {
                return false;
            }

            string requestedLeaf = string.IsNullOrWhiteSpace(requestedLeafName) ? string.Empty : requestedLeafName.Trim();
            if (string.IsNullOrWhiteSpace(requestedLeaf) || requestedLeaf.Contains("/"))
            {
                if (showValidationDialog)
                {
                    EditorUtility.DisplayDialog(
                        "Rename Sub-State Machine",
                        "Name must be non-empty and cannot contain '/'.",
                        "OK");
                }

                return false;
            }

            string parentScopePath = GetStateScopePathFromName(sourceScopePath);
            string targetScopePath = string.IsNullOrWhiteSpace(parentScopePath)
                ? requestedLeaf
                : parentScopePath + "/" + requestedLeaf;
            targetScopePath = NormalizeScopePath(targetScopePath);

            if (string.Equals(sourceScopePath, targetScopePath, StringComparison.OrdinalIgnoreCase))
            {
                renamedScopePath = sourceScopePath;
                return false;
            }

            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string stateScopePath = NormalizeScopePath(GetStateScopePathFromName(state.Name));
                if (IsSameScopeOrChild(stateScopePath, sourceScopePath))
                {
                    continue;
                }

                if (IsSameScopeOrChild(stateScopePath, targetScopePath))
                {
                    if (showValidationDialog)
                    {
                        EditorUtility.DisplayDialog(
                            "Rename Sub-State Machine",
                            string.Format("A scope with path '{0}' already exists in this layer.", targetScopePath),
                            "OK");
                    }

                    return false;
                }
            }

            if (_graph.ScopeUtilityNodeLayouts != null)
            {
                for (int i = 0; i < _graph.ScopeUtilityNodeLayouts.Count; ++i)
                {
                    FusionAnimatorScopeUtilityNodeLayout layout = _graph.ScopeUtilityNodeLayouts[i];
                    if (layout == null || string.Equals(layout.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string layoutScopePath = NormalizeScopePath(layout.ScopePath);
                    if (IsSameScopeOrChild(layoutScopePath, sourceScopePath))
                    {
                        continue;
                    }

                    if (IsSameScopeOrChild(layoutScopePath, targetScopePath))
                    {
                        if (showValidationDialog)
                        {
                            EditorUtility.DisplayDialog(
                                "Rename Sub-State Machine",
                                string.Format("A scope layout with path '{0}' already exists in this layer.", targetScopePath),
                                "OK");
                        }

                        return false;
                    }
                }
            }

            RecordUndo("Rename FusionAnimator Sub-State Machine");
            bool changed = false;
            for (int i = 0; i < _graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = _graph.States[i];
                if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string renamedStateName = ReplaceStateNameScopePrefix(state.Name, sourceScopePath, targetScopePath);
                if (string.Equals(renamedStateName, state.Name, StringComparison.Ordinal))
                {
                    continue;
                }

                state.Name = renamedStateName;
                changed = true;
            }

            if (_graph.ScopeUtilityNodeLayouts != null)
            {
                for (int i = 0; i < _graph.ScopeUtilityNodeLayouts.Count; ++i)
                {
                    FusionAnimatorScopeUtilityNodeLayout layout = _graph.ScopeUtilityNodeLayouts[i];
                    if (layout == null || string.Equals(layout.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string renamedLayoutScopePath = ReplaceScopePathPrefix(layout.ScopePath, sourceScopePath, targetScopePath);
                    if (string.Equals(renamedLayoutScopePath, NormalizeScopePath(layout.ScopePath), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    layout.ScopePath = renamedLayoutScopePath;
                    changed = true;
                }
            }

            if (_graph.ScopeTransitionSuppressions != null)
            {
                for (int i = 0; i < _graph.ScopeTransitionSuppressions.Count; ++i)
                {
                    FusionAnimatorScopeTransitionSuppression suppression = _graph.ScopeTransitionSuppressions[i];
                    if (suppression == null || string.Equals(suppression.LayerId, layerId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    string renamedSuppressedScopePath = ReplaceScopePathPrefix(suppression.ScopePath, sourceScopePath, targetScopePath);
                    if (string.Equals(renamedSuppressedScopePath, NormalizeScopePath(suppression.ScopePath), StringComparison.Ordinal))
                    {
                        continue;
                    }

                    suppression.ScopePath = renamedSuppressedScopePath;
                    changed = true;
                }
            }

            if (changed == false)
            {
                return false;
            }

            _activeScopePath = ReplaceScopePathPrefix(_activeScopePath, sourceScopePath, targetScopePath);
            _selectedLayerScopePath = ReplaceScopePathPrefix(_selectedLayerScopePath, sourceScopePath, targetScopePath);
            _selectedLayerIndex = FindLayerIndexById(layerId);
            _graphView?.SetRenderContext(_activeLayerId, _activeScopePath);
            _graphView?.RebuildFromGraphData();
            if (_selectedLayerIndex >= 0 && string.IsNullOrWhiteSpace(_selectedLayerScopePath) == false)
            {
                _graphView?.SelectScopeNodeByPath(_selectedLayerScopePath);
            }

            RefreshScopeMenu();
            RefreshScopeBreadcrumb();
            ResetPreviewToDefaultSimulation();
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
            MarkGraphDirty();

            renamedScopePath = targetScopePath;
            return true;
        }

        private void ShowConvertMenu()
        {
            if (_graph == null)
            {
                EditorUtility.DisplayDialog("Fusion Animator Convert", "Create or assign a FusionAnimator graph before converting.", "OK");
                return;
            }

            UnityEngine.Object source = _convertSourceField != null ? _convertSourceField.value : null;
            if (source == null && _graph != null)
            {
                source = _graph.PreviewSource;
            }
            if (TryNormalizeConvertSource(source, out UnityEngine.Object normalizedSource))
            {
                source = normalizedSource;
            }
            else
            {
                source = null;
            }

            GenericMenu menu = new GenericMenu();
            IFusionAnimatorGraphConverter converterForSource = ResolveConverterForSource(source);
            if (converterForSource != null)
            {
                menu.AddItem(new GUIContent("As-is"), false, () => RunConvert(converterForSource, source, false));
                menu.AddItem(new GUIContent("Normalize"), false, () => RunConvert(converterForSource, source, true));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("As-is"));
                menu.AddDisabledItem(new GUIContent("Normalize"));
                menu.AddSeparator("");
                menu.AddDisabledItem(new GUIContent("Select a compatible source asset in Source field"));
            }

            menu.ShowAsContext();
        }

        private void RunConvert(IFusionAnimatorGraphConverter converter, UnityEngine.Object source, bool normalizeImport)
        {
            if (converter == null || source == null || _graph == null)
            {
                return;
            }

            string importModeLabel = normalizeImport ? "Normalize" : "As-is";

            bool confirmed = EditorUtility.DisplayDialog(
                "Fusion Animator Convert",
                string.Format(
                    "Import mode: {0}\n\nConverting will overwrite this graph's layers, states, transitions, and parameters.\n\nContinue?",
                    importModeLabel),
                importModeLabel,
                "Cancel");
            if (confirmed == false)
            {
                return;
            }

            Undo.RecordObject(_graph, "Convert FusionAnimator Graph");

            if (converter.TryConvert(source, _graph, out string message))
            {
                UpgradeLegacyLookPoseBlendTrees(_graph);

                if (normalizeImport)
                {
                    NormalizeImportedBlendTrees(_graph);
                }

                PersistGraphSourceReference(source, false);
                PersistGraphTargetReference(_previewTarget, false);
                EditorUtility.SetDirty(_graph);
                _graphView?.RebuildFromGraphData();
                _graphView?.SetPreviewApplyRootMotion(_graph.ApplyRootMotion);
                _graphView?.FrameAll();
                _leftPanel?.MarkDirtyRepaint();
                _inspector?.MarkDirtyRepaint();

                Debug.Log(string.Format("FusionAnimator conversion succeeded: {0}", message), _graph);
            }
            else
            {
                Debug.LogError(string.Format("FusionAnimator conversion failed: {0}", message), _graph);
            }
        }

        private void ShowSourceSelectionMenu()
        {
            GenericMenu menu = new GenericMenu();
            int entryCount = 0;

            string[] animatorGuids = AssetDatabase.FindAssets("t:AnimatorController");
            if (animatorGuids != null && animatorGuids.Length > 0)
            {
                for (int i = 0; i < animatorGuids.Length; ++i)
                {
                    string guid = animatorGuids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                    if (controller == null)
                    {
                        continue;
                    }

                    string label = string.Format("Animator Controllers/{0}", path);
                    bool selected = ReferenceEquals(_convertSourceField != null ? _convertSourceField.value : null, controller);
                    AnimatorController capturedController = controller;
                    menu.AddItem(new GUIContent(label), selected, () => AssignConvertSource(capturedController));
                    ++entryCount;
                }
            }

            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab");
            if (prefabGuids != null && prefabGuids.Length > 0)
            {
                menu.AddSeparator("");
                for (int i = 0; i < prefabGuids.Length; ++i)
                {
                    string guid = prefabGuids[i];
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                    if (prefab == null || IsCharacterAnimationControllerPrefab(prefab) == false)
                    {
                        continue;
                    }

                    string label = string.Format("Character Prefabs/{0}", path);
                    bool selected = ReferenceEquals(_convertSourceField != null ? _convertSourceField.value : null, prefab);
                    GameObject capturedPrefab = prefab;
                    menu.AddItem(new GUIContent(label), selected, () => AssignConvertSource(capturedPrefab));
                    ++entryCount;
                }
            }

            if (entryCount == 0)
            {
                menu.AddDisabledItem(new GUIContent("No compatible sources found"));
            }

            menu.ShowAsContext();
        }

        private void AssignConvertSource(UnityEngine.Object source)
        {
            if (TryNormalizeConvertSource(source, out UnityEngine.Object normalizedSource) == false)
            {
                EditorUtility.DisplayDialog(
                    "Fusion Animator Source",
                    "Source must be either:\n- an AnimatorController asset, or\n- a prefab containing CharacterAnimationController.",
                    "OK");
                return;
            }

            if (_convertSourceField != null)
            {
                _convertSourceField.SetValueWithoutNotify(normalizedSource);
            }
            PersistGraphSourceReference(normalizedSource, false);
        }

        private static IFusionAnimatorGraphConverter ResolveConverterForSource(UnityEngine.Object source)
        {
            if (source == null)
            {
                return null;
            }

            IReadOnlyList<IFusionAnimatorGraphConverter> converters = FusionAnimatorGraphConverterRegistry.GetConverters();
            for (int i = 0, count = converters.Count; i < count; ++i)
            {
                IFusionAnimatorGraphConverter converter = converters[i];
                if (converter != null && converter.CanConvert(source))
                {
                    return converter;
                }
            }

            return null;
        }

        private static bool TryNormalizeConvertSource(UnityEngine.Object source, out UnityEngine.Object normalizedSource)
        {
            normalizedSource = null;
            if (source == null)
            {
                return false;
            }

            if (source is AnimatorController animatorController)
            {
                normalizedSource = animatorController;
                return true;
            }

            if (source is Component componentSource)
            {
                source = componentSource.gameObject;
            }

            if (source is GameObject gameObjectSource)
            {
                if (IsCharacterAnimationControllerPrefab(gameObjectSource))
                {
                    normalizedSource = gameObjectSource;
                    return true;
                }
            }

            return false;
        }

        private static bool IsCharacterAnimationControllerPrefab(GameObject gameObject)
        {
            if (gameObject == null || EditorUtility.IsPersistent(gameObject) == false)
            {
                return false;
            }

            MonoBehaviour[] behaviours = gameObject.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; ++i)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                if (IsCharacterAnimationControllerType(behaviour.GetType()))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsCharacterAnimationControllerType(Type type)
        {
            while (type != null)
            {
                if (string.Equals(type.FullName, CharacterAnimationControllerTypeName, StringComparison.Ordinal) ||
                    string.Equals(type.Name, "CharacterAnimationController", StringComparison.Ordinal))
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        private static void NormalizeImportedBlendTrees(FusionAnimatorGraphAsset graph)
        {
            if (graph == null || graph.States == null)
            {
                return;
            }

            for (int i = 0; i < graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = graph.States[i];
                if (state == null || state.MotionType != FusionAnimatorMotionType.BlendTree || state.BlendTree == null)
                {
                    continue;
                }

                NormalizeBlendTreeChildren(state.BlendTree);
            }
        }

        private static void UpgradeLegacyLookPoseBlendTrees(FusionAnimatorGraphAsset graph)
        {
            if (graph == null || graph.States == null)
            {
                return;
            }

            for (int i = 0; i < graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = graph.States[i];
                if (state == null ||
                    state.MotionType != FusionAnimatorMotionType.BlendTree ||
                    state.BlendTree == null ||
                    state.Presentation == null ||
                    state.Presentation.Semantic != FusionAnimatorStateSemantic.LookPose)
                {
                    continue;
                }

                FusionAnimatorBlendTreeDefinition blendTree = state.BlendTree;
                blendTree.Type = FusionAnimatorBlendTreeType.DirectionalPoseTime2D;
                if (string.IsNullOrWhiteSpace(blendTree.ParameterXId) &&
                    string.IsNullOrWhiteSpace(blendTree.ParameterVector2Id))
                {
                    blendTree.ParameterXId = "param_look_pitch";
                }

                if (blendTree.InputPowerX <= 0.0001f)
                {
                    blendTree.InputPowerX = 1.0f;
                }

                if (blendTree.Children != null)
                {
                    for (int childIndex = 0; childIndex < blendTree.Children.Count; ++childIndex)
                    {
                        FusionAnimatorBlendTreeChild child = blendTree.Children[childIndex];
                        if (child == null)
                        {
                            continue;
                        }

                        Vector2 position = child.Position;
                        if (position.sqrMagnitude <= 0.000001f)
                        {
                            if (Mathf.Abs(child.Threshold) > 0.0001f)
                            {
                                position = child.Threshold >= 0.0f ? Vector2.right : Vector2.left;
                            }
                            else
                            {
                                position = blendTree.Children.Count > 1
                                    ? (childIndex % 2 == 0 ? Vector2.right : Vector2.left)
                                    : Vector2.right;
                            }
                        }

                        child.Position = position.normalized;
                    }
                }

                state.Presentation.Semantic = FusionAnimatorStateSemantic.None;
            }
        }

        private static void NormalizeBlendTreeChildren(FusionAnimatorBlendTreeDefinition blendTree)
        {
            if (blendTree == null)
            {
                return;
            }

            if (blendTree.Type == FusionAnimatorBlendTreeType.DirectionalPoseTime2D)
            {
                // Directional-pose trees intentionally preserve authored thresholds/ranges.
                return;
            }

            List<FusionAnimatorBlendTreeChild> children = blendTree.Children;
            if (children == null || children.Count == 0)
            {
                return;
            }

            var rawPositions = new Vector2[children.Count];
            bool hasAny = false;
            for (int i = 0; i < children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = children[i];
                if (child == null)
                {
                    continue;
                }

                Vector2 raw = child.Position;
                if (Mathf.Approximately(raw.x, 0.0f) && Mathf.Approximately(raw.y, 0.0f) && Mathf.Approximately(child.Threshold, 0.0f) == false)
                {
                    raw = new Vector2(child.Threshold, 0.0f);
                }

                rawPositions[i] = raw;
                hasAny |= Mathf.Abs(raw.x) > 0.0001f || Mathf.Abs(raw.y) > 0.0001f;
            }

            if (hasAny == false)
            {
                for (int i = 0; i < children.Count; ++i)
                {
                    FusionAnimatorBlendTreeChild child = children[i];
                    if (child == null)
                    {
                        continue;
                    }

                    child.Position = Vector2.zero;
                    child.Threshold = 0.0f;
                }

                return;
            }

            for (int i = 0; i < children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = children[i];
                if (child == null)
                {
                    continue;
                }

                Vector2 raw = rawPositions[i];
                float magnitude = raw.magnitude;
                if (magnitude <= 0.0001f)
                {
                    child.Position = Vector2.zero;
                    child.Threshold = 0.0f;
                    continue;
                }

                Vector2 direction = raw / magnitude;
                float gaitScale = ResolveNormalizeGaitScale(child.Name);
                Vector2 normalized = direction * gaitScale;
                child.Position = normalized;
                child.Threshold = Mathf.Max(Mathf.Abs(normalized.x), Mathf.Abs(normalized.y));
            }
        }

        private static float ResolveNormalizeGaitScale(string childName)
        {
            if (string.IsNullOrWhiteSpace(childName))
            {
                return 1.0f;
            }

            string lower = childName.ToLowerInvariant();
            if (lower.Contains("run") || lower.Contains("sprint"))
            {
                return 1.0f;
            }

            if (lower.Contains("walk"))
            {
                return 0.5f;
            }

            if (lower.Contains("strafe"))
            {
                return 0.5f;
            }

            return 1.0f;
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            if (separator <= 0)
            {
                return;
            }

            string parent = path.Substring(0, separator);
            string child = path.Substring(separator + 1);
            EnsureFolder(parent);
            if (AssetDatabase.IsValidFolder(path) == false)
            {
                AssetDatabase.CreateFolder(parent, child);
            }
        }

        private VisualElement CreateEmptyStateOverlay()
        {
            VisualElement overlay = new VisualElement();
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0.0f;
            overlay.style.top = 0.0f;
            overlay.style.right = 0.0f;
            overlay.style.bottom = 0.0f;
            overlay.style.alignItems = Align.Center;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.backgroundColor = new Color(0.12f, 0.12f, 0.12f, 0.9f);

            VisualElement card = new VisualElement();
            card.style.width = 520.0f;
            card.style.minHeight = 180.0f;
            card.style.paddingLeft = 18.0f;
            card.style.paddingRight = 18.0f;
            card.style.paddingTop = 18.0f;
            card.style.paddingBottom = 18.0f;
            card.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f, 0.98f);
            card.style.borderLeftWidth = 1.0f;
            card.style.borderRightWidth = 1.0f;
            card.style.borderTopWidth = 1.0f;
            card.style.borderBottomWidth = 1.0f;
            card.style.borderLeftColor = new Color(1.0f, 1.0f, 1.0f, 0.08f);
            card.style.borderRightColor = new Color(1.0f, 1.0f, 1.0f, 0.08f);
            card.style.borderTopColor = new Color(1.0f, 1.0f, 1.0f, 0.08f);
            card.style.borderBottomColor = new Color(1.0f, 1.0f, 1.0f, 0.08f);
            card.style.borderTopLeftRadius = 6.0f;
            card.style.borderTopRightRadius = 6.0f;
            card.style.borderBottomLeftRadius = 6.0f;
            card.style.borderBottomRightRadius = 6.0f;

            Label title = new Label("Fusion Animator");
            title.style.fontSize = 16.0f;
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 8.0f;
            card.Add(title);

            Label message = new Label("To begin authoring, create or assign a Fusion Animator Graph asset.");
            message.style.whiteSpace = WhiteSpace.Normal;
            message.style.unityTextAlign = TextAnchor.MiddleLeft;
            message.style.marginBottom = 14.0f;
            card.Add(message);

            VisualElement buttons = new VisualElement();
            buttons.style.flexDirection = FlexDirection.Row;

            Button create = new Button(CreateGraphAsset)
            {
                text = "Create Graph",
                tooltip = "Create a new FusionAnimator graph asset.",
            };
            create.style.marginRight = 8.0f;
            buttons.Add(create);

            Button useSelected = new Button(() =>
            {
                if (Selection.activeObject is FusionAnimatorGraphAsset graph)
                {
                    BindGraph(graph);
                }
            })
            {
                text = "Use Selected Graph",
                tooltip = "Assign currently selected FusionAnimatorGraph asset.",
            };
            buttons.Add(useSelected);

            card.Add(buttons);
            overlay.Add(card);
            return overlay;
        }
    }
}

