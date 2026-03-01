using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FusionAnimator.Editor
{
    public sealed partial class FusionAnimatorGraphCanvasWindow : EditorWindow
    {
        private string _parameterSearch = string.Empty;
        private string _bindingSearch = string.Empty;
        private string _layerSearch = string.Empty;
        private int _dragParameterIndex = -1;
        private int _dragParameterTargetIndex = -1;
        private int _dragBindingIndex = -1;
        private int _dragBindingTargetIndex = -1;
        private string _dragBindingTargetGroupId = null;
        private int _dragBindingGroupIndex = -1;
        private int _dragBindingGroupTargetIndex = -1;
        private int _dragLayerIndex = -1;
        private int _dragLayerTargetIndex = -1;
        private readonly Dictionary<string, bool> _parameterUsageFoldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _bindingUsageFoldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _bindingGroupFoldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _layerFoldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private string _selectedLayerScopePath = string.Empty;
        private bool _focusScopeRenameField;
        private readonly Dictionary<string, bool> _layerScopeFoldoutStates = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly HashSet<int> _selectedParameterIndices = new HashSet<int>();
        private readonly HashSet<int> _selectedBindingIndices = new HashSet<int>();
        private readonly HashSet<int> _selectedLayerIndices = new HashSet<int>();
        private readonly HashSet<string> _selectedStateIds = new HashSet<string>(StringComparer.Ordinal);
        private readonly HashSet<string> _selectedScopeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private string _selectedBindingGroupId = string.Empty;
        private int _parameterSelectionAnchorIndex = -1;
        private int _bindingSelectionAnchorIndex = -1;
        private int _layerSelectionAnchorIndex = -1;
        private static readonly FusionAnimatorConditionOperator[] BoolConditionOperators =
        {
            FusionAnimatorConditionOperator.IsTrue,
            FusionAnimatorConditionOperator.IsFalse,
            FusionAnimatorConditionOperator.Equal,
            FusionAnimatorConditionOperator.NotEqual,
        };

        private static readonly FusionAnimatorConditionOperator[] TriggerConditionOperators =
        {
            FusionAnimatorConditionOperator.IsTrue,
        };

        private static readonly FusionAnimatorConditionOperator[] NumericConditionOperators =
        {
            FusionAnimatorConditionOperator.Equal,
            FusionAnimatorConditionOperator.NotEqual,
            FusionAnimatorConditionOperator.Greater,
            FusionAnimatorConditionOperator.GreaterOrEqual,
            FusionAnimatorConditionOperator.Less,
            FusionAnimatorConditionOperator.LessOrEqual,
        };

        private sealed class ParameterUsageLocation
        {
            public string Label;
            public string LayerId;
            public string ScopePath;
            public string StateId;
            public string TransitionId;
            public string BindingId;
        }

        private sealed class BindingUsageLocation
        {
            public string Label;
            public string LayerId;
            public string ScopePath;
            public string StateId;
        }

        [Serializable]
        private sealed class ParameterClipboardPayload
        {
            public string Token;
            public FusionAnimatorParameterDefinition Parameter;
        }

        [Serializable]
        private sealed class BindingClipboardPayload
        {
            public string Token;
            public FusionAnimatorClipBindingDefinition Binding;
        }

        private const string ParameterClipboardPrefix = "FusionAnimator.ParameterClipboard:";
        private const string BindingClipboardPrefix = "FusionAnimator.BindingClipboard:";
        private static FusionAnimatorParameterDefinition _parameterClipboardCache;
        private static FusionAnimatorClipBindingDefinition _bindingClipboardCache;
        private static string _parameterClipboardToken = string.Empty;
        private static string _bindingClipboardToken = string.Empty;

        private void DrawLeftPanel()
        {
            if (_graph == null)
            {
                EditorGUILayout.HelpBox("Select a FusionAnimator graph asset.", MessageType.Info);
                return;
            }

            EnsureGraphCollections();
            GetHiddenGraphIssueCounts(
                out int orphanStateCount,
                out int blankTransitionConditionCount,
                out int invalidTransitionConditionReferenceCount);
            int hiddenIssueCount = orphanStateCount + blankTransitionConditionCount + invalidTransitionConditionReferenceCount;
            if (hiddenIssueCount > 0)
            {
                string warningMessage = string.Format(
                    "Hidden graph data issue(s): {0} orphan state layer ref(s), {1} blank transition condition(s), {2} invalid transition condition parameter ref(s).",
                    orphanStateCount,
                    blankTransitionConditionCount,
                    invalidTransitionConditionReferenceCount);
                EditorGUILayout.HelpBox(warningMessage, MessageType.Warning);
                using (new EditorGUILayout.HorizontalScope())
                {
                    GUILayout.FlexibleSpace();
                    if (GUILayout.Button(new GUIContent("Repair Hidden Data", "Auto-repair hidden orphan references and invalid condition rows."), GUILayout.Width(150.0f)))
                    {
                        RepairGraphData();
                    }
                }
                EditorGUILayout.Space(4.0f);
            }

            Event evt = Event.current;
            if ((_dragLayerIndex >= 0 || _dragParameterIndex >= 0) &&
                evt != null &&
                evt.type != EventType.MouseDrag &&
                evt.type != EventType.MouseUp &&
                evt.type != EventType.Repaint &&
                evt.type != EventType.Layout)
            {
                CancelReorderDrag();
            }

            int tabIndex = _leftLibraryTab == LeftLibraryTab.Layers ? 0 : _leftLibraryTab == LeftLibraryTab.Parameters ? 1 : 2;
            tabIndex = GUILayout.Toolbar(tabIndex, new[] { "Layers", "Parameters", "Bindings" });
            _leftLibraryTab = tabIndex == 0 ? LeftLibraryTab.Layers : tabIndex == 1 ? LeftLibraryTab.Parameters : LeftLibraryTab.Bindings;
            EditorGUILayout.Space(6.0f);
            HandleLibraryClipboardCommands(evt);
            HandleLibraryDeleteCommand(evt);

            if (_leftLibraryTab == LeftLibraryTab.Bindings)
            {
                DrawBindingsLibrary(evt);
                return;
            }

            if (_leftLibraryTab == LeftLibraryTab.Parameters)
            {
                EditorGUILayout.LabelField(new GUIContent("Parameters", "Animation parameters used by transition conditions and runtime control."), EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _parameterSearch = EditorGUILayout.TextField(new GUIContent("Search", "Filter parameters by name or id."), _parameterSearch ?? string.Empty);
                    if (GUILayout.Button(new GUIContent("+", "Create a new parameter."), GUILayout.Width(24.0f)))
                    {
                        CancelReorderDrag();
                        RecordUndo("Add FusionAnimator Parameter");
                        FusionAnimatorParameterDefinition parameter = new FusionAnimatorParameterDefinition
                        {
                            Id = FusionAnimatorGraphAsset.NewId("param"),
                            Name = "Parameter",
                        };
                        _graph.Parameters.Add(parameter);
                        _selectedParameterIndex = _graph.Parameters.Count - 1;
                        _selectedBindingIndex = -1;
                        _selectedBindingGroupId = string.Empty;
                        _selectedLayerIndex = -1;
                        _selectedState = null;
                        _selectedTransition = null;
                        _inspector?.MarkDirtyRepaint();
                        MarkGraphDirty();
                    }

                    bool canRemoveParameter = _selectedParameterIndex >= 0 && _selectedParameterIndex < _graph.Parameters.Count;
                    using (new EditorGUI.DisabledScope(canRemoveParameter == false))
                    {
                        if (GUILayout.Button(new GUIContent("-", "Remove selected parameter."), GUILayout.Width(24.0f)))
                        {
                            TryRemoveSelectedParameterFromLibrary();
                        }
                    }
                }
                EditorGUILayout.Space(4.0f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                {
                    _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));

                    GUIStyle parameterRowStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(10, 6, 0, 0),
                        fontSize = 12,
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                    };
                    GUIStyle parameterTypeStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(0.78f, 0.84f, 0.9f, 1.0f) },
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                    };
                    GUIStyle handleStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(0.75f, 0.75f, 0.75f, 0.95f) },
                    };
                    GUIStyle dragGhostStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip,
                        fontSize = 11,
                        normal = { textColor = new Color(0.92f, 0.96f, 1.0f, 0.95f) },
                        padding = new RectOffset(8, 6, 3, 3),
                    };
                    GUIStyle usageRowStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                        padding = new RectOffset(8, 6, 0, 0),
                        normal = { textColor = new Color(0.80f, 0.86f, 0.92f, 0.96f) },
                    };
                    GUIStyle usageCountStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(0.58f, 0.72f, 0.86f, 0.95f) },
                    };

                    List<int> visibleParameterIndices = new List<int>();
                    for (int i = 0; i < _graph.Parameters.Count; ++i)
                    {
                        FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                        if (parameter == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(_parameterSearch) == false)
                        {
                            string filter = _parameterSearch.Trim();
                            string combined = string.Format("{0} {1}", parameter.Name, parameter.Id);
                            if (combined.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                        }

                        visibleParameterIndices.Add(i);
                    }

                    for (int visibleIndex = 0; visibleIndex < visibleParameterIndices.Count; ++visibleIndex)
                    {
                        int i = visibleParameterIndices[visibleIndex];
                        FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                        string parameterId = parameter != null ? parameter.Id : string.Empty;
                        List<ParameterUsageLocation> parameterUsages = BuildParameterUsageLocations(parameterId);
                        bool hasUsages = parameterUsages.Count > 0;

                        Rect rowRect = EditorGUILayout.GetControlRect(false, 24.0f);
                        rowRect.xMax -= 14.0f;
                        bool selected = IsParameterLibrarySelected(i);
                        Color rowColor = selected
                            ? new Color(0.18f, 0.36f, 0.60f, 0.78f)
                            : new Color(0.19f, 0.19f, 0.19f, 0.96f);
                        EditorGUI.DrawRect(rowRect, rowColor);

                        Rect handleRect = rowRect;
                        handleRect.xMin += 2.0f;
                        handleRect.xMax = handleRect.xMin + 12.0f;
                        GUI.Label(handleRect, "|||", handleStyle);

                        Rect usageFoldoutRect = rowRect;
                        usageFoldoutRect.xMin = handleRect.xMax + 2.0f;
                        usageFoldoutRect.xMax = usageFoldoutRect.xMin + 14.0f;
                        bool usageExpanded = false;
                        if (hasUsages)
                        {
                            usageExpanded = _parameterUsageFoldoutStates.TryGetValue(parameterId ?? string.Empty, out bool expanded) && expanded;
                            bool nextUsageExpanded = EditorGUI.Foldout(usageFoldoutRect, usageExpanded, GUIContent.none, false);
                            if (nextUsageExpanded != usageExpanded)
                            {
                                _parameterUsageFoldoutStates[parameterId ?? string.Empty] = nextUsageExpanded;
                                usageExpanded = nextUsageExpanded;
                            }
                        }

                        Rect nameRect = rowRect;
                        // Always reserve the foldout column so parameter names align regardless of usage count.
                        nameRect.xMin = usageFoldoutRect.xMax + 2.0f;
                        nameRect.xMax -= 70.0f;
                        string parameterName = string.IsNullOrWhiteSpace(parameter.Name) ? "Parameter" : parameter.Name;
                        GUI.Label(nameRect, new GUIContent(parameterName, "Select this parameter for editing and highlight transitions using it."), parameterRowStyle);
                        if (hasUsages)
                        {
                            Rect usageCountRect = rowRect;
                            usageCountRect.xMin = nameRect.xMax + 2.0f;
                            usageCountRect.xMax -= 68.0f;
                            GUI.Label(usageCountRect, string.Format("{0}", parameterUsages.Count), usageCountStyle);
                        }

                        bool clickOnUsageFoldout = evt.type == EventType.MouseDown &&
                                                   evt.button == 0 &&
                                                   hasUsages &&
                                                   usageFoldoutRect.Contains(evt.mousePosition);
                        if (evt.type == EventType.MouseDown && evt.button == 1 && rowRect.Contains(evt.mousePosition))
                        {
                            CancelReorderDrag();
                            ClearGraphSelectionForLibraryInteraction();
                            _selectedParameterIndices.Clear();
                            _selectedParameterIndices.Add(i);
                            _parameterSelectionAnchorIndex = i;
                            _selectedBindingIndices.Clear();
                            _selectedBindingGroupId = string.Empty;
                            _selectedLayerIndices.Clear();
                            _selectedParameterIndex = i;
                            _selectedBindingIndex = -1;
                            _selectedLayerIndex = -1;
                            _selectedState = null;
                            _selectedTransition = null;
                            _selectedLayerScopePath = string.Empty;
                            _selectedEntryLinkTargetStateId = string.Empty;
                            _graphView?.SetHoveredLayer(null);
                            _graphView?.SetHoveredParameter(parameter.Id);
                            _inspector?.MarkDirtyRepaint();
                            ShowParameterContextMenu(i);
                            evt.Use();
                        }

                        if (evt.type == EventType.MouseDown && evt.button == 0 && rowRect.Contains(evt.mousePosition) && clickOnUsageFoldout == false)
                        {
                            if (handleRect.Contains(evt.mousePosition))
                            {
                                if (_selectedParameterIndices.Count <= 1 || _selectedParameterIndices.Contains(i) == false)
                                {
                                    _selectedParameterIndices.Clear();
                                    _selectedParameterIndices.Add(i);
                                    _parameterSelectionAnchorIndex = i;
                                    _selectedBindingIndices.Clear();
                                    _selectedBindingGroupId = string.Empty;
                                    _selectedLayerIndices.Clear();
                                    _selectedParameterIndex = i;
                                }

                                _dragParameterIndex = i;
                                _dragParameterTargetIndex = i;
                            }
                            else
                            {
                                CancelReorderDrag();
                                ClearGraphSelectionForLibraryInteraction();
                                SelectLibraryIndex(_selectedParameterIndices, ref _selectedParameterIndex, ref _parameterSelectionAnchorIndex, i, _graph.Parameters.Count);
                                _selectedBindingIndices.Clear();
                                _selectedBindingGroupId = string.Empty;
                                _selectedLayerIndices.Clear();
                                _selectedBindingIndex = -1;
                                _selectedLayerIndex = -1;
                                _selectedState = null;
                                _selectedTransition = null;
                                _selectedLayerScopePath = string.Empty;
                                _selectedEntryLinkTargetStateId = string.Empty;
                                _graphView?.SetHoveredLayer(null);
                                _graphView?.SetHoveredParameter(parameter.Id);
                                _inspector?.MarkDirtyRepaint();
                            }

                            evt.Use();
                        }

                        if (_dragParameterIndex >= 0 && evt.type == EventType.MouseDrag && rowRect.Contains(evt.mousePosition))
                        {
                            _dragParameterTargetIndex = evt.mousePosition.y > rowRect.center.y ? i + 1 : i;
                            evt.Use();
                        }

                        if (_dragParameterIndex >= 0 && (i == _dragParameterTargetIndex || i + 1 == _dragParameterTargetIndex))
                        {
                            Color indicator = new Color(0.42f, 0.68f, 1.0f, 0.95f);
                            bool insertAfter = _dragParameterTargetIndex == i + 1;
                            float y = insertAfter ? rowRect.yMax : rowRect.yMin;
                            EditorGUI.DrawRect(new Rect(rowRect.xMin + 1.0f, y - 1.0f, Mathf.Max(0.0f, rowRect.width - 2.0f), 2.0f), indicator);
                        }

                        Rect typeRect = rowRect;
                        typeRect.xMin = nameRect.xMax + 2.0f;
                        typeRect.xMax -= 6.0f;
                        GUI.Label(typeRect, parameter.Type.ToString(), parameterTypeStyle);

                        if (hasUsages && usageExpanded)
                        {
                            for (int usageIndex = 0; usageIndex < parameterUsages.Count; ++usageIndex)
                            {
                                ParameterUsageLocation usage = parameterUsages[usageIndex];
                                if (usage == null)
                                {
                                    continue;
                                }

                                Rect usageRowRect = EditorGUILayout.GetControlRect(false, 18.0f);
                                usageRowRect.xMin += 30.0f;
                                usageRowRect.xMax -= 14.0f;
                                bool usageHovered = usageRowRect.Contains(evt.mousePosition);
                                Color usageRowColor = usageHovered
                                    ? new Color(0.18f, 0.26f, 0.36f, 0.65f)
                                    : new Color(0.13f, 0.13f, 0.13f, 0.82f);
                                EditorGUI.DrawRect(usageRowRect, usageRowColor);

                                Rect usageNameRect = usageRowRect;
                                usageNameRect.xMin += 8.0f;
                                GUI.Label(usageNameRect, usage.Label, usageRowStyle);

                                if (evt.type == EventType.MouseDown && evt.button == 0 && usageRowRect.Contains(evt.mousePosition))
                                {
                                    CancelReorderDrag();
                                    JumpToParameterUsage(usage);
                                    evt.Use();
                                }
                            }
                        }
                    }

                    if (_dragParameterIndex >= 0 && _dragParameterIndex < _graph.Parameters.Count && evt.type == EventType.Repaint)
                    {
                        FusionAnimatorParameterDefinition dragged = _graph.Parameters[_dragParameterIndex];
                        List<int> draggedSelection = ResolveDraggedSelectionIndices(_selectedParameterIndices, _dragParameterIndex, _graph.Parameters.Count);
                        string dragLabel;
                        if (draggedSelection.Count > 1)
                        {
                            string leadName = dragged != null && string.IsNullOrWhiteSpace(dragged.Name) == false ? dragged.Name : "Parameter";
                            dragLabel = string.Format("{0} items ({1}...)", draggedSelection.Count, leadName);
                        }
                        else
                        {
                            dragLabel = dragged != null && string.IsNullOrWhiteSpace(dragged.Name) == false ? dragged.Name : "Parameter";
                        }
                        Rect dragRect = new Rect(
                            evt.mousePosition.x + 16.0f,
                            evt.mousePosition.y - 12.0f,
                            Mathf.Min(200.0f, position.width * 0.52f),
                            22.0f);
                        EditorGUI.DrawRect(dragRect, new Color(0.16f, 0.34f, 0.58f, 0.88f));
                        GUI.Label(dragRect, dragLabel, dragGhostStyle);
                    }

                    if (_dragParameterIndex >= 0 && evt.type == EventType.MouseUp)
                    {
                        int draggedIndex = _dragParameterIndex;
                        int insertionIndex = _dragParameterTargetIndex;
                        _dragParameterIndex = -1;
                        _dragParameterTargetIndex = -1;
                        List<int> movingIndices = ResolveDraggedSelectionIndices(_selectedParameterIndices, draggedIndex, _graph.Parameters.Count);
                        if (movingIndices.Count > 0)
                        {
                            int draggedOrder = movingIndices.IndexOf(draggedIndex);
                            if (draggedOrder < 0)
                            {
                                draggedOrder = 0;
                            }

                            int insertionAfterRemoval = ComputeInsertionAfterRemoval(movingIndices, insertionIndex, _graph.Parameters.Count);
                            bool orderChanged = WouldMoveSelectionChangeOrder(movingIndices, insertionAfterRemoval);
                            if (orderChanged)
                            {
                                RecordUndo("Reorder FusionAnimator Parameters");
                                MoveSelectedListItems(_graph.Parameters, movingIndices, insertionIndex, out List<int> newIndices, out _);
                                _selectedParameterIndex = -1;
                                _selectedBindingIndex = -1;
                                _selectedParameterIndices.Clear();
                                for (int index = 0; index < newIndices.Count; ++index)
                                {
                                    _selectedParameterIndices.Add(newIndices[index]);
                                }

                                int selectedOrder = Mathf.Clamp(draggedOrder, 0, newIndices.Count - 1);
                                _selectedParameterIndex = newIndices.Count > 0 ? newIndices[selectedOrder] : -1;
                                _parameterSelectionAnchorIndex = _selectedParameterIndex;
                                _inspector?.MarkDirtyRepaint();
                                MarkGraphDirty();
                            }
                        }

                        evt.Use();
                    }

                    EditorGUILayout.EndScrollView();
                }
            }
            else
            {
                EditorGUILayout.LabelField(new GUIContent("Layers", "Animation layers used to group states and blending behavior."), EditorStyles.boldLabel);
                using (new EditorGUILayout.HorizontalScope())
                {
                    _layerSearch = EditorGUILayout.TextField(new GUIContent("Search", "Filter layers by name or id."), _layerSearch ?? string.Empty);
                    if (GUILayout.Button(new GUIContent("+", "Create a new layer."), GUILayout.Width(24.0f)))
                    {
                        CancelReorderDrag();
                        RecordUndo("Add FusionAnimator Layer");
                        FusionAnimatorLayerDefinition layer = new FusionAnimatorLayerDefinition
                        {
                            Id = FusionAnimatorGraphAsset.NewId("layer"),
                            Name = "Layer",
                            DefaultWeight = 1.0f,
                        };
                        _graph.Layers.Add(layer);
                        _selectedLayerIndex = _graph.Layers.Count - 1;
                        _selectedParameterIndex = -1;
                        _selectedBindingIndex = -1;
                        _selectedBindingGroupId = string.Empty;
                        _selectedState = null;
                        _selectedTransition = null;
                        _inspector?.MarkDirtyRepaint();
                        _graphView?.RebuildFromGraphData();
                        MarkGraphDirty();
                    }

                    bool canRemoveLayer = _selectedLayerIndex >= 0 && _selectedLayerIndex < _graph.Layers.Count;
                    using (new EditorGUI.DisabledScope(canRemoveLayer == false))
                    {
                        if (GUILayout.Button(new GUIContent("-", "Remove selected layer and its states/transitions."), GUILayout.Width(24.0f)))
                        {
                            TryRemoveSelectedLayerFromLibrary();
                        }
                    }
                }
                EditorGUILayout.Space(4.0f);

                using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
                {
                    _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));

                    GUIStyle layerRowStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(10, 6, 0, 0),
                        fontSize = 12,
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                    };
                    GUIStyle layerFlagsStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleRight,
                        fontStyle = FontStyle.Bold,
                        normal = { textColor = new Color(0.78f, 0.84f, 0.9f, 1.0f) },
                        clipping = TextClipping.Clip,
                        wordWrap = false,
                    };
                    GUIStyle handleStyle = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = new Color(0.75f, 0.75f, 0.75f, 0.95f) },
                    };
                    GUIStyle dragGhostStyle = new GUIStyle(EditorStyles.label)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        clipping = TextClipping.Clip,
                        fontSize = 11,
                        normal = { textColor = new Color(0.92f, 0.96f, 1.0f, 0.95f) },
                        padding = new RectOffset(8, 6, 3, 3),
                    };

                    Rect overviewRowRect = EditorGUILayout.GetControlRect(false, 24.0f);
                    overviewRowRect.xMax -= 14.0f;
                    bool overviewSelected = string.IsNullOrWhiteSpace(_activeLayerId);
                    Color overviewColor = overviewSelected
                        ? new Color(0.18f, 0.36f, 0.60f, 0.78f)
                        : new Color(0.19f, 0.19f, 0.19f, 0.96f);
                    EditorGUI.DrawRect(overviewRowRect, overviewColor);
                    Rect overviewNameRect = overviewRowRect;
                    overviewNameRect.xMin += 6.0f;
                    overviewNameRect.xMax -= 6.0f;
                    if (GUI.Button(overviewNameRect, new GUIContent("Overview", "Select graph overview and show graph-level inspector."), layerRowStyle))
                    {
                        ClearLibraryMultiSelectionState();
                        _selectedLayerIndex = -1;
                        _selectedParameterIndex = -1;
                        _selectedBindingIndex = -1;
                        _selectedBindingGroupId = string.Empty;
                        _selectedState = null;
                        _selectedTransition = null;
                        _selectedLayerScopePath = string.Empty;
                        _selectedEntryLinkTargetStateId = string.Empty;
                        SetActiveLayer(string.Empty);
                        _inspector?.MarkDirtyRepaint();
                    }

                    List<int> visibleLayerIndices = new List<int>();
                    for (int i = 0; i < _graph.Layers.Count; ++i)
                    {
                        FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                        if (layer == null)
                        {
                            continue;
                        }

                        if (string.IsNullOrWhiteSpace(_layerSearch) == false)
                        {
                            string filter = _layerSearch.Trim();
                            string combined = string.Format("{0} {1}", layer.Name, layer.Id);
                            if (combined.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                            {
                                continue;
                            }
                        }

                        visibleLayerIndices.Add(i);
                    }

                    for (int visibleIndex = 0; visibleIndex < visibleLayerIndices.Count; ++visibleIndex)
                    {
                        int i = visibleLayerIndices[visibleIndex];
                        FusionAnimatorLayerDefinition layer = _graph.Layers[i];

                        Rect rowRect = EditorGUILayout.GetControlRect(false, 24.0f);
                        rowRect.xMax -= 14.0f;
                        bool selected = IsLayerLibrarySelected(i);
                        if (_selectedLayerIndices.Count == 0 &&
                            (_selectedState != null || _selectedStateIds.Count > 0 || _selectedScopeKeys.Count > 0 || string.IsNullOrWhiteSpace(_selectedLayerScopePath) == false))
                        {
                            selected = false;
                        }
                        Color rowColor = selected
                            ? new Color(0.18f, 0.36f, 0.60f, 0.78f)
                            : new Color(0.19f, 0.19f, 0.19f, 0.96f);
                        EditorGUI.DrawRect(rowRect, rowColor);

                        Rect handleRect = rowRect;
                        handleRect.xMin += 2.0f;
                        handleRect.xMax = handleRect.xMin + 12.0f;
                        GUI.Label(handleRect, "|||", handleStyle);

                        Rect foldoutRect = rowRect;
                        foldoutRect.xMin = handleRect.xMax + 2.0f;
                        foldoutRect.xMax = foldoutRect.xMin + 14.0f;
                        bool isExpanded = false;
                        if (string.IsNullOrWhiteSpace(layer.Id) == false)
                        {
                            _layerFoldoutStates.TryGetValue(layer.Id, out isExpanded);
                        }

                        bool nextExpanded = EditorGUI.Foldout(foldoutRect, isExpanded, GUIContent.none, false);
                        if (nextExpanded != isExpanded && string.IsNullOrWhiteSpace(layer.Id) == false)
                        {
                            _layerFoldoutStates[layer.Id] = nextExpanded;
                        }

                        Rect nameRect = rowRect;
                        nameRect.xMin = foldoutRect.xMax + 2.0f;
                        nameRect.xMax -= 90.0f;
                        string displayName = string.IsNullOrWhiteSpace(layer.Name) ? "Layer" : layer.Name;
                        if (i == 0)
                        {
                            displayName += " (Default)";
                        }

                        GUI.Label(nameRect, new GUIContent(displayName, "Single-click selects this layer. Double-click scopes this layer in the graph view."), layerRowStyle);

                        bool clickOnFoldout = evt.type == EventType.MouseDown && evt.button == 0 && foldoutRect.Contains(evt.mousePosition);
                        if (evt.type == EventType.MouseDown && evt.button == 0 && rowRect.Contains(evt.mousePosition) && clickOnFoldout == false)
                        {
                            bool additiveLayer = evt.control || evt.command || evt.shift;
                            if (handleRect.Contains(evt.mousePosition))
                            {
                                if (_selectedLayerIndices.Count <= 1 || _selectedLayerIndices.Contains(i) == false)
                                {
                                    _selectedLayerIndices.Clear();
                                    _selectedLayerIndices.Add(i);
                                    _layerSelectionAnchorIndex = i;
                                    _selectedParameterIndices.Clear();
                                    _selectedBindingIndices.Clear();
                                    _selectedBindingGroupId = string.Empty;
                                    if (additiveLayer == false)
                                    {
                                        _selectedStateIds.Clear();
                                        _selectedScopeKeys.Clear();
                                    }
                                    _selectedLayerIndex = i;
                                }

                                _dragLayerIndex = i;
                                _dragLayerTargetIndex = i;
                            }
                            else
                            {
                                CancelReorderDrag();
                                ClearGraphSelectionForLibraryInteraction();
                                SelectLibraryIndex(_selectedLayerIndices, ref _selectedLayerIndex, ref _layerSelectionAnchorIndex, i, _graph.Layers.Count);
                                _selectedParameterIndices.Clear();
                                _selectedBindingIndices.Clear();
                                _selectedBindingGroupId = string.Empty;
                                _selectedParameterIndex = -1;
                                _selectedBindingIndex = -1;
                                if (additiveLayer == false)
                                {
                                    _selectedState = null;
                                    _selectedTransition = null;
                                    _selectedStateIds.Clear();
                                    _selectedScopeKeys.Clear();
                                    _selectedLayerScopePath = string.Empty;
                                    _selectedEntryLinkTargetStateId = string.Empty;
                                }

                                if (additiveLayer == false && _selectedLayerIndices.Count <= 1 && evt.clickCount >= 2)
                                {
                                    SetActiveLayer(layer.Id);
                                }
                                else if (additiveLayer == false && _selectedLayerIndices.Count <= 1)
                                {
                                    _graphView?.SelectLayerNodeByLayerId(layer.Id, false);
                                }

                                _inspector?.MarkDirtyRepaint();
                            }

                            evt.Use();
                        }

                        if (_dragLayerIndex >= 0 && evt.type == EventType.MouseDrag && rowRect.Contains(evt.mousePosition))
                        {
                            _dragLayerTargetIndex = evt.mousePosition.y > rowRect.center.y ? i + 1 : i;
                            evt.Use();
                        }

                        if (_dragLayerIndex >= 0 && (i == _dragLayerTargetIndex || i + 1 == _dragLayerTargetIndex))
                        {
                            Color indicator = new Color(0.42f, 0.68f, 1.0f, 0.95f);
                            bool insertAfter = _dragLayerTargetIndex == i + 1;
                            float y = insertAfter ? rowRect.yMax : rowRect.yMin;
                            EditorGUI.DrawRect(new Rect(rowRect.xMin + 1.0f, y - 1.0f, Mathf.Max(0.0f, rowRect.width - 2.0f), 2.0f), indicator);
                        }

                        Rect flagsRect = rowRect;
                        flagsRect.xMin = nameRect.xMax + 2.0f;
                        flagsRect.xMax -= 6.0f;
                        string flags = string.Empty;
                        if (layer.AvatarMask != null)
                        {
                            flags += "M ";
                        }

                        if (layer.BlendMode == FusionAnimatorLayerBlendMode.Additive)
                        {
                            flags += "A ";
                        }

                        if (layer.SyncedLayerIndex >= 0)
                        {
                            flags += "S ";
                        }

                        if (layer.IKPass)
                        {
                            flags += "IK";
                        }

                        bool showDefaultLayerWarning = i == 0 && layer.AvatarMask != null;
                        string trimmedFlags = flags.Trim();
                        if (showDefaultLayerWarning)
                        {
                            GUIStyle warningFlagsStyle = new GUIStyle(layerFlagsStyle);
                            warningFlagsStyle.normal.textColor = new Color(0.98f, 0.80f, 0.16f, 1.0f);
                            GUIContent warningContent = new GUIContent("!!", "Character orientation may be erroneous for humanoid motion if there is an AvatarMask on the default layer. Consider not using a mask for the default layer.");
                            float warningWidth = Mathf.Max(18.0f, warningFlagsStyle.CalcSize(warningContent).x + 2.0f);
                            Rect warningRect = flagsRect;
                            warningRect.xMin = warningRect.xMax - warningWidth;
                            GUI.Label(
                                warningRect,
                                warningContent,
                                warningFlagsStyle);

                            if (string.IsNullOrWhiteSpace(trimmedFlags) == false)
                            {
                                Rect tagsRect = flagsRect;
                                tagsRect.xMax = warningRect.xMin - 4.0f;
                                GUI.Label(tagsRect, trimmedFlags, layerFlagsStyle);
                            }
                        }
                        else
                        {
                            GUI.Label(flagsRect, trimmedFlags, layerFlagsStyle);
                        }

                        if (nextExpanded)
                        {
                            List<FusionAnimatorStateDefinition> layerStates = _graph.States
                                .Where(state =>
                                    state != null &&
                                    IsScopeSentinelStateName(state.Name) == false &&
                                    string.Equals(state.LayerId, layer.Id, StringComparison.Ordinal))
                                .OrderBy(state => state.Name, StringComparer.OrdinalIgnoreCase)
                                .ToList();
                            List<string> layoutScopes = new List<string>();
                            if (_graph.ScopeUtilityNodeLayouts != null)
                            {
                                HashSet<string> layoutScopeSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                for (int layoutIndex = 0; layoutIndex < _graph.ScopeUtilityNodeLayouts.Count; ++layoutIndex)
                                {
                                    FusionAnimatorScopeUtilityNodeLayout layout = _graph.ScopeUtilityNodeLayouts[layoutIndex];
                                    if (layout == null || string.Equals(layout.LayerId, layer.Id, StringComparison.Ordinal) == false)
                                    {
                                        continue;
                                    }

                                    string normalizedLayoutScope = NormalizeScopePath(layout.ScopePath);
                                    if (string.IsNullOrWhiteSpace(normalizedLayoutScope))
                                    {
                                        continue;
                                    }

                                    layoutScopeSet.Add(normalizedLayoutScope);
                                }

                                if (layoutScopeSet.Count > 0)
                                {
                                    layoutScopes = layoutScopeSet
                                        .OrderBy(value => value, StringComparer.OrdinalIgnoreCase)
                                        .ToList();
                                }
                            }

                            string LayerScopeKey(string scopePath)
                            {
                                return string.Format("{0}|{1}", layer.Id, scopePath ?? string.Empty);
                            }

                            string GetScopeLeaf(string scopePath)
                            {
                                if (string.IsNullOrWhiteSpace(scopePath))
                                {
                                    return "Sub-State Machine";
                                }

                                int separator = scopePath.LastIndexOf('/');
                                return separator >= 0 ? scopePath.Substring(separator + 1) : scopePath;
                            }

                            List<string> GetChildScopes(string parentScope)
                            {
                                HashSet<string> children = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                                string normalizedParent = string.IsNullOrWhiteSpace(parentScope) ? string.Empty : parentScope.Trim();
                                void AddScopeIfDirectChild(string candidateScope)
                                {
                                    if (string.IsNullOrWhiteSpace(candidateScope))
                                    {
                                        return;
                                    }

                                    if (string.IsNullOrWhiteSpace(normalizedParent))
                                    {
                                        int slash = candidateScope.IndexOf('/');
                                        string child = slash >= 0 ? candidateScope.Substring(0, slash) : candidateScope;
                                        if (string.IsNullOrWhiteSpace(child) == false)
                                        {
                                            children.Add(child);
                                        }
                                    }
                                    else if (candidateScope.StartsWith(normalizedParent + "/", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string remainder = candidateScope.Substring(normalizedParent.Length + 1);
                                        int slash = remainder.IndexOf('/');
                                        string childLeaf = slash >= 0 ? remainder.Substring(0, slash) : remainder;
                                        if (string.IsNullOrWhiteSpace(childLeaf) == false)
                                        {
                                            children.Add(normalizedParent + "/" + childLeaf);
                                        }
                                    }
                                }

                                for (int lsIndex = 0; lsIndex < layerStates.Count; ++lsIndex)
                                {
                                    FusionAnimatorStateDefinition candidate = layerStates[lsIndex];
                                    string candidateScope = GetStateScopePathFromName(candidate.Name);
                                    if (string.Equals(candidateScope, normalizedParent, StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    AddScopeIfDirectChild(candidateScope);
                                }

                                for (int layoutScopeIndex = 0; layoutScopeIndex < layoutScopes.Count; ++layoutScopeIndex)
                                {
                                    string candidateScope = layoutScopes[layoutScopeIndex];
                                    if (string.Equals(candidateScope, normalizedParent, StringComparison.OrdinalIgnoreCase))
                                    {
                                        continue;
                                    }

                                    AddScopeIfDirectChild(candidateScope);
                                }

                                return children.OrderBy(value => value, StringComparer.OrdinalIgnoreCase).ToList();
                            }

                            List<FusionAnimatorStateDefinition> GetDirectStates(string parentScope)
                            {
                                string normalizedParent = string.IsNullOrWhiteSpace(parentScope) ? string.Empty : parentScope.Trim();
                                List<FusionAnimatorStateDefinition> direct = new List<FusionAnimatorStateDefinition>();
                                for (int lsIndex = 0; lsIndex < layerStates.Count; ++lsIndex)
                                {
                                    FusionAnimatorStateDefinition candidate = layerStates[lsIndex];
                                    string candidateScope = GetStateScopePathFromName(candidate.Name);
                                    if (string.Equals(candidateScope, normalizedParent, StringComparison.OrdinalIgnoreCase))
                                    {
                                        direct.Add(candidate);
                                    }
                                }

                                return direct.OrderBy(state => state.Name, StringComparer.OrdinalIgnoreCase).ToList();
                            }

                            void DrawScopeRow(string scopePath, int depth)
                            {
                                Rect scopeRowRect = EditorGUILayout.GetControlRect(false, 20.0f);
                                scopeRowRect.xMin += 24.0f + depth * 14.0f;
                                scopeRowRect.xMax -= 14.0f;

                                string scopeSelectionKey = BuildScopeSelectionKey(layer.Id, scopePath);
                                bool scopeSelected = _selectedScopeKeys.Count > 0
                                    ? _selectedScopeKeys.Contains(scopeSelectionKey)
                                    : (_selectedLayerIndex == i &&
                                       _selectedState == null &&
                                       string.Equals(_selectedLayerScopePath, scopePath, StringComparison.OrdinalIgnoreCase));
                                Color scopeColor = scopeSelected
                                    ? new Color(0.22f, 0.42f, 0.66f, 0.74f)
                                    : new Color(0.15f, 0.15f, 0.15f, 0.9f);
                                EditorGUI.DrawRect(scopeRowRect, scopeColor);

                                Rect scopeFoldoutRect = scopeRowRect;
                                scopeFoldoutRect.xMin += 2.0f;
                                scopeFoldoutRect.xMax = scopeFoldoutRect.xMin + 14.0f;
                                string scopeKey = LayerScopeKey(scopePath);
                                bool scopeExpanded = _layerScopeFoldoutStates.TryGetValue(scopeKey, out bool expandedValue) ? expandedValue : true;
                                bool nextScopeExpanded = EditorGUI.Foldout(scopeFoldoutRect, scopeExpanded, GUIContent.none, false);
                                if (nextScopeExpanded != scopeExpanded)
                                {
                                    _layerScopeFoldoutStates[scopeKey] = nextScopeExpanded;
                                }

                                Rect scopeNameRect = scopeRowRect;
                                scopeNameRect.xMin = scopeFoldoutRect.xMax + 2.0f;
                                GUI.Label(scopeNameRect, new GUIContent(GetScopeLeaf(scopePath), scopePath), layerRowStyle);

                                if (evt.type == EventType.MouseDown && evt.button == 0 && scopeRowRect.Contains(evt.mousePosition) && scopeFoldoutRect.Contains(evt.mousePosition) == false)
                                {
                                    CancelReorderDrag();
                                    bool additiveScope = evt.control || evt.command || evt.shift;
                                    string scopeKeyId = scopeSelectionKey;
                                    _selectedParameterIndex = -1;
                                    _selectedBindingIndex = -1;
                                    _selectedBindingGroupId = string.Empty;
                                    _selectedParameterIndices.Clear();
                                    _selectedBindingIndices.Clear();
                                    _selectedEntryLinkTargetStateId = string.Empty;
                                    if (additiveScope)
                                    {
                                        _selectedState = null;
                                        _selectedTransition = null;
                                        if (_selectedScopeKeys.Contains(scopeKeyId))
                                        {
                                            _selectedScopeKeys.Remove(scopeKeyId);
                                        }
                                        else
                                        {
                                            _selectedScopeKeys.Add(scopeKeyId);
                                        }

                                        if (_selectedScopeKeys.Count == 1)
                                        {
                                            _selectedLayerIndex = i;
                                            _selectedLayerScopePath = scopePath;
                                        }
                                        else if (_selectedScopeKeys.Count == 0)
                                        {
                                            _selectedLayerIndex = ResolveFirstSelectedIndex(_selectedLayerIndices);
                                            _selectedLayerScopePath = string.Empty;
                                        }
                                        else
                                        {
                                            _selectedLayerIndex = -1;
                                            _selectedLayerScopePath = string.Empty;
                                        }
                                    }
                                    else
                                    {
                                        _selectedState = null;
                                        _selectedTransition = null;
                                        _selectedLayerIndices.Clear();
                                        _selectedStateIds.Clear();
                                        _selectedScopeKeys.Clear();
                                        _selectedScopeKeys.Add(scopeKeyId);
                                        _selectedLayerIndex = i;
                                        _selectedLayerScopePath = scopePath;
                                        SetActiveLayer(layer.Id);
                                        string parentScope = GetStateScopePathFromName(scopePath);
                                        SetScopePath(parentScope);
                                        _graphView?.SelectScopeNodeByPath(scopePath, false);
                                    }

                                    _inspector?.MarkDirtyRepaint();
                                    evt.Use();
                                }

                                if (nextScopeExpanded == false)
                                {
                                    return;
                                }

                                List<string> childScopes = GetChildScopes(scopePath);
                                for (int childIndex = 0; childIndex < childScopes.Count; ++childIndex)
                                {
                                    DrawScopeRow(childScopes[childIndex], depth + 1);
                                }

                                List<FusionAnimatorStateDefinition> directStates = GetDirectStates(scopePath);
                                for (int directIndex = 0; directIndex < directStates.Count; ++directIndex)
                                {
                                    DrawStateRow(directStates[directIndex], depth + 1);
                                }
                            }

                            void DrawStateRow(FusionAnimatorStateDefinition state, int depth)
                            {
                                if (state == null)
                                {
                                    return;
                                }

                                string stateName = string.IsNullOrWhiteSpace(state.Name) ? "State" : state.Name;
                                int separator = stateName.LastIndexOf('/');
                                string leafName = separator >= 0 ? stateName.Substring(separator + 1) : stateName;
                                if (string.IsNullOrWhiteSpace(leafName))
                                {
                                    leafName = "State";
                                }

                                Rect stateRowRect = EditorGUILayout.GetControlRect(false, 20.0f);
                                stateRowRect.xMin += 24.0f + depth * 14.0f;
                                stateRowRect.xMax -= 14.0f;

                                bool stateSelected = _selectedState != null && string.Equals(_selectedState.Id, state.Id, StringComparison.Ordinal);
                                if (_selectedStateIds.Count > 0)
                                {
                                    stateSelected = _selectedStateIds.Contains(state.Id);
                                }
                                Color stateColor = stateSelected
                                    ? new Color(0.22f, 0.42f, 0.66f, 0.74f)
                                    : new Color(0.16f, 0.16f, 0.16f, 0.92f);
                                EditorGUI.DrawRect(stateRowRect, stateColor);

                                Rect stateNameRect = stateRowRect;
                                stateNameRect.xMin += 8.0f;
                                GUI.Label(stateNameRect, new GUIContent(leafName, stateName), layerRowStyle);

                                if (evt.type == EventType.MouseDown && evt.button == 0 && stateRowRect.Contains(evt.mousePosition))
                                {
                                    CancelReorderDrag();
                                    bool additiveState = evt.control || evt.command || evt.shift;
                                    _selectedParameterIndex = -1;
                                    _selectedBindingIndex = -1;
                                    _selectedBindingGroupId = string.Empty;
                                    _selectedParameterIndices.Clear();
                                    _selectedBindingIndices.Clear();
                                    _selectedEntryLinkTargetStateId = string.Empty;
                                    if (additiveState)
                                    {
                                        _selectedLayerScopePath = string.Empty;
                                        _selectedTransition = null;
                                        if (_selectedStateIds.Contains(state.Id))
                                        {
                                            _selectedStateIds.Remove(state.Id);
                                            if (_selectedState != null && string.Equals(_selectedState.Id, state.Id, StringComparison.Ordinal))
                                            {
                                                _selectedState = null;
                                            }
                                        }
                                        else
                                        {
                                            _selectedStateIds.Add(state.Id);
                                            _selectedState = state;
                                        }

                                        if (_selectedStateIds.Count == 1)
                                        {
                                            string focusedStateId = _selectedStateIds.First();
                                            FusionAnimatorStateDefinition focusedState = FindStateById(focusedStateId);
                                            _selectedState = focusedState;
                                            _selectedLayerIndex = focusedState != null
                                                ? FindLayerIndexById(focusedState.LayerId)
                                                : ResolveFirstSelectedIndex(_selectedLayerIndices);
                                        }
                                        else if (_selectedStateIds.Count == 0)
                                        {
                                            _selectedState = null;
                                            _selectedLayerIndex = ResolveFirstSelectedIndex(_selectedLayerIndices);
                                        }
                                        else
                                        {
                                            _selectedLayerIndex = -1;
                                            _selectedState = null;
                                        }
                                    }
                                    else
                                    {
                                        _selectedState = state;
                                        _selectedTransition = null;
                                        _selectedLayerIndices.Clear();
                                        _selectedStateIds.Clear();
                                        _selectedScopeKeys.Clear();
                                        _selectedStateIds.Add(state.Id);
                                        _selectedLayerIndex = i;
                                        _selectedLayerScopePath = string.Empty;
                                        SetActiveLayer(layer.Id);
                                        SetScopePath(GetStateScopePathFromName(state.Name));
                                        _graphView?.SelectStateById(state.Id, true);
                                    }

                                    _inspector?.MarkDirtyRepaint();
                                    evt.Use();
                                }
                            }

                            List<string> rootScopes = GetChildScopes(string.Empty);
                            for (int rootIndex = 0; rootIndex < rootScopes.Count; ++rootIndex)
                            {
                                DrawScopeRow(rootScopes[rootIndex], 1);
                            }

                            List<FusionAnimatorStateDefinition> rootStates = GetDirectStates(string.Empty);
                            for (int rootStateIndex = 0; rootStateIndex < rootStates.Count; ++rootStateIndex)
                            {
                                DrawStateRow(rootStates[rootStateIndex], 1);
                            }
                        }
                    }

                    if (_dragLayerIndex >= 0 && _dragLayerIndex < _graph.Layers.Count && evt.type == EventType.Repaint)
                    {
                        FusionAnimatorLayerDefinition dragged = _graph.Layers[_dragLayerIndex];
                        List<int> draggedSelection = ResolveDraggedSelectionIndices(_selectedLayerIndices, _dragLayerIndex, _graph.Layers.Count);
                        string dragLabel;
                        if (draggedSelection.Count > 1)
                        {
                            string leadName = dragged != null && string.IsNullOrWhiteSpace(dragged.Name) == false ? dragged.Name : "Layer";
                            dragLabel = string.Format("{0} items ({1}...)", draggedSelection.Count, leadName);
                        }
                        else
                        {
                            dragLabel = dragged != null && string.IsNullOrWhiteSpace(dragged.Name) == false ? dragged.Name : "Layer";
                        }
                        Rect dragRect = new Rect(
                            evt.mousePosition.x + 16.0f,
                            evt.mousePosition.y - 12.0f,
                            Mathf.Min(220.0f, position.width * 0.56f),
                            22.0f);
                        EditorGUI.DrawRect(dragRect, new Color(0.16f, 0.34f, 0.58f, 0.88f));
                        GUI.Label(dragRect, dragLabel, dragGhostStyle);
                    }

                    if (_dragLayerIndex >= 0 && evt.type == EventType.MouseUp)
                    {
                        int draggedIndex = _dragLayerIndex;
                        int insertionIndex = _dragLayerTargetIndex;
                        _dragLayerIndex = -1;
                        _dragLayerTargetIndex = -1;
                        List<int> movingIndices = ResolveDraggedSelectionIndices(_selectedLayerIndices, draggedIndex, _graph.Layers.Count);
                        if (movingIndices.Count > 0)
                        {
                            int draggedOrder = movingIndices.IndexOf(draggedIndex);
                            if (draggedOrder < 0)
                            {
                                draggedOrder = 0;
                            }

                            int insertionAfterRemoval = ComputeInsertionAfterRemoval(movingIndices, insertionIndex, _graph.Layers.Count);
                            bool orderChanged = WouldMoveSelectionChangeOrder(movingIndices, insertionAfterRemoval);
                            if (orderChanged)
                            {
                                RecordUndo("Reorder FusionAnimator Layers");
                                MoveSelectedListItems(_graph.Layers, movingIndices, insertionIndex, out List<int> newIndices, out _);
                                NormalizeLayerPriorities();
                                _selectedLayerIndices.Clear();
                                for (int index = 0; index < newIndices.Count; ++index)
                                {
                                    _selectedLayerIndices.Add(newIndices[index]);
                                }

                                int selectedOrder = Mathf.Clamp(draggedOrder, 0, newIndices.Count - 1);
                                _selectedLayerIndex = newIndices.Count > 0 ? newIndices[selectedOrder] : -1;
                                _layerSelectionAnchorIndex = _selectedLayerIndex;
                                _graphView?.RebuildFromGraphData();
                                _inspector?.MarkDirtyRepaint();
                                MarkGraphDirty();
                            }
                        }

                        evt.Use();
                    }

                    EditorGUILayout.EndScrollView();
                }
            }

            string selectedLayerId = null;
            if (_selectedLayerIndex >= 0 && _selectedLayerIndex < _graph.Layers.Count)
            {
                FusionAnimatorLayerDefinition selectedLayer = _graph.Layers[_selectedLayerIndex];
                if (selectedLayer != null)
                {
                    selectedLayerId = selectedLayer.Id;
                }
            }

            string selectedParameterId = null;
            if (_selectedParameterIndex >= 0 && _selectedParameterIndex < _graph.Parameters.Count)
            {
                FusionAnimatorParameterDefinition selectedParameter = _graph.Parameters[_selectedParameterIndex];
                if (selectedParameter != null)
                {
                    selectedParameterId = selectedParameter.Id;
                }
            }

            _graphView?.SetHoveredLayer(selectedLayerId);
            _graphView?.SetHoveredParameter(selectedParameterId);
        }

        private void DrawBindingsLibrary(Event evt)
        {
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(null);
            EditorGUILayout.LabelField(new GUIContent("Bindings", "Reusable AnimationClip references that clip slots can bind to by id."), EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _bindingSearch = EditorGUILayout.TextField(new GUIContent("Search", "Filter bindings by name, id, or clip name."), _bindingSearch ?? string.Empty);
                if (GUILayout.Button(new GUIContent("+", "Create a new clip binding."), GUILayout.Width(24.0f)))
                {
                    CancelReorderDrag();
                    RecordUndo("Add FusionAnimator Clip Binding");
                    FusionAnimatorClipBindingDefinition binding = new FusionAnimatorClipBindingDefinition
                    {
                        Id = FusionAnimatorGraphAsset.NewId("binding"),
                        Name = "Binding",
                    };
                    _graph.ClipBindings.Add(binding);
                    _selectedBindingIndex = _graph.ClipBindings.Count - 1;
                    _selectedBindingIndices.Clear();
                    _selectedBindingIndices.Add(_selectedBindingIndex);
                    _bindingSelectionAnchorIndex = _selectedBindingIndex;
                    _selectedBindingGroupId = string.Empty;
                    _selectedParameterIndices.Clear();
                    _selectedLayerIndices.Clear();
                    _selectedParameterIndex = -1;
                    _selectedLayerIndex = -1;
                    _selectedState = null;
                    _selectedTransition = null;
                    _selectedLayerScopePath = string.Empty;
                    _selectedEntryLinkTargetStateId = string.Empty;
                    _graphView?.SetHoveredLayer(null);
                    _graphView?.SetHoveredParameter(null);
                    _inspector?.MarkDirtyRepaint();
                    MarkGraphDirty();
                }

                if (GUILayout.Button(new GUIContent("G+", "Create a new binding group."), GUILayout.Width(30.0f)))
                {
                    CreateBindingGroup();
                }

                if ((_selectedBindingIndex < 0 || _selectedBindingIndex >= _graph.ClipBindings.Count) &&
                    _selectedBindingIndices.Count > 0)
                {
                    _selectedBindingIndex = ResolveFirstSelectedIndex(_selectedBindingIndices);
                }

                bool hasSelectedGroup = FindBindingGroupById(_selectedBindingGroupId) != null;
                bool canRemoveBinding = hasSelectedGroup ||
                                        (_selectedBindingIndex >= 0 && _selectedBindingIndex < _graph.ClipBindings.Count);
                using (new EditorGUI.DisabledScope(canRemoveBinding == false))
                {
                    if (GUILayout.Button(new GUIContent("-", "Remove selected binding."), GUILayout.Width(24.0f)))
                    {
                        if (hasSelectedGroup)
                        {
                            RemoveBindingGroup(_selectedBindingGroupId);
                        }
                        else
                        {
                            TryRemoveSelectedBindingFromLibrary();
                        }
                    }
                }
            }

            EditorGUILayout.Space(4.0f);

            using (new EditorGUILayout.VerticalScope(GUILayout.ExpandHeight(true)))
            {
                _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll, GUIStyle.none, GUI.skin.verticalScrollbar, GUILayout.ExpandHeight(true));

                GUIStyle nameStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    padding = new RectOffset(10, 6, 0, 0),
                    fontSize = 12,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                };
                GUIStyle clipStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    normal = { textColor = new Color(0.78f, 0.84f, 0.9f, 1.0f) },
                };
                GUIStyle handleStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    normal = { textColor = new Color(0.75f, 0.75f, 0.75f, 0.95f) },
                };
                GUIStyle groupNameStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    fontStyle = FontStyle.Bold,
                    padding = new RectOffset(6, 6, 0, 0),
                };
                GUIStyle groupCountStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.72f, 0.78f, 0.84f, 0.95f) },
                };
                GUIStyle usageRowStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    padding = new RectOffset(8, 6, 0, 0),
                    normal = { textColor = new Color(0.80f, 0.86f, 0.92f, 0.96f) },
                };
                GUIStyle usageCountStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.MiddleRight,
                    clipping = TextClipping.Clip,
                    wordWrap = false,
                    fontStyle = FontStyle.Bold,
                    normal = { textColor = new Color(0.58f, 0.72f, 0.86f, 0.95f) },
                };
                GUIStyle dragGhostStyle = new GUIStyle(EditorStyles.label)
                {
                    alignment = TextAnchor.MiddleLeft,
                    clipping = TextClipping.Clip,
                    fontSize = 11,
                    normal = { textColor = new Color(0.92f, 0.96f, 1.0f, 0.95f) },
                    padding = new RectOffset(8, 6, 3, 3),
                };

                bool IsKnownGroupId(string groupId)
                {
                    if (string.IsNullOrWhiteSpace(groupId) || _graph.BindingGroups == null)
                    {
                        return false;
                    }

                    for (int groupIndex = 0; groupIndex < _graph.BindingGroups.Count; ++groupIndex)
                    {
                        FusionAnimatorBindingGroupDefinition group = _graph.BindingGroups[groupIndex];
                        if (group != null && string.Equals(group.Id, groupId, StringComparison.Ordinal))
                        {
                            return true;
                        }
                    }

                    return false;
                }

                bool BindingMatchesSearch(FusionAnimatorClipBindingDefinition binding)
                {
                    if (binding == null)
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(_bindingSearch))
                    {
                        return true;
                    }

                    string filter = _bindingSearch.Trim();
                    string optionClipNames = string.Empty;
                    if (binding.Clips != null)
                    {
                        for (int optionIndex = 0; optionIndex < binding.Clips.Count; ++optionIndex)
                        {
                            FusionAnimatorClipBindingSlot option = binding.Clips[optionIndex];
                            if (option?.Clip != null)
                            {
                                if (optionClipNames.Length > 0)
                                {
                                    optionClipNames += " ";
                                }

                                optionClipNames += option.Clip.name;
                            }
                        }
                    }

                    string combined = string.Format("{0} {1} {2}", binding.Name, binding.Id, optionClipNames);
                    return combined.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                bool GroupMatchesSearch(FusionAnimatorBindingGroupDefinition group)
                {
                    if (group == null)
                    {
                        return false;
                    }

                    if (string.IsNullOrWhiteSpace(_bindingSearch))
                    {
                        return true;
                    }

                    string filter = _bindingSearch.Trim();
                    string combined = string.Format("{0} {1}", group.Name, group.Id);
                    return combined.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                }

                int ResolveInsertIndexForGroup(string groupId)
                {
                    if (_graph.ClipBindings == null || _graph.ClipBindings.Count == 0)
                    {
                        return 0;
                    }

                    int insertIndex = _graph.ClipBindings.Count;
                    for (int i = 0; i < _graph.ClipBindings.Count; ++i)
                    {
                        FusionAnimatorClipBindingDefinition candidate = _graph.ClipBindings[i];
                        string candidateGroup = candidate != null && IsKnownGroupId(candidate.GroupId) ? candidate.GroupId : string.Empty;
                        if (string.Equals(candidateGroup, groupId ?? string.Empty, StringComparison.Ordinal))
                        {
                            insertIndex = i + 1;
                        }
                    }

                    return Mathf.Clamp(insertIndex, 0, _graph.ClipBindings.Count);
                }

                if (_dragBindingIndex >= 0 && evt != null && evt.type == EventType.MouseDrag)
                {
                    _dragBindingTargetIndex = _graph.ClipBindings != null ? _graph.ClipBindings.Count : 0;
                    _dragBindingTargetGroupId = string.Empty;
                }

                if (_dragBindingGroupIndex >= 0 && evt != null && evt.type == EventType.MouseDrag)
                {
                    _dragBindingGroupTargetIndex = _graph.BindingGroups != null ? _graph.BindingGroups.Count : 0;
                }

                List<int> ungroupedVisible = new List<int>();
                Dictionary<string, List<int>> groupedVisible = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                Dictionary<string, List<int>> groupedAll = new Dictionary<string, List<int>>(StringComparer.Ordinal);
                for (int bindingIndex = 0; bindingIndex < _graph.ClipBindings.Count; ++bindingIndex)
                {
                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[bindingIndex];
                    if (binding == null)
                    {
                        continue;
                    }

                    bool matches = BindingMatchesSearch(binding);
                    string groupId = IsKnownGroupId(binding.GroupId) ? binding.GroupId : string.Empty;
                    if (string.IsNullOrWhiteSpace(groupId))
                    {
                        if (matches)
                        {
                            ungroupedVisible.Add(bindingIndex);
                        }
                    }
                    else
                    {
                        if (groupedAll.TryGetValue(groupId, out List<int> allList) == false)
                        {
                            allList = new List<int>();
                            groupedAll[groupId] = allList;
                        }

                        allList.Add(bindingIndex);

                        if (matches)
                        {
                            if (groupedVisible.TryGetValue(groupId, out List<int> visibleList) == false)
                            {
                                visibleList = new List<int>();
                                groupedVisible[groupId] = visibleList;
                            }

                            visibleList.Add(bindingIndex);
                        }
                    }
                }

                List<int> ResolveVisibleBindingsForGroup(FusionAnimatorBindingGroupDefinition group, out List<int> allInGroup)
                {
                    allInGroup = groupedAll.TryGetValue(group.Id, out List<int> allList)
                        ? allList
                        : new List<int>();

                    bool groupSearchMatch = GroupMatchesSearch(group);
                    List<int> visibleInGroup = groupSearchMatch
                        ? allInGroup
                        : (groupedVisible.TryGetValue(group.Id, out List<int> visibleList) ? visibleList : new List<int>());
                    if (visibleInGroup.Count == 0 && string.IsNullOrWhiteSpace(_bindingSearch) == false && groupSearchMatch == false)
                    {
                        return null;
                    }

                    return visibleInGroup;
                }

                List<int> visibleBindingSelectionOrder = new List<int>(ungroupedVisible.Count + groupedVisible.Values.Sum(list => list != null ? list.Count : 0));
                for (int visibleUngroupedIndex = 0; visibleUngroupedIndex < ungroupedVisible.Count; ++visibleUngroupedIndex)
                {
                    visibleBindingSelectionOrder.Add(ungroupedVisible[visibleUngroupedIndex]);
                }

                if (_graph.BindingGroups != null)
                {
                    for (int groupIndex = 0; groupIndex < _graph.BindingGroups.Count; ++groupIndex)
                    {
                        FusionAnimatorBindingGroupDefinition group = _graph.BindingGroups[groupIndex];
                        if (group == null || string.IsNullOrWhiteSpace(group.Id))
                        {
                            continue;
                        }

                        if (_bindingGroupFoldoutStates.TryGetValue(group.Id, out bool expanded) == false)
                        {
                            expanded = true;
                            _bindingGroupFoldoutStates[group.Id] = true;
                        }

                        List<int> visibleInGroup = ResolveVisibleBindingsForGroup(group, out _);
                        if (visibleInGroup == null || expanded == false)
                        {
                            continue;
                        }

                        for (int visibleIndex = 0; visibleIndex < visibleInGroup.Count; ++visibleIndex)
                        {
                            visibleBindingSelectionOrder.Add(visibleInGroup[visibleIndex]);
                        }
                    }
                }

                void DrawBindingRow(int bindingIndex, string groupId, int indentLevel)
                {
                    if (bindingIndex < 0 || bindingIndex >= _graph.ClipBindings.Count)
                    {
                        return;
                    }

                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[bindingIndex];
                    if (binding == null)
                    {
                        return;
                    }

                    Rect rowRect = EditorGUILayout.GetControlRect(false, 24.0f);
                    float indent = Mathf.Max(0, indentLevel) * 18.0f;
                    rowRect.xMin += indent;
                    rowRect.xMax -= 14.0f;
                    bool selected = IsBindingLibrarySelected(bindingIndex);
                    Color rowColor = selected
                        ? new Color(0.18f, 0.36f, 0.60f, 0.78f)
                        : new Color(0.19f, 0.19f, 0.19f, 0.96f);
                    EditorGUI.DrawRect(rowRect, rowColor);

                    Rect handleRect = rowRect;
                    handleRect.xMin += 2.0f;
                    handleRect.xMax = handleRect.xMin + 12.0f;
                    GUI.Label(handleRect, "|||", handleStyle);

                    string bindingId = binding.Id ?? string.Empty;
                    List<BindingUsageLocation> usages = BuildBindingUsageLocations(bindingId);
                    bool hasUsages = usages.Count > 0;
                    Rect usageFoldoutRect = rowRect;
                    usageFoldoutRect.xMin = handleRect.xMax + 2.0f;
                    usageFoldoutRect.xMax = usageFoldoutRect.xMin + 14.0f;
                    bool usageExpanded = false;
                    if (hasUsages)
                    {
                        usageExpanded = _bindingUsageFoldoutStates.TryGetValue(bindingId, out bool expanded) && expanded;
                        bool nextExpanded = EditorGUI.Foldout(usageFoldoutRect, usageExpanded, GUIContent.none, false);
                        if (nextExpanded != usageExpanded)
                        {
                            _bindingUsageFoldoutStates[bindingId] = nextExpanded;
                            usageExpanded = nextExpanded;
                        }
                    }

                    Rect nameRect = rowRect;
                    nameRect.xMin = usageFoldoutRect.xMax + 2.0f;
                    nameRect.xMax -= 130.0f;
                    GUI.Label(nameRect, string.IsNullOrWhiteSpace(binding.Name) ? "Binding" : binding.Name, nameStyle);

                    if (hasUsages)
                    {
                        Rect usageCountRect = rowRect;
                        usageCountRect.xMin = nameRect.xMax + 2.0f;
                        usageCountRect.xMax -= 132.0f;
                        GUI.Label(usageCountRect, string.Format("{0}", usages.Count), usageCountStyle);
                    }

                    Rect clipRect = rowRect;
                    clipRect.xMin = nameRect.xMax + 4.0f;
                    clipRect.xMax -= 8.0f;
                    FusionAnimatorClipBindingSlot activeSlot = string.IsNullOrWhiteSpace(binding.Id)
                        ? null
                        : FusionAnimatorClipBindingUtility.ResolveBindingClipSlot(_graph, binding.Id, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                    AnimationClip displayClip = activeSlot != null ? activeSlot.Clip : null;
                    GUI.Label(clipRect, displayClip != null ? displayClip.name : "<None>", clipStyle);

                    bool clickOnUsageFoldout = evt != null &&
                                               evt.type == EventType.MouseDown &&
                                               evt.button == 0 &&
                                               hasUsages &&
                                               usageFoldoutRect.Contains(evt.mousePosition);
                    if (evt != null && evt.type == EventType.MouseDown && evt.button == 0 && rowRect.Contains(evt.mousePosition) && clickOnUsageFoldout == false)
                    {
                        if (handleRect.Contains(evt.mousePosition))
                        {
                            if (_selectedBindingIndices.Count <= 1 || _selectedBindingIndices.Contains(bindingIndex) == false)
                            {
                                _selectedBindingIndices.Clear();
                                _selectedBindingIndices.Add(bindingIndex);
                                _bindingSelectionAnchorIndex = bindingIndex;
                                _selectedBindingGroupId = string.Empty;
                                _selectedParameterIndices.Clear();
                                _selectedLayerIndices.Clear();
                                _selectedBindingIndex = bindingIndex;
                            }

                            _dragBindingIndex = bindingIndex;
                            _dragBindingTargetIndex = bindingIndex;
                            _dragBindingTargetGroupId = groupId ?? string.Empty;
                        }
                        else
                        {
                            CancelReorderDrag();
                            ClearGraphSelectionForLibraryInteraction();
                            SelectLibraryIndexByVisibleOrder(
                                _selectedBindingIndices,
                                ref _selectedBindingIndex,
                                ref _bindingSelectionAnchorIndex,
                                bindingIndex,
                                visibleBindingSelectionOrder,
                                _graph.ClipBindings.Count);
                            _selectedBindingGroupId = string.Empty;
                            _selectedParameterIndices.Clear();
                            _selectedLayerIndices.Clear();
                            _selectedParameterIndex = -1;
                            _selectedLayerIndex = -1;
                            _selectedState = null;
                            _selectedTransition = null;
                            _selectedLayerScopePath = string.Empty;
                            _selectedEntryLinkTargetStateId = string.Empty;
                            _graphView?.SetHoveredLayer(null);
                            _graphView?.SetHoveredParameter(null);
                            _inspector?.MarkDirtyRepaint();
                        }

                        evt.Use();
                    }
                    else if (evt != null && evt.type == EventType.MouseDown && evt.button == 1 && rowRect.Contains(evt.mousePosition))
                    {
                        CancelReorderDrag();
                        ClearGraphSelectionForLibraryInteraction();
                        if (_selectedBindingIndices.Count == 0 || _selectedBindingIndices.Contains(bindingIndex) == false)
                        {
                            _selectedBindingIndices.Clear();
                            _selectedBindingIndices.Add(bindingIndex);
                            _bindingSelectionAnchorIndex = bindingIndex;
                        }
                        _selectedBindingGroupId = string.Empty;
                        _selectedParameterIndices.Clear();
                        _selectedLayerIndices.Clear();
                        _selectedBindingIndex = bindingIndex;
                        _selectedParameterIndex = -1;
                        _selectedLayerIndex = -1;
                        _selectedState = null;
                        _selectedTransition = null;
                        _selectedLayerScopePath = string.Empty;
                        _selectedEntryLinkTargetStateId = string.Empty;
                        _graphView?.SetHoveredLayer(null);
                        _graphView?.SetHoveredParameter(null);
                        _inspector?.MarkDirtyRepaint();
                        ShowBindingContextMenu(bindingIndex);
                        evt.Use();
                    }

                    if (_dragBindingIndex >= 0 && evt != null && evt.type == EventType.MouseDrag && rowRect.Contains(evt.mousePosition))
                    {
                        _dragBindingTargetIndex = evt.mousePosition.y > rowRect.center.y ? bindingIndex + 1 : bindingIndex;
                        _dragBindingTargetGroupId = groupId ?? string.Empty;
                        evt.Use();
                    }

                    if (_dragBindingIndex >= 0 && (bindingIndex == _dragBindingTargetIndex || bindingIndex + 1 == _dragBindingTargetIndex))
                    {
                        Color indicator = new Color(0.42f, 0.68f, 1.0f, 0.95f);
                        bool insertAfter = _dragBindingTargetIndex == bindingIndex + 1;
                        float y = insertAfter ? rowRect.yMax : rowRect.yMin;
                        EditorGUI.DrawRect(new Rect(rowRect.xMin + 1.0f, y - 1.0f, Mathf.Max(0.0f, rowRect.width - 2.0f), 2.0f), indicator);
                    }

                    if (hasUsages && usageExpanded)
                    {
                        for (int usageIndex = 0; usageIndex < usages.Count; ++usageIndex)
                        {
                            BindingUsageLocation usage = usages[usageIndex];
                            if (usage == null)
                            {
                                continue;
                            }

                            Rect usageRowRect = EditorGUILayout.GetControlRect(false, 18.0f);
                            usageRowRect.xMin += 30.0f + indent;
                            usageRowRect.xMax -= 14.0f;
                            bool usageHovered = evt != null && usageRowRect.Contains(evt.mousePosition);
                            Color usageRowColor = usageHovered
                                ? new Color(0.18f, 0.26f, 0.36f, 0.65f)
                                : new Color(0.13f, 0.13f, 0.13f, 0.82f);
                            EditorGUI.DrawRect(usageRowRect, usageRowColor);

                            Rect usageNameRect = usageRowRect;
                            usageNameRect.xMin += 8.0f;
                            GUI.Label(usageNameRect, usage.Label, usageRowStyle);

                            if (evt != null && evt.type == EventType.MouseDown && evt.button == 0 && usageRowRect.Contains(evt.mousePosition))
                            {
                                CancelReorderDrag();
                                JumpToBindingUsage(usage);
                                evt.Use();
                            }
                        }
                    }
                }

                void DrawGroupHeader(FusionAnimatorBindingGroupDefinition group, int groupIndex, int visibleCount, int totalCount)
                {
                    if (group == null || string.IsNullOrWhiteSpace(group.Id))
                    {
                        return;
                    }

                    if (_bindingGroupFoldoutStates.TryGetValue(group.Id, out bool _) == false)
                    {
                        _bindingGroupFoldoutStates[group.Id] = true;
                    }

                    bool expanded = _bindingGroupFoldoutStates[group.Id];
                    Rect headerRect = EditorGUILayout.GetControlRect(false, 22.0f);
                    headerRect.xMax -= 14.0f;
                    bool groupSelected = string.Equals(_selectedBindingGroupId, group.Id, StringComparison.Ordinal);
                    Color headerColor = groupSelected
                        ? new Color(0.20f, 0.37f, 0.58f, 0.82f)
                        : new Color(0.16f, 0.16f, 0.16f, 0.94f);
                    EditorGUI.DrawRect(headerRect, headerColor);

                    Rect handleRect = headerRect;
                    handleRect.xMin += 2.0f;
                    handleRect.xMax = handleRect.xMin + 12.0f;
                    GUI.Label(handleRect, "|||", handleStyle);

                    Rect foldoutRect = headerRect;
                    foldoutRect.xMin = handleRect.xMax + 2.0f;
                    foldoutRect.xMax = foldoutRect.xMin + 14.0f;
                    bool nextExpanded = EditorGUI.Foldout(foldoutRect, expanded, GUIContent.none, false);
                    if (nextExpanded != expanded)
                    {
                        _bindingGroupFoldoutStates[group.Id] = nextExpanded;
                        expanded = nextExpanded;
                    }

                    Rect nameRect = headerRect;
                    nameRect.xMin = foldoutRect.xMax + 2.0f;
                    float nameWidth = Mathf.Min(128.0f, Mathf.Max(84.0f, headerRect.width * 0.32f));
                    nameRect.xMax = nameRect.xMin + nameWidth;
                    if (groupSelected)
                    {
                        EditorGUI.BeginChangeCheck();
                        string name = EditorGUI.DelayedTextField(nameRect, string.IsNullOrWhiteSpace(group.Name) ? "Group" : group.Name, groupNameStyle);
                        if (EditorGUI.EndChangeCheck())
                        {
                            RecordUndo("Rename FusionAnimator Binding Group");
                            group.Name = string.IsNullOrWhiteSpace(name) ? "Group" : name;
                            MarkGraphDirty();
                        }
                    }
                    else
                    {
                        GUI.Label(nameRect, string.IsNullOrWhiteSpace(group.Name) ? "Group" : group.Name, groupNameStyle);
                    }

                    Rect countRect = headerRect;
                    countRect.xMin = nameRect.xMax + 8.0f;
                    countRect.xMax -= 6.0f;
                    GUI.Label(countRect, string.Format("{0}/{1}", visibleCount, totalCount), groupCountStyle);

                    if (_dragBindingIndex >= 0 && evt != null && evt.type == EventType.MouseDrag && headerRect.Contains(evt.mousePosition))
                    {
                        _dragBindingTargetGroupId = group.Id;
                        _dragBindingTargetIndex = ResolveInsertIndexForGroup(group.Id);
                        evt.Use();
                    }

                    if (_dragBindingIndex >= 0 && string.Equals(_dragBindingTargetGroupId, group.Id, StringComparison.Ordinal))
                    {
                        Color indicator = new Color(0.42f, 0.68f, 1.0f, 0.55f);
                        EditorGUI.DrawRect(new Rect(headerRect.xMin + 1.0f, headerRect.yMin + 1.0f, Mathf.Max(0.0f, headerRect.width - 2.0f), Mathf.Max(0.0f, headerRect.height - 2.0f)), indicator);
                    }

                    if (evt != null && evt.type == EventType.MouseDown && evt.button == 0 && headerRect.Contains(evt.mousePosition))
                    {
                        if (foldoutRect.Contains(evt.mousePosition) == false)
                        {
                            if (handleRect.Contains(evt.mousePosition))
                            {
                                CancelReorderDrag();
                                _dragBindingGroupIndex = groupIndex;
                                _dragBindingGroupTargetIndex = groupIndex;
                            }
                            else
                            {
                                CancelReorderDrag();
                            }

                            ClearGraphSelectionForLibraryInteraction();
                            _selectedBindingGroupId = group.Id;
                            _selectedBindingIndex = -1;
                            _selectedBindingIndices.Clear();
                            _bindingSelectionAnchorIndex = -1;
                            _selectedParameterIndices.Clear();
                            _selectedLayerIndices.Clear();
                            _selectedParameterIndex = -1;
                            _selectedLayerIndex = -1;
                            _selectedState = null;
                            _selectedTransition = null;
                            _selectedLayerScopePath = string.Empty;
                            _selectedEntryLinkTargetStateId = string.Empty;
                            _graphView?.SetHoveredLayer(null);
                            _graphView?.SetHoveredParameter(null);
                            _inspector?.MarkDirtyRepaint();
                            evt.Use();
                        }
                    }

                    if (_dragBindingGroupIndex >= 0 && evt != null && evt.type == EventType.MouseDrag && headerRect.Contains(evt.mousePosition))
                    {
                        _dragBindingGroupTargetIndex = evt.mousePosition.y > headerRect.center.y ? groupIndex + 1 : groupIndex;
                        evt.Use();
                    }

                    if (_dragBindingGroupIndex >= 0 && (groupIndex == _dragBindingGroupTargetIndex || groupIndex + 1 == _dragBindingGroupTargetIndex))
                    {
                        Color indicator = new Color(0.35f, 0.62f, 0.96f, 0.95f);
                        bool insertAfter = _dragBindingGroupTargetIndex == groupIndex + 1;
                        float y = insertAfter ? headerRect.yMax : headerRect.yMin;
                        EditorGUI.DrawRect(new Rect(headerRect.xMin + 1.0f, y - 1.0f, Mathf.Max(0.0f, headerRect.width - 2.0f), 2.0f), indicator);
                    }

                    if (evt != null && evt.type == EventType.MouseDown && evt.button == 1 && headerRect.Contains(evt.mousePosition))
                    {
                        _selectedBindingGroupId = group.Id;
                        _selectedBindingIndex = -1;
                        _selectedBindingIndices.Clear();
                        _bindingSelectionAnchorIndex = -1;
                        _selectedParameterIndices.Clear();
                        _selectedLayerIndices.Clear();
                        _selectedParameterIndex = -1;
                        _selectedLayerIndex = -1;
                        _selectedState = null;
                        _selectedTransition = null;
                        _selectedLayerScopePath = string.Empty;
                        _selectedEntryLinkTargetStateId = string.Empty;
                        _graphView?.SetHoveredLayer(null);
                        _graphView?.SetHoveredParameter(null);
                        _inspector?.MarkDirtyRepaint();
                        ShowBindingGroupContextMenu(group.Id);
                        evt.Use();
                    }
                }

                bool hasVisibleContent = false;
                for (int i = 0; i < ungroupedVisible.Count; ++i)
                {
                    hasVisibleContent = true;
                    DrawBindingRow(ungroupedVisible[i], string.Empty, 0);
                }

                if (_graph.BindingGroups != null)
                {
                    for (int groupIndex = 0; groupIndex < _graph.BindingGroups.Count; ++groupIndex)
                    {
                        FusionAnimatorBindingGroupDefinition group = _graph.BindingGroups[groupIndex];
                        if (group == null || string.IsNullOrWhiteSpace(group.Id))
                        {
                            continue;
                        }

                        List<int> visibleInGroup = ResolveVisibleBindingsForGroup(group, out List<int> allInGroup);
                        if (visibleInGroup == null)
                        {
                            continue;
                        }

                        hasVisibleContent = true;
                        DrawGroupHeader(group, groupIndex, visibleInGroup.Count, allInGroup.Count);
                        bool expanded = _bindingGroupFoldoutStates.TryGetValue(group.Id, out bool expandedValue) && expandedValue;
                        if (expanded)
                        {
                            for (int i = 0; i < visibleInGroup.Count; ++i)
                            {
                                DrawBindingRow(visibleInGroup[i], group.Id, 1);
                            }
                        }
                    }
                }

                if (hasVisibleContent == false)
                {
                    EditorGUILayout.HelpBox("No bindings match the current filter.", MessageType.Info);
                }

                if (_dragBindingIndex >= 0 && _dragBindingIndex < _graph.ClipBindings.Count && evt != null && evt.type == EventType.Repaint)
                {
                    FusionAnimatorClipBindingDefinition dragged = _graph.ClipBindings[_dragBindingIndex];
                    List<int> draggedSelection = ResolveDraggedSelectionIndices(_selectedBindingIndices, _dragBindingIndex, _graph.ClipBindings.Count);
                    string dragLabel;
                    if (draggedSelection.Count > 1)
                    {
                        string leadName = dragged != null && string.IsNullOrWhiteSpace(dragged.Name) == false ? dragged.Name : "Binding";
                        dragLabel = string.Format("{0} items ({1}...)", draggedSelection.Count, leadName);
                    }
                    else
                    {
                        dragLabel = dragged != null && string.IsNullOrWhiteSpace(dragged.Name) == false ? dragged.Name : "Binding";
                    }
                    Rect dragRect = new Rect(
                        evt.mousePosition.x + 16.0f,
                        evt.mousePosition.y - 12.0f,
                        Mathf.Min(220.0f, position.width * 0.56f),
                        22.0f);
                    EditorGUI.DrawRect(dragRect, new Color(0.16f, 0.34f, 0.58f, 0.88f));
                    GUI.Label(dragRect, dragLabel, dragGhostStyle);
                }
                else if (_dragBindingGroupIndex >= 0 &&
                         _graph.BindingGroups != null &&
                         _dragBindingGroupIndex < _graph.BindingGroups.Count &&
                         evt != null &&
                         evt.type == EventType.Repaint)
                {
                    FusionAnimatorBindingGroupDefinition draggedGroup = _graph.BindingGroups[_dragBindingGroupIndex];
                    string dragLabel = draggedGroup != null && string.IsNullOrWhiteSpace(draggedGroup.Name) == false ? draggedGroup.Name : "Group";
                    Rect dragRect = new Rect(
                        evt.mousePosition.x + 16.0f,
                        evt.mousePosition.y - 12.0f,
                        Mathf.Min(220.0f, position.width * 0.56f),
                        22.0f);
                    EditorGUI.DrawRect(dragRect, new Color(0.18f, 0.30f, 0.50f, 0.88f));
                    GUI.Label(dragRect, dragLabel, dragGhostStyle);
                }

                if (_dragBindingIndex >= 0 && evt != null && evt.type == EventType.MouseUp)
                {
                    int draggedIndex = _dragBindingIndex;
                    int insertionIndex = _dragBindingTargetIndex;
                    string targetGroupId = _dragBindingTargetGroupId;
                    _dragBindingIndex = -1;
                    _dragBindingTargetIndex = -1;
                    _dragBindingTargetGroupId = null;

                    List<int> movingIndices = ResolveDraggedSelectionIndices(_selectedBindingIndices, draggedIndex, _graph.ClipBindings.Count);
                    if (movingIndices.Count > 0)
                    {
                        string normalizedTargetGroup = IsKnownGroupId(targetGroupId) ? targetGroupId : string.Empty;
                        int draggedOrder = movingIndices.IndexOf(draggedIndex);
                        if (draggedOrder < 0)
                        {
                            draggedOrder = 0;
                        }

                        bool groupChanged = false;
                        for (int i = 0; i < movingIndices.Count; ++i)
                        {
                            int index = movingIndices[i];
                            if (index < 0 || index >= _graph.ClipBindings.Count)
                            {
                                continue;
                            }

                            FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[index];
                            string currentGroupId = IsKnownGroupId(binding?.GroupId) ? binding.GroupId : string.Empty;
                            if (string.Equals(currentGroupId, normalizedTargetGroup, StringComparison.Ordinal) == false)
                            {
                                groupChanged = true;
                                break;
                            }
                        }

                        int insertionAfterRemoval = ComputeInsertionAfterRemoval(movingIndices, insertionIndex, _graph.ClipBindings.Count);
                        bool orderChanged = WouldMoveSelectionChangeOrder(movingIndices, insertionAfterRemoval);
                        if (orderChanged || groupChanged)
                        {
                            RecordUndo("Reorder FusionAnimator Bindings");
                            List<int> newIndices;
                            if (orderChanged)
                            {
                                if (MoveSelectedListItems(_graph.ClipBindings, movingIndices, insertionIndex, out newIndices, out _) == false)
                                {
                                    newIndices = new List<int>(movingIndices);
                                }
                            }
                            else
                            {
                                newIndices = new List<int>(movingIndices);
                            }

                            for (int movedIndex = 0; movedIndex < newIndices.Count; ++movedIndex)
                            {
                                int bindingIndex = newIndices[movedIndex];
                                if (bindingIndex < 0 || bindingIndex >= _graph.ClipBindings.Count)
                                {
                                    continue;
                                }

                                FusionAnimatorClipBindingDefinition movedBinding = _graph.ClipBindings[bindingIndex];
                                if (movedBinding != null)
                                {
                                    movedBinding.GroupId = normalizedTargetGroup;
                                }
                            }

                            _selectedBindingIndices.Clear();
                            _selectedBindingGroupId = string.Empty;
                            for (int index = 0; index < newIndices.Count; ++index)
                            {
                                _selectedBindingIndices.Add(newIndices[index]);
                            }

                            int selectedOrder = Mathf.Clamp(draggedOrder, 0, newIndices.Count - 1);
                            _selectedBindingIndex = newIndices.Count > 0 ? newIndices[selectedOrder] : -1;
                            _bindingSelectionAnchorIndex = _selectedBindingIndex;
                            _inspector?.MarkDirtyRepaint();
                            MarkGraphDirty();
                        }
                    }

                    evt.Use();
                }

                if (_dragBindingGroupIndex >= 0 && evt != null && evt.type == EventType.MouseUp)
                {
                    int fromGroupIndex = _dragBindingGroupIndex;
                    int toGroupIndex = _dragBindingGroupTargetIndex;
                    _dragBindingGroupIndex = -1;
                    _dragBindingGroupTargetIndex = -1;
                    toGroupIndex = Mathf.Clamp(toGroupIndex, 0, _graph.BindingGroups.Count);
                    if (toGroupIndex > fromGroupIndex)
                    {
                        toGroupIndex--;
                    }

                    if (fromGroupIndex >= 0 &&
                        fromGroupIndex < _graph.BindingGroups.Count &&
                        toGroupIndex >= 0 &&
                        toGroupIndex < _graph.BindingGroups.Count &&
                        fromGroupIndex != toGroupIndex)
                    {
                        RecordUndo("Reorder FusionAnimator Binding Groups");
                        MoveListItem(_graph.BindingGroups, fromGroupIndex, toGroupIndex);
                        _inspector?.MarkDirtyRepaint();
                        MarkGraphDirty();
                    }

                    evt.Use();
                }

                EditorGUILayout.EndScrollView();
            }
        }
        private static void MoveListItem<T>(List<T> list, int fromIndex, int toIndex)
        {
            if (list == null || fromIndex < 0 || toIndex < 0 || fromIndex >= list.Count || toIndex > list.Count || fromIndex == toIndex)
            {
                return;
            }

            T item = list[fromIndex];
            list.RemoveAt(fromIndex);
            if (toIndex > fromIndex)
            {
                toIndex--;
            }

            toIndex = Mathf.Clamp(toIndex, 0, list.Count);
            list.Insert(toIndex, item);
        }

        private static bool AreIndicesContiguous(IList<int> sortedIndices)
        {
            if (sortedIndices == null || sortedIndices.Count <= 1)
            {
                return true;
            }

            for (int i = 1; i < sortedIndices.Count; ++i)
            {
                if (sortedIndices[i] != sortedIndices[i - 1] + 1)
                {
                    return false;
                }
            }

            return true;
        }

        private static List<int> ResolveDraggedSelectionIndices(HashSet<int> selectedIndices, int draggedIndex, int count)
        {
            List<int> resolved = new List<int>();
            if (draggedIndex < 0 || draggedIndex >= count)
            {
                return resolved;
            }

            if (selectedIndices != null && selectedIndices.Count > 1 && selectedIndices.Contains(draggedIndex))
            {
                foreach (int index in selectedIndices)
                {
                    if (index >= 0 && index < count)
                    {
                        resolved.Add(index);
                    }
                }

                resolved.Sort();
                return resolved;
            }

            resolved.Add(draggedIndex);
            return resolved;
        }

        private static int ComputeInsertionAfterRemoval(IList<int> sortedIndices, int insertionIndex, int listCount)
        {
            if (sortedIndices == null || sortedIndices.Count == 0)
            {
                return -1;
            }

            insertionIndex = Mathf.Clamp(insertionIndex, 0, listCount);
            int removedBefore = 0;
            for (int i = 0; i < sortedIndices.Count; ++i)
            {
                if (sortedIndices[i] < insertionIndex)
                {
                    removedBefore++;
                }
            }

            return Mathf.Clamp(insertionIndex - removedBefore, 0, listCount - sortedIndices.Count);
        }

        private static bool WouldMoveSelectionChangeOrder(IList<int> sortedIndices, int insertionAfterRemoval)
        {
            if (sortedIndices == null || sortedIndices.Count == 0)
            {
                return false;
            }

            int firstIndex = sortedIndices[0];
            return AreIndicesContiguous(sortedIndices) == false || insertionAfterRemoval != firstIndex;
        }

        private static bool MoveSelectedListItems<T>(
            List<T> list,
            List<int> selectedSortedIndices,
            int insertionIndex,
            out List<int> newSelectedIndices,
            out int insertionAfterRemoval)
        {
            newSelectedIndices = new List<int>();
            insertionAfterRemoval = -1;
            if (list == null || selectedSortedIndices == null || selectedSortedIndices.Count == 0)
            {
                return false;
            }

            List<int> validIndices = new List<int>(selectedSortedIndices.Count);
            int last = int.MinValue;
            for (int i = 0; i < selectedSortedIndices.Count; ++i)
            {
                int index = selectedSortedIndices[i];
                if (index < 0 || index >= list.Count || index == last)
                {
                    continue;
                }

                validIndices.Add(index);
                last = index;
            }

            if (validIndices.Count == 0)
            {
                return false;
            }

            insertionAfterRemoval = ComputeInsertionAfterRemoval(validIndices, insertionIndex, list.Count);
            bool orderChanged = WouldMoveSelectionChangeOrder(validIndices, insertionAfterRemoval);
            if (orderChanged == false)
            {
                for (int i = 0; i < validIndices.Count; ++i)
                {
                    newSelectedIndices.Add(validIndices[i]);
                }

                return false;
            }

            List<T> movedItems = new List<T>(validIndices.Count);
            for (int i = 0; i < validIndices.Count; ++i)
            {
                movedItems.Add(list[validIndices[i]]);
            }

            for (int i = validIndices.Count - 1; i >= 0; --i)
            {
                list.RemoveAt(validIndices[i]);
            }

            for (int i = 0; i < movedItems.Count; ++i)
            {
                list.Insert(insertionAfterRemoval + i, movedItems[i]);
                newSelectedIndices.Add(insertionAfterRemoval + i);
            }

            return true;
        }

        private void CancelReorderDrag()
        {
            _dragParameterIndex = -1;
            _dragParameterTargetIndex = -1;
            _dragBindingIndex = -1;
            _dragBindingTargetIndex = -1;
            _dragBindingTargetGroupId = null;
            _dragBindingGroupIndex = -1;
            _dragBindingGroupTargetIndex = -1;
            _dragLayerIndex = -1;
            _dragLayerTargetIndex = -1;
        }

        private void ClearLibraryMultiSelectionState()
        {
            _selectedParameterIndices.Clear();
            _selectedBindingIndices.Clear();
            _selectedLayerIndices.Clear();
            _selectedStateIds.Clear();
            _selectedScopeKeys.Clear();
            _selectedBindingGroupId = string.Empty;
            _parameterSelectionAnchorIndex = -1;
            _bindingSelectionAnchorIndex = -1;
            _layerSelectionAnchorIndex = -1;
        }

        private int GetExplicitSelectionCount()
        {
            int selectionCount = 0;

            selectionCount += _selectedParameterIndices.Count > 0
                ? _selectedParameterIndices.Count
                : (_selectedParameterIndex >= 0 ? 1 : 0);

            selectionCount += _selectedBindingIndices.Count > 0
                ? _selectedBindingIndices.Count
                : (_selectedBindingIndex >= 0 ? 1 : 0);

            selectionCount += string.IsNullOrWhiteSpace(_selectedBindingGroupId) ? 0 : 1;

            bool hasStateLikeSelection = false;
            if (_selectedStateIds.Count > 0)
            {
                selectionCount += _selectedStateIds.Count;
                hasStateLikeSelection = true;
            }
            else if (_selectedState != null)
            {
                selectionCount += 1;
                hasStateLikeSelection = true;
            }

            if (_selectedTransition != null)
            {
                selectionCount += 1;
                hasStateLikeSelection = true;
            }

            if (string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false)
            {
                selectionCount += 1;
                hasStateLikeSelection = true;
            }

            if (_selectedScopeKeys.Count > 0)
            {
                selectionCount += _selectedScopeKeys.Count;
                hasStateLikeSelection = true;
            }
            else if (string.IsNullOrWhiteSpace(_selectedLayerScopePath) == false && _selectedState == null)
            {
                selectionCount += 1;
                hasStateLikeSelection = true;
            }

            if (_selectedLayerIndices.Count > 0)
            {
                selectionCount += _selectedLayerIndices.Count;
            }
            else if (_selectedLayerIndex >= 0 && hasStateLikeSelection == false)
            {
                selectionCount += 1;
            }

            return selectionCount;
        }

        private bool HasAnyMultiSelectionContext()
        {
            if (GetExplicitSelectionCount() > 1)
            {
                return true;
            }

            return false;
        }

        private static string BuildScopeSelectionKey(string layerId, string scopePath)
        {
            return string.Format("{0}|{1}", layerId ?? string.Empty, NormalizeScopePath(scopePath) ?? string.Empty);
        }

        private static int ResolveFirstSelectedIndex(HashSet<int> indices)
        {
            if (indices == null || indices.Count == 0)
            {
                return -1;
            }

            int first = int.MaxValue;
            foreach (int index in indices)
            {
                if (index < first)
                {
                    first = index;
                }
            }

            return first == int.MaxValue ? -1 : first;
        }

        private static void SelectIndexRange(HashSet<int> indices, int fromInclusive, int toInclusive)
        {
            if (indices == null)
            {
                return;
            }

            indices.Clear();
            if (fromInclusive > toInclusive)
            {
                int swap = fromInclusive;
                fromInclusive = toInclusive;
                toInclusive = swap;
            }

            for (int i = fromInclusive; i <= toInclusive; ++i)
            {
                indices.Add(i);
            }
        }

        private void SelectLibraryIndex(
            HashSet<int> indices,
            ref int primaryIndex,
            ref int anchorIndex,
            int clickedIndex,
            int count)
        {
            if (indices == null || clickedIndex < 0 || clickedIndex >= count)
            {
                return;
            }

            Event evt = Event.current;
            bool additive = evt != null && (evt.control || evt.command);
            bool range = evt != null && evt.shift;

            if (range)
            {
                int anchor = anchorIndex >= 0 ? anchorIndex : (primaryIndex >= 0 ? primaryIndex : clickedIndex);
                anchor = Mathf.Clamp(anchor, 0, Mathf.Max(0, count - 1));
                SelectIndexRange(indices, anchor, clickedIndex);
            }
            else if (additive)
            {
                if (indices.Contains(clickedIndex))
                {
                    indices.Remove(clickedIndex);
                }
                else
                {
                    indices.Add(clickedIndex);
                }

                anchorIndex = clickedIndex;
            }
            else
            {
                indices.Clear();
                indices.Add(clickedIndex);
                anchorIndex = clickedIndex;
            }

            if (indices.Count == 0)
            {
                primaryIndex = -1;
            }
            else if (indices.Contains(clickedIndex))
            {
                primaryIndex = clickedIndex;
            }
            else
            {
                primaryIndex = ResolveFirstSelectedIndex(indices);
            }
        }

        private void SelectLibraryIndexByVisibleOrder(
            HashSet<int> indices,
            ref int primaryIndex,
            ref int anchorIndex,
            int clickedIndex,
            List<int> visibleIndices,
            int totalCount)
        {
            if (indices == null)
            {
                return;
            }

            Event evt = Event.current;
            bool range = evt != null && evt.shift;
            if (range == false || visibleIndices == null || visibleIndices.Count == 0)
            {
                SelectLibraryIndex(indices, ref primaryIndex, ref anchorIndex, clickedIndex, totalCount);
                return;
            }

            int clickedVisibleIndex = visibleIndices.IndexOf(clickedIndex);
            if (clickedVisibleIndex < 0)
            {
                SelectLibraryIndex(indices, ref primaryIndex, ref anchorIndex, clickedIndex, totalCount);
                return;
            }

            int anchorValue = anchorIndex >= 0 ? anchorIndex : (primaryIndex >= 0 ? primaryIndex : clickedIndex);
            int anchorVisibleIndex = visibleIndices.IndexOf(anchorValue);
            if (anchorVisibleIndex < 0)
            {
                anchorVisibleIndex = clickedVisibleIndex;
                anchorIndex = clickedIndex;
            }

            int start = Mathf.Min(anchorVisibleIndex, clickedVisibleIndex);
            int end = Mathf.Max(anchorVisibleIndex, clickedVisibleIndex);
            indices.Clear();
            for (int i = start; i <= end; ++i)
            {
                int candidateIndex = visibleIndices[i];
                if (candidateIndex >= 0 && candidateIndex < totalCount)
                {
                    indices.Add(candidateIndex);
                }
            }

            if (indices.Count == 0)
            {
                primaryIndex = -1;
            }
            else if (indices.Contains(clickedIndex))
            {
                primaryIndex = clickedIndex;
            }
            else
            {
                primaryIndex = ResolveFirstSelectedIndex(indices);
            }
        }

        private bool IsParameterLibrarySelected(int index)
        {
            if (_selectedParameterIndices.Count > 0)
            {
                return _selectedParameterIndices.Contains(index);
            }

            return _selectedParameterIndex == index;
        }

        private bool IsBindingLibrarySelected(int index)
        {
            if (_selectedBindingIndices.Count > 0)
            {
                return _selectedBindingIndices.Contains(index);
            }

            return _selectedBindingIndex == index;
        }

        private bool IsLayerLibrarySelected(int index)
        {
            if (_selectedLayerIndices.Count > 0)
            {
                return _selectedLayerIndices.Contains(index);
            }

            return _selectedLayerIndex == index;
        }

        private void NormalizeLayerPriorities()
        {
            if (_graph?.Layers == null)
            {
                return;
            }

            for (int i = 0; i < _graph.Layers.Count; ++i)
            {
                FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                if (layer != null)
                {
                    layer.Priority = i;
                }
            }
        }

        private void DrawInspectorPanel()
        {
            if (_graph == null)
            {
                EditorGUILayout.HelpBox("Select a FusionAnimator graph asset.", MessageType.Info);
                return;
            }

            EnsureGraphCollections();
            _inspectorScroll = GUILayout.BeginScrollView(
                _inspectorScroll,
                false,
                true,
                GUIStyle.none,
                GUI.skin.verticalScrollbar);
            try
            {
                bool hasMultiSelection = HasAnyMultiSelectionContext();
                if (hasMultiSelection == false)
                {
                    if (_selectedState != null)
                    {
                        DrawStateInspector(_selectedState);
                        return;
                    }

                    if (_selectedTransition != null)
                    {
                        DrawTransitionInspector(_selectedTransition);
                        return;
                    }

                    if (string.IsNullOrWhiteSpace(_selectedEntryLinkTargetStateId) == false)
                    {
                        DrawEntryTransitionInspector(_selectedEntryLinkTargetStateId);
                        return;
                    }

                    if (_selectedParameterIndex >= 0 && _selectedParameterIndex < _graph.Parameters.Count)
                    {
                        FusionAnimatorParameterDefinition parameter = _graph.Parameters[_selectedParameterIndex];
                        if (parameter != null)
                        {
                            DrawParameterInspector(parameter);
                            return;
                        }
                    }

                    if (_selectedBindingIndex >= 0 && _selectedBindingIndex < _graph.ClipBindings.Count)
                    {
                        FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[_selectedBindingIndex];
                        if (binding != null)
                        {
                            DrawClipBindingInspector(binding);
                            return;
                        }
                    }

                    if (_selectedLayerIndex >= 0 && _selectedLayerIndex < _graph.Layers.Count)
                    {
                        FusionAnimatorLayerDefinition layer = _graph.Layers[_selectedLayerIndex];
                        if (layer != null)
                        {
                            if (string.IsNullOrWhiteSpace(_selectedLayerScopePath) == false)
                            {
                                DrawSubStateScopeInspector(layer, _selectedLayerScopePath);
                                return;
                            }

                            DrawLayerInspector(layer);
                            return;
                        }
                    }

                    if (string.IsNullOrWhiteSpace(_activeLayerId) == false)
                    {
                        for (int layerIndex = 0; layerIndex < _graph.Layers.Count; ++layerIndex)
                        {
                            FusionAnimatorLayerDefinition activeLayer = _graph.Layers[layerIndex];
                            if (activeLayer != null && string.Equals(activeLayer.Id, _activeLayerId, StringComparison.Ordinal))
                            {
                                _selectedLayerIndex = layerIndex;
                                if (string.IsNullOrWhiteSpace(_activeScopePath) == false)
                                {
                                    DrawSubStateScopeInspector(activeLayer, _activeScopePath);
                                }
                                else
                                {
                                    DrawLayerInspector(activeLayer);
                                }

                                return;
                            }
                        }
                    }
                }

                EditorGUILayout.LabelField("Graph", EditorStyles.boldLabel);
                bool changed = false;
                bool undoRecorded = false;
                void EnsureUndo()
                {
                    if (undoRecorded)
                    {
                        return;
                    }

                    RecordUndo("Edit FusionAnimator Graph");
                    undoRecorded = true;
                }

                string displayName = EditorGUILayout.TextField(new GUIContent("Display Name", "Human-readable graph name shown in editor tooling."), _graph.DisplayName);
                if (displayName != _graph.DisplayName)
                {
                    EnsureUndo();
                    _graph.DisplayName = displayName;
                    changed = true;
                }

                string graphId = EditorGUILayout.TextField(new GUIContent("Graph Id", "Stable unique identifier for this graph asset."), _graph.GraphId);
                if (graphId != _graph.GraphId)
                {
                    EnsureUndo();
                    _graph.GraphId = graphId;
                    changed = true;
                }

                string entryStateId = EditorGUILayout.TextField(new GUIContent("Entry State Id", "State id used as default layer entry at start."), _graph.EntryStateId);
                if (entryStateId != _graph.EntryStateId)
                {
                    EnsureUndo();
                    _graph.EntryStateId = entryStateId;
                    changed = true;
                }

                bool applyRootMotion = EditorGUILayout.Toggle(new GUIContent("Apply Root Motion", "Global root motion enable/disable for this graph (authoring + preview setting)."), _graph.ApplyRootMotion);
                if (applyRootMotion != _graph.ApplyRootMotion)
                {
                    EnsureUndo();
                    _graph.ApplyRootMotion = applyRootMotion;
                    _graphView?.SetPreviewApplyRootMotion(applyRootMotion);
                    changed = true;
                }

                GUILayout.Space(10.0f);
                EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Parameters", _graph.Parameters != null ? _graph.Parameters.Count.ToString() : "0");
                EditorGUILayout.LabelField("Bindings", _graph.ClipBindings != null ? _graph.ClipBindings.Count.ToString() : "0");
                EditorGUILayout.LabelField("Layers", _graph.Layers != null ? _graph.Layers.Count.ToString() : "0");
                int visibleStateCount = 0;
                if (_graph.States != null)
                {
                    for (int stateIndex = 0; stateIndex < _graph.States.Count; ++stateIndex)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[stateIndex];
                        if (state != null && IsScopeSentinelStateName(state.Name) == false)
                        {
                            visibleStateCount++;
                        }
                    }
                }

                EditorGUILayout.LabelField("States", visibleStateCount.ToString());
                EditorGUILayout.LabelField("Transitions", _graph.Transitions != null ? _graph.Transitions.Count.ToString() : "0");

                if (changed)
                {
                    MarkGraphDirty();
                }
            }
            finally
            {
                GUILayout.EndScrollView();
            }
        }

        private void DrawSubStateScopeInspector(FusionAnimatorLayerDefinition layer, string scopePath)
        {
            string normalizedScopePath = NormalizeScopePath(scopePath);
            EditorGUILayout.LabelField("Sub-State Machine", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Layer", string.IsNullOrWhiteSpace(layer?.Name) ? layer?.Id : layer.Name);
            EditorGUILayout.LabelField("Scope", normalizedScopePath);

            string scopeLeafName = GetScopeLeafName(normalizedScopePath);
            if (_focusScopeRenameField)
            {
                GUI.SetNextControlName("FusionAnimatorScopeRenameField");
            }

            string renamedLeafName = EditorGUILayout.DelayedTextField(
                new GUIContent("Name", "Sub-state machine name. Renames this scope and all nested child scopes."),
                scopeLeafName);
            if (_focusScopeRenameField)
            {
                EditorGUI.FocusTextInControl("FusionAnimatorScopeRenameField");
                _focusScopeRenameField = false;
            }

            if (string.Equals(renamedLeafName, scopeLeafName, StringComparison.Ordinal) == false)
            {
                if (TryRenameSubStateMachineScope(
                    layer != null ? layer.Id : null,
                    normalizedScopePath,
                    renamedLeafName,
                    true,
                    out string renamedScopePath))
                {
                    normalizedScopePath = renamedScopePath;
                }
            }

            int directStates = 0;
            int nestedStates = 0;
            if (_graph?.States != null && layer != null)
            {
                for (int i = 0; i < _graph.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state == null || string.Equals(state.LayerId, layer.Id, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    if (IsScopeSentinelStateName(state.Name))
                    {
                        continue;
                    }

                    string stateScope = GetStateScopePathFromName(state.Name);
                    if (string.Equals(stateScope, normalizedScopePath, StringComparison.OrdinalIgnoreCase))
                    {
                        directStates++;
                    }
                    else if (stateScope.StartsWith(normalizedScopePath + "/", StringComparison.OrdinalIgnoreCase))
                    {
                        nestedStates++;
                    }
                }
            }

            EditorGUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Direct States", directStates.ToString());
            EditorGUILayout.LabelField("Nested States", nestedStates.ToString());
        }

        private void DrawEntryTransitionInspector(string targetStateId)
        {
            EditorGUILayout.LabelField("Entry Transition", EditorStyles.boldLabel);
            FusionAnimatorStateDefinition destinationState = null;
            if (_graph?.States != null && string.IsNullOrWhiteSpace(targetStateId) == false)
            {
                for (int i = 0; i < _graph.States.Count; ++i)
                {
                    FusionAnimatorStateDefinition state = _graph.States[i];
                    if (state != null &&
                        IsScopeSentinelStateName(state.Name) == false &&
                        string.Equals(state.Id, targetStateId, StringComparison.Ordinal))
                    {
                        destinationState = state;
                        break;
                    }
                }
            }

            EditorGUILayout.LabelField("From", "Entry");
            EditorGUILayout.LabelField("To", destinationState != null ? destinationState.Name : targetStateId);
            EditorGUILayout.LabelField("Scope", destinationState != null ? GetStateScopePathFromName(destinationState.Name) : string.Empty);
            EditorGUILayout.LabelField("Type", "Default scope entry");
            EditorGUILayout.HelpBox("This is the default-entry link for the current scope. Change default by dragging Entry to a different destination in the canvas.", MessageType.Info);
        }

        private void DrawParameterInspector(FusionAnimatorParameterDefinition parameter)
        {
            EditorGUILayout.LabelField("Parameter", EditorStyles.boldLabel);
            bool changed = false;
            bool undoRecorded = false;
            void EnsureUndo()
            {
                if (undoRecorded)
                {
                    return;
                }

                RecordUndo("Edit FusionAnimator Parameter");
                undoRecorded = true;
            }

            string id = EditorGUILayout.TextField(new GUIContent("Id", "Stable unique id for this parameter, used by conditions."), parameter.Id);
            if (id != parameter.Id)
            {
                EnsureUndo();
                parameter.Id = id;
                changed = true;
            }

            string name = EditorGUILayout.TextField(new GUIContent("Name", "Human-readable parameter name for tooling."), parameter.Name);
            if (name != parameter.Name)
            {
                EnsureUndo();
                parameter.Name = name;
                changed = true;
            }

            FusionAnimatorParameterType type = (FusionAnimatorParameterType)EditorGUILayout.EnumPopup(new GUIContent("Type", "Underlying value type used by this parameter."), parameter.Type);
            if (type != parameter.Type)
            {
                EnsureUndo();
                parameter.Type = type;
                changed = true;
            }

            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                case FusionAnimatorParameterType.Trigger:
                {
                    bool defaultBool = EditorGUILayout.Toggle(new GUIContent("Default Bool", "Default boolean value for this parameter."), parameter.DefaultBool);
                    if (defaultBool != parameter.DefaultBool)
                    {
                        EnsureUndo();
                        parameter.DefaultBool = defaultBool;
                        changed = true;
                    }

                    if (parameter.Type == FusionAnimatorParameterType.Bool)
                    {
                        bool invertBool = EditorGUILayout.Toggle(new GUIContent("Invert Input", "If enabled, preview gamepad input writes the inverse bool value for this parameter. Runtime evaluation semantics are unchanged."), parameter.Invert);
                        if (invertBool != parameter.Invert)
                        {
                            EnsureUndo();
                            parameter.Invert = invertBool;
                            changed = true;
                        }
                    }
                    break;
                }
                case FusionAnimatorParameterType.Int:
                {
                    int defaultInt = EditorGUILayout.IntField(new GUIContent("Default Int", "Default integer value for this parameter."), parameter.DefaultInt);
                    if (defaultInt != parameter.DefaultInt)
                    {
                        EnsureUndo();
                        parameter.DefaultInt = defaultInt;
                        changed = true;
                    }
                    break;
                }
                case FusionAnimatorParameterType.Float:
                {
                    float defaultFloat = EditorGUILayout.FloatField(new GUIContent("Default Float", "Default float value for this parameter."), parameter.DefaultFloat);
                    if (Mathf.Approximately(defaultFloat, parameter.DefaultFloat) == false)
                    {
                        EnsureUndo();
                        parameter.DefaultFloat = defaultFloat;
                        changed = true;
                    }
                    break;
                }
                case FusionAnimatorParameterType.Vector2:
                {
                    Vector2 defaultVector = EditorGUILayout.Vector2Field(new GUIContent("Default Vector2", "Default Vector2 value for this parameter."), parameter.DefaultVector2);
                    if (defaultVector != parameter.DefaultVector2)
                    {
                        EnsureUndo();
                        parameter.DefaultVector2 = defaultVector;
                        changed = true;
                    }
                    break;
                }
            }

            DrawParameterPreviewOverride(parameter);

            if (changed)
            {
                MarkGraphDirty();
            }
        }

        private void DrawClipBindingInspector(FusionAnimatorClipBindingDefinition binding)
        {
            EditorGUILayout.LabelField("Binding", EditorStyles.boldLabel);
            bool changed = false;
            bool undoRecorded = false;
            void EnsureUndo()
            {
                if (undoRecorded)
                {
                    return;
                }

                RecordUndo("Edit FusionAnimator Clip Binding");
                undoRecorded = true;
            }

            string id = EditorGUILayout.TextField(new GUIContent("Id", "Stable unique id referenced by clip slots in Binding mode."), binding.Id);
            if (id != binding.Id)
            {
                EnsureUndo();
                binding.Id = id;
                changed = true;
            }

            string name = EditorGUILayout.TextField(new GUIContent("Name", "Human-readable binding name used in pickers."), binding.Name);
            if (name != binding.Name)
            {
                EnsureUndo();
                binding.Name = name;
                changed = true;
            }

            if (binding.Conditions == null)
            {
                EnsureUndo();
                binding.Conditions = new List<FusionAnimatorConditionDefinition>();
                changed = true;
            }

            FusionAnimatorClipBindingSlot debugActiveSlot = string.IsNullOrWhiteSpace(binding.Id) == false
                ? FusionAnimatorClipBindingUtility.ResolveBindingClipSlot(_graph, binding.Id, EvaluateCondition, ResolvePreviewBindingClipIndexParameter)
                : null;
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField(
                    new GUIContent("Active Clip", "Read-only debug reference to the clip currently selected by this binding under active preview parameters."),
                    debugActiveSlot != null ? debugActiveSlot.Clip : null,
                    typeof(AnimationClip),
                    false);
            }

            string clipIndexParameterId = binding.ClipIndexParameterId;
            DrawIntParameterPicker(
                "Index Parameter",
                "Optional Int parameter used to select clip slot by index (clamped to slot count).",
                value =>
                {
                    EnsureUndo();
                    binding.ClipIndexParameterId = value;
                },
                binding.ClipIndexParameterId);
            if (clipIndexParameterId != binding.ClipIndexParameterId)
            {
                changed = true;
            }

            if (DrawConditionEditorList(
                "Binding Conditions",
                "Add a condition required for this binding to be eligible for clip selection.",
                binding.Conditions,
                EnsureUndo))
            {
                changed = true;
            }

            if (binding.Clips == null)
            {
                EnsureUndo();
                binding.Clips = new List<FusionAnimatorClipBindingSlot>();
                changed = true;
            }

            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Add Clip Slot", "Add a conditional clip option for this binding.")))
            {
                EnsureUndo();
                binding.Clips.Add(new FusionAnimatorClipBindingSlot());
                changed = true;
            }

            for (int i = 0; i < binding.Clips.Count; ++i)
            {
                FusionAnimatorClipBindingSlot option = binding.Clips[i];
                if (option == null)
                {
                    EnsureUndo();
                    option = new FusionAnimatorClipBindingSlot();
                    binding.Clips[i] = option;
                    changed = true;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.Format("Slot {0}", i), EditorStyles.miniBoldLabel);
                if (GUILayout.Button(new GUIContent("Remove", "Remove this binding clip slot."), GUILayout.Width(80.0f)))
                {
                    EnsureUndo();
                    binding.Clips.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    changed = true;
                    break;
                }

                EditorGUILayout.EndHorizontal();

                string slotKey = EditorGUILayout.TextField(new GUIContent("Key", "Human-readable key for this clip option."), option.Slot);
                if (slotKey != option.Slot)
                {
                    EnsureUndo();
                    option.Slot = slotKey;
                    changed = true;
                }

                AnimationClip optionClip = EditorGUILayout.ObjectField(new GUIContent("Clip", "AnimationClip used by this option."), option.Clip, typeof(AnimationClip), false) as AnimationClip;
                if (optionClip != option.Clip)
                {
                    EnsureUndo();
                    option.Clip = optionClip;
                    changed = true;
                }

                float speed = Mathf.Max(0.0f, EditorGUILayout.FloatField(new GUIContent("Speed", "Playback speed multiplier for this option."), option.Speed));
                if (Mathf.Approximately(speed, option.Speed) == false)
                {
                    EnsureUndo();
                    option.Speed = speed;
                    changed = true;
                }

                bool loop = EditorGUILayout.Toggle(new GUIContent("Loop", "Loop mode for this option."), option.Loop);
                if (loop != option.Loop)
                {
                    EnsureUndo();
                    option.Loop = loop;
                    changed = true;
                }

                if (option.Conditions == null)
                {
                    EnsureUndo();
                    option.Conditions = new List<FusionAnimatorConditionDefinition>();
                    changed = true;
                }

                if (DrawConditionEditorList(
                    "Conditions",
                    "Add a condition required for this clip option to be selected.",
                    option.Conditions,
                    EnsureUndo))
                {
                    changed = true;
                }

                EditorGUILayout.EndVertical();
            }

            if (changed)
            {
                _graphView?.RefreshNodeDisplay(_selectedState);
                MarkGraphDirty();
            }
        }

        private bool DrawConditionEditorList(
            string headerLabel,
            string addConditionTooltip,
            List<FusionAnimatorConditionDefinition> conditions,
            Action ensureUndo)
        {
            if (conditions == null)
            {
                return false;
            }

            bool changed = false;

            EditorGUILayout.Space(4.0f);
            EditorGUILayout.LabelField(headerLabel, EditorStyles.miniBoldLabel);
            if (GUILayout.Button(new GUIContent("Add Condition", addConditionTooltip)))
            {
                ensureUndo?.Invoke();
                conditions.Add(new FusionAnimatorConditionDefinition());
                changed = true;
            }

            for (int conditionIndex = 0; conditionIndex < conditions.Count; ++conditionIndex)
            {
                FusionAnimatorConditionDefinition condition = conditions[conditionIndex];
                if (condition == null)
                {
                    ensureUndo?.Invoke();
                    condition = new FusionAnimatorConditionDefinition();
                    conditions[conditionIndex] = condition;
                    changed = true;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.Format("Condition {0}", conditionIndex), EditorStyles.miniBoldLabel);
                if (GUILayout.Button(new GUIContent("Remove", "Remove this condition."), GUILayout.Width(80.0f)))
                {
                    ensureUndo?.Invoke();
                    conditions.RemoveAt(conditionIndex);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    changed = true;
                    break;
                }

                EditorGUILayout.EndHorizontal();

                string parameterId = condition.ParameterId;
                DrawParameterPicker("Parameter", "Parameter id evaluated by this condition.", value =>
                {
                    ensureUndo?.Invoke();
                    condition.ParameterId = value;
                }, condition.ParameterId);
                if (condition.ParameterId != parameterId)
                {
                    changed = true;
                }

                FusionAnimatorParameterDefinition conditionParameter = FindParameterById(condition.ParameterId);
                if (conditionParameter != null && conditionParameter.Type == FusionAnimatorParameterType.Trigger)
                {
                    if (condition.Operator != FusionAnimatorConditionOperator.IsTrue)
                    {
                        ensureUndo?.Invoke();
                        condition.Operator = FusionAnimatorConditionOperator.IsTrue;
                        changed = true;
                    }

                    EditorGUILayout.LabelField(
                        new GUIContent("Operator", "Trigger conditions are one-shot and evaluate only when the trigger fires."),
                        new GUIContent("Fired Once"));
                }
                else
                {
                    FusionAnimatorConditionOperator op = DrawConditionOperatorField(conditionParameter, condition.Operator);
                    if (op != condition.Operator)
                    {
                        ensureUndo?.Invoke();
                        condition.Operator = op;
                        changed = true;
                    }
                }

                bool supportsAbsolute = false;
                if (conditionParameter != null)
                {
                    switch (conditionParameter.Type)
                    {
                        case FusionAnimatorParameterType.Int:
                        case FusionAnimatorParameterType.Float:
                            supportsAbsolute = true;
                            break;
                        case FusionAnimatorParameterType.Vector2:
                            supportsAbsolute = condition.Operator != FusionAnimatorConditionOperator.IsTrue &&
                                               condition.Operator != FusionAnimatorConditionOperator.IsFalse;
                            break;
                    }
                }

                if (supportsAbsolute)
                {
                    bool useAbsolute = EditorGUILayout.Toggle(
                        new GUIContent("Absolute", "Apply absolute value to the sampled input before comparison."),
                        condition.UseAbsoluteValue);
                    if (useAbsolute != condition.UseAbsoluteValue)
                    {
                        ensureUndo?.Invoke();
                        condition.UseAbsoluteValue = useAbsolute;
                        changed = true;
                    }
                }
                else if (condition.UseAbsoluteValue)
                {
                    ensureUndo?.Invoke();
                    condition.UseAbsoluteValue = false;
                    changed = true;
                }

                if (conditionParameter != null)
                {
                    switch (conditionParameter.Type)
                    {
                        case FusionAnimatorParameterType.Bool:
                        {
                            if (condition.Operator == FusionAnimatorConditionOperator.Equal || condition.Operator == FusionAnimatorConditionOperator.NotEqual)
                            {
                                bool boolValue = EditorGUILayout.Toggle(new GUIContent("Bool Value", "Boolean comparison target value."), condition.BoolValue);
                                if (boolValue != condition.BoolValue)
                                {
                                    ensureUndo?.Invoke();
                                    condition.BoolValue = boolValue;
                                    changed = true;
                                }
                            }

                            break;
                        }
                        case FusionAnimatorParameterType.Trigger:
                        {
                            break;
                        }
                        case FusionAnimatorParameterType.Int:
                        {
                            int intValue = EditorGUILayout.IntField(new GUIContent("Int Value", "Integer comparison target value."), condition.IntValue);
                            if (intValue != condition.IntValue)
                            {
                                ensureUndo?.Invoke();
                                condition.IntValue = intValue;
                                changed = true;
                            }

                            break;
                        }
                        case FusionAnimatorParameterType.Float:
                        {
                            float floatValue = EditorGUILayout.FloatField(new GUIContent("Float Value", "Float comparison target value."), condition.FloatValue);
                            if (Mathf.Approximately(floatValue, condition.FloatValue) == false)
                            {
                                ensureUndo?.Invoke();
                                condition.FloatValue = floatValue;
                                changed = true;
                            }

                            break;
                        }
                        case FusionAnimatorParameterType.Vector2:
                        {
                            if (condition.Operator != FusionAnimatorConditionOperator.IsTrue && condition.Operator != FusionAnimatorConditionOperator.IsFalse)
                            {
                                FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out _, out FusionAnimatorParameterComponent component);
                                bool componentSelected = component != FusionAnimatorParameterComponent.None;
                                GUIContent valueLabel = componentSelected
                                    ? new GUIContent("Float Value", "Vector2 component comparison target value.")
                                    : new GUIContent("Magnitude Value", "Vector2 magnitude comparison target value.");
                                float magnitudeValue = EditorGUILayout.FloatField(valueLabel, condition.FloatValue);
                                if (Mathf.Approximately(magnitudeValue, condition.FloatValue) == false)
                                {
                                    ensureUndo?.Invoke();
                                    condition.FloatValue = magnitudeValue;
                                    changed = true;
                                }
                            }

                            break;
                        }
                    }
                }

                EditorGUILayout.EndVertical();
            }

            return changed;
        }

        private List<ParameterUsageLocation> BuildParameterUsageLocations(string parameterId)
        {
            List<ParameterUsageLocation> usages = new List<ParameterUsageLocation>(32);
            if (_graph == null || string.IsNullOrWhiteSpace(parameterId))
            {
                return usages;
            }

            bool ParameterReferenceMatches(string reference)
            {
                if (string.IsNullOrWhiteSpace(reference))
                {
                    return false;
                }

                bool hasRequested = FusionAnimatorParameterReferenceUtility.TryParse(parameterId, out string requestedBaseId, out _);
                bool hasReference = FusionAnimatorParameterReferenceUtility.TryParse(reference, out string referenceBaseId, out _);
                if (hasRequested && hasReference)
                {
                    return string.Equals(requestedBaseId, referenceBaseId, StringComparison.Ordinal);
                }

                return string.Equals(reference, parameterId, StringComparison.Ordinal);
            }

            if (_graph.Transitions != null)
            {
                for (int transitionIndex = 0; transitionIndex < _graph.Transitions.Count; ++transitionIndex)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[transitionIndex];
                    if (transition?.Conditions == null || transition.Conditions.Count == 0)
                    {
                        continue;
                    }

                    FusionAnimatorStateDefinition fromState = FindStateById(transition.FromStateId);
                    FusionAnimatorStateDefinition toState = FindStateById(transition.ToStateId);
                    string transitionLayerId = fromState != null ? fromState.LayerId : (toState != null ? toState.LayerId : string.Empty);
                    string transitionScope = ResolveTransitionUsageScopePath(fromState, toState);

                    for (int conditionIndex = 0; conditionIndex < transition.Conditions.Count; ++conditionIndex)
                    {
                        FusionAnimatorConditionDefinition condition = transition.Conditions[conditionIndex];
                        if (condition == null ||
                            ParameterReferenceMatches(condition.ParameterId) == false)
                        {
                            continue;
                        }

                        string fromName = ResolveTransitionEndpointUsageDisplay(transition.FromStateId);
                        string toName = ResolveTransitionEndpointUsageDisplay(transition.ToStateId);
                        string label = string.Format("{0} -> {1}", fromName, toName);
                        if (transition.Conditions.Count > 1)
                        {
                            label = string.Format("{0} (Condition {1})", label, conditionIndex + 1);
                        }

                        usages.Add(new ParameterUsageLocation
                        {
                            Label = label,
                            LayerId = transitionLayerId,
                            ScopePath = transitionScope,
                            TransitionId = transition.Id,
                        });
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

                    if (state.MotionType != FusionAnimatorMotionType.BlendTree || state.BlendTree == null)
                    {
                        continue;
                    }

                    string stateScopePath = NormalizeScopePath(GetStateScopePathFromName(state.Name));
                    string stateLabel = string.IsNullOrWhiteSpace(state.Name) ? ResolveStateLeafName(state.Name) : state.Name;

                    void AddBlendTreeUsage(string suffix)
                    {
                        usages.Add(new ParameterUsageLocation
                        {
                            Label = string.Format("{0} ({1})", stateLabel, suffix),
                            LayerId = state.LayerId,
                            ScopePath = stateScopePath,
                            StateId = state.Id,
                        });
                    }

                    if (ParameterReferenceMatches(state.BlendTree.ParameterXId))
                    {
                        AddBlendTreeUsage("BlendTree.X");
                    }

                    if (ParameterReferenceMatches(state.BlendTree.ParameterYId))
                    {
                        AddBlendTreeUsage("BlendTree.Y");
                    }

                    if (ParameterReferenceMatches(state.BlendTree.ParameterVector2Id))
                    {
                        AddBlendTreeUsage("BlendTree.Vector2");
                    }

                    if (ParameterReferenceMatches(state.BlendTree.PoseTimeParameterId))
                    {
                        AddBlendTreeUsage("BlendTree.PoseTime");
                    }

                    if (ParameterReferenceMatches(state.BlendTree.DirectBlendParameterId))
                    {
                        AddBlendTreeUsage("BlendTree.Direct");
                    }

                    if (state.BlendTree.Children != null)
                    {
                        for (int childIndex = 0; childIndex < state.BlendTree.Children.Count; ++childIndex)
                        {
                            FusionAnimatorBlendTreeChild child = state.BlendTree.Children[childIndex];
                            if (child == null ||
                                ParameterReferenceMatches(child.DirectParameterId) == false)
                            {
                                continue;
                            }

                            string childName = string.IsNullOrWhiteSpace(child.Name) ? string.Format("Child {0}", childIndex + 1) : child.Name;
                            usages.Add(new ParameterUsageLocation
                            {
                                Label = string.Format("{0} ({1})", stateLabel, childName),
                                LayerId = state.LayerId,
                                ScopePath = stateScopePath,
                                StateId = state.Id,
                            });
                        }
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

                    string bindingName = string.IsNullOrWhiteSpace(binding.Name) ? "Binding" : binding.Name;
                    if (ParameterReferenceMatches(binding.ClipIndexParameterId))
                    {
                        usages.Add(new ParameterUsageLocation
                        {
                            Label = string.Format("{0} (Binding.IndexParameter)", bindingName),
                            BindingId = binding.Id,
                        });
                    }

                    if (binding.Conditions != null)
                    {
                        for (int conditionIndex = 0; conditionIndex < binding.Conditions.Count; ++conditionIndex)
                        {
                            FusionAnimatorConditionDefinition condition = binding.Conditions[conditionIndex];
                            if (condition == null || ParameterReferenceMatches(condition.ParameterId) == false)
                            {
                                continue;
                            }

                            usages.Add(new ParameterUsageLocation
                            {
                                Label = string.Format("{0} (Binding Condition {1})", bindingName, conditionIndex + 1),
                                BindingId = binding.Id,
                            });
                        }
                    }

                    if (binding.Clips == null)
                    {
                        continue;
                    }

                    for (int slotIndex = 0; slotIndex < binding.Clips.Count; ++slotIndex)
                    {
                        FusionAnimatorClipBindingSlot slot = binding.Clips[slotIndex];
                        if (slot?.Conditions == null)
                        {
                            continue;
                        }

                        string slotName = string.IsNullOrWhiteSpace(slot.Slot) ? string.Format("Slot {0}", slotIndex + 1) : slot.Slot;
                        for (int conditionIndex = 0; conditionIndex < slot.Conditions.Count; ++conditionIndex)
                        {
                            FusionAnimatorConditionDefinition condition = slot.Conditions[conditionIndex];
                            if (condition == null || ParameterReferenceMatches(condition.ParameterId) == false)
                            {
                                continue;
                            }

                            usages.Add(new ParameterUsageLocation
                            {
                                Label = string.Format("{0}/{1} (Binding Condition {2})", bindingName, slotName, conditionIndex + 1),
                                BindingId = binding.Id,
                            });
                        }
                    }
                }
            }

            return usages
                .OrderBy(usage => usage.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private List<BindingUsageLocation> BuildBindingUsageLocations(string bindingId)
        {
            List<BindingUsageLocation> usages = new List<BindingUsageLocation>(16);
            if (_graph == null || string.IsNullOrWhiteSpace(bindingId) || _graph.States == null)
            {
                return usages;
            }

            for (int stateIndex = 0; stateIndex < _graph.States.Count; ++stateIndex)
            {
                FusionAnimatorStateDefinition state = _graph.States[stateIndex];
                if (state == null || IsScopeSentinelStateName(state.Name))
                {
                    continue;
                }

                string stateScopePath = NormalizeScopePath(GetStateScopePathFromName(state.Name));
                string layerName = GetLayerDisplayName(state.LayerId);
                string stateLabel = ResolveStateLeafName(state.Name);
                string prefix = string.IsNullOrWhiteSpace(stateScopePath)
                    ? string.Format("{0}/{1}", layerName, stateLabel)
                    : string.Format("{0}/{1}/{2}", layerName, stateScopePath, stateLabel);

                if (state.Clips != null)
                {
                    for (int slotIndex = 0; slotIndex < state.Clips.Count; ++slotIndex)
                    {
                        FusionAnimatorClipSlot slot = state.Clips[slotIndex];
                        if (slot == null ||
                            slot.ReferenceMode != FusionAnimatorClipReferenceMode.Binding ||
                            string.Equals(slot.BindingId, bindingId, StringComparison.Ordinal) == false)
                        {
                            continue;
                        }

                        string slotName = string.IsNullOrWhiteSpace(slot.Slot) ? string.Format("Slot {0}", slotIndex + 1) : slot.Slot;
                        usages.Add(new BindingUsageLocation
                        {
                            Label = string.Format("{0} (Clip:{1})", prefix, slotName),
                            LayerId = state.LayerId,
                            ScopePath = stateScopePath,
                            StateId = state.Id,
                        });
                    }
                }

                if (state.BlendTree != null && state.BlendTree.Children != null)
                {
                    for (int childIndex = 0; childIndex < state.BlendTree.Children.Count; ++childIndex)
                    {
                        FusionAnimatorBlendTreeChild child = state.BlendTree.Children[childIndex];
                        if (child == null ||
                            child.ReferenceMode != FusionAnimatorClipReferenceMode.Binding ||
                            string.Equals(child.BindingId, bindingId, StringComparison.Ordinal) == false)
                        {
                            continue;
                        }

                        string childName = string.IsNullOrWhiteSpace(child.Name) ? string.Format("Child {0}", childIndex + 1) : child.Name;
                        usages.Add(new BindingUsageLocation
                        {
                            Label = string.Format("{0} (BlendTree:{1})", prefix, childName),
                            LayerId = state.LayerId,
                            ScopePath = stateScopePath,
                            StateId = state.Id,
                        });
                    }
                }
            }

            return usages
                .OrderBy(usage => usage.Label, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static string ResolveTransitionUsageScopePath(FusionAnimatorStateDefinition fromState, FusionAnimatorStateDefinition toState)
        {
            string ResolveStateScope(FusionAnimatorStateDefinition state)
            {
                if (state == null)
                {
                    return string.Empty;
                }

                string stateScope = NormalizeScopePath(GetStateScopePathFromName(state.Name));
                if (IsScopeSentinelStateName(state.Name))
                {
                    stateScope = NormalizeScopePath(GetStateScopePathFromName(stateScope));
                }

                return stateScope;
            }

            string fromScope = ResolveStateScope(fromState);
            if (string.IsNullOrWhiteSpace(fromScope) == false)
            {
                return fromScope;
            }

            string toScope = ResolveStateScope(toState);
            return string.IsNullOrWhiteSpace(toScope) ? string.Empty : toScope;
        }

        private string ResolveTransitionEndpointUsageDisplay(string endpointId)
        {
            if (string.IsNullOrWhiteSpace(endpointId))
            {
                return "<none>";
            }

            if (string.Equals(endpointId, FusionAnimatorGraphAsset.SpecialNodeEntryId, StringComparison.Ordinal))
            {
                return "Entry";
            }

            if (string.Equals(endpointId, FusionAnimatorGraphAsset.SpecialNodeAnyId, StringComparison.Ordinal))
            {
                return "Any State";
            }

            if (string.Equals(endpointId, FusionAnimatorGraphAsset.SpecialNodeExitId, StringComparison.Ordinal))
            {
                return "Exit";
            }

            FusionAnimatorStateDefinition state = FindStateById(endpointId);
            if (state == null)
            {
                return endpointId;
            }

            if (IsScopeSentinelStateName(state.Name))
            {
                string scopePath = NormalizeScopePath(GetStateScopePathFromName(state.Name));
                string scopeLeaf = GetScopeLeafName(scopePath);
                return string.IsNullOrWhiteSpace(scopeLeaf) ? "Sub-State" : scopeLeaf;
            }

            return ResolveStateLeafName(state.Name);
        }

        private void JumpToParameterUsage(ParameterUsageLocation usage)
        {
            if (usage == null || _graphView == null)
            {
                return;
            }

            ClearLibraryMultiSelectionState();
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedEntryLinkTargetStateId = string.Empty;
            _selectedLayerScopePath = string.Empty;
            string usageLayerId = string.IsNullOrWhiteSpace(usage.LayerId) ? string.Empty : usage.LayerId;
            string usageScopePath = NormalizeScopePath(usage.ScopePath);

            if (string.IsNullOrWhiteSpace(usage.TransitionId) == false)
            {
                FusionAnimatorTransitionDefinition transition = FindTransitionById(usage.TransitionId);
                if (transition != null && TrySelectTransitionUsage(transition, usageLayerId, usageScopePath))
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(usage.StateId) == false)
            {
                FusionAnimatorStateDefinition state = FindStateById(usage.StateId);
                if (state != null && TrySelectStateUsage(state, usageLayerId, usageScopePath))
                {
                    return;
                }
            }

            if (string.IsNullOrWhiteSpace(usage.BindingId) == false && _graph?.ClipBindings != null)
            {
                for (int bindingIndex = 0; bindingIndex < _graph.ClipBindings.Count; ++bindingIndex)
                {
                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[bindingIndex];
                    if (binding == null || string.Equals(binding.Id, usage.BindingId, StringComparison.Ordinal) == false)
                    {
                        continue;
                    }

                    _selectedBindingIndex = bindingIndex;
                    _selectedBindingIndices.Clear();
                    _selectedBindingIndices.Add(bindingIndex);
                    _bindingSelectionAnchorIndex = bindingIndex;
                    _selectedParameterIndex = -1;
                    _selectedLayerIndex = -1;
                    _selectedState = null;
                    _selectedTransition = null;
                    _selectedLayerScopePath = string.Empty;
                    _selectedEntryLinkTargetStateId = string.Empty;
                    _leftLibraryTab = LeftLibraryTab.Bindings;
                    ClearGraphSelectionForLibraryInteraction();
                    _graphView.SetHoveredLayer(null);
                    _graphView.SetHoveredParameter(null);
                    _leftPanel?.MarkDirtyRepaint();
                    _inspector?.MarkDirtyRepaint();
                    return;
                }
            }

            // Fallback context selection if the target no longer exists.
            SetActiveLayer(usageLayerId);
            SetScopePath(usageScopePath);

            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private void JumpToBindingUsage(BindingUsageLocation usage)
        {
            if (usage == null || _graphView == null)
            {
                return;
            }

            ClearLibraryMultiSelectionState();
            _selectedParameterIndex = -1;
            _selectedBindingIndex = -1;
            _selectedEntryLinkTargetStateId = string.Empty;
            _selectedLayerScopePath = string.Empty;
            string usageLayerId = string.IsNullOrWhiteSpace(usage.LayerId) ? string.Empty : usage.LayerId;
            string usageScopePath = NormalizeScopePath(usage.ScopePath);

            if (string.IsNullOrWhiteSpace(usage.StateId) == false)
            {
                FusionAnimatorStateDefinition state = FindStateById(usage.StateId);
                if (state != null && TrySelectStateUsage(state, usageLayerId, usageScopePath))
                {
                    return;
                }
            }

            SetActiveLayer(usageLayerId);
            SetScopePath(usageScopePath);
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private bool TrySelectTransitionUsage(FusionAnimatorTransitionDefinition transition, string usageLayerId, string usageScopePath)
        {
            if (transition == null || _graphView == null)
            {
                return false;
            }

            FusionAnimatorStateDefinition fromState = FindStateById(transition.FromStateId);
            FusionAnimatorStateDefinition toState = FindStateById(transition.ToStateId);
            string transitionLayerId = string.IsNullOrWhiteSpace(usageLayerId) == false
                ? usageLayerId
                : (fromState != null ? fromState.LayerId : (toState != null ? toState.LayerId : string.Empty));

            List<string> candidateScopes = BuildTransitionUsageCandidateScopes(
                usageScopePath,
                ResolveTransitionUsageScopePath(fromState, toState),
                ResolveTransitionEndpointScopePath(fromState),
                ResolveTransitionEndpointScopePath(toState),
                transitionLayerId);

            for (int scopeIndex = 0; scopeIndex < candidateScopes.Count; ++scopeIndex)
            {
                string candidateScope = candidateScopes[scopeIndex];
                SetActiveLayer(transitionLayerId);
                SetScopePath(candidateScope);

                if (_graphView.SelectTransitionById(transition.Id, true))
                {
                    OnGraphSelectionChanged(null, transition);
                    return true;
                }
            }

            return false;
        }

        private bool TrySelectStateUsage(FusionAnimatorStateDefinition state, string usageLayerId, string usageScopePath)
        {
            if (state == null || _graphView == null)
            {
                return false;
            }

            string stateLayerId = string.IsNullOrWhiteSpace(usageLayerId) == false ? usageLayerId : (state.LayerId ?? string.Empty);
            string stateScope = NormalizeScopePath(GetStateScopePathFromName(state.Name));
            List<string> candidateScopes = BuildStateUsageCandidateScopes(usageScopePath, stateScope, stateLayerId);

            for (int scopeIndex = 0; scopeIndex < candidateScopes.Count; ++scopeIndex)
            {
                string candidateScope = candidateScopes[scopeIndex];
                SetActiveLayer(stateLayerId);
                SetScopePath(candidateScope);

                if (_graphView.SelectStateById(state.Id, true))
                {
                    OnGraphSelectionChanged(state, null);
                    return true;
                }
            }

            return false;
        }

        private List<string> BuildTransitionUsageCandidateScopes(
            string usageScopePath,
            string resolvedScopePath,
            string fromScopePath,
            string toScopePath,
            string layerId)
        {
            List<string> candidates = new List<string>(12);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddScopeAndAncestors(usageScopePath, candidates, seen);
            AddScopeAndAncestors(resolvedScopePath, candidates, seen);
            AddScopeAndAncestors(fromScopePath, candidates, seen);
            AddScopeAndAncestors(toScopePath, candidates, seen);
            AppendLayerScopeFallbacks(layerId, candidates, seen);
            AddScopeCandidate(string.Empty, candidates, seen);

            return candidates;
        }

        private List<string> BuildStateUsageCandidateScopes(string usageScopePath, string stateScopePath, string layerId)
        {
            List<string> candidates = new List<string>(8);
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            AddScopeAndAncestors(usageScopePath, candidates, seen);
            AddScopeAndAncestors(stateScopePath, candidates, seen);
            AppendLayerScopeFallbacks(layerId, candidates, seen);
            AddScopeCandidate(string.Empty, candidates, seen);

            return candidates;
        }

        private void AppendLayerScopeFallbacks(string layerId, List<string> candidates, HashSet<string> seen)
        {
            if (string.IsNullOrWhiteSpace(layerId))
            {
                return;
            }

            List<string> availableScopes = GetAvailableScopes(layerId);
            if (availableScopes == null || availableScopes.Count == 0)
            {
                return;
            }

            availableScopes.Sort((lhs, rhs) =>
            {
                int rhsDepth = GetScopeDepth(rhs);
                int lhsDepth = GetScopeDepth(lhs);
                int depthCompare = rhsDepth.CompareTo(lhsDepth);
                return depthCompare != 0 ? depthCompare : string.Compare(lhs, rhs, StringComparison.OrdinalIgnoreCase);
            });

            for (int i = 0; i < availableScopes.Count; ++i)
            {
                AddScopeCandidate(availableScopes[i], candidates, seen);
            }
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
                    ++depth;
                }
            }

            return depth;
        }

        private static string ResolveTransitionEndpointScopePath(FusionAnimatorStateDefinition state)
        {
            if (state == null)
            {
                return string.Empty;
            }

            string stateScope = NormalizeScopePath(GetStateScopePathFromName(state.Name));
            if (IsScopeSentinelStateName(state.Name))
            {
                stateScope = NormalizeScopePath(GetStateScopePathFromName(stateScope));
            }

            return stateScope;
        }

        private static void AddScopeAndAncestors(string scopePath, List<string> candidates, HashSet<string> seen)
        {
            string current = NormalizeScopePath(scopePath);
            while (true)
            {
                AddScopeCandidate(current, candidates, seen);
                if (string.IsNullOrWhiteSpace(current))
                {
                    break;
                }

                current = NormalizeScopePath(GetStateScopePathFromName(current));
            }
        }

        private static void AddScopeCandidate(string scopePath, List<string> candidates, HashSet<string> seen)
        {
            if (candidates == null || seen == null)
            {
                return;
            }

            string normalized = NormalizeScopePath(scopePath);
            if (seen.Add(normalized ?? string.Empty))
            {
                candidates.Add(normalized ?? string.Empty);
            }
        }

        private static string ResolveStateLeafName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return "State";
            }

            int separator = stateName.LastIndexOf('/');
            string leaf = separator >= 0 ? stateName.Substring(separator + 1) : stateName;
            return string.IsNullOrWhiteSpace(leaf) ? "State" : leaf;
        }

        private string BuildUniqueStateNameForScope(string layerId, string scopePath, string desiredLeafName, string ignoreStateId)
        {
            string normalizedScope = NormalizeScopePath(scopePath);
            string baseLeaf = SanitizeStateLeafName(desiredLeafName);
            int suffix = 1;
            while (suffix < 1000)
            {
                string candidateLeaf = suffix == 1 ? baseLeaf : string.Format("{0} {1}", baseLeaf, suffix);
                string candidateName = string.IsNullOrWhiteSpace(normalizedScope)
                    ? candidateLeaf
                    : string.Format("{0}/{1}", normalizedScope, candidateLeaf);

                bool exists = false;
                if (_graph?.States != null)
                {
                    for (int i = 0; i < _graph.States.Count; ++i)
                    {
                        FusionAnimatorStateDefinition candidate = _graph.States[i];
                        if (candidate == null || string.Equals(candidate.Id, ignoreStateId, StringComparison.Ordinal))
                        {
                            continue;
                        }

                        if (string.Equals(candidate.LayerId, layerId, StringComparison.Ordinal) == false)
                        {
                            continue;
                        }

                        if (string.Equals(candidate.Name, candidateName, StringComparison.OrdinalIgnoreCase))
                        {
                            exists = true;
                            break;
                        }
                    }
                }

                if (exists == false)
                {
                    return candidateName;
                }

                ++suffix;
            }

            return string.IsNullOrWhiteSpace(normalizedScope) ? baseLeaf : string.Format("{0}/{1}", normalizedScope, baseLeaf);
        }

        private static string SanitizeStateLeafName(string leafName)
        {
            string sanitized = string.IsNullOrWhiteSpace(leafName) ? "State" : leafName.Trim();
            sanitized = sanitized.Replace("/", "_").Replace("\\", "_");
            return string.IsNullOrWhiteSpace(sanitized) ? "State" : sanitized;
        }

        private void DrawLayerInspector(FusionAnimatorLayerDefinition layer)
        {
            EditorGUILayout.LabelField("Layer", EditorStyles.boldLabel);
            bool changed = false;
            bool undoRecorded = false;
            void EnsureUndo()
            {
                if (undoRecorded)
                {
                    return;
                }

                RecordUndo("Edit FusionAnimator Layer");
                undoRecorded = true;
            }

            string id = EditorGUILayout.TextField(new GUIContent("Id", "Stable unique id used to assign states to this layer."), layer.Id);
            if (id != layer.Id)
            {
                EnsureUndo();
                layer.Id = id;
                changed = true;
            }

            string name = EditorGUILayout.TextField(new GUIContent("Name", "Human-readable layer name."), layer.Name);
            if (name != layer.Name)
            {
                EnsureUndo();
                layer.Name = name;
                changed = true;
            }

            int priority = EditorGUILayout.IntField(new GUIContent("Priority", "Higher priority layers can evaluate before lower priority layers."), layer.Priority);
            if (priority != layer.Priority)
            {
                EnsureUndo();
                layer.Priority = priority;
                changed = true;
            }

            float defaultWeight = EditorGUILayout.Slider(new GUIContent("Default Weight", "Default layer blend weight at activation time."), layer.DefaultWeight, 0.0f, 1.0f);
            if (Mathf.Approximately(defaultWeight, layer.DefaultWeight) == false)
            {
                EnsureUndo();
                layer.DefaultWeight = defaultWeight;
                changed = true;
            }

            bool enabledByDefault = EditorGUILayout.Toggle(new GUIContent("Enabled By Default", "If true, this layer starts active without explicit runtime enable."), layer.EnabledByDefault);
            if (enabledByDefault != layer.EnabledByDefault)
            {
                EnsureUndo();
                layer.EnabledByDefault = enabledByDefault;
                changed = true;
            }

            FusionAnimatorLayerBlendMode blendMode = (FusionAnimatorLayerBlendMode)EditorGUILayout.EnumPopup(new GUIContent("Blend Mode", "How this layer combines with prior layers."), layer.BlendMode);
            if (blendMode != layer.BlendMode)
            {
                EnsureUndo();
                layer.BlendMode = blendMode;
                changed = true;
            }

            AvatarMask avatarMask = EditorGUILayout.ObjectField(new GUIContent("Avatar Mask", "Optional Unity AvatarMask that limits bone influence for this layer."), layer.AvatarMask, typeof(AvatarMask), false) as AvatarMask;
            if (avatarMask != layer.AvatarMask)
            {
                EnsureUndo();
                layer.AvatarMask = avatarMask;
                changed = true;
            }

            int defaultLayerIndex = _graph.Layers != null ? _graph.Layers.IndexOf(layer) : -1;
            if (defaultLayerIndex == 0 && layer.AvatarMask != null)
            {
                EditorGUILayout.HelpBox("Character orientation may be erroneous for humanoid motion if there is an AvatarMask on the default layer. Consider not using a mask for the default layer.", MessageType.Warning);
            }

            int syncedLayerIndex = EditorGUILayout.IntField(new GUIContent("Synced Layer Index", "Unity Animator synced-layer index. -1 means this layer is not synced."), layer.SyncedLayerIndex);
            if (syncedLayerIndex != layer.SyncedLayerIndex)
            {
                EnsureUndo();
                layer.SyncedLayerIndex = syncedLayerIndex;
                changed = true;
            }

            bool syncTiming = EditorGUILayout.Toggle(new GUIContent("Sync Timing", "If true, synced timing affects this layer as in Unity Animator layer settings."), layer.SyncTiming);
            if (syncTiming != layer.SyncTiming)
            {
                EnsureUndo();
                layer.SyncTiming = syncTiming;
                changed = true;
            }

            bool ikPass = EditorGUILayout.Toggle(new GUIContent("IK Pass", "If true, layer runs with IK pass enabled (parity field with Unity Animator layer setting)."), layer.IKPass);
            if (ikPass != layer.IKPass)
            {
                EnsureUndo();
                layer.IKPass = ikPass;
                changed = true;
            }

            if (changed)
            {
                _graphView?.RebuildFromGraphData();
                MarkGraphDirty();
            }
        }

        private void DrawStateInspector(FusionAnimatorStateDefinition state)
        {
            EditorGUILayout.LabelField("State", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Id", state.Id);
            bool changed = false;
            bool rebuildGraph = false;
            bool undoRecorded = false;
            void EnsureUndo()
            {
                if (undoRecorded)
                {
                    return;
                }

                RecordUndo("Edit FusionAnimator State");
                undoRecorded = true;
            }

            string stateScopePath = NormalizeScopePath(GetStateScopePathFromName(state.Name));
            string stateLeafName = ResolveStateLeafName(state.Name);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.TextField(
                    new GUIContent("Scope", "Scope path is managed by sub-state machine context and is not edited from the state inspector."),
                    string.IsNullOrWhiteSpace(stateScopePath) ? "<root>" : stateScopePath);
            }

            string editedLeafName = EditorGUILayout.DelayedTextField(new GUIContent("Name", "Leaf state name within the current scope."), stateLeafName);
            if (editedLeafName != stateLeafName)
            {
                string sanitizedLeafName = SanitizeStateLeafName(editedLeafName);
                string uniqueScopedName = BuildUniqueStateNameForScope(state.LayerId, stateScopePath, sanitizedLeafName, state.Id);
                EnsureUndo();
                state.Name = uniqueScopedName;
                changed = true;
            }

            string layerId = state.LayerId;
            if (string.IsNullOrWhiteSpace(_activeLayerId))
            {
                DrawLayerPicker("Layer", "Layer id that this state belongs to.", value =>
                {
                    EnsureUndo();
                    state.LayerId = value;
                }, state.LayerId);
                if (state.LayerId != layerId)
                {
                    changed = true;
                    rebuildGraph = true;
                }
            }
            else
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(new GUIContent("Layer", "Layer id inherited from current scoped layer."), GetLayerDisplayName(_activeLayerId));
                }

                if (string.Equals(state.LayerId, _activeLayerId, StringComparison.Ordinal) == false)
                {
                    EnsureUndo();
                    state.LayerId = _activeLayerId;
                    changed = true;
                    rebuildGraph = true;
                }
            }

            float minDuration = Mathf.Max(0.0f, EditorGUILayout.FloatField(new GUIContent("Min Duration", "Minimum time in seconds the state must remain active before eligible transitions can exit."), state.MinDurationSeconds));
            if (Mathf.Approximately(minDuration, state.MinDurationSeconds) == false)
            {
                EnsureUndo();
                state.MinDurationSeconds = minDuration;
                changed = true;
            }

            bool canTransitionOut = EditorGUILayout.Toggle(new GUIContent("Can Transition Out", "If false, outgoing transition port is removed and this state cannot initiate transitions."), state.CanTransitionOut);
            if (canTransitionOut != state.CanTransitionOut)
            {
                EnsureUndo();
                state.CanTransitionOut = canTransitionOut;
                changed = true;
                rebuildGraph = true;
            }

            bool writeDefaults = EditorGUILayout.Toggle(new GUIContent("Write Defaults", "Reserved compatibility flag for state output write behavior."), state.WriteDefaults);
            if (writeDefaults != state.WriteDefaults)
            {
                EnsureUndo();
                state.WriteDefaults = writeDefaults;
                changed = true;
            }

            FusionAnimatorMotionType motionType = (FusionAnimatorMotionType)EditorGUILayout.EnumPopup(new GUIContent("Motion Type", "Clip uses discrete motion slots, BlendTree uses multi-child parameterized blending."), state.MotionType);
            if (motionType != state.MotionType)
            {
                EnsureUndo();
                state.MotionType = motionType;
                changed = true;
            }

            Vector2 nodePosition = EditorGUILayout.Vector2Field(new GUIContent("Node Position", "Canvas position used for this state node."), state.NodePosition);
            if (nodePosition != state.NodePosition)
            {
                EnsureUndo();
                state.NodePosition = nodePosition;
                changed = true;
                rebuildGraph = true;
            }

            GUILayout.Space(8.0f);
            if (state.MotionType == FusionAnimatorMotionType.BlendTree)
            {
                changed |= DrawBlendTreeInspector(state);
            }
            else
            {
                EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
                if (GUILayout.Button(new GUIContent("Add Clip Slot", "Add an animation clip binding entry for this state.")))
                {
                    EnsureUndo();
                    state.Clips.Add(new FusionAnimatorClipSlot());
                    changed = true;
                }

                for (int i = 0; i < state.Clips.Count; ++i)
                {
                    FusionAnimatorClipSlot slot = state.Clips[i];
                    if (slot == null)
                    {
                        continue;
                    }

                    EditorGUILayout.BeginVertical("box");
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField(string.Format("Slot {0}", i), EditorStyles.miniBoldLabel);
                    if (GUILayout.Button(new GUIContent("Remove", "Remove this clip slot entry."), GUILayout.Width(80.0f)))
                    {
                        EnsureUndo();
                        state.Clips.RemoveAt(i);
                        EditorGUILayout.EndHorizontal();
                        EditorGUILayout.EndVertical();
                        changed = true;
                        break;
                    }

                    EditorGUILayout.EndHorizontal();
                    string slotKey = EditorGUILayout.TextField(new GUIContent("Key", "Logical clip key within this state (for runtime lookup/profiles)."), slot.Slot);
                    if (slotKey != slot.Slot)
                    {
                        EnsureUndo();
                        slot.Slot = slotKey;
                        changed = true;
                    }

                    DrawClipBindingPicker(new GUIContent("Binding", "Choose Direct to use this slot's clip field, or select a reusable graph binding."), slot, EnsureUndo, () => changed = true);

                    AnimationClip displayedClip = slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct
                        ? slot.Clip
                        : FusionAnimatorClipBindingUtility.ResolveClip(_graph, slot, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                    using (new EditorGUI.DisabledScope(slot.ReferenceMode != FusionAnimatorClipReferenceMode.Direct))
                    {
                        AnimationClip clip = EditorGUILayout.ObjectField(new GUIContent("Clip", "Direct AnimationClip for this slot (used when Binding = Direct)."), displayedClip, typeof(AnimationClip), false) as AnimationClip;
                        if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct && clip != slot.Clip)
                        {
                            EnsureUndo();
                            slot.Clip = clip;
                            changed = true;
                        }
                    }

                    float displayedSpeed = slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct
                        ? slot.Speed
                        : FusionAnimatorClipBindingUtility.ResolveSpeed(_graph, slot, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                    using (new EditorGUI.DisabledScope(slot.ReferenceMode != FusionAnimatorClipReferenceMode.Direct))
                    {
                        float speed = EditorGUILayout.FloatField(new GUIContent("Speed", "Playback speed multiplier for this clip slot."), displayedSpeed);
                        if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct && Mathf.Approximately(speed, slot.Speed) == false)
                        {
                            EnsureUndo();
                            slot.Speed = speed;
                            changed = true;
                        }
                    }

                    bool displayedLoop = slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct
                        ? slot.Loop
                        : FusionAnimatorClipBindingUtility.ResolveLoop(_graph, slot, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                    using (new EditorGUI.DisabledScope(slot.ReferenceMode != FusionAnimatorClipReferenceMode.Direct))
                    {
                        bool loop = EditorGUILayout.Toggle(new GUIContent("Loop", "If true, this clip is treated as looping."), displayedLoop);
                        if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct && loop != slot.Loop)
                        {
                            EnsureUndo();
                            slot.Loop = loop;
                            changed = true;
                        }
                    }
                    EditorGUILayout.EndVertical();
                }
            }

            if (changed)
            {
                string selectedStateId = state.Id;
                if (rebuildGraph)
                {
                    _graphView?.RebuildFromGraphData();
                    if (string.IsNullOrWhiteSpace(selectedStateId) == false)
                    {
                        _graphView?.SelectStateById(selectedStateId);
                    }
                }
                else
                {
                    _graphView?.RefreshNodeDisplay(state);
                }

                MarkGraphDirty();
            }
        }

        private void DrawTransitionInspector(FusionAnimatorTransitionDefinition transition)
        {
            EditorGUILayout.LabelField("Transition", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Id", transition.Id);
            bool changed = false;
            bool rebuildGraph = false;
            bool undoRecorded = false;
            void EnsureUndo()
            {
                if (undoRecorded)
                {
                    return;
                }

                RecordUndo("Edit FusionAnimator Transition");
                undoRecorded = true;
            }

            string name = EditorGUILayout.TextField(new GUIContent("Name", "Human-readable transition name."), transition.Name);
            if (name != transition.Name)
            {
                EnsureUndo();
                transition.Name = name;
                changed = true;
            }

            string fromStateId = transition.FromStateId;
            DrawStatePicker("From State", "Source state id for this transition.", value =>
            {
                EnsureUndo();
                transition.FromStateId = value;
            }, transition.FromStateId);
            if (transition.FromStateId != fromStateId)
            {
                changed = true;
                rebuildGraph = true;
            }

            string toStateId = transition.ToStateId;
            DrawStatePicker("To State", "Destination state id for this transition.", value =>
            {
                EnsureUndo();
                transition.ToStateId = value;
            }, transition.ToStateId);
            if (transition.ToStateId != toStateId)
            {
                changed = true;
                rebuildGraph = true;
            }

            int priority = EditorGUILayout.IntField(new GUIContent("Priority", "Transition evaluation priority (lower first unless runtime policy overrides)."), transition.Priority);
            if (priority != transition.Priority)
            {
                EnsureUndo();
                transition.Priority = priority;
                changed = true;
            }

            bool mute = EditorGUILayout.Toggle(new GUIContent("Mute", "When enabled, this transition is ignored during evaluation."), transition.Mute);
            if (mute != transition.Mute)
            {
                EnsureUndo();
                transition.Mute = mute;
                changed = true;
            }

            bool solo = EditorGUILayout.Toggle(new GUIContent("Solo", "If any outgoing transition is Solo, only Solo transitions are considered."), transition.Solo);
            if (solo != transition.Solo)
            {
                EnsureUndo();
                transition.Solo = solo;
                changed = true;
            }

            bool hasExitTime = EditorGUILayout.Toggle(new GUIContent("Has Exit Time", "Require source state normalized time to reach Exit Time before transition can fire."), transition.HasExitTime);
            if (hasExitTime != transition.HasExitTime)
            {
                EnsureUndo();
                transition.HasExitTime = hasExitTime;
                changed = true;
            }

            float exitTimeNormalized = EditorGUILayout.FloatField(new GUIContent("Exit Time", "Normalized source-state time threshold. 1.0 means one full cycle."), transition.ExitTimeNormalized);
            if (Mathf.Approximately(exitTimeNormalized, transition.ExitTimeNormalized) == false)
            {
                EnsureUndo();
                transition.ExitTimeNormalized = exitTimeNormalized;
                changed = true;
            }

            float offsetNormalized = EditorGUILayout.Slider(new GUIContent("Transition Offset", "Normalized destination-state start offset in [0..1]."), transition.StartOffsetNormalized, 0.0f, 1.0f);
            if (Mathf.Approximately(offsetNormalized, transition.StartOffsetNormalized) == false)
            {
                EnsureUndo();
                transition.StartOffsetNormalized = offsetNormalized;
                changed = true;
            }

            bool fixedDuration = EditorGUILayout.Toggle(new GUIContent("Fixed Duration", "If enabled, Blend Duration is seconds. If disabled, it is normalized source-state duration."), transition.FixedDuration);
            if (fixedDuration != transition.FixedDuration)
            {
                EnsureUndo();
                transition.FixedDuration = fixedDuration;
                changed = true;
            }

            float blendDuration = Mathf.Max(0.0f, EditorGUILayout.FloatField(new GUIContent("Blend Duration", "Blend time in seconds when entering destination state."), transition.BlendDurationSeconds));
            if (Mathf.Approximately(blendDuration, transition.BlendDurationSeconds) == false)
            {
                EnsureUndo();
                transition.BlendDurationSeconds = blendDuration;
                changed = true;
            }

            bool canInterrupt = EditorGUILayout.Toggle(new GUIContent("Can Interrupt", "Whether this transition can be interrupted by higher-priority candidates."), transition.CanInterrupt);
            if (canInterrupt != transition.CanInterrupt)
            {
                EnsureUndo();
                transition.CanInterrupt = canInterrupt;
                changed = true;
            }

            FusionAnimatorInterruptionSource interruptionSource = (FusionAnimatorInterruptionSource)EditorGUILayout.EnumPopup(new GUIContent("Interruption Source", "Scope used when evaluating interruption candidates."), transition.InterruptionSource);
            if (interruptionSource != transition.InterruptionSource)
            {
                EnsureUndo();
                transition.InterruptionSource = interruptionSource;
                changed = true;
            }

            GUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Conditions", EditorStyles.boldLabel);
            if (GUILayout.Button(new GUIContent("Add Condition", "Add a condition required for this transition to become eligible.")))
            {
                EnsureUndo();
                transition.Conditions.Add(new FusionAnimatorConditionDefinition());
                changed = true;
            }

            for (int i = 0; i < transition.Conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = transition.Conditions[i];
                if (condition == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.Format("Condition {0}", i), EditorStyles.miniBoldLabel);
                if (GUILayout.Button(new GUIContent("Remove", "Remove this transition condition."), GUILayout.Width(80.0f)))
                {
                    EnsureUndo();
                    transition.Conditions.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    changed = true;
                    break;
                }

                EditorGUILayout.EndHorizontal();
                string parameterId = condition.ParameterId;
                DrawParameterPicker("Parameter", "Parameter id evaluated by this condition.", value =>
                {
                    EnsureUndo();
                    condition.ParameterId = value;
                }, condition.ParameterId);
                if (condition.ParameterId != parameterId)
                {
                    changed = true;
                }

                FusionAnimatorParameterDefinition conditionParameter = FindParameterById(condition.ParameterId);
                if (conditionParameter != null && conditionParameter.Type == FusionAnimatorParameterType.Trigger)
                {
                    if (condition.Operator != FusionAnimatorConditionOperator.IsTrue)
                    {
                        EnsureUndo();
                        condition.Operator = FusionAnimatorConditionOperator.IsTrue;
                        changed = true;
                    }

                    EditorGUILayout.LabelField(
                        new GUIContent("Operator", "Trigger conditions are one-shot and evaluate only when the trigger fires."),
                        new GUIContent("Fired Once"));
                }
                else
                {
                    FusionAnimatorConditionOperator op = DrawConditionOperatorField(conditionParameter, condition.Operator);
                    if (op != condition.Operator)
                    {
                        EnsureUndo();
                        condition.Operator = op;
                        changed = true;
                    }
                }

                bool supportsAbsolute = false;
                if (conditionParameter != null)
                {
                    switch (conditionParameter.Type)
                    {
                        case FusionAnimatorParameterType.Int:
                        case FusionAnimatorParameterType.Float:
                            supportsAbsolute = true;
                            break;
                        case FusionAnimatorParameterType.Vector2:
                            supportsAbsolute = condition.Operator != FusionAnimatorConditionOperator.IsTrue &&
                                               condition.Operator != FusionAnimatorConditionOperator.IsFalse;
                            break;
                    }
                }

                if (supportsAbsolute)
                {
                    bool useAbsolute = EditorGUILayout.Toggle(
                        new GUIContent("Absolute", "Apply absolute value to the sampled input before comparison."),
                        condition.UseAbsoluteValue);
                    if (useAbsolute != condition.UseAbsoluteValue)
                    {
                        EnsureUndo();
                        condition.UseAbsoluteValue = useAbsolute;
                        changed = true;
                    }
                }
                else if (condition.UseAbsoluteValue)
                {
                    EnsureUndo();
                    condition.UseAbsoluteValue = false;
                    changed = true;
                }

                if (conditionParameter != null)
                {
                    switch (conditionParameter.Type)
                    {
                        case FusionAnimatorParameterType.Bool:
                        {
                            if (condition.Operator == FusionAnimatorConditionOperator.Equal || condition.Operator == FusionAnimatorConditionOperator.NotEqual)
                            {
                                bool boolValue = EditorGUILayout.Toggle(new GUIContent("Bool Value", "Boolean comparison target value."), condition.BoolValue);
                                if (boolValue != condition.BoolValue)
                                {
                                    EnsureUndo();
                                    condition.BoolValue = boolValue;
                                    changed = true;
                                }
                            }

                            break;
                        }
                        case FusionAnimatorParameterType.Trigger:
                        {
                            break;
                        }
                        case FusionAnimatorParameterType.Int:
                        {
                            int intValue = EditorGUILayout.IntField(new GUIContent("Int Value", "Integer comparison target value."), condition.IntValue);
                            if (intValue != condition.IntValue)
                            {
                                EnsureUndo();
                                condition.IntValue = intValue;
                                changed = true;
                            }

                            break;
                        }
                        case FusionAnimatorParameterType.Float:
                        {
                            float floatValue = EditorGUILayout.FloatField(new GUIContent("Float Value", "Float comparison target value."), condition.FloatValue);
                            if (Mathf.Approximately(floatValue, condition.FloatValue) == false)
                            {
                                EnsureUndo();
                                condition.FloatValue = floatValue;
                                changed = true;
                            }

                            break;
                        }
                        case FusionAnimatorParameterType.Vector2:
                        {
                            if (condition.Operator != FusionAnimatorConditionOperator.IsTrue && condition.Operator != FusionAnimatorConditionOperator.IsFalse)
                            {
                                FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out _, out FusionAnimatorParameterComponent component);
                                bool componentSelected = component != FusionAnimatorParameterComponent.None;
                                GUIContent valueLabel = componentSelected
                                    ? new GUIContent("Float Value", "Vector2 component comparison target value.")
                                    : new GUIContent("Magnitude Value", "Vector2 magnitude comparison target value.");
                                float magnitudeValue = EditorGUILayout.FloatField(valueLabel, condition.FloatValue);
                                if (Mathf.Approximately(magnitudeValue, condition.FloatValue) == false)
                                {
                                    EnsureUndo();
                                    condition.FloatValue = magnitudeValue;
                                    changed = true;
                                }
                            }

                            break;
                        }
                    }
                }
                EditorGUILayout.EndVertical();
            }

            if (changed)
            {
                if (rebuildGraph)
                {
                    _graphView?.RebuildFromGraphData();
                }
                else
                {
                    _graphView?.RefreshEdgeForTransition(transition);
                }

                MarkGraphDirty();
            }
        }

        private bool DrawBlendTreeInspector(FusionAnimatorStateDefinition state)
        {
            bool changed = false;
            bool undoRecorded = false;
            void EnsureUndo()
            {
                if (undoRecorded)
                {
                    return;
                }

                RecordUndo("Edit FusionAnimator Blend Tree");
                undoRecorded = true;
            }

            if (state.BlendTree == null)
            {
                EnsureUndo();
                state.BlendTree = new FusionAnimatorBlendTreeDefinition();
                changed = true;
            }

            FusionAnimatorBlendTreeDefinition blendTree = state.BlendTree;
            EditorGUILayout.LabelField("Blend Tree", EditorStyles.boldLabel);

            FusionAnimatorBlendTreeType blendType = (FusionAnimatorBlendTreeType)EditorGUILayout.EnumPopup(new GUIContent("Type", "Blend tree evaluation mode."), blendTree.Type);
            if (blendType != blendTree.Type)
            {
                EnsureUndo();
                blendTree.Type = blendType;
                changed = true;
            }

            string parameterX = blendTree.ParameterXId;
            DrawParameterPicker("Parameter X", "Primary blend parameter id.", value =>
            {
                EnsureUndo();
                blendTree.ParameterXId = value;
            }, blendTree.ParameterXId);
            if (blendTree.ParameterXId != parameterX)
            {
                changed = true;
            }

            bool isTwoDBlendTree =
                blendTree.Type == FusionAnimatorBlendTreeType.TwoDSimpleDirectional ||
                blendTree.Type == FusionAnimatorBlendTreeType.TwoDFreeformDirectional ||
                blendTree.Type == FusionAnimatorBlendTreeType.TwoDFreeformCartesian ||
                blendTree.Type == FusionAnimatorBlendTreeType.DirectionalPoseTime2D;

            if (isTwoDBlendTree)
            {
                string parameterVector2 = blendTree.ParameterVector2Id;
                DrawParameterPicker("Parameter XY", "Optional Vector2 parameter id used as 2D blend input.", value =>
                {
                    EnsureUndo();
                    blendTree.ParameterVector2Id = value;
                }, blendTree.ParameterVector2Id, includeVectorComponents: false);
                if (blendTree.ParameterVector2Id != parameterVector2)
                {
                    changed = true;
                }
            }

            if (isTwoDBlendTree)
            {
                string parameterY = blendTree.ParameterYId;
                DrawParameterPicker("Parameter Y", "Secondary blend parameter id.", value =>
                {
                    EnsureUndo();
                    blendTree.ParameterYId = value;
                }, blendTree.ParameterYId);
                if (blendTree.ParameterYId != parameterY)
                {
                    changed = true;
                }
            }

            if (blendTree.Type == FusionAnimatorBlendTreeType.Direct)
            {
                string directParameter = blendTree.DirectBlendParameterId;
                DrawParameterPicker("Direct Param", "Parameter id used as a direct child weight source.", value =>
                {
                    EnsureUndo();
                    blendTree.DirectBlendParameterId = value;
                }, blendTree.DirectBlendParameterId);
                if (blendTree.DirectBlendParameterId != directParameter)
                {
                    changed = true;
                }
            }
            else if (blendTree.Type == FusionAnimatorBlendTreeType.DirectionalPoseTime2D)
            {
                string poseTimeParameter = blendTree.PoseTimeParameterId;
                DrawParameterPicker("Pose Time Param", "Optional pose-time driver parameter. If empty, uses magnitude of the directional input.", value =>
                {
                    EnsureUndo();
                    blendTree.PoseTimeParameterId = value;
                }, blendTree.PoseTimeParameterId);
                if (blendTree.PoseTimeParameterId != poseTimeParameter)
                {
                    changed = true;
                }

                float timeOffset = EditorGUILayout.FloatField(new GUIContent("Time Offset", "Offset applied before shaping/clamping pose-time input."), blendTree.InputOffsetX);
                if (Mathf.Approximately(timeOffset, blendTree.InputOffsetX) == false)
                {
                    EnsureUndo();
                    blendTree.InputOffsetX = timeOffset;
                    changed = true;
                }

                float timePower = EditorGUILayout.FloatField(new GUIContent("Time Power", "Power/exponent applied after offset to shape pose-time response."), blendTree.InputPowerX);
                timePower = Mathf.Max(0.0001f, timePower);
                if (Mathf.Approximately(timePower, blendTree.InputPowerX) == false)
                {
                    EnsureUndo();
                    blendTree.InputPowerX = timePower;
                    changed = true;
                }
            }

            bool normalizeTimeScale = EditorGUILayout.Toggle(new GUIContent("Normalize Speed", "Normalize child time scales during blending."), blendTree.NormalizeTimeScale);
            if (normalizeTimeScale != blendTree.NormalizeTimeScale)
            {
                EnsureUndo();
                blendTree.NormalizeTimeScale = normalizeTimeScale;
                changed = true;
            }

            EditorGUILayout.Space(6.0f);
            EditorGUILayout.LabelField("Children", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button(new GUIContent("Add Child", "Add a blend tree motion child.")))
                {
                    EnsureUndo();
                    blendTree.Children.Add(new FusionAnimatorBlendTreeChild());
                    changed = true;
                }

                GUILayout.FlexibleSpace();
                bool autoDetectOnClipAssign = GUILayout.Toggle(
                    blendTree.AutoDetectOnClipAssign,
                    new GUIContent("Auto Detect", "If enabled, assigning a child clip auto-runs detect for threshold/position values."),
                    EditorStyles.toolbarButton,
                    GUILayout.Width(100.0f));
                if (autoDetectOnClipAssign != blendTree.AutoDetectOnClipAssign)
                {
                    EnsureUndo();
                    blendTree.AutoDetectOnClipAssign = autoDetectOnClipAssign;
                    changed = true;
                }
            }

            for (int i = 0; i < blendTree.Children.Count; ++i)
            {
                FusionAnimatorBlendTreeChild child = blendTree.Children[i];
                if (child == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.Format("Child {0}", i), EditorStyles.miniBoldLabel);
                AnimationClip resolvedChildClip = FusionAnimatorClipBindingUtility.ResolveClip(_graph, child, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                using (new EditorGUI.DisabledScope(resolvedChildClip == null))
                {
                    if (GUILayout.Button(new GUIContent("Detect", "Auto-detect this child threshold/position from its assigned clip."), GUILayout.Width(70.0f)))
                    {
                        EnsureUndo();
                        AutoDetectBlendTreeChildFromClip(blendTree, child, resolvedChildClip, i);
                        changed = true;
                    }
                }

                if (GUILayout.Button(new GUIContent("Remove", "Remove this blend child."), GUILayout.Width(80.0f)))
                {
                    EnsureUndo();
                    blendTree.Children.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    changed = true;
                    break;
                }

                EditorGUILayout.EndHorizontal();

                string childName = EditorGUILayout.TextField(new GUIContent("Name", "Display name for this child motion."), child.Name);
                if (childName != child.Name)
                {
                    EnsureUndo();
                    child.Name = childName;
                    changed = true;
                }

                DrawClipBindingPicker(new GUIContent("Binding", "Choose Direct to use this child's clip field, or select a reusable graph binding."), child, EnsureUndo, () => changed = true);

                AnimationClip displayedChildClip = child.ReferenceMode == FusionAnimatorClipReferenceMode.Direct
                    ? child.Clip
                    : FusionAnimatorClipBindingUtility.ResolveClip(_graph, child, EvaluateCondition, ResolvePreviewBindingClipIndexParameter);
                using (new EditorGUI.DisabledScope(child.ReferenceMode != FusionAnimatorClipReferenceMode.Direct))
                {
                    AnimationClip childClip = EditorGUILayout.ObjectField(new GUIContent("Clip", "Animation clip used by this child."), displayedChildClip, typeof(AnimationClip), false) as AnimationClip;
                    if (child.ReferenceMode == FusionAnimatorClipReferenceMode.Direct && childClip != child.Clip)
                    {
                        EnsureUndo();
                        child.Clip = childClip;
                        if (blendTree.AutoDetectOnClipAssign && childClip != null)
                        {
                            AutoDetectBlendTreeChildFromClip(blendTree, child, childClip, i);
                        }
                        changed = true;
                    }
                }

                float threshold = EditorGUILayout.FloatField(new GUIContent("Threshold", "1D threshold or directional angle threshold value."), child.Threshold);
                if (Mathf.Approximately(threshold, child.Threshold) == false)
                {
                    EnsureUndo();
                    child.Threshold = RoundBlendValue(threshold);
                    if (blendTree.Type == FusionAnimatorBlendTreeType.OneD)
                    {
                        child.Position = new Vector2(child.Threshold, 0.0f);
                    }
                    changed = true;
                }

                if (blendTree.Type == FusionAnimatorBlendTreeType.OneD)
                {
                    float xPosition = EditorGUILayout.FloatField(new GUIContent("Position X", "1D child position (X axis)."), child.Position.x);
                    float roundedX = RoundBlendValue(xPosition);
                    if (Mathf.Approximately(roundedX, child.Position.x) == false)
                    {
                        EnsureUndo();
                        child.Position = new Vector2(roundedX, 0.0f);
                        child.Threshold = roundedX;
                        changed = true;
                    }
                }
                else
                {
                    Vector2 position = EditorGUILayout.Vector2Field(new GUIContent("Position", "2D cartesian/directional child position."), child.Position);
                    Vector2 roundedPosition = new Vector2(RoundBlendValue(position.x), RoundBlendValue(position.y));
                    if (roundedPosition != child.Position)
                    {
                        EnsureUndo();
                        child.Position = roundedPosition;
                        changed = true;
                    }
                }

                string childDirectParam = child.DirectParameterId;
                DrawParameterPicker("Direct Param", "Optional per-child direct parameter id.", value =>
                {
                    EnsureUndo();
                    child.DirectParameterId = value;
                }, child.DirectParameterId);
                if (child.DirectParameterId != childDirectParam)
                {
                    changed = true;
                }

                float timeScale = Mathf.Max(0.0f, EditorGUILayout.FloatField(new GUIContent("Time Scale", "Child-specific playback speed multiplier."), child.TimeScale));
                if (Mathf.Approximately(timeScale, child.TimeScale) == false)
                {
                    EnsureUndo();
                    child.TimeScale = timeScale;
                    changed = true;
                }

                EditorGUILayout.EndVertical();
            }

            return changed;
        }

        private static void AutoDetectBlendTreeChildFromClip(FusionAnimatorBlendTreeDefinition blendTree, FusionAnimatorBlendTreeChild child, AnimationClip clip, int childIndex)
        {
            if (blendTree == null || child == null || clip == null)
            {
                return;
            }

            string name = string.IsNullOrWhiteSpace(child.Name) ? clip.name : child.Name;
            Vector2 planarMotion = GetClipPlanarMotion(clip);

            switch (blendTree.Type)
            {
                case FusionAnimatorBlendTreeType.OneD:
                {
                    float directionalThreshold = GetDirectionalThresholdFromName(name);
                    if (Mathf.Abs(directionalThreshold) > 0.0001f || NameImpliesForward(name))
                    {
                        child.Threshold = directionalThreshold;
                    }
                    else if (planarMotion.sqrMagnitude > 0.0001f)
                    {
                        child.Threshold = planarMotion.magnitude;
                    }
                    else
                    {
                        child.Threshold = childIndex;
                    }

                    child.Threshold = RoundBlendValue(child.Threshold);
                    child.Position = new Vector2(child.Threshold, 0.0f);

                    break;
                }
                case FusionAnimatorBlendTreeType.TwoDSimpleDirectional:
                case FusionAnimatorBlendTreeType.TwoDFreeformDirectional:
                case FusionAnimatorBlendTreeType.TwoDFreeformCartesian:
                case FusionAnimatorBlendTreeType.DirectionalPoseTime2D:
                {
                    bool directionalTree = blendTree.Type == FusionAnimatorBlendTreeType.TwoDSimpleDirectional ||
                                           blendTree.Type == FusionAnimatorBlendTreeType.TwoDFreeformDirectional ||
                                           blendTree.Type == FusionAnimatorBlendTreeType.DirectionalPoseTime2D;
                    float childRadius = directionalTree ? 1.0f : Mathf.Max(0.5f, planarMotion.magnitude);
                    if (planarMotion.sqrMagnitude > 0.0001f)
                    {
                        child.Position = directionalTree ? planarMotion.normalized : planarMotion;
                    }
                    else
                    {
                        Vector2 directionalPosition = GetDirectionalPositionFromName(name);
                        if (directionalPosition.sqrMagnitude > 0.0001f)
                        {
                            child.Position = directionalTree ? directionalPosition.normalized : directionalPosition * childRadius;
                        }
                        else
                        {
                            float angleDeg = child.Threshold;
                            if (Mathf.Approximately(angleDeg, 0.0f))
                            {
                                int count = blendTree.Children != null ? Mathf.Max(1, blendTree.Children.Count) : 1;
                                angleDeg = (360.0f / count) * childIndex;
                            }

                            float angleRad = angleDeg * Mathf.Deg2Rad;
                            Vector2 ringDirection = new Vector2(Mathf.Sin(angleRad), Mathf.Cos(angleRad));
                            child.Position = directionalTree ? ringDirection.normalized : ringDirection * childRadius;
                        }
                    }

                    child.Position = new Vector2(RoundBlendValue(child.Position.x), RoundBlendValue(child.Position.y));

                    break;
                }
                case FusionAnimatorBlendTreeType.Direct:
                {
                    if (string.IsNullOrWhiteSpace(child.DirectParameterId))
                    {
                        child.DirectParameterId = blendTree.DirectBlendParameterId;
                    }

                    break;
                }
            }
        }

        private static Vector2 GetClipPlanarMotion(AnimationClip clip)
        {
            if (clip == null)
            {
                return Vector2.zero;
            }

            if (TryExtractRootLocalPositionDelta(clip, out Vector2 rootDelta))
            {
                return rootDelta / Mathf.Max(0.0001f, clip.length);
            }

            Vector2 average = new Vector2(clip.averageSpeed.x, clip.averageSpeed.z);
            if (average.sqrMagnitude > 0.000001f)
            {
                return average;
            }

            return TryExtractAveragePlanarVelocityFromCurves(clip, out Vector2 curveVelocity)
                ? curveVelocity
                : Vector2.zero;
        }

        private static bool TryExtractRootLocalPositionDelta(AnimationClip clip, out Vector2 delta)
        {
            delta = Vector2.zero;
            if (clip == null)
            {
                return false;
            }

            float duration = Mathf.Max(0.0001f, clip.length);
            AnimationCurve rootX = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), "m_LocalPosition.x"));
            AnimationCurve rootZ = AnimationUtility.GetEditorCurve(clip, EditorCurveBinding.FloatCurve(string.Empty, typeof(Transform), "m_LocalPosition.z"));
            bool hasX = rootX != null && rootX.length > 0;
            bool hasZ = rootZ != null && rootZ.length > 0;
            if (hasX == false && hasZ == false)
            {
                return false;
            }

            float startX = hasX ? rootX.Evaluate(0.0f) : 0.0f;
            float endX = hasX ? rootX.Evaluate(duration) : 0.0f;
            float startZ = hasZ ? rootZ.Evaluate(0.0f) : 0.0f;
            float endZ = hasZ ? rootZ.Evaluate(duration) : 0.0f;
            delta = new Vector2(endX - startX, endZ - startZ);
            return true;
        }

        private static bool TryExtractAveragePlanarVelocityFromCurves(AnimationClip clip, out Vector2 planarVelocity)
        {
            planarVelocity = Vector2.zero;
            if (clip == null)
            {
                return false;
            }

            EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
            if (bindings == null || bindings.Length == 0)
            {
                return false;
            }

            float duration = Mathf.Max(0.0001f, clip.length);
            bool hasX = false;
            bool hasZ = false;
            float startX = 0.0f;
            float endX = 0.0f;
            float startZ = 0.0f;
            float endZ = 0.0f;

            for (int i = 0; i < bindings.Length; ++i)
            {
                EditorCurveBinding binding = bindings[i];
                if (TryMatchRootPlanarBinding(binding.propertyName, out int axis) == false)
                {
                    continue;
                }

                AnimationCurve curve = AnimationUtility.GetEditorCurve(clip, binding);
                if (curve == null || curve.length == 0)
                {
                    continue;
                }

                float begin = curve.Evaluate(0.0f);
                float end = curve.Evaluate(duration);
                if (axis == 0)
                {
                    startX = begin;
                    endX = end;
                    hasX = true;
                }
                else if (axis == 1)
                {
                    startZ = begin;
                    endZ = end;
                    hasZ = true;
                }
            }

            if (hasX == false && hasZ == false)
            {
                return false;
            }

            float vx = hasX ? (endX - startX) / duration : 0.0f;
            float vz = hasZ ? (endZ - startZ) / duration : 0.0f;
            planarVelocity = new Vector2(vx, vz);
            return true;
        }

        private static bool TryMatchRootPlanarBinding(string propertyName, out int axis)
        {
            axis = -1;
            if (string.IsNullOrWhiteSpace(propertyName))
            {
                return false;
            }

            string normalized = propertyName.Replace(" ", string.Empty);
            bool canUse =
                normalized.IndexOf("LocalPosition", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("RootT", StringComparison.OrdinalIgnoreCase) >= 0 ||
                normalized.IndexOf("MotionT", StringComparison.OrdinalIgnoreCase) >= 0;

            if (canUse == false)
            {
                return false;
            }

            if (normalized.EndsWith(".x", StringComparison.OrdinalIgnoreCase))
            {
                axis = 0;
                return true;
            }

            if (normalized.EndsWith(".z", StringComparison.OrdinalIgnoreCase))
            {
                axis = 1;
                return true;
            }

            return false;
        }

        private static float GetDirectionalThresholdFromName(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return 0.0f;
            }

            string lower = clipName.ToLowerInvariant();
            if (lower.Contains("back") || lower.Contains("bwd"))
            {
                return -180.0f;
            }

            if (lower.Contains("left") || lower.Contains("lt"))
            {
                return -90.0f;
            }

            if (lower.Contains("right") || lower.Contains("rt"))
            {
                return 90.0f;
            }

            if (NameImpliesForward(lower))
            {
                return 0.0f;
            }

            return 0.0f;
        }

        private static float RoundBlendValue(float value)
        {
            if (Mathf.Abs(value) < 0.0001f)
            {
                return 0.0f;
            }

            float rounded = (float)Math.Round(value, 3, MidpointRounding.AwayFromZero);
            if (Mathf.Abs(rounded - Mathf.Round(rounded)) < 0.0005f)
            {
                rounded = Mathf.Round(rounded);
            }

            return rounded;
        }

        private static bool NameImpliesForward(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return false;
            }

            string lower = clipName.ToLowerInvariant();
            return lower.Contains("fwd") || lower.Contains("forward") || lower.Contains("front");
        }

        private static Vector2 GetDirectionalPositionFromName(string clipName)
        {
            if (string.IsNullOrWhiteSpace(clipName))
            {
                return Vector2.zero;
            }

            string lower = clipName.ToLowerInvariant();
            int x = 0;
            int y = 0;

            if (lower.Contains("left") || lower.Contains("lt")) x -= 1;
            if (lower.Contains("right") || lower.Contains("rt")) x += 1;
            if (lower.Contains("back") || lower.Contains("bwd")) y -= 1;
            if (lower.Contains("fwd") || lower.Contains("forward") || lower.Contains("front")) y += 1;

            Vector2 direction = new Vector2(x, y);
            if (direction.sqrMagnitude > 1.0f)
            {
                direction.Normalize();
            }

            return direction;
        }

        private FusionAnimatorConditionOperator DrawConditionOperatorField(FusionAnimatorParameterDefinition parameter, FusionAnimatorConditionOperator current)
        {
            if (parameter == null)
            {
                return (FusionAnimatorConditionOperator)EditorGUILayout.EnumPopup(new GUIContent("Operator", "Comparison operator used by this condition."), current);
            }

            FusionAnimatorConditionOperator[] options = GetConditionOperatorsForType(parameter.Type);
            FusionAnimatorConditionOperator normalized = NormalizeConditionOperator(current, options);
            string[] labels = new string[options.Length];
            int selectedIndex = 0;
            for (int i = 0; i < options.Length; ++i)
            {
                labels[i] = options[i].ToString();
                if (options[i] == normalized)
                {
                    selectedIndex = i;
                }
            }

            int newIndex = EditorGUILayout.Popup(new GUIContent("Operator", "Comparison operator used by this condition."), selectedIndex, labels);
            newIndex = Mathf.Clamp(newIndex, 0, options.Length - 1);
            return options[newIndex];
        }

        private static FusionAnimatorConditionOperator[] GetConditionOperatorsForType(FusionAnimatorParameterType parameterType)
        {
            switch (parameterType)
            {
                case FusionAnimatorParameterType.Bool:
                    return BoolConditionOperators;
                case FusionAnimatorParameterType.Trigger:
                    return TriggerConditionOperators;
                case FusionAnimatorParameterType.Int:
                case FusionAnimatorParameterType.Float:
                    return NumericConditionOperators;
                case FusionAnimatorParameterType.Vector2:
                    return NumericConditionOperators;
                default:
                    return NumericConditionOperators;
            }
        }

        private static FusionAnimatorConditionOperator NormalizeConditionOperator(FusionAnimatorConditionOperator value, FusionAnimatorConditionOperator[] options)
        {
            for (int i = 0; i < options.Length; ++i)
            {
                if (options[i] == value)
                {
                    return value;
                }
            }

            return options.Length > 0 ? options[0] : FusionAnimatorConditionOperator.Equal;
        }

        private void DrawLayerPicker(string label, string tooltip, Action<string> setValue, string current)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string inputValue = EditorGUILayout.TextField(new GUIContent(label, tooltip), current);
                if (inputValue != current)
                {
                    setValue(inputValue);
                }

                if (GUILayout.Button(new GUIContent("Pick", "Pick an existing layer id from the graph."), GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    for (int i = 0, count = _graph.Layers.Count; i < count; ++i)
                    {
                        FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                        if (layer == null)
                        {
                            continue;
                        }

                        string layerId = layer.Id;
                        menu.AddItem(new GUIContent(string.Format("{0} ({1})", layer.Name, layer.Id)), layerId == current, () =>
                        {
                            setValue(layerId);
                            MarkGraphDirty();
                            _graphView?.RebuildFromGraphData();
                        });
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawStatePicker(string label, string tooltip, Action<string> setValue, string current)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string inputValue = EditorGUILayout.TextField(new GUIContent(label, tooltip), current);
                if (inputValue != current)
                {
                    setValue(inputValue);
                }

                if (GUILayout.Button(new GUIContent("Pick", "Pick an existing state id from the graph."), GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("[Entry]"), current == FusionAnimatorGraphAsset.SpecialNodeEntryId, () =>
                    {
                        setValue(FusionAnimatorGraphAsset.SpecialNodeEntryId);
                        _graphView?.RebuildFromGraphData();
                        MarkGraphDirty();
                    });
                    menu.AddItem(new GUIContent("[Any State]"), current == FusionAnimatorGraphAsset.SpecialNodeAnyId, () =>
                    {
                        setValue(FusionAnimatorGraphAsset.SpecialNodeAnyId);
                        _graphView?.RebuildFromGraphData();
                        MarkGraphDirty();
                    });
                    menu.AddItem(new GUIContent("[Exit]"), current == FusionAnimatorGraphAsset.SpecialNodeExitId, () =>
                    {
                        setValue(FusionAnimatorGraphAsset.SpecialNodeExitId);
                        _graphView?.RebuildFromGraphData();
                        MarkGraphDirty();
                    });
                    menu.AddSeparator(string.Empty);
                    for (int i = 0, count = _graph.States.Count; i < count; ++i)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[i];
                        if (state == null || IsScopeSentinelStateName(state.Name))
                        {
                            continue;
                        }

                        string stateId = state.Id;
                        menu.AddItem(new GUIContent(string.Format("{0} ({1})", state.Name, state.Id)), stateId == current, () =>
                        {
                            setValue(stateId);
                            _graphView?.RebuildFromGraphData();
                            MarkGraphDirty();
                        });
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawParameterPicker(string label, string tooltip, Action<string> setValue, string current, bool includeVectorComponents = true)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string displayValue = ResolveParameterDisplayName(current);
                string inputValue = EditorGUILayout.TextField(
                    new GUIContent(label, string.Format("{0}\nCurrent Id: {1}", tooltip, string.IsNullOrWhiteSpace(current) ? "<none>" : current)),
                    displayValue);
                if (inputValue != displayValue)
                {
                    setValue(ResolveParameterIdFromDisplayInput(inputValue));
                }

                if (GUILayout.Button(new GUIContent("Pick", "Pick an existing parameter id from the graph."), GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("None"), string.IsNullOrWhiteSpace(current), () =>
                    {
                        setValue(string.Empty);
                        MarkGraphDirty();
                    });
                    menu.AddSeparator(string.Empty);
                    for (int i = 0, count = _graph.Parameters.Count; i < count; ++i)
                    {
                        FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                        if (parameter == null)
                        {
                            continue;
                        }

                        string parameterId = parameter.Id;
                        menu.AddItem(new GUIContent(string.Format("{0} ({1})", parameter.Name, parameter.Id)), parameterId == current, () =>
                        {
                            setValue(parameterId);
                            MarkGraphDirty();
                        });

                        if (includeVectorComponents && parameter.Type == FusionAnimatorParameterType.Vector2)
                        {
                            string xReference = FusionAnimatorParameterReferenceUtility.Build(parameter.Id, FusionAnimatorParameterComponent.X);
                            string yReference = FusionAnimatorParameterReferenceUtility.Build(parameter.Id, FusionAnimatorParameterComponent.Y);
                            string parameterName = string.IsNullOrWhiteSpace(parameter.Name) ? parameter.Id : parameter.Name;
                            menu.AddItem(new GUIContent(string.Format("{0}.X ({1})", parameterName, xReference)), xReference == current, () =>
                            {
                                setValue(xReference);
                                MarkGraphDirty();
                            });
                            menu.AddItem(new GUIContent(string.Format("{0}.Y ({1})", parameterName, yReference)), yReference == current, () =>
                            {
                                setValue(yReference);
                                MarkGraphDirty();
                            });
                        }
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawIntParameterPicker(string label, string tooltip, Action<string> setValue, string current)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string displayValue = ResolveParameterDisplayName(current);
                string inputValue = EditorGUILayout.TextField(
                    new GUIContent(label, string.Format("{0}\nCurrent Id: {1}", tooltip, string.IsNullOrWhiteSpace(current) ? "<none>" : current)),
                    displayValue);
                if (inputValue != displayValue)
                {
                    string resolved = ResolveParameterIdFromDisplayInput(inputValue);
                    if (string.IsNullOrWhiteSpace(resolved))
                    {
                        setValue(string.Empty);
                    }
                    else
                    {
                        FusionAnimatorParameterDefinition parameter = FindParameterById(resolved);
                        setValue(parameter != null && parameter.Type == FusionAnimatorParameterType.Int ? resolved : string.Empty);
                    }
                }

                if (GUILayout.Button(new GUIContent("Pick", "Pick an Int parameter id from the graph."), GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    menu.AddItem(new GUIContent("None"), string.IsNullOrWhiteSpace(current), () =>
                    {
                        setValue(string.Empty);
                        MarkGraphDirty();
                    });
                    menu.AddSeparator(string.Empty);
                    for (int i = 0, count = _graph.Parameters.Count; i < count; ++i)
                    {
                        FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                        if (parameter == null || parameter.Type != FusionAnimatorParameterType.Int)
                        {
                            continue;
                        }

                        string parameterId = parameter.Id;
                        menu.AddItem(new GUIContent(string.Format("{0} ({1})", parameter.Name, parameter.Id)), parameterId == current, () =>
                        {
                            setValue(parameterId);
                            MarkGraphDirty();
                        });
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawClipBindingPicker(GUIContent label, FusionAnimatorClipSlot slot, Action ensureUndo, Action onChanged)
        {
            if (slot == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(label, ResolveClipBindingDisplayName(slot));
                }

                if (GUILayout.Button(new GUIContent("Pick", "Pick Direct or one of the graph clip bindings."), GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    bool isDirect = slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct;
                    menu.AddItem(new GUIContent("Direct"), isDirect, () =>
                    {
                        ensureUndo?.Invoke();
                        slot.ReferenceMode = FusionAnimatorClipReferenceMode.Direct;
                        slot.BindingId = string.Empty;
                        onChanged?.Invoke();
                        _graphView?.RefreshNodeDisplay(_selectedState);
                        MarkGraphDirty();
                    });

                    if (_graph.ClipBindings != null && _graph.ClipBindings.Count > 0)
                    {
                        menu.AddSeparator(string.Empty);
                        for (int i = 0; i < _graph.ClipBindings.Count; ++i)
                        {
                            FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[i];
                            if (binding == null || string.IsNullOrWhiteSpace(binding.Id))
                            {
                                continue;
                            }

                            string bindingId = binding.Id;
                            string bindingName = string.IsNullOrWhiteSpace(binding.Name) ? bindingId : binding.Name;
                            bool selected = slot.ReferenceMode == FusionAnimatorClipReferenceMode.Binding &&
                                            string.Equals(slot.BindingId, bindingId, StringComparison.Ordinal);
                            menu.AddItem(new GUIContent(string.Format("{0} ({1})", bindingName, bindingId)), selected, () =>
                            {
                                ensureUndo?.Invoke();
                                slot.ReferenceMode = FusionAnimatorClipReferenceMode.Binding;
                                slot.BindingId = bindingId;
                                onChanged?.Invoke();
                                _graphView?.RefreshNodeDisplay(_selectedState);
                                MarkGraphDirty();
                            });
                        }
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawClipBindingPicker(GUIContent label, FusionAnimatorBlendTreeChild child, Action ensureUndo, Action onChanged)
        {
            if (child == null)
            {
                return;
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.TextField(label, ResolveClipBindingDisplayName(child));
                }

                if (GUILayout.Button(new GUIContent("Pick", "Pick Direct or one of the graph clip bindings."), GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    bool isDirect = child.ReferenceMode == FusionAnimatorClipReferenceMode.Direct;
                    menu.AddItem(new GUIContent("Direct"), isDirect, () =>
                    {
                        ensureUndo?.Invoke();
                        child.ReferenceMode = FusionAnimatorClipReferenceMode.Direct;
                        child.BindingId = string.Empty;
                        onChanged?.Invoke();
                        _graphView?.RefreshNodeDisplay(_selectedState);
                        MarkGraphDirty();
                    });

                    if (_graph.ClipBindings != null && _graph.ClipBindings.Count > 0)
                    {
                        menu.AddSeparator(string.Empty);
                        for (int i = 0; i < _graph.ClipBindings.Count; ++i)
                        {
                            FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[i];
                            if (binding == null || string.IsNullOrWhiteSpace(binding.Id))
                            {
                                continue;
                            }

                            string bindingId = binding.Id;
                            string bindingName = string.IsNullOrWhiteSpace(binding.Name) ? bindingId : binding.Name;
                            bool selected = child.ReferenceMode == FusionAnimatorClipReferenceMode.Binding &&
                                            string.Equals(child.BindingId, bindingId, StringComparison.Ordinal);
                            menu.AddItem(new GUIContent(string.Format("{0} ({1})", bindingName, bindingId)), selected, () =>
                            {
                                ensureUndo?.Invoke();
                                child.ReferenceMode = FusionAnimatorClipReferenceMode.Binding;
                                child.BindingId = bindingId;
                                onChanged?.Invoke();
                                _graphView?.RefreshNodeDisplay(_selectedState);
                                MarkGraphDirty();
                            });
                        }
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private string ResolveClipBindingDisplayName(FusionAnimatorClipSlot slot)
        {
            if (slot == null)
            {
                return string.Empty;
            }

            if (slot.ReferenceMode == FusionAnimatorClipReferenceMode.Direct)
            {
                return "Direct";
            }

            if (string.IsNullOrWhiteSpace(slot.BindingId))
            {
                return "Unassigned";
            }

            FusionAnimatorClipBindingDefinition binding = FusionAnimatorClipBindingUtility.FindBinding(_graph, slot.BindingId);
            if (binding == null)
            {
                return string.Format("{0} (Missing)", slot.BindingId);
            }

            string bindingName = string.IsNullOrWhiteSpace(binding.Name) ? binding.Id : binding.Name;
            return string.Format("{0} ({1})", bindingName, binding.Id);
        }

        private string ResolveClipBindingDisplayName(FusionAnimatorBlendTreeChild child)
        {
            if (child == null)
            {
                return string.Empty;
            }

            if (child.ReferenceMode == FusionAnimatorClipReferenceMode.Direct)
            {
                return "Direct";
            }

            if (string.IsNullOrWhiteSpace(child.BindingId))
            {
                return "Unassigned";
            }

            FusionAnimatorClipBindingDefinition binding = FusionAnimatorClipBindingUtility.FindBinding(_graph, child.BindingId);
            if (binding == null)
            {
                return string.Format("{0} (Missing)", child.BindingId);
            }

            string bindingName = string.IsNullOrWhiteSpace(binding.Name) ? binding.Id : binding.Name;
            return string.Format("{0} ({1})", bindingName, binding.Id);
        }

        private string ResolveParameterDisplayName(string parameterId)
        {
            if (_graph == null || _graph.Parameters == null || string.IsNullOrWhiteSpace(parameterId))
            {
                return parameterId ?? string.Empty;
            }

            if (FusionAnimatorParameterReferenceUtility.TryParse(parameterId, out string baseParameterId, out FusionAnimatorParameterComponent component) == false)
            {
                return parameterId;
            }

            for (int i = 0, count = _graph.Parameters.Count; i < count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter == null ||
                    string.IsNullOrWhiteSpace(parameter.Id))
                {
                    continue;
                }

                if (string.Equals(parameter.Id, baseParameterId, StringComparison.Ordinal))
                {
                    string displayName = string.IsNullOrWhiteSpace(parameter.Name) == false ? parameter.Name : parameter.Id;
                    return FusionAnimatorParameterReferenceUtility.ResolveDisplayName(displayName, parameter.Id, component);
                }
            }

            return parameterId;
        }

        private string ResolveParameterIdFromDisplayInput(string input)
        {
            if (_graph == null || _graph.Parameters == null || string.IsNullOrWhiteSpace(input))
            {
                return input ?? string.Empty;
            }

            string trimmed = input.Trim();
            if (FusionAnimatorParameterReferenceUtility.TryParse(trimmed, out string explicitBaseId, out FusionAnimatorParameterComponent explicitComponent))
            {
                FusionAnimatorParameterDefinition explicitParameter = FindParameterById(explicitBaseId);
                if (explicitParameter != null &&
                    (explicitComponent == FusionAnimatorParameterComponent.None || explicitParameter.Type == FusionAnimatorParameterType.Vector2))
                {
                    return FusionAnimatorParameterReferenceUtility.Build(explicitParameter.Id, explicitComponent);
                }
            }

            for (int i = 0, count = _graph.Parameters.Count; i < count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter == null)
                {
                    continue;
                }

                if (string.Equals(parameter.Id, trimmed, StringComparison.Ordinal))
                {
                    return parameter.Id;
                }

                if (string.IsNullOrWhiteSpace(parameter.Name) == false &&
                    string.Equals(parameter.Name, trimmed, StringComparison.OrdinalIgnoreCase))
                {
                    return parameter.Id;
                }

                if (parameter.Type == FusionAnimatorParameterType.Vector2)
                {
                    string parameterName = string.IsNullOrWhiteSpace(parameter.Name) ? parameter.Id : parameter.Name;
                    string displayX = FusionAnimatorParameterReferenceUtility.ResolveDisplayName(parameterName, parameter.Id, FusionAnimatorParameterComponent.X);
                    string displayY = FusionAnimatorParameterReferenceUtility.ResolveDisplayName(parameterName, parameter.Id, FusionAnimatorParameterComponent.Y);
                    if (string.Equals(displayX, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return FusionAnimatorParameterReferenceUtility.Build(parameter.Id, FusionAnimatorParameterComponent.X);
                    }

                    if (string.Equals(displayY, trimmed, StringComparison.OrdinalIgnoreCase))
                    {
                        return FusionAnimatorParameterReferenceUtility.Build(parameter.Id, FusionAnimatorParameterComponent.Y);
                    }
                }
            }

            return trimmed;
        }

        private int? ResolvePreviewBindingClipIndexParameter(string parameterReference)
        {
            if (TryResolvePreviewParameterReference(parameterReference, out string parameterId, out FusionAnimatorParameterComponent component) == false)
            {
                return null;
            }

            if (component != FusionAnimatorParameterComponent.None)
            {
                return null;
            }

            FusionAnimatorParameterDefinition parameter = FindParameterById(parameterId);
            if (parameter == null || parameter.Type != FusionAnimatorParameterType.Int)
            {
                return null;
            }

            PreviewParameterEntry entry = FindPreviewEntry(parameterId);
            if (entry == null)
            {
                return parameter.DefaultInt;
            }

            return entry.IntValue;
        }

        private void HandleLibraryDeleteCommand(Event evt)
        {
            if (evt == null || _graph == null || EditorGUIUtility.editingTextField)
            {
                return;
            }

            if (evt.type != EventType.KeyDown)
            {
                return;
            }

            if (evt.keyCode != KeyCode.Delete && evt.keyCode != KeyCode.Backspace)
            {
                return;
            }

            if (evt.alt || evt.command || evt.control)
            {
                return;
            }

            bool handled = false;
            switch (_leftLibraryTab)
            {
                case LeftLibraryTab.Parameters:
                    handled = TryRemoveSelectedParameterFromLibrary();
                    break;
                case LeftLibraryTab.Layers:
                    handled = TryRemoveSelectedLayerFromLibrary();
                    break;
                case LeftLibraryTab.Bindings:
                    if (string.IsNullOrWhiteSpace(_selectedBindingGroupId) == false &&
                        FindBindingGroupById(_selectedBindingGroupId) != null)
                    {
                        RemoveBindingGroup(_selectedBindingGroupId);
                        handled = true;
                    }
                    else
                    {
                        handled = TryRemoveSelectedBindingFromLibrary();
                    }
                    break;
            }

            if (handled)
            {
                evt.Use();
            }
        }

        private bool TryRemoveSelectedParameterFromLibrary()
        {
            if (_graph?.Parameters == null ||
                _selectedParameterIndex < 0 ||
                _selectedParameterIndex >= _graph.Parameters.Count)
            {
                return false;
            }

            CancelReorderDrag();
            FusionAnimatorParameterDefinition parameterToRemove = _graph.Parameters[_selectedParameterIndex];
            string parameterName = parameterToRemove != null && string.IsNullOrWhiteSpace(parameterToRemove.Name) == false ? parameterToRemove.Name : "Parameter";
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Parameter",
                string.Format("Remove parameter '{0}' and all transition conditions that reference it?", parameterName),
                "Remove",
                "Cancel");
            if (confirmed)
            {
                RecordUndo("Remove FusionAnimator Parameter");
                string removedParameterId = parameterToRemove != null ? parameterToRemove.Id : null;
                _graph.Parameters.RemoveAt(_selectedParameterIndex);
                _selectedParameterIndex = -1;
                _selectedBindingIndex = -1;
                _selectedLayerIndex = -1;
                _selectedState = null;
                _selectedTransition = null;

                if (string.IsNullOrWhiteSpace(removedParameterId) == false && _graph.Transitions != null)
                {
                    for (int transitionIndex = 0; transitionIndex < _graph.Transitions.Count; ++transitionIndex)
                    {
                        FusionAnimatorTransitionDefinition transition = _graph.Transitions[transitionIndex];
                        if (transition?.Conditions == null)
                        {
                            continue;
                        }

                        transition.Conditions.RemoveAll(condition =>
                        {
                            if (condition == null || string.IsNullOrWhiteSpace(condition.ParameterId))
                            {
                                return false;
                            }

                            if (FusionAnimatorParameterReferenceUtility.TryParse(condition.ParameterId, out string baseParameterId, out _) == false)
                            {
                                baseParameterId = condition.ParameterId;
                            }

                            return string.Equals(baseParameterId, removedParameterId, StringComparison.Ordinal);
                        });
                    }
                }

                _inspector?.MarkDirtyRepaint();
                MarkGraphDirty();
            }

            return true;
        }

        private bool TryRemoveSelectedLayerFromLibrary()
        {
            if (_graph?.Layers == null ||
                _selectedLayerIndex < 0 ||
                _selectedLayerIndex >= _graph.Layers.Count)
            {
                return false;
            }

            CancelReorderDrag();
            FusionAnimatorLayerDefinition layerToRemove = _graph.Layers[_selectedLayerIndex];
            string layerDisplayName = layerToRemove != null && string.IsNullOrWhiteSpace(layerToRemove.Name) == false ? layerToRemove.Name : "Layer";
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Layer",
                string.Format("Remove layer '{0}' and all states/transitions assigned to it?", layerDisplayName),
                "Remove",
                "Cancel");
            if (confirmed)
            {
                RecordUndo("Remove FusionAnimator Layer");
                string removedLayerId = layerToRemove != null ? layerToRemove.Id : null;

                HashSet<string> removedStateIds = new HashSet<string>(StringComparer.Ordinal);
                for (int stateIndex = _graph.States.Count - 1; stateIndex >= 0; --stateIndex)
                {
                    FusionAnimatorStateDefinition state = _graph.States[stateIndex];
                    if (state != null && string.Equals(state.LayerId, removedLayerId, StringComparison.Ordinal))
                    {
                        removedStateIds.Add(state.Id);
                        _graph.States.RemoveAt(stateIndex);
                    }
                }

                for (int transitionIndex = _graph.Transitions.Count - 1; transitionIndex >= 0; --transitionIndex)
                {
                    FusionAnimatorTransitionDefinition transition = _graph.Transitions[transitionIndex];
                    if (transition == null)
                    {
                        _graph.Transitions.RemoveAt(transitionIndex);
                        continue;
                    }

                    if (removedStateIds.Contains(transition.FromStateId) || removedStateIds.Contains(transition.ToStateId))
                    {
                        _graph.Transitions.RemoveAt(transitionIndex);
                    }
                }

                _graph.Layers.RemoveAt(_selectedLayerIndex);
                _selectedLayerIndex = -1;
                _selectedParameterIndex = -1;
                _selectedBindingIndex = -1;
                _selectedState = null;
                _selectedTransition = null;
                if (string.Equals(_activeLayerId, removedLayerId, StringComparison.Ordinal))
                {
                    SetActiveLayer(string.Empty);
                }

                NormalizeLayerPriorities();
                _graphView?.RebuildFromGraphData();
                _inspector?.MarkDirtyRepaint();
                MarkGraphDirty();
            }

            return true;
        }

        private bool TryRemoveSelectedBindingFromLibrary()
        {
            if ((_selectedBindingIndex < 0 || _selectedBindingIndex >= (_graph?.ClipBindings?.Count ?? 0)) &&
                _selectedBindingIndices.Count > 0)
            {
                _selectedBindingIndex = ResolveFirstSelectedIndex(_selectedBindingIndices);
            }

            if (_graph?.ClipBindings == null ||
                _selectedBindingIndex < 0 ||
                _selectedBindingIndex >= _graph.ClipBindings.Count)
            {
                return false;
            }

            CancelReorderDrag();
            FusionAnimatorClipBindingDefinition bindingToRemove = _graph.ClipBindings[_selectedBindingIndex];
            string bindingName = bindingToRemove != null && string.IsNullOrWhiteSpace(bindingToRemove.Name) == false ? bindingToRemove.Name : "Binding";
            bool confirmed = EditorUtility.DisplayDialog(
                "Remove Binding",
                string.Format("Remove binding '{0}'?", bindingName),
                "Remove",
                "Cancel");
            if (confirmed)
            {
                RecordUndo("Remove FusionAnimator Clip Binding");
                _graph.ClipBindings.RemoveAt(_selectedBindingIndex);
                _selectedBindingIndex = -1;
                _selectedBindingIndices.Clear();
                _bindingSelectionAnchorIndex = -1;
                _selectedBindingGroupId = string.Empty;
                _inspector?.MarkDirtyRepaint();
                MarkGraphDirty();
            }

            return true;
        }

        private void HandleLibraryClipboardCommands(Event evt)
        {
            if (evt == null || _graph == null)
            {
                return;
            }

            if (EditorGUIUtility.editingTextField)
            {
                return;
            }

            if (evt.type == EventType.ValidateCommand || evt.type == EventType.ExecuteCommand)
            {
                bool isExecute = evt.type == EventType.ExecuteCommand;
                if (string.Equals(evt.commandName, "Copy", StringComparison.Ordinal))
                {
                    if (CanCopyCurrentLibrarySelection())
                    {
                        if (isExecute)
                        {
                            ExecuteCopyCurrentLibrarySelection();
                        }

                        evt.Use();
                    }

                    return;
                }

                if (string.Equals(evt.commandName, "Paste", StringComparison.Ordinal))
                {
                    if (CanPasteIntoCurrentLibraryTab())
                    {
                        if (isExecute)
                        {
                            ExecutePasteIntoCurrentLibraryTab();
                        }

                        evt.Use();
                    }

                    return;
                }
            }

            if (evt.type == EventType.KeyDown && (evt.control || evt.command) && evt.alt == false)
            {
                if (evt.keyCode == KeyCode.C && CanCopyCurrentLibrarySelection())
                {
                    ExecuteCopyCurrentLibrarySelection();
                    evt.Use();
                    return;
                }

                if (evt.keyCode == KeyCode.V && CanPasteIntoCurrentLibraryTab())
                {
                    ExecutePasteIntoCurrentLibraryTab();
                    evt.Use();
                }
            }
        }

        private bool CanCopyCurrentLibrarySelection()
        {
            switch (_leftLibraryTab)
            {
                case LeftLibraryTab.Parameters:
                    return _graph?.Parameters != null &&
                           _selectedParameterIndex >= 0 &&
                           _selectedParameterIndex < _graph.Parameters.Count &&
                           _graph.Parameters[_selectedParameterIndex] != null;
                case LeftLibraryTab.Bindings:
                    return _graph?.ClipBindings != null &&
                           _selectedBindingIndex >= 0 &&
                           _selectedBindingIndex < _graph.ClipBindings.Count &&
                           _graph.ClipBindings[_selectedBindingIndex] != null;
                default:
                    return false;
            }
        }

        private bool CanPasteIntoCurrentLibraryTab()
        {
            switch (_leftLibraryTab)
            {
                case LeftLibraryTab.Parameters:
                    return CanPasteParameterFromClipboard();
                case LeftLibraryTab.Bindings:
                    return CanPasteBindingFromClipboard();
                default:
                    return false;
            }
        }

        private void ExecuteCopyCurrentLibrarySelection()
        {
            if (_graph == null)
            {
                return;
            }

            if (_leftLibraryTab == LeftLibraryTab.Parameters &&
                _graph.Parameters != null &&
                _selectedParameterIndex >= 0 &&
                _selectedParameterIndex < _graph.Parameters.Count)
            {
                CopyParameterToClipboard(_graph.Parameters[_selectedParameterIndex]);
                return;
            }

            if (_leftLibraryTab == LeftLibraryTab.Bindings &&
                _graph.ClipBindings != null &&
                _selectedBindingIndex >= 0 &&
                _selectedBindingIndex < _graph.ClipBindings.Count)
            {
                CopyBindingToClipboard(_graph.ClipBindings[_selectedBindingIndex]);
            }
        }

        private void ExecutePasteIntoCurrentLibraryTab()
        {
            switch (_leftLibraryTab)
            {
                case LeftLibraryTab.Parameters:
                {
                    int insertIndex = _selectedParameterIndex >= 0 ? _selectedParameterIndex + 1 : (_graph?.Parameters?.Count ?? 0);
                    PasteParameterFromClipboard(insertIndex);
                    break;
                }
                case LeftLibraryTab.Bindings:
                {
                    int insertIndex = _selectedBindingIndex >= 0 ? _selectedBindingIndex + 1 : (_graph?.ClipBindings?.Count ?? 0);
                    PasteBindingFromClipboard(insertIndex);
                    break;
                }
            }
        }

        private void ShowParameterContextMenu(int parameterIndex)
        {
            GenericMenu menu = new GenericMenu();
            bool canCopy = _graph?.Parameters != null &&
                           parameterIndex >= 0 &&
                           parameterIndex < _graph.Parameters.Count &&
                           _graph.Parameters[parameterIndex] != null;
            if (canCopy)
            {
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    if (_graph?.Parameters == null ||
                        parameterIndex < 0 ||
                        parameterIndex >= _graph.Parameters.Count)
                    {
                        return;
                    }

                    CopyParameterToClipboard(_graph.Parameters[parameterIndex]);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Copy"));
            }

            if (CanPasteParameterFromClipboard())
            {
                menu.AddItem(new GUIContent("Paste"), false, () =>
                {
                    _leftLibraryTab = LeftLibraryTab.Parameters;
                    int insertIndex = parameterIndex >= 0 ? parameterIndex + 1 : (_graph?.Parameters?.Count ?? 0);
                    PasteParameterFromClipboard(insertIndex);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste"));
            }

            menu.ShowAsContext();
        }

        private void ShowBindingContextMenu(int bindingIndex)
        {
            GenericMenu menu = new GenericMenu();
            bool canCopy = _graph?.ClipBindings != null &&
                           bindingIndex >= 0 &&
                           bindingIndex < _graph.ClipBindings.Count &&
                           _graph.ClipBindings[bindingIndex] != null;
            if (canCopy)
            {
                menu.AddItem(new GUIContent("Copy"), false, () =>
                {
                    if (_graph?.ClipBindings == null ||
                        bindingIndex < 0 ||
                        bindingIndex >= _graph.ClipBindings.Count)
                    {
                        return;
                    }

                    CopyBindingToClipboard(_graph.ClipBindings[bindingIndex]);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Copy"));
            }

            if (CanPasteBindingFromClipboard())
            {
                menu.AddItem(new GUIContent("Paste"), false, () =>
                {
                    _leftLibraryTab = LeftLibraryTab.Bindings;
                    int insertIndex = bindingIndex >= 0 ? bindingIndex + 1 : (_graph?.ClipBindings?.Count ?? 0);
                    PasteBindingFromClipboard(insertIndex);
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Paste"));
            }

            bool canCreateGroup = _graph?.ClipBindings != null && _graph.ClipBindings.Count > 0;
            menu.AddSeparator(string.Empty);
            if (canCreateGroup)
            {
                menu.AddItem(new GUIContent("Create Group From Selection"), false, () =>
                {
                    if (_graph == null || _graph.ClipBindings == null || _graph.ClipBindings.Count == 0)
                    {
                        return;
                    }

                    if (_selectedBindingIndices.Count == 0 &&
                        bindingIndex >= 0 &&
                        bindingIndex < _graph.ClipBindings.Count)
                    {
                        _selectedBindingIndices.Clear();
                        _selectedBindingIndices.Add(bindingIndex);
                        _selectedBindingIndex = bindingIndex;
                        _bindingSelectionAnchorIndex = bindingIndex;
                    }

                    CreateBindingGroup();
                });
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("Create Group From Selection"));
            }

            menu.ShowAsContext();
        }

        private void CreateBindingGroup()
        {
            if (_graph == null)
            {
                return;
            }

            EnsureGraphCollections();
            List<int> selectedBindingIndices = GetSelectedBindingIndicesForGrouping();

            RecordUndo("Add FusionAnimator Binding Group");
            int groupNumber = (_graph.BindingGroups != null ? _graph.BindingGroups.Count : 0) + 1;
            FusionAnimatorBindingGroupDefinition group = new FusionAnimatorBindingGroupDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("group"),
                Name = string.Format("Group {0}", groupNumber),
            };
            _graph.BindingGroups.Add(group);
            _bindingGroupFoldoutStates[group.Id] = true;

            if (_graph.ClipBindings != null && selectedBindingIndices.Count > 0)
            {
                for (int i = 0; i < selectedBindingIndices.Count; ++i)
                {
                    int bindingIndex = selectedBindingIndices[i];
                    if (bindingIndex < 0 || bindingIndex >= _graph.ClipBindings.Count)
                    {
                        continue;
                    }

                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[bindingIndex];
                    if (binding != null)
                    {
                        binding.GroupId = group.Id;
                    }
                }
            }

            _selectedBindingGroupId = group.Id;
            _selectedBindingIndex = -1;
            _selectedBindingIndices.Clear();
            _bindingSelectionAnchorIndex = -1;
            _selectedParameterIndices.Clear();
            _selectedLayerIndices.Clear();
            _selectedParameterIndex = -1;
            _selectedLayerIndex = -1;
            _selectedState = null;
            _selectedTransition = null;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(null);
            MarkGraphDirty();
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private FusionAnimatorBindingGroupDefinition FindBindingGroupById(string groupId)
        {
            if (_graph?.BindingGroups == null || string.IsNullOrWhiteSpace(groupId))
            {
                return null;
            }

            for (int i = 0; i < _graph.BindingGroups.Count; ++i)
            {
                FusionAnimatorBindingGroupDefinition group = _graph.BindingGroups[i];
                if (group != null && string.Equals(group.Id, groupId, StringComparison.Ordinal))
                {
                    return group;
                }
            }

            return null;
        }

        private List<int> GetSelectedBindingIndicesForGrouping()
        {
            List<int> selected = new List<int>();
            if (_graph?.ClipBindings == null)
            {
                return selected;
            }

            if (_selectedBindingIndices.Count > 0)
            {
                foreach (int index in _selectedBindingIndices)
                {
                    if (index >= 0 && index < _graph.ClipBindings.Count)
                    {
                        selected.Add(index);
                    }
                }
            }
            else if (_selectedBindingIndex >= 0 && _selectedBindingIndex < _graph.ClipBindings.Count)
            {
                selected.Add(_selectedBindingIndex);
            }

            if (string.IsNullOrWhiteSpace(_bindingSearch) == false)
            {
                string filter = _bindingSearch.Trim();
                selected.RemoveAll(index =>
                {
                    if (index < 0 || index >= _graph.ClipBindings.Count)
                    {
                        return true;
                    }

                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[index];
                    if (binding == null)
                    {
                        return true;
                    }

                    string optionClipNames = string.Empty;
                    if (binding.Clips != null)
                    {
                        for (int optionIndex = 0; optionIndex < binding.Clips.Count; ++optionIndex)
                        {
                            FusionAnimatorClipBindingSlot option = binding.Clips[optionIndex];
                            if (option?.Clip == null)
                            {
                                continue;
                            }

                            if (optionClipNames.Length > 0)
                            {
                                optionClipNames += " ";
                            }

                            optionClipNames += option.Clip.name;
                        }
                    }

                    string combined = string.Format("{0} {1} {2}", binding.Name, binding.Id, optionClipNames);
                    return combined.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0;
                });
            }

            selected.Sort();
            return selected;
        }

        private void ShowBindingGroupContextMenu(string groupId)
        {
            if (_graph?.BindingGroups == null || string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            int groupIndex = -1;
            FusionAnimatorBindingGroupDefinition group = null;
            for (int i = 0; i < _graph.BindingGroups.Count; ++i)
            {
                FusionAnimatorBindingGroupDefinition candidate = _graph.BindingGroups[i];
                if (candidate != null && string.Equals(candidate.Id, groupId, StringComparison.Ordinal))
                {
                    groupIndex = i;
                    group = candidate;
                    break;
                }
            }

            if (groupIndex < 0 || group == null)
            {
                return;
            }

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Add Binding"), false, () =>
            {
                CancelReorderDrag();
                RecordUndo("Add FusionAnimator Clip Binding");
                FusionAnimatorClipBindingDefinition binding = new FusionAnimatorClipBindingDefinition
                {
                    Id = FusionAnimatorGraphAsset.NewId("binding"),
                    Name = "Binding",
                    GroupId = groupId,
                };
                _graph.ClipBindings.Add(binding);
                _selectedBindingIndex = _graph.ClipBindings.Count - 1;
                _selectedBindingIndices.Clear();
                _selectedBindingIndices.Add(_selectedBindingIndex);
                _bindingSelectionAnchorIndex = _selectedBindingIndex;
                _selectedBindingGroupId = string.Empty;
                _selectedParameterIndices.Clear();
                _selectedLayerIndices.Clear();
                _selectedParameterIndex = -1;
                _selectedLayerIndex = -1;
                _selectedState = null;
                _selectedTransition = null;
                _selectedLayerScopePath = string.Empty;
                _selectedEntryLinkTargetStateId = string.Empty;
                _graphView?.SetHoveredLayer(null);
                _graphView?.SetHoveredParameter(null);
                _inspector?.MarkDirtyRepaint();
                MarkGraphDirty();
            });
            menu.AddSeparator(string.Empty);
            menu.AddItem(new GUIContent("Delete Group"), false, () =>
            {
                RemoveBindingGroup(groupId);
            });
            menu.ShowAsContext();
        }

        private void RemoveBindingGroup(string groupId)
        {
            if (_graph?.BindingGroups == null || string.IsNullOrWhiteSpace(groupId))
            {
                return;
            }

            int groupIndex = -1;
            FusionAnimatorBindingGroupDefinition group = null;
            for (int i = 0; i < _graph.BindingGroups.Count; ++i)
            {
                FusionAnimatorBindingGroupDefinition candidate = _graph.BindingGroups[i];
                if (candidate != null && string.Equals(candidate.Id, groupId, StringComparison.Ordinal))
                {
                    groupIndex = i;
                    group = candidate;
                    break;
                }
            }

            if (groupIndex < 0 || group == null)
            {
                return;
            }

            string displayName = string.IsNullOrWhiteSpace(group.Name) ? "Group" : group.Name;
            bool confirmed = EditorUtility.DisplayDialog(
                "Delete Binding Group",
                string.Format("Delete binding group '{0}'?\nBindings in this group will be moved to ungrouped.", displayName),
                "Delete",
                "Cancel");
            if (confirmed == false)
            {
                return;
            }

            RecordUndo("Delete FusionAnimator Binding Group");
            _graph.BindingGroups.RemoveAt(groupIndex);
            if (_graph.ClipBindings != null)
            {
                for (int bindingIndex = 0; bindingIndex < _graph.ClipBindings.Count; ++bindingIndex)
                {
                    FusionAnimatorClipBindingDefinition binding = _graph.ClipBindings[bindingIndex];
                    if (binding != null && string.Equals(binding.GroupId, groupId, StringComparison.Ordinal))
                    {
                        binding.GroupId = string.Empty;
                    }
                }
            }

            _bindingGroupFoldoutStates.Remove(groupId);
            if (string.Equals(_dragBindingTargetGroupId, groupId, StringComparison.Ordinal))
            {
                _dragBindingTargetGroupId = string.Empty;
            }

            if (string.Equals(_selectedBindingGroupId, groupId, StringComparison.Ordinal))
            {
                _selectedBindingGroupId = string.Empty;
            }

            if (_dragBindingGroupIndex >= 0)
            {
                _dragBindingGroupIndex = -1;
                _dragBindingGroupTargetIndex = -1;
            }

            MarkGraphDirty();
            _leftPanel?.MarkDirtyRepaint();
            _inspector?.MarkDirtyRepaint();
        }

        private void CopyParameterToClipboard(FusionAnimatorParameterDefinition parameter)
        {
            if (parameter == null)
            {
                return;
            }

            FusionAnimatorParameterDefinition cloned = CloneParameterDefinition(parameter);
            _parameterClipboardCache = cloned;
            _parameterClipboardToken = FusionAnimatorGraphAsset.NewId("paramclip");
            ParameterClipboardPayload payload = new ParameterClipboardPayload
            {
                Token = _parameterClipboardToken,
                Parameter = CloneParameterDefinition(cloned),
            };
            EditorGUIUtility.systemCopyBuffer = ParameterClipboardPrefix + JsonUtility.ToJson(payload, false);
        }

        private void CopyBindingToClipboard(FusionAnimatorClipBindingDefinition binding)
        {
            if (binding == null)
            {
                return;
            }

            FusionAnimatorClipBindingDefinition cloned = CloneBindingDefinition(binding);
            _bindingClipboardCache = cloned;
            _bindingClipboardToken = FusionAnimatorGraphAsset.NewId("bindclip");
            BindingClipboardPayload payload = new BindingClipboardPayload
            {
                Token = _bindingClipboardToken,
                Binding = CloneBindingDefinition(cloned),
            };
            EditorGUIUtility.systemCopyBuffer = BindingClipboardPrefix + JsonUtility.ToJson(payload, false);
        }

        private bool CanPasteParameterFromClipboard()
        {
            return TryReadParameterFromClipboard(out _);
        }

        private bool CanPasteBindingFromClipboard()
        {
            return TryReadBindingFromClipboard(out _);
        }

        private bool PasteParameterFromClipboard(int insertIndex)
        {
            if (_graph == null || TryReadParameterFromClipboard(out FusionAnimatorParameterDefinition copied) == false)
            {
                return false;
            }

            EnsureGraphCollections();
            RecordUndo("Paste FusionAnimator Parameter");
            copied.Id = BuildUniqueParameterId(copied.Id);
            if (string.IsNullOrWhiteSpace(copied.Name))
            {
                copied.Name = "Parameter";
            }

            int clampedInsert = Mathf.Clamp(insertIndex, 0, _graph.Parameters.Count);
            _graph.Parameters.Insert(clampedInsert, copied);
            CancelReorderDrag();
            ClearGraphSelectionForLibraryInteraction();
            _selectedParameterIndex = clampedInsert;
            _selectedBindingIndex = -1;
            _selectedLayerIndex = -1;
            _selectedState = null;
            _selectedTransition = null;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(copied.Id);
            _inspector?.MarkDirtyRepaint();
            MarkGraphDirty();
            return true;
        }

        private bool PasteBindingFromClipboard(int insertIndex)
        {
            if (_graph == null || TryReadBindingFromClipboard(out FusionAnimatorClipBindingDefinition copied) == false)
            {
                return false;
            }

            EnsureGraphCollections();
            RecordUndo("Paste FusionAnimator Clip Binding");
            copied.Id = BuildUniqueBindingId(copied.Id);
            if (string.IsNullOrWhiteSpace(copied.Name))
            {
                copied.Name = "Binding";
            }

            int clampedInsert = Mathf.Clamp(insertIndex, 0, _graph.ClipBindings.Count);
            _graph.ClipBindings.Insert(clampedInsert, copied);
            CancelReorderDrag();
            ClearGraphSelectionForLibraryInteraction();
            _selectedBindingIndex = clampedInsert;
            _selectedBindingIndices.Clear();
            _selectedBindingIndices.Add(_selectedBindingIndex);
            _bindingSelectionAnchorIndex = _selectedBindingIndex;
            _selectedBindingGroupId = string.Empty;
            _selectedParameterIndex = -1;
            _selectedLayerIndex = -1;
            _selectedState = null;
            _selectedTransition = null;
            _selectedLayerScopePath = string.Empty;
            _selectedEntryLinkTargetStateId = string.Empty;
            _graphView?.SetHoveredLayer(null);
            _graphView?.SetHoveredParameter(null);
            _inspector?.MarkDirtyRepaint();
            MarkGraphDirty();
            return true;
        }

        private bool TryReadParameterFromClipboard(out FusionAnimatorParameterDefinition parameter)
        {
            parameter = null;
            string clipboard = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(clipboard) ||
                clipboard.StartsWith(ParameterClipboardPrefix, StringComparison.Ordinal) == false)
            {
                return false;
            }

            string payloadJson = clipboard.Substring(ParameterClipboardPrefix.Length);
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            try
            {
                ParameterClipboardPayload payload = JsonUtility.FromJson<ParameterClipboardPayload>(payloadJson);
                if (payload == null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(payload.Token) == false &&
                    string.Equals(payload.Token, _parameterClipboardToken, StringComparison.Ordinal) &&
                    _parameterClipboardCache != null)
                {
                    parameter = CloneParameterDefinition(_parameterClipboardCache);
                }
                else if (payload.Parameter != null)
                {
                    parameter = CloneParameterDefinition(payload.Parameter);
                }
            }
            catch (ArgumentException)
            {
                return false;
            }

            return parameter != null;
        }

        private bool TryReadBindingFromClipboard(out FusionAnimatorClipBindingDefinition binding)
        {
            binding = null;
            string clipboard = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(clipboard) ||
                clipboard.StartsWith(BindingClipboardPrefix, StringComparison.Ordinal) == false)
            {
                return false;
            }

            string payloadJson = clipboard.Substring(BindingClipboardPrefix.Length);
            if (string.IsNullOrWhiteSpace(payloadJson))
            {
                return false;
            }

            try
            {
                BindingClipboardPayload payload = JsonUtility.FromJson<BindingClipboardPayload>(payloadJson);
                if (payload == null)
                {
                    return false;
                }

                if (string.IsNullOrWhiteSpace(payload.Token) == false &&
                    string.Equals(payload.Token, _bindingClipboardToken, StringComparison.Ordinal) &&
                    _bindingClipboardCache != null)
                {
                    binding = CloneBindingDefinition(_bindingClipboardCache);
                }
                else if (payload.Binding != null)
                {
                    binding = CloneBindingDefinition(payload.Binding);
                }
            }
            catch (ArgumentException)
            {
                return false;
            }

            return binding != null;
        }

        private string BuildUniqueParameterId(string sourceId)
        {
            string prefix = ResolveIdPrefix(sourceId, "param");
            return FusionAnimatorGraphAsset.NewId(prefix);
        }

        private string BuildUniqueBindingId(string sourceId)
        {
            string prefix = ResolveIdPrefix(sourceId, "binding");
            return FusionAnimatorGraphAsset.NewId(prefix);
        }

        private static string ResolveIdPrefix(string sourceId, string fallbackPrefix)
        {
            if (string.IsNullOrWhiteSpace(sourceId))
            {
                return fallbackPrefix;
            }

            string trimmed = sourceId.Trim();
            int separatorIndex = trimmed.IndexOf('_');
            string candidate = separatorIndex > 0 ? trimmed.Substring(0, separatorIndex) : trimmed;
            if (string.IsNullOrWhiteSpace(candidate))
            {
                return fallbackPrefix;
            }

            StringBuilder builder = new StringBuilder(candidate.Length);
            for (int i = 0; i < candidate.Length; ++i)
            {
                char c = candidate[i];
                if (char.IsLetterOrDigit(c))
                {
                    builder.Append(char.ToLowerInvariant(c));
                }
            }

            return builder.Length > 0 ? builder.ToString() : fallbackPrefix;
        }

        private static FusionAnimatorParameterDefinition CloneParameterDefinition(FusionAnimatorParameterDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            return new FusionAnimatorParameterDefinition
            {
                Id = source.Id,
                Name = source.Name,
                Type = source.Type,
                DefaultBool = source.DefaultBool,
                Invert = source.Invert,
                DefaultInt = source.DefaultInt,
                DefaultFloat = source.DefaultFloat,
                DefaultVector2 = source.DefaultVector2,
                PreviewInputBinding = source.PreviewInputBinding,
                PreviewInputScale = source.PreviewInputScale,
                PreviewBoolInputSource = source.PreviewBoolInputSource,
                PreviewBoolInputOperator = source.PreviewBoolInputOperator,
                PreviewBoolInputCompareValue = source.PreviewBoolInputCompareValue,
            };
        }

        private static FusionAnimatorClipBindingDefinition CloneBindingDefinition(FusionAnimatorClipBindingDefinition source)
        {
            if (source == null)
            {
                return null;
            }

            FusionAnimatorClipBindingDefinition clone = new FusionAnimatorClipBindingDefinition
            {
                Id = source.Id,
                Name = source.Name,
                GroupId = source.GroupId,
                ClipIndexParameterId = source.ClipIndexParameterId,
                Conditions = CloneConditionList(source.Conditions),
                Clips = new List<FusionAnimatorClipBindingSlot>(),
            };

            if (source.Clips != null)
            {
                for (int i = 0; i < source.Clips.Count; ++i)
                {
                    FusionAnimatorClipBindingSlot slot = source.Clips[i];
                    if (slot == null)
                    {
                        clone.Clips.Add(null);
                        continue;
                    }

                    clone.Clips.Add(new FusionAnimatorClipBindingSlot
                    {
                        Slot = slot.Slot,
                        Clip = slot.Clip,
                        Speed = slot.Speed,
                        Loop = slot.Loop,
                        Conditions = CloneConditionList(slot.Conditions),
                    });
                }
            }

            return clone;
        }

        private static List<FusionAnimatorConditionDefinition> CloneConditionList(List<FusionAnimatorConditionDefinition> source)
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
                    clone.Add(null);
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

        private void EnsureGraphCollections()
        {
            if (_graph == null)
            {
                return;
            }

            if (_graph.Parameters == null)
            {
                _graph.Parameters = new List<FusionAnimatorParameterDefinition>();
            }

            if (_graph.BindingGroups == null)
            {
                _graph.BindingGroups = new List<FusionAnimatorBindingGroupDefinition>();
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


