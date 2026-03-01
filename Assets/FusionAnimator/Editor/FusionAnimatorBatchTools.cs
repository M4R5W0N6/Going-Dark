using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace FusionAnimator.Editor
{
    public static class FusionAnimatorBatchTools
    {
        private const string DefaultGraphPath = "Assets/FusionAnimator/Graphs/FusionAnimatorGraph.asset";

        public static void RebuildDefaultFusionAgentGraph()
        {
            FusionAnimatorGraphAsset graph = AssetDatabase.LoadAssetAtPath<FusionAnimatorGraphAsset>(DefaultGraphPath);
            if (graph == null)
            {
                throw new InvalidOperationException($"Graph not found at '{DefaultGraphPath}'.");
            }

            UnityEngine.Object source = graph.PreviewSource;
            if (source == null)
            {
                throw new InvalidOperationException("Graph PreviewSource is not assigned.");
            }

            IFusionAnimatorGraphConverter converter = FusionAnimatorGraphConverterRegistry
                .GetConverters()
                .FirstOrDefault(item => item is FusionAgentToFusionConverter);

            if (converter == null)
            {
                throw new InvalidOperationException("Fusion agent converter is not available.");
            }

            if (converter.CanConvert(source) == false)
            {
                throw new InvalidOperationException($"Source '{source.name}' is not supported by the fusion agent converter.");
            }

            if (converter.TryConvert(source, graph, out string message) == false)
            {
                throw new InvalidOperationException($"Conversion failed: {message}");
            }

            List<FusionAnimatorValidationIssue> validationIssues = FusionAnimatorValidator.Validate(graph);
            int validationErrorCount = validationIssues.Count(issue => issue.Severity == FusionAnimatorValidationSeverity.Error);
            if (validationErrorCount > 0)
            {
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < validationIssues.Count; ++i)
                {
                    FusionAnimatorValidationIssue issue = validationIssues[i];
                    if (issue.Severity != FusionAnimatorValidationSeverity.Error)
                    {
                        continue;
                    }

                    builder.AppendLine(string.Format("[{0}] {1}: {2}", issue.Severity, issue.Context, issue.Message));
                }

                throw new InvalidOperationException(string.Format("Graph validation failed with {0} error(s).\n{1}", validationErrorCount, builder.ToString()));
            }

            List<string> parityIssues = FusionAnimatorParityChecks.ValidateGameplayTimingParity(graph);
            if (parityIssues.Count > 0)
            {
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < parityIssues.Count; ++i)
                {
                    builder.AppendLine("- " + parityIssues[i]);
                }

                throw new InvalidOperationException("Gameplay parity checks failed:\n" + builder.ToString());
            }

            EditorUtility.SetDirty(graph);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"Fusion animator graph rebuilt. {message}");
        }
    }

    internal static class FusionAnimatorParityChecks
    {
        private const float Epsilon = 0.0001f;

        public static List<string> ValidateGameplayTimingParity(FusionAnimatorGraphAsset graph)
        {
            List<string> issues = new List<string>();

            ValidateControllerTimingConstant(issues, "UPPER_BODY_EQUIP_ARM_TIME", 0.4f);
            ValidateControllerTimingConstant(issues, "UPPER_BODY_UNEQUIP_DISARM_TIME", 0.5f);
            ValidateControllerTimingConstant(issues, "UPPER_BODY_UNEQUIP_SWITCH_TIME", 1.0f);
            ValidateControllerTimingConstant(issues, "UPPER_BODY_THROW_START_TIME", 0.2f);
            ValidateControllerTimingConstant(issues, "UPPER_BODY_GRENDE_EQUIP_TIME", 0.5f);
            ValidateControllerTimingConstant(issues, "UPPER_BODY_GRENDE_THROW_FIRE_TIME", 0.45f);
            ValidateControllerTimingConstant(issues, "UPPER_BODY_RELOAD_EXIT_TIME", 0.9f);
            ValidateControllerTimingConstant(issues, "UPPER_BODY_RELOAD_RETURN_TIME", 0.05f);
            ValidateControllerTimingConstant(issues, "SHOOT_TRIGGER_DURATION", 0.05f);

            if (graph == null || graph.States == null || graph.Transitions == null)
            {
                issues.Add("Graph is null or missing states/transitions.");
                return issues;
            }

            Dictionary<string, string> stateNamesById = new Dictionary<string, string>(StringComparer.Ordinal);
            for (int i = 0; i < graph.States.Count; ++i)
            {
                FusionAnimatorStateDefinition state = graph.States[i];
                if (state == null || string.IsNullOrWhiteSpace(state.Id))
                {
                    continue;
                }

                stateNamesById[state.Id] = state.Name ?? string.Empty;
            }

            RequireTransition(
                issues,
                graph,
                stateNamesById,
                "Grenade/Equip",
                "Grenade/Hold",
                transition => transition.HasExitTime && Approximately(transition.ExitTimeNormalized, 0.8f) && Approximately(transition.BlendDurationSeconds, 0.2f),
                "Missing Grenade/Equip -> Grenade/Hold parity transition (exit 0.8, blend 0.2).");

            RequireTransition(
                issues,
                graph,
                stateNamesById,
                "Grenade/Reload",
                "Grenade/Hold",
                transition => transition.HasExitTime && Approximately(transition.ExitTimeNormalized, 0.8f) && Approximately(transition.BlendDurationSeconds, 0.2f),
                "Missing Grenade/Reload -> Grenade/Hold parity transition (exit 0.8, blend 0.2).");

            RequireTransition(
                issues,
                graph,
                stateNamesById,
                "Grenade/Throw",
                "Grenade/Hold",
                transition => transition.HasExitTime && Approximately(transition.ExitTimeNormalized, 0.95f) && Approximately(transition.BlendDurationSeconds, 0.2f),
                "Missing Grenade/Throw -> Grenade/Hold parity transition (exit 0.95, blend 0.2).");

            RequireTransition(
                issues,
                graph,
                stateNamesById,
                "Grenade/Hold",
                "Grenade/Arm",
                transition => HasBoolCondition(transition, "param_throw_start", true),
                "Missing Grenade/Hold -> Grenade/Arm transition gated by param_throw_start=true.");

            RequireTransition(
                issues,
                graph,
                stateNamesById,
                "Grenade/Hold",
                "Grenade/Arm",
                transition => HasBoolCondition(transition, "param_throw_hold", true),
                "Missing Grenade/Hold -> Grenade/Arm transition gated by param_throw_hold=true.");

            RequireTransition(
                issues,
                graph,
                stateNamesById,
                "Grenade/Arm",
                "Grenade/Throw",
                transition => HasBoolCondition(transition, "param_throw_hold", false),
                "Missing Grenade/Arm -> Grenade/Throw transition gated by param_throw_hold=false.");

            RequireAnyTransitionFromState(
                issues,
                graph,
                stateNamesById,
                "Unequip",
                transition =>
                    transition.HasExitTime &&
                    Approximately(transition.ExitTimeNormalized, 1.0f) &&
                    HasIntCondition(transition, "param_pending_weapon_slot", FusionAnimatorConditionOperator.LessOrEqual, 0),
                "Missing Unequip auto-return parity transition (exit 1.0 + pending slot <= 0).");

            RequireAnyTransitionFromState(
                issues,
                graph,
                stateNamesById,
                "Reload",
                transition => transition.HasExitTime && Approximately(transition.ExitTimeNormalized, 1.0f),
                "Missing Reload auto-return parity transition (exit 1.0).");

            RequireAnyTransitionFromState(
                issues,
                graph,
                stateNamesById,
                "Shoot",
                transition => transition.HasExitTime && Approximately(transition.ExitTimeNormalized, 1.0f) && Approximately(transition.BlendDurationSeconds, 0.1f),
                "Missing Shoot auto-return parity transition (exit 1.0, blend 0.1).");

            return issues;
        }

        private static void ValidateControllerTimingConstant(List<string> issues, string fieldName, float expected)
        {
            if (issues == null || string.IsNullOrWhiteSpace(fieldName))
            {
                return;
            }

            Type controllerType = ResolveTypeByName("TPSBR.CharacterAnimationController");
            if (controllerType == null)
            {
                issues.Add("Could not resolve type 'TPSBR.CharacterAnimationController' for timing parity checks.");
                return;
            }

            FieldInfo field = controllerType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Static);
            if (field == null)
            {
                issues.Add(string.Format("Could not locate CharacterAnimationController constant '{0}'.", fieldName));
                return;
            }

            object raw = field.GetRawConstantValue();
            if (raw == null)
            {
                issues.Add(string.Format("CharacterAnimationController constant '{0}' has no value.", fieldName));
                return;
            }

            float actual = Convert.ToSingle(raw, CultureInfo.InvariantCulture);
            if (Approximately(actual, expected) == false)
            {
                issues.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "Timing constant '{0}' mismatch: expected {1:0.###}, actual {2:0.###}.",
                    fieldName,
                    expected,
                    actual));
            }
        }

        private static void RequireTransition(
            List<string> issues,
            FusionAnimatorGraphAsset graph,
            Dictionary<string, string> stateNamesById,
            string fromStateName,
            string toStateName,
            Func<FusionAnimatorTransitionDefinition, bool> predicate,
            string message)
        {
            if (issues == null || graph == null || graph.Transitions == null || stateNamesById == null)
            {
                return;
            }

            for (int i = 0; i < graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = graph.Transitions[i];
                if (transition == null)
                {
                    continue;
                }

                if (stateNamesById.TryGetValue(transition.FromStateId ?? string.Empty, out string fromName) == false ||
                    stateNamesById.TryGetValue(transition.ToStateId ?? string.Empty, out string toName) == false)
                {
                    continue;
                }

                if (StateNameMatches(fromName, fromStateName) == false || StateNameMatches(toName, toStateName) == false)
                {
                    continue;
                }

                if (predicate == null || predicate(transition))
                {
                    return;
                }
            }

            issues.Add(message);
        }

        private static void RequireAnyTransitionFromState(
            List<string> issues,
            FusionAnimatorGraphAsset graph,
            Dictionary<string, string> stateNamesById,
            string fromCanonicalName,
            Func<FusionAnimatorTransitionDefinition, bool> predicate,
            string message)
        {
            if (issues == null || graph == null || graph.Transitions == null || stateNamesById == null)
            {
                return;
            }

            for (int i = 0; i < graph.Transitions.Count; ++i)
            {
                FusionAnimatorTransitionDefinition transition = graph.Transitions[i];
                if (transition == null)
                {
                    continue;
                }

                if (stateNamesById.TryGetValue(transition.FromStateId ?? string.Empty, out string fromName) == false)
                {
                    continue;
                }

                if (StateNameMatches(fromName, fromCanonicalName) == false)
                {
                    continue;
                }

                if (predicate == null || predicate(transition))
                {
                    return;
                }
            }

            issues.Add(message);
        }

        private static bool HasBoolCondition(FusionAnimatorTransitionDefinition transition, string parameterId, bool expected)
        {
            if (transition == null || transition.Conditions == null)
            {
                return false;
            }

            for (int i = 0; i < transition.Conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = transition.Conditions[i];
                if (condition == null)
                {
                    continue;
                }

                if (string.Equals(condition.ParameterId, parameterId, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                if (condition.Operator == FusionAnimatorConditionOperator.Equal || condition.Operator == FusionAnimatorConditionOperator.IsTrue || condition.Operator == FusionAnimatorConditionOperator.IsFalse)
                {
                    bool actual = condition.Operator == FusionAnimatorConditionOperator.IsTrue
                        ? true
                        : condition.Operator == FusionAnimatorConditionOperator.IsFalse
                            ? false
                            : condition.BoolValue;

                    if (actual == expected)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static bool HasIntCondition(FusionAnimatorTransitionDefinition transition, string parameterId, FusionAnimatorConditionOperator op, int expectedValue)
        {
            if (transition == null || transition.Conditions == null)
            {
                return false;
            }

            for (int i = 0; i < transition.Conditions.Count; ++i)
            {
                FusionAnimatorConditionDefinition condition = transition.Conditions[i];
                if (condition == null)
                {
                    continue;
                }

                if (string.Equals(condition.ParameterId, parameterId, StringComparison.OrdinalIgnoreCase) == false)
                {
                    continue;
                }

                if (condition.Operator == op && condition.IntValue == expectedValue)
                {
                    return true;
                }
            }

            return false;
        }

        private static bool StateNameMatches(string candidate, string expected)
        {
            string normalizedCandidate = NormalizeStateName(candidate);
            string normalizedExpected = NormalizeStateName(expected);
            return string.Equals(normalizedCandidate, normalizedExpected, StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeStateName(string stateName)
        {
            if (string.IsNullOrWhiteSpace(stateName))
            {
                return string.Empty;
            }

            string normalized = stateName.Trim();
            int variantOpen = normalized.LastIndexOf('(');
            int variantClose = normalized.LastIndexOf(')');
            if (variantOpen >= 0 && variantClose > variantOpen)
            {
                string suffix = normalized.Substring(variantOpen);
                if (suffix.IndexOf('/') < 0)
                {
                    normalized = normalized.Substring(0, variantOpen).TrimEnd();
                }
            }

            return normalized;
        }

        private static bool Approximately(float lhs, float rhs)
        {
            return Mathf.Abs(lhs - rhs) <= Epsilon;
        }

        private static Type ResolveTypeByName(string fullName)
        {
            if (string.IsNullOrWhiteSpace(fullName))
            {
                return null;
            }

            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; ++i)
            {
                Assembly assembly = assemblies[i];
                if (assembly == null)
                {
                    continue;
                }

                Type type = assembly.GetType(fullName, false, false);
                if (type != null)
                {
                    return type;
                }
            }

            return null;
        }
    }
}
