using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FusionAnimator.Editor
{
    public sealed class FusionAnimatorWindow : EditorWindow
    {
        private enum DataView
        {
            Graph = 0,
            Parameters = 1,
            Layers = 2,
            States = 3,
            Transitions = 4,
            Validation = 5,
        }

        private FusionAnimatorGraphAsset _graph;
        private DataView _view = DataView.Graph;
        private Vector2 _leftScroll;
        private Vector2 _rightScroll;

        private int _selectedParameter = -1;
        private int _selectedLayer = -1;
        private int _selectedState = -1;
        private int _selectedTransition = -1;

        private List<FusionAnimatorValidationIssue> _issues = new List<FusionAnimatorValidationIssue>();

        [MenuItem("Tools/Fusion/Fusion Animator", false, 250)]
        public static void Open()
        {
            FusionAnimatorWindow window = GetWindow<FusionAnimatorWindow>();
            window.titleContent = new GUIContent("FusionAnimator");
            window.minSize = new Vector2(980.0f, 560.0f);
            window.Show();
        }

        public static void Open(FusionAnimatorGraphAsset graph)
        {
            Open();
            FusionAnimatorWindow window = GetWindow<FusionAnimatorWindow>();
            window.SetGraph(graph);
        }

        private void OnSelectionChange()
        {
            if (_graph == null && Selection.activeObject is FusionAnimatorGraphAsset selectedGraph)
            {
                SetGraph(selectedGraph);
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();

            if (_graph == null)
            {
                EditorGUILayout.HelpBox("Select or create a FusionAnimator graph asset.", MessageType.Info);
                return;
            }

            EnsureCollections();

            EditorGUILayout.BeginHorizontal();
            DrawLeftPanel();
            DrawRightPanel();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            FusionAnimatorGraphAsset newGraph = EditorGUILayout.ObjectField(_graph, typeof(FusionAnimatorGraphAsset), false, GUILayout.Width(360.0f)) as FusionAnimatorGraphAsset;
            if (newGraph != _graph)
            {
                SetGraph(newGraph);
            }

            if (GUILayout.Button("Create Graph", EditorStyles.toolbarButton, GUILayout.Width(90.0f)))
            {
                CreateGraphAsset();
            }

            GUI.enabled = _graph != null;
            if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(70.0f)))
            {
                _issues = FusionAnimatorValidator.Validate(_graph);
                _view = DataView.Validation;
            }

            if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(50.0f)))
            {
                SaveGraph();
            }

            if (GUILayout.Button("Canvas", EditorStyles.toolbarButton, GUILayout.Width(60.0f)))
            {
                FusionAnimatorGraphCanvasWindow.Open(_graph);
            }

            GUI.enabled = true;
            GUILayout.FlexibleSpace();

            if (_graph != null)
            {
                GUILayout.Label(_graph.DisplayName, EditorStyles.miniBoldLabel);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawLeftPanel()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(360.0f));
            _view = (DataView)GUILayout.Toolbar((int)_view, new[] { "Graph", "Params", "Layers", "States", "Transitions", "Validation" });

            _leftScroll = EditorGUILayout.BeginScrollView(_leftScroll);

            switch (_view)
            {
                case DataView.Graph:
                    DrawGraphSummary();
                    break;
                case DataView.Parameters:
                    DrawParameterList();
                    break;
                case DataView.Layers:
                    DrawLayerList();
                    break;
                case DataView.States:
                    DrawStateList();
                    break;
                case DataView.Transitions:
                    DrawTransitionList();
                    break;
                case DataView.Validation:
                    DrawValidationList();
                    break;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawRightPanel()
        {
            EditorGUILayout.BeginVertical();
            _rightScroll = EditorGUILayout.BeginScrollView(_rightScroll);

            switch (_view)
            {
                case DataView.Graph:
                    DrawGraphInspector();
                    break;
                case DataView.Parameters:
                    DrawSelectedParameterInspector();
                    break;
                case DataView.Layers:
                    DrawSelectedLayerInspector();
                    break;
                case DataView.States:
                    DrawSelectedStateInspector();
                    break;
                case DataView.Transitions:
                    DrawSelectedTransitionInspector();
                    break;
                case DataView.Validation:
                    DrawValidationInspector();
                    break;
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawGraphSummary()
        {
            EditorGUILayout.LabelField("Graph Summary", EditorStyles.boldLabel);
            EditorGUILayout.LabelField("Parameters", _graph.Parameters.Count.ToString());
            EditorGUILayout.LabelField("Layers", _graph.Layers.Count.ToString());
            EditorGUILayout.LabelField("States", _graph.States.Count.ToString());
            EditorGUILayout.LabelField("Transitions", _graph.Transitions.Count.ToString());
        }

        private void DrawGraphInspector()
        {
            EditorGUILayout.LabelField("Graph", EditorStyles.boldLabel);
            _graph.DisplayName = EditorGUILayout.TextField("Display Name", _graph.DisplayName);
            _graph.GraphId = EditorGUILayout.TextField("Graph Id", _graph.GraphId);

            using (new EditorGUILayout.HorizontalScope())
            {
                _graph.EntryStateId = EditorGUILayout.TextField("Entry State Id", _graph.EntryStateId);
                if (GUILayout.Button("Pick", GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    for (int i = 0, count = _graph.States.Count; i < count; ++i)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[i];
                        string id = state != null ? state.Id : string.Empty;
                        string label = state != null ? string.Format("{0} ({1})", state.Name, state.Id) : "<null>";
                        menu.AddItem(new GUIContent(label), id == _graph.EntryStateId, () =>
                        {
                            _graph.EntryStateId = id;
                            MarkGraphDirty();
                            Repaint();
                        });
                    }

                    menu.ShowAsContext();
                }
            }

            MarkGraphDirty();
        }

        private void DrawParameterList()
        {
            EditorGUILayout.LabelField("Parameters", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Parameter"))
            {
                FusionAnimatorParameterDefinition parameter = new FusionAnimatorParameterDefinition();
                parameter.Id = FusionAnimatorGraphAsset.NewId("param");
                parameter.Name = "Parameter";
                _graph.Parameters.Add(parameter);
                _selectedParameter = _graph.Parameters.Count - 1;
                MarkGraphDirty();
            }

            DrawRemovableList(
                _graph.Parameters.Count,
                index => GetParameterDisplayName(index),
                index => _selectedParameter = index,
                index => _selectedParameter == index,
                index =>
                {
                    _graph.Parameters.RemoveAt(index);
                    _selectedParameter = Mathf.Clamp(_selectedParameter - 1, -1, _graph.Parameters.Count - 1);
                    MarkGraphDirty();
                });
        }

        private void DrawLayerList()
        {
            EditorGUILayout.LabelField("Layers", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Layer"))
            {
                FusionAnimatorLayerDefinition layer = new FusionAnimatorLayerDefinition();
                layer.Id = FusionAnimatorGraphAsset.NewId("layer");
                layer.Name = "Layer";
                layer.DefaultWeight = 1.0f;
                _graph.Layers.Add(layer);
                _selectedLayer = _graph.Layers.Count - 1;
                MarkGraphDirty();
            }

            DrawRemovableList(
                _graph.Layers.Count,
                index => GetLayerDisplayName(index),
                index => _selectedLayer = index,
                index => _selectedLayer == index,
                index =>
                {
                    _graph.Layers.RemoveAt(index);
                    _selectedLayer = Mathf.Clamp(_selectedLayer - 1, -1, _graph.Layers.Count - 1);
                    MarkGraphDirty();
                });
        }

        private void DrawStateList()
        {
            EditorGUILayout.LabelField("States", EditorStyles.boldLabel);
            if (GUILayout.Button("Add State"))
            {
                FusionAnimatorStateDefinition state = new FusionAnimatorStateDefinition();
                state.Id = FusionAnimatorGraphAsset.NewId("state");
                state.Name = "State";
                if (_graph.Layers.Count > 0 && _graph.Layers[0] != null)
                {
                    state.LayerId = _graph.Layers[0].Id;
                }

                _graph.States.Add(state);
                _selectedState = _graph.States.Count - 1;
                MarkGraphDirty();
            }

            DrawRemovableList(
                _graph.States.Count,
                index => GetStateDisplayName(index),
                index => _selectedState = index,
                index => _selectedState == index,
                index =>
                {
                    _graph.States.RemoveAt(index);
                    _selectedState = Mathf.Clamp(_selectedState - 1, -1, _graph.States.Count - 1);
                    MarkGraphDirty();
                });
        }

        private void DrawTransitionList()
        {
            EditorGUILayout.LabelField("Transitions", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Transition"))
            {
                FusionAnimatorTransitionDefinition transition = new FusionAnimatorTransitionDefinition();
                transition.Id = FusionAnimatorGraphAsset.NewId("transition");
                transition.Name = "Transition";

                if (_graph.States.Count > 0 && _graph.States[0] != null)
                {
                    transition.FromStateId = _graph.States[0].Id;
                }

                if (_graph.States.Count > 1 && _graph.States[1] != null)
                {
                    transition.ToStateId = _graph.States[1].Id;
                }
                else
                {
                    transition.ToStateId = transition.FromStateId;
                }

                _graph.Transitions.Add(transition);
                _selectedTransition = _graph.Transitions.Count - 1;
                MarkGraphDirty();
            }

            DrawRemovableList(
                _graph.Transitions.Count,
                index => GetTransitionDisplayName(index),
                index => _selectedTransition = index,
                index => _selectedTransition == index,
                index =>
                {
                    _graph.Transitions.RemoveAt(index);
                    _selectedTransition = Mathf.Clamp(_selectedTransition - 1, -1, _graph.Transitions.Count - 1);
                    MarkGraphDirty();
                });
        }

        private void DrawValidationList()
        {
            EditorGUILayout.LabelField("Validation", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox("Click Validate in the toolbar to refresh issues.", MessageType.None);

            if (_issues == null || _issues.Count == 0)
            {
                EditorGUILayout.LabelField("No validation results.");
                return;
            }

            for (int i = 0, count = _issues.Count; i < count; ++i)
            {
                FusionAnimatorValidationIssue issue = _issues[i];
                MessageType messageType = ToMessageType(issue.Severity);
                EditorGUILayout.HelpBox(string.Format("{0}: {1}", issue.Context, issue.Message), messageType);
            }
        }

        private void DrawSelectedParameterInspector()
        {
            if (!TryGetSelected(_graph.Parameters, _selectedParameter, out FusionAnimatorParameterDefinition parameter))
            {
                EditorGUILayout.HelpBox("Select a parameter.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Parameter", EditorStyles.boldLabel);
            parameter.Id = EditorGUILayout.TextField("Id", parameter.Id);
            parameter.Name = EditorGUILayout.TextField("Name", parameter.Name);
            parameter.Type = (FusionAnimatorParameterType)EditorGUILayout.EnumPopup("Type", parameter.Type);

            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                case FusionAnimatorParameterType.Trigger:
                    parameter.DefaultBool = EditorGUILayout.Toggle("Default Bool", parameter.DefaultBool);
                    if (parameter.Type == FusionAnimatorParameterType.Bool)
                    {
                        parameter.Invert = EditorGUILayout.Toggle("Invert Input", parameter.Invert);
                    }
                    break;
                case FusionAnimatorParameterType.Int:
                    parameter.DefaultInt = EditorGUILayout.IntField("Default Int", parameter.DefaultInt);
                    break;
                case FusionAnimatorParameterType.Float:
                    parameter.DefaultFloat = EditorGUILayout.FloatField("Default Float", parameter.DefaultFloat);
                    break;
            }

            MarkGraphDirty();
        }

        private void DrawSelectedLayerInspector()
        {
            if (!TryGetSelected(_graph.Layers, _selectedLayer, out FusionAnimatorLayerDefinition layer))
            {
                EditorGUILayout.HelpBox("Select a layer.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Layer", EditorStyles.boldLabel);
            layer.Id = EditorGUILayout.TextField("Id", layer.Id);
            layer.Name = EditorGUILayout.TextField("Name", layer.Name);
            layer.Priority = EditorGUILayout.IntField("Priority", layer.Priority);
            layer.DefaultWeight = EditorGUILayout.Slider("Default Weight", layer.DefaultWeight, 0.0f, 1.0f);
            layer.EnabledByDefault = EditorGUILayout.Toggle("Enabled By Default", layer.EnabledByDefault);
            layer.BlendMode = (FusionAnimatorLayerBlendMode)EditorGUILayout.EnumPopup("Blend Mode", layer.BlendMode);
            layer.AvatarMask = EditorGUILayout.ObjectField("Avatar Mask", layer.AvatarMask, typeof(AvatarMask), false) as AvatarMask;
            layer.SyncedLayerIndex = EditorGUILayout.IntField("Synced Layer Index", layer.SyncedLayerIndex);
            layer.SyncTiming = EditorGUILayout.Toggle("Sync Timing", layer.SyncTiming);
            layer.IKPass = EditorGUILayout.Toggle("IK Pass", layer.IKPass);

            MarkGraphDirty();
        }

        private void DrawSelectedStateInspector()
        {
            if (!TryGetSelected(_graph.States, _selectedState, out FusionAnimatorStateDefinition state))
            {
                EditorGUILayout.HelpBox("Select a state.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("State", EditorStyles.boldLabel);
            state.Id = EditorGUILayout.TextField("Id", state.Id);
            state.Name = EditorGUILayout.TextField("Name", state.Name);
            DrawLayerIdField("Layer Id", value => state.LayerId = value, state.LayerId);
            state.MinDurationSeconds = EditorGUILayout.FloatField(
                new GUIContent(
                    "Min Duration (Normalized)",
                    "Minimum normalized state time before exit transitions are eligible. Runtime seconds are resolved as (min duration * current clip/reference length)."),
                state.MinDurationSeconds);
            state.CanTransitionOut = EditorGUILayout.Toggle("Can Transition Out", state.CanTransitionOut);
            state.WriteDefaults = EditorGUILayout.Toggle("Write Defaults", state.WriteDefaults);

            EditorGUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Clips", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Clip Slot"))
            {
                state.Clips.Add(new FusionAnimatorClipSlot());
            }

            for (int i = 0; i < state.Clips.Count; ++i)
            {
                FusionAnimatorClipSlot clipSlot = state.Clips[i];
                if (clipSlot == null)
                {
                    continue;
                }

                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(string.Format("Clip Slot {0}", i), EditorStyles.miniBoldLabel);
                if (GUILayout.Button("Remove", GUILayout.Width(70.0f)))
                {
                    state.Clips.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndHorizontal();
                clipSlot.Slot = EditorGUILayout.TextField("Slot", clipSlot.Slot);
                clipSlot.Clip = EditorGUILayout.ObjectField("Clip", clipSlot.Clip, typeof(AnimationClip), false) as AnimationClip;
                clipSlot.Speed = EditorGUILayout.FloatField("Speed", clipSlot.Speed);
                clipSlot.Loop = EditorGUILayout.Toggle("Loop", clipSlot.Loop);
                EditorGUILayout.EndVertical();
            }

            MarkGraphDirty();
        }

        private void DrawSelectedTransitionInspector()
        {
            if (!TryGetSelected(_graph.Transitions, _selectedTransition, out FusionAnimatorTransitionDefinition transition))
            {
                EditorGUILayout.HelpBox("Select a transition.", MessageType.Info);
                return;
            }

            EditorGUILayout.LabelField("Transition", EditorStyles.boldLabel);
            transition.Id = EditorGUILayout.TextField("Id", transition.Id);
            transition.Name = EditorGUILayout.TextField("Name", transition.Name);
            DrawStateIdField("From State Id", value => transition.FromStateId = value, transition.FromStateId);
            DrawStateIdField("To State Id", value => transition.ToStateId = value, transition.ToStateId);
            transition.Priority = EditorGUILayout.IntField("Priority", transition.Priority);
            transition.BlendDurationSeconds = Mathf.Max(0.0f, EditorGUILayout.FloatField("Blend Duration", transition.BlendDurationSeconds));
            transition.CanInterrupt = EditorGUILayout.Toggle("Can Interrupt", transition.CanInterrupt);
            transition.InterruptionSource = (FusionAnimatorInterruptionSource)EditorGUILayout.EnumPopup("Interruption Source", transition.InterruptionSource);

            EditorGUILayout.Space(8.0f);
            EditorGUILayout.LabelField("Conditions", EditorStyles.boldLabel);
            if (GUILayout.Button("Add Condition"))
            {
                transition.Conditions.Add(new FusionAnimatorConditionDefinition());
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
                if (GUILayout.Button("Remove", GUILayout.Width(70.0f)))
                {
                    transition.Conditions.RemoveAt(i);
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.EndVertical();
                    break;
                }

                EditorGUILayout.EndHorizontal();
                DrawParameterIdField("Parameter Id", value => condition.ParameterId = value, condition.ParameterId);
                FusionAnimatorParameterDefinition conditionParameter = FindParameterById(condition.ParameterId);
                bool isTriggerCondition = conditionParameter != null && conditionParameter.Type == FusionAnimatorParameterType.Trigger;
                if (isTriggerCondition)
                {
                    condition.Operator = FusionAnimatorConditionOperator.IsTrue;
                    EditorGUILayout.LabelField("Operator", "Fired Once");
                }
                else
                {
                    condition.Operator = (FusionAnimatorConditionOperator)EditorGUILayout.EnumPopup("Operator", condition.Operator);
                }

                if (conditionParameter == null || conditionParameter.Type == FusionAnimatorParameterType.Bool)
                {
                    condition.BoolValue = EditorGUILayout.Toggle("Bool Value", condition.BoolValue);
                }
                else if (conditionParameter.Type == FusionAnimatorParameterType.Int)
                {
                    condition.IntValue = EditorGUILayout.IntField("Int Value", condition.IntValue);
                }
                else if (conditionParameter.Type == FusionAnimatorParameterType.Float || conditionParameter.Type == FusionAnimatorParameterType.Vector2)
                {
                    condition.FloatValue = EditorGUILayout.FloatField("Float Value", condition.FloatValue);
                }
                EditorGUILayout.EndVertical();
            }

            MarkGraphDirty();
        }

        private void DrawValidationInspector()
        {
            DrawValidationList();
        }

        private void DrawLayerIdField(string label, System.Action<string> assign, string current)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string newValue = EditorGUILayout.TextField(label, current);
                if (newValue != current)
                {
                    assign(newValue);
                }

                if (GUILayout.Button("Pick", GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    for (int i = 0, count = _graph.Layers.Count; i < count; ++i)
                    {
                        FusionAnimatorLayerDefinition layer = _graph.Layers[i];
                        if (layer == null)
                        {
                            continue;
                        }

                        string id = layer.Id;
                        string content = string.Format("{0} ({1})", layer.Name, layer.Id);
                        menu.AddItem(new GUIContent(content), id == current, () =>
                        {
                            assign(id);
                            MarkGraphDirty();
                            Repaint();
                        });
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawStateIdField(string label, System.Action<string> assign, string current)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string newValue = EditorGUILayout.TextField(label, current);
                if (newValue != current)
                {
                    assign(newValue);
                }

                if (GUILayout.Button("Pick", GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    for (int i = 0, count = _graph.States.Count; i < count; ++i)
                    {
                        FusionAnimatorStateDefinition state = _graph.States[i];
                        if (state == null)
                        {
                            continue;
                        }

                        string id = state.Id;
                        string content = string.Format("{0} ({1})", state.Name, state.Id);
                        menu.AddItem(new GUIContent(content), id == current, () =>
                        {
                            assign(id);
                            MarkGraphDirty();
                            Repaint();
                        });
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawParameterIdField(string label, System.Action<string> assign, string current)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                string newValue = EditorGUILayout.TextField(label, current);
                if (newValue != current)
                {
                    assign(newValue);
                }

                if (GUILayout.Button("Pick", GUILayout.Width(60.0f)))
                {
                    GenericMenu menu = new GenericMenu();
                    for (int i = 0, count = _graph.Parameters.Count; i < count; ++i)
                    {
                        FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                        if (parameter == null)
                        {
                            continue;
                        }

                        string id = parameter.Id;
                        string content = string.Format("{0} ({1})", parameter.Name, parameter.Id);
                        menu.AddItem(new GUIContent(content), id == current, () =>
                        {
                            assign(id);
                            MarkGraphDirty();
                            Repaint();
                        });
                    }

                    menu.ShowAsContext();
                }
            }
        }

        private void DrawRemovableList(
            int count,
            System.Func<int, string> getLabel,
            System.Action<int> onSelect,
            System.Func<int, bool> isSelected,
            System.Action<int> onRemove)
        {
            for (int i = 0; i < count; ++i)
            {
                EditorGUILayout.BeginHorizontal();
                GUIStyle style = isSelected(i) ? EditorStyles.toolbarButton : EditorStyles.miniButtonLeft;
                if (GUILayout.Button(getLabel(i), style))
                {
                    onSelect(i);
                }

                if (GUILayout.Button("X", GUILayout.Width(24.0f)))
                {
                    onRemove(i);
                    EditorGUILayout.EndHorizontal();
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }
        }

        private static bool TryGetSelected<T>(List<T> list, int index, out T value) where T : class
        {
            if (list != null && index >= 0 && index < list.Count)
            {
                value = list[index];
                return value != null;
            }

            value = null;
            return false;
        }

        private FusionAnimatorParameterDefinition FindParameterById(string parameterId)
        {
            if (_graph == null || _graph.Parameters == null || string.IsNullOrWhiteSpace(parameterId))
            {
                return null;
            }

            for (int i = 0; i < _graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = _graph.Parameters[i];
                if (parameter != null && parameter.Id == parameterId)
                {
                    return parameter;
                }
            }

            return null;
        }

        private string GetParameterDisplayName(int index)
        {
            FusionAnimatorParameterDefinition parameter = _graph.Parameters[index];
            return parameter == null ? "<null parameter>" : string.Format("{0} [{1}]", parameter.Name, parameter.Type);
        }

        private string GetLayerDisplayName(int index)
        {
            FusionAnimatorLayerDefinition layer = _graph.Layers[index];
            return layer == null ? "<null layer>" : string.Format("{0} [{1}]", layer.Name, layer.BlendMode);
        }

        private string GetStateDisplayName(int index)
        {
            FusionAnimatorStateDefinition state = _graph.States[index];
            return state == null ? "<null state>" : string.Format("{0} ({1})", state.Name, state.LayerId);
        }

        private string GetTransitionDisplayName(int index)
        {
            FusionAnimatorTransitionDefinition transition = _graph.Transitions[index];
            return transition == null
                ? "<null transition>"
                : string.Format("{0}: {1} -> {2}", transition.Name, transition.FromStateId, transition.ToStateId);
        }

        private static MessageType ToMessageType(FusionAnimatorValidationSeverity severity)
        {
            switch (severity)
            {
                case FusionAnimatorValidationSeverity.Error:
                    return MessageType.Error;
                case FusionAnimatorValidationSeverity.Warning:
                    return MessageType.Warning;
                default:
                    return MessageType.Info;
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

            SetGraph(graph);
            Selection.activeObject = graph;
        }

        private void SetGraph(FusionAnimatorGraphAsset graph)
        {
            _graph = graph;
            _selectedLayer = -1;
            _selectedParameter = -1;
            _selectedState = -1;
            _selectedTransition = -1;
            _issues.Clear();
        }

        private void EnsureCollections()
        {
            if (_graph.Parameters == null)
            {
                _graph.Parameters = new List<FusionAnimatorParameterDefinition>();
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
        }

        private void SaveGraph()
        {
            if (_graph == null)
            {
                return;
            }

            MarkGraphDirty();
            AssetDatabase.SaveAssets();
        }

        private void MarkGraphDirty()
        {
            if (_graph == null)
            {
                return;
            }

            EditorUtility.SetDirty(_graph);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            int separator = path.LastIndexOf('/');
            if (separator > 0)
            {
                string parent = path.Substring(0, separator);
                string child = path.Substring(separator + 1);
                EnsureFolder(parent);
                if (!AssetDatabase.IsValidFolder(path))
                {
                    AssetDatabase.CreateFolder(parent, child);
                }
            }
        }
    }
}

