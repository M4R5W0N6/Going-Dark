using System;

namespace FusionAnimator.Editor
{
    internal static class FusionAnimatorEditorSelectionContext
    {
        public static event Action SelectionChanged;

        public static FusionAnimatorGraphAsset Graph { get; private set; }
        public static string SelectedStateId { get; private set; }
        public static string SelectedTransitionId { get; private set; }

        public static void SetSelection(FusionAnimatorGraphAsset graph, string selectedStateId, string selectedTransitionId)
        {
            bool changed = ReferenceEquals(Graph, graph) == false ||
                           string.Equals(SelectedStateId, selectedStateId, StringComparison.Ordinal) == false ||
                           string.Equals(SelectedTransitionId, selectedTransitionId, StringComparison.Ordinal) == false;

            Graph = graph;
            SelectedStateId = selectedStateId;
            SelectedTransitionId = selectedTransitionId;

            if (changed)
            {
                SelectionChanged?.Invoke();
            }
        }
    }
}
