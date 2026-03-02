using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace FusionAnimator.Editor
{
    internal sealed class FusionAgentToFusionConverter : IFusionAnimatorGraphConverter
    {
        private const string AnimationControllerTypeName = "Fusion.Addons.AnimationController.AnimationController";
        private const string AnimationLayerTypeName = "Fusion.Addons.AnimationController.AnimationLayer";
        private const string AnimationStateTypeName = "Fusion.Addons.AnimationController.AnimationState";
        private const string MultiClipStateTypeName = "Fusion.Addons.AnimationController.MultiClipState";
        private const string MultiBlendTreeStateTypeName = "Fusion.Addons.AnimationController.MultiBlendTreeState";
        private const string MirrorBlendTreeStateTypeName = "Fusion.Addons.AnimationController.MirrorBlendTreeState";
        private const string ClipStateTypeName = "Fusion.Addons.AnimationController.ClipState";
        private const string MixerStateTypeName = "Fusion.Addons.AnimationController.MixerState";

        private const string ParamWeaponSlotId = "param_weapon_slot";
        private const string ParamPendingWeaponSlotId = "param_pending_weapon_slot";
        private const string ParamMoveXId = "param_move_x";
        private const string ParamMoveYId = "param_move_y";
        private const string ParamLookPitchId = "param_look_pitch";
        private const string ParamTurnDirectionId = "param_turn_direction";
        private const string ParamIsDeadId = "param_is_dead";
        private const string ParamIsJetpackActiveId = "param_is_jetpack_active";
        private const string ParamIsGroundedId = "param_is_grounded";
        private const string ParamHasJumpedId = "param_has_jumped";
        private const string ParamIsReloadingId = "param_is_reloading";
        private const string ParamIsEquippingId = "param_is_equipping";
        private const string ParamIsUnequippingId = "param_is_unequipping";
        private const string ParamEquipTriggerId = "param_equip_trigger";
        private const string ParamUnequipTriggerId = "param_unequip_trigger";
        private const string ParamIsThrowingId = "param_is_throwing";
        private const string ParamIsTurningId = "param_is_turning";
        private const string ParamShootTriggerId = "param_shoot_trigger";
        private const string ParamThrowStartId = "param_throw_start";
        private const string ParamThrowHoldId = "param_throw_hold";
        private const string ParamGrenadeEquipId = "param_grenade_equip";

        private enum SlotSelector
        {
            None = 0,
            CurrentWeapon = 1,
            PendingWeapon = 2,
        }

        private enum SlotMap
        {
            Exact = 0,
            GrenadeToZero = 1,
            GrenadeToOne = 2,
        }

        private sealed class ImportedVariant
        {
            public string StateId;
            public int SlotIndex;
        }

        private sealed class ImportedGroup
        {
            public string Name;
            public string TypeName;
            public string LayerId;
            public SlotSelector Selector;
            public SlotMap Map;
            public bool RequireJetpackInactive;
            public readonly List<ImportedVariant> Variants = new List<ImportedVariant>();
            public readonly Dictionary<string, string> FieldStateIds = new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private sealed class LayerBuildContext
        {
            public string LayerId;
            public string LayerName;
            public int LayerIndex;
            public int RowIndex;
            public string DefaultStateId;
        }

        private static readonly Dictionary<string, FusionAnimatorStateDefinition> s_stateCache =
            new Dictionary<string, FusionAnimatorStateDefinition>(StringComparer.Ordinal);

        public string Id => "fusion-agent-to-fusion-graph";
        public string DisplayName => "Fusion Agent Prefab -> FusionAnimatorGraph";

        public bool CanConvert(UnityEngine.Object source)
        {
            return TryResolveAnimationController(source, out _, out _);
        }

        public bool TryConvert(UnityEngine.Object source, FusionAnimatorGraphAsset target, out string message)
        {
            if (target == null)
            {
                message = "Target graph is null.";
                return false;
            }

            if (!TryResolveAnimationController(source, out GameObject sourceRoot, out Component animationController))
            {
                message = "Source must be a prefab or GameObject containing a Fusion AnimationController.";
                return false;
            }

            s_stateCache.Clear();

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
            target.EntryStateId = string.Empty;

            AddDefaultParameters(target.Parameters);

            Transform layersRoot = GetFieldValue(animationController, "_root") as Transform;
            if (layersRoot == null)
            {
                layersRoot = animationController.transform;
            }

            List<Component> layerComponents = CollectLayerComponents(layersRoot);
            if (layerComponents.Count == 0)
            {
                message = "No animation layers found under controller root.";
                return false;
            }

            List<ImportedGroup> importedGroups = new List<ImportedGroup>(64);
            Dictionary<string, string> neutralStateIdsByLayer = new Dictionary<string, string>(StringComparer.Ordinal);
            int stateFallbackIndex = 0;

            for (int i = 0; i < layerComponents.Count; ++i)
            {
                Component layerComponent = layerComponents[i];
                if (layerComponent == null)
                {
                    continue;
                }

                string layerName = layerComponent.gameObject.name;
                string layerTypeName = layerComponent.GetType() != null ? layerComponent.GetType().Name : string.Empty;
                bool hasPersistentLayerDefault = HasPersistentLayerDefault(layerTypeName);
                string layerId = BuildStableId("layer", layerComponent, layerName, i);

                var layerDefinition = new FusionAnimatorLayerDefinition
                {
                    Id = layerId,
                    Name = layerName,
                    Priority = i,
                    DefaultWeight = ReadFloatField(layerComponent, "_initialWeight", 1.0f),
                    EnabledByDefault = true,
                    BlendMode = ReadBoolField(layerComponent, "_isAdditive", false)
                        ? FusionAnimatorLayerBlendMode.Additive
                        : FusionAnimatorLayerBlendMode.Override,
                    AvatarMask = GetFieldValue(layerComponent, "_avatarMask") as AvatarMask,
                    SyncedLayerIndex = -1,
                    SyncTiming = false,
                    IKPass = false,
                };
                target.Layers.Add(layerDefinition);

                LayerBuildContext layerContext = new LayerBuildContext
                {
                    LayerId = layerId,
                    LayerName = layerName,
                    LayerIndex = i,
                    RowIndex = 0,
                    DefaultStateId = string.Empty,
                };

                Transform layerTransform = layerComponent.transform;
                for (int childIndex = 0; childIndex < layerTransform.childCount; ++childIndex)
                {
                    Transform child = layerTransform.GetChild(childIndex);
                    CollectStatesRecursive(
                        child,
                        scopePath: string.Empty,
                        depth: 0,
                        layerContext,
                        target.States,
                        importedGroups,
                        ref stateFallbackIndex);
                }

                if (hasPersistentLayerDefault == true && string.IsNullOrWhiteSpace(layerContext.DefaultStateId))
                {
                    layerContext.DefaultStateId = ResolveLayerDefaultStateId(layerComponent, target.States, layerId);
                }

                if (hasPersistentLayerDefault == false)
                {
                    neutralStateIdsByLayer[layerId] = FusionAnimatorGraphAsset.SpecialNodeExitId;
                }
                else if (string.IsNullOrWhiteSpace(layerContext.DefaultStateId) == false)
                {
                    neutralStateIdsByLayer[layerId] = layerContext.DefaultStateId;
                }

                if (hasPersistentLayerDefault == true && string.IsNullOrWhiteSpace(layerContext.DefaultStateId) == false)
                {
                    target.Transitions.Add(new FusionAnimatorTransitionDefinition
                    {
                        Id = FusionAnimatorGraphAsset.NewId("transition"),
                        Name = string.Format("{0}.Entry", layerName),
                        FromStateId = FusionAnimatorGraphAsset.SpecialNodeEntryId,
                        ToStateId = layerContext.DefaultStateId,
                        Priority = target.Transitions.Count,
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

                    if (string.IsNullOrWhiteSpace(target.EntryStateId))
                    {
                        target.EntryStateId = layerContext.DefaultStateId;
                    }
                }
            }

            int transitionPriority = target.Transitions.Count;
            for (int i = 0; i < importedGroups.Count; ++i)
            {
                ImportedGroup group = importedGroups[i];
                if (group == null || group.Variants.Count <= 1 || group.Selector == SlotSelector.None)
                {
                    continue;
                }

                AddVariantTransitions(group, target.Transitions, ref transitionPriority);

                if (string.Equals(group.TypeName, "DeadState", StringComparison.Ordinal))
                {
                    AddAnyStateDeadTransitions(group, target.Transitions, ref transitionPriority);
                }
            }

            AddCodeDrivenLayerTransitions(importedGroups, target.Transitions, ref transitionPriority);
            AddAutoReturnTransitions(importedGroups, neutralStateIdsByLayer, target.Transitions, ref transitionPriority);

            target.DisplayName = sourceRoot != null ? sourceRoot.name : source.name;
            if (string.IsNullOrWhiteSpace(target.GraphId))
            {
                target.GraphId = FusionAnimatorGraphAsset.NewId("graph");
            }

            SetSpecialNodePositions(target);

            message = string.Format(
                "Converted '{0}' -> Parameters={1}, Layers={2}, States={3}, Transitions={4}",
                target.DisplayName,
                target.Parameters.Count,
                target.Layers.Count,
                target.States.Count,
                target.Transitions.Count);
            return true;
        }

        private static void AddDefaultParameters(List<FusionAnimatorParameterDefinition> parameters)
        {
            AddParameter(parameters, ParamWeaponSlotId, "Weapon Slot", FusionAnimatorParameterType.Int, defaultInt: 0);
            AddParameter(parameters, ParamPendingWeaponSlotId, "Pending Weapon Slot", FusionAnimatorParameterType.Int, defaultInt: -1);
            AddParameter(parameters, ParamMoveXId, "Move X", FusionAnimatorParameterType.Float, defaultFloat: 0.0f, previewInputBinding: "<Gamepad>/leftStick/x");
            AddParameter(parameters, ParamMoveYId, "Move Y", FusionAnimatorParameterType.Float, defaultFloat: 0.0f, previewInputBinding: "<Gamepad>/leftStick/y");
            AddParameter(parameters, ParamLookPitchId, "Look Pitch", FusionAnimatorParameterType.Float, defaultFloat: 0.0f, previewInputBinding: "<Gamepad>/rightStick/y");
            AddParameter(parameters, ParamTurnDirectionId, "Turn Direction", FusionAnimatorParameterType.Float, defaultFloat: 0.0f, previewInputBinding: "<Gamepad>/rightStick/x");
            AddParameter(parameters, ParamIsDeadId, "Is Dead", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamIsJetpackActiveId, "Is Jetpack Active", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamIsGroundedId, "Is Grounded", FusionAnimatorParameterType.Bool, defaultBool: true);
            AddParameter(parameters, ParamHasJumpedId, "Has Jumped", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamIsReloadingId, "Is Reloading", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamIsEquippingId, "Is Equipping", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamIsUnequippingId, "Is Unequipping", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamEquipTriggerId, "Equip Trigger", FusionAnimatorParameterType.Trigger, defaultBool: false);
            AddParameter(parameters, ParamUnequipTriggerId, "Unequip Trigger", FusionAnimatorParameterType.Trigger, defaultBool: false);
            AddParameter(parameters, ParamIsThrowingId, "Is Throwing", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamIsTurningId, "Is Turning", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamShootTriggerId, "Shoot Trigger", FusionAnimatorParameterType.Trigger, defaultBool: false);
            AddParameter(parameters, ParamThrowStartId, "Throw Start", FusionAnimatorParameterType.Trigger, defaultBool: false);
            AddParameter(parameters, ParamThrowHoldId, "Throw Hold", FusionAnimatorParameterType.Bool, defaultBool: false);
            AddParameter(parameters, ParamGrenadeEquipId, "Grenade Equip", FusionAnimatorParameterType.Bool, defaultBool: false);
        }

        private static void AddParameter(
            List<FusionAnimatorParameterDefinition> parameters,
            string id,
            string name,
            FusionAnimatorParameterType type,
            bool defaultBool = false,
            int defaultInt = 0,
            float defaultFloat = 0.0f,
            string previewInputBinding = "")
        {
            parameters.Add(new FusionAnimatorParameterDefinition
            {
                Id = id,
                Name = name,
                Type = type,
                DefaultBool = defaultBool,
                DefaultInt = defaultInt,
                DefaultFloat = defaultFloat,
                DefaultVector2 = Vector2.zero,
                PreviewInputBinding = previewInputBinding,
                PreviewInputScale = 1.0f,
            });
        }

        private static string ResolveLayerDefaultStateId(
            Component layerComponent,
            List<FusionAnimatorStateDefinition> states,
            string layerId)
        {
            if (states == null || string.IsNullOrWhiteSpace(layerId))
            {
                return string.Empty;
            }

            string layerTypeName = layerComponent != null && layerComponent.GetType() != null
                ? layerComponent.GetType().Name
                : string.Empty;

            string preferredCanonicalState = string.Empty;
            if (string.Equals(layerTypeName, "LocomotionLayer", StringComparison.Ordinal))
            {
                preferredCanonicalState = "Move";
            }
            else if (string.Equals(layerTypeName, "LookLayer", StringComparison.Ordinal))
            {
                preferredCanonicalState = "Look";
            }

            string preferredStateId = string.Empty;
            string idleStateId = string.Empty;
            FusionAnimatorStateDefinition fallbackState = null;

            for (int i = 0; i < states.Count; ++i)
            {
                FusionAnimatorStateDefinition state = states[i];
                if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string canonicalName = GetCanonicalStateName(state.Name);
                if (string.IsNullOrWhiteSpace(preferredCanonicalState) == false &&
                    string.Equals(canonicalName, preferredCanonicalState, StringComparison.OrdinalIgnoreCase))
                {
                    preferredStateId = state.Id;
                    break;
                }

                if (string.IsNullOrWhiteSpace(idleStateId) &&
                    string.Equals(canonicalName, "Idle", StringComparison.OrdinalIgnoreCase))
                {
                    idleStateId = state.Id;
                }

                if (fallbackState == null ||
                    state.NodePosition.y < fallbackState.NodePosition.y ||
                    (Mathf.Approximately(state.NodePosition.y, fallbackState.NodePosition.y) && state.NodePosition.x < fallbackState.NodePosition.x))
                {
                    fallbackState = state;
                }
            }

            if (string.IsNullOrWhiteSpace(preferredStateId) == false)
            {
                return preferredStateId;
            }

            if (string.IsNullOrWhiteSpace(idleStateId) == false)
            {
                return idleStateId;
            }

            return fallbackState != null ? fallbackState.Id : string.Empty;
        }

        private static bool HasPersistentLayerDefault(string layerTypeName)
        {
            return string.Equals(layerTypeName, "LocomotionLayer", StringComparison.Ordinal) ||
                   string.Equals(layerTypeName, "LookLayer", StringComparison.Ordinal);
        }

        private static string GetCanonicalStateName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return string.Empty;
            }

            string canonical = stateName.Trim();
            int slash = canonical.LastIndexOf('/');
            if (slash >= 0 && slash + 1 < canonical.Length)
            {
                canonical = canonical.Substring(slash + 1);
            }

            int variantSeparator = canonical.IndexOf(" (", StringComparison.Ordinal);
            if (variantSeparator > 0)
            {
                canonical = canonical.Substring(0, variantSeparator);
            }

            return canonical.Trim();
        }

        private static List<Component> CollectLayerComponents(Transform root)
        {
            List<Component> layers = new List<Component>(8);
            if (root == null)
            {
                return layers;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                Component layer = FindComponentByTypeName(child.gameObject, AnimationLayerTypeName);
                if (layer != null)
                {
                    layers.Add(layer);
                }
            }

            return layers;
        }

        private static void CollectStatesRecursive(
            Transform transform,
            string scopePath,
            int depth,
            LayerBuildContext layerContext,
            List<FusionAnimatorStateDefinition> statesOut,
            List<ImportedGroup> groupsOut,
            ref int fallbackStateIndex)
        {
            if (transform == null)
            {
                return;
            }

            Component stateComponent = FindComponentByTypeName(transform.gameObject, AnimationStateTypeName);
            string nextScope = scopePath;

            if (stateComponent != null)
            {
                string baseStateName = string.IsNullOrWhiteSpace(scopePath)
                    ? transform.name
                    : string.Format("{0}/{1}", scopePath, transform.name);
                bool importAsScopeOnly = ShouldImportAsScopeOnly(stateComponent);
                bool hasImportedConcreteState = false;

                if (importAsScopeOnly == false)
                {
                    Vector2 basePosition = new Vector2(
                        160.0f + depth * 440.0f + layerContext.LayerIndex * 40.0f,
                        120.0f + layerContext.RowIndex * 180.0f);
                    layerContext.RowIndex++;

                    ImportedGroup group = BuildStateGroup(
                        stateComponent,
                        baseStateName,
                        layerContext.LayerId,
                        basePosition,
                        ref fallbackStateIndex);

                    if (group != null && group.Variants.Count > 0)
                    {
                        groupsOut.Add(group);
                        hasImportedConcreteState = true;
                    }

                    if (group != null)
                    {
                        for (int i = 0; i < group.Variants.Count; ++i)
                        {
                            ImportedVariant variant = group.Variants[i];
                            if (variant == null)
                            {
                                continue;
                            }

                            FusionAnimatorStateDefinition state = FindStateById(statesOut, variant.StateId);
                            if (state != null)
                            {
                                AddStateIfMissing(statesOut, state);
                            }
                        }
                    }
                }

                nextScope = baseStateName;

                if (importAsScopeOnly == true)
                {
                    for (int i = 0; i < transform.childCount; ++i)
                    {
                        CollectStatesRecursive(
                            transform.GetChild(i),
                            nextScope,
                            depth + 1,
                            layerContext,
                            statesOut,
                            groupsOut,
                            ref fallbackStateIndex);
                    }

                    ImportedGroup scopeGroup = BuildScopeOnlyGroupFromChildStates(
                        stateComponent,
                        stateComponent.GetType().Name,
                        baseStateName,
                        layerContext.LayerId,
                        statesOut);
                    if (scopeGroup != null && scopeGroup.Variants.Count > 0)
                    {
                        groupsOut.Add(scopeGroup);
                    }

                    return;
                }

                if (hasImportedConcreteState == true)
                {
                    return;
                }
            }

            for (int i = 0; i < transform.childCount; ++i)
            {
                CollectStatesRecursive(
                    transform.GetChild(i),
                    nextScope,
                    depth + 1,
                    layerContext,
                    statesOut,
                    groupsOut,
                    ref fallbackStateIndex);
            }
        }

        private static bool ShouldImportAsScopeOnly(Component stateComponent)
        {
            if (stateComponent == null)
            {
                return false;
            }

            Type stateType = stateComponent.GetType();
            if (stateType == null || stateType.Name.EndsWith("State", StringComparison.Ordinal) == false)
            {
                return false;
            }

            if (IsTypeOrSubclassOf(stateType, MultiBlendTreeStateTypeName) ||
                IsTypeOrSubclassOf(stateType, MirrorBlendTreeStateTypeName) ||
                IsTypeOrSubclassOf(stateType, MultiClipStateTypeName) ||
                IsTypeOrSubclassOf(stateType, ClipStateTypeName) ||
                string.Equals(stateType.Name, "LookState", StringComparison.Ordinal) ||
                string.Equals(stateType.Name, "TurnState", StringComparison.Ordinal))
            {
                return false;
            }

            Transform transform = stateComponent.transform;
            if (transform == null)
            {
                return false;
            }

            if (IsTypeOrSubclassOf(stateType, MixerStateTypeName))
            {
                for (int i = 0; i < transform.childCount; ++i)
                {
                    Transform child = transform.GetChild(i);
                    if (child == null)
                    {
                        continue;
                    }

                    Component childState = FindComponentByTypeName(child.gameObject, AnimationStateTypeName);
                    if (childState != null)
                    {
                        return true;
                    }
                }

                return false;
            }

            int childStateCount = 0;
            for (int i = 0; i < transform.childCount; ++i)
            {
                Transform child = transform.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                Component childState = FindComponentByTypeName(child.gameObject, AnimationStateTypeName);
                if (childState == null)
                {
                    continue;
                }

                childStateCount++;
                if (IsTypeOrSubclassOf(childState.GetType(), ClipStateTypeName) == false)
                {
                    return false;
                }
            }

            return childStateCount > 0;
        }

        private static ImportedGroup BuildScopeOnlyGroupFromChildStates(
            Component ownerState,
            string typeName,
            string scopeName,
            string layerId,
            List<FusionAnimatorStateDefinition> states)
        {
            if (string.IsNullOrWhiteSpace(scopeName) || string.IsNullOrWhiteSpace(layerId) || states == null || states.Count == 0)
            {
                return null;
            }

            string prefix = scopeName + "/";
            string firstStateId = null;

            for (int i = 0; i < states.Count; ++i)
            {
                FusionAnimatorStateDefinition state = states[i];
                if (state == null || string.Equals(state.LayerId, layerId, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string stateName = state.Name;
                if (string.IsNullOrWhiteSpace(stateName) || stateName.StartsWith(prefix, StringComparison.Ordinal) == false)
                {
                    continue;
                }

                string remainder = stateName.Substring(prefix.Length);
                if (string.IsNullOrWhiteSpace(remainder) || remainder.IndexOf('/') >= 0)
                {
                    continue;
                }

                if (string.IsNullOrWhiteSpace(firstStateId))
                {
                    firstStateId = state.Id;
                }
            }

            ResolveSelectorPolicy(typeName, out SlotSelector selector, out SlotMap map, out bool requireJetpackInactive);

            ImportedGroup group = new ImportedGroup
            {
                Name = scopeName,
                TypeName = typeName,
                LayerId = layerId,
                Selector = selector,
                Map = map,
                RequireJetpackInactive = requireJetpackInactive,
            };

            PopulateFieldStateIds(ownerState, group, states);

            string selectedStateId = firstStateId;
            if (string.Equals(typeName, "GrenadeState", StringComparison.Ordinal))
            {
                string equipStateId = GetFieldStateId(group, "_equipState");
                string holdStateId = GetFieldStateId(group, "_holdState");
                string armStateId = GetFieldStateId(group, "_armState");
                string throwStateId = GetFieldStateId(group, "_throwState");
                string reloadStateId = GetFieldStateId(group, "_reloadState");

                if (string.IsNullOrWhiteSpace(equipStateId) == false)
                {
                    selectedStateId = equipStateId;
                }
                else if (string.IsNullOrWhiteSpace(holdStateId) == false)
                {
                    selectedStateId = holdStateId;
                }
                else if (string.IsNullOrWhiteSpace(armStateId) == false)
                {
                    selectedStateId = armStateId;
                }
                else if (string.IsNullOrWhiteSpace(throwStateId) == false)
                {
                    selectedStateId = throwStateId;
                }
                else if (string.IsNullOrWhiteSpace(reloadStateId) == false)
                {
                    selectedStateId = reloadStateId;
                }
            }

            if (string.IsNullOrWhiteSpace(selectedStateId))
            {
                return null;
            }

            group.Variants.Add(new ImportedVariant
            {
                StateId = selectedStateId,
                SlotIndex = 0,
            });

            return group;
        }

        private static void PopulateFieldStateIds(
            Component ownerState,
            ImportedGroup group,
            List<FusionAnimatorStateDefinition> states)
        {
            if (ownerState == null || group == null || states == null)
            {
                return;
            }

            Type type = ownerState.GetType();
            while (type != null)
            {
                FieldInfo[] fields = type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                for (int i = 0; i < fields.Length; ++i)
                {
                    FieldInfo field = fields[i];
                    if (field == null)
                    {
                        continue;
                    }

                    object fieldValue = field.GetValue(ownerState);
                    Component childState = fieldValue as Component;
                    if (childState == null)
                    {
                        continue;
                    }

                    if (IsTypeOrSubclassOf(childState.GetType(), AnimationStateTypeName) == false)
                    {
                        continue;
                    }

                    string candidateStateId = BuildStateIdFromComponent(childState, 0);
                    if (string.IsNullOrWhiteSpace(candidateStateId))
                    {
                        continue;
                    }

                    FusionAnimatorStateDefinition childStateDef = FindStateById(states, candidateStateId);
                    if (childStateDef == null)
                    {
                        continue;
                    }

                    if (group.FieldStateIds.ContainsKey(field.Name) == false)
                    {
                        group.FieldStateIds.Add(field.Name, candidateStateId);
                    }
                }

                type = type.BaseType;
            }
        }

        private static string BuildStateIdFromComponent(Component stateComponent, int slotIndex)
        {
            if (stateComponent == null)
            {
                return string.Empty;
            }

            string baseId = BuildStableId("state", stateComponent, stateComponent.name, 0);
            return string.Format("{0}_set{1}", baseId, slotIndex);
        }

        private static ImportedGroup BuildStateGroup(
            Component stateComponent,
            string baseStateName,
            string layerId,
            Vector2 basePosition,
            ref int fallbackStateIndex)
        {
            if (stateComponent == null)
            {
                return null;
            }

            string typeName = stateComponent.GetType().Name;
            SlotSelector selector;
            SlotMap map;
            bool requireJetpackInactive;
            ResolveSelectorPolicy(typeName, out selector, out map, out requireJetpackInactive);

            List<FusionAnimatorStateDefinition> builtStates = new List<FusionAnimatorStateDefinition>(4);
            List<int> slotIndices = new List<int>(4);

            if (IsTypeOrSubclassOf(stateComponent.GetType(), MultiBlendTreeStateTypeName) ||
                IsTypeOrSubclassOf(stateComponent.GetType(), MirrorBlendTreeStateTypeName))
            {
                object[] sets = GetArrayField(stateComponent, "_sets");
                for (int i = 0; i < sets.Length; ++i)
                {
                    object set = sets[i];
                    FusionAnimatorBlendTreeDefinition tree = BuildBlendTreeFromSet(set, ParamMoveXId, ParamMoveYId, FusionAnimatorBlendTreeType.TwoDFreeformDirectional);
                    if (tree.Children.Count == 0)
                    {
                        continue;
                    }

                    string variantName = BuildVariantStateName(baseStateName, i);
                    string stateId = BuildVariantStateId(stateComponent, i, ref fallbackStateIndex);
                    builtStates.Add(new FusionAnimatorStateDefinition
                    {
                        Id = stateId,
                        Name = variantName,
                        LayerId = layerId,
                        NodePosition = basePosition + new Vector2(i * 220.0f, 0.0f),
                        MinDurationSeconds = 0.0f,
                        CanTransitionOut = true,
                        WriteDefaults = ReadBoolField(stateComponent, "_writeDefaults", false),
                        MotionType = FusionAnimatorMotionType.BlendTree,
                        Clips = new List<FusionAnimatorClipSlot>(),
                        BlendTree = tree,
                    });
                    slotIndices.Add(i);
                }
            }
            else if (string.Equals(typeName, "LookState", StringComparison.Ordinal))
            {
                object[] sets = GetArrayField(stateComponent, "_sets");
                for (int i = 0; i < sets.Length; ++i)
                {
                    object set = sets[i];
                    object[] nodes = GetArrayField(set, "Nodes");
                    if (nodes.Length == 0)
                    {
                        continue;
                    }

                    var tree = new FusionAnimatorBlendTreeDefinition
                    {
                        Type = FusionAnimatorBlendTreeType.DirectionalPoseTime2D,
                        ParameterXId = ParamLookPitchId,
                        ParameterYId = string.Empty,
                        ParameterVector2Id = string.Empty,
                        PoseTimeParameterId = string.Empty,
                        DirectBlendParameterId = string.Empty,
                        InputOffsetX = ReadFloatField(set, "Offset", 0.0f),
                        InputPowerX = Mathf.Max(0.0001f, ReadFloatField(set, "Power", 1.0f)),
                        NormalizeTimeScale = true,
                        Children = new List<FusionAnimatorBlendTreeChild>(),
                    };

					for (int nodeIndex = 0; nodeIndex < nodes.Length; ++nodeIndex)
					{
						object node = nodes[nodeIndex];
						AnimationClip clip = GetFieldValue(node, "Clip") as AnimationClip;
						if (clip == null)
						{
							continue;
						}

						float lookThreshold;
						if (nodes.Length <= 1)
						{
							lookThreshold = 1.0f;
						}
						else
						{
							lookThreshold = nodeIndex == 0 ? 90.0f : -90.0f;
						}

						Vector2 directionalPosition;
						if (nodes.Length <= 1)
						{
							directionalPosition = Vector2.right;
						}
						else
						{
							directionalPosition = nodeIndex == 0 ? Vector2.right : Vector2.left;
						}

						tree.Children.Add(new FusionAnimatorBlendTreeChild
						{
							Name = clip.name,
							Clip = clip,
							Threshold = lookThreshold,
							Position = directionalPosition,
							DirectParameterId = string.Empty,
							TimeScale = ReadFloatField(node, "Speed", 1.0f),
						});
					}

                    if (tree.Children.Count == 0)
                    {
                        continue;
                    }

                    string variantName = BuildVariantStateName(baseStateName, i);
                    string stateId = BuildVariantStateId(stateComponent, i, ref fallbackStateIndex);
                    builtStates.Add(new FusionAnimatorStateDefinition
                    {
                        Id = stateId,
                        Name = variantName,
                        LayerId = layerId,
                        NodePosition = basePosition + new Vector2(i * 220.0f, 0.0f),
                        MinDurationSeconds = 0.0f,
                        CanTransitionOut = true,
                        WriteDefaults = false,
                        MotionType = FusionAnimatorMotionType.BlendTree,
                        Clips = new List<FusionAnimatorClipSlot>(),
                        BlendTree = tree,
                        Presentation = new FusionAnimatorStatePresentationDefinition
                        {
                            Semantic = FusionAnimatorStateSemantic.None,
                            Offset = tree.InputOffsetX,
                            Power = tree.InputPowerX <= 0.0001f ? 1.0f : tree.InputPowerX,
                            BlendSpeed = 1.0f,
                            TurnSpeed = 1.0f,
                            MaxMagnitude = 90.0f,
                            OverlayWeight = 1.0f,
                        },
                    });
                    slotIndices.Add(i);
                }
            }
            else if (string.Equals(typeName, "TurnState", StringComparison.Ordinal))
            {
                float turnBlendSpeed = Mathf.Max(0.0001f, ReadFloatField(stateComponent, "_blendSpeed", 1.0f));
                float turnSpeed = Mathf.Max(0.0001f, ReadFloatField(stateComponent, "_turnSpeed", 1.0f));
                float turnMaxMagnitude = Mathf.Max(0.0001f, ReadFloatField(stateComponent, "_maxAnimationSpeed", 1.0f));
                float turnAnimationPower = Mathf.Clamp01(ReadFloatField(stateComponent, "_animationPower", 1.0f));

                object[] nodes = GetArrayField(stateComponent, "_nodes");
                int setCount = nodes.Length / 3;
                for (int i = 0; i < setCount; ++i)
                {
                    object idleNode = nodes[i * 3 + 0];
                    object leftNode = nodes[i * 3 + 1];
                    object rightNode = nodes[i * 3 + 2];

                    AnimationClip idleClip = GetFieldValue(idleNode, "Clip") as AnimationClip;
                    AnimationClip leftClip = GetFieldValue(leftNode, "Clip") as AnimationClip;
                    AnimationClip rightClip = GetFieldValue(rightNode, "Clip") as AnimationClip;

                    var tree = new FusionAnimatorBlendTreeDefinition
                    {
                        Type = FusionAnimatorBlendTreeType.OneD,
                        ParameterXId = ParamTurnDirectionId,
                        ParameterYId = string.Empty,
                        DirectBlendParameterId = string.Empty,
                        NormalizeTimeScale = true,
                        Children = new List<FusionAnimatorBlendTreeChild>(),
                    };

                    if (leftClip != null)
                    {
                        tree.Children.Add(new FusionAnimatorBlendTreeChild
                        {
                            Name = leftClip.name,
                            Clip = leftClip,
                            Threshold = -1.0f,
                            Position = new Vector2(-1.0f, 0.0f),
                            TimeScale = ReadFloatField(leftNode, "Speed", 1.0f),
                        });
                    }
                    if (idleClip != null)
                    {
                        tree.Children.Add(new FusionAnimatorBlendTreeChild
                        {
                            Name = idleClip.name,
                            Clip = idleClip,
                            Threshold = 0.0f,
                            Position = new Vector2(0.0f, 0.0f),
                            TimeScale = ReadFloatField(idleNode, "Speed", 1.0f),
                        });
                    }
                    if (rightClip != null)
                    {
                        tree.Children.Add(new FusionAnimatorBlendTreeChild
                        {
                            Name = rightClip.name,
                            Clip = rightClip,
                            Threshold = 1.0f,
                            Position = new Vector2(1.0f, 0.0f),
                            TimeScale = ReadFloatField(rightNode, "Speed", 1.0f),
                        });
                    }

                    if (tree.Children.Count == 0)
                    {
                        continue;
                    }

                    string variantName = BuildVariantStateName(baseStateName, i);
                    string stateId = BuildVariantStateId(stateComponent, i, ref fallbackStateIndex);
                    builtStates.Add(new FusionAnimatorStateDefinition
                    {
                        Id = stateId,
                        Name = variantName,
                        LayerId = layerId,
                        NodePosition = basePosition + new Vector2(i * 220.0f, 0.0f),
                        MinDurationSeconds = 0.0f,
                        CanTransitionOut = true,
                        WriteDefaults = false,
                        MotionType = FusionAnimatorMotionType.BlendTree,
                        Clips = new List<FusionAnimatorClipSlot>(),
                        BlendTree = tree,
                        Presentation = new FusionAnimatorStatePresentationDefinition
                        {
                            Semantic = FusionAnimatorStateSemantic.TurnInPlace,
                            Offset = 0.0f,
                            Power = 1.0f,
                            BlendSpeed = turnBlendSpeed,
                            TurnSpeed = turnSpeed,
                            MaxMagnitude = turnMaxMagnitude,
                            OverlayWeight = turnAnimationPower,
                        },
                    });
                    slotIndices.Add(i);
                }
            }
            else if (string.Equals(typeName, "ShootState", StringComparison.Ordinal))
            {
                object[] nodes = GetArrayField(stateComponent, "_nodes");
                int slotCount = nodes.Length / 2;
                if (slotCount <= 0)
                {
                    slotCount = nodes.Length >= 2 ? 1 : 0;
                }

                float shootOverlayWeight = Mathf.Clamp01(ReadFloatField(stateComponent, "_animationPower", 1.0f));
                for (int i = 0; i < slotCount; ++i)
                {
                    int idleNodeIndex = Mathf.Clamp(i * 2 + 0, 0, Mathf.Max(0, nodes.Length - 1));
                    int shootNodeIndex = Mathf.Clamp(i * 2 + 1, 0, Mathf.Max(0, nodes.Length - 1));
                    object idleNode = nodes.Length > 0 ? nodes[idleNodeIndex] : null;
                    object shootNode = nodes.Length > 0 ? nodes[shootNodeIndex] : null;

                    AnimationClip idleClip = GetFieldValue(idleNode, "Clip") as AnimationClip;
                    AnimationClip shootClip = GetFieldValue(shootNode, "Clip") as AnimationClip;
                    if (shootClip == null)
                    {
                        continue;
                    }

                    if (idleClip == null)
                    {
                        idleClip = shootClip;
                    }

                    string variantName = slotCount > 1 ? BuildVariantStateName(baseStateName, i) : baseStateName;
                    string stateId = BuildVariantStateId(stateComponent, i, ref fallbackStateIndex);

                    builtStates.Add(new FusionAnimatorStateDefinition
                    {
                        Id = stateId,
                        Name = variantName,
                        LayerId = layerId,
                        NodePosition = basePosition + new Vector2(i * 220.0f, 0.0f),
                        MinDurationSeconds = 0.0f,
                        CanTransitionOut = true,
                        WriteDefaults = false,
                        MotionType = FusionAnimatorMotionType.Clip,
                        Clips = new List<FusionAnimatorClipSlot>
                        {
                            new FusionAnimatorClipSlot
                            {
                                Slot = "Idle",
                                Clip = idleClip,
                                Speed = ReadFloatField(idleNode, "Speed", 1.0f),
                                Loop = ReadBoolField(idleNode, "IsLooping", true),
                            },
                            new FusionAnimatorClipSlot
                            {
                                Slot = "Shoot",
                                Clip = shootClip,
                                Speed = ReadFloatField(shootNode, "Speed", 1.0f),
                                Loop = ReadBoolField(shootNode, "IsLooping", true),
                            },
                        },
                        BlendTree = new FusionAnimatorBlendTreeDefinition(),
                        Presentation = new FusionAnimatorStatePresentationDefinition
                        {
                            Semantic = FusionAnimatorStateSemantic.ShootOverlay,
                            Offset = 0.0f,
                            Power = 1.0f,
                            BlendSpeed = 1.0f,
                            TurnSpeed = 1.0f,
                            MaxMagnitude = 1.0f,
                            OverlayWeight = shootOverlayWeight,
                        },
                    });
                    slotIndices.Add(i);
                }
            }
            else if (IsTypeOrSubclassOf(stateComponent.GetType(), MultiClipStateTypeName))
            {
                object[] nodes = GetArrayField(stateComponent, "_nodes");
                int slotCount = nodes.Length;
                if (slotCount <= 0)
                {
                    slotCount = 0;
                }

                for (int i = 0; i < slotCount; ++i)
                {
                    int nodeIndex = Mathf.Clamp(i, 0, Mathf.Max(0, nodes.Length - 1));
                    object node = nodes.Length > 0 ? nodes[nodeIndex] : null;
                    AnimationClip clip = GetFieldValue(node, "Clip") as AnimationClip;
                    if (clip == null)
                    {
                        continue;
                    }

                    string variantName = slotCount > 1 ? BuildVariantStateName(baseStateName, i) : baseStateName;
                    string stateId = BuildVariantStateId(stateComponent, i, ref fallbackStateIndex);

                    builtStates.Add(new FusionAnimatorStateDefinition
                    {
                        Id = stateId,
                        Name = variantName,
                        LayerId = layerId,
                        NodePosition = basePosition + new Vector2(i * 220.0f, 0.0f),
                        MinDurationSeconds = 0.0f,
                        CanTransitionOut = true,
                        WriteDefaults = false,
                        MotionType = FusionAnimatorMotionType.Clip,
                        Clips = new List<FusionAnimatorClipSlot>
                        {
                            new FusionAnimatorClipSlot
                            {
                                Slot = "Default",
                                Clip = clip,
                                Speed = ReadFloatField(node, "Speed", 1.0f),
                                Loop = ReadBoolField(node, "IsLooping", true),
                            },
                        },
                        BlendTree = new FusionAnimatorBlendTreeDefinition(),
                    });
                    slotIndices.Add(i);
                }
            }
            else if (IsTypeOrSubclassOf(stateComponent.GetType(), ClipStateTypeName))
            {
                object node = GetFieldValue(stateComponent, "_node");
                AnimationClip clip = GetFieldValue(node, "Clip") as AnimationClip;
                if (clip == null)
                {
                    return null;
                }

                builtStates.Add(new FusionAnimatorStateDefinition
                {
                    Id = BuildVariantStateId(stateComponent, 0, ref fallbackStateIndex),
                    Name = baseStateName,
                    LayerId = layerId,
                    NodePosition = basePosition,
                    MinDurationSeconds = 0.0f,
                    CanTransitionOut = true,
                    WriteDefaults = false,
                    MotionType = FusionAnimatorMotionType.Clip,
                    Clips = new List<FusionAnimatorClipSlot>
                    {
                        new FusionAnimatorClipSlot
                        {
                            Slot = "Default",
                            Clip = clip,
                            Speed = ReadFloatField(node, "Speed", 1.0f),
                            Loop = ReadBoolField(node, "IsLooping", true),
                        },
                    },
                    BlendTree = new FusionAnimatorBlendTreeDefinition(),
                });
                slotIndices.Add(0);
            }
            else if (IsTypeOrSubclassOf(stateComponent.GetType(), MixerStateTypeName))
            {
                List<FusionAnimatorClipSlot> mixerSlots = BuildClipSlotsFromMixerState(stateComponent, typeName);
                if (mixerSlots.Count == 0)
                {
                    AnimationClip representativeClip = TryExtractRepresentativeClipFromMixer(stateComponent);
                    if (representativeClip != null)
                    {
                        mixerSlots.Add(new FusionAnimatorClipSlot
                        {
                            Slot = "Default",
                            Clip = representativeClip,
                            Speed = 1.0f,
                            Loop = true,
                        });
                    }
                }

                if (mixerSlots.Count == 0)
                {
                    return null;
                }

                builtStates.Add(new FusionAnimatorStateDefinition
                {
                    Id = BuildVariantStateId(stateComponent, 0, ref fallbackStateIndex),
                    Name = baseStateName,
                    LayerId = layerId,
                    NodePosition = basePosition,
                    MinDurationSeconds = 0.0f,
                    CanTransitionOut = true,
                    WriteDefaults = false,
                    MotionType = FusionAnimatorMotionType.Clip,
                    Clips = mixerSlots,
                    BlendTree = new FusionAnimatorBlendTreeDefinition(),
                });
                slotIndices.Add(0);
            }

            if (builtStates.Count == 0)
            {
                return null;
            }

            ImportedGroup group = new ImportedGroup
            {
                Name = baseStateName,
                TypeName = typeName,
                LayerId = layerId,
                Selector = selector,
                Map = map,
                RequireJetpackInactive = requireJetpackInactive,
            };

            for (int i = 0; i < builtStates.Count; ++i)
            {
                FusionAnimatorStateDefinition state = builtStates[i];
                if (state == null)
                {
                    continue;
                }

                s_stateCache[state.Id] = state;
                group.Variants.Add(new ImportedVariant
                {
                    StateId = state.Id,
                    SlotIndex = i < slotIndices.Count ? slotIndices[i] : i,
                });
            }

            return group;
        }

        private static FusionAnimatorStateDefinition FindStateById(List<FusionAnimatorStateDefinition> states, string stateId)
        {
            if (string.IsNullOrWhiteSpace(stateId))
            {
                return null;
            }

            if (s_stateCache.TryGetValue(stateId, out FusionAnimatorStateDefinition cached))
            {
                return cached;
            }

            for (int i = 0; i < states.Count; ++i)
            {
                FusionAnimatorStateDefinition state = states[i];
                if (state != null && string.Equals(state.Id, stateId, StringComparison.Ordinal))
                {
                    return state;
                }
            }

            return null;
        }

        private static void AddStateIfMissing(List<FusionAnimatorStateDefinition> states, FusionAnimatorStateDefinition state)
        {
            if (states == null || state == null || string.IsNullOrWhiteSpace(state.Id))
            {
                return;
            }

            for (int i = 0; i < states.Count; ++i)
            {
                FusionAnimatorStateDefinition existing = states[i];
                if (existing != null && string.Equals(existing.Id, state.Id, StringComparison.Ordinal))
                {
                    return;
                }
            }

            states.Add(state);
        }

        private static FusionAnimatorBlendTreeDefinition BuildBlendTreeFromSet(
            object setObject,
            string parameterXId,
            string parameterYId,
            FusionAnimatorBlendTreeType blendType)
        {
            var tree = new FusionAnimatorBlendTreeDefinition
            {
                Type = blendType,
                ParameterXId = parameterXId,
                ParameterYId = parameterYId,
                PoseTimeParameterId = string.Empty,
                DirectBlendParameterId = string.Empty,
                InputOffsetX = 0.0f,
                InputPowerX = 1.0f,
                NormalizeTimeScale = true,
                Children = new List<FusionAnimatorBlendTreeChild>(),
            };

            object[] nodes = GetArrayField(setObject, "_nodes");
            for (int i = 0; i < nodes.Length; ++i)
            {
                object node = nodes[i];
                AnimationClip clip = GetFieldValue(node, "Clip") as AnimationClip;
                if (clip == null)
                {
                    continue;
                }

                Vector2 position = ReadVector2Field(node, "Position", Vector2.zero);
                tree.Children.Add(new FusionAnimatorBlendTreeChild
                {
                    Name = clip.name,
                    Clip = clip,
                    Threshold = position.magnitude,
                    Position = position,
                    DirectParameterId = string.Empty,
                    TimeScale = ReadFloatField(node, "Speed", 1.0f),
                });
            }

            return tree;
        }

        private static AnimationClip TryExtractRepresentativeClipFromMixer(Component mixerState)
        {
            if (mixerState == null)
            {
                return null;
            }

            Transform mixerTransform = mixerState.transform;
            if (mixerTransform == null)
            {
                return null;
            }

            return TryExtractFirstClipFromStateChildrenRecursive(mixerTransform, mixerState);
        }

        private static List<FusionAnimatorClipSlot> BuildClipSlotsFromMixerState(Component mixerState, string typeName)
        {
            List<FusionAnimatorClipSlot> slots = new List<FusionAnimatorClipSlot>(8);
            if (mixerState == null)
            {
                return slots;
            }

            if (string.Equals(typeName, "GrenadeState", StringComparison.Ordinal))
            {
                AddClipSlotFromStateField(slots, mixerState, "_holdState", "Hold");
                AddClipSlotFromStateField(slots, mixerState, "_armState", "Arm");
                AddClipSlotFromStateField(slots, mixerState, "_throwState", "Throw");
                AddClipSlotFromStateField(slots, mixerState, "_reloadState", "Reload");
                AddClipSlotFromStateField(slots, mixerState, "_equipState", "Equip");

                if (slots.Count > 0)
                {
                    return slots;
                }
            }

            Transform root = mixerState.transform;
            if (root == null)
            {
                return slots;
            }

            for (int i = 0; i < root.childCount; ++i)
            {
                Transform child = root.GetChild(i);
                if (child == null)
                {
                    continue;
                }

                Component childState = FindComponentByTypeName(child.gameObject, AnimationStateTypeName);
                if (childState == null)
                {
                    continue;
                }

                AnimationClip clip = TryExtractFirstClipFromState(childState);
                if (clip == null)
                {
                    continue;
                }

                slots.Add(new FusionAnimatorClipSlot
                {
                    Slot = string.IsNullOrWhiteSpace(child.name) ? "Default" : child.name,
                    Clip = clip,
                    Speed = ReadFloatField(childState, "_speed", 1.0f),
                    Loop = true,
                });
            }

            return slots;
        }

        private static void AddClipSlotFromStateField(
            List<FusionAnimatorClipSlot> slots,
            Component ownerState,
            string fieldName,
            string slotName)
        {
            if (slots == null || ownerState == null)
            {
                return;
            }

            Component nestedState = GetFieldValue(ownerState, fieldName) as Component;
            if (nestedState == null)
            {
                return;
            }

            AnimationClip clip = TryExtractFirstClipFromState(nestedState);
            if (clip == null)
            {
                return;
            }

            slots.Add(new FusionAnimatorClipSlot
            {
                Slot = slotName,
                Clip = clip,
                Speed = ReadFloatField(nestedState, "_speed", 1.0f),
                Loop = true,
            });
        }

        private static AnimationClip TryExtractFirstClipFromStateChildrenRecursive(Transform root, Component rootState)
        {
            if (root == null)
            {
                return null;
            }

            for (int childIndex = 0; childIndex < root.childCount; ++childIndex)
            {
                Transform child = root.GetChild(childIndex);
                if (child == null)
                {
                    continue;
                }

                Component childState = FindComponentByTypeName(child.gameObject, AnimationStateTypeName);
                if (childState != null && ReferenceEquals(childState, rootState) == false)
                {
                    AnimationClip clip = TryExtractFirstClipFromState(childState);
                    if (clip != null)
                    {
                        return clip;
                    }
                }

                AnimationClip nestedClip = TryExtractFirstClipFromStateChildrenRecursive(child, rootState);
                if (nestedClip != null)
                {
                    return nestedClip;
                }
            }

            return null;
        }

        private static AnimationClip TryExtractFirstClipFromState(Component stateComponent)
        {
            if (stateComponent == null)
            {
                return null;
            }

            Type stateType = stateComponent.GetType();

            if (IsTypeOrSubclassOf(stateType, ClipStateTypeName))
            {
                AnimationClip clip = GetFieldValue(stateComponent, "_clip") as AnimationClip;
                if (clip != null)
                {
                    return clip;
                }

                object node = GetFieldValue(stateComponent, "_node");
                clip = GetFieldValue(node, "Clip") as AnimationClip;
                if (clip != null)
                {
                    return clip;
                }

                return GetFieldValue(stateComponent, "Clip") as AnimationClip;
            }

            if (IsTypeOrSubclassOf(stateType, MultiClipStateTypeName))
            {
                object[] nodes = GetArrayField(stateComponent, "_nodes");
                for (int i = 0; i < nodes.Length; ++i)
                {
                    AnimationClip nodeClip = GetFieldValue(nodes[i], "Clip") as AnimationClip;
                    if (nodeClip != null)
                    {
                        return nodeClip;
                    }
                }

                return null;
            }

            if (IsTypeOrSubclassOf(stateType, MultiBlendTreeStateTypeName) ||
                IsTypeOrSubclassOf(stateType, MirrorBlendTreeStateTypeName) ||
                string.Equals(stateType.Name, "LookState", StringComparison.Ordinal))
            {
                object[] sets = GetArrayField(stateComponent, "_sets");
                for (int setIndex = 0; setIndex < sets.Length; ++setIndex)
                {
                    object[] nodes = GetArrayField(sets[setIndex], "_nodes");
                    for (int nodeIndex = 0; nodeIndex < nodes.Length; ++nodeIndex)
                    {
                        AnimationClip nodeClip = GetFieldValue(nodes[nodeIndex], "Clip") as AnimationClip;
                        if (nodeClip != null)
                        {
                            return nodeClip;
                        }
                    }
                }

                return null;
            }

            if (IsTypeOrSubclassOf(stateType, MixerStateTypeName))
            {
                return TryExtractRepresentativeClipFromMixer(stateComponent);
            }

            object[] fallbackNodes = GetArrayField(stateComponent, "_nodes");
            for (int i = 0; i < fallbackNodes.Length; ++i)
            {
                AnimationClip nodeClip = GetFieldValue(fallbackNodes[i], "Clip") as AnimationClip;
                if (nodeClip != null)
                {
                    return nodeClip;
                }
            }

            return null;
        }

        private static void AddVariantTransitions(
            ImportedGroup group,
            List<FusionAnimatorTransitionDefinition> transitions,
            ref int priority)
        {
            string selectorParameterId = group.Selector == SlotSelector.PendingWeapon
                ? ParamPendingWeaponSlotId
                : ParamWeaponSlotId;

            for (int fromIndex = 0; fromIndex < group.Variants.Count; ++fromIndex)
            {
                ImportedVariant fromVariant = group.Variants[fromIndex];
                if (fromVariant == null)
                {
                    continue;
                }

                for (int toIndex = 0; toIndex < group.Variants.Count; ++toIndex)
                {
                    if (toIndex == fromIndex)
                    {
                        continue;
                    }

                    ImportedVariant toVariant = group.Variants[toIndex];
                    if (toVariant == null)
                    {
                        continue;
                    }

                    List<List<FusionAnimatorConditionDefinition>> slotConditionSets = BuildSlotConditionSets(
                        selectorParameterId,
                        toVariant.SlotIndex,
                        group.Map);

                    for (int setIndex = 0; setIndex < slotConditionSets.Count; ++setIndex)
                    {
                        List<FusionAnimatorConditionDefinition> slotConditions = slotConditionSets[setIndex];
                        if (slotConditions == null || slotConditions.Count == 0)
                        {
                            continue;
                        }

                        List<FusionAnimatorConditionDefinition> conditions = new List<FusionAnimatorConditionDefinition>(slotConditions.Count + 1);
                        for (int conditionIndex = 0; conditionIndex < slotConditions.Count; ++conditionIndex)
                        {
                            conditions.Add(slotConditions[conditionIndex]);
                        }

                        if (group.RequireJetpackInactive)
                        {
                            conditions.Add(new FusionAnimatorConditionDefinition
                            {
                                ParameterId = ParamIsJetpackActiveId,
                                Operator = FusionAnimatorConditionOperator.IsFalse,
                                BoolValue = false,
                                IntValue = 0,
                                FloatValue = 0.0f,
                                Vector2Value = Vector2.zero,
                            });
                        }

                        List<List<FusionAnimatorConditionDefinition>> resolvedConditionSets =
                            ExpandSelectorConditionAlternatives(conditions, selectorParameterId);

                        for (int resolvedIndex = 0; resolvedIndex < resolvedConditionSets.Count; ++resolvedIndex)
                        {
                            List<FusionAnimatorConditionDefinition> resolvedConditions = resolvedConditionSets[resolvedIndex];
                            transitions.Add(new FusionAnimatorTransitionDefinition
                            {
                                Id = FusionAnimatorGraphAsset.NewId("transition"),
                                Name = string.Format("{0}: {1}->{2}", group.Name, fromVariant.SlotIndex, toVariant.SlotIndex),
                                FromStateId = fromVariant.StateId,
                                ToStateId = toVariant.StateId,
                                Priority = priority++,
                                Mute = false,
                                Solo = false,
                                HasExitTime = false,
                                ExitTimeNormalized = 1.0f,
                                StartOffsetNormalized = 0.0f,
                                FixedDuration = true,
                                BlendDurationSeconds = 0.1f,
                                InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext,
                                CanInterrupt = true,
                                Conditions = resolvedConditions,
                            });
                        }
                    }
                }
            }
        }

        private static void AddAnyStateDeadTransitions(
            ImportedGroup deadGroup,
            List<FusionAnimatorTransitionDefinition> transitions,
            ref int priority)
        {
            if (deadGroup == null || deadGroup.Selector == SlotSelector.None)
            {
                return;
            }

            string selectorParameterId = deadGroup.Selector == SlotSelector.PendingWeapon
                ? ParamPendingWeaponSlotId
                : ParamWeaponSlotId;

            for (int i = 0; i < deadGroup.Variants.Count; ++i)
            {
                ImportedVariant variant = deadGroup.Variants[i];
                if (variant == null)
                {
                    continue;
                }

                List<List<FusionAnimatorConditionDefinition>> slotConditionSets = BuildSlotConditionSets(
                    selectorParameterId,
                    variant.SlotIndex,
                    deadGroup.Map);

                for (int setIndex = 0; setIndex < slotConditionSets.Count; ++setIndex)
                {
                    List<FusionAnimatorConditionDefinition> slotConditions = slotConditionSets[setIndex];
                    if (slotConditions == null || slotConditions.Count == 0)
                    {
                        continue;
                    }

                    List<FusionAnimatorConditionDefinition> conditions = new List<FusionAnimatorConditionDefinition>(slotConditions.Count + 1);
                    for (int conditionIndex = 0; conditionIndex < slotConditions.Count; ++conditionIndex)
                    {
                        conditions.Add(slotConditions[conditionIndex]);
                    }
                    conditions.Add(new FusionAnimatorConditionDefinition
                    {
                        ParameterId = ParamIsDeadId,
                        Operator = FusionAnimatorConditionOperator.IsTrue,
                        BoolValue = true,
                        IntValue = 1,
                        FloatValue = 1.0f,
                        Vector2Value = Vector2.zero,
                    });

                    List<List<FusionAnimatorConditionDefinition>> resolvedConditionSets =
                        ExpandSelectorConditionAlternatives(conditions, selectorParameterId);

                    for (int resolvedIndex = 0; resolvedIndex < resolvedConditionSets.Count; ++resolvedIndex)
                    {
                        List<FusionAnimatorConditionDefinition> resolvedConditions = resolvedConditionSets[resolvedIndex];
                        transitions.Add(new FusionAnimatorTransitionDefinition
                        {
                            Id = FusionAnimatorGraphAsset.NewId("transition"),
                            Name = string.Format("Any->Dead ({0})", variant.SlotIndex),
                            FromStateId = FusionAnimatorGraphAsset.SpecialNodeAnyId,
                            ToStateId = variant.StateId,
                            Priority = priority++,
                            Mute = false,
                            Solo = false,
                            HasExitTime = false,
                            ExitTimeNormalized = 1.0f,
                            StartOffsetNormalized = 0.0f,
                            FixedDuration = true,
                            BlendDurationSeconds = 0.1f,
                            InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext,
                            CanInterrupt = true,
                            Conditions = resolvedConditions,
                        });
                    }
                }
            }
        }

        private static void AddCodeDrivenLayerTransitions(
            List<ImportedGroup> groups,
            List<FusionAnimatorTransitionDefinition> transitions,
            ref int priority)
        {
            ImportedGroup jump = FindFirstGroup(groups, "JumpState");
            ImportedGroup fall = FindFirstGroup(groups, "FallState");
            ImportedGroup land = FindFirstGroup(groups, "LandState");
            ImportedGroup jetpack = FindFirstGroup(groups, "JetpackState");
            ImportedGroup equip = FindFirstGroup(groups, "EquipState");
            ImportedGroup unequip = FindFirstGroup(groups, "UnequipState");
            ImportedGroup reload = FindFirstGroup(groups, "ReloadState");
            ImportedGroup shoot = FindFirstGroup(groups, "ShootState");
            ImportedGroup grenade = FindFirstGroup(groups, "GrenadeState");
            ImportedGroup turn = FindFirstGroup(groups, "TurnState");
            ImportedGroup look = FindFirstGroup(groups, "LookState");

            if (jump != null)
            {
                AddAnyToGroupTransitions(
                    jump,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamHasJumpedId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));
            }

            if (fall != null)
            {
                AddAnyToGroupTransitions(
                    fall,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamHasJumpedId, false),
                    BuildBoolCondition(ParamIsGroundedId, false),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));
            }

            if (jetpack != null)
            {
                AddAnyToGroupTransitions(
                    jetpack,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamIsJetpackActiveId, true),
                    BuildBoolCondition(ParamIsDeadId, false));
            }

            if (jump != null && fall != null)
            {
                AddGroupToGroupTransitions(
                    jump,
                    fall,
                    transitions,
                    ref priority,
                    hasExitTime: true,
                    exitTimeNormalized: 1.0f,
                    BuildBoolCondition(ParamIsGroundedId, false));
            }

            if (jump != null && land != null)
            {
                AddGroupToGroupTransitions(
                    jump,
                    land,
                    transitions,
                    ref priority,
                    hasExitTime: false,
                    exitTimeNormalized: 1.0f,
                    BuildBoolCondition(ParamIsGroundedId, true));
            }

            if (fall != null && land != null)
            {
                AddGroupToGroupTransitions(
                    fall,
                    land,
                    transitions,
                    ref priority,
                    hasExitTime: false,
                    exitTimeNormalized: 1.0f,
                    BuildBoolCondition(ParamIsGroundedId, true));
            }

            if (jetpack != null && fall != null)
            {
                AddGroupToGroupTransitions(
                    jetpack,
                    fall,
                    transitions,
                    ref priority,
                    hasExitTime: false,
                    exitTimeNormalized: 1.0f,
                    BuildBoolCondition(ParamIsJetpackActiveId, false),
                    BuildBoolCondition(ParamIsGroundedId, false));
            }

            if (jetpack != null && land != null)
            {
                AddGroupToGroupTransitions(
                    jetpack,
                    land,
                    transitions,
                    ref priority,
                    hasExitTime: false,
                    exitTimeNormalized: 1.0f,
                    BuildBoolCondition(ParamIsJetpackActiveId, false),
                    BuildBoolCondition(ParamIsGroundedId, true));
            }

            if (unequip != null && equip != null)
            {
                AddGroupToGroupTransitions(
                    unequip,
                    equip,
                    transitions,
                    ref priority,
                    true,
                    1.0f,
                    false,
                    BuildIntCondition(ParamPendingWeaponSlotId, FusionAnimatorConditionOperator.Greater, 0));
            }

            if (reload != null)
            {
                AddAnyToGroupTransitions(
                    reload,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamIsReloadingId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));
            }

            if (turn != null)
            {
                AddAnyToGroupTransitions(
                    turn,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamIsTurningId, true),
                    BuildBoolCondition(ParamIsDeadId, false));
            }

            if (look != null)
            {
                AddAnyToGroupTransitions(
                    look,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));
            }

            if (shoot != null)
            {
                AddAnyToGroupTransitions(
                    shoot,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamShootTriggerId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false),
                    BuildBoolCondition(ParamIsEquippingId, false),
                    BuildBoolCondition(ParamIsUnequippingId, false),
                    BuildBoolCondition(ParamIsReloadingId, false),
                    BuildBoolCondition(ParamIsThrowingId, false));
            }

            if (grenade != null)
            {
                AddAnyToGroupTransitions(
                    grenade,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamGrenadeEquipId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));

                AddAnyToGroupTransitions(
                    grenade,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamThrowHoldId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));

                AddAnyToGroupTransitions(
                    grenade,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamThrowStartId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));

                AddGrenadeInternalTransitions(grenade, transitions, ref priority);
            }

            if (equip != null)
            {
                AddAnyToGroupTransitions(
                    equip,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamEquipTriggerId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));
            }

            if (unequip != null)
            {
                AddAnyToGroupTransitions(
                    unequip,
                    transitions,
                    ref priority,
                    BuildBoolCondition(ParamUnequipTriggerId, true),
                    BuildBoolCondition(ParamIsDeadId, false),
                    BuildBoolCondition(ParamIsJetpackActiveId, false));
            }

            if (grenade != null && unequip != null)
            {
                AddGroupToGroupTransitions(
                    grenade,
                    unequip,
                    transitions,
                    ref priority,
                    false,
                    1.0f,
                    false,
                    BuildBoolCondition(ParamUnequipTriggerId, true));
            }
        }

		private static void AddAutoReturnTransitions(
			List<ImportedGroup> groups,
			Dictionary<string, string> neutralStateIdsByLayer,
			List<FusionAnimatorTransitionDefinition> transitions,
			ref int priority)
        {
            if (groups == null || neutralStateIdsByLayer == null || transitions == null)
            {
                return;
            }

            for (int i = 0; i < groups.Count; ++i)
            {
                ImportedGroup group = groups[i];
                if (group == null || group.Variants == null || group.Variants.Count == 0)
                {
                    continue;
                }

                if (neutralStateIdsByLayer.TryGetValue(group.LayerId, out string neutralStateId) == false ||
                    string.IsNullOrWhiteSpace(neutralStateId))
                {
                    continue;
                }

				bool hasExitTime = true;
				float exitTimeNormalized = 1.0f;
				float blendDurationSeconds = 0.2f;
				List<FusionAnimatorConditionDefinition> extraConditions = null;

				if (string.Equals(group.TypeName, "ReloadState", StringComparison.Ordinal) == true ||
					string.Equals(group.TypeName, "EquipState", StringComparison.Ordinal) == true ||
					string.Equals(group.TypeName, "ShootState", StringComparison.Ordinal) == true ||
					string.Equals(group.TypeName, "LandState", StringComparison.Ordinal) == true)
				{
					hasExitTime = true;
					exitTimeNormalized = 1.0f;
					blendDurationSeconds =
						string.Equals(group.TypeName, "ShootState", StringComparison.Ordinal) == true ||
						string.Equals(group.TypeName, "LandState", StringComparison.Ordinal) == true
							? 0.1f
							: 0.2f;
				}
				else if (string.Equals(group.TypeName, "UnequipState", StringComparison.Ordinal) == true)
				{
					hasExitTime = true;
					exitTimeNormalized = 1.0f;
					blendDurationSeconds = 0.2f;
					extraConditions = new List<FusionAnimatorConditionDefinition>(1)
					{
						BuildIntCondition(ParamPendingWeaponSlotId, FusionAnimatorConditionOperator.LessOrEqual, 0),
					};
				}
				else if (string.Equals(group.TypeName, "TurnState", StringComparison.Ordinal) == true)
				{
					hasExitTime = false;
					exitTimeNormalized = 1.0f;
					blendDurationSeconds = 0.1f;
					extraConditions = new List<FusionAnimatorConditionDefinition>(1)
					{
						BuildBoolCondition(ParamIsTurningId, false),
                    };
                }
				else if (string.Equals(group.TypeName, "JumpState", StringComparison.Ordinal) == true)
				{
					hasExitTime = false;
					exitTimeNormalized = 1.0f;
					blendDurationSeconds = 0.1f;
					extraConditions = new List<FusionAnimatorConditionDefinition>(1)
					{
						BuildBoolCondition(ParamIsGroundedId, true),
					};
				}
				else if (string.Equals(group.TypeName, "FallState", StringComparison.Ordinal) == true)
				{
					hasExitTime = false;
					exitTimeNormalized = 1.0f;
					blendDurationSeconds = 0.1f;
					extraConditions = new List<FusionAnimatorConditionDefinition>(1)
					{
						BuildBoolCondition(ParamIsGroundedId, true),
					};
				}
				else if (string.Equals(group.TypeName, "LookState", StringComparison.Ordinal) == true)
				{
					hasExitTime = false;
					exitTimeNormalized = 1.0f;
					blendDurationSeconds = 0.1f;
					extraConditions = new List<FusionAnimatorConditionDefinition>(1)
					{
						BuildBoolCondition(ParamIsJetpackActiveId, true),
					};
				}
                else
                {
                    continue;
                }

                for (int variantIndex = 0; variantIndex < group.Variants.Count; ++variantIndex)
                {
                    ImportedVariant variant = group.Variants[variantIndex];
                    if (variant == null || string.IsNullOrWhiteSpace(variant.StateId))
                    {
                        continue;
                    }

					AddExplicitTransition(
						transitions,
						ref priority,
						variant.StateId,
						neutralStateId,
						hasExitTime,
						exitTimeNormalized,
						blendDurationSeconds,
						extraConditions != null ? extraConditions.ToArray() : null);
				}
			}
		}

		private static void AddGrenadeInternalTransitions(
			ImportedGroup grenade,
			List<FusionAnimatorTransitionDefinition> transitions,
			ref int priority)
        {
            if (grenade == null || transitions == null)
            {
                return;
            }

            string holdStateId = GetFieldStateId(grenade, "_holdState");
            string armStateId = GetFieldStateId(grenade, "_armState");
			string throwStateId = GetFieldStateId(grenade, "_throwState");
			string reloadStateId = GetFieldStateId(grenade, "_reloadState");
			string equipStateId = GetFieldStateId(grenade, "_equipState");

            if (string.IsNullOrWhiteSpace(equipStateId) == false && string.IsNullOrWhiteSpace(holdStateId) == false)
            {
                AddExplicitTransition(transitions, ref priority, equipStateId, holdStateId, true, 0.80f, 0.20f);
            }

            if (string.IsNullOrWhiteSpace(reloadStateId) == false && string.IsNullOrWhiteSpace(holdStateId) == false)
            {
                AddExplicitTransition(transitions, ref priority, reloadStateId, holdStateId, true, 0.80f, 0.20f);
            }

            if (string.IsNullOrWhiteSpace(throwStateId) == false && string.IsNullOrWhiteSpace(holdStateId) == false)
            {
                AddExplicitTransition(transitions, ref priority, throwStateId, holdStateId, true, 0.95f, 0.20f);
            }

            if (string.IsNullOrWhiteSpace(holdStateId) == false && string.IsNullOrWhiteSpace(armStateId) == false)
            {
                AddExplicitTransition(
                    transitions,
                    ref priority,
                    holdStateId,
                    armStateId,
                    false,
                    1.0f,
                    0.10f,
                    BuildBoolCondition(ParamThrowStartId, true));

                AddExplicitTransition(
                    transitions,
                    ref priority,
                    holdStateId,
                    armStateId,
                    false,
                    1.0f,
                    0.10f,
                    BuildBoolCondition(ParamThrowHoldId, true));
            }

            if (string.IsNullOrWhiteSpace(reloadStateId) == false && string.IsNullOrWhiteSpace(armStateId) == false)
            {
                AddExplicitTransition(
                    transitions,
                    ref priority,
                    reloadStateId,
                    armStateId,
                    false,
                    1.0f,
                    0.10f,
                    BuildBoolCondition(ParamThrowStartId, true));

                AddExplicitTransition(
                    transitions,
                    ref priority,
                    reloadStateId,
                    armStateId,
                    false,
                    1.0f,
                    0.10f,
                    BuildBoolCondition(ParamThrowHoldId, true));
            }

            if (string.IsNullOrWhiteSpace(armStateId) == false && string.IsNullOrWhiteSpace(throwStateId) == false)
            {
                AddExplicitTransition(
                    transitions,
                    ref priority,
                    armStateId,
                    throwStateId,
                    false,
                    1.0f,
                    0.10f,
                    BuildBoolCondition(ParamThrowHoldId, false));
            }

            if (string.IsNullOrWhiteSpace(holdStateId) == false && string.IsNullOrWhiteSpace(reloadStateId) == false)
            {
                AddExplicitTransition(
                    transitions,
                    ref priority,
                    holdStateId,
                    reloadStateId,
                    false,
                    1.0f,
                    0.20f,
                    BuildBoolCondition(ParamIsReloadingId, true));
            }

            if (string.IsNullOrWhiteSpace(armStateId) == false && string.IsNullOrWhiteSpace(reloadStateId) == false)
            {
                AddExplicitTransition(
                    transitions,
                    ref priority,
                    armStateId,
                    reloadStateId,
                    false,
                    1.0f,
                    0.20f,
                    BuildBoolCondition(ParamIsReloadingId, true));
            }

            if (string.IsNullOrWhiteSpace(equipStateId) == false && string.IsNullOrWhiteSpace(reloadStateId) == false)
            {
                AddExplicitTransition(
                    transitions,
                    ref priority,
                    equipStateId,
                    reloadStateId,
                    false,
                    1.0f,
                    0.20f,
                    BuildBoolCondition(ParamIsReloadingId, true));
            }
        }

        private static string GetFieldStateId(ImportedGroup group, string fieldName)
        {
            if (group == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return string.Empty;
            }

            if (group.FieldStateIds != null && group.FieldStateIds.TryGetValue(fieldName, out string stateId) == true)
            {
                return stateId;
            }

            return string.Empty;
        }

        private static void AddExplicitTransition(
            List<FusionAnimatorTransitionDefinition> transitions,
            ref int priority,
            string fromStateId,
            string toStateId,
            bool hasExitTime,
            float exitTimeNormalized,
            float blendDurationSeconds,
            params FusionAnimatorConditionDefinition[] conditions)
        {
            if (transitions == null ||
                string.IsNullOrWhiteSpace(fromStateId) ||
                string.IsNullOrWhiteSpace(toStateId))
            {
                return;
            }

            List<FusionAnimatorConditionDefinition> transitionConditions = new List<FusionAnimatorConditionDefinition>(4);
            AddExtraConditions(transitionConditions, conditions);

            transitions.Add(new FusionAnimatorTransitionDefinition
            {
                Id = FusionAnimatorGraphAsset.NewId("transition"),
                Name = string.Format("{0}->{1}", fromStateId, toStateId),
                FromStateId = fromStateId,
                ToStateId = toStateId,
                Priority = priority++,
                Mute = false,
                Solo = false,
                HasExitTime = hasExitTime,
                ExitTimeNormalized = exitTimeNormalized,
                StartOffsetNormalized = 0.0f,
                FixedDuration = true,
                BlendDurationSeconds = blendDurationSeconds,
                InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext,
                CanInterrupt = true,
                Conditions = transitionConditions,
            });
        }

        private static ImportedGroup FindFirstGroup(List<ImportedGroup> groups, string typeName)
        {
            if (groups == null || string.IsNullOrWhiteSpace(typeName))
            {
                return null;
            }

            for (int i = 0; i < groups.Count; ++i)
            {
                ImportedGroup group = groups[i];
                if (group != null && string.Equals(group.TypeName, typeName, StringComparison.Ordinal))
                {
                    return group;
                }
            }

            return null;
        }

        private static void AddAnyToGroupTransitions(
            ImportedGroup toGroup,
            List<FusionAnimatorTransitionDefinition> transitions,
            ref int priority,
            params FusionAnimatorConditionDefinition[] extraConditions)
        {
            if (toGroup == null || transitions == null)
            {
                return;
            }

            string selectorParameterId = toGroup.Selector == SlotSelector.PendingWeapon
                ? ParamPendingWeaponSlotId
                : ParamWeaponSlotId;

            for (int i = 0; i < toGroup.Variants.Count; ++i)
            {
                ImportedVariant toVariant = toGroup.Variants[i];
                if (toVariant == null)
                {
                    continue;
                }

                List<List<FusionAnimatorConditionDefinition>> slotConditionSets = toGroup.Selector == SlotSelector.None
                    ? new List<List<FusionAnimatorConditionDefinition>> { new List<FusionAnimatorConditionDefinition>() }
                    : BuildSlotConditionSets(
                        selectorParameterId,
                        toVariant.SlotIndex,
                        toGroup.Map);

                for (int setIndex = 0; setIndex < slotConditionSets.Count; ++setIndex)
                {
                    List<FusionAnimatorConditionDefinition> slotConditions = slotConditionSets[setIndex];
                    if (slotConditions == null || slotConditions.Count == 0 && slotConditionSets.Count > 1)
                    {
                        continue;
                    }

                    List<FusionAnimatorConditionDefinition> conditions = new List<FusionAnimatorConditionDefinition>(
                        2 + (slotConditions != null ? slotConditions.Count : 0));
                    if (slotConditions != null)
                    {
                        for (int conditionIndex = 0; conditionIndex < slotConditions.Count; ++conditionIndex)
                        {
                            conditions.Add(slotConditions[conditionIndex]);
                        }
                    }

                    AddExtraConditions(conditions, extraConditions);

                    List<List<FusionAnimatorConditionDefinition>> resolvedConditionSets =
                        ExpandSelectorConditionAlternatives(conditions, selectorParameterId);

                    for (int resolvedIndex = 0; resolvedIndex < resolvedConditionSets.Count; ++resolvedIndex)
                    {
                        List<FusionAnimatorConditionDefinition> resolvedConditions = resolvedConditionSets[resolvedIndex];
                        transitions.Add(new FusionAnimatorTransitionDefinition
                        {
                            Id = FusionAnimatorGraphAsset.NewId("transition"),
                            Name = string.Format("Any->{0}", toVariant.StateId),
                            FromStateId = FusionAnimatorGraphAsset.SpecialNodeAnyId,
                            ToStateId = toVariant.StateId,
                            Priority = priority++,
                            Mute = false,
                            Solo = false,
                            HasExitTime = false,
                            ExitTimeNormalized = 1.0f,
                            StartOffsetNormalized = 0.0f,
                            FixedDuration = true,
                            BlendDurationSeconds = 0.1f,
                            InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext,
                            CanInterrupt = true,
                            Conditions = resolvedConditions,
                        });
                    }
                }
            }
        }

        private static void AddGroupToGroupTransitions(
            ImportedGroup fromGroup,
            ImportedGroup toGroup,
            List<FusionAnimatorTransitionDefinition> transitions,
            ref int priority,
            bool hasExitTime,
            float exitTimeNormalized,
            params FusionAnimatorConditionDefinition[] extraConditions)
        {
            AddGroupToGroupTransitions(
                fromGroup,
                toGroup,
                transitions,
                ref priority,
                hasExitTime,
                exitTimeNormalized,
                true,
                extraConditions);
        }

        private static void AddGroupToGroupTransitions(
            ImportedGroup fromGroup,
            ImportedGroup toGroup,
            List<FusionAnimatorTransitionDefinition> transitions,
            ref int priority,
            bool hasExitTime,
            float exitTimeNormalized,
            bool matchByFromSlot,
            params FusionAnimatorConditionDefinition[] extraConditions)
        {
            if (fromGroup == null || toGroup == null || transitions == null)
            {
                return;
            }

            string selectorParameterId = toGroup.Selector == SlotSelector.PendingWeapon
                ? ParamPendingWeaponSlotId
                : ParamWeaponSlotId;

            for (int fromIndex = 0; fromIndex < fromGroup.Variants.Count; ++fromIndex)
            {
                ImportedVariant fromVariant = fromGroup.Variants[fromIndex];
                if (fromVariant == null)
                {
                    continue;
                }

                int toVariantStart = matchByFromSlot == true ? -1 : 0;
                int toVariantEnd = matchByFromSlot == true ? -1 : toGroup.Variants.Count - 1;

                if (matchByFromSlot == true)
                {
                    ImportedVariant mappedVariant = FindBestVariantBySlot(toGroup, fromVariant.SlotIndex);
                    if (mappedVariant == null)
                    {
                        continue;
                    }

                    for (int i = 0; i < toGroup.Variants.Count; ++i)
                    {
                        if (ReferenceEquals(toGroup.Variants[i], mappedVariant))
                        {
                            toVariantStart = i;
                            toVariantEnd = i;
                            break;
                        }
                    }

                    if (toVariantStart < 0)
                    {
                        continue;
                    }
                }

                for (int toIndex = toVariantStart; toIndex <= toVariantEnd; ++toIndex)
                {
                    ImportedVariant toVariant = toGroup.Variants[toIndex];
                    if (toVariant == null)
                    {
                        continue;
                    }

                    List<List<FusionAnimatorConditionDefinition>> slotConditionSets = toGroup.Selector == SlotSelector.None
                        ? new List<List<FusionAnimatorConditionDefinition>> { new List<FusionAnimatorConditionDefinition>() }
                        : BuildSlotConditionSets(
                            selectorParameterId,
                            toVariant.SlotIndex,
                            toGroup.Map);

                    for (int setIndex = 0; setIndex < slotConditionSets.Count; ++setIndex)
                    {
                        List<FusionAnimatorConditionDefinition> slotConditions = slotConditionSets[setIndex];
                        if (slotConditions == null || slotConditions.Count == 0 && slotConditionSets.Count > 1)
                        {
                            continue;
                        }

                        List<FusionAnimatorConditionDefinition> conditions = new List<FusionAnimatorConditionDefinition>(
                            2 + (slotConditions != null ? slotConditions.Count : 0));
                        if (slotConditions != null)
                        {
                            for (int conditionIndex = 0; conditionIndex < slotConditions.Count; ++conditionIndex)
                            {
                                conditions.Add(slotConditions[conditionIndex]);
                            }
                        }

                        AddExtraConditions(conditions, extraConditions);

                        List<List<FusionAnimatorConditionDefinition>> resolvedConditionSets =
                            ExpandSelectorConditionAlternatives(conditions, selectorParameterId);

                        for (int resolvedIndex = 0; resolvedIndex < resolvedConditionSets.Count; ++resolvedIndex)
                        {
                            List<FusionAnimatorConditionDefinition> resolvedConditions = resolvedConditionSets[resolvedIndex];
                            transitions.Add(new FusionAnimatorTransitionDefinition
                            {
                                Id = FusionAnimatorGraphAsset.NewId("transition"),
                                Name = string.Format("{0}->{1}", fromVariant.StateId, toVariant.StateId),
                                FromStateId = fromVariant.StateId,
                                ToStateId = toVariant.StateId,
                                Priority = priority++,
                                Mute = false,
                                Solo = false,
                                HasExitTime = hasExitTime,
                                ExitTimeNormalized = exitTimeNormalized,
                                StartOffsetNormalized = 0.0f,
                                FixedDuration = true,
                                BlendDurationSeconds = 0.1f,
                                InterruptionSource = FusionAnimatorInterruptionSource.CurrentThenNext,
                                CanInterrupt = true,
                                Conditions = resolvedConditions,
                            });
                        }
                    }
                }
            }
        }

        private static ImportedVariant FindBestVariantBySlot(ImportedGroup group, int slotIndex)
        {
            if (group == null || group.Variants.Count == 0)
            {
                return null;
            }

            for (int i = 0; i < group.Variants.Count; ++i)
            {
                ImportedVariant variant = group.Variants[i];
                if (variant != null && variant.SlotIndex == slotIndex)
                {
                    return variant;
                }
            }

            int mappedSlot = slotIndex;
            if (mappedSlot > 2)
            {
                if (group.Map == SlotMap.GrenadeToZero)
                {
                    mappedSlot = 0;
                }
                else if (group.Map == SlotMap.GrenadeToOne)
                {
                    mappedSlot = 1;
                }
            }

            for (int i = 0; i < group.Variants.Count; ++i)
            {
                ImportedVariant variant = group.Variants[i];
                if (variant != null && variant.SlotIndex == mappedSlot)
                {
                    return variant;
                }
            }

            return group.Variants[0];
        }

        private static void AddExtraConditions(
            List<FusionAnimatorConditionDefinition> targetConditions,
            FusionAnimatorConditionDefinition[] extraConditions)
        {
            if (targetConditions == null || extraConditions == null)
            {
                return;
            }

            for (int i = 0; i < extraConditions.Length; ++i)
            {
                FusionAnimatorConditionDefinition condition = extraConditions[i];
                if (condition == null)
                {
                    continue;
                }

                targetConditions.Add(CloneCondition(condition));
            }
        }

        private static List<List<FusionAnimatorConditionDefinition>> ExpandSelectorConditionAlternatives(
            List<FusionAnimatorConditionDefinition> conditions,
            string selectorParameterId)
        {
            List<List<FusionAnimatorConditionDefinition>> resolved = new List<List<FusionAnimatorConditionDefinition>>(2);
            if (conditions == null || conditions.Count == 0)
            {
                resolved.Add(new List<FusionAnimatorConditionDefinition>());
                return resolved;
            }

            if (string.IsNullOrWhiteSpace(selectorParameterId))
            {
                resolved.Add(CloneConditions(conditions));
                return resolved;
            }

            List<FusionAnimatorConditionDefinition> sharedConditions = new List<FusionAnimatorConditionDefinition>(conditions.Count);
            List<FusionAnimatorConditionDefinition> selectorAlternatives = null;

            for (int i = 0; i < conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = conditions[i];
                if (condition == null)
                {
                    continue;
                }

                bool isSelectorAlternative =
                    string.Equals(condition.ParameterId, selectorParameterId, StringComparison.Ordinal) &&
                    (condition.Operator == FusionAnimatorConditionOperator.Equal ||
                     (condition.Operator == FusionAnimatorConditionOperator.Greater && condition.IntValue > 0));

                if (isSelectorAlternative)
                {
                    if (selectorAlternatives == null)
                    {
                        selectorAlternatives = new List<FusionAnimatorConditionDefinition>(2);
                    }

                    selectorAlternatives.Add(CloneCondition(condition));
                    continue;
                }

                sharedConditions.Add(CloneCondition(condition));
            }

            if (selectorAlternatives == null || selectorAlternatives.Count <= 1)
            {
                if (selectorAlternatives != null && selectorAlternatives.Count == 1)
                {
                    sharedConditions.Add(CloneCondition(selectorAlternatives[0]));
                }

                resolved.Add(sharedConditions);
                return resolved;
            }

            for (int i = 0; i < selectorAlternatives.Count; ++i)
            {
                List<FusionAnimatorConditionDefinition> conditionSet = CloneConditions(sharedConditions);
                conditionSet.Add(CloneCondition(selectorAlternatives[i]));
                resolved.Add(conditionSet);
            }

            return resolved;
        }

        private static List<FusionAnimatorConditionDefinition> CloneConditions(List<FusionAnimatorConditionDefinition> source)
        {
            List<FusionAnimatorConditionDefinition> cloned = new List<FusionAnimatorConditionDefinition>(source != null ? source.Count : 0);
            if (source == null)
            {
                return cloned;
            }

            for (int i = 0; i < source.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = source[i];
                if (condition == null)
                {
                    continue;
                }

                cloned.Add(CloneCondition(condition));
            }

            return cloned;
        }

        private static FusionAnimatorConditionDefinition CloneCondition(FusionAnimatorConditionDefinition condition)
        {
            if (condition == null)
            {
                return null;
            }

            return new FusionAnimatorConditionDefinition
            {
                ParameterId = condition.ParameterId,
                Operator = condition.Operator,
                UseAbsoluteValue = condition.UseAbsoluteValue,
                BoolValue = condition.BoolValue,
                IntValue = condition.IntValue,
                FloatValue = condition.FloatValue,
                Vector2Value = condition.Vector2Value,
            };
        }

        private static List<List<FusionAnimatorConditionDefinition>> BuildSlotConditionSets(
            string parameterId,
            int slotIndex,
            SlotMap map)
        {
            List<List<FusionAnimatorConditionDefinition>> conditionSets = new List<List<FusionAnimatorConditionDefinition>>(2);

            if (map == SlotMap.GrenadeToZero && slotIndex == 0)
            {
                conditionSets.Add(new List<FusionAnimatorConditionDefinition>(1)
                {
                    BuildIntCondition(parameterId, FusionAnimatorConditionOperator.Equal, 0),
                });
                conditionSets.Add(new List<FusionAnimatorConditionDefinition>(1)
                {
                    BuildIntCondition(parameterId, FusionAnimatorConditionOperator.Greater, 2),
                });
                return conditionSets;
            }

            if (map == SlotMap.GrenadeToOne && slotIndex == 1)
            {
                conditionSets.Add(new List<FusionAnimatorConditionDefinition>(1)
                {
                    BuildIntCondition(parameterId, FusionAnimatorConditionOperator.Equal, 1),
                });
                conditionSets.Add(new List<FusionAnimatorConditionDefinition>(1)
                {
                    BuildIntCondition(parameterId, FusionAnimatorConditionOperator.Greater, 2),
                });
                return conditionSets;
            }

            conditionSets.Add(new List<FusionAnimatorConditionDefinition>(1)
            {
                BuildIntCondition(parameterId, FusionAnimatorConditionOperator.Equal, slotIndex),
            });
            return conditionSets;
        }

        private static FusionAnimatorConditionDefinition BuildIntCondition(
            string parameterId,
            FusionAnimatorConditionOperator op,
            int value)
        {
            return new FusionAnimatorConditionDefinition
            {
                ParameterId = parameterId,
                Operator = op,
                BoolValue = value != 0,
                IntValue = value,
                FloatValue = value,
                Vector2Value = new Vector2(value, 0.0f),
            };
        }

        private static FusionAnimatorConditionDefinition BuildBoolCondition(string parameterId, bool value)
        {
            return new FusionAnimatorConditionDefinition
            {
                ParameterId = parameterId,
                Operator = value ? FusionAnimatorConditionOperator.IsTrue : FusionAnimatorConditionOperator.IsFalse,
                BoolValue = value,
                IntValue = value ? 1 : 0,
                FloatValue = value ? 1.0f : 0.0f,
                Vector2Value = Vector2.zero,
            };
        }

        private static void ResolveSelectorPolicy(string typeName, out SlotSelector selector, out SlotMap map, out bool requireJetpackInactive)
        {
            selector = SlotSelector.None;
            map = SlotMap.Exact;
            requireJetpackInactive = false;

            switch (typeName)
            {
                case "MoveState":
                case "JumpState":
                case "FallState":
                case "LandState":
                {
                    selector = SlotSelector.CurrentWeapon;
                    map = SlotMap.GrenadeToZero;
                    break;
                }
                case "EquipState":
                {
                    selector = SlotSelector.PendingWeapon;
                    map = SlotMap.GrenadeToOne;
                    break;
                }
                case "DeadState":
                case "ReloadState":
                case "UnequipState":
                case "ShootState":
                case "TurnState":
                {
                    selector = SlotSelector.CurrentWeapon;
                    map = SlotMap.GrenadeToOne;
                    break;
                }
                case "LookState":
                {
                    selector = SlotSelector.CurrentWeapon;
                    map = SlotMap.GrenadeToOne;
                    requireJetpackInactive = true;
                    break;
                }
            }
        }

        private static bool TryResolveAnimationController(
            UnityEngine.Object source,
            out GameObject sourceRoot,
            out Component animationController)
        {
            sourceRoot = null;
            animationController = null;

            if (source == null)
            {
                return false;
            }

            if (source is GameObject gameObjectSource)
            {
                sourceRoot = gameObjectSource;
            }
            else if (source is Component componentSource)
            {
                sourceRoot = componentSource.gameObject;
            }
            else
            {
                return false;
            }

            MonoBehaviour[] behaviours = sourceRoot.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; ++i)
            {
                MonoBehaviour behaviour = behaviours[i];
                if (behaviour == null)
                {
                    continue;
                }

                if (IsTypeOrSubclassOf(behaviour.GetType(), AnimationControllerTypeName))
                {
                    animationController = behaviour;
                    return true;
                }
            }

            return false;
        }

        private static Component FindComponentByTypeName(GameObject gameObject, string fullTypeName)
        {
            if (gameObject == null)
            {
                return null;
            }

            Component[] components = gameObject.GetComponents<Component>();
            for (int i = 0; i < components.Length; ++i)
            {
                Component component = components[i];
                if (component == null)
                {
                    continue;
                }

                if (IsTypeOrSubclassOf(component.GetType(), fullTypeName))
                {
                    return component;
                }
            }

            return null;
        }

        private static bool IsTypeOrSubclassOf(Type type, string fullTypeName)
        {
            while (type != null)
            {
                if (string.Equals(type.FullName, fullTypeName, StringComparison.Ordinal))
                {
                    return true;
                }

                type = type.BaseType;
            }

            return false;
        }

        private static object[] GetArrayField(object source, string fieldName)
        {
            object value = GetFieldValue(source, fieldName);
            if (value is IList list)
            {
                object[] result = new object[list.Count];
                for (int i = 0; i < list.Count; ++i)
                {
                    result[i] = list[i];
                }

                return result;
            }

            return Array.Empty<object>();
        }

        private static object GetFieldValue(object source, string fieldName)
        {
            if (source == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return null;
            }

            Type current = source.GetType();
            while (current != null)
            {
                FieldInfo field = current.GetField(fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(source);
                }

                current = current.BaseType;
            }

            return null;
        }

        private static bool ReadBoolField(object source, string fieldName, bool fallback)
        {
            object value = GetFieldValue(source, fieldName);
            return value is bool boolValue ? boolValue : fallback;
        }

        private static float ReadFloatField(object source, string fieldName, float fallback)
        {
            object value = GetFieldValue(source, fieldName);
            if (value is float floatValue)
            {
                return floatValue;
            }

            if (value is double doubleValue)
            {
                return (float)doubleValue;
            }

            return fallback;
        }

        private static Vector2 ReadVector2Field(object source, string fieldName, Vector2 fallback)
        {
            object value = GetFieldValue(source, fieldName);
            return value is Vector2 vectorValue ? vectorValue : fallback;
        }

        private static string BuildVariantStateName(string baseStateName, int slotIndex)
        {
            return string.Format("{0} ({1})", baseStateName, SlotLabel(slotIndex));
        }

        private static string SlotLabel(int slotIndex)
        {
            switch (slotIndex)
            {
                case 0: return "Unarmed";
                case 1: return "Pistol";
                case 2: return "Rifle";
                default: return string.Format("Set {0}", slotIndex);
            }
        }

        private static string BuildVariantStateId(Component stateComponent, int slotIndex, ref int fallbackIndex)
        {
            string baseId = BuildStableId("state", stateComponent, stateComponent != null ? stateComponent.name : "state", fallbackIndex++);
            return string.Format("{0}_set{1}", baseId, slotIndex);
        }

        private static string BuildStableId(string prefix, UnityEngine.Object source, string fallbackName, int fallbackIndex)
        {
            if (source != null && AssetDatabase.TryGetGUIDAndLocalFileIdentifier(source, out string _, out long localId))
            {
                string suffix = localId < 0 ? ("n" + (-localId)) : localId.ToString();
                return string.Format("{0}_{1}", prefix, suffix);
            }

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
            for (int i = 0; i < chars.Length; ++i)
            {
                char c = chars[i];
                bool isAlphaNum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
                chars[i] = isAlphaNum ? c : '_';
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

            if (graph == null || graph.States == null || graph.States.Count == 0)
            {
                graph.EntryNodePosition = new Vector2(-300.0f, -120.0f);
                graph.AnyNodePosition = new Vector2(-300.0f, 20.0f);
                graph.ExitNodePosition = new Vector2(300.0f, -40.0f);
                return;
            }

            float minX = float.MaxValue;
            float minY = float.MaxValue;
            float maxX = float.MinValue;

            for (int i = 0; i < graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = graph.States[i];
                if (state == null)
                {
                    continue;
                }

                minX = Mathf.Min(minX, state.NodePosition.x);
                minY = Mathf.Min(minY, state.NodePosition.y);
                maxX = Mathf.Max(maxX, state.NodePosition.x);
            }

            if (minX == float.MaxValue)
            {
                minX = 0.0f;
                minY = 0.0f;
                maxX = 0.0f;
            }

            graph.EntryNodePosition = new Vector2(minX - 280.0f, minY - 80.0f);
            graph.AnyNodePosition = new Vector2(minX - 280.0f, minY + 60.0f);
            graph.ExitNodePosition = new Vector2(maxX + 340.0f, minY - 20.0f);
        }
    }
}
