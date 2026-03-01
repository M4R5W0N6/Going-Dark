using System.Collections.Generic;
using UnityEngine;

namespace FusionAnimator.Editor
{
    internal interface IFusionAnimatorGraphConverter
    {
        string Id { get; }
        string DisplayName { get; }
        bool CanConvert(Object source);
        bool TryConvert(Object source, FusionAnimatorGraphAsset target, out string message);
    }

    internal static class FusionAnimatorGraphConverterRegistry
    {
        private static readonly List<IFusionAnimatorGraphConverter> _converters = new List<IFusionAnimatorGraphConverter>
        {
            new UnityToFusionConverter(),
            new FusionAgentToFusionConverter(),
        };

        public static IReadOnlyList<IFusionAnimatorGraphConverter> GetConverters()
        {
            return _converters;
        }
    }
}
