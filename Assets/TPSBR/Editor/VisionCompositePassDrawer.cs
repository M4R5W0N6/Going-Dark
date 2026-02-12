namespace TPSBR.EditorTools
{
	using UnityEditor;
	using UnityEditor.Rendering.HighDefinition;
	using UnityEngine;

	[CustomPassDrawer(typeof(TPSBR.VisionCompositePass))]
	public sealed class VisionCompositePassDrawer : CustomPassDrawer
	{
		protected override PassUIFlag commonPassUIFlags => PassUIFlag.Name;

		private SerializedProperty _renderMode;

		protected override void Initialize(SerializedProperty customPass)
		{
			_renderMode = customPass.FindPropertyRelative("_renderMode");
		}

		protected override void DoPassGUI(SerializedProperty customPass, Rect rect)
		{
			if (_renderMode != null)
			{
				EditorGUI.PropertyField(rect, _renderMode, new GUIContent("Render Mode"));
			}
		}

		protected override float GetPassHeight(SerializedProperty customPass)
		{
			return EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
		}
	}
}
