using System;
using System.Collections.Generic;
using UnityEngine;

namespace FusionAnimator
{
    public interface IFusionAnimatorParameterSource
    {
        bool TryGetBool(string parameterId, out bool value);
        bool TryGetInt(string parameterId, out int value);
        bool TryGetFloat(string parameterId, out float value);
        bool TryGetVector2(string parameterId, out Vector2 value);
        bool TryPeekTrigger(string parameterId, out bool fired);
        bool TryConsumeTrigger(string parameterId, out bool fired);
    }

    [Serializable]
    public sealed class FusionAnimatorParameterStore : IFusionAnimatorParameterSource
    {
        [Serializable]
        private struct ParameterValue
        {
            public bool BoolValue;
            public int IntValue;
            public float FloatValue;
            public Vector2 Vector2Value;
        }

        private readonly Dictionary<string, ParameterValue> _values = new Dictionary<string, ParameterValue>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _triggerPending = new Dictionary<string, bool>(StringComparer.Ordinal);
        private readonly Dictionary<string, bool> _triggerInputState = new Dictionary<string, bool>(StringComparer.Ordinal);

        public void Clear()
        {
            _values.Clear();
            _triggerPending.Clear();
            _triggerInputState.Clear();
        }

        public void SetDefaults(FusionAnimatorGraphAsset graph)
        {
            _values.Clear();
            _triggerPending.Clear();
            _triggerInputState.Clear();
            if (graph == null || graph.Parameters == null)
            {
                return;
            }

            for (int i = 0; i < graph.Parameters.Count; ++i)
            {
                FusionAnimatorParameterDefinition parameter = graph.Parameters[i];
                if (parameter == null || string.IsNullOrWhiteSpace(parameter.Id))
                {
                    continue;
                }

                _values[parameter.Id] = new ParameterValue
                {
                    BoolValue = parameter.DefaultBool,
                    IntValue = parameter.DefaultInt,
                    FloatValue = parameter.DefaultFloat,
                    Vector2Value = parameter.DefaultVector2,
                };
                bool defaultInputState = ResolveDefaultInputState(parameter);
                _triggerPending[parameter.Id] = false;
                _triggerInputState[parameter.Id] = defaultInputState;
            }
        }

        public void SetBool(string parameterId, bool value)
        {
            if (string.IsNullOrWhiteSpace(parameterId))
            {
                return;
            }

            ParameterValue existing = GetOrCreate(parameterId);
            existing.BoolValue = value;
            existing.IntValue = value ? 1 : 0;
            existing.FloatValue = value ? 1.0f : 0.0f;
            if (value == false)
            {
                existing.Vector2Value = Vector2.zero;
            }
            _values[parameterId] = existing;
            UpdateTriggerState(parameterId, value);
        }

        public void SetInt(string parameterId, int value)
        {
            if (string.IsNullOrWhiteSpace(parameterId))
            {
                return;
            }

            ParameterValue existing = GetOrCreate(parameterId);
            existing.IntValue = value;
            existing.BoolValue = value != 0;
            existing.FloatValue = value;
            existing.Vector2Value = new Vector2(value, 0.0f);
            _values[parameterId] = existing;
            UpdateTriggerState(parameterId, value != 0);
        }

        public void SetFloat(string parameterId, float value)
        {
            if (string.IsNullOrWhiteSpace(parameterId))
            {
                return;
            }

            ParameterValue existing = GetOrCreate(parameterId);
            existing.FloatValue = value;
            existing.BoolValue = Mathf.Abs(value) > 0.000001f;
            existing.IntValue = Mathf.RoundToInt(value);
            _values[parameterId] = existing;
            UpdateTriggerState(parameterId, existing.BoolValue);
        }

        public void SetVector2(string parameterId, Vector2 value)
        {
            if (string.IsNullOrWhiteSpace(parameterId))
            {
                return;
            }

            ParameterValue existing = GetOrCreate(parameterId);
            existing.Vector2Value = value;
            existing.FloatValue = value.magnitude;
            existing.IntValue = Mathf.RoundToInt(value.magnitude);
            existing.BoolValue = value.sqrMagnitude > 0.000001f;
            _values[parameterId] = existing;
            UpdateTriggerState(parameterId, existing.BoolValue);
        }

        public bool TryGetBool(string parameterId, out bool value)
        {
            if (_values.TryGetValue(parameterId, out ParameterValue stored))
            {
                value = stored.BoolValue;
                return true;
            }

            value = false;
            return false;
        }

        public bool TryGetInt(string parameterId, out int value)
        {
            if (_values.TryGetValue(parameterId, out ParameterValue stored))
            {
                value = stored.IntValue;
                return true;
            }

            value = 0;
            return false;
        }

        public bool TryGetFloat(string parameterId, out float value)
        {
            if (_values.TryGetValue(parameterId, out ParameterValue stored))
            {
                value = stored.FloatValue;
                return true;
            }

            value = 0.0f;
            return false;
        }

        public bool TryGetVector2(string parameterId, out Vector2 value)
        {
            if (_values.TryGetValue(parameterId, out ParameterValue stored))
            {
                value = stored.Vector2Value;
                return true;
            }

            value = Vector2.zero;
            return false;
        }

        public bool TryPeekTrigger(string parameterId, out bool fired)
        {
            if (_triggerPending.TryGetValue(parameterId, out fired))
            {
                return true;
            }

            fired = false;
            return false;
        }

        public bool TryConsumeTrigger(string parameterId, out bool fired)
        {
            if (_triggerPending.TryGetValue(parameterId, out fired))
            {
                if (fired)
                {
                    _triggerPending[parameterId] = false;
                }

                return true;
            }

            fired = false;
            return false;
        }

        private ParameterValue GetOrCreate(string parameterId)
        {
            if (_values.TryGetValue(parameterId, out ParameterValue existing))
            {
                return existing;
            }

            return default;
        }

        private void UpdateTriggerState(string parameterId, bool isPressed)
        {
            bool previousPressed = false;
            _triggerInputState.TryGetValue(parameterId, out previousPressed);

            if (isPressed && previousPressed == false)
            {
                _triggerPending[parameterId] = true;
            }
            else if (_triggerPending.ContainsKey(parameterId) == false)
            {
                _triggerPending[parameterId] = false;
            }

            _triggerInputState[parameterId] = isPressed;
        }

        private static bool ResolveDefaultInputState(FusionAnimatorParameterDefinition parameter)
        {
            if (parameter == null)
            {
                return false;
            }

            switch (parameter.Type)
            {
                case FusionAnimatorParameterType.Bool:
                case FusionAnimatorParameterType.Trigger:
                    return parameter.DefaultBool;
                case FusionAnimatorParameterType.Int:
                    return parameter.DefaultInt != 0;
                case FusionAnimatorParameterType.Float:
                    return Mathf.Abs(parameter.DefaultFloat) > 0.000001f;
                case FusionAnimatorParameterType.Vector2:
                    return parameter.DefaultVector2.sqrMagnitude > 0.000001f;
                default:
                    return false;
            }
        }
    }
}
