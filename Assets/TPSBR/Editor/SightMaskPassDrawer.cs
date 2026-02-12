namespace TPSBR.EditorTools
{
	using UnityEditor;
	using UnityEditor.Rendering.HighDefinition;
	using UnityEngine;
	using UnityEngine.Rendering;

	[CustomPassDrawer(typeof(TPSBR.SightMaskPass))]
	public sealed class SightMaskPassDrawer : CustomPassDrawer
	{
		protected override PassUIFlag commonPassUIFlags => PassUIFlag.Name;

		private SerializedProperty _lightLayerMask;
		private SerializedProperty _debugDrawToCamera;

		protected override void Initialize(SerializedProperty customPass)
		{
			_lightLayerMask = customPass.FindPropertyRelative("_lightLayerMask");
			_debugDrawToCamera = customPass.FindPropertyRelative("_debugDrawToCamera");
		}

		protected override void DoPassGUI(SerializedProperty customPass, Rect rect)
		{
			if (_debugDrawToCamera != null)
			{
				EditorGUI.PropertyField(rect, _debugDrawToCamera, new GUIContent("Debug (Draw To Camera)"));
				rect.y += EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
			}

			if (_lightLayerMask != null)
			{
				EditorGUI.BeginChangeCheck();
				RenderingLayerMask selectedMask = (RenderingLayerMask)_lightLayerMask.uintValue;
				selectedMask = EditorGUI.RenderingLayerMaskField(rect, "Light Layers", selectedMask);
				if (EditorGUI.EndChangeCheck())
				{
					_lightLayerMask.uintValue = (uint)selectedMask;
				}
			}
		}

		protected override float GetPassHeight(SerializedProperty customPass)
		{
			return (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) * 2.0f;
		}
	}
}
