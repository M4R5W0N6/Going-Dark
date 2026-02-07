using UnityEngine;
using Unity.Mathematics;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace FOW
{
    public class RevealerDebug : MonoBehaviour
    {
#if UNITY_EDITOR
        public bool DrawDebugStats = true;
        public bool DrawSegments = false;
        public bool DrawOutline = false;
        public bool DrawForward = false;
        [SerializeField] protected float DrawRayNoise = 0;

        private FogOfWarRevealer _revealer;

        private void OnDrawGizmos()
        {
            if (!Application.isPlaying)
                return;
            if (_revealer == null)
            {
                if (!TryGetComponent<FogOfWarRevealer>(out _revealer))
                    return;
            }
            if (!DrawDebugStats)
                return;
            if (FogOfWarWorld.instance == null)
                return;

            for (int i = 0; i < _revealer.NumberOfPoints; i++)
            {
                DrawString(i.ToString(), GetSegmentEnd(i), Color.white);
                if (DrawDebugStats)
                {
                    //Debug.Log(deg);
                    //Debug.DrawRay(GetEyePosition(), (ViewPoints[i].point - GetEyePosition()) + UnityEngine.Random.insideUnitSphere * DrawRayNoise, Color.blue);
                    if (DrawSegments)
                        Debug.DrawRay(_revealer.GetEyePosition(), (GetSegmentEnd(i) - _revealer.GetEyePosition()) + (float3)UnityEngine.Random.insideUnitSphere * DrawRayNoise, Color.blue, Time.deltaTime);
                    //drawString(i.ToString(), ViewPoints[i].point, Color.white);

                    if (i != 0 && DrawOutline)
                        Debug.DrawLine(GetSegmentEnd(i), GetSegmentEnd(i - 1), Color.yellow);
                    //Debug.DrawLine(ViewPoints[i].point, ViewPoints[i - 1].point, Color.yellow);
                }
            }

            if (DrawForward)
                Debug.DrawLine(_revealer.GetEyePosition(), _revealer.GetEyePosition() + _revealer.ForwardVectorCached * 3, Color.magenta);
        }

        static void DrawString(string text, Vector3 worldPos, Color? colour = null)
        {
            UnityEditor.Handles.BeginGUI();
            if (colour.HasValue) GUI.color = colour.Value;
            var view = UnityEditor.SceneView.currentDrawingSceneView;
            if (!view)
                return;
            Vector3 screenPos = view.camera.WorldToScreenPoint(worldPos);
            Vector2 size = GUI.skin.label.CalcSize(new GUIContent(text));
            GUIStyle guiStyle = new GUIStyle(GUI.skin.label);
            guiStyle.normal.textColor = Color.red;
            GUI.Label(new Rect(screenPos.x - (size.x / 2), -screenPos.y + view.position.height + 4, size.x, size.y), text, guiStyle);
            UnityEditor.Handles.EndGUI();

        }

        float3 GetSegmentEnd(int index)
        {
            //return _revealer.GetEyePosition() + (_revealer.DirFromAngle(_revealer.Directions[index]) * (_revealer.AreHits[index] ? _revealer.Radii[index] : _revealer.GetRayDistance()));
            return _revealer.GetEyePosition() + new float3(_revealer.OutputDirections[index].x, 0, _revealer.OutputDirections[index].y) * (_revealer.OutputDistances[index] <= _revealer.TotalRevealerRadius ? _revealer.OutputDistances[index] : _revealer.TotalRevealerRadius);
        }
#endif
    }

#if UNITY_EDITOR
    [CustomEditor(typeof(RevealerDebug))]
    public class RevealerDebugEditor : Editor
    {
        RaycastRevealer Revealer;
        public override void OnInspectorGUI()
        {
            RevealerDebug stat = (RevealerDebug)target;
            DrawDefaultInspector();
            

            //FogOfWarRevealer rev = stat.GetRevealerComponent();

            if (Revealer == null)
            {
                if (!stat.TryGetComponent<RaycastRevealer>(out Revealer))
                {
                    EditorGUILayout.LabelField($"Revealer component not found.");
                    return;
                }
            }


            EditorGUILayout.LabelField(" ");

            EditorGUILayout.LabelField($"Revealer array position id (can change): {Revealer.RevealerArrayPosition}");
            EditorGUILayout.LabelField($"GPU data position id (cant change): {Revealer.RevealerGPUDataPosition}");

            if (!stat.DrawDebugStats)
                return;

            EditorGUILayout.LabelField(" ");
            if (stat.DrawSegments)
            {
                EditorGUILayout.LabelField($"NUM SEGMENTS: {Revealer.NumberOfPoints}");
                for (int i = 0; i < Revealer.NumberOfPoints; i++)
                {
                    EditorGUILayout.LabelField($"------------- Segment {i} -------------");
                    EditorGUILayout.LabelField($"Angle: {Revealer.ViewPoints[i].Angle}");
                    EditorGUILayout.LabelField($"Direction: {Revealer.OutputDirections[i]}");
                    EditorGUILayout.LabelField($"Radius: {Revealer.OutputDistances[i]}");
                    EditorGUILayout.LabelField($"Did Hit?: {Revealer.OutputDistances[i] <= Revealer.TotalRevealerRadius}");
                }
            }

            EditorGUILayout.LabelField($"HASH BUCKETS:");
            for (int i = 0; i < Revealer.SpatialHashBuckets.Count; i++)
            {
                EditorGUILayout.LabelField(Revealer.SpatialHashBuckets[i].ToString());
            }
            if (Application.isPlaying)
            {
                if (GUILayout.Button("Debug Toggle Static"))
                {
                    Revealer.SetRevealerAsStatic(!Revealer.CurrentlyStaticRevealer);
                }
            }
        }
    }
#endif
}