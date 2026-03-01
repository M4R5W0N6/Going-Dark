using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FusionAnimator.Editor
{
    [CustomEditor(typeof(FusionAnimatorGraphAsset))]
    public sealed class FusionAnimatorGraphAssetInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            serializedObject.ApplyModifiedProperties();

            GUILayout.Space(8.0f);

            FusionAnimatorGraphAsset graph = (FusionAnimatorGraphAsset)target;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Open Graph Editor"))
                {
                    FusionAnimatorWindow.Open(graph);
                }

                if (GUILayout.Button("Validate"))
                {
                    List<FusionAnimatorValidationIssue> issues = FusionAnimatorValidator.Validate(graph);
                    int errorCount = 0;
                    int warningCount = 0;

                    for (int i = 0, count = issues.Count; i < count; ++i)
                    {
                        FusionAnimatorValidationIssue issue = issues[i];
                        switch (issue.Severity)
                        {
                            case FusionAnimatorValidationSeverity.Error:
                                ++errorCount;
                                break;
                            case FusionAnimatorValidationSeverity.Warning:
                                ++warningCount;
                                break;
                        }
                    }

                    if (errorCount > 0)
                    {
                        Debug.LogError(string.Format("FusionAnimator validation failed for '{0}' with {1} error(s), {2} warning(s).", graph.name, errorCount, warningCount), graph);
                    }
                    else if (warningCount > 0)
                    {
                        Debug.LogWarning(string.Format("FusionAnimator validation for '{0}' has {1} warning(s).", graph.name, warningCount), graph);
                    }
                    else
                    {
                        Debug.Log(string.Format("FusionAnimator graph '{0}' is valid.", graph.name), graph);
                    }
                }
            }
        }
    }
}
